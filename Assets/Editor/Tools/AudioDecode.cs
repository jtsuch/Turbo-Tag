using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Decodes audio assets to 16-bit WAVs in Library/TurboTag/scratch so Claude can chop and process them outside Unity
/// (no audio tools on this PC): job "DecodeAudio" reads asset paths, one per line, from
/// Library/TurboTag/scratch/decode_request.txt and writes scratch/{file name}.wav for each. The clip must load
/// decompressed (the default for short clips).
/// </summary>
public static class AudioDecode
{
    private const string Scratch = "Library/TurboTag/scratch";

    public static void Run()
    {
        string request = $"{Scratch}/decode_request.txt";
        if (!File.Exists(request)) { Debug.LogWarning("[AudioDecode] no decode_request.txt"); return; }
        foreach (string line in File.ReadAllLines(request))
        {
            string path = line.Trim();
            if (path.Length == 0) continue;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) { Debug.LogError($"[AudioDecode] {path} did not load"); continue; }
            clip.LoadAudioData();
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) { Debug.LogError($"[AudioDecode] {path}: GetData failed"); continue; }
            string outPath = $"{Scratch}/{Path.GetFileNameWithoutExtension(path)}.wav";
            using var w = new BinaryWriter(File.Create(outPath));
            int bytes = data.Length * 2;
            w.Write("RIFF".ToCharArray()); w.Write(36 + bytes); w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)clip.channels);
            w.Write(clip.frequency); w.Write(clip.frequency * clip.channels * 2); w.Write((short)(clip.channels * 2)); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(bytes);
            foreach (float f in data) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(f * 32767f), -32768, 32767));
            Debug.Log($"[AudioDecode] {path}: {clip.length:0.00} s, {clip.channels} ch, {clip.frequency} Hz -> {outPath}");
        }
    }
}
