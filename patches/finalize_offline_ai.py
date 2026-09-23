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

# Reuse the historical AI entry that is already present in the shipped menu.
# The previous build proved that this object is renderable on-device, while
# cloning single_ into the live layout could disappear entirely. Activate the
# existing hidden AI branch, then move its outer menu group to the nearest free
# slot so it cannot overlap My Card or another visible entry.
menu_path = assets / "SibylSystem" / "Menu" / "Menu.cs"
menu = menu_path.read_text(encoding="utf-8-sig")
create_anchor = "        createWindow(Program.I().new_ui_menu);\\n"
if create_anchor not in menu:
    raise SystemExit("Could not locate Menu.initialize main-menu createWindow call")
if "EnableAiMenuEntry();" not in menu:
    menu = menu.replace(create_anchor, create_anchor + "        EnableAiMenuEntry();\\n", 1)

helper_anchor = "    private void CreateSuperPreMenuItem()\\n"
if "private void EnableAiMenuEntry()" not in menu:
    if helper_anchor not in menu:
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
        if (aiEntry.parent != null && aiEntry.parent.parent == gameObject.transform)
        {
            // trans_menu.prefab stores ai_ inside an outer "ai" group. Move the
            // whole group so its background/label/button remain aligned.
            movable = aiEntry.parent;
        }

        ActivateAiMenuHierarchy(aiEntry);
        movable.gameObject.SetActive(true);
        MoveAiMenuToNearestFreeSlot(movable);

        if (aiEntry.parent != null)
        {
            UIHelper.registEvent(aiEntry.parent.gameObject, "ai_", onClickAI);
        }
        UIHelper.registEvent(gameObject, "ai_", onClickAI);

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

    private void MoveAiMenuToNearestFreeSlot(Transform movable)
    {
        if (movable == null || movable.parent == null)
        {
            return;
        }

        Transform parent = movable.parent;
        Vector3 origin = movable.localPosition;

        // The shipped trans_menu uses roughly 40-unit vertical spacing. Search
        // both directions first so the AI item stays close to its intended
        // location, then try a second column if every nearby row is occupied.
        for (int step = 1; step <= 6; step++)
        {
            Vector3 up = origin + new Vector3(0f, 40f * step, 0f);
            if (IsAiMenuSlotFree(parent, movable, up))
            {
                movable.localPosition = up;
                return;
            }

            Vector3 down = origin + new Vector3(0f, -40f * step, 0f);
            if (IsAiMenuSlotFree(parent, movable, down))
            {
                movable.localPosition = down;
                return;
            }
        }

        Vector3 right = origin + new Vector3(180f, 0f, 0f);
        if (IsAiMenuSlotFree(parent, movable, right))
        {
            movable.localPosition = right;
            return;
        }

        Vector3 left = origin + new Vector3(-180f, 0f, 0f);
        if (IsAiMenuSlotFree(parent, movable, left))
        {
            movable.localPosition = left;
            return;
        }

        // Deterministic last resort: keep the proven original AI object visible
        // but offset it horizontally rather than allowing an exact overlap.
        movable.localPosition = right;
    }

    private bool IsAiMenuSlotFree(Transform parent, Transform movable, Vector3 candidate)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform sibling = parent.GetChild(i);
            if (sibling == null || sibling == movable || !sibling.gameObject.activeSelf)
            {
                continue;
            }

            Vector3 p = sibling.localPosition;
            if (Mathf.Abs(p.x - candidate.x) < 70f && Mathf.Abs(p.y - candidate.y) < 22f)
            {
                return false;
            }
        }
        return true;
    }

'''
    menu = menu.replace(helper_anchor, helper + helper_anchor, 1)
menu_path.write_text(menu, encoding="utf-8")

print("Finalized offline AI integration:")
print(f"  - installed {bootstrap_target}")
print(f"  - installed {viewport_target}")
print("  - retained explicit native 16:9 startup marker")
print("  - made UTF-8 native paths NUL-terminated and byte-safe")
print("  - enabled bundled AI/card-script bootstrap")
print("  - cloned AI into the visible menu layout instead of exposing the overlapping legacy slot")
print("  - added layout reposition/free-slot fallback for the AI menu entry")
print("  - replaced obsolete non-ASCII filename warning")
