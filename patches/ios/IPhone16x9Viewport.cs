using UnityEngine;

// Managed marker retained so the build can verify that the viewport feature
// reaches IL2CPP. iPhone X compatibility sizing is selected by the legacy
// launch-image configuration before Unity starts. The native wrapper remains
// as a defensive fallback, but no runtime resolution switch is made:
// changing the render resolution after scene load can stall startup and does
// not change the iOS window compatibility mode.
public sealed class IPhone16x9Viewport : MonoBehaviour
{
    private static IPhone16x9Viewport instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallFromUnityRuntime()
    {
        EnsureInstalled();
    }

    public static void EnsureInstalled()
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (instance != null)
        {
            return;
        }

        GameObject host = new GameObject("IPhone16x9Viewport");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<IPhone16x9Viewport>();
        Debug.Log("[IPhone16x9Viewport] Legacy 16:9 iPhone compatibility framing enabled.");
#endif
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
