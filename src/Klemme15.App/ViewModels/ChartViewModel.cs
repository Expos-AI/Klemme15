using Klemme15.Core;

namespace Klemme15.App.ViewModels;

/// <summary>One graph on the Tests page: every line that shares a unit.</summary>
public sealed class ChartViewModel(string unit, IReadOnlyList<TestTrace> traces)
{
    public string Unit { get; } = unit;
    public IReadOnlyList<TestTrace> Traces { get; } = traces;

    public string Title => Unit switch
    {
        "rpm" => "Engine speed (rpm)",
        "%" => Traces.Any(t => t.Name.Contains("trim", StringComparison.OrdinalIgnoreCase)) ? "Fuel trim (%)" : "Command (%)",
        "°" => "Camshaft position (°)",
        "V" => "Battery voltage (V)",
        "pump" => "Water pump speed (as the engine computer reports it)",
        _ => Unit,
    };

    public static IReadOnlyList<ChartViewModel> From(IReadOnlyList<TestTrace>? traces) =>
        traces is null ? [] : traces.GroupBy(t => t.Unit).Select(g => new ChartViewModel(g.Key, g.ToList())).ToList();
}
