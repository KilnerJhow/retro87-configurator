#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Ensures the SPDX header in the tracked sources (C#, XAML, and scripts).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

missing=0
count=0
while IFS= read -r file; do
    count=$((count + 1))
    if ! head -n 5 "$file" | grep -q 'SPDX-License-Identifier: GPL-3.0-or-later'; then
        echo "no SPDX: $file"
        missing=$((missing + 1))
    fi
done < <(git ls-files '*.cs' '*.axaml' 'scripts/*.sh' 'scripts/*.ps1')

if [[ "$missing" -ne 0 ]]; then
    echo "missing SPDX in $missing of $count file(s)." >&2
    exit 1
fi

echo "SPDX ok: $count file(s)"
