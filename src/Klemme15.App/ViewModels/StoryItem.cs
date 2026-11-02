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

/// <summary>One moment in the car's warning history, for the timeline on the Faults page.</summary>
public sealed class StoryItem(StoryEvent e)
{
    public StoryEvent Event { get; } = e;
    public string When => Event.When;
    public string Title => Event.Title;
    public string Explanation => Event.Explanation;
    public bool IsRed => Event.Severity == CcSeverity.Critical && Event.Kind != StoryKind.LowVoltage;
    public bool IsBattery => Event.Kind is StoryKind.LowVoltage or StoryKind.BatteryWork;
    public bool IsOther => !IsRed && !IsBattery;
    public string Codes => string.Join("  ", Event.Messages.Select(m => m.Code)) + (Event.Times > Event.Messages.Count ? $"  ·  shown {Event.Times}×" : "");
}
