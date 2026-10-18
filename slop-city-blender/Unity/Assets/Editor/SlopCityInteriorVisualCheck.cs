#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Five short, verified interior views in an isolated graphics Editor; no exhaustive floor traversal.</summary>
    [InitializeOnLoad]
    public static class SlopCityInteriorVisualCheck
    {
        private const string Active = "SlopCity.InteriorVisual.Active", PrefixKey = "SlopCity.InteriorVisual.Prefix";
        private static IEnumerator work;
        private static Receipt receipt;
        private static int lastFrame = -1;
        private static bool finishing;
        private static double enteredAt;
        private static string Prefix => SessionState.GetString(PrefixKey, "dusk-v0.12.0");
        private static string Folder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports");

        static SlopCityInteriorVisualCheck()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Active, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (state == PlayModeStateChange.EnteredEditMode && !finishing) Finish(new InvalidOperationException("Interior visual Play Mode ended early."));
            };
            Application.logMessageReceived += (message, trace, kind) =>
            {
                if (!SessionState.GetBool(Active, false) || receipt == null) return;
                if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert)
                {
                    if (message.Contains("Search index") || (trace ?? "").Contains("UnityEditor.Search.SearchDatabase")) receipt.editorDiagnostics.Add(message);
                    else receipt.runtimeErrors.Add(message + "\n" + trace);
                }
            };
        }

        public static void Run()
        {
            if (!Application.isBatchMode || !Application.persistentDataPath.Contains("SlopCityValidation"))
                throw new InvalidOperationException("Interior visual run requires an isolated SlopCityValidation cloned project/save identity.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Start outside Play Mode with graphics enabled.");
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-slopCityInteriorVisualPrefix");
            string prefix = index >= 0 && index + 1 < args.Length ? args[index + 1] : "dusk-v0.12.0";
            if (prefix.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '.'))) throw new ArgumentException("Invalid prefix.");
            SessionState.SetString(PrefixKey, prefix); SessionState.SetString("SlopCity.InteriorVisual.Started", DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(Active, true); receipt = NewReceipt(); Write();
            try
            {
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                DisableMedia(); EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }

        private static Receipt NewReceipt() => new Receipt { startedAtUtc = SessionState.GetString("SlopCity.InteriorVisual.Started", DateTime.UtcNow.ToString("O")),
            graphicsDevice = SystemInfo.graphicsDeviceType.ToString(), isolatedDataPath = Application.persistentDataPath };
        private static void DisableMedia()
        {
            foreach (SlopCityTikTokSync item in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None)) item.enabled = false;
            foreach (SlopCityCityMedia item in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None)) item.enabled = false;
            foreach (SlopCityMediaPlayer item in Object.FindObjectsByType<SlopCityMediaPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) item.enabled = false;
        }
        private static void Update()
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (receipt == null) receipt = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(receipt.startedAtUtc).ToUniversalTime()).TotalMinutes > 4)
                    throw new TimeoutException("Interior visual review exceeded four minutes.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                if (EditorApplication.timeSinceStartup - enteredAt < 1) return;
                if (work == null) work = Exercise();
                if (!work.MoveNext()) Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static IEnumerator Exercise()
        {
            Application.runInBackground = true; AudioListener.volume = 0;
            SlopCityFirstPerson player = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            if (!player || !player.Sandbox) throw new InvalidOperationException("Production player is missing.");
            player.SendMessage("OnApplicationFocus", true); player.SendMessage("OnApplicationPause", false);
            player.PlayOrResume(); player.StopMedia();
            player.GetComponent<SlopCityPlayerInput>().RequestInteract();
            while (player.IsSeated || !player.GetComponent<CharacterController>().enabled) yield return null;
            player.enabled = false; DisableMedia();
            Camera camera = player.ViewCamera;
            SlopCityBuildingWorld world = SlopCityBuildingWorld.Ensure(player);
            if (!world || !camera || !camera.GetComponent<SlopCityCameraFinish>().enabled) throw new InvalidOperationException("Production world/camera finish is missing.");
            SlopCityBuildingInterior[] buildings = world.Buildings.Where(item => item.IsTower).ToArray();
            var requests = new[]
            {
                new Request { label = "kitchen-wide", building = buildings.First(item => item.Purpose.StartsWith("Residential", StringComparison.Ordinal)), floor = 1, fixture = "EastSouth_Kitchen_Sink" },
                new Request { label = "citadel-archive", building = buildings.Single(item => item.BuildingId == "MilitarySF_CommunicationsCitadel"), floor = 1, fixture = "EastSouth_Catalogue_Shelf" },
                new Request { label = "market-packing", building = buildings.First(item => item.Purpose.Contains("Market")), floor = 1, fixture = "EastSouth_Workbench" },
                new Request { label = "signal-laboratory", building = buildings.First(item => item.Purpose.Contains("Signal")), floor = 1, fixture = "EastSouth_Signal_Test_Rack" },
                new Request { label = "publisher-print", building = buildings.First(item => item.Purpose.Contains("Publisher")), floor = 3, fixture = "EastSouth_Workbench" }
            };
            foreach (Request request in requests)
            {
                player.TeleportTo(request.building.LandingFeet(request.floor), request.building.transform.eulerAngles.y);
                world.RefreshStreaming();
                Transform floor = request.building.EnsureFloor(request.floor), fixture = floor.Find(request.fixture);
                if (!fixture) throw new InvalidOperationException(request.label + " expected fixture is missing: " + request.fixture);
                receipt.kitchensVerified += VerifyKitchenFacing(floor);
                Physics.SyncTransforms();
                if (!TryFrame(request.building, request.floor, floor, fixture, player.GetComponent<CharacterController>(), out Vector3 feet, out Vector3 focus))
                    throw new InvalidOperationException(request.label + " has no supported capsule-clear, unobstructed framing position.");
                player.TeleportTo(feet, request.building.transform.eulerAngles.y); world.RefreshStreaming();
                camera.transform.position = feet + Vector3.up * 1.62f; camera.transform.LookAt(focus);
                float previousFov = camera.fieldOfView; camera.fieldOfView = 72f;
                try
                {
                    for (int frame = 0; frame < 12; frame++) yield return null;
                    Physics.SyncTransforms();
                    if (!ClearFeet(feet, player.GetComponent<CharacterController>()) || !ClearView(camera.transform.position, focus, fixture, player.GetComponent<CharacterController>()))
                        throw new InvalidOperationException(request.label + " framing lost physical support/visibility while settling.");
                    string path = Path.Combine(Folder, Prefix + "-interior-review-" + request.label + ".png");
                    Capture(camera, path);
                    SlopCityInteriorFurnishing details = floor.GetComponent<SlopCityInteriorFurnishing>();
                    receipt.views.Add(new View { label = request.label, building = request.building.BuildingId, floor = request.floor,
                        fixture = request.fixture, path = path, feet = feet, cameraPosition = camera.transform.position,
                        cameraRotation = camera.transform.rotation, focus = focus, detailPieces = details ? details.DetailPieces : 0,
                        detailBatches = details ? details.BatchCount : 0, supportAndClearance = true, lineOfSight = true });
                    Write();
                }
                finally { camera.fieldOfView = previousFov; }
            }
            receipt.passed = receipt.views.Count == requests.Length && receipt.runtimeErrors.Count == 0;
        }

        /// <summary>Choose a standing view of actual room furniture, keeping the entire adult capsule and target sightline clear.</summary>
        public static bool TryFrame(SlopCityBuildingInterior building, int floor, Transform floorRoot, Transform fixture,
            CharacterController player, out Vector3 feet, out Vector3 focus)
        {
            feet = focus = default(Vector3);
            if (!building || !floorRoot || !fixture) return false;
            Renderer[] renderers = fixture.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            Vector3 center = bounds.center;
            // Worktops need their contents in frame; taller racks/shelves stay centered.
            center.y = floorRoot.position.y + (bounds.size.y < 1.3f ? 1.18f : 1.22f);
            Vector3 localTarget = floorRoot.InverseTransformPoint(center);
            Vector2 size = building.GetFloorSize(floor);
            bool east = localTarget.x > 0, south = localTarget.z < 0;
            float xMin = east ? 1.45f : -size.x / 2 + .40f, xMax = east ? size.x / 2 - .40f : -1.45f;
            float zMin = south ? -size.y / 2 + .40f : .40f, zMax = south ? -.40f : size.y / 2 - .40f;
            // Pick the side of the fixture that faces the interior of its own
            // half-floor, not the side facing its nearest boundary wall. A rack or
            // shelf at the north end needs a south-facing observer; a worktop at
            // the south end needs a north-facing observer, regardless of room ID.
            float roomMiddle = (zMin + zMax) * .5f;
            float intoRoom = localTarget.z < roomMiddle ? 1f : -1f;
            float best = float.PositiveInfinity;
            foreach (float distance in new[] { 3.8f, 3.1f, 4.6f, 2.5f, 5.5f })
                for (int direction = 0; direction < 24; direction++)
                {
                    float angle = direction * 15f * Mathf.Deg2Rad;
                    Vector3 local = new Vector3(localTarget.x + Mathf.Cos(angle) * distance, .015f,
                        localTarget.z + Mathf.Sin(angle) * distance);
                    if (local.x < xMin || local.x > xMax || local.z < zMin || local.z > zMax) continue;
                    float frontDepth = (local.z - localTarget.z) * intoRoom;
                    if (frontDepth < distance * .65f) continue;
                    Vector3 candidate = floorRoot.TransformPoint(local);
                    if (!Supported(candidate, player, out Vector3 grounded)) continue;
                    Vector3 eye = grounded + Vector3.up * 1.62f;
                    if (!ClearView(eye, center, fixture, player)) continue;
                    // Prefer a diagonal near the hall side that reveals the counter
                    // face, worktop and adjacent objects with useful perspective.
                    // A 30-degree front diagonal reveals the worktop and the
                    // shelving/rack face while retaining the surrounding room.
                    float desiredX = (east ? -1f : 1f) * distance * .5f;
                    float score = Mathf.Abs(distance - 3.8f) + Mathf.Abs((local.x - localTarget.x) - desiredX) * .5f;
                    if (score >= best) continue;
                    best = score; feet = grounded; focus = center;
                }
            return !float.IsInfinity(best);
        }

        private static bool Relevant(Collider item, CharacterController player) => item && item.enabled && !item.isTrigger
            && (!player || item != player && !item.transform.IsChildOf(player.transform));
        private static bool Supported(Vector3 candidate, CharacterController player, out Vector3 feet)
        {
            feet = candidate;
            RaycastHit[] ground = Physics.RaycastAll(candidate + Vector3.up * .35f, Vector3.down, .7f, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => Relevant(hit.collider, player) && hit.normal.y > .7f).OrderBy(hit => hit.distance).ToArray();
            if (ground.Length == 0 || Mathf.Abs(ground[0].point.y - candidate.y) > .14f) return false;
            feet.y = ground[0].point.y + .025f;
            return ClearFeet(feet, player);
        }

        private static int VerifyKitchenFacing(Transform floor)
        {
            int checkedKitchens = 0;
            foreach (SlopCityInteractable sink in floor.GetComponentsInChildren<SlopCityInteractable>(true)
                .Where(item => item.name.EndsWith("_Kitchen_Sink", StringComparison.Ordinal)))
            {
                string id = sink.name.Substring(0, sink.name.Length - "_Kitchen_Sink".Length);
                Bounds counter = LocalBounds(sink.transform, floor);
                Transform splash = floor.Find(id + "_Backsplash"), upper = floor.Find(id + "_Upper_Cabinet"), tap = floor.Find(id + "_Sink_Tap");
                Transform[] doors = floor.GetComponentsInChildren<Transform>(true).Where(item => item.name == id + "_Lower_Cabinet_Door").ToArray();
                if (!splash || !upper || !tap || doors.Length != 2
                    || LocalBounds(splash, floor).max.z >= counter.center.z
                    || LocalBounds(upper, floor).center.z >= counter.center.z
                    || LocalBounds(tap, floor).center.z >= counter.center.z
                    || doors.Any(door => LocalBounds(door, floor).min.z <= counter.max.z))
                    throw new InvalidOperationException(id + " kitchen back/tap/upper units or front door faces obstruct the occupied-room orientation.");
                Transform refrigerator = floor.Find(id + "_Refrigerator"), refrigeratorDoor = floor.Find(id + "_Refrigerator_Door");
                if (refrigerator && (!refrigeratorDoor || LocalBounds(refrigeratorDoor, floor).min.z <= LocalBounds(refrigerator, floor).max.z))
                    throw new InvalidOperationException(id + " refrigerator door faces the counter/wall instead of the occupied room.");
                checkedKitchens++;
            }
            return checkedKitchens;
        }

        private static Bounds LocalBounds(Transform item, Transform floor)
        {
            MeshFilter filter = item.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh) throw new InvalidOperationException("Expected actual furnishing mesh: " + item.name);
            Bounds source = filter.sharedMesh.bounds;
            Matrix4x4 matrix = floor.worldToLocalMatrix * item.localToWorldMatrix;
            Bounds result = new Bounds(matrix.MultiplyPoint3x4(source.min), Vector3.zero);
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(x == 0 ? source.min.x : source.max.x,
                    y == 0 ? source.min.y : source.max.y, z == 0 ? source.min.z : source.max.z)));
            return result;
        }
        private static bool ClearFeet(Vector3 feet, CharacterController player) => !Physics.OverlapCapsule(feet + Vector3.up * .22f,
            feet + Vector3.up * 1.50f, .22f, ~0, QueryTriggerInteraction.Ignore).Any(item => Relevant(item, player));
        private static bool ClearView(Vector3 eye, Vector3 focus, Transform fixture, CharacterController player)
        {
            Vector3 line = focus - eye;
            return !Physics.RaycastAll(eye, line.normalized, line.magnitude - .05f, ~0, QueryTriggerInteraction.Ignore)
                .Any(hit => Relevant(hit.collider, player) && hit.transform != fixture && !hit.transform.IsChildOf(fixture));
        }
        private static void Capture(Camera camera, string path)
        {
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(1600, 1000, 24, RenderTextureFormat.DefaultHDR);
            RenderTexture display = RenderTexture.GetTemporary(1600, 1000, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            Texture2D image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            bool oldSRGB = GL.sRGBWrite;
            try
            {
                camera.targetTexture = target; camera.Render(); GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(target, display); GL.sRGBWrite = oldSRGB; RenderTexture.active = display;
                image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive; GL.sRGBWrite = oldSRGB;
                RenderTexture.ReleaseTemporary(target); RenderTexture.ReleaseTemporary(display); Object.DestroyImmediate(image);
            }
        }
        private static void Write() { Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, Prefix + "-interior-review.json"), JsonUtility.ToJson(receipt, true)); }
        private static void Finish(Exception error)
        {
            if (finishing) return; finishing = true;
            if (receipt == null) receipt = NewReceipt();
            if (error != null) { receipt.passed = false; receipt.runtimeErrors.Add(error.ToString()); }
            receipt.finishedAtUtc = DateTime.UtcNow.ToString("O"); Write(); SessionState.SetBool(Active, false);
            EditorApplication.Exit(receipt.passed && receipt.runtimeErrors.Count == 0 ? 0 : 1);
        }
        private sealed class Request { public string label, fixture; public SlopCityBuildingInterior building; public int floor; }
        [Serializable] private sealed class Receipt
        {
            public string startedAtUtc, finishedAtUtc, graphicsDevice, isolatedDataPath; public bool passed; public int kitchensVerified;
            public string scope = "Five generated furnished floors; adult eye/capsule, supported clearance and furniture sightline, actual production finish and HDR-to-sRGB. Media/network disabled only in isolated fixture. Screenshots show representative programs, not every floor's artistic quality or physical-phone performance. No production scene/source asset or normal save edited.";
            public List<View> views = new List<View>(); public List<string> runtimeErrors = new List<string>(), editorDiagnostics = new List<string>();
        }
        [Serializable] private sealed class View
        {
            public string label, building, fixture, path; public int floor, detailPieces, detailBatches;
            public Vector3 feet, cameraPosition, focus; public Quaternion cameraRotation; public bool supportAndClearance, lineOfSight;
        }
    }
}
#endif
