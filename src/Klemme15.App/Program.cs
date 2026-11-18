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

using Avalonia;
using Avalonia.Media;

namespace Klemme15.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Errors the UI thread doesn't catch go to a banner (App). One on another thread ends the process: write it
        // down first, so the next start can offer it for a bug report instead of the app just vanishing.
        var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "";
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Klemme15.Core.CrashReport.Write(ex, Klemme15.Core.CrashReport.DefaultFolder, version, "a background task");
        };
        // A task nobody waited for: note it, but don't let it end the session.
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception e)
        {
            Klemme15.Core.CrashReport.Write(e, Klemme15.Core.CrashReport.DefaultFolder, version, "the window");
            throw;
        }
    }

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions { DefaultFamilyName = "avares://Klemme15/Assets/Fonts#Barlow" })
            .LogToTrace();
}
