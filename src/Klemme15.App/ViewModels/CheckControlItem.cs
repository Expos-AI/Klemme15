using Klemme15.Core;

namespace Klemme15.App.ViewModels;

/// <summary>One dash (Check Control) message in the side panel.</summary>
public sealed class CheckControlItem(CheckControlMessage message)
{
    public CheckControlMessage Message { get; } = message;
    public string Title => Message.Title;
    public string Code => Message.Code;
    public bool IsRed => Message.Severity == CcSeverity.Critical && Message.Active;
    public bool IsYellow => !IsRed && Message.Active;
    public bool IsHistory => !Message.Active;
    public string StateText => Message.Active
        ? Message.Severity switch { CcSeverity.Critical => "Red", CcSeverity.Warning => "Yellow", _ => "Advisory" }
        : Message.MileageKm is { } km ? $"~{km:N0} km" : "earlier";
    public string? Description => Message.Active ? Message.Description : null;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string? Hint => Message.Active && Message.Hint is { } h ? $"Tip: {h}" : null;
    public bool HasHint => Hint is not null;
    public string? Official => Message.OfficialText is { } t && t != Message.Title ? $"BMW: {t}" : null;
    public bool HasOfficial => Official is not null;
}
