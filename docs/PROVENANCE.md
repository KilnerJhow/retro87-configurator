# Provenance and legal boundary

This document records where the project's knowledge came from and what **does not** belong in
version control.

## Origins

- The protocol is described in terms of the **device and bytes on the wire** in
  [`PROTOCOL.md`](PROTOCOL.md).
- Knowledge was obtained through interoperability analysis and read-only probes on a real
  keyboard. The raw research material (vendor application, captures, dumps, notes, and analysis
  scripts) is not part of this repository.
- The code in `src/Retro87/` is an original C# implementation. No vendor source code has been
  copied into `src/`.
- The SVG artwork in `src/Retro87/Brand/` is original (see
  [`../THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md)).

## What is not versioned

`.gitignore` keeps the whole `local/` directory out of Git, and
`scripts/check_vendor_artifacts.sh` blocks tracking vendor artifacts: decompiled code, the vendor
application, captures, firmware, and device dumps. Keep any local research material in `local/`
or outside the repository.

## Derived tables

`src/Retro87/Models/XboxIdTable.g.cs` is an id compatibility table (wire value, display name, and
type the device expects). It contains **factual interoperability data** confirmed against the
device and the wire notes in [`PROTOCOL.md`](PROTOCOL.md), not vendor code.

## Contribution rules

1. Do not copy snippets from the vendor application, decompiled files, or vendor DLLs.
2. Document the **wire fact** (command, offset, response) and implement from it.
3. Do not commit firmware, binaries, captures, or configuration dumps from a real keyboard.
4. Support reports (`--report`) are sanitized and can be shared; raw dumps cannot.

## License

The code is licensed under **GPL-3.0-or-later** ([`../LICENSE`](../LICENSE)). This project is
unofficial and unaffiliated with 8BitDo.
