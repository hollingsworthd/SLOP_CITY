#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Deterministic physics checks for the browser's shared crosshair target.</summary>
    public static class SlopCityWebInteractionSmokeTest
    {
        private const string SaveKey = "SlopCity.Sandbox.Progress.v1";
        private const string ReportPath = "Builds/Reports/web-interaction-check.json";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static GameObject fixture;
        private static Transform user;
        private static Camera camera;
        private static SlopCitySandbox sandbox;
        private static SlopCityFirstPerson controller;
        private static Receipt report;

        // Dedicated batch Editor only; this method writes its receipt and exits itself.
        // Compile with SLOP_CITY_WEB_INTERACTION_TEST using a temporary Assets/csc.rsp.
        // No PlayerSettings symbols or scene assets are changed by the runner.
        public static void Run()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Browser interaction checks require a dedicated batch Editor.");
            bool hadSave = PlayerPrefs.HasKey(SaveKey);
            string saved = PlayerPrefs.GetString(SaveKey, "");
            float timeScale = Time.timeScale;
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            report = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            try
            {
#if SLOP_CITY_WEB_INTERACTION_TEST
                report.browserTestSymbol = true;
                PlayerPrefs.DeleteKey(SaveKey);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Exercise();
#else
                throw new InvalidOperationException("Compile the Editor with SLOP_CITY_WEB_INTERACTION_TEST before running this check.");
#endif
            }
            catch (Exception exception) { report.errors.Add(exception.ToString()); }
            finally
            {
                // Remove all fixture save writers before restoring the user's progress.
                if (fixture) Object.DestroyImmediate(fixture);
                if (hadSave) PlayerPrefs.SetString(SaveKey, saved);
                else PlayerPrefs.DeleteKey(SaveKey);
                PlayerPrefs.Save();
                Time.timeScale = timeScale;
                UnityEngine.Random.state = randomState;
                report.playerPrefsRestored = PlayerPrefs.HasKey(SaveKey) == hadSave
                    && (!hadSave || PlayerPrefs.GetString(SaveKey, "") == saved);
                if (!report.playerPrefsRestored) report.errors.Add("Original PlayerPrefs progression was not restored.");
                report.passed = report.errors.Count == 0;
                report.finishedAtUtc = DateTime.UtcNow.ToString("O");
                string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                Debug.Log("SLOP_CITY_WEB_INTERACTION_" + (report.passed ? "PASSED" : "FAILED") + " " + ReportPath);
                if (!report.passed) Debug.LogError(string.Join("\n", report.errors));
                EditorApplication.Exit(report.passed ? 0 : 1);
            }
        }

#if SLOP_CITY_WEB_INTERACTION_TEST
        private static void Exercise()
        {
            fixture = new GameObject("Browser_Crosshair_Test_Fixture");
            user = Child("Test_Player", fixture.transform);
            camera = Child("Test_Camera", user).gameObject.AddComponent<Camera>();
            camera.nearClipPlane = .03f;
            camera.farClipPlane = 100f;
            camera.fieldOfView = 60f;
            camera.transform.localPosition = Vector3.up * 1.6f;
            camera.enabled = false;

            // A distant eligible site keeps legacy initialization from tagging the
            // deliberately untagged inspection fixtures as contract destinations.
            GameObject dispatch = Box("Fixture_Dispatch", new Vector3(-100, 1.6f, -100), Vector3.one);
            dispatch.AddComponent<SlopCityInteractable>().Configure(SlopCityInteractionKind.Terminal, "Fixture dispatch", true);
            GameObject nearLimit = Box("Within_Reach", new Vector3(0, 1.6f, 3.49f), new Vector3(.4f, .4f, .2f));
            GameObject beyondLimit = Box("Outside_Reach", new Vector3(12, 1.6f, 3.51f), new Vector3(.4f, .4f, .2f));
            GameObject offAxis = Box("Off_Crosshair", new Vector3(25.2f, 1.6f, 2), Vector3.one * .4f);
            GameObject blocker = Box("Nearest_Untagged_Surface", new Vector3(36, 1.6f, 1.2f), Vector3.one * .4f);
            GameObject behind = Box("Far_Interactive_Light", new Vector3(36, 1.6f, 2.5f), Vector3.one * .5f);
            behind.AddComponent<SlopCityInteractable>().Configure(SlopCityInteractionKind.Light, "Far light", false);
            Light lamp = behind.AddComponent<Light>();
            GameObject rotated = Box("Rotated_Colliderless_Trim", new Vector3(48.68f, .92f, 2), new Vector3(2, .15f, .1f), false);
            rotated.transform.rotation = Quaternion.Euler(0, 0, 45);
            GameObject behindTrim = Box("Surface_Behind_Trim", new Vector3(48, 1.6f, 2.8f), Vector3.one * .4f);
            GameObject poster = Box("Untagged_Colliderless_Poster", new Vector3(60, 1.6f, 2), new Vector3(.8f, .8f, .05f), false);
            Transform doorRoot = Child("Fixture_Door", fixture.transform);
            GameObject leaf = Box("Physical_Door_Leaf", new Vector3(72, 1.6f, 2), new Vector3(1, 2, .15f));
            leaf.transform.SetParent(doorRoot, true);
            var door = doorRoot.gameObject.AddComponent<SlopCityDoor>();
            door.Configure(leaf.transform, leaf.GetComponent<Collider>(), leaf.transform.position, "Fixture door");
            GameObject generic = Box("Centered_Generic_Prop", new Vector3(84, 1.6f, 2.5f), Vector3.one * .4f);
            GameObject tv = Box("Broadcast_TV_Frame", new Vector3(85.2f, 1.6f, 1.8f), Vector3.one * .4f);
            GameObject records = Box("Turntable", new Vector3(82.8f, 1.6f, 1.8f), Vector3.one * .4f);
            GameObject couch = Box("SC_Couch.SeatCushion.01", new Vector3(85.2f, 1.6f, .8f), Vector3.one * .4f);
            Transform stand = Child("Standing_Feet", fixture.transform);
            stand.position = new Vector3(84, 0, 0);
            Transform car = Child("Movable_Car", fixture.transform);
            car.gameObject.AddComponent<SlopCityInteractable>().Configure(SlopCityInteractionKind.Car, "Fixture car", false);
            GameObject carTrim = Box("Static_Marked_Car_Trim", new Vector3(100, 1.6f, 2), new Vector3(.8f, .8f, .1f), false);
            carTrim.transform.SetParent(car, true);

            sandbox = user.gameObject.AddComponent<SlopCitySandbox>();
            sandbox.Initialize(user, text => report.lastFeedback = text);
            controller = user.gameObject.AddComponent<SlopCityFirstPerson>();
            controller.enabled = false;
            controller.Configure(stand, stand, tv.transform, tv.transform, records.transform, camera, null);
            SetField(controller, "sandbox", sandbox);
            SetField(controller, "seated", false);
            SetField(controller, "doors", new[] { door });
            Physics.SyncTransforms();

            Aim(0);
            Check(Target() == nearLimit.transform, "A surface 3.39m from the eye is within the 3.4m interaction reach.");
            Aim(12);
            Check(Target() == null, "A surface 3.41m from the eye is outside reach, independent of the camera near plane.");
            Aim(24);
            Check(Target() == null, "A nearby object outside the center crosshair does not become the target.");
            Aim(36);
            Check(Target() == blocker.transform, "The nearest centered surface blocks the farther interactive object.");
            Check(sandbox.Interact(camera) && blocker.GetComponent<SlopCityInteractable>() && lamp.enabled,
                "E inspects the centered generic blocker without activating the farther light.");

            Aim(48);
            var centerRay = new Ray(camera.transform.position, camera.transform.forward);
            Check(rotated.GetComponent<Renderer>().bounds.IntersectRay(centerRay),
                "Rotated-trim fixture genuinely overlaps the center ray in its world AABB.");
            Check(Target() == behindTrim.transform, "A rotated colliderless mesh's empty world-AABB corner does not steal the target.");
            camera.transform.LookAt(rotated.transform.position);
            Check(Target() == rotated.transform, "The same rotated colliderless mesh is targetable when its local bounds are centered.");
            var geometry = fixture.AddComponent<SlopCityBrowserGeometry>();
            geometry.Capture(fixture.GetComponentsInChildren<Renderer>(true));
            StaticBatchingUtility.Combine(new[] { rotated, behindTrim }, fixture);
            // Web scenes are already batched at startup: restore bounds from serialized metadata,
            // rather than accidentally passing with the fixture's pre-batch runtime cache.
            GetField<Dictionary<Renderer, Bounds>>(sandbox, "webAuthoredBounds").Clear();
            typeof(SlopCitySandbox).GetMethod("RebuildWebScenery", PrivateInstance).Invoke(sandbox, null);
            Check(rotated.GetComponent<Renderer>().isPartOfStaticBatch,
                "The rotated colliderless fixture is genuinely part of a static batch.");
            Aim(48);
            Check(Target() == behindTrim.transform, "Static batching preserves the rotated colliderless trim's empty-corner rejection.");
            camera.transform.LookAt(rotated.transform.position);
            Transform batchedTarget = Target();
            Renderer batchedRenderer = rotated.GetComponent<Renderer>();
            Mesh batchedMesh = rotated.GetComponent<MeshFilter>().sharedMesh;
            report.diagnostics.Add("Static batch ray: eye=" + camera.transform.position + " direction=" + camera.transform.forward
                + " selected=" + (batchedTarget ? batchedTarget.name : "null") + " expected=" + rotated.name);
            report.diagnostics.Add("Static batch bounds: local=" + batchedRenderer.localBounds + " world=" + batchedRenderer.bounds
                + " mesh=" + batchedMesh.bounds + " subMeshStartIndex=" + ((MeshRenderer)batchedRenderer).subMeshStartIndex);
            report.diagnostics.Add("Renderer localToWorld=" + batchedRenderer.localToWorldMatrix.ToString("F4")
                + " worldToLocal=" + batchedRenderer.worldToLocalMatrix.ToString("F4"));
            report.diagnostics.Add("Transform localToWorld=" + rotated.transform.localToWorldMatrix.ToString("F4")
                + " worldToLocal=" + rotated.transform.worldToLocalMatrix.ToString("F4"));
            Check(batchedTarget == rotated.transform, "Static batching preserves the rotated trim's actual local hit bounds.");

            Aim(60);
            Check(Target() == poster.transform && sandbox.Interact(camera)
                && poster.GetComponent<SlopCityInteractable>().Kind == SlopCityInteractionKind.Inspect,
                "Centered colliderless scenery retains universal inspection behavior.");
            poster.GetComponent<Renderer>().enabled = false;
            Check(Target() == null, "Disabled colliderless renderers cannot become crosshair targets.");
            poster.GetComponent<Renderer>().enabled = true;
            poster.SetActive(false);
            Check(Target() == null, "Inactive colliderless objects cannot become crosshair targets.");
            poster.SetActive(true);
            Check(Target() == poster.transform, "Reactivated scenery remains targetable without rebuilding the spatial cache.");

            Aim(72);
            Check(Target() == leaf.transform, "The shared target exposes the actual physical door leaf.");
            Check(NearbyAction() == "Door" && GetField<SlopCityDoor>(controller, "nearbyDoor") == door,
                "FirstPerson maps the centered door leaf to the matching door action.");
            Check(!sandbox.Interact(camera), "Sandbox inspection still defers door handling to FirstPerson.");

            Aim(84);
            doorRoot.position = new Vector3(13.2f, 0, 0);
            Check(Target() == generic.transform && NearbyAction() == "None",
                "Off-axis TV, records, couch and door do not preempt centered generic inspection.");
            typeof(SlopCityFirstPerson).GetMethod("HandleBrowserInteraction", PrivateInstance).Invoke(controller, null);
            Check(generic.GetComponent<SlopCityInteractable>() && !door.IsOpen && !door.IsMoving,
                "The production E dispatch inspects the centered generic prop instead of opening the nearby off-axis door.");
            generic.SetActive(false);
            Check(Target() == null && NearbyAction() == "None", "An empty crosshair does not implicitly sit on the nearby couch.");
            typeof(SlopCityFirstPerson).GetMethod("HandleBrowserInteraction", PrivateInstance).Invoke(controller, null);
            Check(GetField<string>(controller, "feedback").Contains("3.4"),
                "The production E dispatch explains reach when the crosshair has no target.");
            camera.transform.LookAt(tv.transform.position);
            Check(Target() == tv.transform && NearbyAction() == "Television", "Centering the TV selects its media action.");
            camera.transform.LookAt(records.transform.position);
            Check(sandbox.BrowserTarget(camera) == records.transform && NearbyAction() == "Records",
                "Rotating the camera in the same frame invalidates the cached TV target and selects the record player without a forced refresh.");
            camera.transform.LookAt(couch.transform.position);
            Check(Target() == couch.transform && NearbyAction() == "Sit", "Centering the couch selects the sit action.");

            Aim(96);
            Check(Target() == null, "The movable car trim starts outside the crosshair.");
            car.position = new Vector3(-4, 0, 0);
            Check(Target() == carTrim.transform, "A static-marked child of a moved car uses current bounds after crossing spatial cells.");
            car.rotation = Quaternion.Euler(0, 30, 0);
            camera.transform.LookAt(carTrim.transform.position);
            user.position = carTrim.transform.position - camera.transform.forward * 2f - Vector3.up * 1.6f;
            Check(Target() == carTrim.transform, "Moved and rotated car trim remains targetable from the new camera position.");
        }

        private static Transform Target()
        {
            Physics.SyncTransforms();
            return sandbox.BrowserTarget(camera, true);
        }

        private static string NearbyAction()
        {
            Target();
            return typeof(SlopCityFirstPerson).GetMethod("FindNearbyAction", PrivateInstance).Invoke(controller, null).ToString();
        }

        private static void Aim(float x)
        {
            user.SetPositionAndRotation(new Vector3(x, 0, 0), Quaternion.identity);
            camera.transform.localPosition = Vector3.up * 1.6f;
            camera.transform.localRotation = Quaternion.identity;
            Physics.SyncTransforms();
        }

        private static Transform Child(string name, Transform parent)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static GameObject Box(string name, Vector3 point, Vector3 size, bool collider = true)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(fixture.transform);
            box.transform.position = point;
            box.transform.localScale = size;
            box.isStatic = true;
            if (!collider) Object.DestroyImmediate(box.GetComponent<Collider>());
            return box;
        }

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
        private static T GetField<T>(object target, string name)
            => (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            report.checks.Add(description);
        }
#endif

        [Serializable] private sealed class Receipt
        {
            public bool passed, browserTestSymbol, playerPrefsRestored;
            public string startedAtUtc, finishedAtUtc, unityVersion, lastFeedback;
            public string scope = "Dedicated batch Editor; production browser interaction branches enabled by a temporary test symbol. Deterministic Unity collider and colliderless-mesh fixtures test centered targeting, reach, occlusion, generic inspection, home actions and moving scenery. Browser input dispatch and rendering are separate checks.";
            public List<string> checks = new List<string>(), errors = new List<string>();
            public List<string> diagnostics = new List<string>();
        }
    }
}
#endif
