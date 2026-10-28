using Klemme15.Telemetry;
using Xunit;

namespace Klemme15.Tests;

public class MqttTests
{
    [Theory]
    [InlineData(0, new byte[] { 0x00 })]
    [InlineData(127, new byte[] { 0x7F })]
    [InlineData(128, new byte[] { 0x80, 0x01 })]
    [InlineData(16383, new byte[] { 0xFF, 0x7F })]
    [InlineData(16384, new byte[] { 0x80, 0x80, 0x01 })]
    public void Encodes_the_remaining_length_like_the_mqtt_spec(int length, byte[] expected)
    {
        var p = new List<byte>();
        MqttPublisher.AddLength(p, length);
        Assert.Equal(expected, p.ToArray());
    }
}
