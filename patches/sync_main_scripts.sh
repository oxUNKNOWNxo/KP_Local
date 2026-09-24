#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <KoishiPro2-source-root>" >&2
  exit 2
fi

SOURCE_ROOT="$(cd "$1" && pwd)"
WORK="${RUNNER_TEMP:-/tmp}/koishipro2-main-script-sync"
YGOPRO_REPO="https://github.com/purerosefallen/ygopro.git"
SCRIPT_REPO="https://github.com/Smile-DK/ygopro-scripts.git"

rm -rf "$WORK"
mkdir -p "$WORK"

DATA_LIST="$WORK/current-data-zips.txt"
find "$SOURCE_ROOT/Assets" -type f -name 'ygopro2-data.zip' -print > "$DATA_LIST"
DATA_COUNT=$(awk 'END { print NR + 0 }' "$DATA_LIST")
if [ "$DATA_COUNT" -ne 1 ]; then
  echo "Expected exactly one ygopro2-data.zip; found $DATA_COUNT" >&2
  cat "$DATA_LIST" >&2
  exit 1
fi
CURRENT_ZIP=$(sed -n '1p' "$DATA_LIST")

fetch_head() {
  local repo="$1"
  local dir="$2"
  mkdir -p "$dir"
  git -C "$dir" init -q
  git -C "$dir" remote add origin "$repo"
  git -C "$dir" config http.version HTTP/1.1
  git -C "$dir" fetch --quiet --no-tags --depth=1 origin master
  git -C "$dir" checkout --quiet --detach FETCH_HEAD
}

fetch_commit() {
  local repo="$1"
  local sha="$2"
  local dir="$3"
  mkdir -p "$dir"
  git -C "$dir" init -q
  git -C "$dir" remote add origin "$repo"
  git -C "$dir" config http.version HTTP/1.1
  git -C "$dir" fetch --quiet --no-tags --depth=1 origin "$sha"
  git -C "$dir" checkout --quiet --detach FETCH_HEAD
  test "$(git -C "$dir" rev-parse HEAD)" = "$sha"
}

echo "Resolving current Koishi script revision..."
fetch_head "$YGOPRO_REPO" "$WORK/ygopro"
YGOPRO_SHA="$(git -C "$WORK/ygopro" rev-parse HEAD)"
SCRIPT_SHA="$(git -C "$WORK/ygopro" ls-tree "$YGOPRO_SHA" script | awk '{print $3}')"
if [ -z "$SCRIPT_SHA" ]; then
  echo "Could not resolve script submodule from purerosefallen/ygopro@$YGOPRO_SHA" >&2
  exit 1
fi
fetch_commit "$SCRIPT_REPO" "$SCRIPT_SHA" "$WORK/script"

for required in constant.lua utility.lua procedure.lua patches/entry.lua; do
  if [ ! -s "$WORK/script/$required" ]; then
    echo "Koishi script snapshot is missing required file: $required" >&2
    exit 1
  fi
done

OUT_DIR="$SOURCE_ROOT/Assets/StreamingAssets"
OUT_UPDATE="$OUT_DIR/koishi-main-script-update.zip"
OUT_REVISION="$OUT_DIR/koishi-main-scripts-revision.txt"
mkdir -p "$OUT_DIR"

python3 - "$CURRENT_ZIP" "$WORK/script" "$OUT_UPDATE" "$OUT_REVISION" "$YGOPRO_SHA" "$SCRIPT_SHA" <<'PY'
from pathlib import Path
import hashlib
import os
import sys
import tempfile
import zipfile

current_path = Path(sys.argv[1])
script_root = Path(sys.argv[2])
update_path = Path(sys.argv[3])
revision_path = Path(sys.argv[4])
ygopro_sha = sys.argv[5].strip()
script_sha = sys.argv[6].strip()
marker_name = "updates/koishi-main-scripts-revision.txt"
revision = f"ygopro={ygopro_sha}\nscript={script_sha}\n"
marker_data = revision.encode("utf-8")


def normalize(name: str) -> str:
    return name.replace("\\", "/").lstrip("./")


def bundled_scripts(path: Path):
    out = {}
    with zipfile.ZipFile(path, "r") as z:
        for info in z.infolist():
            name = normalize(info.filename)
            lower = name.lower()
            if lower.startswith("script/") and lower.endswith(".lua") and not info.is_dir():
                data = z.read(info.filename)
                out[name] = hashlib.sha256(data).hexdigest()
    return out


current = bundled_scripts(current_path)
latest = {}
for path in sorted(script_root.rglob("*.lua")):
    if ".git" in path.parts:
        continue
    rel = path.relative_to(script_root).as_posix()
    name = "script/" + rel
    data = path.read_bytes()
    latest[name] = (data, hashlib.sha256(data).hexdigest())

required = {
    "script/constant.lua",
    "script/utility.lua",
    "script/procedure.lua",
    "script/patches/entry.lua",
}
missing = sorted(required.difference(latest))
if missing:
    raise SystemExit(f"Koishi script snapshot is missing required scripts: {missing}")

card_count = sum(
    1 for name in latest
    if Path(name).name.startswith("c") and name.lower().endswith(".lua")
)
if card_count < 10000:
    raise SystemExit(f"Koishi script snapshot is unexpectedly small: {card_count} card scripts")

added = sorted(name for name in latest if name not in current)
changed = sorted(
    name for name in latest
    if name in current and latest[name][1] != current[name]
)
removed = sorted(name for name in current if name not in latest)
delta = sorted(set(added + changed))

fd, tmp_name = tempfile.mkstemp(
    prefix=current_path.name + ".",
    suffix=".tmp",
    dir=str(current_path.parent),
)
os.close(fd)
try:
    with zipfile.ZipFile(current_path, "r") as zin, zipfile.ZipFile(
        tmp_name, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9
    ) as zout:
        for info in zin.infolist():
            name = normalize(info.filename)
            lower = name.lower()
            if lower.startswith("script/") and lower.endswith(".lua"):
                continue
            if name == marker_name:
                continue
            zout.writestr(info, zin.read(info.filename))

        for name in sorted(latest):
            data, _ = latest[name]
            zout.writestr(name, data)
        zout.writestr(marker_name, marker_data)

    os.replace(tmp_name, current_path)
finally:
    if os.path.exists(tmp_name):
        os.unlink(tmp_name)

if update_path.exists():
    update_path.unlink()
with zipfile.ZipFile(
    update_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9
) as zout:
    for name in delta:
        data, _ = latest[name]
        zout.writestr(name, data)
    zout.writestr(marker_name, marker_data)

revision_path.write_text(revision, encoding="utf-8")

print(f"Koishi desktop revision: {ygopro_sha}")
print(f"Koishi script revision: {script_sha}")
print(f"Previously bundled scripts: {len(current)}")
print(f"Current Koishi scripts: {len(latest)}")
print(f"Card scripts: {card_count}")
print(f"Added: {len(added)}")
print(f"Changed: {len(changed)}")
print(f"Removed upstream (not deleted from persistent installs): {len(removed)}")
print(f"Existing-install overlay entries: {len(delta)}")
PY

test -s "$CURRENT_ZIP"
test -s "$OUT_UPDATE"
test -s "$OUT_REVISION"

echo "Updated base data: $CURRENT_ZIP"
echo "Existing-install script overlay: $OUT_UPDATE"
echo "Script revision:"
cat "$OUT_REVISION"
