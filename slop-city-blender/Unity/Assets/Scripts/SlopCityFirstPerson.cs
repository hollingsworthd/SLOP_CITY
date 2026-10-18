using UnityEngine;
using UnityEngine.EventSystems;

namespace SlopCity
{
    /// <summary>First-person home, physical door interactions, and the nine-block city.</summary>
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
        [SerializeField] private SlopCityMediaPlayer media;
        [SerializeField] private SlopCityDistrict[] districts = new SlopCityDistrict[0];
        [Header("Movement")]
        [SerializeField] private float walkingSpeed = 3.2f;
        [SerializeField] private float sprintingSpeed = 5.5f;
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
        private SlopCityMediaChannel selectedChannel;
        private bool appFocused = true;
        private bool appSuspended;
        private SlopCityDoor[] doors;
        private SlopCityDoor nearbyDoor;
        private SlopCitySandbox sandbox;
        private SlopCityCityMedia cityMedia;
        private SlopCityCrowd crowd;
        private bool externalMovement;
#if UNITY_WEBGL && !UNITY_EDITOR
        private bool browserCursorLocked;
        private float nextBrowserHudRefresh;
#endif
        private readonly bool[] visitedBlocks = new bool[9];
        private Bounds homeBounds;
        private bool hasHomeBounds;
        private string currentDistrictName = "HOME / DANIEL'S LOUNGE";
        private enum NearbyAction { None, Sit, Television, Records, Door }

        public bool IsPaused => paused;
        public Camera ViewCamera => viewCamera;
        public SlopCitySandbox Sandbox => sandbox;
        public bool IsSeated => seated;
        public SlopCityMediaPlayer Media => media;
        public SlopCityDistrict[] Districts => districts;
        public string CurrentDistrictName => currentDistrictName;
        public int VisitedBlockCount { get; private set; }
        public bool HasVisited(int index) => index >= 0 && index < visitedBlocks.Length && visitedBlocks[index];
        public float HomeDistance
        {
            get
            {
                if (!standingFeetAnchor) return 0f;
                Vector3 offset = transform.position - standingFeetAnchor.position;
                offset.y = 0f;
                return offset.magnitude;
            }
        }
        public bool IsOutside => !seated && (hasHomeBounds ? !homeBounds.Contains(transform.position) : HomeDistance > 7f);

        public void ConfigureMedia(SlopCityMediaPlayer player) { media = player; }
        public void ConfigureCity(SlopCityDistrict[] blocks) { districts = blocks ?? new SlopCityDistrict[0]; }

        public void Configure(Transform seat, Transform stand, Transform lookTarget,
            Transform television, Transform records, Camera camera, SlopCityHud overlay)
        {
            seatedEyeAnchor = seat; standingFeetAnchor = stand; seatedLookTarget = lookTarget;
            televisionTarget = television; recordPlayerTarget = records; viewCamera = camera; hud = overlay;
        }

        private void Awake()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browser Escape can release pointer lock without delivering a key event.
            WebGLInput.stickyCursorLock = false;
#endif
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
            doors = FindObjectsByType<SlopCityDoor>(FindObjectsSortMode.None);
            GameObject floor = GameObject.Find("Floor_Main");
            Renderer floorRenderer = floor ? floor.GetComponent<Renderer>() : null;
            if (floorRenderer)
            {
                homeBounds = floorRenderer.bounds;
                homeBounds.Expand(new Vector3(0.1f, 5f, 0.1f));
                hasHomeBounds = true;
            }
            RespawnAtCouch();
            if (hud) hud.Initialize(this, playerInput);
            SlopCityBuildingWorld buildingWorld = SlopCityBuildingWorld.Ensure(this);
            SlopCityMetroWorld.Ensure(this);
            SlopCitySteakTable.Ensure(this);
            sandbox = GetComponent<SlopCitySandbox>();
            if (!sandbox) sandbox = gameObject.AddComponent<SlopCitySandbox>();
            sandbox.Initialize(transform, ShowFeedback);
            SlopCityStreetCars.Ensure(this);
            sandbox.RefreshWorldGeometry();
            SlopCitySalvageHud.Ensure(this, sandbox, playerInput, viewCamera);
            // Work utilities are installed by Sandbox. Recheck pedestrians after the
            // final street props exist, without translating the baked routes again.
            Physics.SyncTransforms();
            crowd = FindFirstObjectByType<SlopCityCrowd>();
            if (crowd)
            {
                crowd.RemapLayout((index, point) => point);
                crowd.PreparePopulation(buildingWorld);
                crowd.LocalAgentReportRequested += ShowLocalReport;
            }
            sandbox.StartMatrixSearch(this);
            sandbox.TeleportRequested += TeleportTo;
            sandbox.MovementOverrideRequested += SetExternalMovement;
            sandbox.MunicipalReportRequested += ShowLocalReport;
            cityMedia = FindFirstObjectByType<SlopCityCityMedia>();
            SetPaused(true);
            RefreshHud();
            SlopCityPhoneBooths.Ensure(this);
        }

        private bool inventoryOpen;
        public bool InventoryOpen => inventoryOpen;
        public void SetInventoryOpen(bool value)
        {
            if (value == inventoryOpen) return;
            inventoryOpen = value;
            SetPaused(value);
            if (value && hud) hud.SetMenu(false, started);
        }

        private void Update()
        {
            if (inventoryOpen)
            {
                if (playerInput.ConsumeInventory() || playerInput.ConsumePause()) SetInventoryOpen(false);
                return;
            }
            if (started && !paused && playerInput.ConsumeInventory()) { sandbox.OpenInventory(); return; }
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!paused && !playerInput.UseTouchControls)
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                if (browserCursorLocked && !locked)
                {
                    SetPaused(true);
                    RefreshHud();
                    return;
                }
                browserCursorLocked = locked;
#if ENABLE_LEGACY_INPUT_MANAGER
                // Retry only on a player click if the browser deferred the first request.
                if (!locked && Input.GetMouseButtonDown(0))
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
#endif
            }
#endif
            if (playerInput.ConsumePause())
            {
                if (sourceOpen) PlayOrResume();
                else if (started) SetPaused(!paused);
            }
            if (paused)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                RefreshBrowserHud();
#else
                RefreshHud();
#endif
                return;
            }
            if (playerInput.ConsumeNewContract() && sandbox) sandbox.RerollObjective();
            if (playerInput.ConsumeRespawn()) RespawnAtCouch();
            // Consume mobile taps even while seated or driving; they must never jump on a later exit.
            bool jumpRequested = playerInput.ConsumeJump();
#if (!UNITY_WEBGL || UNITY_EDITOR) && !SLOP_CITY_WEB_INTERACTION_TEST
            if (playerInput.ConsumeInteract())
            {
                if (seated) StandUp();
                else
                {
                    NearbyAction action = FindNearbyAction();
                    if (action != NearbyAction.None) PerformInteraction(action);
                    else if (sandbox) sandbox.Interact(viewCamera);
                }
            }
#endif
            if (!paused)
            {
                Vector2 look = playerInput.ReadLook();
                if (Time.unscaledTime < ignoreLookUntil) look = Vector2.zero;
                yaw += look.x * mouseSensitivity;
                pitch = Mathf.Clamp(pitch - look.y * mouseSensitivity, -75f, 75f);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                if (!seated && !externalMovement)
                {
                    Vector2 axis = playerInput.ReadMovement();
                    Vector3 direction = Vector3.ClampMagnitude(transform.right * axis.x + transform.forward * axis.y, 1f);
                    if (jumpRequested && controller.isGrounded) verticalSpeed = 5.3f;
                    ApplyMovement(direction * (playerInput.ReadSprint() ? sprintingSpeed : walkingSpeed) * (sandbox ? sandbox.SpeedMultiplier : 1f));
                    if (transform.position.y < -25f)
                    {
                        RespawnAtCouch();
                        ShowFeedback("Returned safely to the couch.");
                    }
                }
            }

#if (UNITY_WEBGL && !UNITY_EDITOR) || SLOP_CITY_WEB_INTERACTION_TEST
            // Resolve E after this frame's look and movement, exactly where the bullseye points.
            if (playerInput.ConsumeInteract()) HandleBrowserInteraction();
#endif
            if (playerInput.ConsumeClean() && sandbox) sandbox.CleanAimed(viewCamera);
            if (playerInput.ConsumePickup() && sandbox) sandbox.TakeAimed(viewCamera);
            UpdateCityProgress();
            if (sandbox) sandbox.Tick(Time.deltaTime);
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshBrowserHud();
#else
            RefreshHud();
#endif
        }

        public void PlayOrResume()
        {
            bool firstPlay = !started;
            started = true; sourceOpen = false;
            if (hud) hud.HideSource();
            SetPaused(false);
            if (firstPlay && media) media.Play(SlopCityMediaChannel.Broadcast);
        }

        public void RestartLounge()
        {
            stoodUp = inspectedBroadcast = returnedHome = false;
            System.Array.Clear(visitedBlocks, 0, visitedBlocks.Length);
            VisitedBlockCount = 0;
            selectedChannel = SlopCityMediaChannel.Broadcast;
            if (media) media.Stop();
            if (sandbox) sandbox.Restart();
            RespawnAtCouch();
            PlayOrResume();
            if (media) media.Play(selectedChannel);
            ShowFeedback("Back at home. A fresh contract is ready; your earned reputation and credits are saved.");
        }

        public void QuitGame()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Leave a usable page and an explicit way to start playing again.
            started = false; sourceOpen = false;
            if (media) media.Stop();
            SetPaused(true);
#else
            Time.timeScale = 1f; UnlockCursor();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
#endif
        }


        private void SetPaused(bool value)
        {
            paused = value;
#if UNITY_WEBGL && !UNITY_EDITOR
            browserCursorLocked = false;
            nextBrowserHudRefresh = 0;
#endif
            RefreshMediaSuspension();
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
            if (sandbox) sandbox.ReturnHome();
            if (stoodUp && inspectedBroadcast && !seated) returnedHome = true;
            externalMovement = false;
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
            UpdateCityProgress();
            if (returnedHome) ShowFeedback("HOME AGAIN / Your city discoveries are saved for this session. Head out whenever you're ready.");
        }

        private void StandUp()
        {
            Vector3 destination = standingFeetAnchor.position;
            SlopCityBuildingWorld world = SlopCityBuildingWorld.Ensure(this);
            if (world && !world.PrepareWalkingSpace(destination)) return;
            float radius = controller.radius * 0.9f;
            Vector3 bottom = destination + Vector3.up * (controller.radius + 0.02f);
            Vector3 top = destination + Vector3.up * (controller.height - controller.radius);
            if (Physics.CheckCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                ShowFeedback("There is not enough room to stand here. " + (playerInput.UseTouchControls
                    ? "Tap HOME to reset the couch position." : "Press R to reset the couch position."));
                return;
            }
            controller.enabled = false;
            transform.position = destination;
            viewCamera.transform.localPosition = Vector3.up * standingEyeHeight;
            controller.enabled = true;
            verticalSpeed = -2f; seated = false; stoodUp = true;
            ShowFeedback(playerInput.UseTouchControls
                ? "Tap USE at the home door. Use a woman in red for a brief signal. Use the living-room TV to choose media, or the record player for In-Q-Tel."
                : "E opens the home door. Use a woman in red for a brief signal. Use the living-room TV to choose media, or the record player for In-Q-Tel.");
        }

        public void SetExternalMovement(bool value)
        {
            externalMovement = value;
            if (controller) controller.enabled = !value && !seated;
        }

        /// <summary>Carry a walking passenger with a physical platform, retaining capsule collision.</summary>
        public void MoveWithPlatform(Vector3 delta)
        {
            if (!controller || !controller.enabled || seated || externalMovement || paused) return;
            controller.Move(delta);
            if (verticalSpeed < 0f) verticalSpeed = -2f;
        }

        /// <summary>Carry an enabled capsule around a railway curve without replacing normal walking.</summary>
        public void MoveWithVehicle(Vector3 delta, float yawDelta)
        {
            if (!controller || !controller.enabled || seated || externalMovement || paused) return;
            controller.Move(delta);
            yaw += yawDelta;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (verticalSpeed < 0f) verticalSpeed = -2f;
        }

        /// <summary>Called once after a group of streamed rooms changes, never per rendered frame.</summary>
        public void RefreshWorldInteractions()
        {
            doors = FindObjectsByType<SlopCityDoor>(FindObjectsSortMode.None);
            if (sandbox) sandbox.RefreshWorldGeometry();
        }

        /// <summary>Transit arrival/vehicle exit. Callers supply a capsule-clear, ground-supported destination.</summary>
        public void TeleportTo(Vector3 position, float heading = 0f)
        {
            SlopCityBuildingWorld world = SlopCityBuildingWorld.Ensure(this);
            if (world && !world.PrepareWalkingSpace(position)) return;
            controller.enabled = false;
            transform.position = position;
            seated = false; verticalSpeed = -2f; yaw = heading; pitch = 0f;
            if (!externalMovement) viewCamera.transform.localPosition = Vector3.up * standingEyeHeight;
            viewCamera.transform.localRotation = Quaternion.identity;
            transform.rotation = Quaternion.Euler(0f, heading, 0f);
            controller.enabled = !externalMovement;
            Physics.SyncTransforms();
        }

        public void NewContract() { if (sandbox) sandbox.RerollObjective(); }

        private NearbyAction FindNearbyAction()
        {
#if (UNITY_WEBGL && !UNITY_EDITOR) || SLOP_CITY_WEB_INTERACTION_TEST
            nearbyDoor = null;
            if (seated || !sandbox || sandbox.IsDriving || sandbox.IsTravelling || sandbox.IsResting) return NearbyAction.None;
            Transform target = sandbox.BrowserTarget(viewCamera);
            if (!target) return NearbyAction.None;
            nearbyDoor = target.GetComponentInParent<SlopCityDoor>();
            if (nearbyDoor && nearbyDoor.isActiveAndEnabled) return NearbyAction.Door;
            // These authored home props are siblings of their interaction anchors in the FBX.
            bool televisionPart = televisionTarget && televisionTarget.parent && target.IsChildOf(televisionTarget.parent)
                || media && target.IsChildOf(media.transform);
            bool recordPart = recordPlayerTarget && recordPlayerTarget.parent && target.IsChildOf(recordPlayerTarget.parent);
            for (Transform part = target; part; part = part.parent)
            {
                string label = part.name;
                if (televisionPart && (label.StartsWith("Broadcast_TV_", System.StringComparison.Ordinal)
                    || label.StartsWith("TV_", System.StringComparison.Ordinal) && !label.StartsWith("TV_Console", System.StringComparison.Ordinal)))
                    return NearbyAction.Television;
                if (recordPart && (label.StartsWith("Turntable", System.StringComparison.Ordinal)
                    || label.StartsWith("HoodRich_Radio", System.StringComparison.Ordinal)
                    || label.StartsWith("Radio_", System.StringComparison.Ordinal)))
                    return NearbyAction.Records;
                if (standingFeetAnchor && standingFeetAnchor.parent && target.IsChildOf(standingFeetAnchor.parent)
                    && label.StartsWith("SC_Couch", System.StringComparison.Ordinal)) return NearbyAction.Sit;
            }
            return NearbyAction.None;
#else
            nearbyDoor = null;
            if (sandbox && (sandbox.IsDriving || sandbox.IsTravelling || sandbox.IsResting)) return NearbyAction.None;
            if (seated) return NearbyAction.None;
            if (sandbox && sandbox.IsLookingAtNpc(viewCamera)) return NearbyAction.None;
            NearbyAction best = NearbyAction.None;
            float closest = float.PositiveInfinity;
            if (televisionTarget && CanReach(televisionTarget.position))
            {
                closest = (televisionTarget.position - viewCamera.transform.position).sqrMagnitude;
                best = NearbyAction.Television;
            }
            if (recordPlayerTarget && CanReach(recordPlayerTarget.position))
            {
                float distance = (recordPlayerTarget.position - viewCamera.transform.position).sqrMagnitude;
                if (distance < closest) { closest = distance; best = NearbyAction.Records; }
            }
            if (doors != null) foreach (SlopCityDoor door in doors)
            {
                if (!door || !door.isActiveAndEnabled || !CanReach(door.InteractionPoint, door)) continue;
                float distance = (door.InteractionPoint - viewCamera.transform.position).sqrMagnitude;
                if (distance <= closest) { closest = distance; nearbyDoor = door; best = NearbyAction.Door; }
            }
            if (best != NearbyAction.None) return best;
            return !IsOutside && (standingFeetAnchor.position - transform.position).sqrMagnitude <= couchDistance * couchDistance
                && HasLineOfSight(standingFeetAnchor.position + Vector3.up * standingEyeHeight)
                ? NearbyAction.Sit : NearbyAction.None;
#endif
        }

#if (UNITY_WEBGL && !UNITY_EDITOR) || SLOP_CITY_WEB_INTERACTION_TEST
        private void HandleBrowserInteraction()
        {
            if (seated) { StandUp(); return; }
            if (!sandbox) return;
            // Force a new query on E; a HUD refresh must never leave an old target selected.
            sandbox.BrowserTarget(viewCamera, true);
            NearbyAction action = FindNearbyAction();
            if (action != NearbyAction.None) PerformInteraction(action);
            else if (!sandbox.Interact(viewCamera)) ShowFeedback("Aim at an object within 3.4 metres to interact.");
        }
#endif

        private bool CanReach(Vector3 point, SlopCityDoor door = null)
        {
            Vector3 direction = point - viewCamera.transform.position;
            if (direction.sqrMagnitude > interactionDistance * interactionDistance) return false;
            return (direction.sqrMagnitude < 0.01f || Vector3.Dot(viewCamera.transform.forward, direction.normalized) > 0.40f)
                && HasLineOfSight(point, door);
        }

        private bool HasLineOfSight(Vector3 point, SlopCityDoor door = null)
        {
            Vector3 offset = point - viewCamera.transform.position;
            float distance = offset.magnitude;
            if (distance < 0.05f) return true;
            foreach (RaycastHit hit in Physics.RaycastAll(viewCamera.transform.position, offset / distance,
                distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                if (door && (hit.collider == door.Blocker || (door.Leaf && hit.transform.IsChildOf(door.Leaf)))) continue;
                // Media anchors sit just inside their physical props. Doors require
                // an unobstructed path right up to their own moving leaf.
                if (hit.distance < distance - (door ? 0.02f : 0.25f)) return false;
            }
            return true;
        }

        private void PerformInteraction(NearbyAction action)
        {
            switch (action)
            {
                case NearbyAction.Sit: RespawnAtCouch(); break;
                case NearbyAction.Door:
                    if (nearbyDoor && !nearbyDoor.Toggle(transform))
                        ShowFeedback(nearbyDoor.IsMoving ? "The door is moving." : "Step clear of the door's swing, then try again.");
                    break;
                case NearbyAction.Television:
                    inspectedBroadcast = true;
                    ShowMediaControls();
                    break;
                case NearbyAction.Records:
                    PlayMedia(SlopCityMediaChannel.Record);
                    ShowFeedback("RECORD PLAYER / IN-Q-TEL. Use the living-room TV to choose media.");
                    break;
            }
        }



        private void ShowMediaControls()
        {
            sourceOpen = true;
            SetPaused(true);
            if (hud) hud.ShowSourceTitleOnly();
        }

        private void ShowLocalReport(string title, string report)
        {
            // Capture evidence before this callback, then suspend the ordinary
            // player/world/media while the existing paged reader is visible.
            Vector3 position = transform.position;
            string engine = "RUNNING WORLD / SNAPSHOT\n"
                + "District: " + CurrentDistrictName + "\n"
                + string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Player: ({0:0.0}, {1:0.0}, {2:0.0}) metres; frame {3}\n", position.x, position.y, position.z, Time.frameCount)
                + (viewCamera ? "Renderer: " + viewCamera.actualRenderingPath + "; city "
                    + viewCamera.pixelWidth + " × " + viewCamera.pixelHeight + "\n" : "Renderer unavailable\n")
                + "Unity tracked allocated / reserved: "
                + (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)) + " / "
                + (UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024 * 1024))
                + " MiB. These are not total browser or device memory.\n\n";
            sourceOpen = false;
            SetPaused(true);
            if (hud) hud.ShowLocalReport(title, engine + report);
        }

        public void PlayMedia(SlopCityMediaChannel channel)
        {
            selectedChannel = channel;
            if (media) media.Play(channel);
        }

        public void ToggleMedia()
        {
            if (!media) return;
            if (media.HasSelection) media.TogglePause();
            else media.Play(selectedChannel);
        }

        public void StopMedia() { if (media) media.Stop(); }
        public void ChangeMediaVolume(float delta)
        {
            if (media) media.SetVolume(media.Volume + delta);
            if (cityMedia && media) cityMedia.SetVolume(media.Volume);
        }
        public void OpenCreatorFeed(int profile) { if (cityMedia) cityMedia.OpenCreatorFeed(profile); }
        public void ToggleCityMedia() { if (cityMedia) cityMedia.TogglePaused(); }

        private void RefreshMediaSuspension()
        {
            if (media) media.SetSuspended((paused && !sourceOpen) || !appFocused || appSuspended);
            if (cityMedia) cityMedia.SetSuspended((paused && !sourceOpen) || !appFocused || appSuspended);
        }

        private void UpdateCityProgress()
        {
            currentDistrictName = IsOutside ? "CITY STREETS" : "HOME / DANIEL'S LOUNGE";
            if (!IsOutside || districts == null) return;
            foreach (SlopCityDistrict district in districts)
            {
                if (!district) continue;
                Vector3 offset = transform.position - district.Center;
                if (!district.ContainsStreetPoint(transform.position) || Mathf.Abs(offset.y) > 4f) continue;
                currentDistrictName = district.DisplayName;
                int index = district.Row * 3 + district.Column;
                if (index >= 0 && index < visitedBlocks.Length && !visitedBlocks[index])
                {
                    visitedBlocks[index] = true;
                    VisitedBlockCount++;
                    ShowFeedback(VisitedBlockCount == 9 ? "ALL NINE ROUTES MAPPED / Every district is connected."
                        : "NOW ENTERING / " + district.DisplayName + " / " + VisitedBlockCount + " / ROUTE MAPPED");
                }
                return;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void RefreshBrowserHud()
        {
            if (Time.unscaledTime < nextBrowserHudRefresh) return;
            nextBrowserHudRefresh = Time.unscaledTime + .1f;
            RefreshHud();
        }
#endif

        private void RefreshHud()
        {
            if (!hud) return;
            string key = playerInput.UseTouchControls ? "TAP USE" : "[E]";
            string prompt;
            if (!string.IsNullOrEmpty(feedback) && Time.unscaledTime < feedbackUntil) prompt = feedback;
            else if (seated) prompt = key + " GET UP FROM THE COUCH";
            else
            {
                switch (FindNearbyAction())
                {
                    case NearbyAction.Television: prompt = key + " CHOOSE LIVING-ROOM MEDIA"; break;
                    case NearbyAction.Records: prompt = key + " PLAY IN-Q-TEL RAP"; break;
                    case NearbyAction.Door: prompt = nearbyDoor.IsMoving ? nearbyDoor.DisplayName + " / MOVING"
                        : key + (nearbyDoor.IsOpen ? " CLOSE " : " OPEN ") + nearbyDoor.DisplayName; break;
                    case NearbyAction.Sit: prompt = key + " SIT BACK DOWN"; break;
                    default:
                        string interaction = sandbox ? sandbox.Prompt(viewCamera) : "";
                        prompt = !string.IsNullOrEmpty(interaction) ? key + " " + interaction
                            : "FIND A WOMAN IN RED / USE FOR A SIGNAL";
                        break;
                }
            }
            string controls = playerInput.UseTouchControls
                ? "LEFT THUMB  MOVE     RIGHT SIDE  LOOK     USE  INTERACT     HOLD RUN  SPRINT"
                : "WASD MOVE   SHIFT RUN   SPACE JUMP   E USE   Q NEW JOB   R HOME   ESC PAUSE";
            hud.Render(seated ? "DANIEL / SEATED AT HOME" : "DANIEL / " + CurrentDistrictName, prompt, controls, !paused);
            if (sandbox) hud.RenderSandbox(sandbox, viewCamera, paused);
            else hud.SetObjectives(stoodUp, inspectedBroadcast, returnedHome);
            hud.RenderMedia(media);
            hud.RenderCity(this);
        }

        public void ShowFeedback(string text)
        {
            feedback = text; feedbackUntil = Time.unscaledTime + 4.5f;
#if UNITY_WEBGL && !UNITY_EDITOR
            nextBrowserHudRefresh = 0;
#endif
        }
        private static void UnlockCursor() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        private void OnApplicationFocus(bool focused)
        {
            appFocused = focused;
            if (!focused && playerInput && started)
            {
                SetPaused(true);
                if (sourceOpen && hud) hud.ShowSourceTitleOnly();
            }
            RefreshMediaSuspension();
        }
        private void OnApplicationPause(bool suspended)
        {
            appSuspended = suspended;
            if (suspended && playerInput && started)
            {
                SetPaused(true);
                if (sourceOpen && hud) hud.ShowSourceTitleOnly();
            }
            RefreshMediaSuspension();
        }
        private void OnDestroy()
        {
            if (crowd) crowd.LocalAgentReportRequested -= ShowLocalReport;
            if (sandbox)
            {
                sandbox.TeleportRequested -= TeleportTo;
                sandbox.MovementOverrideRequested -= SetExternalMovement;
                sandbox.MunicipalReportRequested -= ShowLocalReport;
            }
        }
        private void OnDisable() { if (media) media.SetSuspended(true); Time.timeScale = 1f; UnlockCursor(); }
    }
}
