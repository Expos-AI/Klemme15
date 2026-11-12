Beta candidate. Quick test, faults, readings, live data, guided tests and experimental service functions.

**New in 0.99.38:**
- **EDIABAS mode quick test in seconds, not minutes.** Before BMW's files take over the cable, Klemme15 now asks the car with one broadcast which modules are fitted, and the quick test asks only those. On a 2006 E90 the quick test took two minutes, almost all of it waiting four seconds each on thirty modules the car doesn't have.
- **What's due reads the whole car.** The instrument cluster only keeps some service items (brake fluid, spark plugs, vehicle check, coolant). Engine oil is kept by the engine computer, the brake pads by the stability control and the microfilter by the climate control, so What's due now asks all four. It reads the distance left correctly (the modules store it in steps of 10 km), recognises items by number, and leaves out Germany's two statutory inspections. The cluster's part is checked against a real 2006 E90's answer.
- **Service resets go to the module that keeps the item.** "Reset a service reminder" failed for engine oil and brake pads on a 2006 E90, because the cluster doesn't keep them. There are now three separate functions: **Reset the engine oil service** (engine computer), **Reset the brake pad service** (stability control) and **Reset the cabin microfilter service** (climate control). They are experimental and pending field validation. The cluster's own function now offers only what the cluster keeps; its brake fluid, spark plug and vehicle check resets are validated on that E90. 78 functions in all.
- **Steering column module in EDIABAS mode.** It was skipped when the BMW files don't include the address-named file for it; its named file is now tried as well.
- **Cleaner fault details.** Figures a module doesn't keep (shown as -1) are left out, and a background entry a module lists many times over, identical each time, is shown once with a count.
- **Service catalog brought up to date.** Battery registration, adaptation resets and other engine functions now list the engine computer versions added with the newer car models.

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

Earlier versions: [CHANGELOG.md](https://github.com/Expos-AI/Klemme15/blob/main/CHANGELOG.md).
