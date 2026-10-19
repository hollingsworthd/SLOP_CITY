#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Graphics-enabled, real-time repeated street traversal through production walking input.</summary>
    [InitializeOnLoad]
    public static class SlopCityRapidTraversalSmokeTest
    {
        private const string Active = "SlopCity.RapidTraversal.Active", Started = Active + ".Started";
        private const string Company = "SlopCityValidation", Product = "RapidTraversalSmoke";
        private const string Scene = "Assets/Scenes/SlopCity_CouchSpawn.unity";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const float MaximumFrameTravel = .75f;
        private const float MinimumMeanMovingSpeed = 11f; // Twice the production 5.5m/s sprint.
        private static Receipt report;
        private static SlopCityFirstPerson player;
        private static SlopCityPlayerInput input;
        private static CharacterController capsule;
        private static SlopCityBuildingWorld world;
        private static SlopCityHintRoute route;
        private static readonly List<Checkpoint> checkpoints = new List<Checkpoint>(18);
        private static string output;
        private static int waypoint, destination, completedLaps, lastFrame = -1, segmentJumps;
        private static double enteredAt, segmentAt, planningAt, progressAt, nextReceipt, nextStallDiagnostic, lastJumpAt = double.NegativeInfinity, movingSeconds, movingDistance, lapStartedSeconds, lapStartedDistance;
        private static Vector3 previousPosition, progressPosition;
        private static float oldSprint, previousFrameSeconds;
        private static bool oldInput, oldBackground, initialized, finishing, priorCaptured;
        private static bool previousWasMoving, sampledMovingFrame, lapPlaybackObserved;
        private static FieldInfo sprintField, yawField, pitchField;

        static SlopCityRapidTraversalSmokeTest()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Active, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (state == PlayModeStateChange.EnteredEditMode) Finish(new InvalidOperationException("Rapid traversal Play Mode ended early."));
            };
            Application.logMessageReceived += CaptureLog;
        }

        // Dedicated copied project only; graphics enabled; omit -quit. Run exits after its receipt.
        public static void Run()
        {
            // Check identities BEFORE opening the scene or inspecting any persistence.
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode
                || PlayerSettings.companyName != Company || PlayerSettings.productName != Product
                || Application.companyName != Company || Application.productName != Product)
                throw new InvalidOperationException("Requires a dedicated isolated project with company SlopCityValidation and product RapidTraversalSmoke.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Rapid traversal requires graphics; omit -nographics.");
            if (Application.unityVersion != "6000.3.26f1")
                throw new InvalidOperationException("Rapid traversal requires the pinned Unity 6000.3.26f1 Editor.");
            output = Path.GetFullPath(Argument("-slopCityRapidReport", "Builds/Reports/rapid-traversal-check.json"));
            if (File.Exists(output)) throw new IOException("Refusing to overwrite a previous rapid traversal receipt.");
            report = new Receipt
            {
                startedAtUtc = DateTime.UtcNow.ToString("O"), status = "initializing", unityVersion = Application.unityVersion,
                company = Company, product = Product, persistentDataPath = Application.persistentDataPath,
                scenePath = Scene, graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName,
                requestedLaps = IntegerArgument("-slopCityRapidLaps", 2, 1, 10),
                requestedSpeed = FloatArgument("-slopCityRapidSpeed", 55f, 5.5f, 110f),
                maximumSeconds = IntegerArgument("-slopCityRapidSeconds", 480, 60, 600),
                maximumCommandedFrameTravel = MaximumFrameTravel,
                minimumMeanMovingSpeed = MinimumMeanMovingSpeed,
                limitations = "Native Editor graphics/decoders; not WebGL, WebKit or physical-device acceptance. One declared setup teleport; no lap teleports. Production simulation time and media remain enabled. Dedicated QA persistence identifiers only."
            };
            SessionState.SetString(Started, report.startedAtUtc);
            SessionState.SetString(Active + ".Output", output);
            SessionState.SetBool(Active + ".Background", Application.runInBackground);
            SessionState.SetBool(Active, true);
            WriteReport();
            try
            {
                oldBackground = Application.runInBackground;
                Application.runInBackground = true;
                EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(Active, false) || finishing) return;
            try
            {
                // Static state is rebuilt after the normal Play Mode domain reload.
                if (report == null)
                {
                    output = SessionState.GetString(Active + ".Output", "");
                    report = JsonUtility.FromJson<Receipt>(File.ReadAllText(output));
                    oldBackground = SessionState.GetBool(Active + ".Background", false);
                    Application.runInBackground = true;
                }
                double now = EditorApplication.timeSinceStartup;
                if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Started, ""), CultureInfo.InvariantCulture).ToUniversalTime()).TotalSeconds > report.maximumSeconds)
                    throw new TimeoutException("Rapid traversal exceeded its bounded overall duration.");
                if (report.runtimeErrorCount > 0) throw new InvalidOperationException("A runtime error was logged; see receipt.");
                if (now >= nextReceipt) { Sample("heartbeat"); WriteReport(); nextReceipt = now + 2d; }
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || initialized) return;
                if (now - enteredAt < 3d) return;
                player = Object.FindFirstObjectByType<SlopCityFirstPerson>();
                SlopCityCrowd crowd = SlopCityCrowd.Instance;
                if (!player || !player.Sandbox || !crowd || !crowd.LayoutRevalidated)
                {
                    if (now - enteredAt > 45d) throw new TimeoutException("Production player/crowd did not initialize.");
                    return;
                }
                Initialize(crowd);
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Initialize(SlopCityCrowd crowd)
        {
            input = player.GetComponent<SlopCityPlayerInput>(); capsule = player.GetComponent<CharacterController>();
            if (!input || !capsule || !SlopCityCityMedia.Instance) throw new InvalidOperationException("Missing production input, capsule or city media.");
            sprintField = typeof(SlopCityFirstPerson).GetField("sprintingSpeed", Private);
            yawField = typeof(SlopCityFirstPerson).GetField("yaw", Private);
            pitchField = typeof(SlopCityFirstPerson).GetField("pitch", Private);
            if (sprintField == null || yawField == null || pitchField == null)
                throw new MissingFieldException("Production locomotion fixture contract changed.");
            oldSprint = (float)sprintField.GetValue(player); oldInput = input.GameplayEnabled; priorCaptured = true;
            report.originalSprintSpeed = oldSprint;
            // SetTouchMovement feeds production ReadMovement on native too.
            // Keep the scene's native control/layout mode throughout the run.
            foreach (var group in crowd.Routes.Where(item => item != null && item.nodes != null && item.districtIndex >= 0 && item.districtIndex < 9)
                .GroupBy(item => item.districtIndex).OrderBy(item => item.Key))
            {
                Vector3[] nodes = group.SelectMany(item => item.nodes).Where(item => item != null && item.neighbors != null
                    && item.neighbors.Length > 0 && Mathf.Abs(item.position.y) < 1f && Finite(item.position)).Select(item => item.position).ToArray();
                if (nodes.Length == 0) throw new InvalidOperationException("No supported outdoor route nodes for district " + group.Key);
                Vector3 center = nodes.Aggregate(Vector3.zero, (sum, point) => sum + point) / nodes.Length;
                Vector3 point = nodes.OrderBy(item => FlatDistance(item, center)).ThenBy(item => item.x).ThenBy(item => item.z).First();
                checkpoints.Add(new Checkpoint { district = group.Key, point = point });
            }
            if (checkpoints.Count != 9) throw new InvalidOperationException("All nine district route graphs are required.");
            // Serpentine streets reduce long cross-city searches. Route searches still validate every connecting segment.
            var ordered = checkpoints.OrderBy(item => item.point.z).ToArray(); checkpoints.Clear();
            for (int row = 0; row < 3; row++)
                checkpoints.AddRange(row % 2 == 0 ? ordered.Skip(row * 3).Take(3).OrderBy(item => item.point.x)
                    : ordered.Skip(row * 3).Take(3).OrderByDescending(item => item.point.x));
            // Return through adjacent districts rather than making one diagonal
            // cross-city search or silently teleporting to begin another lap.
            checkpoints.AddRange(checkpoints.Take(8).Skip(1).Reverse().ToArray());
            report.route = checkpoints.ToArray();
            player.SendMessage("OnApplicationFocus", true); player.SendMessage("OnApplicationPause", false);
            foreach (SlopCityMediaPlayer lounge in Object.FindObjectsByType<SlopCityMediaPlayer>(FindObjectsSortMode.None))
            { lounge.SendMessage("OnApplicationFocus", true); lounge.SendMessage("OnApplicationPause", false); }
            SlopCityCityMedia.Instance.SendMessage("OnApplicationFocus", true);
            SlopCityCityMedia.Instance.SendMessage("OnApplicationPause", false);
            player.PlayOrResume();
            // Sole setup relocation. Every subsequent metre uses input -> FirstPerson.Update -> ApplyMovement -> CharacterController.Move.
            player.TeleportTo(checkpoints[0].point + Vector3.up * .03f, 0f);
            if (FlatDistance(player.transform.position, checkpoints[0].point) > 1f) throw new InvalidOperationException("Declared initial outdoor setup failed.");
            report.setupTeleports = 1; report.foregroundCallbacksSimulatedAtSetup = true;
            world = Object.FindFirstObjectByType<SlopCityBuildingWorld>();
            previousPosition = progressPosition = player.transform.position; progressAt = EditorApplication.timeSinceStartup;
            initialized = true; report.status = "running"; destination = 1;
            new GameObject("RapidTraversalSmoke_ProductionInputDriver").AddComponent<SlopCityRapidTraversalDriver>();
            BeginSegment(); Sample("setup"); WriteReport();
        }

        private static void BeginSegment()
        {
            input.SetTouchMovement(Vector2.zero); input.RequestSprint(false);
            report.commandedSpeed = 0f;
            segmentJumps = 0;
            route = new SlopCityHintRoute();
            route.Begin(player.transform.position, checkpoints[destination].point, player.transform, null);
            waypoint = 1; segmentAt = planningAt = progressAt = EditorApplication.timeSinceStartup;
            progressPosition = player.transform.position; report.destinationDistrict = checkpoints[destination].district;
        }

        // Called before the production controller Update so the current frame's deltaTime bounds input velocity.
        internal static void DriveFrame()
        {
            if (!initialized || finishing || !SessionState.GetBool(Active, false) || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                if (player.IsPaused || player.IsSeated || !capsule.enabled || !input.GameplayEnabled)
                    throw new InvalidOperationException("Production walking was interrupted; refusing to skip or teleport.");
                if (Mathf.Abs(Time.timeScale - 1f) > .0001f) throw new InvalidOperationException("Simulation timeScale must stay exactly one.");
                double now = EditorApplication.timeSinceStartup;
                Vector3 current = player.transform.position;
                float traveled = FlatDistance(current, previousPosition);
                report.distanceMetres += traveled; report.maximumObservedFrameTravel = Mathf.Max(report.maximumObservedFrameTravel, traveled);
                if (traveled > 8f || current.y < -2f || current.y > 2f) throw new InvalidOperationException("Unexpected displacement or departure from supported street height.");
                previousPosition = current;
                sampledMovingFrame = previousWasMoving;
                previousWasMoving = false;
                if (sampledMovingFrame && previousFrameSeconds > 0f)
                {
                    movingSeconds += previousFrameSeconds;
                    movingDistance += traveled;
                    report.achievedSpeed = traveled / previousFrameSeconds;
                    report.peakAchievedSpeed = Mathf.Max(report.peakAchievedSpeed, report.achievedSpeed);
                }
                else report.achievedSpeed = 0f;
                report.movingSeconds = movingSeconds;
                report.movingDistanceMetres = movingDistance;
                report.meanAchievedSpeed = movingSeconds > 0 ? movingDistance / movingSeconds : 0;
                if (now - segmentAt > 45d) throw new TimeoutException("Street segment exceeded 45 seconds; no route skip is allowed.");
                if (!route.Finished)
                {
                    input.SetTouchMovement(Vector2.zero); input.RequestSprint(false);
                    route.Step(40);
                    if (now - planningAt > 15d) throw new TimeoutException("Supported street route planning exceeded fifteen seconds.");
                    if (!route.Finished) return;
                    if (!route.ReachedTarget || route.Points.Count < 2) throw new InvalidOperationException("No supported route to district " + report.destinationDistrict);
                    report.plannedSegments++; progressAt = now;
                }
                while (waypoint < route.Points.Count && FlatDistance(current, route.Points[waypoint]) <= .12f) waypoint++;
                if (waypoint >= route.Points.Count)
                {
                    if (FlatDistance(current, checkpoints[destination].point) > 2.7f) throw new InvalidOperationException("Route ended away from its declared checkpoint.");
                    report.checkpointsReached++;
                    if (destination == 0) { completedLaps++; report.completedLaps = completedLaps; }
                    Sample("checkpoint");
                    if (destination == 0)
                    {
                        var lap = new LapReceipt
                        {
                            lap = completedLaps, distanceMetres = movingDistance - lapStartedDistance,
                            movingSeconds = movingSeconds - lapStartedSeconds, mediaPlaybackObserved = lapPlaybackObserved
                        };
                        lap.meanMovingSpeed = lap.movingSeconds > 0 ? lap.distanceMetres / lap.movingSeconds : 0;
                        lap.minimumSpeedMet = lap.meanMovingSpeed >= MinimumMeanMovingSpeed;
                        lap.success = lap.minimumSpeedMet && lap.mediaPlaybackObserved;
                        report.laps.Add(lap); WriteReport();
                        if (!lap.success) throw new InvalidOperationException("Lap " + completedLaps
                            + " did not meet rapid traversal acceptance: mean moving speed "
                            + lap.meanMovingSpeed.ToString("F2", CultureInfo.InvariantCulture) + " m/s (minimum 11 m/s), playing native video observed=" + lap.mediaPlaybackObserved);
                        lapStartedDistance = movingDistance; lapStartedSeconds = movingSeconds; lapPlaybackObserved = false;
                    }
                    WriteReport();
                    if (completedLaps >= report.requestedLaps) { Finish(null); return; }
                    destination = (destination + 1) % checkpoints.Count; BeginSegment(); return;
                }
                // Coalesce collinear/clear grid nodes only through the solver's
                // real capsule sweep; preserve corners and physical obstructions.
                while (waypoint + 1 < route.Points.Count && FlatDistance(current, route.Points[waypoint + 1]) <= 4f
                    && Collinear(current, route.Points[waypoint], route.Points[waypoint + 1])
                    && route.ClearWalk(current, route.Points[waypoint + 1])) waypoint++;
                if (FlatDistance(current, progressPosition) > .25f) { progressAt = now; progressPosition = current; }
                else
                {
                    if (now - progressAt > .75d && now >= nextStallDiagnostic)
                    { CaptureStall(); TryJumpLowObstacle(now); WriteReport(); nextStallDiagnostic = now + 1d; }
                    if (now - progressAt > 6d)
                    { CaptureStall(); throw new TimeoutException("Production capsule stalled for six seconds; see stall diagnostics. No teleport recovery is allowed."); }
                }
                Vector3 direction = route.Points[waypoint] - current; direction.y = 0;
                float dt = Mathf.Max(Time.deltaTime, .0001f);
                float speed = Mathf.Min(Mathf.Min(report.requestedSpeed, MaximumFrameTravel / dt), Mathf.Max(0f, direction.magnitude - .02f) / dt);
                float multiplier = player.Sandbox ? Mathf.Max(.01f, player.Sandbox.SpeedMultiplier) : 1f;
                sprintField.SetValue(player, speed / multiplier);
                yawField.SetValue(player, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
                pitchField.SetValue(player, 0f);
                input.ResetContinuousTouch(); input.SetTouchMovement(Vector2.up); input.RequestSprint(true);
                previousFrameSeconds = Time.deltaTime; previousWasMoving = true;
                report.commandedSpeed = speed;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Sample(string reason)
        {
            if (report == null) return;
            long allocated = Profiler.GetTotalAllocatedMemoryLong(), reserved = Profiler.GetTotalReservedMemoryLong(), managed = Profiler.GetMonoUsedSizeLong();
            var detail = Object.FindObjectsByType<SlopCityDetailStreaming>(FindObjectsSortMode.None);
            var players = Object.FindObjectsByType<VideoPlayer>(FindObjectsSortMode.None);
            SlopCityCityMedia cityMedia = SlopCityCityMedia.Instance;
            SlopCityMediaPlayer loungeMedia = player ? player.Media : null;
            SlopCityTikTokSync sync = SlopCityTikTokSync.Instance;
            var sample = new SampleReceipt
            {
                atUtc = DateTime.UtcNow.ToString("O"), reason = reason, lap = completedLaps, destinationDistrict = report.destinationDistrict,
                position = player ? player.transform.position : Vector3.zero, distanceMetres = report.distanceMetres,
                allocatedBytes = allocated, reservedBytes = reserved, managedBytes = managed,
                activeFloors = world ? world.ActiveFloorCount : 0, activeElevators = world ? world.ActiveElevatorCount : 0,
                detailResidentBytes = detail.Sum(item => item.ResidentBytes), pendingDetails = detail.Count(item => item.PendingIndex >= 0),
                nativeVideoPlayers = players.Length, playingVideos = players.Count(item => item.isPlaying),
                meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length, materials = Resources.FindObjectsOfTypeAll<Material>().Length,
                renderTextures = Resources.FindObjectsOfTypeAll<RenderTexture>().Length, timeScale = Time.timeScale,
                actualSpeed = report.achievedSpeed, commandedSpeed = report.commandedSpeed,
                cityMediaStatus = cityMedia ? cityMedia.Status : "absent", loungeMediaStatus = loungeMedia ? loungeMedia.Status : "absent",
                catalogStatus = sync ? sync.Status : "absent",
                cityMediaSuspension = cityMedia ? MediaSuspension(cityMedia) : "absent",
                loungeMediaSuspension = loungeMedia ? MediaSuspension(loungeMedia) : "absent",
                videos = players.Take(4).Select(item => new VideoReceipt { hierarchy = Hierarchy(item.transform), source = item.url,
                    prepared = item.isPrepared, playing = item.isPlaying, frame = item.frame, time = item.time }).ToArray()
            };
            report.peakAllocatedBytes = Math.Max(report.peakAllocatedBytes, allocated); report.peakReservedBytes = Math.Max(report.peakReservedBytes, reserved);
            report.peakManagedBytes = Math.Max(report.peakManagedBytes, managed); report.peakDetailResidentBytes = Math.Max(report.peakDetailResidentBytes, sample.detailResidentBytes);
            report.peakFloors = Math.Max(report.peakFloors, sample.activeFloors); report.peakElevators = Math.Max(report.peakElevators, sample.activeElevators);
            report.peakVideoPlayers = Math.Max(report.peakVideoPlayers, players.Length); report.peakMeshes = Math.Max(report.peakMeshes, sample.meshes);
            report.peakPlayingVideos = Math.Max(report.peakPlayingVideos, sample.playingVideos);
            sample.duringTraversalMovement = initialized && !finishing && route != null && route.Finished
                && (previousWasMoving || sampledMovingFrame);
            if (sample.duringTraversalMovement && sample.playingVideos > 0)
            { report.mediaPlaybackObserved = true; lapPlaybackObserved = true; }
            report.peakMaterials = Math.Max(report.peakMaterials, sample.materials); report.peakRenderTextures = Math.Max(report.peakRenderTextures, sample.renderTextures);
            if (report.samples.Count >= 384) { report.samples.RemoveAt(0); report.droppedSamples++; }
            report.samples.Add(sample);
        }

        private static string MediaSuspension(Component media)
        {
            string[] names = { "menuSuspended", "focusSuspended", "applicationSuspended" };
            return string.Join(", ", names.Select(name => name + "=" + media.GetType().GetField(name, Private)?.GetValue(media)));
        }

        private static void CaptureStall()
        {
            Vector3 current = player.transform.position, target = route.Points[waypoint];
            Vector3 center = player.transform.TransformPoint(capsule.center), up = player.transform.up;
            float half = Mathf.Max(0, capsule.height * .5f - capsule.radius);
            Vector3 bottom = center - up * half, top = center + up * half;
            Vector3 direction = target - current; direction.y = 0;
            SlopCityDoor door = route.NearbyDoor(current, target);
            RaycastHit[] casts = Physics.CapsuleCastAll(bottom, top, capsule.radius, direction.normalized,
                Mathf.Min(3f, direction.magnitude + 1f), ~0, QueryTriggerInteraction.Ignore);
            Collider[] overlaps = Physics.OverlapCapsule(bottom, top, capsule.radius + capsule.skinWidth, ~0, QueryTriggerInteraction.Ignore);
            report.stallDiagnosticCount++;
            report.lastStall = new StallReceipt
            {
                atUtc = DateTime.UtcNow.ToString("O"), current = current, target = target,
                destination = checkpoints[destination].point, destinationDistrict = report.destinationDistrict,
                waypoint = waypoint, pathPoints = route.Points.Count, distanceToTarget = direction.magnitude,
                capsuleRadius = capsule.radius, capsuleHeight = capsule.height, skinWidth = capsule.skinWidth,
                capsuleCenter = capsule.center, grounded = capsule.isGrounded, collisionFlags = capsule.collisionFlags.ToString(),
                solverClearWalk = route.ClearWalk(current, target), nearbyDoor = door ? Hierarchy(door.transform) : "",
                nearbyDoorOpen = door && door.IsOpen, nearbyDoorMoving = door && door.IsMoving,
                nearbyDoorInteraction = door ? door.InteractionPoint : Vector3.zero,
                nearbyPath = route.Points.Skip(Mathf.Max(0, waypoint - 2)).Take(5).ToArray(),
                casts = casts.Where(hit => hit.collider && !hit.collider.transform.IsChildOf(player.transform))
                    .OrderBy(hit => hit.distance).Take(24).Select(hit => ColliderDiagnostic(hit.collider, hit.distance, hit.point, hit.normal)).ToArray(),
                overlaps = overlaps.Where(item => item && !item.transform.IsChildOf(player.transform))
                    .Take(24).Select(item => ColliderDiagnostic(item, 0, Vector3.zero, Vector3.zero)).ToArray()
            };
        }

        private static void TryJumpLowObstacle(double now)
        {
            // HintRoute permits supported rises up to .34m; the authored capsule
            // steps only .25m. A confirmed small prop can therefore need an
            // ordinary jump. Keep collision, gravity and the six-second deadline.
            if (!capsule.isGrounded || now - lastJumpAt < 1.5d || segmentJumps >= 4 || report.jumpCount >= 64 || report.lastStall == null) return;
            ColliderReceipt obstacle = report.lastStall.casts.FirstOrDefault(hit => string.IsNullOrEmpty(hit.door)
                && hit.distance <= .4f && Mathf.Abs(hit.normal.y) < .5f
                && hit.boundsCenter.y + hit.boundsSize.y * .5f - player.transform.position.y > .1f
                && hit.boundsCenter.y + hit.boundsSize.y * .5f - player.transform.position.y <= .65f
                && hit.boundsSize.y <= 1f && hit.boundsSize.x <= 3f && hit.boundsSize.z <= 3f);
            if (obstacle == null) return;
            input.RequestJump(); // Same request consumed by production FirstPerson.Update.
            lastJumpAt = now; segmentJumps++; report.jumpCount++;
            report.jumps.Add(new JumpReceipt { atUtc = DateTime.UtcNow.ToString("O"), lap = completedLaps + 1,
                destinationDistrict = report.destinationDistrict, waypoint = waypoint, position = player.transform.position,
                obstacle = obstacle, requestedThroughProductionInput = true });
        }

        private static ColliderReceipt ColliderDiagnostic(Collider item, float distance, Vector3 point, Vector3 normal)
        {
            SlopCityDoor door = item.GetComponentInParent<SlopCityDoor>();
            return new ColliderReceipt { hierarchy = Hierarchy(item.transform), type = item.GetType().Name,
                distance = distance, point = point, normal = normal, boundsCenter = item.bounds.center, boundsSize = item.bounds.size,
                door = door ? Hierarchy(door.transform) : "", doorOpen = door && door.IsOpen };
        }

        private static string Hierarchy(Transform item)
        {
            string value = item.name;
            for (int depth = 0; item.parent && depth < 12; depth++) { item = item.parent; value = item.name + "/" + value; }
            return value;
        }

        private static void CaptureLog(string message, string trace, LogType type)
        {
            if (!SessionState.GetBool(Active, false) || finishing || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            if (report == null)
            {
                output = SessionState.GetString(Active + ".Output", "");
                if (File.Exists(output)) report = JsonUtility.FromJson<Receipt>(File.ReadAllText(output));
                if (report == null) return;
                oldBackground = SessionState.GetBool(Active + ".Background", false);
            }
            string diagnostic = message + "\n" + trace;
            bool editorSearchStartup = !initialized && message.StartsWith("ArgumentOutOfRangeException:", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(trace) && trace.Contains("UnityEditor.Search.SearchDatabase")
                && trace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup") && !trace.Contains("SlopCity.");
            // This exact Editor-only index startup fault is separately classified
            // by existing smoke fixtures. Any game stack, later occurrence or
            // different error remains fatal to traversal acceptance.
            if (editorSearchStartup)
            {
                report.editorDiagnosticCount++;
                if (report.editorDiagnostics.Count < 24) report.editorDiagnostics.Add(diagnostic.Substring(0, Math.Min(6000, diagnostic.Length)));
                return;
            }
            report.runtimeErrorCount++;
            if (report.runtimeErrors.Count < 24) report.runtimeErrors.Add(diagnostic.Substring(0, Math.Min(6000, diagnostic.Length)));
        }

        private static void Finish(Exception error)
        {
            if (finishing) return; finishing = true;
            SessionState.SetBool(Active, false);
            try
            {
                if (priorCaptured && input) { input.ResetContinuousTouch(); input.SetGameplayEnabled(oldInput); }
                if (priorCaptured && player) sprintField.SetValue(player, oldSprint);
                Application.runInBackground = oldBackground;
                if (report != null)
                {
                    report.success = error == null && report.completedLaps == report.requestedLaps && report.runtimeErrorCount == 0
                        && report.mediaPlaybackObserved && report.laps.Count == report.requestedLaps
                        && report.laps.All(lap => lap.success);
                    report.status = report.success ? "complete" : "failed"; report.error = error?.ToString(); report.completedAtUtc = DateTime.UtcNow.ToString("O");
                    Sample("finish"); WriteReport();
                }
            }
            catch (Exception cleanup) { Debug.LogError("Rapid traversal final receipt/cleanup failed: " + cleanup); }
            Debug.Log("SLOP_CITY_RAPID_TRAVERSAL_COMPLETE " + output + " success=" + (report != null && report.success));
            EditorApplication.Exit(report != null && report.success ? 0 : 1);
        }

        private static void WriteReport()
        {
            report.updatedAtUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            string temporary = output + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(report, true));
            if (File.Exists(output)) File.Replace(temporary, output, null); else File.Move(temporary, output);
        }
        private static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name);
            if (index < 0) return fallback;
            if (index + 1 == args.Length) throw new ArgumentException("Missing value for " + name);
            return args[index + 1];
        }
        private static int IntegerArgument(string name, int fallback, int minimum, int maximum)
        {
            if (!int.TryParse(Argument(name, fallback.ToString(CultureInfo.InvariantCulture)), out int value) || value < minimum || value > maximum)
                throw new ArgumentException(name + " must be within " + minimum + ".." + maximum);
            return value;
        }
        private static float FloatArgument(string name, float fallback, float minimum, float maximum)
        {
            if (!float.TryParse(Argument(name, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                || float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum) throw new ArgumentException("Invalid " + name);
            return value;
        }
        private static bool Finite(Vector3 value) => !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude);
        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
        private static bool Collinear(Vector3 from, Vector3 middle, Vector3 to)
        {
            Vector3 a = middle - from, b = to - middle; a.y = b.y = 0;
            return a.sqrMagnitude < .0001f || Vector3.Dot(a.normalized, b.normalized) > .999f;
        }
        [Serializable] public sealed class Checkpoint { public int district; public Vector3 point; }
        [Serializable] public sealed class SampleReceipt
        {
            public string atUtc, reason; public int lap, destinationDistrict, activeFloors, activeElevators, pendingDetails, nativeVideoPlayers, playingVideos, meshes, materials, renderTextures;
            public string cityMediaStatus, loungeMediaStatus, catalogStatus, cityMediaSuspension, loungeMediaSuspension;
            public bool duringTraversalMovement;
            public Vector3 position; public double distanceMetres; public long allocatedBytes, reservedBytes, managedBytes, detailResidentBytes; public float timeScale, actualSpeed, commandedSpeed;
            public VideoReceipt[] videos;
        }
        [Serializable] public sealed class VideoReceipt
        { public string hierarchy, source; public bool prepared, playing; public long frame; public double time; }
        [Serializable] public sealed class ColliderReceipt
        {
            public string hierarchy, type, door; public bool doorOpen;
            public float distance; public Vector3 point, normal, boundsCenter, boundsSize;
        }
        [Serializable] public sealed class StallReceipt
        {
            public string atUtc, nearbyDoor, collisionFlags;
            public int destinationDistrict, waypoint, pathPoints;
            public float distanceToTarget, capsuleRadius, capsuleHeight, skinWidth;
            public bool grounded, solverClearWalk, nearbyDoorOpen, nearbyDoorMoving;
            public Vector3 current, target, destination, capsuleCenter, nearbyDoorInteraction;
            public Vector3[] nearbyPath;
            public ColliderReceipt[] casts, overlaps;
        }
        [Serializable] public sealed class JumpReceipt
        {
            public string atUtc; public int lap, destinationDistrict, waypoint;
            public Vector3 position; public ColliderReceipt obstacle; public bool requestedThroughProductionInput;
        }
        [Serializable] public sealed class LapReceipt
        {
            public int lap; public double distanceMetres, movingSeconds, meanMovingSpeed;
            public bool minimumSpeedMet, mediaPlaybackObserved, success;
        }
        [Serializable] private sealed class Receipt
        {
            public string startedAtUtc, updatedAtUtc, completedAtUtc, status, error, unityVersion, scenePath, graphicsDevice, company, product, persistentDataPath, limitations;
            public bool success, foregroundCallbacksSimulatedAtSetup, mediaPlaybackObserved;
            public int requestedLaps, completedLaps, maximumSeconds, setupTeleports, destinationDistrict, checkpointsReached, plannedSegments, runtimeErrorCount, editorDiagnosticCount, droppedSamples, stallDiagnosticCount, jumpCount;
            public int peakFloors, peakElevators, peakVideoPlayers, peakPlayingVideos, peakMeshes, peakMaterials, peakRenderTextures;
            public float requestedSpeed, originalSprintSpeed, commandedSpeed, achievedSpeed, peakAchievedSpeed, maximumObservedFrameTravel, maximumCommandedFrameTravel, minimumMeanMovingSpeed;
            public double distanceMetres, movingDistanceMetres, movingSeconds, meanAchievedSpeed;
            public long peakAllocatedBytes, peakReservedBytes, peakManagedBytes, peakDetailResidentBytes;
            public Checkpoint[] route;
            public StallReceipt lastStall;
            public List<SampleReceipt> samples = new List<SampleReceipt>();
            public List<LapReceipt> laps = new List<LapReceipt>(10);
            public List<JumpReceipt> jumps = new List<JumpReceipt>(64);
            public List<string> runtimeErrors = new List<string>();
            public List<string> editorDiagnostics = new List<string>();
        }
    }

    [DefaultExecutionOrder(-32000)]
    public sealed class SlopCityRapidTraversalDriver : MonoBehaviour
    {
        private void Update() { SlopCityRapidTraversalSmokeTest.DriveFrame(); }
    }
}
#endif
