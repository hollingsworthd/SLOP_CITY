#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Real runtime interiors and elevator physics in an isolated batch Editor identity.</summary>
    [InitializeOnLoad]
    public static class SlopCityInteriorSmokeTest
    {
        private const string Active = "SlopCity.InteriorSmoke.Active", Started = "SlopCity.InteriorSmoke.Started";
        private const string DefaultPrefix = "concrete-v0.11.5";
        private static string Prefix => SessionState.GetString("SlopCity.InteriorSmoke.Prefix", DefaultPrefix);
        public static string ReportPath => "Builds/Reports/" + Prefix + "-interiors-check.json";
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static readonly HashSet<string> Captured = new HashSet<string>();
        private static readonly Dictionary<string, Vector3[]> HomeFacadeTriangles = new Dictionary<string, Vector3[]>();
        private static string homeFacadeCapturedAtUtc;
        private static Receipt report;
        private static SlopCityFirstPerson player;
        private static CharacterController capsule;
        private static SlopCityPlayerInput input;
        private static Camera camera;
        private static SlopCityBuildingWorld world;
        private static bool started;
        private static int lastFrame = -1;
        private static double enteredAt;

        // Validation-only readback before Unity makes the authored military batches
        // unreadable. No production mesh, flag or renderer is changed by this snapshot.
        public static void CaptureHomeFacadeBeforeBatch(Scene scene)
        {
            if (!SessionState.GetBool(Active, false) || !Application.isBatchMode
                || !Application.persistentDataPath.Contains("SlopCityValidation")) return;
            HomeFacadeTriangles.Clear();
            Transform city = scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<Transform>(true))
                .Single(item => item.name == "SlopCity_NineBlocks");
            SlopCityDistrict home = city.GetComponentsInChildren<SlopCityDistrict>(true).Single(item => item.Index == 4);
            Transform east = home.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name.StartsWith("City_Skyscraper_", StringComparison.Ordinal) && item.position.x > 0);
            var roots = new List<Transform> { east };
            Transform armor = city.Find("City_MilitarySciFiArchitecture");
            if (armor)
                foreach (Transform root in armor)
                    if (root.name.StartsWith("MilitarySF_LocalTower_", StringComparison.Ordinal)
                        && Vector3.Distance(root.position, east.position) < .05f) roots.Add(root);
            foreach (MeshFilter filter in roots.SelectMany(item => item.GetComponentsInChildren<MeshFilter>(true)))
            {
                Mesh mesh = filter.sharedMesh;
                if (!mesh || !mesh.isReadable) throw new InvalidOperationException("Home facade snapshot requires original readable geometry: " + Hierarchy(filter.transform));
                Vector3[] vertices = mesh.vertices; int[] indices = mesh.triangles;
                Vector3[] triangles = new Vector3[indices.Length];
                for (int index = 0; index < indices.Length; index++)
                    triangles[index] = filter.transform.TransformPoint(vertices[indices[index]]);
                HomeFacadeTriangles[Hierarchy(filter.transform)] = triangles;
            }
            homeFacadeCapturedAtUtc = DateTime.UtcNow.ToString("O");
            // Scene processing can precede the Editor's Play Mode domain reload.
            // Persist the fixture's raw triangle evidence so that reload cannot lose it.
            string snapshotPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "Builds/Reports/" + Prefix + "-home-facade-triangles.json");
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath));
            File.WriteAllText(snapshotPath, JsonUtility.ToJson(new HomeTriangleSnapshot { capturedAtUtc = homeFacadeCapturedAtUtc,
                meshes = HomeFacadeTriangles.Select(item => new HomeTriangleMesh { path = item.Key, triangles = item.Value }).ToArray() }));
            SessionState.SetString("SlopCity.InteriorSmoke.HomeTriangles", snapshotPath);
        }

        static SlopCityInteriorSmokeTest()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Active, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (state == PlayModeStateChange.EnteredEditMode) Finish(new InvalidOperationException("Interior Play Mode ended early."));
            };
            Application.logMessageReceived += CaptureLog;
        }

        // Dedicated cloned project only: companyName must contain SlopCityValidation.
        // Graphics enabled, StandaloneOSX, omit -quit; this runner exits itself.
        public static void Run()
        {
            if (!Application.isBatchMode || !Application.persistentDataPath.Contains("SlopCityValidation"))
                throw new InvalidOperationException("Interior smoke requires a dedicated cloned project with an isolated SlopCityValidation company/save identity.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start outside Play Mode.");
            string[] args = Environment.GetCommandLineArgs();
            int arg = Array.IndexOf(args, "-slopCityInteriorPrefix");
            string prefix = arg >= 0 && arg + 1 < args.Length ? args[arg + 1] : DefaultPrefix;
            if (prefix.Any(character => !(char.IsLetterOrDigit(character) || character == '-' || character == '.')))
                throw new ArgumentException("Interior prefix may contain letters, digits, dots and hyphens only.");
            SessionState.SetString("SlopCity.InteriorSmoke.Prefix", prefix);
            SessionState.SetString("SlopCity.InteriorSmoke.HomeTriangles", "");
            SessionState.SetString(Started, DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(Active, true); report = NewReceipt(); WriteReport();
            try
            {
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                foreach (SlopCityTikTokSync sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsSortMode.None)) sync.enabled = false;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }

        private static Receipt NewReceipt() => new Receipt
        {
            startedAtUtc = SessionState.GetString(Started, DateTime.UtcNow.ToString("O")),
            unityVersion = Application.unityVersion, scenePath = SlopCitySceneBuilder.ScenePath,
            isolatedDataPath = Application.persistentDataPath
        };

        private static void Update()
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (report == null) report = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(report.startedAtUtc).ToUniversalTime()).TotalMinutes > 25)
                    throw new TimeoutException("Interior smoke exceeded twenty-five minutes.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                if (!started)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < 1) return;
                    started = true; Work.Push(Exercise());
                }
                while (Work.Count > 0)
                {
                    IEnumerator current = Work.Peek();
                    if (!current.MoveNext()) { Work.Pop(); continue; }
                    if (current.Current is IEnumerator nested) { Work.Push(nested); continue; }
                    return;
                }
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static IEnumerator Exercise()
        {
            Application.runInBackground = true;
            player = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            Check(player && player.Sandbox, "Saved scene starts its production first-person controller and sandbox.");
            capsule = player.GetComponent<CharacterController>(); input = player.GetComponent<SlopCityPlayerInput>(); camera = player.ViewCamera;
            Check(capsule && input && camera, "Real player capsule, input and camera exist.");
            report.playerHeight = capsule.height; report.playerRadius = capsule.radius;
            report.eyeHeight = new SerializedObject(player).FindProperty("standingEyeHeight").floatValue;
            Check(Mathf.Abs(report.playerHeight - 1.72f) < .001f && Mathf.Abs(report.playerRadius - .22f) < .001f
                && Mathf.Abs(report.eyeHeight - 1.62f) < .001f && (player.transform.lossyScale - Vector3.one).sqrMagnitude < .00001f,
                "Human-scale city preserves the 1.72m player, .22m radius and 1.62m eye without shrinking the observer.");
            player.SendMessage("OnApplicationFocus", true); player.SendMessage("OnApplicationPause", false);
            player.PlayOrResume(); player.StopMedia(); input.RequestInteract();
            yield return Until(() => !player.IsSeated && capsule.enabled, "Couch USE restores normal walking.");
            world = SlopCityBuildingWorld.Ensure(player);
            Check(world, "Runtime building installer exists in the actual saved city.");
            report.protectedGeometryRenderers = SlopCityWebGeometryBuildProcessor.ProtectedRendererCount;
            Check(report.protectedGeometryRenderers > 0,
                "Actual production Play Mode scene processing protected metric geometry before static batching.");
            var buildings = world.Buildings.OrderBy(building => building.BuildingId).ToArray();
            report.buildings = buildings.Length;
            string[] towerIds = world.GetComponentsInChildren<Transform>(true).Where(item => item.name.StartsWith("City_Skyscraper_", StringComparison.Ordinal)
                || item.name.StartsWith("Skyline_Tower_", StringComparison.Ordinal) || item.name == "MilitarySF_CommunicationsCitadel")
                .Select(item => item.name).Distinct().OrderBy(id => id).ToArray();
            string[] descriptorIds = buildings.Where(building => building.IsTower).Select(building => building.BuildingId).OrderBy(id => id).ToArray();
            report.expectedTowerCount = towerIds.Length;
            report.inventoryComplete = towerIds.SequenceEqual(descriptorIds);
            report.missingBuildings.AddRange(towerIds.Except(descriptorIds));
            Check(towerIds.Length >= 51 && buildings.Count(building => building.PreservedInterior) == 7,
                "Saved-city inventory retains its district/skyline towers, six venues and the home.");
            Check(buildings.Select(building => building.BuildingId).Distinct().Count() == buildings.Length,
                "Every building has a distinct persistent descriptor identity.");
            foreach (SlopCityBuildingInterior building in buildings)
            {
                SlopCityDoor[] entries = building.transform.GetComponentsInChildren<SlopCityDoor>(true)
                    .Where(door => building.PreservedInterior || door.name == "Building_Entrance_Door").ToArray();
                // The authored home door is a scene sibling rather than a lounge child.
                if (building.BuildingId == "SlopCity_Lounge")
                    entries = Object.FindObjectsByType<SlopCityDoor>(FindObjectsSortMode.None)
                        .Where(door => door.name == "Home_Entrance").ToArray();
                Check(entries.Length > 0, building.BuildingId + " has a physical operable building entrance.");
                foreach (SlopCityDoor door in entries)
                {
                    Check(door.Leaf && door.Blocker && door.Blocker.enabled && !door.IsOpen && !door.IsMoving,
                        building.BuildingId + " entry starts closed with collision and bounded interaction.");
                    Check(Quaternion.Angle(door.Leaf.localRotation,
                        new SerializedObject(door).FindProperty("closedLocalRotation").quaternionValue) < .01f,
                        building.BuildingId + " actual entry leaf matches its physical closed pose.");
                    if (building.IsTower)
                    {
                        Physics.SyncTransforms();
                        Vector3 eye = building.EntranceFeet + Vector3.up * report.eyeHeight;
                        Vector3 aim = door.InteractionPoint - eye;
                        RaycastHit[] hits = Physics.RaycastAll(eye, aim.normalized, aim.magnitude + .2f, ~0, QueryTriggerInteraction.Ignore)
                            .Where(hit => Relevant(hit.collider)).OrderBy(hit => hit.distance).ToArray();
                        Check(aim.magnitude < 2.4f && hits.Length > 0 && hits[0].transform.GetComponentInParent<SlopCityDoor>() == door,
                            building.BuildingId + " closed street leaf is the unobstructed nearby aim target.");
                    }
                }
            }
            report.occupiedStories = buildings.Sum(building => building.OccupiedFloorCount);
            report.declaredStops = buildings.Sum(building => building.FloorCount);
            Check(buildings.Length == 59 && descriptorIds.Length == 52 && report.occupiedStories == 1259 && report.declaredStops == 1311,
                "Human-scale layout preserves all 59 buildings, 1259 occupied stories and 52 roofs / 1311 stops.");
            Capture("dense-home-start", "Authored Home", 0);
            foreach (SlopCityBuildingInterior building in buildings.Where(item => item.IsTower)) VerifyBuildingMetrics(building);
            VerifyTowerPlotSeparation(buildings);
            MeasureCityFootprint(buildings);
            VerifyStreetBuildLines(buildings);
            VerifyCrowdPavement();
            int credits = player.Sandbox.Credits, xp = player.Sandbox.Experience, completed = player.Sandbox.CompletedContracts;
            int sources = Object.FindObjectsByType<SlopCityCityScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            report.initialObjects = CountObjects(); report.initialMaterials = CountMaterials();
            report.initialAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong();

            // First-person simulation is temporarily disabled while independent elevated-floor probes run.
            // Geometry generation is real production code; no scene file or normal save is changed.
            player.enabled = false;
            VerifyRoofJobSelection(buildings);
            var streetTargets = buildings.Where(building => building.IsTower).Select(building => building.EntranceFeet).ToArray();
            // Narrow streets need closer graph nodes. Every edge still uses the real
            // capsule sweep and support samples at the original .45m spacing.
            // Include a building-width detour around outward-facing boundary doors.
            report.streetGraphStep = 2f;
            report.streetGraphPadding = buildings.Where(building => building.IsTower)
                .Max(building => Mathf.Max(building.GetFloorSize(0).x, building.GetFloorSize(0).y)) + 8f;
            var streets = new FloorRoutes(new Vector3(8, .025f, 0), streetTargets,
                report.streetGraphStep, report.streetGraphPadding);
            report.streetReachableCells = streets.ReachableCount;
            foreach (SlopCityBuildingInterior building in buildings.Where(building => building.IsTower))
            {
                bool supported = Supported(building.EntranceFeet, out Vector3 entry);
                List<Vector3> route = supported ? streets.To(entry) : null;
                bool fineConnection = false;
                if (supported && route == null)
                {
                    Vector3[] joins = streets.ReachableNear(entry, 16f).ToArray();
                    if (joins.Length > 0)
                    {
                        var approach = new FloorRoutes(entry, joins);
                        route = joins.Select(join => approach.To(join)).FirstOrDefault(path => path != null);
                        fineConnection = route != null;
                        report.fineEntranceConnections++;
                    }
                }
                Vector3 nearest = streets.NearestReachable(entry);
                var diagnosis = new EntranceReceipt { building = building.BuildingId, hint = building.EntranceFeet,
                    feet = entry, supported = supported, connected = route != null, fineConnection = fineConnection, nearestReachable = nearest,
                    nearestDistance = Vector3.Distance(nearest, entry), blockingColliders = PointDiagnosis(building.EntranceFeet) };
                report.entrances.Add(diagnosis);
                if (!supported || route == null)
                {
                    player.TeleportTo(building.EntranceFeet - building.transform.forward * 1.5f);
                    camera.transform.LookAt(building.EntranceFeet + Vector3.up);
                    Capture("failed-entrance", building.BuildingId, 0);
                }
            }
            WriteReport();
            Check(report.entrances.All(entry => entry.supported && entry.connected),
                "Supported capsule-clear public streets must reach every tower entrance; failures: "
                + string.Join(", ", report.entrances.Where(entry => !entry.supported || !entry.connected).Select(entry => entry.building)));
            yield return VerifyHomeEnclosure(buildings, streets);
            yield return VerifyServiceAlleys(buildings, streets);
            yield return VerifyStreetCanyons(buildings, streets);
            CaptureMainAvenue(streets);
            foreach (SlopCityBuildingInterior building in buildings)
            {
                var coverage = new BuildingReceipt { id = building.BuildingId, floors = building.FloorCount,
                    occupiedStories = building.OccupiedFloorCount, hasRoofStop = building.IsTower };
                report.coverage.Add(coverage);
                Check(building.FloorCount > 0 && building.LocalFloorElevations.Length == building.FloorCount,
                    building.BuildingId + " declares every floor elevation.");
                for (int floor = 0; floor < building.FloorCount; floor++)
                {
                    yield return GuardFloor(VerifyFloor(building, floor, coverage), building, floor);
                    report.peakActiveFloors = Mathf.Max(report.peakActiveFloors, world.ActiveFloorCount);
                    // Park on permanent street ground before releasing this actual floor.
                    player.TeleportTo(new Vector3(8, .025f, 0));
                    building.ReleaseFloor(floor);
                    yield return null;
                    world.RefreshStreaming();
                    Check(world.ActiveFloorCount <= SlopCityBuildingWorld.MaximumActiveFloors,
                        "Settled streaming stays within its floor bound after " + building.BuildingId + " floor " + floor + ".");
                    Check(world.ActiveElevatorCount <= SlopCityBuildingWorld.MaximumActiveElevators,
                        "Settled streaming stays within its elevator bound after " + building.BuildingId + " floor " + floor + ".");
                }
                WriteReport();
            }
            yield return ExerciseElevator(buildings.First(building => building.IsTower));
            yield return ExerciseRepeatedStreaming(buildings.First(building => building.IsTower));
            yield return ExerciseStreamingAdmission(buildings.Where(building => building.IsTower).Take(3).ToArray());
            player.enabled = true;
            player.TeleportTo(new Vector3(8, .025f, 0));
            yield return null; yield return null;
            report.finalObjects = CountObjects(); report.finalMaterials = CountMaterials();
            report.finalAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
            Check(world.ActiveFloorCount <= SlopCityBuildingWorld.MaximumActiveFloors,
                "Settled streaming retains its declared floor-detail root bound.");
            Check(world.ActiveElevatorCount <= SlopCityBuildingWorld.MaximumActiveElevators,
                "Settled streaming retains its declared total physical elevator instance bound.");
            Check(report.finalMaterials <= report.initialMaterials + 20, "Sequential floor visits do not create an unbounded material population.");
            Check(Object.FindObjectsByType<SlopCityCityScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == sources,
                "Interior generation preserves existing media devices without accumulating streamed video surfaces.");
            Check(player.Sandbox.Credits == credits && player.Sandbox.Experience == xp && player.Sandbox.CompletedContracts == completed,
                "Floor visits/elevator use preserve banked credits, XP and completed jobs.");
            Check(SlopCityWorldUtilities.MaintenancePanelCount == 23 && SlopCityWorldUtilities.RareCount == 4,
                "Runtime interior/elevator retrofit preserves all 23 roof maintenance panels and four deliberate rare finds.");
            Check(report.coverage.All(item => item.verifiedFloors == item.floors), "Every declared floor in every building received real generated-geometry verification.");
            Check(report.inventoryComplete, "Every actual tower requires an installed interior descriptor; missing: " + string.Join(", ", report.missingBuildings));
        }

        private static void VerifyRoofJobSelection(SlopCityBuildingInterior[] buildings)
        {
            // Exercise the production selector with actual persistent roof devices
            // and the actual catalogue step; restore its live job and RNG afterward.
            SlopCitySandbox sandbox = player.Sandbox;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Type type = typeof(SlopCitySandbox);
            var liveSites = (IList)type.GetField("sites", flags).GetValue(sandbox);
            var liveSteps = (IList)type.GetField("steps", flags).GetValue(sandbox);
            object[] originalSites = liveSites.Cast<object>().ToArray(), originalSteps = liveSteps.Cast<object>().ToArray();
            Type legType = type.GetNestedType("ContractStep", System.Reflection.BindingFlags.NonPublic);
            var choose = type.GetMethod("Choose", flags);
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            SlopCityJobStepDefinition maintenance = SlopCityJobCatalog.All.First(job => job.id == "landing-maintenance").steps[1];
            SlopCityInteractable[] panels = originalSites.OfType<SlopCityInteractable>()
                .Where(item => item && item.name.StartsWith("Roof_Maintenance_Panel_", StringComparison.Ordinal)).ToArray();
            try
            {
                Check(panels.Length == 23, "Actual dispatch site inventory contains all 23 persistent roof maintenance panels.");
                foreach (SlopCityBuildingInterior building in buildings.Where(item => item.BuildingId.StartsWith("City_Skyscraper_", StringComparison.Ordinal)))
                {
                    SlopCityInteractable route = building.transform.Find("Tower_Lift_Street").GetComponent<SlopCityInteractable>();
                    string serial = building.BuildingId.Substring("City_Skyscraper_".Length);
                    SlopCityInteractable expected = panels.First(item => item.name == "Roof_Maintenance_Panel_" + serial);
                    Check(route && route.Destination, building.BuildingId + " retains its actual roof-travel destination for dispatch.");
                    object leg = Activator.CreateInstance(legType, true);
                    legType.GetField("site").SetValue(leg, route); legType.GetField("action").SetValue(leg, SlopCityJobAction.Travel);
                    liveSteps.Clear(); liveSteps.Add(leg); liveSites.Clear(); foreach (SlopCityInteractable panel in panels) liveSites.Add(panel);
                    SlopCityInteractable selected = (SlopCityInteractable)choose.Invoke(sandbox, new object[] { maintenance });
                    Check(selected == expected, building.BuildingId + " production upper-level job selector chooses its real destination roof panel.");
                    float distance = Vector2.Distance(new Vector2(expected.InteractionPoint.x, expected.InteractionPoint.z),
                        new Vector2(route.Destination.position.x, route.Destination.position.z));
                    report.farthestRoofPanelFromArrival = Mathf.Max(report.farthestRoofPanelFromArrival, distance);
                    report.roofJobPairs++;
                    SlopCityInteractable neighbour = panels.FirstOrDefault(item => item != expected
                        && Mathf.Abs(item.InteractionPoint.y - route.Destination.position.y) <= 2.6f);
                    if (neighbour)
                    {
                        liveSites.Clear(); liveSites.Add(neighbour);
                        Check(!(SlopCityInteractable)choose.Invoke(sandbox, new object[] { maintenance }),
                            building.BuildingId + " production selector refuses another building's same-level roof panel.");
                        report.rejectedNeighbourRoofPanels++;
                    }
                }
                Check(report.roofJobPairs == 23 && report.farthestRoofPanelFromArrival > 12f,
                    "Real roof dispatch covers all 23 buildings, including a widened roof whose panel exceeds the legacy 12m arrival radius.");
            }
            finally
            {
                liveSites.Clear(); foreach (object site in originalSites) liveSites.Add(site);
                liveSteps.Clear(); foreach (object step in originalSteps) liveSteps.Add(step);
                UnityEngine.Random.state = randomState;
            }
        }

        private static void VerifyBuildingMetrics(SlopCityBuildingInterior building)
        {
            Check((building.transform.lossyScale - Vector3.one).sqrMagnitude < .00001f,
                building.BuildingId + " keeps metric transforms; furniture and elevator passengers are not globally scaled.");
            Vector2 lower = building.GetFloorSize(0), upper = building.GetFloorSize(building.FloorCount - 1);
            bool street = building.BuildingId.StartsWith("City_Skyscraper_", StringComparison.Ordinal);
            if (street)
                Check(lower.x >= 14.399f && lower.y >= 13.199f,
                    building.BuildingId + " has a full-size street footprint at least 14.4 x 13.2m; actual=" + lower);
            if (building.BuildingId.StartsWith("Skyline_Tower_", StringComparison.Ordinal))
            {
                int serial = int.Parse(building.BuildingId.Substring("Skyline_Tower_".Length));
                int index = (serial - 23) % 7, style = serial % 4;
                Vector2 expectedLower = new Vector2(18 + index % 3 * 6, 19 + index % 2 * 10);
                Vector2 expectedUpper = new Vector2(expectedLower.x * (style == 0 ? .83f : .70f),
                    expectedLower.y * (style == 3 ? .68f : .80f));
                Check(Vector2.Distance(lower, expectedLower) < .005f && Vector2.Distance(upper, expectedUpper) < .005f,
                    building.BuildingId + " preserves the originally full-size outer tower and its setback footprint.");
            }
            Transform core = building.transform.Find("Structural_Core");
            if (core)
            {
                Bounds metric = GeometryBounds(core, building.transform);
                Check(Mathf.Abs(metric.size.x - lower.x) < .02f && Mathf.Abs(metric.size.z - lower.y) < .02f,
                    building.BuildingId + " actual exterior core footprint agrees with its interior descriptor.");
            }
            if (street || building.BuildingId.StartsWith("Skyline_Tower_", StringComparison.Ordinal)) VerifyFacadeMetrics(building);
            for (int floor = 1; floor < building.OccupiedFloorCount; floor++)
                Check(Mathf.Abs(building.LocalFloorElevations[floor] - building.LocalFloorElevations[floor - 1] - 3.2f) < .005f,
                    building.BuildingId + " occupied story " + floor + " retains a 3.2m pitch.");
            report.buildingMetrics.Add(new BuildingMetric { building = building.BuildingId, lowerFootprint = lower,
                upperFootprint = upper, occupiedStories = building.OccupiedFloorCount });
            Transform shell = building.transform.Find("Runtime_Hollow_Building_Shell");
            Transform[] front = shell.GetComponentsInChildren<Transform>(true).Where(item => item.name == "Exterior_Front_Glazing").ToArray();
            if (front.Length >= 2)
            {
                Bounds left = GeometryBounds(front.OrderBy(item => item.localPosition.x).First(), building.transform);
                Bounds right = GeometryBounds(front.OrderBy(item => item.localPosition.x).Last(), building.transform);
                Check(right.min.x - left.max.x >= 1f,
                    building.BuildingId + " physical exterior entrance aperture is at least one metre wide.");
            }
        }

        private static void VerifyTowerPlotSeparation(SlopCityBuildingInterior[] buildings)
        {
            SlopCityBuildingInterior[] towers = buildings.Where(building => building.IsTower).ToArray();
            for (int a = 0; a < towers.Length; a++) for (int b = a + 1; b < towers.Length; b++)
            {
                Vector3[] left = PlotCorners(towers[a]), right = PlotCorners(towers[b]);
                Vector3[] axes = { towers[a].transform.right, towers[a].transform.forward,
                    towers[b].transform.right, towers[b].transform.forward };
                float overlap = float.PositiveInfinity;
                foreach (Vector3 axis in axes)
                {
                    float leftMin = left.Min(point => Vector3.Dot(point, axis)), leftMax = left.Max(point => Vector3.Dot(point, axis));
                    float rightMin = right.Min(point => Vector3.Dot(point, axis)), rightMax = right.Max(point => Vector3.Dot(point, axis));
                    overlap = Mathf.Min(overlap, Mathf.Min(leftMax, rightMax) - Mathf.Max(leftMin, rightMin));
                }
                report.towerPlotPairs++;
                Check(overlap <= .05f, "Full-size occupied tower plots must not overlap: " + towers[a].BuildingId
                    + " / " + towers[b].BuildingId + "; minimum projected overlap=" + overlap.ToString("F3") + "m.");
            }
        }

        private static Vector3[] PlotCorners(SlopCityBuildingInterior building)
        {
            Vector2 size = building.GetFloorSize(0);
            return new[] { new Vector3(-size.x * .5f, 0, -size.y * .5f), new Vector3(size.x * .5f, 0, -size.y * .5f),
                new Vector3(-size.x * .5f, 0, size.y * .5f), new Vector3(size.x * .5f, 0, size.y * .5f) }
                .Select(building.transform.TransformPoint).ToArray();
        }

        private static void MeasureCityFootprint(SlopCityBuildingInterior[] buildings)
        {
            Vector3[] corners = buildings.Where(item => item.IsTower).SelectMany(PlotCorners).ToArray();
            Bounds occupied = new Bounds(corners[0], Vector3.zero);
            foreach (Vector3 corner in corners.Skip(1)) occupied.Encapsulate(corner);
            Renderer[] ground = world.GetComponentsInChildren<Renderer>(true).Where(item => item.enabled && item.gameObject.activeInHierarchy
                && (item.name == "Asphalt_Shaft_Cutout" || item.name == "Neighbourhood_Outer_Asphalt")).ToArray();
            Check(ground.Length > 0, "Compact city has actual supported core/outer road geometry.");
            Bounds supported = ground[0].bounds;
            foreach (Renderer surface in ground.Skip(1)) supported.Encapsulate(surface.bounds);
            report.occupiedTowerBounds = occupied; report.supportedGroundBounds = supported;
            report.supportedGroundEnvelopeArea = supported.size.x * supported.size.z;
            report.v0112GroundEnvelopeArea = 332f * 285f;
            report.groundEnvelopeAreaRatioToV0112 = report.supportedGroundEnvelopeArea / report.v0112GroundEnvelopeArea;
            report.totalTowerFootprintArea = buildings.Where(item => item.IsTower).Sum(item => item.GetFloorSize(0).x * item.GetFloorSize(0).y);
            report.layoutColumnPitch = SlopCityDenseLayout.ColumnPitch; report.layoutRowPitch = SlopCityDenseLayout.RowPitch;
            Bounds declared = SlopCityDenseLayout.GroundBounds;
            Check(Mathf.Abs(supported.size.x - declared.size.x) < .05f && Mathf.Abs(supported.size.z - declared.size.z) < .05f
                && Mathf.Abs(supported.center.x - declared.center.x) < .05f && Mathf.Abs(supported.center.z - declared.center.z) < .05f,
                "Actual supported roads agree with the current compact layout bounds.");
            Check(!float.IsNaN(report.supportedGroundEnvelopeArea) && !float.IsInfinity(report.supportedGroundEnvelopeArea) && report.supportedGroundEnvelopeArea > 0,
                "Compactness evidence records the measured supported-ground envelope without treating a numeric density target as art quality.");
        }

        private static IEnumerator VerifyStreetCanyons(SlopCityBuildingInterior[] buildings, FloorRoutes streets)
        {
            var districts = new HashSet<int>();
            var towerPairs = new HashSet<string>();
            foreach (SlopCityBuildingInterior building in buildings.Where(item => item.BuildingId.StartsWith("City_Skyscraper_", StringComparison.Ordinal))
                .OrderBy(item => item.GetComponentInParent<SlopCityDistrict>().Index == 4 ? 0 : 1))
            {
                SlopCityDistrict district = building.GetComponentInParent<SlopCityDistrict>();
                if (!district || districts.Contains(district.Index)) continue;
                Vector2 size = building.GetFloorSize(0);
                bool captured = false;
                for (int side = 0; side < 4 && !captured; side++)
                for (int sample = -1; sample <= 1 && !captured; sample++)
                {
                    bool acrossX = side < 2;
                    float sign = side % 2 == 0 ? -1 : 1;
                    Vector3 local = acrossX ? new Vector3(sign * (size.x * .5f + 1.3f), 0, sample * size.y * .25f)
                        : new Vector3(sample * size.x * .25f, 0, sign * (size.y * .5f + 1.3f));
                    Vector3 hint = building.transform.TransformPoint(local); hint.y = .025f;
                    if (!Supported(hint, out Vector3 feet) || streets.To(feet) == null) continue;
                    Vector3 axis = acrossX ? building.transform.right : building.transform.forward;
                    if (!StreetWall(feet + Vector3.up * report.eyeHeight, axis, out RaycastHit left, out SlopCityBuildingInterior leftOwner)
                        || !StreetWall(feet + Vector3.up * report.eyeHeight, -axis, out RaycastHit right, out SlopCityBuildingInterior rightOwner)
                        || leftOwner == rightOwner) continue;
                    float width = left.distance + right.distance;
                    if (width < 2f || width > 14f) continue;
                    string pair = string.CompareOrdinal(leftOwner.BuildingId, rightOwner.BuildingId) < 0
                        ? leftOwner.BuildingId + "/" + rightOwner.BuildingId : rightOwner.BuildingId + "/" + leftOwner.BuildingId;
                    if (towerPairs.Contains(pair)) continue;
                    Vector3 along = Vector3.Cross(Vector3.up, axis).normalized;
                    Vector3 destination = feet;
                    foreach (float distance in new[] { 6f, 4f, 2f })
                    {
                        if (Supported(feet + along * distance, out Vector3 target) && ClearSegment(feet, target))
                        { destination = target; break; }
                    }
                    if (Vector3.Distance(feet, destination) < 1.9f) continue;
                    player.TeleportTo(feet);
                    camera.transform.LookAt(feet + along * 15f + Vector3.up * 3.5f);
                    string label = report.streetCanyons.Count == 0 ? "metric-street" : "street-canyon-" + district.Index;
                    Capture(label, district.DisplayName, 0);
                    yield return WalkTo(destination);
                    Check(Vector3.Distance(player.transform.position, destination) < .15f,
                        district.DisplayName + " narrow public lane supports actual adult-capsule walking between full-size facades.");
                    report.streetCanyons.Add(new StreetCanyonReceipt { district = district.Index, leftBuilding = leftOwner.BuildingId,
                        rightBuilding = rightOwner.BuildingId, feet = feet, walkedTo = destination, wallToWallWidth = width });
                    districts.Add(district.Index); towerPairs.Add(pair); captured = true;
                }
                if (districts.Count >= 3) break;
            }
            WriteReport();
            Check(report.streetCanyons.Count >= 3,
                "At least three different districts and distinct tower pairs retain supported, connected and walkable 2–14m street canyons between physical building facades; actual="
                + report.streetCanyons.Count + ".");
        }

        private static Bounds DistrictFootprint(SlopCityBuildingInterior building, SlopCityDistrict district)
        {
            Vector3[] corners = PlotCorners(building).Select(district.transform.InverseTransformPoint).ToArray();
            Bounds measured = new Bounds(corners[0], Vector3.zero);
            foreach (Vector3 corner in corners.Skip(1)) measured.Encapsulate(corner);
            return measured;
        }

        private static void VerifyStreetBuildLines(SlopCityBuildingInterior[] buildings)
        {
            foreach (SlopCityBuildingInterior building in buildings.Where(item => item.BuildingId.StartsWith("City_Skyscraper_", StringComparison.Ordinal)))
            {
                SlopCityDistrict district = building.GetComponentInParent<SlopCityDistrict>();
                Check(district, building.BuildingId + " retains its actual district plot identity.");
                Bounds plot = DistrictFootprint(building, district);
                if (Mathf.Abs(plot.center.x) < .1f) continue;
                float sign = Mathf.Sign(plot.center.x);
                float face = sign > 0 ? plot.max.x : -plot.min.x;
                Renderer curb = district.GetComponentsInChildren<Renderer>(true)
                    .Where(item => item.name == "Low_Curb_NS" && item.enabled && item.gameObject.activeInHierarchy)
                    .OrderBy(item => Mathf.Abs(district.transform.InverseTransformPoint(item.bounds.center).x - sign * SlopCityDenseLayout.PavementHalfWidth)).FirstOrDefault();
                Check(curb, building.BuildingId + " has a physical curb along its side frontage.");
                Vector3 curbMin = district.transform.InverseTransformPoint(curb.bounds.min), curbMax = district.transform.InverseTransformPoint(curb.bounds.max);
                float curbEdge = sign > 0 ? curbMax.x : -curbMin.x;
                float curbInner = sign > 0 ? curbMin.x : -curbMax.x;
                var measured = new StreetFrontageReceipt { district = district.Index, building = building.BuildingId,
                    side = sign > 0 ? "east" : "west", declaredBuildLine = SlopCityDenseLayout.SideBuildToLine,
                    actualOccupiedFace = face, curbOuterEdge = curbEdge, curbInnerEdge = curbInner,
                    declaredSidewalkWidth = SlopCityDenseLayout.FrontageSidewalkWidth,
                    physicalSidewalkWidth = curbEdge - face, clearSlabToCurbWidth = curbInner - face,
                    clearCentreLaneWidth = SlopCityDenseLayout.ColumnPitch - 2 * SlopCityDenseLayout.PavementHalfWidth,
                    occupiedFront = plot.min.z, declaredSouthBuildLine = SlopCityDenseLayout.SouthBuildToLine,
                    specialPreservedVenue = district.Index == 2 || district.Index == 4 || district.Index == 5 };
                report.streetFrontages.Add(measured);
                Check(Mathf.Abs(face - measured.declaredBuildLine) < .025f,
                    building.BuildingId + " actual occupied side wall meets the aligned sidewalk build line without a forecourt setback.");
                Check(Mathf.Abs(curbEdge - SlopCityDenseLayout.PavementHalfWidth) < .025f
                    && Mathf.Abs(measured.physicalSidewalkWidth - measured.declaredSidewalkWidth) < .025f,
                    building.BuildingId + " physical curb and occupied wall bound the declared side sidewalk, measured=" + measured.physicalSidewalkWidth.ToString("F3") + "m.");
                Check(measured.clearSlabToCurbWidth > capsule.radius * 2 + .2f && curb.bounds.max.y < .081f,
                    building.BuildingId + " measured frontage retains adult body width beside a physically low curb.");
                Check(measured.clearCentreLaneWidth >= capsule.radius * 2 + .6f,
                    "Pedestrian shared lane between actual side curbs retains adult passing width without enlarging the city envelope.");
                if (!measured.specialPreservedVenue)
                {
                    Check(Mathf.Abs(plot.min.z - measured.declaredSouthBuildLine) < .025f,
                        building.BuildingId + " actual occupied south frontage shares its street build line with the preserved full-size venue.");
                    SlopCityBuildingInterior venue = buildings.Single(item => item.PreservedInterior && item.transform.parent == district.transform);
                    Transform venueFloor = venue.transform.Find("Interior_Floor");
                    Check(venueFloor, district.DisplayName + " preserved venue retains its actual physical floor.");
                    Bounds venueFootprint = GeometryBounds(venueFloor, district.transform);
                    measured.venueLeftEdge = venueFootprint.min.x; measured.venueRightEdge = venueFootprint.max.x;
                    measured.actualInnerFace = sign > 0 ? plot.min.x : plot.max.x;
                    measured.serviceGap = sign > 0 ? plot.min.x - venueFootprint.max.x : venueFootprint.min.x - plot.max.x;
                    Check(Mathf.Abs(plot.size.x - (sign > 0 ? 21f : 22.5f)) < .025f,
                        building.BuildingId + " uses the full-size occupied infill width on its actual street plot.");
                    Check(Mathf.Abs(measured.serviceGap - (sign > 0 ? 1.5f : 0f)) < .025f,
                        building.BuildingId + (sign > 0 ? " retains the measured 1.5m service alley beside the actual venue." : " meets the actual venue footprint at the shared party-wall line."));
                    if (sign < 0)
                    {
                        Bounds actualWall = venue.GetComponentsInChildren<Transform>(true).Where(item => item.name == "West_Wall" || item.name == "East_Wall")
                            .Select(item => GeometryBounds(item, district.transform)).OrderBy(item => item.center.x).First();
                        Check(Mathf.Abs(actualWall.center.x - plot.max.x) < .025f,
                            building.BuildingId + " attached frontage coincides with the actual preserved venue side-wall centre line.");
                    }
                }
            }
            Check(report.streetFrontages.Count == 18,
                "All eighteen east/west district tower frontages receive independent occupied-slab and physical-curb measurements.");
            Check(report.streetFrontages.Count(item => !item.specialPreservedVenue) == 12
                && report.streetFrontages.Count(item => !item.specialPreservedVenue && item.side == "east") == 6,
                "All six ordinary districts retain both real occupied frontage towers and one practical right-side service alley.");
        }

        private static IEnumerator VerifyHomeEnclosure(SlopCityBuildingInterior[] buildings, FloorRoutes streets)
        {
            if (HomeFacadeTriangles.Count == 0)
            {
                string snapshotPath = SessionState.GetString("SlopCity.InteriorSmoke.HomeTriangles", "");
                Check(File.Exists(snapshotPath), "Actual prepared home facade triangle evidence survives Play Mode domain reload.");
                HomeTriangleSnapshot snapshot = JsonUtility.FromJson<HomeTriangleSnapshot>(File.ReadAllText(snapshotPath));
                homeFacadeCapturedAtUtc = snapshot.capturedAtUtc;
                foreach (HomeTriangleMesh mesh in snapshot.meshes) HomeFacadeTriangles[mesh.path] = mesh.triangles;
            }
            SlopCityDistrict home = world.GetComponentsInChildren<SlopCityDistrict>(true).Single(item => item.Index == 4);
            SlopCityBuildingInterior[] towers = buildings.Where(item => item.IsTower
                && item.GetComponentInParent<SlopCityDistrict>() == home).OrderBy(item => item.transform.position.x).ToArray();
            Check(towers.Length == 2, "Home enclosure uses its two existing occupied towers.");
            foreach (SlopCityBuildingInterior tower in towers)
            {
                Bounds plot = DistrictFootprint(tower, home);
                Check(Mathf.Abs(plot.size.x - 16.5f) < .025f && Mathf.Abs(plot.size.z - 15f) < .025f,
                    tower.BuildingId + " retains its complete 16.5 by 15 metre occupied footprint.");
                Check(Mathf.Abs(plot.min.z - SlopCityDenseLayout.SouthBuildToLine) < .025f
                    && Mathf.Abs(plot.max.z - 2.8f) < .025f,
                    tower.BuildingId + " has a measured zero-setback south frontage and encloses the actual z=0 home exit.");
                float inner = plot.center.x > 0 ? plot.min.x : -plot.max.x;
                Check(Mathf.Abs(inner - SlopCityDenseLayout.HomeAlleyWall) < .025f,
                    tower.BuildingId + " has its occupied inner wall at the measured home alley line.");
                Vector3 entry = home.transform.InverseTransformPoint(tower.EntranceFeet);
                Check(entry.z < plot.min.z && entry.z > plot.min.z - 2f,
                    tower.BuildingId + " actual entrance follows its new south face.");
                Check(report.entrances.Any(item => item.building == tower.BuildingId && item.supported && item.connected),
                    tower.BuildingId + " new entrance connects to physical public streets.");
                report.homeTowerPlots.Add(plot);
            }

            SlopCityFeaturedBillboard featured = Object.FindFirstObjectByType<SlopCityFeaturedBillboard>();
            Check(featured && featured.Screen && featured.Screen.FeaturedClipKey == "1984drh/7696938454575238430"
                && featured.Screen.FeaturedHoldAtSeconds < 0 && featured.Screen.FeaturedHoldSeconds == 0,
                "Home enclosure preserves the exact continuously looping featured post.");
            Check(Mathf.Abs(featured.transform.position.x - SlopCityDenseLayout.HomeFeaturedMount) < .025f
                && Mathf.Abs(featured.Screen.transform.localScale.x - 5.4f) < .001f
                && Mathf.Abs(featured.Screen.transform.localScale.y - 9.6f) < .001f,
                "The physical featured portrait retains its full-size aspect and relocated home-facing mount.");
            SlopCityBuildingInterior east = towers.Last();
            var dressed = new List<Renderer>(east.GetComponentsInChildren<Renderer>(true));
            Transform armor = world.transform.Find("City_MilitarySciFiArchitecture");
            if (armor)
                foreach (Transform root in armor)
                    if (root.name.StartsWith("MilitarySF_LocalTower_", StringComparison.Ordinal)
                        && Vector3.Distance(root.position, east.transform.position) < .05f)
                        dressed.AddRange(root.GetComponentsInChildren<Renderer>(true));
            Renderer[] shell = dressed.Where(item => item.enabled && item.gameObject.activeInHierarchy).Distinct().ToArray();
            Check(shell.Length > 0, "Featured clearance includes the actual tower facade and attached military dressing.");
            float nearestGap = float.PositiveInfinity;
            foreach (Renderer part in featured.GetComponentsInChildren<Renderer>(true))
            {
                Bounds board = part.bounds;
                foreach (Renderer facade in shell)
                {
                    Bounds face = facade.bounds;
                    if (face.max.y <= board.min.y || face.min.y >= board.max.y
                        || face.max.z <= board.min.z || face.min.z >= board.max.z) continue;
                    float gap = face.min.x - board.max.x;
                    if (gap <= .01f)
                    {
                        string path = Hierarchy(facade.transform);
                        Check(HomeFacadeTriangles.TryGetValue(path, out Vector3[] triangles),
                            "A broad facade-bound rejection must have actual prebatch triangle evidence: " + path);
                        float broadGap = gap;
                        gap = ClippedTriangleClearance(triangles, board, out int overlapping);
                        report.featuredTriangleClearances.Add(new TriangleClearanceReceipt { devicePart = part.name,
                            facade = path, capturedAtUtc = homeFacadeCapturedAtUtc, triangles = triangles.Length / 3,
                            overlappingYZTriangles = overlapping, noActualOverlap = float.IsInfinity(gap),
                            broadBoundsGap = broadGap, clippedTriangleGap = float.IsInfinity(gap) ? 0 : gap });
                    }
                    nearestGap = Mathf.Min(nearestGap, gap);
                    Check(gap > .01f, "Full featured picture/back/support bounds remain outside dressed tower geometry: "
                        + part.name + " / " + facade.name + " clearance=" + gap.ToString("F3") + "m.");
                }
            }
            Check(!float.IsInfinity(nearestGap), "Complete featured bounds overlap a measured occupied facade behind the picture.");
            report.featuredFacadeClearance = nearestGap;
            SlopCityDoor door = Object.FindObjectsByType<SlopCityDoor>(FindObjectsSortMode.None).Single(item => item.name == "Home_Entrance");
            Check(door.Blocker && door.Leaf, "Home exit retains its actual operable physical leaf.");
            Check(featured.GetComponentsInChildren<Renderer>(true).All(item => item.bounds.min.x > door.Blocker.bounds.max.x + 1f),
                "Every featured picture/back/support remains beyond the physical home door's swing area.");
            Check(Supported(new Vector3(3.1f, .025f, 0), out Vector3 start), "Actual inside-home door approach is supported.");
            player.TeleportTo(start);
            camera.transform.LookAt(door.InteractionPoint);
            Vector3 aim = door.InteractionPoint - camera.transform.position;
            RaycastHit[] doorHits = Physics.RaycastAll(camera.transform.position, aim.normalized, 2.4f, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => Relevant(hit.collider)).OrderBy(hit => hit.distance).ToArray();
            Check(aim.magnitude <= 2.4f && doorHits.Length > 0 && doorHits[0].transform.GetComponentInParent<SlopCityDoor>() == door,
                "Home door is visible and reachable from the actual adult inside approach.");
            if (!door.IsOpen) Check(door.Toggle(player.transform), "Actual home door accepts its bounded clear-swing opening.");
            yield return Until(() => door.IsOpen && !door.IsMoving, "Actual home door completes its physical opening.");
            Vector3[] hints = { new Vector3(5.2f, .025f, 0), new Vector3(6.1f, .025f, 0),
                new Vector3(6.1f, .025f, -15.5f), new Vector3(31.5f, -.08f, -15.5f) };
            foreach (Vector3 hint in hints)
            {
                Check(Supported(hint, out Vector3 target), "Actual home exit/south alley has supported clear waypoint " + hint + ".");
                Check(ClearSegment(player.transform.position, target), "Actual home exit/south alley capsule sweep clears " + hint + ".");
                yield return WalkTo(target);
                Check(Vector3.Distance(player.transform.position, target) < .15f,
                    "Adult capsule physically traverses the home doorway/south-alley route to " + target + ".");
                report.homeExitWalk.Add(player.transform.position);
                if (hint.x == 5.2f)
                {
                    camera.transform.LookAt(featured.transform.position);
                    Capture("home-door-enclosure", "Home Block", 0);
                }
            }
            foreach (SlopCityBuildingInterior tower in towers)
            {
                Check(Supported(tower.EntranceFeet, out Vector3 target), tower.BuildingId + " relocated entrance remains capsule-clear.");
                List<Vector3> route = streets.To(target);
                Check(route != null, tower.BuildingId + " relocated entrance has a physical route for actual traversal.");
                player.TeleportTo(route[0]);
                foreach (Vector3 point in route.Skip(1)) yield return WalkTo(point);
                Check(Vector3.Distance(player.transform.position, target) < .15f,
                    tower.BuildingId + " relocated south entrance receives an actual adult-capsule walk.");
            }
            SlopCityInteractable[] fixtures = home.GetComponentsInChildren<SlopCityInteractable>(true)
                .Where(item => (item.name == "Street_Lamp" && item.transform.localPosition.z < 0)
                    || item.name == "Litter_Bin" || item.name == "Billboard_Tuning_Console").ToArray();
            Check(fixtures.Length == 4, "Both moved home lamps, recycling bin and billboard tuning control remain present.");
            foreach (SlopCityInteractable fixture in fixtures)
            {
                bool reachable = false;
                foreach (float radius in new[] { .8f, 1.2f, 1.6f })
                {
                    for (int direction = 0; direction < 8 && !reachable; direction++)
                    {
                        float angle = direction * Mathf.PI * .25f;
                        Vector3 hint = fixture.InteractionPoint + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                        hint.y = .025f;
                        if (!Supported(hint, out Vector3 feet) || streets.To(feet) == null) continue;
                        Vector3 eye = feet + Vector3.up * report.eyeHeight, delta = fixture.InteractionPoint - eye;
                        if (delta.magnitude > 3.4f) continue;
                        RaycastHit[] hits = Physics.RaycastAll(eye, delta.normalized, 3.4f, ~0, QueryTriggerInteraction.Ignore)
                            .Where(hit => Relevant(hit.collider)).OrderBy(hit => hit.distance).ToArray();
                        if (hits.Length == 0 || hits[0].transform.GetComponentInParent<SlopCityInteractable>() != fixture) continue;
                        report.homeFixtureApproaches.Add(new FixtureApproach { building = "Home Block", floor = 0,
                            fixture = fixture.name, feet = feet, interactionPoint = fixture.InteractionPoint, hitDistance = hits[0].distance });
                        reachable = true;
                    }
                    if (reachable) break;
                }
                Check(reachable, "Relocated " + fixture.name + " has a bounded visible practical use point connected to public walking routes.");
            }
            WriteReport();
        }

        private static float ClippedTriangleClearance(Vector3[] triangles, Bounds device, out int overlapping)
        {
            overlapping = 0; float gap = float.PositiveInfinity;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                var polygon = new List<Vector3> { triangles[index], triangles[index + 1], triangles[index + 2] };
                polygon = ClipTrianglePlane(polygon, 1, device.min.y, true);
                polygon = ClipTrianglePlane(polygon, 1, device.max.y, false);
                polygon = ClipTrianglePlane(polygon, 2, device.min.z, true);
                polygon = ClipTrianglePlane(polygon, 2, device.max.z, false);
                if (polygon.Count == 0) continue;
                overlapping++;
                gap = Mathf.Min(gap, polygon.Min(point => point.x) - device.max.x);
            }
            return gap;
        }

        private static List<Vector3> ClipTrianglePlane(List<Vector3> polygon, int axis, float plane, bool keepGreater)
        {
            var clipped = new List<Vector3>();
            if (polygon.Count == 0) return clipped;
            Vector3 previous = polygon[polygon.Count - 1];
            bool previousInside = keepGreater ? previous[axis] >= plane : previous[axis] <= plane;
            foreach (Vector3 point in polygon)
            {
                bool inside = keepGreater ? point[axis] >= plane : point[axis] <= plane;
                if (previousInside != inside)
                {
                    float t = (plane - previous[axis]) / (point[axis] - previous[axis]);
                    clipped.Add(Vector3.LerpUnclamped(previous, point, t));
                }
                if (inside) clipped.Add(point);
                previous = point; previousInside = inside;
            }
            return clipped;
        }

        private static IEnumerator VerifyServiceAlleys(SlopCityBuildingInterior[] buildings, FloorRoutes streets)
        {
            foreach (StreetFrontageReceipt frontage in report.streetFrontages.Where(item => !item.specialPreservedVenue && item.side == "east"))
            {
                SlopCityBuildingInterior building = buildings.Single(item => item.BuildingId == frontage.building);
                SlopCityDistrict district = building.GetComponentInParent<SlopCityDistrict>();
                SlopCityBuildingInterior venue = buildings.Single(item => item.PreservedInterior && item.transform.parent == district.transform);
                Bounds venueFloor = GeometryBounds(venue.transform.Find("Interior_Floor"), district.transform);
                float x = (frontage.actualInnerFace + frontage.venueRightEdge) * .5f;
                Vector3 before = district.transform.TransformPoint(new Vector3(x, .025f, venueFloor.min.z - .8f));
                Vector3 behind = district.transform.TransformPoint(new Vector3(x, .025f, venueFloor.max.z + .6f));
                bool startSupported = Supported(before, out Vector3 start), finishSupported = Supported(behind, out Vector3 finish);
                Check(startSupported && finishSupported,
                    building.BuildingId + " service lane endpoints have actual solid ground and adult capsule clearance.");
                Check(streets.To(start) != null && ClearSegment(start, finish),
                    building.BuildingId + " physical party-wall service lane remains connected and clear between actual walls and projecting hardware.");
                player.TeleportTo(start); yield return WalkTo(finish);
                Check(Vector3.Distance(player.transform.position, finish) < .15f,
                    building.BuildingId + " adult CharacterController traverses the entire practical service lane beside the furnished venue.");
                frontage.alleyWalkFrom = start; frontage.alleyWalkTo = finish;
            }
        }

        private static void CaptureMainAvenue(FloorRoutes streets)
        {
            SlopCityDistrict home = world.GetComponentsInChildren<SlopCityDistrict>(true).FirstOrDefault(item => item.Index == 4);
            if (!home) return;
            // The actual street runs midway between full-size district pavements.
            // These are actual route-connected feet; no signs, lighting or art are relocated.
            foreach (float side in new[] { 1f, -1f })
            foreach (float fraction in new[] { .5f, .25f, .75f, .1f, .9f })
            {
                float along = Mathf.Lerp(SlopCityDenseLayout.PavementSouth, SlopCityDenseLayout.PavementNorth, fraction);
                Vector3 hint = home.transform.TransformPoint(new Vector3(side * SlopCityDenseLayout.ColumnPitch * .5f, .025f, along));
                if (!Supported(hint, out Vector3 feet) || streets.To(feet) == null) continue;
                player.TeleportTo(feet);
                camera.transform.LookAt(feet + home.transform.forward * 25f + Vector3.up * 4f);
                Capture("dense-main-avenue", home.DisplayName, 0);
                Check(true, "Main avenue review uses real supported, route-connected adult-eye feet alongside the existing Japanese street signs and neon.");
                return;
            }
        }

        private static bool StreetWall(Vector3 origin, Vector3 direction, out RaycastHit wall, out SlopCityBuildingInterior owner)
        {
            foreach (RaycastHit hit in Physics.RaycastAll(origin, direction, 14f, ~0, QueryTriggerInteraction.Ignore).OrderBy(hit => hit.distance))
            {
                if (!Relevant(hit.collider) || Vector3.Dot(hit.normal, -direction) < .7f) continue;
                SlopCityBuildingInterior building = hit.collider.GetComponentInParent<SlopCityBuildingInterior>();
                Transform shell = building ? building.transform.Find("Runtime_Hollow_Building_Shell") : null;
                if (!shell || !hit.collider.transform.IsChildOf(shell)) continue;
                wall = hit; owner = building; return true;
            }
            wall = default; owner = null; return false;
        }

        private static void VerifyCrowdPavement()
        {
            SlopCityCrowd crowd = world.GetComponentInChildren<SlopCityCrowd>(true);
            Check(crowd && crowd.LayoutRevalidated, "Dense layout revalidates its actual pedestrian routes after the final physical street geometry.");
            report.crowdPopulation = crowd.PopulationCount; report.crowdNodes = crowd.RevalidatedNodeCount;
            report.crowdEdges = crowd.RevalidatedEdgeCount; report.rejectedCrowdNodes = crowd.RejectedLayoutNodeCount;
            report.crowdSeedSpacing = crowd.LayoutMinimumSeedSpacing;
            Check(report.crowdPopulation == 432 && report.crowdSeedSpacing >= .749f,
                "Replotting preserves the 432 authored pedestrians and at least .75m spacing at their initial route positions.");
            int nodeCount = 0, directedEdges = 0;
            foreach (SlopCityCrowdRoute route in crowd.Routes)
            {
                Check(route != null && route.nodes != null && route.nodes.Length > 1,
                    "Every pedestrian route retains connected public-pavement nodes.");
                nodeCount += route.nodes.Length;
                var reached = new HashSet<int> { 0 }; var queue = new Queue<int>(); queue.Enqueue(0);
                for (int index = 0; index < route.nodes.Length; index++)
                {
                    SlopCityCrowdNode node = route.nodes[index];
                    Check(node != null && node.neighbors != null && node.neighbors.Length > 0,
                        route.label + " pedestrian node " + index + " has a usable next pavement node.");
                    CheckCrowdSample(route, index, index, node.position, node.position, node.position,
                        route.label + " pedestrian node " + index + " has ground and full-size .32m capsule clearance.");
                    foreach (int neighbor in node.neighbors)
                    {
                        Check(neighbor >= 0 && neighbor < route.nodes.Length && neighbor != index,
                            route.label + " pedestrian edge indices remain valid.");
                        SlopCityCrowdNode next = route.nodes[neighbor];
                        Check(next != null && next.neighbors.Contains(index), route.label + " pedestrian pavement links remain reciprocal.");
                        directedEdges++;
                        if (neighbor < index) continue;
                        Vector3 delta = next.position - node.position;
                        Check(delta.sqrMagnitude <= 4.001f && Mathf.Abs(delta.y) <= .201f,
                            route.label + " retained pedestrian edges stay within the original two-metre pavement grid.");
                        int samples = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / .45f));
                        for (int sample = 1; sample < samples; sample++)
                            CheckCrowdSample(route, index, neighbor, node.position, next.position,
                                Vector3.Lerp(node.position, next.position, sample / (float)samples),
                                route.label + " pedestrian edge retains supported capsule-clear intermediate pavement.");
                        Check(!Physics.CapsuleCastAll(node.position + Vector3.up * .34f, node.position + Vector3.up * 1.64f,
                            .32f, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore).Any(hit => Relevant(hit.collider)),
                            route.label + " pedestrian edge does not cross a building, sign or street obstruction.");
                    }
                }
                while (queue.Count > 0)
                    foreach (int neighbor in route.nodes[queue.Dequeue()].neighbors)
                        if (reached.Add(neighbor)) queue.Enqueue(neighbor);
                Check(reached.Count == route.nodes.Length, route.label + " retained pedestrian route is one physically connected pavement component.");
            }
            Check(nodeCount == report.crowdNodes && directedEdges == report.crowdEdges * 2,
                "Crowd revalidation statistics agree with the independently inspected runtime route graph.");
            var seeds = (SlopCityNpcSeed[])typeof(SlopCityCrowd).GetField("population",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(crowd);
            var positions = new List<Vector3>();
            foreach (SlopCityNpcSeed seed in seeds)
            {
                Check(seed.route >= 0 && seed.route < crowd.Routes.Count, "Preserved pedestrian seed references a retained route.");
                SlopCityCrowdNode[] nodes = crowd.Routes[seed.route].nodes;
                Check(seed.node >= 0 && seed.node < nodes.Length && nodes[seed.node].neighbors.Contains(seed.next),
                    "Preserved pedestrian seed starts on pavement with a checked next edge.");
                Vector3 point = nodes[seed.node].position;
                Check(positions.All(previous => Vector3.Distance(previous, point) >= .749f),
                    "Preserved pedestrians have independently measured .75m starting separation.");
                positions.Add(point);
            }
        }

        private static bool CrowdSupported(Vector3 hint)
        {
            if (!Supported(hint, out Vector3 feet) || Mathf.Abs(feet.y - hint.y) > .10f) return false;
            return !Physics.OverlapCapsule(feet + Vector3.up * .34f, feet + Vector3.up * 1.64f, .32f,
                ~0, QueryTriggerInteraction.Ignore).Any(Relevant);
        }

        private static void CheckCrowdSample(SlopCityCrowdRoute route, int fromIndex, int toIndex, Vector3 from, Vector3 to,
            Vector3 hint, string label)
        {
            bool valid = CrowdSupported(hint);
            string diagnosis = "";
            if (!valid)
            {
                diagnosis = CrowdDiagnosis(hint);
                report.crowdFailures.Add(new CrowdFailure { route = route.label, fromIndex = fromIndex, toIndex = toIndex,
                    from = from, to = to, sample = hint, diagnosis = diagnosis });
                player.TeleportTo(from);
                camera.transform.LookAt(hint + Vector3.up * 1.2f);
                Capture("failed-crowd-view", route.label, 0);
                WriteReport();
            }
            Check(valid, label + (!valid ? " from[" + fromIndex + "]=" + from.ToString("F4")
                + " to[" + toIndex + "]=" + to.ToString("F4") + " sample=" + hint.ToString("F4") + "; " + diagnosis : ""));
        }

        private static string CrowdDiagnosis(Vector3 hint)
        {
            RaycastHit[] ground = Physics.RaycastAll(hint + Vector3.up * .5f, Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => Relevant(hit.collider) && hit.normal.y > .7f).OrderBy(hit => hit.distance).ToArray();
            Vector3 feet = ground.Length > 0 ? ground[0].point + Vector3.up * .025f : hint;
            Collider[] blockers = Physics.OverlapCapsule(feet + Vector3.up * .34f, feet + Vector3.up * 1.64f, .32f,
                ~0, QueryTriggerInteraction.Ignore).Where(Relevant).ToArray();
            return "feet=" + feet.ToString("F4") + "; supportDelta=" + (feet.y - hint.y).ToString("F4")
                + "; ground=" + string.Join("; ", ground.Take(4).Select(hit => Hierarchy(hit.collider.transform)
                    + "@" + hit.point.ToString("F4") + " normal=" + hit.normal.ToString("F4")))
                + "; npcBlockers=" + string.Join("; ", blockers.Select(collider => Hierarchy(collider.transform)
                    + " bounds=" + collider.bounds)) + "; playerSupport=" + PointDiagnosis(hint);
        }

        private static void VerifyFacadeMetrics(SlopCityBuildingInterior building)
        {
            SlopCityMetricFacade facade = building.GetComponent<SlopCityMetricFacade>();
            Check(facade && facade.Prepared && facade.OccupiedStories == building.OccupiedFloorCount,
                building.BuildingId + " installs a metric facade with exactly one exterior row per occupied story.");
            Transform panes = building.transform.Find("Metric_Facade/Human_Scale_Window_Panes");
            MeshFilter filter = panes ? panes.GetComponent<MeshFilter>() : null;
            Check(filter && filter.sharedMesh && filter.sharedMesh.isReadable,
                building.BuildingId + " preserves readable actual window vertices before static batching.");
            Matrix4x4 matrix = building.transform.worldToLocalMatrix * panes.localToWorldMatrix;
            Vector3[] vertices = filter.sharedMesh.vertices;
            var rows = new bool[building.OccupiedFloorCount];
            Check(vertices.Length > 0 && vertices.Length % 4 == 0, building.BuildingId + " metric glazing contains actual quads.");
            for (int first = 0; first < vertices.Length; first += 4)
            {
                float low = float.PositiveInfinity, high = float.NegativeInfinity;
                for (int i = 0; i < 4; i++)
                {
                    float y = matrix.MultiplyPoint3x4(vertices[first + i]).y;
                    low = Mathf.Min(low, y); high = Mathf.Max(high, y);
                }
                int floor = Mathf.RoundToInt((low - SlopCityMetricFacade.FloorDatum - SlopCityMetricFacade.WindowSill) / SlopCityMetricFacade.StoryPitch);
                Check(floor >= 0 && floor < rows.Length, building.BuildingId + " glazing row maps to a real occupied story.");
                float datum = building.LocalFloorElevations[floor];
                Check(Mathf.Abs(low - datum - .95f) < .015f && Mathf.Abs(high - datum - 2.65f) < .015f
                    && high < building.LocalFloorElevations[floor + 1] - .16f,
                    building.BuildingId + " actual window sill/head align with floor " + floor + " and remain below its next slab.");
                rows[floor] = true;
            }
            Check(rows.All(found => found), building.BuildingId + " has readable full-height windows on every occupied story.");
            report.facadeWindowQuads += vertices.Length / 4;
        }

        private static void VerifyFloorMetrics(SlopCityBuildingInterior building, int floor, Transform root)
        {
            Vector2 size = building.GetFloorSize(floor);
            string slabName = floor == 0 ? "Permanent_Ground_Slab" : floor == building.FloorCount - 1 ? "Permanent_Roof_Slab" : "Occupied_Floor_Slab";
            Transform source = floor == 0 || floor == building.FloorCount - 1
                ? building.transform.Find("Runtime_Hollow_Building_Shell") : root;
            Transform[] slabs = source.GetComponentsInChildren<Transform>(true).Where(item => item.name == slabName).ToArray();
            Check(slabs.Length > 0, building.BuildingId + " floor " + floor + " has actual metric slab geometry.");
            Bounds footprint = GeometryBounds(slabs[0], building.transform);
            foreach (Transform slab in slabs.Skip(1)) footprint.Encapsulate(GeometryBounds(slab, building.transform));
            Check(Mathf.Abs(footprint.size.x - size.x) < .02f && Mathf.Abs(footprint.size.z - size.y) < .02f
                && Mathf.Abs(footprint.max.y - building.LocalFloorElevations[floor]) < .02f,
                building.BuildingId + " floor " + floor + " actual slab spans its declared footprint at its declared elevation.");
            int beds = 0, doors = 0;
            float shortestBed = float.PositiveInfinity, narrowestDoor = float.PositiveInfinity;
            foreach (SlopCityInteractable device in root.GetComponentsInChildren<SlopCityInteractable>(true))
            {
                if (device.name.IndexOf("bed", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Vector3 dimensions = GeometryBounds(device.transform, building.transform).size;
                float length = Mathf.Max(dimensions.x, dimensions.z);
                Check(length >= 1.999f, building.BuildingId + " floor " + floor + " adult bed " + device.name + " is at least two metres long; actual=" + length);
                beds++; shortestBed = Mathf.Min(shortestBed, length);
            }
            foreach (Transform lintel in root.GetComponentsInChildren<Transform>(true).Where(item => item.name == "Room_Door_Lintel"))
            {
                Bounds aperture = GeometryBounds(lintel, building.transform);
                Vector3 dimensions = aperture.size;
                float width = Mathf.Max(dimensions.x, dimensions.z);
                Check(width >= .999f && aperture.min.y - building.LocalFloorElevations[floor] >= 2.099f,
                    building.BuildingId + " floor " + floor + " room doorway spans at least one metre with adult headroom.");
                doors++; narrowestDoor = Mathf.Min(narrowestDoor, width);
            }
            Transform[] partitions = root.GetComponentsInChildren<Transform>(true).Where(item => item.name == "Room_Door_Partition").ToArray();
            Bounds[] leftWalls = partitions.Where(item => item.localPosition.x < 0).Select(item => GeometryBounds(item, building.transform)).ToArray();
            Bounds[] rightWalls = partitions.Where(item => item.localPosition.x > 0).Select(item => GeometryBounds(item, building.transform)).ToArray();
            float corridorWidth = 0;
            if (floor < building.OccupiedFloorCount)
            {
                Check(leftWalls.Length > 0 && rightWalls.Length > 0,
                    building.BuildingId + " floor " + floor + " has actual walls defining its common hall.");
                corridorWidth = rightWalls.Min(wall => wall.min.x) - leftWalls.Max(wall => wall.max.x);
                Check(corridorWidth >= 1.999f, building.BuildingId + " floor " + floor + " physical common hall is at least two metres clear; actual=" + corridorWidth);
            }
            if (building.Purpose.StartsWith("Residential", StringComparison.Ordinal) && floor > 0
                && floor < building.OccupiedFloorCount && floor % 5 != 0)
                Check(beds > 0, building.BuildingId + " residential floor " + floor + " includes a measured adult bed.");
            if (floor > 0 && floor < building.OccupiedFloorCount)
                Check(doors > 0, building.BuildingId + " occupied upper floor " + floor + " has measurable full-size room doorways.");
            report.floorMetrics.Add(new FloorMetric { building = building.BuildingId, floor = floor, footprint = size,
                adultBeds = beds, roomDoors = doors, shortestBed = beds == 0 ? 0 : shortestBed,
                narrowestDoor = doors == 0 ? 0 : narrowestDoor, corridorWidth = corridorWidth });
        }

        private static Bounds GeometryBounds(Transform item, Transform relativeTo)
        {
            BoxCollider box = item.GetComponent<BoxCollider>(); MeshFilter mesh = item.GetComponent<MeshFilter>();
            if (!box && (!mesh || !mesh.sharedMesh)) throw new InvalidOperationException("Metric geometry missing: " + Hierarchy(item));
            Bounds local = box ? new Bounds(box.center, box.size) : mesh.sharedMesh.bounds;
            Matrix4x4 matrix = relativeTo.worldToLocalMatrix * item.localToWorldMatrix;
            var result = new Bounds(matrix.MultiplyPoint3x4(local.min), Vector3.zero);
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(x == 0 ? local.min.x : local.max.x,
                    y == 0 ? local.min.y : local.max.y, z == 0 ? local.min.z : local.max.z)));
            return result;
        }

        private static IEnumerator VerifyFloor(SlopCityBuildingInterior building, int floor, BuildingReceipt coverage)
        {
            // Routes used for real walking must include the cabin shaft and hall jambs
            // before their first sample; streaming can otherwise introduce them mid-walk.
            if (building.IsTower && floor == 1) building.EnsureElevator();
            Transform root = building.EnsureFloor(floor);
            Check(root && building.IsFloorLoaded(floor), building.BuildingId + " generates declared floor " + floor + ".");
            Physics.SyncTransforms();
            Vector3 landing = building.LandingFeet(floor);
            if (building.PreservedInterior && building.BuildingId.StartsWith("Venue_", StringComparison.Ordinal))
            {
                Transform preservedFloor = building.transform.Find("Interior_Floor");
                Check(preservedFloor && preservedFloor.GetComponent<Collider>(), building.BuildingId + " retains its actual authored interior floor collider.");
                Bounds actualFloor = GeometryBounds(preservedFloor, building.transform);
                Vector3 actualLanding = building.transform.InverseTransformPoint(landing);
                Check(actualLanding.x >= actualFloor.min.x + capsule.radius && actualLanding.x <= actualFloor.max.x - capsule.radius
                    && actualLanding.z >= actualFloor.min.z + capsule.radius && actualLanding.z <= actualFloor.max.z - capsule.radius,
                    building.BuildingId + " preserved circulation anchor remains inside its actual furnished room after the complete venue moves.");
            }
            Check(Supported(landing, out Vector3 feet), building.BuildingId + " floor " + floor + " has a supported capsule-clear landing.");
            // Occupy the generated floor before the first yield. Normal production
            // streaming must retain it while its physical doors animate, rather than
            // correctly evicting the remote floor while this fixture waits elsewhere.
            player.TeleportTo(feet, building.transform.eulerAngles.y);
            world.RefreshStreaming(); Physics.SyncTransforms();
            RequireFloorLoaded(building, floor);
            Check(root, building.BuildingId + " floor " + floor + " remains present after occupied-floor streaming admission.");
            Check(Supported(landing, out feet), building.BuildingId + " floor " + floor + " admitted elevator geometry leaves its landing capsule-clear.");
            if (building.IsTower && floor < building.OccupiedFloorCount) yield return OpenRoomDoors(building, floor, root);
            Physics.SyncTransforms();
            if (building.IsTower) VerifyFloorMetrics(building, floor, root);
            if (building.IsTower && floor < building.OccupiedFloorCount)
            {
                SlopCityInteriorFurnishing contents = root.GetComponent<SlopCityInteriorFurnishing>();
                Check(contents && contents.DetailPieces > 0 && contents.BatchCount + contents.CleanablePropCount > 0 && contents.BatchCount <= 6,
                    building.BuildingId + " floor " + floor + " has individual cleanable props and at most six residual material batches.");
            }
            Transform[] markers = root.GetComponentsInChildren<Transform>(true)
                .Where(marker => marker.name.StartsWith("Room_Circulation_Point", StringComparison.Ordinal)
                    || marker.name == "Marker_Corridor").ToArray();
            Check(markers.Length > 0, building.BuildingId + " floor " + floor + " contains explicit circulation destinations.");
            var route = new FloorRoutes(feet, markers.Select(marker => marker.position).ToArray());
            FloorRoutes refinedRoute = null;
            if (building.IsTower && floor == 0)
            {
                SlopCityDoor entrance = building.transform.GetComponentsInChildren<SlopCityDoor>(true)
                    .Single(door => door.name == "Building_Entrance_Door");
                if (!entrance.IsOpen) yield return OpenEntrance(building, entrance);
                Check(Supported(building.EntranceFeet, out Vector3 entry), building.BuildingId + " street entrance has supported clear feet.");
                var entryRoute = new FloorRoutes(entry, new[] { feet });
                Check(entryRoute.To(feet) != null, building.BuildingId + " has a physical street-doorway-to-elevator-lobby route.");
            }
            foreach (Transform marker in markers)
            {
                Check(Supported(marker.position, out Vector3 supported), building.BuildingId + " floor " + floor + " supports " + marker.name + ".");
                List<Vector3> path = route.To(supported);
                if (path == null)
                {
                    // A coarse grid can skip the usable center of a narrow passage.
                    // Resample only failing floors; capsule size, support and collision rules remain identical.
                    if (refinedRoute == null)
                    {
                        refinedRoute = new FloorRoutes(feet, markers.Select(item => item.position).ToArray(), .15f);
                        report.refinedFloorRoutes++;
                    }
                    path = refinedRoute.To(supported);
                }
                string failure = path == null ? "; nearest reachable=" + refinedRoute.NearestReachable(supported)
                    + "; blockers=" + SegmentDiagnosis(refinedRoute.NearestReachable(supported), supported) : "";
                Check(path != null, building.BuildingId + " floor " + floor + " has a capsule-clear landing-to-room route: " + marker.name + failure);
                // Real capsule traversal supplements static routes in one occupied floor per tower.
                if (floor == 1 && building.IsTower)
                {
                    player.TeleportTo(feet, building.transform.eulerAngles.y);
                    foreach (Vector3 waypoint in path) yield return WalkTo(waypoint, building, floor);
                    Check(Mathf.Abs(player.transform.position.y - supported.y) < .15f,
                        building.BuildingId + " capsule remains on the actual elevated room floor.");
                }
            }
            if (building.IsTower && floor == 1)
            {
                Vector2 dimensions = building.GetFloorSize(floor);
                if (dimensions.x < 11 || dimensions.y < 11)
                    VerifyCompactFixtureApproaches(building, floor, root, markers, feet);
                else
                {
                    SlopCityInteractable[] fixtures = root.GetComponentsInChildren<SlopCityInteractable>(true)
                        .Where(item => item.Kind == SlopCityInteractionKind.Fountain || item.Kind == SlopCityInteractionKind.Vending
                            || item.name.EndsWith("_Toilet", StringComparison.Ordinal)
                            || item.name.IndexOf("bed", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                    Check(fixtures.Any(item => item.Kind == SlopCityInteractionKind.Fountain)
                        && fixtures.Any(item => item.name.EndsWith("_Toilet", StringComparison.Ordinal)),
                        building.BuildingId + " full-size occupied floor provides its actual kitchen and bathroom fixtures.");
                    VerifyFixtureApproaches(building, floor, markers, feet, fixtures, report.fullSizeFixtureApproaches);
                }
            }
            SlopCityInteractable[] devices = root.GetComponentsInChildren<SlopCityInteractable>(true)
                .Where(device => device.Kind != SlopCityInteractionKind.Building && device.Kind != SlopCityInteractionKind.Inspect).ToArray();
            coverage.practicalDevices += devices.Length;
            if (building.IsTower && floor < building.OccupiedFloorCount)
            {
                Check(devices.Length > 0, building.BuildingId + " occupied floor " + floor + " contains practical supported operations.");
                foreach (SlopCityInteractable device in devices)
                    Check(!device.ObjectiveEligible, "Streamed interior device does not invalidate street dispatch when unloaded: " + device.name);
            }
            if (building.IsTower && floor == 1 && devices.Length > 0)
            {
                CaptureHumanFixtures(building, floor, root, markers);
                Vector2 size = building.GetFloorSize(floor);
                string view = size.x < 11 || size.y < 11 ? "compact-studio" : "large-room";
                if (!Captured.Contains(view))
                {
                    Vector3 reviewFeet = markers.First(marker => marker.name.StartsWith("Room_Circulation_Point", StringComparison.Ordinal)).position;
                    player.TeleportTo(reviewFeet);
                    SlopCityInteractable focus = devices.FirstOrDefault(item => item.name != "Public_Water_Refill" && item.name != "Public_Service_Junction");
                    Vector3 target = focus ? focus.InteractionPoint : devices[0].InteractionPoint;
                    camera.transform.LookAt(target);
                    Capture(view, building.BuildingId, floor);
                }
                if (building.Purpose.StartsWith("Residential", StringComparison.Ordinal) && !Captured.Contains("residential-room"))
                {
                    player.TeleportTo(markers.First(marker => marker.name.StartsWith("Room_Circulation_Point", StringComparison.Ordinal)).position);
                    camera.transform.LookAt(devices[0].InteractionPoint);
                    Capture("residential-room", building.BuildingId, floor);
                }
                if (!Captured.Contains(view + "-wide"))
                {
                    Vector3 reviewFeet, focus;
                    if (view == "compact-studio")
                    {
                        reviewFeet = markers.First(marker => marker.name == "Marker_Corridor").position;
                        focus = root.TransformPoint(new Vector3(0, 1.25f, building.ShaftLocalCenter.z - .7f));
                    }
                    else
                    {
                        Transform room = markers.First(marker => marker.name.StartsWith("Room_Circulation_Point", StringComparison.Ordinal));
                        Vector3 local = root.InverseTransformPoint(room.position);
                        // Full-size floor plans define their own circulation anchors; a
                        // hardcoded x=1.45m can now land inside a redesigned partition.
                        reviewFeet = room.position;
                        focus = room.position + building.transform.TransformVector(new Vector3(Mathf.Sign(local.x), 1.25f, 1));
                    }
                    Check(Supported(reviewFeet, out Vector3 reviewSupported), "Wide rendered review begins at a physically clear room entry.");
                    player.TeleportTo(reviewSupported); camera.transform.LookAt(focus);
                    Capture(view + "-wide", building.BuildingId, floor);
                }
                if (building.BuildingId == "MilitarySF_CommunicationsCitadel")
                {
                    Transform shelf = root.Find("EastSouth_Catalogue_Shelf");
                    Check(shelf && SlopCityInteriorVisualCheck.TryFrame(building, floor, root, shelf, capsule,
                        out _, out _), "Citadel rendered archive view has clear adult support and furniture sightline.");
                    SlopCityInteriorVisualCheck.TryFrame(building, floor, root, shelf, capsule, out Vector3 archiveFeet, out Vector3 archiveFocus);
                    player.TeleportTo(archiveFeet); camera.transform.position = archiveFeet + Vector3.up * report.eyeHeight;
                    float previousFov = camera.fieldOfView;
                    try
                    {
                        camera.fieldOfView = 72f; camera.transform.LookAt(archiveFocus);
                        Capture("citadel-room", building.BuildingId, floor);
                    }
                    finally { camera.fieldOfView = previousFov; }
                    Vector3[] approaches = new[] { 3f, 6f, 10f, 16f, 28f }.SelectMany(distance => new[] {
                        building.EntranceFeet - building.transform.forward * distance,
                        building.EntranceFeet + building.transform.right * distance,
                        building.EntranceFeet - building.transform.right * distance }).ToArray();
                    foreach (Vector3 exterior in approaches)
                    {
                        if (!Supported(exterior, out Vector3 supportedExterior)) continue;
                        player.TeleportTo(supportedExterior);
                        float height = building.LocalFloorElevations[building.FloorCount - 1];
                        camera.transform.LookAt(building.transform.position + Vector3.up * Mathf.Min(height * .25f, 40f));
                        Capture("citadel-exterior", building.BuildingId, 0);
                        break;
                    }
                    Check(Captured.Contains("citadel-exterior"), "Citadel exterior review uses a real supported public-street camera position.");
                }
                SlopCityInteractable device = devices.First(item => item.Kind == SlopCityInteractionKind.Fountain && devices.Count(candidate => candidate.name == item.name) == 1);
                string deviceName = device.name;
                device.MarkUsed(120f); device.RecordService("Interior retention probe");
                int services = device.ServiceCount;
                player.TeleportTo(new Vector3(8, .025f, 0));
                building.ReleaseFloor(floor); yield return null;
                Transform reloaded = building.EnsureFloor(floor);
                SlopCityInteractable retained = reloaded.GetComponentsInChildren<SlopCityInteractable>(true)
                    .First(item => item.name == deviceName);
                Check(retained.CooldownRemaining > 110f && retained.ServiceCount == services
                    && retained.ServiceStatus == "Interior retention probe",
                    building.BuildingId + " streamed utility retains cooldown and service state after release/reload.");
            }
            coverage.verifiedFloors++; report.floors++;
        }

        private static IEnumerator OpenRoomDoors(SlopCityBuildingInterior building, int floor, Transform root)
        {
            SlopCityDoor[] doors = root.GetComponentsInChildren<SlopCityDoor>(true);
            Check(doors.Length >= 4, building.BuildingId + " floor " + floor + " has actual operable room doors.");
            var speeds = new Dictionary<SlopCityDoor, float>();
            try
            {
                foreach (SlopCityDoor door in doors)
                {
                    Check(door.Leaf && door.Blocker && door.Blocker.enabled && !door.IsOpen,
                        building.BuildingId + " floor " + floor + " room door begins with a real closed physical leaf.");
                    Vector3 dimensions = GeometryBounds(door.Blocker.transform, building.transform).size;
                    Check(Mathf.Max(dimensions.x, dimensions.z) >= .999f && dimensions.y >= 2.099f,
                        building.BuildingId + " floor " + floor + " operable door has an adult-size leaf.");
                    SerializedObject configuration = new SerializedObject(door);
                    SerializedProperty speed = configuration.FindProperty("degreesPerSecond");
                    speeds.Add(door, speed.floatValue); speed.floatValue = 11000f; configuration.ApplyModifiedPropertiesWithoutUndo();
                    Check(door.Toggle(null), building.BuildingId + " floor " + floor + " accepts opening its real room door.");
                }
                yield return Until(() =>
                {
                    RequireFloorLoaded(building, floor);
                    if (!root || doors.Any(door => !door || !door.Leaf || !door.Blocker))
                        throw new InvalidOperationException(building.BuildingId + " floor " + floor
                            + " lost a physical room door while the fixture occupied its landing; " + StreamingDiagnosis());
                    return doors.All(door => door.IsOpen && !door.IsMoving);
                },
                    building.BuildingId + " floor " + floor + " physical room door leaves settle open.");
                report.roomDoorOpenings += doors.Length;
            }
            finally
            {
                foreach (var entry in speeds)
                {
                    if (!entry.Key) continue;
                    SerializedObject configuration = new SerializedObject(entry.Key);
                    configuration.FindProperty("degreesPerSecond").floatValue = entry.Value;
                    configuration.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static IEnumerator OpenEntrance(SlopCityBuildingInterior building, SlopCityDoor door)
        {
            Vector3 inside = building.transform.TransformPoint(new Vector3(0, building.LocalFloorElevations[0],
                -building.GetFloorSize(0).y / 2 + 2.1f));
            player.TeleportTo(inside);
            Physics.SyncTransforms();
            float speed = new SerializedObject(door).FindProperty("degreesPerSecond").floatValue;
            try
            {
                SerializedObject configuration = new SerializedObject(door);
                configuration.FindProperty("degreesPerSecond").floatValue = 11000f;
                configuration.ApplyModifiedPropertiesWithoutUndo();
                Check(door.Toggle(null), building.BuildingId + " street leaf accepts a real open operation.");
                yield return Until(() => door && door.IsOpen && !door.IsMoving,
                    building.BuildingId + " street leaf settles open before its access route is traversed.");
                Check(door.Blocker.enabled, building.BuildingId + " open street leaf retains physical collision.");
            }
            finally
            {
                if (door)
                {
                    SerializedObject configuration = new SerializedObject(door);
                    configuration.FindProperty("degreesPerSecond").floatValue = speed;
                    configuration.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static void CaptureHumanFixtures(SlopCityBuildingInterior building, int floor, Transform root, Transform[] markers)
        {
            SlopCityInteractable[] fixtures = root.GetComponentsInChildren<SlopCityInteractable>(true);
            string[] labels = { "adult-bed", "full-kitchen", "full-bathroom" };
            string[] suffixes = { "_Bed", "_Kitchen", "_Bathroom" };
            for (int kind = 0; kind < labels.Length; kind++)
            {
                if (Captured.Contains(labels[kind])) continue;
                SlopCityInteractable fixture = kind == 0
                    ? fixtures.FirstOrDefault(item => item.name.IndexOf("bed", StringComparison.OrdinalIgnoreCase) >= 0)
                    : kind == 1 ? fixtures.FirstOrDefault(item => item.name == "EastSouth_Kitchen_Sink")
                        ?? fixtures.FirstOrDefault(item => item.name.EndsWith("_Kitchen_Sink", StringComparison.Ordinal))
                    : fixtures.FirstOrDefault(item => item.name.EndsWith("_Toilet", StringComparison.Ordinal));
                if (!fixture) continue;
                Transform approach = markers.Where(marker => marker.name.EndsWith(suffixes[kind], StringComparison.Ordinal))
                    .OrderBy(marker => (marker.position - fixture.transform.position).sqrMagnitude).FirstOrDefault();
                Check(approach && Supported(approach.position, out _),
                    building.BuildingId + " " + labels[kind] + " rendered review uses its verified fixture approach.");
                Supported(approach.position, out Vector3 feet);
                Vector3 framedFocus = Vector3.zero;
                if (kind == 1)
                {
                    Check(SlopCityInteriorVisualCheck.TryFrame(building, floor, root, fixture.transform, capsule,
                        out feet, out framedFocus), building.BuildingId + " kitchen image uses a supported adult view with unobstructed furniture sightline.");
                }
                player.TeleportTo(feet, building.transform.eulerAngles.y);
                Bounds metric = GeometryBounds(fixture.transform, building.transform);
                Vector3 focus = building.transform.TransformPoint(metric.center);
                if (kind == 1) focus = framedFocus;
                if (kind == 2)
                {
                    string id = fixture.name.Substring(0, fixture.name.Length - "_Toilet".Length);
                    Transform shower = root.Find(id + "_Shower_Tray");
                    if (shower) focus = (shower.position + fixture.transform.position) * .5f;
                    // Frame both separate back-corner fixtures and the full shower
                    // riser, rather than looking down at only the toilet base.
                    focus.y = feet.y + 1.25f;
                }
                float originalFieldOfView = camera.fieldOfView;
                try
                {
                    if (kind == 1 || kind == 2) camera.fieldOfView = 75f;
                    if (kind == 1) camera.transform.position = feet + Vector3.up * report.eyeHeight;
                    camera.transform.LookAt(focus);
                    Capture(labels[kind], building.BuildingId, floor);
                }
                finally { camera.fieldOfView = originalFieldOfView; }
            }
        }

        private static void VerifyCompactFixtureApproaches(SlopCityBuildingInterior building, int floor, Transform root, Transform[] markers, Vector3 landing)
        {
            SlopCityInteractable[] all = root.GetComponentsInChildren<SlopCityInteractable>(true);
            SlopCityInteractable kitchen = all.FirstOrDefault(item => item.Kind == SlopCityInteractionKind.Fountain);
            SlopCityInteractable bathroom = all.FirstOrDefault(item => item.name.EndsWith("_Toilet", StringComparison.Ordinal));
            SlopCityInteractable parts = all.FirstOrDefault(item => item.name == "Reusable_Components");
            Check(kitchen && bathroom && parts, building.BuildingId + " compact occupied floor has kitchen, bathroom and reusable components.");
            VerifyFixtureApproaches(building, floor, markers, landing, new[] { kitchen, bathroom, parts }, report.compactFixtureApproaches);
        }

        private static void VerifyFixtureApproaches(SlopCityBuildingInterior building, int floor, Transform[] markers,
            Vector3 landing, SlopCityInteractable[] fixtures, List<FixtureApproach> receipts)
        {
            Vector3[] approaches = markers.Select(marker => marker.position).Concat(new[] { landing }).ToArray();
            foreach (SlopCityInteractable target in fixtures)
            {
                bool reachable = false;
                var diagnosis = new List<string>();
                foreach (Vector3 hint in approaches)
                {
                    if (!Supported(hint, out Vector3 feet)) continue;
                    Vector3 eye = feet + Vector3.up * 1.62f, delta = target.InteractionPoint - eye;
                    float verticalAngle = Mathf.Atan2(Mathf.Abs(delta.y), new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
                    if (delta.magnitude > 3.4f || verticalAngle > 75f) continue;
                    RaycastHit[] hits = Physics.RaycastAll(eye, delta.normalized, 3.4f, ~0, QueryTriggerInteraction.Ignore)
                        .Where(hit => Relevant(hit.collider)).OrderBy(hit => hit.distance).ToArray();
                    if (hits.Length > 0 && hits[0].transform.GetComponentInParent<SlopCityInteractable>() == target)
                    {
                        reachable = true;
                        receipts.Add(new FixtureApproach { building = building.BuildingId, floor = floor,
                            fixture = target.name, feet = feet, interactionPoint = target.InteractionPoint, hitDistance = hits[0].distance });
                        break;
                    }
                    diagnosis.Add("eye=" + eye + " nearest=" + (hits.Length > 0 ? Hierarchy(hits[0].transform) : "no collider"));
                }
                Check(reachable, building.BuildingId + " " + target.name
                    + " has bounded visible physical interaction from a verified walking approach; " + string.Join("; ", diagnosis));
            }
        }

        private static IEnumerator ExerciseStreamingAdmission(SlopCityBuildingInterior[] buildings)
        {
            bool enabled = world.enabled; world.enabled = false;
            try
            {
                player.TeleportTo(new Vector3(8, .025f, 0));
                foreach (SlopCityBuildingInterior previous in buildings.Take(2))
                {
                    SlopCityElevator empty = previous.EnsureElevator();
                    Check(empty.RequestFloor(empty.FloorCount - 1), "Empty prior-building car accepts a real remote request.");
                    for (int tick = 0; tick < 60 && !empty.IsMoving; tick++) empty.Tick(.1f);
                    Check(empty.IsMoving && !empty.PlayerInside, "Prior-building fixture car is genuinely moving empty.");
                }
                SlopCityBuildingInterior current = buildings[2];
                Transform floor = current.EnsureFloor(1);
                player.TeleportTo(current.LandingFeet(1));
                world.RefreshStreaming();
                Check(current.IsFloorLoaded(1), "Entering a third building keeps its occupied floor despite two previously moving empty cars.");
                Check(world.Buildings.Count(item => item.Elevator && item.Elevator.gameObject.activeInHierarchy)
                    <= SlopCityBuildingWorld.MaximumActiveTowerElevators,
                    "Retiring remote empty cars preserves the declared two-tower admission bound.");
                Transform corridor = floor.GetComponentsInChildren<Transform>(true).First(item => item.name == "Marker_Corridor");
                bool startSupported = Supported(current.LandingFeet(1), out Vector3 start);
                bool finishSupported = Supported(corridor.position, out Vector3 finish);
                Check(startSupported && finishSupported,
                    "Newly admitted occupied floor retains real landing and corridor support.");
                var routes = new FloorRoutes(start, new[] { finish });
                List<Vector3> path = routes.To(finish);
                Check(path != null, "Newly admitted floor retains its physically clear corridor route.");
                foreach (Vector3 point in path) yield return WalkTo(point, current, 1);
                Check(current.IsFloorLoaded(1) && Mathf.Abs(player.transform.position.y - finish.y) < .15f,
                    "Real capsule walks on the retained third-building floor without falling.");
                player.TeleportTo(new Vector3(8, .025f, 0)); world.RefreshStreaming();
                yield return null;
            }
            finally { world.enabled = enabled; }
        }

        private static IEnumerator GuardFloor(IEnumerator routine, SlopCityBuildingInterior building, int floor)
        {
            var work = new Stack<IEnumerator>(); work.Push(routine);
            while (work.Count > 0)
            {
                IEnumerator current = work.Peek();
                bool advanced = false, failed = false;
                try { advanced = current.MoveNext(); }
                catch (Exception error)
                {
                    failed = true;
                    string details = PointDiagnosis(building.LandingFeet(floor)) + "\n" + StreamingDiagnosis();
                    if (building.IsFloorLoaded(floor))
                    {
                        Transform root = building.EnsureFloor(floor);
                        foreach (Transform marker in root.GetComponentsInChildren<Transform>(true).Where(item => item.name.StartsWith("Room_Circulation_Point", StringComparison.Ordinal) || item.name == "Marker_Corridor"))
                            details += "\n" + marker.name + " @" + marker.position + " " + PointDiagnosis(marker.position);
                        player.TeleportTo(building.LandingFeet(floor));
                        camera.transform.LookAt(root.position + Vector3.up);
                        Capture("failed-floor", building.BuildingId, floor);
                    }
                    report.floorFailures.Add(new FloorFailure { building = building.BuildingId, floor = floor, error = error.ToString(), physics = details });
                    report.errors.Add(building.BuildingId + " floor " + floor + ": " + error.Message);
                }
                if (failed) yield break;
                if (!advanced) { work.Pop(); continue; }
                if (current.Current is IEnumerator child) { work.Push(child); continue; }
                yield return current.Current;
            }
        }

        private static IEnumerator ExerciseElevator(SlopCityBuildingInterior building)
        {
            bool streamingEnabled = world.enabled; world.enabled = false;
            try
            {
                building.EnsureElevator();
                SlopCityElevator lift = building.Elevator;
                Check(lift && lift.FloorCount == building.FloorCount, "Real building elevator serves every occupied story and roof.");
                player.TeleportTo(lift.Cabin.position + Vector3.up * .025f);
                Check(capsule.enabled && lift.PlayerInside, "Enabled real player capsule boards the physical cabin.");
                Check(!lift.CanUnload && !lift.Release(), "Streaming cannot retire a stationary occupied car.");
                lift.OpenDoors();
                for (int i = 0; i < 20; i++) lift.Tick(.1f);
                Check(lift.DoorsOpen, "Cabin opens only at its aligned actual landing.");
                player.TeleportTo(building.LandingFeet(0));
                yield return WalkTo(lift.Cabin.position + Vector3.up * .025f);
                Check(lift.PlayerInside, "Real capsule walks across the slab/cabin seam into the open car.");
                yield return WalkTo(building.LandingFeet(0));
                yield return WalkTo(lift.Cabin.position + Vector3.up * .025f);
                // The enabled controller receives one real production USE on a physical floor button.
                SlopCityElevatorButton button = lift.Cabin.GetComponentsInChildren<SlopCityElevatorButton>(true)
                    .First(item => item.Action == SlopCityElevatorButtonKind.Floor && item.FloorIndex == 1);
                Quaternion cameraPose = camera.transform.localRotation;
                camera.transform.LookAt(button.transform.position);
                GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.name = "Interior_Button_Occlusion_Probe";
                blocker.transform.position = Vector3.Lerp(camera.transform.position, button.transform.position, .5f);
                blocker.transform.localScale = Vector3.one * .25f;
                player.RefreshWorldInteractions(); Physics.SyncTransforms();
                player.Sandbox.Interact(camera);
                for (int i = 0; i < 10; i++) lift.Tick(.1f);
                Check(!lift.IsMoving && lift.CurrentFloor == 0, "A real intervening solid blocks production dispatch to a farther cabin button.");
                blocker.SetActive(false); Object.Destroy(blocker);
                camera.transform.localRotation = cameraPose;
                player.RefreshWorldInteractions(); player.enabled = true;
                yield return AimAt(button.transform.position);
                Capture("cabin-controls", building.BuildingId, 0);
                input.RequestInteract(); yield return null; yield return null;
                player.enabled = false;
                for (int i = 0; i < 60 && !lift.IsMoving; i++) lift.Tick(.1f);
                Check(lift.IsMoving && lift.TargetFloor == 1, "Production USE aimed at the cabin's physical floor button starts its requested trip.");
                Check(!lift.CanUnload && !lift.Release(), "Streaming cannot retire a traveling car or strand its rider.");
                Check(!lift.OpenDoors() && lift.DoorOpenProgress < .001f,
                    "Movement interlocks cabin doors and refuses opening in the shaft.");
                player.enabled = true; input.RequestPause();
                yield return Until(() => player.IsPaused, "Production pause input pauses an underway elevator trip.");
                Vector3 pausedCabin = lift.Cabin.position, pausedPlayer = player.transform.position;
                lift.Tick(.25f);
                Check(lift.Cabin.position == pausedCabin && player.transform.position == pausedPlayer,
                    "Pause freezes both real cabin and passenger positions.");
                input.RequestPause(); yield return Until(() => !player.IsPaused, "Production resume input resumes elevator simulation.");
                player.enabled = false;
                yield return Ride(lift, 1, false);
                int middle = Mathf.Max(2, (lift.FloorCount - 1) / 2);
                yield return Ride(lift, middle, true);
                player.TeleportTo(lift.Cabin.TransformPoint(new Vector3(.40f, .025f, .45f)));
                yield return Ride(lift, lift.FloorCount - 1, true);
                // Closed hall doors protect the empty lobby while the car is physically on the roof.
                Vector3 lobby = building.LandingFeet(0);
                Vector3 shaftLobby = lift.transform.TransformPoint(new Vector3(0, lift.FloorElevation(0) + .025f, 0));
                Check(!ClearSegment(lobby, shaftLobby), "Absent cabin leaves a solid interlocked lobby gate across the shaft entrance.");
                yield return Ride(lift, 0, true);
                Vector3 doorway = lift.transform.TransformPoint(new Vector3(0, lift.FloorElevation(0) + .025f, -1.08f));
                player.TeleportTo(doorway); lift.CloseDoors();
                for (int i = 0; i < 40; i++) lift.Tick(.1f);
                Check(lift.DoorsOpen && !lift.IsMoving, "Real capsule in the doorway causes doors to reopen without starting or crushing.");
                Check(!Physics.OverlapCapsule(player.transform.position + Vector3.up * (capsule.radius + .04f),
                    player.transform.position + Vector3.up * (capsule.height - capsule.radius), capsule.radius * .9f,
                    ~0, QueryTriggerInteraction.Ignore).Any(Relevant), "Reopened doorway leaves the player capsule unpenetrated.");
                player.TeleportTo(lobby);
                Vector3 outside = player.transform.position;
                yield return Ride(lift, middle, true, false);
                Check(Vector3.Distance(player.transform.position, outside) < .01f, "An off-car request moves the elevator without moving the landing player.");
                yield return Ride(lift, 0, true, false);
                Check(lift.DoorsOpen && lift.CurrentFloor == 0, "An off-floor hall request brings the same physical car back to the lobby.");
                Check(!lift.RequestFloor(-1) && !lift.RequestFloor(lift.FloorCount), "Unavailable floor indices are rejected without altering the car route.");
                Check(lift.CanUnload && lift.Release(), "Empty stationary car can retire safely after the player exits.");
                Check(!ClearSegment(lobby, shaftLobby), "Retirement closes the physical hall gate instead of exposing an unloaded shaft.");
                player.TeleportTo(building.EntranceFeet);
                camera.transform.LookAt(building.transform.position + Vector3.up * 10f);
                Capture("preserved-facade", building.BuildingId, 0);

                // Metro cars cross the exact production ground/ceiling apertures with the same enabled capsule.
                foreach (SlopCityElevator metro in world.StationLifts)
                {
                    metro.gameObject.SetActive(true);
                    player.TeleportTo(metro.Cabin.position + Vector3.up * .025f);
                    Check(metro.PlayerInside, "Player boards the real subway lift cabin.");
                    yield return Ride(metro, metro.FloorCount - 1, true);
                    yield return Ride(metro, 0, true);
                    Check(metro.CurrentFloor == 0 && metro.DoorsOpen, "Subway lift and player cross the actual ground and station ceiling in both directions.");
                }
            }
            finally { world.enabled = streamingEnabled; }
        }

        private static IEnumerator ExerciseRepeatedStreaming(SlopCityBuildingInterior building)
        {
            player.TeleportTo(new Vector3(8, .025f, 0)); world.RefreshStreaming();
            bool enabled = world.enabled; world.enabled = false;
            try
            {
                building.ReleaseFloor(1); yield return null; yield return null;
                int objects = building.GetComponentsInChildren<Transform>(true).Length, materials = CountMaterials();
                for (int repeat = 0; repeat < 8; repeat++)
                {
                    Transform floor = building.EnsureFloor(1);
                    Check(floor, "Repeated floor activation produces usable actual geometry.");
                    building.ReleaseFloor(1); yield return null; yield return null;
                    Check(building.GetComponentsInChildren<Transform>(true).Length == objects && CountMaterials() == materials,
                        "Repeated floor release restores the same object and shared-material population, cycle " + repeat + ".");
                }
            }
            finally { world.enabled = enabled; }
        }

        private static IEnumerator AimAt(Vector3 point)
        {
            float sensitivity = new SerializedObject(player).FindProperty("mouseSensitivity").floatValue;
            double end = EditorApplication.timeSinceStartup + 10;
            while (Vector3.Dot(camera.transform.forward, (point - camera.transform.position).normalized) < .999f)
            {
                if (EditorApplication.timeSinceStartup > end) throw new TimeoutException("Production look input did not center physical cabin button at " + point + "; eye=" + camera.transform.position + "; forward=" + camera.transform.forward);
                Quaternion aim = Quaternion.LookRotation(point - camera.transform.position);
                float yaw = Mathf.DeltaAngle(player.transform.eulerAngles.y, aim.eulerAngles.y);
                float pitch = Mathf.DeltaAngle(camera.transform.localEulerAngles.x, aim.eulerAngles.x);
                float inputScale = 126f / Mathf.Max(1, Screen.height);
                input.AddTouchLook(new Vector2(yaw, -pitch) / (inputScale * sensitivity));
                yield return null;
            }
            Check(true, "Production look input centers the physical cabin button using actual display-height input scaling.");
        }

        private static IEnumerator Ride(SlopCityElevator lift, int target, bool request, bool passenger = true)
        {
            if (request) Check(lift.RequestFloor(target), "Physical car accepts floor request " + target + ".");
            Vector3 relative = passenger ? lift.Cabin.InverseTransformPoint(player.transform.position) : Vector3.zero;
            float beginning = lift.Cabin.localPosition.y;
            bool intermediate = false; int steps = 0;
            while (lift.CurrentFloor != target || lift.IsMoving || !lift.DoorsOpen)
            {
                if (++steps > 1800) throw new TimeoutException("Physical cabin failed to arrive at floor " + target + " from y=" + beginning + "; cabin=" + lift.Cabin.position + "; passenger=" + player.transform.position);
                Vector3 prior = lift.Cabin.position;
                lift.Tick(.1f); report.elevatorSamples++;
                float delta = Vector3.Distance(lift.Cabin.position, prior);
                Check(delta <= .245f, "Cabin advances through bounded continuous physical motion.");
                if (lift.IsMoving && delta > .001f)
                {
                    intermediate = true;
                    Check(lift.DoorOpenProgress < .001f, "Doors remain closed through each moving cabin sample.");
                    if (passenger)
                    {
                        Check(capsule.enabled && lift.PlayerInside, "Real enabled capsule stays inside its moving cabin.");
                        Check(Vector3.Distance(lift.Cabin.InverseTransformPoint(player.transform.position), relative) < .05f,
                            "Passenger tracks the cabin continuously without an endpoint teleport.");
                        if (Mathf.Abs(lift.Cabin.localPosition.y - beginning) > 1f && !Captured.Contains("mid-ride"))
                            Capture("mid-ride", lift.transform.parent.name, target);
                    }
                }
                if (steps % 40 == 0) yield return null;
            }
            Check(intermediate || Mathf.Abs(beginning - lift.FloorElevation(target)) < .25f,
                "Requested trip has real intermediate cabin positions.");
            Check(Mathf.Abs(lift.Cabin.localPosition.y - lift.FloorElevation(target)) < .01f,
                "Cabin physically aligns with its requested floor before opening.");
        }

        private static int CountObjects() => Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        private static int CountMaterials() => Resources.FindObjectsOfTypeAll<Material>().Length;

        private static string PointDiagnosis(Vector3 hint)
        {
            var ground = Physics.RaycastAll(hint + Vector3.up * .5f, Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => Relevant(hit.collider) && hit.normal.y > .7f).OrderBy(hit => hit.distance).ToArray();
            Vector3 feet = ground.Length > 0 ? ground[0].point + Vector3.up * .025f : hint;
            var solids = Physics.OverlapCapsule(feet + Vector3.up * (capsule.radius + .04f),
                feet + Vector3.up * (capsule.height - capsule.radius), capsule.radius * .94f,
                ~0, QueryTriggerInteraction.Ignore).Where(Relevant);
            return "ground=" + string.Join("; ", ground.Take(3).Select(hit => Hierarchy(hit.collider.transform) + "@" + hit.point))
                + " overlaps=" + string.Join("; ", solids.Select(solid => Hierarchy(solid.transform)));
        }
        private static string SegmentDiagnosis(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from; delta.y = 0;
            if (delta.magnitude < .001f) return PointDiagnosis(to);
            Vector3 origin = new Vector3(from.x, Mathf.Max(from.y, to.y), from.z);
            var hits = Physics.CapsuleCastAll(origin + Vector3.up * (capsule.radius + .09f),
                origin + Vector3.up * (capsule.height - capsule.radius), capsule.radius * .94f,
                delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => Relevant(hit.collider)).OrderBy(hit => hit.distance).Take(6);
            return string.Join("; ", hits.Select(hit => Hierarchy(hit.collider.transform) + "@" + hit.distance));
        }
        private static string Hierarchy(Transform item)
        {
            string path = item.name;
            while (item.parent) { item = item.parent; path = item.name + "/" + path; }
            return path;
        }

        private static void Capture(string label, string building, int floor)
        {
            if (!Captured.Add(label)) return;
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.DefaultHDR);
            var display = RenderTexture.GetTemporary(1280, 720, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render();
                bool previousSRGB = GL.sRGBWrite;
                try { GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear; Graphics.Blit(target, display); }
                finally { GL.sRGBWrite = previousSRGB; }
                RenderTexture.active = display;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports/" + Prefix + "-interior-" + label + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, image.EncodeToPNG());
                report.screenshots.Add(new ScreenshotReceipt { path = path, building = building, floor = floor,
                    cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation });
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(target); RenderTexture.ReleaseTemporary(display); Object.DestroyImmediate(image);
            }
        }

        private sealed class FloorRoutes
        {
            private readonly float step;
            private readonly Vector3 start;
            private readonly Bounds bounds;
            private readonly Dictionary<Vector2Int, Vector3> valid = new Dictionary<Vector2Int, Vector3>();
            private readonly HashSet<Vector2Int> sampled = new HashSet<Vector2Int>();
            private readonly Dictionary<Vector2Int, Vector2Int> previous = new Dictionary<Vector2Int, Vector2Int>();
            private static readonly Vector2Int[] Directions = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
            public int ReachableCount => previous.Count;
            public IEnumerable<Vector3> ReachableNear(Vector3 target, float radius)
            {
                float limit = radius * radius;
                return previous.Keys.Select(cell => valid[cell]).Where(point => (point - target).sqrMagnitude <= limit)
                    .OrderBy(point => (point - target).sqrMagnitude);
            }
            public Vector3 NearestReachable(Vector3 target)
            {
                Vector3 best = start; float distance = float.PositiveInfinity;
                foreach (Vector2Int cell in previous.Keys)
                {
                    float score = (valid[cell] - target).sqrMagnitude;
                    if (score < distance) { distance = score; best = valid[cell]; }
                }
                return best;
            }
            public FloorRoutes(Vector3 start, Vector3[] targets, float step = .45f, float horizontalPadding = 3f)
            {
                this.start = start; this.step = step; bounds = new Bounds(start, Vector3.zero);
                foreach (Vector3 target in targets) bounds.Encapsulate(target);
                bounds.Expand(new Vector3(horizontalPadding, .2f, horizontalPadding));
                var origin = Vector2Int.zero;
                Check(Sample(origin), "Floor route begins on real supported clear floor.");
                var queue = new Queue<Vector2Int>(); queue.Enqueue(origin); previous.Add(origin, origin);
                while (queue.Count > 0)
                {
                    Vector2Int here = queue.Dequeue();
                    foreach (Vector2Int direction in Directions)
                    {
                        Vector2Int next = here + direction;
                        if (previous.ContainsKey(next) || !Sample(next) || !RouteSegment(valid[here], valid[next])) continue;
                        previous.Add(next, here); queue.Enqueue(next);
                    }
                }
            }
            private bool Sample(Vector2Int cell)
            {
                if (sampled.Contains(cell)) return valid.ContainsKey(cell);
                sampled.Add(cell);
                Vector3 point = start + new Vector3(cell.x * step, 0, cell.y * step);
                if (point.x < bounds.min.x || point.x > bounds.max.x || point.z < bounds.min.z || point.z > bounds.max.z) return false;
                if (!Supported(point, out Vector3 feet)) return false;
                valid.Add(cell, feet); return true;
            }
            public List<Vector3> To(Vector3 target)
            {
                Vector2Int cell = new Vector2Int(Mathf.RoundToInt((target.x - start.x) / step), Mathf.RoundToInt((target.z - start.z) / step));
                Vector2Int best = default; float distance = float.PositiveInfinity; bool found = false;
                for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++)
                {
                    Vector2Int candidate = cell + new Vector2Int(x, z);
                    if (!previous.ContainsKey(candidate)) continue;
                    float score = (valid[candidate] - target).sqrMagnitude;
                    if (score < distance && RouteSegment(valid[candidate], target)) { best = candidate; distance = score; found = true; }
                }
                if (!found) return null;
                var path = new List<Vector3>();
                for (Vector2Int cursor = best; cursor != Vector2Int.zero; cursor = previous[cursor]) path.Add(valid[cursor]);
                path.Add(start); path.Reverse(); path.Add(target); return path;
            }
            private static bool RouteSegment(Vector3 from, Vector3 to)
            {
                if (!ClearSegment(from, to)) return false;
                int samples = Mathf.CeilToInt(Vector3.Distance(from, to) / .45f);
                Vector3 previous = from;
                for (int sample = 1; sample < samples; sample++)
                {
                    Vector3 hint = Vector3.Lerp(from, to, sample / (float)samples);
                    if (!Supported(hint, out Vector3 feet) || Mathf.Abs(feet.y - previous.y) > .3f) return false;
                    previous = feet;
                }
                return Mathf.Abs(to.y - previous.y) <= .3f;
            }
        }

        private static IEnumerator Until(Func<bool> condition, string label, double seconds = 10)
        {
            double end = EditorApplication.timeSinceStartup + seconds;
            while (!condition())
            { if (EditorApplication.timeSinceStartup > end) throw new TimeoutException(label); yield return null; }
            Check(true, label);
        }

        private static bool Relevant(Collider collider) => collider && collider.enabled && !collider.isTrigger
            && collider != capsule && !collider.transform.IsChildOf(player.transform);

        private static bool Supported(Vector3 hint, out Vector3 feet)
        {
            feet = hint;
            RaycastHit[] hits = Physics.RaycastAll(hint + Vector3.up * .5f, Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => Relevant(hit.collider) && hit.normal.y > .7f).OrderBy(hit => hit.distance).ToArray();
            if (hits.Length == 0 || Mathf.Abs(hits[0].point.y - hint.y) > .30f) return false;
            feet = hits[0].point + Vector3.up * .025f;
            return !Physics.OverlapCapsule(feet + Vector3.up * (capsule.radius + .04f),
                feet + Vector3.up * (capsule.height - capsule.radius), capsule.radius * .94f,
                ~0, QueryTriggerInteraction.Ignore).Any(Relevant);
        }

        private static bool ClearSegment(Vector3 from, Vector3 to)
        {
            if (Mathf.Abs(from.y - to.y) > .3f) return false;
            Vector3 delta = to - from; delta.y = 0;
            if (delta.magnitude < .001f) return true;
            float y = Mathf.Max(from.y, to.y);
            Vector3 origin = new Vector3(from.x, y, from.z);
            return !Physics.CapsuleCastAll(origin + Vector3.up * (capsule.radius + .09f),
                origin + Vector3.up * (capsule.height - capsule.radius), capsule.radius * .94f,
                delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore).Any(hit => Relevant(hit.collider));
        }

        private static string StreamingDiagnosis() => "active=" + string.Join("; ", world.Buildings.Where(item => item.Elevator)
            .Select(item => item.BuildingId + " floors=" + item.LoadedFloorCount + " carFloor=" + item.Elevator.CurrentFloor
                + " moving=" + item.Elevator.IsMoving + " playerInside=" + item.Elevator.PlayerInside + " unloadable=" + item.Elevator.CanUnload));

        private static void RequireFloorLoaded(SlopCityBuildingInterior owner, int floor)
        {
            if (owner && !owner.IsFloorLoaded(floor))
                throw new InvalidOperationException("Production streaming unloaded the floor under the walking capsule: "
                    + owner.BuildingId + " floor " + floor + " at " + player.transform.position + "; " + StreamingDiagnosis());
        }

        private static IEnumerator WalkTo(Vector3 target, SlopCityBuildingInterior owner = null, int floor = -1)
        {
            int stalled = 0;
            for (int step = 0; Vector3.Distance(player.transform.position, target) > .11f; step++)
            {
                RequireFloorLoaded(owner, floor);
                Vector3 prior = player.transform.position, delta = target - prior; delta.y = 0;
                capsule.Move(Vector3.ClampMagnitude(delta, .12f) + Vector3.down * .02f);
                report.capsuleMoves++;
                float moved = Vector3.Distance(prior, player.transform.position); report.walkedMeters += moved;
                stalled = moved < .002f ? stalled + 1 : 0;
                if (stalled > 20 || step > 500) throw new InvalidOperationException("Real capsule route is obstructed toward " + target + " from " + prior);
                if (step % 8 == 7) { yield return null; RequireFloorLoaded(owner, floor); }
            }
            yield return null;
            RequireFloorLoaded(owner, floor);
        }

        private static void Check(bool passed, string label)
        {
            report.checkCount++;
            if (!passed) throw new InvalidOperationException(label);
            if (report.checks.Count < 300) report.checks.Add(label);
        }

        private static void CaptureLog(string message, string trace, LogType kind)
        {
            if (!SessionState.GetBool(Active, false) || (kind != LogType.Error && kind != LogType.Exception && kind != LogType.Assert)) return;
            if (report == null) report = NewReceipt();
            bool editorSearch = trace.Contains("UnityEditor.Search.SearchDatabase") && !trace.Contains("SlopCity.");
            if (editorSearch) report.editorDiagnostics.Add(message + "\n" + trace);
            else report.runtimeErrors.Add(message + "\n" + trace);
        }

        private static void Finish(Exception error)
        {
            SessionState.SetBool(Active, false); Work.Clear(); started = false;
            if (report == null) report = NewReceipt();
            if (error != null) report.errors.Add(error.ToString());
            report.passed = report.errors.Count == 0 && report.runtimeErrors.Count == 0;
            report.finishedAtUtc = DateTime.UtcNow.ToString("O"); WriteReport();
            Debug.Log("SLOP_CITY_INTERIOR_SMOKE_" + (report.passed ? "PASSED" : "FAILED") + " " + ReportPath);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }

        private static void WriteReport()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }

        [Serializable] private sealed class Receipt
        {
            public bool passed, inventoryComplete;
            public string startedAtUtc, finishedAtUtc, unityVersion, scenePath, isolatedDataPath;
            public string scope = "Isolated graphics-enabled native Editor Play Mode. Every installed building/floor is generated sequentially and checked with measured slab/bed/door dimensions and elevated support/capsule probes. Actual readable facade vertices align every exterior window row with its occupied floor. Tower footprint separation and revalidated crowd pavement are checked independently. All eighteen side frontage plots are measured against physical curbs; all twelve ordinary infill plots align with actual preserved venue walls/floors, and all six right service alleys receive actual adult CharacterController walks with support/capsule sweeps. Preserved venue landings must remain inside their actual authored interior floor. Street connectivity uses a 2m graph with unchanged capsule sweeps, support samples at most .45m apart and fine entrance joins when needed. Three different district street canyons are measured against physical opposing facades and walked with the real adult capsule, with eye-height rendered review. Representative interiors use real CharacterController traversal. Room door opening speed is temporarily accelerated in this isolated fixture, preserving production swept-arc checks and restoring each speed afterward. Elevator ticks are accelerated deterministically, with intermediate cabin/player positions and physical door tests. Network sync disabled only in fixture; physical phones, standalone rendering and long-term process memory are outside this run.";
            public int checkCount, buildings, floors, capsuleMoves, elevatorSamples, peakActiveFloors, initialObjects, finalObjects, initialMaterials, finalMaterials, streetReachableCells, protectedGeometryRenderers, expectedTowerCount, refinedFloorRoutes, occupiedStories, declaredStops, fineEntranceConnections, facadeWindowQuads, roomDoorOpenings, roofJobPairs, rejectedNeighbourRoofPanels, towerPlotPairs, crowdPopulation, crowdNodes, crowdEdges, rejectedCrowdNodes;
            public float walkedMeters, playerHeight, playerRadius, eyeHeight, streetGraphStep, streetGraphPadding, farthestRoofPanelFromArrival, crowdSeedSpacing;
            public Bounds occupiedTowerBounds, supportedGroundBounds;
            public float supportedGroundEnvelopeArea, v0112GroundEnvelopeArea, groundEnvelopeAreaRatioToV0112, totalTowerFootprintArea, layoutColumnPitch, layoutRowPitch;
            public long initialAllocatedBytes, finalAllocatedBytes;
            public List<string> checks = new List<string>(), errors = new List<string>(), runtimeErrors = new List<string>(), editorDiagnostics = new List<string>();
            public List<BuildingReceipt> coverage = new List<BuildingReceipt>();
            public List<ScreenshotReceipt> screenshots = new List<ScreenshotReceipt>();
            public List<EntranceReceipt> entrances = new List<EntranceReceipt>();
            public List<FloorFailure> floorFailures = new List<FloorFailure>();
            public List<string> missingBuildings = new List<string>();
            public List<FixtureApproach> compactFixtureApproaches = new List<FixtureApproach>();
            public List<FixtureApproach> fullSizeFixtureApproaches = new List<FixtureApproach>();
            public List<BuildingMetric> buildingMetrics = new List<BuildingMetric>();
            public List<FloorMetric> floorMetrics = new List<FloorMetric>();
            public List<StreetCanyonReceipt> streetCanyons = new List<StreetCanyonReceipt>();
            public List<StreetFrontageReceipt> streetFrontages = new List<StreetFrontageReceipt>();
            public List<CrowdFailure> crowdFailures = new List<CrowdFailure>();
            public List<Bounds> homeTowerPlots = new List<Bounds>();
            public List<Vector3> homeExitWalk = new List<Vector3>();
            public List<FixtureApproach> homeFixtureApproaches = new List<FixtureApproach>();
            public List<TriangleClearanceReceipt> featuredTriangleClearances = new List<TriangleClearanceReceipt>();
            public float featuredFacadeClearance;
            public string homeEnclosureScope = "Actual measured home tower occupied plots, south fronts and existing dimensions; complete featured renderer picture/back/support bounds checked against facade and attached military renderer bounds. Broad bound rejections require exact world-space triangle snapshots before static batching, clipped to each device part's Y/Z slab, retaining the positive 1 cm clearance requirement. Real hinged home door opening, adult CharacterController doorway-to-south-alley traversal and both relocated tower entrance approaches. Moved lamp/recycling/billboard controls require a bounded unobstructed raycast from supported public-connected feet. This fixture does not claim physical speaker playback or player media runtime verification.";
        }
        [Serializable] private sealed class StreetFrontageReceipt
        {
            public int district;
            public string building, side;
            public bool specialPreservedVenue;
            public float declaredBuildLine, actualOccupiedFace, curbOuterEdge, curbInnerEdge, declaredSidewalkWidth,
                physicalSidewalkWidth, clearSlabToCurbWidth, clearCentreLaneWidth, occupiedFront, declaredSouthBuildLine,
                venueLeftEdge, venueRightEdge, actualInnerFace, serviceGap;
            public Vector3 alleyWalkFrom, alleyWalkTo;
        }
        [Serializable] private sealed class TriangleClearanceReceipt
        {
            public string devicePart, facade, capturedAtUtc;
            public int triangles, overlappingYZTriangles;
            public bool noActualOverlap;
            public float broadBoundsGap, clippedTriangleGap;
        }
        [Serializable] private sealed class HomeTriangleSnapshot
        {
            public string capturedAtUtc;
            public HomeTriangleMesh[] meshes;
        }
        [Serializable] private sealed class HomeTriangleMesh
        {
            public string path;
            public Vector3[] triangles;
        }
        [Serializable] private sealed class CrowdFailure
        {
            public string route, diagnosis;
            public int fromIndex, toIndex;
            public Vector3 from, to, sample;
        }
        [Serializable] private sealed class StreetCanyonReceipt
        {
            public int district;
            public string leftBuilding, rightBuilding;
            public Vector3 feet, walkedTo;
            public float wallToWallWidth;
        }
        [Serializable] private sealed class BuildingMetric
        {
            public string building;
            public Vector2 lowerFootprint, upperFootprint;
            public int occupiedStories;
        }
        [Serializable] private sealed class FloorMetric
        {
            public string building;
            public int floor, adultBeds, roomDoors;
            public Vector2 footprint;
            public float shortestBed, narrowestDoor, corridorWidth;
        }
        [Serializable] private sealed class BuildingReceipt
        {
            public string id;
            public int floors, occupiedStories, verifiedFloors, practicalDevices;
            public bool hasRoofStop;
        }
        [Serializable] private sealed class ScreenshotReceipt
        {
            public string path, building;
            public int floor;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
        }
        [Serializable] private sealed class FloorFailure
        {
            public string building, error, physics;
            public int floor;
        }
        [Serializable] private sealed class FixtureApproach
        {
            public string building, fixture;
            public int floor;
            public Vector3 feet, interactionPoint;
            public float hitDistance;
        }
        [Serializable] private sealed class EntranceReceipt
        {
            public string building, blockingColliders;
            public bool supported, connected, fineConnection;
            public Vector3 hint, feet, nearestReachable;
            public float nearestDistance;
        }
    }
}
#endif
