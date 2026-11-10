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

/// <summary>What each module does, in a sentence, for people who don't know BMW's abbreviations.</summary>
public static class ModuleGlossary
{
    private static readonly Dictionary<string, string> What = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DME"] = "The engine computer: fuel, ignition, emissions and the check engine light.",
        ["EGS"] = "Controls the automatic gearbox: shifting and its adaptations.",
        ["DSC"] = "Anti-lock brakes, traction and stability control.",
        ["ACSM"] = "Airbags, seat belt tensioners and crash sensors.",
        ["MRS"] = "Airbags, seat belt tensioners and crash sensors.",
        ["CAS"] = "Recognises the key and lets the car start; also the steering lock and the start button.",
        ["EWS"] = "The immobiliser: recognises the key and lets the engine start.",
        ["KOMBI"] = "The instrument cluster: gauges, warning lights, mileage and service reminders.",
        ["IKE"] = "The instrument cluster: gauges, warning lights, mileage and service reminders.",
        ["FRM"] = "Footwell module: exterior and interior lights, front windows and mirrors.",
        ["JBE"] = "Junction box: power distribution, wipers, rear windows, central locking and many relays.",
        ["KBM"] = "Body module: central locking, windows, wipers and interior lights.",
        ["GM"] = "Body module: central locking, windows, wipers and interior lights.",
        ["GM5"] = "Body module: central locking, windows, wipers and interior lights.",
        ["LM"] = "Light module: headlights, tail lights, indicators and bulb monitoring.",
        ["LCM"] = "Light module: headlights, tail lights, indicators and bulb monitoring.",
        ["IHKA"] = "Heating and air conditioning.",
        ["SZL"] = "Steering column switches: indicators, wipers, steering angle sensor and the airbag clock spring.",
        ["LWS"] = "Steering angle sensor, used by stability control.",
        ["EKPS"] = "Runs the electric fuel pump in the tank.",
        ["PDC"] = "Parking sensors.",
        ["CIC"] = "The head unit: radio, navigation and iDrive screen.",
        ["CCCA"] = "Part of the navigation and iDrive head unit.",
        ["RAD"] = "The radio and its display.",
        ["NAV"] = "The navigation computer.",
        ["FZD"] = "Roof console: sunroof, interior lights, rain and light sensor, alarm sensors.",
        ["SHD"] = "The sunroof motor.",
        ["RLS"] = "Rain and light sensor for automatic wipers and headlights.",
        ["VGSG"] = "Transfer case control for all-wheel drive (xDrive).",
        ["RDC"] = "Tyre pressure monitoring.",
        ["CVM"] = "The convertible top.",
        ["EPS"] = "Electric power steering.",
        ["AFS"] = "Active steering.",
        ["LDM"] = "Cruise control.",
        ["ACC"] = "Radar cruise control.",
        ["EDC"] = "Electronic dampers.",
        ["EMF"] = "The electric parking brake.",
        ["EHC"] = "Rear air suspension (self-levelling).",
        ["TEL"] = "The built-in telephone and Bluetooth.",
        ["AMP"] = "The audio amplifier.",
        ["DWA"] = "The alarm system.",
        ["SMFA"] = "Driver's seat memory and adjustment.",
        ["SMBF"] = "Passenger seat memory and adjustment.",
        ["ZBE"] = "The iDrive controller knob.",
        ["CID"] = "The iDrive display.",
        ["HUD"] = "The head-up display.",
        ["MFL"] = "Steering wheel buttons.",
        ["SINE"] = "The alarm siren and tilt sensor.",
        ["AHM"] = "Trailer module.",
        ["CDC"] = "The CD changer.",
        ["VM"] = "The TV and video module.",
        ["SDARS"] = "Satellite radio.",
        ["MOSTGW"] = "Gateway between the entertainment network and the rest of the car.",
        ["PGS"] = "Comfort access: keyless entry and start.",
        ["HKL"] = "The power tailgate.",
    };

    /// <summary>A sentence on what the module does, or null when it isn't in the glossary.</summary>
    public static string? For(EcuDefinition ecu)
    {
        var id = ecu.Id.Contains('.') ? ecu.Id[(ecu.Id.LastIndexOf('.') + 1)..] : ecu.Id;
        return What.GetValueOrDefault(id);
    }

    /// <summary>"Engine (DME)" plus what it does, for a tooltip.</summary>
    public static string Describe(EcuDefinition ecu) => For(ecu) is { } what ? $"{ecu.Name}\n{what}" : ecu.Name;
}
