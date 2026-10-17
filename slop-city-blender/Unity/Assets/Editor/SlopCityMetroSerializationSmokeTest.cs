#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Actual prepared graph saved/reopened; temporary assets bridge Editor-only transient resource persistence.</summary>
    public static class SlopCityMetroSerializationSmokeTest
    {
        private const string Folder = "Assets/__SlopCityMetroSerializationFixture";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static Receipt receipt;
        private static bool createdFolder;

        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode
                || Application.companyName != "SlopCityValidation-v0116"
                || !Application.persistentDataPath.Contains(Path.DirectorySeparatorChar + "SlopCityValidation-v0116" + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Serialization checks require isolated SlopCityValidation-v0116 batch Edit Mode.");
            receipt = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            var random = UnityEngine.Random.state;
            string sceneFile = SlopCitySceneBuilder.ScenePath;
            string sceneHash = FileHash(sceneFile);
            Application.logMessageReceived += Log;
            try
            {
                Check(!Directory.Exists(Folder) && !File.Exists(Folder + ".meta"), "Disposable fixture path is unused.");
                var scene = EditorSceneManager.OpenScene(sceneFile, OpenSceneMode.Single);
                foreach (var sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None)) sync.enabled = false;
                foreach (var media in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None)) media.enabled = false;
                foreach (var player in Object.FindObjectsByType<SlopCityFirstPerson>(FindObjectsInactive.Include, FindObjectsSortMode.None)) player.enabled = false;
                SlopCityWebGeometryBuildProcessor.ProtectRuntimeBuildingGeometry(scene);
                Transform sourceCity = GameObject.Find("SlopCity_NineBlocks").transform;
                SlopCityMetroWorld world = sourceCity.GetComponentInChildren<SlopCityMetroWorld>(true);
                Validate(world);
                string[] hashes = MeshHashes(world);
                string[] legacy = world.Platforms.Select(item => item.GetComponent<SlopCityMetroPlatform>().LegacyRouteId).ToArray();
                Vector3[] positions = world.Stations.Select(item => item.position).ToArray();
                Quaternion[] rotations = world.Stations.Select(item => item.rotation).ToArray();
                string[] names = Get<Transform[]>(world, "models").Select(item => item.name).ToArray();
                Check(legacy.Count(value => !string.IsNullOrEmpty(value)) == 2, "Two original platform route identities are serialized.");

                // Move only the prepared network, never save or modify the authored .unity on disk.
                var temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var temporaryCity = new GameObject("SlopCity_NineBlocks");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(temporaryCity, temporary);
                temporaryCity.transform.SetPositionAndRotation(sourceCity.position, sourceCity.rotation);
                temporaryCity.transform.localScale = sourceCity.lossyScale;
                world.transform.SetParent(null, true);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(world.gameObject, temporary);
                world.transform.SetParent(temporaryCity.transform, true);
                AssetDatabase.CreateFolder("Assets", "__SlopCityMetroSerializationFixture");
                createdFolder = true;
                int resource = 0;
                foreach (Object dependency in EditorUtility.CollectDependencies(new Object[] { temporaryCity }).Distinct())
                {
                    if (EditorUtility.IsPersistent(dependency) || (!(dependency is Mesh) && !(dependency is Material))) continue;
                    AssetDatabase.CreateAsset(dependency, Folder + "/Resource_" + resource++ + ".asset");
                }
                receipt.temporaryAssetCount = resource;
                // Ownership transfers to disposable Editor assets, not to a second runtime world.
                Set(world, "ownsTrackMeshes", false);
                Check(MeshHashes(world).SequenceEqual(hashes), "Temporary assetization preserves exact track mesh bytes.");
                AssetDatabase.SaveAssets();
                Check(EditorSceneManager.SaveScene(temporary, Folder + "/PreparedMetro.unity"), "Prepared temporary scene saves.");
                EditorSceneManager.OpenScene(Folder + "/PreparedMetro.unity", OpenSceneMode.Single);
                world = GameObject.Find("SlopCity_NineBlocks").GetComponentInChildren<SlopCityMetroWorld>(true);
                Validate(world);
                Check(MeshHashes(world).SequenceEqual(hashes), "All eighteen track meshes survive save/reopen byte exactly.");
                Check(world.Platforms.Select(item => item.GetComponent<SlopCityMetroPlatform>().LegacyRouteId).SequenceEqual(legacy), "Legacy route strings survive save/reopen.");
                Check(Get<Transform[]>(world, "models").Select(item => item.name).SequenceEqual(names), "Three original carriage identities survive.");
                for (int index = 0; index < 6; index++)
                {
                    Check(world.Stations[index].position == positions[index] && world.Stations[index].rotation == rotations[index], "Station pose survives save/reopen: " + index);
                }
                Check(!Get<bool>(world, "initialized") && !Get<bool>(world, "ownsTrackMeshes"), "Reopened prepared world has no stale runtime initialization or resource ownership.");
                Check(SlopCityMetroWorld.Prepare(world.transform.parent) == world, "Prepare recognizes serialized marker without rebuilding.");
                Check(MeshHashes(world).SequenceEqual(hashes), "Repeated Prepare does not replace track geometry.");
                var playerObject = new GameObject("SerializationFixturePlayer");
                playerObject.AddComponent<CharacterController>();
                var owner = playerObject.AddComponent<SlopCityFirstPerson>(); owner.enabled = false;
                Set(owner, "paused", true); Set(owner, "seated", false);
                Check(SlopCityMetroWorld.Ensure(owner) == world && world.Trains.Count == 3, "Reopened graph initializes exactly three train controllers.");
                var trains = world.Trains.ToArray();
                Check(SlopCityMetroWorld.Ensure(owner) == world && world.Trains.SequenceEqual(trains), "Repeated Ensure preserves controller identities.");
                foreach (var train in trains)
                    Check(train.World == world && !train.IsMoving && train.GetComponentsInChildren<Transform>(true).Count(part => part.name.StartsWith("Metro_SlidingDoor_", StringComparison.Ordinal)) == 2, "Each restored train owns two runtime door leaves.");
                Check(MeshHashes(world).SequenceEqual(hashes), "Runtime train setup preserves the serialized rail payload.");
                Check(FileHash(sceneFile) == sceneHash, "Authored source scene bytes remain unchanged.");
                Check(receipt.runtimeErrors.Count == 0, "No runtime errors or assertions.");
            }
            catch (Exception error) { receipt.errors.Add(error.ToString()); }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (createdFolder && !AssetDatabase.DeleteAsset(Folder)) receipt.errors.Add("Disposable fixture asset deletion failed.");
                receipt.temporaryAssetsRemoved = !Directory.Exists(Folder) && !File.Exists(Folder + ".meta");
                if (createdFolder && !receipt.temporaryAssetsRemoved) receipt.errors.Add("Disposable scene/assets remain.");
                UnityEngine.Random.state = random;
                Application.logMessageReceived -= Log;
            }
            receipt.passed = receipt.errors.Count == 0 && receipt.runtimeErrors.Count == 0;
            receipt.finishedAtUtc = DateTime.UtcNow.ToString("O");
            string report = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports/metro-serialization-check.json");
            Directory.CreateDirectory(Path.GetDirectoryName(report)); File.WriteAllText(report, JsonUtility.ToJson(receipt, true));
            Debug.Log("SLOP_CITY_METRO_SERIALIZATION_" + (receipt.passed ? "PASSED" : "FAILED") + " " + receipt.checks + " checks");
            EditorApplication.Exit(receipt.passed ? 0 : 1);
        }

        private static void Validate(SlopCityMetroWorld world)
        {
            Check(world && world.Prepared && MonoScript.FromMonoBehaviour(world).GetClass() == typeof(SlopCityMetroWorld), "Prepared world has a typed script binding.");
            Check(world.Stations.Count == 6 && world.Destinations.Count == 6 && world.Platforms.Count == 6, "Six ordered graph entries persist.");
            Check(world.Trains.Count == 0 && world.GetComponentsInChildren<SlopCityMetroTrain>(true).Length == 0, "Build preparation has no runtime controllers.");
            for (int index = 0; index < 6; index++)
            {
                var platform = world.Platforms[index].GetComponent<SlopCityMetroPlatform>();
                Check(platform && platform.World == world && platform.StopIndex == index, "Platform typed reference/index: " + index);
                Check(MonoScript.FromMonoBehaviour(platform).GetClass() == typeof(SlopCityMetroPlatform)
                    && AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(platform)) == "Assets/Scripts/SlopCityMetroPlatform.cs", "Platform has matching-file script binding: " + index);
                Check(world.Platforms[index].Destination == world.Destinations[(index + 1) % 6], "Platform references next fixed station: " + index);
                Check(world.Stations[index].IsChildOf(world.transform) && world.Destinations[index].IsChildOf(world.Stations[index]), "Station graph reference ownership: " + index);
            }
            var models = Get<Transform[]>(world, "models");
            Check(models.Length == 3 && models.Distinct().Count() == 3, "Three distinct serialized carriage models.");
            foreach (var model in models)
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                    Check(GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) == 0 && !renderer.gameObject.isStatic && !renderer.isPartOfStaticBatch, "Moving carriage renderer is excluded from static batching: " + renderer.name);
            foreach (var part in world.GetComponentsInChildren<Transform>(true))
                Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(part.gameObject) == 0, "No missing graph script: " + part.name);
            Check(Get<TextMesh[]>(world, "statusLabels").Length == 12 && Get<TextMesh[]>(world, "statusLabels").All(label => label), "All twelve station status labels persist.");
            Check(Get<Light[]>(world, "localStationLights").Length == 4 && Get<Light[]>(world, "localStationLights").All(light => light), "Four bounded additional-station lights persist.");
        }

        private static string[] MeshHashes(SlopCityMetroWorld world)
        {
            Mesh[] meshes = Get<Mesh[]>(world, "trackMeshes");
            Check(meshes.Length == 18 && meshes.All(mesh => mesh), "Eighteen owned rail/bed/envelope meshes persist.");
            foreach (Mesh mesh in meshes)
            {
                var filter = world.GetComponentsInChildren<MeshFilter>(true).Single(item => item.sharedMesh == mesh);
                Check(filter.GetComponent<MeshCollider>() && filter.GetComponent<MeshCollider>().sharedMesh == mesh, "Rail render/collision payload identity: " + mesh.name);
            }
            return meshes.Select(mesh => SlopCityDetailMeshCodec.ComputeHash(SlopCityDetailMeshCodec.Encode(mesh))).ToArray();
        }
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, Hidden).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); receipt.checks++; }
        private static string FileHash(string file) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        private static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception) receipt.runtimeErrors.Add(message + "\n" + trace); }
        [Serializable] private sealed class Receipt
        {
            public bool passed, temporaryAssetsRemoved;
            public int checks, temporaryAssetCount;
            public string startedAtUtc, finishedAtUtc, unityVersion;
            public string scope = "Actual prepared graph saved/reopened in disposable Editor scene. Transient meshes/materials temporarily assetized without changing bytes; this does not substitute for exported UnityFS mesh verification. No Play Mode, network, physical traversal or preference changes.";
            public List<string> errors = new List<string>(), runtimeErrors = new List<string>();
        }
    }
}
#endif
