using System;
using System.IO;
using UnityEngine;

public static class AIBootstrap
{
    private const string BundledPackName = "koishi-ai-pack.zip";
    private const string PackMarker = "ai/KOISHIPRO2-AI-PACK-V2.txt";

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
            bool ok = HasUsableAiData();
            Program.DEBUGLOG("[OfflineAI] bundled AI/runtime script pack installed: " + ok);
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
        if (!Directory.Exists("ai") || !Directory.Exists("ai/ydk") || !Directory.Exists("script"))
        {
            return false;
        }

        // V2 deliberately requires the legacy card-script runtime as well as
        // the AI Lua itself.  Older test builds installed only ai/*, which let
        // the room open but caused the restored ocgcore to spam script errors
        // as soon as a duel started.
        return File.Exists(PackMarker)
            && File.Exists("ai/ai.lua")
            && Directory.GetFiles("ai/ydk", "*.ydk", SearchOption.TopDirectoryOnly).Length > 0
            && File.Exists("script/constant.lua")
            && File.Exists("script/utility.lua")
            && Directory.GetFiles("script", "c*.lua", SearchOption.TopDirectoryOnly).Length > 1000;
    }
}
