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

# Several Settings controls are cloned/generated after the base prefab exists.
# Translate after initialize() has created those controls, not immediately after
# LAZYsetting is acquired.
initialize_end = "        screenFixHeight = Screen.height;\n    }"
initialize_replacement = (
    "        screenFixHeight = Screen.height;\n"
    "        SettingJapaneseLayout.Apply(gameObject);\n"
    "    }"
)
if "SettingJapaneseLayout.Apply(gameObject);" not in text:
    if text.count(initialize_end) != 1:
        raise SystemExit(
            "Could not uniquely locate the end of Setting.initialize()"
        )
    text = text.replace(initialize_end, initialize_replacement, 1)

# Re-apply whenever the Settings window is shown in case a label was refreshed.
show_anchor = (
    "    public override void applyShowArrangement()\n"
    "    {\n"
    "        base.applyShowArrangement();\n"
)
show_replacement = (
    show_anchor
    + "        SettingJapaneseLayout.Apply(gameObject);\n"
)
if text.count("SettingJapaneseLayout.Apply(gameObject);") < 2:
    if text.count(show_anchor) != 1:
        raise SystemExit("Could not locate Setting.applyShowArrangement()")
    text = text.replace(show_anchor, show_replacement, 1)

# The custom-resolution row is generated later while the screen is already
# visible. Localize its own literals directly so it never appears in Chinese.
literal_replacements = {
    'private const string CustomScreenItem = "自定义...";':
        'private const string CustomScreenItem = "カスタム...";',
    '"apply_", "应用",':
        '"apply_", "適用",',
    '"cancel_", "取消",':
        '"cancel_", "キャンセル",',
    'input.defaultText = "宽*高";':
        'input.defaultText = "幅×高さ";',
}
for source, target in literal_replacements.items():
    if source in text:
        text = text.replace(source, target)
    elif target not in text:
        raise SystemExit(f"Settings dynamic localization anchor missing: {source}")

setting_path.write_text(text, encoding="utf-8")

verify = setting_path.read_text(encoding="utf-8")
if verify.count("SettingJapaneseLayout.Apply(gameObject);") < 2:
    raise SystemExit("Settings localization hook verification failed")
for needle in (
    'private const string CustomScreenItem = "カスタム...";',
    '"apply_", "適用",',
    '"cancel_", "キャンセル",',
    'input.defaultText = "幅×高さ";',
):
    if needle not in verify:
        raise SystemExit(f"Settings dynamic source localization failed: {needle}")

helper_verify = helper_target.read_text(encoding="utf-8")
for needle in (
    "システム設定",
    "チェーン演出",
    "BGMを有効化",
    "リプレイを自動保存",
    "中央フェイズバーのタップを無効化",
    "SettingJapaneseLayout",
):
    if needle not in helper_verify:
        raise SystemExit(f"Settings Japanese helper verification failed: {needle}")

print("Prepared screen-local Japanese settings UI:")
print(f"  - patched Setting.initialize(): {setting_path}")
print(f"  - exact-match translator: {helper_target}")
print("  - no global InterString/UIHelper/Servant behavior changed")
