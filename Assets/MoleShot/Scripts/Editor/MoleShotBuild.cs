using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MoleShot.EditorTools
{
    /// <summary>Android APK build entry point (menu or -executeMethod MoleShot.EditorTools.MoleShotBuild.BuildAndroid).</summary>
    public static class MoleShotBuild
    {
        public static string LastResult = "not run";

        public static string ApkPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Deliverables/MoleShot.apk"));

        [MenuItem("Mole Shot/Build Android APK")]
        public static void BuildAndroid()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            EditorUserBuildSettings.buildAppBundle = false;
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MoleShotSceneBuilder.ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            LastResult = $"{s.result} size={s.totalSize} bytes time={s.totalTime} errors={s.totalErrors} warnings={s.totalWarnings} path={ApkPath}";
            Debug.Log("[MoleShot] Android build: " + LastResult);
        }
    }
}
