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

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Klemme15.Core;

namespace Klemme15.App.Controls;

/// <summary>
/// Line chart for a guided test: value over seconds, one shared scale. A target and its actual share a colour; the target
/// is dashed. Draws its own legend, the scale at the left and the time underneath.
/// </summary>
public sealed class TraceChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<TestTrace>?> TracesProperty =
        AvaloniaProperty.Register<TraceChart, IReadOnlyList<TestTrace>?>(nameof(Traces));
    public static readonly StyledProperty<IBrush?> PrimaryProperty = AvaloniaProperty.Register<TraceChart, IBrush?>(nameof(Primary), Brushes.SkyBlue);
    public static readonly StyledProperty<IBrush?> SecondaryProperty = AvaloniaProperty.Register<TraceChart, IBrush?>(nameof(Secondary), Brushes.Orange);
    public static readonly StyledProperty<IBrush?> TertiaryProperty = AvaloniaProperty.Register<TraceChart, IBrush?>(nameof(Tertiary), Brushes.SteelBlue);
    public static readonly StyledProperty<IBrush?> GridProperty = AvaloniaProperty.Register<TraceChart, IBrush?>(nameof(Grid), Brushes.Gray);
    public static readonly StyledProperty<IBrush?> LabelProperty = AvaloniaProperty.Register<TraceChart, IBrush?>(nameof(Label), Brushes.Gray);

    static TraceChart() => AffectsRender<TraceChart>(TracesProperty, PrimaryProperty, SecondaryProperty, TertiaryProperty, GridProperty, LabelProperty);

    public IReadOnlyList<TestTrace>? Traces { get => GetValue(TracesProperty); set => SetValue(TracesProperty, value); }
    public IBrush? Primary { get => GetValue(PrimaryProperty); set => SetValue(PrimaryProperty, value); }
    public IBrush? Secondary { get => GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public IBrush? Tertiary { get => GetValue(TertiaryProperty); set => SetValue(TertiaryProperty, value); }
    public IBrush? Grid { get => GetValue(GridProperty); set => SetValue(GridProperty, value); }
    public IBrush? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    private static readonly string[] Roles = ["target", "actual", "requested", "commanded"];

    /// <summary>"Intake target" and "Intake actual" → "intake", so they get the same colour.</summary>
    public static string Family(string name)
    {
        var words = name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Roles.Contains(w)).ToArray();
        return words.Length == 0 ? "pump" : string.Join(' ', words);
    }

    private FormattedText Text(string s, IBrush? brush) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, brush);

    public override void Render(DrawingContext context)
    {
        var traces = Traces;
        var b = Bounds;
        if (traces is null || traces.Count == 0 || b.Width < 60 || b.Height < 60) return;
        var all = traces.SelectMany(t => t.Values).ToList();
        var times = traces.SelectMany(t => t.Seconds).ToList();
        if (all.Count == 0) return;
        double min = all.Min(), max = all.Max();
        if (min > 0 && min < (max - min) * 0.6) min = 0;
        if (max - min < 1e-6) { min -= 1; max += 1; }
        var pad = (max - min) * 0.08; max += pad; if (min != 0) min -= pad;
        double t0 = times.Min(), t1 = Math.Max(times.Max(), t0 + 1);

        // Legend (one line per family, drawn left to right).
        var families = traces.Select(t => Family(t.Name)).Distinct().ToList();
        IBrush? ColourOf(string family) => (families.IndexOf(family) % 3) switch { 0 => Primary, 1 => Secondary, _ => Tertiary };
        double lx = 46, legendH = 18;
        foreach (var t in traces)
        {
            var brush = ColourOf(Family(t.Name));
            var pen = new Pen(brush, 2, t.Target ? new DashStyle([3, 2], 0) : null);
            var label = Text(t.Name, Label);
            if (lx + 26 + label.Width > b.Width) break;
            context.DrawLine(pen, new Point(lx, 8), new Point(lx + 18, 8));
            context.DrawText(label, new Point(lx + 22, 8 - label.Height / 2));
            lx += 22 + label.Width + 16;
        }

        var plot = new Rect(46, legendH + 6, b.Width - 54, b.Height - legendH - 6 - 20);
        var gridPen = new Pen(Grid, 1);
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4;
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var v = min + (max - min) * i / 4;
            var label = Text(Math.Abs(max - min) >= 20 ? v.ToString("0", CultureInfo.CurrentCulture) : v.ToString("0.0", CultureInfo.CurrentCulture), Label);
            context.DrawText(label, new Point(plot.Left - 6 - label.Width, y - label.Height / 2));
        }
        foreach (var s in new[] { t0, (t0 + t1) / 2, t1 })
        {
            var label = Text($"{s - t0:0} s", Label);
            var x = plot.Left + (s - t0) / (t1 - t0) * plot.Width;
            context.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, plot.Left, plot.Right - label.Width), plot.Bottom + 4));
        }

        Point P(double t, double v) => new(plot.Left + (t - t0) / (t1 - t0) * plot.Width, plot.Bottom - (v - min) / (max - min) * plot.Height);
        foreach (var t in traces)
        {
            if (t.Values.Count == 0) continue;
            var pen = new Pen(ColourOf(Family(t.Name)), t.Target ? 1.5 : 2, t.Target ? new DashStyle([3, 2], 0) : null);
            if (t.Values.Count == 1) { context.DrawEllipse(pen.Brush, null, P(t.Seconds[0], t.Values[0]), 2.5, 2.5); continue; }
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(P(t.Seconds[0], t.Values[0]), false);
                for (var i = 1; i < t.Values.Count; i++) ctx.LineTo(P(t.Seconds[i], t.Values[i]));
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, pen, g);
        }
    }
}
