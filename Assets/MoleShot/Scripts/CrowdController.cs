using Unity.Profiling;
using UnityEngine;

namespace MoleShot
{
    /// <summary>
    /// Cheap crowd reaction for rigless spectators. On every accepted hit (RoundManager.MoleHit event) a fraction of
    /// the crowd swaps to its baked "cheer" mesh and jumps 1-2 times, starting in a wave from the stand centre, then
    /// returns to its resting mesh/pose. Between reactions an optional per-person sway keeps the stand alive.
    /// Everything is Update-driven with preallocated arrays: no coroutines, no per-frame allocations, and
    /// round start / end snap every spectator back to its base state.
    /// </summary>
    public class CrowdController : MonoBehaviour
    {
        [SerializeField] MoleShotConfig config;
        [SerializeField] RoundManager round;
        [SerializeField] MeshFilter[] people;
        [Tooltip("Resting mesh per person (idle, or cheer for the permanent cheerers).")]
        [SerializeField] Mesh[] restMeshes;
        [Tooltip("Arms-up mesh per person.")]
        [SerializeField] Mesh[] cheerMeshes;
        [SerializeField] int seed = 1234;

        Transform[] tr;
        Vector3[] basePos, baseScale;
        Quaternion[] baseRot;
        float[] phase, start, height;
        int[] jumps;
        bool[] active, swapped;
        float centerX, maxDx;
        int activeCount;
        bool idleDirty;
        System.Random rnd;

        static readonly ProfilerMarker UpdateMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MoleShot.Crowd.Update");

        public int ActiveCount => activeCount;

        /// <summary>Debug / profiling: trigger a reaction without a real hit.</summary>
        public void DebugCheer(bool big) => React(big);
        public int PeopleCount => people != null ? people.Length : 0;

        void Awake()
        {
            int n = people != null ? people.Length : 0;
            tr = new Transform[n];
            basePos = new Vector3[n]; baseScale = new Vector3[n]; baseRot = new Quaternion[n];
            phase = new float[n]; start = new float[n]; height = new float[n];
            jumps = new int[n]; active = new bool[n]; swapped = new bool[n];
            rnd = new System.Random(seed);
            float minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                tr[i] = people[i].transform;
                basePos[i] = tr[i].localPosition;
                baseRot[i] = tr[i].localRotation;
                baseScale[i] = tr[i].localScale;
                phase[i] = (float)rnd.NextDouble() * Mathf.PI * 2f;
                minX = Mathf.Min(minX, basePos[i].x); maxX = Mathf.Max(maxX, basePos[i].x);
            }
            centerX = n > 0 ? (minX + maxX) * 0.5f : 0f;
            maxDx = n > 0 ? Mathf.Max(0.01f, (maxX - minX) * 0.5f) : 1f;
        }

        void OnEnable()
        {
            if (round == null) return;
            round.MoleHit += OnMoleHit;
            round.RoundStarted += ResetAll;
            round.RoundEnded += OnRoundEnded;
            round.LevelEnded += OnLevelEnded;
            round.MenuShown += ResetAll;
        }

        void OnDisable()
        {
            if (round == null) return;
            round.MoleHit -= OnMoleHit;
            round.RoundStarted -= ResetAll;
            round.RoundEnded -= OnRoundEnded;
            round.LevelEnded -= OnLevelEnded;
            round.MenuShown -= ResetAll;
        }

        // A won level gets the big celebration (everyone jumps twice) after the end-of-round reset.
        void OnLevelEnded(bool win) { if (win) React(true); }

        void OnRoundEnded(int _) => ResetAll();

        /// <summary>Snaps every spectator back to its base transform and resting mesh immediately.</summary>
        public void ResetAll()
        {
            if (tr == null) return;
            for (int i = 0; i < tr.Length; i++) Rest(i);
            activeCount = 0;
        }

        void Rest(int i)
        {
            active[i] = false;
            if (swapped[i]) { people[i].sharedMesh = restMeshes[i]; swapped[i] = false; }
            tr[i].localPosition = basePos[i];
            tr[i].localRotation = baseRot[i];
            tr[i].localScale = baseScale[i];
        }

        void OnMoleHit(HoleController hole, int points, int multiplier)
        {
            React(hole.MoleType == MoleType.Fast || multiplier > 1);
        }

        void React(bool big)
        {
            if (config == null || !config.crowdEnabled || tr == null) return;
            float fraction = big ? config.crowdParticipationBig : config.crowdParticipationNormal;
            float h = big ? config.crowdJumpHeightBig : config.crowdJumpHeight;
            float now = Time.time;
            for (int i = 0; i < tr.Length; i++)
            {
                if (active[i] || rnd.NextDouble() >= fraction) continue;
                float wave = Mathf.Abs(basePos[i].x - centerX) / maxDx;
                start[i] = now + config.crowdWaveSpread * wave + (float)rnd.NextDouble() * 0.05f;
                height[i] = h * (0.8f + 0.4f * (float)rnd.NextDouble());
                jumps[i] = big ? 2 : (rnd.NextDouble() < 0.35 ? 2 : 1);
                active[i] = true;
                activeCount++;
            }
        }

        void Update()
        {
            using (UpdateMarker.Auto()) Tick();
        }

        void Tick()
        {
            if (tr == null || config == null) return;
            float sway = config.crowdIdleSway;
            if (activeCount == 0 && sway <= 0f)
            {
                if (idleDirty) { ResetAll(); idleDirty = false; }
                return;
            }
            float now = Time.time;
            float dur = Mathf.Max(0.05f, config.crowdJumpDuration);
            for (int i = 0; i < tr.Length; i++)
            {
                if (active[i])
                {
                    float t = now - start[i];
                    if (t < 0f) { Idle(i, now, sway); continue; }
                    if (t >= jumps[i] * dur) { Rest(i); activeCount--; continue; }
                    if (!swapped[i] && cheerMeshes[i] != restMeshes[i]) { people[i].sharedMesh = cheerMeshes[i]; swapped[i] = true; }
                    float p = (t % dur) / dur;
                    // Airborne for the middle 80% of each jump; squash on take-off / landing.
                    float air = Mathf.Clamp01((p - 0.1f) / 0.8f);
                    float y = height[i] * 4f * air * (1f - air);
                    float squash = p < 0.1f ? Mathf.Sin(p / 0.1f * Mathf.PI) : p > 0.9f ? Mathf.Sin((p - 0.9f) / 0.1f * Mathf.PI) : 0f;
                    Vector3 s = baseScale[i];
                    tr[i].localScale = new Vector3(s.x * (1f + 0.06f * squash), s.y * (1f - 0.1f * squash), s.z * (1f + 0.06f * squash));
                    tr[i].localPosition = basePos[i] + new Vector3(0f, y, 0f);
                    tr[i].localRotation = baseRot[i];
                }
                else if (sway > 0f) Idle(i, now, sway);
            }
            idleDirty = sway > 0f;
        }

        void Idle(int i, float now, float sway)
        {
            float a = Mathf.Sin(now * 1.3f + phase[i]);
            tr[i].localRotation = baseRot[i] * Quaternion.Euler(0f, 0f, a * sway);
            tr[i].localPosition = basePos[i] + new Vector3(0f, Mathf.Abs(Mathf.Sin(now * 2.1f + phase[i] * 1.7f)) * sway * 0.006f, 0f);
        }
    }
}
