# Releasing

Every release, before tagging:

1. **Version**: bump `<Version>` in `Directory.Build.props`.
2. **Release notes**: replace the `**New in x.y.z:**` section in `.github/release-notes.md` with this version's changes, written for owners (what they can do now, not how it was built). The release page shows only this file, so it holds one version. Add the same text under `## x.y.z` at the top of `CHANGELOG.md`, which keeps every version.
3. **README**: new features in the right "What it does" group; update the Status line when something is validated on a vehicle.
4. **Roadmap**: move finished items from Next to Done.
5. **Privacy**: anything new that's saved to disk or goes online must be in `PRIVACY.md`.
6. **CI**: the docs tests fail if the release notes don't match the version, if a number in the README or roadmap no longer matches the data, or if a link between the docs is broken. Check the layout report on the screenshots branch too.
7. **Website** (`docs/index.html`, served by GitHub Pages): every user-facing feature belongs in the right feature section, with a screenshot from the screenshots branch when it has a page of its own; keep the jump links, the "Will it work on my car?" table, the ISTA comparison and the page description current. Check it at phone and desktop width (no sideways scrolling).
8. **Everything else a visitor reads**: `SECURITY.md`, `CONTRIBUTING.md`, the docs a new feature touches (`docs/*.md`), the About window, and the winget description (`scripts/update-packaging.sh`).
9. **GitHub "About"** (repository settings, the gear next to About): keep the description and topics below in sync when the feature set changes.

Release from Actions: run the **build** workflow with the version (e.g. `v0.19.0`). It tests, publishes the downloads, creates the GitHub release with these notes and updates the Scoop and winget recipes.

## GitHub About

Description:

> Free, open-source BMW E-series diagnostics that run offline on your laptop with a K+DCAN cable. Read and clear every module's faults in plain English, watch live data, catch intermittent faults, run guided tests and service functions, and back up your coding. E46, E90 family, E60, E70, X1, Z4 and more. Windows, macOS, Linux.

Website: `https://expos-ai.github.io/Klemme15/`

Topics: `bmw` `diagnostics` `obd2` `right-to-repair` `e90` `e46` `ediabas` `kdcan` `car-diagnostics` `automotive` `open-source` `dotnet` `avalonia`
