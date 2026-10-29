namespace Klemme15.Core;

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

/// <summary>Everything the database knows about one code.</summary>
public sealed record CodeInfo(string Text, string? Hint = null, string? Confidence = null, IReadOnlyList<string>? Sources = null, string? Component = null, bool UsuallyHarmless = false);

/// <summary>Richer lookup than <see cref="IFaultCodeDatabase"/>: tips, confidence and sources.</summary>
public interface IFaultCodeDetails
{
    CodeInfo? LookupBmw(string ecuId, string code);
}

/// <summary>One code's entry in an official (BMW-authored) fault dictionary.</summary>
/// <param name="Text">BMW's text as stored, usually German.</param>
/// <param name="English">BMW's own English text, where BMW translated it.</param>
public sealed record OfficialEntry(string Text, string? English);

/// <summary>
/// A per-variant fault dictionary the user supplies: their own BMW files, or a fault database they chose to download.
/// Klemme15 ships none of this data itself.
/// </summary>
public interface IOfficialFaultSource
{
    /// <summary>Shown to the user next to each text, e.g. "your BMW files".</summary>
    string Name { get; }

    bool HasVariant(string variant);
    OfficialEntry? Lookup(string variant, int code);

    /// <summary>Engine codes the variant is for, as BMW writes them (e.g. "N52", "N52KP"), empty if unknown.</summary>
    IReadOnlyCollection<string> EnginesOf(string variant);

    /// <summary>BMW hardware part numbers (7 digits) known for the variant, empty if unknown.</summary>
    IReadOnlyCollection<string> PartNumbersOf(string variant);
}

/// <summary>One status reading a module offers (an EDIABAS STATUS_ job that needs no arguments).</summary>
/// <param name="Job">BMW's job name, e.g. STATUS_L_SONDE.</param>
/// <param name="Comment">BMW's description of the job (usually German), if any.</param>
public sealed record ModuleReading(string Job, string? Comment);

/// <summary>One value a reading returned.</summary>
/// <param name="Name">BMW's result name, e.g. STAT_LAMBDA_SONDE_WERT.</param>
/// <param name="Comment">BMW's description of the result, if any.</param>
public sealed record ReadingValue(string Name, string Text, double? Number, string? Unit, string? Comment);

/// <summary>
/// Module-specific readings (sensor values, switch states, adaptation values) computed by BMW's own job scripts.
/// Only backends that can run those scripts (EDIABAS mode with the user's files, or the demo) offer this.
/// </summary>
public interface IModuleReadings
{
    /// <summary>The module variant the readings come from (e.g. "MSV70").</summary>
    string? VariantOf(EcuDefinition ecu);

    Task<IReadOnlyList<ModuleReading>> ListReadingsAsync(EcuDefinition ecu, CancellationToken ct = default);
    Task<IReadOnlyList<ReadingValue>> ReadAsync(EcuDefinition ecu, string job, CancellationToken ct = default);

    /// <summary>Live-data source string for pinning one value to the Live data page.</summary>
    string PinSource(EcuDefinition ecu, string job, string result);

    /// <summary>False for modules this provider has no readings for (they are hidden on the Readings page).</summary>
    bool Supports(EcuDefinition ecu) => true;
}

/// <summary>One value an engine computer can report on request, from BMW's measurement table (MESSWERTETAB).</summary>
/// <param name="Id">Common identifier to ask for (e.g. 0x4200).</param>
/// <param name="Result">BMW's result name, e.g. STAT_ANSAUGLUFTTEMPERATUR_WERT.</param>
/// <param name="DataType">"unsigned char", "signed integer", ... or "-" when the size comes from the reply.</param>
/// <param name="LowHigh">True when BMW's table marks the value as low byte first.</param>
public sealed record MeasurementDef(ushort Id, string Result, string? Comment, string? Unit, string DataType, bool LowHigh, double Mul, double Div, double Add);

/// <summary>Per-variant measurement tables the user downloaded (see FAULT-DATA.md). Klemme15 ships none.</summary>
public interface IMeasurementTables
{
    bool Has(string variant);
    IReadOnlyList<MeasurementDef> MeasurementsOf(string variant);
}

/// <summary>Backends that can send one raw diagnostic request to a module and return its reply (or null on no reply).</summary>
/// <summary>Readings that can fetch many values in one go (much faster than one request per value).</summary>
public interface IBatchReadings
{
    /// <summary>Reads several readings; ones that fail are missing from the result.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<ReadingValue>>> ReadManyAsync(EcuDefinition ecu, IReadOnlyList<string> jobs, CancellationToken ct = default);
}

public interface IRawRequests
{
    Task<byte[]?> RequestAsync(EcuDefinition ecu, byte[] request, CancellationToken ct = default);
}

/// <summary>Fills in a fault's meaning using everything known about the module it came from.</summary>
public interface IFaultDescriber
{
    Fault Describe(Fault fault, EcuDefinition ecu, EcuIdentity? identity);
}

/// <summary>Backends that can find which modules are present in one go (e.g. a functional broadcast).</summary>
public interface IPresenceProbe
{
    /// <summary>Returns the addresses that answered, or null if the probe isn't supported on this car.</summary>
    Task<IReadOnlySet<byte>?> ProbePresentAsync(CancellationToken ct = default);
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
