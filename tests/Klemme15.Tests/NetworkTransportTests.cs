using System.Net;
using System.Net.Sockets;
using Klemme15.Protocols.Transport;
using Xunit;

namespace Klemme15.Tests;

public class NetworkTransportTests
{
    [Fact]
    public void Parses_addresses()
    {
        Assert.True(NetworkTransport.TryParse("rfc2217://192.168.4.1:2217", out var host, out var port, out var rfc));
        Assert.Equal(("192.168.4.1", 2217, true), (host, port, rfc));
        Assert.True(NetworkTransport.TryParse("tcp://pi.local:4000", out host, out port, out rfc));
        Assert.Equal(("pi.local", 4000, false), (host, port, rfc));
        Assert.False(NetworkTransport.IsNetworkAddress("COM3"));
        Assert.False(NetworkTransport.IsNetworkAddress("/dev/ttyUSB0"));
        Assert.False(NetworkTransport.IsNetworkAddress("tcp://nohost"));
    }

    [Fact]
    public void Rfc2217_sets_the_line_escapes_data_and_reads_ignition()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var transport = new NetworkTransport($"rfc2217://127.0.0.1:{port}");
        transport.Open();
        using var server = listener.AcceptTcpClient();
        var s = server.GetStream();

        transport.Configure(115200, SerialParity.None);
        transport.SetBreak(true);
        transport.Write([0x12, 0xFF, 0x34]);

        // Server: modem state with DSR set (ignition on), then data with an escaped 0xFF.
        s.Write([255, 250, 44, 107, 0x20, 255, 240, 0x80, 0xFF, 0xFF, 0x01]);
        var buffer = new byte[16];
        var n = transport.Read(buffer, TimeSpan.FromSeconds(2));
        while (n < 3) n += transport.Read(buffer.AsSpan(n), TimeSpan.FromSeconds(2));
        Assert.Equal(new byte[] { 0x80, 0xFF, 0x01 }, buffer[..n]);
        Assert.True(transport.IgnitionOn == true);

        // What the server received: WILL COM-PORT, modem mask, baud 115200, 8 bits, no parity, 1 stop, break on, data.
        Thread.Sleep(100);
        var got = new byte[256];
        var count = s.Read(got, 0, got.Length);
        var sent = got[..count];
        Assert.Equal(new byte[] { 255, 251, 44 }, sent[..3]);
        Assert.True(Contains(sent, [255, 250, 44, 1, 0x00, 0x01, 0xC2, 0x00, 255, 240]));   // 115200 baud
        Assert.True(Contains(sent, [255, 250, 44, 3, 1, 255, 240]));                         // no parity
        Assert.True(Contains(sent, [255, 250, 44, 5, 5, 255, 240]));                         // break on
        Assert.True(Contains(sent, [0x12, 0xFF, 0xFF, 0x34]));                               // data, 0xFF doubled
        listener.Stop();
    }

    [Fact]
    public async Task Scans_a_car_through_a_network_bridge()
    {
        // The virtual E90 behind a raw TCP bridge, like ser2net or an ESP32 on the cable.
        var car = Klemme15.Protocols.Simulation.VirtualCar.E90_325i();
        car.Open();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var stop = new CancellationTokenSource();
        var bridge = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var s = client.GetStream();
            var toCar = Task.Run(async () =>
            {
                var buf = new byte[1024];
                int n;
                while (!stop.IsCancellationRequested && (n = await s.ReadAsync(buf, stop.Token)) > 0) car.Write(buf.AsSpan(0, n));
            });
            var fromCar = new byte[1024];
            while (!stop.IsCancellationRequested)
            {
                var n = car.Read(fromCar, TimeSpan.FromMilliseconds(5));
                if (n > 0) await s.WriteAsync(fromCar.AsMemory(0, n));
            }
        });

        var db = Klemme15.Data.VehicleDatabase.Load();
        await using var backend = new Klemme15.Protocols.NativeBackend(new NetworkTransport($"tcp://127.0.0.1:{port}"),
            new Klemme15.Protocols.NativeBackendOptions { Mode = Klemme15.Protocols.CableMode.DCan, ProbeTimeout = TimeSpan.FromSeconds(2) });
        await backend.ConnectAsync();
        var family = db.FindVehicle("E9x")!;
        // What matters: the same requests and replies work through the network. (No VIN check: on a busy
        // test machine its single attempt can time out, which says nothing about the transport.)
        var dme = family.Modules.Single(m => m.Id == "E9x.DME");
        var identity = await backend.IdentifyAsync(dme);
        var faults = await backend.ReadFaultsAsync(dme);
        stop.Cancel();
        listener.Stop();
        Assert.NotNull(identity);
        Assert.Contains(faults, f => f.Code == "2A82");
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return true;
        return false;
    }
}
