using System.Collections.ObjectModel;
using Avalonia.Media;
using Klemme15.App.Services;
using Klemme15.Core;
using Klemme15.Core.Coding;
using Klemme15.Data;

namespace Klemme15.App.ViewModels;

public sealed class CodingModuleItem(EcuDefinition ecu, EcuIdentity? identity, Action<CodingModuleItem> select) : ViewModelBase
{
    private bool _isSelected;
    public EcuDefinition Ecu { get; } = ecu;
    public EcuIdentity? Identity { get; } = identity;
    public string Name => Ecu.Name;
    public string Detail => Identity is null ? "" : string.Join("  ", new[] { Identity.PartNumber is { } p ? $"Part {p}" : null, Identity.CodingIndex is { } c ? $"CI {c}" : null }.Where(x => x is not null));
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public RelayCommand SelectCommand => new(() => select(this));
}

public sealed class CodingBackupItem(string path, CodingBackup backup, Func<CodingBackupItem, Task> restore, Func<CodingBackupItem, Task> compare) : ViewModelBase
{
    public string Path { get; } = path;
    public CodingBackup Backup { get; } = backup;
    public string Title => $"{Backup.Taken.LocalDateTime:yyyy-MM-dd HH:mm}" + (Backup.Note is { } n ? $" · {n}" : "");
    public string Detail => $"{Backup.Blocks.Count} blocks" + (Backup.PartNumber is { } p ? $" · part {p}" : "");
    public AsyncCommand RestoreCommand => new(() => restore(this));
    public AsyncCommand CompareCommand => new(() => compare(this));
}

/// <summary>One comfort option, or a preset (several options switched together in one write).</summary>
public sealed class CodingToggleItem(string title, string description, string? caveat, IReadOnlyList<CodingToggle> toggles,
    ToggleState state, int count, bool canWrite, Action<CodingToggleItem, bool> request, bool isPreset = false) : ViewModelBase
{
    public CodingToggleItem(CodingToggle toggle, ToggleState state, int count, bool canWrite, Action<CodingToggleItem, bool> request)
        : this(toggle.Title, toggle.Description, toggle.Caveat, [toggle], state, count, canWrite, request) { }

    /// <summary>The options this item switches (one for a single option).</summary>
    public IReadOnlyList<CodingToggle> Toggles { get; } = toggles;
    public bool IsPreset { get; } = isPreset;
    public ToggleState State { get; } = state;
    public string Title { get; } = title;
    public string Description { get; } = description;
    public string Caveat => caveat ?? "";
    public bool HasCaveat => caveat is not null;
    public string StateText => State switch
    {
        ToggleState.On => IsPreset ? "All on" : $"On ({count} setting{(count == 1 ? "" : "s")})",
        ToggleState.Off => IsPreset ? "All off (factory)" : "Off (factory)",
        ToggleState.Mixed => "Partly on",
        ToggleState.Unknown => "Can't tell",
        _ => "Not in this module",
    };
    public string IncludesText => IsPreset ? "Includes: " + string.Join(" · ", Toggles.Select(t => t.Title.Split(':')[0])) : "";
    public bool Available => State is not ToggleState.Unavailable;
    public bool CanTurnOn => canWrite && State is ToggleState.Off or ToggleState.Mixed;
    public bool CanTurnOff => canWrite && State is ToggleState.On or ToggleState.Mixed;
    public string OnLabel => IsPreset ? "Apply all" : "Turn on";
    public string OffLabel => IsPreset ? "Undo all" : "Turn off";
    public RelayCommand TurnOnCommand => new(() => request(this, true));
    public RelayCommand TurnOffCommand => new(() => request(this, false));
}

public sealed record CodingSettingItem(string Name, string Value, string Group);

/// <summary>
/// Coding: back up and restore a module's coding (no BMW data needed), read its settings by name from the user's own
/// NCS Expert data, and switch a short list of popular comfort options with every safety check Klemme15 has.
/// </summary>
public sealed partial class CodingViewModel : PageViewModel
{
    private readonly SessionState _session;
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private CodingModuleItem? _selected;
    private IReadOnlyList<CodingBlock>? _blocks;
    private NcsLibrary? _library;
    private NcsModule? _module;
    private NcsLayout? _layout;
    private NcsEvidence? _evidence;
    private VerifiedCoding? _shared;
    private string _status = "";
    private string _ncsStatus = "";
    private string _verification = "";
    private string _diff = "";
    private string _filter = "";
    private bool _busy;
    private (CodingToggleItem Item, bool On)? _pending;

    public CodingViewModel(SessionState session, AppSettings settings, IDialogService dialogs)
    {
        _session = session;
        _settings = settings;
        _dialogs = dialogs;
        ReadCommand = new AsyncCommand(ReadAsync, () => CanTalk, e => Status = e.Message);
        BackupCommand = new AsyncCommand(BackupAsync, () => CanTalk, e => Status = e.Message);
        RestoreFileCommand = new AsyncCommand(RestoreFromFileAsync, () => CanTalk, e => Status = e.Message);
        ChooseNcsCommand = new AsyncCommand(ChooseNcsAsync, () => !_busy, e => NcsStatus = e.Message);
        VerifyCommand = new AsyncCommand(VerifyAsync, () => CanTalk && _module is not null, e => Verification = e.Message);
        ConfirmCommand = new AsyncCommand(ApplyPendingAsync, () => _pending is not null && !_busy, e => Status = e.Message);
        ExportCommand = new AsyncCommand(ExportAsync, () => IsVerified && !_busy, e => Status = e.Message);
        CancelCommand = new RelayCommand(() => Pending = null);
        InitQuickCodes();
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionState.LastScan) or nameof(SessionState.Backend)) Refresh();
        };
        UpdateNcsStatus();
    }

    public override string Title => "Coding";
    public override Geometry Icon => Icons.Coding;

    public ObservableCollection<CodingModuleItem> Modules { get; } = [];
    public ObservableCollection<CodingBackupItem> Backups { get; } = [];
    public ObservableCollection<CodingToggleItem> Toggles { get; } = [];

    /// <summary>One-click presets (e.g. every LED bulb warning off), shown above the single options.</summary>
    public ObservableCollection<CodingToggleItem> Presets { get; } = [];
    public bool HasPresets => Presets.Count > 0;

    /// <summary>
    /// When this module has presets but Klemme15 doesn't yet know where their settings live on it, explains what unlocks
    /// them. Byte positions are never guessed: a wrong bit in a footwell module can switch off lights or worse.
    /// </summary>
    public string PresetLockedText
    {
        get
        {
            if (_selected is not { } m || HasPresets) return "";
            var names = CodingPresets.All.Where(p => p.Module.Equals(CodingProtocol.ShortName(m.Ecu), StringComparison.OrdinalIgnoreCase)).Select(p => p.Title).ToList();
            if (names.Count == 0) return "";
            return $"One-click: {string.Join("; ", names)}. This unlocks as soon as Klemme15 knows exactly where these settings live in your module " +
                   $"(part {m.Identity?.PartNumber ?? "unknown"}): either pick your NCS Expert data folder and verify it once (step 2), or once someone with " +
                   "the same module shares a verified map. Klemme15 never guesses coding bits: a wrong bit in this module can switch off lights or worse.";
        }
    }
    public bool HasPresetLocked => PresetLockedText.Length > 0;
    public ObservableCollection<CodingSettingItem> Settings { get; } = [];

    public AsyncCommand ReadCommand { get; }
    public AsyncCommand BackupCommand { get; }
    public AsyncCommand RestoreFileCommand { get; }
    public AsyncCommand ChooseNcsCommand { get; }
    public AsyncCommand VerifyCommand { get; }
    public AsyncCommand ConfirmCommand { get; }
    public AsyncCommand ExportCommand { get; }
    public RelayCommand CancelCommand { get; }

    public bool HasModule => _selected is not null;
    public bool NoModules => Modules.Count == 0;
    public string ModuleTitle => _selected?.Name ?? "";
    public string FrmText => _selected is { } m && FrmGuard.IsFootwellModule(m.Ecu) ? $"{FrmGuard.Assess(m.Identity).Label}: {FrmGuard.Assess(m.Identity).Advice}" : "";
    public bool HasFrmText => FrmText.Length > 0;
    public bool HasToggles => Toggles.Count > 0;
    public bool HasSettings => Settings.Count > 0;
    public bool HasBackups => Backups.Count > 0;
    public bool Experimental => _settings.ExperimentalService;
    public bool NotExperimental => !Experimental;
    /// <summary>Writing is unlocked only while the loaded module file is the one NCS Expert's trace verified.</summary>
    public bool IsVerified => _evidence is not null && _module is not null && _evidence.Module.FileName == _module.FileName;

    private bool CanTalk => !_busy && _selected is not null && _session.Backend is IRawRequests;

    public string Status { get => _status; private set => Set(ref _status, value); }
    public string NcsStatus { get => _ncsStatus; private set => Set(ref _ncsStatus, value); }
    public string Verification { get => _verification; private set => Set(ref _verification, value); }
    public string Diff { get => _diff; private set { if (Set(ref _diff, value)) Raise(nameof(HasDiff)); } }
    public bool HasDiff => _diff.Length > 0;

    public string Filter
    {
        get => _filter;
        set { if (Set(ref _filter, value)) FillSettings(); }
    }

    public string PendingText => _pending is { } p
        ? $"{(p.On ? (p.Item.IsPreset ? "Apply" : "Turn on") : (p.Item.IsPreset ? "Undo" : "Turn off"))}: {p.Item.Title} on {ModuleTitle}? Klemme15 saves a backup first, writes only these settings' bits in one go, reads everything back and puts the original back if anything looks wrong."
        : "";
    public bool HasPending => _pending is not null;
    public bool PendingIsPreset => _pending is { Item.IsPreset: true };
    public bool PendingIsToggle => _pending is { Item.IsPreset: false };

    private (CodingToggleItem Item, bool On)? Pending
    {
        get => _pending;
        set
        {
            _pending = value;
            Raise(nameof(PendingText));
            Raise(nameof(HasPending));
            Raise(nameof(PendingIsPreset));
            Raise(nameof(PendingIsToggle));
            ConfirmCommand.RaiseCanExecuteChanged();
        }
    }

    public override void OnShown()
    {
        Refresh();
        Raise(nameof(Experimental));
        Raise(nameof(NotExperimental));
    }

    private static string BackupFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Klemme15", "coding-backups");

    /// <summary>The comfort modules that answered the last quick test.</summary>
    private void Refresh()
    {
        var scan = _session.LastScan;
        var answered = scan?.Ecus.Where(e => e.Status is EcuScanStatus.Ok or EcuScanStatus.Faults && CodingProtocol.IsCodable(e.Ecu)).ToList() ?? [];
        var keep = _selected?.Ecu.Id;
        Modules.Clear();
        foreach (var e in answered) Modules.Add(new CodingModuleItem(e.Ecu, e.Identity, Select));
        Raise(nameof(NoModules));
        var again = Modules.FirstOrDefault(m => m.Ecu.Id == keep);
        if (again is not null) Select(again);
        else if (_selected is not null) Select(null);
        Status = _session.Backend is null ? "Connect to the car (or a demo car) in the Garage first."
            : _session.Backend is not IRawRequests ? "Coding uses Klemme15's own connection: reconnect without \"Use my own BMW files\"."
            : scan is null ? "Run the quick test first: Klemme15 codes only modules that answered it." : "";
        Refresh(ReadCommand, BackupCommand, RestoreFileCommand, VerifyCommand, ExportCommand);
    }

    private static void Refresh(params AsyncCommand[] commands)
    {
        foreach (var c in commands) c.RaiseCanExecuteChanged();
    }

    private void Select(CodingModuleItem? item)
    {
        foreach (var m in Modules) m.IsSelected = m == item;
        _selected = item;
        _blocks = null;
        _module = null;
        _layout = null;
        _evidence = null;
        _shared = item is not null ? _session.Database.VerifiedCoding.For(item.Ecu, item.Identity) : null;
        Pending = null;
        Toggles.Clear();
        Settings.Clear();
        Diff = "";
        Verification = "";
        LoadBackups();
        foreach (var p in new[] { nameof(HasModule), nameof(ModuleTitle), nameof(FrmText), nameof(HasFrmText), nameof(HasToggles), nameof(HasSettings), nameof(IsVerified), nameof(HasPresets), nameof(PresetLockedText), nameof(HasPresetLocked) })
            Raise(p);
        Refresh(ReadCommand, BackupCommand, RestoreFileCommand, VerifyCommand, ExportCommand);
        ResetLearning();
        UpdateSetup();
    }

    private void LoadBackups()
    {
        Backups.Clear();
        if (_selected is { } m && Directory.Exists(BackupFolder))
            foreach (var file in Directory.EnumerateFiles(BackupFolder, "*.k15coding.json").OrderByDescending(f => f))
            {
                try
                {
                    var b = CodingBackup.FromJson(File.ReadAllText(file));
                    if (b.ModuleId.Equals(m.Ecu.Id, StringComparison.OrdinalIgnoreCase)) Backups.Add(new CodingBackupItem(file, b, RestoreAsync, CompareAsync));
                }
                catch (Exception e) when (e is IOException or System.Text.Json.JsonException or FormatException or InvalidDataException) { }
            }
        Raise(nameof(HasBackups));
    }

    private IRawRequests Raw => _session.Backend as IRawRequests ?? throw new DiagnosticException("This connection can't read coding.");

    private async Task<T> Busy<T>(Func<Task<T>> work)
    {
        _busy = true;
        Refresh(ReadCommand, BackupCommand, RestoreFileCommand, VerifyCommand, ConfirmCommand, ChooseNcsCommand, ExportCommand);
        try { return await work(); }
        finally
        {
            _busy = false;
            Refresh(ReadCommand, BackupCommand, RestoreFileCommand, VerifyCommand, ConfirmCommand, ChooseNcsCommand, ExportCommand);
            RefreshLearn();
            UpdateSetup();
        }
    }

    /// <summary>Reads the module's coding: the blocks the NCS map names, or every block the module has.</summary>
    private async Task<IReadOnlyList<CodingBlock>> ReadBlocksAsync()
    {
        var progress = new Progress<string>(s => Status = s);
        if (_module is { } module && _layout is { } layout)
        {
            var ids = module.Supported.Select(s => layout.Locate(s).Block).Where(CodingProtocol.IsCodingBlock).Distinct().ToList();
            var known = await CodingProtocol.ReadBlocksAsync(Raw, _selected!.Ecu, ids, progress);
            if (known.Count > 0) return known;
        }
        return await CodingProtocol.DiscoverAsync(Raw, _selected!.Ecu, progress);
    }

    private async Task ReadAsync()
    {
        if (_selected is not { } m) return;
        await Busy(async () =>
        {
            Status = "Reading the coding...";
            _blocks = await CodingProtocol.DiscoverAsync(Raw, m.Ecu, new Progress<string>(s => Status = s));
            _session.Log.Note($"Coding read from {m.Ecu.Name}: {_blocks.Count} blocks");
            if (_blocks.Count == 0) { Status = $"{m.Name} didn't return any coding blocks."; return 0; }
            Status = $"Read {_blocks.Count} coding blocks.";
            PickModuleFile(null);
            return 0;
        });
    }

    /// <summary>Chooses the NCS module file that fits best, decodes the settings and fills the toggles.</summary>
    private void PickModuleFile(IReadOnlyDictionary<string, string>? trace)
    {
        if (_selected is not { } m || _blocks is null) return;
        _library ??= _settings.NcsFolder is { } f && _session.Vehicle is { } v ? NcsLibrary.Open(f, v.Id) : null;
        if (_library is null)
        {
            NcsStatus = _settings.NcsFolder is null
                ? "To see settings by name, point Klemme15 at your NCS Expert data folder (usually C:\\NCSEXPER\\DATEN). Backups and restores work without it."
                : $"No NCS data for this car family in {_settings.NcsFolder}.";
            FillToggles();
            return;
        }
        var candidates = _library.Candidates(CodingProtocol.ShortName(m.Ecu), m.Identity?.CodingIndex, _blocks, trace);
        if (candidates.Count == 0) { NcsStatus = $"None of the module files in {_library.Folder} fits {m.Name}."; FillToggles(); return; }
        _module = _library.Load(candidates[0].FileName);
        if (_evidence is not null && _evidence.Module.FileName != _module.FileName) _evidence = null; // a different file isn't verified
        _layout = trace is null
            ? NcsLayout.Candidates.MaxBy(l => NcsDecoder.Plausibility(_module, l, _blocks))
            : NcsDecoder.Verify(_module, _blocks, trace).Layout;
        NcsStatus = $"Module file {_module.FileName}: {candidates[0].Why}. {_module.Supported.Count()} settings.";
        FillSettings();
        FillToggles();
        VerifyCommand.RaiseCanExecuteChanged();
    }

    private void FillSettings()
    {
        Settings.Clear();
        if (_module is not null && _layout is not null && _blocks is not null)
            foreach (var (s, value) in NcsDecoder.Decode(_module, _layout, _blocks))
                if (_filter.Length == 0 || s.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                    Settings.Add(new CodingSettingItem(s.Key, value ?? "?", s.Group));
        Raise(nameof(HasSettings));
    }

    private void FillToggles()
    {
        Toggles.Clear();
        Presets.Clear();
        if (_selected is { } m && _blocks is not null)
        {
            var shortName = CodingProtocol.ShortName(m.Ecu);
            var mine = CodingToggles.All.Where(t => t.Module.Equals(shortName, StringComparison.OrdinalIgnoreCase)).ToList();
            var states = new Dictionary<string, (ToggleState State, int Count)>();
            bool canWrite = false;
            if (_module is not null && _layout is not null)
            {
                canWrite = IsVerified && Experimental;
                foreach (var t in mine) states[t.Id] = (CodingToggles.StateOf(t, _module, _layout, _blocks), t.Match(_module).Count);
            }
            else if (_shared is { } shared)
            {
                // No NCS data, but a community-verified map for this exact module: use it.
                canWrite = Experimental;
                foreach (var t in mine.Where(t => shared.Toggles.ContainsKey(t.Id)))
                {
                    var state = VerifiedWriter.StateOf(shared, t.Id, _blocks) switch
                    {
                        VerifiedState.On => ToggleState.On, VerifiedState.Off => ToggleState.Off,
                        VerifiedState.Mixed => ToggleState.Mixed, VerifiedState.Unavailable => ToggleState.Unavailable, _ => ToggleState.Unknown,
                    };
                    states[t.Id] = (state, shared.Toggles[t.Id].Count);
                }
            }
            foreach (var t in mine.Where(t => states.ContainsKey(t.Id)))
                Toggles.Add(new CodingToggleItem(t, states[t.Id].State, states[t.Id].Count, canWrite, (item, on) => Pending = (item, on)));
            foreach (var preset in CodingPresets.All.Where(p => p.Module.Equals(shortName, StringComparison.OrdinalIgnoreCase)))
            {
                var members = preset.Toggles.Where(t => states.TryGetValue(t.Id, out var st) && st.State != ToggleState.Unavailable).ToList();
                if (members.Count < 2) continue;   // a preset of one is just the option itself
                var combined = CodingPresets.Combine(members.Select(t => states[t.Id].State));
                Presets.Add(new CodingToggleItem(preset.Title, preset.Description, preset.Caveat, members, combined,
                    members.Sum(t => states[t.Id].Count), canWrite, (item, on) => Pending = (item, on), isPreset: true));
            }
        }
        Raise(nameof(HasToggles));
        UpdateSetup();
        Raise(nameof(HasPresets));
        Raise(nameof(PresetLockedText));
        Raise(nameof(HasPresetLocked));
        Raise(nameof(HasShared));
        Raise(nameof(SharedText));
        Raise(nameof(IsVerified));
        ExportCommand.RaiseCanExecuteChanged();
    }

    public bool HasShared => _shared is not null && _module is null;
    public string SharedText => _shared is { } s ? $"A community-verified map for this module (part {s.PartNumber}) is built in, so these options work without NCS data." : "";

    private async Task<CodingBackup> TakeBackupAsync(string? note)
    {
        var m = _selected!;
        var blocks = _blocks ?? await CodingProtocol.DiscoverAsync(Raw, m.Ecu, new Progress<string>(s => Status = s));
        if (blocks.Count == 0) throw new DiagnosticException($"{m.Name} didn't return any coding blocks.");
        var backup = new CodingBackup
        {
            ModuleId = m.Ecu.Id, ModuleName = m.Name, PartNumber = m.Identity?.PartNumber, CodingIndex = m.Identity?.CodingIndex,
            Variant = m.Identity?.Variant, Taken = DateTimeOffset.Now, Note = note, Blocks = blocks,
        };
        Directory.CreateDirectory(BackupFolder);
        await File.WriteAllTextAsync(Path.Combine(BackupFolder, backup.FileName), backup.ToJson());
        _session.Log.Note($"Coding backup of {m.Name}: {blocks.Count} blocks saved as {backup.FileName}");
        LoadBackups();
        return backup;
    }

    private async Task BackupAsync()
    {
        if (_selected is null) return;
        await Busy(async () =>
        {
            _blocks = null; // always a fresh read
            var b = await TakeBackupAsync(null);
            _blocks = b.Blocks;
            Status = $"Backup saved: {b.Blocks.Count} blocks. Keep a copy somewhere safe: it is the way back if any coding tool goes wrong.";
            return 0;
        });
    }

    private async Task<bool> GuardAsync()
    {
        var m = _selected!;
        if (_session.IsDemo) return true;
        var guard = await WriteGuard.CheckAsync(_session.Backend!, m.Ecu, m.Identity, WriteKind.Coding,
            _session.Vehicle?.Modules.FirstOrDefault(x => x.IsEngine), _session.Measurements);
        if (!guard.Allowed) Status = $"Not written: {guard.Reason}";
        return guard.Allowed;
    }

    private async Task RestoreAsync(CodingBackupItem item)
    {
        if (_selected is not { } m) return;
        await Busy(async () =>
        {
            if (!Experimental) { Status = "Restoring writes to the module: switch on experimental functions on the Service page first."; return 0; }
            if (CodingProtocol.Mismatch(item.Backup, m.Ecu, m.Identity) is { } why) { Status = why; return 0; }
            if (!await GuardAsync()) return 0;
            _blocks = null;
            await TakeBackupAsync("before restoring"); // so a wrong restore can be undone too
            var written = await CodingProtocol.RestoreAsync(Raw, m.Ecu, item.Backup, m.Identity, new Progress<string>(s => Status = s));
            _session.Log.Note($"Coding of {m.Name} restored from {item.Title}: {written} blocks written");
            _blocks = null;
            Status = written == 0 ? "The module already matches this backup: nothing to write."
                : $"Restored {written} block{(written == 1 ? "" : "s")} and read them back. Switch the ignition off for 10 seconds and on again.";
            return 0;
        });
    }

    private async Task RestoreFromFileAsync()
    {
        var text = await _dialogs.OpenTextAsync("Restore a coding backup", "Klemme15 coding backup", ["*.k15coding.json", "*.json"]);
        if (text is null || _selected is null) return;
        var backup = CodingBackup.FromJson(text);
        await RestoreAsync(new CodingBackupItem("", backup, RestoreAsync, CompareAsync));
    }

    private async Task CompareAsync(CodingBackupItem item)
    {
        if (_selected is not { } m) return;
        await Busy(async () =>
        {
            var now = await CodingProtocol.ReadBlocksAsync(Raw, m.Ecu, item.Backup.Blocks.Select(b => b.Id), new Progress<string>(s => Status = s));
            var diffs = CodingProtocol.Compare(item.Backup.Blocks, now);
            var named = new List<string>();
            if (_module is not null && _layout is not null)
            {
                var before = NcsDecoder.Decode(_module, _layout, item.Backup.Blocks).ToDictionary(x => x.Setting.Key, x => x.Value);
                foreach (var (s, v) in NcsDecoder.Decode(_module, _layout, now))
                    if (before.TryGetValue(s.Key, out var old) && old != v) named.Add($"{s.Key}: {old ?? "?"} → {v ?? "?"}");
            }
            Diff = diffs.Count == 0 ? $"The module's coding is identical to the backup from {item.Title}."
                : $"{diffs.Count} byte{(diffs.Count == 1 ? "" : "s")} differ from the backup from {item.Title}:\n"
                  + string.Join("\n", named.Count > 0 ? named : diffs.Select(d => d.ToString()));
            Status = "";
            return 0;
        });
    }

    private async Task ChooseNcsAsync()
    {
        var folder = await _dialogs.PickFolderAsync("Your NCS Expert data folder (NCSEXPER\\DATEN)");
        if (folder is null) return;
        _settings.NcsFolder = folder;
        _settings.Save();
        _library = null;
        UpdateNcsStatus();
        if (_blocks is not null) PickModuleFile(null);
    }

    private void UpdateNcsStatus()
    {
        if (_settings.NcsFolder is null && NcsLocator.Find() is { } found)
        {
            // NCS Expert is installed: use it without asking.
            _settings.NcsFolder = found.Daten;
            try { _settings.Save(); } catch (Exception) { /* not fatal */ }
        }
        if (_settings.NcsFolder is null)
        {
            NcsStatus = "Optional: point Klemme15 at your NCS Expert data folder (usually C:\\NCSEXPER\\DATEN) to see settings by name. Klemme15 doesn't include BMW's coding data.";
            return;
        }
        var family = _session.Vehicle?.Id ?? _settings.VehicleId;
        _library = NcsLibrary.Open(_settings.NcsFolder, family);
        NcsStatus = _library is null ? $"No NCS data for {family} found in {_settings.NcsFolder}." : $"NCS data: {_library.Folder} ({_library.ModuleFiles.Count} module files).";
        UpdateSetup();
    }

    /// <summary>
    /// Unlocks writing for this module: NCS Expert's own reading of the module (FSW_PSW.TRC) must match what Klemme15
    /// decodes, setting for setting.
    /// </summary>
    private async Task VerifyAsync()
    {
        var text = await _dialogs.OpenTextAsync("NCS Expert's reading of this module (FSW_PSW.TRC)", "NCS Expert trace", ["*.TRC", "*.trc"]);
        if (text is null) return;
        await VerifyWithTraceAsync(text);
    }

    private async Task VerifyWithTraceAsync(string text)
    {
        if (_selected is null) return;
        var trace = NcsFiles.ParseTrace(text);
        await Busy(async () =>
        {
            _blocks = await CodingProtocol.DiscoverAsync(Raw, _selected!.Ecu, new Progress<string>(s => Status = s));
            PickModuleFile(trace);
            if (_module is null) { Verification = "No module file to verify."; return 0; }
            var result = NcsDecoder.Verify(_module, _blocks, trace);
            var evidence = new NcsEvidence(_module, trace, _blocks);
            _evidence = evidence.Layouts.Count > 0 ? evidence : null;
            Verification = _evidence is not null
                ? $"Verified: {result.Summary} Klemme15 reads this module exactly like NCS Expert does, so the switches below can write to it."
                : $"Not verified: {result.Summary} {string.Join("; ", result.Mismatches)}. Make sure the trace was read from this car's {_selected!.Name} just now. Writing stays locked.";
            _session.Log.Note($"Coding map check for {_selected!.Name} with {_module.FileName}: {result.Summary}");
            FillSettings();
            FillToggles();
            Status = "";
            return 0;
        });
    }

    private async Task ApplyPendingAsync()
    {
        // Everything the write depends on is captured now, so nothing can swap it while the checks below await.
        if (_pending is not { } p || _selected is not { } m) return;
        var evidence = _evidence is { } e && _module?.FileName == e.Module.FileName ? e : null;
        var shared = evidence is null ? _shared : null;
        if (evidence is null && shared is null) return;
        Pending = null;
        if (!Experimental) { Status = "Switch on experimental functions on the Service page first."; return; }
        await Busy(async () =>
        {
            if (!await GuardAsync()) return 0;
            _blocks = null; // the safety backup is always a fresh read
            var backup = await TakeBackupAsync($"before: {p.Item.Title} {(p.On ? "on" : "off")}");
            CodingResult result;
            if (evidence is not null)
            {
                var changes = p.Item.Toggles.SelectMany(t => CodingToggles.ChangesFor(t, evidence.Module, p.On)).DistinctBy(c => c.Setting.Key).ToList();
                _session.Log.Note($"Coding {m.Name}: {p.Item.Title} {(p.On ? "on" : "off")} ({string.Join(", ", changes.Select(c => $"{c.Setting.Key}={c.Value}"))})");
                result = await CodingWriter.ApplyAsync(Raw, m.Ecu, evidence, changes, backup, new Progress<string>(s => Status = s));
            }
            else
            {
                _session.Log.Note($"Coding {m.Name} (community-verified map): {p.Item.Title} {(p.On ? "on" : "off")}");
                result = await VerifiedWriter.ApplyAsync(Raw, m.Ecu, m.Identity, shared!,
                    p.Item.Toggles.Select(t => t.Id).Where(shared!.Toggles.ContainsKey).ToList(), p.On, backup, new Progress<string>(s => Status = s));
            }
            _session.Log.Note($"  -> {result.Message}");
            Status = result.Message;
            _blocks = await ReadBlocksAsync();
            FillSettings();
            FillToggles();
            return 0;
        });
    }

    private async Task ExportAsync()
    {
        if (_selected is not { } m || _evidence is not { } evidence || _module?.FileName != evidence.Module.FileName) return;
        var entry = VerifiedCodingExport.FromEvidence(m.Ecu, m.Identity, _session.Vehicle?.Id ?? "", evidence);
        if (entry is null) { Status = "Nothing verified to export yet."; return; }
        var saved = await _dialogs.SaveTextAsync("Save a verified coding map to contribute",
            $"{m.Ecu.Id}-{entry.PartNumber}.coding-verified.json", "json", VerifiedCodingExport.ToJson(entry));
        Status = saved ? "Saved. Please open a pull request adding it to db/coding-verified.json so others with this module can use it." : "";
    }
}
