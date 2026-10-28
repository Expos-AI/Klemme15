using System.Net.Sockets;

namespace Klemme15.Protocols.Transport;

/// <summary>
/// A K+DCAN cable reached over the network instead of USB: a Wi-Fi bridge (ESP32 with esp-link, ser2net on a
/// Raspberry Pi, or any adapter that exposes the cable's serial port on a TCP socket).
/// <list type="bullet">
/// <item><c>rfc2217://host:port</c>: Telnet COM Port Control (RFC 2217). Klemme15 sets baud rate, parity, break,
/// DTR and RTS on the far end, and gets the cable's ignition (DSR) line back, like a local cable.</item>
/// <item><c>tcp://host:port</c>: raw bytes. The bridge must already be set to the right speed (115200 8N1 for
/// D-CAN cars); K-line cars need RFC 2217.</item>
/// </list>
/// D-CAN (E90 family and newer) works well over Wi-Fi. The K-line wake-up of E46-era cars needs a precise 25 ms
/// break, which network delays can spoil; use a USB cable there if it fails.
/// </summary>
public sealed class NetworkTransport : ITransport
{
    // Telnet and RFC 2217 constants.
    private const byte Iac = 255, Will = 251, Wont = 252, Do = 253, Dont = 254, Sb = 250, Se = 240;
    private const byte ComPortOption = 44;
    private const byte SetBaudrate = 1, SetDatasize = 2, SetParity = 3, SetStopsize = 4, SetControl = 5, SetModemstateMask = 11;
    private const byte NotifyModemstate = 7, ServerOffset = 100;
    private const byte ControlBreakOn = 5, ControlBreakOff = 6, ControlDtrOn = 8, ControlDtrOff = 9, ControlRtsOn = 11, ControlRtsOff = 12;
    private const byte ModemDsr = 0x20;

    private readonly string _host;
    private readonly int _port;
    private readonly bool _rfc2217;
    private readonly object _lock = new();
    private readonly Queue<byte> _rx = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private bool? _dsr;

    // Telnet parser state for incoming data.
    private enum Rx { Data, Iac, Command, SubNeg, SubNegIac }
    private Rx _state;
    private byte _command;
    private readonly List<byte> _sub = [];

    public NetworkTransport(string address)
    {
        if (!TryParse(address, out var host, out var port, out var rfc2217))
            throw new ArgumentException($"Not a network cable address: {address}. Use rfc2217://host:port or tcp://host:port.");
        _host = host;
        _port = port;
        _rfc2217 = rfc2217;
    }

    /// <summary>True for addresses this transport handles ("rfc2217://192.168.4.1:2217", "tcp://pi.local:4000").</summary>
    public static bool IsNetworkAddress(string? address) => address is not null && TryParse(address, out _, out _, out _);

    public static bool TryParse(string address, out string host, out int port, out bool rfc2217)
    {
        host = "";
        port = 0;
        rfc2217 = address.StartsWith("rfc2217://", StringComparison.OrdinalIgnoreCase);
        var raw = address.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase);
        if (!rfc2217 && !raw) return false;
        var rest = address[(address.IndexOf("://", StringComparison.Ordinal) + 3)..].TrimEnd('/');
        var colon = rest.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(rest[(colon + 1)..], out port) || port is < 1 or > 65535) return false;
        host = rest[..colon].Trim('[', ']');
        return host.Length > 0;
    }

    public string Name => $"{(_rfc2217 ? "Wi-Fi/network (RFC 2217)" : "Wi-Fi/network")} {_host}:{_port}";
    public bool IsOpen => _client?.Connected == true;

    /// <summary>The cable's ignition line, when the bridge reports modem state (RFC 2217 only).</summary>
    public bool? IgnitionOn { get { lock (_lock) return _dsr; } }

    public void Open()
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            if (!client.ConnectAsync(_host, _port).Wait(TimeSpan.FromSeconds(5)))
                throw new IOException($"No answer from {_host}:{_port}. Check the adapter's Wi-Fi and address.");
        }
        catch (AggregateException e) when (e.InnerException is SocketException se)
        {
            client.Dispose();
            throw new IOException($"Can't reach {_host}:{_port}: {se.Message}", se);
        }
        _client = client;
        _stream = client.GetStream();
        if (_rfc2217)
        {
            // Ask the server to accept COM port control, and report the ignition (DSR) line.
            Send([Iac, Will, ComPortOption]);
            SendSub(SetModemstateMask, [ModemDsr]);
        }
    }

    public void Close()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }

    public void Dispose() => Close();

    public void Configure(int baudRate, SerialParity parity)
    {
        if (!_rfc2217) return;
        SendSub(SetBaudrate, [(byte)(baudRate >> 24), (byte)(baudRate >> 16), (byte)(baudRate >> 8), (byte)baudRate]);
        SendSub(SetDatasize, [8]);
        SendSub(SetParity, [parity switch { SerialParity.Even => (byte)4, SerialParity.Odd => (byte)3, _ => (byte)1 }]);
        SendSub(SetStopsize, [1]);
    }

    public void SetBreak(bool on)
    {
        if (_rfc2217) SendSub(SetControl, [on ? ControlBreakOn : ControlBreakOff]);
    }

    public void SetDtr(bool on)
    {
        if (_rfc2217) SendSub(SetControl, [on ? ControlDtrOn : ControlDtrOff]);
    }

    public void SetRts(bool on)
    {
        if (_rfc2217) SendSub(SetControl, [on ? ControlRtsOn : ControlRtsOff]);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (!_rfc2217)
        {
            Send(data);
            return;
        }
        // Telnet: a data byte 0xFF is sent twice.
        var escaped = new List<byte>(data.Length + 4);
        foreach (var b in data)
        {
            escaped.Add(b);
            if (b == Iac) escaped.Add(Iac);
        }
        Send(escaped.ToArray());
    }

    public int Read(Span<byte> buffer, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            lock (_lock)
            {
                if (_rx.Count > 0)
                {
                    var n = 0;
                    while (n < buffer.Length && _rx.Count > 0) buffer[n++] = _rx.Dequeue();
                    return n;
                }
            }
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero || !Pump(left)) return 0;
        }
    }

    public void DiscardInput()
    {
        while (Pump(TimeSpan.Zero)) { }
        lock (_lock) _rx.Clear();
    }

    /// <summary>Reads from the socket into the receive queue. False when nothing arrived in time.</summary>
    private bool Pump(TimeSpan wait)
    {
        var stream = _stream ?? throw new IOException("The network cable is not connected.");
        var socket = _client!.Client;
        if (socket.Available == 0 && !socket.Poll((int)Math.Max(0, wait.TotalMicroseconds), SelectMode.SelectRead)) return false;
        var chunk = new byte[4096];
        var read = stream.Read(chunk, 0, chunk.Length);
        if (read == 0) throw new IOException("The network cable closed the connection.");
        Feed(chunk.AsSpan(0, read));
        return true;
    }

    /// <summary>Separates data from Telnet commands (RFC 2217 only) and queues the data.</summary>
    internal void Feed(ReadOnlySpan<byte> incoming)
    {
        lock (_lock)
        {
            if (!_rfc2217)
            {
                foreach (var b in incoming) _rx.Enqueue(b);
                return;
            }
            foreach (var b in incoming)
            {
                switch (_state)
                {
                    case Rx.Data:
                        if (b == Iac) _state = Rx.Iac;
                        else _rx.Enqueue(b);
                        break;
                    case Rx.Iac:
                        if (b == Iac) { _rx.Enqueue(Iac); _state = Rx.Data; }
                        else if (b == Sb) { _sub.Clear(); _state = Rx.SubNeg; }
                        else if (b is Will or Wont or Do or Dont) { _command = b; _state = Rx.Command; }
                        else _state = Rx.Data;
                        break;
                    case Rx.Command:
                        // Accept binary transmission and COM port control; refuse everything else.
                        if (_command == Do && b != ComPortOption && b != 0) QueueReply([Iac, Wont, b]);
                        else if (_command == Will && b != ComPortOption && b != 0 && b != 3) QueueReply([Iac, Dont, b]);
                        _state = Rx.Data;
                        break;
                    case Rx.SubNeg:
                        if (b == Iac) _state = Rx.SubNegIac;
                        else _sub.Add(b);
                        break;
                    case Rx.SubNegIac:
                        if (b == Se)
                        {
                            if (_sub.Count >= 3 && _sub[0] == ComPortOption && _sub[1] == NotifyModemstate + ServerOffset)
                                _dsr = (_sub[2] & ModemDsr) != 0;
                            _state = Rx.Data;
                        }
                        else
                        {
                            _sub.Add(b);
                            _state = Rx.SubNeg;
                        }
                        break;
                }
            }
        }
    }

    private readonly List<byte[]> _replies = [];

    private void QueueReply(byte[] reply) => _replies.Add(reply);

    private void SendSub(byte command, ReadOnlySpan<byte> value)
    {
        var bytes = new List<byte> { Iac, Sb, ComPortOption, command };
        foreach (var b in value)
        {
            bytes.Add(b);
            if (b == Iac) bytes.Add(Iac);
        }
        bytes.Add(Iac);
        bytes.Add(Se);
        Send(bytes.ToArray());
    }

    private void Send(ReadOnlySpan<byte> data)
    {
        var stream = _stream ?? throw new IOException("The network cable is not connected.");
        byte[][] pending;
        lock (_lock)
        {
            pending = [.. _replies];
            _replies.Clear();
        }
        foreach (var r in pending) stream.Write(r);
        stream.Write(data);
        stream.Flush();
    }

    /// <summary>For tests: the data bytes received so far.</summary>
    internal byte[] TakeReceived()
    {
        lock (_lock)
        {
            var all = _rx.ToArray();
            _rx.Clear();
            return all;
        }
    }
}
