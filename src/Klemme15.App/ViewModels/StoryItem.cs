using Klemme15.Core;

namespace Klemme15.App.ViewModels;

/// <summary>One moment in the car's warning history, for the timeline on the Faults page.</summary>
public sealed class StoryItem(StoryEvent e)
{
    public StoryEvent Event { get; } = e;
    public string When => Event.When;
    public string Title => Event.Title;
    public string Explanation => Event.Explanation;
    public bool IsRed => Event.Severity == CcSeverity.Critical && Event.Kind != StoryKind.LowVoltage;
    public bool IsBattery => Event.Kind is StoryKind.LowVoltage or StoryKind.BatteryWork;
    public bool IsOther => !IsRed && !IsBattery;
    public string Codes => string.Join("  ", Event.Messages.Select(m => m.Code)) + (Event.Times > Event.Messages.Count ? $"  ·  shown {Event.Times}×" : "");
}
