using System;
using UnityEngine;

namespace MoleShot
{
    /// <summary>
    /// The football. Ready (kinematic on the launch mark) -> InFlight -> Resolved (waiting to reset).
    /// Every shot resolves exactly once: mole trigger (hit), ground contact (miss unless a hittable mole is within
    /// holeCatchRadius), out of bounds (miss) or timeout (miss). Hit acceptance is delegated to the RoundManager.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class BallController : MonoBehaviour
    {
        [SerializeField] MoleShotConfig config;
        [SerializeField] RoundManager round;
        [SerializeField] Collider groundCollider;
        [SerializeField] Transform launchPoint;
        [Tooltip("Spin as a fraction of the rolling rate for the flight speed (smooth topspin along the flight direction).")]
        [SerializeField] float launchSpin = 0.6f;

        [Header("Visual polish")]
        [Tooltip("World-aligned pivot that carries the squash; the mesh below it copies the body rotation.")]
        [SerializeField] Transform squashPivot;
        [SerializeField] Transform visual;
        [SerializeField] TrailRenderer trail;
        [SerializeField] Transform shadow;
        [SerializeField] Renderer shadowRenderer;
        [SerializeField] float shadowAlpha = 0.5f;
        [SerializeField] float squashDuration = 0.16f;
        [SerializeField] float squashAmount = 0.2f;

        public event Action<Vector3> Launched;
        public event Action ResetToReady;

        Rigidbody body;
        SphereCollider sphere;
        BallState state = BallState.Ready;
        float stateTime;
        float resetDelay;
        float launchTime;
        /// <summary>The ball rests on the ground at launch; ignore that contact for the first moments of flight.</summary>
        const float GroundGrace = 0.15f;
        Vector3 lastVelocity;

        // Diagnostics: where the ball actually crossed targetHeight on the way down during the last shot.
        public bool HasCrossing { get; private set; }
        public Vector3 LastCrossing { get; private set; }
        public float LastCrossingTime { get; private set; }
        public Vector3 LastImpact { get; private set; }
        public Vector3 LastLaunchVelocity { get; private set; }

        public BallState State => state;
        public float Radius => sphere != null ? sphere.radius * transform.lossyScale.x : 0.3f;
        public Vector3 LaunchPosition => launchPoint != null ? launchPoint.position : transform.position;
        public Rigidbody Body => body;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            sphere = GetComponent<SphereCollider>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.linearDamping = 0f;
            body.angularDamping = 0.05f;
            PlaceOnMark();
        }

        public bool Launch(Vector3 velocity)
        {
            if (state != BallState.Ready) return false;
            if (round != null && !round.IsPlaying) return false;

            body.isKinematic = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = velocity;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 side = Vector3.Cross(Vector3.up, flat.normalized);
            body.angularVelocity = side * (flat.magnitude / Mathf.Max(0.05f, Radius) * launchSpin);
            if (trail != null) { trail.Clear(); trail.emitting = true; }
            LastLaunchVelocity = velocity;
            launchTime = Time.time;
            HasCrossing = false;
            SetState(BallState.InFlight);
            Launched?.Invoke(velocity);
            return true;
        }

        void SetState(BallState s) { state = s; stateTime = 0f; }

        void FixedUpdate()
        {
            if (state == BallState.Ready) return;
            if (state == BallState.InFlight)
            {
                TrackCrossing();
                lastVelocity = body.linearVelocity;
                Vector3 p = body.position;
                if (Mathf.Abs(p.x) > config.boundsHalfWidth || p.z > config.boundsFar || p.z < config.boundsNear || p.y < -2f)
                {
                    Resolve(false, p, true);
                }
            }
        }

        Vector3 prevPos;
        void TrackCrossing()
        {
            Vector3 p = body.position;
            if (!HasCrossing && stateTime > 0f && body.linearVelocity.y < 0f && prevPos.y > config.targetHeight && p.y <= config.targetHeight)
            {
                float f = (prevPos.y - config.targetHeight) / Mathf.Max(1e-5f, prevPos.y - p.y);
                LastCrossing = Vector3.Lerp(prevPos, p, f);
                LastCrossingTime = stateTime;
                HasCrossing = true;
            }
            prevPos = p;
        }

        float squashTime = 999f, squashStrength;
        MaterialPropertyBlock shadowMpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        Vector3 shadowBaseScale = Vector3.one;
        bool shadowInit;

        void LateUpdate()
        {
            Vector3 p = transform.position;   // interpolated
            if (squashPivot != null)
            {
                squashPivot.rotation = Quaternion.identity;
                squashTime += Time.deltaTime;
                float s = squashTime < squashDuration ? Mathf.Sin(squashTime / squashDuration * Mathf.PI) * squashAmount * squashStrength : 0f;
                squashPivot.localScale = new Vector3(1f + s * 0.6f, 1f - s, 1f + s * 0.6f);
                squashPivot.position = p - new Vector3(0f, Radius * s, 0f);   // squash about the contact point
                if (visual != null) visual.rotation = transform.rotation;
            }
            if (trail != null && state != BallState.InFlight && trail.emitting) trail.emitting = false;
            if (shadow != null)
            {
                if (!shadowInit) { shadowBaseScale = shadow.localScale; shadowInit = true; }
                float h = Mathf.Max(0f, p.y - Radius);
                float k = Mathf.Clamp01(h / 4f);
                shadow.position = new Vector3(p.x, 0.014f, p.z);
                shadow.localScale = shadowBaseScale * (1f + 0.45f * k);
                if (shadowRenderer != null)
                {
                    shadowMpb ??= new MaterialPropertyBlock();
                    shadowMpb.SetColor(BaseColorId, new Color(0f, 0f, 0f, shadowAlpha * (1f - 0.65f * k)));
                    shadowRenderer.SetPropertyBlock(shadowMpb);
                }
            }
        }

        void Update()
        {
            if (state == BallState.Ready) return;
            stateTime += Time.deltaTime;
            if (state == BallState.InFlight && stateTime >= config.shotTimeout)
                Resolve(false, body.position, false);
            else if (state == BallState.Resolved && stateTime >= resetDelay)
                PlaceOnMark();
        }

        void OnTriggerEnter(Collider other)
        {
            if (state != BallState.InFlight) return;
            var hitbox = other.GetComponent<MoleHitbox>();
            if (hitbox == null || hitbox.Hole == null) return;
            if (round != null && round.TryScoreHit(hitbox.Hole))
            {
                // Bounce back/up off the mole for a readable reaction.
                Vector3 v = body.linearVelocity;
                body.linearVelocity = new Vector3(-v.x * 0.25f, 4.5f, -Mathf.Abs(v.z) * 0.25f);
                LastImpact = body.position;
                Finish(true);
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            // Landing squash on every real bounce (scaled by impact speed), in any state.
            float impact = Mathf.Abs(collision.relativeVelocity.y);
            if (impact > 1.5f) { squashTime = 0f; squashStrength = Mathf.Clamp01(impact / 10f); }
            if (state != BallState.InFlight) return;
            if (groundCollider != null && collision.collider != groundCollider) return;
            if (Time.time - launchTime < GroundGrace) return;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : body.position;

            // Forgiving catch: landing right in front of / in a hole with a hittable mole still counts.
            if (round != null)
            {
                var hole = round.FindHittableHoleNear(point, config.holeCatchRadius);
                if (hole != null && round.TryScoreHit(hole))
                {
                    LastImpact = point;
                    Finish(true);
                    return;
                }
            }
            Resolve(false, point, false);
        }

        void Resolve(bool hit, Vector3 point, bool outOfBounds)
        {
            if (state != BallState.InFlight) return;
            LastImpact = point;
            if (!hit && round != null) round.ReportMiss(point, outOfBounds);
            Finish(hit);
        }

        void Finish(bool hit)
        {
            resetDelay = hit ? config.resetDelayAfterHit : config.resetDelayAfterMiss;
            SetState(BallState.Resolved);
        }

        /// <summary>Immediately returns the ball to the launch mark (round end / restart).</summary>
        public void ForceReset() => PlaceOnMark();

        void PlaceOnMark()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = true;
            Vector3 pos = LaunchPosition;
            body.position = pos;
            body.rotation = Quaternion.identity;
            transform.SetPositionAndRotation(pos, Quaternion.identity);
            prevPos = pos;
            if (trail != null) { trail.emitting = false; trail.Clear(); }
            squashTime = 999f;
            SetState(BallState.Ready);
            ResetToReady?.Invoke();
        }
    }
}
