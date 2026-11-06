using System;
using System.Collections.Generic;
using System.Text;
using Photon.Pun;
using TMPro;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// The admin console: press ` (back quote) in a level to type cheat commands at other players, e.g.
/// "Bob ability 0" (Bob's abilities off), "all size 3", "others stare 5" (well, everyone stares at the others).
/// Targets: a player's name (or the start of it; Tab completes it), "all", "others" or "me".
/// Only admins (<see cref="Admins"/>; anyone in the editor, for testing) can open it, and every receiving
/// screen checks the sender too, so friends can't cheat back. While it's open, gameplay input is paused.
/// Created per level by GameModeManager, with <see cref="AdminCommands"/> doing the work.
/// </summary>
public class AdminConsole : MonoBehaviour
{
    /// <summary>Names allowed to use admin commands (PlayFab display names; case doesn't matter).</summary>
    public static readonly string[] Admins = { "NewLemon" };

    private static AdminConsole instance;

    /// <summary>True while the console is open (counts as paused: no gameplay input).</summary>
    public static bool IsOpen => instance != null && instance.open;

    /// <summary>The frame the console was closed (Escape there mustn't also open the pause menu).</summary>
    public static int ClosedFrame { get; private set; } = -1;

    private const int MaxLines = 14;

    private bool           open;
    private GameObject     panel;
    private TMP_InputField field;
    private TMP_Text       log;
    private readonly List<string> lines   = new();
    private readonly List<string> history = new();
    private int historyIndex;

    public static bool IsAdmin(PhotonPlayer player)
    {
        if (Application.isEditor) return true; // Editor testing (virtual players have made-up names)
        if (player == null) return false;
        foreach (string name in Admins)
            if (string.Equals(player.NickName, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static void Create()
    {
        if (instance != null) return;
        instance = new GameObject("AdminConsole").AddComponent<AdminConsole>();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // ─── Open / close ─────────────────────────────────────────────────────────

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.BackQuote) && IsAdmin(PhotonNetwork.LocalPlayer))
        {
            SetOpen(!open);
            return;
        }
        if (!open) return;

        if (Input.GetKeyDown(KeyCode.Escape)) { SetOpen(false); return; }
        if (Input.GetKeyDown(KeyCode.Tab)) Complete();
        if (Input.GetKeyDown(KeyCode.UpArrow))   Recall(-1);
        if (Input.GetKeyDown(KeyCode.DownArrow)) Recall(+1);
        if (!field.isFocused) field.ActivateInputField();
    }

    private void SetOpen(bool value)
    {
        if (panel == null) Build();
        open = value;
        panel.SetActive(open);
        if (open)
        {
            field.text = "";
            field.ActivateInputField();
            if (lines.Count == 0) Print("<color=#FFC93C>Admin console.</color> Type <b>help</b> for commands. ` or Esc closes.");
        }
        else
        {
            field.DeactivateInputField();
            ClosedFrame = Time.frameCount;
        }
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    private void Submit(string text)
    {
        text = (text ?? "").Replace("`", "").Trim();
        field.text = "";
        field.ActivateInputField();
        if (text.Length == 0) return;

        history.Add(text);
        historyIndex = history.Count;
        Print($"<color=#B9B2E0>> {text}</color>");
        Run(text);
    }

    private void Run(string text)
    {
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string first = words[0].ToLowerInvariant();
        var admin = AdminCommands.Instance;

        switch (first)
        {
            case "help":
                Print("<b><target> <command> [value]</b>   targets: a name (Tab completes), all, others, me");
                var sb = new StringBuilder();
                foreach (var c in AdminCommands.Help)
                    sb.Append($"<color=#22D3EE>{c.name}</color> {c.args}  <color=#B9B2E0>{c.what}</color>\n");
                Print(sb.ToString().TrimEnd());
                Print("<color=#22D3EE>announce</color> <text>  ·  <color=#22D3EE>goto</color> <name>  ·  <color=#22D3EE>list</color>  ·  <color=#22D3EE>clear</color>");
                return;
            case "clear":
                lines.Clear();
                Refresh();
                return;
            case "list":
            case "players":
                foreach (var p in PhotonNetwork.PlayerList)
                    Print($"  {p.NickName}{(p.IsMasterClient ? "  <color=#FFC93C>(host)</color>" : "")}{(p.IsLocal ? "  <color=#B9B2E0>(you)</color>" : "")}");
                return;
            case "announce":
                if (admin != null && words.Length > 1) admin.Announce(text.Substring(text.IndexOf(' ') + 1));
                return;
            case "goto":
                if (words.Length < 2) { Print("goto <name>"); return; }
                var whom = AdminCommands.MatchPlayers(words[1]);
                var there = whom.Count == 1 ? Player.Find(whom[0].ActorNumber) : null;
                if (there == null || Player.Local == null) { Print(Error(words[1], whom.Count)); return; }
                Player.Local.Teleport(there.transform.position - there.transform.forward * 2f, there.transform.rotation);
                Print($"Went to {whom[0].NickName}.");
                return;
        }

        if (words.Length < 2) { Print("Usage: <target> <command> [value]. Type help."); return; }
        if (admin == null) { Print("<color=#FF4D6D>Admin commands only work in a level.</color>"); return; }

        string command = words[1].ToLowerInvariant();
        if (!AdminCommands.IsCommand(command)) { Print($"<color=#FF4D6D>Unknown command '{command}'.</color> Type help."); return; }
        string args = words.Length > 2 ? string.Join(" ", words, 2, words.Length - 2) : "";

        var targets = Targets(words[0]);
        if (targets.Count == 0) { Print(Error(words[0], AdminCommands.MatchPlayers(words[0]).Count)); return; }
        foreach (var target in targets) admin.Send(target, command, args);

        string names = targets.Count > 3 ? $"{targets.Count} players" : string.Join(", ", targets.ConvertAll(t => t.NickName));
        Print($"<color=#7CE85A>{command}{(args.Length > 0 ? " " + args : "")}</color> → {names}");
    }

    private static List<PhotonPlayer> Targets(string word)
    {
        var all = new List<PhotonPlayer>(PhotonNetwork.PlayerList);
        switch (word.ToLowerInvariant())
        {
            case "all":    return all;
            case "others": return all.FindAll(p => !p.IsLocal);
            case "me":     return all.FindAll(p => p.IsLocal);
        }
        var found = AdminCommands.MatchPlayers(word);
        return found.Count == 1 ? found : new List<PhotonPlayer>();
    }

    private static string Error(string word, int matches) => matches > 1
        ? $"<color=#FF4D6D>'{word}' matches {matches} players.</color> Type more of the name."
        : $"<color=#FF4D6D>No player called '{word}'.</color> Type list.";

    // Tab: finish the player name being typed (first word), or the command (second word)
    private void Complete()
    {
        string text = field.text.Replace("\t", "");
        string[] words = text.Split(' ');
        if (words.Length == 1)
        {
            var found = AdminCommands.MatchPlayers(words[0]);
            if (found.Count == 1) text = found[0].NickName + " ";
            else if (found.Count > 1) Print(string.Join(", ", found.ConvertAll(p => p.NickName)));
        }
        else if (words.Length == 2)
        {
            var matches = new List<string>();
            foreach (var c in AdminCommands.Help)
                if (c.name.StartsWith(words[1], StringComparison.OrdinalIgnoreCase)) matches.Add(c.name);
            if (matches.Count == 1) text = $"{words[0]} {matches[0]} ";
            else if (matches.Count > 1) Print(string.Join(", ", matches));
        }
        field.text = text;
        field.caretPosition = text.Length;
    }

    private void Recall(int step)
    {
        if (history.Count == 0) return;
        historyIndex = Mathf.Clamp(historyIndex + step, 0, history.Count);
        field.text = historyIndex < history.Count ? history[historyIndex] : "";
        field.caretPosition = field.text.Length;
    }

    private void Print(string line)
    {
        lines.Add(line);
        while (lines.Count > 60) lines.RemoveAt(0);
        Refresh();
    }

    private void Refresh()
    {
        if (log == null) return;
        // The last lines that fit (help is several lines in one entry)
        var shown = new List<string>();
        int count = 0;
        for (int i = lines.Count - 1; i >= 0 && count < MaxLines; i--)
        {
            shown.Insert(0, lines[i]);
            count += lines[i].Split('\n').Length;
        }
        log.text = string.Join("\n", shown);
    }

    // ─── UI ───────────────────────────────────────────────────────────────────

    private void Build()
    {
        var canvas = UIKit.Canvas("AdminConsole Canvas", 30); // Over the pause menu (20)
        canvas.transform.SetParent(transform, false);
        var theme = UITheme.Current;

        var card = UIKit.Card(canvas.transform, UIKit.WithAlpha(theme.backgroundTop, 0.94f), 22f, 0.5f, "Console");
        card.anchorMin = new Vector2(0f, 1f);
        card.anchorMax = new Vector2(1f, 1f);
        card.pivot     = new Vector2(0.5f, 1f);
        card.offsetMin = new Vector2(24f, -560f);
        card.offsetMax = new Vector2(-24f, -16f);
        panel = card.gameObject;

        var title = UIKit.Text(card, "ADMIN CONSOLE", 20f, UIKit.Font.Display, theme.pink, TextAlignmentOptions.TopLeft);
        UIKit.Stretch(title.rectTransform, 24f, 24f, 14f, 0f);
        title.rectTransform.anchorMin = new Vector2(0f, 1f);
        title.rectTransform.sizeDelta = new Vector2(-48f, 28f);

        log = UIKit.Text(card, "", 20f, UIKit.Font.Body, theme.text, TextAlignmentOptions.BottomLeft);
        UIKit.Stretch(log.rectTransform, 24f, 24f, 46f, 92f);
        log.textWrappingMode = TextWrappingModes.Normal;
        log.richText = true;

        field = UIKit.InputField(card, "Bob ability 0   ·   all size 3   ·   help", false, 56f);
        var rect = (RectTransform)field.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot     = new Vector2(0.5f, 0f);
        rect.offsetMin = new Vector2(20f, 20f);
        rect.offsetMax = new Vector2(-20f, 76f);
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.onFocusSelectAll = false;
        field.restoreOriginalTextOnEscape = false;
        field.onValidateInput = (_, _, c) => c == '`' || c == '\t' ? '\0' : c;
        field.onSubmit.AddListener(Submit);

        panel.SetActive(false);
    }
}
