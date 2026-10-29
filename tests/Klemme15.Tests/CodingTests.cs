using System.Text;
using Klemme15.Core;
using Klemme15.Core.Coding;
using Xunit;

namespace Klemme15.Tests;

public class CodingTests
{
    private static readonly EcuDefinition Frm = new() { Id = "E9x.FRM", Name = "Footwell module (FRM)", Address = 0x72, Protocol = BusProtocol.BmwFast };
    private static readonly EcuDefinition Cas = new() { Id = "E9x.CAS", Name = "Key module (CAS)", Address = 0x40, Protocol = BusProtocol.BmwFast };

    /// <summary>A module that keeps its coding in memory and answers $22/$2E like a real one.</summary>
    private sealed class CodedModule : IRawRequests
    {
        public Dictionary<ushort, byte[]> Blocks { get; } = new();
        public List<byte[]> Writes { get; } = [];
        public Func<ushort, byte[], byte[]>? Corrupt { get; set; }

        public Task<byte[]?> RequestAsync(EcuDefinition ecu, byte[] request, CancellationToken ct = default)
        {
            var id = (ushort)(request[1] << 8 | request[2]);
            if (request[0] == 0x22)
                return Task.FromResult<byte[]?>(Blocks.TryGetValue(id, out var d) ? [0x62, request[1], request[2], .. d] : [0x7F, 0x22, 0x31]);
            if (request[0] == 0x2E)
            {
                Writes.Add(request);
                var data = request[3..];
                Blocks[id] = Corrupt?.Invoke(id, data) ?? data;
                return Task.FromResult<byte[]?>([0x6E, request[1], request[2]]);
            }
            return Task.FromResult<byte[]?>([0x7F, request[0], 0x11]);
        }
    }

    // ---- synthetic NCS files, in the record layout the parser reads ----

    private static byte[] Keywords(params (int Id, string Word)[] words)
    {
        var b = new List<byte>();
        foreach (var (id, w) in words)
        {
            var text = Encoding.ASCII.GetBytes(w);
            b.AddRange([(byte)(text.Length + 7), 0x00, 0x01, 0x00, (byte)id, (byte)(id >> 8)]);
            b.AddRange(text);
            b.Add(0);
        }
        return b.ToArray();
    }

    private static void Setting(List<byte> b, uint word, int byteAddr, int fsw, byte mask, params (int Psw, int Data)[] values)
    {
        b.AddRange([0x20, 0x00, 0x12, 0x00, 0x00]);
        b.AddRange(BitConverter.GetBytes(word));
        b.AddRange([(byte)byteAddr, (byte)(byteAddr >> 8), (byte)fsw, (byte)(fsw >> 8), 0x00, 0x01, 0x00, mask, 0x00, 0x00]);
        foreach (var (psw, data) in values)
            b.AddRange([0x0A, 0x00, 0x10, 0x00, (byte)psw, (byte)(psw >> 8), 0x01, 0x00, (byte)data]);
    }

    private const int Aktiv = 1, NichtAktiv = 2, Wert1 = 3, Wert2 = 4, Wert3 = 5;

    /// <summary>
    /// A module with 24 settings spread over blocks 3000 and 3001: one on/off bit for each of 22 lamps, plus a three-way
    /// value and the digital speed bit. The addresses use NCS's 0x00BBBBOO form plus a byte offset.
    /// </summary>
    private static (NcsModule Module, CodedModule Car, Dictionary<string, string> Trace) Build()
    {
        var fsw = new List<(int, string)> { (100, "BC_DIGITAL_V"), (101, "BLINK_ANZAHL") };
        var lamps = new[] { "BLK_VL", "BLK_VR", "BLK_HL", "BLK_HR", "BL_L", "BL_R", "BL_M", "KZL_L", "KZL_R", "SL_V_L", "SL_V_R", "NSW_L", "NSW_R" };
        for (var i = 0; i < lamps.Length; i++) fsw.Add((200 + i, $"KALTUEBERWACHUNG_{lamps[i]}"));
        for (var i = 0; i < 9; i++) fsw.Add((300 + i, $"OPTION_{i}"));
        var functions = NcsFiles.ParseKeywords(Keywords([.. fsw]));
        var values = NcsFiles.ParseKeywords(Keywords((Aktiv, "aktiv"), (NichtAktiv, "nicht_aktiv"), (Wert1, "wert_01"), (Wert2, "wert_02"), (Wert3, "wert_03")));

        var file = new List<byte>();
        file.AddRange([0x20, 0x00, 0x06, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        file.AddRange(Encoding.ASCII.GetBytes("Lampen\0"));
        Setting(file, 0x00300000, 2, 100, 0x10, (Aktiv, 1), (NichtAktiv, 0));
        Setting(file, 0x00300000, 5, 101, 0x0C, (Wert1, 1), (Wert2, 2), (Wert3, 3));
        var car = new CodedModule();
        car.Blocks[0x3000] = new byte[16];
        car.Blocks[0x3001] = new byte[16];
        car.Blocks[0x3000][5] = 0x08;                     // BLINK_ANZAHL = wert_02 (2 << 2)
        var trace = new Dictionary<string, string> { ["BC_DIGITAL_V"] = "nicht_aktiv", ["BLINK_ANZAHL"] = "wert_02" };
        for (var i = 0; i < lamps.Length; i++)
        {
            var bit = (byte)(1 << (i % 8));
            var offset = i / 8;
            Setting(file, 0x00300100, offset, 200 + i, bit, (Aktiv, 1), (NichtAktiv, 0));
            car.Blocks[0x3001][offset] |= bit;            // all lamps monitored (factory)
            trace[$"KALTUEBERWACHUNG_{lamps[i]}"] = "aktiv";
        }
        for (var i = 0; i < 9; i++)
        {
            // Three-value options spread over block 3001 bytes 4-12, so a wrong layout can't explain them by chance.
            Setting(file, 0x00300100, 4 + i, 300 + i, 0x03, (Wert1, 1), (Wert2, 2), (Wert3, 3));
            car.Blocks[0x3001][4 + i] = (byte)(1 + i % 3);
            trace[$"OPTION_{i}"] = $"wert_0{1 + i % 3}";
        }
        var module = new NcsModule("FRM2_E89.C05", NcsFiles.ParseModule(file.ToArray(), functions, values));
        return (module, car, trace);
    }

    private static IReadOnlyList<CodingBlock> BlocksOf(CodedModule car) => car.Blocks.Select(kv => new CodingBlock(kv.Key, kv.Value.ToArray())).ToList();

    private static CodingBackup Backup(CodedModule car) => new()
    {
        ModuleId = Frm.Id, ModuleName = Frm.Name, PartNumber = "6961130", CodingIndex = "05", Taken = DateTimeOffset.Now, Blocks = BlocksOf(car),
    };

    [Fact]
    public void Parses_keywords_and_module_files()
    {
        var (module, _, _) = Build();
        Assert.Equal(24, module.Settings.Count);
        var digital = module.Settings.Single(s => s.Name == "BC_DIGITAL_V");
        Assert.Equal(0x00300000u, digital.WordAddress);
        Assert.Equal(2, digital.ByteAddress);
        Assert.Equal(0x10, digital.Mask);
        Assert.Equal(new[] { "aktiv", "nicht_aktiv" }, digital.Values.Select(v => v.Name).ToArray());
        Assert.Equal("Lampen", digital.Group);
    }

    [Fact]
    public void Verifies_the_layout_against_ncs_experts_reading()
    {
        var (module, car, trace) = Build();
        var result = NcsDecoder.Verify(module, BlocksOf(car), trace);
        Assert.True(result.Verified, result.Summary + string.Join("; ", result.Mismatches));
        Assert.StartsWith("block-from-word, offset=word+byte, shifted", result.Layout!.Id);
        var layouts = NcsDecoder.VerifiedLayouts(module, BlocksOf(car), trace);
        Assert.All(layouts, l => Assert.True(l.ShiftIntoMask));

        // A trace from a different car (one value differs) is not a verification.
        var other = new Dictionary<string, string>(trace) { ["OPTION_3"] = "wert_03" };
        Assert.False(NcsDecoder.Verify(module, BlocksOf(car), other).Verified);
    }

    [Fact]
    public void Brake_light_toggle_never_touches_low_beam_or_other_lamps()
    {
        var brake = CodingToggles.All.Single(t => t.Id == "led-brake-lights");
        bool Hits(string name) => brake.Keywords.Any(k => System.Text.RegularExpressions.Regex.IsMatch(name, $"^(?:{k})$"));
        Assert.True(Hits("KALTUEBERWACHUNG_BL_L"));
        Assert.True(Hits("WARMUEBERWACHUNG_BL_M_1"));
        Assert.False(Hits("KALTUEBERWACHUNG_BLK_VL"));
        Assert.False(Hits("KALTUEBERWACHUNG_BL_ANH"));
        Assert.DoesNotContain(CodingToggles.All.SelectMany(t => t.Keywords), k => k.Contains("_AL", StringComparison.Ordinal));
    }

    [Fact]
    public void Parses_an_ncs_trace_file()
    {
        var trace = NcsFiles.ParseTrace("BC_DIGITAL_V\r\n\taktiv\r\nWL_SL\r\n\twert_01\r\nWL_SL\r\n\twert_02\r\n");
        Assert.Equal("aktiv", trace["BC_DIGITAL_V"]);
        Assert.Equal("wert_01", trace["WL_SL"]);
        Assert.Equal("wert_02", trace["WL_SL#1"]);
    }

    [Fact]
    public async Task Toggle_changes_only_its_own_bits_and_reads_back()
    {
        var (module, car, trace) = Build();
        var evidence = new NcsEvidence(module, trace, BlocksOf(car));
        var layouts = evidence.Layouts;
        var toggle = CodingToggles.All.Single(t => t.Id == "led-turn-signals");
        Assert.Equal(ToggleState.Off, CodingToggles.StateOf(toggle, module, layouts[0], BlocksOf(car)));
        var before = BlocksOf(car);

        var result = await CodingWriter.ApplyAsync(car, Frm, evidence, CodingToggles.ChangesFor(toggle, module, on: true), Backup(car));
        Assert.True(result.Success, result.Message);
        Assert.Equal(ToggleState.On, CodingToggles.StateOf(toggle, module, layouts[0], BlocksOf(car)));
        // Only the four indicator bits changed; brake lights and the rest are untouched.
        var diffs = CodingProtocol.Compare(before, BlocksOf(car));
        Assert.All(diffs, d => Assert.Equal(0x3001, d.Block));
        Assert.Equal(0x0F, diffs.Aggregate(0, (m, d) => m | d.ChangedBits));
        Assert.Equal(ToggleState.Off, CodingToggles.StateOf(CodingToggles.All.Single(t => t.Id == "led-brake-lights"), module, layouts[0], BlocksOf(car)));
    }

    [Fact]
    public async Task Puts_the_original_back_when_the_module_misbehaves()
    {
        var (module, car, trace) = Build();
        var evidence = new NcsEvidence(module, trace, BlocksOf(car));
        var original = BlocksOf(car);
        var calls = 0;
        // The first write lands with an extra bit flipped (a module that "accepts" but stores something else).
        car.Corrupt = (_, data) => calls++ == 0 ? [.. data[..15], (byte)(data[15] ^ 0x80)] : data;
        var result = await CodingWriter.ApplyAsync(car, Frm, evidence,
            CodingToggles.ChangesFor(CodingToggles.All.Single(t => t.Id == "digital-speed"), module, on: true), Backup(car));
        Assert.False(result.Success);
        Assert.True(result.Restored, result.Message);
        Assert.Empty(CodingProtocol.Compare(original, BlocksOf(car)));
    }

    [Fact]
    public async Task Refuses_without_verification_or_on_protected_modules()
    {
        var (module, car, trace) = Build();
        var changes = CodingToggles.ChangesFor(CodingToggles.All.Single(t => t.Id == "digital-speed"), module, on: true);
        // A trace from another car (one value differs) verifies nothing.
        var wrongTrace = new Dictionary<string, string>(trace) { ["OPTION_3"] = "wert_03" };
        var unverified = await CodingWriter.ApplyAsync(car, Frm, new NcsEvidence(module, wrongTrace, BlocksOf(car)), changes, Backup(car));
        Assert.False(unverified.Success);
        Assert.Empty(car.Writes);
        // A setting NCS Expert's reading didn't include is refused, even on a verified module.
        var partial = new Dictionary<string, string>(trace);
        partial.Remove("BC_DIGITAL_V");
        var uncovered = await CodingWriter.ApplyAsync(car, Frm, new NcsEvidence(module, partial, BlocksOf(car)), changes, Backup(car));
        Assert.False(uncovered.Success);
        Assert.Contains("didn't include", uncovered.Message);
        Assert.Empty(car.Writes);
        Assert.False(CodingProtocol.IsCodable(Cas));
        var cas = await CodingWriter.ApplyAsync(car, Cas, new NcsEvidence(module, trace, BlocksOf(car)), changes, Backup(car));
        Assert.False(cas.Success);
        Assert.Empty(car.Writes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CodingProtocol.WriteBlockAsync(car, Cas, new CodingBlock(0x3000, new byte[16])));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CodingProtocol.WriteBlockAsync(car, Frm, new CodingBlock(0x3F00, new byte[16])));
    }

    [Fact]
    public async Task Discovers_blocks_backs_up_and_restores()
    {
        var car = new CodedModule();
        foreach (var id in new ushort[] { 0x3000, 0x3001, 0x3002, 0x3010, 0x3400, 0x3401 }) car.Blocks[id] = Enumerable.Repeat((byte)(id & 0xFF), 16).ToArray();
        var found = await CodingProtocol.DiscoverAsync(car, Frm);
        Assert.Equal(new ushort[] { 0x3000, 0x3001, 0x3002, 0x3010, 0x3400, 0x3401 }, found.Select(b => b.Id).ToArray());

        var backup = new CodingBackup { ModuleId = Frm.Id, ModuleName = Frm.Name, PartNumber = "6961130", CodingIndex = "05", Taken = DateTimeOffset.Now, Blocks = found };
        var copy = CodingBackup.FromJson(backup.ToJson());
        Assert.Empty(CodingProtocol.Compare(backup.Blocks, copy.Blocks));
        Assert.DoesNotContain("3F", string.Join(",", copy.Blocks.Select(b => b.IdHex[..2])));

        car.Blocks[0x3401][3] = 0xAA;
        var written = await CodingProtocol.RestoreAsync(car, Frm, copy, new EcuIdentity { PartNumber = "6961130", CodingIndex = "05" });
        Assert.Equal(1, written);
        Assert.Equal(0x01, car.Blocks[0x3401][3]);
        // Never onto a different module, or one Klemme15 can't identify.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CodingProtocol.RestoreAsync(car, Frm, copy, new EcuIdentity { PartNumber = "9224596", CodingIndex = "05" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CodingProtocol.RestoreAsync(car, Frm, copy, null));
        // A backup whose blocks don't fit the module (here: a block the module doesn't have) writes nothing.
        var writesBefore = car.Writes.Count;
        var odd = copy with { Blocks = [.. copy.Blocks, new CodingBlock(0x3500, new byte[16])] };
        car.Blocks[0x3000][0] ^= 0xFF;
        await Assert.ThrowsAsync<InvalidOperationException>(() => CodingProtocol.RestoreAsync(car, Frm, odd, new EcuIdentity { PartNumber = "6961130", CodingIndex = "05" }));
        Assert.Equal(writesBefore, car.Writes.Count);
    }
}
