using System;
using System.IO;
using UnityEngine;

public static class AIBootstrap
{
    private const string BundledPackName = "koishi-ai-pack.zip";

    public static bool EnsureInstalled()
    {
        try
        {
            Directory.CreateDirectory("ai");
            Directory.CreateDirectory("ai/ydk");
            Directory.CreateDirectory("script");

            if (HasUsableAiData())
            {
                return true;
            }

            string packPath = Path.Combine(Application.streamingAssetsPath, BundledPackName);
            if (!File.Exists(packPath))
            {
                Program.DEBUGLOG("Bundled AI pack not found: " + packPath);
                return false;
            }

            byte[] data = File.ReadAllBytes(packPath);
            Program.I().ExtractZipFile(data, Directory.GetCurrentDirectory());

            bool usable = HasUsableAiData();
            if (!usable)
            {
                Program.DEBUGLOG("Bundled AI pack extracted but required AI/card scripts are still missing.");
            }
            return usable;
        }
        catch (Exception e)
        {
            Program.DEBUGLOG("Failed to install bundled AI pack: " + e);
            return false;
        }
    }

    private static bool HasUsableAiData()
    {
        if (!Directory.Exists("ai") || !Directory.Exists("ai/ydk") || !Directory.Exists("script"))
        {
            return false;
        }

        return File.Exists("ai/ai.lua")
            && Directory.GetFiles("ai/ydk", "*.ydk", SearchOption.TopDirectoryOnly).Length > 0
            && File.Exists("script/constant.lua")
            && File.Exists("script/utility.lua")
            && Directory.GetFiles("script", "c*.lua", SearchOption.TopDirectoryOnly).Length > 1000;
    }
}
