# Contributing to the open database

The database lives in `src/OpenHood.Data/db/` as plain JSON. You can also drop files with the same names into your user folder (`%APPDATA%\OpenHood\db` on Windows, `~/.config/OpenHood/db` on Linux and macOS) to try changes without rebuilding. Entries there override the built-in ones.

## Ground rules

1. **Write everything in your own words.** Never copy text from BMW software, SGBD files, ISTA, TIS/WDS or repair manuals.
2. **Confirm on a real car.** Mention the car (chassis, engine, year) in your pull request.
3. **Include evidence.** Attach the saved traffic log or report from the session where you confirmed it.

## Verifying a module (`modules.json`)

Run a quick test on your car. For every module that answers, set `"verified": true` and note the car in the PR. If a module answers at a different address, or a module you have is missing, add or fix the entry.

```json
{ "id": "E9x.CAS", "name": "Car access system (CAS)", "address": "40", "protocol": "BmwFast",
  "category": "Body", "sgbd": ["D_0040"], "verified": true }
```

## Placing things on the car drawing (`components.json`, `pos`)

The Vehicle page draws the car from above in a 1000 x 460 grid: **front at the left, driver's side (left-hand drive) at the bottom**. Every module in `modules.json` has a `"pos": [x, y]` for where it sits. `components.json` lists parts faults can come from (cylinders, thermostat, O2 sensors...) with their positions, plus `rules` that map OBD-II codes to a part with a regular expression:

```json
{ "match": "^P030([1-6])$", "component": "cyl$1" }
```

If you know where a module or part really sits on your car and the marker is off, move it and say which car you checked.

## Models and demo cars (`vehicles.json`)

Each family lists its models with engine, years, drive, the modules its demo car has fitted, and the demo faults. Demo faults with OBD-II codes use real meanings. BMW-format demo codes are invented for the demo and their text must start with "Demo fault:" so nobody mistakes them for real BMW definitions.

## Describing a BMW fault code (`bmw-codes.json`)

Key by module id, then the 4-digit hex code as OpenHood displays it:

```json
{
  "modules": {
    "E9x.DME": {
      "XXXX": { "text": "Short description of the fault in your own words" }
    }
  }
}
```

Good descriptions say what the module detected and, where known, a common cause. Keep guesses out or label them "common cause".

## OBD-II codes (`obd-codes.json`)

Generic codes are standardized; improve the wording or add a `hint` with practical E-series advice:

```json
"P0171": { "text": "System too lean, bank 1", "hint": "Common causes on M54/N52: ..." }
```

## DS2 fault decoders

For E46-era modules the native backend can identify modules but cannot yet decode their fault memory. Each module family uses its own record layout. If you can capture a module's fault-memory response together with what the fault actually is (for example by causing a known fault and clearing it afterwards), open an issue with the traffic log: that is enough to write a decoder.
