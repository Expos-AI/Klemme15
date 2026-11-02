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

using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Klemme15.App.ViewModels;

namespace Klemme15.App.Views;

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
