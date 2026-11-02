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
using Klemme15.Core.Coding;
using Klemme15.Data;

namespace Klemme15.App.ViewModels;

/// <summary>One coding map in the library: which exact module it's for, its options, where it came from.</summary>
public sealed class CodingMapItem(CodingMapEntry entry, bool fitsYourCar)
{
    private VerifiedCoding Map => entry.Map;
    public string Title => $"{Map.Module} · {Map.Chassis}";
    public string Detail => $"Part {Map.PartNumber} · coding index {Map.CodingIndex}";
    public string Options => string.Join(", ", Map.Toggles.Keys.Select(id =>
        CodingToggles.All.FirstOrDefault(t => t.Id == id)?.Title.Split(':')[0] ?? id).Order());
    public string Origin => entry.Origin switch
    {
        CodingMapOrigin.Shipped => "Comes with Klemme15",
        CodingMapOrigin.Downloaded => "Downloaded community map",
        _ => "Yours: verified, learned or imported on this computer",
    };
    public bool FitsYourCar { get; } = fitsYourCar;
}

public sealed partial class CodingViewModel
{
    public ObservableCollection<CodingMapItem> Maps { get; } = [];
    public bool HasMaps => Maps.Count > 0;
    public bool NoMaps => Maps.Count == 0;

    private string _libraryStatus = "";
    public string LibraryStatus { get => _libraryStatus; private set { if (Set(ref _libraryStatus, value)) Raise(nameof(HasLibraryStatus)); } }
    public bool HasLibraryStatus => _libraryStatus.Length > 0;

    public string LibraryHeadline
    {
        get
        {
            var fits = Maps.Count(m => m.FitsYourCar);
            return Maps.Count == 0 ? "No coding maps yet"
                : $"{Maps.Count} coding map{(Maps.Count == 1 ? "" : "s")}" + (_session.LastScan is null ? "" : fits == 0 ? " · none fits your modules yet" : $" · {fits} fit{(fits == 1 ? "s" : "")} your car");
        }
    }

    public AsyncCommand DownloadMapsCommand { get; private set; } = null!;
    public AsyncCommand ImportMapCommand { get; private set; } = null!;

    private void InitLibrary()
    {
        DownloadMapsCommand = new AsyncCommand(DownloadMapsAsync, () => !_busy, e => LibraryStatus = $"Couldn't download: {e.Message}");
        ImportMapCommand = new AsyncCommand(ImportMapAsync, () => !_busy, e => LibraryStatus = $"Couldn't import: {e.Message}");
        RebuildLibrary();
    }

    private void RebuildLibrary()
    {
        Maps.Clear();
        var ecus = _session.LastScan?.Ecus ?? [];
        foreach (var entry in _session.Database.CodingMaps
                     .OrderByDescending(e => ecus.Any(x => e.Map.Matches(x.Ecu, x.Identity)))
                     .ThenBy(e => e.Map.Module).ThenBy(e => e.Map.PartNumber))
            Maps.Add(new CodingMapItem(entry, ecus.Any(x => entry.Map.Matches(x.Ecu, x.Identity))));
        Raise(nameof(HasMaps));
        Raise(nameof(NoMaps));
        Raise(nameof(LibraryHeadline));
    }

    private async Task DownloadMapsAsync()
    {
        LibraryStatus = "Downloading the latest community maps...";
        var count = await _session.Database.DownloadCommunityMapsAsync();
        RebuildLibrary();
        if (_selected is { } m) Select(m);
        LibraryStatus = count == 0
            ? "Downloaded: the community library has no maps yet. Be the first: verify or teach an option, then press \"Share this map\"."
            : $"Downloaded {count} community map{(count == 1 ? "" : "s")}. Maps that fit your modules unlock their options above.";
    }

    private async Task ImportMapAsync()
    {
        var json = await _dialogs.OpenTextAsync("Import a coding map", "Coding map (JSON)", ["*.json", "*.txt"]);
        if (json is null) return;
        var count = _session.Database.ImportCodingMaps(json);
        RebuildLibrary();
        if (_selected is { } m) Select(m);
        LibraryStatus = count == 0
            ? "No usable map in that file. Klemme15 only takes maps with a part number, coding index and clean option bits, for modules it may code."
            : $"Imported {count} map{(count == 1 ? "" : "s")}. As always, Klemme15 backs up the module before using one and puts the original back if anything reads back wrong.";
    }
}
