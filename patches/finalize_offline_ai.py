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
main_script_bootstrap_source = patch_root / "MainScriptBootstrap.cs"
main_script_bootstrap_target = assets / "SibylSystem" / "MainScriptBootstrap.cs"
viewport_source = patch_root / "ios" / "IPhone16x9Viewport.cs"
viewport_target = assets / "SibylSystem" / "IPhone16x9Viewport.cs"
for required in (bootstrap_source, main_script_bootstrap_source, viewport_source):
    if not required.is_file():
        raise SystemExit(f"Required patch source missing: {required}")
shutil.copyfile(bootstrap_source, bootstrap_target)
shutil.copyfile(main_script_bootstrap_source, main_script_bootstrap_target)
shutil.copyfile(viewport_source, viewport_target)

# Keep an explicit startup marker for the iOS viewport integration. The actual
# containment is performed below Unity, in the generated native iOS view.
program_path = assets / "SibylSystem" / "Program.cs"
program = program_path.read_text(encoding="utf-8-sig")
if "IPhone16x9Viewport.EnsureInstalled();" not in program:
    startup_pattern = re.compile(
        r"(?m)^(?P<indent>[ \t]*)InitializeBasicDataSyncState\(\);[ \t]*\n"
        r"(?P=indent)UseBundledBasicData\(\);[ \t]*$"
    )
    match = startup_pattern.search(program)
    if not match:
        raise SystemExit("Could not locate patched Program startup sequence for viewport installation")
    indent = match.group("indent")
    replacement = match.group(0) + "\n" + indent + "IPhone16x9Viewport.EnsureInstalled();"
    program = program[: match.start()] + replacement + program[match.end() :]
# Prepare shared card scripts and AI-specific data before the main menu
# becomes interactive. This keeps AI-room entry free of installation/copy work.
startup_anchor = "        backGroundPic.show();\n        shiftToServant(menu);"
startup_replacement = (
    "        MainScriptBootstrap.EnsureInstalled();\n"
    "        AIBootstrap.EnsureInstalled();\n"
    "        backGroundPic.show();\n"
    "        shiftToServant(menu);"
)
if startup_anchor not in program:
    raise SystemExit("Could not locate Program.gameStart menu startup anchor")
program = program.replace(startup_anchor, startup_replacement, 1)

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

# Reuse the historical AI entry already shipped in the menu prefab. Earlier
# device testing proved that this exact object can render on iOS. Enable its
# hidden hierarchy and move the complete outer AI group to a deterministic
# second column so it cannot overlap the existing My Card entry.
menu_path = assets / "SibylSystem" / "Menu" / "Menu.cs"
menu = menu_path.read_text(encoding="utf-8-sig")

ai_register_pattern = re.compile(
    r'(?m)^(?P<indent>[ \t]*)UIHelper\.registEvent\(gameObject,\s*"ai_",\s*onClickAI\);[ \t]*$'
)
ai_register_match = ai_register_pattern.search(menu)
if ai_register_match is None:
    raise SystemExit("Could not locate restored ai_ menu registration call")
if "EnableAiMenuEntry();" not in menu:
    indent = ai_register_match.group("indent")
    insertion = indent + "EnableAiMenuEntry();\n" + ai_register_match.group(0)
    menu = menu[: ai_register_match.start()] + insertion + menu[ai_register_match.end() :]

helper_anchor_pattern = re.compile(
    r"(?m)^(?P<indent>[ \t]*)private void CreateSuperPreMenuItem\(\)[ \t]*$"
)
helper_anchor_match = helper_anchor_pattern.search(menu)
if "private void EnableAiMenuEntry()" not in menu:
    if helper_anchor_match is None:
        raise SystemExit("Could not locate menu helper insertion point")
    helper = r'''    private void EnableAiMenuEntry()
    {
        Transform aiEntry = null;
        Transform[] entries = gameObject.GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < entries.Length; i++)
        {
            Transform entry = entries[i];
            if (entry != null && entry.name == "ai_")
            {
                aiEntry = entry;
                break;
            }
        }

        if (aiEntry == null)
        {
            UnityEngine.Debug.LogWarning("[OfflineAI] Existing ai_ menu entry was not found.");
            return;
        }

        Transform movable = aiEntry;
        if (aiEntry.parent != null && aiEntry.parent.name == "ai")
        {
            movable = aiEntry.parent;
        }

        ActivateAiMenuHierarchy(aiEntry);
        movable.gameObject.SetActive(true);

        // Reflow only the actual menu-button column. Derive the usable bounds
        // from the already-visible menu items, NOT from the hidden AI group's
        // original position. The original AI object can live above the visible
        // menu in the prefab; including that Y value made the new first row
        // remain off-screen on device.
        Transform menuColumn = movable.parent;
        Vector3 p = movable.localPosition;

        if (menuColumn != null)
        {
            System.Collections.Generic.List<Transform> columnItems =
                new System.Collections.Generic.List<Transform>();

            float topY = 0f;
            float bottomY = 0f;
            bool foundExistingBounds = false;

            for (int i = 0; i < menuColumn.childCount; i++)
            {
                Transform sibling = menuColumn.GetChild(i);
                if (sibling == null || sibling == movable || !sibling.gameObject.activeSelf)
                {
                    continue;
                }

                Vector3 siblingPosition = sibling.localPosition;
                if (Mathf.Abs(siblingPosition.x - p.x) > 12f)
                {
                    continue;
                }

                columnItems.Add(sibling);
                if (!foundExistingBounds)
                {
                    topY = siblingPosition.y;
                    bottomY = siblingPosition.y;
                    foundExistingBounds = true;
                }
                else
                {
                    topY = Mathf.Max(topY, siblingPosition.y);
                    bottomY = Mathf.Min(bottomY, siblingPosition.y);
                }
            }

            if (foundExistingBounds)
            {
                // AI becomes the new first row while all rows remain inside the
                // exact top/bottom range that was already visible before AI was
                // enabled. Existing items keep their relative vertical order.
                columnItems.Sort((a, b) =>
                    b.localPosition.y.CompareTo(a.localPosition.y));
                columnItems.Insert(0, movable);

                if (columnItems.Count > 1 && topY > bottomY)
                {
                    float rowSpacing = (topY - bottomY) / (columnItems.Count - 1);
                    for (int i = 0; i < columnItems.Count; i++)
                    {
                        Transform item = columnItems[i];
                        Vector3 itemPosition = item.localPosition;
                        item.localPosition = new Vector3(
                            itemPosition.x,
                            topY - (rowSpacing * i),
                            itemPosition.z
                        );
                    }
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning(
                    "[OfflineAI] Could not derive visible menu bounds; AI position left unchanged.");
            }
        }

        UnityEngine.Debug.Log("[OfflineAI] Existing AI menu entry enabled."
            + " groupPosition=" + movable.localPosition
            + " buttonPosition=" + aiEntry.localPosition
            + " activeInHierarchy=" + aiEntry.gameObject.activeInHierarchy);
    }

    private void ActivateAiMenuHierarchy(Transform entry)
    {
        Transform cursor = entry;
        int depth = 0;

        while (cursor != null && depth < 12)
        {
            if (!cursor.gameObject.activeSelf)
            {
                cursor.gameObject.SetActive(true);
            }

            if (cursor == gameObject.transform)
            {
                break;
            }

            cursor = cursor.parent;
            depth++;
        }

        entry.gameObject.SetActive(true);
    }

'''
    # The Menu class uses four-space indentation at this insertion point.
    # Keep the helper text deterministic and insert it immediately before the
    # existing CreateSuperPreMenuItem method.
    insertion_at = helper_anchor_match.start()
    menu = menu[:insertion_at] + helper + menu[insertion_at:]

menu_path.write_text(menu, encoding="utf-8")

print("Finalized offline AI integration:")
print(f"  - installed {bootstrap_target}")
print(f"  - installed {main_script_bootstrap_target}")
print(f"  - installed {viewport_target}")
print("  - retained explicit native 16:9 startup marker")
print("  - made UTF-8 native paths NUL-terminated and byte-safe")
print("  - AI now uses the main KoishiPro2 script/ directory")
print("  - main script synchronization and AI data install run before menu display")
print("  - re-enabled the existing AI menu hierarchy")
print("  - reflowed AI from visible sibling bounds without using its hidden prefab Y")
print("  - replaced obsolete non-ASCII filename warning")
