Beta candidate. Quick test, faults, readings, live data, guided tests and experimental service functions.

**New in 0.99.34:**
- **Compare two snapshots.** In the Garage, **Compare two snapshots...** takes two .k15 files (before and after a repair, last month and today, or your car and a healthy one) and shows which faults are gone, new, changed or still there, which modules stopped or started answering, whether a module was replaced, and whether coding differs. A module that went silent is never counted as a fixed fault. The result is saved as a page you can share. Command line: `klemme15 diff before.k15 after.k15`.

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

Earlier versions: [CHANGELOG.md](https://github.com/Expos-AI/Klemme15/blob/main/CHANGELOG.md).
