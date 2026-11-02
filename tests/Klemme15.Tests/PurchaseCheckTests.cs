using Klemme15.Core;
using Xunit;

namespace Klemme15.Tests;

public class PurchaseCheckTests
{
    private static readonly EcuDefinition Dsc = new() { Id = "E9x.DSC", Name = "Stability control (DSC)", Address = 0x29, Protocol = BusProtocol.BmwFast, Category = "Chassis" };
    private static readonly EcuDefinition Dme = new() { Id = "E9x.DME", Name = "Engine (DME)", Address = 0x12, Protocol = BusProtocol.BmwFast, Category = "Powertrain" };

    private static Fault F(string code, FaultState state, string? km = null) => new()
    {
        EcuId = "x", Code = code, Source = FaultSource.Bmw, State = state, Description = "Something",
        Details = km is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Mileage"] = km },
    };

    private static VehicleScanResult Scan(params (EcuDefinition Ecu, Fault[] Faults)[] ecus) => new()
    {
        Started = DateTimeOffset.Now, Backend = "test", BatteryVoltage = 12.5,
        Ecus = ecus.Select(e => new EcuScanResult { Ecu = e.Ecu, Status = e.Faults.Length > 0 ? EcuScanStatus.Faults : EcuScanStatus.Ok, Faults = e.Faults }).ToList(),
    };

    [Theory]
    [InlineData("123456 km", 123456)]
    [InlineData("123.456 km", 123456)]
    [InlineData("123,456", 123456)]
    [InlineData("123456.0 km", 123456)]
    public void MileageIsReadFromRecords(string text, long km) => Assert.Equal(km, PurchaseCheck.ParseKm(text));

    [Fact]
    public void AWoundBackOdometerIsCaught()
    {
        var scan = Scan((Dme, [F("2A82", FaultState.Stored, "212000 km")]));
        var report = PurchaseCheck.Assess(scan, null, odometerKm: 150_000);
        Assert.Equal("Mileage doesn't add up", report.Items[0].Title);
        Assert.Equal(CheckLevel.Warn, report.Items[0].Level);
        Assert.Contains(report.Items, i => i.Title == "Mileage doesn't add up");
        Assert.DoesNotContain(PurchaseCheck.Assess(scan, null, odometerKm: 212_300).Items, i => i.Title == "Mileage doesn't add up");
    }

    [Fact]
    public void ActiveSafetyFaultsAreWarnings()
    {
        var report = PurchaseCheck.Assess(Scan((Dsc, [F("5E1A", FaultState.Present)]), (Dme, [])), null);
        Assert.Contains(report.Items, i => i.Level == CheckLevel.Warn && i.Title.Contains("chassis"));
        Assert.True(report.Warnings >= 1);
    }

    [Fact]
    public void ClearedMemoriesRaiseAQuestionAndLimitsAreStated()
    {
        var empty = Enumerable.Range(0, 6).Select(i => (new EcuDefinition { Id = $"E9x.M{i}", Name = $"M{i}", Address = (byte)i, Protocol = BusProtocol.BmwFast }, Array.Empty<Fault>())).ToArray();
        var report = PurchaseCheck.Assess(Scan(empty), null);
        Assert.Contains(report.Items, i => i.Title == "Fault memories look recently cleared");
        Assert.Contains(report.NotChecked, n => n.StartsWith("Mileage cross-check"));
        Assert.Contains(report.NotChecked, n => n.StartsWith("Rust"));
    }
}
