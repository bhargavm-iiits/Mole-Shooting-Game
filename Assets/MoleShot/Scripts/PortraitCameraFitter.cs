using UnityEngine;
using UnityEngine.Rendering;

namespace MoleShot
{
    /// <summary>
    /// Fixed perspective camera that widens its vertical FOV on narrow (tall) screens so that a configured
    /// world half-width, measured at a reference point, always fits horizontally. 9:16 through 9:21 all show
    /// all six holes with margin.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class PortraitCameraFitter : MonoBehaviour
    {
        [Tooltip("World point whose horizontal neighbourhood must fit (e.g. the back row centre).")]
        public Vector3 referencePoint = new Vector3(0f, 0f, 8.5f);
        [Tooltip("Half-width (m) around the reference point that must stay visible.")]
        public float halfWidth = 4f;
        [Tooltip("Never go below this vertical FOV (degrees) on wide screens.")]
        public float minVerticalFov = 50f;
        public float maxVerticalFov = 90f;

        Camera cam;
        float lastAspect = -1f;

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            lastAspect = -1f;
            Fit();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        void OnDisable() { RenderPipelineManager.beginCameraRendering -= OnBeginCamera; }

        // Refit right before rendering so captures / recorders at any resolution get the correct framing.
        void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c == cam && !Mathf.Approximately(cam.aspect, lastAspect)) Fit();
        }

        void LateUpdate() { if (!Mathf.Approximately(cam.aspect, lastAspect)) Fit(); }

        public void Fit()
        {
            if (cam == null) cam = GetComponent<Camera>();
            lastAspect = cam.aspect;
            // Depth of the reference point along the view axis.
            float depth = Vector3.Dot(referencePoint - transform.position, transform.forward);
            if (depth <= 0.01f) return;
            float tanHalfH = halfWidth / depth;
            float vFov = 2f * Mathf.Atan(tanHalfH / Mathf.Max(0.01f, cam.aspect)) * Mathf.Rad2Deg;
            cam.fieldOfView = Mathf.Clamp(vFov, minVerticalFov, maxVerticalFov);
        }
    }
}
