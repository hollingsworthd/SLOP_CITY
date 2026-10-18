#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Dedicated batch Play Mode test of real public pagination, native playback and photo posts.</summary>
    [InitializeOnLoad]
    public static class SlopCityTikTokSmokeTest
    {
        public const string ReportPath = "Builds/Reports/tiktok-playback-check.json";
        private const string Key = "SlopCity.TikTokSmoke.";
        private const string ProgressKey = "SlopCity.Sandbox.Progress.v1";
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt report;
        private static SlopCityTikTokSync sync;
        private static SlopCityCityMedia media;
        private static SlopCityFirstPerson controller;
        private static SlopCityPlayerInput input;
        private static SlopCityTikTokCatalog fullCatalog;
        private static double enteredAt;

        static SlopCityTikTokSmokeTest()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key + "Active", false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (state == PlayModeStateChange.EnteredEditMode) Finish(new Exception("Play Mode ended before TikTok verification completed."));
            };
            Application.logMessageReceived += (message, trace, type) =>
            {
                if (!SessionState.GetBool(Key + "Active", false)
                    || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
                if (report == null) report = NewReceipt();
                string diagnostic = message + "\n" + trace;
                bool editorSearchStartup = message.StartsWith("ArgumentOutOfRangeException:", StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(trace) && trace.Contains("UnityEditor.Search.SearchDatabase")
                    && trace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup") && !trace.Contains("SlopCity.");
                if (editorSearchStartup) report.editorDiagnostics.Add(diagnostic);
                else report.runtimeErrors.Add(diagnostic);
            };
        }

        // Omit -quit and -nographics. The runner needs native graphics/audio decoders and exits itself.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("TikTok smoke requires a dedicated batch Editor outside Play Mode.");
            SessionState.SetString(Key + "Started", DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(Key + "ProgressExists", PlayerPrefs.HasKey(ProgressKey));
            SessionState.SetString(Key + "Progress", PlayerPrefs.GetString(ProgressKey, ""));
            BackupFile("playback.json"); BackupFile("playback.json.tmp");
            SessionState.SetBool(Key + "Active", true);
            report = NewReceipt(); WriteReport();
            try
            {
                // Begin at the newest post for reproducible native decode coverage; restore the exact bytes on exit.
                foreach (string name in new[] { "playback.json", "playback.json.tmp" })
                    if (File.Exists(PlaybackPath(name))) File.Delete(PlaybackPath(name));
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }

        private static Receipt NewReceipt() => new Receipt
        {
            startedAtUtc = SessionState.GetString(Key + "Started", DateTime.UtcNow.ToString("O")),
            unityVersion = Application.unityVersion,
            graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName
        };

        private static void Tick()
        {
            if (!SessionState.GetBool(Key + "Active", false)) return;
            if (report == null) report = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(report.startedAtUtc).ToUniversalTime()).TotalSeconds > 420)
                    throw new TimeoutException("TikTok smoke exceeded 420 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (media)
                {
                    report.maxNativeDecoders = Math.Max(report.maxNativeDecoders, Players().Length);
                    if (report.maxNativeDecoders > 3) throw new InvalidOperationException("City network exceeded three shared native decoders.");
                }
                if (Work.Count == 0)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < .8) return;
                    Work.Push(Exercise());
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
            catch (Exception error) { Finish(error); }
        }

        private static IEnumerator Exercise()
        {
            Application.runInBackground = true;
            media = Object.FindFirstObjectByType<SlopCityCityMedia>();
            controller = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            sync = Object.FindFirstObjectByType<SlopCityTikTokSync>();
            Check(media && controller && sync && SlopCityTikTokSync.Instance == sync,
                "Saved scene initializes the real public TikTok synchronizer and city network.");
            input = controller.GetComponent<SlopCityPlayerInput>();
            Check(input, "Saved player retains its real input component.");
            controller.SendMessage("OnApplicationFocus", true); controller.SendMessage("OnApplicationPause", false);
            media.SendMessage("OnApplicationFocus", true); media.SendMessage("OnApplicationPause", false);
            sync.RequestRefresh();
            DateTime began = DateTime.Parse(report.startedAtUtc).ToUniversalTime();
            yield return Until(() => !sync.IsRefreshing && sync.Catalog.accounts.Length == 2
                && DateTime.TryParse(sync.Catalog.syncedAtUtc, out DateTime fetched) && fetched.ToUniversalTime() >= began,
                120, "A fresh public API refresh did not finish within 120 seconds.");
            fullCatalog = sync.Catalog;
            report.syncStatus = sync.Status; report.syncedAtUtc = fullCatalog.syncedAtUtc;
            report.publicPostCount = fullCatalog.videos.Length;
            report.publicVideoCount = fullCatalog.videos.Count(post => post.kind != "slideshow");
            report.publicSlideshowCount = fullCatalog.videos.Count(post => post.kind == "slideshow");
            Check(fullCatalog.version == 2 && fullCatalog.videos.Select(post => post.creator + "/" + post.id).Distinct().Count() == fullCatalog.videos.Length,
                "Fresh catalogue uses version 2 and contains no duplicate authored post IDs.");
            foreach (string creator in new[] { "machine_publishers", "1984drh" })
            {
                SlopCityTikTokAccount[] matches = fullCatalog.accounts.Where(account => account.creator == creator).ToArray();
                Check(matches.Length == 1, "Fresh sync includes exactly one account record for @" + creator + ".");
                SlopCityTikTokAccount account = matches[0];
                int discovered = fullCatalog.videos.Count(post => post.creator == creator);
                report.accounts.Add(new AccountEvidence { creator = creator, discovered = discovered,
                    reported = account.totalReported, complete = account.complete, status = account.status });
                Check(account.complete && account.totalReported > 0 && discovered == account.totalReported
                    && discovered == account.discoveredCount,
                    "@" + creator + " reaches terminal pagination with every currently reported public post, without assuming a fixed historical count.");
                Check(fullCatalog.videos.Where(post => post.creator == creator).All(post => SlopCityTikTokParser.ValidateVideo(post, creator)),
                    "Every imported @" + creator + " post retains validated ownership and playable source metadata.");
            }
            Check(media.PlaylistCount(0) == fullCatalog.videos.Count(post => post.creator == "machine_publishers")
                && media.PlaylistCount(1) == fullCatalog.videos.Count(post => post.creator == "1984drh")
                && media.PlaylistCount(2) == fullCatalog.videos.Length,
                "Creator channels contain their complete public catalogues and the third channel combines both.");
            Check(media.Status.Contains(fullCatalog.videos.Length + " TikTok posts") && !media.Status.Contains("Bundled"),
                "Live catalogue status identifies synchronized posts truthfully.");
            SlopCityCityScreen[] screens = Object.FindObjectsByType<SlopCityCityScreen>(FindObjectsSortMode.None);
            report.screenCount = screens.Length;
            Check(screens.Length == media.ScreenCount && screens.Length >= 48, "All saved megacity displays register with the shared live network.");
            for (int channel = 0; channel < 3; channel++)
            {
                var displays = screens.Where(screen => screen.Channel == channel).ToArray();
                Check(displays.Length > 0 && displays.Select(screen => screen.Display.sharedMaterial).Distinct().Count() == 1
                    && displays[0].Display.sharedMaterial.mainTexture is RenderTexture,
                    "Every display on channel " + channel + " shares one material and native render texture.");
            }
            Check(Players().Length == 0, "Public metadata can synchronize while the start menu creates no decoders.");
            controller.PlayOrResume(); controller.StopMedia();
            yield return Until(() => Players().Length == 3 && Enumerable.Range(0, 3).All(channel =>
                !media.IsSlideshow(channel) && !string.IsNullOrEmpty(media.CurrentVideoId(channel))
                && Player(channel) && Player(channel).isPrepared && Player(channel).isPlaying && Player(channel).frame >= 3),
                120, "All three initial catalogue channels did not decode native videos.");
            int[] decoderIds = Players().Select(player => player.GetInstanceID()).OrderBy(id => id).ToArray();
            for (int channel = 0; channel < 2; channel++) yield return VerifyVideo(channel);

            media.SetSuspended(true); yield return Delay(.15);
            string[] idsBefore = Enumerable.Range(0, 3).Select(media.CurrentVideoId).ToArray();
            double[] timesBefore = Enumerable.Range(0, 3).Select(channel => Player(channel).time).ToArray();
            media.ApplyTikTokCatalog(fullCatalog);
            media.ApplyTikTokCatalog(new SlopCityTikTokCatalog { videos = fullCatalog.videos.Concat(fullCatalog.videos).ToArray() });
            media.ApplyTikTokCatalog(new SlopCityTikTokCatalog());
            media.ApplyTikTokCatalog(new SlopCityTikTokCatalog { version = -1, videos = fullCatalog.videos });
            yield return Delay(.4);
            Check(Enumerable.Range(0, 3).All(channel => media.CurrentVideoId(channel) == idsBefore[channel]
                && Math.Abs(Player(channel).time - timesBefore[channel]) < .08)
                && media.PlaylistCount(2) == fullCatalog.videos.Length,
                "Identical, duplicate, empty and invalid refreshes preserve the active frame and deduplicated last-known playlist.");
            media.SetSuspended(false);
            yield return VerifyRealEndRotation(0);

            SlopCityTikTokVideo photo = fullCatalog.videos.FirstOrDefault(post => post.kind == "slideshow" && post.imageUrls.Length > 1
                && !string.IsNullOrEmpty(post.audioUrl));
            Check(photo != null, "Current public catalogue includes a multi-image post with music for genuine slideshow coverage.");
            report.photoPostId = photo.id; report.photoImageCount = photo.imageUrls.Length;
            media.SetSuspended(true);
            for (int image = 0; image < photo.imageUrls.Length; image++)
            {
                bool resolved = false; string path = null;
                sync.ResolveImage(photo, image, uri => { path = uri; resolved = true; });
                yield return Until(() => resolved, 50, "Public slideshow image resolution timed out.");
                Check(LocalFile(path, out string imagePath) && File.Exists(imagePath), "Slideshow image " + image + " resolves to the persistent media cache.");
                byte[] bytes = File.ReadAllBytes(imagePath);
                Check(bytes.Length > 128 && ((bytes[0] == 255 && bytes[1] == 216) || (bytes[0] == 137 && bytes[1] == 80)),
                    "Slideshow image " + image + " uses a native JPEG/PNG format.");
            }
            bool audioResolved = false; string audioUri = null;
            sync.ResolveAudio(photo, uri => { audioUri = uri; audioResolved = true; });
            yield return Until(() => audioResolved, 70, "Public slideshow music resolution/conversion timed out.");
            Check(LocalFile(audioUri, out string wavPath) && File.Exists(wavPath), "Real post music resolves to a cached native audio file.");
            byte[] wav = File.ReadAllBytes(wavPath);
            Check(wav.Length > 44 && System.Text.Encoding.ASCII.GetString(wav, 0, 4) == "RIFF"
                && System.Text.Encoding.ASCII.GetString(wav, 8, 4) == "WAVE", "Slideshow music is converted to playable PCM WAV rather than relabeled compressed bytes.");
            report.audioCacheBytes = wav.Length;
            media.ApplyTikTokCatalog(new SlopCityTikTokCatalog { accounts = fullCatalog.accounts, videos = new[] { photo } });
            media.SetSuspended(false);
            for (int channel = 1; channel < 3; channel++)
            {
                int selected = channel;
                yield return Until(() => media.IsSlideshow(selected) || (Player(selected) && Player(selected).isPrepared),
                    40, "A channel was still preparing before the photo transition.");
                if (!media.IsSlideshow(channel)) SeekNearEnd(Player(channel));
            }
            yield return Until(() => media.CurrentVideoId(1) == photo.id && media.IsSlideshow(1) && media.CurrentImageIndex(1) == 0
                && media.CurrentVideoId(2) == photo.id && media.CurrentImageIndex(2) >= 0,
                50, "The actual clip-end transition did not start the real photo queue.");
            media.SetSuspended(true);
            AudioSource photoAudio = Player(1).GetTargetAudioSource(0);
            yield return Until(() => photoAudio && photoAudio.clip && photoAudio.clip.loadState == AudioDataLoadState.Loaded,
                20, "Converted slideshow music did not load into the existing shared AudioSource.");
            Check(photoAudio.clip.samples > 0 && photoAudio.clip.channels > 0 && AudioEnergy(photoAudio.clip) > .000001,
                "Converted post music produces nonzero decoded PCM samples.");
            report.audioClipSeconds = photoAudio.clip.length;
            yield return VerifyPhotoFrame(1, 0);
            media.SetSuspended(false);
            yield return Until(() => photoAudio.isPlaying && photoAudio.time > .1f, 6, "Slideshow music did not advance after resume.");
            input.RequestPause();
            yield return Until(() => controller.IsPaused, 4, "Actual game pause input was not consumed.");
            yield return Delay(.15);
            int heldImage = media.CurrentImageIndex(1); float heldAudio = photoAudio.time;
            yield return Delay(6.1);
            Check(media.CurrentImageIndex(1) == heldImage && !photoAudio.isPlaying && Math.Abs(photoAudio.time - heldAudio) < .1f,
                "Actual game pause holds slideshow image and music clocks beyond the five-second image interval.");
            input.RequestPause();
            yield return Until(() => !controller.IsPaused, 4, "Actual game resume input was not consumed.");
            for (int image = 1; image < photo.imageUrls.Length; image++)
            {
                int expected = image;
                yield return Until(() => media.CurrentVideoId(1) == photo.id && media.CurrentImageIndex(1) == expected,
                    9, "Photo sequence did not advance to image " + image + " after resume.");
                yield return VerifyPhotoFrame(1, image);
            }
            Check(photoAudio.isPlaying, "Slideshow music resumes with the image sequence.");
            Check(Players().Select(player => player.GetInstanceID()).OrderBy(id => id).SequenceEqual(decoderIds),
                "Video rotation and photo playback reuse the same three native decoder objects.");
            media.SetSuspended(true); media.ApplyTikTokCatalog(fullCatalog);
            yield return VerifyConfirmedEmptyQueues();
            media.ApplyTikTokCatalog(fullCatalog);
            Check(media.PlaylistCount(2) == fullCatalog.videos.Length, "The complete synchronized catalogue is restored after focused photo and empty-account coverage.");
            Check(report.runtimeErrors.Count == 0, "No unexpected runtime, decoder or conversion errors occurred.");
        }

        private static IEnumerator VerifyConfirmedEmptyQueues()
        {
            yield return Until(() => Enumerable.Range(0, 3).All(channel => media.IsSlideshow(channel)
                || (Player(channel) && Player(channel).isPrepared)), 40, "Pending decode did not settle before empty-account verification.");
            string[] playing = Enumerable.Range(0, 3).Select(media.CurrentVideoId).ToArray();
            var unavailable = new SlopCityTikTokCatalog { accounts = new[] {
                new SlopCityTikTokAccount { creator = "machine_publishers", complete = false, discoveredCount = 0, totalReported = 0 }
            } };
            media.ApplyTikTokCatalog(unavailable);
            Check(media.PlaylistCount(2) == fullCatalog.videos.Length && media.PlaylistCount(0) > 0,
                "A partial empty creator response cannot erase prior playlists.");
            var machineEmpty = new SlopCityTikTokAccount { creator = "machine_publishers", complete = true, discoveredCount = 0, totalReported = -1 };
            media.ApplyTikTokCatalog(new SlopCityTikTokCatalog { accounts = new[] { machineEmpty } });
            int drhCount = fullCatalog.videos.Count(post => post.creator == "1984drh");
            Check(media.PlaylistCount(0) == 0 && media.PlaylistCount(1) == drhCount && media.PlaylistCount(2) == drhCount,
                "A terminal empty account with unknown reported total clears only its creator queue and combined entries; the missing other account retains its posts.");
            media.ApplyTikTokCatalog(new SlopCityTikTokCatalog { accounts = new[] { machineEmpty,
                new SlopCityTikTokAccount { creator = "1984drh", complete = true, discoveredCount = 0, totalReported = 0 }
            } });
            yield return Delay(.2);
            Check(Enumerable.Range(0, 3).All(channel => media.PlaylistCount(channel) == 0),
                "Explicit successful empty results for both accounts clear all future catalogue queues.");
            Check(Enumerable.Range(0, 3).All(channel => media.CurrentVideoId(channel) == playing[channel]),
                "Authoritative empty refreshes preserve the current playing post until its normal clip boundary.");
        }

        private static IEnumerator VerifyVideo(int channel)
        {
            VideoPlayer player = Player(channel);
            string id = media.CurrentVideoId(channel), creator = media.CurrentCreator(channel);
            var evidence = new VideoEvidence { creator = creator, postId = id, firstFrame = player.frame,
                firstSeconds = player.time, durationSeconds = player.length, nativeWidth = player.width, nativeHeight = player.height };
            Check(creator == (channel == 0 ? "machine_publishers" : "1984drh"), "Native creator channel owns the expected account.");
            Check(LocalFile(player.url, out string path) && path.StartsWith(sync.CacheDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && Path.GetFileNameWithoutExtension(path) == id, "Native video decodes the requested post from its persistent on-demand cache.");
            yield return Until(() => player.frame > evidence.firstFrame && player.time > evidence.firstSeconds + .15, 8, "Native video frame/time did not advance.");
            evidence.lastFrame = player.frame; evidence.lastSeconds = player.time; evidence.pixelVariance = PixelVariance(Texture(channel));
            Check(evidence.pixelVariance > .0001, "Native @" + creator + " video produces nonuniform visible decoded pixels.");
            Check(player.audioTrackCount > 0 && player.GetTargetAudioSource(0) && player.audioOutputMode == VideoAudioOutputMode.AudioSource,
                "Native @" + creator + " video routes its real audio track through the shared spatial source.");
            report.decodedVideos.Add(evidence);
        }

        private static IEnumerator VerifyRealEndRotation(int channel)
        {
            VideoPlayer player = Player(channel); string before = media.CurrentVideoId(channel); bool reachedEnd = false;
            VideoPlayer.EventHandler ended = _ => reachedEnd = true;
            player.loopPointReached += ended;
            SeekNearEnd(player);
            yield return Until(() => reachedEnd && media.CurrentVideoId(channel) != before && !string.IsNullOrEmpty(media.CurrentVideoId(channel)),
                60, "A real native clip-end event did not advance the catalogue playlist.");
            player.loopPointReached -= ended;
            report.rotationFrom = before; report.rotationTo = media.CurrentVideoId(channel);
            Check(true, "Seeking near the native clip end produces a real loopPointReached event and selects a different catalogue post.");
        }

        private static void SeekNearEnd(VideoPlayer player)
        {
            Check(player && player.isPrepared && player.canSetTime && player.length > 1, "Native decoder supports a real near-end seek.");
            player.time = Math.Max(0, player.length - .6);
        }

        private static IEnumerator VerifyPhotoFrame(int channel, int expected)
        {
            yield return Delay(.1);
            double variance = PixelVariance(Texture(channel));
            Check(media.CurrentImageIndex(channel) == expected && variance > .0001,
                "Real slideshow image " + expected + " decodes into nonuniform shared display pixels.");
            report.photoFrames.Add(new PhotoEvidence { index = expected, pixelVariance = variance });
        }

        private static double AudioEnergy(AudioClip clip)
        {
            int frames = Math.Min(4096, clip.samples); var values = new float[frames * clip.channels]; double energy = 0;
            foreach (float seconds in new[] { .5f, 2f, 5f })
            {
                int offset = Mathf.Clamp((int)(seconds * clip.frequency), 0, Math.Max(0, clip.samples - frames));
                if (clip.GetData(values, offset)) foreach (float value in values) energy = Math.Max(energy, Math.Abs(value));
            }
            return energy;
        }
        private static VideoPlayer[] Players() => media.GetComponentsInChildren<VideoPlayer>();
        private static VideoPlayer Player(int channel)
        {
            // The persistent session name remains stable even when its VideoPlayer target is detached for a photo.
            string name = channel == 0 ? "CityDecoder_broadcast" : channel == 1 ? "CityDecoder_record" : "CityDecoder_duet";
            return Players().FirstOrDefault(player => player.name == name);
        }
        private static RenderTexture Texture(int channel) => Object.FindObjectsByType<SlopCityCityScreen>(FindObjectsSortMode.None)
            .First(screen => screen.Channel == channel).Display.sharedMaterial.mainTexture as RenderTexture;
        private static bool LocalFile(string value, out string path)
        {
            path = "";
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || !uri.IsFile) return false;
            path = uri.LocalPath; return true;
        }
        private static IEnumerator Until(Func<bool> condition, double seconds, string error)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (!condition())
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(error + " " + (sync ? sync.Status : ""));
                yield return null;
            }
        }
        private static IEnumerator Delay(double seconds)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < deadline) yield return null;
        }
        private static double PixelVariance(RenderTexture source)
        {
            if (!source || !source.IsCreated()) return 0;
            RenderTexture previous = RenderTexture.active, sample = RenderTexture.GetTemporary(64, 36, 0, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(64, 36, TextureFormat.RGB24, false, true);
            try
            {
                Graphics.Blit(source, sample); RenderTexture.active = sample;
                pixels.ReadPixels(new Rect(0, 0, 64, 36), 0, 0); pixels.Apply();
                Color32[] data = pixels.GetPixels32(); double sum = 0, squared = 0; int count = 0;
                for (int y = 4; y < 32; y++) for (int x = 26; x < 38; x++)
                { Color32 pixel = data[y * 64 + x]; double luma = (pixel.r + pixel.g + pixel.b) / 765d; sum += luma; squared += luma * luma; count++; }
                return squared / count - Math.Pow(sum / count, 2);
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(sample); Object.DestroyImmediate(pixels); }
        }
        private static void Check(bool pass, string message)
        { if (!pass) throw new InvalidOperationException(message); report.checks.Add(message); }
        private static string PlaybackPath(string name) => Path.Combine(Application.persistentDataPath, "TikTok", name);
        private static void BackupFile(string name)
        {
            string path = PlaybackPath(name); bool exists = File.Exists(path);
            SessionState.SetBool(Key + name + ".Exists", exists);
            SessionState.SetString(Key + name + ".Bytes", exists ? Convert.ToBase64String(File.ReadAllBytes(path)) : "");
        }
        private static void RestoreFile(string name)
        {
            string path = PlaybackPath(name);
            if (SessionState.GetBool(Key + name + ".Exists", false))
            { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, Convert.FromBase64String(SessionState.GetString(Key + name + ".Bytes", ""))); }
            else if (File.Exists(path)) File.Delete(path);
        }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Key + "Active", false); Work.Clear();
            if (report == null) report = NewReceipt();
            if (error != null) report.errors.Add(error.ToString());
            try
            {
                // Stop all writers before restoring user settings; keep the useful newly downloaded media/cache catalogue.
                if (media) { media.SetSuspended(true); media.enabled = false; }
                if (sync) { sync.StopAllCoroutines(); sync.enabled = false; }
                if (EditorApplication.isPlaying)
                    foreach (SlopCitySandbox sandbox in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsSortMode.None)) Object.DestroyImmediate(sandbox);
                RestoreFile("playback.json"); RestoreFile("playback.json.tmp");
                if (SessionState.GetBool(Key + "ProgressExists", false)) PlayerPrefs.SetString(ProgressKey, SessionState.GetString(Key + "Progress", ""));
                else PlayerPrefs.DeleteKey(ProgressKey);
                PlayerPrefs.Save(); report.userPlaybackSettingsRestored = true;
            }
            catch (Exception restoreError) { report.errors.Add("User playback settings restoration failed: " + restoreError); }
            report.passed = report.errors.Count == 0 && report.runtimeErrors.Count == 0;
            report.result = report.passed ? "Passed" : "Failed"; report.finishedAtUtc = DateTime.UtcNow.ToString("O");
            WriteReport(); Debug.Log("SLOP_CITY_TIKTOK_SMOKE_" + report.result.ToUpperInvariant() + " " + ReportPath);
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
        private static void WriteReport()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }
        [Serializable] private sealed class Receipt
        {
            public string result = "Running", startedAtUtc, finishedAtUtc, syncedAtUtc, unityVersion, graphicsDevice, syncStatus;
            public string scope = "Real public-network catalogue sync and native on-demand media decode in the saved scene. Completeness is checked against current public counts; private/deleted posts are excluded. Only sampled videos and one complete photo post are downloaded/decoded, not every catalogued video. Speaker output is not listened to.";
            public bool passed, userPlaybackSettingsRestored;
            public int publicPostCount, publicVideoCount, publicSlideshowCount, screenCount, maxNativeDecoders, photoImageCount, audioCacheBytes;
            public string photoPostId, rotationFrom, rotationTo; public float audioClipSeconds;
            public List<AccountEvidence> accounts = new List<AccountEvidence>();
            public List<VideoEvidence> decodedVideos = new List<VideoEvidence>();
            public List<PhotoEvidence> photoFrames = new List<PhotoEvidence>();
            public List<string> checks = new List<string>(), errors = new List<string>(), runtimeErrors = new List<string>(), editorDiagnostics = new List<string>();
        }
        [Serializable] private sealed class AccountEvidence { public string creator, status; public int discovered, reported; public bool complete; }
        [Serializable] private sealed class VideoEvidence
        { public string creator, postId; public long firstFrame, lastFrame; public uint nativeWidth, nativeHeight; public double firstSeconds, lastSeconds, durationSeconds, pixelVariance; }
        [Serializable] private sealed class PhotoEvidence { public int index; public double pixelVariance; }
    }
}
#endif
