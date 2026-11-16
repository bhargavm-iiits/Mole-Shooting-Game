using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Unity.Profiling;

namespace MoleShot.EditorTools
{
    /// <summary>
    /// Automated Play Mode checks driven from the Editor (EditorApplication.update), so they can be started from a
    /// script-eval call and read back from the console. Every shot goes through the real ShotSolver / BallController /
    /// RoundManager path. Results are logged with the "[Playtest]" prefix and kept in <see cref="Report"/>.
    /// </summary>
    public static class MoleShotPlaytest
    {
        public static readonly StringBuilder Report = new StringBuilder();
        public static bool Running { get; private set; }

        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        static float waitUntil;
        static Func<bool> waitCondition;
        static float conditionDeadline;

        static RoundManager Round => UnityEngine.Object.FindAnyObjectByType<RoundManager>();
        static ShotController Shots => UnityEngine.Object.FindAnyObjectByType<ShotController>();
        static HudController Hud => UnityEngine.Object.FindAnyObjectByType<HudController>();

        // ------------------------------------------------------------------ public entry points

        public static string StartAccuracyTest() => Start(AccuracyTest());
        public static string StartRulesTest() => Start(RulesTest());
        public static string StartCrowdCostTest() => Start(CrowdCost());

        static IEnumerator CrowdCost()
        {
            var crowd = UnityEngine.Object.FindAnyObjectByType<CrowdController>();
            var round = Round;
            round.spawningEnabled = false;
            round.Restart();
            yield return 0.5f;
            using (var crowdRec = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "MoleShot.Crowd.Update", 240))
            using (var mainRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 240))
            {
                yield return 3f;
                Log($"CROWD IDLE (sway only): crowdUpdate={Avg(crowdRec) / 1e6:0.000}ms mainThread={Avg(mainRec) / 1e6:0.00}ms samples={crowdRec.Count}");
            }
            crowd.DebugCheer(true);
            using (var crowdRec2 = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "MoleShot.Crowd.Update", 240))
            using (var mainRec2 = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 240))
            {
                // ~3 s of continuous big reactions (re-triggered every 0.5 s so most of the stand is always jumping).
                int maxActive = 0;
                for (int k = 0; k < 6; k++) { crowd.DebugCheer(true); maxActive = Mathf.Max(maxActive, crowd.ActiveCount); yield return 0.5f; }
                Log($"CROWD CHEERING (big, {crowd.PeopleCount} people, peak active {maxActive}): crowdUpdate={Avg(crowdRec2) / 1e6:0.000}ms mainThread={Avg(mainRec2) / 1e6:0.00}ms samples={crowdRec2.Count}");
            }
            round.spawningEnabled = true;
            round.Restart();
            Log($"after restart activeNow={crowd.ActiveCount}");
        }

        /// <summary>Full AutoPlayer round (optionally followed by two restarts verified clean).</summary>
        public static string StartAutoRound(bool verifyRestarts) => Start(AutoLevel(0));

        /// <summary>AutoPlayer plays one level (0-based) through the real intro / play / result flow.</summary>
        public static string StartAutoLevel(int level) => Start(AutoLevel(level));

        public static string Stop()
        {
            EditorApplication.update -= Tick;
            stack.Clear(); Running = false;
            return "stopped";
        }

        // ------------------------------------------------------------------ runner

        static string Start(IEnumerator r)
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first";
            Stop();
            Report.Clear();
            MoleShotDebug.LogEvents = true;
            stack.Clear(); stack.Push(r); Running = true; waitCondition = null; waitUntil = 0f;
            EditorApplication.update += Tick;
            return "started";
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) { Log("Play Mode exited - aborting"); Stop(); return; }
            if (Time.time < waitUntil) return;
            if (waitCondition != null)
            {
                if (!waitCondition() && Time.time < conditionDeadline) return;
                waitCondition = null;
            }
            try
            {
                while (stack.Count > 0)
                {
                    var top = stack.Peek();
                    if (!top.MoveNext()) { stack.Pop(); continue; }
                    switch (top.Current)
                    {
                        case float secs: waitUntil = Time.time + secs; return;
                        case Func<bool> cond: waitCondition = cond; conditionDeadline = Time.time + 400f; return;
                        case IEnumerator nested: stack.Push(nested); continue;
                        default: return; // null -> next frame
                    }
                }
                Log("DONE"); Stop();
            }
            catch (Exception e) { Log("EXCEPTION " + e); Stop(); }
        }

        static void Log(string s)
        {
            Report.AppendLine(s);
            Debug.Log("[Playtest] " + s);
        }

        static IEnumerator WaitReady(BallController ball)
        {
            yield return (Func<bool>)(() => ball.State == BallState.Ready);
        }

        // ------------------------------------------------------------------ tests

        static IEnumerator AccuracyTest()
        {
            var round = Round; var shots = Shots; var ball = shots.Ball; var cfg = round.Config;
            round.spawningEnabled = false;
            round.skipIntro = true;
            round.StartLevel(0);
            Log($"Accuracy test: g={cfg.gravity} angle={cfg.launchAngle} fixedDt={Time.fixedDeltaTime:0.00000} ({1f / Time.fixedDeltaTime:0} Hz) targetHeight={cfg.targetHeight} targetFrameRate={Application.targetFrameRate}");
            float maxErr = 0f, backFlight = 0f, backFlightPred = 0f, minClear = float.MaxValue;
            // Front-row mole hitbox top (up pose) for the clearance check.
            var hb = round.Holes[4].GetComponentInChildren<SphereCollider>(true);
            float hitboxTop = hb.transform.localPosition.y + hb.center.y + hb.radius;
            float frontZ = round.Holes[4].Center.z;
            for (int i = 0; i < round.Holes.Length; i++)
            {
                yield return 0.2f;
                foreach (var h in round.Holes) h.ForceHide();
                yield return WaitReady(ball);
                var hole = round.Holes[i];
                Vector2 swipe = ShotSolver.SwipeForTarget(hole.TargetPoint, ball.LaunchPosition, ball.Radius, shots.ViewCamera, cfg, shots.Forward);
                var shot = shots.Solve(swipe);
                ShotSolver.Simulate(ball.LaunchPosition, shot.velocity, cfg.gravity, Time.fixedDeltaTime, cfg.targetHeight, 1000, null, out var pred, out float predT);
                ShotSolver.Simulate(ball.LaunchPosition, shot.velocity, cfg.gravity, Time.fixedDeltaTime, ball.Radius, 1000, null, out var predGround, out float predGroundT);
                // Also drive the preview with the same shot to confirm preview endpoint == solver endpoint.
                shots.Preview.Show(ball.LaunchPosition, shot.velocity);
                Vector3 previewEnd = shots.Preview.LastEndPoint;
                shots.Preview.Hide();
                if (!ball.Launch(shot.velocity)) { Log($"hole {i}: launch refused"); continue; }
                yield return (Func<bool>)(() => ball.State != BallState.InFlight);
                float err = Vector3.Distance(pred, ball.LastCrossing);
                float errHole = Vector2.Distance(new Vector2(ball.LastCrossing.x, ball.LastCrossing.z), new Vector2(hole.Center.x, hole.Center.z));
                float errGround = Vector2.Distance(new Vector2(predGround.x, predGround.z), new Vector2(ball.LastImpact.x, ball.LastImpact.z));
                maxErr = Mathf.Max(maxErr, err);
                if (i < 3)
                {
                    backFlight = Mathf.Max(backFlight, ball.LastCrossingTime);
                    backFlightPred = Mathf.Max(backFlightPred, predT);
                    // Height of the ball's underside as it passes over the front row.
                    var pts = new System.Collections.Generic.List<Vector3>();
                    ShotSolver.Simulate(ball.LaunchPosition, shot.velocity, cfg.gravity, Time.fixedDeltaTime, cfg.targetHeight, 1000, pts, out _, out _);
                    float yAtFront = 0f;
                    for (int k = 1; k < pts.Count; k++)
                        if (pts[k].z >= frontZ) { float f = (frontZ - pts[k - 1].z) / Mathf.Max(1e-5f, pts[k].z - pts[k - 1].z); yAtFront = Mathf.Lerp(pts[k - 1].y, pts[k].y, f); break; }
                    float clear = yAtFront - ball.Radius - hitboxTop;
                    minClear = Mathf.Min(minClear, clear);
                    Log($"  clearance over front row (z={frontZ:0.0}): ball underside {yAtFront - ball.Radius:0.00} m vs mole hitbox top {hitboxTop:0.00} m -> {clear:0.00} m");
                }
                Log($"hole {i} {hole.name}: swipe=({swipe.x:0.000},{swipe.y:0.000}) |v|={shot.velocity.magnitude:0.00} yaw={shot.yawDegrees:0.0} " +
                    $"pred={pred:F3} actual={(ball.HasCrossing ? ball.LastCrossing.ToString("F3") : "none")} err={err:0.000}m holeErr={errHole:0.000}m " +
                    $"previewEnd={previewEnd:F3} tPred={predT:0.000}s tActual={ball.LastCrossingTime:0.000}s groundPred={predGround:F2} groundActual={ball.LastImpact:F2} groundErr={errGround:0.000}m");
            }
            Log($"MAX targetHeight-crossing error = {maxErr:0.000} m; back-row flight time predicted {backFlightPred:0.000} s (measured {backFlight:0.000} s, frame-quantised); min front-row clearance {minClear:0.00} m -> {(minClear >= 0.3f ? "PASS" : "FAIL")}");
            foreach (var lv in cfg.levels)
            {
                float fastMin = Mathf.Max(cfg.fastVisibleFloor, Mathf.Min(lv.fastVisible.x, lv.fastVisible.y));
                float need = 0.6f + backFlightPred;
                Log($"{lv.name}: fast window min {fastMin:0.00} s vs reaction 0.6 + flight {backFlightPred:0.00} = {need:0.00} s -> margin {fastMin - need:+0.00;-0.00} s {(fastMin - need >= 0.1f ? "REACHABLE" : "TOO TIGHT")}");
            }
            round.spawningEnabled = true;
            round.skipIntro = false;
            round.ShowMenu();
        }

        static IEnumerator RulesTest()
        {
            var round = Round; var shots = Shots; var ball = shots.Ball; var cfg = round.Config; var hud = Hud;
            var crowd = UnityEngine.Object.FindAnyObjectByType<CrowdController>();
            round.spawningEnabled = false;
            round.skipIntro = true;
            MoleShotProgress.ResetAll(round.LevelCount);
            round.StartLevel(round.LevelCount - 1);
            yield return 0.2f;
            foreach (var h in round.Holes) h.ForceHide();

            // 1) Tap is rejected; ball stays ready.
            shots.BeginAim();
            bool launched = shots.ReleaseAim(new Vector2(0f, cfg.minSwipe * 0.5f));
            Log($"TAP: launched={launched} ballState={ball.State} -> {(!launched && ball.State == BallState.Ready ? "PASS" : "FAIL")}");

            // 2) Downward swipe is rejected.
            shots.BeginAim();
            launched = shots.ReleaseAim(new Vector2(0.02f, -0.2f));
            Log($"DOWNWARD: launched={launched} ballState={ball.State} -> {(!launched && ball.State == BallState.Ready ? "PASS" : "FAIL")}");

            // 3) Normal mole hit = 1 point, fast mole hit = 2 points (x1 combo).
            int before = round.Score;
            yield return ShootAt(round.Holes[4], MoleType.Normal, 3f);
            Log($"NORMAL HIT: score {before} -> {round.Score} (expected +1) -> {(round.Score - before == cfg.normalPoints ? "PASS" : "FAIL")}");
            yield return WaitReady(ball);
            before = round.Score;
            yield return ShootAt(round.Holes[1], MoleType.Fast, 3f);
            Log($"FAST HIT (back row): score {before} -> {round.Score} (expected +{cfg.fastPoints}) -> {(round.Score - before == cfg.fastPoints ? "PASS" : "FAIL")}");
            yield return WaitReady(ball);

            // 4) Combo: hit until multiplier 2 and check the award.
            int guard = 0;
            while (round.Multiplier < 2 && guard++ < 6) { yield return ShootAt(round.Holes[3 + guard % 3], MoleType.Normal, 3f); yield return WaitReady(ball); }
            before = round.Score;
            int mult = round.Multiplier;
            yield return ShootAt(round.Holes[2], MoleType.Fast, 3f);
            Log($"COMBO: multiplier={mult} fast hit {before} -> {round.Score} (expected +{cfg.fastPoints * mult}) -> {(round.Score - before == cfg.fastPoints * mult ? "PASS" : "FAIL")}");
            yield return WaitReady(ball);

            // 5) Late arrival: mole ducks before the ball gets there -> miss, no points, combo reset.
            before = round.Score; int missesBefore = round.TotalMisses;
            yield return ShootAt(round.Holes[1], MoleType.Normal, 0.15f);
            Log($"LATE ARRIVAL: score {before} -> {round.Score}, misses {missesBefore} -> {round.TotalMisses}, combo={round.ComboHits} -> {(round.Score == before && round.TotalMisses == missesBefore + 1 && round.ComboHits == 0 ? "PASS" : "FAIL")}");
            yield return WaitReady(ball);

            // 6) Plain miss on an empty hole.
            missesBefore = round.TotalMisses;
            yield return ShootAt(round.Holes[0], null, 0f);
            Log($"EMPTY-HOLE MISS: misses {missesBefore} -> {round.TotalMisses} -> {(round.TotalMisses == missesBefore + 1 ? "PASS" : "FAIL")}");
            yield return WaitReady(ball);

            // 7) Out of bounds: wide shot.
            missesBefore = round.TotalMisses;
            var oobSwipe = new Vector2(0.4f, 0.36f);
            var oob = shots.Solve(oobSwipe);
            ball.Launch(new Vector3(oob.velocity.x * 3f, oob.velocity.y, oob.velocity.z));
            yield return (Func<bool>)(() => ball.State != BallState.InFlight);
            Log($"OUT OF BOUNDS: misses {missesBefore} -> {round.TotalMisses} impact={ball.LastImpact:F1} -> {(round.TotalMisses == missesBefore + 1 ? "PASS" : "FAIL")}");
            yield return WaitReady(ball);

            // 7b) Fairness while emerging: arrival just after the mole passes hittableFromEmergeFraction = hit;
            //     arrival while it is still only peeking = miss.
            {
                var hole = round.Holes[4];
                float delay = hole.HittableDelay;
                before = round.Score;
                yield return ShootTimed(hole, MoleType.Normal, delay + 0.06f);
                Log($"EMERGING HIT (arrive {delay + 0.06f:0.00}s after spawn, hittable from {delay:0.00}s, rise={lastRise:0.00}): score {before} -> {round.Score} -> {(round.Score > before ? "PASS" : "FAIL")}");
                yield return WaitReady(ball);
                foreach (var h in round.Holes) h.ForceHide();
                yield return 0.6f;
                before = round.Score; missesBefore = round.TotalMisses;
                yield return ShootTimed(hole, MoleType.Normal, delay - 0.1f);
                Log($"PEEKING MISS (arrive {delay - 0.1f:0.00}s after spawn, rise={lastRise:0.00}): score {before} -> {round.Score}, misses {missesBefore} -> {round.TotalMisses} -> {(round.Score == before && round.TotalMisses == missesBefore + 1 ? "PASS" : "FAIL")}");
                yield return WaitReady(ball);
                foreach (var h in round.Holes) h.ForceHide();
                yield return 0.6f;
            }

            // 8) Timer expires while the ball is in flight toward a hittable mole -> no score, one results screen.
            int ends = 0;
            Action<int> onEnd = _ => ends++;
            round.RoundEnded += onEnd;
            var target = round.Holes[1];
            target.Spawn(MoleType.Normal, 5f);
            yield return 0.25f;
            before = round.Score;
            Vector2 sw = ShotSolver.SwipeForTarget(target.TargetPoint, ball.LaunchPosition, ball.Radius, shots.ViewCamera, cfg, shots.Forward);
            ball.Launch(shots.Solve(sw).velocity);
            round.DebugSetTimeLeft(0.25f);
            yield return 1.5f;
            round.RoundEnded -= onEnd;
            Log($"EXPIRY DURING FLIGHT (fail): score {before} -> {round.Score}, state={round.State}, win={round.LastWin}, roundEndedEvents={ends}, results={hud.ResultsVisible}, buttons=[{string.Join(",", hud.ResultButtonLabels())}], ball={ball.State}, moles hidden={AllHidden(round)} -> " +
                $"{(round.Score == before && round.State == RoundState.Ended && !round.LastWin && ends == 1 && hud.ResultsVisible && ball.State == BallState.Ready && AllHidden(round) && string.Join(",", hud.ResultButtonLabels()) == "RETRY,MENU" ? "PASS" : "FAIL")}");

            // 9) Launch refused after round end.
            bool refused = !ball.Launch(new Vector3(0, 8, 8));
            Log($"LAUNCH AFTER END refused={refused} -> {(refused ? "PASS" : "FAIL")}");

            // 10) Two restarts (RETRY) give clean rounds.
            for (int k = 1; k <= 2; k++)
            {
                hud.PressResultButton("RETRY");
                if (k == 2) round.Restart();
                yield return 0.1f;
                bool clean = round.Score == 0 && Mathf.Abs(round.TimeLeft - round.Duration) < 0.5f && round.ComboHits == 0 &&
                             ball.State == BallState.Ready && !hud.ResultsVisible && round.IsPlaying && AllHidden(round) && crowd.ActiveCount == 0;
                Log($"RETRY/RESTART {k}: level={round.LevelIndex + 1} score={round.Score} time={round.TimeLeft:0.0} combo={round.ComboHits} ball={ball.State} results={hud.ResultsVisible} hidden={AllHidden(round)} crowdActive={crowd.ActiveCount} -> {(clean ? "PASS" : "FAIL")}");
            }

            // 11) WIN: reaching the goal ends the level once, unlocks the next level (persisted), offers NEXT/RETRY/MENU.
            round.StartLevel(0);
            yield return 0.2f;
            foreach (var h in round.Holes) h.ForceHide();
            ends = 0; int levelEnds = 0; bool lastWin = false;
            Action<int> onEnd2 = _ => ends++;
            Action<bool> onLevel = w => { levelEnds++; lastWin = w; };
            round.RoundEnded += onEnd2; round.LevelEnded += onLevel;
            round.DebugSetHits(round.TargetHits - 1);
            before = round.Score;
            yield return ShootAt(round.Holes[4], MoleType.Normal, 3f);
            yield return 0.2f;
            string labels = string.Join(",", hud.ResultButtonLabels());
            bool unlockSaved = PlayerPrefs.GetInt("MoleShot.UnlockedLevel", 0) >= 1 && MoleShotProgress.IsUnlocked(1) && !MoleShotProgress.IsUnlocked(2);
            Log($"WIN AT TARGET: hits={round.TotalHits}/{round.TargetHits} state={round.State} win={lastWin} roundEnded={ends} levelEnded={levelEnds} results={hud.ResultsVisible} buttons=[{labels}] stars={round.LastStars} -> " +
                $"{(round.State == RoundState.Ended && lastWin && ends == 1 && levelEnds == 1 && hud.ResultsVisible && labels == "NEXT LEVEL,RETRY,MENU" ? "PASS" : "FAIL")}");
            Log($"UNLOCK PERSISTED: PlayerPrefs MoleShot.UnlockedLevel={PlayerPrefs.GetInt("MoleShot.UnlockedLevel", 0)} L2 unlocked={MoleShotProgress.IsUnlocked(1)} L3 unlocked={MoleShotProgress.IsUnlocked(2)} L1 completed={MoleShotProgress.IsCompleted(0)} stars={MoleShotProgress.Stars(0)} -> {(unlockSaved && MoleShotProgress.IsCompleted(0) ? "PASS" : "FAIL")}");

            // 12) A late hit after the win does not count.
            var late = round.Holes[1];
            late.Spawn(MoleType.Normal, 3f);
            yield return (Func<bool>)(() => late.IsHittable);
            int scoreAfterWin = round.Score, hitsAfterWin = round.TotalHits;
            bool accepted = round.TryScoreHit(late);
            Log($"LATE HIT AFTER WIN: accepted={accepted} score {scoreAfterWin} -> {round.Score} hits {hitsAfterWin} -> {round.TotalHits} levelEnded={levelEnds} -> {(!accepted && round.Score == scoreAfterWin && round.TotalHits == hitsAfterWin && levelEnds == 1 ? "PASS" : "FAIL")}");
            late.ForceHide();
            yield return 1.8f;
            Log($"RESULT POPUP ANIMATION finished={hud.ResultAnimationDone}");
            round.RoundEnded -= onEnd2; round.LevelEnded -= onLevel;

            // 13) NEXT LEVEL -> clean level 2 state; MENU -> clean menu state.
            hud.PressResultButton("NEXT LEVEL");
            yield return 0.1f;
            bool nextClean = round.LevelIndex == 1 && round.Score == 0 && round.TotalHits == 0 && Mathf.Abs(round.TimeLeft - round.Duration) < 0.5f &&
                             !hud.ResultsVisible && AllHidden(round) && ball.State == BallState.Ready && crowd.ActiveCount == 0 && round.TargetHits == cfg.levels[1].targetHits;
            Log($"NEXT LEVEL CLEAN: level={round.LevelIndex + 1} target={round.TargetHits} time={round.TimeLeft:0.0} score={round.Score} results={hud.ResultsVisible} crowdActive={crowd.ActiveCount} -> {(nextClean ? "PASS" : "FAIL")}");
            round.DebugSetTimeLeft(0.05f);
            yield return 0.3f;
            hud.PressResultButton("MENU");
            yield return 0.1f;
            bool menuClean = round.State == RoundState.Menu && hud.MenuVisible && !hud.ResultsVisible && AllHidden(round) && ball.State == BallState.Ready && crowd.ActiveCount == 0;
            Log($"MENU CLEAN: state={round.State} menu={hud.MenuVisible} results={hud.ResultsVisible} hidden={AllHidden(round)} ball={ball.State} -> {(menuClean ? "PASS" : "FAIL")}");
            var select = UnityEngine.Object.FindAnyObjectByType<LevelSelectUI>();
            bool lockedRefused = !select.Select(2);
            Log($"LOCKED LEVEL REFUSED (L3 while only L2 unlocked): refused={lockedRefused} state={round.State} -> {(lockedRefused && round.State == RoundState.Menu ? "PASS" : "FAIL")}");

            round.spawningEnabled = true;
            round.skipIntro = false;
        }

        static float lastRise;

        /// <summary>Launches at the hole so the ball crosses targetHeight exactly `arrival` seconds after the mole spawns.</summary>
        static IEnumerator ShootTimed(HoleController hole, MoleType type, float arrival)
        {
            var shots = Shots; var ball = shots.Ball; var cfg = Round.Config;
            yield return WaitReady(ball);
            Vector2 swipe = ShotSolver.SwipeForTarget(hole.TargetPoint, ball.LaunchPosition, ball.Radius, shots.ViewCamera, cfg, shots.Forward);
            var shot = shots.Solve(swipe);
            ShotSolver.Simulate(ball.LaunchPosition, shot.velocity, cfg.gravity, Time.fixedDeltaTime, cfg.targetHeight, 1000, null, out _, out float flight);
            if (arrival >= flight)
            {
                hole.Spawn(type, 5f);
                yield return arrival - flight;
                ball.Launch(shot.velocity);
            }
            else
            {
                ball.Launch(shot.velocity);
                yield return flight - arrival;
                hole.Spawn(type, 5f);
            }
            float until = Time.time + 2f;
            while (ball.State == BallState.InFlight && Time.time < until)
            {
                if (hole.RiseFraction > 0f) lastRise = hole.RiseFraction;
                yield return null;
            }
        }

        static bool AllHidden(RoundManager round)
        {
            foreach (var h in round.Holes) if (!h.IsHidden) return false;
            return true;
        }

        /// <summary>Spawns a mole (or none) at the hole, waits until it is hittable, then fires the solver's shot at it.</summary>
        static IEnumerator ShootAt(HoleController hole, MoleType? type, float window)
        {
            var shots = Shots; var ball = shots.Ball; var cfg = Round.Config;
            yield return WaitReady(ball);
            if (type.HasValue)
            {
                hole.Spawn(type.Value, window);
                yield return (Func<bool>)(() => hole.IsHittable);
            }
            Vector2 swipe = ShotSolver.SwipeForTarget(hole.TargetPoint, ball.LaunchPosition, ball.Radius, shots.ViewCamera, cfg, shots.Forward);
            shots.BeginAim();
            shots.UpdateAim(swipe);
            shots.ReleaseAim(swipe);
            yield return (Func<bool>)(() => ball.State != BallState.InFlight);
        }

        static IEnumerator AutoLevel(int level)
        {
            var round = Round; var hud = Hud;
            var auto = UnityEngine.Object.FindAnyObjectByType<AutoPlayer>(FindObjectsInactive.Include);
            round.spawningEnabled = true;
            round.skipIntro = false;
            auto.autoRestartDelay = -1f;
            auto.autoNavigate = false;
            auto.ResetNavigation();
            int shots0 = auto.ShotsFired, miss0 = auto.DeliberateMisses;
            auto.enabled = true;
            int ends = 0;
            Action<int> onEnd = _ => ends++;
            round.RoundEnded += onEnd;
            round.StartLevel(level);
            float t0 = Time.time;
            Log($"AutoPlayer {round.Level.name} started (goal {round.TargetHits} moles in {round.Duration:0} s)");
            yield return (Func<bool>)(() => round.State == RoundState.Ended);
            float played = Time.time - t0 - round.Config.levelIntroDuration;
            yield return 1.8f;
            Log($"AUTO {round.Level.name}: result={(round.LastWin ? "WIN" : "TIME'S UP")} hits={round.TotalHits}/{round.TargetHits} score={round.Score} misses={round.TotalMisses} " +
                $"timeLeft={round.TimeLeft:0.0}s played={played:0.0}s stars={round.LastStars} shots={auto.ShotsFired - shots0} deliberateMisses={auto.DeliberateMisses - miss0} roundEnded={ends} results={hud.ResultsVisible} buttons=[{string.Join(",", hud.ResultButtonLabels())}]");
            round.RoundEnded -= onEnd;
            auto.enabled = false;
        }

        // ------------------------------------------------------------------ render stats (Editor Game view)

        static ProfilerRecorder recBatches, recSetPass, recDraw, recTris, recMain, recRender;

        public static string StartStats()
        {
            StopStats();
            recBatches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 120);
            recSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 120);
            recDraw = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 120);
            recTris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 120);
            recMain = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 120);
            recRender = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 120);
            return "stats started";
        }

        static void StopStats()
        {
            if (recBatches.Valid) recBatches.Dispose();
            if (recSetPass.Valid) recSetPass.Dispose();
            if (recDraw.Valid) recDraw.Dispose();
            if (recTris.Valid) recTris.Dispose();
            if (recMain.Valid) recMain.Dispose();
            if (recRender.Valid) recRender.Dispose();
        }

        static double Avg(ProfilerRecorder r)
        {
            if (!r.Valid || r.Count == 0) return -1;
            double sum = 0; int n = r.Count;
            for (int i = 0; i < n; i++) sum += r.GetSample(i).Value;
            return sum / n;
        }

        public static string ReadStats()
        {
            return $"samples={recBatches.Count} batches={Avg(recBatches):0} setpass={Avg(recSetPass):0} drawcalls={Avg(recDraw):0} " +
                   $"tris={Avg(recTris):0} mainThread={Avg(recMain) / 1e6:0.00}ms renderThread={Avg(recRender) / 1e6:0.00}ms";
        }

        /// <summary>Selects (adding if needed) a fixed Game view resolution, so captures use the real aspect ratio.</summary>
        public static string SetGameViewSize(int w, int h)
        {
            var asm = typeof(Editor).Assembly;
            var sizesType = asm.GetType("UnityEditor.GameViewSizes");
            var single = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = single.GetProperty("instance").GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { (int)GameViewSizeGroupType.Android });
            var gType = group.GetType();
            int total = (int)gType.GetMethod("GetTotalCount").Invoke(group, null);
            int index = -1;
            for (int i = 0; i < total; i++)
            {
                var sz = gType.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
                int sw = (int)sz.GetType().GetProperty("width").GetValue(sz), sh = (int)sz.GetType().GetProperty("height").GetValue(sz);
                if (sw == w && sh == h) { index = i; break; }
            }
            if (index < 0)
            {
                var sizeType = asm.GetType("UnityEditor.GameViewSize");
                var kind = asm.GetType("UnityEditor.GameViewSizeType");
                var ctor = sizeType.GetConstructor(new[] { kind, typeof(int), typeof(int), typeof(string) });
                var size = ctor.Invoke(new object[] { System.Enum.Parse(kind, "FixedResolution"), w, h, $"MoleShot {w}x{h}" });
                gType.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                index = (int)gType.GetMethod("GetTotalCount").Invoke(group, null) - 1;
            }
            var gvType = asm.GetType("UnityEditor.GameView");
            var gv = EditorWindow.GetWindow(gvType);
            gvType.GetMethod("SizeSelectionCallback", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                  .Invoke(gv, new object[] { index, null });
            gv.Repaint();
            return $"game view {w}x{h} (index {index})";
        }
    }
}
