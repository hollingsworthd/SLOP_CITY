using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>Endless city jobs, persistent progression, scenery interactions and physical transport.</summary>
    public sealed partial class SlopCitySandbox : MonoBehaviour
    {
        private const string SaveKey = "SlopCity.Sandbox.Progress.v1";
        private const float Reach = 3.4f;
        private readonly List<SlopCityInteractable> sites = new List<SlopCityInteractable>();
        private readonly List<ContractStep> steps = new List<ContractStep>();
        private readonly HashSet<string> discoveries = new HashSet<string>();
        private Transform user;
        private SlopCityPlayerInput input;
        private Camera activeCamera;
        private Action<string> feedback,npcDialogue;
        private bool npcDialogueActive;
        private Renderer[] scenery = Array.Empty<Renderer>();
        private Progress progress = new Progress();
        private bool persistenceUnavailable;
        private bool recoverySave;
        private bool unreadableRecoverySave;
        private readonly SlopCityMunicipalLog municipalLog = new SlopCityMunicipalLog();
        private bool municipalOperationActive;
        private string municipalOperationMessage;
        private SlopCityInteractable municipalTransitSite;
        public SlopCityMunicipalLog MunicipalLog => municipalLog;
        public SlopCityMatrixHunt MatrixHunt { get; private set; }
        public SlopCityMunicipalState MunicipalState => CaptureMunicipalState(null);
        public event Action<string, string> MunicipalReportRequested;
        public string BuildMunicipalReport() => municipalLog.FormatRecent();
#if UNITY_EDITOR
        // Fault injection stays at the actual storage boundary; gameplay and serialization remain production code.
        internal static Action<string, string> ProgressStoreForTesting;
#endif
        private int stepIndex;
        private int contractNumber;
        private string currentJobId, currentJobOutcome, lastJobOutcome;
        private readonly Dictionary<string, int> jobHistory = new Dictionary<string, int>();
        private readonly Dictionary<string, SlopCityInteractable.WorkState> workArchive = new Dictionary<string, SlopCityInteractable.WorkState>();
        private readonly Dictionary<string, SlopCityInteractable> workedSites = new Dictionary<string, SlopCityInteractable>();
        private const int MaximumWorkRecords = 2048;
        private SlopCityInteractable pendingTravelStep;
        private SlopCityInteractable pendingElevatorStep;
        private SlopCityElevator pendingElevator;
        private int pendingElevatorFloor;
        private readonly List<SlopCityJobDefinition> availableJobs = new List<SlopCityJobDefinition>();
        private string contractTitle;
        private string cargo;
        private float boostUntil;
        private float nextTargetAt;
        private float nextRefreshAt;
        private Target aimed;
        private bool initialized;
        private bool legacyStreetInteractionsPrepared;
        private static readonly RaycastHit[] feetRayHits = new RaycastHit[24];
        private static readonly RaycastHit[] driveSupportHits = new RaycastHit[16];
        private readonly RaycastHit[] targetRayHits = new RaycastHit[96];
        private SlopCityInteractable vehicle;
        private SlopCityInteractable restingAt;
        private Transform transitDestination;
        private float transitRemaining;
        private string transitLabel;
        private Vector3 savedCameraOffset;
        private float vehicleSpeed;
        private Vector3 vehicleLocalCenter;
        private Vector3 vehicleHalfExtents;
        private Vector3 vehicleEntryPosition;
#if (UNITY_WEBGL && !UNITY_EDITOR) || SLOP_CITY_WEB_INTERACTION_TEST
        private const float WebSceneryCellSize = 8f;
        private readonly Dictionary<Vector3Int, List<int>> webSceneryGrid = new Dictionary<Vector3Int, List<int>>();
        private readonly List<int> webSceneryFallback = new List<int>();
        private readonly List<int> webSceneryCandidates = new List<int>();
        private readonly Dictionary<Renderer, Bounds> webAuthoredBounds = new Dictionary<Renderer, Bounds>();
        private Bounds[] webSceneryBounds = Array.Empty<Bounds>();
        private Bounds[] webSceneryLocalBounds = Array.Empty<Bounds>();
        private bool[] webSceneryHasLocalBounds = Array.Empty<bool>();
        private bool[] webSceneryMoves = Array.Empty<bool>();
        private int[] webScenerySeen = Array.Empty<int>();
        private int webSceneryQuery;
        private bool webSceneryReady, webSceneryWarning;
        private const int WebTargetHitCapacity = 384;
        private readonly RaycastHit[] webTargetHits = new RaycastHit[WebTargetHitCapacity];
        private Camera webTargetCamera;
        private Ray webTargetRay;
        private int webTargetFrame = -1;
        private Matrix4x4 webTargetTransform;
#endif

        public Action<Vector3, float> TeleportRequested;
        public Action<bool> MovementOverrideRequested;
        public bool IsDriving => vehicle;
        public bool IsResting => restingAt;
        public bool IsTravelling => transitRemaining > 0f;
        public int Credits => progress.credits;
        public int Experience => progress.experience;
        public int Salvage => progress.salvage;
        public int CompletedContracts => progress.completed;
        public int WaterSupply => progress.water;
        public int Recycling => progress.recycling;
        public int FieldReadings => progress.fieldReadings;
        public int RareFindCount => progress.rareFinds;
        public int AvailableJobTypes => availableJobs.Count;
        public string CurrentJobId => currentJobId;
        // An assignment owns its step objects across ordinary and unique phases.
        // A repeated catalog job receives new steps, without changing the save format.
        public object CurrentJobAssignment => HasWaypoint ? steps[0] : null;
        public string CurrentJobTitle => contractTitle ?? "City work";
        public string CurrentJobOutcome => currentJobOutcome ?? "Find work at a street terminal.";
        public string CargoLabel => cargo ?? "";
        public int CompletedJobTypes => jobHistory.Count;
        public int JobCompletions(string id) => id != null && jobHistory.TryGetValue(id, out int count) ? count : 0;
        public string JobCoverageText => CompletedJobTypes + "/" + SlopCityJobCatalog.All.Length + " JOB TYPES COMPLETED";
        public string LastJobOutcome => lastJobOutcome ?? "";
        public int Rank => 1 + progress.experience / 300;
        public SlopCityInteractable ObjectiveTarget => HasWaypoint ? steps[stepIndex].site : null;
        private float MealSpeedMultiplier => MatrixHunt ? MatrixHunt.MealSpeedMultiplier : 1f;
        public float SpeedMultiplier => Mathf.Max(Time.time < boostUntil ? 1.3f : 1f,MealSpeedMultiplier);
        public bool HasWaypoint => steps.Count > stepIndex && steps[stepIndex].site;
        public Vector3 WaypointPosition => TryUniqueResidentWaypoint(out Vector3 residentPosition, out _) ? residentPosition : HasWaypoint ? steps[stepIndex].site.InteractionPoint : user ? user.position : Vector3.zero;
        public string WaypointLabel => TryUniqueResidentWaypoint(out _, out string residentLabel) ? residentLabel : HasWaypoint ? steps[stepIndex].site.DisplayName : "Use the city and check a dispatch terminal";
        public float WaypointDistance => user && HasWaypoint ? Vector3.Distance(user.position, WaypointPosition) : 0f;
        public string ObjectiveAction => HasWaypoint ? steps[stepIndex].instruction : "Use the city and check a dispatch terminal";
        public string ProgressText => CompletedContracts + " JOBS  /  " + Salvage + " PARTS  /  " + WaterSupply + " WATER";
        public string ObjectiveText
        {
            get
            {
                if (!initialized) return "TUNING THE RECEIVER…";
                if (!HasWaypoint) return "OPEN FREQUENCY / Use the city. A dispatch terminal can file your field readings.";
                ContractStep step = steps[stepIndex];
                return "CITY JOB / " + contractTitle
                    + "\n" + step.instruction + "  [" + (stepIndex + 1) + "/" + steps.Count + "]"
                    + "\nFOR THE CITY / " + currentJobOutcome
                    + (string.IsNullOrEmpty(cargo) ? "" : "\nCARRYING: " + cargo);
            }
        }

        [Serializable] private sealed class Progress
        {
            public int credits;
            public int experience;
            public int salvage;
            public int completed;
            public int water, recycling, fieldReadings, rareFinds;
            public string[] discovered = Array.Empty<string>();
            public string activeJobId, cargo;
            public int activeStep, dispatchCursor;
            public string[] activeSites = Array.Empty<string>();
            public JobRecord[] jobs = Array.Empty<JobRecord>();
            public WorkRecord[] work = Array.Empty<WorkRecord>();
            public InventoryEntry[] inventory = Array.Empty<InventoryEntry>();
            public InventoryEntry[] pawnStock = Array.Empty<InventoryEntry>();
            public SalvageRecord[] objects = Array.Empty<SalvageRecord>();
            public UniqueJobState uniqueJobs;
        }

        [Serializable] private sealed class JobRecord { public string id; public int completed; }
        [Serializable] private sealed class WorkRecord { public string id; public SlopCityInteractable.WorkState state; }

        private sealed class ContractStep
        {
            public SlopCityInteractable site;
            public string instruction;
            public SlopCityJobAction action;
        }

        private struct Target
        {
            public Transform transform;
            public SlopCityInteractable interaction;
            public Renderer renderer;
            public Vector3 point;
            public bool isNpc;
            public int npcIndex;
        }

        public void Initialize(Transform player, Action<string> onFeedback = null, Action<string> onNpcDialogue = null)
        {
            if (initialized)
            {
                user = player; input = player ? player.GetComponent<SlopCityPlayerInput>() : null;
                if (onFeedback != null) feedback = onFeedback;
                if (onNpcDialogue != null) npcDialogue = onNpcDialogue;
                return;
            }
            user = player;
            feedback = onFeedback; npcDialogue = onNpcDialogue;
            input = player ? player.GetComponent<SlopCityPlayerInput>() : null;
            try
            {
                progress = ReadProgress(SaveKey);
            }
            catch (Exception exception)
            {
                // Preserve the original bytes. A damaged/oversized save must never
                // be silently replaced by a new session's empty progress.
                recoverySave = true;
                Debug.LogWarning("SLOP CITY: Original progress preserved; using a separate recovery save: " + exception.Message);
                try { progress = ReadProgress(SaveKey + ".recovery"); }
                catch (Exception) { unreadableRecoverySave = persistenceUnavailable = true; progress = new Progress(); }
                feedback?.Invoke(unreadableRecoverySave
                    ? "The saved progress could not be read and has been preserved. Progress in this session cannot be saved."
                    : "The original save could not be read and has been preserved. This session uses separate recovery progress.");
            }
            progress.credits = Mathf.Max(0, progress.credits);
            progress.experience = Mathf.Max(0, progress.experience);
            progress.salvage = Mathf.Max(0, progress.salvage);
            progress.water = Mathf.Clamp(progress.water, 0, 3);
            progress.recycling = Mathf.Clamp(progress.recycling, 0, 12);
            progress.fieldReadings = Mathf.Clamp(progress.fieldReadings, 0, 24);
            progress.rareFinds = Mathf.Max(0, progress.rareFinds);
            progress.completed = Mathf.Max(0, progress.completed);
            if (progress.discovered != null) foreach (string id in progress.discovered)
                if (!string.IsNullOrEmpty(id)) discoveries.Add(id);
            if (progress.jobs != null)
                foreach (JobRecord record in progress.jobs)
                    if (record != null && record.completed > 0 && Array.Exists(SlopCityJobCatalog.All, job => job.id == record.id))
                        jobHistory[record.id] = Mathf.Max(JobCompletions(record.id), record.completed);
            if (progress.work != null)
                foreach (WorkRecord record in progress.work)
                    if (record != null && !string.IsNullOrEmpty(record.id) && record.id.Length < 2048 && workArchive.Count < MaximumWorkRecords)
                        workArchive[record.id] = record.state;
            SlopCityWorldUtilities.Ensure(player);
            InitializeSalvage();
            InitializeUniqueJobs();
            MigrateLegacyUniqueCargo();
            RefreshSalvage();
            RefreshSites();
            initialized = true;
            contractNumber = progress.completed;
            if (!TryRestoreUniqueAssignment() && !RestoreAssignment()) NextContract(false);
        }

        public void Configure(Transform player) { Initialize(player); }

        public void StartMatrixSearch(SlopCityFirstPerson player)
        {
            MatrixHunt = GetComponent<SlopCityMatrixHunt>();
            if (!MatrixHunt) MatrixHunt = gameObject.AddComponent<SlopCityMatrixHunt>();
            MatrixHunt.Configure(player);
        }

        private static Progress ReadProgress(string key)
        {
            string json = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(json)) return new Progress();
            if (json.Length > 1024 * 1024 || !json.TrimStart().StartsWith("{", StringComparison.Ordinal))
                throw new FormatException("Progress data exceeds its supported format or size.");
            Progress value = JsonUtility.FromJson<Progress>(json);
            if (value == null || !ValidDiscoveryArchive(value.discovered) || !ValidSalvageArchive(value))
                throw new FormatException("Progress contains an invalid discovery archive.");
            return value;
        }

        // The JSON byte/character budget bounds total entries; do not reject a
        // valid legacy archive merely because its next discovery crosses a count.
        private static bool ValidDiscoveryArchive(string[] ids)
            => ids == null || !Array.Exists(ids, id => id != null && id.Length > 2048);

        private static int AddBounded(int value, int amount) => (int)Math.Max(0L, Math.Min(int.MaxValue, (long)value + amount));

        public void Tick(float deltaTime)
        {
            if (!initialized || !user || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            if (MatrixHunt) MatrixHunt.Tick();
            TickUniqueJobs();
            // Unity destroyed-object references compare equal to null but remain
            // managed objects. Release their movement/camera ownership as well.
            if (!ReferenceEquals(vehicle, null) && !vehicle) StopDriving(true);
            if (!ReferenceEquals(restingAt, null) && !restingAt) StopResting();
            if (vehicle) Drive(deltaTime);
            if (IsTravelling)
            {
                transitRemaining = Mathf.Max(0f, transitRemaining - deltaTime);
                if (transitRemaining <= 0f)
                {
                    bool arrived = transitDestination && TeleportRequested != null;
                    Vector3 destinationPosition = transitDestination ? transitDestination.position : user.position;
                    if (arrived)
                        TeleportRequested(transitDestination.position, transitDestination.eulerAngles.y);
                    bool arrivalObserved = arrived && (user.position - destinationPosition).sqrMagnitude <= 1f;
                    RecordMunicipal(municipalTransitSite, "Travel", arrivalObserved ? SlopCityMunicipalOutcome.Completed : SlopCityMunicipalOutcome.Blocked,
                        CaptureMunicipalState(municipalTransitSite), CaptureMunicipalState(municipalTransitSite),
                        arrivalObserved ? "Passenger position observed within one metre of destination." : "Destination arrival not observed; no completed journey established.");
                    municipalTransitSite = null;
                    MovementOverrideRequested?.Invoke(false);
                    transitDestination = null;
                    nextTargetAt = 0f;
                    Say(arrivalObserved ? "ARRIVED / " + transitLabel + ". Exit onto the platform; dispatch remains active."
                        : "Transit destination unavailable. You are back on foot; choose another route.");
                    if (arrivalObserved && pendingTravelStep && IsContractTarget(pendingTravelStep)
                        && steps[stepIndex].action == SlopCityJobAction.Travel) AdvanceContract();
                    pendingTravelStep = null;
                }
            }
            if (restingAt && input && input.ReadMovement().sqrMagnitude > .01f) StopResting();
            if (Time.time >= nextRefreshAt)
            {
                nextRefreshAt = Time.time + 15f;
                // Destroyed or unloaded destinations never strand an active contract.
                bool lost = steps.Count == 0;
                for (int i = stepIndex; i < steps.Count; i++)
                    if (!steps[i].site || !steps[i].site.isActiveAndEnabled || !steps[i].site.gameObject.activeInHierarchy) { lost = true; break; }
                if (lost) { RefreshSites(); NextContract(false); }
            }
        }

#if (UNITY_WEBGL && !UNITY_EDITOR) || SLOP_CITY_WEB_INTERACTION_TEST
        // First-person prompts and E dispatch use the same nearest surface as
        // generic city interactions. Instanced crowd targets stay with TryTalk.
        public Transform BrowserTarget(Camera camera, bool refresh = false)
        {
            if (refresh)
            {
                webTargetFrame = -1;
                nextTargetAt = 0f;
                Physics.SyncTransforms();
            }
            Target target = FindTarget(camera);
            return target.isNpc ? null : target.transform;
        }
#endif

        public bool IsLookingAtNpc(Camera camera) => FindTarget(camera).isNpc;

        public string Prompt(Camera camera)
        {
            if (IsTravelling) return "CANCEL " + transitLabel + " / ARRIVING IN " + Mathf.CeilToInt(transitRemaining) + "s";
            if (vehicle) return "EXIT " + vehicle.DisplayName + (input && input.UseTouchControls ? " / LEFT STICK DRIVE / " : " / WASD DRIVE / ")
                + Mathf.RoundToInt(Mathf.Abs(vehicleSpeed) * 3.6f) + " KM/H";
            if (restingAt) return "STAND UP / rested: sprint boost active";
            Target target = FindTarget(camera);
            if (!target.transform) return "";
            if (target.isNpc && SlopCityCrowd.Instance)
            {
                string contact = MatrixHunt ? MatrixHunt.ContactPrompt(target.npcIndex) : "";
                return string.IsNullOrEmpty(contact) ? SlopCityCrowd.Instance.TalkPrompt(target.npcIndex) : contact;
            }
            SlopCityPawnCounter pawn = target.transform.GetComponentInParent<SlopCityPawnCounter>();
            if (pawn) return "PAWN SHOP / SELL RECOVERED INVENTORY";
            SlopCitySteakTable steak = target.transform.GetComponentInParent<SlopCitySteakTable>();
            if (steak) return steak.Prompt + " / " + steak.VenueName;
            SlopCityInteractable item = target.interaction;
            SlopCityElevatorButton elevatorButton = target.transform.GetComponentInParent<SlopCityElevatorButton>();
            if (elevatorButton) return elevatorButton.Prompt;
            if (!item) return "CHECK CONDITION / " + Readable(target.transform.name);
            if (IsContractTarget(item)) return steps[stepIndex].instruction.ToUpperInvariant() + " / " + steps[stepIndex].site.DisplayName;
            if (item.Kind == SlopCityInteractionKind.Terminal && item.SupplyStock > 0)
                return "COLLECT DELIVERED SUPPLIES / " + item.SupplyStock + " KITS / " + item.DisplayName;
            if (IsLogCabinet(item) && FieldReadings == 0) return "READ MUNICIPAL LOG / OPTIONAL";
            string verb;
            switch (item.Kind)
            {
                case SlopCityInteractionKind.Bench: verb = "REST ON"; break;
                case SlopCityInteractionKind.Light: verb = item.IsOn ? "SWITCH OFF" : "SWITCH ON"; break;
                case SlopCityInteractionKind.Bin: verb = item.CooldownRemaining > 0 ? "CHECK / RESTOCK " + Mathf.CeilToInt(item.CooldownRemaining) + "s" : "SCAVENGE"; break;
                case SlopCityInteractionKind.Collectible: verb = "SALVAGE"; break;
                case SlopCityInteractionKind.Screen:
                    var broadcast = item.GetComponentInParent<SlopCityCityScreen>() ?? item.GetComponentInChildren<SlopCityCityScreen>();
                    verb = broadcast && broadcast.AudioOnly ? "PLAY / PAUSE RADIO" : "TUNE CHANNEL"; break;
                case SlopCityInteractionKind.Terminal: verb = FieldReadings > 0 ? "FILE FIELD READINGS" : Salvage > 0 ? "INSTALL REPAIR PART" : "RUN DIAGNOSTICS"; break;
                case SlopCityInteractionKind.Vending: verb = "COLLECT PUBLIC SUPPLIES"; break;
                case SlopCityInteractionKind.Car: verb = "DRIVE"; break;
                case SlopCityInteractionKind.GasPump: verb = "SERVICE PUMP / TUNE AD"; break;
                case SlopCityInteractionKind.Building: verb = item.Destination ? "TAKE LIFT" : "CHECK IN"; break;
                case SlopCityInteractionKind.Subway: verb = item.Destination ? "TRAVEL" : "TUNE TRAIN TV"; break;
                case SlopCityInteractionKind.Plant: verb = WaterSupply > 0 ? "WATER" : "CHECK WATER NEEDS"; break;
                case SlopCityInteractionKind.Hydrant: verb = "TEST PRESSURE / REFILL"; break;
                case SlopCityInteractionKind.Fountain: verb = "REFILL WATER / REFRESH"; break;
                case SlopCityInteractionKind.RareFind: verb = "RECOVER RARE TRANSMISSION"; break;
                default: verb = "READ / CHECK CONDITION"; break;
            }
            return verb + " " + item.DisplayName + " / " + item.StateDescription;
        }

        public bool Interact(Camera camera)
        {
            activeCamera = camera;
            if (IsTravelling) { CancelTravel(); Say("Journey cancelled. Back on the platform."); return true; }
            if (vehicle) { StopDriving(); return true; }
            if (restingAt) { StopResting(); Say("Back on your feet. Rested sprint boost: 45 seconds."); return true; }
            nextTargetAt = 0f;
            Target target = FindTarget(camera);
            if (!target.transform) return false;
#if (UNITY_WEBGL && !UNITY_EDITOR) || SLOP_CITY_WEB_INTERACTION_TEST
            // The first-person controller owns door toggles. Never turn a door
            // leaf into a generic inspect item when called directly.
            if (target.transform.GetComponentInParent<SlopCityDoor>()) return false;
#endif
            if (target.isNpc && SlopCityCrowd.Instance)
            {
                if (!SlopCityCrowd.Instance.IsResidentPresented(target.npcIndex)) return false;
                bool previousDialogue=npcDialogueActive;
                npcDialogueActive=true;
                try
                {
                    if (TryUniqueResidentInteraction(camera, target.npcIndex, out bool barterCompleted))
                    { if (barterCompleted) AdvanceContract(); else Save(); return true; }
                    if (MatrixHunt && MatrixHunt.TryContact(target.npcIndex, out string contact))
                    {
                        SlopCityCrowd.Instance.TryTalk(target.npcIndex, camera.transform.position, out _);
                        Say(contact); return true;
                    }
                    if (SlopCityCrowd.Instance.TryTalk(target.npcIndex, camera.transform.position, out string line)) Say(line);
                    return true;
                }
                finally { npcDialogueActive=previousDialogue; }
            }
            SlopCityPawnCounter pawn = target.transform.GetComponentInParent<SlopCityPawnCounter>();
            if (pawn) { OpenInventory(pawn); return true; }
            SlopCitySteakTable steak = target.transform.GetComponentInParent<SlopCitySteakTable>();
            if (steak)
            {
                steak.Use(out string meal, out bool ate);
                if (ate && MatrixHunt) meal = MatrixHunt.OnSteakEaten(steak);
                Say(meal); return true;
            }
            SlopCityInteractable item = target.interaction;
            SlopCityElevatorButton elevatorButton = target.transform.GetComponentInParent<SlopCityElevatorButton>();
            if (elevatorButton)
            {
                if (item) item.Pulse(target.renderer);
                SlopCityMunicipalState before = CaptureMunicipalState(item);
                RecordMunicipal(item, "ElevatorControl", SlopCityMunicipalOutcome.Attempted, before, before, "Control requested.");
                string response = elevatorButton.Activate(this);
                RecordMunicipal(item, "ElevatorControl", SlopCityMunicipalOutcome.Requested, before, CaptureMunicipalState(item), response);
                Say(response);
                return true;
            }
            if (!item)
            {
                item = target.transform.gameObject.AddComponent<SlopCityInteractable>();
                item.Configure(SlopCityInteractionKind.Inspect, Readable(target.transform.name), false);
                item.ConfigureInteractionPoint(target.point);
            }
            if (TryInteractUniqueDevice(item, camera, out bool uniqueCompleted))
            { if (uniqueCompleted) AdvanceContract(); else Save(); return true; }
            item.Pulse(target.renderer);
            if (IsContractTarget(item))
            {
                ContractStep step = steps[stepIndex];
                if (ExecuteAction(step.site, step.action, camera, true)) AdvanceContract();
                return true;
            }
            Perform(item, camera);
            return true;
        }

        public void Restart()
        {
            ReturnHome();
            NextContract(false);
            Say("New dispatch. Your completed work, supplies and rare finds are saved.");
        }

        public void RerollObjective()
        {
            if (!initialized) return;
            RefreshSites();
            NextContract(false);
            Say("NEW CITY JOB / " + CurrentJobTitle + ". " + CurrentJobOutcome);
        }

        public void ReturnHome() { if (!ReferenceEquals(vehicle, null)) StopDriving(true); StopResting(); CancelTravel(); }

        public void CancelTravel()
        {
            ClearElevatorTravel();
            if (!IsTravelling) return;
            RecordMunicipal(municipalTransitSite, "Travel", SlopCityMunicipalOutcome.Incomplete,
                CaptureMunicipalState(municipalTransitSite), CaptureMunicipalState(municipalTransitSite), "Player cancelled before arrival.");
            municipalTransitSite = null;
            transitRemaining = 0f;
            transitDestination = null;
            pendingTravelStep = null;
            MovementOverrideRequested?.Invoke(false);
        }

        public void BeginElevatorTravel(SlopCityInteractable route, SlopCityElevator elevator, int destinationFloor)
        {
            if (!route || !elevator || (!IsUniqueElevatorTravel(route) && (!IsContractTarget(route) || steps[stepIndex].action != SlopCityJobAction.Travel))) return;
            ClearElevatorTravel();
            pendingElevatorStep = route;
            pendingElevator = elevator;
            pendingElevatorFloor = destinationFloor;
            elevator.Arrived += OnElevatorArrived;
        }

        private void OnElevatorArrived(SlopCityElevator elevator, int floor)
        {
            if (elevator != pendingElevator || floor != pendingElevatorFloor || !elevator.PlayerInside) return;
            SlopCityInteractable route = pendingElevatorStep;
            ClearElevatorTravel();
            NotifyUniqueElevatorArrival(route, elevator);
            Save();
            if (!route || !IsContractTarget(route) || steps[stepIndex].action != SlopCityJobAction.Travel) return;
            SlopCityMunicipalState before = CaptureMunicipalState(route);
            route.RecordService("Arrived by elevator");
            RecordMunicipal(route, "Travel", SlopCityMunicipalOutcome.Completed, before, CaptureMunicipalState(route), "Elevator arrival event; passenger inside at the requested floor.");
            AdvanceContract();
        }

        private void ClearElevatorTravel()
        {
            if (pendingElevator) pendingElevator.Arrived -= OnElevatorArrived;
            pendingElevator = null;
            pendingElevatorStep = null;
        }

        /// <summary>Only a physical passenger arrival can finish a station's travel stage.</summary>
        public void NotifyMetroArrival(SlopCityInteractable route, Transform destination, SlopCityMetroTrain train)
        {
            if (!route || !destination || route.Destination != destination || !train || !user
                || train.RiderTransform != user || !train.PlayerInside || train.IsMoving) return;
            SlopCityMetroPlatform platform = route.GetComponent<SlopCityMetroPlatform>();
            if (!platform || !platform.World || train.World != platform.World) return;
            int arrival = (platform.StopIndex + 1) % SlopCityMetroWorld.StationCount;
            if (train.CurrentStop != arrival || platform.World.Destinations[arrival] != destination) return;
            bool registered = false;
            foreach (SlopCityMetroTrain owned in platform.World.Trains)
                if (owned == train) { registered = true; break; }
            if (!registered) return;
            NotifyUniqueRiddenArrival(route, destination, train);
            Save();
            if (!IsContractTarget(route) || steps[stepIndex].action != SlopCityJobAction.Travel) return;
            SlopCityMunicipalState before = CaptureMunicipalState(route);
            route.RecordService("Arrived on the scheduled train");
            RecordMunicipal(route, "Travel", SlopCityMunicipalOutcome.Completed, before, CaptureMunicipalState(route),
                "Train arrival event; passenger carried from the assigned platform to its next stop.");
            AdvanceContract();
        }

        private void OnDestroy() { ClearElevatorTravel(); SlopCitySalvageItem.Admitted -= AdmitSalvage; if (uniqueWorld) uniqueWorld.Release(this); }

        public void AwardForInteraction(int credits, int experience = 0, int parts = 0)
        {
            progress.credits = AddBounded(progress.credits, credits);
            progress.experience = AddBounded(progress.experience, experience);
            progress.salvage = AddBounded(progress.salvage, parts);
            Save();
        }

        private void Perform(SlopCityInteractable item, Camera camera)
        {
            if (item.Kind == SlopCityInteractionKind.Terminal && item.SupplyStock > 0)
            {
                if (item.SpendStock())
                {
                    int parts = item.WorkCondition >= 90 ? 2 : 1;
                    progress.salvage = AddBounded(progress.salvage, parts); progress.water = 3;
                    RememberWork(item); Save();
                    Say("DELIVERED KIT / +" + parts + " repair parts and a full water carrier. " + item.SupplyStock + " kits remain here.");
                }
                return;
            }
            if (IsLogCabinet(item) && FieldReadings == 0)
            { MunicipalReportRequested?.Invoke("MUNICIPAL LOG", BuildMunicipalReport()); return; }
            switch (item.Kind)
            {
                case SlopCityInteractionKind.RareFind: Discover(item); return;
                case SlopCityInteractionKind.Car: StartDriving(item, camera); return;
                case SlopCityInteractionKind.Light:
                    ExecuteAction(item, item.IsOn ? SlopCityJobAction.SwitchOff : SlopCityJobAction.SwitchOn, camera, false); return;
                case SlopCityInteractionKind.Bin:
                case SlopCityInteractionKind.Collectible:
                    ExecuteAction(item, SlopCityJobAction.RecoverPart, camera, false); return;
                case SlopCityInteractionKind.Terminal:
                    ExecuteAction(item, FieldReadings > 0 ? SlopCityJobAction.FileReport
                        : Salvage > 0 && item.CooldownRemaining <= 0 ? SlopCityJobAction.Repair : SlopCityJobAction.Diagnose, camera, false); return;
                case SlopCityInteractionKind.Vending: ExecuteAction(item, SlopCityJobAction.Supply, camera, false); return;
                case SlopCityInteractionKind.Bench: ExecuteAction(item, SlopCityJobAction.Rest, camera, false); return;
                case SlopCityInteractionKind.Screen: ExecuteAction(item, SlopCityJobAction.Tune, camera, false); return;
                case SlopCityInteractionKind.GasPump:
                    bool pumpChecked = ExecuteAction(item, SlopCityJobAction.PressureTest, camera, false);
                    Say((pumpChecked ? "Pressure reading packed. " : "") + Tune(item)); return;
                case SlopCityInteractionKind.Building:
                case SlopCityInteractionKind.Subway:
                    ExecuteAction(item, item.Destination ? SlopCityJobAction.Travel : item.Kind == SlopCityInteractionKind.Subway
                        ? SlopCityJobAction.Tune : SlopCityJobAction.ReadNotice, camera, false); return;
                case SlopCityInteractionKind.Plant: ExecuteAction(item, SlopCityJobAction.WaterPlant, camera, false); return;
                case SlopCityInteractionKind.Hydrant:
                    ExecuteAction(item, SlopCityJobAction.FillWater, camera, false);
                    if (item.CooldownRemaining <= 0f) ExecuteAction(item, SlopCityJobAction.PressureTest, camera, false);
                    return;
                case SlopCityInteractionKind.Fountain: ExecuteAction(item, SlopCityJobAction.FillWater, camera, false); return;
                default: ExecuteAction(item, SlopCityJobAction.ReadNotice, camera, false); return;
            }
        }

        private static bool IsLogCabinet(SlopCityInteractable item) => item
            && item.Kind == SlopCityInteractionKind.Terminal
            && item.DisplayName.IndexOf("municipal signal junction", StringComparison.OrdinalIgnoreCase) >= 0;

        // Jobs and free use share the same resource changes and world operations.
        // A dispatch assignment supplies access to its reserved materials, never cash per click.
        private bool ExecuteAction(SlopCityInteractable item, SlopCityJobAction action, Camera camera, bool dispatch)
        {
            SlopCityMunicipalState before = CaptureMunicipalState(item);
            string actionName = action.ToString();
            RecordMunicipal(item, actionName, SlopCityMunicipalOutcome.Attempted, before, before, "Player operation requested.");
            municipalOperationActive = true;
            municipalOperationMessage = null;
            try
            {
                bool completed = ExecuteActionCore(item, action, camera, dispatch);
                SlopCityMunicipalOutcome outcome = completed ? SlopCityMunicipalOutcome.Completed : SlopCityMunicipalOutcome.Blocked;
                if (completed && action == SlopCityJobAction.Tune) outcome = SlopCityMunicipalOutcome.Requested;
                if (!completed && action == SlopCityJobAction.Travel && (IsTravelling || pendingElevator)) outcome = SlopCityMunicipalOutcome.InProgress;
                else if (!completed && action == SlopCityJobAction.Travel && item
                    && (item.GetComponent<SlopCityElevatorButton>() || item.GetComponent<SlopCityMetroPlatform>())) outcome = SlopCityMunicipalOutcome.Requested;
                if (completed && action == SlopCityJobAction.Travel && item && item.Destination
                    && (!user || (user.position - item.Destination.position).sqrMagnitude > 1f)) outcome = SlopCityMunicipalOutcome.Blocked;
                RecordMunicipal(item, actionName, outcome, before, CaptureMunicipalState(item),
                    outcome == SlopCityMunicipalOutcome.Blocked && completed ? "Arrival was not observed at the requested destination."
                    : municipalOperationMessage ?? "Operation returned without a status message.");
                return completed;
            }
            catch (Exception exception)
            {
                RecordMunicipal(item, actionName, SlopCityMunicipalOutcome.Failed, before, CaptureMunicipalState(item), "Operation threw " + exception.GetType().Name + "; completion not established.");
                throw;
            }
            finally { municipalOperationActive = false; municipalOperationMessage = null; }
        }

        private bool ExecuteActionCore(SlopCityInteractable item, SlopCityJobAction action, Camera camera, bool dispatch)
        {
            if (action == SlopCityJobAction.UniqueWork)
                return TryExecuteUniqueJob(item, camera, out bool completed) && completed;
            if (!SlopCityJobCatalog.Supports(item, action)) { Say("This device cannot perform that action."); return false; }
            string result;
            switch (action)
            {
                case SlopCityJobAction.CollectParcel:
                    if (!string.IsNullOrEmpty(cargo)) { Say("Finish the current handoff before taking another parcel."); return false; }
                    cargo = "SEALED PARCEL"; result = "Parcel collected. The receiving terminal will accept the handoff."; break;
                case SlopCityJobAction.DeliverParcel:
                    if (cargo != "SEALED PARCEL") { Say("Collect the parcel before making its delivery."); return false; }
                    cargo = null; item.ReceiveParcel(); result = "Supplies delivered: three work kits are now available at " + item.DisplayName + "."; break;
                case SlopCityJobAction.RecoverPart:
                    if (!dispatch && item.CooldownRemaining > 0f) { Say("No loose parts left here. Public supply stations can restock your kit."); return false; }
                    progress.salvage = Mathf.Max(progress.salvage, Mathf.Min(12, progress.salvage + 1)); item.MarkUsed(60f);
                    result = "Usable component packed. Install it at a service terminal or assigned repair."; break;
                case SlopCityJobAction.Repair:
                    if (Salvage < 1) { Say("One component needed. Recover a part from a bin or supply crate, then return."); return false; }
                    if (!item.RepairDevice()) { Say("This device is already fully serviced. Keep the component for another repair."); return false; }
                    progress.salvage--; item.MarkUsed(45f);
                    result = "SERVICE RESTORED / " + item.DisplayName + ": " + item.StateDescription + "."; break;
                case SlopCityJobAction.CollectRecycling:
                    if (!dispatch && item.CooldownRemaining > 0f) { Say("This collection point is clear."); return false; }
                    progress.recycling = Mathf.Min(12, progress.recycling + 1); item.MarkUsed(60f);
                    result = "Sorted material packed for the recovery terminal."; break;
                case SlopCityJobAction.Recycle:
                    if (Recycling < 1) { Say("Collect a recycling bundle before unloading here."); return false; }
                    int recovered = progress.recycling; progress.recycling = 0;
                    progress.salvage = Mathf.Max(progress.salvage, Mathf.Min(12, progress.salvage + recovered));
                    result = recovered + " recycling bundles processed into usable repair components."; break;
                case SlopCityJobAction.FillWater:
                    progress.water = 3;
                    boostUntil = Mathf.Max(boostUntil, Time.time + (item.WorkCondition >= 90 ? 60f : 25f));
                    result = "Water carrier refilled: three uses. Refreshed for the next leg."; break;
                case SlopCityJobAction.WaterPlant:
                    if (WaterSupply < 1) { Say("Refill your water carrier at a public fountain or hydrant first."); return false; }
                    if (!dispatch && item.CooldownRemaining > 0f) { Say("Already watered. Save the water for another tree."); return false; }
                    if (!item.WaterPlant()) { Say("This planting is still watered. Save your water for a dry planting."); return false; }
                    progress.water--; item.MarkUsed(120f);
                    result = "Roots watered. " + WaterSupply + " water uses remain in your carrier."; break;
                case SlopCityJobAction.PressureTest:
                    if (!RecordReading(item, dispatch)) return false;
                    result = item.DisplayName + " / service pressure " + item.WorkCondition + "%" + (item.WorkCondition >= 90 ? ". Full recovery available at this supply." : ". A replacement part restores full capacity."); break;
                case SlopCityJobAction.SwitchOn:
                case SlopCityJobAction.SwitchOff:
                    bool shouldBeOn = action == SlopCityJobAction.SwitchOn;
                    if (item.IsOn != shouldBeOn) item.ToggleLights();
                    result = item.DisplayName + (shouldBeOn ? " / Power on; path illuminated." : " / Power isolated for service."); break;
                case SlopCityJobAction.Tune:
                    SlopCityCityMedia tuner = SlopCityCityMedia.Instance;
                    if (!tuner || !tuner.TryTuneScreen(item.transform, out result))
                    { Say(tuner ? "No change was made to this display. Choose a working ordinary screen." : "City media is still preparing. Try again when its display is ready."); return false; }
                    RecordReading(item, true); break;
                case SlopCityJobAction.Diagnose:
                    if (!RecordReading(item, dispatch)) return false;
                    result = item.DisplayName + " / " + item.StateDescription + "."; break;
                case SlopCityJobAction.Upload:
                case SlopCityJobAction.FileReport:
                    if (FieldReadings < 1) { Say("Record a device reading or read a service notice before filing a report."); return false; }
                    int readings = progress.fieldReadings; progress.fieldReadings = 0;
                    result = readings + " field readings filed. Municipal service record updated."; break;
                case SlopCityJobAction.Rest:
                    boostUntil = Mathf.Max(boostUntil, Time.time + 45f);
                    if (!dispatch && item.Kind == SlopCityInteractionKind.Bench && camera)
                    {
                        restingAt = item; activeCamera = camera; savedCameraOffset = camera.transform.localPosition;
                        camera.transform.localPosition = new Vector3(savedCameraOffset.x, .95f, savedCameraOffset.z);
                    }
                    result = "Rested. Movement boost active for 45 seconds."; break;
                case SlopCityJobAction.Supply:
                    boostUntil = Mathf.Max(boostUntil, Time.time + 90f);
                    progress.water = 3; progress.salvage = Mathf.Max(progress.salvage, 2);
                    result = "Public work kit restocked: water, repair components and an energy drink. No charge."; break;
                case SlopCityJobAction.CollectSample:
                    if (!string.IsNullOrEmpty(cargo)) { Say("Complete the current handoff before taking a sample."); return false; }
                    cargo = "SEALED SAMPLE"; result = "Sample sealed for the municipal analysis terminal."; break;
                case SlopCityJobAction.DeliverSample:
                    if (cargo != "SEALED SAMPLE") { Say("Collect the sample before delivering it."); return false; }
                    cargo = null; item.ReceiveSample(); result = "Sample accepted: a tested supply kit is available at " + item.DisplayName + "."; break;
                case SlopCityJobAction.ReadNotice:
                    if (!RecordReading(item, dispatch)) return false; result = "LOCAL REQUEST / " + (dispatch ? CurrentJobTitle + ". " + CurrentJobOutcome : item.DisplayName + " / " + item.StateDescription); break;
                case SlopCityJobAction.MaintainSurface:
                    if (!item.MaintainSurface()) { Say("This surface is already clean and ready to use."); return false; }
                    item.MarkUsed(60f); RecordReading(item, true);
                    result = "CLEANED / " + item.DisplayName + ". The usable surface is restored."; break;
                case SlopCityJobAction.Travel:
                    SlopCityMetroPlatform platform = item.GetComponent<SlopCityMetroPlatform>();
                    if (platform) { Say(platform.Status + " Walk aboard through the open doors; trains depart automatically."); return false; }
                    SlopCityElevatorButton liftButton = item.GetComponent<SlopCityElevatorButton>();
                    if (liftButton) { Say(liftButton.Activate(this)); return false; }
                    if (!item.Destination || TeleportRequested == null) { Say("This route is unavailable. Request a different job."); return false; }
                    if (item.Kind == SlopCityInteractionKind.Subway && item.DisplayName.StartsWith("RIDE", StringComparison.OrdinalIgnoreCase))
                    {
                        transitDestination = item.Destination; transitRemaining = 4f; transitLabel = item.DisplayName;
                        municipalTransitSite = item;
                        pendingTravelStep = dispatch ? item : null; MovementOverrideRequested?.Invoke(true);
                        item.RecordService("Journey in progress");
                        Say("DEPARTING / " + transitLabel + ". Arrival in four seconds; USE cancels."); return false;
                    }
                    TeleportRequested(item.Destination.position, item.Destination.eulerAngles.y); nextTargetAt = 0f;
                    if (!user || (user.position - item.Destination.position).sqrMagnitude > 1f)
                    { Say("Arrival was not completed. The journey remains unfinished."); return false; }
                    result = "ARRIVED / " + item.DisplayName + ". Route completed."; break;
                default: return false;
            }
            item.RecordService(result); RememberWork(item);
            // Dispatch progress and its world/resource effects are one saved transaction in AdvanceContract.
            if (!dispatch) Save();
            Say(result); return true;
        }

        private bool RecordReading(SlopCityInteractable item, bool dispatch)
        {
            if (dispatch || item.CooldownRemaining <= 0f)
            {
                progress.fieldReadings = Mathf.Min(24, progress.fieldReadings + 1);
                item.MarkUsed(30f); return true;
            }
            Say("Already checked recently. File outstanding readings at a municipal terminal, or service another object.");
            return false;
        }

        private string Tune(SlopCityInteractable item)
        {
            if (item.Kind == SlopCityInteractionKind.Car && item.GetComponentInParent<SlopCityStreetCars>())
                return "No dashboard broadcast in this car.";
            SlopCityCityMedia media = SlopCityCityMedia.Instance;
            return media ? media.TuneScreen(item.transform) : "Channel tuned. City screens will reconnect when media is available.";
        }

        private bool IsContractTarget(SlopCityInteractable item)
        {
            return item && HasWaypoint && (steps[stepIndex].site == item
                || item.transform.IsChildOf(steps[stepIndex].site.transform));
        }

        private void Discover(SlopCityInteractable item)
        {
            if (!item || item.Kind != SlopCityInteractionKind.RareFind || string.IsNullOrEmpty(item.RareId)) return;
            string id = "rare:" + item.RareId;
            if (discoveries.Add(id))
            {
                progress.rareFinds = AddBounded(progress.rareFinds, 1); AwardForInteraction(45, 60);
                Say("RARE TRANSMISSION / " + item.DisplayName + " / +$45 / +60 XP. " + item.RareStory);
            }
            else Say(item.DisplayName + " / " + item.RareStory + " / Already archived.");
        }

        private void AdvanceContract()
        {
            string completed = steps[stepIndex].instruction;
            stepIndex++;
            if (stepIndex >= steps.Count)
            {
                progress.completed = AddBounded(progress.completed, 1); lastJobOutcome = currentJobOutcome;
                jobHistory[currentJobId] = AddBounded(JobCompletions(currentJobId), 1); Save();
                string outcome = lastJobOutcome;
                NextContract(true);
                Say("JOB COMPLETE / " + outcome + " Next dispatch: " + contractTitle + ".");
            }
            else { Save(); Say(completed + " complete. Next: " + steps[stepIndex].instruction + " at " + steps[stepIndex].site.DisplayName + "."); }
        }

        public string DispatchContact()
        {
            if (!HasWaypoint) { RefreshSites(); NextContract(false); }
            return HasWaypoint ? "STREET CONTACT / " + CurrentJobTitle + ". " + ObjectiveAction + " at " + WaypointLabel + ". " + CurrentJobOutcome
                : "STREET CONTACT / No reachable work is available yet. Try a public supply desk.";
        }

        // Explicit selection supports dispatch controls and focused validation; routine completion uses fair rotation.
        public bool SelectJob(string id)
        {
            SlopCityJobDefinition definition = Array.Find(SlopCityJobCatalog.All, job => job.id == id);
            if (!initialized || definition == null) return false;
            RefreshSites();
            var selected = BuildAssignment(definition);
            if (selected == null) return false;
            InstallAssignment(definition, selected); return true;
        }

        private void NextContract(bool completed)
        {
            sites.RemoveAll(site => !site || !site.isActiveAndEnabled || !site.gameObject.activeInHierarchy);
            availableJobs.Clear();
            foreach (SlopCityJobDefinition definition in SlopCityJobCatalog.All)
                if (definition != null && definition.steps != null && definition.steps.Length > 0
                    && (definition.recipeVersion == 2 || Array.TrueForAll(definition.steps, step => step != null && step.kinds != null && sites.Exists(site => Matches(site, step)))))
                    availableJobs.Add(definition);
            int count = SlopCityJobCatalog.All.Length;
            int cursor = Mathf.Clamp(progress.dispatchCursor, 0, count - 1);
            // Least-completed jobs first; a persisted circular tie-breaker makes all 40 recur fairly.
            var ordered = new List<SlopCityJobDefinition>(availableJobs);
            ordered.Sort((a, b) =>
            {
                int compare = JobCompletions(a.id).CompareTo(JobCompletions(b.id));
                if (compare != 0) return compare;
                int ai = Array.IndexOf(SlopCityJobCatalog.All, a), bi = Array.IndexOf(SlopCityJobCatalog.All, b);
                return ((ai - cursor + count) % count).CompareTo((bi - cursor + count) % count);
            });
            foreach (SlopCityJobDefinition definition in ordered)
            {
                var selected = BuildAssignment(definition);
                if (selected == null) continue;
                InstallAssignment(definition, selected); return;
            }
            CancelTravel(); steps.Clear(); stepIndex = 0; cargo = null;
            currentJobId = currentJobOutcome = contractTitle = null; Save();
        }

        private List<ContractStep> BuildAssignment(SlopCityJobDefinition definition)
        {
            if (TryBuildUniqueAssignment(definition, out List<ContractStep> unique)) return unique;
            // Generation has no save, cargo, travel or current-objective side effects.
            var original = new List<ContractStep>(steps);
            try
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    steps.Clear(); bool valid = true;
                    for (int index = 0; index < definition.steps.Length; index++)
                    {
                        SlopCityJobStepDefinition step = definition.steps[index];
                        SlopCityInteractable site = step.reuseStep >= 0
                            ? step.reuseStep < steps.Count && Matches(steps[step.reuseStep].site, step) ? steps[step.reuseStep].site : null
                            : Choose(step, definition, index);
                        if (!site) { valid = false; break; }
                        steps.Add(new ContractStep { site = site, instruction = step.instruction, action = step.action });
                    }
                    if (valid) return new List<ContractStep>(steps);
                }
                return null;
            }
            finally { steps.Clear(); steps.AddRange(original); }
        }

        private void InstallAssignment(SlopCityJobDefinition definition, List<ContractStep> selected)
        {
            CancelTravel(); steps.Clear(); steps.AddRange(selected); stepIndex = 0; cargo = null;
            contractTitle = definition.title; currentJobId = definition.id; currentJobOutcome = definition.outcome;
            contractNumber = AddBounded(contractNumber, 1);
            CommitUniqueSelection(definition);
            progress.dispatchCursor = (Array.IndexOf(SlopCityJobCatalog.All, definition) + 1) % SlopCityJobCatalog.All.Length;
            Save();
        }

        private bool RestoreAssignment()
        {
            SlopCityJobDefinition definition = Array.Find(SlopCityJobCatalog.All, job => job.id == progress.activeJobId);
            if (definition == null || progress.activeSites == null || progress.activeSites.Length != definition.steps.Length
                || progress.activeStep < 0 || progress.activeStep >= definition.steps.Length) return false;
            var restored = new List<ContractStep>();
            for (int index = 0; index < definition.steps.Length; index++)
            {
                SlopCityJobStepDefinition step = definition.steps[index];
                SlopCityInteractable site = sites.Find(candidate => StableId(candidate.transform) == progress.activeSites[index] && Matches(candidate, step));
                if (!site) site = RestoreMovedMetroSite(progress.activeSites[index], step);
                if (!site) return false;
                restored.Add(new ContractStep { site = site, action = step.action, instruction = step.instruction });
            }
            string expectedCargo = null;
            for (int index = 0; index < progress.activeStep; index++)
                switch (definition.steps[index].action)
                {
                    case SlopCityJobAction.CollectParcel: expectedCargo = "SEALED PARCEL"; break;
                    case SlopCityJobAction.CollectSample: expectedCargo = "SEALED SAMPLE"; break;
                    case SlopCityJobAction.DeliverParcel: case SlopCityJobAction.DeliverSample: expectedCargo = null; break;
                }
            if ((progress.cargo ?? "") != (expectedCargo ?? "")) return false;
            steps.AddRange(restored); stepIndex = progress.activeStep;
            currentJobId = definition.id; currentJobOutcome = definition.outcome; contractTitle = definition.title;
            cargo = progress.cargo == "SEALED PARCEL" || progress.cargo == "SEALED SAMPLE" ? progress.cargo : null;
            return true;
        }

        private SlopCityInteractable RestoreMovedMetroSite(string savedId, SlopCityJobStepDefinition step)
        {
            if (string.IsNullOrEmpty(savedId) || savedId.IndexOf("/Subway_", StringComparison.Ordinal) < 0) return null;
            string savedPath = savedId.Split(':')[0];
            string trainPath = TrainIdentity(savedPath);
            SlopCityInteractable match = null;
            foreach (SlopCityInteractable candidate in sites)
            {
                if (!candidate || !Matches(candidate, step)) continue;
                SlopCityMetroPlatform platform = candidate.GetComponent<SlopCityMetroPlatform>();
                string currentPath = StableId(candidate.transform).Split(':')[0];
                bool migrated = platform && platform.LegacyRouteId == savedId;
                migrated |= candidate.Kind == SlopCityInteractionKind.Subway && currentPath == savedPath;
                migrated |= trainPath != null && candidate.GetComponentInParent<SlopCityMetroTrain>()
                    && TrainIdentity(currentPath) == trainPath;
                if (!migrated) continue;
                if (match) return null; // Never guess between duplicate routes.
                match = candidate;
            }
            return match;
        }

        private static string TrainIdentity(string path)
        {
            int at = path.IndexOf("/Subway_Train_", StringComparison.Ordinal);
            return at < 0 ? null : path.Substring(at);
        }

        private void RememberWork(SlopCityInteractable item)
        {
            if (!item) return;
            string id = StableId(item.transform);
            if (!workArchive.ContainsKey(id) && workArchive.Count >= MaximumWorkRecords) return;
            workArchive[id] = item.CaptureWorkState(); workedSites[id] = item;
        }

        private static bool NeedsWork(SlopCityInteractable site, SlopCityJobAction action)
        {
            if (action == SlopCityJobAction.Repair) return site.WorkCondition < 100;
            if (action == SlopCityJobAction.WaterPlant) return !site.IsWatered;
            if (action == SlopCityJobAction.MaintainSurface) return !site.IsClean;
            if (action == SlopCityJobAction.Tune) return SlopCityCityMedia.Instance && SlopCityCityMedia.Instance.CanTuneScreen(site.transform);
            return true;
        }

        private static bool OnArrivalLevel(SlopCityInteractable site, Transform destination)
        {
            if (!destination || Mathf.Abs(site.InteractionPoint.y - destination.position.y) > 2.6f) return false;
            SlopCityBuildingInterior building = destination.GetComponentInParent<SlopCityBuildingInterior>();
            return building && building.IsTower ? building.ContainsHorizontal(site.InteractionPoint, .5f)
                : Vector2.Distance(new Vector2(site.InteractionPoint.x, site.InteractionPoint.z),
                    new Vector2(destination.position.x, destination.position.z)) <= 12f;
        }

        private static bool Matches(SlopCityInteractable site, SlopCityJobStepDefinition step)
        {
            if (!site || Array.IndexOf(step.kinds, site.Kind) < 0 || !SlopCityJobCatalog.Supports(site, step.action)) return false;
            if (step.action != SlopCityJobAction.Travel) return true;
            return site.Kind == SlopCityInteractionKind.Subway
                ? site.DisplayName.StartsWith("RIDE", StringComparison.OrdinalIgnoreCase)
                : site.Destination.position.y > site.InteractionPoint.y + 2f;
        }

        private SlopCityInteractable Choose(SlopCityJobStepDefinition step, SlopCityJobDefinition definition, int index)
        {
            var candidates = sites.FindAll(site => Matches(site, step) && NeedsWork(site, step.action));
            // Reused targets must also support the later practical work (e.g. diagnose then repair).
            for (int later = index + 1; later < definition.steps.Length; later++)
                if (definition.steps[later].reuseStep == index)
                {
                    SlopCityJobAction future = definition.steps[later].action;
                    candidates.RemoveAll(site => !NeedsWork(site, future));
                }
            if (step.action == SlopCityJobAction.Travel && index + 1 < definition.steps.Length
                && definition.steps[index + 1].action == SlopCityJobAction.MaintainSurface)
                candidates.RemoveAll(site => !sites.Exists(surface => Matches(surface, definition.steps[index + 1])
                    && NeedsWork(surface, SlopCityJobAction.MaintainSurface) && OnArrivalLevel(surface, site.Destination)));
            if (candidates.Count == 0) return null;
            ContractStep previousLeg = steps.Count > 0 ? steps[steps.Count - 1] : null;
            if (step.action == SlopCityJobAction.MaintainSurface && previousLeg != null
                && previousLeg.action == SlopCityJobAction.Travel && previousLeg.site.Destination)
            {
                Vector3 arrival = previousLeg.site.Destination.position;
                SlopCityBuildingInterior building = previousLeg.site.Destination.GetComponentInParent<SlopCityBuildingInterior>();
                // A wide roof's service panel can be farther than 12m from its legacy
                // arrival marker. Keep work on that building and level, not a nearby roof.
                candidates.RemoveAll(site => Mathf.Abs(site.InteractionPoint.y - arrival.y) > 2.6f
                    || (building && building.IsTower ? !building.ContainsHorizontal(site.InteractionPoint, .5f)
                        : Vector2.Distance(new Vector2(site.InteractionPoint.x, site.InteractionPoint.z), new Vector2(arrival.x, arrival.z)) > 12f));
                if (candidates.Count == 0) return null;
            }
            // Prefer fresh sites per stage. Explicit reuseStep handles servicing one device.
            var fresh = candidates.FindAll(site => !steps.Exists(previous => previous.site == site));
            if (fresh.Count > 0) candidates = fresh;
            else if (step.action == SlopCityJobAction.DeliverParcel || step.action == SlopCityJobAction.DeliverSample
                || step.action == SlopCityJobAction.WaterPlant || step.action == SlopCityJobAction.MaintainSurface
                || step.action == SlopCityJobAction.Repair) return null;
            ContractStep prior = steps.Count > 0 ? steps[steps.Count - 1] : null;
            Vector3 origin = prior != null ? (prior.action == SlopCityJobAction.Travel && prior.site.Destination
                ? prior.site.Destination.position : prior.site.InteractionPoint) : user ? user.position : Vector3.zero;
            candidates.Sort((a, b) => (a.InteractionPoint - origin).sqrMagnitude.CompareTo((b.InteractionPoint - origin).sqrMagnitude));
            return candidates[UnityEngine.Random.Range(0, Mathf.Min(5, candidates.Count))];
        }

        public void RefreshWorldGeometry()
        {
            if (!initialized) return;
            RefreshSalvage();
            RefreshSites();
            aimed = default;
            nextTargetAt = 0f;
        }

        /// <summary>Invalidate aim/scenery after streamed collision without rescanning the whole city.</summary>
        internal void RefreshWorldGeometryLight()
        {
            if (!initialized) return;
            RefreshSalvageLight();
            aimed = default;
            nextTargetAt = 0f;
        }

        private void RefreshSites()
        {
#if (UNITY_WEBGL && !UNITY_EDITOR) || SLOP_CITY_WEB_INTERACTION_TEST
            // A refresh can replace scenery before allocation or native reads fail.
            // Never query the previous index against a different renderer array.
            webSceneryReady = false;
#endif
            // Diegetic guidance is visual only, never an inspectable city object.
            scenery = Array.FindAll(FindObjectsByType<Renderer>(FindObjectsSortMode.None),
                renderer => renderer && !renderer.GetComponentInParent<SlopCityRabbitTrail>());
            // Older scene exports get sensible interactions without requiring a rebuild.
            if (!legacyStreetInteractionsPrepared)
            {
                foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                {
                    if (!candidate || (user && candidate.IsChildOf(user)) || candidate.GetComponentInParent<SlopCityInteractable>()) continue;
                    string label = candidate.name;
                    SlopCityInteractionKind kind;
                    if (label == "City_Bench") kind = SlopCityInteractionKind.Bench;
                    else if (label == "Street_Lamp") kind = SlopCityInteractionKind.Light;
                    else if (label == "Litter_Bin") kind = SlopCityInteractionKind.Bin;
                    else if (label == "Parked_CityCar") kind = SlopCityInteractionKind.Car;
                    else if (label == "Tree_Trunk") kind = SlopCityInteractionKind.Plant;
                    else continue;
                    var item = candidate.gameObject.AddComponent<SlopCityInteractable>();
                    item.Configure(kind, Readable(label), kind != SlopCityInteractionKind.Car);
                    Renderer renderer = candidate.GetComponent<Renderer>();
                    if (renderer) item.ConfigureInteractionPoint(renderer.bounds.center);
                }
                legacyStreetInteractionsPrepared = true;
            }
            sites.Clear(); jobVehicles.Clear();
            foreach (SlopCityInteractable item in FindObjectsByType<SlopCityInteractable>(FindObjectsSortMode.None))
            {
                if (!item.isActiveAndEnabled || !item.gameObject.activeInHierarchy) continue;
                if (item.Kind == SlopCityInteractionKind.Car) { jobVehicles.Add(item); continue; }
                if (item.Kind == SlopCityInteractionKind.RareFind) continue;
                SlopCityBuildingInterior interior = item.GetComponentInParent<SlopCityBuildingInterior>();
                // An inspected room sign can acquire an Inspect component. It must
                // not become a dispatch target that vanishes when its room unloads.
                if (interior && !interior.PreservedInterior && !item.Destination) continue;
                // Destination panels keep their transport behavior. Elevated billboard faces use their reachable consoles.
                string label = item.name.ToLowerInvariant();
                bool groundNotice = item.Kind == SlopCityInteractionKind.Inspect
                    && (label.Contains("notice") || label.Contains("poster") || label.Contains("sign"))
                    && item.InteractionPoint.y > -.5f && item.InteractionPoint.y < 2.6f;
                if (item.ObjectiveEligible || item.Destination || groundNotice)
                {
                    sites.Add(item);
                    string id = StableId(item.transform);
                    if (workArchive.TryGetValue(id, out SlopCityInteractable.WorkState state)
                        && (!workedSites.TryGetValue(id, out SlopCityInteractable previous) || previous != item))
                    { item.RestoreWorkState(state); workedSites[id] = item; }
                }
            }
#if (UNITY_WEBGL && !UNITY_EDITOR) || SLOP_CITY_WEB_INTERACTION_TEST
            RebuildWebScenery();
#endif
            nextRefreshAt = Time.time + 15f;
        }

#if (UNITY_WEBGL && !UNITY_EDITOR) || SLOP_CITY_WEB_INTERACTION_TEST
        private void RebuildWebScenery()
        {
            webSceneryReady = false;
            try
            {
                // Serialized bounds are copied again below. Drop references to rooms
                // destroyed by streaming before adding the currently loaded renderers.
                webAuthoredBounds.Clear();
                webSceneryGrid.Clear();
                webSceneryFallback.Clear();
                webSceneryBounds = new Bounds[scenery.Length];
                webSceneryLocalBounds = new Bounds[scenery.Length];
                webSceneryHasLocalBounds = new bool[scenery.Length];
                webSceneryMoves = new bool[scenery.Length];
                webScenerySeen = new int[scenery.Length];
                webSceneryQuery = 0;
                webTargetFrame = -1;
                // Build-scene metadata retains authored bounds before Unity rewrites
                // static-batched vertices and replaces localBounds with batch AABBs.
                foreach (SlopCityBrowserGeometry geometry in FindObjectsByType<SlopCityBrowserGeometry>(FindObjectsSortMode.None))
                    geometry.CopyTo(webAuthoredBounds);
                for (int index = 0; index < scenery.Length; index++)
                {
                    Renderer renderer = scenery[index];
                    if (!renderer) continue;
                    if (!renderer.isPartOfStaticBatch)
                    {
                        Bounds localBounds = renderer.localBounds;
                        if (renderer.TryGetComponent<MeshFilter>(out MeshFilter filter) && filter.sharedMesh)
                            localBounds = filter.sharedMesh.bounds;
                        webAuthoredBounds[renderer] = localBounds;
                    }
                    webSceneryHasLocalBounds[index] = webAuthoredBounds.TryGetValue(renderer, out webSceneryLocalBounds[index]);
                    Bounds bounds = webSceneryBounds[index] = renderer.bounds;
                    bool moves = !renderer.gameObject.isStatic || renderer is not MeshRenderer;
                    // A static-marked child can still belong to a movable car or hinge.
                    for (Transform parent = renderer.transform; !moves && parent; parent = parent.parent)
                    {
                        SlopCityInteractable item = parent.GetComponent<SlopCityInteractable>();
                        moves = parent == user || (item && item.Kind == SlopCityInteractionKind.Car)
                            || parent.GetComponent<SlopCityDoor>() || parent.GetComponent<Rigidbody>()
                            || parent.GetComponent<Animator>() || parent.GetComponent<Animation>()
                            || parent.GetComponent<SlopCityMetroTrain>();
                    }
                    webSceneryMoves[index] = moves;
                    if (!TryWebSceneryCells(bounds, out Vector3Int min, out Vector3Int max)) continue;
                    long xCells = (long)max.x - min.x + 1, yCells = (long)max.y - min.y + 1, zCells = (long)max.z - min.z + 1;
                    // Ground planes and large facades must not fill thousands of cells.
                    if (moves || xCells > 64 || yCells > 64 || zCells > 64 || xCells * yCells * zCells > 64)
                    {
                        webSceneryFallback.Add(index);
                        continue;
                    }
                    for (int x = min.x; x <= max.x; x++)
                        for (int y = min.y; y <= max.y; y++)
                            for (int z = min.z; z <= max.z; z++)
                            {
                                var cell = new Vector3Int(x, y, z);
                                if (!webSceneryGrid.TryGetValue(cell, out List<int> entries))
                                    webSceneryGrid[cell] = entries = new List<int>();
                                entries.Add(index);
                            }
                }
                webSceneryReady = true;
                webSceneryWarning = false;
            }
            catch (Exception error)
            {
                // Physics targeting remains usable when this optional renderer
                // index cannot be rebuilt. Drop every partial index together.
                webAuthoredBounds.Clear(); webSceneryGrid.Clear(); webSceneryFallback.Clear(); webSceneryCandidates.Clear();
                webSceneryBounds = webSceneryLocalBounds = Array.Empty<Bounds>();
                webSceneryHasLocalBounds = webSceneryMoves = Array.Empty<bool>();
                webScenerySeen = Array.Empty<int>();
                if (!webSceneryWarning)
                {
                    webSceneryWarning = true;
                    Debug.LogWarning("City scenery targeting is unavailable until the next geometry refresh: " + error.GetType().Name, this);
                }
            }
        }

        private static bool TryWebSceneryCells(Bounds bounds, out Vector3Int min, out Vector3Int max)
        {
            min = max = default;
            Vector3 lower = bounds.min / WebSceneryCellSize, upper = bounds.max / WebSceneryCellSize;
            if (!Finite(lower) || !Finite(upper) || lower.x > upper.x || lower.y > upper.y || lower.z > upper.z) return false;
            // Exclude integer endpoints: x++ at int.MaxValue would wrap and
            // turn a corrupt/extreme bound into an effectively endless loop.
            if ((double)lower.x <= int.MinValue || (double)lower.y <= int.MinValue || (double)lower.z <= int.MinValue
                || (double)upper.x >= int.MaxValue || (double)upper.y >= int.MaxValue || (double)upper.z >= int.MaxValue) return false;
            min = WebSceneryCell(bounds.min); max = WebSceneryCell(bounds.max);
            return true;
        }

        private static Vector3Int WebSceneryCell(Vector3 point) => new Vector3Int(
            Mathf.FloorToInt(point.x / WebSceneryCellSize), Mathf.FloorToInt(point.y / WebSceneryCellSize),
            Mathf.FloorToInt(point.z / WebSceneryCellSize));

        private void GatherWebScenery(Ray ray, float reach)
        {
            webSceneryCandidates.Clear();
            if (!webSceneryReady) return;
            if (++webSceneryQuery == int.MaxValue)
            {
                Array.Clear(webScenerySeen, 0, webScenerySeen.Length);
                webSceneryQuery = 1;
            }
            var query = new Bounds(ray.origin, Vector3.zero);
            query.Encapsulate(ray.GetPoint(reach));
            query.Expand(.002f);
            if (!TryWebSceneryCells(query, out Vector3Int min, out Vector3Int max)) return;
            if ((long)max.x - min.x > 64 || (long)max.y - min.y > 64 || (long)max.z - min.z > 64) return;
            for (int x = min.x; x <= max.x; x++)
                for (int y = min.y; y <= max.y; y++)
                    for (int z = min.z; z <= max.z; z++)
                        if (webSceneryGrid.TryGetValue(new Vector3Int(x, y, z), out List<int> entries))
                            foreach (int index in entries) AddWebSceneryCandidate(index);
            foreach (int index in webSceneryFallback)
            {
                if (!scenery[index]) continue;
                if (webSceneryMoves[index]) webSceneryBounds[index] = scenery[index].bounds;
                if (webSceneryBounds[index].Intersects(query)) AddWebSceneryCandidate(index);
            }
            // The original scan is order-sensitive within its 0.025m hit margin.
            webSceneryCandidates.Sort();
        }

        private void AddWebSceneryCandidate(int index)
        {
            if (webScenerySeen[index] == webSceneryQuery) return;
            webScenerySeen[index] = webSceneryQuery;
            webSceneryCandidates.Add(index);
        }

        private Target FindBrowserTarget(Camera camera)
        {
            if (!camera || !user) return default;
            activeCamera = camera;
            Ray ray = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
            // ViewportPointToRay starts on the near plane. Reach is measured
            // from the player's eye, rather than gaining the near-clip distance.
            if (!camera.orthographic) ray.origin = camera.transform.position;
            bool targetUnchanged = !aimed.transform || aimed.transform.localToWorldMatrix.Equals(webTargetTransform);
            if (webTargetFrame == Time.frameCount && Time.time < nextTargetAt && webTargetCamera == camera
                && ray.origin.Equals(webTargetRay.origin) && ray.direction.Equals(webTargetRay.direction) && targetUnchanged)
                return aimed;
            webTargetFrame = Time.frameCount;
            webTargetCamera = camera;
            webTargetRay = ray;
            nextTargetAt = Time.time + .07f;
            aimed = default;
            float closest = Reach;

            int count = Physics.RaycastNonAlloc(ray, webTargetHits, Reach, ~0, QueryTriggerInteraction.Ignore);
            // A full unordered buffer may omit a nearer wall. Keep this query
            // allocation-free and refuse interaction until the ray is unambiguous.
            if (count == webTargetHits.Length) return default;
            RaycastHit[] hits = webTargetHits;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = hits[index];
                if (!hit.transform || hit.transform == user || hit.transform.IsChildOf(user) || hit.distance > closest) continue;
                closest = hit.distance;
                aimed = new Target { transform = hit.transform, point = hit.point,
                    interaction = hit.transform.GetComponentInParent<SlopCityInteractable>(), renderer = hit.transform.GetComponent<Renderer>() };
            }

            SlopCityInteractable solidInteraction = aimed.interaction;
            SlopCityDoor solidDoor = aimed.transform ? aimed.transform.GetComponentInParent<SlopCityDoor>() : null;
            GatherWebScenery(ray, closest);
            foreach (int index in webSceneryCandidates)
            {
                Renderer renderer = scenery[index];
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.GetComponent<TextMesh>()
                    || renderer.transform == user || renderer.transform.IsChildOf(user)) continue;
                if (!webSceneryBounds[index].IntersectRay(ray, out float broadDistance) || broadDistance > closest) continue;
                // A solid collider already provides an exact hit. Falling back
                // to its render bounds would make spheres and hollow meshes
                // selectable where the physics ray explicitly missed them.
                if (renderer.TryGetComponent<Collider>(out Collider collider) && collider.enabled && !collider.isTrigger) continue;
                if (!webSceneryHasLocalBounds[index]
                    || !BrowserRendererHit(renderer, webSceneryLocalBounds[index], ray, out float distance)
                    || distance < .015f || distance > closest) continue;
                // Preserve the precise collider hit when the two surfaces tie.
                if (aimed.transform && distance >= closest - .0001f) continue;
                SlopCityInteractable interaction = renderer.GetComponentInParent<SlopCityInteractable>();
                // A hollow decorative mesh's local AABB can cover empty space in
                // front of a device. This is approximate bounds selection, not a
                // triangle hit: retain an exact actionable collider against an
                // unrelated generic inspection. Physical doors are owned by
                // FirstPerson and need the same protection without an Interactable.
                // Same-door handles and nearer functional colliderless controls
                // retain their normal targeting.
                if (!BrowserDecorationCanReplaceSolidTarget(solidInteraction, interaction, solidDoor,
                    renderer.GetComponentInParent<SlopCityDoor>())) continue;
                closest = distance;
                aimed = new Target { transform = renderer.transform, renderer = renderer, point = ray.GetPoint(distance),
                    interaction = interaction };
            }

            SlopCityCrowd crowd = SlopCityCrowd.Instance;
            if (crowd && crowd.TryFindTarget(ray, closest, out int npcIndex, out Vector3 npcPoint))
                aimed = new Target { transform = crowd.transform, point = npcPoint, isNpc = true, npcIndex = npcIndex };
            webTargetTransform = aimed.transform ? aimed.transform.localToWorldMatrix : Matrix4x4.identity;
            return aimed;
        }

        private static bool BrowserDecorationCanReplaceSolidTarget(SlopCityInteractable solid, SlopCityInteractable decoration,
            SlopCityDoor solidDoor, SlopCityDoor decorationDoor)
        {
            if (solidDoor && solidDoor.isActiveAndEnabled)
                return decorationDoor == solidDoor || IsFunctionalInteraction(decoration);
            return !IsFunctionalInteraction(solid) || decoration == solid
                || IsFunctionalInteraction(decoration);
        }

        private static bool BrowserRendererHit(Renderer renderer, Bounds bounds, Ray worldRay, out float distance)
        {
            distance = 0f;
            Matrix4x4 toLocal = renderer.transform.worldToLocalMatrix;
            Vector3 direction = toLocal.MultiplyVector(worldRay.direction);
            float scale = direction.magnitude;
            if (scale < .000001f) return false;
            // Bounds stay in the authored transform's space even for static
            // batches, avoiding the empty corners of their world-space AABBs.
            var localRay = new Ray(toLocal.MultiplyPoint3x4(worldRay.origin), direction / scale);
            if (!bounds.IntersectRay(localRay, out float localDistance)) return false;
            distance = localDistance / scale;
            return distance >= 0f;
        }
#endif

        private static bool IsPhysicalWorkControl(SlopCityInteractable item)
            => item && (item.GetComponent<SlopCityJobDevice>() || item.GetComponent<SlopCityPawnCounter>());

        private static bool IsFunctionalInteraction(SlopCityInteractable item)
            => item && (item.Kind != SlopCityInteractionKind.Inspect || IsPhysicalWorkControl(item));

        private Target FindTarget(Camera camera)
        {
#if (UNITY_WEBGL && !UNITY_EDITOR) || SLOP_CITY_WEB_INTERACTION_TEST
            return FindBrowserTarget(camera);
#else
            if (!camera || !user) return default;
            activeCamera = camera;
            if (Time.time < nextTargetAt) return aimed;
            nextTargetAt = Time.time + .07f;
            aimed = default;
            Ray ray = new Ray(camera.transform.position, camera.transform.forward);
            float closest = Reach;
            bool doorBlocks = false;
            int hitCount = Physics.RaycastNonAlloc(ray, targetRayHits, Reach, ~0, QueryTriggerInteraction.Ignore);
            if (hitCount == targetRayHits.Length) return default;
            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = targetRayHits[index];
                if (!hit.transform || hit.transform == user || hit.transform.IsChildOf(user) || hit.distance >= closest) continue;
                closest = hit.distance;
                doorBlocks = hit.transform.GetComponentInParent<SlopCityDoor>();
                aimed = new Target { transform = hit.transform, point = hit.point,
                    interaction = hit.transform.GetComponentInParent<SlopCityInteractable>(), renderer = hit.transform.GetComponent<Renderer>() };
            }
            SlopCityInteractable solidWorkControl = IsPhysicalWorkControl(aimed.interaction) ? aimed.interaction : null;
            // Colliderless trim, windows, posters, road paint and other decorative meshes also respond.
#if UNITY_WEBGL && !UNITY_EDITOR
            GatherWebScenery(ray, closest);
            foreach (int index in webSceneryCandidates)
            {
                Renderer renderer = scenery[index];
#else
            foreach (Renderer renderer in scenery)
            {
#endif
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.GetComponent<TextMesh>()
                    || renderer.transform == user || renderer.transform.IsChildOf(user)) continue;
#if UNITY_WEBGL && !UNITY_EDITOR
                if (!webSceneryBounds[index].IntersectRay(ray, out float distance) || distance < .015f || distance >= closest - .025f) continue;
#else
                if (!renderer.bounds.IntersectRay(ray, out float distance) || distance < .015f || distance >= closest - .025f) continue;
#endif
                if (renderer.GetComponentInParent<SlopCityDoor>()) continue;
                SlopCityInteractable interaction = renderer.GetComponentInParent<SlopCityInteractable>();
                // Newly admitted work controls have exact colliders, while hollow
                // city scenery has only approximate bounds. Keep the real control
                // unless a nearer actionable object owns the decorative surface.
                if (solidWorkControl && interaction != solidWorkControl && !IsFunctionalInteraction(interaction)) continue;
                closest = distance; doorBlocks = false;
                aimed = new Target { transform = renderer.transform, renderer = renderer, point = ray.GetPoint(distance),
                    interaction = interaction };
            }
            SlopCityCrowd crowd = SlopCityCrowd.Instance;
            if (crowd && crowd.TryFindTarget(ray, closest, out int npcIndex, out Vector3 npcPoint))
            {
                aimed = new Target { transform = crowd.transform, point = npcPoint, isNpc = true, npcIndex = npcIndex };
                doorBlocks = false;
            }
            if (doorBlocks) aimed = default;
            return aimed;
#endif
        }

        private void StartDriving(SlopCityInteractable car, Camera camera)
        {
            if (TeleportRequested == null || MovementOverrideRequested == null) { Say("Driver controls are unavailable. Inspect the dashboard screen to tune the car media."); return; }
            if (TryUsePortableJobFuel(car.transform, out string fuelMessage)) { Say(fuelMessage); return; }
            Bounds localBounds = new Bounds(new Vector3(0f, .85f, 0f), new Vector3(1.7f, 1.5f, 3.7f));
            bool foundBounds = false;
            foreach (Collider collider in car.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                if (!TryColliderLocalBounds(collider, out Bounds shape)) continue;
                Matrix4x4 toCar = car.transform.worldToLocalMatrix * collider.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 p = toCar.MultiplyPoint3x4(new Vector3(
                        (corner & 1) == 0 ? shape.min.x : shape.max.x,
                        (corner & 2) == 0 ? shape.min.y : shape.max.y,
                        (corner & 4) == 0 ? shape.min.z : shape.max.z));
                    if (!foundBounds) { localBounds = new Bounds(p, Vector3.zero); foundBounds = true; }
                    else localBounds.Encapsulate(p);
                }
            }
            localBounds.Expand(new Vector3(.04f, 0f, .04f));
            vehicleLocalCenter = localBounds.center;
            vehicleHalfExtents = localBounds.extents;
            vehicle = car;
            vehicleEntryPosition = user.position;
            savedCameraOffset = camera.transform.localPosition;
            activeCamera = camera;
            vehicleSpeed = 0f;
            MovementOverrideRequested(true);
            camera.transform.localPosition = Vector3.up * 1.25f;
            TeleportRequested(car.transform.TransformPoint(new Vector3(-.22f, 0f, -.1f)), car.transform.eulerAngles.y);
            Say("DRIVING / " + (input && input.UseTouchControls
                ? "Left stick: forward accelerates, back brakes/reverses, sideways steers. USE exits. "
                : "W accelerates / S brakes and reverses / A D steer / E exits. ") + Tune(car));
        }

        private static bool TryColliderLocalBounds(Collider collider, out Bounds bounds)
        {
            // World AABBs lose the original orientation. Transform shape-local corners so a
            // parked car retains the same envelope after turning and being entered again.
            if (collider is BoxCollider box)
            {
                bounds = new Bounds(box.center, box.size);
                return true;
            }
            if (collider is SphereCollider sphere)
            {
                bounds = new Bounds(sphere.center, Vector3.one * sphere.radius * 2f);
                return true;
            }
            if (collider is CapsuleCollider capsule)
            {
                Vector3 size = Vector3.one * capsule.radius * 2f;
                size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                bounds = new Bounds(capsule.center, size);
                return true;
            }
            if (collider is MeshCollider mesh && mesh.sharedMesh)
            {
                bounds = mesh.sharedMesh.bounds;
                return true;
            }
            bounds = default;
            return false;
        }

        private void Drive(float dt)
        {
            if (!vehicle || input == null) return;
            dt = Mathf.Min(dt, .05f);
            Vector2 axes = CanDriveJobVehicle(vehicle.transform) ? input.ReadMovement() : Vector2.zero;
            float mealSpeed = MealSpeedMultiplier;
            float targetSpeed = (axes.y >= 0f ? axes.y * 13f : axes.y * 5f) * mealSpeed;
            vehicleSpeed = Mathf.MoveTowards(vehicleSpeed, targetSpeed, (axes.y == 0f ? 6f : 9f) * mealSpeed * dt);
            Transform car = vehicle.transform;
            float steering = axes.x * Mathf.Clamp(vehicleSpeed / 3f, -1f, 1f) * 58f * dt;
            Quaternion rotation = car.rotation * Quaternion.Euler(0f, steering, 0f);
            Vector3 move = rotation * Vector3.forward * vehicleSpeed * dt;
            Vector3 candidate = car.position + move;
            Vector3 center = candidate + rotation * vehicleLocalCenter;
            bool blocked = false;
            foreach (Collider obstacle in Physics.OverlapBox(center, vehicleHalfExtents, rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!obstacle || obstacle.transform.IsChildOf(car) || obstacle.transform == car
                    || obstacle.transform == user || obstacle.transform.IsChildOf(user)) continue;
                // Pavement, lane markings and curbs remain drivable; walls and street furniture do not.
                if (obstacle.bounds.max.y <= car.position.y + .20f) continue;
                blocked = true; break;
            }
            if (!blocked)
            {
                // Cars cannot leave the supported street surface or drive off the edge of the map.
                bool supported = false;
                int supportCount = Physics.RaycastNonAlloc(candidate + Vector3.up * .5f, Vector3.down, driveSupportHits, 1.2f,
                    ~0, QueryTriggerInteraction.Ignore);
                for (int supportIndex = 0; supportIndex < supportCount; supportIndex++)
                {
                    RaycastHit hit = driveSupportHits[supportIndex];
                    if (hit.normal.y > .8f && !hit.transform.IsChildOf(car) && hit.transform != user && !hit.transform.IsChildOf(user))
                    { supported = true; break; }
                }
                if (supported) { car.SetPositionAndRotation(candidate, rotation); TickVehicleFuel(car, move.magnitude); Physics.SyncTransforms(); }
                else vehicleSpeed = 0f;
            }
            else vehicleSpeed = 0f;
            TeleportRequested?.Invoke(car.TransformPoint(new Vector3(-.22f, 0f, -.1f)), car.eulerAngles.y);
        }

        private void StopDriving(bool returningHome = false)
        {
            if (!vehicle)
            {
                if (ReferenceEquals(vehicle, null)) return;
                vehicle = null; vehicleSpeed = 0f;
                if (activeCamera) activeCamera.transform.localPosition = savedCameraOffset;
                MovementOverrideRequested?.Invoke(false);
                Say("Vehicle unavailable. You are back on foot.");
                return;
            }
            Transform car = vehicle.transform;
            Vector3 destination = vehicleEntryPosition;
            bool safe = returningHome;
            Vector3[] offsets = { new Vector3(-1.6f, 0, 0), new Vector3(1.6f, 0, 0), new Vector3(0, 0, -2.7f), new Vector3(0, 0, 2.7f) };
            foreach (Vector3 offset in offsets)
            {
                Vector3 proposed = car.TransformPoint(offset);
                if (!TrySafeFeet(proposed, out Vector3 feet)) continue;
                destination = feet; safe = true; break;
            }
            if (!safe && !TrySafeFeet(destination, out destination))
            {
                Say("Exit blocked. Move the car to an open stretch of road and try again."); return;
            }
            vehicle = null; vehicleSpeed = 0f;
            if (activeCamera) activeCamera.transform.localPosition = savedCameraOffset;
            TeleportRequested?.Invoke(destination, car.eulerAngles.y);
            MovementOverrideRequested?.Invoke(false);
            nextTargetAt = 0f;
            Say("Parked. Back on foot; your contract is still active.");
        }

        private bool TrySafeFeet(Vector3 location, out Vector3 feet)
        {
            feet = location;
            Vector3 origin = location + Vector3.up * 1.6f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, feetRayHits, 3f, ~0, QueryTriggerInteraction.Ignore);
            if (count == feetRayHits.Length) return false;
            while (count > 0)
            {
                int nearest = -1;
                float nearestDistance = float.PositiveInfinity;
                for (int index = 0; index < count; index++)
                {
                    RaycastHit hit = feetRayHits[index];
                    if (hit.distance >= nearestDistance || hit.normal.y < .75f || hit.transform == user || hit.transform.IsChildOf(user)) continue;
                    nearest = index; nearestDistance = hit.distance;
                }
                if (nearest < 0) break;
                RaycastHit chosen = feetRayHits[nearest];
                feetRayHits[nearest] = feetRayHits[count - 1];
                count--;
                Vector3 candidate = chosen.point + Vector3.up * .04f;
                bool blocked = false;
                foreach (Collider collider in Physics.OverlapCapsule(candidate + Vector3.up * .27f,
                    candidate + Vector3.up * 1.5f, .23f, ~0, QueryTriggerInteraction.Ignore))
                    if (collider.transform != user && !collider.transform.IsChildOf(user)) { blocked = true; break; }
                if (!blocked) { feet = candidate; return true; }
            }
            return false;
        }

        private void StopResting()
        {
            if (ReferenceEquals(restingAt, null)) return;
            if (activeCamera) activeCamera.transform.localPosition = savedCameraOffset;
            restingAt = null;
        }

        private static string InspectDescription(SlopCityInteractable item)
        {
            string name = item.DisplayName.ToLowerInvariant();
            string detail = name.Contains("road") || name.Contains("asphalt") ? "Rough asphalt. Traffic-polished aggregate catches the streetlights."
                : name.Contains("glass") || name.Contains("window") ? "Cold glazing. The skyline reflects across its surface."
                : name.Contains("wall") || name.Contains("concrete") ? "Weathered concrete, with seams and wear from city life."
                : name.Contains("sign") || name.Contains("poster") ? "A neighborhood notice. Every surface has a story."
                : name.Contains("water") ? "The water ripples under the city lights."
                : "Condition checked for the municipal maintenance log.";
            return item.DisplayName + " / " + detail;
        }

        private static string Readable(string value) => value.Replace('_', ' ').Replace("City ", "").Replace("(Clone)", "").Trim();
        private static string StableId(Transform item)
        {
            string path = item.name;
            for (Transform parent = item.parent; parent; parent = parent.parent) path = parent.name + "/" + path;
            if (item.GetComponentInParent<SlopCityMetroTrain>()) return path + ":moving";
            Vector3 p = item.position;
            return path + ":" + Mathf.RoundToInt(p.x * 10) + "," + Mathf.RoundToInt(p.y * 10) + "," + Mathf.RoundToInt(p.z * 10);
        }

        private const string PersistenceNotice = "Saving is unavailable. Progress is kept for this session; saving will retry.";
        private SlopCityMunicipalState CaptureMunicipalState(SlopCityInteractable item) => new SlopCityMunicipalState
        {
            parts = Salvage, water = WaterSupply, recycling = Recycling, readings = FieldReadings,
            serviceCount = item ? item.ServiceCount : 0,
            hasPower = item && item.Kind == SlopCityInteractionKind.Light, powerOn = item && item.IsOn
        };

        private void RecordMunicipal(SlopCityInteractable item, string action, SlopCityMunicipalOutcome outcome,
            SlopCityMunicipalState before, SlopCityMunicipalState after, string evidence)
        {
            Vector3 position = item ? item.InteractionPoint : user ? user.position : Vector3.zero;
            municipalLog.Record("Player", action, item ? item.DisplayName : "City operation", position.x, position.y, position.z,
                outcome, before, after, evidence, Time.time);
        }

        private void Say(string message)
        {
            if (municipalOperationActive) municipalOperationMessage = message;
            Action<string> present=npcDialogueActive&&npcDialogue!=null?npcDialogue:feedback;
            present?.Invoke(persistenceUnavailable ? message + " " + PersistenceNotice : message);
        }
        private bool Save()
        {
            if (unreadableRecoverySave) return false;
            try
            {
                CaptureSalvageProgress();
                progress.uniqueJobs = CaptureUniqueJobState();
                progress.activeJobId = currentJobId; progress.activeStep = stepIndex; progress.cargo = cargo;
                progress.activeSites = new string[steps.Count];
                for (int i = 0; i < steps.Count; i++) progress.activeSites[i] = steps[i].site ? StableId(steps[i].site.transform) : "";
                progress.jobs = new JobRecord[jobHistory.Count]; int jobIndex = 0;
                foreach (var pair in jobHistory) progress.jobs[jobIndex++] = new JobRecord { id = pair.Key, completed = pair.Value };
                foreach (var pair in workedSites) if (pair.Value) workArchive[pair.Key] = pair.Value.CaptureWorkState();
                progress.work = new WorkRecord[workArchive.Count]; int workIndex = 0;
                foreach (var pair in workArchive) progress.work[workIndex++] = new WorkRecord { id = pair.Key, state = pair.Value };
                progress.discovered = new string[discoveries.Count];
                discoveries.CopyTo(progress.discovered);
                if (!ValidDiscoveryArchive(progress.discovered))
                    throw new InvalidOperationException("Progress contains an unsupported discovery identifier.");
                string json = JsonUtility.ToJson(progress);
                if (json.Length > 1024 * 1024) throw new InvalidOperationException("Progress archive is too large to save safely.");
                string key = recoverySave ? SaveKey + ".recovery" : SaveKey;
#if UNITY_EDITOR
                if (ProgressStoreForTesting != null) ProgressStoreForTesting(key, json);
                else
#endif
                    PlayerPrefs.SetString(key, json);
                PlayerPrefs.Save();
                persistenceUnavailable = false;
                return true;
            }
            catch (Exception exception)
            {
                // A storage failure must not abort an already-applied resource/cargo operation or job advancement.
                // Keep all in-memory progress and retry through the existing interaction/pause/quit save calls.
                if (!persistenceUnavailable)
                {
                    Debug.LogWarning("SLOP CITY: Progress could not be saved: " + exception.Message, this);
                    feedback?.Invoke(PersistenceNotice);
                }
                persistenceUnavailable = true;
                return false;
            }
        }
        private void OnApplicationPause(bool paused) { if (paused && initialized) Save(); }
        private void OnApplicationQuit() { if (initialized) Save(); }
    }
}
