using UnityEngine;

// Managed marker retained so the build can verify that the viewport feature
// reaches IL2CPP. The actual notch-safe containment is performed below Unity,
// in the generated iOS view hierarchy, where the Unity view itself is placed
// inside a centred 16:9 black container. No camera.rect adjustment is applied
// here, avoiding double letterboxing or NGUI coordinate mismatches.
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
        Debug.Log("[IPhone16x9Viewport] Native centred 16:9 iOS container enabled.");
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
