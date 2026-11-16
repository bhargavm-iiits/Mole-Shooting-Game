using UnityEngine;

namespace MoleShot
{
    /// <summary>Keeps a quad facing the camera (optionally only around the world Y axis).</summary>
    public class Billboard : MonoBehaviour
    {
        [SerializeField] Camera targetCamera;
        [SerializeField] bool yAxisOnly;

        void LateUpdate()
        {
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) return;
            Vector3 fwd = cam.transform.forward;
            if (yAxisOnly) { fwd.y = 0f; if (fwd.sqrMagnitude < 1e-6f) return; }
            transform.rotation = Quaternion.LookRotation(fwd, cam.transform.up);
        }
    }
}
