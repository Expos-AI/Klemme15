# Privacy policy

Klemme15 is a desktop application that runs on your own computer. It is not a web service, and it has no account, login or server of its own.

## What Klemme15 does with your data

- **Everything stays on your computer.** Scans, faults, readings, live-data recordings, coding backups and traffic logs are held in memory while the app runs and are only written to disk when **you** choose to save or export them, to a location you pick.
- **Klemme15 does not send your vehicle data anywhere.** It does not phone home, and it has no analytics, telemetry or crash reporting that leaves your machine. (The optional `klemme15 serve` mode publishes live data on **your own** network to a dashboard or MQTT broker you choose — it is off unless you run it, and it only goes where you point it.)
- **Your VIN is never transmitted.** It is shown to you locally, and the "Copy for a forum post" feature removes it before copying.

## Network connections Klemme15 may make

Klemme15 connects to the internet only when you ask it to, and only to these:

- **Downloading the optional fault database or engine-readings data**, if you choose to (from the public `bmw-unified-db` project and package hosts). This downloads data *to* you; it sends nothing about your car.
- **Opening a link** (About, Support, Privacy, Buy me a coffee) in your browser, when you click it.
- **A network cable** (`rfc2217://` or `tcp://`) or `klemme15 serve`, which talk only to the devices and brokers you configure on your own network.

## Your BMW files and NCS Expert data

If you point Klemme15 at your own ISTA/INPA/EDIABAS files or your NCS Expert data folder, it reads them locally to name faults and coding settings. Nothing from those folders is copied into Klemme15 or uploaded anywhere.

## Contributions you choose to make

If you export a verified coding map or a scan report to share, that file contains only what is shown in the app (and, for coding maps, no VIN). Sharing it — for example by opening a pull request — is your choice and your action, not something Klemme15 does automatically.

## Contact

Questions about this policy: open an issue at the project page listed in the app's About dialog.

*Klemme15 is an independent, open-source project and is not affiliated with or endorsed by BMW AG.*
