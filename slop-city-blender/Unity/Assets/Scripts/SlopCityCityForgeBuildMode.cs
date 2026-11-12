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
        public enum View { Streets, Lots, Buildings, Services, Life }

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
        private Button roadButton, removeRoadButton, roadDirectionButton, zoneButton, kitButton, facadeButton, roofButton, storefrontButton, entranceButton, floorsButton, interiorButton, serviceButton, landmarkButton, situationButton, situationStateButton, mountButton, hostButton, targetButton, deviceButton;
        private readonly Dictionary<View, Button> viewButtons = new Dictionary<View, Button>();
        private readonly Dictionary<View, List<GameObject>> viewControls = new Dictionary<View, List<GameObject>>();
        private CityForgeCoord selected;
        private CityForgeChunkPlan draft;
        private SlopCityCityForgeDraftHistory history;
        private View view;
        private bool opened;
        private CityForgeRoadClass roadClass = CityForgeRoadClass.LocalStreet;
        private CityForgeDirection roadDirection = CityForgeDirection.North;
        private CityForgeZone lotZone = CityForgeZone.Home;
        private bool removeRoad;
        private string buildingBlueprint = "walk-up", facade = "wet-masonry", roof = "service-roof", storefront = "none", interior = "";
        private string serviceType = "care-station", landmarkClass = "", landmarkOutcome = "", situationTemplate = "", situationState = "available", screenMount = "interior-wall-tv", screenDevice = "public-crt-01";
        private int screenHostMode;
        private string screenHostId = string.Empty;
        private int buildingFloors = 2;
        private CityForgeDirection entrance = CityForgeDirection.North;
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
        private const float MapDragThresholdPixels = 8f;
        private bool mapMousePressed;
        private bool mapMouseDragged;
        private Vector2 mapMouseStart;
        private Vector2 mapMousePrevious;
        private bool hasHoveredCell;
        private CityForgeCoord hoveredCell;

        public bool IsOpen { get { return opened; } }
        public bool EditingEnabled { get { return editingEnabled; } }
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
            if (!openedFromPendingRequest && editingEnabled && Input.GetKeyDown(toggleKey)) { if (opened) Close(); else RequestLauncherOpen(); }
            if (!opened) UpdateLauncherPointerActivation();
            if (!opened) return;
            bool mouseStartedOverBuildUi = UpdateBuildUiPointerActivation();
            if (!opened) return;
            UpdateMapMousePointer(mouseStartedOverBuildUi);
            MoveCamera();
            UpdateHoveredCell();
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SelectView(View.Streets);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SelectView(View.Lots);
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SelectView(View.Buildings);
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) SelectView(View.Services);
            if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) SelectView(View.Life);
            if (Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) Undo();
            if (Input.GetKeyDown(KeyCode.Y) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) Redo();
            if (Input.touchCount == 1 && !suppressBuildUiTouchUntilNoTouches && buildUiTouchFingerId < 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    if (!buildUiTouchReleasedThisFrame && !touchPanned && !(EventSystem.current && EventSystem.current.IsPointerOverGameObject(touch.fingerId))) ClickMap(touch.position);
                    touchPanned = false;
                }
            }
        }

        private void OnGUI()
        {
            Event current = Event.current;
            if (current == null || current.button != 0) return;
            EventType type = current.type == EventType.Used ? current.rawType : current.type;
            if (type != EventType.MouseDown && type != EventType.MouseUp) return;
            Vector2 screenPosition = new Vector2(current.mousePosition.x, Screen.height - current.mousePosition.y);

            if (!opened)
            {
                if (type == EventType.MouseDown && launcherCanvas && launcherCanvas.gameObject.activeInHierarchy && launcherButton && launcherButton.interactable && LauncherContains(screenPosition))
                {
                    current.Use();
                    RequestLauncherOpen();
                }
                return;
            }
            if (!canvas || !canvas.gameObject.activeInHierarchy) return;

            if (type == EventType.MouseDown)
            {
                buildUiGuiMouseSurfaceCapture = BuildUiSurfaceContains(screenPosition);
                buildUiGuiMouseCapture = buildUiGuiMouseSurfaceCapture ? BuildUiActivatableButtonAt(screenPosition) : null;
                buildUiGuiMousePressFrame = Time.frameCount;
                if (buildUiGuiMouseSurfaceCapture) current.Use();
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
            ResetAtomicDrafts(draft); history = new SlopCityCityForgeDraftHistory(draft); opened = true; view = View.Streets;
            ResumeFirstPersonForBuildMode(); SetHudSuppressed(true); GateFirstPerson(true); CaptureCamera(); controller.SetBuildMode(true); controller.StreamAt(selected); ShowStrategicMap(); SetUi(true); SetLauncher(false); RefreshUi(); RefreshBuildOverlay(); SetMessage("Choose a road side, then click any grid cell. The lit bar previews the road you will place."); BuildModeChanged?.Invoke(true); return true;
        }

        public void Close()
        {
            if (!opened) return;
            opened = false; ResetBuildUiPointerActivation(); mapMousePressed = false; mapMouseDragged = false; hasHoveredCell = false; previewSink?.ClearCityForgeDraftPreview(); buildOverlaySink?.ClearCityForgeBuildOverlay(); strategicMap?.ClearCityForgeStrategicMap(); if (controller && controller.IsInitialized) controller.SetBuildMode(false); RestoreCamera(); GateFirstPerson(false); SetHudSuppressed(false); SetUi(false); SetLauncher(editingEnabled);
            SlopCityFirstPerson player = FindFirstObjectByType<SlopCityFirstPerson>();
            if (player) PlayerNeighbourhoodRequested?.Invoke(new CityForgeCoord(Mathf.FloorToInt(player.transform.position.x / SlopCityCityForge.ChunkSizeMeters), Mathf.FloorToInt(player.transform.position.z / SlopCityCityForge.ChunkSizeMeters)));
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

        public void SelectView(View next) { view = next; RefreshUi(); RefreshBuildOverlay(); }
        public bool TrySetRoad(int cellX, int cellZ, CityForgeDirection direction, CityForgeRoadClass roadClass, bool remove)
        {
            if (draft == null || cellX < 0 || cellX > 8 || cellZ < 0 || cellZ > 8) return false;
            CanonicalRoad(ref cellX, ref cellZ, ref direction);
            CityForgeChunkPlan next = draft.Clone();
            SetRoad(next, cellX, cellZ, direction, roadClass, remove);
            CityForgeRoad road = new CityForgeRoad { cellX = cellX, cellZ = cellZ, direction = direction, roadClass = roadClass };
            if (!road.IsBoundary) return Apply(next, remove ? "Street removal planned." : "Street planned.");

            CityForgeCoord neighbour = SlopCityCityForge.Neighbour(selected, direction);
            if (SlopCityCityForge.IsProtectedOldCity(neighbour)) { SetMessage("The Old City boundary cannot be changed."); return false; }
            if (!EnsureAtomicNeighbour(neighbour) || !EnsureDraft(neighbour)) return false;
            if (!TryGetAtomicDraft(neighbour, out CityForgeChunkPlan reciprocal)) return false;
            ReciprocalRoad(cellX, cellZ, direction, out int reciprocalX, out int reciprocalZ, out CityForgeDirection reciprocalDirection);
            SetRoad(reciprocal, reciprocalX, reciprocalZ, reciprocalDirection, roadClass, remove);
            return ApplyAtomic(new Dictionary<CityForgeCoord, CityForgeChunkPlan>
            {
                { next.coord, next }, { reciprocal.coord, reciprocal }
            }, remove ? "Street removal planned on both sides of the boundary." : "Street connection planned on both sides of the boundary.");
        }
        public bool TryPlaceLot(IEnumerable<int> cells, CityForgeZone zone, string parcelId = null)
        {
            if (draft == null) return false; List<int> selectedCells = (cells ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (selectedCells.Count == 0 || selectedCells.Count > 81 || selectedCells.Any(item => item < 0 || item >= 81) || (draft.parcels ?? new List<CityForgeParcel>()).Any(parcel => parcel != null && parcel.cells != null && parcel.cells.Intersect(selectedCells).Any())) return false;
            CityForgeChunkPlan next = draft.Clone(); next.parcels.Add(new CityForgeParcel { id = string.IsNullOrEmpty(parcelId) ? NextId("parcel") : parcelId, zone = zone, cells = selectedCells, entranceEdge = CityForgeDirection.North }); return Apply(next, "Lot planned.");
        }
        public bool TryPlaceBuilding(string parcelId, string blueprintId, string facadeId, string roofId, int floors, CityForgeDirection entrance, string storefrontId = null, string interiorRole = null)
        {
            if (draft == null || (draft.buildings ?? new List<CityForgeBuilding>()).Any(item => item != null && item.parcelId == parcelId)) return false;
            CityForgeChunkPlan next = draft.Clone(); next.buildings.Add(new CityForgeBuilding { id = NextId("building"), parcelId = parcelId, blueprintId = blueprintId, facadeId = facadeId, roofId = roofId, floors = floors, entranceEdge = entrance, storefrontId = storefrontId ?? string.Empty, interiorRole = interiorRole ?? string.Empty }); return Apply(next, "Building kit placed.");
        }
        public bool TryPlaceService(string parcelId, string type)
        {
            if (draft == null) return false; CityForgeChunkPlan next = draft.Clone(); next.services.Add(new CityForgeService { id = NextId("service"), parcelId = parcelId, type = type }); return Apply(next, "Service planned.");
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
            if (atomicUndo.Count == 0) return;
            Dictionary<CityForgeCoord, CityForgeChunkPlan> restore = atomicUndo.Pop();
            atomicRedo.Push(CloneDraftMap(atomicDrafts));
            RestoreAtomicDrafts(restore); SetMessage("Draft edit undone."); RefreshUi(); RefreshBuildOverlay(); NotifyDraftsChanged();
        }

        public void Redo()
        {
            if (atomicRedo.Count == 0) return;
            Dictionary<CityForgeCoord, CityForgeChunkPlan> restore = atomicRedo.Pop();
            atomicUndo.Push(CloneDraftMap(atomicDrafts));
            RestoreAtomicDrafts(restore); SetMessage("Draft edit restored."); RefreshUi(); RefreshBuildOverlay(); NotifyDraftsChanged();
        }

        public void Preview()
        {
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
            SetMessage("Draft checks pass. The highlighted plan is ready to commit.");
        }

        public void Commit()
        {
            List<CityForgeChunkPlan> plans = PlansForCommit();
            if (plans.Count == 0) { SetMessage("Make a City Forge edit before committing."); return; }
            if (!controller.ValidateDraftOnly(plans, out CityForgeValidation validation)) { SetMessage(validation.Messages.FirstOrDefault() ?? "Check this plan first."); return; }
            if (CommitRequested != null) { CommitRequested(plans.Select(item => item.Clone()).ToList()); SetMessage("Submitting plan…"); return; }
            if (!controller.TryCommit(plans, out validation)) { SetMessage(validation.Messages.FirstOrDefault() ?? "Commit failed."); return; }
            draft = controller.BeginDraft(selected); ResetAtomicDrafts(draft); history = new SlopCityCityForgeDraftHistory(draft); ShowStrategicMap(); SetMessage("Accepted locally. Leave Build Mode to walk the updated block."); RefreshUi(); RefreshBuildOverlay();
        }

        /// <summary>Called by a hosted bridge only after its authoritative D1 response is verified.</summary>
        public bool ApplyAuthoritativeCommit(CityForgeWorldHead head, IEnumerable<CityForgeChunkPlan> plans, out CityForgeValidation validation)
        {
            if (!controller.ApplyAuthoritativeCommit(head, plans, out validation)) { SetMessage(validation.Messages.FirstOrDefault() ?? "The hosted commit was refused."); return false; }
            if (opened && controller.TryGetAccepted(selected, out CityForgeChunkPlan accepted)) { draft = accepted; ResetAtomicDrafts(draft); history = new SlopCityCityForgeDraftHistory(draft); }
            ShowStrategicMap(); SetMessage("Accepted by the City Forge world."); RefreshUi(); RefreshBuildOverlay(); return true;
        }

        public void SetStatusMessage(string value) { SetMessage(value); }

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
                CityForgeChunkPlan value = pair.Value.Clone(); atomicDrafts[pair.Key] = value;
                if (!atomicBaselines.ContainsKey(pair.Key)) atomicBaselines[pair.Key] = BaselineFor(value);
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
            foreach (CityForgeChunkPlan value in updates.Values)
                if (!controller.TrySavePrivateDraft(value, out string error)) { SetMessage(error ?? "That edit is unavailable."); return false; }
            PushAtomicUndo();
            foreach (KeyValuePair<CityForgeCoord, CityForgeChunkPlan> pair in updates)
            {
                atomicDrafts[pair.Key] = pair.Value.Clone();
                if (!atomicBaselines.ContainsKey(pair.Key)) atomicBaselines[pair.Key] = BaselineFor(pair.Value);
            }
            if (atomicDrafts.TryGetValue(selected, out CityForgeChunkPlan current)) draft = current.Clone();
            history = new SlopCityCityForgeDraftHistory(draft); SetMessage(success); RefreshUi(); RefreshBuildOverlay(); NotifyDraftsChanged(updates.Values); return true;
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
            if (Input.touchCount > 0)
            {
                mapMousePressed = false;
                mapMouseDragged = false;
                return;
            }
            if (Input.GetMouseButtonDown(0))
            {
                mapMousePressed = !mouseStartedOverBuildUi && !(EventSystem.current && EventSystem.current.IsPointerOverGameObject());
                mapMouseDragged = false;
                mapMouseStart = Input.mousePosition;
                mapMousePrevious = mapMouseStart;
            }
            if (mapMousePressed && Input.GetMouseButton(0))
            {
                Vector2 current = Input.mousePosition;
                if (!mapMouseDragged && Vector2.Distance(current, mapMouseStart) >= MapDragThresholdPixels) mapMouseDragged = true;
                if (mapMouseDragged && buildCamera)
                {
                    Vector2 movement = current - mapMousePrevious;
                    float metresPerPixel = topDownHeight / Mathf.Max(240f, Screen.height);
                    buildCamera.transform.position -= new Vector3(movement.x * metresPerPixel, 0f, movement.y * metresPerPixel);
                }
                mapMousePrevious = current;
            }
            if (!Input.GetMouseButtonUp(0)) return;
            bool activate = mapMousePressed && !mapMouseDragged;
            mapMousePressed = false;
            mapMouseDragged = false;
            if (activate) ClickMap(Input.mousePosition);
        }

        private void UpdateHoveredCell()
        {
            bool nextHasHover = false;
            CityForgeCoord nextHover = default(CityForgeCoord);
            if (!BuildUiSurfaceContains(Input.mousePosition) && !mapMouseDragged && TryGetMapCell(Input.mousePosition, out CityForgeCoord coordinate, out int x, out int z) && coordinate.Equals(selected))
            {
                nextHasHover = true;
                nextHover = new CityForgeCoord(x, z);
            }
            if (nextHasHover == hasHoveredCell && (!nextHasHover || nextHover.Equals(hoveredCell))) return;
            hasHoveredCell = nextHasHover;
            hoveredCell = nextHover;
            RefreshBuildOverlay();
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

        private void ClickMap(Vector3 screenPosition)
        {
            if (!TryGetMapCell(screenPosition, out CityForgeCoord coord, out int x, out int z)) return;
            if (!coord.Equals(selected)) { SelectChunk(coord); return; }
            if (view == View.Streets) ToggleStreet(x, z);
            else if (view == View.Lots) AddLot(x, z);
            else if (view == View.Buildings) AddBuilding(x, z);
            else if (view == View.Services) AddService(x, z);
            else AddLife(x, z);
        }

        private void ToggleStreet(int x, int z)
        {
            TrySetRoad(x, z, roadDirection, roadClass, removeRoad);
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
            TryPlaceBuilding(parcel.id, buildingBlueprint, facade, roof, buildingFloors, entrance, storefront == "none" ? null : storefront, interior);
        }

        private void AddService(int x, int z)
        {
            CityForgeParcel parcel = ParcelAt(x, z);
            if (parcel == null) { SetMessage("Add a lot first."); return; }
            CityForgeChunkPlan next = draft.Clone();
            next.services.Add(new CityForgeService { id = NextId("service"), type = serviceType, parcelId = parcel.id });
            Apply(next, "Service added.");
        }

        private void AddCityworks(int x, int z)
        {
            CityForgeParcel parcel = ParcelAt(x, z);
            if (parcel == null) { SetMessage("Add a lot first."); return; }
            CityForgeChunkPlan next = draft.Clone();
            LandmarkChoice choice = SelectedLandmarkChoice(parcel.zone);
            next.landmarks.Add(new CityForgeLandmark { id = NextId("landmark"), classId = choice.classId, outcomeId = choice.outcomeId, parcelId = parcel.id, state = "unavailable", stableSeed = Seed("landmark") });
            Apply(next, "Cityworks site added.");
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
        private void CycleLotZone()
        {
            lotZone = (CityForgeZone)(((int)lotZone + 1) % 6);
            string[] preferred = { "walk-up", "workshop", "corner-shop", "care-station", "studio", "garden" }; buildingBlueprint = preferred[(int)lotZone];
            landmarkClass = landmarkOutcome = string.Empty;
            RefreshUi();
        }
        private void CycleBuildingKit()
        {
            string[] options = { "walk-up", "courtyard-home", "tower-residence", "live-work-block", "workshop", "repair-bay", "clinic", "laundry", "depot", "small-office", "corner-shop", "market-hall", "cafe", "exchange", "kiosk", "care-station", "library-room", "utility-office", "community-hall", "screening-room", "studio", "venue", "archive", "performance-space", "garden", "plaza", "water-node", "power-relay", "shelter", "service-yard" };
            int index = Array.IndexOf(options, buildingBlueprint); buildingBlueprint = options[(index + 1 + options.Length) % options.Length]; RefreshUi();
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
            CanvasScaler scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720);
            Image panel = root.AddComponent<Image>(); panel.color = new Color(.025f, .045f, .06f, .96f);
            buildUiSurface = root.GetComponent<RectTransform>(); buildUiSurface.anchorMin = new Vector2(0f, 1f); buildUiSurface.anchorMax = new Vector2(1f, 1f); buildUiSurface.pivot = new Vector2(.5f, 1f); buildUiSurface.sizeDelta = new Vector2(0f, 164f);
            heading = Label(root.transform, "City Forge", 20, new Vector2(12, -10), new Vector2(420, 26));
            contextHint = Label(root.transform, "", 13, new Vector2(436, -10), new Vector2(830, 24)); contextHint.alignment = TextAnchor.MiddleRight;
            message = Label(root.transform, "", 13, new Vector2(12, -142), new Vector2(1240, 18));
            float x = 12f;
            foreach (View choice in new[] { View.Streets, View.Lots, View.Buildings, View.Services, View.Life })
            {
                View local = choice; Button button = Button(root.transform, choice.ToString(), new Vector2(x, -42), new Vector2(84, 30)); button.onClick.AddListener(() => SelectView(local)); viewButtons.Add(choice, button); x += 90f;
            }
            undoButton = Button(root.transform, "Undo", new Vector2(472, -42), new Vector2(64, 30)); undoButton.onClick.AddListener(Undo);
            redoButton = Button(root.transform, "Redo", new Vector2(542, -42), new Vector2(64, 30)); redoButton.onClick.AddListener(Redo);
            Button preview = Button(root.transform, "Review", new Vector2(612, -42), new Vector2(82, 30)); preview.onClick.AddListener(Preview);
            Button commit = Button(root.transform, "Commit", new Vector2(700, -42), new Vector2(82, 30)); commit.onClick.AddListener(Commit);
            Button exit = Button(root.transform, "Return", new Vector2(788, -42), new Vector2(82, 30)); exit.onClick.AddListener(Close);

            roadButton = Button(root.transform, "Road", new Vector2(12, -96), new Vector2(160, 32)); roadButton.onClick.AddListener(CycleRoadClass); RegisterViewControl(View.Streets, roadButton);
            removeRoadButton = Button(root.transform, "Add road", new Vector2(178, -96), new Vector2(112, 32)); removeRoadButton.onClick.AddListener(ToggleRoadRemoval); RegisterViewControl(View.Streets, removeRoadButton);
            roadDirectionButton = Button(root.transform, "Direction", new Vector2(296, -96), new Vector2(144, 32)); roadDirectionButton.onClick.AddListener(CycleRoadDirection); RegisterViewControl(View.Streets, roadDirectionButton);

            zoneButton = Button(root.transform, "Zone", new Vector2(12, -96), new Vector2(160, 32)); zoneButton.onClick.AddListener(CycleLotZone); RegisterViewControl(View.Lots, zoneButton);
            Button merge = Button(root.transform, "Merge lots", new Vector2(178, -96), new Vector2(112, 32)); merge.onClick.AddListener(MergeSelectedLots); RegisterViewControl(View.Lots, merge);
            Button split = Button(root.transform, "Split lot", new Vector2(296, -96), new Vector2(108, 32)); split.onClick.AddListener(SplitSelectedLot); RegisterViewControl(View.Lots, split);

            kitButton = Button(root.transform, "Kit", new Vector2(12, -96), new Vector2(120, 32)); kitButton.onClick.AddListener(CycleBuildingKit); RegisterViewControl(View.Buildings, kitButton);
            facadeButton = Button(root.transform, "Facade", new Vector2(138, -96), new Vector2(130, 32)); facadeButton.onClick.AddListener(CycleFacade); RegisterViewControl(View.Buildings, facadeButton);
            roofButton = Button(root.transform, "Roof", new Vector2(274, -96), new Vector2(126, 32)); roofButton.onClick.AddListener(CycleRoof); RegisterViewControl(View.Buildings, roofButton);
            storefrontButton = Button(root.transform, "Store", new Vector2(406, -96), new Vector2(118, 32)); storefrontButton.onClick.AddListener(CycleStorefront); RegisterViewControl(View.Buildings, storefrontButton);
            entranceButton = Button(root.transform, "Entrance", new Vector2(530, -96), new Vector2(116, 32)); entranceButton.onClick.AddListener(CycleEntrance); RegisterViewControl(View.Buildings, entranceButton);
            Button floorDown = Button(root.transform, "Floor −", new Vector2(652, -96), new Vector2(84, 32)); floorDown.onClick.AddListener(() => ChangeFloors(-1)); RegisterViewControl(View.Buildings, floorDown);
            floorsButton = Button(root.transform, "Floor +", new Vector2(742, -96), new Vector2(100, 32)); floorsButton.onClick.AddListener(() => ChangeFloors(1)); RegisterViewControl(View.Buildings, floorsButton);
            interiorButton = Button(root.transform, "Interior", new Vector2(848, -96), new Vector2(180, 32)); interiorButton.onClick.AddListener(CycleInterior); RegisterViewControl(View.Buildings, interiorButton);
            Button demolish = Button(root.transform, "Demolish", new Vector2(1034, -96), new Vector2(120, 32)); demolish.onClick.AddListener(DemolishLast); RegisterViewControl(View.Buildings, demolish);

            serviceButton = Button(root.transform, "Service", new Vector2(12, -96), new Vector2(180, 32)); serviceButton.onClick.AddListener(CycleService); RegisterViewControl(View.Services, serviceButton);
            landmarkButton = Button(root.transform, "Cityworks", new Vector2(198, -96), new Vector2(160, 32)); landmarkButton.onClick.AddListener(CycleLandmarkOutcome); RegisterViewControl(View.Services, landmarkButton);
            mountButton = Button(root.transform, "Mount", new Vector2(364, -96), new Vector2(170, 32)); mountButton.onClick.AddListener(CycleScreenMount); RegisterViewControl(View.Services, mountButton);
            hostButton = Button(root.transform, "Host", new Vector2(540, -96), new Vector2(120, 32)); hostButton.onClick.AddListener(CycleScreenHost); RegisterViewControl(View.Services, hostButton);
            targetButton = Button(root.transform, "Screen target", new Vector2(666, -96), new Vector2(220, 32)); targetButton.onClick.AddListener(CycleScreenTarget); RegisterViewControl(View.Services, targetButton);
            deviceButton = Button(root.transform, "Device", new Vector2(892, -96), new Vector2(170, 32)); deviceButton.onClick.AddListener(CycleScreenDevice); RegisterViewControl(View.Services, deviceButton);
            Button screen = Button(root.transform, "Place screen", new Vector2(1068, -96), new Vector2(150, 32)); screen.onClick.AddListener(PlaceScreenOnLastBuilding); RegisterViewControl(View.Services, screen);

            situationButton = Button(root.transform, "Situation", new Vector2(12, -96), new Vector2(250, 32)); situationButton.onClick.AddListener(CycleSituationTemplate); RegisterViewControl(View.Life, situationButton);
            situationStateButton = Button(root.transform, "Situation state", new Vector2(268, -96), new Vector2(150, 32)); situationStateButton.onClick.AddListener(CycleSituationState); RegisterViewControl(View.Life, situationStateButton);
            Button renovate = Button(root.transform, "Renovate", new Vector2(424, -96), new Vector2(140, 32)); renovate.onClick.AddListener(RenovateLast); RegisterViewControl(View.Life, renovate);
            buildUiButtons.AddRange(root.GetComponentsInChildren<Button>(true));
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

        private string ViewHint(View value)
        {
            switch (value)
            {
                case View.Streets:
                    return removeRoad
                        ? "Choose a road side, then click any grid cell to erase it · the lit bar previews the road · drag to pan · scroll to zoom"
                        : "Choose a road side, then click any grid cell to place it · the lit bar previews the road · drag to pan · scroll to zoom";
                case View.Lots: return "Click a cell to make a lot · Shift-click builds a footprint";
                case View.Buildings: return "Click an empty lot to place the selected building kit";
                case View.Services: return "Click a lot to add service · choose a valid host before placing a screen";
                default: return "Click a Cityworks lot to add or advance its authored situation";
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
                // A locked pointer reports at the center of the display. Put
                // the visible control at that real target rather than leaving
                // a lower-corner button that cannot receive the click.
                launcherButton.interactable = editingEnabled && !launcherUnavailable;
                SetButtonLabel(launcherButton, launcherUnavailable ? "CITY FORGE\nUNAVAILABLE" : !launcherReady ? "PREPARING CITY" : launcherCursorLocked ? "BUILD CITY\nB" : "BUILD CITY");
            }
            if (launcherStatus)
                launcherStatus.text = launcherUnavailable ? launcherUnavailableMessage : !launcherReady ? "City Forge is preparing your starter ring…" : launcherCursorLocked ? "Click or press B to open City Forge" : "Open City Forge · B";
        }

        private void ConfigureLauncherLayout(bool cursorLocked)
        {
            if (launcherButton)
            {
                RectTransform buttonRect = launcherButton.GetComponent<RectTransform>();
                if (buttonRect)
                {
                    buttonRect.anchorMin = cursorLocked ? new Vector2(.5f, .5f) : Vector2.zero;
                    buttonRect.anchorMax = buttonRect.anchorMin;
                    buttonRect.pivot = cursorLocked ? new Vector2(.5f, .5f) : Vector2.zero;
                    buttonRect.anchoredPosition = cursorLocked ? Vector2.zero : new Vector2(22f, 22f);
                    buttonRect.sizeDelta = cursorLocked ? new Vector2(224f, 58f) : new Vector2(188f, 52f);
                }
            }
            if (launcherStatus)
            {
                RectTransform statusRect = launcherStatus.GetComponent<RectTransform>();
                if (statusRect)
                {
                    statusRect.anchorMin = cursorLocked ? new Vector2(.5f, .5f) : Vector2.zero;
                    statusRect.anchorMax = statusRect.anchorMin;
                    statusRect.pivot = cursorLocked ? new Vector2(.5f, .5f) : Vector2.zero;
                    statusRect.anchoredPosition = cursorLocked ? new Vector2(0f, 44f) : new Vector2(22f, 80f);
                    statusRect.sizeDelta = cursorLocked ? new Vector2(360f, 24f) : new Vector2(420f, 24f);
                }
                launcherStatus.alignment = cursorLocked ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
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
            if (!launcherCanvas || !launcherCanvas.gameObject.activeInHierarchy || !launcherButton || !launcherButton.interactable)
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
            RectTransform rect = launcherButton ? launcherButton.GetComponent<RectTransform>() : null;
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
            return buildUiSurface && buildUiSurface.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(buildUiSurface, screenPosition, null);
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
            if (contextHint) contextHint.text = ViewHint(view);
            if (undoButton) undoButton.interactable = atomicUndo.Count > 0;
            if (redoButton) redoButton.interactable = atomicRedo.Count > 0;
            foreach (KeyValuePair<View, Button> pair in viewButtons) if (pair.Value) pair.Value.GetComponent<Image>().color = pair.Key == view ? new Color(.63f, .82f, .35f, 1f) : new Color(.23f, .34f, .25f, 1f);
            foreach (KeyValuePair<View, List<GameObject>> pair in viewControls)
                foreach (GameObject control in pair.Value)
                    if (control) control.SetActive(pair.Key == view);
            SetButtonLabel(roadButton, "Road: " + RoadClassLabel(roadClass)); SetButtonLabel(removeRoadButton, removeRoad ? "Erase roads" : "Place roads"); SetButtonLabel(roadDirectionButton, "Road side: " + DirectionLabel(roadDirection)); SetButtonLabel(zoneButton, "Zone: " + lotZone);
            SetButtonLabel(kitButton, "Kit: " + buildingBlueprint); SetButtonLabel(facadeButton, "Facade: " + facade); SetButtonLabel(roofButton, "Roof: " + roof); SetButtonLabel(storefrontButton, "Store: " + storefront); SetButtonLabel(entranceButton, "Entry: " + entrance); SetButtonLabel(floorsButton, "Floor + (" + buildingFloors + ")"); SetButtonLabel(interiorButton, "Interior: " + (string.IsNullOrEmpty(interior) ? "none" : interior));
            string host = screenHostMode == 0 ? "building" : screenHostMode == 1 ? "site" : "road";
            string target = string.IsNullOrEmpty(screenHostId) ? "choose target" : screenHostId;
            SetButtonLabel(serviceButton, "Service: " + serviceType); SetButtonLabel(landmarkButton, "Cityworks: " + (string.IsNullOrEmpty(landmarkOutcome) ? "choose" : landmarkOutcome)); SetButtonLabel(mountButton, "Mount: " + screenMount); SetButtonLabel(hostButton, "Host: " + host); SetButtonLabel(targetButton, "Screen target: " + target); SetButtonLabel(deviceButton, "Device: " + screenDevice); SetButtonLabel(situationButton, "Situation: " + (string.IsNullOrEmpty(situationTemplate) ? "choose" : situationTemplate)); SetButtonLabel(situationStateButton, "State: " + situationState);
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
            if (!focused) { ResetLauncherPointerActivation(); CancelBuildUiPointerActivation(); }
        }
        private void OnApplicationPause(bool paused)
        {
            if (paused) { ResetLauncherPointerActivation(); CancelBuildUiPointerActivation(); }
        }
        private void OnDisable() { ResetLauncherPointerActivation(); ResetBuildUiPointerActivation(); }
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
