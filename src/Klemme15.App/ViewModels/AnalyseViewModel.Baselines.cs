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

using System.Collections.ObjectModel;
using System.Text;
using Klemme15.Core;

namespace Klemme15.App.ViewModels;

/// <summary>A healthy curve the user can lay behind the scatter plot.</summary>
public sealed class BaselineChoice(BaselineProfile? profile, BaselineCurve? curve)
{
    public BaselineProfile? Profile { get; } = profile;
    public BaselineCurve? Curve { get; } = curve;
    public string Label => Profile is null || Curve is null ? "No baseline" : $"{Curve.Title} · {Profile.Title}";
    public override string ToString() => Label;
}

/// <summary>
/// "Known good" overlays: pick a healthy curve, the scatter switches to that curve's axes, the healthy range is drawn
/// behind your points, and a line says how much of your log sits inside it. Baselines line up on engine speed or load,
/// never on time, so a log and a baseline recorded on different days compare fairly.
/// </summary>
public sealed partial class AnalyseViewModel
{
    private BaselineChoice? _baseline;
    private string _baselineStatus = "";

    /// <summary>The shipped baselines (set by the main window; tests leave it empty).</summary>
    public Func<IReadOnlyList<BaselineProfile>> ShippedBaselines { get; set; } = () => [];

    /// <summary>The engine of the connected car, so baselines for other engines are left out.</summary>
    public Func<string?> EngineCode { get; set; } = () => null;

    /// <summary>Where your own baselines live (null = don't save).</summary>
    public BaselineStore? Store { get; set; }

    public AsyncCommand ImportBaselineCommand { get; }
    public AsyncCommand MakeBaselineCommand { get; }
    public AsyncCommand ShareBaselineCommand { get; private set; } = null!;

    private string? _shareEngine;

    /// <summary>The engine code for a shared baseline (from the connected car; editable, e.g. N52B25 outside the US).</summary>
    public string ShareEngine { get => _shareEngine ?? EngineCode() ?? ""; set => Set(ref _shareEngine, value?.Trim().ToUpperInvariant()); }

    /// <summary>
    /// Shares this log so owners of the same engine get a community baseline: checks it's warm and covers idle and a
    /// range of engine speeds, saves a contribution file with only the engine values (no VIN, no date, no location),
    /// and opens the form on GitHub to attach it. A pipeline there checks it again and prepares the baseline.
    /// </summary>
    private async Task ShareBaselineAsync()
    {
        var engine = ShareEngine;
        var problems = BaselineContribution.Check(_log, engine);
        if (problems.Count > 0) { BaselineStatus = "Not ready to share yet: " + string.Join(" ", problems); return; }
        var version = typeof(AnalyseViewModel).Assembly.GetName().Version?.ToString(3) ?? "";
        var csv = BaselineContribution.ToCsv(_log, engine, ChassisName(), ModelName(), version);
        var saved = await _dialogs.SaveTextAsync("Save your baseline to share", $"baseline-{engine.ToLowerInvariant()}", "csv", csv);
        if (!saved) return;
        await _dialogs.OpenUrlAsync(BaselineContribution.IssueUrl(engine));
        BaselineStatus = "Saved. On the GitHub page that just opened, attach the file and send it: a check runs on it straight away and prepares " +
                         $"the {engine} baseline for the next release. Thank you!";
    }

    /// <summary>The connected car's chassis and model, for the contribution's comment lines (set by the main window).</summary>
    public Func<string?> ChassisName { get; set; } = () => null;
    public Func<string?> ModelName { get; set; } = () => null;

    public ObservableCollection<BaselineChoice> Baselines { get; } = [];
    public bool HasBaselines => Baselines.Count > 1;

    public BaselineChoice? Baseline
    {
        get => _baseline;
        set
        {
            if (!Set(ref _baseline, value)) return;
            if (value?.Curve is { } curve)
            {
                // Put the scatter on the baseline's axes, so you compare like with like.
                if (ChoiceFor(curve.X) is { } x) ScatterX = x;
                if (ChoiceFor(curve.Y) is { } y) ScatterY = y;
            }
            RaiseBand();
        }
    }

    public IReadOnlyList<BaselinePoint>? Band => _baseline?.Curve is { } curve && OnBaselineAxes(curve) ? curve.Points : null;
    public bool HasBand => Band is not null;
    public string BaselineTrust => _baseline?.Profile is { } p ? $"{p.Trust}. {p.Conditions}" : "";

    public string BaselineSummary
    {
        get
        {
            if (_baseline?.Curve is not { } curve) return "";
            if (!OnBaselineAxes(curve)) return "Switch the scatter back to the baseline's axes to see the healthy range.";
            return BaselineAligner.Compare(_log, curve)?.Summary ?? "This log doesn't have the values this baseline needs.";
        }
    }

    public string BaselineStatus { get => _baselineStatus; private set => Set(ref _baselineStatus, value); }

    private void RaiseBand()
    {
        Raise(nameof(Band)); Raise(nameof(HasBand)); Raise(nameof(BaselineSummary)); Raise(nameof(BaselineTrust));
    }

    private bool OnBaselineAxes(BaselineCurve curve) =>
        _scatterX is { } x && _scatterY is { } y && ChoiceFor(curve.X) == x && ChoiceFor(curve.Y) == y;

    private ChannelChoice? ChoiceFor(string id)
    {
        var channel = id.Contains('+') ? _log.Channel(BaselineSignals.Label(id)) : BaselineSignals.Find(_log, id);
        return channel is null ? null : Channels.FirstOrDefault(c => ReferenceEquals(c.Channel, channel));
    }

    /// <summary>Adds "Total trim bank N" (short-term + long-term) when a log has both, since that's what the engine really corrects,
    /// and shows air mass in g/s.</summary>
    private static DataLog WithTotalTrims(DataLog log)
    {
        var extra = new List<LogChannel>();
        foreach (var bank in new[] { "1", "2" })
        {
            var id = $"stft{bank}+ltft{bank}";
            var name = BaselineSignals.Label(id);
            if (log.Channel(name) is not null || !BaselineSignals.Has(log, id)) continue;
            var cache = new Dictionary<string, LogChannel?>();
            var samples = Enumerable.Range(0, log.Count).Select(i => BaselineSignals.At(log, id, i, cache)).ToList();
            extra.Add(new LogChannel(name, "%", samples));
        }
        // Air mass logged in kg/h is shown in g/s, like Klemme15's own logs and every baseline.
        var channels = log.Channels.Select(c => BaselineSignals.IsKgPerHour(c) && ReferenceEquals(BaselineSignals.Find(log, "maf"), c)
            ? new LogChannel(c.Name, "g/s", c.Samples.Select(v => v / 3.6).ToList()) : c).ToList();
        var converted = channels.Where((c, i) => !ReferenceEquals(c, log.Channels[i])).Any();
        return extra.Count == 0 && !converted ? log : new DataLog(log.Times, [.. channels, .. extra]);
    }

    private void RefreshBaselines()
    {
        var previous = _baseline?.Curve;
        Baselines.Clear();
        Baselines.Add(new BaselineChoice(null, null));
        var engine = EngineCode();
        var profiles = ShippedBaselines().Concat(Store?.Load() ?? []).Where(p => p.Fits(engine));
        foreach (var p in profiles)
            foreach (var c in p.Curves.Where(c => BaselineAligner.Applies(_log, c)))
                Baselines.Add(new BaselineChoice(p, c));
        _baseline = Baselines.FirstOrDefault(b => b.Curve is not null && b.Curve == previous) ?? Baselines[0];
        Raise(nameof(Baseline)); Raise(nameof(HasBaselines));
        RaiseBand();
    }

    private async Task ImportBaselineAsync()
    {
        var text = await _dialogs.OpenTextAsync("Open a baseline", "Klemme15 baseline (JSON)", ["*.json"]);
        if (text is null) return;
        if (BaselineProfile.Parse(text) is not { } profile) { BaselineStatus = "That file isn't a Klemme15 baseline (or its numbers don't add up)."; return; }
        if (Store is null) { BaselineStatus = "Baselines can't be saved here."; return; }
        Store.Save(profile);
        BaselineStatus = $"Added \"{profile.Title}\". {profile.Trust}.";
        RefreshBaselines();
    }

    /// <summary>Makes a baseline from this log, for when you know the car is healthy: keep it, compare later drives, or share it.</summary>
    private async Task MakeBaselineAsync()
    {
        var engine = EngineCode();
        var stamp = DateTime.Now;
        var id = $"mine-{(engine ?? "car").ToLowerInvariant()}-{stamp:yyyyMMdd-HHmm}";
        var title = $"My {(engine ?? "car")} on {stamp:d MMM yyyy}";
        var profile = BaselineStore.FromLog(_log, id, title, engine, "Made from one of your own logs. Warm engine where the log had coolant temperature.");
        if (profile is null) { BaselineStatus = "This log needs engine speed and air mass, load or fuel trims to make a baseline."; return; }
        Store?.Save(profile);
        var saved = await _dialogs.SaveTextAsync("Save the baseline to share", id, "json", profile.ToJson());
        BaselineStatus = $"Made \"{title}\" with {profile.Curves.Count} curve{(profile.Curves.Count == 1 ? "" : "s")}" +
                         (saved ? ". A copy is saved where you chose, ready to share." : ".") +
                         " One car is a hint, not an answer key: baselines get better as more healthy cars add to them.";
        RefreshBaselines();
    }
}
