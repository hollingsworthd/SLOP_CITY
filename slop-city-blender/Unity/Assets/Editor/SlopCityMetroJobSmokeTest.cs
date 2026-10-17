#if UNITY_EDITOR
using System;
using System.Collections;
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
    /// <summary>Real carried job arrival and explicit legacy identity migration, without Start or network.</summary>
    public static class SlopCityMetroJobSmokeTest
    {
        private const string SaveKey = "SlopCity.Sandbox.Progress.v1";
        private static readonly string[] Keys = { SaveKey, SaveKey + ".recovery", "SlopCity.MatrixSearch.Progress.v1", "SlopCity.MatrixSearch.Progress.v1.recovery" };
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static Receipt receipt;
        private static SlopCityFirstPerson player;
        private static CharacterController capsule;
        private static SlopCityMetroWorld metro;
        private static SlopCitySandbox sandbox;
        private static SlopCityInteractable firstDesk, secondDesk;

        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode
                || Application.companyName != "SlopCityValidation-v0116"
                || !Application.persistentDataPath.Contains(Path.DirectorySeparatorChar + "SlopCityValidation-v0116" + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Metro job checks require isolated SlopCityValidation-v0116 batch Edit Mode.");
            receipt = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O") };
            var backups = Keys.ToDictionary(key => key, key => new Backup { existed = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetString(key, "") });
            UnityEngine.Random.State random = UnityEngine.Random.state;
            Application.logMessageReceived += CaptureLog;
            try
            {
                foreach (string key in Keys) PlayerPrefs.DeleteKey(key);
                var scene = EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                DisableMedia();
                player = Object.FindFirstObjectByType<SlopCityFirstPerson>(); Check(player, "Saved city contains its authored player.");
                player.enabled = false; capsule = player.GetComponent<CharacterController>();
                Set(player, "controller", capsule); Set(player, "paused", false); Set(player, "seated", false); Set(player, "externalMovement", false);
                capsule.enabled = true;
                SlopCityWebGeometryBuildProcessor.ProtectRuntimeBuildingGeometry(scene);
                var buildings = GameObject.Find("SlopCity_NineBlocks").GetComponent<SlopCityBuildingWorld>()
                    ?? GameObject.Find("SlopCity_NineBlocks").AddComponent<SlopCityBuildingWorld>();
                Set(buildings, "constrainedBrowser", true);
                Check(SlopCityBuildingWorld.Ensure(player), "Prepared city has real station access."); buildings.enabled = false;
                metro = SlopCityMetroWorld.Ensure(player); Check(metro && metro.Trains.Count == 3, "Actual service owns three configured trains.");
                SlopCityWorldUtilities.Ensure(player.transform); DisableMedia();
                SlopCityInteractable[] desks = Object.FindObjectsByType<SlopCityInteractable>(FindObjectsSortMode.None)
                    .Where(item => item.Kind == SlopCityInteractionKind.Terminal && !item.Destination && item.ObjectiveEligible)
                    .OrderBy(item => Identity(item), StringComparer.Ordinal).ToArray();
                Check(desks.Length >= 2, "Two real city desks can receive the existing parcel job."); firstDesk = desks[0]; secondDesk = desks[1];
                ArrivalIntegration();
                LegacyMigration();
                Check(receipt.runtimeErrors.Count == 0, "No runtime error, exception or assertion was logged.");
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
                if (!receipt.savesRestored) receipt.errors.Add("Four preference key existence/bytes were not restored exactly.");
                UnityEngine.Random.state = random;
                Application.logMessageReceived -= CaptureLog;
            }
            receipt.passed = receipt.errors.Count == 0 && receipt.runtimeErrors.Count == 0;
            receipt.finishedAtUtc = DateTime.UtcNow.ToString("O");
            string path = Argument("-slopCityMetroJobReport") ?? "Builds/Reports/transit-20261016/metro-job-check.json";
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(receipt, true));
            Debug.Log("SLOP_CITY_METRO_JOB_" + (receipt.passed ? "PASSED" : "FAILED") + " " + receipt.checks + " checks");
            EditorApplication.Exit(receipt.passed ? 0 : 1);
        }

        private static void ArrivalIntegration()
        {
            SlopCityMetroTrain train = metro.Trains[0];
            NewSandbox(Seed(0));
            string resources = Resources();
            Place(metro.Stations[0].position + Vector3.up * 30f);
            Until(() => !train.IsMoving && train.CurrentStop == 1, 22f, "An empty physical train reaches stop one.");
            Check(Step == 1 && sandbox.CargoLabel == "SEALED PARCEL" && Resources() == resources, "Empty train arrival cannot complete the carried parcel's travel stage.");
            sandbox.NotifyMetroArrival(metro.Platforms[0], metro.Destinations[1], train);
            metro.NotifyRiderArrival(train, 0, 1);
            Check(Step == 1 && !train.PlayerInside, "A stopped train at the correct destination still requires the real rider inside.");

            NewSandbox(Seed(1)); resources = Resources();
            // Setup at a valid interior position well beyond the obsolete8m distance from the lift marker.
            Place(train.transform.TransformPoint(new Vector3(4.6f, .15f, .35f)), train.transform.eulerAngles.y);
            capsule.Move(Vector3.down * .02f); Physics.SyncTransforms();
            Check(train.PlayerInside && train.RiderTransform == player.transform, "The assigned rider is inside the actual owned carriage.");
            sandbox.NotifyMetroArrival(metro.Platforms[1], metro.Destinations[2], train);
            metro.NotifyRiderArrival(train, 1, 2);
            Check(Step == 1 && Resources() == resources, "Wrong-stop notification cannot advance an assigned journey.");
            sandbox.NotifyMetroArrival(metro.Platforms[1], metro.Destinations[3], train);
            Check(Step == 1, "A wrong destination cannot advance an assigned journey.");
            var foreign = new GameObject("Unregistered metro arrival probe").AddComponent<SlopCityMetroTrain>();
            sandbox.NotifyMetroArrival(metro.Platforms[1], metro.Destinations[2], foreign);
            Check(Step == 1, "A foreign/unconfigured carriage cannot claim an arrival."); Object.DestroyImmediate(foreign.gameObject);
            SlopCityInteractable bench = train.GetComponentsInChildren<SlopCityInteractable>().First(item => item.Kind == SlopCityInteractionKind.Bench);
            string benchIdentity = Identity(bench);
            Check(benchIdentity.EndsWith(":moving", StringComparison.Ordinal), "Moving-car bench identity has no transient world coordinates.");
            Until(() => train.IsMoving, 12f, "The real train departs with its assigned rider.");
            Until(() => !train.IsMoving && train.CurrentStop == 2, 14f, "The real train physically reaches the assigned next stop.");
            Check(train.PlayerInside && Step == 2 && sandbox.ObjectiveTarget == secondDesk && sandbox.CargoLabel == "SEALED PARCEL",
                "Actual ridden arrival advances exactly to delivery while retaining the carried parcel.");
            float distance = Vector3.Distance(player.transform.position, metro.Destinations[2].position); receipt.arrivalDistanceFromLiftMarker = distance;
            Check(distance > 8f && train.transform.InverseTransformPoint(player.transform.position).x > 4f,
                "A valid far-end rider completes travel beyond the former arbitrary8m marker radius.");
            Check(Resources() == resources, "A journey neither spends cargo/resources nor fabricates a job reward.");
            Check(Identity(bench) == benchIdentity, "Bench identity remains stable after carriage movement and turning.");
            metro.NotifyRiderArrival(train, 1, 2);
            Check(Step == 2 && Resources() == resources, "Duplicate arrival notification cannot advance the following delivery stage.");
            ReloadSandbox();
            Check(Step == 2 && sandbox.CurrentJobId == "transit-courier" && sandbox.CargoLabel == "SEALED PARCEL"
                && sandbox.ObjectiveTarget == secondDesk && Resources() == resources, "Reload preserves the arrival's saved stage, exact receiving desk, cargo and legacy resources.");
        }

        private static void LegacyMigration()
        {
            string legacy = metro.Platforms[0].GetComponent<SlopCityMetroPlatform>().LegacyRouteId;
            Check(!string.IsNullOrEmpty(legacy), "The original authored train control retains its exact old route ID.");
            ProgressSeed seed = Seed(0); seed.activeSites[1] = legacy;
            NewSandbox(seed);
            Check(sandbox.CurrentJobId == "transit-courier" && Step == 1 && sandbox.ObjectiveTarget == metro.Platforms[0]
                && sandbox.CargoLabel == "SEALED PARCEL" && sandbox.Credits == seed.credits && sandbox.CompletedContracts == seed.completed
                && sandbox.Salvage == seed.salvage && sandbox.Experience == seed.experience && sandbox.RareFindCount == seed.rareFinds,
                "Old exact train-route save migrates to its fixed platform without losing stage, parcel, totals or legacy rewards.");
            // A real LIFT control is not a RIDE recipe target. Migration must not make it one.
            SlopCityInteractable lift = metro.Stations[0].Find("Subway_SurfaceLift/Platform_Lift_Control").GetComponent<SlopCityInteractable>();
            var travel = new SlopCityJobStepDefinition { action = SlopCityJobAction.Travel, kinds = new[] { SlopCityInteractionKind.Subway } };
            string movedLift = Identity(lift).Split(':')[0] + ":9999,9999,9999";
            Check(Call(sandbox, "RestoreMovedMetroSite", movedLift, travel) == null,
                "An unsupported lift control cannot become a travel-job target through identity migration.");
            // Narrow compatible-helper coverage uses only explicitly synthetic objects; actual
            // controls and seats retain their authored labels, eligibility and physical state.
            IList sites = Get<IList>(sandbox, "sites");
            var helper = new GameObject("Subway_Migration_Helper_Route").AddComponent<SlopCityInteractable>();
            helper.transform.SetParent(metro.Stations[0], false);
            helper.Configure(SlopCityInteractionKind.Subway, "RIDE TO MIGRATION HELPER", true);
            helper.ConfigureDestination(metro.Destinations[1]); sites.Add(helper);
            try
            {
                string oldId = Identity(helper).Split(':')[0] + ":9999,9999,9999";
                Check(ReferenceEquals(Call(sandbox, "RestoreMovedMetroSite", oldId, travel), helper),
                    "Compatible synthetic same-path route helper ignores obsolete coordinates.");
            }
            finally { sites.Remove(helper); Object.DestroyImmediate(helper.gameObject); }
            var helperBench = new GameObject("Synthetic_Migration_Seat").AddComponent<SlopCityInteractable>();
            helperBench.transform.SetParent(metro.Trains[0].transform, false);
            helperBench.Configure(SlopCityInteractionKind.Bench, "Synthetic migration seat", true); sites.Add(helperBench);
            try
            {
                string suffix = Identity(helperBench).Split(':')[0]; int at = suffix.IndexOf("/Subway_Train_", StringComparison.Ordinal);
                string oldId = "SlopCity_NineBlocks/SlopCity_Subway/Subway_Station_0" + suffix.Substring(at) + ":123,456,789";
                var rest = new SlopCityJobStepDefinition { action = SlopCityJobAction.Rest, kinds = new[] { SlopCityInteractionKind.Bench } };
                Check(ReferenceEquals(Call(sandbox, "RestoreMovedMetroSite", oldId, rest), helperBench),
                    "Compatible synthetic seat helper preserves exact train/seat suffix across reparenting.");
            }
            finally { sites.Remove(helperBench); Object.DestroyImmediate(helperBench.gameObject); }
        }

        private static ProgressSeed Seed(int platform) => new ProgressSeed {
            activeSites = new[] { Identity(firstDesk), Identity(metro.Platforms[platform]), Identity(secondDesk) } };
        private static void NewSandbox(ProgressSeed seed)
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(seed)); PlayerPrefs.Save(); ReloadSandbox();
            Check(sandbox.CurrentJobId == "transit-courier" && Step == seed.activeStep && sandbox.CargoLabel == seed.cargo,
                "An existing validated transit assignment restores without regeneration.");
        }
        private static void ReloadSandbox()
        {
            if (sandbox) Object.DestroyImmediate(sandbox);
            sandbox = player.gameObject.AddComponent<SlopCitySandbox>(); Set(player, "sandbox", sandbox);
            sandbox.Initialize(player.transform);
        }
        private static int Step => Get<int>(sandbox, "stepIndex");
        private static string Resources() => sandbox.Credits + "/" + sandbox.Experience + "/" + sandbox.Salvage + "/"
            + sandbox.CompletedContracts + "/" + sandbox.WaterSupply + "/" + sandbox.Recycling + "/" + sandbox.FieldReadings + "/" + sandbox.RareFindCount;
        private static void Until(Func<bool> condition, float timeout, string message)
        {
            float elapsed = 0;
            while (!condition() && elapsed < timeout)
            { foreach (SlopCityMetroTrain train in metro.Trains) train.Advance(.05f); elapsed += .05f; }
            Check(condition(), message + " elapsed=" + elapsed + ", player=" + player.transform.position);
        }
        private static void Place(Vector3 point, float yaw = 0)
        { capsule.enabled = false; player.transform.SetPositionAndRotation(point, Quaternion.Euler(0, yaw, 0)); Set(player, "yaw", yaw); capsule.enabled = true; Physics.SyncTransforms(); }
        private static void DisableMedia()
        {
            foreach (SlopCityTikTokSync sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None)) sync.enabled = false;
            foreach (SlopCityCityMedia media in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None)) media.enabled = false;
        }
        private static string Identity(SlopCityInteractable item) => (string)typeof(SlopCitySandbox).GetMethod("StableId", Hidden).Invoke(null, new object[] { item.transform });
        private static void Set(object item, string name, object value) => item.GetType().GetField(name, Hidden).SetValue(item, value);
        private static T Get<T>(object item, string name) => (T)item.GetType().GetField(name, Hidden).GetValue(item);
        private static object Call(object item, string name, params object[] args) => item.GetType().GetMethod(name, Hidden).Invoke(item, args);
        private static void Check(bool value, string message) { receipt.checks++; if (!value) throw new InvalidOperationException(message); }
        private static void CaptureLog(string message, string stack, LogType kind)
        { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) receipt.runtimeErrors.Add(message); }
        private static string Argument(string key)
        { string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
        [Serializable] private sealed class ProgressSeed
        {
            public int credits = 101, experience = 222, salvage = 7, completed = 9, water = 2, recycling = 1, fieldReadings = 3, rareFinds = 4;
            public string[] discovered = { "rare:existing-metro-fixture" };
            public string activeJobId = "transit-courier", cargo = "SEALED PARCEL";
            public int activeStep = 1, dispatchCursor = 0;
            public string[] activeSites;
        }
        private sealed class Backup { public bool existed; public string value; }
        [Serializable] private sealed class Receipt
        {
            public string startedAtUtc, finishedAtUtc;
            public bool passed, savesRestored;
            public int checks;
            public float arrivalDistanceFromLiftMarker;
            public string scope = "Isolated batch Edit Mode. Actual prepared saved city/carried next-stop event advances an existing parcel assignment, preserves cargo/resources, rejects empty/wrong-stop/foreign notifications and survives reload. Exact old route save migration is real assignment coverage; unsupported real lift is rejected; compatible moved-path/bench helper checks use explicitly synthetic objects without changing actual target eligibility. Capsule setup inside carriage is bounded; no whole-city job walk, Start, Play Mode, network, media decoder, app launch or device performance acceptance. All four preference keys restored and source scene never saved.";
            public List<string> errors = new List<string>(), runtimeErrors = new List<string>();
        }
    }
}
#endif
