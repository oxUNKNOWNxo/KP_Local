using UnityEngine;

// Runtime fallback for extra-wide iPhones plus a small menu-layout repair.
// The primary notch fix is applied in generated native iOS UI code; camera.rect
// remains here only as a secondary containment path.
public sealed class IPhone16x9Viewport : MonoBehaviour
{
    private const float TargetAspect = 16f / 9f;
    private const float WideDeviceThreshold = 1.95f;

    private static IPhone16x9Viewport instance;
    private Camera blackCamera;
    private Rect lastContentRect = new Rect(0f, 0f, 1f, 1f);
    private int lastWidth;
    private int lastHeight;
    private bool aiMenuPositionFixed;

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
        Debug.Log("[IPhone16x9Viewport] Installed runtime layout controller.");
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
        FixAiMenuLayout();
    }

    private void FixAiMenuLayout()
    {
        if (aiMenuPositionFixed)
        {
            return;
        }

        GameObject ai = GameObject.Find("ai_");
        GameObject myCard = GameObject.Find("mycard_");
        if (ai == null || myCard == null)
        {
            return;
        }

        Transform aiTransform = ai.transform;
        Transform myCardTransform = myCard.transform;
        const float gap = 80f;

        if (aiTransform.parent == myCardTransform.parent)
        {
            aiTransform.localPosition = myCardTransform.localPosition + new Vector3(0f, -gap, 0f);
        }
        else if (aiTransform.parent != null)
        {
            Vector3 world = myCardTransform.TransformPoint(Vector3.zero);
            Vector3 local = aiTransform.parent.InverseTransformPoint(world);
            aiTransform.localPosition = local + new Vector3(0f, -gap, 0f);
        }
        else
        {
            aiTransform.position = myCardTransform.position + new Vector3(0f, -gap, 0f);
        }

        aiMenuPositionFixed = true;
        Debug.Log("[OfflineAI] Positioned AI menu below My Card.");
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

        lastWidth = width;
        lastHeight = height;
        lastContentRect = contentRect;
    }
}
