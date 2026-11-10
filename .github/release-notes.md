Beta candidate. Quick test, faults, readings, live data, guided tests and experimental service functions.

**New in 0.99.23:**
- **A real mileage check for used cars.** "Buying this car?" now reads the mileage the modules themselves remember: the engine computer's own count, the cluster's stored mileage, and the mileage each module was last programmed at. It compares them with each other and with the odometer, alongside the mileage on every fault and dash message. A cluster that was wound back or swapped stands out, because the engine computer keeps counting. Every figure is listed in a table and in the buyer's report, and `klemme15 mileage` does the same from the command line. It only ever reads; every module's figures need your BMW files (EDIABAS mode). This is a strong check, not proof: a professional rollback that also resets the engine computer can still pass. Pending field validation.
- **Typed-in jobs follow the same rules.** The Expert page and the command line's `job` command now refuse jobs that would flash a module, read or change keys or the immobiliser, change mileage, or write to the key module (apart from the steering lock counter reset).

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

Earlier versions: [CHANGELOG.md](https://github.com/Expos-AI/Klemme15/blob/main/CHANGELOG.md).
