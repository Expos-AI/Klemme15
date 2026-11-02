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

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using Klemme15.Core;
using Klemme15.Core.GhostHunter;

namespace Klemme15.App.ViewModels;

public enum TileLevel { Normal, Warn, Bad }

/// <summary>One live value pinned to a spot on the car drawing (coolant at the thermostat, voltage at the battery).</summary>
public sealed class TopologyTile(string id, string label, double left, double top, Func<double, TileLevel> judge, string format) : ViewModelBase
{
    private string _value = "–";
    private TileLevel _level;
    private bool _hasValue;

    public string Id { get; } = id;
    public string Label { get; } = label;
    public double Left { get; } = left;
    public double Top { get; } = top;
    public string Unit => SignalLabels.Unit(Id);
    public string Value { get => _value; private set => Set(ref _value, value); }
    public bool HasValue { get => _hasValue; private set => Set(ref _hasValue, value); }
    public TileLevel Level { get => _level; private set { if (Set(ref _level, value)) { Raise(nameof(IsWarn)); Raise(nameof(IsBad)); } } }
    public bool IsWarn => _level == TileLevel.Warn;
    public bool IsBad => _level == TileLevel.Bad;

    public void Show(double? value)
    {
        HasValue = value is not null;
        Value = value is { } v ? v.ToString(format, CultureInfo.CurrentCulture) + (Unit.Length > 0 ? " " + Unit : "") : "–";
        Level = value is { } x ? judge(x) : TileLevel.Normal;
    }
}

/// <summary>
/// Live values on the car drawing. A background loop reads the engine computer as fast as it answers and keeps only the
/// newest value of each signal; a dispatcher timer copies those to the tiles four times a second, so fast polling never
/// floods the UI with redraws. It stops on its own when you leave the page, start a scan, or the Ghost Hunter takes the
/// cable, since one cable can only carry one conversation at a time.
/// </summary>
public sealed class TopologyOverlayViewModel : ViewModelBase
{
    private readonly SessionState _session;
    private readonly ConcurrentDictionary<string, double> _latest = new();
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _isOn;
    private string _status = "";
    private DateTime _lastRead;

    /// <summary>How often the tiles refresh. Reads can come faster; the tiles show the newest.</summary>
    public static readonly TimeSpan UiInterval = TimeSpan.FromMilliseconds(250);

    public TopologyOverlayViewModel(SessionState session)
    {
        _session = session;
        // Positions are on the 1000 x 460 car drawing: front at left, driver's side at the bottom.
        Tiles =
        [
            new("coolant", "Coolant", 112, 112, v => v >= 120 ? TileLevel.Bad : v >= 113 ? TileLevel.Warn : TileLevel.Normal, "0"),
            new("rpm", "Engine", 112, 286, _ => TileLevel.Normal, "0"),
            new("oiltemp", "Oil", 228, 286, v => v >= 150 ? TileLevel.Bad : v >= 135 ? TileLevel.Warn : TileLevel.Normal, "0"),
            new("iat", "Intake air", 228, 112, v => v >= 70 ? TileLevel.Warn : TileLevel.Normal, "0"),
            new("speed", "Road speed", 600, 120, _ => TileLevel.Normal, "0"),
            new("voltage", "Battery", 812, 140, v => v < 11.8 || v > 15.2 ? TileLevel.Bad : v < 12.2 || v > 14.9 ? TileLevel.Warn : TileLevel.Normal, "0.0"),
        ];
        _timer = new DispatcherTimer { Interval = UiInterval };
        _timer.Tick += (_, _) => Push();
        ToggleCommand = new RelayCommand(Toggle, () => CanRun);
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.IsWatching) or nameof(SessionState.Backend) or nameof(SessionState.IsSnapshot))
                OnUi(() => { if (!CanRun) Stop(); ToggleCommand.RaiseCanExecuteChanged(); Raise(nameof(CanRun)); });
        };
    }

    public ObservableCollection<TopologyTile> Tiles { get; }
    public RelayCommand ToggleCommand { get; }

    /// <summary>Set by the page while it's busy with the cable (a scan, clearing faults).</summary>
    public Func<bool> PageBusy { get; set; } = () => false;

    public bool CanRun => _session.IsConnected && !_session.IsSnapshot && !_session.IsWatching && !PageBusy();
    public bool IsOn { get => _isOn; private set { if (Set(ref _isOn, value)) Raise(nameof(ToggleLabel)); } }
    public string ToggleLabel => _isOn ? "Hide live values" : "Show live values";
    public string Status { get => _status; private set => Set(ref _status, value); }

    public void RaiseCanRun() { ToggleCommand.RaiseCanExecuteChanged(); Raise(nameof(CanRun)); }

    private void Toggle() { if (_isOn) Stop(); else Start(); }

    public void Start()
    {
        if (_isOn || !CanRun || _session.EngineSignalSource() is not { } source) { if (_session.EngineSignalSource() is null) Status = "No engine computer to read."; return; }
        _latest.Clear();
        foreach (var t in Tiles) t.Show(null);
        _cts = new CancellationTokenSource();
        var ids = Tiles.Select(t => t.Id).ToList();
        var ct = _cts.Token;
        _loop = Task.Run(() => ReadLoop(source, ids, ct), ct);
        _timer.Start();
        IsOn = true;
        Status = "Reading...";
    }

    public void Stop()
    {
        if (!_isOn) return;
        _cts?.Cancel();
        _cts = null;
        _timer.Stop();
        IsOn = false;
        Status = "";
    }

    /// <summary>Waits until the background reads have stopped, so the cable is free for whatever comes next.</summary>
    public async Task StopAndWaitAsync()
    {
        var loop = _loop;
        Stop();
        if (loop is not null) try { await loop; } catch (OperationCanceledException) { }
    }

    private async Task ReadLoop(ISignalSource source, IReadOnlyList<string> ids, CancellationToken ct)
    {
        var wanted = ids.ToList();
        var minGap = TimeSpan.FromMilliseconds(100);
        while (!ct.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            try
            {
                var values = await source.ReadAsync(wanted, ct);
                foreach (var (k, v) in values) if (double.IsFinite(v)) _latest[k] = v;
                // Stop asking for what this car never reports, after the first full read.
                if (_lastRead == default && values.Count > 0) wanted = wanted.Where(values.ContainsKey).ToList();
                _lastRead = DateTime.UtcNow;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException)
            {
                _session.Log.Note($"Live values on the car drawing: read skipped ({e.Message})");
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            }
            var gap = minGap - (DateTime.UtcNow - started);
            if (gap > TimeSpan.Zero) try { await Task.Delay(gap, ct); } catch (OperationCanceledException) { break; }
        }
        _lastRead = default;
    }

    /// <summary>Copies the newest values to the tiles (UI thread, at most four times a second).</summary>
    private void Push()
    {
        foreach (var t in Tiles) t.Show(_latest.TryGetValue(t.Id, out var v) ? v : null);
        Status = _lastRead == default ? "Reading..." : $"Live · updated {DateTime.UtcNow - _lastRead:s\\.f} s ago";
    }
}
