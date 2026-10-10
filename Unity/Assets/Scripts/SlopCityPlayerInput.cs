using UnityEngine;
using UnityEngine.EventSystems;

namespace SlopCity
{
    /// <summary>Keyboard/mouse today; the same movement contract accepts mobile touch.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityPlayerInput : MonoBehaviour
    {
        [SerializeField] private bool forceTouchControlsForTesting;
        private Vector2 touchMovement;
        private Vector2 touchLook;
        private bool interactRequested;
        private bool respawnRequested;
        private bool pauseRequested;
        private bool gameplayEnabled;

        public bool UseTouchControls => Application.isMobilePlatform || forceTouchControlsForTesting;

        public void SetGameplayEnabled(bool enabled)
        {
            gameplayEnabled = enabled;
            touchMovement = touchLook = Vector2.zero;
            interactRequested = respawnRequested = false;
        }

        public Vector2 ReadMovement()
        {
            if (!gameplayEnabled) return Vector2.zero;
            Vector2 movement = UseTouchControls ? touchMovement : Vector2.zero;
#if ENABLE_LEGACY_INPUT_MANAGER
            movement.x += (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            movement.y += (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
#endif
            return Vector2.ClampMagnitude(movement, 1f);
        }

        public Vector2 ReadLook()
        {
            if (!gameplayEnabled) { touchLook = Vector2.zero; return Vector2.zero; }
            Vector2 look = touchLook * 0.14f;
            touchLook = Vector2.zero;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!UseTouchControls && Cursor.lockState == CursorLockMode.Locked)
                look += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#endif
            return look;
        }

        public bool ConsumeInteract()
        {
            bool requested = interactRequested;
            interactRequested = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            requested |= Input.GetKeyDown(KeyCode.E);
#endif
            return gameplayEnabled && requested;
        }

        public bool ConsumeRespawn()
        {
            bool requested = respawnRequested;
            respawnRequested = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            requested |= Input.GetKeyDown(KeyCode.R);
#endif
            return gameplayEnabled && requested;
        }

        public bool ConsumePause()
        {
            bool requested = pauseRequested;
            pauseRequested = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            requested |= Input.GetKeyDown(KeyCode.Escape);
#endif
            return requested;
        }

        public void SetTouchMovement(Vector2 movement) { touchMovement = Vector2.ClampMagnitude(movement, 1f); }
        public void AddTouchLook(Vector2 delta) { if (gameplayEnabled) touchLook += delta; }
        public void RequestInteract() { if (gameplayEnabled) interactRequested = true; }
        public void RequestRespawn() { if (gameplayEnabled) respawnRequested = true; }
        public void RequestPause() { pauseRequested = true; }
    }

    public sealed class SlopCityTouchStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private SlopCityPlayerInput input;
        private RectTransform surface;
        private RectTransform thumb;
        private int pointer = int.MinValue;
        public void Configure(SlopCityPlayerInput source, RectTransform knob)
        {
            input = source; thumb = knob; surface = GetComponent<RectTransform>();
        }
        public void OnPointerDown(PointerEventData data)
        {
            if (pointer != int.MinValue) return;
            pointer = data.pointerId; UpdateStick(data);
        }
        public void OnDrag(PointerEventData data) { if (data.pointerId == pointer) UpdateStick(data); }
        private void UpdateStick(PointerEventData data)
        {
            if (!input || !surface) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(surface, data.position, data.pressEventCamera, out local)) return;
            float radius = Mathf.Max(1f, surface.rect.width * 0.34f);
            Vector2 axis = Vector2.ClampMagnitude(local / radius, 1f);
            input.SetTouchMovement(axis);
            if (thumb) thumb.anchoredPosition = axis * radius;
        }
        public void OnPointerUp(PointerEventData data) { if (pointer == data.pointerId) ResetStick(); }
        private void OnDisable() { ResetStick(); }
        private void ResetStick()
        {
            pointer = int.MinValue;
            if (input) input.SetTouchMovement(Vector2.zero);
            if (thumb) thumb.anchoredPosition = Vector2.zero;
        }
    }

    public sealed class SlopCityTouchLook : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private SlopCityPlayerInput input;
        private int pointer = int.MinValue;
        public void Configure(SlopCityPlayerInput source) { input = source; }
        public void OnPointerDown(PointerEventData data) { if (pointer == int.MinValue) pointer = data.pointerId; }
        public void OnDrag(PointerEventData data) { if (pointer == data.pointerId && input) input.AddTouchLook(data.delta); }
        public void OnPointerUp(PointerEventData data) { if (pointer == data.pointerId) pointer = int.MinValue; }
        private void OnDisable() { pointer = int.MinValue; }
    }
}
