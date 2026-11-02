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

using System.Text;
using Klemme15.Data;
using Xunit;

namespace Klemme15.Tests;

public class SgbdFileTests
{
    /// <summary>Builds a minimal EDIABAS-style file with one table, encoded the same way as real .prg files.</summary>
    private static byte[] Build(string table, string[][] rowsIncludingHeader)
    {
        var cells = new List<byte>();
        foreach (var row in rowsIncludingHeader)
            foreach (var cell in row) { cells.AddRange(Encoding.Latin1.GetBytes(cell)); cells.Add(0); }

        const int dir = 0x100;
        int cellStart = dir + 4 + 0x50;
        var plain = new byte[cellStart + cells.Count];
        void Put(int pos, int v) => BitConverter.GetBytes(v).CopyTo(plain, pos);
        Put(dir, 1);
        Encoding.ASCII.GetBytes(table).CopyTo(plain, dir + 4);
        Put(dir + 4 + 0x40, cellStart);
        Put(dir + 4 + 0x48, rowsIncludingHeader[0].Length);
        Put(dir + 4 + 0x4C, rowsIncludingHeader.Length - 1);
        cells.CopyTo(plain, cellStart);

        var file = plain.Select(b => (byte)(b ^ 0xF7)).ToArray();
        BitConverter.GetBytes(dir).CopyTo(file, 0x84);   // the directory pointer is stored in the clear
        return file;
    }

    [Fact]
    public void Reads_a_fault_text_table()
    {
        var bytes = Build("FORTTEXTE", new[]
        {
            new[] { "ORT", "ORTTEXT" },
            new[] { "0x2A82", "Test text one" },
            new[] { "0x2A87", "Groß €" },   // ß is Latin-1, € is the Windows-1252 extra at 0x80
        }.Select(r => r.Select(c => c.Replace("€", "\u0080")).ToArray()).ToArray());

        var f = SgbdFile.FromBytes("MSV70.prg", bytes);

        Assert.True(f.HasTable("forttexte"));
        var rows = f.ReadTable("FORTTEXTE")!;
        Assert.Equal(2, rows.Count);
        Assert.Equal("0x2A82", rows[0]["ORT"]);
        Assert.Equal("Test text one", rows[0]["orttext"]);
        Assert.Equal("Groß €", rows[1]["ORTTEXT"]);
        Assert.Null(f.ReadTable("NOPE"));
    }
}
