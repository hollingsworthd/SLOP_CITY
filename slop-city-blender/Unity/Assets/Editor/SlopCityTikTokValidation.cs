#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SlopCity.Editor
{
    /// <summary>Offline fixtures for the production parser; no network, cookies or user cache.</summary>
    public static class SlopCityTikTokValidation
    {
        public const string ReportPath = "Builds/Reports/tiktok-parser-validation.json";
        private const string Creator = "machine_publishers", IdA = "7694500222541597983", IdB = "7573075941463756062";
        private const string VideoUrl = "https://v16-webapp-prime.us.tiktok.com/video/tos/useast8/public-fixture/?mime_type=video_mp4";
        private const string ImageUrl = "https://p19-common-sign.tiktokcdn-us.com/tos-useast8/public-fixture.jpeg";
        private const string AudioUrl = "https://sf16.tiktokcdn-us.com/obj/tos-useast8/public-fixture.m4a";

        // Unity -batchmode -quit -executeMethod SlopCity.Editor.SlopCityTikTokValidation.Run
        public static void Run()
        {
            Report report = Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            report.ThrowIfInvalid();
            Debug.Log("SLOP_CITY_TIKTOK_PARSER_VALIDATED " + report.checks + " deterministic checks.");
        }

        public static Report Validate()
        {
            var report = new Report { checkedAtUtc = DateTime.UtcNow.ToString("O") };
            Paging(report); Identity(report); Hydration(report); Slideshows(report); AddressSafety(report); MalformedData(report);
            report.passed = report.errors.Count == 0;
            return report;
        }

        private static void Paging(Report r)
        {
            var first = Parse(List(Item(IdA, Creator, 1791515454) + "," + Item(IdB, Creator, 1763244162), "true"));
            r.Check(first.valid && first.hasMore && first.videos.Length == 2, "Partial creator list remains explicitly incomplete.");
            r.Check(first.cursor == 1763244162000L, "Creator cursor comes from the last raw createTime in milliseconds.");
            r.Check(first.totalReported == 10 && first.rejectedItems == 0, "Author stats preserve TikTok's reported public post count.");
            var last = Parse(List(Item(IdB, Creator, 1763244162), "false"));
            r.Check(last.valid && !last.hasMore && last.videos.Length == 1, "A final valid nonempty page can complete paging.");
            var overlap = new HashSet<string>(first.videos.Select(video => video.id));
            foreach (var video in last.videos) overlap.Add(video.id);
            r.Check(overlap.Count == 2, "Overlapping timestamp pages can be deduplicated by exact post ID.");
            var duplicates = Parse(List(Item(IdA) + "," + Item(IdA)));
            r.Check(duplicates.valid && duplicates.videos.Length == 1, "Duplicate IDs inside one page only appear once.");
            var explicitCursor = Parse("{\"statusCode\":0,\"itemList\":[" + Item() + "],\"hasMore\":1,\"cursor\":1234567890123}");
            r.Check(explicitCursor.valid && explicitCursor.hasMore && explicitCursor.cursor == 1234567890123, "post/item_list explicit cursor and numeric paging flag are supported.");
            var empty = Parse(List(""));
            r.Check(empty.valid && !empty.hasMore && empty.videos.Length == 0, "Only an explicit successful terminal empty array represents an empty page.");
            var stalled = Parse(List(Item(), "true"));
            var repeated = Parse(List(Item(), "true"));
            r.Check(stalled.valid && repeated.valid && repeated.hasMore && repeated.cursor == stalled.cursor,
                "A stalled cursor remains incomplete so the provider can stop without falsely declaring completion.");
            foreach (string json in new[] {
                "{}", "{\"statusCode\":0}", "{\"statusCode\":0,\"hasMore\":false}",
                "{\"statusCode\":0,\"itemList\":null,\"hasMore\":false}",
                "{\"statusCode\":0,\"itemList\":{},\"hasMore\":false}",
                "{\"itemList\":[],\"hasMore\":false}", "{\"statusCode\":0,\"itemList\":[]}",
                "{\"statusCode\":0,\"itemList\":[],\"hasMore\":\"unknown\"}",
                "{\"statusCode\":10221,\"itemList\":[],\"hasMore\":false}",
                "{\"statusCode\":0,\"itemList\":[],\"hasMore\":true}",
                "{\"statusCode\":0,\"itemList\":[],\"hasMore\":false,\"cursor\":-1}"
            }) r.Check(!Parse(json).valid, "Missing, blocked or unpageable lists do not masquerade as a complete empty account: " + json);
            var statsV2 = Parse(List(Item().Replace("\"authorStats\":{\"videoCount\":10}", "\"authorStatsV2\":{\"videoCount\":\"10\"}")));
            r.Check(statsV2.valid && statsV2.totalReported == 10, "String-valued authorStatsV2 counts are accepted without precision loss.");
        }

        private static void Identity(Report r)
        {
            var idString = Parse(List(Item()));
            var idNumber = Parse(List(Item().Replace("\"id\":\"" + IdA + "\"", "\"id\":" + IdA)));
            r.Check(idString.videos.Length == 1 && idString.videos[0].id == IdA, "String post IDs are preserved exactly.");
            r.Check(idNumber.videos.Length == 1 && idNumber.videos[0].id == IdA, "19-digit numeric IDs are preserved exactly, without double rounding.");
            var upper = Parse(List(Item(IdA, "MACHINE_PUBLISHERS")));
            r.Check(upper.videos.Length == 1 && upper.videos[0].creator == Creator, "Creator matching is exact and case-insensitive.");
            foreach (string wrong in new[] { "machine_publishers_fake", "xmachine_publishers", "1984drh", "@machine_publishers", " machine_publishers ", "@@machine_publishers" })
            {
                var page = Parse(List(Item(IdA, wrong)));
                r.Check(page.valid && page.videos.Length == 0 && page.rejectedItems == 1, "Foreign or malformed author is rejected: " + wrong);
            }
            var duet = SlopCityTikTokParser.ParsePublicList(List(Item(IdA, "1984drh", 1791515454,
                ",\"desc\":\"duet with @machine_publishers\",\"textExtra\":[{\"userUniqueId\":\"machine_publishers\"}]", false)), "1984drh");
            r.Check(duet.videos.Length == 1 && duet.videos[0].creator == "1984drh", "Duet caption mentions never replace the actual author.");
            foreach (string flag in new[] { "privateItem", "secret", "forFriend", "isProhibited", "isReviewing" })
                r.Check(Parse(List(Item(extra: ",\"" + flag + "\":true"))).videos.Length == 0, "Nonpublic post flag is respected: " + flag);
            r.Check(Parse(List(Item(extra: ",\"takeDown\":1"))).videos.Length == 0, "Taken-down posts are not eligible for playback.");
            r.Check(Parse(List(Item().Replace("\"privateAccount\":false", "\"privateAccount\":true"))).videos.Length == 0, "Private account posts are rejected.");
            r.Check(Parse(List(Item().Replace("\"uniqueId\":\"machine_publishers\"", "\"nickname\":\"machine_publishers\""))).videos.Length == 0,
                "A display nickname cannot impersonate the exact public creator handle.");
            foreach (string badId in new[] { "../12345", "12345\n", "1234", "1e19", "１２３４５", "7694500222541597983.mp4" })
                r.Check(Parse(List(Item(badId))).videos.Length == 0, "Only bounded ASCII numeric post IDs are accepted.");
            var stringTime = Parse(List(Item().Replace("\"createTime\":1791515454", "\"createTime\":\"1791515454\"")));
            r.Check(stringTime.videos.Length == 1 && stringTime.videos[0].createTime == 1791515454, "Video-detail string timestamps are accepted.");
            r.Check(!SlopCityTikTokParser.ParsePublicList(List(Item()), "unrelated_creator").valid, "Only the two requested accounts are accepted.");
        }

        private static void Hydration(Report r)
        {
            string detail = "{\"__DEFAULT_SCOPE__\":{\"webapp.video-detail\":{\"statusCode\":0,\"itemInfo\":{\"itemStruct\":" + Item() + "}}}}";
            var page = SlopCityTikTokParser.ParsePublicPage(Html(detail), Creator);
            r.Check(page.status == "ok" && page.videos.Length == 1 && page.videos[0].id == IdA, "Actual UNIVERSAL_DATA video-detail shape is extracted.");
            r.Check(SlopCityTikTokParser.ParsePublicPage(Html(detail), "1984drh").videos.Length == 0, "Hydrated detail for another creator is not imported.");
            string profile = "{\"__DEFAULT_SCOPE__\":{\"webapp.user-detail\":{\"statusCode\":0,\"userInfo\":{\"user\":{\"uniqueId\":\"machine_publishers\",\"secUid\":\"public-fixture-secuid\",\"privateAccount\":false},\"stats\":{\"videoCount\":10}}}}}";
            var user = SlopCityTikTokParser.ParsePublicPage(Html(profile), Creator);
            r.Check(user.status == "ok" && user.secUid == "public-fixture-secuid" && user.totalReported == 10 && user.videos.Length == 0,
                "Profile metadata exposes enumeration identity and count without claiming a profile preview is a full list.");
            var hidden = SlopCityTikTokParser.ParsePublicPage(Html(profile.Replace("\"privateAccount\":false", "\"privateAccount\":true")), Creator);
            r.Check(hidden.privateAccount && hidden.status == "private" && hidden.videos.Length == 0, "Private profile returns an explicit unavailable-for-playback status.");
            var wrong = SlopCityTikTokParser.ParsePublicPage(Html(profile), "1984drh");
            r.Check(wrong.status == "creator_mismatch" && wrong.secUid == "", "A foreign profile cannot supply the requested account's secUid.");
            string embed = "{\"props\":{\"pageProps\":{\"videoData\":{\"itemInfos\":{\"id\":\"" + IdA + "\",\"text\":\"Embedded clip\",\"video\":{\"urls\":[" + Quote(VideoUrl) + "]}},\"authorInfos\":{\"uniqueId\":\"machine_publishers\"}}}}}";
            var embedded = SlopCityTikTokParser.ParsePublicPage(Html(embed, "__NEXT_DATA__"), Creator);
            r.Check(embedded.status == "ok" && embedded.videos.Length == 1 && embedded.videos[0].mediaUrl == VideoUrl, "NEXT_DATA public embed itemInfos/authorInfos shape is supported.");
            var legacy = SlopCityTikTokParser.ParsePublicPage(Html("{\"ItemModule\":{\"" + IdA + "\":" + Item() + "}}", "SIGI_STATE"), Creator);
            r.Check(legacy.videos.Length == 1, "Legacy SIGI top-level authored items are supported without recursively collecting recommendations.");
            var redirected = SlopCityTikTokParser.ParsePublicPage("<html>Verify to continue<script>window.challenge=true;</script></html>", Creator);
            r.Check(redirected.status == "unavailable" && redirected.videos.Length == 0, "Challenge HTML is unavailable, never a successful empty profile.");
            var unavailable = SlopCityTikTokParser.ParsePublicPage(Html(detail.Replace("\"statusCode\":0", "\"statusCode\":10204")), Creator);
            r.Check(unavailable.status == "unavailable" && unavailable.videos.Length == 0, "Nonzero hydration status suppresses stale embedded items.");
        }

        private static void Slideshows(Report r)
        {
            var slide = Parse(List(Photo(IdA, 1791515454)));
            r.Check(slide.valid && slide.videos.Length == 1 && slide.photoPosts == 1 && slide.rejectedItems == 0, "Public slideshow remains in the catalogue and photo diagnostics.");
            if (slide.videos.Length == 1)
            {
                var post = slide.videos[0];
                r.Check(post.kind == "slideshow" && post.imageUrls.Length == 2 && post.imageUrls[0] == ImageUrl, "Slideshow frame order and preferred HTTPS image URLs are retained.");
                r.Check(post.audioUrl == AudioUrl && post.sourceUrl.EndsWith("/photo/" + IdA), "Slideshow keeps accompaniment and canonical public photo source.");
                r.Check(SlopCityTikTokParser.ValidateVideo(post, Creator), "A complete slideshow is playable without an MP4 mediaUrl.");
            }
            var finalPhoto = Parse(List(Item(IdA, Creator, 1791515454) + "," + Photo(IdB, 1763244162), "true"));
            r.Check(finalPhoto.cursor == 1763244162000L && finalPhoto.videos.Length == 2, "A last-item slideshow advances creator pagination correctly.");
            r.Check(Parse(List(Photo().Replace("\"imagePost\"", "\"itemImagePost\""))).photoPosts == 1, "Alternate itemImagePost metadata is recognized.");
            var noAudio = Parse(List(Photo().Replace(AudioUrl, "http://unsafe.invalid/song.mp3")));
            r.Check(noAudio.videos.Length == 1 && noAudio.videos[0].audioUrl == "", "Unavailable slideshow accompaniment does not remove valid public frames.");
            var badFrame = Parse(List(Photo().Replace(ImageUrl, "https://unrelated.invalid/image.jpeg")));
            r.Check(badFrame.videos.Length == 0 && badFrame.rejectedItems == 1, "A malformed slideshow cannot silently drop frames and claim full discovery.");
        }

        private static void AddressSafety(Report r)
        {
            r.Check(SlopCityTikTokParser.IsMediaUrl(VideoUrl), "TikTok v16 direct-CDN /video routes are playable; they are not post URLs.");
            r.Check(SlopCityTikTokParser.IsMediaUrl("https://v19.tiktokcdn.com/public/clip.mp4"), "TikTok CDN MP4 addresses are accepted.");
            r.Check(SlopCityTikTokParser.IsImageUrl(ImageUrl), "TikTok public image CDN addresses are accepted.");
            foreach (string bad in new[] {
                "https://www.tiktok.com/@machine_publishers/video/" + IdA, "https://www.tiktok.com/embed/v2/" + IdA,
                "https://www.tiktok.com/@machine_publishers", "http://v16.tiktokcdn.com/file.mp4", "file:///private/tmp/clip.mp4",
                "javascript:alert(1)", "https://v16.tiktokcdn.com.evil.invalid/file.mp4", "https://evil.invalid/file.mp4",
                "https://user:password@v16.tiktokcdn.com/file.mp4", "https://v16.tiktokcdn.com:444/file.mp4",
                "https://v16.tiktokcdn.com/file.mp4\n", "https://v16.tiktokcdn.com/file.mp4#fragment"
            }) r.Check(!SlopCityTikTokParser.IsMediaUrl(bad), "Unsafe or nonmedia address is rejected: " + bad);
            r.Check(SlopCityTikTokParser.IsSafeLocalFile(IdA + ".mp4", IdA), "Cached video filename is exactly the expected numeric post ID.");
            foreach (string bad in new[] { "../" + IdA + ".mp4", "/tmp/" + IdA + ".mp4", "folder/" + IdA + ".mp4", "folder\\" + IdA + ".mp4", IdA + ".mp4.exe", IdA + "\n.mp4", "123.mp4", IdB + ".mp4" })
                r.Check(!SlopCityTikTokParser.IsSafeLocalFile(bad, IdA), "Cache paths cannot escape their expected post filename.");
            var valid = Parse(List(Item())).videos.FirstOrDefault();
            r.Check(SlopCityTikTokParser.ValidateVideo(valid, Creator), "An extracted video meets catalogue validation.");
            if (valid != null)
            {
                valid.sourceUrl = "https://www.tiktok.com/@1984drh/video/" + IdA;
                r.Check(!SlopCityTikTokParser.ValidateVideo(valid, Creator), "Cross-account source URL cannot relabel an item.");
                valid.sourceUrl = "https://www.tiktok.com/@machine_publishers/video/" + IdB;
                r.Check(!SlopCityTikTokParser.ValidateVideo(valid, Creator), "Source URL ID must match the catalogue item ID.");
            }
        }

        private static void MalformedData(Report r)
        {
            foreach (string json in new[] { "null", "[]", "{", "{\"a\":", "{\"a\":1,}", "{\"a\":01}", "{\"a\":1e}", "{\"a\":truee}", "{\"a\":\"\\uZZZZ\"}", "{\"a\":\"\\x00\"}", "{\"a\":\"line\nfeed\"}", List("") + "garbage", "{\"statusCode\":0,\"statusCode\":1,\"itemList\":[],\"hasMore\":false}" })
                r.Check(!Parse(json).valid, "Malformed, ambiguous or trailing JSON is rejected without throwing.");
            string deep = "{\"deep\":" + new string('[', 80) + "0" + new string(']', 80) + "}";
            r.Check(!Parse(deep).valid, "JSON nesting depth is bounded.");
            r.Check(!Parse(new string(' ', SlopCityTikTokParser.MaxDocumentCharacters + 1)).valid, "Response allocation is bounded before JSON parsing.");
            r.Check(SlopCityTikTokParser.ParsePublicPage(new string('x', SlopCityTikTokParser.MaxDocumentCharacters + 1), Creator).status == "invalid", "HTML hydration size is bounded too.");
            string unicode = Item(extra: ",\"desc\":\"\\u96fb\\u8133 \\ud83c\\udf03\\n[caption]\"", description: false);
            var utf = Parse(List(unicode));
            r.Check(utf.videos.Length == 1 && utf.videos[0].title.Contains("電脳") && !utf.videos[0].title.Contains("\n"), "JSON Unicode survives decoding while control characters are removed from HUD titles.");
            var markup = Parse(List(Item(extra: ",\"desc\":\"<size=200>caption</size>\"", description: false)));
            r.Check(markup.videos.Length == 1 && !markup.videos[0].title.Contains("<"), "Remote captions cannot inject Unity rich-text formatting.");
        }

        private static SlopCityTikTokParser.ListPage Parse(string json) => SlopCityTikTokParser.ParsePublicList(json, Creator);
        private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
        private static string Item(string id = IdA, string creator = Creator, long created = 1791515454, string extra = "", bool description = true)
        {
            return "{\"id\":" + Quote(id) + ",\"createTime\":" + created + (description ? ",\"desc\":\"Public fixture\"" : "")
                + ",\"author\":{\"uniqueId\":" + Quote(creator) + ",\"privateAccount\":false},\"authorStats\":{\"videoCount\":10},\"video\":{\"duration\":65,\"playAddr\":" + Quote(VideoUrl) + "}" + extra + "}";
        }
        private static string Photo(string id = IdA, long created = 1791515454)
        {
            return "{\"id\":" + Quote(id) + ",\"createTime\":" + created + ",\"author\":{\"uniqueId\":\"machine_publishers\"},\"imagePost\":{\"images\":[{\"imageURL\":{\"urlList\":[" + Quote(ImageUrl) + "]}},{\"imageURL\":{\"urlList\":[" + Quote(ImageUrl + "?frame=2") + "]}}]},\"music\":{\"playUrl\":" + Quote(AudioUrl) + "}}";
        }
        private static string List(string items, string more = "false") => "{\"statusCode\":0,\"itemList\":[" + items + "],\"hasMorePrevious\":" + more + "}";
        private static string Html(string json, string id = "__UNIVERSAL_DATA_FOR_REHYDRATION__") => "<!doctype html><html><script data-test='fixture' type='application/json' id='" + id + "'>" + json + "</script></html>";

        [Serializable]
        public sealed class Report
        {
            public bool passed;
            public int checks;
            public string checkedAtUtc;
            public string scope = "Offline parser fixtures: complete/partial paging, public identity, slideshow frames, exact IDs, schema changes, bounded malformed data and source/cache address safety.";
            public List<string> errors = new List<string>();
            public void Check(bool condition, string description) { checks++; if (!condition) errors.Add(description); }
            public void ThrowIfInvalid() { if (!passed) throw new InvalidOperationException("TikTok parser validation failed: " + string.Join("; ", errors)); }
        }
    }
}
#endif
