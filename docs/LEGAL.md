# Safety, legal and transparency

Klemme15 talks to the computers in your car. Most of what it does only reads, but some functions switch parts on or write to a module. This page says plainly what that means for you, what Klemme15 will and won't do, whose work it builds on, and how it's made. The app shows the same text under **Safety, legal and privacy**, and asks you to read the short version once before you start.

## In short

- **Klemme15 is free and comes with no warranty.** You use it at your own risk, and you're responsible for your car.
- **Work safely:** parked, handbrake on, a battery charger for anything that writes, and never operate the laptop while driving.
- **Functions marked experimental** haven't been confirmed on a real car yet. They're off until you switch them on.
- **Klemme15 never** flashes modules, touches keys or the immobiliser, or changes mileage.
- **No BMW software is included.** If you use your own BMW files, having the right to use them is up to you.
- **Fault meanings are research, not a verdict.** Brakes, airbags and steering belong with a qualified mechanic.
- **Nothing about your car leaves your computer** unless you choose to share it.
- **Klemme15 isn't affiliated with BMW AG.** It's an independent project by one owner, built with help from AI coding tools, and its whole source is public.

## No warranty: you use it at your own risk

Klemme15 is free software under the GNU General Public License, version 3. As that licence says (sections 15 and 16), it comes **without any warranty**, to the extent the law allows: not that it works, not that it suits your car, not that its information is correct. To the extent the law allows, neither the author nor anyone who contributes to Klemme15 is liable for any damage that comes from using it, to your car, your computer, your data or anything else.

You decide what to run on your car, and you're responsible for the result. If you're not sure about a step, stop and ask someone who knows, or a mechanic.

This page doesn't take away any right the licence gives you, and it doesn't add conditions to it: you can still use, study, change and share Klemme15 under the GPL.

## Working on a car safely

- **Park safely.** Level ground, automatic in P or manual in neutral, handbrake on. For tests that run the engine, keep hands, hair, clothes and cables away from the fan and belts, and run the engine only outside or with good ventilation, never in a closed garage.
- **Keep the voltage up for anything that writes.** Coding, restoring coding, clearing faults and most service functions write to a module. A voltage dip during a write can leave a module that doesn't work. Use a proper battery charger or power supply (not a jump pack) for those, and don't unplug the cable, close the laptop or switch off the ignition until Klemme15 says it's done. Klemme15 checks the voltage first and refuses below its limits, but it can't see a battery that's about to give up.
- **The footwell module (FRM3)** on E-series cars is known to corrupt itself if the voltage drops while it writes. Klemme15 applies stricter voltage rules to it, and to any footwell module it can't identify.
- **Never operate the laptop while driving.** For the Ghost Hunter and live recordings, start them while parked, put the laptop somewhere it can't slide or fly around, and stop when you've parked again. Better still, have a passenger handle it. Obey the traffic laws: no test is worth an accident.
- **Airbags, brakes and steering are safety systems.** Reading their faults is fine. Repairing them is a job for someone trained: an airbag can fire during work on its wiring, and mistakes on brakes or steering can kill.
- **Functions that switch a part on** (pumps, fans, valves, the water pump sweep) run it briefly and switch it off again. If something sounds or smells wrong, press Stop and switch the ignition off.

## Experimental functions

A function is **experimental** until it has been confirmed to work on a real car with Klemme15. Experimental functions follow BMW's own documented procedures as closely as Klemme15 can describe them, but they haven't been tried on every module version. They're off until you switch them on in the Service page, and each one says what it does, what it needs and what to check afterwards. Please report how it went: that's how a function becomes confirmed.

## What Klemme15 never does

- **No flashing:** it never rewrites a module's software.
- **No immobiliser or key work:** keys, the immobiliser and its secret key (ISN) are never read or changed. The one write to the key module (CAS) is clearing the steering lock's event counters, a documented service job.
- **No mileage changes:** recorded mileage is read, never written. The used-car check compares the mileage the modules remember; a careful rollback can still pass it.
- **No help defeating emissions or safety equipment.** Keeping your car legal and roadworthy where you drive it is your responsibility. Coding presets change comfort, cluster and lighting settings (such as the bulb warnings after an LED conversion); they don't touch the engine, emissions, airbag, brake or stability systems. Make sure any lights you fit are legal where you drive, and that they work: with the bulb warning off, the car won't tell you when one fails.

## Copyright and BMW's software

Klemme15 is written from scratch and contains **no BMW software and no BMW data files**. It doesn't decrypt or unpack BMW's programs, and it doesn't link to unofficial copies of them.

- **Your own BMW files.** If you point Klemme15 at an ISTA, INPA, EDIABAS or NCS Expert installation, it reads those files on your computer and never copies or uploads them. Whether you have the right to use them is between you and BMW. Workshops can get BMW's software legally from BMW's aftersales portal.
- **The fault database.** The optional download of BMW's fault texts and engine values comes from [bmw-unified-db](https://github.com/gediz/bmw-unified-db), an independent community project. That project describes this data as derived from BMW's own and copyrighted by BMW, published for interoperability and right to repair. Klemme15 doesn't include or host it: it fetches it from that project only when you ask, after showing you this notice once, and checks its checksum. Whether you may use it where you live, and on what terms, is your responsibility. Klemme15 works without it.
- **EdiabasLib.** EDIABAS mode uses [EdiabasLib](https://github.com/uholeschak/ediabaslib), an open-source re-implementation of EDIABAS, under its own licence.
- **Fonts and libraries.** The Barlow fonts are used under the SIL Open Font License; the user interface is built with Avalonia (MIT licence). Each component keeps its own licence.

## The law, briefly

This is a plain summary of the rules Klemme15 is built around, for the United States, as of late 2026. It isn't legal advice, laws differ between countries and states, and they change: check the rules where you live.

- **Repair and diagnosis.** The U.S. Copyright Office's exemption under section 1201 of the DMCA, renewed in [October 2024](https://www.federalregister.gov/documents/2024/10/28/2024-24563/exemption-to-prohibition-on-circumvention-of-copyright-protection-systems-for-access-control), lets vehicle owners, and people acting for them, get around access controls on a vehicle's software to diagnose, repair or lawfully modify it. Klemme15 doesn't need that exemption for what it does: it talks to the car over its documented diagnostic protocols, and it never decrypts or unlocks BMW's programs.
- **Emissions.** The Clean Air Act forbids removing or disabling a vehicle's emissions controls, and the EPA still enforces it ([EPA tampering policy](https://www.epa.gov/enforcement/epa-tampering-policy-epa-enforcement-policy-vehicle-and-engine-tampering-and)). Klemme15 has no function that disables, deletes or hides emissions equipment.
- **Mileage.** Federal law makes it illegal to change a vehicle's recorded mileage ([49 U.S.C. § 32703](https://www.law.cornell.edu/uscode/text/49/32703)). Klemme15 only reads mileage.
- **Trademarks.** Naming BMW, ISTA or INPA to say what Klemme15 works with is allowed as long as it's accurate and doesn't suggest BMW endorses it ([nominative fair use](https://www.inta.org/fact-sheets/fair-use-of-trademarks-intended-for-a-non-legal-audience/)). That's why Klemme15 uses BMW's names only in plain text, never its logos, and says everywhere that it isn't affiliated with BMW.

## Trademarks

BMW, ISTA, INPA, NCS Expert and EDIABAS are trademarks of BMW AG. Klemme15 is an independent project. It isn't affiliated with, endorsed by or supported by BMW AG, and BMW's names appear only to say which cars and tools Klemme15 works with.

## How good the information is

Fault meanings, tips, guides and diagnostic chains come from public research by owners and mechanics, written in Klemme15's own words with their sources, and each says how confident it is. They can be wrong for your car. Treat them as a well-informed starting point, check the sources, and confirm a diagnosis before you buy parts. Test verdicts are clues based on Klemme15's own rules of thumb, not a guarantee.

## Privacy

Klemme15 runs on your computer, with no account, no server and no analytics. It goes online only when you ask (downloading the fault database or coding maps, translating a fault text, opening a link) and never sends anything about your car on its own. What it keeps on your computer, where, and what goes into files you choose to share is listed in the [privacy policy](../PRIVACY.md). The VIN is left out of forum posts, snapshots, captures and history (the policy explains the one exception, a traffic log you choose to include).

## How Klemme15 is made

Klemme15 is designed, written and maintained by Kindric Castro, an E90 owner. AI coding assistants help write parts of the code, the documentation and the research behind the data. To keep that honest:

- The whole source is public on GitHub, so anyone can read exactly what it does.
- Every change is built and tested automatically on Windows, macOS and Linux before release, and releases come with checksums and signed build provenance.
- Facts about cars (fault meanings, module jobs, value names) carry their sources, and anything that drives a part or writes to a module stays experimental until it's been confirmed on a real car.

## Questions and problems

- Bugs and questions: [open an issue](https://github.com/Expos-AI/Klemme15/issues/new/choose).
- Security problems: see [SECURITY.md](../SECURITY.md).
- The licence in full: [LICENSE](../LICENSE).

*This page explains how Klemme15 is meant to be used. It isn't legal advice.*
