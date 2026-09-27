using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

public class AIRoom : WindowServantSP
{
    const int DefaultLife = 8000;
    const int OptionFontSize = 22;
    const int DeckListFontSize = 22;
    const int TitleFontSize = 28;
    const int SerializedMainWindowWidth = 1066;
    const int MainWindowHeight = 480;
    const int DeckListWidth = 330;
    const int DeckListClipWidth = 290;
    const float DeckListOffset = 310f;
    const int ActionButtonWidth = 250;
    const int ActionButtonHeight = 44;
    const string CenterColumnName = "WindBotCenterColumn";
    const string NoShuffleToggleName = "WindBotNoShuffle";
    const string PlayerFirstToggleName = "WindBotPlayerFirst";
    const string StartButtonName = "WindBotStart";
    const string BackButtonName = "WindBotBack";
    const string LocalRpsHash = "WindBot_LocalRps";
    const string LocalTurnChoiceHash = "WindBot_LocalTurnChoice";

    UIselectableList playerDeckList;
    UIselectableList aiDeckList;
    Transform centerColumnRoot;
    UIToggle noShuffleToggle;
    UIToggle playerFirstToggle;
    Transform startButton;
    Transform backButton;
    KoishiWindBotBridge windbot;

    string sort = "sortByTimeDeck";
    string[] aiDeckNames = new string[0];
    List<string> playerDeckNames = new List<string>();

    bool pregamePending;
    string pendingPlayerDeck;
    string pendingAiDeck;
    bool pendingNoShuffle;
    int layoutRefreshFrames;
    GameObject wiredWindow;

    public override void initialize()
    {
        createWindow(Program.I().new_ui_aiRoom);

        playerDeckList = FindPlayerDeckList();
        if (playerDeckList == null)
            throw new InvalidOperationException("AI room player deck list was not found.");

        CreateSideBySideDeckLists();
        ConfigureMainWindow();

        CreateCenterColumnRoot();
        EnsureCenterColumnControls();
        ConfigureCenterOptions();
        CreateDeckListTitles();
        ApplyDynamicCenterLayout();

        playerDeckList.selectedAction = OnPlayerDeckSelected;
        aiDeckList.selectedAction = OnAiDeckSelected;

        // AIRoom can be entered again after another duel/menu mode. If the same
        // runtime window is reused, do not register click handlers or install
        // selectable lists a second time. A newly-created window is wired once.
        if (wiredWindow != gameObject)
        {
            UIHelper.registEvent(gameObject, StartButtonName, onStart);
            UIHelper.registEvent(gameObject, BackButtonName, () => { Program.I().shiftToServant(Program.I().menu); });
            UIHelper.registEvent(gameObject, "exit_", () => { Program.I().shiftToServant(Program.I().menu); });

            playerDeckList.install();
            aiDeckList.install();
            wiredWindow = gameObject;
        }

        ParkListPrototype(playerDeckList);
        ParkListPrototype(aiDeckList);

        SetActiveFalse();
    }

    Transform FindControl(string name)
    {
        Transform[] children = gameObject.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; ++i)
            if (children[i].name == name)
                return children[i];
        return null;
    }

    Transform FindGeneratedControl(string name)
    {
        Transform first = null;
        Transform[] children = gameObject.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; ++i)
        {
            Transform current = children[i];
            if (current == null || current.name != name)
                continue;

            if (first == null)
            {
                first = current;
                continue;
            }

            // A previous re-initialization must never leave two runtime copies.
            current.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(current.gameObject);
        }
        return first;
    }

    UIselectableList FindPlayerDeckList()
    {
        UIselectableList fallback = null;
        UIselectableList[] lists =
            gameObject.GetComponentsInChildren<UIselectableList>(true);

        for (int i = 0; i < lists.Length; ++i)
        {
            UIselectableList list = lists[i];
            if (list == null)
                continue;

            if (list.gameObject.name == "PlayerDeckList")
                return list;

            if (list.gameObject.name != "AiDeckList" && fallback == null)
                fallback = list;
        }

        return fallback;
    }

    UIselectableList FindGeneratedDeckList(string name)
    {
        UIselectableList first = null;
        UIselectableList[] lists =
            gameObject.GetComponentsInChildren<UIselectableList>(true);

        for (int i = 0; i < lists.Length; ++i)
        {
            UIselectableList list = lists[i];
            if (list == null || list.gameObject.name != name)
                continue;

            if (first == null)
            {
                first = list;
                continue;
            }

            list.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(list.gameObject);
        }

        return first;
    }

    UILabel FindControlLabel(string name)
    {
        Transform control = FindControl(name);
        if (control == null)
            return null;

        UILabel label = control.GetComponent<UILabel>();
        if (label == null)
            label = control.GetComponentInChildren<UILabel>(true);
        return label;
    }

    Transform LayoutRoot()
    {
        Transform mainWindow = FindControl("mainWindow");
        if (mainWindow == null || mainWindow.parent == null)
            throw new InvalidOperationException("AI room layout root was not found.");
        return mainWindow.parent;
    }

    UILabel VisibleLabelTemplate()
    {
        // percyHint is not reliably rendered in the iOS build. Use a label
        // that is confirmed visible on-device as the font/material template.
        UILabel label = FindControlLabel("unrand_");
        if (label == null)
            label = FindControlLabel("first_");
        if (label == null)
            label = FindControlLabel("start_");
        if (label == null)
            throw new InvalidOperationException("A visible AI-room UILabel template was not found.");
        return label;
    }

    UILabel CreateStandaloneLabel(
        Transform parent,
        string objectName,
        string text,
        int width,
        int height,
        int fontSize,
        int depth)
    {
        UILabel template = VisibleLabelTemplate();
        GameObject display = (GameObject)UnityEngine.Object.Instantiate(template.gameObject);
        display.name = objectName;
        display.transform.SetParent(parent, false);
        display.transform.localScale = template.transform.localScale;
        display.transform.localPosition = new Vector3(0f, 0f, template.transform.localPosition.z);

        UILabel label = display.GetComponent<UILabel>();
        if (label == null)
            label = display.GetComponentInChildren<UILabel>(true);
        if (label == null)
            throw new InvalidOperationException("Standalone AI-room UILabel was not found.");

        label.text = text;
        label.fontSize = fontSize;
        label.width = width;
        label.height = height;
        label.depth = depth;
        label.pivot = UIWidget.Pivot.Center;
        label.alignment = NGUIText.Alignment.Center;
        label.overflowMethod = UILabel.Overflow.ClampContent;
        label.multiLine = false;
        ClearAnchors(label);
        label.enabled = true;

        Collider[] colliders = display.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; ++i)
            colliders[i].enabled = false;

        return label;
    }

    UILabel EnsureStandaloneLabel(
        Transform parent,
        string objectName,
        string text,
        int width,
        int height,
        int fontSize,
        int depth)
    {
        Transform existing = FindGeneratedControl(objectName);
        if (existing == null)
            return CreateStandaloneLabel(
                parent, objectName, text, width, height, fontSize, depth);

        if (existing.parent != parent)
            existing.SetParent(parent, true);

        UILabel label = existing.GetComponent<UILabel>();
        if (label == null)
            label = existing.GetComponentInChildren<UILabel>(true);
        if (label == null)
            throw new InvalidOperationException(
                "Existing standalone AI-room label is missing UILabel: " + objectName);

        label.text = text;
        label.fontSize = fontSize;
        label.width = width;
        label.height = height;
        label.depth = depth;
        label.pivot = UIWidget.Pivot.Center;
        label.alignment = NGUIText.Alignment.Center;
        label.overflowMethod = UILabel.Overflow.ClampContent;
        label.multiLine = false;
        ClearAnchors(label);
        label.enabled = true;

        Collider[] colliders = existing.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; ++i)
            colliders[i].enabled = false;

        return label;
    }

    Bounds GetWidgetBoundsInLayout(UIWidget widget)
    {
        Transform root = LayoutRoot();
        if (widget == null)
            return new Bounds(Vector3.zero, Vector3.zero);

        Vector3[] corners = widget.worldCorners;
        Vector3 first = root.InverseTransformPoint(corners[0]);
        Bounds bounds = new Bounds(first, Vector3.zero);
        for (int i = 1; i < corners.Length; ++i)
            bounds.Encapsulate(root.InverseTransformPoint(corners[i]));
        return bounds;
    }

    Bounds GetVisualBoundsInSpace(Transform target, Transform space)
    {
        Vector3 fallback = target == null
            ? Vector3.zero
            : space.InverseTransformPoint(target.position);
        Bounds bounds = new Bounds(fallback, Vector3.zero);
        bool found = false;

        if (target == null || space == null)
            return bounds;

        UIWidget[] widgets = target.GetComponentsInChildren<UIWidget>(true);
        for (int i = 0; i < widgets.Length; ++i)
        {
            UIWidget widget = widgets[i];
            if (widget == null || !widget.enabled || !widget.gameObject.activeInHierarchy)
                continue;

            Vector3[] corners = widget.worldCorners;
            for (int j = 0; j < corners.Length; ++j)
            {
                Vector3 point = space.InverseTransformPoint(corners[j]);
                if (!found)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    found = true;
                }
                else
                    bounds.Encapsulate(point);
            }
        }

        return bounds;
    }

    Bounds GetVisualBoundsInLayout(Transform target)
    {
        return GetVisualBoundsInSpace(target, LayoutRoot());
    }

    void MoveVisualCenterInLayout(Transform target, float targetX, float targetY)
    {
        if (target == null)
            return;

        Transform root = LayoutRoot();
        Bounds visual = GetVisualBoundsInLayout(target);
        Vector3 rootPosition = root.InverseTransformPoint(target.position);
        rootPosition.x += targetX - visual.center.x;
        rootPosition.y += targetY - visual.center.y;
        target.position = root.TransformPoint(rootPosition);
    }

    Bounds GetCenterColumnBounds(UIWidget playerFrame, UIWidget aiFrame)
    {
        Bounds playerBounds = GetWidgetBoundsInLayout(playerFrame);
        Bounds aiBounds = GetWidgetBoundsInLayout(aiFrame);

        float left = playerBounds.max.x;
        float right = aiBounds.min.x;
        if (right < left)
        {
            float swap = left;
            left = right;
            right = swap;
        }

        float bottom = Mathf.Max(playerBounds.min.y, aiBounds.min.y);
        float top = Mathf.Min(playerBounds.max.y, aiBounds.max.y);
        if (top < bottom)
        {
            bottom = Mathf.Min(playerBounds.min.y, aiBounds.min.y);
            top = Mathf.Max(playerBounds.max.y, aiBounds.max.y);
        }

        Vector3 min = new Vector3(left, bottom, 0f);
        Vector3 max = new Vector3(right, top, 0f);
        Bounds column = new Bounds((min + max) * 0.5f, Vector3.zero);
        column.Encapsulate(min);
        column.Encapsulate(max);
        return column;
    }

    void SetControlLabel(string name, string text, int fontSize)
    {
        Transform control = FindControl(name);
        if (control == null)
            return;

        UILabel label = FindControlLabel(name);
        if (label != null)
            label.text = text;

        UILabel[] labels = control.GetComponentsInChildren<UILabel>(true);
        for (int i = 0; i < labels.Length; ++i)
            labels[i].fontSize = fontSize;
    }

    void SetControlPosition(string name, float x, float y)
    {
        Transform control = FindControl(name);
        if (control == null)
            return;

        Vector3 p = control.localPosition;
        p.x = x;
        p.y = y;
        control.localPosition = p;
    }

    void HideControl(string name)
    {
        Transform control = FindControl(name);
        if (control != null)
            control.gameObject.SetActive(false);
    }

    void SetSetupScreenVisible(bool visible)
    {
        foreach (Transform child in gameObject.transform)
            child.gameObject.SetActive(visible);
    }

    int CalculateMainWindowWidth(Transform mainWindow)
    {
        UIWidget mainFrame = mainWindow == null
            ? null
            : mainWindow.GetComponent<UIWidget>();
        UIWidget playerFrame = playerDeckList == null
            ? null
            : playerDeckList.GetComponent<UIWidget>();
        UIWidget aiFrame = aiDeckList == null
            ? null
            : aiDeckList.GetComponent<UIWidget>();

        if (mainFrame == null || playerFrame == null || aiFrame == null)
            return SerializedMainWindowWidth;

        Bounds mainBounds = GetWidgetBoundsInLayout(mainFrame);
        Bounds playerBounds = GetWidgetBoundsInLayout(playerFrame);
        Bounds aiBounds = GetWidgetBoundsInLayout(aiFrame);

        float deckBottom = Mathf.Min(playerBounds.min.y, aiBounds.min.y);
        float bottomMargin = Mathf.Max(0f, deckBottom - mainBounds.min.y);
        float deckLeft = Mathf.Min(playerBounds.min.x, aiBounds.min.x);
        float deckRight = Mathf.Max(playerBounds.max.x, aiBounds.max.x);
        float halfContentWidth = Mathf.Max(
            mainBounds.center.x - deckLeft,
            deckRight - mainBounds.center.x);

        float desiredWidth = (halfContentWidth + bottomMargin) * 2f;
        return Mathf.Max(1, Mathf.RoundToInt(desiredWidth));
    }

    void ConfigureMainWindow()
    {
        Transform mainWindow = FindControl("mainWindow");
        if (mainWindow == null)
            throw new InvalidOperationException("AI room mainWindow was not found.");

        int mainWindowWidth = CalculateMainWindowWidth(mainWindow);
        UIWidget frame = mainWindow.GetComponent<UIWidget>();
        if (frame != null)
        {
            frame.width = mainWindowWidth;
            frame.height = MainWindowHeight;
        }

        UIPanel rootPanel = mainWindow.parent == null
            ? null
            : mainWindow.parent.GetComponent<UIPanel>();
        if (rootPanel != null)
        {
            Vector4 clip = rootPanel.baseClipRegion;
            clip.x = 0f;
            clip.y = 0f;
            clip.z = mainWindowWidth;
            clip.w = MainWindowHeight;
            rootPanel.baseClipRegion = clip;
        }

        Transform glass = FindControl("glass");
        if (glass != null)
        {
            UIWidget glassWidget = glass.GetComponent<UIWidget>();
            if (glassWidget != null)
            {
                glassWidget.width = mainWindowWidth - 36;
                glassWidget.height = MainWindowHeight - 50;
            }
        }

        BoxCollider collider = mainWindow.GetComponent<BoxCollider>();
        if (collider != null)
        {
            Vector3 size = collider.size;
            size.x = mainWindowWidth;
            size.y = MainWindowHeight;
            collider.size = size;
        }

        Transform separator = FindControl("line_");
        if (separator != null)
        {
            UIWidget line = separator.GetComponent<UIWidget>();
            if (line != null)
                line.width = mainWindowWidth - 32;
        }

        SetControlPosition(
            "exit_",
            mainWindowWidth * 0.5f - 28f,
            MainWindowHeight * 0.5f - 35f);

        Transform title = FindControl("percyHint");
        if (title != null)
        {
            Vector3 p = title.localPosition;
            p.x = 0f;
            p.y = MainWindowHeight * 0.5f - 35f;
            title.localPosition = p;

            UILabel titleLabel = title.GetComponent<UILabel>();
            if (titleLabel == null)
                titleLabel = title.GetComponentInChildren<UILabel>(true);
            if (titleLabel != null)
            {
                titleLabel.text = "WindBot AI対戦";
                titleLabel.fontSize = TitleFontSize;
                titleLabel.width = mainWindowWidth - 120;
                titleLabel.depth = 30;
            }
        }
    }

    void CreateSideBySideDeckLists()
    {
        Transform originalTransform = playerDeckList.transform;
        Transform parent = originalTransform.parent;

        originalTransform.gameObject.name = "PlayerDeckList";

        aiDeckList = FindGeneratedDeckList("AiDeckList");
        if (aiDeckList == null)
        {
            GameObject aiListObject = (GameObject)UnityEngine.Object.Instantiate(
                originalTransform.gameObject,
                originalTransform.localPosition,
                originalTransform.localRotation);
            aiListObject.name = "AiDeckList";
            aiListObject.transform.SetParent(parent, false);
            aiListObject.transform.localScale = originalTransform.localScale;

            aiDeckList = aiListObject.GetComponent<UIselectableList>();
            if (aiDeckList == null)
                throw new InvalidOperationException("Cloned AI deck list is missing UIselectableList.");
        }
        else if (aiDeckList.transform.parent != parent)
        {
            aiDeckList.transform.SetParent(parent, true);
        }

        ConfigureDeckListGeometry(playerDeckList, -DeckListOffset);
        ConfigureDeckListGeometry(aiDeckList, DeckListOffset);
        EnlargeDeckListTemplate(playerDeckList);
        EnlargeDeckListTemplate(aiDeckList);
    }

    void ConfigureDeckListGeometry(UIselectableList list, float x)
    {
        if (list == null)
            return;

        Vector3 p = list.transform.localPosition;
        p.x = x;
        p.y = -25f;
        list.transform.localPosition = p;

        UIWidget frame = list.GetComponent<UIWidget>();
        if (frame != null)
        {
            frame.width = DeckListWidth;
            frame.height = 314;
        }

        if (list.panel != null)
        {
            Vector3 panelPosition = list.panel.transform.localPosition;
            panelPosition.x = 0f;
            list.panel.transform.localPosition = panelPosition;

            Vector4 clip = list.panel.baseClipRegion;
            clip.x = 0f;
            clip.y = 0f;
            clip.z = DeckListClipWidth;
            clip.w = 314f;
            list.panel.baseClipRegion = clip;
        }

        Transform bar = list.transform.Find("bar_");
        if (bar != null)
        {
            Vector3 bp = bar.localPosition;
            bp.x = DeckListWidth * 0.5f - 5f;
            bar.localPosition = bp;
        }
    }

    void EnlargeDeckListTemplate(UIselectableList list)
    {
        if (list == null || list.mod == null)
            return;

        UILabel label = list.mod.GetComponentInChildren<UILabel>(true);
        if (label != null)
        {
            label.fontSize = DeckListFontSize;
            label.width = DeckListClipWidth - 24;
        }
    }

    void ParkListPrototype(UIselectableList list)
    {
        if (list == null || list.mod == null)
            return;

        Vector3 p = list.mod.transform.localPosition;
        p.y = 100000f;
        list.mod.transform.localPosition = p;
    }

    void CreateDeckListTitles()
    {
        EnsureStandaloneLabel(
            playerDeckList.transform,
            "PlayerDeckListTitle",
            "自分のデッキ",
            DeckListWidth,
            36,
            DeckListFontSize,
            35);

        EnsureStandaloneLabel(
            aiDeckList.transform,
            "AiDeckListTitle",
            "AIデッキ",
            DeckListWidth,
            36,
            DeckListFontSize,
            35);

        PositionDeckListTitle(playerDeckList, "PlayerDeckListTitle");
        PositionDeckListTitle(aiDeckList, "AiDeckListTitle");
    }

    void PositionDeckListTitle(UIselectableList list, string titleName)
    {
        if (list == null)
            return;

        UIWidget frame = list.GetComponent<UIWidget>();
        Transform title = FindControl(titleName);
        UILabel label = title == null ? null : title.GetComponent<UILabel>();
        if (frame == null || title == null || label == null)
            return;

        Vector3[] corners = frame.localCorners;
        float centerX = (corners[0].x + corners[2].x) * 0.5f;
        float topY = Mathf.Max(corners[1].y, corners[2].y);

        label.width = frame.width;
        label.overflowMethod = UILabel.Overflow.ClampContent;
        label.fontSize = DeckListFontSize;
        ClearAnchors(label);

        float gap = Mathf.Max(4f, label.fontSize * 0.35f);
        Vector3 position = title.localPosition;
        position.x = centerX;
        position.y = topY + label.height * 0.5f + gap;
        title.localPosition = position;
    }


    void CreateCenterColumnRoot()
    {
        Transform mainWindow = FindControl("mainWindow");
        if (mainWindow == null)
            throw new InvalidOperationException("AI room mainWindow was not found.");

        centerColumnRoot = FindGeneratedControl(CenterColumnName);
        if (centerColumnRoot == null)
        {
            GameObject rootObject = new GameObject(CenterColumnName);
            centerColumnRoot = rootObject.transform;
            centerColumnRoot.SetParent(mainWindow, false);
        }
        else if (centerColumnRoot.parent != mainWindow)
        {
            centerColumnRoot.SetParent(mainWindow, false);
        }

        centerColumnRoot.localPosition = Vector3.zero;
        centerColumnRoot.localRotation = Quaternion.identity;
        centerColumnRoot.localScale = Vector3.one;
    }

    void ClearAnchors(UIRect rect)
    {
        if (rect == null)
            return;

        rect.leftAnchor.target = null;
        rect.rightAnchor.target = null;
        rect.bottomAnchor.target = null;
        rect.topAnchor.target = null;
        rect.updateAnchors = UIRect.AnchorUpdate.OnStart;
    }

    Transform EnsureCenterClone(string runtimeName, Transform source)
    {
        if (centerColumnRoot == null || source == null)
            return null;

        Transform existing = FindGeneratedControl(runtimeName);
        if (existing == null)
        {
            GameObject cloneObject =
                (GameObject)UnityEngine.Object.Instantiate(source.gameObject);
            cloneObject.name = runtimeName;
            existing = cloneObject.transform;
        }

        if (existing.parent != centerColumnRoot)
            existing.SetParent(centerColumnRoot, false);

        existing.gameObject.SetActive(true);
        existing.localRotation = Quaternion.identity;
        existing.localScale = Vector3.one;
        existing.localPosition = Vector3.zero;
        return existing;
    }

    void MeasureCenterToggle(
        Transform control,
        string text,
        float availableWidth,
        out float checkWidth,
        out float gap,
        out float textWidth)
    {
        checkWidth = Mathf.Max(18f, OptionFontSize * 0.8f);
        gap = Mathf.Max(8f, OptionFontSize * 0.35f);
        textWidth = 1f;

        if (control == null)
            return;

        UILabel label = control.GetComponentInChildren<UILabel>(true);
        Transform background = control.Find("Background");
        UIWidget backgroundWidget =
            background == null ? null : background.GetComponent<UIWidget>();

        if (backgroundWidget != null)
            checkWidth = Mathf.Max(1f, backgroundWidget.width);
        if (label == null)
            return;

        label.text = text;
        label.fontSize = OptionFontSize;
        label.multiLine = false;
        label.overflowMethod = UILabel.Overflow.ResizeFreely;
        label.width = Mathf.Max(1, Mathf.RoundToInt(availableWidth));
        label.height = Mathf.Max(label.height, OptionFontSize + 4);
        textWidth = Mathf.Max(1f, Mathf.Ceil(label.printedSize.x));
    }

    void ConfigureCenterToggle(
        Transform control,
        string text,
        float rowWidth,
        float checkWidth,
        float gap)
    {
        if (control == null)
            return;

        UIWidget widget = control.GetComponent<UIWidget>();
        UILabel label = control.GetComponentInChildren<UILabel>(true);
        Transform background = control.Find("Background");
        UIWidget backgroundWidget =
            background == null ? null : background.GetComponent<UIWidget>();

        if (widget == null || label == null)
            return;

        ClearAnchors(widget);
        widget.pivot = UIWidget.Pivot.Center;

        label.text = text;
        label.fontSize = OptionFontSize;
        label.pivot = UIWidget.Pivot.Left;
        label.alignment = NGUIText.Alignment.Left;
        label.multiLine = false;
        ClearAnchors(label);

        widget.width = Mathf.RoundToInt(rowWidth);
        widget.height = Mathf.Max(widget.height, OptionFontSize + 6);

        float left = -rowWidth * 0.5f;

        if (backgroundWidget != null)
        {
            ClearAnchors(backgroundWidget);
            backgroundWidget.pivot = UIWidget.Pivot.Center;

            Vector3 bp = background.localPosition;
            bp.x = left + checkWidth * 0.5f;
            bp.y = 0f;
            background.localPosition = bp;
        }

        label.width = Mathf.Max(
            1,
            Mathf.RoundToInt(rowWidth - checkWidth - gap));
        label.height = Mathf.Max(label.height, OptionFontSize + 4);
        label.overflowMethod = UILabel.Overflow.ClampContent;
        label.fontSize = OptionFontSize;

        Vector3 lp = label.transform.localPosition;
        lp.x = left + checkWidth + gap;
        lp.y = 0f;
        label.transform.localPosition = lp;

        BoxCollider collider = control.GetComponent<BoxCollider>();
        if (collider != null)
        {
            collider.center = Vector3.zero;
            Vector3 size = collider.size;
            size.x = rowWidth;
            size.y = widget.height;
            collider.size = size;
        }
    }

    void ConfigureCenterButton(
        Transform control,
        string text,
        float availableWidth)
    {
        if (control == null)
            return;

        UIWidget widget = control.GetComponent<UIWidget>();
        if (widget == null)
            return;

        ClearAnchors(widget);
        widget.pivot = UIWidget.Pivot.Center;

        float width = Mathf.Min(
            ActionButtonWidth,
            Mathf.Max(1f, availableWidth));
        widget.width = Mathf.RoundToInt(width);
        widget.height = ActionButtonHeight;

        BoxCollider collider = control.GetComponent<BoxCollider>();
        if (collider != null)
        {
            collider.center = Vector3.zero;
            Vector3 size = collider.size;
            size.x = width;
            size.y = ActionButtonHeight;
            collider.size = size;
        }

        UILabel[] labels = control.GetComponentsInChildren<UILabel>(true);
        for (int i = 0; i < labels.Length; ++i)
        {
            UILabel label = labels[i];
            ClearAnchors(label);
            label.text = text;
            label.fontSize = OptionFontSize;
            label.width = Mathf.Max(1, Mathf.RoundToInt(width - 30f));
            label.height = ActionButtonHeight;
            label.pivot = UIWidget.Pivot.Center;
            label.alignment = NGUIText.Alignment.Center;
            label.overflowMethod = UILabel.Overflow.ClampContent;
            label.multiLine = false;

            Vector3 p = label.transform.localPosition;
            p.x = 0f;
            p.y = 0f;
            label.transform.localPosition = p;
        }
    }

    void EnsureCenterColumnControls()
    {
        Transform legacyNoShuffle = FindControl("unrand_");
        Transform legacyPlayerFirst = FindControl("first_");
        Transform legacyStart = FindControl("start_");

        if (legacyNoShuffle == null || legacyPlayerFirst == null || legacyStart == null)
            throw new InvalidOperationException("Legacy AI-room center templates were not found.");

        Transform noShuffle = EnsureCenterClone(
            NoShuffleToggleName, legacyNoShuffle);
        Transform playerFirst = EnsureCenterClone(
            PlayerFirstToggleName, legacyPlayerFirst);
        startButton = EnsureCenterClone(
            StartButtonName, legacyStart);
        backButton = EnsureCenterClone(
            BackButtonName, legacyStart);

        noShuffleToggle =
            noShuffle == null ? null : noShuffle.GetComponent<UIToggle>();
        playerFirstToggle =
            playerFirst == null ? null : playerFirst.GetComponent<UIToggle>();

        if (noShuffleToggle == null || playerFirstToggle == null
            || startButton == null || backButton == null)
            throw new InvalidOperationException("WindBot center controls could not be created.");
    }

    void ConfigureCenterOptions()
    {
        HideControl("life_");
        HideControl("mr4_");
        HideControl("god_");
        HideControl("rank_");
        HideControl("aideck_");

        // The original Percy controls are templates only. Keeping them out of
        // the live layout prevents their NGUI anchors from re-applying old
        // positions when the AI room is enabled again.
        HideControl("unrand_");
        HideControl("first_");
        HideControl("start");
    }

    float GetCenterControlHeight(Transform control)
    {
        UIWidget widget = control == null ? null : control.GetComponent<UIWidget>();
        return widget == null ? 1f : Mathf.Max(1f, widget.height);
    }

    void ArrangeCenterColumnContents(float columnWidth)
    {
        if (centerColumnRoot == null)
            return;

        Transform noShuffle = FindGeneratedControl(NoShuffleToggleName);
        Transform playerFirst = FindGeneratedControl(PlayerFirstToggleName);
        startButton = FindGeneratedControl(StartButtonName);
        backButton = FindGeneratedControl(BackButtonName);

        if (noShuffle == null || playerFirst == null
            || startButton == null || backButton == null)
            return;

        float margin = Mathf.Max(10f, OptionFontSize * 0.5f);
        float usableWidth = Mathf.Max(1f, columnWidth - margin * 2f);

        float noShuffleCheckWidth;
        float noShuffleGap;
        float noShuffleTextWidth;
        float playerFirstCheckWidth;
        float playerFirstGap;
        float playerFirstTextWidth;
        MeasureCenterToggle(
            noShuffle,
            "シャッフルしない",
            usableWidth,
            out noShuffleCheckWidth,
            out noShuffleGap,
            out noShuffleTextWidth);
        MeasureCenterToggle(
            playerFirst,
            "自分が先攻",
            usableWidth,
            out playerFirstCheckWidth,
            out playerFirstGap,
            out playerFirstTextWidth);

        float sharedCheckWidth = Mathf.Max(
            noShuffleCheckWidth,
            playerFirstCheckWidth);
        float sharedGap = Mathf.Max(noShuffleGap, playerFirstGap);
        float sharedTextWidth = Mathf.Max(
            noShuffleTextWidth,
            playerFirstTextWidth);
        float sharedToggleWidth = Mathf.Min(
            Mathf.Max(
                sharedCheckWidth + sharedGap + sharedTextWidth,
                OptionFontSize * 5f),
            usableWidth);

        ConfigureCenterToggle(
            noShuffle,
            "シャッフルしない",
            sharedToggleWidth,
            sharedCheckWidth,
            sharedGap);
        ConfigureCenterToggle(
            playerFirst,
            "自分が先攻",
            sharedToggleWidth,
            sharedCheckWidth,
            sharedGap);
        ConfigureCenterButton(
            startButton, "対戦開始", usableWidth);
        ConfigureCenterButton(
            backButton, "戻る", usableWidth);

        float noShuffleHeight = GetCenterControlHeight(noShuffle);
        float playerFirstHeight = GetCenterControlHeight(playerFirst);
        float startHeight = GetCenterControlHeight(startButton);
        float backHeight = GetCenterControlHeight(backButton);

        float rowGap = Mathf.Max(
            6f,
            Mathf.Min(noShuffleHeight, playerFirstHeight) * 0.25f);
        float sectionGap = Mathf.Max(
            rowGap * 2f,
            Mathf.Min(playerFirstHeight, startHeight) * 0.65f);
        float buttonGap = Mathf.Max(
            6f,
            Mathf.Min(startHeight, backHeight) * 0.20f);

        float totalHeight =
            noShuffleHeight + rowGap
            + playerFirstHeight + sectionGap
            + startHeight + buttonGap
            + backHeight;

        float cursor = totalHeight * 0.5f;

        Vector3 p = noShuffle.localPosition;
        p.x = 0f;
        p.y = cursor - noShuffleHeight * 0.5f;
        noShuffle.localPosition = p;
        cursor -= noShuffleHeight + rowGap;

        p = playerFirst.localPosition;
        p.x = 0f;
        p.y = cursor - playerFirstHeight * 0.5f;
        playerFirst.localPosition = p;
        cursor -= playerFirstHeight + sectionGap;

        p = startButton.localPosition;
        p.x = 0f;
        p.y = cursor - startHeight * 0.5f;
        startButton.localPosition = p;
        cursor -= startHeight + buttonGap;

        p = backButton.localPosition;
        p.x = 0f;
        p.y = cursor - backHeight * 0.5f;
        backButton.localPosition = p;
    }

    void PositionCenterColumn(Bounds columnBounds)
    {
        if (centerColumnRoot == null || centerColumnRoot.parent == null)
            return;

        Transform root = LayoutRoot();
        Vector3 targetWorld = root.TransformPoint(new Vector3(
            columnBounds.center.x,
            columnBounds.center.y,
            0f));
        Vector3 local =
            centerColumnRoot.parent.InverseTransformPoint(targetWorld);
        local.z = centerColumnRoot.localPosition.z;
        centerColumnRoot.localPosition = local;
    }

    void ApplyDynamicCenterLayout()
    {
        if (playerDeckList == null || aiDeckList == null || centerColumnRoot == null)
            return;

        UIWidget playerFrame = playerDeckList.GetComponent<UIWidget>();
        UIWidget aiFrame = aiDeckList.GetComponent<UIWidget>();
        if (playerFrame == null || aiFrame == null)
            return;

        Bounds columnBounds = GetCenterColumnBounds(playerFrame, aiFrame);

        // Runtime controls are normalized to center pivots and no parent anchors.
        // Their local X is always zero; only the measured column determines
        // the container position. Re-entering the room therefore cannot add
        // another positional correction.
        ArrangeCenterColumnContents(columnBounds.size.x);
        PositionCenterColumn(columnBounds);
    }


    void OnPlayerDeckSelected()
    {
        if (playerDeckList == null || String.IsNullOrWhiteSpace(playerDeckList.selectedString))
            return;

        Config.Set("deckInUse", playerDeckList.selectedString);
    }

    void OnAiDeckSelected()
    {
        if (aiDeckList == null || String.IsNullOrWhiteSpace(aiDeckList.selectedString))
            return;

        Config.Set("list_aideck", aiDeckList.selectedString);
    }

    void ApplyDeckListRowStyle(UIselectableList list)
    {
        if (list == null || list.panel == null)
            return;

        UIselectableListItem[] items =
            list.panel.GetComponentsInChildren<UIselectableListItem>(true);

        for (int i = 0; i < items.Length; ++i)
        {
            UIselectableListItem item = items[i];
            if (item == null)
                continue;

            if (item.lable != null)
            {
                item.lable.fontSize = DeckListFontSize;
                item.lable.width = DeckListClipWidth - 24;
                item.lable.height = 33;
            }

            if (item.selectedObject != null)
            {
                UIWidget selected = item.selectedObject.GetComponent<UIWidget>();
                if (selected != null)
                    selected.width = DeckListClipWidth - 8;
            }

            BoxCollider collider = item.btn == null
                ? null
                : item.btn.GetComponent<BoxCollider>();
            if (collider != null)
            {
                Vector3 size = collider.size;
                size.x = DeckListClipWidth - 8;
                collider.size = size;
            }
        }
    }

    void PopulatePlayerDeckList()
    {
        playerDeckList.clear();
        string selected = Config.Get("deckInUse", "miaowu");

        for (int i = 0; i < playerDeckNames.Count; ++i)
            playerDeckList.add(playerDeckNames[i]);

        if (!playerDeckNames.Contains(selected) && playerDeckNames.Count > 0)
        {
            selected = playerDeckNames[0];
            Config.Set("deckInUse", selected);
        }

        playerDeckList.selectedString = selected;
        playerDeckList.toTop();
        playerDeckList.mark();
        ApplyDeckListRowStyle(playerDeckList);
    }

    void PopulateAiDeckList()
    {
        aiDeckList.clear();
        string selected = Config.Get("list_aideck", "RadiantTyphoon");

        for (int i = 0; i < aiDeckNames.Length; ++i)
            aiDeckList.add(aiDeckNames[i]);

        if (Array.IndexOf(aiDeckNames, selected) < 0 && aiDeckNames.Length > 0)
        {
            selected = aiDeckNames[0];
            Config.Set("list_aideck", selected);
        }

        aiDeckList.selectedString = selected;
        aiDeckList.toTop();
        aiDeckList.mark();
        ApplyDeckListRowStyle(aiDeckList);
    }

    void ApplyStableLayout()
    {
        ConfigureDeckListGeometry(playerDeckList, -DeckListOffset);
        ConfigureDeckListGeometry(aiDeckList, DeckListOffset);
        ConfigureMainWindow();
        ConfigureCenterOptions();
        ApplyDeckListRowStyle(playerDeckList);
        ApplyDeckListRowStyle(aiDeckList);
        PositionDeckListTitle(playerDeckList, "PlayerDeckListTitle");
        PositionDeckListTitle(aiDeckList, "AiDeckListTitle");
        ApplyDynamicCenterLayout();
    }

    void onStart()
    {
        if (!isShowed || pregamePending)
            return;

        pendingPlayerDeck = "deck/" + Config.Get("deckInUse", "miaowu") + ".ydk";
        pendingAiDeck = Config.Get("list_aideck", "RadiantTyphoon");
        if (noShuffleToggle == null || playerFirstToggle == null)
            throw new InvalidOperationException("WindBot center toggles are unavailable.");

        pendingNoShuffle = noShuffleToggle.value;
        bool forcePlayerFirst = playerFirstToggle.value;

        pregamePending = true;
        if (forcePlayerFirst)
            StartPendingDuel(true);
        else
        {
            SetSetupScreenVisible(false);
            ShowLocalRockPaperScissors();
        }
    }

    void ShowLocalRockPaperScissors()
    {
        if (!pregamePending)
            return;

        RMSshow_tp(
            LocalRpsHash,
            new messageSystemValue { hint = "jiandao", value = "1" },
            new messageSystemValue { hint = "shitou", value = "2" },
            new messageSystemValue { hint = "bu", value = "3" }
        );
    }

    void ShowLocalHandResult(int playerHand, int aiHand)
    {
        Program.I().new_ui_handShower.GetComponent<handShower>().me = playerHand - 1;
        Program.I().new_ui_handShower.GetComponent<handShower>().op = aiHand - 1;
        GameObject result = create(
            Program.I().new_ui_handShower,
            Vector3.zero,
            Vector3.zero,
            false,
            Program.I().ui_main_2d
        );
        destroy(result, 2f);
    }

    static int RockPaperScissorsWinner(int playerHand, int aiHand)
    {
        if (playerHand == aiHand)
            return 0;
        if ((playerHand == 1 && aiHand == 3)
            || (playerHand == 2 && aiHand == 1)
            || (playerHand == 3 && aiHand == 2))
            return 1;
        return -1;
    }

    void ResolveLocalRockPaperScissors(int playerHand)
    {
        int aiHand;
        bool aiWantsFirst;
        try
        {
            KoishiWindBotBridge.GetAiPregameChoices(pendingAiDeck, out aiHand, out aiWantsFirst);
        }
        catch (Exception e)
        {
            pregamePending = false;
            SetSetupScreenVisible(true);
            Program.DEBUGLOG("[WindBot] pregame failed: " + e);
            RMSshow_none("WindBotのじゃんけん準備に失敗しました。\n" + e.Message);
            return;
        }

        ShowLocalHandResult(playerHand, aiHand);
        int winner = RockPaperScissorsWinner(playerHand, aiHand);

        if (winner == 0)
        {
            Program.go(1300, () =>
            {
                if (pregamePending)
                    ShowLocalRockPaperScissors();
            });
            return;
        }

        if (winner > 0)
        {
            Program.go(1300, () =>
            {
                if (!pregamePending)
                    return;
                RMSshow_FS(
                    LocalTurnChoiceHash,
                    new messageSystemValue { hint = "先攻", value = "first" },
                    new messageSystemValue { hint = "後攻", value = "second" }
                );
            });
            return;
        }

        bool humanFirst = !aiWantsFirst;
        Program.go(1300, () =>
        {
            if (pregamePending)
                StartPendingDuel(humanFirst);
        });
    }

    void StartPendingDuel(bool playerGoFirst)
    {
        if (!pregamePending)
            return;

        if (windbot != null)
            windbot.Dispose();
        windbot = new KoishiWindBotBridge();

        pregamePending = false;
        if (windbot.StartAI(pendingPlayerDeck, pendingAiDeck, playerGoFirst, pendingNoShuffle, DefaultLife))
            RMSshow_none("WindBot: " + pendingAiDeck + " で開始します。");
        else
            SetSetupScreenVisible(true);
    }

    public override void ES_RMS(string hashCode, List<messageSystemValue> result)
    {
        base.ES_RMS(hashCode, result);

        if (!pregamePending || result == null || result.Count == 0)
            return;

        if (hashCode == LocalRpsHash)
        {
            int hand;
            if (Int32.TryParse(result[0].value, out hand) && hand >= 1 && hand <= 3)
                ResolveLocalRockPaperScissors(hand);
            return;
        }

        if (hashCode == LocalTurnChoiceHash)
        {
            if (result[0].value == "first")
                StartPendingDuel(true);
            else if (result[0].value == "second")
                StartPendingDuel(false);
        }
    }

    void LoadDeckNames()
    {
        Directory.CreateDirectory("deck");
        FileInfo[] files = (new DirectoryInfo("deck")).GetFiles("*.ydk");
        Array.Sort(files, Config.Get(sort, "1") == "1" ? UIHelper.CompareTime : UIHelper.CompareName);

        playerDeckNames.Clear();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < files.Length; ++i)
        {
            string name = Path.GetFileNameWithoutExtension(files[i].Name);
            if (String.IsNullOrWhiteSpace(name) || !seen.Add(name))
                continue;
            playerDeckNames.Add(name);
        }

        WindBot.Game.AI.DecksManager.Init();
        aiDeckNames = WindBot.Game.AI.DecksManager.GetDeckNames();
    }

    public override void show()
    {
        pregamePending = false;

        if (windbot != null)
        {
            windbot.Dispose();
            windbot = null;
        }

        base.show();
        SetSetupScreenVisible(true);
        ApplyStableLayout();
        layoutRefreshFrames = 3;

        LoadDeckNames();
        PopulatePlayerDeckList();
        PopulateAiDeckList();
        Program.charge();
    }

    public override void preFrameFunction()
    {
        base.preFrameFunction();

        if (isShowed)
        {
            if (layoutRefreshFrames > 0)
            {
                ApplyStableLayout();
                --layoutRefreshFrames;
            }

            // UIselectableList creates visible rows lazily while scrolling.
            // Keep newly-created rows on the AI-room-specific 22px style.
            ApplyDeckListRowStyle(playerDeckList);
            ApplyDeckListRowStyle(aiDeckList);
        }

        Menu.checkCommend();
    }
}
