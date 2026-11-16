// Klemme15: open-source BMW diagnostics
// Copyright (C) 2026 Kindric Castro
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
// Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version. This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
// General Public License for more details. You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later

using Klemme15.Core;
using Xunit;

namespace Klemme15.Tests;

public class ReadingMapsTests
{
    private static MapField Field(int at, int bits, int bit = 0, bool signed = false, bool little = false, double factor = 1, double offset = 0) =>
        new() { Name = "value", Byte = at, Bit = bit, Bits = bits, Signed = signed, LittleEndian = little, Factor = factor, Offset = offset, Observed = "test" };

    private static double Decode(byte[] reply, MapField field)
    {
        Assert.True(ReadingMaps.TryDecode(reply, field, out var value));
        return value;
    }

    [Fact]
    public void WholeBytesAreReadInEitherByteOrder()
    {
        byte[] reply = [0x61, 0x10, 0x12, 0x34, 0xFF];
        Assert.Equal(0x1234, Decode(reply, Field(2, 16)));
        Assert.Equal(0x3412, Decode(reply, Field(2, 16, little: true)));
        Assert.Equal(0x12, Decode(reply, Field(2, 8)));
        Assert.Equal(0x1234FF, Decode(reply, Field(2, 24)));
    }

    [Fact]
    public void BitsAreCutOutFromTheLowEnd()
    {
        byte[] reply = [0x61, 0b1011_0100];
        Assert.Equal(0, Decode(reply, Field(1, 1)));
        Assert.Equal(1, Decode(reply, Field(1, 1, bit: 2)));
        Assert.Equal(0b1011, Decode(reply, Field(1, 4, bit: 4)));
        Assert.Equal(0b1101, Decode(reply, Field(1, 4, bit: 2)));
        // A value that crosses a byte boundary: 10 bits, 3 bits up from the low end of two bytes.
        Assert.Equal((0x1234 >> 3) & 0x3FF, Decode([0x12, 0x34], Field(0, 10, bit: 3)));
    }

    [Fact]
    public void SignedValuesAndScalingWork()
    {
        Assert.Equal(-1, Decode([0xFF, 0xFF], Field(0, 16, signed: true)));
        Assert.Equal(-32768, Decode([0x80, 0x00], Field(0, 16, signed: true)));
        Assert.Equal(-2, Decode([0b0000_1110], Field(0, 4, signed: true)));
        Assert.Equal(60, Decode([0x64], Field(0, 8, factor: 1, offset: -40)));
        Assert.Equal(25.5, Decode([0xFF], Field(0, 8, factor: 0.1)));
    }

    [Fact]
    public void AReplyThatIsTooShortGivesNoValue()
    {
        Assert.False(ReadingMaps.TryDecode([0x61, 0x10], Field(1, 16), out _));
        Assert.False(ReadingMaps.TryDecode([], Field(0, 8), out _));
        Assert.False(ReadingMaps.TryDecode([1, 2, 3], Field(0, 33), out _));
    }

    private const string Good = """
        { "maps": [ { "module": "TEST1", "request": "21 05", "fields": [
            { "name": "First", "unit": "rpm", "byte": 2, "bits": 16, "factor": 0.5, "observed": "held the engine at a known speed" },
            { "name": "Flag", "byte": 4, "bit": 7, "bits": 1, "observed": "switched it on and off" } ] } ] }
        """;

    [Fact]
    public void AMapFileIsLoadedAndApplied()
    {
        var map = Assert.Single(ReadingMaps.Load(Good));
        Assert.Equal(new byte[] { 0x21, 0x05 }, map.RequestBytes);
        Assert.Equal(new byte[] { 0x61, 0x05 }, map.ReplyPrefixBytes);
        var values = ReadingMaps.Decode(map, [0x61, 0x05, 0x07, 0xD0, 0x80]);
        Assert.Equal(2, values.Count);
        Assert.Equal(1000, values[0].Value);
        Assert.Equal("First: 1000 rpm", ReadingMaps.Format(values[0]));
        Assert.Equal(1, values[1].Value);
        // Another module's reply, or a refusal, isn't decoded.
        Assert.Empty(ReadingMaps.Decode(map, [0x61, 0x06, 0x07, 0xD0, 0x80]));
        Assert.Empty(ReadingMaps.Decode(map, [0x7F, 0x21, 0x12]));
        Assert.Single(ReadingMaps.Load(ReadingMaps.ToJson([map])));
    }

    [Theory]
    [InlineData("2E 10 00 01")]   // write data
    [InlineData("3B 90 01")]      // write by local identifier
    [InlineData("31 01")]         // start a routine
    [InlineData("30 10 07")]      // drive an output
    [InlineData("14 FF FF")]      // clear faults
    [InlineData("11 01")]         // reset the module
    [InlineData("27 01")]         // security access
    [InlineData("34 00")]         // download
    public void AMapCanOnlyAskToRead(string request)
    {
        var e = Assert.Throws<DiagnosticException>(() => ReadingMaps.Load(Good.Replace("21 05", request)));
        Assert.Contains("isn't a read", e.Message);
    }

    [Fact]
    public void BrokenMapsAreRefusedWithAReason()
    {
        Assert.Throws<DiagnosticException>(() => ReadingMaps.Load("{ not json"));
        Assert.Throws<DiagnosticException>(() => ReadingMaps.Load("""{ "maps": [] }"""));
        Assert.Contains("observed", Assert.Throws<DiagnosticException>(() => ReadingMaps.Load(Good.Replace("held the engine at a known speed", " "))).Message);
        Assert.Contains("bits", Assert.Throws<DiagnosticException>(() => ReadingMaps.Load(Good.Replace("\"bits\": 16", "\"bits\": 40"))).Message);
        Assert.Contains("hex", Assert.Throws<DiagnosticException>(() => ReadingMaps.Load(Good.Replace("21 05", "21 0G"))).Message);
    }
}
