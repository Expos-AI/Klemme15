// Klemme15: open-source BMW diagnostics
// Copyright (C) 2026 Kindric Castro
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
// Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version. This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
// General Public License for more details. You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later

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

    /// <summary>How long to wait for a module that is fitted (null: each protocol's own default). Tests shorten it.</summary>
    public TimeSpan? ResponseTimeout { get; init; }

    /// <summary>Refuse to connect when the cable reports ignition off (only for cables with a working DSR line).</summary>
    public bool RequireIgnition { get; init; }
}

/// <summary>
/// Talks to the car with Klemme15's own protocol implementation. Needs no BMW software or files.
/// What it can decode is limited by the open database; EDIABAS mode covers the rest.
/// </summary>
public sealed class NativeBackend : IDiagnosticBackend, IModuleDiscovery, IPresenceProbe, IRawRequests, ICheckControlReader, IConnectionWatch
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

    /// <summary>OBD values a module didn't answer, counted; after <see cref="SilentLimit"/> misses they aren't asked again.</summary>
    private readonly Dictionary<(byte Address, byte Pid), int> _silentPids = [];
    private readonly Dictionary<(byte Address, byte Pid), DateTime> _silentSince = [];
    private const int SilentLimit = 2;

    /// <summary>A silenced value is tried again after this long, so a glitch on a long drive doesn't drop it for good.</summary>
    private static readonly TimeSpan SilentRetry = TimeSpan.FromSeconds(30);

    private bool Silenced((byte Address, byte Pid) key)
    {
        if (_silentPids.GetValueOrDefault(key) < SilentLimit) return false;
        if (DateTime.UtcNow - _silentSince.GetValueOrDefault(key) < SilentRetry) return true;
        _silentPids[key] = SilentLimit - 1;   // one more try; a miss silences it again for another 30 s
        return false;
    }

    /// <summary>The PIDs each engine computer says it supports (PIDs 00, 20, 40), or null when it didn't say.</summary>
    private readonly Dictionary<byte, HashSet<byte>?> _supportedPids = [];
    private readonly Dictionary<byte, int> _bitmapTries = [];

    /// <summary>
    /// How long to wait for a module that didn't answer the broadcast. Not every module answers the broadcast: on a real
    /// E90 the junction box and seat module didn't, yet answered directly (the seat module after 80 ms), and with 100 ms
    /// the steering column switches, which other tools reach on the same car, were reported missing. 300 ms leaves
    /// room for a module behind a gateway, at the cost of a few seconds per quick test.
    /// </summary>
    internal static readonly TimeSpan AbsentConfirmTimeout = TimeSpan.FromMilliseconds(300);

    /// <summary>The wait for a module that didn't answer the broadcast: never longer than the normal wait.</summary>
    private TimeSpan AbsentWait => AbsentConfirmTimeout < _options.ProbeTimeout ? AbsentConfirmTimeout : _options.ProbeTimeout;

    public NativeBackend(ITransport transport, NativeBackendOptions? options = null)
    {
        _transport = transport;
        _options = options ?? new NativeBackendOptions();
        _fast = new BmwFastChannel(transport);
        _ds2 = new Ds2Channel(transport);
        _obd = new KLineObdChannel(transport);
        if (_options.ResponseTimeout is { } wait)
        {
            _fast.ResponseTimeout = wait;
            _ds2.ResponseTimeout = wait;
        }
    }

    /// <summary>Raised once, off the bus thread, when the cable itself is gone (unplugged, port lost).</summary>
    public event Action<string>? ConnectionLost;
    private int _lostReported;

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
        catch (Exception e) when (e is not DiagnosticException)
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
        // A real 2006 E90 ignored 0xDF; BMW's own jobs address all modules at 0xEF.
        if (answers.Count == 0)
            answers = _fast.RequestFunctional([Kwp2000.TesterPresent], TimeSpan.FromMilliseconds(700), ct, BmwFastChannel.FunctionalEf);
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
                var wait = _present is not null && !_present.Contains(ecu.Address) ? AbsentWait : _options.ProbeTimeout;
                var r = _fast.Request(ecu.Address, [Kwp2000.ReadEcuIdentification, 0x80], wait, ct);
                if (r is null) return null;
                if (r[0] == 0x7F)   // answers but refuses ident: still present
                    return new EcuIdentity { Raw = new Dictionary<string, string> { ["negative"] = Convert.ToHexString(r) } };
                return ParseKwpIdent(r.Length > 2 ? r[2..] : []);
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
                BmwFastChannel.EnsurePositive(r, Kwp2000.ReadDtcByStatus, $"Reading faults from {ecu.Name}");
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
                if (InfoMemory.Offered(ecu))
                {
                    // Info / shadow memory ($22 2000): a bonus, so a refusal or silence is fine.
                    try { faults.AddRange(InfoMemory.Parse(ecu, _fast.Request(ecu.Address, InfoMemory.Request, TimeSpan.FromMilliseconds(400), ct))); }
                    catch (DiagnosticException) { }
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
                BmwFastChannel.EnsurePositive(_fast.Request(ecu.Address, [Kwp2000.ClearDtc, 0xFF, 0xFF], ct: ct), Kwp2000.ClearDtc, $"Clearing faults in {ecu.Name}");
                if (ecu.IsEngine) _fast.Request(ecu.Address, [0x04], ct: ct);
                ClearInfoMemory(ecu, ct);
                break;
            case BusProtocol.Ds2 when !ecu.IsEngine:
                UseDs2();
                Ds2Channel.EnsureOk(_ds2.Request(ecu.Address, [Ds2.ClearFaultMemory], ct: ct), $"Clearing faults in {ecu.Name}");
                break;
            default:
                UseObd();
                if (!_obd.Request([0x04], ct).Any(p => p.Length > 0 && p[0] == 0x44))
                    throw new DiagnosticException("Clearing the engine codes: the engine computer didn't confirm it. Switch the engine off, leave the ignition on, and try again.");
                break;
        }
        return true;
    }, ct);

    /// <summary>
    /// Clears the Shadow / Info memory the way ISTA does (IS_LOESCHEN, <c>$31 06</c>), but only after the module has
    /// confirmed it has one by answering <c>$22 2000</c>. A module without one is left alone.
    /// </summary>
    private void ClearInfoMemory(EcuDefinition ecu, CancellationToken ct)
    {
        if (!InfoMemory.Offered(ecu)) return;
        var probe = _fast.Request(ecu.Address, InfoMemory.Request, TimeSpan.FromMilliseconds(400), ct);
        if (!InfoMemory.Supported(probe)) return;
        var reply = _fast.Request(ecu.Address, InfoMemory.ClearRequest, ct: ct);
        // Some modules (seen on a real E90: footwell module, junction box, cruise control) keep their info memory and say
        // so with "not supported". That's the module's choice, not a failure: its fault memory was still cleared.
        if (InfoMemory.Declined(reply)) return;
        if (!InfoMemory.Cleared(reply))
            throw new DiagnosticException($"Clearing the background entries in {ecu.Name}: {(reply is { Length: > 2 } && reply[0] == 0x7F ? "the module refused (" + Kwp2000.NegativeResponseText(reply[2]) + ")" : "the module didn't confirm it")}. Its fault memory was cleared.");
    }

    /// <summary>
    /// The cluster's Check Control list: <c>$21 09</c> for what's showing now, <c>$21 0F</c> for the history.
    /// Both raw replies go to the traffic log, so an unexpected layout can be reported and fixed.
    /// </summary>
    public Task<CheckControlReading?> ReadCheckControlAsync(EcuDefinition cluster, CancellationToken ct = default) => OnBus<CheckControlReading?>(() =>
    {
        if (ProtocolFor(cluster) != BusProtocol.BmwFast) return null;
        UseFast();
        var active = _fast.Request(cluster.Address, CheckControlParser.ActiveRequest, ct: ct);
        var ids = CheckControlParser.ParseActive(active);
        if (ids is null) return null;
        byte[]? history = null;
        try { history = _fast.Request(cluster.Address, CheckControlParser.HistoryRequest, ct: ct); }
        catch (DiagnosticException) { /* history is a bonus */ }
        return new CheckControlReading(ids, CheckControlParser.ParseHistory(history), CheckControlParser.Hex(active), history is null ? null : CheckControlParser.Hex(history));
    }, ct);

    /// <summary>One raw request to a BMW-FAST (D-CAN or KWP on K-line) module; the reply as received, or null on no reply.</summary>
    public Task<byte[]?> RequestAsync(EcuDefinition ecu, byte[] request, CancellationToken ct = default) => OnBus<byte[]?>(() =>
    {
        if (ProtocolFor(ecu) != BusProtocol.BmwFast)
            throw new NotSupportedException($"{ecu.Name} uses the older DS2 protocol; module readings for it need EDIABAS mode for now.");
        UseFast();
        return _fast.Request(ecu.Address, request, TimeSpan.FromMilliseconds(400), ct);
    }, ct);

    public IReadOnlyList<LiveParameter> GetLiveParameters(EcuDefinition ecu) =>
        ecu.IsEngine || ecu.Protocol == BusProtocol.Obd2 ? Obd2.AllParameters : [];

    public Task<IReadOnlyList<LiveValue>> ReadLiveAsync(EcuDefinition ecu, IReadOnlyList<LiveParameter> parameters, CancellationToken ct = default) =>
        OnBus<IReadOnlyList<LiveValue>>(() =>
        {
            var values = new List<LiveValue>(parameters.Count);
            var supported = SupportedPids(ecu, ct);
            foreach (var p in parameters)
            {
                ct.ThrowIfCancellationRequested();
                double? value = null;
                string? note = null;
                if (!Obd2.TryGetPid(p, out var pid)) { }
                else if (supported is not null && !supported.Contains(pid.Id))
                    note = "not reported by this engine computer";
                else if (!Silenced((ecu.Address, pid.Id)))
                {
                    value = ObdRequest(ecu, [0x01, pid.Id], ct).Select(r => Obd2.DecodeResponse(r, pid)).FirstOrDefault(v => v is not null);
                    // A value the engine computer never answers (PID 0x42 on an MSV70) would cost a timeout on every update.
                    var key = (ecu.Address, pid.Id);
                    _silentPids[key] = value is null ? _silentPids.GetValueOrDefault(key) + 1 : 0;
                    if (_silentPids[key] == SilentLimit) _silentSince[key] = DateTime.UtcNow;
                }
                values.Add(new LiveValue(p, value, value is null ? note ?? "no data" : null, DateTimeOffset.Now));
            }
            return values;
        }, ct);

    private IEnumerable<byte[]> ObdRequest(EcuDefinition ecu, byte[] request, CancellationToken ct)
    {
        if (_options.Mode == CableMode.DCan)
        {
            UseFast();
            var r = _fast.Request(ecu.Address, request, TimeSpan.FromMilliseconds(300), ct);
            return r is null ? [] : [r];
        }
        UseObd();
        return _obd.Request(request, ct);
    }

    /// <summary>
    /// Asks once which PIDs the engine computer supports (the standard bitmaps at PIDs 00, 20 and 40), so values it
    /// doesn't have are never asked for. On a K-line car each unanswered value costs a timeout of a second or more,
    /// which made the first live readings slow. Null when it doesn't answer the bitmaps: then every value is tried.
    /// </summary>
    private HashSet<byte>? SupportedPids(EcuDefinition ecu, CancellationToken ct)
    {
        if (_supportedPids.TryGetValue(ecu.Address, out var known)) return known;
        var set = new HashSet<byte>();
        var complete = false;
        foreach (var basePid in new byte[] { 0x00, 0x20, 0x40 })
        {
            byte[]? reply;
            try { reply = ObdRequest(ecu, [0x01, basePid], ct).FirstOrDefault(r => r.Length >= 6 && r[0] == 0x41 && r[1] == basePid); }
            catch (TimeoutException) { reply = null; }
            if (reply is null) break;
            foreach (var id in Obd2.DecodeSupported(basePid, reply.AsSpan(2, 4))) set.Add(id);
            // The last bit says whether the next bitmap exists; when it doesn't, the list is complete.
            if (!set.Contains((byte)(basePid + 0x20)) || basePid == 0x40) { complete = true; break; }
        }
        if (complete && set.Count > 0) return _supportedPids[ecu.Address] = set;
        // A bitmap went unanswered (a dropped reply on a busy line): don't trust a partial list. Ask everything this time,
        // try the bitmaps again next time, and give up on them after a few tries.
        var tries = _bitmapTries[ecu.Address] = _bitmapTries.GetValueOrDefault(ecu.Address) + 1;
        if (tries >= 3) _supportedPids[ecu.Address] = null;
        return null;
    }

    /// <summary>Asks each address for its identification with a short timeout; only modules that answer are returned.</summary>
    public async Task<IReadOnlyList<DiscoveredModule>> DiscoverAsync(IReadOnlyList<byte> addresses, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var found = new List<DiscoveredModule>();
        var timeout = AbsentWait;
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
                        : ParseKwpIdent(r.Length > 2 ? r[2..] : []);
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
    /// BMW's KWP2000 identification ($1A 80) on D-CAN cars, as confirmed on a real E90's modules:
    /// bytes 0-5 the part number as packed digits (00 00 07 56 16 84 = 7561684), then the hardware, coding,
    /// diagnostic and variant indexes, two characters, the production date as packed digits (20 06 04 18 =
    /// 2006-04-18) and the supplier number. Anything that doesn't fit falls back to <see cref="ParseIdent"/>.
    /// </summary>
    internal static EcuIdentity ParseKwpIdent(byte[] data)
    {
        if (data.Length < 17) return ParseIdent(data);
        var digits = Convert.ToHexString(data, 0, 6);
        if (!digits.All(char.IsAsciiDigit)) return ParseIdent(data);
        var part = digits.TrimStart('0');
        if (part.Length is < 6 or > 8) return ParseIdent(data);

        string? date = null;
        var d = Convert.ToHexString(data, 12, 4);
        if (d.All(char.IsAsciiDigit) && int.Parse(d[..4]) is >= 1990 and <= 2035 && int.Parse(d[4..6]) is >= 1 and <= 12 && int.Parse(d[6..8]) is >= 1 and <= 31)
            date = $"{d[..4]}-{d[4..6]}-{d[6..8]}";
        var basic = ParseIdent(data);
        var raw = new Dictionary<string, string>(basic.Raw)
        {
            ["variantIndex"] = data[9].ToString("X2"),
        };
        return new EcuIdentity
        {
            PartNumber = part,
            HardwareVersion = data[6].ToString("X2"),
            CodingIndex = data[7].ToString("X2"),
            DiagnosticIndex = data[8].ToString("X2"),
            ProductionDate = date,
            Supplier = data[16].ToString("X2"),
            Raw = raw,
        };
    }

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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException || e is InvalidOperationException or TimeoutException && !TransportOpen())
        {
            // The cable is gone. Release the port so a reconnect can open it again, and tell the session once, so every
            // page stops instead of each one finding out by itself.
            _line = LineState.Closed;
            try { _transport.Close(); } catch (Exception) { /* already gone */ }
            var message = $"The cable stopped responding ({e.Message}). It may have been unplugged: check the plug at both ends, then connect again.";
            if (Interlocked.Exchange(ref _lostReported, 1) == 0 && ConnectionLost is { } lost) _ = Task.Run(() => lost(message));
            throw new DiagnosticException(message, e);
        }
        catch (TimeoutException e)
        {
            _line = LineState.Closed;
            throw new DiagnosticException($"Cable error: {e.Message}. Is the cable still plugged in?", e);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or FormatException or OverflowException or KeyNotFoundException or InvalidOperationException)
        {
            // The last line of defence against a damaged or unexpected answer: a clear message, never a crash.
            _line = LineState.Closed;
            throw new DiagnosticException($"Klemme15 couldn't read the module's answer: it was damaged or in a layout it doesn't know ({e.GetType().Name}). Try again; the traffic log (Log page) has the bytes.", e);
        }
        finally
        {
            _bus.Release();
        }
    }

    private Task OnBus(Func<bool> work, CancellationToken ct) => OnBus<bool>(work, ct);

    private bool TransportOpen()
    {
        try { return _transport.IsOpen; } catch (Exception) { return false; }
    }
}
