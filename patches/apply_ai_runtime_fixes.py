#!/usr/bin/env python3
from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
assets = root / "Assets"
if not assets.is_dir():
    raise SystemExit(f"Assets directory not found: {assets}")

patch_root = Path(__file__).resolve().parent

# ---------------------------------------------------------------------------
# Install a device-readable AI runtime log. The old native core reports Lua and
# AI failures through the chat callback; writing them to a file lets an iPhone-
# only test capture the real failure without hundreds of popups/messages.
# ---------------------------------------------------------------------------
log_source = patch_root / "ai" / "AIRuntimeLog.cs"
log_target = assets / "SibylSystem" / "AIRuntimeLog.cs"
if not log_source.is_file():
    raise SystemExit(f"AI runtime logger missing: {log_source}")
shutil.copyfile(log_source, log_target)

core_path = assets / "SibylSystem" / "coreWrapper.cs"
core = core_path.read_text(encoding="utf-8-sig")
old_handler = '''            string message = System.Text.Encoding.UTF8.GetString(arr);
            if (message.Contains("\\0"))
                message = message.Substring(0, message.IndexOf('\\0'));
            chat_handler(message);'''
new_handler = '''            string message = System.Text.Encoding.UTF8.GetString(arr);
            if (message.Contains("\\0"))
                message = message.Substring(0, message.IndexOf('\\0'));
            AIRuntimeLog.Write("native[" + messageType + "]: " + message);
            if (AIRuntimeLog.ShouldShowToChat(message))
                chat_handler(message);'''
if old_handler not in core:
    raise SystemExit("Could not locate native AI message handler for runtime logging")
core = core.replace(old_handler, new_handler, 1)
core_path.write_text(core, encoding="utf-8")

airoom_path = assets / "SibylSystem" / "Room" / "AIRoom.cs"
airoom = airoom_path.read_text(encoding="utf-8-sig")
if "AIRuntimeLog.ResetSession();" not in airoom:
    anchor = "        int l = 8000;\n"
    if anchor not in airoom:
        raise SystemExit("Could not locate AIRoom.onStart runtime-log insertion point")
    airoom = airoom.replace(anchor, "        AIRuntimeLog.ResetSession();\n" + anchor, 1)
airoom_path.write_text(airoom, encoding="utf-8")

# ---------------------------------------------------------------------------
# Move the restored AI entry to an actually free row. The historical ai branch
# occupies coordinates now used by My Card. Work from the live transform tree:
# find the lowest common ancestor of AI and Single Player, identify each branch
# directly below it, then append AI one row below the lowest active sibling in
# Single Player's column. This avoids hard-coding one prefab's coordinates.
# ---------------------------------------------------------------------------
menu_path = assets / "SibylSystem" / "Menu" / "Menu.cs"
menu = menu_path.read_text(encoding="utf-8-sig")
call_anchor = "        ActivateAiMenuHierarchy(aiEntry);\n\n"
if "RepositionAiMenuSlot(aiEntry, singleEntry);" not in menu:
    if call_anchor not in menu:
        raise SystemExit("Could not locate AI hierarchy activation call")
    menu = menu.replace(
        call_anchor,
        call_anchor + "        RepositionAiMenuSlot(aiEntry, singleEntry);\n\n",
        1,
    )

helper_anchor = "    private void SetAiMenuLabel(GameObject root)\n"
if "private void RepositionAiMenuSlot(" not in menu:
    if helper_anchor not in menu:
        raise SystemExit("Could not locate AI menu helper insertion point")
    helper = '''    private void RepositionAiMenuSlot(Transform aiEntry, Transform singleEntry)
    {
        if (aiEntry == null || singleEntry == null)
        {
            return;
        }

        Transform common = FindAiMenuCommonAncestor(aiEntry, singleEntry);
        if (common == null)
        {
            UnityEngine.Debug.LogWarning("[OfflineAI] Could not find common menu ancestor for placement.");
            return;
        }

        Transform aiSlot = FindAiMenuBranch(aiEntry, common);
        Transform singleSlot = FindAiMenuBranch(singleEntry, common);
        if (aiSlot == null || singleSlot == null)
        {
            return;
        }

        // If both buttons live in one legacy branch, fall back to the buttons
        // themselves so moving AI cannot move Single Player with it.
        if (aiSlot == singleSlot)
        {
            aiSlot = aiEntry;
            singleSlot = singleEntry;
        }

        Transform rowRoot = singleSlot.parent;
        if (rowRoot == null || aiSlot.parent != rowRoot)
        {
            UnityEngine.Debug.LogWarning("[OfflineAI] AI and Single Player rows do not share a layout parent.");
            return;
        }

        System.Collections.Generic.List<float> occupied = new System.Collections.Generic.List<float>();
        float columnX = singleSlot.localPosition.x;
        for (int i = 0; i < rowRoot.childCount; i++)
        {
            Transform child = rowRoot.GetChild(i);
            if (child == null || child == aiSlot || !child.gameObject.activeInHierarchy)
            {
                continue;
            }
            if (Mathf.Abs(child.localPosition.x - columnX) > 24f)
            {
                continue;
            }

            float y = child.localPosition.y;
            bool duplicate = false;
            for (int j = 0; j < occupied.Count; j++)
            {
                if (Mathf.Abs(occupied[j] - y) < 1f)
                {
                    duplicate = true;
                    break;
                }
            }
            if (!duplicate)
            {
                occupied.Add(y);
            }
        }

        if (occupied.Count == 0)
        {
            occupied.Add(singleSlot.localPosition.y);
        }
        occupied.Sort();

        float spacing = 60f;
        if (occupied.Count >= 2)
        {
            float best = 9999f;
            for (int i = 1; i < occupied.Count; i++)
            {
                float gap = Mathf.Abs(occupied[i] - occupied[i - 1]);
                if (gap >= 20f && gap < best)
                {
                    best = gap;
                }
            }
            if (best < 9999f)
            {
                spacing = Mathf.Clamp(best, 40f, 100f);
            }
        }

        Vector3 before = aiSlot.localPosition;
        float targetY = occupied[0] - spacing;
        aiSlot.localPosition = new Vector3(columnX, targetY, before.z);
        UnityEngine.Debug.Log(
            "[OfflineAI] AI menu row moved from " + before + " to " + aiSlot.localPosition +
            " using spacing=" + spacing
        );
    }

    private Transform FindAiMenuCommonAncestor(Transform a, Transform b)
    {
        Transform pa = a;
        int guardA = 0;
        while (pa != null && guardA < 32)
        {
            Transform pb = b;
            int guardB = 0;
            while (pb != null && guardB < 32)
            {
                if (pa == pb)
                {
                    return pa;
                }
                pb = pb.parent;
                guardB++;
            }
            pa = pa.parent;
            guardA++;
        }
        return null;
    }

    private Transform FindAiMenuBranch(Transform leaf, Transform ancestor)
    {
        if (leaf == null || ancestor == null || leaf == ancestor)
        {
            return leaf;
        }

        Transform cursor = leaf;
        int guard = 0;
        while (cursor.parent != null && cursor.parent != ancestor && guard < 32)
        {
            cursor = cursor.parent;
            guard++;
        }
        return cursor;
    }

'''
    menu = menu.replace(helper_anchor, helper + helper_anchor, 1)
menu_path.write_text(menu, encoding="utf-8")

print("Applied AI runtime fixes:")
print(f"  - installed {log_target}")
print("  - native/Lua AI messages are logged to offline-ai-runtime.log")
print("  - repetitive native error display is throttled")
print("  - runtime log resets at each AI duel start")
print("  - AI menu row is appended below the lowest occupied row")
