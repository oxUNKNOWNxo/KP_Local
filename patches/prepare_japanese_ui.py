#!/usr/bin/env python3
from pathlib import Path
import re
import shutil
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source")
patch_root = Path(__file__).resolve().parent
assets = root / "Assets"

inter_path = assets / "SibylSystem" / "InterString.cs"
uihelper_path = assets / "SibylSystem" / "MonoHelpers" / "UIHelper.cs"
servant_path = assets / "SibylSystem" / "Servant.cs"
translation_source = patch_root / "localization" / "ja-JP_translation.conf"
streaming = assets / "StreamingAssets"
translation_target = streaming / "koishi-ja-translation.conf"

for required in (inter_path, uihelper_path, servant_path, translation_source):
    if not required.is_file():
        raise SystemExit(f"Japanese UI patch source missing: {required}")

streaming.mkdir(parents=True, exist_ok=True)
shutil.copyfile(translation_source, translation_target)

# InterString: prefer the bundled Japanese dictionary on iOS while keeping the
# original config path as the destination for unknown-string logging.
text = inter_path.read_text(encoding="utf-8-sig")
if "using UnityEngine;" not in text:
    text = text.replace("using System.IO;\n", "using System.IO;\nusing UnityEngine;\n", 1)

if "koishi-ja-translation.conf" not in text:
    pat = re.compile(
        r"(?m)^(?P<indent>\s*)(?:string|var)\s+txtString\s*=\s*File\.ReadAllText\(path\);\s*$"
    )
    m = pat.search(text)
    if not m:
        raise SystemExit("Could not locate InterString translation file read")
    indent = m.group("indent")
    replacement = (
        indent + 'string translationSource = path;\n'
        + indent + 'string bundledJapanese = Path.Combine(Application.streamingAssetsPath, "koishi-ja-translation.conf");\n'
        + indent + 'if (File.Exists(bundledJapanese))\n'
        + indent + '    translationSource = bundledJapanese;\n'
        + indent + 'string txtString = File.ReadAllText(translationSource);'
    )
    text = text[:m.start()] + replacement + text[m.end():]

if "TryGetExisting" not in text:
    marker = "    public static string Get(string original)\n"
    pos = text.find(marker)
    if pos < 0:
        raise SystemExit("Could not locate InterString.Get")
    helper = '''    public static bool TryGetExisting(string original, out string translated)
    {
        translated = original;
        if (translations.TryGetValue(original, out translated))
        {
            translated = translated.Replace("@n", "\\r\\n").Replace("@ui", "");
            return true;
        }
        translated = original;
        return false;
    }

    public static string TranslateUiText(string original)
    {
        string translated;
        if (TryGetExisting(original, out translated))
            return translated;

        // Some fixed-source update labels are generated with a percentage and
        // therefore cannot be represented by an exact translation.conf key.
        if (!String.IsNullOrEmpty(original) && original.StartsWith("正在") && original.Contains("%"))
        {
            int percent = original.LastIndexOf('%');
            int start = percent - 1;
            while (start >= 0 && Char.IsDigit(original[start]))
                --start;
            ++start;
            if (start < percent)
                return "処理中…" + original.Substring(start, percent - start + 1);
            return "処理中…";
        }

        return original;
    }

'''
    text = text[:pos] + helper + text[pos:]

inter_path.write_text(text, encoding="utf-8")

# UIHelper: translate every label when a translation already exists. Unknown
# ordinary labels are left untouched; the legacy !/yes/no behavior still logs
# missing strings through InterString.Get.
text = uihelper_path.read_text(encoding="utf-8-sig")
start = text.find("    public static void InterGameObject(GameObject father)")
end = text.find("    public static GameObject getByName(GameObject father, string name)", start)
if start < 0 or end < 0:
    raise SystemExit("Could not locate UIHelper.InterGameObject")
new_inter = '''    public static void InterGameObject(GameObject father)
    {
        if (father == null)
            return;

        var all = father.transform.GetComponentsInChildren<UILabel>();
        for (int i = 0; i < all.Length; i++)
        {
            string original = all[i].text;
            string translated = InterString.TranslateUiText(original);
            if (translated != original)
            {
                all[i].text = translated;
            }
            else if ((all[i].name.Length > 1 && all[i].name[0] == '!')
                || all[i].name == "yes_" || all[i].name == "no_")
            {
                all[i].text = InterString.Get(original);
            }
        }
    }

'''
text = text[:start] + new_inter + text[end:]

# Dynamically assigned labels should also use a known Japanese mapping.
method_start = text.find("    public static void trySetLableText(GameObject father, string name, string text)")
if method_start >= 0:
    method_end = text.find("    public static string tryGetLableText", method_start)
    if method_end < 0:
        raise SystemExit("Could not locate end of UIHelper.trySetLableText")
    block = text[method_start:method_end]
    old = "            l.text = text;"
    if old in block and "TranslateUiText(text)" not in block:
        block = block.replace(old, "            l.text = InterString.TranslateUiText(text);", 1)
        text = text[:method_start] + block + text[method_end:]

uihelper_path.write_text(text, encoding="utf-8")

# Re-run the UI translation pass after a servant becomes visible. This catches
# labels whose C# code sets text after the prefab was instantiated.
text = servant_path.read_text(encoding="utf-8-sig")
if "Koishi iOS Japanese UI pass" not in text:
    old = '''        if (isShowed)
        {
            applyShowArrangement();
        }
        else
        {
            applyHideArrangement();
        }'''
    new = '''        if (isShowed)
        {
            applyShowArrangement();
            // Koishi iOS Japanese UI pass: translate labels that were assigned
            // after the prefab's initial InterGameObject call.
            if (gameObject != null)
                UIHelper.InterGameObject(gameObject);
        }
        else
        {
            applyHideArrangement();
        }'''
    if old not in text:
        raise SystemExit("Could not locate Servant.fixScreenProblem layout branch")
    text = text.replace(old, new, 1)

servant_path.write_text(text, encoding="utf-8")

for needle, path in (
    ("koishi-ja-translation.conf", inter_path),
    ("TryGetExisting", inter_path),
    ("TranslateUiText", inter_path),
    ("InterString.TranslateUiText(original)", uihelper_path),
    ("InterString.TranslateUiText(text)", uihelper_path),
    ("Koishi iOS Japanese UI pass", servant_path),
):
    if needle not in path.read_text(encoding="utf-8"):
        raise SystemExit(f"Japanese UI verification failed: {needle} in {path}")

if not translation_target.is_file() or translation_target.stat().st_size == 0:
    raise SystemExit("Bundled Japanese translation file was not created")

print("Prepared Japanese UI localization:")
print(f"  - bundled dictionary: {translation_target}")
print(f"  - runtime dictionary override: {inter_path}")
print(f"  - all-known-label translation: {uihelper_path}")
print(f"  - post-layout translation pass: {servant_path}")
