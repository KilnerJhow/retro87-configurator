# Contributing to Retro87

Thank you for your interest. This project talks directly to real hardware, so the rules below
are part of its safety contract, not bureaucracy.

## Before you start

1. **Do not commit vendor artifacts.** All research material — the official app, decompiled
   files, captures, firmware, dumps, and analysis scripts — belongs in `local/`, which Git
   ignores. Read [`docs/PROVENANCE.md`](docs/PROVENANCE.md).
2. **Do not add firmware writing.** This is a product decision: the app only checks the version.
3. **Writing to the keyboard is irreversible.** Every change to the write path needs a backup,
   readback, and a documented reversible test.

## Environment

- .NET 8 SDK (`dotnet --version` should show 8.x).
- Linux: `hidraw` + the [`packaging/linux/70-8bitdo.rules`](packaging/linux/70-8bitdo.rules) rule.
- Windows: builds and probes work; `WinExe` has no console, so use the DLL for the CLI.

## Build and tests

```bash
dotnet build src/Retro87/Retro87.csproj -c Release
dotnet test tests/Retro87.Tests/Retro87.Tests.csproj -c Release
./scripts/verify.sh
```

On Windows:

```powershell
pwsh ./scripts/verify.ps1
```

`--selftest` covers models, codecs, layout, macros, profiles, and a protocol dry run against a
simulated keyboard. The xUnit tests cover the same hardware-free layer and the `--report` report.
**Neither proves hardware behavior** — that requires a session with the device.

### Preview the screen without a keyboard

```bash
./scripts/screenshots.sh [directory]   # default: /tmp/retro87-shots
```

The tests in `tests/Retro87.Tests/Screens` open the actual window (Avalonia.Headless + Skia) with a
fake factory configuration and save PNGs: the five tabs and selected key popovers, in pt and en.
For a new screen, use `ScreenHarness` (`Select`, `SelectTab`, `Save`). Only the screen with a
connected keyboard and known family (Config fields) is not yet covered; no test opens HID.

Before opening a PR, also run the guards:

```bash
./scripts/check_vendor_artifacts.sh
./scripts/check_udev_rule.sh
./scripts/check_version.sh
./scripts/check_spdx.sh
```

## Hardware

- Start with `--plan` for any write probe.
- Never run a write probe without explicit authorization from the keyboard owner.
- Prefer a harmless key (Scroll Lock, Pause, PrtSc, Ins, Home, F13–F16).
- Check by reading back and undo the change; the test only passes with **0 differences** from the backup.
- The keyboard sleeps after link loss and **only a keypress wakes it**; the app's wake command
  does not cover that state (see `docs/PROTOCOL.md` §20).

## Protocol

- New protocol facts go in `docs/PROTOCOL.md` (Xbox) or `docs/PROTOCOL.md` (JP),
  with the command, frame, and evidence.
- Do not copy code from the official app. Document the wire fact and implement from that document.
- Changed the record layout? Update `docs/SUPPORT_MATRIX.md` and the `--selftest` dry run.
- New PIDs: add them to `Models/KeyboardModels.cs` (`KnownDevices`) and the detection test.

## Style

- C# nullable is enabled; avoid `!` without justification.
- Every `.cs`, `.axaml`, and script starts with `SPDX-License-Identifier: GPL-3.0-or-later`
  (`scripts/check_spdx.sh`).
- UI text goes through `Localization/Strings.cs` (Portuguese and English); code, comments, docs,
  and CLI output are in English.
- Status text must state **problem + next step**.
- Follow `DESIGN.md` (one amber accent, no shadows/gradients, AA).
- Comments explain why, not the obvious.

## Commits and PRs

- One topic per commit; use an imperative message (`fix:`, `feat:`, `docs:`, `test:`).
- Describe the hardware risk and what was checked without hardware.
- If the change touches the write path, include the `--plan` output and readback result.
- Keep `VERSION` and the `.csproj` in sync; otherwise the guard fails.

## License

By contributing, you agree that your contribution is licensed under **GPL-3.0-or-later**, the
repository's license (`LICENSE`). Do not submit third-party code under an incompatible license —
in particular, do not copy code from `openbitdo` without confirming compatibility first (it is
also GPL-3.0-or-later, so incorporating it is allowed when copyright notices are preserved).
