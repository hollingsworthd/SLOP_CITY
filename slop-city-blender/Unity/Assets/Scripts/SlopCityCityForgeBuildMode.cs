using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SlopCity
{
    /// <summary>Public top-down City Forge editor for bounded grid plans.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityForgeBuildMode : MonoBehaviour
    {
        public enum View { Streets, Ground, Lots, Buildings, Services, Life }

        [SerializeField] private SlopCityCityForgeController controller;
        [SerializeField] private Camera buildCamera;
        [SerializeField] private MonoBehaviour previewSinkComponent;
        [SerializeField] private KeyCode toggleKey = KeyCode.B;
        [SerializeField, Range(40f, 280f)] private float topDownHeight = 130f;
        [SerializeField] private bool editingEnabled = true;
        private const int CityForgeCanvasSortingOrder = 300;
        private Canvas canvas;
        private Canvas launcherCanvas;
        private Text heading, message, contextHint;
        private Text launcherStatus;
        private Button undoButton, redoButton;
        private Button launcherButton;
        private Button groundActionButton, roadButton, removeRoadButton, roadDirectionButton, zoneButton, kitButton, facadeButton, roofButton, storefrontButton, entranceButton, floorsButton, interiorButton, serviceButton, landmarkButton, situationButton, situationStateButton, mountButton, hostButton, targetButton, deviceButton;
        private readonly Dictionary<View, Button> viewButtons = new Dictionary<View, Button>();
        private readonly Dictionary<View, List<GameObject>> viewControls = new Dictionary<View, List<GameObject>>();
        private CityForgeCoord selected;
        private CityForgeChunkPlan draft;
        private SlopCityCityForgeDraftHistory history;
        private View view;
        private bool opened;
        // Hosted saves are authoritative but must not eject a player from
        // their plan between adjacent ground, road, lot, and building edits.
        // While a bridge verifies one save, retain the editor and make its
        // controls deliberately inert until the acknowledgement arrives.
        private bool autosaveInFlight;
        private CityForgeRoadClass roadClass = CityForgeRoadClass.LocalStreet;
        private CityForgeDirection roadDirection = CityForgeDirection.North;
        private CityForgeZone lotZone = CityForgeZone.Home;
        private bool removeRoad;
        // Ground is deliberately a non-destructive tool by default. Clearing
        // island cells is a distinct choice so one more click cannot make a
        // supported lot or street look like a failed ground placement.
        private bool removeGround;
        private string buildingBlueprint = "walk-up", facade = "wet-masonry", roof = "service-roof", storefront = "none", interior = "";
        private string serviceType = "care-station", landmarkClass = "", landmarkOutcome = "", situationTemplate = "", situationState = "available", screenMount = "interior-wall-tv", screenDevice = "public-crt-01";
        private int screenHostMode;
        private string screenHostId = string.Empty;
        private int buildingFloors = 2;
        private CityForgeDirection entrance = CityForgeDirection.North;
        // Returning from the map after laying ground should put the player on
        // the accepted island, rather than back on the entry deck wondering
        // whether the autosave worked. Hosted worlds promote this only after
        // the authoritative response; offline worlds promote it immediately
        // after their durable local write.
        private bool hasPendingGroundPlacement;
        private CityForgeCoord pendingGroundCoordinate;
        private int pendingGroundCell = -1;
        private bool hasRecentGroundPlacement;
        private CityForgeCoord recentGroundCoordinate;
        private int recentGroundCell = -1;
        private readonly HashSet<int> lotFootprint = new HashSet<int>();
        // A boundary connection is one player gesture but two authoritative
        // plans. Keep that small atomic set separate from the selected canvas
        // block so it can be previewed, undone, and committed together.
        private readonly Dictionary<CityForgeCoord, CityForgeChunkPlan> atomicDrafts = new Dictionary<CityForgeCoord, CityForgeChunkPlan>();
        private readonly Dictionary<CityForgeCoord, CityForgeChunkPlan> atomicBaselines = new Dictionary<CityForgeCoord, CityForgeChunkPlan>();
        private readonly Stack<Dictionary<CityForgeCoord, CityForgeChunkPlan>> atomicUndo = new Stack<Dictionary<CityForgeCoord, CityForgeChunkPlan>>();
        private readonly Stack<Dictionary<CityForgeCoord, CityForgeChunkPlan>> atomicRedo = new Stack<Dictionary<CityForgeCoord, CityForgeChunkPlan>>();
        private CameraState originalCamera;
        private ISlopCityCityForgeDraftPreviewSink previewSink;
        private ISlopCityCityForgeBuildOverlaySink buildOverlaySink;
        private ISlopCityCityForgeStrategicMapSink strategicMap;
        private ISlopCityCityForgeStrategicSummarySink strategicSummaryMap;
        private CityForgeCoord pendingNavigation;
        private bool hasPendingNavigation;
        private SlopCityFirstPerson firstPerson;
        private SlopCityHud hud;
        private SlopCitySalvageHud salvageHud;
        private bool firstPersonWasEnabled;
        private bool hudSuppressed;
        private Vector2 lastTouchPosition;
        private float lastPinchDistance;
        private bool touchPanned;
        private bool pendingOpenWhenReady;
        private bool launcherReadinessKnown;
        private bool launcherReady;
        private bool launcherUnavailable;
        private string launcherUnavailableMessage;
        private bool cursorStateCaptured;
        private CursorLockMode savedCursorLockState;
        private bool savedCursorVisible;
        private bool launcherCursorStateKnown;
        private bool launcherCursorLocked;
        private bool launcherMousePressedInside;
        private int launcherMouseActivationSequence;
        private bool launcherMouseGestureConsumed;
        private int launcherTouchFingerId = -1;
        private Vector2 launcherTouchStart;
        private bool launcherTouchCanceled;
        private int launcherTouchActivationSequence;
        private bool launcherTouchGestureConsumed;
        private int observedLauncherPointerActivationSequence;
        private int lastLauncherRequestFrame = -1;
        private RectTransform buildUiSurface;
        private readonly List<Button> buildUiButtons = new List<Button>();
        private Button buildUiMouseCapture;
        private bool buildUiMouseSurfaceCapture;
        private int buildUiMousePressFrame = -1;
        // IMGUI receives native desktop mouse events on platforms where a
        // legacy StandaloneInputModule can miss a short click. Keep its
        // capture separate so it can safely converge with UGUI/raw input.
        private Button buildUiGuiMouseCapture;
        private bool buildUiGuiMouseSurfaceCapture;
        private int buildUiGuiMousePressFrame = -1;
        private Button buildUiTouchCapture;
        private int buildUiTouchFingerId = -1;
        private int buildUiTouchPressFrame = -1;
        private Vector2 buildUiTouchStart;
        private bool buildUiTouchCanceled;
        private bool buildUiTouchReleasedThisFrame;
        // A touch which began on the toolbar owns the whole multi-touch
        // gesture. This prevents its remaining finger from becoming a map
        // gesture after the toolbar finger is released.
        private bool suppressBuildUiTouchUntilNoTouches;
        private const float LauncherTapSlopPixels = 18f;
        // A short desktop click can move several backing pixels before the
        // release, especially on a Retina trackpad. Treat it as a map click
        // unless it travels a deliberate, comfortably visible distance.
        private const float MapDragThresholdPixels = 16f;
        private bool mapMousePressed;
        private bool mapMouseDragged;
        private Vector2 mapMouseStart;
        private Vector2 mapMousePrevious;
        // macOS can drop a short legacy mouse gesture and can report the
        // legacy Input position in an unrelated backing-pixel space. Keep
        // native IMGUI timing and its Canvas-mapped position together so the
        // visible toolbar and map cells receive the gesture the player made.
        private bool mapGuiMousePressed;
        private bool mapGuiMouseDragged;
        private Vector2 mapGuiMouseStart;
        private Vector2 mapGuiMousePrevious;
        private Vector2 mapGuiPointer;
        private bool hasMapGuiPointer;
        // Resolve the grid cell at press time and retain it through release.
        // This avoids a backing-scale/window move between down and up turning
        // a ground click into a different block or a dropped map gesture.
        private bool hasCapturedMapCell;
        private CityForgeCoord capturedMapCoordinate;
        private int capturedMapCellX;
        private int capturedMapCellZ;
        private bool hasHoveredCell;
        private CityForgeCoord hoveredCell;
        private static readonly string[] BuildingBlueprintOptions =
        {
            "walk-up", "courtyard-home", "tower-residence", "live-work-block", "workshop", "repair-bay", "clinic", "laundry", "depot", "small-office",
            "corner-shop", "market-hall", "cafe", "exchange", "kiosk", "care-station", "library-room", "utility-office", "community-hall", "screening-room",
            "studio", "venue", "archive", "performance-space", "garden", "plaza", "water-node", "power-relay", "shelter", "service-yard"
        };

        public bool IsOpen { get { return opened; } }
        public bool EditingEnabled { get { return editingEnabled; } }
        public bool AutosaveInFlight { get { return autosaveInFlight; } }
        public View ActiveView { get { return view; } }
        public CityForgeCoord SelectedChunk { get { return selected; } }
        public CityForgeChunkPlan Draft { get { return draft == null ? null : draft.Clone(); } }
        /// <summary>Only the plans materially changed from their loaded baseline.</summary>
        public IReadOnlyList<CityForgeChunkPlan> AtomicDrafts { get { return PlansForCommit(); } }
        public bool HasPendingNavigation { get { return hasPendingNavigation; } }
        public CityForgeCoord PendingNavigation { get { return pendingNavigation; } }
        public event Action<CityForgeChunkPlan> PreviewRequested;
        /// <summary>Raised with a clone after an accepted local edit, undo, or redo; never merely on opening a block.</summary>
        public event Action<CityForgeChunkPlan> DraftChanged;
        public event Action<CityForgeResolvedChunk> DraftPreviewCompiled;
        public event Action<IReadOnlyList<CityForgeChunkPlan>> CommitRequested;
        public event Action<bool> BuildModeChanged;
        /// <summary>Raised on close so public/offline streaming can return to the walker's neighborhood without a topology reload.</summary>
        public event Action<CityForgeCoord> PlayerNeighbourhoodRequested;
        /// <summary>Bridge hook: return false until the requested center and all radius-one neighbors are loaded and verified.</summary>
        public event Func<CityForgeCoord, bool> EnsureChunkLoaded;
        /// <summary>Bridge hook: return false until the durable draft for this verified chunk has been hydrated.</summary>
        public event Func<CityForgeCoord, bool> EnsureDraftLoaded;
        /// <summary>Bridge hook for a reciprocal boundary peer; it must not navigate or replace the selected center.</summary>
        public event Func<CityForgeCoord, bool> EnsureAtomicNeighbourLoaded;
        public event Action<CityForgeCoord> ChunkNavigationRequested;

        private void Awake()
        {
            if (!controller) controller = GetComponent<SlopCityCityForgeController>();
            if (!buildCamera) buildCamera = Camera.main;
            previewSink = previewSinkComponent as ISlopCityCityForgeDraftPreviewSink ?? FindPreviewSink();
            buildOverlaySink = previewSinkComponent as ISlopCityCityForgeBuildOverlaySink ?? FindBuildOverlaySink();
            strategicMap = FindStrategicMap();
            strategicSummaryMap = strategicMap as ISlopCityCityForgeStrategicSummarySink;
            if (controller) controller.StrategicMapChanged += HandleStrategicMapChanged;
            CreateUi(); CreateLauncher(); SetUi(false); RefreshLauncher();
        }

        private void Update()
        {
            RefreshLauncherReadiness();
            RefreshLauncherCursorMode();
            bool openedFromPendingRequest = false;
            if (!opened && pendingOpenWhenReady && editingEnabled && controller && controller.IsInitialized)
            {
                pendingOpenWhenReady = false;
                openedFromPendingRequest = Open();
            }
            if (!openedFromPendingRequest && editingEnabled && !autosaveInFlight && Input.GetKeyDown(toggleKey)) { if (opened) Close(); else RequestLauncherOpen(); }
            if (!opened) UpdateLauncherPointerActivation();
            if (!opened) return;
            if (autosaveInFlight)
            {
                // Keep the plan on screen while a hosted bridge verifies its
                // authoritative write. Do not let a stale mouse up, a mode
                // key, or a map tap mutate a second draft meanwhile.
                ResetBuildUiPointerActivation();
                ResetNativeMapPointer();
                mapMousePressed = false;
                mapMouseDragged = false;
                return;
            }
            bool mouseStartedOverBuildUi = UpdateBuildUiPointerActivation();
            if (!opened) return;
            UpdateMapMousePointer(mouseStartedOverBuildUi);
            MoveCamera();
            UpdateHoveredCell();
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SelectView(View.Streets);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SelectView(View.Ground);
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SelectView(View.Lots);
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) SelectView(View.Buildings);
            if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) SelectView(View.Services);
            if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6)) SelectView(View.Life);
            if (Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) Undo();
            if (Input.GetKeyDown(KeyCode.Y) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) Redo();
            if (Input.touchCount == 1 && !suppressBuildUiTouchUntilNoTouches && buildUiTouchFingerId < 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Ended)
                {
                    // As with mouse input, only the editor's visible toolbar
                    // blocks a map gesture. A hidden or unrelated UI
                    // raycaster must not turn a valid cell tap into a no-op.
                    if (!buildUiTouchReleasedThisFrame && !touchPanned && !BuildUiSurfaceContains(touch.position)) ClickMap(touch.position);
                    touchPanned = false;
                }
            }
        }

        private void OnGUI()
        {
            Event current = Event.current;
            if (current == null) return;
            EventType type = current.type == EventType.Used ? current.rawType : current.type;
            if (type == EventType.MouseMove && opened && UsesNativeMacMapPointer() && canvas && canvas.gameObject.activeInHierarchy)
            {
                mapGuiPointer = GuiPointerToCanvasScreenPoint(current, canvas);
                hasMapGuiPointer = true;
                return;
            }
            if (current.button != 0) return;
            if (type != EventType.MouseDown && type != EventType.MouseDrag && type != EventType.MouseUp) return;
            // On Retina and mixed-DPI Mac desktops Input.mousePosition can
            // stay in a stale backing-pixel space while the player window has
            // moved. The IMGUI event is the pointer the player actually used;
            // translate it into the overlay/camera pixel rectangle once and
            // carry that coordinate through the whole gesture.
            Vector2 screenPosition = UsesNativeMacMapPointer()
                ? GuiPointerToCanvasScreenPoint(current, opened ? canvas : launcherCanvas)
                : Input.mousePosition;

            if (!opened)
            {
                if (type == EventType.MouseDown && launcherCanvas && launcherCanvas.gameObject.activeInHierarchy && launcherButton && launcherButton.isActiveAndEnabled && launcherButton.interactable && LauncherContains(screenPosition))
                {
                    current.Use();
                    RequestLauncherOpen();
                }
                return;
            }
            if (!canvas || !canvas.gameObject.activeInHierarchy) return;
            if (autosaveInFlight)
            {
                current.Use();
                return;
            }

            if (UsesNativeMacMapPointer())
            {
                HandleNativeMacMapPointer(current, type, screenPosition);
                return;
            }

            if (type == EventType.MouseDown)
            {
                buildUiGuiMouseSurfaceCapture = BuildUiSurfaceContains(screenPosition);
                buildUiGuiMouseCapture = buildUiGuiMouseSurfaceCapture ? BuildUiActivatableButtonAt(screenPosition) : null;
                buildUiGuiMousePressFrame = Time.frameCount;
                if (buildUiGuiMouseSurfaceCapture)
                {
                    // Update can run before this raw desktop event. Cancel a
                    // mistaken map capture before its matching MouseUp would
                    // otherwise lay ground behind the toolbar control.
                    mapMousePressed = false;
                    mapMouseDragged = false;
                    current.Use();
                }
                return;
            }

            if (!buildUiGuiMouseSurfaceCapture) return;
            Button capture = buildUiGuiMouseCapture;
            bool activate = capture && BuildUiActivatableButtonAt(screenPosition) == capture && !BuildUiButtonActivatedOnOrAfter(capture, buildUiGuiMousePressFrame);
            buildUiGuiMouseCapture = null;
            buildUiGuiMouseSurfaceCapture = false;
            buildUiGuiMousePressFrame = -1;
            current.Use();
            if (activate) InvokeBuildUiFallback(capture, PointerInputModule.kMouseLeftId);
        }

        private static bool UsesNativeMacMapPointer()
        {
            return Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor;
        }

        /// <summary>
        /// Converts the native top-left IMGUI event to the lower-left pixel
        /// coordinates used by both a ScreenSpaceOverlay Canvas and a Camera.
        /// AlignRectToDevice is available in macOS players (unlike the
        /// editor-only pixelsPerPoint API), and handles Retina/per-display
        /// scaling without assuming a fixed 2x backing store.
        /// </summary>
        private static Vector2 GuiPointerToCanvasScreenPoint(Event current, Canvas targetCanvas)
        {
            Rect pixelRect = targetCanvas ? targetCanvas.pixelRect : new Rect(0f, 0f, Screen.width, Screen.height);
            if (pixelRect.width <= 0f || pixelRect.height <= 0f) pixelRect = new Rect(0f, 0f, Screen.width, Screen.height);
            const float probePoints = 256f;
            GUIUtility.AlignRectToDevice(new Rect(0f, 0f, probePoints, probePoints), out int probePixelsX, out int probePixelsY);
            float pixelsPerPointX = probePixelsX > 0 ? probePixelsX / probePoints : 1f;
            float pixelsPerPointY = probePixelsY > 0 ? probePixelsY / probePoints : 1f;
            return new Vector2(pixelRect.x + current.mousePosition.x * pixelsPerPointX,
                pixelRect.y + pixelRect.height - current.mousePosition.y * pixelsPerPointY);
        }

        /// <summary>Owns reliable Mac press/release timing in the same
        /// Canvas-mapped pixel space used by the map camera.</summary>
        private void HandleNativeMacMapPointer(Event current, EventType type, Vector2 screenPosition)
        {
            mapGuiPointer = screenPosition;
            hasMapGuiPointer = true;

            if (type == EventType.MouseDown)
            {
                ResolveNativeMacBuildUiHit(screenPosition, out buildUiGuiMouseSurfaceCapture, out buildUiGuiMouseCapture);
                buildUiGuiMousePressFrame = Time.frameCount;

                // Suppress the legacy gesture so its MouseUp cannot lay or
                // select a different cell behind this native-pixel gesture.
                mapMousePressed = false;
                mapMouseDragged = false;
                mapGuiMousePressed = !buildUiGuiMouseSurfaceCapture;
                mapGuiMouseDragged = false;
                mapGuiMouseStart = screenPosition;
                mapGuiMousePrevious = screenPosition;
                if (mapGuiMousePressed) CaptureMapCell(screenPosition);
                else ClearCapturedMapCell();
                current.Use();
                return;
            }

            if (type == EventType.MouseDrag)
            {
                if (!mapGuiMousePressed) return;
                if (!mapGuiMouseDragged && Vector2.Distance(screenPosition, mapGuiMouseStart) >= MapDragThresholdPixels) mapGuiMouseDragged = true;
                if (mapGuiMouseDragged) ClearCapturedMapCell();
                if (mapGuiMouseDragged) PanMap(screenPosition - mapGuiMousePrevious, buildCamera ? buildCamera.pixelHeight : Screen.height);
                mapGuiMousePrevious = screenPosition;
                current.Use();
                return;
            }

            if (buildUiGuiMouseSurfaceCapture)
            {
                Button capture = buildUiGuiMouseCapture;
                ResolveNativeMacBuildUiHit(screenPosition, out bool releasedOnSurface, out Button releasedOnButton);
                bool activate = releasedOnSurface && capture && releasedOnButton == capture && !BuildUiButtonActivatedOnOrAfter(capture, buildUiGuiMousePressFrame);
                buildUiGuiMouseCapture = null;
                buildUiGuiMouseSurfaceCapture = false;
                buildUiGuiMousePressFrame = -1;
                current.Use();
                if (activate) InvokeBuildUiFallback(capture, PointerInputModule.kMouseLeftId);
                return;
            }

            if (mapGuiMousePressed && !mapGuiMouseDragged && Vector2.Distance(screenPosition, mapGuiMouseStart) >= MapDragThresholdPixels) mapGuiMouseDragged = true;
            if (mapGuiMouseDragged) ClearCapturedMapCell();
            bool clickMap = mapGuiMousePressed && !mapGuiMouseDragged;
            mapGuiMousePressed = false;
            mapGuiMouseDragged = false;
            current.Use();
            if (clickMap) ClickCapturedMapCell(screenPosition);
            else ClearCapturedMapCell();
        }

        public bool Open()
        {
            if (opened) return true;
            if (!editingEnabled || launcherUnavailable)
            {
                SetLauncherStatus(launcherUnavailable ? launcherUnavailableMessage : "City Forge is not available in this city.");
                return false;
            }
            if (!controller || !controller.IsInitialized)
            {
                // A click during startup is a real player request, not a
                // discarded input. The offline composition fulfils it as
                // soon as it has installed its bounded starter ring.
                pendingOpenWhenReady = true;
                SetLauncherStatus("City Forge is preparing your starter ring…");
                return false;
            }
            if (!firstPerson) firstPerson = FindFirstObjectByType<SlopCityFirstPerson>();
            if (firstPerson && firstPerson.InventoryOpen)
            {
                SetLauncherStatus("Close your inventory before entering City Forge.");
                return false;
            }
            selected = new CityForgeCoord(2, 0);
            if (!EnsureLoaded(selected)) return false;
            if (!EnsureDraft(selected)) return false;
            try { draft = controller.BeginDraft(selected); }
            catch (Exception error) { SetMessage(error.Message); return false; }
            pendingOpenWhenReady = false;
            ResetAtomicDrafts(draft); history = new SlopCityCityForgeDraftHistory(draft); opened = true; view = View.Ground;
            ResumeFirstPersonForBuildMode(); SetHudSuppressed(true); GateFirstPerson(true); CaptureCamera(); controller.SetBuildMode(true); controller.StreamAt(selected); ShowStrategicMap(); SetUi(true); SetLauncher(false); RefreshUi(); RefreshBuildOverlay(); SetMessage("Sky Sprawl blocks are open air. Use Ground to lay island cells before you build."); BuildModeChanged?.Invoke(true); return true;
        }

        public void Close()
        {
            if (!opened) return;
            opened = false; ResetBuildUiPointerActivation(); ResetNativeMapPointer(); mapMousePressed = false; mapMouseDragged = false; hasHoveredCell = false; previewSink?.ClearCityForgeDraftPreview(); buildOverlaySink?.ClearCityForgeBuildOverlay(); strategicMap?.ClearCityForgeStrategicMap(); if (controller && controller.IsInitialized) controller.SetBuildMode(false); RestoreCamera(); GateFirstPerson(false); SetHudSuppressed(false); SetUi(false); SetLauncher(editingEnabled);
            SlopCityFirstPerson player = FindFirstObjectByType<SlopCityFirstPerson>();
            if (player)
            {
                PlacePlayerOnRecentIsland(player);
                PlayerNeighbourhoodRequested?.Invoke(new CityForgeCoord(Mathf.FloorToInt(player.transform.position.x / SlopCityCityForge.ChunkSizeMeters), Mathf.FloorToInt(player.transform.position.z / SlopCityCityForge.ChunkSizeMeters)));
            }
            BuildModeChanged?.Invoke(false);
        }

        public bool SelectChunk(CityForgeCoord coordinate)
        {
            if (!opened || !controller) return false;
            if (!EnsureLoaded(coordinate)) return false;
            if (!controller.CanDraft(coordinate)) { SetMessage("Survey an adjoining block first."); return false; }
            if (!EnsureDraft(coordinate)) return false;
            try { draft = controller.BeginDraft(coordinate); }
            catch (Exception error) { SetMessage(error.Message); return false; }
            selected = coordinate; hasHoveredCell = false; ResetAtomicDrafts(draft); history = new SlopCityCityForgeDraftHistory(draft); controller.StreamAt(selected); ShowStrategicMap(); FocusCamera(); RefreshUi(); RefreshBuildOverlay(); return true;
        }

        public void SelectView(View next)
        {
            if (autosaveInFlight) { SetMessage("Saving City Forge…"); return; }
            view = next;
            RefreshUi();
            RefreshBuildOverlay();
            SetMessage(ViewHint(next));
        }
        public bool TrySetGround(int cellX, int cellZ, bool remove)
        {
            if (draft == null || cellX < 0 || cellX > 8 || cellZ < 0 || cellZ > 8) return false;
            int cell = cellX + cellZ * SlopCityCityForge.GridSize;
            CityForgeChunkPlan next = draft.Clone();
            if (next.groundCells == null) next.groundCells = new List<int>();
            bool hasGround = next.groundCells.Contains(cell);
            if (remove)
            {
                if (!hasGround) { SetMessage("This cell is already open sky. Switch to Lay ground to make an island."); return false; }
                if ((next.parcels ?? new List<CityForgeParcel>()).Any(parcel => parcel != null && (parcel.cells ?? new List<int>()).Contains(cell)))
                {
                    SetMessage("Remove the lot before clearing the island ground beneath it.");
                    return false;
                }
                if ((next.roads ?? new List<CityForgeRoad>()).Any(road => SlopCityCityForge.RoadTouchesCell(road, cell)))
                {
                    SetMessage("Remove the street before clearing island ground beneath it.");
                    return false;
                }
                next.groundCells.Remove(cell);
            }
            else
            {
                if (hasGround) { SetMessage("Island ground is already laid. Switch to Erase ground to clear it."); return false; }
                next.groundCells.Add(cell);
            }
            next.groundCells.Sort();
            if (!remove) QueueGroundPlacement(selected, cell);
            string success = remove
                ? "Island ground cleared."
                : HasAdjacentGroundPair(new HashSet<int>(next.groundCells))
                    ? "Island ground laid. Next: Streets — click either touching cell to make their shared street."
                    : "Island ground laid. Next: lay one touching cell before adding a street or building.";
            bool applied = Apply(next, success);
            if (!applied && !remove) ClearPendingGroundPlacement(selected, cell);
            if (applied && remove) ClearRecentGroundPlacement(selected, cell);
            return applied;
        }

        public bool TrySetRoad(int cellX, int cellZ, CityForgeDirection direction, CityForgeRoadClass roadClass, bool remove)
        {
            if (draft == null || cellX < 0 || cellX > 8 || cellZ < 0 || cellZ > 8) return false;
            CanonicalRoad(ref cellX, ref cellZ, ref direction);
            CityForgeRoad road = new CityForgeRoad { cellX = cellX, cellZ = cellZ, direction = direction, roadClass = roadClass };
            if (!remove && !SlopCityCityForge.RoadHasIslandGround(draft, road))
            {
                SetMessage("Lay island ground under both sides of a street before placing it.");
                return false;
            }
            CityForgeChunkPlan next = draft.Clone();
            SetRoad(next, cellX, cellZ, direction, roadClass, remove);
            if (!road.IsBoundary) return Apply(next, remove ? "Street removal planned." : "Street planned. Next: Lots — click a street-touching ground cell.");

            CityForgeCoord neighbour = SlopCityCityForge.Neighbour(selected, direction);
            if (SlopCityCityForge.IsProtectedOldCity(neighbour)) { SetMessage("The Old City boundary cannot be changed."); return false; }
            if (!EnsureAtomicNeighbour(neighbour) || !EnsureDraft(neighbour)) return false;
            if (!TryGetAtomicDraft(neighbour, out CityForgeChunkPlan reciprocal)) return false;
            ReciprocalRoad(cellX, cellZ, direction, out int reciprocalX, out int reciprocalZ, out CityForgeDirection reciprocalDirection);
            if (!remove && !SlopCityCityForge.RoadHasIslandGround(reciprocal, new CityForgeRoad { cellX = reciprocalX, cellZ = reciprocalZ, direction = reciprocalDirection, roadClass = roadClass }))
            {
                SetMessage("Lay island ground on the neighboring block before joining this street.");
                return false;
            }
            SetRoad(reciprocal, reciprocalX, reciprocalZ, reciprocalDirection, roadClass, remove);
            return ApplyAtomic(new Dictionary<CityForgeCoord, CityForgeChunkPlan>
            {
                { next.coord, next }, { reciprocal.coord, reciprocal }
            }, remove ? "Street removal planned on both sides of the boundary." : "Street connection planned. Next: Lots — click a street-touching ground cell.");
        }
        public bool TryPlaceLot(IEnumerable<int> cells, CityForgeZone zone, string parcelId = null)
        {
            if (draft == null) return false; List<int> selectedCells = (cells ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (selectedCells.Count == 0 || selectedCells.Count > 81 || selectedCells.Any(item => item < 0 || item >= 81) || (draft.parcels ?? new List<CityForgeParcel>()).Any(parcel => parcel != null && parcel.cells != null && parcel.cells.Intersect(selectedCells).Any())) return false;
            HashSet<int> ground = new HashSet<int>(draft.groundCells ?? new List<int>());
            if (selectedCells.Any(cell => !ground.Contains(cell))) { SetMessage("Lay island ground under every lot before zoning it."); return false; }
            CityForgeChunkPlan next = draft.Clone();
            CityForgeParcel parcel = new CityForgeParcel { id = string.IsNullOrEmpty(parcelId) ? NextId("parcel") : parcelId, zone = zone, cells = selectedCells, entranceEdge = CityForgeDirection.North };
            next.parcels.Add(parcel);
            return Apply(next, HasStreetFrontage(parcel, null)
                ? "Lot planned. Next: Buildings — choose a design, then click this lot."
                : "Lot planned. Add a street beside this lot before placing a building.");
        }
        public bool TryPlaceBuilding(string parcelId, string blueprintId, string facadeId, string roofId, int floors, CityForgeDirection entrance, string storefrontId = null, string interiorRole = null)
        {
            if (draft == null || (draft.buildings ?? new List<CityForgeBuilding>()).Any(item => item != null && item.parcelId == parcelId)) return false;
            CityForgeParcel parcel = (draft.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && item.id == parcelId);
            if (parcel == null) { SetMessage("Add a lot first."); return false; }
            CityForgeDirection resolvedEntrance = entrance;
            if (!HasStreetFrontage(parcel, resolvedEntrance) && !TryFindStreetFrontage(parcel, out resolvedEntrance))
            {
                SetMessage("This lot needs a street touching it before a building can be placed.");
                return false;
            }
            this.entrance = resolvedEntrance;
            CityForgeChunkPlan next = draft.Clone(); next.buildings.Add(new CityForgeBuilding { id = NextId("building"), parcelId = parcelId, blueprintId = blueprintId, facadeId = facadeId, roofId = roofId, floors = floors, entranceEdge = resolvedEntrance, storefrontId = storefrontId ?? string.Empty, interiorRole = interiorRole ?? string.Empty }); return Apply(next, "Building placed facing the " + DirectionLabel(resolvedEntrance).ToLowerInvariant() + " street. Add services or keep designing.");
        }
        public bool TryPlaceService(string parcelId, string type)
        {
            if (draft == null) return false;
            CityForgeParcel parcel = (draft.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && item.id == parcelId);
            if (parcel == null) { SetMessage("Add a lot first."); return false; }
            if (!HasStreetFrontage(parcel, null)) { SetMessage("Lay a street beside this lot before adding a service."); return false; }
            CityForgeChunkPlan next = draft.Clone(); next.services.Add(new CityForgeService { id = NextId("service"), parcelId = parcelId, type = type }); return Apply(next, "Service planned.");
        }
        public bool TryPlaceScreen(string hostId, string mountId, string deviceId)
        {
            if (draft == null) return false; CityForgeChunkPlan next = draft.Clone(); next.screens.Add(new CityForgeScreen { screenId = NextId("screen"), hostId = hostId, screenMountId = mountId, deviceId = deviceId }); return Apply(next, "Physical screen fixture planned.");
        }
        public bool TryDemolish(string structureId)
        {
            if (draft == null || string.IsNullOrEmpty(structureId)) return false; CityForgeChunkPlan next = draft.Clone();
            next.buildings.RemoveAll(item => item != null && item.id == structureId); next.services.RemoveAll(item => item != null && item.id == structureId); next.screens.RemoveAll(item => item != null && item.hostId == structureId);
            if (!next.demolishedIds.Contains(structureId)) next.demolishedIds.Add(structureId); return Apply(next, "Demolition planned.");
        }
        public bool TrySetLandmarkState(string landmarkId, string state)
        {
            if (draft == null) return false; CityForgeChunkPlan next = draft.Clone(); CityForgeLandmark item = next.landmarks.FirstOrDefault(value => value != null && value.id == landmarkId); if (item == null) return false; item.state = state; return Apply(next, "Cityworks lifecycle updated.");
        }
        public bool TryPlaceSituation(string landmarkId, string templateId, string state = "available", string stableSeed = null)
        {
            if (draft == null || string.IsNullOrEmpty(landmarkId) || string.IsNullOrEmpty(templateId)
                || (draft.situations ?? new List<CityForgeSituationState>()).Any(item => item != null && item.landmarkId == landmarkId && item.situationId == templateId)) return false;
            CityForgeChunkPlan next = draft.Clone();
            next.situations.Add(new CityForgeSituationState { id = NextId("situation"), landmarkId = landmarkId, situationId = templateId, state = state, stableSeed = string.IsNullOrEmpty(stableSeed) ? Seed("situation") : stableSeed });
            return Apply(next, "CitySituation opportunity planned.");
        }
        public bool TrySetSituationState(string situationId, string state)
        {
            if (draft == null || string.IsNullOrEmpty(situationId)) return false;
            CityForgeChunkPlan next = draft.Clone(); CityForgeSituationState item = next.situations.FirstOrDefault(value => value != null && value.id == situationId);
            if (item == null) return false; item.state = state; return Apply(next, "CitySituation lifecycle updated.");
        }
        public void Undo()
        {
            if (autosaveInFlight) { SetMessage("Saving City Forge…"); return; }
            if (atomicUndo.Count == 0) return;
            Dictionary<CityForgeCoord, CityForgeChunkPlan> restore = atomicUndo.Pop();
            atomicRedo.Push(CloneDraftMap(atomicDrafts));
            RestoreAtomicDrafts(restore); AutoSavePendingEdits("Saved undo.");
        }

        public void Redo()
        {
            if (autosaveInFlight) { SetMessage("Saving City Forge…"); return; }
            if (atomicRedo.Count == 0) return;
            Dictionary<CityForgeCoord, CityForgeChunkPlan> restore = atomicRedo.Pop();
            atomicUndo.Push(CloneDraftMap(atomicDrafts));
            RestoreAtomicDrafts(restore); AutoSavePendingEdits("Saved redo.");
        }

        public void Preview()
        {
            if (autosaveInFlight) { SetMessage("Saving City Forge…"); return; }
            if (draft == null) return;
            List<CityForgeChunkPlan> plans = PlansForCommit();
            if (!controller.TryResolveDraftPreview(plans, selected, out CityForgeResolvedChunk preview, out CityForgeValidation validation)) { SetMessage(validation.Messages.FirstOrDefault() ?? "Check this plan first."); return; }
            // The planning surface already renders the exact draft in its
            // intended block. Rendering a second opaque physical chunk above
            // the accepted one made Review look like a failed duplicate world,
            // rather than an honest preflight. Keep the compiled result for
            // integrators, but make Review a clear validation checkpoint.
            previewSink?.ClearCityForgeDraftPreview();
            PreviewRequested?.Invoke(draft.Clone()); DraftPreviewCompiled?.Invoke(preview);
            RefreshBuildOverlay();
            SetMessage("Draft checks pass. Changes save automatically.");
        }

        private bool AutoSavePendingEdits(string success)
        {
            List<CityForgeChunkPlan> plans = PlansForCommit();
            if (plans.Count == 0) { SetMessage("Saved."); RefreshUi(); RefreshBuildOverlay(); return true; }
            if (!controller.ValidateDraftOnly(plans, out CityForgeValidation validation))
                return RestoreDurableOfflineDraft(validation.Messages.FirstOrDefault() ?? "This change cannot be saved.");
            if (CommitRequested != null)
            {
                SetAutosaveInFlight(true);
                NotifyDraftsChanged(plans);
                CommitRequested(plans.Select(item => item.Clone()).ToList());
                if (autosaveInFlight) SetMessage("Saving City Forge…");
                return true;
            }
            // The player is safely gated in the top-down editor, so update the
            // nearby presentation immediately instead of teleporting them for
            // every individual map click.
            if (!controller.TryCommit(plans, true, false, out validation))
                return RestoreDurableOfflineDraft(validation.Messages.FirstOrDefault() ?? "This change could not be saved.");
            foreach (CityForgeChunkPlan plan in plans)
            {
                if (!controller.TryGetAccepted(plan.coord, out CityForgeChunkPlan accepted)) continue;
                atomicBaselines[plan.coord] = accepted.Clone();
                atomicDrafts[plan.coord] = controller.BeginDraft(plan.coord);
            }
            if (atomicDrafts.TryGetValue(selected, out CityForgeChunkPlan current)) draft = current.Clone();
            history = new SlopCityCityForgeDraftHistory(draft);
            ConfirmPendingGroundPlacement();
            controller.StreamAt(selected);
            ShowStrategicMap(); SetMessage(success); RefreshUi(); RefreshBuildOverlay(); return true;
        }

        /// <summary>
        /// Offline edits are autosaves, not a separate pending/commit state.
        /// If validation or PlayerPrefs rejects one, put the editor back on the
        /// accepted city immediately so its overlay cannot masquerade as a
        /// persistent change. Hosted bridges keep their own acknowledged draft
        /// lifecycle and do not take this path.
        /// </summary>
        private bool RestoreDurableOfflineDraft(string failure)
        {
            if (controller == null || CommitRequested != null) { SetMessage(failure); return false; }
            foreach (CityForgeCoord coordinate in atomicDrafts.Keys.ToList()) controller.DiscardPrivateDraft(coordinate);
            try
            {
                CityForgeChunkPlan restored = controller.BeginDraft(selected);
                draft = restored.Clone();
                ResetAtomicDrafts(draft);
                history = new SlopCityCityForgeDraftHistory(draft);
                controller.StreamAt(selected);
                ShowStrategicMap();
                RefreshUi();
                RefreshBuildOverlay();
                SetMessage("Not saved — restored the last saved city. " + failure);
            }
            catch (Exception error)
            {
                SetMessage("Not saved. " + (string.IsNullOrEmpty(failure) ? error.Message : failure));
            }
            return false;
        }

        /// <summary>Called by a hosted bridge only after its authoritative D1 response is verified.</summary>
        public bool ApplyAuthoritativeCommit(CityForgeWorldHead head, IEnumerable<CityForgeChunkPlan> plans, out CityForgeValidation validation)
        {
            if (!controller.ApplyAuthoritativeCommit(head, plans, out validation)) { SetMessage(validation.Messages.FirstOrDefault() ?? "The hosted save was refused."); return false; }
            AcknowledgeAuthoritativeCommit();
            SetMessage("Saved to the City Forge world.");
            return true;
        }

        /// <summary>
        /// Called after an external owner bridge has installed a verified
        /// accepted page through its own controller path. It deliberately does
        /// not apply a second mutation; it only reconciles the editor's
        /// autosave acknowledgement and presentation with that accepted data.
        /// </summary>
        public void AcknowledgeAuthoritativeCommit()
        {
            if (opened && controller != null && controller.TryGetAccepted(selected, out CityForgeChunkPlan accepted))
            {
                draft = accepted;
                ResetAtomicDrafts(draft);
                history = new SlopCityCityForgeDraftHistory(draft);
            }
            ConfirmPendingGroundPlacement();
            ShowStrategicMap(); RefreshUi(); RefreshBuildOverlay();
        }

        public void SetStatusMessage(string value) { SetMessage(value); }

        /// <summary>
        /// Lets an authoritative owner bridge hold this editor open while one
        /// autosave is being verified. This deliberately does not alter edit
        /// permission or close the map: the player keeps their plan in view
        /// and receives a clear saving state instead of being sent back to
        /// first person after every tile.
        /// </summary>
        public void SetAutosaveInFlight(bool value)
        {
            if (autosaveInFlight == value) return;
            autosaveInFlight = value;
            ResetBuildUiPointerActivation();
            ResetNativeMapPointer();
            mapMousePressed = false;
            mapMouseDragged = false;
            if (autosaveInFlight) SetMessage("Saving City Forge…");
            RefreshUi();
        }

        /// <summary>Lets an offline setup or a hosted owner bridge expose this editor only when the player may edit.</summary>
        public void SetEditingEnabled(bool value)
        {
            if (editingEnabled == value) return;
            editingEnabled = value;
            if (!editingEnabled && opened) Close();
            if (!editingEnabled) pendingOpenWhenReady = false;
            SetLauncher(editingEnabled && !opened);
        }

        /// <summary>Shows a clear recovery state instead of a launch control which can never succeed.</summary>
        public void SetLaunchUnavailable(string value)
        {
            launcherUnavailable = true;
            launcherUnavailableMessage = string.IsNullOrEmpty(value) ? "City Forge needs recovery before this device can build." : value;
            pendingOpenWhenReady = false;
            if (opened) Close();
            editingEnabled = false;
            SetLauncher(true);
        }

        private bool EnsureLoaded(CityForgeCoord coordinate, bool navigation = true)
        {
            pendingNavigation = coordinate;
            hasPendingNavigation = true;
            if (navigation) ChunkNavigationRequested?.Invoke(coordinate);
            if (EnsureChunkLoaded == null) { hasPendingNavigation = false; return true; }
            foreach (Delegate callback in EnsureChunkLoaded.GetInvocationList())
                if (!((Func<CityForgeCoord, bool>)callback)(coordinate)) { SetMessage("Loading this City Forge neighborhood…"); return false; }
            hasPendingNavigation = false;
            return true;
        }

        private bool EnsureDraft(CityForgeCoord coordinate)
        {
            if (EnsureDraftLoaded == null) return true;
            foreach (Delegate callback in EnsureDraftLoaded.GetInvocationList())
                if (!((Func<CityForgeCoord, bool>)callback)(coordinate)) { SetMessage("Loading your saved City Forge draft…"); return false; }
            return true;
        }

        private bool EnsureAtomicNeighbour(CityForgeCoord coordinate)
        {
            if (EnsureAtomicNeighbourLoaded == null) return EnsureLoaded(coordinate, false);
            foreach (Delegate callback in EnsureAtomicNeighbourLoaded.GetInvocationList())
                if (!((Func<CityForgeCoord, bool>)callback)(coordinate)) { SetMessage("Loading the matching City Forge boundary block…"); return false; }
            return true;
        }

        private void ResetAtomicDrafts(CityForgeChunkPlan initial)
        {
            atomicDrafts.Clear(); atomicBaselines.Clear(); atomicUndo.Clear(); atomicRedo.Clear();
            if (initial == null) return;
            atomicDrafts[initial.coord] = initial.Clone();
            atomicBaselines[initial.coord] = BaselineFor(initial);
        }

        private CityForgeChunkPlan BaselineFor(CityForgeChunkPlan plan)
        {
            if (controller.TryGetAccepted(plan.coord, out CityForgeChunkPlan accepted)) return accepted;
            CityForgeChunkPlan baseline = plan.Clone();
            baseline.roads = new List<CityForgeRoad>(); baseline.removedRoadEdges = new List<string>(); baseline.parcels = new List<CityForgeParcel>();
            baseline.buildings = new List<CityForgeBuilding>(); baseline.services = new List<CityForgeService>(); baseline.screens = new List<CityForgeScreen>();
            baseline.landmarks = new List<CityForgeLandmark>(); baseline.situations = new List<CityForgeSituationState>(); baseline.demolishedIds = new List<string>(); baseline.packedBlobHash = string.Empty;
            return baseline;
        }

        private bool TryGetAtomicDraft(CityForgeCoord coordinate, out CityForgeChunkPlan result)
        {
            if (atomicDrafts.TryGetValue(coordinate, out CityForgeChunkPlan current)) { result = current.Clone(); return true; }
            if (atomicDrafts.Count >= 4) { SetMessage("A City Forge boundary commit can change at most four blocks."); result = null; return false; }
            if (!controller.CanDraft(coordinate)) { SetMessage("Survey an adjoining accepted block first."); result = null; return false; }
            try { result = controller.BeginDraft(coordinate); }
            catch (Exception error) { SetMessage(error.Message); result = null; return false; }
            atomicDrafts[coordinate] = result.Clone(); atomicBaselines[coordinate] = BaselineFor(result); return true;
        }

        private static Dictionary<CityForgeCoord, CityForgeChunkPlan> CloneDraftMap(IDictionary<CityForgeCoord, CityForgeChunkPlan> source)
        {
            Dictionary<CityForgeCoord, CityForgeChunkPlan> clone = new Dictionary<CityForgeCoord, CityForgeChunkPlan>();
            foreach (KeyValuePair<CityForgeCoord, CityForgeChunkPlan> pair in source) clone[pair.Key] = pair.Value == null ? null : pair.Value.Clone();
            return clone;
        }

        private void PushAtomicUndo()
        {
            atomicUndo.Push(CloneDraftMap(atomicDrafts)); atomicRedo.Clear();
            if (atomicUndo.Count <= 64) return;
            Dictionary<CityForgeCoord, CityForgeChunkPlan>[] snapshots = atomicUndo.Reverse().Skip(1).Reverse().ToArray();
            atomicUndo.Clear(); foreach (Dictionary<CityForgeCoord, CityForgeChunkPlan> snapshot in snapshots.Reverse()) atomicUndo.Push(snapshot);
        }

        private void RestoreAtomicDrafts(Dictionary<CityForgeCoord, CityForgeChunkPlan> restore)
        {
            atomicDrafts.Clear();
            foreach (KeyValuePair<CityForgeCoord, CityForgeChunkPlan> pair in restore)
            {
                CityForgeChunkPlan value = pair.Value.Clone();
                NormalizeRoads(value);
                atomicDrafts[pair.Key] = value;
                if (controller.TryGetAccepted(pair.Key, out CityForgeChunkPlan accepted))
                {
                    // Undo/redo creates a new immutable revision, never a
                    // stale copy of an earlier one. Preserve the authored
                    // shape while binding it to the newest accepted parent.
                    value.baseChunkHash = accepted.baseChunkHash;
                    value.parentChunkRevision = accepted.revision;
                    value.revision = 0;
                    value.revisionId = string.Empty;
                    value.packedBlobHash = string.Empty;
                    atomicDrafts[pair.Key] = value;
                    atomicBaselines[pair.Key] = accepted;
                }
                else if (!atomicBaselines.ContainsKey(pair.Key)) atomicBaselines[pair.Key] = BaselineFor(value);
                controller.TrySavePrivateDraft(value, out string ignored);
            }
            if (atomicDrafts.TryGetValue(selected, out CityForgeChunkPlan current)) draft = current.Clone();
            history = new SlopCityCityForgeDraftHistory(draft);
        }

        private List<CityForgeChunkPlan> PlansForCommit()
        {
            List<CityForgeChunkPlan> plans = new List<CityForgeChunkPlan>();
            foreach (KeyValuePair<CityForgeCoord, CityForgeChunkPlan> pair in atomicDrafts.OrderBy(item => item.Key))
            {
                if (!atomicBaselines.TryGetValue(pair.Key, out CityForgeChunkPlan baseline)
                    || !string.Equals(SlopCityCityForgeCanonical.Pack(pair.Value), SlopCityCityForgeCanonical.Pack(baseline), StringComparison.Ordinal)) plans.Add(pair.Value.Clone());
            }
            return plans;
        }

        private bool ApplyAtomic(IDictionary<CityForgeCoord, CityForgeChunkPlan> updates, string success)
        {
            if (updates == null || updates.Count == 0 || updates.Count > 4) return false;
            foreach (CityForgeChunkPlan value in updates.Values)
                if (value == null || !controller.CanDraft(value.coord)) { SetMessage("Survey an adjoining accepted block before planning there."); return false; }
            Dictionary<CityForgeCoord, CityForgeChunkPlan> prepared = new Dictionary<CityForgeCoord, CityForgeChunkPlan>();
            foreach (KeyValuePair<CityForgeCoord, CityForgeChunkPlan> pair in updates)
            {
                CityForgeChunkPlan value = pair.Value.Clone();
                NormalizeRoads(value);
                prepared[pair.Key] = value;
            }
            // Validate the complete candidate before allowing any private-draft
            // write. Previously a malformed road gesture could be written,
            // then fail autosave and restore the whole visible city—making a
            // perfectly good ground tile appear unreliable or lost.
            Dictionary<CityForgeCoord, CityForgeChunkPlan> candidate = CloneDraftMap(atomicDrafts);
            foreach (KeyValuePair<CityForgeCoord, CityForgeChunkPlan> pair in prepared) candidate[pair.Key] = pair.Value.Clone();
            foreach (CityForgeChunkPlan value in candidate.Values) NormalizeRoads(value);
            if (!controller.ValidateDraftOnly(candidate.Values, out CityForgeValidation validation))
            {
                SetMessage(validation.Messages.FirstOrDefault() ?? "That change is not a valid City Forge plan.");
                return false;
            }
            foreach (CityForgeChunkPlan value in candidate.Values)
                if (!controller.TrySavePrivateDraft(value, out string error)) { SetMessage(error ?? "That edit is unavailable."); return false; }
            PushAtomicUndo();
            atomicDrafts.Clear();
            foreach (KeyValuePair<CityForgeCoord, CityForgeChunkPlan> pair in candidate)
            {
                atomicDrafts[pair.Key] = pair.Value.Clone();
                if (!atomicBaselines.ContainsKey(pair.Key)) atomicBaselines[pair.Key] = BaselineFor(pair.Value);
            }
            if (atomicDrafts.TryGetValue(selected, out CityForgeChunkPlan current)) draft = current.Clone();
            history = new SlopCityCityForgeDraftHistory(draft); RefreshUi(); RefreshBuildOverlay(); return AutoSavePendingEdits(success);
        }

        private void NotifyDraftsChanged(IEnumerable<CityForgeChunkPlan> values = null)
        {
            if (DraftChanged == null) return;
            foreach (CityForgeChunkPlan value in (values ?? atomicDrafts.Values).Where(item => item != null).OrderBy(item => item.coord)) DraftChanged(value.Clone());
        }

        private static void CanonicalRoad(ref int x, ref int z, ref CityForgeDirection direction)
        {
            if (direction == CityForgeDirection.West && x > 0) { x--; direction = CityForgeDirection.East; }
            if (direction == CityForgeDirection.South && z > 0) { z--; direction = CityForgeDirection.North; }
        }

        private static void NormalizeRoads(CityForgeChunkPlan plan)
        {
            if (plan == null) return;
            Dictionary<string, CityForgeRoad> uniqueRoads = new Dictionary<string, CityForgeRoad>(StringComparer.Ordinal);
            foreach (CityForgeRoad road in plan.roads ?? new List<CityForgeRoad>())
            {
                if (road == null) continue;
                if (road.cellX < 0 || road.cellX >= SlopCityCityForge.GridSize || road.cellZ < 0 || road.cellZ >= SlopCityCityForge.GridSize
                    || !Enum.IsDefined(typeof(CityForgeDirection), road.direction))
                {
                    uniqueRoads[road.LocalKey] = road;
                    continue;
                }
                int x = road.cellX;
                int z = road.cellZ;
                CityForgeDirection direction = road.direction;
                CanonicalRoad(ref x, ref z, ref direction);
                road.cellX = x;
                road.cellZ = z;
                road.direction = direction;
                uniqueRoads[CanonicalEdge(x, z, direction)] = road;
            }
            plan.roads = uniqueRoads.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Value).ToList();
            HashSet<string> roadEdges = new HashSet<string>(uniqueRoads.Keys, StringComparer.Ordinal);
            List<string> normalizedRemovals = new List<string>();
            HashSet<string> seenRemovals = new HashSet<string>(StringComparer.Ordinal);
            foreach (string edge in plan.removedRoadEdges ?? new List<string>())
            {
                string canonical = CanonicalizeRoadEdge(edge);
                if (string.IsNullOrEmpty(canonical) || roadEdges.Contains(canonical) || !seenRemovals.Add(canonical)) continue;
                normalizedRemovals.Add(canonical);
            }
            plan.removedRoadEdges = normalizedRemovals;
        }

        private static string CanonicalizeRoadEdge(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 9 || value[1] != ',' || value[3] != ':'
                || value[0] < '0' || value[0] > '8' || value[2] < '0' || value[2] > '8') return string.Empty;
            CityForgeDirection direction;
            string side = value.Substring(4);
            if (side == "north") direction = CityForgeDirection.North;
            else if (side == "east") direction = CityForgeDirection.East;
            else if (side == "south") direction = CityForgeDirection.South;
            else if (side == "west") direction = CityForgeDirection.West;
            else return string.Empty;
            return CanonicalEdge(value[0] - '0', value[2] - '0', direction);
        }

        private static void ReciprocalRoad(int x, int z, CityForgeDirection direction, out int reciprocalX, out int reciprocalZ, out CityForgeDirection reciprocalDirection)
        {
            reciprocalDirection = SlopCityCityForge.Opposite(direction); reciprocalX = x; reciprocalZ = z;
            if (direction == CityForgeDirection.East) reciprocalX = 0;
            else if (direction == CityForgeDirection.West) reciprocalX = SlopCityCityForge.GridSize - 1;
            else if (direction == CityForgeDirection.North) reciprocalZ = 0;
            else reciprocalZ = SlopCityCityForge.GridSize - 1;
        }

        private static void SetRoad(CityForgeChunkPlan plan, int x, int z, CityForgeDirection direction, CityForgeRoadClass roadClass, bool remove)
        {
            if (plan.roads == null) plan.roads = new List<CityForgeRoad>();
            if (plan.removedRoadEdges == null) plan.removedRoadEdges = new List<string>();
            string edge = CanonicalEdge(x, z, direction);
            plan.roads.RemoveAll(item => item != null && CanonicalEdge(item.cellX, item.cellZ, item.direction) == edge);
            plan.removedRoadEdges.RemoveAll(item => item == edge);
            if (remove) plan.removedRoadEdges.Add(edge);
            else plan.roads.Add(new CityForgeRoad { cellX = x, cellZ = z, direction = direction, roadClass = roadClass });
        }

        private ISlopCityCityForgeDraftPreviewSink FindPreviewSink()
        {
            foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
            {
                ISlopCityCityForgeDraftPreviewSink candidate = component as ISlopCityCityForgeDraftPreviewSink;
                if (candidate != null) return candidate;
            }
            return null;
        }

        private ISlopCityCityForgeBuildOverlaySink FindBuildOverlaySink()
        {
            foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
            {
                ISlopCityCityForgeBuildOverlaySink candidate = component as ISlopCityCityForgeBuildOverlaySink;
                if (candidate != null) return candidate;
            }
            return null;
        }

        private void RefreshBuildOverlay()
        {
            if (!opened || buildOverlaySink == null || controller == null || draft == null) return;
            bool valid = controller.ValidateDraftOnly(atomicDrafts.Values, out CityForgeValidation ignored);
            CityForgeCoord? hover = hasHoveredCell ? hoveredCell : (CityForgeCoord?)null;
            buildOverlaySink.ShowCityForgeBuildOverlay(controller.WorldHead, selected, draft, hover, roadDirection, view, valid);
        }

        private ISlopCityCityForgeStrategicMapSink FindStrategicMap()
        {
            foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
            {
                ISlopCityCityForgeStrategicMapSink candidate = component as ISlopCityCityForgeStrategicMapSink;
                if (candidate != null) return candidate;
            }
            return null;
        }

        private void ShowStrategicMap()
        {
            if (controller == null || strategicMap == null) return;
            if (strategicSummaryMap != null)
                strategicSummaryMap.ShowCityForgeStrategicMap(controller.WorldHead, selected, 6, controller.AcceptedChunks, controller.StrategicChunks);
            else strategicMap.ShowCityForgeStrategicMap(controller.WorldHead, selected, 6, controller.AcceptedChunks);
        }

        private void HandleStrategicMapChanged()
        {
            if (opened) ShowStrategicMap();
        }

        private void UpdateMapMousePointer(bool mouseStartedOverBuildUi)
        {
            if (UsesNativeMacMapPointer() && Input.touchCount == 0)
            {
                // The IMGUI path owns macOS desktop gestures in the map
                // camera's native backing-pixel space. Touch remains on the
                // normal path below.
                mapMousePressed = false;
                mapMouseDragged = false;
                return;
            }
            if (Input.touchCount > 0)
            {
                mapMousePressed = false;
                mapMouseDragged = false;
                return;
            }
            if (Input.GetMouseButtonDown(0))
            {
                // The build canvas deliberately owns a full-screen Canvas so
                // the map can be drawn beneath the toolbar. Do not ask the
                // global EventSystem whether the pointer is over *any* UI:
                // another HUD (or the canvas root itself) may report true
                // even when the player pressed a clear map cell. The editor
                // has an explicit toolbar hit surface, which is the only
                // thing allowed to claim this gesture.
                mapMousePressed = !mouseStartedOverBuildUi;
                mapMouseDragged = false;
                mapMouseStart = Input.mousePosition;
                mapMousePrevious = mapMouseStart;
                if (mapMousePressed) CaptureMapCell(mapMouseStart);
                else ClearCapturedMapCell();
            }
            if (mapMousePressed && Input.GetMouseButton(0))
            {
                Vector2 current = Input.mousePosition;
                if (!mapMouseDragged && Vector2.Distance(current, mapMouseStart) >= MapDragThresholdPixels) mapMouseDragged = true;
                if (mapMouseDragged) ClearCapturedMapCell();
                if (mapMouseDragged) PanMap(current - mapMousePrevious, Screen.height);
                mapMousePrevious = current;
            }
            if (!Input.GetMouseButtonUp(0)) return;
            bool activate = mapMousePressed && !mapMouseDragged;
            mapMousePressed = false;
            mapMouseDragged = false;
            if (activate) ClickCapturedMapCell(Input.mousePosition);
            else ClearCapturedMapCell();
        }

        private void UpdateHoveredCell()
        {
            bool nextHasHover = false;
            CityForgeCoord nextHover = default(CityForgeCoord);
            Vector2 pointer = UsesNativeMacMapPointer() && hasMapGuiPointer ? mapGuiPointer : Input.mousePosition;
            bool pointerDragging = UsesNativeMacMapPointer() ? mapGuiMouseDragged : mapMouseDragged;
            if (!BuildUiSurfaceContains(pointer) && !pointerDragging && TryGetMapCell(pointer, out CityForgeCoord coordinate, out int x, out int z) && coordinate.Equals(selected))
            {
                nextHasHover = true;
                nextHover = new CityForgeCoord(x, z);
            }
            if (nextHasHover == hasHoveredCell && (!nextHasHover || nextHover.Equals(hoveredCell))) return;
            hasHoveredCell = nextHasHover;
            hoveredCell = nextHover;
            RefreshBuildOverlay();
        }

        private void PanMap(Vector2 movement, float pixelHeight)
        {
            if (!buildCamera) return;
            float metresPerPixel = topDownHeight / Mathf.Max(240f, pixelHeight);
            buildCamera.transform.position -= new Vector3(movement.x * metresPerPixel, 0f, movement.y * metresPerPixel);
        }

        private bool TryGetMapCell(Vector3 screenPosition, out CityForgeCoord coordinate, out int x, out int z)
        {
            coordinate = default(CityForgeCoord); x = z = 0;
            if (!buildCamera) return false;
            Ray ray = buildCamera.ScreenPointToRay(screenPosition);
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance)) return false;
            Vector3 point = ray.GetPoint(distance);
            coordinate = new CityForgeCoord(Mathf.FloorToInt(point.x / SlopCityCityForge.ChunkSizeMeters), Mathf.FloorToInt(point.z / SlopCityCityForge.ChunkSizeMeters));
            x = Mathf.Clamp(Mathf.FloorToInt((point.x - coordinate.x * SlopCityCityForge.ChunkSizeMeters) / SlopCityCityForge.CellSizeMeters), 0, 8);
            z = Mathf.Clamp(Mathf.FloorToInt((point.z - coordinate.z * SlopCityCityForge.ChunkSizeMeters) / SlopCityCityForge.CellSizeMeters), 0, 8);
            return true;
        }

        private void CaptureMapCell(Vector2 screenPosition)
        {
            hasCapturedMapCell = TryGetMapCell(screenPosition, out capturedMapCoordinate, out capturedMapCellX, out capturedMapCellZ);
        }

        private void ClearCapturedMapCell()
        {
            hasCapturedMapCell = false;
            capturedMapCoordinate = default(CityForgeCoord);
            capturedMapCellX = 0;
            capturedMapCellZ = 0;
        }

        private void ClickCapturedMapCell(Vector2 fallbackScreenPosition)
        {
            if (!hasCapturedMapCell)
            {
                ClickMap(fallbackScreenPosition);
                return;
            }
            CityForgeCoord coordinate = capturedMapCoordinate;
            int x = capturedMapCellX;
            int z = capturedMapCellZ;
            ClearCapturedMapCell();
            ClickMapCell(coordinate, x, z);
        }

        private void ClickMap(Vector3 screenPosition)
        {
            if (autosaveInFlight) { SetMessage("Saving City Forge…"); return; }
            if (!TryGetMapCell(screenPosition, out CityForgeCoord coord, out int x, out int z)) return;
            ClickMapCell(coord, x, z);
        }

        private void ClickMapCell(CityForgeCoord coordinate, int x, int z)
        {
            if (autosaveInFlight) { SetMessage("Saving City Forge…"); return; }
            if (!coordinate.Equals(selected))
            {
                if (view == View.Ground)
                {
                    SetMessage("Click inside the highlighted block to lay ground. Select another block from Streets, Lots, Buildings, Services, or Life.");
                    return;
                }
                SelectChunk(coordinate);
                return;
            }
            if (view == View.Streets) ToggleStreet(x, z);
            else if (view == View.Ground) TrySetGround(x, z, removeGround);
            else if (view == View.Lots) AddLot(x, z);
            else if (view == View.Buildings) AddBuilding(x, z);
            else if (view == View.Services) AddService(x, z);
            else AddLife(x, z);
        }

        private void ToggleStreet(int x, int z)
        {
            CityForgeDirection direction = roadDirection;
            // The first two neighbouring island cells have exactly one
            // sensible shared edge. Choose it for the player so an otherwise
            // correct starter block never fails simply because Road side was
            // still set to its previous value.
            if (!removeRoad && TryFindUniqueGroundedRoadEdge(x, z, out CityForgeDirection inferred))
            {
                direction = inferred;
                if (roadDirection != inferred)
                {
                    roadDirection = inferred;
                    RefreshUi();
                    RefreshBuildOverlay();
                }
            }
            TrySetRoad(x, z, direction, roadClass, removeRoad);
        }

        private bool TryFindUniqueGroundedRoadEdge(int x, int z, out CityForgeDirection direction)
        {
            direction = CityForgeDirection.North;
            if (draft == null || x < 0 || x >= SlopCityCityForge.GridSize || z < 0 || z >= SlopCityCityForge.GridSize) return false;
            HashSet<int> ground = new HashSet<int>(draft.groundCells ?? new List<int>());
            if (!ground.Contains(x + z * SlopCityCityForge.GridSize)) return false;
            List<CityForgeDirection> candidates = new List<CityForgeDirection>();
            if (z + 1 < SlopCityCityForge.GridSize && ground.Contains(x + (z + 1) * SlopCityCityForge.GridSize)) candidates.Add(CityForgeDirection.North);
            if (x + 1 < SlopCityCityForge.GridSize && ground.Contains(x + 1 + z * SlopCityCityForge.GridSize)) candidates.Add(CityForgeDirection.East);
            if (z > 0 && ground.Contains(x + (z - 1) * SlopCityCityForge.GridSize)) candidates.Add(CityForgeDirection.South);
            if (x > 0 && ground.Contains(x - 1 + z * SlopCityCityForge.GridSize)) candidates.Add(CityForgeDirection.West);
            if (candidates.Count != 1) return false;
            direction = candidates[0];
            return true;
        }

        private void AddLot(int x, int z)
        {
            int cell = x + z * 9;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                if (!lotFootprint.Add(cell)) lotFootprint.Remove(cell);
                SetMessage(lotFootprint.Count + " grid cells selected for a lot footprint."); return;
            }
            List<int> footprint = lotFootprint.Count == 0 ? new List<int> { cell } : lotFootprint.Concat(new[] { cell }).Distinct().ToList();
            if (TryPlaceLot(footprint, lotZone)) lotFootprint.Clear();
        }

        private void AddBuilding(int x, int z)
        {
            CityForgeParcel parcel = ParcelAt(x, z);
            if (parcel == null) { SetMessage("Add a lot first."); return; }
            if ((draft.buildings ?? new List<CityForgeBuilding>()).Any(item => item != null && item.parcelId == parcel.id)) { SetMessage("That lot already has a building."); return; }
            if (!SlopCityCityForgeCatalog.Default.IsBuildingAllowedInZone(buildingBlueprint, parcel.zone))
            {
                buildingBlueprint = BuildingBlueprintsForZone(parcel.zone).FirstOrDefault() ?? buildingBlueprint;
                RefreshUi();
            }
            TryPlaceBuilding(parcel.id, buildingBlueprint, facade, roof, buildingFloors, entrance, storefront == "none" ? null : storefront, interior);
        }

        private void AddService(int x, int z)
        {
            CityForgeParcel parcel = ParcelAt(x, z);
            if (parcel == null) { SetMessage("Add a lot first."); return; }
            TryPlaceService(parcel.id, serviceType);
        }

        private void AddCityworks(int x, int z)
        {
            CityForgeParcel parcel = ParcelAt(x, z);
            if (parcel == null) { SetMessage("Add a lot first."); return; }
            CityForgeChunkPlan next = draft.Clone();
            LandmarkChoice choice = SelectedLandmarkChoice(parcel.zone);
            if (string.IsNullOrEmpty(choice.classId) || string.IsNullOrEmpty(choice.outcomeId)) { SetMessage("No Cityworks outcome fits this lot."); return; }
            landmarkClass = choice.classId;
            landmarkOutcome = choice.outcomeId;
            RefreshUi();
            next.landmarks.Add(new CityForgeLandmark { id = NextId("landmark"), classId = choice.classId, outcomeId = choice.outcomeId, parcelId = parcel.id, state = "unavailable", stableSeed = Seed("landmark") });
            Apply(next, "Cityworks site added: " + choice.outcomeId + ".");
        }

        private void AddLife(int x, int z)
        {
            CityForgeParcel parcel = ParcelAt(x, z);
            if (parcel == null) { SetMessage("Add a Cityworks lot first."); return; }
            CityForgeLandmark landmark = (draft.landmarks ?? new List<CityForgeLandmark>()).FirstOrDefault(item => item != null && item.parcelId == parcel.id);
            if (landmark == null) { AddCityworks(x, z); return; }
            CityForgeSituationState existing = (draft.situations ?? new List<CityForgeSituationState>()).FirstOrDefault(item => item != null && item.landmarkId == landmark.id && item.situationId == situationTemplate);
            if (existing == null)
            {
                if (string.IsNullOrEmpty(situationTemplate)) CycleSituationTemplate();
                TryPlaceSituation(landmark.id, situationTemplate, situationState);
                return;
            }
            TrySetSituationState(existing.id, NextSituationState(existing.state));
        }

        private LandmarkChoice SelectedLandmarkChoice(CityForgeZone zone)
        {
            List<LandmarkChoice> choices = LandmarkChoices(zone);
            LandmarkChoice selected = choices.FirstOrDefault(item => item.classId == landmarkClass && item.outcomeId == landmarkOutcome);
            return string.IsNullOrEmpty(selected.classId) ? choices.FirstOrDefault() : selected;
        }

        private bool Apply(CityForgeChunkPlan next, string success)
        {
            return ApplyAtomic(new Dictionary<CityForgeCoord, CityForgeChunkPlan> { { next.coord, next } }, success);
        }

        private CityForgeParcel ParcelAt(int x, int z)
        {
            int cell = x + z * 9;
            return (draft.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && item.cells != null && item.cells.Contains(cell));
        }

        /// <summary>Matches the validator's frontage requirement before an edit is saved.</summary>
        private bool HasStreetFrontage(CityForgeParcel parcel, CityForgeDirection? requiredEdge)
        {
            if (parcel == null || parcel.cells == null) return false;
            foreach (int cell in parcel.cells)
            {
                int x = cell % SlopCityCityForge.GridSize;
                int z = cell / SlopCityCityForge.GridSize;
                foreach (CityForgeDirection edge in requiredEdge.HasValue
                    ? new[] { requiredEdge.Value }
                    : new[] { CityForgeDirection.North, CityForgeDirection.East, CityForgeDirection.South, CityForgeDirection.West })
                {
                    string frontage = CanonicalEdge(x, z, edge);
                    if ((draft.roads ?? new List<CityForgeRoad>()).Any(road => road != null
                        && SlopCityCityForge.RoadHasIslandGround(draft, road)
                        && CanonicalEdge(road.cellX, road.cellZ, road.direction) == frontage)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Buildings face the selected entry when it has a street. Otherwise
        /// choose a real adjoining street deterministically, so a correct
        /// ground → road → lot sequence never fails because of hidden global
        /// Entry state from an earlier placement.
        /// </summary>
        private bool TryFindStreetFrontage(CityForgeParcel parcel, out CityForgeDirection edge)
        {
            foreach (CityForgeDirection candidate in new[] { CityForgeDirection.North, CityForgeDirection.East, CityForgeDirection.South, CityForgeDirection.West })
            {
                if (!HasStreetFrontage(parcel, candidate)) continue;
                edge = candidate;
                return true;
            }
            edge = CityForgeDirection.North;
            return false;
        }

        private void QueueGroundPlacement(CityForgeCoord coordinate, int cell)
        {
            hasPendingGroundPlacement = true;
            pendingGroundCoordinate = coordinate;
            pendingGroundCell = cell;
        }

        private void ClearPendingGroundPlacement(CityForgeCoord coordinate, int cell)
        {
            if (!hasPendingGroundPlacement || !pendingGroundCoordinate.Equals(coordinate) || pendingGroundCell != cell) return;
            hasPendingGroundPlacement = false;
            pendingGroundCell = -1;
        }

        private void ClearRecentGroundPlacement(CityForgeCoord coordinate, int cell)
        {
            ClearPendingGroundPlacement(coordinate, cell);
            if (!hasRecentGroundPlacement || !recentGroundCoordinate.Equals(coordinate) || recentGroundCell != cell) return;
            hasRecentGroundPlacement = false;
            recentGroundCell = -1;
        }

        private void ConfirmPendingGroundPlacement()
        {
            if (!hasPendingGroundPlacement || controller == null) return;
            if (!controller.TryGetAccepted(pendingGroundCoordinate, out CityForgeChunkPlan accepted)) return;
            if (!(accepted.groundCells ?? new List<int>()).Contains(pendingGroundCell))
            {
                hasPendingGroundPlacement = false;
                pendingGroundCell = -1;
                return;
            }
            hasRecentGroundPlacement = true;
            recentGroundCoordinate = pendingGroundCoordinate;
            recentGroundCell = pendingGroundCell;
            hasPendingGroundPlacement = false;
            pendingGroundCell = -1;
        }

        private void PlacePlayerOnRecentIsland(SlopCityFirstPerson player)
        {
            // Opening the editor from the couch is supported, but a seated
            // player has no active walking capsule. Leave the accepted island
            // queued for a later standing return instead of stranding them.
            if (!hasRecentGroundPlacement || !player || controller == null || player.IsSeated) return;
            hasRecentGroundPlacement = false;
            if (!controller.TryGetAccepted(recentGroundCoordinate, out CityForgeChunkPlan accepted)
                || !(accepted.groundCells ?? new List<int>()).Contains(recentGroundCell)) return;

            int x = recentGroundCell % SlopCityCityForge.GridSize;
            int z = recentGroundCell / SlopCityCityForge.GridSize;
            float cell = SlopCityCityForge.CellSizeMeters;
            Vector3 center = new Vector3(recentGroundCoordinate.x * SlopCityCityForge.ChunkSizeMeters + (x + .5f) * cell,
                2f, recentGroundCoordinate.z * SlopCityCityForge.ChunkSizeMeters + (z + .5f) * cell);
            CharacterController capsule = player.GetComponent<CharacterController>();
            bool capsuleEnabled = capsule && capsule.enabled;
            if (capsule) capsule.enabled = false;

            // A bare tile uses its center. If the player placed a lot or a
            // building on that same tile before returning, choose a clear
            // corner of the tile rather than embedding the walker in a wall.
            Vector3[] offsets =
            {
                Vector3.zero,
                new Vector3(-cell * .34f, 0f, -cell * .34f), new Vector3(cell * .34f, 0f, -cell * .34f),
                new Vector3(-cell * .34f, 0f, cell * .34f), new Vector3(cell * .34f, 0f, cell * .34f)
            };
            Vector3 destination = center;
            bool hasClearStandingSpace = !capsule;
            if (capsule)
            {
                foreach (Vector3 offset in offsets)
                {
                    Vector3 candidate = center + offset;
                    Vector3 bottom = candidate + Vector3.up * (capsule.radius + .03f);
                    Vector3 top = candidate + Vector3.up * (capsule.height - capsule.radius);
                    if (Physics.CheckCapsule(bottom, top, capsule.radius * .90f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    destination = candidate;
                    hasClearStandingSpace = true;
                    break;
                }
            }
            // Do not put the capsule inside a dense multi-cell building just
            // to prove a save. The normal entry deck remains the safe refuge
            // when every point on the last tile is occupied.
            if (!hasClearStandingSpace) destination = SlopCityCityForgeTravel.StarterEntryPosition;
            if (capsule) capsule.enabled = capsuleEnabled;
            // TeleportTo restores the first-person eye relative to the player
            // as well as moving the capsule.  A raw transform assignment here
            // can leave the camera at its pre-editor world position, making a
            // real Sky Sprawl arrival appear to be inside the old city.
            player.TeleportTo(destination, player.transform.eulerAngles.y);
            if (capsule && !capsuleEnabled) capsule.enabled = false;
        }

        private string NextId(string prefix) { return NextId(draft, prefix); }

        private static string NextId(CityForgeChunkPlan source, string prefix)
        {
            HashSet<string> used = new HashSet<string>((source?.parcels ?? new List<CityForgeParcel>()).Where(item => item != null).Select(item => item.id)
                .Concat((source?.buildings ?? new List<CityForgeBuilding>()).Where(item => item != null).Select(item => item.id))
                .Concat((source?.services ?? new List<CityForgeService>()).Where(item => item != null).Select(item => item.id))
                .Concat((source?.screens ?? new List<CityForgeScreen>()).Where(item => item != null).Select(item => item.screenId))
                .Concat((source?.landmarks ?? new List<CityForgeLandmark>()).Where(item => item != null).Select(item => item.id))
                .Concat((source?.situations ?? new List<CityForgeSituationState>()).Where(item => item != null).Select(item => item.id)), StringComparer.Ordinal);
            int index = 1; string value; do { value = prefix + "-" + index; index++; } while (used.Contains(value)); return value;
        }
        private string Seed(string prefix) { return SlopCityCityForge.Sha256(prefix + "|" + selected + "|" + (draft == null ? 0 : draft.revision)).Substring(0, 16); }
        private static string CanonicalEdge(int x, int z, CityForgeDirection direction)
        {
            if (direction == CityForgeDirection.West && x > 0) { x--; direction = CityForgeDirection.East; }
            if (direction == CityForgeDirection.South && z > 0) { z--; direction = CityForgeDirection.North; }
            string side = direction == CityForgeDirection.North ? "north" : direction == CityForgeDirection.East ? "east" : direction == CityForgeDirection.South ? "south" : "west";
            return x + "," + z + ":" + side;
        }

        private void CycleRoadClass() { roadClass = (CityForgeRoadClass)(((int)roadClass + 1) % 5); RefreshUi(); RefreshBuildOverlay(); }
        private void CycleRoadDirection()
        {
            roadDirection = (CityForgeDirection)(((int)roadDirection + 1) % 4);
            RefreshUi();
            RefreshBuildOverlay();
        }
        private void ToggleRoadRemoval() { removeRoad = !removeRoad; RefreshUi(); }
        private void ToggleGroundRemoval() { removeGround = !removeGround; RefreshUi(); }
        private void CycleLotZone()
        {
            lotZone = (CityForgeZone)(((int)lotZone + 1) % 6);
            string[] preferred = { "walk-up", "workshop", "corner-shop", "care-station", "studio", "garden" }; buildingBlueprint = preferred[(int)lotZone];
            landmarkClass = landmarkOutcome = string.Empty;
            RefreshUi();
        }
        private void CycleBuildingKit()
        {
            List<string> options = BuildingBlueprintsForZone(lotZone);
            if (options.Count == 0) return;
            int index = options.IndexOf(buildingBlueprint); buildingBlueprint = options[(index + 1 + options.Count) % options.Count]; RefreshUi();
        }

        private static List<string> BuildingBlueprintsForZone(CityForgeZone zone)
        {
            return BuildingBlueprintOptions.Where(value => SlopCityCityForgeCatalog.Default.IsBuildingAllowedInZone(value, zone)).ToList();
        }
        private void CycleFacade() { string[] values = { "wet-masonry", "poured-concrete", "glazed-ceramic", "oxidized-metal", "painted-render", "brick-utility", "night-glass", "garden-lattice" }; facade = values[(Array.IndexOf(values, facade) + 1 + values.Length) % values.Length]; RefreshUi(); }
        private void CycleRoof() { string[] values = { "service-roof", "rain-garden", "solar-rack", "water-tank", "relay-array", "lantern-roof", "terrace", "greenhouse", "vent-stack", "civic-canopy", "screen-crown", "utility-bridge" }; roof = values[(Array.IndexOf(values, roof) + 1 + values.Length) % values.Length]; RefreshUi(); }
        private void CycleStorefront() { string[] values = { "none", "shutter", "awning", "glazed", "arcade", "market-stall" }; storefront = values[(Array.IndexOf(values, storefront) + 1 + values.Length) % values.Length]; RefreshUi(); }
        private void CycleInterior()
        {
            string[] values = { "", "home-room", "workshop-floor", "repair-counter", "care-room", "laundry-room", "depot-bay", "office-floor", "shop-floor", "market-floor", "cafe-room", "exchange-room", "library-room", "community-room", "screening-room", "studio-room", "venue-room", "archive-room", "performance-room", "utility-room", "shelter-room" };
            interior = values[(Array.IndexOf(values, interior) + 1 + values.Length) % values.Length]; RefreshUi();
        }
        private void CycleService()
        {
            string[] values = { "care-station", "repair-counter", "laundry-service", "delivery-bay", "neighborhood-office", "market-stall", "food-counter", "exchange-desk", "library-desk", "utility-desk", "community-room", "screening-service", "studio-service", "venue-door", "archive-desk", "garden-steward", "water-service", "power-service", "shelter-desk", "service-yard-bay" };
            serviceType = values[(Array.IndexOf(values, serviceType) + 1 + values.Length) % values.Length]; RefreshUi();
        }
        private List<LandmarkChoice> LandmarkChoices(CityForgeZone zone)
        {
            List<LandmarkChoice> result = new List<LandmarkChoice>();
            foreach (CityForgeLandmarkClass item in SlopCityCityForgeCatalog.Default.LandmarkClasses.OrderBy(value => value.id, StringComparer.Ordinal))
                if (item.compatibleZones != null && Array.IndexOf(item.compatibleZones, zone) >= 0)
                    foreach (string outcome in item.outcomes ?? Array.Empty<string>()) result.Add(new LandmarkChoice(item.id, outcome));
            return result;
        }
        private void CycleLandmarkOutcome()
        {
            List<LandmarkChoice> choices = LandmarkChoices(lotZone); if (choices.Count == 0) return;
            int current = choices.FindIndex(item => item.classId == landmarkClass && item.outcomeId == landmarkOutcome);
            LandmarkChoice next = choices[(current + 1 + choices.Count) % choices.Count]; landmarkClass = next.classId; landmarkOutcome = next.outcomeId; RefreshUi();
        }
        private void CycleSituationTemplate()
        {
            string[] choices = SlopCityCityForgeCatalog.Default.Situations.OrderBy(item => item, StringComparer.Ordinal).ToArray();
            if (choices.Length == 0) return;
            int current = Array.IndexOf(choices, situationTemplate); situationTemplate = choices[(current + 1 + choices.Length) % choices.Length]; RefreshUi();
        }
        private void CycleSituationState()
        {
            string[] states = { "available", "active", "resolved", "paused", "archived", "unavailable" };
            int current = Array.IndexOf(states, situationState); situationState = states[(current + 1 + states.Length) % states.Length]; RefreshUi();
        }
        private static string NextSituationState(string state)
        {
            if (state == "unavailable") return "available";
            if (state == "available") return "active";
            if (state == "active") return "resolved";
            if (state == "resolved") return "archived";
            if (state == "paused") return "available";
            return "archived";
        }
        private void CycleScreenMount()
        {
            string[] values = { "interior-wall-tv", "facade-media-wall", "plaza-civic-support", "street-kiosk-screen" };
            screenMount = values[(Array.IndexOf(values, screenMount) + 1 + values.Length) % values.Length]; screenHostId = string.Empty; RefreshUi();
        }
        private void CycleScreenHost() { screenHostMode = (screenHostMode + 1) % 3; screenHostId = string.Empty; RefreshUi(); }
        private void CycleScreenTarget()
        {
            List<string> choices = AvailableScreenHosts();
            if (choices.Count == 0) { SetMessage("No compatible host is available for that mount."); return; }
            int current = choices.IndexOf(screenHostId); screenHostId = choices[(current + 1 + choices.Count) % choices.Count]; RefreshUi();
        }
        private void CycleScreenDevice()
        {
            string[] values = { "public-crt-01", "public-led-01", "public-projector-01", "public-window-01" };
            screenDevice = values[(Array.IndexOf(values, screenDevice) + 1 + values.Length) % values.Length]; RefreshUi();
        }
        private void CycleEntrance() { entrance = (CityForgeDirection)(((int)entrance + 1) % 4); RefreshUi(); }
        private void ChangeFloors(int amount) { buildingFloors = Mathf.Clamp(buildingFloors + amount, 1, 24); RefreshUi(); }

        public bool TryMergeLots(IEnumerable<string> parcelIds, string mergedId = null)
        {
            if (draft == null) return false;
            HashSet<string> ids = new HashSet<string>((parcelIds ?? Enumerable.Empty<string>()).Where(item => !string.IsNullOrEmpty(item)), StringComparer.Ordinal);
            List<CityForgeParcel> parcels = (draft.parcels ?? new List<CityForgeParcel>()).Where(item => item != null && ids.Contains(item.id)).ToList();
            if (parcels.Count < 2 || parcels.Select(item => item.zone).Distinct().Count() != 1 || HasParcelDependencies(draft, ids)) { SetMessage("Merge empty lots from one zone before placing structures."); return false; }
            CityForgeChunkPlan next = draft.Clone(); next.parcels.RemoveAll(item => item != null && ids.Contains(item.id));
            next.parcels.Add(new CityForgeParcel { id = string.IsNullOrEmpty(mergedId) ? parcels.OrderBy(item => item.id, StringComparer.Ordinal).First().id : mergedId, zone = parcels[0].zone, cells = parcels.SelectMany(item => item.cells ?? new List<int>()).Distinct().OrderBy(item => item).ToList() });
            return Apply(next, "Lots merged.");
        }

        public bool TrySplitLot(string parcelId, IEnumerable<IEnumerable<int>> fragments)
        {
            if (draft == null || string.IsNullOrEmpty(parcelId)) return false;
            CityForgeParcel source = (draft.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && item.id == parcelId);
            List<List<int>> pieces = (fragments ?? Enumerable.Empty<IEnumerable<int>>()).Select(item => (item ?? Enumerable.Empty<int>()).Distinct().OrderBy(cell => cell).ToList()).Where(item => item.Count > 0).ToList();
            if (source == null || pieces.Count < 2 || HasParcelDependencies(draft, new HashSet<string>(new[] { parcelId }, StringComparer.Ordinal))
                || !new HashSet<int>(pieces.SelectMany(item => item)).SetEquals(source.cells ?? new List<int>()) || pieces.Sum(item => item.Count) != (source.cells ?? new List<int>()).Count)
            { SetMessage("Split an empty lot into non-overlapping pieces."); return false; }
            CityForgeChunkPlan next = draft.Clone(); next.parcels.RemoveAll(item => item != null && item.id == parcelId);
            for (int index = 0; index < pieces.Count; index++) next.parcels.Add(new CityForgeParcel { id = NextId(next, "parcel"), zone = source.zone, cells = pieces[index] });
            return Apply(next, "Lot split into " + pieces.Count + " pieces.");
        }

        private void MergeSelectedLots()
        {
            if (lotFootprint.Count == 0) { SetMessage("Shift-tap two or more lot cells, then merge."); return; }
            HashSet<string> ids = new HashSet<string>((draft.parcels ?? new List<CityForgeParcel>()).Where(item => item != null && item.cells != null && item.cells.Intersect(lotFootprint).Any()).Select(item => item.id), StringComparer.Ordinal);
            if (TryMergeLots(ids)) lotFootprint.Clear();
        }

        private void SplitSelectedLot()
        {
            int cell = lotFootprint.Count > 0 ? lotFootprint.First() : -1;
            CityForgeParcel parcel = cell >= 0 ? (draft.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && item.cells != null && item.cells.Contains(cell)) : null;
            if (parcel == null) { SetMessage("Shift-tap a lot cell, then split its empty lot."); return; }
            if (TrySplitLot(parcel.id, parcel.cells.Select(value => (IEnumerable<int>)new[] { value }))) lotFootprint.Clear();
        }

        private static bool HasParcelDependencies(CityForgeChunkPlan plan, ISet<string> ids)
        {
            return (plan.buildings ?? new List<CityForgeBuilding>()).Any(item => item != null && ids.Contains(item.parcelId))
                || (plan.services ?? new List<CityForgeService>()).Any(item => item != null && ids.Contains(item.parcelId))
                || (plan.landmarks ?? new List<CityForgeLandmark>()).Any(item => item != null && ids.Contains(item.parcelId));
        }
        private void PlaceScreenOnLastBuilding()
        {
            if (string.IsNullOrEmpty(screenHostId)) { SetMessage("Choose a screen target before placing a screen."); return; }
            if (!TryResolveScreenHost(out string host)) { SetMessage("Choose a compatible building, public site, or street screen host."); return; }
            int capacity = ScreenHostCapacity(host);
            int occupied = (draft.screens ?? new List<CityForgeScreen>()).Count(item => item != null && item.hostId == host);
            if (capacity <= occupied) { SetMessage("That shipped screen host has no remaining fixture positions."); return; }
            TryPlaceScreen(host, screenMount, screenDevice);
        }

        /// <summary>Chooses a catalog-derived target; callers cannot supply a transform or raw slot.</summary>
        public bool TrySelectScreenHost(string hostId)
        {
            if (string.IsNullOrEmpty(hostId) || !AvailableScreenHosts().Contains(hostId)) return false;
            screenHostId = hostId;
            RefreshUi();
            return true;
        }

        private List<string> AvailableScreenHosts()
        {
            List<string> result = new List<string>();
            if (draft == null) return result;
            if (screenHostMode == 0)
            {
                foreach (CityForgeBuilding building in (draft.buildings ?? new List<CityForgeBuilding>()).Where(item => item != null).OrderBy(item => item.id, StringComparer.Ordinal))
                    if (SlopCityCityForgeCatalog.Default.IsBuildingScreenMount(building.blueprintId, screenMount)) result.Add(building.id);
            }
            else if (screenHostMode == 1)
            {
                if (screenMount != "plaza-civic-support") return result;
                foreach (CityForgeParcel parcel in (draft.parcels ?? new List<CityForgeParcel>()).Where(item => item != null).OrderBy(item => item.id, StringComparer.Ordinal))
                    if (SlopCityCityForgeCatalog.Default.GetSiteScreenSlots(parcel.zone) > 0) result.Add("site-" + parcel.id);
            }
            else
            {
                if (screenMount != "street-kiosk-screen") return result;
                foreach (CityForgeRoad road in (draft.roads ?? new List<CityForgeRoad>()).Where(item => item != null).OrderBy(item => item.LocalKey, StringComparer.Ordinal))
                    if (SlopCityCityForgeCatalog.Default.GetRoadScreenSlots(road.roadClass) > 0) result.Add(RoadHostId(road));
            }
            return result;
        }

        private bool TryResolveScreenHost(out string host)
        {
            List<string> choices = AvailableScreenHosts();
            if (choices.Count == 0) { host = null; return false; }
            if (!choices.Contains(screenHostId)) screenHostId = choices[0];
            host = screenHostId;
            return true;
        }

        private int ScreenHostCapacity(string host)
        {
            CityForgeBuilding building = (draft.buildings ?? new List<CityForgeBuilding>()).FirstOrDefault(item => item != null && item.id == host);
            if (building != null) return SlopCityCityForgeCatalog.Default.GetBuildingScreenSlots(building.blueprintId);
            if (host != null && host.StartsWith("site-", StringComparison.Ordinal))
            {
                CityForgeParcel parcel = (draft.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && "site-" + item.id == host);
                return parcel == null ? 0 : SlopCityCityForgeCatalog.Default.GetSiteScreenSlots(parcel.zone);
            }
            CityForgeRoad road = (draft.roads ?? new List<CityForgeRoad>()).FirstOrDefault(item => item != null && RoadHostId(item) == host);
            return road == null ? 0 : SlopCityCityForgeCatalog.Default.GetRoadScreenSlots(road.roadClass);
        }

        private static string RoadHostId(CityForgeRoad road)
        {
            string direction = road.direction == CityForgeDirection.North ? "north" : road.direction == CityForgeDirection.East ? "east" : road.direction == CityForgeDirection.South ? "south" : "west";
            return "road-" + road.cellX + "-" + road.cellZ + "-" + direction;
        }
        private void DemolishLast()
        {
            CityForgeBuilding building = (draft.buildings ?? new List<CityForgeBuilding>()).LastOrDefault(item => item != null);
            if (building == null) { SetMessage("There is no building to demolish."); return; }
            TryDemolish(building.id);
        }
        private void RenovateLast()
        {
            CityForgeLandmark landmark = (draft.landmarks ?? new List<CityForgeLandmark>()).LastOrDefault(item => item != null);
            if (landmark == null) { SetMessage("There is no Cityworks site to renovate."); return; }
            string[] states = { "unavailable", "discovered", "diagnosing", "chosen", "active", "opening", "established", "paused", "renovating" }; int index = Array.IndexOf(states, landmark.state); TrySetLandmarkState(landmark.id, states[(index + 1 + states.Length) % states.Length]);
        }

        private void CreateUi()
        {
            GameObject root = new GameObject("CityForgeBuildMode", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false); canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.overrideSorting = true; canvas.sortingOrder = CityForgeCanvasSortingOrder;
            // The Mac player uses the native OnGUI path below for one
            // coherent point/pixel conversion. Leaving UGUI's raycaster live
            // would give the same click a second, stale Input.mousePosition
            // interpretation on Retina and mixed-DPI displays.
            GraphicRaycaster buildRaycaster = root.GetComponent<GraphicRaycaster>();
            if (buildRaycaster) buildRaycaster.enabled = !UsesNativeMacMapPointer();
            CanvasScaler scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720);
            // A root overlay Canvas is screen-sized even when its RectTransform
            // appears to be resized. Giving that root an Image made an
            // invisible full-screen raycast target on standalone builds, so
            // every map click was classified as UI. Keep the root transparent
            // to input and put the actual, bounded toolbar in a child.
            GameObject toolbar = new GameObject("CityForgeToolbar", typeof(RectTransform), typeof(Image)); toolbar.transform.SetParent(root.transform, false);
            buildUiSurface = toolbar.GetComponent<RectTransform>(); buildUiSurface.anchorMin = new Vector2(0f, 1f); buildUiSurface.anchorMax = new Vector2(1f, 1f); buildUiSurface.pivot = new Vector2(.5f, 1f); buildUiSurface.sizeDelta = new Vector2(0f, 164f);
            Image panel = toolbar.GetComponent<Image>(); panel.color = new Color(.025f, .045f, .06f, .96f);
            heading = Label(toolbar.transform, "City Forge", 20, new Vector2(12, -10), new Vector2(420, 26));
            contextHint = Label(toolbar.transform, "", 13, new Vector2(436, -10), new Vector2(830, 24)); contextHint.alignment = TextAnchor.MiddleRight;
            message = Label(toolbar.transform, "", 13, new Vector2(12, -142), new Vector2(1240, 18));
            float x = 12f;
            foreach (View choice in new[] { View.Streets, View.Ground, View.Lots, View.Buildings, View.Services, View.Life })
            {
                View local = choice; Button button = Button(toolbar.transform, choice.ToString(), new Vector2(x, -42), new Vector2(80, 30)); button.onClick.AddListener(() => SelectView(local)); viewButtons.Add(choice, button); x += 86f;
            }
            undoButton = Button(toolbar.transform, "Undo", new Vector2(532, -42), new Vector2(64, 30)); undoButton.onClick.AddListener(Undo);
            redoButton = Button(toolbar.transform, "Redo", new Vector2(602, -42), new Vector2(64, 30)); redoButton.onClick.AddListener(Redo);
            Button preview = Button(toolbar.transform, "Review", new Vector2(672, -42), new Vector2(82, 30)); preview.onClick.AddListener(Preview);
            Text autosave = Label(toolbar.transform, "Auto-saves", 13, new Vector2(760, -42), new Vector2(96, 30)); autosave.alignment = TextAnchor.MiddleCenter; autosave.color = new Color(.72f, .88f, .55f, 1f);
            Button exit = Button(toolbar.transform, "Return", new Vector2(862, -42), new Vector2(82, 30)); exit.onClick.AddListener(Close);

            groundActionButton = Button(toolbar.transform, "Mode: Lay ground", new Vector2(12, -96), new Vector2(180, 32)); groundActionButton.onClick.AddListener(ToggleGroundRemoval); RegisterViewControl(View.Ground, groundActionButton);
            roadButton = Button(toolbar.transform, "Road", new Vector2(12, -96), new Vector2(160, 32)); roadButton.onClick.AddListener(CycleRoadClass); RegisterViewControl(View.Streets, roadButton);
            removeRoadButton = Button(toolbar.transform, "Add road", new Vector2(178, -96), new Vector2(112, 32)); removeRoadButton.onClick.AddListener(ToggleRoadRemoval); RegisterViewControl(View.Streets, removeRoadButton);
            roadDirectionButton = Button(toolbar.transform, "Direction", new Vector2(296, -96), new Vector2(144, 32)); roadDirectionButton.onClick.AddListener(CycleRoadDirection); RegisterViewControl(View.Streets, roadDirectionButton);

            zoneButton = Button(toolbar.transform, "Zone", new Vector2(12, -96), new Vector2(160, 32)); zoneButton.onClick.AddListener(CycleLotZone); RegisterViewControl(View.Lots, zoneButton);
            Button merge = Button(toolbar.transform, "Merge lots", new Vector2(178, -96), new Vector2(112, 32)); merge.onClick.AddListener(MergeSelectedLots); RegisterViewControl(View.Lots, merge);
            Button split = Button(toolbar.transform, "Split lot", new Vector2(296, -96), new Vector2(108, 32)); split.onClick.AddListener(SplitSelectedLot); RegisterViewControl(View.Lots, split);

            kitButton = Button(toolbar.transform, "Kit", new Vector2(12, -96), new Vector2(120, 32)); kitButton.onClick.AddListener(CycleBuildingKit); RegisterViewControl(View.Buildings, kitButton);
            facadeButton = Button(toolbar.transform, "Facade", new Vector2(138, -96), new Vector2(130, 32)); facadeButton.onClick.AddListener(CycleFacade); RegisterViewControl(View.Buildings, facadeButton);
            roofButton = Button(toolbar.transform, "Roof", new Vector2(274, -96), new Vector2(126, 32)); roofButton.onClick.AddListener(CycleRoof); RegisterViewControl(View.Buildings, roofButton);
            storefrontButton = Button(toolbar.transform, "Store", new Vector2(406, -96), new Vector2(118, 32)); storefrontButton.onClick.AddListener(CycleStorefront); RegisterViewControl(View.Buildings, storefrontButton);
            entranceButton = Button(toolbar.transform, "Entrance", new Vector2(530, -96), new Vector2(116, 32)); entranceButton.onClick.AddListener(CycleEntrance); RegisterViewControl(View.Buildings, entranceButton);
            Button floorDown = Button(toolbar.transform, "Floor −", new Vector2(652, -96), new Vector2(84, 32)); floorDown.onClick.AddListener(() => ChangeFloors(-1)); RegisterViewControl(View.Buildings, floorDown);
            floorsButton = Button(toolbar.transform, "Floor +", new Vector2(742, -96), new Vector2(100, 32)); floorsButton.onClick.AddListener(() => ChangeFloors(1)); RegisterViewControl(View.Buildings, floorsButton);
            interiorButton = Button(toolbar.transform, "Interior", new Vector2(848, -96), new Vector2(180, 32)); interiorButton.onClick.AddListener(CycleInterior); RegisterViewControl(View.Buildings, interiorButton);
            Button demolish = Button(toolbar.transform, "Demolish", new Vector2(1034, -96), new Vector2(120, 32)); demolish.onClick.AddListener(DemolishLast); RegisterViewControl(View.Buildings, demolish);

            serviceButton = Button(toolbar.transform, "Service", new Vector2(12, -96), new Vector2(180, 32)); serviceButton.onClick.AddListener(CycleService); RegisterViewControl(View.Services, serviceButton);
            mountButton = Button(toolbar.transform, "Mount", new Vector2(364, -96), new Vector2(170, 32)); mountButton.onClick.AddListener(CycleScreenMount); RegisterViewControl(View.Services, mountButton);
            hostButton = Button(toolbar.transform, "Host", new Vector2(540, -96), new Vector2(120, 32)); hostButton.onClick.AddListener(CycleScreenHost); RegisterViewControl(View.Services, hostButton);
            targetButton = Button(toolbar.transform, "Screen target", new Vector2(666, -96), new Vector2(220, 32)); targetButton.onClick.AddListener(CycleScreenTarget); RegisterViewControl(View.Services, targetButton);
            deviceButton = Button(toolbar.transform, "Device", new Vector2(892, -96), new Vector2(170, 32)); deviceButton.onClick.AddListener(CycleScreenDevice); RegisterViewControl(View.Services, deviceButton);
            Button screen = Button(toolbar.transform, "Place screen", new Vector2(1068, -96), new Vector2(150, 32)); screen.onClick.AddListener(PlaceScreenOnLastBuilding); RegisterViewControl(View.Services, screen);

            landmarkButton = Button(toolbar.transform, "Cityworks outcome", new Vector2(12, -96), new Vector2(250, 32)); landmarkButton.onClick.AddListener(CycleLandmarkOutcome); RegisterViewControl(View.Life, landmarkButton);
            situationButton = Button(toolbar.transform, "Situation", new Vector2(268, -96), new Vector2(250, 32)); situationButton.onClick.AddListener(CycleSituationTemplate); RegisterViewControl(View.Life, situationButton);
            situationStateButton = Button(toolbar.transform, "Situation state", new Vector2(524, -96), new Vector2(150, 32)); situationStateButton.onClick.AddListener(CycleSituationState); RegisterViewControl(View.Life, situationStateButton);
            Button renovate = Button(toolbar.transform, "Renovate", new Vector2(680, -96), new Vector2(140, 32)); renovate.onClick.AddListener(RenovateLast); RegisterViewControl(View.Life, renovate);
            buildUiButtons.AddRange(toolbar.GetComponentsInChildren<Button>(true));
        }

        private void RegisterViewControl(View scope, Button button)
        {
            if (!button) return;
            if (!viewControls.TryGetValue(scope, out List<GameObject> controls))
            {
                controls = new List<GameObject>();
                viewControls.Add(scope, controls);
            }
            controls.Add(button.gameObject);
        }

        /// <summary>
        /// Keeps the first usable city block legible without sneaking a
        /// default island into the Sky Sprawl. A player always sees the one
        /// next prerequisite for ground, street, lot, and building.
        /// </summary>
        private string BuildProgressHint()
        {
            if (autosaveInFlight) return "Saving City Forge…";
            if (draft == null) return ViewHint(view);
            HashSet<int> ground = new HashSet<int>(draft.groundCells ?? new List<int>());
            if (!HasAdjacentGroundPair(ground)) return "Step 1 of 4 · Lay two touching island cells.";
            if (!(draft.roads ?? new List<CityForgeRoad>()).Any(road => road != null && SlopCityCityForge.RoadHasIslandGround(draft, road)))
                return "Step 2 of 4 · Streets: a two-cell starter pair picks its shared edge; otherwise choose Road side, then click.";
            if (!(draft.parcels ?? new List<CityForgeParcel>()).Any(parcel => HasStreetFrontage(parcel, null)))
                return "Step 3 of 4 · Lots: click a ground cell touching a street.";
            if (!(draft.buildings ?? new List<CityForgeBuilding>()).Any(item => item != null))
                return "Step 4 of 4 · Buildings: choose a kit, then click the street-fronted lot.";
            return "Block ready · add services, screens, Cityworks, or keep designing.";
        }

        private static bool HasAdjacentGroundPair(HashSet<int> ground)
        {
            if (ground == null || ground.Count < 2) return false;
            foreach (int cell in ground)
            {
                int x = cell % SlopCityCityForge.GridSize;
                int z = cell / SlopCityCityForge.GridSize;
                if (x + 1 < SlopCityCityForge.GridSize && ground.Contains(cell + 1)) return true;
                if (z + 1 < SlopCityCityForge.GridSize && ground.Contains(cell + SlopCityCityForge.GridSize)) return true;
            }
            return false;
        }

        private string ViewHint(View value)
        {
            switch (value)
            {
                case View.Streets:
                    return removeRoad
                        ? "Choose a road side, then click any grid cell to erase it · the lit bar previews the road · drag to pan · scroll to zoom"
                        : "Lay two touching island cells, then click either one · a simple pair picks its shared edge; otherwise choose Road side · drag to pan · scroll to zoom";
                case View.Ground: return removeGround
                    ? "Erase ground mode · click a clear island cell to return it to open sky · lots and streets stay protected · every change auto-saves"
                    : "Lay ground mode · click open sky to make an island · click Mode: Lay ground to switch to erase · every change auto-saves";
                case View.Lots: return "Click a cell to make a lot · Shift-click builds a footprint · lay a street beside it before building · changes auto-save";
                case View.Buildings: return "Click a zoned lot to add a designed building · it automatically faces any street touching the lot · changes auto-save";
                case View.Services: return "Lay a street beside the lot, then click it to add service · choose a screen target before placing a screen · changes auto-save";
                default: return "Choose a Cityworks outcome, then click a lot to make its site · click it again to add or advance its situation · changes auto-save";
            }
        }

        private static string RoadClassLabel(CityForgeRoadClass value)
        {
            switch (value)
            {
                case CityForgeRoadClass.LocalStreet: return "Local street";
                case CityForgeRoadClass.Boulevard: return "Boulevard";
                case CityForgeRoadClass.ServiceAlley: return "Service alley";
                case CityForgeRoadClass.PedestrianRoute: return "Pedestrian route";
                default: return "Transit/service";
            }
        }

        private static string DirectionLabel(CityForgeDirection value)
        {
            return value == CityForgeDirection.North ? "North" : value == CityForgeDirection.East ? "East" : value == CityForgeDirection.South ? "South" : "West";
        }

        private void CreateLauncher()
        {
            GameObject root = new GameObject("CityForgeBuildLauncher", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            launcherCanvas = root.GetComponent<Canvas>(); launcherCanvas.renderMode = RenderMode.ScreenSpaceOverlay; launcherCanvas.overrideSorting = true; launcherCanvas.sortingOrder = CityForgeCanvasSortingOrder;
            GraphicRaycaster launcherRaycaster = root.GetComponent<GraphicRaycaster>();
            if (launcherRaycaster) launcherRaycaster.enabled = !UsesNativeMacMapPointer();
            CanvasScaler scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720);
            launcherButton = Button(root.transform, "BUILD CITY", Vector2.zero, new Vector2(188f, 52f), true);
            RectTransform rect = launcherButton.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.zero; rect.pivot = Vector2.zero; rect.anchoredPosition = new Vector2(22f, 22f);
            launcherStatus = Label(root.transform, "Open City Forge · B", 13, Vector2.zero, new Vector2(420f, 24f));
            RectTransform statusRect = launcherStatus.GetComponent<RectTransform>(); statusRect.anchorMin = Vector2.zero; statusRect.anchorMax = Vector2.zero; statusRect.pivot = Vector2.zero; statusRect.anchoredPosition = new Vector2(22f, 80f);
            launcherStatus.alignment = TextAnchor.MiddleLeft;
            launcherButton.onClick.AddListener(RequestLauncherOpen); SetLauncher(editingEnabled);
        }

        private void SetLauncher(bool visible)
        {
            if (!launcherCanvas) return;
            launcherCanvas.gameObject.SetActive(visible);
            if (!visible) ResetLauncherPointerActivation();
            if (visible) RefreshLauncher();
        }

        private void RefreshLauncherReadiness()
        {
            bool ready = editingEnabled && controller && controller.IsInitialized;
            if (launcherReadinessKnown && launcherReady == ready) return;
            launcherReadinessKnown = true;
            launcherReady = ready;
            if (!opened && !pendingOpenWhenReady) RefreshLauncher();
        }

        private void RefreshLauncherCursorMode()
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (launcherCursorStateKnown && launcherCursorLocked == locked) return;
            launcherCursorStateKnown = true;
            launcherCursorLocked = locked;
            if (!opened) RefreshLauncher();
        }

        private void RefreshLauncher()
        {
            launcherReady = editingEnabled && controller && controller.IsInitialized;
            launcherReadinessKnown = true;
            launcherCursorLocked = Cursor.lockState == CursorLockMode.Locked;
            launcherCursorStateKnown = true;
            ConfigureLauncherLayout(launcherCursorLocked);
            if (launcherButton)
            {
                // A locked first-person pointer is not a useful way to target
                // a corner control. Keep that visual space quiet and make B
                // the explicit entry route; reveal the clickable launcher
                // only when the pointer is free.
                launcherButton.gameObject.SetActive(!launcherCursorLocked);
                launcherButton.interactable = editingEnabled && !launcherUnavailable;
                SetButtonLabel(launcherButton, launcherUnavailable ? "CITY FORGE\nUNAVAILABLE" : !launcherReady ? "PREPARING CITY" : "BUILD CITY");
            }
            if (launcherStatus)
                launcherStatus.text = launcherUnavailable ? launcherUnavailableMessage : !launcherReady ? "City Forge is preparing your starter ring…" : launcherCursorLocked ? "Press B to build your city" : "Open City Forge · B";
        }

        private void ConfigureLauncherLayout(bool cursorLocked)
        {
            if (launcherButton)
            {
                RectTransform buttonRect = launcherButton.GetComponent<RectTransform>();
                if (buttonRect)
                {
                    buttonRect.anchorMin = Vector2.zero;
                    buttonRect.anchorMax = Vector2.zero;
                    buttonRect.pivot = Vector2.zero;
                    buttonRect.anchoredPosition = new Vector2(22f, 22f);
                    buttonRect.sizeDelta = new Vector2(188f, 52f);
                }
            }
            if (launcherStatus)
            {
                RectTransform statusRect = launcherStatus.GetComponent<RectTransform>();
                if (statusRect)
                {
                    statusRect.anchorMin = Vector2.zero;
                    statusRect.anchorMax = Vector2.zero;
                    statusRect.pivot = Vector2.zero;
                    statusRect.anchoredPosition = cursorLocked ? new Vector2(22f, 22f) : new Vector2(22f, 80f);
                    statusRect.sizeDelta = new Vector2(420f, 24f);
                }
                launcherStatus.alignment = TextAnchor.MiddleLeft;
            }
        }

        private void RequestLauncherOpen()
        {
            // A pointer-down fast path and the ordinary UGUI click can both
            // arrive in one frame. Treat the gesture as one player request.
            if (lastLauncherRequestFrame == Time.frameCount) return;
            lastLauncherRequestFrame = Time.frameCount;
            Open();
        }

        private void UpdateLauncherPointerActivation()
        {
            if (!launcherCanvas || !launcherCanvas.gameObject.activeInHierarchy || !launcherButton || !launcherButton.isActiveAndEnabled || !launcherButton.interactable)
            {
                ResetLauncherPointerActivation();
                return;
            }

            int activationSequence = LauncherPointerActivationSequence();
            bool activationSincePreviousUpdate = activationSequence != observedLauncherPointerActivationSequence;
            if (launcherMousePressedInside && activationSequence != launcherMouseActivationSequence) launcherMouseGestureConsumed = true;
            if (launcherTouchFingerId >= 0 && activationSequence != launcherTouchActivationSequence) launcherTouchGestureConsumed = true;

            if (Input.touchCount == 0)
            {
                // macOS owns the complete mouse gesture in OnGUI using its
                // native event coordinates. Reading legacy Input here races
                // a stale Retina/mixed-display position against that raw
                // gesture, which can turn a toolbar click into a map click.
                if (UsesNativeMacMapPointer())
                {
                    launcherMousePressedInside = false;
                    launcherMouseActivationSequence = activationSequence;
                    launcherMouseGestureConsumed = false;
                    observedLauncherPointerActivationSequence = activationSequence;
                    return;
                }
                bool mouseInside = LauncherContains(Input.mousePosition);
                if (Input.GetMouseButtonDown(0))
                {
                    launcherMousePressedInside = mouseInside;
                    launcherMouseActivationSequence = activationSequence;
                    launcherMouseGestureConsumed = activationSincePreviousUpdate;
                }
                if (Input.GetMouseButtonUp(0))
                {
                    bool activate = launcherMousePressedInside && mouseInside && !launcherMouseGestureConsumed && launcherMouseActivationSequence == LauncherPointerActivationSequence();
                    launcherMousePressedInside = false;
                    launcherMouseGestureConsumed = false;
                    if (activate) RequestLauncherOpen();
                }
            }
            else
            {
                launcherMousePressedInside = false;
                for (int index = 0; index < Input.touchCount; index++)
                {
                    Touch touch = Input.GetTouch(index);
                    if (touch.phase == TouchPhase.Began && launcherTouchFingerId < 0 && LauncherContains(touch.position))
                    {
                        launcherTouchFingerId = touch.fingerId;
                        launcherTouchStart = touch.position;
                        launcherTouchCanceled = false;
                        launcherTouchActivationSequence = activationSequence;
                        launcherTouchGestureConsumed = activationSincePreviousUpdate;
                        continue;
                    }
                    if (touch.fingerId != launcherTouchFingerId) continue;
                    if ((touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary) && Vector2.Distance(touch.position, launcherTouchStart) > LauncherTapSlopPixels)
                        launcherTouchCanceled = true;
                    if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    {
                        bool activate = touch.phase == TouchPhase.Ended && !launcherTouchCanceled && !launcherTouchGestureConsumed && LauncherContains(touch.position) && launcherTouchActivationSequence == LauncherPointerActivationSequence();
                        launcherTouchFingerId = -1;
                        launcherTouchCanceled = false;
                        launcherTouchGestureConsumed = false;
                        if (activate) RequestLauncherOpen();
                    }
                }
            }
            observedLauncherPointerActivationSequence = LauncherPointerActivationSequence();
        }

        private bool LauncherContains(Vector2 screenPosition)
        {
            RectTransform rect = launcherButton && launcherButton.isActiveAndEnabled ? launcherButton.GetComponent<RectTransform>() : null;
            return rect && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, null);
        }

        private void ResetLauncherPointerActivation()
        {
            launcherMousePressedInside = false;
            launcherMouseActivationSequence = LauncherPointerActivationSequence();
            launcherMouseGestureConsumed = false;
            launcherTouchFingerId = -1;
            launcherTouchCanceled = false;
            launcherTouchActivationSequence = LauncherPointerActivationSequence();
            launcherTouchGestureConsumed = false;
            observedLauncherPointerActivationSequence = LauncherPointerActivationSequence();
        }

        private int LauncherPointerActivationSequence()
        {
            SlopCityCityForgeActionButton button = launcherButton as SlopCityCityForgeActionButton;
            return button ? button.PointerDownActivationSequence : 0;
        }

        private bool UpdateBuildUiPointerActivation()
        {
            buildUiTouchReleasedThisFrame = false;
            if (!canvas || !canvas.gameObject.activeInHierarchy)
            {
                ResetBuildUiPointerActivation();
                return false;
            }

            bool mouseStartedOverBuildUi = false;
            if (Input.touchCount == 0)
            {
                ClearBuildUiTouchCapture();
                suppressBuildUiTouchUntilNoTouches = false;
                // On macOS, OnGUI owns pointer down/up and uses the native
                // event coordinate throughout. Do not compete with it here
                // through legacy Input.mousePosition: that value can lag a
                // Retina display change and capture the wrong toolbar cell.
                if (UsesNativeMacMapPointer())
                {
                    buildUiMouseCapture = null;
                    buildUiMouseSurfaceCapture = false;
                    buildUiMousePressFrame = -1;
                    return false;
                }
                if (Input.GetMouseButtonDown(0))
                {
                    buildUiMouseSurfaceCapture = BuildUiSurfaceContains(Input.mousePosition);
                    buildUiMouseCapture = buildUiMouseSurfaceCapture ? BuildUiActivatableButtonAt(Input.mousePosition) : null;
                    buildUiMousePressFrame = Time.frameCount;
                    mouseStartedOverBuildUi = buildUiMouseSurfaceCapture;
                }
                if (Input.GetMouseButtonUp(0))
                {
                    Button capture = buildUiMouseCapture;
                    bool activate = buildUiMouseSurfaceCapture && capture && BuildUiActivatableButtonAt(Input.mousePosition) == capture && !BuildUiButtonActivatedOnOrAfter(capture, buildUiMousePressFrame);
                    buildUiMouseCapture = null;
                    buildUiMouseSurfaceCapture = false;
                    buildUiMousePressFrame = -1;
                    if (activate) InvokeBuildUiFallback(capture, PointerInputModule.kMouseLeftId);
                }
            }
            else
            {
                buildUiMouseCapture = null;
                buildUiMouseSurfaceCapture = false;
                buildUiMousePressFrame = -1;
                for (int index = 0; index < Input.touchCount; index++)
                {
                    Touch touch = Input.GetTouch(index);
                    if (touch.phase == TouchPhase.Began && BuildUiSurfaceContains(touch.position))
                    {
                        suppressBuildUiTouchUntilNoTouches = true;
                        if (buildUiTouchFingerId < 0)
                        {
                            buildUiTouchCapture = BuildUiActivatableButtonAt(touch.position);
                            buildUiTouchFingerId = touch.fingerId;
                            buildUiTouchPressFrame = Time.frameCount;
                            buildUiTouchStart = touch.position;
                            buildUiTouchCanceled = false;
                        }
                        continue;
                    }
                    if (touch.fingerId != buildUiTouchFingerId) continue;
                    if ((touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary) && Vector2.Distance(touch.position, buildUiTouchStart) > LauncherTapSlopPixels)
                        buildUiTouchCanceled = true;
                    if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    {
                        Button capture = buildUiTouchCapture;
                        buildUiTouchReleasedThisFrame = true;
                        bool activate = touch.phase == TouchPhase.Ended && capture && !buildUiTouchCanceled && BuildUiActivatableButtonAt(touch.position) == capture && !BuildUiButtonActivatedOnOrAfter(capture, buildUiTouchPressFrame);
                        ClearBuildUiTouchCapture();
                        if (activate) InvokeBuildUiFallback(capture, touch.fingerId);
                    }
                }
                // A browser can drop an Ended/Canceled event. Keep an actual
                // ended touch long enough to consume its release, then clear a
                // capture only when its finger has genuinely disappeared.
                if (buildUiTouchFingerId >= 0 && !BuildUiTouchIsPresent(buildUiTouchFingerId)) ClearBuildUiTouchCapture();
            }
            return mouseStartedOverBuildUi;
        }

        private bool BuildUiSurfaceContains(Vector2 screenPosition)
        {
            if (!buildUiSurface || !buildUiSurface.gameObject.activeInHierarchy) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(buildUiSurface, screenPosition, null);
        }

        private Button BuildUiActivatableButtonAt(Vector2 screenPosition)
        {
            for (int index = buildUiButtons.Count - 1; index >= 0; index--)
            {
                Button button = buildUiButtons[index];
                RectTransform rect = button ? button.GetComponent<RectTransform>() : null;
                if (button && button.IsActive() && button.IsInteractable() && rect && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, null)) return button;
            }
            return null;
        }

        /// <summary>
        /// Both toolbar and map receive the same Canvas-mapped native point.
        /// Do not try alternative point/pixel scales here: a map click can
        /// alias into the toolbar under a speculative scale and get swallowed.
        /// </summary>
        private void ResolveNativeMacBuildUiHit(Vector2 screenPosition, out bool withinToolbar, out Button hit)
        {
            withinToolbar = BuildUiSurfaceContains(screenPosition);
            hit = withinToolbar ? BuildUiActivatableButtonAt(screenPosition) : null;
        }

        private static bool BuildUiButtonActivatedOnOrAfter(Button button, int pressFrame)
        {
            SlopCityCityForgeActionButton action = button as SlopCityCityForgeActionButton;
            return action && action.LastActivationFrame >= pressFrame;
        }

        private static bool BuildUiTouchIsPresent(int fingerId)
        {
            if (fingerId < 0) return false;
            for (int index = 0; index < Input.touchCount; index++) if (Input.GetTouch(index).fingerId == fingerId) return true;
            return SlopCityPlayerInput.TouchIsActive(fingerId);
        }

        private static void InvokeBuildUiFallback(Button button, int pointerId)
        {
            if (!button || !button.IsActive() || !button.IsInteractable()) return;
            SlopCityCityForgeActionButton action = button as SlopCityCityForgeActionButton;
            if (action) action.RecordFallbackActivation(pointerId);
            button.onClick.Invoke();
        }

        private void ClearBuildUiTouchCapture()
        {
            buildUiTouchCapture = null;
            buildUiTouchFingerId = -1;
            buildUiTouchPressFrame = -1;
            buildUiTouchCanceled = false;
        }

        private void ResetBuildUiPointerActivation()
        {
            buildUiMouseCapture = null;
            buildUiMouseSurfaceCapture = false;
            buildUiMousePressFrame = -1;
            buildUiGuiMouseCapture = null;
            buildUiGuiMouseSurfaceCapture = false;
            buildUiGuiMousePressFrame = -1;
            ClearBuildUiTouchCapture();
            buildUiTouchReleasedThisFrame = false;
            suppressBuildUiTouchUntilNoTouches = false;
        }

        private void ResetNativeMapPointer()
        {
            mapGuiMousePressed = false;
            mapGuiMouseDragged = false;
            mapGuiMouseStart = Vector2.zero;
            mapGuiMousePrevious = Vector2.zero;
            mapGuiPointer = Vector2.zero;
            hasMapGuiPointer = false;
            ClearCapturedMapCell();
        }

        private void CancelBuildUiPointerActivation()
        {
            bool hadBuildUiTouch = suppressBuildUiTouchUntilNoTouches || buildUiTouchFingerId >= 0;
            ResetBuildUiPointerActivation();
            suppressBuildUiTouchUntilNoTouches = hadBuildUiTouch;
        }

        private void SetLauncherStatus(string value)
        {
            if (launcherStatus) launcherStatus.text = value ?? string.Empty;
        }

        private void ResumeFirstPersonForBuildMode()
        {
            if (!firstPerson) firstPerson = FindFirstObjectByType<SlopCityFirstPerson>();
            // The initial pause card is deliberately a hard input modal. A
            // successful City Forge entry replaces it with the editor rather
            // than leaving its full-screen raycaster over the build surface.
            if (firstPerson && firstPerson.IsPaused) firstPerson.PlayOrResume();
        }

        private void SetHudSuppressed(bool value)
        {
            if (!firstPerson) firstPerson = FindFirstObjectByType<SlopCityFirstPerson>();
            SlopCityHud playerHud = firstPerson ? firstPerson.Hud : null;
            if (playerHud) hud = playerHud;
            if (!hud) hud = FindFirstObjectByType<SlopCityHud>();
            if (!salvageHud) salvageHud = firstPerson ? firstPerson.GetComponent<SlopCitySalvageHud>() : FindFirstObjectByType<SlopCitySalvageHud>();
            if (hud) hud.SetExternalOverlaySuppressed(value);
            if (salvageHud) salvageHud.SetExternalOverlaySuppressed(value);
            hudSuppressed = value;
        }

        private void GateFirstPerson(bool gate)
        {
            if (gate)
            {
                if (!firstPerson) firstPerson = FindFirstObjectByType<SlopCityFirstPerson>();
                if (!firstPerson) return;
                firstPersonWasEnabled = firstPerson.enabled;
                // SetExternalMovement keeps the capsule safe; disabling this public
                // gameplay component for the editor also stops its look/input loop.
                firstPerson.SetExternalMovement(true);
                if (firstPersonWasEnabled) firstPerson.enabled = false;
                return;
            }
            if (!firstPerson) return;
            if (firstPersonWasEnabled) firstPerson.enabled = true;
            firstPerson.SetExternalMovement(false);
            firstPersonWasEnabled = false;
        }

        private static Text Label(Transform parent, string value, int fontSize, Vector2 anchored, Vector2 size)
        {
            GameObject item = new GameObject("Label_" + value, typeof(RectTransform), typeof(Text)); item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = anchored; rect.sizeDelta = size;
            Text text = item.GetComponent<Text>(); text.text = value; text.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); text.fontSize = fontSize; text.color = Color.white; text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false; return text;
        }
        private static Button Button(Transform parent, string label, Vector2 anchored, Vector2 size, bool invokeOnMousePointerDown = false)
        {
            GameObject item = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image)); item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = anchored; rect.sizeDelta = size;
            Image image = item.GetComponent<Image>(); image.color = new Color(.23f, .34f, .25f, 1f);
            SlopCityCityForgeActionButton button = item.AddComponent<SlopCityCityForgeActionButton>(); button.InvokeOnMousePointerDown = invokeOnMousePointerDown;
            Text text = Label(item.transform, label, 13, Vector2.zero, size); text.alignment = TextAnchor.MiddleCenter; RectTransform textRect = text.GetComponent<RectTransform>(); textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.pivot = new Vector2(.5f, .5f); textRect.anchoredPosition = Vector2.zero; return button;
        }
        private void SetUi(bool visible) { if (canvas) canvas.gameObject.SetActive(visible); }
        private void RefreshUi()
        {
            if (heading) heading.text = "City Forge · " + view + " · block " + selected;
            if (contextHint) contextHint.text = BuildProgressHint();
            foreach (Button button in buildUiButtons) if (button) button.interactable = !autosaveInFlight;
            if (undoButton) undoButton.interactable = !autosaveInFlight && atomicUndo.Count > 0;
            if (redoButton) redoButton.interactable = !autosaveInFlight && atomicRedo.Count > 0;
            foreach (KeyValuePair<View, Button> pair in viewButtons)
                if (pair.Value)
                {
                    pair.Value.interactable = !autosaveInFlight;
                    pair.Value.GetComponent<Image>().color = pair.Key == view ? new Color(.63f, .82f, .35f, 1f) : new Color(.23f, .34f, .25f, 1f);
                }
            foreach (KeyValuePair<View, List<GameObject>> pair in viewControls)
                foreach (GameObject control in pair.Value)
                    if (control) control.SetActive(pair.Key == view);
            SetButtonLabel(groundActionButton, removeGround ? "Mode: Erase ground" : "Mode: Lay ground");
            SetButtonLabel(roadButton, "Road: " + RoadClassLabel(roadClass)); SetButtonLabel(removeRoadButton, removeRoad ? "Erase roads" : "Place roads"); SetButtonLabel(roadDirectionButton, "Road side: " + DirectionLabel(roadDirection)); SetButtonLabel(zoneButton, "Zone: " + lotZone);
            SetButtonLabel(kitButton, "Kit: " + buildingBlueprint); SetButtonLabel(facadeButton, "Facade: " + facade); SetButtonLabel(roofButton, "Roof: " + roof); SetButtonLabel(storefrontButton, "Store: " + storefront); SetButtonLabel(entranceButton, "Entry: " + entrance); SetButtonLabel(floorsButton, "Floor + (" + buildingFloors + ")"); SetButtonLabel(interiorButton, "Interior: " + (string.IsNullOrEmpty(interior) ? "none" : interior));
            string host = screenHostMode == 0 ? "building" : screenHostMode == 1 ? "site" : "road";
            string target = ScreenTargetLabel(screenHostId);
            SetButtonLabel(serviceButton, "Service: " + serviceType); SetButtonLabel(landmarkButton, "Cityworks: " + (string.IsNullOrEmpty(landmarkOutcome) ? "choose" : landmarkOutcome)); SetButtonLabel(mountButton, "Mount: " + screenMount); SetButtonLabel(hostButton, "Host: " + host); SetButtonLabel(targetButton, "Screen target: " + target); SetButtonLabel(deviceButton, "Device: " + screenDevice); SetButtonLabel(situationButton, "Situation: " + (string.IsNullOrEmpty(situationTemplate) ? "choose" : situationTemplate)); SetButtonLabel(situationStateButton, "State: " + situationState);
        }

        private static string ScreenTargetLabel(string host)
        {
            if (string.IsNullOrEmpty(host)) return "choose target";
            if (host.StartsWith("building-", StringComparison.Ordinal)) return "building " + host.Substring("building-".Length);
            if (host.StartsWith("site-", StringComparison.Ordinal)) return "Cityworks site " + host.Substring("site-".Length);
            if (host.StartsWith("road-", StringComparison.Ordinal)) return "road fixture";
            return host;
        }
        private static void SetButtonLabel(Button button, string value) { Text text = button ? button.GetComponentInChildren<Text>(true) : null; if (text) text.text = value; }
        private void SetMessage(string value)
        {
            if (message) message.text = value ?? string.Empty;
            if (!opened) SetLauncherStatus(value);
        }

        private void OnDestroy()
        {
            if (controller) controller.StrategicMapChanged -= HandleStrategicMapChanged;
            previewSink?.ClearCityForgeDraftPreview();
            buildOverlaySink?.ClearCityForgeBuildOverlay();
            RestoreCamera();
            GateFirstPerson(false);
            if (hudSuppressed) SetHudSuppressed(false);
        }

        private void CaptureCamera()
        {
            if (buildCamera)
            {
                originalCamera = new CameraState { position = buildCamera.transform.position, rotation = buildCamera.transform.rotation, orthographic = buildCamera.orthographic, orthographicSize = buildCamera.orthographicSize };
                FocusCamera();
            }
            savedCursorLockState = Cursor.lockState;
            savedCursorVisible = Cursor.visible;
            cursorStateCaptured = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        private void RestoreCamera()
        {
            if (!cursorStateCaptured) return;
            if (buildCamera)
            {
                buildCamera.transform.position = originalCamera.position; buildCamera.transform.rotation = originalCamera.rotation; buildCamera.orthographic = originalCamera.orthographic; buildCamera.orthographicSize = originalCamera.orthographicSize;
            }
            Cursor.lockState = savedCursorLockState;
            Cursor.visible = savedCursorVisible;
            cursorStateCaptured = false;
        }
        private void FocusCamera()
        {
            if (!buildCamera) return;
            buildCamera.orthographic = true; buildCamera.orthographicSize = topDownHeight * .55f; buildCamera.transform.position = new Vector3((selected.x + .5f) * 72f, topDownHeight, (selected.z + .5f) * 72f); buildCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
        private void MoveCamera()
        {
            if (!buildCamera) return;
            float pan = topDownHeight * .45f * Time.unscaledDeltaTime; Vector3 delta = new Vector3(Input.GetAxisRaw("Horizontal") * pan, 0f, Input.GetAxisRaw("Vertical") * pan);
            if (delta.sqrMagnitude > 0f) buildCamera.transform.position += delta;
            float wheel = Input.GetAxis("Mouse ScrollWheel"); if (Mathf.Abs(wheel) > .001f) { topDownHeight = Mathf.Clamp(topDownHeight - wheel * 45f, 40f, 280f); buildCamera.orthographicSize = topDownHeight * .55f; buildCamera.transform.position = new Vector3(buildCamera.transform.position.x, topDownHeight, buildCamera.transform.position.z); }
            if (Input.touchCount == 1 && !suppressBuildUiTouchUntilNoTouches && buildUiTouchFingerId < 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began) { lastTouchPosition = touch.position; touchPanned = false; }
                else if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                {
                    Vector2 movement = touch.position - lastTouchPosition;
                    if (movement.sqrMagnitude > 9f) touchPanned = true;
                    float metresPerPixel = topDownHeight / Mathf.Max(240f, Screen.height);
                    buildCamera.transform.position -= new Vector3(movement.x * metresPerPixel, 0f, movement.y * metresPerPixel);
                    lastTouchPosition = touch.position;
                }
                lastPinchDistance = 0f;
            }
            else if (Input.touchCount >= 2 && !suppressBuildUiTouchUntilNoTouches && buildUiTouchFingerId < 0)
            {
                Touch first = Input.GetTouch(0); Touch second = Input.GetTouch(1);
                float distance = Vector2.Distance(first.position, second.position);
                if (lastPinchDistance > 0f)
                {
                    float change = distance - lastPinchDistance;
                    if (Mathf.Abs(change) > .01f) { topDownHeight = Mathf.Clamp(topDownHeight - change * .16f, 40f, 280f); buildCamera.orthographicSize = topDownHeight * .55f; buildCamera.transform.position = new Vector3(buildCamera.transform.position.x, topDownHeight, buildCamera.transform.position.z); }
                }
                lastPinchDistance = distance; touchPanned = true;
            }
            else lastPinchDistance = 0f;
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!focused) { ResetLauncherPointerActivation(); CancelBuildUiPointerActivation(); ResetNativeMapPointer(); }
        }
        private void OnApplicationPause(bool paused)
        {
            if (paused) { ResetLauncherPointerActivation(); CancelBuildUiPointerActivation(); ResetNativeMapPointer(); }
        }
        private void OnDisable() { ResetLauncherPointerActivation(); ResetBuildUiPointerActivation(); ResetNativeMapPointer(); }
        private struct CameraState { public Vector3 position; public Quaternion rotation; public bool orthographic; public float orthographicSize; }
        private struct LandmarkChoice
        {
            public string classId;
            public string outcomeId;
            public LandmarkChoice(string classValue, string outcomeValue) { classId = classValue; outcomeId = outcomeValue; }
        }
    }

    /// <summary>Tracks City Forge button activation and gives the launcher an optional pointer-down fast path.</summary>
    public sealed class SlopCityCityForgeActionButton : Button
    {
        private const int NoPointer = int.MinValue;
        private int suppressedPointerClickId = NoPointer;
        private int suppressedPointerClickFrame = -1;
        public bool InvokeOnMousePointerDown { get; set; }
        public int PointerDownActivationSequence { get; private set; }
        public int LastActivationFrame { get; private set; } = -1;

        public void RecordFallbackActivation(int pointerId)
        {
            LastActivationFrame = Time.frameCount;
            suppressedPointerClickId = pointerId;
            suppressedPointerClickFrame = Time.frameCount;
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            // Clear a suppression left by a raw fallback that never received
            // its matching UGUI click before accepting a new gesture.
            if (eventData.pointerId == suppressedPointerClickId && Time.frameCount > suppressedPointerClickFrame)
            {
                suppressedPointerClickId = NoPointer;
                suppressedPointerClickFrame = -1;
            }
            base.OnPointerDown(eventData);
            if (InvokeOnMousePointerDown && eventData.pointerId == PointerInputModule.kMouseLeftId && eventData.button == PointerEventData.InputButton.Left && IsActive() && IsInteractable())
            {
                PointerDownActivationSequence++;
                LastActivationFrame = Time.frameCount;
                RecordFallbackActivation(eventData.pointerId);
                onClick.Invoke();
            }
        }

        public override void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.pointerId == suppressedPointerClickId)
            {
                suppressedPointerClickId = NoPointer;
                suppressedPointerClickFrame = -1;
                return;
            }
            if (!IsActive() || !IsInteractable()) return;
            LastActivationFrame = Time.frameCount;
            base.OnPointerClick(eventData);
        }
    }
}
