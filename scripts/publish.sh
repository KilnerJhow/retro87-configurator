#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Produce a self-contained, single-file build in dist/<rid>/.
#
#   ./scripts/publish.sh linux-x64
#   ./scripts/publish.sh win-x64
#   ./scripts/publish.sh osx-arm64
#
# Linux output includes the udev rule; every output includes README, VERSION,
# CHANGELOG and LICENSE so a tarball is self-describing.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RID="${1:-linux-x64}"
OUT="$ROOT/dist/$RID"

if [[ -n "${DOTNET:-}" ]]; then
    DOTNET_BIN="$DOTNET"
elif command -v dotnet >/dev/null 2>&1; then
    DOTNET_BIN="dotnet"
else
    DOTNET_BIN="$HOME/.dotnet/dotnet"
    export DOTNET_ROOT="$HOME/.dotnet"
fi

rm -rf "$OUT"
mkdir -p "$OUT"

"$DOTNET_BIN" publish "$ROOT/src/Retro87/Retro87.csproj" \
    -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -o "$OUT"

cp "$ROOT/README.md" "$ROOT/VERSION" "$ROOT/CHANGELOG.md" "$ROOT/LICENSE" "$OUT/"

if [[ "$RID" == linux-* ]]; then
    mkdir -p "$OUT/packaging/linux"
    cp "$ROOT/packaging/linux/70-8bitdo.rules" "$OUT/packaging/linux/"
fi

(
    cd "$ROOT/dist"
    tar -czf "retro87-${RID}.tar.gz" "$RID"
)

echo "done: $OUT"
echo "package: $ROOT/dist/retro87-${RID}.tar.gz"
