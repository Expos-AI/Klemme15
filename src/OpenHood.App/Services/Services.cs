using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OpenHood.Core;

namespace OpenHood.App.Services;

public interface IDialogService
{
    /// <summary>Ask where to save and write the text. Returns false if the user cancelled.</summary>
    Task<bool> SaveTextAsync(string title, string suggestedName, string extension, string content);

    Task<string?> PickFolderAsync(string title);
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
    public string? Port { get; set; }
    public bool UseDemo { get; set; }
    public bool UseEdiabas { get; set; }
    public string? EdiabasPath { get; set; }
    public bool TrustDsr { get; set; }
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
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenHood", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception) { /* corrupt settings: start fresh */ }
        return new AppSettings();
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
