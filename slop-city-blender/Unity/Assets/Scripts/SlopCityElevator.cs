using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>Continuous physical transport with cabin/landing interlocks and swept passenger protection.</summary>
    [DefaultExecutionOrder(-40)]
    public sealed class SlopCityElevator : MonoBehaviour
    {
        private sealed class DoorPair
        {
            public Transform left, right;
            public BoxCollider leftCollider, rightCollider;
            public Vector3 leftClosed, rightClosed;
        }

        private const float CarHalf = .94f;
        private const float DoorTravel = .65f;
        private const int PageSize = 8;
        private const float Speed = 2.4f;
        private static Mesh cube;
        private static Material steel, floorMaterial, dark, buttonMaterial, textMaterial;
        private static Font font;
        private Transform cabin;
        private SlopCityFirstPerson player;
        private CharacterController passenger;
        private float[] elevations;
        private string[] labels;
        private DoorPair cabinDoors;
        private DoorPair[] landingDoors;
        private readonly Queue<int> requests = new Queue<int>();
        private bool[] requested;
        private Action<int> prepareFloor;
        private Func<int, bool> prepareFloorWhenReady;
        private bool openWhenPrepared, waitingForFloor;
        private float preparationRetryAfter;
        private float openProgress;
        private float doorTarget;
        private float dwell;
        private int currentFloor;
        private int targetFloor;
        private bool moving;
        private int page;
        private SlopCityElevatorButton[] floorButtons;
        private TextMesh indicator;
        private string indicatorText;
        private bool initialized;
        private bool streamLandingVisuals;
        private Transform[] landingRoots;
        private readonly Dictionary<int, List<GameObject>> landingVisuals = new Dictionary<int, List<GameObject>>();
        private readonly HashSet<int> wantedLandingVisuals = new HashSet<int>();
        private readonly List<int> retireLandingVisuals = new List<int>();
        private Action visualGeometryChanged;
        internal const int MaximumVisualLandings = 3;
        public int VisualLandingCount => streamLandingVisuals ? landingVisuals.Count : FloorCount;
        private bool retiring;
        private float blockedSeconds, obstructionRetry;
        private bool returningFromObstruction, obstructed;

        public event Action<SlopCityElevator, int> Arrived;
        public Transform Cabin => cabin;
        public int CurrentFloor => currentFloor;
        public int TargetFloor => targetFloor;
        public int FloorCount => elevations == null ? 0 : elevations.Length;
        public bool IsMoving => moving;
        public bool DoorsOpen => !moving && openProgress >= .999f;
        public float DoorOpenProgress => openProgress;
        public bool CanUnload => !PlayerInside && !moving && Mathf.Abs(openProgress - doorTarget) < .001f;
        public string PageLabel => "" + (page * PageSize + 1) + "–" + Mathf.Min((page + 1) * PageSize, FloorCount);
        public bool PlayerInside
        {
            get
            {
                if (!initialized || !player || !passenger || !passenger.enabled || player.IsSeated || !cabin) return false;
                Vector3 feet = cabin.InverseTransformPoint(player.transform.position);
                // Include a standing/jumping rider, while never capturing somebody on a landing outside the car.
                return Mathf.Abs(feet.x) <= CarHalf && Mathf.Abs(feet.z) <= CarHalf
                    && feet.y >= -.24f && feet.y <= 1.15f;
            }
        }

        public float FloorElevation(int index) => index >= 0 && index < FloorCount ? elevations[index] : float.NaN;
        public string FloorLabel(int index) => index < 0 || index >= FloorCount ? "UNAVAILABLE" : labels[index];

        public static SlopCityElevator Create(Transform parent, Vector3 shaftLocalCenter, float[] floorElevations,
            string[] floorLabels, SlopCityFirstPerson rider, Action<int> prepare, bool streamLandingVisuals = false,
            Action visualGeometryChanged = null, Func<int, bool> prepareWhenReady = null)
        {
            if (!parent || floorElevations == null || floorElevations.Length == 0)
                throw new ArgumentException("An elevator requires a building and at least one floor.");
            for (int i = 0; i < floorElevations.Length; i++)
                if (float.IsNaN(floorElevations[i]) || float.IsInfinity(floorElevations[i])
                    || (i > 0 && floorElevations[i] - floorElevations[i - 1] < 2.6f))
                    throw new ArgumentException("Elevator floors must increase with at least 2.6 metres of headroom.");
            EnsureAssets();
            var root = new GameObject("Physical_Elevator");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(shaftLocalCenter.x, 0, shaftLocalCenter.z);
            var elevator = root.AddComponent<SlopCityElevator>();
            try
            {
                elevator.elevations = (float[])floorElevations.Clone();
                elevator.labels = new string[floorElevations.Length];
                for (int i = 0; i < elevator.labels.Length; i++)
                    elevator.labels[i] = floorLabels != null && i < floorLabels.Length && !string.IsNullOrWhiteSpace(floorLabels[i])
                        ? floorLabels[i] : i == 0 ? "LOBBY" : i == elevator.labels.Length - 1 ? "ROOF" : "FLOOR " + i.ToString("00");
                elevator.player = rider;
                elevator.passenger = rider ? rider.GetComponent<CharacterController>() : null;
                elevator.prepareFloor = prepare;
                elevator.prepareFloorWhenReady = prepareWhenReady;
                elevator.streamLandingVisuals = streamLandingVisuals;
                elevator.visualGeometryChanged = visualGeometryChanged;
                elevator.requested = new bool[elevator.FloorCount];
                elevator.Build();
                elevator.initialized = true;
                // Original synchronous callers retain their construction failure
                // behavior. Tower streaming can instead report a deferred landing.
                if (prepareWhenReady == null) prepare?.Invoke(0);
                elevator.openWhenPrepared = prepareWhenReady != null && !elevator.TryPrepareFloor(0);
                elevator.doorTarget = elevator.openWhenPrepared ? 0f : 1f;
                elevator.UpdateIndicator();
                return elevator;
            }
            catch
            {
                root.SetActive(false);
                if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
                throw;
            }
        }

        private void Build()
        {
            float bottom = elevations[0] - .15f, top = elevations[FloorCount - 1] + 2.6f;
            foreach (float side in new[] { -1f, 1f })
                Box(transform, "Shaft_Side", new Vector3(side * 1.125f, (bottom + top) * .5f, 0), new Vector3(.05f, top - bottom, 2.30f), dark);
            Box(transform, "Shaft_Back", new Vector3(0, (bottom + top) * .5f, 1.125f), new Vector3(2.3f, top - bottom, .05f), dark);
            landingDoors = new DoorPair[FloorCount];
            landingRoots = new Transform[FloorCount];
            for (int i = 0; i < FloorCount; i++)
            {
                Transform landing = Root(transform, "Elevator_Landing_" + i.ToString("00"), new Vector3(0, elevations[i], 0));
                landingRoots[i] = landing;
                foreach (float side in new[] { -1f, 1f })
                    Box(landing, "Landing_Jamb", new Vector3(side * .85f, 1.10f, -1.16f), new Vector3(.60f, 2.20f, .12f), steel, !streamLandingVisuals);
                float beam = i == FloorCount - 1 ? .4f : elevations[i + 1] - elevations[i] - 2.20f;
                Box(landing, "Shaft_Front_Interfloor_Barrier", new Vector3(0, 2.20f + beam * .5f, -1.16f), new Vector3(2.30f, beam, .12f), dark, !streamLandingVisuals);
                landingDoors[i] = MakeDoors(landing, "Landing", -1.16f, !streamLandingVisuals);
                MakeButton(landing, "Call_Button", new Vector3(.83f, 1.18f, -1.255f), new Vector2(.19f, .23f), "CALL", SlopCityElevatorButtonKind.Call, i, !streamLandingVisuals);
                if (!streamLandingVisuals) Text(landing, FloorLabel(i), new Vector3(-.85f, 1.70f, -1.235f), .055f);
            }
            cabin = Root(transform, "Elevator_Cabin", new Vector3(0, elevations[0], 0));
            Box(cabin, "Cabin_Floor", new Vector3(0, -.065f, 0), new Vector3(2.08f, .13f, 2.08f), floorMaterial);
            Box(cabin, "Cabin_Ceiling", new Vector3(0, 2.5f, 0), new Vector3(2.08f, .10f, 2.08f), steel);
            Box(cabin, "Cabin_Back", new Vector3(0, 1.25f, .99f), new Vector3(2.08f, 2.5f, .1f), steel);
            foreach (float side in new[] { -1f, 1f })
            {
                Box(cabin, "Cabin_Side", new Vector3(side * .99f, 1.25f, 0), new Vector3(.1f, 2.5f, 2.08f), steel);
                Box(cabin, "Cabin_Front_Wing", new Vector3(side * .795f, 1.10f, -1.03f), new Vector3(.49f, 2.20f, .09f), steel);
                Box(cabin, "Cabin_Handrail", new Vector3(side * .925f, .95f, .05f), new Vector3(.025f, .055f, 1.3f), dark);
            }
            Box(cabin, "Cabin_Front_Header", new Vector3(0, 2.35f, -1.03f), new Vector3(2.08f, .30f, .09f), steel);
            cabinDoors = MakeDoors(cabin, "Cabin", -1.03f);
            Box(cabin, "Floor_Control_Panel", new Vector3(0, 1.43f, .912f), new Vector3(1.46f, 1.76f, .045f), dark);
            indicator = Text(cabin, "", new Vector3(0, 2.19f, .875f), .065f);
            floorButtons = new SlopCityElevatorButton[PageSize];
            for (int i = 0; i < PageSize; i++)
                floorButtons[i] = MakeButton(cabin, "Floor_Button_" + i, new Vector3((i % 2 == 0 ? -.28f : .28f), 1.97f - (i / 2) * .22f, .865f),
                    new Vector2(.42f, .17f), "", SlopCityElevatorButtonKind.Floor, i);
            MakeButton(cabin, "Lobby_Button", new Vector3(-.51f, .96f, .865f), new Vector2(.27f, .18f), FloorLabel(0), SlopCityElevatorButtonKind.Floor, 0);
            MakeButton(cabin, "Roof_Button", new Vector3(-.17f, .96f, .865f), new Vector2(.27f, .18f), FloorLabel(FloorCount - 1), SlopCityElevatorButtonKind.Floor, FloorCount - 1);
            MakeButton(cabin, "Open_Button", new Vector3(.17f, .96f, .865f), new Vector2(.27f, .18f), "OPEN", SlopCityElevatorButtonKind.Open);
            MakeButton(cabin, "Close_Button", new Vector3(.51f, .96f, .865f), new Vector2(.27f, .18f), "CLOSE", SlopCityElevatorButtonKind.Close);
            MakeButton(cabin, "Previous_Floors", new Vector3(-.28f, .71f, .865f), new Vector2(.42f, .18f), "< FLOORS", SlopCityElevatorButtonKind.PreviousPage);
            MakeButton(cabin, "Next_Floors", new Vector3(.28f, .71f, .865f), new Vector2(.42f, .18f), "FLOORS >", SlopCityElevatorButtonKind.NextPage);
            ChangePage(0);
            RefreshLandingVisuals();
        }

        internal void RefreshLandingVisuals()
        {
            if (!streamLandingVisuals || landingRoots == null) return;
            wantedLandingVisuals.Clear();
            wantedLandingVisuals.Add(currentFloor);
            wantedLandingVisuals.Add(moving ? targetFloor : currentFloor);
            if (player)
            {
                float height = transform.InverseTransformPoint(player.transform.position).y;
                int nearest = 0;
                for (int index = 1; index < FloorCount; index++)
                    if (Mathf.Abs(elevations[index] - height) < Mathf.Abs(elevations[nearest] - height)) nearest = index;
                wantedLandingVisuals.Add(nearest);
            }
            retireLandingVisuals.Clear();
            foreach (int floor in landingVisuals.Keys)
                if (!wantedLandingVisuals.Contains(floor)) retireLandingVisuals.Add(floor);
            bool changed = retireLandingVisuals.Count != 0;
            foreach (int floor in retireLandingVisuals)
            {
                foreach (GameObject visual in landingVisuals[floor]) SlopCityInteriorGeometry.Dispose(visual);
                landingVisuals.Remove(floor);
            }
            foreach (int floor in wantedLandingVisuals)
            {
                if (landingVisuals.ContainsKey(floor)) continue;
                var visuals = new List<GameObject>();
                // Register ownership before fallible renderer/font construction.
                landingVisuals.Add(floor, visuals);
                Transform landing = landingRoots[floor];
                try
                {
                    foreach (BoxCollider body in landing.GetComponentsInChildren<BoxCollider>())
                    {
                        Material finish = body.name == "Shaft_Front_Interfloor_Barrier" ? dark
                            : body.name == "Call_Button" ? buttonMaterial : steel;
                        visuals.Add(VisualSurface(body.transform, body.size, finish).gameObject);
                    }
                    SlopCityElevatorButton button = landing.GetComponentInChildren<SlopCityElevatorButton>();
                    TextMesh call = Text(button.transform, "CALL", new Vector3(0, 0, -.020f), .045f);
                    visuals.Add(call.gameObject); button.SetLabel(call);
                    visuals.Add(Text(landing, FloorLabel(floor), new Vector3(-.85f, 1.70f, -1.235f), .055f).gameObject);
                    changed = true;
                }
                catch
                {
                    foreach (GameObject visual in visuals) if (visual) SlopCityInteriorGeometry.Dispose(visual);
                    landingVisuals.Remove(floor);
                    throw;
                }
            }
            if (changed) visualGeometryChanged?.Invoke();
        }

        public void ChangePage(int direction)
        {
            page = Mathf.Clamp(page + direction, 0, (FloorCount - 1) / PageSize);
            if (floorButtons != null)
                for (int i = 0; i < floorButtons.Length; i++) floorButtons[i].SetFloor(page * PageSize + i);
            UpdateIndicator();
        }

        public bool RequestFloor(int index)
        {
            if (!initialized || retiring || obstructed || returningFromObstruction || index < 0 || index >= FloorCount) return false;
            if (!moving && index == currentFloor) return OpenDoors();
            if ((moving && index == targetFloor) || requested[index]) return true;
            requests.Enqueue(index); requested[index] = true;
            // A selected floor gets a short boarding interval; repeated taps do not extend it forever.
            if (!moving && doorTarget > 0f && dwell <= 0f) dwell = .75f;
            UpdateIndicator();
            return true;
        }

        public bool OpenDoors()
        {
            if (!initialized || retiring || moving) return false;
            if (!TryPrepareFloor(currentFloor))
            {
                openWhenPrepared = true; doorTarget = 0f;
                return false;
            }
            openWhenPrepared = false;
            doorTarget = 1f; dwell = 3f;
            return true;
        }

        public bool CloseDoors()
        {
            if (!initialized || retiring || moving) return false;
            openWhenPrepared = false;
            doorTarget = 0f; dwell = 0f;
            return true;
        }

        /// <summary>Only empty, stationary elevators may be retired by the building streaming owner.</summary>
        public bool Release()
        {
            if (retiring) return false;
            if (!CanUnload) return false;
            requests.Clear(); Array.Clear(requested, 0, requested.Length);
            openWhenPrepared = waitingForFloor = false;
            doorTarget = openProgress = 0f;
            PositionDoors(cabinDoors, 0f); PositionDoors(landingDoors[currentFloor], 0f);
            Physics.SyncTransforms();
            return true;
        }

        /// <summary>
        /// Retire an empty instance outside the player's occupied building. The streaming owner must
        /// dispose it immediately after success, never suspend/reuse it: its empty car may be in the shaft.
        /// No passenger is relocated, and a capsule in either closing doorway prevents retirement.
        /// </summary>
        public bool ReleaseEmpty()
        {
            if (!initialized || retiring || PlayerInside || DoorTouchesPassenger(cabinDoors, 0f)
                || DoorTouchesPassenger(landingDoors[currentFloor], 0f)) return false;
            requests.Clear(); Array.Clear(requested, 0, requested.Length);
            openWhenPrepared = waitingForFloor = false;
            moving = false;
            doorTarget = openProgress = 0f;
            PositionDoors(cabinDoors, 0f); PositionDoors(landingDoors[currentFloor], 0f);
            retiring = true;
            Physics.SyncTransforms();
            return true;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Production tick shared with focused fixtures. Long frames use bounded physical substeps.</summary>
        public void Tick(float deltaTime)
        {
            if (!initialized || retiring || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)
                || (player && player.IsPaused)) return;
            float remaining = Mathf.Min(deltaTime, .25f);
            while (remaining > .00001f)
            {
                float dt = Mathf.Min(remaining, .025f); remaining -= dt;
                if (moving) MoveCabin(dt);
                else
                {
                    if (openWhenPrepared && requests.Count == 0) OpenDoors();
                    if (requests.Count > 0 && doorTarget > 0f && openProgress >= .999f)
                    {
                        dwell = Mathf.Max(0f, dwell - dt);
                        if (dwell <= 0f) doorTarget = 0f;
                    }
                    MoveDoors(dt);
                    if (requests.Count > 0 && openProgress <= .00001f && doorTarget <= 0f)
                    {
                        // Deferred readiness is not a failed button press. Keep
                        // queue ownership until a supported destination exists.
                        targetFloor = requests.Peek();
                        if (!TryPrepareFloor(targetFloor)) { doorTarget = 0f; continue; }
                        requests.Dequeue(); requested[targetFloor] = false;
                        if (targetFloor == currentFloor) { OpenDoors(); continue; }
                        openWhenPrepared = false;
                        blockedSeconds = obstructionRetry = 0f;
                        returningFromObstruction = obstructed = false;
                        moving = true;
                    }
                }
            }
            RefreshLandingVisuals();
            UpdateIndicator();
        }

        private void MoveDoors(float dt)
        {
            if (Mathf.Abs(openProgress - doorTarget) < .00001f) return;
            float next = Mathf.MoveTowards(openProgress, doorTarget, dt * 1.25f);
            if (DoorTouchesPassenger(cabinDoors, next) || DoorTouchesPassenger(landingDoors[currentFloor], next))
            {
                if (doorTarget < openProgress)
                {
                    doorTarget = 1f; dwell = 2.5f;
                    if (player) player.ShowFeedback("Elevator doorway blocked. Step inside or back onto the landing.");
                }
                return;
            }
            openProgress = next;
            PositionDoors(cabinDoors, next); PositionDoors(landingDoors[currentFloor], next);
            Physics.SyncTransforms();
        }

        private bool DoorTouchesPassenger(DoorPair doors, float progress)
        {
            if (!passenger || !passenger.enabled || !passenger.gameObject.activeInHierarchy) return false;
            return LeafTouchesPassenger(doors.leftCollider, doors.leftClosed + Vector3.left * (DoorTravel * progress))
                || LeafTouchesPassenger(doors.rightCollider, doors.rightClosed + Vector3.right * (DoorTravel * progress));
        }

        private bool LeafTouchesPassenger(BoxCollider leaf, Vector3 localPosition)
        {
            return Physics.ComputePenetration(leaf, leaf.transform.parent.TransformPoint(localPosition), leaf.transform.rotation,
                passenger, passenger.transform.position, passenger.transform.rotation, out _, out float depth) && depth > .001f;
        }

        private void MoveCabin(float dt)
        {
            if (obstructed)
            {
                obstructionRetry -= dt;
                if (obstructionRetry > 0f) return;
                obstructionRetry = 1f;
                if (returningFromObstruction && !TryPrepareFloor(targetFloor)) { obstructionRetry = .05f; return; }
            }
            // Every hall door is closed before movement starts and only the aligned landing opens.
            float from = cabin.localPosition.y;
            float to = Mathf.MoveTowards(from, elevations[targetFloor], Speed * dt);
            Vector3 localDelta = Vector3.up * (to - from);
            Vector3 worldDelta = transform.TransformVector(localDelta);
            bool carry = PlayerInside;
            Vector3 oldPassenger = carry ? player.transform.position : Vector3.zero;
            // Rising: clear the rider above the old floor first. Descending: lower the support first.
            if (carry && to > from) player.MoveWithPlatform(worldDelta);
            cabin.localPosition += localDelta;
            Physics.SyncTransforms();
            if (carry && to < from) player.MoveWithPlatform(worldDelta);
            if (carry && (player.transform.position - oldPassenger - worldDelta).sqrMagnitude > .0004f)
            {
                // A real obstruction pauses the car instead of crushing or dropping its passenger.
                cabin.localPosition -= localDelta;
                Physics.SyncTransforms();
                player.MoveWithPlatform(oldPassenger - player.transform.position);
                blockedSeconds += dt;
                if (!returningFromObstruction && blockedSeconds >= 1f)
                {
                    returningFromObstruction = true;
                    targetFloor = currentFloor;
                    // The departure room may have streamed out during a long ride.
                    // Restore its supported landing before a return can open those doors.
                    if (!TryPrepareFloor(targetFloor)) { obstructed = true; obstructionRetry = .05f; }
                    requests.Clear(); Array.Clear(requested, 0, requested.Length);
                    blockedSeconds = 0f;
                    if (player) player.ShowFeedback("Elevator obstruction. Returning to the last landing; keep clear of the doors.");
                }
                else if (returningFromObstruction && !obstructed && blockedSeconds >= 1f)
                {
                    obstructed = true;
                    obstructionRetry = 1f;
                    if (player) player.ShowFeedback("Elevator stopped safely. Move clear to let it recover, or use HOME in the pause menu.");
                }
                return;
            }
            blockedSeconds = 0f;
            obstructed = false;
            if (Mathf.Abs(to - elevations[targetFloor]) <= .00001f)
            {
                currentFloor = targetFloor; moving = false;
                returningFromObstruction = false;
                if (Arrived != null)
                    foreach (Action<SlopCityElevator, int> listener in Arrived.GetInvocationList())
                        try { listener(this, currentFloor); }
                        catch (Exception error) { Debug.LogWarning("Elevator arrival listener failed: " + error.Message, this); }
                OpenDoors();
            }
        }

        private bool TryPrepareFloor(int floor)
        {
            if (Time.unscaledTime < preparationRetryAfter) { waitingForFloor = true; return false; }
            try
            {
                if (prepareFloorWhenReady != null)
                {
                    bool ready = prepareFloorWhenReady(floor);
                    waitingForFloor = !ready;
                    return ready;
                }
                prepareFloor?.Invoke(floor);
                waitingForFloor = false;
                return true;
            }
            catch (Exception error)
            {
                waitingForFloor = true;
                // An actual construction error gets a bounded retry rate; the
                // ordinary next-frame pending path above logs no failure.
                preparationRetryAfter = Time.unscaledTime + 1f;
                Debug.LogWarning("Elevator landing unavailable: " + error.Message, this);
                if (player) player.ShowFeedback("That landing could not be loaded. Choose another floor, or use HOME in the pause menu.");
                return false;
            }
        }

        private void UpdateIndicator()
        {
            if (!indicator) return;
            string value = (obstructed ? "OBSTRUCTION / HOLD" : moving ? "TO " + FloorLabel(targetFloor)
                : waitingForFloor ? "LANDING / WAIT" : FloorLabel(currentFloor)) + "\nBUTTONS " + PageLabel;
            if (value == indicatorText) return;
            indicatorText = value; indicator.text = value;
        }

        private DoorPair MakeDoors(Transform parent, string prefix, float z, bool visual = true)
        {
            var pair = new DoorPair();
            pair.left = Box(parent, prefix + "_Door_Left", new Vector3(-.275f, 1.10f, z), new Vector3(.55f, 2.20f, .075f), steel, visual).transform;
            pair.right = Box(parent, prefix + "_Door_Right", new Vector3(.275f, 1.10f, z), new Vector3(.55f, 2.20f, .075f), steel, visual).transform;
            pair.leftCollider = pair.left.GetComponent<BoxCollider>(); pair.rightCollider = pair.right.GetComponent<BoxCollider>();
            pair.leftClosed = pair.left.localPosition; pair.rightClosed = pair.right.localPosition;
            return pair;
        }

        private static void PositionDoors(DoorPair pair, float progress)
        {
            pair.left.localPosition = pair.leftClosed + Vector3.left * (DoorTravel * progress);
            pair.right.localPosition = pair.rightClosed + Vector3.right * (DoorTravel * progress);
        }

        private SlopCityElevatorButton MakeButton(Transform parent, string name, Vector3 point, Vector2 size, string content,
            SlopCityElevatorButtonKind kind, int index = -1, bool visual = true)
        {
            GameObject value = Box(parent, name, point, new Vector3(size.x, size.y, .035f), buttonMaterial, visual);
            var button = value.AddComponent<SlopCityElevatorButton>(); button.Configure(this, kind, index);
            if (visual)
            {
                TextMesh text = Text(value.transform, content, new Vector3(0, 0, -.020f), content.Length > 5 ? .038f : .045f);
                // The button body has scaled geometry, its transform itself remains at unit scale.
                button.SetLabel(text);
            }
            return button;
        }

        private static Transform Root(Transform parent, string name, Vector3 point)
        {
            var value = new GameObject(name); value.transform.SetParent(parent, false); value.transform.localPosition = point;
            return value.transform;
        }

        private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material, bool visual = true)
        {
            var value = new GameObject(name); value.transform.SetParent(parent, false); value.transform.localPosition = center;
            // Colliders and moving door transforms exist at every floor. A distant
            // landing needs no renderer, mesh binding, font geometry or property block.
            if (visual) VisualSurface(value.transform, size, material);
            var collider = value.AddComponent<BoxCollider>(); collider.size = size;
            value.isStatic = false;
            return value;
        }

        private static Transform VisualSurface(Transform parent, Vector3 size, Material material)
        {
            Transform mesh = Root(parent, "Surface", Vector3.zero);
            try
            {
                mesh.localScale = size;
                mesh.gameObject.AddComponent<MeshFilter>().sharedMesh = cube;
                var renderer = mesh.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                mesh.gameObject.isStatic = false;
                return mesh;
            }
            catch { SlopCityInteriorGeometry.Dispose(mesh.gameObject); throw; }
        }

        private static TextMesh Text(Transform parent, string content, Vector3 point, float size)
        {
            Transform value = Root(parent, "Control_Label", point);
            try
            {
                // Unity TextMesh's visible front is its local -Z face; all controls face toward the cabin/landing.
                var text = value.gameObject.AddComponent<TextMesh>(); text.text = content; text.font = font;
                text.fontSize = 48; text.characterSize = size * 10f / 48f; text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
                text.color = new Color(.88f, .96f, .91f);
                value.GetComponent<MeshRenderer>().sharedMaterial = textMaterial;
                value.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                value.gameObject.AddComponent<SlopCityWorldText>().Configure(textMaterial);
                value.gameObject.isStatic = false;
                return text;
            }
            catch { SlopCityInteriorGeometry.Dispose(value.gameObject); throw; }
        }

        private static void EnsureAssets()
        {
            if (cube && steel && floorMaterial && dark && buttonMaterial && textMaterial && font) return;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Shader textShader = Shader.Find("SlopCity/WorldText");
            Font readyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!shader || !shader.isSupported || !textShader || !textShader.isSupported || !readyFont)
                throw new InvalidOperationException("Elevator materials or font are unavailable.");
            if (!cube)
            {
                GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
                try { cube = primitive.GetComponent<MeshFilter>().sharedMesh; }
                finally
                {
                    primitive.SetActive(false);
                    if (Application.isPlaying) Destroy(primitive); else DestroyImmediate(primitive);
                }
            }
            Material[] previous = { steel, floorMaterial, dark, buttonMaterial, textMaterial };
            Material[] next = (Material[])previous.Clone();
            string[] names = { "Elevator shared metal", "Elevator shared floor", "Elevator shared shaft", "Elevator shared buttons" };
            Color[] colors = { new Color(.30f, .36f, .35f), new Color(.13f, .17f, .16f), new Color(.045f, .065f, .062f), new Color(.25f, .48f, .40f) };
            try
            {
                for (int index = 0; index < names.Length; index++)
                {
                    if (next[index]) continue;
                    Material material = next[index] = new Material(shader); material.name = names[index];
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colors[index]);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", colors[index]);
                }
                if (!next[4]) { next[4] = new Material(textShader); next[4].name = "Elevator shared occluded text"; }
                steel = next[0]; floorMaterial = next[1]; dark = next[2]; buttonMaterial = next[3]; textMaterial = next[4]; font = readyFont;
            }
            catch
            {
                for (int index = 0; index < next.Length; index++)
                    if (next[index] && next[index] != previous[index])
                    { if (Application.isPlaying) Destroy(next[index]); else DestroyImmediate(next[index]); }
                throw;
            }
        }
    }
}
