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
using UnityEngine.Profiling;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Bounded native lifecycle stress using the saved scene and production media owners.</summary>
    [InitializeOnLoad]
    public static class SlopCityStabilitySmokeTest
    {
        private const string ActiveKey = "SlopCity.StabilitySmoke.Active";
        private const string StartedKey = "SlopCity.StabilitySmoke.Started";
        private const string SaveKey = "SlopCity.Sandbox.Progress.v1";
        private const string ReportPath = "Builds/Reports/native-stability-check.json";
        private const string RecoveryPath = "Builds/Reports/native-stability-save-recovery.json";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt report;
        private static SlopCityMediaPlayer lounge;
        private static SlopCityCityMedia city;
        private static SlopCityFirstPerson controller;
        private static Transform observer;
        private static SlopCityCityScreen[] testScreens;
        private static double enteredAt;
        private static bool startedWork;

        static SlopCityStabilitySmokeTest()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                if (change == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (change == PlayModeStateChange.EnteredEditMode)
                    Finish(new InvalidOperationException("Play Mode ended before stability checks completed."));
            };
            Application.logMessageReceived += (message, trace, type) =>
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                if (report == null) report = NewReceipt();
                if (type == LogType.Warning) { report.warnings.Add(message); return; }
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                string diagnostic = message + "\n" + trace;
                bool editorSearchStartup = message.StartsWith("ArgumentOutOfRangeException:", StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(trace) && trace.Contains("UnityEditor.Search.SearchDatabase")
                    && trace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup") && !trace.Contains("SlopCity.");
                if (editorSearchStartup) report.editorDiagnostics.Add(diagnostic);
                else report.runtimeErrors.Add(diagnostic);
            };
        }

        // Dedicated batch Editor, with graphics. Omit -quit; this runner writes its receipt and exits.
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Stability smoke tests require a dedicated batch Editor.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start outside Play Mode.");
            BackupSave();
            SessionState.SetString(StartedKey, DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(ActiveKey, true);
            report = NewReceipt(); WriteReport();
            try
            {
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                // The fixture measures bundled/native lifecycle behavior without modifying the live cache.
                foreach (SlopCityTikTokSync sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsSortMode.None)) sync.enabled = false;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static string ProjectPath(string relative) => Path.Combine(Directory.GetParent(Application.dataPath).FullName, relative);
        private static Receipt NewReceipt() => new Receipt
        {
            startedAtUtc = SessionState.GetString(StartedKey, DateTime.UtcNow.ToString("O")),
            unityVersion = Application.unityVersion, scenePath = SlopCitySceneBuilder.ScenePath,
            graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName
        };

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (report == null) report = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(report.startedAtUtc).ToUniversalTime()).TotalSeconds > 240d)
                    throw new TimeoutException("Stability fixture exceeded its four-minute startup/runtime limit.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (!startedWork)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < 1d) return;
                    startedWork = true;
                    Work.Push(Exercise());
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

        private static IEnumerator Exercise()
        {
            Application.runInBackground = true;
            Time.timeScale = 1f;
            controller = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            lounge = Object.FindFirstObjectByType<SlopCityMediaPlayer>();
            city = Object.FindFirstObjectByType<SlopCityCityMedia>();
            Check(controller && lounge && city && controller.Media == lounge, "Saved scene supplies its production player and both media owners.");
            controller.SendMessage("OnApplicationFocus", true);
            controller.SendMessage("OnApplicationPause", false);
            controller.PlayOrResume();
            controller.enabled = false;
            lounge.SendMessage("OnApplicationFocus", true);
            lounge.SendMessage("OnApplicationPause", false);
            lounge.SetSuspended(false);
            city.SendMessage("OnApplicationFocus", true);
            city.SendMessage("OnApplicationPause", false);
            city.SetSuspended(true);
            report.simulatedForegroundCallbacks = true;

            SlopCityTikTokSync sync = Object.FindFirstObjectByType<SlopCityTikTokSync>();
            Check(sync && !sync.enabled, "Live synchronization is disabled for the offline stability run.");
            yield return (IEnumerator)typeof(SlopCityTikTokSync).GetMethod("LoadBundledCatalog", PrivateInstance).Invoke(sync, null);
            city.ApplyTikTokCatalog(sync.Catalog);
            var posts = sync.Catalog.videos.ToDictionary(post => post.creator + "/" + post.id);
            testScreens = Object.FindObjectsByType<SlopCityCityScreen>(FindObjectsSortMode.None)
                .Where(screen => posts.TryGetValue(screen.ClipKey, out SlopCityTikTokVideo post) && post.kind != "slideshow" && post.duration >= 30)
                .OrderBy(screen => screen.ClipKey, StringComparer.Ordinal).Take(6).ToArray();
            Check(testScreens.Length == 6, "Six existing world displays carry distinct bundled videos.");
            var fixture = new GameObject("Stability_IsolatedScreenViews");
            fixture.transform.position = new Vector3(5000f, 1000f, 5000f);
            observer = new GameObject("Stability_Observer").transform;
            observer.SetParent(fixture.transform, false);
            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (camera.CompareTag("MainCamera")) camera.tag = "Untagged";
                camera.enabled = false;
            }
            observer.tag = "MainCamera";
            Camera view = observer.gameObject.AddComponent<Camera>();
            view.nearClipPlane = .05f; view.farClipPlane = 80f;
            for (int i = 0; i < testScreens.Length; i++)
            {
                testScreens[i].transform.SetParent(fixture.transform, false);
                testScreens[i].transform.localPosition = new Vector3((i % 3 - 1) * 3f + (i / 3) * 100f, 0, 6f);
                testScreens[i].transform.localRotation = Quaternion.identity;
                testScreens[i].transform.localScale = new Vector3(2, 1.125f, 1);
            }
            Physics.SyncTransforms();
            city.Configure(fixture.transform, observer, Shader.Find("Unlit/Texture"));
            city.SetSuspended(false);
            SetView(0);
            lounge.Play(SlopCityMediaChannel.Broadcast);
            yield return WaitForPrepared("warmup");
            yield return SettleAndSample("warmup", 0);

            // Rapid replacements deliberately include interrupted preparations. Every tenth change
            // waits for real native decode, so a run cannot pass by only issuing unobserved requests.
            for (int round = 1; round <= 3; round++)
            {
                for (int change = 0; change < 40; change++)
                {
                    lounge.Play((SlopCityMediaChannel)(change % 3));
                    report.loungeChannelChanges++;
                    SetView(change % 2);
                    report.cityViewChanges++;
                    yield return Delay(.3d);
                    if ((change + 1) % 10 == 0) yield return WaitForPrepared("round " + round + " / change " + (change + 1));
                }
                yield return SettleAndSample("round " + round, round);
            }

            yield return WaitForPrepared("before pause");
            lounge.TogglePause(); city.TogglePaused();
            yield return Delay(.2d);
            VideoPlayer[] held = OwnedPlayers();
            double[] times = held.Select(player => player.time).ToArray();
            yield return Delay(.5d);
            for (int i = 0; i < held.Length; i++)
                Check(held[i] && !held[i].isPlaying && Math.Abs(held[i].time - times[i]) < .08d, "Explicit pause holds decoder " + i + ".");
            lounge.TogglePause(); city.TogglePaused();
            yield return WaitForPrepared("resumed");
            VideoPlayer looping = lounge.GetComponentInChildren<VideoPlayer>();
            Check(looping && looping.isLooping && looping.length > 3d, "Lounge selection uses a real looping native clip.");
            double length = looping.length;
            looping.time = length - 1.5d;
            yield return Until(() => looping && looping.time > length - 3d, 5d, "Native seek did not reach the loop boundary.");
            yield return Until(() => looping && looping.isPlaying && looping.time < 3d, 8d, "Native playback did not survive its clip loop.");
            report.nativeLoopObserved = true;
            yield return SettleAndSample("after pause/resume and loop", 4);
            Check(report.loungeChannelChanges == 120 && report.cityViewChanges == 120, "Both production lifecycle paths complete 120 changes.");
            Check(report.runtimeErrors.Count == 0, "No runtime error, exception or assertion occurred during the fixture.");
        }

        private static void SetView(int cluster)
        {
            observer.localPosition = new Vector3(cluster * 100f, 0, 0);
            observer.localRotation = Quaternion.identity;
        }

        private static VideoPlayer[] OwnedPlayers() => lounge.GetComponentsInChildren<VideoPlayer>()
            .Concat(city.GetComponentsInChildren<VideoPlayer>()).ToArray();

        private static bool Prepared()
        {
            VideoPlayer[] cityPlayers = city.GetComponentsInChildren<VideoPlayer>();
            VideoPlayer[] loungePlayers = lounge.GetComponentsInChildren<VideoPlayer>();
            return cityPlayers.Length == city.DecoderLimit && loungePlayers.Length == 1
                && cityPlayers.Concat(loungePlayers).All(player => player && player.isPrepared && player.isPlaying && player.frame >= 2
                    && player.targetTexture && player.targetTexture.IsCreated());
        }

        private static IEnumerator WaitForPrepared(string label)
        {
            yield return Until(Prepared, 30d, "Native decoders did not prepare at " + label + ".");
            VideoPlayer[] players = OwnedPlayers();
            double[] times = players.Select(player => player.time).ToArray();
            yield return Until(() => players.All(player => player && player.isPlaying)
                && players.Where((player, i) => player.time > times[i] + .1d || player.time < times[i] - .1d).Count() == players.Length,
                5d, "Native playback did not advance at " + label + ".");
            foreach (VideoPlayer player in players)
                report.decodes.Add(new Decode { stage = label, name = player.name, url = player.url, seconds = player.time, frame = player.frame });
            report.preparedSamplePoints++;
        }

        private static IEnumerator SettleAndSample(string label, int round)
        {
            // Allow deferred Destroy calls and asynchronous decoder teardown to finish first.
            yield return Delay(1d);
            AsyncOperation unload = Resources.UnloadUnusedAssets();
            while (!unload.isDone) yield return null;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            yield return Delay(.3d);
            VideoPlayer[] players = OwnedPlayers();
            Check(players.Length <= city.DecoderLimit + 1, "Settled production decoder ownership remains bounded at " + label + ".");
            var sample = new ResourceSample
            {
                stage = label, round = round, capturedAtUtc = DateTime.UtcNow.ToString("O"),
                texture2DCount = Resources.FindObjectsOfTypeAll<Texture2D>().Length,
                renderTextureCount = Resources.FindObjectsOfTypeAll<RenderTexture>().Length,
                audioClipCount = Resources.FindObjectsOfTypeAll<AudioClip>().Length,
                videoPlayerCount = Resources.FindObjectsOfTypeAll<VideoPlayer>().Length,
                ownedVideoPlayers = players.Length, allocatedBytes = Profiler.GetTotalAllocatedMemoryLong(),
                activeVideoNames = players.Select(player => player.name).ToArray()
            };
            // The registered city screens are world objects, not children of the media owner.
            sample.ownedTextureIds = testScreens.Select(screen => screen.Display.sharedMaterial.mainTexture.GetInstanceID()).ToArray();
            if (report.samples.Count > 0)
            {
                ResourceSample baseline = report.samples[0];
                sample.texture2DDelta = sample.texture2DCount - baseline.texture2DCount;
                sample.renderTextureDelta = sample.renderTextureCount - baseline.renderTextureCount;
                sample.audioClipDelta = sample.audioClipCount - baseline.audioClipCount;
                sample.videoPlayerDelta = sample.videoPlayerCount - baseline.videoPlayerCount;
                sample.allocatedBytesDelta = sample.allocatedBytes - baseline.allocatedBytes;
                Check(sample.ownedTextureIds.SequenceEqual(baseline.ownedTextureIds), "Existing screen textures survive decoder handoffs at " + label + ".");
            }
            report.samples.Add(sample);
            WriteReport();
        }

        private static IEnumerator Until(Func<bool> condition, double seconds, string failure)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (!condition())
            {
                if (EditorApplication.timeSinceStartup >= deadline) throw new TimeoutException(failure + " " + (city ? city.Status : ""));
                yield return null;
            }
        }
        private static IEnumerator Delay(double seconds)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < deadline) yield return null;
        }
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            report.checks.Add(description);
        }

        private static void BackupSave()
        {
            string recovery = ProjectPath(RecoveryPath);
            if (File.Exists(recovery)) RestoreSave();
            string playback = Path.Combine(Application.persistentDataPath, "TikTok", "playback.json");
            var snapshot = new SaveSnapshot
            {
                capturedAtUtc = DateTime.UtcNow.ToString("O"), hasProgress = PlayerPrefs.HasKey(SaveKey),
                progress = PlayerPrefs.GetString(SaveKey, ""), files = new[] { SnapshotFile(playback), SnapshotFile(playback + ".tmp") }
            };
            Directory.CreateDirectory(Path.GetDirectoryName(recovery));
            File.WriteAllText(recovery, JsonUtility.ToJson(snapshot, true));
        }
        private static SavedFile SnapshotFile(string path) => new SavedFile
        { path = path, exists = File.Exists(path), bytes = File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : "" };
        private static void RestoreSave()
        {
            string recovery = ProjectPath(RecoveryPath);
            SaveSnapshot snapshot = JsonUtility.FromJson<SaveSnapshot>(File.ReadAllText(recovery));
            if (snapshot == null || snapshot.files == null || snapshot.files.Length != 2) throw new IOException("Invalid stability save recovery snapshot.");
            if (snapshot.hasProgress) PlayerPrefs.SetString(SaveKey, snapshot.progress);
            else PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
            foreach (SavedFile file in snapshot.files)
            {
                if (file.exists) { Directory.CreateDirectory(Path.GetDirectoryName(file.path)); File.WriteAllBytes(file.path, Convert.FromBase64String(file.bytes)); }
                else if (File.Exists(file.path)) File.Delete(file.path);
            }
            File.Delete(recovery);
        }

        private static void Finish(Exception exception)
        {
            if (report == null) report = NewReceipt();
            if (exception != null) report.errors.Add(exception.ToString());
            Work.Clear();
            try
            {
                // Destroy production save writers before restoration; quitting must not replace the original save.
                foreach (SlopCitySandbox sandbox in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsSortMode.None)) Object.DestroyImmediate(sandbox);
                if (controller) controller.enabled = false;
                if (city) Object.DestroyImmediate(city);
                if (lounge) lounge.Stop();
                RestoreSave();
                report.savedProgressRestored = true;
            }
            catch (Exception restoreError) { report.errors.Add("Save restoration: " + restoreError); }
            SessionState.SetBool(ActiveKey, false);
            report.finishedAtUtc = DateTime.UtcNow.ToString("O");
            report.passed = report.errors.Count == 0 && report.runtimeErrors.Count == 0 && report.savedProgressRestored;
            report.result = report.passed ? "Passed" : "Failed";
            WriteReport();
            Debug.Log("SLOP_CITY_STABILITY_SMOKE_" + report.result.ToUpperInvariant() + " " + ReportPath);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
        private static void WriteReport() => File.WriteAllText(ProjectPath(ReportPath), JsonUtility.ToJson(report, true));

        [Serializable] private sealed class Receipt
        {
            public string result = "Running", startedAtUtc, finishedAtUtc, unityVersion, scenePath, graphicsDevice;
            public bool passed, savedProgressRestored, simulatedForegroundCallbacks, nativeLoopObserved;
            public int loungeChannelChanges, cityViewChanges, preparedSamplePoints;
            public string scope = "Saved-scene native Editor media stress: three rounds of 40 lounge channel replacements and isolated viewpoints over six existing city displays, real decode sampling, pause/resume and a native loop. Existing scene is never regenerated or saved. Foreground callbacks are simulated. Bundled catalogue only; remote sync and physical speakers are outside this run.";
            public string memoryInterpretation = "Global resource counts and allocation deltas are observations after deferred destruction, UnloadUnusedAssets and GC. Editor/global cache fluctuations are not automatically treated as leaks; there is no arbitrary memory budget. Production decoder count and stable screen texture identities are checked. A passing bounded run does not prove absence of longer-term growth or a process crash on another platform.";
            public List<string> checks = new List<string>(), errors = new List<string>(), runtimeErrors = new List<string>(), editorDiagnostics = new List<string>(), warnings = new List<string>();
            public List<ResourceSample> samples = new List<ResourceSample>();
            public List<Decode> decodes = new List<Decode>();
        }
        [Serializable] private sealed class ResourceSample
        {
            public string stage, capturedAtUtc;
            public int round, texture2DCount, renderTextureCount, audioClipCount, videoPlayerCount, ownedVideoPlayers;
            public int texture2DDelta, renderTextureDelta, audioClipDelta, videoPlayerDelta;
            public long allocatedBytes, allocatedBytesDelta;
            public int[] ownedTextureIds;
            public string[] activeVideoNames;
        }
        [Serializable] private sealed class Decode { public string stage, name, url; public double seconds; public long frame; }
        [Serializable] private sealed class SaveSnapshot { public string capturedAtUtc, progress; public bool hasProgress; public SavedFile[] files; }
        [Serializable] private sealed class SavedFile { public string path, bytes; public bool exists; }
    }
}
#endif
