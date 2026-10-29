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
        if (floor is not { } min) return new(true, null, voltage, null, frm);
        var what = kind switch { WriteKind.ClearFaults => "clearing its faults", WriteKind.Coding => "coding it", _ => "this function" };
        if (voltage is null)
        {
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
        if (FloorFor(target, identity, kind) is null) return new(true, null, null, null, null);
        var state = await ServiceRunner.ReadStateAsync(backend, engine, measurements, ct);
        return Decide(target, identity, kind, state.Voltage);
    }
}
