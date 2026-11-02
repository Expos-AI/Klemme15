# Security policy

Klemme15 talks to cars, so safety and security bugs matter more than most.

## Reporting

Please report security problems **privately** through GitHub's [private vulnerability reporting](https://github.com/Expos-AI/Klemme15/security/advisories/new), not in a public issue. Include the version, what you did and what happened. You'll get a reply within a week.

Report there, too, anything that could make Klemme15 write to a car in a way it shouldn't: a write without a backup, a coding change outside the option's bits, a service function running when its checks failed, or anything reaching the key module or immobiliser.

## Supported versions

Only the latest release gets fixes. Klemme15 is early alpha and updates often; winget and Scoop keep you current.

## By design

- No network access except what you ask for (see [PRIVACY.md](PRIVACY.md)).
- No flashing, no key or immobiliser functions, no coding of the key, engine, gearbox, brake or airbag modules.
- Dependencies are watched by Dependabot, and every release is built by GitHub Actions from this repository.
