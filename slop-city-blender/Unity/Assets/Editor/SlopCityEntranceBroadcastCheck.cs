#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Offline native decode onto the actual entrance quad in a small isolated scene.</summary>
    [InitializeOnLoad]
    public static class SlopCityEntranceBroadcastCheck
    {
        private const string Active = "SlopCity.EntranceBroadcastCheck.Active", Started = Active + ".Started";
        private const string ReportPath = "Builds/Reports/entrance-broadcast-check.json";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt receipt;
        private static SlopCityCityMedia media;
        private static Camera camera;
        private static bool exercised;

        static SlopCityEntranceBroadcastCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (SessionState.GetBool(Active, false) && state == PlayModeStateChange.EnteredEditMode)
                    Finish(new InvalidOperationException("Broadcast fixture left Play Mode early."));
            };
        }

        // Dedicated graphics-enabled StandaloneOSX batch Editor, omit -quit/-nographics.
        // Requires isolated product/save name containing SlopCityValidation and:
        // -slopCityBroadcastMedia /absolute/path/to/current/site/StreamingAssets/TikTok
        public static void Run()
        {
            if (!Application.isBatchMode || !Application.persistentDataPath.Contains("SlopCityValidation")
                || EditorApplication.isPlayingOrWillChangePlaymode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Use an isolated SlopCityValidation identity and graphics-enabled batch Editor, outside Play Mode.");
            SessionState.SetString(Started, DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(Active, true);
            receipt = NewReceipt();
            try
            {
                // No saved city, synchronizer, player controller, or external feed is started.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }

        private static Receipt NewReceipt() => new Receipt { startedAtUtc = SessionState.GetString(Started, DateTime.UtcNow.ToString("O")),
            unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceType.ToString(), isolatedDataPath = Application.persistentDataPath };

        private static void Tick()
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (receipt == null) receipt = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(receipt.startedAtUtc).ToUniversalTime()).TotalSeconds > 60d)
                    throw new TimeoutException("Entrance broadcast fixture exceeded sixty seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (!exercised) { exercised = true; Work.Push(Exercise()); }
                while (Work.Count > 0)
                {
                    IEnumerator step = Work.Peek();
                    if (!step.MoveNext()) { Work.Pop(); continue; }
                    if (step.Current is IEnumerator nested) { Work.Push(nested); continue; }
                    return;
                }
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static IEnumerator Exercise()
        {
            Application.runInBackground = true;
            Check(Application.persistentDataPath.Contains("SlopCityValidation"), "Fixture writes only to its isolated validation save/cache identity.");
            string donor = Argument("-slopCityBroadcastMedia");
            string catalogPath = Path.Combine(donor, "catalog.json"), receiptPath = Path.Combine(donor, "bundle-receipt.json");
            var bundle = JsonUtility.FromJson<BundleReceipt>(File.ReadAllText(receiptPath));
            Check(bundle != null && bundle.webOptimization != null && Hash(File.ReadAllBytes(catalogPath)) == bundle.webOptimization.catalogSha256,
                "Local creator catalog matches the compact bundle receipt.");
            var catalog = JsonUtility.FromJson<SlopCityTikTokCatalog>(File.ReadAllText(catalogPath));
            var assets = bundle.posts.SelectMany(post => post.assets).ToDictionary(asset => asset.file);
            SlopCityTikTokVideo[] selected = catalog.videos.Where(post => post.kind == "video" && post.duration >= 12
                && assets.ContainsKey(post.id + ".mp4")).OrderBy(post => assets[post.id + ".mp4"].bytes).Take(2).ToArray();
            Check(selected.Length == 2, "Current local bundle supplies two distinct real creator clips.");
            string cache = Path.Combine(Application.persistentDataPath, "TikTok", "media");
            Directory.CreateDirectory(cache);
            foreach (SlopCityTikTokVideo post in selected)
            {
                string file = post.id + ".mp4";
                BundleAsset asset = assets[file];
                byte[] bytes = File.ReadAllBytes(Path.Combine(donor, "media", file));
                Check(bytes.LongLength == asset.bytes && Hash(bytes) == asset.sha256, "Verified compact clip bytes: " + file);
                File.WriteAllBytes(Path.Combine(cache, file), bytes);
                // The production native cache path resolves a safe localFile without
                // a synchronizer. Remove all remote playback alternatives in this clone.
                post.localFile = file; post.mediaUrl = post.audioUrl = "";
                post.imageUrls = Array.Empty<string>();
                receipt.clips.Add(new Clip { id = post.id, creator = post.creator, bytes = asset.bytes, sha256 = asset.sha256 });
            }
            Check(Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0,
                "Fixture has no synchronizer or network discovery component.");
            string configPath = Path.Combine(Application.streamingAssetsPath, "Media", "city-feed.json");
            var feed = JsonUtility.FromJson<Feed>(File.ReadAllText(configPath));
            Check(feed != null && string.IsNullOrEmpty(feed.feedUrl)
                && feed.channels.All(channel => string.IsNullOrEmpty(channel.mediaUrl)), "Native configuration has no remote feed or legacy playback URL.");

            typeof(SlopCityEntranceBroadcast).Assembly.GetType("SlopCity.SlopCityInteriorGeometry")
                .GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Transform city = new GameObject("Broadcast_Check_Isolated_City").transform;
            GameObject observer = new GameObject("Broadcast_Check_Observer");
            observer.tag = "MainCamera";
            observer.transform.position = new Vector3(0f, 3.635f, -8f);
            observer.AddComponent<SlopCityPlayerInput>().SetGameplayEnabled(true);
            camera = observer.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .12f, .17f);
            camera.nearClipPlane = .05f; camera.farClipPlane = 30f; camera.fieldOfView = 45f;
            observer.AddComponent<AudioListener>();
            Transform firstEntrance = Entrance(city, "First_Entrance", -1.3f);
            SlopCityEntranceBroadcast first = SlopCityEntranceBroadcast.Attach(firstEntrance, 4f);
            Check(first && first.Screen, "Actual framed entrance quad exists before media initialization.");
            media = new GameObject("Broadcast_Check_Shared_Media").AddComponent<SlopCityCityMedia>();
            media.Configure(city, observer.transform, Shader.Find("Unlit/Texture"));
            yield return Until(() => (bool)Field(media, "initialized"), 5d, "City media did not initialize.");
            Check(media.ScreenCount == 1, "Discovery registers the pre-Start screen exactly once.");
            media.RegisterScreen(first.Screen);
            Check(media.ScreenCount == 1, "Explicit registration duplicates do not allocate another display.");
            Transform secondEntrance = Entrance(city, "Second_Entrance", 1.3f);
            SlopCityEntranceBroadcast second = SlopCityEntranceBroadcast.Attach(secondEntrance, 4f);
            yield return Until(() => media.ScreenCount == 2, 3d, "Late entrance did not register at Start.");
            Check(SlopCityEntranceBroadcast.Attach(firstEntrance, 4f) == first && firstEntrance.childCount == 1
                && media.ScreenCount == 2, "Reattaching the persistent entrance preserves its single component and display.");
            Check(media.DecoderLimit == 3, "Native shared decoder limit remains three.");
            Check(first.Screen.FrontNormal == Vector3.back && first.Screen.Display.bounds.min.y >= 3.09f,
                "Physical picture faces the street and leaves door/sign headroom.");
            media.ApplyTikTokCatalog(new SlopCityTikTokCatalog { videos = selected });
            Check(first.Screen.ClipKey != second.Screen.ClipKey && selected.Any(post => post.creator + "/" + post.id == first.Screen.ClipKey)
                && selected.Any(post => post.creator + "/" + post.id == second.Screen.ClipKey), "Both entrance pictures receive independent local creator assignments.");
            media.SendMessage("OnApplicationFocus", true); media.SendMessage("OnApplicationPause", false);
            media.SetVolume(0f); media.SetSuspended(false);
            Physics.SyncTransforms();
            yield return Until(() => Players().Length == 2 && Players().All(player => player.isPrepared && player.isPlaying && player.frame >= 2),
                30d, "Shared pool did not decode both local entrance clips.");
            receipt.peakDecoders = Players().Length;
            Check(receipt.peakDecoders <= media.DecoderLimit && Players().All(player => new Uri(player.url).IsFile),
                "Two physical entrance displays use bounded local-file shared decoders.");
            var displays = new[] { first.Screen, second.Screen };
            foreach (SlopCityCityScreen display in displays)
                Check(Players().Any(player => player.targetTexture && player.targetTexture == display.Display.sharedMaterial.mainTexture),
                    "Actual entrance material binds its active decoder RenderTexture.");
            VideoPlayer[] players = Players();
            long[] frames = players.Select(player => player.frame).ToArray();
            double[] times = players.Select(player => player.time).ToArray();
            receipt.firstImage = Capture("entrance-broadcast-first.png");
            yield return Until(() => players.Select((player, i) => player.isPlaying && player.frame > frames[i] + 3 && player.time > times[i] + .5d).All(value => value),
                6d, "Actual native frames/time did not advance on both entrance pictures.");
            receipt.secondImage = Capture("entrance-broadcast-moving.png");
            Check(Hash(File.ReadAllBytes(receipt.firstImage)) != Hash(File.ReadAllBytes(receipt.secondImage)),
                "Rendered physical entrance pixels change while both actual video clocks advance.");
            for (int i = 0; i < players.Length; i++)
                receipt.motion.Add(new Motion { source = players[i].url, firstFrame = frames[i], lastFrame = players[i].frame,
                    firstSeconds = times[i], lastSeconds = players[i].time });

            var oldMaterial = first.Screen.Display.sharedMaterial;
            var oldTexture = oldMaterial.mainTexture as RenderTexture;
            object oldState = ((IDictionary)Field(media, "displayLookup"))[first.Screen];
            media.UnregisterScreen(first.Screen);
            Check(media.ScreenCount == 1 && !((IDictionary)Field(media, "displayLookup")).Contains(first.Screen)
                && Field(oldState, "slot") == null, "Unregister immediately clears registry and shared decoder ownership.");
            Check(!Players().Any(player => player.targetTexture == oldTexture), "Retired display texture is detached from every decoder.");
            Object.Destroy(firstEntrance.gameObject);
            yield return null; yield return null;
            Check(!oldMaterial && !oldTexture && media.ScreenCount == 1, "Owned material/texture retire and entrance destruction remains idempotent.");
            Object.Destroy(media.gameObject);
            yield return null; yield return null;
            Check(!Object.FindFirstObjectByType<VideoPlayer>(), "Destroying the shared manager releases all native decoder sessions.");
        }

        private static VideoPlayer[] Players() => media.GetComponentsInChildren<VideoPlayer>();
        private static Transform Entrance(Transform parent, string name, float x)
        {
            Transform root = new GameObject(name).transform;
            root.SetParent(parent, false); root.localPosition = new Vector3(x, 0f, 0f); return root;
        }
        private static object Field(object owner, string name) => owner.GetType().GetField(name, Private).GetValue(owner);
        private static IEnumerator Until(Func<bool> condition, double seconds, string message)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (!condition()) { if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(message); yield return null; }
        }
        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 == args.Length || !Path.IsPathRooted(args[index + 1])) throw new InvalidOperationException(name + " requires an absolute local directory.");
            return args[index + 1];
        }
        private static string Capture(string name)
        {
            string directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name);
            RenderTexture target = RenderTexture.GetTemporary(960, 540, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
                Color32[] data = pixels.GetPixels32();
                Check(data.Select(color => (int)color.r + color.g + color.b).Distinct().Take(8).Count() == 8,
                    "Camera capture contains nonuniform physical broadcast pixels.");
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.Destroy(pixels); }
            return path;
        }
        private static string Hash(byte[] bytes)
        { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); receipt.checks.Add(message); }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Active, false); Work.Clear();
            if (receipt == null) receipt = NewReceipt();
            if (error != null) receipt.errors.Add(error.ToString());
            receipt.passed = receipt.errors.Count == 0;
            receipt.finishedAtUtc = DateTime.UtcNow.ToString("O");
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(receipt, true));
            Debug.Log("SLOP_CITY_ENTRANCE_BROADCAST_CHECK_" + (receipt.passed ? "PASSED" : "FAILED") + " " + path);
            EditorApplication.Exit(receipt.passed ? 0 : 1);
        }
        [Serializable] private sealed class Receipt
        {
            public bool passed; public string startedAtUtc, finishedAtUtc, unityVersion, graphicsDevice, isolatedDataPath, firstImage, secondImage;
            public int peakDecoders;
            public string scope = "Actual native Unity decoder motion on runtime entrance quads; isolated offline local creator subset; no browser/iPhone/audio-output acceptance.";
            public List<string> checks = new List<string>(), errors = new List<string>();
            public List<Clip> clips = new List<Clip>(); public List<Motion> motion = new List<Motion>();
        }
        [Serializable] private sealed class Clip { public string id, creator, sha256; public long bytes; }
        [Serializable] private sealed class Motion { public string source; public long firstFrame, lastFrame; public double firstSeconds, lastSeconds; }
        [Serializable] private sealed class Feed { public string feedUrl = ""; public FeedChannel[] channels = Array.Empty<FeedChannel>(); }
        [Serializable] private sealed class FeedChannel { public string mediaUrl = ""; }
        [Serializable] private sealed class BundleReceipt { public BundlePost[] posts = Array.Empty<BundlePost>(); public Optimization webOptimization = null; }
        [Serializable] private sealed class BundlePost { public BundleAsset[] assets = Array.Empty<BundleAsset>(); }
        [Serializable] private sealed class BundleAsset { public string file = "", sha256 = ""; public long bytes = 0; }
        [Serializable] private sealed class Optimization { public string catalogSha256 = ""; }
    }
}
#endif
