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

# Do not expose the historical hidden ai branch directly: on the current menu
# its coordinates overlap another visible entry (My Card). Instead, clone the
# already-visible single_ entry into the live menu layout, rename it ai_, put it
# at the end of the same layout, then let the menu layout component reposition
# its children. A geometry fallback places it in a free slot if there is no
# Reposition-capable component.
menu_path = assets / "SibylSystem" / "Menu" / "Menu.cs"
menu = menu_path.read_text(encoding="utf-8-sig")
create_anchor = "        createWindow(Program.I().new_ui_menu);\n"
if create_anchor not in menu:
    raise SystemExit("Could not locate Menu.initialize main-menu createWindow call")
if "EnableAiMenuEntry();" not in menu:
    menu = menu.replace(create_anchor, create_anchor + "        EnableAiMenuEntry();\n", 1)

helper_anchor = "    private void CreateSuperPreMenuItem()\n"
if "private void EnableAiMenuEntry()" not in menu:
    if helper_anchor not in menu:
        raise SystemExit("Could not locate menu helper insertion point")
    helper = r'''    private void EnableAiMenuEntry()
    {
        Transform legacyAiEntry = null;
        Transform singleEntry = null;
        Transform[] entries = gameObject.GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < entries.Length; i++)
        {
            Transform entry = entries[i];
            if (entry == null)
            {
                continue;
            }

            if (entry.name == "ai_" && legacyAiEntry == null)
            {
                legacyAiEntry = entry;
            }
            else if (entry.name == "single_" && entry.gameObject.activeInHierarchy && singleEntry == null)
            {
                singleEntry = entry;
            }
        }

        if (singleEntry == null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                Transform entry = entries[i];
                if (entry != null && entry.name == "single_")
                {
                    singleEntry = entry;
                    break;
                }
            }
        }

        Transform aiEntry = null;
        bool cloned = false;

        if (singleEntry != null && singleEntry.parent != null)
        {
            if (legacyAiEntry != null)
            {
                // Prevent event lookup from finding the old hidden/overlapping
                // entry. Its parent remains untouched and can stay inactive.
                legacyAiEntry.name = "ai_legacy_hidden";
            }

            GameObject clone = UnityEngine.Object.Instantiate(singleEntry.gameObject, singleEntry.parent, false);
            clone.name = "ai_";
            clone.transform.localPosition = singleEntry.localPosition;
            clone.transform.localRotation = singleEntry.localRotation;
            clone.transform.localScale = singleEntry.localScale;
            clone.SetActive(true);
            clone.transform.SetSiblingIndex(clone.transform.parent.childCount - 1);
            SetAiMenuLabel(clone);

            if (!TryRepositionMenuParent(clone.transform.parent))
            {
                PlaceAiMenuInFreeSlot(clone.transform, singleEntry);
            }

            aiEntry = clone.transform;
            cloned = true;
        }
        else if (legacyAiEntry != null)
        {
            // Last-resort compatibility path for an unexpected prefab variant.
            ActivateAiMenuHierarchy(legacyAiEntry);
            PlaceAiMenuInFreeSlot(legacyAiEntry, null);
            aiEntry = legacyAiEntry;
        }

        if (aiEntry == null)
        {
            UnityEngine.Debug.LogWarning("[OfflineAI] No visible menu entry could be created for AI.");
            return;
        }

        if (aiEntry.parent != null)
        {
            UIHelper.registEvent(aiEntry.parent.gameObject, "ai_", onClickAI);
        }
        UIHelper.registEvent(gameObject, "ai_", onClickAI);
        UnityEngine.Debug.Log("[OfflineAI] AI menu entry ready. cloned=" + cloned
            + " position=" + aiEntry.localPosition
            + " activeInHierarchy=" + aiEntry.gameObject.activeInHierarchy);
    }

    private void ActivateAiMenuHierarchy(Transform entry)
    {
        Transform cursor = entry;
        bool activatedHiddenBranch = false;
        int depth = 0;

        while (cursor != null && depth < 12)
        {
            if (!cursor.gameObject.activeSelf)
            {
                cursor.gameObject.SetActive(true);
                activatedHiddenBranch = true;
            }
            else if (activatedHiddenBranch && cursor != entry)
            {
                break;
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

    private void PlaceAiMenuInFreeSlot(Transform entry, Transform reference)
    {
        if (entry == null || entry.parent == null)
        {
            return;
        }

        Transform parent = entry.parent;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float minX = float.MaxValue;
        float maxY = float.MinValue;
        int count = 0;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child == null || child == entry || !child.gameObject.activeSelf)
            {
                continue;
            }
            Vector3 p = child.localPosition;
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            count++;
        }

        Vector3 basePos = reference != null ? reference.localPosition : entry.localPosition;
        if (count == 0)
        {
            entry.localPosition = basePos + new Vector3(0f, -80f, 0f);
            return;
        }

        float xSpread = maxX - minX;
        float ySpread = maxY - minY;
        if (xSpread > ySpread * 1.25f)
        {
            entry.localPosition = new Vector3(maxX + 100f, basePos.y, basePos.z);
        }
        else
        {
            entry.localPosition = new Vector3(basePos.x, minY - 80f, basePos.z);
        }
    }

    private void SetAiMenuLabel(GameObject root)
    {
        Component[] components = root.GetComponentsInChildren<Component>(true);
        int changed = 0;
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null)
            {
                continue;
            }

            System.Reflection.PropertyInfo property = component.GetType().GetProperty(
                "text",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
            );
            if (property == null || !property.CanWrite || property.PropertyType != typeof(string))
            {
                continue;
            }

            try
            {
                property.SetValue(component, "AI", null);
                changed++;
            }
            catch
            {
            }
        }
        UnityEngine.Debug.Log("[OfflineAI] AI menu label components updated: " + changed);
    }

    private bool TryRepositionMenuParent(Transform parent)
    {
        if (parent == null)
        {
            return false;
        }

        Component[] components = parent.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null)
            {
                continue;
            }

            System.Reflection.MethodInfo reposition = component.GetType().GetMethod(
                "Reposition",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                null,
                System.Type.EmptyTypes,
                null
            );
            if (reposition == null)
            {
                continue;
            }

            try
            {
                reposition.Invoke(component, null);
                return true;
            }
            catch
            {
            }
        }
        return false;
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
