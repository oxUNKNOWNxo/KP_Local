using UnityEngine;

// Keeps KoishiPro2's camera-rendered UI/gameplay inside a centred 16:9 area on
// extra-wide iPhones. The installer is also called explicitly from Program.cs;
// RuntimeInitializeOnLoadMethod is retained only as a fallback.
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
    private static void InstallFallback()
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

        IPhone16x9Viewport[] existing = Resources.FindObjectsOfTypeAll<IPhone16x9Viewport>();
        if (existing != null && existing.Length > 0 && existing[0] != null)
        {
            instance = existing[0];
            return;
        }

        GameObject host = new GameObject("IPhone16x9Viewport");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<IPhone16x9Viewport>();
        Debug.Log("[IPhone16x9Viewport] Explicit runtime viewport installed.");
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
        CreateBlackBackgroundCamera();
        ApplyViewport(true);
    }

    private void LateUpdate()
    {
        ApplyViewport(false);
    }

    private void CreateBlackBackgroundCamera()
    {
        if (blackCamera != null)
        {
            return;
        }

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

        if (force || geometryChanged)
        {
            Debug.Log("[IPhone16x9Viewport] Screen=" + width + "x" + height
                + " rect=" + contentRect.x + "," + contentRect.y + ","
                + contentRect.width + "," + contentRect.height
                + " cameras=" + cameras.Length);
        }

        lastWidth = width;
        lastHeight = height;
        lastContentRect = contentRect;
    }
}
