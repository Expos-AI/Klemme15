# Community reading maps

A reading map is a small JSON file that says where values sit in a module's reply: which request to send, which bytes and bits hold the value, and how to scale it. With a map, Klemme15 can show those values on a car without anyone's BMW files.

Status: the decoder is tested; reading a car with a map is **pending field validation**, and no maps ship with Klemme15 yet.

## Rules

1. **Maps describe reads, nothing else.** Klemme15 refuses a map whose request isn't one of three read requests (`21`, `22`, `1A`). A map can't write to a module, drive a part, clear faults or reset anything.
2. **Work it out on a car.** Change something you can measure another way (engine speed, a switch, a temperature), record it, and see which bytes follow. Every value needs an `observed` note saying how you established it. Never copy offsets, names or scaling from BMW software or files: a map made that way isn't yours to share, and it won't be accepted.
3. **Say which car.** `confirmedOn` names the chassis, engine and year you saw it on. Other module versions may differ.

## Making one

1. Record a session while the value changes in a way you know: `klemme15 live --port COM3 --record idle.k15log`, or tick **Record my sessions** in the Garage ([recordings](RECORDINGS.md)).
2. List what was asked and answered: `klemme15 recording idle.k15log --text`.
3. Write the map, then check it against the recording: `klemme15 decode --map my-map.json --recording idle.k15log` prints each value with its time. Compare with what you measured.
4. Try it on the car: `klemme15 mapped --map my-map.json --module <id> --port COM3`.

`klemme15 decode --map my-map.json --reply "61 05 07 D0 80"` decodes a single reply.

## The file

```json
{
  "maps": [
    {
      "module": "EXAMPLE1",
      "request": "21 05",
      "author": "your name or handle",
      "fields": [
        { "name": "Example speed", "unit": "rpm", "byte": 2, "bits": 16, "factor": 0.5,
          "observed": "Held the engine at 1,000 and 2,000 rpm by the rev counter; the value followed.",
          "confirmedOn": "chassis, engine, year" },
        { "name": "Example switch", "byte": 4, "bit": 7, "bits": 1,
          "observed": "Switched it on and off five times; the bit followed each time." }
      ]
    }
  ]
}
```

That example is made up to show the format; it isn't a real module or request.

| Field | Meaning |
|---|---|
| `module` | The module as Klemme15 names it (see `klemme15 vehicles`), or its version as the quick test shows it |
| `request` | The read request, as hex |
| `replyPrefix` | What a good reply starts with. Leave it out for the usual case: the request with 0x40 added to its first byte |
| `byte` | The value's first byte, counting from the start of the reply (the first byte is 0) |
| `bits` | How many bits the value has (1 to 32) |
| `bit` | Bits to skip at the low end (0 to 7); 0 for whole bytes |
| `signed`, `littleEndian` | For negative values, and for values stored low byte first |
| `factor`, `offset` | Shown value = raw value × factor + offset |

## Sharing

Open a pull request or an issue with the map and the evidence: what you changed on the car and what the value did. See [CONTRIBUTING-DATA.md](CONTRIBUTING-DATA.md).
