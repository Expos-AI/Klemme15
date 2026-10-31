# Quick codes: one-click coding, step by step

Quick codes are the one-click options on the **Coding** page: the LED conversion preset (every bulb warning off), digital speed in the cluster, instant consumption, dials always lit, mirror fold on lock and the others. They work fully offline.

## Why there's a setup step

Every option is a few bits somewhere in a module's coding, and *where* those bits sit changes between module versions. Klemme15 never guesses: a wrong bit in a footwell module can turn off lights or worse. Commercial apps keep a private database of those positions. Klemme15 builds an open one instead, one verified module at a time, and doesn't ship BMW's data.

So a module needs a **map** (where each option lives on that exact part number and coding index) once. After that it's one click, for you and for everyone with the same module once you share it.

There are three ways to get a map.

## Route 1: someone already shared your module

Open **Coding**, pick the module and press **Read coding**. If the panel says *"Quick codes are ready"*, a verified map for your exact module is built in. Switch on experimental functions on the Service page, then press **Apply all** on a preset. That's it.

## Route 2: you have NCS Expert

NCS Expert is part of BMW Standard Tools, which many E-series owners already have installed alongside INPA. Klemme15 doesn't include it and doesn't link to unofficial copies. Workshops can get BMW's software legally through [BMW's aftersales portal](https://aos.bmwgroup.com/) (in the US, [bmwtechinfo.com](https://www.bmwtechinfo.com/)).

1. **Klemme15 finds it by itself.** It looks in `C:\NCSEXPER` (and `C:\EC-APPS\NCSEXPER`). The checklist on the Coding page shows *"Find your NCS Expert data: Found"*. If it's somewhere else, press **Choose NCS data folder...** and pick the `DATEN` folder.
2. **In Klemme15:** connect, run the quick test, open **Coding**, pick the module (e.g. Footwell module) and press **Read coding**.
3. **In NCS Expert, read the module once.** This step only reads, it doesn't change anything. Close Klemme15's connection first, or use the same cable after Klemme15 has disconnected.
   - File → Load profile → **Expert mode**.
   - **F1** VIN/ZCS/FA → **F3** ZCS/FA f. ECU → pick the chassis (**E89** covers the E8x and E9x 1 and 3 Series; E60 and E70 have their own) → pick **CAS** to read the car's order → **F6** Back.
   - **F4** Process ECU → pick the module (e.g. **FRM**) → **F2** Change job → **CODIERDATEN_LESEN** → **F3** Execute job.
   - NCS Expert saves the reading as `C:\NCSEXPER\WORK\FSW_PSW.TRC`.
   - Don't run SG_CODIEREN or any other job here. You only need the reading.
4. **Back in Klemme15:** reconnect, press **Verify with NCS Expert's latest reading**. Klemme15 decodes every setting itself and compares it with NCS Expert's reading. They must match exactly, or writing stays locked.
5. **Quick codes unlock.** Press **Apply all** on a preset. Klemme15 backs up, writes only those bits, reads everything back, and puts the original back automatically if anything is off. Switch the ignition off and on again afterwards.
6. **Share it.** Press **Share this map**. A pre-filled GitHub issue opens in your browser. Press Submit, and the next owner of that module gets quick codes with no setup.

## Route 3: you use another coding tool (BimmerCode or similar)

Klemme15 can learn an option by watching what your tool changes.

1. In Klemme15, open **Coding**, pick the module, choose the option in **Teach Klemme15 from another coding tool**, and say whether you'll switch it on or off.
2. Press **1. Read before**.
3. With your other tool, change **only that one option**.
4. Press **2. Read after**. Klemme15 shows exactly which bytes changed. It refuses if nothing changed, if something outside the coding changed, or if more than 8 bytes changed (that means several options, or a full recode).
5. Press **3. Save**. The option now works from Klemme15 on this module, with the same backup, read-back and auto-restore. Repeat for other options. Then **Share this map**.

A learned map is exactly what your tool wrote for that option on your exact module (same part number and coding index). Klemme15 only ever replays it on a module with the same part number and coding index.

## What's never coded

The key module (CAS, which also holds the immobiliser), the engine, gearbox, brakes and airbags. That includes *"close all windows when locking"*, which lives in the CAS. See [CODING.md](CODING.md) for the full safety design.
