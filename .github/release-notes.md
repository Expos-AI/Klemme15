Beta candidate. Quick test, faults, readings, live data, guided tests and experimental service functions.

**New in 0.99.27:**
- **Find your car without knowing its chassis code.** Type the model and year in the Garage's search box ("2011 328i", "08 535i", "X5 diesel") or the chassis code from the door jamb sticker ("E92"). Cars that aren't supported yet, such as F-series and Minis, get a straight answer instead of an empty list.
- **Start here.** New to Klemme15? The Garage shows three steps: pick your car, plug in the cable (with the right switch position for that car), run the quick test. It goes away once you've scanned a car or hide it.
- **Can't connect?** When the car doesn't answer, Klemme15 now lists what to check, in order: ignition, the cable's switch, the cable check, other programs holding the cable, and the USB port.
- **Plain-English fault meanings on more cars.** The 203 researched E90-family meanings now also appear on the 1 Series, X1 and Z4, and on 5 Series and X5 modules, wherever the car's module is the same version as the E90's (the key module, footwell module, junction box, engine computers and more). A meaning is only borrowed for the same module version and, for engine codes, the same engine; its tip says where it was researched.
- **Likely one cause, not many.** The Faults page now looks across every module and groups faults that belong together: several modules logging low voltage (one flat-battery event), several modules that lost contact with the same module (look at that one first), or faults recorded at the same mileage. Saved reports include it.
- **Gone since the last quick test.** Health over time now also lists the faults that were there last time and aren't now, so you can see that a repair held.
- **Find this part.** When a fault points at a part, a button searches the web for that part for your car (in your browser, with no shop or referral behind it). Test the part first: Klemme15 still tells you how.

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

Earlier versions: [CHANGELOG.md](https://github.com/Expos-AI/Klemme15/blob/main/CHANGELOG.md).
