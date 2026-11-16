using UnityEngine;

namespace MoleShot
{
    /// <summary>
    /// Procedural stadium-crowd sounds (no audio assets):
    /// - Cheer: 28-40 "voices" (PolyBLEP sawtooth, 180-400 Hz, gliding up a few semitones with vibrato) through two
    ///   vowel formant band-passes (~650 Hz and ~1.25 kHz) = a "whoa / yeah" roar, plus breath noise and 15-30 claps,
    ///   under an 80 ms swell / sustain / release envelope. Normalised to -3 dBFS.
    /// - Murmur: a quiet, seamless crowd-ambience loop.
    /// Deterministic (seeded) so the sound is the same on every device.
    /// </summary>
    public static class CrowdCheerSynth
    {
        public const int SampleRate = 44100;
        const float PeakTarget = 0.708f; // -3 dBFS

        public static float[] Cheer(bool big, int seed = 7)
        {
            var rnd = new System.Random(seed + (big ? 1000 : 0));
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

            float dur = big ? 2.0f : 1.5f;
            float attack = 0.08f, sustainEnd = big ? 1.05f : 0.7f;
            int n = Mathf.CeilToInt(dur * SampleRate);
            var voicesSum = new float[n];

            // ---- voices ("whoa / yeah") ----
            int voices = big ? 40 : 28;
            for (int v = 0; v < voices; v++)
            {
                float f0 = R(180f, big ? 420f : 380f);
                float glide = R(2f, big ? 6f : 4.5f);       // semitones up over the first part
                float vibHz = R(4.5f, 7f), vibDepth = R(0.008f, 0.02f), vibPh = R(0f, 6.28f);
                float start = R(0f, 0.15f), vEnd = R(sustainEnd * 0.85f, dur - 0.05f);
                float amp = R(0.5f, 1f);
                float jitterAmt = R(0.002f, 0.006f);
                float ph = R(0f, 1f), jitter = 0f, vAtk = R(0.04f, 0.12f);
                uint js = (uint)rnd.Next();
                int s0 = Mathf.FloorToInt(start * SampleRate);
                for (int i = s0; i < n; i++)
                {
                    float t = i / (float)SampleRate;
                    float lt = t - start;
                    if (t > vEnd + 0.25f) break;
                    // pitch: glide up over ~0.45 s, then sag slightly
                    float semis = glide * Mathf.Clamp01(lt / 0.45f) - 1.5f * Mathf.Clamp01((lt - 0.6f) / 1.2f);
                    jitter += (Noise(ref js) - jitter) * 0.002f;
                    float f = f0 * Mathf.Pow(2f, semis / 12f) * (1f + vibDepth * Mathf.Sin(2f * Mathf.PI * vibHz * lt + vibPh) + jitter * jitterAmt * 20f);
                    float dt = f / SampleRate;
                    ph += dt; if (ph >= 1f) ph -= 1f;
                    float saw = 2f * ph - 1f - PolyBlep(ph, dt);
                    float env = Mathf.Clamp01(lt / vAtk) * Mathf.Clamp01((vEnd + 0.25f - t) / 0.35f);
                    voicesSum[i] += saw * amp * env;
                }
            }

            // ---- vowel formants on the voice bus + breath noise ----
            var f1 = Biquad.BandPass(650f, 1.8f);
            var f2 = Biquad.BandPass(1250f, 2.5f);
            var f3 = Biquad.BandPass(2600f, 3f);
            var breathBp = Biquad.BandPass(1100f, 0.8f);
            var outBuf = new float[n];
            uint noiseSeed = (uint)(seed * 7919 + (big ? 17 : 3));
            for (int i = 0; i < n; i++)
            {
                float x = voicesSum[i];
                float formants = f1.Process(x) * 1.0f + f2.Process(x) * 0.7f + f3.Process(x) * (big ? 0.35f : 0.2f);
                float breath = breathBp.Process(Noise(ref noiseSeed)) * 0.9f;
                outBuf[i] = formants + breath;
            }

            // ---- claps: short high-passed noise transients ----
            int claps = big ? rnd.Next(24, 31) : rnd.Next(15, 23);
            var clapHp = Biquad.HighPass(1200f, 0.7f);
            var clapBus = new float[n];
            for (int c = 0; c < claps; c++)
            {
                float t0 = R(0.05f, sustainEnd + 0.25f);
                float len = R(0.008f, 0.016f), a = R(0.35f, 1f);
                int s = Mathf.FloorToInt(t0 * SampleRate), e = Mathf.Min(n, s + Mathf.CeilToInt(len * 4f * SampleRate));
                for (int i = s; i < e; i++)
                {
                    float lt = (i - s) / (float)SampleRate;
                    clapBus[i] += Noise(ref noiseSeed) * a * Mathf.Exp(-lt / len);
                }
            }
            for (int i = 0; i < n; i++) outBuf[i] += clapHp.Process(clapBus[i]) * 0.55f;

            // ---- master envelope: quick swell, sustain, release ----
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = t < attack ? Mathf.SmoothStep(0f, 1f, t / attack)
                          : t < sustainEnd ? 1f
                          : Mathf.Pow(Mathf.Clamp01(1f - (t - sustainEnd) / (dur - sustainEnd)), 1.6f);
                outBuf[i] *= env;
            }
            Normalize(outBuf, PeakTarget);
            return outBuf;
        }

        /// <summary>Quiet, seamless murmur loop (~4 s): breathy formant noise with slow swells.</summary>
        public static float[] Murmur(float seconds = 4f, int seed = 3)
        {
            int n = Mathf.CeilToInt(seconds * SampleRate);
            int fade = SampleRate / 2;
            var tmp = new float[n + fade];
            uint ns = (uint)(seed * 104729 + 1);
            var a = Biquad.BandPass(500f, 1.2f);
            var b = Biquad.BandPass(1100f, 1.5f);
            for (int i = 0; i < tmp.Length; i++)
            {
                float t = i / (float)SampleRate;
                float w = Noise(ref ns);
                float swell = 0.75f + 0.15f * Mathf.Sin(2f * Mathf.PI * 0.37f * t) + 0.1f * Mathf.Sin(2f * Mathf.PI * 1.13f * t + 1.3f);
                tmp[i] = (a.Process(w) + 0.6f * b.Process(w)) * swell;
            }
            // crossfade the tail into the head so the loop is seamless
            var outBuf = new float[n];
            for (int i = 0; i < n; i++) outBuf[i] = tmp[i];
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                outBuf[i] = tmp[n + i] * (1f - k) + tmp[i] * k;
            }
            Normalize(outBuf, PeakTarget);
            return outBuf;
        }

        public static AudioClip ToClip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void Normalize(float[] d, float target)
        {
            float peak = 0f;
            for (int i = 0; i < d.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            if (peak < 1e-6f) return;
            float g = target / peak;
            for (int i = 0; i < d.Length; i++) d[i] *= g;
        }

        static float PolyBlep(float t, float dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1f; }
            if (t > 1f - dt) { t = (t - 1f) / dt; return t * t + t + t + 1f; }
            return 0f;
        }

        static float Noise(ref uint s)
        {
            s = s * 1664525u + 1013904223u;
            return (s >> 8) / 8388608f - 1f;
        }

        /// <summary>RBJ biquad (transposed direct form II).</summary>
        struct Biquad
        {
            float b0, b1, b2, a1, a2, z1, z2;

            public static Biquad BandPass(float fc, float q)
            {
                float w = 2f * Mathf.PI * fc / SampleRate, alpha = Mathf.Sin(w) / (2f * q), a0 = 1f + alpha;
                return new Biquad { b0 = alpha / a0, b1 = 0f, b2 = -alpha / a0, a1 = -2f * Mathf.Cos(w) / a0, a2 = (1f - alpha) / a0 };
            }

            public static Biquad HighPass(float fc, float q)
            {
                float w = 2f * Mathf.PI * fc / SampleRate, cs = Mathf.Cos(w), alpha = Mathf.Sin(w) / (2f * q), a0 = 1f + alpha;
                return new Biquad { b0 = (1f + cs) / 2f / a0, b1 = -(1f + cs) / a0, b2 = (1f + cs) / 2f / a0, a1 = -2f * cs / a0, a2 = (1f - alpha) / a0 };
            }

            public float Process(float x)
            {
                float y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }
        }
    }
}
