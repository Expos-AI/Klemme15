using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Klemme15.Core;

namespace Klemme15.App.Controls;

/// <summary>
/// A tuning-style grid: one value (a fuel trim, say) averaged over two axes (RPM across, load up). Each cell is coloured
/// from green (value at or below the low end) through to red (at or above the high end); empty cells stay blank. The cell
/// shows its number, like a fuel or ignition map in tuning software.
/// </summary>
public sealed class BinMap : Control
{
    public static readonly StyledProperty<Bin[,]?> BinsProperty = AvaloniaProperty.Register<BinMap, Bin[,]?>(nameof(Bins));
    public static readonly StyledProperty<double> LowProperty = AvaloniaProperty.Register<BinMap, double>(nameof(Low), -10);
    public static readonly StyledProperty<double> HighProperty = AvaloniaProperty.Register<BinMap, double>(nameof(High), 10);
    public static readonly StyledProperty<bool> DivergingProperty = AvaloniaProperty.Register<BinMap, bool>(nameof(Diverging), true);
    public static readonly StyledProperty<IBrush?> GridBrushProperty = AvaloniaProperty.Register<BinMap, IBrush?>(nameof(GridBrush), Brushes.Gray);
    public static readonly StyledProperty<IBrush?> TextBrushProperty = AvaloniaProperty.Register<BinMap, IBrush?>(nameof(TextBrush), Brushes.White);

    static BinMap() => AffectsRender<BinMap>(BinsProperty, LowProperty, HighProperty, DivergingProperty, GridBrushProperty, TextBrushProperty);

    public Bin[,]? Bins { get => GetValue(BinsProperty); set => SetValue(BinsProperty, value); }
    public double Low { get => GetValue(LowProperty); set => SetValue(LowProperty, value); }
    public double High { get => GetValue(HighProperty); set => SetValue(HighProperty, value); }
    /// <summary>True for values centred on zero (green-to-red both ways); false for a plain low-to-high scale.</summary>
    public bool Diverging { get => GetValue(DivergingProperty); set => SetValue(DivergingProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? TextBrush { get => GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        var bins = Bins;
        var b = Bounds;
        if (bins is null || b.Width < 20 || b.Height < 20) return;
        int nx = bins.GetLength(0), ny = bins.GetLength(1);
        if (nx == 0 || ny == 0) return;

        var cw = b.Width / nx;
        var ch = b.Height / ny;
        var grid = new Pen(GridBrush ?? Brushes.Gray, 0.5);
        var small = cw > 34 && ch > 20;

        for (var xi = 0; xi < nx; xi++)
            for (var yi = 0; yi < ny; yi++)
            {
                var bin = bins[xi, yi];
                // Low load / low rpm at the bottom-left, like a tuning map.
                var rect = new Rect(xi * cw, (ny - 1 - yi) * ch, cw, ch);
                if (bin.Count > 0)
                {
                    context.DrawRectangle(new SolidColorBrush(Heat(bin.Average)), null, rect);
                    if (small)
                    {
                        var text = new FormattedText(bin.Average.ToString("0.#", CultureInfo.InvariantCulture),
                            CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Barlow"), 11, TextBrush ?? Brushes.White);
                        context.DrawText(text, new Point(rect.X + (cw - text.Width) / 2, rect.Y + (ch - text.Height) / 2));
                    }
                }
                context.DrawRectangle(null, grid, rect);
            }
    }

    private Color Heat(double value)
    {
        double t;
        if (Diverging)
        {
            var span = Math.Max(Math.Abs(Low), Math.Abs(High));
            t = Math.Clamp((value + span) / (2 * span), 0, 1); // 0 = -span (green), 0.5 = 0, 1 = +span (red)
        }
        else t = Math.Clamp((value - Low) / (High - Low), 0, 1);

        // green -> yellow -> red
        byte r = (byte)(t < 0.5 ? 255 * (t * 2) : 255);
        byte g = (byte)(t < 0.5 ? 200 : 200 * (1 - (t - 0.5) * 2));
        return Color.FromArgb(205, r, g, 60);
    }
}
