using System.Collections.ObjectModel;
using Klemme15.App.Services;
using Klemme15.Core;
using Klemme15.Core.Coding;

namespace Klemme15.App.ViewModels;

/// <summary>One step of the quick-codes setup checklist.</summary>
public sealed record SetupStep(bool Done, string Title, string Detail)
{
    public bool NotDone => !Done;
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>
/// Quick codes: a guided setup that gets a module to one-click coding. Two routes, both offline and without BMW data in
/// Klemme15: (1) the user's own NCS Expert, found automatically, verified once against its latest reading; (2) teaching
/// Klemme15 from any coding tool the user already has, by reading the module before and after they change one option.
/// Either way the result is a map for this exact module that can be shared so the next owner needs no setup at all.
/// </summary>
public sealed partial class CodingViewModel
{
    private IReadOnlyList<CodingBlock>? _learnBefore;
    private CodingLearner.Learned? _learned;
    private CodingToggle? _learnOption;
    private bool _learnTurnedOn = true;
    private string _learnStatus = "";
    private string _learnChanges = "";

    public ObservableCollection<SetupStep> SetupSteps { get; } = [];
    public bool ShowSetup => SetupSteps.Count > 0;
    public string SetupHeadline { get; private set; } = "";

    public AsyncCommand VerifyLatestCommand { get; private set; } = null!;
    public AsyncCommand LearnBeforeCommand { get; private set; } = null!;
    public AsyncCommand LearnAfterCommand { get; private set; } = null!;
    public AsyncCommand LearnSaveCommand { get; private set; } = null!;
    public AsyncCommand ShareCommand { get; private set; } = null!;
    public RelayCommand OpenGuideCommand { get; private set; } = null!;
    public RelayCommand OpenBmwCommand { get; private set; } = null!;

    private void InitQuickCodes()
    {
        VerifyLatestCommand = new AsyncCommand(VerifyLatestAsync, () => CanTalk && _module is not null && LatestTrace() is not null, e => Verification = e.Message);
        LearnBeforeCommand = new AsyncCommand(LearnBeforeAsync, () => CanTalk && _learnOption is not null, e => LearnStatus = e.Message);
        LearnAfterCommand = new AsyncCommand(LearnAfterAsync, () => CanTalk && _learnBefore is not null, e => LearnStatus = e.Message);
        LearnSaveCommand = new AsyncCommand(LearnSaveAsync, () => _learned?.Entry is not null && !_busy, e => LearnStatus = e.Message);
        ShareCommand = new AsyncCommand(ShareAsync, () => ShareableEntry() is not null && !_busy, e => Status = e.Message);
        OpenGuideCommand = new RelayCommand(() => _ = _dialogs.OpenUrlAsync(Links.QuickCodesGuide));
        OpenBmwCommand = new RelayCommand(() => _ = _dialogs.OpenUrlAsync(Links.BmwAftersales));
    }

    // ------------------------------------------------------------------ setup checklist

    private NcsLocator.NcsInstall? Install => NcsLocator.FromDaten(_settings.NcsFolder) ?? NcsLocator.Find();

    private (string Path, DateTime Written)? LatestTrace() => Install is { } i ? NcsLocator.LatestTrace(i) : null;

    private IReadOnlyList<CodingToggle> ModuleOptions =>
        _selected is { } m ? CodingToggles.All.Where(t => t.Module.Equals(CodingProtocol.ShortName(m.Ecu), StringComparison.OrdinalIgnoreCase)).ToList() : [];

    private void UpdateSetup()
    {
        SetupSteps.Clear();
        SetupHeadline = "";
        if (_selected is { } m && ModuleOptions.Count > 0)
        {
            var ready = HasPresets || Toggles.Any(t => t.CanTurnOn || t.CanTurnOff);
            if (_shared is not null && _module is null)
                SetupHeadline = ready ? "Quick codes are ready: a verified map for your exact module is on this computer." : "A verified map for your module is here. Read the coding to use it.";
            else
            {
                SetupHeadline = ready ? "Quick codes are ready." : "Set up quick codes for this module (once)";
                var trace = LatestTrace();
                SetupSteps.Add(new(_blocks is not null, "Read this module's coding", _blocks is null ? "Press Read coding (or Back up now) below." : ""));
                SetupSteps.Add(new(_library is not null, "Find your NCS Expert data",
                    _library is not null ? $"Found: {_library.Folder}"
                    : "Klemme15 looks in C:\\NCSEXPER automatically. No NCS Expert? Use \"Teach Klemme15 from another coding tool\" below instead."));
                SetupSteps.Add(new(trace is { } t && DateTime.Now - t.Written < TimeSpan.FromHours(12), "Read the module once in NCS Expert",
                    trace is { } tr ? $"Latest reading: {tr.Written:yyyy-MM-dd HH:mm}. If that's older than this session, read it again."
                    : $"In NCS Expert: load the Expert mode profile, F1 VIN/ZCS/FA, F3 ZCS/FA f. ECU, chassis {NcsChassis}, read the order from CAS, F6 Back; then F4 Process ECU, pick {CodingProtocol.ShortName(m.Ecu)}, F2 Change job: CODIERDATEN_LESEN (read only), F3 Execute job. The guide walks through it."));
                SetupSteps.Add(new(IsVerified, "Verify with NCS Expert's reading", IsVerified ? "" : "Press \"Verify with NCS Expert's latest reading\". Klemme15 must decode every setting exactly as NCS Expert did."));
                SetupSteps.Add(new(ready, "Use the one-click presets", ready ? "" : (Experimental ? "" : "Also switch on experimental functions on the Service page.")));
            }
        }
        Raise(nameof(SetupSteps));
        Raise(nameof(ShowSetup));
        Raise(nameof(SetupHeadline));
        Raise(nameof(HasSetupHeadline));
        Raise(nameof(LearnOptions));
        Raise(nameof(CanLearn));
        VerifyLatestCommand?.RaiseCanExecuteChanged();
        ShareCommand?.RaiseCanExecuteChanged();
        LearnBeforeCommand?.RaiseCanExecuteChanged();
    }

    public bool HasSetupHeadline => SetupHeadline.Length > 0;

    /// <summary>The NCS Expert chassis name for this family (E9x cars are listed as E89 in NCS Expert).</summary>
    private string NcsChassis => (_session.Vehicle?.Id ?? "") switch
    {
        "E9x" or "E8x" or "E89" => "E89",
        "E60" => "E60",
        "E70" => "E70",
        var other => other,
    };

    private async Task VerifyLatestAsync()
    {
        if (LatestTrace() is not { } t) return;
        string text;
        try { text = await File.ReadAllTextAsync(t.Path); }
        catch (IOException e) { Verification = $"Couldn't read {t.Path}: {e.Message}"; return; }
        Verification = $"Using NCS Expert's reading from {t.Written:HH:mm}...";
        await VerifyWithTraceAsync(text);
        UpdateSetup();
    }

    // ------------------------------------------------------------------ teach from another tool

    public IReadOnlyList<CodingToggle> LearnOptions => ModuleOptions;
    public bool CanLearn => _selected is not null && LearnOptions.Count > 0 && _session.Backend is IRawRequests;

    public CodingToggle? LearnOption
    {
        get => _learnOption;
        set { if (Set(ref _learnOption, value)) { _learnBefore = null; _learned = null; LearnChanges = ""; LearnStatus = ""; RefreshLearn(); } }
    }

    public bool LearnTurnedOn { get => _learnTurnedOn; set { if (Set(ref _learnTurnedOn, value)) Raise(nameof(LearnTurnedOff)); } }
    public bool LearnTurnedOff { get => !_learnTurnedOn; set => LearnTurnedOn = !value; }
    public string LearnStatus { get => _learnStatus; private set => Set(ref _learnStatus, value); }
    public string LearnChanges { get => _learnChanges; private set { if (Set(ref _learnChanges, value)) Raise(nameof(HasLearnChanges)); } }
    public bool HasLearnChanges => _learnChanges.Length > 0;

    private void ResetLearning()
    {
        _learnOption = null;
        _learnBefore = null;
        _learned = null;
        LearnStatus = "";
        LearnChanges = "";
        Raise(nameof(LearnOption));
        RefreshLearn();
    }

    private void RefreshLearn()
    {
        LearnBeforeCommand?.RaiseCanExecuteChanged();
        LearnAfterCommand?.RaiseCanExecuteChanged();
        LearnSaveCommand?.RaiseCanExecuteChanged();
    }

    private async Task LearnBeforeAsync()
    {
        if (_selected is not { } m || _learnOption is null) return;
        await Busy(async () =>
        {
            LearnStatus = "Reading the module before the change...";
            _learnBefore = await CodingProtocol.DiscoverAsync(Raw, m.Ecu, new Progress<string>(x => LearnStatus = x));
            _learned = null;
            LearnChanges = "";
            LearnStatus = $"Got {_learnBefore.Count} blocks. Now, with your coding tool, change ONLY \"{_learnOption.Title}\" " +
                          $"({(LearnTurnedOn ? "switch it on" : "switch it off")}), then press Read after.";
            _session.Log.Note($"Coding learn ({m.Name}): before reading, {_learnBefore.Count} blocks");
            return 0;
        });
        RefreshLearn();
    }

    private async Task LearnAfterAsync()
    {
        if (_selected is not { } m || _learnOption is null || _learnBefore is null) return;
        await Busy(async () =>
        {
            LearnStatus = "Reading the module after the change...";
            var after = await CodingProtocol.DiscoverAsync(Raw, m.Ecu, new Progress<string>(x => LearnStatus = x));
            _learned = CodingLearner.Learn(m.Ecu, m.Identity, _session.Vehicle?.Id ?? "", _learnOption, LearnTurnedOn, _learnBefore, after);
            LearnChanges = string.Join("\n", _learned.Changes.Select(c => c.ToString()));
            LearnStatus = _learned.Entry is null ? _learned.Message
                : _learned.Message + " If those are the only bytes your tool changed for this option, press Save.";
            _session.Log.Note($"Coding learn ({m.Name}): {_learned.Message}");
            return 0;
        });
        RefreshLearn();
    }

    private Task LearnSaveAsync()
    {
        if (_learned?.Entry is not { } entry || _selected is not { } m) return Task.CompletedTask;
        var path = _session.Database.SaveUserCoding(entry);
        _shared = _session.Database.VerifiedCoding.For(m.Ecu, m.Identity);
        LearnStatus = $"Saved to {path}. This option now works from Klemme15 on this module, with backup and read-back. Share the map so others with part {entry.PartNumber} get it too.";
        _learned = null;
        _learnBefore = null;
        FillToggles();
        RefreshLearn();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ sharing

    /// <summary>The map for this module that can be shared: verified against NCS Expert now, or one saved on this computer.</summary>
    private VerifiedCoding? ShareableEntry()
    {
        if (_selected is not { } m) return null;
        if (_evidence is { } ev && IsVerified && VerifiedCodingExport.FromEvidence(m.Ecu, m.Identity, _session.Vehicle?.Id ?? "", ev) is { } fromNcs) return fromNcs;
        return _session.Database.VerifiedCoding.For(m.Ecu, m.Identity);
    }

    private async Task ShareAsync()
    {
        if (ShareableEntry() is not { } entry) return;
        var json = VerifiedCodingExport.ToJson(entry);
        await _dialogs.CopyTextAsync(json);
        await _dialogs.OpenUrlAsync(Links.ShareCodingMap($"Coding map: {entry.Module} part {entry.PartNumber} CI {entry.CodingIndex}", json));
        Status = "The map is copied and a pre-filled GitHub issue is open in your browser: press Submit to share it. It holds only the module's part number, coding index and the option bits, nothing about you or your car.";
    }
}
