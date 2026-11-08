# E36 beta: tester guide

Thank you for helping bring Klemme15 to the E36. Nobody has tried it on a real E36 yet, so **every result helps, including "nothing answered"**. This guide takes about 30 minutes. Everything in it only reads from the car, except step 8, which is optional.

## What's in the E36 beta

| Part | What to expect |
|---|---|
| **Engine** (fault codes, live data, fuel trim test) | Uses standard OBD-II, which 1996 to 1999 E36s have. This is the part most likely to work. |
| **Other modules** (ABS/ASC, airbag, cluster, body electronics, climate, automatic gearbox, park distance control) | Klemme15 asks for them at the addresses the E46 uses. Nobody has confirmed those on an E36 yet, so some may not answer. Finding out where they really are is the main point of this test. |
| **Cars from before 1996** | Not supported yet: their modules use older protocols. |

## What you need

- A **1996 to 1999 E36** (sedan, coupe, convertible, 318ti or M3) with the OBD-II port under the dashboard, on the driver's side.
- A **K+DCAN cable** (the USB cable BMW owners use with INPA). A cheap Bluetooth or Wi-Fi OBD reader won't work.
- A Windows, Mac or Linux laptop.
- **Optional:** a 20-pin round adapter for the plug under the bonnet, if you have one (step 7).
- **Optional:** INPA/EDIABAS already installed on a Windows laptop (step 9).

## Before you start

1. Install the latest Klemme15 from the [website](https://expos-ai.github.io/Klemme15/) or [Releases](https://github.com/Expos-AI/Klemme15/releases). On first launch, read the short safety notice and tick the box.
2. Park somewhere safe: level ground, handbrake on, automatic in P or manual in neutral. Run the engine only outdoors.
3. Set the cable's switch to **K-line** (on cables with a switch). Plug the cable into the OBD-II port and the laptop.
4. Switch the ignition **on** (key position 2, engine off).

## The test, step by step

Write down or screenshot what you see at each step. "It said X" is more useful than "it didn't work".

**1. Pick your car.** In the **Garage**, choose **E36 beta** and your model. Read the yellow beta note.

**2. Check the cable.** Press **Connect to my car**, choose the cable's port, then press **Check cable**. Note what it says about the cable (FTDI or not, latency), and fix anything it flags.

**3. Connect.** Press **Connect**. Note whether it connects and what the header says.

**4. Quick test.** On the **Vehicle** page, press **Run quick test**. Note **which modules answered** (green) and which didn't. Screenshot the car drawing.

**5. Find other modules.** Still on the **Vehicle** page, press **Find other modules**. It asks every other address on the K-line, which takes a few minutes. **Screenshot the result**: the addresses and part numbers it lists tell us exactly where your car's modules are.

**6. Engine.** With the **engine running** and warm (coolant above 70 °C):
   - **Faults** page: note the engine fault codes it shows, and whether they match what your car has (a check engine light, a code you read elsewhere).
   - **Live data**: pick rpm, coolant temperature, short-term and long-term fuel trims, and record a minute or two. Do the values look right? Does rpm match the tachometer?
   - **Tests** → **Fuel trim test**: follow it to the end. Note the verdict.

**7. The round plug under the bonnet (optional).** If you have a 20-pin adapter: ignition off, unplug the cable from the OBD-II port, plug the adapter into the round connector under the bonnet, ignition on, then repeat steps 3 to 5. Some E36 modules may only answer there. Note which answered this time.

**8. Clear the engine's faults (optional).** On the **Faults** page, clear the engine faults and read them again. Note whether they cleared. Skip this if you need your codes for a repair, and **don't do it right before a smog check or emissions inspection**: clearing faults also resets the readiness monitors, and the car needs a few days of driving to set them again.

**9. EDIABAS mode (optional, Windows only, and only if you already have INPA/EDIABAS installed).** Disconnect, then in the **Garage**'s cable panel tick **Use my own BMW files (EDIABAS mode)**, check the folder it shows (usually `C:\EDIABAS\Ecu`), press **Connect** and repeat step 4. This shows whether Klemme15 can read the other modules' faults through your INPA files. If you use INPA anyway, it's also the perfect reference: note which modules INPA reaches on your car (and whether through the OBD-II port or the round plug), so we can compare them with what Klemme15 found.

Please don't use the **Service** or **Coding** pages on your E36: there's nothing for the E36 there yet.

## Sending your results

1. On the **Faults** page, under **Share a snapshot of your car**, write a short note (year, model, engine, which steps you did), tick **Traffic log**, and press **Save snapshot...**. The traffic log is the most useful part: it shows exactly what Klemme15 asked and what your car answered. The VIN is removed (a VIN split across two messages can slip through the traffic log, so skim it if that matters to you).
2. Also save the **Log** page (**Save log...**), and keep your screenshots from steps 4, 5 and 7.
3. Put everything in a `.zip`, then either open an [E36 beta report](https://github.com/Expos-AI/Klemme15/issues/new?template=e36-beta.yml) and attach it, or send it to whoever asked you to test.

## What happens next

Every module your car answers gets added at its real address, and a second test round checks them. Once an E36 has confirmed them, they're marked as confirmed for every E36 owner. Thank you!

*Klemme15 is free, comes with no warranty, and isn't affiliated with BMW AG. See [Safety, legal and transparency](LEGAL.md).*
