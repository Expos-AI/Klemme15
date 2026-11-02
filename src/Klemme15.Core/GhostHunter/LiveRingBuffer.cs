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

namespace Klemme15.Core.GhostHunter;

/// <summary>One reading of every watched value at one moment.</summary>
public sealed record DatalogFrame(DateTimeOffset Time, IReadOnlyDictionary<string, double> Values);

/// <summary>
/// A fixed-size, thread-safe circular buffer that keeps the last <see cref="Window"/> of items. It never grows: the
/// array is allocated once, the oldest item is overwritten when it's full, and items older than the window are skipped
/// on read, so a drive of any length uses the same memory. Reads return copies, so a capture can be written while new
/// frames keep arriving.
/// </summary>
public sealed class LiveRingBuffer<T>
{
    private readonly object _lock = new();
    private readonly T[] _items;
    private readonly Func<T, DateTimeOffset> _timeOf;
    private int _start, _count;

    /// <param name="window">How much history to keep (60 seconds for the Ghost Hunter).</param>
    /// <param name="capacity">Upper bound on items: the window at the fastest expected rate, with headroom.</param>
    public LiveRingBuffer(TimeSpan window, int capacity, Func<T, DateTimeOffset> timeOf)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Window = window;
        _items = new T[capacity];
        _timeOf = timeOf;
    }

    public TimeSpan Window { get; }
    public int Capacity => _items.Length;
    public int Count { get { lock (_lock) return _count; } }

    public void Add(T item)
    {
        lock (_lock)
        {
            var end = (_start + _count) % _items.Length;
            _items[end] = item;
            if (_count < _items.Length) _count++;
            else _start = (_start + 1) % _items.Length;   // full: overwrite the oldest
            // Drop items that have fallen out of the window (so the old references can be collected).
            var newest = _timeOf(item);
            while (_count > 1 && newest - _timeOf(_items[_start]) > Window)
            {
                _items[_start] = default!;
                _start = (_start + 1) % _items.Length;
                _count--;
            }
        }
    }

    /// <summary>A copy of everything in the window, oldest first.</summary>
    public IReadOnlyList<T> Snapshot()
    {
        lock (_lock)
        {
            var copy = new T[_count];
            for (var i = 0; i < _count; i++) copy[i] = _items[(_start + i) % _items.Length];
            return copy;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_items);
            _start = _count = 0;
        }
    }
}
