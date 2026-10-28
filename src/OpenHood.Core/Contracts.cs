namespace OpenHood.Core;

[Flags]
public enum BackendCapabilities
{
    None = 0,
    Identify = 1,
    ReadFaults = 2,
    ClearFaults = 4,
    LiveData = 8,
    ReadVin = 16,
    /// <summary>Can run arbitrary EDIABAS jobs (expert mode).</summary>
    Jobs = 32,
}

/// <summary>
/// A way of talking to the car. Implementations: the native protocol stack (no BMW files needed)
/// and the EDIABAS backend (runs the user's own SGBD files). Every call is serialized internally
/// because there is only one diagnostic bus.
/// </summary>
public interface IDiagnosticBackend : IAsyncDisposable
{
    string Name { get; }
    BackendCapabilities Capabilities { get; }

    /// <summary>Ignition state from the cable's DSR line, if the cable reports it.</summary>
    bool? IgnitionOn { get; }

    Task ConnectAsync(CancellationToken ct = default);

    Task<string?> ReadVinAsync(CancellationToken ct = default);

    /// <summary>Returns null when the ECU does not answer (not fitted or asleep).</summary>
    Task<EcuIdentity?> IdentifyAsync(EcuDefinition ecu, CancellationToken ct = default);

    Task<IReadOnlyList<Fault>> ReadFaultsAsync(EcuDefinition ecu, CancellationToken ct = default);

    Task ClearFaultsAsync(EcuDefinition ecu, CancellationToken ct = default);

    /// <summary>Live parameters this backend can read from the given ECU.</summary>
    IReadOnlyList<LiveParameter> GetLiveParameters(EcuDefinition ecu);

    Task<IReadOnlyList<LiveValue>> ReadLiveAsync(EcuDefinition ecu, IReadOnlyList<LiveParameter> parameters, CancellationToken ct = default);
}

/// <summary>A module that answered at an address, found by probing the bus.</summary>
public sealed record DiscoveredModule(byte Address, EcuIdentity Identity)
{
    public string AddressHex => $"0x{Address:X2}";
}

/// <summary>Probes diagnostic addresses to find modules the database doesn't know about yet. Read-only.</summary>
public interface IModuleDiscovery
{
    Task<IReadOnlyList<DiscoveredModule>> DiscoverAsync(IReadOnlyList<byte> addresses, IProgress<int>? progress = null, CancellationToken ct = default);
}

/// <summary>Expert mode: run any job from an SGBD, like Tool32.</summary>
public interface IJobRunner
{
    Task<IReadOnlyList<string>> ListJobsAsync(string sgbd, CancellationToken ct = default);

    /// <summary>Returns one dictionary per result set (name -> formatted value).</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> RunJobAsync(string sgbd, string job, string arguments, CancellationToken ct = default);
}

/// <summary>Looks up human-readable fault descriptions.</summary>
public interface IFaultCodeDatabase
{
    /// <summary>OBD-II description for codes like "P0171".</summary>
    string? DescribeObd(string code);

    /// <summary>BMW description for a hex code from a given ECU (by definition id, e.g. "DME").</summary>
    string? DescribeBmw(string ecuId, string code);
}

/// <summary>Raised when the diagnostic link fails in a way the user must fix (cable, ignition, port).</summary>
public sealed class DiagnosticException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Raw traffic log so every session can be inspected and attached to bug reports.</summary>
public sealed class TrafficLog
{
    private readonly object _lock = new();
    private readonly List<TrafficEntry> _entries = [];
    private readonly int _capacity;

    public TrafficLog(int capacity = 20_000) => _capacity = capacity;

    public event Action<TrafficEntry>? EntryAdded;

    public void Add(TrafficDirection direction, ReadOnlySpan<byte> data, string? note = null)
    {
        var entry = new TrafficEntry(DateTimeOffset.Now, direction, data.ToArray(), note);
        lock (_lock)
        {
            if (_entries.Count >= _capacity) _entries.RemoveRange(0, _capacity / 10);
            _entries.Add(entry);
        }
        EntryAdded?.Invoke(entry);
    }

    public void Note(string text) => Add(TrafficDirection.Info, ReadOnlySpan<byte>.Empty, text);

    public IReadOnlyList<TrafficEntry> Snapshot()
    {
        lock (_lock) return _entries.ToArray();
    }

    public string Export() => string.Join(Environment.NewLine, Snapshot().Select(e => e.ToString()));
}

public enum TrafficDirection { Tx, Rx, Info }

public sealed record TrafficEntry(DateTimeOffset Time, TrafficDirection Direction, byte[] Data, string? Note)
{
    public override string ToString()
    {
        var hex = Data.Length > 0 ? Convert.ToHexString(Data).Chunk(2).Select(c => new string(c)).Aggregate((a, b) => a + " " + b) : "";
        var tag = Direction switch { TrafficDirection.Tx => "TX", TrafficDirection.Rx => "RX", _ => "--" };
        return $"{Time:HH:mm:ss.fff} {tag} {hex}{(Note is null ? "" : (hex.Length > 0 ? "  " : "") + Note)}";
    }
}
