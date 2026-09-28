using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Runtime main-menu layout for the iOS build.
//
// The original prefab gives every menu label a fixed box. Japanese strings can
// be wider than the Simplified Chinese source strings, so NGUI's ShrinkContent
// path makes only the longer items render at a smaller apparent font size.
// Keep one real font size for every row and resize the surrounding menu instead.
public static class MenuResponsiveLayout
{
    const int MinimumMenuFontSize = 20;

    static readonly Dictionary<string, string> MenuTranslations =
        new Dictionary<string, string>()
        {
            { "人机模式", "AI対戦" },
            { "人机对战", "AI対戦" },
            { "联机模式", "オンライン" },
            { "聯機模式", "オンライン" },
            { "观看录像", "リプレイ" },
            { "觀看錄像", "リプレイ" },
            { "编辑卡组", "デッキ編集" },
            { "編輯卡組", "デッキ編集" },
            { "系统设置", "設定" },
            { "系統設置", "設定" },
            { "残局模式", "詰めデュエル" },
            { "殘局模式", "詰めデュエル" },
            { "退出游戏", "ゲーム終了" },
            { "退出遊戲", "ゲーム終了" },
            { "超先行卡", "超先行カード" },
            { "资源下载", "素材DL" },
            { "資源下載", "素材DL" },
            { "资源更新", "リソース更新" },
            { "資源更新", "リソース更新" },
            { "资源下载中", "素材DL中" },
            { "資源下載中", "素材DL中" },
        };
    const float LabelExtraWidth = 8f;
    const float LeftHorizontalPadding = 12f;
    const float RightHorizontalPadding = 22f;
    const float VerticalPadding = 14f;
    const float MinimumHeaderGap = 18f;
    const float IconAspectTolerance = 0.35f;
    const float MaximumIconSize = 64f;
    const float MinimumRowGap = 4f;

    class MenuRow
    {
        public Transform root;
        public UILabel label;
        public float originalTextLeft;
        public float originalRootX;
        public float originalRootY;
    }

    public static void ApplyAndSchedule(GameObject menuRoot)
    {
        Apply(menuRoot);
        if (menuRoot != null && Program.I() != null)
            Program.I().StartCoroutine(RefreshAfterNguiLayout(menuRoot));
    }

    static IEnumerator RefreshAfterNguiLayout(GameObject menuRoot)
    {
        // One frame catches NGUI anchor/layout work. The short timed pass also
        // catches menu labels that are populated immediately after show().
        yield return null;
        Apply(menuRoot);
        yield return new WaitForSeconds(0.05f);
        Apply(menuRoot);
    }

    public static void Apply(GameObject menuRoot)
    {
        if (menuRoot == null || !menuRoot.activeInHierarchy)
            return;

        // Keep localization local to the main menu. Do not route every game UI
        // label through a new dictionary: duel UI has its own size/font rules.
        TranslateMenuLabels(menuRoot);

        Transform space = menuRoot.transform;
        List<MenuRow> rows = CollectRows(menuRoot);
        if (rows.Count == 0)
            return;

        rows.Sort(delegate(MenuRow a, MenuRow b)
        {
            return b.originalRootY.CompareTo(a.originalRootY);
        });

        int fixedFontSize = MinimumMenuFontSize;
        for (int i = 0; i < rows.Count; ++i)
            fixedFontSize = Mathf.Max(fixedFontSize, rows[i].label.fontSize);

        // Preserve the existing icon/text gap, but make every text start from
        // one shared column. This keeps icons and Japanese labels aligned.
        float sharedTextLeft = float.NegativeInfinity;
        float sharedRootX = 0f;
        for (int i = 0; i < rows.Count; ++i)
        {
            sharedTextLeft = Mathf.Max(sharedTextLeft, rows[i].originalTextLeft);
            sharedRootX += rows[i].originalRootX;
        }
        sharedRootX /= rows.Count;

        for (int i = 0; i < rows.Count; ++i)
        {
            ConfigureLabel(rows[i].label, fixedFontSize);
            MoveWidgetLeftInSpace(rows[i].label, rows[i].root, sharedTextLeft);
            AlignLabelToIconCenter(rows[i].root, rows[i].label);
        }

        UIWidget back = FindBackground(menuRoot, rows);
        Bounds backBounds = back == null
            ? GetCombinedRowBounds(rows, space)
            : GetWidgetBoundsInSpace(back, space);
        float panelCenterY = backBounds.center.y;

        // Fit the frame to the actual icon/text bounds instead of keeping the
        // old Chinese-prefab center and expanding symmetrically around it.
        Bounds contentBounds = GetCombinedRowBounds(rows, space);
        float panelLeft = contentBounds.min.x - LeftHorizontalPadding;
        float panelRight = contentBounds.max.x + RightHorizontalPadding;
        float panelCenterX = (panelLeft + panelRight) * 0.5f;
        float panelWidth = Mathf.Max(1f, panelRight - panelLeft);

        // Keep the original row spacing if it was already generous. Increase
        // it only when the fixed font/icon height requires more room.
        float rowHeight = 1f;
        float averagePitch = 0f;
        for (int i = 0; i < rows.Count; ++i)
        {
            Bounds visual = GetVisualBoundsInSpace(rows[i].root, space);
            rowHeight = Mathf.Max(rowHeight, visual.size.y);
            if (i > 0)
                averagePitch += Mathf.Abs(rows[i - 1].originalRootY - rows[i].originalRootY);
        }
        if (rows.Count > 1)
            averagePitch /= rows.Count - 1;

        rowHeight = Mathf.Max(rowHeight, fixedFontSize + 10f);
        float rowGap = Mathf.Max(MinimumRowGap, fixedFontSize * 0.20f);
        float rowPitch = Mathf.Max(rowHeight + rowGap, averagePitch);

        UILabel version = FindVersionLabel(menuRoot);
        float headerHeight = 0f;
        if (version != null && version.gameObject.activeInHierarchy)
            headerHeight = Mathf.Max(1f, GetWidgetBoundsInSpace(version, space).size.y);

        float headerGap = version == null ? 0f : MinimumHeaderGap;
        float rowsHeight =
            rowHeight + Mathf.Max(0, rows.Count - 1) * rowPitch;
        float panelHeight =
            VerticalPadding * 2f + headerHeight + headerGap + rowsHeight;

        if (back != null)
        {
            ClearAnchors(back);
            back.width = Mathf.Max(1, Mathf.CeilToInt(panelWidth));
            back.height = Mathf.Max(1, Mathf.CeilToInt(panelHeight));
            MoveVisualCenterInSpace(
                back.transform,
                space,
                panelCenterX,
                panelCenterY);

            BoxCollider backCollider = back.GetComponent<BoxCollider>();
            if (backCollider == null)
                backCollider = back.GetComponentInChildren<BoxCollider>(true);
            if (backCollider != null)
            {
                Vector3 size = backCollider.size;
                size.x = back.width;
                size.y = back.height;
                backCollider.size = size;
            }
        }

        // The menu root itself is a clipped UIPanel in the Koishi/YGOPro2
        // prefab. Growing only the dark background widget leaves the new area
        // invisible because the old panel clip still cuts it to the original
        // Chinese-sized rectangle. Keep the clip synchronized with the same
        // responsive bounds.
        ResizeRootPanelClip(
            menuRoot,
            panelCenterX,
            panelCenterY,
            panelWidth,
            panelHeight);

        float panelTop = panelCenterY + panelHeight * 0.5f;
        float cursor = panelTop - VerticalPadding;

        if (version != null && version.gameObject.activeInHierarchy)
        {
            MoveVisualCenterInSpace(
                version.transform,
                space,
                GetWidgetBoundsInSpace(version, space).center.x,
                cursor - headerHeight * 0.5f);
            cursor -= headerHeight + headerGap;
        }

        // Reflow the rows vertically, but keep their common horizontal origin.
        for (int i = 0; i < rows.Count; ++i)
        {
            float targetY = cursor - rowHeight * 0.5f - i * rowPitch;
            SetTransformPositionInSpace(
                rows[i].root,
                space,
                sharedRootX,
                targetY);
        }

        // Make the clickable row area follow the responsive panel width.
        float clickableWidth = Mathf.Max(
            1f,
            panelWidth - LeftHorizontalPadding - RightHorizontalPadding);
        for (int i = 0; i < rows.Count; ++i)
        {
            BoxCollider collider = PrimaryCollider(rows[i].root);
            if (collider == null)
                continue;

            Vector3 size = collider.size;
            size.x = clickableWidth;
            size.y = Mathf.Max(size.y, rowHeight);
            collider.size = size;

            Vector3 centerWorld = space.TransformPoint(new Vector3(
                panelCenterX,
                space.InverseTransformPoint(collider.transform.position).y,
                0f));
            Vector3 centerLocal = collider.transform.InverseTransformPoint(centerWorld);
            Vector3 center = collider.center;
            center.x = centerLocal.x;
            collider.center = center;
        }
    }

    static void ResizeRootPanelClip(
        GameObject menuRoot,
        float centerX,
        float centerY,
        float width,
        float height)
    {
        if (menuRoot == null)
            return;

        UIPanel panel = menuRoot.GetComponent<UIPanel>();
        if (panel == null)
        {
            // Some source revisions put the UIPanel one level below the
            // instantiated root. Accept only a panel whose transform is still
            // part of this menu hierarchy.
            UIPanel[] panels = menuRoot.GetComponentsInChildren<UIPanel>(true);
            if (panels != null && panels.Length > 0)
                panel = panels[0];
        }

        if (panel == null)
            return;

        Transform root = menuRoot.transform;
        Vector3 centerWorld = root.TransformPoint(
            new Vector3(centerX, centerY, 0f));
        Vector3 panelCenter = panel.transform.InverseTransformPoint(centerWorld);

        Vector4 clip = panel.baseClipRegion;
        clip.x = panelCenter.x;
        clip.y = panelCenter.y;
        clip.z = Mathf.Max(1f, Mathf.Ceil(width));
        clip.w = Mathf.Max(1f, Mathf.Ceil(height));
        panel.baseClipRegion = clip;
    }

    static void TranslateMenuLabels(GameObject menuRoot)
    {
        UILabel[] labels = menuRoot.GetComponentsInChildren<UILabel>(true);
        for (int i = 0; i < labels.Length; ++i)
        {
            UILabel label = labels[i];
            if (label == null || IsVersionLabel(label))
                continue;

            string text = label.text == null ? "" : label.text;
            string translated;
            if (MenuTranslations.TryGetValue(text, out translated))
            {
                label.text = translated;
                continue;
            }

            // Progress text contains a changing percentage and is therefore not
            // suitable for an exact-key translation dictionary.
            if (text.StartsWith("正在") && text.Contains("%"))
            {
                int percent = text.LastIndexOf('%');
                int start = percent - 1;
                while (start >= 0 && Char.IsDigit(text[start]))
                    --start;
                ++start;
                if (start < percent)
                    label.text = "処理中…" + text.Substring(start, percent - start + 1);
                else
                    label.text = "処理中…";
            }
        }
    }

    static List<MenuRow> CollectRows(GameObject menuRoot)
    {
        List<MenuRow> rows = new List<MenuRow>();
        HashSet<Transform> seen = new HashSet<Transform>();
        UILabel[] labels = menuRoot.GetComponentsInChildren<UILabel>(true);
        Transform root = menuRoot.transform;

        for (int i = 0; i < labels.Length; ++i)
        {
            UILabel label = labels[i];
            if (label == null || !label.gameObject.activeInHierarchy)
                continue;
            if (IsVersionLabel(label))
                continue;

            Transform rowRoot = FindRowRoot(label.transform, root);
            if (rowRoot == null
                || !rowRoot.gameObject.activeInHierarchy
                || PrimaryCollider(rowRoot) == null
                || seen.Contains(rowRoot))
                continue;

            seen.Add(rowRoot);
            Bounds labelBounds = GetWidgetBoundsInSpace(label, rowRoot);
            Vector3 rootPosition = root.InverseTransformPoint(rowRoot.position);

            MenuRow row = new MenuRow();
            row.root = rowRoot;
            row.label = label;
            row.originalTextLeft = labelBounds.min.x;
            row.originalRootX = rootPosition.x;
            row.originalRootY = rootPosition.y;
            rows.Add(row);
        }

        return rows;
    }

    static Transform FindRowRoot(Transform label, Transform menuRoot)
    {
        Transform current = label;
        while (current != null && current != menuRoot)
        {
            Transform parent = current.parent;
            if (parent == null || parent == menuRoot)
                return current;

            int labelCount = CountActiveLabels(parent);
            if (labelCount > 1)
                return current;

            current = parent;
        }
        return null;
    }

    static int CountActiveLabels(Transform target)
    {
        UILabel[] labels = target.GetComponentsInChildren<UILabel>(true);
        int count = 0;
        for (int i = 0; i < labels.Length; ++i)
        {
            if (labels[i] != null
                && labels[i].gameObject.activeInHierarchy
                && !IsVersionLabel(labels[i]))
                ++count;
        }
        return count;
    }

    static bool IsVersionLabel(UILabel label)
    {
        if (label == null)
            return false;

        Transform t = label.transform;
        while (t != null)
        {
            string name = t.name == null ? "" : t.name.ToLowerInvariant();
            if (name.Contains("version"))
                return true;
            t = t.parent;
        }
        return false;
    }

    static UILabel FindVersionLabel(GameObject root)
    {
        UILabel[] labels = root.GetComponentsInChildren<UILabel>(true);
        for (int i = 0; i < labels.Length; ++i)
            if (IsVersionLabel(labels[i]))
                return labels[i];
        return null;
    }

    static void ConfigureLabel(UILabel label, int fontSize)
    {
        if (label == null)
            return;

        ClearAnchors(label);
        label.fontSize = fontSize;
        label.multiLine = false;
        label.pivot = UIWidget.Pivot.Left;
        label.alignment = NGUIText.Alignment.Left;

        // Measure at the real fixed size. Do not let ShrinkContent alter only
        // the longer Japanese rows.
        label.overflowMethod = UILabel.Overflow.ResizeFreely;
        label.width = 4096;
        label.height = Mathf.Max(label.height, fontSize + 6);
        Vector2 printed = label.printedSize;

        label.width = Mathf.Max(
            1,
            Mathf.CeilToInt(printed.x + LabelExtraWidth));
        label.height = Mathf.Max(
            fontSize + 6,
            Mathf.CeilToInt(printed.y + 4f));
        label.overflowMethod = UILabel.Overflow.ClampContent;
        label.fontSize = fontSize;
    }

    static void AlignLabelToIconCenter(Transform rowRoot, UILabel label)
    {
        if (rowRoot == null || label == null)
            return;

        UIWidget[] widgets = rowRoot.GetComponentsInChildren<UIWidget>(true);
        UIWidget icon = null;
        float bestArea = float.PositiveInfinity;

        for (int i = 0; i < widgets.Length; ++i)
        {
            UIWidget widget = widgets[i];
            if (widget == null
                || widget == label
                || widget is UILabel
                || !widget.gameObject.activeInHierarchy)
                continue;

            Bounds bounds = GetWidgetBoundsInSpace(widget, rowRoot);
            float width = Mathf.Abs(bounds.size.x);
            float height = Mathf.Abs(bounds.size.y);
            if (width < 1f || height < 1f
                || width > MaximumIconSize || height > MaximumIconSize)
                continue;

            float aspect = width / height;
            if (Mathf.Abs(aspect - 1f) > IconAspectTolerance)
                continue;

            float area = width * height;
            if (area < bestArea)
            {
                bestArea = area;
                icon = widget;
            }
        }

        if (icon == null)
            return;

        Bounds iconBounds = GetWidgetBoundsInSpace(icon, rowRoot);
        Bounds labelBounds = GetWidgetBoundsInSpace(label, rowRoot);
        Vector3 position = rowRoot.InverseTransformPoint(label.transform.position);
        position.y += iconBounds.center.y - labelBounds.center.y;
        label.transform.position = rowRoot.TransformPoint(position);
    }

    static UIWidget FindBackground(GameObject root, List<MenuRow> rows)
    {
        Transform named = FindTransform(root.transform, "back");
        if (named != null)
        {
            UIWidget namedWidget = named.GetComponent<UIWidget>();
            if (namedWidget == null)
                namedWidget = named.GetComponentInChildren<UIWidget>(true);
            if (namedWidget != null)
                return namedWidget;
        }

        UIWidget[] widgets = root.GetComponentsInChildren<UIWidget>(true);
        UIWidget best = null;
        float bestArea = float.PositiveInfinity;
        Transform space = root.transform;

        for (int i = 0; i < widgets.Length; ++i)
        {
            UIWidget widget = widgets[i];
            if (widget == null
                || widget is UILabel
                || !widget.gameObject.activeInHierarchy)
                continue;

            Bounds bounds = GetWidgetBoundsInSpace(widget, space);
            bool containsAll = true;
            for (int j = 0; j < rows.Count; ++j)
            {
                Vector3 p = space.InverseTransformPoint(rows[j].root.position);
                if (p.x < bounds.min.x || p.x > bounds.max.x
                    || p.y < bounds.min.y || p.y > bounds.max.y)
                {
                    containsAll = false;
                    break;
                }
            }
            if (!containsAll)
                continue;

            float area = Mathf.Max(1f, bounds.size.x * bounds.size.y);
            if (area < bestArea)
            {
                bestArea = area;
                best = widget;
            }
        }

        return best;
    }

    static Transform FindTransform(Transform root, string name)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; ++i)
            if (all[i] != null && all[i].name == name)
                return all[i];
        return null;
    }

    static BoxCollider PrimaryCollider(Transform root)
    {
        BoxCollider[] colliders = root.GetComponentsInChildren<BoxCollider>(true);
        BoxCollider best = null;
        float bestArea = -1f;
        for (int i = 0; i < colliders.Length; ++i)
        {
            BoxCollider collider = colliders[i];
            if (collider == null)
                continue;
            float area = Mathf.Abs(collider.size.x * collider.size.y);
            if (area > bestArea)
            {
                bestArea = area;
                best = collider;
            }
        }
        return best;
    }

    static void ClearAnchors(UIRect rect)
    {
        if (rect == null)
            return;
        rect.leftAnchor.target = null;
        rect.rightAnchor.target = null;
        rect.bottomAnchor.target = null;
        rect.topAnchor.target = null;
        rect.updateAnchors = UIRect.AnchorUpdate.OnStart;
    }

    static Bounds GetWidgetBoundsInSpace(UIWidget widget, Transform space)
    {
        Vector3 fallback = widget == null
            ? Vector3.zero
            : space.InverseTransformPoint(widget.transform.position);
        Bounds bounds = new Bounds(fallback, Vector3.zero);
        if (widget == null)
            return bounds;

        Vector3[] corners = widget.worldCorners;
        for (int i = 0; i < corners.Length; ++i)
        {
            Vector3 p = space.InverseTransformPoint(corners[i]);
            if (i == 0)
                bounds = new Bounds(p, Vector3.zero);
            else
                bounds.Encapsulate(p);
        }
        return bounds;
    }

    static Bounds GetVisualBoundsInSpace(Transform target, Transform space)
    {
        Vector3 fallback = target == null
            ? Vector3.zero
            : space.InverseTransformPoint(target.position);
        Bounds bounds = new Bounds(fallback, Vector3.zero);
        bool found = false;
        if (target == null)
            return bounds;

        UIWidget[] widgets = target.GetComponentsInChildren<UIWidget>(true);
        for (int i = 0; i < widgets.Length; ++i)
        {
            UIWidget widget = widgets[i];
            if (widget == null || !widget.gameObject.activeInHierarchy)
                continue;

            Vector3[] corners = widget.worldCorners;
            for (int j = 0; j < corners.Length; ++j)
            {
                Vector3 p = space.InverseTransformPoint(corners[j]);
                if (!found)
                {
                    bounds = new Bounds(p, Vector3.zero);
                    found = true;
                }
                else
                    bounds.Encapsulate(p);
            }
        }
        return bounds;
    }

    static Bounds GetCombinedRowBounds(List<MenuRow> rows, Transform space)
    {
        Bounds bounds = GetVisualBoundsInSpace(rows[0].root, space);
        for (int i = 1; i < rows.Count; ++i)
        {
            Bounds row = GetVisualBoundsInSpace(rows[i].root, space);
            bounds.Encapsulate(row.min);
            bounds.Encapsulate(row.max);
        }
        return bounds;
    }

    static void MoveWidgetLeftInSpace(UIWidget widget, Transform space, float targetLeft)
    {
        if (widget == null)
            return;

        Bounds bounds = GetWidgetBoundsInSpace(widget, space);
        Vector3 position = space.InverseTransformPoint(widget.transform.position);
        position.x += targetLeft - bounds.min.x;
        widget.transform.position = space.TransformPoint(position);
    }

    static void MoveVisualCenterInSpace(
        Transform target,
        Transform space,
        float targetX,
        float targetY)
    {
        if (target == null)
            return;

        Bounds visual = GetVisualBoundsInSpace(target, space);
        Vector3 position = space.InverseTransformPoint(target.position);
        position.x += targetX - visual.center.x;
        position.y += targetY - visual.center.y;
        target.position = space.TransformPoint(position);
    }

    static void SetTransformPositionInSpace(
        Transform target,
        Transform space,
        float x,
        float y)
    {
        if (target == null)
            return;

        Vector3 position = space.InverseTransformPoint(target.position);
        position.x = x;
        position.y = y;
        target.position = space.TransformPoint(position);
    }
}
