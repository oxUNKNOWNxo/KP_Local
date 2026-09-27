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
    const int MainWindowWidth = 1100;
    const int MainWindowHeight = 480;
    const int DeckListWidth = 330;
    const int DeckListClipWidth = 290;
    const float DeckListOffset = 310f;
    const int ActionButtonWidth = 250;
    const int ActionButtonHeight = 44;
    const string LocalRpsHash = "WindBot_LocalRps";
    const string LocalTurnChoiceHash = "WindBot_LocalTurnChoice";

    UIselectableList playerDeckList;
    UIselectableList aiDeckList;
    Transform centerColumnRoot;
    KoishiWindBotBridge windbot;

    string sort = "sortByTimeDeck";
    string[] aiDeckNames = new string[0];
    List<string> playerDeckNames = new List<string>();

    bool pregamePending;
    string pendingPlayerDeck;
    string pendingAiDeck;
    bool pendingNoShuffle;
    int layoutRefreshFrames;

    public override void initialize()
    {
        createWindow(Program.I().new_ui_aiRoom);

        playerDeckList = gameObject.GetComponentInChildren<UIselectableList>();
        if (playerDeckList == null)
            throw new InvalidOperationException("AI room player deck list was not found.");

        ConfigureMainWindow();
        CreateSideBySideDeckLists();

        ConfigureCenterOptions();
        CreateCenterColumnRoot();
        CreateDeckListTitles();
        CreateBackButton();
        ArrangeCenterColumnContents();
        ApplyDynamicCenterLayout();

        playerDeckList.selectedAction = OnPlayerDeckSelected;
        aiDeckList.selectedAction = OnAiDeckSelected;

        UIHelper.registEvent(gameObject, "start_", onStart);
        UIHelper.registEvent(gameObject, "back_", () => { Program.I().shiftToServant(Program.I().menu); });
        UIHelper.registEvent(gameObject, "exit_", () => { Program.I().shiftToServant(Program.I().menu); });

        playerDeckList.install();
        aiDeckList.install();

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
        label.enabled = true;

        Collider[] colliders = display.GetComponentsInChildren<Collider>(true);
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

    void ConfigureMainWindow()
    {
        Transform mainWindow = FindControl("mainWindow");
        if (mainWindow == null)
            throw new InvalidOperationException("AI room mainWindow was not found.");

        UIWidget frame = mainWindow.GetComponent<UIWidget>();
        if (frame != null)
        {
            frame.width = MainWindowWidth;
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
            clip.z = MainWindowWidth;
            clip.w = MainWindowHeight;
            rootPanel.baseClipRegion = clip;
        }

        Transform glass = FindControl("glass");
        if (glass != null)
        {
            UIWidget glassWidget = glass.GetComponent<UIWidget>();
            if (glassWidget != null)
            {
                glassWidget.width = MainWindowWidth - 36;
                glassWidget.height = MainWindowHeight - 50;
            }
        }

        BoxCollider collider = mainWindow.GetComponent<BoxCollider>();
        if (collider != null)
        {
            Vector3 size = collider.size;
            size.x = MainWindowWidth;
            size.y = MainWindowHeight;
            collider.size = size;
        }

        Transform separator = FindControl("line_");
        if (separator != null)
        {
            UIWidget line = separator.GetComponent<UIWidget>();
            if (line != null)
                line.width = MainWindowWidth - 32;
        }

        SetControlPosition(
            "exit_",
            MainWindowWidth * 0.5f - 28f,
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
                titleLabel.width = MainWindowWidth - 120;
                titleLabel.depth = 30;
            }
        }
    }

    void CreateSideBySideDeckLists()
    {
        Transform originalTransform = playerDeckList.transform;
        Transform parent = originalTransform.parent;

        originalTransform.gameObject.name = "PlayerDeckList";

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
        CreateStandaloneLabel(
            playerDeckList.transform,
            "PlayerDeckListTitle",
            "自分のデッキ",
            DeckListWidth,
            36,
            DeckListFontSize,
            35);

        CreateStandaloneLabel(
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

        Bounds frameBounds = GetWidgetBoundsInLayout(frame);
        label.width = frame.width;

        float gap = Mathf.Max(4f, label.fontSize * 0.35f);
        float targetY = frameBounds.max.y + label.height * 0.5f + gap;
        MoveVisualCenterInLayout(title, frameBounds.center.x, targetY);
    }


    void CreateCenterColumnRoot()
    {
        Transform mainWindow = FindControl("mainWindow");
        if (mainWindow == null)
            throw new InvalidOperationException("AI room mainWindow was not found.");

        GameObject rootObject = new GameObject("WindBotCenterColumn");
        centerColumnRoot = rootObject.transform;
        centerColumnRoot.SetParent(mainWindow, false);
        centerColumnRoot.localPosition = Vector3.zero;
        centerColumnRoot.localRotation = Quaternion.identity;
        centerColumnRoot.localScale = Vector3.one;

        string[] names = { "unrand_", "first_", "start" };
        for (int i = 0; i < names.Length; ++i)
        {
            Transform control = FindControl(names[i]);
            if (control != null)
                control.SetParent(centerColumnRoot, false);
        }
    }

    void CenterChildHorizontally(Transform target)
    {
        if (target == null || centerColumnRoot == null)
            return;

        Bounds bounds = GetVisualBoundsInSpace(target, centerColumnRoot);
        Vector3 p = target.localPosition;
        p.x -= bounds.center.x;
        target.localPosition = p;
    }

    void PositionSecondToggleBelowFirst()
    {
        if (centerColumnRoot == null)
            return;

        Transform unrand = FindControl("unrand_");
        Transform first = FindControl("first_");
        if (unrand == null || first == null)
            return;

        Bounds upper = GetVisualBoundsInSpace(unrand, centerColumnRoot);
        Bounds lower = GetVisualBoundsInSpace(first, centerColumnRoot);
        float gap = Mathf.Max(6f, Mathf.Min(upper.size.y, lower.size.y) * 0.25f);
        float targetCenterY = upper.min.y - gap - lower.size.y * 0.5f;

        Vector3 p = first.localPosition;
        p.y += targetCenterY - lower.center.y;
        first.localPosition = p;
    }

    void PositionBackBelowStart()
    {
        Transform start = FindControl("start_");
        Transform back = FindControl("back_");
        if (start == null || back == null || start.parent == null || start.parent != back.parent)
            return;

        Transform parent = start.parent;
        Bounds startBounds = GetVisualBoundsInSpace(start, parent);
        Bounds backBounds = GetVisualBoundsInSpace(back, parent);
        float gap = Mathf.Max(6f, Mathf.Min(startBounds.size.y, backBounds.size.y) * 0.20f);
        float targetCenterY = startBounds.min.y - gap - backBounds.size.y * 0.5f;

        Vector3 p = back.localPosition;
        p.x = start.localPosition.x;
        p.y += targetCenterY - backBounds.center.y;
        back.localPosition = p;
    }

    void PositionButtonGroupBelowToggles()
    {
        if (centerColumnRoot == null)
            return;

        Transform first = FindControl("first_");
        Transform startGroup = FindControl("start");
        Transform start = FindControl("start_");
        if (first == null || startGroup == null || start == null)
            return;

        Bounds firstBounds = GetVisualBoundsInSpace(first, centerColumnRoot);
        Bounds groupBounds = GetVisualBoundsInSpace(startGroup, centerColumnRoot);
        Bounds startBounds = GetVisualBoundsInSpace(start, startGroup);

        float sectionGap = Mathf.Max(
            12f,
            Mathf.Min(firstBounds.size.y, startBounds.size.y) * 0.65f);
        float targetTop = firstBounds.min.y - sectionGap;

        Vector3 p = startGroup.localPosition;
        p.y += targetTop - groupBounds.max.y;
        startGroup.localPosition = p;
    }

    void ArrangeCenterColumnContents()
    {
        if (centerColumnRoot == null)
            return;

        // Preserve one hierarchy and arrange inside it. Do not move each row in
        // world space: NGUI child widgets and their parents use different origins.
        PositionSecondToggleBelowFirst();
        PositionBackBelowStart();

        CenterChildHorizontally(FindControl("unrand_"));
        CenterChildHorizontally(FindControl("first_"));
        CenterChildHorizontally(FindControl("start"));

        PositionButtonGroupBelowToggles();
    }

    void ConfigureCenterOptions()
    {
        HideControl("life_");
        HideControl("mr4_");
        HideControl("god_");

        // The legacy Percy popup controls have their own anchored child labels.
        // Keep them hidden. The selected deck is already visible in each list,
        // so do not duplicate long deck names in the narrow center column.
        HideControl("rank_");
        HideControl("aideck_");

        SetControlLabel("unrand_", "シャッフルしない", OptionFontSize);
        SetControlLabel("first_", "自分が先攻", OptionFontSize);

        SetControlWidgetWidth("unrand_", 200);
        SetControlWidgetWidth("first_", 200);

        Transform startGroup = FindControl("start");
        if (startGroup != null)
        {
            Transform texture = startGroup.Find("Texture");
            if (texture != null)
                texture.gameObject.SetActive(false);
        }

        ConfigureActionButton("start_", "対戦開始");
    }


    void ConfigureActionButton(string name, string text)
    {
        Transform control = FindControl(name);
        if (control == null)
            return;

        UIWidget widget = control.GetComponent<UIWidget>();
        if (widget != null)
        {
            widget.width = ActionButtonWidth;
            widget.height = ActionButtonHeight;
        }

        BoxCollider collider = control.GetComponent<BoxCollider>();
        if (collider != null)
        {
            Vector3 size = collider.size;
            size.x = ActionButtonWidth;
            size.y = ActionButtonHeight;
            collider.size = size;
        }

        UILabel[] labels = control.GetComponentsInChildren<UILabel>(true);
        for (int i = 0; i < labels.Length; ++i)
        {
            labels[i].text = text;
            labels[i].fontSize = OptionFontSize;
            labels[i].width = ActionButtonWidth - 30;
            labels[i].height = ActionButtonHeight;
        }
    }

    void CreateBackButton()
    {
        Transform start = FindControl("start_");
        if (start == null)
            return;

        GameObject back = (GameObject)UnityEngine.Object.Instantiate(start.gameObject);
        back.name = "back_";
        back.transform.SetParent(start.parent, false);
        back.transform.localScale = start.localScale;

        back.transform.localPosition = start.localPosition;

        ConfigureActionButton("back_", "戻る");
        PositionBackBelowStart();
    }


    void SetControlWidgetWidth(string name, int width)
    {
        Transform control = FindControl(name);
        if (control == null)
            return;

        UIWidget widget = control.GetComponent<UIWidget>();
        if (widget != null)
            widget.width = width;

        UILabel[] labels = control.GetComponentsInChildren<UILabel>(true);
        for (int i = 0; i < labels.Length; ++i)
            labels[i].width = Math.Max(labels[i].width, width - 30);
    }

    void ApplyDynamicCenterLayout()
    {
        if (playerDeckList == null || aiDeckList == null || centerColumnRoot == null)
            return;

        UIWidget playerFrame = playerDeckList.GetComponent<UIWidget>();
        UIWidget aiFrame = aiDeckList.GetComponent<UIWidget>();
        if (playerFrame == null || aiFrame == null)
            return;

        ArrangeCenterColumnContents();

        Bounds columnBounds = GetCenterColumnBounds(playerFrame, aiFrame);
        Bounds contentBounds = GetVisualBoundsInLayout(centerColumnRoot);

        Transform root = LayoutRoot();
        Vector3 rootPosition = root.InverseTransformPoint(centerColumnRoot.position);
        rootPosition.x += columnBounds.center.x - contentBounds.center.x;
        rootPosition.y += columnBounds.center.y - contentBounds.center.y;
        centerColumnRoot.position = root.TransformPoint(rootPosition);
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
        ConfigureMainWindow();
        ConfigureDeckListGeometry(playerDeckList, -DeckListOffset);
        ConfigureDeckListGeometry(aiDeckList, DeckListOffset);
        ConfigureCenterOptions();
        ConfigureActionButton("back_", "戻る");
        ApplyDeckListRowStyle(playerDeckList);
        ApplyDeckListRowStyle(aiDeckList);
        PositionDeckListTitle(playerDeckList, "PlayerDeckListTitle");
        PositionDeckListTitle(aiDeckList, "AiDeckListTitle");
        ArrangeCenterColumnContents();
        ApplyDynamicCenterLayout();
    }

    void onStart()
    {
        if (!isShowed || pregamePending)
            return;

        pendingPlayerDeck = "deck/" + Config.Get("deckInUse", "miaowu") + ".ydk";
        pendingAiDeck = Config.Get("list_aideck", "RadiantTyphoon");
        pendingNoShuffle = UIHelper.getByName<UIToggle>(gameObject, "unrand_").value;
        bool forcePlayerFirst = UIHelper.getByName<UIToggle>(gameObject, "first_").value;

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
