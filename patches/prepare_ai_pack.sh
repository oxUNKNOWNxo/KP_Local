#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <KoishiPro2-source-root>" >&2
  exit 2
fi

SOURCE_ROOT="$(cd "$1" && pwd)"
AI_REPO='https://github.com/Snarkie/YGOProAIScript.git'
AI_COMMIT='1f57db863fb9b5a46e5fe17b7285bde82032e6e4'
CARD_REPO='https://github.com/Fluorohydride/ygopro-scripts.git'
# Pin card scripts to the same period as the restored AI-enabled ocgcore
# (YGOProUnity_V2 was committed 2019-04-30). Newer EDOPro scripts use APIs that
# this 2019 core does not implement.
CARD_COMMIT='a314224f9a96068b8dc33f7297170e167d5701b5'
WORK="${RUNNER_TEMP:-/tmp}/koishipro2-ai-pack"
rm -rf "$WORK"
mkdir -p "$WORK/ai" "$WORK/cards"

git -C "$WORK/ai" init -q
git -C "$WORK/ai" remote add origin "$AI_REPO"
git -C "$WORK/ai" fetch --quiet --no-tags --depth=1 origin "$AI_COMMIT"
git -C "$WORK/ai" checkout --quiet --detach FETCH_HEAD
test "$(git -C "$WORK/ai" rev-parse HEAD)" = "$AI_COMMIT"
test -f "$WORK/ai/AI/ai.lua"
test -f "$WORK/ai/AI/decks/Blackwing.lua"
test -f "$WORK/ai/AI/decks/Shaddoll.lua"
test -f "$WORK/ai/LICENSE.md"

git -C "$WORK/cards" init -q
git -C "$WORK/cards" remote add origin "$CARD_REPO"
git -C "$WORK/cards" fetch --quiet --no-tags --depth=1 origin "$CARD_COMMIT"
git -C "$WORK/cards" checkout --quiet --detach FETCH_HEAD
test "$(git -C "$WORK/cards" rev-parse HEAD)" = "$CARD_COMMIT"
test -f "$WORK/cards/constant.lua"
test -f "$WORK/cards/utility.lua"
test -f "$WORK/cards/c89631139.lua"
test -f "$WORK/cards/LICENSE"

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

python3 - "$DATA_ZIP" "$WORK/ai/AI" "$WORK/ai/LICENSE.md" "$WORK/cards" "$OUT_ZIP" <<'PY'
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

card_scripts = sorted(p for p in card_root.glob('*.lua') if p.is_file())
if len(card_scripts) < 1000:
    raise SystemExit(f'Unexpectedly small 2019 card script set: {len(card_scripts)}')

if out_zip.exists():
    out_zip.unlink()

with zipfile.ZipFile(out_zip, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for path in sorted(ai_root.rglob('*')):
        if path.is_file():
            z.write(path, f'ai/{path.relative_to(ai_root).as_posix()}')
    for name, data in decks.items():
        z.writestr(f'ai/ydk/{name}', data)
    z.write(ai_license, 'ai/Snarkie-YGOProAIScript-LICENSE.md')

    # The restored 2019 ocgcore calls default_script_reader(), which opens
    # ./script/constant.lua, ./script/utility.lua and ./script/c<ID>.lua from
    # the filesystem. These files are therefore part of the offline AI runtime,
    # not optional data.
    for path in card_scripts:
        z.write(path, f'script/{path.name}')
    z.write(card_root / 'LICENSE', 'script/Fluorohydride-ygopro-scripts-LICENSE')

    z.writestr(
        'ai/KOISHIPRO2-AI-PACK.txt',
        'Bundled offline AI pack\n'
        'AI scripts: Snarkie/YGOProAIScript @ 1f57db863fb9b5a46e5fe17b7285bde82032e6e4\n'
        'Card scripts: Fluorohydride/ygopro-scripts @ a314224f9a96068b8dc33f7297170e167d5701b5 (2019-04-30)\n'
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
        'script/constant.lua',
        'script/utility.lua',
        'script/c89631139.lua',
    }
    missing = sorted(required.difference(names))
    if missing:
        raise SystemExit(f'AI pack validation failed; missing {missing}')
    card_count = sum(1 for n in names if n.startswith('script/c') and n.endswith('.lua'))
    if card_count < 1000:
        raise SystemExit(f'AI pack card script count too small: {card_count}')
    print(f'AI pack entries: {len(names)}')
    print(f'Bundled card scripts: {card_count}')
PY

ls -lh "$OUT_ZIP"
echo "AI scripts: $AI_REPO@$AI_COMMIT"
echo "Card scripts: $CARD_REPO@$CARD_COMMIT"
echo "KoishiPro2 data source: $DATA_ZIP"
