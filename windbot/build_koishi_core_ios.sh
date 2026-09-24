#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <output-directory>" >&2
  exit 2
fi

OUT_DIR="$(mkdir -p "$1" && cd "$1" && pwd)"
WORK="${RUNNER_TEMP:-/tmp}/koishipro-windbot-runtime"
YGOPRO_REPO="https://github.com/purerosefallen/ygopro.git"
CORE_REPO="https://github.com/purerosefallen/ygopro-core.git"
SCRIPT_REPO="https://github.com/Smile-DK/ygopro-scripts.git"
WINDBOT_REPO="https://github.com/purerosefallen/windbot.git"
LUA_URL="https://www.lua.org/ftp/lua-5.4.8.tar.gz"
LUA_SHA256="4f18ddae154e793e46eeab727c59ef1c0c0c2b744e7b94219710d76f530629ae"

rm -rf "$WORK"
mkdir -p "$WORK"

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

echo "Resolving current KoishiPro runtime revisions..."
fetch_head "$YGOPRO_REPO" "$WORK/ygopro"
YGOPRO_SHA="$(git -C "$WORK/ygopro" rev-parse HEAD)"
CORE_SHA="$(git -C "$WORK/ygopro" ls-tree "$YGOPRO_SHA" ocgcore | awk '{print $3}')"
SCRIPT_SHA="$(git -C "$WORK/ygopro" ls-tree "$YGOPRO_SHA" script | awk '{print $3}')"
test -n "$CORE_SHA"
test -n "$SCRIPT_SHA"

fetch_commit "$CORE_REPO" "$CORE_SHA" "$WORK/core"
fetch_commit "$SCRIPT_REPO" "$SCRIPT_SHA" "$WORK/script"
fetch_head "$WINDBOT_REPO" "$WORK/windbot"
WINDBOT_SHA="$(git -C "$WORK/windbot" rev-parse HEAD)"

cat > "$OUT_DIR/koishi-runtime-revisions.txt" <<EOF
ygopro=$YGOPRO_SHA
ocgcore=$CORE_SHA
script=$SCRIPT_SHA
windbot=$WINDBOT_SHA
lua=5.4.8
EOF

echo "KoishiPro runtime:"
cat "$OUT_DIR/koishi-runtime-revisions.txt"

for required in constant.lua utility.lua procedure.lua; do
  test -s "$WORK/script/$required"
done
test -s "$WORK/core/ocgapi.h"
test -s "$WORK/core/interpreter.cpp"

echo "Downloading Lua 5.4.8..."
curl --fail --location --retry 4 --retry-delay 2 "$LUA_URL" -o "$WORK/lua.tar.gz"
printf '%s  %s\n' "$LUA_SHA256" "$WORK/lua.tar.gz" | shasum -a 256 -c -
tar -xzf "$WORK/lua.tar.gz" -C "$WORK"
LUA="$WORK/lua-5.4.8/src"
CORE="$WORK/core"

SDKROOT="$(xcrun --sdk iphoneos --show-sdk-path)"
CLANG="$(xcrun --sdk iphoneos --find clang)"
CLANGXX="$(xcrun --sdk iphoneos --find clang++)"
AR="$(xcrun --sdk iphoneos --find ar)"

OBJ="$WORK/obj"
mkdir -p "$OBJ/core" "$OBJ/lua"

COMMON=(
  -arch arm64
  -isysroot "$SDKROOT"
  -miphoneos-version-min=13.0
  -O2
  -fPIC
  -fvisibility=hidden
  -I"$CORE"
  -I"$LUA"
)

echo "Compiling KoishiPro ocgcore for iOS arm64..."
while IFS= read -r src; do
  base="$(basename "$src" .cpp)"
  "$CLANGXX" "${COMMON[@]}" -std=c++14 -DOCGCORE_EXPORT_FUNCTIONS -Wno-deprecated-declarations -c "$src" -o "$OBJ/core/$base.o"
done < <(find "$CORE" -maxdepth 1 -type f -name '*.cpp' | sort)

echo "Compiling Lua 5.4.8..."
while IFS= read -r src; do
  base="$(basename "$src" .c)"
  case "$base" in
    lua|luac|onelua|linit) continue ;;
  esac
  "$CLANG" "${COMMON[@]}" -std=gnu99 -DLUA_USE_POSIX -Wno-deprecated-declarations -c "$src" -o "$OBJ/lua/$base.o"
done < <(find "$LUA" -maxdepth 1 -type f -name '*.c' | sort)

LIB="$OUT_DIR/libkoishi_ocgcore.a"
"$AR" rcs "$LIB" "$OBJ"/core/*.o "$OBJ"/lua/*.o

echo "Validating exported API..."
SYMBOLS="$OUT_DIR/koishi-ocgcore-symbols.txt"
xcrun nm -gU "$LIB" | tee "$SYMBOLS"
for symbol in _create_duel_v2 _start_duel _end_duel _process _get_message _set_responseb _set_script_reader _set_card_reader _preload_script _query_field_card _set_registry_value; do
  grep -q "$symbol" "$SYMBOLS"
done

cp "$CORE/LICENSE" "$OUT_DIR/ocgcore-LICENSE.txt"
cp "$WORK/script/LICENSE" "$OUT_DIR/scripts-LICENSE.txt" 2>/dev/null || true
cp "$WORK/windbot/LICENSE" "$OUT_DIR/windbot-LICENSE.txt"

echo "Built: $LIB"
file "$LIB"
ls -lh "$LIB"
