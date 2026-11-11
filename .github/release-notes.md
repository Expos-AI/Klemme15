Beta candidate. Quick test, faults, readings, live data, guided tests and experimental service functions.

**New in 0.99.31:**
- **Stricter voltage rules for every write.** Klemme15 now refuses to write anything to any module (clearing faults, service functions, coding) when the battery is below 10.5 V, not only to the footwell module. A voltage reading that can't be right (0 V, 25 V, a damaged answer) is treated as unreadable instead of trusted, and coding re-checks the voltage right before it writes, after the backup has been read.
- **A pulled cable is handled in one place.** When the cable is unplugged or its port disappears mid-job, Klemme15 releases the port, disconnects, and says what happened, instead of each page failing separately. Reconnecting then works without restarting the app.
- **Tested against hostile data.** New randomized tests feed the K-line and D-CAN code damaged, truncated, repeated and random answers (tens of thousands of cases, plus a simulated car that answers every request wrongly). A damaged answer now always ends in a clear message or "no answer".
- **Recordings survive a crash.** While you record live data, Klemme15 keeps a copy on disk. If the app closes, the laptop battery dies or it crashes before you save, the Live data page offers the recording back next time. The copy is removed once you save or delete it.
- **Faster live data.** The cable code no longer re-copies and re-reads everything it has received each time more bytes arrive, and the fixed pause after every update is gone, so live values refresh faster, most noticeably with several values ticked.

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

Earlier versions: [CHANGELOG.md](https://github.com/Expos-AI/Klemme15/blob/main/CHANGELOG.md).
