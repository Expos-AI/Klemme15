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

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Klemme15.App.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}

/// <summary>
/// Async command that disables itself while running and reports errors through a callback. A command without its own
/// callback reports to <see cref="DefaultError"/> (a banner in the main window), so no error ever closes the app.
/// </summary>
public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null) : ICommand
{
    /// <summary>Where errors go when a command has no callback of its own. Set by the app at start.</summary>
    public static Action<Exception>? DefaultError { get; set; }

    private bool _running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        RaiseCanExecuteChanged();
        try { await execute(); }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (onError is not null) onError(e);
            else if (DefaultError is not null) DefaultError(e);
            else System.Diagnostics.Trace.TraceError($"Unhandled command error: {e}");
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>A screen in the sidebar.</summary>
public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }
    public abstract Geometry Icon { get; }

    /// <summary>Called whenever the page becomes visible.</summary>
    public virtual void OnShown() { }

    /// <summary>Called when another page is shown instead.</summary>
    public virtual void OnHidden() { }
}

public static class Icons
{
    public static readonly Geometry Garage = StreamGeometry.Parse("M12 2 2 9v13h4V12h12v10h4V9zM8 14h8v2H8zm0 4h8v2H8z");
    public static readonly Geometry Car = StreamGeometry.Parse("M8 1h8a4 4 0 0 1 4 4v14a4 4 0 0 1-4 4H8a4 4 0 0 1-4-4V5a4 4 0 0 1 4-4zm-1 6 1 4h8l1-4zm1 10v3h8v-3z");
    public static readonly Geometry Theme = StreamGeometry.Parse("M12 2a10 10 0 1 0 0 20 10 10 0 1 0 0-20zm0 2v16a8 8 0 0 1 0-16z");
    public static readonly Geometry Connect = StreamGeometry.Parse("M7 2h2v5h6V2h2v5h2v4a7 7 0 0 1-6 6.9V22h-2v-4.1A7 7 0 0 1 5 11V7h2z");
    public static readonly Geometry Scan = StreamGeometry.Parse("M10 2a8 8 0 0 1 6.3 12.9l5.4 5.4-1.4 1.4-5.4-5.4A8 8 0 1 1 10 2zm0 2a6 6 0 1 0 0 12 6 6 0 0 0 0-12z");
    public static readonly Geometry Faults = StreamGeometry.Parse("M12 2 1 21h22zm-1 7h2v6h-2zm0 8h2v2h-2z");
    public static readonly Geometry Readings = StreamGeometry.Parse("M12 3a10 10 0 0 1 10 10c0 2.4-.8 4.6-2.3 6.3l-1.5-1.3A8 8 0 1 0 5.8 18l-1.5 1.3A10 10 0 0 1 12 3zm4.6 4.6 1.4 1.4-4.3 4.3a2 2 0 1 1-1.4-1.4z");
    public static readonly Geometry Live = StreamGeometry.Parse("M3 20h18v2H3zM5 10h3v8H5zm5-6h3v14h-3zm5 9h3v5h-3z");
    public static readonly Geometry Service = StreamGeometry.Parse("M14.7 6.3a4 4 0 0 0-5.2 5.2L3 18l3 3 6.5-6.5a4 4 0 0 0 5.2-5.2l-2.5 2.5-2.1-.6-.6-2.1z");
    // A ghost: the Ghost Hunter catches faults that come and go.
    public static readonly Geometry GhostHunter = StreamGeometry.Parse("M12 2a8 8 0 0 0-8 8v12l2.7-2 2.6 2L12 20l2.7 2 2.6-2 2.7 2V10a8 8 0 0 0-8-8zm-3 7a1.5 1.5 0 1 1 0 3 1.5 1.5 0 0 1 0-3zm6 0a1.5 1.5 0 1 1 0 3 1.5 1.5 0 0 1 0-3z");
    public static readonly Geometry Tests = StreamGeometry.Parse("M2 11h4l2-5 4 12 3-9 2 2h5v2h-6l-1-1-3 9-4-12-1 2H2z");
    public static readonly Geometry Expert = StreamGeometry.Parse("M2 4h20v16H2zm2 2v12h16V6zM6.4 8 11 12l-4.6 4L5 14.6 8 12 5 9.4zM12 15h6v2h-6z");
    public static readonly Geometry Info = StreamGeometry.Parse("M12 2a10 10 0 1 0 0 20 10 10 0 1 0 0-20zm-1 5h2v2h-2zm0 4h2v6h-2z");
    public static readonly Geometry Support = StreamGeometry.Parse("M12 2a10 10 0 1 0 0 20 10 10 0 1 0 0-20zm0 15.5a1.2 1.2 0 1 0 0 2.4 1.2 1.2 0 0 0 0-2.4zm0-11c-2 0-3.4 1.2-3.6 3.1l1.9.3c.1-1 .7-1.6 1.6-1.6.9 0 1.5.5 1.5 1.3 0 .7-.3 1-1.2 1.6-1 .7-1.6 1.4-1.5 2.8h1.9c0-.8.3-1.2 1.2-1.8 1-.7 1.7-1.5 1.7-2.8 0-1.8-1.5-2.9-3.5-2.9z");
    public static readonly Geometry Privacy = StreamGeometry.Parse("M12 2 4 5v6c0 5 3.4 8.6 8 11 4.6-2.4 8-6 8-11V5zm0 2.2 6 2.2V11c0 3.8-2.5 6.7-6 8.7-3.5-2-6-4.9-6-8.7V6.4z");
    public static readonly Geometry Coffee = StreamGeometry.Parse("M4 8h14v5a5 5 0 0 1-5 5H9a5 5 0 0 1-5-5zm14 0h2a3 3 0 0 1 0 6h-2zM7 2h2v3H7zm4 0h2v3h-2zM3 21h16v2H3z");
    public static readonly Geometry Analyse = StreamGeometry.Parse("M3 3v18h18v-2H5V3zm4 11 3-3 3 3 5-6-1.4-1.4L15 11l-3-3-5 5z");
    public static readonly Geometry Coding = StreamGeometry.Parse("M9 2v2H7a2 2 0 0 0-2 2v2H3v2h2v2H3v2h2v2a2 2 0 0 0 2 2h2v2h2v-2h2v2h2v-2h2a2 2 0 0 0 2-2v-2h2v-2h-2v-2h2V8h-2V6a2 2 0 0 0-2-2h-2V2h-2v2h-2V2zM7 6h10v12H7zm2 3v6h6V9z");
    public static readonly Geometry Log = StreamGeometry.Parse("M4 5h16v2H4zm0 6h16v2H4zm0 6h10v2H4z");
}

/// <summary>Turns an unexpected error into one plain sentence with a next step.</summary>
public static class Problems
{
    public static string Describe(Exception e) => e switch
    {
        UnauthorizedAccessException => $"Klemme15 isn't allowed to use that file or folder. Pick another place (your Documents folder works). ({e.Message})",
        IOException io when io.Message.Contains("being used", StringComparison.OrdinalIgnoreCase) || io.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021) =>
            "That file is open in another program. Close it there and try again.",
        FileNotFoundException or DirectoryNotFoundException => "That file or folder isn't there any more. It may have been moved or deleted.",
        IOException => $"The file couldn't be read or saved: {e.Message}",
        System.Text.Json.JsonException => "That file isn't one Klemme15 can read: it's damaged or from another program.",
        TimeoutException => "The car didn't answer in time. Check the cable is plugged in and the ignition is on, then try again.",
        Klemme15.Core.DiagnosticException => e.Message,
        _ => $"Something went wrong: {e.Message}",
    };
}
