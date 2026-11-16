using UnityEngine;

namespace MoleShot
{
    /// <summary>
    /// One burrow and its mole. State machine: Hidden -> Emerging -> Hittable -> Retreating -> Hidden,
    /// plus Hit (squash reaction, then a fast retreat). The mole has no rig, so it is translated vertically
    /// from below the ground to its up pose. Its trigger collider is enabled ONLY while Hittable.
    /// Everything is Update-driven (no coroutines) so ForceHide can never leave stale work behind.
    /// </summary>
    public class HoleController : MonoBehaviour
    {
        [SerializeField] MoleShotConfig config;
        [Tooltip("Moves vertically; parent of the mole mesh and hitbox.")]
        [SerializeField] Transform moleRoot;
        [Tooltip("Visual that wobbles/squashes (child of moleRoot).")]
        [SerializeField] Transform moleVisual;
        [SerializeField] Collider moleHitbox;
        [SerializeField] GameObject fastMarker;
        [SerializeField] GameObject fastGlow;
        [SerializeField] Renderer aimHighlight;
        [SerializeField] float hiddenY = -1.1f;
        [SerializeField] float upY = 0f;
        [Tooltip("Radius (m) used for aim highlighting and AutoPlayer targeting.")]
        [SerializeField] float radius = 0.62f;

        MoleState state = MoleState.Hidden;
        MoleType type = MoleType.Normal;
        float stateTime;
        float riseTime = 999f;   // seconds since Spawn; drives the rise animation (continues into Hittable)
        float visibleWindow;
        float hiddenTime = 999f;
        Vector3 visualBaseScale = Vector3.one;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public MoleState State => state;
        public MoleType MoleType => type;
        public bool IsHittable => state == MoleState.Hittable;
        public bool IsHidden => state == MoleState.Hidden;
        public bool IsFree => state == MoleState.Hidden && hiddenTime >= config.holeCooldown;
        public Vector3 Center => transform.position;
        public float Radius => radius;
        /// <summary>Seconds left in the hittable window (0 when not hittable).</summary>
        /// <summary>
        /// Rise height (0 = hidden, 1 = up) at normalised emerge time t: a slow peek to ~45 % (eyes and nose above the rim), a short hold, then an
        /// ease-out-back rise that overshoots slightly and settles.
        /// </summary>
        const float Peek = 0.45f; // eyes-and-nose height of the initial peek

        public static float RiseCurve(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < 0.3f) { float u = t / 0.3f; return Peek * (1f - Mathf.Pow(1f - u, 3f)); }
            if (t < 0.38f) return Peek + 0.015f * Mathf.Sin((t - 0.3f) / 0.08f * Mathf.PI);   // short hold
            float v = (t - 0.38f) / 0.62f;
            const float c1 = 1.4f, c3 = c1 + 1f;
            float back = 1f + c3 * Mathf.Pow(v - 1f, 3f) + c1 * Mathf.Pow(v - 1f, 2f);
            return Peek + (1f - Peek) * back;
        }

        /// <summary>Seconds after Spawn at which the mole has risen to hittableFromEmergeFraction (trigger turns on).</summary>
        public float HittableDelay
        {
            get
            {
                float frac = config.hittableFromEmergeFraction;
                for (int i = 0; i <= 400; i++)
                {
                    float t = i / 400f;
                    if (RiseCurve(t) >= frac) return t * config.emergeDuration;
                }
                return config.emergeDuration;
            }
        }

        /// <summary>Current rise height fraction (0..~1.05), for tests and debugging.</summary>
        public float RiseFraction => state == MoleState.Emerging || state == MoleState.Hittable
            ? RiseCurve(riseTime / Mathf.Max(0.01f, config.emergeDuration)) : 0f;

        public float HittableTimeLeft => state == MoleState.Hittable ? Mathf.Max(0f, visibleWindow - stateTime) : 0f;
        /// <summary>World position of the mole body centre in its up pose (aim point).</summary>
        public Vector3 TargetPoint => new Vector3(transform.position.x, config.targetHeight, transform.position.z);

        void Awake()
        {
            if (moleVisual != null) visualBaseScale = moleVisual.localScale;
            ForceHide();
            SetHighlight(false, Color.white);
        }

        public bool Spawn(MoleType moleType, float window)
        {
            if (!IsFree) return false;
            type = moleType;
            visibleWindow = window;
            if (fastMarker != null) fastMarker.SetActive(type == MoleType.Fast);
            if (fastGlow != null) fastGlow.SetActive(type == MoleType.Fast);
            moleRoot.gameObject.SetActive(true);
            riseTime = 0f;
            SetState(MoleState.Emerging);
            return true;
        }

        /// <summary>Atomically checks the mole is hittable at the moment of contact and starts the hit reaction.</summary>
        public bool TryHit()
        {
            if (state != MoleState.Hittable) return false;
            SetState(MoleState.Hit);
            return true;
        }

        public void ForceHide()
        {
            state = MoleState.Hidden;
            stateTime = 0f;
            hiddenTime = 999f;
            SetY(hiddenY);
            if (moleHitbox != null) moleHitbox.enabled = false;
            if (moleVisual != null) { moleVisual.localScale = visualBaseScale; moleVisual.localRotation = Quaternion.identity; }
            if (fastMarker != null) fastMarker.SetActive(false);
            if (fastGlow != null) fastGlow.SetActive(false);
            if (moleRoot != null) moleRoot.gameObject.SetActive(false);
        }

        public void SetHighlight(bool on, Color color)
        {
            if (aimHighlight == null) return;
            aimHighlight.enabled = on;
            if (!on) return;
            mpb ??= new MaterialPropertyBlock();
            mpb.SetColor(BaseColorId, color);
            aimHighlight.SetPropertyBlock(mpb);
        }

        void SetState(MoleState s)
        {
            state = s;
            stateTime = 0f;
            if (moleHitbox != null) moleHitbox.enabled = s == MoleState.Hittable;
            if (s == MoleState.Hidden)
            {
                hiddenTime = 0f;
                if (fastMarker != null) fastMarker.SetActive(false);
                if (fastGlow != null) fastGlow.SetActive(false);
                if (moleRoot != null) moleRoot.gameObject.SetActive(false);
            }
        }

        void SetY(float y)
        {
            if (moleRoot == null) return;
            var p = moleRoot.localPosition;
            p.y = y;
            moleRoot.localPosition = p;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            stateTime += dt;
            riseTime += dt;
            switch (state)
            {
                case MoleState.Hidden:
                    hiddenTime += dt;
                    break;

                case MoleState.Emerging:
                {
                    float t = riseTime / Mathf.Max(0.01f, config.emergeDuration);
                    float h = RiseCurve(t);
                    SetY(Mathf.LerpUnclamped(hiddenY, upY, h));
                    // Fairness: once the mole is visibly mostly out, it can be hit (the rise keeps animating).
                    if (h >= config.hittableFromEmergeFraction || t >= 1f) SetState(MoleState.Hittable);
                    break;
                }

                case MoleState.Hittable:
                {
                    float left = visibleWindow - stateTime;
                    float rt = riseTime / Mathf.Max(0.01f, config.emergeDuration);
                    bool warning = left < config.retreatWarning;
                    // Finish the rise animation, then sink slightly during the warning so the duck is telegraphed.
                    float sink = warning ? Mathf.Clamp01(1f - left / Mathf.Max(0.01f, config.retreatWarning)) * 0.08f : 0f;
                    SetY((rt < 1f ? Mathf.LerpUnclamped(hiddenY, upY, RiseCurve(rt)) : upY) - sink);
                    if (moleVisual != null)
                    {
                        // Gentle idle bob; a clear wobble as a warning shortly before ducking.
                        float wobble = warning ? Mathf.Sin(stateTime * 32f) * 13f : 0f;
                        moleVisual.localRotation = Quaternion.Euler(0f, 0f, wobble);
                        float bob = 1f + Mathf.Sin(stateTime * 6f) * 0.015f;
                        moleVisual.localScale = new Vector3(visualBaseScale.x, visualBaseScale.y * bob, visualBaseScale.z);
                    }
                    if (left <= 0f) SetState(MoleState.Retreating);
                    break;
                }

                case MoleState.Retreating:
                {
                    float t = Mathf.Clamp01(stateTime / config.retreatDuration);
                    float from = upY - (config.retreatWarning > 0f ? 0.08f : 0f); // continue from the warning sink
                    SetY(Mathf.Lerp(from, hiddenY, t * t));
                    if (moleVisual != null) moleVisual.localRotation = Quaternion.identity;
                    if (t >= 1f) Hide();
                    break;
                }

                case MoleState.Hit:
                {
                    float squashT = Mathf.Clamp01(stateTime / config.hitReactionDuration);
                    if (moleVisual != null)
                    {
                        // Squash & stretch, then a quick drop.
                        float s = Mathf.Sin(squashT * Mathf.PI);
                        moleVisual.localScale = Vector3.Scale(visualBaseScale, new Vector3(1f + 0.25f * s, 1f - 0.35f * s, 1f + 0.25f * s));
                        moleVisual.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(stateTime * 50f) * 6f * (1f - squashT));
                    }
                    float dropT = Mathf.Clamp01((stateTime - config.hitReactionDuration * 0.5f) / (config.retreatDuration * 0.75f));
                    SetY(Mathf.Lerp(upY, hiddenY, dropT * dropT));
                    if (dropT >= 1f && squashT >= 1f) Hide();
                    break;
                }
            }
        }

        void Hide()
        {
            if (moleVisual != null) { moleVisual.localScale = visualBaseScale; moleVisual.localRotation = Quaternion.identity; }
            SetY(hiddenY);
            SetState(MoleState.Hidden);
        }
    }
}
