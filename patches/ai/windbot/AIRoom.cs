using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

public class AIRoom : WindowServantSP
{
    const int DefaultLife = 8000;
    const int OptionFontSize = 26;
    const int DeckListFontSize = 24;
    const float FallbackDeckListOffset = 330f;
    const string LocalRpsHash = "WindBot_LocalRps";
    const string LocalTurnChoiceHash = "WindBot_LocalTurnChoice";

    UIselectableList playerDeckList;
    UIselectableList aiDeckList;
    UIPopupList playerDeckDisplay;
    UIPopupList aiDeckDisplay;
    KoishiWindBotBridge windbot;

    string sort = "sortByTimeDeck";
    string[] aiDeckNames = new string[0];
    List<string> playerDeckNames = new List<string>();

    bool pregamePending;
    string pendingPlayerDeck;
    string pendingAiDeck;
    bool pendingNoShuffle;

    public override void initialize()
    {
        createWindow(Program.I().new_ui_aiRoom);

        playerDeckList = gameObject.GetComponentInChildren<UIselectableList>();
        if (playerDeckList == null)
            throw new InvalidOperationException("AI room player deck list was not found.");

        CreateSideBySideDeckLists();

        playerDeckDisplay = UIHelper.getByName<UIPopupList>(gameObject, "rank_");
        aiDeckDisplay = UIHelper.getByName<UIPopupList>(gameObject, "aideck_");

        ConfigureCenterOptions();

        playerDeckList.selectedAction = OnPlayerDeckSelected;
        aiDeckList.selectedAction = OnAiDeckSelected;

        UIHelper.registEvent(gameObject, "start_", onStart);
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
        UILabel label = FindControlLabel(name);
        if (label == null)
            return;

        label.text = text;
        label.fontSize = fontSize;
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

    void CreateSideBySideDeckLists()
    {
        Transform originalTransform = playerDeckList.transform;
        Transform parent = originalTransform.parent;
        Vector3 originalPosition = originalTransform.localPosition;

        float sideOffset = Math.Abs(originalPosition.x);
        if (sideOffset < 180f)
            sideOffset = FallbackDeckListOffset;

        originalTransform.localPosition = new Vector3(
            -sideOffset,
            originalPosition.y,
            originalPosition.z);
        originalTransform.gameObject.name = "PlayerDeckList";

        GameObject aiListObject = (GameObject)UnityEngine.Object.Instantiate(
            originalTransform.gameObject,
            originalTransform.localPosition,
            originalTransform.localRotation);
        aiListObject.name = "AiDeckList";
        aiListObject.transform.SetParent(parent, false);
        aiListObject.transform.localScale = originalTransform.localScale;
        aiListObject.transform.localPosition = new Vector3(
            sideOffset,
            originalPosition.y,
            originalPosition.z);

        aiDeckList = aiListObject.GetComponent<UIselectableList>();
        if (aiDeckList == null)
            throw new InvalidOperationException("Cloned AI deck list is missing UIselectableList.");

        EnlargeDeckListTemplate(playerDeckList);
        EnlargeDeckListTemplate(aiDeckList);
    }

    void EnlargeDeckListTemplate(UIselectableList list)
    {
        if (list == null || list.mod == null)
            return;

        UILabel label = list.mod.GetComponentInChildren<UILabel>(true);
        if (label != null)
            label.fontSize = Math.Max(label.fontSize, DeckListFontSize);
    }

    void ParkListPrototype(UIselectableList list)
    {
        if (list == null || list.mod == null)
            return;

        Vector3 p = list.mod.transform.localPosition;
        p.y = 100000f;
        list.mod.transform.localPosition = p;
    }

    void ConfigureCenterOptions()
    {
        HideControl("life_");
        HideControl("mr4_");
        HideControl("god_");

        SetControlLabel("unrand_", "シャッフルしない", OptionFontSize);
        SetControlLabel("first_", "自分が先攻", OptionFontSize);
        SetControlLabel("start_", "対戦開始", OptionFontSize);
        SetControlLabel("exit_", "戻る", OptionFontSize);

        SetControlPosition("rank_", 0f, 135f);
        SetControlPosition("aideck_", 0f, 90f);
        SetControlPosition("unrand_", 0f, 25f);
        SetControlPosition("first_", 0f, -25f);

        Transform hint = FindControl("percyHint");
        if (hint != null)
        {
            Vector3 p = hint.localPosition;
            p.x = 0f;
            p.y = 190f;
            hint.localPosition = p;
        }
        SetControlLabel("percyHint", "WindBot AI対戦", OptionFontSize);

        Transform buttons = FindControl("btns");
        if (buttons != null)
        {
            Vector3 p = buttons.localPosition;
            p.x = 0f;
            buttons.localPosition = p;
        }
        SetControlPosition("start_", -70f, 142f);
        SetControlPosition("exit_", 70f, 142f);

        ConfigureReadOnlyDisplay(playerDeckDisplay);
        ConfigureReadOnlyDisplay(aiDeckDisplay);
        UpdateSelectedDeckDisplays();
    }

    void ConfigureReadOnlyDisplay(UIPopupList popup)
    {
        if (popup == null)
            return;

        popup.enabled = false;
        popup.fontSize = OptionFontSize;

        UILabel label = popup.GetComponent<UILabel>();
        if (label == null)
            label = popup.GetComponentInChildren<UILabel>(true);
        if (label != null)
            label.fontSize = OptionFontSize;

        Collider[] colliders = popup.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; ++i)
            colliders[i].enabled = false;
    }

    void SetReadOnlyDisplay(UIPopupList popup, string text)
    {
        if (popup == null)
            return;

        popup.enabled = true;
        popup.Clear();
        popup.AddItem(text);
        popup.value = text;
        popup.enabled = false;

        UILabel label = popup.GetComponent<UILabel>();
        if (label == null)
            label = popup.GetComponentInChildren<UILabel>(true);
        if (label != null)
        {
            label.text = text;
            label.fontSize = OptionFontSize;
        }
    }

    void UpdateSelectedDeckDisplays()
    {
        string player = Config.Get("deckInUse", "");
        string ai = Config.Get("list_aideck", "RadiantTyphoon");

        SetReadOnlyDisplay(playerDeckDisplay, "自分: " + player);
        SetReadOnlyDisplay(aiDeckDisplay, "AI: " + ai);
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
        SetSetupScreenVisible(true);

        if (windbot != null)
        {
            windbot.Dispose();
            windbot = null;
        }

        base.show();
        LoadDeckNames();
        PopulatePlayerDeckList();
        PopulateAiDeckList();
        UpdateSelectedDeckDisplays();
        Program.charge();
    }

    public override void preFrameFunction()
    {
        base.preFrameFunction();
        Menu.checkCommend();
    }
}
