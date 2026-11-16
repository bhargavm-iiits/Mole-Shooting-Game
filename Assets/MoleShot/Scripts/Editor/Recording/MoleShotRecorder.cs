#if MOLESHOT_RECORDER
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace MoleShot.EditorTools
{
    /// <summary>
    /// Records the demo video with Unity Recorder: AutoPlayer plays one full round, the results card shows, PLAY AGAIN
    /// is pressed, and recording stops a few seconds into the second round. Game View input at 1080x1920, MP4, 30 fps
    /// constant (game time is locked to the capture rate, so the video is smooth even if the Editor renders slowly).
    /// Start it in Play Mode: MoleShot.EditorTools.MoleShotRecorder.StartDemo().
    /// </summary>
    public static class MoleShotRecorder
    {
        static RecorderController controller;
        static float tailAfterRestart = 6f;
        static float restartTime = -1f;
        static int startGeneration;
        public static string Status = "idle";
        static string currentOutput;

        public static string OutputBase => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Deliverables/MoleShot_gameplay"));

        public static string StartDemo() => StartDemo(null);

        public static string StartDemo(string outputBase)
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first";
            var round = Object.FindAnyObjectByType<RoundManager>();
            var auto = Object.FindAnyObjectByType<AutoPlayer>(FindObjectsInactive.Include);
            if (round == null || auto == null) return "Scene objects missing";

            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "MoleShotDemo";
            movie.Enabled = true;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };
            movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1080, OutputHeight = 1920 };
            movie.AudioInputSettings.PreserveAudio = true;
            movie.OutputFile = outputBase ?? OutputBase;
            currentOutput = (outputBase ?? OutputBase) + ".mp4";
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRatePlayback = FrameRatePlayback.Constant;
            settings.FrameRate = 30f;
            settings.CapFrameRate = true;

            MoleShotDebug.LogEvents = true;
            // Level select -> LEVEL 1 (AutoPlayer) -> LEVEL COMPLETE popup -> NEXT LEVEL -> a few seconds of LEVEL 2.
            auto.autoRestartDelay = 3.2f;
            auto.maxAutoRestarts = 1;
            auto.autoNavigate = true;
            auto.startLevel = 0;
            auto.menuDelay = 2.2f;
            auto.deliberateMissChance = 0.08f;
            auto.forceMissOnShot = 3;          // guarantees a readable miss early in the demo
            auto.ResetNavigation();
            auto.enabled = true;
            round.spawningEnabled = true;
            round.skipIntro = false;
            MoleShotProgress.ResetAll(round.LevelCount);
            round.ShowMenu();
            startGeneration = round.RoundGeneration + 1;   // the generation of the LEVEL 1 attempt
            restartTime = -1f;

            controller = new RecorderController(settings);
            controller.PrepareRecording();
            bool ok = controller.StartRecording();
            Status = ok ? "recording" : "failed to start";
            EditorApplication.update -= Tick;
            if (ok) EditorApplication.update += Tick;
            return Status + " -> " + currentOutput;
        }

        static void Tick()
        {
            if (controller == null) { EditorApplication.update -= Tick; return; }
            var round = Object.FindAnyObjectByType<RoundManager>();
            if (!EditorApplication.isPlaying || round == null) { StopNow("play mode exited"); return; }
            if (round.RoundGeneration > startGeneration && restartTime < 0f) restartTime = Time.time;
            if (restartTime >= 0f && Time.time - restartTime >= tailAfterRestart) StopNow("done");
        }

        public static string StopNow(string why = "stopped")
        {
            EditorApplication.update -= Tick;
            if (controller != null && controller.IsRecording()) controller.StopRecording();
            controller = null;
            var auto = Object.FindAnyObjectByType<AutoPlayer>(FindObjectsInactive.Include);
            if (auto != null) auto.enabled = false;
            Status = why;
            Debug.Log("[MoleShot] Recorder " + why + ": " + currentOutput);
            return Status;
        }
    }
}
#endif
