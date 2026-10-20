using System;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace SlopCity
{
    public enum SlopCityMediaChannel { Broadcast, Record, Duet }

    /// <summary>One local video signal, shared by the lounge television and media controls.</summary>
    public sealed class SlopCityMediaPlayer : MonoBehaviour
    {
        [SerializeField] private Renderer screen;
        [SerializeField] private Renderer[] screenLettering;
        [SerializeField] private Transform television;
        [SerializeField] private Transform records;
        [SerializeField] private Shader screenShader;
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

        private const float PrepareTimeoutSeconds = 25f;
        private enum PlaybackState { Idle, Loading, Ready, Ended, Failed }
        private PlaybackState state;
        private SlopCityMediaChannel selectedChannel;
        private VideoPlayer player;
        private AudioSource audioSource;
        private GameObject playbackObject;
        private bool releasingPlayback;
        private RenderTexture screenTexture;
        private Material screenMaterial;
        private Material failedDisplayMaterial;
        private RenderTexture failedDisplayTexture;
        private Material originalScreenMaterial;
        private bool originalScreenEnabled;
        private bool[] originalLetteringEnabled;
        private bool capturedDisplay;
        private bool displayVisible;
        private bool wantsPlayback;
        private bool menuSuspended;
        private bool focusSuspended;
        private bool applicationSuspended;
        private float prepareStartedAt;
        private string failure;
        private Camera audioCamera;
        private float nextAudioUpdate, audioScore;
#if UNITY_WEBGL && !UNITY_EDITOR
        private readonly Plane[] webFrustum = new Plane[6];
        private Camera webCamera;
        private float nextWebMediaUpdate;
        private bool webRelevant = true;
        private bool webDecoderRetired;
        private bool webCityPriority;
        private bool webAwaitingAdmission;
        private float webDecoderAdmitAt;
        private float webIrrelevantSince = -1f;
        private float webPrepareSuspendedAt = -1f;
        private bool webAwaitingFirstPicture, webHasPicture, webPumpingPicture;
        private int webPublishedFrameSerial, webCopiedFrameSerial, webPicturePumpFrame = -1;
        private long webPublishedFrameIndex = long.MinValue;
        private int webPlaybackGeneration;
        private VideoPlayer.FrameReadyEventHandler webFrameReadyHandler;
        private static SlopCityMediaPlayer mobileDecoderOwner;
        private bool MobileWebBudget
        {
            get
            {
                if (!webCamera) webCamera = Camera.main;
                SlopCityPlayerInput input = webCamera ? webCamera.GetComponentInParent<SlopCityPlayerInput>() : null;
                return input && input.UseTouchControls;
            }
        }
#endif
        public static bool MobileDecoderInUse
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return mobileDecoderOwner && (mobileDecoderOwner.player || mobileDecoderOwner.webAwaitingAdmission);
#else
                return false;
#endif
            }
        }

        public Texture ScreenTexture => displayVisible ? screenTexture : null;
        public bool IsPlaying => state == PlaybackState.Ready && player && player.isPlaying;
        public bool IsLoading => state == PlaybackState.Loading;
        public bool HasSelection => state != PlaybackState.Idle;
        public bool IsPaused => (state == PlaybackState.Ready || state == PlaybackState.Loading) && !wantsPlayback;
        public float Volume => volume;
        public SlopCityMediaChannel SelectedChannel => selectedChannel;
        public double TimeSeconds => player && player.isPrepared ? FiniteSeconds(player.time) : 0d;
        public double DurationSeconds => player && player.isPrepared ? FiniteSeconds(player.length) : 0d;
        private bool IsSuspended => menuSuspended || focusSuspended || applicationSuspended
#if UNITY_WEBGL && !UNITY_EDITOR
            || !webRelevant || webCityPriority
#endif
            ;

        public string Title
        {
            get
            {
                if (!HasSelection) return "LOUNGE MEDIA";
                switch (selectedChannel)
                {
                    case SlopCityMediaChannel.Record: return "IN-Q-TEL";
                    case SlopCityMediaChannel.Duet: return "1984DRH DUET";
                    default: return "MACHINE PUBLISHERS";
                }
            }
        }

        public string Status
        {
            get
            {
                switch (state)
                {
                    case PlaybackState.Loading: return wantsPlayback ? "Loading the local clip..." : "Loading the local clip / playback paused.";
                    case PlaybackState.Failed: return failure;
                    case PlaybackState.Ended: return "Finished. Press play to watch again.";
                    case PlaybackState.Ready:
                        if (!wantsPlayback) return "Paused.";
#if UNITY_WEBGL && !UNITY_EDITOR
                        if (!webRelevant) return "TV resumes when you return or open media controls.";
#endif
                        if (IsSuspended) return "Paused while the lounge is suspended.";
                        return selectedChannel == SlopCityMediaChannel.Record
                            ? "Playing on the TV / sound from the record corner."
                            : "Playing on the lounge TV.";
                    default: return "Choose a channel to play in the lounge.";
                }
            }
        }

        public void Configure(Renderer screen, Renderer[] screenLettering, Transform television,
            Transform records, Shader screenShader)
        {
            Stop();
            if (playbackObject || player || audioSource) return;
            ReleaseDisplay();
            if (screenTexture || screenMaterial) return;
            this.screen = screen;
            this.screenLettering = screenLettering;
            this.television = television;
            this.records = records;
            this.screenShader = screenShader;
        }

        public void Play(SlopCityMediaChannel channel)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (MobileWebBudget && player) webDecoderAdmitAt = Time.unscaledTime + .25f;
#endif
            Stop();
#if UNITY_WEBGL && !UNITY_EDITOR
            if (MobileWebBudget && webHasPicture && screenTexture) SetDisplayVisible(true);
#endif
            selectedChannel = channel;
            state = PlaybackState.Loading;
            wantsPlayback = true;
            prepareStartedAt = Time.unscaledTime;
#if UNITY_WEBGL && !UNITY_EDITOR
            nextWebMediaUpdate = 0f;
            webDecoderRetired = false;
            webIrrelevantSince = -1f;
            if (MobileWebBudget && webCityPriority)
            {
                // Channel controls retain their selection while direct city aim wins.
                webDecoderRetired = true;
                state = PlaybackState.Ready;
                if (webHasPicture && screenTexture) SetDisplayVisible(true);
                return;
            }
            if (MobileWebBudget)
            {
                if (SlopCityCityMedia.Instance && SlopCityCityMedia.Instance.ReleaseMobileDecodersForLounge())
                    webDecoderAdmitAt = Time.unscaledTime + .25f;
                mobileDecoderOwner = this;
                if (Time.unscaledTime < webDecoderAdmitAt)
                {
                    // City Destroy completes at end of frame. Reserve its sole slot
                    // while waiting, without constructing a second native decoder.
                    webAwaitingAdmission = true;
                    return;
                }
            }
#endif
            BeginPlayback(channel);
        }

        private void BeginPlayback(SlopCityMediaChannel channel)
        {
            // A rejected native Destroy remains owned. Never overwrite that
            // session on a retry or allocate a second decoder beside it.
            if (playbackObject || player || audioSource)
            {
                wantsPlayback = false;
                state = PlaybackState.Failed;
                failure = "The previous clip could not retire. Choose its channel to retry.";
                return;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            if (MobileWebBudget && SlopCityCityMedia.Instance && SlopCityCityMedia.Instance.ReleaseMobileDecodersForLounge())
            {
                // A previously refused city Destroy may still own the one phone
                // decoder. Retry it and allow accepted destruction to finish.
                webAwaitingAdmission = true;
                webDecoderAdmitAt = Time.unscaledTime + .25f;
                return;
            }
#endif
            prepareStartedAt = Time.unscaledTime;
#if UNITY_WEBGL && !UNITY_EDITOR
            webPrepareSuspendedAt = -1f;
            RefreshMobilePreparationClock();
#endif
            string filename;
            switch (channel)
            {
                case SlopCityMediaChannel.Broadcast: filename = "broadcast.mp4"; break;
                case SlopCityMediaChannel.Record: filename = "in-q-tel.mp4"; break;
                case SlopCityMediaChannel.Duet: filename = "duet.mp4"; break;
                default: Fail("This media channel is unavailable."); return;
            }

            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "Media", filename);
#if (!UNITY_ANDROID && !UNITY_WEBGL) || UNITY_EDITOR
                if (!File.Exists(path))
                {
                    Fail("This clip is missing from the game. Rebuild with the bundled media.");
                    return;
                }
#endif
                if (!EnsureDisplay()) return;
#if UNITY_WEBGL && !UNITY_EDITOR
                if (!MobileWebBudget)
#endif
                ClearScreenTexture();

                // A fresh session prevents late decoder callbacks from a previous channel
                // from changing the current selection. The old session is stopped first.
                playbackObject = new GameObject("LoungeMedia_" + channel);
                playbackObject.transform.SetParent(transform, false);
                Transform emitter = channel == SlopCityMediaChannel.Record ? records : television;
                if (emitter) playbackObject.transform.position = emitter.position;
                audioSource = playbackObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
                audioSource.rolloffMode = AudioRolloffMode.Custom;
                audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(0f, 1f, 1f, 1f));
                audioSource.minDistance = 2f;
                audioSource.maxDistance = 18f;
                audioSource.dopplerLevel = 0f;
                audioSource.volume = 0f;

                player = playbackObject.AddComponent<VideoPlayer>();
                player.playOnAwake = false;
                player.source = VideoSource.Url;
#if UNITY_WEBGL && !UNITY_EDITOR
                if (MobileWebBudget)
                {
                    // Keep the same decoder texture for the entire clip. Copy each
                    // advancing frame into the retained physical picture; metadata
                    // alone must never replace it with a black or frozen first frame.
                    player.renderMode = VideoRenderMode.APIOnly;
                    webAwaitingFirstPicture = true;
                    int generation = webPlaybackGeneration;
                    VideoPlayer expected = player;
                    webFrameReadyHandler = (source, frame) =>
                    {
                        if (!this || !isActiveAndEnabled || !source || source != expected || source != player
                            || generation != webPlaybackGeneration || releasingPlayback) return;
                        // The independent Update pump reads the texture. This
                        // callback must remain safe inside that native getter.
                        if (webPublishedFrameIndex == frame) return;
                        webPublishedFrameIndex = frame;
                        webPublishedFrameSerial++;
                    };
                    player.frameReady += webFrameReadyHandler;
                    player.sendFrameReadyEvents = true;
                }
                else
#endif
                {
                    player.renderMode = VideoRenderMode.RenderTexture;
                    player.targetTexture = screenTexture;
                }
                player.aspectRatio = VideoAspectRatio.FitInside;
                player.isLooping = true;
                player.waitForFirstFrame = true;
                player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
#if UNITY_WEBGL && !UNITY_EDITOR
                // Web video bypasses AudioSource spatialization and volume controls.
                player.audioOutputMode = VideoAudioOutputMode.Direct;
#else
                player.audioOutputMode = VideoAudioOutputMode.AudioSource;
#endif
                player.controlledAudioTrackCount = 1;
                player.EnableAudioTrack(0, true);
#if !UNITY_WEBGL || UNITY_EDITOR
                player.SetTargetAudioSource(0, audioSource);
#endif
                player.prepareCompleted += OnPrepared;
                player.errorReceived += OnError;
                player.loopPointReached += OnEnded;
#if UNITY_WEBGL && !UNITY_EDITOR
                player.url = Application.streamingAssetsPath.TrimEnd('/') + "/Media/" + Uri.EscapeDataString(filename);
#elif UNITY_ANDROID && !UNITY_EDITOR
                player.url = path.Replace('\\', '/');
#else
                player.url = new Uri(Path.GetFullPath(path)).AbsoluteUri;
#endif
                player.Prepare();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("SLOP CITY: Could not open " + filename + ": " + exception.Message, this);
                Fail("The clip could not be opened. Choose its channel to try again.");
            }
        }

        public void TogglePause()
        {
            if (!HasSelection) return;
            if (state == PlaybackState.Failed || state == PlaybackState.Ended)
            {
                Play(selectedChannel);
                return;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClock();
#endif
            wantsPlayback = !wantsPlayback;
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClock();
            if (!wantsPlayback) CancelPendingAdmission();
#endif
            ApplyPlaybackIntent();
        }

        public void Stop()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            webDecoderRetired = false;
            webAwaitingAdmission = false;
            webIrrelevantSince = -1f;
#endif
            wantsPlayback = false;
            state = PlaybackState.Idle;
            failure = null;
            audioScore = 0f;
            TryMediaCleanup(() => SlopCityMediaAudioFocus.Remove(this));
            ReleasePlayback();
            TryMediaCleanup(() => SetDisplayVisible(false));
        }

        public void SetSuspended(bool suspended)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClock();
#endif
            menuSuspended = suspended;
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClock();
            if (suspended) CancelPendingAdmission();
            RefreshWebInterest();
#endif
            ApplyPlaybackIntent();
        }

        public void SetVolume(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            volume = Mathf.Clamp01(value);
            ApplyVolume();
        }

        public void SetMobileCityPriority(bool prioritized)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!MobileWebBudget || webCityPriority == prioritized) return;
            webCityPriority = prioritized;
            if (prioritized && HasSelection && (player || webAwaitingAdmission))
            {
                // Direct city aim owns the phone's sole decoder, even beside home.
                // Retain the selected TV channel and its last physical-screen image.
                ReleasePlayback();
                webAwaitingAdmission = false;
                SlopCityMediaAudioFocus.Remove(this);
                webDecoderRetired = true;
                webIrrelevantSince = -1f;
                state = PlaybackState.Ready;
            }
#endif
        }

        private void ApplyVolume()
        {
            bool audible = wantsPlayback && !IsSuspended && state == PlaybackState.Ready && player && player.isPlaying;
            SlopCityMediaAudioFocus.Submit(this, audible ? audioScore : 0f, volume);
            float gain = audible ? SlopCityMediaAudioFocus.Gain(this) : 0f;
            if (audioSource) audioSource.volume = gain;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (player && player.isPrepared && player.audioTrackCount > 0)
            {
                player.SetDirectAudioVolume(0, gain);
                player.SetDirectAudioMute(0, gain <= 0f);
            }
#endif
        }

        private void RefreshAudioInterest()
        {
            audioScore = 0f;
            if (!audioCamera) audioCamera = Camera.main;
            // Spatial eligibility is independent of preparation/playback state.
            // Actual output remains gated by Ready/isPlaying in ApplyVolume.
            if (!audioCamera || !wantsPlayback || menuSuspended || focusSuspended || applicationSuspended) return;
#if !UNITY_WEBGL || UNITY_EDITOR
            if (!audioSource) return;
#endif
            Transform emitter = selectedChannel == SlopCityMediaChannel.Record ? records : television;
            if (!emitter) return;
            Transform listener = audioCamera.transform;
            Vector3 centre = selectedChannel == SlopCityMediaChannel.Record || !screen
                ? emitter.position : screen.bounds.center;
            Vector3 toListener = listener.position - centre;
            float distance = toListener.magnitude;
            // Eligibility must remain measurable after an irrelevant decoder retires.
            float minDistance = audioSource ? audioSource.minDistance : 2f;
            float maxDistance = audioSource ? audioSource.maxDistance : 18f;
            if (distance >= maxDistance) return;
            Vector3 direction = distance > .001f ? toListener / distance : -listener.forward;
            float facing = selectedChannel == SlopCityMediaChannel.Record || !screen ? 1f
                : Vector3.Dot(screen.transform.TransformDirection(Vector3.back), direction);
            float gaze = Vector3.Dot(listener.forward, -direction);
            float range = 1f - Mathf.InverseLerp(minDistance, maxDistance, distance);
            // A playing device is still audible across the lounge when we turn our
            // head. Aim extends its useful range; it must not be a compulsory gate.
            float nearby = .85f * (1f - Mathf.InverseLerp(minDistance, 8f, distance));
            float aimed = range * Mathf.InverseLerp(.15f, .8f, facing) * Mathf.InverseLerp(.25f, .85f, gaze);
            float score = Mathf.Max(nearby, aimed);
            if (score <= .001f) return;
            if (MediaPathOccluded(listener, -direction, distance, emitter, selectedChannel != SlopCityMediaChannel.Record, out bool upholsteredObstruction)) return;
            audioScore = score * (upholsteredObstruction ? .85f : 1f);
        }

        private readonly RaycastHit[] mediaPathHits = new RaycastHit[64];

        private bool MediaPathOccluded(Transform listener, Vector3 direction, float distance, Transform emitter, bool televisionPicture, out bool upholsteredObstruction)
        {
            upholsteredObstruction = false;
            SlopCityFirstPerson controller = listener.GetComponentInParent<SlopCityFirstPerson>();
            int count = Physics.RaycastNonAlloc(listener.position, direction, mediaPathHits, Mathf.Max(0f, distance - .15f),
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == mediaPathHits.Length) return true;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = mediaPathHits[index];
                Transform obstacle = hit.transform;
                if (!obstacle || (screen && (obstacle == screen.transform || obstacle.IsChildOf(screen.transform)))
                    || (emitter && (obstacle == emitter || obstacle.IsChildOf(emitter)))
                    || (controller && (obstacle == controller.transform || obstacle.IsChildOf(controller.transform)))) continue;
                // Imported lounge props are siblings of the interaction markers.
                // The couch blanket's collision bounds cross the record-speaker ray:
                // upholstery softens sound; it cannot make a nearby speaker silent.
                if (emitter && emitter.parent && obstacle.IsChildOf(emitter.parent))
                {
                    if (obstacle.name.StartsWith("SC_Couch", StringComparison.Ordinal))
                    { upholsteredObstruction = true; continue; }
                    if (televisionPicture
                        && obstacle.name.StartsWith("Broadcast_TV_", StringComparison.Ordinal)) continue;
                }
                return true;
            }
            return false;
        }

        private void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClock();
#endif
            if (Time.unscaledTime >= nextAudioUpdate)
            {
                nextAudioUpdate = Time.unscaledTime + .2f;
                RefreshAudioInterest();
            }
            ApplyVolume();
            if (IsLoading
#if UNITY_WEBGL && !UNITY_EDITOR
                && !webAwaitingAdmission
                && !(MobileWebBudget && MobilePreparationSuspended)
#endif
                && Time.unscaledTime - prepareStartedAt >= PrepareTimeoutSeconds)
                Fail("The clip took too long to load. Choose its channel to try again.");
#if UNITY_WEBGL && !UNITY_EDITOR
            if (Time.unscaledTime >= nextWebMediaUpdate)
            {
                nextWebMediaUpdate = Time.unscaledTime + 0.2f;
                RefreshWebInterest();
                RefreshMobileDecoderOwnership();
                ApplyPlaybackIntent();
            }
            PumpWebVideoPicture();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void PumpWebVideoPicture()
        {
            if (!MobileWebBudget || !wantsPlayback || IsSuspended || state != PlaybackState.Ready
                || !player || releasingPlayback || webFrameReadyHandler == null || webPumpingPicture
                || webPicturePumpFrame == Time.frameCount) return;
            VideoPlayer source = player;
            RenderTexture target = screenTexture;
            int generation = webPlaybackGeneration;
            webPicturePumpFrame = Time.frameCount;
            webPumpingPicture = true;
            try
            {
                // Drive APIOnly texture publication before waiting for the event
                // it produces. Preserve the poster until a real frame was stamped.
                Texture picture = source.texture;
                int publication = webPublishedFrameSerial;
                if (!this || !isActiveAndEnabled || generation != webPlaybackGeneration || source != player
                    || releasingPlayback || publication == webCopiedFrameSerial || !picture || picture.width <= 0
                    || picture.height <= 0 || !target || target != screenTexture || !target.IsCreated()) return;
                SlopCityCityMedia.DrawImage(picture, target);
                webCopiedFrameSerial = publication;
                webAwaitingFirstPicture = false;
                webHasPicture = true;
                SetDisplayVisible(true);
            }
            catch (Exception error)
            {
                if (generation == webPlaybackGeneration && source == player)
                {
                    Debug.LogWarning("SLOP CITY: Video picture: " + error.Message, this);
                    Fail("The clip could not display its picture. Choose its channel to try again.");
                }
            }
            finally { webPumpingPicture = false; }
        }

        private bool MobilePreparationSuspended => !wantsPlayback || menuSuspended || focusSuspended || applicationSuspended;

        private void RefreshMobilePreparationClock()
        {
            if (!MobileWebBudget || !IsLoading || webAwaitingAdmission) return;
            float now = Time.unscaledTime;
            if (MobilePreparationSuspended)
            {
                if (webPrepareSuspendedAt < 0f) webPrepareSuspendedAt = now;
            }
            else if (webPrepareSuspendedAt >= 0f)
            {
                // A callback can begin preparation while already suspended. Only
                // exclude the part of that suspension overlapping this attempt.
                prepareStartedAt += Mathf.Max(0f, now - Mathf.Max(prepareStartedAt, webPrepareSuspendedAt));
                webPrepareSuspendedAt = -1f;
            }
        }

        private void RefreshMobileDecoderOwnership()
        {
            if (!MobileWebBudget || !HasSelection) return;
            if (webCityPriority) return;
            if (webAwaitingAdmission)
            {
                if (!wantsPlayback || menuSuspended || focusSuspended || applicationSuspended || !webRelevant)
                    CancelPendingAdmission();
                else if (Time.unscaledTime >= webDecoderAdmitAt)
                {
                    webAwaitingAdmission = false;
                    BeginPlayback(selectedChannel);
                }
                return;
            }
            if (webRelevant)
            {
                webIrrelevantSince = -1f;
                if (webDecoderRetired && wantsPlayback && !menuSuspended && !focusSuspended && !applicationSuspended)
                    Play(selectedChannel);
                return;
            }
            if (!player) return;
            if (webIrrelevantSince < 0f) webIrrelevantSince = Time.unscaledTime;
            if (Time.unscaledTime - webIrrelevantSince < 2f) return;
            // Pausing retains compressed media, decode surfaces and linear GPU
            // scratch storage. Keep the physical TV's last picture, retire its
            // decoder outside the room, and re-prepare only on returning.
            ReleasePlayback();
            SlopCityMediaAudioFocus.Remove(this);
            webDecoderRetired = true;
            state = PlaybackState.Ready;
        }

        private void CancelPendingAdmission()
        {
            if (!webAwaitingAdmission) return;
            webAwaitingAdmission = false;
            ReleasePlayback();
            webDecoderRetired = true;
            state = PlaybackState.Ready;
        }

        private void RefreshWebInterest()
        {
            if (!webCamera)
            {
                webCamera = Camera.main;
            }
            // Only world proximity/visibility makes the decoder relevant. Menus contain controls, never video.
            webRelevant = false;
            if (!webCamera || !webCamera.isActiveAndEnabled) return;
            Vector3 position = webCamera.transform.position;
            // A TV behind the home wall must not reserve the city's only decoder.
            // Reuse actual bounded/occluded audio eligibility, including couch audio.
            if (volume > 0f && audioScore > .001f) webRelevant = true;
            if (webRelevant || !screen || !screen.enabled
                || (screen.bounds.center - position).sqrMagnitude >= 24f * 24f) return;
            GeometryUtility.CalculateFrustumPlanes(webCamera, webFrustum);
            if (!GeometryUtility.TestPlanesAABB(webFrustum, screen.bounds)) return;
            Vector3 toScreen = screen.bounds.center - position;
            webRelevant = !MediaPathOccluded(webCamera.transform, toScreen.normalized, toScreen.magnitude, television, true, out _);
        }
#endif

        private void OnPrepared(VideoPlayer source)
        {
            if (releasingPlayback || source != player || state != PlaybackState.Loading) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (MobileWebBudget && (source.width > 480 || source.height > 480))
            { Fail("Clip resolution exceeds this browser's safe decode limit."); return; }
#endif
            if (source.width > 4096 || source.height > 4096 || (ulong)source.width * source.height > 8UL * 1024 * 1024)
            { Fail("This clip exceeds the safe decode resolution. Choose another channel."); return; }
            state = PlaybackState.Ready;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!webAwaitingFirstPicture)
#endif
            SetDisplayVisible(true);
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshAudioInterest();
            RefreshWebInterest();
#endif
            ApplyPlaybackIntent();
        }

        private void OnError(VideoPlayer source, string message)
        {
            if (releasingPlayback || source != player) return;
            Debug.LogWarning("SLOP CITY: Media decoder: " + message, this);
            Fail("This clip could not be played. Choose another channel or try again.");
        }

        private void OnEnded(VideoPlayer source)
        {
            if (releasingPlayback || source != player) return;
            // loopPointReached is also raised on a looping decoder. Keep its
            // playback intent, including an explicit pause at the loop boundary.
            if (source.isLooping) return;
            wantsPlayback = false;
            state = PlaybackState.Ended;
        }

        private void ApplyPlaybackIntent()
        {
            if (state != PlaybackState.Ready || !player || !player.isPrepared) return;
            ApplyVolume();
            if (wantsPlayback && !IsSuspended)
            {
                if (!player.isPlaying) player.Play();
            }
            else player.Pause();
        }

        private void Fail(string message)
        {
            wantsPlayback = false;
            state = PlaybackState.Failed;
            failure = message;
            ReleasePlayback();
            TryMediaCleanup(() => SetDisplayVisible(false));
        }

        private bool EnsureDisplay()
        {
            if (!ReleaseFailedDisplayAllocations())
            { Fail("The previous video display is still retiring. Choose its channel to retry."); return false; }
            if (!screen || !screenShader || !screenShader.isSupported)
            {
                Fail("The television display is unavailable. Rebuild the lounge scene.");
                return false;
            }
            if (!capturedDisplay)
            {
                originalScreenMaterial = screen.sharedMaterial;
                originalScreenEnabled = screen.enabled;
                originalLetteringEnabled = new bool[screenLettering == null ? 0 : screenLettering.Length];
                for (int i = 0; i < originalLetteringEnabled.Length; i++)
                    originalLetteringEnabled[i] = screenLettering[i] && screenLettering[i].enabled;
                capturedDisplay = true;
            }
            RenderTexture previousTexture = screenTexture;
            Material previousMaterial = screenMaterial;
            try
            {
                if (!screenTexture)
                {
#if UNITY_WEBGL && !UNITY_EDITOR
                    screenTexture = new RenderTexture(MobileWebBudget ? 480 : 960, MobileWebBudget ? 270 : 540, 0, RenderTextureFormat.ARGB32);
#else
                    screenTexture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
#endif
                    screenTexture.name = "SlopCity_TelevisionVideo";
                    screenTexture.filterMode = FilterMode.Bilinear;
                    screenTexture.wrapMode = TextureWrapMode.Clamp;
                    screenTexture.useMipMap = false;
                    screenTexture.autoGenerateMips = false;
                }
                if (!screenTexture.IsCreated() && !screenTexture.Create())
                    throw new InvalidOperationException("Video texture allocation failed.");
                if (!screenMaterial)
                {
                    screenMaterial = new Material(screenShader);
                    screenMaterial.name = "SlopCity_TelevisionVideo_Runtime";
                    screenMaterial.mainTexture = screenTexture;
                }
                return true;
            }
            catch (Exception error)
            {
                // Retire only allocations made by this attempt. A pre-existing
                // still picture remains owned and usable after a failed retry.
                Material failedMaterial = screenMaterial;
                RenderTexture failedTexture = screenTexture;
                screenMaterial = previousMaterial;
                screenTexture = previousTexture;
                if (failedMaterial && failedMaterial != previousMaterial) failedDisplayMaterial = failedMaterial;
                if (failedTexture && failedTexture != previousTexture) failedDisplayTexture = failedTexture;
                ReleaseFailedDisplayAllocations();
                Debug.LogWarning("SLOP CITY: Television display: " + error.GetType().Name, this);
                Fail("The television could not start its video display. Try again.");
                return false;
            }
        }

        private void SetDisplayVisible(bool visible)
        {
            displayVisible = visible;
            if (!capturedDisplay) return;
            if (screen)
            {
                screen.sharedMaterial = visible ? screenMaterial : originalScreenMaterial;
                screen.enabled = visible || originalScreenEnabled;
            }
            for (int i = 0; i < originalLetteringEnabled.Length; i++)
                if (screenLettering[i]) screenLettering[i].enabled = !visible && originalLetteringEnabled[i];
        }

        private void ClearScreenTexture()
        {
            RenderTexture previous = RenderTexture.active;
            try { RenderTexture.active = screenTexture; GL.Clear(true, true, Color.black); }
            finally { RenderTexture.active = previous; }
        }

        private void ReleasePlayback()
        {
            // Native Stop can synchronously report an error if event detachment
            // failed. Do not recurse into release or revive a retiring player.
            if (releasingPlayback) return;
            releasingPlayback = true;
            try { ReleasePlaybackResources(); }
            finally { releasingPlayback = false; }
        }

        private void ReleasePlaybackResources()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            webPlaybackGeneration++;
            webAwaitingFirstPicture = false;
            webPublishedFrameSerial = webCopiedFrameSerial = 0;
            webPublishedFrameIndex = long.MinValue;
            webPicturePumpFrame = -1;
            webPrepareSuspendedAt = -1f;
#endif
            VideoPlayer retiredPlayer = player;
            AudioSource retiredAudio = audioSource;
            GameObject retiredObject = playbackObject;
            if (retiredPlayer)
            {
                TryMediaCleanup(() => retiredPlayer.prepareCompleted -= OnPrepared);
                TryMediaCleanup(() => retiredPlayer.errorReceived -= OnError);
                TryMediaCleanup(() => retiredPlayer.loopPointReached -= OnEnded);
#if UNITY_WEBGL && !UNITY_EDITOR
                if (webFrameReadyHandler != null)
                    TryMediaCleanup(() => retiredPlayer.frameReady -= webFrameReadyHandler);
                TryMediaCleanup(() => retiredPlayer.sendFrameReadyEvents = false);
#endif
                // A failed native operation must not skip the other release
                // operations or the destruction of the decoder owner.
                TryMediaCleanup(() => retiredPlayer.targetTexture = null);
                TryMediaCleanup(retiredPlayer.Stop);
            }
            if (retiredAudio) TryMediaCleanup(retiredAudio.Stop);
            bool retired = TryMediaCleanup(() => { if (retiredObject) Destroy(retiredObject); });
            // If destruction was refused, retain the exact owner for a later
            // Stop/retry. A throw after destruction is already a released owner.
            if (!retired && retiredObject) return;
            player = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            webFrameReadyHandler = null;
            if (mobileDecoderOwner == this) mobileDecoderOwner = null;
#endif
            audioSource = null;
            playbackObject = null;
        }

        private void ReleaseDisplay()
        {
            TryMediaCleanup(() => SetDisplayVisible(false));
            Material material = screenMaterial;
            RenderTexture texture = screenTexture;
            if (TryMediaCleanup(() => { if (material) Destroy(material); }) || !material) screenMaterial = null;
            if (TryMediaCleanup(() => DisposeDisplayTexture(texture)) || !texture) screenTexture = null;
            // Keep any handle whose destruction was refused available to retry.
            if (screenMaterial || screenTexture) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            webHasPicture = false;
#endif
            capturedDisplay = false;
            originalLetteringEnabled = null;
        }

        private bool ReleaseFailedDisplayAllocations()
        {
            Material material = failedDisplayMaterial;
            RenderTexture texture = failedDisplayTexture;
            if (TryMediaCleanup(() => { if (material) Destroy(material); }) || !material) failedDisplayMaterial = null;
            if (TryMediaCleanup(() => DisposeDisplayTexture(texture)) || !texture) failedDisplayTexture = null;
            return !failedDisplayMaterial && !failedDisplayTexture;
        }

        private static bool TryMediaCleanup(Action cleanup)
        {
            try { cleanup(); return true; }
            catch (Exception error)
            {
                Debug.LogWarning("SLOP CITY: Media cleanup: " + error.GetType().Name);
                return false;
            }
        }

        private static void DisposeDisplayTexture(RenderTexture texture)
        {
            if (!texture) return;
            try { texture.Release(); }
            catch (Exception error) { Debug.LogWarning("SLOP CITY: Television texture release: " + error.GetType().Name); }
            finally { Destroy(texture); }
        }

        private static double FiniteSeconds(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0d ? 0d : value;
        }

        private void OnApplicationFocus(bool focused)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClock();
#endif
            focusSuspended = !focused;
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClock();
            if (!focused) CancelPendingAdmission();
#endif
            ApplyPlaybackIntent();
        }

        private void OnApplicationPause(bool suspended)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClock();
#endif
            applicationSuspended = suspended;
#if UNITY_WEBGL && !UNITY_EDITOR
            RefreshMobilePreparationClock();
            if (suspended) CancelPendingAdmission();
#endif
            ApplyPlaybackIntent();
        }

        private void OnDisable() { Stop(); }
        private void OnDestroy()
        {
            TryMediaCleanup(() => SlopCityMediaAudioFocus.Remove(this));
            TryMediaCleanup(ReleasePlayback);
            TryMediaCleanup(ReleaseDisplay);
            ReleaseFailedDisplayAllocations();
        }
    }
}
