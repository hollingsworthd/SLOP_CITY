#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Real filesystem failures in the native cache path; never writes to player storage or calls TikTok.</summary>
    [InitializeOnLoad]
    public static class SlopCityCacheFailureSmokeTest
    {
        private const string ActiveKey = "SlopCity.CacheFailureSmoke.Active";
        private const string StartedKey = "SlopCity.CacheFailureSmoke.Started";
        private const string ReportPath = "Builds/Reports/cache-failure-check.json";
        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt report;
        private static SlopCityTikTokSync sync;
        private static GameObject owner;
        private static string temporaryDirectory;
        private static double enteredAt;
        private static bool started;

        static SlopCityCacheFailureSmokeTest()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                if (change == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (change == PlayModeStateChange.EnteredEditMode) Finish(new Exception("Play Mode ended before cache failure checks completed."));
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

        // Dedicated graphics-enabled batch Editor; omit -quit and -nographics.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Cache failure smoke requires a dedicated batch Editor outside Play Mode.");
            SessionState.SetString(StartedKey, DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(ActiveKey, true);
            report = NewReceipt(); WriteReport();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static Receipt NewReceipt() => new Receipt
        { startedAtUtc = SessionState.GetString(StartedKey, DateTime.UtcNow.ToString("O")), unityVersion = Application.unityVersion };
        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (report == null) report = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(report.startedAtUtc).ToUniversalTime()).TotalSeconds > 90d)
                    throw new TimeoutException("Cache failure smoke exceeded 90 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (!started)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < .5d) return;
                    started = true; Work.Push(Exercise());
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
            temporaryDirectory = Path.Combine(Path.GetTempPath(), "slop-cache-failure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            report.temporaryDirectory = temporaryDirectory;
            owner = new GameObject("CacheFailure_Resolver");
            sync = owner.AddComponent<SlopCityTikTokSync>();
            sync.enabled = false; // Never run Start/live sync or touch persistentDataPath.
            string blocker = Path.Combine(temporaryDirectory, "blocked-parent");
            File.WriteAllText(blocker, "A file occupies the required directory path.");
            string unavailable = Path.Combine(blocker, "media");
            bool ready = (bool)Invoke("PrepareCacheDirectory", unavailable);
            Check(!ready && sync.Status.Contains("Media cache unavailable"), "Real directory creation failure is contained and visible in resolver status.");
            yield return (IEnumerator)Invoke("LoadBundledCatalog");
            Check(sync.Catalog.videos.Length > 0, "Production bundled catalogue still loads after cache directory failure.");
            SlopCityTikTokCatalog baseline = sync.Catalog;
            int callbacks = 0;
            string resolved = null;
            sync.ResolveVideo(Array.Find(baseline.videos, post => post.kind != "slideshow"), url => { callbacks++; resolved = url; });
            Check(callbacks == 1 && !string.IsNullOrEmpty(resolved), "Bundled video resolves immediately while native cache is unavailable.");

            var remote = new SlopCityTikTokVideo
            {
                id = "9999999999999999999", creator = "machine_publishers", kind = "video",
                sourceUrl = "https://www.tiktok.com/@machine_publishers/video/9999999999999999999",
                mediaUrl = "https://v16.tiktokcdn.com/video/cache-failure-fixture", duration = 15
            };
            string filename = remote.id + ".mp4";
            callbacks = 0; resolved = "unexpected";
            Invoke("ResolveCached", remote, "video", filename, -1, (Action<string>)(url => { callbacks++; resolved = url; }), unavailable);
            Check(callbacks == 1 && resolved == null && PendingCount() == 0 && PinnedCount() == 0,
                "Failed cache resolve returns null once without leaving pending consumers or pins.");
            Check(ReferenceEquals(sync.Catalog, baseline), "Failed cache use preserves the usable prior catalogue.");

            File.Delete(blocker);
            Directory.CreateDirectory(unavailable);
            byte[] header = new byte[256]; header[4] = (byte)'f'; header[5] = (byte)'t'; header[6] = (byte)'y'; header[7] = (byte)'p';
            string cached = Path.Combine(unavailable, filename);
            File.WriteAllBytes(cached, header);
            callbacks = 0; resolved = null;
            Invoke("ResolveCached", remote, "video", filename, -1, (Action<string>)(url => { callbacks++; resolved = url; }), unavailable);
            Check(callbacks == 1 && resolved == new Uri(cached).AbsoluteUri && !sync.Status.Contains("Media cache unavailable"),
                "Later cache use retries the recovered directory and resolves an existing valid cache entry.");

            // A cache directory may become unavailable after Resolve succeeds but before
            // Download opens its file. Exercise the actual iterator's failure/finally path.
            string vanished = Path.Combine(temporaryDirectory, "vanished-directory");
            File.WriteAllText(vanished, "A restored file replaced this cache directory.");
            string failedPath = Path.Combine(vanished, filename);
            callbacks = 0; resolved = "unexpected";
            object download = NewDownload(remote, "video", failedPath, filename, url => { callbacks++; resolved = url; });
            PendingDownloads()[filename] = download;
            yield return (IEnumerator)Invoke("Download", download);
            Check(callbacks == 1 && resolved == null && PendingCount() == 0 && PinnedCount() == 0,
                "Download file-open failure drains consumers and pins through its production finally block.");
            Check(!File.Exists(failedPath + ".part") && ReferenceEquals(sync.Catalog, baseline),
                "Failed download leaves no partial media and preserves previous catalogue data.");

            object[] requestArguments = { remote.mediaUrl, failedPath + ".part", (long)1024 * 1024, null, null };
            bool opened = (bool)StaticMethod("TryCreateDownloadRequest").Invoke(null, requestArguments);
            Check(!opened && requestArguments[3] == null && requestArguments[4] == null, "File-handler creation failure returns no live request or stream.");
            // The helper is also inspected on its successful ownership-transfer path without sending it.
            string validPart = Path.Combine(temporaryDirectory, "request.part");
            requestArguments = new object[] { remote.mediaUrl, validPart, (long)1024 * 1024, null, null };
            opened = (bool)StaticMethod("TryCreateDownloadRequest").Invoke(null, requestArguments);
            using (FileStream output = requestArguments[4] as FileStream)
            using (UnityWebRequest request = requestArguments[3] as UnityWebRequest)
                Check(opened && request != null && output != null && request.downloadHandler != null,
                    "Successful request creation transfers its bounded handler and stream to the caller for disposal.");
            if (File.Exists(validPart)) File.Delete(validPart);

            string png = Path.Combine(temporaryDirectory, "source.png");
            var original = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try { original.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white }); original.Apply(); File.WriteAllBytes(png, original.EncodeToPNG()); }
            finally { Object.DestroyImmediate(original); }
            string destinationDirectory = Path.Combine(temporaryDirectory, "jpeg-write-denied");
            Directory.CreateDirectory(destinationDirectory);
            int baselineTextures = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                ExpectImageIoFailure(png, destinationDirectory);
                yield return null; // Allow Destroy in the production finally to run.
            }
            ExpectImageIoFailure(Path.Combine(temporaryDirectory, "missing-source.jpg"), Path.Combine(temporaryDirectory, "missing-output.jpg"));
            yield return null;
            report.texturesBeforeFailures = baselineTextures;
            report.texturesAfterFailures = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            Check(report.texturesAfterFailures == baselineTextures, "Twenty failed JPEG writes and a missing source leave no decoded Texture2D objects.");
            Check(report.runtimeErrors.Count == 0, "Expected filesystem failures produce no unhandled runtime errors.");
        }

        private static void ExpectImageIoFailure(string source, string destination)
        {
            try { StaticMethod("ConvertImageWithUnity").Invoke(null, new object[] { source, destination }); }
            catch (TargetInvocationException exception) when (exception.InnerException is IOException || exception.InnerException is UnauthorizedAccessException)
            { report.expectedExceptions.Add(exception.InnerException.GetType().Name); return; }
            throw new InvalidOperationException("The real filesystem fixture did not reject its image read/write.");
        }
        private static object NewDownload(SlopCityTikTokVideo post, string kind, string path, string filename, Action<string> callback)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Type type = typeof(SlopCityTikTokSync).GetNestedType("DownloadWork", BindingFlags.NonPublic);
            object work = Activator.CreateInstance(type, true);
            foreach (KeyValuePair<string, object> field in new Dictionary<string, object> {
                { "post", post }, { "kind", kind }, { "path", path }, { "filename", filename }, { "imageIndex", -1 },
                { "temporary", path + ".fixture.part" }, { "converted", path + ".fixture.convert" } })
                type.GetField(field.Key, fields).SetValue(work, field.Value);
            Type consumerType = typeof(SlopCityTikTokSync).GetNestedType("Consumer", BindingFlags.NonPublic);
            object consumer = Activator.CreateInstance(consumerType, true);
            consumerType.GetField("callback", fields).SetValue(consumer, callback);
            ((IList)type.GetField("consumers", fields).GetValue(work)).Add(consumer);
            return work;
        }
        private static object Invoke(string name, params object[] arguments)
        {
            MethodInfo method = typeof(SlopCityTikTokSync).GetMethod(name, InstancePrivate);
            ParameterInfo[] parameters = method.GetParameters();
            int supplied = arguments.Length;
            Array.Resize(ref arguments, parameters.Length);
            for (int index = supplied; index < parameters.Length; index++) arguments[index] = Type.Missing;
            return method.Invoke(sync, arguments);
        }
        private static MethodInfo StaticMethod(string name) => typeof(SlopCityTikTokSync).GetMethod(name, StaticPrivate);
        private static IDictionary PendingDownloads() => (IDictionary)typeof(SlopCityTikTokSync).GetField("downloads", InstancePrivate).GetValue(sync);
        private static int PendingCount() => PendingDownloads().Count;
        private static int PinnedCount() => ((HashSet<string>)typeof(SlopCityTikTokSync).GetField("pinned", InstancePrivate).GetValue(sync)).Count;
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            report.checks.Add(description);
        }
        private static void Finish(Exception exception)
        {
            Work.Clear();
            if (report == null) report = NewReceipt();
            if (exception != null) report.errors.Add(exception.ToString());
            try
            {
                if (owner) Object.DestroyImmediate(owner);
                if (!string.IsNullOrEmpty(temporaryDirectory) && Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
                report.temporaryFilesRemoved = true;
            }
            catch (Exception cleanupError) { report.errors.Add("Temporary fixture cleanup: " + cleanupError); }
            SessionState.SetBool(ActiveKey, false);
            report.passed = report.errors.Count == 0 && report.runtimeErrors.Count == 0;
            report.result = report.passed ? "Passed" : "Failed";
            report.finishedAtUtc = DateTime.UtcNow.ToString("O");
            WriteReport(); Debug.Log("SLOP_CITY_CACHE_FAILURE_SMOKE_" + report.result.ToUpperInvariant() + " " + ReportPath);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
        private static void WriteReport()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }
        [Serializable] private sealed class Receipt
        {
            public string result = "Running", startedAtUtc, finishedAtUtc, unityVersion, temporaryDirectory;
            public bool passed, temporaryFilesRemoved;
            public int texturesBeforeFailures, texturesAfterFailures;
            public string scope = "Actual temporary filesystem failures with production cache helpers, bundled catalogue loading, callback/pin drainage, directory recovery and Unity texture conversion cleanup. Empty Play Mode scene; automatic Start/live sync disabled. No network requests are sent and player storage is untouched. These recoverable exception tests do not establish a process crash cause.";
            public List<string> checks = new List<string>(), errors = new List<string>(), runtimeErrors = new List<string>(), editorDiagnostics = new List<string>(), warnings = new List<string>(), expectedExceptions = new List<string>();
        }
    }
}
#endif
