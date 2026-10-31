using Avalonia;
using Avalonia.Controls;

namespace Klemme15.App.Views;

public partial class MainWindow : Window
{
    /// <summary>Below this height the navigation rail shows icons only, so all pages fit without scrolling.</summary>
    private const double CompactRailHeight = 860;

    public MainWindow()
    {
        InitializeComponent();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ClientSizeProperty) Rail.Classes.Set("compact", ClientSize.Height < CompactRailHeight);
        };
    }
}
