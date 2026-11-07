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

    /// <summary>
    /// The rough idle and air conditioning check is running: the driver stays off the pedal, switches the air
    /// conditioning on partway through, and the engine dips a little as the compressor engages.
    /// </summary>
    public bool IdleCheck { get; init; }

    /// <summary>What the demo "actuator" last asked the pump for (0-1), or null when the engine computer drives it.</summary>
    public double? PumpCommand { get; set; }

    private double J(double centre, double spread) => centre + (_random.NextDouble() - 0.5) * 2 * spread;

    public async Task<IReadOnlyDictionary<string, double>> ReadAsync(IReadOnlyCollection<string> signals, CancellationToken ct = default)
    {
        _reads++;
        // Idle for the first readings of a test, then the "driver" holds 2,500 rpm for a while, then idles again.
        var idleCheck = IdleCheck;
        var loaded = !idleCheck && _reads % 90 is > 34 and < 75;
        var rpm = loaded ? J(2500, 60) : J(760, 15);
        if (idleCheck && _reads is >= 50 and < 53) rpm = J(655, 10);
        var values = new Dictionary<string, double>
        {
            ["rpm"] = rpm, ["coolant"] = 92, ["voltage"] = 14.1, ["maf"] = loaded ? J(11.5, 0.4) : J(3.6, 0.15), ["load"] = loaded ? J(24, 1) : J(18, 1),
            ["stft1"] = loaded ? J(1, 2) : J(4, 2), ["ltft1"] = loaded ? J(2, 0.5) : J(10, 0.5),
            ["stft2"] = loaded ? J(0, 2) : J(3, 2), ["ltft2"] = loaded ? J(1, 0.5) : J(9, 0.5),
        };
        if (idleCheck)
        {
            values["idle_target"] = 760;
            values["ac"] = _reads >= 48 ? 1 : 0;
            values["ac_compressor"] = _reads >= 50 ? 1 : 0;
        }
        // A slightly tired electric water pump: it reaches about 90 % of what's asked, a bit less at high demand.
        if (PumpCommand is { } cmd)
        {
            values["rpm"] = 0;
            values["voltage"] = 12.5;
            values["coolant"] = 24;
            values["pump_target"] = cmd * 100;
            values["pump_speed"] = Math.Max(0, cmd * 100 * (cmd > 0.7 ? 0.82 : 0.92) + J(0, 1.5));
        }
        else
        {
            var target = loaded ? 72 : 38;
            values["pump_target"] = target;
            values["pump_speed"] = target * (loaded ? 0.86 : 0.93) + J(0, 1.2);
        }
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
