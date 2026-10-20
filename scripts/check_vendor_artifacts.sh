#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Keep vendor binaries, decompiled sources, firmware images, raw captures and the
# internal reverse-engineering notes out of version control. The clean protocol
# notes live in docs/PROTOCOL.md; the artifacts that produced them stay local
# (see docs/PROVENANCE.md).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    echo "warning: not a git repository; guard skipped"
    exit 0
fi

bad_paths='^local/|'
bad_paths+='^(decompiled/|app-oficial/|analysis/(extracted|native|captures|reampi)/)|'
bad_paths+='^(HANDOFF\.md|docs/0[12]-[a-z0-9-]+\.md)$|'
bad_paths+='^analysis/(8bitdoadvance\.snapshot|8bitdoadvance_exports\.txt|analyze_overview\.json|calls_test\.json|decompile_test\.json|rea_mcp_test_out\.json)$|'
bad_paths+='rea-evidence-bundle.*\.json$|'
bad_paths+='\.(dll|exe|pdb|so|dylib|ufw|pcapng|pcap|tsv|bin|zip|tar|gz)$'

bad="$(git ls-files | grep -E "$bad_paths" || true)"

if [[ -n "$bad" ]]; then
    echo "vendor/RE artifacts tracked by git:" >&2
    echo "$bad" >&2
    echo >&2
    echo "Remove them from the index (git rm --cached ...) and keep them in .gitignore." >&2
    exit 1
fi

# A second safety net: nothing huge should ever be committed.
huge="$(git ls-files -z | xargs -0 -I{} du -k "{}" 2>/dev/null | awk '$1 > 5120 {print $1" KB\t"$2}')"
if [[ -n "$huge" ]]; then
    echo "large files (over 5 MB) tracked:" >&2
    echo "$huge" >&2
    exit 1
fi

echo "no vendor artifacts tracked"
