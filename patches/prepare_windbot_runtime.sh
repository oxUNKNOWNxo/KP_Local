#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <KoishiPro2-source-root>" >&2
  exit 2
fi

SOURCE_ROOT="$(cd "$1" && pwd)"
CONTROL_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WINDBOT_ROOT="$CONTROL_ROOT/windbot"
WORK="${RUNNER_TEMP:-/tmp}/koishipro-windbot-package"

rm -rf "$WORK"
mkdir -p "$WORK"

chmod +x "$WINDBOT_ROOT/prepare_windbot_subset.sh"
"$WINDBOT_ROOT/prepare_windbot_subset.sh" "$WORK/subset"

EXCLUDED_DECKS="$WINDBOT_ROOT/excluded_decks.txt"
if [ -f "$EXCLUDED_DECKS" ]; then
  python3 - "$WORK/subset" "$EXCLUDED_DECKS" <<'PY'
from pathlib import Path
import sys

subset = Path(sys.argv[1])
exclude_path = Path(sys.argv[2])
excluded = {
    line.strip()
    for line in exclude_path.read_text(encoding="utf-8-sig").splitlines()
    if line.strip() and not line.lstrip().startswith("#")
}

if not excluded:
    raise SystemExit("WindBot exclusion list is empty")

catalog = subset / "data" / "deck-catalog.tsv"
lines = catalog.read_text(encoding="utf-8").splitlines()
if not lines or lines[0] != "name\tfile\tlevel\texecutor":
    raise SystemExit("Unexpected WindBot deck catalog format")

rows = []
for line in lines[1:]:
    if not line:
        continue
    fields = line.split("\t")
    if len(fields) != 4:
        raise SystemExit("Malformed WindBot deck catalog row: " + line)
    rows.append(fields)

matched = {row[0] for row in rows if row[0] in excluded}
missing = sorted(excluded - matched)
if missing:
    print("WindBot exclusions not present upstream: " + ", ".join(missing))

manager = subset / "src" / "Game" / "AI" / "DecksManager.cs"
manager_text = manager.read_text(encoding="utf-8")
anchor = "            _list = new List<DeckInstance>();"
if anchor not in manager_text:
    raise SystemExit("WindBot DecksManager list anchor not found")

remove_lines = "".join(
    '            _decks.Remove("' + name.replace("\\", "\\\\").replace('"', '\\"') + '");\n'
    for name in sorted(matched, key=str.lower)
)
manager_text = manager_text.replace(
    anchor,
    remove_lines + ("\n" if remove_lines else "") + anchor,
    1,
)
manager.write_text(manager_text, encoding="utf-8")

kept = []
for row in rows:
    name, deck_file, level, executor = row
    if name in excluded:
        ydk = subset / "data" / "Decks" / (deck_file + ".ydk")
        if ydk.exists():
            ydk.unlink()
        print("Excluded WindBot deck: " + name + " -> " + deck_file)
        continue
    kept.append(row)

catalog.write_text(
    lines[0] + "\n" +
    "".join("\t".join(row) + "\n" for row in kept),
    encoding="utf-8",
)

remaining_names = {row[0] for row in kept}
leaked = sorted(excluded & remaining_names)
if leaked:
    raise SystemExit("Excluded WindBot decks remained in catalog: " + ", ".join(leaked))

print("Applied persistent WindBot exclusions: " + str(len(matched)) + " decks")
PY
fi

MANAGED="$SOURCE_ROOT/Assets/SibylSystem/WindBotRuntime"
DATA="$SOURCE_ROOT/Assets/StreamingAssets/WindBotData"
rm -rf "$MANAGED" "$DATA"
mkdir -p "$MANAGED" "$DATA"

cp -R "$WORK/subset/src/." "$MANAGED/"
cp -R "$WORK/subset/data/." "$DATA/"
mkdir -p "$DATA/licenses"
cp "$WORK/subset/WindBot-LICENSE.txt" "$DATA/licenses/WindBot-LICENSE.txt"
cp "$WORK/subset/windbot-revision.txt" "$DATA/windbot-revision.txt"

test -s "$MANAGED/Game/AI/Decks/RadiantTyphoonExecutor.cs"
test -s "$MANAGED/Game/AI/Decks/DarkMagicianExecutor.cs"
test -s "$MANAGED/LocalRuntime/LocalDuelNative.cs"
test -s "$MANAGED/LocalRuntime/LocalDuelRouter.cs"
test -s "$DATA/Decks/AI_RadiantTyphoon.ydk"
test -s "$DATA/Decks/AI_DarkMagician.ydk"
test ! -e "$DATA/Decks/AI_BlueEyes.ydk"
test ! -e "$DATA/Decks/AI_Test.ydk"
test -s "$DATA/deck-catalog.tsv"
! grep -q '^BlueEyes[[:space:]]' "$DATA/deck-catalog.tsv"
! grep -q '^Test[[:space:]]' "$DATA/deck-catalog.tsv"
test -s "$DATA/Dialogs/wof-Kasumisawa-Haruma.json"
test "$(find "$MANAGED/Game/AI/Decks" -maxdepth 1 -type f -name '*.cs' | wc -l | tr -d ' ')" -gt 20
test "$(find "$DATA/Decks" -maxdepth 1 -type f -name '*.ydk' | wc -l | tr -d ' ')" -gt 20

echo "Prepared WindBot managed runtime: $MANAGED"
echo "Prepared WindBot data: $DATA"
echo "WindBot revision: $(cat "$DATA/windbot-revision.txt")"
