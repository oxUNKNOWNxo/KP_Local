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

# iPhone X full-screen mode is enabled by the modern iPhone launch storyboard.
# For this TrollStore build we intentionally omit the iPhone launch storyboard
# and provide legacy launch images only through the iPhone 8/8 Plus generation.
# iOS then uses its compatibility/letterbox mode on notched iPhones, keeping the
# entire Unity surface and touch coordinate system out of the notch/home-indicator
# region. Keep the iPad-specific storyboard untouched.
with plist_path.open("rb") as f:
    plist = plistlib.load(f)

for key in (
    "UILaunchStoryboardName",
    "UILaunchStoryboardName~iphone",
    "UILaunchStoryboardName~ipod",
    "UILaunchScreen",
    "UILaunchScreens",
):
    plist.pop(key, None)

with plist_path.open("wb") as f:
    plistlib.dump(plist, f, fmt=plistlib.FMT_XML, sort_keys=False)

catalogs = sorted(root.rglob("*.xcassets"))
if not catalogs:
    raise SystemExit("No Xcode asset catalog (*.xcassets) found")

# Prefer the catalog containing the app icon, because it is already part of the
# Unity-iPhone target's Resources build phase.
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
    # Opaque black RGBA launch image. Uniform scanlines compress very small.
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
    # Deliberately stop at pre-notch iPhones. Do NOT add 2436h/812h images.
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

print(f"Prepared iPhone notch compatibility launch assets: {launch_set}")
print("Removed iPhone modern launch storyboard keys from Info.plist")
print("No iPhone X/2436h launch image is intentionally provided")
