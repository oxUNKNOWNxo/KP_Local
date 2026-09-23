using System;
using System.IO;
using UnityEngine;

public static class AIBootstrap
{
    private const string BundledPackName = "koishi-ai-pack.zip";
    private const string PackMarker = "ai/KOISHIPRO2-AI-PACK-V4.txt";

    public static bool EnsureInstalled()
    {
        try
        {
            Directory.CreateDirectory("ai");
            Directory.CreateDirectory("ai/ydk");
            Directory.CreateDirectory("script");

            // V4 ships a complete snapshot of the current official card scripts.
            // script_current is authoritative for the bundled official files;
            // the historical set is used only when the current snapshot does not
            // contain a file required by the restored Percy/ocgcore runtime.
            if (!File.Exists(PackMarker)
                || !Directory.Exists("script_current")
                || !Directory.Exists("script_legacy"))
            {
                if (Directory.Exists("script_current"))
                {
                    Directory.Delete("script_current", true);
                }
                if (Directory.Exists("script_legacy"))
                {
                    Directory.Delete("script_legacy", true);
                }

                string packPath = Path.Combine(Application.streamingAssetsPath, BundledPackName);
                if (!File.Exists(packPath))
                {
                    Program.DEBUGLOG("Bundled AI pack not found: " + packPath);
                    return false;
                }

                byte[] data = File.ReadAllBytes(packPath);
                Program.I().ExtractZipFile(data, Directory.GetCurrentDirectory());
            }

            int currentCopied = CopyScripts("script_current", "script", true);
            int legacyAdded = CopyScripts("script_legacy", "script", false);
            bool ok = HasUsableAiData();
            Program.DEBUGLOG(
                "[OfflineAI] runtime scripts ready=" + ok
                + " currentCopied=" + currentCopied
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

    private static int CopyScripts(string sourceDir, string destinationDir, bool overwrite)
    {
        if (!Directory.Exists(sourceDir))
        {
            return 0;
        }

        int copied = 0;
        string sourcePrefix = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string[] files = Directory.GetFiles(sourceDir, "*.lua", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            string source = Path.GetFullPath(files[i]);
            string relative = source.Substring(sourcePrefix.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string destination = Path.Combine(destinationDir, relative);
            string parent = Path.GetDirectoryName(destination);
            if (!String.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            if (!overwrite && File.Exists(destination))
            {
                continue;
            }

            File.Copy(source, destination, overwrite);
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
            && Directory.GetFiles("script", "c*.lua", SearchOption.AllDirectories).Length > 1000;
    }
}
