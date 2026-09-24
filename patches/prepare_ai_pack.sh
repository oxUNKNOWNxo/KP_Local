#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <KoishiPro2-source-root>" >&2
  exit 2
fi

SOURCE_ROOT="$(cd "$1" && pwd)"

# Validate the generated AI menu helper before packaging data.
MENU_PATH="$SOURCE_ROOT/Assets/SibylSystem/Menu/Menu.cs"
python3 - "$MENU_PATH" <<'PY'
from pathlib import Path
import sys

p = Path(sys.argv[1])
t = p.read_text(encoding='utf-8-sig')
start = t.find('    private void EnableAiMenuEntry()')
end = t.find('    private void CreateSuperPreMenuItem()', start)
if start < 0 or end < 0:
    raise SystemExit('AI menu helper block not found before pack preparation')

block = t[start:end]
required = (
    'entry.name == "ai_"',
    'aiEntry.parent.name == "ai"',
    'System.Collections.Generic.List<Transform> columnItems',
    'Mathf.Abs(siblingPosition.x - p.x) > 12f',
    'float rowSpacing = (topY - bottomY) / (columnItems.Count - 1);',
)
missing = [item for item in required if item not in block]
if missing:
    raise SystemExit(f'AI menu helper validation failed; missing {missing}')
if '\\"' in block:
    raise SystemExit('AI menu helper still contains escaped C# quotes')

call_lines = [
    i for i, line in enumerate(t.splitlines())
    if line.strip() == 'EnableAiMenuEntry();'
]
registration_lines = [
    i for i, line in enumerate(t.splitlines())
    if line.strip() == 'UIHelper.registEvent(gameObject, "ai_", onClickAI);'
]
if len(call_lines) != 1:
    raise SystemExit(f'Expected exactly one AI activation call; found {len(call_lines)}')
if len(registration_lines) != 1:
    raise SystemExit(f'Expected exactly one active ai_ event registration; found {len(registration_lines)}')
if call_lines[0] > registration_lines[0]:
    raise SystemExit('AI menu activation must occur before the single active ai_ event registration')
PY

AI_REPO='https://github.com/Snarkie/YGOProAIScript.git'
AI_COMMIT='1f57db863fb9b5a46e5fe17b7285bde82032e6e4'
WORK="${RUNNER_TEMP:-/tmp}/koishipro2-ai-pack"
rm -rf "$WORK"
mkdir -p "$WORK/ai-src"

git -C "$WORK/ai-src" init -q
git -C "$WORK/ai-src" remote add origin "$AI_REPO"
git -C "$WORK/ai-src" fetch --quiet --no-tags --depth=1 origin "$AI_COMMIT"
git -C "$WORK/ai-src" checkout --quiet --detach FETCH_HEAD
test "$(git -C "$WORK/ai-src" rev-parse HEAD)" = "$AI_COMMIT"
test -f "$WORK/ai-src/AI/ai.lua"
test -f "$WORK/ai-src/AI/decks/Blackwing.lua"
test -f "$WORK/ai-src/AI/decks/Shaddoll.lua"
test -f "$WORK/ai-src/LICENSE.md"

# AI-specific data only. Card scripts are owned by the main KoishiPro2
# ygopro2-data.zip and synchronized separately by sync_main_scripts.sh.
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

python3 - "$DATA_ZIP" "$WORK/ai-src/AI" "$WORK/ai-src/LICENSE.md" "$OUT_ZIP" <<'PY'
from pathlib import Path
import sys
import zipfile

data_zip = Path(sys.argv[1])
ai_root = Path(sys.argv[2])
ai_license = Path(sys.argv[3])
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
        if path.is_file():
            z.write(path, f'ai/{path.relative_to(ai_root).as_posix()}')

    for name, data in decks.items():
        z.writestr(f'ai/ydk/{name}', data)

    z.write(ai_license, 'ai/Snarkie-YGOProAIScript-LICENSE.md')
    z.writestr(
        'ai/KOISHIPRO2-AI-PACK-V5.txt',
        'Bundled offline AI runtime pack v5\n'
        'AI scripts: Snarkie/YGOProAIScript @ 1f57db863fb9b5a46e5fe17b7285bde82032e6e4\n'
        'Card scripts: use KoishiPro2 main script/ directory\n'
    )

with zipfile.ZipFile(out_zip, 'r') as z:
    names = set(z.namelist())
    required = {
        'ai/ai.lua',
        'ai/decks/Blackwing.lua',
        'ai/decks/Shaddoll.lua',
        'ai/ydk/Blackwing.ydk',
        'ai/ydk/Shaddoll.ydk',
        'ai/KOISHIPRO2-AI-PACK-V5.txt',
    }
    missing = sorted(required.difference(names))
    if missing:
        raise SystemExit(f'AI pack validation failed; missing {missing}')

    forbidden_prefixes = ('script_current/', 'script_legacy/', 'script/')
    forbidden = sorted(
        n for n in names if n.startswith(forbidden_prefixes)
    )
    if forbidden:
        raise SystemExit(
            'AI pack must not contain card scripts; found '
            + ', '.join(forbidden[:20])
        )

    print(f'AI pack entries: {len(names)}')
    print('Card scripts: external main KoishiPro2 script/ directory')
PY

ls -lh "$OUT_ZIP"
echo "AI scripts: $AI_REPO@$AI_COMMIT"
echo "KoishiPro2 main card scripts are not duplicated in the AI pack."
echo "KoishiPro2 data source: $DATA_ZIP"
