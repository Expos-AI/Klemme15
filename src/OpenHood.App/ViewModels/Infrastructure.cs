using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace OpenHood.App.ViewModels;

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

/// <summary>Async command that disables itself while running and reports errors through a callback.</summary>
public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null) : ICommand
{
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
            if (onError is null) throw;
            onError(e);
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
}

public static class Icons
{
    public static readonly Geometry Garage = StreamGeometry.Parse("M12 2 2 9v13h4V12h12v10h4V9zM8 14h8v2H8zm0 4h8v2H8z");
    public static readonly Geometry Car = StreamGeometry.Parse("M8 1h8a4 4 0 0 1 4 4v14a4 4 0 0 1-4 4H8a4 4 0 0 1-4-4V5a4 4 0 0 1 4-4zm-1 6 1 4h8l1-4zm1 10v3h8v-3z");
    public static readonly Geometry Theme = StreamGeometry.Parse("M12 2a10 10 0 1 0 0 20 10 10 0 1 0 0-20zm0 2v16a8 8 0 0 1 0-16z");
    public static readonly Geometry Connect = StreamGeometry.Parse("M7 2h2v5h6V2h2v5h2v4a7 7 0 0 1-6 6.9V22h-2v-4.1A7 7 0 0 1 5 11V7h2z");
    public static readonly Geometry Scan = StreamGeometry.Parse("M10 2a8 8 0 0 1 6.3 12.9l5.4 5.4-1.4 1.4-5.4-5.4A8 8 0 1 1 10 2zm0 2a6 6 0 1 0 0 12 6 6 0 0 0 0-12z");
    public static readonly Geometry Faults = StreamGeometry.Parse("M12 2 1 21h22zm-1 7h2v6h-2zm0 8h2v2h-2z");
    public static readonly Geometry Live = StreamGeometry.Parse("M3 20h18v2H3zM5 10h3v8H5zm5-6h3v14h-3zm5 9h3v5h-3z");
    public static readonly Geometry Expert = StreamGeometry.Parse("M2 4h20v16H2zm2 2v12h16V6zM6.4 8 11 12l-4.6 4L5 14.6 8 12 5 9.4zM12 15h6v2h-6z");
    public static readonly Geometry Log = StreamGeometry.Parse("M4 5h16v2H4zm0 6h16v2H4zm0 6h10v2H4z");
}
