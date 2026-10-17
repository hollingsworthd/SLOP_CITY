#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Offline checks of the production bundled loader, resolvers and live-metadata overlay.</summary>
    public static class SlopCityBundledMediaSmokeTest
    {
        private const string ReportPath = "Builds/Reports/bundled-media-check.json";
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
        private static Receipt report;
        private static GameObject fixture;
        private static double started;

        // Dedicated batch Editor; omit -quit. This runner exits itself and never starts live sync.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Bundled media checks require a dedicated batch Editor outside Play Mode.");
            report = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            started = EditorApplication.timeSinceStartup;
            work.Push(Exercise());
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - started > 180d) throw new TimeoutException("Bundled media checks exceeded three minutes.");
                if (work.Count == 0) { Finish(null); return; }
                IEnumerator current = work.Peek();
                if (!current.MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); return; }
                if (current.Current is IEnumerator nested) work.Push(nested);
            }
            catch (Exception error) { Finish(error); }
        }

        private static IEnumerator Exercise()
        {
            fixture = new GameObject("Bundled_Media_Offline_Check") { hideFlags = HideFlags.HideAndDontSave };
            SlopCityTikTokSync sync = fixture.AddComponent<SlopCityTikTokSync>();
            sync.enabled = false;
            int publications = 0;
            sync.CatalogChanged += _ => publications++;
            MethodInfo load = typeof(SlopCityTikTokSync).GetMethod("LoadBundledCatalog", PrivateInstance);
            Check(load != null, "Production bundled loader is available.");
            yield return (IEnumerator)load.Invoke(sync, null);
            SlopCityTikTokCatalog catalog = sync.Catalog;
            Check(publications == 1 && catalog.videos.Length > 0, "Offline startup publishes the bundled catalogue without live sync.");
            Check(!sync.IsRefreshing, "Bundled startup does not start a live network refresh.");
            Check(catalog.accounts.Length == 2 && catalog.accounts.All(account => account.complete
                && account.discoveredCount == catalog.videos.Count(post => post.creator == account.creator)
                && (account.totalReported < 0 || account.discoveredCount >= account.totalReported)), "Both bundled accounts contain their complete discovered public listings.");
            report.bundledAtUtc = catalog.bundledAtUtc;
            report.posts = catalog.videos.Length;
            report.videos = catalog.videos.Count(post => post.kind != "slideshow");
            report.slideshows = catalog.videos.Count(post => post.kind == "slideshow");
            var expectedFiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (SlopCityTikTokVideo post in catalog.videos)
            {
                Check(SlopCityTikTokParser.ValidateVideo(post, post.creator), "Bundled post validates: " + post.id);
                Resolve(callback => sync.ResolveThumbnail(post, callback), post.bundledThumbnailFile, expectedFiles);
                if (post.kind == "slideshow")
                {
                    for (int index = 0; index < post.imageUrls.Length; index++)
                    {
                        int frame = index;
                        Resolve(callback => sync.ResolveImage(post, frame, callback), post.bundledImageFiles[frame], expectedFiles);
                        report.images++;
                    }
                    if (!string.IsNullOrEmpty(post.audioUrl))
                    {
                        Resolve(callback => sync.ResolveAudio(post, callback), post.bundledAudioFile, expectedFiles);
                        report.audioTracks++;
                    }
                }
                else Resolve(callback => sync.ResolveVideo(post, callback), post.bundledVideoFile, expectedFiles);

                // SyncAccounts invokes this production helper on each freshly parsed live DTO.
                var refreshed = JsonUtility.FromJson<SlopCityTikTokVideo>(JsonUtility.ToJson(post));
                refreshed.bundledVideoFile = refreshed.bundledAudioFile = refreshed.bundledThumbnailFile = "";
                refreshed.bundledImageFiles = Array.Empty<string>();
                refreshed.title = "Fresh live metadata " + post.id;
                MethodInfo overlay = typeof(SlopCityTikTokSync).GetMethod("ApplyBundledFiles", PrivateInstance);
                Check(overlay != null, "Production live-metadata overlay is available.");
                overlay.Invoke(sync, new object[] { refreshed });
                Check(refreshed.bundledVideoFile == post.bundledVideoFile
                    && refreshed.bundledAudioFile == post.bundledAudioFile
                    && refreshed.bundledThumbnailFile == post.bundledThumbnailFile
                    && refreshed.bundledImageFiles.SequenceEqual(post.bundledImageFiles)
                    && refreshed.title == "Fresh live metadata " + post.id,
                    "Fresh live metadata retains every bundled fallback path without replacing live fields: " + post.id);
                yield return null;
            }
            report.assets = expectedFiles.Count;
            var downloads = (IDictionary)typeof(SlopCityTikTokSync).GetField("downloads", PrivateInstance).GetValue(sync);
            Check(downloads.Count == 0, "Every bundled asset resolves without a native download/cache operation.");
            string mediaDirectory = Path.Combine(Application.streamingAssetsPath, "TikTok", "media");
            var receipt = JsonUtility.FromJson<BundleReceipt>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "TikTok", "bundle-receipt.json")));
            Check(receipt != null && receipt.passed && receipt.postCount == catalog.videos.Length, "Bundle provenance receipt matches the loaded catalogue.");
            var assets = receipt.posts.SelectMany(post => post.assets).ToArray();
            Check(assets.Select(asset => asset.file).Distinct().Count() == expectedFiles.Count
                && expectedFiles.SetEquals(assets.Select(asset => asset.file)), "Every referenced asset appears exactly once in the provenance receipt.");
            foreach (BundleAsset asset in assets)
            {
                string path = Path.Combine(mediaDirectory, asset.file);
                Check(new FileInfo(path).Length == asset.bytes && asset.bytes > 128 && asset.bytes < 24L * 1024 * 1024,
                    "Packaged asset length matches the host-safe receipt: " + asset.file);
                using (var stream = File.OpenRead(path))
                using (SHA256 hash = SHA256.Create())
                    Check(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == asset.sha256,
                        "Packaged asset SHA256 matches: " + asset.file);
                report.bytes += asset.bytes;
                yield return null;
            }
            Check(report.bytes == receipt.totalBytes, "Verified byte count matches the complete bundle receipt.");
            Check(!SlopCityTikTokSync.IsBundledMediaUri("../catalog.json", "file:///tmp/catalog.json"), "Traversal filenames cannot become bundled media paths.");
            Check(!SlopCityTikTokSync.IsBundledMediaUri(catalog.videos[0].bundledThumbnailFile, "https://example.com/other.jpg"), "Bundled URL validation rejects external substitutes.");
        }

        private static void Resolve(Action<Action<string>> resolver, string filename, HashSet<string> files)
        {
            string result = null;
            int callbacks = 0;
            resolver(uri => { callbacks++; result = uri; });
            Check(callbacks == 1 && !string.IsNullOrEmpty(result), "Bundled resolver completes immediately exactly once: " + filename);
            Check(SlopCityTikTokSync.IsBundledMediaUri(filename, result), "Bundled URI resolves to the expected packaged file: " + filename);
            Check(Uri.TryCreate(result, UriKind.Absolute, out Uri uri) && uri.IsFile && File.Exists(uri.LocalPath),
                "Native packaged file exists: " + filename);
            files.Add(filename);
        }

        private static void Check(bool condition, string assertion)
        {
            report.checks++;
            if (!condition) throw new InvalidOperationException(assertion);
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            if (fixture) Object.DestroyImmediate(fixture);
            if (error != null) report.errors.Add(error.ToString());
            report.passed = error == null;
            report.finishedAtUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            Debug.Log("SLOP_CITY_BUNDLED_MEDIA_CHECK " + (report.passed ? "PASSED " : "FAILED ") + report.checks + " checks");
            EditorApplication.Exit(report.passed ? 0 : 1);
        }

        [Serializable] private sealed class Receipt
        {
            public string scope = "Offline production catalogue loading and bundled-first URI resolution; fresh live DTO fallback-path overlay; every packaged asset SHA256/size. No live request, decoder playback, screen assignment, or audio spatial behavior is claimed.";
            public string startedAtUtc, finishedAtUtc, bundledAtUtc, unityVersion;
            public bool passed;
            public int checks, posts, videos, slideshows, images, audioTracks, assets;
            public long bytes;
            public List<string> errors = new List<string>();
        }
        [Serializable] private sealed class BundleReceipt
        {
            public bool passed = false;
            public int postCount = 0;
            public long totalBytes = 0;
            public BundlePost[] posts = Array.Empty<BundlePost>();
        }
        [Serializable] private sealed class BundlePost { public BundleAsset[] assets = Array.Empty<BundleAsset>(); }
        [Serializable] private sealed class BundleAsset { public string file = "", sha256 = ""; public long bytes = 0; }
    }
}
#endif
