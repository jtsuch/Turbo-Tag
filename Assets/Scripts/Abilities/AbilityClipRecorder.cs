#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor only: films a short looping clip of every ability for the Abilities browser. Started by the editor job
/// RecordAbilityClips (Pinnacle's plaza, offline Freeplay): for each available ability it equips it, puts the player
/// on the start spot, drives the keys with <see cref="SimInput"/> (a script per slot: Basic held while running,
/// Quick tapped after a jump, Throw aimed and charged, Trap placed then backed away from) and films 3.2 s from a
/// third-person camera at 15 fps into one 8 × 6 frame sheet, Assets/Resources/AbilityClips/{Id}.jpg
/// (played back by <see cref="AbilityClipPlayer"/>). Leaves play mode when done; the last picks are restored.
/// </summary>
[DefaultExecutionOrder(-10000)] // Sets the simulated keys before InputHandler reads them
public class AbilityClipRecorder : MonoBehaviour
{
    public const string SessionKey = "TT_RecordClips";   // SessionState: "all" or a comma list of ids
    public const string OutFolder  = "Assets/Resources/AbilityClips";
    public const int    Columns = 8, Rows = 6, FrameW = 256, FrameH = 144, Fps = 15;
    private const float Length = Columns * Rows / (float)Fps; // 3.2 s

    private static readonly Vector3 StartSpot = new(-2f, 0.1f, 22f); // Pinnacle plaza's open north side, facing the tower
    private const float StartYaw = 180f;

    private readonly HashSet<KeyCode> keys = new();
    private Camera filmCam;
    private RenderTexture rt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void MaybeStart()
    {
        if (string.IsNullOrEmpty(SessionState.GetString(SessionKey, ""))) return;
        new GameObject("Ability Clip Recorder").AddComponent<AbilityClipRecorder>();
    }

    private void Update()
    {
        if (SimInput.Active) SimInput.BeginFrame(keys);
    }

    private IEnumerator Start()
    {
        string which = SessionState.GetString(SessionKey, "");
        SessionState.EraseString(SessionKey);
        var log = new List<string>();

        float wait = 0f;
        while (Player.Local == null && wait < 20f) { wait += Time.deltaTime; yield return null; }
        var player = Player.Local;
        if (player == null) { Finish(log, "no local player"); yield break; }
        yield return new WaitForSeconds(1f);

        var lastPicks = new Dictionary<AbilitySlot, string>();
        foreach (AbilitySlot slot in System.Enum.GetValues(typeof(AbilitySlot))) lastPicks[slot] = AbilityLoadout.LastChoice(slot);

        Directory.CreateDirectory(OutFolder);
        rt = new RenderTexture(FrameW, FrameH, 24) { antiAliasing = 4 };
        filmCam = new GameObject("Clip Camera").AddComponent<Camera>();
        filmCam.enabled = false;
        filmCam.fieldOfView = 55f;
        filmCam.targetTexture = rt;
        var init = player.GetComponent<PlayerInitializer>();

        var ids = new List<string>();
        foreach (AbilitySlot slot in System.Enum.GetValues(typeof(AbilitySlot)))
            foreach (var id in AbilityCatalog.InSlot(slot))
                if (AbilityCatalog.IsAvailable(id) && (which == "all" || ("," + which + ",").Contains("," + id + ",")))
                    ids.Add(id);

        foreach (var id in ids)
        {
            var slot = AbilityCatalog.SlotOf(id).Value;
            yield return Reset(player);
            AbilityLoadout.SetLocal(slot, id);
            yield return new WaitForSeconds(0.3f);
            if (init != null) init.SetOwnBodyVisible(true);
            var equipped = player.Abilities != null ? player.Abilities.GetEquipped(slot) : null;
            string equippedId = equipped != null ? equipped.Id : "none";

            var atlas = new Texture2D(Columns * FrameW, Rows * FrameH, TextureFormat.RGB24, false);
            SimInput.Start(StartYaw, 0f);
            float t0 = Time.time;
            int frame = 0;
            Vector3 camPos = player.transform.position + Quaternion.Euler(0, StartYaw, 0) * new Vector3(2f, 2.8f, -6f);
            while (frame < Columns * Rows)
            {
                float t = Time.time - t0;
                Drive(slot, id, t);
                // Third-person follow camera (behind and to the right)
                Quaternion facing = Quaternion.Euler(0f, SimInput.Yaw, 0f);
                // Throws are filmed from the side (to see the arc), traps from above, the rest from behind
                Vector3 offset = slot switch
                {
                    AbilitySlot.Throw => new Vector3(7.5f, 2.6f, 3.5f),
                    AbilitySlot.Trap  => new Vector3(1.5f, 5.5f, -5f),
                    _                 => new Vector3(2.2f, 2.8f, -6.5f),
                };
                Vector3 want = player.transform.position + facing * offset;
                camPos = Vector3.Lerp(camPos, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
                if (frame == 0 && t < 0.05f) camPos = want; // Snap on the first frame
                filmCam.transform.position = camPos;
                filmCam.transform.LookAt(player.transform.position + facing * (slot == AbilitySlot.Throw ? new Vector3(0f, 1.8f, 4.5f) : new Vector3(0f, 1.2f, 4f)));

                if (t >= frame / (float)Fps)
                {
                    filmCam.Render();
                    RenderTexture.active = rt;
                    int col = frame % Columns, row = frame / Columns;
                    atlas.ReadPixels(new Rect(0, 0, FrameW, FrameH), col * FrameW, (Rows - 1 - row) * FrameH);
                    RenderTexture.active = null;
                    frame++;
                }
                yield return null;
            }
            SimInput.Stop();
            keys.Clear();
            atlas.Apply();
            File.WriteAllBytes($"{OutFolder}/{id}.jpg", atlas.EncodeToJPG(82));
            Destroy(atlas);
            int made = 0;
            foreach (var view in FindObjectsByType<PhotonView>(FindObjectsSortMode.None))
                if (view != null && view.IsMine && view.GetComponent<Player>() == null && !view.IsRoomView) made++;
            log.Add($"ok {id} (equipped: {equippedId}, objects made: {made})");
        }

        foreach (var pair in lastPicks) AbilityLoadout.SetLocal(pair.Key, pair.Value);
        if (init != null) init.SetOwnBodyVisible(false);
        Finish(log, null);
    }

    // Back on the start spot: abilities stopped, effects and placed objects gone
    private IEnumerator Reset(Player player)
    {
        SimInput.Stop();
        keys.Clear();
        if (player.Abilities != null) player.Abilities.CancelAll();
        PlayerEffect.ClearAll(player);
        foreach (var view in FindObjectsByType<PhotonView>(FindObjectsSortMode.None))
            if (view != null && view.IsMine && view.gameObject != player.gameObject && view.GetComponent<Player>() == null && view.ViewID != 0
                && !view.IsRoomView)
                PhotonNetwork.Destroy(view.gameObject);
        yield return null;
        player.Teleport(StartSpot, Quaternion.Euler(0f, StartYaw, 0f));
        player.Body.linearVelocity = Vector3.zero;
        yield return new WaitForSeconds(0.4f);
    }

    // The keys for time t of a clip
    private void Drive(AbilitySlot slot, string id, float t)
    {
        keys.Clear();
        KeyCode ability = Keybinds.ForSlot(slot), action = Keybinds.Get(InputAction.Action);
        KeyCode jump = Keybinds.Get(InputAction.Jump), sprint = Keybinds.Get(InputAction.Sprint);
        bool Between(float a, float b) => t >= a && t < b;

        switch (slot)
        {
            case AbilitySlot.Basic: // Running, jumping, holding it
                SimInput.Move = new Vector2(0f, 1f);
                SimInput.Pitch = -10f;
                keys.Add(sprint);
                if (Between(0.3f, 0.45f) || Between(1.45f, 1.6f)) keys.Add(jump);
                if (Between(0.6f, 3.0f)) keys.Add(ability);
                break;

            case AbilitySlot.Quick: // A jump, then a tap at the top
                SimInput.Move = new Vector2(0f, 1f);
                SimInput.Pitch = -5f;
                keys.Add(sprint);
                if (Between(0.3f, 0.4f)) keys.Add(jump);
                if (Between(0.7f, 0.82f)) keys.Add(ability);
                break;

            case AbilitySlot.Throw: // Aim, charge with the Action key, let go
                SimInput.Move = Between(0f, 0.4f) ? new Vector2(0f, 1f) : Vector2.zero;
                SimInput.Pitch = -8f;
                if (id == "BoomBomb")
                {
                    if (Between(0.4f, 0.5f) || Between(1.8f, 1.9f)) keys.Add(ability); // Toss, then set it off
                }
                else
                {
                    if (Between(0.4f, 0.5f)) keys.Add(ability);
                    if (Between(0.8f, 1.5f)) keys.Add(action);
                }
                break;

            case AbilitySlot.Trap: // Look down, place it, then walk on past it (it stays between us and the camera)
                SimInput.Pitch = t < 1.1f ? 35f : 0f;
                SimInput.Move = Between(1.2f, 3.0f) ? new Vector2(0f, 1f) : Vector2.zero;
                if (Between(0.3f, 0.4f)) keys.Add(ability);
                if (Between(0.9f, 1.0f)) keys.Add(action);
                break;
        }
    }

    private void Finish(List<string> log, string error)
    {
        SimInput.Stop();
        if (error != null) log.Add("! " + error);
        Directory.CreateDirectory("Library/TurboTag/scratch");
        File.WriteAllLines("Library/TurboTag/scratch/clip_record.txt", log);
        if (filmCam != null) Destroy(filmCam.gameObject);
        if (rt != null) rt.Release();
        EditorApplication.isPlaying = false;
    }
}
#endif
