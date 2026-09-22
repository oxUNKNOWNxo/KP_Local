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

# Keep the normal modern iOS launch path. The actual notch fix is applied below
# to Unity's native UIView, not to Unity cameras or launch-screen compatibility.
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

# ---------------------------------------------------------------------------
# Native 16:9 containment.
#
# Camera.rect did not affect KoishiPro2's NGUI layout on iPhone X. Instead,
# make UnityView itself a centered 16:9 child of a black full-screen container.
# UIKit then maps touches to the same constrained UnityView bounds as rendering.
# Only extra-wide screens (long/short >= 1.95) are constrained, so ordinary
# 16:9 devices keep their native full-screen layout.
# ---------------------------------------------------------------------------
view_files = list(root.rglob("UnityAppController+ViewHandling.mm"))
if len(view_files) != 1:
    raise SystemExit(
        "Expected exactly one UnityAppController+ViewHandling.mm, found "
        + str(len(view_files))
        + ": "
        + str(view_files)
    )

view_path = view_files[0]
view_text = view_path.read_text(encoding="utf-8")

native_marker = "Koishi16x9ContainerView"
if native_marker not in view_text:
    implementation_anchor = "@implementation UnityAppController\n"
    if implementation_anchor not in view_text:
        raise SystemExit("Could not locate UnityAppController implementation anchor")

    helper = r'''@interface Koishi16x9ContainerView : UIView
{
    UIView* _koishiUnityContentView;
}
- (instancetype)initWithFrame:(CGRect)frame contentView:(UIView*)contentView;
@end

@implementation Koishi16x9ContainerView

- (instancetype)initWithFrame:(CGRect)frame contentView:(UIView*)contentView
{
    self = [super initWithFrame:frame];
    if (self)
    {
        _koishiUnityContentView = contentView;
        self.backgroundColor = [UIColor blackColor];
        self.clipsToBounds = YES;
        [self addSubview:contentView];
    }
    return self;
}

- (void)layoutSubviews
{
    [super layoutSubviews];

    UIView* content = _koishiUnityContentView;
    if (content == nil)
        return;

    CGRect bounds = self.bounds;
    CGFloat width = CGRectGetWidth(bounds);
    CGFloat height = CGRectGetHeight(bounds);
    CGFloat longSide = MAX(width, height);
    CGFloat shortSide = MIN(width, height);
    CGRect contentFrame = bounds;

    if (shortSide > 0.0 && (longSide / shortSide) >= 1.95)
    {
        if (width >= height)
        {
            CGFloat targetWidth = height * (16.0 / 9.0);
            contentFrame = CGRectMake((width - targetWidth) * 0.5, 0.0, targetWidth, height);
        }
        else
        {
            CGFloat targetHeight = width * (16.0 / 9.0);
            contentFrame = CGRectMake(0.0, (height - targetHeight) * 0.5, width, targetHeight);
        }
    }

    content.frame = CGRectIntegral(contentFrame);
}

@end

'''
    view_text = view_text.replace(implementation_anchor, helper + implementation_anchor, 1)

old_root = '''    _unityView.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;

    _rootController.view = _rootView = _unityView;'''
new_root = '''    // KoishiPro2's UI predates notched iPhones. Keep the actual Unity render
    // and touch surface inside a centered 16:9 child view on extra-wide phones.
    _unityView.autoresizingMask = UIViewAutoresizingNone;
    CGRect koishiRootFrame = [UIScreen mainScreen].bounds;
    Koishi16x9ContainerView* koishiRoot = [[Koishi16x9ContainerView alloc]
        initWithFrame:koishiRootFrame
        contentView:_unityView];
    koishiRoot.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
    [koishiRoot setNeedsLayout];
    [koishiRoot layoutIfNeeded];

    _rootController.view = _rootView = koishiRoot;'''

if old_root not in view_text:
    if new_root not in view_text:
        raise SystemExit("Could not locate UnityView root assignment for native 16:9 patch")
else:
    view_text = view_text.replace(old_root, new_root, 1)

if native_marker not in view_text or "_rootController.view = _rootView = koishiRoot;" not in view_text:
    raise SystemExit("Native 16:9 UnityView patch validation failed")

view_path.write_text(view_text, encoding="utf-8")
print(f"Patched native Unity view for centred 16:9 containment: {view_path}")

# Keep a deterministic legacy LaunchImage set as a harmless fallback resource.
# Modern iOS uses UILaunchScreen above; these assets do not control the runtime
# viewport anymore.
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
print("Runtime notch containment is handled by Koishi16x9ContainerView")
