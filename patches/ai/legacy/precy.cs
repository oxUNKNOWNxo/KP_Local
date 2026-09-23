using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using YGOSharp.OCGWrapper.Enums;

public class PrecyOcg
{
    public static string HintInGame = Percy.smallYgopro.HintInGame;
    public static bool godMode = false;

    public Percy.smallYgopro ygopro;

    private int visibleCoreErrorCount = 0;
    private string lastCoreError = null;

    public PrecyOcg()
    {
        ygopro = new Percy.smallYgopro(receiveHandler, cardHandler, chatHandler);
        ygopro.m_log = (a) => { Program.DEBUGLOG(a); };
    }

    public void dispose()
    {
        ygopro.dispose();
    }

    object locker = new object();
    void receiveHandler(byte[] buffer)
    {
        byte[] bufferR = new byte[buffer.Length + 1];
        bufferR[0] = 1;
        buffer.CopyTo(bufferR,1);
        TcpHelper.addDateJumoLine(bufferR);
    }

    public void startPuzzle(System.String path)
    {
        if (Program.I().ocgcore.isShowed == false)
        {
            Program.I().room.mode = 0;
            Program.I().ocgcore.MasterRule = 3;
            godMode = true;
            visibleCoreErrorCount = 0;
            lastCoreError = null;
            prepareOcgcore();
            Program.I().ocgcore.isFirst = true;
            Program.I().ocgcore.returnServant = Program.I().puzzleMode;
            if (!ygopro.startPuzzle(path))
            {
                Program.I().cardDescription.RMSshow_none("AIスクリプトまたはデッキの読み込みに失敗しました。");
                return;
            }
            else
            {
                Program.I().shiftToServant(Program.I().ocgcore);
            }
        ((CardDescription)Program.I().cardDescription).setTitle(path);
        }
    }

    public void startAI(string playerDek, string aiDeck, string aiScript, bool playerGo, bool unrand, int life,bool god,int rule)
    {
        if (Program.I().ocgcore.isShowed == false)
        {
            Program.I().room.mode = 0;
            Program.I().ocgcore.MasterRule = rule;
            godMode = god;
            visibleCoreErrorCount = 0;
            lastCoreError = null;
            prepareOcgcore();
            Program.I().ocgcore.lpLimit = life;
            Program.I().ocgcore.isFirst = playerGo;
            Program.I().ocgcore.returnServant = Program.I().aiRoom;
            if (!ygopro.startAI(playerDek, aiDeck, aiScript, playerGo, unrand, life, god, rule))
            {
                Program.I().cardDescription.RMSshow_none("AIスクリプトまたはデッキの読み込みに失敗しました。");
                return;
            }
            else
            {
                Program.I().shiftToServant(Program.I().ocgcore);
            }
        }
    }

    private void prepareOcgcore()
    {
        Program.I().ocgcore.name_0 = Config.Get("name","一秒一喵机会");
        Program.I().ocgcore.name_0_c = Program.I().ocgcore.name_0;
        Program.I().ocgcore.name_1 = "Percy AI";
        Program.I().ocgcore.name_1_c = "Percy AI";
        Program.I().ocgcore.name_0_tag = "---";
        Program.I().ocgcore.name_1_tag = "---";
        Program.I().ocgcore.timeLimit = 240;
        Program.I().ocgcore.lpLimit = 8000;
        Program.I().ocgcore.handler = response;
        Program.I().ocgcore.shiftCondition(Ocgcore.Condition.watch);
        Program.I().ocgcore.InAI = true;
    }

    public void response(byte[] resp)
    {
        ygopro.response(resp);
    }

    Percy.CardData cardHandler(long code)
    {
        YGOSharp.Card card = YGOSharp.CardsManager.GetCard((int)code);
        if (card==null)
        {
            card = new YGOSharp.Card();
        }
        Percy.CardData retuvalue = new Percy.CardData();
        retuvalue.Alias = card.Alias;
        retuvalue.Attack = card.Attack;
        retuvalue.Attribute = card.Attribute;
        retuvalue.Code = card.Id;
        retuvalue.Defense = card.Defense;
        retuvalue.Level = card.Level;
        retuvalue.LScale = card.LScale;
        retuvalue.Race = card.Race;
        retuvalue.RScale = card.RScale;
        retuvalue.Setcode = card.Setcode;
        retuvalue.Type = card.Type;
        retuvalue.LinkMarker = card.LinkMarker;
        return retuvalue;
    }

    void chatHandler(string result)
    {
        if (String.IsNullOrEmpty(result))
        {
            return;
        }

        // The legacy core reports Lua/script failures through this same chat
        // callback.  Older code expanded every "Error Occurred." into a long
        // repeated sentence, turning one bad script into an unusable flood of
        // messages.  Keep the exact native message in the debug log and show at
        // most three distinct runtime errors on screen.
        bool isRuntimeError = result.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
            || result.IndexOf("script", StringComparison.OrdinalIgnoreCase) >= 0
            || result.IndexOf("lua", StringComparison.OrdinalIgnoreCase) >= 0;

        if (isRuntimeError)
        {
            Program.DEBUGLOG("[OfflineAI/Core] " + result);
            if (result == lastCoreError || visibleCoreErrorCount >= 3)
            {
                return;
            }
            lastCoreError = result;
            visibleCoreErrorCount++;
            result = "[AI] " + result;
        }

        BinaryMaster p = new BinaryMaster();
        p.writer.Write((byte)YGOSharp.OCGWrapper.Enums.GameMessage.sibyl_chat);
        p.writer.WriteUnicode(result, result.Length + 1);
        receiveHandler(p.get());
    }
}
