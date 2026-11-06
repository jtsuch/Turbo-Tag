using UnityEngine;

/// <summary>
/// Curse: scrambled movement keys. W, A, S and D each move a random (wrong) way, reshuffled every few seconds.
/// Works through <see cref="InputHandler.MoveRemap"/>.
/// </summary>
public class KeybindSwitchEffect : PlayerEffect
{
    [SerializeField] private float reshuffleEvery = 5f;

    // Forward, back, right, left as directions
    private static readonly Vector2[] Directions = { Vector2.up, Vector2.down, Vector2.right, Vector2.left };
    private readonly int[] map = { 0, 1, 2, 3 };
    private float nextShuffle;

    public override string DisplayName => "Scrambled Keys";
    public override string Description => "WASD go the wrong ways";
    public override bool   IsGood      => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        Shuffle();
        player.Input.MoveRemap = Remap;
    }

    protected override void OnEffectEnd()
    {
        if (player != null && player.Input != null && player.Input.MoveRemap == (System.Func<Vector2, Vector2>)Remap)
            player.Input.MoveRemap = null;
    }

    private void Update()
    {
        if (IsLocalEffect && Time.time >= nextShuffle) Shuffle();
    }

    // A random order where no key keeps its own direction
    private void Shuffle()
    {
        nextShuffle = Time.time + reshuffleEvery;
        do
        {
            for (int i = map.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (map[i], map[j]) = (map[j], map[i]);
            }
        } while (map[0] == 0 || map[1] == 1 || map[2] == 2 || map[3] == 3);
    }

    private Vector2 Remap(Vector2 input)
    {
        float[] pressed = { Mathf.Max(0f, input.y), Mathf.Max(0f, -input.y), Mathf.Max(0f, input.x), Mathf.Max(0f, -input.x) };
        Vector2 result = Vector2.zero;
        for (int i = 0; i < 4; i++) result += Directions[map[i]] * pressed[i];
        return Vector2.ClampMagnitude(result, 1f);
    }
}
