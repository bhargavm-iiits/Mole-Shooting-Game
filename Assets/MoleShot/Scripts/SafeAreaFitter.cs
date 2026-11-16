using UnityEngine;

namespace MoleShot
{
    /// <summary>Fits a full-screen RectTransform to Screen.safeArea (notches, rounded corners, gesture bars).</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour
    {
        Rect lastSafe;
        Vector2Int lastSize;

        void OnEnable() => Apply(true);
        void Update() => Apply(false);

        void Apply(bool force)
        {
            Rect safe = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (!force && safe == lastSafe && size == lastSize) return;
            lastSafe = safe; lastSize = size;
            if (size.x <= 0 || size.y <= 0) return;

            var rt = (RectTransform)transform;
            Vector2 min = safe.position, max = safe.position + safe.size;
            min.x /= size.x; min.y /= size.y; max.x /= size.x; max.y /= size.y;
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
