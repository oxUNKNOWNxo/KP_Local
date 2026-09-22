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
            return HasUsableAiData();
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

        return Directory.GetFiles("ai", "*.lua", SearchOption.TopDirectoryOnly).Length > 0
            && Directory.GetFiles("ai/ydk", "*.ydk", SearchOption.TopDirectoryOnly).Length > 0;
    }
}
