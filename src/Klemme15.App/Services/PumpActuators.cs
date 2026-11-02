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

using System.Globalization;
using Klemme15.Core;
using Klemme15.Data;
using Klemme15.App.ViewModels;

namespace Klemme15.App.Services;

/// <summary>
/// Drives the electric water pump through the engine computer's own test job (the same job and argument format as
/// Service &gt; Test the electric water pump), from the user's BMW files. Built only when the module version has the job.
/// </summary>
public sealed class JobPumpActuator(IJobRunner jobs, string variant, ServiceImplementation impl, TrafficLog log) : IActuator
{
    /// <summary>The service function's argument is "half speed;seconds": its first number tells the scale (50 = percent, 128 = 0-255).</summary>
    private double Scale => double.TryParse(impl.Steps[0].Args.Split(';')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var half) && half > 0 ? half * 2 : 100;

    public static JobPumpActuator? For(SessionState session, EcuDefinition engine)
    {
        if (session.Backend is not IJobRunner jobs) return null;
        var function = session.Database.Service.All.FirstOrDefault(f => f.Id == "test-water-pump");
        if (function is null || session.ServiceVariant(engine, function) is not { } variant) return null;
        var impl = function.Implementations.FirstOrDefault(i => i.Variants.Contains(variant.ToLowerInvariant()));
        return impl is null || impl.Steps.Count == 0 || !impl.Steps[0].Args.Contains(';') ? null : new JobPumpActuator(jobs, variant.ToLowerInvariant(), impl, session.Log);
    }

    public async Task SetAsync(double fraction, int seconds, CancellationToken ct)
    {
        var value = Math.Round(Math.Clamp(fraction, 0, 1) * Scale);
        log.Note($"Water pump sweep: {impl.Steps[0].Job} {value};{seconds}");
        await jobs.RunJobAsync(variant, impl.Steps[0].Job, string.Create(CultureInfo.InvariantCulture, $"{value};{seconds}"), ct);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        // The engine computer has an "end control" job for the pump on the versions that have the test job;
        // if it doesn't, the step's own duration ends it.
        try { await jobs.RunJobAsync(variant, "STEUERN_ENDE_EWAP", "", ct); }
        catch (Exception e) when (e is DiagnosticException or NotSupportedException) { }
    }
}

/// <summary>The demo car's pump: nothing is sent, the made-up pump follows the command. The demo engine is "off" for the sweep.</summary>
public sealed class DemoPumpActuator : IActuator
{
    private readonly Klemme15.Protocols.Simulation.DemoSignals signals;

    public DemoPumpActuator(Klemme15.Protocols.Simulation.DemoSignals signals)
    {
        this.signals = signals;
        signals.PumpCommand = 0;
    }

    public Task SetAsync(double fraction, int seconds, CancellationToken ct) { signals.PumpCommand = fraction; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) { signals.PumpCommand = null; return Task.CompletedTask; }
}
