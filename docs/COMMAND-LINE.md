# Command line

The `klemme15` command-line tool ships in the `cli/` folder of every download. It covers what the app does, for scripts, a small Linux computer or a quick look without the window. Releases are built for 64-bit Windows, Linux and macOS only, so a Raspberry Pi needs a build from source for now. Run `klemme15 --help` for every option.

```bash
klemme15 ports                                   # find the cable
klemme15 scan --vehicle E9x --port COM3 --report scan.html
klemme15 live --vehicle E46 --port /dev/ttyUSB0 --pids rpm,coolant,stft1,ltft1 --csv drive.csv
klemme15 faults --module DME --port COM3
klemme15 readings --module DME --port COM3 --ediabas "C:\ISTA\Ediabas\Ecu" --filter lambda
klemme15 clear --module DME --port COM3
klemme15 clear --all --port COM3                # every module: faults and Shadow / Info memory
klemme15 service                                 # list service functions
klemme15 service battery-registration --port COM3 --experimental
klemme15 service service-reset --item oil --port COM3 --ediabas "C:\ISTA\Ediabas\Ecu" --experimental
klemme15 scan --demo --vehicle E9x --model 335i # try it without a car
klemme15 scan --port COM3 --forum                # summary for a forum post, VIN removed
klemme15 test fuel-trims --port COM3             # guided test with a verdict (klemme15 test lists them all)
klemme15 serve --port rfc2217://192.168.4.1:2217 --mqtt 192.168.1.10   # headless, over Wi-Fi
klemme15 scan --port COM3 --snapshot car.k15 --note "Rough idle when warm"   # a snapshot others can open
klemme15 open car.k15 --report car.html          # read a snapshot someone shared
klemme15 coding --module FRM --port COM3 backup  # back up a module's coding (no BMW data needed)
klemme15 discover --port COM3                    # look for modules the database doesn't list
klemme15 scan --port COM3 --log traffic.txt      # save the raw cable traffic for a bug report
```

`--vehicle` takes the car family: E46, E39, E53, E38, E85, E83 and E36 (K-line; the E83 and E36 are betas), or E9x, E8x, E60, E70, E84 and E89 (D-CAN). `klemme15 vehicles` lists each family's models and modules. Without `--vehicle`, Klemme15 assumes E9x.

BMW's own fault texts (optional, see [FAULT-DATA.md](FAULT-DATA.md)):

```bash
klemme15 db download                             # fetch bmw-unified-db from its GitHub release (13 MB)
klemme15 code E9x.DSC 5E1A --model 325i          # what does a code mean on a given model?
klemme15 scan --port COM3 --ecu "C:\ISTA\Ediabas\Ecu"   # or read texts from your own ISTA/INPA files
```

Two folder options, both pointing at the `Ecu` folder of your EDIABAS, INPA or ISTA install:

- `--ediabas <folder>` runs BMW's jobs from those files (EDIABAS mode, Windows): every module's readings, service functions, and `klemme15 job D_0012 IDENT --ediabas C:\EDIABAS\Ecu --port COM3` for any job, except the ones Klemme15 never runs (flashing, keys and the immobiliser, mileage changes, key module writes).
- `klemme15 mileage --odometer 152000 --miles --ediabas C:\EDIABAS\Ecu --port COM3` reads the mileage every module remembers and compares it with the odometer. It exits with 1 when the figures don't add up. Without `--ediabas` it uses the fault and dash message records, plus the engine computer's count where Klemme15's own engine tables list it.
- `--ecu <folder>` only reads BMW's fault texts from those files, on any system.

For running Klemme15 unattended with a phone dashboard and MQTT, see [HEADLESS.md](HEADLESS.md).
