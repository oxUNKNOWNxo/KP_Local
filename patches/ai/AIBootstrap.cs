using System;
using System.IO;
using UnityEngine;

public static class AIBootstrap
{
    private const string BundledPackName = "koishi-ai-pack.zip";
    private const string PackMarker = "ai/KOISHIPRO2-AI-PACK-V4.txt";
    private const string RuntimeMarker = "ai/KOISHIPRO2-AI-RUNTIME-V4.txt";
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

            if (IsRuntimeReadyFast())
            {
                readyThisSession = true;
                return true;
            }

            bool hadExtractedPack =
                File.Exists(PackMarker)
                && Directory.Exists("script_current")
                && Directory.Exists("script_legacy");

            // Builds prior to this optimization already extracted and copied the
            // complete runtime, but had no runtime-ready marker. Adopt that
            // existing installation with a read-only count check instead of
            // rewriting thousands of Lua files again.
            if (hadExtractedPack && CanAdoptExistingRuntime())
            {
                MarkRuntimeReady();
                readyThisSession = true;
                return true;
            }

            // V4 ships a complete snapshot of the current official card scripts.
            // script_current is authoritative for the bundled official files;
            // the historical set is used only when the current snapshot does not
            // contain a file required by the restored Percy/ocgcore runtime.
            if (!hadExtractedPack)
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
            if (ok)
            {
                MarkRuntimeReady();
                readyThisSession = true;
            }
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

    private static bool IsRuntimeReadyFast()
    {
        return File.Exists(RuntimeMarker)
            && File.Exists(PackMarker)
            && File.Exists("ai/ai.lua")
            && File.Exists("script/constant.lua")
            && File.Exists("script/utility.lua");
    }

    private static bool CanAdoptExistingRuntime()
    {
        if (!File.Exists("ai/ai.lua")
            || !Directory.Exists("ai/ydk")
            || !File.Exists("script/constant.lua")
            || !File.Exists("script/utility.lua"))
        {
            return false;
        }

        string[] aiDecks = Directory.GetFiles("ai/ydk", "*.ydk", SearchOption.TopDirectoryOnly);
        if (aiDecks.Length == 0)
        {
            return false;
        }

        // This is intentionally a one-time migration check only. It is far
        // cheaper than copying the whole current script snapshot on every tap.
        string[] liveCardScripts = Directory.GetFiles("script", "c*.lua", SearchOption.AllDirectories);
        return liveCardScripts.Length > 1000;
    }

    private static void MarkRuntimeReady()
    {
        File.WriteAllText(
            RuntimeMarker,
            "KoishiPro2 offline AI runtime v4 ready\n"
        );
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
