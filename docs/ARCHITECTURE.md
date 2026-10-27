# Architecture

OpenHood is split so that each layer can be tested without the one below it, and so that the protocol stack never depends on BMW files.

## Projects

| Project | Depends on | Purpose |
|---|---|---|
| `OpenHood.Core` | nothing | Models (`EcuDefinition`, `Fault`, `EcuScanResult`...), `IDiagnosticBackend`, `VehicleScanner`, `ReportWriter`, `TrafficLog` |
| `OpenHood.Protocols` | Core | Framing (KWP2000/BMW-FAST, DS2, ISO 9141-2), channels, OBD-II PIDs, `NativeBackend`, `VirtualCar` simulator |
| `OpenHood.Data` | Core | The open database (embedded JSON) plus per-user overrides |
| `OpenHood.Serial` | Protocols | `SerialPortTransport` for the real K+DCAN cable, port discovery |
| `OpenHood.Ediabas` | Core, EdiabasLib | `EdiabasBackend`: runs the user's SGBDs. Windows only |
| `OpenHood.Cli` | all above | Command-line tool |
| `OpenHood.App` | all above | Avalonia desktop app (MVVM) |
| `src/Shared` | | `BackendFactory`, compiled into App and CLI |

## The layers

**Transport** (`ITransport`): a byte pipe with baud/parity, break, DTR/RTS and the DSR ignition line. Synchronous on purpose, because K-line init timing is measured in milliseconds. `LoggingTransport` wraps any transport and feeds the traffic log.

**Channels** speak one protocol each:

- `BmwFastChannel`: KWP2000 over D-CAN at 115200 baud. The K+DCAN cable turns these serial frames into CAN. Handles "response pending" (7F xx 78) and echo.
- `Ds2Channel`: BMW DS2 on the K-line, 9600 baud 8E1, with busy-retry and echo filtering.
- `KLineObdChannel`: generic OBD-II at 10400 baud. ISO 14230 fast init first, then ISO 9141-2 5-baud init. Keeps the session alive and re-initializes after DS2 has used the line.

**Backends** (`IDiagnosticBackend`) turn "identify / read faults / clear / live data" into channel calls. Each backend serializes access with a semaphore because there is one physical bus.

- `NativeBackend` picks a channel per module from its `BusProtocol` and the cable mode.
- `EdiabasBackend` resolves an SGBD for the module (from `sgbd` in the database, or the `D_00xx` convention), then runs `IDENT`, `FS_LESEN`, `FS_LOESCHEN` or any job.

**Scanner** (`VehicleScanner`): identify each module with a short timeout; only modules that answer get their fault memory read. That is what keeps a quick test fast. Cancellation is honored between and during requests.

## Adding a vehicle

1. Add modules to `src/OpenHood.Data/db/modules.json` (address, protocol, SGBD names).
2. Add the family to `vehicles.json` with the cable switch position.
3. Optionally add a `VirtualCar` profile and a test.

## Adding a protocol (e.g. ENET for F-series)

Write a transport (TCP) and a channel (HSFZ/UDS), then either extend `NativeBackend` or add a new backend. The scanner, app and reports need no changes.

## Testing without a car

`VirtualCar` implements `ITransport` and answers like real ECUs, including K-line echo, fast init, DS2 and BMW-FAST. Every test and the app's demo mode use it. When you capture traffic from a real car, turn it into a test so regressions are caught.
