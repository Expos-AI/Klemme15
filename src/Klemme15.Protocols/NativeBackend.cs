using System.Text;
using Klemme15.Core;
using Klemme15.Protocols.Channels;
using Klemme15.Protocols.Framing;
using Klemme15.Protocols.Transport;

namespace Klemme15.Protocols;

public enum CableMode
{
    /// <summary>E46 and older: K-line (DS2 + OBD-II). Cable switch in K-line position.</summary>
    KLine,
    /// <summary>E9x and other 2007+ E-series: D-CAN. Cable switch in D-CAN position.</summary>
    DCan,
}

public sealed record NativeBackendOptions
{
    public CableMode Mode { get; init; } = CableMode.DCan;

    /// <summary>How long to wait for a module that may not be fitted. Short = fast scans.</summary>
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Refuse to connect when the cable reports ignition off (only for cables with a working DSR line).</summary>
    public bool RequireIgnition { get; init; }
}

/// <summary>
/// Talks to the car with Klemme15's own protocol implementation. Needs no BMW software or files.
/// What it can decode is limited by the open database; EDIABAS mode covers the rest.
/// </summary>
public sealed class NativeBackend : IDiagnosticBackend, IModuleDiscovery, IPresenceProbe
{
    private readonly ITransport _transport;
    private readonly NativeBackendOptions _options;
    private readonly SemaphoreSlim _bus = new(1, 1);
    private readonly BmwFastChannel _fast;
    private readonly Ds2Channel _ds2;
    private readonly KLineObdChannel _obd;
    private enum LineState { Closed, BmwFast, Ds2, Obd }
    private LineState _line = LineState.Closed;
    private IReadOnlySet<byte>? _present;

    /// <summary>How long to wait for a module the broadcast says isn't there (one quick confirmation).</summary>
    private static readonly TimeSpan AbsentConfirmTimeout = TimeSpan.FromMilliseconds(100);

    public NativeBackend(ITransport transport, NativeBackendOptions? options = null)
    {
        _transport = transport;
        _options = options ?? new NativeBackendOptions();
        _fast = new BmwFastChannel(transport);
        _ds2 = new Ds2Channel(transport);
        _obd = new KLineObdChannel(transport);
    }

    public string Name => $"Klemme15 native ({(_options.Mode == CableMode.DCan ? "D-CAN" : "K-line")})";

    public BackendCapabilities Capabilities =>
        BackendCapabilities.Identify | BackendCapabilities.ReadFaults | BackendCapabilities.ClearFaults |
        BackendCapabilities.LiveData | BackendCapabilities.ReadVin;

    public bool? IgnitionOn => _transport.IsOpen ? _transport.IgnitionOn : null;

    public Task ConnectAsync(CancellationToken ct = default) => OnBus(() =>
    {
        try
        {
            if (!_transport.IsOpen) _transport.Open();
        }
        catch (Exception e)
        {
            throw new DiagnosticException($"Could not open {_transport.Name}: {e.Message}", e);
        }
        if (_options.RequireIgnition && _transport.IgnitionOn == false)
            throw new DiagnosticException("Ignition appears to be OFF (cable reports no terminal 15). Turn the key to position 2 and try again.");
        if (_options.Mode == CableMode.DCan) UseFast();
        else UseDs2();
        return true;
    }, ct);

    public Task<string?> ReadVinAsync(CancellationToken ct = default) => OnBus(() =>
    {
        if (_options.Mode == CableMode.DCan)
        {
            UseFast();
            // KWP2000 readEcuIdentification option 0x90 = VIN, asked of the engine ECU.
            var r = _fast.Request(0x12, [Kwp2000.ReadEcuIdentification, 0x90], ct: ct);
            if (r is { Length: >= 19 } && r[0] == 0x5A && r[1] == 0x90)
                return Encoding.ASCII.GetString(r, 2, 17);
            var obd = _fast.Request(0x12, [0x09, 0x02], ct: ct);
            return obd is null ? null : Obd2.DecodeVin([obd]);
        }
        UseObd();
        return Obd2.DecodeVin(_obd.Request([0x09, 0x02], ct));
    }, ct);

    /// <summary>
    /// D-CAN only: one functional identification request; every fitted module answers with its own address.
    /// Lets the quick test skip absent option modules fast. Null when nothing answered (not supported on this car).
    /// </summary>
    public Task<IReadOnlySet<byte>?> ProbePresentAsync(CancellationToken ct = default) => OnBus<IReadOnlySet<byte>?>(() =>
    {
        _present = null;
        if (_options.Mode != CableMode.DCan) return null;
        UseFast();
        var answers = _fast.RequestFunctional([Kwp2000.TesterPresent], TimeSpan.FromMilliseconds(700), ct);
        if (answers.Count == 0) return null;
        _present = answers.Select(a => a.Source).ToHashSet();
        return _present;
    }, ct);

    public Task<EcuIdentity?> IdentifyAsync(EcuDefinition ecu, CancellationToken ct = default) => OnBus(() =>
    {
        switch (ProtocolFor(ecu))
        {
            case BusProtocol.BmwFast:
            {
                UseFast();
                var wait = _present is not null && !_present.Contains(ecu.Address) ? AbsentConfirmTimeout : _options.ProbeTimeout;
                var r = _fast.Request(ecu.Address, [Kwp2000.ReadEcuIdentification, 0x80], wait, ct);
                if (r is null) return null;
                if (r[0] == 0x7F)   // answers but refuses ident: still present
                    return new EcuIdentity { Raw = new Dictionary<string, string> { ["negative"] = Convert.ToHexString(r) } };
                return ParseIdent(r.Length > 2 ? r[2..] : []);
            }
            case BusProtocol.Ds2:
            {
                UseDs2();
                var r = _ds2.Request(ecu.Address, [Ds2.Identify], _options.ProbeTimeout, ct);
                if (r is null) return null;
                return ParseIdent(r.Data.Length > 1 ? r.Data[1..] : []);
            }
            default:
            {
                UseObd();
                var r = _obd.Request([0x01, 0x00], ct);
                return r.Count == 0 ? null : new EcuIdentity
                {
                    Variant = "OBD-II",
                    Raw = new Dictionary<string, string> { ["protocol"] = _obd.Protocol.ToString() },
                };
            }
        }
    }, ct);

    public Task<IReadOnlyList<Fault>> ReadFaultsAsync(EcuDefinition ecu, CancellationToken ct = default) => OnBus<IReadOnlyList<Fault>>(() =>
    {
        var faults = new List<Fault>();
        switch (ProtocolFor(ecu))
        {
            case BusProtocol.BmwFast:
            {
                UseFast();
                var r = _fast.Request(ecu.Address, [Kwp2000.ReadDtcByStatus, 0x02, 0xFF, 0xFF], ct: ct);
                BmwFastChannel.EnsurePositive(r, Kwp2000.ReadDtcByStatus, $"Read faults {ecu.Id}");
                var count = r![1];
                for (int i = 2, n = 0; i + 2 < r.Length && n < count; i += 3, n++)
                {
                    var status = r[i + 2];
                    faults.Add(new Fault
                    {
                        EcuId = ecu.Id,
                        Code = $"{r[i]:X2}{r[i + 1]:X2}",
                        Source = FaultSource.Bmw,
                        State = Kwp2000.StateFromStatus(status),
                        Details = new Dictionary<string, string>
                        {
                            ["status"] = $"0x{status:X2}",
                            ["symptom"] = $"0x{status & 0x1F:X2}",
                            ["warning lamp"] = (status & 0x80) != 0 ? "requested" : "off",
                        },
                    });
                }
                if (ecu.IsEngine)
                {
                    var obd = _fast.Request(ecu.Address, [0x03], ct: ct);
                    if (obd is not null) faults.AddRange(ObdFaults(ecu, Obd2.DecodeDtcs(obd, hasCountByte: true)));
                }
                break;
            }
            case BusProtocol.Ds2 when !ecu.IsEngine:
                throw new NotSupportedException(
                    "Native decoding of this module's DS2 fault memory is not in the database yet. Use EDIABAS mode, or contribute a decoder.");
            default:
            {
                UseObd();
                foreach (var p in _obd.Request([0x03], ct))
                    faults.AddRange(ObdFaults(ecu, Obd2.DecodeDtcs(p, hasCountByte: false)));
                break;
            }
        }
        return faults.DistinctBy(f => (f.Source, f.Code)).ToList();
    }, ct);

    public Task ClearFaultsAsync(EcuDefinition ecu, CancellationToken ct = default) => OnBus(() =>
    {
        switch (ProtocolFor(ecu))
        {
            case BusProtocol.BmwFast:
                UseFast();
                BmwFastChannel.EnsurePositive(_fast.Request(ecu.Address, [Kwp2000.ClearDtc, 0xFF, 0xFF], ct: ct), Kwp2000.ClearDtc, $"Clear faults {ecu.Id}");
                if (ecu.IsEngine) _fast.Request(ecu.Address, [0x04], ct: ct);
                break;
            case BusProtocol.Ds2 when !ecu.IsEngine:
                UseDs2();
                Ds2Channel.EnsureOk(_ds2.Request(ecu.Address, [Ds2.ClearFaultMemory], ct: ct), $"Clear faults {ecu.Id}");
                break;
            default:
                UseObd();
                if (!_obd.Request([0x04], ct).Any(p => p.Length > 0 && p[0] == 0x44))
                    throw new DiagnosticException("Clear OBD-II codes: not confirmed by ECU (engine must be off, ignition on)");
                break;
        }
        return true;
    }, ct);

    public IReadOnlyList<LiveParameter> GetLiveParameters(EcuDefinition ecu) =>
        ecu.IsEngine || ecu.Protocol == BusProtocol.Obd2 ? Obd2.AllParameters : [];

    public Task<IReadOnlyList<LiveValue>> ReadLiveAsync(EcuDefinition ecu, IReadOnlyList<LiveParameter> parameters, CancellationToken ct = default) =>
        OnBus<IReadOnlyList<LiveValue>>(() =>
        {
            var values = new List<LiveValue>(parameters.Count);
            foreach (var p in parameters)
            {
                ct.ThrowIfCancellationRequested();
                double? value = null;
                if (Obd2.TryGetPid(p, out var pid))
                {
                    IEnumerable<byte[]> responses;
                    if (_options.Mode == CableMode.DCan)
                    {
                        UseFast();
                        var r = _fast.Request(ecu.Address, [0x01, pid.Id], TimeSpan.FromMilliseconds(300), ct);
                        responses = r is null ? [] : [r];
                    }
                    else
                    {
                        UseObd();
                        responses = _obd.Request([0x01, pid.Id], ct);
                    }
                    value = responses.Select(r => Obd2.DecodeResponse(r, pid)).FirstOrDefault(v => v is not null);
                }
                values.Add(new LiveValue(p, value, value is null ? "no data" : null, DateTimeOffset.Now));
            }
            return values;
        }, ct);

    /// <summary>Asks each address for its identification with a short timeout; only modules that answer are returned.</summary>
    public async Task<IReadOnlyList<DiscoveredModule>> DiscoverAsync(IReadOnlyList<byte> addresses, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var found = new List<DiscoveredModule>();
        var timeout = TimeSpan.FromMilliseconds(150);
        for (var i = 0; i < addresses.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var address = addresses[i];
            var identity = await OnBus<EcuIdentity?>(() =>
            {
                if (_options.Mode == CableMode.DCan)
                {
                    UseFast();
                    var r = _fast.Request(address, [Kwp2000.ReadEcuIdentification, 0x80], timeout, ct);
                    if (r is null) return null;
                    return r[0] == 0x7F
                        ? new EcuIdentity { Raw = new Dictionary<string, string> { ["negative"] = Convert.ToHexString(r) } }
                        : ParseIdent(r.Length > 2 ? r[2..] : []);
                }
                UseDs2();
                var d = _ds2.Request(address, [Ds2.Identify], timeout, ct);
                return d is null ? null : ParseIdent(d.Data.Length > 1 ? d.Data[1..] : []);
            }, ct);
            if (identity is not null) found.Add(new DiscoveredModule(address, identity));
            progress?.Report(i + 1);
        }
        return found;
    }

    public async ValueTask DisposeAsync()
    {
        // Let an in-flight request finish before closing the port underneath it.
        await _bus.WaitAsync().ConfigureAwait(false);
        try
        {
            try { _transport.Close(); } catch { /* already gone */ }
            _transport.Dispose();
            _line = LineState.Closed;
        }
        finally { _bus.Release(); }
    }

    // ---------------------------------------------------------------- helpers

    private BusProtocol ProtocolFor(EcuDefinition ecu)
    {
        // An ECU defined for one cable mode may be reached by OBD-II in the other.
        if (_options.Mode == CableMode.DCan) return BusProtocol.BmwFast;
        return ecu.Protocol == BusProtocol.BmwFast ? BusProtocol.Obd2 : ecu.Protocol;
    }

    private void UseFast()
    {
        if (_line == LineState.BmwFast) return;
        _fast.Open();
        _line = LineState.BmwFast;
    }

    private void UseDs2()
    {
        if (_line == LineState.Ds2) return;
        _ds2.Open();
        _obd.Invalidate();
        _line = LineState.Ds2;
    }

    private void UseObd()
    {
        if (_line != LineState.Obd) _obd.Invalidate();
        _line = LineState.Obd;
    }

    private static IEnumerable<Fault> ObdFaults(EcuDefinition ecu, IEnumerable<string> codes) =>
        codes.Select(c => new Fault { EcuId = ecu.Id, Code = c, Source = FaultSource.Obd2, State = FaultState.Stored });

    /// <summary>
    /// Ident layouts differ per ECU, so keep everything raw and pull out the obvious bits:
    /// the BMW part number is the first run of 7 digits.
    /// </summary>
    internal static EcuIdentity ParseIdent(byte[] data)
    {
        var ascii = new string(data.Select(b => b is >= 0x20 and < 0x7F ? (char)b : '·').ToArray());
        string? part = null;
        for (var i = 0; i + 7 <= ascii.Length; i++)
        {
            var run = ascii.Substring(i, 7);
            if (run.All(char.IsDigit)) { part = run; break; }
        }
        return new EcuIdentity
        {
            PartNumber = part,
            Raw = new Dictionary<string, string>
            {
                ["hex"] = Convert.ToHexString(data),
                ["text"] = ascii,
            },
        };
    }

    private async Task<T> OnBus<T>(Func<T> work, CancellationToken ct)
    {
        await _bus.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(work, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException)
        {
            _line = LineState.Closed;
            throw new DiagnosticException($"Cable error: {e.Message}. Is the cable still plugged in?", e);
        }
        finally
        {
            _bus.Release();
        }
    }

    private Task OnBus(Func<bool> work, CancellationToken ct) => OnBus<bool>(work, ct);
}
