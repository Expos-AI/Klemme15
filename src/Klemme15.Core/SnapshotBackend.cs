using System.Diagnostics;

namespace Klemme15.Core;

/// <summary>
/// Stands in for the car when a snapshot is opened: answers identification, fault and live-data questions from what was
/// recorded, and refuses everything that would change a car. Live data replays the snapshot's last minute in a loop.
/// </summary>
public sealed class SnapshotBackend : IDiagnosticBackend
{
    public const string ReadOnlyMessage = "This is a snapshot: nothing is connected, so nothing can be changed. Open it on the car it came from to do that.";

    private readonly CarSnapshot _snapshot;
    private readonly Dictionary<string, EcuScanResult> _ecus;
    private readonly DataLog _live;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public SnapshotBackend(CarSnapshot snapshot)
    {
        _snapshot = snapshot;
        _ecus = snapshot.Scan.Ecus.GroupBy(e => e.Ecu.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _live = snapshot.HasLive ? DataLog.Parse(snapshot.LiveCsv) : new DataLog([], []);
        Parameters = _live.Channels.Where(c => c.HasData).Select((c, i) => new LiveParameter
        {
            Id = IdFor(c.Name, i),
            Name = c.Name,
            Unit = c.Unit,
            Source = "snapshot:" + i,
        }).ToList();
    }

    public CarSnapshot Snapshot => _snapshot;
    public string Name => "Snapshot";
    public BackendCapabilities Capabilities => BackendCapabilities.Identify | BackendCapabilities.ReadFaults | (_live.Count > 0 ? BackendCapabilities.LiveData : 0);
    public bool? IgnitionOn => _snapshot.Scan.IgnitionOn;

    /// <summary>The recorded live values, with the ids Klemme15's engine signals use where the names match.</summary>
    public IReadOnlyList<LiveParameter> Parameters { get; }

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<string?> ReadVinAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);

    public Task<EcuIdentity?> IdentifyAsync(EcuDefinition ecu, CancellationToken ct = default) =>
        Task.FromResult(_ecus.TryGetValue(ecu.Id, out var e) ? e.Identity : null);

    public Task<IReadOnlyList<Fault>> ReadFaultsAsync(EcuDefinition ecu, CancellationToken ct = default) =>
        _ecus.TryGetValue(ecu.Id, out var e) && e.Status is EcuScanStatus.Ok or EcuScanStatus.Faults
            ? Task.FromResult(e.Faults)
            : throw new DiagnosticException($"{ecu.Name} didn't answer when the snapshot was taken.");

    public Task ClearFaultsAsync(EcuDefinition ecu, CancellationToken ct = default) => throw new DiagnosticException(ReadOnlyMessage);

    public IReadOnlyList<LiveParameter> GetLiveParameters(EcuDefinition ecu) => ecu.IsEngine ? Parameters : [];

    /// <summary>The replay position: wall-clock time since opening, looped over the recording.</summary>
    public int ReplayIndex
    {
        get
        {
            if (_live.Count == 0) return -1;
            var length = Math.Max(_live.Duration.TotalSeconds, 0.5);
            var t = TimeSpan.FromSeconds(_clock.Elapsed.TotalSeconds % length);
            return _live.SampleAt(t);
        }
    }

    public Task<IReadOnlyList<LiveValue>> ReadLiveAsync(EcuDefinition ecu, IReadOnlyList<LiveParameter> parameters, CancellationToken ct = default)
    {
        var i = ReplayIndex;
        var now = DateTimeOffset.Now;
        IReadOnlyList<LiveValue> values = parameters.Select(p =>
        {
            var index = Parameters.ToList().FindIndex(x => x.Id == p.Id);
            var v = i >= 0 && index >= 0 ? _live.Channels.Where(c => c.HasData).ElementAt(index).Samples[i] : null;
            return new LiveValue(p, v, null, now);
        }).ToList();
        return Task.FromResult(values);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>"Engine speed" → "rpm", so the guided tests' signal names line up; otherwise a slug of the name.</summary>
    private static string IdFor(string name, int i)
    {
        var n = name.ToLowerInvariant();
        return n switch
        {
            "engine speed" or "rpm" => "rpm",
            "coolant" or "coolant temperature" => "coolant",
            "battery voltage" or "voltage" or "control module voltage" => "voltage",
            _ => "snap" + i,
        };
    }
}
