using UnityEngine;

namespace MoleShot
{
    /// <summary>Level progress persisted in PlayerPrefs: highest unlocked level, completed flags, best scores and stars.</summary>
    public static class MoleShotProgress
    {
        const string UnlockedKey = "MoleShot.UnlockedLevel";
        static string BestKey(int level) => "MoleShot.Best.L" + level;
        static string DoneKey(int level) => "MoleShot.Done.L" + level;
        static string StarsKey(int level) => "MoleShot.Stars.L" + level;

        /// <summary>Index of the highest unlocked level (level 0 is always unlocked).</summary>
        public static int UnlockedLevel => Mathf.Max(0, PlayerPrefs.GetInt(UnlockedKey, 0));
        public static bool IsUnlocked(int level) => level <= UnlockedLevel;
        public static bool IsCompleted(int level) => PlayerPrefs.GetInt(DoneKey(level), 0) == 1;
        public static int Best(int level) => PlayerPrefs.GetInt(BestKey(level), 0);
        public static int Stars(int level) => PlayerPrefs.GetInt(StarsKey(level), 0);

        /// <summary>
        /// Stars for a won level: 1 for completing it, +1 for finishing with at least 35 % of the time left,
        /// +1 for shot accuracy of at least 75 %.
        /// </summary>
        public static int ComputeStars(bool won, float timeLeft, float duration, int hits, int misses)
        {
            if (!won) return 0;
            int stars = 1;
            if (duration > 0f && timeLeft / duration >= 0.35f) stars++;
            if (hits + misses > 0 && hits / (float)(hits + misses) >= 0.75f) stars++;
            return stars;
        }

        /// <summary>Records a finished level. Returns true if the score is a new best.</summary>
        public static bool Record(int level, int score, bool won, int stars, int levelCount)
        {
            bool newBest = score > Best(level);
            if (newBest) PlayerPrefs.SetInt(BestKey(level), score);
            if (won)
            {
                PlayerPrefs.SetInt(DoneKey(level), 1);
                if (stars > Stars(level)) PlayerPrefs.SetInt(StarsKey(level), stars);
                if (level + 1 < levelCount && UnlockedLevel < level + 1) PlayerPrefs.SetInt(UnlockedKey, level + 1);
            }
            PlayerPrefs.Save();
            return newBest;
        }

        public static void ResetAll(int levelCount)
        {
            PlayerPrefs.DeleteKey(UnlockedKey);
            for (int i = 0; i < levelCount; i++)
            {
                PlayerPrefs.DeleteKey(BestKey(i)); PlayerPrefs.DeleteKey(DoneKey(i)); PlayerPrefs.DeleteKey(StarsKey(i));
            }
            PlayerPrefs.DeleteKey("MoleShot.BestScore");
            PlayerPrefs.Save();
        }
    }
}
