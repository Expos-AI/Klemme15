# Releasing

Every release, before tagging:

1. **Version**: bump `<Version>` in `Directory.Build.props`.
2. **Release notes**: add `**New in x.y.z:**` at the top of `.github/release-notes.md`, written for owners (what they can do now, not how it was built). Move the previous section down as `**x.y.z:**`.
3. **README**: new features in the right "What it does" group; update the Status line when something is confirmed on a real car.
4. **Roadmap**: move finished items from Next to Done.
5. **Privacy**: anything new that's saved to disk or goes online must be in `PRIVACY.md`.
6. **CI**: the docs tests fail if the release notes don't match the version, if a number in the README or roadmap no longer matches the data, or if a link between the docs is broken. Check the layout report on the screenshots branch too.
7. **GitHub "About"** (repository settings, the gear next to About): keep the description and topics below in sync when the feature set changes.

Release from Actions: run the **build** workflow with the version (e.g. `v0.19.0`). It tests, publishes the downloads, creates the GitHub release with these notes and updates the Scoop and winget recipes.

## GitHub About

Description:

> Free, open-source, offline BMW diagnostics for E-series owners (E46, E90 and more): scan every module, decode faults and dash warnings, catch intermittent faults, track your car's health over time, run graphed component tests and service functions, back up and code comfort modules. Windows, macOS, Linux.

Website: `https://github.com/Expos-AI/Klemme15/releases/latest`

Topics: `bmw` `diagnostics` `obd2` `right-to-repair` `e90` `e46` `ediabas` `kdcan` `car-diagnostics` `automotive` `open-source` `dotnet` `avalonia`
