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
test -s "$MANAGED/LocalRuntime/LocalDuelNative.cs"
test -s "$MANAGED/LocalRuntime/LocalDuelRouter.cs"
test -s "$DATA/Decks/AI_RadiantTyphoon.ydk"
test -s "$DATA/Dialogs/wof-Kasumisawa-Haruma.json"

echo "Prepared WindBot managed runtime: $MANAGED"
echo "Prepared WindBot data: $DATA"
echo "WindBot revision: $(cat "$DATA/windbot-revision.txt")"
