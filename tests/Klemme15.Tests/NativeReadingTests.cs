using Klemme15.Core;
using Klemme15.Data;
using Klemme15.Protocols;
using Xunit;

namespace Klemme15.Tests;

public class NativeReadingTests
{
    private static MeasurementDef Def(string type, double mul = 1, double add = 0, bool lh = false) =>
        new(0x4200, "STAT_TEST_WERT", "Test", "°C", type, lh, mul, 1, add);

    [Theory]
    [InlineData("unsigned char", new byte[] { 0x50 }, 0.75, -48, 12.0)]                 // 80 × 0.75 − 48
    [InlineData("signed char", new byte[] { 0xF6 }, 1, 0, -10.0)]
    [InlineData("unsigned integer", new byte[] { 0x01, 0x2C }, 1, 0, 300.0)]           // big-endian by default
    [InlineData("signed integer", new byte[] { 0xFF, 0x38 }, 0.1, 0, -20.0)]
    [InlineData("unsigend int", new byte[] { 0x00, 0x0A }, 1, 0, 10.0)]                 // BMW's typo
    [InlineData("unsigned long", new byte[] { 0x00, 0x01, 0x00, 0x00 }, 1, 0, 65536.0)]
    [InlineData("0x0F", new byte[] { 0xF3 }, 1, 0, 3.0)]                                // bit mask
    [InlineData("motorola float", new byte[] { 0x40, 0x49, 0x0F, 0xDB }, 1, 0, 3.1416)] // 3.14159...
    [InlineData("-", new byte[] { 0x00, 0x64 }, 1, 0, 100.0)]                           // size from the reply
    public void Decodes_bmw_types(string type, byte[] data, double mul, double add, double expected) =>
        Assert.Equal(expected, NativeReadings.Decode(Def(type, mul, add), data));

    [Fact]
    public void Low_high_flag_reads_little_endian() =>
        Assert.Equal(300.0, NativeReadings.Decode(Def("unsigned integer", lh: true), new byte[] { 0x2C, 0x01 }));

    private sealed class FakeModule(Func<byte[], byte[]?> answer) : IRawRequests
    {
        public List<byte[]> Sent { get; } = [];
        public Task<byte[]?> RequestAsync(EcuDefinition ecu, byte[] request, CancellationToken ct = default)
        {
            Sent.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private sealed class FakeTables : IMeasurementTables
    {
        public bool Has(string variant) => variant == "msv70";
        public IReadOnlyList<MeasurementDef> MeasurementsOf(string variant) =>
            [new(0x4200, "STAT_ANSAUGLUFTTEMPERATUR_WERT", "Ansauglufttemperatur 1", "°C", "unsigned char", false, 0.75, 1, -48)];
    }

    private static readonly EcuDefinition Dme = new() { Id = "E9x.DME", Name = "Engine (DME)", Address = 0x12, Protocol = BusProtocol.BmwFast };

    /// <summary>Behaves like the MSV70 in a real E90: $22 for these ids is refused, $2C F0 / $21 F0 works.</summary>
    private static byte[]? Msv70(byte[] r, Dictionary<ushort, byte[]> values, List<ushort> defined)
    {
        switch (r[0])
        {
            case 0x22:
                return [0x7F, 0x22, 0x12];
            case 0x2C when r.Length == 3 && r[2] == 0x04:
                defined.Clear();
                return [0x6C, 0xF0];
            case 0x2C:
                for (var i = 2; i + 5 < r.Length; i += 6)
                {
                    var id = (ushort)(r[i + 3] << 8 | r[i + 4]);
                    if (!values.ContainsKey(id)) return [0x7F, 0x2C, 0x31];
                    defined.Add(id);
                }
                return [0x6C, 0xF0];
            case 0x21 when r[1] == 0xF0:
                return [0x61, 0xF0, .. defined.SelectMany(id => values[id])];
            default:
                return [0x7F, r[0], 0x11];
        }
    }

    [Fact]
    public async Task Reads_a_value_through_a_measurement_record_and_scales_it()
    {
        var defined = new List<ushort>();
        var module = new FakeModule(r => Msv70(r, new() { [0x4200] = [0x50] }, defined));
        var readings = new NativeReadings(module, new FakeTables(), _ => "msv70");
        Assert.True(readings.Supports(Dme));
        var list = await readings.ListReadingsAsync(Dme);
        var values = await readings.ReadAsync(Dme, list[0].Job);
        Assert.Equal(new byte[] { 0x2C, 0xF0, 0x04 }, module.Sent[0]);
        Assert.Equal(new byte[] { 0x2C, 0xF0, 0x02, 0x01, 0x01, 0x42, 0x00, 0x01 }, module.Sent[1]);
        Assert.Equal(new byte[] { 0x21, 0xF0 }, module.Sent[2]);
        Assert.Equal(12.0, values[0].Number);
        Assert.Equal("°C", values[0].Unit);
    }

    private sealed class ThreeValues : IMeasurementTables
    {
        public bool Has(string variant) => true;
        public IReadOnlyList<MeasurementDef> MeasurementsOf(string variant) =>
        [
            new(0x4200, "STAT_A_WERT", "a", "°C", "unsigned char", false, 0.75, 1, -48),
            new(0x5A85, "STAT_B_WERT", "b", "rpm", "unsigned integer", false, 1, 1, 0),
            new(0x5800, "STAT_MISSING_WERT", "c", "%", "unsigned char", false, 1, 1, 0),
        ];
    }

    [Fact]
    public async Task Reads_many_values_in_one_record_and_skips_ones_the_module_lacks()
    {
        var defined = new List<ushort>();
        var module = new FakeModule(r => Msv70(r, new() { [0x4200] = [0x50], [0x5A85] = [0x02, 0xEE] }, defined));
        var readings = new NativeReadings(module, new ThreeValues(), _ => "msv70");
        var values = await readings.ReadMeasurementsAsync(Dme, ["STAT_A_WERT", "STAT_B_WERT", "STAT_MISSING_WERT"]);
        Assert.Equal(12.0, values["STAT_A_WERT"]);
        Assert.Equal(750.0, values["STAT_B_WERT"]);
        Assert.False(values.ContainsKey("STAT_MISSING_WERT"));
        // The record is defined once for all three; the module refuses it, so the values are retried one by one.
        Assert.Equal(2 + 3 * 6, module.Sent[1].Length);
    }

    [Fact]
    public async Task Reads_many_readings_at_once()
    {
        var defined = new List<ushort>();
        var module = new FakeModule(r => Msv70(r, new() { [0x4200] = [0x50], [0x5A85] = [0x02, 0xEE] }, defined));
        var readings = new NativeReadings(module, new ThreeValues(), _ => "msv70");
        var values = await readings.ReadManyAsync(Dme, ["STAT_A_WERT", "STAT_B_WERT"]);
        Assert.Equal(12.0, values["STAT_A_WERT"][0].Number);
        Assert.Equal(750.0, values["STAT_B_WERT"][0].Number);
        Assert.Equal(3, module.Sent.Count);   // clear, define both, read once
    }

    [Fact]
    public void Units_are_readable() =>
        Assert.Equal(["°C", "° crank", "rpm", "mg/stroke", null, "l"],
            new[] { "degC", "Grad KW", "1/min", "mg/Hub", "0/1", "l manuell" }.Select(Units.Normalize).ToArray());

    [Fact]
    public async Task Falls_back_to_22_when_the_module_refuses_records()
    {
        var module = new FakeModule(r => r[0] == 0x22 ? [0x62, r[1], r[2], 0x50] : [0x7F, r[0], 0x11]);
        var readings = new NativeReadings(module, new FakeTables(), _ => "msv70");
        var values = await readings.ReadAsync(Dme, "STAT_ANSAUGLUFTTEMPERATUR_WERT");
        Assert.Equal(12.0, values[0].Number);
        Assert.Equal(new byte[] { 0x22, 0x42, 0x00 }, module.Sent[^1]);
    }

    [Fact]
    public async Task A_refusal_is_explained()
    {
        var defined = new List<ushort>();
        var readings = new NativeReadings(new FakeModule(r => Msv70(r, [], defined)), new FakeTables(), _ => "msv70");
        var e = await Assert.ThrowsAsync<DiagnosticException>(() => readings.ReadAsync(Dme, "STAT_ANSAUGLUFTTEMPERATUR_WERT"));
        Assert.Contains("doesn't offer", e.Message);
    }

    [Fact]
    public void Modules_without_a_table_are_not_offered() =>
        Assert.False(new NativeReadings(new FakeModule(_ => null), new FakeTables(), _ => "dsc_87").Supports(Dme));

    [Fact]
    public void Tables_load_from_the_data_folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"k15-tables-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(folder, "tables"));
        File.WriteAllText(Path.Combine(folder, "tables", "msv70.json"),
            """{"measurements":[{"id":"0x4200","result":"STAT_A_WERT","comment":"x","unit":"°C","type":"unsigned char","lh":false,"mul":0.75,"div":1,"add":-48}]}""");
        try
        {
            var t = new MeasurementTables(folder);
            Assert.True(t.Has("MSV70"));
            var m = Assert.Single(t.MeasurementsOf("msv70"));
            Assert.Equal(0x4200, m.Id);
            Assert.Equal(0.75, m.Mul);
            Assert.Equal(0.75, MeasurementTables.ParseBmwNumber("0,75"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Decodes_bmw_fast_identification_as_read_from_a_real_e90()
    {
        // MSV70 engine computer and MK60 DSC from a 2006 E90 323i (identification only, no VIN).
        var dme = NativeBackend.ParseKwpIdent(Convert.FromHexString("00000756168400040100333420060418040000000000000000000000FFFFFF"));
        Assert.Equal("7561684", dme.PartNumber);
        Assert.Equal("04", dme.CodingIndex);
        Assert.Equal("2006-04-18", dme.ProductionDate);
        var dsc = NativeBackend.ParseKwpIdent(Convert.FromHexString("000006775387080705B14C41200603291800 0BFD041E0D000000000000".Replace(" ", "")));
        Assert.Equal("6775387", dsc.PartNumber);
        Assert.Equal("08", dsc.HardwareVersion);
        Assert.Equal("2006-03-29", dsc.ProductionDate);
        Assert.Contains("Part 6775387", dsc.Summary);
        // Something that isn't this layout still gives the ASCII part number.
        Assert.Equal("7519308", NativeBackend.ParseKwpIdent(System.Text.Encoding.ASCII.GetBytes("7519308 MS43 HW 01")).PartNumber);
    }
}
