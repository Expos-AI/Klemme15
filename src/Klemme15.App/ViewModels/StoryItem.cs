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

namespace Klemme15.App.ViewModels;

/// <summary>One moment in the car's history, for the timeline on the Faults page: dash warnings and the faults stored with them.</summary>
public sealed class StoryItem(Incident incident)
{
    public Incident Incident { get; } = incident;
    public StoryEvent? Event => Incident.Event;
    public string When => Incident.When;
    public string Title => Incident.Title;
    public string Explanation => Incident.Explanation;
    public bool IsRed => Event is { Severity: CcSeverity.Critical } e && e.Kind != StoryKind.LowVoltage;
    public bool IsBattery => Event?.Kind is StoryKind.LowVoltage or StoryKind.BatteryWork || (Event is null && Incident.Faults.All(f => f.Supply));
    public bool IsOther => !IsRed && !IsBattery;
    public string Codes => Event is not { } e ? "" : string.Join("  ", e.Messages.Select(m => m.Code)) + (e.Times > e.Messages.Count ? $"  ·  shown {e.Times}×" : "");
    public bool HasCodes => Codes.Length > 0;

    /// <summary>The faults modules stored at this mileage.</summary>
    public string Faults => Incident.Faults.Count == 0 ? "" : "Faults stored then:  " + Incident.FaultLine;
    public bool HasFaults => Incident.Faults.Count > 0;
}
