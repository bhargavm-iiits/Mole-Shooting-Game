using UnityEngine;
using UnityEngine.UI;

namespace MoleShot
{
    /// <summary>
    /// Celebration confetti for the LEVEL COMPLETE popup: a pooled set of small UI pieces that burst upward from
    /// the top of the card, tumble, fall under gravity and fade. Update-driven, no allocations after Awake.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ConfettiUI : MonoBehaviour
    {
        [SerializeField] Sprite pieceSprite;
        [SerializeField] int count = 90;
        [SerializeField] float duration = 2.4f;
        [SerializeField] float gravity = -1900f;
        [SerializeField] Color[] colors =
        {
            new Color(1f, 0.84f, 0.12f), new Color(0.3f, 0.89f, 0.25f), new Color(0.25f, 0.6f, 1f),
            new Color(1f, 0.4f, 0.35f), new Color(1f, 1f, 1f), new Color(0.9f, 0.45f, 1f)
        };

        RectTransform[] pieces;
        Image[] images;
        Vector2[] pos, vel;
        float[] rot, spin, flip;
        float time = -1f;
        System.Random rnd = new System.Random(5);

        public bool IsPlaying => time >= 0f && time < duration;

        void Awake()
        {
            pieces = new RectTransform[count]; images = new Image[count];
            pos = new Vector2[count]; vel = new Vector2[count];
            rot = new float[count]; spin = new float[count]; flip = new float[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Piece", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var img = go.AddComponent<Image>();
                img.sprite = pieceSprite;
                img.raycastTarget = false;
                pieces[i] = (RectTransform)go.transform;
                pieces[i].sizeDelta = new Vector2(18f, 30f);
                images[i] = img;
                go.SetActive(false);
            }
        }

        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        /// <summary>Bursts the confetti from a point (local to this rect, e.g. the top of the card).</summary>
        public void Play(Vector2 origin)
        {
            if (pieces == null) Awake();
            time = 0f;
            for (int i = 0; i < count; i++)
            {
                pos[i] = origin + new Vector2(R(-260f, 260f), R(-20f, 20f));
                float ang = R(55f, 125f) * Mathf.Deg2Rad;
                float spd = R(900f, 1900f);
                vel[i] = new Vector2(Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd);
                rot[i] = R(0f, 360f); spin[i] = R(-720f, 720f); flip[i] = R(4f, 11f);
                images[i].color = colors[rnd.Next(colors.Length)];
                pieces[i].sizeDelta = new Vector2(R(14f, 22f), R(24f, 36f));
                pieces[i].gameObject.SetActive(true);
            }
            Step(0f);
        }

        public void Stop()
        {
            time = -1f;
            if (pieces == null) return;
            foreach (var p in pieces) p.gameObject.SetActive(false);
        }

        void Update()
        {
            if (time < 0f) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            time += dt;
            if (time >= duration) { Stop(); return; }
            Step(dt);
        }

        void Step(float dt)
        {
            float fade = Mathf.Clamp01((duration - time) / 0.6f);
            for (int i = 0; i < count; i++)
            {
                vel[i].y += gravity * dt;
                vel[i] *= 1f - 0.9f * dt;            // air drag so pieces flutter down
                pos[i] += vel[i] * dt;
                rot[i] += spin[i] * dt;
                pieces[i].anchoredPosition = pos[i];
                pieces[i].localRotation = Quaternion.Euler(0f, 0f, rot[i]);
                pieces[i].localScale = new Vector3(Mathf.Cos(time * flip[i] + i), 1f, 1f);   // paper-flip shimmer
                var c = images[i].color; c.a = fade; images[i].color = c;
            }
        }
    }
}
