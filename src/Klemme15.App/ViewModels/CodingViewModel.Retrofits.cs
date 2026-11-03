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
using Klemme15.Core;

namespace Klemme15.App.ViewModels;

/// <summary>A retrofit the person can pick ("What did you fit?").</summary>
public sealed class RetrofitOption : ViewModelBase
{
    private bool _selected;

    public RetrofitOption(RetrofitGuide guide, Action<RetrofitOption> select)
    {
        Guide = guide;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public RetrofitGuide Guide { get; }
    public string Title => Guide.Title;
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
    public RelayCommand SelectCommand { get; }
}

/// <summary>Something the checker noticed, with a shortcut to the matching guide.</summary>
public sealed class RetrofitFindingItem(RetrofitFinding finding, RelayCommand? open)
{
    public string Title => finding.Title;
    public string Detail => finding.Detail;
    public RelayCommand? OpenCommand { get; } = open;
    public bool HasGuide => OpenCommand is not null;
}

public sealed partial class CodingViewModel
{
    public ObservableCollection<RetrofitFindingItem> RetrofitFindings { get; } = [];
    public ObservableCollection<RetrofitOption> RetrofitOptions { get; } = [];
    public bool HasRetrofitFindings => RetrofitFindings.Count > 0;

    private RetrofitOption? _retrofit;
    public RetrofitGuide? Retrofit => _retrofit?.Guide;
    public bool HasRetrofit => _retrofit is not null;
    public string RetrofitTitle => Retrofit?.Title ?? "";
    public string RetrofitSummary => Retrofit?.Summary ?? "";
    public IReadOnlyList<string> RetrofitNeeds => Retrofit?.Needs ?? [];
    public IReadOnlyList<string> RetrofitHelps => Retrofit?.Klemme15 ?? [];
    public IReadOnlyList<SourceLink> RetrofitSources => Retrofit?.Sources.Select(u => new SourceLink(u, url => _ = _dialogs.OpenUrlAsync(url))).ToList() ?? [];

    private string _retrofitCheck = "";
    public string RetrofitCheckText { get => _retrofitCheck; private set => Set(ref _retrofitCheck, value); }
    public RelayCommand CheckRetrofitCommand { get; private set; } = null!;

    private void InitRetrofits()
    {
        foreach (var g in _session.Database.Retrofits)
        {
            var option = new RetrofitOption(g, SelectRetrofit);
            RetrofitOptions.Add(option);
        }
        CheckRetrofitCommand = new RelayCommand(CheckRetrofit, () => _retrofit is not null);
        RebuildRetrofits();
    }

    private void SelectRetrofit(RetrofitOption option)
    {
        foreach (var o in RetrofitOptions) o.IsSelected = o == option;
        _retrofit = option;
        RetrofitCheckText = "";
        foreach (var p in new[] { nameof(Retrofit), nameof(HasRetrofit), nameof(RetrofitTitle), nameof(RetrofitSummary), nameof(RetrofitNeeds), nameof(RetrofitHelps), nameof(RetrofitSources) }) Raise(p);
        CheckRetrofitCommand.RaiseCanExecuteChanged();
        if (_session.LastScan is not null) CheckRetrofit();
    }

    private void CheckRetrofit()
    {
        if (Retrofit is not { } guide) return;
        if (_session.LastScan is not { } scan) { RetrofitCheckText = "Run a quick test after fitting, then check again."; return; }
        var result = RetrofitAdvisor.Check(guide, scan);
        RetrofitCheckText = result.Clean
            ? $"Looks finished: the last quick test ({scan.Started.LocalDateTime:t}) shows none of the dash messages or faults that would say otherwise. Drive it, then run a quick test again to be sure."
            : "Not finished yet. The last quick test still shows:\n• " + string.Join("\n• ", result.Remaining);
    }

    private void RebuildRetrofits()
    {
        RetrofitFindings.Clear();
        if (_session.LastScan is { } scan && !_session.IsSnapshot)
        {
            IReadOnlyList<HealthSnapshot> history = [];
            try { if (HealthSnapshot.CarKeyFor(scan) is { } key) history = _session.History.Load(key); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            foreach (var f in RetrofitAdvisor.Detect(scan, _session.Model, history, _session.Database.Retrofits))
            {
                var option = f.GuideId is { } id ? RetrofitOptions.FirstOrDefault(o => o.Guide.Id == id) : null;
                RetrofitFindings.Add(new RetrofitFindingItem(f, option is null ? null : new RelayCommand(() => SelectRetrofit(option))));
            }
        }
        Raise(nameof(HasRetrofitFindings));
        if (_retrofit is not null) CheckRetrofit();
    }
}
