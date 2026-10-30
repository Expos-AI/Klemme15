using Avalonia.Controls;
using Avalonia.Media;
using AvPath = Avalonia.Controls.Shapes.Path;

namespace Klemme15.App.Views;

/// <summary>
/// Startup splash. The wordmark types in, the tile builds around it, and a loading line with a
/// progress bar under the logo shows the app's real startup progress. When startup has finished the
/// lamp lights (terminal 15 on = ready). Any key or click skips the animation, never the startup work.
/// </summary>
public partial class SplashWindow : Window
{
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#F2A93B"));
    private static readonly IBrush LampDim = new SolidColorBrush(Color.Parse("#BFC4CC"));

    // Right edge of each letter (K l e m m e 1 5) in lockup units; the typing cursor follows it.
    private static readonly double[] GlyphRight = { 381.4, 410.0, 491.3, 609.9, 730.3, 810.7, 858.6, 940.5 };

    private readonly AvPath[] _glyphs;
    private readonly CancellationTokenSource _skip = new();

    public SplashWindow()
    {
        InitializeComponent();
        _glyphs = new[] { G0, G1, G2, G3, G4, G5, G6, G7 };
        KeyDown += (_, _) => _skip.Cancel();
        PointerPressed += (_, _) => _skip.Cancel();
    }

    /// <param name="startupWork">
    /// The app's real startup. It reports (status text, fraction 0..1) as each step really happens.
    /// The splash never finishes before this task does.
    /// </param>
    public async Task RunAsync(Func<IProgress<(string Status, double Fraction)>, Task> startupWork)
    {
        // Created on the UI thread, so reports from background work are marshalled back to it.
        var progress = new Progress<(string Status, double Fraction)>(p => SetLoad(p.Status, p.Fraction));
        var work = startupWork(progress);
        var skip = _skip.Token;

        try
        {
            await Task.Delay(250, skip);
            for (var i = 0; i < _glyphs.Length; i++)                  // 1. the wordmark types in
            {
                _glyphs[i].Opacity = 1;
                Canvas.SetLeft(TypingCursor, GlyphRight[i] + 9);
                await Task.Delay(62, skip);
            }
            await Task.Delay(90, skip);

            TypingCursor.IsVisible = false;                           // 2. the tile builds, lamp still dim
            Tile.Opacity = 1;
            Lamp.Fill = LampDim;
            await Task.Delay(380, skip);

            LoadPanel.Opacity = 1;                                    // 3. the loading line fades in
            await Task.Delay(300, skip);
        }
        catch (OperationCanceledException)
        {
            ShowEndState();
        }
        catch (Exception e)
        {
            // The animation is decoration: whatever goes wrong here must not interfere with startup.
            System.Diagnostics.Trace.TraceError($"Splash animation failed: {e}");
        }

        await work;                                                   // never close before startup is done
        LoadPanel.Opacity = 1;
        SetLoad("ready", 1);
        Status.Foreground = Amber;
        await IgniteAsync();                                          // 4. the lamp lights: ready
        await Task.Delay(450);
    }

    private void SetLoad(string status, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        Status.Text = status;
        Percent.Text = $"{(int)Math.Round(fraction * 100)}%";
        BarFill.Width = Track.Bounds.Width * fraction;
    }

    private async Task IgniteAsync()
    {
        // A short flicker, like a bulb check, then it stays on.
        foreach (var on in new[] { true, false, true, false, true })
        {
            Lamp.Fill = on ? Amber : LampDim;
            await Task.Delay(55);
        }
    }

    private void ShowEndState()
    {
        foreach (var glyph in _glyphs) glyph.Opacity = 1;
        TypingCursor.IsVisible = false;
        Tile.Opacity = 1;
        Lamp.Fill = LampDim;
        LoadPanel.Opacity = 1;
    }
}
