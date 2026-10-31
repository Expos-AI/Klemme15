using Klemme15.Serial;

namespace Klemme15.App.Services;

/// <summary>
/// What the splash screen found while the app started: the official fault texts, the user's BMW files and the
/// serial ports. The Garage uses it instead of looking again, so it opens ready instead of "Looking for...".
/// Any field left null is simply done the usual way.
/// </summary>
public sealed record StartupInfo
{
    public OfficialStatus? Official { get; init; }

    /// <summary>True when the search for BMW files ran (whether or not it found any).</summary>
    public bool EdiabasSearched { get; init; }
    public SgbdFolder? Ediabas { get; init; }

    public IReadOnlyList<PortInfo>? Ports { get; init; }
}
