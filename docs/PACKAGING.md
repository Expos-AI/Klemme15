# Packaging (maintainer notes)

Klemme15 is distributed three ways. The portable `.zip` per platform is the source of truth; the two Windows package managers point at it.

## How it stays current

`scripts/update-packaging.sh <tag> <win-x64.zip>` regenerates, from the released zip:

- `bucket/klemme15.json` — the Scoop manifest (this repo doubles as a Scoop bucket).
- `packaging/winget/Expos-AI.Klemme15.*.yaml` — the winget manifests.

The release workflow runs it automatically after every release (the `packaging` job): it downloads the new `win-x64` zip, fills in the version and SHA-256, and commits the refreshed manifests to `main`. No manual editing.

## Scoop

Works immediately once the manifest is on `main`:

```powershell
scoop bucket add klemme15 https://github.com/Expos-AI/Klemme15
scoop install klemme15
```

`scoop update klemme15` picks up new releases via the manifest's `checkver`/`autoupdate`.

## winget

The generated manifests are correct, but the **first** submission to the community `microsoft/winget-pkgs` repo is a one-time manual step (it needs your own GitHub account to open the PR):

```powershell
winget install wingetcreate
wingetcreate submit packaging/winget
```

After that, set a `WINGET_TOKEN` repository secret (a classic PAT with `public_repo`, on a fork of `winget-pkgs`) and the `packaging` job will open the update PR automatically on every release.

## Removing the SmartScreen prompt on direct downloads

Direct `.zip` downloads are unsigned, so Windows SmartScreen warns on first run. winget/Scoop avoid it. To remove it for direct downloads too, sign the Windows binaries — **Azure Trusted Signing** is the cheapest route (~$10/month, no hardware token). Add the signing step to the `publish` job once an account is set up.
