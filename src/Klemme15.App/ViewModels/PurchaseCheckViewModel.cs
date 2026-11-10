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
/// <summary>One row of the mileage table: a module, what its figure is, and the figure.</summary>
public sealed class MileageRowViewModel(MileageRecord record, bool miles, long? odometerKm)
{
    public string Module => record.Module;
    public string What => record.What + (record.Kind == MileageKind.Reference ? " (shown only, pending validation)" : "");
    public string Figure => miles ? $"{record.Km / 1.609344:N0} mi" : $"{record.Km:N0} km";
    public string Kind => record.Kind switch
    {
        MileageKind.Current => "Running count",
        MileageKind.Programmed => "At programming",
        MileageKind.Event => "Record",
        _ => "Reference",
    };
    public bool AboveOdometer => record.CountsForVerdict && odometerKm is { } o && record.Km > o + MileageAnalysis.Tolerance;
}

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
        ReadMileageCommand = new AsyncCommand(ReadMileageAsync,
            () => _session.LastScan is not null && _session.IsConnected && !_session.IsSnapshot && _session.BusyWith is null && !_reading,
            e => MileageStatus = $"Couldn't read the mileage: {e.Message}");
        _session.ScanUpdated += () => OnUi(() =>
        {
            _stored = null;
            MileageStatus = "";
            ToggleCommand.RaiseCanExecuteChanged(); SaveCommand.RaiseCanExecuteChanged(); ReadMileageCommand.RaiseCanExecuteChanged();
            if (_open) Refresh();
        });
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.BusyWith) or nameof(SessionState.IsConnected)) OnUi(ReadMileageCommand.RaiseCanExecuteChanged);
        };
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

    /// <summary>Reads the mileage each module remembers (read-only), for the mileage table and the verdict.</summary>
    public AsyncCommand ReadMileageCommand { get; }
    public ObservableCollection<MileageRowViewModel> Mileage { get; } = [];
    public bool HasMileage => Mileage.Count > 0;
    public string MileageStatus { get => _mileageStatus; private set { if (Set(ref _mileageStatus, value)) Raise(nameof(HasMileageStatus)); } }
    public bool HasMileageStatus => _mileageStatus.Length > 0;
    private string _mileageStatus = "";
    private IReadOnlyList<MileageRecord>? _stored;
    private bool _reading;

    private async Task ReadMileageAsync()
    {
        if (_session.LastScan is not { } scan) return;
        _reading = true;
        ReadMileageCommand.RaiseCanExecuteChanged();
        try
        {
            var answered = scan.Ecus.Where(e => e.Status is EcuScanStatus.Ok or EcuScanStatus.Faults).Select(e => e.Ecu).ToList();
            if (_session.IsDemo)
                _stored = DemoMileage(scan, answered);
            else
            {
                using var bus = _session.ClaimBus("Reading the mileage");
                var progress = new Progress<string>(t => MileageStatus = t);
                _stored = await MileageReader.ReadAsync(_session.Backend, _session.Readings, answered, progress);
            }
            var counts = _stored.Count(r => r.Kind == MileageKind.Current);
            MileageStatus = _stored.Count == 0
                ? (_session.Backend is IJobRunner ? "No module reported a mileage figure." : "Without your BMW files only some engine computers report their mileage, and this one didn't. Connect in EDIABAS mode for every module's figures.")
                : $"Read {_stored.Count} figure{(_stored.Count == 1 ? "" : "s")} from the modules, including {counts} running count{(counts == 1 ? "" : "s")}." + (_session.IsDemo ? " Demo car: the figures are made up." : "");
            Refresh();
        }
        finally
        {
            _reading = false;
            ReadMileageCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Made-up figures for the demo car, consistent with its own records.</summary>
    private static IReadOnlyList<MileageRecord> DemoMileage(VehicleScanResult scan, IReadOnlyList<EcuDefinition> answered)
    {
        var top = MileageAnalysis.FromScan(scan).Select(r => r.Km).DefaultIfEmpty(148_000).Max();
        var list = new List<MileageRecord>();
        if (answered.FirstOrDefault(e => e.IsEngine) is { } dme) list.Add(new(dme.Name, "Engine computer's own mileage count", MileageKind.Current, top + 1_240));
        if (answered.FirstOrDefault(e => e.Id.EndsWith(".KOMBI", StringComparison.Ordinal) || e.Id.EndsWith(".IKE", StringComparison.Ordinal)) is { } kombi)
            list.Add(new(kombi.Name, "Cluster's stored mileage", MileageKind.Current, top + 1_220));
        var step = 0;
        foreach (var e in answered.Take(6)) list.Add(new(e.Name, "Mileage when last programmed", MileageKind.Programmed, Math.Max(1_000, top - 60_000 + 7_300 * step++)));
        return list;
    }

    private long? OdometerKm =>
        double.TryParse(_odometer.Replace(",", "").Replace(".", "").Replace(" ", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0
            ? (long)Math.Round(_miles ? v * 1.609344 : v) : null;

    private PurchaseReport? Report() => _session.LastScan is { } scan ? PurchaseCheck.Assess(scan, _session.Model, OdometerKm, _stored, _miles) : null;

    private void Refresh()
    {
        Items.Clear();
        NotChecked.Clear();
        if (Report() is not { } report) { Headline = ""; return; }
        foreach (var i in report.Items) Items.Add(new PurchaseItemViewModel(i));
        foreach (var n in report.NotChecked) NotChecked.Add(n);
        Mileage.Clear();
        foreach (var r in report.Mileage ?? []) Mileage.Add(new MileageRowViewModel(r, _miles, OdometerKm));
        Raise(nameof(HasMileage));
        Headline = report.Headline;
    }

    private async Task SaveAsync()
    {
        if (_session.LastScan is not { } scan || Report() is not { } report) return;
        await _dialogs.SaveTextAsync("Save buyer's report", $"klemme15-buyers-report-{scan.Started:yyyyMMdd-HHmm}.html", "html", ReportWriter.ToHtml(scan with { Vin = null }, report));
    }
}
