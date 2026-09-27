#!/usr/bin/env python3
from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
patch_root = Path(__file__).resolve().parent
assets = root / "Assets"

setting_path = assets / "SibylSystem" / "Setting" / "Setting.cs"
helper_source = patch_root / "settings" / "SettingJapaneseLayout.cs"
helper_target = assets / "SibylSystem" / "Setting" / "SettingJapaneseLayout.cs"

for required in (setting_path, helper_source):
    if not required.is_file():
        raise SystemExit(f"Settings Japanese patch source missing: {required}")

helper_target.parent.mkdir(parents=True, exist_ok=True)
shutil.copyfile(helper_source, helper_target)

text = setting_path.read_text(encoding="utf-8-sig")
needle = "        setting = gameObject.GetComponentInChildren<LAZYsetting>();"
insertion = (
    needle
    + "\n"
    + "        SettingJapaneseLayout.Apply(gameObject);"
)

if "SettingJapaneseLayout.Apply(gameObject);" not in text:
    if text.count(needle) != 1:
        raise SystemExit(
            "Could not uniquely locate Setting.initialize() localization anchor"
        )
    text = text.replace(needle, insertion, 1)

setting_path.write_text(text, encoding="utf-8")

verify = setting_path.read_text(encoding="utf-8")
if "SettingJapaneseLayout.Apply(gameObject);" not in verify:
    raise SystemExit("Settings localization hook verification failed")

helper_verify = helper_target.read_text(encoding="utf-8")
for needle in (
    "システム設定",
    "チェーン演出",
    "BGMを有効化",
    "SettingJapaneseLayout",
):
    if needle not in helper_verify:
        raise SystemExit(f"Settings Japanese helper verification failed: {needle}")

print("Prepared screen-local Japanese settings UI:")
print(f"  - patched Setting.initialize(): {setting_path}")
print(f"  - exact-match translator: {helper_target}")
print("  - no global InterString/UIHelper/Servant behavior changed")
