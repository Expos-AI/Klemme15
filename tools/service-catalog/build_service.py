#!/usr/bin/env python3
"""
Builds src/Klemme15.Data/db/service.json from the recipes below and the job definitions in bmw-unified-db
(bmw.sqlite, https://github.com/gediz/bmw-unified-db). Nothing from the database is copied except job names,
argument names (to check the recipes) and which module versions have each job. All descriptions are our own.

    python3 tools/service-catalog/build_service.py /path/to/bmw.sqlite

A recipe runs a BMW job with arguments we chose. It applies to every module version (variant) of the function's
module that has the job with exactly the argument list in `sig`, so a version whose job expects something else
never gets our arguments. `stop` jobs hand a part back to the module afterwards; a version without them is left out.
"""
import json, sqlite3, sys, pathlib, collections

ROOT = pathlib.Path(__file__).resolve().parents[2]
DB = sys.argv[1] if len(sys.argv) > 1 else "bmw.sqlite"
con = sqlite3.connect(DB)

modules = json.load(open(ROOT / "src/Klemme15.Data/db/modules.json"))["modules"]
def variants_of(short):
    out = []
    for m in modules:
        if m["id"].split(".")[1] == short:
            out += [v.lower() for v in m.get("variants", [])]
    return out

def job_args(variant, job):
    rows = con.execute("select j.name from job j where lower(j.sgbd)=? and j.name=?", (variant, job)).fetchall()
    if not rows:
        return None
    return [r[0] for r in con.execute("select name from job_arg where lower(sgbd)=? and job=? order by ord", (variant, job))]

def has_job(variant, job):
    return con.execute("select 1 from job where lower(sgbd)=? and name=?", (variant, job)).fetchone() is not None

def S(job, args="", wait=0, show=False, say=None):
    d = {"job": job, "args": args}
    if wait: d["wait"] = wait
    if show: d["show"] = True
    if say: d["say"] = say
    return d

def R(job, args="", sig=(), then=(), stop=(), wait=0, say=None, show=False, map=None, only=None, exclude=(), native=None, expect=None):
    return dict(job=job, args=args, sig=list(sig) if sig is not None else None, then=list(then), stop=list(stop), wait=wait,
                say=say, show=show, map=map, only=only, exclude=set(exclude), native=native, expect=expect)

def choice(id, label, choices):
    return {"id": id, "label": label, "type": "choice", "choices": [{"value": v, "label": l} for v, l in choices]}

def checks(ignition=True, engineOff=False, engineRunning=False, minVoltage=None, stationary=False, confirm=()):
    c = {"ignition": ignition}
    if engineOff: c["engineOff"] = True
    if engineRunning: c["engineRunning"] = True
    if minVoltage: c["minVoltage"] = minVoltage
    if stationary: c["stationary"] = True
    if confirm: c["confirm"] = list(confirm)
    return c

FUNCTIONS = []
def F(id, title, category, module, summary, when, risk, recipes, before=(), after=(), checks=None, inputs=(), duration=None):
    FUNCTIONS.append(dict(id=id, title=title, category=category, module=module, summary=summary, when=when, risk=risk,
                          before=list(before), after=list(after), checks=checks, inputs=list(inputs), duration=duration, recipes=recipes))

ENGINE_OFF = dict(ignition=True, engineOff=True)
NOTHING_NEAR = "Hands, tools and loose clothing are clear of moving parts."

# Small helper for component tests that need a stop job. `on` is the job, `off` hands control back.
def timed(job, args, sig, seconds, say, off=None, off_args=""):
    return R(job, args, sig, wait=seconds if off else 0, say=say, stop=[S(off, off_args)] if off else [])

# ============================================================================ Service and maintenance

SERVICE_ITEMS = [
    ("oil", "Engine oil service"), ("brakes-front", "Front brake pads"), ("brakes-rear", "Rear brake pads"),
    ("brake-fluid", "Brake fluid"), ("microfilter", "Cabin microfilter"), ("spark-plugs", "Spark plugs"),
    ("vehicle-check", "Vehicle check"), ("coolant", "Coolant"),
    ("inspection-distance", "Inspection by distance (older cars)"), ("inspection-time", "Inspection by time (older cars)"),
]
F("service-reset", "Reset a service reminder", "Service and maintenance", ["KOMBI", "IKE"],
  "Resets one service item in the instrument cluster (oil, brake pads, brake fluid, microfilter, spark plugs, vehicle check, coolant) after you did the work, like the cluster's own reset menu.",
  "After an oil change, new brake pads, a brake fluid change or another service item.",
  "low",
  [R("CBS_RESET", "{item};100;31",
     sig=["CBS_KENNUNG", "CBS_VERFUEGBARKEIT", "CBS_ANZAHL_SERVICE", "CBS_ZIEL_MONAT", "CBS_ZIEL_JAHR", "RMM_CBS_WERT", "ST_UN_CBS_RSTG",
          "FRC_INTM_WAY_CBS_MESS", "FRC_INTM_T_CBS_MESS", "Res_Byte"],
     show=True, then=[S("CBS_DATEN_LESEN", show=True)],
     map={"item": {"oil": "Oel", "brakes-front": "Br_v", "brakes-rear": "Br_h", "brake-fluid": "Brfl", "microfilter": "Filt",
                   "spark-plugs": "ZKrz", "vehicle-check": "Sic", "coolant": "Kfl"}}),
   R("SIA_RESET", "{item}", sig=["ARG1"],
     map={"item": {"oil": "Oel_Reset", "inspection-distance": "Weg_Reset", "inspection-time": "Zeit_Reset"}})],
  before=["Do the work first. For brake pads, also fit the new wear sensor: with the old, worn sensor the reminder comes straight back."],
  after=["Check the cluster: the item should show its full interval again."],
  checks=checks(**ENGINE_OFF),
  inputs=[choice("item", "Service item", SERVICE_ITEMS)])

F("battery-registration", "Register a new battery", "Service and maintenance", ["DME"],
  "Tells the engine computer a new battery was fitted, so it stops charging as if the old, worn battery were still there.",
  "After fitting a new battery of the same type and capacity as the old one.",
  "low",
  [R("STEUERN_BATTERIETAUSCH_REGISTRIEREN", native="3130001000", expect="7130",
     only=["msv70", "msv80", "msd80", "mss60", "me17n45", "me9n45", "mev9n46l", "mevd174k", "mevd17kw", "mevd176k", "d60m47a0", "d60m57a0",
           "d60m57d0", "d62m57a0", "d62m57b0", "d70n47a0", "d70n47b0", "d71n47a0", "d71n47c0", "d72n47b0", "d73n57b0", "d73n57c0", "d73n57d0",
           "ms450ds0", "mev9n46"]),
   R("STEUERN_BATTERIETAUSCH_REGISTRIEREN")],
  before=["The new battery must match the old one's type (AGM or standard) and capacity. A different type or capacity also needs the car's coding changed, which Klemme15 doesn't do."],
  after=["Nothing else to do: charging adapts over the next drives.",
         "Clear the fault memories if fitting the battery left low-voltage faults behind."],
  checks=checks(engineOff=True, minVoltage=12.0, confirm=["The new battery is the same type and capacity as the old one."]))

F("dpf-service", "Diesel particulate filter: after service work", "Service and maintenance", ["DME"],
  "Tells the diesel engine computer what was done to the particulate filter system, so its soot and oil-dilution calculations start from the right values.",
  "After replacing the particulate filter or its pressure sensor, after an oil change on a car that complained about oil dilution, or to clear a power reduction caused by the filter.",
  "medium",
  [R("ABGLEICH_CSF_PROG", "{case}", sig=["IDENTIFIER_SERVICE", "IDENTIFIER_BANK"],
     map={"case": {"new-filter": "FILTERTAUSCH", "new-sensor": "DRUCKSENSOR", "oil-change": "OELWECHSEL",
                   "oil-dilution": "OELVERDUENNUNG", "power-loss": "LEISTUNGSMANGEL"}})],
  before=["Only report work that was really done: telling the engine computer the filter is new when it isn't hides a blocked filter."],
  after=["Drive at a steady speed on a main road for 20 minutes so the filter can regenerate if it needs to."],
  checks=checks(**ENGINE_OFF),
  inputs=[choice("case", "What was done", [("new-filter", "Particulate filter replaced"), ("new-sensor", "Differential pressure sensor replaced"),
                                           ("oil-change", "Oil changed"), ("oil-dilution", "Reset the oil dilution counter"),
                                           ("power-loss", "Clear a filter-related power reduction")])])

# ============================================================================ Engine

F("engine-adaptation-reset", "Reset engine adaptations", "Engine", ["DME"],
  "Clears what the engine computer has learned (fuel trims, idle, throttle, knock, VANOS and more), so it relearns from scratch.",
  "After replacing a sensor, injector, throttle body or intake part, or after fixing a vacuum leak, so old corrections don't linger.",
  "medium",
  # Byte 2 keeps bit 4 (battery registration) and bit 6 (learned variants); byte 3 keeps bit 3 (the factory
  # cylinder balancing from the end of the production line, which doesn't relearn in a workshop).
  [R("ADAP_SELEKTIV_LOESCHEN", "255;175;247", sig=["AUSWAHLBYTE_1", "AUSWAHLBYTE_2", "AUSWAHLBYTE_3"],
     exclude=[v for v in variants_of("DME") if v.startswith("d")]),
   R("ADAPT_LOESCHEN")],
  before=["Fix the cause first: resetting adaptations doesn't repair anything, it only makes the engine relearn."],
  after=["Start the engine and let it idle for 5 minutes without touching anything, with the air conditioning off.",
         "Then drive 20 to 30 minutes with a mix of steady speeds and gentle acceleration. Idle and response can feel slightly off until it has relearned.",
         "The battery registration, the detected equipment and the factory cylinder balancing are kept."],
  checks=checks(engineOff=True, minVoltage=12.0))

F("valvetronic-learn", "Learn the Valvetronic end stops", "Engine", ["DME"],
  "Lets the engine computer move the Valvetronic eccentric shaft to its end stops and store them.",
  "After replacing the Valvetronic motor, its sensor or the engine computer, or with Valvetronic faults after repairs.",
  "medium",
  [R("START_SYSTEMCHECK_VVT_ANSCHLAG", "6", sig=["PARAMETER"], wait=30, say="Learning the end stops", stop=[S("STOP_SYSTEMCHECK_VVT_ANSCHLAG")]),
   R("START_SYSTEMCHECK_VVT_ANSCHLAG", "6", sig=["PARAMETER_WERT"], wait=30, say="Learning the end stops", stop=[S("STOP_SYSTEMCHECK_VVT_ANSCHLAG")]),
   R("START_SYSTEMCHECK_VVT_ANSCHLAG", wait=30, say="Learning the end stops", stop=[S("STOP_SYSTEMCHECK_VVT_ANSCHLAG")]),
   R("STEUERN_VVT_ANSCHLAG", wait=30, say="Learning the end stops", stop=[S("STOP_VVT_ANSCHLAG")]),
   R("STEUERN_VVT_ANSCHLAG", wait=30, say="Learning the end stops", stop=[S("ENDE_VVT_ANSCHLAG")])],
  before=["Engine off, ignition on. You may hear the motor at the front of the cylinder head buzz."],
  after=["Clear the engine fault memory, start the engine and check it idles smoothly."],
  checks=checks(engineOff=True, minVoltage=12.5),
  duration="About 30 seconds")

F("disa-learn", "Learn the intake (DISA) flap end stops", "Engine", ["DME"],
  "Lets the engine computer move the variable intake flap to its end stops and store them.",
  "After replacing the DISA actuator or the intake manifold.",
  "low",
  [R("STEUERN_DISA_ANSCHLAG", wait=10, say="Learning the end stops", stop=[S("STOP_DISA_ANSCHLAG")])],
  checks=checks(**ENGINE_OFF))

CYL = [(str(n), f"Cylinder {n}") for n in range(1, 9)]
def cyl_numbers(count): return {str(n): str(n) for n in range(1, count + 1)}
def cyl_bits(count): return {str(n): str(1 << (n - 1)) for n in range(1, count + 1)}
F("cylinder-cutout", "Cylinder cut-out test (find a misfire)", "Engine", ["DME"],
  "Switches off one cylinder's injector for 10 seconds while the engine idles. If the engine barely changes, that cylinder wasn't doing its share.",
  "Rough idle or a misfire that doesn't point clearly at one cylinder. Test each cylinder in turn and compare.",
  "low",
  # Six-cylinder engine computers take the cylinder number; the four-cylinder ones a bit mask.
  [R("START_SYSTEMCHECK_EVAUSBL", "{cyl}", sig=["VENTIL_NR"], wait=10, say="Cylinder switched off", stop=[S("STOP_SYSTEMCHECK_EVAUSBL")], map={"cyl": cyl_numbers(6)}),
   R("START_SYSTEMCHECK_EVAUSBL", "{cyl}", sig=["VENTIL_NR_WERT"], wait=10, say="Cylinder switched off", stop=[S("STOP_SYSTEMCHECK_EVAUSBL")], map={"cyl": cyl_numbers(6)}),
   R("START_SYSTEMCHECK_EVAUSBL", "{cyl}", sig=["EV_AUS_WERT"], wait=10, say="Cylinder switched off", stop=[S("STOP_SYSTEMCHECK_EVAUSBL")], map={"cyl": cyl_bits(4)}),
   R("START_SYSTEMCHECK_EVAUSBL", "{cyl}", sig=["DEVOFF"], wait=10, say="Cylinder switched off", stop=[S("STOP_SYSTEMCHECK_EVAUSBL")], map={"cyl": cyl_bits(4)}),
   R("STEUERN_EVAUSBL", "{cyl}", sig=["VENTIL_NR"], wait=10, say="Cylinder switched off", stop=[S("STEUERN_EVAUSBL_AUS", "0")], map={"cyl": cyl_bits(4)})],
  before=["Warm engine, idling, in P or neutral with the handbrake on. Listen and watch the rev counter during the 10 seconds."],
  after=["A healthy cylinder makes the idle drop and shake noticeably. A cylinder that changes little is the weak one: check its coil, plug and injector."],
  checks=checks(engineRunning=True, stationary=True, confirm=["The car is in P or neutral with the handbrake on."]),
  inputs=[choice("cyl", "Cylinder", CYL)],
  duration="10 seconds")

F("evap-leak-test", "Fuel tank leak test", "Engine", ["DME"],
  "Starts the fuel tank leak test: the leak-detection pump pressurises the tank system and the engine computer measures how fast it leaks.",
  "Evaporative system leak faults, or after replacing the fuel cap, purge valve or leak-detection pump.",
  "low",
  [R("START_SYSTEMCHECK_DMTL", say="Starting the test", then=[S("STATUS_SYSTEMCHECK_DMTL", wait=0, show=True)])],
  before=["Fuel tank between a quarter and three quarters full, fuel cap closed. Engine off, ignition on."],
  after=["The test runs on its own for several minutes. Leave the ignition on, then read the engine fault memory: a leak shows as a fault."],
  checks=checks(engineOff=True, minVoltage=12.0, confirm=["The fuel cap is closed and the tank is between a quarter and three quarters full."]),
  duration="Several minutes (runs on its own)")

F("secondary-air-check", "Secondary air system check", "Engine", ["DME"],
  "Runs the engine computer's own check of the secondary air system (pump, valve and hoses) and shows the result.",
  "Secondary air faults, or after replacing the pump, valve or hoses.",
  "low",
  [R("START_SYSTEMCHECK_SEK_LUFT", wait=30, say="Checking the secondary air system", show=True,
     then=[S("STATUS_SYSTEMCHECK_SEK_LUFT", show=True)], stop=[S("STOP_SYSTEMCHECK_SEK_LUFT")]),
   R("START_SYSTEMCHECK_SEK_LUFT", wait=30, say="Checking the secondary air system", show=True, stop=[S("STOP_SYSTEMCHECK_SEK_LUFT")])],
  before=["Warm engine (above 75 °C), idling. The pump is loud while the check runs.",
          "Stored secondary air, air mass meter or misfire faults stop the check from running: fix and clear those first."],
  checks=checks(engineRunning=True, stationary=True, confirm=["The engine is at full operating temperature."]),
  duration="About 30 seconds")

F("oxygen-sensor-swap-check", "Check the oxygen sensor plugs aren't swapped", "Engine", ["DME"],
  "Runs the engine computer's check that the bank 1 and bank 2 oxygen sensors are plugged into the right connectors.",
  "After replacing oxygen sensors on a six-cylinder with two catalysts, or with mixture faults that appeared right after such work.",
  "low",
  [R("START_SYSTEMCHECK_L_SONDE", wait=60, say="Checking the oxygen sensors", then=[S("STATUS_SYSTEMCHECK_L_SONDE", show=True)],
     stop=[S("STOP_SYSTEMCHECK_L_SONDE")], exclude=["mss60"]),
   R("START_SYSTEMCHECK_L_SONDE", wait=60, say="Checking the oxygen sensors", stop=[S("STOP_SYSTEMCHECK_L_SONDE")], exclude=["mss60"])],
  before=["Warm engine, idling."],
  after=["If the result says swapped (or a fault appears), swap the two front sensor plugs and run the check again."],
  checks=checks(engineRunning=True, stationary=True, confirm=["The engine is at full operating temperature."]),
  duration="About a minute")

F("catalyst-test", "Catalytic converter quick test", "Engine", ["DME"],
  "Runs the engine computer's short catalyst efficiency test and shows the result.",
  "Catalyst efficiency faults, or after replacing a catalytic converter.",
  "low",
  [R("START_SYSTEMCHECK_DKAT", wait=60, say="Testing the catalysts", then=[S("STATUS_SYSTEMCHECK_DKAT", show=True)], stop=[S("STOP_SYSTEMCHECK_DKAT")]),
   R("STEUERN_DKAT", wait=60, say="Testing the catalysts")],
  before=["Warm engine (drive 15 minutes first), idling."],
  checks=checks(engineRunning=True, stationary=True, confirm=["The engine and exhaust are fully warm."]),
  duration="About a minute")

F("vanos-flush", "Flush the VANOS units", "Engine", ["DME"],
  "Moves the intake and exhaust camshaft adjusters back and forth repeatedly at idle, to flush dirt out of the VANOS solenoids and units.",
  "VANOS faults after an oil change with old, dirty oil, or after cleaning or replacing the VANOS solenoids.",
  "low",
  [R("START_VANOSSPUELEN", "1;10;2.0;1000;1200;0;5;5.6;-5.6;-5.6;5.6",
     sig=["VANOSSPL_M_MODUS", "VANOSSPL_N_AZLVERSTL", "VANOSSPL_T_HLTZVERSTL", "VANOSSPL_N1_UDZLGRNZ", "VANOSSPL_N2_ODZLGRNZ", "VANOSSPL_V_MAX",
          "VANOSSPL_T2_ZUBRZ", "VANOSSPL_DVSE1_VO1EV", "VANOSSPL_DVSE2_VO2EV", "VANOSSPL_DVSA1_VO1AV", "VANOSSPL_DVSA2_VO1AV"],
     wait=45, say="Flushing the VANOS units", stop=[S("STOP_VANOSSPUELEN")])],
  before=["Warm engine, idling, in P or neutral. The idle may sound uneven while it runs."],
  checks=checks(engineRunning=True, stationary=True),
  duration="About 45 seconds")

# ---------------------------------------------------------------------------- component tests (engine)
CT = "Component tests"
F("test-radiator-fan", "Test the radiator fan", CT, ["DME"],
  "Runs the electric radiator fan at half speed for 10 seconds.",
  "When you suspect the fan or its wiring (overheating at idle, fan faults).",
  "low",
  [R("STEUERN_E_LUEFTER", "50;10", sig=["SW_TV_ELUE_WERT", "SW_TO_ELUE_WERT"], say="Fan running"),
   R("STEUERN_E_LUEFTER", "50;10", sig=["SW_TV_ELUE", "SW_TO_ELUE"], say="Fan running"),
   R("STEUERN_E_LUEFTER", "50;10", sig=["TV_ELUE", "TO_ELUE"], say="Fan running"),
   timed("STEUERN_E_LUEFTER", "50", ["PWM"], 10, "Fan running", "STEUERN_E_LUEFTER_ENDE"),
   timed("STEUERN_E_LUEFTER", "50", ["TASTRATE"], 10, "Fan running", "STEUERN_E_LUEFTER_AUS"),
   timed("STEUERN_E_LUEFTER", "50", ["TASTVERHAELTNIS"], 10, "Fan running", "STEUERN_E_LUEFTER_AUS"),
   R("STEUERN_E_LUEFTER", "50", sig=["TASTVERHAELTNIS"], wait=10, say="Fan running", stop=[S("STEUERN_E_LUEFTER", "0")],
     only=["ms420ds0", "ms430ds0"]),
   R("STEUERN_E_LUEFTER", "128", sig=["TASTVERHAELTNIS"], wait=10, say="Fan running", stop=[S("STEUERN_E_LUEFTER", "0")],
     only=["bms46ds0", "bms46ds1"])],
  before=["Keep hands, tools and clothing away from the fan."],
  after=["If the fan didn't run, check its fuse, connector and wiring before replacing it."],
  checks=checks(engineOff=True, confirm=[NOTHING_NEAR]), duration="10 seconds")

F("test-fuel-pump", "Test the fuel pump", CT, ["DME"],
  "Switches the in-tank fuel pump on for 10 seconds. Listen under the rear seat or at the filler.",
  "Long cranking, no start, or low fuel pressure faults.",
  "low",
  [R("STEUERN_EKP", "99;10", sig=["SW_TV_EKP1_WERT", "SW_TO_EKP1_WERT"], only=["msv80", "msd80"], say="Pump running"),
   R("STEUERN_EKP", "1;10", sig=["SW_TV_EKP1_WERT", "SW_TO_EKP1_WERT"], only=["msv70", "me17n45"], say="Pump running"),
   R("STEUERN_EKP", "1;10", sig=["SW_TV_EKP1", "SW_TO_EKP1"], say="Pump running"),
   timed("STEUERN_EKP", "", [], 10, "Pump running", "STEUERN_EKP_AUS"),
   timed("STEUERN_EKP", "1", ["ON"], 10, "Pump running", "STEUERN_EKP_ENDE"),
   R("STEUERN_EKP", "255", sig=["PUMPE_EIN_AUS"], wait=10, say="Pump running", stop=[S("STEUERN_EKP", "0")]),
   R("STEUERN_EKP", "255", sig=["TASTVERHAELTNIS"], wait=10, say="Pump running", stop=[S("STEUERN_EKP", "0")])],
  before=["Engine off. Nobody working on the fuel lines."],
  after=["No sound usually means the pump, its fuse or relay, or (on cars with one) the fuel pump control module."],
  checks=checks(engineOff=True, confirm=["Nobody is working on the fuel lines."]), duration="10 seconds")

F("test-thermostat", "Test the map thermostat heater", CT, ["DME"],
  "Powers the electric heater in the map-controlled thermostat for 30 seconds.",
  "Thermostat faults, or a coolant temperature that never settles where it should.",
  "low",
  [R("STEUERN_KFT", "99;30", sig=["SW_TV_KFT_WERT", "SW_TO_KFT_WERT"], say="Thermostat heater on"),
   R("STEUERN_KFT", "1;30", sig=["SW_TV_KFT", "SW_TO_KFT"], say="Thermostat heater on"),
   timed("STEUERN_KFK", "", [], 30, "Thermostat heater on", "STEUERN_KFK_AUS"),
   timed("STEUERN_KFK", "99", ["PWM"], 30, "Thermostat heater on", "STEUERN_KFK_ENDE"),
   R("STEUERN_KF_THERMOSTAT", "100", sig=["TASTVERHAELTNIS"], wait=30, say="Thermostat heater on", stop=[S("STEUERN_KF_THERMOSTAT", "0")],
     only=["ms420ds0", "ms430ds0"]),
   R("STEUERN_KF_THERMOSTAT", "255", sig=["TASTVERHAELTNIS"], wait=30, say="Thermostat heater on", stop=[S("STEUERN_KF_THERMOSTAT", "0")],
     only=["bms46ds0", "bms46ds1"])],
  after=["If the module reports the thermostat circuit faulty, check the two-pin connector on the thermostat before replacing it."],
  checks=checks(**ENGINE_OFF), duration="30 seconds")

F("test-water-pump", "Test the electric water pump", CT, ["DME"],
  "Runs the electric coolant pump at half speed for 20 seconds. You may hear it and feel the coolant hoses pulse.",
  "Water pump faults or overheating warnings.",
  "low",
  [R("STEUERN_EWAP", "50;20", sig=["SW_TV_EWAP_WERT", "SW_TO_EWAP_WERT"], say="Pump running"),
   R("STEUERN_EWAP", "128;20", sig=["SW_TV_EWAP", "SW_TO_EWAP"], say="Pump running")],
  before=["The cooling system must be full: never run the pump dry."],
  after=["If it doesn't run, check its connector and power supply first."],
  checks=checks(engineOff=True, confirm=["The cooling system is full."]), duration="20 seconds")

F("test-secondary-air-pump", "Test the secondary air pump", CT, ["DME"],
  "Runs the secondary air pump for 10 seconds. It is loud by design.",
  "Secondary air faults.",
  "low",
  [R("STEUERN_SLP", "1;10", sig=["SW_TV_SLP_WERT", "SW_TO_SLP_WERT"], say="Pump running"),
   timed("STEUERN_SLP", "", [], 10, "Pump running", "STEUERN_SLP_AUS"),
   timed("STEUERN_SLP", "1", ["ON"], 10, "Pump running", "STEUERN_SLP_ENDE"),
   R("STEUERN_SLP", "255", sig=["TASTVERHAELTNIS"], wait=10, say="Pump running", stop=[S("STEUERN_SLP", "0")]),
   R("STEUERN_SEK_PUMPE", "255", sig=["PUMPE_EIN_AUS"], wait=10, say="Pump running", stop=[S("STEUERN_SEK_PUMPE", "0")]),
   R("STEUERN_SEK_PUMPE", "255", sig=["TASTVERHAELTNIS"], wait=10, say="Pump running", stop=[S("STEUERN_SEK_PUMPE", "0")])],
  after=["If it runs but the fault remains, check the hoses and the check valve for blockage."],
  checks=checks(**ENGINE_OFF), duration="10 seconds")

F("test-throttle", "Test the throttle valve", CT, ["DME"],
  "Opens the electronic throttle to half for 5 seconds, so you can watch or hear it move.",
  "Throttle faults, or after cleaning the throttle body.",
  "low",
  [R("STEUERN_DK", "50;5", sig=["SW_TV_DK_WERT", "SW_TO_DK_WERT"], say="Throttle open"),
   R("STEUERN_DK", "50;5", sig=["SW_TV_DK", "SW_TO_DK"], say="Throttle open")],
  before=["Keep fingers out of the throttle body."],
  checks=checks(engineOff=True, confirm=["Nothing is inside the throttle body."]), duration="5 seconds")

F("test-disa", "Test the intake (DISA) flap", CT, ["DME"],
  "Switches the variable intake flap for 5 seconds. Listen for a click or clunk at the intake manifold.",
  "DISA faults, a flat spot in the mid-range, or a rattle from the intake.",
  "low",
  [R("STEUERN_DISA", "1;5", sig=["SW_TV_DISA_WERT", "SW_TO_DISA_WERT"], say="Flap switched"),
   timed("STEUERN_DISA", "100", ["WINKEL"], 5, "Flap switched", "STEUERN_DISA_AUS"),
   R("STEUERN_DISA", "255", sig=["DISA_EIN_AUS"], wait=5, say="Flap switched", stop=[S("STEUERN_DISA", "0")]),
   R("STEUERN_DISA", "255", sig=["TASTVERHAELTNIS"], wait=5, say="Flap switched", stop=[S("STEUERN_DISA", "0")]),
   timed("STEUERN_VIMDISA", "1", ["ON"], 5, "Flap switched", "STEUERN_VIMDISA_ENDE")],
  checks=checks(**ENGINE_OFF), duration="5 seconds")

F("test-purge-valve", "Test the purge valve", CT, ["DME"],
  "Opens the fuel tank purge valve (the one between the charcoal canister and the intake) for 10 seconds. You should hear it click.",
  "Purge or evaporative system faults, a rough idle right after refuelling, or a hard start after refuelling.",
  "low",
  [R("STEUERN_TEV", "50;10", sig=["SW_TV_TEV_WERT", "SW_TO_TEV_WERT"], exclude=["me17n45"], say="Valve clicking"),
   R("STEUERN_TEV", "1;10", sig=["SW_TV_TEV_WERT", "SW_TO_TEV_WERT"], only=["me17n45"], say="Valve clicking"),
   R("STEUERN_TEV", "50;10", sig=["SW_TV_TEV", "SW_TO_TEV"], say="Valve clicking"),
   R("STEUERN_TEV", "50;10", sig=["TV_TEV", "TO_TEV"], say="Valve clicking"),
   timed("STEUERN_TEV", "50", ["ANSTEUERRATE"], 10, "Valve clicking", "STEUERN_TEV_AUS"),
   timed("STEUERN_TEV", "50", ["PWM"], 10, "Valve clicking", "STEUERN_TEV_ENDE"),
   R("STEUERN_TEV", "50", sig=["TASTVERHAELTNIS"], wait=10, say="Valve clicking", stop=[S("STEUERN_TEV", "0")], only=["ms420ds0", "ms430ds0"]),
   R("STEUERN_TEV", "255", sig=["TASTVERHAELTNIS"], wait=10, say="Valve clicking", stop=[S("STEUERN_TEV", "0")], only=["bms46ds0", "bms46ds1"])],
  after=["No click: check the valve's connector and power. A valve that clicks but leaks shows up in the fuel tank leak test."],
  checks=checks(**ENGINE_OFF), duration="10 seconds")

F("test-fuel-pump-module", "Check the fuel pump control module", CT, ["EKPS"],
  "Asks the fuel pump control module whether it can see the fuel pump connected.",
  "No start, fuel pump faults, or after replacing the pump or the control module.",
  "low",
  [R("STEUERN_TEST_PUMPE_VORHANDEN", show=True)],
  checks=checks(**ENGINE_OFF))

# ============================================================================ Diesel
# Four-cylinder diesels (M47, N47); the others in the list are six-cylinder (M57, N57).
FOUR_CYL_DIESEL = [v for v in variants_of("DME") if v.startswith("d") and "47" in v]
def glow(count): return {"GLUALLE": "GLUALLE", **{f"GLU{n}": f"GLU{n}" for n in range(1, count + 1)}}

F("glow-plug-reset", "Reset the glow plug values", "Diesel", ["DME"],
  "Tells the glow plug control unit that glow plugs were replaced, so it stops using the learned values of the old ones.",
  "After replacing one or all glow plugs.",
  "low",
  [R("STEUERN_GLUEHKERZENTAUSCH", "{plug}", sig=["IDENTIFIER_GLU"], map={"plug": glow(4)}, only=FOUR_CYL_DIESEL),
   R("STEUERN_GLUEHKERZENTAUSCH", "{plug}", sig=["IDENTIFIER_GLU"], map={"plug": glow(6)}),
   R("STEUERN_GLUEHSTEUERGERAET", "GLU_RES;{plug}", sig=["IDENTIFIER_SERVICE", "IDENTIFIER_GLU"], map={"plug": glow(4)}, only=FOUR_CYL_DIESEL),
   R("STEUERN_GLUEHSTEUERGERAET", "GLU_RES;{plug}", sig=["IDENTIFIER_SERVICE", "IDENTIFIER_GLU"], map={"plug": glow(6)})],
  checks=checks(**ENGINE_OFF),
  inputs=[choice("plug", "Which glow plug", [("GLUALLE", "All glow plugs")] + [(f"GLU{n}", f"Glow plug {n}") for n in range(1, 7)])])

F("egr-swirl-learn", "Learn the EGR valve or swirl flap positions", "Diesel", ["DME"],
  "Lets the diesel engine computer learn the closed position of the EGR valve or the swirl flaps.",
  "After replacing or cleaning the EGR valve or the swirl flap actuator.",
  "low",
  [R("STEUERN_OFFSETLERNEN", "{part};1",
     sig=["STELLER", "AUTOMATISCHES_LERNEN", "OFFSETWERT11", "OFFSETWERT12", "OFFSETWERT21", "OFFSETWERT22", "STARTZAEHLER"],
     map={"part": {"egr": "AGRO", "swirl": "DRAO", "lp-egr": "NAGRO"}})],
  after=["Switch the ignition off for at least a minute, then start the engine: the learning finishes on its own."],
  checks=checks(**ENGINE_OFF),
  inputs=[choice("part", "Part", [("egr", "EGR valve"), ("swirl", "Swirl flaps"), ("lp-egr", "Low-pressure EGR valve")])])

F("diesel-adaptation-reset", "Reset diesel learned values", "Diesel", ["DME"],
  "Resets one group of learned values in the diesel engine computer so it relearns with the new part.",
  "After replacing the air mass meter, the oxygen sensor or all injectors.",
  "medium",
  [R("LERNWERTE_RUECKSETZEN", "{what}", sig=["IDENTIFIER_ABGLEICH", "LERNWERT_RUECKSETZEN_WERT"],
     only=["d70n47a0", "d70n47b0", "d71n47c0", "d72n47b0", "d73n57b0", "d73n57c0", "d73n57d0"],
     map={"what": {"maf": "MMAR", "o2-sensor": "MMALSUR", "injectors": "NMKINJTAU"}}),
   R("LERNWERTE_RUECKSETZEN", "{what}", sig=["IDENTIFIER_ABGLEICH", "LERNWERT_RUECKSETZEN_WERT"],
     exclude=["d71n47a0"], map={"what": {"maf": "MMAR", "o2-sensor": "LSUADAP", "injectors": "NMKINJTAU"}}),
   R("LERNWERTE_RUECKSETZEN", "{what}", sig=["IDENTIFIER_ABGLEICH", "LERNWERT_RUECKSETZEN_WERT"],
     map={"what": {"maf": "MMAR", "injectors": "NMKINJTAU"}})],
  after=["Drive normally: the engine computer relearns over the next drives."],
  checks=checks(**ENGINE_OFF),
  inputs=[choice("what", "What was replaced", [("maf", "Air mass meter"), ("o2-sensor", "Oxygen sensor (also resets the air mass adaptation on newer engines)"), ("injectors", "All injectors")])])

F("injector-codes-read", "Read the injector codes", "Diesel", ["DME"],
  "Shows the injector correction codes stored in the diesel engine computer, in the format printed on the injectors.",
  "Before or after replacing an injector, to compare with the codes on the injectors.",
  "low",
  [R("ABGLEICH_IMA_LESEN", show=True)],
  checks=checks(**ENGINE_OFF))

F("injector-code-program", "Program a new injector's code", "Diesel", ["DME"],
  "Stores the correction code printed on a new injector, so the engine computer doses that injector correctly.",
  "After replacing a diesel injector.",
  "high",
  [R("ABGLEICH_PROGRAMMIEREN_IMA_ZYL", "IMA{cyl};{code}", sig=["ABGLEICH_VERSTELLEN_IMA_IDENT", "ABGLEICH_VERSTELLEN_IMA_ZYL"],
     then=[S("ABGLEICH_IMA_LESEN", show=True)], map={"cyl": cyl_numbers(4)}, only=FOUR_CYL_DIESEL),
   R("ABGLEICH_PROGRAMMIEREN_IMA_ZYL", "IMA{cyl};{code}", sig=["ABGLEICH_VERSTELLEN_IMA_IDENT", "ABGLEICH_VERSTELLEN_IMA_ZYL"],
     then=[S("ABGLEICH_IMA_LESEN", show=True)], map={"cyl": cyl_numbers(6)})],
  before=["Copy the code exactly as printed on the new injector. A wrong code makes that cylinder run rich or lean."],
  after=["Check that the code shown for that cylinder matches the injector.",
         "Then run 'Reset diesel learned values' for the injectors if you replaced all of them."],
  checks=checks(engineOff=True, confirm=["The code was copied from the injector fitted to that cylinder."]),
  inputs=[choice("cyl", "Cylinder", [(str(n), f"Cylinder {n}") for n in range(1, 7)]),
          {"id": "code", "label": "Code printed on the injector", "type": "text", "pattern": "^[0-9A-Za-z]{4,16}$",
           "hint": "letters and numbers only, as printed on the injector"}])

F("diesel-cylinder-balance", "Cylinder balance test (diesel)", "Diesel", ["DME"],
  "Runs the diesel engine computer's smooth-running measurement at idle and shows the result.",
  "Rough idle or shaking at idle, to see whether one injector is out of line.",
  "low",
  # BMW: the stop job is mandatory once the measurement was started, where the version has one.
  [R("START_SYSTEMCHECK_ZYL", "LLR_AUS", sig=["SWITCH_MENGEN_DREHZAHL"], wait=30, say="Measuring",
     then=[S("STATUS_SYSTEMCHECK_ZYL", show=True)], stop=[S("STOP_SYSTEMCHECK_ZYL")]),
   R("START_SYSTEMCHECK_ZYL", "LLR_AUS", sig=["SWITCH_MENGEN_DREHZAHL"], wait=30, say="Measuring", stop=[S("STOP_SYSTEMCHECK_ZYL")]),
   R("START_SYSTEMCHECK_ZYL", "LLR_AUS", sig=["SWITCH_MENGEN_DREHZAHL"], wait=30, say="Measuring")],
  before=["Warm engine, idling, all consumers (air conditioning, lights, heated seats) off."],
  after=["Then open Readings for the engine: the per-cylinder quantity corrections show which injector is out of line.",
         "Switch the ignition off for a minute before driving, so the engine's smooth-running control is back on."],
  checks=checks(engineRunning=True, stationary=True), duration="About 30 seconds")

F("intercooler-reset", "Reset the intercooler efficiency", "Diesel", ["DME"],
  "Resets the learned intercooler efficiency in the diesel engine computer.",
  "After cleaning or replacing the intercooler or its hoses.",
  "low",
  [R("STEUERN_LLKETA_RESET")],
  checks=checks(**ENGINE_OFF))

F("coolant-bleed", "Bleed the cooling system", "Diesel", ["DME"],
  "Starts the engine computer's bleeding routine, which runs the electric water pump and opens the valves to push air out of the cooling system.",
  "After draining or replacing coolant, the water pump, thermostat or radiator.",
  "low",
  [R("STEUERN_ENTLUEFTUNG_KUEHLKREIS")],
  before=["Fill the expansion tank to the MAX mark. Switch the heater to maximum temperature and the fan to low."],
  after=["The routine runs for about 12 minutes on its own. Keep the ignition on, then top up the expansion tank and repeat if you still hear gurgling."],
  checks=checks(engineOff=True, minVoltage=12.0, confirm=["The expansion tank is filled to MAX.", "A battery charger is connected (the routine takes about 12 minutes)."]),
  duration="About 12 minutes (runs on its own)")

F("neutral-sensor-learn", "Learn the gearbox neutral sensor", "Engine", ["DME"],
  "Teaches the engine computer the neutral position of a manual gearbox, used by Auto Start-Stop.",
  "After replacing the neutral position sensor, the gearbox or the engine computer on manual cars with Auto Start-Stop.",
  "low",
  [R("STEUERN_NULLGANG_LERNEN")],
  before=["Manual gearbox in neutral, clutch pedal released, engine idling."],
  checks=checks(engineRunning=True, stationary=True, confirm=["The gearbox is in neutral and the clutch pedal is released."]))

# ============================================================================ Brakes, DSC and steering
DSC = "Brakes, DSC and steering"
WHEELS_STRAIGHT = "The wheels point straight ahead and the steering wheel is centred."
F("steering-angle-calibration", "Calibrate the steering angle sensor", DSC, ["DSC"],
  "Teaches the stability control module where 'straight ahead' is.",
  "After a wheel alignment, steering repairs, or when the steering angle reads off-centre with the wheels straight.",
  "high",
  [R("LENKWINKEL_DSC_ABGLEICHEN", sig=["LWS_OFFSET_KORREKTUR_GRAD"], then=[S("STAT_ERGEBNIS_ABGLEICH_ROUTINE", "LWS;R", show=True)]),
   R("LENKWINKEL_DSC_ABGLEICHEN", show=True),
   R("ABGLEICH_DSC_SENSOREN", show=True)],
  before=["Park on level ground with the wheels pointing exactly straight ahead and the steering wheel centred. Don't touch the wheel during the calibration."],
  after=["Check on the Readings page that the steering angle now reads close to zero with the wheels straight.",
         "Clear the DSC fault memory, then test drive carefully: the DSC light should stay off and the car should not brake on its own."],
  checks=checks(stationary=True, confirm=[WHEELS_STRAIGHT, "The car is on level ground and nobody will touch the steering wheel."]))

F("dsc-pressure-sensor-zero", "Zero the brake pressure sensor", DSC, ["DSC"],
  "Teaches the stability control module the brake pressure sensor's reading with no pressure applied.",
  "After replacing the DSC unit or its pressure sensor, or with pressure sensor faults.",
  "high",
  [R("DRUCKSENSOR_DSC_ABGLEICHEN", "E", sig=["E_OR_W"]), R("DRUCKSENSOR_DSC_ABGLEICHEN")],
  before=["Engine off. Don't touch the brake pedal during the calibration."],
  after=["Clear the DSC fault memory and check that the brake pressure reads 0 bar on the Readings page with the pedal released."],
  checks=checks(engineOff=True, stationary=True, confirm=["Nobody is touching the brake pedal."]))

F("dsc-lateral-sensor-zero", "Zero the lateral acceleration sensor", DSC, ["DSC"],
  "Teaches the stability control module the sideways acceleration sensor's reading with the car level and still.",
  "After replacing the DSC sensor or the DSC unit, or with yaw/lateral sensor faults.",
  "high",
  [R("QUERBESCHLEUNIGUNG_DSC_ABGLEICHEN", "E", sig=["E_OR_W"]), R("QUERBESCHLEUNIGUNG_DSC_ABGLEICHEN"),
   R("QUERBESCHLEUNIGUNGSSENSOR_DSC_ABGLEICHEN")],
  before=["Car on level ground, nobody in it, doors closed."],
  after=["Clear the DSC fault memory and test drive carefully."],
  checks=checks(engineOff=True, stationary=True, confirm=["The car is on level ground and nobody is inside or leaning on it."]))

F("dsc-longitudinal-sensor-zero", "Zero the longitudinal acceleration sensor", DSC, ["DSC"],
  "Teaches the stability control module the forward acceleration sensor's reading with the car level and still.",
  "After replacing the DSC sensor or the DSC unit, or with longitudinal acceleration sensor faults.",
  "high",
  [R("LAENGSBESCHLEUNIGUNG_DSC_ABGLEICHEN", "E", sig=["E_OR_W"]), R("LAENGSBESCHLEUNIGUNG_DSC_ABGLEICHEN", "R", sig=["ERGEBNIS"], show=True)],
  before=["Car on level ground, nobody in it, doors closed."],
  after=["Clear the DSC fault memory and test drive carefully."],
  checks=checks(engineOff=True, stationary=True, confirm=["The car is on level ground and nobody is inside or leaning on it."]))

F("abs-bleed", "Bleed the ABS/DSC unit", DSC, ["DSC"],
  "Cycles the valves and pump in the DSC unit for one brake circuit, so air trapped inside the unit is pushed out to the bleed screw.",
  "After replacing the DSC unit, or when air got into the system (a spongy pedal that normal bleeding doesn't fix).",
  "high",
  [R("NA_ENTLUEFTUNG_{circuit}_DX", map={"circuit": {"front": "VA", "rear": "HA", "left": "LI", "right": "RE"}}),
   R("NA_ENTLUEFTUNG_{circuit}", map={"circuit": {"left": "LI", "right": "RE"}}),
   R("STEUERN_REP_ENTLUEFTUNG", "E;{circuit};3;R", sig=["E_OR_W", "RAD_NR", "WIEDERHOLUNG", "ERGEBNIS"], show=True,
     map={"circuit": {"rear-left": "H_links", "front-left": "V_links", "front-right": "V_rechts", "rear-right": "H_rechts"}})],
  before=["Connect a pressure bleeder (about 2 bar) to the reservoir, or have a helper ready on the pedal.",
          "Fit a bleed hose and bottle to the bleed screw of the circuit you choose and open it just before you press Run.",
          "Do the normal bleed first, then this, then a normal bleed again.",
          "On xDrive cars (one routine per wheel) go in this order: rear left, front left, front right, rear right."],
  after=["Close the bleed screw, check the fluid level and bleed that wheel normally once more.",
         "Do every circuit the same way. Check the pedal is firm before driving."],
  checks=checks(engineOff=True, stationary=True, minVoltage=12.0,
                confirm=["A pressure bleeder or a helper is ready and the reservoir is full.", "The bleed screw for this circuit is open with a hose into a bottle."]),
  inputs=[choice("circuit", "Brake circuit", [("front", "Front axle"), ("rear", "Rear axle"), ("left", "Left circuit"), ("right", "Right circuit"),
                                              ("front-left", "Front left"), ("front-right", "Front right"), ("rear-left", "Rear left"),
                                              ("rear-right", "Rear right")])])

F("flat-tyre-reset", "Reset the flat tyre monitor", DSC, ["DSC"],
  "Resets the flat tyre warning (which compares wheel speeds), so it learns the current tyres as the correct reference.",
  "After setting the tyre pressures, changing or rotating tyres, or when the flat tyre warning keeps coming back.",
  "low",
  [R("RPA_RESET"), R("DDS_RESET")],
  before=["Set all four tyres to the pressure on the door pillar sticker first."],
  after=["The system learns during the next drive; the warning can't come on until it has finished."],
  checks=checks(engineOff=True, stationary=True, confirm=["All four tyres are at the correct pressure."]))

F("wheel-speed-sensor-test", "Test the wheel speed sensors", DSC, ["DSC"],
  "Records each wheel speed sensor's minimum and maximum reading for 20 seconds while you spin the wheels by hand.",
  "ABS or DSC faults for a wheel speed sensor, to find which sensor is dead or erratic.",
  "low",
  [R("STEUERN_DREHZAHLFUEHLER_ALLE", "20000", sig=["A_ZEIT"], show=True, say="Spin each wheel now"),
],
  before=["Raise the car so all four wheels turn freely, gearbox in neutral, engine off.",
          "After pressing Run, spin each wheel by hand for a few seconds within 20 seconds."],
  after=["A sensor that shows no speed (or a much smaller maximum) while its wheel turned is faulty, or its wiring or tone ring is."],
  checks=checks(engineOff=True, confirm=["The car is safely raised on stands and all wheels turn freely."]),
  duration="20 seconds")

F("active-steering-init", "Initialise active steering", DSC, ["AFS"],
  "Starts the active steering's initialisation and shows whether it completed.",
  "After replacing the active steering unit, a steering angle sensor, or after a wheel alignment on cars with active steering.",
  "high",
  [R("START_LWG_INIT", show=True, wait=3, then=[S("LWM_INIT_ABFRAGEN", show=True)])],
  before=["Car on level ground, wheels straight ahead, steering wheel centred, engine running."],
  after=["Clear the active steering and DSC fault memories, then test drive carefully."],
  checks=checks(engineRunning=True, stationary=True, confirm=[WHEELS_STRAIGHT]))

F("eps-angle-reset", "Reset the electric power steering angle", DSC, ["EPS"],
  "Resets the steering angle offset in the electric power steering.",
  "After a wheel alignment or steering repairs on cars with electric power steering.",
  "high",
  [R("LW_OFFSET_RUECKSETZEN")],
  before=["Wheels straight ahead, steering wheel centred."],
  after=["Turn the steering wheel fully left and right once with the engine running, then clear the fault memory."],
  checks=checks(stationary=True, confirm=[WHEELS_STRAIGHT]))

F("edc-steering-angle", "Calibrate the adaptive suspension steering angle", DSC, ["EDC"],
  "Calibrates the steering angle used by the electronic damper control.",
  "After a wheel alignment on cars with adaptive dampers (EDC).",
  "medium",
  [R("STEUERN_LWS_ABGLEICH")],
  checks=checks(stationary=True, confirm=[WHEELS_STRAIGHT]))

# ============================================================================ Gearbox and drivetrain
GB = "Gearbox and drivetrain"
F("gearbox-adaptation-reset", "Reset automatic gearbox adaptations", GB, ["EGS"],
  "Clears what the automatic gearbox has learned about shift timing and pressure, so it relearns from scratch.",
  "After a gearbox fluid change or a repair, or when shifts have become harsh or slurred.",
  "medium",
  [R("STEUERN_ADAPTIONSWERTE_RUECKSETZEN"), R("RESET_ADAPTION"), R("STEUERN_ADAPTIONSWERTE_LOESCHEN", "0", sig=["ARG"])],
  before=["Engine off, selector in P."],
  after=["Shifts may feel firm or soft for a while. Drive gently through all gears, including several light-throttle up- and downshifts, so it relearns."],
  checks=checks(engineOff=True, stationary=True, confirm=["The selector is in P."]))

F("smg-clutch-learn", "SMG: learn the clutch touch point", GB, ["EGS"],
  "Lets the SMG gearbox learn where the clutch starts to bite.",
  "After a clutch replacement, SMG pump or actuator work, or when take-off is jerky.",
  "medium",
  [R("STEUERN_KUPPLUNGSSCHLEIFPUNKT_EINLERNEN", wait=30, say="Learning the touch point")],
  before=["Warm engine, idling, in neutral, foot on the brake, doors closed."],
  after=["Switch the ignition off for at least a minute, then drive gently."],
  checks=checks(engineRunning=True, stationary=True, confirm=["The gearbox is in neutral and your foot is on the brake."]),
  duration="About 30 seconds")

F("smg-gear-learn", "SMG: learn the gear positions", GB, ["EGS"],
  "Lets the SMG gearbox learn the shift positions of every gear.",
  "After SMG actuator or gearbox work.",
  "medium",
  [R("STEUERN_GETRIEBE_EINLERNEN", wait=30, say="Learning the gear positions")],
  before=["Engine off, ignition on, foot on the brake."],
  after=["Switch the ignition off for at least a minute, then run the clutch touch point learning."],
  checks=checks(engineOff=True, stationary=True, minVoltage=12.5, confirm=["Your foot is on the brake."]),
  duration="About 30 seconds")

F("smg-clutch-bleed", "SMG: bleed the clutch hydraulics", GB, ["EGS"],
  "Runs the SMG pump and clutch valve so you can bleed air out of the clutch hydraulics.",
  "After replacing the clutch slave cylinder, the hydraulic lines or the SMG pump.",
  "high",
  [R("STEUERN_ENTLUEFTEN_KUPPLUNG", wait=30, say="Bleeding")],
  before=["Fit a bleed hose and bottle to the clutch slave cylinder's bleed screw and open it just before pressing Run.",
          "Fill the SMG reservoir to the mark."],
  after=["Close the bleed screw and top up the reservoir.",
         "Switch the ignition off and on before running another SMG function, then learn the clutch touch point."],
  checks=checks(engineOff=True, stationary=True, minVoltage=12.5, confirm=["The bleed hose is fitted and the reservoir is full."]),
  duration="About 30 seconds")

F("transfer-case-relearn", "xDrive transfer case: reset and relearn", GB, ["VGSG"],
  "Deletes the transfer case clutch calibration and runs the function test, so the actuator relearns the clutch.",
  "After replacing the transfer case actuator motor, the transfer case or its fluid, or with xDrive clutch faults.",
  "medium",
  [R("STEUERN_FUNKTIONSPRUEFUNG", wait=15, say="Relearning the clutch")],
  before=["Car stationary on level ground, ignition on, engine off."],
  after=["Clear the transfer case and DSC fault memories and test drive."],
  checks=checks(engineOff=True, stationary=True, minVoltage=12.0))

F("transfer-case-wear-reset", "xDrive transfer case: reset wear counters", GB, ["VGSG"],
  "Resets the transfer case's wear counters, which it uses to adjust how hard it clamps the clutch.",
  "Only after fitting a new transfer case or new transfer case clutch.",
  "medium",
  [R("STEUERN_HO_INTEGRATOREN_LOESCHEN", then=[S("STEUERN_LT_INTEGRATOREN_LOESCHEN")])],
  before=["Don't do this on a worn transfer case: the counters tell it how to compensate for wear."],
  after=["Run 'xDrive transfer case: reset and relearn' afterwards."],
  checks=checks(engineOff=True, stationary=True, confirm=["The transfer case (or its clutch) is new."]))

F("xdrive-clutch-test", "xDrive transfer case clutch test", GB, ["DSC"],
  "Runs the stability control module's test of the xDrive clutch and shows the result.",
  "xDrive faults, binding in tight turns, or after transfer case work.",
  "low",
  [R("STEUERN_KUPPLUNGS_TEST", "R", sig=["ERGEBNIS"], show=True, say="Testing the clutch (up to 30 seconds)")],
  before=["Car stationary on level ground, engine running, selector in P."],
  checks=checks(engineRunning=True, stationary=True, confirm=["The selector is in P with the handbrake on."]))

# ============================================================================ Body and comfort
BODY = "Body and comfort"
F("window-initialisation", "Initialise the windows (one-touch and anti-trap)", BODY, ["FRM"],
  "Teaches the footwell module the windows' end positions, so one-touch up/down and the anti-trap protection work again.",
  "After the battery was disconnected or flat, or when one-touch window operation stopped working.",
  "medium",
  [R("STEUERN_FENSTERHEBER_EINLERNEN", "{window}", sig=["AUSWAHL_FENSTER"],
     map={"window": {"driver": "0x11", "passenger": "0x12", "both": "0x21", "all": "0x40"}}),
   R("STEUERN_FENSTERHEBER_EINLERNEN", "{window}", sig=["AUSWAHL_FH"],
     map={"window": {"driver": "FH_FAT", "passenger": "FH_BFT", "both": "BEIDE_FH"}})],
  before=["Close all doors. The windows will move on their own: keep hands, pets and objects out of the way."],
  after=["Try one-touch up and down on each window."],
  checks=checks(engineOff=True, confirm=["All doors are closed and nothing is in the way of the windows."]),
  inputs=[choice("window", "Windows", [("both", "Both front windows"), ("driver", "Driver's window"), ("passenger", "Passenger's window"),
                                       ("all", "All windows")])])

F("sunroof-initialisation", "Initialise the sunroof", BODY, ["FZD"],
  "Starts the sunroof's automatic initialisation: it runs to its end positions and stores them.",
  "After the battery was disconnected, after sunroof repairs, or when the sunroof stops partway or won't do one-touch.",
  "medium",
  [R("STEUERN_AUTOINIT", "ein", sig=["AUTOINIT_CONTROL"])],
  before=["The sunroof will open and close on its own: keep hands and objects out of the opening."],
  after=["Wait until the sunroof has stopped moving (about a minute) before switching the ignition off, then try one-touch open and close."],
  checks=checks(engineOff=True, confirm=["Nothing is in the sunroof opening."]),
  duration="About a minute (runs on its own)")

F("climate-flap-calibration", "Calibrate the climate control flaps", BODY, ["IHKA"],
  "Runs every air flap motor to its end stops so the climate control knows their positions again.",
  "After replacing a flap motor or the climate control unit, or when air comes out of the wrong vents or a flap clicks.",
  "low",
  [R("STEUERN_MOTOREN_EICHLAUF", wait=30, say="Calibrating the flaps"),
   R("STEUERN_EICHLAUF", "ja", sig=["DIAGNOSE_AUFRECHT"], wait=30, say="Calibrating the flaps", stop=[S("DIAGNOSE_ENDE")]),
   R("STEUERN_EICHLAUF", wait=30, say="Calibrating the flaps", stop=[S("DIAGNOSE_ENDE")])],
  after=["Listen for clicking from the dashboard: a flap motor that keeps clicking or doesn't move is faulty."],
  checks=checks(**ENGINE_OFF), duration="About 30 seconds")

F("rain-sensor-init", "Initialise the rain sensor", BODY, ["RLS"],
  "Lets the rain sensor recalibrate to the windscreen.",
  "After replacing the windscreen or the rain sensor, or refitting the sensor's gel pad.",
  "low",
  [R("AIC_INIT")],
  before=["The windscreen must be clean and dry at the sensor."],
  checks=checks(**ENGINE_OFF))

F("pdc-sensor-test", "Test the parking sensors", BODY, ["PDC"],
  "Runs the parking sensors' self-test and shows the result.",
  "Parking sensor faults or a sensor that beeps constantly.",
  "low",
  [R("STEUERN_SENSORTEST", show=True)],
  checks=checks(**ENGINE_OFF))

F("cluster-test", "Instrument cluster gauge test", BODY, ["KOMBI", "IKE"],
  "Sweeps every gauge needle and lights the warning lamps once, so you can see that each works.",
  "A gauge that sticks or reads wrong, or a warning lamp you think is dead.",
  "low",
  [R("STEUERN_SELBSTTEST_EIN", "1", sig=["BYTE0"], wait=15, say="Gauges sweeping", stop=[S("STEUERN_SELBSTTEST_AUS")]),
   R("STEUERN_SELBSTTEST", wait=15, say="Gauges sweeping")],
  checks=checks(**ENGINE_OFF), duration="About 15 seconds")

# ============================================================================ build
def build():
    out = []
    problems = []
    for f in FUNCTIONS:
        mods = f["module"] if isinstance(f["module"], list) else [f["module"]]
        pool = list(dict.fromkeys(v for m in mods for v in variants_of(m)))
        claimed = set()
        impls = []
        for r in f["recipes"]:
            matched = []
            for v in pool:
                if v in claimed or v in r["exclude"] or (r["only"] and v not in r["only"]):
                    continue
                job = r["job"]
                # A job name with {choice} must exist for every value the map offers.
                names = [job]
                if "{" in job:
                    key = job[job.index("{") + 1: job.index("}")]
                    names = [job.replace("{" + key + "}", val) for val in (r["map"] or {}).get(key, {}).values()]
                sigs = [job_args(v, n) for n in names]
                if any(s is None for s in sigs):
                    continue
                if r["sig"] is not None and any(s != r["sig"] for s in sigs):
                    continue
                if any(not has_job(v, s["job"]) for s in r["stop"]) or any(not has_job(v, s["job"]) for s in r["then"]):
                    continue
                matched.append(v)
            if not matched:
                problems.append(f"{f['id']}: recipe {r['job']} {r['sig']} matched no module version")
                continue
            claimed.update(matched)
            steps = [S(r["job"], r["args"], r["wait"], r["show"], r["say"])] + r["then"]
            impl = {"variants": sorted(matched), "steps": steps}
            if r["stop"]: impl["stop"] = r["stop"]
            if r["map"]: impl["map"] = r["map"]
            if r["native"]: impl["native"], impl["expect"] = r["native"], r["expect"]
            impls.append(impl)
        if not impls:
            problems.append(f"{f['id']}: no implementation at all")
            continue
        # Choices that no implementation supports are dropped (e.g. a circuit only another module version has).
        inputs = []
        for inp in f["inputs"]:
            if inp.get("type") == "choice":
                keep = [c for c in inp["choices"] if any(not i.get("map") or inp["id"] not in i["map"] or c["value"] in i["map"][inp["id"]] for i in impls)]
                inp = dict(inp, choices=keep)
            inputs.append(inp)
        entry = {k: f[k] for k in ("id", "title", "category", "module", "summary", "when", "risk")}
        entry["verified"] = False
        if f["duration"]: entry["duration"] = f["duration"]
        entry["before"], entry["after"] = f["before"], f["after"]
        entry["checks"] = f["checks"] or {"ignition": True}
        if inputs: entry["inputs"] = inputs
        entry["implementations"] = impls
        out.append(entry)
    comment = ("Klemme15 service functions, generated by tools/service-catalog/build_service.py (edit the recipes there, not this file). "
               "Descriptions are our own words. Job names and the module versions that have them were checked against the job definitions "
               "in bmw-unified-db. 'steps' run BMW's jobs from the user's files in order; 'stop' always runs afterwards to hand a part back to the "
               "module; 'map' translates a user's choice into what that module version expects; 'native' is a request Klemme15 sends itself, "
               "only where it is documented for exactly those versions. 'verified' stays false until confirmed on a real car. "
               "Never add flashing, coding or immobiliser functions.")
    doc = {"$comment": comment, "functions": out}
    path = ROOT / "src/Klemme15.Data/db/service.json"
    path.write_text(json.dumps(doc, indent=1, ensure_ascii=False) + "\n")
    print(f"{len(out)} functions, {sum(len(e['implementations']) for e in out)} implementations -> {path}")
    for e in out:
        n = sum(len(i["variants"]) for i in e["implementations"])
        print(f"  {e['id']:32} {n:3} versions")
    for p in problems:
        print("WARNING", p)
    write_docs(out)

def write_docs(out):
    lines = ["# Service functions: full list", "",
             "Generated from `src/Klemme15.Data/db/service.json` by `tools/service-catalog/build_service.py`. "
             "See [SERVICE.md](SERVICE.md) for how they run and how they are kept safe.", "",
             "All functions are **experimental** until confirmed on a real car. Except where noted, they run BMW's own procedure "
             "and need your BMW files (EDIABAS mode).", ""]
    for cat in dict.fromkeys(e["category"] for e in out):
        lines += [f"## {cat}", "", "| Function | Module | When to use it | Module versions |", "|---|---|---|---|"]
        for e in out:
            if e["category"] != cat: continue
            mods = e["module"] if isinstance(e["module"], list) else [e["module"]]
            n = sum(len(i["variants"]) for i in e["implementations"])
            native = any("native" in i for i in e["implementations"])
            title = e["title"] + (" (also without BMW files)" if native else "")
            lines.append(f"| {title} | {' / '.join(mods)} | {e['when']} | {n} |")
        lines.append("")
    (ROOT / "docs/SERVICE-FUNCTIONS.md").write_text("\n".join(lines))

build()
