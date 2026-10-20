# Protocol

Wire facts for the 8BitDo Retro keyboard families this app talks to. This document is written
in terms of the device and the bytes on the wire; it does not reference any vendor source or
binary. Anything marked **dry run** was verified only against the simulated device in
`--selftest`, not on hardware.

> Read-only facts below were confirmed with the keyboard connected; writes were exercised with
> reversible probes that read back and undo themselves.

## Families

| Family | PIDs | Record |
|---|---|---|
| JP | `0x5200` (2.4 G), `0x5201` (USB), `0xFF08` | 95 mapping positions, 8 macro slots |
| Xbox | `0x2028` (USB), `0x202E` (2.4 G), `0x3026`/`0x3027` (UK) | 1532-byte record |

## Xbox family

### Transport

- Vendor HID collection, usage page `0xFFA0`; input report id `0x02`, output report id `0x81`,
  up to 63 data bytes per report.
- Request (64 bytes): `[0]=0x81 [1]=0x04 [2]=cmd [3]=sub [4]=len [5]=checksum [6..9]=offset u32 LE
  [10..]=data` (zero-padded).
- Reply (64 bytes): `[0]=0x02 [1]=0x04 [2]=0x03 [3]=cmd [4]=count [5]=checksum [6..9]=offset`,
  with data from byte **10**. A refusal is `02 04 C0`.
- A chunk carries at most `0x35` (53) data bytes. A reply is waited for up to 250 ms; a chunk is
  retried up to 30 times. A late reply to the previous chunk must be rejected by comparing the
  echoed command, offset and checksum — otherwise the whole record shifts by one chunk.

### Commands

| cmd | Meaning |
|---|---|
| `0x01` | write a range |
| `0x02` | read a range |
| `0x05` | macro prepare (offset = slot base) |
| `0x06` | macro write (data + sum, offset) |
| `0x07` | macro read |
| `0x08` | session; sub `0` before a read/write, sub `1` starts live key reports |
| `0x09` | status |
| `0x0A` | version; reply byte 10 = battery %, bytes 11..12 = firmware u16 big-endian (e.g. `0x006B` = 1.07) |
| `0x0B` | dongle status |
| `0x0D`/`0x0E`/`0x0F` | lights prepare / write / read (not used by this app) |

### Record (1532 bytes, `0x5FC`)

| Offset | Size | Field |
|---|---|---|
| `0x000` | 4 | flag; `0x20200902` marks the slot as holding a profile |
| `0x004` | 32 | profile name, UTF-16 **big-endian** |
| `0x024` | 1 | profile switch (1 while the keyboard's Profile button is on) |
| `0x025` | 1 | fn-lock byte, kept as read |
| `0x026`, `0x027` | 1 each | sleep / lights-off minutes (Retro 68 and Riviera only) |
| `0x028` | 1440 | key table: 120 entries × 12 bytes |
| `0x5C8` | 1 | lock bits: `0x1` Windows, `0x2` Alt+Tab, `0x4` Alt+F4 |
| `0x5C9` | 1 | volume, 1–5 |
| `0x5CA` | 1 | theme in use |
| `0x5CC` | 4 | lights idle time (300 = 5 min) |
| `0x5D0`+ | — | theme blocks |
| `0x5F9`, `0x5FA` | 1 each | per-key lights switch, dynamic-lighting switch |

**Key table entry (12 bytes).** `keyCode` (u8) at +0; +1..+3 unused; `mapping` (u32 LE) at +4;
`mappingType` (u32 LE) at +8.

- `mappingType`: `0` = the key as itself, `1` = keyboard, `2` = consumer/media, `3` = mouse,
  `4` = macro.
- For a macro, `mapping` is the macro slot's device offset (`slot × 0x1000`), not an id.
- Indexes: 0–7 modifiers; 9 and 10 the A/B buttons; 12–107 the ordinary keys except 58;
  108–115 the K1–K8 port buttons. On the UK variant, 108 is the ISO key and K1–K8 are 109–116.

### Macros

- Ten slots of 4096 bytes; a slot starts at `slot × 0x1000`.
- Header at the slot base (48 bytes): flag u32 (`0x20200902`), keyCode u8, name 32 bytes
  UTF-16 big-endian, step count u16, interval u16 (ms between repeats), repeat u8
  (`0` = until stopped, else 1–99), play mode u8.
- Steps start at slot base **+ `0x40`**, 4 bytes each: action u8 (`1` press, `2` release,
  `3` wait), key usage u8, duration u16 ms. At most 1008 steps.
- Taking a macro off a key rewrites only the key's entry; the slot is left as it is.

### Input reports (Retro 87 "X")

The keyboard collection (`hidraw` interface 1) exposes three input reports; the mouse collection
(interface 0) exposes a fourth. The first byte is the report id.

| Id | Size | Layout | Used for |
|---|---|---|---|
| `0x01` | 8 B | modifier byte + 6 key usages | boot keyboard |
| `0x02` | 3 B | 16-bit consumer usage, little-endian (`CD 00` = Play) | media keys |
| `0x03` | 17 B | modifier byte + 120 bits, usages `0x00`..`0x77` | NKRO keyboard |
| `0x04` | 5–6 B | 5 buttons, X, Y, wheel | mouse |

- The **17-byte report is the ordinary NKRO keyboard report**, not a Super Button event: a normal
  key produces `03 <mods> <bitmap>` with the bit of its usage set.
- A Super Button produces exactly the target stored in its record: a media target appears as
  report `0x02`, a keyboard target as report `0x03`. **The device does not say which of the ten
  buttons was pressed**; two buttons with the same target are indistinguishable at this level.
- Reading these reports is independent of the desktop: a media key that the session consumes
  before the window still reaches every reader of the `hidraw` node.
- The vendor collection (interface 2, `max input = 64`) carries command replies (report id `0x02`
  of that collection), not key presses; input listeners must skip it.

### Sleep and link loss (measured)

- The keyboard sleeps when it loses the link (dongle removed, PC powered off, long idle).
- The wake handshake (`cmd 0x08 sub 0`) does **not** bring it back in that state (tried 1× and
  19×): only a keypress does.
- After minutes idle the dongle itself goes silent and answers `02 04 C0` to everything.
- While the official application is open it keeps writing to the keyboard, so reads become
  unstable; the reversible probes refuse to run when the reading is not stable.

## JP family

- Output report id `0x52`: 33 bytes (report id + 32 payload). A jump variant uses `0xB2`.
- A read drops the input report id, so `resp[i] == rawReport[i + 1]`.
- Every write is followed by a read loop waiting for the acknowledgement `0xE4 0x08`.
- The feature-report path (id `7`, 64 bytes) is not used by the profile protocol.
- The family exposes no HID command that reports the firmware version; the official software
  shells out to an external tool, so this app reports version `0` for it.
- Record: 95 mapping positions (85 board keys + 10 extra), 8 macro slots, special feature and
  voice. Name write uses `0x70` (61 UTF-16 bytes in chunks of 29); macro write uses `0x76`
  (three bytes per step plus an optional four-byte delay entry).

## Reproducing on hardware

- Start with the read-only paths: `Retro87.Bench --xbox-probe` and `Retro87 --report`.
- Every write probe has a `--plan` mode that only reads and prints the frames it would send.
- A write probe reads back and undoes what it wrote; it only passes with **0 differences**
  against the backup. See [`../CONTRIBUTING.md`](../CONTRIBUTING.md).
