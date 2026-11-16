# Session recordings (.k15log)

A session recording holds everything Klemme15 asked your car and everything the car answered, with the time of each piece. Later, without the car, Klemme15 can play the file back: it connects to the recording as if it were the cable, and every page shows what the car said that day.

Status: validated with the demo cars. **Pending field validation** on a real car.

## What it's for

- **Bug reports.** Instead of describing what happened, send the recording privately to whoever is helping you. They open it and see the same answers Klemme15 saw, in the same order and with the same timing. Don't attach one to a public GitHub issue or forum post: it contains your VIN (see [Privacy](#privacy)).
- **Looking again later.** Record at the car, then go through the faults, module details and readings at a desk.
- **Working out values.** A recording is the raw material for a [community reading map](READING-MAPS.md).

## What it is not

It is not a recording of the car's internal networks. A K+DCAN cable on the diagnostic socket carries only the questions a diagnostic tool asks and the answers to them, so that is all any tool can record through it. A recording therefore holds what Klemme15 read during that session and nothing else: a page you didn't open while recording has nothing to show when you play it back.

## Recording

- **In the app:** in the Garage, under the cable settings, tick **Record my sessions, to play back later without the car**. Every connection to a real car is then saved in `recordings/sessions` under Klemme15's data folder. The newest 20 are kept.
- **Command line:** add `--record drive.k15log` to any command that talks to the car.

Recording isn't available in EDIABAS mode yet.

Recording doesn't slow the car's answers down: the cable code only copies each piece into a queue, and another thread compresses and writes it. The file is flushed about once a second, so a recording survives the laptop going to sleep or the app closing unexpectedly; such a file plays back up to the point where it stops.

## Playing back

- **In the app:** in the Garage, **Play back a recording...** and pick the file. The header says "Recording (playback)".
- **Command line:** `klemme15 scan --replay drive.k15log`, or any other command with `--replay`. `klemme15 recording drive.k15log` shows what a file holds, and `--text` lists every request and reply.

How playback answers:

- Each request is matched to the next identical request in the recording, and gets the reply the car gave then, after the same delay. Values that were read over and over move forward the way they did during the session, and start again from the beginning when the recording ends.
- A request the recording doesn't have gets no reply, exactly like a module that isn't fitted.
- Nothing is sent anywhere. Buttons that would change the car (clearing faults, service functions, coding) reach only the recording. Nothing from playback is added to the car's health history.

## Privacy

**A recording contains your VIN** and your modules' serial and part numbers, because the car sends them in its answers. Unlike a [snapshot](SNAPSHOTS.md), it can't be cleaned without changing the data it exists to preserve. Share a recording only with people you trust; for a public forum post, share a snapshot instead.

Recordings stay on your computer. Klemme15 never sends them anywhere.

A recording someone sends you is checked before it's played: a file that isn't a recording, is damaged, or unpacks to more than 256 MB is refused or cut off there.

## The file

`K15L`, a version byte, a short JSON header (Klemme15's version, the car family and model, the cable type, the start time; no VIN), then one Brotli-compressed stream of records. Each record is the time since the record before in microseconds, a kind (sent, received, line settings, note, end), a length and the bytes. Numbers are 7-bit variable-length integers. The code is in `src/Klemme15.Protocols/Recording`.
