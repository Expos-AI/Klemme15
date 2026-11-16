Beta candidate. Quick test, faults, readings, live data, guided tests and experimental service functions.

**New in 0.99.42:**
- **Record a session, play it back without the car.** Tick **Record my sessions** in the Garage (or add `--record file.k15log` on the command line) and Klemme15 saves everything it asks and everything the car answers, with the timing. **Play back a recording...** then opens that file as if the car were connected: the quick test, faults, module details and readings show what the car said that day, and nothing is sent anywhere. It is the most useful thing to attach to a bug report. A recording contains the VIN, so share it only with people you trust. Validated with the demo cars; pending field validation on a real car, and not in EDIABAS mode yet. [How it works](https://github.com/Expos-AI/Klemme15/blob/main/docs/RECORDINGS.md).
- **Community reading maps.** A small JSON file can now describe where values sit in a module's reply, so Klemme15 can show them without BMW files. `klemme15 decode` applies a map to a recording, and `klemme15 mapped` reads a car with one. Maps can only use read requests: Klemme15 refuses any map that would write, drive a part or clear anything. No maps ship yet, and reading a car with one is pending field validation. [How to make one](https://github.com/Expos-AI/Klemme15/blob/main/docs/READING-MAPS.md).
- **Sustained drift on the Analyse page.** With a baseline chosen, Klemme15 now also says whether a value stayed outside the healthy range and from what point in the log, not only how often it left it. This measures distance from the baseline; it is not a forecast of when a part will fail.

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

Earlier versions: [CHANGELOG.md](https://github.com/Expos-AI/Klemme15/blob/main/CHANGELOG.md).
