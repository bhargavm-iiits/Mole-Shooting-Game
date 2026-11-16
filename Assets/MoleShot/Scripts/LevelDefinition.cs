using UnityEngine;

namespace MoleShot
{
    /// <summary>
    /// One level: length, goal (moles to hit) and its own difficulty ranges. Each Vector2 is
    /// [level start, level end], lerped by the progressive-difficulty curve while the level runs.
    /// </summary>
    [System.Serializable]
    public class LevelDefinition
    {
        public string name = "LEVEL";
        [Tooltip("Level length in seconds.")]
        [Range(10f, 300f)] public float duration = 60f;
        [Tooltip("Moles to hit to complete the level (counts hits, not points).")]
        [Range(1, 100)] public int targetHits = 10;
        [Tooltip("Seconds between spawns.")]
        public Vector2 spawnInterval = new Vector2(1.5f, 1.1f);
        [Tooltip("Maximum moles above ground at once (rounded).")]
        public Vector2 maxActiveMoles = new Vector2(1f, 1f);
        [Tooltip("Hittable seconds for a normal mole.")]
        public Vector2 normalVisible = new Vector2(2.8f, 2.2f);
        [Tooltip("Hittable seconds for a fast mole (never below MoleShotConfig.fastVisibleFloor).")]
        public Vector2 fastVisible = new Vector2(1.8f, 1.55f);
        [Tooltip("Probability that a spawn is a fast mole.")]
        public Vector2 fastMoleChance = new Vector2(0.15f, 0.25f);

        public LevelDefinition() { }

        public LevelDefinition(string name, float duration, int targetHits, Vector2 spawnInterval, Vector2 maxActive,
                               Vector2 normalVisible, Vector2 fastVisible, Vector2 fastChance)
        {
            this.name = name; this.duration = duration; this.targetHits = targetHits;
            this.spawnInterval = spawnInterval; maxActiveMoles = maxActive;
            this.normalVisible = normalVisible; this.fastVisible = fastVisible; fastMoleChance = fastChance;
        }
    }
}
