#!/usr/bin/env python3
from pathlib import Path
import os
import re
import shutil
import subprocess
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
assets = root / "Assets"
if not assets.is_dir():
    raise SystemExit(f"Assets directory not found: {assets}")

# ---------------------------------------------------------------------------
# Keep the user's local/basic data on normal startup.
# ---------------------------------------------------------------------------
candidates = []
for path in assets.rglob("*.cs"):
    try:
        text = path.read_text(encoding="utf-8-sig")
    except UnicodeDecodeError:
        continue
    if (
        "UpdateClientCoroutine()" in text
        and "UseBundledBasicData()" in text
        and "RetryBasicDataUpdate()" in text
    ):
        candidates.append((path, text))

if len(candidates) != 1:
    raise SystemExit(
        f"Expected exactly one Program source candidate, found {len(candidates)}: "
        f"{[str(p) for p, _ in candidates]}"
    )

program_path, text = candidates[0]
startup_matches = list(
    re.finditer(r"(?m)^(?P<indent>[ \t]*)RetryBasicDataUpdate\(\);[ \t]*$", text)
)
if len(startup_matches) != 1:
    raise SystemExit(
        f"Expected exactly one standalone startup RetryBasicDataUpdate call, found {len(startup_matches)}"
    )

m = startup_matches[0]
indent = m.group("indent")
startup_replacement = (
    f"{indent}InitializeBasicDataSyncState();\n"
    f"{indent}UseBundledBasicData();"
)
text = text[: m.start()] + startup_replacement + text[m.end() :]
if re.search(r"(?m)^[ \t]*RetryBasicDataUpdate\(\);[ \t]*$", text):
    raise SystemExit("Startup RetryBasicDataUpdate call is still present after patch")
# A completed previous online sync must not leave the UI in the default
# Checking/0% state. Without this, UseBundledBasicData() returns early and
# the main menu permanently shows a bogus "checking card data... 0%" line.
already_synced_old = """        if (_hasCompletedBasicDataSync)
        {
            return true;
        }"""
already_synced_new = """        if (_hasCompletedBasicDataSync)
        {
            BasicDataState = BasicDataUpdateState.Ready;
            BasicDataCurrentFile = "";
            BasicDataProgress = 1f;
            _initialBasicDataSyncPending = false;
            return true;
        }"""
if already_synced_old not in text:
    raise SystemExit("Could not locate already-synced UseBundledBasicData branch")
text = text.replace(already_synced_old, already_synced_new, 1)

# iPhone X responsiveness: the upstream texture pump may decode up to five
# textures in one frame while eight downloads feed the queue. A single image
# decode can already exceed a mobile frame budget, so five consecutive decodes
# can look like a multi-second UI freeze. Keep the background system intact,
# but constrain iOS to two network jobs and one main-thread texture creation.
download_limit_old = "    private const int MAX_CONCURRENT_DOWNLOADS = 8;"
download_limit_new = """#if UNITY_IOS || UNITY_IPHONE
    private const int MAX_CONCURRENT_DOWNLOADS = 2;
#else
    private const int MAX_CONCURRENT_DOWNLOADS = 8;
#endif"""
if download_limit_old not in text:
    raise SystemExit("Could not locate texture download concurrency constant")
text = text.replace(download_limit_old, download_limit_new, 1)

texture_method_marker = "    private void ProcessTextureManagerUpdates()"
texture_method_index = text.find(texture_method_marker)
if texture_method_index < 0:
    raise SystemExit("Could not locate ProcessTextureManagerUpdates")
texture_tail = text[texture_method_index:]
texture_tasks_old = "        int maxTasksPerFrame = 5;"
texture_tasks_index = texture_tail.find(texture_tasks_old)
if texture_tasks_index < 0:
    raise SystemExit("Could not locate texture per-frame task limit")
texture_abs = texture_method_index + texture_tasks_index
texture_tasks_new = """#if UNITY_IOS || UNITY_IPHONE
        int maxTasksPerFrame = 1;
#else
        int maxTasksPerFrame = 5;
#endif"""
text = text[:texture_abs] + texture_tasks_new + text[texture_abs + len(texture_tasks_old):]

# Do not invoke Unity's global unused-asset unload merely because the native
# iOS compatibility container reports a size transition. This operation can
# synchronously stall the main thread and is unnecessary for normal rotation/
# safe-area relayout.
resize_unload_old = """            if (screenSizeChanged)
            {
                Resources.UnloadUnusedAssets();
            }
            onRESIZED();"""
resize_unload_new = """#if !UNITY_IOS && !UNITY_IPHONE
            if (screenSizeChanged)
            {
                Resources.UnloadUnusedAssets();
            }
#endif
            onRESIZED();"""
if resize_unload_old not in text:
    raise SystemExit("Could not locate resize-time Resources.UnloadUnusedAssets block")
text = text.replace(resize_unload_old, resize_unload_new, 1)

program_path.write_text(text, encoding="utf-8")
print(f"Patched: {program_path}")
print("  - disabled forced basic-data sync at startup")
print("  - normalized already-synced basic-data state to Ready")
print("  - limited iOS texture decode/download pressure for UI responsiveness")
print("  - disabled resize-time Resources.UnloadUnusedAssets on iOS")
print("  - left explicit/manual Resource Update behavior unchanged")

# ---------------------------------------------------------------------------
# Restore the last upstream in-process AI implementation removed by
# 1cd5d12ad7b888b8774da7d67c8dba9898a108ee (parent bd251b8...).
# ---------------------------------------------------------------------------
patch_root = Path(__file__).resolve().parent
legacy = patch_root / "ai" / "legacy"
required_legacy = [legacy / "coreWrapper.cs", legacy / "precy.cs", legacy / "AIRoom.cs"]
missing = [str(p) for p in required_legacy if not p.is_file()]
if missing:
    raise SystemExit(f"Vendored legacy AI sources are missing: {missing}")

core_target = assets / "SibylSystem" / "coreWrapper.cs"
precy_target = assets / "SibylSystem" / "precy.cs"
airoom_target = assets / "SibylSystem" / "Room" / "AIRoom.cs"
core_target.parent.mkdir(parents=True, exist_ok=True)
airoom_target.parent.mkdir(parents=True, exist_ok=True)
shutil.copyfile(legacy / "coreWrapper.cs", core_target)
shutil.copyfile(legacy / "precy.cs", precy_target)
shutil.copyfile(legacy / "AIRoom.cs", airoom_target)

# Unity iOS statically links native plugins into the main executable. P/Invoke
# must resolve through __Internal on device. The native->managed callbacks also
# need explicit AOT thunks and rooted delegate instances under IL2CPP.
core_text = core_target.read_text(encoding="utf-8-sig")
class_marker = "    unsafe static class dll\n    {"
if class_marker not in core_text:
    raise SystemExit("Could not locate Percy.dll wrapper class")
lib_block = (
    class_marker
    + "\n#if UNITY_IOS && !UNITY_EDITOR\n"
    + '        const string OcgCoreLibrary = "__Internal";\n'
    + "#else\n"
    + '        const string OcgCoreLibrary = "ocgcore";\n'
    + "#endif"
)
core_text = core_text.replace(class_marker, lib_block, 1)

callback_marker = (
    "        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]\n"
    "        delegate UInt32 MessageHandler(IntPtr pDuel, UInt32 messageType);"
)
callback_block = (
    callback_marker
    + "\n        static readonly CardReader NativeCardReader = OnCardReader;\n"
    + "        static readonly MessageHandler NativeMessageHandler = OnMessageHandler;"
)
if callback_marker not in core_text:
    raise SystemExit("Could not locate native AI callback delegate declarations")
core_text = core_text.replace(callback_marker, callback_block, 1)
core_text = core_text.replace("set_card_reader(OnCardReader);", "set_card_reader(NativeCardReader);")
core_text = core_text.replace("set_message_handler(OnMessageHandler);", "set_message_handler(NativeMessageHandler);")
core_text = core_text.replace("set_chat_handler(OnMessageHandler);", "set_chat_handler(NativeMessageHandler);")

card_cb = "        private static UInt32 OnCardReader(UInt32 code, CardData* pData)"
msg_cb = "        private static UInt32 OnMessageHandler(IntPtr pDuel, UInt32 messageType)"
if card_cb not in core_text or msg_cb not in core_text:
    raise SystemExit("Could not locate native AI callback methods")
core_text = core_text.replace(
    card_cb,
    "        [AOT.MonoPInvokeCallback(typeof(CardReader))]\n" + card_cb,
    1,
)
core_text = core_text.replace(
    msg_cb,
    "        [AOT.MonoPInvokeCallback(typeof(MessageHandler))]\n" + msg_cb,
    1,
)

count = core_text.count('[DllImport("ocgcore",')
if count < 20:
    raise SystemExit(f"Unexpected legacy ocgcore import count: {count}")
core_text = core_text.replace('[DllImport("ocgcore",', '[DllImport(OcgCoreLibrary,')
if '[DllImport("ocgcore",' in core_text:
    raise SystemExit("A legacy dynamic ocgcore import remains")
core_target.write_text(core_text, encoding="utf-8")

# Make the restored AI room safe when the optional AI pack has not yet been
# installed. The original desktop implementation assumed these folders always
# existed and would throw while opening the room on a clean iOS install.
ai_text = airoom_target.read_text(encoding="utf-8-sig")
print_marker = "    void printFile()\n    {\n        string deckInUse"
print_replacement = (
    "    void printFile()\n    {\n"
    '        Directory.CreateDirectory("deck");\n'
    '        Directory.CreateDirectory("ai");\n'
    '        Directory.CreateDirectory("ai/ydk");\n'
    "        string deckInUse"
)
if print_marker not in ai_text:
    raise SystemExit("Could not install AI data-directory guard")
ai_text = ai_text.replace(print_marker, print_replacement, 1)

start_marker = "        if (!isShowed)\n        {\n            return;\n        }"
start_replacement = (
    start_marker
    + "\n"
    + "        if (list_aideck == null || list_airank == null || "
      "list_aideck.items == null || list_aideck.items.Count <= 1 || "
      "list_airank.items == null || list_airank.items.Count == 0)\n"
    + "        {\n"
    + '            RMSshow_none("AIデータが見つかりません。ai フォルダにAIスクリプト、ai/ydk にAIデッキを配置してください。");\n'
    + "            return;\n"
    + "        }"
)
if start_marker not in ai_text:
    raise SystemExit("Could not install AI start guard")
ai_text = ai_text.replace(start_marker, start_replacement, 1)
airoom_target.write_text(ai_text, encoding="utf-8")

# Restore the AI menu button without replacing the current Menu implementation.
menu_path = assets / "SibylSystem" / "Menu" / "Menu.cs"
if not menu_path.is_file():
    raise SystemExit(f"Menu source not found: {menu_path}")
menu_text = menu_path.read_text(encoding="utf-8-sig")

# Avoid automatic network/file work every time the main menu is shown on iOS.
# Manual Resource Update and Super-Pre update actions remain unchanged.
auto_superpre_old = """        // 自动检查超先行卡更新
        if (!_isCheckingUpdate && !isPreDownloading)
        {
            Program.I().StartCoroutine(CheckSuperPreUpdateCoroutine());
        }"""
auto_superpre_new = """        // KoishiPro2 iOS: do not perform an automatic network update check
        // merely because the main menu became visible.
#if !UNITY_IOS && !UNITY_IPHONE
        if (!_isCheckingUpdate && !isPreDownloading)
        {
            Program.I().StartCoroutine(CheckSuperPreUpdateCoroutine());
        }
#endif"""
if auto_superpre_old not in menu_text:
    raise SystemExit("Could not locate automatic Super-Pre menu-show update check")
menu_text = menu_text.replace(auto_superpre_old, auto_superpre_new, 1)

if not re.search(r'(?m)^[ \t]*UIHelper\.registEvent\(gameObject,\s*"ai_",\s*onClickAI\);', menu_text):
    anchor_matches = list(re.finditer(
        r'(?m)^(?P<indent>[ \t]*)UIHelper\.registEvent\(gameObject,\s*"single_",\s*onClickPizzle\);[ \t]*$',
        menu_text,
    ))
    if len(anchor_matches) != 1:
        raise SystemExit(f"Could not uniquely locate menu AI registration anchor: {len(anchor_matches)}")
    a = anchor_matches[0]
    line = a.group(0)
    insertion = line + "\n" + a.group("indent") + 'UIHelper.registEvent(gameObject, "ai_", onClickAI);'
    menu_text = menu_text[: a.start()] + insertion + menu_text[a.end() :]


def replace_method_body(source: str, method_name: str, body: str) -> str:
    match = re.search(rf"\bvoid\s+{re.escape(method_name)}\s*\(\s*\)\s*\{{", source)
    if not match:
        raise SystemExit(f"Method not found: {method_name}")
    open_brace = source.find("{", match.start())
    depth = 0
    in_string = False
    escape = False
    for i in range(open_brace, len(source)):
        ch = source[i]
        if in_string:
            if escape:
                escape = False
            elif ch == "\\":
                escape = True
            elif ch == '"':
                in_string = False
            continue
        if ch == '"':
            in_string = True
        elif ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                line_start = source.rfind("\n", 0, match.start()) + 1
                indent_match = re.match(r"[ \t]*", source[line_start:match.start()])
                method_indent = indent_match.group(0) if indent_match else ""
                inner = method_indent + "    " + body
                return source[: open_brace + 1] + "\n" + inner + "\n" + method_indent + source[i:]
    raise SystemExit(f"Unbalanced braces while replacing {method_name}")

menu_text = replace_method_body(
    menu_text,
    "onClickAI",
    "Program.I().shiftToServant(Program.I().aiRoom);",
)
menu_path.write_text(menu_text, encoding="utf-8")

print("Restored offline AI sources:")
print(f"  - {core_target}")
print(f"  - {precy_target}")
print(f"  - {airoom_target}")
print(f"  - enabled ai_ menu handler in {menu_path}")
print("  - disabled automatic Super-Pre checks on iOS menu show")
print("  - configured iOS P/Invoke through __Internal")
print("  - added IL2CPP/AOT-safe native callback thunks")
print("  - added safe handling for a missing AI data pack")

# The production iOS workflow calls this script as its single patch entry point.
# Keep validation workflows independent, but make the real iOS build complete:
# finalize managed AI integration, bundle the offline AI data, and build the
# ARM64 static ocgcore plugin before Unity imports/exports the project.
if os.environ.get("GITHUB_WORKFLOW") == "KoishiPro2 iOS cloud build":
    print("Preparing production offline AI payload for iOS build...")
    subprocess.run([sys.executable, str(patch_root / "finalize_offline_ai.py"), str(root)], check=True)
    subprocess.run(["bash", str(patch_root / "prepare_ai_pack.sh"), str(root)], check=True)
    subprocess.run(["bash", str(patch_root / "build_ai_core_ios.sh"), str(root)], check=True)

    ai_pack = assets / "StreamingAssets" / "koishi-ai-pack.zip"
    ai_core = assets / "Plugins" / "iOS" / "libocgcore.a"
    if not ai_pack.is_file() or ai_pack.stat().st_size == 0:
        raise SystemExit(f"Bundled AI pack was not created: {ai_pack}")
    if not ai_core.is_file() or ai_core.stat().st_size == 0:
        raise SystemExit(f"ARM64 iOS ocgcore library was not created: {ai_core}")
    print(f"  - bundled AI data: {ai_pack}")
    print(f"  - bundled ARM64 ocgcore: {ai_core}")
