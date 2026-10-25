using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace SlopCity
{
    /// <summary>
    /// Plays the sealed bundled creator catalogue without live account synchronization.
    /// Missing bundled media stays unavailable rather than acquiring replacement bytes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityTikTokSync : MonoBehaviour
    {
        public const string UserAgent = "SlopCity/0.8";
        public const string ApiEndpoint = "https://www.tiktok.com/api/creator/item_list/";
        private const int MaxPages = 1000;
        private const long MaxMediaBytes = 512L * 1024 * 1024;
        private const long MaxCacheBytes = 2L * 1024 * 1024 * 1024;
        private static readonly string[] Creators = { "1984drh", "machine_publishers" };
        // These are public account identifiers returned in the accounts' public post
        // metadata, not access tokens. Every returned post must match its creator.
        private static readonly string[] PublicIds = {
            "MS4wLjABAAAANqovoiLnOvAKhLkQEulN5JPLfBWj1h3k961etm8QuKmmkl_ak1svDsVBrdu4J7HU",
            "MS4wLjABAAAAGvvrkD-_D5H7MxpzKhFGnof7MvexPitzLGaPzlgzmOlE3wRKRTf68V_ayYD4bcmG"
        };
        private SlopCityTikTokCatalog catalog = new SlopCityTikTokCatalog();
        private readonly Dictionary<string, SlopCityTikTokVideo> bundledPosts = new Dictionary<string, SlopCityTikTokVideo>(StringComparer.Ordinal);
        private sealed class Consumer { internal Action<string> callback; internal Func<bool> needed; }
        private sealed class DownloadWork
        {
            internal SlopCityTikTokVideo post;
            internal string kind, path, filename, temporary, converted;
            internal int imageIndex;
            internal long reservation;
            internal readonly List<Consumer> consumers = new List<Consumer>();
        }
        private readonly SlopCityMediaCoroutines mediaCoroutines = new SlopCityMediaCoroutines();
        private bool initializationStarted, initializationComplete;
        private readonly Dictionary<string, DownloadWork> downloads = new Dictionary<string, DownloadWork>();
        private readonly Queue<DownloadWork> downloadQueue = new Queue<DownloadWork>();
        private const int MaxConcurrentDownloads = 2, MaxQueuedDownloads = 24;
        private int activeDownloads;
        // A native conversion may outlive its component. Retain its file ownership,
        // byte reservation and worker slot until the task has actually finished.
        private static readonly object CacheWorkLock = new object();
        private static readonly Dictionary<string, DownloadWork> activeCacheWork = new Dictionary<string, DownloadWork>(StringComparer.Ordinal);
        private readonly HashSet<string> pinned = new HashSet<string>(StringComparer.Ordinal);
        private float nextRefresh, refreshSeconds = 300;
        private bool syncing, destroying, cacheUnavailable;
#if !UNITY_WEBGL || UNITY_EDITOR
        private bool started, requested;
#endif
#if UNITY_WEBGL && !UNITY_EDITOR
        private const string BrowserStatus = "Loading the bundled creator catalogue…";
        private string status = BrowserStatus;
#else
        private string status = "Loading the bundled offline creator catalogue…";
#endif
        public static SlopCityTikTokSync Instance { get; private set; }
        public SlopCityTikTokCatalog Catalog => catalog;
        public bool IsRefreshing => syncing;
        public string Status => status + (cacheUnavailable ? " Media cache unavailable; bundled posts remain playable." : "");
        public string CacheDirectory => string.IsNullOrEmpty(Application.persistentDataPath) ? ""
            : Path.Combine(Application.persistentDataPath, "TikTok", "media");
        private string CatalogPath => string.IsNullOrEmpty(Application.persistentDataPath) ? ""
            : Path.Combine(Application.persistentDataPath, "TikTok", "catalog.json");
        public event Action<SlopCityTikTokCatalog> CatalogChanged;

        private void Awake() { Instance = this; }
        private void Start() { StartInitialization(); }
        private void OnEnable()
        {
            if (initializationStarted && !initializationComplete) StartInitialization();
        }
        private void StartInitialization()
        {
            initializationStarted = true;
            mediaCoroutines.Start(this, Initialize());
        }
        private IEnumerator Initialize()
        {
            // StreamingAssets uses HTTP in browsers and jar URLs on Android;
            // UnityWebRequest handles those and normal desktop file URLs alike.
            yield return LoadBundledCatalog();
            initializationComplete = true;
        }

        public void RequestRefresh()
        {
            status = bundledPosts.Count > 0 ? bundledPosts.Count + " bundled offline creator posts."
                : "Bundled catalogue unavailable; archive clips remain available.";
        }

        private IEnumerator LoadBundledCatalog()
        {
            using (UnityWebRequest request = SlopCityMediaTransfer.Get(StreamingAssetUri("TikTok/catalog.json"), 16 * 1024 * 1024, 30))
            {
                var operation = SlopCityMediaTransfer.Send(request);
                if (operation == null) { status = "Bundled catalogue unavailable; archive clips remain available."; yield break; }
                while (!operation.isDone)
                {
                    if (request.downloadedBytes > 16UL * 1024 * 1024) { request.Abort(); break; }
                    yield return null;
                }
                if (request.result != UnityWebRequest.Result.Success)
                {
                    status = "Bundled catalogue unavailable; archive clips remain available.";
                    Debug.LogWarning("SLOP CITY bundled creator catalogue could not load (HTTP " + request.responseCode + ").");
                    yield break;
                }
                SlopCityTikTokCatalog bundled = null;
                try
                {
                    bundled = JsonUtility.FromJson<SlopCityTikTokCatalog>(request.downloadHandler.text);
                    if (bundled == null || bundled.version != 2 || bundled.videos == null || bundled.videos.Length == 0
                        || bundled.accounts == null || bundled.accounts.Length != Creators.Length
                        || bundled.videos.Any(post => !ValidBundledPost(post))
                        || bundled.videos.Select(post => post.creator + "/" + post.id).Distinct().Count() != bundled.videos.Length)
                        throw new InvalidDataException("Bundled post metadata is incomplete or invalid.");
                    foreach (string creator in Creators)
                    {
                        SlopCityTikTokAccount account = bundled.accounts.Single(item => item != null && item.creator == creator);
                        int count = bundled.videos.Count(post => post.creator == creator);
                        if (!account.complete || account.discoveredCount != count || (account.totalReported >= 0 && count < account.totalReported))
                            throw new InvalidDataException("Bundled account is incomplete: " + creator);
                    }
                    foreach (SlopCityTikTokVideo post in bundled.videos) bundledPosts.Add(post.creator + "/" + post.id, post);
                    catalog = bundled;
                    status = bundled.videos.Length + " bundled creator posts / "
                        + bundled.videos.Count(post => post.kind == "slideshow") + " slideshows.";
                    Publish();
                }
                catch (Exception error)
                {
                    bundledPosts.Clear();
                    status = "Bundled catalogue failed validation; archive clips remain available.";
                    Debug.LogWarning("SLOP CITY bundled creator catalogue: " + error.Message);
                }
            }
        }

        private bool ValidBundledPost(SlopCityTikTokVideo post)
        {
            if (!ValidPost(post) || post.bundledThumbnailFile != post.id + "_thumb.jpg") return false;
            if (post.kind != "slideshow") return post.bundledVideoFile == post.id + ".mp4";
            return post.bundledImageFiles != null && post.bundledImageFiles.Length == post.imageUrls.Length
                && (string.IsNullOrEmpty(post.audioUrl) || post.bundledAudioFile == post.id + ".wav");
        }

        private void ApplyBundledFiles(SlopCityTikTokVideo post)
        {
            if (!bundledPosts.TryGetValue(post.creator + "/" + post.id, out SlopCityTikTokVideo bundled) || post.kind != bundled.kind) return;
            post.bundledVideoFile = bundled.bundledVideoFile;
            post.bundledThumbnailFile = bundled.bundledThumbnailFile;
            post.bundledAudioFile = bundled.bundledAudioFile;
            if ((post.imageUrls?.Length ?? 0) == bundled.imageUrls.Length) post.bundledImageFiles = bundled.bundledImageFiles;
        }

        private static string StreamingAssetUri(string relative)
        {
            string path = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" + relative;
            return path.IndexOf("://", StringComparison.Ordinal) >= 0 || path.StartsWith("jar:", StringComparison.Ordinal)
                ? path : new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }

        public static bool IsBundledMediaUri(string filename, string uri)
        {
            if (string.IsNullOrEmpty(filename) || string.IsNullOrEmpty(uri)) return false;
            bool safe = SlopCityTikTokParser.IsSafeLocalFile(filename) || IsOwnedCacheFile(filename);
            if (!safe && filename.EndsWith("_thumb.jpg", StringComparison.Ordinal))
                safe = SlopCityTikTokParser.IsSafeLocalFile(filename.Substring(0, filename.Length - "_thumb.jpg".Length) + ".mp4");
            return safe && string.Equals(uri, StreamingAssetUri("TikTok/media/" + filename), StringComparison.Ordinal);
        }

        private string BundledUri(SlopCityTikTokVideo post, string kind, int index = -1)
        {
            if (post == null || !bundledPosts.TryGetValue(post.creator + "/" + post.id, out SlopCityTikTokVideo bundled)) return null;
            string filename = kind == "video" ? bundled.bundledVideoFile : kind == "audio" ? bundled.bundledAudioFile
                : kind == "thumbnail" ? bundled.bundledThumbnailFile
                : index >= 0 && index < bundled.bundledImageFiles.Length ? bundled.bundledImageFiles[index] : "";
            if (string.IsNullOrEmpty(filename)) return null;
            string uri = StreamingAssetUri("TikTok/media/" + filename);
            return IsBundledMediaUri(filename, uri) ? uri : null;
        }

        public void ResolveThumbnail(SlopCityTikTokVideo post, Action<string> completed)
        {
            Complete(completed, !destroying && ValidPost(post) ? BundledUri(post, "thumbnail") : null);
        }

        private void LoadCatalog()
        {
            try
            {
                if (!File.Exists(CatalogPath) || new FileInfo(CatalogPath).Length > 16 * 1024 * 1024) return;
                var saved = JsonUtility.FromJson<SlopCityTikTokCatalog>(File.ReadAllText(CatalogPath));
                if (saved == null || saved.version != 2) return;
                var source = saved.videos ?? Array.Empty<SlopCityTikTokVideo>();
                bool filtered = source.Any(post => !ValidPost(post));
                saved.videos = source.Where(ValidPost)
                    .GroupBy(post => post.creator + "/" + post.id).Select(group => group.First()).ToArray();
                foreach (SlopCityTikTokVideo post in saved.videos)
                {
                    ApplyBundledFiles(post);
                    post.localFile = post.kind != "slideshow" && CachedMediaIsValid(Path.Combine(CacheDirectory, post.id + ".mp4"), "video") ? post.id + ".mp4" : "";
                }
                saved.videos = bundledPosts.Values.Concat(saved.videos).GroupBy(post => post.creator + "/" + post.id)
                    .Select(group => group.Last()).OrderByDescending(post => post.createTime).ThenBy(post => post.id, StringComparer.Ordinal).ToArray();
                saved.bundledAtUtc = catalog.bundledAtUtc;
                // The installed content selection takes precedence over an older native
                // cache. A catalogue without this optional setting keeps its cached pin.
                if (catalog.TryGetFeaturedVideo(out SlopCityTikTokVideo bundledFeatured))
                { saved.featuredCreator = bundledFeatured.creator; saved.featuredPostId = bundledFeatured.id; }
                saved.accounts = Creators.Select(creator =>
                {
                    var previous = (saved.accounts ?? Array.Empty<SlopCityTikTokAccount>()).FirstOrDefault(account => account != null && account.creator == creator);
                    int known = saved.videos.Count(post => post.creator == creator);
                    var account = AccountSnapshot(creator, previous);
                    account.discoveredCount = known;
                    account.complete = !filtered && previous != null && previous.complete
                        && previous.discoveredCount == known && (account.totalReported < 0 || known >= account.totalReported);
                    account.status = account.complete ? "Previously synchronized public catalogue." : "Cached public catalogue; refresh pending.";
                    return account;
                }).ToArray();
                catalog = saved;
                status = saved.videos.Length + " cached TikTok posts. Checking for updates…";
                Publish();
            }
            catch (Exception error) { Debug.LogWarning("TikTok catalogue cache: " + error.GetType().Name); }
        }

        private IEnumerator SyncAccounts()
        {
            syncing = true;
            nextRefresh = Time.unscaledTime + refreshSeconds;
            try
            {
                var merged = catalog.videos.ToDictionary(post => post.creator + "/" + post.id, post => post);
                // Each progress publication retains the untouched account's last known metadata.
                var accounts = Creators.Select(creator => AccountSnapshot(creator,
                    (catalog.accounts ?? Array.Empty<SlopCityTikTokAccount>()).FirstOrDefault(item => item != null && item.creator == creator))).ToArray();
                for (int accountIndex = 0; accountIndex < Creators.Length; accountIndex++)
                {
                    string creator = Creators[accountIndex];
                    SlopCityTikTokAccount previousAccount = accounts[accountIndex];
                    var account = new SlopCityTikTokAccount { creator = creator, profileUrl = "https://www.tiktok.com/@" + creator,
                        secUid = PublicIds[accountIndex], status = "checking", lastSyncUtc = previousAccount.lastSyncUtc };
                    var fetched = new Dictionary<string, SlopCityTikTokVideo>();
                    var cursors = new HashSet<long>();
                    long cursor = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    int rejected = 0;
                    bool receivedPage = false;
                    for (int pageNumber = 0; pageNumber < MaxPages; pageNumber++)
                    {
                        status = "Syncing @" + creator + " / " + fetched.Count + " posts…";
                        if (!cursors.Add(cursor)) { account.status = "Pagination stalled; previous posts retained."; break; }
                        string payload = null, failure = null;
                        yield return GetText(ListUrl(account.secUid, cursor), (text, error) => { payload = text; failure = error; });
                        if (payload == null) { account.status = failure ?? "Public listing unavailable; previous posts retained."; break; }
                        var page = SlopCityTikTokParser.ParsePublicList(payload, creator);
                        if (!page.valid || page.statusCode != 0)
                        { account.status = "TikTok listing unavailable (" + page.statusCode + "); previous posts retained."; break; }
                        receivedPage = true;
                        if (page.totalReported >= 0) account.totalReported = page.totalReported;
                        rejected += page.rejectedItems;
                        int previousCount = fetched.Count;
                        foreach (SlopCityTikTokVideo post in page.videos)
                        {
                            if (!SlopCityTikTokParser.ValidateVideo(post, creator)) { rejected++; continue; }
                            post.localFile = post.kind != "slideshow" && CachedMediaIsValid(Path.Combine(CacheDirectory, post.id + ".mp4"), "video") ? post.id + ".mp4" : "";
                            ApplyBundledFiles(post);
                            fetched[post.id] = post;
                        }
                        if (!page.hasMore)
                        {
                            account.complete = rejected == 0 && (account.totalReported < 0 || fetched.Count >= account.totalReported);
                            account.status = account.complete ? "All public posts synchronized." : "Some posts unavailable; previous posts retained.";
                            break;
                        }
                        long following = page.cursor;
                        if (following <= 0 || following >= cursor || fetched.Count == previousCount)
                        { account.status = "Pagination did not advance; previous posts retained."; break; }
                        cursor = following;
                        if (pageNumber == MaxPages - 1) account.status = "Listing safety limit reached; catalogue is partial.";
                        yield return new WaitForSecondsRealtime(.15f);
                    }
                    if (account.complete)
                        foreach (string key in merged.Keys.Where(key => key.StartsWith(creator + "/", StringComparison.Ordinal)
                            && !bundledPosts.ContainsKey(key)).ToArray()) merged.Remove(key);
                    foreach (SlopCityTikTokVideo post in fetched.Values)
                    {
                        string key = creator + "/" + post.id;
                        if (!account.complete && merged.TryGetValue(key, out SlopCityTikTokVideo previous)
                            && post.kind == previous.kind && string.IsNullOrEmpty(post.mediaUrl)) post.mediaUrl = previous.mediaUrl;
                        merged[key] = post;
                    }
                    if (!receivedPage) account.totalReported = previousAccount.totalReported;
                    if (account.complete) account.lastSyncUtc = DateTime.UtcNow.ToString("O");
                    account.discoveredCount = merged.Values.Count(post => post.creator == creator);
                    accounts[accountIndex] = account;
                    catalog = new SlopCityTikTokCatalog { syncedAtUtc = DateTime.UtcNow.ToString("O"), bundledAtUtc = catalog.bundledAtUtc,
                        featuredCreator = catalog.featuredCreator, featuredPostId = catalog.featuredPostId, accounts = accounts.ToArray(),
                        videos = merged.Values.OrderByDescending(post => post.createTime).ThenBy(post => post.id, StringComparer.Ordinal).ToArray() };
                    SaveCatalog();
                    Publish();
                }
                int photos = catalog.videos.Count(post => post.kind == "slideshow");
                status = catalog.videos.Length + " TikTok posts / " + photos + " slideshows"
                    + (accounts.All(account => account.complete) ? " / up to date." : " / partial sync; cached posts retained.");
            }
            finally
            {
                syncing = false;
                nextRefresh = Time.unscaledTime + refreshSeconds;
            }
        }

        public static string ListUrl(string secUid, long cursor) => ApiEndpoint + "?aid=1988&secUid=" + Uri.EscapeDataString(secUid)
            + "&count=15&cursor=" + cursor.ToString(CultureInfo.InvariantCulture) + "&type=1";

        private IEnumerator GetText(string url, Action<string, string> completed)
        {
            using (UnityWebRequest request = SlopCityMediaTransfer.Get(url, SlopCityTikTokParser.MaxDocumentCharacters, 20))
            {
                var operation = SendRequest(request, true);
                if (operation == null) { completed(null, "Public request could not start; previous posts retained."); yield break; }
                while (!operation.isDone)
                {
                    if (request.downloadedBytes > SlopCityTikTokParser.MaxDocumentCharacters) { request.Abort(); completed(null, "Public response exceeded its size limit."); yield break; }
                    yield return null;
                }
                if (request.result != UnityWebRequest.Result.Success)
                { completed(null, "TikTok request unavailable (HTTP " + request.responseCode + "); previous posts retained."); yield break; }
                string text = request.downloadHandler.text;
                completed(text.Length <= SlopCityTikTokParser.MaxDocumentCharacters ? text : null, "Public response exceeded its size limit.");
            }
        }

        private void Publish()
        {
            SlopCityCityMedia network = GetComponent<SlopCityCityMedia>();
            try { if (network) network.ApplyTikTokCatalog(catalog); }
            catch (Exception error) { Debug.LogException(error); }
            var subscribers = CatalogChanged;
            if (subscribers != null) foreach (Action<SlopCityTikTokCatalog> subscriber in subscribers.GetInvocationList())
                try { subscriber(catalog); } catch (Exception error) { Debug.LogException(error); }
        }

        private static SlopCityTikTokAccount AccountSnapshot(string creator, SlopCityTikTokAccount previous)
        {
            return new SlopCityTikTokAccount {
                creator = creator, profileUrl = "https://www.tiktok.com/@" + creator,
                secUid = PublicIds[Array.IndexOf(Creators, creator)],
                totalReported = previous != null && previous.totalReported >= 0 ? previous.totalReported : -1,
                discoveredCount = previous == null ? 0 : Math.Max(0, previous.discoveredCount),
                complete = previous != null && previous.complete,
                status = previous == null ? "Refresh pending." : previous.status ?? "",
                lastSyncUtc = previous != null && DateTimeOffset.TryParse(previous.lastSyncUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTimeOffset synced) ? synced.ToUniversalTime().ToString("O") : ""
            };
        }
        private const int MaxRetainedCatalogVideos = 2048;

        private void SaveCatalog()
        {
            if (string.IsNullOrEmpty(CatalogPath) || cacheUnavailable) return;
            try
            {
                if (catalog?.videos != null && catalog.videos.Length > MaxRetainedCatalogVideos)
                {
                    Array.Sort(catalog.videos, (left, right) =>
                    {
                        int byTime = right.createTime.CompareTo(left.createTime);
                        return byTime != 0 ? byTime : string.CompareOrdinal(left.id, right.id);
                    });
                    catalog.videos = catalog.videos.Take(MaxRetainedCatalogVideos).ToArray();
                }
                string temporary = CatalogPath + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(catalog, true));
                if (File.Exists(CatalogPath)) File.Replace(temporary, CatalogPath, null);
                else File.Move(temporary, CatalogPath);
            }
            catch (Exception error) { Debug.LogWarning("TikTok catalogue save: " + error.GetType().Name); }
        }

        public void ResolveVideo(SlopCityTikTokVideo post, Action<string> completed, Func<bool> stillNeeded = null)
        {
            if (!ValidPost(post) || post.kind == "slideshow") { Complete(completed, null); return; }
            Resolve(post, "video", post.id + ".mp4", -1, completed, stillNeeded);
        }
        public void ResolveImage(SlopCityTikTokVideo post, int index, Action<string> completed, Func<bool> stillNeeded = null)
        {
            if (!ValidPost(post) || post.kind != "slideshow" || index < 0 || index >= post.imageUrls.Length) { Complete(completed, null); return; }
            Resolve(post, "image", post.id + "_" + index + ".jpg", index, completed, stillNeeded);
        }
        public void ResolveAudio(SlopCityTikTokVideo post, Action<string> completed, Func<bool> stillNeeded = null)
        {
            if (!ValidPost(post) || (!SlopCityTikTokParser.IsMediaUrl(post.audioUrl) && string.IsNullOrEmpty(BundledUri(post, "audio"))))
            { Complete(completed, null); return; }
            Resolve(post, "audio", post.id + ".wav", -1, completed, stillNeeded);
        }
        private bool ValidPost(SlopCityTikTokVideo post) => post != null && Creators.Contains(post.creator)
            && SlopCityTikTokParser.ValidateVideo(post, post.creator);

        private void Resolve(SlopCityTikTokVideo post, string kind, string filename, int imageIndex, Action<string> completed, Func<bool> stillNeeded)
        {
            if (destroying) { Complete(completed, null); return; }
            string bundled = BundledUri(post, kind, imageIndex);
            if (!string.IsNullOrEmpty(bundled)) { Complete(completed, bundled); return; }
            // A sealed bundle is the only canonical runtime media source.
            // Missing content stays unavailable; no live lookup or download.
            Complete(completed, null);
        }

        private bool PrepareCacheDirectory(string directory)
        {
            bool available = TryEnsureCacheDirectory(directory, out Exception error);
            if (!available && !cacheUnavailable) Debug.LogWarning("TikTok media cache: " + error.GetType().Name);
            cacheUnavailable = !available;
            return available;
        }

        private static bool TryEnsureCacheDirectory(string directory, out Exception error)
        {
            error = null;
            try
            {
                if (string.IsNullOrEmpty(directory) || !Path.IsPathRooted(directory))
                    throw new NotSupportedException("Persistent media cache is unsupported on this platform.");
                Directory.CreateDirectory(directory); return true;
            }
            catch (Exception failure) { error = failure; return false; }
        }

        private void ResolveCached(SlopCityTikTokVideo post, string kind, string filename, int imageIndex,
            Action<string> completed, string directory, Func<bool> stillNeeded = null)
        {
            // Re-check on each use: removable volumes and directory permissions can recover
            // after startup. Failure never prevents the bundled catalogue from loading.
            if (!PrepareCacheDirectory(directory)) { Complete(completed, null); return; }
            string path = Path.Combine(directory, filename);
            // Only filenames generated from validated numeric post IDs reach this path.
            if (CachedMediaIsValid(path, kind))
            {
                try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                Complete(completed, new Uri(path).AbsoluteUri); return;
            }
            var consumer = new Consumer { callback = completed, needed = stillNeeded };
            if (!Needed(consumer)) { Complete(completed, null); return; }
            if (downloads.TryGetValue(filename, out DownloadWork waiting))
            {
                waiting.consumers.RemoveAll(item => !Needed(item));
                if (waiting.consumers.Count >= 32) { Complete(completed, null); return; }
                waiting.consumers.Add(consumer); return;
            }
            if (downloads.Count >= MaxQueuedDownloads) { Complete(completed, null); return; }
            string privatePath = path + "." + Guid.NewGuid().ToString("N");
            var work = new DownloadWork { post = post, kind = kind, path = path, filename = filename, imageIndex = imageIndex,
                temporary = privatePath + ".part", converted = privatePath + ".convert" };
            work.consumers.Add(consumer);
            downloads.Add(filename, work);
            downloadQueue.Enqueue(work);
            PumpDownloads();
        }

        private static bool TryCreateDownloadRequest(string url, string temporary, long limit,
            out UnityWebRequest request, out FileStream output)
        {
            request = null; output = null;
            try
            {
                output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
                request.downloadHandler = new SlopCityMediaTransfer.BoundedFileDownload(output, limit);
                request.timeout = 60;
                return true;
            }
            catch (Exception error)
            {
                request?.Dispose(); output?.Dispose(); request = null; output = null;
                Debug.LogWarning("Media cache request: " + error.GetType().Name); return false;
            }
        }

        private static bool Needed(Consumer consumer)
        {
            try { return consumer.needed == null || consumer.needed(); }
            catch (Exception) { return false; }
        }
        private bool HasConsumers(DownloadWork work)
        {
            work.consumers.RemoveAll(consumer => !Needed(consumer));
            return !destroying && work.consumers.Count > 0;
        }
        private void PumpDownloads()
        {
            while (!destroying && isActiveAndEnabled && activeDownloads < MaxConcurrentDownloads
                && HasDownloadCapacity() && downloadQueue.Count > 0)
            {
                DownloadWork work = downloadQueue.Dequeue();
                if (!HasConsumers(work)) { downloads.Remove(work.filename); continue; }
                // Reserve both the source and the largest converted output before starting.
                work.reservation = work.kind == "video" ? MaxMediaBytes : work.kind == "image"
                    ? 2L * SlopCityMediaTransfer.MaxImageBytes : 112L * 1024 * 1024;
                if (!ReserveCache(Path.GetDirectoryName(work.path), work))
                {
                    downloads.Remove(work.filename);
                    foreach (Consumer consumer in work.consumers.ToArray()) Complete(consumer.callback, null);
                    continue;
                }
                activeDownloads++;
                mediaCoroutines.Start(this, Download(work));
            }
        }
        private static UnityWebRequestAsyncOperation SendRequest(UnityWebRequest request, bool text)
        {
            if (request == null) return null;
            try
            {
                request.SetRequestHeader("User-Agent", UserAgent);
                request.SetRequestHeader(text ? "Accept" : "Referer", text ? "application/json,text/html;q=0.9" : "https://www.tiktok.com/");
                return SlopCityMediaTransfer.Send(request);
            }
            catch (Exception error) { Debug.LogWarning("TikTok request setup: " + error.GetType().Name); return null; }
        }
        private IEnumerator Download(DownloadWork work)
        {
            string result = null, path = work.path, filename = work.filename, kind = work.kind;
            SlopCityTikTokVideo post = work.post;
            int imageIndex = work.imageIndex;
            pinned.Add(path);
            string temporary = work.temporary, converted = work.converted;
            Task<bool> conversion = null;
            try
            {
                for (int attempt = 0; attempt < 2 && HasConsumers(work); attempt++)
                {
                    DeletePartial(temporary); DeletePartial(converted);
                    SlopCityTikTokVideo current = catalog.videos.FirstOrDefault(item => item.id == post.id && item.creator == post.creator) ?? post;
                    string url = kind == "image" ? (imageIndex < current.imageUrls.Length ? current.imageUrls[imageIndex] : "")
                        : kind == "audio" ? current.audioUrl : current.mediaUrl;
                    if (attempt > 0 || (kind == "image" ? !SlopCityTikTokParser.IsImageUrl(url) : !SlopCityTikTokParser.IsMediaUrl(url)))
                    {
                        string html = null;
                        yield return GetText(post.sourceUrl, (payload, _) => html = payload);
                        if (!HasConsumers(work)) yield break;
                        var page = html == null ? null : SlopCityTikTokParser.ParsePublicPage(html, post.creator);
                        SlopCityTikTokVideo refreshed = page?.videos.FirstOrDefault(item => item.id == post.id);
                        if (refreshed == null) break;
                        current.mediaUrl = refreshed.mediaUrl; current.imageUrls = refreshed.imageUrls; current.audioUrl = refreshed.audioUrl;
                        url = kind == "image" ? (imageIndex < current.imageUrls.Length ? current.imageUrls[imageIndex] : "")
                            : kind == "audio" ? current.audioUrl : current.mediaUrl;
                    }
                    bool allowed = kind == "image" ? SlopCityTikTokParser.IsImageUrl(url) : SlopCityTikTokParser.IsMediaUrl(url);
                    if (!allowed) break;
                    long sizeLimit = kind == "video" ? MaxMediaBytes : kind == "image" ? SlopCityMediaTransfer.MaxImageBytes : 48L * 1024 * 1024;
                    if (!TryCreateDownloadRequest(url, temporary, sizeLimit, out UnityWebRequest downloadRequest, out FileStream downloadOutput))
                    { cacheUnavailable = true; break; }
                    using (FileStream output = downloadOutput)
                    using (UnityWebRequest request = downloadRequest)
                    {
                        var operation = SendRequest(request, false);
                        if (operation == null) break;
                        while (!operation.isDone)
                        {
                            if (!HasConsumers(work) || request.downloadedBytes > (ulong)sizeLimit)
                            { request.Abort(); break; }
                            yield return null;
                        }
                        if (!HasConsumers(work)) yield break;
                        if (request.result != UnityWebRequest.Result.Success || request.downloadedBytes < 129 || request.downloadedBytes > (ulong)sizeLimit)
                        { DeletePartial(temporary); continue; }
                    }
                    bool accepted = false;
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                    if (kind == "image" || kind == "audio")
                    {
                        // Native codecs only touch these private files, never Unity objects.
                        // Keep the bounded worker slot until completion, including cancellation.
                        conversion = Task.Run(() =>
                        {
                            try { return kind == "image" ? ConvertImage(temporary, converted) : ConvertAudio(temporary, converted); }
                            catch (Exception) { return false; }
                        });
                        while (!conversion.IsCompleted) yield return null;
                        accepted = conversion.Status == TaskStatus.RanToCompletion && conversion.Result;
                        conversion = null;
                    }
                    else
#endif
                    {
                        try
                        {
                            if (kind == "image") accepted = ConvertImage(temporary, converted);
                            else if (kind == "audio")
                            {
                                accepted = ConvertAudio(temporary, converted);
                                if (!accepted) status = "Live photo music codec unavailable on this platform; bundled music remains playable.";
                            }
                            else if (LooksLikeMedia(temporary, kind)) { File.Move(temporary, converted); accepted = true; }
                        }
                        catch (Exception error) { Debug.LogWarning("TikTok media conversion: " + error.GetType().Name); }
                    }
                    if (!HasConsumers(work)) yield break;
                    if (!accepted || !CachedMediaIsValid(converted, kind)) break;
                    try
                    {
                        // Publish only a validated complete replacement. Failed conversions leave
                        // the prior cache entry untouched and never expose a half-written file.
                        if (File.Exists(path)) File.Replace(converted, path, null);
                        else File.Move(converted, path);
                        result = new Uri(path).AbsoluteUri;
                        if (kind == "video") current.localFile = filename;
                        SaveCatalog();
                    }
                    catch (Exception error) { Debug.LogWarning("TikTok media cache: " + error.GetType().Name); }
                    break;
                }
            }
            finally
            {
                if (conversion != null && !conversion.IsCompleted)
                {
                    // Unity may destroy this owner while ImageIO still owns the files.
                    conversion.ContinueWith(_ => ReleaseTemporaryFiles(work), TaskScheduler.Default);
                }
                else ReleaseTemporaryFiles(work);
                pinned.Remove(path);
                activeDownloads = Math.Max(0, activeDownloads - 1);
                downloads.Remove(filename);
                foreach (Consumer consumer in work.consumers.ToArray()) if (Needed(consumer)) Complete(consumer.callback, result);
            }
        }

        private static bool CachedMediaIsValid(string path, string kind)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) return false;
                var file = new FileInfo(path);
                long limit = kind == "image" ? SlopCityMediaTransfer.MaxImageBytes : kind == "audio" ? 64L * 1024 * 1024 : MaxMediaBytes;
                if (!file.Exists || file.Length <= 128 || file.Length > limit || (file.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                if (kind == "video") return LooksLikeMedia(path, "video");
                var header = new byte[12];
                using (var stream = File.OpenRead(path)) if (stream.Read(header, 0, header.Length) < header.Length) return false;
                if (kind == "image") return SlopCityMediaTransfer.ImageSizeIsSafe(File.ReadAllBytes(path), out _, out _);
                using (var stream = File.OpenRead(path))
                    return kind == "audio" && SlopCityMediaTransfer.WaveIsSafe(stream, out _, out _, out _, out _);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static void Complete(Action<string> callback, string value)
        { try { callback?.Invoke(value); } catch (Exception error) { Debug.LogException(error); } }

        private static bool LooksLikeMedia(string path, string kind)
        {
            var header = new byte[16];
            using (var stream = File.OpenRead(path)) if (stream.Read(header, 0, header.Length) < header.Length) return false;
            if (kind == "video") return header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p';
            return (header[0] == 'I' && header[1] == 'D' && header[2] == '3') || (header[0] == 0xff && (header[1] & 0xe0) == 0xe0);
        }
        private static void DeletePartial(string path)
        { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

        private static bool ConvertImage(string source, string destination)
        {
            // The native Mac codec handles WebP as well as JPEG/PNG and caps decoded
            // dimensions before loading a texture. It invokes only ImageIO, never a UI.
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return SlopCityConvertImage(source, destination) == 1;
#else
            return ConvertImageWithUnity(source, destination);
#endif
        }

        private static bool ConvertImageWithUnity(string source, string destination)
        {
            if (new FileInfo(source).Length > SlopCityMediaTransfer.MaxImageBytes) return false;
            byte[] bytes = File.ReadAllBytes(source);
            if (!SlopCityMediaTransfer.ImageSizeIsSafe(bytes, out _, out _)) return false;
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes)) return false;
                byte[] jpeg = texture.EncodeToJPG(90);
                if (jpeg == null || jpeg.Length > SlopCityMediaTransfer.MaxImageBytes) return false;
                File.WriteAllBytes(destination, jpeg);
                return true;
            }
            finally { if (texture) Destroy(texture); }
        }
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        [DllImport("SlopCityImageCodec")]
        private static extern int SlopCityConvertImage(string source, string destination);
        [DllImport("SlopCityImageCodec")]
        private static extern int SlopCityConvertAudio(string source, string destination);
#endif
        private static bool ConvertAudio(string source, string destination)
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return SlopCityConvertAudio(source, destination) == 1;
#else
            // Already-portable PCM WAV can be reused without a platform codec.
            using (var stream = File.OpenRead(source))
                if (!SlopCityMediaTransfer.WaveIsSafe(stream, out _, out _, out _, out _)) return false;
            File.Copy(source, destination, true);
            return true;
#endif
        }

        private static bool HasDownloadCapacity()
        {
            lock (CacheWorkLock) return activeCacheWork.Count < MaxConcurrentDownloads;
        }

        private static void ReleaseTemporaryFiles(DownloadWork work)
        {
            lock (CacheWorkLock)
            {
                DeletePartial(work.temporary);
                DeletePartial(work.converted);
                activeCacheWork.Remove(work.temporary);
            }
        }

        private bool ReserveCache(string directory, DownloadWork work)
        {
            try
            {
                // The continuation can delete its files concurrently. Keep accounting
                // and pruning consistent with that deletion and reservation release.
                lock (CacheWorkLock)
                {
                    var files = new DirectoryInfo(directory).GetFiles().Where(file => IsOwnedCacheEntry(file.Name)
                        && (file.Attributes & FileAttributes.ReparsePoint) == 0).OrderBy(file => file.LastAccessTimeUtc).ToArray();
                    long size = files.Where(file => !IsReservedPartial(file.FullName)).Sum(file => file.Length);
                    long reserved = activeCacheWork.Values.Where(active => Path.GetDirectoryName(active.path) == directory)
                        .Sum(active => active.reservation);
                    long target = MaxCacheBytes - reserved - work.reservation;
                    foreach (FileInfo file in files)
                    {
                        if (size <= target) break;
                        if (pinned.Contains(file.FullName) || IsReservedPartial(file.FullName) || file.LastAccessTimeUtc > DateTime.UtcNow.AddHours(-1)) continue;
                        long length = file.Length; file.Delete(); size -= length;
                    }
                    if (size <= target) { activeCacheWork.Add(work.temporary, work); return true; }
                }
                status = "Media cache budget reached; existing and bundled posts remain playable.";
            }
            catch (Exception error) { Debug.LogWarning("TikTok media budget: " + error.GetType().Name); cacheUnavailable = true; }
            return false;
        }

        // Call only while holding CacheWorkLock. Legacy partials have no live owner
        // and remain counted/prunable alongside abandoned unique partials.
        private static bool IsReservedPartial(string path)
        {
            if (path.EndsWith(".convert", StringComparison.Ordinal)) path = path.Substring(0, path.Length - 8) + ".part";
            return activeCacheWork.ContainsKey(path);
        }
        private static bool IsOwnedCacheEntry(string name)
        {
            bool partial = name.EndsWith(".part", StringComparison.Ordinal);
            bool converted = name.EndsWith(".convert", StringComparison.Ordinal);
            if (partial) name = name.Substring(0, name.Length - 5);
            else if (converted) name = name.Substring(0, name.Length - 8);
            if (IsOwnedCacheFile(name)) return true;
            if (!partial && !converted) return false;
            int separator = name.LastIndexOf('.');
            return separator > 0 && Guid.TryParseExact(name.Substring(separator + 1), "N", out _)
                && IsOwnedCacheFile(name.Substring(0, separator));
        }

        private static bool IsOwnedCacheFile(string filename)
        {
            if (SlopCityTikTokParser.IsSafeLocalFile(filename)) return true;
            if (filename.EndsWith(".wav", StringComparison.Ordinal))
                return SlopCityTikTokParser.IsSafeLocalFile(filename.Substring(0, filename.Length - 4) + ".mp4");
            if (!filename.EndsWith(".jpg", StringComparison.Ordinal)) return false;
            string stem = filename.Substring(0, filename.Length - 4);
            int separator = stem.LastIndexOf('_');
            if (separator < 1 || !SlopCityTikTokParser.IsSafeLocalFile(stem.Substring(0, separator) + ".mp4")) return false;
            string index = stem.Substring(separator + 1);
            return int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                && number >= 0 && number < 100 && index == number.ToString(CultureInfo.InvariantCulture);
        }
        private void OnDisable()
        {
            StopAllCoroutines();
            mediaCoroutines.DisposeAll();
        }

        private void OnDestroy()
        {
            destroying = true;
            if (Instance == this) Instance = null;
            // Clear before invoking arbitrary consumers; callbacks can request another resolve
            // or destroy a different media owner without mutating this enumeration.
            Action<string>[] callbacks = downloads.Values.SelectMany(work => work.consumers).Select(consumer => consumer.callback).ToArray();
            foreach (DownloadWork work in downloads.Values) work.consumers.Clear();
            downloads.Clear();
            downloadQueue.Clear();
            lock (CacheWorkLock)
            {
                foreach (string key in activeCacheWork.Keys.ToArray())
                    if (activeCacheWork.TryGetValue(key, out DownloadWork reserved))
                        ReleaseTemporaryFiles(reserved);
            }
            StopAllCoroutines();
            mediaCoroutines.DisposeAll();
            pinned.Clear();
            foreach (Action<string> callback in callbacks) Complete(callback, null);
        }
    }
}
