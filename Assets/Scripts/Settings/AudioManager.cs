using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent music player. Menus play the menu track; levels play a shuffled playlist with
/// crossfades. Volume comes from SettingsManager.
/// Attach to: a manager GameObject in the Loading scene (DontDestroyOnLoad).
/// </summary>
public class AudioManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    private static AudioManager _instance;
    public static AudioManager Instance => _instance;

    [System.Serializable]
    public struct MusicTrack
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume;
    }

    [Header("Music")]
    [SerializeField] private AudioClip    menuMusic;
    [SerializeField] private MusicTrack[] gamePlaylist;

    [Header("Settings")]
    [SerializeField] private float musicVolume       = 0.6f;
    [SerializeField] private float crossfadeDuration = 1.5f;

    private AudioSource musicSource;
    private float       currentTrackVolume = 1f;
    private int         lastPlayedIndex    = -1;
    private bool        playingPlaylist;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop         = true;
        musicSource.playOnAwake  = false;
        musicSource.spatialBlend = 0f;
    }

    private void OnEnable()  => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void Start()
    {
        if (SettingsManager.Instance != null) musicVolume = SettingsManager.Instance.MusicVolume;
        HandleMusicForScene(SceneManager.GetActiveScene().name);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => HandleMusicForScene(scene.name);

    // The single place that decides which music plays where
    private void HandleMusicForScene(string sceneName)
    {
        tempo = 1f; // A new scene starts at normal speed (the last round may have sped it up)
        if (musicSource != null) musicSource.pitch = 1f;
        if (SceneNames.IsLevel(sceneName)) StartPlaylist();
        else                              PlayTrack(menuMusic);
    }

    // ─── Playback ─────────────────────────────────────────────────────────────

    public void PlayTrack(AudioClip clip)
    {
        playingPlaylist = false;
        if (clip == null || (musicSource.clip == clip && musicSource.isPlaying)) return;

        StopAllCoroutines();
        StartCoroutine(CrossfadeTo(new MusicTrack { clip = clip, volume = 1f }));
    }

    private void StartPlaylist()
    {
        if (playingPlaylist || gamePlaylist == null || gamePlaylist.Length == 0) return;
        playingPlaylist = true;
        StopAllCoroutines();
        StartCoroutine(PlaylistRoutine());
    }

    private IEnumerator PlaylistRoutine()
    {
        while (true)
        {
            MusicTrack track = NextTrack();
            if (track.clip == null) yield break;
            yield return CrossfadeTo(track);
            yield return new WaitForSeconds(Mathf.Max(1f, track.clip.length - crossfadeDuration));
        }
    }

    private MusicTrack NextTrack()
    {
        if (gamePlaylist.Length == 1) return gamePlaylist[0];

        int index;
        do index = Random.Range(0, gamePlaylist.Length);
        while (index == lastPlayedIndex);

        lastPlayedIndex = index;
        return gamePlaylist[index];
    }

    private IEnumerator CrossfadeTo(MusicTrack track)
    {
        float half  = crossfadeDuration / 2f;
        float start = musicSource.volume;

        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            musicSource.volume = Mathf.Lerp(start, 0f, t / half);
            yield return null;
        }

        currentTrackVolume = track.volume;
        musicSource.clip   = track.clip;
        musicSource.Play();

        float target = musicVolume * currentTrackVolume;
        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            musicSource.volume = Mathf.Lerp(0f, target, t / half);
            yield return null;
        }
        musicSource.volume = target;
    }

    // ─── Controls ─────────────────────────────────────────────────────────────

    private float tempo = 1f;

    /// <summary>Music speed (1 = normal; the HUD raises it in a round's last 30 and 10 seconds). Eases there.</summary>
    public void SetTempo(float pitch) => tempo = Mathf.Clamp(pitch, 0.5f, 2f);

    private void Update()
    {
        if (musicSource != null && !Mathf.Approximately(musicSource.pitch, tempo))
            musicSource.pitch = Mathf.MoveTowards(musicSource.pitch, tempo, Time.unscaledDeltaTime * 0.15f);
    }

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        musicSource.volume = musicVolume * currentTrackVolume;
    }

    public void StopMusic()
    {
        StopAllCoroutines();
        playingPlaylist = false;
        musicSource.Stop();
    }
}
