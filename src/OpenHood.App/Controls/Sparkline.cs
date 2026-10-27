using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenHood.App.Controls;

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
