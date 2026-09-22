#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <KoishiPro2-source-root>" >&2
  exit 2
fi

TARGET_ROOT="$(cd "$1" && pwd)"
CORE_REPO='https://github.com/Heavenswind/YGOProUnity_V2.git'
CORE_COMMIT='c00c90cff77f5e3c65d217597481a065404ed94b'
WORK="${RUNNER_TEMP:-/tmp}/koishipro2-ai-core"
rm -rf "$WORK"
mkdir -p "$WORK/src" "$WORK/obj"

git -C "$WORK/src" init -q
git -C "$WORK/src" remote add origin "$CORE_REPO"
git -C "$WORK/src" fetch --quiet --no-tags --depth=1 origin "$CORE_COMMIT"
git -C "$WORK/src" checkout --quiet --detach FETCH_HEAD
test "$(git -C "$WORK/src" rev-parse HEAD)" = "$CORE_COMMIT"

CORE="$WORK/src/AI_core_vs2017solution/ocgcore"
test -d "$CORE"

python3 - "$CORE" <<'PY'
from pathlib import Path
import sys
core = Path(sys.argv[1])
h = core / 'ocgapi.h'
s = h.read_text(encoding='utf-8-sig')
old = '#define WIN32\n#include "common.h"\n#ifdef WIN32\n#include <windows.h>\n#define DECL_DLLEXPORT __declspec(dllexport)\n#else\n#define DECL_DLLEXPORT\n#endif'
new = '#include "common.h"\n#if defined(_WIN32) || defined(WIN32)\n#include <windows.h>\n#define DECL_DLLEXPORT __declspec(dllexport)\n#else\n#define DECL_DLLEXPORT __attribute__((visibility("default")))\n#endif'
if old not in s:
    raise SystemExit('Expected Windows-only export block not found')
h.write_text(s.replace(old, new, 1), encoding='utf-8')

los = core / 'loslib.c'
s = los.read_text(encoding='utf-8-sig')
marker = 'static int os_execute (lua_State *L) {'
start = s.index(marker)
end = s.index('\n}\n', start) + 3
replacement = 'static int os_execute (lua_State *L) {\n#if defined(__APPLE__) && defined(__ENVIRONMENT_IPHONE_OS_VERSION_MIN_REQUIRED__)\n  (void)L;\n  lua_pushnil(L);\n  lua_pushliteral(L, "os.execute is unavailable on iOS");\n  return 2;\n#else\n  const char *cmd = luaL_optstring(L, 1, NULL);\n  int stat = system(cmd);\n  if (cmd != NULL)\n    return luaL_execresult(L, stat);\n  else {\n    lua_pushboolean(L, stat);\n    return 1;\n  }\n#endif\n}\n'
los.write_text(s[:start] + replacement + s[end:], encoding='utf-8')
PY

SDKROOT="$(xcrun --sdk iphoneos --show-sdk-path)"
CLANG="$(xcrun --sdk iphoneos --find clang)"
CLANGXX="$(xcrun --sdk iphoneos --find clang++)"
AR="$(xcrun --sdk iphoneos --find ar)"
COMMON=( -arch arm64 -isysroot "$SDKROOT" -miphoneos-version-min=13.0 -O2 -fPIC -fvisibility=hidden -I"$CORE" )

find "$CORE" -maxdepth 1 -type f \( -name '*.cpp' -o -name '*.c' \) -print | sort > "$WORK/sources.txt"
while IFS= read -r src; do
  base="$(basename "$src")"
  obj="$WORK/obj/${base%.*}.o"
  case "$src" in
    *.cpp) "$CLANGXX" "${COMMON[@]}" -std=gnu++11 -Wno-deprecated-declarations -c "$src" -o "$obj" ;;
    *.c) "$CLANG" "${COMMON[@]}" -std=gnu99 -Wno-deprecated-declarations -c "$src" -o "$obj" ;;
  esac
done < "$WORK/sources.txt"

OUT_DIR="$TARGET_ROOT/Assets/Plugins/iOS"
mkdir -p "$OUT_DIR"
"$AR" rcs "$OUT_DIR/libocgcore.a" "$WORK"/obj/*.o

SYMBOLS="$WORK/exported-symbols.txt"
xcrun nm -gU "$OUT_DIR/libocgcore.a" | grep -E '(_create_duel|_process|_set_response|_query_|_set_chat_handler|_preload_script|_set_ai_id|_get_ai_going_first_second)' | tee "$SYMBOLS"
for symbol in _create_duel _set_chat_handler _preload_script _set_ai_id _get_ai_going_first_second; do
  grep -q "$symbol" "$SYMBOLS"
done

cp "$CORE/LICENSE" "$OUT_DIR/ocgcore-LICENSE.txt"
file "$OUT_DIR/libocgcore.a"
ls -lh "$OUT_DIR/libocgcore.a"
echo "AI core source: $CORE_REPO@$CORE_COMMIT"
