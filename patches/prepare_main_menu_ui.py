#!/usr/bin/env python3
from pathlib import Path
import re
import shutil
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
patch_root = Path(__file__).resolve().parent
assets = root / "Assets"
menu_path = assets / "SibylSystem" / "Menu" / "Menu.cs"
helper_source = patch_root / "menu" / "MenuResponsiveLayout.cs"
helper_target = assets / "SibylSystem" / "Menu" / "MenuResponsiveLayout.cs"

for required in (menu_path, helper_source):
    if not required.is_file():
        raise SystemExit(f"Responsive menu source missing: {required}")

helper_target.parent.mkdir(parents=True, exist_ok=True)
shutil.copyfile(helper_source, helper_target)

text = menu_path.read_text(encoding="utf-8-sig")

# Re-apply layout whenever the menu becomes visible. The immediate pass fixes
# normal entry, and the delayed pass catches NGUI anchors/translations that are
# refreshed on the following layout tick.
if "MenuResponsiveLayout.ApplyAndSchedule(gameObject);" not in text:
    show = re.search(
        r"(?ms)(public\s+override\s+void\s+show\s*\(\s*\)\s*\{)(.*?)(\n\s*\})",
        text,
    )
    if not show:
        raise SystemExit("Could not locate Menu.show()")

    body = show.group(2)
    base_show = re.search(r"(?m)^(?P<indent>\s*)base\.show\(\);\s*$", body)
    if not base_show:
        raise SystemExit("Could not locate base.show() in Menu.show()")

    indent = base_show.group("indent")
    insertion = (
        base_show.group(0)
        + "\n"
        + indent + "MenuResponsiveLayout.ApplyAndSchedule(gameObject);"
    )
    body = body[:base_show.start()] + insertion + body[base_show.end():]
    text = text[:show.start(2)] + body + text[show.end(2):]

menu_path.write_text(text, encoding="utf-8")

verify = menu_path.read_text(encoding="utf-8")
for needle in (
    "MenuResponsiveLayout.ApplyAndSchedule(gameObject);",
):
    if needle not in verify:
        raise SystemExit(f"Responsive menu patch verification failed: {needle}")

helper_verify = helper_target.read_text(encoding="utf-8")
for needle in (
    "fixedFontSize",
    "UILabel.Overflow.ResizeFreely",
    "UILabel.Overflow.ClampContent",
    "panelWidth",
    "panelHeight",
    "CollectRows",
    "RefreshAfterNguiLayout",
    "TranslateMenuLabels",
):
    if needle not in helper_verify:
        raise SystemExit(f"Responsive menu helper verification failed: {needle}")

print("Prepared responsive main menu:")
print(f"  - patched Menu.show(): {menu_path}")
print(f"  - runtime layout helper: {helper_target}")
print("  - fixed one font size across visible menu rows")
print("  - disabled per-label shrink/wrap behavior")
print("  - panel width/height follows translated text and row count")
