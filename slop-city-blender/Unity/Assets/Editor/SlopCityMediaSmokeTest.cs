#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Batch Play Mode integration checks using the real native video decoder; no UI automation.</summary>
    [InitializeOnLoad]
    public static class SlopCityMediaSmokeTest
    {
        private const string ActiveKey = "SlopCity.MediaSmoke.Active";
        private const string StartedKey = "SlopCity.MediaSmoke.Started";
        private const string ReportPath = "Builds/Reports/media-playback-check.json";
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt report;
        private static SlopCityFirstPerson controller;
        private static SlopCityMediaPlayer media;
        private static SlopCityPlayerInput input;
        private static SlopCityHud hud;
        private static Renderer screen;
        private static Material originalMaterial;
        private static readonly Dictionary<Renderer, bool> Lettering = new Dictionary<Renderer, bool>();
        private static double playModeEnteredAt;
        private static bool samplingAudio = true;
        private static readonly float[] AudioSamples = new float[1024];

        static SlopCityMediaSmokeTest()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Tick;
        }

        // Run with -batchmode -executeMethod SlopCity.Editor.SlopCityMediaSmokeTest.Run.
        // Omit -quit and -nographics: this runner exits after writing its receipt.
        public static void Run()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Media smoke tests require a dedicated batch Editor; they exit that Editor when complete.");
            SessionState.SetString(StartedKey, DateTime.UtcNow.ToString("O"));
            report = NewReceipt();
            SessionState.SetBool(ActiveKey, true);
            WriteReport();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Start the media smoke test outside Play Mode.");
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static Receipt NewReceipt()
        {
            return new Receipt
            {
                startedAtUtc = SessionState.GetString(StartedKey, DateTime.UtcNow.ToString("O")),
                unityVersion = Application.unityVersion,
                scenePath = SlopCitySceneBuilder.ScenePath,
                graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName
            };
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
                playModeEnteredAt = EditorApplication.timeSinceStartup;
            else if (change == PlayModeStateChange.EnteredEditMode)
                Finish(new InvalidOperationException("Play Mode ended before the smoke test completed."));
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (report == null) report = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(report.startedAtUtc).ToUniversalTime()).TotalSeconds > 180d)
                    throw new TimeoutException("Media smoke test exceeded its 180-second overall limit.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (Work.Count == 0)
                {
                    if (EditorApplication.timeSinceStartup - playModeEnteredAt < 1d) return;
                    Work.Push(ExerciseLounge());
                }
                ObserveAudio();
                // Advance nested enumerators until the next frame boundary.
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

        private static IEnumerator ExerciseLounge()
        {
            Application.runInBackground = true;
            controller = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            media = Object.FindFirstObjectByType<SlopCityMediaPlayer>();
            Check(controller && media && controller.Media == media, "Saved scene connects the controller to its media player.");
            input = controller.GetComponent<SlopCityPlayerInput>();
            Check(input, "The controller has an active input component.");
            hud = Object.FindFirstObjectByType<SlopCityHud>();
            Check(hud, "The saved scene has its production HUD.");
            var settings = new SerializedObject(media);
            screen = settings.FindProperty("screen").objectReferenceValue as Renderer;
            Check(screen && !screen.enabled, "TV playback quad starts hidden.");
            originalMaterial = screen.sharedMaterial;
            SerializedProperty labels = settings.FindProperty("screenLettering");
            for (int i = 0; i < labels.arraySize; i++)
            {
                var label = labels.GetArrayElementAtIndex(i).objectReferenceValue as Renderer;
                if (label) Lettering[label] = label.enabled;
            }
            Check(Lettering.Count > 0, "Authored TV lettering exists for restoration checks.");
            Check(!media.HasSelection && !media.IsPlaying, "Start menu does not autoplay media.");
            VerifyWorldOnly("Start menu");

            // Batch mode has no foreground Game view. Exercise the application's
            // foreground callbacks directly without touching a user's windows.
            controller.SendMessage("OnApplicationFocus", true);
            media.SendMessage("OnApplicationFocus", true);
            controller.SendMessage("OnApplicationPause", false);
            media.SendMessage("OnApplicationPause", false);
            report.simulatedForegroundCallbacks = true;
            controller.PlayOrResume();
            yield return VerifyDecode(SlopCityMediaChannel.Broadcast);
            Check(!controller.IsPaused && Time.timeScale == 1f, "PLAY starts the lounge and the broadcast.");
            Check(!SourceCard().activeSelf, "PLAY leaves the media controls closed.");
            yield return VerifyCouchAudio();
            VideoPlayer looping = NativePlayer();
            Check(looping.isLooping, "Lounge broadcasts loop instead of silently ending while the player is seated.");
            looping.time = media.DurationSeconds - 1.5d;
            yield return WaitFor(() => media.TimeSeconds > media.DurationSeconds - 3d, 5d,
                "Native decoder did not seek to its loop boundary.");
            yield return WaitFor(() => media.IsPlaying && media.TimeSeconds < 3d, 8d,
                "The broadcast did not continue playing across its native loop boundary.");
            Check(!media.IsPaused, "Native loop callback preserves playback intent.");
            yield return VerifyAdvance("Lounge playback advances after its clip loops.");

            VideoPlayer broadcastSession = NativePlayer();
            InvokeTelevisionInteraction();
            Check(controller.IsPaused && SourceCard().activeSelf && NativePlayer() == broadcastSession,
                "Living-room TV E opens existing media selection without recreating its decoder.");
            VerifyWorldOnly("Physical TV media controls opened");
            yield return VerifyAdvance("Video keeps advancing while physical TV media controls are open.");
            controller.PlayOrResume();
            Check(!controller.IsPaused && Time.timeScale == 1f, "BACK TO LOUNGE resumes gameplay.");
            yield return VerifyAdvance("Returning to gameplay keeps the current media playing.");

            input.RequestPause();
            yield return WaitFor(() => controller.IsPaused, 3d, "Pause input was not consumed.");
            yield return VerifyHeld("The normal pause menu holds video time.");
            input.RequestPause();
            yield return WaitFor(() => !controller.IsPaused, 3d, "Resume input was not consumed.");
            yield return VerifyAdvance("Leaving the normal pause menu resumes intended playback.");

            controller.ToggleMedia();
            yield return VerifyHeld("Manual media pause holds video time.");
            media.SetSuspended(true);
            media.SetSuspended(false);
            input.RequestPause();
            yield return WaitFor(() => controller.IsPaused, 3d, "Pause input was not consumed while media was paused.");
            input.RequestPause();
            yield return WaitFor(() => !controller.IsPaused, 3d, "Resume input was not consumed while media was paused.");
            yield return VerifyHeld("Manual pause survives suspension and a pause-menu round trip.");
            Check(media.IsPaused, "Manual pause intent remains set after suspension.");
            controller.ToggleMedia();
            yield return VerifyAdvance("Explicit media resume advances the same selection.");

            media.SetSuspended(true);
            yield return VerifyHeld("Explicit suspension holds playing video.");
            media.SetSuspended(false);
            yield return VerifyAdvance("Releasing suspension resumes a selection that intended to play.");
            media.SendMessage("OnApplicationPause", true);
            yield return VerifyHeld("Application suspension holds playing video.");
            media.SendMessage("OnApplicationPause", false);
            yield return VerifyAdvance("Returning from application suspension resumes intended playback.");

            controller.PlayMedia(SlopCityMediaChannel.Record);
            yield return VerifyDecode(SlopCityMediaChannel.Record);
            var recordAnchor = settings.FindProperty("records").objectReferenceValue as Transform;
            Check(recordAnchor && Vector3.Distance(CurrentAudio().transform.position, recordAnchor.position) < 0.01f,
                "Record audio is emitted from the in-world record corner.");
            controller.PlayMedia(SlopCityMediaChannel.Duet);
            yield return VerifyDecode(SlopCityMediaChannel.Duet);
            var tvAnchor = settings.FindProperty("television").objectReferenceValue as Transform;
            Check(tvAnchor && Vector3.Distance(CurrentAudio().transform.position, tvAnchor.position) < 0.01f,
                "Duet audio is emitted from the in-world television.");

            media.SetVolume(-5f);
            Check(media.Volume == 0f && CurrentAudio().volume == 0f, "Volume clamps to silence at its lower bound.");
            VerifyFocusedVolume(0f, "Lower user-volume bound");
            media.SetVolume(5f);
            Check(media.Volume == 1f, "User volume clamps to one at its upper bound.");
            VerifyFocusedVolume(1f, "Upper user-volume bound preserves spatial and exclusive focus gain");
            media.SetVolume(0.8f);
            VerifyFocusedVolume(0.8f, "Restored user volume preserves spatial and exclusive focus gain");
            controller.StopMedia();
            yield return Delay(0.25d);
            Check(!media.HasSelection && !media.IsPlaying && !media.ScreenTexture, "STOP clears selection, playback, and the world-screen texture.");
            Check(!screen.enabled && screen.sharedMaterial == originalMaterial, "STOP hides the quad and restores its original material.");
            foreach (var label in Lettering)
                Check(label.Key && label.Key.enabled == label.Value, "STOP restores authored TV label: " + label.Key.name + ".");
            Check(media.GetComponentsInChildren<VideoPlayer>().Length == 0
                && media.GetComponentsInChildren<AudioSource>().Length == 0,
                "STOP releases the lounge's native video and audio components independently of city screens.");
            VerifyWorldOnly("STOP releases lounge playback");
        }

        private static IEnumerator VerifyDecode(SlopCityMediaChannel channel)
        {
            yield return WaitFor(() => media.SelectedChannel == channel && media.IsPlaying && NativePlayer()
                && NativePlayer().isPrepared && NativePlayer().frame > 2 && media.TimeSeconds > 0.15d,
                32d, channel + " did not produce native video frames. Status: ");
            VideoPlayer native = NativePlayer();
            var evidence = new ChannelEvidence { channel = channel.ToString(), durationSeconds = media.DurationSeconds };
            report.channels.Add(evidence);
            evidence.firstFrame = native.frame;
            evidence.firstTimeSeconds = media.TimeSeconds;
            yield return VerifyAdvance(channel + " native frame index and playback time advance.");
            double deadline = EditorApplication.timeSinceStartup + 10d;
            do
            {
                evidence.centerPixelVariance = PixelVariance(media.ScreenTexture as RenderTexture);
                if (evidence.centerPixelVariance > 0.0001d) break;
                yield return Delay(0.2d);
            } while (EditorApplication.timeSinceStartup < deadline);
            evidence.lastFrame = native.frame;
            evidence.lastTimeSeconds = media.TimeSeconds;
            Check(evidence.lastFrame > evidence.firstFrame && evidence.lastTimeSeconds > evidence.firstTimeSeconds,
                channel + " decoder advances both frames and timestamps.");
            Check(evidence.centerPixelVariance > 0.0001d, channel + " RenderTexture contains decoded, nonuniform center pixels.");
            Check(screen.enabled && screen.sharedMaterial.mainTexture == media.ScreenTexture,
                channel + " decoded texture is bound to the in-world television.");
            foreach (var label in Lettering) Check(!label.Key.enabled, channel + " hides TV label: " + label.Key.name + ".");
            AudioSource audio = CurrentAudio();
            Check(native.audioTrackCount > 0 && native.audioOutputMode == VideoAudioOutputMode.AudioSource && audio
                && native.GetTargetAudioSource(0) == audio && audio.spatialBlend == 1f,
                channel + " has native audio routed through its spatial AudioSource.");
            Check(media.GetComponentsInChildren<VideoPlayer>().Length == 1
                && media.GetComponentsInChildren<AudioSource>().Length == 1,
                channel + " is the only active lounge video/audio session.");
            VerifyWorldOnly(channel + " channel decoded on its world device");
            Debug.Log("SLOP_CITY_MEDIA_CHANNEL_VERIFIED " + channel + " frame=" + evidence.lastFrame
                + " time=" + evidence.lastTimeSeconds + " variance=" + evidence.centerPixelVariance);
        }

        private static IEnumerator VerifyCouchAudio()
        {
            var failures = new List<string>();
            controller.RespawnAtCouch();
            Check(controller.IsSeated, "Audio regressions use the real saved-scene seated couch position.");
            Transform turntable = Array.Find(Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), item => item.name == "Turntable");
            Check(turntable, "Couch record aim targets the physical turntable rather than its remote interaction marker.");
            foreach (SlopCityMediaChannel channel in new[] { SlopCityMediaChannel.Broadcast, SlopCityMediaChannel.Record })
            {
                if (media.SelectedChannel != channel) controller.PlayMedia(channel);
                yield return VerifyDecode(channel);
                Vector3 target = channel == SlopCityMediaChannel.Record ? turntable.position : screen.bounds.center;
                AimFromCouch(target);
                yield return CaptureAudio(channel + " couch / centered on physical device", true, failures);
                Vector3 away = controller.ViewCamera.transform.position
                    + (controller.ViewCamera.transform.position - target).normalized * 3f;
                AimFromCouch(away);
                yield return CaptureAudio(channel + " couch / nearby looking away", true, failures);
                AimFromCouch(target);
                media.SetVolume(0f);
                yield return CaptureAudio(channel + " couch / explicit mute", false, failures);
                media.SetVolume(.8f);
                controller.ToggleMedia();
                yield return CaptureAudio(channel + " couch / manual pause", false, failures);
                controller.ToggleMedia();
                yield return CaptureAudio(channel + " couch / resumed", true, failures);

                // Add a real collider between the real couch and the active emitter,
                // covering both screen and marker rays without bypassing production occlusion.
                Vector3 listener = controller.ViewCamera.transform.position;
                Vector3 audioTarget = channel == SlopCityMediaChannel.Record
                    ? CurrentAudio().transform.position : screen.bounds.center;
                Vector3 direction = (audioTarget - listener).normalized;
                GameObject wall = new GameObject("MediaSmoke_InterveningWall");
                wall.transform.position = Vector3.Lerp(listener, audioTarget, .5f);
                wall.transform.rotation = Quaternion.LookRotation(direction);
                wall.AddComponent<BoxCollider>().size = new Vector3(3f, 3f, .2f);
                Physics.SyncTransforms();
                yield return CaptureAudio(channel + " couch / intervening wall", false, failures);
                Object.DestroyImmediate(wall);
                Physics.SyncTransforms();
                yield return CaptureAudio(channel + " couch / wall removed", true, failures);
            }
            // Collect both devices' poses first, so an initial TV failure cannot hide
            // the reported record failure from the same diagnostic receipt.
            Check(failures.Count == 0, "Saved-scene couch audio regressions: " + string.Join(" | ", failures));
            controller.RespawnAtCouch();
            controller.PlayMedia(SlopCityMediaChannel.Broadcast);
            yield return VerifyDecode(SlopCityMediaChannel.Broadcast);
        }

        private static void AimFromCouch(Vector3 target)
        {
            Vector3 direction = (target - controller.ViewCamera.transform.position).normalized;
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg, -75f, 75f);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(SlopCityFirstPerson).GetField("yaw", flags).SetValue(controller, yaw);
            typeof(SlopCityFirstPerson).GetField("pitch", flags).SetValue(controller, pitch);
            controller.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            controller.ViewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            Physics.SyncTransforms();
        }

        private static IEnumerator CaptureAudio(string stage, bool expectedAudible, List<string> failures)
        {
            yield return Delay(1.1d); // Refresh interest, expire competing requests and settle both focus fades.
            AudioSource source = CurrentAudio();
            var evidence = new CouchAudioEvidence
            {
                stage = stage, expectedAudible = expectedAudible, seated = controller.IsSeated,
                nativeEmitterVolume = source ? source.volume : 0f,
                playing = media.IsPlaying, diagnostics = AudioDiagnostics()
            };
            report.couchAudioChecks.Add(evidence);
            double deadline = EditorApplication.timeSinceStartup + (expectedAudible ? 2d : .3d);
            do
            {
                if (source && samplingAudio)
                {
                    source.GetOutputData(AudioSamples, 0);
                    foreach (float value in AudioSamples)
                        evidence.maxAudioSampleMagnitude = Math.Max(evidence.maxAudioSampleMagnitude, Math.Abs(value));
                }
                yield return null;
            } while (EditorApplication.timeSinceStartup < deadline);
            evidence.nativeEmitterVolume = source ? source.volume : 0f;
            evidence.passed = evidence.seated && (expectedAudible
                ? media.IsPlaying && evidence.nativeEmitterVolume >= .08f && evidence.maxAudioSampleMagnitude > .00001d
                : evidence.nativeEmitterVolume <= .0001f);
            string description = stage + " / output=" + evidence.nativeEmitterVolume
                + " samples=" + evidence.maxAudioSampleMagnitude + " / " + evidence.diagnostics;
            if (!evidence.passed) failures.Add(description);
            else report.checks.Add(description);
            Debug.Log("SLOP_CITY_COUCH_AUDIO " + (evidence.passed ? "PASS " : "FAIL ") + description);
            VerifyWorldOnly(stage);
        }

        private static string AudioDiagnostics()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Transform listener = controller.ViewCamera.transform;
            AudioSource source = CurrentAudio();
            Vector3 centre = media.SelectedChannel == SlopCityMediaChannel.Record
                ? source.transform.position : screen.bounds.center;
            Vector3 delta = centre - listener.position;
            Vector3 direction = delta.normalized;
            float facing = media.SelectedChannel == SlopCityMediaChannel.Record ? 1f
                : Vector3.Dot(screen.transform.TransformDirection(Vector3.back), -direction);
            var obstacles = new List<string>();
            foreach (RaycastHit hit in Physics.RaycastAll(listener.position, direction, Mathf.Max(0f, delta.magnitude - .15f),
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                obstacles.Add(hit.transform.name + "@" + hit.distance.ToString("F3"));
            return "audioScore=" + typeof(SlopCityMediaPlayer).GetField("audioScore", flags).GetValue(media)
                + " facing=" + facing.ToString("F3") + " gaze=" + Vector3.Dot(listener.forward, direction).ToString("F3")
                + " distance=" + delta.magnitude.ToString("F3") + " rayHits=[" + string.Join(", ", obstacles) + "]";
        }

        private static IEnumerator VerifyAdvance(string description)
        {
            double initial = media.TimeSeconds;
            yield return WaitFor(() => media.IsPlaying && media.TimeSeconds > initial + 0.35d, 6d,
                description + " Current status: ");
            Check(true, description);
            VerifyWorldOnly(description);
        }

        private static IEnumerator VerifyHeld(string description)
        {
            yield return Delay(0.2d); // Allow an already submitted native frame to settle.
            double initial = media.TimeSeconds;
            yield return Delay(0.55d);
            Check(!media.IsPlaying && Math.Abs(media.TimeSeconds - initial) < 0.08d, description);
            VerifyWorldOnly(description);
        }

        private static GameObject SourceCard()
        {
            FieldInfo field = typeof(SlopCityHud).GetField("sourceCard", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(typeof(SlopCityHud).FullName, "sourceCard");
            GameObject card = field.GetValue(hud) as GameObject;
            if (!card) throw new InvalidOperationException("The media controls card was not created by the production HUD.");
            return card;
        }

        private static void VerifyFocusedVolume(float expectedUserVolume, string stage)
        {
            AudioSource emitter = CurrentAudio();
            Check(emitter && Mathf.Abs(media.Volume - expectedUserVolume) < .0001f,
                stage + " / user preference is clamped or restored independently of output attenuation.");
            // The sink must receive the shared mixer's result, rather than bypass
            // distance/aim/occlusion and the one-audible-source policy by copying
            // the user slider directly. Gain is evaluated once per frame; SetVolume
            // has already submitted this preference and applied that same result.
            Type mixer = typeof(SlopCityMediaPlayer).Assembly.GetType("SlopCity.SlopCityMediaAudioFocus", true);
            MethodInfo gainMethod = mixer.GetMethod("Gain", BindingFlags.Static | BindingFlags.NonPublic);
            if (gainMethod == null) throw new MissingMethodException(mixer.FullName, "Gain");
            float computedGain = (float)gainMethod.Invoke(null, new object[] { media });
            Check(!float.IsNaN(computedGain) && !float.IsInfinity(computedGain) && computedGain >= 0f && computedGain <= 1f
                && Mathf.Abs(emitter.volume - computedGain) < .0001f,
                stage + " / native AudioSource volume equals the finite bounded gain chosen by shared audio focus.");
            if (expectedUserVolume == 0f)
                Check(computedGain == 0f && emitter.volume == 0f,
                    stage + " / mute silences the native emitter despite any retained fade state.");
            report.volumeChecks.Add(new VolumeEvidence
            {
                stage = stage, userVolume = media.Volume, computedFocusGain = computedGain, nativeEmitterVolume = emitter.volume
            });
        }

        private static void InvokeTelevisionInteraction()
        {
            // Native E resolves its aimed prop and dispatches this exact production
            // handler. Invoke the dispatch directly to avoid changing player/camera
            // positions for test setup; this is not hardware keyboard/aim validation.
            Type nearbyAction = typeof(SlopCityFirstPerson).GetNestedType("NearbyAction", BindingFlags.NonPublic);
            MethodInfo perform = typeof(SlopCityFirstPerson).GetMethod("PerformInteraction", BindingFlags.Instance | BindingFlags.NonPublic);
            if (nearbyAction == null || perform == null)
                throw new MissingMethodException("Production TV interaction dispatch was not found.");
            Camera camera = controller.ViewCamera;
            Check(camera, "TV interaction has its real first-person camera.");
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            bool wasPaused = controller.IsPaused;
            Check(!SourceCard().activeSelf, "TV E regression starts with media controls closed.");
            perform.Invoke(controller, new[] { Enum.Parse(nearbyAction, "Television") });
            Check(!SourceCard().activeSelf && controller.IsPaused == wasPaused,
                "TV E does not open the media controls card or pause gameplay.");
            Check(Vector3.Distance(position, camera.transform.position) < .0001f
                && Quaternion.Angle(rotation, camera.transform.rotation) < .001f
                && Mathf.Abs(fieldOfView - camera.fieldOfView) < .0001f,
                "TV E preserves the first-person camera position, rotation and field of view.");
            VerifyWorldOnly("Production TV E dispatch");
        }

        private static void VerifyWorldOnly(string stage)
        {
            // Include inactive menus: hiding an unwanted video preview would leave
            // its renderer available to reappear on the next M/E interaction.
            RawImage[] hudImages = hud.GetComponentsInChildren<RawImage>(true);
            Check(hudImages.Length == 0, stage + " / HUD contains no RawImage video preview, including inactive menus.");
            var worldTextures = new HashSet<Texture>();
            if (media.ScreenTexture) worldTextures.Add(media.ScreenTexture);
            VideoPlayer[] players = Object.FindObjectsByType<VideoPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (VideoPlayer decoder in players)
            {
                Check(decoder.renderMode == VideoRenderMode.RenderTexture,
                    stage + " / " + decoder.name + " outputs to a world RenderTexture, never a camera plane.");
                if (decoder.targetTexture) worldTextures.Add(decoder.targetTexture);
                Check(!decoder.isPrepared || !decoder.isPlaying || decoder.targetTexture,
                    stage + " / prepared playing decoder has a texture target: " + decoder.name);
            }
            foreach (RawImage image in Object.FindObjectsByType<RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool displaysWorldVideo = image.texture && worldTextures.Contains(image.texture)
                    || image.material && image.material.mainTexture && worldTextures.Contains(image.material.mainTexture);
                Check(!displaysWorldVideo, stage + " / UI RawImage does not display a world video texture: " + image.name);
            }
            VideoPlayer lounge = NativePlayer();
            if (lounge && lounge.isPrepared && media.HasSelection)
                Check(lounge.targetTexture && lounge.targetTexture == media.ScreenTexture && screen.enabled
                    && screen.sharedMaterial && screen.sharedMaterial.mainTexture == lounge.targetTexture,
                    stage + " / lounge video target remains bound to the physical TV renderer.");
            report.worldOnlyStages.Add(stage);
        }

        private static IEnumerator WaitFor(Func<bool> condition, double seconds, string failure)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (!condition())
            {
                if (EditorApplication.timeSinceStartup >= deadline)
                    throw new TimeoutException(failure + (media ? media.Status : ""));
                yield return null;
            }
        }

        private static IEnumerator Delay(double seconds)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < deadline) yield return null;
        }

        private static VideoPlayer NativePlayer() => media ? media.GetComponentInChildren<VideoPlayer>() : null;
        private static AudioSource CurrentAudio() => media ? media.GetComponentInChildren<AudioSource>() : null;

        private static double PixelVariance(RenderTexture source)
        {
            if (!source || !source.IsCreated()) return 0d;
            RenderTexture previous = RenderTexture.active;
            RenderTexture sample = RenderTexture.GetTemporary(64, 36, 0, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(64, 36, TextureFormat.RGB24, false, true);
            try
            {
                Graphics.Blit(source, sample);
                RenderTexture.active = sample;
                pixels.ReadPixels(new Rect(0, 0, 64, 36), 0, 0);
                pixels.Apply();
                Color32[] data = pixels.GetPixels32();
                double total = 0d, squared = 0d;
                int count = 0;
                // Sample within portrait content, excluding the black FitInside pillars.
                for (int y = 4; y < 32; y++) for (int x = 26; x < 38; x++)
                {
                    Color32 pixel = data[y * 64 + x];
                    double luma = (pixel.r + pixel.g + pixel.b) / 765d;
                    total += luma; squared += luma * luma; count++;
                }
                return squared / count - (total / count) * (total / count);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(sample);
                Object.DestroyImmediate(pixels);
            }
        }

        private static void ObserveAudio()
        {
            if (!samplingAudio || !media || !media.IsPlaying) return;
            AudioSource audio = CurrentAudio();
            if (!audio) return;
            try
            {
                audio.GetOutputData(AudioSamples, 0);
                foreach (float value in AudioSamples)
                    report.maxAudioSampleMagnitude = Math.Max(report.maxAudioSampleMagnitude, Math.Abs(value));
            }
            catch (Exception exception)
            {
                samplingAudio = false;
                report.warnings.Add("Audio sample readback unavailable: " + exception.Message);
            }
        }

        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            report.checks.Add(description);
        }

        private static void Finish(Exception exception)
        {
            SessionState.SetBool(ActiveKey, false);
            Work.Clear();
            if (report == null) report = NewReceipt();
            if (exception != null) report.errors.Add(exception.ToString());
            report.passed = report.errors.Count == 0;
            report.result = report.passed ? "Passed" : "Failed";
            report.finishedAtUtc = DateTime.UtcNow.ToString("O");
            report.audioVerification = report.maxAudioSampleMagnitude > 0.00001d
                ? "Nonzero native AudioSource samples observed; audible speaker output was not listened to."
                : "Native audio track and spatial routing checked; output samples/audible playback unverified in batch mode.";
            WriteReport();
            Debug.Log("SLOP_CITY_MEDIA_SMOKE_" + report.result.ToUpperInvariant() + " " + ReportPath);
            if (exception != null) Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1);
        }

        private static void WriteReport()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }

        [Serializable]
        private sealed class Receipt
        {
            public string result = "Running";
            public bool passed;
            public string startedAtUtc;
            public string finishedAtUtc;
            public string unityVersion;
            public string scenePath;
            public string graphicsDevice;
            public string scope = "Batch Editor Play Mode; real saved-scene couch poses aimed at physical TV/turntable, per-pose useful native emitter gain and sample readback, nearby look-away audio, mute/pause and intervening collider suppression, native MP4 decode, numeric world RenderTexture readback, production TV E dispatch, queued M input, no UI video preview, runtime controls and resource lifecycle. TV dispatch is invoked directly without hardware keyboard interaction. No screenshots, browser native fullscreen, standalone app interaction, or speaker listening.";
            public bool simulatedForegroundCallbacks;
            public string audioVerification = "Not yet checked.";
            public double maxAudioSampleMagnitude;
            public List<string> checks = new List<string>();
            public List<string> worldOnlyStages = new List<string>();
            public List<ChannelEvidence> channels = new List<ChannelEvidence>();
            public List<VolumeEvidence> volumeChecks = new List<VolumeEvidence>();
            public List<CouchAudioEvidence> couchAudioChecks = new List<CouchAudioEvidence>();
            public List<string> errors = new List<string>();
            public List<string> warnings = new List<string>();
        }

        [Serializable]
        private sealed class VolumeEvidence
        {
            public string stage;
            public float userVolume, computedFocusGain, nativeEmitterVolume;
        }

        [Serializable]
        private sealed class CouchAudioEvidence
        {
            public string stage, diagnostics;
            public bool expectedAudible, seated, playing, passed;
            public float nativeEmitterVolume;
            public double maxAudioSampleMagnitude;
        }

        [Serializable]
        private sealed class ChannelEvidence
        {
            public string channel;
            public double durationSeconds;
            public long firstFrame;
            public long lastFrame;
            public double firstTimeSeconds;
            public double lastTimeSeconds;
            public double centerPixelVariance;
        }
    }
}
#endif
