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

using Klemme15.App.Services;
using Klemme15.Core;

namespace Klemme15.App.ViewModels;

/// <summary>
/// "Share snapshot": the owner writes what's wrong, picks what goes in, sees exactly what that is, and saves a .k15 file
/// to attach to a forum post or GitHub issue. Nothing is uploaded by Klemme15.
/// </summary>
public sealed class SnapshotShareViewModel : ViewModelBase
{
    private readonly SessionState _session;
    private readonly IDialogService _dialogs;
    private readonly string _version;
    private bool _open, _live = true, _tests = true, _coding, _log;
    private string _note = "", _status = "";

    public SnapshotShareViewModel(SessionState session, IDialogService dialogs, string version)
    {
        _session = session;
        _dialogs = dialogs;
        _version = version;
        ToggleCommand = new RelayCommand(() => IsOpen = !IsOpen, () => CanShare);
        SaveCommand = new AsyncCommand(SaveAsync, () => CanShare, e => Status = $"Couldn't save: {e.Message}");
        _session.ScanUpdated += () => OnUi(Refresh);
        _session.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(SessionState.Backend)) OnUi(Refresh); };
    }

    /// <summary>A real or demo car with a quick test: a snapshot can't be re-shared from a snapshot.</summary>
    public bool CanShare => _session.LastScan is not null && !_session.IsSnapshot;

    public bool IsOpen { get => _open; set { if (Set(ref _open, value)) { Raise(nameof(ToggleText)); Refresh(); } } }
    public string ToggleText => _open ? "Close" : "Share snapshot";
    public string Note { get => _note; set => Set(ref _note, value); }
    public bool IncludeLive { get => _live; set { if (Set(ref _live, value)) Refresh(); } }
    public bool IncludeTests { get => _tests; set { if (Set(ref _tests, value)) Refresh(); } }
    public bool IncludeCoding { get => _coding; set { if (Set(ref _coding, value)) Refresh(); } }
    public bool IncludeLog { get => _log; set { if (Set(ref _log, value)) Refresh(); } }
    public string Status { get => _status; private set { if (Set(ref _status, value)) Raise(nameof(HasStatus)); } }
    public bool HasStatus => _status.Length > 0;

    public string LiveText => _session.LiveBuffer.Count > 0
        ? $"Last {Math.Min(60, _session.LiveBuffer.Count / 2):0}+ seconds of live data ({_session.LiveBuffer.Count} readings)"
        : "Live data: none recorded yet (run Live data for a minute first)";
    public string TestsText => _session.TestResults.Count > 0 ? $"{_session.TestResults.Count} test result{(_session.TestResults.Count == 1 ? "" : "s")} with graphs" : "Tests: none run in this session";
    public string CodingText => _session.CodingReads.Count > 0 ? $"Coding of {string.Join(", ", _session.CodingReads.Keys)} (to view and compare; it can't be written to a car from a snapshot)" : "Coding: none read in this session";
    public string Contents => _session.LastScan is { } scan
        ? $"Always in it: {scan.RespondingEcus} modules with part numbers, {scan.TotalFaults} faults with their details, dash messages, the car's story" +
          (scan.Health is not null ? ", health trends" : "") + ", battery voltage. Never in it: the VIN (removed everywhere)."
        : "";

    public RelayCommand ToggleCommand { get; }
    public AsyncCommand SaveCommand { get; }

    private void Refresh()
    {
        foreach (var p in new[] { nameof(CanShare), nameof(LiveText), nameof(TestsText), nameof(CodingText), nameof(Contents) }) Raise(p);
        ToggleCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
        if (!CanShare && _open) IsOpen = false;
    }

    private async Task SaveAsync()
    {
        var snapshot = _session.BuildSnapshot(Note, IncludeLive, IncludeTests, IncludeCoding, IncludeLog, _version);
        var bytes = SnapshotFile.Write(snapshot);
        var name = $"klemme15-{(_session.Model?.Name ?? snapshot.Manifest.Chassis ?? "car").Replace(' ', '-')}-{DateTime.Now:yyyyMMdd-HHmm}{SnapshotFile.Extension}";
        var saved = await _dialogs.SaveBytesAsync("Save snapshot", name, "k15", "Klemme15 snapshot", bytes);
        if (saved is null) return;
        Status = $"Saved {saved} ({bytes.Length / 1024 + 1} KB). Attach it to your forum post or GitHub issue: anyone with Klemme15 can open it in the Garage and explore your car as it was, read-only." +
                 (IncludeLog ? " The traffic log is scrubbed of the VIN, but a VIN split across two messages can slip through: skim it if that matters to you." : "");
    }
}
