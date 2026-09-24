using System;
using System.IO;
using UnityEngine;

public static class MainScriptBootstrap
{
    private const string BundledRevisionName = "koishi-main-scripts-revision.txt";
    private const string BundledUpdateName = "koishi-main-script-update.zip";
    private const string InstalledRevisionPath = "updates/koishi-main-scripts-revision.txt";
    private static bool checkedThisSession = false;

    public static bool EnsureInstalled()
    {
        if (checkedThisSession)
        {
            return true;
        }

        try
        {
            string bundledRevisionPath =
                Path.Combine(Application.streamingAssetsPath, BundledRevisionName);
            if (!File.Exists(bundledRevisionPath))
            {
                checkedThisSession = true;
                return true;
            }

            string expectedRevision = File.ReadAllText(bundledRevisionPath).Trim();
            if (String.IsNullOrEmpty(expectedRevision))
            {
                Program.DEBUGLOG("[MainScripts] Bundled revision marker is empty.");
                return false;
            }

            string installedRevision = "";
            if (File.Exists(InstalledRevisionPath))
            {
                installedRevision = File.ReadAllText(InstalledRevisionPath).Trim();
            }

            if (installedRevision == expectedRevision)
            {
                checkedThisSession = true;
                return HasCoreScripts();
            }

            string updatePath =
                Path.Combine(Application.streamingAssetsPath, BundledUpdateName);
            if (!File.Exists(updatePath))
            {
                Program.DEBUGLOG("[MainScripts] Update pack not found: " + updatePath);
                return false;
            }

            byte[] data = File.ReadAllBytes(updatePath);
            Program.I().ExtractZipFile(data, Directory.GetCurrentDirectory());

            Directory.CreateDirectory("updates");
            File.WriteAllText(InstalledRevisionPath, expectedRevision + "\n");

            bool ok = HasCoreScripts();
            checkedThisSession = ok;
            Program.DEBUGLOG(
                "[MainScripts] synchronized=" + ok
                + " revision=" + expectedRevision
            );
            return ok;
        }
        catch (Exception e)
        {
            Program.DEBUGLOG("[MainScripts] synchronization failed: " + e);
            return false;
        }
    }

    private static bool HasCoreScripts()
    {
        return File.Exists("script/constant.lua")
            && File.Exists("script/utility.lua");
    }
}
