using Klemme15.Core;

namespace Klemme15.App.ViewModels;

/// <summary>One finding from comparing scans over time, for the "Health over time" card.</summary>
public sealed class TrendItem(HealthTrend t)
{
    public string Title => t.Title;
    public string Detail => t.Detail;
    public bool IsAct => t.Level == TrendLevel.Act;
    public bool IsWatch => t.Level == TrendLevel.Watch;
    public bool IsInfo => t.Level == TrendLevel.Info;
}
