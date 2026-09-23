#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <KoishiPro2-source-root>" >&2
  exit 2
fi

SOURCE_ROOT="$(cd "$1" && pwd)"
AI_REPO='https://github.com/Snarkie/YGOProAIScript.git'
AI_COMMIT='1f57db863fb9b5a46e5fe17b7285bde82032e6e4'
CARD_SCRIPT_REPO='https://github.com/Fluorohydride/ygopro-scripts.git'
# Match the AI script era instead of feeding the 2017 Percy AI core modern
# Project-Ignis scripts that call APIs this legacy core does not implement.
CARD_SCRIPT_COMMIT='afbac0d80b7cca2770c071a6fd3f83e1718fad66'
WORK="${RUNNER_TEMP:-/tmp}/koishipro2-ai-pack"
rm -rf "$WORK"
mkdir -p "$WORK/ai-src" "$WORK/card-scripts"

git -C "$WORK/ai-src" init -q
git -C "$WORK/ai-src" remote add origin "$AI_REPO"
git -C "$WORK/ai-src" fetch --quiet --no-tags --depth=1 origin "$AI_COMMIT"
git -C "$WORK/ai-src" checkout --quiet --detach FETCH_HEAD
test "$(git -C "$WORK/ai-src" rev-parse HEAD)" = "$AI_COMMIT"
test -f "$WORK/ai-src/AI/ai.lua"
test -f "$WORK/ai-src/AI/decks/Blackwing.lua"
test -f "$WORK/ai-src/AI/decks/Shaddoll.lua"
test -f "$WORK/ai-src/LICENSE.md"

git -C "$WORK/card-scripts" init -q
git -C "$WORK/card-scripts" remote add origin "$CARD_SCRIPT_REPO"
git -C "$WORK/card-scripts" fetch --quiet --no-tags --depth=1 origin "$CARD_SCRIPT_COMMIT"
git -C "$WORK/card-scripts" checkout --quiet --detach FETCH_HEAD
test "$(git -C "$WORK/card-scripts" rev-parse HEAD)" = "$CARD_SCRIPT_COMMIT"
test -f "$WORK/card-scripts/constant.lua"
test -f "$WORK/card-scripts/utility.lua"
test -f "$WORK/card-scripts/LICENSE"
SCRIPT_COUNT=$(find "$WORK/card-scripts" -maxdepth 1 -type f -name 'c*.lua' | wc -l | tr -d ' ')
if [ "$SCRIPT_COUNT" -lt 1000 ]; then
  echo "Unexpectedly small historical card-script set: $SCRIPT_COUNT" >&2
  exit 1
fi

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

python3 - "$DATA_ZIP" "$WORK/ai-src/AI" "$WORK/ai-src/LICENSE.md" "$WORK/card-scripts" "$OUT_ZIP" <<'PY'
from pathlib import Path
import sys
import zipfile

data_zip = Path(sys.argv[1])
ai_root = Path(sys.argv[2])
ai_license = Path(sys.argv[3])
card_root = Path(sys.argv[4])
out_zip = Path(sys.argv[5])

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
        if path.is_file():
            z.write(path, f'ai/{path.relative_to(ai_root).as_posix()}')

    for name, data in decks.items():
        z.writestr(f'ai/ydk/{name}', data)

    # Legacy AI ocgcore's default_script_reader uses fopen(script_name), and
    # card scripts are requested as script/c<ID>.lua plus constant/utility.lua.
    for path in sorted(card_root.glob('*.lua')):
        z.write(path, f'script/{path.name}')

    z.write(ai_license, 'ai/Snarkie-YGOProAIScript-LICENSE.md')
    z.write(card_root / 'LICENSE', 'script/Fluorohydride-ygopro-scripts-LICENSE')
    z.writestr(
        'ai/KOISHIPRO2-AI-PACK-V2.txt',
        'Bundled offline AI runtime pack v2\n'
        'AI scripts: Snarkie/YGOProAIScript @ 1f57db863fb9b5a46e5fe17b7285bde82032e6e4\n'
        'Card scripts: Fluorohydride/ygopro-scripts @ afbac0d80b7cca2770c071a6fd3f83e1718fad66\n'
        'Starter AI decks: copied from KoishiPro2 bundled Blackwing/Shaddoll decks\n'
    )

with zipfile.ZipFile(out_zip, 'r') as z:
    names = set(z.namelist())
    required = {
        'ai/ai.lua',
        'ai/decks/Blackwing.lua',
        'ai/decks/Shaddoll.lua',
        'ai/ydk/Blackwing.ydk',
        'ai/ydk/Shaddoll.ydk',
        'ai/KOISHIPRO2-AI-PACK-V2.txt',
        'script/constant.lua',
        'script/utility.lua',
    }
    missing = sorted(required.difference(names))
    if missing:
        raise SystemExit(f'AI pack validation failed; missing {missing}')
    card_count = sum(1 for n in names if n.startswith('script/c') and n.endswith('.lua'))
    if card_count < 1000:
        raise SystemExit(f'AI pack contains too few card scripts: {card_count}')
    print(f'AI pack entries: {len(names)}')
    print(f'Historical card scripts: {card_count}')
PY

ls -lh "$OUT_ZIP"
echo "AI scripts: $AI_REPO@$AI_COMMIT"
echo "Card scripts: $CARD_SCRIPT_REPO@$CARD_SCRIPT_COMMIT"
echo "KoishiPro2 data source: $DATA_ZIP"
