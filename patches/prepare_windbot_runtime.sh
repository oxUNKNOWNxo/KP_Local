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

# The user-facing exclusion list normally follows the YDK filename without
# the AI_ prefix (for example BlueEyes), while WindBot's Deck attribute may
# expose a different display name (for example Blue-Eyes). Accept either the
# registered deck name, the exact YDK basename, or the YDK basename without
# AI_ so upstream display-name punctuation changes do not resurrect a deck.
def row_aliases(row):
    name, deck_file, level, executor = row
    aliases = {name, deck_file}
    if deck_file.startswith("AI_"):
        aliases.add(deck_file[3:])
    return aliases

excluded_rows = []
matched_tokens = set()
for row in rows:
    tokens = excluded & row_aliases(row)
    if not tokens:
        continue
    excluded_rows.append(row)
    matched_tokens.update(tokens)

missing = sorted(excluded - matched_tokens)
if missing:
    print("WindBot exclusions not present upstream: " + ", ".join(missing))

manager = subset / "src" / "Game" / "AI" / "DecksManager.cs"
manager_text = manager.read_text(encoding="utf-8")
anchor = "            _list = new List<DeckInstance>();"
if anchor not in manager_text:
    raise SystemExit("WindBot DecksManager list anchor not found")

excluded_names = sorted({row[0] for row in excluded_rows}, key=str.lower)
remove_lines = "".join(
    '            _decks.Remove("' + name.replace("\\", "\\\\").replace('"', '\\"') + '");\n'
    for name in excluded_names
)
manager_text = manager_text.replace(
    anchor,
    remove_lines + ("\n" if remove_lines else "") + anchor,
    1,
)
manager.write_text(manager_text, encoding="utf-8")

excluded_row_keys = {(row[0], row[1]) for row in excluded_rows}
kept = []
for row in rows:
    name, deck_file, level, executor = row
    if (name, deck_file) in excluded_row_keys:
        ydk = subset / "data" / "Decks" / (deck_file + ".ydk")
        if ydk.exists():
            ydk.unlink()
        matched_for_row = sorted(excluded & row_aliases(row))
        requested = ",".join(matched_for_row)
        print("Excluded WindBot deck: " + requested + " => " + name + " -> " + deck_file)
        continue
    kept.append(row)

catalog.write_text(
    lines[0] + "\n" +
    "".join("\t".join(row) + "\n" for row in kept),
    encoding="utf-8",
)

for row in kept:
    leaked_tokens = excluded & row_aliases(row)
    if leaked_tokens:
        raise SystemExit(
            "Excluded WindBot deck remained in catalog: " +
            row[0] + " (" + ", ".join(sorted(leaked_tokens)) + ")"
        )

print(
    "Applied persistent WindBot exclusions: " +
    str(len(excluded_rows)) + " registered decks / " +
    str(len(matched_tokens)) + " requested entries"
)
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

# Patch only the generated WindBot/Koishi integration. The stock result panel,
# replay save setting, and all non-AI surrender paths remain unchanged.
python3 "$CONTROL_ROOT/patches/prepare_windbot_surrender.py" "$SOURCE_ROOT"

test -s "$MANAGED/Game/AI/Decks/RadiantTyphoonExecutor.cs"
test -s "$MANAGED/Game/AI/Decks/DarkMagicianExecutor.cs"
test -s "$MANAGED/LocalRuntime/LocalDuelNative.cs"
test -s "$MANAGED/LocalRuntime/LocalDuelRouter.cs"
test -s "$DATA/Decks/AI_RadiantTyphoon.ydk"
test -s "$DATA/Decks/AI_DarkMagician.ydk"
test ! -e "$DATA/Decks/AI_BlueEyes.ydk"
test ! -e "$DATA/Decks/AI_Test.ydk"
test -s "$DATA/deck-catalog.tsv"
! grep -q '^Blue-Eyes[[:space:]]' "$DATA/deck-catalog.tsv"
! grep -q '^Test[[:space:]]' "$DATA/deck-catalog.tsv"
test -s "$DATA/Dialogs/wof-Kasumisawa-Haruma.json"
test "$(find "$MANAGED/Game/AI/Decks" -maxdepth 1 -type f -name '*.cs' | wc -l | tr -d ' ')" -gt 20
test "$(find "$DATA/Decks" -maxdepth 1 -type f -name '*.ydk' | wc -l | tr -d ' ')" -gt 20

grep -Fq 'public void SurrenderHuman()' "$MANAGED/LocalRuntime/LocalDuelRouter.cs"
grep -Fq 'byte[] win = new byte[] { MsgWin, (byte)_aiPlayer, 0x04 };' "$MANAGED/LocalRuntime/LocalDuelRouter.cs"
grep -Fq 'public void SurrenderHuman()' "$MANAGED/LocalRuntime/RadiantTyphoonLocalDuel.cs"
grep -Fq 'BeginWindBotSurrenderPrompt' "$SOURCE_ROOT/Assets/SibylSystem/Room/AIRoom.cs"
grep -Fq 'HandleWindBotSurrenderPromptResult' "$SOURCE_ROOT/Assets/SibylSystem/Ocgcore/Ocgcore.cs"
grep -Fq 'UIHelper.registEvent(toolBar, "exit_", onSurrender);' "$SOURCE_ROOT/Assets/SibylSystem/Ocgcore/Ocgcore.cs"
grep -Fq 'surrenderPromptPending' "$SOURCE_ROOT/Assets/SibylSystem/KoishiWindBotBridge.cs"

echo "Prepared WindBot managed runtime: $MANAGED"
echo "Prepared WindBot data: $DATA"
echo "WindBot revision: $(cat "$DATA/windbot-revision.txt")"