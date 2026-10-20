#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Renders the window with no display and no keyboard (Avalonia.Headless) and saves PNGs.
#
# Usage: scripts/screenshots.sh [output-directory]   (default: /tmp/retro87-shots)
# Never opens HID or writes to the keyboard: it uses a fake factory configuration.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-/tmp/retro87-shots}"
rm -rf "$OUT"
RETRO87_SHOTS="$OUT" dotnet test "$ROOT/tests/Retro87.Tests" --filter "FullyQualifiedName~Screens" "${@:2}"
echo "PNGs in: $OUT"
ls "$OUT"
