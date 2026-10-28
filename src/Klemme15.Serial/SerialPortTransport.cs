using System.Diagnostics;
using System.IO.Ports;
using Klemme15.Protocols.Transport;

namespace Klemme15.Serial;

/// <summary>A K+DCAN cable on a serial port (COM3, /dev/ttyUSB0, /dev/cu.usbserial-...).</summary>
public sealed class SerialPortTransport(string portName) : ITransport
{
    private readonly SerialPort _port = new(portName)
    {
        BaudRate = 115200,
        DataBits = 8,
        StopBits = StopBits.One,
        Parity = Parity.None,
        Handshake = Handshake.None,
        ReadTimeout = 50,
        WriteTimeout = 1000,
        ReadBufferSize = 8192,
    };

    /// <summary>
    /// K+DCAN cables report ignition on DSR, but some clones leave it unconnected (always false).
    /// Only trust it when the user has confirmed their cable supports it.
    /// </summary>
    public bool TrustDsrForIgnition { get; init; }

    public string Name => portName;
    public bool IsOpen => _port.IsOpen;

    public bool? IgnitionOn
    {
        get
        {
            if (!_port.IsOpen) return null;
            try
            {
                var dsr = _port.DsrHolding;
                return dsr || TrustDsrForIgnition ? dsr : null;
            }
            catch (Exception) { return null; }
        }
    }

    public void Open()
    {
        _port.Open();
        _port.DtrEnable = false;
        _port.RtsEnable = false;
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
    }

    public void Close()
    {
        if (_port.IsOpen) _port.Close();
    }

    public void Configure(int baudRate, SerialParity parity)
    {
        _port.BaudRate = baudRate;
        _port.Parity = parity switch
        {
            SerialParity.Even => Parity.Even,
            SerialParity.Odd => Parity.Odd,
            _ => Parity.None,
        };
    }

    public void SetBreak(bool on) => _port.BreakState = on;
    public void SetDtr(bool on) => _port.DtrEnable = on;
    public void SetRts(bool on) => _port.RtsEnable = on;
    public void DiscardInput() => _port.DiscardInBuffer();

    public void Write(ReadOnlySpan<byte> data)
    {
        var buffer = data.ToArray();
        _port.Write(buffer, 0, buffer.Length);
    }

    public int Read(Span<byte> buffer, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var available = _port.BytesToRead;
            if (available > 0)
            {
                var tmp = new byte[Math.Min(available, buffer.Length)];
                var n = _port.Read(tmp, 0, tmp.Length);
                tmp.AsSpan(0, n).CopyTo(buffer);
                return n;
            }
            if (sw.Elapsed >= timeout) return 0;
            Thread.Sleep(1);
        }
    }

    public void Dispose()
    {
        Close();
        _port.Dispose();
    }
}

public sealed record PortInfo(string Name, string Description, bool LikelyKDCan);

public static class PortDiscovery
{
    /// <summary>List serial ports, flagging FTDI adapters (what K+DCAN cables use).</summary>
    public static IReadOnlyList<PortInfo> List()
    {
        var ports = new List<PortInfo>();
        foreach (var name in SerialPort.GetPortNames().Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var (desc, ftdi) = Describe(name);
            ports.Add(new PortInfo(name, desc, ftdi));
        }
        return ports.OrderByDescending(p => p.LikelyKDCan).ToList();
    }

    private static (string, bool) Describe(string name)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var dev = Path.GetFileName(name);
                var vendorFile = $"/sys/class/tty/{dev}/device/../../idVendor";
                if (File.Exists(vendorFile))
                {
                    var vendor = File.ReadAllText(vendorFile).Trim();
                    var product = File.Exists($"/sys/class/tty/{dev}/device/../../product")
                        ? File.ReadAllText($"/sys/class/tty/{dev}/device/../../product").Trim() : "USB serial";
                    return (product, vendor == "0403");
                }
            }
            if (OperatingSystem.IsMacOS())
                return ("USB serial", name.Contains("usbserial", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception) { /* description is cosmetic */ }
        return ("Serial port", false);
    }
}
