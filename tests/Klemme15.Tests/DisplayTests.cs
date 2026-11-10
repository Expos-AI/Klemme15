using Klemme15.Core;
using Klemme15.Data;
using Xunit;

namespace Klemme15.Tests;

public class DisplayTests
{
    [Fact]
    public void UsUnitsConvertWhatPeopleReadAndLeaveTheRestAlone()
    {
        try
        {
            DisplayUnits.System = UnitSystem.US;
            Assert.Equal((194.0, "°F"), DisplayUnits.Convert(90, "°C"));
            Assert.Equal("mph", DisplayUnits.Convert(100, "km/h").Unit);
            Assert.Equal(62.1, DisplayUnits.Convert(100, "km/h").Value, 1);
            Assert.Equal(14.5, DisplayUnits.Convert(100, "kPa").Value, 1);
            Assert.Equal((750.0, "rpm"), DisplayUnits.Convert(750, "rpm"));
            Assert.Equal("60,969 mi", DisplayUnits.Distance(98_120));
            Assert.Equal("194 °F", DisplayUnits.Temperature(90));
            DisplayUnits.System = UnitSystem.Metric;
            Assert.Equal((90.0, "°C"), DisplayUnits.Convert(90, "°C"));
            Assert.Equal("98,120 km", DisplayUnits.Distance(98_120));
        }
        finally { DisplayUnits.System = UnitSystem.Metric; }
        Assert.Equal(UnitSystem.US, DisplayUnits.ForRegion("US"));
        Assert.Equal(UnitSystem.Metric, DisplayUnits.ForRegion("DE"));
    }

    [Fact]
    public void EveryModuleADemoCarHasIsExplainedInPlainWords()
    {
        var db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "klemme15-no-overrides"));
        foreach (var family in db.Vehicles)
            foreach (var model in family.Models)
                foreach (var id in model.ModuleIds)
                {
                    var ecu = family.Modules.Single(m => m.Id == id);
                    Assert.True(ModuleGlossary.For(ecu) is { Length: > 10 }, $"{id} has no plain-word description");
                }
    }
}
