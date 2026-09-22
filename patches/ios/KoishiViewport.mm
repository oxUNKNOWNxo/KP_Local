#import <UIKit/UIKit.h>
#import <objc/runtime.h>

extern "C" UIView* UnityGetGLView(void);
extern "C" UIViewController* UnityGetGLViewController(void);
extern "C" UIWindow* UnityGetMainWindow(void);

static void KoishiApplyViewport(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        UIView *unityView = UnityGetGLView();
        UIViewController *controller = UnityGetGLViewController();
        UIWindow *window = UnityGetMainWindow();
        if (unityView == nil || controller == nil || window == nil)
            return;

        UIView *container = unityView.superview ?: controller.view;
        if (container == nil)
            return;

        container.backgroundColor = UIColor.blackColor;
        window.backgroundColor = UIColor.blackColor;

        CGRect bounds = container.bounds;
        CGFloat width = CGRectGetWidth(bounds);
        CGFloat height = CGRectGetHeight(bounds);
        if (width <= 0.0 || height <= 0.0)
            return;

        CGFloat longSide = MAX(width, height);
        CGFloat shortSide = MIN(width, height);
        if (longSide / shortSide < 1.95)
        {
            unityView.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
            unityView.frame = bounds;
            return;
        }

        const CGFloat targetAspect = 16.0 / 9.0;
        CGRect content = bounds;
        if (width >= height)
        {
            CGFloat targetWidth = height * targetAspect;
            content = CGRectMake((width - targetWidth) * 0.5, 0.0, targetWidth, height);
        }
        else
        {
            CGFloat targetHeight = width * targetAspect;
            content = CGRectMake(0.0, (height - targetHeight) * 0.5, width, targetHeight);
        }

        unityView.autoresizingMask = UIViewAutoresizingNone;
        unityView.frame = CGRectIntegral(content);
        unityView.clipsToBounds = YES;
    });
}

@interface KoishiViewportObserver : NSObject
- (void)apply:(NSNotification *)notification;
@end

@implementation KoishiViewportObserver

+ (void)load
{
    dispatch_async(dispatch_get_main_queue(), ^{
        KoishiViewportObserver *observer = [KoishiViewportObserver new];
        objc_setAssociatedObject(UIApplication.sharedApplication, @selector(load), observer, OBJC_ASSOCIATION_RETAIN_NONATOMIC);

        NSNotificationCenter *center = NSNotificationCenter.defaultCenter;
        [center addObserver:observer selector:@selector(apply:) name:UIApplicationDidBecomeActiveNotification object:nil];
        [center addObserver:observer selector:@selector(apply:) name:UIApplicationDidChangeStatusBarOrientationNotification object:nil];
        [center addObserver:observer selector:@selector(apply:) name:UIDeviceOrientationDidChangeNotification object:nil];

        [observer apply:nil];
    });
}

- (void)apply:(NSNotification *)notification
{
    (void)notification;
    KoishiApplyViewport();
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.15 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
        KoishiApplyViewport();
    });
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.75 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
        KoishiApplyViewport();
    });
}

@end
