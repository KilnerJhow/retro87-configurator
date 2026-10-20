#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# The udev rule is easy to get subtly wrong: TAG+="uaccess" is applied by
# systemd's 73-seat-late.rules, so a rule named 99-* silently grants nothing.
# This guard encodes the lesson from openbitdo and checks the live rule file.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RULE="$ROOT/packaging/linux/70-8bitdo.rules"
fail=0

if [[ ! -f "$RULE" ]]; then
    echo "udev rule not found: $RULE" >&2
    exit 1
fi

base="$(basename "$RULE")"
prefix="${base%%-*}"
if ! [[ "$prefix" =~ ^[0-9]+$ ]]; then
    echo "rule name without a numeric prefix: $base" >&2
    fail=1
elif (( 10#$prefix >= 73 )); then
    echo "rule '$base' sorts after 73-seat-late.rules; uaccess will not be applied" >&2
    fail=1
fi

grep -q 'SUBSYSTEM=="hidraw"' "$RULE" || { echo "missing SUBSYSTEM==\"hidraw\" in $base" >&2; fail=1; }
grep -q 'TAG+="uaccess"' "$RULE" || { echo "missing TAG+=\"uaccess\" in $base" >&2; fail=1; }
grep -q 'idVendor}=="2dc8"' "$RULE" || { echo "missing the VID 2dc8 restriction in $base" >&2; fail=1; }

# The old name must not come back.
if find "$ROOT" -path "$ROOT/.git" -prune -o -name '99-*8bitdo*.rules' -print -quit | grep -q .; then
    echo "found an old 99-*8bitdo*.rules rule; remove it" >&2
    fail=1
fi

if command -v udevadm >/dev/null 2>&1; then
    udevadm verify "$RULE"
fi

if [[ "$fail" -ne 0 ]]; then
    exit 1
fi
echo "regra udev ok: $base"
