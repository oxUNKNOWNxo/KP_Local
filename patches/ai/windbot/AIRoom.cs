using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

public class AIRoom : WindowServantSP
{
    const int DefaultLife = 8000;
    const int AiDecksPerPage = 8;
    const string LocalRpsHash = "WindBot_LocalRps";
    const string LocalTurnChoiceHash = "WindBot_LocalTurnChoice";

    UIselectableList superScrollView = null;
    string sort = "sortByTimeDeck";
    UIPopupList list_aideck;
    UIPopupList list_airank;
    KoishiWindBotBridge windbot;
    string[] aiDeckNames = new string[0];
    int aiDeckPage = 0;

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

        UIHelper.registEvent(gameObject, "aideck_", onSave);
        UIHelper.registEvent(gameObject, "rank_", onSave);
        UIHelper.registEvent(gameObject, "start_", onStart);
        UIHelper.registEvent(gameObject, "exit_", () => { Program.I().shiftToServant(Program.I().menu); });
        UIHelper.trySetLableText(gameObject, "percyHint", "WindBot AIモード");
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

    void SetControlLabel(string name, string text)
    {
        Transform control = FindControl(name);
        if (control == null)
            return;

        UILabel label = control.GetComponent<UILabel>();
        if (label == null)
            label = control.GetComponentInChildren<UILabel>(true);
        if (label != null)
            label.text = text;
    }

    void HideControl(string name)
    {
        Transform control = FindControl(name);
        if (control != null)
            control.gameObject.SetActive(false);
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

        SetControlLabel("unrand_", "シャッフルしない");
        SetControlLabel("first_", "自分が先攻（OFFでじゃんけん）");
    }

    void onSelected()
    {
        Config.Set("deckInUse", superScrollView.selectedString);
    }

    void onSave()
    {
        if (list_airank != null && !String.IsNullOrWhiteSpace(list_airank.value))
        {
            string nav = list_airank.value;
            if (nav == "前のAI")
            {
                aiDeckPage = Math.Max(0, aiDeckPage - 1);
                RebuildAiDeckPage();
                RebuildAiPageNavigator();
                return;
            }
            if (nav == "次のAI")
            {
                aiDeckPage = Math.Min(AiDeckPageCount() - 1, aiDeckPage + 1);
                RebuildAiDeckPage();
                RebuildAiPageNavigator();
                return;
            }
        }

        if (list_aideck != null && !String.IsNullOrWhiteSpace(list_aideck.value))
            Config.Set("list_aideck", list_aideck.value);
    }

    int AiDeckPageCount()
    {
        return Math.Max(1, (aiDeckNames.Length + AiDecksPerPage - 1) / AiDecksPerPage);
    }

    string FormatAiDeckPage(int page)
    {
        return "AI " + (page + 1) + "/" + AiDeckPageCount();
    }

    void RebuildAiPageNavigator()
    {
        if (list_airank == null)
            return;

        list_airank.Clear();
        if (aiDeckPage > 0)
            list_airank.AddItem("前のAI");

        string current = FormatAiDeckPage(aiDeckPage);
        list_airank.AddItem(current);

        if (aiDeckPage + 1 < AiDeckPageCount())
            list_airank.AddItem("次のAI");

        list_airank.value = current;
        Config.Set("list_airank", current);
    }

    void RebuildAiDeckPage()
    {
        if (list_aideck == null)
            return;

        string selectedAiDeck = Config.Get("list_aideck", "RadiantTyphoon");
        int totalPages = AiDeckPageCount();
        aiDeckPage = Math.Max(0, Math.Min(totalPages - 1, aiDeckPage));

        int start = aiDeckPage * AiDecksPerPage;
        int end = Math.Min(aiDeckNames.Length, start + AiDecksPerPage);

        list_aideck.Clear();
        bool selectedExists = false;
        for (int i = start; i < end; ++i)
        {
            list_aideck.AddItem(aiDeckNames[i]);
            if (String.Equals(aiDeckNames[i], selectedAiDeck, StringComparison.Ordinal))
                selectedExists = true;
        }

        if (!selectedExists && start < end)
            selectedAiDeck = aiDeckNames[start];

        if (!String.IsNullOrEmpty(selectedAiDeck))
        {
            list_aideck.value = selectedAiDeck;
            Config.Set("list_aideck", selectedAiDeck);
        }
    }

    void onStart()
    {
        if (!isShowed || pregamePending)
            return;

        pendingPlayerDeck = "deck/" + Config.Get("deckInUse", "miaowu") + ".ydk";
        pendingAiDeck = list_aideck == null ? "" : list_aideck.value;
        if (String.IsNullOrWhiteSpace(pendingAiDeck))
            pendingAiDeck = Config.Get("list_aideck", "RadiantTyphoon");

        pendingNoShuffle = UIHelper.getByName<UIToggle>(gameObject, "unrand_").value;
        bool forcePlayerFirst = UIHelper.getByName<UIToggle>(gameObject, "first_").value;

        pregamePending = true;
        if (forcePlayerFirst)
            StartPendingDuel(true);
        else
            ShowLocalRockPaperScissors();
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

    void printFile()
    {
        Directory.CreateDirectory("deck");
        string deckInUse = Config.Get("deckInUse", "miaowu");
        superScrollView.clear();
        FileInfo[] files = (new DirectoryInfo("deck")).GetFiles("*.ydk");
        Array.Sort(files, Config.Get(sort, "1") == "1" ? UIHelper.CompareTime : UIHelper.CompareName);

        List<string> deckNames = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < files.Length; ++i)
        {
            string name = Path.GetFileNameWithoutExtension(files[i].Name);
            if (String.IsNullOrWhiteSpace(name) || !seen.Add(name))
                continue;
            deckNames.Add(name);
        }

        if (String.IsNullOrWhiteSpace(deckInUse) || !seen.Contains(deckInUse))
        {
            deckInUse = deckNames.Count > 0 ? deckNames[0] : "";
            Config.Set("deckInUse", deckInUse);
        }

        if (!String.IsNullOrEmpty(deckInUse))
            superScrollView.add(deckInUse);
        for (int i = 0; i < deckNames.Count; ++i)
        {
            if (!String.Equals(deckNames[i], deckInUse, StringComparison.Ordinal))
                superScrollView.add(deckNames[i]);
        }

        WindBot.Game.AI.DecksManager.Init();
        aiDeckNames = WindBot.Game.AI.DecksManager.GetDeckNames();
        string selectedAiDeck = Config.Get("list_aideck", "RadiantTyphoon");

        int selectedIndex = Array.IndexOf(aiDeckNames, selectedAiDeck);
        if (selectedIndex >= 0)
            aiDeckPage = selectedIndex / AiDecksPerPage;
        else
            aiDeckPage = 0;

        RebuildAiPageNavigator();
        RebuildAiDeckPage();
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
        printFile();
        string selectedDeck = Config.Get("deckInUse", "");
        if (!String.IsNullOrWhiteSpace(selectedDeck))
            superScrollView.selectedString = selectedDeck;
        superScrollView.toTop();
        Program.charge();
    }

    public override void preFrameFunction()
    {
        base.preFrameFunction();
        Menu.checkCommend();
    }
}
