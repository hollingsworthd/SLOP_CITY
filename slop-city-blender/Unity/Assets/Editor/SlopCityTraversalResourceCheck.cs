#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Production constrained traversal ownership and all-floor physics parity, without saved-scene writes.</summary>
    public static class SlopCityTraversalResourceCheck
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int checks;
        private static Receipt receipt;
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Traversal resources require a dedicated batch Editor outside Play Mode.");
            receipt = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            int exit = 0;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ElevatorVisualsAndPhysics();
                FloorAdmissionAndRequests();
                EntranceAdmission();
                ArchitectureRetirement();
                receipt.success = true;
            }
            catch (Exception error) { receipt.error = error.ToString(); exit = 1; }
            finally
            {
                receipt.completedAtUtc = DateTime.UtcNow.ToString("O"); receipt.checks = checks;
                Directory.CreateDirectory("Builds/Reports");
                File.WriteAllText("Builds/Reports/traversal-resource-check.json", JsonUtility.ToJson(receipt, true));
            }
            Debug.Log("Traversal resource checks: " + checks + "; success=" + receipt.success);
            EditorApplication.Exit(exit);
        }

        private static void ElevatorVisualsAndPhysics()
        {
            GameObject root = new GameObject("Traversal_Lift_Fixture");
            GameObject user = new GameObject("Traversal_User");
            SlopCityFirstPerson rider = user.AddComponent<SlopCityFirstPerson>();
            try
            {
                const int floors = 55;
                float[] elevations = Enumerable.Range(0, floors).Select(index => index * 3.2f).ToArray();
                string[] labels = Enumerable.Range(0, floors).Select(index => "FLOOR " + index).ToArray();
                SlopCityElevator native = SlopCityElevator.Create(root.transform, Vector3.zero, elevations, labels, null, null);
                SlopCityElevator mobile = SlopCityElevator.Create(root.transform, Vector3.zero, elevations, labels, rider, null, true);
                int nativeRenderers = native.GetComponentsInChildren<MeshRenderer>(true).Length;
                Check(nativeRenderers == 8 * floors + 45, "Default/native elevator keeps every original landing renderer.");
                string[] physics = PhysicsSignature(native.transform);
                Check(physics.Length == 6 * floors + 30, "Complete all-floor collider count is unchanged.");
                Check(physics.SequenceEqual(PhysicsSignature(mobile.transform)), "Streamed and native elevators have exactly equal collider positions, sizes and scales.");
                Set(rider, "paused", false);
                for (int floor = 0; floor < floors; floor++)
                {
                    user.transform.position = new Vector3(0, elevations[floor] + .01f, -1.75f);
                    Set(mobile, "currentFloor", floor);
                    Set(mobile, "targetFloor", (floor + 13) % floors);
                    Set(mobile, "moving", true);
                    Call(mobile, "RefreshLandingVisuals");
                    Check(mobile.VisualLandingCount <= 3, "Current, target and occupied landing visuals stay bounded across every floor.");
                    Check(mobile.GetComponentsInChildren<MeshRenderer>(true).Length <= 69, "Renderer count does not scale with tower height or visit count.");
                    Check(physics.SequenceEqual(PhysicsSignature(mobile.transform)), "Visual retirement never changes physical door/barrier colliders.");
                    Check(mobile.transform.Find("Elevator_Landing_" + floor.ToString("00")).GetComponentsInChildren<MeshRenderer>().Length == 8,
                        "Occupied/current landing retains all original surfaces and labels.");
                    mobile.ChangePage(1);
                    Check(mobile.FloorLabel(floor) == labels[floor] && mobile.FloorElevation(floor) == elevations[floor], "All floor metadata and paging remain available.");
                }
                Set(mobile, "moving", false); Set(mobile, "currentFloor", 0); Set(mobile, "targetFloor", 0);
                user.transform.position = mobile.Cabin.TransformPoint(new Vector3(0, .01f, 0));
                Physics.SyncTransforms();
                Check(mobile.PlayerInside && !mobile.ReleaseEmpty(), "A standing cabin passenger prevents retirement.");
                user.transform.position = new Vector3(1000, 0, 1000);
                Physics.SyncTransforms();
                Check(mobile.ReleaseEmpty(), "A distant empty cabin can retire safely.");
                receipt.nativeLiftRenderers = nativeRenderers;
                receipt.maximumConstrainedLiftRenderers = 69;
                receipt.completeLiftColliders = physics.Length;
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(user); }
        }

        private static void FloorAdmissionAndRequests()
        {
            GameObject root = new GameObject("Floor_Admission_Fixture");
            GameObject user = new GameObject("Floor_Admission_User");
            Mesh coreMesh = null;
            try
            {
                SlopCityFirstPerson rider = user.AddComponent<SlopCityFirstPerson>();
                Set(rider, "paused", false);
                var world = root.AddComponent<SlopCityBuildingWorld>();
                Set(world, "constrainedBrowser", true); Set(world, "player", rider);
                Type geometry = typeof(SlopCityBuildingWorld).Assembly.GetType("SlopCity.SlopCityInteriorGeometry", true);
                geometry.GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
                coreMesh = Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
                Object.DestroyImmediate(primitive);
                coreMesh.bounds = new Bounds(new Vector3(0, 9.6f, 0), new Vector3(16, 19.2f, 16));
                GameObject tower = Child(root.transform, "Skyline_Tower_990");
                Surface(tower.transform, "Structural_Core", coreMesh).AddComponent<BoxCollider>().size = coreMesh.bounds.size;
                var building = tower.AddComponent<SlopCityBuildingInterior>();
                Call(building, "InitializeTower", world, rider);
                Transform occupied = building.EnsureFloor(1);
                Transform spare = building.EnsureFloor(0);
                user.transform.position = building.LandingFeet(1);
                int frame = Time.frameCount;
                // Editor destruction is immediate; explicitly exercise the same
                // policy with deferred-destruction=true and controlled frame IDs.
                Call(building, "NoteFloorRetirement", frame, true);
                Check(!(bool)Call(building, "CanAdmitFloorDetail", frame)
                    && (bool)Call(building, "CanAdmitFloorDetail", frame + 1),
                    "Deferred constrained floor retirement admits only on the next frame.");
                Check(!(bool)Call(building, "PrepareFloor", 5), "A destination reports normal pending readiness during the retirement frame.");
                Check(building.IsFloorLoaded(1) && !building.IsFloorLoaded(5) && !spare && building.LoadedFloorCount == 1,
                    "Preparing a replacement retires the spare room while preserving occupied support and deferring allocation.");
                CheckFloorSupport(building, occupied, 1);
                int meshesAfterRetirement = Resources.FindObjectsOfTypeAll<Mesh>().Length;
                for (int repeat = 0; repeat < 12; repeat++)
                    Check(!(bool)Call(building, "PrepareFloor", 5) && building.LoadedFloorCount == 1,
                        "Repeated pending preparation neither allocates a destination nor loses occupied support.");
                Check(Resources.FindObjectsOfTypeAll<Mesh>().Length == meshesAfterRetirement,
                    "Repeated pending calls retain exactly the same live native mesh count.");
                Check((bool)Call(building, "PrepareFloor", 1), "An already supported occupied landing remains ready across the barrier.");
                Set(building, "floorAdmissionAfterFrame", frame);
                Check((bool)Call(building, "PrepareFloor", 5) && building.IsFloorLoaded(5) && building.LoadedFloorCount == 2,
                    "Destination allocation resumes when the next-frame policy permits it, within the two-room budget.");
                Check(building.EnsureFloor(1) == occupied, "Destination preparation preserves the exact occupied floor object.");
                CheckFloorSupport(building, occupied, 1);
                Call(building, "NoteFloorRetirement", frame, true);
                Call(building, "StreamAround", user.transform.position, 2);
                Check(!building.IsFloorLoaded(5) && !building.IsFloorLoaded(0) && building.EnsureFloor(1) == occupied,
                    "Walking streaming retires the old neighbor before admitting a replacement and preserves occupied support.");
                Call(building, "StreamAround", user.transform.position, 2);
                Check(building.LoadedFloorCount == 1, "Same-frame walking refresh does not bypass deferred admission.");
                Set(building, "floorAdmissionAfterFrame", frame);
                Call(building, "StreamAround", user.transform.position, 2);
                Check(building.IsFloorLoaded(0) && building.EnsureFloor(1) == occupied && building.LoadedFloorCount == 2,
                    "Walking admits its next neighbor after the barrier without replacing the occupied floor.");
                CheckFloorSupport(building, occupied, 1);
                Set(world, "constrainedBrowser", false);
                Call(building, "NoteFloorRetirement", frame, true);
                Check((bool)Call(building, "CanAdmitFloorDetail", frame), "Native immediate destruction retains immediate floor admission.");
                Set(world, "constrainedBrowser", true);

                bool destinationReady = false;
                SlopCityElevator lift = SlopCityElevator.Create(root.transform, Vector3.zero,
                    new[] { 0f, 3.2f, 6.4f }, null, null, null, true, null,
                    floor => floor == 0 || destinationReady);
                lift.CloseDoors();
                string[] liftSupport = PhysicsSignature(lift.transform);
                Check(lift.RequestFloor(2), "A real elevator queues a destination whose floor is still pending.");
                for (int repeat = 0; repeat < 12; repeat++) lift.Tick(.1f);
                var requests = (Queue<int>)Get(lift, "requests");
                Check(requests.Count == 1 && requests.Peek() == 2 && ((bool[])Get(lift, "requested"))[2]
                    && !lift.IsMoving && lift.DoorOpenProgress == 0f && (float)Get(lift, "doorTarget") == 0f,
                    "A pending landing keeps its exact queued request and all doors shut without movement.");
                Check(liftSupport.SequenceEqual(PhysicsSignature(lift.transform)),
                    "Pending destination readiness preserves the exact cabin support and closed landing collider positions.");
                Check(lift.RequestFloor(2) && requests.Count == 1, "Repeated destination taps do not duplicate a pending request.");
                destinationReady = true; lift.Tick(.025f);
                Check(lift.IsMoving && lift.TargetFloor == 2 && requests.Count == 0 && !((bool[])Get(lift, "requested"))[2],
                    "The original request starts exactly once after its destination becomes ready.");
                for (int tick = 0; tick < 200 && !lift.DoorsOpen; tick++) lift.Tick(.1f);
                Check(lift.CurrentFloor == 2 && lift.DoorsOpen, "The ready destination is reached and its supported doors open.");

                bool initialReady = false;
                SlopCityElevator held = SlopCityElevator.Create(root.transform, Vector3.zero,
                    new[] { 0f, 3.2f }, null, null, null, true, null, _ => initialReady);
                held.Tick(.25f);
                Check(held.DoorOpenProgress == 0f && !held.OpenDoors(), "An unsupported initial landing cannot open its doors.");
                initialReady = true;
                for (int tick = 0; tick < 20 && !held.DoorsOpen; tick++) held.Tick(.1f);
                Check(held.DoorsOpen, "An initial deferred landing retries and opens automatically when supported.");

                bool failPreparation = true;
                SlopCityElevator failed = SlopCityElevator.Create(root.transform, Vector3.zero,
                    new[] { 0f, 3.2f }, null, null, null, true, null, floor => {
                        if (floor != 0 && failPreparation) throw new InvalidOperationException("Expected synthetic floor-construction fault; no allocation pressure.");
                        return true;
                    });
                failed.CloseDoors(); failed.RequestFloor(1); failed.Tick(.1f);
                Check(((Queue<int>)Get(failed, "requests")).Count == 1 && !failed.IsMoving && failed.DoorOpenProgress == 0f,
                    "A genuine preparation exception also preserves the queued request and closed-door interlock.");
                failPreparation = false; Set(failed, "preparationRetryAfter", 0f); failed.Tick(.025f);
                Check(failed.IsMoving && failed.TargetFloor == 1, "A preparation error recovers using the original request without another button press.");
                receipt.floorAdmissionPolicyChecks = true;
                receipt.pendingElevatorReadinessChecks = true;
            }
            finally { DestroyFixture(root); Object.DestroyImmediate(user); if (coreMesh) Object.DestroyImmediate(coreMesh); }
        }

        private static void EntranceAdmission()
        {
            GameObject root = new GameObject("Entrance_Admission_Fixture");
            GameObject user = new GameObject("Entrance_Admission_User");
            Mesh coreMesh = null;
            try
            {
                SlopCityFirstPerson rider = user.AddComponent<SlopCityFirstPerson>();
                Set(rider, "paused", false); Set(rider, "seated", false);
                var world = root.AddComponent<SlopCityBuildingWorld>();
                Set(world, "constrainedBrowser", true); Set(world, "player", rider);
                GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
                coreMesh = Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
                Object.DestroyImmediate(primitive);
                coreMesh.bounds = new Bounds(new Vector3(0, 9.6f, 0), new Vector3(16, 19.2f, 16));
                GameObject tower = Child(root.transform, "Skyline_Tower_991");
                Surface(tower.transform, "Structural_Core", coreMesh).AddComponent<BoxCollider>().size = coreMesh.bounds.size;
                var building = tower.AddComponent<SlopCityBuildingInterior>();
                Call(building, "InitializeTower", world, rider);
                ((List<SlopCityBuildingInterior>)Get(world, "buildings")).Add(building);
                Vector3 entrance = building.EntranceFeet;
                Vector3 side = tower.transform.TransformPoint(new Vector3(12, .08f, 0));
                Vector3 back = tower.transform.TransformPoint(new Vector3(0, .08f, 12));
                foreach (Vector3 point in new[] { side, back, entrance + Vector3.back * 5.1f, entrance + Vector3.up * 4f })
                {
                    user.transform.position = point; world.RefreshStreaming();
                    Check(!building.Elevator && building.LoadedFloorCount == 0 && !tower.transform.Find("Runtime_Hollow_Building_Shell"),
                        "Constrained side/back walking, distant approach and unsupported vertical proximity allocate no full tower detail.");
                }
                user.transform.position = entrance + Vector3.back * 4.9f;
                world.RefreshStreaming();
                SlopCityElevator lift = building.Elevator;
                Check(lift && building.IsFloorLoaded(0) && building.LoadedFloorCount <= 2,
                    "A front approach within five metres admits the physical lift and supported lobby before reaching the door.");
                for (int tick = 0; tick < 20 && !lift.DoorsOpen; tick++) lift.Tick(.1f);
                Check(lift.CanUnload, "The approach fixture has an actually idle, unloadable car before testing retention hysteresis.");
                user.transform.position = entrance + Vector3.back * 6.9f; world.RefreshStreaming();
                Check(building.Elevator == lift && building.IsFloorLoaded(0),
                    "An existing idle lift retains its exact supported instance within the seven-metre entrance margin.");
                user.transform.position = entrance + Vector3.back * 7.1f; world.RefreshStreaming();
                Check(!building.Elevator && building.LoadedFloorCount == 0,
                    "An idle empty lift outside the seven-metre entrance margin retires its rooms and car.");

                Transform occupied = building.EnsureFloor(1);
                user.transform.position = building.LandingFeet(1); world.RefreshStreaming();
                lift = building.Elevator;
                Check(lift && building.EnsureFloor(1) == occupied,
                    "An occupied upper floor admits and retains its lift independently of the ground-door vertical guard.");
                CheckFloorSupport(building, occupied, 1);
                int roof = building.FloorCount - 1;
                user.transform.position = building.LandingFeet(roof); world.RefreshStreaming();
                Check(building.Elevator == lift && building.IsFloorLoaded(roof),
                    "An occupied roof retains its floor and lift despite being far above the entrance.");
                user.transform.position = lift.Cabin.TransformPoint(Vector3.up * .01f); Physics.SyncTransforms();
                Check(lift.PlayerInside && (bool)Call(world, "ShouldConsiderTowerDetail", building, entrance + Vector3.right * 1000f),
                    "An actual cabin passenger pins the lift even when the candidate point is outside all entrance and footprint ranges.");

                user.transform.position = entrance + Vector3.back * 12f;
                lift.CloseDoors(); lift.RequestFloor(1);
                for (int tick = 0; tick < 30 && !lift.IsMoving; tick++) lift.Tick(.1f);
                Check(lift.IsMoving && !lift.PlayerInside && !lift.CanUnload,
                    "A genuinely moving empty car supplies the non-unloadable fixture.");
                world.RefreshStreaming();
                Check(building.Elevator == lift && (bool)Call(world, "ShouldConsiderTowerDetail", building, user.transform.position),
                    "A non-unloadable car remains eligible outside the entrance margin.");

                Set(lift, "moving", false); Set(lift, "currentFloor", 0); Set(lift, "targetFloor", 0);
                Set(lift, "openProgress", 0f); Set(lift, "doorTarget", 0f);
                lift.Cabin.localPosition = Vector3.up * lift.FloorElevation(0); Physics.SyncTransforms();
                Set(world, "constrainedBrowser", false);
                foreach (Vector3 point in new[] { side, back, entrance, entrance + Vector3.back * 12f, entrance + Vector3.up * 100f })
                {
                    bool original = (bool)Call(building, "ContainsHorizontal", point, building.Elevator ? 9f : 7f)
                        || (building.Elevator && !building.Elevator.CanUnload);
                    Check((bool)Call(world, "ShouldConsiderTowerDetail", building, point) == original,
                        "Native candidate admission exactly preserves the original footprint and lift policy.");
                }
                user.transform.position = new Vector3(1000, 0, 1000); world.RefreshStreaming();
                Check(!building.Elevator, "A distant idle native fixture retires before cold-admission parity is checked.");
                user.transform.position = side; world.RefreshStreaming();
                Check(building.Elevator && building.IsFloorLoaded(0), "Native side-wall approach still admits tower detail using its original footprint policy.");
                receipt.entranceAdmissionChecks = true;
            }
            finally { DestroyFixture(root); Object.DestroyImmediate(user); if (coreMesh) Object.DestroyImmediate(coreMesh); }
        }

        private static void ArchitectureRetirement()
        {
            GameObject root = new GameObject("Traversal_City_Fixture");
            GameObject user = new GameObject("Traversal_Architecture_User");
            SlopCityFirstPerson rider = user.AddComponent<SlopCityFirstPerson>();
            Mesh cube = null, facade = null;
            try
            {
                var world = root.AddComponent<SlopCityBuildingWorld>();
                Set(world, "constrainedBrowser", true); Set(world, "player", rider);
                Set(world, "detailAdmissionAfterFrame", 42);
                Check(!(bool)Call(world, "CanAdmitTowerDetail", null, 41), "A retired tower's frame cannot admit the next detail allocation.");
                Check((bool)Call(world, "CanAdmitTowerDetail", null, 42), "The next frame admits detail after deferred destruction.");
                Set(world, "constrainedBrowser", false);
                Check((bool)Call(world, "CanAdmitTowerDetail", null, 41), "Native admission keeps its existing immediate behavior.");
                Set(world, "constrainedBrowser", true);
                GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube = Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
                Object.DestroyImmediate(primitive);
                cube.bounds = new Bounds(new Vector3(0, 9.6f, 0), new Vector3(16, 19.2f, 16));
                facade = new Mesh { name = "Traversal_Original_Facade" };
                facade.vertices = new[] { new Vector3(-8, 0, 0), new Vector3(8, 0, 0), new Vector3(-8, 19.2f, 0), new Vector3(8, 19.2f, 0) };
                facade.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                facade.triangles = new[] { 0, 2, 1, 2, 3, 1 }; facade.RecalculateBounds(); facade.RecalculateNormals();
                Type geometry = typeof(SlopCityBuildingWorld).Assembly.GetType("SlopCity.SlopCityInteriorGeometry", true);
                geometry.GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                var retainedDoorwayMeshes = new List<Mesh>();
                long retainedDoorwayBytes = 0;
                for (int towerIndex = 0; towerIndex < 8; towerIndex++)
                {
                    GameObject tower = Child(root.transform, "Skyline_Tower_" + (900 + towerIndex));
                    tower.transform.localPosition = new Vector3(towerIndex * 50, 0, 0);
                    GameObject core = Surface(tower.transform, "Structural_Core", cube);
                    Renderer coreRenderer = core.GetComponent<Renderer>(); BoxCollider coreCollider = core.AddComponent<BoxCollider>();
                    coreCollider.size = cube.bounds.size;
                    GameObject deck = Surface(tower.transform, "Rooftop_Deck", cube);
                    deck.GetComponent<Renderer>().enabled = false; deck.AddComponent<BoxCollider>();
                    GameObject front = Surface(tower.transform, "CurtainWall_Panel", facade);
                    front.transform.localPosition = new Vector3(0, 0, -8.018f);
                    GameObject equipment = Child(tower.transform, "Mechanical_Unit");
                    Vector3 originalEquipment = equipment.transform.localPosition = new Vector3(3, 20, 4);
                    var building = tower.AddComponent<SlopCityBuildingInterior>();
                    Call(building, "InitializeTower", world, rider);
                    Check(!tower.transform.Find("Runtime_Hollow_Building_Shell"), "Constrained tower registration does not allocate distant architecture.");
                    Mesh[] doorwayMeshes = null;
                    long doorwayBytes = 0;
                    for (int cycle = 0; cycle < 3; cycle++)
                    {
                        user.transform.position = new Vector3(1000, 0, 1000);
                        Call(building, "EnsureArchitecture");
                        Check(!coreRenderer.enabled && !coreCollider.enabled, "Completed replacement safely retires authored solid collision.");
                        Mesh trim = front.GetComponent<MeshFilter>().sharedMesh;
                        Check(trim != facade && !trim.isReadable, "Finished entrance trim owns only its GPU render copy.");
                        Mesh[] owned = ((List<Mesh>)Get(building, "ownedFacadeMeshes")).ToArray();
                        Check(owned.Length == 2 && owned.All(mesh => mesh && !mesh.isReadable),
                            "Each synthetic tower retains exactly one core cut and one facade cut, without CPU mesh copies.");
                        if (doorwayMeshes == null)
                        {
                            doorwayMeshes = owned; doorwayBytes = owned.Sum(NativeMeshPayloadBytes);
                            Check(doorwayBytes > 0 && doorwayBytes <= 64 * 1024,
                                "Measured synthetic doorway mesh payload stays below its 64 KiB fixture budget.");
                            retainedDoorwayMeshes.AddRange(owned); retainedDoorwayBytes += doorwayBytes;
                        }
                        Check(owned.SequenceEqual(doorwayMeshes) && owned.Sum(NativeMeshPayloadBytes) == doorwayBytes,
                            "Repeated admission reuses the exact same native doorway meshes and buffer payload.");
                        SlopCityDoor door = tower.transform.Find("Runtime_Persistent_Building_Entrance").GetComponentInChildren<SlopCityDoor>();
                        Set(door, "angle", 45f); Set(door, "targetAngle", 95f);
                        door.Leaf.localRotation = Quaternion.Euler(0, 45, 0);
                        Quaternion rotation = door.Leaf.localRotation;
                        user.transform.position = tower.transform.TransformPoint(new Vector3(0, 1, 0));
                        Check(!(bool)Call(building, "ReleaseStream"), "An occupied building cannot retire its shell or support.");
                        user.transform.position = tower.transform.TransformPoint(new Vector3(0, .08f, -8.8f));
                        Check(!(bool)Call(building, "ReleaseArchitecture"), "Nearby entrance support is preserved during admission.");
                        user.transform.position = new Vector3(1000, 0, 1000);
                        Check((bool)Call(building, "ReleaseStream"), "Distant empty tower releases its architecture.");
                        Check(!tower.transform.Find("Runtime_Hollow_Building_Shell") && trim,
                            "Full shell retires while the small doorway cut remains available to the persistent entrance.");
                        Check(front.GetComponent<MeshFilter>().sharedMesh == trim && facade.isReadable,
                            "The open-entrance facade cut persists while its exact authored source remains untouched.");
                        Check(((List<Mesh>)Get(building, "ownedFacadeMeshes")).SequenceEqual(doorwayMeshes),
                            "Retirement neither destroys nor accumulates the deliberately retained doorway meshes.");
                        Check(coreRenderer.enabled && coreCollider.enabled && !deck.GetComponent<Renderer>().enabled && deck.GetComponent<Collider>().enabled,
                            "Exact authored enabled and disabled renderer/collider states are restored.");
                        Check(equipment.transform.localPosition == originalEquipment, "Distant roof equipment restores its original silhouette.");
                        Check(door && door.Leaf.localRotation == rotation && (float)Get(door, "targetAngle") == 95f, "Entrance door identity, angle and target state survive retirement.");
                        Call(building, "EnsureArchitecture");
                        Check(tower.transform.Find("Runtime_Persistent_Building_Entrance").GetComponentInChildren<SlopCityDoor>() == door && door.Leaf.localRotation == rotation,
                            "Re-admission never recreates or resets the persistent physical door.");
                        Call(building, "ReleaseStream");
                        Check(root.GetComponentsInChildren<Transform>(true).Count(item => item.name == "Runtime_Hollow_Building_Shell") == 0,
                            "Visited towers accumulate no hollow-shell residency.");
                        Check(retainedDoorwayMeshes.All(mesh => mesh) && retainedDoorwayMeshes.Count == (towerIndex + 1) * 2,
                            "First visits retain at most two doorway meshes per visited synthetic tower; revisits add none.");
                    }
                }
                receipt.towersVisited = 8;
                receipt.retainedDoorwayMeshes = retainedDoorwayMeshes.Count;
                receipt.retainedDoorwayPayloadBytes = retainedDoorwayBytes;
                // Unawakened Edit Mode components do not reliably receive runtime
                // OnDestroy. Invoke that exact owner cleanup at the deletion
                // boundary; this is a cleanup-path check, not a Play Mode soak.
                DestroyFixture(root); root = null;
                Check(retainedDoorwayMeshes.All(mesh => !mesh), "Tower deletion cleanup destroys every retained native doorway mesh.");
            }
            finally { DestroyFixture(root); Object.DestroyImmediate(user); if (cube) Object.DestroyImmediate(cube); if (facade) Object.DestroyImmediate(facade); }
        }

        private static GameObject Child(Transform parent, string name) { var item = new GameObject(name); item.transform.SetParent(parent, false); return item; }
        private static void DestroyFixture(GameObject root)
        {
            if (!root) return;
            foreach (SlopCityInteriorFurnishing furnishing in root.GetComponentsInChildren<SlopCityInteriorFurnishing>(true)) Call(furnishing, "Release");
            foreach (SlopCityBuildingInterior building in root.GetComponentsInChildren<SlopCityBuildingInterior>(true)) Call(building, "OnDestroy");
            Object.DestroyImmediate(root);
        }
        private static long NativeMeshPayloadBytes(Mesh mesh)
        {
            long bytes = 0;
            for (int stream = 0; stream < mesh.vertexBufferCount; stream++) bytes += (long)mesh.vertexCount * mesh.GetVertexBufferStride(stream);
            int indexBytes = mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ? 4 : 2;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) bytes += (long)mesh.GetIndexCount(submesh) * indexBytes;
            return bytes;
        }
        private static void CheckFloorSupport(SlopCityBuildingInterior building, Transform floor, int index)
        {
            Physics.SyncTransforms();
            bool supported = Physics.Raycast(building.LandingFeet(index) + Vector3.up * .5f, Vector3.down,
                out RaycastHit hit, 1f, ~0, QueryTriggerInteraction.Ignore);
            Check(supported && hit.transform.IsChildOf(floor), "The preserved occupied landing retains its actual floor collider support.");
        }
        private static GameObject Surface(Transform parent, string name, Mesh mesh) { GameObject item = Child(parent, name); item.AddComponent<MeshFilter>().sharedMesh = mesh; item.AddComponent<MeshRenderer>(); return item; }
        private static string[] PhysicsSignature(Transform root)
        {
            return root.GetComponentsInChildren<BoxCollider>(true).Select(body =>
            {
                Vector3 point = root.InverseTransformPoint(body.transform.position);
                return body.name + ":" + Vector(point) + ":" + Vector(body.center) + ":" + Vector(body.size) + ":" + Vector(body.transform.lossyScale) + ":" + body.enabled + ":" + body.isTrigger;
            }).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }
        private static string Vector(Vector3 value) => value.x.ToString("R", CultureInfo.InvariantCulture) + "," + value.y.ToString("R", CultureInfo.InvariantCulture) + "," + value.z.ToString("R", CultureInfo.InvariantCulture);
        private static object Call(object target, string method, params object[] values) => target.GetType().GetMethod(method, Private).Invoke(target, values);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
        private static void Check(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); checks++; }
        [Serializable] private sealed class Receipt
        {
            public string startedAtUtc, completedAtUtc, unityVersion, error;
            public bool success, floorAdmissionPolicyChecks, pendingElevatorReadinessChecks, entranceAdmissionChecks;
            public int checks, towersVisited, nativeLiftRenderers, maximumConstrainedLiftRenderers, completeLiftColliders, retainedDoorwayMeshes;
            public long retainedDoorwayPayloadBytes;
        }
    }
}
#endif
