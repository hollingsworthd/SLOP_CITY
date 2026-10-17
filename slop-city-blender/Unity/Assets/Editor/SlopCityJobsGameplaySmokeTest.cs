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
    /// <summary>Production jobs, physical targeting and persistent world outcomes without Play Mode or network.</summary>
    public static class SlopCityJobsGameplaySmokeTest
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Save = "SlopCity.Sandbox.Progress.v1";
        private static readonly string[] SaveKeys = { Save, Save + ".recovery", "SlopCity.MatrixSearch.Progress.v1", "SlopCity.MatrixSearch.Progress.v1.recovery" };
        private static readonly Collider[] Overlaps = new Collider[128];
        private static readonly RaycastHit[] Hits = new RaycastHit[128];
        private static readonly List<SlopCityInteractable> FixtureSites = new List<SlopCityInteractable>();
        private static readonly List<Object> Owned = new List<Object>();
        private static Receipt receipt;
        private static GameObject fixture;
        private static Transform user;
        private static Camera camera;
        private static CharacterController capsule;
        private static SlopCitySandbox sandbox;
        private static SlopCityCityMedia media;
        private static int reportRequests;

        // Isolated batch Editor, -quit is supported. No EnterPlaymode/Start/decoder work.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode
                || Application.companyName != "SlopCityValidation-v0116"
                || !Application.persistentDataPath.Contains(Path.DirectorySeparatorChar + "SlopCityValidation-v0116" + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Job checks require isolated SlopCityValidation-v0116 batch Edit Mode.");
            receipt = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
#if SLOP_CITY_WEB_INTERACTION_TEST
            receipt.targeting = "Web";
#else
            receipt.targeting = "Native";
#endif
            var backups = SaveKeys.ToDictionary(key => key, key => new Backup { existed = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetString(key, "") });
            UnityEngine.Random.State random = UnityEngine.Random.state;
            Application.logMessageReceived += CaptureLog;
            try
            {
                Check(SlopCityJobCatalog.All.Length == 40 && SlopCityJobCatalog.All.Select(job => job.id).Distinct().Count() == 40,
                    "Exactly forty stable job identities exist.");
                string[] expectedIds = ("parcel-courier transit-courier addressed-delivery return-courier service-crate diagnostic-dispatch "
                    + "spare-part diagnostic-repair pressure-repair lamp-isolation repair-verification ordered-lamp-service hardware-cleanup pump-service-round "
                    + "recycling-handoff surface-cleanup two-bin-round posted-cleanup district-recycling garden-cleanup three-plant-round irrigation-record "
                    + "water-quality-route garden-seating posted-garden-round signal-diagnostic requested-channel lighting-cycle reception-audit remote-broadcast "
                    + "pump-display-service sample-courier pressure-sample requested-sample-transfer fountain-seating-check utilities-inspection notice-surface-audit "
                    + "landing-maintenance notice-round garden-supply-round").Split(' ');
                Check(new HashSet<string>(expectedIds).SetEquals(SlopCityJobCatalog.All.Select(job => job.id)), "Every original job identity remains available for progression and saves.");
                foreach (SlopCityJobDefinition job in SlopCityJobCatalog.All)
                    Check(job.steps.Any(step => step.action != SlopCityJobAction.ReadNotice && step.action != SlopCityJobAction.Diagnose
                        && step.action != SlopCityJobAction.PressureTest && step.action != SlopCityJobAction.Upload && step.action != SlopCityJobAction.FileReport),
                        job.id + ": the recipe includes practical work beyond reading or recording conditions.");
                Check(SlopCityJobCatalog.ValidateDefinitions().Length == 0, "All recipes pass production catalog validation.");
                SavedCityGeneration();
                SyntheticWorld();
                LegacyAndActiveSave();
                AtomicDispatchAndCorruptCargo();
                ExplicitRecipes();
                FairCycles();
                RejectedOperations();
                WorkStateLifecycle();
                PhysicalWorkVisuals();
                Check(reportRequests == 0, "All gameplay, dispatch contact and completion checks require zero municipal report openings.");
                Check(receipt.runtimeErrors.Count == 0, "No runtime error, exception or assertion was logged.");
            }
            catch (Exception error) { receipt.errors.Add(error.ToString()); }
            finally
            {
                // Remove every save writer before putting original preferences back.
                foreach (SlopCitySandbox writer in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(writer);
                if (media)
                {
                    // Metadata-only states own no decoder or material. Avoid destroying shared fixture art.
                    foreach (object display in (IList)Get(media, "displays")) Set(display, "material", null);
                }
                if (fixture) Object.DestroyImmediate(fixture);
                foreach (Object item in Owned) if (item) Object.DestroyImmediate(item);
                Owned.Clear(); FixtureSites.Clear();
                foreach (var pair in backups)
                {
                    if (pair.Value.existed) PlayerPrefs.SetString(pair.Key, pair.Value.value);
                    else PlayerPrefs.DeleteKey(pair.Key);
                }
                PlayerPrefs.Save();
                receipt.savesRestored = backups.All(pair => PlayerPrefs.HasKey(pair.Key) == pair.Value.existed
                    && (!pair.Value.existed || PlayerPrefs.GetString(pair.Key) == pair.Value.value));
                if (!receipt.savesRestored) receipt.errors.Add("Original preference existence/bytes were not restored.");
                UnityEngine.Random.state = random;
                Application.logMessageReceived -= CaptureLog;
            }
            receipt.passed = receipt.errors.Count == 0 && receipt.runtimeErrors.Count == 0;
            receipt.finishedAtUtc = DateTime.UtcNow.ToString("O");
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports/jobs-gameplay-check.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(receipt, true));
            Debug.Log("SLOP_CITY_JOBS_GAMEPLAY_" + (receipt.passed ? "PASSED" : "FAILED") + " " + receipt.checks + " checks");
            EditorApplication.Exit(receipt.passed ? 0 : 1);
        }

        private static void SavedCityGeneration()
        {
            EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
            foreach (SlopCityTikTokSync sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None)) sync.enabled = false;
            foreach (SlopCityCityMedia cityMedia in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None)) cityMedia.enabled = false;
            SlopCityFirstPerson authoredPlayer = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            Check(authoredPlayer, "The actual saved city contains its authored player.");
            authoredPlayer.enabled = false;
            fixture = new GameObject("Saved_City_Job_Probe");
            BuildUser(authoredPlayer.transform.position);
            SlopCityWorldUtilities.Ensure(user);
            SavedCityMetadataMedia();
            ClearSaves(); NewSandbox();
            UnityEngine.Random.InitState(40501);
            var distinctTargets = new HashSet<string>();
            foreach (SlopCityJobDefinition job in SlopCityJobCatalog.All)
            {
                Check(sandbox.SelectJob(job.id), job.id + ": production selection builds a route in the actual saved city.");
                IList steps = Get<IList>(sandbox, "steps");
                Check(steps.Count == job.steps.Length, job.id + ": saved-city route retains every authored stage.");
                var row = new JobEvidence { id = job.id, title = job.title };
                for (int index = 0; index < steps.Count; index++)
                {
                    SlopCityInteractable site = Get<SlopCityInteractable>(steps[index], "site");
                    SlopCityJobStepDefinition expected = job.steps[index];
                    Check(site && site.isActiveAndEnabled && site.gameObject.activeInHierarchy && SlopCityJobCatalog.Supports(site, expected.action)
                        && Array.IndexOf(expected.kinds, site.Kind) >= 0, job.id + ": saved-city stage " + index + " has a real compatible target.");
                    if (expected.reuseStep >= 0)
                        Check(site == Get<SlopCityInteractable>(steps[expected.reuseStep], "site"), job.id + ": required device identity is retained.");
                    Check(expected.action != SlopCityJobAction.Upload && expected.action != SlopCityJobAction.FileReport,
                        job.id + ": physical work does not require a report-only stage.");
                    bool reachable = Approach(site, false);
                    row.targets.Add(Hierarchy(site.transform));
                    if (reachable) receipt.savedCityApproaches++;
                    else receipt.unprovenApproaches.Add(job.id + " / " + index + " / " + Hierarchy(site.transform) + " / " + site.InteractionPoint);
                    distinctTargets.Add(Hierarchy(site.transform));
                }
                receipt.savedCityJobs.Add(row);
            }
            receipt.savedCityDistinctTargets = distinctTargets.Count;
            Check(receipt.savedCityJobs.Count == 40, "All forty jobs generate against saved-city devices.");
            Check(receipt.unprovenApproaches.Count == 0, "Every generated stage has a sampled clear supported capsule and physical center ray; failures are listed explicitly.");
            Object.DestroyImmediate(sandbox); sandbox = null;
            ReleaseMetadataMaterials();
            Object.DestroyImmediate(fixture); fixture = null;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static void SyntheticWorld()
        {
            fixture = new GameObject("Jobs_Actual_Action_Physics_Fixture");
            Box("Physical ground", new Vector3(20, -.1f, 20), new Vector3(120, .2f, 120));
            BuildUser(new Vector3(-5, .025f, -5));
            for (int i = 0; i < 5; i++) AddSite(SlopCityInteractionKind.Terminal, "Receiving desk " + i);
            for (int i = 0; i < 3; i++) AddSite(SlopCityInteractionKind.Plant, "Plant " + i);
            for (int i = 0; i < 3; i++) AddSite(SlopCityInteractionKind.Light, "Lamp " + i).gameObject.AddComponent<Light>();
            for (int i = 0; i < 3; i++) AddSite(SlopCityInteractionKind.Bin, "Recycling intake " + i);
            AddSite(SlopCityInteractionKind.Collectible, "Parts crate");
            AddSite(SlopCityInteractionKind.Fountain, "Water refill");
            AddSite(SlopCityInteractionKind.Vending, "Supply dispenser");
            AddSite(SlopCityInteractionKind.Hydrant, "Pressure line");
            AddSite(SlopCityInteractionKind.GasPump, "Fuel pump");
            AddSite(SlopCityInteractionKind.Bench, "Public seating");
            AddSite(SlopCityInteractionKind.Inspect, "Civic surface");
            var ride = AddSite(SlopCityInteractionKind.Subway, "RIDE TO NEXT STATION");
            var destination = new GameObject("Real station arrival").transform; destination.SetParent(fixture.transform); destination.position = new Vector3(42, .025f, 42);
            ride.ConfigureDestination(destination);
            var lift = AddSite(SlopCityInteractionKind.Building, "Upper-level route");
            var roof = new GameObject("Real elevated landing").transform; roof.SetParent(fixture.transform); roof.position = new Vector3(42, 5.025f, 35);
            lift.ConfigureDestination(roof);
            Box("Physical elevated landing", new Vector3(42, 4.9f, 35), new Vector3(8, .2f, 8));
            AddSite(SlopCityInteractionKind.Inspect, "Upper maintenance panel", new Vector3(43, 5.75f, 35));
            var screen0 = AddSite(SlopCityInteractionKind.Screen, "Signal display zero");
            var screen1 = AddSite(SlopCityInteractionKind.Screen, "Signal display one");
            MetadataMedia(new[] { screen0, screen1 });
            PrepareWork(); Physics.SyncTransforms();
        }

        private static void BuildUser(Vector3 position)
        {
            user = new GameObject("Physical_job_player").transform; user.SetParent(fixture.transform); user.position = position;
            capsule = user.gameObject.AddComponent<CharacterController>(); capsule.height = 1.72f; capsule.radius = .22f; capsule.center = Vector3.up * .86f;
            camera = new GameObject("Job_center_ray_camera").AddComponent<Camera>(); camera.transform.SetParent(user, false); camera.transform.localPosition = Vector3.up * 1.62f;
        }

        private static void NewSandbox()
        {
            if (sandbox) Object.DestroyImmediate(sandbox);
            sandbox = user.gameObject.AddComponent<SlopCitySandbox>();
            sandbox.TeleportRequested += (position, heading) =>
            {
                capsule.enabled = false; user.SetPositionAndRotation(position, Quaternion.Euler(0, heading, 0)); capsule.enabled = true; Physics.SyncTransforms();
            };
            sandbox.MovementOverrideRequested += held => capsule.enabled = !held;
            sandbox.MunicipalReportRequested += (_, __) => reportRequests++;
            sandbox.Initialize(user, message => receipt.lastFeedback = message);
        }

        private static void LegacyAndActiveSave()
        {
            PlayerPrefs.SetString(Save, "{\"credits\":231,\"experience\":478,\"salvage\":5,\"completed\":12,\"water\":2,\"recycling\":2,\"fieldReadings\":4,\"rareFinds\":1,\"discovered\":[\"rare:jobs-legacy-fixture\"]}");
            PlayerPrefs.DeleteKey(Save + ".recovery"); NewSandbox();
            Check(sandbox.Credits == 231 && sandbox.Experience == 478 && sandbox.Salvage == 5 && sandbox.CompletedContracts == 12
                && sandbox.WaterSupply == 2 && sandbox.Recycling == 2 && sandbox.FieldReadings == 4 && sandbox.RareFindCount == 1,
                "Legacy balances, completed work, supplies and rare discoveries survive schema extension.");
            Check(sandbox.SelectJob("lamp-isolation"), "A real lamp work route is available for persistence checks.");
            SlopCityInteractable serviced = sandbox.ObjectiveTarget;
            CompleteJob(SlopCityJobCatalog.All.First(job => job.id == "lamp-isolation"), false);
            Check(sandbox.SelectJob("parcel-courier"), "Explicit catalog selection selects the real courier recipe.");
            UseCurrent();
            Check(sandbox.CargoLabel == "SEALED PARCEL" && Stage() == 1, "The collection action produces real persistent carried cargo.");
            string id = sandbox.CurrentJobId, title = sandbox.CurrentJobTitle, coverage = sandbox.JobCoverageText;
            SlopCityInteractable recipient = sandbox.ObjectiveTarget;
            int completed = sandbox.CompletedContracts;
            string dispatch = sandbox.DispatchContact();
            Check(!string.IsNullOrWhiteSpace(dispatch) && dispatch.Contains(title), "Dispatch contact returns the actual current job details.");
            Check(sandbox.CurrentJobId == id && sandbox.ObjectiveTarget == recipient && Stage() == 1 && sandbox.CargoLabel == "SEALED PARCEL",
                "Dispatch contact preserves the active stage, recipient and carried parcel.");
            var remembered = serviced.CaptureWorkState(); remembered.condition = 100; remembered.clean = true; remembered.watered = true; remembered.stockUnits = 7;
            serviced.RestoreWorkState(remembered);
            if (serviced.IsOn) serviced.ToggleLights();
            Invoke(sandbox, "Save");
            // A restart observes persisted device state, not leftover object fields.
            serviced.RestoreWorkState(default(SlopCityInteractable.WorkState));
            NewSandbox();
            Check(sandbox.CurrentJobId == id && sandbox.CurrentJobTitle == title && Stage() == 1 && sandbox.ObjectiveTarget == recipient
                && sandbox.CargoLabel == "SEALED PARCEL" && sandbox.JobCoverageText == coverage,
                "Reload restores the exact active recipe, stage, site identities, cargo and coverage.");
            Check(serviced.WorkCondition == 100 && serviced.IsClean && serviced.IsWatered && serviced.SupplyStock == 7 && !serviced.IsOn
                && serviced.GetComponentsInChildren<Light>(true).All(light => !light.enabled),
                "Reload restores concrete device condition, cleaned/watered flags, stock and actual lamp power.");
            UseCurrent();
            Check(sandbox.CompletedContracts == completed + 1 && sandbox.LastJobOutcome == SlopCityJobCatalog.All.First(job => job.id == id).outcome
                && string.IsNullOrEmpty(sandbox.CargoLabel), "The restored parcel can be delivered exactly once.");
            Check(sandbox.Credits == 231 && sandbox.Experience == 478 && sandbox.RareFindCount == 1,
                "Resumed routine work preserves banked credits, XP and rare finds.");
            Check(PlayerPrefs.GetString(Save).Contains("rare:jobs-legacy-fixture"), "New writes preserve the legacy discovery archive.");
            int after = sandbox.CompletedContracts; string next = sandbox.CurrentJobId; NewSandbox();
            Check(sandbox.CompletedContracts == after && sandbox.CurrentJobId == next,
                "Reload after completion retains completion and next dispatch without duplication.");
            Check(!sandbox.SelectJob("missing-job-id") && sandbox.CurrentJobId == next,
                "Unknown explicit selection cannot discard the valid active dispatch.");
            PrepareWork(); Check(sandbox.SelectJob("spare-part"), "A different work route leaves the receiving desk available for free supply use.");
            int stock = recipient.SupplyStock, parts = sandbox.Salvage;
            Check(Approach(recipient, true) && sandbox.Interact(camera), "Normal physical USE collects an available delivered kit from the actual receiving desk.");
            Check(recipient.SupplyStock == stock - 1 && sandbox.Salvage == parts + (recipient.WorkCondition >= 90 ? 2 : 1)
                && sandbox.WaterSupply == 3, "Delivered stock can be spent into actual carried parts and a full water carrier.");
        }

        private static void AtomicDispatchAndCorruptCargo()
        {
            PrepareWork(); Check(sandbox.SelectJob("parcel-courier"), "Atomic dispatch probe selects a real courier route.");
            FieldInfo store = typeof(SlopCitySandbox).GetField("ProgressStoreForTesting", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Check(store != null, "Storage-boundary fault/snapshot hook is available only in the Editor.");
            object original = store.GetValue(null);
            var writes = new List<SaveView>();
            Action<string, string> capture = (key, json) =>
            {
                if (key == Save) writes.Add(JsonUtility.FromJson<SaveView>(json));
                if (original is Action<string, string> previous) previous(key, json);
                else PlayerPrefs.SetString(key, json);
            };
            store.SetValue(null, capture);
            int completed = sandbox.CompletedContracts;
            try
            {
                UseCurrent();
                Check(writes.Count > 0 && writes.All(value => value.activeJobId == "parcel-courier" && value.activeStep == 1
                    && value.cargo == "SEALED PARCEL" && value.completed == completed),
                    "Every successful collection write atomically pairs carried cargo with the advanced stage; no old-step snapshot escapes.");
                writes.Clear(); SlopCityInteractable recipient = sandbox.ObjectiveTarget;
                string recipientId = (string)typeof(SlopCitySandbox).GetMethod("StableId", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { recipient.transform });
                int beforeStock = recipient.SupplyStock;
                UseCurrent();
                Check(writes.Count > 0 && writes.All(value => value.completed == completed + 1 && string.IsNullOrEmpty(value.cargo)
                    && value.jobs != null && value.jobs.Any(job => job.id == "parcel-courier" && job.completed == sandbox.JobCompletions("parcel-courier"))
                    && value.work != null && value.work.Any(work => work.id == recipientId && work.state.stockUnits == beforeStock + 3)),
                    "Every delivery write atomically includes the consumed cargo, completed job/count and actual delivered stock.");
            }
            finally { store.SetValue(null, original); }
            PrepareWork(); Check(sandbox.SelectJob("parcel-courier"), "Corrupt-assignment probe selects a real courier route."); UseCurrent();
            Type progressType = typeof(SlopCitySandbox).GetNestedType("Progress", BindingFlags.NonPublic);
            object damaged = JsonUtility.FromJson(PlayerPrefs.GetString(Save), progressType); Set(damaged, "cargo", null);
            PlayerPrefs.SetString(Save, JsonUtility.ToJson(damaged)); PlayerPrefs.Save();
            completed = sandbox.CompletedContracts; NewSandbox();
            Check(sandbox.HasWaypoint && string.IsNullOrEmpty(sandbox.CargoLabel) && Stage() == 0 && sandbox.CompletedContracts == completed,
                "An assignment missing required carried cargo regenerates usable work without granting completion or stranding a delivery stage.");
            Check(sandbox.SelectJob("parcel-courier"), "A recovered assignment still accepts valid explicit work selection.");
            CompleteJob(SlopCityJobCatalog.All.First(job => job.id == "parcel-courier"), false);
        }

        private static void ExplicitRecipes()
        {
            ClearSaves(); PrepareWork(); NewSandbox(); UnityEngine.Random.InitState(40502);
            foreach (SlopCityJobDefinition job in SlopCityJobCatalog.All)
            {
                AgeWork();
                Check(sandbox.SelectJob(job.id), job.id + ": explicit production selection succeeds.");
                CompleteJob(job, true);
            }
            Check(receipt.executedJobs.Count == 40 && sandbox.CompletedContracts == 40,
                "All forty recipes complete through physical production camera-ray interactions.");
            Check(sandbox.Credits == 0 && sandbox.Experience == 0, "Routine job completion awards no cash or XP.");
        }

        private static void FairCycles()
        {
            ClearSaves(); PrepareWork(); NewSandbox(); UnityEngine.Random.InitState(40503);
            for (int cycle = 0; cycle < 2; cycle++)
            {
                var seen = new HashSet<string>();
                for (int n = 0; n < 40; n++)
                {
                    AgeWork(); sandbox.RerollObjective(); string selected = sandbox.CurrentJobId;
                    Check(seen.Add(selected), "Fair cycle " + cycle + " never repeats a type before all forty have appeared: " + selected);
                    CompleteJob(SlopCityJobCatalog.All.First(job => job.id == selected), false);
                    // Save/reload midway through both cycles must preserve fairness history and active dispatch.
                    if (n == 18) { string pending = sandbox.CurrentJobId, coverage = sandbox.JobCoverageText; NewSandbox(); Check(sandbox.CurrentJobId == pending && sandbox.JobCoverageText == coverage, "Mid-cycle reload preserves active dispatch and fair-rotation coverage."); }
                }
                Check(seen.Count == 40, "Automatic fair cycle " + cycle + " reaches every authored job type.");
                receipt.fairCycles++;
            }
            Check(sandbox.CompletedContracts == 80, "Two automatic fair rotations complete exactly eighty jobs.");
            Check(SlopCityJobCatalog.All.All(job => sandbox.JobCompletions(job.id) == 2), "Persisted per-type completion counts reconcile both full forty-job rotations.");
        }

        private static void CompleteJob(SlopCityJobDefinition job, bool record)
        {
            int beforeJobs = sandbox.CompletedContracts, credits = sandbox.Credits, xp = sandbox.Experience;
            Check(sandbox.CurrentJobId == job.id && sandbox.CurrentJobTitle == job.title && sandbox.CurrentJobOutcome == job.outcome,
                job.id + ": displayed identity/outcome matches the selected recipe.");
            var row = new JobEvidence { id = job.id, title = job.title };
            var selected = new List<SlopCityInteractable>();
            for (int stage = 0; stage < job.steps.Length; stage++)
            {
                SlopCityJobStepDefinition definition = job.steps[stage]; SlopCityInteractable site = sandbox.ObjectiveTarget;
                Check(site && SlopCityJobCatalog.Supports(site, definition.action) && Array.IndexOf(definition.kinds, site.Kind) >= 0,
                    job.id + ": stage " + stage + " targets a compatible physical device.");
                Check(definition.action != SlopCityJobAction.Upload && definition.action != SlopCityJobAction.FileReport,
                    job.id + ": every required stage performs work without filing a generic report.");
                if (definition.reuseStep >= 0) Check(site == selected[definition.reuseStep], job.id + ": explicit device reuse is preserved.");
                selected.Add(site); row.targets.Add(Hierarchy(site.transform));
                Snapshot before = Capture(site); string expectedInstruction = sandbox.ObjectiveAction;
                UseCurrent();
                if (definition.action == SlopCityJobAction.Travel && sandbox.IsTravelling)
                {
                    Check(Stage() == stage && sandbox.CompletedContracts == beforeJobs && !capsule.enabled, "Transit departure holds the stage and physical movement ownership.");
                    sandbox.Tick(2f); Check(sandbox.IsTravelling && Stage() == stage, "Half a real transit interval cannot complete the route.");
                    sandbox.Tick(2.1f);
                    Check(!sandbox.IsTravelling && capsule.enabled, "Physical arrival releases transit movement ownership.");
                }
                VerifyEffect(definition.action, site, before);
                Check(sandbox.Credits == credits && sandbox.Experience == xp, job.id + ": physical work preserves cash and XP.");
                Check(sandbox.CompletedContracts == beforeJobs + (stage + 1 == job.steps.Length ? 1 : 0),
                    job.id + ": only the final successful work stage records one completed job.");
                if (stage + 1 < job.steps.Length) Check(sandbox.CurrentJobId == job.id && Stage() == stage + 1, expectedInstruction + ": exactly one stage advances.");
                receipt.executedStages++;
                if (!receipt.actions.Contains(definition.action.ToString())) receipt.actions.Add(definition.action.ToString());
            }
            Check(sandbox.LastJobOutcome == job.outcome, job.id + ": completion records the concrete authored outcome.");
            for (int tick = 0; tick < 4; tick++) sandbox.Tick(.016f);
            Check(sandbox.CompletedContracts == beforeJobs + 1, job.id + ": later frames cannot duplicate completion.");
            if (record) receipt.executedJobs.Add(row);
        }

        private static Snapshot Capture(SlopCityInteractable site)
        {
            SlopCityCityScreen display = site.GetComponentInChildren<SlopCityCityScreen>();
            if (!display) display = Object.FindObjectsByType<SlopCityCityScreen>(FindObjectsSortMode.None)
                .Where(screen => !screen.IsFeatured && (screen.transform.position - site.transform.position).sqrMagnitude < 144)
                .OrderBy(screen => (screen.transform.position - site.transform.position).sqrMagnitude).FirstOrDefault();
            return new Snapshot {
                parts = sandbox.Salvage, water = sandbox.WaterSupply, recycling = sandbox.Recycling, readings = sandbox.FieldReadings,
                service = site.ServiceCount, condition = site.WorkCondition, stock = site.SupplyStock,
                screen = display, channel = display ? display.Channel : -1, clip = display ? display.ClipKey : ""
            };
        }

        private static void VerifyEffect(SlopCityJobAction action, SlopCityInteractable site, Snapshot before)
        {
            Check(site.ServiceCount > before.service && !string.IsNullOrWhiteSpace(site.ServiceStatus), action + ": the device records the successful physical operation.");
            switch (action)
            {
                case SlopCityJobAction.CollectParcel: Check(sandbox.CargoLabel == "SEALED PARCEL", "Collecting creates carried parcel cargo."); break;
                case SlopCityJobAction.DeliverParcel: Check(string.IsNullOrEmpty(sandbox.CargoLabel) && site.SupplyStock == before.stock + 3, "Delivery consumes cargo and adds three actual stock units."); break;
                case SlopCityJobAction.CollectSample: Check(sandbox.CargoLabel == "SEALED SAMPLE", "Collecting creates sealed sample cargo."); break;
                case SlopCityJobAction.DeliverSample: Check(string.IsNullOrEmpty(sandbox.CargoLabel) && site.SupplyStock == before.stock + 1, "Sample receipt consumes cargo and adds actual stock."); break;
                case SlopCityJobAction.RecoverPart: Check(sandbox.Salvage == Math.Min(12, before.parts + 1), "Recovery adds one bounded usable part."); break;
                case SlopCityJobAction.Repair: Check(before.parts > 0 && sandbox.Salvage == before.parts - 1 && before.condition < 100 && site.WorkCondition == 100, "Repair spends a carried component and restores actual condition."); break;
                case SlopCityJobAction.CollectRecycling: Check(sandbox.Recycling == Math.Min(12, before.recycling + 1), "Collection produces a real recycling bundle."); break;
                case SlopCityJobAction.Recycle: Check(before.recycling > 0 && sandbox.Recycling == 0 && sandbox.Salvage == Math.Min(12, before.parts + before.recycling), "Recycling consumes carried bundles and supplies usable parts."); break;
                case SlopCityJobAction.FillWater: Check(sandbox.WaterSupply == 3, "Filling supplies three actual water uses."); break;
                case SlopCityJobAction.WaterPlant: Check(before.water > 0 && sandbox.WaterSupply == before.water - 1 && site.IsWatered, "Watering consumes carried water and changes the actual plant state."); break;
                case SlopCityJobAction.MaintainSurface: Check(site.IsClean, "Maintenance changes the actual surface cleanliness."); break;
                case SlopCityJobAction.SwitchOff:
                case SlopCityJobAction.SwitchOn:
                    bool on = action == SlopCityJobAction.SwitchOn; Check(site.IsOn == on, "Lamp power matches its physical operation.");
                    foreach (Light light in site.GetComponentsInChildren<Light>(true)) Check(light.enabled == on, "The actual Light component follows the lamp power."); break;
                case SlopCityJobAction.Tune: Check(before.screen && (before.screen.Channel != before.channel || before.screen.ClipKey != before.clip), "Tuning changes an actual registered display assignment."); break;
                case SlopCityJobAction.Supply: Check(sandbox.WaterSupply == 3 && sandbox.Salvage >= Math.Max(2, before.parts) && sandbox.SpeedMultiplier > 1, "Supply restocks the carried work kit and recovery."); break;
                case SlopCityJobAction.Rest: Check(sandbox.SpeedMultiplier > 1, "Rest supplies a real movement recovery effect."); break;
                case SlopCityJobAction.Travel: Check(site.Destination && Vector3.Distance(user.position, site.Destination.position) < .05f, "Transport physically reaches the authored arrival before success."); break;
                case SlopCityJobAction.Diagnose:
                case SlopCityJobAction.PressureTest:
                case SlopCityJobAction.ReadNotice: Check(sandbox.FieldReadings == Math.Min(24, before.readings + 1), "Inspection produces a bounded real field reading."); break;
                default: throw new InvalidOperationException("No independent world-effect assertion exists for required action " + action);
            }
        }

        private static void RejectedOperations()
        {
            PrepareWork(); Check(sandbox.SelectJob("signal-diagnostic"), "A tuning route is available for rejection checks.");
            int guard = 0;
            while (Get<SlopCityJobAction>(Get<IList>(sandbox, "steps")[Stage()], "action") != SlopCityJobAction.Tune && guard++ < 8) UseCurrent();
            Check(guard < 8, "The physical tuning stage is reached.");
            int stage = Stage(), completed = sandbox.CompletedContracts;
            PropertyInfo instance = typeof(SlopCityCityMedia).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
            object previous = instance.GetValue(null); instance.SetValue(null, null);
            try { UseCurrent(); Check(Stage() == stage && sandbox.CompletedContracts == completed, "A missing media service cannot report a successful tune or advance the job."); }
            finally { instance.SetValue(null, previous); }
            PrepareWork(); Check(sandbox.SelectJob("transit-courier"), "A real transit route is available for rejection checks.");
            UseCurrent(); SlopCityInteractable ride = sandbox.ObjectiveTarget; Transform original = ride.Destination;
            Approach(ride, true); Action<Vector3, float> originalTeleport = sandbox.TeleportRequested;
            sandbox.TeleportRequested = (_, __) => { }; // Scoped failed movement observation; destination remains distant.
            stage = Stage(); completed = sandbox.CompletedContracts;
            try
            {
                sandbox.Interact(camera); sandbox.Tick(4.1f);
                Check(Stage() == stage && sandbox.CompletedContracts == completed && Vector3.Distance(user.position, original.position) > 1,
                    "A transport callback that fails to move the player cannot complete a travel stage.");
            }
            finally { sandbox.CancelTravel(); sandbox.TeleportRequested = originalTeleport; }
            var lamp = FixtureSites.First(site => site.Kind == SlopCityInteractionKind.Light);
            PrepareWork(); Check(lamp.RepairDevice() && lamp.WorkCondition == 100 && !lamp.RepairDevice(), "Healthy equipment rejects a duplicate repair.");
            var plant = FixtureSites.First(site => site.Kind == SlopCityInteractionKind.Plant);
            Check(plant.WaterPlant() && !plant.WaterPlant(), "A wet plant rejects duplicate watering.");
            var bench = FixtureSites.First(site => site.Kind == SlopCityInteractionKind.Bench);
            Check(bench.MaintainSurface() && !bench.MaintainSurface(), "A clean surface rejects duplicate maintenance.");
        }

        private static void WorkStateLifecycle()
        {
            var site = FixtureSites.First(item => item.Kind == SlopCityInteractionKind.Light);
            var state = site.CaptureWorkState(); state.condition = 100; state.clean = true; state.watered = true;
            state.wearAgeSeconds = state.cleanAgeSeconds = state.waterAgeSeconds = 601;
            site.RestoreWorkState(state);
            Check(site.WorkCondition == 80 && !site.IsClean && !site.IsWatered, "Elapsed active-use ages cause bounded wear, dirt and drying after ten minutes.");
            var roundTrip = site.CaptureWorkState(); site.RestoreWorkState(roundTrip);
            Check(site.WorkCondition == 80, "Capturing and restoring elapsed ages cannot apply the same wear interval twice.");
            state = site.CaptureWorkState(); state.condition = 100; state.clean = true; state.watered = true;
            state.wearAgeSeconds = state.cleanAgeSeconds = state.waterAgeSeconds = 599;
            site.RestoreWorkState(state);
            Check(site.WorkCondition == 100 && site.IsClean && site.IsWatered, "Work effects remain active before the actual ten-minute expiry.");
            while (site.SupplyStock > 0) Check(site.SpendStock(), "Each successful stock use consumes an existing unit.");
            Check(!site.SpendStock() && site.SupplyStock == 0, "Insufficient stock cannot be spent into a negative balance.");
            site.ReceiveParcel(); site.ReceiveSample();
            Check(site.SupplyStock == 4 && site.SpendStock() && site.SupplyStock == 3, "Parcel/sample stock is authoritative and usable by later work.");
        }

        private static void PhysicalWorkVisuals()
        {
            Shader shader = Shader.Find("Standard"); Check(shader, "The actual Built-In material shader is available for world-effect checks.");
            var material = new Material(shader) { name = "Jobs_fixture_shared_world_material", color = new Color(.8f, .7f, .6f, 1) };
            material.SetColor("_EmissionColor", new Color(2, 1, .5f, 1)); Owned.Add(material);
            Color sourceColor = material.color, sourceEmission = material.GetColor("_EmissionColor");
            var owner = new GameObject("World_effect_lamp"); owner.transform.SetParent(fixture.transform); owner.transform.position = new Vector3(60, 0, 60);
            var item = owner.AddComponent<SlopCityInteractable>(); item.Configure(SlopCityInteractionKind.Light, "World effect lamp", false);
            var light = owner.AddComponent<Light>(); light.intensity = 2;
            GameObject lens = Box("Lamp_Lens", owner.transform.position + Vector3.up, Vector3.one * .3f); lens.transform.SetParent(owner.transform, true);
            Renderer renderer = lens.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            var properties = new MaterialPropertyBlock(); properties.SetFloat("_JobsFixtureForeign", 17); renderer.SetPropertyBlock(properties);
            item.RestoreWorkState(new SlopCityInteractable.WorkState { initialized = true, condition = 25 });
            renderer.GetPropertyBlock(properties); Color dim = properties.GetColor("_Color");
            Check(light.enabled && Mathf.Abs(light.intensity - .5f) < .0001f && dim != sourceColor,
                "A degraded physical lamp dims its actual Light and its lens property block.");
            item.Pulse(renderer);
            Check(item.RepairDevice(), "The physical lamp accepts a real repair.");
            renderer.GetPropertyBlock(properties);
            Check(Mathf.Abs(light.intensity - 2) < .0001f && properties.GetColor("_Color") == sourceColor
                && properties.GetColor("_EmissionColor") == sourceEmission && properties.GetFloat("_JobsFixtureForeign") == 17,
                "Repair replaces the transient pulse with the repaired lens/emission and preserves unrelated property-block ownership.");
            item.ToggleLights(); Check(!light.enabled && light.intensity == 0, "Power isolation switches off the actual lamp light and emission.");
            item.ToggleLights(); Check(light.enabled && Mathf.Abs(light.intensity - 2) < .0001f, "Power restoration reinstates the repaired actual lamp.");
            Check(renderer.sharedMaterial == material && material.color == sourceColor && material.GetColor("_EmissionColor") == sourceEmission,
                "World work leaves the original shared material and its properties unchanged.");
            foreach (var spec in new[] { new { kind = SlopCityInteractionKind.Bench, child = "StreetBench_TimberSlats" }, new { kind = SlopCityInteractionKind.Plant, child = "Tree_Canopy" } })
            {
                var root = new GameObject("World_effect_" + spec.kind); root.transform.SetParent(fixture.transform); root.transform.position = new Vector3(65, 0, 60);
                var device = root.AddComponent<SlopCityInteractable>(); device.Configure(spec.kind, spec.kind.ToString(), false);
                GameObject surface = Box(spec.child, root.transform.position + Vector3.up, Vector3.one * .3f); surface.transform.SetParent(root.transform, true);
                Renderer display = surface.GetComponent<Renderer>(); display.sharedMaterial = material;
                properties.Clear(); properties.SetFloat("_JobsFixtureForeign", 29); display.SetPropertyBlock(properties);
                device.RestoreWorkState(new SlopCityInteractable.WorkState { initialized = true, condition = 60 });
                display.GetPropertyBlock(properties); Color before = properties.GetColor("_Color");
                bool worked = spec.kind == SlopCityInteractionKind.Bench ? device.MaintainSurface() : device.WaterPlant();
                display.GetPropertyBlock(properties);
                Check(worked && properties.GetColor("_Color") != before && properties.GetColor("_Color") == sourceColor
                    && properties.GetFloat("_JobsFixtureForeign") == 29 && display.sharedMaterial == material,
                    spec.kind + ": real cleaning/watering visibly changes only its authored property block and preserves shared art/foreign properties.");
            }
            var panelRoot = new GameObject("Roof_Maintenance_Panel_fixture"); panelRoot.transform.SetParent(fixture.transform); panelRoot.transform.position = new Vector3(70, 0, 60);
            var panel = panelRoot.AddComponent<SlopCityInteractable>(); panel.Configure(SlopCityInteractionKind.Inspect, "Roof panel", false);
            GameObject face = Box("Roof_Service_Face", panelRoot.transform.position + Vector3.up, Vector3.one * .3f); face.transform.SetParent(panelRoot.transform, true);
            GameObject wall = Box("Unrelated_building_skin", panelRoot.transform.position + Vector3.right, Vector3.one * .3f); wall.transform.SetParent(panelRoot.transform, true);
            Renderer faceRenderer = face.GetComponent<Renderer>(), wallRenderer = wall.GetComponent<Renderer>(); faceRenderer.sharedMaterial = wallRenderer.sharedMaterial = material;
            properties.Clear(); properties.SetFloat("_JobsFixtureForeign", 91); properties.SetColor("_Color", Color.cyan); wallRenderer.SetPropertyBlock(properties);
            panel.RestoreWorkState(new SlopCityInteractable.WorkState { initialized = true, condition = 60 });
            faceRenderer.GetPropertyBlock(properties); Color dirty = properties.GetColor("_Color");
            Check(panel.MaintainSurface(), "A real dirty roof service panel accepts maintenance.");
            faceRenderer.GetPropertyBlock(properties); Check(properties.GetColor("_Color") != dirty && properties.GetColor("_Color") == sourceColor,
                "Roof work visibly cleans its actual authored service face.");
            wallRenderer.GetPropertyBlock(properties); Check(properties.GetColor("_Color") == Color.cyan && properties.GetFloat("_JobsFixtureForeign") == 91
                && wallRenderer.sharedMaterial == material && material.color == sourceColor,
                "Roof maintenance leaves unrelated building skin, its property ownership and shared art unchanged.");
        }

        private static void MetadataMedia(IEnumerable<SlopCityInteractable> targets)
        {
            media = fixture.AddComponent<SlopCityCityMedia>(); media.enabled = false; Invoke(media, "Awake");
            Array channels = Get<Array>(media, "channels");
            Type channelType = typeof(SlopCityCityMedia).GetNestedType("Channel", BindingFlags.NonPublic);
            for (int index = 0; index < channels.Length; index++) { object channel = Activator.CreateInstance(channelType, true); Set(channel, "index", index); channels.SetValue(channel, index); }
            Type displayType = typeof(SlopCityCityMedia).GetNestedType("ScreenState", BindingFlags.NonPublic);
            int ordinal = 0;
            foreach (SlopCityInteractable item in targets)
            {
                var screen = item.gameObject.AddComponent<SlopCityCityScreen>(); screen.Configure(item.GetComponent<Renderer>(), ordinal % 3, 14);
                object display = Activator.CreateInstance(displayType, true); Set(display, "screen", screen); Set(display, "ordinal", ordinal++);
                ((IList)Get(media, "displays")).Add(display); ((IDictionary)Get(media, "displayLookup")).Add(screen, display); ((IList)Get(media, "screens")).Add(screen);
            }
            Set(media, "initialized", true);
            var posts = new List<SlopCityTikTokVideo>();
            for (int index = 0; index < 8; index++)
            {
                string id = (7696938454575238400L + index).ToString(); string creator = index % 2 == 0 ? "1984drh" : "machine_publishers";
                posts.Add(new SlopCityTikTokVideo { creator = creator, id = id, kind = "video", title = "Offline metadata " + index,
                    sourceUrl = "https://www.tiktok.com/@" + creator + "/video/" + id, bundledVideoFile = id + ".mp4" });
            }
            media.ApplyTikTokCatalog(new SlopCityTikTokCatalog { videos = posts.ToArray() });
            Check(media.ScreenCount == 2, "Two real registered city screens support offline metadata tuning without a decoder.");
        }

        private static void SavedCityMetadataMedia()
        {
            media = fixture.AddComponent<SlopCityCityMedia>(); media.enabled = false; Invoke(media, "Awake");
            media.Configure(GameObject.Find("SlopCity_NineBlocks").transform, user, Shader.Find("Unlit/Texture"));
            // Production screen discovery/registration, with retained textures deliberately deferred.
            // No Start, ReadProgress, feed refresh, decoder or frame update is called.
            Set(media, "activeDecoderLimit", 1); Invoke(media, "ReadConfiguration"); Invoke(media, "DiscoverScreens");
            Set(media, "initialized", true);
            Check(media.ScreenCount > 20, "Production discovery registers the actual saved-city screen surfaces without playback.");
            foreach (object display in (IList)Get(media, "displays"))
            {
                Check(Get(display, "texture") == null, "Saved-city generation allocates no retained screen texture or decoder.");
                Material material = Get<Material>(display, "material"); if (material) Owned.Add(material);
            }
        }

        private static void ReleaseMetadataMaterials()
        {
            if (!media) return;
            foreach (object display in (IList)Get(media, "displays")) Set(display, "material", null);
        }

        private static void PrepareWork()
        {
            foreach (SlopCityInteractable site in FixtureSites)
            {
                var state = site.CaptureWorkState(); state.initialized = true; state.condition = 60; state.clean = false; state.watered = false;
                state.stockUnits = 6; state.wearAgeSeconds = state.cleanAgeSeconds = state.waterAgeSeconds = 0;
                site.RestoreWorkState(state); site.MarkUsed(0);
                if (!site.IsOn) site.ToggleLights();
            }
        }

        private static void AgeWork()
        {
            // Explicit fixture clock advancement, exercising production expiry rather than
            // making a completed repair/cleaning/watering immediately ineffective in gameplay.
            foreach (SlopCityInteractable site in FixtureSites)
            {
                var state = site.CaptureWorkState(); state.wearAgeSeconds = 1201;
                if (state.clean) state.cleanAgeSeconds = 601;
                if (state.watered) state.waterAgeSeconds = 601;
                site.RestoreWorkState(state); site.MarkUsed(0);
                if (!site.IsOn) site.ToggleLights();
            }
            receipt.explicitAgeAdvances++;
        }

        private static void UseCurrent()
        {
            SlopCityInteractable site = sandbox.ObjectiveTarget;
            Check(site && Approach(site, true), "A clear physical center ray reaches the actual current work target.");
            Check(sandbox.Interact(camera), "Production USE handles the aimed physical device.");
        }

        private static bool Approach(SlopCityInteractable site, bool synthetic)
        {
            Vector3 point = site.InteractionPoint;
            foreach (float radius in new[] { .65f, 1.15f, 1.75f, 2.4f, 2.8f }) for (int direction = 0; direction < 24; direction++)
            {
                Vector3 offset = Quaternion.Euler(0, direction * 15f, 0) * Vector3.forward * radius;
                if (!Physics.Raycast(new Vector3(point.x + offset.x, point.y + .9f, point.z + offset.z), Vector3.down,
                    out RaycastHit support, 4f, ~0, QueryTriggerInteraction.Ignore) || support.normal.y < .8f) continue;
                Vector3 feet = support.point + Vector3.up * .025f;
                int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * .24f, feet + Vector3.up * 1.50f, .215f, Overlaps, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = count == Overlaps.Length;
                for (int i = 0; i < count; i++) if (Overlaps[i] && !Overlaps[i].transform.IsChildOf(user)
                    && !Overlaps[i].GetComponentInParent<SlopCityFirstPerson>()) blocked = true;
                if (blocked) continue;
                Vector3 eye = feet + Vector3.up * 1.62f, aim = point - eye;
                if (aim.sqrMagnitude < .01f || aim.sqrMagnitude > 3.4f * 3.4f) continue;
                count = Physics.RaycastNonAlloc(eye, aim.normalized, Hits, 3.4f, ~0, QueryTriggerInteraction.Ignore);
                if (count == Hits.Length) continue;
                Transform nearest = null; float distance = 3.4f;
                for (int i = 0; i < count; i++)
                    if (Hits[i].transform && !Hits[i].transform.IsChildOf(user) && Hits[i].distance < distance)
                    { nearest = Hits[i].transform; distance = Hits[i].distance; }
                if (!nearest || nearest.GetComponentInParent<SlopCityInteractable>() != site) continue;
                capsule.enabled = false; user.position = feet; user.rotation = Quaternion.identity; capsule.enabled = true;
                camera.transform.localPosition = Vector3.up * 1.62f; camera.transform.LookAt(point); Physics.SyncTransforms();
                Set(sandbox, "nextTargetAt", 0f);
                typeof(SlopCitySandbox).GetField("webTargetFrame", Members)?.SetValue(sandbox, -1);
                object target = Invoke(sandbox, "FindTarget", camera);
                SlopCityInteractable actual = Get<SlopCityInteractable>(target, "interaction");
                if (actual == site || (actual && actual.transform.IsChildOf(site.transform))) return true;
            }
            return false;
        }

        private static SlopCityInteractable AddSite(SlopCityInteractionKind kind, string label, Vector3? position = null)
        {
            int ordinal = FixtureSites.Count; Vector3 location = position ?? new Vector3((ordinal % 6) * 7, .75f, (ordinal / 6) * 7);
            GameObject box = Box(label, location, new Vector3(.8f, 1.5f, .8f));
            var site = box.AddComponent<SlopCityInteractable>(); site.Configure(kind, label); site.ConfigureInteractionPoint(location); FixtureSites.Add(site); return site;
        }
        private static GameObject Box(string name, Vector3 position, Vector3 scale)
        { GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name; box.transform.SetParent(fixture.transform); box.transform.position = position; box.transform.localScale = scale; return box; }
        private static void ClearSaves() { foreach (string key in SaveKeys) PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); }
        private static int Stage() => Get<int>(sandbox, "stepIndex");
        private static string Hierarchy(Transform item) { string path = item.name; for (Transform p = item.parent; p; p = p.parent) path = p.name + "/" + path; return path; }
        private static object Get(object owner, string name) => owner.GetType().GetField(name, Members).GetValue(owner);
        private static T Get<T>(object owner, string name) => (T)Get(owner, name);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Members).SetValue(owner, value);
        private static object Invoke(object owner, string name, params object[] arguments) => owner.GetType().GetMethod(name, Members).Invoke(owner, arguments);
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); receipt.checks++; }
        private static void CaptureLog(string message, string trace, LogType kind) { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) receipt.runtimeErrors.Add(message + "\n" + trace); }
        private sealed class Backup { public bool existed; public string value; }
        private sealed class Snapshot { public int parts, water, recycling, readings, service, condition, stock, channel; public SlopCityCityScreen screen; public string clip; }
        [Serializable] private sealed class JobEvidence { public string id, title; public List<string> targets = new List<string>(); }
        [Serializable] private sealed class SaveView { public string activeJobId, cargo; public int activeStep, completed; public JobCount[] jobs; public SavedWork[] work; }
        [Serializable] private sealed class JobCount { public string id; public int completed; }
        [Serializable] private sealed class SavedWork { public string id; public SlopCityInteractable.WorkState state; }
        [Serializable] private sealed class Receipt
        {
            public bool passed, savesRestored;
            public string startedAtUtc, finishedAtUtc, unityVersion, targeting, lastFeedback;
            public int checks, executedStages, fairCycles, savedCityApproaches, savedCityDistinctTargets, explicitAgeAdvances;
            public string scope = "Isolated batch Edit Mode. Saved-scene production SelectJob and production display discovery validate all forty routes and sampled local standing capsule/physical center rays; no saved-city work or whole-route walk is claimed. Separate synthetic physical devices execute all forty recipes plus two fair rotations through production Sandbox.Interact, real resource/device state and delayed transit Tick. Fixture work ages advance explicitly between jobs, followed by fair dispatch refresh; no production work effect is reset. Production metadata-only screen reconciliation validates tuning without decoding. Elevated synthetic route uses its existing destination; full moving-elevator acceptance belongs to the separate Interior fixture. No Start, Play Mode, network, decoder, application launch, audio or device-performance acceptance. All four progression/recovery keys restored; scene/import settings never saved.";
            public List<JobEvidence> savedCityJobs = new List<JobEvidence>(), executedJobs = new List<JobEvidence>();
            public List<string> actions = new List<string>(), unprovenApproaches = new List<string>(), runtimeErrors = new List<string>(), errors = new List<string>();
        }
    }
}
#endif
