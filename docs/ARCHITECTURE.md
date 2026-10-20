# Architecture

## Layers

```text
Views (Avalonia/XAML)
        │
ViewModels (MainWindowViewModel, JpProtocolFactory)
        │
Services (DeviceMonitor, SuperButtonInputListener, ProfileStore, ConfigBackup,
          FirmwareUpdateChecker, DiagnosticReport)
        │
Protocol (IJpProtocol, JpProtocol, XboxJpProtocol, JpWire, HidSharpTransport)
        │
Models (KeyboardLayout, MappingTables, codecs, geometry, XboxModels)
        │
HidSharp → hidraw / system HID
```

`JpProtocolFactory` selects the implementation by PID family:

- `JpProtocol` for `0x5200`, `0x5201`, `0xFF08`;
- `XboxJpProtocol` for `0x2028`, `0x202E`.

## Read flow

1. `DeviceMonitor` enumerates HID collections and groups them by physical identity
   (`DeviceIdentity.PhysicalKey`).
2. The UI (or `--report`) requests a session from `JpProtocolFactory`.
3. The protocol opens the vendor collection, performs the handshake when needed, and reads the record.
4. `KeyboardConfig` is the model shared by both families.

## Write flow

1. The UI only enables **Write to keyboard** with the lock checked and a read (or profile) loaded.
2. `ConfigBackup` saves what was read before any write frame.
3. `WriteConfig` compares the draft with the read state and sends only what changed (unless `force`).
4. After the ACK, the configuration is read back and the screen reflects the device.
5. Failure partway through: the status message names the problem and the next step.

## Super Button identification flow

1. On connection, `MainWindowViewModel` opens the physical keyboard's input collections
   (`SuperButtonInputListener`), skipping the 64-byte vendor collection.
2. `HidInputDecoder` tracks each report's state (`0x01` boot, `0x02` consumer, `0x03` NKRO,
   `0x04` mouse) and emits only press **transitions**.
3. The view-model converts the usage to the family's id (`MappingIdSpace.IdForUsage`) and looks
   for Super Button rows with that target and type.
4. One candidate: opens the port and selects the button. More than one (duplicate target): shows
   only candidates and asks for the port. None: ignores it unless the user is identifying a button.
5. The window event (`TryCaptureSuperButton`) remains a fallback when HID cannot be opened.

## Invariants

- No code path writes firmware.
- No writing without the explicit UI lock.
- No writing without a backup.
- The diagnostic report is read-only and does not include the raw 1532 bytes.
- HID collection selection uses the version-command response (Xbox) or the vendor interface (JP);
  never the first collection in the list.
- The protocol layer has no knowledge of Avalonia.

## Families and framing

| | JP | Xbox |
|---|---|---|
| Output report | family-specific | `0x81` |
| Input report | family-specific | `0x02` |
| Header | `JpWire` | 10 bytes (`cmd`, `sub`, `len`, `sum`, `offset`) |
| Chunk | depends on command | up to 53 B |
| Record | 95 positions | 1532 B (`0x5FC`) |
| Wake | not applicable | `cmd 0x08 sub 0` (does not cover link loss) |

## Hardware-free testing

`--selftest` runs:

- layout and geometry tables;
- payload ↔ id codec;
- UTF-16 name;
- macro body/chunking;
- record decoding;
- profile round-trip;
- editor view-models;
- `JpProtocol` and `XboxJpProtocol` dry runs against simulated devices, including a lost frame,
  a response to another command, and a missing ACK.

The dry run proves consistency against the frame model; it does **not** prove device behavior.

The xUnit tests (`tests/Retro87.Tests`) cover pure logic independent of the frame model
(Super Button indices and names, per-record write plan) and the `--report` report with an empty
device list. They do not prove device behavior either.

## Adding a command

1. Document the wire fact in `docs/`.
2. Add the constant/structure in `Models/` or `Protocol/`.
3. Implement it in the family's protocol.
4. Cover it in `--selftest` with a simulated device and/or in `tests/Retro87.Tests`.
5. If it writes, create/extend a reversible probe with `--plan`.
6. Update `docs/SUPPORT_MATRIX.md`.
