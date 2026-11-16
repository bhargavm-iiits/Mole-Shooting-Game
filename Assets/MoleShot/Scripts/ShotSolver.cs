using System.Collections.Generic;
using UnityEngine;

namespace MoleShot
{
    /// <summary>Result of turning one swipe into a launch.</summary>
    public struct ShotResult
    {
        public bool valid;
        public string rejectReason;
        public Vector3 velocity;
        public Vector3 direction;   // horizontal unit vector
        public float range;         // desired ground distance where the ball passes targetHeight
        public float speed;         // launch speed (before integrator compensation)
        public float yawDegrees;    // signed, relative to forward
    }

    /// <summary>
    /// Pure swipe -> launch-velocity math. No scene state: everything comes in through parameters, so it
    /// can be unit tested and reused by the preview, the real launch, the tutorial hint and the AutoPlayer.
    ///
    /// Mapping: swipe LENGTH -> desired ground RANGE (linear, then shaped by rangeExponent), and swipe
    /// DIRECTION on screen -> world direction via a ground-plane projection through the camera.
    /// Speed is then solved for a fixed launch angle so the ball passes through targetHeight at that range.
    /// </summary>
    public static class ShotSolver
    {
        /// <summary>Swipes whose upward component is below this (fraction of screen height) are rejected.</summary>
        public const float UpwardEpsilon = 0.005f;

        /// <summary>Distance (fraction of screen height) of the probe point used to project the swipe direction.</summary>
        public const float AimProbeFraction = 0.12f;

        public static bool IsValidSwipe(Vector2 normDelta, MoleShotConfig c, out string reason)
        {
            if (normDelta.magnitude < c.minSwipe) { reason = "tap"; return false; }
            if (normDelta.y <= UpwardEpsilon) { reason = "not upward"; return false; }
            reason = null;
            return true;
        }

        /// <summary>0..1 power of a swipe of the given length (fraction of screen height).</summary>
        public static float Power01(float swipeLength, MoleShotConfig c)
        {
            float t = Mathf.InverseLerp(c.minSwipe, c.fullSwipe, swipeLength);
            return Mathf.Pow(t, c.rangeExponent);
        }

        public static float RangeForSwipeLength(float swipeLength, MoleShotConfig c)
        {
            return Mathf.Lerp(c.minRange, c.maxRange, Power01(swipeLength, c));
        }

        /// <summary>Inverse of <see cref="RangeForSwipeLength"/> (clamped to the valid swipe band).</summary>
        public static float SwipeLengthForRange(float range, MoleShotConfig c)
        {
            float p = Mathf.InverseLerp(c.minRange, c.maxRange, range);
            float t = Mathf.Pow(p, 1f / Mathf.Max(0.0001f, c.rangeExponent));
            return Mathf.Lerp(c.minSwipe, c.fullSwipe, t);
        }

        /// <summary>
        /// Launch speed that makes a projectile fired at angleDeg pass height dy (relative to launch) at horizontal
        /// distance range under gravity g. Returns +Infinity when the angle cannot reach that point.
        /// v^2 = g R^2 / (2 cos^2(theta) (R tan(theta) - dy))
        /// </summary>
        public static float SpeedForRange(float range, float angleDeg, float dy, float g)
        {
            float th = angleDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(th);
            float denom = 2f * cos * cos * (range * Mathf.Tan(th) - dy);
            if (denom <= 1e-5f) return float.PositiveInfinity;
            return Mathf.Sqrt(g * range * range / denom);
        }

        public static float ClampYaw(float yawDegrees, MoleShotConfig c)
        {
            return Mathf.Clamp(yawDegrees * c.yawGain, -c.maxYaw, c.maxYaw);
        }

        /// <summary>
        /// Perspective-correct aim: projects a point a short way along the swipe direction (from the ball's
        /// ground-contact point on screen) onto the ground plane, and returns the horizontal world direction
        /// from the ball to it. Swiping toward a hole on screen therefore points at that hole in the world.
        /// </summary>
        public static Vector3 AimDirection(Camera cam, Vector3 ballGround, Vector2 swipeDir, Vector3 forward)
        {
            forward.y = 0f;
            forward.Normalize();
            if (swipeDir.sqrMagnitude < 1e-8f) return forward;
            swipeDir.Normalize();

            if (cam != null)
            {
                Vector3 anchor = cam.WorldToScreenPoint(ballGround);
                Vector3 probe = anchor + (Vector3)(swipeDir * (AimProbeFraction * cam.pixelHeight));
                Ray ray = cam.ScreenPointToRay(new Vector3(probe.x, probe.y, 0f));
                if (Mathf.Abs(ray.direction.y) > 1e-5f)
                {
                    float t = (ballGround.y - ray.origin.y) / ray.direction.y;
                    if (t > 0f)
                    {
                        Vector3 d = ray.GetPoint(t) - ballGround;
                        d.y = 0f;
                        if (d.sqrMagnitude > 1e-6f) return d.normalized;
                    }
                }
            }
            // Fallback (probe above the horizon / no camera): treat the swipe angle as the yaw.
            float yaw = Mathf.Atan2(swipeDir.x, swipeDir.y) * Mathf.Rad2Deg;
            return Quaternion.Euler(0f, yaw, 0f) * forward;
        }

        /// <summary>
        /// Builds the launch velocity. PhysX integrates gravity semi-implicitly (v += g dt; p += v dt), which lands
        /// the ball g*dt*t/2 lower than the analytic arc; adding g*dt/2 to the initial vertical speed cancels that
        /// exactly, so the analytic range solution holds for the stepped simulation.
        /// </summary>
        public static Vector3 BuildVelocity(Vector3 dirXZ, float speed, float angleDeg, float g, float fixedDt)
        {
            float th = angleDeg * Mathf.Deg2Rad;
            Vector3 v = dirXZ * (speed * Mathf.Cos(th));
            v.y = speed * Mathf.Sin(th) + g * fixedDt * 0.5f;
            return v;
        }

        public static ShotResult Solve(Vector2 normDelta, Vector3 ballPos, float ballRadius, Camera cam,
                                       MoleShotConfig c, Vector3 forward, float fixedDt)
        {
            var r = new ShotResult();
            if (!IsValidSwipe(normDelta, c, out r.rejectReason)) return r;

            r.range = RangeForSwipeLength(normDelta.magnitude, c);
            float dy = c.targetHeight - ballPos.y;
            r.speed = Mathf.Min(SpeedForRange(r.range, c.launchAngle, dy, c.gravity), c.maxSpeed);

            Vector3 ballGround = new Vector3(ballPos.x, ballPos.y - ballRadius, ballPos.z);
            Vector3 aim = AimDirection(cam, ballGround, normDelta, forward);
            Vector3 fwd = new Vector3(forward.x, 0f, forward.z).normalized;
            r.yawDegrees = ClampYaw(Vector3.SignedAngle(fwd, aim, Vector3.up), c);
            r.direction = Quaternion.Euler(0f, r.yawDegrees, 0f) * fwd;
            r.velocity = BuildVelocity(r.direction, r.speed, c.launchAngle, c.gravity, fixedDt);
            r.valid = true;
            return r;
        }

        /// <summary>
        /// Inverse mapping: the screen-space swipe (normalized by screen height) that sends the ball through
        /// targetHeight above <paramref name="target"/>. Used by the tutorial hint and the AutoPlayer.
        /// </summary>
        public static Vector2 SwipeForTarget(Vector3 target, Vector3 ballPos, float ballRadius, Camera cam,
                                             MoleShotConfig c, Vector3 forward)
        {
            Vector3 ballGround = new Vector3(ballPos.x, ballPos.y - ballRadius, ballPos.z);
            Vector3 flat = target - ballPos; flat.y = 0f;
            float range = flat.magnitude;
            float len = SwipeLengthForRange(range, c);

            Vector3 fwd = new Vector3(forward.x, 0f, forward.z).normalized;
            float yaw = Vector3.SignedAngle(fwd, flat, Vector3.up) / Mathf.Max(0.01f, c.yawGain);
            Vector3 aimPoint = ballGround + Quaternion.Euler(0f, yaw, 0f) * fwd * Mathf.Max(1f, range);

            Vector2 dir = Vector2.up;
            if (cam != null)
            {
                Vector3 a = cam.WorldToScreenPoint(ballGround);
                Vector3 b = cam.WorldToScreenPoint(aimPoint);
                Vector2 d = new Vector2(b.x - a.x, b.y - a.y);
                if (d.sqrMagnitude > 1e-6f) dir = d.normalized;
            }
            return dir * len;
        }

        /// <summary>
        /// Steps a projectile with the same integrator PhysX uses (semi-implicit Euler at fixedDt) until it descends
        /// through stopHeight. Appends every step position to points (if not null). Returns true if it crossed;
        /// endPoint is the interpolated crossing point (or the last point on failure).
        /// </summary>
        public static bool Simulate(Vector3 origin, Vector3 velocity, float g, float fixedDt, float stopHeight,
                                    int maxSteps, List<Vector3> points, out Vector3 endPoint, out float flightTime)
        {
            Vector3 p = origin, v = velocity;
            points?.Add(p);
            for (int i = 1; i <= maxSteps; i++)
            {
                Vector3 prev = p;
                v.y -= g * fixedDt;
                p += v * fixedDt;
                if (v.y < 0f && p.y <= stopHeight)
                {
                    float f = Mathf.Approximately(prev.y, p.y) ? 1f : Mathf.Clamp01((prev.y - stopHeight) / (prev.y - p.y));
                    endPoint = Vector3.Lerp(prev, p, f);
                    flightTime = (i - 1 + f) * fixedDt;
                    points?.Add(endPoint);
                    return true;
                }
                points?.Add(p);
            }
            endPoint = p;
            flightTime = maxSteps * fixedDt;
            return false;
        }
    }
}
