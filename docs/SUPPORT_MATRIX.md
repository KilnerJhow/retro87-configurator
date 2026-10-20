# Support matrix

Legend:

- **validated** — exercised on the real device, with readback and undo for writes.
- **implemented** — code and dry run against a simulated keyboard; hardware session pending.
- **no** — outside the scope of this version.

## Families

| Feature | JP (`0x5200`/`0x5201`/`0xFF08`) | Xbox (`0x2028`/`0x202E`) |
|---|---|---|
| Vendor collection detection and selection | implemented | validated |
| Version / battery | no version command in this family | validated |
| Read configuration (1532 B) | implemented | validated |
| Write mapping (95 positions) | implemented | validated |
| Keyboard, media, and mouse targets | implemented | validated |
| Restore key to default | implemented | validated |
| Macros: read | implemented | validated |
| Macros: write (info + steps) | implemented | validated |
| Profile name | implemented | validated |
| Volume (1–5) | not applicable | validated |
| Quick Setup (Win / Alt+Tab / Alt+F4) | not applicable | validated |
| `special_feature` / `voice` | implemented | not applicable |
| 10 Super Buttons (KA/KB + A/B/X/Y) | implemented in UI | validated on 2026-10-19 (10 records + `WriteConfig`; 0 differences) |
| Identify pressed Super Button (HID reports) | implemented | validated via dongle on 2026-10-19 (media key; duplicate target prompts for port) |
| LED / theme (`cmd 0x0F`, 283 B) | no | no |
| Firmware notification (read-only) | implemented | implemented |
| Firmware writing | **no, by product decision** | **no, by product decision** |
| TOML diagnostic report | implemented | implemented |

## Transport

| Scenario | Status |
|---|---|
| USB cable (`0x2028`) | validated (reading and writing) |
| 2.4 G dongle (`0x202E`) | validated (reading and writing) |
| Windows window with keyboard | validated |
| Linux CLI (hidraw), reading | validated on 2026-10-19 |
| Linux CLI (hidraw), writing | validated on 2026-10-19 |
| `70-8bitdo.rules` udev rule | validated on 2026-10-19 (ACL recreated without the Steam rule; Bluetooth not exercised) |
| Linux window with keyboard | validated on 2026-10-19 (reading; writing through the UI not yet exercised) |
| Sleep after link loss | measured and documented (`docs/PROTOCOL.md` §20) |

## What the app does not do

- Does not download or write firmware.
- Does not control LED/theme in this version.
- Does not configure other models (Retro 68, Retro 108, controllers, mouse).
- Does not send telemetry; the only network activity is checking the published firmware version,
  and network failures are silent.

## Updating this matrix

A row only changes to **validated** after a documented device session, including the command
used, readback, and, for writes, undo with 0 differences. `--report` helps attach evidence
without exposing the full dump.
