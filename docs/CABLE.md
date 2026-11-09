# The cable

Most "it won't connect" problems are the cable or its settings, not the car or the software. This page covers choosing one, setting it up, and what to do when no module answers.

## Which cable

A **K+DCAN USB cable with a K-line / D-CAN switch and an FTDI chip**, the kind sold for INPA and NCS Expert.

- **The switch matters.** K-line for the E46, E39, E53, E38, Z4 E85/E86, X3 E83 (to 2006) and E36; D-CAN for the E90 family and newer. On K-line cars the cable has to connect OBD pins 7 and 8, and on D-CAN cars it mustn't, which is what the switch does. Cheap cables without a switch often have pins 7 and 8 bridged permanently: they work on K-line cars and not on D-CAN cars.
- **FTDI is what Klemme15 is tested with.** Cables with other chips (CH340, PL2303, CP210x) can work on K-line cars, but their timing is less dependable.
- Used for every real-car test so far: the [AntiBreak K+DCAN cable with switch](https://www.amazon.com/dp/B079KC43WG). Klemme15 isn't affiliated with any seller.

## Check cable

In the Garage, pick your car and press **Connect to my car**, then choose the cable's port and press **Check cable**. It also runs by itself before every connection. It reads what your computer knows about the cable (nothing is sent to the car) and tells you:

| Finding | What it means | What to do |
|---|---|---|
| FTDI chip | A genuine-looking FTDI chip, as K+DCAN cables use. | Nothing. |
| Probably a cloned FTDI chip | Its serial number, A50285BI, is shared by a huge number of cloned chips. Many work fine; they're the usual cause of dropped connections. | Carry on; if connections drop, try a cable with a genuine FTDI chip. |
| Product ID 0000 | An FTDI driver from 2014 did this to counterfeit chips. | Use a different cable. |
| Not an FTDI chip | A CH340, PL2303 or CP210x chip. | Fine on many K-line cars; get an FTDI cable if it drops out. |
| Latency set to 16 ms | The cable waits up to 16 ms before passing data on, which slows every exchange and can cause timeouts. | Set it to 1 ms (below). |
| Port in use | Another program (INPA, Tool32, NCS Expert, a terminal) has the cable open. | Close it and try again. |

A clone can only be suspected from these signs, never proven. The latency can be read on Windows and Linux; macOS doesn't let apps read it.

## Finding the port

- **Windows:** the cable shows up as a COM port (COM3, COM4…). Windows installs the FTDI driver by itself the first time you plug it in.
- **Mac:** it shows up as `/dev/cu.usbserial-…`, with Apple's built-in driver. Klemme15 lists only the `cu.` name: the matching `tty.` one waits for a signal the cable never sends, so it would hang.
- **Linux:** it shows up as `/dev/ttyUSB0`. If Klemme15 says Linux won't let it open the port, add yourself to the serial port group (`sudo usermod -aG dialout $USER` on Ubuntu, Debian and Fedora; `uucp` on Arch), then log out and back in.

If another program has the port open (INPA, ISTA, NCS Expert, a terminal), close it first: only one program can use the cable at a time.

## Set the latency to 1 ms

**Windows:** Device Manager → Ports (COM & LPT) → USB Serial Port (COMx) → Properties → Port Settings → Advanced → Latency Timer (msec): choose **1**, press OK, then unplug the cable and plug it back in.

**Linux:** `echo 1 | sudo tee /sys/bus/usb-serial/devices/ttyUSB0/latency_timer` (use your port's name). It resets when the cable is unplugged; a udev rule makes it permanent.

**macOS:** FTDI's own driver has the setting; Apple's built-in driver doesn't expose it. Klemme15 works with the default.

## No module answers

1. **Switch position:** K-line for the E46, E39, E53, E38, Z4 E85/E86, X3 E83 (to 2006) and E36; D-CAN for the E90 family and newer. Unplug, move the switch, plug back in.
2. **Ignition:** key position 2 (or Start once without the brake on push-button cars), engine off.
3. **The right port:** the cable's port, not a Bluetooth or built-in one. Press **Refresh** after plugging in.
4. **Battery:** below about 12 V many modules don't answer reliably. Put a charger on.
5. **Pins 7 and 8:** on a D-CAN car, if moving the switch changes nothing, the cable may have them bridged permanently. Try another cable.
6. **Still nothing:** save the traffic log (Log page) and [open an issue](https://github.com/Expos-AI/Klemme15/issues/new/choose).

## Wi-Fi cables

A cable behind a Wi-Fi bridge (ser2net on a Raspberry Pi, or an ESP32) works too: type `rfc2217://address:port` or `tcp://address:port` in the Garage instead of picking a port. It's best on D-CAN cars; K-line timing over Wi-Fi is less dependable.
