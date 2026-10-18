#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Street-facing authored triangles, fallback collision and persistent door state through browser shell retirement.</summary>
    [InitializeOnLoad]
    public static class SlopCityEntranceStreamingCheck
    {
        private const string Active = "SlopCity.EntranceStreaming.Active", Started = Active + ".Started", Geometry = Active + ".Geometry";
        private const string Save = "SlopCity.Sandbox.Progress.v1";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt receipt;
        private static Snapshot snapshot;
        private static SlopCityFirstPerson player;
        private static SlopCityBuildingWorld world;
        private static Camera camera;
        private static double enteredAt;
        private static int lastFrame = -1;
        private static string Folder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports/entrance-streaming");

        static SlopCityEntranceStreamingCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Active, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (state == PlayModeStateChange.EnteredEditMode) Finish(new InvalidOperationException("Entrance streaming Play Mode ended early."));
            };
        }

        // Graphics-enabled isolated StandaloneOSX batch Editor, omit -quit:
        // -executeMethod SlopCity.Editor.SlopCityEntranceStreamingCheck.Run
        // No test routing symbol is required. Never saves the scene/import settings.
        public static void Run()
        {
            if (!Application.isBatchMode || !Application.persistentDataPath.Contains("SlopCityValidation")
                || EditorApplication.isPlayingOrWillChangePlaymode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Use an isolated SlopCityValidation project/save identity, outside Play Mode, with graphics enabled.");
            SessionState.SetString(Started, DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(Active, true);
            foreach (string key in new[] { Save, Save + ".recovery" })
            {
                SessionState.SetBool(Active + key, PlayerPrefs.HasKey(key));
                SessionState.SetString(Active + key + ".value", PlayerPrefs.GetString(key, ""));
            }
            receipt = NewReceipt();
            try
            {
                var scene = EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                DisableMedia();
                // Prevent native Start from eagerly constructing every tower. Awake
                // still establishes the actual controller/camera references.
                Object.FindFirstObjectByType<SlopCityFirstPerson>().enabled = false;
                SlopCityWebGeometryBuildProcessor.ProtectRuntimeBuildingGeometry(scene);
                SlopCityWebDecorativeMerge.Prepare(scene);
                snapshot = new Snapshot();
                foreach (Transform tower in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .Where(item => item.name.StartsWith("City_Skyscraper_", StringComparison.Ordinal)))
                    foreach (MeshRenderer renderer in tower.GetComponentsInChildren<MeshRenderer>(true).Where(item => IsOldLobby(item.name)))
                    {
                        Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        if (!mesh) continue;
                        snapshot.parts.Add(new Part { tower = tower.name, indices = ChildIndices(tower, renderer.transform),
                            vertices = mesh.vertices, triangles = mesh.triangles });
                    }
                // Preserve exact authored triangles before Unity makes shared static
                // batches unreadable. Live transforms/enabled flags are used later.
                SessionState.SetString(Geometry, JsonUtility.ToJson(snapshot));
                Renderer[] renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
                new GameObject("Entrance_Check_Authored_Browser_Bounds").AddComponent<SlopCityBrowserGeometry>().Capture(renderers);
                SlopCityWebMeshBudget.Prepare(scene, true);
                SlopCityWebSingletonMaterialBuildProcessor.Prepare(scene);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    GameObject[] batch = root.GetComponentsInChildren<MeshRenderer>(true)
                        .Where(item => (GameObjectUtility.GetStaticEditorFlags(item.gameObject) & StaticEditorFlags.BatchingStatic) != 0)
                        .Select(item => item.gameObject).ToArray();
                    if (batch.Length > 0) StaticBatchingUtility.Combine(batch, root);
                }
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }

        private static void DisableMedia()
        {
            foreach (SlopCityTikTokSync item in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None)) item.enabled = false;
            foreach (SlopCityCityMedia item in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None)) item.enabled = false;
            foreach (SlopCityMediaPlayer item in Object.FindObjectsByType<SlopCityMediaPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) item.enabled = false;
        }
        private static Receipt NewReceipt() => new Receipt { startedAtUtc = SessionState.GetString(Started, DateTime.UtcNow.ToString("O")),
            unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceType.ToString(), isolatedDataPath = Application.persistentDataPath };
        private static void Tick()
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (receipt == null) receipt = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(receipt.startedAtUtc).ToUniversalTime()).TotalSeconds > 240)
                    throw new TimeoutException("Entrance streaming regression exceeded 240 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                if (Work.Count == 0)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < 1) return;
                    snapshot = JsonUtility.FromJson<Snapshot>(SessionState.GetString(Geometry, "{}"));
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
            Application.runInBackground = true; AudioListener.volume = 0;
            player = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            Require(player && !player.enabled, "Actual player is present with automatic native initialization held.");
            // Constrain the world before its first Install, then run the normal
            // player initialization exactly once with bounded tower registration.
            typeof(SlopCityFirstPerson).GetField("seated", Private).SetValue(player, true);
            GameObject city = GameObject.Find("SlopCity_NineBlocks");
            SlopCityBuildingWorld seed = city.GetComponent<SlopCityBuildingWorld>() ?? city.AddComponent<SlopCityBuildingWorld>();
            typeof(SlopCityBuildingWorld).GetField("constrainedBrowser", Private).SetValue(seed, true);
            world = SlopCityBuildingWorld.Ensure(player);
            Require(world, "Actual constrained building world initializes.");
            world.enabled = false;
            typeof(SlopCityFirstPerson).GetMethod("Start", Private).Invoke(player, null);
            player.SendMessage("OnApplicationFocus", true); player.SendMessage("OnApplicationPause", false);
            player.PlayOrResume(); player.StopMedia(); DisableMedia();
            camera = player.ViewCamera;
            Require(camera, "Actual production view camera is available.");
            player.TeleportTo(new Vector3(130, .1f, 130));
            Require(world.Buildings.Where(item => item.IsTower).All(item => item.LoadedFloorCount == 0), "No tower floors are loaded by fixture initialization.");
            SlopCityBuildingInterior[] cities = world.Buildings.Where(item => item.BuildingId.StartsWith("City_Skyscraper_", StringComparison.Ordinal))
                .OrderBy(item => item.BuildingId == "City_Skyscraper_16" ? 0 : 1).ThenBy(item => item.BuildingId, StringComparer.Ordinal).Take(2).ToArray();
            SlopCityBuildingInterior[] skylines = world.Buildings.Where(item => item.BuildingId.StartsWith("Skyline_Tower_", StringComparison.Ordinal))
                .OrderBy(item => item.BuildingId, StringComparer.Ordinal).Take(2).ToArray();
            Require(cities.Length == 2 && skylines.Length == 2, "Two real authored City and two Skyline entrances are covered.");
            foreach (SlopCityBuildingInterior tower in cities.Concat(skylines)) yield return Tower(tower);
        }

        private static IEnumerator Tower(SlopCityBuildingInterior tower)
        {
            string id = tower.BuildingId;
            Transform core = tower.transform.Find("Structural_Core");
            Require(core, id + " has its authored metric core.");
            Collider[] fallback = core.GetComponents<Collider>();
            bool[] fallbackEnabled = fallback.Select(item => item.enabled).ToArray();
            if (id.StartsWith("City_Skyscraper_", StringComparison.Ordinal))
                Require(fallback.Length > 0 && fallbackEnabled.Any(item => item), id + " begins with solid authored fallback collision.");
            else Check(true, id + " records " + fallback.Length + " original core colliders without inventing new fallback physics.");
            Invoke(tower, "EnsureArchitecture");
            SlopCityDoor door = Entrance(tower);
            Transform leaf = door.Leaf; Collider blocker = door.Blocker;
            Require(door && leaf && blocker, id + " admits a real persistent physical door.");
            Transform broadcast = tower.transform.Find("Runtime_Persistent_Building_Entrance/Entrance_City_Broadcast");
            Check(broadcast, id + " admits its street broadcast beside the persistent entrance.");
            if (broadcast)
            {
                Check(broadcast.GetComponentsInChildren<SlopCityCityScreen>(true).Length == 1, id + " broadcast has one ordinary city screen.");
                Check(broadcast.GetComponentsInChildren<UnityEngine.Video.VideoPlayer>(true).Length == 0,
                    id + " broadcast does not create a separate decoder outside the city media pool.");
                foreach (Renderer renderer in broadcast.GetComponentsInChildren<Renderer>(true))
                {
                    Bounds bounds = renderer.bounds;
                    float lowest = float.PositiveInfinity;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1,
                            (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        lowest = Mathf.Min(lowest, tower.transform.InverseTransformPoint(point).y);
                    }
                    Check(lowest >= 2.65f, id + " broadcast " + renderer.name + " sits above the full door opening.");
                }
            }
            Require(door.Toggle(null), id + " actual door accepts an unobstructed opening swing.");
            yield return Until(() => !door.IsMoving, id + " opening animation did not settle.");
            Require(door.IsOpen, id + " opens with its production Update.");
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Quaternion rotation = leaf.localRotation;
                bool open = door.IsOpen;
                float angle = (float)typeof(SlopCityDoor).GetField("angle", Private).GetValue(door);
                float target = (float)typeof(SlopCityDoor).GetField("targetAngle", Private).GetValue(door);
                Stage(tower, "streamed-" + cycle, cycle == 0);
                Require((bool)Invoke(tower, "ReleaseArchitecture"), id + " retires its unoccupied shell in cycle " + cycle + ".");
                Physics.SyncTransforms();
                Check(door == Entrance(tower) && door.Leaf == leaf && door.Blocker == blocker && blocker.enabled,
                    id + " cycle " + cycle + " retirement retains the exact active door, leaf and collider.");
                Check(door.IsOpen == open && Quaternion.Angle(rotation, leaf.localRotation) < .001f
                    && (float)typeof(SlopCityDoor).GetField("angle", Private).GetValue(door) == angle
                    && (float)typeof(SlopCityDoor).GetField("targetAngle", Private).GetValue(door) == target,
                    id + " cycle " + cycle + " retirement preserves open state, angle and target.");
                Check(fallback.Select((item, index) => item && item.enabled == fallbackEnabled[index]).All(item => item),
                    id + " cycle " + cycle + " restores original fallback collider enabled states.");
                Ray fallbackRay = StreetRay(tower, 0, 1.4f);
                if (fallbackEnabled.Any(item => item))
                    Check(fallback.Any(item => item.enabled && item.Raycast(fallbackRay, out _, 1.0f)),
                        id + " cycle " + cycle + " distant fallback still physically blocks unsupported entry.");
                Check(core.GetComponents<Collider>().SequenceEqual(fallback), id + " cycle " + cycle + " preserves the authored fallback collider membership.");
                Check(tower.transform.Find("Runtime_Persistent_Building_Entrance/Entrance_City_Broadcast") == broadcast,
                    id + " cycle " + cycle + " retirement preserves broadcast identity.");
                Stage(tower, "released-" + cycle, cycle == 0);
                // Re-admit in the same frame: old deferred-destroy shell must not
                // steal ownership or produce a duplicate persistent entrance.
                Invoke(tower, "EnsureArchitecture");
                Check(Entrance(tower) == door && door.Leaf == leaf && door.Blocker == blocker && blocker.enabled,
                    id + " cycle " + cycle + " same-frame re-admission retains physical door identity.");
                Check(Quaternion.Angle(rotation, leaf.localRotation) < .001f && door.IsOpen == open,
                    id + " cycle " + cycle + " re-admission preserves exact door rotation and open state.");
                Check(tower.transform.Cast<Transform>().Count(item => item.name == "Runtime_Hollow_Building_Shell" && item.gameObject.activeInHierarchy) == 1,
                    id + " cycle " + cycle + " has exactly one live replacement shell.");
                Check(tower.transform.Find("Runtime_Persistent_Building_Entrance/Entrance_City_Broadcast") == broadcast,
                    id + " cycle " + cycle + " re-admission preserves one broadcast identity.");
                Stage(tower, "readmitted-" + cycle, cycle == 0);
                Check(tower.LoadedFloorCount == 0 && !tower.Elevator, id + " cycle " + cycle + " never admits floors or a lift.");
                yield return null;
                Require(door.Toggle(null), id + " cycle " + cycle + " existing door still toggles.");
                yield return Until(() => !door.IsMoving, id + " post-cycle animation did not settle.");
                receipt.cycles++;
            }
            Invoke(tower, "ReleaseArchitecture");
            yield return null;
            receipt.towers++;
        }

        private static void Stage(SlopCityBuildingInterior tower, string stage, bool capture)
        {
            var evidence = new View { building = tower.BuildingId, stage = stage };
            // Sample the visible door rectangle, including both sides of the
            // centre rail. Rays stop just behind the front plane, excluding rear
            // walls and the intentional physical door leaf itself.
            foreach (float x in new[] { -.55f, 0f, .55f }) foreach (float y in new[] { .5f, 1.4f, 2.3f })
            {
                Ray ray = StreetRay(tower, x, y);
                foreach (Part part in snapshot.parts.Where(item => item.tower == tower.BuildingId))
                {
                    Transform live = Resolve(tower.transform, part.indices);
                    Renderer renderer = live ? live.GetComponent<Renderer>() : null;
                    if (renderer && renderer.enabled && live.gameObject.activeInHierarchy && Intersects(part.vertices, part.triangles, live.localToWorldMatrix, ray, .8f))
                        evidence.obstructions.Add(live.name + " at " + x + "," + y);
                }
                foreach (MeshFilter filter in tower.GetComponentsInChildren<MeshFilter>(true))
                {
                    bool facade = filter.name == "CurtainWall_Panel" || filter.name == "Masonry_WindowBays";
                    bool broadcastPart = filter.name == "Entrance_Broadcast_Frame" || filter.name == "Entrance_Broadcast_Picture";
                    if (filter.name != "Structural_Core" && !facade && !broadcastPart) continue;
                    if (facade && Mathf.Abs(filter.transform.localPosition.z + tower.GetFloorSize(0).y / 2 + .018f) > .05f) continue;
                    Renderer renderer = filter.GetComponent<Renderer>();
                    if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !filter.sharedMesh) continue;
                    if (MeshIntersects(filter.sharedMesh, filter.transform.localToWorldMatrix, ray, .8f)) evidence.obstructions.Add(filter.name + " at " + x + "," + y);
                }
                receipt.visualRays++;
            }
            if (capture)
            {
                Vector2 size = tower.GetFloorSize(0);
                camera.transform.position = tower.transform.TransformPoint(new Vector3(0, 1.62f, -size.y / 2 - 4.2f));
                camera.transform.LookAt(tower.transform.TransformPoint(new Vector3(0, 1.5f, -size.y / 2)));
                evidence.image = Path.Combine(Folder, tower.BuildingId + "-" + stage + ".png");
                Capture(evidence.image);
            }
            receipt.views.Add(evidence);
            Check(evidence.obstructions.Count == 0, tower.BuildingId + " " + stage + " actual authored front triangles leave the door rectangle clear"
                + (evidence.obstructions.Count > 0 ? ": " + string.Join("; ", evidence.obstructions) : "."));
        }

        private static Ray StreetRay(SlopCityBuildingInterior tower, float x, float y) => new Ray(
            tower.transform.TransformPoint(new Vector3(x, y, -tower.GetFloorSize(0).y / 2 - .4f)), tower.transform.TransformDirection(Vector3.forward).normalized);
        private static bool MeshIntersects(Mesh mesh, Matrix4x4 matrix, Ray ray, float distance)
        {
            using (Mesh.MeshDataArray data = MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
            {
                data[0].GetVertices(vertices);
                Vector3[] points = vertices.ToArray();
                for (int submesh = 0; submesh < data[0].subMeshCount; submesh++)
                {
                    if (data[0].GetSubMesh(submesh).topology != MeshTopology.Triangles) continue;
                    using (var indices = new NativeArray<int>(data[0].GetSubMesh(submesh).indexCount, Allocator.Temp))
                    {
                        data[0].GetIndices(indices, submesh);
                        if (Intersects(points, indices.ToArray(), matrix, ray, distance)) return true;
                    }
                }
            }
            return false;
        }
        private static bool Intersects(Vector3[] vertices, int[] indices, Matrix4x4 matrix, Ray ray, float distance)
        {
            Vector3 origin = matrix.inverse.MultiplyPoint3x4(ray.origin), direction = matrix.inverse.MultiplyVector(ray.direction);
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]], e1 = vertices[indices[i + 1]] - a, e2 = vertices[indices[i + 2]] - a;
                Vector3 p = Vector3.Cross(direction, e2); float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-7f) continue;
                Vector3 offset = origin - a; float u = Vector3.Dot(offset, p) / det;
                if (u < 0 || u > 1) continue;
                Vector3 q = Vector3.Cross(offset, e1); float v = Vector3.Dot(direction, q) / det;
                if (v < 0 || u + v > 1) continue;
                float t = Vector3.Dot(e2, q) / det;
                if (t >= 0 && t <= distance) return true;
            }
            return false;
        }
        private static void Capture(string path)
        {
            const int width = 640, height = 480;
            RenderTexture oldActive = RenderTexture.active, oldTarget = camera.targetTexture;
            float oldAspect = camera.aspect; bool oldSRGB = GL.sRGBWrite;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            var display = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                Require(target.Create() && display.Create(), "Street capture targets allocate.");
                camera.aspect = width / (float)height; camera.targetTexture = target; camera.Render();
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear; Graphics.Blit(target, display); GL.sRGBWrite = oldSRGB;
                RenderTexture.active = display; pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                Directory.CreateDirectory(Folder); File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = oldActive; GL.sRGBWrite = oldSRGB;
                Object.DestroyImmediate(pixels); target.Release(); display.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(display);
            }
        }
        private static bool IsOldLobby(string name) => name == "Lobby_Entrance_Recess" || name == "Lobby_Glazing" || name == "Lobby_DoorRail";
        private static int[] ChildIndices(Transform root, Transform item)
        {
            var result = new List<int>(); while (item != root) { result.Add(item.GetSiblingIndex()); item = item.parent; } result.Reverse(); return result.ToArray();
        }
        private static Transform Resolve(Transform root, int[] indices)
        {
            foreach (int index in indices) { if (!root || index >= root.childCount) return null; root = root.GetChild(index); } return root;
        }
        private static SlopCityDoor Entrance(SlopCityBuildingInterior tower)
        {
            SlopCityDoor[] doors = tower.GetComponentsInChildren<SlopCityDoor>(true).Where(item => item.name == "Building_Entrance_Door" && item.isActiveAndEnabled).ToArray();
            Require(doors.Length == 1, tower.BuildingId + " has exactly one active entrance door."); return doors[0];
        }
        private static object Invoke(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
        private static IEnumerator Until(Func<bool> condition, string message)
        {
            double deadline = EditorApplication.timeSinceStartup + 8;
            while (!condition()) { if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(message); yield return null; }
        }
        private static void Check(bool condition, string message) { if (condition) receipt.checks.Add(message); else receipt.failures.Add(message); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); receipt.checks.Add(message); }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Active, false); Work.Clear();
            if (receipt == null) receipt = NewReceipt();
            if (error != null) receipt.errors.Add(error.ToString());
            foreach (SlopCitySandbox item in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsSortMode.None)) Object.DestroyImmediate(item);
            foreach (string key in new[] { Save, Save + ".recovery" })
            {
                if (SessionState.GetBool(Active + key, false)) PlayerPrefs.SetString(key, SessionState.GetString(Active + key + ".value", ""));
                else PlayerPrefs.DeleteKey(key);
            }
            PlayerPrefs.Save(); SessionState.EraseString(Geometry);
            receipt.finishedAtUtc = DateTime.UtcNow.ToString("O"); receipt.passed = receipt.errors.Count == 0 && receipt.failures.Count == 0;
            Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "entrance-streaming-check.json"), JsonUtility.ToJson(receipt, true));
            Debug.Log("SLOP_CITY_ENTRANCE_STREAMING_CHECK " + (receipt.passed ? "PASSED" : "FAILED") + " checks=" + receipt.checks.Count + " cycles=" + receipt.cycles);
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(receipt.passed ? 0 : 1);
        }
        [Serializable] private sealed class Snapshot { public List<Part> parts = new List<Part>(); }
        [Serializable] private sealed class Part { public string tower; public int[] indices; public Vector3[] vertices; public int[] triangles; }
        [Serializable] private sealed class View { public string building, stage, image; public List<string> obstructions = new List<string>(); }
        [Serializable] private sealed class Receipt
        {
            public string startedAtUtc, finishedAtUtc, unityVersion, graphicsDevice, isolatedDataPath;
            public string scope = "Prepared authored scene, constrained browser shell policy, four representative City/Skyline entrances, three retirement/re-admission cycles each; actual front-plane triangle rays, original fallback collision, persistent door identities/angles and broadcast placement/identity without adding decoders. 640x480 production-camera PNGs convert finished linear HDR to display sRGB. No tower floors/lifts or media/network workload. This Editor fixture does not establish physical-device acceptance, compiled Web appearance or subjective visual quality. Scene/import settings are not saved; progress keys restored.";
            public bool passed; public int towers, cycles, visualRays;
            public List<string> checks = new List<string>(), failures = new List<string>(), errors = new List<string>();
            public List<View> views = new List<View>();
        }
    }
}
#endif
