"""Adds a "variants" list to each module in db/modules.json: the EDIABAS variant names (SGBD file names)
that module can be on that chassis. These are just identifiers, used to pick the right fault dictionary
from the user's own BMW files or from the optional bmw-unified-db download.

Usage: python3 tools/research/variants.py <path to bmw-unified-db json/variants folder>

E9x candidates come from the database's own applicability and routing data (E9x is filed under E89).
E46 modules are mostly K-line (DS2) and have no routing entries, so they are listed by hand below.
"""
import json, os, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MODULES = ROOT / "src/Klemme15.Data/db/modules.json"
E9X = {"E89", "E90", "E91", "E92", "E93", "E81", "E82", "E87", "E88"}

E46 = {
    "E46.DME": ["ms430ds0", "ms450ds0", "ms420ds0", "mss54ds0", "me9n45", "me9k_ng4", "mev9n46",
                "bms46ds0", "bms46ds1", "bms43ds0", "d50m47a", "d50m47b1", "dde50k47", "d50m57a1", "d50m57e1"],
    "E46.EGS": ["_gs20", "gs20", "gs30", "gs8600", "gs8601", "gs8604", "_gs834", "_gs860x"],
    "E46.DSC": ["dsc_e46", "dsc57", "dsc_mk60", "ascmk20"],
    "E46.IKE": ["kombi46", "kombi46r"],
    "E46.GM5": ["zke5", "zke5_s12"],
    "E46.LCM": ["lsz", "lsz_2"],
    "E46.IHKA": ["ihka46", "ihka46_2", "ihka46_3", "ihkr46"],
    "E46.EWS": ["ews3"],
    "E46.MRS": ["mrs4"],
    "E46.PDC": ["pdcact"],
    "E46.RLS": ["rls_ds2"],
    "E46.SHD": ["shd46", "shd46_2"],
    "E46.NAV": ["navmk4", "navmk4_2"],
    "E46.CVM": ["cvm_ii"],
    "E46.TEL": ["telibus", "telibus2", "telibus3", "tel_us"],
    "E46.VM": ["vm5ibus"],
}


def main(folder):
    variants = {}
    for f in os.listdir(folder):
        if not f.endswith(".json"):
            continue
        d = json.load(open(os.path.join(folder, f), encoding="utf-8"))
        name = d["sgbd"]
        if d.get("is_stub") or not d.get("dtcs") or " " in name or name.startswith("kopie"):
            continue
        variants[name] = d

    data = json.load(open(MODULES, encoding="utf-8"))
    for m in data["modules"]:
        if m["id"].startswith("E9x."):
            addr = "0x" + m["address"].upper()
            found = [v for v in variants.values()
                     if E9X & set(v.get("chassis") or [])
                     and any((r.get("diag_address") or "").upper() == addr.upper() for r in v.get("routes", []))]
            found.sort(key=lambda v: (-int(v.get("has_local_binary") or 0),
                                      -sum(1 for x in v["dtcs"] if x.get("location_text_en")), v["sgbd"]))
            names = [v["sgbd"] for v in found]
        else:
            names = [n for n in E46.get(m["id"], []) if n in variants]
        if names:
            m["variants"] = names
        else:
            m.pop("variants", None)

    with open(MODULES, "w", encoding="utf-8") as fh:
        json.dump(data, fh, indent=1, ensure_ascii=False)
        fh.write("\n")
    for m in data["modules"]:
        print(f'{m["id"]:12} {", ".join(m.get("variants", [])) or "-"}')


if __name__ == "__main__":
    main(sys.argv[1])
