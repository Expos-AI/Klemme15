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

**Reply layouts.** These were confirmed on a real E90 (KOMBI, CAS3, MSV70):

- **`$21 09`** answers `61 09` followed by 2 bytes per ID, high byte first, with no count byte. A count byte first is accepted too; Klemme15 tells the two apart by length.
- **`$21 0F`** answers `61 0F` followed by 4 bytes per entry: the ID, then the mileage in 8 km steps, oldest first. The same message can appear many times.
- **`$22 2000`** on the CAS answers `62 20 00`, a count byte, then 3 bytes per entry (code, status).
- **MSV70 engine computer** declines `$22 2000` (`7F 22 12`), which is expected and ignored.

Replies that don't fit these layouts are dropped rather than guessed at. The raw bytes are always kept in the traffic log.

**Tip:** in the history, several messages at the *same* mileage (DSC, brake assist, hill start, flat tyre monitor, "charge battery") usually mean one event, almost always a voltage dip from a weak battery or a long crank. They don't mean five separate faults.

## Where the meanings come from

`src/Klemme15.Data/db/check-control.json` holds Klemme15's own list: 396 IDs from 1 to 999, written in our own words from the public community lists, with the sources, a severity and the module that usually raises it. The public lists turn out to be copies of one list, so a meaning BMW's own table also confirms (checked locally, never shipped) is marked `high`; where the two differ, BMW's numbering wins and the entry says so (for example, 123 and 133 are the *left* rear light). IDs that appear in no public list, such as 34 and 77, still show by number, with BMW's wording if you have your BMW files.

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

Klemme15 reads it from every D-CAN module that has one, and **Clear fault memory** clears it too, as ISTA does (EDIABAS `IS_LOESCHEN`; natively KWP `$31 06`, sent only to a module that has just confirmed it has an info memory). Modules log events here that never light a lamp on their own, for example a crank that was aborted, a remote that answered weakly, or a voltage dip. These entries are labelled **Shadow / Info memory** everywhere: on the Faults page, in the reports and in forum posts. They are sorted as background, never counted as faults, and are never a reason to replace a part on their own.
