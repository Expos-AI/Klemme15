using Klemme15.Core;
using Xunit;

namespace Klemme15.Tests;

public class RootCauseTests
{
    private static EcuDefinition M(string id, string name) => new() { Id = "E9x." + id, Name = name, Address = (byte)id.GetHashCode(), Protocol = BusProtocol.BmwFast };
    private static readonly EcuDefinition Dsc = M("DSC", "Stability control (DSC)"), Frm = M("FRM", "Footwell module (FRM)"),
        Kombi = M("KOMBI", "Instrument cluster (KOMBI)"), Egs = M("EGS", "Automatic transmission (EGS)"), Dme = M("DME", "Engine (DME)");

    private static Fault F(string code, string text, string? km = null) => new()
    {
        EcuId = "x", Code = code, Source = FaultSource.Bmw, State = FaultState.Stored, Description = text,
        Details = km is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Mileage"] = km },
    };

    private static VehicleScanResult Scan(params (EcuDefinition Ecu, Fault[] Faults)[] ecus) => new()
    {
        Started = DateTimeOffset.Now, Backend = "test", BatteryVoltage = 11.9,
        Ecus = ecus.Select(e => new EcuScanResult { Ecu = e.Ecu, Status = e.Faults.Length > 0 ? EcuScanStatus.Faults : EcuScanStatus.Ok, Faults = e.Faults }).ToList(),
    };

    [Fact]
    public void ALowVoltageEventIsOneCauseNotMany()
    {
        var groups = RootCause.Find(Scan(
            (Frm, [F("A0B4", "Undervoltage detected", "98112 km")]),
            (Kombi, [F("A3B3", "Supply voltage too low", "98112 km")]),
            (Egs, [F("5088", "Battery voltage low", "98116 km")]),
            (Dme, [F("2A82", "VANOS intake stiff")])));
        var g = Assert.Single(groups);
        Assert.Equal("3 faults point at one low-voltage event", g.Title);
        Assert.Contains("all at about 98,112 km", g.Detail);
        Assert.Contains("11.9 V", g.Detail);
        Assert.Equal(["FRM A0B4", "KOMBI A3B3", "EGS 5088"], g.Faults);
    }

    [Fact]
    public void ModulesThatAllMissTheSameOnePointAtIt()
    {
        var groups = RootCause.Find(Scan(
            (Kombi, [F("D35A", "Message missing from DSC")]),
            (Egs, [F("CF17", "No message from DSC, timeout")]),
            (Frm, [F("E584", "Timeout, message (wheel speed) from DSC")]),
            (Dsc, [])));
        var g = Assert.Single(groups);
        Assert.Equal("3 modules lost contact with Stability control (DSC)", g.Title);
        Assert.Contains("Look at DSC first", g.Detail);
        Assert.Contains("answers now", g.Detail);
    }

    [Fact]
    public void FaultsFromTheSameMomentAreGroupedAndLoneFaultsAreLeftAlone()
    {
        var groups = RootCause.Find(Scan(
            (Dsc, [F("5E20", "Wheel speed sensor front left", "120040 km"), F("5E21", "Pressure sensor", "60000 km")]),
            (Egs, [F("4F81", "Ratio monitoring", "120044 km")]),
            (Dme, [F("2A82", "VANOS intake stiff", "120046 km")])));
        var g = Assert.Single(groups);
        Assert.StartsWith("3 faults were recorded at about 120,040 km", g.Title);
        Assert.Empty(RootCause.Find(Scan((Dme, [F("2A82", "VANOS intake stiff"), F("29CD", "Misfire cylinder 1")]))));
    }
}

public class PartSearchTests
{
    [Theory]
    [InlineData("VANOS solenoids, front of cylinder head", "BMW E90 328i N52 VANOS solenoids")]
    [InlineData("Cylinder 3: ignition coil, spark plug, injector", "BMW E90 328i N52 ignition coil spark plug injector")]
    [InlineData("Thermostat (front of engine)", "BMW E90 328i N52 Thermostat")]
    [InlineData("Oxygen sensor before catalyst, cylinders 1-3", "BMW E90 328i N52 Oxygen sensor before catalyst")]
    public void TheSearchNamesTheCarAndThePart(string component, string query)
    {
        var db = Klemme15.Data.VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "klemme15-no-overrides"));
        var family = db.FindVehicle("E9x")!;
        Assert.Equal(query, PartSearch.Query(component, family, family.Models.Single(m => m.Id == "E9x-328i")));
        Assert.StartsWith("https://duckduckgo.com/?q=BMW%20E90", PartSearch.Url(query));
    }
}
