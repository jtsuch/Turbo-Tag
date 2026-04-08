using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [System.Serializable]
    public struct MusicTrack
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume;
    }

    [Header("Music Clips")]
    [SerializeField] private AudioClip menuMusic;
    //[SerializeField] private AudioClip lobbyMusic;

    [Header("Music Settings")]
    [SerializeField] private float musicVolume = 0.6f;
    [SerializeField] private float crossfadeDuration = 1.5f;
    
    [Header("Game Music Playlist")]
    [SerializeField] private MusicTrack[] gamePlaylist;

private int lastPlayedIndex = -1; // Avoids playing the same track twice in a row

    private AudioSource musicSource;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;
        musicSource.volume = musicVolume;
    }

    private void OnEnable()
    {
        // Subscribe: when any scene loads, call OnSceneLoaded
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        // Always unsubscribe when disabled to avoid ghost listeners
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        // Handle the very first scene manually since sceneLoaded
        // won't fire for the scene that was already loaded
        HandleMusicForScene(SceneManager.GetActiveScene().name);
    }

    // -------------------------------------------------------------------------
    // Scene Event Handler
    // -------------------------------------------------------------------------

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        HandleMusicForScene(scene.name);
    }

    // This is your central "which music plays where" logic.
    // As your game grows, this is the ONE place you update.
    private void HandleMusicForScene(string sceneName)
    {
        switch (sceneName)
        {
            case "Loading":
                TransitionTo(menuMusic);
                break;
            case "MainMenu":
                break;
            case "Lobby":
                break;
            case "Pregame":
                //TransitionTo(lobbyMusic);
                break;

            // Any scene not explicitly listed (your levels) gets game music
            default:
                StartPlaylist();
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Music Transitions
    // -------------------------------------------------------------------------

    public void TransitionTo(AudioClip newClip)
    {
        if (newClip == null) return;

        // Don't restart if the same track is already playing
        if (musicSource.clip == newClip && musicSource.isPlaying) return;

        StopAllCoroutines();
        StartCoroutine(CrossfadeTo(newClip));
    }

    private IEnumerator CrossfadeTo(AudioClip newClip, float targetVolume = 1f)
    {
        float startVolume = musicSource.volume * targetVolume;

        // Fade out
        float elapsed = 0f;
        while (elapsed < crossfadeDuration / 2f)
        {
            elapsed += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / (crossfadeDuration / 2f));
            yield return null;
        }

        // Swap clip
        musicSource.Stop();
        musicSource.clip = newClip;
        musicSource.Play();

        // Fade in
        elapsed = 0f;
        while (elapsed < crossfadeDuration / 2f)
        {
            elapsed += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(0f, musicVolume, elapsed / (crossfadeDuration / 2f));
            yield return null;
        }

        musicSource.volume = musicVolume;
    }

    // Call this instead of TransitionTo(gameMusic) in your switch
    private void StartPlaylist()
    {
        StopAllCoroutines();
        StartCoroutine(PlaylistRoutine());
    }

    private IEnumerator PlaylistRoutine()
    {
        while (true) // Loops forever until a scene change stops it
        {
            MusicTrack track = GetNextTrack();

            // Crossfade in
            yield return StartCoroutine(CrossfadeTo(track.clip, track.volume));

            // Wait for the track to finish, then loop back
            yield return new WaitForSeconds(track.clip.length);
        }
    }

    private MusicTrack GetNextTrack()
    {
        if (gamePlaylist.Length == 1) return gamePlaylist[0];

        int index;
        do
        {
            index = Random.Range(0, gamePlaylist.Length);
        } while (index == lastPlayedIndex); // Keep rolling until it's a different track

        lastPlayedIndex = index;
        return gamePlaylist[index];
    }

    // -------------------------------------------------------------------------
    // Public Controls (for UI buttons, etc.)
    // -------------------------------------------------------------------------

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        musicSource.volume = musicVolume;
    }

    public void StopMusic() => musicSource.Stop();
}