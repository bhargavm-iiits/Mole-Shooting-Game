using UnityEngine;

namespace MoleShot
{
    /// <summary>
    /// Glue: SwipeInput -> ShotSolver -> TrajectoryPreview -> BallController.
    /// Also owns the "first shot" tutorial hint (pulsing demo arc + SWIPE TO SHOOT pill).
    /// The AutoPlayer drives the same methods (BeginAim / UpdateAim / ReleaseAim) as a finger would.
    /// </summary>
    public class ShotController : MonoBehaviour
    {
        [SerializeField] MoleShotConfig config;
        [SerializeField] SwipeInput swipeInput;
        [SerializeField] BallController ball;
        [SerializeField] TrajectoryPreview preview;
        [SerializeField] RoundManager round;
        [SerializeField] HudController hud;
        [SerializeField] AudioFeedback audioFeedback;
        [SerializeField] Camera viewCamera;
        [Tooltip("Hole the tutorial hint arc points at (front-right burrow).")]
        [SerializeField] HoleController hintHole;

        bool aiming;
        bool previewShown;
        bool firstShotTaken;
        Vector2 currentDelta;

        public bool IsAiming => aiming;
        public bool FirstShotTaken => firstShotTaken;
        public Vector3 Forward => Vector3.forward;
        public Camera ViewCamera => viewCamera;
        public BallController Ball => ball;
        public TrajectoryPreview Preview => preview;

        void OnEnable()
        {
            if (swipeInput == null) return;
            swipeInput.AimStarted += BeginAim;
            swipeInput.AimUpdated += UpdateAim;
            swipeInput.Released += OnReleased;
            swipeInput.Cancelled += CancelAim;
        }

        void OnDisable()
        {
            if (swipeInput == null) return;
            swipeInput.AimStarted -= BeginAim;
            swipeInput.AimUpdated -= UpdateAim;
            swipeInput.Released -= OnReleased;
            swipeInput.Cancelled -= CancelAim;
        }

        void OnReleased(Vector2 normDelta) => ReleaseAim(normDelta);

        public ShotResult Solve(Vector2 normDelta)
        {
            return ShotSolver.Solve(normDelta, ball.LaunchPosition, ball.Radius, viewCamera, config, Forward, Time.fixedDeltaTime);
        }

        public void BeginAim()
        {
            if (ball.State != BallState.Ready || !round.IsPlaying) return;
            aiming = true;
            currentDelta = Vector2.zero;
            preview.Hide();
            previewShown = false;
        }

        public void UpdateAim(Vector2 normDelta)
        {
            if (!aiming) return;
            if (ball.State != BallState.Ready || !round.IsPlaying) { CancelAim(); return; }
            currentDelta = normDelta;
            var shot = Solve(normDelta);
            if (shot.valid) { preview.SetTarget(ball.LaunchPosition, shot.velocity, 1f, !previewShown); previewShown = true; }
            else { preview.Hide(); previewShown = false; }
        }

        /// <summary>Returns true if the ball was launched.</summary>
        public bool ReleaseAim(Vector2 normDelta)
        {
            if (!aiming) return false;
            aiming = false;
            preview.Hide();
            if (ball.State != BallState.Ready || !round.IsPlaying) return false;
            var shot = Solve(normDelta);
            if (!shot.valid) return false;          // tap / downward swipe: ball stays ready
            if (!ball.Launch(shot.velocity)) return false;
            if (audioFeedback != null) audioFeedback.PlayKick(ShotSolver.Power01(normDelta.magnitude, config));
            if (!firstShotTaken) { firstShotTaken = true; if (hud != null) hud.SetSwipeHint(false); }
            return true;
        }

        public void CancelAim()
        {
            aiming = false;
            preview.Hide();
        }

        public void OnRoundStarted()
        {
            aiming = false;
            firstShotTaken = false;
            preview.Hide();
            if (hud != null) hud.SetSwipeHint(true);
        }

        public void OnRoundEnded()
        {
            aiming = false;
            preview.Hide();
            if (hud != null) hud.SetSwipeHint(false);
        }

        void LateUpdate()
        {
            // Tutorial hint: before the first shot, show a gently pulsing demo arc toward the front-right burrow.
            if (aiming || firstShotTaken || round == null || !round.IsPlaying || ball.State != BallState.Ready || hintHole == null)
                return;
            Vector2 swipe = ShotSolver.SwipeForTarget(hintHole.TargetPoint, ball.LaunchPosition, ball.Radius, viewCamera, config, Forward);
            var shot = Solve(swipe);
            if (!shot.valid) return;
            float alpha = 0.45f + 0.35f * Mathf.Sin(Time.unscaledTime * 3.2f);
            preview.SetTarget(ball.LaunchPosition, shot.velocity, alpha, true);
        }
    }
}
