using System.Collections.Generic;
using UnityEngine;

namespace MoleShot
{
    /// <summary>
    /// Draws the predicted arc. It simulates the SAME launch velocity from the SAME origin with the SAME integrator
    /// as PhysX (semi-implicit Euler at Time.fixedDeltaTime), so the preview and the real flight coincide.
    /// Dotted line (LineRenderer, tiled dot texture), arrowhead at the end, translucent landing ring where the
    /// path descends through targetHeight, and a highlight ring on the hole the shot will land in.
    /// </summary>
    public class TrajectoryPreview : MonoBehaviour
    {
        [SerializeField] MoleShotConfig config;
        [SerializeField] RoundManager round;
        [SerializeField] LineRenderer line;
        [SerializeField] Transform arrowHead;
        [SerializeField] Renderer arrowRenderer;
        [SerializeField] Transform landingRing;
        [SerializeField] Renderer landingRenderer;
        [SerializeField] Camera viewCamera;
        [Tooltip("World length of one dot tile along the line.")]
        [SerializeField] float dotSpacing = 0.3f;
        [SerializeField] Color lineColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] Color ringColor = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] Color ringOnTargetColor = new Color(0.55f, 1f, 0.45f, 0.55f);
        [SerializeField] Color holeHighlightColor = new Color(0.85f, 1f, 0.7f, 0.45f);
        [Tooltip("Shorten the drawn line by this much (m) so the arrowhead sits at its tip.")]
        [SerializeField] float arrowInset = 0.25f;
        [Tooltip("When the arc lands in a burrow, the drawn path stops this far (m) from the mole's centre.")]
        [SerializeField] float targetCutRadius = 0.75f;

        readonly List<Vector3> points = new List<Vector3>(256);
        MaterialPropertyBlock mpb;
        HoleController highlighted;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public Vector3 LastEndPoint { get; private set; }
        public float LastFlightTime { get; private set; }
        public bool IsVisible { get; private set; }

        void Awake() { Hide(); }

        /// <summary>Shows the arc for a launch velocity. alpha scales everything (used for the pulsing hint).</summary>
        [Tooltip("Exponential smoothing rate (1/s) of the DISPLAYED aim while dragging. The launch always uses the exact solve.")]
        [SerializeField] float aimSmoothing = 18f;

        bool hasTarget;
        Vector3 targetOrigin, targetVelocity, shownVelocity;
        float targetAlpha;

        /// <summary>
        /// Smoothed display: the drawn arc eases toward this launch velocity every frame (light exponential smoothing),
        /// so finger jitter does not make the path twitch. snap = jump straight to it (first frame of an aim).
        /// </summary>
        public void SetTarget(Vector3 origin, Vector3 velocity, float alpha, bool snap)
        {
            targetOrigin = origin; targetVelocity = velocity; targetAlpha = alpha;
            if (snap || !hasTarget) shownVelocity = velocity;
            hasTarget = true;
        }

        void LateUpdate()
        {
            if (!hasTarget) return;
            float k = 1f - Mathf.Exp(-aimSmoothing * Time.unscaledDeltaTime);
            shownVelocity = Vector3.Lerp(shownVelocity, targetVelocity, k);
            Show(targetOrigin, shownVelocity, targetAlpha);
        }

        public void Show(Vector3 origin, Vector3 velocity, float alpha = 1f)
        {
            points.Clear();
            ShotSolver.Simulate(origin, velocity, config.gravity, Time.fixedDeltaTime, config.targetHeight, 600,
                                points, out var end, out var flightTime);
            LastEndPoint = end;
            LastFlightTime = flightTime;

            // If the arc lands in a burrow, end the drawn path at the near edge of the mole (arrow at the rim, as in
            // the reference) instead of inside the mole body.
            HoleController target = FindHoleAt(end);
            int n = points.Count;
            Vector3 tip = end;
            Vector3 dir = n >= 2 ? (points[n - 1] - points[n - 2]) : velocity;
            if (target != null)
            {
                Vector3 c = new Vector3(target.Center.x, config.targetHeight + 0.15f, target.Center.z);
                for (int i = 1; i < n; i++)
                {
                    if ((points[i] - c).sqrMagnitude > targetCutRadius * targetCutRadius) continue;
                    tip = points[i]; dir = points[i] - points[i - 1];
                    points.RemoveRange(i + 1, n - i - 1);
                    break;
                }
            }

            // Trim the start (inside the ball) and the tip (under the arrowhead).
            float startSkip = 0.35f;
            var drawn = Trim(points, startSkip, arrowInset);
            line.positionCount = drawn.Count;
            for (int i = 0; i < drawn.Count; i++) line.SetPosition(i, drawn[i]);
            line.textureScale = new Vector2(1f / Mathf.Max(0.05f, dotSpacing), 1f);
            Color lc = lineColor; lc.a *= alpha;
            line.startColor = lc; line.endColor = lc;
            line.enabled = drawn.Count > 1;

            // Arrowhead at the tip, pointing along the arc, facing the camera.
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            dir.Normalize();
            var cam = viewCamera != null ? viewCamera : Camera.main;
            Vector3 toCam = cam != null ? (cam.transform.position - tip).normalized : Vector3.back;
            Vector3 up = Vector3.ProjectOnPlane(dir, toCam);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            arrowHead.SetPositionAndRotation(tip, Quaternion.LookRotation(-toCam, up.normalized));
            arrowHead.gameObject.SetActive(true);
            SetColor(arrowRenderer, lc);

            // Target hole & landing ring.
            SetHighlighted(target, alpha);
            landingRing.position = new Vector3(end.x, 0.02f, end.z);
            landingRing.gameObject.SetActive(true);
            Color rc = target != null ? ringOnTargetColor : ringColor; rc.a *= alpha;
            SetColor(landingRenderer, rc);
            IsVisible = true;
        }

        public void Hide()
        {
            hasTarget = false;
            if (line != null) { line.positionCount = 0; line.enabled = false; }
            if (arrowHead != null) arrowHead.gameObject.SetActive(false);
            if (landingRing != null) landingRing.gameObject.SetActive(false);
            SetHighlighted(null, 1f);
            IsVisible = false;
        }

        HoleController FindHoleAt(Vector3 p)
        {
            if (round == null || round.Holes == null) return null;
            HoleController best = null;
            float bestD = float.MaxValue;
            foreach (var h in round.Holes)
            {
                Vector3 d = h.Center - p; d.y = 0f;
                float m = d.magnitude;
                if (m <= h.Radius && m < bestD) { bestD = m; best = h; }
            }
            return best;
        }

        void SetHighlighted(HoleController hole, float alpha)
        {
            if (highlighted != null && highlighted != hole) highlighted.SetHighlight(false, Color.clear);
            highlighted = hole;
            if (hole != null) { Color c = holeHighlightColor; c.a *= alpha; hole.SetHighlight(true, c); }
        }

        void SetColor(Renderer r, Color c)
        {
            if (r == null) return;
            mpb ??= new MaterialPropertyBlock();
            mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(mpb);
        }

        static readonly List<Vector3> trimmed = new List<Vector3>(256);
        static List<Vector3> Trim(List<Vector3> src, float startSkip, float endSkip)
        {
            trimmed.Clear();
            if (src.Count < 2) { trimmed.AddRange(src); return trimmed; }
            float total = 0f;
            for (int i = 1; i < src.Count; i++) total += Vector3.Distance(src[i - 1], src[i]);
            float from = Mathf.Min(startSkip, total * 0.3f), to = Mathf.Max(from, total - endSkip);
            float acc = 0f;
            for (int i = 1; i < src.Count; i++)
            {
                Vector3 a = src[i - 1], b = src[i];
                float seg = Vector3.Distance(a, b);
                float s0 = acc, s1 = acc + seg;
                if (s1 >= from && s0 <= to && seg > 1e-6f)
                {
                    if (trimmed.Count == 0) trimmed.Add(Vector3.Lerp(a, b, Mathf.Clamp01((from - s0) / seg)));
                    if (s1 <= to) trimmed.Add(b);
                    else { trimmed.Add(Vector3.Lerp(a, b, Mathf.Clamp01((to - s0) / seg))); break; }
                }
                acc = s1;
            }
            return trimmed;
        }
    }
}

