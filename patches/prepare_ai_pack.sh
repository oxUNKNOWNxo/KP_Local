#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <KoishiPro2-source-root>" >&2
  exit 2
fi

SOURCE_ROOT="$(cd "$1" && pwd)"
AI_REPO='https://github.com/Snarkie/YGOProAIScript.git'
AI_COMMIT='1f57db863fb9b5a46e5fe17b7285bde82032e6e4'
WORK="${RUNNER_TEMP:-/tmp}/koishipro2-ai-pack"
rm -rf "$WORK"
mkdir -p "$WORK/scripts"

git -C "$WORK/scripts" init -q
git -C "$WORK/scripts" remote add origin "$AI_REPO"
git -C "$WORK/scripts" fetch --quiet --no-tags --depth=1 origin "$AI_COMMIT"
git -C "$WORK/scripts" checkout --quiet --detach FETCH_HEAD
test "$(git -C "$WORK/scripts" rev-parse HEAD)" = "$AI_COMMIT"
test -f "$WORK/scripts/AI/ai.lua"
test -f "$WORK/scripts/AI/decks/Blackwing.lua"
test -f "$WORK/scripts/AI/decks/Shaddoll.lua"
test -f "$WORK/scripts/LICENSE.md"

# macOS ships Bash 3.2, so avoid Bash 4-only mapfile/readarray.
DATA_LIST="$WORK/ygopro2-data-zips.txt"
find "$SOURCE_ROOT/Assets" -type f -name 'ygopro2-data.zip' -print > "$DATA_LIST"
DATA_COUNT=$(awk 'END { print NR + 0 }' "$DATA_LIST")
if [ "$DATA_COUNT" -ne 1 ]; then
  echo "Expected exactly one ygopro2-data.zip under Assets; found $DATA_COUNT" >&2
  cat "$DATA_LIST" >&2
  exit 1
fi
DATA_ZIP=$(sed -n '1p' "$DATA_LIST")
test -n "$DATA_ZIP"

OUT_DIR="$SOURCE_ROOT/Assets/StreamingAssets"
OUT_ZIP="$OUT_DIR/koishi-ai-pack.zip"
mkdir -p "$OUT_DIR"

python3 - "$DATA_ZIP" "$WORK/scripts/AI" "$WORK/scripts/LICENSE.md" "$OUT_ZIP" <<'PY'
from pathlib import Path
import sys
import zipfile

data_zip = Path(sys.argv[1])
ai_root = Path(sys.argv[2])
license_path = Path(sys.argv[3])
out_zip = Path(sys.argv[4])

with zipfile.ZipFile(data_zip, 'r') as z:
    names = {name.replace('\\', '/').lower(): name for name in z.namelist()}
    decks = {}
    for deck_name in ('Blackwing.ydk', 'Shaddoll.ydk'):
        key = f'deck/{deck_name}'.lower()
        source_name = names.get(key)
        if source_name is None:
            raise SystemExit(f'Missing bundled starter deck: {deck_name}')
        decks[deck_name] = z.read(source_name)

if out_zip.exists():
    out_zip.unlink()

with zipfile.ZipFile(out_zip, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for path in sorted(ai_root.rglob('*')):
        if not path.is_file():
            continue
        rel = path.relative_to(ai_root).as_posix()
        z.write(path, f'ai/{rel}')
    for name, data in decks.items():
        z.writestr(f'ai/ydk/{name}', data)
    z.write(license_path, 'ai/Snarkie-YGOProAIScript-LICENSE.md')
    z.writestr('ai/KOISHIPRO2-AI-PACK.txt',
               'Bundled offline AI pack\n'
               'AI scripts: Snarkie/YGOProAIScript @ 1f57db863fb9b5a46e5fe17b7285bde82032e6e4\n'
               'Starter AI decks: copied from KoishiPro2 bundled Blackwing/Shaddoll decks\n')

with zipfile.ZipFile(out_zip, 'r') as z:
    required = {
        'ai/ai.lua',
        'ai/decks/Blackwing.lua',
        'ai/decks/Shaddoll.lua',
        'ai/ydk/Blackwing.ydk',
        'ai/ydk/Shaddoll.ydk',
    }
    missing = sorted(required.difference(z.namelist()))
    if missing:
        raise SystemExit(f'AI pack validation failed; missing {missing}')
    print(f'AI pack entries: {len(z.namelist())}')
PY

ls -lh "$OUT_ZIP"
echo "AI scripts: $AI_REPO@$AI_COMMIT"
echo "KoishiPro2 data source: $DATA_ZIP"
