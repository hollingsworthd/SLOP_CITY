#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity.Editor
{
    /// <summary>Localized sign spill and irregular rainwater, without adding walkable obstructions.</summary>
    public static class SlopCityMegacityAtmosphereBuilder
    {
        private const string Folder = "Assets/Art/MegacityAtmosphere";

        public static void Build(SlopCityDistrict[] districts)
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            Transform city = districts[0].transform.parent;
            BuildServiceBridges(city);
            foreach (Light light in city.GetComponentsInChildren<Light>())
                if (light.name == "Lamp_Pool")
                {
                    light.intensity = 1.38f;
                    light.range = 11.5f;
                    light.color = new Color(1f, .89f, .73f);
                }
            foreach (SlopCityDistrict district in districts)
            {
                Transform root = new GameObject("Megacity_StreetAtmosphere").transform;
                root.SetParent(district.transform, false);
                AddSpill(root, "Cyan_Sign_Spill", new Vector3(-12.8f, 4.2f, 2.7f), new Color(.38f, .73f, .91f), .95f);
                AddSpill(root, "Rose_Sign_Spill", new Vector3(12.8f, 4.8f, -7.4f), new Color(.95f, .43f, .44f), .65f);
            }
            Material water = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Rainwater.mat");
            if (!water)
            {
                water = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(water, Folder + "/Rainwater.mat");
            }
            water.color = new Color(.45f, .50f, .55f, .20f);
            water.mainTexture = WaterEdge();
            water.SetFloat("_Mode", 2);
            water.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            water.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            water.SetFloat("_ZWrite", 0);
            water.SetFloat("_Metallic", 0f);
            water.SetFloat("_Glossiness", .94f);
            water.DisableKeyword("_ALPHATEST_ON");
            water.EnableKeyword("_ALPHABLEND_ON");
            water.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            water.SetOverrideTag("RenderType", "Transparent");
            water.renderQueue = 3000;
            EditorUtility.SetDirty(water);
            // Replace the earlier opaque rain decals with the same shallow-water optics.
            foreach (Renderer earlier in city.GetComponentsInChildren<Renderer>())
                if (earlier.name == "Rain_Puddle") earlier.sharedMaterial = water;
            Transform rain = new GameObject("Megacity_Rainwater").transform;
            rain.SetParent(city, false);
            int index = 0;
            foreach (float street in new[] { -20f, 20f })
                foreach (float along in new[] { -49f, -31f, -9f, 9f, 31f, 49f })
                    foreach (bool horizontal in new[] { false, true })
                    {
                        var owner = new GameObject("Rainwater_Shallow_Patch_" + index);
                        owner.transform.SetParent(rain, false);
                        float side = index % 2 == 0 ? -2.45f : 2.45f;
                        owner.transform.localPosition = horizontal ? new Vector3(along, -.078f, street + side)
                            : new Vector3(street + side, -.078f, along);
                        owner.transform.localRotation = Quaternion.Euler(0, index * 39, 0);
                        owner.transform.localScale = new Vector3(1.3f + index % 3 * .31f, 1, 2.1f + index % 4 * .34f);
                        owner.AddComponent<MeshFilter>().sharedMesh = Puddle(index % 4);
                        MeshRenderer renderer = owner.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = water;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                        owner.isStatic = true;
                        index++;
                    }
            AssetDatabase.SaveAssets();
        }

        private static void AddSpill(Transform root, string name, Vector3 position, Color color, float strength)
        {
            var source = new GameObject(name);
            source.transform.SetParent(root, false);
            source.transform.localPosition = position;
            Light light = source.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = strength;
            light.range = 15f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
        }

        private static void BuildServiceBridges(Transform city)
        {
            Material steel = AssetDatabase.LoadAssetAtPath<Material>(SlopCityCityBuilder.MaterialsFolder + "/Metro_BrushedSteel.mat");
            Material glass = AssetDatabase.LoadAssetAtPath<Material>(SlopCityCityBuilder.MaterialsFolder + "/Metro_TransitWindowGlass.mat");
            Material lit = AssetDatabase.LoadAssetAtPath<Material>(SlopCityCityBuilder.MaterialsFolder + "/Metro_WarmLighting.mat");
            Transform[] towers = city.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("City_Skyscraper_"))
                .OrderBy(t => t.position.z).ThenBy(t => t.position.x).ToArray();
            var used = new System.Collections.Generic.HashSet<Transform>();
            int count = 0;
            foreach (Transform tower in towers)
            {
                if (used.Contains(tower)) continue;
                Transform other = towers.Where(t => t != tower && !used.Contains(t) && t.parent != tower.parent
                    && t.position.x > tower.position.x + 20 && t.position.x < tower.position.x + 45
                    && Mathf.Abs(t.position.z - tower.position.z) < 3)
                    .OrderBy(t => Vector3.Distance(t.position, tower.position)).FirstOrDefault();
                if (!other) continue;
                float length = Vector3.Distance(tower.position, other.position);
                Transform bridge = new GameObject("Megacity_Elevated_ServiceLink_" + count).transform;
                bridge.SetParent(city, false);
                bridge.position = (tower.position + other.position) * .5f + Vector3.up * (18.6f + count % 2 * 4.1f);
                bridge.rotation = Quaternion.LookRotation((other.position - tower.position).normalized);
                SlopCityArchitecturalMeshes.RoundedBox(bridge, "Bridge_Steel_Deck", Vector3.zero, new Vector3(2.85f, .35f, length), .05f, steel);
                SlopCityArchitecturalMeshes.RoundedBox(bridge, "Bridge_Roof", new Vector3(0, 2.9f, 0), new Vector3(2.9f, .20f, length), .04f, steel);
                int bays = Mathf.CeilToInt(length / 3.0f);
                float spacing = length / bays;
                foreach (float side in new[] { -1f, 1f })
                {
                    SlopCityArchitecturalMeshes.RoundedBox(bridge, "Bridge_Continuous_Sill", new Vector3(side * 1.35f, .43f, 0), new Vector3(.16f, .65f, length), .03f, steel, false);
                    SlopCityArchitecturalMeshes.RoundedBox(bridge, "Bridge_LightStrip", new Vector3(side * 1.3f, 2.77f, 0), new Vector3(.045f, .04f, length), .008f, lit, false);
                    for (int bay = 0; bay < bays; bay++)
                    {
                        float z = -length * .5f + bay * spacing;
                        SlopCityArchitecturalMeshes.RoundedBox(bridge, "Bridge_Frame", new Vector3(side * 1.35f, 1.6f, z), new Vector3(.14f, 2.4f, .12f), .015f, steel, false);
                        SlopCityArchitecturalMeshes.RoundedBox(bridge, "Bridge_Glazing", new Vector3(side * 1.34f, 1.7f, z + spacing * .5f), new Vector3(.028f, 1.9f, spacing - .14f), .008f, glass, false);
                        SlopCityArchitecturalMeshes.Tube(bridge, "Bridge_Truss", new[] { new Vector3(side * 1.40f, .35f, z), new Vector3(side * 1.40f, 2.85f, z + spacing) }, .045f, steel);
                    }
                }
                used.Add(tower); used.Add(other);
                if (++count >= 4) break;
            }
        }

        private static Mesh Puddle(int variant)
        {
            string path = Folder + "/Rainwater_FeatheredEdge_" + variant + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh) return mesh;
            const int segments = 40;
            var vertices = new Vector3[segments + 1];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 3];
            uv[0] = new Vector2(.5f, .5f);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                float radius = .81f + .12f * Mathf.Sin(angle * 3 + variant) + .07f * Mathf.Cos(angle * 7 + variant * 1.7f);
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                uv[i + 1] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .5f + Vector2.one * .5f;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % segments + 1;
                triangles[i * 3 + 2] = i + 1;
            }
            mesh = new Mesh { name = "Irregular_Rainwater_" + variant, vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Texture2D WaterEdge()
        {
            string path = Folder + "/Rainwater_FeatherMask.asset";
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture) return texture;
            const int size = 128;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
                { name = "Rainwater_FeatherMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float radius = new Vector2((x + .5f) / size * 2 - 1, (y + .5f) / size * 2 - 1).magnitude;
                    pixels[y * size + x] = new Color(1, 1, 1, Mathf.SmoothStep(0, 1, (1 - radius) * 6));
                }
            texture.SetPixels(pixels); texture.Apply(true, true);
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }
    }
}
#endif
