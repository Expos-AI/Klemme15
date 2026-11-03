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
using System.Globalization;
using Klemme15.Core;

namespace Klemme15.App.ViewModels;

public sealed class DrainClueViewModel(DrainClue clue)
{
    public string Title => clue.Title;
    public string Detail => clue.Detail;
    public bool IsWarn => clue.Level == CheckLevel.Warn;
    public bool IsAsk => clue.Level == CheckLevel.Ask;
    public bool IsGood => clue.Level == CheckLevel.Good;
    public bool IsInfo => clue.Level == CheckLevel.Info;
}

public sealed class DrainStepViewModel(int number, DrainStep step) : ViewModelBase
{
    private bool _done;
    public string Title => $"{number}. {step.Title}";
    public string Detail => step.Detail;
    public bool Done { get => _done; set => Set(ref _done, value); }
}

/// <summary>"Battery keeps going flat?": the car's own clues, a parked-current calculator, and the fuse-pull hunt.</summary>
public sealed class DrainFinderViewModel : ViewModelBase
{
    private readonly SessionState _session;
    private bool _open;
    private string _milliamps = "", _ah = "70", _volts = "";
    private DrainEstimate? _estimate;
    private string? _stepsFor;

    public DrainFinderViewModel(SessionState session)
    {
        _session = session;
        ToggleCommand = new RelayCommand(() => IsOpen = !IsOpen);
        _session.ScanUpdated += () => OnUi(() => { if (_open) Refresh(); });
        Refresh();
    }

    public bool IsOpen { get => _open; set { if (Set(ref _open, value)) { Raise(nameof(ToggleText)); if (value) Refresh(); } } }
    public string ToggleText => _open ? "Close drain finder" : "Battery going flat?";
    public RelayCommand ToggleCommand { get; }

    public ObservableCollection<DrainClueViewModel> Clues { get; } = [];
    public ObservableCollection<DrainStepViewModel> Steps { get; } = [];
    public bool HasClues => Clues.Count > 0;
    public string CluesHeadline => _session.LastScan is null ? "Run a quick test first, and Klemme15 lists what the car itself recorded about its battery."
        : Clues.Count == 0 ? "The car recorded nothing about its battery running down. A drain can still be there: measure it below."
        : $"What the car itself recorded ({Clues.Count}):";

    public string Milliamps { get => _milliamps; set { if (Set(ref _milliamps, value)) Recalculate(); } }
    public string BatteryAh { get => _ah; set { if (Set(ref _ah, value)) Recalculate(); } }
    public string RestingVolts { get => _volts; set { if (Set(ref _volts, value)) Recalculate(); } }

    public string EstimateHeadline => _estimate?.Headline ?? "";
    public string EstimateDetail => _estimate?.Detail ?? "";
    public bool EstimateWarn => _estimate?.Level == CheckLevel.Warn;
    public bool EstimateAsk => _estimate?.Level == CheckLevel.Ask;
    public bool EstimateGood => _estimate?.Level == CheckLevel.Good;

    private static double? Number(string text) =>
        double.TryParse(text.Replace(',', '.').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private void Recalculate()
    {
        _estimate = Number(_milliamps) is { } ma && Number(_ah) is { } ah ? BatteryDrain.Estimate(ma, ah, Number(_volts)) : BatteryDrain.Estimate(0, 0);
        foreach (var p in new[] { nameof(EstimateHeadline), nameof(EstimateDetail), nameof(EstimateWarn), nameof(EstimateAsk), nameof(EstimateGood) }) Raise(p);
    }

    private void Refresh()
    {
        Clues.Clear();
        if (_session.LastScan is { } scan)
            foreach (var c in BatteryDrain.Clues(scan)) Clues.Add(new DrainClueViewModel(c));
        if (Steps.Count == 0 || _stepsFor != _session.Vehicle?.Id)
        {
            _stepsFor = _session.Vehicle?.Id;
            Steps.Clear();
            var n = 1;
            foreach (var s in BatteryDrain.Hunt(_session.Vehicle?.Id)) Steps.Add(new DrainStepViewModel(n++, s));
        }
        if (string.IsNullOrEmpty(_volts) && _session.LastScan?.BatteryVoltage is { } v and < 13.0)
            _volts = v.ToString("0.0#", CultureInfo.InvariantCulture);
        Raise(nameof(HasClues)); Raise(nameof(CluesHeadline)); Raise(nameof(RestingVolts));
        Recalculate();
    }
}
