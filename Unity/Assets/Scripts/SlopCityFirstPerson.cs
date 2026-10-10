using UnityEngine;
using UnityEngine.EventSystems;

namespace SlopCity
{
    /// <summary>A local first-person lounge, with a complete start/pause/interaction loop.</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class SlopCityFirstPerson : MonoBehaviour
    {
        public const string TvSourceUrl = "https://www.tiktok.com/@machine_publishers/video/7694500222541597983";
        public const string RadioSourceUrl = "https://www.tiktok.com/@machine_publishers/video/7573075941463756062";

        [Header("Blender export anchors — world positions, Unity metres")]
        [SerializeField] private Transform seatedEyeAnchor;
        [SerializeField] private Transform standingFeetAnchor;
        [SerializeField] private Transform seatedLookTarget;
        [SerializeField] private Transform televisionTarget;
        [SerializeField] private Transform recordPlayerTarget;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private SlopCityHud hud;
        [Header("Movement")]
        [SerializeField] private float walkingSpeed = 2.4f;
        [SerializeField] private float standingEyeHeight = 1.62f;
        [SerializeField] private float mouseSensitivity = 1.8f;
        [SerializeField] private float gravity = -18f;
        [SerializeField] private float interactionDistance = 2.4f;
        [SerializeField] private float couchDistance = 1.6f;

        private CharacterController controller;
        private SlopCityPlayerInput playerInput;
        private bool seated;
        private bool paused = true;
        private bool started;
        private bool sourceOpen;
        private bool stoodUp;
        private bool inspectedBroadcast;
        private bool returnedHome;
        private float yaw;
        private float pitch;
        private float verticalSpeed;
        private float ignoreLookUntil;
        private string feedback;
        private float feedbackUntil;
        private string selectedSource;
        private enum NearbyAction { None, Sit, Television, Records }

        public bool IsPaused => paused;
        public bool IsSeated => seated;

        public void Configure(Transform seat, Transform stand, Transform lookTarget,
            Transform television, Transform records, Camera camera, SlopCityHud overlay)
        {
            seatedEyeAnchor = seat; standingFeetAnchor = stand; seatedLookTarget = lookTarget;
            televisionTarget = television; recordPlayerTarget = records; viewCamera = camera; hud = overlay;
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            playerInput = GetComponent<SlopCityPlayerInput>();
            if (!playerInput) playerInput = gameObject.AddComponent<SlopCityPlayerInput>();
            if (!seatedEyeAnchor || !standingFeetAnchor || !viewCamera)
            {
                Debug.LogError("SLOP CITY: Missing couch spawn anchors or camera. Rebuild from the SLOP CITY menu.", this);
                enabled = false;
            }
        }

        private void Start()
        {
            RespawnAtCouch();
            if (hud) hud.Initialize(this, playerInput);
            SetPaused(true);
            RefreshHud();
        }

        private void Update()
        {
            if (playerInput.ConsumePause())
            {
                if (sourceOpen) PlayOrResume();
                else if (started) SetPaused(!paused);
            }
            if (paused) { RefreshHud(); return; }
            if (playerInput.ConsumeRespawn()) RespawnAtCouch();
            if (playerInput.ConsumeInteract())
            {
                if (seated) StandUp();
                else PerformInteraction(FindNearbyAction());
            }
            if (!paused)
            {
                Vector2 look = playerInput.ReadLook();
                if (Time.unscaledTime < ignoreLookUntil) look = Vector2.zero;
                yaw += look.x * mouseSensitivity;
                pitch = Mathf.Clamp(pitch - look.y * mouseSensitivity, -75f, 75f);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                if (!seated)
                {
                    Vector2 axis = playerInput.ReadMovement();
                    Vector3 direction = Vector3.ClampMagnitude(transform.right * axis.x + transform.forward * axis.y, 1f);
                    ApplyMovement(direction * walkingSpeed);
                    if (transform.position.y < standingFeetAnchor.position.y - 5f)
                    {
                        RespawnAtCouch();
                        ShowFeedback("Returned safely to the couch.");
                    }
                }
            }
            RefreshHud();
        }

        public void PlayOrResume()
        {
            started = true; sourceOpen = false;
            if (hud) hud.HideSource();
            SetPaused(false);
        }

        public void RestartLounge()
        {
            stoodUp = inspectedBroadcast = returnedHome = false;
            selectedSource = null;
            RespawnAtCouch();
            PlayOrResume();
            ShowFeedback("A fresh start. Get up and inspect the broadcast.");
        }

        public void QuitGame()
        {
            Time.timeScale = 1f; UnlockCursor();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void SetPaused(bool value)
        {
            paused = value;
            Time.timeScale = value ? 0f : 1f;
            playerInput.SetGameplayEnabled(!value);
            if (!value) ignoreLookUntil = Time.unscaledTime + 0.1f;
            if (value || playerInput.UseTouchControls) UnlockCursor();
            else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (hud) hud.SetMenu(value, started);
            if (!value && EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }

        private void ApplyMovement(Vector3 horizontalVelocity)
        {
            if (!controller.enabled) return;
            if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
            verticalSpeed = Mathf.Max(verticalSpeed + gravity * Time.deltaTime, -25f);
            controller.Move((horizontalVelocity + Vector3.up * verticalSpeed) * Time.deltaTime);
        }

        public void RespawnAtCouch()
        {
            if (!controller || !seatedEyeAnchor || !viewCamera) return;
            if (stoodUp && inspectedBroadcast && !seated) returnedHome = true;
            controller.enabled = false;
            seated = true; verticalSpeed = 0f;
            transform.position = seatedEyeAnchor.position;
            viewCamera.transform.localPosition = Vector3.zero;
            Transform target = seatedLookTarget ? seatedLookTarget : televisionTarget;
            Vector3 forward = target ? target.position - seatedEyeAnchor.position : seatedEyeAnchor.forward;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg, -75f, 75f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            feedback = null;
            if (returnedHome) ShowFeedback("LOUNGE LOOP COMPLETE / Make yourself at home. Keep exploring or restart from the menu.");
        }

        private void StandUp()
        {
            Vector3 destination = standingFeetAnchor.position;
            float radius = controller.radius * 0.9f;
            Vector3 bottom = destination + Vector3.up * (controller.radius + 0.02f);
            Vector3 top = destination + Vector3.up * (controller.height - controller.radius);
            if (Physics.CheckCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                ShowFeedback("There is not enough room to stand here. Press R to reset the couch position.");
                return;
            }
            controller.enabled = false;
            transform.position = destination;
            viewCamera.transform.localPosition = Vector3.up * standingEyeHeight;
            controller.enabled = true;
            verticalSpeed = -2f; seated = false; stoodUp = true;
            ShowFeedback(inspectedBroadcast ? "Back on your feet." : "Find the television and press E to inspect its broadcast.");
        }

        private NearbyAction FindNearbyAction()
        {
            if (seated) return NearbyAction.None;
            bool nearTv = CanReach(televisionTarget);
            bool nearRecords = CanReach(recordPlayerTarget);
            if (nearTv && nearRecords)
                return (televisionTarget.position - viewCamera.transform.position).sqrMagnitude
                    <= (recordPlayerTarget.position - viewCamera.transform.position).sqrMagnitude
                    ? NearbyAction.Television : NearbyAction.Records;
            if (nearTv) return NearbyAction.Television;
            if (nearRecords) return NearbyAction.Records;
            Vector3 toCouch = standingFeetAnchor.position - transform.position;
            toCouch.y = 0f;
            return toCouch.sqrMagnitude <= couchDistance * couchDistance ? NearbyAction.Sit : NearbyAction.None;
        }

        private bool CanReach(Transform target)
        {
            if (!target) return false;
            Vector3 direction = target.position - viewCamera.transform.position;
            if (direction.sqrMagnitude > interactionDistance * interactionDistance) return false;
            return direction.sqrMagnitude < 0.01f || Vector3.Dot(viewCamera.transform.forward, direction.normalized) > 0.40f;
        }

        private void PerformInteraction(NearbyAction action)
        {
            switch (action)
            {
                case NearbyAction.Sit: RespawnAtCouch(); break;
                case NearbyAction.Television:
                    inspectedBroadcast = true;
                    InspectSource("MACHINE PUBLISHERS", "Broadcast inspected. The signal reaches the lounge.\n\nReturn to the couch to finish your first loop. You can also open the original TikTok in your browser using the button below.", TvSourceUrl);
                    break;
                case NearbyAction.Records:
                    InspectSource("HOOD RICH RECORDS", "The lounge's record corner.\n\nOpen the linked original source in your browser, or return to the room. Media does not play automatically.", RadioSourceUrl);
                    break;
            }
        }

        private void InspectSource(string title, string description, string url)
        {
            selectedSource = url;
            sourceOpen = true;
            SetPaused(true);
            if (hud) hud.ShowSource(title, description);
        }

        public void OpenSelectedSource()
        {
            // Only the explicit source-card button launches an external browser.
            if (selectedSource != TvSourceUrl && selectedSource != RadioSourceUrl) return;
            Application.OpenURL(selectedSource);
        }

        private void RefreshHud()
        {
            if (!hud) return;
            string key = playerInput.UseTouchControls ? "TAP INTERACT" : "[E]";
            string prompt;
            if (!string.IsNullOrEmpty(feedback) && Time.unscaledTime < feedbackUntil) prompt = feedback;
            else if (seated) prompt = key + " GET UP FROM THE COUCH";
            else
            {
                switch (FindNearbyAction())
                {
                    case NearbyAction.Television: prompt = key + " INSPECT MACHINE PUBLISHERS BROADCAST"; break;
                    case NearbyAction.Records: prompt = key + " INSPECT HOOD RICH RECORDS"; break;
                    case NearbyAction.Sit: prompt = key + " SIT BACK DOWN"; break;
                    default: prompt = "HOME BASE / EXPLORE THE LOUNGE"; break;
                }
            }
            string controls = playerInput.UseTouchControls
                ? "LEFT PAD  MOVE     DRAG RIGHT SIDE  LOOK     INTERACT  USE     COUCH  RESET"
                : "WASD / ARROWS  MOVE     MOUSE  LOOK     E  INTERACT     R  COUCH     ESC  PAUSE";
            hud.Render(seated ? "DANIEL / SEATED AT HOME" : "DANIEL / ON YOUR FEET", prompt, controls, !paused);
            hud.SetObjectives(stoodUp, inspectedBroadcast, returnedHome);
        }

        private void ShowFeedback(string text) { feedback = text; feedbackUntil = Time.unscaledTime + 4.5f; }
        private static void UnlockCursor() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        private void OnApplicationFocus(bool focused)
        {
            if (!focused && playerInput && started)
            {
                SetPaused(true);
                if (sourceOpen && hud) hud.ShowSourceTitleOnly();
            }
        }
        private void OnApplicationPause(bool suspended)
        {
            if (suspended && playerInput && started)
            {
                SetPaused(true);
                if (sourceOpen && hud) hud.ShowSourceTitleOnly();
            }
        }
        private void OnDisable() { Time.timeScale = 1f; UnlockCursor(); }
    }
}
