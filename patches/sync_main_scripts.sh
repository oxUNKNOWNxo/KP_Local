#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <KoishiPro2-source-root>" >&2
  exit 2
fi

SOURCE_ROOT="$(cd "$1" && pwd)"
UPSTREAM_REPO="${SOURCE_REPO:-https://code.moenext.com/hex/ygopro2.git}"
WORK="${RUNNER_TEMP:-/tmp}/koishipro2-main-script-sync"
rm -rf "$WORK"
mkdir -p "$WORK"

DATA_LIST="$WORK/current-data-zips.txt"
find "$SOURCE_ROOT/Assets" -type f -name 'ygopro2-data.zip' -print > "$DATA_LIST"
DATA_COUNT=$(awk 'END { print NR + 0 }' "$DATA_LIST")
if [ "$DATA_COUNT" -ne 1 ]; then
  echo "Expected exactly one current ygopro2-data.zip; found $DATA_COUNT" >&2
  cat "$DATA_LIST" >&2
  exit 1
fi
CURRENT_ZIP=$(sed -n '1p' "$DATA_LIST")

if [ ! -d "$SOURCE_ROOT/.git" ]; then
  echo "KoishiPro2 source root must be a git checkout: $SOURCE_ROOT" >&2
  exit 1
fi

git -C "$SOURCE_ROOT" config http.version HTTP/1.1
git -C "$SOURCE_ROOT" fetch --quiet --no-tags --depth=1 origin HEAD
LATEST_COMMIT=$(git -C "$SOURCE_ROOT" rev-parse FETCH_HEAD)
test -n "$LATEST_COMMIT"

LATEST_ROOT="$WORK/latest-source"
git -C "$SOURCE_ROOT" worktree add --quiet --detach "$LATEST_ROOT" "$LATEST_COMMIT"
cleanup() {
  git -C "$SOURCE_ROOT" worktree remove --force "$LATEST_ROOT" >/dev/null 2>&1 || true
}
trap cleanup EXIT

LATEST_LIST="$WORK/latest-data-zips.txt"
find "$LATEST_ROOT/Assets" -type f -name 'ygopro2-data.zip' -print > "$LATEST_LIST"
LATEST_COUNT=$(awk 'END { print NR + 0 }' "$LATEST_LIST")
if [ "$LATEST_COUNT" -ne 1 ]; then
  echo "Expected exactly one latest ygopro2-data.zip; found $LATEST_COUNT at $LATEST_COMMIT" >&2
  cat "$LATEST_LIST" >&2
  exit 1
fi
LATEST_ZIP=$(sed -n '1p' "$LATEST_LIST")

OUT_DIR="$SOURCE_ROOT/Assets/StreamingAssets"
OUT_UPDATE="$OUT_DIR/koishi-main-script-update.zip"
OUT_REVISION="$OUT_DIR/koishi-main-scripts-revision.txt"
mkdir -p "$OUT_DIR"

python3 - "$CURRENT_ZIP" "$LATEST_ZIP" "$OUT_UPDATE" "$OUT_REVISION" "$LATEST_COMMIT" <<'PY'
from pathlib import Path
import hashlib
import os
import sys
import tempfile
import zipfile

current_path = Path(sys.argv[1])
latest_path = Path(sys.argv[2])
update_path = Path(sys.argv[3])
revision_path = Path(sys.argv[4])
latest_commit = sys.argv[5].strip()
marker_name = "updates/koishi-main-scripts-revision.txt"


def normalize(name: str) -> str:
    return name.replace("\\", "/").lstrip("./")


def script_entries(path: Path):
    out = {}
    with zipfile.ZipFile(path, "r") as z:
        for info in z.infolist():
            name = normalize(info.filename)
            lower = name.lower()
            if lower.startswith("script/") and lower.endswith(".lua") and not info.is_dir():
                data = z.read(info.filename)
                out[name] = (info, data, hashlib.sha256(data).hexdigest())
    return out


current = script_entries(current_path)
latest = script_entries(latest_path)

required = {"script/constant.lua", "script/utility.lua"}
missing = sorted(required.difference(latest))
if missing:
    raise SystemExit(f"Latest KoishiPro2 data is missing required scripts: {missing}")

latest_card_count = sum(
    1
    for name in latest
    if Path(name).name.startswith("c") and name.lower().endswith(".lua")
)
if latest_card_count < 1000:
    raise SystemExit(f"Latest KoishiPro2 script set is unexpectedly small: {latest_card_count}")

added = sorted(name for name in latest if name not in current)
changed = sorted(
    name
    for name in latest
    if name in current and latest[name][2] != current[name][2]
)
removed = sorted(name for name in current if name not in latest)
delta = sorted(set(added + changed))

marker_data = (latest_commit + "\n").encode("utf-8")

# Fresh installs should get the latest KoishiPro2 script namespace directly
# from the normal ygopro2-data.zip, with no AI-specific copy step.
fd, tmp_name = tempfile.mkstemp(prefix=current_path.name + ".", suffix=".tmp", dir=str(current_path.parent))
os.close(fd)
try:
    with zipfile.ZipFile(current_path, "r") as zin, zipfile.ZipFile(tmp_name, "w") as zout:
        for info in zin.infolist():
            name = normalize(info.filename)
            lower = name.lower()
            if lower.startswith("script/") and lower.endswith(".lua"):
                continue
            if name == marker_name:
                continue
            zout.writestr(info, zin.read(info.filename))

        for name in sorted(latest):
            info, data, _ = latest[name]
            new_info = zipfile.ZipInfo(name, date_time=info.date_time)
            new_info.compress_type = zipfile.ZIP_DEFLATED
            new_info.external_attr = info.external_attr
            new_info.create_system = info.create_system
            zout.writestr(new_info, data)

        zout.writestr(marker_name, marker_data)

    os.replace(tmp_name, current_path)
finally:
    if os.path.exists(tmp_name):
        os.unlink(tmp_name)

# Existing installations keep persistentDataPath. Ship only added/changed
# scripts as a one-time overlay; deliberately do not delete old/custom scripts.
if update_path.exists():
    update_path.unlink()
with zipfile.ZipFile(update_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zout:
    for name in delta:
        _, data, _ = latest[name]
        zout.writestr(name, data)
    zout.writestr(marker_name, marker_data)

revision_path.write_text(latest_commit + "\n", encoding="utf-8")

print(f"KoishiPro2 latest script revision: {latest_commit}")
print(f"Current bundled scripts: {len(current)}")
print(f"Latest bundled scripts: {len(latest)}")
print(f"Added: {len(added)}")
print(f"Changed: {len(changed)}")
print(f"Removed upstream (left untouched on existing installs): {len(removed)}")
print(f"Existing-install overlay entries: {len(delta)}")
print(f"Latest card scripts: {latest_card_count}")
if added:
    print("Sample added:", ", ".join(added[:12]))
if changed:
    print("Sample changed:", ", ".join(changed[:12]))
if removed:
    print("Sample removed:", ", ".join(removed[:12]))
PY

test -s "$CURRENT_ZIP"
test -s "$OUT_UPDATE"
test -s "$OUT_REVISION"

echo "Updated base data: $CURRENT_ZIP"
echo "Existing-install script overlay: $OUT_UPDATE"
echo "Script revision: $(cat "$OUT_REVISION")"
