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

namespace Klemme15.Core;

/// <summary>Why a module turned a request down, in the sense the diagnostic standard (KWP2000) gives each answer.</summary>
public enum RefusalKind { None, GeneralReject, NotSupported, Conditions, Busy, NotFinished, OutOfRange, Security, NoAnswer }

/// <summary>
/// Turns a module's "no" into plain words and a next step. Modules answer a request they won't carry out with a
/// short code (KWP2000 negative responses); EDIABAS reports the same codes as names such as ERROR_ECU_GENERAL_REJECT.
/// The meaning of each code is the standard's; what to do about it is ours.
/// </summary>
public static class ModuleRefusals
{
    public static RefusalKind FromCode(byte code) => code switch
    {
        0x10 => RefusalKind.GeneralReject,
        0x11 or 0x12 => RefusalKind.NotSupported,
        0x21 or 0x78 => RefusalKind.Busy,
        0x22 or 0x24 => RefusalKind.Conditions,
        0x23 => RefusalKind.NotFinished,
        0x31 => RefusalKind.OutOfRange,
        0x33 or 0x35 or 0x36 or 0x37 => RefusalKind.Security,
        _ => RefusalKind.None,
    };

    /// <summary>What kind of refusal an error message reports (EDIABAS's names, or no answer at all).</summary>
    public static RefusalKind Classify(string? message)
    {
        if (string.IsNullOrEmpty(message)) return RefusalKind.None;
        var m = message.ToUpperInvariant();
        if (m.Contains("GENERAL_REJECT")) return RefusalKind.GeneralReject;
        if (m.Contains("CONDITIONS_NOT_CORRECT") || m.Contains("SEQUENCE_ERROR")) return RefusalKind.Conditions;
        if (m.Contains("NOT_SUPPORTED")) return RefusalKind.NotSupported;
        if (m.Contains("BUSY") || m.Contains("RESPONSE_PENDING")) return RefusalKind.Busy;
        if (m.Contains("ROUTINE_NOT_COMPLETE")) return RefusalKind.NotFinished;
        if (m.Contains("OUT_OF_RANGE")) return RefusalKind.OutOfRange;
        if (m.Contains("SECURITY_ACCESS") || m.Contains("INVALID_KEY") || m.Contains("EXCEED_NUMBER_OF_ATTEMPTS") || m.Contains("TIME_DELAY")) return RefusalKind.Security;
        if (m.Contains("IFH-0009") || m.Contains("IFH_0009") || m.Contains("NO RESPONSE")) return RefusalKind.NoAnswer;
        return RefusalKind.None;
    }

    /// <summary>Whether a fault stored in the module could be the reason (it refused because of the car's state).</summary>
    public static bool StateRelated(RefusalKind kind) => kind is RefusalKind.GeneralReject or RefusalKind.Conditions;

    /// <summary>One or two plain sentences: what the module said, and what to do. Null when there's nothing to add.</summary>
    public static string? Explain(RefusalKind kind, string module) => kind switch
    {
        RefusalKind.GeneralReject =>
            $"The {module} received the request and refused it without giving a reason. Most often the car isn't in the state the job needs (ignition, engine, speed, gear), or the module is in its fallback mode because of a fault. Go through 'Before you start' again and read this module's faults before you try again.",
        RefusalKind.Conditions =>
            $"The {module} says the car isn't in the state this job needs, or a step came in the wrong order. Go through 'Before you start' again. Faults stored in this module can also stop it: read them before you try again.",
        RefusalKind.NotSupported =>
            $"The {module} doesn't accept this request. Its software may be a different version from the one this procedure was written for, so trying again won't help.",
        RefusalKind.Busy => $"The {module} was busy. Wait a few seconds and try once more.",
        RefusalKind.NotFinished => $"The {module} started but hasn't finished yet. Wait, then check the result before you start it again.",
        RefusalKind.OutOfRange => $"The {module} rejected a value in the request. Check what you entered.",
        RefusalKind.Security => $"The {module} wants to be unlocked first. Klemme15 doesn't unlock modules, so this can't run here.",
        RefusalKind.NoAnswer => $"The {module} didn't answer. Check the ignition is on and the cable is plugged in at both ends.",
        _ => null,
    };
}
