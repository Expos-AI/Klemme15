using Klemme15.Core;

namespace Klemme15.Protocols.Simulation;

/// <summary>
/// Engine signals for the demo car's guided tests. It plays a driver who holds 2,500 rpm when asked, and an engine with
/// a small vacuum leak (lean at idle, fine under load), a slightly rough cylinder 3 and healthy VANOS. Made up.
/// </summary>
public sealed class DemoSignals(IMeasurementReader measurements, EcuDefinition engine) : ISignalSource
{
    private readonly Random _random = new(7);
    private int _reads;

    private double J(double centre, double spread) => centre + (_random.NextDouble() - 0.5) * 2 * spread;

    public async Task<IReadOnlyDictionary<string, double>> ReadAsync(IReadOnlyCollection<string> signals, CancellationToken ct = default)
    {
        _reads++;
        // Idle for the first readings of a test, then the "driver" holds 2,500 rpm for a while, then idles again.
        var loaded = _reads % 90 is > 34 and < 75;
        var rpm = loaded ? J(2500, 60) : J(760, 15);
        var values = new Dictionary<string, double>
        {
            ["rpm"] = rpm, ["coolant"] = 92, ["voltage"] = 14.1,
            ["stft1"] = loaded ? J(1, 2) : J(4, 2), ["ltft1"] = loaded ? J(2, 0.5) : J(10, 0.5),
            ["stft2"] = loaded ? J(0, 2) : J(3, 2), ["ltft2"] = loaded ? J(1, 0.5) : J(9, 0.5),
        };
        var bmw = signals.Where(s => !values.ContainsKey(s) && EngineSignals.BmwNames.ContainsKey(s)).ToList();
        if (bmw.Count > 0)
        {
            var read = await measurements.ReadMeasurementsAsync(engine, bmw.SelectMany(s => EngineSignals.BmwNames[s]).ToList(), ct);
            foreach (var s in bmw)
                if (EngineSignals.BmwNames[s].Where(read.ContainsKey).Select(n => read[n]).Cast<double?>().FirstOrDefault() is { } d)
                    values[s] = d;
        }
        else await Task.Delay(30, ct);
        return values.Where(kv => signals.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
