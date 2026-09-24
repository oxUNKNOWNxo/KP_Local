using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using KoishiWindBot.Local;

public sealed class KoishiWindBotBridge
{
    private RadiantTyphoonLocalDuel duel;

    public void Dispose()
    {
        if (duel != null)
        {
            duel.Dispose();
            duel = null;
        }
    }

    public bool StartAI(string playerDeckPath, bool playerGoFirst, bool noShuffle, int life)
    {
        if (Program.I().ocgcore.isShowed)
            return false;

        try
        {
            List<int> main = new List<int>();
            List<int> extra = new List<int>();
            LoadPlayerDeck(playerDeckPath, main, extra);

            string dbPath = File.Exists("cdb/cards.cdb") ? "cdb/cards.cdb" : "cards.cdb";
            if (!File.Exists(dbPath))
                throw new FileNotFoundException("cards.cdb not found", dbPath);

            string dataRoot = Path.Combine(Application.streamingAssetsPath, "WindBotData");
            if (!Directory.Exists(dataRoot))
                throw new DirectoryNotFoundException("WindBotData not found: " + dataRoot);

            PrepareOcgcore(playerGoFirst, life);

            duel = new RadiantTyphoonLocalDuel(
                dataRoot,
                dbPath,
                ReceiveHumanGameMessage,
                message => Program.DEBUGLOG("[WindBot] " + message),
                playerGoFirst);

            duel.Start(main, extra, noShuffle, life, 5, 1, 5);
            Program.I().shiftToServant(Program.I().ocgcore);
            return true;
        }
        catch (Exception e)
        {
            Program.DEBUGLOG("[WindBot] start failed: " + e);
            Program.I().cardDescription.RMSshow_none("WindBot AIの起動に失敗しました。\n" + e.Message);
            Dispose();
            return false;
        }
    }

    private void PrepareOcgcore(bool playerGoFirst, int life)
    {
        Program.I().room.mode = 0;
        Program.I().ocgcore.MasterRule = 5;
        Program.I().ocgcore.lpLimit = life;
        Program.I().ocgcore.isFirst = playerGoFirst;
        Program.I().ocgcore.returnServant = Program.I().aiRoom;
        Program.I().ocgcore.name_0 = Config.Get("name", "Player");
        Program.I().ocgcore.name_0_c = Program.I().ocgcore.name_0;
        Program.I().ocgcore.name_1 = "WindBot - Radiant Typhoon";
        Program.I().ocgcore.name_1_c = Program.I().ocgcore.name_1;
        Program.I().ocgcore.name_0_tag = "---";
        Program.I().ocgcore.name_1_tag = "---";
        Program.I().ocgcore.timeLimit = 240;
        Program.I().ocgcore.handler = Response;
        Program.I().ocgcore.shiftCondition(Ocgcore.Condition.watch);
        Program.I().ocgcore.InAI = true;
    }

    private void Response(byte[] response)
    {
        if (duel == null)
            return;
        try
        {
            duel.SubmitHumanResponse(response);
        }
        catch (Exception e)
        {
            Program.DEBUGLOG("[WindBot] response failed: " + e);
            Program.I().cardDescription.RMSshow_none("WindBot AI対戦中にエラーが発生しました。\n" + e.Message);
        }
    }

    private void ReceiveHumanGameMessage(byte[] gameMessage)
    {
        byte[] framed = new byte[gameMessage.Length + 1];
        framed[0] = 1; // STOC_GAME_MSG
        Buffer.BlockCopy(gameMessage, 0, framed, 1, gameMessage.Length);
        TcpHelper.addDateJumoLine(framed);
    }

    private static void LoadPlayerDeck(string path, List<int> main, List<int> extra)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Player deck not found", path);

        bool side = false;
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (line == "!side")
            {
                side = true;
                continue;
            }
            if (line[0] == '#')
                continue;
            if (side)
                continue;

            int id;
            if (!Int32.TryParse(line, out id))
                continue;
            YGOSharp.Card card = YGOSharp.CardsManager.GetCard(id);
            if (card == null)
                continue;
            if (card.IsExtraCard())
                extra.Add(id);
            else
                main.Add(id);
        }

        if (main.Count == 0)
            throw new InvalidDataException("Player deck has no usable main-deck cards.");
    }
}
