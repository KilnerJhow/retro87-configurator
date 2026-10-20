#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# VERSION is the single source of truth for the release number. Keep the
# csproj in sync; this guard fails the build if they drift.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION_FILE="$(tr -d '[:space:]' < "$ROOT/VERSION")"
CSPROJ_VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/Retro87/Retro87.csproj" | head -n 1)"

if [[ -z "$VERSION_FILE" ]]; then
    echo "VERSION is empty" >&2
    exit 1
fi
if [[ "$VERSION_FILE" != "$CSPROJ_VERSION" ]]; then
    echo "VERSION ($VERSION_FILE) != <Version> no csproj ($CSPROJ_VERSION)" >&2
    exit 1
fi
echo "consistent version: $VERSION_FILE"
