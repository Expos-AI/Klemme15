# Coding

Klemme15's Coding page (and `klemme15 coding` on the command line) does three things, in increasing order of risk:

1. **Back up, compare and restore** a module's coding. This works on any car, with no BMW data.
2. **Show every setting by name**, using the NCS Expert data you already have. This only reads.
3. **Switch a short list of comfort options**. This is experimental, and writing unlocks only after the checks below pass.

## What can be coded

Only comfort and lighting modules can be coded: the footwell module (FRM), the instrument cluster (KOMBI), the junction box (JBE), climate (IHKA), the roof module (FZD), the light module (LM), the body module (KBM), parking sensors (PDC) and the sunroof (SHD).

Some modules are never coded:

- **The key module (CAS) and immobiliser (EWS).** Comfort closing with the remote, auto-lock and key behaviour live there, next to the immobiliser data. Klemme15 leaves them alone.
- **Engine, gearbox, brakes, steering and airbag modules.** This includes the steering column switches (SZL), which carry the steering angle sensor that DSC uses.

Klemme15 also never touches the vehicle order, the VIN or the coding change index. It only reads and writes coding blocks 0x3000-0x3EFF.

## 1. Backups (no BMW data needed)

**Back up now** reads every coding block the module has and saves it. Klemme15 finds the blocks itself: it asks for blocks 0x3000 upwards on each page and moves to the next page after 16 missing blocks in a row. The app keeps backups in your Klemme15 data folder, under `coding-backups`. The CLI saves them in the current folder, or wherever `--out` points. A backup records the module's part number and coding index. It contains nothing that identifies the car.

**Compare with car** lists every byte that differs from the backup, and names the settings that changed if NCS data is loaded. This shows exactly what BimmerCode, NCS Expert or a workshop changed.

**Restore** writes back only the blocks that differ, and reads each one back. Before writing, Klemme15 saves the module's current coding, so a wrong restore can be undone too. If a block fails part-way, the blocks already written are put back to how they were before. Restoring is refused in these cases:

- the backup can't be matched to the module (different module, part number or coding index, or any of them unknown);
- a block in the backup doesn't exist on the module, or has a different length;
- experimental functions are off;
- the battery is below the floor (see "Voltage" below).

Klemme15 also saves a backup automatically before every change it makes.

```bash
klemme15 coding --module FRM backup --out frm-before.json --port COM3
klemme15 coding --module FRM compare frm-before.json --port COM3
klemme15 coding --module FRM restore frm-before.json --experimental --port COM3
```

## 2. Settings by name (your NCS Expert data)

Klemme15 doesn't ship BMW's coding data. If you have NCS Expert, point the Coding page at its data folder (usually `C:\NCSEXPER\DATEN`); on the CLI, pass `--ncs`.

Klemme15 then:

1. picks the module description file that matches your module's coding index;
2. reads the setting names and their values from it;
3. decodes your module's coding into a list like NCS Expert's `FSW_PSW.TRC`.

The file format knowledge comes from the open-source [BimmerDaten](https://github.com/zer02dev/BimmerDaten) project (GPLv3). Nothing from your NCS folder is copied or uploaded.

## 3. Comfort options, and why writing needs verification

How NCS's addresses map onto a module's coding blocks has not been confirmed on real cars yet. So Klemme15 never guesses where a setting lives. It writes to a module only after proving its map against NCS Expert's own reading of that same module:

1. Read the module once with NCS Expert. NCS Expert saves the result as `FSW_PSW.TRC` in its WORK folder.
2. On the Coding page, press **Verify with NCS Expert's FSW_PSW.TRC** and pick that file. On the CLI, pass `--trc`.
3. Klemme15 reads the module itself and decodes it with each possible layout. Verification passes only if one layout reproduces **every** setting in the trace exactly. At least 20 settings must be compared, and at least 8 of them must have non-zero bits, because all-zero bytes can match "off" values by chance. If two layouts both match, Klemme15 writes only when they agree on every byte.

The writer re-runs this check itself from the trace and the reading taken at verification time. It also refuses:

- any setting the trace didn't include;
- any setting whose values can't each be stored as a distinct byte within its mask.

If you load a different module file, the verification is dropped.

Once a module is verified, the options become switchable (with experimental functions on). Each change follows these steps:

1. **Voltage check.** It reads the battery voltage and refuses below the floor.
2. **Backup.** It saves a fresh backup.
3. **Plan the change.** It works out the new bytes, and refuses if anything outside the chosen settings' own bits would change.
4. **Write.** It writes block by block, reading each one back.
5. **Re-check.** It re-reads and decodes the whole module. The changed settings must read as requested, and every other setting must read exactly as before.
6. **Undo if needed.** If anything is off (a refusal, a read-back difference, an unexpected value), it writes the original blocks back automatically and reports it.

Afterwards, switch the ignition off, wait 10 seconds and switch it on again. Klemme15 deliberately does not reset the module, because a reset is when a weak FRM3 shows its damage.

| Option | Module | NCS settings (keywords) |
|---|---|---|
| LED turn signals: no bulb warning or fast flashing | FRM | `KALTUEBERWACHUNG_BLK*`, `WARMUEBERWACHUNG_BLK*` → `nicht_aktiv` |
| LED brake lights: no bulb warning | FRM | `KALTUEBERWACHUNG_BL*`, `WARMUEBERWACHUNG_BL*` → `nicht_aktiv` |
| LED number plate lights | FRM | `KALTUEBERWACHUNG_KZL*`, `WARMUEBERWACHUNG_KZL*` → `nicht_aktiv` |
| LED parking lights / angel eyes | FRM | `KALTUEBERWACHUNG_SL_V*`, `..._AL` → `nicht_aktiv` |
| LED fog lights | FRM | `KALTUEBERWACHUNG_NSW*` → `nicht_aktiv` |
| Digital speed in the cluster | KOMBI | `BC_DIGITAL_V` → `aktiv` |
| Instant fuel consumption | KOMBI | `DIGITAL_KVA` → `aktiv` |
| Dial lighting always on | KOMBI | `M_TAG_SKALENBELEUCHTUNG` → `aktiv` |
| Fold mirrors when locking with the remote | FRM | `SPIEGEL_KOMFORT_EINKLAPP` / `ASP_KOMFORT_EINKLAPP` → `aktiv` (also needs comfort closing in the CAS, which Klemme15 doesn't code) |
| Windows keep closing when a door opens | FRM | `TUERAUF_STOP_MAUT` → `nicht_aktiv` |
| Wipers finish their sweep | JBE | `ZYCLUS_VOLLEND_KLR_AUS` → `aktiv` |

Only settings that exist in your module's file are changed. Options your module doesn't have are hidden.

With bulb monitoring off you won't be warned about a failed lamp, so check your lights now and then. Lighting rules differ by country; check what applies to you.

```bash
klemme15 coding options
klemme15 coding --module FRM show --ncs C:\NCSEXPER\DATEN --trc C:\NCSEXPER\WORK\FSW_PSW.TRC --port COM3
klemme15 coding --module FRM set led-turn-signals on --ncs C:\NCSEXPER\DATEN --trc C:\NCSEXPER\WORK\FSW_PSW.TRC --experimental --port COM3
```

## Voltage

Every coding write needs at least **12.5 V**. A footwell module that is an **FRM3**, or one Klemme15 can't identify, needs **13.0 V**, which in practice means a battery charger (not a jump pack). Klemme15 never codes without a voltage reading.

## Help verify more modules

If verification passes on your car, please report the module file name, the coding index and the part number (no VIN) in an issue. A shared list of verified combinations is planned, so owners without NCS Expert can code those modules too.
