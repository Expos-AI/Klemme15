# Working on Klemme15

Klemme15 is free, open-source, offline BMW E-series diagnostics for owners with the right to repair. These rules come from the maintainer, Kindric Castro, and apply to every change.

## After every change

Update everything a visitor or user reads, in the same push, so the project always looks professional, current and trustworthy:

- `README.md`, the website (`docs/index.html`), `.github/release-notes.md`, `docs/ROADMAP.md`, `PRIVACY.md` (anything new saved to disk or sent online), `SECURITY.md`, `CONTRIBUTING.md`, and the `docs/*.md` page for the feature.
- The full checklist is in [docs/RELEASING.md](docs/RELEASING.md). The docs tests check versions, counts and links; the rest is on you.
- Write for owners: what they can do now, in plain words. No claims that aren't true or confirmed.

## Lines never crossed

- Stay 100 % legal. No flashing, no bootloader access, no immobiliser, key or ISN work, no CAS coding, never change mileage.
- **The one agreed exception:** the service function "Steering lock: clear the fault counters" (`steering-lock-counter-reset`) writes to the CAS to clear the steering lock's event counters. It's the most requested feature and must stay. It may only use the jobs `STATUS_ELV_WERTE`, `STEUERN_ELVCOUNTER_CAS_LOESCHEN` and `STEUERN_ELVCOUNTER_ELV_LOESCHEN` (ServiceTests enforces this). Never remove it, and never add any other CAS write.
- Never bundle BMW data or software, never decrypt ISTA. Write everything in our own words, with sources. Don't link unofficial BMW software.
- Never commit a user's VIN or personal data.
- Anything that drives a part or writes to a module stays experimental until confirmed on a real car.
- Don't invent technical facts (fault codes, job names, value names, specifications). Check them against the data in the repo, or say plainly that something needs confirming on a real car.

## How changes land

- `main` takes direct pushes (no force pushes, no deletion). Run the tests before pushing and watch CI afterwards.
- Commits are authored as Kindric Castro <175667204+Expos-AI@users.noreply.github.com> (set with `git config user.name` / `user.email` in the clone). No co-author trailers, session links, AI or bot attribution of any kind in commit messages, pull requests, release notes or files.
- New `.cs` and `.axaml` files need the license header: `python3 scripts/license-headers.py`.
- Releases: bump `<Version>` and the release notes, push, wait for green CI on main, then run the build workflow with the version. The maintainer has approved releasing every finished change.
