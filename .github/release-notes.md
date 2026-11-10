Beta candidate. Quick test, faults, readings, live data, guided tests and experimental service functions.

**New in 0.99.29:**
- **What's due for service.** On the Service page, **What's due for service?** reads the instrument cluster's own service list (engine oil, brake pads, brake fluid, microfilter, spark plugs, vehicle check and more) and shows how much of each interval is left, most urgent first. It only reads. For cars with Condition Based Service (3 Series E90 family, 1 Series, 5 Series E60, X5 E70, X1, Z4 E89), in EDIABAS mode with your own BMW files; pending field validation.
- **Open a snapshot in a browser.** Someone you sent a .k15 file to no longer needs Klemme15 installed: the [snapshot viewer](https://expos-ai.github.io/Klemme15/viewer.html) on the website shows the modules, faults, dash messages, the car's story, test results and live data. The file is read on their own device and never uploaded, and it's checked against its checksums like in the app.

**Downloads:** unzip and run Klemme15 (Klemme15.exe on Windows). The command-line tool is in the cli folder. EDIABAS mode (your own BMW files) is included in every build.

Earlier versions: [CHANGELOG.md](https://github.com/Expos-AI/Klemme15/blob/main/CHANGELOG.md).
