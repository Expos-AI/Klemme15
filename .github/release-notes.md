Beta candidate. Quick test, faults, readings, live data, guided tests and experimental service functions.

**New in 0.99.36:**
- **EDIABAS mode starts again.** Connecting with your own BMW files failed straight away with "The type initializer for 'EdiabasLib.EdiabasNet' threw an exception". The library that runs BMW's module programs needs a Windows text encoding that Klemme15 didn't switch on. It does now, and every build is checked to make sure EDIABAS mode actually starts, not only that it compiles. If EDIABAS mode ever fails to start again, the message now says why.

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

Earlier versions: [CHANGELOG.md](https://github.com/Expos-AI/Klemme15/blob/main/CHANGELOG.md).
