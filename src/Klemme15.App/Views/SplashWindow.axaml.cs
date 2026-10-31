using Avalonia.Controls;
using Avalonia.Media;
using AvPath = Avalonia.Controls.Shapes.Path;

namespace Klemme15.App.Views;

/// <summary>
/// Startup splash. The wordmark types in, the tile builds around it, and a loading line with a progress bar
/// under the logo shows the app's real startup steps (database, BMW files, fault texts, ports, interface).
/// It stays exactly as long as that work takes, plus the short intro animation, and then the lamp lights
/// (terminal 15 on = ready). There is no artificial wait, so there is nothing to skip.
/// </summary>
public partial class SplashWindow : Window
{
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#F2A93B"));
    private static readonly IBrush LampDim = new SolidColorBrush(Color.Parse("#BFC4CC"));

    // Right edge of each letter (K l e m m e 1 5) in lockup units; the typing cursor follows it.
    private static readonly double[] GlyphRight = { 381.4, 410.0, 491.3, 609.9, 730.3, 810.7, 858.6, 940.5 };

    private readonly AvPath[] _glyphs;

    public SplashWindow()
    {
        InitializeComponent();
        _glyphs = new[] { G0, G1, G2, G3, G4, G5, G6, G7 };
        VersionText.Text = $"v{typeof(SplashWindow).Assembly.GetName().Version?.ToString(3)}";
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
        try
        {
            // About one second in all; the real work runs alongside it.
            await Task.Delay(150);
            for (var i = 0; i < _glyphs.Length; i++)                  // 1. the wordmark types in
            {
                _glyphs[i].Opacity = 1;
                Canvas.SetLeft(TypingCursor, GlyphRight[i] + 9);
                await Task.Delay(45);
            }
            await Task.Delay(70);

            TypingCursor.IsVisible = false;                           // 2. the tile builds, lamp still dim
            Tile.Opacity = 1;
            Lamp.Fill = LampDim;
            LoadPanel.Opacity = 1;                                    // 3. the loading line fades in with it
            await Task.Delay(220);
        }
        catch (Exception e)
        {
            // The animation is decoration: whatever goes wrong here must not interfere with startup.
            System.Diagnostics.Trace.TraceError($"Splash animation failed: {e}");
            ShowEndState();
        }

        await work;                                                   // never close before startup is done
        LoadPanel.Opacity = 1;
        SetLoad("Ready", 1);
        Status.Foreground = Amber;
        await IgniteAsync();                                          // 4. the lamp lights: ready
        await Task.Delay(250);
    }

    private void SetLoad(string status, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        Status.Text = fraction < 1 ? status + "…" : status;
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
