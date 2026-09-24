using System;
using System.IO;
using UnityEngine;

public class AIRoom : WindowServantSP
{
    UIselectableList superScrollView = null;
    string sort = "sortByTimeDeck";
    UIPopupList list_aideck;
    UIPopupList list_airank;
    KoishiWindBotBridge windbot;

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
        UIHelper.trySetLableText(gameObject, "percyHint", "WindBot AIモード（実証版：絢嵐）");
        superScrollView.install();
        SetActiveFalse();
    }

    void onSelected()
    {
        Config.Set("deckInUse", superScrollView.selectedString);
    }

    void onSave()
    {
        Config.Set("list_aideck", "RadiantTyphoon");
        Config.Set("list_airank", "WindBot");
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
        bool playerGoFirst = UIHelper.getByName<UIToggle>(gameObject, "first_").value;
        bool noShuffle = UIHelper.getByName<UIToggle>(gameObject, "unrand_").value;

        if (windbot != null)
            windbot.Dispose();
        windbot = new KoishiWindBotBridge();

        if (windbot.StartAI(playerDeck, playerGoFirst, noShuffle, life))
            RMSshow_none("WindBot実証版：絢嵐デッキで開始します。");
    }

    void printFile()
    {
        Directory.CreateDirectory("deck");
        string deckInUse = Config.Get("deckInUse", "miaowu");
        superScrollView.clear();
        FileInfo[] files = (new DirectoryInfo("deck")).GetFiles("*.ydk");
        Array.Sort(files, Config.Get(sort, "1") == "1" ? UIHelper.CompareTime : UIHelper.CompareName);

        for (int pass = 0; pass < 2; ++pass)
        {
            for (int i = 0; i < files.Length; ++i)
            {
                string name = Path.GetFileNameWithoutExtension(files[i].Name);
                bool selected = name == deckInUse;
                if ((pass == 0 && selected) || (pass == 1 && !selected))
                    superScrollView.add(name);
            }
        }

        list_aideck.Clear();
        list_aideck.AddItem("RadiantTyphoon");
        list_aideck.value = "RadiantTyphoon";

        list_airank.Clear();
        list_airank.AddItem("WindBot");
        list_airank.value = "WindBot";
    }

    public override void show()
    {
        base.show();
        printFile();
        superScrollView.selectedString = Config.Get("deckInUse", "miaowu");
        superScrollView.toTop();
        Program.charge();
    }

    public override void preFrameFunction()
    {
        base.preFrameFunction();
        Menu.checkCommend();
    }
}
