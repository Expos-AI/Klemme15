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
using Avalonia.Controls;
using Avalonia.Media;

namespace Klemme15.App.Controls;

/// <summary>Tiny line chart for live values. Scales to its own min/max so small changes stay visible.</summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke), Brushes.DodgerBlue);

    static Sparkline() => AffectsRender<Sparkline>(ValuesProperty, StrokeProperty);

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var values = Values;
        var bounds = Bounds;
        if (values is null || values.Count < 2 || bounds.Width < 4 || bounds.Height < 4) return;

        double min = values.Min(), max = values.Max();
        if (max - min < 1e-9) { min -= 1; max += 1; }
        var stepX = bounds.Width / (values.Count - 1);
        var pad = 2.0;
        var h = bounds.Height - 2 * pad;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < values.Count; i++)
            {
                var p = new Point(i * stepX, pad + h - (values[i] - min) / (max - min) * h);
                if (i == 0) ctx.BeginFigure(p, false);
                else ctx.LineTo(p);
            }
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(Stroke, 1.5), geometry);
    }
}
