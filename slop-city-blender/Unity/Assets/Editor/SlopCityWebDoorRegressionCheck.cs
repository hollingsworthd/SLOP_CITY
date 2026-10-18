#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Actual prepared city, browser USE dispatch, moving leaves and physical doorway passage.</summary>
    [InitializeOnLoad]
    public static class SlopCityWebDoorRegressionCheck
    {
        private const string Active = "SlopCity.WebDoorCheck.Active", Started = "SlopCity.WebDoorCheck.Started";
        private const string Save = "SlopCity.Sandbox.Progress.v1";
        private const string Report = "Builds/Reports/web-door-regression-check.json";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt receipt;
        private static SlopCityFirstPerson player;
        private static CharacterController capsule;
        private static SlopCitySandbox sandbox;
        private static Camera camera;
        private static double enteredAt;

        static SlopCityWebDoorRegressionCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Active, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (state == PlayModeStateChange.EnteredEditMode) Finish(new Exception("Play Mode ended before the door check completed."));
            };
        }

        // Dedicated isolated batch Editor, omit -quit. Compile Assembly-CSharp with
        // -define:SLOP_CITY_WEB_INTERACTION_TEST in temporary Assets/csc.rsp first.
        // The saved scene and source/import settings are never written by this runner.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Door regression requires a dedicated batch Editor outside Play Mode.");
#if !SLOP_CITY_WEB_INTERACTION_TEST
            throw new InvalidOperationException("Compile with SLOP_CITY_WEB_INTERACTION_TEST before running the browser door regression.");
#else
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
                SlopCityWebGeometryBuildProcessor.ProtectRuntimeBuildingGeometry(scene);
                SlopCityWebDecorativeMerge.Prepare(scene);
                Renderer[] renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
                new GameObject("Door_Check_Authored_Browser_Bounds").AddComponent<SlopCityBrowserGeometry>().Capture(renderers);
                SlopCityWebMeshBudget.Prepare(scene, true);
                SlopCityWebSingletonMaterialBuildProcessor.Prepare(scene);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    GameObject[] batch = root.GetComponentsInChildren<MeshRenderer>(true)
                        .Where(renderer => (GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.BatchingStatic) != 0)
                        .Select(renderer => renderer.gameObject).ToArray();
                    if (batch.Length > 0) StaticBatchingUtility.Combine(batch, root);
                }
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
#endif
        }

        private static Receipt NewReceipt() => new Receipt { startedAtUtc = SessionState.GetString(Started, DateTime.UtcNow.ToString("O")), unityVersion = Application.unityVersion };
        private static void Tick()
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (receipt == null) receipt = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(receipt.startedAtUtc).ToUniversalTime()).TotalSeconds > 240)
                    throw new TimeoutException("Browser door regression exceeded 240 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (Work.Count == 0)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < 1) return;
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
            player = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            Check(player, "Actual scene has its first-person controller.");
            capsule = player.GetComponent<CharacterController>(); sandbox = player.GetComponent<SlopCitySandbox>();
            camera = (Camera)typeof(SlopCityFirstPerson).GetField("viewCamera", Private).GetValue(player);
            Check(capsule && sandbox && camera, "Actual player retains camera, sandbox and collision capsule.");
            player.SendMessage("OnApplicationFocus", true); player.SendMessage("OnApplicationPause", false);
            player.PlayOrResume(); player.StopMedia();
            // Disable only the input/movement loop: invoke production USE directly and
            // move the actual capsule in bounded steps while doors animate normally.
            player.enabled = false;
            SlopCityBuildingWorld world = SlopCityBuildingWorld.Ensure(player);
            Check(world, "Actual building routes initialize.");
            world.enabled = false;
            typeof(SlopCityBuildingWorld).GetField("constrainedBrowser", Private).SetValue(world, true);
            SlopCityDoor home = Object.FindObjectsByType<SlopCityDoor>(FindObjectsSortMode.None).Single(door => door.name == "Home_Entrance");
            yield return Door(home, "home entrance", true);

            SlopCityBuildingInterior[] towers = world.Buildings.Where(item => item && item.IsTower
                && item.name != "MilitarySF_CommunicationsCitadel").OrderBy(item => item.EntranceFeet.sqrMagnitude).Take(2).ToArray();
            Check(towers.Length == 2, "Two real tower routes are available for streamed-door coverage.");
            foreach (SlopCityBuildingInterior tower in towers)
            {
                player.TeleportTo(new Vector3(130, .1f, 130));
                Check((bool)typeof(SlopCityBuildingInterior).GetMethod("ReleaseStream", Private).Invoke(tower, null),
                    tower.BuildingId + " retires its unoccupied initial Editor architecture.");
                Transform retired = tower.transform.Find("Retired_Hollow_Building_Shell");
                Check(!retired || !retired.gameObject.activeInHierarchy,
                    tower.BuildingId + " retired shell immediately stops rendering, targeting and collision.");
                // Editor Start uses native eager construction. This fixture explicitly
                // changes to the mobile policy, so its initial shell-owned entrance
                // must finish deferred destruction before counting re-admitted doors.
                yield return Until(() => !retired, tower.BuildingId + " retired Editor shell was not destroyed.");
                Transform floor = tower.EnsureFloor(0);
                player.RefreshWorldInteractions();
                Transform persistent = tower.transform.Find("Runtime_Persistent_Building_Entrance");
                Check(persistent && persistent.gameObject.activeInHierarchy,
                    tower.BuildingId + " re-admits the live persistent mobile entrance.");
                SlopCityDoor[] entrances = tower.GetComponentsInChildren<SlopCityDoor>(true)
                    .Where(door => door.name == "Building_Entrance_Door" && door.isActiveAndEnabled).ToArray();
                Check(entrances.Length == 1 && entrances[0].transform.IsChildOf(persistent),
                    tower.BuildingId + " has exactly one live entrance owned by its persistent root.");
                SlopCityDoor entrance = entrances[0];
                yield return Door(entrance, tower.BuildingId + " streamed entrance", false);
                SlopCityDoor room = floor.GetComponentsInChildren<SlopCityDoor>(true).FirstOrDefault(door => door.name.StartsWith("Room_Hinged_Door_", StringComparison.Ordinal));
                Check(room, tower.BuildingId + " contains a real furnished room door.");
                yield return Door(room, tower.BuildingId + " room door", false);
            }
        }

        private static IEnumerator Door(SlopCityDoor door, string label, bool blockerControl)
        {
            Check(door && door.Leaf && door.Blocker && door.Blocker.enabled, label + " has its enabled physical leaf.");
            Check(!door.Leaf.GetComponentsInChildren<MeshRenderer>(true).Any(renderer => renderer.isPartOfStaticBatch), label + " moving art is excluded from static batching.");
            Bounds closed = door.Blocker.bounds;
            Vector3 normal = closed.size.x < closed.size.z ? Vector3.right : Vector3.forward;
            SlopCityBuildingInterior owner = door.GetComponentInParent<SlopCityBuildingInterior>();
            if (label.EndsWith(" room door", StringComparison.Ordinal) && owner
                && Vector3.Dot(closed.center - owner.transform.position, normal) < 0) normal = -normal;
            Vector3 middle = closed.center; middle.y = closed.min.y + .025f;
            Vector3 near = middle - normal * 1.25f, far = middle + normal * 1.25f;
            player.TeleportTo(near);
            Physics.SyncTransforms();
            Check(!door.IsOpen && !door.IsMoving, label + " starts closed.");
            yield return Walk(far, 50);
            Check(Vector3.Dot(player.transform.position - middle, normal) < -.1f, label + " closed physical leaf blocks the real capsule.");
            player.TeleportTo(near);
            camera.transform.LookAt(door.InteractionPoint);
            Physics.SyncTransforms();
            Transform target = Target();
            receipt.targets.Add(label + ": " + (target ? PathOf(target) : "null"));
            Check(target && target.GetComponentInParent<SlopCityDoor>() == door, label + " crosshair resolves to the real door action through prepared scenery.");
            Check(typeof(SlopCityFirstPerson).GetMethod("FindNearbyAction", Private).Invoke(player, null).ToString() == "Door", label + " FirstPerson selects Door.");
            if (blockerControl)
            {
                GameObject obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
                try
                {
                    obstruction.name = "Door_Check_Nearest_Solid_Obstruction";
                    obstruction.transform.position = Vector3.Lerp(camera.transform.position, door.InteractionPoint, .5f);
                    obstruction.transform.localScale = Vector3.one * .4f;
                    player.RefreshWorldInteractions(); Physics.SyncTransforms();
                    target = Target();
                    Check(target && target.GetComponentInParent<SlopCityDoor>() != door, "A nearer physical obstruction still prevents door targeting.");
                    Use();
                    Check(!door.IsOpen && !door.IsMoving, "USE does not activate a door through a nearer physical obstruction.");
                }
                finally { Object.DestroyImmediate(obstruction); player.RefreshWorldInteractions(); Physics.SyncTransforms(); }
            }
            Quaternion before = door.Leaf.localRotation;
            Use();
            Check(door.IsMoving, label + " production browser USE requests a swing.");
            yield return Until(() => !door.IsMoving, label + " animation did not settle.");
            Check(door.IsOpen && Quaternion.Angle(before, door.Leaf.localRotation) >= 60, label + " real Update animates the physical leaf open.");
            Check(door.Blocker.enabled, label + " open leaf keeps its physical collider.");
            yield return Walk(far, 75);
            Check(Vector3.Distance(new Vector3(player.transform.position.x, far.y, player.transform.position.z), far) < .16f, label + " actual CharacterController passes through the open doorway.");
            if (blockerControl)
                Check(!door.Toggle(player.transform) && door.IsOpen && !door.IsMoving,
                    "The home door refuses a closing arc that would intersect the capsule 1.25m beyond its doorway.");
            // The open leaf can extend 1.55m onto the far side. Crossing its
            // doorway is not proof the caller has cleared its full closing arc.
            // Return through the opening using collision, to the same approach
            // side whose opening sweep was already proven clear.
            yield return Walk(near, 75);
            Check(Vector3.Distance(new Vector3(player.transform.position.x, near.y, player.transform.position.z), near) < .16f,
                label + " capsule physically returns to the clear approach side before closing.");
            Check(door.Toggle(player.transform), label + " clear approach side accepts closing without bypassing capsule protection.");
            yield return Until(() => !door.IsMoving, label + " closing animation did not settle.");
            Check(!door.IsOpen && door.Blocker.enabled && Quaternion.Angle(before, door.Leaf.localRotation) < .1f, label + " closes to the exact original leaf rotation with collision.");
            receipt.doorsPassed++;
        }

        private static void Use() => typeof(SlopCityFirstPerson).GetMethod("HandleBrowserInteraction", Private).Invoke(player, null);
        private static Transform Target() => (Transform)typeof(SlopCitySandbox).GetMethod("BrowserTarget", BindingFlags.Instance | BindingFlags.Public).Invoke(sandbox, new object[] { camera, true });
        private static IEnumerator Walk(Vector3 destination, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                Vector3 delta = destination - player.transform.position; delta.y = 0;
                if (delta.magnitude < .06f) break;
                capsule.Move(Vector3.ClampMagnitude(delta, .07f) + Vector3.down * .035f);
                receipt.capsuleMoves++; Physics.SyncTransforms();
                yield return null;
            }
        }
        private static IEnumerator Until(Func<bool> condition, string message)
        {
            double end = EditorApplication.timeSinceStartup + 8;
            while (!condition()) { if (EditorApplication.timeSinceStartup > end) throw new TimeoutException(message); yield return null; }
        }
        private static string PathOf(Transform item) => item.parent ? PathOf(item.parent) + "/" + item.name : item.name;
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); receipt.checks.Add(message); }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Active, false); Work.Clear();
            if (receipt == null) receipt = NewReceipt();
            if (error != null) receipt.errors.Add(error.ToString());
            // Destroy all save writers before returning the original progress snapshots.
            foreach (SlopCitySandbox item in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsSortMode.None)) Object.DestroyImmediate(item);
            foreach (string key in new[] { Save, Save + ".recovery" })
            {
                if (SessionState.GetBool(Active + key, false)) PlayerPrefs.SetString(key, SessionState.GetString(Active + key + ".value", ""));
                else PlayerPrefs.DeleteKey(key);
            }
            PlayerPrefs.Save();
            receipt.passed = receipt.errors.Count == 0; receipt.finishedAtUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory(Path.GetDirectoryName(Report)); File.WriteAllText(Report, JsonUtility.ToJson(receipt, true));
            Debug.Log("SLOP_CITY_WEB_DOOR_CHECK " + (receipt.passed ? "PASSED" : "FAILED") + " checks=" + receipt.checks.Count + " doors=" + receipt.doorsPassed);
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(receipt.passed ? 0 : 1);
        }
        [Serializable] private sealed class Receipt
        {
            public string startedAtUtc, finishedAtUtc, unityVersion;
            public bool passed;
            public int doorsPassed, capsuleMoves;
            public List<string> checks = new List<string>(), errors = new List<string>(), targets = new List<string>();
        }
    }
}
#endif
