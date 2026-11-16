using UnityEngine;

namespace MoleShot
{
    /// <summary>
    /// Debug bot for automated play-tests and demo recording. Disabled by default and outside the normal gameplay
    /// path: it drives ShotController exactly like a finger would (BeginAim -> UpdateAim... -> ReleaseAim), using
    /// ShotSolver.SwipeForTarget to invert the swipe mapping. It reacts with a human-ish delay and misses on purpose
    /// some of the time.
    /// </summary>
    public class AutoPlayer : MonoBehaviour
    {
        [SerializeField] ShotController shots;
        [SerializeField] RoundManager round;
        [SerializeField] HudController hud;
        [SerializeField] MoleShotConfig config;

        [Tooltip("Seconds between a mole becoming hittable and the bot starting to aim.")]
        public Vector2 reactionDelay = new Vector2(0.25f, 0.45f);
        [Tooltip("Seconds the bot spends 'dragging' (preview visible) before release.")]
        public Vector2 aimDuration = new Vector2(0.3f, 0.45f);
        [Range(0f, 1f)] public float deliberateMissChance = 0.2f;
        [Tooltip("Always miss on purpose on this shot number of each level (1-based, 0 = off), e.g. so a demo shows a miss.")]
        public int forceMissOnShot = 0;
        [Tooltip("Press NEXT LEVEL (after a win) or RETRY this many seconds after the results appear (<0 = never).")]
        public float autoRestartDelay = 2.5f;
        [Tooltip("Stop pressing result buttons after this many presses (keeps a recording bounded).")]
        public int maxAutoRestarts = 2;
        [Tooltip("On the level select, start `startLevel` automatically after menuDelay seconds.")]
        public bool autoNavigate = false;
        [Tooltip("Level index (0-based) the AutoPlayer starts from the level select.")]
        public int startLevel = 0;
        public float menuDelay = 1.5f;

        enum Phase { Idle, Waiting, Aiming }
        Phase phase;
        HoleController target;
        Vector2 targetSwipe;
        float timer, aimTime;
        float resultsTime;
        int restarts;
        int levelShots, levelGeneration = -1;

        public int ShotsFired { get; private set; }
        public int DeliberateMisses { get; private set; }

        void OnEnable() { phase = Phase.Idle; }

        /// <summary>Resets the result-button press counter (start of a scripted run).</summary>
        public void ResetNavigation() { restarts = 0; resultsTime = 0f; }

        void OnDisable()
        {
            if (phase == Phase.Aiming && shots != null) shots.CancelAim();
            phase = Phase.Idle;
        }

        void Update()
        {
            if (shots == null || round == null) return;

            if (!round.IsPlaying)
            {
                phase = Phase.Idle;
                resultsTime += Time.deltaTime;
                if (autoNavigate && round.State == RoundState.Menu && resultsTime >= menuDelay)
                {
                    resultsTime = 0f;
                    round.StartLevel(startLevel);
                }
                else if (round.State == RoundState.Ended && autoRestartDelay >= 0f && hud != null && hud.ResultsVisible &&
                         restarts < maxAutoRestarts && resultsTime >= autoRestartDelay)
                {
                    resultsTime = 0f; restarts++;
                    if (round.LastWin && round.HasNextLevel) round.NextLevel(); else round.Restart();
                }
                return;
            }
            resultsTime = 0f;

            if (round.RoundGeneration != levelGeneration) { levelGeneration = round.RoundGeneration; levelShots = 0; }
            var ball = shots.Ball;
            switch (phase)
            {
                case Phase.Idle:
                    if (ball.State != BallState.Ready || shots.IsAiming) return;
                    target = PickTarget();
                    if (target == null) return;
                    timer = Random.Range(reactionDelay.x, reactionDelay.y);
                    phase = Phase.Waiting;
                    break;

                case Phase.Waiting:
                    timer -= Time.deltaTime;
                    if (!target.IsHittable || ball.State != BallState.Ready) { phase = Phase.Idle; return; }
                    if (timer > 0f) return;
                    Vector3 aimPoint = target.TargetPoint;
                    if (Random.value < deliberateMissChance || (forceMissOnShot > 0 && levelShots + 1 == forceMissOnShot))
                    {
                        // Miss on purpose: overshoot / undershoot or pull wide.
                        Vector2 off = Random.insideUnitCircle.normalized * Random.Range(1.4f, 2.2f);
                        aimPoint += new Vector3(off.x, 0f, off.y);
                        DeliberateMisses++;
                    }
                    targetSwipe = ShotSolver.SwipeForTarget(aimPoint, ball.LaunchPosition, ball.Radius, shots.ViewCamera, config, shots.Forward);
                    aimTime = Random.Range(aimDuration.x, aimDuration.y);
                    timer = 0f;
                    shots.BeginAim();
                    phase = Phase.Aiming;
                    break;

                case Phase.Aiming:
                    timer += Time.deltaTime;
                    float t = Mathf.Clamp01(timer / aimTime);
                    // Drag out quickly then settle, like a thumb.
                    float e = 1f - Mathf.Pow(1f - t, 2.5f);
                    shots.UpdateAim(Vector2.Lerp(targetSwipe * 0.35f, targetSwipe, e));
                    if (t >= 1f)
                    {
                        if (shots.ReleaseAim(targetSwipe)) { ShotsFired++; levelShots++; }
                        phase = Phase.Idle;
                    }
                    break;
            }
        }

        HoleController PickTarget()
        {
            HoleController best = null;
            float bestScore = float.MinValue;
            foreach (var h in round.Holes)
            {
                if (!h.IsHittable) continue;
                // Prefer fast moles and the ones that still have enough time left for reaction + aim + flight.
                float needed = reactionDelay.y + aimDuration.y + 0.85f;
                float slack = h.HittableTimeLeft - needed;
                float s = slack + (h.MoleType == MoleType.Fast ? 0.5f : 0f);
                if (slack > -0.4f && s > bestScore) { bestScore = s; best = h; }
            }
            return best;
        }
    }
}
