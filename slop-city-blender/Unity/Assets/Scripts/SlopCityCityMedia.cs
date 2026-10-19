using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

namespace SlopCity
{
    /// <summary>
    /// Independent screen assignments backed by a bounded decoder pool. Screens outside
    /// the pool retain their own thumbnail/last frame; only the strongest unobstructed
    /// nearby screen is audible. Native synchronization and bundled media share a catalogue.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityMedia : MonoBehaviour
    {
        private const int ChannelCount = 3;
        private static readonly List<ScreenState> retiredScreenResources = new List<ScreenState>();
        private const int MaximumScreenLoop = 5;
        private const float PrepareTimeout = 25f;
        private const float ResolveTimeout = 120f;
        private const string AnchorPrefix = "SlopCityMediaScreenAnchor_";
        private static readonly string[] Profiles = { "1984drh", "machine_publishers" };
        [SerializeField] private Transform cityRoot;
        [SerializeField] private Transform viewer;
        [SerializeField] private Shader screenShader;
        [SerializeField, Range(0f, 1f)] private float volume = 0.45f;
        private readonly List<SlopCityCityScreen> screens = new List<SlopCityCityScreen>();
        private readonly Channel[] channels = new Channel[ChannelCount];
        private readonly List<ScreenState> displays = new List<ScreenState>();
        private readonly List<ScreenState> candidates = new List<ScreenState>();
        private readonly Dictionary<SlopCityCityScreen, ScreenState> displayLookup = new Dictionary<SlopCityCityScreen, ScreenState>();
        private readonly SlopCityMediaCoroutines mediaCoroutines = new SlopCityMediaCoroutines();
        private readonly Plane[] frustum = new Plane[6];
        private readonly RaycastHit[] audioHits = new RaycastHit[16];
        private Camera viewCamera;
        private SlopCityMediaPlayer loungeMedia;
        private int activeDecoderLimit = ChannelCount;
        private const int MobileScreenTextureLimit = 8;
        private bool MobileWebBudget => activeDecoderLimit == 1;
        private float nextScreenUpdate, audioGain, audioHoldUntil, focusedAudioGain, featuredTickAt;
        private ScreenState audioOwner, desiredAudioOwner;
        private ScreenState mobilePendingOwner;
        private float mobileCandidateSince;
        private bool mobileLoungePriority, mobileLoungePriorityRequest;
        private float mobileLoungePrioritySince;
        private const float ScreenUpdateInterval = 0.2f;
        private const float ScreenViewRange = 48f;
        private readonly Dictionary<string, Retry> retries = new Dictionary<string, Retry>(StringComparer.Ordinal);
        private SlopCityTikTokCatalog pendingCatalog;
        private PlaybackProgress progress = new PlaybackProgress();
        private bool initialized, displayUnavailable;
        private bool menuSuspended = true;
        private bool focusSuspended;
        private bool applicationSuspended;
        private bool manuallyPaused;
        private bool refreshing;
        private bool feedUnavailable;
        private string feedUrl = "";
        private float refreshSeconds = 300f;
        private float nextRefresh;
        private string feedMessage = "Bundled clips. Open creator feeds for current TikTok posts.";

        public static SlopCityCityMedia Instance { get; private set; }
        public int ScreenCount => screens.Count;
        public bool IsPaused => manuallyPaused;
        public float Volume => volume;
        public bool IsRefreshing => refreshing;
        public int DecoderLimit => activeDecoderLimit;
        public int AssignedClipCount
        {
            get
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (ScreenState display in displays) if (display.screen && !string.IsNullOrEmpty(display.screen.ClipKey)) keys.Add(display.screen.ClipKey);
                return keys.Count;
            }
        }
        public SlopCityCityScreen AudibleScreen => audioOwner != null && focusedAudioGain > .001f ? audioOwner.screen : null;
        public int PlaylistCount(int channel) => channels[Mathf.Clamp(channel, 0, ChannelCount - 1)]?.playlist.Count ?? 0;
        public string CurrentVideoId(int channel) => channels[Mathf.Clamp(channel, 0, ChannelCount - 1)]?.currentVideo?.id ?? "";
        public string CurrentCreator(int channel) => channels[Mathf.Clamp(channel, 0, ChannelCount - 1)]?.currentVideo?.creator ?? "";
        public bool IsSlideshow(int channel) => channels[Mathf.Clamp(channel, 0, ChannelCount - 1)]?.slideshow ?? false;
        public int CurrentImageIndex(int channel) => channels[Mathf.Clamp(channel, 0, ChannelCount - 1)]?.imageIndex ?? -1;
        private bool Suspended => menuSuspended || focusSuspended || applicationSuspended || !isActiveAndEnabled;

        public string Status
        {
            get
            {
                if (displayUnavailable) return feedMessage;
                if (!initialized) return "City screens starting...";
                if (manuallyPaused) return "City screens paused.";
                if (refreshing) return "Checking configured media feed...";
                int streaming = 0, failed = 0;
                foreach (Channel channel in channels)
                {
                    if (channel == null) continue;
                    if (channel.failed) failed++;
                    if (channel.owner != null && channel.ready) streaming++;
                }
                if (failed == ChannelCount) return "City video unavailable. Creator feeds still open in your browser.";
                if (PlaylistCount(2) > 0) return PlaylistCount(2) + " posts / " + ScreenCount + " independent screens / "
                    + streaming + " active players / nearby directional audio.";
                if (streaming > 0) return ScreenCount + " city screens / " + streaming + " online channels."
                    + (feedUnavailable ? " Feed refresh unavailable; existing signals retained." : "");
                return ScreenCount + " city screens / " + feedMessage;
            }
        }

        public void Configure(Transform root, Transform player, Shader shader)
        {
            cityRoot = root;
            viewer = player;
            screenShader = shader;
            viewCamera = null;
            RefreshPlatformDecoderBudget();
        }

        private void RefreshPlatformDecoderBudget()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshDecoderBudget(true);
#else
            RefreshDecoderBudget(false);
#endif
        }

        public bool ReleaseMobileDecodersForLounge()
        {
            if (!MobileWebBudget) return false;
            bool releasedDecoder = false;
            desiredAudioOwner = null;
            foreach (Channel signal in channels)
                if (signal != null && (signal.owner != null || signal.player || signal.session))
                {
                    releasedDecoder |= signal.player || signal.session;
                    if (signal.owner != null) ReleaseSlot(signal);
                    else ReleasePlayback(signal);
                }
            return releasedDecoder;
        }

        private void RefreshDecoderBudget(bool browserBuild)
        {
            // Use the same established input decision that creates mobile controls;
            // native touch editions keep their three-slot pool and display quality.
            SlopCityPlayerInput input = viewer ? viewer.GetComponentInParent<SlopCityPlayerInput>() : null;
            activeDecoderLimit = browserBuild && input && input.UseTouchControls ? 1 : ChannelCount;
        }

        private void Awake() { Instance = this; }

        private void Start()
        {
            if (!viewer && Camera.main) viewer = Camera.main.transform;
            RefreshPlatformDecoderBudget();
            if (!screenShader) screenShader = Shader.Find("Unlit/Texture");
            if (!screenShader || !screenShader.isSupported)
            {
                feedMessage = "Video shader unavailable. Creator feeds still open in your browser.";
                displayUnavailable = true;
                return;
            }
            ReadConfiguration();
#if !UNITY_WEBGL || UNITY_EDITOR
            ReadProgress();
#endif
            SlopCityFeaturedBillboard.Ensure(cityRoot);
            DiscoverScreens();
            initialized = true;
            if (pendingCatalog != null) { ApplyTikTokCatalog(pendingCatalog); pendingCatalog = null; }
            nextRefresh = Time.unscaledTime + refreshSeconds;
            if (!string.IsNullOrEmpty(feedUrl)) RefreshFeed();
        }

        private void ReadConfiguration()
        {
            Feed settings = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            // StreamingAssets is an HTTP URL in WebGL. Browser builds deliberately
            // use the bundled archive rather than the native TikTok cache/feed.
            feedMessage = "Bundled clips. Open creator feeds for current TikTok posts.";
#else
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "Media", "city-feed.json");
                if (File.Exists(path) && new FileInfo(path).Length <= 131072)
                    settings = JsonUtility.FromJson<Feed>(File.ReadAllText(path));
            }
            catch (Exception exception) { Debug.LogWarning("City media configuration: " + exception.Message, this); }
            if (settings != null)
            {
                if (IsHttps(settings.feedUrl)) feedUrl = settings.feedUrl;
                refreshSeconds = Mathf.Clamp(settings.refreshSeconds, 60f, 3600f);
            }
#endif
            for (int i = 0; i < ChannelCount; i++)
            {
                Media fallback = BundledMedia(i);
                Media configured = FindMedia(settings, fallback.id);
                channels[i] = new Channel { index = i, fallback = fallback, media = fallback, advance = true };
                if (configured != null && IsDirectMedia(configured.mediaUrl))
                {
                    channels[i].legacy = configured;
                }
            }
        }

        /// <summary>Turn authored anchors into actual playable, raycastable screen surfaces.</summary>
        private void DiscoverScreens()
        {
            RegisterAnchors(cityRoot);
            SlopCityCityScreen.RegisterActiveScreens(this);
        }

        /// <summary>Admit one newly constructed subtree; never scan the scene during Update.</summary>
        public void RegisterAnchors(Transform root)
        {
            Transform[] objects = root ? root.GetComponentsInChildren<Transform>(true)
                : FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Transform anchor in objects)
            {
                if (!anchor.name.StartsWith(AnchorPrefix, StringComparison.Ordinal)) continue;
                if (anchor.GetComponentInChildren<SlopCityCityScreen>(true)) continue;
                GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
                surface.name = "CityMediaScreen_" + (anchor.parent ? anchor.parent.name : anchor.name);
                surface.transform.SetParent(anchor, false);
                surface.transform.localPosition = new Vector3(0, 0, 0.015f);
                surface.transform.localRotation = Quaternion.Euler(0, 180, 0);
                string suffix = anchor.name.Substring(AnchorPrefix.Length);
                int channel = suffix.StartsWith("Channel", StringComparison.Ordinal)
                    && int.TryParse(suffix.Substring("Channel".Length), out int authoredChannel)
                    ? Mathf.Clamp(authoredChannel, 0, ChannelCount - 1) : 0;
                surface.AddComponent<SlopCityCityScreen>().Configure(surface.GetComponent<Renderer>(), channel);
                var interaction = surface.AddComponent<SlopCityInteractable>();
                // Tall billboards use their street-level tuning consoles for objectives.
                interaction.Configure(SlopCityInteractionKind.Screen, "CITY VIDEO DISPLAY", false);
                interaction.ConfigureInteractionPoint(surface.transform.position);
            }
            SlopCityCityScreen[] discovered = root ? root.GetComponentsInChildren<SlopCityCityScreen>(true)
                : FindObjectsByType<SlopCityCityScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (SlopCityCityScreen screen in discovered) RegisterScreen(screen);
        }

        public void RegisterScreen(SlopCityCityScreen screen)
        {
            if (!RetryRetiredScreenResources()) return;
            if (!screen || !screen.isActiveAndEnabled || !isActiveAndEnabled || !screen.Display || !screenShader || !screenShader.isSupported || screens.Contains(screen)) return;
            int selected = Mathf.Clamp(screen.Channel, 0, ChannelCount - 1);
            if (channels[selected] == null) return;
            var state = new ScreenState { screen = screen, ordinal = displays.Count, fallback = BundledMedia(displays.Count % ChannelCount) };
            try
            {
                if (!screen.AudioOnly)
                {
                    state.material = new Material(screenShader);
                    state.material.name = "CityScreenMaterial_" + state.ordinal;
                    state.material.mainTexture = Texture2D.blackTexture;
                    if (!MobileWebBudget && !EnsureScreenTexture(state)) throw new InvalidOperationException("Video texture allocation failed.");
                }
                screen.Tune(selected, state.material);
                screens.Add(screen);
                displays.Add(state);
                displayLookup.Add(screen, state);
                AssignNextPost(state);
            }
            catch (Exception error)
            {
                screens.Remove(screen); displays.Remove(state); displayLookup.Remove(screen);
                CleanupMedia(screen.Restore);
                ReleaseScreenResources(state);
                Debug.LogWarning("City display unavailable: " + error.GetType().Name, this);
            }
        }

        /// <summary>Rebind a configured group or featured post without changing collection membership.</summary>
        public void RefreshScreenAssignment(SlopCityCityScreen screen)
        {
            if (!screen || !displayLookup.TryGetValue(screen, out ScreenState display)) return;
            if (display.slot != null) ReleaseSlot(display.slot);
            display.program = null;
            AssignNextPost(display);
            nextScreenUpdate = 0f;
        }

        /// <summary>Pause one physical display while releasing its decoder ownership.</summary>
        public void SetScreenPaused(SlopCityCityScreen screen, bool paused)
        {
            if (!screen || !displayLookup.TryGetValue(screen, out ScreenState display)) return;
            if (display.paused == paused) return;
            display.paused = paused;
            if (paused)
            {
                CapturePausedVideo(display);
                if (display.slot != null) ReleaseSlot(display.slot);
            }
            nextScreenUpdate = 0f;
        }

        // Stop intentionally forgets position; pause can retire the same decoder
        // without making the next Play restart the selected clip.
        public void ResetScreenPlayback(SlopCityCityScreen screen)
        {
            if (!screen || !displayLookup.TryGetValue(screen, out ScreenState display)) return;
            ClearPausedVideo(display);
            display.paused = true;
            if (display.slot != null) ReleaseSlot(display.slot);
            nextScreenUpdate = 0f;
        }

        private static void ClearPausedVideo(ScreenState display)
        {
            display.pausedVideoKey = null;
            display.pausedVideoTime = 0d;
            display.pausedVideoGeneration = -1;
        }

        private static bool ValidPlaybackSeconds(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d && value <= 3600d;

        private static void CapturePausedVideo(ScreenState display)
        {
            string key = display.post != null && display.post.kind != "slideshow" ? VideoKey(display.post) : null;
            if (string.IsNullOrEmpty(key) || display.pausedVideoKey != key || display.pausedVideoGeneration != display.generation)
                ClearPausedVideo(display);
            Channel slot = display.slot;
            // A second pause during preparation must not erase the still-pending
            // position. Only an actually prepared, same-post video can replace it.
            if (string.IsNullOrEmpty(key) || slot == null || slot.owner != display || !slot.ready || slot.slideshow
                || slot.currentVideo != display.post || !slot.player || !slot.player.isPrepared) return;
            double time = slot.player.time, duration = slot.player.length;
            if (!ValidPlaybackSeconds(time) || !ValidPlaybackSeconds(duration) || time >= duration - .05d)
            { ClearPausedVideo(display); return; }
            display.pausedVideoKey = key;
            display.pausedVideoTime = time;
            display.pausedVideoGeneration = display.generation;
        }

        private static void RestorePausedVideo(Channel signal, VideoPlayer source)
        {
            ScreenState display = signal.owner;
            if (display == null || display.slot != signal || !source || source != signal.player) return;
            string key = signal.currentVideo != null ? VideoKey(signal.currentVideo) : null;
            double time = display.pausedVideoTime;
            bool restore = source.isPrepared && source.canSetTime
                && !signal.slideshow && !string.IsNullOrEmpty(key) && signal.currentVideo == display.post && signal.currentVideo.kind != "slideshow"
                && key == display.pausedVideoKey && display.generation == display.pausedVideoGeneration
                && ValidPlaybackSeconds(time) && ValidPlaybackSeconds(source.length) && time < source.length - .05d;
            // Consume once even when the new stream cannot seek or has changed
            // duration. Never carry an old offset into a new post or clip loop.
            ClearPausedVideo(display);
            if (restore) source.time = time;
        }

        public bool HasScreen(SlopCityCityScreen screen) => screen && displayLookup.ContainsKey(screen);
        public Texture PictureFor(SlopCityCityScreen screen)
            => screen && displayLookup.TryGetValue(screen, out ScreenState display) ? display.texture : null;
        public bool ScreenIsPlaying(SlopCityCityScreen screen)
            => !Suspended && !manuallyPaused && screen && displayLookup.TryGetValue(screen, out ScreenState display)
                && !display.paused && display.slot != null && display.slot.ready && (display.slot.slideshow || (display.slot.player && display.slot.player.isPlaying));
        public bool ScreenIsLoading(SlopCityCityScreen screen)
            => screen && displayLookup.TryGetValue(screen, out ScreenState display) && display.slot != null
                && (display.slot.loading || display.slot.resolving);
        public string SourceFor(SlopCityCityScreen screen)
            => screen && displayLookup.TryGetValue(screen, out ScreenState display) ? display.post?.sourceUrl ?? "" : "";
        public double ScreenTime(SlopCityCityScreen screen, bool duration)
        {
            if (!screen || !displayLookup.TryGetValue(screen, out ScreenState display)
                || display.slot == null || !display.slot.player || !display.slot.player.isPrepared) return 0d;
            double value = duration ? display.slot.player.length : display.slot.player.time;
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0d ? 0d : value;
        }

        /// <summary>Retire a dynamic physical display without retaining its pictures or decoder ownership.</summary>
        public void UnregisterScreen(SlopCityCityScreen screen)
        {
            // A destroyed Unity object may compare equal to null while remaining
            // a dictionary key until its owner's OnDestroy is delivered.
            if (ReferenceEquals(screen, null) || !displayLookup.TryGetValue(screen, out ScreenState display)) return;
            RemoveScreen(display);
            nextScreenUpdate = 0f;
        }

        private void RemoveScreen(ScreenState display)
        {
            display.generation++;
            try
            {
                if (display.slot != null) ReleaseSlot(display.slot);
            }
            finally
            {
                if (audioOwner == display) { audioOwner = null; audioGain = focusedAudioGain = 0f; }
                if (desiredAudioOwner == display) desiredAudioOwner = null;
                if (mobilePendingOwner == display) mobilePendingOwner = null;
                displayLookup.Remove(display.screen);
                screens.Remove(display.screen);
                displays.Remove(display);
                candidates.Remove(display);
                CleanupMedia(() => { if (display.screen) display.screen.Restore(); });
                ReleaseScreenResources(display);
            }
            // Retry entries are shared clip/catalog backoff, rather than screen
            // ownership. Removing them here would reset another display's retry.
        }

        private bool EnsureScreenTexture(ScreenState display)
        {
            if (!RetryRetiredScreenResources()) return false;
            if (!display.screen || display.screen.AudioOnly) return false;
            if (display.texture && display.texture.IsCreated()) return true;
            try
            {
                if (!display.texture)
                {
#if UNITY_WEBGL && !UNITY_EDITOR
                    display.texture = new RenderTexture(display.screen.IsFeatured ? 270 : 320, display.screen.IsFeatured ? 480 : 180, 0, RenderTextureFormat.ARGB32);
#else
                    display.texture = new RenderTexture(display.screen.IsFeatured ? 360 : 640, display.screen.IsFeatured ? 640 : 360, 0, RenderTextureFormat.ARGB32);
#endif
                    display.texture.name = "CityScreen_" + display.ordinal;
                    display.texture.filterMode = FilterMode.Bilinear;
                    display.texture.wrapMode = TextureWrapMode.Clamp;
                    display.texture.useMipMap = false;
                    display.texture.autoGenerateMips = false;
                }
                if (!display.texture.Create()) throw new InvalidOperationException("Video texture allocation failed.");
                Clear(display.texture);
                display.material.mainTexture = display.texture;
                display.needsThumbnail = !display.screen.AudioOnly && display.post != null;
                return true;
            }
            catch (Exception error)
            {
                ReleaseScreenTexture(display);
                Debug.LogWarning("City display unavailable: " + error.GetType().Name, this);
                return false;
            }
        }

        private static void ReleaseScreenTexture(ScreenState display)
        {
            // Texture generations cancel poster writes, not the same post's pause
            // position. A phone retires this texture on its first paused interest
            // tick; carry only an already valid same-post bookmark across that.
            bool keepPausedVideo = display.pausedVideoGeneration == display.generation
                && !string.IsNullOrEmpty(display.pausedVideoKey) && display.post != null
                && display.post.kind != "slideshow" && display.pausedVideoKey == VideoKey(display.post);
            // Invalidate in-flight poster loads before releasing their destination.
            display.generation++;
            if (keepPausedVideo) display.pausedVideoGeneration = display.generation;
            else ClearPausedVideo(display);
            RenderTexture texture = display.texture;
            CleanupMedia(() => { if (display.material) display.material.mainTexture = Texture2D.blackTexture; });
            if (TryMediaCleanup(() => DisposeScreenTexture(texture)) || !texture) display.texture = null;
            display.needsThumbnail = display.screen && !display.screen.AudioOnly && display.post != null;
        }

        private static void DisposeScreenTexture(RenderTexture texture)
        {
            if (!texture) return;
            try { texture.Release(); }
            catch (Exception error) { Debug.LogWarning("City texture release: " + error.GetType().Name); }
            finally { Destroy(texture); }
        }

        private static void ReleaseScreenResources(ScreenState display)
        {
            CleanupMedia(() => ReleaseScreenTexture(display));
            Material material = display.material;
            if (TryMediaCleanup(() => { if (material) Destroy(material); }) || !material) display.material = null;
            if ((display.texture || display.material) && !retiredScreenResources.Contains(display))
                retiredScreenResources.Add(display);
        }

        private static bool RetryRetiredScreenResources()
        {
            // Refused native destruction is bounded by already-owned screens.
            // New screen/texture admissions wait instead of growing an orphan list.
            for (int i = retiredScreenResources.Count - 1; i >= 0; i--)
            {
                ScreenState display = retiredScreenResources[i];
                ReleaseScreenResources(display);
                if (!display.texture && !display.material) retiredScreenResources.RemoveAt(i);
            }
            return retiredScreenResources.Count == 0;
        }

        private void RefreshMobileScreenTextures()
        {
            if (!MobileWebBudget) return;
            foreach (ScreenState display in displays)
            {
                int rank = candidates.IndexOf(display);
                if (display.texture && display.slot == null && (rank < 0 || rank >= MobileScreenTextureLimit))
                    ReleaseScreenTexture(display);
            }
            for (int i = 0; i < Mathf.Min(MobileScreenTextureLimit, candidates.Count); i++)
                EnsureScreenTexture(candidates[i]);
        }

        private SlopCityCityScreen FindTuningScreen(Transform target)
        {
            if (!target || screens.Count == 0) return null;
            SlopCityCityScreen nearest = target.GetComponentInParent<SlopCityCityScreen>();
            if (!nearest) nearest = target.GetComponentInChildren<SlopCityCityScreen>();
            if (nearest) return nearest.isActiveAndEnabled && screens.Contains(nearest) ? nearest : null;
            float bestDistance = 12f * 12f;
            if (!nearest)
                foreach (SlopCityCityScreen screen in screens)
                {
                    if (!screen || !screen.isActiveAndEnabled || screen.IsFeatured) continue;
                    float distance = (screen.transform.position - target.position).sqrMagnitude;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance; nearest = screen;
                }
            return nearest && nearest.isActiveAndEnabled && displayLookup.ContainsKey(nearest) ? nearest : null;
        }

        public bool CanTuneScreen(Transform target)
        {
            SlopCityCityScreen screen = FindTuningScreen(target);
            return screen && !screen.IsFeatured;
        }

        public string TuneScreen(Transform target)
        {
            TryTuneScreen(target, out string message);
            return message;
        }

        public bool TryTuneScreen(Transform target, out string message)
        {
            SlopCityCityScreen nearest = FindTuningScreen(target);
            if (!nearest) { message = "No city display is in reach."; return false; }
            if (nearest.IsFeatured) { message = "Dedicated creator broadcast / this display keeps its featured post."; return false; }
            ScreenState display = displayLookup[nearest];
            if (nearest.AudioOnly)
            {
                SetScreenPaused(nearest, !display.paused);
                message = display.paused ? "Radio paused." : "Radio on.";
                return true;
            }
            int before = nearest.Channel;
            int selected = (before + 1) % ChannelCount;
            nearest.Tune(selected, display.material);
            if (display.slot != null) ReleaseSlot(display.slot);
            AssignNextPost(display);
            nextScreenUpdate = 0f;
            bool changed = nearest.Channel != before;
            message = changed ? "Tuned to " + nearest.ClipTitle : "The display did not accept the channel change.";
            return changed;
        }

        // Jobs observe the real pooled decoder; they never create a player or fabricate playback time.
        public bool TryObserveJobScreen(Transform target, out string identity, out int channel, out double time, out bool playing)
        {
            identity = ""; channel = -1; time = 0; playing = false;
            SlopCityCityScreen screen = FindTuningScreen(target);
            if (!screen || screen.IsFeatured || screen.AudioOnly) return false;
            ScreenState display = displayLookup[screen];
            identity = SlopCitySalvageItem.SourceIdentity(screen.transform) + ":" + screen.ClipKey;
            channel = screen.Channel;
            Channel slot = display.slot;
            if (slot != null && slot.owner == display && slot.player && slot.player.isPrepared)
            { time = slot.player.time; playing = slot.player.isPlaying && slot.player.texture; }
            return true;
        }
        public bool TryTuneJobScreen(Transform target, int requestedChannel, out string message)
        {
            SlopCityCityScreen screen = FindTuningScreen(target);
            if (!screen || screen.IsFeatured || screen.AudioOnly || requestedChannel < 0 || requestedChannel >= ChannelCount)
            { message = "Choose an ordinary city display and a valid channel."; return false; }
            if (screen.Channel == requestedChannel) { message = "The requested channel is already selected."; return true; }
            ScreenState display = displayLookup[screen];
            screen.Tune(requestedChannel, display.material);
            if (display.slot != null) ReleaseSlot(display.slot);
            AssignNextPost(display); nextScreenUpdate = 0;
            message = "Tuned to " + screen.ClipTitle;
            return screen.Channel == requestedChannel;
        }

        public string ChannelTitle(int channel)
        {
            channel = Mathf.Clamp(channel, 0, ChannelCount - 1);
            Channel signal = channels[channel];
            if (signal == null) return "City media";
            return (signal.currentVideo != null ? "TIKTOK / " : signal.remote ? "ONLINE / " : "BUNDLED / ") + signal.media.title;
        }

        public void TogglePaused()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClocks();
#endif
            manuallyPaused = !manuallyPaused;
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClocks();
#endif
            featuredTickAt = Time.unscaledTime;
            ApplyPlayback();
        }

        public void SetSuspended(bool suspended)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClocks();
#endif
            menuSuspended = suspended;
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClocks();
#endif
            featuredTickAt = Time.unscaledTime;
            ApplyPlayback();
        }

        public void SetVolume(float value)
        {
            if (!float.IsNaN(value) && !float.IsInfinity(value)) volume = Mathf.Clamp01(value);
        }

        private void Update()
        {
            if (!initialized) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClocks();
#endif
            if (Time.unscaledTime >= nextScreenUpdate)
            {
                nextScreenUpdate = Time.unscaledTime + ScreenUpdateInterval;
                PruneScreens();
                RefreshScreenInterest();
                foreach (Channel signal in channels)
                {
                    if (signal == null || signal.owner == null) continue;
                    if (signal.resumeImage && !Suspended && !manuallyPaused) StartImage(signal, signal.requestedImageIndex);
                    if (signal.resumeAudio && !Suspended && !manuallyPaused) RequestPhotoAudio(signal);
                    if (signal.advance && !signal.resolving && !signal.loading && !Suspended && !manuallyPaused
                        && Time.unscaledTime >= signal.nextAttempt) StartChannel(signal);
                    if (signal.loading && PreparationElapsed(signal) > PrepareTimeout)
                        FailChannel(signal, "Preparation timed out.");
                    if (signal.resolving && PreparationElapsed(signal) > ResolveTimeout)
                        FailChannel(signal, "Clip download timed out.");
                }
                ApplyPlayback();
                if (!refreshing && !string.IsNullOrEmpty(feedUrl) && Time.unscaledTime >= nextRefresh) RefreshFeed();
            }
            foreach (Channel signal in channels)
            {
                if (signal == null || !signal.slideshow || !signal.ready || signal.loading || signal.resolving
                    || signal.owner == null || Suspended || manuallyPaused) continue;
                signal.imageElapsed += Time.unscaledDeltaTime;
                if (signal.imageElapsed < 5f) continue;
                if (signal.imageIndex + 1 < signal.currentVideo.imageUrls.Length) StartImage(signal, signal.imageIndex + 1);
                else AdvanceScreen(signal);
            }
            UpdateFeaturedLoops();
            UpdateAudio();
            PumpThumbnails();
        }

        private void PruneScreens()
        {
            for (int index = displays.Count - 1; index >= 0; index--)
            {
                ScreenState display = displays[index];
                if (display.screen && display.screen.Display) continue;
                RemoveScreen(display);
            }
        }

        private void StartPreparationClock(Channel signal)
        {
            signal.startedAt = Time.unscaledTime;
#if UNITY_WEBGL && !UNITY_EDITOR
            signal.webPrepareSuspendedAt = MobileWebBudget && (Suspended || manuallyPaused) ? signal.startedAt : -1f;
#endif
        }

        private float PreparationElapsed(Channel signal)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (MobileWebBudget && (Suspended || manuallyPaused)) return 0f;
#endif
            return Time.unscaledTime - signal.startedAt;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void RefreshMobilePreparationClocks()
        {
            if (!MobileWebBudget) return;
            float now = Time.unscaledTime;
            bool suspended = Suspended || manuallyPaused;
            foreach (Channel signal in channels)
            {
                if (signal == null || (!signal.loading && !signal.resolving)) continue;
                if (suspended)
                {
                    if (signal.webPrepareSuspendedAt < 0f) signal.webPrepareSuspendedAt = now;
                }
                else if (signal.webPrepareSuspendedAt >= 0f)
                {
                    // Resolution may finish and start a fresh preparation during
                    // suspension. Exclude only this attempt's overlapping time.
                    signal.startedAt += Mathf.Max(0f, now - Mathf.Max(signal.startedAt, signal.webPrepareSuspendedAt));
                    signal.webPrepareSuspendedAt = -1f;
                }
            }
        }
#endif

        private void RefreshScreenInterest()
        {
            if (!viewCamera && viewer) viewCamera = viewer.GetComponentInChildren<Camera>();
            if (!viewCamera && !viewer) viewCamera = Camera.main;
            Transform listener = viewCamera ? viewCamera.transform : viewer;
            bool hasCamera = viewCamera && viewCamera.isActiveAndEnabled;
            if (hasCamera) GeometryUtility.CalculateFrustumPlanes(viewCamera, frustum);
            candidates.Clear();
            ScreenState strongest = null;
            foreach (ScreenState display in displays)
            {
                display.audioScore = 0f;
                display.audioPriority = 0f;
                display.aimed = false;
                display.priority = 0f;
                SlopCityCityScreen screen = display.screen;
                if (!listener || !screen || !screen.isActiveAndEnabled || !screen.Display || (!screen.AudioOnly && !screen.Display.enabled)
                    || display.paused) continue;
                Vector3 centre = screen.Display.bounds.center;
                Vector3 toListener = listener.position - centre;
                float distance = toListener.magnitude;
                float facing = distance > .001f ? Vector3.Dot(screen.FrontNormal, toListener / distance) : 1f;
                bool visible = !screen.AudioOnly && facing > .03f && distance < ScreenViewRange
                    && (!hasCamera || GeometryUtility.TestPlanesAABB(frustum, screen.Display.bounds))
                    && !AudioOccluded(listener, screen, centre, distance);
                Vector3 nearbyPoint = screen.ClosestSurfacePoint(listener.position);
                float nearbyDistance = Vector3.Distance(listener.position, nearbyPoint);
                float nearbyGain = 1f - Mathf.InverseLerp(1.5f, screen.AudibleRange, nearbyDistance);
                if (nearbyGain > .001f && !AudioOccluded(listener, screen, nearbyPoint, nearbyDistance))
                    display.audioScore = nearbyGain;
                if (screen.TryAimPoint(listener, out Vector3 aimPoint))
                {
                    float aimedDistance = Vector3.Distance(listener.position, aimPoint);
                    float aimedGain = .8f * (1f - Mathf.InverseLerp(1.5f, screen.FocusedAudibleRange, aimedDistance));
                    if (aimedGain > .001f && !AudioOccluded(listener, screen, aimPoint, aimedDistance))
                    {
                        display.audioScore = Mathf.Max(display.audioScore, aimedGain);
                        display.aimed = true;
                    }
                }
                if (display.audioScore > .001f)
                {
                    // Looking directly at a screen chooses its broadcast over another
                    // nearby one. Gain remains a separate, smoothly attenuated value.
                    display.audioPriority = display.audioScore + (display.aimed ? 1f : 0f);
                    if (strongest == null || display.audioPriority > strongest.audioPriority) strongest = display;
                }
                if (visible) display.priority = 2f / (1f + distance / 12f);
            }
            if (audioOwner != null && audioOwner.slot != null && audioOwner.audioScore > .001f
                && !(strongest != null && strongest.aimed && !audioOwner.aimed)
                && (Time.unscaledTime < audioHoldUntil || strongest == null
                    || strongest.audioPriority < audioOwner.audioPriority * 1.25f)) strongest = audioOwner;
            foreach (ScreenState display in displays)
            {
                // Reserve one decoder for the audible screen even when it is offscreen;
                // the remaining slots serve visible pictures, without a chorus behind us.
                if (display == strongest) display.priority = 10f + display.audioPriority;
                else if (display == audioOwner && audioGain > .001f) display.priority = 9f;
                if (display.priority <= 0f) continue;
                if (display.slot != null) display.priority += .08f;
                candidates.Add(display);
            }
            candidates.Sort((left, right) =>
            {
                int order = right.priority.CompareTo(left.priority);
                return order != 0 ? order : left.ordinal.CompareTo(right.ordinal);
            });
            RefreshMobileLoungePriority(strongest != null && strongest.aimed && !Suspended && !manuallyPaused);
            if (Suspended || manuallyPaused) { desiredAudioOwner = null; return; }
            if (activeDecoderLimit == 1)
            {
                ScreenState requested = candidates.Count > 0 ? candidates[0] : null;
                ScreenState selected = ConfirmMobileScreen(requested);
                if (selected != requested)
                {
                    // Retain the existing decoder during a brief camera sweep;
                    // this never extends its actual audio ray/range eligibility.
                    candidates.Remove(selected);
                    if (selected != null) candidates.Insert(0, selected);
                    strongest = selected != null && selected.audioScore > .001f ? selected : null;
                }
            }
            if (activeDecoderLimit == 1 && strongest != null && audioOwner != null && audioOwner != strongest
                && audioOwner.slot != null && focusedAudioGain > .001f)
            {
                // One phone slot cannot prepare the next clip while retaining the old
                // audible decoder. Finish the existing fade-down before stopping it;
                // normal pool rebinding then prepares the new source and fades it in.
                desiredAudioOwner = null;
                return;
            }
            foreach (Channel signal in channels)
            {
                if (signal == null || signal.owner == null) continue;
                int rank = candidates.IndexOf(signal.owner);
                if (rank < 0 || rank >= activeDecoderLimit || (MobileWebBudget && SlopCityMediaPlayer.MobileDecoderInUse)) ReleaseSlot(signal);
            }
            RefreshMobileScreenTextures();
            int decoderBudget = MobileWebBudget && SlopCityMediaPlayer.MobileDecoderInUse ? 0 : activeDecoderLimit;
            for (int i = 0; i < Mathf.Min(decoderBudget, candidates.Count); i++)
            {
                ScreenState display = candidates[i];
                if (display.slot != null) continue;
                foreach (Channel signal in channels)
                {
                    if (signal == null || signal.owner != null) continue;
                    BindSlot(signal, display);
                    break;
                }
            }
            desiredAudioOwner = strongest != null && strongest.slot != null ? strongest : null;
        }

        private void RefreshMobileLoungePriority(bool aimedCityScreen, bool immediate = false)
        {
            if (!MobileWebBudget) return;
            if (immediate)
            {
                mobileLoungePriority = mobileLoungePriorityRequest = aimedCityScreen;
                mobileLoungePrioritySince = Time.unscaledTime;
            }
            else
            {
                if (mobileLoungePriorityRequest != aimedCityScreen)
                {
                    mobileLoungePriorityRequest = aimedCityScreen;
                    mobileLoungePrioritySince = Time.unscaledTime;
                }
                // Raw aim/occlusion can flicker while walking past the portrait.
                // Confirm both directions before destroying either device's decoder.
                // Deliberate city focus wins in .4s; brief losses get the same .8s
                // grace as screen selection. Audio eligibility still uses live rays.
                float confirmation = aimedCityScreen ? .4f : .8f;
                if (Time.unscaledTime - mobileLoungePrioritySince + .0001f >= confirmation)
                    mobileLoungePriority = aimedCityScreen;
            }
            if (!loungeMedia && viewer)
            {
                SlopCityFirstPerson person = viewer.GetComponentInParent<SlopCityFirstPerson>();
                if (person) loungeMedia = person.Media;
            }
            if (!loungeMedia) loungeMedia = FindFirstObjectByType<SlopCityMediaPlayer>();
            if (loungeMedia) loungeMedia.SetMobileCityPriority(mobileLoungePriority);
        }

        private ScreenState ConfirmMobileScreen(ScreenState requested)
        {
            ScreenState current = null;
            foreach (Channel signal in channels)
                if (signal != null && signal.owner != null) { current = signal.owner; break; }
            if (current == null || current == requested || !current.screen || !current.screen.isActiveAndEnabled)
            {
                mobilePendingOwner = requested;
                mobileCandidateSince = Time.unscaledTime;
                return requested;
            }
            if (mobilePendingOwner != requested)
            {
                mobilePendingOwner = requested;
                mobileCandidateSince = Time.unscaledTime;
            }
            // Two normal interest intervals confirm deliberate focus. A short empty
            // view grace avoids Stop/Prepare churn crossing a frustum edge; it never
            // grants audio eligibility and never changes a display's programming.
            float confirmation = requested != null ? .4f : .8f;
            return Time.unscaledTime - mobileCandidateSince < confirmation ? current : requested;
        }

        private bool AudioOccluded(Transform listener, SlopCityCityScreen screen, Vector3 centre, float distance)
        {
            if (distance < .25f) return false;
            Vector3 direction = (centre - listener.position).normalized;
            int count = Physics.RaycastNonAlloc(listener.position, direction, audioHits, distance - .15f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // Crowded city geometry must not hide a wall behind a truncated result buffer.
            RaycastHit[] hits = count == audioHits.Length
                ? Physics.RaycastAll(listener.position, direction, distance - .15f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                : audioHits;
            if (hits != audioHits) count = hits.Length;
            for (int i = 0; i < count; i++)
            {
                Transform hit = hits[i].transform;
                if (!hit || hit == screen.transform || hit.IsChildOf(screen.transform)
                    || hit == listener || hit.IsChildOf(listener)
                    || (viewer && (hit == viewer || hit.IsChildOf(viewer)))) continue;
                return true;
            }
            return false;
        }

        private void BindSlot(Channel signal, ScreenState display)
        {
            if (!display.screen.AudioOnly && !EnsureScreenTexture(display)) return;
            signal.owner = display;
            display.slot = signal;
            signal.texture = display.texture;
            signal.material = display.material;
            signal.hasScreen = true;
            signal.advance = true;
            signal.failed = false;
            // Destroy retires the previous decoder at the end of this frame;
            // let that finish before preparing a replacement on a phone.
            signal.nextAttempt = MobileWebBudget ? Time.unscaledTime + .25f : 0f;
        }

        private void ReleaseSlot(Channel signal)
        {
            ScreenState previous = signal.owner;
            try { StopPlayback(signal); }
            finally
            {
                try { ReleasePhoto(signal); }
                finally
                {
                    try
                    {
                        // Retire phone slots even when native Stop has failed.
                        if (MobileWebBudget) ReleasePlayback(signal);
                    }
                    finally
                    {
                        signal.operation++;
                        signal.resolving = false;
                        signal.advance = false;
                        signal.hasScreen = false;
                        signal.currentVideo = signal.pendingVideo = null;
                        signal.owner = null;
                        signal.texture = null;
                        signal.material = null;
                        if (previous != null) previous.slot = null;
                        if (audioOwner == previous) { audioOwner = null; audioGain = 0f; }
                        if (desiredAudioOwner == previous) desiredAudioOwner = null;
                    }
                }
            }
        }

        private void AdvanceScreen(Channel signal)
        {
            StopPlayback(signal);
            if (signal.owner != null && !signal.owner.screen.IsFeatured) AssignNextPost(signal.owner);
            signal.advance = true;
        }

        private void UpdateFeaturedLoops()
        {
            // Hold time is active playback time: menus, focus loss and manual pause freeze it.
            // Released slots clear their state and replay the same post when the screen is relevant again.
            float now = Time.unscaledTime;
            float delta = Mathf.Max(0f, now - featuredTickAt);
            featuredTickAt = now;
            if (Suspended || manuallyPaused) return;
            foreach (Channel signal in channels)
            {
                if (signal == null || signal.owner == null || !signal.owner.screen.IsFeatured
                    || !signal.ready || !signal.player || signal.advance || signal.resolving || signal.loading) continue;
                if (signal.featuredHolding)
                {
                    signal.featuredHoldRemaining = Mathf.Max(0f, signal.featuredHoldRemaining - delta);
                    if (signal.featuredHoldRemaining > 0f) continue;
                    signal.featuredHolding = false;
                    ApplyPlayback();
                }
                else if (!signal.featuredHeldThisLoop && signal.owner.screen.FeaturedHoldAtSeconds >= 0f
                    && signal.player.time >= signal.owner.screen.FeaturedHoldAtSeconds)
                {
                    signal.featuredHeldThisLoop = true;
                    signal.featuredHoldRemaining = signal.owner.screen.FeaturedHoldSeconds;
                    signal.featuredHolding = signal.featuredHoldRemaining > 0f;
                    if (!signal.featuredHolding) continue;
                    signal.player.Pause();
                    if (signal.audio) signal.audio.Pause();
                }
            }
        }

        private void UpdateAudio()
        {
            if (Suspended || manuallyPaused || volume <= 0f)
            {
                audioGain = 0f;
                desiredAudioOwner = null;
            }
            else
            {
                if (audioOwner != desiredAudioOwner)
                {
                    audioGain = Mathf.MoveTowards(audioGain, 0f, Time.unscaledDeltaTime / .18f);
                    if (audioGain <= .001f)
                    {
                        audioGain = 0f;
                        audioOwner = desiredAudioOwner;
                        audioHoldUntil = Time.unscaledTime + .5f;
                    }
                }
                else
                {
                    float target = audioOwner != null && audioOwner.slot != null && audioOwner.slot.ready
                        ? audioOwner.audioScore * volume : 0f;
                    audioGain = Mathf.MoveTowards(audioGain, target, Time.unscaledDeltaTime / .25f);
                }
            }
            SlopCityMediaAudioFocus.Submit(this, !Suspended && !manuallyPaused && audioOwner != null
                && audioOwner.slot != null && audioOwner.slot.ready ? audioOwner.audioScore : 0f, volume);
            float focusedGain = SlopCityMediaAudioFocus.Gain(this);
            focusedAudioGain = Mathf.Min(audioGain, focusedGain);
            foreach (Channel signal in channels)
            {
                if (signal == null) continue;
                float gain = signal.owner != null && signal.owner == audioOwner && signal.ready && !signal.featuredHolding
                    ? Mathf.Min(audioGain, focusedGain) : 0f;
                if (signal.audio)
                {
                    if (signal.owner != null && signal.owner.screen)
                    {
                        signal.audio.transform.position = signal.owner.screen.Display.bounds.center;
                        signal.audio.maxDistance = signal.owner.screen.FocusedAudibleRange;
                    }
                    signal.audio.volume = gain;
                }
#if UNITY_WEBGL && !UNITY_EDITOR
                // Browser video bypasses AudioSource. Retain the same direction/range/occlusion mix.
                if (signal.player && signal.player.isPrepared && signal.player.audioTrackCount > 0)
                {
                    if (!signal.player.isPlaying) gain = 0f;
                    signal.player.SetDirectAudioVolume(0, gain);
                    signal.player.SetDirectAudioMute(0, gain <= 0f);
                }
#endif
            }
        }

        /// <summary>Atomically replace playlist metadata. A playing clip always finishes before the new queue is used.</summary>
        public void ApplyTikTokCatalog(SlopCityTikTokCatalog catalog)
        {
            if (catalog == null || catalog.version != 2 || catalog.videos == null) return;
            if (!initialized) { pendingCatalog = catalog; return; }
            var unique = new Dictionary<string, SlopCityTikTokVideo>(StringComparer.Ordinal);
            foreach (SlopCityTikTokVideo video in catalog.videos)
            {
                if (!ValidCatalogVideo(video)) continue;
                string key = VideoKey(video);
                if (!unique.TryGetValue(key, out SlopCityTikTokVideo previous)
                    || (string.IsNullOrEmpty(previous.localFile) && !string.IsNullOrEmpty(video.localFile))
                    || (string.IsNullOrEmpty(previous.mediaUrl) && !string.IsNullOrEmpty(video.mediaUrl))) unique[key] = video;
            }
            bool hasMachine = false, hasDrh = false;
            foreach (SlopCityTikTokVideo video in unique.Values)
            {
                hasMachine |= video.creator == "machine_publishers";
                hasDrh |= video.creator == "1984drh";
            }
            bool clearMachine = !hasMachine && ConfirmsEmptyAccount(catalog, "machine_publishers");
            bool clearDrh = !hasDrh && ConfirmsEmptyAccount(catalog, "1984drh");
            // Failed, partial or ambiguous empty responses retain playback. An explicit,
            // successful terminal empty account must remove its queue at the next clip boundary.
            if (unique.Count == 0 && !clearMachine && !clearDrh) return;
            var ordered = new List<SlopCityTikTokVideo>(unique.Values);
            ordered.Sort((left, right) =>
            {
                int date = right.createTime.CompareTo(left.createTime);
                return date != 0 ? date : string.CompareOrdinal(VideoKey(left), VideoKey(right));
            });
            foreach (Channel signal in channels)
            {
                if (signal == null) continue;
                var updated = new List<SlopCityTikTokVideo>();
                foreach (SlopCityTikTokVideo video in ordered)
                    if (signal.index == 2 || video.creator == (signal.index == 0 ? "machine_publishers" : "1984drh")) updated.Add(video);
                bool confirmedClear = signal.index == 0 ? clearMachine : signal.index == 1 ? clearDrh : clearMachine || clearDrh;
                if (signal.index == 2 && confirmedClear)
                {
                    // An empty record for one creator is not evidence that the other creator
                    // disappeared. Keep its previous combined entries when that record is absent/partial.
                    foreach (SlopCityTikTokVideo previous in signal.playlist)
                        if ((previous.creator == "machine_publishers" && !clearMachine && !hasMachine)
                            || (previous.creator == "1984drh" && !clearDrh && !hasDrh)) updated.Add(previous);
                }
                if (updated.Count == 0 && !confirmedClear) continue;
                signal.playlist = updated;
                signal.failed = false;
                if (!signal.ready && !signal.loading && !signal.resolving) signal.advance = true;
            }
            PruneRetries(catalog, unique);
            if (catalog.TryGetFeaturedVideo(out SlopCityTikTokVideo featured))
            {
                foreach (ScreenState display in displays)
                    if (display.screen && display.screen.IsFeatured
                        && display.screen.GetComponentInParent<SlopCityFeaturedBillboard>(true))
                        display.screen.ConfigureFeaturedPost(featured.creator, featured.id,
                            SlopCityFeaturedBillboard.HoldAtSeconds, SlopCityFeaturedBillboard.HoldSeconds);
            }
            // Preserve an active clip through metadata refresh. Its next boundary
            // rebuilds the bounded program from the newly reconciled catalogue.
            foreach (ScreenState display in displays) display.program = null;
            ReconcileScreens();
            nextScreenUpdate = 0f;
        }

        private void PruneRetries(SlopCityTikTokCatalog catalog, Dictionary<string, SlopCityTikTokVideo> posts)
        {
            // Only a complete, internally consistent account snapshot establishes
            // removal. Failed/partial refreshes retain usable queues and their backoff.
            foreach (string creator in Profiles)
            {
                SlopCityTikTokAccount matched = null;
                bool duplicate = false;
                foreach (SlopCityTikTokAccount account in catalog.accounts ?? Array.Empty<SlopCityTikTokAccount>())
                {
                    if (account == null || account.creator != creator) continue;
                    if (matched != null) { duplicate = true; break; }
                    matched = account;
                }
                int count = 0;
                foreach (SlopCityTikTokVideo post in posts.Values) if (post.creator == creator) count++;
                if (duplicate || matched == null || !matched.complete || matched.discoveredCount != count
                    || matched.totalReported < -1 || matched.totalReported > count) continue;
                foreach (string key in new List<string>(retries.Keys))
                    if (key.StartsWith(creator + "/", StringComparison.Ordinal) && !posts.ContainsKey(key)) retries.Remove(key);
            }
        }

        private static string PostTitle(SlopCityTikTokVideo video)
            => string.IsNullOrWhiteSpace(video.title) ? "@" + video.creator : "@" + video.creator + " / " + video.title;

        private void ReconcileScreens()
        {
            PruneScreens();
            if (channels[2] == null) return;
            var byKey = new Dictionary<string, SlopCityTikTokVideo>(StringComparer.Ordinal);
            foreach (SlopCityTikTokVideo post in channels[2].playlist) byKey[VideoKey(post)] = post;
            foreach (ScreenState display in displays)
            {
                string key = display.post != null ? VideoKey(display.post) : "";
                if (display.screen.IsFeatured)
                {
                    if (byKey.TryGetValue(display.screen.FeaturedClipKey, out SlopCityTikTokVideo featured))
                    {
                        if (key != display.screen.FeaturedClipKey)
                        {
                            if (display.slot != null) ReleaseSlot(display.slot);
                            display.post = null; AssignNextPost(display);
                        }
                        else { display.post = featured; display.screen.AssignClip(VideoKey(featured), PostTitle(featured)); }
                    }
                    else if (display.post == null) AssignNextPost(display);
                    continue;
                }
                bool reserved = displays.Exists(other => other.screen && other.screen.IsFeatured && other.screen.FeaturedClipKey == key);
                if (!reserved && byKey.TryGetValue(key, out SlopCityTikTokVideo current))
                {
                    display.post = current;
                    display.screen.AssignClip(key, PostTitle(current));
                    continue;
                }
                if (!reserved && display.slot != null && display.slot.ready && display.post != null) continue;
                if (display.slot != null) ReleaseSlot(display.slot);
                display.post = null;
            }
            foreach (ScreenState display in displays) if (display.post == null) AssignNextPost(display);
        }

        private void AssignNextPost(ScreenState display)
        {
            if (!display.screen || channels[2] == null) return;
            ClearPausedVideo(display);
            List<SlopCityTikTokVideo> available = channels[2].playlist;
            // Radios use the same assigned video catalogue, without photo/picture work.
            if (display.screen.AudioOnly) available = available.FindAll(post => post != null && post.kind != "slideshow");
            if (display.screen.IsFeatured)
            {
                SlopCityTikTokVideo featured = available.Find(post => VideoKey(post) == display.screen.FeaturedClipKey);
                // Never replace a dedicated broadcast with unrelated archives or another creator's post.
                display.post = featured ?? (display.post != null && VideoKey(display.post) == display.screen.FeaturedClipKey ? display.post : null);
                display.generation++;
                display.needsThumbnail = !display.screen.AudioOnly && display.post != null;
                display.screen.AssignClip(display.screen.FeaturedClipKey,
                    display.post != null ? PostTitle(display.post) : "FEATURED CREATOR BROADCAST");
                return;
            }
            if (display.program == null || display.programChannel != display.screen.Channel)
            {
                var reserved = new HashSet<string>(StringComparer.Ordinal);
                foreach (ScreenState other in displays)
                    if (other.screen && other.screen.IsFeatured) reserved.Add(other.screen.FeaturedClipKey);
                string identity = SlopCitySalvageItem.SourceIdentity(display.screen.transform) + ":" + display.screen.Channel;
                display.program = BuildScreenLoop(available, identity, display.screen.Channel, reserved);
                display.programChannel = display.screen.Channel;
                display.programIndex = display.post == null && display.program.Count > 0
                    ? (int)(ScreenProgramRank(identity + ":phase", 2166136261u) % (uint)display.program.Count) - 1
                    : display.program.FindIndex(post => VideoKey(post) == VideoKey(display.post));
            }
            SlopCityTikTokVideo selected = null;
            for (int offset = 1; offset <= display.program.Count; offset++)
            {
                int index = (display.programIndex + offset) % display.program.Count;
                SlopCityTikTokVideo candidate = display.program[index];
                string key = VideoKey(candidate);
                if (displays.Exists(other => other.screen && other.screen.IsFeatured && other.screen.FeaturedClipKey == key)
                    || (retries.TryGetValue(key, out Retry retry) && Time.unscaledTime < retry.after)) continue;
                selected = candidate;
                display.programIndex = index;
                break;
            }
            display.post = selected;
            display.generation++;
            display.needsThumbnail = !display.screen.AudioOnly && selected != null;
            display.screen.AssignClip(selected != null ? VideoKey(selected) : "archive/" + display.fallback.localFile,
                selected != null ? PostTitle(selected) : "BUNDLED / " + display.fallback.title);
        }

        private static List<SlopCityTikTokVideo> BuildScreenLoop(List<SlopCityTikTokVideo> available,
            string identity, int channel, HashSet<string> reserved)
        {
            var result = new List<SlopCityTikTokVideo>(MaximumScreenLoop);
            if (available == null) return result;
            uint seed = ScreenProgramRank(identity ?? "", 2166136261u);
            // Keep creator channels meaningful. Only an empty preferred pool falls
            // back to the combined catalogue; dedicated broadcasts stay reserved.
            for (int pass = 0; pass < 2 && result.Count == 0; pass++)
            {
                foreach (SlopCityTikTokVideo candidate in available)
                {
                    if (candidate == null || string.IsNullOrEmpty(candidate.id)) continue;
                    string key = VideoKey(candidate);
                    if (reserved.Contains(key) || result.Exists(post => VideoKey(post) == key)) continue;
                    bool preferred = channel == 2 || candidate.creator == (channel == 0 ? "machine_publishers" : "1984drh");
                    if (pass == 0 && !preferred) continue;
                    uint rank = ScreenProgramRank(key, seed);
                    int index = 0;
                    while (index < result.Count)
                    {
                        string other = VideoKey(result[index]);
                        uint otherRank = ScreenProgramRank(other, seed);
                        if (rank < otherRank || (rank == otherRank && string.CompareOrdinal(key, other) < 0)) break;
                        index++;
                    }
                    if (index >= MaximumScreenLoop) continue;
                    if (result.Count == MaximumScreenLoop) result.RemoveAt(MaximumScreenLoop - 1);
                    result.Insert(index, candidate);
                }
            }
            return result;
        }

        private static uint ScreenProgramRank(string value, uint seed)
        {
            // Stable unsigned arithmetic across players; no global random state or
            // process-randomized string hashes affect screen identity or tuning.
            unchecked
            {
                uint rank = seed;
                foreach (char character in value) rank = (rank ^ character) * 16777619u;
                rank ^= rank >> 16; rank *= 0x7feb352du;
                rank ^= rank >> 15; rank *= 0x846ca68bu;
                return rank ^ (rank >> 16);
            }
        }

        private int thumbnailRequests;
        private void PumpThumbnails()
        {
            if (Suspended || thumbnailRequests >= (MobileWebBudget ? 1 : 2) || !SlopCityTikTokSync.Instance) return;
            if (MobileWebBudget)
                foreach (Channel signal in channels)
                    if (signal != null && signal.slideshow && (signal.loading || signal.resolving)) return;
            ScreenState next = null;
            // Fill nearby displays first, then finish the remaining low-resolution posters.
            foreach (ScreenState display in candidates) if (display.needsThumbnail && display.texture) { next = display; break; }
            if (next == null && !MobileWebBudget) foreach (ScreenState display in displays) if (display.needsThumbnail) { next = display; break; }
            if (next == null) return;
            next.needsThumbnail = false;
            ScreenState target = next;
            int generation = target.generation;
            SlopCityTikTokVideo post = target.post;
            if (post == null) return;
            thumbnailRequests++;
            SlopCityTikTokSync.Instance.ResolveThumbnail(post, url =>
            {
                if (!this) return;
                if (!isActiveAndEnabled || generation != target.generation
                    || !SlopCityTikTokSync.IsBundledMediaUri(post.bundledThumbnailFile, url))
                { thumbnailRequests = Mathf.Max(0, thumbnailRequests - 1); return; }
                mediaCoroutines.Start(this, LoadThumbnail(target, generation, url));
            });
        }

        private IEnumerator LoadThumbnail(ScreenState display, int generation, string url)
        {
            Texture2D thumbnail = null;
            try
            {
                using (UnityWebRequest request = SlopCityMediaTransfer.Get(url, SlopCityMediaTransfer.MaxImageBytes, 15))
                {
                    var operation = SlopCityMediaTransfer.Send(request);
                    if (operation == null) yield break;
                    while (!operation.isDone)
                    {
                        if (!display.screen || generation != display.generation) { request.Abort(); yield break; }
                        yield return null;
                    }
                    if (!display.screen || generation != display.generation || request.result != UnityWebRequest.Result.Success) yield break;
                    try
                    {
                        byte[] bytes = request.downloadHandler.data;
                        if (SlopCityMediaTransfer.ImageSizeIsSafe(bytes, out _, out _))
                        {
                            thumbnail = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                            if (thumbnail.LoadImage(bytes, true) && (display.slot == null || !display.slot.ready
#if UNITY_WEBGL && !UNITY_EDITOR
                                || display.slot.webAwaitingFirstPicture
#endif
                                ))
                                DrawImage(thumbnail, display.texture);
                        }
                    }
                    catch (Exception error) { Debug.LogWarning("City thumbnail unavailable: " + error.GetType().Name, this); }
                }
            }
            finally
            {
                if (thumbnail) Destroy(thumbnail);
                thumbnailRequests = Mathf.Max(0, thumbnailRequests - 1);
            }
        }

        private static bool ConfirmsEmptyAccount(SlopCityTikTokCatalog catalog, string creator)
        {
            SlopCityTikTokAccount matched = null;
            foreach (SlopCityTikTokAccount account in catalog.accounts ?? Array.Empty<SlopCityTikTokAccount>())
            {
                if (account == null || account.creator != creator) continue;
                if (matched != null) return false; // Conflicting/duplicate records are not authoritative.
                matched = account;
            }
            // Empty item lists cannot supply authorStats, so a valid terminal result may
            // have an unknown reported total (-1). A positive total contradicts emptiness.
            return matched != null && matched.complete && matched.discoveredCount == 0
                && (matched.totalReported == 0 || matched.totalReported == -1);
        }

        private void StartChannel(Channel signal)
        {
            if (signal.owner == null) return;
            if (!signal.playbackConfigured && (signal.session || signal.player || signal.audio))
            {
                // A retained native owner may only now accept Destroy. Give that
                // retirement its own frame before preparing any replacement.
                ReleasePlayback(signal);
                signal.advance = true;
                signal.nextAttempt = Time.unscaledTime + .25f;
                return;
            }
            ReleasePhoto(signal);
            if (signal.photoAudio)
            { signal.advance = true; signal.nextAttempt = Time.unscaledTime + 1f; return; }
            signal.advance = false;
            signal.failed = false;
            signal.pendingVideo = signal.owner.post;
            signal.pendingMedia = null;
            SlopCityTikTokVideo video = signal.pendingVideo;
            if (video == null && signal.owner.screen.IsFeatured)
            {
                signal.advance = true; signal.nextAttempt = Time.unscaledTime + 1f;
                return;
            }
            if (video != null)
            {
                string key = VideoKey(video);
                if (retries.TryGetValue(key, out Retry retry) && Time.unscaledTime < retry.after)
                {
                    AssignNextPost(signal.owner);
                    signal.advance = true;
                    signal.nextAttempt = signal.owner.screen.IsFeatured ? retry.after : Time.unscaledTime + 1f;
                    return;
                }
                signal.lastSelectedKey = key;
                signal.pendingMedia = new Media { id = video.id, title = PostTitle(video), creator = video.creator, sourceUrl = video.sourceUrl };
                signal.pendingRemote = string.IsNullOrEmpty(video.bundledVideoFile) && string.IsNullOrEmpty(video.bundledThumbnailFile);
                if (video.kind == "slideshow") { StartSlideshow(signal); return; }
                if (SlopCityTikTokSync.Instance)
                {
                    signal.resolving = true;
                    StartPreparationClock(signal);
                    int operation = ++signal.operation;
                    SlopCityTikTokSync.Instance.ResolveVideo(video, url =>
                    {
                        if (!this || operation != signal.operation || !signal.resolving) return;
                        signal.resolving = false;
                        if (!SafeResolvedUrl(video, url)) { FailChannel(signal, "Clip is not available yet."); return; }
                        PrepareChannel(signal, url);
                    }, () => this && isActiveAndEnabled && operation == signal.operation && signal.resolving);
                }
                else
                {
                    string local = CachedUrl(video, video.localFile);
                    if (!string.IsNullOrEmpty(local)) PrepareChannel(signal, local);
                    else if (SlopCityTikTokParser.IsMediaUrl(video.mediaUrl)) PrepareChannel(signal, video.mediaUrl);
                    else FailChannel(signal, "Clip resolver is unavailable.");
                }
                return;
            }
            Media fallback = signal.owner.fallback;
            Media legacy = channels[Mathf.Clamp(signal.owner.screen.Channel, 0, ChannelCount - 1)].legacy;
            signal.pendingRemote = legacy != null;
            signal.pendingMedia = legacy ?? fallback;
            string source = signal.pendingMedia.mediaUrl;
            if (!signal.pendingRemote)
            {
                string basePath = Application.streamingAssetsPath.TrimEnd('/');
                if (basePath.Contains("://")) source = basePath + "/Media/" + Uri.EscapeDataString(fallback.localFile);
                else
                {
                    string path = Path.Combine(basePath, "Media", fallback.localFile);
                    if (!File.Exists(path)) { FailChannel(signal, "Bundled file missing."); return; }
                    source = new Uri(Path.GetFullPath(path)).AbsoluteUri;
                }
            }
            PrepareChannel(signal, source);
        }

        private void EnsurePlayer(Channel signal)
        {
            if (signal.player && signal.playbackConfigured) return;
            if (signal.session || signal.player || signal.audio)
            {
                ReleasePlayback(signal);
                if (signal.session || signal.player || signal.audio)
                    throw new InvalidOperationException("The previous city decoder has not retired.");
            }
            try
            {
            signal.session = new GameObject("CityDecoder_" + signal.fallback.id);
            signal.session.transform.SetParent(transform, false);
            signal.audio = signal.session.AddComponent<AudioSource>();
            signal.audio.playOnAwake = false;
            signal.audio.spatialBlend = 1f;
            // Range and occlusion are already mixed once in UpdateAudio; retain native panning.
            signal.audio.rolloffMode = AudioRolloffMode.Custom;
            signal.audio.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(0f, 1f, 1f, 1f));
            signal.audio.minDistance = 1.5f;
            signal.audio.maxDistance = 14f;
            signal.audio.dopplerLevel = 0f;
            signal.audio.volume = 0f;
            signal.player = signal.session.AddComponent<VideoPlayer>();
            VideoPlayer player = signal.player;
            player.playOnAwake = false;
            player.source = VideoSource.Url;
            player.isLooping = false;
            player.waitForFirstFrame = true;
            player.skipOnDrop = true;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.aspectRatio = VideoAspectRatio.FitInside;
            player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
#if UNITY_WEBGL && !UNITY_EDITOR
            player.audioOutputMode = VideoAudioOutputMode.Direct;
#else
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
#endif
            player.controlledAudioTrackCount = 1;
            player.EnableAudioTrack(0, true);
#if !UNITY_WEBGL || UNITY_EDITOR
            player.SetTargetAudioSource(0, signal.audio);
#endif
                signal.playbackConfigured = true;
            }
            catch
            {
                ReleasePlayback(signal);
                throw;
            }
        }

        private void PrepareChannel(Channel signal, string url)
        {
            try
            {
                EnsurePlayer(signal);
                StopPlayback(signal);
                signal.loading = true;
                StartPreparationClock(signal);
                int operation = ++signal.operation;
                VideoPlayer player = signal.player;
                player.isLooping = signal.owner != null && signal.owner.screen.IsFeatured
                    && (signal.owner.screen.FeaturedHoldAtSeconds < 0f || signal.owner.screen.FeaturedHoldSeconds <= 0f);
                player.url = url;
                bool audioOnly = signal.owner != null && signal.owner.screen.AudioOnly;
                player.renderMode = audioOnly ? VideoRenderMode.APIOnly : VideoRenderMode.RenderTexture;
                player.targetTexture = audioOnly ? null : signal.texture;
#if UNITY_WEBGL && !UNITY_EDITOR
                if (MobileWebBudget && !audioOnly)
                {
                    // Preparation can finish at metadata. APIOnly still decodes and
                    // uploads, without letting Unity replace the poster with an empty frame.
                    player.renderMode = VideoRenderMode.APIOnly;
                    signal.webAwaitingFirstPicture = true;
                    ScreenState owner = signal.owner;
                    RenderTexture target = signal.texture;
                    signal.webFrameReadyHandler = (source, frame) =>
                    {
                        if (!this || !isActiveAndEnabled || !source || source != player || source != signal.player
                            || operation != signal.operation || !signal.webAwaitingFirstPicture
                            || signal.owner != owner || owner == null || owner.slot != signal
                            || !owner.screen || signal.texture != target) return;
                        Texture picture = source.texture;
                        if (!picture || picture.width <= 0 || picture.height <= 0) return;
                        try
                        {
                            // Preserve FitInside and copy before the mode switch can
                            // replace/release Unity's internal source texture.
                            DrawImage(picture, target);
                            signal.webAwaitingFirstPicture = false;
                            source.frameReady -= signal.webFrameReadyHandler;
                            source.sendFrameReadyEvents = false;
                            signal.webFrameReadyHandler = null;
                            source.targetTexture = target;
                            source.renderMode = VideoRenderMode.RenderTexture;
                        }
                        catch (Exception exception) { FailChannel(signal, exception.Message); }
                    };
                    player.frameReady += signal.webFrameReadyHandler;
                    player.sendFrameReadyEvents = true;
                }
                else
#endif
                {
                    player.targetTexture = signal.texture;
                }
                signal.preparedHandler = source =>
                {
                    if (source != signal.player || operation != signal.operation) return;
                    if (source.width > 4096 || source.height > 4096 || (ulong)source.width * source.height > 8UL * 1024 * 1024)
                    { FailChannel(signal, "Clip resolution exceeds this player's safe decode limit."); return; }
                    signal.ready = true;
                    signal.loading = false;
                    signal.media = signal.pendingMedia;
                    signal.remote = signal.pendingRemote;
                    signal.currentVideo = signal.pendingVideo;
                    RestorePausedVideo(signal, source);
#if UNITY_WEBGL && !UNITY_EDITOR
                    // Track metadata is not valid before Prepare completes. Begin
                    // muted so autoplay can start; Update applies nearby audio.
                    if (source.audioTrackCount > 0)
                    {
                        source.SetDirectAudioVolume(0, 0f);
                        source.SetDirectAudioMute(0, true);
                    }
#endif
                    if (signal.currentVideo != null)
                    {
                        string key = VideoKey(signal.currentVideo);
                        RememberVideo(signal, key);
                    }
                    ApplyPlayback();
                };
                signal.errorHandler = (source, error) =>
                {
                    if (source == signal.player && operation == signal.operation) FailChannel(signal, error);
                };
                signal.endedHandler = source =>
                {
                    if (source != signal.player || operation != signal.operation) return;
                    if (source.isLooping) return;
                    AdvanceScreen(signal);
                };
                player.prepareCompleted += signal.preparedHandler;
                player.errorReceived += signal.errorHandler;
                player.loopPointReached += signal.endedHandler;
                player.Prepare();
            }
            catch (Exception exception) { FailChannel(signal, exception.Message); }
        }

        private void StartSlideshow(Channel signal)
        {
            if (!SlopCityTikTokSync.Instance || signal.pendingVideo.imageUrls == null || signal.pendingVideo.imageUrls.Length == 0)
            { FailChannel(signal, "Photo post is not available yet."); return; }
            try
            {
                EnsurePlayer(signal);
                StopPlayback(signal);
                signal.slideshow = true;
                signal.photoGeneration++;
                StartImage(signal, 0);
            }
            catch (Exception error) { FailChannel(signal, error.Message); }
        }

        private void StartImage(Channel signal, int index)
        {
            if (!SlopCityTikTokSync.Instance) { FailChannel(signal, "Photo resolver is unavailable."); return; }
            signal.resumeImage = false;
            signal.requestedImageIndex = index;
            signal.ready = false;
            signal.resolving = true;
            StartPreparationClock(signal);
            int operation = ++signal.operation;
            SlopCityTikTokVideo video = signal.pendingVideo;
            SlopCityTikTokSync.Instance.ResolveImage(video, index, url =>
            {
                if (!this || operation != signal.operation || !signal.resolving) return;
                signal.resolving = false;
                if (!SafePhotoUrl(video, index, url)) { FailChannel(signal, "Photo download is not available yet."); return; }
                signal.loading = true;
                StartPreparationClock(signal);
                mediaCoroutines.Start(this, LoadImage(signal, operation, index, url));
            }, () => this && isActiveAndEnabled && operation == signal.operation && signal.resolving);
        }

        private IEnumerator LoadImage(Channel signal, int operation, int index, string url)
        {
            Texture2D photo = null;
            try
            {
                using (UnityWebRequest request = SlopCityMediaTransfer.Get(url, SlopCityMediaTransfer.MaxImageBytes, 15))
                {
                    var transfer = SlopCityMediaTransfer.Send(request);
                    if (transfer != null)
                        while (!transfer.isDone)
                        {
                            if (operation != signal.operation || !signal.slideshow) { request.Abort(); yield break; }
                            yield return null;
                        }
                    if (operation != signal.operation || !signal.slideshow) yield break;
                    bool accepted = false;
                    try
                    {
                        if (transfer != null && request.result == UnityWebRequest.Result.Success)
                        {
                            byte[] bytes = request.downloadHandler.data;
                            if (SlopCityMediaTransfer.ImageSizeIsSafe(bytes, out _, out _))
                            {
                                photo = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                                accepted = photo.LoadImage(bytes, true);
                                if (accepted) DrawImage(photo, signal.texture);
                            }
                        }
                    }
                    catch (Exception error) { accepted = false; Debug.LogWarning("City photo unavailable: " + error.GetType().Name, this); }
                    if (!accepted) { FailChannel(signal, "Photo could not be decoded safely."); yield break; }
                    // The device RenderTexture now owns the displayed picture. Keep
                    // no second full-resolution GPU image between slideshow frames.
                    signal.imageIndex = index;
                    signal.imageElapsed = 0;
                    signal.ready = true;
                    signal.loading = false;
                    signal.media = signal.pendingMedia;
                    signal.remote = true;
                    signal.currentVideo = signal.pendingVideo;
                    if (index == 0)
                    {
                        RememberVideo(signal, VideoKey(signal.currentVideo));
                        RequestPhotoAudio(signal);
                    }
                    ApplyPlayback();
                }
            }
            finally { if (photo) Destroy(photo); }
        }

        private IEnumerator LoadPhotoAudio(Channel signal, int generation, string url)
        {
            AudioClip clip = null;
            try
            {
                using (UnityWebRequest request = SlopCityMediaTransfer.Get(url, SlopCityMediaTransfer.MaxWaveBytes, 15))
                {
                    var transfer = SlopCityMediaTransfer.Send(request);
                    if (transfer == null) yield break;
                    while (!transfer.isDone)
                    {
                        if (generation != signal.photoGeneration || !signal.slideshow) { request.Abort(); yield break; }
                        yield return null;
                    }
                    if (generation != signal.photoGeneration || !signal.slideshow || !signal.audio
                        || request.result != UnityWebRequest.Result.Success) yield break;
                    byte[] bytes = null;
                    int channels = 0, frequency = 0, frames = 0, offset = 0;
                    try
                    {
                        bytes = request.downloadHandler.data;
                        using (var stream = new MemoryStream(bytes, false))
                            if (SlopCityMediaTransfer.WaveIsSafe(stream, out channels, out frequency, out frames, out offset))
                                clip = AudioClip.Create("City slideshow music", frames, channels, frequency, false);
                    }
                    catch (Exception error) { Debug.LogWarning("Slideshow audio unavailable: " + error.GetType().Name, this); }
                    if (!clip) yield break;
#if UNITY_WEBGL && !UNITY_EDITOR
                    const bool singleUpload = true;
#else
                    const bool singleUpload = false;
#endif
                    bool accepted = false;
                    Func<bool> current = () => generation == signal.photoGeneration && signal.slideshow && signal.audio;
                    yield return WritePhotoPcm(bytes, channels, frames, offset, singleUpload, current, (samples, frame) =>
                    {
                        try { return clip.SetData(samples, frame); }
                        catch (Exception error) { Debug.LogWarning("Slideshow audio unavailable: " + error.GetType().Name, this); return false; }
                    }, () => accepted = true);
                    if (!accepted || !current()) yield break;
                    if (signal.photoAudio) { signal.audio.Stop(); Destroy(signal.photoAudio); }
                    signal.photoAudio = clip; signal.audio.clip = clip; clip = null;
                    signal.audio.loop = false;
                    signal.audioStarted = false;
                    ApplyPlayback();
                }
            }
            finally
            {
                if (clip) Destroy(clip);
                if (generation == signal.photoGeneration) signal.audioLoading = false;
            }
        }

        private static IEnumerator WritePhotoPcm(byte[] bytes, int channels, int frames, int offset,
            bool singleUpload, Func<bool> current, Func<float[], int, bool> write, Action complete)
        {
            // WaveIsSafe bounds this allocation. Unity WebGL rebuilds its complete
            // browser AudioBuffer on EVERY SetData, so one bounded full PCM upload
            // avoids hundreds of full-track allocations. Native keeps small blocks.
            const int blockFrames = 8192;
            int blocksPerYield = singleUpload ? 4 : 16;
            if (!current()) yield break;
            var samples = new float[checked((singleUpload ? frames : Math.Min(blockFrames, frames)) * channels)];
            for (int frame = 0; frame < frames; frame += blockFrames)
            {
                if (!current()) yield break;
                int count = Math.Min(blockFrames, frames - frame) * channels;
                if (!singleUpload && samples.Length != count) samples = new float[count];
                int start = singleUpload ? frame * channels : 0;
                for (int sample = 0; sample < count; sample++)
                {
                    int at = offset + (frame * channels + sample) * 2;
                    samples[start + sample] = (short)(bytes[at] | (bytes[at + 1] << 8)) / 32768f;
                }
                if (!singleUpload && !write(samples, frame)) yield break;
                if ((frame / blockFrames) % blocksPerYield == blocksPerYield - 1 && frame + blockFrames < frames) yield return null;
            }
            if (!current() || (singleUpload && !write(samples, 0))) yield break;
            complete();
        }

        private void RequestPhotoAudio(Channel signal)
        {
            signal.resumeAudio = false;
            if (signal.audioLoading || signal.photoAudio || signal.currentVideo == null || !signal.slideshow
                || string.IsNullOrEmpty(signal.currentVideo.audioUrl) || !SlopCityTikTokSync.Instance) return;
            int generation = signal.photoGeneration;
            SlopCityTikTokVideo video = signal.currentVideo;
            signal.audioLoading = true;
            SlopCityTikTokSync.Instance.ResolveAudio(video, audioUrl =>
            {
                if (!this || !isActiveAndEnabled || generation != signal.photoGeneration || !signal.slideshow) return;
                if (!SafeAudioUrl(video, audioUrl)) { signal.audioLoading = false; return; }
                mediaCoroutines.Start(this, LoadPhotoAudio(signal, generation, audioUrl));
            }, () => this && isActiveAndEnabled && generation == signal.photoGeneration && signal.slideshow);
        }

        internal static void DrawImage(Texture photo, RenderTexture target)
        {
            if (!photo || !target || !target.IsCreated()) throw new InvalidOperationException("City display texture is unavailable.");
            RenderTexture previous = RenderTexture.active;
            bool pushed = false;
            try
            {
                RenderTexture.active = target;
                GL.Clear(true, true, Color.black);
                float fit = Mathf.Min((float)target.width / photo.width, (float)target.height / photo.height);
                float width = photo.width * fit, height = photo.height * fit;
                GL.PushMatrix(); pushed = true;
                GL.LoadPixelMatrix(0, target.width, target.height, 0);
                Graphics.DrawTexture(new Rect((target.width - width) * 0.5f, (target.height - height) * 0.5f, width, height), photo);
            }
            finally
            {
                try { if (pushed) GL.PopMatrix(); }
                finally { RenderTexture.active = previous; }
            }
        }

        private static void ReleasePhoto(Channel signal)
        {
            signal.photoGeneration++;
            AudioClip clip = signal.photoAudio;
            if (signal.audio && clip)
            {
                CleanupMedia(signal.audio.Stop);
                CleanupMedia(() => signal.audio.clip = null);
            }
            if (TryMediaCleanup(() => { if (clip) Destroy(clip); }) || !clip) signal.photoAudio = null;
            signal.slideshow = false;
            signal.resumeImage = false;
            signal.audioLoading = signal.resumeAudio = false;
            signal.audioStarted = false;
            signal.imageIndex = -1;
        }

        private void FailChannel(Channel signal, string error)
        {
            Debug.LogWarning("City media " + signal.fallback.id + ": " + error, this);
            CleanupMedia(() => StopPlayback(signal));
            CleanupMedia(() => ReleasePhoto(signal));
            signal.resolving = false;
            signal.operation++;
            if (signal.pendingVideo != null)
            {
                string key = VideoKey(signal.pendingVideo);
                retries.TryGetValue(key, out Retry retry);
                int failures = Mathf.Min(6, (retry?.failures ?? 0) + 1);
                retries[key] = new Retry { failures = failures, after = Time.unscaledTime + Mathf.Min(600f, 15f * Mathf.Pow(2, failures - 1)) };
                signal.pendingVideo = null;
                if (signal.owner != null) AssignNextPost(signal.owner);
                signal.advance = true;
                signal.nextAttempt = Time.unscaledTime + 0.2f;
            }
            else if (signal.pendingRemote)
            {
                if (signal.owner != null) channels[Mathf.Clamp(signal.owner.screen.Channel, 0, ChannelCount - 1)].legacy = null;
                signal.pendingRemote = false;
                signal.advance = true;
                signal.nextAttempt = Time.unscaledTime + 0.2f;
                feedMessage = "Online signal unavailable / bundled clips. Creator feeds open in your browser.";
            }
            else
            {
                signal.failed = true;
                signal.advance = true;
                signal.nextAttempt = Time.unscaledTime + 30f;
            }
        }

        private void ApplyPlayback()
        {
            // Suspension is authoritative, rather than a transient gaze loss.
            // Never carry a confirmed/pending city claim across pause or focus loss.
            if (Suspended || manuallyPaused) RefreshMobileLoungePriority(false, true);
            foreach (Channel signal in channels)
            {
                if (signal == null) continue;
                if (signal.slideshow)
                {
                    if (!signal.audio || !signal.photoAudio) continue;
                    if (!Suspended && !manuallyPaused && signal.hasScreen && !signal.advance)
                    {
                        if (!signal.audioStarted) { signal.audio.Play(); signal.audioStarted = true; }
                        else signal.audio.UnPause();
                    }
                    else signal.audio.Pause();
                    continue;
                }
                if (!signal.player || !signal.ready || signal.advance || signal.resolving) continue;
                if (!Suspended && !manuallyPaused && signal.hasScreen && !signal.featuredHolding)
                {
                    if (!signal.player.isPlaying) signal.player.Play();
                    if (signal.owner != null && signal.owner.screen.IsFeatured && signal.audio) signal.audio.UnPause();
                }
                else signal.player.Pause();
            }
        }

        public void RefreshFeed()
        {
            if (!initialized || refreshing || !isActiveAndEnabled) return;
            if (string.IsNullOrEmpty(feedUrl))
            {
                feedMessage = "Bundled clips. Current TikTok posts are in the browser creator feeds.";
                return;
            }
            mediaCoroutines.Start(this, FetchFeed());
        }

        private IEnumerator FetchFeed()
        {
            refreshing = true;
            nextRefresh = Time.unscaledTime + refreshSeconds;
            try
            {
                using (UnityWebRequest request = SlopCityMediaTransfer.Get(feedUrl, 131072, 12))
                {
                    var operation = SlopCityMediaTransfer.Send(request);
                    if (operation == null) { feedUnavailable = true; yield break; }
                    yield return operation;
                    if (request.result != UnityWebRequest.Result.Success || request.downloadedBytes > 131072)
                    {
                        feedUnavailable = true;
                        feedMessage = "Feed unavailable / bundled clips. Creator feeds open in your browser.";
                    }
                    else
                    {
                        Feed result = null;
                        try { result = JsonUtility.FromJson<Feed>(request.downloadHandler.text); }
                        catch (Exception exception) { Debug.LogWarning("City feed: " + exception.Message, this); }
                        int accepted = 0;
                        if (result != null && result.version == 1)
                        {
                            foreach (Channel signal in channels)
                            {
                                if (signal == null) continue;
                                Media update = FindMedia(result, signal.fallback.id);
                                if (update == null || !IsDirectMedia(update.mediaUrl)) continue;
                                accepted++;
                                if (signal.legacy != null && signal.legacy.mediaUrl == update.mediaUrl) continue;
                                signal.legacy = update;
                                signal.failed = false;
                                if (!signal.ready && !signal.loading && !signal.resolving) signal.advance = true;
                            }
                        }
                        feedUnavailable = accepted == 0;
                        feedMessage = accepted > 0 ? "Configured online media feed synchronized."
                            : "Feed contains no playable media / bundled clips.";
                    }
                }
            }
            finally { refreshing = false; }
        }

        /// <summary>Explicit user action only: opens the creator's current public TikTok profile.</summary>
        public void OpenCreatorFeed(int profileIndex)
        {
            int selected = Mathf.Clamp(profileIndex, 0, Profiles.Length - 1);
            Application.OpenURL("https://www.tiktok.com/@" + Profiles[selected]);
        }

        /// <summary>Official TikTok creator embeds use a browser, where their player is supported.</summary>
        public void OpenCreatorPortal()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Media", "creator-feeds.html");
#if UNITY_WEBGL && !UNITY_EDITOR
            Application.OpenURL(Application.streamingAssetsPath.TrimEnd('/') + "/Media/creator-feeds.html");
#else
            if (File.Exists(path)) Application.OpenURL(new Uri(Path.GetFullPath(path)).AbsoluteUri);
            else OpenCreatorFeed(0);
#endif
        }

        private static bool IsHttps(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps
                && string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrEmpty(uri.Host);
        }

        private static bool IsDirectMedia(string value)
        {
            if (!IsHttps(value)) return false;
            Uri uri = new Uri(value);
            // A TikTok profile/post page is HTML, not a native decoder source.
            return !uri.Host.Equals("tiktok.com", StringComparison.OrdinalIgnoreCase)
                && !uri.Host.EndsWith(".tiktok.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ValidCatalogVideo(SlopCityTikTokVideo video)
        {
            return video != null && (video.creator == "1984drh" || video.creator == "machine_publishers")
                && SlopCityTikTokParser.ValidateVideo(video, video.creator);
        }

        private static string VideoKey(SlopCityTikTokVideo video) => video.creator + "/" + video.id;

        private static string CachedUrl(SlopCityTikTokVideo video, string localFile)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return null;
#else
            if (string.IsNullOrEmpty(Application.persistentDataPath) || !SlopCityTikTokParser.IsSafeLocalFile(localFile, video.id)) return null;
            string path = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "TikTok", "media", localFile));
            if (!File.Exists(path)) return null;
            // The sync cache prunes by last access; directly reused clips must count as active reads too.
            try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return new Uri(path).AbsoluteUri;
#endif
        }

        private static bool SafeResolvedUrl(SlopCityTikTokVideo video, string value)
        {
            if (SlopCityTikTokSync.IsBundledMediaUri(video.bundledVideoFile, value)) return true;
            if (SlopCityTikTokParser.IsMediaUrl(value)) return true;
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || !uri.IsFile || !string.IsNullOrEmpty(uri.Host)) return false;
            string expected = CachedUrl(video, video.id + ".mp4");
            return !string.IsNullOrEmpty(expected) && string.Equals(expected, uri.AbsoluteUri, StringComparison.Ordinal);
        }

        private static bool SafePhotoUrl(SlopCityTikTokVideo video, int index, string value)
            => (video.bundledImageFiles != null && index >= 0 && index < video.bundledImageFiles.Length
                && SlopCityTikTokSync.IsBundledMediaUri(video.bundledImageFiles[index], value))
                || SafeCachedUri(video.id + "_" + index + ".jpg", value);

        private static bool SafeAudioUrl(SlopCityTikTokVideo video, string value)
            => SlopCityTikTokSync.IsBundledMediaUri(video.bundledAudioFile, value) || SafeCachedUri(video.id + ".wav", value);

        private static bool SafeCachedUri(string basename, string value)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || !uri.IsFile || !string.IsNullOrEmpty(uri.Host)) return false;
            if (string.IsNullOrEmpty(Application.persistentDataPath)) return false;
            string path = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "TikTok", "media", basename));
            return File.Exists(path) && string.Equals(new Uri(path).AbsoluteUri, uri.AbsoluteUri, StringComparison.Ordinal);
#endif
        }

        private void ReadProgress()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            try
            {
                string path = ProgressPath();
                if (File.Exists(path) && new FileInfo(path).Length <= 131072)
                    progress = JsonUtility.FromJson<PlaybackProgress>(File.ReadAllText(path));
            }
            catch (Exception exception) { Debug.LogWarning("City playlist progress: " + exception.Message, this); }
            if (progress == null || progress.lastVideoKeys == null || progress.lastVideoKeys.Length != ChannelCount)
                progress = new PlaybackProgress();
#endif
        }

        private void SaveProgress()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            try
            {
                string path = ProgressPath();
                if (string.IsNullOrEmpty(path)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path + ".tmp", JsonUtility.ToJson(progress));
                if (File.Exists(path)) File.Replace(path + ".tmp", path, null);
                else File.Move(path + ".tmp", path);
            }
            catch (Exception exception) { Debug.LogWarning("City playlist progress: " + exception.Message, this); }
#endif
        }

        private static string ProgressPath() => string.IsNullOrEmpty(Application.persistentDataPath) ? ""
            : Path.Combine(Application.persistentDataPath, "TikTok", "playback.json");

        private void RememberVideo(Channel signal, string key)
        {
            retries.Remove(key);
            progress.lastVideoKeys[signal.index] = key;
            SaveProgress();
        }

        private static Media FindMedia(Feed feed, string id)
        {
            if (feed == null || feed.channels == null) return null;
            foreach (Media media in feed.channels)
                if (media != null && media.id == id && !string.IsNullOrWhiteSpace(media.title)) return media;
            return null;
        }

        private static Media BundledMedia(int channel)
        {
            switch (channel)
            {
                case 1: return new Media { id = "record", title = "In-Q-Tel / 1984DRH & Hood Rich Records", creator = "machine_publishers", localFile = "in-q-tel.mp4", sourceUrl = "https://www.tiktok.com/@machine_publishers/video/7573075941463756062" };
                // The original source URL records the historical account name; TikTok now attributes this post to 1984drh.
                case 2: return new Media { id = "duet", title = "1984DRH / In-Q-Tel duet", creator = "1984drh", localFile = "duet.mp4", sourceUrl = "https://www.tiktok.com/@beef_talko/video/7689137720760864030" };
                default: return new Media { id = "broadcast", title = "Machine Publishers broadcast", creator = "machine_publishers", localFile = "broadcast.mp4", sourceUrl = "https://www.tiktok.com/@machine_publishers/video/7694500222541597983" };
            }
        }

        private static void Clear(RenderTexture texture)
        {
            if (!texture || !texture.IsCreated()) return;
            RenderTexture previous = RenderTexture.active;
            try { RenderTexture.active = texture; GL.Clear(true, true, Color.black); }
            finally { RenderTexture.active = previous; }
        }

        private static void StopPlayback(Channel signal)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            signal.webAwaitingFirstPicture = false;
#endif
            VideoPlayer player = signal.player;
            if (player)
            {
                CleanupMedia(() => player.prepareCompleted -= signal.preparedHandler);
                CleanupMedia(() => player.errorReceived -= signal.errorHandler);
                CleanupMedia(() => player.loopPointReached -= signal.endedHandler);
#if UNITY_WEBGL && !UNITY_EDITOR
                if (signal.webFrameReadyHandler != null)
                    CleanupMedia(() => player.frameReady -= signal.webFrameReadyHandler);
                CleanupMedia(() => player.sendFrameReadyEvents = false);
#endif
                // Native failures must not strand audio or leave stale preparation
                // state armed. Every cleanup has its own failure boundary.
                CleanupMedia(() => player.targetTexture = null);
                CleanupMedia(player.Stop);
            }
            AudioSource audio = signal.audio;
            if (audio)
            {
                CleanupMedia(audio.Stop);
                CleanupMedia(() => audio.volume = 0f);
            }
            signal.ready = false;
            signal.loading = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            signal.webFrameReadyHandler = null;
            signal.webPrepareSuspendedAt = -1f;
#endif
            signal.featuredHolding = signal.featuredHeldThisLoop = false;
            signal.featuredHoldRemaining = 0f;
        }

        private static void ReleasePlayback(Channel signal)
        {
            signal.playbackConfigured = false;
            CleanupMedia(() => StopPlayback(signal));
            GameObject session = signal.session;
            bool retired = TryMediaCleanup(() => { if (session) Destroy(session); });
            // A refused Destroy still occupies its exact channel. Keep ownership
            // until a later release succeeds instead of admitting another decoder.
            if (!retired && session) return;
            signal.player = null;
            signal.audio = null;
            signal.session = null;
            signal.ready = false;
            signal.loading = false;
        }

        private void OnApplicationFocus(bool focused)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClocks();
#endif
            focusSuspended = !focused;
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClocks();
#endif
            featuredTickAt = Time.unscaledTime;
            ApplyPlayback();
        }

        private void OnApplicationPause(bool suspended)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClocks();
#endif
            applicationSuspended = suspended;
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClocks();
#endif
            featuredTickAt = Time.unscaledTime;
            ApplyPlayback();
        }

        private void OnEnable()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClocks();
#endif
            if (initialized) SlopCityCityScreen.RegisterActiveScreens(this);
            featuredTickAt = Time.unscaledTime;
            ApplyPlayback();
        }
        private void OnDisable()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClocks();
#endif
            CleanupMedia(() => RefreshMobileLoungePriority(false, true));
            CleanupMedia(ApplyPlayback);
            CleanupMedia(UpdateAudio);
            CleanupMedia(() => SlopCityMediaAudioFocus.Remove(this));
            foreach (Channel signal in channels)
                if (signal != null && signal.slideshow && signal.audioLoading)
                {
                    signal.photoGeneration++;
                    signal.audioLoading = false;
                    signal.resumeAudio = true;
                }
            CleanupMedia(StopAllCoroutines);
            CleanupMedia(mediaCoroutines.DisposeAll);
            refreshing = false;
            thumbnailRequests = 0;
            foreach (ScreenState display in displays)
            {
                display.generation++;
                display.needsThumbnail = display.screen && !display.screen.AudioOnly && display.post != null && (display.slot == null || !display.slot.ready);
            }
            foreach (Channel signal in channels)
            {
                if (signal == null) continue;
                if (signal.slideshow && (signal.loading || signal.resolving))
                {
                    signal.loading = false;
                    signal.resolving = false;
                    signal.operation++;
                    signal.resumeImage = true;
                    continue;
                }
                if (!signal.resolving) continue;
                signal.resolving = false;
                signal.operation++;
                signal.advance = true;
            }
        }

        private void OnDestroy()
        {
            CleanupMedia(() => RefreshMobileLoungePriority(false, true));
            CleanupMedia(StopAllCoroutines);
            CleanupMedia(mediaCoroutines.DisposeAll);
            CleanupMedia(() => SlopCityMediaAudioFocus.Remove(this));
            if (Instance == this) Instance = null;
            foreach (SlopCityCityScreen screen in screens)
                if (screen) CleanupMedia(screen.Restore);
            foreach (Channel signal in channels)
            {
                if (signal == null) continue;
                CleanupMedia(() => ReleasePhoto(signal));
                CleanupMedia(() => ReleasePlayback(signal));
            }
            foreach (ScreenState display in displays)
            {
                ReleaseScreenResources(display);
            }
            RetryRetiredScreenResources();
            screens.Clear();
            displays.Clear();
            displayLookup.Clear();
            candidates.Clear();
        }

        private static bool TryMediaCleanup(Action cleanup)
        {
            try { cleanup(); return true; }
            catch (Exception error)
            {
                Debug.LogWarning("City media cleanup failed: " + error.GetType().Name);
                return false;
            }
        }

        private static void CleanupMedia(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception error) { Debug.LogWarning("City media cleanup failed: " + error.Message); }
        }

        [Serializable] private sealed class Feed
        {
            public int version = 1;
            public string feedUrl;
            public float refreshSeconds = 300f;
            public Media[] channels;
        }

        [Serializable] private sealed class Media
        {
            public string id;
            public string title;
            public string creator;
            public string sourceUrl;
            public string localFile;
            public string mediaUrl;
        }

        private sealed class ScreenState
        {
            public SlopCityCityScreen screen;
            public int ordinal, generation;
            public Media fallback;
            public SlopCityTikTokVideo post;
            public List<SlopCityTikTokVideo> program;
            public int programChannel = -1, programIndex = -1;
            public RenderTexture texture;
            public Material material;
            public Channel slot;
            public float audioScore, audioPriority, priority;
            public bool aimed;
            public bool needsThumbnail, paused;
            public string pausedVideoKey;
            public double pausedVideoTime;
            public int pausedVideoGeneration = -1;
        }

        private sealed class Channel
        {
            public ScreenState owner;
            public int index, operation, photoGeneration, requestedImageIndex, imageIndex = -1;
            public Media fallback, media, legacy, pendingMedia;
            public SlopCityTikTokVideo currentVideo, pendingVideo;
            public List<SlopCityTikTokVideo> playlist = new List<SlopCityTikTokVideo>();
            public string lastSelectedKey;
            public GameObject session;
            public VideoPlayer player;
            public AudioSource audio;
            public RenderTexture texture;
            public AudioClip photoAudio;
            public Material material;
            public VideoPlayer.EventHandler preparedHandler, endedHandler;
            public VideoPlayer.ErrorEventHandler errorHandler;
            public bool remote, ready, loading, failed, hasScreen, pendingRemote, resolving, advance, slideshow, audioStarted, resumeImage, playbackConfigured;
            public bool audioLoading, resumeAudio;
            public float startedAt, nextAttempt, imageElapsed;
#if UNITY_WEBGL && !UNITY_EDITOR
            [NonSerialized] public float webPrepareSuspendedAt = -1f;
            [NonSerialized] public bool webAwaitingFirstPicture;
            [NonSerialized] public VideoPlayer.FrameReadyEventHandler webFrameReadyHandler;
#endif
            public bool featuredHolding, featuredHeldThisLoop;
            public float featuredHoldRemaining;
        }

        private sealed class Retry { public int failures; public float after; }

        [Serializable] private sealed class PlaybackProgress
        {
            public string[] lastVideoKeys = new string[ChannelCount];
        }
    }
}
