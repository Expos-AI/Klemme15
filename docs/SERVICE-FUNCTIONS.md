# Service functions: full list

Generated from `src/Klemme15.Data/db/service.json` by `tools/service-catalog/build_service.py`. See [SERVICE.md](SERVICE.md) for how they run and how they are kept safe.

All functions are **experimental** until confirmed on a real car. Except where noted, they run BMW's own procedure and need your BMW files (EDIABAS mode).

## Service and maintenance

| Function | Module | When to use it | Module versions |
|---|---|---|---|
| Reset a service reminder | KOMBI / IKE | After an oil change, new brake pads, a brake fluid change or another service item. | 4 |
| Register a new battery (also without BMW files) | DME | After fitting a new battery of the same type and capacity as the old one. | 42 |
| Diesel particulate filter: after service work | DME | After replacing the particulate filter or its pressure sensor, after an oil change on a car that complained about oil dilution, or to clear a power reduction caused by the filter. | 7 |

## Engine

| Function | Module | When to use it | Module versions |
|---|---|---|---|
| Reset engine adaptations | DME | After replacing a sensor, injector, throttle body or intake part, or after fixing a vacuum leak, so old corrections don't linger. | 39 |
| Learn the Valvetronic end stops (also without BMW files) | DME | After replacing the Valvetronic motor, its sensor or the engine computer, or with Valvetronic faults after repairs. | 15 |
| Learn the intake (DISA) flap end stops | DME | After replacing the DISA actuator or the intake manifold. | 8 |
| Cylinder cut-out test (find a misfire) | DME | Rough idle or a misfire that doesn't point clearly at one cylinder. Test each cylinder in turn and compare. | 21 |
| Fuel tank leak test | DME | Evaporative system leak faults, or after replacing the fuel cap, purge valve or leak-detection pump. | 23 |
| Secondary air system check | DME | Secondary air faults, or after replacing the pump, valve or hoses. | 28 |
| Check the oxygen sensor plugs aren't swapped | DME | After replacing oxygen sensors on a six-cylinder with two catalysts, or with mixture faults that appeared right after such work. | 9 |
| Catalytic converter quick test | DME | Catalyst efficiency faults, or after replacing a catalytic converter. | 4 |
| Flush the VANOS units | DME | VANOS faults after an oil change with old, dirty oil, or after cleaning or replacing the VANOS solenoids. | 3 |
| Learn the gearbox neutral sensor | DME | After replacing the neutral position sensor, the gearbox or the engine computer on manual cars with Auto Start-Stop. | 11 |

## Component tests

| Function | Module | When to use it | Module versions |
|---|---|---|---|
| Test the radiator fan | DME | When you suspect the fan or its wiring (overheating at idle, fan faults). | 57 |
| Test the fuel pump | DME | Long cranking, no start, or low fuel pressure faults. | 22 |
| Test the map thermostat heater | DME | Thermostat faults, or a coolant temperature that never settles where it should. | 24 |
| Test the electric water pump | DME | Water pump faults or overheating warnings. | 9 |
| Test the secondary air pump | DME | Secondary air faults. | 16 |
| Test the throttle valve | DME | Throttle faults, or after cleaning the throttle body. | 10 |
| Test the intake (DISA) flap | DME | DISA faults, a flat spot in the mid-range, or a rattle from the intake. | 20 |
| Test the purge valve | DME | Purge or evaporative system faults, a rough idle right after refuelling, or a hard start after refuelling. | 28 |
| Check the fuel pump control module | EKPS | No start, fuel pump faults, or after replacing the pump or the control module. | 2 |

## Diesel

| Function | Module | When to use it | Module versions |
|---|---|---|---|
| Reset the glow plug values | DME | After replacing one or all glow plugs. | 7 |
| Learn the EGR valve or swirl flap positions | DME | After replacing or cleaning the EGR valve or the swirl flap actuator. | 7 |
| Reset diesel learned values | DME | After replacing the air mass meter, the oxygen sensor or all injectors. | 20 |
| Read the injector codes | DME | Before or after replacing an injector, to compare with the codes on the injectors. | 27 |
| Program a new injector's code | DME | After replacing a diesel injector. | 27 |
| Cylinder balance test (diesel) | DME | Rough idle or shaking at idle, to see whether one injector is out of line. | 28 |
| Reset the intercooler efficiency | DME | After cleaning or replacing the intercooler or its hoses. | 3 |
| Bleed the cooling system | DME | After draining or replacing coolant, the water pump, thermostat or radiator. | 2 |

## Brakes, DSC and steering

| Function | Module | When to use it | Module versions |
|---|---|---|---|
| Calibrate the steering angle sensor | DSC | After a wheel alignment, steering repairs, or when the steering angle reads off-centre with the wheels straight. | 5 |
| Zero the brake pressure sensor | DSC | After replacing the DSC unit or its pressure sensor, or with pressure sensor faults. | 3 |
| Zero the lateral acceleration sensor | DSC | After replacing the DSC sensor or the DSC unit, or with yaw/lateral sensor faults. | 3 |
| Zero the longitudinal acceleration sensor | DSC | After replacing the DSC sensor or the DSC unit, or with longitudinal acceleration sensor faults. | 5 |
| Bleed the ABS/DSC unit | DSC | After replacing the DSC unit, or when air got into the system (a spongy pedal that normal bleeding doesn't fix). | 10 |
| Reset the flat tyre monitor | DSC | After setting the tyre pressures, changing or rotating tyres, or when the flat tyre warning keeps coming back. | 3 |
| Test the wheel speed sensors | DSC | ABS or DSC faults for a wheel speed sensor, to find which sensor is dead or erratic. | 6 |
| Initialise active steering | AFS | After replacing the active steering unit, a steering angle sensor, or after a wheel alignment on cars with active steering. | 2 |
| Reset the electric power steering angle | EPS | After a wheel alignment or steering repairs on cars with electric power steering. | 2 |
| Calibrate the adaptive suspension steering angle | EDC | After a wheel alignment on cars with adaptive dampers (EDC). | 1 |
| Steering lock: clear the fault counters | CAS | Red steering wheel warning or 'steering lock' faults on an early E90/E91/E92/E93 with a key slot (no Comfort Access), after you have checked the battery. | 1 |

## Gearbox and drivetrain

| Function | Module | When to use it | Module versions |
|---|---|---|---|
| Reset automatic gearbox adaptations | EGS | After a gearbox fluid change or a repair, or when shifts have become harsh or slurred. | 21 |
| SMG: learn the clutch touch point | EGS | After a clutch replacement, SMG pump or actuator work, or when take-off is jerky. | 2 |
| SMG: learn the gear positions | EGS | After SMG actuator or gearbox work. | 2 |
| SMG: bleed the clutch hydraulics | EGS | After replacing the clutch slave cylinder, the hydraulic lines or the SMG pump. | 2 |
| xDrive transfer case: reset and relearn | VGSG | After replacing the transfer case actuator motor, the transfer case or its fluid, or with xDrive clutch faults. | 2 |
| xDrive transfer case: reset wear counters | VGSG | Only after fitting a new transfer case or new transfer case clutch. | 2 |
| xDrive transfer case clutch test | DSC | xDrive faults, binding in tight turns, or after transfer case work. | 5 |

## Body and comfort

| Function | Module | When to use it | Module versions |
|---|---|---|---|
| Initialise the windows (one-touch and anti-trap) | FRM | After the battery was disconnected or flat, or when one-touch window operation stopped working. | 2 |
| Initialise the sunroof | FZD | After the battery was disconnected, after sunroof repairs, or when the sunroof stops partway or won't do one-touch. | 1 |
| Calibrate the climate control flaps | IHKA | After replacing a flap motor or the climate control unit, or when air comes out of the wrong vents or a flap clicks. | 17 |
| Initialise the rain sensor | RLS | After replacing the windscreen or the rain sensor, or refitting the sensor's gel pad. | 1 |
| Test the parking sensors | PDC | Parking sensor faults or a sensor that beeps constantly. | 1 |
| Instrument cluster gauge test | KOMBI / IKE | A gauge that sticks or reads wrong, or a warning lamp you think is dead. | 7 |

## Recovery

| Function | Module | When to use it | Module versions |
|---|---|---|---|
| Restart the iDrive screen (CIC) | CIC | When the iDrive screen (CIC) has frozen or stopped responding but still answers the diagnostic cable. | 10 |
| Restart the central information display (CID) | CID | When the central information display (CID) has frozen or stopped responding but still answers the diagnostic cable. | 6 |
| Restart the Bluetooth/phone module (TEL) | TEL | When the Bluetooth/phone module (TEL) has frozen or stopped responding but still answers the diagnostic cable. | 9 |
| Restart the amplifier (AMP) | AMP | When the amplifier (AMP) has frozen or stopped responding but still answers the diagnostic cable. | 5 |
| Restart the climate control (IHKA) | IHKA | When the climate control (IHKA) has frozen or stopped responding but still answers the diagnostic cable. | 8 |
| Restart the parking sensors (PDC) | PDC | When the parking sensors (PDC) has frozen or stopped responding but still answers the diagnostic cable. | 4 |
| Restart the tyre pressure module (RDC) | RDC | When the tyre pressure module (RDC) has frozen or stopped responding but still answers the diagnostic cable. | 3 |
| Restart the roof function centre (FZD) | FZD | When the roof function centre (FZD) has frozen or stopped responding but still answers the diagnostic cable. | 2 |
| Restart the sunroof module (SHD) | SHD | When the sunroof module (SHD) has frozen or stopped responding but still answers the diagnostic cable. | 1 |
| Restart the audio module (ASK) | ASK | When the audio module (ASK) has frozen or stopped responding but still answers the diagnostic cable. | 2 |
| Restart the satellite radio (SDARS) | SDARS | When the satellite radio (SDARS) has frozen or stopped responding but still answers the diagnostic cable. | 2 |
| Restart the MOST gateway (MOSTGW) | MOSTGW | When the MOST gateway (MOSTGW) has frozen or stopped responding but still answers the diagnostic cable. | 8 |
| Restart the iDrive controller (ZBE) | ZBE | When the iDrive controller (ZBE) has frozen or stopped responding but still answers the diagnostic cable. | 7 |
| Restart the top-view camera (CCCA) | CCCA | When the top-view camera (CCCA) has frozen or stopped responding but still answers the diagnostic cable. | 4 |
| Restart the trailer module (AHM) | AHM | When the trailer module (AHM) has frozen or stopped responding but still answers the diagnostic cable. | 2 |
| Restart the convertible-top module (CVM) | CVM | When the convertible-top module (CVM) has frozen or stopped responding but still answers the diagnostic cable. | 10 |
| Restart the alarm module (DWA) | DWA | When the alarm module (DWA) has frozen or stopped responding but still answers the diagnostic cable. | 2 |
| Restart the passenger seat module (SMBF) | SMBF | When the passenger seat module (SMBF) has frozen or stopped responding but still answers the diagnostic cable. | 3 |
| Restart the driver seat module (SMFA) | SMFA | When the driver seat module (SMFA) has frozen or stopped responding but still answers the diagnostic cable. | 3 |
| Restart the antenna tuner (ANTTU) | ANTTU | When the antenna tuner (ANTTU) has frozen or stopped responding but still answers the diagnostic cable. | 1 |

## Retrofits and upgrades

| Function | Module | When to use it | Module versions |
|---|---|---|---|
| 3-stage intake manifold: check both flaps | DME | Right after the manifold swap, before the first drive. | 2 |
