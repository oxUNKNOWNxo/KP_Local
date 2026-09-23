#!/usr/bin/env python3
from pathlib import Path
import json
import plistlib
import shutil
import struct
import sys
import zlib

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source/output")
plist_path = root / "Info.plist"
if not plist_path.is_file():
    raise SystemExit(f"Info.plist not found: {plist_path}")

with plist_path.open("rb") as f:
    plist = plistlib.load(f)

for key in (
    "UILaunchStoryboardName",
    "UILaunchStoryboardName~iphone",
    "UILaunchStoryboardName~ipod",
    "UILaunchScreens",
):
    plist.pop(key, None)
plist["UILaunchScreen"] = {}

with plist_path.open("wb") as f:
    plistlib.dump(plist, f, fmt=plistlib.FMT_XML, sort_keys=False)

# Primary iPhone X/notch fix: do not let Unity's root view fill the whole
# physical screen on extra-wide iPhones.  Wrap it in a black full-screen
# container and center the Unity rendering view at 16:9.  This works below the
# Unity camera/UI layer, so NGUI cannot stretch back into the notch area.
view_candidates = [
    root / "Classes" / "UI" / "UnityAppController+ViewHandling.mm",
    root / "Classes" / "UnityAppController+ViewHandling.mm",
]
view_path = next((p for p in view_candidates if p.is_file()), None)
if view_path is None:
    found = list(root.rglob("UnityAppController+ViewHandling.mm"))
    if len(found) == 1:
        view_path = found[0]
if view_path is None:
    raise SystemExit("UnityAppController+ViewHandling.mm not found")

text = view_path.read_text(encoding="utf-8")
old = "    _rootController.view = _rootView = _unityView;"
if old not in text:
    raise SystemExit("Could not locate Unity root-view assignment")

replacement = r'''    // KoishiPro2: keep the actual Unity view inside a centred 16:9 area on
    // extra-wide iPhones. The full-screen container stays black.
    CGRect koishiBounds = [UIScreen mainScreen].bounds;
    CGFloat koishiW = CGRectGetWidth(koishiBounds);
    CGFloat koishiH = CGRectGetHeight(koishiBounds);
    CGFloat koishiLong = MAX(koishiW, koishiH);
    CGFloat koishiShort = MIN(koishiW, koishiH);
    BOOL koishiWidePhone = (UI_USER_INTERFACE_IDIOM() == UIUserInterfaceIdiomPhone)
        && koishiShort > 0.0
        && (koishiLong / koishiShort) >= 1.95;

    if (koishiWidePhone)
    {
        UIView* koishiContainer = [[UIView alloc] initWithFrame: koishiBounds];
        koishiContainer.backgroundColor = [UIColor blackColor];
        koishiContainer.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;

        CGRect koishiFrame = koishiBounds;
        const CGFloat koishiAspect = 16.0 / 9.0;
        if (koishiW >= koishiH)
        {
            CGFloat targetW = koishiH * koishiAspect;
            koishiFrame.origin.x = (koishiW - targetW) * 0.5;
            koishiFrame.origin.y = 0.0;
            koishiFrame.size.width = targetW;
            koishiFrame.size.height = koishiH;
        }
        else
        {
            CGFloat targetH = koishiW * koishiAspect;
            koishiFrame.origin.x = 0.0;
            koishiFrame.origin.y = (koishiH - targetH) * 0.5;
            koishiFrame.size.width = koishiW;
            koishiFrame.size.height = targetH;
        }

        _unityView.autoresizingMask = UIViewAutoresizingNone;
        _unityView.frame = koishiFrame;
        [koishiContainer addSubview: _unityView];
        _rootView = koishiContainer;
        _rootController.view = koishiContainer;
        _window.backgroundColor = [UIColor blackColor];
    }
    else
    {
        _rootController.view = _rootView = _unityView;
    }'''

if "koishiWidePhone" not in text:
    text = text.replace(old, replacement, 1)
    view_path.write_text(text, encoding="utf-8")

# Keep deterministic legacy launch images as build-time fallback resources.
catalogs = sorted(root.rglob("*.xcassets"))
if not catalogs:
    raise SystemExit("No Xcode asset catalog (*.xcassets) found")

catalog = None
for candidate in catalogs:
    if any(candidate.glob("*.appiconset")):
        catalog = candidate
        break
if catalog is None:
    catalog = catalogs[0]

launch_set = catalog / "LaunchImage.launchimage"
if launch_set.exists():
    shutil.rmtree(launch_set)
launch_set.mkdir(parents=True)


def png_chunk(kind: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)


def write_black_png(path: Path, width: int, height: int) -> None:
    signature = b"\x89PNG\r\n\x1a\n"
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    row = b"\x00" + (b"\x00\x00\x00\xff" * width)
    raw = row * height
    data = zlib.compress(raw, 9)
    path.write_bytes(signature + png_chunk(b"IHDR", ihdr) + png_chunk(b"IDAT", data) + png_chunk(b"IEND", b""))

images = [
    ("Default@2x.png", 640, 960, {"orientation":"portrait","idiom":"iphone","extent":"full-screen","minimum-system-version":"7.0","scale":"2x"}),
    ("Default-568h@2x.png", 640, 1136, {"orientation":"portrait","idiom":"iphone","extent":"full-screen","subtype":"retina4","minimum-system-version":"7.0","scale":"2x"}),
    ("Default-667h@2x.png", 750, 1334, {"orientation":"portrait","idiom":"iphone","extent":"full-screen","subtype":"667h","minimum-system-version":"8.0","scale":"2x"}),
    ("Default-736h@3x.png", 1242, 2208, {"orientation":"portrait","idiom":"iphone","extent":"full-screen","subtype":"736h","minimum-system-version":"8.0","scale":"3x"}),
    ("Default-736h-Landscape@3x.png", 2208, 1242, {"orientation":"landscape","idiom":"iphone","extent":"full-screen","subtype":"736h","minimum-system-version":"8.0","scale":"3x"}),
]

contents = {"images": [], "info": {"version": 1, "author": "xcode"}}
for filename, width, height, meta in images:
    write_black_png(launch_set / filename, width, height)
    entry = dict(meta)
    entry["filename"] = filename
    contents["images"].append(entry)

(launch_set / "Contents.json").write_text(json.dumps(contents, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

print(f"Prepared fallback launch assets: {launch_set}")
print(f"Patched native Unity root view for centred 16:9: {view_path}")
