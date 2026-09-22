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

# Keep iOS in modern full-screen launch mode.  Runtime notch containment is
# handled by the native Unity UIView patch; launch metadata should not be used
# to force legacy iPhone compatibility mode.
with plist_path.open("rb") as f:
    plist = plistlib.load(f)

for key in (
    "UILaunchStoryboardName",
    "UILaunchStoryboardName~iphone",
    "UILaunchStoryboardName~ipod",
    "UILaunchScreens",
):
    plist.pop(key, None)

# An empty UILaunchScreen uses systemBackground, which is visibly white in
# Light Mode. Use an explicit named black color so the transition into Unity is
# black rather than the brief white flash seen on the previous build.
plist["UILaunchScreen"] = {"UIColorName": "KoishiLaunchBlack"}

with plist_path.open("wb") as f:
    plistlib.dump(plist, f, fmt=plistlib.FMT_XML, sort_keys=False)

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

# Named color used by UILaunchScreen.
color_set = catalog / "KoishiLaunchBlack.colorset"
if color_set.exists():
    shutil.rmtree(color_set)
color_set.mkdir(parents=True)
(color_set / "Contents.json").write_text(
    json.dumps(
        {
            "colors": [
                {
                    "idiom": "universal",
                    "color": {
                        "color-space": "srgb",
                        "components": {
                            "red": "0.000",
                            "green": "0.000",
                            "blue": "0.000",
                            "alpha": "1.000",
                        },
                    },
                }
            ],
            "info": {"version": 1, "author": "xcode"},
        },
        ensure_ascii=False,
        indent=2,
    ) + "\n",
    encoding="utf-8",
)

# Keep the existing build command's LaunchImage fallback deterministic and
# black. Modern iOS uses UILaunchScreen; these images are not used for notch
# containment.
launch_set = catalog / "LaunchImage.launchimage"
if launch_set.exists():
    shutil.rmtree(launch_set)
launch_set.mkdir(parents=True)


def png_chunk(kind: bytes, data: bytes) -> bytes:
    return (
        struct.pack(">I", len(data))
        + kind
        + data
        + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    )


def write_black_png(path: Path, width: int, height: int) -> None:
    signature = b"\x89PNG\r\n\x1a\n"
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    row = b"\x00" + (b"\x00\x00\x00\xff" * width)
    raw = row * height
    data = zlib.compress(raw, 9)
    path.write_bytes(
        signature
        + png_chunk(b"IHDR", ihdr)
        + png_chunk(b"IDAT", data)
        + png_chunk(b"IEND", b"")
    )


images = [
    ("Default@2x.png", (640, 960), {"orientation": "portrait", "idiom": "iphone", "extent": "full-screen", "minimum-system-version": "7.0", "scale": "2x"}),
    ("Default-568h@2x.png", (640, 1136), {"orientation": "portrait", "idiom": "iphone", "extent": "full-screen", "subtype": "retina4", "minimum-system-version": "7.0", "scale": "2x"}),
    ("Default-667h@2x.png", (750, 1334), {"orientation": "portrait", "idiom": "iphone", "extent": "full-screen", "subtype": "667h", "minimum-system-version": "8.0", "scale": "2x"}),
    ("Default-736h@3x.png", (1242, 2208), {"orientation": "portrait", "idiom": "iphone", "extent": "full-screen", "subtype": "736h", "minimum-system-version": "8.0", "scale": "3x"}),
    ("Default-736h-Landscape@3x.png", (2208, 1242), {"orientation": "landscape", "idiom": "iphone", "extent": "full-screen", "subtype": "736h", "minimum-system-version": "8.0", "scale": "3x"}),
]

contents = {"images": [], "info": {"version": 1, "author": "xcode"}}
for filename, size, meta in images:
    write_black_png(launch_set / filename, *size)
    entry = dict(meta)
    entry["filename"] = filename
    contents["images"].append(entry)

(launch_set / "Contents.json").write_text(
    json.dumps(contents, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
)

print(f"Prepared black fallback launch assets: {launch_set}")
print(f"Prepared black UILaunchScreen named color: {color_set}")
print("Runtime notch containment is handled by native KoishiViewport.mm")
