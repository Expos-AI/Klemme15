using Klemme15.Core;
using Klemme15.Protocols;
using Klemme15.Protocols.Channels;
using Klemme15.Protocols.Framing;
using Xunit;

namespace Klemme15.Tests;

public class FramingTests
{
    [Fact]
    public void KwpFastInitFrameMatchesStandard()
    {
        // ISO 14230 StartCommunication to the OBD functional address.
        Assert.Equal(new byte[] { 0xC1, 0x33, 0xF1, 0x81, 0x66 }, KwpFrame.Build(0x33, 0xF1, [0x81], 0xC0));
    }

    [Fact]
    public void KwpShortFrameRoundTrips()
    {
        var frame = KwpFrame.Build(0x12, 0xF1, [0x18, 0x02, 0xFF, 0xFF]);
        Assert.Equal(0x84, frame[0]);
        var parsed = Assert.Single(KwpFrame.ParseAll(frame));
        Assert.Equal(0x12, parsed.Target);
        Assert.Equal(0xF1, parsed.Source);
        Assert.Equal(new byte[] { 0x18, 0x02, 0xFF, 0xFF }, parsed.Data);
    }

    [Fact]
    public void KwpLongFrameUsesLengthByte()
    {
        var data = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        var frame = KwpFrame.Build(0xF1, 0x12, data);
        Assert.Equal(0x80, frame[0]);
        Assert.Equal(100, frame[3]);
        Assert.Equal(data, Assert.Single(KwpFrame.ParseAll(frame)).Data);
    }

    [Fact]
    public void KwpParserSkipsGarbageAndBadChecksums()
    {
        var good = KwpFrame.Build(0xF1, 0x12, [0x7E]);
        var bad = KwpFrame.Build(0xF1, 0x29, [0x7E]);
        bad[^1] ^= 0xFF;
        byte[] buf = [0x00, 0x13, .. bad, .. good, 0x55];
        var parsed = Assert.Single(KwpFrame.ParseAll(buf));
        Assert.Equal(0x12, parsed.Source);
    }

    [Fact]
    public void Ds2FrameRoundTrips()
    {
        var frame = Ds2Frame.Build(0x12, [0x00]);
        Assert.Equal(new byte[] { 0x12, 0x04, 0x00, 0x16 }, frame);
        var parsed = Assert.Single(Ds2Frame.ParseAll(frame));
        Assert.Equal(0x12, parsed.Address);
        Assert.Equal(new byte[] { 0x00 }, parsed.Data);
    }

    [Fact]
    public void Iso9141SplitsBackToBackResponses()
    {
        byte[] one = [0x48, 0x6B, 0x10, 0x41, 0x0C, 0x0C, 0x80];
        one = [.. one, Checksum.Sum(one)];
        byte[] two = [0x48, 0x6B, 0x18, 0x41, 0x05, 0x7B];
        two = [.. two, Checksum.Sum(two)];
        var parsed = Iso9141Frame.ParseResponses([.. one, .. two]);
        Assert.Equal(2, parsed.Count);
        Assert.Equal(new byte[] { 0x41, 0x0C, 0x0C, 0x80 }, parsed[0]);
        Assert.Equal(new byte[] { 0x41, 0x05, 0x7B }, parsed[1]);
    }

    [Fact]
    public void EchoIsStrippedOnlyWhenPresent()
    {
        byte[] sent = [1, 2, 3];
        Assert.Equal(new byte[] { 9 }, Echo.Strip(sent, [1, 2, 3, 9]).ToArray());
        Assert.Equal(new byte[] { 9, 8 }, Echo.Strip(sent, [9, 8]).ToArray());
    }

    [Theory]
    [InlineData(0x60, FaultState.Present)]
    [InlineData(0xE0, FaultState.Present)]
    [InlineData(0x40, FaultState.Intermittent)]
    [InlineData(0x20, FaultState.Stored)]
    [InlineData(0x98, FaultState.Stored)]   // seen on a real 2006 E90 (FZD)
    public void Kwp2000DtcStatusDecodes(byte status, FaultState expected) =>
        Assert.Equal(expected, Kwp2000.StateFromStatus(status));
}

public class Obd2Tests
{
    [Theory]
    [InlineData(0x01, 0x71, "P0171")]
    [InlineData(0x04, 0x20, "P0420")]
    [InlineData(0x41, 0x23, "C0123")]
    [InlineData(0x81, 0x00, "B0100")]
    [InlineData(0xC1, 0x55, "U0155")]
    [InlineData(0x15, 0x9A, "P159A")]
    public void DecodesDtcBytes(byte hi, byte lo, string code) => Assert.Equal(code, ObdCodes.Decode(hi, lo));

    [Fact]
    public void DecodesRpm()
    {
        var rpm = Obd2.ById[0x0C];
        Assert.Equal(800, Obd2.DecodeResponse([0x41, 0x0C, 0x0C, 0x80], rpm));
        Assert.Null(Obd2.DecodeResponse([0x41, 0x0D, 0x0C, 0x80], rpm));
        Assert.Null(Obd2.DecodeResponse([0x41, 0x0C, 0x0C], rpm));
    }

    [Fact]
    public void DecodesDtcListBothStyles()
    {
        Assert.Equal(["P0171", "P0174"], Obd2.DecodeDtcs([0x43, 0x01, 0x71, 0x01, 0x74, 0x00, 0x00], hasCountByte: false));
        Assert.Equal(["P0171"], Obd2.DecodeDtcs([0x43, 0x01, 0x01, 0x71], hasCountByte: true));
        Assert.Empty(Obd2.DecodeDtcs([0x43, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], hasCountByte: false));
    }

    [Fact]
    public void DecodesSupportedPidBitmap()
    {
        var pids = Obd2.DecodeSupported(0x00, [0x80, 0x00, 0x00, 0x01]).ToList();
        Assert.Equal(new byte[] { 0x01, 0x20 }, pids);
    }

    [Fact]
    public void AssemblesMultiFrameVin()
    {
        var vin = "\0\0\0WBAEV53403KM12345"u8.ToArray();
        var frames = Enumerable.Range(0, 5).Select(i => (byte[])[0x49, 0x02, (byte)(i + 1), .. vin.AsSpan(i * 4, 4).ToArray()]);
        Assert.Equal("WBAEV53403KM12345", Obd2.DecodeVin(frames));
    }

    [Fact]
    public void FallbackDescribesUnknownCodes()
    {
        Assert.Contains("manufacturer-specific", ObdCodes.Fallback("P1234"));
        Assert.Contains("ignition", ObdCodes.Fallback("P0399"));
    }
}
