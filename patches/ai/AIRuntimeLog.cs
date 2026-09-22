using System;
using System.IO;
using UnityEngine;

public static class AIRuntimeLog
{
    private static readonly object Sync = new object();
    private const string FileName = "offline-ai-runtime.log";
    private static int shownMessages;
    private static string lastMessage = "";
    private static int repeatCount;

    public static string LogPath
    {
        get { return Path.Combine(Directory.GetCurrentDirectory(), FileName); }
    }

    public static void ResetSession()
    {
        lock (Sync)
        {
            shownMessages = 0;
            lastMessage = "";
            repeatCount = 0;
            try
            {
                int cardScriptCount = Directory.Exists("script")
                    ? Directory.GetFiles("script", "c*.lua", SearchOption.TopDirectoryOnly).Length
                    : 0;
                File.WriteAllText(
                    LogPath,
                    "KoishiPro2 offline AI runtime log\n" +
                    "time=" + DateTime.Now.ToString("O") + "\n" +
                    "cwd=" + Directory.GetCurrentDirectory() + "\n" +
                    "script/constant.lua=" + File.Exists("script/constant.lua") + "\n" +
                    "script/utility.lua=" + File.Exists("script/utility.lua") + "\n" +
                    "cardScripts=" + cardScriptCount + "\n" +
                    "ai/ai.lua=" + File.Exists("ai/ai.lua") + "\n\n"
                );
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OfflineAI] Failed to reset runtime log: " + e.Message);
            }
        }
    }

    public static void Write(string message)
    {
        if (String.IsNullOrEmpty(message))
        {
            return;
        }

        lock (Sync)
        {
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + "\n");
            }
            catch
            {
            }
        }
    }

    public static bool ShouldShowToChat(string message)
    {
        lock (Sync)
        {
            if (message == lastMessage)
            {
                repeatCount++;
            }
            else
            {
                lastMessage = message;
                repeatCount = 1;
            }

            if (shownMessages >= 8 || repeatCount > 2)
            {
                return false;
            }

            shownMessages++;
            return true;
        }
    }
}
