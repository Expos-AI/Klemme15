using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Klemme15.Core;

namespace Klemme15.App.Controls;

/// <summary>
/// A scatter plot of one channel against another (fuel trim vs RPM, say), with axes and a highlighted point for the
/// current playback position. Points are coloured from blue (early in the log) to orange (late) so a drift shows.
/// A "known good" band can be drawn behind the points: a translucent fill between the healthy 10th and 90th
/// percentiles, and a dashed median line, so you see at a glance where your engine leaves the healthy range.
/// </summary>
public sealed class ScatterPlot : Control
{
    public static readonly StyledProperty<IReadOnlyList<ScatterPoint>?> PointsProperty =
        AvaloniaProperty.Register<ScatterPlot, IReadOnlyList<ScatterPoint>?>(nameof(Points));

    public static readonly StyledProperty<int> HighlightProperty =
        AvaloniaProperty.Register<ScatterPlot, int>(nameof(Highlight), -1);

    public static readonly StyledProperty<IBrush?> AxisBrushProperty =
        AvaloniaProperty.Register<ScatterPlot, IBrush?>(nameof(AxisBrush), Brushes.Gray);

    public static readonly StyledProperty<IReadOnlyList<BaselinePoint>?> BandProperty =
        AvaloniaProperty.Register<ScatterPlot, IReadOnlyList<BaselinePoint>?>(nameof(Band));

    public static readonly StyledProperty<IBrush?> BandBrushProperty =
        AvaloniaProperty.Register<ScatterPlot, IBrush?>(nameof(BandBrush), Brushes.SeaGreen);

    static ScatterPlot() => AffectsRender<ScatterPlot>(PointsProperty, HighlightProperty, AxisBrushProperty, BandProperty, BandBrushProperty);

    public IReadOnlyList<ScatterPoint>? Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public int Highlight { get => GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }
    public IBrush? AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public IReadOnlyList<BaselinePoint>? Band { get => GetValue(BandProperty); set => SetValue(BandProperty, value); }
    public IBrush? BandBrush { get => GetValue(BandBrushProperty); set => SetValue(BandBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        var points = Points;
        var b = Bounds;
        if (points is null || points.Count == 0 || b.Width < 20 || b.Height < 20) return;

        const double pad = 4;
        double xMin = points.Min(p => p.X), xMax = points.Max(p => p.X);
        double yMin = points.Min(p => p.Y), yMax = points.Max(p => p.Y);
        // Only the stretch of the band your log covers, so a band to 6,500 rpm doesn't squash an idle log into a corner.
        var band = Band?.Where(p => p.X >= xMin - 1e-9 && p.X <= xMax + 1e-9).OrderBy(p => p.X).ToList() ?? [];
        if (band.Count > 0) { yMin = Math.Min(yMin, band.Min(p => p.P10)); yMax = Math.Max(yMax, band.Max(p => p.P90)); }
        if (xMax - xMin < 1e-9) { xMin -= 1; xMax += 1; }
        if (yMax - yMin < 1e-9) { yMin -= 1; yMax += 1; }
        var w = b.Width - 2 * pad;
        var h = b.Height - 2 * pad;

        Point Map(double x, double y) => new(pad + (x - xMin) / (xMax - xMin) * w, pad + h - (y - yMin) / (yMax - yMin) * h);

        var axis = new Pen(AxisBrush ?? Brushes.Gray, 1);
        // A zero line for the Y axis (fuel trims read against zero), when zero is in range.
        if (yMin < 0 && yMax > 0)
        {
            var zeroY = Map(xMin, 0).Y;
            context.DrawLine(axis, new Point(pad, zeroY), new Point(pad + w, zeroY));
        }

        if (band.Count > 0) DrawBand(context, band, Map);

        var max = points.Count - 1;
        for (var i = 0; i < points.Count; i++)
        {
            var p = Map(points[i].X, points[i].Y);
            var t = max == 0 ? 0 : (double)i / max;
            var color = Color.FromRgb((byte)(70 + 150 * t), (byte)(140 - 40 * t), (byte)(200 - 150 * t));
            context.DrawEllipse(new SolidColorBrush(color, 0.55), null, p, 2.2, 2.2);
        }

        if (Highlight >= 0 && points.FirstOrDefault(p => p.Sample == Highlight) is { Sample: >= 0 } hit)
        {
            var p = Map(hit.X, hit.Y);
            context.DrawEllipse(null, new Pen(Brushes.White, 2), p, 5, 5);
        }
    }

    private void DrawBand(DrawingContext context, IReadOnlyList<BaselinePoint> band, Func<double, double, Point> map)
    {
        var colour = (BandBrush as ISolidColorBrush)?.Color ?? Colors.SeaGreen;
        if (band.Count == 1)
        {
            var top = map(band[0].X, band[0].P90);
            var bottom = map(band[0].X, band[0].P10);
            context.DrawLine(new Pen(new SolidColorBrush(colour, 0.35), 8), top, bottom);
            return;
        }
        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.BeginFigure(map(band[0].X, band[0].P90), true);
            foreach (var p in band.Skip(1)) g.LineTo(map(p.X, p.P90));
            foreach (var p in band.Reverse()) g.LineTo(map(p.X, p.P10));
            g.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(colour, 0.18), null, fill);

        var median = new StreamGeometry();
        using (var g = median.Open())
        {
            g.BeginFigure(map(band[0].X, band[0].Median), false);
            foreach (var p in band.Skip(1)) g.LineTo(map(p.X, p.Median));
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(colour, 0.8), 1.5, new DashStyle([4, 3], 0)), median);
    }
}
