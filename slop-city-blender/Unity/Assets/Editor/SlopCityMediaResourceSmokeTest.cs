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
    /// <summary>Cancel the production WAV decoder while it owns an uncommitted AudioClip.</summary>
    [InitializeOnLoad]
    public static class SlopCityMediaResourceSmokeTest
    {
        private const string ActiveKey = "SlopCity.MediaResourceSmoke.Active";
        private const string StartedKey = "SlopCity.MediaResourceSmoke.Started";
        private const string ReportPath = "Builds/Reports/media-resource-check.json";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt report;
        private static GameObject owner;
        private static string directory;
        private static double enteredAt;
        private static bool started;

        static SlopCityMediaResourceSmokeTest()
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

        // Dedicated batch Editor; omit -quit. Uses an empty scene and private temporary WAV, never player data or network.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Media resource smoke requires a dedicated batch Editor outside Play Mode.");
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
                if ((DateTime.UtcNow - DateTime.Parse(report.startedAtUtc).ToUniversalTime()).TotalSeconds > 90d) throw new TimeoutException("Media resource fixture exceeded 90 seconds.");
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
        private static IEnumerator Exercise()
        {
            directory = Path.Combine(Path.GetTempPath(), "slop-media-resources-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string wav = Path.Combine(directory, "cancel.wav");
            const int frames = 400000;
            using (var writer = new BinaryWriter(File.Create(wav)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + frames * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(44100); writer.Write(88200); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(frames * 2); writer.Write(new byte[frames * 2]);
            }
            for (int round = 0; round < 12; round++)
            {
                owner = new GameObject("MediaResource_Cancellation");
                SlopCityCityMedia city = owner.AddComponent<SlopCityCityMedia>();
                city.enabled = false; // Suppress production Start, scene discovery and persistence.
                Type channelType = typeof(SlopCityCityMedia).GetNestedType("Channel", BindingFlags.NonPublic);
                object channel = Activator.CreateInstance(channelType, true);
                channelType.GetField("audio").SetValue(channel, owner.AddComponent<AudioSource>());
                channelType.GetField("slideshow").SetValue(channel, true);
                channelType.GetField("audioLoading").SetValue(channel, true);
                var channels = (Array)typeof(SlopCityCityMedia).GetField("channels", Private).GetValue(city);
                channels.SetValue(channel, 0);
                int baseline = MusicClips().Length;
                var routine = (IEnumerator)typeof(SlopCityCityMedia).GetMethod("LoadPhotoAudio", Private).Invoke(city, new[] { channel, (object)0, new Uri(wav).AbsoluteUri });
                object owned = typeof(SlopCityCityMedia).GetField("mediaCoroutines", Private).GetValue(city);
                owned.GetType().GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owned, new object[] { city, routine });
                double deadline = EditorApplication.timeSinceStartup + 15d;
                while (MusicClips().Length == baseline && EditorApplication.timeSinceStartup < deadline) yield return null;
                Check(MusicClips().Length == baseline + 1 && channelType.GetField("photoAudio").GetValue(channel) == null,
                    "Production WAV decoder yielded with a real uncommitted AudioClip before ownership transfer.");
                city.enabled = true; // Re-enable and deactivate in the same tick, before production Start can run.
                owner.SetActive(false); // Real OnDisable stops the nested coroutine at its actual PCM decode yield.
                yield return null; yield return null;
                report.clipsAfterOwnerCancellation = MusicClips().Length - baseline;
                Check(report.clipsAfterOwnerCancellation == 0, "Disabling the media owner destroys the in-flight, uncommitted AudioClip.");
                Check(!(bool)channelType.GetField("audioLoading").GetValue(channel), "Cancellation clears audio-loading state for a later resume.");
                Object.DestroyImmediate(owner); owner = null;
                report.cancellationRounds++;
            }
            yield return ExerciseWaveCompletion(wav);
            yield return ExerciseNativeCancellation();
            yield return ExerciseWaits();
            yield return ExerciseRetryLifetime();
        }
        private static IEnumerator ExerciseWaveCompletion(string wav)
        {
            owner = new GameObject("MediaResource_NormalWaveCompletion");
            SlopCityCityMedia city = owner.AddComponent<SlopCityCityMedia>(); city.enabled = false;
            Type channelType = typeof(SlopCityCityMedia).GetNestedType("Channel", BindingFlags.NonPublic);
            object channel = Activator.CreateInstance(channelType, true);
            channelType.GetField("audio").SetValue(channel, owner.AddComponent<AudioSource>());
            channelType.GetField("slideshow").SetValue(channel, true);
            channelType.GetField("audioLoading").SetValue(channel, true);
            ((Array)typeof(SlopCityCityMedia).GetField("channels", Private).GetValue(city)).SetValue(channel, 0);
            int baseline = MusicClips().Length;
            var routine = (IEnumerator)typeof(SlopCityCityMedia).GetMethod("LoadPhotoAudio", Private).Invoke(city, new[] { channel, (object)0, new Uri(wav).AbsoluteUri });
            object owned = typeof(SlopCityCityMedia).GetField("mediaCoroutines", Private).GetValue(city);
            owned.GetType().GetMethod("Start", Private).Invoke(owned, new object[] { city, routine });
            double deadline = EditorApplication.timeSinceStartup + 15d;
            while (channelType.GetField("photoAudio").GetValue(channel) == null && EditorApplication.timeSinceStartup < deadline) yield return null;
            AudioClip completed = (AudioClip)channelType.GetField("photoAudio").GetValue(channel);
            Check(completed && MusicClips().Length == baseline + 1 && ((AudioSource)channelType.GetField("audio").GetValue(channel)).clip == completed,
                "Normal production WAV completion transfers its live AudioClip to the channel and audio source.");
            Check(!(bool)channelType.GetField("audioLoading").GetValue(channel), "Normal completion clears the loading flag.");
            city.enabled = true; owner.SetActive(false);
            yield return null;
            Check(completed && MusicClips().Length == baseline + 1, "Disabling after completion retains owned music for playback resume.");
            Object.DestroyImmediate(owner); owner = null;
            yield return null; yield return null;
            Check(MusicClips().Length == baseline, "Destroying the completed channel releases its transferred AudioClip.");
        }
        private static IEnumerator ExerciseNativeCancellation()
        {
            owner = new GameObject("MediaResource_NativeCacheCancellation");
            SlopCityTikTokSync sync = owner.AddComponent<SlopCityTikTokSync>();
            sync.enabled = false; // No automatic startup/live requests or persistent cache writes.
            string response = Path.Combine(directory, "private-response.json");
            File.WriteAllText(response, new string(' ', 1024 * 1024));
            Type workType = typeof(SlopCityTikTokSync).GetNestedType("DownloadWork", BindingFlags.NonPublic);
            object download = Activator.CreateInstance(workType, true);
            var post = new SlopCityTikTokVideo { id = "9999999999999999999", creator = "machine_publishers", kind = "video",
                sourceUrl = new Uri(response).AbsoluteUri, mediaUrl = "", imageUrls = Array.Empty<string>() };
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            string filename = post.id + ".mp4", path = Path.Combine(directory, filename);
            foreach (KeyValuePair<string, object> field in new Dictionary<string, object> {
                { "post", post }, { "kind", "video" }, { "path", path }, { "filename", filename }, { "imageIndex", -1 },
                { "temporary", path + ".probe.part" }, { "converted", path + ".probe.convert" }, { "reservation", (long)1024 * 1024 } })
                workType.GetField(field.Key, fields).SetValue(download, field.Value);
            Type consumerType = typeof(SlopCityTikTokSync).GetNestedType("Consumer", BindingFlags.NonPublic);
            object consumer = Activator.CreateInstance(consumerType, true);
            int callbacks = 0;
            consumerType.GetField("callback", fields).SetValue(consumer, (Action<string>)(_ => callbacks++));
            ((IList)workType.GetField("consumers", fields).GetValue(download)).Add(consumer);
            var reservations = (IDictionary)typeof(SlopCityTikTokSync).GetField("activeCacheWork", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            int baselineReservations = reservations.Count;
            bool reserved = (bool)typeof(SlopCityTikTokSync).GetMethod("ReserveCache", Private).Invoke(sync, new[] { (object)directory, download });
            Check(reserved && reservations.Count == baselineReservations + 1, "Production cache budget reserves this private download before it begins.");
            var downloads = (IDictionary)typeof(SlopCityTikTokSync).GetField("downloads", Private).GetValue(sync);
            downloads.Add(filename, download);
            typeof(SlopCityTikTokSync).GetField("activeDownloads", Private).SetValue(sync, 1);
            var routine = (IEnumerator)typeof(SlopCityTikTokSync).GetMethod("Download", Private).Invoke(sync, new[] { download });
            object owned = typeof(SlopCityTikTokSync).GetField("mediaCoroutines", Private).GetValue(sync);
            owned.GetType().GetMethod("Start", Private).Invoke(owned, new object[] { sync, routine });
            // The invalid media URL takes production's public-page fallback. Only this
            // private fixture URL is injected into that fallback; no public request is sent.
            UnityWebRequest request = FindRequest(owned);
            Check(request != null, "Production nested Download/GetText coroutine owns an in-flight local-file request.");
            sync.enabled = true; owner.SetActive(false);
            Check(reservations.Count == baselineReservations && downloads.Count == 0,
                "Cancellation unwinds nested text download and releases the native cache reservation and pending work.");
            Check(((HashSet<string>)typeof(SlopCityTikTokSync).GetField("pinned", Private).GetValue(sync)).Count == 0
                && (int)typeof(SlopCityTikTokSync).GetField("activeDownloads", Private).GetValue(sync) == 0 && callbacks == 1,
                "Cancelled cache work releases its pin and worker slot and completes its consumer exactly once.");
            FieldInfo pointer = typeof(UnityWebRequest).GetField("m_Ptr", Private);
            Check(pointer != null && (IntPtr)pointer.GetValue(request) == IntPtr.Zero,
                "Cancelled nested UnityWebRequest has released its actual native request pointer.");
            Object.DestroyImmediate(owner); owner = null;
            Check(callbacks == 1, "Destroying an already cancelled cache owner does not repeat consumer callbacks.");
            yield return null;
        }
        private static UnityWebRequest FindRequest(object owned)
        {
            var stacks = (IEnumerable)owned.GetType().GetField("active", Private).GetValue(owned);
            foreach (IEnumerable stack in stacks)
                foreach (object iterator in stack)
                    foreach (FieldInfo field in iterator.GetType().GetFields(Private))
                        if (field.GetValue(iterator) is UnityWebRequest request) return request;
            return null;
        }
        private static IEnumerator ExerciseWaits()
        {
            owner = new GameObject("MediaResource_Waits");
            SlopCityCityMedia city = owner.AddComponent<SlopCityCityMedia>(); city.enabled = false;
            object owned = typeof(SlopCityCityMedia).GetField("mediaCoroutines", Private).GetValue(city);
            var events = new List<string>();
            owned.GetType().GetMethod("Start", Private).Invoke(owned, new object[] { city, Outer(events) });
            Check(events.Count == 1 && events[0] == "entered", "Owned nested iterator starts and yields its custom realtime wait.");
            yield return null;
            Check(events.Count == 1, "CustomYieldInstruction retains Unity wait semantics across the next frame.");
            city.enabled = true; owner.SetActive(false);
            Check(events.Count == 3 && events[1] == "inner disposed" && events[2] == "outer disposed",
                "Cancellation disposes nested iterators from inner to outer exactly once.");
            Object.DestroyImmediate(owner); owner = null;
            Check(events.Count == 3, "Repeated teardown does not dispose completed ownership twice.");
        }
        private static IEnumerator Outer(List<string> events)
        { try { yield return Inner(events); } finally { events.Add("outer disposed"); } }
        private static IEnumerator Inner(List<string> events)
        { try { events.Add("entered"); yield return new WaitForSecondsRealtime(10f); events.Add("wait completed"); } finally { events.Add("inner disposed"); } }
        private static IEnumerator ExerciseRetryLifetime()
        {
            owner = new GameObject("MediaResource_RetryLifetime");
            SlopCityCityMedia city = owner.AddComponent<SlopCityCityMedia>(); city.enabled = false;
            typeof(SlopCityCityMedia).GetMethod("ReadConfiguration", Private).Invoke(city, null);
            typeof(SlopCityCityMedia).GetField("initialized", Private).SetValue(city, true);
            var channels = (Array)typeof(SlopCityCityMedia).GetField("channels", Private).GetValue(city);
            object channel = channels.GetValue(0);
            var retries = (IDictionary)typeof(SlopCityCityMedia).GetField("retries", Private).GetValue(city);
            SlopCityTikTokVideo retained = RetryPost("7999999999999999999");
            SlopCityTikTokVideo removed = RetryPost("8999999999999999999");
            city.ApplyTikTokCatalog(RetryCatalog(true, retained, removed));
            FailPost(city, channel, retained); FailPost(city, channel, removed);
            object originalBackoff = retries[retained.creator + "/" + retained.id];
            city.ApplyTikTokCatalog(RetryCatalog(false, retained));
            Check(retries.Count == 2, "Partial catalog refresh preserves retries for posts missing from its partial listing.");
            var contradictory = RetryCatalog(true, retained);
            contradictory.accounts[0].discoveredCount = 2;
            city.ApplyTikTokCatalog(contradictory);
            Check(retries.Count == 2, "Contradictory complete-account count does not establish removal or clear backoff.");
            city.ApplyTikTokCatalog(RetryCatalog(true, retained));
            Check(retries.Count == 1 && ReferenceEquals(originalBackoff, retries[retained.creator + "/" + retained.id]),
                "Complete replacement removes only the departed post's retry and preserves retained backoff.");
            for (int round = 0; round < 12; round++)
            {
                SlopCityTikTokVideo transient = RetryPost((9000000000000000000L + round).ToString(System.Globalization.CultureInfo.InvariantCulture));
                city.ApplyTikTokCatalog(RetryCatalog(true, retained, transient)); FailPost(city, channel, transient);
                Check(retries.Count == 2, "Production decoder failure adds one current-post backoff.");
                city.ApplyTikTokCatalog(RetryCatalog(true, retained));
                Check(retries.Count == 1 && ReferenceEquals(originalBackoff, retries[retained.creator + "/" + retained.id]),
                    "Repeated complete catalog turnover keeps retry storage bounded by current posts.");
            }
            // The second creator's missing/incomplete record cannot remove its prior retry.
            SlopCityTikTokVideo other = RetryPost("7888888888888888888"); other.creator = "1984drh";
            other.sourceUrl = "https://www.tiktok.com/@1984drh/video/" + other.id;
            FailPost(city, channel, other);
            city.ApplyTikTokCatalog(RetryCatalog(true, retained));
            Check(retries.Count == 2, "A complete account snapshot preserves the other creator's unconfirmed retry.");
            var empty = new SlopCityTikTokCatalog { accounts = new[] { new SlopCityTikTokAccount { creator = "machine_publishers", complete = true, discoveredCount = 0, totalReported = 0 } }, videos = Array.Empty<SlopCityTikTokVideo>() };
            city.ApplyTikTokCatalog(empty);
            Check(retries.Count == 1 && retries.Contains(other.creator + "/" + other.id), "Confirmed empty account releases its retries while retaining the unconfirmed creator.");
            Object.DestroyImmediate(owner); owner = null;
            yield return null;
        }
        private static SlopCityTikTokVideo RetryPost(string id) => new SlopCityTikTokVideo {
            creator = "machine_publishers", id = id, kind = "video", sourceUrl = "https://www.tiktok.com/@machine_publishers/video/" + id };
        private static SlopCityTikTokCatalog RetryCatalog(bool complete, params SlopCityTikTokVideo[] posts) => new SlopCityTikTokCatalog {
            accounts = new[] { new SlopCityTikTokAccount { creator = "machine_publishers", complete = complete, discoveredCount = posts.Length, totalReported = posts.Length } }, videos = posts };
        private static void FailPost(SlopCityCityMedia city, object channel, SlopCityTikTokVideo post)
        {
            channel.GetType().GetField("pendingVideo").SetValue(channel, post);
            typeof(SlopCityCityMedia).GetMethod("FailChannel", Private).Invoke(city, new[] { channel, (object)"Expected fixture decoder failure." });
        }
        private static AudioClip[] MusicClips() => Array.FindAll(Resources.FindObjectsOfTypeAll<AudioClip>(), clip => clip.name == "City slideshow music");
        private static void Check(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); report.checks.Add(description); }
        private static void Finish(Exception exception)
        {
            Work.Clear();
            if (report == null) report = NewReceipt();
            if (exception != null) report.errors.Add(exception.ToString());
            try
            {
                if (owner) Object.DestroyImmediate(owner);
                // Includes leaked baseline objects, so the failing probe never pollutes a later fixture.
                foreach (AudioClip clip in MusicClips()) Object.DestroyImmediate(clip);
                if (directory != null && Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (Exception cleanup) { report.errors.Add(cleanup.ToString()); }
            SessionState.SetBool(ActiveKey, false); report.passed = report.errors.Count == 0 && report.runtimeErrors.Count == 0;
            report.finishedAtUtc = DateTime.UtcNow.ToString("O"); WriteReport();
            Debug.Log("SLOP_CITY_MEDIA_RESOURCE_SMOKE_" + (report.passed ? "PASSED" : "FAILED") + " " + ReportPath);
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
            public int clipsAfterOwnerCancellation, cancellationRounds;
            public string scope = "Twelve actual production WAV cancellations at incremental PCM yield, normal completed-clip transfer/retention/destruction and nested native cache/text-request cancellation with private local files; native request pointer, reservations, pins, worker count and callback drainage. Empty scene; automatic Start/live sync disabled; no player data or public network. Custom wait semantics, inner-to-outer cleanup and complete/partial catalog retry lifetimes checked. Does not prove physical-device crash recovery.";
            public List<string> checks = new List<string>(), errors = new List<string>(), runtimeErrors = new List<string>(), editorDiagnostics = new List<string>(), warnings = new List<string>();
        }
    }
}
#endif
