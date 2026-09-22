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

# Keep iOS in its normal modern full-screen launch mode.  The previous attempt
# intentionally omitted modern launch metadata to trigger old iPhone
# compatibility mode, but iOS 16 on iPhone X did not letterbox KoishiPro2 and
# the launch transition felt slower.  UILaunchScreen is Apple's storyboard-free
# modern launch-screen key.  Runtime 16:9 containment is now handled inside
# Unity by IPhone16x9Viewport instead of relying on iOS compatibility behavior.
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

# The Xcode command line still names LaunchImage for compatibility with the
# existing Unity project.  Keep a tiny, deterministic pre-notch launch-image set
# as a fallback build resource; UILaunchScreen is what modern iOS uses at run
# time, so these images no longer control iPhone X display mode.
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
    {
        "filename": "Default@2x.png",
        "size": (640, 960),
        "meta": {
            "orientation": "portrait",
            "idiom": "iphone",
            "extent": "full-screen",
            "minimum-system-version": "7.0",
            "scale": "2x",
        },
    },
    {
        "filename": "Default-568h@2x.png",
        "size": (640, 1136),
        "meta": {
            "orientation": "portrait",
            "idiom": "iphone",
            "extent": "full-screen",
            "subtype": "retina4",
            "minimum-system-version": "7.0",
            "scale": "2x",
        },
    },
    {
        "filename": "Default-667h@2x.png",
        "size": (750, 1334),
        "meta": {
            "orientation": "portrait",
            "idiom": "iphone",
            "extent": "full-screen",
            "subtype": "667h",
            "minimum-system-version": "8.0",
            "scale": "2x",
        },
    },
    {
        "filename": "Default-736h@3x.png",
        "size": (1242, 2208),
        "meta": {
            "orientation": "portrait",
            "idiom": "iphone",
            "extent": "full-screen",
            "subtype": "736h",
            "minimum-system-version": "8.0",
            "scale": "3x",
        },
    },
    {
        "filename": "Default-736h-Landscape@3x.png",
        "size": (2208, 1242),
        "meta": {
            "orientation": "landscape",
            "idiom": "iphone",
            "extent": "full-screen",
            "subtype": "736h",
            "minimum-system-version": "8.0",
            "scale": "3x",
        },
    },
]

contents = {"images": [], "info": {"version": 1, "author": "xcode"}}
for item in images:
    write_black_png(launch_set / item["filename"], *item["size"])
    entry = dict(item["meta"])
    entry["filename"] = item["filename"]
    contents["images"].append(entry)

(launch_set / "Contents.json").write_text(
    json.dumps(contents, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
)

print(f"Prepared fallback launch assets: {launch_set}")
print("Configured modern storyboard-free UILaunchScreen for iPhone")
print("Runtime notch containment is handled by IPhone16x9Viewport")
