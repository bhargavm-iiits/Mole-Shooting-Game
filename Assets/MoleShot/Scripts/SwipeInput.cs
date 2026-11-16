using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace MoleShot
{
    /// <summary>
    /// One input path for touch (device) and mouse (Editor), built on the Input System.
    /// Owns exactly one pointer from press to release/cancel and ignores every other touch.
    /// A press only counts if it starts near the ball on screen. Deltas are normalized by Screen.height.
    /// </summary>
    public class SwipeInput : MonoBehaviour
    {
        const int MousePointerId = -1000;
        const int NoPointer = int.MinValue;

        [SerializeField] MoleShotConfig config;
        [SerializeField] Camera worldCamera;
        [SerializeField] Transform ball;
        [Tooltip("When false, presses are ignored and any active aim is cancelled.")]
        public bool inputEnabled = true;

        public event Action AimStarted;
        public event Action<Vector2> AimUpdated;
        public event Action<Vector2> Released;
        public event Action Cancelled;

        int activePointer = NoPointer;
        Vector2 startPos, lastPos;

        public bool IsAiming => activePointer != NoPointer;

        void OnEnable() { EnhancedTouchSupport.Enable(); }

        void OnDisable()
        {
            CancelActive();
            EnhancedTouchSupport.Disable();
        }

        /// <summary>Drops the current aim (if any) and raises Cancelled.</summary>
        public void CancelActive()
        {
            if (activePointer == NoPointer) return;
            activePointer = NoPointer;
            Cancelled?.Invoke();
        }

        void Update()
        {
            if (!inputEnabled) { CancelActive(); return; }

            if (activePointer == NoPointer) TryBegin();
            else Track();
        }

        void TryBegin()
        {
            foreach (var t in Touch.activeTouches)
            {
                if (t.phase != TouchPhase.Began) continue;
                if (Begin(t.touchId, t.screenPosition)) return;
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Touch.activeTouches.Count == 0)
                Begin(MousePointerId, mouse.position.ReadValue());
        }

        bool Begin(int pointerId, Vector2 pos)
        {
            if (!IsNearBall(pos) || IsOverUI(pointerId)) return false;
            activePointer = pointerId;
            startPos = lastPos = pos;
            AimStarted?.Invoke();
            AimUpdated?.Invoke(Vector2.zero);
            return true;
        }

        void Track()
        {
            if (activePointer == MousePointerId)
            {
                var mouse = Mouse.current;
                if (mouse == null) { CancelActive(); return; }
                Vector2 pos = mouse.position.ReadValue();
                if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed) { Finish(pos); return; }
                Move(pos);
                return;
            }

            foreach (var t in Touch.activeTouches)
            {
                if (t.touchId != activePointer) continue;
                switch (t.phase)
                {
                    case TouchPhase.Canceled: CancelActive(); return;
                    case TouchPhase.Ended: Finish(t.screenPosition); return;
                    default: Move(t.screenPosition); return;
                }
            }
            // Our touch disappeared without an Ended phase we could observe: release at last known position.
            Finish(lastPos);
        }

        void Move(Vector2 pos)
        {
            if ((pos - lastPos).sqrMagnitude < 0.01f) return;
            lastPos = pos;
            AimUpdated?.Invoke(Normalize(pos - startPos));
        }

        void Finish(Vector2 pos)
        {
            lastPos = pos;
            activePointer = NoPointer;
            Released?.Invoke(Normalize(pos - startPos));
        }

        static Vector2 Normalize(Vector2 pixels) => pixels / Mathf.Max(1, Screen.height);

        bool IsNearBall(Vector2 screenPos)
        {
            if (worldCamera == null || ball == null || config == null) return true;
            Vector3 b = worldCamera.WorldToScreenPoint(ball.position);
            if (b.z <= 0f) return false;
            return Vector2.Distance(screenPos, b) <= config.launchAreaRadius * Screen.height;
        }

        static bool IsOverUI(int pointerId)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            return pointerId == MousePointerId ? es.IsPointerOverGameObject() : es.IsPointerOverGameObject(pointerId);
        }
    }
}
