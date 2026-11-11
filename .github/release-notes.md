Beta candidate. Quick test, faults, readings, live data, guided tests and experimental service functions.

**New in 0.99.35:**
- **One timeline for warnings and faults.** "Your car's story" on the Faults page now also places the faults modules stored, by the mileage each module recorded with them. A dash warning and the faults from the same few kilometres appear as one event, and faults that share a mileage across several modules are grouped and explained (for example, one flat battery instead of five separate defects). The mileage stored with a fault is read in EDIABAS mode with your own BMW files; without it the story shows the dash warnings as before.
- **Snapshot fingerprints.** Every snapshot now has a short fingerprint, like K15-41Y2-X1Q7-7N4N-KQ3E, shown when you save it, when someone opens it, and in the website's viewer. Post the fingerprint next to the file (in a listing or forum post): it matches only if the file is the same, unchanged one. It shows a file wasn't altered after it was saved; it can't prove how the car was scanned.

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

Earlier versions: [CHANGELOG.md](https://github.com/Expos-AI/Klemme15/blob/main/CHANGELOG.md).
