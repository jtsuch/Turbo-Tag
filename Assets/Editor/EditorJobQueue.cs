using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Runs editor jobs requested from outside Unity (e.g. by Claude Code) so nobody has to click
/// through menus. Jobs are lines in Library/TurboTag/PendingEditorJobs.txt; the editor picks
/// them up automatically when it is idle and writes the outcome, including any errors logged
/// while running, to Library/TurboTag/EditorJobResults.txt.
///
/// Safety: jobs never run in Play mode or while compiling, and wait while any open scene has
/// unsaved changes (so your work is never discarded). Your open scenes are restored afterwards.
/// Add new jobs to <see cref="Jobs"/>.
/// </summary>
[InitializeOnLoad]
public static class EditorJobQueue
{
    private const string Folder      = "Library/TurboTag";
    private const string QueueFile   = Folder + "/PendingEditorJobs.txt";
    private const string ResultsFile = Folder + "/EditorJobResults.txt";
    private const double PollSeconds = 2.0;

    private static readonly Dictionary<string, Action> Jobs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SyncBuildSettings"] = LevelTools.SyncBuildSettings,
        ["UpgradeAllLevels"]  = LevelTools.UpgradeAllLevels,
        ["RebuildGreybox"]    = LevelTools.RebuildGreybox,
        ["Refresh"]           = AssetDatabase.Refresh,
        ["AssignSceneViewIds"] = LevelTools.AssignSceneViewIds,
        ["RemoveStraySpawners"] = LevelTools.RemoveStraySpawners,
        ["ProcessMeshes"] = MeshPrefabBuilder.ProcessMeshes,
        ["BuildImprovementExpansion"] = ImprovementExpansion.Build,
        ["BuildUITheme"] = UIThemeBuilder.Build,
        ["BuildGunPrefabs"] = GunPrefabBuilder.Build,
        ["BuildPinnacle"] = PinnacleBuilder.Build,
        ["BuildSkyreach"] = SkyreachBuilder.Build,
    };

    private static double nextPoll;
    private static bool   waitingNoticeShown;

    static EditorJobQueue() => EditorApplication.update += Poll;

    /// <summary>Queues a job by name to run when the editor is idle.</summary>
    public static void Enqueue(string job)
    {
        Directory.CreateDirectory(Folder);
        File.AppendAllText(QueueFile, job + Environment.NewLine);
    }

    [MenuItem("Turbo Tag/Run Pending Editor Jobs")]
    private static void RunNowMenu()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) RunPending();
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + PollSeconds;

        if (!File.Exists(QueueFile)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        if (HasUnsavedScenes())
        {
            if (!waitingNoticeShown)
                Debug.Log("[EditorJobQueue] Editor jobs are waiting — save your open scene(s) and they'll run automatically.");
            waitingNoticeShown = true;
            return;
        }

        RunPending();
    }

    private static void RunPending()
    {
        if (!File.Exists(QueueFile)) return;

        // Remove the queue first so a failing job can never loop
        var queued = File.ReadAllLines(QueueFile).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        File.Delete(QueueFile);
        waitingNoticeShown = false;
        if (queued.Count == 0) return;

        var setup  = EditorSceneManager.GetSceneManagerSetup();
        var report = new StringBuilder();
        report.AppendLine($"Run at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        foreach (string job in queued)
        {
            var problems = new List<string>();
            void Capture(string message, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning)
                    problems.Add($"  {type}: {message}");
            }

            Application.logMessageReceived += Capture;
            try
            {
                if (Jobs.TryGetValue(job, out var action))
                {
                    action();
                    report.AppendLine($"[OK] {job}");
                }
                else
                {
                    report.AppendLine($"[UNKNOWN] {job} — known jobs: {string.Join(", ", Jobs.Keys)}");
                }
            }
            catch (Exception e)
            {
                report.AppendLine($"[FAILED] {job}: {e}");
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }
            foreach (var problem in problems) report.AppendLine(problem);
        }

        // Put the user back where they were
        if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path) && File.Exists(s.path)))
            EditorSceneManager.RestoreSceneManagerSetup(setup);

        Directory.CreateDirectory(Folder);
        File.WriteAllText(ResultsFile, report.ToString());
        Debug.Log("[EditorJobQueue] Finished editor jobs:\n" + report);
    }

    private static bool HasUnsavedScenes()
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty) return true;
        return false;
    }
}
