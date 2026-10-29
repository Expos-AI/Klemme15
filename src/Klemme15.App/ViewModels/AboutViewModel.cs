using Avalonia.Media;
using Klemme15.App.Services;

namespace Klemme15.App.ViewModels;

/// <summary>The About dialog: what Klemme15 is, its version, and links out. Read-only.</summary>
public sealed class AboutViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;

    public AboutViewModel(string version, IDialogService dialogs)
    {
        _dialogs = dialogs;
        Version = version;
        ProjectCommand = new RelayCommand(() => _ = _dialogs.OpenUrlAsync(Links.Project));
        SupportCommand = new RelayCommand(() => _ = _dialogs.OpenUrlAsync(Links.Support));
        PrivacyCommand = new RelayCommand(() => _ = _dialogs.OpenUrlAsync(Links.Privacy));
        CoffeeCommand = new RelayCommand(() => _ = _dialogs.OpenUrlAsync(Links.Coffee));
    }

    public string Version { get; }
    public Geometry Logo => Icons.Scan; // placeholder; the real logo mark is drawn by the control

    public string Tagline => "Open-source BMW E-series diagnostics.";
    public string Blurb =>
        "Scan every module, read and clear faults, watch and log live data, run guided tests and service functions, " +
        "and back up before you code. It works with a cheap K+DCAN cable on Windows, macOS and Linux — no dealer software.";
    public string Confirmed =>
        "Confirmed on a real 2006 E90 325i over D-CAN. Other chassis and the service and coding functions are built from " +
        "BMW's own data and tested against demo cars — please report results from your car.";
    public string Independent =>
        "Independent project, not affiliated with or endorsed by BMW AG. BMW, ISTA, INPA and EDIABAS are trademarks of BMW AG.";
    public string License => "Licensed under GPLv3. Your data stays on your computer — see the privacy policy.";

    public bool HasCoffee => !string.IsNullOrWhiteSpace(Links.Coffee);

    public RelayCommand ProjectCommand { get; }
    public RelayCommand SupportCommand { get; }
    public RelayCommand PrivacyCommand { get; }
    public RelayCommand CoffeeCommand { get; }
}
