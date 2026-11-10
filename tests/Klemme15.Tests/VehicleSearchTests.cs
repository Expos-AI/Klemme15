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

using Klemme15.Core;
using Klemme15.Data;
using Xunit;

namespace Klemme15.Tests;

public class VehicleSearchTests
{
    private static readonly VehicleDatabase Db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "klemme15-no-overrides"));
    private static IReadOnlyList<VehicleMatch> Find(string q) => VehicleSearch.Find(Db.Vehicles, q).Matches;

    [Theory]
    [InlineData("2011 328i", "E9x-328i")]
    [InlineData("328i 2011", "E9x-328i")]
    [InlineData("08 535i", "E60-535i")]
    [InlineData("E92 M3", "E9x-m3")]
    [InlineData("e46 m3", "E46-m3")]
    [InlineData("335xi", "E9x-335xi")]
    [InlineData("x5 diesel 2010", "E70-35d")]
    [InlineData("1997 328i", "E36-328i")]
    [InlineData("bmw 128i", "E8x-128i")]
    public void OwnersFindTheirCarByWhatTheyKnow(string query, string modelId) => Assert.Equal(modelId, Find(query)[0].Model.Id);

    [Fact]
    public void AChassisCodeListsThatFamilysModels()
    {
        Assert.All(Find("E92"), m => Assert.Equal("E9x", m.Family.Id));
        Assert.True(Find("E92").Count >= 10);
        Assert.All(Find("e87"), m => Assert.Equal("E8x", m.Family.Id));
        Assert.Contains(Find("xdrive"), m => m.Model.Id == "E9x-335xi");
    }

    [Fact]
    public void UnsupportedCarsGetAStraightAnswer()
    {
        Assert.Contains("F- or G-series", VehicleSearch.Find(Db.Vehicles, "F30 340i").Note!);
        Assert.Contains("Mini", VehicleSearch.Find(Db.Vehicles, "r56").Note!);
        Assert.Contains("isn't supported yet", VehicleSearch.Find(Db.Vehicles, "E65").Note!);
        Assert.Contains("No car matches", VehicleSearch.Find(Db.Vehicles, "corolla").Note!);
        Assert.Null(VehicleSearch.Find(Db.Vehicles, "").Note);
    }
}
