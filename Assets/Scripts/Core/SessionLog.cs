using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Playtest support, always on (builds and editor): every session writes its own log file
/// (<c>{persistentDataPath}/Logs/session-{time}.txt</c>, the last 15 kept) with a header (version, machine) and every
/// log message, scene change and match start; errors and exceptions are counted and shown as a small red note in the
/// corner. <b>F8</b> (or the pause menu's Report a Bug) saves a bug report to <c>{persistentDataPath}/BugReports/{time}/</c>:
/// a screenshot, the game state (scene, room, mode, map, phase, players, ping, position) and the recent log.
/// Created automatically at startup; nothing to add to scenes.
/// </summary>
public class SessionLog : MonoBehaviour
{
    public const KeyCode ReportKey = KeyCode.F8;
    private const int KeepSessions = 15, RecentLines = 300;

    private static SessionLog instance;
    private readonly ConcurrentQueue<string> pending = new();
    private readonly Queue<string> recent = new();
    private StreamWriter writer;
    private string sessionFile;
    private int errors;
    private string toast;
    private float toastUntil;

    /// <summary>The folder bug reports go in.</summary>
    public static string ReportsFolder => Path.Combine(Application.persistentDataPath, "BugReports");

    /// <summary>The folder session logs go in.</summary>
    public static string LogsFolder => Path.Combine(Application.persistentDataPath, "Logs");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("SessionLog");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SessionLog>();
    }

    private void Awake()
    {
        try
        {
            Directory.CreateDirectory(LogsFolder);
            foreach (var old in new DirectoryInfo(LogsFolder).GetFiles("session-*.txt")
                         .OrderByDescending(f => f.CreationTimeUtc).Skip(KeepSessions - 1))
                old.Delete();
            sessionFile = Path.Combine(LogsFolder, $"session-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
            writer = new StreamWriter(sessionFile, false, Encoding.UTF8) { AutoFlush = false };
            writer.WriteLine($"Turbo Tag v{Application.version}  {DateTime.Now:yyyy-MM-dd HH:mm:ss}  {(Application.isEditor ? "EDITOR" : "BUILD")}");
            writer.WriteLine($"{SystemInfo.operatingSystem} | {SystemInfo.processorType} | {SystemInfo.graphicsDeviceName} | {SystemInfo.systemMemorySize} MB RAM | {Screen.width}x{Screen.height}");
            writer.WriteLine(new string('-', 80));
            writer.Flush();
        }
        catch (Exception e) { Debug.LogWarning($"[SessionLog] Can't write logs: {e.Message}"); }

        Application.logMessageReceivedThreaded += OnLog;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        Application.logMessageReceivedThreaded -= OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Flush();
        writer?.Dispose();
        if (instance == this) instance = null;
    }

    private void OnApplicationQuit() => Flush();

    // Any thread
    private void OnLog(string message, string stack, LogType type)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {Tag(type)}{message}";
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
        {
            System.Threading.Interlocked.Increment(ref errors);
            if (!string.IsNullOrEmpty(stack)) line += "\n    " + stack.Trim().Replace("\n", "\n    ");
        }
        pending.Enqueue(line);
    }

    private static string Tag(LogType type) => type switch
    {
        LogType.Exception => "EXCEPTION ",
        LogType.Error     => "ERROR ",
        LogType.Assert    => "ASSERT ",
        LogType.Warning   => "warn ",
        _                 => "",
    };

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) =>
        pending.Enqueue($"[{DateTime.Now:HH:mm:ss}] === Scene {scene.name}{RoomSummary()}");

    private static string RoomSummary()
    {
        if (!PhotonNetwork.InRoom) return "";
        return $" | room '{PhotonNetwork.CurrentRoom.Name}' ({PhotonNetwork.CurrentRoom.PlayerCount} players) | mode {MatchRules.GameModeKey} | map {MatchRules.Map}" +
               $" | {(PhotonNetwork.IsMasterClient ? "HOST" : "client")} | ping {PhotonNetwork.GetPing()} ms";
    }

    private void Update()
    {
        Flush();
        if (Input.GetKeyDown(ReportKey)) Report();
    }

    private void Flush()
    {
        bool any = false;
        while (pending.TryDequeue(out string line))
        {
            any = true;
            recent.Enqueue(line);
            while (recent.Count > RecentLines) recent.Dequeue();
            writer?.WriteLine(line);
        }
        if (any) writer?.Flush();
    }

    // ─── Bug reports ──────────────────────────────────────────────────────────

    /// <summary>Saves a bug report (screenshot, state, recent log). Returns its folder.</summary>
    public static string Report(string note = null)
    {
        if (instance == null) return null;
        return instance.SaveReport(note);
    }

    private string SaveReport(string note)
    {
        Flush();
        string folder = Path.Combine(ReportsFolder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        try
        {
            Directory.CreateDirectory(folder);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "screenshot.png")); // Written at the end of the frame

            var s = new StringBuilder();
            s.AppendLine($"Turbo Tag v{Application.version}  {DateTime.Now:yyyy-MM-dd HH:mm:ss}  {(Application.isEditor ? "EDITOR" : "BUILD")}");
            if (!string.IsNullOrEmpty(note)) s.AppendLine($"Note: {note}");
            s.AppendLine($"Scene: {SceneManager.GetActiveScene().name}{RoomSummary()}");
            if (PhotonNetwork.InRoom)
            {
                s.AppendLine($"Phase: {NetProps.Room(RoomKeys.Phase, 0)} ({(MatchPhase)NetProps.Room(RoomKeys.Phase, 0)})  Round {NetProps.Room(RoomKeys.Round, 0)}");
                foreach (var p in PhotonNetwork.PlayerList)
                    s.AppendLine($"  {(p.IsLocal ? "*" : " ")} {p.NickName} #{p.ActorNumber}{(p.IsMasterClient ? " (host)" : "")} role {PlayerRoles.Get(p)} score {NetProps.Of(p, PlayerKeys.Score, 0f):0.#}");
            }
            var local = Player.Local;
            if (local != null)
                s.AppendLine($"Local player: pos {local.transform.position} vel {local.Velocity} state {local.State} caught {local.IsCaught} loadout " +
                             $"{NetProps.Of(PhotonNetwork.LocalPlayer, PlayerKeys.AbilityBasic, "")}/{NetProps.Of(PhotonNetwork.LocalPlayer, PlayerKeys.AbilityQuick, "")}/" +
                             $"{NetProps.Of(PhotonNetwork.LocalPlayer, PlayerKeys.AbilityThrow, "")}/{NetProps.Of(PhotonNetwork.LocalPlayer, PlayerKeys.AbilityTrap, "")}");
            s.AppendLine($"FPS: {1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):0}  Errors this session: {errors}");
            s.AppendLine(new string('-', 80));
            foreach (var line in recent) s.AppendLine(line);
            File.WriteAllText(Path.Combine(folder, "report.txt"), s.ToString());
            if (sessionFile != null && File.Exists(sessionFile))
            {
                writer?.Flush();
                File.Copy(sessionFile, Path.Combine(folder, "session-log.txt"), true);
            }
            ShowToast($"Bug report saved ({ReportKey}). Find it in Pause → Report a Bug → Open Folder.");
            Debug.Log($"[SessionLog] Bug report saved: {folder}");
        }
        catch (Exception e)
        {
            ShowToast($"Couldn't save the bug report: {e.Message}");
        }
        return folder;
    }

    /// <summary>Opens the bug reports folder in the file browser.</summary>
    public static void OpenReportsFolder()
    {
        Directory.CreateDirectory(ReportsFolder);
        Application.OpenURL("file:///" + ReportsFolder.Replace('\\', '/'));
    }

    private void ShowToast(string text)
    {
        toast = text;
        toastUntil = Time.unscaledTime + 4f;
    }

    // ─── On-screen notes ──────────────────────────────────────────────────────

    private GUIStyle style;

    private void OnGUI()
    {
        style ??= new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height / 60f), richText = true };
        if (errors > 0)
            GUI.Label(new Rect(8, Screen.height - style.fontSize * 2.2f, Screen.width, style.fontSize * 2f),
                      $"<color=#FF5A6E>⚠ {errors} error{(errors == 1 ? "" : "s")} · press {ReportKey} to save a bug report</color>", style);
        if (toast != null && Time.unscaledTime < toastUntil)
            GUI.Label(new Rect(8, Screen.height - style.fontSize * 4.2f, Screen.width, style.fontSize * 2f),
                      $"<color=#FFD23F>{toast}</color>", style);
    }
}
