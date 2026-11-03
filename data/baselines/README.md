# Community baselines

Healthy-engine logs that owners shared from Klemme15 (Analyse page → **Share as a healthy baseline**), one folder per engine code (`N52B30`, `M57D30`...). Each file holds only engine values (engine speed, load, coolant, air mass, fuel trims, intake air, throttle) against time from zero: no VIN, no date of the drive, no location.

They arrive through the [Share a healthy baseline](https://github.com/Expos-AI/Klemme15/issues/new?template=healthy-baseline.yml) form. The **Baseline intake** workflow checks each one with `scripts/baseline_intake.py`, stores it here, rebuilds that engine's community profile in `src/Klemme15.Data/db/baselines.json` and opens a pull request. See [docs/BASELINES.md](../../docs/BASELINES.md).
