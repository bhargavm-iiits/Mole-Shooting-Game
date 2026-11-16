using System.IO;
using UnityEditor;
using UnityEngine;

namespace MoleShot.EditorTools
{
    /// <summary>Exports the procedural crowd sounds as 16-bit WAV files so they can be auditioned outside Unity.</summary>
    public static class MoleShotAudioExport
    {
        [MenuItem("Mole Shot/Export Crowd Audio (WAV)")]
        public static string ExportCrowdWavs() => ExportCrowdWavs(Path.GetFullPath(Path.Combine(Application.dataPath, "../../Deliverables/audio")));

        public static string ExportCrowdWavs(string dir)
        {
            Directory.CreateDirectory(dir);
            Write(Path.Combine(dir, "crowd_cheer_normal.wav"), CrowdCheerSynth.Cheer(false));
            Write(Path.Combine(dir, "crowd_cheer_big.wav"), CrowdCheerSynth.Cheer(true));
            Write(Path.Combine(dir, "crowd_murmur_loop.wav"), CrowdCheerSynth.Murmur());
            return dir;
        }

        static void Write(string path, float[] data)
        {
            int sr = CrowdCheerSynth.SampleRate;
            using var w = new BinaryWriter(File.Create(path));
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data.Length * 2);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(data.Length * 2);
            foreach (var v in data) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
        }
    }
}
