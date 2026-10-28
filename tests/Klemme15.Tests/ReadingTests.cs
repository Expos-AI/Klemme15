using Klemme15.Core;
using Klemme15.Data;
using Klemme15.Protocols.Simulation;
using Xunit;

namespace Klemme15.Tests;

public class ReadingTests
{
    [Theory]
    [InlineData("0x300A01     Ansauglufttemperatur auslesen NO_CON keine Vorraussetzungen", "Ansauglufttemperatur")]
    [InlineData("Auslesen des Drosselklappen Winkels KWP 2000* $21 57 TPS_AV ReadDataByLocalIdentifier", "Drosselklappen Winkels")]
    [InlineData("$22 40 0A Adaption Lambda Regelung lesen", "Adaption Lambda Regelung")]
    public void Comments_lose_protocol_noise(string comment, string expected) =>
        Assert.Equal(expected, ReadingLabels.CleanComment(comment));

    [Theory]
    [InlineData("STATUS_L_SONDE", "0x302101 Lambdasonde vor Kat Bank1 auslesen NO_CON keine Vorraussetzungen", "Oxygen sensors and mixture")]
    [InlineData("STATUS_AGK", "0x30C101 Abgasklappe auslesen NO_CON keine Vorraussetzungen", "Intake, exhaust, throttle and pedal")]
    [InlineData("STATUS_VANOS_EINLASS", null, "VANOS and camshafts")]
    [InlineData("STATUS_RADGESCHWINDIGKEIT", null, "Wheels, brakes and stability")]
    [InlineData("STATUS_EV3", null, "Fuel system")]
    [InlineData("STATUS_UBATT", null, "Battery, alternator and power")]
    public void Readings_land_in_sensible_groups(string job, string? comment, string group) =>
        Assert.Equal(group, ReadingLabels.GroupOf(job, comment));

    [Fact]
    public void Boilerplate_does_not_make_everything_a_misfire() =>
        Assert.NotEqual("Misfires and smooth running", ReadingLabels.GroupOf("STATUS_XYZ", "Irgendwas auslesen NO_CON keine Vorraussetzungen"));

    [Fact]
    public void Titles_are_English_when_the_glossary_knows_the_words()
    {
        Assert.Equal("Oxygen sensor before catalytic converter bank1",
            ReadingLabels.JobTitle("STATUS_L_SONDE", "0x302101 Lambdasonde vor Kat Bank1 auslesen NO_CON keine Vorraussetzungen"));
        Assert.Equal("Adaptation mixture additive bank1", ReadingLabels.ResultLabel("STAT_ADD_WERT1", "Adaption Gemisch additiv Bank1"));
        Assert.Equal("Coolant temperature", ReadingLabels.ResultLabel("STAT_KUEHLMITTELTEMPERATUR_WERT", null));
    }

    [Fact]
    public async Task Demo_readings_return_values_with_units()
    {
        var db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "k15-none"));
        var model = db.FindVehicle("E9x")!.Models.First();
        var dme = db.FindVehicle("E9x")!.Modules.First(m => m.IsEngine);
        var demo = new DemoReadings(model);
        var list = await demo.ListReadingsAsync(dme);
        Assert.NotEmpty(list);
        var values = await demo.ReadAsync(dme, list[0].Job);
        Assert.NotEmpty(values);
        Assert.All(values, v => Assert.NotNull(v.Unit));
    }
}
