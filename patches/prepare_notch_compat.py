#!/usr/bin/env python3
from pathlib import Path
import plistlib
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "source/output")
plist_path = root / "Info.plist"
if not plist_path.is_file():
    raise SystemExit(f"Info.plist not found: {plist_path}")

with plist_path.open("rb") as f:
    plist = plistlib.load(f)

# Use the modern iPhone launch storyboard so iOS exposes the full physical
# iPhone X-class window instead of placing the app in legacy 16:9
# compatibility mode. The actual playable viewport is constrained below by a
# native notch-aware container.
iphone_storyboards = [
    p for p in root.rglob("*.storyboard")
    if "launchscreen" in p.name.lower() and "ipad" not in p.name.lower()
]
if not iphone_storyboards:
    raise SystemExit("No iPhone launch storyboard found in exported Xcode project")
launch_storyboard = sorted(iphone_storyboards)[0]
launch_name = launch_storyboard.stem
plist["UILaunchStoryboardName"] = launch_name
plist["UILaunchStoryboardName~iphone"] = launch_name
plist["UIRequiresFullScreen"] = True

# Do not let a legacy LaunchImage declaration force iPhone X back into the old
# compatibility canvas.
for key in ("UILaunchImages", "UILaunchImages~iphone"):
    plist.pop(key, None)

with plist_path.open("wb") as f:
    plistlib.dump(plist, f, fmt=plistlib.FMT_XML, sort_keys=False)

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
if old not in text and "KoishiNotchAwareContainerView* koishiContainer" not in text:
    raise SystemExit("Could not locate Unity root-view assignment")

container_class = r'''
@interface KoishiNotchAwareContainerView : UIView
@end

@implementation KoishiNotchAwareContainerView

- (UIInterfaceOrientation)koishiInterfaceOrientation
{
    if (@available(iOS 13.0, *))
    {
        UIWindowScene* scene = self.window.windowScene;
        if (scene != nil)
            return scene.interfaceOrientation;
    }
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    return UIApplication.sharedApplication.statusBarOrientation;
#pragma clang diagnostic pop
}

- (void)koishiApplyViewport
{
    UIView* content = self.subviews.firstObject;
    if (content == nil)
        return;

    CGRect bounds = self.bounds;
    CGFloat width = CGRectGetWidth(bounds);
    CGFloat height = CGRectGetHeight(bounds);
    if (width <= 0.0 || height <= 0.0)
        return;

    CGFloat longSide = MAX(width, height);
    CGFloat shortSide = MIN(width, height);
    BOOL widePhone = (UI_USER_INTERFACE_IDIOM() == UIUserInterfaceIdiomPhone)
        && shortSide > 0.0
        && (longSide / shortSide) >= 1.95;

    if (!widePhone || width < height)
    {
        content.frame = CGRectIntegral(bounds);
        return;
    }

    UIEdgeInsets safe = self.safeAreaInsets;
    CGFloat cutout = MAX(safe.left, safe.right);

    // On some iOS versions the first layout pass reports zero insets. The
    // iPhone-X-class aspect ratio is enough to identify this fallback case.
    // 44 pt is the original iPhone X landscape notch inset; later devices
    // report their larger value through safeAreaInsets on the next pass.
    if (cutout < 1.0)
        cutout = 44.0;

    // safeAreaInsets intentionally includes extra breathing room beyond the
    // physical iPhone X notch. KoishiPro2 is already rendering edge-to-edge,
    // so trim that conservative padding and keep only a small hardware margin.
    // On iPhone X this turns the usual 44 pt safe inset into roughly the
    // physical notch depth instead of leaving the visible extra strip.
    cutout = MAX(30.0, cutout - 12.0);

    // Keep only the cutout side reserved. The opposite edge is intentionally
    // used all the way to the physical screen edge, giving KoishiPro2 a
    // substantially wider canvas than the old centred 16:9 letterbox.
    cutout = MIN(cutout, width * 0.18);
    CGRect frame = bounds;
    frame.size.width = MAX(1.0, width - cutout);

    UIInterfaceOrientation orientation = [self koishiInterfaceOrientation];
    if (orientation == UIInterfaceOrientationLandscapeLeft)
    {
        // Unity/iOS reports the opposite landscape label from the physical
        // notch side observed on this KoishiPro2 build. Reserve the right
        // edge for LandscapeLeft.
        frame.origin.x = 0.0;
    }
    else if (orientation == UIInterfaceOrientationLandscapeRight)
    {
        // Reserve the left edge for LandscapeRight.
        frame.origin.x = cutout;
    }
    else
    {
        // Orientation can be unknown during the first startup layout. Centre
        // only for that transient frame; a rotation/layout event will move it
        // to the correct side.
        frame.origin.x = cutout * 0.5;
    }

    frame.origin.y = 0.0;
    frame.size.height = height;
    content.frame = CGRectIntegral(frame);
}

- (void)layoutSubviews
{
    [super layoutSubviews];
    [self koishiApplyViewport];
}

- (void)safeAreaInsetsDidChange
{
    [super safeAreaInsetsDidChange];
    [self setNeedsLayout];
}

- (void)didMoveToWindow
{
    [super didMoveToWindow];
    [self setNeedsLayout];
}

@end

'''

if "@interface KoishiNotchAwareContainerView" not in text:
    implementation_marker = "@implementation UnityAppController"
    index = text.find(implementation_marker)
    if index < 0:
        raise SystemExit("Could not locate UnityAppController implementation marker")
    text = text[:index] + container_class + text[index:]

replacement = r'''    // KoishiPro2 iPhone X+: expose the full native landscape window, then
    // reserve only the physical notch side. Rotating the phone changes which
    // side is reserved, intentionally sliding the playable viewport sideways.
    KoishiNotchAwareContainerView* koishiContainer = [[KoishiNotchAwareContainerView alloc] initWithFrame: _window.bounds];
    koishiContainer.backgroundColor = [UIColor blackColor];
    koishiContainer.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
    koishiContainer.clipsToBounds = YES;

    _unityView.autoresizingMask = UIViewAutoresizingNone;
    [koishiContainer addSubview: _unityView];
    _rootView = koishiContainer;
    _rootController.view = koishiContainer;
    _window.backgroundColor = [UIColor blackColor];
    [koishiContainer setNeedsLayout];'''

if "KoishiNotchAwareContainerView* koishiContainer" not in text:
    text = text.replace(old, replacement, 1)

if "@interface KoishiNotchAwareContainerView" not in text or "KoishiNotchAwareContainerView* koishiContainer" not in text:
    raise SystemExit("Native notch-aware viewport patch was not installed")

view_path.write_text(text, encoding="utf-8")

print(f"Enabled native iPhone launch viewport with: {launch_name}")
print(f"Patched notch-aware maximum-width Unity viewport: {view_path}")
print("LandscapeLeft reserves the right edge; LandscapeRight reserves the left edge.")
