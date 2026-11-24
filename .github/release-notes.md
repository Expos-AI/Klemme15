Beta candidate. Quick test, faults, readings, live data, guided tests and experimental service functions.

**New in 0.99.44:**
- When a module says no, Klemme15 now says why. A service function that a module refuses used to end with a code such as `ERROR_ECU_GENERAL_REJECT`. Now the result says in plain words what the module meant and what to do, keeps the module's own answer underneath, and, when the car's state is the likely reason, reads that module's stored faults and lists them. **Check the car** also mentions faults the module had at the last quick test.
- xDrive transfer case relearn: the conditions are spelled out. The function now says, in its *Before you start* list, that the transfer case refuses while it's in its fallback mode (some faults put it there), that it needs to hear the rest of the car, and that BMW's job deletes the clutch calibration before it tests, so a refusal shouldn't be retried over and over. Still experimental, pending field validation. The X1 (E84) xDrive's transfer case now gets this function too: BMW's job for it does the same thing.
- EDIABAS mode: clearer when it can't reach the car. A module whose BMW file is missing from your folder now says so, instead of looking like a module that didn't answer. If the engine doesn't answer, Klemme15 asks again without its speed-up shortcuts, and when a quick test in EDIABAS mode reaches only part of the car, it says what to do (run it without EDIABAS mode, and send the log).
- A function that needs your BMW files says why: it's BMW's own procedure, which only exists in those files.
- If Klemme15 ever closes on an error, it now writes what happened to a small file and tells you where it is on the next start, so you can attach it to a bug report. No car data is in it.

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

Earlier versions: [CHANGELOG.md](https://github.com/Expos-AI/Klemme15/blob/main/CHANGELOG.md).
