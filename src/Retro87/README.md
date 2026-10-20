# Retro87 — 8BitDo Retro 87 configurator (Linux)

Linux port of "8BitDo Ultimate Software V2", focused on the **8BitDo Retro 87** keyboard
("JP" protocol family). An Avalonia/.NET 8 app that talks directly to the keyboard's HID
interface through HidSharp — no official program or Windows required.

## Features

- **Read** the keyboard configuration (95-key remapping, macros, special feature, voice).
- **Adjust device configuration** in the *Settings* tab: on the Xbox family (Retro 87 "X"),
  **volume** (`volume_lv`, 1–5 as in the official app) and the three **Quick Setup** locks
  (disable the Windows key, Alt + Tab, and Alt + F4, bits of `fast_setting`); on JP, *special feature*.
- **Remap** any key: each row in the "Mapping" tab has a target selector grouped by category
  (keyboard / shortcut-media / mouse), type-to-search (just type over the combo), an
  "only modified keys" filter, and per-key reset.
- **Edit macros**: the 8 slots with name, cycles, interval, and a step list
  (Press / Release / Wait), with add, reorder, and remove actions.
- **Local profiles**: save/load configuration as one JSON file per profile in
  `~/.config/Retro87/profiles` (saving locally does not touch the keyboard; it is your backup before writing).
- **Firmware notification**: only **checks** the published version and **notifies** when a new one exists.

> **Writing firmware is prohibited in this program.** Firmware is written exclusively through
> the official program on Windows. This app only provides notifications.

## Requirements

- Linux with `hidraw` access (any distro with udev).
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build.
- Retro 87 keyboard connected by USB or the 2.4 G dongle (VID `0x2DC8`).

## Build and run

```bash
cd src/Retro87
dotnet build
dotnet run                       # opens the window
```

The binary is in `bin/Debug/net8.0/`. Run the self-test through the **DLL**:

```bash
dotnet bin/Debug/net8.0/Retro87.dll --selftest
# expected: SELFTEST PASSED, no FAIL
```

On Windows this is required (`WinExe` has no console of its own, so the `.exe` prints nothing).
On Linux the executable also works: `./bin/Debug/net8.0/Retro87 --selftest`.

The self-test covers layout tables, the payload ↔ id codec, UTF-16 names, macro body/chunking,
record decoding, profile round-trips, and editor view-models — all **without hardware**.
It also includes a **protocol dry run**: `JpProtocol` is exercised end to end against a simulated
keyboard (`Protocol/FakeKeyboardDevice.cs`) — reading a device configured "externally", writing
to a new device, and reading back for comparison, plus failure paths (lost frame, response to
another command, no ACK). There are ~383 checks; it takes a few seconds (the protocol uses `Sleep`).

**Read-only** hardware probe for the Retro 87 "X" (**Xbox** family, PID 0x2028/0x202E):

```bash
dotnet bin/Debug/net8.0/Retro87.dll --xbox-probe
# lists HID collections, finds the vendor collection (out=64), prints firmware/battery + config summary
```

**Identify a Super Button through HID reports** (without the window; shows port and target):

```bash
dotnet bin/Debug/net8.0/Retro87.dll --xbox-super-listen --seconds 30
# press the buttons; each line shows the target, port (A/B/X/Y), and Super Button
```

The app reads input reports directly from `hidraw` (keyboard, consumer control, and mouse), so
a media key consumed by the desktop is still identified. If two Super Buttons share a target,
the device does not say which one was pressed — the UI asks for the port, listing the candidates.

The keyboard **sleeps** after inactivity (and the dongle without a keyboard responds `02 04 C0 …`
to everything). The app sends the same wake *handshake* as the official software (`cmd 0x08 sub 0`)
on connection and reads again after failure; to exercise that frame manually:

```bash
dotnet bin/Debug/net8.0/Retro87.dll --xbox-wake --plan     # ONLY prints the frame (nothing sent)
dotnet bin/Debug/net8.0/Retro87.dll --xbox-wake            # sends wake and reports responses
dotnet bin/Debug/net8.0/Retro87.dll --xbox-wake --force    # sends wake even if already awake
dotnet bin/Debug/net8.0/Retro87.dll --xbox-wake --watch 15 # waits silently for 15 min, tests recovery
```

> **Important (measured on 2026-10-18):** when the keyboard **loses the link** — dongle removed,
> or PC powered off, cutting dongle power — it sleeps and **stops listening to the dongle**:
> the wake handshake does not bring it back (tested 1× and 19× in a row). **Only a keypress wakes
> it.** The app states this in the status bar and *Device* tab; software cannot resolve it alone.
> The dongle itself also goes silent after several idle minutes (likely system USB suspension).

To measure this behavior (`--gap` is how long the dongle stays unplugged; `--insist` repeats
wake every 500 ms):

```bash
dotnet bin/Debug/net8.0/Retro87.dll --xbox-link-test --gap 60 --insist 20
# asks you to unplug the dongle, waits, asks you to replug, and probes immediately — read-only
```

**Reversible** device write test (only with your explicit authorization — it explains the
frames before anything else):

```bash
dotnet bin/Debug/net8.0/Retro87.dll --xbox-write-test --plan   # READ ONLY: backup + exact frames
dotnet bin/Debug/net8.0/Retro87.dll --xbox-write-test          # runs, checks each step, and undoes
dotnet bin/Debug/net8.0/Retro87.dll --xbox-restore <backup.bin> # restores the 1532 bytes of a backup
```

The test selects a harmless key from the device's actual state (Scroll Lock, Pause, PrtSc, Ins,
Home, F13–F16), writes the same record (no-op), remaps, undoes, repeats through the UI path
(with the `cmd 0x08` open/close pair), and finally compares all **1532 bytes** against the backup.
It **aborts if reading is unstable** — which happens when the **official app is open** (it keeps
writing to the keyboard). This is how writing was validated on hardware (`docs/PROTOCOL.md` §14).

**Macro** write test (`cmd 0x05`/`0x06`), also reversible and requiring authorization:

```bash
dotnet bin/Debug/net8.0/Retro87.dll --xbox-macro-test --plan   # READ ONLY: 10-slot table + plan + frames
dotnet bin/Debug/net8.0/Retro87.dll --xbox-macro-test          # writes an empty slot, reads back, undoes
```

It reads the ten slots with `cmd 0x07`, selects a harmless key and the first **empty slot ≥ 1**
(leaving the official app's slot 0 intact; prefers 1), writes the macro (info + steps), checks
byte for byte through `cmd 0x07` and configuration readback, then undoes — requiring **0
differences** in the 1532 B and all slots in their original state (`docs/PROTOCOL.md` §15).

**Profile name** and **single-byte** test (`volume_lv@0x5C9`), also reversible:

```bash
dotnet bin/Debug/net8.0/Retro87.dll --xbox-config-test --plan  # READ ONLY: current name/byte + frames
dotnet bin/Debug/net8.0/Retro87.dll --xbox-config-test         # writes (32 B UTF-16BE + 1 byte), checks, undoes
```

It writes the byte first with its own value (no-op), then with the alternative (`1..2`, the
values used by the official app), changes the name to `"RT87 TEST"`, checks each step by reading
back, and undoes — requiring **0 differences** from the backup (`docs/PROTOCOL.md` §16).

**Media and mouse target** test (what a key emits), through the UI path:

```bash
dotnet bin/Debug/net8.0/Retro87.dll --xbox-media-test --plan  # READ ONLY: current record + frames
dotnet bin/Debug/net8.0/Retro87.dll --xbox-media-test         # Play (MKEY) and Left_Click (MOUSE), checks, undoes
```

It uses the first harmless key that also has a position in the UI table, prefers `Play` (`0xCD`,
`MKEY`) and `Left_Click` (`0xE8`, `MOUSE`), and writes through `WriteConfig` with the selector's
category — validating what the *Write to keyboard* button does with media and mouse targets
(`docs/PROTOCOL.md` §17). Dual-meaning ids (e.g. `0xE9` = middle button *or* VoiceUp) remain
rejected without a category.

The dry run proves **self-consistency against the frame model**, not hardware behavior:
it catches wrong offsets or broken chunks in the port, but the real keyboard session confirms the model.

**Settings tab fields** test (volume and Quick Setup), through the UI path:

```bash
dotnet bin/Debug/net8.0/Retro87.dll --xbox-ui-config-test --plan  # READ ONLY: current values + frames
dotnet bin/Debug/net8.0/Retro87.dll --xbox-ui-config-test         # writes, checks, and undoes
```

It writes **volume** (`volume_lv@0x5C9`, 1→3) and **Quick Setup** (`fast_setting@0x5C8`, enabling
only "Disable Alt + F4") through `WriteConfig` — the same primitive as the *Write to keyboard*
button — checks each step through the region, readback, **and the projection shown in the Settings
tab**, undoes both, and requires **0 differences** in the 1532 B (`docs/PROTOCOL.md` §19).
This was the **first validated write through the 2.4 G dongle** (`2dc8:202e`): all previous
writes (C1–C5) used a cable. It enables the wake handshake because the dongle sleeps.

## Linux permissions (udev)

The user needs access to the keyboard's `hidraw` interface. Install the rule included in
[`packaging/linux/70-8bitdo.rules`](../../packaging/linux/70-8bitdo.rules):

```bash
sudo install -m 0644 packaging/linux/70-8bitdo.rules /etc/udev/rules.d/70-8bitdo.rules
sudo udevadm control --reload-rules
sudo udevadm trigger
```

Then reconnect the keyboard and log out and back in. The rule contains:

```udev
KERNEL=="hidraw*", SUBSYSTEM=="hidraw", ATTRS{idVendor}=="2dc8", TAG+="uaccess"
KERNEL=="hidraw*", SUBSYSTEM=="hidraw", KERNELS=="*:2DC8:*", TAG+="uaccess"
```

`TAG+="uaccess"` grants the active session access without `sudo`. **The filename must sort
before `73-seat-late.rules`**: that systemd file converts the tag into an ACL.
A `99-*.rules` file applies the tag too late and grants nothing — this was the defect fixed
in this revision. A quick alternative (testing only) is running as root, which is not recommended.

## Usage

1. **Connect**: choose the keyboard from the list and click *Connect*.
2. **Read from keyboard**: brings the current configuration onto the screen.
3. Edit what you need (*Mapping*, *Macros*, *Settings* tabs).
4. **Profiles → Save the current profile**: keeps a local backup. **Do this before writing.**
5. **Write to keyboard**: check **"Allow writing to this keyboard"** and click *Write to keyboard* —
   the app automatically backs up the device's previous state (`backups` directory), then sends
   the configuration. Without a read (or loaded profile) in the session, *Write to keyboard*
   **refuses**: otherwise it would overwrite the device with the screen defaults.

Buttons are disabled during an operation, and numeric fields (cycles 0–255, interval/delay
0–65535 ms) are limited by the controls themselves.

The **interface is original**, not a copy of the official software: slate-gray background,
subtle cards, one amber accent (selected tab, "modified key" marker, focus), uppercase sections,
and technical values in monospace. Each tab has an **empty state** stating the next step
(instead of blank lists), the notice that nothing is written without permission stays at the
top, and the status bar always shows the last action. To review a visual change, capture
**only the app window** in a PNG.

## Status and limitations

- JP family protocol documented in [`../../docs/PROTOCOL.md`](../../docs/PROTOCOL.md).
- **Xbox family (Retro 87 "X", PID 0x2028/0x202E)** — the user's keyboard: its own protocol
  documented in [`../../docs/PROTOCOL.md`](../../docs/PROTOCOL.md) and implemented in
  `Protocol/XboxJpProtocol.cs`: PID detection, version/battery, 1532 B configuration reads,
  and **macro reads** (cmd 0x07), validated on real hardware, and now also **writes** — 12 B
  per-key records (cmd 0x01 at `0x28 + i·12`), profile name (32 B UTF-16BE at offset 4),
  individual bytes (e.g. volume at `0x5C9`), and **macros** (cmd 0x05 → 0x06), all using
  the same flow captured from the official app. **Mapping** writes (`cmd 0x01`, including the
  UI path) were validated on the device on 2026-10-17 (`docs/PROTOCOL.md` §14); **macro** writes
  (`cmd 0x05`/`0x06`, with `mapping = slot × 0x1000` and `cmd 0x07` readback) in `§15`;
  **profile name** and **single-byte** writes in `§16`; and **media/mouse target** writes
  (through the UI path) in `§17` — all checked by reading back and undone, ending identical to
  the backup. The *Settings* tab fields (**volume** and **Quick Setup**) were written in `§19`,
  the **first validated write through the 2.4 G dongle** (previous writes used a cable).
  Since keyboard writes are irreversible, the UI requires checking **"Allow writing to this
  keyboard"** and saves an **automatic backup** of its previous state (`backups`, alongside profiles).
  The Xbox layer has its **own id table** (136 ids with name and type, in
  `Models/XboxIdTable.g.cs`, documented in `docs/PROTOCOL.md`), and the target selector follows
  the connected family, so media and mouse targets can now be written (`Play` is `0xCD`,
  `Next`/`Last` are `0xB5`/`0xB6`, …) and appear with the right names. The device uses some ids
  with **two meanings** (`0xE2` is *Left Alt* or *NoVoice*, `0xE9` is the middle mouse button or
  *VoiceUp*, `0x6F`/`0x70` are F20/F21 or brightness): these are only written when the type comes
  from the selector — otherwise the app **refuses and warns**, rather than guessing.
  A key with "no target" writes the app's `None` (`NONE_MAP` = `0xF3`).
  Xbox configuration fields (**volume** 1–5 and **Quick Setup**: disable the Windows key,
  Alt + Tab, and Alt + F4) are in *Settings* and use the same single-byte write validated on the device.
  The **LED/theme** subsystem (`cmd 0x0F`, 283 B, `*_theme` records) remains outside v1,
  and the device's `fnLock` has no control in the official app (the port reads and returns the byte intact).
- **Write by difference**: writing starts from the read configuration and sends only changes
  (keys whose target is unchanged keep the device's existing record byte for byte, including its
  type); `force` rewrites all known positions.
- The **protocol dry run** (in `--selftest`) already validates the port's logic end to end
  against a simulated keyboard: read → write → readback is stable, every catalog target
  survives writing, and retry paths work.
- **Not yet validated on real hardware** (read → write → recheck). Check first: response
  offsets and ACK timing on the real device, the 21-byte shift in `writeMacroJP`, and
  modifier macro-step `Op` (`0x83`/`0x03`).
- `WriteConfig` only writes keys whose target differs from the default (parity with the original);
  this is why per-key reset uses *Restore all* + `Write to keyboard`. It also does not write the
  **profile name** (the original does not call `writeName`) and **ignores unnamed macro slots** —
  both match the original and are covered by the dry run.

## Structure

| Directory | Contents |
| --- | --- |
| `Protocol/` | JP framing and commands (`JpProtocol`, `JpWire`), HID transport, simulated dry-run keyboard |
| `Models/` | mapping tables, payload codec, layout, target catalog, models |
| `Services/` | device monitor, local profiles, firmware notification |
| `ViewModels/`, `Views/` | interface MVVM |
| `linux/` | udev rule |
| `SelfTest.cs` | hardware-free self-test (`--selftest`) |
