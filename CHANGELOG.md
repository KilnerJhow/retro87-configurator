# Changelog

All notable changes to this project are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/). The first version is `0.0.0`.

## [Unreleased]

### Added

- Battery level in the header, next to the keyboard name: drawn battery icon plus percent, green from 40%, amber from 15%, red below. Shown only when the family reports it (Xbox).
- Screen tests without a keyboard (`tests/Retro87.Tests/Screens`, Avalonia.Headless + Skia): open
  the window with a fake factory configuration, check that the key popover fits inside the window,
  and save PNGs of the five tabs, in pt and en. `scripts/screenshots.sh [directory]` runs them and lists the files.
- Portuguese and English interface (`Localization/`), with a selector in the header; changes take
  effect after restarting. Preference stored in `~/.config/Retro87/language.txt`.
- **About the keyboard** tab (last), with "Check for updates".
- Original SVG artwork for the interface (`src/Retro87/Brand/skin-*.svg`): keyboard plate,
  calibration strip, and Super Button interface panel, including the variant with Mecha Break
  symbols (**» ◆ X ▾**). Rendered with `Avalonia.Svg.Skia`.
- Super Button identification: the app now reads the keyboard's **HID input reports directly**
  (keyboard, consumer control, and mouse collections) instead of relying solely on window events.
  The **Listen for Super Button** button identifies the button and port even when the desktop
  consumes the media key or the window is unfocused, and the **Last** line shows the port with the
  Mecha Break symbol. When two Super Buttons use the same target (e.g. two ports on Play/Pause),
  the app asks which port was pressed, listing only the candidates.
- `--xbox-super-listen [--seconds N]` probe: reads the configuration and prints each Super Button
  press with port and target, decoded from HID reports (read-only).
- Toggle between **A/B/X/Y** ports and **Mecha Break symbols** in the UI (default: symbols).
- `--xbox-input-probe [--seconds N]` probe: prints input reports from each HID collection
  (read-only), used to investigate Super Button presses.
- Linux write validated with the real keyboard: reversible probe (`cmd 0x01`) and UI path (`WriteConfig`), with backup, readback, and
  **0 differences** across the 1532 bytes at the end.
- Linux Avalonia window validated: connection and
  reading with the real keyboard (profile `Skipult`, 85 keys, 10 Super Buttons).
- udev rule validated from scratch: with Steam's
  `2dc8` rule disabled, `70-8bitdo.rules` recreated the ACL and granted the session user access
  to the `hidraw` nodes.
- `--xbox-super-test [--plan]` probe: reversibly tests the ten Super Button records and the UI's
  `WriteConfig` path, with backup and a final byte-for-byte comparison
  (10 records written and restored; 0 differences).
- xUnit tests in `tests/Retro87.Tests` (`dotnet test`) for Super Buttons, the write plan, and the
  `--report` report. `scripts/verify.sh` and CI now run `dotnet test` alongside `--selftest`.

### Fixed

- **UI:** `Connect`, `Read from keyboard`, and `Write to keyboard` blocked the UI thread during
  HID I/O. They now run off the UI thread, show the operation in the status bar and an
  indeterminate progress bar; failures remain visible with error severity.
- **UI:** the amber "Super Button writing not yet validated" warning became a neutral note after
  validation of the ten records on the device on 2026-10-19.

### Changed

- Reorganized tabs: Mapping (first and initially open), Macros, Settings, Profiles, About the keyboard.
- The key editor is a popover next to the key, replacing the side panel. Super Buttons sit next
  to the ports. The "modified" dot was removed from the key (the amber border is enough).
- Simple names for targets and categories (e.g. `NoVoice` → Mute).
- Removed the "Listen for Super Button" button: capture is automatic.
- `SPDX-License-Identifier: GPL-3.0-or-later` headers in C#, XAML, and script sources, enforced by
  `scripts/check_spdx.sh`.
- Research material (official app, decompiled files, captures, notes, and analysis scripts) moved
  to `local/`, which Git ignores. `.gitignore` covers builds and `local/`.

## [0.0.0] - 2026-10-19

First version. An experimental prototype written by AI — read the notice in
[`README.md`](README.md) before use, especially before writing to the keyboard.

### Added

- Reading and writing Retro 87 configuration for the JP and Xbox families.
- Remapping of the 95 positions, with keyboard, media, and mouse targets.
- 10 Super Buttons with selection by connection (KA/KB, A, B, X, Y).
- Editor for 8 macro slots (name, cycles, interval, and steps).
- Settings tab: volume and Quick Setup (Xbox) and special feature (JP).
- Local JSON profiles, automatic backup, and readback after writing.
- Explicit write lock per connection.
- Read-only firmware notification; no firmware writing.
- Reversible bench probes (`--xbox-*`) with `--plan` mode.
- Hardware-free self-test (`--selftest`) with dry run against a simulated keyboard.
- `--report [file.toml]`: read-only diagnostic report with identity, family, firmware, profile name,
  non-default keys, named macros, and Xbox family fields.
- `--version` and `--help` in the CLI.
- `scripts/verify.sh` / `scripts/verify.ps1` and `scripts/publish.sh` scripts.
- `scripts/check_vendor_artifacts.sh`, `scripts/check_udev_rule.sh`, and
  `scripts/check_version.sh` guards.
- GitHub Actions CI (Ubuntu + Windows) and Dependabot for NuGet and Actions.
- Documentation in [`docs/`](docs/): protocol, support matrix, architecture, provenance,
  release, and third-party notices in [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
- Linux read validated with the real keyboard (firmware 1.07,
  battery 100%, 12 non-default keys; no writes).

### Changed

- License set to **GPL-3.0-or-later** ([`LICENSE`](LICENSE)), aligned with the 8BitDo
  interoperability ecosystem.
- The root README became the project's entry point.
- Reverse-engineering working notes (dossier, journal, and analysis scripts) remain
  **outside version control**; the protocol is described in [`docs/PROTOCOL.md`](docs/PROTOCOL.md).

### Fixed

- **Linux:** the `99-*` udev rule applied `TAG+="uaccess"` after `73-seat-late.rules` and did not
  grant `hidraw` access. The prefix is now `70-`, in
  [`packaging/linux/70-8bitdo.rules`](packaging/linux/70-8bitdo.rules), with `SUBSYSTEM=="hidraw"`
  and Bluetooth coverage.

### Security

- Vendor artifacts (decompiled files, binaries, captures, firmware, dumps) stay outside version
  control and are blocked by `scripts/check_vendor_artifacts.sh`.
- Provenance boundary documented in [`docs/PROVENANCE.md`](docs/PROVENANCE.md).
- Explicit notice that the project was AI-generated and may contain errors.

[Unreleased]: https://github.com/KilnerJhow/retro87-configurator/commits/main
[0.0.0]: https://github.com/KilnerJhow/retro87-configurator/commits/main
