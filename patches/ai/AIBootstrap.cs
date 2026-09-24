using System;
using System.IO;
using UnityEngine;

public static class AIBootstrap
{
    private const string BundledPackName = "koishi-ai-pack.zip";
    private const string PackMarker = "ai/KOISHIPRO2-AI-PACK-V5.txt";
    private static bool readyThisSession = false;

    public static bool EnsureInstalled()
    {
        if (readyThisSession)
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory("ai");
            Directory.CreateDirectory("ai/ydk");
            Directory.CreateDirectory("script");

            if (!File.Exists(PackMarker) || !File.Exists("ai/ai.lua"))
            {
                string packPath = Path.Combine(Application.streamingAssetsPath, BundledPackName);
                if (!File.Exists(packPath))
                {
                    Program.DEBUGLOG("Bundled AI pack not found: " + packPath);
                    return false;
                }

                byte[] data = File.ReadAllBytes(packPath);
                Program.I().ExtractZipFile(data, Directory.GetCurrentDirectory());
            }

            bool ok = HasUsableAiData();
            readyThisSession = ok;
            Program.DEBUGLOG("[OfflineAI] AI-specific data ready=" + ok);
            return ok;
        }
        catch (Exception e)
        {
            Program.DEBUGLOG("Failed to install bundled AI pack: " + e);
            return false;
        }
    }

    private static bool HasUsableAiData()
    {
        if (!Directory.Exists("ai") || !Directory.Exists("ai/ydk"))
        {
            return false;
        }

        return File.Exists(PackMarker)
            && File.Exists("ai/ai.lua")
            && Directory.GetFiles("ai/ydk", "*.ydk", SearchOption.TopDirectoryOnly).Length > 0
            && File.Exists("script/constant.lua")
            && File.Exists("script/utility.lua");
    }
}
