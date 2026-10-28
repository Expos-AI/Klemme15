using System.Text;

namespace Klemme15.Data;

/// <summary>
/// Reads the tables inside an EDIABAS description file (.prg / .grp) that the user already has
/// installed with INPA, ISTA or EDIABAS. Nothing from these files is shipped with Klemme15; they are
/// read on the user's own machine, the same way EDIABAS itself reads them.
///
/// Format notes follow EdiabasLib (GPLv3, github.com/uholeschak/ediabaslib, EdiabasNet.ReadAllTables):
/// the file body is XOR 0xF7; the table directory offset is a plain int32 at 0x84; each directory
/// entry is 0x50 bytes (name[0x40], column-data offset, ?, columns, rows); cells are NUL-terminated
/// Windows-1252 strings, row 0 holds the column names.
/// </summary>
public sealed class SgbdFile
{
    private const byte Key = 0xF7;
    private readonly byte[] _data;
    private readonly Dictionary<string, (uint Offset, int Columns, int Rows)> _tables = new(StringComparer.OrdinalIgnoreCase);

    public string Path { get; }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path).ToUpperInvariant();
    public IReadOnlyCollection<string> TableNames => _tables.Keys;

    private SgbdFile(string path, byte[] data)
    {
        Path = path;
        _data = data;
        ReadDirectory();
    }

    public static SgbdFile Open(string path) => new(path, File.ReadAllBytes(path));

    public static SgbdFile FromBytes(string name, byte[] data) => new(name, data);

    public bool HasTable(string name) => _tables.ContainsKey(name);

    /// <summary>Returns the table as rows of column-name → value, or null when the file has no such table.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>>? ReadTable(string name)
    {
        if (!_tables.TryGetValue(name, out var t)) return null;
        int pos = (int)t.Offset;
        var header = new string[t.Columns];
        for (int c = 0; c < t.Columns; c++) header[c] = ReadCell(ref pos).ToUpperInvariant();
        var rows = new List<IReadOnlyDictionary<string, string>>(t.Rows);
        for (int r = 0; r < t.Rows; r++)
        {
            var row = new Dictionary<string, string>(t.Columns, StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < t.Columns; c++) row[header[c]] = ReadCell(ref pos);
            rows.Add(row);
        }
        return rows;
    }

    private void ReadDirectory()
    {
        if (_data.Length < 0x88) throw new InvalidDataException($"{Path}: too short to be an EDIABAS file.");
        int dir = BitConverter.ToInt32(_data, 0x84);          // stored unencrypted
        if (dir <= 0 || dir + 4 > _data.Length) return;      // file without tables
        int count = Int32At(dir);
        if (count < 0 || count > 10_000) throw new InvalidDataException($"{Path}: implausible table count {count}.");
        for (int i = 0; i < count; i++)
        {
            int e = dir + 4 + i * 0x50;
            if (e + 0x50 > _data.Length) throw new InvalidDataException($"{Path}: table directory runs past end of file.");
            string name = DecodeZ(e, 0x40);
            uint offset = (uint)Int32At(e + 0x40);
            int columns = Int32At(e + 0x48);
            int rows = Int32At(e + 0x4C);
            if (name.Length == 0 || offset >= _data.Length || columns <= 0 || columns > 256 || rows < 0) continue;
            _tables[name] = (offset, columns, rows);
        }
    }

    private int Int32At(int pos)
    {
        Span<byte> b = stackalloc byte[4];
        for (int i = 0; i < 4; i++) b[i] = (byte)(_data[pos + i] ^ Key);
        return BitConverter.ToInt32(b);
    }

    private string DecodeZ(int pos, int max)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < max && pos + i < _data.Length; i++)
        {
            byte b = (byte)(_data[pos + i] ^ Key);
            if (b == 0) break;
            sb.Append(Cp1252(b));
        }
        return sb.ToString();
    }

    private string ReadCell(ref int pos)
    {
        var sb = new StringBuilder();
        while (pos < _data.Length)
        {
            byte b = (byte)(_data[pos++] ^ Key);
            if (b == 0) break;
            if (sb.Length < 4096) sb.Append(Cp1252(b));
        }
        return sb.ToString();
    }

    // Windows-1252 without needing the CodePages encoding provider.
    private static readonly char[] High =
    {
        '€', '\u0081', '‚', 'ƒ', '„', '…', '†', '‡', 'ˆ', '‰', 'Š', '‹', 'Œ', '\u008D', 'Ž', '\u008F',
        '\u0090', '‘', '’', '“', '”', '•', '–', '—', '˜', '™', 'š', '›', 'œ', '\u009D', 'ž', 'Ÿ',
    };

    private static char Cp1252(byte b) => b is >= 0x80 and <= 0x9F ? High[b - 0x80] : (char)b;
}
