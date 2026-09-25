using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

public class AIRoom : WindowServantSP
{
    const int DefaultLife = 8000;
    const int OptionFontSize = 26;
    const int DeckListFontSize = 24;
    const string PlayerDeckMode = "自分のデッキ";
    const string AiDeckMode = "AIデッキ";
    const string LocalRpsHash = "WindBot_LocalRps";
    const string LocalTurnChoiceHash = "WindBot_LocalTurnChoice";

    UIselectableList superScrollView = null;
    string sort = "sortByTimeDeck";
    UIPopupList list_aideck;
    UIPopupList list_airank;
    KoishiWindBotBridge windbot;
    string[] aiDeckNames = new string[0];
    List<string> playerDeckNames = new List<string>();
    bool showingAiDecks;

    bool pregamePending;
    string pendingPlayerDeck;
    string pendingAiDeck;
    bool pendingNoShuffle;

    public override void initialize()
    {
        createWindow(Program.I().new_ui_aiRoom);
        superScrollView = gameObject.GetComponentInChildren<UIselectableList>();
        superScrollView.selectedAction = onSelected;
        list_aideck = UIHelper.getByName<UIPopupList>(gameObject, "aideck_");
        list_airank = UIHelper.getByName<UIPopupList>(gameObject, "rank_");

        SimplifyWindBotOptions();
        ConfigureDeckListModeSelector();
        EnlargeDeckListTemplate();

        UIHelper.registEvent(gameObject, "rank_", onListModeChanged);
        UIHelper.registEvent(gameObject, "start_", onStart);
        UIHelper.registEvent(gameObject, "exit_", () => { Program.I().shiftToServant(Program.I().menu); });
        UpdateHeader();

        superScrollView.install();
        if (superScrollView.mod != null)
        {
            Vector3 prototypePosition = superScrollView.mod.transform.localPosition;
            prototypePosition.y = 100000f;
            superScrollView.mod.transform.localPosition = prototypePosition;
        }
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

    void SetControlLabel(string name, string text, int fontSize = 0)
    {
        UILabel label = FindControlLabel(name);
        if (label == null)
            return;

        label.text = text;
        if (fontSize > 0)
            label.fontSize = fontSize;
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

    void SimplifyWindBotOptions()
    {
        Transform life = FindControl("life_");
        Transform unrand = FindControl("unrand_");
        Transform first = FindControl("first_");

        if (life != null && unrand != null && first != null)
        {
            float spacing = first.localPosition.y - unrand.localPosition.y;

            Vector3 p = unrand.localPosition;
            p.y = life.localPosition.y;
            unrand.localPosition = p;

            p = first.localPosition;
            p.y = life.localPosition.y + spacing;
            first.localPosition = p;
        }

        HideControl("life_");
        HideControl("mr4_");
        HideControl("god_");
        HideControl("aideck_");

        SetControlLabel("unrand_", "シャッフルしない", OptionFontSize);
        SetControlLabel("first_", "自分が先攻", OptionFontSize);
    }

    void ConfigureDeckListModeSelector()
    {
        if (list_airank == null)
            return;

        list_airank.Clear();
        list_airank.AddItem(PlayerDeckMode);
        list_airank.AddItem(AiDeckMode);

        string saved = Config.Get("aiDeckListMode", PlayerDeckMode);
        showingAiDecks = String.Equals(saved, AiDeckMode, StringComparison.Ordinal);
        list_airank.value = showingAiDecks ? AiDeckMode : PlayerDeckMode;
        list_airank.fontSize = OptionFontSize;
    }

    void EnlargeDeckListTemplate()
    {
        if (superScrollView == null || superScrollView.mod == null)
            return;

        UILabel label = superScrollView.mod.GetComponentInChildren<UILabel>(true);
        if (label != null)
            label.fontSize = Math.Max(label.fontSize, DeckListFontSize);
    }

    void UpdateHeader()
    {
        string aiDeck = Config.Get("list_aideck", "RadiantTyphoon");
        UIHelper.trySetLableText(gameObject, "percyHint", "WindBot AIモード  /  AI: " + aiDeck);
    }

    void onSelected()
    {
        if (String.IsNullOrWhiteSpace(superScrollView.selectedString))
            return;

        if (showingAiDecks)
        {
            Config.Set("list_aideck", superScrollView.selectedString);
            UpdateHeader();
        }
        else
        {
            Config.Set("deckInUse", superScrollView.selectedString);
        }
    }

    void onListModeChanged()
    {
        if (list_airank == null || String.IsNullOrWhiteSpace(list_airank.value))
            return;

        bool nextAi = String.Equals(list_airank.value, AiDeckMode, StringComparison.Ordinal);
        if (showingAiDecks == nextAi && superScrollView.Selected())
            return;

        showingAiDecks = nextAi;
        Config.Set("aiDeckListMode", showingAiDecks ? AiDeckMode : PlayerDeckMode);
        PopulateDeckList();
    }

    void PopulateDeckList()
    {
        superScrollView.clear();

        if (showingAiDecks)
        {
            string selectedAi = Config.Get("list_aideck", "RadiantTyphoon");
            for (int i = 0; i < aiDeckNames.Length; ++i)
                superScrollView.add(aiDeckNames[i]);

            if (Array.IndexOf(aiDeckNames, selectedAi) < 0 && aiDeckNames.Length > 0)
            {
                selectedAi = aiDeckNames[0];
                Config.Set("list_aideck", selectedAi);
            }

            superScrollView.selectedString = selectedAi;
        }
        else
        {
            string selectedPlayer = Config.Get("deckInUse", "miaowu");
            for (int i = 0; i < playerDeckNames.Count; ++i)
                superScrollView.add(playerDeckNames[i]);

            if (!playerDeckNames.Contains(selectedPlayer) && playerDeckNames.Count > 0)
            {
                selectedPlayer = playerDeckNames[0];
                Config.Set("deckInUse", selectedPlayer);
            }

            superScrollView.selectedString = selectedPlayer;
        }

        superScrollView.toTop();
        superScrollView.mark();
        UpdateHeader();
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

        string savedMode = Config.Get("aiDeckListMode", PlayerDeckMode);
        showingAiDecks = String.Equals(savedMode, AiDeckMode, StringComparison.Ordinal);
        if (list_airank != null)
            list_airank.value = showingAiDecks ? AiDeckMode : PlayerDeckMode;

        PopulateDeckList();
        Program.charge();
    }

    public override void preFrameFunction()
    {
        base.preFrameFunction();
        Menu.checkCommend();
    }
}
