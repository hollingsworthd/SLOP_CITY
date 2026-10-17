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
    /// <summary>Isolated native mesh ownership fault injection and production floor/lift retirement.</summary>
    public static class SlopCityWorldResourceSmokeTest
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        private const string ReportPath = "Builds/Reports/world-resource-check.json";
        private static Receipt report;

        // Dedicated batch Editor outside Play Mode. No saved scene, player storage or network.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("World resources require a dedicated batch Editor outside Play Mode.");
            report = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            int exit = 0;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                FailedMetricClone();
                FailedScaleClone();
                StreamRetirement();
                report.success = true;
            }
            catch (Exception error) { report.error = error.ToString(); exit = 1; }
            finally
            {
                report.completedAtUtc = DateTime.UtcNow.ToString("O");
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            }
            Debug.Log("World resource checks: " + report.checks + "; success=" + report.success);
            EditorApplication.Exit(exit);
        }

        private static void FailedMetricClone()
        {
            Type geometry = typeof(SlopCityBuildingWorld).Assembly.GetType("SlopCity.SlopCityInteriorGeometry", true);
            MethodInfo apply = geometry.GetMethod("ApplyMetricSurface", Members);
            const string marker = "ResourceFault_Metric";
            Mesh source = IncompleteNormals(marker + "_Source");
            GameObject owner = new GameObject(marker);
            owner.AddComponent<MeshFilter>().sharedMesh = source;
            owner.AddComponent<MeshRenderer>();
            try
            {
                for (int cycle = 0; cycle < 12; cycle++)
                {
                    ExpectNormalsFailure(() => apply.Invoke(null, new object[] { owner }));
                    Check(owner.GetComponent<MeshFilter>().sharedMesh == source, "Failed UV mapping keeps the original source bound.");
                    Check(CountMeshes(marker + "_WorldMetreUV") == 0, "Failed UV mapping immediately retires its native clone.");
                    report.metricFaultCycles++;
                }
            }
            finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(source); }
        }

        private static void FailedScaleClone()
        {
            FieldInfo owned = typeof(SlopCityHumanScale).GetField("ownedMeshes", Members);
            const string marker = "ResourceFault_Scale";
            Mesh source = IncompleteNormals(marker + "_Source");
            Shader shader = Shader.Find("Standard");
            Check(shader, "The production Standard surface shader is available.");
            Material scanned = new Material(shader);
            Texture2D texture = new Texture2D(1, 1) { name = marker + "_albedo" };
            scanned.mainTexture = texture;
            try
            {
                for (int cycle = 0; cycle < 12; cycle++)
                {
                    GameObject owner = new GameObject(marker);
                    try
                    {
                        // Exercise the actual Apply transaction, including its existing
                        // catch/undo/dispose rollback, instead of relying on edit-mode
                        // MonoBehaviour destruction to invoke play-mode OnDestroy.
                        GameObject asphalt = Child(owner.transform, "City_Asphalt");
                        var filter = asphalt.AddComponent<MeshFilter>(); filter.sharedMesh = source;
                        asphalt.AddComponent<MeshRenderer>().sharedMaterial = scanned;
                        GameObject home = Child(owner.transform, "Floor_Main");
                        home.AddComponent<MeshFilter>().sharedMesh = source;
                        home.AddComponent<MeshRenderer>().sharedMaterial = scanned;
                        var sidewalks = new List<GameObject>();
                        var districts = new List<SlopCityDistrict>();
                        var positions = new List<Vector3>();
                        for (int index = 0; index < 9; index++)
                        {
                            GameObject district = Child(owner.transform, "ResourceDistrict_" + index);
                            district.transform.localPosition = new Vector3(index % 3 * 35, 0, index / 3 * 35);
                            var identity = district.AddComponent<SlopCityDistrict>();
                            identity.Configure(index % 3, index / 3, "Resource district", district.transform.position);
                            districts.Add(identity); positions.Add(district.transform.position);
                            GameObject sidewalk = Child(district.transform, "Sidewalk");
                            sidewalk.AddComponent<MeshFilter>().sharedMesh = source;
                            sidewalk.AddComponent<MeshRenderer>().sharedMaterial = scanned;
                            sidewalks.Add(sidewalk);
                        }
                        int originals = owner.GetComponentsInChildren<Transform>(true).Length;
                        bool threw = false;
                        try { SlopCityHumanScale.Prepare(owner.transform); }
                        catch (IndexOutOfRangeException) { threw = true; }
                        Check(threw, "Actual city preparation reaches the post-clone asphalt UV failure.");
                        var profile = owner.GetComponent<SlopCityHumanScale>();
                        Check(profile && !profile.Prepared, "Failed city preparation stays retryable.");
                        Check(((List<Mesh>)owned.GetValue(profile)).Count == 0, "Actual transaction rollback empties native mesh ownership.");
                        Check(filter.sharedMesh == source && CountMeshes(source.name + "_MetricPlots") == 0,
                            "Actual rollback retires the failed clone and keeps the original mesh.");
                        Check(owner.GetComponentsInChildren<Transform>(true).Length == originals,
                            "Actual rollback removes generated pavement objects.");
                        for (int index = 0; index < 9; index++)
                            Check(sidewalks[index].activeSelf && districts[index].transform.position == positions[index],
                                "Actual rollback restores district coordinates and authored sidewalk visibility.");
                    }
                    finally { Object.DestroyImmediate(owner); }
                    report.scaleFaultCycles++;
                }
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(scanned); Object.DestroyImmediate(texture); }
        }

        private static GameObject Child(Transform parent, string name)
        {
            var child = new GameObject(name); child.transform.SetParent(parent, false); return child;
        }

        private static void StreamRetirement()
        {
            GameObject owner = new GameObject("Resource_Stream_Owner");
            Mesh source = IncompleteNormals("Resource_Stream_Core");
            // Production occupied plans require at least 12.5m on both axes.
            source.bounds = new Bounds(Vector3.up * 4.8f, new Vector3(16, 9.6f, 16));
            try
            {
                var world = owner.AddComponent<SlopCityBuildingWorld>();
                GameObject tower = new GameObject("City_Skyscraper_900"); tower.transform.SetParent(owner.transform, false);
                GameObject core = new GameObject("Structural_Core"); core.transform.SetParent(tower.transform, false);
                core.AddComponent<MeshFilter>().sharedMesh = source;
                var building = tower.AddComponent<SlopCityBuildingInterior>();
                Type geometry = typeof(SlopCityBuildingWorld).Assembly.GetType("SlopCity.SlopCityInteriorGeometry", true);
                geometry.GetMethod("Initialize", Members).Invoke(null, null);
                typeof(SlopCityBuildingInterior).GetMethod("InitializeTower", Members).Invoke(building, new object[] { world, null });
                int permanentObjects = tower.GetComponentsInChildren<Transform>(true).Length;
                int warmedMaterials = -1, warmedMeshes = -1;
                for (int cycle = 0; cycle < 12; cycle++)
                {
                    SlopCityElevator elevator = building.EnsureElevator();
                    building.EnsureFloor(1); building.EnsureFloor(2);
                    Check(building.LoadedFloorCount == 3 && elevator, "Three real floors and one physical lift load.");
                    Transform[] floors = new Transform[3];
                    for (int floor = 0; floor < 3; floor++) floors[floor] = building.EnsureFloor(floor);
                    typeof(SlopCityBuildingInterior).GetMethod("ReleaseStream", Members).Invoke(building, null);
                    Check(building.LoadedFloorCount == 0 && !building.Elevator && !elevator, "Production retirement removes all floors and the empty lift.");
                    foreach (Transform floor in floors) Check(!floor, "Retired floor GameObjects are destroyed.");
                    Check(tower.GetComponentsInChildren<Transform>(true).Length == permanentObjects, "Repeated streaming retains only the permanent shell/routes.");
                    int materials = Resources.FindObjectsOfTypeAll<Material>().Length;
                    int meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length;
                    if (cycle == 0) { warmedMaterials = materials; warmedMeshes = meshes; }
                    else
                    {
                        Check(materials == warmedMaterials, "Repeated streaming allocates no retained materials after cache warmup.");
                        Check(meshes == warmedMeshes, "Repeated streaming allocates no retained meshes after cache warmup.");
                    }
                    report.streamingCycles++;
                }
            }
            finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(source); }
        }

        private static Mesh IncompleteNormals(string name)
        {
            // Missing normals force the production UV routine to throw AFTER cloning.
            // This deterministically probes the same ownership boundary as an allocation
            // failure during array reads/uploads; it does not reproduce device OOM.
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            return mesh;
        }
        private static void ExpectNormalsFailure(Action operation)
        {
            bool threw = false;
            try { operation(); }
            catch (TargetInvocationException error) when (error.InnerException is IndexOutOfRangeException) { threw = true; }
            Check(threw, "Fault injection reaches the production post-clone UV failure.");
        }
        private static int CountMeshes(string name)
        {
            int count = 0;
            foreach (Mesh mesh in Resources.FindObjectsOfTypeAll<Mesh>()) if (mesh && mesh.name == name) count++;
            return count;
        }
        private static void Check(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException(detail);
            report.checks++;
        }
        [Serializable] private sealed class Receipt
        {
            public string startedAtUtc, completedAtUtc, unityVersion, error;
            public bool success;
            public int checks, metricFaultCycles, scaleFaultCycles, streamingCycles;
        }
    }
}
#endif
