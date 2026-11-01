using Klemme15.Core.Coding;
using Klemme15.Data;
using Xunit;

namespace Klemme15.Tests;

public class CodingLibraryTests
{
    private static VerifiedCoding Map(string module, string part = "6976405") => new()
    {
        Chassis = "E90", Module = module, PartNumber = part, CodingIndex = "0F",
        Toggles = new Dictionary<string, IReadOnlyList<CodingOp>> { ["led-turn-signals"] = [new CodingOp(0x3001, 5, 0x10, 0x10, 0)] },
    };

    [Fact]
    public void MapsForModulesKlemme15NeverCodesAreRejected()
    {
        Assert.True(Map("FRM").WellFormed);
        Assert.False(Map("CAS").WellFormed);
        Assert.False(Map("DME").WellFormed);
        Assert.False(Map("XYZ").WellFormed);
    }

    [Fact]
    public void ImportTakesSingleEntriesAndWholeFilesAndRemembersTheOrigin()
    {
        var dir = Path.Combine(Path.GetTempPath(), "k15-maps-" + Guid.NewGuid().ToString("N"));
        try
        {
            var db = VehicleDatabase.Load(dir);
            Assert.Equal(1, db.ImportCodingMaps(VerifiedCodingExport.ToJson(Map("FRM"))));
            Assert.Equal(1, db.ImportCodingMaps(CodingLearner.ToFileJson([Map("KOMBI", "9187317"), Map("CAS")])));
            Assert.Equal(0, db.ImportCodingMaps("not json at all"));
            var yours = db.CodingMaps.Where(m => m.Origin == CodingMapOrigin.Yours).ToList();
            Assert.Equal(2, yours.Count);
            Assert.Contains(yours, m => m.Map.Module == "KOMBI");

            // A downloaded community file sits between the shipped maps and yours.
            File.WriteAllText(Path.Combine(dir, VehicleDatabase.CommunityMapsFile), CodingLearner.ToFileJson([Map("JBE", "1234567")]));
            var again = VehicleDatabase.Load(dir);
            Assert.Contains(again.CodingMaps, m => m.Origin == CodingMapOrigin.Downloaded && m.Map.Module == "JBE");
            Assert.Equal(3, again.CodingMaps.Count(m => m.Origin != CodingMapOrigin.Shipped));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

public class CheckLibraryTests
{
    private static readonly Klemme15.Core.EcuDefinition Dme = new() { Id = "E9x.DME", Name = "Engine", Address = 0x12, Protocol = Klemme15.Core.BusProtocol.BmwFast };
    private static readonly Klemme15.Core.EcuDefinition Fzd = new() { Id = "E9x.FZD", Name = "Roof", Address = 0x56, Protocol = Klemme15.Core.BusProtocol.BmwFast };

    [Fact]
    public void ChecksLoadAndEveryOneHasSources()
    {
        var checks = GuideLibrary.LoadChecks().All;
        Assert.True(checks.Count >= 8);
        Assert.All(checks, c => Assert.NotEmpty(c.SourceList));
        Assert.All(checks, c => Assert.NotEmpty(c.Steps));
    }

    [Fact]
    public void FaultsFindTheirCheck()
    {
        var checks = GuideLibrary.LoadChecks();
        Klemme15.Core.Fault F(string code, string text) => new() { EcuId = "x", Code = code, Description = text, Source = Klemme15.Core.FaultSource.Bmw };
        Assert.Equal("check-vanos-solenoid", checks.For(F("2A82", "VANOS intake: control fault"), Dme)?.Id);
        Assert.Equal("check-ignition-coil", checks.For(F("P0303", "Cylinder 3 misfire detected") with { Source = Klemme15.Core.FaultSource.Obd2 }, Dme)?.Id);
        Assert.Equal("check-mirror-sensors", checks.For(F("A670", "Misting sensor: no communication"), Fzd)?.Id);
        Assert.Null(checks.For(F("1234", "Something unrelated entirely"), Dme));
    }
}
