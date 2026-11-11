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

namespace Klemme15.Data;

public enum WriteKind
{
    /// <summary>Clearing a module's fault memory (a small write to its memory).</summary>
    ClearFaults,
    /// <summary>A service function that writes to or resets the module.</summary>
    ServiceFunction,
    /// <summary>Changing the module's coding.</summary>
    Coding,
}

/// <summary>The answer to "may Klemme15 write to this module now?".</summary>
public sealed record WriteCheck(bool Allowed, string? Reason, double? Voltage, double? Floor, FrmAssessment? Frm);

/// <summary>
/// The last line of defence before any write: it reads the battery voltage and refuses when it is below the floor for
/// this module and kind of write. Fragile footwell modules (FRM3, or a footwell module that can't be identified) get the
/// strictest floor, because a voltage dip during a write can leave them dead.
/// </summary>
public static class WriteGuard
{
    /// <summary>
    /// Below this, Klemme15 writes nothing to any module: a 12 V battery this low is deeply discharged, and modules can
    /// reset in the middle of a write. Klemme15's own rule of thumb, deliberately below any voltage a healthy car shows
    /// with the ignition on.
    /// </summary>
    public const double DeadBatteryFloor = 10.5;

    /// <summary>A reading outside this range isn't a car battery's voltage; it is treated as "couldn't be read".</summary>
    public const double LowestPlausible = 6.0, HighestPlausible = 16.5;

    /// <summary>The reading when it can be a 12 V system's voltage, otherwise null (a sensor fault, a damaged answer).</summary>
    public static double? Plausible(double? voltage) =>
        voltage is { } v && !double.IsNaN(v) && v >= LowestPlausible && v <= HighestPlausible ? v : null;

    /// <summary>The voltage floor for a write, or null when there is none.</summary>
    public static double? FloorFor(EcuDefinition target, EcuIdentity? identity, WriteKind kind, FrmAssessment? frm = null)
    {
        frm ??= FrmGuard.IsFootwellModule(target) ? FrmGuard.Assess(identity) : null;
        return kind switch
        {
            WriteKind.ClearFaults => frm?.ClearFloor,
            WriteKind.ServiceFunction => frm is not null ? frm.WriteFloor : null,
            WriteKind.Coding => frm?.WriteFloor ?? FrmGuard.NormalWriteFloor,
            _ => null,
        };
    }

    /// <summary>Decides from a voltage already measured (null when it couldn't be read).</summary>
    public static WriteCheck Decide(EcuDefinition target, EcuIdentity? identity, WriteKind kind, double? voltage)
    {
        var frm = FrmGuard.IsFootwellModule(target) ? FrmGuard.Assess(identity) : null;
        var floor = FloorFor(target, identity, kind, frm);
        var reported = voltage;
        voltage = Plausible(voltage);
        var what = kind switch { WriteKind.ClearFaults => "clearing its faults", WriteKind.Coding => "coding it", _ => "this function" };
        if (voltage < DeadBatteryFloor)
            return new(false, $"Battery at {voltage:0.0} V. That is too low for a module to write reliably, so Klemme15 writes nothing below {DeadBatteryFloor:0.0} V. Charge the battery or connect a charger, then try again.",
                voltage, Math.Max(floor ?? 0, DeadBatteryFloor), frm);
        if (floor is not { } min) return new(true, null, voltage, null, frm);
        if (voltage is null)
        {
            if (reported is not null && (kind == WriteKind.Coding || frm?.Fragile == true && kind == WriteKind.ServiceFunction))
                return new(false, $"The car reported a battery voltage of {reported:0.0} V, which can't be right, and {target.Name} needs at least {min:0.0} V for {what}. Measure the battery with a meter, connect a charger, then try again.", null, min, frm);
            // Coding is never done blind. Clearing faults with an unknown voltage is allowed: the scan before it would have
            // shown a low battery, and it's the same small write every dealer tool does.
            return kind == WriteKind.Coding || frm?.Fragile == true && kind == WriteKind.ServiceFunction
                ? new(false, $"Klemme15 couldn't read the battery voltage, and {target.Name} needs at least {min:0.0} V for {what}. Connect a charger and make sure the engine computer answers, then try again.", null, min, frm)
                : new(true, null, null, min, frm);
        }
        if (voltage < min)
            return new(false, frm?.Fragile == true
                ? $"Battery at {voltage:0.0} V. {target.Name} is {(frm.Generation == FrmGeneration.Frm3 ? "an FRM3, the type" : "possibly the FRM3 type")} that can corrupt itself if the voltage dips while it writes, so Klemme15 needs at least {min:0.0} V for {what}. Connect a battery charger (not a jump pack) and try again."
                : $"Battery at {voltage:0.0} V; {what} on {target.Name} needs at least {min:0.0} V. Charge the battery or connect a charger and try again.",
                voltage, min, frm);
        return new(true, null, voltage, min, frm);
    }

    /// <summary>Reads the voltage through the engine computer and decides.</summary>
    public static async Task<WriteCheck> CheckAsync(IDiagnosticBackend backend, EcuDefinition target, EcuIdentity? identity, WriteKind kind,
        EcuDefinition? engine, IMeasurementReader? measurements = null, CancellationToken ct = default)
    {
        // A write with its own floor (coding, a fragile footwell module) gets a fresh reading. The dead-battery floor
        // alone accepts a reading up to a minute old, so clearing every module doesn't re-read the voltage each time.
        var maxAge = FloorFor(target, identity, kind) is null ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(3);
        return Decide(target, identity, kind, await VoltageAsync(backend, engine, measurements, maxAge, ct));
    }

    private sealed class Sample { public double? Voltage; public DateTime At; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IDiagnosticBackend, Sample> Recent = [];

    /// <summary>The battery voltage through the engine computer, read again when the last reading is older than <paramref name="maxAge"/>.</summary>
    private static async Task<double?> VoltageAsync(IDiagnosticBackend backend, EcuDefinition? engine, IMeasurementReader? measurements, TimeSpan maxAge, CancellationToken ct)
    {
        var sample = Recent.GetOrCreateValue(backend);
        lock (sample)
            if (sample.Voltage is not null && DateTime.UtcNow - sample.At <= maxAge) return sample.Voltage;
        var voltage = (await ServiceRunner.ReadStateAsync(backend, engine, measurements, ct)).Voltage;
        lock (sample) { sample.Voltage = voltage; sample.At = DateTime.UtcNow; }
        return voltage;
    }
}
