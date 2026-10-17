#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Actual saved-scene lifts, automatic carriages and enabled-capsule journeys in isolated Edit Mode.</summary>
    public static class SlopCityMetroSmokeTest
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] Keys = { "SlopCity.Sandbox.Progress.v1", "SlopCity.Sandbox.Progress.v1.recovery", "SlopCity.MatrixSearch.Progress.v1", "SlopCity.MatrixSearch.Progress.v1.recovery" };
        private static readonly RaycastHit[] Hits = new RaycastHit[64];
        private static readonly Collider[] Overlaps = new Collider[128];
        private static Receipt receipt;
        private static SlopCityFirstPerson player;
        private static CharacterController capsule;
        private static SlopCityBuildingWorld buildings;
        private static SlopCityMetroWorld metro;
        private static int arrivals;
        private static float elapsed;
        private static SlopCityMetroTrain riding;

        // -executeMethod SlopCity.Editor.SlopCityMetroSmokeTest.Run; isolated batch Edit Mode, no Start/Play Mode.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode
                || Application.companyName != "SlopCityValidation-v0116"
                || !Application.persistentDataPath.Contains(Path.DirectorySeparatorChar + "SlopCityValidation-v0116" + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Metro checks require isolated SlopCityValidation-v0116 batch Edit Mode.");
            receipt = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            var backups = Keys.ToDictionary(key => key, key => new Backup { existed = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetString(key, "") });
            UnityEngine.Random.State random = UnityEngine.Random.state;
            float scale = Time.timeScale;
            Application.logMessageReceived += CaptureLog;
            try
            {
                foreach (string key in Keys) PlayerPrefs.DeleteKey(key);
                var scene = EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                DisableMedia();
                player = Object.FindFirstObjectByType<SlopCityFirstPerson>(); Check(player, "Saved scene contains the authored first-person player.");
                player.enabled = false;
                capsule = player.GetComponent<CharacterController>(); Check(capsule, "Authored enabled-capsule movement is available.");
                Set(player, "controller", capsule); Set(player, "paused", false); Set(player, "seated", false); Set(player, "externalMovement", false);
                capsule.enabled = true;
                SlopCityWebGeometryBuildProcessor.ProtectRuntimeBuildingGeometry(scene);
                GameObject city = GameObject.Find("SlopCity_NineBlocks"); Check(city, "Actual saved city root exists.");
                buildings = city.GetComponent<SlopCityBuildingWorld>() ?? city.AddComponent<SlopCityBuildingWorld>();
                Set(buildings, "constrainedBrowser", true);
                buildings = SlopCityBuildingWorld.Ensure(player); Check(buildings, "Constrained building installation succeeds without Start."); buildings.enabled = false;
                metro = SlopCityMetroWorld.Ensure(player); Check(metro, "Production metro service initializes.");
                DisableMedia();
                Check(metro.Stations.Count == 6 && metro.Stations.Distinct().Count() == 6, "Exactly six distinct ordered station roots exist.");
                Check(metro.Trains.Count == 3 && metro.Trains.Distinct().Count() == 3, "Exactly three distinct automatic physical trains exist.");
                var stations = metro.Stations.ToArray(); var trains = metro.Trains.ToArray();
                Check(SlopCityMetroWorld.Ensure(player) == metro && metro.Stations.SequenceEqual(stations) && metro.Trains.SequenceEqual(trains), "Repeat Ensure preserves station/train identities and creates no additional pool.");
                foreach (SlopCityMetroTrain train in trains) train.RiderArrived += OnArrival;
                Check(buildings.StationLifts.Count == 6, "Every station has one actual two-floor lift.");
                StationLifts();
                LegacyRoutes();
                TrackClearance();
                ServiceAndRide();
                TrackBrake();
                PauseAndEmptyService();
                Check(receipt.runtimeErrors.Count == 0, "No runtime exception, error or assertion was logged.");
            }
            catch (Exception error) { receipt.errors.Add(error.ToString()); }
            finally
            {
                foreach (SlopCitySandbox writer in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(writer);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                foreach (var pair in backups)
                    if (pair.Value.existed) PlayerPrefs.SetString(pair.Key, pair.Value.value); else PlayerPrefs.DeleteKey(pair.Key);
                PlayerPrefs.Save();
                receipt.savesRestored = backups.All(pair => PlayerPrefs.HasKey(pair.Key) == pair.Value.existed && (!pair.Value.existed || PlayerPrefs.GetString(pair.Key) == pair.Value.value));
                if (!receipt.savesRestored) receipt.errors.Add("Original four preference keys were not restored exactly.");
                UnityEngine.Random.state = random; Time.timeScale = scale;
                Application.logMessageReceived -= CaptureLog;
            }
            receipt.passed = receipt.errors.Count == 0 && receipt.runtimeErrors.Count == 0;
            receipt.finishedAtUtc = DateTime.UtcNow.ToString("O"); receipt.simulatedTrainSeconds = elapsed;
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports/metro-gameplay-check.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(receipt, true));
            Debug.Log("SLOP_CITY_METRO_" + (receipt.passed ? "PASSED" : "FAILED") + " " + receipt.checks + " checks");
            EditorApplication.Exit(receipt.passed ? 0 : 1);
        }

        private static void StationLifts()
        {
            for (int index = 0; index < buildings.StationLifts.Count; index++)
            {
                SlopCityElevator lift = buildings.StationLifts[index];
                Check(lift && lift.FloorCount == 2, "Station " + index + " has platform/street lift floors.");
                Place(lift.Cabin.TransformPoint(new Vector3(0, .035f, 0)), lift.Cabin.rotation.eulerAngles.y);
                for (int direction = 1; direction >= 0; direction--)
                {
                    Check(lift.PlayerInside && capsule.enabled, "Lift " + index + " captures the standing enabled capsule.");
                    Check(lift.RequestFloor(direction), "Lift " + index + " accepts the real floor request " + direction + ".");
                    float timeout = 0;
                    while ((lift.CurrentFloor != direction || !lift.DoorsOpen) && timeout < 14f)
                    {
                        lift.Tick(.05f); Settle(); timeout += .05f;
                        Check(capsule.enabled, "Lift tick preserves enabled capsule collision.");
                    }
                    Check(lift.CurrentFloor == direction && lift.DoorsOpen && !lift.IsMoving && lift.PlayerInside,
                        "Lift " + index + " physically reaches/open floor " + direction + " through the cut ground and ceiling; actual=" + lift.CurrentFloor + ", cabin=" + lift.Cabin.position + ", player=" + player.transform.position);
                    Check(Mathf.Abs(lift.Cabin.InverseTransformPoint(player.transform.position).y) < .15f, "Lift rider remains on its physical floor.");
                    receipt.liftTrips++;
                }
            }
        }

        private static void LegacyRoutes()
        {
            for (int index = 0; index < 6; index++)
            {
                SlopCityInteractable platform = metro.Platforms[index];
                Check(platform && platform.Destination == metro.Destinations[(index + 1) % 6], "Platform " + index + " retains its fixed next-stop route.");
                SlopCityMetroPlatform marker = platform.GetComponent<SlopCityMetroPlatform>();
                Check(marker && marker.World == metro && marker.StopIndex == index && !platform.GetComponentInParent<SlopCityMetroTrain>(), "Route belongs to the fixed platform, independently of moving trains.");
            }
            string first = metro.Platforms[0].GetComponent<SlopCityMetroPlatform>().LegacyRouteId;
            string second = metro.Platforms[3].GetComponent<SlopCityMetroPlatform>().LegacyRouteId;
            Check(!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(second) && first != second, "The two original route IDs survive before station reparenting/rotation.");
        }

        private static void ServiceAndRide()
        {
            riding = metro.Trains[0];
            Check(riding.CurrentStop == 0 && riding.DoorsOpen, "Train zero begins at station zero with boarding doors open.");
            Place(riding.transform.TransformPoint(new Vector3(0, .15f, -3f)), riding.transform.eulerAngles.y);
            WalkTo(riding.transform.TransformPoint(new Vector3(0, .15f, -1.78f)), 2f);
            Check(!riding.PlayerInside, "The capsule straddles the open doorway instead of already being fully inside.");
            Vector3 blockedPose = riding.transform.position;
            Advance(9.5f);
            Check(!riding.IsMoving && Vector3.Distance(blockedPose, riding.transform.position) < .001f && riding.DoorOpenProgress > .8f,
                "A straddling capsule reopens doors and blocks departure.");
            WalkTo(riding.transform.TransformPoint(new Vector3(0, .15f, -.8f)), 2f);
            Check(riding.PlayerInside && capsule.enabled, "Ordinary capsule Move crosses bridge/doorway into the train.");
            float lastYaw = player.transform.eulerAngles.y, turned = 0;
            for (int leg = 0; leg < 6; leg++)
            {
                int origin = riding.CurrentStop, before = arrivals;
                Until(() => riding.IsMoving, 12f, "Doors close and train departs after the capsule clears its doorway.");
                Check(riding.DoorOpenProgress < .001f, "Train departs only with closed leaves.");
                // Demonstrate retained normal walking inside the moving carriage, without replacing movement.
                Vector3 localBefore = riding.transform.InverseTransformPoint(player.transform.position);
                capsule.Move(riding.transform.right * .12f);
                Check((riding.transform.InverseTransformPoint(player.transform.position) - localBefore).x > .07f, "Passenger can walk inside the moving train.");
                Until(() => !riding.IsMoving && riding.CurrentStop == (origin + 1) % 6, 16f, "Physical train completes leg " + origin + " -> " + ((origin + 1) % 6));
                Check(arrivals == before + 1 && riding.PlayerInside && capsule.enabled, "Actual rider arrival fires once with an enabled capsule at the next station.");
                float yaw = player.transform.eulerAngles.y; turned += Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)); lastYaw = yaw;
                Until(() => riding.DoorsOpen, 2f, "Arrival opens the boarding doors.");
                receipt.riddenLegs++;
            }
            Check(turned > 300f, "Both continuous end connectors carry passenger yaw around the loop.");
            int completedArrivals = arrivals;
            WalkTo(riding.transform.TransformPoint(new Vector3(0, .15f, -3f)), 3f);
            Check(!riding.PlayerInside && capsule.enabled, "Ordinary capsule Move alights across the open bridge.");
            Advance(1f); Check(arrivals == completedArrivals, "Alighting creates no additional arrival event.");
            riding = null;
        }

        private static void TrackClearance()
        {
            for (int leg = 0; leg < 6; leg++)
                for (int index = 0; index <= 10; index++)
                {
                    metro.SampleLeg(leg, index / 10f, out Vector3 point, out Quaternion rotation);
                    Place(point + Vector3.up * .14f, rotation.eulerAngles.y);
                    Check(Supported(), "Route leg " + leg + " sample " + index + " has a supported walking capsule.");
                    Check(CapsuleClear(), "Route leg " + leg + " sample " + index + " has actual capsule clearance.");
                    receipt.trackSamples++;
                }
        }

        private static void TrackBrake()
        {
            SlopCityMetroTrain train = metro.Trains[0];
            Place(metro.Stations[0].position + Vector3.up * 30f);
            Until(() => !train.IsMoving && train.DoorsOpen, 20f, "An empty train reaches a boarding stop for the obstruction setup.");
            Until(() => train.IsMoving, 12f, "Empty train departs before the rail obstruction setup.");
            // Setup only: place this same enabled walking capsule on the track ahead, outside the car.
            metro.SampleLeg(train.CurrentStop, Mathf.Min(.9f, train.TripElapsedSeconds / 10f + .12f), out Vector3 point, out Quaternion rotation);
            Place(point + Vector3.up * .12f, rotation.eulerAngles.y);
            int before = arrivals;
            Until(() => train.IsHeld, 4f, "Swept body detection brakes before the track pedestrian.");
            float trip = train.TripElapsedSeconds; Vector3 pose = train.transform.position, pedestrian = player.transform.position;
            Advance(.5f);
            Check(train.IsHeld && Mathf.Abs(train.TripElapsedSeconds - trip) < .001f && Vector3.Distance(pose, train.transform.position) < .001f,
                "A blocked train holds its clock and physical pose instead of tunneling.");
            Check(Vector3.Distance(pedestrian, player.transform.position) < .02f && arrivals == before && capsule.enabled,
                "The track pedestrian is neither carried/crushed nor counted as an arrival.");
            Place(metro.Stations[0].position + Vector3.up * 30f);
            Advance(.3f); Check(!train.IsHeld && train.TripElapsedSeconds > trip, "Moving clear lets the physical train resume.");
        }

        private static void PauseAndEmptyService()
        {
            Place(metro.Stations[0].position + Vector3.up * 30f);
            int before = arrivals;
            var positions = metro.Trains.Select(train => train.transform.position).ToArray();
            var clocks = metro.Trains.Select(train => train.TripElapsedSeconds).ToArray();
            Set(player, "paused", true); Advance(1f); Set(player, "paused", false);
            for (int index = 0; index < 3; index++) Check(positions[index] == metro.Trains[index].transform.position && clocks[index] == metro.Trains[index].TripElapsedSeconds, "Pause freezes train " + index + ".");
            foreach (SlopCityMetroTrain train in metro.Trains) train.Advance(0f);
            Check(positions.SequenceEqual(metro.Trains.Select(train => train.transform.position)), "Zero delta cannot move trains.");
            // Reset only timetable fields/poses for a synchronized service measurement, no rider journey.
            for (int index = 0; index < 3; index++)
            {
                SlopCityMetroTrain train = metro.Trains[index];
                Set(train, "stop", index * 2); Set(train, "moving", false); Set(train, "dwellElapsed", 0f); Set(train, "tripElapsed", 0f); Set(train, "journeyValid", false);
                metro.GetStopPose(index * 2, out Vector3 point, out Quaternion rotation); train.transform.SetPositionAndRotation(point, rotation);
                Invoke(train, "PositionDoors", 1f);
            }
            Physics.SyncTransforms();
            Until(() => metro.Trains[0].IsMoving, 9f, "Fresh synchronized service departs after its eight-second dwell.");
            float wait = metro.Trains.Min(train => train.SecondsUntilStop(0)); receipt.missedDepartureWaitSeconds = wait;
            Check(Mathf.Abs(wait - 28f) < .25f, "A just-missed departure has about28 seconds until the following train; actual=" + wait);
            Advance(109f);
            Check(arrivals == before, "Empty full-circuit service and missed trains never emit a rider arrival.");
        }

        private static void Advance(float seconds)
        {
            float remaining = seconds;
            while (remaining > .00001f)
            {
                float dt = Mathf.Min(.05f, remaining); remaining -= dt;
                foreach (SlopCityMetroTrain train in metro.Trains) train.Advance(dt);
                elapsed += dt;
                if (riding && riding.PlayerInside && riding.IsMoving)
                {
                    Check(capsule.enabled, "Riding sample retains enabled collision.");
                    Vector3 local = riding.transform.InverseTransformPoint(player.transform.position);
                    Check(local.y > -.2f && local.y < .4f && Mathf.Abs(local.x) < 5.7f && Mathf.Abs(local.z) < 1.5f,
                        "Rider remains within supported car bounds; local=" + local);
                    Check(Supported(), "Rider capsule has a real physical floor directly beneath it."); receipt.supportSamples++;
                    Check(CapsuleClear(), "Riding sample has no deep capsule penetration into car/world geometry.");
                }
            }
        }

        private static void Until(Func<bool> condition, float timeout, string message)
        {
            float time = 0;
            while (!condition() && time < timeout) { Advance(.05f); time += .05f; }
            Check(condition(), message + " (time=" + time + ", player=" + player.transform.position + ")");
        }
        private static void WalkTo(Vector3 target, float timeout)
        {
            float time = 0;
            while (time < timeout)
            {
                Vector3 delta = target - player.transform.position; delta.y = 0;
                if (delta.magnitude < .045f) break;
                capsule.Move(Vector3.ClampMagnitude(delta, .055f) + Vector3.down * .025f); Physics.SyncTransforms(); time += .02f;
                Check(capsule.enabled, "Ordinary walking retains capsule collision."); receipt.walkMoves++;
            }
            Vector3 error = target - player.transform.position; error.y = 0;
            Check(error.magnitude < .08f, "Ordinary capsule Move reaches the centerline boarding/alighting target; error=" + error + ", player=" + player.transform.position + ", target=" + target);
        }
        private static void Place(Vector3 position, float yaw = 0)
        {
            capsule.enabled = false; player.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            Set(player, "yaw", yaw); capsule.enabled = true; Physics.SyncTransforms(); receipt.setupPlacements++;
        }
        private static void Settle() { capsule.Move(Vector3.down * .02f); Physics.SyncTransforms(); }
        private static bool Supported()
        {
            int count = Physics.RaycastNonAlloc(player.transform.position + Vector3.up * .12f, Vector3.down, Hits, .5f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) if (Hits[i].collider && Hits[i].collider != capsule && Hits[i].normal.y > .6f) return true;
            return false;
        }
        private static bool CapsuleClear()
        {
            Vector3 center = capsule.transform.TransformPoint(capsule.center);
            float radius = capsule.radius * Mathf.Max(capsule.transform.lossyScale.x, capsule.transform.lossyScale.z);
            float half = Mathf.Max(0, capsule.height * capsule.transform.lossyScale.y * .5f - radius);
            int count = Physics.OverlapCapsuleNonAlloc(center - Vector3.up * half, center + Vector3.up * half,
                radius, Overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == Overlaps.Length) return false;
            for (int index = 0; index < count; index++)
            {
                Collider other = Overlaps[index];
                if (!other || other == capsule) continue;
                if (Physics.ComputePenetration(capsule, capsule.transform.position, capsule.transform.rotation,
                    other, other.transform.position, other.transform.rotation, out _, out float depth) && depth > .04f)
                {
                    receipt.lastPenetration = other.name + " depth=" + depth + " player=" + player.transform.position;
                    return false;
                }
            }
            return true;
        }
        private static void OnArrival(SlopCityMetroTrain train, int origin, int arrival)
        {
            Check(train == riding && train.PlayerInside && arrival == (origin + 1) % 6, "Arrival event identifies its actual carried rider, origin and next stop."); arrivals++; receipt.riderArrivals++;
        }
        private static void DisableMedia()
        {
            foreach (SlopCityTikTokSync item in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None)) item.enabled = false;
            foreach (SlopCityCityMedia item in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None)) item.enabled = false;
            foreach (SlopCityMediaPlayer item in Object.FindObjectsByType<SlopCityMediaPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) item.enabled = false;
        }
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
        private static object Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Hidden).Invoke(owner, args);
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); receipt.checks++; }
        private static void CaptureLog(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) receipt.runtimeErrors.Add(message + "\n" + trace); }
        private sealed class Backup { public bool existed; public string value; }
        [Serializable] private sealed class Receipt
        {
            public bool passed, savesRestored;
            public string startedAtUtc, finishedAtUtc, unityVersion, lastPenetration;
            public int checks, liftTrips, riddenLegs, riderArrivals, supportSamples, trackSamples, walkMoves, setupPlacements;
            public float simulatedTrainSeconds, missedDepartureWaitSeconds;
            public string scope = "Synchronous isolated batch Edit Mode, actual saved scene and production building/metro/lift code. Explicit .05s train/lift ticks are physics fixtures, not frame-performance measurements. No Start, Play Mode, network, media playback, scene save or physical-device acceptance. Bounded setup placements disable the capsule only for fixture setup; boarding/alighting use ordinary enabled CharacterController.Move and rides use the production physical carry. Four save/recovery keys restored. Legacy route identity is checked; full legacy job-save migration and job completion integration are separate checks.";
            public List<string> errors = new List<string>(), runtimeErrors = new List<string>();
        }
    }
}
#endif
