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
    /// <summary>Storage-boundary failure injection while production E applies and completes a parcel job.</summary>
    [InitializeOnLoad]
    public static class SlopCityPersistenceFailureSmokeTest
    {
        private const string Active = "SlopCity.PersistenceFailure.Active";
        private const string Backup = "SlopCity.PersistenceFailure.Backup";
        private const string HadSave = "SlopCity.PersistenceFailure.HadSave";
        private const string Started = "SlopCity.PersistenceFailure.Started";
        private const string SaveKey = "SlopCity.Sandbox.Progress.v1";
        private const string ReportPath = "Builds/Reports/persistence-failure-check.json";
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt report;
        private static GameObject fixture;
        private static SlopCityFirstPerson player;
        private static SlopCitySandbox sandbox;
        private static SlopCityPlayerInput input;
        private static Camera camera;
        private static string feedback;
        private static int lastFrame = -1;
        private static double enteredAt;
        private static bool exercising;

        static SlopCityPersistenceFailureSmokeTest()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Active, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (state == PlayModeStateChange.EnteredEditMode) Finish(new Exception("Persistence failure test ended early."));
            };
        }
        // Dedicated batch Editor. Omit -quit; this runner exits and restores the original save.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Persistence failure test requires a dedicated batch Editor outside Play Mode.");
            SessionState.SetBool(HadSave, PlayerPrefs.HasKey(SaveKey));
            SessionState.SetString(Backup, PlayerPrefs.GetString(SaveKey, ""));
            SessionState.SetString(Started, DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(Active, true); report = NewReceipt(); WriteReport();
            try
            {
                PlayerPrefs.SetString(SaveKey, "{\"credits\":231,\"experience\":478,\"salvage\":5,\"completed\":12,\"discovered\":[]}");
                PlayerPrefs.Save();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
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
                if ((DateTime.UtcNow - DateTime.Parse(report.startedAtUtc).ToUniversalTime()).TotalSeconds > 90)
                    throw new TimeoutException("Persistence failure test exceeded ninety seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (!exercising)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < .25) return;
                    exercising = true; Application.logMessageReceived += CaptureLog; Work.Push(Exercise());
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
            fixture = new GameObject("Persistence_Physics_Fixture");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.SetParent(fixture.transform); ground.transform.position = new Vector3(0, -.1f, 0);
            ground.transform.localScale = new Vector3(40, .2f, 40);
            CreateDesk("Pickup desk", -3); CreateDesk("Delivery desk", 3);
            Transform seat = new GameObject("SeatAnchor").transform; seat.SetParent(fixture.transform); seat.position = new Vector3(0, 1.62f, -12);
            Transform stand = new GameObject("StandAnchor").transform; stand.SetParent(fixture.transform); stand.position = new Vector3(0, .04f, -12);
            GameObject owner = new GameObject("Production_Player"); owner.transform.SetParent(fixture.transform); owner.SetActive(false);
            var capsule = owner.AddComponent<CharacterController>(); capsule.radius = .22f; capsule.height = 1.7f; capsule.center = Vector3.up * .85f;
            camera = new GameObject("View").AddComponent<Camera>(); camera.transform.SetParent(owner.transform);
            player = owner.AddComponent<SlopCityFirstPerson>(); player.Configure(seat, stand, null, null, null, camera, null);
            owner.SetActive(true);
            yield return Frames(2);
            sandbox = player.Sandbox; input = owner.GetComponent<SlopCityPlayerInput>();
            Check(sandbox && input, "Production controller initialized the sandbox and input.");
            sandbox.Initialize(owner.transform, message => feedback = message);
            player.SendMessage("OnApplicationFocus", true); player.SendMessage("OnApplicationPause", false);
            player.PlayOrResume(); input.RequestInteract();
            yield return WaitFor(() => !player.IsSeated, "Production E stands from the couch.");
            for (int i = 0; sandbox.CurrentJobId != "parcel-courier" && i < 100; i++) sandbox.RerollObjective();
            Check(sandbox.CurrentJobId == "parcel-courier", "Two real fixture desks produce a supported courier job.");
            int originalJobs = sandbox.CompletedContracts;
            AimAt(sandbox.ObjectiveTarget); input.RequestInteract();
            yield return WaitFor(() => Read<int>(sandbox, "stepIndex") == 1, "Production E picks up the parcel.");
            Check(Read<string>(sandbox, "cargo") == "SEALED PARCEL", "Pickup creates the production cargo state.");
            string validSave = PlayerPrefs.GetString(SaveKey);
            SetStore((key, json) =>
            { report.storageAttempts++; throw new PlayerPrefsException("Injected preference write denial."); });
            AimAt(sandbox.ObjectiveTarget); input.RequestInteract();
            yield return WaitFor(() => sandbox.CompletedContracts == originalJobs + 1, "Delivery E finishes despite denied storage.");
            Check(string.IsNullOrEmpty(Read<string>(sandbox, "cargo")), "The delivered parcel is consumed exactly once.");
            Check(PlayerPrefs.GetString(SaveKey) == validSave, "Write denial preserves the previous valid persisted save.");
            Check(sandbox.Credits == 231 && sandbox.Experience == 478 && sandbox.Salvage == 5,
                "Write denial retains banked progress in memory.");
            Check(report.storageAttempts == 2 && report.storageWarnings == 1, "Action and completion save retries warn once per failure streak.");
            Check(feedback.Contains("JOB COMPLETE") && feedback.Contains("Saving is unavailable"), "Existing feedback reports both completion and unavailable persistence.");
            sandbox.SendMessage("OnApplicationPause", true);
            Check(report.storageAttempts == 3 && report.storageWarnings == 1, "Background save retries safely without a duplicate warning.");
            SetStore(null);
            sandbox.SendMessage("OnApplicationPause", true);
            Check(!Read<bool>(sandbox, "persistenceUnavailable") && PlayerPrefs.GetString(SaveKey) != validSave,
                "A later normal save recovers all retained progress.");
            string recovered = PlayerPrefs.GetString(SaveKey);
            var reloadedRoot = new GameObject("RecoveredProgress"); reloadedRoot.transform.SetParent(fixture.transform);
            var reloaded = reloadedRoot.AddComponent<SlopCitySandbox>(); reloaded.Initialize(owner.transform);
            Check(reloaded.CompletedContracts == originalJobs + 1 && reloaded.Credits == 231 && reloaded.Experience == 478,
                "Recovered save reloads the completed job and preserved balances.");
            Object.DestroyImmediate(reloadedRoot);
            SetStore((key, json) => { throw new PlayerPrefsException("Second injected denial."); });
            sandbox.SendMessage("OnApplicationPause", true);
            Check(report.storageWarnings == 2 && PlayerPrefs.GetString(SaveKey) == recovered, "A new failure streak warns once and preserves the recovered save.");
            SetStore((key, json) => { throw new InvalidOperationException("Unrelated test fault."); });
            bool unrelatedEscaped = false;
            try { typeof(SlopCitySandbox).GetMethod("Save", Members).Invoke(sandbox, null); }
            catch (TargetInvocationException error) { unrelatedEscaped = error.InnerException is InvalidOperationException; }
            Check(unrelatedEscaped, "The persistence boundary does not suppress unrelated exceptions.");
            SetStore(null);
            Check(report.runtimeErrors.Count == 0, "Denied preference writes produce no unhandled runtime errors.");
        }
        private static void CreateDesk(string label, float x)
        {
            var desk = GameObject.CreatePrimitive(PrimitiveType.Cube); desk.name = label; desk.transform.SetParent(fixture.transform);
            desk.transform.position = new Vector3(x, .75f, 0); desk.transform.localScale = new Vector3(.8f, 1.5f, .8f);
            var item = desk.AddComponent<SlopCityInteractable>(); item.Configure(SlopCityInteractionKind.Terminal, label);
            item.ConfigureInteractionPoint(desk.transform.position);
        }
        private static void AimAt(SlopCityInteractable site)
        {
            Vector3 feet = new Vector3(site.InteractionPoint.x, .04f, -2);
            player.TeleportTo(feet, 0); Vector3 aim = site.InteractionPoint - camera.transform.position;
            float pitch = -Mathf.Asin(aim.normalized.y) * Mathf.Rad2Deg;
            typeof(SlopCityFirstPerson).GetField("pitch", Members).SetValue(player, pitch);
            camera.transform.localRotation = Quaternion.Euler(pitch, 0, 0); Physics.SyncTransforms();
        }
        private static IEnumerator Frames(int count)
        { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
        private static IEnumerator WaitFor(Func<bool> done, string label)
        {
            double limit = EditorApplication.timeSinceStartup + 5;
            while (!done()) { if (EditorApplication.timeSinceStartup >= limit) throw new TimeoutException(label); yield return null; }
            Check(true, label);
        }
        private static void SetStore(Action<string, string> store)
        { typeof(SlopCitySandbox).GetField("ProgressStoreForTesting", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, store); }
        private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, Members).GetValue(target);
        private static void Check(bool valid, string label)
        { if (!valid) throw new InvalidOperationException(label); report.checks++; }
        private static void CaptureLog(string message, string trace, LogType kind)
        {
            if (kind == LogType.Warning && message.StartsWith("SLOP CITY: Progress could not be saved:", StringComparison.Ordinal)) report.storageWarnings++;
            if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) report.runtimeErrors.Add(message + "\n" + trace);
        }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Active, false); Application.logMessageReceived -= CaptureLog;
            SetStore(null); Work.Clear(); exercising = false;
            if (report == null) report = NewReceipt();
            if (error != null) report.errors.Add(error.ToString());
            if (fixture) Object.DestroyImmediate(fixture);
            if (SessionState.GetBool(HadSave, false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(Backup, ""));
            else PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
            report.saveRestored = PlayerPrefs.HasKey(SaveKey) == SessionState.GetBool(HadSave, false)
                && (!PlayerPrefs.HasKey(SaveKey) || PlayerPrefs.GetString(SaveKey) == SessionState.GetString(Backup, ""));
            if (!report.saveRestored) report.errors.Add("Original save was not restored byte-for-byte.");
            report.passed = report.errors.Count == 0 && report.runtimeErrors.Count == 0;
            report.finishedAtUtc = DateTime.UtcNow.ToString("O"); WriteReport();
            Debug.Log("SLOP_CITY_PERSISTENCE_FAILURE_" + (report.passed ? "PASSED" : "FAILED")); EditorApplication.Exit(report.passed ? 0 : 1);
        }
        private static void WriteReport()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }
        [Serializable] private sealed class Receipt
        {
            public bool passed, saveRestored;
            public string startedAtUtc, finishedAtUtc, unityVersion;
            public int checks, storageAttempts, storageWarnings;
            public string scope = "Production first-person RequestInteract/E picks up and delivers a real physics-fixture parcel. Editor-only storage setter injection throws PlayerPrefsException; this establishes containment and recovery, not real-browser quota reproduction. Original valid save and banked balances remain intact; background retries recover; unrelated faults still escape. Original PlayerPrefs is restored.";
            public List<string> runtimeErrors = new List<string>(), errors = new List<string>();
        }
    }
}
#endif
