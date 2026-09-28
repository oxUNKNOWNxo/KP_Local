#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
assets = root / "Assets"

list_path = assets / "transUI" / "UIselectableList.cs"
select_deck_path = assets / "SibylSystem" / "SelectDeck" / "selectDeck.cs"
room_path = assets / "SibylSystem" / "Room" / "Room.cs"

for required in (list_path, select_deck_path, room_path):
    if not required.is_file():
        raise SystemExit(f"Deck-list density source missing: {required}")

# Stock KoishiPro2 uses a 45-unit row pitch and the shared row prefab starts at
# fontSize 38 / label height 33. On the widened iPhone X viewport this makes
# deck-name lists look sparse, while long names may shrink even smaller.
#
# Keep UIselectableList stock by default and add an opt-in per-list style so
# replay lists, room-browser rows, battle UI, etc. remain untouched.
text = list_path.read_text(encoding="utf-8-sig")

field_anchor = """    float heightOfEach = 45;
    public float preHeight = 0;
"""
field_replacement = """    float heightOfEach = 45;
    public float preHeight = 0;

    int forcedItemFontSize = 0;
    int forcedItemHeight = 0;

    public void ConfigureDeckNameRows(float rowHeight, int fontSize, int itemHeight)
    {
        heightOfEach = Mathf.Max(24f, rowHeight);
        forcedItemFontSize = Mathf.Max(0, fontSize);
        forcedItemHeight = Mathf.Max(0, itemHeight);
        needRefresh = true;
    }

    void ApplyDeckNameRowStyle(UIselectableListItem item)
    {
        if (item == null)
            return;

        if (item.lable != null)
        {
            if (forcedItemFontSize > 0)
                item.lable.fontSize = forcedItemFontSize;
            if (forcedItemHeight > 0)
                item.lable.height = forcedItemHeight;
        }

        if (forcedItemHeight > 0 && item.btn != null)
        {
            BoxCollider collider = item.btn.GetComponent<BoxCollider>();
            if (collider != null)
            {
                Vector3 size = collider.size;
                size.y = forcedItemHeight;
                collider.size = size;
            }
        }
    }
"""
if "ConfigureDeckNameRows" not in text:
    if text.count(field_anchor) != 1:
        raise SystemExit("Could not uniquely locate UIselectableList row-height fields")
    text = text.replace(field_anchor, field_replacement, 1)

create_anchor = """                        currentItem.obj = MonoBehaviour
                            .Instantiate<GameObject>(mod)
                            .GetComponent<UIselectableListItem>();
                        currentItem.obj.List = this;
                        currentItem.obj.transform.SetParent(panel.transform, false);
                        currentItem.obj.lable.width = (int)width - 10;
"""
create_replacement = """                        currentItem.obj = MonoBehaviour
                            .Instantiate<GameObject>(mod)
                            .GetComponent<UIselectableListItem>();
                        currentItem.obj.List = this;
                        currentItem.obj.transform.SetParent(panel.transform, false);
                        ApplyDeckNameRowStyle(currentItem.obj);
                        currentItem.obj.lable.width = (int)width - 10;
"""
if "ApplyDeckNameRowStyle(currentItem.obj);" not in text:
    if text.count(create_anchor) != 1:
        raise SystemExit("Could not uniquely locate selectable-row creation")
    text = text.replace(create_anchor, create_replacement, 1)

list_path.write_text(text, encoding="utf-8")

# Deck selection / deck management entry screen.
text = select_deck_path.read_text(encoding="utf-8-sig")
select_anchor = """        superScrollView = gameObject.GetComponentInChildren<UIselectableList>();
        superScrollView.selectedAction = onSelected;
"""
select_replacement = """        superScrollView = gameObject.GetComponentInChildren<UIselectableList>();
        superScrollView.ConfigureDeckNameRows(40f, 40, 36);
        superScrollView.selectedAction = onSelected;
"""
if "ConfigureDeckNameRows(40f, 40, 36);" not in text:
    if text.count(select_anchor) != 1:
        raise SystemExit("Could not uniquely locate selectDeck selectable list")
    text = text.replace(select_anchor, select_replacement, 1)
select_deck_path.write_text(text, encoding="utf-8")

# Deck selector shown in single/match/tag online rooms. Do not touch RoomList:
# its rows contain room metadata rather than deck names.
text = room_path.read_text(encoding="utf-8-sig")
room_anchor = """        superScrollView = gameObject.GetComponentInChildren<UIselectableList>();
        superScrollView.selectedAction = onSelected;
        superScrollView.install();
        printFile();
"""
room_replacement = """        superScrollView = gameObject.GetComponentInChildren<UIselectableList>();
        superScrollView.ConfigureDeckNameRows(40f, 40, 36);
        superScrollView.selectedAction = onSelected;
        superScrollView.install();
        printFile();
"""
if "ConfigureDeckNameRows(40f, 40, 36);" not in text:
    if text.count(room_anchor) != 1:
        raise SystemExit("Could not uniquely locate Room deck selectable list")
    text = text.replace(room_anchor, room_replacement, 1)
room_path.write_text(text, encoding="utf-8")

verify_list = list_path.read_text(encoding="utf-8")
verify_select = select_deck_path.read_text(encoding="utf-8")
verify_room = room_path.read_text(encoding="utf-8")

for needle in (
    "ConfigureDeckNameRows",
    "ApplyDeckNameRowStyle(currentItem.obj);",
    "float heightOfEach = 45;",
):
    if needle not in verify_list:
        raise SystemExit(f"UIselectableList density verification failed: {needle}")

if "ConfigureDeckNameRows(40f, 40, 36);" not in verify_select:
    raise SystemExit("selectDeck density hook verification failed")
if "ConfigureDeckNameRows(40f, 40, 36);" not in verify_room:
    raise SystemExit("Room density hook verification failed")

print("Prepared compact deck-name lists:")
print("  - stock default UIselectableList row pitch remains 45")
print("  - selectDeck deck-name rows: pitch 40 / font 40 / item height 36")
print("  - Room deck-name rows: pitch 40 / font 40 / item height 36")
print("  - replay, room-browser, puzzle and other selectable lists unchanged")
