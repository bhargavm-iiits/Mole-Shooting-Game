using UnityEngine;
using UnityEngine.UI;

namespace MoleShot
{
    /// <summary>
    /// Level select screen: one card per level showing its goal and time, a lock for locked levels, and the best
    /// score plus a tick for completed ones. Level 1 is always unlocked; completing a level unlocks the next.
    /// </summary>
    public class LevelSelectUI : MonoBehaviour
    {
        [SerializeField] RoundManager round;
        [SerializeField] GameObject root;
        [SerializeField] Button[] cards;
        [SerializeField] Image[] cardImages;
        [SerializeField] Text[] titles;
        [SerializeField] Text[] infos;
        [SerializeField] Text[] statuses;
        [SerializeField] GameObject[] locks;
        [SerializeField] GameObject[] ticks;
        [SerializeField] Button resetButton;
        [Tooltip("3 star images per card, flattened: card0 star0..2, card1 star0..2, ...")]
        [SerializeField] Image[] stars;
        [SerializeField] Color starOnColor = new Color(1f, 0.82f, 0.15f);
        [SerializeField] Color starOffColor = new Color(1f, 1f, 1f, 0.18f);
        [SerializeField] Color unlockedColor = new Color(0.08f, 0.2f, 0.45f, 0.97f);
        [SerializeField] Color lockedColor = new Color(0.2f, 0.24f, 0.32f, 0.92f);

        public bool IsVisible => root != null && root.activeSelf;

        void Awake()
        {
            for (int i = 0; i < cards.Length; i++)
            {
                int index = i;
                cards[i].onClick.AddListener(() => Select(index));
            }
            if (resetButton != null) resetButton.onClick.AddListener(ResetProgress);
        }

        public void Show()
        {
            if (root != null) root.SetActive(true);
            Refresh();
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        /// <summary>Starts the level if it is unlocked. Returns false for a locked level.</summary>
        public bool Select(int index)
        {
            if (!MoleShotProgress.IsUnlocked(index)) return false;
            round.StartLevel(index);
            return true;
        }

        public void ResetProgress()
        {
            MoleShotProgress.ResetAll(round.LevelCount);
            Refresh();
        }

        public void Refresh()
        {
            var levels = round.Config.levels;
            for (int i = 0; i < cards.Length; i++)
            {
                bool exists = i < levels.Length;
                cards[i].gameObject.SetActive(exists);
                if (!exists) continue;
                var lv = levels[i];
                bool unlocked = MoleShotProgress.IsUnlocked(i);
                bool done = MoleShotProgress.IsCompleted(i);
                int best = MoleShotProgress.Best(i);
                int secs = Mathf.RoundToInt(lv.duration);
                titles[i].text = lv.name;
                infos[i].text = $"Hit {lv.targetHits} moles  ·  {secs / 60}:{secs % 60:00}";
                statuses[i].text = !unlocked ? "LOCKED" : done ? $"COMPLETED  ·  BEST {best}" : best > 0 ? $"BEST {best}" : "PLAY";
                locks[i].SetActive(!unlocked);
                ticks[i].SetActive(done);
                cards[i].interactable = unlocked;
                int earned = MoleShotProgress.Stars(i);
                for (int k = 0; k < 3 && stars != null && i * 3 + k < stars.Length; k++)
                {
                    stars[i * 3 + k].gameObject.SetActive(unlocked);
                    stars[i * 3 + k].color = k < earned ? starOnColor : starOffColor;
                }
                if (cardImages != null && i < cardImages.Length) cardImages[i].color = unlocked ? unlockedColor : lockedColor;
            }
        }
    }
}
