using System.Globalization;

namespace Klemme15.Core;

/// <summary>Reads BMW measurement values by result name ("STAT_LAUFUNRUHE_ZYL1_WERT"). Missing ones are left out.</summary>
public interface IMeasurementReader
{
    Task<IReadOnlyDictionary<string, double>> ReadMeasurementsAsync(EcuDefinition ecu, IReadOnlyList<string> resultNames, CancellationToken ct = default);
}

/// <summary>
/// Named engine signals for guided tests ("rpm", "stft1", "rough3", "vanos_in_actual"), from whatever the connection
/// offers: the standard OBD values the engine computer reports, and BMW's own measurement values.
/// </summary>
public interface ISignalSource
{
    Task<IReadOnlyDictionary<string, double>> ReadAsync(IReadOnlyCollection<string> signals, CancellationToken ct = default);
}

public sealed class EngineSignals(IDiagnosticBackend backend, EcuDefinition engine, IMeasurementReader? measurements) : ISignalSource
{
    /// <summary>BMW result names that carry each signal on E90-family engine computers, in order of preference.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> BmwNames = new Dictionary<string, string[]>
    {
        ["rpm"] = ["STAT_MOTORDREHZAHL_WERT"],
        ["coolant"] = ["STAT_KUEHLMITTELTEMPERATUR_WERT", "STAT_MOTORTEMPERATUR_WERT"],
        ["voltage"] = ["STAT_UBATT_WERT"],
        ["stft1"] = ["STAT_INTEGRATOR_BANK1_WERT", "STAT_LAMBDA_INTEGRATOR_GRUPPE1_WERT"],
        ["stft2"] = ["STAT_INTEGRATOR_BANK2_WERT", "STAT_LAMBDA_INTEGRATOR_GRUPPE2_WERT"],
        ["ltft1"] = ["STAT_ADAPTION_MULTIPLIKATIV_BANK1_WERT", "STAT_LAMBDA_ADAPTION_MULTIPLIKATIV_GRUPPE1_WERT"],
        ["ltft2"] = ["STAT_ADAPTION_MULTIPLIKATIV_BANK2_WERT", "STAT_LAMBDA_ADAPTION_MULTIPLIKATIV_GRUPPE2_WERT"],
        ["vanos_in_target"] = ["STAT_NW_EINLASSSPREIZUNG_SOLL_WERT", "STAT_VVT_EINLASSSPREIZUNG_SOLL_WERT", "STAT_VANOS_SOLLPOSITION_EINLASS_B1_WERT"],
        ["vanos_in_actual"] = ["STAT_NW_EINLASSSPREIZUNG_WERT", "STAT_VANOS_ISTPOSITION_EINLASS_B1_WERT"],
        ["vanos_ex_target"] = ["STAT_NW_AUSLASS_SOLL_WERT", "STAT_VANOS_SOLLPOSITION_AUSLASS_B1_WERT"],
        ["vanos_ex_actual"] = ["STAT_NW_AUSLASSSPREIZUNG_WERT", "STAT_VANOS_ISTPOSITION_AUSLASS_B1_WERT"],
        ["rough1"] = ["STAT_LAUFUNRUHE_ZYL1_WERT"], ["rough2"] = ["STAT_LAUFUNRUHE_ZYL2_WERT"],
        ["rough3"] = ["STAT_LAUFUNRUHE_ZYL3_WERT"], ["rough4"] = ["STAT_LAUFUNRUHE_ZYL4_WERT"],
        ["rough5"] = ["STAT_LAUFUNRUHE_ZYL5_WERT"], ["rough6"] = ["STAT_LAUFUNRUHE_ZYL6_WERT"],
        ["rough7"] = ["STAT_LAUFUNRUHE_ZYL7_WERT"], ["rough8"] = ["STAT_LAUFUNRUHE_ZYL8_WERT"],
    };

    private static readonly HashSet<string> ObdIds = ["rpm", "coolant", "voltage", "stft1", "stft2", "ltft1", "ltft2", "speed"];

    /// <summary>Reads the signals it can. BMW's fuel trims are factors (1.05 = +5 %); they are turned into percent.</summary>
    public async Task<IReadOnlyDictionary<string, double>> ReadAsync(IReadOnlyCollection<string> signals, CancellationToken ct = default)
    {
        var values = new Dictionary<string, double>();
        var obd = backend.GetLiveParameters(engine).Where(p => ObdIds.Contains(p.Id) && signals.Contains(p.Id)).ToList();
        if (obd.Count > 0)
        {
            try
            {
                foreach (var v in await backend.ReadLiveAsync(engine, obd, ct))
                    if (v.Value is { } d) values[v.Parameter.Id] = d;
            }
            catch (Exception e) when (e is DiagnosticException or TimeoutException or NotSupportedException) { }
        }

        var missing = signals.Where(s => !values.ContainsKey(s) && BmwNames.ContainsKey(s)).ToList();
        if (missing.Count > 0 && measurements is not null)
        {
            var names = missing.SelectMany(s => BmwNames[s]).Distinct().ToList();
            IReadOnlyDictionary<string, double> read;
            try { read = await measurements.ReadMeasurementsAsync(engine, names, ct); }
            catch (Exception e) when (e is DiagnosticException or TimeoutException or NotSupportedException) { read = new Dictionary<string, double>(); }
            foreach (var s in missing)
                if (BmwNames[s].Select(n => read.TryGetValue(n, out var d) ? d : (double?)null).FirstOrDefault(d => d is not null) is { } value)
                    values[s] = s.StartsWith("stft", StringComparison.Ordinal) || s.StartsWith("ltft", StringComparison.Ordinal) ? TrimPercent(value) : value;
        }
        return values;
    }

    /// <summary>BMW reports mixture corrections as a factor around 1.0 (sometimes as percent already).</summary>
    public static double TrimPercent(double value) => value is > 0.5 and < 1.5 ? (value - 1) * 100 : value;

    public static string Format(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);
}
