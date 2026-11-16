using UnityEngine;
using UnityEngine.UI;

namespace MoleShot
{
    /// <summary>
    /// Stadium-Kickoff HUD: score capsule, timer capsule with draining bar, combo badge, goal counter (MOLES n/N),
    /// LEVEL n intro banner, SWIPE TO SHOOT pill, and the result card (LEVEL COMPLETE / TIME'S UP) with
    /// NEXT / REPLAY / RETRY / MENU buttons. Reads the RoundManager every frame (no duplicated state).
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [SerializeField] RoundManager round;
        [SerializeField] LevelSelectUI levelSelect;
        [Tooltip("In-level HUD group (score, timer, goal, hint); hidden on the level select.")]
        [SerializeField] GameObject hudRoot;
        [SerializeField] Text scoreText;
        [SerializeField] RectTransform scoreRoot;
        [SerializeField] Text timerText;
        [SerializeField] RectTransform timerFill;
        [SerializeField] Image timerFillImage;
        [SerializeField] GameObject comboBadge;
        [SerializeField] Text comboText;
        [SerializeField] Text goalText;
        [SerializeField] RectTransform goalFill;
        [SerializeField] CanvasGroup swipeHint;
        [SerializeField] CanvasGroup introBanner;
        [SerializeField] Text introTitle;
        [SerializeField] Text introSubtitle;
        [SerializeField] GameObject resultsPanel;
        [SerializeField] Text resultsTitle;
        [SerializeField] Text resultsLine;
        [SerializeField] Text resultsScoreText;
        [SerializeField] Text resultsBestText;
        [SerializeField] GameObject newBestLabel;
        [SerializeField] Button[] resultButtons;
        [SerializeField] Text[] resultButtonLabels;
        [SerializeField] Color timerColor = new Color(0.30f, 0.89f, 0.25f);
        [SerializeField] Color timerLowColor = new Color(1f, 0.33f, 0.25f);
        [SerializeField] Color winTitleColor = new Color(1f, 0.84f, 0.12f);
        [SerializeField] Color failTitleColor = new Color(1f, 0.45f, 0.35f);

        [Header("Result popup animation")]
        [SerializeField] AudioFeedback audioFeedback;
        [SerializeField] ConfettiUI confetti;
        [SerializeField] RectTransform resultsCard;
        [SerializeField] CanvasGroup resultsGroup;
        [SerializeField] CanvasGroup resultButtonsGroup;
        [SerializeField] RectTransform resultButtonsRoot;
        [SerializeField] Image[] resultStars;
        [SerializeField] Color starOnColor = new Color(1f, 0.82f, 0.15f);
        [SerializeField] Color starOffColor = new Color(0.16f, 0.22f, 0.38f, 1f);

        float resultTime = -1f;
        bool resultWin, confettiFired;
        int resultStarCount, resultScore, starsChimed;
        Vector2 cardBasePos, buttonsBasePos;
        bool basesCached;

        bool hintVisible;
        float scorePulse, introTime;
        int shownScore = -1, shownSeconds = -1, shownMult = -1, shownHits = -1;

        public bool ResultsVisible => resultsPanel != null && resultsPanel.activeSelf;
        public bool MenuVisible => levelSelect != null && levelSelect.IsVisible;

        void Awake()
        {
            if (resultsPanel != null) resultsPanel.SetActive(false);
            if (introBanner != null) introBanner.alpha = 0f;
        }

        public void ShowMenu()
        {
            if (resultsPanel != null) resultsPanel.SetActive(false);
            if (hudRoot != null) hudRoot.SetActive(false);
            if (introBanner != null) introBanner.alpha = 0f;
            hintVisible = false;
            if (levelSelect != null) levelSelect.Show();
        }

        public void OnRoundStarted(bool withIntro)
        {
            if (resultsPanel != null) resultsPanel.SetActive(false);
            if (levelSelect != null) levelSelect.Hide();
            if (hudRoot != null) hudRoot.SetActive(true);
            shownScore = shownSeconds = shownMult = shownHits = -1;
            scorePulse = 0f;
            introTime = withIntro ? round.Config.levelIntroDuration : 0f;
            if (introTitle != null) introTitle.text = round.Level.name;
            if (introSubtitle != null) introSubtitle.text = $"HIT {round.TargetHits} MOLES  ·  {FormatTime(round.Duration)}";
            if (introBanner != null) introBanner.alpha = withIntro ? 1f : 0f;
        }

        public void ShowLevelResult(bool win, bool newBest)
        {
            if (resultsPanel == null) return;
            resultsPanel.SetActive(true);
            bool allDone = win && !round.HasNextLevel;
            resultsTitle.text = win ? (allDone ? "ALL LEVELS\nCOMPLETE!" : "LEVEL COMPLETE!") : "TIME'S UP";
            resultsTitle.color = win ? winTitleColor : failTitleColor;
            resultsLine.text = win
                ? $"MOLES {round.TotalHits}/{round.TargetHits}   ·   TIME LEFT {FormatTime(round.TimeLeft)}"
                : $"{round.TotalHits}/{round.TargetHits} MOLES   ·   {round.TargetHits - round.TotalHits} MORE NEEDED";
            resultScore = round.Score;
            resultsScoreText.text = "0";
            resultsBestText.text = "BEST  " + round.Best;
            if (newBestLabel != null) newBestLabel.SetActive(newBest && round.Score > 0);

            if (win && round.HasNextLevel) SetButtons(("NEXT LEVEL", round.NextLevel), ("RETRY", round.Restart), ("MENU", round.ShowMenu));
            else if (win) SetButtons(("RETRY", round.Restart), ("MENU", round.ShowMenu), (null, null));
            else SetButtons(("RETRY", round.Restart), ("MENU", round.ShowMenu), (null, null));

            // Start the popup sequence: dim fade, card pop (win) / drop + shake (time's up), stars stamp in,
            // score counts up, confetti, then the buttons slide in.
            if (!basesCached && resultsCard != null && resultButtonsRoot != null)
            {
                cardBasePos = resultsCard.anchoredPosition; buttonsBasePos = resultButtonsRoot.anchoredPosition; basesCached = true;
            }
            resultWin = win;
            resultStarCount = round.LastStars;
            resultTime = 0f;
            confettiFired = false;
            starsChimed = 0;
            if (resultStars != null)
                for (int i = 0; i < resultStars.Length; i++)
                {
                    resultStars[i].gameObject.SetActive(true);   // empty stars on time-up keep the card balanced
                    resultStars[i].color = starOffColor;
                    resultStars[i].rectTransform.localScale = Vector3.one;
                }
            if (confetti != null) confetti.Stop();
            AnimateResult(0f);
        }

        public bool ResultAnimationDone => resultTime < 0f || resultTime >= 1.6f;

        static float EaseOutBack(float t, float k = 1.7f)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + (k + 1f) * t * t * t + k * t * t;
        }

        void AnimateResult(float dt)
        {
            if (resultTime < 0f) return;
            resultTime += dt;
            float t = resultTime;
            if (resultsGroup != null) resultsGroup.alpha = Mathf.Clamp01(t / 0.18f);

            if (resultsCard != null)
            {
                if (resultWin)
                {
                    float sc = Mathf.LerpUnclamped(0.45f, 1f, EaseOutBack(t / 0.42f, 2.2f));
                    float pulse = t > 1.6f ? 1f + 0.012f * Mathf.Sin((t - 1.6f) * 3f) : 1f;
                    resultsCard.localScale = Vector3.one * sc * pulse;
                    resultsCard.anchoredPosition = cardBasePos;
                }
                else
                {
                    // Drop in from above, settle, then a short "no" shake.
                    float drop = 1f - EaseOutBack(t / 0.45f, 1.2f);
                    float shake = t > 0.45f && t < 0.85f ? Mathf.Sin((t - 0.45f) * 55f) * 18f * (1f - (t - 0.45f) / 0.4f) : 0f;
                    resultsCard.localScale = Vector3.one;
                    resultsCard.anchoredPosition = cardBasePos + new Vector2(shake, drop * 700f);
                }
            }

            // Stars stamp in one by one with a punch and a chime.
            if (resultWin && resultStars != null)
            {
                for (int i = 0; i < resultStars.Length; i++)
                {
                    float st = t - (0.5f + 0.24f * i);
                    var rt = resultStars[i].rectTransform;
                    bool earned = i < resultStarCount;
                    if (st < 0f) { rt.localScale = earned ? Vector3.zero : Vector3.one; continue; }
                    if (earned)
                    {
                        resultStars[i].color = starOnColor;
                        float punch = st < 0.28f ? Mathf.LerpUnclamped(0f, 1f, EaseOutBack(st / 0.28f, 3f)) : 1f;
                        float glow = 1f + 0.06f * Mathf.Sin(t * 5f + i);
                        rt.localScale = Vector3.one * punch * (st > 0.28f ? glow : 1f);
                        rt.localRotation = Quaternion.Euler(0f, 0f, st < 0.28f ? (1f - st / 0.28f) * -40f : 0f);
                        if (starsChimed <= i) { starsChimed = i + 1; if (audioFeedback != null) audioFeedback.PlayStar(i); }
                    }
                    else rt.localScale = Vector3.one;
                }
            }

            // Score count-up.
            float c = Mathf.Clamp01((t - 0.35f) / 0.8f);
            resultsScoreText.text = Mathf.RoundToInt(resultScore * (1f - Mathf.Pow(1f - c, 3f))).ToString();

            // Confetti burst from the top of the card.
            if (resultWin && !confettiFired && t >= 0.3f && confetti != null)
            {
                confettiFired = true;
                var crt = (RectTransform)confetti.transform;
                Vector2 local = crt.InverseTransformPoint(resultsCard != null ? resultsCard.TransformPoint(new Vector3(0f, resultsCard.rect.yMax - 120f, 0f)) : crt.position);
                confetti.Play(local);
            }

            // Buttons slide up and fade in last.
            if (resultButtonsGroup != null)
            {
                float b = Mathf.Clamp01((t - (resultWin ? 1.15f : 0.7f)) / 0.3f);
                resultButtonsGroup.alpha = b;
                resultButtonsGroup.interactable = b > 0.9f;
                resultButtonsGroup.blocksRaycasts = b > 0.9f;
                if (resultButtonsRoot != null) resultButtonsRoot.anchoredPosition = buttonsBasePos + new Vector2(0f, (1f - EaseOutBack(b, 1.4f)) * -80f);
            }
        }

        void SetButtons(params (string label, UnityEngine.Events.UnityAction action)[] defs)
        {
            for (int i = 0; i < resultButtons.Length; i++)
            {
                var b = resultButtons[i];
                bool on = i < defs.Length && defs[i].label != null;
                b.gameObject.SetActive(on);
                b.onClick.RemoveAllListeners();
                if (!on) continue;
                resultButtonLabels[i].text = defs[i].label;
                b.onClick.AddListener(defs[i].action);
            }
        }

        /// <summary>Test / AutoPlayer helper: the label of each visible result button, in order.</summary>
        public string[] ResultButtonLabels()
        {
            var list = new System.Collections.Generic.List<string>();
            for (int i = 0; i < resultButtons.Length; i++) if (resultButtons[i].gameObject.activeSelf) list.Add(resultButtonLabels[i].text);
            return list.ToArray();
        }

        /// <summary>Test / AutoPlayer helper: presses the visible result button with this label.</summary>
        public bool PressResultButton(string label)
        {
            for (int i = 0; i < resultButtons.Length; i++)
                if (resultButtons[i].gameObject.activeSelf && resultButtonLabels[i].text == label) { resultButtons[i].onClick.Invoke(); return true; }
            return false;
        }

        public void SetSwipeHint(bool visible) => hintVisible = visible;

        public void PulseScore() => scorePulse = 1f;

        static string FormatTime(float seconds)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        void Update()
        {
            if (resultsPanel != null && resultsPanel.activeSelf) AnimateResult(Mathf.Min(Time.unscaledDeltaTime, 0.05f));
            else resultTime = -1f;
            if (round == null || hudRoot == null || !hudRoot.activeSelf) return;

            int score = round.Score;
            if (score != shownScore)
            {
                shownScore = score;
                scoreText.text = score.ToString("00");
            }

            float left = round.TimeLeft;
            int secs = Mathf.CeilToInt(left);
            if (secs != shownSeconds)
            {
                shownSeconds = secs;
                timerText.text = FormatTime(left);
            }
            float frac = round.Duration > 0f ? Mathf.Clamp01(left / round.Duration) : 0f;
            if (timerFill != null) timerFill.anchorMax = new Vector2(frac, 1f);
            if (timerFillImage != null)
            {
                bool low = left <= 10f && round.IsPlaying;
                timerFillImage.color = low ? Color.Lerp(timerColor, timerLowColor, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f)) : timerColor;
            }

            int hits = round.TotalHits;
            if (hits != shownHits)
            {
                shownHits = hits;
                if (goalText != null) goalText.text = $"MOLES {hits}/{round.TargetHits}";
                if (goalFill != null) goalFill.anchorMax = new Vector2(Mathf.Clamp01(hits / (float)Mathf.Max(1, round.TargetHits)), 1f);
            }

            int mult = round.IsPlaying ? round.Multiplier : 1;
            if (mult != shownMult)
            {
                shownMult = mult;
                if (comboBadge != null) comboBadge.SetActive(mult > 1);
                if (comboText != null) comboText.text = "x" + mult;
            }

            if (swipeHint != null)
            {
                float target = hintVisible && round.State != RoundState.Intro ? 1f : 0f;
                swipeHint.alpha = Mathf.MoveTowards(swipeHint.alpha, target, Time.unscaledDeltaTime * 4f);
            }

            if (introBanner != null && introTime > 0f)
            {
                introTime -= Time.deltaTime;
                float total = round.Config.levelIntroDuration;
                // Hold, then fade out over the last 0.3 s.
                introBanner.alpha = Mathf.Clamp01(introTime / 0.3f);
                float t = 1f - Mathf.Clamp01(introTime / Mathf.Max(0.01f, total));
                introBanner.transform.localScale = Vector3.one * Mathf.Lerp(1.15f, 1f, Mathf.Clamp01(t * 4f));
            }

            if (scoreRoot != null)
            {
                scorePulse = Mathf.MoveTowards(scorePulse, 0f, Time.unscaledDeltaTime * 4f);
                float s = 1f + 0.18f * Mathf.Sin(scorePulse * Mathf.PI);
                scoreRoot.localScale = new Vector3(s, s, 1f);
            }
        }
    }
}
