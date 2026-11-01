#!/usr/bin/env bash
# Regenerates tests/OpenUI.Lang.Core.Tests/Prompt/Goldens from the pinned upstream checkout.
#
#   tools/prompt-goldens/run.sh <path/to/openui-upstream>
#
# Upstream is only read. Its lang-core sources are copied to a scratch directory and zod is
# installed there (postinstall scripts skipped), so the upstream clone never gets node_modules.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
upstream="$(cd "${1:?usage: run.sh <path to the openui-upstream checkout>}" && pwd)"

pinned="$(tr -d '[:space:]' < "$repo/tests/fixtures/UPSTREAM_COMMIT")"
actual="$(git -C "$upstream" rev-parse HEAD)"
if [[ "$pinned" != "$actual" ]]; then
  echo "upstream is at $actual but tests/fixtures/UPSTREAM_COMMIT pins $pinned" >&2
  exit 1
fi
if [[ -n "$(git -C "$upstream" status --porcelain -- packages/lang-core/src)" ]]; then
  echo "upstream packages/lang-core/src has local changes" >&2
  exit 1
fi

# Upstream's postinstall and prompt generation both send telemetry unless told not to.
export OPENUI_TELEMETRY_DISABLED=1 DO_NOT_TRACK=1 OPENUI_RUNTIME_TELEMETRY_ENABLED=0

scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
cp -r "$upstream/packages/lang-core/src" "$scratch/upstream-src"
cp "$here/package.json" "$here/generate.ts" "$here/cases.ts" "$scratch/"
if [[ -f "$here/bun.lock" ]]; then
  cp "$here/bun.lock" "$scratch/"
  (cd "$scratch" && bun install --ignore-scripts --frozen-lockfile)
else
  (cd "$scratch" && bun install --ignore-scripts)
  cp "$scratch/bun.lock" "$here/bun.lock"
fi

bun "$scratch/generate.ts" \
  --upstream "$scratch/upstream-src" \
  --library "$repo/tests/fixtures/1.0/library.json" \
  --out "$repo/tests/OpenUI.Lang.Core.Tests/Prompt/Goldens"
