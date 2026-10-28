using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Klemme15.Core;
using Klemme15.Data;
using Xunit;

namespace Klemme15.Tests;

public class OfficialFaultTests
{
    /// <summary>In-memory stand-in for a user's BMW data. Texts are invented for the tests.</summary>
    private sealed class FakeSource(string name = "fake") : IOfficialFaultSource
    {
        public readonly Dictionary<string, Dictionary<int, OfficialEntry>> Codes = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string[]> Engines = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string[]> Parts = new(StringComparer.OrdinalIgnoreCase);
        public string Name => name;
        public bool HasVariant(string variant) => Codes.ContainsKey(variant);
        public OfficialEntry? Lookup(string variant, int code) => Codes.GetValueOrDefault(variant)?.GetValueOrDefault(code);
        public IReadOnlyCollection<string> EnginesOf(string variant) => Engines.GetValueOrDefault(variant) ?? [];
        public IReadOnlyCollection<string> PartNumbersOf(string variant) => Parts.GetValueOrDefault(variant) ?? [];
    }

    private static readonly EcuDefinition Dsc = new()
    {
        Id = "E9x.DSC", Name = "DSC", Address = 0x29, Protocol = BusProtocol.BmwFast, Variants = ["dsc_a", "dsc_b"],
    };

    private static FakeSource TwoDscs()
    {
        var s = new FakeSource();
        s.Codes["dsc_a"] = new() { [0x5E1A] = new("5E1A - Text A auf Deutsch.", "5E1A - text A in English.") };
        s.Codes["dsc_b"] = new() { [0x5E1A] = new("5E1A - Ganz anderer Text B.", null) };
        s.Parts["dsc_b"] = ["6789012"];
        return s;
    }

    [Fact]
    public void Same_code_on_two_variants_shows_the_first_and_lists_the_other()
    {
        var m = OfficialLookup.Find([TwoDscs()], Dsc, null, null, "5E1A")!;
        Assert.Equal("DSC_A", m.Variant);
        Assert.False(m.VariantConfirmed);
        Assert.Single(m.Alternatives);
        Assert.StartsWith("DSC_B: ", m.Alternatives[0]);
    }

    [Fact]
    public void Part_number_from_the_module_confirms_the_variant()
    {
        var m = OfficialLookup.Find([TwoDscs()], Dsc, new EcuIdentity { PartNumber = "6 789 012" }, null, "5E1A")!;
        Assert.Equal("DSC_B", m.Variant);
        Assert.True(m.VariantConfirmed);
    }

    [Fact]
    public void Variant_named_by_EDIABAS_wins()
    {
        var m = OfficialLookup.Find([TwoDscs()], Dsc, new EcuIdentity { Variant = "DSC_B" }, null, "0x5E1A")!;
        Assert.Equal("DSC_B", m.Variant);
        Assert.True(m.VariantConfirmed);
    }

    [Fact]
    public void Engine_computer_prefers_the_exact_engine_code()
    {
        var dme = Dsc with { Id = "E9x.DME", Variants = ["dme_new", "dme_old", "dme_diesel"] };
        var s = new FakeSource();
        foreach (var v in dme.Variants) s.Codes[v] = new() { [0x2A82] = new($"Text {v}", null) };
        s.Engines["dme_new"] = ["N52KP", "N51"];
        s.Engines["dme_old"] = ["N52"];
        s.Engines["dme_diesel"] = ["N47"];
        var m = OfficialLookup.Find([s], dme, null, "N52", "2A82")!;
        Assert.Equal("DME_OLD", m.Variant);
        Assert.DoesNotContain(m.Alternatives, a => a.StartsWith("DME_DIESEL", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("2A82 Einlass-VANOS", "2A82", "Einlass-VANOS")]
    [InlineData("5E1A - CAN DME/DDE Signal Fehler.", "5E1A", "CAN DME/DDE Signal Fehler")]
    [InlineData("1234 Text that starts with another number", "2A82", "1234 Text that starts with another number")]
    [InlineData("Radgeschwindigkeit &lt; 300km/h", "0001", "Radgeschwindigkeit < 300km/h")]
    public void Clean_removes_only_the_repeated_code(string text, string code, string expected) =>
        Assert.Equal(expected, OfficialLookup.Clean(text, code));

    [Theory]
    [InlineData("Lambdasonde nach Katalysator, Alterung", "Oxygen sensor after catalytic converter, ageing")]
    [InlineData("Verbrennungsaussetzer, Zylinder 2: Einspritzabschaltung", "Misfire, cylinder 2: fuel cut-off")]
    [InlineData("Kurbelwellensensor, Signal", "Crankshaft sensor, signal")]
    [InlineData("FEHLER_EXTERNAL_WATCHDOG", "Fault external watchdog")]
    public void Glossary_translates_common_texts(string german, string english) =>
        Assert.Equal(english, GermanGlossary.Translate(german));

    [Fact]
    public void Polish_fixes_half_translated_English_and_leaves_English_alone()
    {
        Assert.Equal("Speed sensor right rear electrically defective", GermanGlossary.Polish("Drehzahlfuehler right rear electrically defective"));
        Assert.Equal("Steering angle sensor signal implausible offset", GermanGlossary.Polish("steering angle sensor signal implausible offset"));
    }

    [Fact]
    public void Glossary_gives_up_on_mostly_unknown_text() =>
        Assert.Null(GermanGlossary.Translate("Quatschwort Blubberding Fantasietext Signal"));

    [Fact]
    public void Describe_prefers_BMW_English_then_community_then_translated_German()
    {
        var db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "k15-none"));
        var dsc = db.Modules.First(m => m.Id == "E9x.DSC") with { Variants = ["dsc_a", "dsc_b"] };
        var src = TwoDscs();
        src.Codes["dsc_a"][0x0001] = new("Lambdasonde nach Katalysator, Alterung", null);
        db.SetOfficialSources([src]);

        var en = db.Describe(new Fault { EcuId = dsc.Id, Code = "5E1A", Source = FaultSource.Bmw }, dsc, null, null);
        Assert.Equal("bmw-en", en.DescriptionOrigin);
        Assert.Equal("Text A in English", en.Description);
        Assert.Equal("Text A auf Deutsch", en.OfficialText);

        var de = db.Describe(new Fault { EcuId = dsc.Id, Code = "0001", Source = FaultSource.Bmw }, dsc, null, null);
        Assert.Equal("bmw-de-glossary", de.DescriptionOrigin);
        Assert.Equal("Oxygen sensor after catalytic converter, ageing", de.Description);

        var none = db.Describe(new Fault { EcuId = dsc.Id, Code = "FFFF", Source = FaultSource.Bmw }, dsc, null, null);
        Assert.Null(none.OfficialText);
    }

    [Fact]
    public void Install_keeps_wanted_variants_and_reads_them()
    {
        static void Add(TarWriter tar, string name, string json)
        {
            var e = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(json)) };
            tar.WriteEntry(e);
        }
        var archive = Path.Combine(Path.GetTempPath(), $"k15-{Guid.NewGuid():N}.tar.gz");
        using (var file = File.Create(archive))
        using (var gz = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gz))
        {
            Add(tar, "json/index.json", "{}");
            Add(tar, "json/variants/msv70.json", """{"sgbd":"msv70","chassis":["E60"],"dtcs":[{"code":"0x2A82","location_text":"2A82 Testtext","location_text_en":null,"source_table":"FORTTEXTE"}],"engines":[{"engine":"N52","engine_variant":"N52","relation":"engine_ecu"}],"hardware_numbers":[{"hwnr":"7551012"}]}""");
            Add(tar, "json/variants/g_only.json", """{"sgbd":"g_only","chassis":["G20"],"dtcs":[{"code":"0x01","location_text":"x"}]}""");
            Add(tar, "json/variants/e90_extra.json", """{"sgbd":"e90_extra","chassis":["E90"],"dtcs":[{"code":"0x01","location_text":"x"}]}""");
            Add(tar, "json/variants/..evil.json", """{"sgbd":"evil","chassis":["E90"],"dtcs":[{"code":"0x01","location_text":"x"}]}""");
        }

        var folder = Path.Combine(Path.GetTempPath(), $"k15-db-{Guid.NewGuid():N}");
        var db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "k15-none"));
        try
        {
            Assert.Equal(2, FaultDataDownload.InstallFromFile(archive, db, folder));
            Assert.True(File.Exists(Path.Combine(folder, "variants", "msv70.json")));
            Assert.True(File.Exists(Path.Combine(folder, "variants", "e90_extra.json")));
            Assert.False(File.Exists(Path.Combine(folder, "variants", "g_only.json")));
            Assert.NotNull(FaultDataDownload.ReadManifest(folder));

            var src = new UnifiedDbSource(folder);
            Assert.Equal("2A82 Testtext", src.Lookup("MSV70", 0x2A82)!.Text);
            Assert.Contains("N52", src.EnginesOf("msv70"));
            Assert.Contains("7551012", src.PartNumbersOf("msv70"));
        }
        finally
        {
            File.Delete(archive);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Every_E9x_core_module_lists_variants()
    {
        var db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "k15-none"));
        foreach (var id in new[] { "E9x.DME", "E9x.DSC", "E9x.CAS", "E9x.KOMBI", "E9x.FRM", "E9x.JBE", "E9x.ACSM", "E46.DME", "E46.IKE" })
            Assert.NotEmpty(db.Modules.First(m => m.Id == id).Variants);
    }
}
