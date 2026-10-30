using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopCity
{
    /// <summary>Stable floor descriptions with bounded, deterministic furnishings and retained utility state.</summary>
    public sealed class SlopCityBuildingInterior : MonoBehaviour
    {
        private SlopCityBuildingWorld world;
        private SlopCityFirstPerson player;
        private Vector2 lowerSize, upperSize;
        private float setbackHeight, height;
        private int serial, streamingFloor = -1;
        private int floorAdmissionAfterFrame;
        private bool architectureReady, architectureShellReady, floorRetirementWarning;
        // Failed construction must retain its root even when native cleanup is refused.
        // Only one synchronous floor build can be pending for this building.
        private Transform failedFloorRoot;
        private int failedFloorIndex = -1;
        private MeshFilter[] architectureRollbackFilters;
        private HashSet<MeshFilter> architecturePreviousFilters;
        private int architecturePreviousMeshCount, architectureAdmissionAfterFrame;
        private Transform architectureNewShell, architectureNewEntrance;
        private readonly Dictionary<Renderer, bool> authoredRenderers = new Dictionary<Renderer, bool>();
        private readonly Dictionary<Collider, bool> authoredColliders = new Dictionary<Collider, bool>();
        private readonly Dictionary<Transform, Vector3> authoredRoofPositions = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<int, Transform> loaded = new Dictionary<int, Transform>();
        private readonly HashSet<int> wantedFloors = new HashSet<int>();
        private readonly List<int> retiringFloors = new List<int>();
        private readonly Dictionary<string, SlopCityInteractable.SessionState> utilityState = new Dictionary<string, SlopCityInteractable.SessionState>();
        private readonly List<int> utilityFloorOrder = new List<int>();
        // Loaded floors stay. Recent releases stay so a short elevator trip restores
        // switches. Walking a whole tower must not keep every floor's session state.
        private const int MaximumRetainedUtilityFloors = 8;
        private readonly List<SlopCityElevatorButton> routeButtons = new List<SlopCityElevatorButton>();
        private readonly List<Mesh> ownedFacadeMeshes = new List<Mesh>();
        private readonly Dictionary<MeshFilter, Mesh> originalFacadeMeshes = new Dictionary<MeshFilter, Mesh>();
        private Vector3 preservedLanding;
        public string BuildingId { get; private set; }
        public string Purpose { get; private set; }
        public bool PreservedInterior { get; private set; }
        public bool IsTower => !PreservedInterior;
        public int FloorCount => LocalFloorElevations.Length;
        public int OccupiedFloorCount => IsTower ? FloorCount - 1 : 1;
        public float[] LocalFloorElevations { get; private set; } = new float[0];
        public Vector3 ShaftLocalCenter { get; private set; }
        public SlopCityElevator Elevator { get; private set; }
        public int LoadedFloorCount => loaded.Count;
        public Vector3 EntranceFeet => IsTower ? transform.TransformPoint(new Vector3(0, LocalFloorElevations[0], -lowerSize.y / 2 - .8f)) : preservedLanding;

        internal void InitializePreserved(SlopCityBuildingWorld owner, string purpose, Vector3 landing, Vector2 dimensions)
        {
            world = owner; BuildingId = name; Purpose = purpose; PreservedInterior = true;
            preservedLanding = landing; lowerSize = upperSize = dimensions;
            LocalFloorElevations = new[] { transform.InverseTransformPoint(landing).y };
            Transform marker = SlopCityInteriorGeometry.Root(transform, "Room_Circulation_Point_Preserved");
            marker.position = landing;
            SlopCityInteriorPopulation.RegisterVenue(this);
        }

        internal void InitializeTower(SlopCityBuildingWorld owner, SlopCityFirstPerson user)
        {
            world = owner; player = user; BuildingId = name;
            int separator = name.LastIndexOf('_'); int.TryParse(name.Substring(separator + 1), out serial);
            SlopCityDistrict district = GetComponentInParent<SlopCityDistrict>();
            bool citadel = name == "MilitarySF_CommunicationsCitadel";
            string[] purposes = { "Residential studios", "Repair workshops", "Civic offices", "Music studios", "Residential apartments", "Market workshops", "Signal laboratories", "Publisher offices", "Reading and archive rooms" };
            Purpose = district ? purposes[Mathf.Clamp(district.Index, 0, 8)] : (serial % 3 == 0 ? "Residential apartments" : serial % 3 == 1 ? "Publisher offices" : "Civic service centre");
            if (citadel) Purpose = "Civic signal archives";
            Transform core = transform.Find("Structural_Core");
            if (!citadel && (!core || !core.GetComponent<MeshFilter>() || !core.GetComponent<MeshFilter>().sharedMesh))
                throw new InvalidOperationException("Building lacks metric core: " + name);
            Vector3 dimensions = citadel ? new Vector3(34, 172, 29) : core.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            ValidateDimensions(dimensions);
            lowerSize = new Vector2(dimensions.x, dimensions.z); upperSize = lowerSize;
            height = dimensions.y; setbackHeight = height;
            if (citadel) { upperSize = new Vector2(21.5f, 29); setbackHeight = 108.8f; }
            Transform upper = transform.Find("Setback_UpperShaft");
            if (upper && upper.GetComponent<MeshFilter>())
            {
                if (!upper.GetComponent<MeshFilter>().sharedMesh) throw new InvalidOperationException("Building lacks upper metric mesh: " + name);
                Vector3 upperDimensions = upper.GetComponent<MeshFilter>().sharedMesh.bounds.size;
                ValidateDimensions(upperDimensions);
                upperSize = new Vector2(upperDimensions.x, upperDimensions.z);
                setbackHeight = height; height += upperDimensions.y;
            }
            // Reuse the exact authored 3.2m story pitch. A final partial crown becomes the top story's ceiling.
            int stories = Mathf.Max(1, Mathf.FloorToInt((height + .01f) / 3.2f));
            LocalFloorElevations = new float[stories + 1];
            for (int floor = 0; floor < stories; floor++) LocalFloorElevations[floor] = .08f + floor * 3.2f;
            LocalFloorElevations[stories] = height + .14f;
            ShaftLocalCenter = new Vector3(0, 0, upperSize.y / 2 - 1.35f);
            // A phone starts on the home couch. Register every floor and its route
            // controls now, but leave distant towers in their authored solid state
            // until their entrance is approached. Creating all city shells, doors
            // and signs in FirstPerson.Start otherwise overlaps initial scene load.
            if (!owner.DeferTowerArchitecture) EnsureArchitecture();
            BindExistingRoutes();
        }

        internal void EnsureArchitecture()
        {
            if (architectureReady || PreservedInterior) return;
            if (architectureRollbackFilters != null && !RollbackArchitecture())
                throw new InvalidOperationException("Failed building architecture is still retiring.");
            if (Application.isPlaying && Time.frameCount < architectureAdmissionAfterFrame)
                throw new InvalidOperationException("Building architecture retirement is completing this frame.");
            Transform core = transform.Find("Structural_Core");
            Transform upper = transform.Find("Setback_UpperShaft");
            if (!architectureShellReady)
            {
                // Every trim changes an existing authored filter. Snapshot those owners
                // before mutation so rollback needs no LINQ/array allocation under pressure.
                MeshFilter[] rollbackFilters = transform.GetComponentsInChildren<MeshFilter>(true);
                var previousFilters = new HashSet<MeshFilter>(originalFacadeMeshes.Keys);
                architectureRollbackFilters = rollbackFilters;
                architecturePreviousFilters = previousFilters;
                architecturePreviousMeshCount = ownedFacadeMeshes.Count;
                try
                {
                    TrimGroundFacade();
                    if (world && world.DeferTowerArchitecture) TrimEntranceCore(core);
                    BuildShell();
                }
                catch
                {
                    RollbackArchitecture();
                    throw;
                }
                architectureRollbackFilters = null; architecturePreviousFilters = null;
                // Later authored-geometry cleanup can fail too. Its retry must finish
                // this completed shell instead of admitting another native hierarchy.
                architectureShellReady = true;
            }
            // Existing collision is retired only after its replacement shell is complete.
            if (upper) DisableAuthoredSolid(upper);
            if (core) DisableAuthoredSolid(core);
            foreach (Transform item in transform.GetComponentsInChildren<Transform>(true))
                if (item.name.StartsWith("Facade_Frames_", StringComparison.Ordinal) || item.name.StartsWith("Podium_Joints_", StringComparison.Ordinal)
                    || item.name == "Recessed_Floor_Band" || item.name == "Podium_Cornice" || item.name == "Rooftop_Deck" || item.name == "Lobby_Entrance_Recess"
                    || item.name == "Lobby_Glazing" || item.name == "Lobby_DoorRail") DisableAuthoredSolid(item);
            ClearRoofEquipment();
            architectureReady = true;
            architectureNewShell = architectureNewEntrance = null;
            world.GeometryChanged();
        }

        private bool RollbackArchitecture()
        {
            bool pending = false;
            // Removing dictionary entries is safe while walking the earlier array.
            // Preserve all bindings and meshes that predate this exact attempt.
            foreach (MeshFilter filter in architectureRollbackFilters)
            {
                if (ReferenceEquals(filter, null) || architecturePreviousFilters.Contains(filter)
                    || !originalFacadeMeshes.TryGetValue(filter, out Mesh original)) continue;
                try
                {
                    if (filter) filter.sharedMesh = original;
                    originalFacadeMeshes.Remove(filter);
                }
                catch (Exception error) { pending = true; WarnFloorRetirement(error); }
            }
            for (int index = ownedFacadeMeshes.Count - 1; index >= architecturePreviousMeshCount; index--)
            {
                Mesh mesh = ownedFacadeMeshes[index];
                bool stillBound = false;
                foreach (MeshFilter filter in architectureRollbackFilters)
                {
                    if (ReferenceEquals(filter, null) || architecturePreviousFilters.Contains(filter)
                        || !originalFacadeMeshes.ContainsKey(filter)) continue;
                    try { if (filter && filter.sharedMesh == mesh) { stillBound = true; break; } }
                    catch (Exception error) { stillBound = true; WarnFloorRetirement(error); break; }
                }
                if (stillBound) { pending = true; continue; }
                try
                {
                    if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
                    ownedFacadeMeshes.RemoveAt(index);
                }
                catch (Exception error) { pending = true; WarnFloorRetirement(error); }
            }
            if (!RetireArchitectureRoot(ref architectureNewShell, "Retired_Hollow_Building_Shell")) pending = true;
            if (!RetireArchitectureRoot(ref architectureNewEntrance, "Retired_Persistent_Building_Entrance")) pending = true;
            if (pending) return false;
            architectureRollbackFilters = null; architecturePreviousFilters = null;
            if (Application.isPlaying) architectureAdmissionAfterFrame = Time.frameCount + 1;
            return true;
        }

        private bool RetireArchitectureRoot(ref Transform owner, string retiredName)
        {
            if (!owner) { owner = null; return true; }
            try
            {
                try { owner.name = retiredName; }
                finally { SlopCityInteriorGeometry.Dispose(owner.gameObject); }
                owner = null;
                return true;
            }
            catch (Exception error)
            {
                WarnFloorRetirement(error);
                if (!owner) { owner = null; return true; }
                return false;
            }
        }

        private static void ValidateDimensions(Vector3 dimensions)
        {
            if (float.IsNaN(dimensions.sqrMagnitude) || float.IsInfinity(dimensions.sqrMagnitude)
                || dimensions.x < 3f || dimensions.z < 3f || dimensions.y <= 0f || dimensions.y > 1000f)
                throw new InvalidOperationException("Building metric geometry is invalid.");
        }

        public Vector2 GetFloorSize(int floor)
        {
            if (floor < 0 || floor >= FloorCount) throw new ArgumentOutOfRangeException(nameof(floor));
            return LocalFloorElevations[floor] >= setbackHeight - .01f ? upperSize : lowerSize;
        }

        public Vector3 LandingFeet(int floor)
        {
            if (PreservedInterior) return preservedLanding;
            return transform.TransformPoint(new Vector3(ShaftLocalCenter.x, LocalFloorElevations[floor] + .015f, ShaftLocalCenter.z - 1.75f));
        }

        public bool IsFloorLoaded(int floor) => PreservedInterior
            || (loaded.TryGetValue(floor, out Transform root) && root && root.gameObject.activeInHierarchy);
        public Transform EnsureFloor(int floor)
        {
            if (floor < 0 || floor >= FloorCount) throw new ArgumentOutOfRangeException(nameof(floor));
            if (PreservedInterior) return transform;
            if (loaded.TryGetValue(floor, out Transform existing) && existing && existing.gameObject.activeInHierarchy) return existing;
            if (!ReleaseFailedFloor() || !CanAdmitFloorDetail(Time.frameCount))
                throw new InvalidOperationException("A retired floor is still releasing; new floor admission is deferred.");
            EnsureArchitecture();
            if (loaded.TryGetValue(floor, out existing))
            {
                // A native disposal may have deactivated the old root before
                // throwing. It remains owned, but is no longer usable support.
                ReleaseFloor(floor);
                if (loaded.ContainsKey(floor) || !CanAdmitFloorDetail(Time.frameCount))
                    throw new InvalidOperationException("The previous floor is still retiring.");
            }
            Transform root = SlopCityInteriorGeometry.Root(transform, "Runtime_Floor_" + floor.ToString("D2"), Vector3.up * LocalFloorElevations[floor]);
            try
            {
                Vector2 size = GetFloorSize(floor);
                if (floor > 0 && floor < FloorCount - 1)
                    SlopCityInteriorGeometry.Slab(root, "Occupied_Floor_Slab", 0, size, new Vector2(ShaftLocalCenter.x, ShaftLocalCenter.z), SlopCityInteriorGeometry.Floor);
                // Stream the ceiling of the current occupied room too, leaving the complete vertical shaft open.
                if (floor < FloorCount - 1)
                {
                    float ceiling = LocalFloorElevations[floor + 1] - LocalFloorElevations[floor] - .16f;
                    SlopCityInteriorGeometry.Slab(root, "Occupied_Room_Ceiling", ceiling, size, new Vector2(ShaftLocalCenter.x, ShaftLocalCenter.z), SlopCityInteriorGeometry.Wall, .06f);
                    BuildRooms(root, floor, size);
                }
                else BuildRoof(root, size);
                foreach (SlopCityInteractable item in root.GetComponentsInChildren<SlopCityInteractable>())
                    if (utilityState.TryGetValue(floor + "/" + item.name, out SlopCityInteractable.SessionState state)) item.RestoreSessionState(state);
                world.GeometryChanged();
                loaded[floor] = root;
                TouchUtilityFloor(floor);
                SlopCityInteriorPopulation.AdmitFloor(this, floor, root);
                return root;
            }
            catch
            {
                // AdmitFloor can fail after publication. Remove that usable-floor
                // identity, but transfer the exact root to another owner first.
                failedFloorRoot = root; failedFloorIndex = floor;
                if (loaded.TryGetValue(floor, out Transform published) && published == root) loaded.Remove(floor);
                ReleaseFailedFloor();
                throw;
            }
        }

        public void ReleaseFloor(int floor)
        {
            if (!loaded.TryGetValue(floor, out Transform root)) return;
            try
            {
                // Capture/renderer work may throw. Keep the registry owner until
                // retirement actually finishes so a retry cannot duplicate this room.
                if (root)
                    foreach (SlopCityInteractable item in root.GetComponentsInChildren<SlopCityInteractable>())
                        utilityState[floor + "/" + item.name] = item.CaptureSessionState();
                SlopCityInteriorPopulation.RetireFloor(this, floor);
                if (root)
                {
                    SlopCityInteriorBroadcast.ReleaseFloor(root);
                    root.GetComponent<SlopCityInteriorFurnishing>()?.Release();
                    root.name = "Retired_Runtime_Floor_" + floor + "_" + root.GetInstanceID();
                    SlopCityInteriorGeometry.Dispose(root.gameObject);
                    DeferFloorAdmission();
                }
                loaded.Remove(floor);
                floorRetirementWarning = false;
                world.GeometryChanged();
                TouchUtilityFloor(floor);
            }
            catch (Exception error) { WarnFloorRetirement(error); }
        }

        private void TouchUtilityFloor(int floor)
        {
            utilityFloorOrder.Remove(floor);
            utilityFloorOrder.Add(floor);
            int index = 0;
            while (utilityFloorOrder.Count > MaximumRetainedUtilityFloors)
            {
                if (index >= utilityFloorOrder.Count) return;
                int candidate = utilityFloorOrder[index];
                if (loaded.ContainsKey(candidate) || candidate == failedFloorIndex)
                {
                    index++;
                    continue;
                }
                utilityFloorOrder.RemoveAt(index);
                ForgetUtilityFloor(candidate);
            }
        }

        private void ForgetUtilityFloor(int floor)
        {
            string prefix = floor + "/";
            var drop = new List<string>();
            foreach (string key in utilityState.Keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal)) drop.Add(key);
            for (int index = 0; index < drop.Count; index++) utilityState.Remove(drop[index]);
        }

        private bool ReleaseFailedFloor()
        {
            Transform root = failedFloorRoot;
            if (!root) { failedFloorRoot = null; failedFloorIndex = -1; return true; }
            bool disposedRoot = false;
            try
            {
                // Attempt independent owners, but never destroy their last retry
                // handle while any native furniture/media cleanup is still pending.
                bool released = true;
                try { root.gameObject.SetActive(false); }
                catch (Exception error) { released = false; WarnFloorRetirement(error); }
                try { SlopCityInteriorPopulation.RetireFloor(this, failedFloorIndex); }
                catch (Exception error) { released = false; WarnFloorRetirement(error); }
                try { SlopCityInteriorBroadcast.ReleaseFloor(root); }
                catch (Exception error) { released = false; WarnFloorRetirement(error); }
                try { root.GetComponent<SlopCityInteriorFurnishing>()?.Release(); }
                catch (Exception error) { released = false; WarnFloorRetirement(error); }
                if (!released) return false;
                root.name = "Retired_Runtime_Floor_" + failedFloorIndex + "_" + root.GetInstanceID();
                SlopCityInteriorGeometry.Dispose(root.gameObject);
                disposedRoot = true;
            }
            catch (Exception error) { WarnFloorRetirement(error); }
            if (disposedRoot || !root)
            {
                failedFloorRoot = null; failedFloorIndex = -1;
                DeferFloorAdmission();
                if (world) world.GeometryChanged();
                return true;
            }
            return false;
        }

        private void WarnFloorRetirement(Exception error)
        {
            if (floorRetirementWarning) return;
            floorRetirementWarning = true;
            Debug.LogWarning("Floor retirement interrupted; resources stay owned for retry: " + error.GetType().Name, this);
        }

        public SlopCityElevator EnsureElevator()
        {
            if (PreservedInterior) return null;
            EnsureArchitecture();
            if (Elevator) { Elevator.gameObject.SetActive(true); return Elevator; }
            string[] labels = LocalFloorElevations.Select((value, index) => index == FloorCount - 1 ? "ROOF" : index == 0 ? "LOBBY" : "FLOOR " + index).ToArray();
            Elevator = SlopCityElevator.Create(transform, ShaftLocalCenter, LocalFloorElevations, labels, player, null,
                world.DeferTowerArchitecture, world.GeometryChanged, PrepareFloor);
            foreach (SlopCityElevatorButton button in routeButtons)
            {
                if (!button) continue;
                bool roof = button.name == "Tower_Lift_Roof";
                button.Configure(Elevator, SlopCityElevatorButtonKind.Call, roof ? FloorCount - 1 : 0);
            }
            world.GeometryChanged();
            return Elevator;
        }

        private void DeferFloorAdmission() => NoteFloorRetirement(Time.frameCount, Application.isPlaying);

        internal void NoteFloorRetirement(int frame, bool deferredDestruction)
        {
            if (world && world.DeferTowerArchitecture && deferredDestruction)
                floorAdmissionAfterFrame = Mathf.Max(floorAdmissionAfterFrame, frame + 1);
        }

        internal bool CanAdmitFloorDetail(int frame)
            => !world || !world.DeferTowerArchitecture || frame >= floorAdmissionAfterFrame;

        private bool PrepareFloor(int destination)
        {
            // Existing support remains immediately usable during retirement.
            if (IsFloorLoaded(destination)) return true;
            // Keep the player's current supported room when an empty car is summoned elsewhere.
            // Reserve the current room and destination before allocating a new furnished floor.
            int budget = world ? world.DetailedFloorBudget : 3;
            int occupied = -1;
            if (player && ContainsHorizontal(player.transform.position, 0))
            {
                float localHeight = transform.InverseTransformPoint(player.transform.position).y;
                occupied = 0;
                for (int index = 1; index < FloorCount; index++)
                    if (Mathf.Abs(LocalFloorElevations[index] - localHeight) < Mathf.Abs(LocalFloorElevations[occupied] - localHeight)) occupied = index;
            }
            while (loaded.Count >= budget && !loaded.ContainsKey(destination))
            {
                int farthest = -1, distance = -1;
                foreach (int floor in loaded.Keys)
                    if (floor != destination && floor != occupied && Mathf.Abs(floor - destination) > distance)
                    { farthest = floor; distance = Mathf.Abs(floor - destination); }
                if (farthest < 0) break;
                int previousCount = loaded.Count;
                ReleaseFloor(farthest);
                if (loaded.Count == previousCount) break;
            }
            if (loaded.Count >= budget || !CanAdmitFloorDetail(Time.frameCount)) return false;
            EnsureFloor(destination);
            return true;
        }

        internal void StreamAround(Vector3 worldFeet, int count)
        {
            Vector3 local = transform.InverseTransformPoint(worldFeet);
            int nearest = 0;
            for (int floor = 1; floor < FloorCount; floor++)
                if (Mathf.Abs(LocalFloorElevations[floor] - local.y) < Mathf.Abs(LocalFloorElevations[nearest] - local.y)) nearest = floor;
            // A small boundary margin prevents jumping or standing on a floor midpoint
            // from destroying and rebuilding the same furnishings every streaming tick.
            if (streamingFloor >= 0 && streamingFloor < FloorCount
                && Mathf.Abs(LocalFloorElevations[streamingFloor] - local.y)
                    <= Mathf.Abs(LocalFloorElevations[nearest] - local.y) + .35f) nearest = streamingFloor;
            streamingFloor = nearest;
            wantedFloors.Clear();
            bool supportedByMovingCar = world && world.DeferTowerArchitecture
                && Elevator && Elevator.IsMoving && Elevator.PlayerInside;
            // A rider already has the car's solid support. Passing a floor in
            // its closed shaft does not require constructing that floor's rooms.
            if (!supportedByMovingCar) wantedFloors.Add(nearest);
            if (Elevator) wantedFloors.Add(Elevator.IsMoving ? Elevator.TargetFloor : Elevator.CurrentFloor);
            // The floor budget is a ceiling. Touch walking needs its supported
            // room and the lift's destination, not a speculative furnished neighbour
            // (including upstairs while merely approaching the outdoor doorway).
            if (!world || !world.DeferTowerArchitecture)
            {
                if (wantedFloors.Count < count && nearest > 0) wantedFloors.Add(nearest - 1);
                if (wantedFloors.Count < count && nearest + 1 < FloorCount) wantedFloors.Add(nearest + 1);
            }
            retiringFloors.Clear();
            foreach (int floor in loaded.Keys) if (!wantedFloors.Contains(floor)) retiringFloors.Add(floor);
            foreach (int floor in retiringFloors) ReleaseFloor(floor);
            // Furnishing meshes use deferred Destroy in a player. Let those native
            // resources retire at frame end before admitting replacement rooms.
            foreach (int floor in retiringFloors) if (loaded.ContainsKey(floor)) return;
            if (!CanAdmitFloorDetail(Time.frameCount)) return;
            foreach (int floor in wantedFloors) EnsureFloor(floor);
        }

        internal bool ReleaseStream()
        {
            if (player && ContainsInterior(player.transform.position)) return false;
            if (Elevator && !Elevator.ReleaseEmpty()) return false;
            retiringFloors.Clear();
            foreach (int floor in loaded.Keys) retiringFloors.Add(floor);
            foreach (int floor in retiringFloors) ReleaseFloor(floor);
            if (loaded.Count != 0 || !ReleaseFailedFloor()) return false;
            streamingFloor = -1;
            if (Elevator)
            {
                foreach (SlopCityElevatorButton button in routeButtons)
                    if (button) button.Configure(null, SlopCityElevatorButtonKind.Call, button.name == "Tower_Lift_Roof" ? FloorCount - 1 : 0);
                SlopCityInteriorGeometry.Dispose(Elevator.gameObject);
                Elevator = null;
                world.GeometryChanged();
            }
            if (world && world.DeferTowerArchitecture) ReleaseArchitecture();
            return true;
        }

        private void DisableAuthoredSolid(Transform item)
        {
            if (world && world.DeferTowerArchitecture)
            {
                foreach (Renderer renderer in item.GetComponents<Renderer>())
                    if (!authoredRenderers.ContainsKey(renderer)) authoredRenderers.Add(renderer, renderer.enabled);
                foreach (Collider collider in item.GetComponents<Collider>())
                    if (!authoredColliders.ContainsKey(collider)) authoredColliders.Add(collider, collider.enabled);
            }
            SlopCityInteriorGeometry.DisableSolid(item);
        }

        internal bool ReleaseArchitecture()
        {
            if (!architectureReady || PreservedInterior || !world || !world.DeferTowerArchitecture || Elevator || loaded.Count != 0) return false;
            // A nearby entrance/landing is still usable while its lift is being admitted.
            // Never replace its supporting shell with the authored solid around a user.
            if (player && ContainsHorizontal(player.transform.position, 2f)) return false;
            // Keep only the small doorway-cut authored meshes beside the persistent
            // door. Restoring either the opaque core face or the decorative lobby
            // panels would put a visual wall across that same physical door again.
            // The full hollow shell, floors and lift still retire below. Original
            // authored colliders return as the safe distant streaming fallback.
            foreach (var entry in authoredRenderers)
                if (entry.Key) entry.Key.enabled = entry.Value && !IsObsoleteEntranceVisual(entry.Key.transform);
            foreach (var entry in authoredColliders) if (entry.Key) entry.Key.enabled = entry.Value;
            foreach (var entry in authoredRoofPositions) if (entry.Key) entry.Key.localPosition = entry.Value;
            authoredRenderers.Clear(); authoredColliders.Clear(); authoredRoofPositions.Clear();
            // A refused native retirement must keep the exact renamed shell reachable;
            // looking up only its former name on retry would lose its ownership.
            if (!architectureNewShell) architectureNewShell = transform.Find("Runtime_Hollow_Building_Shell");
            if (!RetireArchitectureRoot(ref architectureNewShell, "Retired_Hollow_Building_Shell")) return false;
            // The small persistent entrance owns the original hinge/door component;
            // its rotation, obstruction state and target are never recreated or reset.
            architectureReady = false; architectureShellReady = false;
            if (Application.isPlaying) architectureAdmissionAfterFrame = Time.frameCount + 1;
            world.GeometryChanged();
            return true;
        }

        internal bool ContainsInterior(Vector3 point)
        {
            if (PreservedInterior || !ContainsHorizontal(point, -.10f)) return false;
            float elevation = transform.InverseTransformPoint(point).y;
            return elevation >= LocalFloorElevations[0] - .25f && elevation <= LocalFloorElevations[FloorCount - 1] + 2.7f;
        }

        internal bool ContainsHorizontal(Vector3 point, float expansion)
        {
            Vector3 local = transform.InverseTransformPoint(point);
            return Mathf.Abs(local.x) <= lowerSize.x / 2 + expansion && Mathf.Abs(local.z) <= lowerSize.y / 2 + expansion;
        }
        internal float HorizontalDistanceSquared(Vector3 point)
        {
            Vector3 local = transform.InverseTransformPoint(point);
            float x = Mathf.Max(0, Mathf.Abs(local.x) - lowerSize.x / 2), z = Mathf.Max(0, Mathf.Abs(local.z) - lowerSize.y / 2);
            return x * x + z * z;
        }

        private void BuildShell()
        {
            Transform shell = SlopCityInteriorGeometry.Root(transform, "Runtime_Hollow_Building_Shell");
            architectureNewShell = shell;
            ShellTier(shell, lowerSize, .08f, setbackHeight, true);
            if (height > setbackHeight + .01f)
            {
                ShellTier(shell, upperSize, setbackHeight, height, false);
                // Exterior terrace ledge at the setback cannot expose a fall through the lower shell.
                SlopCityInteriorGeometry.Slab(shell, "Setback_Terrace_Slab", setbackHeight, lowerSize,
                    new Vector2(ShaftLocalCenter.x, ShaftLocalCenter.z), SlopCityInteriorGeometry.Floor);
            }
            SlopCityInteriorGeometry.Slab(shell, "Permanent_Ground_Slab", LocalFloorElevations[0], lowerSize,
                new Vector2(ShaftLocalCenter.x, ShaftLocalCenter.z), SlopCityInteriorGeometry.Floor);
            SlopCityInteriorGeometry.Slab(shell, "Permanent_Roof_Slab", LocalFloorElevations[FloorCount - 1], upperSize,
                new Vector2(ShaftLocalCenter.x, ShaftLocalCenter.z), SlopCityInteriorGeometry.Floor);
            // Roof headhouse protects the open shaft and leaves a full-height cabin/landing opening.
            float top = LocalFloorElevations[FloorCount - 1];
            SlopCityInteriorGeometry.Box(shell, "Lift_Headhouse_Cap", ShaftLocalCenter + Vector3.up * (top + 2.85f),
                new Vector3(2.45f, .18f, 2.45f), SlopCityInteriorGeometry.Metal, true);
            Transform entrance = shell;
            if (world && world.DeferTowerArchitecture)
            {
                entrance = transform.Find("Runtime_Persistent_Building_Entrance");
                if (entrance)
                {
                    SlopCityEntranceBroadcast.Attach(entrance, lowerSize.y);
                    return;
                }
                entrance = SlopCityInteriorGeometry.Root(transform, "Runtime_Persistent_Building_Entrance");
                architectureNewEntrance = entrance;
            }
            SlopCityInteriorGeometry.Sign(entrance, "SLOP CITY™\n" + Purpose.ToUpperInvariant(), new Vector3(0, 2.75f, -lowerSize.y / 2 - .12f), .15f);
            SlopCityInteriorGeometry.Root(entrance, "Entrance_Circulation_Point", new Vector3(0, .095f, -lowerSize.y / 2 + .6f));
            // Keep the entrance in the permanent shell: distant floor streaming must
            // never make a closed street door vanish or reset a door the player opened.
            HingedDoor(entrance, "Building_Entrance_Door", new Vector3(-.8f, .08f, -lowerSize.y / 2),
                1.6f, false, 95f, false, Purpose.ToUpperInvariant() + " / ENTRY", SlopCityInteriorGeometry.Metal, 2.5f);
            foreach (float side in new[] { -1f, 1f })
                SlopCityInteriorGeometry.Box(entrance, "Entrance_Door_Jamb", new Vector3(side * .835f, 1.33f, -lowerSize.y / 2),
                    new Vector3(.07f, 2.5f, .16f), SlopCityInteriorGeometry.Metal);
            SlopCityEntranceBroadcast.Attach(entrance, lowerSize.y);
        }

        private static bool IsObsoleteEntranceVisual(Transform item) => item.name == "Lobby_Entrance_Recess"
            || item.name == "Lobby_Glazing" || item.name == "Lobby_DoorRail";

        private struct EntranceVertex
        {
            public Vector3 point, weights;
            public static EntranceVertex Lerp(EntranceVertex a, EntranceVertex b, float t) => new EntranceVertex
            {
                point = Vector3.LerpUnclamped(a.point, b.point, t),
                weights = Vector3.LerpUnclamped(a.weights, b.weights, t)
            };
        }

        private void TrimEntranceCore(Transform core)
        {
            MeshFilter filter = core ? core.GetComponent<MeshFilter>() : null;
            if (!filter || !filter.sharedMesh || originalFacadeMeshes.ContainsKey(filter)) return;
            Mesh source = filter.sharedMesh;
            if (!source.isReadable) throw new InvalidOperationException("Building entrance core is not readable: " + name);
            // The authored rounded core is a small exterior mesh. Cut only the
            // southern doorway thickness; preserve its sides, silhouette, materials
            // and the separate authored collider used when detail is unloaded.
            Bounds opening = new Bounds(new Vector3(0, 1.365f, -lowerSize.y / 2), new Vector3(1.62f, 2.57f, .6f));
            Matrix4x4 toBuilding = transform.worldToLocalMatrix * core.localToWorldMatrix;
            Vector3[] positions = source.vertices, sourceNormals = source.normals;
            Vector4[] sourceTangents = source.tangents;
            Color[] sourceColors = source.colors;
            var vertices = new List<Vector3>(positions);
            var normals = new List<Vector3>(sourceNormals);
            var tangents = new List<Vector4>(sourceTangents);
            var colors = new List<Color>(sourceColors);
            var uvs = new List<Vector4>[8];
            for (int channel = 0; channel < uvs.Length; channel++)
            {
                uvs[channel] = new List<Vector4>(); source.GetUVs(channel, uvs[channel]);
            }
            var indices = new List<int>[source.subMeshCount];
            for (int submesh = 0; submesh < indices.Length; submesh++)
            {
                int[] triangles = source.GetTriangles(submesh);
                indices[submesh] = new List<int>(triangles.Length);
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    int a = triangles[index], b = triangles[index + 1], c = triangles[index + 2];
                    var triangle = new List<EntranceVertex>
                    {
                        new EntranceVertex { point = toBuilding.MultiplyPoint3x4(positions[a]), weights = Vector3.right },
                        new EntranceVertex { point = toBuilding.MultiplyPoint3x4(positions[b]), weights = Vector3.up },
                        new EntranceVertex { point = toBuilding.MultiplyPoint3x4(positions[c]), weights = Vector3.forward }
                    };
                    Bounds bounds = new Bounds(triangle[0].point, Vector3.zero);
                    bounds.Encapsulate(triangle[1].point); bounds.Encapsulate(triangle[2].point);
                    if (!bounds.Intersects(opening))
                    {
                        indices[submesh].Add(a); indices[submesh].Add(b); indices[submesh].Add(c);
                        continue;
                    }
                    var pieces = new List<List<EntranceVertex>>();
                    List<EntranceVertex> inside = triangle;
                    // Disjoint outside fragments retain the source triangle winding.
                    // The final inside fragment alone is the doorway and is omitted.
                    for (int plane = 0; plane < 6 && inside.Count >= 3; plane++)
                    {
                        int axis = plane / 2;
                        float coordinate = plane % 2 == 0 ? opening.min[axis] : opening.max[axis];
                        SplitEntrance(inside, axis, coordinate, plane % 2 == 0 ? 1f : -1f,
                            out List<EntranceVertex> next, out List<EntranceVertex> outside);
                        if (outside.Count >= 3) pieces.Add(outside);
                        inside = next;
                    }
                    foreach (List<EntranceVertex> polygon in pieces)
                        for (int corner = 1; corner + 1 < polygon.Count; corner++)
                        {
                            EntranceVertex first = polygon[0], second = polygon[corner], third = polygon[corner + 1];
                            if (Vector3.Cross(second.point - first.point, third.point - first.point).sqrMagnitude < 1e-12f) continue;
                            foreach (EntranceVertex vertex in new[] { first, second, third })
                            {
                                Vector3 w = vertex.weights;
                                indices[submesh].Add(vertices.Count);
                                vertices.Add(positions[a] * w.x + positions[b] * w.y + positions[c] * w.z);
                                if (sourceNormals.Length == positions.Length)
                                    normals.Add((sourceNormals[a] * w.x + sourceNormals[b] * w.y + sourceNormals[c] * w.z).normalized);
                                if (sourceTangents.Length == positions.Length)
                                    tangents.Add(sourceTangents[a] * w.x + sourceTangents[b] * w.y + sourceTangents[c] * w.z);
                                if (sourceColors.Length == positions.Length)
                                    colors.Add(sourceColors[a] * w.x + sourceColors[b] * w.y + sourceColors[c] * w.z);
                                foreach (List<Vector4> channel in uvs)
                                    if (channel.Count >= positions.Length)
                                        channel.Add(channel[a] * w.x + channel[b] * w.y + channel[c] * w.z);
                            }
                        }
                }
            }
            Mesh mesh = Instantiate(source);
            try { ownedFacadeMeshes.Add(mesh); }
            catch { ownedFacadeMeshes.Remove(mesh); if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); throw; }
            mesh.name = "Runtime_Entrance_Core_" + serial;
            mesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            if (sourceNormals.Length == positions.Length) mesh.SetNormals(normals);
            if (sourceTangents.Length == positions.Length) mesh.SetTangents(tangents);
            if (sourceColors.Length == positions.Length) mesh.SetColors(colors);
            for (int channel = 0; channel < uvs.Length; channel++)
                if (uvs[channel].Count == vertices.Count)
                {
                    int dimension = source.GetVertexAttributeDimension((UnityEngine.Rendering.VertexAttribute)((int)UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel));
                    if (dimension == 2) mesh.SetUVs(channel, uvs[channel].Select(value => new Vector2(value.x, value.y)).ToList());
                    else if (dimension == 3) mesh.SetUVs(channel, uvs[channel].Select(value => new Vector3(value.x, value.y, value.z)).ToList());
                    else mesh.SetUVs(channel, uvs[channel]);
                }
            for (int submesh = 0; submesh < indices.Length; submesh++) mesh.SetTriangles(indices[submesh], submesh, false);
            // Original bounds remain metric input even after the doorway is cut.
            mesh.bounds = source.bounds;
            originalFacadeMeshes.Add(filter, source);
            filter.sharedMesh = mesh;
            mesh.UploadMeshData(true);
        }

        private static void SplitEntrance(List<EntranceVertex> polygon, int axis, float coordinate, float sign,
            out List<EntranceVertex> inside, out List<EntranceVertex> outside)
        {
            inside = new List<EntranceVertex>(); outside = new List<EntranceVertex>();
            EntranceVertex previous = polygon[polygon.Count - 1];
            float previousDistance = sign * (previous.point[axis] - coordinate);
            foreach (EntranceVertex current in polygon)
            {
                float distance = sign * (current.point[axis] - coordinate);
                bool previousInside = previousDistance >= 0, currentInside = distance >= 0;
                if (previousInside != currentInside)
                {
                    EntranceVertex crossing = EntranceVertex.Lerp(previous, current, previousDistance / (previousDistance - distance));
                    inside.Add(crossing); outside.Add(crossing);
                }
                (currentInside ? inside : outside).Add(current);
                previous = current; previousDistance = distance;
            }
        }

        private void TrimGroundFacade()
        {
            // Preserve one-sided exterior window art. Its back faces do not obstruct an interior view.
            // Only the horizon's ground-level front plane needs a true entrance opening.
            if (!BuildingId.StartsWith("Skyline_", StringComparison.Ordinal)) return;
            foreach (Transform surface in transform.GetComponentsInChildren<Transform>())
            {
                if ((surface.name != "CurtainWall_Panel" && surface.name != "Masonry_WindowBays")
                    || Mathf.Abs(surface.localPosition.z + lowerSize.y / 2 + .018f) > .05f || surface.localPosition.y > setbackHeight) continue;
                MeshFilter filter = surface.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh || originalFacadeMeshes.ContainsKey(filter)) continue;
                if (!filter.sharedMesh.isReadable) throw new InvalidOperationException("Building entrance mesh is not readable: " + surface.name);
                Vector3[] source = filter.sharedMesh.vertices; Vector2[] sourceUv = filter.sharedMesh.uv;
                if (source.Length % 4 != 0 || sourceUv.Length != source.Length)
                    throw new InvalidOperationException("Building entrance mesh has invalid facade coordinates: " + surface.name);
                var vertices = new List<Vector3>(); var uvs = new List<Vector2>(); var indices = new List<int>();
                Rect doorway = Rect.MinMaxRect(-.81f, .08f - surface.localPosition.y, .81f, 2.65f - surface.localPosition.y);
                for (int quad = 0; quad + 3 < source.Length; quad += 4)
                {
                    Rect bounds = Rect.MinMaxRect(source[quad].x, source[quad].y, source[quad + 3].x, source[quad + 3].y);
                    if (bounds.width <= 0 || bounds.height <= 0) throw new InvalidOperationException("Building facade has a degenerate panel.");
                    foreach (Rect piece in SlopCityInteriorGeometry.Subtract(bounds, doorway))
                    {
                        int first = vertices.Count;
                        foreach (Vector2 point in new[] { new Vector2(piece.xMin, piece.yMin), new Vector2(piece.xMax, piece.yMin), new Vector2(piece.xMin, piece.yMax), new Vector2(piece.xMax, piece.yMax) })
                        {
                            vertices.Add(new Vector3(point.x, point.y, source[quad].z));
                            Vector2 fraction = new Vector2((point.x - bounds.xMin) / bounds.width, (point.y - bounds.yMin) / bounds.height);
                            uvs.Add(sourceUv[quad] + Vector2.Scale(sourceUv[quad + 3] - sourceUv[quad], fraction));
                        }
                        indices.AddRange(new[] { first, first + 2, first + 1, first + 2, first + 3, first + 1 });
                    }
                }
                Mesh mesh = new Mesh();
                try { ownedFacadeMeshes.Add(mesh); }
                catch { ownedFacadeMeshes.Remove(mesh); if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); throw; }
                mesh.name = "Runtime_Entrance_Facade_" + serial;
                mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds(); mesh.RecalculateNormals();
                originalFacadeMeshes[filter] = filter.sharedMesh;
                filter.sharedMesh = mesh;
                if (world && world.DeferTowerArchitecture) mesh.UploadMeshData(true);
            }
        }

        private void ClearRoofEquipment()
        {
            Vector3 crownShift = Vector3.zero;
            if (world && world.DeferTowerArchitecture)
                foreach (Transform piece in transform)
                    if (piece.name == "Mechanical_Unit" || piece.name == "HVAC_Louvre" || piece.name == "Roof_Plant_Screen"
                        || piece.name == "Mechanical_Crown" || piece.name == "Crown_Louvre")
                        if (!authoredRoofPositions.ContainsKey(piece)) authoredRoofPositions.Add(piece, piece.localPosition);
            foreach (Transform piece in transform)
            {
                Vector3 point = piece.localPosition;
                if (piece.name == "Mechanical_Unit" || piece.name == "HVAC_Louvre")
                {
                    point.x = upperSize.x / 2 - .60f;
                    piece.localPosition = point;
                }
                else if (piece.name == "Roof_Plant_Screen")
                {
                    point.x = -upperSize.x / 2 + .30f;
                    piece.localPosition = point;
                }
                else if (piece.name == "Mechanical_Crown")
                {
                    MeshFilter filter = piece.GetComponent<MeshFilter>();
                    float depth = filter && filter.sharedMesh ? filter.sharedMesh.bounds.size.z : upperSize.y * .64f;
                    point.z = -upperSize.y / 2 + depth / 2 + .25f;
                    crownShift = point - piece.localPosition;
                    piece.localPosition = point;
                }
            }
            foreach (Transform piece in transform)
                if (piece.name == "Crown_Louvre") piece.localPosition += crownShift;
        }

        private void OnDestroy()
        {
            utilityState.Clear();
            utilityFloorOrder.Clear();
            // Initialization failure can remove this component while its authored
            // tower survives. Restore authored state independently, then retire only
            // the new roots still owned by incomplete architecture initialization.
            foreach (var entry in authoredRenderers)
            {
                try { if (entry.Key) entry.Key.enabled = entry.Value; }
                catch (Exception error) { WarnFloorRetirement(error); }
            }
            foreach (var entry in authoredColliders)
            {
                try { if (entry.Key) entry.Key.enabled = entry.Value; }
                catch (Exception error) { WarnFloorRetirement(error); }
            }
            foreach (var entry in authoredRoofPositions)
            {
                try { if (entry.Key) entry.Key.localPosition = entry.Value; }
                catch (Exception error) { WarnFloorRetirement(error); }
            }
            RetireArchitectureRoot(ref architectureNewShell, "Retired_Hollow_Building_Shell");
            RetireArchitectureRoot(ref architectureNewEntrance, "Retired_Persistent_Building_Entrance");
            ReleaseFailedFloor();
            foreach (var entry in originalFacadeMeshes)
            {
                try
                {
                    if (entry.Key && ownedFacadeMeshes.Contains(entry.Key.sharedMesh)) entry.Key.sharedMesh = entry.Value;
                }
                catch (Exception error) { WarnFloorRetirement(error); }
            }
            originalFacadeMeshes.Clear();
            for (int index = ownedFacadeMeshes.Count - 1; index >= 0; index--)
            {
                try
                {
                    Mesh mesh = ownedFacadeMeshes[index];
                    if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
                    ownedFacadeMeshes.RemoveAt(index);
                }
                catch (Exception error) { WarnFloorRetirement(error); }
            }
        }

        private void ShellTier(Transform shell, Vector2 size, float bottom, float top, bool entrance)
        {
            float hx = size.x / 2, hz = size.y / 2, tall = top - bottom;
            foreach (float side in new[] { -1f, 1f })
            {
                SlopCityInteriorGeometry.Box(shell, "Exterior_Side_Glazing", new Vector3(side * hx, bottom + tall / 2, 0),
                    new Vector3(.12f, tall, size.y), SlopCityInteriorGeometry.Glass, true);
                SlopCityInteriorGeometry.Box(shell, "Exterior_Corner_Column", new Vector3(side * hx, bottom + tall / 2, hz),
                    new Vector3(.22f, tall, .22f), SlopCityInteriorGeometry.Wall, true);
            }
            SlopCityInteriorGeometry.Box(shell, "Exterior_Rear_Glazing", new Vector3(0, bottom + tall / 2, hz),
                new Vector3(size.x, tall, .12f), SlopCityInteriorGeometry.Glass, true);
            if (entrance)
            {
                float segment = (size.x - 1.6f) / 2;
                foreach (float side in new[] { -1f, 1f })
                    SlopCityInteriorGeometry.Box(shell, "Exterior_Front_Glazing", new Vector3(side * (hx + .8f) / 2, bottom + tall / 2, -hz),
                        new Vector3(segment, tall, .12f), SlopCityInteriorGeometry.Glass, true);
                SlopCityInteriorGeometry.Box(shell, "Entrance_Head_Glazing", new Vector3(0, (bottom + 2.5f + top) / 2, -hz),
                    new Vector3(1.6f, Mathf.Max(.05f, top - bottom - 2.5f), .12f), SlopCityInteriorGeometry.Glass, true);
            }
            else SlopCityInteriorGeometry.Box(shell, "Exterior_Front_Glazing", new Vector3(0, bottom + tall / 2, -hz),
                new Vector3(size.x, tall, .12f), SlopCityInteriorGeometry.Glass, true);
        }

        private void BindExistingRoutes()
        {
            foreach (string label in new[] { "Tower_Lift_Street", "Tower_Lift_Roof" })
            {
                Transform control = transform.Find(label);
                bool roof = label == "Tower_Lift_Roof";
                if (!control)
                {
                    control = SlopCityInteriorGeometry.Root(transform, label);
                    SlopCityInteractable generated = control.gameObject.AddComponent<SlopCityInteractable>();
                    generated.Configure(SlopCityInteractionKind.Building, roof ? "Return to street" : Purpose + " roof lift", false);
                    Transform target = SlopCityInteriorGeometry.Root(transform, roof ? "Outer_Street_Route" : "Outer_Roof_Route");
                    target.position = roof ? EntranceFeet : LandingFeet(FloorCount - 1);
                    generated.ConfigureDestination(target);
                    SlopCityInteriorGeometry.Box(control, "Lift_Route_Control_Face", Vector3.zero, new Vector3(.24f, .34f, .055f), SlopCityInteriorGeometry.Accent, false);
                }
                SlopCityInteractable item = control.GetComponent<SlopCityInteractable>();
                if (!item)
                {
                    item = control.gameObject.AddComponent<SlopCityInteractable>();
                    item.Configure(SlopCityInteractionKind.Building, roof ? "Return to street" : Purpose + " roof lift", false);
                }
                Vector3 point = new Vector3(-.82f, LocalFloorElevations[roof ? FloorCount - 1 : 0] + 1.05f, ShaftLocalCenter.z - 1.29f);
                control.localPosition = point; control.localRotation = Quaternion.Euler(0, 180, 0);
                item.ConfigureInteractionPoint(control.position);
                SlopCityElevatorButton button = control.GetComponent<SlopCityElevatorButton>() ?? control.gameObject.AddComponent<SlopCityElevatorButton>();
                button.Configure(null, SlopCityElevatorButtonKind.Call, roof ? FloorCount - 1 : 0);
                button.BindRoute(item, roof ? 0 : FloorCount - 1);
                item.Configure(SlopCityInteractionKind.Building, item.DisplayName, false);
                item.ConfigureInteractionPoint(control.TransformPoint(new Vector3(0, 0, .13f)));
                routeButtons.Add(button);
            }
        }

        // All dimensions below are metres. Keep furniture at adult scale even on upper setbacks.
        private const float HallPartition = 1.15f;
        private const float RoomInnerFace = 1.21f;
        private const float RoomDoorWidth = 1.2f;

        private void BuildRooms(Transform root, int floor, Vector2 size)
        {
            float hx = size.x / 2, hz = size.y / 2;
            // A human-size plan needs a real building footprint; never shrink its fixtures to fit a toy plot.
            if (size.x < 12.5f || size.y < 12.5f)
                throw new InvalidOperationException("Occupied tower floor is too small for its full-size plan: " + BuildingId);
            string role = FloorPurpose(floor);
            SlopCityInteriorGeometry.Sign(root, (floor == 0 ? "LOBBY" : "FLOOR " + floor) + " / " + role.ToUpperInvariant(),
                new Vector3(0, 2.3f, ShaftLocalCenter.z - 1.3f), .105f);
            SlopCityInteriorGeometry.Root(root, "Marker_Corridor", new Vector3(0, .015f, -hz + .9f));
            // The two wall faces leave 2.18m clear. Doors leave 1.20m clear and 2.30m headroom.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int room = 0; room < 2; room++)
                {
                    float south = room == 0 ? -hz + .24f : .14f;
                    float north = room == 0 ? -.14f : hz - .24f;
                    float door = room == 0 ? (south + north) / 2 : Mathf.Min((south + north) / 2, ShaftLocalCenter.z - 2.2f);
                    PartitionDoor(root, side * HallPartition, room == 0 ? -hz : 0, room == 0 ? 0 : hz, door, SlopCityInteriorGeometry.Wall);
                    // These original marker names remain in the open aisle inside each room.
                    SlopCityInteriorGeometry.Root(root, "Room_Circulation_Point_" + side + "_" + (room == 0 ? -1f : 1f),
                        new Vector3(side * (RoomInnerFace + .75f), .015f, door));
                    FurnishRoom(root, floor, role, hx - .24f - RoomInnerFace, south, north, side, room);
                }
                SlopCityInteriorGeometry.Box(root, "Room_Cross_Partition", new Vector3(side * (hx + HallPartition) / 2, 1.3f, 0),
                    new Vector3(hx - HallPartition, 2.6f, .12f), SlopCityInteriorGeometry.Wall, true);
                // A skirting rail makes the room edges legible without another collision boundary.
                SlopCityInteriorGeometry.Box(root, "Room_Outer_Skirting", new Vector3(side * (hx - .14f), .08f, 0),
                    new Vector3(.035f, .16f, size.y - .35f), SlopCityInteriorGeometry.Wood);
            }
            Prop(root, "Public_Service_Junction", new Vector3(-RoomInnerFace - .84f, .64f, -hz + .43f), new Vector3(.46f, 1.28f, .36f),
                SlopCityInteriorGeometry.Metal, SlopCityInteractionKind.Terminal, "Floor signal and recycling junction");
            root.GetComponent<SlopCityInteriorFurnishing>()?.Complete();
        }

        private string FloorPurpose(int floor)
        {
            if (floor == 0) return Purpose + " / reception";
            if (Purpose.StartsWith("Residential", StringComparison.Ordinal)) return floor % 5 == 0 ? "Community kitchen and garden" : "Residential apartment";
            if (Purpose.Contains("Music")) return floor % 3 == 0 ? "Listening library" : "Music production studio";
            if (Purpose.Contains("Signal")) return floor % 3 == 0 ? "Broadcast monitoring" : floor % 3 == 1 ? "Signal laboratory" : "Communications repair workshop";
            if (Purpose.Contains("Market")) return floor % 3 == 0 ? "Market stockroom" : floor % 3 == 1 ? "Market packing workshop" : "Shared kitchen and market pantry";
            if (Purpose.Contains("Repair") || Purpose.Contains("workshop")) return floor % 3 == 0 ? "Parts sorting and service" : "Repair workshop";
            if (Purpose.Contains("Publisher")) return floor % 3 == 0 ? "Print and binding workshop" : floor % 3 == 1 ? "Editorial office" : "Publisher proof archive";
            if (Purpose.Contains("Reading") || Purpose.Contains("archive")) return floor % 3 == 0 ? "Reading and research room" : "Archive and cataloguing";
            return floor % 4 == 0 ? "Shared kitchen and staff rest" : floor % 4 == 1 ? "Editorial office" : floor % 4 == 2 ? "Research and signal lab" : "Records and supply office";
        }

        private static float RoomX(int side, float fromHall) => side * (RoomInnerFace + fromHall);

        private void FurnishRoom(Transform root, int floor, string role, float width, float south, float north, int side, int room)
        {
            string id = (side < 0 ? "West" : "East") + (room == 0 ? "South" : "North");
            float depth = north - south;
            bool residence = role.Contains("Residential");
            bool community = role.Contains("kitchen") || role.Contains("rest") || role.Contains("garden");
            string roomLabel;
            if (side < 0 && room == 1)
            {
                // Separate sanitary room: 2.8m wide, 3.0m deep, fully enclosed and reached through a real 1.1m opening.
                StaffFacilities(root, id, side, width, south, north, floor > 0 && residence);
                roomLabel = residence ? "BATH / LAUNDRY" : "STAFF WASH / KITCHEN";
            }
            else if (floor == 0 && side < 0)
            {
                Reception(root, id, side, width, south, north);
                roomLabel = "RECEPTION / RECORDS";
            }
            else if ((residence && side < 0) && !community)
            {
                Bedroom(root, id, side, width, south, north);
                roomLabel = "BEDROOM / WRITING";
            }
            else if ((residence || community) && side > 0 && room == 0)
            {
                Kitchen(root, id, RoomX(side, width - 1.55f), south + .38f, 2.4f, .65f);
                Fridge(root, id, RoomX(side, width - .43f), south + 1.35f);
                Dining(root, id, RoomX(side, width - 1.55f), north - 1.5f, 1.6f);
                Prop(root, id + "_Reusable_Components", new Vector3(RoomX(side, width - 2.9f), .3f, south + .7f),
                    new Vector3(.58f, .6f, .55f), SlopCityInteriorGeometry.Wood, SlopCityInteractionKind.Collectible, "Reusable household components");
                RoomMarker(root, id + "_Kitchen", RoomX(side, width - 1.55f), south + 1.4f);
                roomLabel = "KITCHEN / DINING";
            }
            else if (residence || community || (floor == 0 && side > 0 && room == 0))
            {
                Living(root, id, side, width, south, north, role.Contains("garden"));
                roomLabel = floor == 0 ? "WAITING / PUBLIC REST" : "LIVING / READING";
            }
            else
            {
                WorkRoom(root, id, role, side, width, south, north, room);
                roomLabel = role.Contains("Music") || role.Contains("Listening") ? "RECORDING / LISTENING" : role.Contains("Archive") || role.Contains("Records") || role.Contains("Reading") ? "ARCHIVE / RESEARCH" : role.Contains("Repair") || role.Contains("service") ? "REPAIR / COMPONENTS" : "OFFICE / RESEARCH";
            }
            // Larger footprints get another useful bay at normal scale, with at least 1.4m between work areas.
            // The cap keeps furnishing cost independent of tower height and large architectural envelopes.
            if (width >= 8.4f)
            {
                float bayX = RoomX(side, 3.2f);
                if (residence || community || floor == 0)
                    Dining(root, id + "_Shared", bayX, south + 1.75f, 1.6f);
                else
                {
                    Desk(root, id + "_Shared", bayX, south + 1.2f, 1.6f, .75f, true);
                    RecordsShelf(root, id + "_Shared", bayX, north - .28f, 1.6f);
                }
            }
            if (depth >= 9f)
            {
                float bayX = RoomX(side, width - 1.55f), bayZ = (south + north) / 2;
                // The staff bath starts at north-3.05m. Reserve its full approach across
                // the door width, including the dining chair's 1.22m northward extent.
                // Otherwise the chair leaves only .29m on the 19m-deep outer tower floors.
                if (side < 0 && room == 1) bayZ = Mathf.Min(bayZ, north - 5.5f);
                if (residence || community || floor == 0) Dining(root, id + "_Middle", bayX, bayZ, 1.6f);
                else Desk(root, id + "_Middle", bayX, bayZ, 1.6f, .75f, true);
            }
            SlopCityInteriorGeometry.Sign(root, roomLabel, new Vector3(RoomX(side, width / 2), 2.18f, north - .11f), .10f);
            SlopCityInteriorFurnishing details = root.GetComponent<SlopCityInteriorFurnishing>() ?? root.gameObject.AddComponent<SlopCityInteriorFurnishing>();
            details.Decorate(id, role, roomLabel, side, width, south, north, (serial + floor + room) % 3);
            // Physical broadcasts are floor residents too. Mounting them at the
            // outer room edge preserves the hall, doors and furniture approaches.
            SlopCityInteriorBroadcast.AttachRoom(root, id, side, width, south, north,
                (serial + floor + room + (side > 0 ? 1 : 0)) % 3, side > 0);
        }

        private static void RoomMarker(Transform root, string id, float x, float z)
        {
            SlopCityInteriorGeometry.Root(root, "Room_Circulation_Point_" + id, new Vector3(x, .015f, z));
        }

        private static void Bedroom(Transform root, string id, int side, float width, float south, float north)
        {
            float bedX = RoomX(side, width - 1.08f), bedZ = south + 1.3f;
            Prop(root, id + "_Double_Bed", new Vector3(bedX, .22f, bedZ), new Vector3(1.6f, .44f, 2.1f),
                SlopCityInteriorGeometry.Wood, SlopCityInteractionKind.Bench, "Apartment bed / rest");
            SlopCityInteriorGeometry.Box(root, id + "_Mattress", new Vector3(bedX, .55f, bedZ), new Vector3(1.57f, .23f, 2.07f), SlopCityInteriorGeometry.Fabric);
            SlopCityInteriorGeometry.Box(root, id + "_Headboard", new Vector3(bedX, .67f, south + .23f), new Vector3(1.7f, 1.1f, .10f), SlopCityInteriorGeometry.Wood);
            SlopCityInteriorGeometry.Box(root, id + "_Blanket", new Vector3(bedX, .68f, bedZ + .3f), new Vector3(1.56f, .045f, 1.35f), SlopCityInteriorGeometry.Accent);
            for (int pillow = -1; pillow <= 1; pillow += 2)
                SlopCityInteriorGeometry.Box(root, id + "_Pillow", new Vector3(bedX + pillow * .39f, .72f, bedZ - .70f), new Vector3(.66f, .13f, .43f), SlopCityInteriorGeometry.Wall);
            Cabinet(root, id + "_Wardrobe", RoomX(side, width - .36f), north - 2.15f, .65f, 1.5f, 2.15f);
            Desk(root, id + "_Writing", RoomX(side, width - 1.25f), north - .5f, 1.6f, .70f, true);
            SlopCityInteriorGeometry.Box(root, id + "_Bedside_Table", new Vector3(RoomX(side, width - 2.3f), .31f, south + .5f), new Vector3(.48f, .62f, .45f), SlopCityInteriorGeometry.Wood, true);
            RoomMarker(root, id + "_Bed", RoomX(side, width - 2.3f), bedZ + .65f);
        }

        private static void Living(Transform root, string id, int side, float width, float south, float north, bool garden)
        {
            float x = RoomX(side, width - 1.65f), z = north - .65f;
            Sofa(root, id, x, z, 2.3f);
            SlopCityInteriorGeometry.Box(root, id + "_Coffee_Table", new Vector3(x, .24f, z - 1.35f), new Vector3(1.25f, .48f, .65f), SlopCityInteriorGeometry.Wood, true);
            SlopCityInteriorGeometry.Box(root, id + "_Reading_Material", new Vector3(x, .5f, z - 1.35f), new Vector3(.40f, .035f, .28f), SlopCityInteriorGeometry.Wall);
            RecordsShelf(root, id + "_Library", x, south + .26f, 2.2f);
            // This library sits against the room's south boundary. Reverse only
            // its visual shelf assembly so the open shelves face the room; its
            // centred rectangular collision envelope is unchanged by the turn.
            root.Find(id + "_Library_Shelf").localRotation = Quaternion.Euler(0, 180f, 0);
            Chair(root, id + "_Reading_Chair", RoomX(side, width - .6f), south + 1.5f, true);
            Prop(root, id + "_Plant", new Vector3(RoomX(side, width - .5f), .35f, north - 2.7f), new Vector3(.55f, .7f, .55f),
                SlopCityInteriorGeometry.Accent, SlopCityInteractionKind.Plant, garden ? "Community garden planter" : "Community houseplant");
            RoomMarker(root, id + "_Living", x, z - 2.15f);
        }

        private static void Reception(Transform root, string id, int side, float width, float south, float north)
        {
            float x = RoomX(side, width - 1.55f);
            Prop(root, id + "_Reception_Desk", new Vector3(x, .5f, south + 1.45f), new Vector3(2.4f, 1f, .8f),
                SlopCityInteriorGeometry.Wood, SlopCityInteractionKind.Terminal, "Reception / file device readings and recycling");
            SlopCityInteriorGeometry.Box(root, id + "_Reception_Worktop", new Vector3(x, 1.03f, south + 1.45f), new Vector3(2.45f, .06f, .85f), SlopCityInteriorGeometry.Metal);
            SlopCityInteriorGeometry.Box(root, id + "_Reception_Terminal", new Vector3(x - .55f, 1.27f, south + 1.48f), new Vector3(.5f, .40f, .08f), SlopCityInteriorGeometry.Accent);
            Chair(root, id + "_Reception_Chair", x, south + 2.5f, false);
            RecordsShelf(root, id + "_Records", x, north - .28f, 2.2f);
            Prop(root, id + "_Public_Supplies", new Vector3(RoomX(side, width - .45f), .6f, north - 1.6f), new Vector3(.72f, 1.2f, .55f),
                SlopCityInteriorGeometry.Metal, SlopCityInteractionKind.Vending, "Free public repair supplies");
            RoomMarker(root, id + "_Reception", x, south + .60f);
        }

        private static void WorkRoom(Transform root, string id, string role, int side, float width, float south, float north, int room)
        {
            float x = RoomX(side, width - 1.45f);
            bool repair = role.IndexOf("workshop", StringComparison.OrdinalIgnoreCase) >= 0 || role.Contains("Repair") || role.Contains("service") || role.Contains("sorting");
            bool archive = role.IndexOf("archive", StringComparison.OrdinalIgnoreCase) >= 0 || role.Contains("Records") || role.Contains("Reading") || role.Contains("library") || role.Contains("stockroom");
            bool music = role.Contains("Music") || role.Contains("Listening");
            bool signal = role.Contains("Signal") || role.Contains("Broadcast");
            if (repair)
            {
                Prop(root, id + "_Workbench", new Vector3(x, .45f, south + .6f), new Vector3(2.4f, .9f, .8f),
                    SlopCityInteriorGeometry.Wood, SlopCityInteractionKind.Terminal, "Repair workbench / log device readings and sort recycling");
                SlopCityInteriorGeometry.Box(root, id + "_Workbench_Top", new Vector3(x, .93f, south + .6f), new Vector3(2.44f, .06f, .85f), SlopCityInteriorGeometry.Metal);
                SlopCityInteriorGeometry.Box(root, id + "_Tool_Rail", new Vector3(x, 1.45f, south + .23f), new Vector3(2.2f, .42f, .06f), SlopCityInteriorGeometry.Accent);
                Prop(root, id + "_Supplies", new Vector3(RoomX(side, width - .48f), .55f, north - 1.4f), new Vector3(.8f, 1.1f, .65f),
                    SlopCityInteriorGeometry.Metal, SlopCityInteractionKind.Vending, "Free workshop repair components");
                Prop(root, id + "_Reusable_Components", new Vector3(x - side * .55f, 1.13f, south + .6f), new Vector3(.55f, .34f, .45f),
                    SlopCityInteriorGeometry.Wood, SlopCityInteractionKind.Collectible, "Reusable workshop components");
                Desk(root, id + "_Service", x, north - .55f, 1.6f, .75f, true);
            }
            else
            {
                Desk(root, id + "_Desk_A", x, south + .65f, 1.6f, .75f, false);
                Prop(root, id + "_Supplies", new Vector3(RoomX(side, width - .45f), .55f, north - 1.4f), new Vector3(.72f, 1.1f, .55f),
                    SlopCityInteriorGeometry.Metal, room == 1 ? SlopCityInteractionKind.Vending : SlopCityInteractionKind.Terminal,
                    room == 1 ? "Free service supplies" : "Device reading and records desk");
                if (archive) RecordsShelf(root, id + "_Catalogue", x, north - .28f, 2.4f);
                else if (music)
                {
                    Desk(root, id + "_Mixing", x, north - .55f, 1.8f, .75f, true);
                    SlopCityInteriorGeometry.Box(root, id + "_Mixing_Console", new Vector3(x, .83f, north - .55f), new Vector3(1.4f, .13f, .45f), SlopCityInteriorGeometry.Metal);
                    for (int speaker = -1; speaker <= 1; speaker += 2)
                        SlopCityInteriorGeometry.Box(root, id + "_Monitor_Speaker", new Vector3(x + speaker * .68f, 1.01f, north - .36f), new Vector3(.28f, .40f, .25f), SlopCityInteriorGeometry.Fabric);
                    // Acoustic panels are decoration, not another playing media source or point light.
                    SlopCityInteriorGeometry.Box(root, id + "_Acoustic_Panel", new Vector3(x, 1.65f, north - .08f), new Vector3(2.1f, .9f, .06f), SlopCityInteriorGeometry.Fabric);
                }
                else if (signal)
                {
                    Prop(root, id + "_Signal_Test_Rack", new Vector3(x, .95f, north - .43f), new Vector3(1.7f, 1.9f, .70f),
                        SlopCityInteriorGeometry.Metal, SlopCityInteractionKind.Terminal, "Signal analysis / file device readings and tune broadcasts");
                    for (int module = 0; module < 4; module++)
                        SlopCityInteriorGeometry.Box(root, id + "_Signal_Module", new Vector3(x, .38f + module * .40f, north - .80f),
                            new Vector3(1.5f, .27f, .05f), SlopCityInteriorGeometry.Accent);
                }
                else Desk(root, id + "_Desk_B", x, north - .55f, 1.6f, .75f, true);
                RecordsShelf(root, id + "_Storage", RoomX(side, width - .3f), (south + north) / 2, .5f, 1.55f);
            }
            Chair(root, id + "_Rest_Seat", RoomX(side, width - 2.9f), (south + north) / 2, true);
            RoomMarker(root, id + "_Work", RoomX(side, width - 2.6f), south + 1.9f);
            RoomMarker(root, id + "_Supplies", x, north - 2.5f);
        }

        private static void StaffFacilities(Transform root, string id, int side, float width, float south, float north, bool laundry)
        {
            float bathX = RoomX(side, width - 1.45f), bathZ = north - 1.55f;
            Bathroom(root, id, bathX, bathZ);
            // A normal 2.1m counter and separate utility cabinet share the front half of the room.
            Kitchen(root, id, RoomX(side, width - 1.5f), south + .42f, 2.1f, .65f);
            Cabinet(root, id + (laundry ? "_Laundry" : "_Cleaning_Supplies"), RoomX(side, width - .4f), south + 1.7f, .7f, .7f, .95f);
            SlopCityInteriorGeometry.Box(root, id + "_Utility_Door", new Vector3(RoomX(side, width - .4f), .48f, south + 1.34f), new Vector3(.48f, .48f, .025f), SlopCityInteriorGeometry.Metal);
            RoomMarker(root, id + "_Kitchen", RoomX(side, width - 1.9f), south + 1.5f);
            RoomMarker(root, id + "_Bathroom", bathX, bathZ - .5f);
        }

        private static void PartitionDoor(Transform root, float x, float south, float north, float doorway, Material material)
        {
            foreach (Vector2 segment in new[] { new Vector2(south, doorway - RoomDoorWidth / 2), new Vector2(doorway + RoomDoorWidth / 2, north) })
                if (segment.y > segment.x)
                    SlopCityInteriorGeometry.Box(root, "Room_Door_Partition", new Vector3(x, 1.3f, (segment.x + segment.y) / 2), new Vector3(.12f, 2.6f, segment.y - segment.x), material, true);
            SlopCityInteriorGeometry.Box(root, "Room_Door_Lintel", new Vector3(x, 2.45f, doorway), new Vector3(.12f, .3f, RoomDoorWidth), material, true);
            foreach (float jamb in new[] { -1f, 1f })
                SlopCityInteriorGeometry.Box(root, "Room_Door_Frame", new Vector3(x, 1.15f, doorway + jamb * (RoomDoorWidth / 2 + .035f)), new Vector3(.16f, 2.3f, .07f), SlopCityInteriorGeometry.Wood);
            HingedDoor(root, "Room_Hinged_Door_" + x + "_" + doorway,
                new Vector3(x, 0, doorway - RoomDoorWidth / 2), RoomDoorWidth, true, x > 0 ? 90f : -90f);
        }

        private static void HingedDoor(Transform root, string id, Vector3 hingePoint, float width, bool alongZ, float angle, bool reverse = false,
            string label = "ROOM DOOR", Material finish = null, float doorHeight = 2.25f)
        {
            Transform hinge = SlopCityInteriorGeometry.Root(root, id, hingePoint);
            if (reverse) hinge.localRotation = Quaternion.Euler(0, 180, 0);
            Vector3 centre = alongZ ? new Vector3(0, doorHeight / 2, width / 2) : new Vector3(width / 2, doorHeight / 2, 0);
            Vector3 size = alongZ ? new Vector3(.065f, doorHeight, width - .06f) : new Vector3(width - .06f, doorHeight, .065f);
            GameObject leaf = SlopCityInteriorGeometry.Box(hinge, "Door_Leaf", centre, size, finish ? finish : SlopCityInteriorGeometry.Wood, true);
            leaf.isStatic = false;
            Vector3 handle = alongZ ? new Vector3(.08f, 1.02f, width - .15f) : new Vector3(width - .15f, 1.02f, -.08f);
            GameObject hardware = SlopCityInteriorGeometry.Box(hinge, "Door_Handle", handle, new Vector3(.11f, .055f, .11f), SlopCityInteriorGeometry.Metal);
            hardware.isStatic = false;
            Vector3 rearHandle = handle; if (alongZ) rearHandle.x *= -1; else rearHandle.z *= -1;
            GameObject rearHardware = SlopCityInteriorGeometry.Box(hinge, "Door_Handle_Inside", rearHandle, new Vector3(.11f, .055f, .11f), SlopCityInteriorGeometry.Metal);
            rearHardware.isStatic = false;
            hinge.gameObject.AddComponent<SlopCityDoor>().Configure(hinge, leaf.GetComponent<Collider>(), hinge.TransformPoint(handle), label, angle);
        }

        private static void Desk(Transform root, string id, float x, float z, float width, float depth, bool chairOnSouth)
        {
            SlopCityInteriorGeometry.Box(root, id + "_Desk", new Vector3(x, .74f, z), new Vector3(width, .10f, depth), SlopCityInteriorGeometry.Wood, true);
            foreach (float side in new[] { -1f, 1f })
                SlopCityInteriorGeometry.Box(root, id + "_Desk_Leg", new Vector3(x + side * width * .37f, .35f, z), new Vector3(.065f, .70f, depth * .65f), SlopCityInteriorGeometry.Metal);
            SlopCityInteriorGeometry.Box(root, id + "_Notebook", new Vector3(x - width * .2f, .81f, z), new Vector3(.36f, .035f, .28f), SlopCityInteriorGeometry.Wall);
            SlopCityInteriorGeometry.Box(root, id + "_Monitor", new Vector3(x + width * .18f, 1.03f, z + (chairOnSouth ? .18f : -.18f)), new Vector3(.46f, .35f, .06f), SlopCityInteriorGeometry.Metal);
            Chair(root, id + "_Chair", x, z + (chairOnSouth ? -.95f : .95f), chairOnSouth);
        }

        private static void Chair(Transform root, string id, float x, float z, bool facesNorth)
        {
            Prop(root, id, new Vector3(x, .25f, z), new Vector3(.52f, .5f, .54f), SlopCityInteriorGeometry.Fabric, SlopCityInteractionKind.Bench, "Chair / rest");
            SlopCityInteriorGeometry.Box(root, id + "_Back", new Vector3(x, .76f, z + (facesNorth ? -.23f : .23f)), new Vector3(.52f, .58f, .09f), SlopCityInteriorGeometry.Fabric);
            SlopCityInteriorGeometry.Box(root, id + "_Legs", new Vector3(x, .13f, z), new Vector3(.42f, .26f, .42f), SlopCityInteriorGeometry.Metal);
        }

        private static void Sofa(Transform root, string id, float x, float z, float width)
        {
            Prop(root, id + "_Sofa", new Vector3(x, .27f, z), new Vector3(width, .54f, .90f), SlopCityInteriorGeometry.Fabric, SlopCityInteractionKind.Bench, "Sofa / rest");
            SlopCityInteriorGeometry.Box(root, id + "_Sofa_Back", new Vector3(x, .75f, z + .39f), new Vector3(width, .8f, .15f), SlopCityInteriorGeometry.Fabric);
            foreach (float side in new[] { -1f, 1f })
            {
                SlopCityInteriorGeometry.Box(root, id + "_Sofa_Arm", new Vector3(x + side * (width / 2 - .10f), .58f, z), new Vector3(.20f, .6f, .9f), SlopCityInteriorGeometry.Wood);
                SlopCityInteriorGeometry.Box(root, id + "_Seat_Cushion", new Vector3(x + side * width * .23f, .57f, z - .02f), new Vector3(width * .44f, .15f, .72f), SlopCityInteriorGeometry.Accent);
            }
        }

        private static void Dining(Transform root, string id, float x, float z, float width)
        {
            SlopCityInteriorGeometry.Box(root, id + "_Dining_Table", new Vector3(x, .75f, z), new Vector3(width, .10f, .85f), SlopCityInteriorGeometry.Wood, true);
            SlopCityInteriorGeometry.Box(root, id + "_Dining_Pedestal", new Vector3(x, .35f, z), new Vector3(.38f, .7f, .42f), SlopCityInteriorGeometry.Metal);
            Chair(root, id + "_Dining_Chair_South", x, z - .95f, true);
            Chair(root, id + "_Dining_Chair_North", x, z + .95f, false);
        }

        private static void Cabinet(Transform root, string id, float x, float z, float width, float depth, float height, bool faceNorth = false)
        {
            SlopCityInteriorGeometry.Box(root, id, new Vector3(x, height / 2, z), new Vector3(width, height, depth), SlopCityInteriorGeometry.Wood, true);
            float front = faceNorth ? 1f : -1f;
            SlopCityInteriorGeometry.Box(root, id + "_Door", new Vector3(x, height / 2, z + front * (depth / 2 + .015f)), new Vector3(width - .04f, height - .08f, .025f), SlopCityInteriorGeometry.Wall);
            SlopCityInteriorGeometry.Box(root, id + "_Handle", new Vector3(x + width * .25f, height * .55f, z + front * (depth / 2 + .04f)), new Vector3(.035f, .20f, .035f), SlopCityInteriorGeometry.Metal);
        }

        private static void RecordsShelf(Transform root, string id, float x, float z, float width, float depth = .40f)
        {
            Transform shelf = SlopCityInteriorGeometry.Root(root, id + "_Shelf", new Vector3(x, 0, z));
            // Retain the same blocking envelope while exposing real shelves instead
            // of a solid timber block with records pasted onto its front face.
            BoxCollider collision = shelf.gameObject.AddComponent<BoxCollider>();
            collision.center = new Vector3(0, 1.03f, 0); collision.size = new Vector3(width, 2.06f, depth);
            foreach (float side in new[] { -1f, 1f })
                SlopCityInteriorGeometry.Box(shelf, "Shelf_Upright", new Vector3(side * (width / 2 - .035f), 1.03f, 0),
                    new Vector3(.07f, 2.06f, depth), SlopCityInteriorGeometry.Wood);
            SlopCityInteriorGeometry.Box(shelf, "Shelf_Back", new Vector3(0, 1.03f, depth / 2 - .015f),
                new Vector3(width, 2.06f, .03f), SlopCityInteriorGeometry.Wood);
            SlopCityInteriorGeometry.Box(shelf, "Shelf_Top", new Vector3(0, 2.025f, 0), new Vector3(width, .07f, depth), SlopCityInteriorGeometry.Wood);
            for (int row = 0; row < 4; row++)
            {
                SlopCityInteriorGeometry.Box(shelf, "Shelf_Board", new Vector3(0, .17f + row * .46f, 0),
                    new Vector3(width, .05f, depth), SlopCityInteriorGeometry.Wood);
                // Leave the second shelf for the individually batched catalogue files.
                if (row != 1)
                    SlopCityInteriorGeometry.Box(shelf, "Record_Cases", new Vector3(0, .34f + row * .46f, -.03f),
                        new Vector3(width * .88f, .29f, Mathf.Min(.32f, depth - .06f)), row % 2 == 0 ? SlopCityInteriorGeometry.Accent : SlopCityInteriorGeometry.Wall);
            }
        }

        private static void Fridge(Transform root, string id, float x, float z)
        {
            // The kitchen and its refrigerator occupy the south wall; expose the
            // refrigerator face to the occupied room instead of to the wall/counter.
            Cabinet(root, id + "_Refrigerator", x, z, .72f, .70f, 1.8f, true);
            SlopCityInteriorGeometry.Box(root, id + "_Freezer_Division", new Vector3(x, 1.3f, z + .36f), new Vector3(.68f, .025f, .025f), SlopCityInteriorGeometry.Metal);
        }

        private static void Kitchen(Transform root, string id, float x, float z, float width, float depth)
        {
            Prop(root, id + "_Kitchen_Sink", new Vector3(x, .45f, z), new Vector3(width, .9f, depth), SlopCityInteriorGeometry.Wood, SlopCityInteractionKind.Fountain, "Kitchen water refill sink");
            SlopCityInteriorGeometry.Box(root, id + "_Countertop", new Vector3(x, .93f, z), new Vector3(width + .04f, .06f, depth + .04f), SlopCityInteriorGeometry.Wall);
            SlopCityInteriorGeometry.Box(root, id + "_Sink_Basin", new Vector3(x + width * .18f, .975f, z), new Vector3(.52f, .045f, .42f), SlopCityInteriorGeometry.Metal);
            SlopCityInteriorGeometry.Box(root, id + "_Sink_Tap", new Vector3(x + width * .18f, 1.08f, z - .19f), new Vector3(.06f, .23f, .06f), SlopCityInteriorGeometry.Accent);
            SlopCityInteriorGeometry.Box(root, id + "_Cooktop", new Vector3(x - width * .28f, .98f, z), new Vector3(.60f, .045f, .48f), SlopCityInteriorGeometry.Metal);
            for (int plate = -1; plate <= 1; plate += 2)
                SlopCityInteriorGeometry.Box(root, id + "_Heating_Plate", new Vector3(x - width * .28f + plate * .14f, 1.015f, z), new Vector3(.19f, .014f, .30f), SlopCityInteriorGeometry.Accent);
            // Every call places this counter against its room's south wall. Keep
            // its back/upper units on that wall side and the doors facing inward.
            SlopCityInteriorGeometry.Box(root, id + "_Backsplash", new Vector3(x, 1.24f, z - .30f), new Vector3(width, .55f, .04f), SlopCityInteriorGeometry.Metal);
            for (int door = -1; door <= 1; door += 2)
                SlopCityInteriorGeometry.Box(root, id + "_Lower_Cabinet_Door", new Vector3(x + door * width * .25f, .45f, z + depth / 2 + .02f), new Vector3(width * .47f, .74f, .035f), SlopCityInteriorGeometry.Wall);
            SlopCityInteriorGeometry.Box(root, id + "_Upper_Cabinet", new Vector3(x, 2.03f, z - .12f), new Vector3(width, .65f, .36f), SlopCityInteriorGeometry.Wood);
        }

        private static void Bathroom(Transform root, string id, float x, float z)
        {
            const float width = 2.8f, depth = 3f, door = 1.1f;
            float left = x - width / 2, right = x + width / 2, front = z - depth / 2, back = z + depth / 2;
            SlopCityInteriorGeometry.Box(root, id + "_Bathroom_Left_Wall", new Vector3(left, 1.25f, z), new Vector3(.10f, 2.5f, depth), SlopCityInteriorGeometry.Wall, true);
            SlopCityInteriorGeometry.Box(root, id + "_Bathroom_Right_Wall", new Vector3(right, 1.25f, z), new Vector3(.10f, 2.5f, depth), SlopCityInteriorGeometry.Wall, true);
            SlopCityInteriorGeometry.Box(root, id + "_Bathroom_Back_Wall", new Vector3(x, 1.25f, back), new Vector3(width, 2.5f, .10f), SlopCityInteriorGeometry.Wall, true);
            foreach (float side in new[] { -1f, 1f })
                SlopCityInteriorGeometry.Box(root, id + "_Bathroom_Front_Wall", new Vector3(x + side * (width + door) / 4, 1.25f, front), new Vector3((width - door) / 2, 2.5f, .10f), SlopCityInteriorGeometry.Wall, true);
            SlopCityInteriorGeometry.Box(root, id + "_Bathroom_Door_Lintel", new Vector3(x, 2.4f, front), new Vector3(door, .2f, .10f), SlopCityInteriorGeometry.Wall, true);
            HingedDoor(root, "Bathroom_Hinged_Door_" + id, new Vector3(x + door / 2, 0, front), door, false, 90f, true);
            // Shower and WC occupy distinct back corners. The middle and front doorway remain open.
            float showerX = left + .65f, showerZ = back - .65f;
            SlopCityInteriorGeometry.Box(root, id + "_Shower_Tray", new Vector3(showerX, .035f, showerZ), new Vector3(1.05f, .07f, 1.05f), SlopCityInteriorGeometry.Wall);
            SlopCityInteriorGeometry.Box(root, id + "_Shower_Riser", new Vector3(showerX, 1.12f, back - .15f), new Vector3(.055f, 2.1f, .055f), SlopCityInteriorGeometry.Metal);
            SlopCityInteriorGeometry.Box(root, id + "_Shower_Head", new Vector3(showerX, 2.12f, back - .26f), new Vector3(.22f, .06f, .25f), SlopCityInteriorGeometry.Metal);
            SlopCityInteriorGeometry.Box(root, id + "_Shower_Screen", new Vector3(showerX + .56f, 1.05f, showerZ + .12f), new Vector3(.035f, 2.0f, .85f), SlopCityInteriorGeometry.Glass);
            Prop(root, id + "_Toilet", new Vector3(right - .48f, .23f, back - .60f), new Vector3(.43f, .46f, .70f), SlopCityInteriorGeometry.Wall, SlopCityInteractionKind.Inspect, "Bathroom fixtures / maintain surface");
            SlopCityInteriorGeometry.Box(root, id + "_Toilet_Cistern", new Vector3(right - .48f, .66f, back - .29f), new Vector3(.43f, .48f, .18f), SlopCityInteriorGeometry.Wall);
            Prop(root, id + "_Washbasin", new Vector3(left + .38f, .43f, front + .65f), new Vector3(.55f, .86f, .48f), SlopCityInteriorGeometry.Wall, SlopCityInteractionKind.Fountain, "Bathroom washbasin / refill water");
            SlopCityInteriorGeometry.Box(root, id + "_Bathroom_Mirror", new Vector3(left + .055f, 1.52f, front + .65f), new Vector3(.025f, .65f, .48f), SlopCityInteriorGeometry.Metal);
            SlopCityInteriorGeometry.Box(root, id + "_Towel_Rail", new Vector3(right - .08f, 1.15f, front + .55f), new Vector3(.035f, .10f, .65f), SlopCityInteriorGeometry.Metal);
        }

        private void BuildRoof(Transform root, Vector2 size)
        {
            SlopCityInteriorGeometry.Root(root, "Marker_Corridor", new Vector3(0, .015f, ShaftLocalCenter.z - 1.75f));
            SlopCityInteriorGeometry.Root(root, "Room_Circulation_Point_Roof", new Vector3(-size.x / 2 + .8f, .015f, -size.y / 2 + .8f));
            SlopCityInteriorGeometry.Sign(root, "ROOF / " + Purpose.ToUpperInvariant(), new Vector3(0, 2.2f, ShaftLocalCenter.z - 1.3f), .1f);
            if (BuildingId.StartsWith("Skyline_", StringComparison.Ordinal) || BuildingId == "MilitarySF_CommunicationsCitadel")
            {
                Prop(root, "Roof_Public_Service", new Vector3(size.x / 2 - .7f, .6f, -size.y / 2 + 1f), new Vector3(.7f, 1.2f, .55f), SlopCityInteriorGeometry.Metal, SlopCityInteractionKind.Terminal, "Roof broadcast service junction");
                foreach (float side in new[] { -1f, 1f })
                {
                    SlopCityInteriorGeometry.Box(root, "Roof_Safety_Parapet_X", new Vector3(side * size.x / 2, .5f, 0), new Vector3(.16f, 1f, size.y), SlopCityInteriorGeometry.Wall, true);
                    SlopCityInteriorGeometry.Box(root, "Roof_Safety_Parapet_Z", new Vector3(0, .5f, side * size.y / 2), new Vector3(size.x, 1f, .16f), SlopCityInteriorGeometry.Wall, true);
                }
            }
        }

        private static void Prop(Transform root, string id, Vector3 center, Vector3 dimensions, Material finish, SlopCityInteractionKind kind, string label)
        {
            GameObject body = SlopCityInteriorGeometry.Box(root, id, center, dimensions, finish, true);
            SlopCityInteractable item = body.AddComponent<SlopCityInteractable>(); item.Configure(kind, label, false);
            item.ConfigureInteractionPoint(body.transform.TransformPoint(new Vector3(0, .25f, -.51f)));
        }
    }
}
