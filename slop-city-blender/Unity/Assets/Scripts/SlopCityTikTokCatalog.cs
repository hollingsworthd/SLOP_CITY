using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System.Text.RegularExpressions;

namespace SlopCity
{
    // Limits apply before Unity's native image decoder sees downloaded bytes.
    internal static class SlopCityMediaTransfer
    {
        internal const int MaxImageBytes = 4 * 1024 * 1024;
        internal const int MaxImageDimension = 2048;
        internal const long MaxImagePixels = 2L * 1024 * 1024;

        // Bound source decode peaks independently of retained display textures.
        internal const int MaxWaveBytes = 8 * 1024 * 1024;
        internal const int MaxWaveSamples = 2 * 1024 * 1024;

        internal static UnityWebRequest Get(string url, int maximumBytes, int timeout)
        {
            // A refused native Dispose remains owned, and blocks new allocation
            // until cleanup succeeds. This ledger holds one failed construction.
            if (!RetryConstructionCleanup()) return null;
            UnityWebRequest request = null;
            DownloadHandler download = null;
            try
            {
                request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
                request.disposeDownloadHandlerOnDispose = false;
                download = new BoundedDownload(maximumBytes);
                request.downloadHandler = download;
                request.timeout = timeout;
                request.disposeDownloadHandlerOnDispose = true;
                return request;
            }
            catch (Exception error)
            {
                // The handler is owned even if its native assignment failed
                // before attachment. Neither cleanup may skip the other.
                constructionRequest = request;
                constructionDownload = download;
                RetryConstructionCleanup();
                Debug.LogWarning("Media request unavailable: " + error.GetType().Name);
                return null;
            }
        }

        private static UnityWebRequest constructionRequest;
        private static DownloadHandler constructionDownload;
        private static bool cleaningConstruction, warnedConstruction;

        internal static void PumpConstructionLedger()
        {
            RetryConstructionCleanup();
        }

        private static bool RetryConstructionCleanup()
        {
            if (cleaningConstruction) return false;
            cleaningConstruction = true;
            try
            {
                if (constructionRequest != null)
                {
                    try { constructionRequest.Dispose(); constructionRequest = null; }
                    catch (Exception) { WarnConstructionCleanup(); }
                }
                if (constructionDownload != null)
                {
                    try { constructionDownload.Dispose(); constructionDownload = null; }
                    catch (Exception) { WarnConstructionCleanup(); }
                }
                bool cleared = constructionRequest == null && constructionDownload == null;
                if (cleared) warnedConstruction = false;
                return cleared;
            }
            finally { cleaningConstruction = false; }
        }

        private static void WarnConstructionCleanup()
        {
            if (warnedConstruction) return;
            warnedConstruction = true;
            Debug.LogWarning("Media request cleanup is pending; further requests wait for native disposal.");
        }

        internal static UnityWebRequestAsyncOperation Send(UnityWebRequest request)
        {
            if (request == null) return null;
            try { return request.SendWebRequest(); }
            catch (Exception error)
            {
                Debug.LogWarning("Media request could not start: " + error.GetType().Name);
                return null;
            }
        }

        private sealed class BoundedDownload : DownloadHandlerScript
        {
            private readonly int limit;
            private readonly MemoryStream bytes = new MemoryStream();
            private bool exceeded;
            private byte[] result;
            internal BoundedDownload(int limit) : base(new byte[32768]) { this.limit = limit; }
            protected override void ReceiveContentLengthHeader(ulong length) { exceeded = length > (ulong)limit; }
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (exceeded || data == null || length < 0 || length > limit - bytes.Length) return false;
                try { bytes.Write(data, 0, length); return true; }
                catch (Exception) { exceeded = true; return false; }
            }
            protected override byte[] GetData()
            {
                if (result == null)
                {
                    result = bytes.ToArray();
                    bytes.SetLength(0); bytes.Capacity = 0;
                }
                return result;
            }
            protected override string GetText() => Encoding.UTF8.GetString(GetData());
        }

        internal sealed class BoundedFileDownload : DownloadHandlerScript
        {
            private readonly FileStream output;
            private readonly long limit;
            private long received;
            private bool exceeded;
            internal BoundedFileDownload(FileStream output, long limit) : base(new byte[32768])
            { this.output = output; this.limit = limit; }
            protected override void ReceiveContentLengthHeader(ulong length) { exceeded = length > (ulong)limit; }
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (exceeded || data == null || length < 0 || length > limit - received) return false;
                try { output.Write(data, 0, length); received += length; return true; }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }
        }

        internal static bool ImageSizeIsSafe(byte[] data, out int width, out int height)
        {
            width = height = 0;
            if (data == null || data.Length < 24 || data.Length > MaxImageBytes) return false;
            if (data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71
                && data[12] == 'I' && data[13] == 'H' && data[14] == 'D' && data[15] == 'R')
            {
                width = BigEndian(data, 16); height = BigEndian(data, 20);
            }
            else if (data[0] == 0xff && data[1] == 0xd8)
            {
                int offset = 2;
                while (offset + 4 <= data.Length)
                {
                    if (data[offset++] != 0xff) return false;
                    while (offset < data.Length && data[offset] == 0xff) offset++;
                    if (offset >= data.Length) return false;
                    int marker = data[offset++];
                    if (marker == 0xd9 || marker == 0xda) return false;
                    if (marker == 0x01 || (marker >= 0xd0 && marker <= 0xd7)) continue;
                    if (offset + 2 > data.Length) return false;
                    int length = (data[offset] << 8) | data[offset + 1];
                    if (length < 2 || length > data.Length - offset) return false;
                    if (marker >= 0xc0 && marker <= 0xcf && marker != 0xc4 && marker != 0xc8 && marker != 0xcc)
                    {
                        if (length < 8) return false;
                        height = (data[offset + 3] << 8) | data[offset + 4];
                        width = (data[offset + 5] << 8) | data[offset + 6];
                        break;
                    }
                    offset += length;
                }
            }
            return width > 0 && height > 0 && width <= MaxImageDimension && height <= MaxImageDimension
                && (long)width * height <= MaxImagePixels;
        }
        internal static bool WaveIsSafe(Stream stream, out int channels, out int frequency, out int frames, out int dataOffset)
        {
            channels = frequency = frames = dataOffset = 0;
            if (!stream.CanSeek || stream.Length < 44 || stream.Length > MaxWaveBytes) return false;
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (reader.ReadUInt32() != 0x46464952 || reader.ReadUInt32() > stream.Length - 8
                    || reader.ReadUInt32() != 0x45564157) return false;
                bool format = false;
                while (stream.Position + 8 <= stream.Length)
                {
                    uint id = reader.ReadUInt32(), length = reader.ReadUInt32();
                    long start = stream.Position;
                    if (length > stream.Length - start) return false;
                    if (id == 0x20746d66)
                    {
                        if (length < 16 || reader.ReadUInt16() != 1) return false;
                        channels = reader.ReadUInt16(); frequency = reader.ReadInt32();
                        int byteRate = reader.ReadInt32(), alignment = reader.ReadUInt16(), bits = reader.ReadUInt16();
                        if (channels < 1 || channels > 2 || frequency < 8000 || frequency > 48000 || bits != 16
                            || alignment != channels * 2 || byteRate != frequency * alignment) return false;
                        format = true;
                    }
                    else if (id == 0x61746164)
                    {
                        if (!format || length < channels * 2 || length % (channels * 2) != 0) return false;
                        frames = (int)(length / (channels * 2)); dataOffset = (int)start;
                        return frames <= frequency * 180 && (long)frames * channels <= MaxWaveSamples;
                    }
                    stream.Position = start + length + (length & 1);
                }
            }
            return false;
        }

        private static int BigEndian(byte[] bytes, int at)
            => (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
    }

    [Serializable]
    public sealed class SlopCityTikTokCatalog
    {
        public int version = 2;
        public string syncedAtUtc = "";
        public string bundledAtUtc = "";
        // Optional content selection. Older version-2 catalogues retain the active/default pin.
        public string featuredCreator = "", featuredPostId = "";
        public SlopCityTikTokAccount[] accounts = Array.Empty<SlopCityTikTokAccount>();
        public SlopCityTikTokVideo[] videos = Array.Empty<SlopCityTikTokVideo>();

        public bool TryGetFeaturedVideo(out SlopCityTikTokVideo selected)
        {
            selected=null;
            if(version!=2||videos==null||string.IsNullOrEmpty(featuredPostId)
                ||!SlopCityTikTokParser.IsAllowedCreator(featuredCreator)
                ||featuredCreator!=SlopCityTikTokParser.NormalizeCreator(featuredCreator))return false;
            // Respect the installed full creator/post identity on every refresh.
            // A missing or rejected selection leaves the physical screen's usable pin intact.
            foreach(var video in videos)
            {
                if(video==null||video.creator!=featuredCreator||video.id!=featuredPostId)continue;
                if(selected!=null||video.kind!="video"||!SlopCityTikTokParser.ValidateVideo(video,featuredCreator))
                { selected=null;return false; }
                selected=video;
            }
            return selected!=null;
        }
    }

    [Serializable]
    public sealed class SlopCityTikTokAccount
    {
        public string creator = "", profileUrl = "", status = "", lastSyncUtc = "", secUid = "";
        public int totalReported = -1, discoveredCount;
        public bool complete;
    }

    [Serializable]
    public sealed class SlopCityTikTokVideo
    {
        public string id = "", title = "", creator = "", sourceUrl = "", mediaUrl = "", localFile = "";
        public string kind = "video", audioUrl = "";
        public string[] imageUrls = Array.Empty<string>();
        public string bundledVideoFile = "", bundledAudioFile = "", bundledThumbnailFile = "";
        public string[] bundledImageFiles = Array.Empty<string>();
        public long createTime;
        public int duration;
    }

    /// <summary>
    /// Bounded extraction from public TikTok hydration and item-list responses. This never
    /// executes page scripts, reads cookies, or treats a missing page/list as an empty account.
    /// Numeric JSON tokens retain their original text so 19-digit post IDs are never rounded.
    /// </summary>
    public static class SlopCityTikTokParser
    {
        public const int MaxDocumentCharacters = 8 * 1024 * 1024;
        private const int MaxDepth = 64, MaxValues = 250000, MaxStringCharacters = 1024 * 1024;
        private static readonly Regex NumericId = new Regex("\\A[0-9]{5,24}\\z", RegexOptions.CultureInvariant);
        private static readonly Regex Scripts = new Regex("<script\\b(?<attributes>[^>]*)>(?<payload>.*?)</script\\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly Regex ScriptId = new Regex("\\bid\\s*=\\s*[\"'](?<id>[^\"']+)[\"']",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        public sealed class Page
        {
            public string creator = "", secUid = "", status = "unavailable";
            public int totalReported = -1;
            public bool privateAccount;
            public SlopCityTikTokVideo[] videos = Array.Empty<SlopCityTikTokVideo>();
        }

        public sealed class ListPage
        {
            public SlopCityTikTokVideo[] videos = Array.Empty<SlopCityTikTokVideo>();
            public bool hasMore, valid;
            public long cursor;
            public int statusCode = -1, totalReported = -1, rejectedItems, photoPosts;
        }

        public static Page ParsePublicPage(string html, string expectedCreator)
        {
            var result = new Page { creator = NormalizeCreator(expectedCreator) };
            if (!IsAllowedCreator(expectedCreator) || !AcceptSize(html)) { result.status = "invalid"; return result; }
            try
            {
                var documents = new List<Dictionary<string, object>>();
                if (html.TrimStart().StartsWith("{", StringComparison.Ordinal)) documents.Add(ParseObject(html));
                else foreach (Match script in Scripts.Matches(html))
                {
                    Match id = ScriptId.Match(script.Groups["attributes"].Value);
                    string name = id.Success ? id.Groups["id"].Value : "";
                    if (name != "__UNIVERSAL_DATA_FOR_REHYDRATION__" && name != "__NEXT_DATA__" && name != "SIGI_STATE") continue;
                    documents.Add(ParseObject(script.Groups["payload"].Value));
                }
                var videos = new List<SlopCityTikTokVideo>();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (Dictionary<string, object> document in documents)
                {
                    if (document == null) continue;
                    Dictionary<string, object> scope = Map(Get(document, "__DEFAULT_SCOPE__"));
                    Dictionary<string, object> profile = Map(Get(scope, "webapp.user-detail"));
                    if (profile != null)
                    {
                        Dictionary<string, object> info = Map(Get(profile, "userInfo"));
                        Dictionary<string, object> user = Map(Get(info, "user"));
                        if (user != null)
                        {
                            if (!SameCreator(Text(Get(user, "uniqueId")), expectedCreator)) { result.status = "creator_mismatch"; continue; }
                            result.secUid = Text(Get(user, "secUid"));
                            result.privateAccount = Flag(Get(user, "privateAccount")) || Flag(Get(user, "secret"));
                            result.totalReported = Count(Get(Map(Get(info, "stats")), "videoCount"), -1);
                            result.status = result.privateAccount ? "private" : "ok";
                        }
                        else if (Number(Get(profile, "statusCode"), 0) != 0) result.status = "unavailable";
                    }
                    Dictionary<string, object> detail = Map(Get(scope, "webapp.video-detail"));
                    if (detail != null)
                    {
                        if (Number(Get(detail, "statusCode"), -1) == 0)
                            AddVideo(videos, ids, ReadVideo(Map(Get(Map(Get(detail, "itemInfo")), "itemStruct")), expectedCreator));
                        else result.status = "unavailable";
                    }
                    // Public embed hydration uses a distinct, explicit itemInfos/authorInfos pair.
                    Dictionary<string, object> props = Map(Get(Map(Get(document, "props")), "pageProps"));
                    if (props != null)
                    {
                        AddVideo(videos, ids, ReadVideo(Map(Get(Map(Get(props, "itemInfo")), "itemStruct")), expectedCreator));
                        ReadEmbed(Map(Get(props, "videoData")), expectedCreator, videos, ids);
                        ReadEmbed(Map(Get(Map(Get(props, "data")), "videoData")), expectedCreator, videos, ids);
                    }
                    // Legacy SIGI only exposes top-level authored items; never recurse into duets,
                    // recommendations or textExtra mentions and mistake them for account ownership.
                    Dictionary<string, object> items = Map(Get(document, "ItemModule"));
                    if (items != null) foreach (object item in items.Values)
                        AddVideo(videos, ids, ReadVideo(Map(item), expectedCreator));
                }
                if (result.privateAccount) videos.Clear();
                result.videos = videos.ToArray();
                if (videos.Count > 0) result.status = "ok";
                return result;
            }
            catch (Exception error) when (error is FormatException || error is OverflowException || error is RegexMatchTimeoutException || error is ArgumentException)
            { result.status = "invalid"; result.videos = Array.Empty<SlopCityTikTokVideo>(); return result; }
        }

        public static ListPage ParsePublicList(string json, string expectedCreator)
        {
            var result = new ListPage();
            if (!IsAllowedCreator(expectedCreator) || !AcceptSize(json)) return result;
            try
            {
                Dictionary<string, object> root = ParseObject(json);
                result.statusCode = Count(Get(root, "statusCode") ?? Get(root, "status_code"), -1);
                // A status, real array and explicit paging flag are all required. Captcha/HTML,
                // null itemList and changed response schemas cannot silently become "complete".
                var items = Get(root, "itemList") as List<object>;
                object more = Get(root, "hasMore") ?? Get(root, "hasMorePrevious");
                if (result.statusCode != 0 || items == null || !TryFlag(more, out result.hasMore)) return result;
                object rawCursor = Get(root, "cursor");
                result.cursor = Number(rawCursor, -1);
                var videos = new List<SlopCityTikTokVideo>();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                long lastCreated = -1;
                foreach (object value in items)
                {
                    Dictionary<string, object> item = Map(value);
                    if (item == null) { result.rejectedItems++; continue; }
                    long created = Number(Get(item, "createTime"), -1);
                    if (created >= 0 && created <= 253402300799L) lastCreated = created;
                    string creator = Author(item);
                    if (SameCreator(creator, expectedCreator))
                    {
                        int reported = Count(Get(Map(Get(item, "authorStats")), "videoCount")
                            ?? Get(Map(Get(item, "authorStatsV2")), "videoCount"), -1);
                        if (reported >= 0) result.totalReported = Math.Max(result.totalReported, reported);
                    }
                    if (!SameCreator(creator, expectedCreator)) { result.rejectedItems++; continue; }
                    if (IsPhoto(item)) result.photoPosts++;
                    SlopCityTikTokVideo video = ReadVideo(item, expectedCreator);
                    if (video == null) { result.rejectedItems++; continue; }
                    AddVideo(videos, ids, video);
                }
                // creator/item_list pages by timestamp and reports hasMorePrevious, without a
                // cursor property. Use the last RAW post (including photos), not a filtered video.
                if (rawCursor == null) result.cursor = lastCreated >= 0 ? checked(lastCreated * 1000) : 0;
                if (result.cursor < 0 || (result.hasMore && result.cursor == 0)) return result;
                result.videos = videos.ToArray(); result.valid = true;
                return result;
            }
            catch (Exception error) when (error is FormatException || error is OverflowException || error is ArgumentException)
            { return result; }
        }

        public static bool ValidateVideo(SlopCityTikTokVideo video, string expectedCreator)
        {
            if (!(video != null && IsAllowedCreator(expectedCreator) && SameCreator(video.creator, expectedCreator)
                && NumericId.IsMatch(video.id ?? "") && IsSourceUrl(video.sourceUrl, expectedCreator, video.id)
                && (string.IsNullOrEmpty(video.mediaUrl) || IsMediaUrl(video.mediaUrl))
                && (string.IsNullOrEmpty(video.localFile) || IsSafeLocalFile(video.localFile, video.id))
                && (string.IsNullOrEmpty(video.audioUrl) || IsMediaUrl(video.audioUrl))
                && (string.IsNullOrEmpty(video.bundledVideoFile) || video.bundledVideoFile == video.id + ".mp4")
                && (string.IsNullOrEmpty(video.bundledAudioFile) || video.bundledAudioFile == video.id + ".wav")
                && (string.IsNullOrEmpty(video.bundledThumbnailFile) || video.bundledThumbnailFile == video.id + "_thumb.jpg")
                && video.createTime >= 0 && video.createTime <= 253402300799L && video.duration >= 0 && video.duration <= 86400)) return false;
            if (video.bundledImageFiles != null && video.bundledImageFiles.Length > 0)
            {
                if (video.kind != "slideshow" || video.imageUrls == null || video.bundledImageFiles.Length != video.imageUrls.Length) return false;
                for (int index = 0; index < video.bundledImageFiles.Length; index++)
                    if (video.bundledImageFiles[index] != video.id + "_" + index.ToString(CultureInfo.InvariantCulture) + ".jpg") return false;
            }
            if (video.kind == "slideshow")
            {
                if (video.imageUrls == null || video.imageUrls.Length == 0 || video.imageUrls.Length > 100) return false;
                foreach (string url in video.imageUrls) if (!IsImageUrl(url)) return false;
                return true;
            }
            return string.IsNullOrEmpty(video.kind) || video.kind == "video";
        }

        public static bool IsSafeLocalFile(string localFile, string expectedId = null)
        {
            if (string.IsNullOrEmpty(localFile) || !localFile.EndsWith(".mp4", StringComparison.Ordinal)) return false;
            string id = localFile.Substring(0, localFile.Length - 4);
            return NumericId.IsMatch(id) && (expectedId == null || id == expectedId);
        }

        public static bool IsSourceUrl(string sourceUrl, string creator, string id)
        {
            if (!IsAllowedCreator(creator) || !NumericId.IsMatch(id ?? "") || !SafeHttps(sourceUrl, out Uri uri)) return false;
            if (!uri.Host.Equals("www.tiktok.com", StringComparison.OrdinalIgnoreCase) && !uri.Host.Equals("tiktok.com", StringComparison.OrdinalIgnoreCase)) return false;
            string path = uri.AbsolutePath.TrimEnd('/'), prefix = "/@" + NormalizeCreator(creator);
            return path.Equals(prefix + "/video/" + id, StringComparison.OrdinalIgnoreCase)
                || path.Equals(prefix + "/photo/" + id, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsMediaUrl(string url)
        {
            if (!SafeHttps(url, out Uri uri)) return false;
            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath;
            if (path.IndexOf("/@", StringComparison.Ordinal) >= 0 || path.StartsWith("/embed", StringComparison.OrdinalIgnoreCase)) return false;
            if (host.EndsWith(".tiktok.com", StringComparison.Ordinal))
            {
                // TikTok's own playable CDN includes v16-webapp-prime.us.tiktok.com/video/...
                return host.Length > 2 && host[0] == 'v' && char.IsDigit(host[1]) && path.StartsWith("/video/", StringComparison.Ordinal)
                    && path.Length > 12;
            }
            foreach (string suffix in new[] { ".tiktokcdn.com", ".tiktokcdn-us.com", ".tiktokcdn-eu.com", ".byteoversea.com", ".ibytedtos.com", ".muscdn.com", ".tiktokv.com" })
                if (host.EndsWith(suffix, StringComparison.Ordinal) && path.Length > 1) return true;
            return false;
        }

        public static bool IsImageUrl(string url)
        {
            if (!SafeHttps(url, out Uri uri) || uri.AbsolutePath.Length < 2) return false;
            string host = uri.Host.ToLowerInvariant();
            foreach (string suffix in new[] { ".tiktokcdn.com", ".tiktokcdn-us.com", ".tiktokcdn-eu.com", ".byteoversea.com", ".ibytedtos.com" })
                if (host.EndsWith(suffix, StringComparison.Ordinal)) return true;
            return false;
        }

        public static bool IsAllowedCreator(string creator)
        {
            string value = NormalizeCreator(creator);
            return value == "1984drh" || value == "machine_publishers";
        }

        public static string NormalizeCreator(string creator)
        {
            string value = (creator ?? "").Trim();
            return (value.StartsWith("@", StringComparison.Ordinal) ? value.Substring(1) : value).ToLowerInvariant();
        }

        private static bool SafeHttps(string url, out Uri uri)
        {
            uri = null;
            foreach (char c in url ?? "") if (char.IsControl(c)) return false;
            return !string.IsNullOrWhiteSpace(url) && url.Length < 32768 && Uri.TryCreate(url, UriKind.Absolute, out uri)
                && uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);
        }

        private static bool SameCreator(string actual, string expected) => IsAllowedCreator(expected)
            && string.Equals(actual, NormalizeCreator(expected), StringComparison.OrdinalIgnoreCase);
        private static bool AcceptSize(string text) => !string.IsNullOrWhiteSpace(text) && text.Length <= MaxDocumentCharacters;
        private static Dictionary<string, object> Map(object value) => value as Dictionary<string, object>;
        private static object Get(Dictionary<string, object> map, string key) => map != null && map.TryGetValue(key, out object value) ? value : null;
        private static string Text(object value) => value is string text ? text : value is JsonNumber number ? number.raw : "";
        private static long Number(object value, long fallback) => long.TryParse(Text(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ? number : fallback;
        private static int Count(object value, int fallback) { long number = Number(value, fallback); return number >= 0 && number <= int.MaxValue ? (int)number : fallback; }
        private static bool Flag(object value) => TryFlag(value, out bool result) && result;
        private static bool TryFlag(object value, out bool result)
        {
            if (value is bool flag) { result = flag; return true; }
            string text = Text(value);
            if (text == "1" || text == "true") { result = true; return true; }
            if (text == "0" || text == "false") { result = false; return true; }
            result = false; return false;
        }
        private static string Author(Dictionary<string, object> item)
        {
            object author = Get(item, "author");
            return author is string creator ? creator : Text(Get(Map(author), "uniqueId"));
        }
        private static bool IsPhoto(Dictionary<string, object> item) => Get(item, "imagePost") != null || Get(item, "itemImagePost") != null;

        private static SlopCityTikTokVideo ReadVideo(Dictionary<string, object> item, string expectedCreator, Dictionary<string, object> embedAuthor = null)
        {
            if (item == null || Flag(Get(item, "privateItem")) || Flag(Get(item, "secret")) || Flag(Get(item, "forFriend"))
                || Flag(Get(item, "isProhibited")) || Flag(Get(item, "isReviewing")) || Number(Get(item, "takeDown"), 0) != 0) return null;
            Dictionary<string, object> author = Map(Get(item, "author")) ?? embedAuthor;
            string creator = embedAuthor == null ? Author(item) : Text(Get(embedAuthor, "uniqueId"));
            if (!SameCreator(creator, expectedCreator) || Flag(Get(author, "privateAccount"))) return null;
            Dictionary<string, object> video = Map(Get(item, "video"));
            bool photo = IsPhoto(item);
            if (video == null && !photo) return null;
            string id = Text(Get(item, "id"));
            if (!NumericId.IsMatch(id)) return null;
            string media = photo ? "" : MediaAddress(video);
            string title = Text(Get(item, "desc") ?? Get(item, "text"));
            string audio = Text(Get(Map(Get(item, "music")), "playUrl"));
            var result = new SlopCityTikTokVideo {
                id = id, creator = NormalizeCreator(creator), title = CleanTitle(title, creator),
                sourceUrl = "https://www.tiktok.com/@" + NormalizeCreator(creator) + (photo ? "/photo/" : "/video/") + id,
                mediaUrl = media, localFile = "", createTime = Number(Get(item, "createTime"), 0),
                duration = Count(Get(video, "duration"), 0), kind = photo ? "slideshow" : "video",
                imageUrls = photo ? ImageAddresses(Map(Get(item, "imagePost") ?? Get(item, "itemImagePost"))) : Array.Empty<string>(),
                audioUrl = photo && IsMediaUrl(audio) ? audio : ""
            };
            return ValidateVideo(result, expectedCreator) ? result : null;
        }

        private static string MediaAddress(Dictionary<string, object> video)
        {
            foreach (string key in new[] { "playAddr", "downloadAddr" })
            {
                string direct = Text(Get(video, key)); if (IsMediaUrl(direct)) return direct;
                string nested = AddressList(Map(Get(video, key))); if (nested.Length > 0) return nested;
            }
            string structure = AddressList(Map(Get(video, "PlayAddrStruct")));
            if (structure.Length > 0) return structure;
            var urls = Get(video, "urls") as List<object>;
            if (urls != null) foreach (object value in urls) { string url = Text(value); if (IsMediaUrl(url)) return url; }
            var bitrates = Get(video, "bitrateInfo") as List<object>;
            if (bitrates != null) foreach (object value in bitrates)
            { string url = AddressList(Map(Get(Map(value), "PlayAddr"))); if (url.Length > 0) return url; }
            return "";
        }
        private static string AddressList(Dictionary<string, object> address)
        {
            var urls = (Get(address, "UrlList") ?? Get(address, "urlList")) as List<object>;
            if (urls != null) foreach (object value in urls) { string url = Text(value); if (IsMediaUrl(url)) return url; }
            return "";
        }
        private static string[] ImageAddresses(Dictionary<string, object> photo)
        {
            var images = Get(photo, "images") as List<object>;
            if (images == null || images.Count == 0 || images.Count > 100) return Array.Empty<string>();
            var result = new List<string>();
            foreach (object image in images)
            {
                var urls = Get(Map(Get(Map(image), "imageURL")), "urlList") as List<object>;
                string selected = "";
                if (urls != null) foreach (object value in urls)
                { string url = Text(value); if (IsImageUrl(url)) { selected = url; break; } }
                // Never silently discard a frame and claim the entire slideshow was discovered.
                if (selected.Length == 0) return Array.Empty<string>();
                result.Add(selected);
            }
            return result.ToArray();
        }
        private static void ReadEmbed(Dictionary<string, object> data, string creator, List<SlopCityTikTokVideo> videos, HashSet<string> ids)
        {
            if (data == null) return;
            Dictionary<string, object> item = Map(Get(data, "itemInfos"));
            AddVideo(videos, ids, ReadVideo(item, creator, Map(Get(data, "authorInfos"))));
            AddVideo(videos, ids, ReadVideo(Map(Get(data, "itemStruct")), creator));
        }
        private static void AddVideo(List<SlopCityTikTokVideo> videos, HashSet<string> ids, SlopCityTikTokVideo video)
        { if (video != null && ids.Add(video.id)) videos.Add(video); }
        private static string CleanTitle(string title, string creator)
        {
            var result = new StringBuilder();
            foreach (char c in title ?? "")
            {
                if (result.Length >= 180) break;
                if (char.IsControl(c)) { if (c == '\n' || c == '\r' || c == '\t') result.Append(' '); continue; }
                result.Append(c == '<' ? '[' : c == '>' ? ']' : c);
            }
            string text = result.ToString().Trim(); return text.Length > 0 ? text : "@" + NormalizeCreator(creator) + " / public video";
        }

        private static Dictionary<string, object> ParseObject(string json)
        {
            if (!AcceptSize(json)) throw new FormatException("Public metadata exceeds its size limit or is empty.");
            var reader = new JsonReader(json); object value = reader.Read(0); reader.End();
            return value as Dictionary<string, object> ?? throw new FormatException("Expected a JSON object.");
        }
        private sealed class JsonNumber { public readonly string raw; public JsonNumber(string value) { raw = value; } }
        private sealed class JsonReader
        {
            private readonly string input;
            private int cursor, values;
            public JsonReader(string text) { input = text; }
            public void End() { White(); if (cursor != input.Length) throw new FormatException("Trailing JSON content."); }
            private void White() { while (cursor < input.Length && (input[cursor] == ' ' || input[cursor] == '\r' || input[cursor] == '\n' || input[cursor] == '\t')) cursor++; }
            private char Take() { if (cursor >= input.Length) throw new FormatException("Truncated JSON."); return input[cursor++]; }
            private void Require(char c) { White(); if (Take() != c) throw new FormatException("Unexpected JSON separator."); }
            public object Read(int depth)
            {
                if (depth > MaxDepth || ++values > MaxValues) throw new FormatException("Public JSON complexity limit exceeded.");
                White(); if (cursor >= input.Length) throw new FormatException("Truncated JSON.");
                char c = input[cursor];
                if (c == '"') return String();
                if (c == '{')
                {
                    cursor++; var map = new Dictionary<string, object>(StringComparer.Ordinal); White();
                    if (cursor < input.Length && input[cursor] == '}') { cursor++; return map; }
                    while (true)
                    {
                        White(); if (cursor >= input.Length || input[cursor] != '"') throw new FormatException("Object key must be a string.");
                        string key = String(); Require(':');
                        if (map.ContainsKey(key)) throw new FormatException("Ambiguous duplicate JSON key.");
                        map.Add(key, Read(depth + 1)); White(); char end = Take();
                        if (end == '}') return map; if (end != ',') throw new FormatException("Invalid JSON object separator.");
                    }
                }
                if (c == '[')
                {
                    cursor++; var list = new List<object>(); White();
                    if (cursor < input.Length && input[cursor] == ']') { cursor++; return list; }
                    while (true)
                    { list.Add(Read(depth + 1)); White(); char end = Take(); if (end == ']') return list; if (end != ',') throw new FormatException("Invalid JSON array separator."); }
                }
                foreach (string keyword in new[] { "true", "false", "null" })
                    if (c == keyword[0])
                    {
                        if (cursor + keyword.Length > input.Length || string.CompareOrdinal(input, cursor, keyword, 0, keyword.Length) != 0) throw new FormatException("Invalid JSON literal.");
                        cursor += keyword.Length; return keyword == "null" ? null : (object)(keyword == "true");
                    }
                int start = cursor;
                if (c == '-') cursor++;
                if (cursor >= input.Length || input[cursor] < '0' || input[cursor] > '9') throw new FormatException("Invalid JSON number.");
                if (input[cursor] == '0') cursor++;
                else while (cursor < input.Length && input[cursor] >= '0' && input[cursor] <= '9') cursor++;
                if (cursor < input.Length && input[cursor] == '.') { cursor++; Digits(); }
                if (cursor < input.Length && (input[cursor] == 'e' || input[cursor] == 'E'))
                { cursor++; if (cursor < input.Length && (input[cursor] == '+' || input[cursor] == '-')) cursor++; Digits(); }
                return new JsonNumber(input.Substring(start, cursor - start));
            }
            private void Digits()
            {
                int start = cursor; while (cursor < input.Length && input[cursor] >= '0' && input[cursor] <= '9') cursor++;
                if (cursor == start) throw new FormatException("JSON number has no digits.");
            }
            private string String()
            {
                Require('"'); var text = new StringBuilder();
                while (true)
                {
                    char c = Take(); if (c == '"') return text.ToString();
                    if (c < 0x20) throw new FormatException("Unescaped JSON control character.");
                    if (c == '\\')
                    {
                        char escape = Take();
                        switch (escape)
                        {
                            case '"': case '\\': case '/': c = escape; break;
                            case 'b': c = '\b'; break; case 'f': c = '\f'; break; case 'n': c = '\n'; break;
                            case 'r': c = '\r'; break; case 't': c = '\t'; break;
                            case 'u':
                                if (cursor + 4 > input.Length || !ushort.TryParse(input.Substring(cursor, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code)) throw new FormatException("Invalid Unicode JSON escape.");
                                c = (char)code; cursor += 4; break;
                            default: throw new FormatException("Unknown JSON escape.");
                        }
                    }
                    text.Append(c); if (text.Length > MaxStringCharacters) throw new FormatException("Public JSON string limit exceeded.");
                }
            }
        }
    }
}
