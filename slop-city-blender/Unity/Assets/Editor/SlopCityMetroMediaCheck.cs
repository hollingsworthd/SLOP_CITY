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
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Isolated authored metro geometry and actual Play Mode screen ownership, without network requests.</summary>
    [InitializeOnLoad]
    public static class SlopCityMetroMediaCheck
    {
        private const string Pending = "SlopCity.MetroMediaCheck.Pending", Result = "SlopCity.MetroMediaCheck.Result";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        [Serializable] private sealed class Receipt
        {
            public bool passed; public int checks; public string unityVersion;
            public string scope = "Actual authored subway front-plane collider rays and real Play Mode dynamic TV/radio registration, pooled target mode, pause and synchronous release. No network, decoded audio/video playback, compiled browser or physical-device acceptance.";
            public List<string> errors = new List<string>();
        }
        static SlopCityMetroMediaCheck() { EditorApplication.playModeStateChanged += StateChanged; }
        private static object Get(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
        private static object Call(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, Hidden).Invoke(target, arguments);
        private static Receipt report;
        private static void Check(bool value, string message) { report.checks++; if (!value) throw new InvalidOperationException(message); }

        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode
                || !Application.companyName.StartsWith("SlopCityValidation", StringComparison.Ordinal)
                || !Application.persistentDataPath.Contains("SlopCityValidation"))
                throw new InvalidOperationException("An isolated SlopCityValidation batch project is required.");
            SessionState.EraseBool(Pending);
            report = new Receipt { unityVersion = Application.unityVersion };
            Application.logMessageReceived += Capture;
            try
            {
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                foreach (SlopCityCityMedia item in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None)) item.enabled = false;
                GameObject city = GameObject.Find("SlopCity_NineBlocks"); Check(city, "Authored saved city exists.");
                SlopCityHumanScale.Prepare(city.transform);
                SlopCityMetroWorld metro = SlopCityMetroWorld.Prepare(city.transform); Check(metro && metro.Prepared, "Actual metro preparation succeeds.");
                var host = new GameObject("Metro_Media_Check_Manager"); var media = host.AddComponent<SlopCityCityMedia>(); media.enabled = false;
                Call(media, "Awake"); media.Configure(city.transform, null, Shader.Find("Unlit/Texture"));
                media.RegisterAnchors(metro.transform); Physics.SyncTransforms();
                int trains = 0;
                foreach (Transform train in metro.transform)
                {
                    if (!train.name.StartsWith("Subway_Train_", StringComparison.Ordinal)) continue;
                    trains++;
                    SlopCityCityScreen[] screens = train.GetComponentsInChildren<SlopCityCityScreen>(true);
                    Check(screens.Length == 1, train.name + " retains exactly one independently wired passenger display.");
                    SlopCityCityScreen screen = screens[0]; Vector3 centre = screen.Display.bounds.center;
                    Vector3 inward = (train.TransformPoint(new Vector3(0, 1.8f, 0)) - centre).normalized;
                    Check(Vector3.Dot(screen.FrontNormal, inward) > .95f, train.name + " visible front faces the carriage interior.");
                    foreach (float offset in new[] { -.65f, 0f, .65f })
                    {
                        Vector3 origin = centre + screen.FrontNormal * 1.2f + train.right * offset;
                        Vector3 delta = centre - origin;
                        bool hit = Physics.Raycast(origin, delta.normalized, out RaycastHit actual, delta.magnitude + .03f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                        Check(hit && actual.transform == screen.Display.transform, train.name + " display is the first physical surface from passenger view " + offset + ".");
                    }
                }
                Check(trains == 3, "All three carriages have physical screens.");
                Check(media.ScreenCount == 0, "Anchor discovery does not allocate when manager is disabled.");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Application.logMessageReceived -= Capture;
                SessionState.SetString(Result, JsonUtility.ToJson(report)); SessionState.SetBool(Pending, true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { report.errors.Add(error.ToString()); Finish(); }
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                report = JsonUtility.FromJson<Receipt>(SessionState.GetString(Result, ""));
                Application.logMessageReceived += Capture;
                try { Ownership(); } catch (Exception error) { report.errors.Add(error.ToString()); }
                finally { Application.logMessageReceived -= Capture; }
                SessionState.SetString(Result, JsonUtility.ToJson(report)); EditorApplication.ExitPlaymode();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                report = JsonUtility.FromJson<Receipt>(SessionState.GetString(Result, ""));
                SessionState.EraseBool(Pending); SessionState.EraseString(Result); Finish();
            }
        }

        private static void Ownership()
        {
            var host = new GameObject("Media_Ownership_Fixture");
            var media = host.AddComponent<SlopCityCityMedia>(); Call(media, "Awake");
            var listener = new GameObject("Listener").AddComponent<Camera>(); listener.transform.position = Vector3.zero; listener.transform.rotation = Quaternion.Euler(0, 180, 0);
            media.Configure(host.transform, listener.transform, Shader.Find("Unlit/Texture")); Call(media, "ReadConfiguration"); Set(media, "initialized", true); Set(media, "menuSuspended", false); Set(media, "activeDecoderLimit", 1);
            var shelf = GameObject.CreatePrimitive(PrimitiveType.Cube); shelf.name = "Radio_Stable_Room"; shelf.transform.position = new Vector3(0, 0, 2);
            Renderer cabinet = shelf.GetComponent<Renderer>(); Material original = cabinet.sharedMaterial;
            var radio = shelf.AddComponent<SlopCityCityScreen>(); radio.ConfigureAudioOnly(); radio.Configure(cabinet, 2, 5);
            Check(media.HasScreen(radio) && media.ScreenCount == 1, "Configured late radio registers without a scene scan.");
            Check(cabinet.sharedMaterial == original && !media.PictureFor(radio), "Radio keeps cabinet material and owns no retained picture.");
            var lookup = (IDictionary)Get(media, "displayLookup"); object display = lookup[radio];
            Check(Get(display, "material") == null && Get(display, "texture") == null && !(bool)Get(display, "needsThumbnail"), "Radio has no private screen material, texture or poster work.");
            Check(!media.TryTuneJobScreen(radio.transform, 0, out _) && !media.TryObserveJobScreen(radio.transform, out _, out _, out _, out _), "Radio cannot satisfy picture/tuning jobs.");
            Set(media, "mobilePendingOwner", display); Set(media, "mobileCandidateSince", Time.unscaledTime - 1f);
            Call(media, "RefreshScreenInterest");
            Check((float)Get(display, "audioScore") > 0 && !(bool)Get(display, "aimed") && Get(display, "slot") != null, "Nearby off-camera radio acquires the shared audio pool without aim/frustum eligibility.");
            object slot = Get(display, "slot"); Check(Get(slot, "texture") == null && Get(slot, "material") == null, "Radio decoder slot has no radio-owned target texture or material.");
            Check(((Array)Get(media, "channels")).Cast<object>().Count(channel => Get(channel, "owner") != null) == 1, "One-slot policy owns exactly one shared decoder slot for the off-camera radio.");
            // Native desktop pooling retains the stopped player for the later TV transition.
            Set(media, "activeDecoderLimit", 3);
            string clip = new Uri(Path.Combine(Application.streamingAssetsPath, "Media", "broadcast.mp4")).AbsoluteUri;
            Call(media, "PrepareChannel", slot, clip);
            VideoPlayer player = (VideoPlayer)Get(slot, "player"); VideoPlayer radioPlayer = player;
            Check(player && player.renderMode == VideoRenderMode.APIOnly && player.targetTexture == null, "Actual native VideoPlayer routes radio to APIOnly with no render target.");
            Check(media.TryTuneScreen(radio.transform, out _) && (bool)Get(display, "paused") && Get(display, "slot") == null, "Radio play/pause synchronously retires decoder ownership.");
            Check(media.TryTuneScreen(radio.transform, out _) && !(bool)Get(display, "paused"), "Radio resumes through the same interaction.");
            listener.transform.position = new Vector3(0, 0, -10); Call(media, "RefreshScreenInterest");
            Check((float)Get(display, "audioScore") == 0 && Get(display, "slot") == null, "Out-of-range radio cannot retain a decoder as an invisible visual candidate.");
            listener.transform.position = Vector3.zero;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0, 0, 1); wall.transform.localScale = new Vector3(3, 3, .15f); Physics.SyncTransforms(); Call(media, "RefreshScreenInterest");
            Check((float)Get(display, "audioScore") == 0 && Get(display, "slot") == null, "A real intervening wall blocks radio audio interest."); Object.DestroyImmediate(wall);
            shelf.SetActive(false); Check(!media.HasScreen(radio) && media.ScreenCount == 0, "OnDisable unregisters synchronously before streamed-floor Destroy.");
            shelf.SetActive(true); Check(media.HasScreen(radio) && media.ScreenCount == 1, "OnEnable re-admits a radio exactly once.");
            media.RegisterScreen(radio); Check(media.ScreenCount == 1, "Explicit registration remains idempotent with component lifecycle.");
            for (int index = 0; index < 12; index++)
            {
                shelf.SetActive(false); shelf.SetActive(true);
                Check(media.ScreenCount == 1 && cabinet.sharedMaterial == original && !media.PictureFor(radio), "Repeated streamed radio admission does not retain pictures/materials.");
            }
            radio.enabled = false; Check(media.ScreenCount == 0, "Disabling the component retires ownership too."); radio.enabled = true;
            media.enabled = false; shelf.SetActive(false); shelf.SetActive(true); Check(media.ScreenCount == 0, "Disabled manager does not admit newly re-enabled devices.");
            media.enabled = true; Check(media.ScreenCount == 1, "Manager re-enable admits known active devices without a global scan.");
            Object.DestroyImmediate(shelf); Check(media.ScreenCount == 0 && lookup.Count == 0, "Direct device destruction removes every lookup entry.");
            Set(media, "activeDecoderLimit", 3);
            var tv = GameObject.CreatePrimitive(PrimitiveType.Quad); tv.transform.position = new Vector3(0, 0, 2); var screen = tv.AddComponent<SlopCityCityScreen>(); screen.Configure(tv.GetComponent<Renderer>(), 0);
            Check(media.HasScreen(screen) && media.PictureFor(screen), "Late ordinary TV still owns its independently retained picture.");
            display = lookup[screen];
            screen.ConfigureFeaturedPost("fixture_owner", "pinned", -1f);
            Check(screen.ClipKey == "fixture_owner/pinned" && Get(display, "post") == null, "A featured pin supplied after automatic Configure admission immediately replaces the ordinary program.");
            screen.ConfigureFeaturedPost("", "", -1f);
            var configureGroup = screen.GetType().GetMethod("ConfigureScreenGroup");
            if (configureGroup != null)
            {
                configureGroup.Invoke(screen, new object[] { "home" });
                Check(media.ScreenCount == 1 && ReferenceEquals(display, lookup[screen]), "A late explicit private screen group refreshes assignment without duplicate registration.");
                configureGroup.Invoke(screen, new object[] { "street" });
            }
            Array channels = (Array)Get(media, "channels"); slot = channels.GetValue(0); Call(media, "BindSlot", slot, display); Call(media, "PrepareChannel", slot, clip); player = (VideoPlayer)Get(slot, "player");
            Check(player == radioPlayer && player.renderMode == VideoRenderMode.RenderTexture && player.targetTexture == media.PictureFor(screen), "The shared slot switches from radio APIOnly back to the ordinary TV render target.");
            RenderTexture picture = (RenderTexture)media.PictureFor(screen); tv.SetActive(false);
            Check(media.ScreenCount == 0 && lookup.Count == 0 && Get(slot, "owner") == null && Get(slot, "texture") == null && !picture.IsCreated(), "TV removal synchronously detaches pooled decoder and releases its retained texture.");
            Object.DestroyImmediate(tv); Object.DestroyImmediate(host); Object.DestroyImmediate(listener.gameObject);
        }

        private static void Capture(string condition, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(condition + "\n" + trace);
        }

        private static void Finish()
        {
            Application.logMessageReceived -= Capture;
            report.passed = report.errors.Count == 0;
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/Reports/metro-media-check.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("SLOP_CITY_METRO_MEDIA_" + (report.passed ? "PASSED" : "FAILED") + " " + report.checks + " checks");
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
}
#endif
