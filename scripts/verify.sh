#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Build the app and run its hardware-free self-test.
#
# This is the same gate CI runs. It never opens a HID device and never writes
# to a keyboard: the self-test uses the simulated devices in Protocol/.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NO_BUILD=0
for arg in "$@"; do
    case "$arg" in
        --no-build) NO_BUILD=1 ;;
        *) echo "uso: $0 [--no-build]" >&2; exit 2 ;;
    esac
done

if [[ -n "${DOTNET:-}" ]]; then
    DOTNET_BIN="$DOTNET"
elif command -v dotnet >/dev/null 2>&1; then
    DOTNET_BIN="dotnet"
elif [[ -x "$HOME/.dotnet/dotnet" ]]; then
    DOTNET_BIN="$HOME/.dotnet/dotnet"
    export DOTNET_ROOT="$HOME/.dotnet"
else
    echo "dotnet not found (install the .NET 8 SDK or set DOTNET=...)" >&2
    exit 127
fi

if [[ "$NO_BUILD" -eq 0 ]]; then
    echo "== dotnet build (Release) =="
    "$DOTNET_BIN" build "$ROOT/src/Retro87/Retro87.csproj" -c Release --nologo
fi

LOG="${TMPDIR:-/tmp}/retro87-selftest.log"
echo "== Retro87.Bench --selftest =="
set +e
"$DOTNET_BIN" run --project "$ROOT/tools/Retro87.Bench" -c Release --nologo -- --selftest >"$LOG" 2>&1
rc=$?
set -e

ok=$(grep -c '^ok' "$LOG" || true)
fail=$(grep -c '^FAIL' "$LOG" || true)
echo "ok:   $ok"
echo "FAIL: $fail"
tail -n 3 "$LOG"

if [[ "$rc" -ne 0 || "$fail" -ne 0 ]]; then
    echo
    echo "Failures (showing the first 40 FAIL lines):"
    grep -n '^FAIL' "$LOG" | head -n 40 || true
    echo "log completo: $LOG"
    exit 1
fi

echo "SELFTEST PASSED"

echo "== dotnet test (xUnit, no hardware) =="
if [[ "$NO_BUILD" -eq 0 ]]; then
    "$DOTNET_BIN" test "$ROOT/tests/Retro87.Tests/Retro87.Tests.csproj" -c Release --nologo
else
    "$DOTNET_BIN" test "$ROOT/tests/Retro87.Tests/Retro87.Tests.csproj" -c Release --no-build --nologo
fi
echo "TESTS PASSED"
