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

public class SnapshotDiffTests
{
    private static readonly EcuDefinition Dme = new() { Id = "E9x.DME", Name = "Engine (DME)", Address = 0x12, Protocol = BusProtocol.BmwFast, IsEngine = true };
    private static readonly EcuDefinition Frm = new() { Id = "E9x.FRM", Name = "Footwell module (FRM)", Address = 0x72, Protocol = BusProtocol.BmwFast };
    private static readonly EcuDefinition Dsc = new() { Id = "E9x.DSC", Name = "Stability control (DSC)", Address = 0x29, Protocol = BusProtocol.BmwFast };

    private static Fault F(EcuDefinition ecu, string code, FaultState state, string text, bool info = false) =>
        new() { EcuId = ecu.Id, Code = code, Source = FaultSource.Bmw, State = state, Description = text, IsInfo = info };

    private static EcuScanResult Module(EcuDefinition ecu, string? part, params Fault[] faults) => new()
    {
        Ecu = ecu, Status = faults.Length > 0 ? EcuScanStatus.Faults : EcuScanStatus.Ok, Faults = faults,
        Identity = part is null ? null : new EcuIdentity { PartNumber = part },
    };

    private static EcuScanResult Silent(EcuDefinition ecu) => new() { Ecu = ecu, Status = EcuScanStatus.NoResponse };

    private static CarSnapshot Snap(DateTimeOffset when, IReadOnlyList<EcuScanResult> ecus, IReadOnlyList<CheckControlMessage>? cc = null, IReadOnlyList<SnapshotCoding>? coding = null, string model = "E9x-325i") => new()
    {
        Manifest = new SnapshotManifest { Created = when, ModelId = model, ModelName = model[4..], Chassis = "E9x " + model[4..] },
        Scan = new VehicleScanResult { Started = when, Backend = "test", Ecus = ecus, CheckControl = cc ?? [] },
        Coding = coding ?? [],
    };

    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ARepairShowsWhatWentAwayAndWhatIsStillThere()
    {
        var before = Snap(Monday,
        [
            Module(Dme, "7561234", F(Dme, "2A82", FaultState.Present, "Intake VANOS stuck"), F(Dme, "29E0", FaultState.Stored, "Mixture lean"), F(Dme, "2F00", FaultState.Stored, "Background entry", info: true)),
            Module(Frm, "7004041", F(Frm, "A8C5", FaultState.Stored, "Left daytime running light")),
            Module(Dsc, "6789012"),
        ], [new CheckControlMessage { Id = 229, Title = "Charge the battery", Active = true }]);
        var after = Snap(Monday.AddDays(3),
        [
            Module(Dme, "7561234", F(Dme, "29E0", FaultState.Present, "Mixture lean"), F(Dme, "2D06", FaultState.Stored, "Air mass, plausibility")),
            Module(Frm, "7004041", F(Frm, "A8C5", FaultState.Stored, "Left daytime running light")),
            Module(Dsc, "6789012"),
        ]);
        var c = SnapshotDiff.Compare(before, after);

        Assert.Equal("1 fault gone, 1 new, 2 still there.", c.Headline);
        Assert.Contains(c.Of(DiffKind.Gone), i => i.Area == "Fault" && i.Title.StartsWith("2A82", StringComparison.Ordinal) && i.Detail.Length == 0);
        Assert.Contains(c.Of(DiffKind.New), i => i.Area == "Fault" && i.Title.StartsWith("2D06", StringComparison.Ordinal));
        Assert.Contains(c.Of(DiffKind.Changed), i => i.Title.StartsWith("29E0", StringComparison.Ordinal) && i.Detail == "Stored before, active now.");
        Assert.Contains(c.Of(DiffKind.Same), i => i.Title.StartsWith("A8C5", StringComparison.Ordinal));
        Assert.Contains(c.Of(DiffKind.Gone), i => i.Area == "Dash message" && i.Title.Contains("Charge the battery"));
        Assert.DoesNotContain(c.Items, i => i.Title.Contains("2F00"));   // background entries are left out
        Assert.Contains("Coding wasn't compared", string.Join(" ", c.Notes));
    }

    [Fact]
    public void AModuleThatWentSilentIsNotCountedAsFixed()
    {
        var before = Snap(Monday, [Module(Dme, "7561234"), Module(Frm, "7004041", F(Frm, "A8C5", FaultState.Stored, "Left daytime running light"))]);
        var after = Snap(Monday.AddDays(1), [Module(Dme, "7561234"), Silent(Frm)]);
        var c = SnapshotDiff.Compare(before, after);
        Assert.Contains(c.Of(DiffKind.Gone), i => i.Area == "Module" && i.Where == Frm.Name);
        var fault = Assert.Single(c.Of(DiffKind.Gone), i => i.Area == "Fault");
        Assert.Contains("isn't proof the fault is fixed", fault.Detail);
    }

    [Fact]
    public void AReplacedModuleAndChangedCodingAreReported()
    {
        var before = Snap(Monday, [Module(Frm, "7004041"), Silent(Dsc)], coding: [new SnapshotCoding("FRM", "7004041", "04", [new SnapshotCodingBlock(0x3000, "00 11 22 33"), new SnapshotCodingBlock(0x3001, "AA BB")])]);
        var after = Snap(Monday.AddDays(9), [Module(Frm, "9249084"), Module(Dsc, "6789012")], coding: [new SnapshotCoding("FRM", "9249084", "04", [new SnapshotCodingBlock(0x3000, "0011FF33"), new SnapshotCodingBlock(0x3001, "aabb")])]);
        var c = SnapshotDiff.Compare(before, after);
        Assert.Contains(c.Of(DiffKind.Changed), i => i.Area == "Module" && i.Detail.Contains("7004041") && i.Detail.Contains("9249084"));
        Assert.Contains(c.Of(DiffKind.New), i => i.Area == "Module" && i.Where == Dsc.Name);
        var coding = Assert.Single(c.Items, i => i.Area == "Coding");
        Assert.Equal("1 byte in 1 block. Open both snapshots on the Coding page to see which options.", coding.Detail);
        Assert.Equal("No faults in either snapshot.", c.Headline);
        Assert.Equal(3, SnapshotDiff.DifferentBytes("0011", "001122334455") + SnapshotDiff.DifferentBytes("FF", "ff") - 1);
    }

    [Fact]
    public void DifferentModelsAndReversedOrderAreSaid()
    {
        var c = SnapshotDiff.Compare(Snap(Monday.AddDays(5), [Module(Dme, "1")], model: "E9x-335i"), Snap(Monday, [Module(Dme, "1")]));
        Assert.Contains(c.Notes, n => n.Contains("different models"));
        Assert.Contains(c.Notes, n => n.Contains("older than the first"));
    }

    [Fact]
    public void TheReportEscapesWhatTheSnapshotsSay()
    {
        var evil = Snap(Monday, [Module(Dme, "1", F(Dme, "1234", FaultState.Stored, "<script>alert(1)</script>"))]);
        var html = SnapshotDiff.ToHtml(SnapshotDiff.Compare(Snap(Monday.AddDays(-1), [Module(Dme, "1")]), evil));
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        var text = SnapshotDiff.ToText(SnapshotDiff.Compare(evil, evil));
        Assert.Contains("Unchanged", text);
        Assert.Contains("1 fault still there.", text);
    }
}
