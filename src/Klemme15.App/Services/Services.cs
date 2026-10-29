using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Klemme15.Core;

namespace Klemme15.App.Services;

public interface IDialogService
{
    /// <summary>Ask where to save and write the text. Returns false if the user cancelled.</summary>
    Task<bool> SaveTextAsync(string title, string suggestedName, string extension, string content);

    Task<string?> PickFolderAsync(string title);

    /// <summary>Asks for a file and returns its text, or null if the user cancelled.</summary>
    Task<string?> OpenTextAsync(string title, string description, IReadOnlyList<string> patterns);

    /// <summary>Opens a web page in the user's browser.</summary>
    Task OpenUrlAsync(string url);

    /// <summary>Puts text on the clipboard. Returns false if that isn't possible here.</summary>
    Task<bool> CopyTextAsync(string text);
}

public sealed class DialogService(Window owner) : IDialogService
{
    public async Task<bool> SaveTextAsync(string title, string suggestedName, string extension, string content)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant() + " file") { Patterns = [$"*.{extension}"] }],
        });
        if (file is null) return false;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
        return true;
    }

    public async Task OpenUrlAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            await owner.Launcher.LaunchUriAsync(uri);
    }

    public async Task<bool> CopyTextAsync(string text)
    {
        if (owner.Clipboard is not { } clipboard) return false;
        await clipboard.SetTextAsync(text);
        return true;
    }

    public async Task<string?> OpenTextAsync(string title, string description, IReadOnlyList<string> patterns)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(description) { Patterns = patterns }],
        });
        if (files.Count == 0) return null;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream, System.Text.Encoding.Latin1);
        return await reader.ReadToEndAsync();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}

/// <summary>User settings, saved as JSON next to the user database overrides.</summary>
public sealed class AppSettings
{
    public string VehicleId { get; set; } = "E9x";
    public string? ModelId { get; set; }
    public string Theme { get; set; } = "Dark";
    public string? Port { get; set; }
    public bool UseDemo { get; set; }
    public bool UseEdiabas { get; set; }
    public string? EdiabasPath { get; set; }

    /// <summary>BMW Ecu folder to read official fault texts from (any OS). Null: use EdiabasPath or auto-detect.</summary>
    public string? EcuFolder { get; set; }
    public bool TrustDsr { get; set; }

    /// <summary>Lets service functions that haven't been confirmed on a real car run. Off by default.</summary>
    public bool ExperimentalService { get; set; }

    /// <summary>The user's NCS Expert data folder (e.g. C:\NCSEXPER\DATEN), for naming coding settings.</summary>
    public string? NcsFolder { get; set; }
    public List<FavoriteParameter> LiveFavorites { get; set; } = [];

    public sealed class FavoriteParameter
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Unit { get; set; } = "";
        public string Source { get; set; } = "";
        public LiveParameter ToParameter() => new() { Id = Id, Name = Name, Unit = Unit, Source = Source };
    }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Klemme15", "settings.json");

    /// <summary>Folder used before the project was renamed from OpenHood.</summary>
    private static string LegacyFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "OpenHood");

    public static AppSettings Load()
    {
        MigrateFromOpenHood();
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception) { /* corrupt settings: start fresh */ }
        return new AppSettings();
    }

    /// <summary>Carries settings and database overrides over from the old OpenHood folder, once.</summary>
    private static void MigrateFromOpenHood()
    {
        try
        {
            var newFolder = Path.GetDirectoryName(FilePath)!;
            if (Directory.Exists(newFolder) || !Directory.Exists(LegacyFolder)) return;
            foreach (var file in Directory.EnumerateFiles(LegacyFolder, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(newFolder, Path.GetRelativePath(LegacyFolder, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }
        }
        catch (Exception) { /* a fresh start is fine */ }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { /* settings are a convenience */ }
    }
}
