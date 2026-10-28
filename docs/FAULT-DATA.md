# Where Klemme15's fault meanings come from

A BMW module reports a fault as a 4-digit hex code (`2A82`, `5E1A`). On its own the number means nothing: the
same code means different things on different modules, and even on different **versions** of the same module.
On an E90, `5E1A` is "CAN signal error from the engine computer" on the earlier DSC (`DSC_87`) but an internal
SPI fault on the later one (`DXC_90`). Getting the module version right is half the job.

Klemme15 combines four sources, and tells you on every fault which one it used.

| Source | What it gives | Ships with Klemme15? |
|---|---|---|
| **Your own BMW files** (ISTA, INPA or EDIABAS `Ecu` folder) | BMW's official text for every code, read from the `.prg` file of your exact module | No. Read on your computer only |
| **bmw-unified-db** (optional download, ~13 MB) | BMW's fault dictionaries for about 220 E46/E8x/E9x module versions, with BMW's English where it exists, part numbers and engine codes for identifying the module | No. Downloaded by you from its own project |
| **Klemme15 research** (`src/Klemme15.Data/db/bmw-codes.json`) | Plain-English meanings, common causes and fixes, with sources | Yes, our own work |
| **Generic OBD-II** (`obd-generic.json`, `obd-codes.json`) | Meanings for `P0xxx`-style codes | Yes |

## How a code is described

1. **Find the module version.** In order of trust:
   - EDIABAS mode: the module names its own variant (e.g. `MSV70`).
   - The module's BMW part number, matched against the part numbers bmw-unified-db lists for each variant.
   - Engine computers: the engine code of the car you picked (N52 → `MSV70`; N54 → `MSD80`).
   - Otherwise the most common variant for that car, marked *"most likely"*, with the meanings on the other
     variants listed under the fault so nothing is hidden.
2. **Pick the headline text.** BMW's own English → Klemme15's researched meaning → BMW's German run through
   Klemme15's glossary (`db/glossary-de-en.txt`, marked *approximate*) → BMW's German as is (with a Translate button).
3. **Always attach** BMW's original wording, the module version, our tip on what to check, and source links.

You can try this without a car:

```
klemme15 db download
klemme15 code E9x.DSC 5E1A --model 325i
klemme15 code E9x.DME 2A82 --model 335i
```

## The optional download: bmw-unified-db

- Project: <https://github.com/gediz/bmw-unified-db> (MIT for its code)
- What Klemme15 downloads: `json.tar.gz` from that project's **latest GitHub release**, pinned to the release tag,
  checked against the release's `SHA256SUMS`. Klemme15 keeps only the module variants for the cars it supports, in
  `%APPDATA%/Klemme15/bmw-unified-db` (`~/.config/Klemme15/...` on Linux and macOS).
- Licence of the data: the project states that its fault texts are **derived from BMW's diagnostic data and are BMW's
  copyright**, published for interoperability and right-to-repair, and removed on request. See its
  [CREDITS.md](https://github.com/gediz/bmw-unified-db/blob/main/CREDITS.md).
- That is why Klemme15 **links to it and never copies it into this repository**. If you redistribute the data,
  that project's terms apply, not Klemme15's GPL.
- Offline machine? Download `json.tar.gz` yourself and run `klemme15 db install json.tar.gz`.

What it covers (release v1.2.0, measured):

| Car | Modules with BMW fault dictionaries | Notes |
|---|---|---|
| E90/E91/E92/E93 | engine (petrol and diesel), DSC, airbag, CAS, KOMBI, FRM, JBE, IHKA, EGS, EPS, fuel pump, PDC, CCC/CIC and most others | About half of the texts have BMW's own English |
| E46 | engine (MS42, MS43, MS45, ME9, diesels), DSC, IKE, GM5, LCM, IHKA, EWS and others | Almost all German, so the glossary and Translate do the work |

## Your own BMW files

Klemme15 finds ISTA, INPA and EDIABAS installs on Windows automatically (Garage → *Official BMW fault meanings*,
or `--ecu <folder>` on the command line). On macOS or Linux, copy the `Ecu` folder from a Windows install and point
Klemme15 at it. The reader (`SgbdFile.cs`) only reads the `FORTTEXTE`/`IORTTEXTE` tables of the variant your module
uses. It never writes to, uploads or decrypts anything. `klemme15 sgbd <file.prg>` lists what a file contains.

## What we deliberately do not do

- We do not commit BMW's text, tables or files to this repository.
- We do not decrypt ISTA's databases or work around any protection.
- We do not send your data anywhere. The only network calls are the download you start, and the Translate button,
  which opens BMW's German text in Google Translate in your browser.

## Help make it better

- **Real-car reports:** a scan report plus the traffic log (Log tab → Save) lets us confirm module versions and
  part numbers.
- **Fixes that worked:** add the code, symptoms and what fixed it to `bmw-codes.json` in your own words with a source
  (see [CONTRIBUTING-DATA.md](CONTRIBUTING-DATA.md)). Our tips show alongside BMW's text, and that is where
  Klemme15 can beat the dealer tool.
- **Glossary:** if a translated text reads badly, add the German word or compound to `db/glossary-de-en.txt`.
