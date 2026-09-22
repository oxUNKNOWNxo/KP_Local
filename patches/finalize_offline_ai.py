#!/usr/bin/env python3
from pathlib import Path
import re
import shutil
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
assets = root / "Assets"
if not assets.is_dir():
    raise SystemExit(f"Assets directory not found: {assets}")

patch_root = Path(__file__).resolve().parent
bootstrap_source = patch_root / "ai" / "AIBootstrap.cs"
bootstrap_target = assets / "SibylSystem" / "AIBootstrap.cs"
viewport_source = patch_root / "ios" / "IPhone16x9Viewport.cs"
viewport_target = assets / "SibylSystem" / "IPhone16x9Viewport.cs"
for required in (bootstrap_source, viewport_source):
    if not required.is_file():
        raise SystemExit(f"Required patch source missing: {required}")
shutil.copyfile(bootstrap_source, bootstrap_target)
shutil.copyfile(viewport_source, viewport_target)

# Do not rely only on RuntimeInitializeOnLoadMethod on IL2CPP/iOS. Program.cs is
# already known to execute this startup path because it selects the bundled DB.
# Call the viewport installer explicitly from the same path.
program_path = assets / "SibylSystem" / "Program.cs"
program = program_path.read_text(encoding="utf-8-sig")
viewport_call = "IPhone16x9Viewport.EnsureInstalled();"
if viewport_call not in program:
    matches = list(re.finditer(
        r"(?m)^(?P<indent>[ \t]*)UseBundledBasicData\(\);[ \t]*$",
        program,
    ))
    if len(matches) != 1:
        raise SystemExit(f"Expected one startup UseBundledBasicData call, found {len(matches)}")
    m = matches[0]
    line = m.group(0)
    program = program[:m.start()] + line + "\n" + m.group("indent") + viewport_call + program[m.end():]
program_path.write_text(program, encoding="utf-8")

core_path = assets / "SibylSystem" / "coreWrapper.cs"
core = core_path.read_text(encoding="utf-8-sig")
old_ptr = '''        private IntPtr getPtrString(string path)
        {
            IntPtr ptrFileName = Marshal.AllocHGlobal(path.Length + 1);
            byte[] s = System.Text.Encoding.UTF8.GetBytes(path);
            Marshal.Copy(s, 0, ptrFileName, s.Length);
            return ptrFileName;
        }'''
new_ptr = '''        private IntPtr getPtrString(string path)
        {
            byte[] s = System.Text.Encoding.UTF8.GetBytes(path);
            IntPtr ptrFileName = Marshal.AllocHGlobal(s.Length + 1);
            Marshal.Copy(s, 0, ptrFileName, s.Length);
            Marshal.WriteByte(ptrFileName, s.Length, 0);
            return ptrFileName;
        }'''
if old_ptr not in core:
    raise SystemExit("Could not locate legacy UTF-8 pointer helper")
core = core.replace(old_ptr, new_ptr, 1)
core_path.write_text(core, encoding="utf-8")

airoom_path = assets / "SibylSystem" / "Room" / "AIRoom.cs"
airoom = airoom_path.read_text(encoding="utf-8-sig")
marker = '''    void printFile()
    {
        Directory.CreateDirectory("deck");
        Directory.CreateDirectory("ai");
        Directory.CreateDirectory("ai/ydk");'''
replacement = '''    void printFile()
    {
        Directory.CreateDirectory("deck");
        Directory.CreateDirectory("ai");
        Directory.CreateDirectory("ai/ydk");
        AIBootstrap.EnsureInstalled();'''
if marker not in airoom:
    raise SystemExit("Could not locate AIRoom data bootstrap insertion point")
airoom = airoom.replace(marker, replacement, 1)
airoom_path.write_text(airoom, encoding="utf-8")

precy_path = assets / "SibylSystem" / "precy.cs"
precy = precy_path.read_text(encoding="utf-8-sig")
precy = precy.replace(
    'Program.I().cardDescription.RMSshow_none(InterString.Get("游戏内部出错，请重试，文件名中不能包含中文。"));',
    'Program.I().cardDescription.RMSshow_none("AIスクリプトまたはデッキの読み込みに失敗しました。");'
)
precy_path.write_text(precy, encoding="utf-8")

# The historical ai_ entry may be inactive. Search inactive descendants rather
# than relying on UIHelper's active-only lookup, then enable the entry before
# the restored event registration runs.
menu_path = assets / "SibylSystem" / "Menu" / "Menu.cs"
menu = menu_path.read_text(encoding="utf-8-sig")
create_anchor = "        createWindow(Program.I().new_ui_menu);\n"
if "EnableAiMenuEntry();" not in menu:
    if create_anchor not in menu:
        raise SystemExit("Could not locate main menu createWindow call")
    menu = menu.replace(create_anchor, create_anchor + "        EnableAiMenuEntry();\n", 1)

helper_anchor = "    private void CreateSuperPreMenuItem()\n"
if "private void EnableAiMenuEntry()" not in menu:
    if helper_anchor not in menu:
        raise SystemExit("Could not locate menu helper insertion point")
    helper = '''    private void EnableAiMenuEntry()
    {
        Transform[] entries = gameObject.GetComponentsInChildren<Transform>(true);
        int enabled = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            Transform entry = entries[i];
            if (entry != null && entry.name == "ai_")
            {
                entry.gameObject.SetActive(true);
                enabled++;
            }
        }

        if (enabled == 0)
        {
            UnityEngine.Debug.LogWarning("[OfflineAI] ai_ menu entry was not found in the instantiated menu prefab.");
        }
        else
        {
            UnityEngine.Debug.Log("[OfflineAI] Enabled ai_ menu entry count: " + enabled);
        }
    }

'''
    menu = menu.replace(helper_anchor, helper + helper_anchor, 1)
menu_path.write_text(menu, encoding="utf-8")

print("Finalized offline AI integration:")
print(f"  - installed {bootstrap_target}")
print(f"  - installed {viewport_target}")
print(f"  - inserted explicit iOS viewport startup call in {program_path}")
print("  - made UTF-8 native paths NUL-terminated and byte-safe")
print("  - enabled first-use bundled AI data bootstrap")
print("  - forced inactive ai_ menu entries visible before event registration")
print("  - installed centred 16:9 runtime viewport for extra-wide iPhones")
print("  - replaced obsolete non-ASCII filename warning")
