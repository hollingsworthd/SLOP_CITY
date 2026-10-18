#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SlopCity.Editor
{
    /// <summary>Physical surface scale, scanned environment lighting and local architectural reflections.</summary>
    public static class SlopCityRealismBuilder
    {
        public const string Folder = "Assets/Art/WorldRendering";

        public static void Apply(Scene scene, Bounds loungeFloor)
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            Renderer[] renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
            foreach (Material material in renderers.SelectMany(renderer => renderer.sharedMaterials).Where(material => material).Distinct())
            {
                // These authored kits already bind metric PBR maps and deliberately
                // tune paint/metal roughness; do not replace their material decisions.
                if (material.name.StartsWith("Habitat_", StringComparison.Ordinal)
                    || material.name.StartsWith("MilitarySF_", StringComparison.Ordinal)) continue;
                bool loungeMaterial = AssetDatabase.GetAssetPath(material).Contains("SlopCityGenerated/");
                SlopCitySurfaceLibrary.Apply(material, material.name, loungeMaterial);
            }
            var corrected = new Dictionary<string, Mesh>();
            foreach (Renderer renderer in renderers)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh || !renderer.sharedMaterials.Any(SlopCitySurfaceLibrary.IsScanned)) continue;
                if (AssetDatabase.GetAssetPath(filter.sharedMesh) == SlopCitySceneBuilder.ModelPath)
                {
                    ApplyAuthoredMetricUV(filter);
                    continue;
                }
                if (filter.sharedMesh.name != "Cube") continue;
                Vector3 scale = renderer.transform.lossyScale;
                string key = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F3}_{1:F3}_{2:F3}", scale.x, scale.y, scale.z);
                if (!corrected.TryGetValue(key, out Mesh mapped))
                {
                    string path = Folder + "/MetricCube_" + Hash128.Compute(key) + ".asset";
                    mapped = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (!mapped)
                    {
                        mapped = UnityEngine.Object.Instantiate(filter.sharedMesh);
                        mapped.name = "Metre_UV_Cube_" + key;
                        Vector3[] vertices = mapped.vertices, normals = mapped.normals;
                        var uv = new Vector2[vertices.Length];
                        for (int i = 0; i < vertices.Length; i++)
                        {
                            Vector3 position = Vector3.Scale(vertices[i], scale), normal = normals[i];
                            uv[i] = Mathf.Abs(normal.y) > .5f ? new Vector2(position.x, position.z)
                                : Mathf.Abs(normal.x) > .5f ? new Vector2(position.z * -Mathf.Sign(normal.x), position.y)
                                : new Vector2(position.x * Mathf.Sign(normal.z), position.y);
                        }
                        mapped.uv = uv;
                        mapped.RecalculateTangents();
                        AssetDatabase.CreateAsset(mapped, path);
                    }
                    corrected[key] = mapped;
                }
                filter.sharedMesh = mapped;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
            }
            Transform environment = new GameObject("City_EnvironmentReflections").transform;
            BakeProbe(environment, "Street_Reflection", new Vector3(20, 4, 20), new Vector3(165, 155, 165), new Vector3(-20, 51, -20), false, 0);
            foreach (float x in new[] { -20f, 20f })
                foreach (float z in new[] { -20f, 20f })
                    BakeProbe(environment, "Junction_Reflection_" + (x < 0 ? "W" : "E") + (z < 0 ? "S" : "N"),
                        new Vector3(x, 3.3f, z), new Vector3(66, 22, 66), new Vector3(0, 6, 0), true, 1);
            BakeProbe(environment, "Lounge_Reflection", new Vector3(loungeFloor.center.x, loungeFloor.max.y + 1.8f, loungeFloor.center.z),
                new Vector3(loungeFloor.size.x + .5f, 4, loungeFloor.size.z + .5f), Vector3.zero, true, 2);
            foreach (Transform station in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>())
                .Where(item => item.name.StartsWith("Subway_Station_")).ToArray())
                BakeProbe(environment, "Reflection_" + station.name, station.position + new Vector3(0, -6.25f, 0), new Vector3(15.2f, 3.6f, 11.2f), Vector3.zero, true, 3);
            AssetDatabase.SaveAssets();
        }

        private static void ApplyAuthoredMetricUV(MeshFilter filter)
        {
            Mesh source = filter.sharedMesh;
            string identity = source.name + "_" + filter.transform.localToWorldMatrix.ToString();
            string path = Folder + "/LoungeMetric_" + Hash128.Compute(identity) + ".asset";
            Mesh mapped = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mapped)
            {
                mapped = UnityEngine.Object.Instantiate(source);
                mapped.name = "Metric_" + source.name;
                Vector3[] vertices = mapped.vertices, normals = mapped.normals;
                var uv = new Vector2[vertices.Length];
                for (int index = 0; index < vertices.Length; index++)
                {
                    Vector3 position = filter.transform.TransformPoint(vertices[index]);
                    Vector3 normal = filter.transform.TransformDirection(normals[index]);
                    Vector3 absolute = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
                    uv[index] = absolute.y >= absolute.x && absolute.y >= absolute.z ? new Vector2(position.x, position.z)
                        : absolute.x >= absolute.z ? new Vector2(position.z * -Mathf.Sign(normal.x), position.y)
                        : new Vector2(position.x * Mathf.Sign(normal.z), position.y);
                }
                mapped.uv = uv;
                mapped.RecalculateTangents();
                AssetDatabase.CreateAsset(mapped, path);
            }
            filter.sharedMesh = mapped;
        }

        private static void BakeProbe(Transform parent, string name, Vector3 position, Vector3 size, Vector3 center, bool boxProjection, int importance)
        {
            var owner = new GameObject(name);
            owner.transform.SetParent(parent, false);
            owner.transform.position = position;
            ReflectionProbe probe = owner.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.resolution = 256;
            probe.hdr = true;
            probe.size = size;
            probe.center = center;
            probe.boxProjection = boxProjection;
            probe.blendDistance = boxProjection ? .5f : 12;
            probe.importance = importance;
            probe.intensity = .82f;
            probe.nearClipPlane = .12f;
            probe.farClipPlane = 380;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            string path = Folder + "/" + name + ".exr";
            if (!Lightmapping.BakeReflectionProbe(probe, path)) throw new InvalidOperationException("Could not bake architectural reflection: " + name);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            // The bakedTexture setter is transient without a scene LightingDataAsset. A custom
            // baked texture is serialized directly and survives reopening the generated scene.
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (!probe.customBakedTexture) throw new InvalidOperationException("Reflection cubemap was not saved: " + path);
        }
    }
}
#endif
