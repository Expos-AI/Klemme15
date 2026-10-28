using Klemme15.Core;

namespace Klemme15.Protocols.Simulation;

/// <summary>
/// Made-up module readings for the demo cars, so the Readings page can be tried without a car. Descriptions are
/// written in the German style of BMW's own files on purpose, so the demo exercises the same English labelling.
/// Values move a little on every read, like a car idling on the driveway. Nothing here is BMW data.
/// </summary>
public sealed class DemoReadings(VehicleModel? model) : IModuleReadings
{
    private readonly Random _random = new(15);
    private readonly bool _turbo = model?.EngineCode?.StartsWith("N54", StringComparison.OrdinalIgnoreCase) == true
                                || model?.EngineCode?.StartsWith("N55", StringComparison.OrdinalIgnoreCase) == true;

    private sealed record Demo(string Job, string Comment, Func<IReadOnlyList<ReadingValue>> Read);

    public string? VariantOf(EcuDefinition ecu) => "DEMO";

    public Task<IReadOnlyList<ModuleReading>> ListReadingsAsync(EcuDefinition ecu, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ModuleReading>>(For(ecu).Select(d => new ModuleReading(d.Job, d.Comment)).ToList());

    public async Task<IReadOnlyList<ReadingValue>> ReadAsync(EcuDefinition ecu, string job, CancellationToken ct = default)
    {
        await Task.Delay(40, ct);   // a real job takes a few tens of milliseconds
        return For(ecu).FirstOrDefault(d => d.Job == job)?.Read() ?? [];
    }

    public string PinSource(EcuDefinition ecu, string job, string result) => $"demo:{ecu.Id}/{job}/{result}";

    private double Jitter(double centre, double spread) => Math.Round(centre + (_random.NextDouble() - 0.5) * 2 * spread, 2);

    private static ReadingValue V(string name, double value, string unit, string comment) =>
        new(name, value.ToString(System.Globalization.CultureInfo.InvariantCulture), value, unit, comment);

    private static ReadingValue T(string name, string text, string comment) => new(name, text, null, null, comment);

    private IReadOnlyList<Demo> For(EcuDefinition ecu)
    {
        var id = ecu.Id.Split('.').Last();
        return id switch
        {
            "DME" =>
            [
                new("STATUS_LAMBDA_FRONT", "Lambdasonde vor Kat Bank 1 und 2", () =>
                [
                    V("STAT_LAMBDA_B1_WERT", Jitter(0.45, 0.35), "V", "Lambdasonde Spannung Bank 1"),
                    V("STAT_LAMBDA_B2_WERT", Jitter(0.45, 0.35), "V", "Lambdasonde Spannung Bank 2"),
                ]),
                new("STATUS_TRIMS", "Adaption Gemisch additiv und multiplikativ", () =>
                [
                    V("STAT_ADD_B1_WERT", Jitter(1.2, 0.3), "%", "Adaption Gemisch additiv Bank 1"),
                    V("STAT_ADD_B2_WERT", Jitter(0.6, 0.3), "%", "Adaption Gemisch additiv Bank 2"),
                    V("STAT_MUL_B1_WERT", Jitter(1.03, 0.01), "", "Adaption Gemisch multiplikativ Bank 1"),
                    V("STAT_MUL_B2_WERT", Jitter(1.01, 0.01), "", "Adaption Gemisch multiplikativ Bank 2"),
                ]),
                new("STATUS_VANOS", "VANOS Einlass und Auslass Position", () =>
                [
                    V("STAT_VANOS_EINLASS_WERT", Jitter(92, 1.5), "°CRK", "Einlassnockenwelle Position"),
                    V("STAT_VANOS_AUSLASS_WERT", Jitter(-88, 1.5), "°CRK", "Auslassnockenwelle Position"),
                ]),
                new("STATUS_LAUFRUHE", "Laufruhe Zylinder 1 bis 6", () =>
                    Enumerable.Range(1, 6).Select(c => V($"STAT_LAUFRUHE_ZYL{c}_WERT", Jitter(c == 3 ? 4.8 : 0.4, 0.3), "1/s²", $"Laufruhe Zylinder {c}")).ToList()),
                new("STATUS_KUEHLMITTEL", "Kuehlmitteltemperatur", () => [V("STAT_KUEHLMITTELTEMPERATUR_WERT", Jitter(91, 0.5), "°C", "Kuehlmitteltemperatur")]),
                new("STATUS_DROSSELKLAPPE", "Drosselklappe Winkel", () => [V("STAT_DROSSELKLAPPE_WERT", Jitter(1.6, 0.1), "%", "Drosselklappe Winkel")]),
                .. (_turbo
                    ? new Demo[] { new("STATUS_LADEDRUCK", "Ladedruck Soll und Ist", () =>
                      [
                          V("STAT_LADEDRUCK_SOLL_WERT", 1010, "hPa", "Ladedruck Soll"),
                          V("STAT_LADEDRUCK_IST_WERT", Jitter(1008, 6), "hPa", "Ladedruck Ist"),
                      ]) }
                    : []),
            ],
            "DSC" =>
            [
                new("STATUS_RADGESCHWINDIGKEIT", "Raddrehzahlen", () =>
                    new[] { "VL", "VR", "HL", "HR" }.Select(w => V($"STAT_RAD_{w}_WERT", 0, "km/h", $"Raddrehzahl {w switch { "VL" => "vorne links", "VR" => "vorne rechts", "HL" => "hinten links", _ => "hinten rechts" }}")).ToList()),
                new("STATUS_LENKWINKEL", "Lenkwinkelsensor", () => [V("STAT_LENKWINKEL_WERT", Jitter(-1.5, 0.2), "°", "Lenkwinkel")]),
                new("STATUS_BREMSLICHTSCHALTER", "Bremslichtschalter", () => [T("STAT_BLS_TEXT", "nicht betaetigt", "Bremslichtschalter")]),
            ],
            "JBE" or "GM5" =>
            [
                new("STATUS_IBS", "Intelligenter Batterie Sensor", () =>
                [
                    V("STAT_UBATT_WERT", Jitter(12.42, 0.02), "V", "Batteriespannung"),
                    V("STAT_IBATT_WERT", Jitter(-14.5, 1.5), "A", "Batteriestrom"),
                    V("STAT_SOC_WERT", 78, "%", "Batterie Ladezustand"),
                ]),
            ],
            "KOMBI" or "IKE" =>
            [
                new("STATUS_KOMBI", "Kombi Messwerte", () =>
                [
                    V("STAT_KILOMETERSTAND_WERT", 187_340, "km", "Kilometerstand"),
                    V("STAT_TANKINHALT_WERT", Jitter(38, 0.1), "l", "Tankfuellstand"),
                    V("STAT_AUSSENTEMPERATUR_WERT", Jitter(14, 0.2), "°C", "Aussentemperatur"),
                ]),
            ],
            "IHKA" =>
            [
                new("STATUS_TEMPERATUREN", "Klima Temperaturen", () =>
                [
                    V("STAT_INNENTEMPERATUR_WERT", Jitter(19.5, 0.2), "°C", "Innentemperatur"),
                    V("STAT_VERDAMPFER_WERT", Jitter(3.5, 0.3), "°C", "Verdampfer Temperatur"),
                ]),
            ],
            _ => [],
        };
    }
}
