#!/usr/bin/env python3
from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
patch_root = Path(__file__).resolve().parent
assets = root / "Assets"

translation_source = patch_root / "localization" / "ja-JP_translation.conf"
streaming = assets / "StreamingAssets"
translation_reference = streaming / "koishi-ja-translation-reference.conf"

if not translation_source.is_file():
    raise SystemExit(f"Japanese translation reference missing: {translation_source}")

streaming.mkdir(parents=True, exist_ok=True)
shutil.copyfile(translation_source, translation_reference)

# IMPORTANT:
# Do not patch InterString, UIHelper.InterGameObject, UIHelper.trySetLableText,
# or Servant.fixScreenProblem globally. The fixed KoishiPro2 source has several
# battle UI labels/fonts that are sized and populated specially. Feeding the
# newer Japanese dictionary into every UILabel caused chain-confirmation layout
# regressions and missing-glyph boxes on device.
#
# Japanese localization must therefore be applied per-screen by reviewed
# layout/localization helpers (currently MenuResponsiveLayout for main menu).
for protected in (
    assets / "SibylSystem" / "InterString.cs",
    assets / "SibylSystem" / "MonoHelpers" / "UIHelper.cs",
    assets / "SibylSystem" / "Servant.cs",
):
    if not protected.is_file():
        raise SystemExit(f"Protected Koishi UI source missing: {protected}")

print("Prepared safe Japanese localization reference:")
print(f"  - reference dictionary: {translation_reference}")
print("  - global InterString/UIHelper/Servant behavior left untouched")
print("  - screen-specific localization is applied by reviewed UI helpers")
