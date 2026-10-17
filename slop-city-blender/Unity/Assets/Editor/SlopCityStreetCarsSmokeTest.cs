#if UNITY_EDITOR
using System;
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
    /// <summary>Car admission, shared ownership and existing driving, without Play Mode or saves.</summary>
    public static class SlopCityStreetCarsSmokeTest
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
        private static Receipt receipt;
        private static readonly List<string> RuntimeErrors = new List<string>();
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Car checks require isolated batch Edit Mode.");
            receipt = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O") };
            Application.logMessageReceived += CaptureLog;
            try
            {
                SyntheticChecks();
                SavedCityChecks();
                Check(RuntimeErrors.Count == 0, "No runtime error or exception was logged.");
            }
            catch (Exception error) { receipt.errors.Add(error.ToString()); }
            finally
            {
                Application.logMessageReceived -= CaptureLog;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            receipt.runtimeErrors = RuntimeErrors.ToArray();
            receipt.passed = receipt.errors.Count == 0 && RuntimeErrors.Count == 0;
            receipt.finishedAtUtc = DateTime.UtcNow.ToString("O");
            string path = Argument("-slopCityStreetCarsReport") ?? "Builds/Reports/transit-20261016/street-cars-check.json";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(receipt, true));
            Debug.Log("SLOP_CITY_STREET_CARS_" + (receipt.passed ? "PASSED" : "FAILED") + " " + receipt.checks + " checks");
            EditorApplication.Exit(receipt.passed ? 0 : 1);
        }

        private static void SyntheticChecks()
        {
            FreshScene();
            Check(!SlopCityStreetCars.Ensure((Transform)null), "Unrelated scenes do not acquire cars or city roots.");
            Transform city = SyntheticCity(true, out Transform user, out SlopCityInteractable original);
            SlopCityStreetCars owner = SlopCityStreetCars.Ensure(user);
            Check(owner && owner.AddedCarCount == 4, "Four supported, clear synthetic districts receive exactly four cars.");
            ValidateCopies(owner, new[] { original });
            Transform moved = owner.Cars[0].transform;
            moved.position += Vector3.right * 3;
            Vector3 parked = moved.position;
            Check(SlopCityStreetCars.Ensure(user) == owner && owner.AddedCarCount == 4 && moved.position == parked,
                "Repeated installation preserves moved vehicles without respawning or duplicating cars.");

            FreshScene();
            city = SyntheticCity(false, out user, out original);
            Check(SlopCityStreetCars.Ensure(user).AddedCarCount == 0, "Unsupported parking positions do not place cars over empty space.");

            FreshScene();
            city = SyntheticCity(true, out user, out original);
            Box("Occupied carriageways", new Vector3(60, 1, 30), new Vector3(600, 2, 600), city);
            Check(SlopCityStreetCars.Ensure(user).AddedCarCount == 0, "Occupied carriageways reject every parking candidate.");

            FreshScene();
            city = SyntheticCity(true, out user, out original);
            SlopCityCrowd crowd = new GameObject("Synthetic existing pedestrian graph").AddComponent<SlopCityCrowd>();
            crowd.transform.SetParent(city, false);
            var routes = new List<SlopCityCrowdRoute>();
            foreach (SlopCityDistrict district in city.GetComponentsInChildren<SlopCityDistrict>())
                routes.Add(new SlopCityCrowdRoute { nodes = new[] {
                    new SlopCityCrowdNode { position = district.transform.TransformPoint(new Vector3(-29, 0, -15.5f)), neighbors = new[] { 1 } },
                    new SlopCityCrowdNode { position = district.transform.TransformPoint(new Vector3(29, 0, -15.5f)), neighbors = new[] { 0 } } } });
            Set(crowd, "routes", routes.ToArray());
            Check(SlopCityStreetCars.Ensure(user).AddedCarCount == 0, "Existing pedestrian edges, not only their endpoints, exclude parking.");

            FreshScene();
            city = SyntheticCity(true, out user, out original);
            foreach (SlopCityDistrict district in city.GetComponentsInChildren<SlopCityDistrict>())
                foreach (float x in new[] { -23f, 8f, -10f, 22f, -16f, 16f, -6f, 6f })
                {
                    Transform door = new GameObject("Existing entrance").transform; door.SetParent(city, false);
                    door.position = district.transform.TransformPoint(new Vector3(x, 1, -15.5f));
                    door.gameObject.AddComponent<SlopCityDoor>();
                }
            Check(SlopCityStreetCars.Ensure(user).AddedCarCount == 0, "Entrance clearance rejects otherwise empty parking positions.");

            FreshScene();
            city = SyntheticCity(true, out user, out original);
            original.transform.localScale = Vector3.one * 2;
            Check(SlopCityStreetCars.Ensure(user).AddedCarCount == 0, "Unexpected scaled donor geometry is refused safely.");
        }

        private static void SavedCityChecks()
        {
            EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
            foreach (SlopCityTikTokSync sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None)) sync.enabled = false;
            foreach (SlopCityCityMedia media in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None)) media.enabled = false;
            SlopCityFirstPerson player = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            Check(player, "Saved scene contains the authored player."); player.enabled = false;
            Check(SlopCityBuildingWorld.Ensure(player), "Real tower/venue doors exist before parking admission.");
            SlopCityWorldUtilities.Ensure(player.transform);
            SlopCityInteractable[] originals = Object.FindObjectsByType<SlopCityInteractable>(FindObjectsSortMode.None)
                .Where(item => item.Kind == SlopCityInteractionKind.Car).ToArray();
            Check(originals.Length == 4, "The four original authored cars remain available.");
            var positions = originals.ToDictionary(item => item, item => item.transform.localToWorldMatrix);
            var meshes = new HashSet<Mesh>(originals.SelectMany(item => item.GetComponentsInChildren<MeshFilter>()).Select(item => item.sharedMesh));
            var materials = new HashSet<Material>(originals.SelectMany(item => item.GetComponentsInChildren<Renderer>()).SelectMany(item => item.sharedMaterials));
            SlopCityStreetCars owner = SlopCityStreetCars.Ensure(player);
            Check(owner && owner.AddedCarCount == 4, "Four additional cars are admitted in the actual prepared saved city.");
            ValidateCopies(owner, originals);
            foreach (var pair in positions) Check(pair.Key.transform.localToWorldMatrix == pair.Value, "An original vehicle retains its authored transform.");
            foreach (SlopCityInteractable car in owner.Cars)
            {
                receipt.savedCityCarPositions.Add(car.name + " / " + car.transform.position);
                foreach (MeshFilter filter in car.GetComponentsInChildren<MeshFilter>()) Check(meshes.Contains(filter.sharedMesh), "Saved-city car mesh reference belongs to original art.");
                foreach (Renderer renderer in car.GetComponentsInChildren<Renderer>())
                    foreach (Material material in renderer.sharedMaterials) Check(materials.Contains(material), "Saved-city paint/trim references remain shared.");
            }
            ExerciseDriving(owner.Cars[0]);
            Vector3 moved = owner.Cars[0].transform.position;
            Check(SlopCityStreetCars.Ensure(player) == owner && owner.AddedCarCount == 4 && owner.Cars[0].transform.position == moved,
                "Saved-city reinstallation retains the player's parked position and the four-car cap.");
        }

        private static void ExerciseDriving(SlopCityInteractable car)
        {
            var probe = new GameObject("Isolated car driver probe");
            var input = probe.AddComponent<SlopCityPlayerInput>(); input.SetGameplayEnabled(true);
            var controller = probe.AddComponent<CharacterController>(); controller.height = 1.72f; controller.radius = .22f; controller.center = Vector3.up * .86f;
            Camera camera = new GameObject("Car driving test view").AddComponent<Camera>(); camera.transform.SetParent(probe.transform, false);
            camera.transform.localPosition = Vector3.up * 1.62f;
            probe.transform.position = car.transform.TransformPoint(new Vector3(-1.6f, 0, 0));
            var sandbox = probe.AddComponent<SlopCitySandbox>();
            Set(sandbox, "user", probe.transform); Set(sandbox, "input", input);
            bool held = false;
            sandbox.TeleportRequested += (position, heading) => {
                controller.enabled = false; probe.transform.SetPositionAndRotation(position, Quaternion.Euler(0, heading, 0)); controller.enabled = true; Physics.SyncTransforms(); };
            sandbox.MovementOverrideRequested += value => held = value;
            // Deliberately do not Initialize Sandbox: these driving methods do not read or write progress.
            Call(sandbox, "StartDriving", car, camera);
            Check(sandbox.IsDriving && held, "Existing production entry takes control of an added car.");
            Vector3 before = car.transform.position;
            input.SetTouchMovement(Vector2.up);
            for (int i = 0; i < 10; i++) Call(sandbox, "Drive", .05f);
            float direction = 1f;
            if (Vector3.Distance(before, car.transform.position) <= .25f)
            {
                direction = -1f; Set(sandbox, "vehicleSpeed", 0f); input.SetTouchMovement(Vector2.down);
                for (int i = 0; i < 10; i++) Call(sandbox, "Drive", .05f);
            }
            Check(Vector3.Distance(before, car.transform.position) > .25f, "The added car accelerates and moves on its supported road.");
            Transform blocker = Box("Physical driving blocker", car.transform.position + car.transform.forward * (2.3f * direction) + Vector3.up,
                new Vector3(4, 2, .3f), null);
            blocker.rotation = car.transform.rotation; Physics.SyncTransforms();
            before = car.transform.position; Call(sandbox, "Drive", .05f);
            Check((before - car.transform.position).sqrMagnitude < .000001f, "Existing production collision stops the car at a real blocker.");
            Object.DestroyImmediate(blocker.gameObject); input.SetTouchMovement(Vector2.zero);
            Call(sandbox, "StopDriving", false);
            Check(!sandbox.IsDriving && !held, "Normal production exit releases car control at a clear supported side.");
            Object.DestroyImmediate(probe);
        }

        private static void ValidateCopies(SlopCityStreetCars owner, SlopCityInteractable[] originals)
        {
            var meshes = new HashSet<Mesh>(originals.SelectMany(item => item.GetComponentsInChildren<MeshFilter>()).Select(item => item.sharedMesh));
            var materials = new HashSet<Material>(originals.SelectMany(item => item.GetComponentsInChildren<Renderer>()).SelectMany(item => item.sharedMaterials));
            Check(owner.Cars.Select(item => item.name).Distinct().Count() == owner.AddedCarCount, "Additional cars have distinct district identities.");
            foreach (SlopCityInteractable car in owner.Cars)
            {
                Check(car.Kind == SlopCityInteractionKind.Car && car.GetComponentsInChildren<Collider>().Length > 0, "Added cars retain physical driving identity and collision.");
                Check(!car.GetComponentInChildren<SlopCityCityScreen>(true) && !car.GetComponentInChildren<SlopCityMediaPlayer>(true), "Added cars create no dashboard media owners or players.");
                foreach (Transform part in car.GetComponentsInChildren<Transform>(true)) Check(!part.gameObject.isStatic, "Movable car parts cannot be static batched.");
                foreach (MeshFilter filter in car.GetComponentsInChildren<MeshFilter>()) Check(meshes.Contains(filter.sharedMesh), "No per-car mesh copy is created.");
                foreach (Renderer renderer in car.GetComponentsInChildren<Renderer>())
                    foreach (Material material in renderer.sharedMaterials) Check(materials.Contains(material), "No per-car paint/trim material copy is created.");
            }
        }

        private static Transform SyntheticCity(bool ground, out Transform user, out SlopCityInteractable template)
        {
            Transform city = new GameObject("SlopCity_NineBlocks").transform;
            if (ground) Box("Supported street", new Vector3(60, -.1f, 30), new Vector3(600, .2f, 600), city);
            foreach (int index in new[] { 0, 2, 5, 8 })
            {
                var district = new GameObject("District_" + index).AddComponent<SlopCityDistrict>(); district.transform.SetParent(city, false);
                district.transform.position = new Vector3(index % 3 * 70, 0, index / 3 * 50);
                district.Configure(index % 3, index / 3, "Synthetic district", Vector3.zero);
            }
            Transform root = new GameObject("Parked_CityCar").transform; root.SetParent(city, false); root.position = new Vector3(-80, .02f, 50);
            Box("Car_Body", new Vector3(0, .7f, 0), new Vector3(1.8f, .8f, 3.9f), root, true);
            template = root.gameObject.AddComponent<SlopCityInteractable>(); template.Configure(SlopCityInteractionKind.Car, "Fixture sedan");
            template.ConfigureInteractionPoint(root.TransformPoint(new Vector3(-1.05f, 1.05f, -.2f)));
            user = new GameObject("Isolated car probe").transform; user.position = new Vector3(-60, 0, -50);
            Physics.SyncTransforms(); return city;
        }

        private static Transform Box(string name, Vector3 point, Vector3 size, Transform parent, bool local = false)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name;
            box.transform.SetParent(parent, false);
            if (local) box.transform.localPosition = point; else box.transform.position = point;
            box.transform.localScale = size; return box.transform;
        }
        private static void FreshScene() { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        private static void Set(object item, string field, object value) => item.GetType().GetField(field, Members).SetValue(item, value);
        private static void Call(object item, string method, params object[] arguments) => item.GetType().GetMethod(method, Members).Invoke(item, arguments);
        private static void Check(bool value, string description) { receipt.checks++; if (!value) throw new InvalidOperationException(description); }
        private static void CaptureLog(string message, string stack, LogType kind)
        { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) RuntimeErrors.Add(message); }
        private static string Argument(string key)
        { string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        [Serializable] private sealed class Receipt
        {
            public string startedAtUtc, finishedAtUtc;
            public bool passed;
            public int checks;
            public string scope = "Isolated batch Edit Mode; no Start, Play Mode, preferences, media/network, app launch or source scene save. Synthetic clearance negatives and shared-art ownership; saved-city doors/utilities prepared in memory, four admitted cars, bounded production acceleration/collision/exit. Not a whole-city driving/endurance or phone acceptance claim.";
            public List<string> savedCityCarPositions = new List<string>(), errors = new List<string>();
            public string[] runtimeErrors;
        }
    }
}
#endif
