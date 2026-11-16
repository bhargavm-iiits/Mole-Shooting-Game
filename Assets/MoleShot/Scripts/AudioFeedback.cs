using UnityEngine;

namespace MoleShot
{
    /// <summary>
    /// Tiny procedural sound set generated at startup (no audio assets): kick, hit pop, fast-hit chime,
    /// miss thud and the end-of-round whistle.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioFeedback : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 0.8f;
        [SerializeField] MoleShotConfig config;

        const int Rate = 44100;
        AudioSource source;
        AudioClip kick, pop, chime, thud, whistle, cheer, cheerBig, murmur;
        AudioSource crowdSource, ambienceSource;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            kick = Make("kick", 0.16f, (t, i) => Mathf.Sin(2f * Mathf.PI * (55f * t + 180f * (1f - Mathf.Exp(-t * 30f)) / 30f)) * Mathf.Exp(-t * 22f)
                                              + Noise(i) * 0.35f * Mathf.Exp(-t * 90f));
            pop = Make("pop", 0.14f, (t, i) => Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(900f, 520f, t / 0.14f) * t) * Mathf.Exp(-t * 28f));
            chime = Make("chime", 0.35f, (t, i) => (Mathf.Sin(2f * Mathf.PI * 1318.5f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 1975.5f * t) * 0.4f)
                                                    * Mathf.Exp(-t * 9f) * Mathf.Clamp01(t * 200f));
            thud = Make("thud", 0.18f, (t, i) => (Mathf.Sin(2f * Mathf.PI * 90f * t) * 0.7f + Noise(i) * 0.3f) * Mathf.Exp(-t * 24f));
            // Crowd sounds get their own sources so hit/kick one-shots (and their pitch changes) never affect them.
            cheer = CrowdCheerSynth.ToClip("crowd_cheer", CrowdCheerSynth.Cheer(false));
            cheerBig = CrowdCheerSynth.ToClip("crowd_cheer_big", CrowdCheerSynth.Cheer(true));
            crowdSource = NewSource("CrowdCheer", 0);
            if (config == null || config.crowdAmbience)
            {
                murmur = CrowdCheerSynth.ToClip("crowd_murmur", CrowdCheerSynth.Murmur());
                ambienceSource = NewSource("CrowdAmbience", 64);
                ambienceSource.clip = murmur;
                ambienceSource.loop = true;
                ambienceSource.volume = (config != null ? config.crowdAmbienceVolume : 0.1f) * volume;
                ambienceSource.Play();
            }
            whistle = Make("whistle", 0.9f, (t, i) =>
            {
                float f = 2350f + 120f * Mathf.Sin(2f * Mathf.PI * 32f * t);
                float env = Mathf.Clamp01(t * 30f) * Mathf.Clamp01((0.9f - t) * 10f);
                return (Mathf.Sin(2f * Mathf.PI * f * t) * 0.7f + Noise(i) * 0.08f) * env * 0.6f;
            });
        }

        static uint seed = 12345;
        static float Noise(int i)
        {
            seed = seed * 1664525u + 1013904223u;
            return (seed >> 8) / 8388608f - 1f;
        }

        static AudioClip Make(string name, float seconds, System.Func<float, int, float> fn)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float fade = Mathf.Clamp01((n - i) / (Rate * 0.01f)); // avoid clicks at the end
                data[i] = Mathf.Clamp(fn(t, i) * fade, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        AudioSource NewSource(string name, int priority)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.priority = priority;
            return src;
        }

        void Play(AudioClip clip, float vol, float pitch = 1f)
        {
            if (clip == null || source == null) return;
            source.pitch = pitch;
            source.PlayOneShot(clip, vol * volume);
        }

        public void PlayKick(float power01) => Play(kick, 0.9f, Mathf.Lerp(1.1f, 0.9f, power01));
        public void PlayHit(bool fast) { Play(pop, 0.75f); if (fast) Play(chime, 0.55f); }
        /// <summary>Crowd roar; louder for fast-mole or combo hits.</summary>
        public void PlayCheer(bool big)
        {
            if (crowdSource == null) return;
            float v = config != null ? (big ? config.crowdCheerVolumeBig : config.crowdCheerVolume) : (big ? 1f : 0.85f);
            crowdSource.PlayOneShot(big ? cheerBig : cheer, v * volume);
        }

        /// <summary>Star stamp chime on the result popup; each star a step higher.</summary>
        public void PlayStar(int index) => Play(chime, 0.55f, 1f + 0.12f * index);

        public void PlayMiss() => Play(thud, 0.6f);
        public void PlayWhistle() => Play(whistle, 0.55f);
    }
}
