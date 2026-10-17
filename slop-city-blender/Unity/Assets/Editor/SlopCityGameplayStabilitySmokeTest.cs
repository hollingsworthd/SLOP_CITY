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
    /// <summary>Repeated production input/state transitions against the saved city, with save restoration.</summary>
    [InitializeOnLoad]
    public static class SlopCityGameplayStabilitySmokeTest
    {
        private const string Active = "SlopCity.GameplayStability.Active";
        private const string Backup = "SlopCity.GameplayStability.Backup";
        private const string HadSave = "SlopCity.GameplayStability.HadSave";
        private const string Started = "SlopCity.GameplayStability.Started";
        private const string SaveKey = "SlopCity.Sandbox.Progress.v1";
        private const string ReportPath = "Builds/Reports/gameplay-stability-check.json";
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static readonly Collider[] Overlaps = new Collider[64];
        private static readonly RaycastHit[] Hits = new RaycastHit[64];
        private static Receipt report;
        private static SlopCityFirstPerson player;
        private static SlopCitySandbox sandbox;
        private static SlopCityPlayerInput input;
        private static Camera camera;
        private static CharacterController capsule;
        private static int lastFrame = -1;
        private static double enteredAt;
        private static bool exercising;

        static SlopCityGameplayStabilitySmokeTest()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Active, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (state == PlayModeStateChange.EnteredEditMode) Finish(new Exception("Gameplay stability Play Mode ended early."));
            };
        }

        // Dedicated batch Editor only, graphics enabled, omit -quit. The runner exits itself.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Gameplay stability requires a dedicated batch Editor outside Play Mode.");
            SessionState.SetBool(HadSave, PlayerPrefs.HasKey(SaveKey));
            SessionState.SetString(Backup, PlayerPrefs.GetString(SaveKey, ""));
            SessionState.SetString(Started, DateTime.UtcNow.ToString("O"));
            for (int index = 0; index < 2; index++)
            {
                string path = PlaybackPath(index);
                SessionState.SetBool(Backup + ".PlaybackExists" + index, File.Exists(path));
                SessionState.SetString(Backup + ".PlaybackBytes" + index,
                    File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : "");
            }
            SessionState.SetBool(Active, true);
            report = NewReceipt(); WriteReport();
            try
            {
                PlayerPrefs.DeleteKey(SaveKey); PlayerPrefs.Save();
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }

        private static Receipt NewReceipt() => new Receipt
        { startedAtUtc = SessionState.GetString(Started, DateTime.UtcNow.ToString("O")), unityVersion = Application.unityVersion };

        private static void Tick()
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (report == null) report = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(report.startedAtUtc).ToUniversalTime()).TotalSeconds > 420)
                    throw new TimeoutException("Gameplay stability exceeded seven minutes.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                if (!exercising)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < 1) return;
                    exercising = true;
                    Application.logMessageReceived += CaptureLog;
                    Work.Push(Exercise());
                }
                while (Work.Count > 0)
                {
                    IEnumerator current = Work.Peek();
                    if (!current.MoveNext()) { Work.Pop(); continue; }
                    if (current.Current is IEnumerator child) { Work.Push(child); continue; }
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
            Check(player && player.Sandbox, "Saved scene initialized its production controller and sandbox.");
            sandbox = player.Sandbox; camera = player.ViewCamera;
            input = player.GetComponent<SlopCityPlayerInput>(); capsule = player.GetComponent<CharacterController>();
            player.SendMessage("OnApplicationFocus", true); player.SendMessage("OnApplicationPause", false);
            player.PlayOrResume();
            // Media decoder stress is covered separately. This test still observes all runtime exceptions.
            player.StopMedia();
            yield return Frames(2);
            Check(!player.IsPaused && player.IsSeated, "PLAY enters the authored couch state.");
            yield return PressInteract(() => !player.IsSeated, "Initial couch E restores walking.");
            UnityEngine.Random.InitState(9501);
            int initialCredits = sandbox.Credits, initialXp = sandbox.Experience;
            var seenSites = new HashSet<int>();
            string[] recipes = { "parcel-courier", "spare-part", "pressure-repair" };
            int initialInteractables = Count<SlopCityInteractable>();
            int initialTrails = Count<SlopCityRabbitTrail>();
            int initialControllers = Count<SlopCityFirstPerson>();
            int initialHud = Count<SlopCityHud>();
            report.initialGameObjects = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
            for (int round = 0; round < 12; round++)
            {
                string id = recipes[round % recipes.Length];
                for (int tries = 0; sandbox.CurrentJobId != id && tries < 1600; tries++) sandbox.RerollObjective();
                Check(sandbox.CurrentJobId == id, "Round " + round + " selects real-city recipe " + id + ".");
                SlopCityJobDefinition definition = Array.Find(SlopCityJobCatalog.All, job => job.id == id);
                int beforeJobs = sandbox.CompletedContracts;
                for (int stage = 0; stage < definition.steps.Length; stage++)
                {
                    SlopCityInteractable site = sandbox.ObjectiveTarget;
                    Check(site && SlopCityJobCatalog.Supports(site, definition.steps[stage].action), "Real job stage has a compatible device.");
                    seenSites.Add(site.GetInstanceID());
                    AimAt(site);
                    yield return PressInteract(() => sandbox.CurrentJobId != id || Read<int>(sandbox, "stepIndex") != stage,
                        id + " stage " + stage + " advances once through production E.");
                    Check(sandbox.CompletedContracts == beforeJobs + (stage + 1 == definition.steps.Length ? 1 : 0),
                        "Only the final stage completes its job.");
                }
                report.completedJobs++;
                SlopCityInteractable bench = Select(SlopCityInteractionKind.Bench, round);
                AvoidObjective(bench); AimAt(bench); Vector3 standingCamera = camera.transform.localPosition;
                yield return PressInteract(() => sandbox.IsResting, "Bench E enters real-city rest.");
                input.RequestPause(); yield return WaitFor(() => player.IsPaused, "Pause consumes real input.");
                yield return Frames(2);
                Check(sandbox.IsResting && !input.GameplayEnabled && Time.timeScale == 0, "Pause preserves the resting state and stops gameplay.");
                input.RequestPause(); yield return WaitFor(() => !player.IsPaused, "Resume consumes real input.");
                yield return PressInteract(() => !sandbox.IsResting, "Rest E returns to walking.");
                Check(Vector3.Distance(camera.transform.localPosition, standingCamera) < .001f, "Rest restores the standing camera offset.");
                SlopCityInteractable car = Select(SlopCityInteractionKind.Car, round);
                AimAt(car);
                yield return PressInteract(() => sandbox.IsDriving, "Car E enters driving.");
                Check(!capsule.enabled, "Driving owns movement without a pedestrian capsule.");
                input.SetTouchMovement(new Vector2(.2f, .6f)); yield return Frames(6); input.SetTouchMovement(Vector2.zero);
                yield return PressInteract(() => !sandbox.IsDriving, "Car E exits at a safe real-city position.");
                Check(capsule.enabled && Finite(player.transform.position), "Vehicle exit restores a finite walking state.");
                SlopCityInteractable ride = Select(SlopCityInteractionKind.Subway, round, true);
                AvoidObjective(ride); AimAt(ride);
                yield return PressInteract(() => sandbox.IsTravelling, "Station E starts travel.");
                if (round % 2 == 0)
                {
                    yield return PressInteract(() => !sandbox.IsTravelling, "E cancels an in-progress journey.");
                    report.cancelledJourneys++;
                }
                else
                {
                    sandbox.Tick(4.1f);
                    yield return Frames(1);
                    Check(!sandbox.IsTravelling && Vector3.Distance(player.transform.position, ride.Destination.position) < .15f,
                        "Transit arrival reaches the authored station destination.");
                    report.arrivedJourneys++;
                }
                Check(capsule.enabled && input.GameplayEnabled, "Travel restores walking and input.");
                player.RestartLounge(); player.StopMedia();
                yield return Frames(1);
                Check(player.IsSeated && !sandbox.IsDriving && !sandbox.IsResting && !sandbox.IsTravelling, "Restart clears active movement modes at the couch.");
                yield return PressInteract(() => !player.IsSeated, "Repeated couch E returns to walking.");
                Check(sandbox.Credits == initialCredits && sandbox.Experience == initialXp, "Routine lifecycle work preserves credits and XP.");
                Check(Count<SlopCityInteractable>() == initialInteractables && Count<SlopCityRabbitTrail>() == initialTrails
                    && Count<SlopCityFirstPerson>() == initialControllers && Count<SlopCityHud>() == initialHud,
                    "Repeated lifecycle operations do not duplicate gameplay components.");
                report.maximumGameObjects = Mathf.Max(report.maximumGameObjects, Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length);
                report.rounds++;
                WriteReport();
            }
            report.distinctJobTargets = seenSites.Count;
            Check(seenSites.Count >= 6, "Stress uses multiple physical job devices in the saved city.");
            Check(report.runtimeErrors.Count == 0, "Repeated gameplay produced no logged errors, exceptions or assertions.");
        }

        private static IEnumerator PressInteract(Func<bool> done, string label)
        { input.RequestInteract(); yield return WaitFor(done, label); report.eInputs++; }
        private static IEnumerator WaitFor(Func<bool> done, string label)
        {
            double limit = EditorApplication.timeSinceStartup + 5;
            while (!done())
            { if (EditorApplication.timeSinceStartup >= limit) throw new TimeoutException(label); yield return null; }
            Check(true, label);
        }
        private static IEnumerator Frames(int count)
        { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
        private static int Count<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsSortMode.None).Length;
        private static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z)
            && !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);
        private static void AvoidObjective(SlopCityInteractable site)
        {
            for (int i = 0; sandbox.ObjectiveTarget == site && i < 100; i++) sandbox.RerollObjective();
            Check(sandbox.ObjectiveTarget != site, "Free-use device is outside the current job stage.");
        }
        private static SlopCityInteractable Select(SlopCityInteractionKind kind, int offset, bool ride = false)
        {
            var sites = new List<SlopCityInteractable>();
            foreach (SlopCityInteractable site in Object.FindObjectsByType<SlopCityInteractable>(FindObjectsSortMode.None))
                if (site.Kind == kind && (!ride || site.Destination && site.DisplayName.StartsWith("RIDE", StringComparison.OrdinalIgnoreCase))) sites.Add(site);
            sites.Sort((a, b) => string.CompareOrdinal(a.name + a.transform.position, b.name + b.transform.position));
            Check(sites.Count > 0, "Saved city provides " + kind + " devices.");
            return sites[offset % sites.Count];
        }

        // Same conservative local capsule/center-ray probe as WorldUtilitySmokeTest.
        // This proves local interaction and uses production teleports; it does not certify global walking routes.
        private static void AimAt(SlopCityInteractable site)
        {
            Vector3 point = site.InteractionPoint;
            foreach (float radius in new[] { .65f, 1.15f, 1.75f, 2.4f }) for (int direction = 0; direction < 16; direction++)
            {
                Vector3 offset = Quaternion.Euler(0, direction * 22.5f, 0) * site.transform.forward * radius;
                if (!Physics.Raycast(new Vector3(point.x + offset.x, point.y + .85f, point.z + offset.z), Vector3.down,
                    out RaycastHit support, 4f, ~0, QueryTriggerInteraction.Ignore) || support.normal.y < .8f) continue;
                Vector3 feet = support.point + Vector3.up * .04f;
                int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * .22f, feet + Vector3.up * 1.50f,
                    .22f, Overlaps, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = count == Overlaps.Length;
                for (int i = 0; i < count; i++) if (Overlaps[i] && !Overlaps[i].transform.IsChildOf(player.transform)) blocked = true;
                if (blocked) continue;
                Vector3 eye = feet + Vector3.up * 1.62f, aim = point - eye;
                if (aim.sqrMagnitude < .01f || aim.sqrMagnitude > 3.4f * 3.4f) continue;
                count = Physics.RaycastNonAlloc(eye, aim.normalized, Hits, 3.4f, ~0, QueryTriggerInteraction.Ignore);
                if (count == Hits.Length) continue;
                Transform nearest = null; float distance = 3.4f;
                for (int i = 0; i < count; i++)
                    if (Hits[i].transform && !Hits[i].transform.IsChildOf(player.transform) && Hits[i].distance < distance)
                    { nearest = Hits[i].transform; distance = Hits[i].distance; }
                if (!nearest || nearest.GetComponentInParent<SlopCityInteractable>() != site) continue;
                player.TeleportTo(feet, Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg);
                float pitch = -Mathf.Asin(aim.normalized.y) * Mathf.Rad2Deg;
                typeof(SlopCityFirstPerson).GetField("pitch", Members).SetValue(player, pitch);
                camera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
                Physics.SyncTransforms();
                return;
            }
            throw new InvalidOperationException("No sampled clear center-ray interaction spot: " + site.name + " / " + site.DisplayName);
        }
        private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, Members).GetValue(target);
        private static void Check(bool condition, string label)
        { if (!condition) throw new InvalidOperationException(label); report.checks++; }
        private static void CaptureLog(string message, string trace, LogType kind)
        {
            if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert)
                report.runtimeErrors.Add(message + "\n" + trace);
        }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Active, false); Application.logMessageReceived -= CaptureLog;
            Work.Clear(); exercising = false;
            if (report == null) report = NewReceipt();
            if (error != null) report.errors.Add(error.ToString());
            // Destroy every initialized save writer before restoring the original PlayerPrefs value.
            foreach (SlopCitySandbox writer in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(writer);
            foreach (SlopCityCityMedia writer in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(writer);
            report.playbackRestored = true;
            for (int index = 0; index < 2; index++)
            {
                string path = PlaybackPath(index);
                if (SessionState.GetBool(Backup + ".PlaybackExists" + index, false))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, Convert.FromBase64String(SessionState.GetString(Backup + ".PlaybackBytes" + index, "")));
                }
                else if (File.Exists(path)) File.Delete(path);
                bool expected = SessionState.GetBool(Backup + ".PlaybackExists" + index, false);
                report.playbackRestored &= File.Exists(path) == expected && (!expected
                    || Convert.ToBase64String(File.ReadAllBytes(path)) == SessionState.GetString(Backup + ".PlaybackBytes" + index, ""));
            }
            if (!report.playbackRestored) report.errors.Add("Original playback files were not restored byte-for-byte.");
            if (SessionState.GetBool(HadSave, false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Backup, ""));
            else PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
            report.saveRestored = PlayerPrefs.HasKey(SaveKey) == SessionState.GetBool(HadSave, false)
                && (!PlayerPrefs.HasKey(SaveKey) || PlayerPrefs.GetString(SaveKey) == SessionState.GetString(Backup, ""));
            if (!report.saveRestored) report.errors.Add("Original progression was not restored byte-for-byte.");
            report.passed = report.errors.Count == 0 && report.runtimeErrors.Count == 0;
            report.finishedAtUtc = DateTime.UtcNow.ToString("O"); WriteReport();
            Debug.Log("SLOP_CITY_GAMEPLAY_STABILITY_" + (report.passed ? "PASSED" : "FAILED") + " " + ReportPath);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
        private static string PlaybackPath(int index) => Path.Combine(Application.persistentDataPath, "TikTok", "playback.json" + (index == 0 ? "" : ".tmp"));
        private static void WriteReport()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }
        [Serializable] private sealed class Receipt
        {
            public bool passed, saveRestored, playbackRestored;
            public string startedAtUtc, finishedAtUtc, unityVersion;
            public int randomSeed = 9501;
            public int checks, rounds, eInputs, completedJobs, cancelledJourneys, arrivedJourneys, distinctJobTargets;
            public int initialGameObjects, maximumGameObjects;
            public string scope = "Saved real city, twelve rounds of production E input, three real job recipes, bench rest, pause/resume, physical car driving/exit, subway cancel/arrival and couch restart. Local clear capsule/ray sampling and production TeleportTo position the player; global walking routes are not certified. Arrival time is advanced through production sandbox Tick; batch foreground callbacks are simulated. Original progression is restored; separate media fixtures cover decoding.";
            public List<string> runtimeErrors = new List<string>(), errors = new List<string>();
        }
    }
}
#endif
