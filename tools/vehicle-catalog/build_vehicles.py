#!/usr/bin/env python3
"""
Adds more BMW families to src/Klemme15.Data/db/modules.json and vehicles.json from bmw-unified-db (bmw.sqlite).

    python3 tools/vehicle-catalog/build_vehicles.py /path/to/bmw.sqlite

D-CAN families (2007 and newer) take each module's diagnostic address from BMW's routing data for that chassis.
K-line (DS2) families use the same DS2 addresses as the E46 (DME 0x12, gearbox 0x32, ABS/DSC 0x56, cluster 0x80,
body module 0x00, light module 0xD0 ...), which are shared across the DS2 cars; only module types whose address is
known are added. Module versions (variants) come from the database's chassis lists. E46 and E9x are not touched.
Model names, years and engines are our own short summary. Everything added here is marked unverified until tested.
"""
import json, sqlite3, sys, pathlib, collections

ROOT = pathlib.Path(__file__).resolve().parents[2]
con = sqlite3.connect(sys.argv[1] if len(sys.argv) > 1 else "bmw.sqlite")
MODULES = ROOT / "src/Klemme15.Data/db/modules.json"
VEHICLES = ROOT / "src/Klemme15.Data/db/vehicles.json"
mod_doc = json.load(open(MODULES))
veh_doc = json.load(open(VEHICLES))
KEEP = {"E46", "E9x"}

existing = {m["id"].split(".")[1]: m for m in mod_doc["modules"] if m["id"].startswith("E9x.")}
existing_ds2 = {m["id"].split(".")[1]: m for m in mod_doc["modules"] if m["id"].startswith("E46.")}

# Names, categories and drawing positions for module types the E46/E9x lists don't have.
EXTRA = {
    "HUD": ("Head-up display", "Infotainment", [300, 230]),
    "LDM": ("Longitudinal dynamics (cruise control)", "Chassis", [250, 300]),
    "EMF": ("Electric parking brake (EMF)", "Chassis", [760, 230]),
    "ARS": ("Active roll stabilisation", "Chassis", [180, 230]),
    "KAFAS": ("Camera systems (lane and light assist)", "Body", [160, 230]),
    "HKL": ("Power tailgate", "Body", [950, 230]),
    "GWS": ("Gear selector", "Powertrain", [430, 230]),
    "FLA": ("High beam assistant", "Body", [140, 260]),
    "RSE": ("Rear seat entertainment", "Infotainment", [700, 230]),
    "RFK": ("Rear view camera", "Body", [960, 260]),
    "FKA": ("Rear climate control", "Comfort", [640, 230]),
    "FRR": ("Active cruise control radar", "Chassis", [64, 268]),
    "TFE": ("Fuel tank electronics", "Powertrain", [760, 300]),
    "TRSVC": ("Top and rear view cameras", "Body", [960, 200]),
    "SVT": ("Servotronic", "Chassis", [262, 130]),
    "ICM": ("Integrated chassis management", "Chassis", [430, 300]),
    "VSW": ("Video switch", "Infotainment", [870, 200]),
    "ECALL": ("Emergency call", "Infotainment", [520, 300]),
    "LM": ("Light module", "Body", [400, 368]),
    "CVM": ("Convertible top", "Body", [858, 300]),
    "ISPB": ("Integrated supply module", "Body", [930, 330]),
    "VDM": ("Vertical dynamics (adaptive drive)", "Chassis", [760, 140]),
    "CCC": ("Navigation and iDrive (CCC/CIC)", "Infotainment", [338, 205]),
    "DME": ("Engine (DME/DDE)", "Powertrain", [285, 110]),
}

# Address -> short id when BMW's group name isn't a good id ("D_KBM" is the FRM, "D_MMI" the head unit).
ADDRESS_IDS = {
    "12": "DME", "18": "EGS", "29": "DSC", "01": "ACSM", "40": "CAS", "60": "KOMBI", "72": "FRM", "00": "JBE", "78": "IHKA",
    "02": "SZL", "17": "EKPS", "64": "PDC", "63": "CIC", "56": "FZD", "16": "AFS", "19": "VGSG", "20": "RDC", "21": "ACC",
    "24": "CVM", "27": "PGS", "30": "EPS", "36": "TEL", "37": "AMP", "38": "EHC", "39": "EDC", "3B": "NAV", "3C": "CDC",
    "3F": "ASK", "41": "DWA", "44": "SHD", "47": "ANTTU", "4B": "VM", "50": "SINE", "54": "SDARS", "5B": "DAB", "62": "MOSTGW",
    "67": "ZBE", "6D": "SMFA", "6E": "SMBF", "71": "AHM", "73": "CID", "7A": "SHZH", "A0": "CCCA", "70": "LM",
}

def variants_for(chassis_list):
    vs = []
    for ch in chassis_list:
        vs += [r[0].lower() for r in con.execute("select sgbd from chassis_variant where chassis=?", (ch,))]
    return sorted(set(vs))

def dcan_modules(prefix, chassis_list):
    by_addr = collections.defaultdict(set)
    for v in variants_for(chassis_list):
        for (addr,) in con.execute("select distinct diag_address from routing where lower(sgbd)=? and diag_address is not null", (v,)):
            a = addr.replace("0x", "").upper().zfill(2)
            if a in ("00",) and not v.startswith("jb"):
                continue
            by_addr[a].add(v)
    # The database's chassis lists are incomplete; add E9x module versions BMW also lists for these chassis
    # (and all of them for the E8x, which shares the E9x platform: komb87, dsc_87, frm_87 ...).
    for short, src in existing.items():
        for v in src.get("variants", []):
            row = con.execute("select chassis from ecu_variant where lower(sgbd)=?", (v,)).fetchone()
            listed = row and row[0] and any(f'"{ch}"' in row[0] for ch in chassis_list)
            if listed or prefix == "E8x":
                by_addr[src["address"].upper().zfill(2)].add(v)
    mods = []
    for addr, vs in sorted(by_addr.items()):
        fam = collections.Counter(con.execute("select ecu_family from ecu_variant where lower(sgbd)=?", (v,)).fetchone()[0] or "" for v in vs).most_common(1)[0][0]
        short = ADDRESS_IDS.get(addr) or fam.upper()
        if short in existing:
            src = existing[short]
            name, cat, pos = src["name"], src["category"], src.get("pos")
        elif short in EXTRA:
            name, cat, pos = EXTRA[short]
        else:
            continue   # unknown module type: leave it to "Find other modules"
        m = {"id": f"{prefix}.{short}", "name": name, "address": addr, "protocol": "BmwFast", "category": cat,
             "sgbd": [f"D_00{addr}"], "verified": False, "variants": sorted(vs)}
        if short == "DME": m["engine"] = True; m["sgbd"] = [f"D_0012", "D_MOTOR"]
        if pos: m["pos"] = pos
        if short in existing and existing[short].get("partOf"):
            m["partOf"] = existing[short]["partOf"].replace("E9x.", f"{prefix}.")
        mods.append(m)
    return mods

# DS2 module types we know the address of (shared with the E46), and the database families that are that module.
DS2 = {
    "DME": ("12", ["DME", "DDE"]), "EGS": ("32", ["EGS", "AGS"]), "DSC": ("56", ["DSC", "ASC", "ABS"]), "MRS": ("A4", ["MRS", "ABG"]),
    "IKE": ("80", ["KOMBI", "IKE", "KMB"]), "GM": ("00", ["GM", "GM3", "ZKE"]), "LCM": ("D0", ["LM", "LCM", "LSZ"]),
    "IHKA": ("5B", ["IHKA", "IHKR", "IHK", "IHR"]), "EWS": ("44", ["EWS"]), "PDC": ("60", ["PDC", "PDCACT", "PDCE"]), "RLS": ("E8", ["RLS"]),
    "SHD": ("08", ["SHD"]), "RAD": ("68", ["RAD"]), "NAV": ("7F", ["NAV", "NAVMK"]), "TEL": ("C8", ["TEL", "TELIBUS"]), "DSP": ("6A", ["DSP"]),
    "LWS": ("57", ["LWS"]),
}
DS2_NAMES = {"GM": ("Body module (GM)", "Body", [395, 110]), "LCM": ("Light check module", "Body", [400, 368])}

ENGINES = {"E39": ["M52", "M54", "M62", "S62", "M57"], "E53": ["M54", "M62", "N62", "M57"], "E38": ["M62", "M73", "M57"]}

def ds2_modules(prefix, chassis_list):
    fams = collections.defaultdict(list)
    # Engine computers by the engines this family had (the chassis lists miss many).
    for eng in ENGINES.get(prefix, []):
        for (v,) in con.execute("select a.sgbd from ecu_alias a join ecu_variant e on lower(e.sgbd)=lower(a.sgbd) "
                                "where a.alias_type='engine' and a.alias=? and e.ecu_family in ('DME','DDE')", (eng,)):
            if not v.lower().startswith(("fl", "flash", "me7flash")):
                fams["DME"].append(v.lower())
    for v in variants_for(chassis_list):
        row = con.execute("select ecu_family from ecu_variant where lower(sgbd)=?", (v,)).fetchone()
        if not row or not row[0] or v.startswith("c_"):
            continue
        fams[row[0].upper()].append(v)
    mods = []
    for short, (addr, families) in DS2.items():
        vs = sorted(set(v for f in families for v in fams.get(f, [])))
        if not vs:
            continue
        src = existing_ds2.get(short) or existing_ds2.get({"IKE": "IKE", "GM": "GM5"}.get(short, short))
        name, cat, pos = DS2_NAMES.get(short) or ((src["name"], src["category"], src.get("pos")) if src else (short, "Body", None))
        m = {"id": f"{prefix}.{short}", "name": name, "address": addr, "protocol": "Ds2", "category": cat,
             "sgbd": [f"D_00{addr}"], "verified": False, "variants": vs}
        if short == "DME": m["engine"] = True; m["sgbd"] = ["D_MOTOR", "D_0012"]
        if pos: m["pos"] = pos
        mods.append(m)
    return mods

def model(fam, mid, name, body, years, engine, drive, mods, idle=700):
    return {"id": f"{fam}-{mid}", "name": name, "body": body, "years": years, "engine": engine, "drive": drive,
            "modules": [f"{fam}.{m}" for m in mods],
            # One standard OBD-II code so the demo car has something to find (lean mixture, or EGR flow on diesels).
            "demo": {"idle": idle, "faults": [{"code": "P0401" if "diesel" in engine else "P0171", "module": f"{fam}.DME", "present": True}]}}

D_CORE = ["DME", "EGS", "DSC", "ACSM", "CAS", "KOMBI", "IHKA", "SZL", "PDC", "CIC"]
FAMILIES = [
    # id, name, years, cable, chassis in the database, models
    ("E8x", "1 Series E81 / E82 / E87 / E88 (2007 on)", "2007-2013", "DCan", ["E87", "E82", "E88"], [
        ("128i", "128i", "Coupe, convertible", "2008-2013", "N52 3.0 L inline-6", "Rear-wheel drive", D_CORE + ["FRM", "JBE"]),
        ("135i", "135i", "Coupe, convertible", "2008-2013", "N54 3.0 L twin-turbo inline-6 (N55 from 2011)", "Rear-wheel drive", D_CORE + ["FRM", "JBE"]),
        ("118d", "118d / 120d", "Hatchback, coupe", "2007-2013", "N47 2.0 L diesel inline-4", "Rear-wheel drive", D_CORE + ["FRM", "JBE"]),
        ("120i", "116i / 118i / 120i", "Hatchback, coupe", "2007-2013", "N46 2.0 L inline-4 (N43/N45 in some markets)", "Rear-wheel drive", D_CORE + ["FRM", "JBE"]),
        ("123d", "123d", "Hatchback, coupe", "2007-2011", "N47 2.0 L twin-turbo diesel inline-4", "Rear-wheel drive", D_CORE + ["FRM", "JBE"]),
    ]),
    ("E60", "5 Series E60 / E61 and 6 Series E63 / E64 (2007 on)", "2007-2010", "DCan", ["E60", "E63", "E64", "E61"], [
        ("528i", "528i", "Sedan, touring", "2008-2010", "N52 3.0 L inline-6", "Rear-wheel drive or xDrive", D_CORE + ["LM"]),
        ("535i", "535i", "Sedan, touring", "2008-2010", "N54 3.0 L twin-turbo inline-6", "Rear-wheel drive or xDrive", D_CORE + ["LM"]),
        ("550i", "550i / 650i", "Sedan, coupe, convertible", "2006-2010", "N62 4.8 L V8", "Rear-wheel drive", D_CORE + ["LM"]),
        ("535d", "535d", "Sedan, touring", "2007-2010", "M57 3.0 L twin-turbo diesel inline-6", "Rear-wheel drive", D_CORE + ["LM"]),
        ("530i", "525i / 530i", "Sedan, touring", "2007-2010", "N52 inline-6", "Rear-wheel drive or xDrive", D_CORE + ["LM"]),
        ("520d", "520d", "Sedan, touring", "2007-2010", "N47 2.0 L diesel inline-4", "Rear-wheel drive", D_CORE + ["LM"]),
    ]),
    ("E70", "X5 E70 / X6 E71", "2007-2013", "DCan", ["E70", "E71", "E72"], [
        ("30i", "X5 3.0si / xDrive30i", "SUV", "2007-2010", "N52 3.0 L inline-6", "xDrive", D_CORE + ["FRM", "JBE", "VGSG"]),
        ("48i", "X5 4.8i", "SUV", "2007-2010", "N62 4.8 L V8", "xDrive", D_CORE + ["FRM", "JBE", "VGSG"]),
        ("35d", "X5 xDrive35d", "SUV", "2009-2013", "M57 3.0 L twin-turbo diesel inline-6", "xDrive", D_CORE + ["FRM", "JBE", "VGSG"]),
        ("x6-35i", "X6 xDrive35i", "Sports activity coupe", "2008-2010", "N54 3.0 L twin-turbo inline-6", "xDrive", D_CORE + ["FRM", "JBE", "VGSG"]),
        ("x6-50i", "X6 xDrive50i", "Sports activity coupe", "2008-2014", "N63 4.4 L twin-turbo V8", "xDrive", D_CORE + ["FRM", "JBE", "VGSG"]),
    ]),
    ("E89", "Z4 E89", "2009-2016", "DCan", ["E89"], [
        ("30i", "sDrive30i", "Roadster", "2009-2011", "N52 3.0 L inline-6", "Rear-wheel drive", D_CORE + ["FRM", "JBE", "CVM"]),
        ("35i", "sDrive35i / 35is", "Roadster", "2009-2016", "N54 3.0 L twin-turbo inline-6", "Rear-wheel drive", D_CORE + ["FRM", "JBE", "CVM"]),
        ("23i", "sDrive23i", "Roadster", "2009-2011", "N52 2.5 L inline-6", "Rear-wheel drive", D_CORE + ["FRM", "JBE", "CVM"]),
        ("28i", "sDrive28i", "Roadster", "2012-2016", "N20 2.0 L turbo inline-4", "Rear-wheel drive", D_CORE + ["FRM", "JBE", "CVM"]),
        ("18i", "sDrive18i / 20i", "Roadster", "2013-2016", "N20 2.0 L turbo inline-4", "Rear-wheel drive", D_CORE + ["FRM", "JBE", "CVM"]),
    ]),
    ("E39", "5 Series E39", "1997-2003", "KLine", ["E39"], [
        ("525i", "525i / 530i", "Sedan, touring", "2001-2003", "M54 inline-6", "Rear-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
        ("528i", "528i", "Sedan, touring", "1997-2000", "M52 2.8 L inline-6", "Rear-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
        ("540i", "540i", "Sedan, touring", "1997-2003", "M62 4.4 L V8", "Rear-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
        ("m5", "M5", "Sedan", "1998-2003", "S62 5.0 L V8", "Rear-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
        ("530d", "525d / 530d", "Sedan, touring", "1998-2003", "M57 diesel inline-6", "Rear-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
    ]),
    ("E53", "X5 E53", "2000-2006", "KLine", ["E53"], [
        ("30i", "X5 3.0i", "SUV", "2001-2006", "M54 3.0 L inline-6", "All-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
        ("44i", "X5 4.4i", "SUV", "2000-2006", "M62 4.4 L V8 (N62 from 2004)", "All-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
        ("46is", "X5 4.6is", "SUV", "2002-2003", "M62 4.6 L V8", "All-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
        ("48is", "X5 4.8is", "SUV", "2004-2006", "N62 4.8 L V8", "All-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
        ("30d", "X5 3.0d", "SUV", "2001-2006", "M57 3.0 L diesel inline-6", "All-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
    ]),
    ("E38", "7 Series E38", "1995-2001", "KLine", ["E38"], [
        ("740i", "740i / 740iL", "Sedan", "1995-2001", "M62 4.4 L V8", "Rear-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
        ("750il", "750iL", "Sedan", "1995-2001", "M73 5.4 L V12", "Rear-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
        ("728i", "728i", "Sedan", "1996-2001", "M52 2.8 L inline-6", "Rear-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
        ("735i", "735i", "Sedan", "1996-2001", "M62 3.5 L V8", "Rear-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
        ("730d", "730d", "Sedan", "1998-2001", "M57 3.0 L diesel inline-6", "Rear-wheel drive", ["DME", "EGS", "DSC", "MRS", "IKE", "GM", "LCM", "IHKA", "EWS"]),
    ]),
]

mod_doc["modules"] = [m for m in mod_doc["modules"] if m["id"].split(".")[0] in KEEP]
veh_doc["vehicles"] = [v for v in veh_doc["vehicles"] if v["id"] in KEEP]
for fid, name, years, cable, chassis, models in FAMILIES:
    mods = dcan_modules(fid, chassis) if cable == "DCan" else ds2_modules(fid, chassis)
    ids = {m["id"].split(".")[1] for m in mods}
    mod_doc["modules"] += mods
    veh_doc["vehicles"].append({
        "id": fid, "name": name, "years": years, "cable": cable, "verified": False,
        "modules": [m["id"] for m in mods],
        "models": [model(fid, mid, mname, body, yrs, eng, drv, [x for x in mm if x in ids]) for mid, mname, body, yrs, eng, drv, mm in models],
    })
    print(f"{fid:4} {cable:5} {len(mods):3} modules: {' '.join(sorted(ids))}")
json.dump(mod_doc, open(MODULES, "w"), indent=2, ensure_ascii=False)
open(MODULES, "a").write("\n")
json.dump(veh_doc, open(VEHICLES, "w"), indent=2, ensure_ascii=False)
open(VEHICLES, "a").write("\n")
