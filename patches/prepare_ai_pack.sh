#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <KoishiPro2-source-root>" >&2
  exit 2
fi

SOURCE_ROOT="$(cd "$1" && pwd)"

# Validate the generated AI menu helper before packaging data. The helper is
# emitted by finalize_offline_ai.py with ordinary C# quotes, so no post-hoc
# source rewriting is needed here.
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
    'movable.localPosition = new Vector3(p.x + 180f, p.y, p.z);',
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
CARD_SCRIPT_REPO='https://github.com/Fluorohydride/ygopro-scripts.git'
# Verified as the repository HEAD on 2026-09-23.
CURRENT_CARD_SCRIPT_COMMIT='f9855151403763b707f888a7240495c05c9071c6'
LEGACY_CARD_SCRIPT_COMMIT='afbac0d80b7cca2770c071a6fd3f83e1718fad66'
WORK="${RUNNER_TEMP:-/tmp}/koishipro2-ai-pack"
rm -rf "$WORK"
mkdir -p "$WORK/ai-src" "$WORK/card-current" "$WORK/card-legacy"

git -C "$WORK/ai-src" init -q
git -C "$WORK/ai-src" remote add origin "$AI_REPO"
git -C "$WORK/ai-src" fetch --quiet --no-tags --depth=1 origin "$AI_COMMIT"
git -C "$WORK/ai-src" checkout --quiet --detach FETCH_HEAD
test "$(git -C "$WORK/ai-src" rev-parse HEAD)" = "$AI_COMMIT"
test -f "$WORK/ai-src/AI/ai.lua"
test -f "$WORK/ai-src/AI/decks/Blackwing.lua"
test -f "$WORK/ai-src/AI/decks/Shaddoll.lua"
test -f "$WORK/ai-src/LICENSE.md"

for spec in "current:$CURRENT_CARD_SCRIPT_COMMIT" "legacy:$LEGACY_CARD_SCRIPT_COMMIT"; do
  kind=${spec%%:*}
  commit=${spec#*:}
  dir="$WORK/card-$kind"
  git -C "$dir" init -q
  git -C "$dir" remote add origin "$CARD_SCRIPT_REPO"
  git -C "$dir" fetch --quiet --no-tags --depth=1 origin "$commit"
  git -C "$dir" checkout --quiet --detach FETCH_HEAD
  test "$(git -C "$dir" rev-parse HEAD)" = "$commit"
  test -f "$dir/constant.lua"
  test -f "$dir/utility.lua"
  test -f "$dir/LICENSE"
  count=$(find "$dir" -type f -name 'c*.lua' | wc -l | tr -d ' ')
  if [ "$count" -lt 1000 ]; then
    echo "Unexpectedly small $kind card-script set: $count" >&2
    exit 1
  fi
done

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

python3 - "$DATA_ZIP" "$WORK/ai-src/AI" "$WORK/ai-src/LICENSE.md" "$WORK/card-current" "$WORK/card-legacy" "$OUT_ZIP" <<'PY'
from pathlib import Path
import sys
import zipfile

data_zip = Path(sys.argv[1])
ai_root = Path(sys.argv[2])
ai_license = Path(sys.argv[3])
current_root = Path(sys.argv[4])
legacy_root = Path(sys.argv[5])
out_zip = Path(sys.argv[6])

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

    # Package every Lua file in the current official repository, preserving
    # subdirectories. AIBootstrap copies this snapshot into live script/ first.
    for path in sorted(current_root.rglob('*.lua')):
        z.write(path, f'script_current/{path.relative_to(current_root).as_posix()}')
    for path in sorted(legacy_root.rglob('*.lua')):
        z.write(path, f'script_legacy/{path.relative_to(legacy_root).as_posix()}')

    z.write(ai_license, 'ai/Snarkie-YGOProAIScript-LICENSE.md')
    z.write(current_root / 'LICENSE', 'script_current/Fluorohydride-ygopro-scripts-LICENSE')
    z.write(legacy_root / 'LICENSE', 'script_legacy/Fluorohydride-ygopro-scripts-LICENSE')
    z.writestr(
        'ai/KOISHIPRO2-AI-PACK-V4.txt',
        'Bundled offline AI runtime pack v4\n'
        'AI scripts: Snarkie/YGOProAIScript @ 1f57db863fb9b5a46e5fe17b7285bde82032e6e4\n'
        'Current card scripts: Fluorohydride/ygopro-scripts @ f9855151403763b707f888a7240495c05c9071c6\n'
        'Legacy fallback scripts: Fluorohydride/ygopro-scripts @ afbac0d80b7cca2770c071a6fd3f83e1718fad66\n'
        'Runtime precedence: current official snapshot > legacy missing-file fallback\n'
    )

with zipfile.ZipFile(out_zip, 'r') as z:
    names = set(z.namelist())
    required = {
        'ai/ai.lua',
        'ai/decks/Blackwing.lua',
        'ai/decks/Shaddoll.lua',
        'ai/ydk/Blackwing.ydk',
        'ai/ydk/Shaddoll.ydk',
        'ai/KOISHIPRO2-AI-PACK-V4.txt',
        'script_current/constant.lua',
        'script_current/utility.lua',
        'script_legacy/constant.lua',
        'script_legacy/utility.lua',
    }
    missing = sorted(required.difference(names))
    if missing:
        raise SystemExit(f'AI pack validation failed; missing {missing}')
    current_count = sum(1 for n in names if n.startswith('script_current/') and Path(n).name.startswith('c') and n.endswith('.lua'))
    legacy_count = sum(1 for n in names if n.startswith('script_legacy/') and Path(n).name.startswith('c') and n.endswith('.lua'))
    if current_count < 1000 or legacy_count < 1000:
        raise SystemExit(f'AI pack script counts too small: current={current_count} legacy={legacy_count}')
    print(f'AI pack entries: {len(names)}')
    print(f'Current card scripts: {current_count}')
    print(f'Legacy fallback scripts: {legacy_count}')
PY

ls -lh "$OUT_ZIP"
echo "AI scripts: $AI_REPO@$AI_COMMIT"
echo "Current card scripts: $CARD_SCRIPT_REPO@$CURRENT_CARD_SCRIPT_COMMIT"
echo "Legacy card scripts: $CARD_SCRIPT_REPO@$LEGACY_CARD_SCRIPT_COMMIT"
echo "KoishiPro2 data source: $DATA_ZIP"
