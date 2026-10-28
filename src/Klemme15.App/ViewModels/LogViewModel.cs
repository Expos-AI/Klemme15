using System.Collections.ObjectModel;
using Avalonia.Media;
using Klemme15.App.Services;
using Klemme15.Core;

namespace Klemme15.App.ViewModels;

/// <summary>Every byte on the wire, live. Invaluable for bug reports and for decoding new modules.</summary>
public sealed class LogViewModel : PageViewModel
{
    private const int MaxLines = 3000;
    private readonly SessionState _session;
    private readonly IDialogService _dialogs;
    private readonly List<string> _pending = [];
    private bool _flushQueued;
    private bool _paused;

    public LogViewModel(SessionState session, IDialogService dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        session.Log.EntryAdded += OnEntry;
        SaveCommand = new AsyncCommand(SaveAsync);
        ClearCommand = new RelayCommand(Lines.Clear);
    }

    public override string Title => "Log";
    public override Geometry Icon => Icons.Log;

    public ObservableCollection<string> Lines { get; } = [];
    public bool Paused { get => _paused; set => Set(ref _paused, value); }
    public AsyncCommand SaveCommand { get; }
    public RelayCommand ClearCommand { get; }

    private void OnEntry(TrafficEntry entry)
    {
        // Entries arrive on the bus thread, possibly hundreds per second: batch them onto the UI thread.
        lock (_pending)
        {
            _pending.Add(entry.ToString());
            if (_flushQueued) return;
            _flushQueued = true;
        }
        OnUi(Flush);
    }

    private void Flush()
    {
        string[] batch;
        lock (_pending)
        {
            batch = _pending.ToArray();
            _pending.Clear();
            _flushQueued = false;
        }
        if (_paused) return;
        foreach (var line in batch) Lines.Add(line);
        while (Lines.Count > MaxLines) Lines.RemoveAt(0);
    }

    private Task SaveAsync() =>
        _dialogs.SaveTextAsync("Save traffic log", $"klemme15-traffic-{DateTime.Now:yyyyMMdd-HHmm}.txt", "txt", _session.Log.Export());
}
