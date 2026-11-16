using UnityEngine;

namespace MoleShot
{
    /// <summary>Global debug switches (off in normal play).</summary>
    public static class MoleShotDebug
    {
        /// <summary>Log every hit / miss / round end to the console (used by automated play-tests).</summary>
        public static bool LogEvents;

        public static void Log(string msg)
        {
            if (LogEvents) Debug.Log("[MoleShot] " + msg);
        }
    }
}
