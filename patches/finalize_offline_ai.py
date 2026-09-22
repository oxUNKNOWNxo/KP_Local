#!/usr/bin/env python3
from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
assets = root / "Assets"
if not assets.is_dir():
    raise SystemExit(f"Assets directory not found: {assets}")

patch_root = Path(__file__).resolve().parent
bootstrap_source = patch_root / "ai" / "AIBootstrap.cs"
bootstrap_target = assets / "SibylSystem" / "AIBootstrap.cs"
native_viewport_source = patch_root / "ios" / "KoishiViewport.mm"
native_viewport_target = assets / "Plugins" / "iOS" / "KoishiViewport.mm"
for required in (bootstrap_source, native_viewport_source):
    if not required.is_file():
        raise SystemExit(f"Required patch source missing: {required}")
shutil.copyfile(bootstrap_source, bootstrap_target)
native_viewport_target.parent.mkdir(parents=True, exist_ok=True)
shutil.copyfile(native_viewport_source, native_viewport_target)

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

# The current upstream has already removed functional puzzle/single-player mode:
# the single_ button only displays a removal message. Reuse that existing,
# already-visible menu slot as the deterministic offline-AI entry rather than
# depending on the historical ai_ object's hidden hierarchy.
menu_path = assets / "SibylSystem" / "Menu" / "Menu.cs"
menu = menu_path.read_text(encoding="utf-8-sig")
single_event = '        UIHelper.registEvent(gameObject, "single_", onClickPizzle);'
if single_event not in menu:
    raise SystemExit("Could not locate single_ menu registration")
menu = menu.replace(single_event, '        UIHelper.registEvent(gameObject, "single_", onClickAI);', 1)

create_anchor = "        createWindow(Program.I().new_ui_menu);\n"
if "ConfigureOfflineAiMenu();" not in menu:
    if create_anchor not in menu:
        raise SystemExit("Could not locate main menu createWindow call")
    menu = menu.replace(create_anchor, create_anchor + "        ConfigureOfflineAiMenu();\n", 1)

helper_anchor = "    private void CreateSuperPreMenuItem()\n"
if "private void ConfigureOfflineAiMenu()" not in menu:
    if helper_anchor not in menu:
        raise SystemExit("Could not locate menu helper insertion point")
    helper = '''    private void ConfigureOfflineAiMenu()
    {
        Transform[] entries = gameObject.GetComponentsInChildren<Transform>(true);
        int aiEntries = 0;
        int singleEntries = 0;

        for (int i = 0; i < entries.Length; i++)
        {
            Transform entry = entries[i];
            if (entry == null)
            {
                continue;
            }

            if (entry.name == "ai_")
            {
                // Keep the historical entry available where its hierarchy is
                // compatible with the current menu.
                for (Transform p = entry; p != null && p != gameObject.transform; p = p.parent)
                {
                    p.gameObject.SetActive(true);
                }
                entry.gameObject.SetActive(true);
                aiEntries++;
            }

            if (entry.name == "single_")
            {
                for (Transform p = entry; p != null && p != gameObject.transform; p = p.parent)
                {
                    p.gameObject.SetActive(true);
                }
                entry.gameObject.SetActive(true);
                UILabel[] labels = entry.gameObject.GetComponentsInChildren<UILabel>(true);
                for (int j = 0; j < labels.Length; j++)
                {
                    labels[j].text = "AI対戦";
                }
                singleEntries++;
            }
        }

        UnityEngine.Debug.Log("[OfflineAI] menu ai_=" + aiEntries + ", single_=" + singleEntries);
    }

'''
    menu = menu.replace(helper_anchor, helper + helper_anchor, 1)
menu_path.write_text(menu, encoding="utf-8")

print("Finalized offline AI integration:")
print(f"  - installed {bootstrap_target}")
print(f"  - installed native iOS viewport plugin {native_viewport_target}")
print("  - made UTF-8 native paths NUL-terminated and byte-safe")
print("  - enabled first-use bundled AI data bootstrap")
print("  - repurposed removed single-player menu slot as visible AI battle entry")
print("  - kept historical ai_ entry as a secondary fallback")
print("  - moved notch containment to the native Unity UIView")
print("  - replaced obsolete non-ASCII filename warning")
