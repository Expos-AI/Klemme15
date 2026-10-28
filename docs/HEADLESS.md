# Headless telemetry (Raspberry Pi, Grafana, MQTT)

`klemme15 serve` connects to the car and publishes its live data, with no window. Leave a cheap Raspberry Pi (or any Linux box) plugged into the OBD port with a K+DCAN cable and read the car from your phone, a Grafana dashboard or a home server. It is **read-only**: it never writes to the car.

```bash
klemme15 serve --port /dev/ttyUSB0 --vehicle E9x --model 335i
```

## HTTP

A small web server runs on port 8080 (change it with `--http`):

- `http://<pi>:8080/` — a live page you can open on a phone.
- `http://<pi>:8080/live.json` — the latest values as JSON (CORS open, so a web app can fetch it):
  ```json
  { "time": "...", "values": { "rpm": { "value": 820, "unit": "rpm", "name": "Engine speed" }, ... } }
  ```
- `http://<pi>:8080/faults` — the current faults with their triage priority.
- `http://<pi>:8080/health` — read count, error count and the last error, for monitoring.

Choose the values with `--pids` (default: rpm, speed, coolant, iat, load, throttle, fuel trims, MAF, voltage) and the poll rate with `--interval` (seconds).

## MQTT

Add `--mqtt <host[:port]>` to publish each value to an MQTT broker (Mosquitto, Home Assistant, Node-RED):

```bash
klemme15 serve --port /dev/ttyUSB0 --mqtt 192.168.1.10 --mqtt-topic car/e90
# publishes car/e90/rpm, car/e90/coolant, ...
```

`--mqtt-user` and `--mqtt-pass` set credentials. Klemme15 publishes at QoS 0 and sends a keep-alive ping every 30 seconds. The MQTT client is built in, so no extra software is needed on the Pi.

## Grafana

Point a JSON or MQTT data source at the Pi. For Grafana's Infinity (JSON) plugin, use `http://<pi>:8080/live.json` and map `values.rpm.value` and so on. For MQTT, subscribe to `klemme15/#`.

## Running it on boot

A systemd service keeps it running:

```ini
[Unit]
Description=Klemme15 telemetry
After=network.target

[Service]
ExecStart=/opt/klemme15/klemme15 serve --port /dev/ttyUSB0 --vehicle E9x --mqtt 192.168.1.10
Restart=always
User=pi

[Install]
WantedBy=multi-user.target
```
