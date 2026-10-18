#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Real native decode and control checks for the many-screen city network.</summary>
    [InitializeOnLoad]
    public static class SlopCityCityMediaSmokeTest
    {
        private const string ActiveKey = "SlopCity.CityMediaSmoke.Active";
        private const string StartedKey = "SlopCity.CityMediaSmoke.Started";
        private const string ReportPath = "Builds/Reports/city-media-playback-check.json";
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt report;
        private static SlopCityCityMedia media;
        private static SlopCityFirstPerson controller;
        private static SlopCityCityScreen[] screens;
        private static double enteredAt;
        private const string ProgressBackupKey = "SlopCity.CityMediaSmoke.ProgressBackup";
        private const string ProgressExistsKey = "SlopCity.CityMediaSmoke.ProgressExists";

        static SlopCityCityMediaSmokeTest()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                if (change == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (change == PlayModeStateChange.EnteredEditMode) Finish(new Exception("Play Mode ended before city media checks completed."));
            };
            Application.logMessageReceived += (message, trace, type) =>
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                if (report == null) report = NewReceipt();
                string diagnostic = message + "\n" + trace;
                // Unity's editor search index can fault during its delayed startup callback.
                // Classify only that identified editor-only stack; gameplay/decoder errors still fail.
                bool editorSearchStartup = message.StartsWith("ArgumentOutOfRangeException:", StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(trace)
                    && trace.Contains("UnityEditor.Search.SearchDatabase")
                    && trace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup")
                    && !trace.Contains("SlopCity.");
                if (editorSearchStartup) report.editorDiagnostics.Add(diagnostic);
                else report.runtimeErrors.Add(diagnostic);
            };
        }

        // Dedicated batch Editor only. Omit -quit and -nographics; this runner exits after its receipt.
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("City media smoke tests require a dedicated batch Editor.");
            string progressPath = Path.Combine(Application.persistentDataPath, "TikTok", "playback.json");
            SessionState.SetBool(ProgressExistsKey, File.Exists(progressPath));
            SessionState.SetString(ProgressBackupKey, File.Exists(progressPath) ? Convert.ToBase64String(File.ReadAllBytes(progressPath)) : "");
            SessionState.SetString(StartedKey, DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(ActiveKey, true);
            report = NewReceipt(); WriteReport();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start outside Play Mode.");
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                // This regression runner explicitly exercises the offline archive.
                // Live catalogue integration has its own TikTokSmokeTest runner.
                foreach (var sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsSortMode.None))
                    sync.enabled = false;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static Receipt NewReceipt() => new Receipt
        {
            startedAtUtc = SessionState.GetString(StartedKey, DateTime.UtcNow.ToString("O")),
            unityVersion = Application.unityVersion,
            scenePath = SlopCitySceneBuilder.ScenePath,
            graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName
        };

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (report == null) report = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(report.startedAtUtc).ToUniversalTime()).TotalSeconds > 240d)
                    throw new TimeoutException("City media smoke exceeded 240 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (Work.Count == 0)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < 1d) return;
                    Work.Push(ExerciseNetwork());
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

        private static IEnumerator ExerciseNetwork()
        {
            Application.runInBackground = true;
            media = Object.FindFirstObjectByType<SlopCityCityMedia>();
            controller = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            Check(media && controller && SlopCityCityMedia.Instance == media, "Saved scene initializes its city media network.");
            controller.enabled = false;
            foreach (SlopCityMediaPlayer lounge in Object.FindObjectsByType<SlopCityMediaPlayer>(FindObjectsSortMode.None)) lounge.Stop();
            screens = Object.FindObjectsByType<SlopCityCityScreen>(FindObjectsSortMode.None);
            report.screenCount = media.ScreenCount;
            Check(media.ScreenCount >= 48 && screens.Length == media.ScreenCount, "Every authored city display registers once.");
            Check(NativePlayers().Length == 0, "The start menu allocates no city video decoders.");
            SlopCityTikTokSync sync = Object.FindFirstObjectByType<SlopCityTikTokSync>();
            Check(sync && !sync.enabled, "Automatic live sync remains disabled in the offline fixture.");
            yield return (IEnumerator)typeof(SlopCityTikTokSync).GetMethod("LoadBundledCatalog", PrivateInstance).Invoke(sync, null);
            SlopCityTikTokCatalog catalog = sync.Catalog;
            media.ApplyTikTokCatalog(catalog);
            Check(catalog.videos.Length >= screens.Length && media.AssignedClipCount == screens.Length,
                "Every physical display receives a distinct post from the complete bundled catalogue.");
            Check(screens.All(item => item.ClipKey.Contains("/")) && screens.Select(item => item.Display.sharedMaterial).Distinct().Count() == screens.Length
                && screens.Select(item => item.Display.sharedMaterial.mainTexture).Distinct().Count() == screens.Length,
                "Each display owns its own material and retained picture, independent of decoder slots.");
            report.independentTextures = screens.Length;
            string[] assignments = screens.Select(item => item.ClipKey).ToArray();
            media.ApplyTikTokCatalog(catalog);
            Check(assignments.SequenceEqual(screens.Select(item => item.ClipKey)), "Refreshing unchanged metadata preserves per-screen assignments.");
            string[] preferredPosts = catalog.videos.Where(post => post.creator == "machine_publishers")
                .Select(post => post.creator + "/" + post.id).ToArray();
            Check(preferredPosts.All(key => screens.Any(item => item.ClipKey == key)),
                "Rotation fixture has every preferred-creator post already assigned.");
            SlopCityCityScreen exhausted = screens.First(item => item.Channel == 0 && item.ClipKey.StartsWith("machine_publishers/", StringComparison.Ordinal));
            string exhaustedKey = exhausted.ClipKey;
            object exhaustedState = ((IDictionary)typeof(SlopCityCityMedia).GetField("displayLookup", PrivateInstance).GetValue(media))[exhausted];
            typeof(SlopCityCityMedia).GetMethod("AssignNextPost", PrivateInstance).Invoke(media, new[] { exhaustedState });
            Check(exhausted.ClipKey != exhaustedKey && exhausted.ClipKey.StartsWith("1984drh/", StringComparison.Ordinal)
                && media.AssignedClipCount == screens.Length,
                "An exhausted preferred creator advances to an unused alternate-creator clip instead of looping its own clip.");

            var fixture = new GameObject("CityMediaSmoke_IsolatedStreet");
            fixture.transform.position = new Vector3(5000f, 1000f, 5000f);
            var observer = new GameObject("CityMediaSmoke_Observer");
            observer.transform.SetParent(fixture.transform, false);
            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (camera.CompareTag("MainCamera")) camera.tag = "Untagged";
            observer.tag = "MainCamera";
            Camera view = observer.AddComponent<Camera>();
            view.nearClipPlane = .05f; view.farClipPlane = 80f;
            Shader shader = Shader.Find("Unlit/Texture");
            var posts = catalog.videos.ToDictionary(post => post.creator + "/" + post.id);
            SlopCityCityScreen[] nearby = screens.Where(item => posts[item.ClipKey].kind != "slideshow"
                && posts[item.ClipKey].duration >= 12).Take(4).ToArray();
            Check(nearby.Length == 4, "Fixture has four distinct bundled videos long enough to inspect playback.");
            Vector3[] positions = { new Vector3(0, 0, 5), new Vector3(3, 0, 6), new Vector3(-3, 0, 6), new Vector3(0, 0, 10) };
            for (int i = 0; i < nearby.Length; i++)
            {
                nearby[i].transform.SetParent(fixture.transform, false);
                nearby[i].transform.localPosition = positions[i];
                nearby[i].transform.localRotation = Quaternion.identity;
                nearby[i].transform.localScale = new Vector3(2, 1.125f, 1);
            }
            Physics.SyncTransforms();
            media.Configure(fixture.transform, observer.transform, shader);
            media.SendMessage("OnApplicationFocus", true);
            media.SendMessage("OnApplicationPause", false);
            media.SetSuspended(false);
            yield return Until(() => NativePlayers().Length == media.DecoderLimit && AllReady(), 40d,
                "The three nearby city decoders did not produce bundled frames.");
            report.maxNativeDecoders = NativePlayers().Length;
            Check(report.maxNativeDecoders == 3 && NativePlayers().Select(player => player.url).Distinct().Count() == 3,
                "Four nearby displays use exactly three decoders, each with a different bundled clip.");
            foreach (VideoPlayer player in NativePlayers()) yield return VerifyDecodedSignal(player);
            yield return Until(() => AudibleEmitters() == 1, 3d, "The front-facing nearby display did not become the sole audible city source.");
            Check(media.AudibleScreen == nearby[0], "The closest centered display owns city audio.");
            Check(NativePlayers().All(player => player.GetTargetAudioSource(0).spatialBlend == 1f),
                "Native media retains spatial panning while shared selection controls gain.");
            media.SetVolume(0f); yield return Delay(.1d);
            Check(AudibleEmitters() == 0 && media.Volume == 0f, "User mute silences every city emitter without changing assignments.");
            media.SetVolume(.45f); yield return Until(() => AudibleEmitters() == 1, 3d, "City audio did not return after unmute.");

            observer.transform.localPosition = new Vector3(0, 0, -15);
            yield return Until(() => media.AudibleScreen == nearby[0] && CityGain() > .10f, 4d,
                "A directly aimed billboard at twenty metres did not produce useful audible gain.");
            Check(AudibleEmitters() == 1 && CityGain() <= media.Volume,
                "Direct aim extends audio beyond the nearby range without exceeding user volume or mixing broadcasts.");
            observer.transform.localRotation = Quaternion.Euler(0, 180, 0);
            yield return Delay(1.2d);
            Check(AudibleEmitters() == 0 && NativePlayers().All(player => !player.isPlaying),
                "Turning away beyond the vicinity range fades audio and releases offscreen decoders.");
            observer.transform.localPosition = Vector3.zero;
            yield return Until(() => media.AudibleScreen == nearby[0] && CityGain() > .20f, 35d,
                "An offscreen billboard within five metres did not resume nearby audio.");
            Check(AudibleEmitters() == 1 && NativePlayers().Count(player => player.isPlaying) == 1,
                "Looking away nearby keeps only the selected billboard decoding and audible.");
            observer.transform.LookAt(nearby[1].Display.bounds.center);
            yield return UntilExclusive(() => media.AudibleScreen == nearby[1], 35d,
                "Aiming at another billboard did not transfer audio focus from the closer vicinity source.");
            Check(media.AudibleScreen == nearby[1], "The directly aimed screen takes priority over a closer nearby broadcast.");
            observer.transform.localRotation = Quaternion.identity;
            yield return Until(() => media.AudibleScreen == nearby[0], 35d, "The centered display did not recover audio focus.");

            // Match the authored street billboard: 6.4 x 3.6m, centre 7.9m above
            // pavement, and a solid chassis immediately behind its front face.
            nearby[0].transform.localPosition = new Vector3(0, 7.9f, 10f);
            nearby[0].transform.localScale = new Vector3(6.4f, 3.6f, 1f);
            var chassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chassis.transform.SetParent(fixture.transform, false);
            chassis.transform.localPosition = new Vector3(0, 7.9f, 10.18f);
            chassis.transform.localScale = new Vector3(6.65f, 3.86f, .30f);
            for (int i = 1; i < nearby.Length; i++) nearby[i].transform.localPosition += new Vector3(100f, 0, 0);
            observer.transform.localPosition = new Vector3(0, 1.7f, 0);
            observer.transform.localRotation = Quaternion.Euler(0, 180, 0);
            Physics.SyncTransforms();
            yield return Until(() => media.AudibleScreen == nearby[0] && CityGain() > .09f, 35d,
                "An elevated street billboard was too quiet or incorrectly occluded by its own backing.");
            Check(AudibleEmitters() == 1, "Street-height vicinity audio works for an elevated authored-size billboard while looking away.");
            Object.Destroy(chassis);
            nearby[0].transform.localPosition = positions[0];
            nearby[0].transform.localScale = new Vector3(2, 1.125f, 1);
            for (int i = 1; i < nearby.Length; i++) nearby[i].transform.localPosition = positions[i];
            observer.transform.localPosition = Vector3.zero;
            observer.transform.localRotation = Quaternion.identity;
            Physics.SyncTransforms();
            yield return Until(() => media.AudibleScreen == nearby[0] && AllReady(), 35d,
                "Nearby visible playback did not recover after the elevated billboard fixture.");

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(fixture.transform, false);
            wall.transform.localPosition = new Vector3(0, 0, 2.5f);
            wall.transform.localScale = new Vector3(20, 20, .25f);
            Physics.SyncTransforms(); yield return Delay(.7d);
            Check(AudibleEmitters() == 0, "A solid wall between the observer and all displays silences their audio.");
            Object.Destroy(wall); yield return Delay(.7d);
            yield return Until(() => AudibleEmitters() == 1, 3d, "Audio did not return after removing the obstruction.");

            // A nearer lounge TV is deliberately colliderless, so both networks contend for focus.
            var television = GameObject.CreatePrimitive(PrimitiveType.Quad);
            television.transform.SetParent(fixture.transform, false);
            television.transform.localPosition = new Vector3(1, 0, 3);
            Object.Destroy(television.GetComponent<Collider>());
            var loungeRoot = new GameObject("CityMediaSmoke_Lounge");
            loungeRoot.transform.SetParent(fixture.transform, false);
            var loungePlayer = loungeRoot.AddComponent<SlopCityMediaPlayer>();
            loungePlayer.Configure(television.GetComponent<Renderer>(), Array.Empty<Renderer>(), television.transform,
                television.transform, shader);
            loungePlayer.SetSuspended(false);
            loungePlayer.Play(SlopCityMediaChannel.Broadcast);
            yield return Until(() => loungePlayer.IsPlaying, 35d, "Bundled lounge video did not start.");
            AudioSource loungeAudio = loungePlayer.GetComponentInChildren<AudioSource>();
            yield return UntilExclusive(() => loungeAudio.volume > .01f, 4d,
                "The stronger nearby lounge source did not acquire focus.", loungeAudio);
            Check(AudibleEmitters() == 0 && loungeAudio.volume > 0f, "Lounge and city never mix two audible media tracks.");
            loungePlayer.Stop();
            yield return Until(() => AudibleEmitters() == 1, 4d, "City audio did not recover after the lounge stopped.");
            Object.Destroy(loungeRoot); Object.Destroy(television);

            string previousKey = nearby[0].ClipKey;
            object state = ((IDictionary)typeof(SlopCityCityMedia).GetField("displayLookup", PrivateInstance).GetValue(media))[nearby[0]];
            object slot = state.GetType().GetField("slot").GetValue(state);
            Check(slot != null, "The focused screen owns a decoder slot before advancing.");
            typeof(SlopCityCityMedia).GetMethod("AdvanceScreen", PrivateInstance).Invoke(media, new[] { slot });
            Check(nearby[0].ClipKey != previousKey && media.AssignedClipCount == screens.Length,
                "A completed clip advances only its own display and reserves another distinct post.");
            yield return Until(() => (bool)slot.GetType().GetField("ready").GetValue(slot), 35d, "The next assigned post did not become ready.");
            string beforeTune = nearby[0].ClipKey;
            Material retained = nearby[0].Display.sharedMaterial;
            media.TuneScreen(nearby[0].transform);
            Check(beforeTune != nearby[0].ClipKey && retained == nearby[0].Display.sharedMaterial
                && media.AssignedClipCount == screens.Length, "Tuning one display selects another distinct post while retaining its own texture.");
            yield return Delay(1d);
            media.TogglePaused(); yield return VerifyHeld("Manual media pause holds every decoder.");
            Check(AudibleEmitters() == 0, "Manual pause also silences the selected vicinity broadcast.");
            media.SetSuspended(true); media.SetSuspended(false);
            Check(media.IsPaused, "Manual pause intent survives a menu suspension round trip.");
            yield return VerifyHeld("Menu resume preserves the user's manual media pause.");
            media.TogglePaused();
            yield return Delay(1d);
            media.SendMessage("OnApplicationFocus", false);
            yield return VerifyHeld("Application focus loss holds billboard playback.");
            Check(AudibleEmitters() == 0, "Application focus loss silences billboard audio.");
            media.SendMessage("OnApplicationFocus", true);
            yield return Until(() => AudibleEmitters() == 1, 4d, "Billboard audio did not recover after application focus returned.");
            double retainedVariance = PixelVariance(nearby[1].Display.sharedMaterial.mainTexture as RenderTexture);
            observer.transform.localRotation = Quaternion.Euler(0, 180, 0);
            yield return Delay(.8d);
            yield return Until(() => AudibleEmitters() == 1, 35d, "Nearby audio did not resume after the user's manual pause ended.");
            Check(NativePlayers().Count(player => player.isPlaying) == 1,
                "Turning away nearby retains one audible decoder and releases the other offscreen video playback.");
            Check(PixelVariance(nearby[1].Display.sharedMaterial.mainTexture as RenderTexture) > .0001d && retainedVariance > .0001d,
                "A released decoder leaves the display's last nonblank picture intact.");
            observer.transform.localPosition = new Vector3(0, 0, -100);
            observer.transform.localRotation = Quaternion.identity;
            yield return Delay(.7d);
            Check(AudibleEmitters() == 0 && NativePlayers().Length <= 3 && NativePlayers().All(player => !player.isPlaying && !player.targetTexture),
                "Distant displays retain their pictures without active decoder uploads.");
            Check(report.runtimeErrors.Count == 0, "No unexpected runtime errors occurred during city media checks.");
        }

        private const System.Reflection.BindingFlags PrivateInstance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        private static int AudibleEmitters() => media.GetComponentsInChildren<AudioSource>().Count(source => source.volume > .001f);
        private static float CityGain() => media.GetComponentsInChildren<AudioSource>().Select(source => source.volume).DefaultIfEmpty(0f).Max();

        private static IEnumerator UntilExclusive(Func<bool> condition, double seconds, string failure, AudioSource lounge = null)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            do
            {
                if (AudibleEmitters() + (lounge && lounge.volume > .001f ? 1 : 0) > 1)
                    throw new InvalidOperationException("A focus transition mixed multiple audible media sources.");
                if (condition()) yield break;
                if (EditorApplication.timeSinceStartup >= deadline) throw new TimeoutException(failure);
                yield return null;
            } while (true);
        }

        private static IEnumerator VerifyDecodedSignal(VideoPlayer player)
        {
            var signal = new SignalEvidence { name = player.name, source = player.url, firstFrame = player.frame, firstSeconds = player.time };
            report.signals.Add(signal);
            double until = EditorApplication.timeSinceStartup + 10d;
            do
            {
                signal.centerPixelVariance = PixelVariance(player.targetTexture);
                if (signal.centerPixelVariance > .0001d) break;
                yield return Delay(.2d);
            } while (EditorApplication.timeSinceStartup < until);
            yield return Until(() => player.isPlaying && player.frame > signal.firstFrame && player.time > signal.firstSeconds + .15d,
                6d, "Native frame/time did not advance for " + player.name + ".");
            signal.lastFrame = player.frame; signal.lastSeconds = player.time;
            signal.durationSeconds = player.length;
            Check(signal.centerPixelVariance > .0001d, player.name + " decodes nonuniform visible texture pixels.");
            Check(player.audioTrackCount > 0 && player.audioOutputMode == VideoAudioOutputMode.AudioSource
                && player.GetTargetAudioSource(0), player.name + " routes its native audio track through its own spatial emitter controlled by exclusive audio focus.");
            Check(player.url.StartsWith("file:", StringComparison.OrdinalIgnoreCase), player.name + " is accurately identified as a bundled file source.");
        }

        private static bool HasAncestor(Transform child, string prefix)
        {
            for (Transform cursor = child; cursor; cursor = cursor.parent)
                if (cursor.name.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        private static string HierarchyPath(Transform item)
        {
            string path = item.name;
            while (item.parent) { item = item.parent; path = item.name + "/" + path; }
            return path;
        }

        private static VideoPlayer[] NativePlayers() => media.GetComponentsInChildren<VideoPlayer>();
        private static bool AllReady()
        {
            foreach (VideoPlayer player in NativePlayers())
                if (!player.isPrepared || !player.isPlaying || player.frame < 3) return false;
            return true;
        }

        private static IEnumerator VerifyHeld(string description)
        {
            yield return Delay(.2d);
            VideoPlayer[] players = NativePlayers();
            var times = new double[players.Length];
            for (int i = 0; i < players.Length; i++) times[i] = players[i].time;
            yield return Delay(.6d);
            for (int i = 0; i < players.Length; i++)
                Check(!players[i].isPlaying && Math.Abs(players[i].time - times[i]) < .08d, description + " / " + players[i].name);
        }

        private static IEnumerator VerifyAdvancing(string description)
        {
            VideoPlayer[] players = NativePlayers();
            var times = new double[players.Length];
            for (int i = 0; i < players.Length; i++) times[i] = players[i].time;
            yield return Until(() =>
            {
                for (int i = 0; i < players.Length; i++)
                    if (!players[i].isPlaying || players[i].time <= times[i] + .2d) return false;
                return true;
            }, 6d, description);
            Check(true, description);
        }

        private static IEnumerator Until(Func<bool> condition, double seconds, string failure)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (!condition())
            {
                if (EditorApplication.timeSinceStartup >= deadline) throw new TimeoutException(failure + " " + (media ? media.Status : ""));
                yield return null;
            }
        }

        private static IEnumerator Delay(double seconds)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < deadline) yield return null;
        }

        private static double PixelVariance(RenderTexture source)
        {
            if (!source || !source.IsCreated()) return 0d;
            RenderTexture previous = RenderTexture.active;
            RenderTexture sample = RenderTexture.GetTemporary(64, 36, 0, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(64, 36, TextureFormat.RGB24, false, true);
            try
            {
                Graphics.Blit(source, sample); RenderTexture.active = sample;
                pixels.ReadPixels(new Rect(0, 0, 64, 36), 0, 0); pixels.Apply();
                Color32[] data = pixels.GetPixels32();
                double sum = 0d, squared = 0d; int count = 0;
                for (int y = 4; y < 32; y++) for (int x = 26; x < 38; x++)
                {
                    Color32 color = data[y * 64 + x];
                    double luma = (color.r + color.g + color.b) / 765d;
                    sum += luma; squared += luma * luma; count++;
                }
                return squared / count - Math.Pow(sum / count, 2d);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(sample); Object.DestroyImmediate(pixels);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            report.checks.Add(message);
        }

        private static void Finish(Exception exception)
        {
            SessionState.SetBool(ActiveKey, false); Work.Clear();
            if (media) media.enabled = false;
            string progressPath = Path.Combine(Application.persistentDataPath, "TikTok", "playback.json");
            if (SessionState.GetBool(ProgressExistsKey, false))
                File.WriteAllBytes(progressPath, Convert.FromBase64String(SessionState.GetString(ProgressBackupKey, "")));
            else if (File.Exists(progressPath)) File.Delete(progressPath);
            if (report == null) report = NewReceipt();
            if (exception != null) report.errors.Add(exception.ToString());
            report.passed = report.errors.Count == 0 && report.runtimeErrors.Count == 0;
            report.result = report.passed ? "Passed" : "Failed";
            report.finishedAtUtc = DateTime.UtcNow.ToString("O");
            report.audioVerification = "Native audio tracks, useful aimed and vicinity gain, elevated billboard geometry, occlusion, pause/mute and lounge/city transition exclusivity checked through runtime state and gain; speaker output was not listened to.";
            WriteReport();
            Debug.Log("SLOP_CITY_CITY_MEDIA_SMOKE_" + report.result.ToUpperInvariant() + " " + ReportPath);
            if (exception != null) Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1);
        }

        private static void WriteReport()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }

        [Serializable] private sealed class Receipt
        {
            public string result = "Running";
            public bool passed;
            public string startedAtUtc, finishedAtUtc, unityVersion, scenePath, graphicsDevice;
            public string scope = "Batch Editor checks of independent city-screen assignments and retained textures, three pooled native decoders, decoded pixels, clip rotation, tuning, mute/pause, twenty-metre direct aim, offscreen vicinity and elevated billboard audio, occlusion, and exclusive lounge/city focus transitions. No live UI interaction or speaker listening.";
            public string catalogVerification = "Production bundled-catalogue loader and resolvers exercised with automatic live synchronization disabled. Every authored display receives a distinct bundled post; this run does not test remote TikTok refresh.";
            public string audioVerification;
            public int screenCount, independentTextures, maxNativeDecoders;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> runtimeErrors = new List<string>();
            public List<string> editorDiagnostics = new List<string>();
            public List<string> warnings = new List<string>();
            public List<SignalEvidence> signals = new List<SignalEvidence>();
        }

        [Serializable] private sealed class SignalEvidence
        {
            public string name, source;
            public long firstFrame, lastFrame;
            public double firstSeconds, lastSeconds, durationSeconds, centerPixelVariance;
        }
    }
}
#endif
