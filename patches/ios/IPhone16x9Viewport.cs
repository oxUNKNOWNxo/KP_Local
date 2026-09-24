using UnityEngine;

// Managed marker retained so the build can verify that the viewport feature
// reaches IL2CPP. The native iOS wrapper now exposes the full iPhone X-class
// landscape window and reserves only the physical notch side. This class does
// not change Unity's render resolution at runtime; the native container updates its frame during
// safe-area/orientation changes.
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
        Debug.Log("[IPhone16x9Viewport] Native notch-aware maximum-width framing enabled.");
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
