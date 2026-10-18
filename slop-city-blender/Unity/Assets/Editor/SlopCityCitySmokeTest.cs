#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Batch Play Mode traversal through the real CharacterController and door colliders.</summary>
    [InitializeOnLoad]
    public static class SlopCityCitySmokeTest
    {
        private const string ActiveKey = "SlopCity.CitySmoke.Active", StartedKey = "SlopCity.CitySmoke.Started";
        private const string ReportPath = "Builds/Reports/city-playback-check.json";
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt report;
        private static SlopCityFirstPerson player;
        private static SlopCityPlayerInput input;
        private static CharacterController capsule;
        private static double enteredAt;

        static SlopCityCitySmokeTest()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                if (change == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (change == PlayModeStateChange.EnteredEditMode) Finish(new Exception("Play Mode ended early."));
            };
        }

        // Dedicated batch Editor only. Omit -quit; this runner writes its report and exits.
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("City smoke tests require a dedicated batch Editor.");
            SessionState.SetString(StartedKey, DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(ActiveKey, true); report = NewReceipt(); WriteReport();
            try
            {
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static Receipt NewReceipt() => new Receipt
        {
            startedAtUtc = SessionState.GetString(StartedKey, DateTime.UtcNow.ToString("O")),
            unityVersion = Application.unityVersion, scenePath = SlopCitySceneBuilder.ScenePath
        };

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (report == null) report = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(report.startedAtUtc).ToUniversalTime()).TotalSeconds > 240d)
                    throw new TimeoutException("City smoke test exceeded 240 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (Work.Count == 0)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < 1d) return;
                    Work.Push(ExerciseCity());
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
            catch (Exception exception) { Finish(exception); }
        }

        private static IEnumerator ExerciseCity()
        {
            Application.runInBackground = true;
            player = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            Check(player, "Saved scene contains a first-person player.");
            capsule = player.GetComponent<CharacterController>(); input = player.GetComponent<SlopCityPlayerInput>();
            Check(capsule && input, "Player has its real CharacterController and gameplay input.");
            player.SendMessage("OnApplicationFocus", true); player.SendMessage("OnApplicationPause", false);
            player.PlayOrResume(); player.StopMedia();
            input.RequestInteract();
            yield return Until(() => !player.IsSeated && capsule.enabled, "Stand-up interaction was not consumed.");
            Vector3 beforeInput = player.transform.position;
            input.SetTouchMovement(new Vector2(.35f, 0f));
            double inputUntil = EditorApplication.timeSinceStartup + .2d;
            while (EditorApplication.timeSinceStartup < inputUntil) yield return null;
            input.SetTouchMovement(Vector2.zero);
            Check(SlopCityWalkGraph.FlatDistance(beforeInput, player.transform.position) > .015f,
                "Gameplay movement input moves the standing player.");

            SlopCityDoor[] doors = Object.FindObjectsByType<SlopCityDoor>(FindObjectsSortMode.None);
            SlopCityDoor home = doors.SingleOrDefault(door => door.name.Contains("Home_Entrance"));
            Check(home, "Home entrance has a working door component.");
            yield return WalkTo(new Vector3(3f, .1f, 0f));
            yield return AttemptClosedPass(new Vector3(6f, .1f, 0f));
            Check(player.transform.position.x < 4.25f, "Closed home door blocks movement out of the lounge.");
            yield return WalkTo(new Vector3(3f, .1f, 0f));
            yield return OpenThroughInteraction(home);
            yield return WalkTo(new Vector3(6f, .1f, 0f));
            Check(player.transform.position.x > 5.8f, "Open home doorway permits real capsule traversal outside.");
            yield return Close(home);
            yield return AttemptClosedPass(new Vector3(3f, .1f, 0f));
            Check(player.transform.position.x > 4.25f, "Closed home door also blocks entry from the street side.");
            yield return WalkTo(new Vector3(6f, .1f, 0f));
            yield return Open(home);
            yield return WalkTo(new Vector3(3f, .1f, 0f));
            Check(player.transform.position.x < 3.2f, "Open home doorway permits return into the lounge.");
            yield return WalkTo(new Vector3(8f, .1f, 0f));

            Physics.SyncTransforms();
            var graph = new SlopCityWalkGraph(capsule); graph.Build(player.transform.position);
            report.reachableGridCells = graph.ReachableCount;
            Check(graph.ReachableCount > 1000, "Ground-supported capsule graph connects the outdoor streets.");
            SlopCityDistrict[] districts = Object.FindObjectsByType<SlopCityDistrict>(FindObjectsSortMode.None)
                .OrderBy(district => district.Index).ToArray();
            Check(districts.Length == 9, "The city contains all nine blocks.");
            foreach (var district in districts)
            {
                List<Vector3> route = graph.Route(player.transform.position, district.VisitPoint);
                Check(route != null, "Street route reaches " + district.DisplayName + ".");
                foreach (Vector3 waypoint in route) yield return WalkTo(waypoint);
                yield return null;
                Check(player.HasVisited(district.Index), "Walking into " + district.DisplayName + " records its visit.");
                report.visitedDistricts.Add(district.DisplayName);
            }
            Check(player.VisitedBlockCount == 9, "Continuous physical traversal records all nine districts.");

            Transform[] markers = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            Transform[] entrances = markers.Where(marker => marker.name.StartsWith("CityEntrance_")).ToArray();
            Check(entrances.Length >= 6, "Six storefronts expose approach and interior markers.");
            foreach (Transform entrance in entrances)
            {
                string slug = entrance.name.Substring("CityEntrance_".Length);
                Transform interior = markers.SingleOrDefault(marker => marker.name == "CityInterior_" + slug);
                SlopCityDoor door = doors.SingleOrDefault(item => item.name == "CityDoor_" + slug);
                Check(interior && door, "Storefront " + slug + " has matching interior and door.");
                List<Vector3> route = graph.Route(player.transform.position, entrance.position);
                Check(route != null, "Street route reaches storefront " + slug + ".");
                foreach (Vector3 waypoint in route) yield return WalkTo(waypoint);
                yield return AttemptClosedPass(interior.position);
                Check(SlopCityWalkGraph.FlatDistance(player.transform.position, interior.position) > 1f,
                    "Closed storefront door blocks entry: " + slug + ".");
                yield return WalkTo(entrance.position);
                yield return Open(door);
                yield return WalkTo(interior.position);
                Check(SlopCityWalkGraph.FlatDistance(player.transform.position, interior.position) < .15f,
                    "Open storefront door permits entry: " + slug + ".");
                yield return WalkTo(entrance.position);
                yield return Close(door);
                report.traversedStorefronts.Add(slug);
            }
            List<Vector3> homeward = graph.Route(player.transform.position, new Vector3(8f, 0f, 0f));
            Check(homeward != null, "Street network supplies a continuous route back home.");
            foreach (Vector3 waypoint in homeward) yield return WalkTo(waypoint);
            yield return WalkTo(new Vector3(3f, .1f, 0f));
            Check(!player.IsOutside, "The player returns inside the lounge after exploring the city.");
            Check(report.characterControllerMoves > 100 && report.walkedMeters > 200f,
                "City coverage uses physical CharacterController movement rather than teleporting between markers.");
        }

        private static IEnumerator OpenThroughInteraction(SlopCityDoor door)
        {
            Camera camera = Camera.main;
            Check(camera && !door.IsOpen, "First door interaction begins with the closed home door in the player view.");
            float sensitivity = new SerializedObject(player).FindProperty("mouseSensitivity").floatValue;
            Quaternion aim = Quaternion.LookRotation(door.InteractionPoint - camera.transform.position);
            float yaw = Mathf.DeltaAngle(player.transform.eulerAngles.y, aim.eulerAngles.y);
            float pitch = Mathf.DeltaAngle(camera.transform.localEulerAngles.x, aim.eulerAngles.x);
            input.AddTouchLook(new Vector2(yaw, -pitch) / (.14f * sensitivity));
            yield return Until(() => Vector3.Dot(camera.transform.forward,
                (door.InteractionPoint - camera.transform.position).normalized) > .995f,
                "Gameplay look input did not aim at the home door.");
            input.RequestInteract();
            yield return Until(() => door.IsOpen && !door.IsMoving,
                "Gameplay interact input did not open the targeted home door.");
            Check(door.IsOpen, "Aimed gameplay E/interact input selects and opens the home door.");
            Physics.SyncTransforms();
        }

        private static IEnumerator Open(SlopCityDoor door)
        {
            Check(!door.IsOpen && door.Toggle(player.transform), "Door accepts an unobstructed open request: " + door.name);
            yield return Until(() => !door.IsMoving, "Door did not finish opening: " + door.name);
            Check(door.IsOpen, "Door leaf swings open: " + door.name);
            Physics.SyncTransforms();
        }

        private static IEnumerator Close(SlopCityDoor door)
        {
            Check(door.IsOpen && door.Toggle(player.transform), "Door accepts an unobstructed close request: " + door.name);
            yield return Until(() => !door.IsMoving, "Door did not finish closing: " + door.name);
            Check(!door.IsOpen && door.Blocker.enabled, "Door closes with its solid collision restored: " + door.name);
            Physics.SyncTransforms();
        }

        private static IEnumerator AttemptClosedPass(Vector3 target)
        {
            for (int i = 0; i < 45; i++) { MoveToward(target); if (i % 8 == 7) yield return null; }
            yield return null;
        }

        private static IEnumerator WalkTo(Vector3 target)
        {
            int stalled = 0, steps = 0;
            while (SlopCityWalkGraph.FlatDistance(player.transform.position, target) > .10f)
            {
                float prior = SlopCityWalkGraph.FlatDistance(player.transform.position, target);
                MoveToward(target); steps++;
                float now = SlopCityWalkGraph.FlatDistance(player.transform.position, target);
                stalled = now >= prior - .002f ? stalled + 1 : 0;
                if (stalled > 24 || steps > 2000)
                    throw new InvalidOperationException("Capsule route obstructed from " + player.transform.position + " toward " + target);
                if (steps % 8 == 0) yield return null;
            }
            yield return null;
        }

        private static void MoveToward(Vector3 target)
        {
            Vector3 before = player.transform.position, delta = target - before; delta.y = 0f;
            capsule.Move(Vector3.ClampMagnitude(delta, .16f) + Vector3.down * .03f);
            report.walkedMeters += SlopCityWalkGraph.FlatDistance(before, player.transform.position);
            report.characterControllerMoves++;
            if (player.transform.position.y < -.5f) throw new InvalidOperationException("Player fell below the walkable city floor.");
        }

        private static IEnumerator Until(Func<bool> predicate, string failure)
        {
            double limit = EditorApplication.timeSinceStartup + 5d;
            while (!predicate())
            {
                if (EditorApplication.timeSinceStartup > limit) throw new TimeoutException(failure);
                yield return null;
            }
        }

        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            report.checks.Add(description);
        }

        private static void Finish(Exception exception)
        {
            SessionState.SetBool(ActiveKey, false); Work.Clear();
            if (report == null) report = NewReceipt();
            if (exception != null) report.errors.Add(exception.ToString());
            report.passed = report.errors.Count == 0; report.result = report.passed ? "Passed" : "Failed";
            report.finishedAtUtc = DateTime.UtcNow.ToString("O"); WriteReport();
            Debug.Log("SLOP_CITY_CITY_SMOKE_" + report.result.ToUpperInvariant() + " " + ReportPath);
            if (exception != null) Debug.LogException(exception);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }

        private static void WriteReport()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }

        [Serializable]
        private sealed class Receipt
        {
            public bool passed;
            public string result = "Running", startedAtUtc, finishedAtUtc, unityVersion, scenePath;
            public string scope = "Batch Editor Play Mode: actual scene colliders, grounded capsule-grid routes and incremental CharacterController.Move traversal. Stand-up and initial movement use gameplay input. No teleporting after normal couch stand-up; no live UI or standalone visual check.";
            public int reachableGridCells, characterControllerMoves;
            public float walkedMeters;
            public List<string> checks = new List<string>(), visitedDistricts = new List<string>(), traversedStorefronts = new List<string>(), errors = new List<string>();
        }
    }
}
#endif
