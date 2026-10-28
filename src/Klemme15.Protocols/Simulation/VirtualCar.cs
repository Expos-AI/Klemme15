using System.Diagnostics;
using System.Text;
using Klemme15.Core;
using Klemme15.Protocols.Framing;
using Klemme15.Protocols.Transport;

namespace Klemme15.Protocols.Simulation;

/// <summary>A simulated control unit inside the virtual car.</summary>
public sealed class VirtualEcu
{
    public required byte Address { get; init; }
    public required string Ident { get; init; }
    public bool IsEngine { get; init; }

    /// <summary>BMW fault memory: (code, ISO 14230 status byte).</summary>
    public List<(ushort Code, byte Status)> Faults { get; init; } = [];

    /// <summary>OBD-II codes as raw 16-bit values, e.g. 0x0171 for P0171.</summary>
    public List<ushort> ObdCodes { get; init; } = [];
}

/// <summary>
/// An <see cref="ITransport"/> that behaves like a K+DCAN cable plugged into a car. It answers
/// BMW-FAST on D-CAN (115200), DS2 (9600 8E1) and OBD-II fast init on the K-line (10400), including
/// the K-line echo. Used for tests, development and the app's demo mode.
/// </summary>
public sealed class VirtualCar : ITransport
{
    public const string Vin = "WBAVB13506PT00001";

    private readonly object _lock = new();
    private readonly Queue<byte> _rx = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<byte, VirtualEcu> _ecus;
    private int _baud = 115200;
    private SerialParity _parity;
    private bool _breakOn;
    private long _breakStartedMs = -1;
    private bool _kwpSession;

    public VirtualCar(string name, IEnumerable<VirtualEcu> ecus)
    {
        Name = name;
        _ecus = ecus.ToDictionary(e => e.Address);
    }

    public string Name { get; }
    public bool IsOpen { get; private set; }
    public bool? IgnitionOn { get; set; } = true;
    public IReadOnlyCollection<VirtualEcu> Ecus => _ecus.Values;

    /// <summary>Whether modules answer the functional (broadcast) address 0xDF.</summary>
    public bool AnswersFunctional { get; set; } = true;

    /// <summary>Idle speed of the simulated engine.</summary>
    public int IdleRpm { get; set; } = 750;

    /// <summary>Simulate boost pressure under load (twin-turbo engines).</summary>
    public bool Turbo { get; set; }

    /// <summary>Delay before an ECU answers, to make timing code honest.</summary>
    public TimeSpan ResponseDelay { get; set; } = TimeSpan.FromMilliseconds(5);

    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;
    public void Dispose() => Close();
    public void SetDtr(bool on) { }
    public void SetRts(bool on) { }

    public void Configure(int baudRate, SerialParity parity)
    {
        _baud = baudRate;
        _parity = parity;
    }

    public void SetBreak(bool on)
    {
        if (on && !_breakOn) _breakStartedMs = _clock.ElapsedMilliseconds;
        if (!on && _breakOn)
        {
            // A 20-30 ms low pulse on the K-line is an ISO 14230 fast-init wake-up.
            var low = _clock.ElapsedMilliseconds - _breakStartedMs;
            if (low is >= 15 and <= 50) _kwpSession = false;
        }
        _breakOn = on;
    }

    public void DiscardInput()
    {
        lock (_lock) _rx.Clear();
    }

    public int Read(Span<byte> buffer, TimeSpan timeout)
    {
        var deadline = _clock.Elapsed + timeout;
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
            if (_clock.Elapsed >= deadline) return 0;
            Thread.Sleep(1);
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (!IsOpen) throw new InvalidOperationException("Port not open");
        var kLine = _baud != 115200;
        if (kLine) Enqueue(data);   // half-duplex K-line: we hear our own transmission

        byte[]? reply = _baud switch
        {
            115200 => HandleBmwFast(data),
            9600 when _parity == SerialParity.Even => HandleDs2(data),
            10400 => HandleKLineObd(data),
            _ => null,
        };
        if (reply is null) return;
        if (ResponseDelay > TimeSpan.Zero) Thread.Sleep(ResponseDelay);
        Enqueue(reply);
    }

    private void Enqueue(ReadOnlySpan<byte> data)
    {
        lock (_lock)
            foreach (var b in data) _rx.Enqueue(b);
    }

    // ----------------------------------------------------------- D-CAN (BMW-FAST)

    private byte[]? HandleBmwFast(ReadOnlySpan<byte> data)
    {
        var frames = KwpFrame.ParseAll(data);
        if (frames.Count == 0) return null;
        if (frames[0].Target == 0xDF && AnswersFunctional)
        {
            // Functional request: every module answers in turn.
            var all = new List<byte>();
            foreach (var e in _ecus.Values.OrderBy(e => e.Address))
                if (KwpService(e, frames[0].Data, canStyleObd: true) is { } r)
                    all.AddRange(KwpFrame.Build(frames[0].Source, e.Address, r));
            return all.Count > 0 ? all.ToArray() : null;
        }
        if (!_ecus.TryGetValue(frames[0].Target, out var ecu)) return null;
        var req = frames[0].Data;
        var resp = KwpService(ecu, req, canStyleObd: true);
        return resp is null ? null : KwpFrame.Build(frames[0].Source, ecu.Address, resp);
    }

    private byte[]? KwpService(VirtualEcu ecu, byte[] req, bool canStyleObd)
    {
        switch (req[0])
        {
            case 0x3E: return [0x7E];
            case 0x1A when req.Length > 1 && req[1] == 0x80:
                return [0x5A, 0x80, .. Encoding.ASCII.GetBytes(ecu.Ident)];
            case 0x1A when req.Length > 1 && req[1] == 0x90 && ecu.IsEngine:
                return [0x5A, 0x90, .. Encoding.ASCII.GetBytes(Vin)];
            case 0x1A:
                return [0x7F, 0x1A, 0x12];
            case 0x18:
            {
                var body = new List<byte> { 0x58, (byte)ecu.Faults.Count };
                foreach (var (code, status) in ecu.Faults) body.AddRange([(byte)(code >> 8), (byte)code, status]);
                return [.. body];
            }
            case 0x14:
                ecu.Faults.Clear();
                return [0x54, 0xFF, 0xFF];
            case 0x01 when ecu.IsEngine && req.Length > 1:
                return ObdMode01(req[1]);
            case 0x03 when ecu.IsEngine:
            {
                var body = new List<byte> { 0x43 };
                if (canStyleObd) body.Add((byte)ecu.ObdCodes.Count);
                foreach (var c in ecu.ObdCodes) body.AddRange([(byte)(c >> 8), (byte)c]);
                if (!canStyleObd) while ((body.Count - 1) % 6 != 0 || body.Count == 1) body.Add(0);
                return [.. body];
            }
            case 0x04 when ecu.IsEngine:
                ecu.ObdCodes.Clear();
                return [0x44];
            case 0x09 when ecu.IsEngine && req.Length > 1 && req[1] == 0x02:
                return [0x49, 0x02, 0x01, .. Encoding.ASCII.GetBytes(Vin)];
            default:
                return [0x7F, req[0], 0x11];
        }
    }

    // ----------------------------------------------------------- live engine model

    private byte[]? ObdMode01(byte pid)
    {
        var t = _clock.Elapsed.TotalSeconds;
        var rpm = IdleRpm + 1600 * Math.Max(0, Math.Sin(t / 4)) + 15 * Math.Sin(t * 7);
        var load = 18 + 40 * Math.Max(0, Math.Sin(t / 4));
        var coolant = Math.Min(92, 25 + t * 1.5);
        byte[]? data = pid switch
        {
            0x00 => [0xBE, 0x3E, 0xB8, 0x11],
            0x20 => [0x80, 0x00, 0x20, 0x01],
            0x40 => [0x44, 0x00, 0x00, 0x00],
            0x04 => [(byte)(load * 255 / 100)],
            0x05 => [(byte)(coolant + 40)],
            0x06 or 0x08 => [(byte)(128 + 4 * Math.Sin(t))],
            0x07 or 0x09 => [(byte)(128 + 9)],                   // +7 %: hint of a vacuum leak
            0x0B => [(byte)(Turbo ? 35 + load * 2.6 : 35 + load * 0.6)],
            0x0C => [(byte)((int)(rpm * 4) >> 8), (byte)(int)(rpm * 4)],
            0x0D => [(byte)Math.Max(0, (rpm - 800) / 40)],
            0x0E => [(byte)((12 + 10 * Math.Sin(t / 4) + 64) * 2)],
            0x0F => [(byte)(35 + 40)],
            0x10 => [(byte)((int)(rpm * 0.8) >> 8), (byte)(int)(rpm * 0.8)],
            0x11 => [(byte)(load * 2.2)],
            0x14 or 0x15 => [(byte)(90 + 80 * Math.Abs(Math.Sin(t * 3))), 0xFF],
            0x1F => [(byte)((int)t >> 8), (byte)(int)t],
            0x2F => [(byte)(0.62 * 255)],
            0x33 => [101],
            0x42 => [(byte)(14100 >> 8), 14100 & 0xFF],
            0x46 => [(byte)(18 + 40)],
            _ => null,
        };
        return data is null ? null : [0x41, pid, .. data];
    }

    // ----------------------------------------------------------- K-line DS2

    private byte[]? HandleDs2(ReadOnlySpan<byte> data)
    {
        var frames = Ds2Frame.ParseAll(data);
        if (frames.Count == 0 || !_ecus.TryGetValue(frames[0].Address, out var ecu)) return null;
        var cmd = frames[0].Data;
        byte[] resp = cmd.Length > 0 ? cmd[0] switch
        {
            0x00 => [Ds2Frame.StatusOk, .. Encoding.ASCII.GetBytes(ecu.Ident)],
            0x05 => ClearDs2(ecu),
            _ => [Ds2Frame.StatusRejected],
        } : [Ds2Frame.StatusRejected];
        return Ds2Frame.Build(ecu.Address, resp);
    }

    private static byte[] ClearDs2(VirtualEcu ecu)
    {
        ecu.Faults.Clear();
        return [Ds2Frame.StatusOk];
    }

    // ----------------------------------------------------------- K-line OBD-II (ISO 14230)

    private byte[]? HandleKLineObd(ReadOnlySpan<byte> data)
    {
        var frames = KwpFrame.ParseAll(data);
        if (frames.Count == 0 || frames[0].Target != 0x33) return null;
        var req = frames[0].Data;
        var engine = _ecus.Values.FirstOrDefault(e => e.IsEngine);
        if (engine is null) return null;
        if (req[0] == 0x81)
        {
            _kwpSession = true;
            return KwpFrame.Build(0xF1, 0x10, [0xC1, 0x8F, 0xEF]);
        }
        if (!_kwpSession) return null;
        if (req[0] == 0x09 && req.Length > 1 && req[1] == 0x02)
        {
            // Five frames of 4 characters, the first padded with zeros (ISO 14230 style).
            var vin = Encoding.ASCII.GetBytes("\0\0\0" + Vin);
            var all = new List<byte>();
            for (var i = 0; i < 5; i++)
                all.AddRange(KwpFrame.Build(0xF1, 0x10, [0x49, 0x02, (byte)(i + 1), .. vin.AsSpan(i * 4, 4).ToArray()]));
            return [.. all];
        }
        var resp = KwpService(engine, req, canStyleObd: false);
        return resp is null ? null : KwpFrame.Build(0xF1, 0x10, resp);
    }

    // ----------------------------------------------------------- ready-made cars

    /// <summary>
    /// Builds a demo car for a model from the database: its fitted modules answer, and its demo faults
    /// sit in the right fault memories (BMW-format codes in the module, OBD-II codes in the engine).
    /// </summary>
    public static VirtualCar ForModel(VehicleModel model, IEnumerable<EcuDefinition> familyModules)
    {
        var ecus = new List<VirtualEcu>();
        foreach (var def in familyModules.Where(m => model.ModuleIds.Contains(m.Id)))
        {
            var faults = model.DemoFaults.Where(f => f.ModuleId == def.Id).ToList();
            ecus.Add(new VirtualEcu
            {
                Address = def.Address,
                IsEngine = def.IsEngine,
                Ident = $"{FakePartNumber(model.Id + def.Id)} {def.Id.Split('.').Last()} HW {Stable(def.Id, 1, 9):00} SW {Stable(model.Id + def.Id, 10, 60):X2}",
                Faults = faults.Where(f => !f.IsObd)
                               .Select(f => (Convert.ToUInt16(f.Code, 16), (byte)(f.Present ? 0x60 : 0x20)))
                               .ToList(),
                ObdCodes = faults.Where(f => f.IsObd).Select(f => ObdCodeToRaw(f.Code)).ToList(),
            });
        }
        return new VirtualCar($"Demo {model.FamilyId} {model.Name}", ecus) { IdleRpm = model.IdleRpm, Turbo = model.Turbo };
    }

    internal static ushort ObdCodeToRaw(string code)
    {
        var system = "PCBU".IndexOf(char.ToUpperInvariant(code[0]));
        var hi = (system << 6) | ((code[1] - '0') << 4) | Convert.ToInt32(code[2].ToString(), 16);
        return (ushort)((hi << 8) | Convert.ToInt32(code[3..], 16));
    }

    private static int Stable(string seed, int min, int max)
    {
        var h = 17;
        foreach (var c in seed) h = unchecked(h * 31 + c);
        return min + (int)((uint)h % (uint)(max - min));
    }

    private static string FakePartNumber(string seed) => Stable(seed, 6_900_000, 7_600_000).ToString();

    /// <summary>Demo E90 325i (N52) with a couple of typical faults.</summary>
    public static VirtualCar E90_325i() => new("Virtual E90 325i", [
        new VirtualEcu
        {
            Address = 0x12, IsEngine = true, Ident = "7562350 MSV70 HW 08 SW 3A CI 0B",
            Faults = [(0x2A82, 0x60), (0x2C2E, 0x20)],   // illustrative codes: one present, one stored
            ObdCodes = [0x0171, 0x0597],
        },
        new VirtualEcu { Address = 0x18, Ident = "7581735 GS19 HW 05 SW 12" },
        new VirtualEcu { Address = 0x29, Ident = "6779871 DSC8 HW 04 SW 1F", Faults = [(0x5E20, 0x20)] },
        new VirtualEcu { Address = 0x01, Ident = "9184432 ACSM HW 02 SW 09" },
        new VirtualEcu { Address = 0x40, Ident = "9147215 CAS3 HW 03 SW 21" },
        new VirtualEcu { Address = 0x60, Ident = "9187040 KOMBI HW 06 SW 0E" },
        new VirtualEcu { Address = 0x72, Ident = "6981534 FRM2 HW 02 SW 0A", Faults = [(0xA3B4, 0x60)] },
        new VirtualEcu { Address = 0x00, Ident = "9147177 JBBF HW 07 SW 33" },
        new VirtualEcu { Address = 0x78, Ident = "9182287 IHKA HW 01 SW 05" },
    ]);

    /// <summary>2003 E46 325i (M54) on the K-line.</summary>
    public static VirtualCar E46_325i() => new("Virtual E46 325i", [
        new VirtualEcu { Address = 0x12, IsEngine = true, Ident = "7519308 MS43 0C 07 00 03", ObdCodes = [0x0171, 0x0174, 0x0411] },
        new VirtualEcu { Address = 0x32, Ident = "7519281 GS20 01 04" },
        new VirtualEcu { Address = 0x56, Ident = "6758970 DSC3 03 11" },
        new VirtualEcu { Address = 0x80, Ident = "6942171 IKE 05 22" },
        new VirtualEcu { Address = 0x00, Ident = "6943841 GM5 0A 19" },
        new VirtualEcu { Address = 0xD0, Ident = "6914563 LCM 04 15" },
    ]);
}
