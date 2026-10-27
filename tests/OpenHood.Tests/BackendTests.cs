using OpenHood.Core;
using OpenHood.Data;
using OpenHood.Protocols;
using OpenHood.Protocols.Simulation;
using OpenHood.Protocols.Transport;
using Xunit;

namespace OpenHood.Tests;

public class DatabaseTests
{
    [Fact]
    public void BuiltInDatabaseLoadsCleanly()
    {
        var db = VehicleDatabase.Load(overrideFolder: Path.Combine(Path.GetTempPath(), "openhood-no-overrides"));
        Assert.Empty(db.LoadWarnings);
        Assert.NotNull(db.FindVehicle("E46"));
        Assert.NotNull(db.FindVehicle("E9x"));
        Assert.All(db.Vehicles, v => Assert.Contains(v.Modules, m => m.IsEngine));
        Assert.All(db.Vehicles, v => Assert.Equal(v.Modules.Count, v.Modules.Select(m => m.Address).Distinct().Count()));
        Assert.StartsWith("System too lean, bank 1", db.DescribeObd("P0171"));
        Assert.Contains("manufacturer-specific", db.DescribeObd("P1523"));
    }

    [Fact]
    public void UserFolderOverridesBuiltInEntries()
    {
        var folder = Directory.CreateTempSubdirectory("openhood-db").FullName;
        File.WriteAllText(Path.Combine(folder, "bmw-codes.json"),
            """{ "modules": { "E9x.DME": { "2A82": { "text": "Test description" } } } }""");
        var db = VehicleDatabase.Load(folder);
        Assert.Equal("Test description", db.DescribeBmw("E9x.DME", "2A82"));
        Assert.Null(db.DescribeBmw("E9x.DME", "FFFF"));
    }
}

public class NativeBackendTests
{
    private static readonly VehicleDatabase Db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "openhood-no-overrides"));

    private static EcuDefinition Module(string id) => Db.Modules.Single(m => m.Id == id);

    [Fact]
    public async Task E90ScanFindsModulesFaultsAndVin()
    {
        var car = VirtualCar.E90_325i();
        await using var backend = new NativeBackend(car, new NativeBackendOptions { Mode = CableMode.DCan, ProbeTimeout = TimeSpan.FromMilliseconds(80) });
        await backend.ConnectAsync();

        var family = Db.FindVehicle("E9x")!;
        var scan = await new VehicleScanner(backend, Db).ScanAsync(family.Modules, "E90 325i");

        Assert.Equal(VirtualCar.Vin, scan.Vin);
        Assert.Equal(car.Ecus.Count, scan.RespondingEcus);
        Assert.All(scan.Ecus.Where(e => e.Status == EcuScanStatus.NoResponse), e => Assert.DoesNotContain(car.Ecus, v => v.Address == e.Ecu.Address));

        var dme = scan.Ecus.Single(e => e.Ecu.Id == "E9x.DME");
        Assert.Equal("7562350", dme.Identity!.PartNumber);
        Assert.Contains(dme.Faults, f => f.Code == "2A82" && f.State == FaultState.Present && f.Source == FaultSource.Bmw);
        Assert.Contains(dme.Faults, f => f.Code == "2C2E" && f.State == FaultState.Stored);
        var lean = Assert.Single(dme.Faults, f => f.Code == "P0171");
        Assert.StartsWith("System too lean", lean.Description);

        Assert.Equal(6, scan.TotalFaults);
        Assert.Contains("P0171", ReportWriter.ToHtml(scan));
        Assert.Contains("| Engine (DME) | 0x12 | Faults | 4 |", ReportWriter.ToMarkdown(scan));
        Assert.Contains("\"Vin\": \"" + VirtualCar.Vin + "\"", ReportWriter.ToJson(scan));
    }

    [Fact]
    public async Task E90ClearFaultsEmptiesMemory()
    {
        var car = VirtualCar.E90_325i();
        await using var backend = new NativeBackend(car, new NativeBackendOptions { Mode = CableMode.DCan });
        await backend.ConnectAsync();
        var dme = Module("E9x.DME");
        Assert.NotEmpty(await backend.ReadFaultsAsync(dme));
        await backend.ClearFaultsAsync(dme);
        Assert.Empty(await backend.ReadFaultsAsync(dme));
    }

    [Fact]
    public async Task E90LiveDataDecodes()
    {
        await using var backend = new NativeBackend(VirtualCar.E90_325i(), new NativeBackendOptions { Mode = CableMode.DCan });
        await backend.ConnectAsync();
        var dme = Module("E9x.DME");
        var parameters = backend.GetLiveParameters(dme).Where(p => p.Id is "rpm" or "coolant" or "voltage").ToList();
        var values = await backend.ReadLiveAsync(dme, parameters);
        Assert.InRange(values.Single(v => v.Parameter.Id == "rpm").Value!.Value, 600, 2500);
        Assert.InRange(values.Single(v => v.Parameter.Id == "coolant").Value!.Value, 20, 95);
        Assert.Equal(14.1, values.Single(v => v.Parameter.Id == "voltage").Value);
        Assert.Empty(backend.GetLiveParameters(Module("E9x.KOMBI")));
    }

    [Fact]
    public async Task E46UsesDs2ForIdentAndObdForEngineFaults()
    {
        var car = VirtualCar.E46_325i();
        var log = new TrafficLog();
        await using var backend = new NativeBackend(new LoggingTransport(car, log), new NativeBackendOptions { Mode = CableMode.KLine, ProbeTimeout = TimeSpan.FromMilliseconds(80) });
        await backend.ConnectAsync();

        var dme = Module("E46.DME");
        var ident = await backend.IdentifyAsync(dme);
        Assert.Equal("7519308", ident!.PartNumber);
        Assert.Null(await backend.IdentifyAsync(Module("E46.MRS")));   // not fitted in the virtual car

        var faults = await backend.ReadFaultsAsync(dme);
        Assert.Equal(["P0171", "P0174", "P0411"], faults.Select(f => f.Code).Order().ToArray());
        Assert.Equal(VirtualCar.Vin, await backend.ReadVinAsync());

        var rpm = (await backend.ReadLiveAsync(dme, [Obd2.AllParameters.Single(p => p.Id == "rpm")])).Single();
        Assert.NotNull(rpm.Value);

        // Switching back to DS2 after OBD must still work.
        Assert.NotNull(await backend.IdentifyAsync(Module("E46.IKE")));
        Assert.Contains(log.Snapshot(), e => e.Note?.Contains("9600") == true);
        Assert.Contains(log.Snapshot(), e => e.Note?.Contains("10400") == true);
    }

    [Fact]
    public async Task E46ScanReportsDs2FaultLimitationAsNote()
    {
        await using var backend = new NativeBackend(VirtualCar.E46_325i(), new NativeBackendOptions { Mode = CableMode.KLine, ProbeTimeout = TimeSpan.FromMilliseconds(80) });
        await backend.ConnectAsync();
        var scan = await new VehicleScanner(backend, Db).ScanAsync(Db.FindVehicle("E46")!.Modules, "E46 325i");
        Assert.Equal(6, scan.RespondingEcus);
        var ike = scan.Ecus.Single(e => e.Ecu.Id == "E46.IKE");
        Assert.Equal(EcuScanStatus.Ok, ike.Status);
        Assert.Contains("EDIABAS", ike.Note);
        Assert.Equal(EcuScanStatus.Faults, scan.Ecus.Single(e => e.Ecu.Id == "E46.DME").Status);
    }

    [Fact]
    public async Task IgnitionOffGivesClearError()
    {
        var car = VirtualCar.E90_325i();
        car.IgnitionOn = false;
        await using var backend = new NativeBackend(car, new NativeBackendOptions { RequireIgnition = true });
        var e = await Assert.ThrowsAsync<DiagnosticException>(() => backend.ConnectAsync());
        Assert.Contains("Ignition", e.Message);
    }

    [Fact]
    public async Task ScanCanBeCancelled()
    {
        await using var backend = new NativeBackend(VirtualCar.E90_325i(), new NativeBackendOptions { ProbeTimeout = TimeSpan.FromMilliseconds(80) });
        await backend.ConnectAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var scan = await new VehicleScanner(backend, Db).ScanAsync(Db.FindVehicle("E9x")!.Modules, null, ct: cts.Token);
        Assert.True(scan.Cancelled);
        Assert.Empty(scan.Ecus);
    }
}
