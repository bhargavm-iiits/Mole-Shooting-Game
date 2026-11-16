using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MoleShot.Tests
{
    public class ShotSolverTests
    {
        MoleShotConfig cfg;
        const float Dt = 0.02f;

        [SetUp] public void SetUp() { cfg = ScriptableObject.CreateInstance<MoleShotConfig>(); }
        [TearDown] public void TearDown() { Object.DestroyImmediate(cfg); }

        [Test]
        public void Tap_IsRejected()
        {
            Assert.IsFalse(ShotSolver.IsValidSwipe(new Vector2(0f, cfg.minSwipe * 0.5f), cfg, out var reason));
            Assert.AreEqual("tap", reason);
            var r = ShotSolver.Solve(new Vector2(0f, cfg.minSwipe * 0.9f), new Vector3(0, 0.3f, 0), 0.3f, null, cfg, Vector3.forward, Dt);
            Assert.IsFalse(r.valid);
        }

        [Test]
        public void DownwardOrSidewaysSwipe_IsRejected()
        {
            Assert.IsFalse(ShotSolver.IsValidSwipe(new Vector2(0f, -0.2f), cfg, out _));
            Assert.IsFalse(ShotSolver.IsValidSwipe(new Vector2(0.3f, 0f), cfg, out _));
            Assert.IsTrue(ShotSolver.IsValidSwipe(new Vector2(0.05f, 0.2f), cfg, out _));
        }

        [Test]
        public void RangeMapping_IsMonotonicAndClamped()
        {
            float prev = float.MinValue;
            for (float len = cfg.minSwipe; len <= cfg.fullSwipe * 1.5f; len += 0.01f)
            {
                float r = ShotSolver.RangeForSwipeLength(len, cfg);
                Assert.GreaterOrEqual(r, prev - 1e-5f);
                Assert.GreaterOrEqual(r, cfg.minRange - 1e-4f);
                Assert.LessOrEqual(r, cfg.maxRange + 1e-4f);
                prev = r;
            }
            Assert.AreEqual(cfg.minRange, ShotSolver.RangeForSwipeLength(cfg.minSwipe, cfg), 1e-4f);
            Assert.AreEqual(cfg.maxRange, ShotSolver.RangeForSwipeLength(cfg.fullSwipe, cfg), 1e-4f);
        }

        [Test]
        public void RangeMapping_InverseRoundTrips()
        {
            foreach (float range in new[] { 4f, 5.5f, 7f, 9f, 11f })
            {
                float len = ShotSolver.SwipeLengthForRange(range, cfg);
                Assert.AreEqual(range, ShotSolver.RangeForSwipeLength(len, cfg), 1e-3f);
            }
        }

        [Test]
        public void YawIsClamped()
        {
            Assert.AreEqual(cfg.maxYaw, ShotSolver.ClampYaw(80f, cfg), 1e-4f);
            Assert.AreEqual(-cfg.maxYaw, ShotSolver.ClampYaw(-80f, cfg), 1e-4f);
            Assert.AreEqual(10f * cfg.yawGain, ShotSolver.ClampYaw(10f, cfg), 1e-4f);
        }

        [Test]
        public void SimulatedFlight_PassesTargetHeightAtDesiredRange()
        {
            // The integrator-compensated launch should cross targetHeight within a few cm of the requested range.
            var origin = new Vector3(0f, 0.3f, 0f);
            foreach (float range in new[] { 4f, 6f, 9f, 11f })
            {
                float speed = ShotSolver.SpeedForRange(range, cfg.launchAngle, cfg.targetHeight - origin.y, cfg.gravity);
                Vector3 v = ShotSolver.BuildVelocity(Vector3.forward, speed, cfg.launchAngle, cfg.gravity, Dt);
                Assert.IsTrue(ShotSolver.Simulate(origin, v, cfg.gravity, Dt, cfg.targetHeight, 1000, null, out var end, out _));
                Assert.AreEqual(range, end.z, 0.05f, $"range {range}");
            }
        }

        [Test]
        public void Simulation_RecordsPathPoints()
        {
            var pts = new List<Vector3>();
            ShotSolver.Simulate(new Vector3(0, 0.3f, 0), new Vector3(0, 8, 8), cfg.gravity, Dt, cfg.targetHeight, 1000, pts, out _, out float t);
            Assert.Greater(pts.Count, 10);
            Assert.Greater(t, 0.3f);
        }

        [Test]
        public void AimDirection_StraightUpSwipe_IsForward_WithCenteredCamera()
        {
            var go = new GameObject("cam");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.transform.position = new Vector3(0, 5.5f, -5f);
                cam.transform.LookAt(new Vector3(0, 0, 6));
                cam.pixelRect = new Rect(0, 0, 1080, 1920);
                Vector3 d = ShotSolver.AimDirection(cam, Vector3.zero, Vector2.up, Vector3.forward);
                Assert.AreEqual(0f, d.x, 1e-3f);
                Assert.Greater(d.z, 0.99f);
                Vector3 right = ShotSolver.AimDirection(cam, Vector3.zero, new Vector2(0.4f, 1f), Vector3.forward);
                Assert.Greater(right.x, 0f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SwipeForTarget_InvertsSolve()
        {
            var go = new GameObject("cam");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.transform.position = new Vector3(0, 5.5f, -5f);
                cam.transform.LookAt(new Vector3(0, 0, 6));
                cam.pixelRect = new Rect(0, 0, 1080, 1920);
                var ball = new Vector3(0, 0.3f, 0);
                foreach (var target in new[] { new Vector3(2.3f, 0.45f, 5f), new Vector3(-2.6f, 0.45f, 9f), new Vector3(0, 0.45f, 9f) })
                {
                    Vector2 swipe = ShotSolver.SwipeForTarget(target, ball, 0.3f, cam, cfg, Vector3.forward);
                    var r = ShotSolver.Solve(swipe, ball, 0.3f, cam, cfg, Vector3.forward, Dt);
                    Assert.IsTrue(r.valid);
                    ShotSolver.Simulate(ball, r.velocity, cfg.gravity, Dt, cfg.targetHeight, 1000, null, out var end, out _);
                    Assert.Less(Vector2.Distance(new Vector2(end.x, end.z), new Vector2(target.x, target.z)), 0.1f, target.ToString());
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
