#!/usr/bin/env python3
from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
assets = root / "Assets"
patch_root = Path(__file__).resolve().parent

room_source = patch_root / "ai" / "windbot" / "AIRoom.cs"
bridge_source = patch_root / "ai" / "windbot" / "KoishiWindBotBridge.cs"
room_target = assets / "SibylSystem" / "Room" / "AIRoom.cs"
bridge_target = assets / "SibylSystem" / "KoishiWindBotBridge.cs"
program_path = assets / "SibylSystem" / "Program.cs"

for p in (room_source, bridge_source, room_target, program_path):
    if not p.is_file():
        raise SystemExit(f"Required WindBot activation file missing: {p}")

shutil.copyfile(room_source, room_target)
shutil.copyfile(bridge_source, bridge_target)

program = program_path.read_text(encoding="utf-8-sig")
program = program.replace("        AIBootstrap.EnsureInstalled();\n", "")
if "AIBootstrap.EnsureInstalled();" in program:
    raise SystemExit("Could not remove legacy AIBootstrap startup call")
if "MainScriptBootstrap.EnsureInstalled();" not in program:
    raise SystemExit("Main script bootstrap is missing")
program_path.write_text(program, encoding="utf-8")

for legacy in (
    assets / "SibylSystem" / "coreWrapper.cs",
    assets / "SibylSystem" / "precy.cs",
    assets / "SibylSystem" / "AIBootstrap.cs",
    assets / "Plugins" / "iOS" / "libocgcore.a",
):
    if legacy.exists():
        legacy.unlink()

offenders = []
for path in (assets / "SibylSystem").rglob("*.cs"):
    text = path.read_text(encoding="utf-8-sig")
    if "PrecyOcg" in text or "Percy.smallYgopro" in text or "AIBootstrap" in text:
        offenders.append(str(path))
if offenders:
    raise SystemExit("Legacy Percy references remain in generated Unity source: " + ", ".join(offenders))

room = room_target.read_text(encoding="utf-8-sig")
required = (
    "KoishiWindBotBridge",
    "RadiantTyphoon",
    "Master Rule 2020" if False else "WindBot",
)
for marker in required:
    if marker not in room:
        raise SystemExit(f"WindBot AIRoom marker missing: {marker}")

print("Activated WindBot-only AI build:")
print(f"  - {room_target}")
print(f"  - {bridge_target}")
print("  - removed generated Percy managed sources")
print("  - removed legacy AI bootstrap")
print("  - removed legacy libocgcore.a if present")
