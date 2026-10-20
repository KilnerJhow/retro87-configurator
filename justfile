set shell := ["bash", "-euo", "pipefail", "-c"]

default:
    @just --list

# Builds in Release.
build:
    dotnet build src/Retro87/Retro87.csproj -c Release --nologo

# Opens the window in Debug.
run:
    dotnet run --project src/Retro87

# Build + self-test + xUnit tests, no hardware.
test: build
    dotnet src/Retro87/bin/Release/net8.0/Retro87.dll --selftest
    dotnet test tests/Retro87.Tests/Retro87.Tests.csproj -c Release --nologo

# The full CI gate: guards + build + self-test + xUnit tests.
check: guards test

# Repository guards (artifacts, udev, version).
guards:
    ./scripts/check_vendor_artifacts.sh
    ./scripts/check_udev_rule.sh
    ./scripts/check_version.sh
    ./scripts/check_spdx.sh

# Build + self-test with the canonical script.
verify:
    ./scripts/verify.sh

# Read-only diagnostic report.
report:
    dotnet src/Retro87/bin/Release/net8.0/Retro87.dll --report

# Build self-contained (linux-x64, win-x64, osx-arm64).
publish rid="linux-x64":
    ./scripts/publish.sh {{rid}}

# Formatting (requires dotnet-format).
format:
    dotnet format src/Retro87/Retro87.csproj
