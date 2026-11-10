# Privacy policy

Klemme15 runs on your computer. It has no account, no server and no analytics, and it never sends anything about your car anywhere on its own.

## What stays on your computer

Everything Klemme15 reads from your car stays on your computer. Reports, recordings, traffic logs and snapshots are only written when you save them, to a place you choose.

Klemme15 also keeps these files by itself, in a `Klemme15` folder in your user's application data (`%APPDATA%\Klemme15` on Windows, `~/.config/Klemme15` on macOS and Linux). None of them is ever uploaded, and you can delete any of them at any time.

| File or folder | What's in it | When |
|---|---|---|
| `settings.json` | Your last car and port, folder paths you chose, the experimental switch, your live data favourites, which version of the "Before you start" notice you confirmed, whether you accepted the notice for the third-party fault database, your units (km and °C, or miles and °F), whether you hid the "Start here" steps, and your update-check choice (on or off, when it last checked, a version you skipped) | When you change a setting |
| `history/` | A small summary of each quick test of a real car: date, model, approximate mileage, battery voltage, engine speed, coolant temperature, fuel trims, fault codes, dash message numbers, module part numbers, and your guided test results. **No VIN**: cars are told apart by a one-way hash of the VIN, or, for older cars that don't report it, of their engine module's and cluster's part numbers. **Forget** under *Your cars* removes a car | After each quick test and guided test |
| `coding-backups/` | A copy of a module's coding, taken before every coding change and restore. Some modules keep the VIN in their coding, so a backup can contain it | Before every write |
| `captures/` | Ghost Hunter captures, saved as snapshots with the VIN removed | While the Ghost Hunter watches |
| `ghost-hunter-rules.json` | Rules you made on the Ghost Hunter page | When you save a rule |
| `baselines/` | Baselines you made or added: curves of values across engine speed, no VIN | When you make or add one |
| `running-job.json` | The job that's changing the car right now (job, module, model, time; no VIN), so Klemme15 can tell you what to do if it's cut off. Deleted when the job ends | While a job runs |
| `db/` | Community coding maps you downloaded | When you press **Get the latest community maps** |
| `bmw-unified-db/` | The optional BMW fault texts and engine readings you downloaded | When you download them |

Versions from before the project was renamed used an `OpenHood` folder; Klemme15 copies its files across once.

## When Klemme15 goes online

Only when you ask, and only to these:

| What you do | Where it goes | What it sends |
|---|---|---|
| Download BMW fault texts or engine readings | github.com (the [bmw-unified-db](https://github.com/gediz/bmw-unified-db) project's release, found through api.github.com) | Nothing about your car |
| **Get the latest community maps** (Coding page) | raw.githubusercontent.com (this project) | Nothing about your car |
| **Translate** a BMW fault text | Google Translate, in your browser | That fault's German text |
| **Find this part** on a fault | DuckDuckGo, in your browser | The search words: your car's model and engine, and the part's name |
| Open a link (About, help, privacy, the PayPal tip page) | That page, in your browser | Nothing about your car |
| Open a snapshot on the website (viewer.html) | Nowhere: the file is read in your browser, on your own device. The page itself loads from expos-ai.github.io and its fonts from Google Fonts, like the rest of the website | Nothing from the file |
| **Check for updates** (the arrow button), or start Klemme15 after you said yes to checking on start (at most once a day) | expos-ai.github.io (this project's website), for one small file, `latest.json` | Nothing: not your version, not anything about you or your car. Klemme15 compares the version itself and never downloads or installs an update on its own |
| Connect a Wi-Fi cable, or run `klemme15 serve` | Only the devices and the MQTT broker you set up, on your own network | Your car's live data, to them |

## Things you choose to share

- **Snapshots (`.k15`)** hold your car's modules, faults, dash messages, health trends, your note and, if you tick them, live data, test results, coding and the traffic log. The VIN is removed before the file is written; a VIN split across two messages in the traffic log can slip through, so skim it if that matters to you ([how snapshots work](docs/SNAPSHOTS.md)). Sharing the file is up to you.
- **A forum post** (**Copy for a forum post**) leaves the VIN out.
- **A buyer's report** (**Save buyer's report**) holds the pre-purchase check, including every mileage figure the modules remember and the odometer you typed. It leaves the VIN out. The mileage figures are only read when you press **Read the mileage from every module**, and aren't kept after you close Klemme15.
- **A healthy baseline** (**Share as a healthy baseline**) saves a `.csv` with only engine values (engine speed, load, coolant, air mass, fuel trims, intake air, throttle), the engine code, the model and the Klemme15 version, and opens a GitHub form. Nothing is uploaded unless you attach it there yourself; then it's public, with your GitHub username as the contributor.
- **Coding maps** (**Share this map**) hold a module's part number, coding index and option positions, no VIN. They're shared through a GitHub issue you submit.

## Your BMW files

If you point Klemme15 at your own ISTA, INPA, EDIABAS or NCS Expert folders, it reads them on your computer to run BMW's jobs and name faults and coding settings. Nothing from those folders is copied into Klemme15 or uploaded.

## The cable check

**Check cable** reads what your operating system knows about the cable (chip maker, serial number, latency setting) and tries opening the port. Nothing is sent anywhere.

## Contact

Questions about this policy: [open an issue](https://github.com/Expos-AI/Klemme15/issues/new/choose).

*Klemme15 is an independent, open-source project, not affiliated with or endorsed by BMW AG.*
