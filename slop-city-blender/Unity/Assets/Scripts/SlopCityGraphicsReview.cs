using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SlopCity
{
    /// <summary>Explicit standalone-only evidence collection; never installed without its command-line flag.</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class SlopCityGraphicsReview : MonoBehaviour
    {
        private const string Flag = "--slop-graphics-review", AudioFlag = "--slop-audio-review";
        private const string SaveKey = "SlopCity.Sandbox.Progress.v1";
        private const float WarmupSeconds = 3f, SampleSeconds = 8f;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private SlopCityFirstPerson player;
        private SlopCityPlayerInput input;
        private SaveSnapshot saved;
        private ReviewReport report;
        private AudioReviewReport audioReport;
        private bool audioReview, priorRunInBackground;
        private double audioReviewDeadline;
        private readonly List<AudioPoseReport> audioPoses = new List<AudioPoseReport>();
        private string folder, recoveryPath, reportPath;
        private bool protectedSession, completed, quitting, finishing;
        private float nextRestore;
        private readonly List<PoseReport> poses = new List<PoseReport>();
        private readonly List<string> errors = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            bool audio = Array.IndexOf(Environment.GetCommandLineArgs(), AudioFlag) >= 0;
            if (!audio && Array.IndexOf(Environment.GetCommandLineArgs(), Flag) < 0) return;
            var owner = new GameObject("SlopCity_ExplicitGraphicsReview");
            DontDestroyOnLoad(owner);
            var review = owner.AddComponent<SlopCityGraphicsReview>();
            review.audioReview = audio;
            try { review.BackupBeforeScene(); }
            catch (Exception exception)
            {
                Debug.LogError("SLOP CITY graphics review could not protect saved progress; review cancelled: " + exception);
                Destroy(owner);
                if (audio) Application.Quit(1);
            }
        }

        private void BackupBeforeScene()
        {
            folder = Path.Combine(Application.persistentDataPath, audioReview ? "AudioReview" : "GraphicsReview");
            Directory.CreateDirectory(folder);
            recoveryPath = Path.Combine(folder, "save-recovery.json");
            reportPath = Path.Combine(folder, audioReview ? "audio-review.json" : "graphics-review.json");
            // A force-killed diagnostic can be recovered by the next explicit diagnostic launch.
            // Ordinary launches do not inspect or alter these files.
            if (File.Exists(recoveryPath))
            {
                saved = JsonUtility.FromJson<SaveSnapshot>(File.ReadAllText(recoveryPath));
                if (saved == null || saved.files == null) throw new IOException("Invalid prior review recovery snapshot.");
                RestoreFilesAndPrefs();
            }
            string playback = Path.Combine(Application.persistentDataPath, "TikTok", "playback.json");
            saved = new SaveSnapshot
            {
                capturedUtc = DateTime.UtcNow.ToString("o"), hasProgress = PlayerPrefs.HasKey(SaveKey),
                progress = PlayerPrefs.GetString(SaveKey, ""),
                files = new[] { SnapshotFile(playback), SnapshotFile(playback + ".tmp") }
            };
            File.WriteAllText(recoveryPath, JsonUtility.ToJson(saved, true));
            protectedSession = true;
            report = new ReviewReport
            {
                startedUtc = DateTime.UtcNow.ToString("o"), applicationVersion = Application.version,
                unityVersion = Application.unityVersion, standalone = !Application.isEditor,
                operatingSystem = SystemInfo.operatingSystem, processor = SystemInfo.processorType,
                processorCount = SystemInfo.processorCount, systemMemoryMB = SystemInfo.systemMemorySize,
                graphicsDevice = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                graphicsVersion = SystemInfo.graphicsDeviceVersion, graphicsMemoryMB = SystemInfo.graphicsMemorySize,
                shaderLevel = SystemInfo.graphicsShaderLevel, supportsInstancing = SystemInfo.supportsInstancing,
                screenWidth = Screen.width, screenHeight = Screen.height,
                displayWidth = Screen.currentResolution.width, displayHeight = Screen.currentResolution.height,
                displayRefreshHz = Screen.currentResolution.refreshRateRatio.value,
                fullscreenMode = Screen.fullScreenMode.ToString(), vSyncCount = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate, qualityLevel = QualitySettings.GetQualityLevel(),
                colorSpace = QualitySettings.activeColorSpace.ToString()
            };
            if (audioReview)
            {
                priorRunInBackground = Application.runInBackground;
                Application.runInBackground = true;
                audioReport = new AudioReviewReport
                {
                    startedUtc = report.startedUtc, applicationVersion = Application.version,
                    unityVersion = Application.unityVersion, standalone = !Application.isEditor,
                    operatingSystem = SystemInfo.operatingSystem, actualApplicationFocusAtStart = Application.isFocused,
                    runInBackgroundEnabledForReview = true
                };
            }
            Application.wantsToQuit += BeforeQuit;
            WriteReport("initializing");
        }

        private void Start()
        {
            if (!protectedSession) return;
            if (audioReview) audioReviewDeadline = Time.realtimeSinceStartupAsDouble + 110;
            StartCoroutine(Guarded(audioReview ? RunAudio() : Run()));
        }

        // Manually unwind nested iterators so failures in any pose also restore the save snapshot.
        private IEnumerator Guarded(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                object current = null;
                bool advanced = false;
                Exception failure = null;
                try
                {
                    if (audioReview && Time.realtimeSinceStartupAsDouble > audioReviewDeadline)
                        throw new TimeoutException("Standalone audio review exceeded its 110-second limit.");
                    advanced = stack.Peek().MoveNext(); if (advanced) current = stack.Peek().Current;
                }
                catch (Exception exception) { failure = exception; }
                if (failure != null)
                {
                    errors.Add(failure.ToString());
                    Debug.LogError("SLOP CITY graphics review: " + failure);
                    Finish("failed");
                    yield break;
                }
                if (!advanced) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (current is IEnumerator nested) stack.Push(nested);
                else yield return current;
            }
        }

        private IEnumerator Run()
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 40;
            do
            {
                yield return null;
                player = FindFirstObjectByType<SlopCityFirstPerson>();
                if (player && player.ViewCamera && player.Sandbox && SlopCityCityMedia.Instance
                    && SlopCityCityMedia.Instance.ScreenCount > 0 && SlopCityCrowd.Instance) break;
            } while (Time.realtimeSinceStartupAsDouble < deadline);
            if (!player || !player.ViewCamera || !player.Sandbox) throw new InvalidOperationException("Player did not initialize.");
            input = player.GetComponent<SlopCityPlayerInput>();
            if (!input) throw new InvalidOperationException("Player input component is missing.");
            yield return null;
            ReadPipelineEvidence();
            yield return Measure("01-startup-couch", true);

            player.PlayOrResume();
            input.SetGameplayEnabled(false);
            yield return Measure("02-living-room", false);
            MoveToEye(new Vector3(20, 1.7f, -10));
            yield return Measure("03-avenue-north", false);
            MoveToEye(new Vector3(14, 1.7f, -10));
            yield return Measure("04-crowd-promenade", false);
            Finish("complete");
        }

        private IEnumerator RunAudio()
        {
            if (Application.isEditor) throw new InvalidOperationException("Audio review requires the standalone player.");
            double deadline = Time.realtimeSinceStartupAsDouble + 40;
            do
            {
                yield return null;
                player = FindFirstObjectByType<SlopCityFirstPerson>();
                if (player && player.ViewCamera && player.Sandbox && player.Media) break;
            } while (Time.realtimeSinceStartupAsDouble < deadline);
            if (!player || !player.ViewCamera || !player.Sandbox || !player.Media)
                throw new InvalidOperationException("Player/media did not initialize.");
            input = player.GetComponent<SlopCityPlayerInput>();
            if (!input) throw new InvalidOperationException("Player input is missing.");
            player.RespawnAtCouch();
            // The owned unattended diagnostic may lack OS focus. Exercise production
            // foreground callbacks explicitly; this is recorded and is not a focus test.
            player.SendMessage("OnApplicationFocus", true);
            player.SendMessage("OnApplicationPause", false);
            player.Media.SendMessage("OnApplicationFocus", true);
            player.Media.SendMessage("OnApplicationPause", false);
            if (SlopCityCityMedia.Instance)
            {
                SlopCityCityMedia.Instance.SendMessage("OnApplicationFocus", true);
                SlopCityCityMedia.Instance.SendMessage("OnApplicationPause", false);
            }
            audioReport.foregroundCallbacksSimulated = true;
            player.PlayOrResume();
            input.SetGameplayEnabled(false);
            Transform turntable = Array.Find(FindObjectsByType<Transform>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), item => item.name == "Turntable");
            if (!turntable) throw new InvalidOperationException("Physical Turntable is missing.");
            Renderer screen = ReadMediaField<Renderer>("screen");
            if (!screen) throw new InvalidOperationException("Physical TV screen is missing.");
            foreach (SlopCityMediaChannel channel in new[] { SlopCityMediaChannel.Broadcast, SlopCityMediaChannel.Record })
            {
                player.PlayMedia(channel);
                deadline = Time.realtimeSinceStartupAsDouble + 35;
                while (!player.Media.IsPlaying && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                if (!player.Media.IsPlaying)
                    throw new InvalidOperationException(channel + " did not start: " + player.Media.Status);
                Vector3 target = channel == SlopCityMediaChannel.Record ? turntable.position : screen.bounds.center;
                AimFromCouch(target);
                yield return CaptureAudio(channel + " / couch centered on physical device", true, screen);
                AimFromCouch(player.ViewCamera.transform.position
                    + (player.ViewCamera.transform.position - target).normalized * 3f);
                yield return CaptureAudio(channel + " / nearby looking away", true, screen);
                AimFromCouch(target);
                player.Media.SetVolume(0f);
                yield return CaptureAudio(channel + " / muted", false, screen);
                player.Media.SetVolume(.8f);
                player.Media.TogglePause();
                yield return CaptureAudio(channel + " / paused", false, screen);
                player.Media.TogglePause();
                yield return CaptureAudio(channel + " / resumed", true, screen);
            }
            Finish(errors.Count == 0 ? "complete" : "failed");
        }

        private T ReadMediaField<T>(string name)
        {
            FieldInfo field = typeof(SlopCityMediaPlayer).GetField(name, PrivateInstance);
            if (field == null) throw new MissingFieldException(typeof(SlopCityMediaPlayer).FullName, name);
            return (T)field.GetValue(player.Media);
        }

        private void AimFromCouch(Vector3 target)
        {
            Vector3 direction = (target - player.ViewCamera.transform.position).normalized;
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg, -75f, 75f);
            typeof(SlopCityFirstPerson).GetField("yaw", PrivateInstance).SetValue(player, yaw);
            typeof(SlopCityFirstPerson).GetField("pitch", PrivateInstance).SetValue(player, pitch);
            player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            player.ViewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            Physics.SyncTransforms();
        }

        private IEnumerator CaptureAudio(string label, bool expectedAudible, Renderer screen)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 1.1;
            while (Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            AudioSource source = ReadMediaField<AudioSource>("audioSource");
            if (!source) throw new InvalidOperationException("Native AudioSource is missing at " + label);
            var pose = new AudioPoseReport
            {
                label = label, expectedAudible = expectedAudible, seated = player.IsSeated,
                actualApplicationFocus = Application.isFocused, eye = player.ViewCamera.transform.position,
                channel = player.Media.SelectedChannel.ToString(),
                cameraForward = player.ViewCamera.transform.forward, emitterPosition = source.transform.position,
                userVolume = player.Media.Volume, spatialBlend = source.spatialBlend,
                minimumDistance = source.minDistance, maximumDistance = source.maxDistance,
                rolloffMode = source.rolloffMode.ToString(), listenerVolume = AudioListener.volume,
                listenerPaused = AudioListener.pause, outputSampleRate = AudioSettings.outputSampleRate,
                listenerPresent = player.ViewCamera.GetComponent<AudioListener>()
                    && player.ViewCamera.GetComponent<AudioListener>().isActiveAndEnabled,
                nativeGainMinimum = float.MaxValue, screenName = screen.name,
                screenBoundsCenter = screen.bounds.center, screenBoundsSize = screen.bounds.size,
                screenTransformPosition = screen.transform.position, screenTransformScale = screen.transform.lossyScale,
                staticBatched = screen.isPartOfStaticBatch
            };
            MeshFilter mesh = screen.GetComponent<MeshFilter>();
            if (mesh && mesh.sharedMesh)
            {
                pose.screenMeshBoundsCenter = mesh.sharedMesh.bounds.center;
                pose.screenMeshBoundsSize = mesh.sharedMesh.bounds.size;
            }
            Vector3 centre = player.Media.SelectedChannel == SlopCityMediaChannel.Record
                ? source.transform.position : screen.bounds.center;
            Vector3 delta = centre - pose.eye;
            pose.distance = delta.magnitude;
            pose.gaze = Vector3.Dot(pose.cameraForward, delta.normalized);
            pose.facing = player.Media.SelectedChannel == SlopCityMediaChannel.Record ? 1f
                : Vector3.Dot(screen.transform.TransformDirection(Vector3.back), -delta.normalized);
            pose.audioScore = ReadMediaField<float>("audioScore");
            var hits = new List<string>();
            foreach (RaycastHit hit in Physics.RaycastAll(pose.eye, delta.normalized, Mathf.Max(0f, pose.distance - .15f),
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                hits.Add(hit.transform.name + "@" + hit.distance.ToString("F3"));
            pose.occlusionHits = hits.ToArray();
            audioPoses.Add(pose);
            float[] samples = new float[1024];
            deadline = Time.realtimeSinceStartupAsDouble + (expectedAudible ? 2 : .35);
            do
            {
                pose.nativeGainMinimum = Mathf.Min(pose.nativeGainMinimum, source.volume);
                pose.nativeGainMaximum = Mathf.Max(pose.nativeGainMaximum, source.volume);
                source.GetOutputData(samples, 0);
                foreach (float sample in samples)
                    pose.maxSourceSampleMagnitude = Math.Max(pose.maxSourceSampleMagnitude, Math.Abs(sample));
                if (string.IsNullOrEmpty(pose.listenerSampleError))
                {
                    try
                    {
                        AudioListener.GetOutputData(samples, 0);
                        pose.listenerSamplesRead = true;
                        foreach (float sample in samples)
                            pose.maxListenerSampleMagnitude = Math.Max(pose.maxListenerSampleMagnitude, Math.Abs(sample));
                    }
                    catch (Exception exception) { pose.listenerSampleError = exception.Message; }
                }
                pose.sampleFrames++;
                yield return null;
            } while (Time.realtimeSinceStartupAsDouble < deadline);
            pose.playing = player.Media.IsPlaying;
            pose.passed = pose.seated && !player.IsPaused && (expectedAudible
                ? pose.playing && pose.listenerPresent && !pose.listenerPaused && pose.nativeGainMinimum > .08f && pose.maxSourceSampleMagnitude > .00001d
                    && (!pose.listenerSamplesRead || pose.maxListenerSampleMagnitude > .00001d)
                : pose.nativeGainMaximum <= .0001f);
            if (!pose.passed) errors.Add(label + " failed / gain=" + pose.nativeGainMinimum + ".." + pose.nativeGainMaximum
                + " sourceSamples=" + pose.maxSourceSampleMagnitude + " listenerSamples=" + pose.maxListenerSampleMagnitude);
            WriteReport("capturing");
            Debug.Log("SLOP_CITY_AUDIO_REVIEW " + (pose.passed ? "PASS " : "FAIL ") + label);
        }

        private void MoveToEye(Vector3 eye)
        {
            player.SetExternalMovement(false);
            player.TeleportTo(eye - Vector3.up * 1.62f, 0);
            // Use the real first-person camera without gravity changing this review pose.
            player.SetExternalMovement(true);
            input.SetGameplayEnabled(false);
        }

        private IEnumerator Measure(string label, bool expectedPaused)
        {
            double warmupEnds = Time.realtimeSinceStartupAsDouble + WarmupSeconds;
            while (Time.realtimeSinceStartupAsDouble < warmupEnds) yield return null;
            if (player.IsPaused != expectedPaused) throw new InvalidOperationException("Review interrupted by a menu change at " + label);
            var pose = new PoseReport
            {
                label = label, paused = player.IsPaused, eye = player.ViewCamera.transform.position,
                rotation = player.ViewCamera.transform.eulerAngles, width = Screen.width, height = Screen.height,
                cameraPixelWidth = player.ViewCamera.pixelWidth, cameraPixelHeight = player.ViewCamera.pixelHeight,
                focusedWholeSample = Application.isFocused, warmupSeconds = WarmupSeconds,
                actualRenderingPath = player.ViewCamera.actualRenderingPath.ToString(), hdr = player.ViewCamera.allowHDR,
                movingNpcsAtStart = SlopCityCrowd.Instance ? SlopCityCrowd.Instance.MovingCount : 0,
                screenCount = SlopCityCityMedia.Instance ? SlopCityCityMedia.Instance.ScreenCount : 0
            };
            poses.Add(pose);
            var frames = new List<double>(1200);
            double start = Time.realtimeSinceStartupAsDouble, previous = start;
            while (Time.realtimeSinceStartupAsDouble - start < SampleSeconds)
            {
                yield return null;
                double now = Time.realtimeSinceStartupAsDouble;
                frames.Add((now - previous) * 1000);
                previous = now;
                pose.focusedWholeSample &= Application.isFocused;
                if (player.IsPaused != expectedPaused) throw new InvalidOperationException("Review interrupted by a menu change at " + label);
            }
            pose.measuredSeconds = previous - start;
            pose.frames = frames.Count;
            pose.finiteTimingAndCamera = Finite(pose.eye.x) && Finite(pose.eye.y) && Finite(pose.eye.z)
                && Finite(pose.rotation.x) && Finite(pose.rotation.y) && Finite(pose.rotation.z);
            double sum = 0;
            foreach (double frame in frames) { sum += frame; pose.finiteTimingAndCamera &= Finite(frame) && frame > 0; }
            if (frames.Count == 0 || !pose.finiteTimingAndCamera) throw new InvalidOperationException("Invalid frame/camera sample at " + label);
            frames.Sort();
            pose.meanMs = sum / frames.Count;
            pose.medianMs = Percentile(frames, .5);
            pose.p95Ms = Percentile(frames, .95);
            pose.p99Ms = Percentile(frames, .99);
            pose.minimumMs = frames[0]; pose.maximumMs = frames[frames.Count - 1];
            pose.averageFps = 1000 / pose.meanMs;
            pose.p99EquivalentFps = 1000 / pose.p99Ms;
            pose.movingNpcsAtEnd = SlopCityCrowd.Instance ? SlopCityCrowd.Instance.MovingCount : 0;
            pose.screenCount = SlopCityCityMedia.Instance ? SlopCityCityMedia.Instance.ScreenCount : 0;
            yield return new WaitForEndOfFrame();
            CaptureBackbuffer(pose);
            WriteReport("capturing");
        }

        private void CaptureBackbuffer(PoseReport pose)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                RenderTexture.active = null;
                image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
                image.Apply(false, false);
                Color32[] pixels = image.GetPixels32();
                int dark = 0, white = 0, magenta = 0;
                foreach (Color32 pixel in pixels)
                {
                    if (pixel.r < 5 && pixel.g < 5 && pixel.b < 5) dark++;
                    if (pixel.r > 250 && pixel.g > 250 && pixel.b > 250) white++;
                    if (pixel.r > 230 && pixel.b > 230 && pixel.g < 35) magenta++;
                }
                pose.darkPixelFraction = (double)dark / pixels.Length;
                pose.whitePixelFraction = (double)white / pixels.Length;
                pose.magentaPixelFraction = (double)magenta / pixels.Length;
                pose.width = image.width; pose.height = image.height;
                pose.screenshot = Path.Combine(folder, pose.label + ".png");
                File.WriteAllBytes(pose.screenshot, image.EncodeToPNG());
                pose.screenshotSaved = File.Exists(pose.screenshot) && new FileInfo(pose.screenshot).Length > 0;
            }
            finally
            {
                RenderTexture.active = previous;
                if (image) Destroy(image);
            }
        }

        private void ReadPipelineEvidence()
        {
            Camera camera = player.ViewCamera;
            Shader shader = Shader.Find("Hidden/SlopCity/CameraFinish");
            var finish = camera.GetComponent<SlopCityCameraFinish>();
            report.finishShaderFound = shader;
            report.finishShaderSupported = shader && shader.isSupported;
            report.finishEnabled = finish && finish.isActiveAndEnabled;
            report.actualRenderingPath = camera.actualRenderingPath.ToString();
            report.actualDeferred = camera.actualRenderingPath == RenderingPath.DeferredShading;
            report.hdrEnabled = camera.allowHDR;
            report.npcPopulation = SlopCityCrowd.Instance ? SlopCityCrowd.Instance.PopulationCount : 0;
            report.cityScreens = SlopCityCityMedia.Instance ? SlopCityCityMedia.Instance.ScreenCount : 0;
            report.expectedPopulationObserved = report.npcPopulation == 432;
            report.expectedScreensObserved = report.cityScreens == 61;
        }

        private void Finish(string status)
        {
            if (finishing || completed) return;
            finishing = true;
            try { ReturnToCouchAndPause(); }
            catch (Exception exception) { errors.Add("Return to paused couch: " + exception.Message); }
            TryRestore(true);
            completed = true;
            finishing = false;
            report.completedUtc = DateTime.UtcNow.ToString("o");
            report.returnedToCouchPaused = player && player.IsSeated && player.IsPaused;
            try { if (!audioReview) ReadPipelineEvidenceIfAvailable(); }
            catch (Exception exception) { errors.Add("Final pipeline evidence: " + exception.Message); }
            WriteReport(status);
            Debug.Log((audioReview ? "SLOP_CITY_AUDIO_REVIEW_COMPLETE " : "SLOP_CITY_GRAPHICS_REVIEW_COMPLETE ") + reportPath + " status=" + status);
            if (audioReview)
            {
                Application.runInBackground = priorRunInBackground;
                Application.Quit(audioReport.passed ? 0 : 1);
            }
        }

        private void ReturnToCouchAndPause()
        {
            if (!player) return;
            player.SetExternalMovement(false);
            player.RespawnAtCouch();
            // Existing pause implementation also suspends media, unlocks the cursor and draws the real menu.
            typeof(SlopCityFirstPerson).GetMethod("SetPaused", PrivateInstance)?.Invoke(player, new object[] { true });
        }

        private void RestoreLiveState()
        {
            if (player && player.Sandbox)
            {
                FieldInfo field = typeof(SlopCitySandbox).GetField("progress", PrivateInstance);
                if (field == null) throw new MissingFieldException("Sandbox progress field changed; disk backup retained.");
                object original = string.IsNullOrEmpty(saved.progress) ? Activator.CreateInstance(field.FieldType, true)
                    : JsonUtility.FromJson(saved.progress, field.FieldType);
                field.SetValue(player.Sandbox, original);
                var discoveries = typeof(SlopCitySandbox).GetField("discoveries", PrivateInstance)?.GetValue(player.Sandbox) as HashSet<string>;
                if (discoveries != null)
                {
                    discoveries.Clear();
                    var ids = field.FieldType.GetField("discovered")?.GetValue(original) as string[];
                    if (ids != null) foreach (string id in ids) discoveries.Add(id);
                }
            }
            var media = SlopCityCityMedia.Instance;
            if (media)
            {
                FieldInfo field = typeof(SlopCityCityMedia).GetField("progress", PrivateInstance);
                if (field == null) throw new MissingFieldException("Playlist progress field changed; disk backup retained.");
                byte[] bytes = saved.files[0].existed ? Convert.FromBase64String(saved.files[0].base64) : null;
                object original = bytes == null ? Activator.CreateInstance(field.FieldType, true)
                    : JsonUtility.FromJson(System.Text.Encoding.UTF8.GetString(bytes), field.FieldType);
                field.SetValue(media, original);
            }
        }

        private void Update()
        {
            if (!completed || !protectedSession || quitting) return;
            // A deliberate resume ends diagnostic ownership; later genuine play is allowed to save normally.
            if (player && !player.IsPaused)
            {
                protectedSession = false;
                Application.wantsToQuit -= BeforeQuit;
                DeleteRecovery();
                return;
            }
            if (Time.unscaledTime < nextRestore) return;
            nextRestore = Time.unscaledTime + .5f;
            // An in-flight decoder preparation can finish after pausing. Undo only its diagnostic playlist save.
            TryRestore(false);
        }

        private bool BeforeQuit()
        {
            if (!protectedSession) return true;
            quitting = true;
            StopAllCoroutines();
            if (!completed) { errors.Add("Application quit before all poses completed."); Finish("interrupted"); }
            TryRestore(true);
            return true;
        }

        private void OnApplicationQuit()
        {
            if (!protectedSession) return;
            quitting = true;
            TryRestore(true);
            if (report != null && report.savedProgressRestored) DeleteRecovery();
        }

        private void OnDestroy()
        {
            Application.wantsToQuit -= BeforeQuit;
            if (protectedSession) TryRestore(true);
        }

        private void TryRestore(bool restoreLive)
        {
            try
            {
                if (restoreLive) RestoreLiveState();
            }
            catch (Exception exception) { errors.Add("Live progress restore: " + exception.Message); }
            try
            {
                RestoreFilesAndPrefs();
                if (report != null) report.savedProgressRestored = true;
            }
            catch (Exception exception)
            {
                if (report != null) report.savedProgressRestored = false;
                errors.Add("Saved progress restore: " + exception.Message);
                Debug.LogError("SLOP CITY graphics review recovery snapshot retained: " + exception.Message);
            }
        }

        private void RestoreFilesAndPrefs()
        {
            if (saved == null) return;
            if (saved.hasProgress) PlayerPrefs.SetString(SaveKey, saved.progress);
            else PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
            foreach (SavedFile file in saved.files)
            {
                if (!file.existed) { if (File.Exists(file.path)) File.Delete(file.path); continue; }
                byte[] bytes = Convert.FromBase64String(file.base64);
                if (File.Exists(file.path) && SameBytes(File.ReadAllBytes(file.path), bytes)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(file.path));
                File.WriteAllBytes(file.path, bytes);
            }
        }

        private void DeleteRecovery()
        {
            try { if (File.Exists(recoveryPath)) File.Delete(recoveryPath); }
            catch (Exception exception) { Debug.LogWarning("Graphics review recovery cleanup: " + exception.Message); }
        }

        private void ReadPipelineEvidenceIfAvailable() { if (player && player.ViewCamera) ReadPipelineEvidence(); }
        private void WriteReport(string status)
        {
            if (report == null) return;
            if (audioReview)
            {
                audioReport.status = status;
                audioReport.completedUtc = completed ? DateTime.UtcNow.ToString("o") : "";
                audioReport.savedProgressRestored = report.savedProgressRestored;
                audioReport.returnedToCouchPaused = report.returnedToCouchPaused;
                audioReport.poses = audioPoses.ToArray(); audioReport.errors = errors.ToArray();
                audioReport.passed = status == "complete" && errors.Count == 0 && audioReport.standalone
                    && audioReport.savedProgressRestored && audioReport.returnedToCouchPaused && audioPoses.Count == 10;
                foreach (AudioPoseReport pose in audioPoses) audioReport.passed &= pose.passed;
                try { File.WriteAllText(reportPath, JsonUtility.ToJson(audioReport, true)); }
                catch (Exception exception) { Debug.LogError("Audio review report: " + exception.Message); }
                return;
            }
            report.status = status;
            report.poses = poses.ToArray(); report.errors = errors.ToArray();
            report.requiredEvidencePassed = status == "complete" && errors.Count == 0 && report.standalone
                && report.finishShaderFound && report.finishShaderSupported && report.finishEnabled
                && report.actualDeferred && report.hdrEnabled && report.expectedPopulationObserved
                && report.expectedScreensObserved && report.savedProgressRestored && report.returnedToCouchPaused
                && poses.Count == 4;
            foreach (PoseReport pose in poses)
                report.requiredEvidencePassed &= pose.screenshotSaved && pose.finiteTimingAndCamera
                    && pose.frames >= 30 && pose.measuredSeconds >= SampleSeconds;
            try { File.WriteAllText(reportPath, JsonUtility.ToJson(report, true)); }
            catch (Exception exception) { Debug.LogError("Graphics review report: " + exception.Message); }
        }

        private static SavedFile SnapshotFile(string path) => new SavedFile
        { path = path, existed = File.Exists(path), base64 = File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : "" };
        private static bool SameBytes(byte[] a, byte[] b)
        { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double Percentile(List<double> sorted, double p)
            => sorted[Mathf.Clamp((int)Math.Ceiling(sorted.Count * p) - 1, 0, sorted.Count - 1)];

        [Serializable] private sealed class AudioReviewReport
        {
            public string scope = "Explicit final standalone native player couch TV/record audio routing and sample checks. "
                + "Uses the real seated camera, TV renderer and physical Turntable; foreground/pause callbacks are simulated "
                + "for an owned unattended review, with background updates enabled during the review. "
                + "Native gain and AudioSource/listener samples are measured; physical speaker playback is not listened to. "
                + "Normal quit/error restores progress and playlist files; force-killed runs retain save-recovery.json.";
            public string status, startedUtc, completedUtc, applicationVersion, unityVersion, operatingSystem;
            public bool standalone, actualApplicationFocusAtStart, foregroundCallbacksSimulated, runInBackgroundEnabledForReview,
                savedProgressRestored, returnedToCouchPaused, passed;
            public AudioPoseReport[] poses;
            public string[] errors;
        }
        [Serializable] private sealed class AudioPoseReport
        {
            public string label, channel, screenName, rolloffMode, listenerSampleError;
            public string[] occlusionHits;
            public bool expectedAudible, seated, playing, actualApplicationFocus, listenerPaused, listenerPresent,
                staticBatched, listenerSamplesRead, passed;
            public Vector3 eye, cameraForward, emitterPosition, screenBoundsCenter, screenBoundsSize,
                screenTransformPosition, screenTransformScale, screenMeshBoundsCenter, screenMeshBoundsSize;
            public float userVolume, nativeGainMinimum, nativeGainMaximum, spatialBlend, minimumDistance,
                maximumDistance, distance, gaze, facing, audioScore, listenerVolume;
            public int sampleFrames, outputSampleRate;
            public double maxSourceSampleMagnitude, maxListenerSampleMagnitude;
        }

        [Serializable] private sealed class SaveSnapshot
        { public string capturedUtc, progress; public bool hasProgress; public SavedFile[] files; }
        [Serializable] private sealed class SavedFile
        { public string path, base64; public bool existed; }
        [Serializable] private sealed class PoseReport
        {
            public string label, actualRenderingPath, screenshot;
            public Vector3 eye, rotation;
            public int width, height, cameraPixelWidth, cameraPixelHeight, frames, movingNpcsAtStart, movingNpcsAtEnd, screenCount;
            public bool paused, hdr, focusedWholeSample, finiteTimingAndCamera, screenshotSaved;
            public double warmupSeconds, measuredSeconds, meanMs, medianMs, p95Ms, p99Ms, minimumMs, maximumMs,
                averageFps, p99EquivalentFps, darkPixelFraction, whitePixelFraction, magentaPixelFraction;
        }
        [Serializable] private sealed class ReviewReport
        {
            public string scope = "Explicit standalone player screenshots including native HUD; real unscaled frame intervals after warmup. "
                + "Frame intervals include VSync, frame caps, CPU, GPU and OS scheduling; no isolated GPU time is claimed. "
                + "PNG pixel fractions describe display output and cannot prove HDR finiteness before tone mapping. "
                + "Supported shaders and configured content counts do not establish subjective AAA quality. "
                + "Normal quits/errors restore saved progress; force-killed runs retain save-recovery.json for the next explicit review launch.";
            public string status, startedUtc, completedUtc, applicationVersion, unityVersion, operatingSystem, processor,
                graphicsDevice, graphicsApi, graphicsVersion, fullscreenMode, colorSpace, actualRenderingPath;
            public int processorCount, systemMemoryMB, graphicsMemoryMB, shaderLevel, screenWidth, screenHeight,
                displayWidth, displayHeight, vSyncCount, targetFrameRate, qualityLevel, npcPopulation, cityScreens;
            public double displayRefreshHz;
            public bool standalone, supportsInstancing, finishShaderFound, finishShaderSupported, finishEnabled,
                actualDeferred, hdrEnabled, expectedPopulationObserved, expectedScreensObserved,
                savedProgressRestored, returnedToCouchPaused, requiredEvidencePassed;
            public PoseReport[] poses;
            public string[] errors;
        }
    }
}
