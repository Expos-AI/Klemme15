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
