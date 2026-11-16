using UnityEngine;

namespace MoleShot
{
    /// <summary>
    /// Every gameplay tunable in one asset. Vector2 "ranges" are [round start, round end] values that are
    /// lerped by the current difficulty (0..1) when progressive difficulty is enabled.
    /// </summary>
    [CreateAssetMenu(menuName = "Mole Shot/Config", fileName = "MoleShotConfig")]
    public class MoleShotConfig : ScriptableObject
    {
        [Header("Levels")]
        [Tooltip("Level 1 = 60 s session (the brief's 45-60 s); levels 2/3 extend it by design.")]
        public LevelDefinition[] levels =
        {
            new LevelDefinition("LEVEL 1", 60f, 10, new Vector2(1.5f, 1.1f), new Vector2(1f, 1f),
                                new Vector2(2.8f, 2.2f), new Vector2(1.8f, 1.55f), new Vector2(0.15f, 0.25f)),
            new LevelDefinition("LEVEL 2", 90f, 15, new Vector2(1.3f, 0.9f), new Vector2(1f, 2f),
                                new Vector2(2.4f, 1.9f), new Vector2(1.6f, 1.4f), new Vector2(0.2f, 0.3f)),
            new LevelDefinition("LEVEL 3", 120f, 20, new Vector2(1.1f, 0.75f), new Vector2(1.6f, 2f),
                                new Vector2(2.1f, 1.7f), new Vector2(1.45f, 1.25f), new Vector2(0.3f, 0.4f)),
        };
        [Tooltip("Seconds the LEVEL n banner shows before the timer and spawning start.")]
        [Range(0f, 3f)] public float levelIntroDuration = 1.1f;

        [Header("Smoothness")]
        [Tooltip("Application.targetFrameRate (Android defaults to 30 fps).")]
        [Range(30, 120)] public int targetFrameRate = 60;
        [Tooltip("Use the display refresh rate instead (capped at 120 Hz).")]
        public bool useDisplayRefreshRate = false;
        [Tooltip("Physics steps per second (Time.fixedDeltaTime = 1 / this). The preview uses the same step.")]
        [Range(30, 120)] public int physicsRate = 60;

        [Header("Physics")]
        [Tooltip("Downward gravity (m/s^2). Applied to Physics.gravity at start and used by the trajectory preview.")]
        [Range(5f, 40f)] public float gravity = 34f;

        [Header("Swipe -> Shot")]
        [Tooltip("Minimum swipe length as a fraction of screen height. Shorter swipes are treated as taps and rejected.")]
        [Range(0.005f, 0.2f)] public float minSwipe = 0.045f;
        [Tooltip("Swipe length (fraction of screen height) that maps to maxRange.")]
        [Range(0.1f, 1f)] public float fullSwipe = 0.36f;
        [Tooltip("Ground distance (m) of the shortest valid swipe.")]
        [Range(0.5f, 10f)] public float minRange = 3.2f;
        [Tooltip("Ground distance (m) of a full-length swipe.")]
        [Range(3f, 30f)] public float maxRange = 11.5f;
        [Tooltip("Shape of the swipe-length -> range curve (1 = linear, >1 = finer control at short range).")]
        [Range(0.3f, 3f)] public float rangeExponent = 1f;
        [Tooltip("Fixed launch elevation angle in degrees.")]
        [Range(10f, 70f)] public float launchAngle = 28f;
        [Tooltip("Hard cap on launch speed (m/s).")]
        [Range(5f, 50f)] public float maxSpeed = 26f;
        [Tooltip("Maximum horizontal aim angle either side of straight ahead (degrees).")]
        [Range(5f, 80f)] public float maxYaw = 32f;
        [Tooltip("Multiplier on the perspective-correct aim angle (1 = shoot exactly where the swipe points on screen).")]
        [Range(0.3f, 2f)] public float yawGain = 1f;
        [Tooltip("A press only starts aiming within this radius (fraction of screen height) of the ball's screen position.")]
        [Range(0.05f, 1f)] public float launchAreaRadius = 0.26f;
        [Tooltip("Height (m) at which the ball should pass the aimed ground distance (roughly mole body height).")]
        [Range(0f, 1.5f)] public float targetHeight = 0.45f;

        [Header("Shot resolution")]
        [Tooltip("A shot that has not resolved after this many seconds counts as a miss.")]
        [Range(1f, 10f)] public float shotTimeout = 3f;
        [Tooltip("Delay before the ball returns to the launch mark after a hit.")]
        [Range(0f, 3f)] public float resetDelayAfterHit = 0.45f;
        [Tooltip("Delay before the ball returns to the launch mark after a miss.")]
        [Range(0f, 3f)] public float resetDelayAfterMiss = 0.5f;
        [Tooltip("A ground impact within this distance (m) of a currently hittable mole still counts as a hit.")]
        [Range(0f, 1.5f)] public float holeCatchRadius = 0.6f;

        [Header("Spawning (per-level ranges live in Levels)")]
        [Range(0f, 5f)] public float firstSpawnDelay = 1.0f;
        [Tooltip("Random +/- seconds added to each spawn interval.")]
        [Range(0f, 1f)] public float spawnJitter = 0.25f;
        [Tooltip("Seconds a hole stays empty after its mole hides before it can be used again.")]
        [Range(0f, 3f)] public float holeCooldown = 0.5f;

        [Header("Mole timing")]
        [Tooltip("Seconds for the full rise (slow peek, short hold, then up with a small overshoot).")]
        [Range(0.05f, 1.5f)] public float emergeDuration = 0.25f;
        [Tooltip("Fraction of the rise height at which the mole becomes hittable (a ball arriving at a visibly emerged mole counts).")]
        [Range(0.2f, 1f)] public float hittableFromEmergeFraction = 0.6f;
        [Range(0.05f, 1f)] public float retreatDuration = 0.25f;
        [Tooltip("A fast mole is never hittable for less than this (keeps back-row shots reachable).")]
        [Range(0.3f, 3f)] public float fastVisibleFloor = 1.25f;
        [Tooltip("The mole wobbles during the last N seconds of its window as a 'about to duck' warning.")]
        [Range(0f, 1f)] public float retreatWarning = 0.5f;
        [Tooltip("Length of the squash reaction after a hit.")]
        [Range(0.1f, 1.5f)] public float hitReactionDuration = 0.55f;

        [Header("Scoring")]
        [Range(1, 10)] public int normalPoints = 1;
        [Range(1, 10)] public int fastPoints = 2;
        public bool comboEnabled = true;
        [Tooltip("Consecutive hits needed per +1 multiplier step.")]
        [Range(1, 10)] public int hitsPerComboStep = 3;
        [Range(1, 10)] public int maxMultiplier = 3;

        [Header("Difficulty")]
        public bool progressiveDifficulty = true;
        [Tooltip("Maps round progress (0..1) to difficulty (0..1).")]
        public AnimationCurve difficultyCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Crowd")]
        [Tooltip("Crowd reacts (jumps / cheers) when a mole is hit.")]
        public bool crowdEnabled = true;
        [Tooltip("Fraction of spectators that react to a normal hit.")]
        [Range(0f, 1f)] public float crowdParticipationNormal = 0.6f;
        [Tooltip("Fraction that react to a fast-mole hit or a combo (x2 / x3) hit.")]
        [Range(0f, 1f)] public float crowdParticipationBig = 1f;
        [Tooltip("Jump height (m) for a normal hit (randomised +/-20% per person).")]
        [Range(0f, 1f)] public float crowdJumpHeight = 0.24f;
        [Tooltip("Jump height (m) for a big hit (fast mole / combo); big hits also jump twice.")]
        [Range(0f, 1f)] public float crowdJumpHeightBig = 0.33f;
        [Tooltip("Seconds per jump.")]
        [Range(0.2f, 1.5f)] public float crowdJumpDuration = 0.45f;
        [Tooltip("Max start delay (s) of the reaction wave from the stand centre to its edges.")]
        [Range(0f, 1f)] public float crowdWaveSpread = 0.2f;
        [Tooltip("Idle life: sway amplitude in degrees (0 = fully static crowd between reactions).")]
        [Range(0f, 6f)] public float crowdIdleSway = 1.5f;
        [Tooltip("Volume of the crowd cheer on a normal hit (played on its own AudioSource).")]
        [Range(0f, 1f)] public float crowdCheerVolume = 0.85f;
        [Tooltip("Volume of the longer, brighter cheer on a fast-mole / combo hit.")]
        [Range(0f, 1f)] public float crowdCheerVolumeBig = 1f;
        [Tooltip("Quiet looping crowd murmur so the stadium feels alive.")]
        public bool crowdAmbience = true;
        [Range(0f, 0.5f)] public float crowdAmbienceVolume = 0.1f;

        [Header("Play area")]
        [Tooltip("Ball is out of bounds when |x| exceeds this (m).")]
        public float boundsHalfWidth = 9f;
        [Tooltip("Ball is out of bounds beyond this z (m).")]
        public float boundsFar = 16f;
        [Tooltip("Ball is out of bounds behind this z (m).")]
        public float boundsNear = -4f;

        public float Difficulty(float progress01)
        {
            if (!progressiveDifficulty) return 0f;
            return Mathf.Clamp01(difficultyCurve.Evaluate(Mathf.Clamp01(progress01)));
        }

        public static float Lerp(Vector2 range, float d) => Mathf.Lerp(range.x, range.y, d);
    }
}
