# Research brief: BMW fault code meanings for Klemme15

Klemme15 is an open-source (GPLv3) BMW diagnostic app. When it reads a module's fault memory it gets a module (e.g. the DSC at address 0x29 on an E90) and a 4-digit hex BMW fault code (e.g. 5E18). We need a database that tells the owner what each code means and where to look. The project must stay legally clean and trustworthy.

## Rules (non-negotiable)

1. **Never copy text verbatim** from BMW software (ISTA, INPA, EDIABAS, TIS, WDS), BMW documents, or any website. Read the sources, then write the meaning in your own plain English (one sentence, under ~25 words). Forum posts often quote ISTA text word for word: paraphrase it.
2. **Only include a code when a specific source discusses that exact code** for this chassis family (E9x = E90/E91/E92/E93; E8x/E6x sharing the same module family is acceptable if you say so in notes). Every entry needs at least one source URL that you actually opened or that a search result clearly attributes to that code.
3. **Do not use paid/licensed code databases as the basis** (Autodata, obdcloud, Alldata, Mitchell). Owner forums (e90post, bimmerfest, bimmerforums, babybmw, e46fanatics, n54tech), independent shop blogs, YouTube descriptions, Pelican Parts and similar are fine.
4. **Confidence**: "high" = two or more independent sources agree on the meaning; "medium" = one specific, credible source. Skip anything weaker. Never guess a meaning from neighbouring codes.
5. A wrong meaning sends someone to buy the wrong part. When unsure, leave it out.
6. Don't mention or use any password/decryption of BMW databases.

## Output

Write a JSON array to the output file named in your task, one object per code:

```json
{
  "module": "E9x.DSC",
  "code": "5E18",
  "text": "DSC is missing data it expects from the engine computer over the CAN bus.",
  "hint": "Optional practical next step or common cause, own words, mark guesses as 'common cause'.",
  "component": "optional id from the component list below, only if the code clearly concerns that part",
  "engines": ["optional, e.g. N52, N54 when the code is engine-specific"],
  "confidence": "high",
  "sources": ["https://...", "https://..."]
}
```

Codes are 4 hex digits, uppercase (e.g. "2A82", "A0A9"). If a source shows the code with leading zeros ("002A82") or as decimal, normalize to 4 hex digits and say so in a "notes" field.

## Module ids (use exactly these)

E9x.DME (engine, 0x12), E9x.EGS (automatic gearbox, 0x18), E9x.DSC (stability/ABS, 0x29), E9x.ACSM (airbag, 0x01), E9x.CAS (car access/immobilizer, 0x40), E9x.KOMBI (instrument cluster, 0x60), E9x.FRM (footwell module: lights, windows, 0x72), E9x.JBE (junction box, 0x00), E9x.IHKA (climate, 0x78), E9x.SZL (steering column switches, 0x02), E9x.EKPS (fuel pump control, 0x17), E9x.PDC (parking sensors, 0x64), E9x.CIC (head unit CCC/CIC, 0x63), E9x.FZD (roof function centre / rain-light-condensation sensors / sunroof, 0x56).
If you find well-sourced codes for another E9x module, use "E9x.<SHORTNAME>" and add "address" if known.

## Component ids (for "component")

engine, cyl1..cyl6, intake, throttle, fuel_rail, thermostat, coolant_sensor, fan, vanos, cam_sensor, crank_sensor, knock, o2_b1s1, o2_b2s1, o2_b1s2, o2_b2s2, cat, secondary_air, turbo, evap, fuel_tank, battery, transmission, wheel_fl, wheel_fr, wheel_rl, wheel_rr, headlight_left, headlight_right, taillight_left, taillight_right, blower, steering_lock, mirror_sensors

## Method

Search broadly (e.g. "E90 <module> fault code list", "N52 fault codes 2A", "<code> E90", "e90post <code>"), collect candidate codes, then confirm each. Aim for the codes owners actually hit most. Quality over quantity, but be thorough: dozens of well-sourced codes per area is the goal. Finish with a short summary: how many codes, how many high vs medium, and any caveats.
