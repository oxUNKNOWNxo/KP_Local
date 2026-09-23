using System;
using System.IO;
using UnityEngine;

public static class AIBootstrap
{
    private const string BundledPackName = "koishi-ai-pack.zip";
    private const string PackMarker = "ai/KOISHIPRO2-AI-PACK-V3.txt";

    public static bool EnsureInstalled()
    {
        try
        {
            Directory.CreateDirectory("ai");
            Directory.CreateDirectory("ai/ydk");
            Directory.CreateDirectory("script");

            // V3 keeps bundled script sets separate from the user's live script
            // folder.  Existing/custom scripts always win; bundled files only
            // fill gaps and are never allowed to overwrite them.
            if (!File.Exists(PackMarker)
                || !Directory.Exists("script_current")
                || !Directory.Exists("script_legacy"))
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

            int currentAdded = CopyMissingScripts("script_current", "script");
            int legacyAdded = CopyMissingScripts("script_legacy", "script");
            bool ok = HasUsableAiData();
            Program.DEBUGLOG(
                "[OfflineAI] runtime scripts ready=" + ok
                + " currentAdded=" + currentAdded
                + " legacyAdded=" + legacyAdded
            );
            return ok;
        }
        catch (Exception e)
        {
            Program.DEBUGLOG("Failed to install bundled AI pack: " + e);
            return false;
        }
    }

    private static int CopyMissingScripts(string sourceDir, string destinationDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            return 0;
        }

        int copied = 0;
        string[] files = Directory.GetFiles(sourceDir, "*.lua", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < files.Length; i++)
        {
            string source = files[i];
            string destination = Path.Combine(destinationDir, Path.GetFileName(source));
            if (File.Exists(destination))
            {
                continue;
            }

            File.Copy(source, destination, false);
            copied++;
        }
        return copied;
    }

    private static bool HasUsableAiData()
    {
        if (!Directory.Exists("ai") || !Directory.Exists("ai/ydk") || !Directory.Exists("script"))
        {
            return false;
        }

        return File.Exists(PackMarker)
            && File.Exists("ai/ai.lua")
            && Directory.GetFiles("ai/ydk", "*.ydk", SearchOption.TopDirectoryOnly).Length > 0
            && File.Exists("script/constant.lua")
            && File.Exists("script/utility.lua")
            && Directory.GetFiles("script", "c*.lua", SearchOption.TopDirectoryOnly).Length > 1000;
    }
}
