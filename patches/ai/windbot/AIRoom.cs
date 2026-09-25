using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

public class AIRoom : WindowServantSP
{
    UIselectableList superScrollView = null;
    string sort = "sortByTimeDeck";
    UIPopupList list_aideck;
    UIPopupList list_airank;
    KoishiWindBotBridge windbot;
    string[] aiDeckNames = new string[0];
    const int AiDecksPerPage = 8;
    int aiDeckPage = 0;

    public override void initialize()
    {
        createWindow(Program.I().new_ui_aiRoom);
        superScrollView = gameObject.GetComponentInChildren<UIselectableList>();
        superScrollView.selectedAction = onSelected;
        list_aideck = UIHelper.getByName<UIPopupList>(gameObject, "aideck_");
        list_airank = UIHelper.getByName<UIPopupList>(gameObject, "rank_");
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
        if (!isShowed)
            return;

        int life = 8000;
        try
        {
            life = int.Parse(UIHelper.getByName<UIInput>(gameObject, "life_").value);
        }
        catch (Exception) { }

        string playerDeck = "deck/" + Config.Get("deckInUse", "miaowu") + ".ydk";
        string aiDeck = list_aideck == null ? "" : list_aideck.value;
        if (String.IsNullOrWhiteSpace(aiDeck))
            aiDeck = Config.Get("list_aideck", "RadiantTyphoon");

        bool playerGoFirst = UIHelper.getByName<UIToggle>(gameObject, "first_").value;
        bool noShuffle = UIHelper.getByName<UIToggle>(gameObject, "unrand_").value;

        if (windbot != null)
            windbot.Dispose();
        windbot = new KoishiWindBotBridge();

        if (windbot.StartAI(playerDeck, aiDeck, playerGoFirst, noShuffle, life))
            RMSshow_none("WindBot: " + aiDeck + " で開始します。");
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
