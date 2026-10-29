using Klemme15.Core;
using Klemme15.Data;
using Xunit;

namespace Klemme15.Tests;

public class GuideTests
{
    private static readonly VehicleDatabase Db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "k15-none"));
    private static EcuDefinition M(string id) => Db.Modules.First(m => m.Id == id);

    private static string? GuideFor(string ecu, string code, string? text = null, FaultSource source = FaultSource.Bmw) =>
        Db.Guides.For(new Fault { EcuId = ecu, Code = code, Source = source, Description = text }, M(ecu))?.Id;

    [Fact]
    public void Guides_load_and_every_step_has_text()
    {
        Assert.True(Db.Guides.All.Count >= 10);
        Assert.All(Db.Guides.All, g => Assert.All(g.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Text))));
    }

    [Theory]
    [InlineData("E9x.DME", "P0171", null, "mixture")]
    [InlineData("E9x.DME", "P0303", null, "misfire")]
    [InlineData("E9x.DME", "29CC", "Misfires were detected on more than one cylinder.", "misfire")]
    [InlineData("E9x.DME", "2A82", "The intake VANOS (camshaft timing) unit is stiff or mechanically stuck.", "vanos")]
    [InlineData("E9x.DSC", "5DC0", "Speed sensor right rear electrically defective", "wheel-speed")]
    [InlineData("E9x.DSC", "5E40", "Steering angle sensor signal implausible offset", "steering-angle")]
    [InlineData("E9x.KOMBI", "A3B9", "The cluster got a faulty or missing stability-control (DSC) status message on the CAN bus.", "bus")]
    [InlineData("E9x.ACSM", "93A0", "Airbag Beifahrer Zuendkreis", "airbag")]
    public void Faults_get_the_right_guide(string ecu, string code, string? text, string expected) =>
        Assert.Equal(expected, GuideFor(ecu, code, text, code.StartsWith('P') ? FaultSource.Obd2 : FaultSource.Bmw));

    [Fact]
    public void Junction_box_relay_and_footwell_faults_get_their_guides()
    {
        Assert.Equal("battery-drain", GuideFor("E9x.JBE", "A731", "Junction box reports a problem with the terminal 30g_f supply circuit."));
        Assert.Equal("frm3", GuideFor("E9x.KOMBI", "A3B4", "The cluster got a faulty lighting-status signal from the footwell module (FRM)."));
        Assert.Equal("trunk-harness", GuideFor("E9x.FRM", "9C9C", "Number plate light left: open circuit"));
    }

    [Fact]
    public void Reference_guides_have_sources_and_never_attach_to_faults()
    {
        var reference = Db.Guides.All.Where(g => g.ReferenceOnly).ToList();
        Assert.Contains(reference, g => g.Id == "where-is-it");
        Assert.All(Db.Guides.All.Where(g => g.Id is "where-is-it" or "fuses-and-power" or "battery-drain" or "frm3"), g => Assert.NotEmpty(g.SourceList));
        Assert.Null(GuideFor("E9x.FRM", "1234", "Where is the module located"));
    }

    [Fact]
    public void Whole_words_only_so_clean_is_not_lean() =>
        Assert.Null(GuideFor("E9x.DME", "1234", "Clean the throttle body"));

    [Fact]
    public void Engine_specific_steps_are_filtered()
    {
        var misfire = Db.Guides.All.First(g => g.Id == "misfire");
        Assert.True(misfire.StepsFor("N54").Count > misfire.StepsFor("M54").Count);
    }
}
