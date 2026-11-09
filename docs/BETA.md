# Klemme15 beta: tester guide

Thank you for testing Klemme15 before 1.0. **Got an E36?** Follow the [E36 tester guide](E36-BETA.md) instead. **Got a 2004-2006 X3 (E83)?** X3 support is in development too: in addition to the tests below, run **Find other modules** on the Vehicle page and include the result, which maps your X3's module addresses. Use Klemme15 on your car and cable the way you would use any diagnostic tool, and report what happened.

Everything Klemme15 does by default is read-only. Anything that writes to the car (clearing faults, coding, service functions) asks first, and service functions stay behind the **Experimental** switch.

## Before you start

1. Install the latest release (on Windows with Scoop: `scoop bucket add klemme15 https://github.com/Expos-AI/Klemme15` then `scoop install klemme15`; or the zip for your system from [Releases](https://github.com/Expos-AI/Klemme15/releases)). Check the download with `SHA256SUMS.txt` if you like ([how](../SECURITY.md#verifying-your-download)).
2. Plug in your cable, pick your car in the Garage, press **Connect to my car**, choose the cable's port and press **Check cable**. Fix anything it flags (latency, port in use), and **note what it said about your cable** for your report.
3. Put a battery charger on the car for anything longer than a quick test.

## What to test

Do as many as you can, in any order. Each is useful on its own.

| # | Test | Where | What we want to know |
|---|---|---|---|
| 1 | **Quick test** | Vehicle → Run quick test | Did every fitted module answer? How long did it take? Any module missing or wrong? |
| 2 | **Faults** | Faults page | Do the meanings make sense for your car? Anything clearly wrong or missing? |
| 3 | **Ghost Hunter on your commute** | Ghost Hunter → tick *Catch a voltage dip* (or anything that fits your car) → **Start watching**, then drive 30 to 45 minutes | Did the app stay responsive? Did the laptop stay awake? Did the *Right now* list make sense? Any captures, and do they look right? |
| 4 | **Live data and Analyse** | Live data → record a few minutes → Analyse | Smooth, or stuttering? Do the values match the dash? |
| 5 | **Guided tests** | Tests → fuel trim test, cylinder roughness, water pump health | Did each finish with a verdict that makes sense? |
| 6 | **Battery drain finder** | Faults → Battery going flat? | Did it find your car's own clues? If you measured a drain, how did the hunt go? |
| 7 | **Cable pulled on purpose** | During a long test, unplug the cable, wait 10 s, plug it back in and reconnect | Did Klemme15 say clearly what happened and what to do? |

Please **don't** run experimental service functions or coding on a car you depend on unless you know how to recover it with other tools.

## How to report

1. On the Faults page, press **Share snapshot**, tick **the traffic log**, and save the `.k15` file. It contains your car's modules, faults, live data and the traffic between Klemme15 and the car. The VIN is removed; in the traffic log, a VIN split across two messages can slip through, so skim it if that matters to you.
2. For a Ghost Hunter drive, the captures are in the Ghost Hunter page's list: open one and share it the same way, or attach the `.k15` files from the captures folder.
3. Put the `.k15` files in a `.zip` (GitHub only accepts certain file types), open a [test report](https://github.com/Expos-AI/Klemme15/issues/new?template=beta-report.yml), fill it in and attach the zip. One report per car and session is perfect.

Something serious (Klemme15 wrote something it shouldn't have, or a car misbehaved after using it)? Report it privately instead: [security policy](../SECURITY.md).

## What happens with your reports

Every report is read. Fixes go into the next release, and what your car confirms (a module, a test, a service function) is marked as confirmed for everyone with the same car.
