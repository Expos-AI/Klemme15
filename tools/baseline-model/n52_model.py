#!/usr/bin/env python3
"""
N52 physics model: the source of the "N52 physics model (not measured)" baseline shipped in
src/Klemme15.Data/db/baselines.json, and of a reference log, n52_model_log.csv.

    python3 tools/baseline-model/n52_model.py            # writes both files next to this script
    python3 tools/baseline-model/n52_model.py --update   # also replaces the profile in db/baselines.json

This is a model, not a measurement. The bands are wide tolerances, not percentiles of real cars, and the profile is
labelled as a model in the app. It is replaced by real baselines once three or more healthy N52s have been logged
(see docs/BASELINES.md).

Air mass = displacement x (rpm / 2) x air density x volumetric efficiency (VE). A four-stroke engine fills each
cylinder once every two turns, hence rpm / 2.
"""
import csv
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DB = os.path.join(HERE, "..", "..", "src", "Klemme15.Data", "db", "baselines.json")

DISPLACEMENT_M3 = 2.996e-3   # N52B30: 2,996 cc
AIR_DENSITY = 1.184          # kg/m3 at 25 C, sea level
IDLE_RPM, HOLD_RPM, REDLINE = 660, 2500, 6500

# Tolerances around the model, wide on purpose: real cars differ with altitude (about -10 % air per 1,000 m),
# air temperature, the 190 / 218 hp versions and each air mass meter's calibration.
MAF_PART_TOLERANCE = 0.30
MAF_WOT_TOLERANCE = 0.25
TRIM_TOLERANCE = 10.0         # total trim (short + long term), percent either side of zero


def volumetric_efficiency(rpm, wot):
    """Part throttle: low, Valvetronic barely opens the valves. Full throttle: about 0.95 near peak torque
    (2,750 rpm), easing to about 0.88 at 6,500."""
    if not wot:
        return 0.22 - 0.02 * (rpm - IDLE_RPM) / (HOLD_RPM - IDLE_RPM)
    if rpm <= 2750:
        return 0.92 + 0.03 * (rpm - 2500) / 250
    return 0.95 - 0.07 * ((rpm - 2750) / (REDLINE - 2750)) ** 1.5


def maf_kg_per_h(rpm, wot):
    return DISPLACEMENT_M3 * (rpm / 2) / 60 * AIR_DENSITY * volumetric_efficiency(rpm, wot) * 3600


def load_percent(rpm, wot):
    """Calculated load is roughly how full the cylinders are, as a percent."""
    return volumetric_efficiency(rpm, wot) * 100


def log_sample(t):
    """60 s at 10 Hz: warm idle, a no-load hold at 2,500 rpm, then a full-throttle pull in gear to 6,500 rpm."""
    if t < 20:
        phase, rpm, wot = "idle", float(IDLE_RPM), False
    elif t < 40:
        phase, rpm, wot = "hold", float(HOLD_RPM), False
    else:
        x = (t - 40) / 20
        phase, rpm, wot = "wot", HOLD_RPM + (REDLINE - HOLD_RPM) * (0.6 * x + 0.4 * x * x), True
    # Closed loop: the short-term trim swings around 0 as the oxygen sensor switches. Full throttle is open loop.
    stft = 0.0 if wot else 1.5 * math.sin(2 * math.pi * (1.0 if phase == "idle" else 2.0) * t)
    return phase, rpm, maf_kg_per_h(rpm, wot), stft, 0.0, 95.0, load_percent(rpm, wot)


def write_log(path):
    with open(path, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["Time(s)", "RPM", "MAF(kg/h)", "STFT_Bank1(%)", "LTFT_Bank1(%)", "CoolantTemp(C)", "Load(%)", "Phase"])
        for i in range(601):
            t = i / 10
            phase, rpm, maf, stft, ltft, coolant, load = log_sample(t)
            w.writerow([f"{t:.1f}", f"{rpm:.0f}", f"{maf:.1f}", f"{stft:.2f}", f"{ltft:.2f}", f"{coolant:.1f}", f"{load:.0f}", phase])


def band(x, median, tolerance, relative=True):
    lo, hi = (median * (1 - tolerance), median * (1 + tolerance)) if relative else (median - tolerance, median + tolerance)
    return {"x": x, "p10": round(lo, 2), "median": round(median, 2), "p90": round(hi, 2), "samples": 0}


def profile():
    part = [band(x, maf_kg_per_h(max(x, IDLE_RPM), False) / 3.6, MAF_PART_TOLERANCE) for x in range(750, 2501, 250)]
    wot = [band(x, maf_kg_per_h(x, True) / 3.6, MAF_WOT_TOLERANCE) for x in range(2500, REDLINE + 1, 250)]
    trim = [band(x, 0.0, TRIM_TOLERANCE, relative=False) for x in range(750, 4001, 250)]
    return {
        "id": "n52-model",
        "title": "N52 physics model (not measured)",
        "engines": ["N52"],
        "conditions": "Worked out from the engine's size and how full its cylinders get, at sea level and 25 °C. "
                      "Warm engine only. Air mass is split into idle / no load and full throttle, so it needs engine load in your log. "
                      "Real cars differ, so the bands are wide: being outside them is worth a look, being inside them doesn't prove the engine is healthy.",
        "cars": 0,
        "isModel": True,
        "sources": ["tools/baseline-model/n52_model.py"],
        "curves": [
            {"name": "Total trim bank 1 across engine speed", "y": "stft1+ltft1", "x": "rpm", "filter": "coolant > 70 and load < 80", "filterRequired": True, "binWidth": 250, "points": trim},
            {"name": "Air mass at idle and no load", "y": "maf", "x": "rpm", "filter": "coolant > 70 and load < 40", "filterRequired": True, "binWidth": 250, "points": part},
            {"name": "Air mass at full throttle", "y": "maf", "x": "rpm", "filter": "coolant > 70 and load > 80", "filterRequired": True, "binWidth": 250, "points": wot},
        ],
    }


def main():
    write_log(os.path.join(HERE, "n52_model_log.csv"))
    p = profile()
    with open(os.path.join(HERE, "n52_model_profile.json"), "w") as f:
        json.dump(p, f, indent=1, ensure_ascii=False)
        f.write("\n")
    if "--update" in sys.argv:
        with open(DB) as f:
            profiles = json.load(f)
        profiles = [q for q in profiles if q.get("id") != p["id"]] + [p]
        with open(DB, "w") as f:
            json.dump(profiles, f, indent=1, ensure_ascii=False)
            f.write("\n")
    print("Wrote n52_model_log.csv and n52_model_profile.json" + (" and updated db/baselines.json" if "--update" in sys.argv else ""))


if __name__ == "__main__":
    main()
