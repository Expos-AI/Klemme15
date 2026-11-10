# Security policy

Klemme15 talks to cars, so safety and security bugs matter more than most. Thank you for taking the time to report one responsibly.

## Reporting a vulnerability

**Please don't open a public issue, discussion or pull request for a security problem.** Report it privately:

1. Go to [Report a vulnerability](https://github.com/Expos-AI/Klemme15/security/advisories/new) (GitHub's private vulnerability reporting). Only the maintainer can see it.
2. Include:
   - the Klemme15 version (bottom of the left rail, or the About window) and your operating system;
   - what you did, what happened, and what you expected;
   - steps or a file to reproduce it (a `.k15` snapshot, a CSV, a coding map), with personal data and your VIN removed;
   - what an attacker could do with it, as far as you know.

If you can't use GitHub, say so in a public issue titled "Security contact request" **without any details**, and the maintainer will reach out.

## What happens next

| When | What |
|---|---|
| Within 7 days | You get a reply confirming the report was received and read. |
| Within 30 days | An assessment: confirmed or not, how severe, and a plan. |
| Fix released | A new release with the fix, and a GitHub security advisory crediting you (unless you'd rather stay anonymous). |

Please give us a reasonable time to release a fix (90 days at most, usually much less) before you publish details. We'll keep you updated and agree a disclosure date with you.

## What counts

Report privately anything that could:

- make Klemme15 **write to a car** in a way it shouldn't: a write without a backup, a coding change outside the option's bits, a service function running when its checks failed, or anything reaching the key module (CAS) or the immobiliser;
- run code or read files through a file someone sends you: a `.k15` snapshot, a coding map, a baseline, a chain, a CSV;
- leak your VIN or personal data, or make Klemme15 connect anywhere you didn't ask it to;
- tamper with the release downloads, the Scoop install recipe or the build pipeline;
- expose the headless mode's dashboard or MQTT data beyond the network you pointed it at.

Not security issues (open a normal issue): wrong fault meanings, a test giving the wrong verdict, crashes with no security impact, and problems that need someone to already control your computer.

## Good-faith research

We won't pursue or support legal action against anyone who researches and reports a vulnerability in good faith under this policy: testing only on cars and computers you own or have permission to use, not accessing other people's data, and giving us time to fix it before going public.

## Supported versions

Only the **latest release** gets security fixes. Klemme15 is in beta and updates often; `scoop update klemme15` keeps a Scoop install current.

## Verifying your download

Every release is built by GitHub Actions from this repository, and comes with:

- **`SHA256SUMS.txt`**: the SHA-256 checksum of every zip. After downloading, check yours matches:
  - Linux: `sha256sum -c SHA256SUMS.txt --ignore-missing`
  - macOS: `shasum -a 256 -c SHA256SUMS.txt --ignore-missing`
  - Windows (PowerShell): `Get-FileHash .\Klemme15-v<version>-win-x64.zip` and compare with the line in `SHA256SUMS.txt`
- **A signed build provenance attestation**: proof, signed through Sigstore, that the zip was built by this repository's workflow from a specific commit. With the GitHub CLI: `gh attestation verify Klemme15-v<version>-win-x64.zip -R Expos-AI/Klemme15`. This is the stronger check, because it doesn't depend on a file stored next to the download. The same signed statement is attached to each release as `Klemme15-v<version>.intoto.jsonl`, so you can also check it offline: add `--bundle Klemme15-v<version>.intoto.jsonl`.

Only download Klemme15 from [the GitHub releases page](https://github.com/Expos-AI/Klemme15/releases) or the project's Scoop bucket.

## How the project protects itself

- No network access except what you ask for (see [PRIVACY.md](PRIVACY.md)).
- No flashing, no key or immobiliser functions, no coding of the key, engine, gearbox, brake or airbag modules. These rules are enforced where every BMW job runs (`JobPolicy`), so they also hold for jobs typed on the Expert page or the command line, and tests fail if a mileage job that could write is ever added.
- Files from other people (snapshots, maps, baselines, chains) are size-limited, checked and validated before anything is shown or used.
- CodeQL scans the code on every push and pull request; Dependabot watches NuGet, npm and GitHub Actions dependencies and raises pull requests for security fixes; every workflow action is pinned to an exact commit, and Dependabot moves the pins forward.
- Workflows run with read-only permissions unless a job needs more.
- Shared baselines are untrusted input: the intake workflow only follows GitHub attachment links, caps the download size, accepts plain numbers in known columns and never runs anything from the file.
- Contributions require a [Developer Certificate of Origin](CONTRIBUTING.md#sign-off-your-commits-dco) sign-off, and every change is reviewed by the maintainer (`.github/CODEOWNERS`).
- Links from other people's data open only if they're web addresses (`https:`/`http:`), in the app and in saved reports, so a crafted snapshot can't launch a program or a script.
- GitHub secret scanning and push protection are on, and security problems can be reported privately (above).
- [OpenSSF Scorecard](https://scorecard.dev/viewer/?uri=github.com/Expos-AI/Klemme15) checks how the project is run every week and publishes the result; it's the badge at the top of the README.
