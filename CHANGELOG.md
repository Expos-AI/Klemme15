# Changelog

Every Klemme15 release, newest first. The downloads are on the [Releases page](https://github.com/Expos-AI/Klemme15/releases).

## 0.99.38

- **EDIABAS mode quick test in seconds, not minutes.** Before BMW's files take over the cable, Klemme15 now asks the car with one broadcast which modules are fitted, and the quick test asks only those. On a 2006 E90 the quick test took two minutes, almost all of it waiting four seconds each on thirty modules the car doesn't have.
- **What's due reads the whole car.** The instrument cluster only keeps some service items (brake fluid, spark plugs, vehicle check, coolant). Engine oil is kept by the engine computer, the brake pads by the stability control and the microfilter by the climate control, so What's due now asks all four. It reads the distance left correctly (the modules store it in steps of 10 km), recognises items by number, and leaves out Germany's two statutory inspections. The cluster's part is checked against a real 2006 E90's answer.
- **Service resets go to the module that keeps the item.** "Reset a service reminder" failed for engine oil and brake pads on a 2006 E90, because the cluster doesn't keep them. There are now three separate functions: **Reset the engine oil service** (engine computer), **Reset the brake pad service** (stability control) and **Reset the cabin microfilter service** (climate control). They are experimental and pending field validation. The cluster's own function now offers only what the cluster keeps; its brake fluid, spark plug and vehicle check resets are validated on that E90. 78 functions in all.
- **Steering column module in EDIABAS mode.** It was skipped when the BMW files don't include the address-named file for it; its named file is now tried as well.
- **Cleaner fault details.** Figures a module doesn't keep (shown as -1) are left out, and a background entry a module lists many times over, identical each time, is shown once with a count.
- **Service catalog brought up to date.** Battery registration, adaptation resets and other engine functions now list the engine computer versions added with the newer car models.

## 0.99.37

- **EDIABAS mode when a car doesn't answer the address-named files.** On a 2006 E90, only the engine and one other module answered in EDIABAS mode: they answered through BMW's named group files (D_MOTOR, D_LDM), while the address-named ones Klemme15 asked first (D_0012, D_0040) stayed silent. Every module now lists its named group as well (247 modules across the E90, 1 Series, 5 Series, X5, X1 and Z4 families), Klemme15 tries both, and once a car shows which kind it answers through, the rest of the quick test asks only that kind.
- **Connecting in EDIABAS mode no longer gets stuck.** When the first module was slow to answer, the Garage stayed on "Connecting...", the header said "Not connected" and the app didn't move to the Vehicle page, although the connection worked. The connection is now marked as up as soon as the cable answers.
- **No more repeated waits on a silent module.** After a module doesn't answer, Klemme15 no longer asks the same file again for the next 45 seconds, and jobs sent to a group file go straight to the module version that answered. Reading the battery voltage before a service function no longer waits several seconds for nothing.
- **A hint when few modules answer.** In EDIABAS mode, a quick test with very few answers now points at the most common cause, the cable's latency timer.

## 0.99.36

- **EDIABAS mode starts again.** Connecting with your own BMW files failed straight away with "The type initializer for 'EdiabasLib.EdiabasNet' threw an exception". The library that runs BMW's module programs needs a Windows text encoding that Klemme15 didn't switch on. It does now, and every build is checked to make sure EDIABAS mode actually starts, not only that it compiles. If EDIABAS mode ever fails to start again, the message now says why.

## 0.99.35

- **One timeline for warnings and faults.** "Your car's story" on the Faults page now also places the faults modules stored, by the mileage each module recorded with them. A dash warning and the faults from the same few kilometres appear as one event, and faults that share a mileage across several modules are grouped and explained (for example, one flat battery instead of five separate defects). The mileage stored with a fault is read in EDIABAS mode with your own BMW files; without it the story shows the dash warnings as before.
- **Snapshot fingerprints.** Every snapshot now has a short fingerprint, like K15-41Y2-X1Q7-7N4N-KQ3E, shown when you save it, when someone opens it, and in the website's viewer. Post the fingerprint next to the file (in a listing or forum post): it matches only if the file is the same, unchanged one. It shows a file wasn't altered after it was saved; it can't prove how the car was scanned.

## 0.99.34

- **Compare two snapshots.** In the Garage, **Compare two snapshots...** takes two .k15 files (before and after a repair, last month and today, or your car and a healthy one) and shows which faults are gone, new, changed or still there, which modules stopped or started answering, whether a module was replaced, and whether coding differs. A module that went silent is never counted as a fixed fault. The result is saved as a page you can share. Command line: `klemme15 diff before.k15 after.k15`.

## 0.99.33

- **Privacy promises checked on every change.** New automatic checks fail the build if Klemme15's code ever names a website that isn't listed in the privacy policy, includes an analytics or crash-reporting library, or sends data to a server. Another check confirms the project ships no BMW software or data files.
- **A parts list with every release.** Each release now comes with a software bill of materials (an SPDX file listing every library Klemme15 is built from), next to the checksums and the signed build provenance. A release also can't be published under the wrong version number any more.
- **Clearer naming.** The part of Klemme15 behind `klemme15 serve` (the phone dashboard on your own network) was called "Telemetry" in the source. It's now "Dashboard", because nothing in Klemme15 reports usage to anyone.

## 0.99.32

- **Garage mode.** A third theme for working in daylight: black on white, heavy lines and larger small print, so the screen stays readable with the laptop on a fender or in the sun. The theme button at the bottom left now steps through dark, light and Garage mode.

## 0.99.31

- **Stricter voltage rules for every write.** Klemme15 now refuses to write anything to any module (clearing faults, service functions, coding) when the battery is below 10.5 V, not only to the footwell module. A voltage reading that can't be right (0 V, 25 V, a damaged answer) is treated as unreadable instead of trusted, and coding re-checks the voltage right before it writes, after the backup has been read.
- **A pulled cable is handled in one place.** When the cable is unplugged or its port disappears mid-job, Klemme15 releases the port, disconnects, and says what happened, instead of each page failing separately. Reconnecting then works without restarting the app.
- **Tested against hostile data.** New randomized tests feed the K-line and D-CAN code damaged, truncated, repeated and random answers (tens of thousands of cases, plus a simulated car that answers every request wrongly). A damaged answer now always ends in a clear message or "no answer".
- **Recordings survive a crash.** While you record live data, Klemme15 keeps a copy on disk. If the app closes, the laptop battery dies or it crashes before you save, the Live data page offers the recording back next time. The copy is removed once you save or delete it.
- **Faster live data.** The cable code no longer re-copies and re-reads everything it has received each time more bytes arrive, and the fixed pause after every update is gone, so live values refresh faster, most noticeably with several values ticked.

## 0.99.30

- **Every model visible on smaller screens.** In the Garage, the car families are now compact buttons instead of one full-width row each, with a heading over the model list. On a laptop-sized window the 13 families used to push the model list off the bottom of the screen, so some owners only saw the first model of their car (for example the E46 323i, not the M3). Long model names such as "335xi / 335i xDrive" now wrap instead of being cut off.

## 0.99.29

- **What's due for service.** On the Service page, **What's due for service?** reads the instrument cluster's own service list (engine oil, brake pads, brake fluid, microfilter, spark plugs, vehicle check and more) and shows how much of each interval is left, most urgent first. It only reads. For cars with Condition Based Service (3 Series E90 family, 1 Series, 5 Series E60, X5 E70, X1, Z4 E89), in EDIABAS mode with your own BMW files; pending field validation.
- **Open a snapshot in a browser.** Someone you sent a .k15 file to no longer needs Klemme15 installed: the [snapshot viewer](https://expos-ai.github.io/Klemme15/viewer.html) on the website shows the modules, faults, dash messages, the car's story, test results and live data. The file is read on their own device and never uploaded, and it's checked against its checksums like in the app.

## 0.99.28

- **Start from a symptom.** Don't have a code, or don't know where to begin? The Tests page now starts with what the car does: won't start, rough idle, check engine light, misfire, loss of power, overheating, a battery that goes flat, ABS or airbag lights, lights and windows that stopped working, a problem that comes and goes, and more. Each one shows what your car already reports about it, then what to do in order, with a button to the right test, guide or page.
- **Warm-up check.** A new diagnostic chain for an engine that runs cold or sets a thermostat code (P0128, P0125): it reads the coolant temperature at idle and at 2,000 rpm after a drive and says whether a thermostat stuck open is likely, and how to rule out the sensor.

## 0.99.27

- **Find your car without knowing its chassis code.** Type the model and year in the Garage's search box ("2011 328i", "08 535i", "X5 diesel") or the chassis code from the door jamb sticker ("E92"). Cars that aren't supported yet, such as F-series and Minis, get a straight answer instead of an empty list.
- **Start here.** New to Klemme15? The Garage shows three steps: pick your car, plug in the cable (with the right switch position for that car), run the quick test. It goes away once you've scanned a car or hide it.
- **Can't connect?** When the car doesn't answer, Klemme15 now lists what to check, in order: ignition, the cable's switch, the cable check, other programs holding the cable, and the USB port.
- **Plain-English fault meanings on more cars.** The 203 researched E90-family meanings now also appear on the 1 Series, X1 and Z4, and on 5 Series and X5 modules, wherever the car's module is the same version as the E90's (the key module, footwell module, junction box, engine computers and more). A meaning is only borrowed for the same module version and, for engine codes, the same engine; its tip says where it was researched.
- **Likely one cause, not many.** The Faults page now looks across every module and groups faults that belong together: several modules logging low voltage (one flat-battery event), several modules that lost contact with the same module (look at that one first), or faults recorded at the same mileage. Saved reports include it.
- **Gone since the last quick test.** Health over time now also lists the faults that were there last time and aren't now, so you can see that a repair held.
- **Find this part.** When a fault points at a part, a button searches the web for that part for your car (in your browser, with no shop or referral behind it). Test the part first: Klemme15 still tells you how.

## 0.99.26

- **Miles and °F.** Choose your units in the Garage: km and °C, or miles and °F (also mph and psi). Klemme15 starts with the units your computer's region uses. It applies to live data, readings, the car drawing, mileage records, tests and reports; recordings, snapshots and baselines stay metric so they can be shared.
- **What each module does, in plain words.** Hover a module on the car drawing, or select it, to see what it's for: "Footwell module: exterior and interior lights, front windows and mirrors."
- **No more overlapping module names** on cars with 40 to 50 modules: markers that shared a spot are nudged apart.

## 0.99.25

- **335xi / 335i xDrive** is now its own model in the Garage (E90/E92, N54, with the xDrive transfer case module), so owners don't have to pick the rear-wheel-drive 335i.

## 0.99.24

- **Check for updates from inside the app.** Press the arrow button at the bottom of the sidebar: if there's a newer version, a banner offers **Download** (the right file for your computer), **What's new** and **Skip this version**. Klemme15 also asks once whether to check by itself when it starts, at most once a day. It only reads a small version file from the Klemme15 website and sends nothing about you or your car; it never downloads or installs anything on its own.

## 0.99.23

- **A real mileage check for used cars.** "Buying this car?" now reads the mileage the modules themselves remember: the engine computer's own count, the cluster's stored mileage, and the mileage each module was last programmed at. It compares them with each other and with the odometer, alongside the mileage on every fault and dash message. A cluster that was wound back or swapped stands out, because the engine computer keeps counting. Every figure is listed in a table and in the buyer's report, and `klemme15 mileage` does the same from the command line. It only ever reads; every module's figures need your BMW files (EDIABAS mode). This is a strong check, not proof: a professional rollback that also resets the engine computer can still pass. Pending field validation.
- **Typed-in jobs follow the same rules.** The Expert page and the command line's `job` command now refuse jobs that would flash a module, read or change keys or the immobiliser, change mileage, or write to the key module (apart from the steering lock counter reset).

## 0.99.22

- **Downloads from the website no longer depend on GitHub's API.** Visitors on a busy mobile network could see "too many requests", and the backup link went to a page that doesn't exist yet for pre-release versions. The download buttons now link straight to the file for your computer, from a version file on the website that each release updates. Macs get the Apple silicon download with a link to the Intel one, and phones are told to open the page on a laptop.
- **Shorter release pages.** Each release now lists only its own changes, so the downloads are right below them. Every earlier version's notes are in [CHANGELOG.md](https://github.com/Expos-AI/Klemme15/blob/main/CHANGELOG.md).

## 0.99.21

Smaller windows and clearer wording.

- **Smaller windows.** The About window's buttons wrap instead of running off the edge, the sidebar shows a scroll bar when not every page fits, and the Garage, Analyse and Readings pages no longer cut text off in a window that isn't maximised. Every page is now checked at 1024 x 640 on each change.
- **Clearer status wording.** Cars, modules and functions are described as *validated* (tested on a vehicle with Klemme15) or *pending field validation*, consistently in the app, the website and the documentation. The E36 and X3 are labelled *in development*.
- **Fault verdicts read more precisely**, for example "Stored, not active. Clear it and drive for a few days; repair it only if it returns."

## 0.99.20

More cars.

- **M cars and missing models.** The E9x M3, E46 M3, E60 M5 and M6, 1 Series M Coupe, X5 M and X6 M are now models of their own, along with the 335is, the 335d, the 2011-on X5 xDrive35i and xDrive50i, and the 3 Series diesels sold outside North America.
- **New families:** the X1 E84 (2009-2015, D-CAN, built on the E90's platform) and the Z4 E85/E86 (2003-2008, K-line). The **X3 E83** (2004-2006, K-line) joins as a beta. All of them are built from BMW's module data and not yet confirmed on a real car: please [report how it goes](https://github.com/Expos-AI/Klemme15/blob/main/docs/BETA.md).
- **Tips and tests that fit your engine.** A diesel no longer gets the fuel trim, VANOS or idle tests, and the water pump tests are only offered on engines with an electric pump (N51-N55). The E9x petrol engines' misfire and power management meanings are now limited to those engines, so on an S65 or a diesel they say they were recorded for other engines.

## 0.99.19

- **Tips go through PayPal** ([paypal.me/KindricC](https://paypal.me/KindricC)) instead of Ko-fi. Same buttons, same deal: Klemme15 stays free with nothing locked.

## 0.99.18

- **Support Klemme15, if you like.** Klemme15 stays free with nothing locked. If it helped, you can now leave a tip: from the cup button in the sidebar, the About window, and a quiet line under a finished test's verdict. No pop-ups, no reminders.

## 0.99.17

The Vehicle page on smaller screens.

- **The car drawing fits the window**, so its legend and the live values button no longer fall off the bottom when there are notices above it.
- **The side panel scrolls as one.** Dash messages and faults used to share it in two boxes with their own scroll bars, which could squeeze the fault list to a sliver. Now it's one list you scroll.
- **No text under the scroll bar.** Long dash message tips and labels like "Advisory" no longer run under it or get cut off, here and on the other pages.
- **Report links sit under the scan summary**, so the summary reads on one line instead of a narrow column.
- **No stray colon** after "high confidence · 5 sources" in the side panel, where the source links aren't shown.

## 0.99.16

- **E36 beta (1996-1999).** The 3 Series E36 is in the Garage as a beta: 328i/328is, 323is/323i, 318i/318is, 318ti and M3. The engine (fault codes, live data, the fuel trim test) uses standard OBD-II, so it's the part most likely to work. The other modules use addresses nobody has confirmed on an E36 yet, so the Garage says so plainly, and **Find other modules** plus a saved snapshot show where they really are. Cars from before 1996 aren't supported yet. Got an E36? The [E36 tester guide](https://github.com/Expos-AI/Klemme15/blob/main/docs/E36-BETA.md) takes about 30 minutes.

## 0.99.15

- **Each release now carries its signed build provenance as a file** (`Klemme15-v0.99.15.intoto.jsonl`), next to the checksums, so anyone can check a download was built by this project's workflow from this commit, even offline. See [SECURITY.md](https://github.com/Expos-AI/Klemme15/blob/main/SECURITY.md#verifying-your-download).

## 0.99.14

A security and legal pass.

- **The BMW fault database is opt-in, with a clear notice.** Before the first download (in the app and with `klemme15 db download`), Klemme15 says what it is: data from the independent bmw-unified-db project, derived from BMW's own and, as that project says, copyrighted by BMW. It downloads only after you confirm, and Klemme15 works without it.
- **Safety, legal and transparency** now has a short, sourced summary of the U.S. rules Klemme15 is built around (the DMCA repair exemption, emissions, mileage, trademarks).
- **Saved reports only link web addresses**, so a crafted snapshot can't put a `javascript:` or `file:` link into a report you save and open.
- **Project security:** OpenSSF Scorecard checks the project weekly (badge in the README), every workflow action is pinned to an exact commit (Dependabot moves the pins forward), and every change has a code owner.

## 0.99.13

- **Safety, legal and transparency, in the app.** The shield button in the sidebar opens the whole page: no warranty and your own risk, working on a car safely, what experimental means, what Klemme15 never does, copyright and BMW's software, trademarks, how good the fault meanings are, privacy, and how Klemme15 is made (AI coding tools help write parts of it). The same text is [docs/LEGAL.md](https://github.com/Expos-AI/Klemme15/blob/main/docs/LEGAL.md).
- **Before you start:** the first time Klemme15 opens, it shows the short version and asks you to tick that you understand you use it at your own risk. It asks again only if the notice changes. It doesn't add conditions to the GPL.

## 0.99.12

- **Command line:** in the quick test list, a module with only a background (Shadow / Info memory) entry says "1 info entry" instead of "1 fault(s)" next to OK.

## 0.99.11

- **Mac:** the zip no longer carries a hidden `._EdiabasLib.config` file inside `Klemme15.app`, which could make macOS call the app damaged when the zip was opened with something other than Archive Utility.

## 0.99.10

A polish pass before wider testing.

- **Mac: a real app.** The Mac download is now `Klemme15.app` with its icon: drag it to Applications and open it. The README and website explain the one-time "Open Anyway" step (and Windows' "More info → Run anyway") for apps without a paid certificate.
- **The website's download button picks your file** on Windows and Linux, and says which system it's for.
- **BMW's own fault texts, offered where you need them.** When codes on the Faults page only have a general meaning, the page offers the free fault database right there, and reads the faults again once it's installed so they come back with BMW's text.
- **Cleaner fault cards.** The common-cause tip no longer looks like a link, "Fix now:" and "Check it:" lost a stray space, and the sources line reads "Community meaning · high confidence · 3 sources:".
- **The car drawing:** a fault label that would print over a module's name moves to the other side of its dot, and the footwell module notice sits above the drawing. When the footwell module's type isn't confirmed, the notice is calm; only a confirmed FRM3 gets the amber warning (the strict voltage rules apply either way).
- **Readings line up:** values of one reading with the same unit share their decimals (0.48 and 5.00, not 5).
- **Demo car:** the Service page only offers what fits the demo car's engine and modules (no diesel particulate filter on a petrol 335i). Long service names and engine descriptions wrap instead of being cut off.
- **Cable port problems say what to do.** A port another program is using (INPA, ISTA, NCS Expert) says so; on Linux, a port you're not allowed to open says which group to join; an unplugged cable says to plug it back in and pick the port again. On a Mac only the `cu.` port is listed, since the matching `tty.` one would hang.
- For contributors: the reel tool records three more scenes (an E46 quick test, a diagnostic chain asking its question, a drive against a healthy baseline), writes `timelines.txt` with what each clip shows from which second, and makes `labeled/` clips with only the small print.

## 0.99.9

- **Rough idle and air conditioning check:** the dip when the compressor engages only counts idle readings, so touching the pedal during the test can't be mistaken for a chug.
- **Demo car:** during the idle check the demo driver stays off the pedal, switches the air conditioning on partway through, and the engine dips a little as the compressor engages, so the test shows what it's for.
- For contributors: `tools/Klemme15.Reel` records the app with the demo cars and composes vertical clips of every feature (run the **reel** workflow).

## 0.99.8

A full pass so no one is left stuck, checked against a real E90's traffic replayed through every function that talks to the car.

- **No error closes the app any more.** Saving to a folder you can't write to, a file open in another program, a damaged backup or capture: you get a plain message with what to do, in a banner you can dismiss.
- **Greyed-out buttons say why.** While the Ghost Hunter watches, live data records, a quick test or a service function runs, a strip at the top says what's using the cable, with a button to go there.
- **Nothing can pull the connection out from under a job.** Disconnect, switching cars and opening a snapshot or capture wait while a service function, coding write or part test is running.
- **Clearing one module's faults updates every page** (headline, car drawing, snapshot, buying check), and "Read again" carries on when one module doesn't answer.
- **Error messages in plain words**: "the module refused (it doesn't offer this)" and what to try, instead of protocol codes.
- Live data keeps a recording if you cancel the save dialog. Coding's Compare and Restore can't crash, and a file that isn't a backup says so. "Show on car" only appears for faults the drawing can show. The Readings, Service and Live data pages say the right thing in a snapshot and without BMW files.
- **Install:** winget was never published, so the README and website now show Scoop and the zip downloads only. Corrected claims on the website: all 75 service functions are experimental, Valvetronic learning without BMW files is N52 only, and the Raspberry Pi needs a build from source.

## 0.99.7

- **New guided test: Rough idle and air conditioning check.** For an idle that's rough, bounces or chugs when the air conditioning kicks in. It watches 20 seconds of idle with the air conditioning off, then catches the moment the compressor engages, then 20 seconds with it on. It compares engine speed with the idle speed the engine computer is aiming for, checks every cylinder and the mixture, and says what it points to: an air leak, a cylinder that only falters under load, or an engine that's fine (then worn engine mounts are the usual cause of the shake). Uses the engine computer's own idle target and air conditioning status where it reports them (MSV70 and others).
- **New Ghost Hunter watch: Catch a chug at idle.** Saves the moment engine speed dips below 600 rpm with the car stopped, with the air conditioning and compressor state in every capture.
- The Ghost Hunter's custom rules can use the idle target speed and the air conditioning and compressor state.

## 0.99.6

- **The Ghost Hunter explains itself.** The page starts with three steps, each watch says how to catch it ("let it idle warm, at lights or in the driveway"), and while watching a **Right now** list shows where each watch stands: "Waiting for coolant temperature above 70 °C (now 58 °C)", "Waiting for vehicle speed above 3 km/h", or "Ready. Battery voltage is 14.6 V, caught if it goes below 11.5 V".
- **Every watch checked against a real car.** 77 seconds of readings from a healthy E90 325i (N52) set off none of the eight watches, and each one fires when its fault is put into those same readings. These checks now run on every change.

## 0.99.5

- **The quick test waits longer for modules behind a gateway.** On a real E90 the steering column switches were reported as not answering, although other tools reach them on the same car. Modules that don't answer the car-wide wake-up call were given only 100 ms; they now get 300 ms. A quick test takes a few seconds longer, and "Find other modules" about a minute.

## 0.99.4

Fixes from the first test on a real E90 325i (N52, MSV70 engine computer, D-CAN cable).

- **No more false misfires.** On a smooth idle, single per-cylinder roughness readings swing widely, on a different cylinder each time. The Ghost Hunter's misfire watch now averages the last 3 seconds before it judges, and the **Cylinder roughness** test compares each cylinder with the average of the others. Before, both could call a healthy cylinder "much rougher". A cylinder that really is rough still stands out.
- **"Clear all" says what happened.** After clearing, Klemme15 reads the car again and tells you which faults came straight back (their cause is still there) and which modules kept their background entries (many modules don't allow those to be cleared, which is normal).
- **Dash messages count as "keeps showing" only across different days.** Several quick tests in one sitting all see the same dash, so they no longer fill the health history with "parking brake applied keeps showing".
- Reports count background entries separately from faults ("0 (+9 background)") and say "1 fault", not "1 faults".
- The car's story says "the car's battery" when the clock needed setting, so it can't be confused with the key's battery.

## 0.99.3

- **Live data cards stay tidy.** When the engine computer doesn't report a value, the card shows a dash with the reason in small print underneath, instead of the reason in large type running out of the card.
- **Fault codes on the car drawing no longer print over each other.** When two fault locations sit side by side (neighbouring cylinders), their codes are shown together next to the right-hand one. Clicking either dot still shows its own faults.
- **Close snapshot** button on the snapshot banner, which takes you back to the Garage.

## 0.99.2

- **The steering lock counter reset is back** (Service > Steering lock: clear the fault counters). On early E90-family cars with a key slot, full counters bring up the red steering wheel and can stop the car starting. Charge the battery, then clear the counters in the key module and the lock. It only clears event counters: it never reads or writes keys or immobiliser data. It was removed by mistake in 0.99.1. The no-start check points to it again. 75 service functions.

## 0.99.1

- **Clearer everywhere.** Every page, message, report and guide was reviewed for plain, precise wording and checked against what the app really does. The website and README no longer claim more than has been confirmed on a real car.
- **Steering lock counter reset removed.** It cleared counters inside the key module (CAS), which Klemme15 never writes. The no-start check now explains the steering lock problem and points to a workshop instead. 74 service functions.
- **Cable check on Linux now identifies the cable** (it couldn't read the USB details before), and picks the K+DCAN cable's port by itself. On macOS an FTDI chip is now reported as "probably", since the port name alone can't prove it. The check no longer blocks the window, and it lets go of the port properly before connecting.
- **Interrupted jobs are never forgotten.** Advice for a job that was cut off now survives starting another job and restarting Klemme15. A coding write or restore that fails partway is now treated as interrupted, so the advice to restore the backup always shows.
- **One conversation on the cable at a time.** While a service function, test, coding write or live data runs, the quick test, clearing faults, live values on the car drawing and other jobs wait, so a background read can't disturb a 13-minute bleed.
- Live data on K-line cars no longer loses values when one of the engine computer's "supported values" replies is dropped.
- Cars without a full VIN that give part of it are now told apart even when two cars share the same modules.
- The drain finder flags a low battery only when it's clearly low, since the quick test measures with the ignition on.
- New guides: [the cable](https://github.com/Expos-AI/Klemme15/blob/main/docs/CABLE.md) and [the command line](https://github.com/Expos-AI/Klemme15/blob/main/docs/COMMAND-LINE.md). A bug report form and a pull request template on GitHub.

## 0.99.0

- **Cable check before connecting.** Press **Check cable** in the Garage (it also runs by itself before every connection). Klemme15 looks at what your computer knows about the cable and tells you, in plain words: whether it's a genuine FTDI chip or shows the known signs of a clone, whether its latency timer is slowing things down ("Your cable latency is set to 16 ms: set it to 1 ms", with exactly where to change it), and whether INPA or another program still has the port. If no module answers, Klemme15 now explains the usual causes, including cheap cables with pins 7 and 8 bridged that can't work on D-CAN cars. Latency can be read on Windows and Linux; macOS doesn't allow it.
- **Recovery when a job is cut off.** If the cable comes out, the laptop sleeps or Windows restarts in the middle of a service function, a test that switches a part on or a coding write, Klemme15 remembers it, even after a restart, and shows exactly what to do next. Parts Klemme15 switched on (the water pump) are switched off again as soon as you reconnect, and an interrupted coding write points you to the backup taken just before it. While a job runs, and while the Ghost Hunter watches a drive, Klemme15 keeps the computer from going to sleep.
- **Share your engine's baseline in one click.** On the Analyse page, **Share as a healthy baseline** checks a log of a warm drive (idle and a range of engine speeds past 3,000 rpm), saves only the engine values (no VIN, no date, no location) and opens a form on GitHub. A check there builds your engine's community baseline with the same maths Klemme15 uses and prepares it for the next release. N54, M57, N47 and every other engine can now get one from their owners ([how](https://github.com/Expos-AI/Klemme15/blob/main/docs/BASELINES.md#share-yours)).
- **Engine codes with size and fuel.** Baselines tell a 2.5 L N52 (N52B25) from a 3.0 L one (N52B30), and diesels (M57D30) from petrol engines.
- **Beta testers wanted.** Got an E46, E60 or E90-family car and a cable? The [tester guide](https://github.com/Expos-AI/Klemme15/blob/main/docs/BETA.md) says what to try, and the new Beta report form collects what you find.

## 0.23.0

- **Battery drain finder.** Battery flat after the car sits? Open **Battery going flat?** on the Faults page. Klemme15 lists what the car itself recorded: the engine computer's record of too much current while parked, battery sensor faults, the footwell module switching lights off to save the battery, "Charge the battery" warnings from the dash history, a resting voltage that keeps falling. Type in the parked current your meter shows and it tells you whether that's normal (up to about 50 mA once asleep) and how many days until the car won't start. Then it walks you through the hunt: make the car think it's closed, put the meter in without waking it, wait for it to sleep, and pull fuses one at a time until the current drops. Read-only.
- **E46, E39, E53 and E38: health history and saved test results.** Many of these engine computers don't report the VIN, so the health history never started. Cars without a full VIN are now told apart by a one-way hash of their engine computer's and cluster's identification, so trends, *Your cars* and test results work on them too.
- **Faster live data on K-line cars.** Klemme15 now asks the engine computer which values it supports and only reads those, instead of waiting out a timeout for each one it doesn't have.
- On K-line cars connected without BMW files, tests that need BMW's own engine values (VANOS, cylinder roughness, the water pump) now say so up front.

## 0.22.1

- **Up-to-date foundations.** The app's UI framework moves to Avalonia 11.3 (bug fixes and smoother rendering on all three systems), the optional full-database import uses the latest patched SQLite library, and the serial-port and build tools are updated. No changes to what Klemme15 does.
- Every download again comes with `SHA256SUMS.txt` and a signed build provenance ([how to verify](https://github.com/Expos-AI/Klemme15/blob/main/SECURITY.md#verifying-your-download)).

## 0.22.0

- **Verify your download.** Every release now comes with `SHA256SUMS.txt` and a signed build provenance: proof, signed through Sigstore, that each zip was built by this repository's GitHub workflow from a specific commit. Check a download with `gh attestation verify <zip> -R Expos-AI/Klemme15`, or compare its SHA-256 with `SHA256SUMS.txt` ([how](https://github.com/Expos-AI/Klemme15/blob/main/SECURITY.md#verifying-your-download)).
- **Security fix:** the phone dashboard's build tool (esbuild) is updated past a moderate advisory, and the dashboard moves to React 19. The dashboard now needs iOS 14.5 or newer (phones from 2021 on).
- **A safer project.** CodeQL scans every change, Dependabot watches every dependency, workflows run with read-only permissions unless a job needs more, and `main` only changes through pull requests that pass every check. Contributions need a [Developer Certificate of Origin](https://github.com/Expos-AI/Klemme15/blob/main/CONTRIBUTING.md#sign-off-your-commits-dco) sign-off. A fuller [security policy](https://github.com/Expos-AI/Klemme15/blob/main/SECURITY.md) explains how to report a problem privately.
- The About window now credits Klemme15's creator and lead developer, Kindric Castro.

## 0.21.0

- **Ghost Hunter templates: say what you're chasing, not how to measure it.** Eight ready-made traps in plain words: *Catch an intermittent misfire*, *a voltage dip*, *a lean condition while driving*, *a lean idle* (vacuum leak hunting), *a rich condition*, *overheating*, *a charging dropout*, *a stall*. Each says when to use it and how to drive for it. Tick one and go, or pick it from **Start from a template** to fill the rule builder and adjust the numbers. The misfire trap watches every cylinder at once and names the rough one; the lean and rich traps watch both banks. Rules now read in words ("Engine speed below 1,000 rpm and Short-term trim, leaner bank above 12 % for 3 s").
- **N52 physics model baseline.** A first baseline for the N52, worked out from the engine's size and how full its cylinders get, with air mass split into idle / no load and full throttle and wide bands. It's labelled in the app as a model, not measured: it catches big problems, like a lean idle, and will be replaced by real logs from healthy cars ([how it's built](https://github.com/Expos-AI/Klemme15/blob/main/docs/BASELINES.md#the-n52-physics-model)).
- **Logs from other tools.** The Analyse page now recognises columns like `STFT_Bank1(%)`, `CoolantTemp(C)` and `MAF(kg/h)`, and shows air mass in kg/h as g/s.
- The "Running hot" trap now fires above 112 °C: N52-era engines run up to about 110 °C on purpose to save fuel.

## 0.20.0

- **Ghost Hunter: catch the fault that only happens sometimes.** Pick what to watch for (a battery voltage dip, running hot, a lean spike, cylinder 3 rough at high rpm, not charging while driving, or a rule you build yourself: any value above or below a number, held for a time, several at once), press Start and drive. Klemme15 reads the engine computer in the background and, the moment it happens, saves a snapshot with the minute before and the minute after. Nothing to tap while driving. Each capture opens like any snapshot, with the live data on the Analyse page, and can be shared. Read-only.
- **Known good baselines.** The Analyse page can draw a healthy range behind your scatter plot: a shaded band where 80 % of healthy readings fall and a dashed median, lined up by engine speed (never by time, so a drive and a baseline from different days compare fairly). A line says how much of your log sits inside it and where it leaves it ("Your total trim reads above the healthy range around 750 rpm"). Only warm-engine readings count. **Make a baseline from this log** when your car runs well, to compare later drives against or to share. Klemme15 ships only a labelled example for now: real baselines will come from owners' healthy logs, and each one says how many cars it's built from.
- **Live values on the car drawing.** On the Vehicle page, **Show live values** puts coolant, battery voltage, oil and intake temperature, engine speed and road speed on the top view of the car, each where it happens, turning amber or red when out of range. Values are read in the background and the tiles refresh four times a second, so fast polling doesn't make the app stutter; it stops by itself when you scan, leave the page or start the Ghost Hunter.
- **Total fuel trim.** Logs with short- and long-term trims get a "Total trim bank 1/2" channel on the Analyse page: what the engine is really correcting.

## 0.19.0

- **Snapshots: share your whole car in one file.** On the Faults page, **Share snapshot** saves a `.k15` file with every module and part number, every fault with its details, the dash messages and the car's story, health trends, the last minute of live data and your test graphs, plus a note saying what's wrong. Attach it to your E90Post, Reddit or GitHub post: anyone with Klemme15 opens it in the Garage and explores your car read-only, as if they were plugged in, with the live data replaying on the Analyse page. The VIN is removed everywhere, nothing can be written from a snapshot, and files from strangers are checked before anything is shown. `klemme15 scan --snapshot` and `klemme15 open` do the same from the command line. [How it works](https://github.com/Expos-AI/Klemme15/blob/main/docs/SNAPSHOTS.md).
- **Diagnostic chains: a mechanic's reasoning, step by step.** The new **Vacuum leak hunter** compares your fuel trims at idle and 2,500 rpm, checks the airflow against your engine's size, asks you to lift the oil filler cap, and points to the crankcase vent valve, a vacuum leak, or fuel delivery and the air mass meter. The **Charging system check** measures the battery at rest, then charging at idle and under load, and tells a tired battery from a weak or overcharging alternator. A **Run the chain** button appears under matching faults. Chains are written as data, read-only by design, and anyone can contribute one: [how](https://github.com/Expos-AI/Klemme15/blob/main/docs/CHAINS.md).
- **Buying a used BMW?** The pre-purchase check (Faults page) compares the mileage the car's modules recorded with the odometer you type in (km or miles), flags safety faults, freshly cleared memories, missing modules, the FRM3 and a weak battery, and saves a buyer's report. Mileage records come with your BMW files (EDIABAS mode).
- **Retrofit checker** (Coding page): notices a module that was replaced since your last scan, or one your model doesn't usually have, explains what that kind of job needs (LCI tail lights, LED bulbs, a used footwell module or cluster, a different battery, parking sensors) and checks the car afterwards. It never writes the vehicle order, flashes or touches mileage: it tells you which tool does those steps.
- More live values for tests and chains: air mass, load, intake temperature, manifold pressure, throttle and more.

## 0.18.0

- **Component tests you can watch.** Tests now draw live graphs as they run: requested against actual for VANOS, fuel trims and engine speed, battery voltage while cranking, the pump through the 12-minute bleed.
- **Water pump health test.** The electric water pump (N51-N55) is a known wear part that fails without warning. With a warm engine, Klemme15 graphs the speed the engine computer asks for against what the pump actually does and tells you whether it keeps up. Read-only, works with the plain cable.
- **Water pump sweep (experimental).** With your BMW files, Klemme15 runs the pump at 30, 60 and 90 % (engine off) and graphs each step. Marked "drives a part"; the pump is always switched off at the end or when you press Stop.
- **Results become trends.** Every test result is saved with the car's health history. When a part tests worse than it did before, the Faults page says so, and reports carry it too.
- **Health in reports.** The Markdown and HTML reports and the forum post now include "Health over time".
- **How to check it.** Faults now offer multimeter checks next to the troubleshooting steps: connectors, voltage-drop tests on grounds, the VANOS solenoid swap, the coil swap, the water pump's power, oxygen sensor heaters, wheel speed sensors, the sensors behind the mirror. In our own words with sources, using methods that don't need BMW pin-outs. Share your own with the new "Share a check or pin note" form.
- **Security: patched SQLite.** The optional full-database download now uses SQLite 3.53.3 (SQLitePCLRaw 2.1.12), fixing CVE-2025-6965 in the bundled SQLite.
- **Coding map library.** The Coding page lists every coding map Klemme15 knows, where it came from and whether it fits your car. One click fetches the latest community maps between releases; maps someone sends you can be imported. Maps for modules Klemme15 never codes (key, engine, brakes, airbag) are refused.

## 0.17.0

- **Health over time.** Every quick test of a real car now leaves a small snapshot on your computer (battery voltage, charging, fuel trims, faults, dash messages). The Faults page compares them and tells you what's drifting *before* it becomes a fault: a battery that rests a little lower each week, charging voltage out of range, long-term fuel trims creeping lean (the early sign of a vacuum leak), a fault that keeps coming back after you clear it, and what's new since last time. Open the demo car to see it with a made-up two-month history.
- **Your cars.** The Garage page lists every car you've scanned on this computer: last scan, mileage, battery, faults to fix and the most important trend. One click selects its model. Run a few cars? This is your fleet view, with no account and no cloud.
- **Private by design.** The history holds no VIN (cars are told apart by a one-way hash), never leaves your computer, and each car can be forgotten with one click. See the [privacy policy](https://github.com/Expos-AI/Klemme15/blob/main/PRIVACY.md).

## 0.16.0

- **Your car's story.** The instrument cluster quietly remembers the last few dozen dash warnings it showed, with the mileage of each. Klemme15 now reads that memory and tells it as a timeline on the Faults page (and in reports): *"221,616 – 221,648 km: Flat tyre. The cluster warned 12 times as the tyre lost pressure."* It groups warnings by moment and recognises the patterns: many unrelated systems complaining at the same instant is a battery voltage dip, not five faults; a tyre warning repeating over a few kilometres is one puncture; "set the clock" means the battery had been disconnected.
- **Battery registration confirmed on a real car** (MSV70 / N52, E90 325i). The Service page now shows which real cars a function has been confirmed on.
- **Battery protection.** The header shows the battery voltage once it's been measured. Below 12.4 V with the engine off, the Vehicle page warns that a connected tool keeps the car awake (10-20 A), and disconnecting reminds you to switch the ignition off.
- Modules that keep their Shadow / Info memory (some footwell modules, junction boxes, cruise control) are no longer reported as failing to clear; their fault memory is still cleared.
- Better English for a few BMW texts (cornering light, overvoltage fault).

## 0.15.0

- **Quick codes set themselves up.** The Coding page has a checklist that gets a module to one-click coding. Klemme15 finds your NCS Expert install and its latest reading of the module by itself, so verifying is one button.
- **Teach Klemme15 from another coding tool.** Using BimmerCode or similar? Read the module, change one option with your tool, read again: Klemme15 learns exactly which bits that option is on your module and can then do it itself, offline, with backup, read-back and auto-restore. It refuses unclear readings (nothing changed, too much changed, outside the coding).
- **Share this map**: one click opens a pre-filled GitHub issue so everyone with the same module gets quick codes with no setup. Maps hold only part number, coding index and option bits.
- A step-by-step [quick codes guide](https://github.com/Expos-AI/Klemme15/blob/main/docs/QUICK-CODES.md), and a link to BMW's official aftersales portal for workshops that want BMW's own software.

## 0.14.0

- **Every dash message decoded.** 396 Check Control IDs (1 to 999) in our own words, each with what it means, what to do, how serious it is and which module usually raises it. Meanings that BMW's own numbering confirms are marked high confidence; where public lists got it wrong (123 and 133 are the *left* rear light), BMW's numbering is used.
- **No fault code left blank.** A BMW code that no source describes still gets a meaning from its number range and module (codes 0xC000-0xEFFF are almost always communication faults), plus what to do next.
- **Clear everything, like ISTA.** Clearing a module now also empties its Shadow / Info memory. Info memory is read from every D-CAN module. New: `klemme15 clear --all`.
- **One-click LED preset** on the Coding page: every bulb warning off (indicators, brake, plate, parking/angel eyes, fog) in one write, with one backup, a full read-back and automatic restore. Also a cluster extras preset. Like single options, presets unlock only when Klemme15 knows exactly where the settings live in your module (your verified NCS data, or a community-verified map); it never guesses coding bits.
- **Layout fixes**: the navigation rail shows icons only on shorter windows so every page fits, and long text wraps instead of being cut off. Every release is now checked for clipped or overlapping text at 1440x900 and 1280x720.

## 0.13.2

- **Dash messages confirmed on a real E90.** The cluster's message list and history and the key module's info memory now decode as seen on a real car. Four more messages added: brake and stability control failed (42), right reversing light out (115), parking brake applied (308), hill start assist off (354).
- Vehicle page: dash message rows no longer overlap when the history shows mileage, and a long list scrolls instead of pushing the faults off screen.

## 0.13.1

- **Faster-feeling startup.** The splash screen now does real setup work while it shows: it loads the vehicle database, finds your EDIABAS/INPA/ISTA files, loads official fault texts and checks cable ports, with each step on the progress bar. The Garage page opens ready instead of showing "Looking for..." first.
- The splash lasts only as long as that work plus a short intro (about a second), with no added waiting, so the skip-on-click has been removed. It shows the version and "Not affiliated with BMW AG".

## 0.13.0

- **Dash messages (Check Control).** The quick test now asks the instrument cluster which warning messages it is showing (key battery low, bulb out, washer fluid, brake pads...) and its recent message history, so you can see **why the warning triangle is lit even when no module has a fault code**. Shown red or yellow like the dash, with what each means and what to do. 61 common messages written in our own words with sources; BMW's own wording is shown alongside when you have your BMW files. Unknown messages still appear by number.
- **Shadow / Info memory**, clearly labelled. Native mode now also reads the info memory of the key module (CAS) and the engine computer. These background entries are labelled "Shadow / Info memory" everywhere and never count as faults.
- Reports (HTML, Markdown, forum post) and `klemme15 scan` include both. Demo cars show a "key battery low" message.
- Note: the cluster's message-history format is not yet confirmed on a real car; a scan with the traffic log saved helps.

## 0.12.3

- A startup splash screen tied to the app's real loading progress.

## 0.12.2

- **Install via winget or Scoop** (`winget install Expos-AI.Klemme15`, or `scoop bucket add klemme15 https://github.com/Expos-AI/Klemme15; scoop install klemme15`), which skips the Windows SmartScreen prompt. Direct `.zip` downloads still work. The install recipes now update themselves on every release.

## 0.12.1

- An **About** dialog (version, what Klemme15 is, links, licence) and small footer buttons in the sidebar for About, support, the privacy policy and a donation link.
- A written **privacy policy** (PRIVACY.md): everything stays on your computer; nothing is sent anywhere unless you ask.

## 0.12.0

- **No-start triage.** "Why won't it start?" (Tests page) now reads the start-chain modules' fault memories (engine, key, steering lock) along with battery voltage and whether the engine cranked, and routes the result to a cause: battery, starter, steering lock, a crank/cam sensor, fuel, ignition, or a genuine immobiliser fault that belongs at a locksmith or dealer. It never touches keys or the immobiliser.
- **Contribute and share coding maps.** After verifying a module against NCS Expert, export its map (part number, coding index and each toggle's bit-operations, in our own numbers) and open a pull request. Once a map is shipped, an owner with the same module can use its comfort toggles with no NCS data — still with a backup, read-back and automatic restore. The shared database (db/coding-verified.json) starts empty and grows as real cars are verified.

## 0.11.1

- **Analyse page (datalogging).** Load a CSV datalog (recorded by Klemme15 or any tool) and scatter one channel against another, see a value averaged over a tuning-style RPM/load map, and play the log back with a scrubber while every channel reads out in step. The Live page's recording opens straight in it with the new "Analyse" button.

## 0.11.0

- **Coding (experimental).** Back up any comfort module's coding with no BMW data, compare it later to see exactly what any tool changed, and restore it in one click, with every block read back. With your own NCS Expert data you can see every setting by name and switch popular options: LED bulb-check off (indicators, brake, plate, parking, fog), digital speed, instant consumption, dial lighting, mirror fold, windows that keep closing when a door opens, wiper finish. Writing unlocks only after Klemme15 decodes your module exactly as NCS Expert does. It backs up first, changes only those bits, re-checks every setting and puts the original back if anything is off. The key module (CAS), engine and safety modules are never coded. See docs/CODING.md.
- **FRM3 protection.** The Vehicle page tells you which footwell module you have, from its part number: FRM1, FRM2, or the fragile FRM3. A fragile or unidentified FRM is only coded or put through service functions at 13.0 V (a charger), and its faults are only cleared at 12.5 V. A low battery now warns before you clear faults or code on cars with an FRM.
- **Valvetronic end-stop learning without BMW files** (N52, MSV70/MSV80).
- **Cooling system bleed assistant** (Tests page) for engines with an electric water pump. It walks you through BMW's 12-minute routine (engine off, pedal to the floor) and watches the pump run.
- **Phone dashboard for headless mode.** Open `klemme15 serve` on your phone for gauges, live tiles with sparklines, warnings and the engine's faults.
- **How-to guides** with sources (Tests page): where every module is on an E90, fuses and the B+ cable, a parasitic battery drain test, connector notes for common jobs, the trunk lid harness, and the FRM3 problem.
- The E60 family's 0x72 module is now correctly named the body module (KBM), not a footwell module.

## 0.10.3

Practical advice for codes that are usually harmless. The junction box relay code A731 (common after an aftermarket radio install or a flat battery), the odometer backup mismatch 9315 and the "power supply interrupted" A738 now say when to ignore them and when to look further (including how to do a parasitic draw test), and are no longer listed as "fix now". DSC 5E40 on E90-family cars explains the steering angle calibration that usually clears it.

## 0.10.2

*Second real-car test.*

Engine readings confirmed working without BMW files (372 values read on a 2006 325i). "Read all" now reads 20 values per request: seconds instead of over a minute. Units are readable (°C, ° crank, mg/stroke, rpm). Live data pauses when you leave its page (unless recording), so the quick test runs at full speed: the module broadcast now works on the E90, so absent modules are skipped in 0.1 s.

**Fixed after the first real-car test (2006 E90 325i):**
- Module identification is decoded: part number, hardware and coding index and production date for every D-CAN module (the engine computer's part number now pins down its exact version, e.g. MSV70).
- Engine readings without BMW files now work: the engine computer refused the request Klemme15 used, so values are read the way BMW's own tools do (a temporary measurement record). This also fixes the VANOS and cylinder roughness tests.
- Battery voltage is read from BMW's own measurement when the engine computer ignores the standard OBD request; live data no longer stalls on values the car never answers.
- Live data CSV: columns could shift when values were ticked after starting; every row now lines up with its header.
- The cruise control module (LDM, 0x1C) is in the E90 list; the junction box and footwell module try the E90 versions first; the quick test is faster.
- Model data checked against published sources: the North American 325i/325xi have the 3.0 L N52, plus corrections to years and body styles across families.

## 0.10.0

- **More cars**: 1 Series E8x, 5/6 Series E60/E61/E63/E64 (2007 on), X5 E70 / X6 E71, Z4 E89, 5 Series E39, X5 E53, 7 Series E38, each with demo cars. Built from BMW's module data, not yet confirmed on real cars: please report.
- **Guided tests with a verdict** (Tests page): fuel trims at idle and 2,500 rpm, VANOS target vs actual, cylinder roughness, and "Why won't it start?" (read-only).
- **Faults sorted** into fix now / worth checking / background (info and shadow memory).
- **Restart a frozen module** (iDrive, Bluetooth, amplifier and more) without disconnecting the battery.
- **12.5 V floor** for every write, and a warning before writing to footwell modules known to corrupt.
- **Copy for a forum post**, with the VIN removed.
- **Wi-Fi/network cables** (RFC 2217 or raw TCP) and **headless mode** with HTTP/JSON and MQTT for a Raspberry Pi.
- **Steering lock counter reset** for early E90s with a key slot (counters only, never keys or immobiliser codes), and a **3-stage intake manifold check** for N52 retrofits. Confirmed on a real E90 over D-CAN; most other models are tested against the built-in demo cars. Please report results from your car: see CONTRIBUTING.md.

**Official BMW meanings for every code:** in the Garage, download the optional bmw-unified-db fault database (13 MB, third-party) or point Klemme15 at your own ISTA/INPA files. See docs/FAULT-DATA.md.

**Service functions (experimental): 75 of them.** Service reminder reset (oil, brakes, brake fluid, microfilter, spark plugs, vehicle check; E46 oil and inspection), battery registration, engine adaptation reset, Valvetronic and DISA learning, cylinder cut-out test, fuel tank leak test, secondary air, oxygen sensor and catalyst checks, VANOS flush, component tests (fan, fuel pump, thermostat, water pump, secondary air pump, throttle, DISA, purge valve), diesel service (particulate filter, glow plugs, EGR and swirl flaps, injector codes, cylinder balance, cooling bleed), DSC (steering angle and sensor calibration, ABS bleeding, flat tyre reset, wheel speed sensor test), gearbox (adaptation reset, SMG learning and bleeding, xDrive transfer case), body (windows, sunroof, climate flaps, rain sensor, parking sensors, gauge test). Klemme15 checks the car, asks you to confirm each requirement, runs BMW's own procedure for your module version and always switches tested parts off again. None has been confirmed on a real car yet, so they only run with experimental functions switched on. Battery registration also works without BMW files; the others need "Use my own BMW files". Full list: docs/SERVICE-FUNCTIONS.md. Please report how it went.

**Readings without ISTA:** in the Garage press "Add engine readings" (one-time ~130 MB download, keeps a few MB). The Readings page then shows hundreds of engine values for every E90-family petrol and diesel engine, read by Klemme15 itself: no ISTA, no BMW files, any Windows 10/11, Mac or Linux computer. Not yet confirmed on a real car: please test and send your traffic log.

**Readings with your BMW files:** with "Use my own BMW files" ticked, every module's status values come from BMW's own scripts (ISTA/INPA/EDIABAS).

**Troubleshooting guides:** press Troubleshoot under a fault for step-by-step checks (mixture, misfires, VANOS/Valvetronic, cooling, fuel pressure, boost, charging, bus faults, wheel speed, steering angle and lock, O2 heaters, secondary air, airbags), with shortcuts to the readings that confirm each step.


macOS: the app is unsigned, so right-click > Open the first time. Linux: chmod +x Klemme15, and add yourself to the dialout group for serial access.
