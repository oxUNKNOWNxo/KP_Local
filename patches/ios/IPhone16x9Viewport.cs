using UnityEngine;

// Keeps KoishiPro2's camera-rendered UI/gameplay inside a centred 16:9 area on
// extra-wide iPhones.  The unused left/right area is cleared to black, so the
// notch and home-indicator region never overlap the game surface.
public sealed class IPhone16x9Viewport : MonoBehaviour
{
    private const float TargetAspect = 16f / 9f;
    private const float WideDeviceThreshold = 1.95f;

    private static IPhone16x9Viewport instance;
    private Camera blackCamera;
    private Rect lastContentRect = new Rect(0f, 0f, 1f, 1f);
    private int lastWidth;
    private int lastHeight;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (instance != null)
        {
            return;
        }

        GameObject host = new GameObject("IPhone16x9Viewport");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<IPhone16x9Viewport>();
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
        CreateBlackBackgroundCamera();
        ApplyViewport(true);
    }

    private void LateUpdate()
    {
        ApplyViewport(false);
    }

    private void CreateBlackBackgroundCamera()
    {
        blackCamera = gameObject.AddComponent<Camera>();
        blackCamera.clearFlags = CameraClearFlags.SolidColor;
        blackCamera.backgroundColor = Color.black;
        blackCamera.cullingMask = 0;
        blackCamera.depth = -10000f;
        blackCamera.rect = new Rect(0f, 0f, 1f, 1f);
        blackCamera.allowHDR = false;
        blackCamera.allowMSAA = false;
    }

    private static bool Approximately(Rect a, Rect b)
    {
        const float epsilon = 0.0005f;
        return Mathf.Abs(a.x - b.x) < epsilon
            && Mathf.Abs(a.y - b.y) < epsilon
            && Mathf.Abs(a.width - b.width) < epsilon
            && Mathf.Abs(a.height - b.height) < epsilon;
    }

    private Rect CalculateContentRect()
    {
        float width = Screen.width;
        float height = Screen.height;
        if (width <= 0f || height <= 0f)
        {
            return new Rect(0f, 0f, 1f, 1f);
        }

        float longSide = Mathf.Max(width, height);
        float shortSide = Mathf.Min(width, height);
        if (longSide / shortSide < WideDeviceThreshold)
        {
            return new Rect(0f, 0f, 1f, 1f);
        }

        if (width >= height)
        {
            float normalizedWidth = (height * TargetAspect) / width;
            return new Rect((1f - normalizedWidth) * 0.5f, 0f, normalizedWidth, 1f);
        }

        float normalizedHeight = (width * TargetAspect) / height;
        return new Rect(0f, (1f - normalizedHeight) * 0.5f, 1f, normalizedHeight);
    }

    private void ApplyViewport(bool force)
    {
        int width = Screen.width;
        int height = Screen.height;
        Rect contentRect = CalculateContentRect();
        bool geometryChanged = width != lastWidth || height != lastHeight || !Approximately(contentRect, lastContentRect);

        Camera[] cameras = Camera.allCameras;
        Rect full = new Rect(0f, 0f, 1f, 1f);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null || cam == blackCamera || cam.targetTexture != null)
            {
                continue;
            }

            // KoishiPro2's gameplay/NGUI cameras are full-screen cameras.  Do
            // not disturb deliberately smaller viewports such as previews.
            if (force || geometryChanged || Approximately(cam.rect, full) || Approximately(cam.rect, lastContentRect))
            {
                if (Approximately(cam.rect, full) || Approximately(cam.rect, lastContentRect))
                {
                    cam.rect = contentRect;
                }
            }
        }

        if (blackCamera != null)
        {
            blackCamera.rect = full;
        }

        lastWidth = width;
        lastHeight = height;
        lastContentRect = contentRect;
    }
}
