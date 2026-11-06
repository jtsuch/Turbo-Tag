using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps Jimmy's eyes black in every penguin colour. The skins recolour the colour map's black parts, and the eyes
/// are painted black on the map too, so they took the skin's colour. This finds the eyes from the mesh: dark
/// patches of the map used by a dense, flat band of vertices at one height (the eyeballs: thousands of vertices
/// within a few centimetres of height, unlike the body's black which spans the whole penguin), takes just the
/// triangles of that band, and copies the pixels they cover (plus a 2-pixel margin) from the original map into
/// every skin in Resources/Player/Skins. Safe to re-run after adding a colour. Job FixSkinEyes; report in
/// Library/TurboTag/scratch/skin_eyes.txt.
/// </summary>
public static class SkinEyeFix
{
    private const string Original = "Assets/Resources/Player/PenguinColorMap-Black.png";
    private const string Folder   = "Assets/Resources/Player/Skins";

    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        source.LoadImage(File.ReadAllBytes(Original));
        int w = source.width, h = source.height;
        var orig = source.GetPixels32();
        bool Dark(Color32 c) => c.a >= 128 && c.r + c.g + c.b < 150;

        // Dark patches of the map
        var label = new int[w * h];
        int id = 0;
        var stack = new Stack<int>();
        for (int s = 0; s < orig.Length; s++)
        {
            if (label[s] != 0 || !Dark(orig[s])) continue;
            label[s] = ++id;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % w, y = i / w;
                foreach (int n in new[] { x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1 })
                    if (n >= 0 && label[n] == 0 && Dark(orig[n])) { label[n] = id; stack.Push(n); }
            }
        }

        // The mesh in its rest pose
        var penguin = (GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<GameObject>("Player/ThePlayer"));
        var smr = penguin.GetComponentInChildren<SkinnedMeshRenderer>(true);
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var verts = baked.vertices.Select(v => smr.transform.TransformPoint(v)).ToArray();
        var uvs = smr.sharedMesh.uv;
        var tris = smr.sharedMesh.triangles;
        Object.DestroyImmediate(penguin);
        int LabelAt(Vector2 uv) => label[Mathf.Clamp((int)(uv.y * h), 0, h - 1) * w + Mathf.Clamp((int)(uv.x * w), 0, w - 1)];

        // Eye patches: used by 1000+ vertices whose middle 90% lie within 30 cm of height
        var heights = new Dictionary<int, List<float>>();
        for (int v = 0; v < verts.Length; v++)
        {
            int l = LabelAt(uvs[v]);
            if (l == 0) continue;
            if (!heights.TryGetValue(l, out var list)) heights[l] = list = new List<float>();
            list.Add(verts[v].y);
        }
        var bands = new Dictionary<int, (float lo, float hi)>();
        foreach (var kv in heights)
        {
            if (kv.Value.Count < 1000) continue;
            kv.Value.Sort();
            float lo = kv.Value[(int)(kv.Value.Count * 0.05f)], hi = kv.Value[(int)(kv.Value.Count * 0.95f)];
            if (hi - lo > 0.3f) continue;
            bands[kv.Key] = (lo - 0.05f, hi + 0.05f);
            log.AppendLine($"eye patch {kv.Key}: {kv.Value.Count} vertices, height {lo:F2}..{hi:F2}");
        }

        // Rasterise the eye triangles into a pixel mask
        var mask = new bool[w * h];
        int eyeTris = 0;
        for (int t = 0; t < tris.Length; t += 3)
        {
            int a = tris[t], b = tris[t + 1], c = tris[t + 2];
            Vector2 centre = (uvs[a] + uvs[b] + uvs[c]) / 3f;
            if (!bands.TryGetValue(LabelAt(centre), out var band)) continue;
            if (new[] { a, b, c }.Any(v => verts[v].y < band.lo || verts[v].y > band.hi)) continue;
            eyeTris++;
            Fill(mask, w, h, uvs[a], uvs[b], uvs[c]);
        }
        // A 2-pixel margin so filtering never blends in the skin colour at the edges
        for (int pass = 0; pass < 2; pass++)
        {
            var grown = (bool[])mask.Clone();
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i]) continue;
                int x = i % w, y = i / w;
                if (x > 0) grown[i - 1] = true;
                if (x < w - 1) grown[i + 1] = true;
                if (y > 0) grown[i - w] = true;
                if (y < h - 1) grown[i + w] = true;
            }
            mask = grown;
        }
        int maskCount = mask.Count(m => m);
        log.AppendLine($"eye triangles: {eyeTris}, pixels: {maskCount}");

        foreach (var file in Directory.GetFiles(Folder, "*.png"))
        {
            var skin = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            skin.LoadImage(File.ReadAllBytes(file));
            if (skin.width != w || skin.height != h) { log.AppendLine($"! {file}: size differs"); continue; }
            var px = skin.GetPixels32();
            int changed = 0;
            for (int i = 0; i < px.Length; i++)
            {
                if (!mask[i] || px[i].Equals(orig[i])) continue;
                px[i] = orig[i];
                changed++;
            }
            if (changed > 0)
            {
                skin.SetPixels32(px);
                skin.Apply();
                File.WriteAllBytes(file, skin.EncodeToPNG());
            }
            log.AppendLine($"{Path.GetFileName(file)}: {changed} eye pixels restored");
            Object.DestroyImmediate(skin);
        }
        Object.DestroyImmediate(source);
        AssetDatabase.Refresh();
        File.WriteAllText("Library/TurboTag/scratch/skin_eyes.txt", log.ToString());
    }

    // Marks every pixel whose centre is inside the UV triangle
    private static void Fill(bool[] mask, int w, int h, Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 pa = new(a.x * w, a.y * h), pb = new(b.x * w, b.y * h), pc = new(c.x * w, c.y * h);
        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x))));
        int x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x))));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y))));
        int y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y))));
        float Edge(Vector2 p, Vector2 q, Vector2 r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
        float area = Edge(pa, pb, pc);
        if (Mathf.Abs(area) < 1e-6f) return;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float e0 = Edge(pb, pc, p), e1 = Edge(pc, pa, p), e2 = Edge(pa, pb, p);
            if ((e0 >= 0 && e1 >= 0 && e2 >= 0) || (e0 <= 0 && e1 <= 0 && e2 <= 0)) mask[y * w + x] = true;
        }
    }
}
