using System;
using UnityEngine;

namespace MoleShot
{
    /// <summary>
    /// Authoritative game state: level select menu, level intro, play (timer, score, combo, goal, difficulty,
    /// spawning) and the win / time's-up result. Spawning is Update-driven (no coroutines), so restarting a level
    /// can never leak work from the previous one.
    /// </summary>
    public class RoundManager : MonoBehaviour
    {
        [SerializeField] MoleShotConfig config;
        [SerializeField] HoleController[] holes;
        [SerializeField] BallController ball;
        [SerializeField] SwipeInput swipeInput;
        [SerializeField] ShotController shots;
        [SerializeField] FeedbackController feedback;
        [SerializeField] AudioFeedback audioFeedback;
        [SerializeField] HudController hud;
        [Tooltip("Debug: disable to stop moles spawning (used by the shot-accuracy test).")]
        public bool spawningEnabled = true;
        [Tooltip("Debug/tests: skip the LEVEL n intro banner.")]
        public bool skipIntro;
        [Tooltip("Debug: start this level directly instead of showing the level select (-1 = show menu).")]
        public int autoStartLevel = -1;

        public event Action<int> RoundEnded;
        /// <summary>Raised once per accepted hit: (hole, points awarded, multiplier used).</summary>
        public event Action<HoleController, int, int> MoleHit;
        /// <summary>Raised at the start of every level attempt (including replay / next level).</summary>
        public event Action RoundStarted;
        /// <summary>Raised once when a level ends: true = goal reached (win), false = time's up.</summary>
        public event Action<bool> LevelEnded;
        /// <summary>Raised when the level select menu is shown.</summary>
        public event Action MenuShown;

        RoundState state = RoundState.Menu;
        int levelIndex;
        float introLeft;
        float timeLeft;
        float spawnTimer;
        int score, comboHits, best, lastHoleIndex = -1;
        int totalHits, totalMisses, roundGeneration;

        public RoundState State => state;
        public bool IsPlaying => state == RoundState.Playing;
        public float TimeLeft => timeLeft;
        public float IntroLeft => introLeft;
        public int Score => score;
        public int Best => best;
        public int ComboHits => comboHits;
        public int TotalHits => totalHits;
        public int TotalMisses => totalMisses;
        public int RoundGeneration => roundGeneration;
        public bool LastWin { get; private set; }
        /// <summary>Stars earned by the last finished level (0 on time's up).</summary>
        public int LastStars { get; private set; }
        public HoleController[] Holes => holes;
        public MoleShotConfig Config => config;
        public int LevelIndex => levelIndex;
        public int LevelCount => config.levels.Length;
        public LevelDefinition Level => config.levels[Mathf.Clamp(levelIndex, 0, config.levels.Length - 1)];
        public float Duration => Level.duration;
        public int TargetHits => Level.targetHits;
        public bool HasNextLevel => levelIndex + 1 < config.levels.Length;
        public float Progress => Level.duration <= 0f ? 1f : 1f - timeLeft / Level.duration;
        public float Difficulty => config.Difficulty(Progress);

        public int Multiplier
        {
            get
            {
                if (!config.comboEnabled) return 1;
                return Mathf.Clamp(1 + comboHits / Mathf.Max(1, config.hitsPerComboStep), 1, config.maxMultiplier);
            }
        }

        void Awake() => ApplyRuntimeSettings();

        /// <summary>Frame rate, physics rate and gravity. The trajectory preview reads the same fixedDeltaTime.</summary>
        void ApplyRuntimeSettings()
        {
            if (Application.isMobilePlatform) QualitySettings.vSyncCount = 0;
            int fps = config.targetFrameRate;
            if (config.useDisplayRefreshRate)
            {
                double hz = Screen.currentResolution.refreshRateRatio.value;
                if (hz > 1.0) fps = Mathf.Clamp(Mathf.RoundToInt((float)hz), 30, 120);
            }
            Application.targetFrameRate = fps;
            Time.fixedDeltaTime = 1f / Mathf.Max(30, config.physicsRate);
            Physics.gravity = new Vector3(0f, -config.gravity, 0f);
        }

        void Start()
        {
            if (autoStartLevel >= 0) StartLevel(autoStartLevel);
            else ShowMenu();
        }

        /// <summary>Back to the level select screen: everything stopped and reset.</summary>
        public void ShowMenu()
        {
            roundGeneration++;
            StopPlay();
            state = RoundState.Menu;
            if (feedback != null) feedback.ClearAll();
            if (shots != null) shots.OnRoundEnded();
            if (hud != null) hud.ShowMenu();
            MenuShown?.Invoke();
        }

        /// <summary>Replays the current level.</summary>
        public void Restart() => StartLevel(levelIndex);

        public void NextLevel() => StartLevel(Mathf.Min(levelIndex + 1, config.levels.Length - 1));

        public void StartLevel(int index)
        {
            roundGeneration++;
            levelIndex = Mathf.Clamp(index, 0, config.levels.Length - 1);
            Physics.gravity = new Vector3(0f, -config.gravity, 0f);
            timeLeft = Level.duration;
            best = MoleShotProgress.Best(levelIndex);
            score = 0; comboHits = 0; totalHits = 0; totalMisses = 0; lastHoleIndex = -1;
            spawnTimer = config.firstSpawnDelay;
            StopPlay();
            if (feedback != null) feedback.ClearAll();
            introLeft = skipIntro ? 0f : config.levelIntroDuration;
            state = introLeft > 0f ? RoundState.Intro : RoundState.Playing;
            if (swipeInput != null) swipeInput.inputEnabled = state == RoundState.Playing;
            if (shots != null) shots.OnRoundStarted();
            if (hud != null) hud.OnRoundStarted(state == RoundState.Intro);
            RoundStarted?.Invoke();
            MoleShotDebug.Log($"LEVEL START {Level.name} duration={Level.duration} target={Level.targetHits} gen={roundGeneration}");
        }

        void StopPlay()
        {
            if (swipeInput != null) { swipeInput.CancelActive(); swipeInput.inputEnabled = false; }
            if (ball != null) ball.ForceReset();
            foreach (var h in holes) h.ForceHide();
        }

        void Update()
        {
            if (state == RoundState.Intro)
            {
                introLeft -= Time.deltaTime;
                if (introLeft <= 0f)
                {
                    introLeft = 0f;
                    state = RoundState.Playing;
                    if (swipeInput != null) swipeInput.inputEnabled = true;
                }
                return;
            }
            if (state != RoundState.Playing) return;
            timeLeft -= Time.deltaTime;
            if (timeLeft <= 0f) { timeLeft = 0f; EndRound(false); return; }

            if (!spawningEnabled) return;
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                if (TrySpawn())
                {
                    float d = Difficulty;
                    spawnTimer = MoleShotConfig.Lerp(Level.spawnInterval, d) + UnityEngine.Random.Range(-config.spawnJitter, config.spawnJitter);
                    spawnTimer = Mathf.Max(0.15f, spawnTimer);
                }
                else spawnTimer = 0.1f; // all holes busy / max active reached: retry shortly
            }
        }

        bool TrySpawn()
        {
            float d = Difficulty;
            int active = 0;
            foreach (var h in holes) if (!h.IsHidden) active++;
            int maxActive = Mathf.Max(1, Mathf.RoundToInt(MoleShotConfig.Lerp(Level.maxActiveMoles, d)));
            if (active >= maxActive) return false;

            // Pick a random free hole, avoiding the last used one when possible.
            int count = 0;
            Span<int> free = stackalloc int[holes.Length];
            for (int i = 0; i < holes.Length; i++)
                if (holes[i].IsFree && i != lastHoleIndex) free[count++] = i;
            if (count == 0 && lastHoleIndex >= 0 && holes[lastHoleIndex].IsFree) free[count++] = lastHoleIndex;
            if (count == 0) return false;

            int index = free[UnityEngine.Random.Range(0, count)];
            bool fast = UnityEngine.Random.value < MoleShotConfig.Lerp(Level.fastMoleChance, d);
            float window = fast ? FastWindow(d) : MoleShotConfig.Lerp(Level.normalVisible, d);
            if (!holes[index].Spawn(fast ? MoleType.Fast : MoleType.Normal, window)) return false;
            lastHoleIndex = index;
            MoleShotDebug.Log($"SPAWN hole={index} type={(fast ? "Fast" : "Normal")} window={window:0.00} d={d:0.00} t={timeLeft:0.00}");
            return true;
        }

        /// <summary>Fast-mole hittable window for a level at difficulty d (never below fastVisibleFloor).</summary>
        public float FastWindow(float d, LevelDefinition level = null)
            => Mathf.Max(config.fastVisibleFloor, MoleShotConfig.Lerp((level ?? Level).fastVisible, d));

        /// <summary>Called by the ball on contact. Checks state and asks the hole atomically.</summary>
        public bool TryScoreHit(HoleController hole)
        {
            if (state != RoundState.Playing || hole == null) return false;
            if (!hole.TryHit()) return false;

            int mult = Multiplier;
            int basePts = hole.MoleType == MoleType.Fast ? config.fastPoints : config.normalPoints;
            int pts = basePts * mult;
            score += pts;
            comboHits++;
            totalHits++;
            if (feedback != null) feedback.ShowHit(hole, pts, mult);
            if (audioFeedback != null)
            {
                audioFeedback.PlayHit(hole.MoleType == MoleType.Fast);
                audioFeedback.PlayCheer(hole.MoleType == MoleType.Fast || mult > 1);
            }
            MoleHit?.Invoke(hole, pts, mult);
            if (hud != null) hud.PulseScore();
            MoleShotDebug.Log($"HIT type={hole.MoleType} base={basePts} mult={mult} pts={pts} score={score} hits={totalHits}/{TargetHits} t={timeLeft:0.00}");
            if (totalHits >= TargetHits) EndRound(true);   // goal reached: the level is won immediately
            return true;
        }

        public void ReportMiss(Vector3 point, bool outOfBounds)
        {
            if (state != RoundState.Playing) return;
            comboHits = 0;
            totalMisses++;
            if (feedback != null) feedback.ShowMiss(point, outOfBounds);
            if (audioFeedback != null) audioFeedback.PlayMiss();
            MoleShotDebug.Log($"MISS oob={outOfBounds} at={point} score={score} t={timeLeft:0.00}");
        }

        public HoleController FindHittableHoleNear(Vector3 point, float maxDistance)
        {
            HoleController bestHole = null;
            float bestD = maxDistance;
            foreach (var h in holes)
            {
                if (!h.IsHittable) continue;
                Vector3 d = h.Center - point; d.y = 0f;
                float m = d.magnitude;
                if (m <= bestD) { bestD = m; bestHole = h; }
            }
            return bestHole;
        }

        void EndRound(bool win)
        {
            if (state != RoundState.Playing) return;
            state = RoundState.Ended;
            LastWin = win;
            // Input stops, spawning stops (state), moles hide; a ball still in the air can no longer score because
            // the state is not Playing. On a win the bonked mole keeps its hit reaction and the ball its bounce.
            if (swipeInput != null) { swipeInput.CancelActive(); swipeInput.inputEnabled = false; }
            if (ball != null && !win) ball.ForceReset();
            foreach (var h in holes) if (!(win && h.State == MoleState.Hit)) h.ForceHide();
            if (shots != null) shots.OnRoundEnded();
            // Floating "+N xM" / MISS popups would show through the semi-transparent result card: clear them.
            if (feedback != null) feedback.ClearAll();
            LastStars = MoleShotProgress.ComputeStars(win, timeLeft, Level.duration, totalHits, totalMisses);
            bool newBest = MoleShotProgress.Record(levelIndex, score, win, LastStars, config.levels.Length);
            best = MoleShotProgress.Best(levelIndex);
            if (audioFeedback != null) { audioFeedback.PlayWhistle(); if (win) audioFeedback.PlayCheer(true); }
            if (hud != null) hud.ShowLevelResult(win, newBest);
            MoleShotDebug.Log($"ROUND END win={win} level={levelIndex + 1} score={score} best={best} hits={totalHits}/{TargetHits} misses={totalMisses} timeLeft={timeLeft:0.0} gen={roundGeneration}");
            RoundEnded?.Invoke(score);
            LevelEnded?.Invoke(win);
        }

        /// <summary>Debug/test helper: set the moles-hit counter (e.g. one short of the goal).</summary>
        public void DebugSetHits(int hits) => totalHits = Mathf.Max(0, hits);

        /// <summary>Debug/test helper: jump the timer (e.g. to test expiry during flight).</summary>
        public void DebugSetTimeLeft(float seconds) => timeLeft = Mathf.Max(0.001f, seconds);
    }
}
