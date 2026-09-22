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

# Explicitly install the viewport from KoishiPro2's normal startup path.  Keep
# RuntimeInitializeOnLoadMethod as a second path, but do not depend on it under
# iOS IL2CPP stripping/runtime initialization.
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

# Restore a usable AI entry in the live menu.  Prefer the historical ai_ entry
# when it exists, but do not assume the current prefab still contains it: if it
# is absent, clone the visible single_ entry, rename it to ai_, relabel it, and
# wire it directly to onClickAI.  This removes the prefab-history dependency
# that made the previous visibility-only patch a no-op on some menu variants.
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
        Transform aiEntry = null;
        Transform singleEntry = null;

        Transform[] entries = gameObject.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < entries.Length; i++)
        {
            Transform entry = entries[i];
            if (entry == null)
            {
                continue;
            }
            if (entry.name == "ai_" && aiEntry == null)
            {
                aiEntry = entry;
            }
            else if (entry.name == "single_" && singleEntry == null)
            {
                singleEntry = entry;
            }
        }

        // Some menu variants are instantiated outside this servant's direct
        // transform tree.  The visible single_ object is active, so use the
        // scene lookup as a safe fallback.
        if (aiEntry == null)
        {
            GameObject activeAi = GameObject.Find("ai_");
            if (activeAi != null)
            {
                aiEntry = activeAi.transform;
            }
        }
        if (singleEntry == null)
        {
            GameObject activeSingle = GameObject.Find("single_");
            if (activeSingle != null)
            {
                singleEntry = activeSingle.transform;
            }
        }

        bool cloned = false;
        if (aiEntry == null && singleEntry != null && singleEntry.parent != null)
        {
            GameObject clone = UnityEngine.Object.Instantiate(singleEntry.gameObject, singleEntry.parent, false);
            clone.name = "ai_";
            clone.transform.localPosition = singleEntry.localPosition;
            clone.transform.localRotation = singleEntry.localRotation;
            clone.transform.localScale = singleEntry.localScale;
            clone.transform.SetSiblingIndex(singleEntry.GetSiblingIndex() + 1);
            clone.SetActive(true);
            aiEntry = clone.transform;
            cloned = true;

            SetAiMenuLabel(clone);
            if (!TryRepositionMenuParent(clone.transform.parent))
            {
                clone.transform.localPosition = singleEntry.localPosition + new Vector3(0f, -80f, 0f);
            }
        }

        if (aiEntry == null)
        {
            UnityEngine.Debug.LogWarning("[OfflineAI] Neither ai_ nor a cloneable single_ menu entry was found.");
            return;
        }

        aiEntry.gameObject.SetActive(true);

        // Register against the actual parent as well as the normal Menu root.
        // This covers menu prefabs instantiated under a separate window root.
        if (aiEntry.parent != null)
        {
            UIHelper.registEvent(aiEntry.parent.gameObject, "ai_", onClickAI);
        }
        UIHelper.registEvent(gameObject, "ai_", onClickAI);

        UnityEngine.Debug.Log("[OfflineAI] AI menu entry ready. cloned=" + cloned + " path=" + aiEntry.name);
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
print("  - explicitly installed 16:9 viewport from Program startup")
print("  - made UTF-8 native paths NUL-terminated and byte-safe")
print("  - enabled first-use bundled AI data bootstrap")
print("  - restored/wired AI menu entry with single_ clone fallback")
print("  - installed centred 16:9 runtime viewport for extra-wide iPhones")
print("  - replaced obsolete non-ASCII filename warning")
