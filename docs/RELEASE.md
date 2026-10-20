# Release checklist

## Before versioning

- [ ] `VERSION` and `<Version>` in the `.csproj` match (`./scripts/check_version.sh`).
- [ ] `CHANGELOG.md` contains the version section and date.
- [ ] `./scripts/verify.sh` passes (Release build + `--selftest` + `dotnet test`, 0 `FAIL`).
- [ ] `./scripts/check_vendor_artifacts.sh` passes.
- [ ] `./scripts/check_udev_rule.sh` passes.
- [ ] `./scripts/check_spdx.sh` passes.
- [ ] CI is green on Ubuntu and Windows.
- [ ] No vendor artifacts in `git ls-files`.

## Hardware (when a device is available)

- [ ] `Retro87 --xbox-probe` reads firmware/battery and the configuration summary.
- [ ] `Retro87 --report` generates TOML without errors.
- [ ] `Retro87 --xbox-write-test --plan` shows the plan without writing.
- [ ] A reversible write session ends with 0 differences from the backup.
- [ ] Windows window with the keyboard: open, read, edit, write, and read back.
- [ ] Linux window with the keyboard: same sequence (pending in 0.0.0).

## Packaging

- [ ] `./scripts/publish.sh linux-x64`.
- [ ] `./scripts/publish.sh win-x64`.
- [ ] Check that the `.tar.gz` contains README, VERSION, CHANGELOG, LICENSE, and the udev rule.
- [ ] Do not publish firmware or configuration dumps.

## Publication

- [ ] Annotated tag `vX.Y.Z`.
- [ ] Release notes copied from `CHANGELOG.md`.
- [ ] Artifact checksums.
- [ ] Review `docs/PROVENANCE.md` and `THIRD_PARTY_NOTICES.md` if dependencies or assets changed.
