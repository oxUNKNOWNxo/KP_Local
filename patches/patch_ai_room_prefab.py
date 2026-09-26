#!/usr/bin/env python3
from pathlib import Path
import re
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
assets = root / "Assets"
prefab = assets / "transUI" / "prefab" / "trans_AIroom.prefab"
uisprite_meta = assets / "NGUI" / "Scripts" / "UI" / "UISprite.cs.meta"
uipanel_meta = assets / "NGUI" / "Scripts" / "UI" / "UIPanel.cs.meta"
uitexture_meta = assets / "NGUI" / "Scripts" / "UI" / "UITexture.cs.meta"

for required in (prefab, uisprite_meta, uipanel_meta, uitexture_meta):
    if not required.is_file():
        raise SystemExit(f"AI room layout source missing: {required}")

def guid_from_meta(path: Path) -> str:
    m = re.search(r"(?m)^guid:\s*([0-9a-f]+)\s*$", path.read_text(encoding="utf-8-sig"))
    if not m:
        raise SystemExit(f"Could not read GUID from {path}")
    return m.group(1)

uisprite_guid = guid_from_meta(uisprite_meta)
uipanel_guid = guid_from_meta(uipanel_meta)
uitexture_guid = guid_from_meta(uitexture_meta)

text = prefab.read_text(encoding="utf-8-sig")
parts = re.split(r"(?=--- !u!)", text)

def block_header(block: str):
    m = re.match(r"--- !u!(\d+) &(\d+)", block)
    return m.groups() if m else (None, None)

def game_object_id(name: str) -> str:
    matches = []
    for block in parts:
        typ, fid = block_header(block)
        if typ != "1":
            continue
        m = re.search(r"(?m)^  m_Name:\s*(.+?)\s*$", block)
        if m and m.group(1) == name:
            matches.append(fid)
    if len(matches) != 1:
        raise SystemExit(f"Expected exactly one GameObject named {name!r}, found {matches}")
    return matches[0]

def component_index(go_id: str, unity_type: str, script_guid: str | None = None) -> int:
    matches = []
    for i, block in enumerate(parts):
        typ, _ = block_header(block)
        if typ != unity_type:
            continue
        if f"m_GameObject: {{fileID: {go_id}}}" not in block:
            continue
        if script_guid is not None and f"guid: {script_guid}" not in block:
            continue
        matches.append(i)
    if len(matches) != 1:
        raise SystemExit(
            f"Expected one component type={unity_type} go={go_id} guid={script_guid}, found {len(matches)}"
        )
    return matches[0]

def replace_number(block: str, field: str, target: int, accepted: tuple[int, ...]) -> str:
    m = re.search(rf"(?m)^(\s*{re.escape(field)}:\s*)(-?\d+)(\s*)$", block)
    if not m:
        raise SystemExit(f"Field {field} not found")
    current = int(m.group(2))
    if current not in accepted and current != target:
        raise SystemExit(f"Unexpected {field}={current}; accepted={accepted}, target={target}")
    return block[:m.start(2)] + str(target) + block[m.end(2):]

def replace_clip_width(block: str, target: int, accepted: tuple[int, ...]) -> str:
    pat = re.compile(
        r"(?m)^(\s*mClipRange:\s*\{x:\s*[^,]+,\s*y:\s*[^,]+,\s*z:\s*)([-0-9.]+)(,\s*w:\s*[^}]+\}\s*)$"
    )
    m = pat.search(block)
    if not m:
        raise SystemExit("mClipRange not found")
    current = float(m.group(2))
    accepted_f = tuple(float(v) for v in accepted)
    if current not in accepted_f and current != float(target):
        raise SystemExit(f"Unexpected clip width={current}; accepted={accepted}, target={target}")
    return block[:m.start(2)] + str(target) + block[m.end(2):]

def replace_clip_height(block: str, target: int, accepted: tuple[int, ...]) -> str:
    pat = re.compile(
        r"(?m)^(\s*mClipRange:\s*\{x:\s*[^,]+,\s*y:\s*[^,]+,\s*z:\s*[^,]+,\s*w:\s*)([-0-9.]+)(\}\s*)$"
    )
    m = pat.search(block)
    if not m:
        raise SystemExit("mClipRange height not found")
    current = float(m.group(2))
    accepted_f = tuple(float(v) for v in accepted)
    if current not in accepted_f and current != float(target):
        raise SystemExit(f"Unexpected clip height={current}; accepted={accepted}, target={target}")
    return block[:m.start(2)] + str(target) + block[m.end(2):]

def replace_local_x(block: str, target: int, accepted: tuple[int, ...]) -> str:
    pat = re.compile(
        r"(?m)^(\s*m_LocalPosition:\s*\{x:\s*)([-0-9.]+)(,\s*y:\s*[^,]+,\s*z:\s*[^}]+\}\s*)$"
    )
    m = pat.search(block)
    if not m:
        raise SystemExit("m_LocalPosition not found")
    current = float(m.group(2))
    accepted_f = tuple(float(v) for v in accepted)
    if current not in accepted_f and current != float(target):
        raise SystemExit(f"Unexpected local x={current}; accepted={accepted}, target={target}")
    return block[:m.start(2)] + str(target) + block[m.end(2):]

# The root content UIPanel is the critical visual clip. If it stays 500x400,
# any widened mainWindow/list is still cut back to the legacy rectangle.
container_go = game_object_id("GameObject")
idx = component_index(container_go, "114", uipanel_guid)
parts[idx] = replace_clip_width(parts[idx], 1100, (500, 980))
parts[idx] = replace_clip_height(parts[idx], 480, (400, 420, 460))

# The sibling glass texture supplies the translucent/blurred backdrop around
# the modal. Widen it with the window so it does not remain a legacy-sized box.
glass_go = game_object_id("glass")
idx = component_index(glass_go, "114", uitexture_guid)
parts[idx] = replace_number(parts[idx], "mWidth", 1064, (464, 944))
parts[idx] = replace_number(parts[idx], "mHeight", 430, (350, 370, 410))

# The original AI room is only 500x400, while one deck list already consumes
# 230x314. A three-column layout cannot fit inside it. Patch the serialized
# NGUI prefab itself so the first activation cannot restore the old dimensions.
main_go = game_object_id("mainWindow")
idx = component_index(main_go, "114", uisprite_guid)
parts[idx] = replace_number(parts[idx], "mWidth", 1100, (500, 980))
parts[idx] = replace_number(parts[idx], "mHeight", 480, (400, 420, 460))

# Widen the original deck-list frame before AIRoom clones it for the AI side.
deck_go = game_object_id("deck")
idx = component_index(deck_go, "114", uisprite_guid)
parts[idx] = replace_number(parts[idx], "mWidth", 330, (230, 280))

# Keep the clipping panel and scrollbar consistent with the widened list.
panel_go = game_object_id("panel_")
idx = component_index(panel_go, "114", uipanel_guid)
parts[idx] = replace_clip_width(parts[idx], 290, (210, 240, 260))

bar_go = game_object_id("bar_")
idx = component_index(bar_go, "4")
parts[idx] = replace_local_x(parts[idx], 160, (110, 135))

# The separator belongs to the main frame. Let it span the widened window.
line_go = game_object_id("line_")
idx = component_index(line_go, "114", uisprite_guid)
parts[idx] = replace_number(parts[idx], "mWidth", 1068, (468, 948))

patched = "".join(parts)
prefab.write_text(patched, encoding="utf-8")

# Fail-fast verification.
verify = prefab.read_text(encoding="utf-8")
for needle in (
    "mClipRange: {x: 0, y: 0, z: 1100, w: 480}",
    "mWidth: 1064",
    "mHeight: 430",
    "mWidth: 1100",
    "mHeight: 480",
    "mWidth: 330",
    "mClipRange: {x: -0.0000038146973, y: 0, z: 290, w: 314}",
    "m_LocalPosition: {x: 160, y: 0, z: 0}",
    "mWidth: 1068",
):
    if needle not in verify:
        raise SystemExit(f"AI room prefab verification failed: {needle}")

print("Patched serialized AI room geometry:")
print("  - root UIPanel clip: 1100x480")
print("  - glass backdrop: 1064x430")
print("  - mainWindow: 1100x480")
print("  - deck list frame: 330 wide")
print("  - deck clip region: 290 wide")
print("  - scrollbar x: 160")
print("  - header separator: 1068 wide")
