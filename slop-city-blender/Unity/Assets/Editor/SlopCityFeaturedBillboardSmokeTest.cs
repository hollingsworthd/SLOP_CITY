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
    /// <summary>Real saved-city geometry and native continuous featured looping. Requires an isolated test identity.</summary>
    [InitializeOnLoad]
    public static class SlopCityFeaturedBillboardSmokeTest
    {
        private const string Active = "SlopCity.FeaturedSmoke.Active", Started = "SlopCity.FeaturedSmoke.Started";
        private const string Backup = "SlopCity.FeaturedSmoke.Playback", Existed = "SlopCity.FeaturedSmoke.Existed";
        private const string ReportPath = "Builds/Reports/featured-billboard-check.json";
        private const string IsolatedCompany = "SlopCityValidation-v0116";
        private const string ExpectedFeaturedKey = "1984drh/7696938454575238430";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt report;
        private static SlopCityCityMedia media;
        private static SlopCityFirstPerson viewer;
        private static SlopCityFeaturedBillboard board;
        private static double enteredAt;
        private static bool started;
        private static int lastFrame = -1;
        static SlopCityFeaturedBillboardSmokeTest()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Active, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (state == PlayModeStateChange.EnteredEditMode) Finish(new Exception("Featured billboard Play Mode ended early."));
            };
            Application.logMessageReceived += (message, trace, kind) =>
            {
                if (!SessionState.GetBool(Active, false) || (kind != LogType.Error && kind != LogType.Exception && kind != LogType.Assert)) return;
                if (report == null) report = NewReceipt();
                bool editorSearch = message.StartsWith("ArgumentOutOfRangeException:", StringComparison.Ordinal)
                    && trace.Contains("UnityEditor.Search.SearchDatabase") && trace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup") && !trace.Contains("SlopCity.");
                if (editorSearch) report.editorDiagnostics.Add(message + "\n" + trace);
                else report.runtimeErrors.Add(message + "\n" + trace);
            };
        }
        // Root runs in a cloned project with companyName SlopCityValidation-v0116, graphics enabled, no -quit.
        public static void Run()
        {
            string identitySegment = Path.DirectorySeparatorChar + IsolatedCompany + Path.DirectorySeparatorChar;
            if (!Application.isBatchMode || Application.companyName != IsolatedCompany
                || Application.persistentDataPath.IndexOf(identitySegment, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("This fixture requires the isolated " + IsolatedCompany + " company/save identity before any save/load work.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start outside Play Mode.");
            for (int index = 0; index < 2; index++)
            {
                string path = PlaybackPath(index);
                SessionState.SetBool(Existed + index, File.Exists(path));
                SessionState.SetString(Backup + index, File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : "");
            }
            SessionState.SetString(Started, DateTime.UtcNow.ToString("O")); SessionState.SetBool(Active, true);
            report = NewReceipt(); WriteReport();
            try
            {
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                foreach (SlopCityTikTokSync sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsSortMode.None)) sync.enabled = false;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }
        private static Receipt NewReceipt() => new Receipt { startedAtUtc = SessionState.GetString(Started, DateTime.UtcNow.ToString("O")), unityVersion = Application.unityVersion };
        private static void Tick()
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (report == null) report = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(report.startedAtUtc).ToUniversalTime()).TotalSeconds > 240) throw new TimeoutException("Featured billboard checks exceeded four minutes.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                if (!started)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < 1) return;
                    started = true; Work.Push(Exercise());
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
            media = Object.FindFirstObjectByType<SlopCityCityMedia>(); viewer = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            board = Object.FindFirstObjectByType<SlopCityFeaturedBillboard>();
            Check(media && viewer && board && board.Screen, "The saved city installs its single runtime featured board.");
            viewer.enabled = false;
            foreach (SlopCityMediaPlayer lounge in Object.FindObjectsByType<SlopCityMediaPlayer>(FindObjectsSortMode.None)) lounge.Stop();
            Transform city = GameObject.Find("SlopCity_NineBlocks").transform;
            int screens = media.ScreenCount;
            Check(SlopCityFeaturedBillboard.Ensure(city) == board && media.ScreenCount == screens
                && Object.FindObjectsByType<SlopCityFeaturedBillboard>(FindObjectsSortMode.None).Length == 1, "Repeated installation never duplicates the board or a screen registration.");
            Check(SlopCityFeaturedBillboard.Creator + "/" + SlopCityFeaturedBillboard.PostId == ExpectedFeaturedKey
                && board.Screen.FeaturedClipKey == ExpectedFeaturedKey
                && board.Screen.FeaturedHoldAtSeconds < 0 && board.Screen.FeaturedHoldSeconds == 0, "The dedicated screen pins the requested post with no timed pause.");
            report.position = board.Screen.Display.bounds.center; report.size = board.Screen.Display.bounds.size;
            Check(Vector3.Distance(report.position, new Vector3(SlopCityDenseLayout.HomeFeaturedMount - .012f, 7.0f, 0)) < .025f
                && Mathf.Abs(board.transform.position.x - SlopCityDenseLayout.HomeFeaturedMount) < .025f
                && Vector3.Dot(board.Screen.FrontNormal, Vector3.left) > .999f, "The giant physical portrait faces west toward the actual home door.");
            Check(Mathf.Abs(board.Screen.Display.transform.lossyScale.x - SlopCityFeaturedBillboard.Width) < .001f
                && Mathf.Abs(board.Screen.Display.transform.lossyScale.y - SlopCityFeaturedBillboard.Height) < .001f
                && Mathf.Abs(SlopCityFeaturedBillboard.Width - 5.4f) < .001f && Mathf.Abs(SlopCityFeaturedBillboard.Height - 9.6f) < .001f,
                "Picture geometry retains its full-size 5.4 by 9.6 metre portrait aspect without cropping.");
            CheckExitGeometry();
            SlopCityTikTokSync sync = Object.FindFirstObjectByType<SlopCityTikTokSync>();
            Check(sync && !sync.enabled, "Live network refresh is disabled only in this isolated fixture.");
            yield return (IEnumerator)typeof(SlopCityTikTokSync).GetMethod("LoadBundledCatalog", Private).Invoke(sync, null);
            media.ApplyTikTokCatalog(sync.Catalog);
            Check(sync.Catalog.videos.Any(post => post.creator == SlopCityFeaturedBillboard.Creator && post.id == SlopCityFeaturedBillboard.PostId), "The refreshed bundle contains the exact featured clip.");
            Check(board.Screen.ClipKey == board.Screen.FeaturedClipKey, "Catalogue assignment pins the requested post.");
            SlopCityCityScreen[] ordinary = Object.FindObjectsByType<SlopCityCityScreen>(FindObjectsSortMode.None).Where(screen => !screen.IsFeatured).ToArray();
            Check(ordinary.All(screen => screen.ClipKey != board.Screen.FeaturedClipKey), "The forced post is reserved for its dedicated board.");
            media.TuneScreen(board.Screen.transform);
            Check(board.Screen.ClipKey == board.Screen.FeaturedClipKey && board.Screen.Channel == 0, "Tuning cannot replace the dedicated broadcast.");
            SlopCityCityScreen rotating = ordinary.First(screen => screen.Channel == 0);
            string old = rotating.ClipKey; media.TuneScreen(rotating.transform);
            Check(rotating.ClipKey != old && rotating.ClipKey != board.Screen.FeaturedClipKey, "Ordinary displays retain independent rotating assignments.");
            MoveToBoard(); media.SendMessage("OnApplicationFocus", true); media.SendMessage("OnApplicationPause", false); media.SetSuspended(false);
            yield return WaitFor(() => FeaturedPlayer() && FeaturedPlayer().isPlaying, 25, "Featured native decoder did not begin playback.");
            VideoPlayer player = FeaturedPlayer();
            Check(player.renderMode == VideoRenderMode.RenderTexture && player.targetTexture == board.Screen.Display.sharedMaterial.mainTexture
                && player.targetTexture.width == 360 && player.targetTexture.height == 640, "Native featured video uses a portrait world RenderTexture with unchanged aspect.");
            Check(player.aspectRatio == VideoAspectRatio.FitInside && player.url.Contains(SlopCityFeaturedBillboard.PostId), "Only the requested uncropped clip is decoded.");
            Check(player.isLooping && !Holding() && PictureEnergy() > .001, "The requested portrait is decoded and loops continuously without a hold.");
            report.featuredClipKey = board.Screen.ClipKey; report.clipLengthSeconds = player.length;
            SlopCityTikTokVideo featuredPost = sync.Catalog.videos.Single(post => post.creator == SlopCityFeaturedBillboard.Creator && post.id == SlopCityFeaturedBillboard.PostId);
            Check(player.length > 0 && !double.IsNaN(player.length) && !double.IsInfinity(player.length)
                && Math.Abs(player.length - featuredPost.duration) <= 2, "The requested clip retains its catalogue duration within metadata rounding.");
            CaptureLoopView();
            yield return SeekTo(player, 0);
            yield return ObserveWraps(player, 1, Math.Max(3, player.length) + 8, "A full natural playback loop from the beginning");
            report.naturalWraps++;
            Check(CityPlayers().Where(other => other != player).All(other => !other.isLooping), "Ordinary city players keep their independent nonlooping rotation.");
            media.TogglePaused(); yield return Delay(.15); double pausedAt = player.time;
            yield return Delay(.6);
            Check(!player.isPlaying && Math.Abs(player.time - pausedAt) < .08 && !Holding(), "Manual pause freezes continuous playback without creating a timed hold.");
            media.TogglePaused(); yield return WaitFor(() => player.isPlaying, 5, "The continuous broadcast did not resume.");
            report.resumedAtSeconds = player.time; report.resumedPrepared = player.isPrepared;
            report.resumedPlaying = player.isPlaying; report.resumedLooping = player.isLooping;
            yield return ObserveWraps(player, 1, Math.Max(3, player.length) + 8, "Natural playback after manual pause/resume without another seek");
            report.naturalWraps++;
            // Pool release/reacquisition should restart cleanly, with no stale hold/callback ownership.
            viewer.TeleportTo(new Vector3(-50, .04f, -50));
            yield return Delay(.6);
            Check(!Holding() && ReadSlot() == null && CityPlayers().Length <= 3, "Walking away releases its decoder slot while retaining the world image.");
            MoveToBoard(); yield return WaitFor(() => FeaturedPlayer() && FeaturedPlayer().isPlaying, 25, "The board did not reacquire a pool decoder.");
            Check(FeaturedPlayer().isLooping && !Holding(), "Reacquired playback continues looping without a timed hold.");
            Check(board.Screen.ClipKey == board.Screen.FeaturedClipKey && CityPlayers().Length <= 3, "Returning preserves the forced clip and bounded pool.");
            Check(report.runtimeErrors.Count == 0, "Featured playback and lifecycle checks report no runtime errors.");
        }
        private static void CheckExitGeometry()
        {
            SlopCityDoor door = Object.FindObjectsByType<SlopCityDoor>(FindObjectsSortMode.None).First(item => item.DisplayName == "HOME / CITY DOOR");
            Vector3 exit = door.InteractionPoint + Vector3.right * 1.25f;
            Vector3 eye = new Vector3(exit.x, 1.65f, 0);
            Vector3 delta = board.Screen.Display.bounds.center - eye;
            RaycastHit[] hits = Physics.RaycastAll(eye, delta.normalized, delta.magnitude - .05f, ~0, QueryTriggerInteraction.Ignore);
            Check(!hits.Any(hit => hit.transform != board.Screen.transform && !hit.transform.IsChildOf(viewer.transform)),
                "The actual doorway exterior has an unobstructed sightline to the billboard picture.");
            Transform view = viewer.ViewCamera.transform;
            Vector3 savedPosition = view.position; Quaternion savedRotation = view.rotation;
            view.SetPositionAndRotation(eye, Quaternion.LookRotation(Vector3.right));
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(viewer.ViewCamera);
            bool visible = GeometryUtility.TestPlanesAABB(planes, board.Screen.Display.bounds);
            view.SetPositionAndRotation(savedPosition, savedRotation);
            Check(visible, "The featured board is visible while exiting east from the doorway.");
            float walkwayEnd = SlopCityDenseLayout.HomeAlleyWall - .26f;
            Check(walkwayEnd > exit.x, "The actual home exit retains a positive-length east walkway before the enclosing tower wall.");
            for (float x = exit.x; x <= walkwayEnd; x += .5f)
            {
                Collider[] blocking = Physics.OverlapCapsule(new Vector3(x, .27f, 0), new Vector3(x, 1.56f, 0), .23f, ~0, QueryTriggerInteraction.Ignore);
                Check(!blocking.Any(hit => hit.transform.IsChildOf(board.transform)), "Featured support/frame leave the central east walkway capsule clear.");
            }
        }
        private static void MoveToBoard()
        {
            viewer.TeleportTo(new Vector3(8, .04f, 0));
            viewer.ViewCamera.transform.rotation = Quaternion.LookRotation(board.Screen.Display.bounds.center - viewer.ViewCamera.transform.position);
        }
        private static object ReadSlot()
        {
            IDictionary lookup = (IDictionary)typeof(SlopCityCityMedia).GetField("displayLookup", Private).GetValue(media);
            object display = lookup[board.Screen]; return display.GetType().GetField("slot", Private).GetValue(display);
        }
        private static T SlotValue<T>(string field)
        { object slot = ReadSlot(); return slot == null ? default : (T)slot.GetType().GetField(field, Private).GetValue(slot); }
        private static VideoPlayer FeaturedPlayer() => SlotValue<VideoPlayer>("player");
        private static bool Holding() => SlotValue<bool>("featuredHolding");
        private static VideoPlayer[] CityPlayers() => media.GetComponentsInChildren<VideoPlayer>(true);
        private static IEnumerator SeekTo(VideoPlayer player, double seconds)
        {
            Check(player.canSetTime && player.length > 0, "The prepared native featured decoder supports a bounded test seek.");
            bool completed = false;
            VideoPlayer.EventHandler handler = source => completed = source == player;
            player.seekCompleted += handler;
            try
            {
                player.time = seconds;
                yield return WaitFor(() => completed, 8, "The native featured test seek did not complete.");
                Check(FeaturedPlayer() == player && player.isPlaying && player.isLooping && !Holding()
                    && Math.Abs(player.time - seconds) < 1, "A completed test seek preserves the same playing, looping decoder without a hold.");
            }
            finally { player.seekCompleted -= handler; }
        }
        private static IEnumerator ObserveWraps(VideoPlayer player, int wanted, double timeoutSeconds, string context)
        {
            int wraps = 0; double previous = player.time;
            double end = EditorApplication.timeSinceStartup + timeoutSeconds;
            while (wraps < wanted)
            {
                if (EditorApplication.timeSinceStartup >= end) throw new TimeoutException("Continuous featured loop did not wrap.");
                if (FeaturedPlayer() != player || !player.isPrepared || !player.isPlaying || !player.isLooping || Holding() || CityPlayers().Length > 3)
                    throw new InvalidOperationException("Continuous looping changed decoder, stopped/unprepared playback, created a hold or exceeded the pool.");
                double current = player.time;
                if (current < previous - .5 && previous > player.length * .5) { wraps++; report.observedWraps++; }
                previous = current;
                yield return null;
            }
            Check(true, context + ": actual native time wraps " + wanted + " time(s) on the same prepared, playing decoder with no hold and at most three city players.");
        }
        private static void CaptureLoopView()
        {
            Camera view = viewer.ViewCamera;
            RenderTexture oldTarget = view.targetTexture, oldActive = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(960, 540, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                view.targetTexture = target; view.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
                string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports/featured-billboard-loop.png");
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, pixels.EncodeToPNG());
                report.loopScreenshot = path;
            }
            finally { view.targetTexture = oldTarget; RenderTexture.active = oldActive; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(pixels); }
        }
        private static Color32[] Picture()
        {
            var source = board.Screen.Display.sharedMaterial.mainTexture;
            RenderTexture sample = RenderTexture.GetTemporary(36, 64, 0, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(36, 64, TextureFormat.RGB24, false, true);
            RenderTexture previous = RenderTexture.active;
            try { Graphics.Blit(source, sample); RenderTexture.active = sample; pixels.ReadPixels(new Rect(0, 0, 36, 64), 0, 0); pixels.Apply(); return pixels.GetPixels32(); }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(sample); Object.DestroyImmediate(pixels); }
        }
        private static uint PictureHash() { uint hash = 2166136261; foreach (Color32 pixel in Picture()) { hash = (hash ^ pixel.r) * 16777619; hash = (hash ^ pixel.g) * 16777619; hash = (hash ^ pixel.b) * 16777619; } return hash; }
        private static double PictureEnergy() => Picture().Average(pixel => (pixel.r + pixel.g + pixel.b) / 765d);
        private static IEnumerator Delay(double seconds) { double end = EditorApplication.timeSinceStartup + seconds; while (EditorApplication.timeSinceStartup < end) yield return null; }
        private static IEnumerator WaitFor(Func<bool> ready, double seconds, string label)
        { double end = EditorApplication.timeSinceStartup + seconds; while (!ready()) { if (EditorApplication.timeSinceStartup >= end) throw new TimeoutException(label); yield return null; } }
        private static void Check(bool passed, string label) { if (!passed) throw new InvalidOperationException(label); report.checks.Add(label); }
        private static string PlaybackPath(int index) => Path.Combine(Application.persistentDataPath, "TikTok", "playback.json" + (index == 0 ? "" : ".tmp"));
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Active, false); Work.Clear(); started = false;
            if (report == null) report = NewReceipt(); if (error != null) report.errors.Add(error.ToString());
            if (media) Object.DestroyImmediate(media);
            for (int index = 0; index < 2; index++)
            {
                string path = PlaybackPath(index);
                if (SessionState.GetBool(Existed + index, false)) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, Convert.FromBase64String(SessionState.GetString(Backup + index, ""))); }
                else if (File.Exists(path)) File.Delete(path);
            }
            report.passed = report.errors.Count == 0 && report.runtimeErrors.Count == 0; report.finishedAtUtc = DateTime.UtcNow.ToString("O"); WriteReport();
            Debug.Log("SLOP_CITY_FEATURED_BILLBOARD_" + (report.passed ? "PASSED" : "FAILED")); EditorApplication.Exit(report.passed ? 0 : 1);
        }
        private static void WriteReport() { string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true)); }
        [Serializable] private sealed class Receipt
        {
            public bool passed;
            public string startedAtUtc, finishedAtUtc, unityVersion, loopScreenshot, featuredClipKey;
            public Vector3 position, size;
            public int observedWraps, naturalWraps;
            public double clipLengthSeconds, resumedAtSeconds;
            public bool resumedPrepared, resumedPlaying, resumedLooping;
            public string scope = "Isolated v0.11.6 saved-city Editor/native decode within 240 seconds: actual doorway visibility/clear home-alley walkway, portrait RenderTexture, exact requested feet post, one full natural native loop from a completed seek to the beginning, no timed hold, manual pause/resume followed by another natural wrap without another seek, and bounded pool release/reacquisition. Foreground callbacks are simulated for batch mode; physical-device/Web behavior requires additional validation.";
            public List<string> checks = new List<string>(), errors = new List<string>(), runtimeErrors = new List<string>(), editorDiagnostics = new List<string>();
        }
    }
}
#endif
