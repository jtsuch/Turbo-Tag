using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// One-click Windows playtest build: stamps a version (0.1.MMdd.HHmm, shown on the main menu and in every log and bug
/// report), builds every scene in Build Settings to Builds/TurboTag-{version}/TurboTag.exe and zips it to
/// Builds/TurboTag-{version}.zip, ready to send to friends. The result goes to Library/TurboTag/scratch/build_result.txt.
/// Menu: Turbo Tag/Build/Windows Playtest Build (editor job: BuildWindows).
/// </summary>
public static class BuildTools
{
    private const string BuildsFolder = "Builds";
    private const string ResultPath   = "Library/TurboTag/scratch/build_result.txt";

    [MenuItem("Turbo Tag/Build/Windows Playtest Build")]
    public static void BuildWindows()
    {
        LevelTools.SyncBuildSettings();
        string version = $"0.1.{DateTime.Now:MMdd.HHmm}";
        PlayerSettings.bundleVersion = version;
        AssetDatabase.SaveAssets();

        string folder = Path.Combine(BuildsFolder, $"TurboTag-{version}");
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(folder, "TurboTag.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        string result;
        if (summary.result == BuildResult.Succeeded)
        {
            // The "DoNotShip" debug folder isn't needed by players
            foreach (var dir in Directory.GetDirectories(folder, "*DoNotShip*")) Directory.Delete(dir, true);
            string zip = folder + ".zip";
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(folder, zip, System.IO.Compression.CompressionLevel.Optimal, false);
            result = $"OK v{version}: {Path.GetFullPath(zip)} ({new FileInfo(zip).Length / (1024f * 1024f):0} MB, {summary.totalTime.TotalMinutes:0.0} min)";
        }
        else
        {
            var errors = report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                               .Select(m => m.content).Take(20);
            result = $"FAILED ({summary.result}), {summary.totalErrors} errors:\n" + string.Join("\n", errors);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
        File.WriteAllText(ResultPath, result);
        Debug.Log("[BuildTools] " + result);
    }
}
