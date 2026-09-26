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
    const int MainWindowWidth = 980;
    const int MainWindowHeight = 420;
    const int DeckListWidth = 280;
    const int DeckListClipWidth = 240;
    const float DeckListOffset = 325f;
    const int ActionButtonWidth = 300;
    const int ActionButtonHeight = 44;
    const string LocalRpsHash = "WindBot_LocalRps";
    const string LocalTurnChoiceHash = "WindBot_LocalTurnChoice";

    UIselectableList playerDeckList;
    UIselectableList aiDeckList;
    UILabel playerDeckDisplayLabel;
    UILabel aiDeckDisplayLabel;
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
        CreateCenterSelectionDisplays();
        CreateDeckListTitles();
        CreateBackButton();

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
        p.y = -35f;
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
        Transform hint = FindControl("percyHint");
        if (hint == null)
            return;

        CreateDeckListTitle(hint.gameObject, playerDeckList, "自分のデッキ", "PlayerDeckListTitle");
        CreateDeckListTitle(hint.gameObject, aiDeckList, "AIデッキ", "AiDeckListTitle");
    }

    void CreateDeckListTitle(GameObject template, UIselectableList list, string text, string objectName)
    {
        if (template == null || list == null || list.panel == null)
            return;

        GameObject title = (GameObject)UnityEngine.Object.Instantiate(template);
        title.name = objectName;
        title.transform.SetParent(list.transform.parent, false);
        title.transform.localScale = template.transform.localScale;

        Vector3 p = list.transform.localPosition;
        p.y = 140f;
        title.transform.localPosition = p;

        UILabel label = title.GetComponent<UILabel>();
        if (label == null)
            label = title.GetComponentInChildren<UILabel>(true);
        if (label != null)
        {
            label.text = text;
            label.fontSize = DeckListFontSize;
            label.width = DeckListWidth;
            label.depth = 30;
        }

        Collider[] colliders = title.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; ++i)
            colliders[i].enabled = false;
    }

    void ConfigureCenterOptions()
    {
        HideControl("life_");
        HideControl("mr4_");
        HideControl("god_");

        // The legacy Percy popup controls have their own anchored child labels.
        // Reusing them caused the selected deck names to intrude into the left
        // deck list. Hide them completely and use dedicated center UILabels.
        HideControl("rank_");
        HideControl("aideck_");

        SetControlLabel("unrand_", "シャッフルしない", OptionFontSize);
        SetControlLabel("first_", "自分が先攻", OptionFontSize);

        SetControlPosition("unrand_", -82f, -8f);
        SetControlPosition("first_", -82f, -52f);

        SetControlWidgetWidth("unrand_", 220);
        SetControlWidgetWidth("first_", 220);

        Transform startGroup = FindControl("start");
        if (startGroup != null)
        {
            Vector3 p = startGroup.localPosition;
            p.x = 0f;
            p.y = -125f;
            startGroup.localPosition = p;

            Transform texture = startGroup.Find("Texture");
            if (texture != null)
                texture.gameObject.SetActive(false);
        }

        SetControlPosition("start_", 0f, 0f);
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

        Vector3 p = start.localPosition;
        p.y -= 52f;
        back.transform.localPosition = p;

        ConfigureActionButton("back_", "戻る");
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


    void CreateCenterSelectionDisplays()
    {
        Transform template = FindControl("percyHint");
        if (template == null)
            throw new InvalidOperationException("AI room title label was not found.");

        playerDeckDisplayLabel = CreateCenterDisplayLabel(
            template.gameObject,
            "PlayerDeckDisplay",
            96f);
        aiDeckDisplayLabel = CreateCenterDisplayLabel(
            template.gameObject,
            "AiDeckDisplay",
            52f);

        UpdateSelectedDeckDisplays();
    }

    UILabel CreateCenterDisplayLabel(GameObject template, string objectName, float y)
    {
        GameObject display = (GameObject)UnityEngine.Object.Instantiate(template);
        display.name = objectName;
        display.transform.SetParent(template.transform.parent, false);
        display.transform.localScale = template.transform.localScale;
        display.transform.localPosition = new Vector3(0f, y, template.transform.localPosition.z);

        UILabel label = display.GetComponent<UILabel>();
        if (label == null)
            label = display.GetComponentInChildren<UILabel>(true);
        if (label == null)
            throw new InvalidOperationException("Center deck display UILabel was not found.");

        label.fontSize = OptionFontSize;
        label.width = 340;
        label.height = 38;
        label.depth = 30;

        Collider[] colliders = display.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; ++i)
            colliders[i].enabled = false;

        return label;
    }

    void PositionCenterSelectionDisplays()
    {
        if (playerDeckDisplayLabel != null)
        {
            Vector3 p = playerDeckDisplayLabel.transform.localPosition;
            p.x = 0f;
            p.y = 96f;
            playerDeckDisplayLabel.transform.localPosition = p;
            playerDeckDisplayLabel.width = 340;
            playerDeckDisplayLabel.fontSize = OptionFontSize;
        }

        if (aiDeckDisplayLabel != null)
        {
            Vector3 p = aiDeckDisplayLabel.transform.localPosition;
            p.x = 0f;
            p.y = 52f;
            aiDeckDisplayLabel.transform.localPosition = p;
            aiDeckDisplayLabel.width = 340;
            aiDeckDisplayLabel.fontSize = OptionFontSize;
        }
    }

    void UpdateSelectedDeckDisplays()
    {
        string player = Config.Get("deckInUse", "");
        string ai = Config.Get("list_aideck", "RadiantTyphoon");

        if (playerDeckDisplayLabel != null)
            playerDeckDisplayLabel.text = "自分: " + player;
        if (aiDeckDisplayLabel != null)
            aiDeckDisplayLabel.text = "AI: " + ai;
    }

    void OnPlayerDeckSelected()
    {
        if (playerDeckList == null || String.IsNullOrWhiteSpace(playerDeckList.selectedString))
            return;

        Config.Set("deckInUse", playerDeckList.selectedString);
        UpdateSelectedDeckDisplays();
    }

    void OnAiDeckSelected()
    {
        if (aiDeckList == null || String.IsNullOrWhiteSpace(aiDeckList.selectedString))
            return;

        Config.Set("list_aideck", aiDeckList.selectedString);
        UpdateSelectedDeckDisplays();
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
        PositionCenterSelectionDisplays();
        UpdateSelectedDeckDisplays();
        ApplyDeckListRowStyle(playerDeckList);
        ApplyDeckListRowStyle(aiDeckList);

        Transform playerTitle = FindControl("PlayerDeckListTitle");
        if (playerTitle != null)
        {
            Vector3 p = playerDeckList.transform.localPosition;
            p.y = 140f;
            playerTitle.localPosition = p;
        }

        Transform aiTitle = FindControl("AiDeckListTitle");
        if (aiTitle != null)
        {
            Vector3 p = aiDeckList.transform.localPosition;
            p.y = 172f;
            aiTitle.localPosition = p;
        }

        Transform back = FindControl("back_");
        Transform start = FindControl("start_");
        if (back != null && start != null)
        {
            Vector3 p = start.localPosition;
            p.y -= 52f;
            back.localPosition = p;
            ConfigureActionButton("back_", "戻る");
        }
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
        UpdateSelectedDeckDisplays();
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
