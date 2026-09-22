using UnityEngine;

// Retained as an IL2CPP/build marker for compatibility with the existing build
// validation. Camera.rect did not constrain KoishiPro2's NGUI layout on device,
// so actual iPhone X containment is now applied to the native Unity UIView in
// prepare_notch_compat.py after Xcode export.
public static class IPhone16x9Viewport
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallAfterSceneLoad()
    {
        // Intentionally empty. Native Unity UIView framing owns 16:9 now.
    }

    public static void EnsureInstalled()
    {
        // Intentionally empty. Kept so Program startup remains source-compatible.
    }
}
