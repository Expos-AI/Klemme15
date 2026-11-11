Beta candidate. Quick test, faults, readings, live data, guided tests and experimental service functions.

**New in 0.99.37:**
- **EDIABAS mode when a car doesn't answer the address-named files.** On a 2006 E90, only the engine and one other module answered in EDIABAS mode: they answered through BMW's named group files (D_MOTOR, D_LDM), while the address-named ones Klemme15 asked first (D_0012, D_0040) stayed silent. Every module now lists its named group as well (247 modules across the E90, 1 Series, 5 Series, X5, X1 and Z4 families), Klemme15 tries both, and once a car shows which kind it answers through, the rest of the quick test asks only that kind.
- **Connecting in EDIABAS mode no longer gets stuck.** When the first module was slow to answer, the Garage stayed on "Connecting...", the header said "Not connected" and the app didn't move to the Vehicle page, although the connection worked. The connection is now marked as up as soon as the cable answers.
- **No more repeated waits on a silent module.** After a module doesn't answer, Klemme15 no longer asks the same file again for the next 45 seconds, and jobs sent to a group file go straight to the module version that answered. Reading the battery voltage before a service function no longer waits several seconds for nothing.
- **A hint when few modules answer.** In EDIABAS mode, a quick test with very few answers now points at the most common cause, the cable's latency timer.

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

Earlier versions: [CHANGELOG.md](https://github.com/Expos-AI/Klemme15/blob/main/CHANGELOG.md).
