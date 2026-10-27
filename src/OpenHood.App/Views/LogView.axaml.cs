using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using OpenHood.App.ViewModels;

namespace OpenHood.App.Views;

public partial class LogView : UserControl
{
    private LogViewModel? _vm;

    public LogView() => InitializeComponent();

    // Subscribe only while visible, so views from earlier page visits don't keep listening.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm = DataContext as LogViewModel;
        if (_vm is not null) _vm.Lines.CollectionChanged += OnLinesChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_vm is not null) _vm.Lines.CollectionChanged -= OnLinesChanged;
        _vm = null;
        base.OnDetachedFromVisualTree(e);
    }

    // Keep the newest traffic in view.
    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var vm = _vm;
        if (e.Action != NotifyCollectionChangedAction.Add || vm is null || vm.Lines.Count == 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (vm.Lines.Count > 0) LogList.ScrollIntoView(vm.Lines.Count - 1);
        }, DispatcherPriority.Background);
    }
}
