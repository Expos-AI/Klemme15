# Dash messages (Check Control) and Shadow / Info memory

The small warning triangle on the instrument cluster is often lit by a **Check Control message** (a CC-ID), not by a fault code. A weak key battery, a bulb out or low washer fluid can all show the triangle while every module's fault memory is empty. A scan that only reads fault codes says "no faults", but the car still shows a warning.

Klemme15 reads both, and keeps them apart from real faults.

## What is read

| What | Module | How (native, D-CAN) | How (EDIABAS mode) |
|---|---|---|---|
| Messages showing now | Instrument cluster (KOMBI, 0x60) | KWP2000 `$21 09` | `STATUS_CHECKCONTROL_LESEN` |
| Message history, with mileage | Instrument cluster | KWP2000 `$21 0F` | `STATUS_CHECKCONTROL_HISTORY` |
| Shadow / Info memory | CAS (0x40) and engine ECU (0x12) | KWP2000 `$22 2000` | `IS_LESEN` (every module that has it) |

Reading these is read-only. Nothing is written to the car.

**Reply layouts.** `$21 09` answers `61 09`, sometimes a count byte, then 2 bytes per ID (high byte first). Klemme15 tells the two forms apart by length. `$21 0F` is read as 4 bytes per entry: the ID, then the mileage in 8 km steps. That layout comes from BMW's result descriptions and **has not been confirmed on a car yet**. Klemme15 drops a reply that doesn't fit, rather than guessing. The raw replies are kept in the traffic log, so please attach a log if something looks wrong.

## Where the meanings come from

`src/Klemme15.Data/db/check-control.json` holds Klemme15's own list. It is written in our own words from community lists, with the sources listed for each entry.

- **Confidence:**
  - `high`: the ID and its meaning agree across community lists and a local cross-check of BMW's cluster text table.
  - `medium`: from community lists only.
- **Severity** follows the colour of the triangle:
  - `Critical`: red, stop safely.
  - `Warning` and `Advisory`: yellow.
- **Scope:** the IDs are for E8x, E9x, E6x and E7x clusters. Other models can number them differently.

BMW's own message texts are **never shipped**. If you point Klemme15 at your own ISTA, INPA or EDIABAS files, it reads the cluster's text table (`CCM_TEXT_TABELLE`, found in some cluster files) on your machine and shows BMW's wording next to Klemme15's.

An ID that isn't in the list still shows up, as "Check Control message N", so nothing is hidden.

### A common mix-up

Several write-ups give the remote key battery as CC-ID 75. On these clusters, **67** is "remote key battery low", 65 and 68 are related key-battery messages, and 75 is "trailer electrics failed".

## Adding or correcting a message

Drop a `check-control.json` into your override folder (`%APPDATA%/Klemme15/db` on Windows). Its entries replace the built-in ones with the same ID:

```json
{ "messages": [
  { "id": 67, "title": "Key battery low", "severity": "Advisory",
    "description": "In your own words.", "hint": "What to do.",
    "confidence": "medium", "sources": ["https://link-to-where-you-learned-it"] }
] }
```

To share a message with everyone, open a pull request against `db/check-control.json`. Write it in your own words and include a source. Please don't paste BMW's text.

## Shadow / Info memory

Modules log events here that never light a lamp on their own, for example a crank that was aborted, a remote that answered weakly, or a voltage dip. These entries are labelled **Shadow / Info memory** everywhere: on the Faults page, in the reports and in forum posts. They are sorted as background, never counted as faults, and are never a reason to replace a part on their own.
