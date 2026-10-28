using System.Globalization;
using Klemme15.Core;

namespace Klemme15.Protocols;

/// <summary>Standard OBD-II mode 01 parameters (SAE J1979) and their formulas.</summary>
public static class Obd2
{
    public sealed record Pid(byte Id, string Key, string Name, string Unit, int Bytes, Func<byte[], double> Decode, double Min, double Max);

    public static readonly IReadOnlyList<Pid> Pids =
    [
        new(0x04, "load", "Engine load", "%", 1, a => a[0] * 100.0 / 255, 0, 100),
        new(0x05, "coolant", "Coolant temperature", "°C", 1, a => a[0] - 40, -40, 130),
        new(0x06, "stft1", "Short-term fuel trim bank 1", "%", 1, a => (a[0] - 128) * 100.0 / 128, -25, 25),
        new(0x07, "ltft1", "Long-term fuel trim bank 1", "%", 1, a => (a[0] - 128) * 100.0 / 128, -25, 25),
        new(0x08, "stft2", "Short-term fuel trim bank 2", "%", 1, a => (a[0] - 128) * 100.0 / 128, -25, 25),
        new(0x09, "ltft2", "Long-term fuel trim bank 2", "%", 1, a => (a[0] - 128) * 100.0 / 128, -25, 25),
        new(0x0B, "map", "Intake manifold pressure", "kPa", 1, a => a[0], 0, 255),
        new(0x0C, "rpm", "Engine speed", "rpm", 2, a => (a[0] * 256 + a[1]) / 4.0, 0, 7500),
        new(0x0D, "speed", "Vehicle speed", "km/h", 1, a => a[0], 0, 260),
        new(0x0E, "timing", "Ignition timing advance", "°", 1, a => a[0] / 2.0 - 64, -20, 60),
        new(0x0F, "iat", "Intake air temperature", "°C", 1, a => a[0] - 40, -40, 100),
        new(0x10, "maf", "Mass air flow", "g/s", 2, a => (a[0] * 256 + a[1]) / 100.0, 0, 250),
        new(0x11, "throttle", "Throttle position", "%", 1, a => a[0] * 100.0 / 255, 0, 100),
        new(0x14, "o2b1s1", "O2 sensor bank 1 sensor 1", "V", 2, a => a[0] / 200.0, 0, 1.275),
        new(0x15, "o2b1s2", "O2 sensor bank 1 sensor 2", "V", 2, a => a[0] / 200.0, 0, 1.275),
        new(0x1F, "runtime", "Run time since start", "s", 2, a => a[0] * 256 + a[1], 0, 65535),
        new(0x2F, "fuel", "Fuel level", "%", 1, a => a[0] * 100.0 / 255, 0, 100),
        new(0x33, "baro", "Barometric pressure", "kPa", 1, a => a[0], 0, 255),
        new(0x42, "voltage", "Control module voltage", "V", 2, a => (a[0] * 256 + a[1]) / 1000.0, 0, 18),
        new(0x46, "ambient", "Ambient air temperature", "°C", 1, a => a[0] - 40, -40, 60),
        new(0x5C, "oiltemp", "Engine oil temperature", "°C", 1, a => a[0] - 40, -40, 160),
    ];

    public static readonly IReadOnlyDictionary<byte, Pid> ById = Pids.ToDictionary(p => p.Id);

    public static LiveParameter ToParameter(Pid p) => new()
    {
        Id = p.Key,
        Name = p.Name,
        Unit = p.Unit,
        Source = $"obd:{p.Id:X2}",
        Min = p.Min,
        Max = p.Max,
    };

    public static IReadOnlyList<LiveParameter> AllParameters { get; } = Pids.Select(ToParameter).ToList();

    public static bool TryGetPid(LiveParameter parameter, out Pid pid)
    {
        pid = null!;
        if (!parameter.Source.StartsWith("obd:", StringComparison.Ordinal)) return false;
        return byte.TryParse(parameter.Source.AsSpan(4), NumberStyles.HexNumber, null, out var id) && ById.TryGetValue(id, out pid!);
    }

    /// <summary>Decode a mode 01 response payload (41 pid data...). Returns null if it does not match.</summary>
    public static double? DecodeResponse(byte[] payload, Pid pid)
    {
        if (payload.Length < 2 + pid.Bytes || payload[0] != 0x41 || payload[1] != pid.Id) return null;
        return Math.Round(pid.Decode(payload[2..]), 2);
    }

    /// <summary>Decode the 32-bit "PIDs supported" bitmaps (PID 00, 20, 40).</summary>
    public static IEnumerable<byte> DecodeSupported(byte basePid, ReadOnlySpan<byte> mask)
    {
        var list = new List<byte>();
        for (var i = 0; i < 32 && i / 8 < mask.Length; i++)
            if ((mask[i / 8] & (0x80 >> (i % 8))) != 0) list.Add((byte)(basePid + i + 1));
        return list;
    }

    /// <summary>Parse the DTC list from a mode 03 response. CAN-style responses carry a count byte first.</summary>
    public static List<string> DecodeDtcs(byte[] payload, bool hasCountByte)
    {
        var codes = new List<string>();
        if (payload.Length == 0 || payload[0] != 0x43) return codes;
        var body = payload.AsSpan(1);
        if (hasCountByte && body.Length % 2 == 1) body = body[1..];
        for (var i = 0; i + 1 < body.Length; i += 2)
            if (body[i] != 0 || body[i + 1] != 0)
                codes.Add(ObdCodes.Decode(body[i], body[i + 1]));
        return codes;
    }

    /// <summary>Assemble a VIN from mode 09 PID 02 responses (single or multi-frame).</summary>
    public static string? DecodeVin(IEnumerable<byte[]> payloads)
    {
        var chars = new List<char>();
        foreach (var p in payloads.Where(p => p.Length >= 3 && p[0] == 0x49 && p[1] == 0x02).OrderBy(p => p[2]))
            foreach (var c in p.Skip(3))
                if (c is >= (byte)'0' and <= (byte)'Z') chars.Add((char)c);
        var vin = new string(chars.ToArray());
        return vin.Length >= 17 ? vin[^17..] : vin.Length > 0 ? vin : null;
    }
}
