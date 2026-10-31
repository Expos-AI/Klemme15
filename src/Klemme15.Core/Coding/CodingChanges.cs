using System.Text.RegularExpressions;

namespace Klemme15.Core.Coding;

/// <summary>
/// A popular convenience setting, described in plain words. It names BMW's function keywords (the identifiers NCS Expert
/// shows) and the values for on and off; the module map from the user's own NCS data says where they live.
/// Nothing in the key module (CAS) is offered: comfort closing with the remote, auto-lock and key settings live there.
/// </summary>
/// <param name="Keywords">Regular expressions for the keywords this toggle sets (all matching settings change together).</param>
public sealed record CodingToggle(string Id, string Title, string Module, string Description, IReadOnlyList<string> Keywords,
    string OnValue, string OffValue, string? Caveat = null)
{
    public IReadOnlyList<NcsSetting> Match(NcsModule module) =>
        module.Supported.Where(s => Keywords.Any(k => Regex.IsMatch(s.Name, $"^(?:{k})$", RegexOptions.IgnoreCase))
                                    && s.Values.Any(v => v.Name.Equals(OnValue, StringComparison.OrdinalIgnoreCase))
                                    && s.Values.Any(v => v.Name.Equals(OffValue, StringComparison.OrdinalIgnoreCase))).ToList();
}

public enum ToggleState { Unavailable, Off, On, Mixed, Unknown }

/// <summary>
/// Several comfort options switched together in one go (one backup, one write, one read-back, all or nothing).
/// A preset is only a list of existing options: where each lives still comes from the user's verified NCS data or a
/// community-verified map for their exact module, never from fixed byte positions.
/// </summary>
public sealed record CodingPreset(string Id, string Title, string Module, string Description, IReadOnlyList<string> ToggleIds, string? Caveat = null)
{
    public IReadOnlyList<CodingToggle> Toggles => CodingToggles.All.Where(t => ToggleIds.Contains(t.Id)).ToList();
}

public static class CodingPresets
{
    public static readonly IReadOnlyList<CodingPreset> All =
    [
        new("led-pack", "LED conversion: all bulb warnings off", "FRM",
            "Turns off the bulb test for the indicators, brake lights, number plate lights, parking lights / angel eyes and fog lights at once, " +
            "so LED replacements don't flicker, flash fast or show bulb-out warnings.",
            ["led-turn-signals", "led-brake-lights", "led-plate-lights", "led-parking-lights", "led-fog-lights"],
            "With bulb tests off you won't be warned when one of these lights fails: walk round the car now and then. Low beams and xenons aren't touched."),
        new("cluster-pack", "Cluster extras: digital speed, live consumption, dials always lit", "KOMBI",
            "Adds digital speed and an instant fuel consumption page to the trip computer, and keeps the dials lit in daylight.",
            ["digital-speed", "instant-consumption", "dial-lighting"]),
    ];

    /// <summary>On when every available option in the preset is on; Unavailable when the module has none of them.</summary>
    public static ToggleState Combine(IEnumerable<ToggleState> states)
    {
        var s = states.Where(x => x != ToggleState.Unavailable).ToList();
        if (s.Count == 0) return ToggleState.Unavailable;
        if (s.Any(x => x == ToggleState.Unknown)) return ToggleState.Unknown;
        if (s.All(x => x == ToggleState.On)) return ToggleState.On;
        if (s.All(x => x == ToggleState.Off)) return ToggleState.Off;
        return ToggleState.Mixed;
    }
}

public static class CodingToggles
{
    public static readonly IReadOnlyList<CodingToggle> All =
    [
        new("led-turn-signals", "LED turn signals: no bulb warning or fast flashing", "FRM",
            "Stops the footwell module testing the indicator bulbs, so LED indicators don't flash fast or show a bulb-out warning.",
            ["(KALT|WARM)UEBERWACHUNG_BLK_.*"], "nicht_aktiv", "aktiv",
            "With monitoring off you won't be warned about a failed indicator: check them now and then."),
        new("led-brake-lights", "LED brake lights: no bulb warning", "FRM",
            "Stops the bulb test on the brake lights (including the centre one) for LED replacements.",
            ["(KALT|WARM)UEBERWACHUNG_BL_(L|R|M|MI|M_1|M_2)"], "nicht_aktiv", "aktiv",
            "You won't be warned about a failed brake light: check them now and then."),
        new("led-plate-lights", "LED number plate lights: no bulb warning", "FRM",
            "Stops the bulb test on the number plate lights for LED replacements.",
            ["(KALT|WARM)UEBERWACHUNG_KZL(_.*)?"], "nicht_aktiv", "aktiv"),
        new("led-parking-lights", "LED parking lights / angel eyes: no bulb warning", "FRM",
            "Stops the bulb test on the front parking lights (angel eyes on many cars) for LED replacements.",
            ["(KALT|WARM)UEBERWACHUNG_SL_V_(L|R)"], "nicht_aktiv", "aktiv"),
        new("led-fog-lights", "LED fog lights: no bulb warning", "FRM",
            "Stops the bulb test on the front fog lights for LED replacements.",
            ["(KALT|WARM)UEBERWACHUNG_NSW(_.*)?"], "nicht_aktiv", "aktiv"),
        new("digital-speed", "Digital speed in the cluster", "KOMBI",
            "Adds your speed as a number to the trip computer display in the instrument cluster.",
            ["BC_DIGITAL_V"], "aktiv", "nicht_aktiv"),
        new("instant-consumption", "Instant fuel consumption in the cluster", "KOMBI",
            "Adds a live fuel consumption page to the trip computer.",
            ["DIGITAL_KVA"], "aktiv", "nicht_aktiv"),
        new("dial-lighting", "Dial lighting always on", "KOMBI",
            "Keeps the instrument dials lit in daylight, not just when the lights are on.",
            ["M_TAG_SKALENBELEUCHTUNG"], "aktiv", "nicht_aktiv"),
        new("mirror-fold", "Fold the mirrors when locking with the remote", "FRM",
            "Folds the door mirrors when you hold the lock button on the key (power-folding mirrors only).",
            ["SPIEGEL_KOMFORT_EINKLAPP", "ASP_KOMFORT_EINKLAPP"], "aktiv", "nicht_aktiv",
            "Only works if the car already closes the windows when you hold the lock button: that setting lives in the key module (CAS), which Klemme15 doesn't code."),
        new("window-door-stop", "Windows keep closing when a door is opened", "FRM",
            "Normally opening a door stops a window that is closing by itself. With this on it carries on.",
            ["TUERAUF_STOP_MAUT", "FH_TUERAUF_STOP_MAUT_3R"], "nicht_aktiv", "aktiv"),
        new("wiper-finish", "Wipers finish their sweep after switching off", "JBE",
            "When you switch the ignition off mid-sweep, the wipers return to rest instead of stopping on the glass.",
            ["ZYCLUS_VOLLEND_KLR_AUS"], "aktiv", "nicht_aktiv"),
    ];

    public static ToggleState StateOf(CodingToggle toggle, NcsModule module, NcsLayout layout, IReadOnlyList<CodingBlock> blocks)
    {
        var matches = toggle.Match(module);
        if (matches.Count == 0) return ToggleState.Unavailable;
        var map = blocks.ToDictionary(b => b.Id);
        var values = matches.Select(s => layout.Decode(s, map)).ToList();
        if (values.Any(v => v is null)) return ToggleState.Unknown;
        if (values.All(v => v!.Equals(toggle.OnValue, StringComparison.OrdinalIgnoreCase))) return ToggleState.On;
        if (values.All(v => v!.Equals(toggle.OffValue, StringComparison.OrdinalIgnoreCase))) return ToggleState.Off;
        return ToggleState.Mixed;
    }

    /// <summary>The setting changes a toggle needs.</summary>
    public static IReadOnlyList<(NcsSetting Setting, string Value)> ChangesFor(CodingToggle toggle, NcsModule module, bool on) =>
        toggle.Match(module).Select(s => (s, on ? toggle.OnValue : toggle.OffValue)).ToList();
}

/// <summary>What a coding change did.</summary>
public sealed record CodingResult(bool Success, string Message, IReadOnlyList<CodingDifference> Written, bool Restored);

/// <summary>
/// Changes coding the careful way: re-runs the verification from the evidence itself, refuses settings NCS Expert's trace
/// didn't cover or that can't be stored losslessly, works out the new bytes under every verified layout (and refuses if
/// they disagree), checks only the requested settings' bits change, writes block by block with read-back, re-reads and
/// decodes the whole module, and puts the original coding back automatically if anything is off.
/// </summary>
public static class CodingWriter
{
    public static async Task<CodingResult> ApplyAsync(IRawRequests raw, EcuDefinition ecu, NcsEvidence evidence,
        IReadOnlyList<(NcsSetting Setting, string Value)> changes, CodingBackup backup, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!CodingProtocol.IsCodable(ecu)) return new(false, $"Klemme15 doesn't code {ecu.Name}.", [], false);
        if (!backup.ModuleId.Equals(ecu.Id, StringComparison.OrdinalIgnoreCase)) return new(false, "The backup is from a different module.", [], false);
        var verified = evidence.Layouts;
        if (verified.Count == 0)
            return new(false, "This module's map hasn't been verified against NCS Expert's reading of your module, so Klemme15 won't write to it.", [], false);
        if (changes.Count == 0) return new(false, "Nothing to change.", [], false);
        var module = evidence.Module;
        foreach (var (s, v) in changes)
        {
            if (!module.Settings.Contains(s)) return new(false, $"{s.Key} isn't from the verified module file.", [], false);
            if (!evidence.Trace.ContainsKey(s.Key))
                return new(false, $"NCS Expert's reading didn't include {s.Key}, so Klemme15 can't be sure where it lives. Nothing was written.", [], false);
            if (verified.Any(l => !s.Lossless(l)))
                return new(false, $"{s.Key}'s values can't be stored unambiguously. Nothing was written.", [], false);
            if (!s.Values.Any(x => x.Name.Equals(v, StringComparison.OrdinalIgnoreCase))) return new(false, $"{s.Key} has no value '{v}'.", [], false);
        }

        var layout = verified[0];
        var touched = changes.Select(c => layout.Locate(c.Setting).Block).Distinct().ToList();
        if (touched.Any(b => !CodingProtocol.IsCodingBlock(b) || backup.Blocks.All(x => x.Id != b)))
            return new(false, "The backup doesn't cover the blocks this change writes. Take a fresh backup first.", [], false);

        progress?.Report("Reading the current coding...");
        var all = module.Supported.Select(s => layout.Locate(s).Block).Where(CodingProtocol.IsCodingBlock).Distinct().ToList();
        var before = await CodingProtocol.ReadBlocksAsync(raw, ecu, all, null, ct);
        var beforeValues = Values(module, layout, before);

        // Every verified layout has to produce exactly the same bytes.
        IReadOnlyList<CodingBlock>? target = null;
        foreach (var l in verified)
        {
            var t = before;
            foreach (var (s, v) in changes) t = l.Apply(s, v, t);
            if (target is null) target = t;
            else if (CodingProtocol.Compare(target, t).Count > 0)
                return new(false, "The verified maps disagree about which bits to change, so Klemme15 won't guess. Nothing was written.", [], false);
        }
        var diffs = CodingProtocol.Compare(before, target!);
        if (diffs.Count == 0) return new(true, "Already set that way: nothing to write.", [], false);

        // Only the requested settings' own bits may change.
        foreach (var d in diffs)
        {
            var allowed = changes.Where(c => layout.Locate(c.Setting) == (d.Block, d.Offset)).Aggregate(0, (m, c) => m | c.Setting.Mask);
            if ((d.ChangedBits & ~allowed) != 0)
                return new(false, $"Safety stop: the change would touch bits outside the requested settings ({d}). Nothing was written.", [], false);
        }

        var original = before.ToDictionary(b => b.Id);
        var written = new List<CodingBlock>();
        try
        {
            foreach (var block in target!.Where(t => diffs.Any(d => d.Block == t.Id)))
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Writing block {block.IdHex}...");
                written.Add(original[block.Id]);
                await CodingProtocol.WriteBlockAsync(raw, ecu, block, CancellationToken.None);
            }

            progress?.Report("Checking every setting...");
            var after = await CodingProtocol.ReadBlocksAsync(raw, ecu, all, null, CancellationToken.None);
            var afterValues = Values(module, layout, after);
            var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (s, v) in changes) wanted[s.Key] = v;
            foreach (var (key, value) in afterValues)
            {
                var expected = wanted.TryGetValue(key, out var w) ? w : beforeValues.GetValueOrDefault(key);
                if (!string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
                    throw new DiagnosticException($"After writing, {key} reads '{value ?? "?"}' instead of '{expected ?? "?"}'.");
            }
            return new(true, "Coded and checked: every setting reads back as expected. Switch the ignition off, wait 10 seconds and switch it on again for the module to use the new setting.", diffs, false);
        }
        catch (Exception e)
        {
            if (written.Count == 0)
            {
                if (e is DiagnosticException or TimeoutException or OperationCanceledException) return new(false, $"Nothing was written: {e.Message}", [], false);
                throw;
            }
            progress?.Report("Something went wrong: putting the original coding back...");
            var failed = await CodingProtocol.WriteAllAsync(raw, ecu, written);
            return failed.Count == 0
                ? new(false, $"{e.Message} The original coding was put back and checked.", [], true)
                : new(false, $"{e.Message} Putting the original back failed for block(s) {string.Join(", ", failed)}. Restore your backup from the Coding page as soon as the module answers again.", diffs, false);
        }
    }

    private static Dictionary<string, string?> Values(NcsModule module, NcsLayout layout, IReadOnlyList<CodingBlock> blocks)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (s, v) in NcsDecoder.Decode(module, layout, blocks)) map[s.Key] = v;
        return map;
    }
}
