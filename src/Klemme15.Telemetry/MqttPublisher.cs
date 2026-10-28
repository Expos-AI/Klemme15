using System.Net.Sockets;
using System.Text;

namespace Klemme15.Telemetry;

/// <summary>
/// A tiny MQTT 3.1.1 publisher (CONNECT, PUBLISH at QoS 0, PINGREQ), enough to push live values to a broker like
/// Mosquitto for Home Assistant, Grafana or Node-RED. No external library, so it builds anywhere the app does.
/// </summary>
public sealed class MqttPublisher : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _clientId;
    private readonly string? _user, _pass;
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public MqttPublisher(string host, int port = 1883, string clientId = "klemme15", string? user = null, string? pass = null)
    {
        _host = host; _port = port; _clientId = clientId; _user = user; _pass = pass;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _tcp = new TcpClient { NoDelay = true };
        await _tcp.ConnectAsync(_host, _port, ct);
        _stream = _tcp.GetStream();

        var payload = new List<byte>();
        AddString(payload, "MQTT");
        payload.Add(0x04);                              // protocol level 4 (3.1.1)
        byte flags = 0x02;                              // clean session
        if (_user is not null) flags |= 0x80;
        if (_pass is not null) flags |= 0x40;
        payload.Add(flags);
        payload.Add(0x00); payload.Add(0x3C);           // keep-alive 60 s
        AddString(payload, _clientId);
        if (_user is not null) AddString(payload, _user);
        if (_pass is not null) AddString(payload, _pass);

        var packet = new List<byte> { 0x10 };           // CONNECT
        AddLength(packet, payload.Count);
        packet.AddRange(payload);
        await _stream.WriteAsync(packet.ToArray(), ct);

        var ack = new byte[4];
        var n = await _stream.ReadAsync(ack, ct);
        if (n < 4 || ack[0] != 0x20 || ack[3] != 0x00)
            throw new IOException($"MQTT broker refused the connection (code {(n >= 4 ? ack[3] : -1)}).");
    }

    public async Task PublishAsync(string topic, string value, bool retain = false, CancellationToken ct = default)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected");
        var payload = new List<byte>();
        AddString(payload, topic);
        payload.AddRange(Encoding.UTF8.GetBytes(value));   // QoS 0: no packet id
        var packet = new List<byte> { (byte)(0x30 | (retain ? 0x01 : 0x00)) };
        AddLength(packet, payload.Count);
        packet.AddRange(payload);
        await _lock.WaitAsync(ct);
        try { await _stream.WriteAsync(packet.ToArray(), ct); }
        finally { _lock.Release(); }
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        if (_stream is null) return;
        await _lock.WaitAsync(ct);
        try { await _stream.WriteAsync(new byte[] { 0xC0, 0x00 }, ct); }
        finally { _lock.Release(); }
    }

    public static void AddLength(List<byte> p, int length)
    {
        do
        {
            var b = (byte)(length & 0x7F);
            length >>= 7;
            if (length > 0) b |= 0x80;
            p.Add(b);
        } while (length > 0);
    }

    private static void AddString(List<byte> p, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        p.Add((byte)(bytes.Length >> 8));
        p.Add((byte)(bytes.Length & 0xFF));
        p.AddRange(bytes);
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_stream is not null) await _stream.WriteAsync(new byte[] { 0xE0, 0x00 }); } catch { /* best effort DISCONNECT */ }
        _stream?.Dispose();
        _tcp?.Dispose();
        _lock.Dispose();
    }
}
