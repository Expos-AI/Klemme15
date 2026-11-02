using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace Klemme15.Core.GhostHunter;

/// <summary>Why a capture was taken: written into the snapshot's manifest.</summary>
public sealed record TriggerEvent(string RuleId, string Title, string Rule, DateTimeOffset At, IReadOnlyDictionary<string, double> ValuesAtTrigger,
    bool Partial = false);

/// <summary>A finished capture: the frames from Before the trigger to After it (or to when watching stopped).</summary>
public sealed record TriggerCapture(TriggerEvent Event, IReadOnlyList<DatalogFrame> Frames)
{
    public TimeSpan Length => Frames.Count < 2 ? TimeSpan.Zero : Frames[^1].Time - Frames[0].Time;

    /// <summary>The frames as CSV for the Analyse page: seconds from the first frame, one column per value with its unit.</summary>
    public string ToCsv()
    {
        if (Frames.Count == 0) return "";
        var columns = Frames.SelectMany(f => f.Values.Keys).Distinct().OrderBy(k => SignalLabels.Order(k)).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("time," + string.Join(",", columns.Select(c => $"\"{SignalLabels.Header(c)}\"")));
        var t0 = Frames[0].Time;
        foreach (var f in Frames)
            sb.AppendLine((f.Time - t0).TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + "," +
                string.Join(",", columns.Select(c => f.Values.TryGetValue(c, out var v) ? v.ToString(CultureInfo.InvariantCulture) : "")));
        return sb.ToString();
    }

    /// <summary>A snapshot of the car with this capture's live data and the reason it was taken.</summary>
    public CarSnapshot ToSnapshot(CarSnapshot baseSnapshot) => baseSnapshot with
    {
        LiveCsv = ToCsv(),
        Manifest = baseSnapshot.Manifest with
        {
            Created = Event.At,
            Trigger = Event,
            Note = $"Ghost Hunter caught: {Event.Title} ({Event.Rule}) at {Event.At.LocalDateTime:HH:mm:ss}" +
                   (Event.Partial ? ". Watching stopped before the full time after the event was recorded." : "."),
        },
    };
}

/// <summary>Display names and units for live signal ids, as the Analyse page and snapshots use them.</summary>
public static class SignalLabels
{
    private static readonly Dictionary<string, (string Name, string Unit, int Order)> Known = new()
    {
        ["rpm"] = ("Engine speed", "rpm", 0), ["speed"] = ("Vehicle speed", "km/h", 1), ["coolant"] = ("Coolant temperature", "°C", 2),
        ["voltage"] = ("Battery voltage", "V", 3), ["load"] = ("Engine load", "%", 4), ["maf"] = ("Air mass", "g/s", 5),
        ["iat"] = ("Intake air", "°C", 6), ["throttle"] = ("Throttle", "%", 7), ["map"] = ("Manifold pressure", "kPa", 8),
        ["stft1"] = ("Short-term trim bank 1", "%", 9), ["ltft1"] = ("Long-term trim bank 1", "%", 10),
        ["stft2"] = ("Short-term trim bank 2", "%", 11), ["ltft2"] = ("Long-term trim bank 2", "%", 12),
        ["oiltemp"] = ("Oil temperature", "°C", 13), ["timing"] = ("Ignition timing", "°", 14),
    };

    public static string Name(string id) =>
        Known.TryGetValue(id, out var k) ? k.Name : id.StartsWith("rough", StringComparison.Ordinal) ? $"Cylinder {id[5..]} roughness" : id;

    public static string Unit(string id) => Known.TryGetValue(id, out var k) ? k.Unit : "";

    public static string Header(string id) => Unit(id).Length > 0 ? $"{Name(id)} ({Unit(id)})" : Name(id);

    public static int Order(string id) => Known.TryGetValue(id, out var k) ? k.Order : 100 + (id.StartsWith("rough", StringComparison.Ordinal) ? 0 : 50);
}

public enum WatchState { Idle, Watching, Capturing, Stopped }

public sealed record WatchStatus(WatchState State, double FramesPerSecond, int Captures, int ArmedRules, string? Last);

/// <summary>
/// The Ghost Hunter. Frames are written to <see cref="Input"/> by whatever reads the car (a background poller); the
/// monitor reads them on its own task, off the UI thread, keeps the last minute in a ring buffer, and checks every rule.
/// When a rule holds, it copies the history before the event, keeps recording for the time after, and hands the whole
/// capture to <c>onCapture</c> (which writes the .k15 snapshot). Read-only: it never sends anything to the car.
/// </summary>
public sealed class TriggerMonitor
{
    private readonly IReadOnlyList<TriggerRule> _rules;
    private readonly Func<TriggerCapture, CancellationToken, Task> _onCapture;
    private readonly Channel<DatalogFrame> _channel;
    private readonly LiveRingBuffer<DatalogFrame> _history;
    private readonly Dictionary<string, RuleState> _state;
    private readonly List<Pending> _pending = [];
    private int _captures;
    private DateTimeOffset? _firstFrame;
    private long _frames;

    private sealed class RuleState
    {
        public readonly Dictionary<int, DateTimeOffset?> TrueSince = [];
        public DateTimeOffset? RestingUntil;
        public int Captures;
        public bool Busy;
    }

    private sealed record Pending(TriggerRule Rule, TriggerEvent Event, List<DatalogFrame> Frames, DateTimeOffset Until);

    public TriggerMonitor(IReadOnlyList<TriggerRule> rules, Func<TriggerCapture, CancellationToken, Task> onCapture, TimeSpan? history = null, int capacity = 2048)
    {
        _rules = rules;
        _onCapture = onCapture;
        var window = history ?? (rules.Count == 0 ? TimeSpan.FromSeconds(60) : rules.Max(r => r.Before));
        _history = new LiveRingBuffer<DatalogFrame>(window, capacity, f => f.Time);
        // Bounded: if writing a capture ever stalls, the oldest frames are dropped rather than memory growing.
        _channel = Channel.CreateBounded<DatalogFrame>(new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        _state = rules.ToDictionary(r => r.Id, _ => new RuleState());
    }

    public ChannelWriter<DatalogFrame> Input => _channel.Writer;

    /// <summary>Every signal any rule needs, plus context worth having in every capture.</summary>
    public static IReadOnlyList<string> SignalsFor(IEnumerable<TriggerRule> rules) =>
        rules.SelectMany(r => r.Parameters).Concat(["rpm", "speed", "coolant", "voltage", "load", "stft1", "ltft1"]).Distinct().ToList();

    public event Action<TriggerEvent>? Fired;
    public event Action<TriggerCapture>? Captured;
    public event Action<WatchStatus>? StatusChanged;

    public WatchState State { get; private set; } = WatchState.Idle;

    /// <summary>Consumes frames until cancelled or the input is completed; then saves any capture still in progress.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        State = WatchState.Watching;
        Report(null);
        try
        {
            await foreach (var frame in _channel.Reader.ReadAllAsync(ct))
                await ProcessAsync(frame, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            // Watching stopped (engine off, Stop pressed): keep what was caught, marked as partial.
            foreach (var p in _pending.ToList())
                await FinishAsync(p, partial: true, CancellationToken.None);
            State = WatchState.Stopped;
            Report(null);
        }
    }

    private async Task ProcessAsync(DatalogFrame frame, CancellationToken ct)
    {
        _firstFrame ??= frame.Time;
        _frames++;
        _history.Add(frame);

        // Captures already running collect this frame, and finish once their time after the event is up.
        foreach (var p in _pending.ToList())
        {
            p.Frames.Add(frame);
            if (frame.Time >= p.Until) await FinishAsync(p, partial: false, ct);
        }

        foreach (var rule in _rules)
        {
            var st = _state[rule.Id];
            if (st.Busy || st.Captures >= rule.MaxCaptures || st.RestingUntil is { } rest && frame.Time < rest) { Track(rule, st, frame); continue; }
            if (!Track(rule, st, frame)) continue;

            st.Busy = true;
            st.Captures++;
            _captures++;
            var values = rule.Parameters.Where(frame.Values.ContainsKey).ToDictionary(p => p, p => frame.Values[p]);
            var ev = new TriggerEvent(rule.Id, rule.Title, rule.Describe(), frame.Time, values);
            var before = _history.Snapshot().Where(f => f.Time >= frame.Time - rule.Before).ToList();
            _pending.Add(new Pending(rule, ev, before, frame.Time + rule.After));
            State = WatchState.Capturing;
            Fired?.Invoke(ev);
            Report($"Caught: {rule.Title}");
            if (rule.After <= TimeSpan.Zero) await FinishAsync(_pending[^1], partial: false, ct);
        }

        if (_frames % 10 == 0) Report(null);
    }

    /// <summary>Updates each condition's "true since" and says whether the whole rule holds now.</summary>
    private static bool Track(TriggerRule rule, RuleState st, DatalogFrame frame)
    {
        var all = true;
        for (var i = 0; i < rule.Conditions.Count; i++)
        {
            var c = rule.Conditions[i];
            var ok = frame.Values.TryGetValue(c.Parameter, out var v) && c.Test(v);
            if (!ok) { st.TrueSince[i] = null; all = false; continue; }
            st.TrueSince.TryGetValue(i, out var since);
            since ??= frame.Time;
            st.TrueSince[i] = since;
            if (frame.Time - since.Value < c.HoldDuration) all = false;
        }
        return all;
    }

    private async Task FinishAsync(Pending p, bool partial, CancellationToken ct)
    {
        _pending.Remove(p);
        var st = _state[p.Rule.Id];
        st.Busy = false;
        st.RestingUntil = p.Event.At + p.Rule.After + p.Rule.Cooldown;
        foreach (var k in st.TrueSince.Keys.ToList()) st.TrueSince[k] = null;
        var capture = new TriggerCapture(partial ? p.Event with { Partial = true } : p.Event, p.Frames.ToList());
        try { await _onCapture(capture, ct); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SnapshotException) { Report($"Couldn't save a capture: {e.Message}"); }
        Captured?.Invoke(capture);
        if (_pending.Count == 0 && State == WatchState.Capturing) State = WatchState.Watching;
        Report($"Saved: {p.Rule.Title}");
    }

    private void Report(string? last)
    {
        var seconds = _firstFrame is { } f && _history.Snapshot() is { Count: > 1 } h ? (h[^1].Time - f).TotalSeconds : 0;
        var rate = seconds > 0 ? _frames / seconds : 0;
        var armed = _rules.Count(r => _state[r.Id].Captures < r.MaxCaptures);
        StatusChanged?.Invoke(new WatchStatus(State, rate, _captures, armed, last));
    }
}

/// <summary>
/// Reads the watched values from the car as fast as the connection allows (with a floor between reads) and writes
/// them to the monitor, on a background task. A failed read is skipped, never fatal: a long drive has to survive a bus hiccup.
/// </summary>
public static class TriggerPoller
{
    public static Task RunAsync(ISignalSource signals, IReadOnlyCollection<string> ids, ChannelWriter<DatalogFrame> output,
        CancellationToken ct, TimeSpan? minInterval = null, Action<Exception>? onError = null) => Task.Run(async () =>
    {
        var floor = minInterval ?? TimeSpan.FromMilliseconds(200);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var started = DateTimeOffset.Now;
                try
                {
                    var values = await signals.ReadAsync(ids, ct);
                    if (values.Count > 0) output.TryWrite(new DatalogFrame(DateTimeOffset.Now, values));
                }
                catch (Exception e) when (e is DiagnosticException or TimeoutException or IOException) { onError?.Invoke(e); }
                var wait = floor - (DateTimeOffset.Now - started);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally { output.TryComplete(); }
    }, CancellationToken.None);
}
