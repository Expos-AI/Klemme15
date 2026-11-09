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

using Avalonia.Media;
using Klemme15.App.Services;

namespace Klemme15.App.ViewModels;

/// <summary>The About dialog: what Klemme15 is, its version, and links out. Read-only.</summary>
public sealed class AboutViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;

    public AboutViewModel(string version, IDialogService dialogs)
    {
        _dialogs = dialogs;
        Version = version;
        ProjectCommand = new RelayCommand(() => _ = _dialogs.OpenUrlAsync(Links.Project));
        SupportCommand = new RelayCommand(() => _ = _dialogs.OpenUrlAsync(Links.Support));
        PrivacyCommand = new RelayCommand(() => _ = _dialogs.OpenUrlAsync(Links.Privacy));
        CoffeeCommand = new RelayCommand(() => _ = _dialogs.OpenUrlAsync(Links.Coffee));
    }

    public string Version { get; }
    public Geometry Logo => Icons.Scan; // placeholder; the real logo mark is drawn by the control

    public string Tagline => "Free, open-source diagnostics for older BMWs.";
    public string Blurb =>
        "Read and clear every module's faults, watch and record live data, run guided tests and service functions, " +
        "and back up before you code. It works with a K+DCAN cable on Windows, macOS and Linux, without dealer software.";
    public string Confirmed =>
        "Validated on a 2006 E90 325i over D-CAN. Other models, service functions and coding are developed from published research " +
        "on each module and verified against simulated vehicles; field validation on more models is in progress, and test reports are welcome.";
    public string Independent =>
        "Independent project, not affiliated with or endorsed by BMW AG. BMW, ISTA, INPA, NCS Expert and EDIABAS are trademarks of BMW AG.";
    public string Author => "Created and maintained by Kindric Castro";
    public string Copyright => "Copyright (C) 2026 Kindric Castro";
    public string License => "Licensed under GPL-3.0-or-later. Your data stays on your computer: see the privacy policy.";

    public bool HasCoffee => !string.IsNullOrWhiteSpace(Links.Coffee);

    public RelayCommand ProjectCommand { get; }
    public RelayCommand SupportCommand { get; }
    public RelayCommand PrivacyCommand { get; }
    public RelayCommand CoffeeCommand { get; }
}
