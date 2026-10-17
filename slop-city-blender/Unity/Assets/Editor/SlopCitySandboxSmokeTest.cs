#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Exercises the production sandbox through camera rays in a deterministic physics fixture.</summary>
    [InitializeOnLoad]
    public static class SlopCitySandboxSmokeTest
    {
        private const string ActiveKey = "SlopCity.SandboxSmoke.Active";
        private const string BackupKey = "SlopCity.SandboxSmoke.Backup";
        private const string BackupExistsKey = "SlopCity.SandboxSmoke.BackupExists";
        private const string StartedKey = "SlopCity.SandboxSmoke.Started";
        private const string SaveKey = "SlopCity.Sandbox.Progress.v1";
        private const string ReportPath = "Builds/Reports/sandbox-playback-check.json";
        private static IEnumerator work;
        private static Receipt report;
        private static double enteredAt;
        private static GameObject fixture;
        private static Transform user;
        private static Camera camera;
        private static CharacterController capsule;
        private static SlopCityPlayerInput input;
        private static SlopCitySandbox sandbox;

        static SlopCitySandboxSmokeTest()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                if (change == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (change == PlayModeStateChange.EnteredEditMode) Finish(new Exception("Play Mode ended before sandbox checks completed."));
            };
        }

        // Dedicated batch Editor only. Omit -quit; the runner saves a report and exits itself.
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Sandbox smoke tests require a dedicated batch Editor.");
            SessionState.SetBool(BackupExistsKey, PlayerPrefs.HasKey(SaveKey));
            SessionState.SetString(BackupKey, PlayerPrefs.GetString(SaveKey, ""));
            SessionState.SetString(StartedKey, DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(ActiveKey, true);
            report = NewReceipt(); WriteReport();
            try
            {
                PlayerPrefs.DeleteKey(SaveKey); PlayerPrefs.Save();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static Receipt NewReceipt() => new Receipt
        {
            startedAtUtc = SessionState.GetString(StartedKey, DateTime.UtcNow.ToString("O")), unityVersion = Application.unityVersion
        };

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (report == null) report = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(report.startedAtUtc).ToUniversalTime()).TotalSeconds > 240d)
                    throw new TimeoutException("Sandbox checks exceeded four minutes.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (work == null)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < .25d) return;
                    work = Exercise();
                }
                if (!work.MoveNext()) Finish(null);
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static IEnumerator Exercise()
        {
            Time.timeScale = 1f;
            fixture = new GameObject("Sandbox_Physics_Test_Fixture");
            Box("Ground", new Vector3(0, -.1f, 0), new Vector3(100, .2f, 100));
            user = new GameObject("Test_Player").transform;
            user.SetParent(fixture.transform);
            capsule = user.gameObject.AddComponent<CharacterController>();
            capsule.radius = .22f; capsule.height = 1.7f; capsule.center = Vector3.up * .85f;
            input = user.gameObject.AddComponent<SlopCityPlayerInput>(); input.SetGameplayEnabled(true);
            camera = new GameObject("Test_Camera").AddComponent<Camera>();
            camera.transform.SetParent(user); camera.transform.localPosition = Vector3.up * 1.62f;
            var bin = Site("Recycling", SlopCityInteractionKind.Bin, new Vector3(0, .75f, 0));
            var terminal = Site("Dispatch terminal", SlopCityInteractionKind.Terminal, new Vector3(5, .75f, 0));
            Site("Receiving terminal", SlopCityInteractionKind.Terminal, new Vector3(-5, .75f, 0));
            Site("Analysis terminal", SlopCityInteractionKind.Terminal, new Vector3(-10, .75f, 0));
            Site("Second recycling intake", SlopCityInteractionKind.Bin, new Vector3(-15, .75f, 0));
            Site("Second planter", SlopCityInteractionKind.Plant, new Vector3(20, .75f, -8));
            Site("Third planter", SlopCityInteractionKind.Plant, new Vector3(20, .75f, -16));
            Site("Parts crate", SlopCityInteractionKind.Collectible, new Vector3(-30, .75f, -12));
            var hydrant = Site("Hydrant", SlopCityInteractionKind.Hydrant, new Vector3(-20, .75f, -12));
            Site("Fuel pump", SlopCityInteractionKind.GasPump, new Vector3(-25, .75f, -12));
            Site("Civic notice", SlopCityInteractionKind.Inspect, new Vector3(-20, .75f, -20));
            Site("Civic building surface", SlopCityInteractionKind.Building, new Vector3(25, .75f, -25));
            Site("Remote signal screen", SlopCityInteractionKind.Screen, new Vector3(10, .75f, -8));
            var rare = Site("Rare receiver", SlopCityInteractionKind.RareFind, new Vector3(-30, .75f, -20), false);
            rare.ConfigureRareFind("sandbox-fixture-receiver", "Rare fixture transmission", "A deliberate hidden civic memory.");
            Site("Signal screen", SlopCityInteractionKind.Screen, new Vector3(10, .75f, 0));
            Site("Public bench", SlopCityInteractionKind.Bench, new Vector3(15, .75f, 0));
            var plant = Site("Planter", SlopCityInteractionKind.Plant, new Vector3(20, .75f, 0));
            var lamp = Site("Street lamp", SlopCityInteractionKind.Light, new Vector3(25, .75f, 0));
            var lampLight = lamp.gameObject.AddComponent<Light>();
            var fountain = Site("Fountain", SlopCityInteractionKind.Fountain, new Vector3(30, .75f, 0));
            for (int i = 1; i <= 2; i++)
                Site("Additional street lamp " + i, SlopCityInteractionKind.Light, new Vector3(25, .75f, -8 * i)).gameObject.AddComponent<Light>();
            var vending = Site("Vending", SlopCityInteractionKind.Vending, new Vector3(0, .75f, -12));
            GameObject untagged = Box("Untagged sculpted wall", new Vector3(0, 1f, -7), Vector3.one);
            GameObject trim = Box("Colliderless poster", new Vector3(4, 1f, -7), Vector3.one);
            Object.DestroyImmediate(trim.GetComponent<Collider>());
            GameObject doorRoot = new GameObject("Test_door"); doorRoot.transform.SetParent(fixture.transform);
            var door = doorRoot.AddComponent<SlopCityDoor>();
            GameObject leaf = Box("Door leaf", new Vector3(9, 1f, -7), new Vector3(1, 2, .2f));
            leaf.transform.SetParent(doorRoot.transform, true);
            door.Configure(leaf.transform, leaf.GetComponent<Collider>(), leaf.transform.position, "Test door");
            var carRoot = new GameObject("Drivable car"); carRoot.transform.SetParent(fixture.transform);
            carRoot.transform.position = new Vector3(0, 0, 8);
            var car = carRoot.AddComponent<SlopCityInteractable>(); car.Configure(SlopCityInteractionKind.Car, "Test car", false);
            GameObject body = Box("Car body", new Vector3(0, .65f, 8), new Vector3(1.8f, .9f, 3.8f));
            body.transform.SetParent(carRoot.transform, true);
            foreach (float side in new[] { -.88f, .88f }) foreach (float axle in new[] { -1.17f, 1.17f })
            {
                GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wheel.name = "Car wheel"; wheel.transform.SetParent(carRoot.transform, false);
                wheel.transform.localPosition = new Vector3(side, .38f, axle);
                wheel.transform.localScale = new Vector3(.76f, .12f, .76f);
                wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
            }
            car.ConfigureInteractionPoint(body.transform.position);
            Box("Driving barrier", new Vector3(0, 2, 18), new Vector3(12, 4, .4f));
            var lift = Site("RIDE TO NEXT STATION", SlopCityInteractionKind.Subway, new Vector3(12, .75f, 12), false);
            var arrival = new GameObject("Safe subway platform").transform;
            arrival.SetParent(fixture.transform); arrival.position = new Vector3(18, .04f, 18);
            lift.ConfigureDestination(arrival);
            var upperArrival = new GameObject("Upper civic landing").transform;
            upperArrival.SetParent(fixture.transform); upperArrival.position = new Vector3(30, 5.04f, 20);
            Box("Upper civic platform", new Vector3(30, 4.9f, 20), new Vector3(6, .2f, 6));
            Site("Upper-level civic panel", SlopCityInteractionKind.Inspect, new Vector3(30, 5.75f, 21));
            var buildingLift = Site("Building lift", SlopCityInteractionKind.Building, new Vector3(30, .75f, 12));
            buildingLift.ConfigureDestination(upperArrival);
            sandbox = user.gameObject.AddComponent<SlopCitySandbox>();
            sandbox.TeleportRequested += (point, heading) =>
            {
                bool wasEnabled = capsule.enabled;
                capsule.enabled = false; user.SetPositionAndRotation(point, Quaternion.Euler(0, heading, 0));
                capsule.enabled = wasEnabled;
                Physics.SyncTransforms();
            };
            sandbox.MovementOverrideRequested += value => capsule.enabled = !value;
            sandbox.Initialize(user, message => report.lastFeedback = message);
            UnityEngine.Random.InitState(81240);
            Check(SlopCityJobCatalog.All.Length == 40 && SlopCityJobCatalog.ValidateDefinitions().Length == 0,
                "All 40 authored job definitions have valid targets, references and resource prerequisites.");
            int catalogSteps = 0, catalogReferences = 0;
            foreach (SlopCityJobDefinition definition in SlopCityJobCatalog.All)
                foreach (SlopCityJobStepDefinition step in definition.steps)
                { catalogSteps++; if (step.reuseStep >= 0) catalogReferences++; }
            Check(catalogSteps == 148 && catalogReferences == 27, "The catalog contains 148 stages and 27 explicit device references.");
            Check(sandbox.HasWaypoint && sandbox.AvailableJobTypes == 40, "The fixture makes all 40 job types actionable.");
            Check(sandbox.Credits == 0 && sandbox.Experience == 0 && sandbox.CompletedContracts == 0,
                "A new save starts without cash, XP or completed jobs.");

            // Call the production action handler directly only for negative precondition cases.
            int initialStage = Stage();
            Check(!InvokeAction(terminal, SlopCityJobAction.Repair) && sandbox.Salvage == 0,
                "Repair without a recovered component is refused.");
            Check(!InvokeAction(plant, SlopCityJobAction.WaterPlant) && sandbox.WaterSupply == 0,
                "Watering without carried water is refused.");
            Check(!InvokeAction(terminal, SlopCityJobAction.DeliverParcel)
                && !InvokeAction(terminal, SlopCityJobAction.DeliverSample), "Empty cargo cannot complete parcel or sample handoffs.");
            Check(!InvokeAction(terminal, SlopCityJobAction.Recycle)
                && !InvokeAction(terminal, SlopCityJobAction.FileReport), "Empty recycling and empty field records cannot be submitted.");
            Check(!InvokeAction(plant, SlopCityJobAction.Repair)
                && !InvokeAction(terminal, (SlopCityJobAction)999), "Wrong kinds and unknown actions have no permissive fallback.");
            Check(Stage() == initialStage && sandbox.CompletedContracts == 0 && sandbox.Credits == 0 && sandbox.Experience == 0,
                "Rejected operations leave dispatch and rewards unchanged.");
            SlopCityInteractable malformedTarget = sandbox.ObjectiveTarget;
            SlopCityInteractionKind originalKind = malformedTarget.Kind;
            string originalLabel = malformedTarget.DisplayName;
            bool originalEligibility = malformedTarget.ObjectiveEligible;
            malformedTarget.Configure(SlopCityInteractionKind.Car, "Incompatible reassigned device", originalEligibility);
            AimAt(malformedTarget.InteractionPoint); sandbox.Interact(camera);
            Check(Stage() == initialStage && sandbox.CompletedContracts == 0 && malformedTarget.ServiceCount == 0,
                "A reassigned incompatible dispatch target cannot advance through E.");
            malformedTarget.Configure(originalKind, originalLabel, originalEligibility);
            Check(!SlopCityJobCatalog.Supports(buildingLift, SlopCityJobAction.ReadNotice)
                && !SlopCityJobCatalog.Supports(lift, SlopCityJobAction.Tune), "Destination panels keep their physical transit role.");
            SlopCityInteractable[] allSites = Object.FindObjectsByType<SlopCityInteractable>(FindObjectsSortMode.None);
            foreach (SlopCityInteractable site in allSites) if (site.Kind != SlopCityInteractionKind.Bench) site.gameObject.SetActive(false);
            sandbox.RerollObjective();
            Check(!sandbox.HasWaypoint && sandbox.AvailableJobTypes == 0, "A world without compatible job devices produces no wrong-kind fallback.");
            foreach (SlopCityInteractable site in allSites) site.gameObject.SetActive(true);
            sandbox.RerollObjective();

            var completedIds = new HashSet<string>();
            foreach (SlopCityJobDefinition definition in SlopCityJobCatalog.All)
            {
                SelectJob(definition.id);
                int previousCompleted = sandbox.CompletedContracts;
                var selectedSites = new List<SlopCityInteractable>();
                for (int stage = 0; stage < definition.steps.Length; stage++)
                {
                    SlopCityJobStepDefinition step = definition.steps[stage];
                    SlopCityInteractable target = sandbox.ObjectiveTarget;
                    Check(target && SlopCityJobCatalog.Supports(target, step.action)
                        && Array.IndexOf(step.kinds, target.Kind) >= 0, definition.id + ": stage uses a strictly compatible active device.");
                    if (step.reuseStep >= 0)
                        Check(target == selectedSites[step.reuseStep], definition.id + ": servicing retains the referenced device.");
                    selectedSites.Add(target);
                    int beforeParts = sandbox.Salvage, beforeWater = sandbox.WaterSupply;
                    int beforeRecycling = sandbox.Recycling, beforeReadings = sandbox.FieldReadings, beforeService = target.ServiceCount;
                    AimAt(target.InteractionPoint);
                    Check(sandbox.Interact(camera), definition.id + ": E reaches the actual centered device.");
                    if (step.action == SlopCityJobAction.Travel && sandbox.IsTravelling)
                    {
                        Check(Stage() == stage && sandbox.CompletedContracts == previousCompleted && !capsule.enabled,
                            definition.id + ": departure does not prematurely advance dispatch.");
                        sandbox.Tick(2f);
                        Check(sandbox.IsTravelling && Stage() == stage, definition.id + ": arrival delay retains the travel stage.");
                        sandbox.Tick(2.1f);
                        Check(!sandbox.IsTravelling && capsule.enabled && Vector3.Distance(user.position, target.Destination.position) < .01f,
                            definition.id + ": arrival physically reaches the destination before advancement.");
                    }
                    VerifyAction(step.action, target, beforeParts, beforeWater, beforeRecycling, beforeReadings, beforeService);
                    Check(sandbox.Credits == 0 && sandbox.Experience == 0, definition.id + ": ordinary work grants no cash or XP.");
                    if (stage + 1 < definition.steps.Length)
                        Check(sandbox.CurrentJobId == definition.id && Stage() == stage + 1, definition.id + ": exactly one stage advances.");
                    yield return null;
                }
                Check(sandbox.CompletedContracts == previousCompleted + 1 && sandbox.LastJobOutcome == definition.outcome,
                    definition.id + ": completion records the concrete outcome once.");
                Check(sandbox.HasWaypoint && sandbox.CurrentJobId != definition.id, definition.id + ": a different dispatch follows completion.");
                for (int tick = 0; tick < 6; tick++) sandbox.Tick(.016f);
                Check(sandbox.CompletedContracts == previousCompleted + 1 && sandbox.Credits == 0 && sandbox.Experience == 0,
                    definition.id + ": later frames cannot duplicate completion or grant ordinary rewards.");
                completedIds.Add(definition.id); report.jobIds.Add(definition.id);
            }
            Check(completedIds.Count == 40 && sandbox.CompletedContracts == 40, "Every authored job completes through production E interactions.");
            report.completedContracts = sandbox.CompletedContracts;
            Check(sandbox.Rank == 1, "Ordinary service work does not inflate experience or rank.");
            SlopCityInteractable unavailable = sandbox.ObjectiveTarget;
            unavailable.gameObject.SetActive(false); sandbox.RerollObjective();
            Check(sandbox.HasWaypoint && sandbox.ObjectiveTarget != unavailable && sandbox.Credits == 0,
                "Free reroll replaces unavailable devices without charging the player.");
            unavailable.gameObject.SetActive(true); sandbox.RerollObjective();

            AvoidContract(bin); bin.MarkUsed(0); AimAt(bin.InteractionPoint);
            int priorParts = sandbox.Salvage; sandbox.Interact(camera);
            Check(sandbox.Salvage == Mathf.Min(12, priorParts + 1), "Ordinary bin recovery supplies one usable component.");
            int firstScavenge = sandbox.Salvage; AimAt(bin.InteractionPoint); sandbox.Interact(camera);
            Check(sandbox.Salvage == firstScavenge && bin.CooldownRemaining > 0, "Repeated scavenging respects its resource cooldown.");
            AvoidContract(terminal); terminal.MarkUsed(0);
            while (sandbox.FieldReadings > 0) { AimAt(terminal.InteractionPoint); sandbox.Interact(camera); }
            int partsBeforeRepair = sandbox.Salvage; AimAt(terminal.InteractionPoint); sandbox.Interact(camera);
            Check(sandbox.Salvage == partsBeforeRepair - 1 && sandbox.Credits == 0, "Free terminal repair consumes a part without money.");
            AvoidContract(lamp); if (!lamp.IsOn) lamp.ToggleLights(); AimAt(lamp.InteractionPoint); sandbox.Interact(camera);
            Check(!lampLight.enabled, "A lamp interaction physically turns its Light component off.");
            AimAt(lamp.InteractionPoint); sandbox.Interact(camera);
            Check(lampLight.enabled, "A second lamp interaction restores illumination.");
            AvoidContract(vending); AimAt(vending.InteractionPoint); sandbox.Interact(camera);
            Check(sandbox.Credits == 0 && sandbox.Experience == 0 && sandbox.WaterSupply == 3 && sandbox.Salvage >= 2 && sandbox.SpeedMultiplier > 1,
                "Public vending supplies water, parts and a movement boost without a charge or XP.");
            AimAt(untagged.transform.position); int ordinaryCash = sandbox.Credits, ordinaryXp = sandbox.Experience;
            Check(sandbox.Interact(camera) && untagged.GetComponent<SlopCityInteractable>(), "Arbitrary colliders support useful condition inspection.");
            AimAt(trim.transform.position); Check(sandbox.Interact(camera) && trim.GetComponent<SlopCityInteractable>(),
                "Colliderless renderers support condition inspection.");
            Check(sandbox.Credits == ordinaryCash && sandbox.Experience == ordinaryXp, "Scenery inspection grants no discovery money or XP.");
            AimAt(leaf.transform.position); Check(!sandbox.Interact(camera), "Door rays defer to the physical door controller.");
            AimAt(rare.InteractionPoint); sandbox.Interact(camera);
            Check(sandbox.Credits == 45 && sandbox.Experience == 60 && sandbox.RareFindCount == 1,
                "One deliberate rare transmission grants the discovery reward.");
            AimAt(rare.InteractionPoint); sandbox.Interact(camera);
            Check(sandbox.Credits == 45 && sandbox.Experience == 60 && sandbox.RareFindCount == 1,
                "Repeated rare-find interaction cannot duplicate its reward.");

            AimAt(car.InteractionPoint); Check(sandbox.Interact(camera) && sandbox.IsDriving && !capsule.enabled,
                "Interacting with a car enters driving mode and disables the pedestrian capsule.");
            Bounds firstEnvelope = DrivingEnvelope();
            Vector3 parked = car.transform.position;
            input.SetTouchMovement(Vector2.up);
            for (int i = 0; i < 240; i++) sandbox.Tick(.016f);
            input.SetTouchMovement(Vector2.zero);
            Check(car.transform.position.z > parked.z + 2f, "Driving input physically moves the car across supported pavement.");
            Check(car.transform.position.z < 15.95f, "The moving car stops before a solid wall instead of crossing it.");
            Check(sandbox.Interact(camera) && !sandbox.IsDriving && capsule.enabled,
                "Exiting the car restores pedestrian movement at a safe nearby position.");
            Check(Vector3.Distance(user.position, car.transform.position) > 1.3f, "The exit destination is outside the car body.");
            car.transform.SetPositionAndRotation(new Vector3(20, 0, 8), Quaternion.Euler(0, 45, 0));
            Physics.SyncTransforms();
            AimAt(car.InteractionPoint); Check(sandbox.Interact(camera) && sandbox.IsDriving,
                "A car parked at a non-axis-aligned heading can be entered again.");
            Bounds rotatedEnvelope = DrivingEnvelope();
            Check(Vector3.Distance(firstEnvelope.center, rotatedEnvelope.center) < .001f
                && Vector3.Distance(firstEnvelope.size, rotatedEnvelope.size) < .001f,
                "Re-entering a car parked at 45 degrees preserves its collision envelope, including mesh-collider wheels.");
            Check(sandbox.Interact(camera) && !sandbox.IsDriving && capsule.enabled,
                "The angled car still provides a safe pedestrian exit.");
            lift.Configure(SlopCityInteractionKind.Subway, "Subway lift", false);
            AvoidContract(lift); AimAt(lift.InteractionPoint); sandbox.Interact(camera);
            Check(Vector3.Distance(user.position, arrival.position) < .01f, "Subway interaction travels to the authored destination marker.");
            lift.Configure(SlopCityInteractionKind.Subway, "RIDE TO NEXT STATION", false);
            AimAt(lift.InteractionPoint); sandbox.Interact(camera);
            Check(sandbox.IsTravelling && !capsule.enabled, "A subway ride starts its departure interval and locks walking.");
            sandbox.Tick(2f);
            Check(sandbox.IsTravelling && Vector3.Distance(user.position, arrival.position) > 1f,
                "Train travel preserves the departure interval before arriving.");
            sandbox.Interact(camera);
            Check(!sandbox.IsTravelling && capsule.enabled, "An in-progress train ride can be cancelled safely.");
            AimAt(lift.InteractionPoint); sandbox.Interact(camera); sandbox.Tick(4.1f);
            Check(!sandbox.IsTravelling && capsule.enabled && Vector3.Distance(user.position, arrival.position) < .01f,
                "A completed subway journey arrives and restores walking.");

            int savedCredits = sandbox.Credits, savedXp = sandbox.Experience, savedJobs = sandbox.CompletedContracts;
            GameObject restoredRoot = new GameObject("Reloaded_sandbox"); restoredRoot.transform.SetParent(fixture.transform);
            var restored = restoredRoot.AddComponent<SlopCitySandbox>(); restored.Initialize(user);
            AimAt(rare.InteractionPoint); restored.Interact(camera);
            Check(restored.Credits == 45 && restored.Experience == 60 && restored.RareFindCount == 1,
                "Reloaded rare-find history prevents a second reward for the same stable ID.");
            Check(restored.Credits == savedCredits && restored.Experience == savedXp && restored.CompletedContracts == savedJobs,
                "A new sandbox instance reloads credits, experience and completed contracts from persistence.");
            Object.DestroyImmediate(restoredRoot);
            string currentSave = PlayerPrefs.GetString(SaveKey, "");
            PlayerPrefs.SetString(SaveKey, "{\"credits\":231,\"experience\":478,\"salvage\":5,\"completed\":12,\"discovered\":[]}");
            var legacyRoot = new GameObject("Legacy_progress_reload"); legacyRoot.transform.SetParent(fixture.transform);
            var legacy = legacyRoot.AddComponent<SlopCitySandbox>(); legacy.Initialize(user);
            Check(legacy.Credits == 231 && legacy.Experience == 478 && legacy.Salvage == 5 && legacy.CompletedContracts == 12,
                "Existing credit, XP, parts and job balances survive the new progress fields.");
            Object.DestroyImmediate(legacyRoot); PlayerPrefs.SetString(SaveKey, currentSave); PlayerPrefs.Save();
            sandbox.Restart();
            Check(sandbox.Credits == savedCredits && sandbox.CompletedContracts == savedJobs && sandbox.HasWaypoint,
                "Restart replaces the active job while retaining earned progression.");
            yield return null;
        }

        private static void SelectJob(string id)
        {
            int attempts = 0;
            while (sandbox.CurrentJobId != id && attempts++ < 1600) sandbox.RerollObjective();
            Check(sandbox.CurrentJobId == id && Stage() == 0, id + ": bounded seeded reroll selects its authored recipe.");
        }

        private static int Stage()
        {
            return (int)typeof(SlopCitySandbox).GetField("stepIndex", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sandbox);
        }

        private static string Cargo()
        {
            return (string)typeof(SlopCitySandbox).GetField("cargo", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sandbox);
        }

        private static bool InvokeAction(SlopCityInteractable site, SlopCityJobAction action)
        {
            return (bool)typeof(SlopCitySandbox).GetMethod("ExecuteAction", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(sandbox, new object[] { site, action, camera, false });
        }

        private static void VerifyAction(SlopCityJobAction action, SlopCityInteractable target,
            int parts, int water, int recycling, int readings, int serviceCount)
        {
            Check(target.ServiceCount > serviceCount && !string.IsNullOrWhiteSpace(target.ServiceStatus),
                action + ": the actual device records a completed operation.");
            switch (action)
            {
                case SlopCityJobAction.CollectParcel:
                    Check(Cargo() == "SEALED PARCEL", "Parcel collection creates carried cargo."); break;
                case SlopCityJobAction.DeliverParcel: case SlopCityJobAction.DeliverSample:
                    Check(string.IsNullOrEmpty(Cargo()), action + ": successful handoff consumes carried cargo."); break;
                case SlopCityJobAction.CollectSample:
                    Check(Cargo() == "SEALED SAMPLE", "Sample collection creates sealed carried cargo."); break;
                case SlopCityJobAction.RecoverPart:
                    Check(sandbox.Salvage == Mathf.Min(12, parts + 1), "Part recovery supplies one bounded usable component."); break;
                case SlopCityJobAction.Repair:
                    Check(parts > 0 && sandbox.Salvage == parts - 1, "Repair consumes one replacement component."); break;
                case SlopCityJobAction.CollectRecycling:
                    Check(sandbox.Recycling == Mathf.Min(12, recycling + 1), "Collection packs a bounded recycling load."); break;
                case SlopCityJobAction.Recycle:
                    Check(recycling > 0 && sandbox.Recycling == 0 && sandbox.Salvage == Mathf.Min(12, parts + recycling),
                        "Recycling unloads all carried bundles into useful components."); break;
                case SlopCityJobAction.FillWater:
                    Check(sandbox.WaterSupply == 3, "Water refill provides three carried uses."); break;
                case SlopCityJobAction.WaterPlant:
                    Check(water > 0 && sandbox.WaterSupply == water - 1, "Plant care consumes one carried water use."); break;
                case SlopCityJobAction.SwitchOff: case SlopCityJobAction.SwitchOn:
                    bool on = action == SlopCityJobAction.SwitchOn;
                    Check(target.IsOn == on, action + ": the lamp's power state matches the request.");
                    foreach (Light light in target.GetComponentsInChildren<Light>(true))
                        Check(light.enabled == on, action + ": physical illumination follows its switch state.");
                    break;
                case SlopCityJobAction.Diagnose: case SlopCityJobAction.PressureTest:
                case SlopCityJobAction.ReadNotice: case SlopCityJobAction.MaintainSurface: case SlopCityJobAction.Tune:
                    Check(sandbox.FieldReadings == Mathf.Min(24, readings + 1), action + ": a field record is created."); break;
                case SlopCityJobAction.Upload: case SlopCityJobAction.FileReport:
                    Check(readings > 0 && sandbox.FieldReadings == 0, action + ": filing consumes all collected readings."); break;
                case SlopCityJobAction.Supply:
                    Check(sandbox.WaterSupply == 3 && sandbox.Salvage == Mathf.Max(parts, 2) && sandbox.SpeedMultiplier > 1,
                        "Public supply restocks water, components and stamina."); break;
                case SlopCityJobAction.Rest:
                    Check(sandbox.SpeedMultiplier > 1, "Rest produces a useful movement boost."); break;
                case SlopCityJobAction.Travel:
                    Check(target.Destination && Vector3.Distance(user.position, target.Destination.position) < .01f,
                        "Physical transport reaches the authored destination."); break;
            }
        }

        private static void AvoidContract(SlopCityInteractable site)
        {
            for (int i = 0; i < 30 && sandbox.ObjectiveTarget == site; i++) sandbox.RerollObjective();
            Check(sandbox.ObjectiveTarget != site, "Fixture interaction is independent of the active contract.");
        }

        private static Bounds DrivingEnvelope()
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            Vector3 center = (Vector3)typeof(SlopCitySandbox).GetField("vehicleLocalCenter", fields).GetValue(sandbox);
            Vector3 half = (Vector3)typeof(SlopCitySandbox).GetField("vehicleHalfExtents", fields).GetValue(sandbox);
            return new Bounds(center, half * 2f);
        }

        private static void AimAt(Vector3 point)
        {
            capsule.enabled = false;
            user.SetPositionAndRotation(new Vector3(point.x, Mathf.Max(0f, point.y - 1.62f), point.z - 2.6f), Quaternion.identity);
            camera.transform.localPosition = Vector3.up * 1.62f;
            camera.transform.LookAt(point);
            capsule.enabled = true;
            Physics.SyncTransforms();
        }

        private static GameObject Box(string label, Vector3 position, Vector3 scale)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = label; box.transform.SetParent(fixture.transform);
            box.transform.position = position; box.transform.localScale = scale;
            return box;
        }

        private static SlopCityInteractable Site(string label, SlopCityInteractionKind kind, Vector3 position, bool eligible = true)
        {
            GameObject box = Box(label, position, new Vector3(.8f, 1.5f, .8f));
            SlopCityInteractable item = box.AddComponent<SlopCityInteractable>();
            item.Configure(kind, label, eligible); item.ConfigureInteractionPoint(position);
            return item;
        }

        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            report.checks.Add(description);
        }

        private static void Finish(Exception exception)
        {
            SessionState.SetBool(ActiveKey, false); work = null;
            if (report == null) report = NewReceipt();
            if (exception != null) report.errors.Add(exception.ToString());
            // Destroy test writers before restoring the save; OnApplicationQuit must not overwrite it.
            if (fixture) Object.DestroyImmediate(fixture);
            if (SessionState.GetBool(BackupExistsKey, false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(BackupKey, ""));
            else PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
            report.passed = report.errors.Count == 0; report.result = report.passed ? "Passed" : "Failed";
            report.finishedAtUtc = DateTime.UtcNow.ToString("O"); WriteReport();
            Debug.Log("SLOP_CITY_SANDBOX_SMOKE_" + report.result.ToUpperInvariant() + " " + ReportPath);
            if (exception != null) Debug.LogException(exception);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }

        private static void WriteReport()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }

        [Serializable] private sealed class Receipt
        {
            public bool passed;
            public string result = "Running", startedAtUtc, finishedAtUtc, unityVersion, lastFeedback;
            public string scope = "Batch Play Mode; production sandbox with camera-ray interactions and Unity physics fixtures. Tests every authored job, resource prerequisites, strict target compatibility, no ordinary rewards, one-time rare rewards, legacy persistence, physical driving, safe exits and transit. Original PlayerPrefs save is backed up and restored. Visual realism and full-scene traversal are separate checks.";
            public int completedContracts;
            public List<string> checks = new List<string>(), errors = new List<string>(), jobIds = new List<string>();
        }
    }
}
#endif
