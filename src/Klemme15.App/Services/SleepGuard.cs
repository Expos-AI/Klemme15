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

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Klemme15.App.Services;

/// <summary>
/// Keeps the computer from going to sleep while a job talks to the car (a service function, a 13-minute bleed, a
/// coding write, the Ghost Hunter on a drive). Windows: the system's own "a task is running" flag. macOS: caffeinate.
/// Linux: systemd-inhibit. The screen may still turn off; the computer and the cable stay awake. Released when the
/// job ends. If none of these is available nothing happens: the job runs as before.
/// </summary>
public static class SleepGuard
{
    private static readonly object Gate = new();
    private static int _holds;
    private static Process? _helper;

    public static IDisposable Hold(string why)
    {
        lock (Gate)
        {
            if (_holds++ == 0) Start(why);
        }
        return new Release();
    }

    private sealed class Release : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 1) return;
            lock (Gate)
            {
                if (--_holds == 0) Stop();
            }
        }
    }

    private const uint EsContinuous = 0x80000000, EsSystemRequired = 0x00000001;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    private static void Start(string why)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Avalonia.Threading.Dispatcher.UIThread.Post(() => SetThreadExecutionState(EsContinuous | EsSystemRequired));
            else if (OperatingSystem.IsMacOS())
                _helper = Process.Start(new ProcessStartInfo("caffeinate", $"-i -w {Environment.ProcessId}") { UseShellExecute = false, CreateNoWindow = true });
            else if (OperatingSystem.IsLinux())
                _helper = Process.Start(new ProcessStartInfo("systemd-inhibit",
                    ["--what=sleep:idle", "--who=Klemme15", $"--why={why}", "--mode=block",
                     // Ends by itself if Klemme15 is gone, so a crash can't keep the computer awake forever.
                     "sh", "-c", $"while kill -0 {Environment.ProcessId} 2>/dev/null; do sleep 5; done"]) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException) { _helper = null; }
    }

    private static void Stop()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Avalonia.Threading.Dispatcher.UIThread.Post(() => SetThreadExecutionState(EsContinuous));
            if (_helper is { HasExited: false } p) p.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException) { }
        finally
        {
            _helper?.Dispose();
            _helper = null;
        }
    }
}
