using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MoleShot
{
    /// <summary>
    /// Hit / miss feedback: particle bursts and pooled floating texts ("+2 x2", "MISS") that track a world point.
    /// </summary>
    public class FeedbackController : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] RectTransform popupLayer;
        [SerializeField] Text popupTemplate;
        [SerializeField] ParticleSystem hitBurst;
        [SerializeField] ParticleSystem dustPuff;
        [SerializeField] Color normalHitColor = new Color(1f, 0.86f, 0.2f);
        [SerializeField] Color fastHitColor = new Color(1f, 0.6f, 0.1f);
        [SerializeField] Color missColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] int poolSize = 8;
        [SerializeField] float popupLifetime = 0.9f;
        [Tooltip("Where out-of-bounds misses are announced (anchored position in the popup layer, normalized 0..1).")]
        [SerializeField] Vector2 outOfBoundsAnchor = new Vector2(0.5f, 0.7f);

        class Popup
        {
            public Text text;
            public RectTransform rect;
            public Vector3 world;
            public bool screenFixed;
            public Vector2 screenNorm;
            public float age, life, rise;
            public Color color;
            public float scale;
            public bool active;
        }

        readonly List<Popup> pool = new List<Popup>();

        void Awake()
        {
            if (popupTemplate == null) return;
            popupTemplate.gameObject.SetActive(false);
            for (int i = 0; i < poolSize; i++)
            {
                var t = Instantiate(popupTemplate, popupLayer);
                t.gameObject.SetActive(false);
                pool.Add(new Popup { text = t, rect = t.rectTransform });
            }
        }

        public void ShowHit(HoleController hole, int points, int multiplier)
        {
            bool fast = hole.MoleType == MoleType.Fast;
            Vector3 p = hole.Center + Vector3.up * 0.8f;
            if (hitBurst != null)
            {
                hitBurst.transform.position = p;
                var main = hitBurst.main;
                main.startColor = new ParticleSystem.MinMaxGradient(fast ? fastHitColor : normalHitColor, Color.white);
                hitBurst.Emit(fast ? 40 : 28);
            }
            string label = multiplier > 1 ? $"+{points}  x{multiplier}" : $"+{points}";
            Spawn(label, p + Vector3.up * 0.4f, false, Vector2.zero, fast ? fastHitColor : normalHitColor, fast ? 1.25f : 1.1f, 0.9f);
        }

        public void ShowMiss(Vector3 point, bool outOfBounds)
        {
            if (!outOfBounds && dustPuff != null)
            {
                dustPuff.transform.position = new Vector3(point.x, 0.05f, point.z);
                dustPuff.Emit(14);
            }
            Spawn("MISS", point + Vector3.up * 0.5f, outOfBounds, outOfBoundsAnchor, missColor, 0.7f, 0.45f);
        }

        public void ClearAll()
        {
            foreach (var p in pool) { p.active = false; p.text.gameObject.SetActive(false); }
            if (hitBurst != null) hitBurst.Clear();
            if (dustPuff != null) dustPuff.Clear();
        }

        void Spawn(string label, Vector3 world, bool screenFixed, Vector2 screenNorm, Color color, float scale, float rise)
        {
            Popup p = null;
            foreach (var c in pool) if (!c.active) { p = c; break; }
            if (p == null)
            {
                // Recycle the oldest.
                foreach (var c in pool) if (p == null || c.age > p.age) p = c;
            }
            if (p == null) return;
            p.active = true; p.age = 0f; p.life = popupLifetime; p.world = world; p.screenFixed = screenFixed;
            p.screenNorm = screenNorm; p.color = color; p.scale = scale; p.rise = rise;
            p.text.text = label;
            p.text.color = color;
            p.text.gameObject.SetActive(true);
            p.rect.SetAsLastSibling();
            Place(p);
        }

        void LateUpdate()
        {
            foreach (var p in pool)
            {
                if (!p.active) continue;
                p.age += Time.deltaTime;
                if (p.age >= p.life) { p.active = false; p.text.gameObject.SetActive(false); continue; }
                Place(p);
            }
        }

        void Place(Popup p)
        {
            float t = p.age / p.life;
            Vector2 local;
            if (p.screenFixed)
            {
                Rect r = popupLayer.rect;
                local = new Vector2(r.xMin + r.width * p.screenNorm.x, r.yMin + r.height * p.screenNorm.y + t * 60f);
            }
            else
            {
                var cam = viewCamera != null ? viewCamera : Camera.main;
                Vector3 sp = cam.WorldToScreenPoint(p.world + Vector3.up * (p.rise * t));
                RectTransformUtility.ScreenPointToLocalPointInRectangle(popupLayer, sp, null, out local);
            }
            // Keep the label on screen (e.g. "+2 x2" over a mole at the right edge).
            Rect layer = popupLayer.rect;
            float halfW = p.text.preferredWidth * 0.5f * p.scale + 20f, halfH = p.text.preferredHeight * 0.5f * p.scale + 20f;
            local.x = Mathf.Clamp(local.x, layer.xMin + halfW, layer.xMax - halfW);
            local.y = Mathf.Clamp(local.y, layer.yMin + halfH, layer.yMax - halfH);
            p.rect.anchoredPosition = local;
            // Pop in, then fade out.
            float pop = t < 0.15f ? Mathf.Lerp(0.6f, 1.15f, t / 0.15f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((t - 0.15f) / 0.15f));
            p.rect.localScale = Vector3.one * (p.scale * pop);
            Color c = p.color; c.a *= t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            p.text.color = c;
        }
    }
}
