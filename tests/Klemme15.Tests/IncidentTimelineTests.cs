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
using Xunit;

namespace Klemme15.Tests;

public class IncidentTimelineTests
{
    private static readonly EcuDefinition Dme = new() { Id = "E9x.DME", Name = "Engine (DME)", Address = 0x12, Protocol = BusProtocol.BmwFast, IsEngine = true };
    private static readonly EcuDefinition Dsc = new() { Id = "E9x.DSC", Name = "Stability control (DSC)", Address = 0x29, Protocol = BusProtocol.BmwFast };
    private static readonly EcuDefinition Kombi = new() { Id = "E9x.KOMBI", Name = "Instrument cluster (KOMBI)", Address = 0x60, Protocol = BusProtocol.BmwFast };
    private static readonly EcuDefinition Frm = new() { Id = "E9x.FRM", Name = "Footwell module (FRM)", Address = 0x72, Protocol = BusProtocol.BmwFast };

    private static Fault F(EcuDefinition ecu, string code, string text, long? km, bool info = false) => new()
    {
        EcuId = ecu.Id, Code = code, Source = FaultSource.Bmw, State = FaultState.Stored, Description = text, IsInfo = info,
        Details = km is { } k ? new Dictionary<string, string> { ["Mileage"] = $"{k} km" } : new Dictionary<string, string>(),
    };

    private static EcuScanResult Module(EcuDefinition ecu, params Fault[] faults) => new() { Ecu = ecu, Status = EcuScanStatus.Faults, Faults = faults };

    private static StoryEvent Event(long from, long to, string title) =>
        new(from, to, StoryKind.Brakes, title, "The cluster showed a brake warning.", [new CheckControlMessage { Id = 35, Title = title }], 1, CcSeverity.Critical);

    private static VehicleScanResult Scan(IReadOnlyList<EcuScanResult> ecus, params StoryEvent[] events) => new()
    {
        Started = DateTimeOffset.Now, Backend = "test", Ecus = ecus,
        Story = events.Length == 0 ? null : new CarStoryResult(events, events.Max(e => e.ToKm), events.Length),
    };

    [Fact]
    public void FaultsJoinTheDashWarningFromTheSameMileage()
    {
        var scan = Scan(
        [
            Module(Dsc, F(Dsc, "5DC0", "Wheel speed sensor rear right, supply", 98_120)),
            Module(Kombi, F(Kombi, "A3B9", "Message from DSC missing", 98_112), F(Kombi, "9999", "Background", 98_112, info: true)),
            Module(Dme, F(Dme, "2A82", "Intake VANOS stuck", 91_400), F(Dme, "P0171", "System too lean", null)),
        ], Event(88_000, 88_024, "Flat tyre"), Event(98_112, 98_112, "DSC failed"));

        var t = IncidentTimeline.Build(scan);
        Assert.Equal(3, t.FaultsWithMileage);
        Assert.Equal(1, t.FaultsWithoutMileage);
        Assert.Equal(["Flat tyre", "Engine (DME) stored 1 fault", "DSC failed"], t.Incidents.Select(i => i.Title).ToArray());

        var dsc = t.Incidents[^1];
        Assert.Equal(2, dsc.Faults.Count);
        Assert.Contains("2 modules stored 2 faults", dsc.Explanation);
        Assert.Contains("most likely belong to one event", dsc.Explanation);
        Assert.Equal("Instrument cluster (KOMBI) A3B9  ·  Stability control (DSC) 5DC0", dsc.FaultLine);
        Assert.Empty(t.Incidents[0].Faults);
        Assert.Equal("The cluster showed a brake warning.", t.Incidents[0].Explanation);
    }

    [Fact]
    public void FaultsWithoutADashWarningAreGroupedByMileage()
    {
        var scan = Scan(
        [
            Module(Dme, F(Dme, "2E7C", "Supply voltage too low", 120_004)),
            Module(Frm, F(Frm, "9CBB", "Undervoltage detected", 120_000)),
            Module(Dsc, F(Dsc, "5E20", "Power supply interrupted", 120_010), F(Dsc, "5DF0", "Hydraulic unit pump motor", 131_500)),
        ]);
        var t = IncidentTimeline.Build(scan);
        Assert.Equal(2, t.Incidents.Count);
        Assert.Equal("3 modules stored faults at the same time", t.Incidents[0].Title);
        Assert.Contains("one flat battery", t.Incidents[0].Explanation);
        Assert.Equal(120_000, t.Incidents[0].FromKm);
        Assert.Equal("Stability control (DSC) stored 1 fault", t.Incidents[1].Title);
        Assert.Equal("Stored at this mileage.", t.Incidents[1].Explanation);
    }

    [Fact]
    public void WithoutMileageNothingIsInvented()
    {
        var t = IncidentTimeline.Build(Scan([Module(Dme, F(Dme, "2A82", "Intake VANOS stuck", null))], Event(50_000, 50_000, "Brake pads worn")));
        var only = Assert.Single(t.Incidents);
        Assert.Empty(only.Faults);
        Assert.Equal(0, t.FaultsWithMileage);
        Assert.Equal(1, t.FaultsWithoutMileage);
        Assert.Empty(IncidentTimeline.Build(Scan([])).Incidents);
    }
}
