using UnityEngine;
using UnityEngine.EventSystems;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

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
        private bool sprintRequested;
        private bool jumpRequested;
        private bool mediaRequested;
        private bool contractRequested;
        private bool gameplayEnabled;
        private bool cleanRequested, pickupRequested, inventoryRequested;

        public bool GameplayEnabled => gameplayEnabled;
        public bool UseTouchControls => Application.isMobilePlatform || forceTouchControlsForTesting || browserTouch;
        private bool browserTouch = false;
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int SlopCityUsesTouch();
#endif
        private void Awake()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { browserTouch = SlopCityUsesTouch() != 0; }
            catch (System.EntryPointNotFoundException) { browserTouch = TouchSupported(); }
            catch (System.DllNotFoundException) { browserTouch = TouchSupported(); }
#endif
        }

        private static bool TouchSupported()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.touchSupported;
#elif ENABLE_INPUT_SYSTEM
            return UnityEngine.InputSystem.Touchscreen.current != null;
#else
            return Application.isMobilePlatform;
#endif
        }

        private void OnApplicationFocus(bool focused) { if (!focused) ClearTouchState(); }
        private void OnApplicationPause(bool paused) { if (paused) ClearTouchState(); }
        private void OnDisable() { ClearTouchState(); }

        private void ClearTouchState()
        {
            ResetContinuousTouch();
            interactRequested = respawnRequested = jumpRequested = mediaRequested = contractRequested = pauseRequested = cleanRequested = pickupRequested = inventoryRequested = false;
        }

        // Layout changes cancel held gestures without discarding completed clicks.
        public void ResetContinuousTouch()
        {
            touchMovement = touchLook = Vector2.zero;
            sprintRequested = false;
        }

        public void SetGameplayEnabled(bool enabled)
        {
            gameplayEnabled = enabled;
            ClearTouchState();
        }

        public Vector2 ReadMovement()
        {
            if (!gameplayEnabled) return Vector2.zero;
            Vector2 movement = touchMovement;
#if ENABLE_LEGACY_INPUT_MANAGER
            movement.x += (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            movement.y += (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
#elif ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                movement.x += (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                    - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
                movement.y += (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                    - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            }
#endif
            return Vector2.ClampMagnitude(movement, 1f);
        }

        public bool ReadSprint()
        {
            if (!gameplayEnabled) return false;
#if ENABLE_LEGACY_INPUT_MANAGER
            return sprintRequested || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#elif ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return sprintRequested || (keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed));
#else
            return sprintRequested;
#endif
        }

        public Vector2 ReadLook()
        {
            if (!gameplayEnabled) { touchLook = Vector2.zero; return Vector2.zero; }
            // Dragging the same fraction of the display produces the same turn on phones/tablets.
            Vector2 look = touchLook * (126f / Mathf.Max(1, Screen.height));
            touchLook = Vector2.zero;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!UseTouchControls && Cursor.lockState == CursorLockMode.Locked)
                look += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#elif ENABLE_INPUT_SYSTEM
            if (!UseTouchControls && Cursor.lockState == CursorLockMode.Locked && UnityEngine.InputSystem.Mouse.current != null)
                look += UnityEngine.InputSystem.Mouse.current.delta.ReadValue() * .1f;
#endif
            return look;
        }

        public bool ConsumeInteract()
        {
            bool requested = interactRequested;
            interactRequested = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            requested |= Input.GetKeyDown(KeyCode.E);
#elif ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            requested |= keyboard != null && keyboard.eKey.wasPressedThisFrame;
#endif
            return gameplayEnabled && requested;
        }

        public bool ConsumeRespawn()
        {
            bool requested = respawnRequested;
            respawnRequested = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            requested |= Input.GetKeyDown(KeyCode.R);
#elif ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            requested |= keyboard != null && keyboard.rKey.wasPressedThisFrame;
#endif
            return gameplayEnabled && requested;
        }

        public bool ConsumePause()
        {
            bool requested = pauseRequested;
            pauseRequested = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            requested |= Input.GetKeyDown(KeyCode.Escape);
#elif ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            requested |= keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#endif
            return requested;
        }

        public bool ConsumeMedia()
        {
            bool requested = mediaRequested;
            mediaRequested = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            requested |= Input.GetKeyDown(KeyCode.M);
#elif ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            requested |= keyboard != null && keyboard.mKey.wasPressedThisFrame;
#endif
            return requested;
        }

        public bool ConsumeJump()
        {
            bool requested = jumpRequested;
            jumpRequested = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            requested |= Input.GetKeyDown(KeyCode.Space);
#elif ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            requested |= keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#endif
            return gameplayEnabled && requested;
        }

        public bool ConsumeNewContract()
        {
            bool requested = contractRequested;
            contractRequested = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            requested |= Input.GetKeyDown(KeyCode.Q);
#elif ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            requested |= keyboard != null && keyboard.qKey.wasPressedThisFrame;
#endif
            return gameplayEnabled && requested;
        }

        public bool ConsumeClean()
        {
            bool requested = cleanRequested; cleanRequested = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            requested |= Input.GetKeyDown(KeyCode.C);
#elif ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            requested |= keyboard != null && keyboard.cKey.wasPressedThisFrame;
#endif
            return gameplayEnabled && requested;
        }
        public bool ConsumePickup()
        {
            bool requested = pickupRequested; pickupRequested = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            requested |= Input.GetKeyDown(KeyCode.F);
#elif ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            requested |= keyboard != null && keyboard.fKey.wasPressedThisFrame;
#endif
            return gameplayEnabled && requested;
        }
        public bool ConsumeInventory()
        {
            bool requested = inventoryRequested; inventoryRequested = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            requested |= Input.GetKeyDown(KeyCode.I);
#elif ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            requested |= keyboard != null && keyboard.iKey.wasPressedThisFrame;
#endif
            return requested;
        }
        public void RequestClean() { if (gameplayEnabled) cleanRequested = true; }
        public void RequestPickup() { if (gameplayEnabled) pickupRequested = true; }
        public void RequestInventory() { inventoryRequested = true; }

        public void SetTouchMovement(Vector2 movement) { touchMovement = gameplayEnabled ? Vector2.ClampMagnitude(movement, 1f) : Vector2.zero; }
        public void AddTouchLook(Vector2 delta) { if (gameplayEnabled) touchLook += delta; }
        public void RequestInteract() { if (gameplayEnabled) interactRequested = true; }
        public void RequestRespawn() { if (gameplayEnabled) respawnRequested = true; }
        public void RequestPause() { pauseRequested = true; }
        public void RequestSprint(bool value) { sprintRequested = gameplayEnabled && value; }
        public void RequestJump() { if (gameplayEnabled) jumpRequested = true; }
        public void RequestMedia() { if (gameplayEnabled) mediaRequested = true; }
        public void RequestNewContract() { if (gameplayEnabled) contractRequested = true; }
    }

    public sealed class SlopCityTouchStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private SlopCityPlayerInput input;
        private RectTransform surface;
        private RectTransform thumb;
        private RectTransform ring;
        private Vector2 origin;
        private int pointer = int.MinValue;
        public void Configure(SlopCityPlayerInput source, RectTransform knob)
        {
            input = source; thumb = knob; surface = GetComponent<RectTransform>();
        }
        public void Configure(SlopCityPlayerInput source, RectTransform knob, RectTransform baseRing)
        {
            Configure(source, knob); ring = baseRing;
        }
        public void OnPointerDown(PointerEventData data)
        {
            if (pointer != int.MinValue || !input || !input.GameplayEnabled) return;
            pointer = data.pointerId;
            if (ring && RectTransformUtility.ScreenPointToLocalPointInRectangle(surface, data.position, data.pressEventCamera, out origin))
            {
                float radius = ring.rect.width * .5f;
                origin.x = Mathf.Clamp(origin.x, surface.rect.xMin + radius, surface.rect.xMax - radius);
                origin.y = Mathf.Clamp(origin.y, surface.rect.yMin + radius, surface.rect.yMax - radius);
                ring.anchoredPosition = origin;
            }
            UpdateStick(data);
        }
        public void OnDrag(PointerEventData data) { if (data.pointerId == pointer) UpdateStick(data); }
        private void UpdateStick(PointerEventData data)
        {
            if (!input || !surface) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(surface, data.position, data.pressEventCamera, out local)) return;
            float radius = Mathf.Max(1f, (ring ? ring.rect.width : surface.rect.width) * 0.34f);
            Vector2 axis = Vector2.ClampMagnitude((local - (ring ? origin : Vector2.zero)) / radius, 1f);
            if (axis.magnitude < .12f) axis = Vector2.zero;
            input.SetTouchMovement(axis);
            if (thumb) thumb.anchoredPosition = axis * radius;
        }
        public void OnPointerUp(PointerEventData data) { if (pointer == data.pointerId) ResetStick(); }
        public void CancelGesture() { ResetStick(); }
        private void OnDisable() { ResetStick(); }
        private void OnApplicationFocus(bool focused) { if (!focused) ResetStick(); }
        private void OnApplicationPause(bool paused) { if (paused) ResetStick(); }
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
        public void OnPointerDown(PointerEventData data) { if (pointer == int.MinValue && input && input.GameplayEnabled) pointer = data.pointerId; }
        public void OnDrag(PointerEventData data) { if (pointer == data.pointerId && input) input.AddTouchLook(data.delta); }
        public void OnPointerUp(PointerEventData data) { if (pointer == data.pointerId) pointer = int.MinValue; }
        public void CancelGesture() { pointer = int.MinValue; }
        private void OnDisable() { pointer = int.MinValue; }
        private void OnApplicationFocus(bool focused) { if (!focused) pointer = int.MinValue; }
        private void OnApplicationPause(bool paused) { if (paused) pointer = int.MinValue; }
    }
}
