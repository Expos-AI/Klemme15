Beta candidate. Quick test, faults, readings, live data, guided tests and experimental service functions.

**New in 0.99.33:**
- **Privacy promises checked on every change.** New automatic checks fail the build if Klemme15's code ever names a website that isn't listed in the privacy policy, includes an analytics or crash-reporting library, or sends data to a server. Another check confirms the project ships no BMW software or data files.
- **A parts list with every release.** Each release now comes with a software bill of materials (an SPDX file listing every library Klemme15 is built from), next to the checksums and the signed build provenance. A release also can't be published under the wrong version number any more.
- **Clearer naming.** The part of Klemme15 behind `klemme15 serve` (the phone dashboard on your own network) was called "Telemetry" in the source. It's now "Dashboard", because nothing in Klemme15 reports usage to anyone.

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

Earlier versions: [CHANGELOG.md](https://github.com/Expos-AI/Klemme15/blob/main/CHANGELOG.md).
