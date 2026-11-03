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
using Klemme15.App.Services;
using Klemme15.Core;

namespace Klemme15.App.ViewModels;

public sealed class PurchaseItemViewModel(PurchaseItem item)
{
    public string Title => item.Title;
    public string Detail => item.Detail;
    public bool IsWarn => item.Level == CheckLevel.Warn;
    public bool IsAsk => item.Level == CheckLevel.Ask;
    public bool IsGood => item.Level == CheckLevel.Good;
    public bool IsInfo => item.Level == CheckLevel.Info;
}

/// <summary>"Buying this car?": a pre-purchase check from the last quick test, with the odometer the buyer reads off the dash.</summary>
public sealed class PurchaseCheckViewModel : ViewModelBase
{
    private readonly SessionState _session;
    private readonly IDialogService _dialogs;
    private bool _open, _miles;
    private string _odometer = "", _headline = "";

    public PurchaseCheckViewModel(SessionState session, IDialogService dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        ToggleCommand = new RelayCommand(() => IsOpen = !IsOpen, () => _session.LastScan is not null);
        SaveCommand = new AsyncCommand(SaveAsync, () => _session.LastScan is not null);
        _session.ScanUpdated += () => OnUi(() => { ToggleCommand.RaiseCanExecuteChanged(); SaveCommand.RaiseCanExecuteChanged(); if (_open) Refresh(); });
    }

    public bool IsOpen { get => _open; set { if (Set(ref _open, value)) { Raise(nameof(ToggleText)); if (value) Refresh(); } } }
    public string ToggleText => _open ? "Close" : "Buying this car?";
    public string Odometer { get => _odometer; set { if (Set(ref _odometer, value)) Refresh(); } }
    public bool Miles { get => _miles; set { if (Set(ref _miles, value)) Refresh(); } }
    public string Headline { get => _headline; private set => Set(ref _headline, value); }
    public ObservableCollection<PurchaseItemViewModel> Items { get; } = [];
    public ObservableCollection<string> NotChecked { get; } = [];
    public RelayCommand ToggleCommand { get; }
    public AsyncCommand SaveCommand { get; }

    private long? OdometerKm =>
        double.TryParse(_odometer.Replace(",", "").Replace(".", "").Replace(" ", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0
            ? (long)Math.Round(_miles ? v * 1.609344 : v) : null;

    private PurchaseReport? Report() => _session.LastScan is { } scan ? PurchaseCheck.Assess(scan, _session.Model, OdometerKm) : null;

    private void Refresh()
    {
        Items.Clear();
        NotChecked.Clear();
        if (Report() is not { } report) { Headline = ""; return; }
        foreach (var i in report.Items) Items.Add(new PurchaseItemViewModel(i));
        foreach (var n in report.NotChecked) NotChecked.Add(n);
        Headline = report.Headline;
    }

    private async Task SaveAsync()
    {
        if (_session.LastScan is not { } scan || Report() is not { } report) return;
        await _dialogs.SaveTextAsync("Save buyer's report", $"klemme15-buyers-report-{scan.Started:yyyyMMdd-HHmm}.html", "html", ReportWriter.ToHtml(scan with { Vin = null }, report));
    }
}
