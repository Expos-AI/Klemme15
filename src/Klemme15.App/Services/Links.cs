namespace Klemme15.App.Services;

/// <summary>Outbound links shown in the app. Edit these to point at your own project and support pages.</summary>
public static class Links
{
    public const string Project = "https://github.com/Expos-AI/Klemme15";
    public const string Support = "https://github.com/Expos-AI/Klemme15/issues";
    public const string Privacy = "https://github.com/Expos-AI/Klemme15/blob/main/PRIVACY.md";

    /// <summary>Step-by-step guide to quick codes (NCS Expert route and the "teach from another tool" route).</summary>
    public const string QuickCodesGuide = "https://github.com/Expos-AI/Klemme15/blob/main/docs/QUICK-CODES.md";

    /// <summary>BMW's official service portal: the legal way for independents to get ISTA and BMW's programming/coding software.</summary>
    public const string BmwAftersales = "https://aos.bmwgroup.com/";

    /// <summary>BMW of North America's technical information site for independent shops (US).</summary>
    public const string BmwTechInfoUs = "https://www.bmwtechinfo.com/";

    /// <summary>Opens a new issue with a coding map attached (the issue form "coding-map.yml", field "map").</summary>
    public static string ShareCodingMap(string title, string json) =>
        $"{Project}/issues/new?template=coding-map.yml&title={Uri.EscapeDataString(title)}&map={Uri.EscapeDataString(json)}";

    /// <summary>Buy-me-a-coffee / donation link. Replace with your own; empty hides the button.</summary>
    public const string Coffee = "https://github.com/sponsors/Expos-AI";
}
