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

# Primary iPhone X/notch fix. The old Camera.rect approach changed Unity's
# rendering but not the native view/touch surface, so NGUI still occupied the
# notch area. Instead, make the actual Unity view a child of a black container
# whose layoutSubviews always centres it at 16:9 on extra-wide iPhones.
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

container_class = r'''
@interface Koishi16x9ContainerView : UIView
@end

@implementation Koishi16x9ContainerView
- (void)layoutSubviews
{
    [super layoutSubviews];

    UIView* content = self.subviews.firstObject;
    if (content == nil)
        return;

    CGRect bounds = self.bounds;
    CGFloat width = CGRectGetWidth(bounds);
    CGFloat height = CGRectGetHeight(bounds);
    CGFloat longSide = MAX(width, height);
    CGFloat shortSide = MIN(width, height);
    BOOL widePhone = (UI_USER_INTERFACE_IDIOM() == UIUserInterfaceIdiomPhone)
        && shortSide > 0.0
        && (longSide / shortSide) >= 1.95;

    if (!widePhone)
    {
        CGRect targetFrame = bounds;
        BOOL sizeChanged = !CGSizeEqualToSize(content.bounds.size, targetFrame.size);
        content.frame = targetFrame;
        if (sizeChanged)
        {
            [self scheduleUnitySurfaceRefresh:content];
        }
        return;
    }

    const CGFloat targetAspect = 16.0 / 9.0;
    CGRect frame = bounds;
    if (width >= height)
    {
        CGFloat targetWidth = height * targetAspect;
        frame.origin.x = (width - targetWidth) * 0.5;
        frame.origin.y = 0.0;
        frame.size.width = targetWidth;
        frame.size.height = height;
    }
    else
    {
        CGFloat targetHeight = width * targetAspect;
        frame.origin.x = 0.0;
        frame.origin.y = (height - targetHeight) * 0.5;
        frame.size.width = width;
        frame.size.height = targetHeight;
    }

    CGRect targetFrame = CGRectIntegral(frame);
    BOOL sizeChanged = !CGSizeEqualToSize(content.bounds.size, targetFrame.size);
    content.frame = targetFrame;
    if (sizeChanged)
    {
        [self scheduleUnitySurfaceRefresh:content];
    }
}

- (void)scheduleUnitySurfaceRefresh:(UIView*)content
{
    if (content == nil)
        return;

    // Changing UnityView.frame updates its UIKit bounds immediately, but Unity's
    // render surface is refreshed from UnityView.layoutSubviews. Defer that
    // child layout until this parent layout pass has fully returned; forcing it
    // synchronously here caused a real-device startup freeze.
    [content setNeedsLayout];
    dispatch_async(dispatch_get_main_queue(), ^{
        [content layoutIfNeeded];
    });
}
@end

'''

if "@interface Koishi16x9ContainerView" not in text:
    implementation_marker = "@implementation UnityAppController"
    index = text.find(implementation_marker)
    if index < 0:
        raise SystemExit("Could not locate UnityAppController implementation marker")
    text = text[:index] + container_class + text[index:]

replacement = r'''    // KoishiPro2: the physical screen remains a black container while the
    // Unity view itself is kept at centred 16:9. Because the container owns
    // layoutSubviews this is reapplied after any orientation/layout update.
    Koishi16x9ContainerView* koishiContainer = [[Koishi16x9ContainerView alloc] initWithFrame: _window.bounds];
    koishiContainer.backgroundColor = [UIColor blackColor];
    koishiContainer.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;

    _unityView.autoresizingMask = UIViewAutoresizingNone;
    [koishiContainer addSubview: _unityView];
    _rootView = koishiContainer;
    _rootController.view = koishiContainer;
    _window.backgroundColor = [UIColor blackColor];
    [koishiContainer setNeedsLayout];
    [koishiContainer layoutIfNeeded];'''

if "Koishi16x9ContainerView* koishiContainer" not in text:
    text = text.replace(old, replacement, 1)

if "@interface Koishi16x9ContainerView" not in text or "Koishi16x9ContainerView* koishiContainer" not in text:
    raise SystemExit("Native centred 16:9 patch was not installed")
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
print(f"Patched native Unity root view with persistent centred 16:9 container: {view_path}")
