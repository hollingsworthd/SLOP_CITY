#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Exercise phone-budget selection with real production city decoders and image ownership.</summary>
    [InitializeOnLoad]
    public static class SlopCityMobileMediaBudgetSmokeTest
    {
        private const string ActiveKey = "SlopCity.MobileMediaBudgetSmoke.Active";
        private const string StartedKey = "SlopCity.MobileMediaBudgetSmoke.Started";
        private const string ReportPath = "Builds/Reports/mobile-media-budget-check.json";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt report;
        private static GameObject owner;
        private static string directory;
        private static double enteredAt;
        private static bool started;

        static SlopCityMobileMediaBudgetSmokeTest()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                if (change == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (change == PlayModeStateChange.EnteredEditMode) Finish(new Exception("Play Mode ended before resource checks completed."));
            };
            Application.logMessageReceived += (message, trace, type) =>
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                if (report == null) report = NewReceipt();
                if (type == LogType.Warning) { report.warnings.Add(message); return; }
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                if (trace != null && trace.Contains("UnityEditor.Search.SearchDatabase") && !trace.Contains("SlopCity."))
                    report.editorDiagnostics.Add(message + "\n" + trace);
                else report.runtimeErrors.Add(message + "\n" + trace);
            };
        }

        // Dedicated graphics-enabled batch Editor; omit -quit and -nographics.
        // Uses isolated validation saves, bundled videos and a private PNG; no public network.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Mobile media budget smoke requires a dedicated batch Editor outside Play Mode.");
            SessionState.SetString(StartedKey, DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(ActiveKey, true);
            report = NewReceipt(); WriteReport();
            try { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); EditorApplication.EnterPlaymode(); }
            catch (Exception exception) { Finish(exception); }
        }
        private static Receipt NewReceipt() => new Receipt { startedAtUtc = SessionState.GetString(StartedKey, DateTime.UtcNow.ToString("O")), unityVersion = Application.unityVersion };
        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (report == null) report = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(report.startedAtUtc).ToUniversalTime()).TotalSeconds > 150d) throw new TimeoutException("Mobile media budget fixture exceeded 150 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (!started) { if (EditorApplication.timeSinceStartup - enteredAt < .5d) return; started = true; Work.Push(Exercise()); }
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
        private static SlopCityCityMedia city;
        private static SlopCityCityScreen[] screens;
        private static GameObject photoOwner;
        private static readonly List<UnityEngine.Object> cleanupObjects = new List<UnityEngine.Object>();
        private static IEnumerator Exercise()
        {
            if (!Application.companyName.StartsWith("SlopCityValidation", StringComparison.Ordinal))
                throw new InvalidOperationException("Use a cloned project with an isolated SlopCityValidation company identity.");
            directory = Path.Combine(Path.GetTempPath(), "slop-mobile-media-budget-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            owner = new GameObject("MobileMediaBudget_PrivateStreet");
            owner.transform.position = new Vector3(5000, 1000, 5000);
            var observer = new GameObject("Observer"); observer.transform.SetParent(owner.transform, false);
            observer.tag = "MainCamera";
            Camera camera = observer.AddComponent<Camera>(); camera.nearClipPlane = .05f; camera.farClipPlane = 80f;
            observer.AddComponent<AudioListener>();
            SlopCityPlayerInput input = observer.AddComponent<SlopCityPlayerInput>();
            Shader shader = Shader.Find("Unlit/Texture"); Check(shader && shader.isSupported, "Fixture supplies the production display shader.");
            screens = new SlopCityCityScreen[4];
            Vector3[] positions = { new Vector3(0, 0, 5), new Vector3(3, 0, 6), new Vector3(-3, 0, 6), new Vector3(0, 0, 10) };
            for (int index = 0; index < screens.Length; index++)
            {
                GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
                surface.transform.SetParent(owner.transform, false); surface.transform.localPosition = positions[index];
                surface.transform.localScale = index == 0 ? new Vector3(2, 3.5556f, 1) : new Vector3(2, 1.125f, 1);
                screens[index] = surface.AddComponent<SlopCityCityScreen>(); screens[index].Configure(surface.GetComponent<Renderer>(), 2);
                if (index == 0) screens[index].ConfigureFeaturedPost(SlopCityFeaturedBillboard.Creator, SlopCityFeaturedBillboard.PostId, -1f, 0f);
            }
            var network = new GameObject("Network"); network.transform.SetParent(owner.transform, false);
            city = network.AddComponent<SlopCityCityMedia>(); city.Configure(owner.transform, observer.transform, shader);
            SlopCityTikTokSync sync = network.AddComponent<SlopCityTikTokSync>(); sync.enabled = false;
            yield return null; yield return null;
            Check(city.ScreenCount == 4 && city.DecoderLimit == 3, "Native startup registers four physical devices with its unchanged three-slot budget.");
            yield return (IEnumerator)typeof(SlopCityTikTokSync).GetMethod("LoadBundledCatalog", Private).Invoke(sync, null);
            SlopCityTikTokVideo featured = sync.Catalog.videos.Single(post => post.creator == SlopCityFeaturedBillboard.Creator && post.id == SlopCityFeaturedBillboard.PostId);
            var videos = new[] { featured }.Concat(sync.Catalog.videos.Where(post => post.kind != "slideshow" && post.duration >= 60 && post.id != featured.id).Take(3)).ToArray();
            Check(videos.Length == 4, "Verified bundle supplies the exact featured clip and three distinct long videos.");
            city.ApplyTikTokCatalog(new SlopCityTikTokCatalog { videos = videos });
            string[] assignments = screens.Select(screen => screen.ClipKey).ToArray();
            Texture[] textures = screens.Select(screen => screen.Display.sharedMaterial.mainTexture).ToArray();
            Check(assignments.Distinct().Count() == 4 && textures.Distinct().Count() == 4, "All four devices retain distinct assigned clips and pictures.");
            Physics.SyncTransforms(); city.SetSuspended(false);
            yield return Until(() => ActivePlayers().Length == 3, 40d, "Native three-slot pool did not decode three bundled videos.");
            Check(ActivePlayers().Select(player => player.url).Distinct().Count() == 3, "Native budget decodes three independent videos.");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(owner.transform, false); wall.transform.localPosition = new Vector3(-1.5f, 0, 3);
            wall.transform.localScale = new Vector3(1.5f, 5, .3f); Physics.SyncTransforms();
            RefreshInterest();
            object blocked = ((IEnumerable)typeof(SlopCityCityMedia).GetField("displays", Private).GetValue(city)).Cast<object>()
                .Single(display => (SlopCityCityScreen)display.GetType().GetField("screen").GetValue(display) == screens[2]);
            Check((float)blocked.GetType().GetField("priority").GetValue(blocked) == 0f
                && (float)blocked.GetType().GetField("audioScore").GetValue(blocked) == 0f,
                "A screen inside the camera frustum but behind a solid wall is ineligible for visual decoding and audio.");
            Object.DestroyImmediate(wall); Physics.SyncTransforms(); RefreshInterest();
            SetBudget(true);
            Check(city.DecoderLimit == 3, "Browser with desktop controls retains the three-slot budget.");
            typeof(SlopCityPlayerInput).GetField("forceTouchControlsForTesting", Private).SetValue(input, true);
            SetBudget(false);
            Check(input.UseTouchControls && city.DecoderLimit == 3, "Native touch controls retain three slots.");
            SetBudget(true);
            Check(city.DecoderLimit == 1, "Browser touch budget derives from the same input decision that creates mobile controls.");
            yield return Until(() => ActivePlayers().Length == 1 && city.AudibleScreen == screens[0], 20d, "Phone budget did not retain only the centered featured source.");
            Check(OwnedSlots() == 1 && screens.Select(screen => screen.ClipKey).SequenceEqual(assignments), "Phone cap releases two slots without changing any device assignment.");
            Check(owner.GetComponentsInChildren<VideoPlayer>().Where(player => !player.isPlaying).All(player => !player.isPrepared && player.targetTexture == null), "Unused native decoder sessions stop and detach from physical display textures.");
            Check(screens.Select(screen => screen.Display.sharedMaterial.mainTexture).SequenceEqual(textures), "Released devices retain their original last-frame RenderTextures.");
            VideoPlayer player = ActivePlayers().Single();
            Check(player.isLooping && player.url.EndsWith(featured.bundledVideoFile, StringComparison.Ordinal), "Phone's selected featured source keeps the exact clip in continuous native loop mode.");
            double time = player.time;
            yield return Delay(1d);
            Check(ActivePlayers().Single() == player && player.time > time, "Stable featured focus keeps the same decoder playing continuously without reprepare.");
            city.TogglePaused(); yield return Delay(.4d);
            Check(ActivePlayers().Length == 0 && OwnedSlots() == 1, "Manual pause stops playback while retaining the single selected device.");
            city.TogglePaused(); yield return Until(() => ActivePlayers().Length == 1, 3d, "Selected phone source did not resume after manual pause.");
            city.SendMessage("OnApplicationFocus", false); yield return Delay(.4d);
            Check(ActivePlayers().Length == 0, "Focus loss pauses the selected source.");
            city.SendMessage("OnApplicationFocus", true); yield return Until(() => ActivePlayers().Length == 1, 3d, "Selected source did not resume after focus return.");
            yield return Until(() => city.AudibleScreen == screens[0] && ActivePlayers().Single().GetTargetAudioSource(0).volume > .15f, 4d, "Featured audio did not settle before handoff.");
            VideoPlayer previousPlayer = ActivePlayers().Single();
            AudioSource previousAudio = previousPlayer.GetTargetAudioSource(0);
            float previousGain = previousAudio.volume;
            for (int sweep = 0; sweep < 12; sweep++)
            {
                observer.transform.LookAt(screens[1 + sweep % 2].Display.bounds.center); Physics.SyncTransforms(); RefreshInterest();
                Check(ActivePlayers().Single() == previousPlayer && previousPlayer.targetTexture == textures[0] && OwnedSlots() == 1,
                    "Brief alternating aim " + sweep + " retains the already playing phone decoder and its display.");
            }
            observer.transform.LookAt(screens[1].Display.bounds.center); Physics.SyncTransforms();
            RefreshInterest();
            Check(previousPlayer.isPlaying && previousAudio.volume == previousGain && OwnedSlots() == 1,
                "Phone focus confirmation retains the old audible decoder before beginning its existing fade-down.");
            // Simulate elapsed confirmation deterministically, so no frame can finish
            // the fade between checking target acceptance and inspecting its old gain.
            typeof(SlopCityCityMedia).GetField("mobileCandidateSince", Private).SetValue(city, Time.unscaledTime - .41f);
            RefreshInterest();
            typeof(SlopCityCityMedia).GetMethod("UpdateAudio", Private).Invoke(city, null);
            Check(previousPlayer.isPlaying && previousAudio.volume < previousGain && previousAudio.volume >= 0f && OwnedSlots() == 1,
                "Old gain ramps down while its decoder is still playing, before Stop or rebind.");
            yield return Until(() => city.AudibleScreen == screens[1] && ActivePlayers().Length == 1, 30d, "Direct aim did not transfer the phone's sole slot and audio to another device.");
            Check(OwnedSlots() == 1 && ActivePlayers().Single().targetTexture == textures[1], "Aim handoff binds the sole decoder to the requested device texture.");
            Check(screens.Select(screen => screen.ClipKey).SequenceEqual(assignments) && screens.Select(screen => screen.Display.sharedMaterial.mainTexture).SequenceEqual(textures), "Handoff preserves independent assignments and every retained picture.");
            observer.transform.localRotation = Quaternion.identity; Physics.SyncTransforms();
            yield return Until(() => city.AudibleScreen == screens[0] && ActivePlayers().Length == 1, 30d, "Featured source did not reacquire the sole slot.");
            Check(ActivePlayers().Single().isLooping && ActivePlayers().Single().url.EndsWith(featured.bundledVideoFile, StringComparison.Ordinal), "Featured reacquisition preserves its exact continually looping programming.");
            Check(report.maxPhonePlayers <= 1, "All sampled phone prepares and handoffs stay within one active city decoder.");
            city.SetSuspended(true); yield return Delay(.4d);
            yield return ExercisePhotoOwnership(shader);
            yield return ExercisePcmUpload();
        }
        private static void RefreshInterest() => typeof(SlopCityCityMedia).GetMethod("RefreshScreenInterest", Private).Invoke(city, null);
        private static void SetBudget(bool browserBuild)
        {
            typeof(SlopCityCityMedia).GetMethod("RefreshDecoderBudget", Private).Invoke(city, new object[] { browserBuild });
            typeof(SlopCityCityMedia).GetMethod("RefreshScreenInterest", Private).Invoke(city, null);
        }
        private static int OwnedSlots() => ((Array)typeof(SlopCityCityMedia).GetField("channels", Private).GetValue(city)).Cast<object>().Count(channel => channel != null && channel.GetType().GetField("owner").GetValue(channel) != null);
        private static VideoPlayer[] ActivePlayers() => owner.GetComponentsInChildren<VideoPlayer>().Where(player => player.isPrepared && player.isPlaying).ToArray();
        private static IEnumerator ExercisePhotoOwnership(Shader shader)
        {
            string png = Path.Combine(directory, "private-photo.png");
            var inputPhoto = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            try { inputPhoto.SetPixels(Enumerable.Repeat(Color.magenta, 16 * 16).ToArray()); inputPhoto.Apply(); File.WriteAllBytes(png, inputPhoto.EncodeToPNG()); }
            finally { Object.DestroyImmediate(inputPhoto); }
            photoOwner = new GameObject("MobileMediaBudget_ImageDecoder");
            SlopCityCityMedia decoder = photoOwner.AddComponent<SlopCityCityMedia>(); decoder.enabled = false;
            Type channelType = typeof(SlopCityCityMedia).GetNestedType("Channel", BindingFlags.NonPublic);
            object channel = Activator.CreateInstance(channelType, true);
            var texture = new RenderTexture(64, 36, 0, RenderTextureFormat.ARGB32); texture.Create(); cleanupObjects.Add(texture);
            channelType.GetField("texture").SetValue(channel, texture);
            channelType.GetField("slideshow").SetValue(channel, true);
            channelType.GetField("pendingVideo").SetValue(channel, new SlopCityTikTokVideo { creator = "machine_publishers", id = "9999999999999999999", kind = "slideshow", imageUrls = new[] { "", "" } });
            int before = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            var load = (IEnumerator)typeof(SlopCityCityMedia).GetMethod("LoadImage", Private).Invoke(decoder, new object[] { channel, 0, 1, new Uri(png).AbsoluteUri });
            object owned = typeof(SlopCityCityMedia).GetField("mediaCoroutines", Private).GetValue(decoder);
            owned.GetType().GetMethod("Start", Private).Invoke(owned, new object[] { decoder, load });
            yield return Until(() => (bool)channelType.GetField("ready").GetValue(channel), 10d, "Production local slideshow image did not decode.");
            yield return null; yield return null;
            Check(Resources.FindObjectsOfTypeAll<Texture2D>().Length == before, "Completed slideshow draw releases its decoded source Texture2D instead of retaining a second picture.");
            var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = texture; readback.ReadPixels(new Rect(32, 18, 1, 1), 0, 0); readback.Apply();
                Color pixel = readback.GetPixel(0, 0);
                Check(pixel.r > .8f && pixel.b > .8f && pixel.g < .1f, "The physical device RenderTexture retains the decoded picture after source texture destruction.");
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(readback); }
            Object.DestroyImmediate(photoOwner); photoOwner = null;
        }
        private static IEnumerator ExercisePcmUpload()
        {
            const int frames = 8192 * 9 + 17, channels = 2, offset = 12;
            var bytes = new byte[offset + frames * channels * 2];
            for (int sample = 0; sample < frames * channels; sample++)
            {
                short value = (short)((sample % 65536) - 32768);
                bytes[offset + sample * 2] = (byte)value;
                bytes[offset + sample * 2 + 1] = (byte)(value >> 8);
            }
            MethodInfo writer = typeof(SlopCityCityMedia).GetMethod("WritePhotoPcm", BindingFlags.Static | BindingFlags.NonPublic);
            int writes = 0, yields = 0; bool complete = false, current = true;
            Func<float[], int, bool> upload = (samples, start) =>
            {
                writes++;
                Check(start == 0 && samples.Length == frames * channels, "Web PCM writer uploads the complete interleaved track at frame zero.");
                Check(samples.Select((sample, index) => sample == (short)((index % 65536) - 32768) / 32768f).All(equal => equal),
                    "The single Web PCM upload preserves every signed stereo sample, including the final partial block.");
                return true;
            };
            IEnumerator routine = (IEnumerator)writer.Invoke(null, new object[] { bytes, channels, frames, offset, true,
                (Func<bool>)(() => current), upload, (Action)(() => complete = true) });
            while (routine.MoveNext()) { yields++; yield return null; }
            Check(complete && writes == 1 && yields >= 2, "Web PCM decode yields in bounded batches and calls SetData exactly once for a multi-block track.");
            writes = 0; complete = false;
            routine = (IEnumerator)writer.Invoke(null, new object[] { bytes, channels, frames, offset, true,
                (Func<bool>)(() => current), upload, (Action)(() => complete = true) });
            Check(routine.MoveNext(), "Long Web PCM decode reaches a cancellation boundary before any full-track upload.");
            current = false;
            Check(!routine.MoveNext() && writes == 0 && !complete, "Cancelling Web PCM decode at a yield prevents stale SetData and ownership completion.");
            current = true; complete = false;
            routine = (IEnumerator)writer.Invoke(null, new object[] { bytes, channels, frames, offset, true,
                (Func<bool>)(() => current), (Func<float[], int, bool>)((samples, start) => false), (Action)(() => complete = true) });
            while (routine.MoveNext()) yield return null;
            Check(!complete, "A rejected Web PCM upload cannot complete or transfer playback ownership.");
            current = true; writes = 0; complete = false;
            int nextFrame = 0;
            Func<float[], int, bool> nativeUpload = (samples, start) =>
            {
                Check(start == nextFrame && samples.Length <= 8192 * channels, "Native PCM upload retains bounded sequential frame blocks.");
                for (int sample = 0; sample < samples.Length; sample++)
                    if (samples[sample] != (short)(((start * channels + sample) % 65536) - 32768) / 32768f)
                        throw new InvalidOperationException("Native PCM sample changed while preserving its block path.");
                nextFrame += samples.Length / channels; writes++; return true;
            };
            routine = (IEnumerator)writer.Invoke(null, new object[] { bytes, channels, frames, offset, false,
                (Func<bool>)(() => current), nativeUpload, (Action)(() => complete = true) });
            while (routine.MoveNext()) yield return null;
            Check(complete && writes == 10 && nextFrame == frames, "Native PCM chunks cover the complete track without adopting the Web full-sample allocation.");
        }
        private static IEnumerator Until(Func<bool> ready, double seconds, string failure)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (!ready() && EditorApplication.timeSinceStartup < deadline)
            {
                if (owner && city && city.DecoderLimit == 1)
                {
                    report.maxPhonePlayers = Math.Max(report.maxPhonePlayers, ActivePlayers().Length);
                    if (report.maxPhonePlayers > 1) throw new InvalidOperationException("Phone handoff overlapped more than one active city decoder.");
                }
                yield return null;
            }
            if (!ready()) throw new TimeoutException(failure);
        }
        private static IEnumerator Delay(double seconds)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < deadline) yield return null;
        }
        private static void Check(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); report.checks.Add(description); }
        private static void Finish(Exception exception)
        {
            Work.Clear();
            if (report == null) report = NewReceipt();
            if (exception != null) report.errors.Add(exception.ToString());
            try
            {
                // Pause while decoded children are still enabled, before Unity's
                // root destruction disables them and invokes component teardown.
                if (city && city.isActiveAndEnabled) city.enabled = false;
                if (owner) Object.DestroyImmediate(owner);
                if (photoOwner) Object.DestroyImmediate(photoOwner);
                foreach (UnityEngine.Object resource in cleanupObjects) if (resource) Object.DestroyImmediate(resource);
                if (directory != null && Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (Exception cleanup) { report.errors.Add(cleanup.ToString()); }
            SessionState.SetBool(ActiveKey, false); report.passed = report.errors.Count == 0 && report.runtimeErrors.Count == 0;
            report.finishedAtUtc = DateTime.UtcNow.ToString("O"); WriteReport();
            Debug.Log("SLOP_CITY_MOBILE_MEDIA_BUDGET_SMOKE_" + (report.passed ? "PASSED" : "FAILED") + " " + ReportPath);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
        private static void WriteReport()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }
        [Serializable] private sealed class Receipt
        {
            public string startedAtUtc, finishedAtUtc, unityVersion;
            public bool passed;
            public int maxPhonePlayers;
            public string scope = "Real bundled native city decodes with the production budget selector invoked using a simulated browser-build flag: desktop/native retain3, browser touch uses1; physical input detection itself needs Web build/browser checks. Solid-wall visual/audio exclusion, rapid alternating aim retention and deliberate aim handoffs with old-source gain reduction before Stop/rebind and no active decoder overlap, independent retained textures/assignments, featured loop intent and stable decoder continuity, pause/focus, completed local slideshow source release and pixel retention. Production PCM writer checks exact stereo samples, yielding single Web upload, cancellation before upload, and unchanged bounded native blocks with an injected upload sink; engine/browser AudioBuffer allocation requires Web checks. Empty scene and isolated validation save identity; live network disabled. Does not prove phone memory/crash recovery or a full natural video loop.";
            public List<string> checks = new List<string>(), errors = new List<string>(), runtimeErrors = new List<string>(), editorDiagnostics = new List<string>(), warnings = new List<string>();
        }
    }
}
#endif
