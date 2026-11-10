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

/// <summary>One dash (Check Control) message in the side panel.</summary>
public sealed class CheckControlItem(CheckControlMessage message)
{
    public CheckControlMessage Message { get; } = message;
    public string Title => Message.Title;
    public string Code => Message.Code;
    public bool IsRed => Message.Severity == CcSeverity.Critical && Message.Active;
    public bool IsYellow => !IsRed && Message.Active;
    public bool IsHistory => !Message.Active;
    public string StateText => Message.Active
        ? Message.Severity switch { CcSeverity.Critical => "Red", CcSeverity.Warning => "Yellow", _ => "Advisory" }
        : Message.MileageKm is { } km ? $"~{DisplayUnits.Distance(km)}" : "earlier";
    public string? Description => Message.Active ? Message.Description : null;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string? Hint => Message.Active && Message.Hint is { } h ? $"Tip: {h}" : null;
    public bool HasHint => Hint is not null;
    public string? ModuleText => Message.Active && Message.Module is { } m ? $"Usually raised by the {m}" : null;
    public bool HasModule => ModuleText is not null;
    public string? Official => Message.OfficialText is { } t && t != Message.Title ? $"BMW: {t}" : null;
    public bool HasOfficial => Official is not null;
}
