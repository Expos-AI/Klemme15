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

namespace Klemme15.App.ViewModels;

/// <summary>One car in the Garage's "Your cars" list: its latest scan and the most important thing that's changing.</summary>
public sealed class CarCardViewModel : ViewModelBase
{
    private bool _confirming;

    public CarCardViewModel(HealthSnapshot latest, IReadOnlyList<HealthSnapshot> history, Action<string> select, Action<string> forget)
    {
        Latest = latest;
        Scans = history.Count;
        var top = HealthTrends.Analyze(history).FirstOrDefault(t => t.Level != TrendLevel.Info);
        TopTrend = top?.Title ?? "";
        TrendIsAct = top?.Level == TrendLevel.Act;
        SelectCommand = new RelayCommand(() => { if (latest.ModelId is { } id) select(id); }, () => latest.ModelId is not null);
        AskForgetCommand = new RelayCommand(() => Confirming = true);
        KeepCommand = new RelayCommand(() => Confirming = false);
        ForgetCommand = new RelayCommand(() => forget(latest.CarKey));
    }

    public HealthSnapshot Latest { get; }
    public int Scans { get; }
    public string Name => Latest.ModelName ?? Latest.Chassis ?? "BMW";
    public string LastScan => $"Last quick test {Latest.Time.LocalDateTime:d MMM yyyy}" + (Latest.Km is { } km ? $" · ~{km:N0} km" : "") + $" · {Scans} quick test{(Scans == 1 ? "" : "s")}";

    public string Status
    {
        get
        {
            var parts = new List<string>();
            if (Latest.Voltage is { } v) parts.Add($"{v:0.0} V");
            parts.Add(Latest.FixNow > 0 ? $"{Latest.FixNow} to fix now" : Latest.CheckIt > 0 ? $"{Latest.CheckIt} worth checking" : "no faults to fix");
            return string.Join(" · ", parts);
        }
    }

    public bool HasProblems => Latest.FixNow > 0;
    public string TopTrend { get; }
    public bool HasTrend => TopTrend.Length > 0;
    public bool TrendIsAct { get; }
    public bool TrendIsWatch => HasTrend && !TrendIsAct;

    public bool Confirming { get => _confirming; set { if (Set(ref _confirming, value)) Raise(nameof(NotConfirming)); } }
    public bool NotConfirming => !_confirming;

    public RelayCommand SelectCommand { get; }
    public RelayCommand AskForgetCommand { get; }
    public RelayCommand KeepCommand { get; }
    public RelayCommand ForgetCommand { get; }
}
