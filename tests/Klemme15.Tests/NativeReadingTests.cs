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

    [Fact]
    public async Task Reads_a_value_with_22_and_scales_it()
    {
        var module = new FakeModule(r => [0x62, r[1], r[2], 0x50]);
        var readings = new NativeReadings(module, new FakeTables(), _ => "msv70");
        Assert.True(readings.Supports(Dme));
        var list = await readings.ListReadingsAsync(Dme);
        var values = await readings.ReadAsync(Dme, list[0].Job);
        Assert.Equal(new byte[] { 0x22, 0x42, 0x00 }, module.Sent[0]);
        Assert.Equal(12.0, values[0].Number);
        Assert.Equal("°C", values[0].Unit);
    }

    [Fact]
    public async Task A_refusal_is_explained()
    {
        var readings = new NativeReadings(new FakeModule(_ => [0x7F, 0x22, 0x31]), new FakeTables(), _ => "msv70");
        var e = await Assert.ThrowsAsync<DiagnosticException>(() => readings.ReadAsync(Dme, "STAT_ANSAUGLUFTTEMPERATUR_WERT"));
        Assert.Contains("declined", e.Message);
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
}
