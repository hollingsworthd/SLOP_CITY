using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>Bounded, wall-mounted utility history on the existing occupied street walls.</summary>
    public sealed class SlopCityStreetAtmosphere : MonoBehaviour
    {
        [SerializeField] private bool prepared;
        [SerializeField] private int decoratedBuildings;
        [SerializeField] private int servicePlaques;
        [SerializeField] private List<Mesh> ownedMeshes = new List<Mesh>();
        public bool Prepared => prepared;
        public int DecoratedBuildings => decoratedBuildings;
        public int ServicePlaques => servicePlaques;
        public int DetailMeshCount => ownedMeshes.Count;
        public const float MaximumWallProjection = .32f;

        public static void Prepare(Transform city)
        {
            if (!city) return;
            SlopCityStreetAtmosphere profile = city.GetComponent<SlopCityStreetAtmosphere>();
            if (!profile) profile = city.gameObject.AddComponent<SlopCityStreetAtmosphere>();
            if (!profile.prepared) profile.Build();
        }

        private void Build()
        {
            SlopCityInteriorGeometry.Initialize();
            Material iron = FindMaterial("BrushedSteel") ?? SlopCityInteriorGeometry.Metal;
            Material enamel = FindMaterial("TransitEnamel") ?? SlopCityInteriorGeometry.Floor;
            Material dark = FindMaterial("Rubber") ?? iron;
            // Snapshot before adding children. All detail belongs to the metric building,
            // so final street replotting carries it along with its real entrance and floors.
            SlopCityMetricFacade[] buildings = GetComponentsInChildren<SlopCityMetricFacade>(true);
            var stages = new List<Transform>();
            int initialMeshes = ownedMeshes.Count;
            int initialBuildings = decoratedBuildings, initialPlaques = servicePlaques;
            try
            {
                foreach (SlopCityMetricFacade facade in buildings)
                {
                    Transform building = facade.transform;
                    Transform core = building.Find("Structural_Core");
                    MeshFilter structural = core ? core.GetComponent<MeshFilter>() : null;
                    if (!facade.Prepared || !structural || !structural.sharedMesh) continue;
                    Vector3 size = structural.sharedMesh.bounds.size;
                    if (size.x < 6 || size.z < 6) continue;
                    int separator = building.name.LastIndexOf('_');
                    int.TryParse(separator >= 0 ? building.name.Substring(separator + 1) : "0", out int serial);
                    bool curtain = building.Find("CurtainWall_Panel") != null;
                    Transform stage = SlopCityInteriorGeometry.Root(building, "Street_Wall_Utility_History");
                    try { stages.Add(stage); }
                    catch { if (Application.isPlaying) Destroy(stage.gameObject); else DestroyImmediate(stage.gameObject); throw; }
                    var metal = new DetailBatch();
                    var boards = new DetailBatch();
                    var vents = new DetailBatch();
                    float front = -size.z * .5f;
                    // Thin drain risers sit in the corner piers. They begin above head height;
                    // no feet, bollards, wires across streets, or decorative collision hazards.
                    if (!curtain)
                        foreach (float side in new[] { -1f, 1f })
                        {
                            float x = side * (size.x * .5f - .19f);
                            metal.Box(new Vector3(x, 6.35f, front - .17f), new Vector3(.045f, 6.15f, .045f));
                            for (int bracket = 0; bracket < 4; bracket++)
                                metal.Box(new Vector3(x, 3.45f + bracket * 1.9f, front - .17f), new Vector3(.12f, .045f, .09f));
                        }
                    // A small louvred relay cabinet lives in the solid band between floor
                    // one's window head and floor two's sill, away from the central door/sign.
                    float serviceX = (serial % 2 == 0 ? 1 : -1) * size.x * .34f;
                    boards.Box(new Vector3(serviceX, 3.6f, front - .175f), new Vector3(.62f, .48f, .075f));
                    vents.Box(new Vector3(serviceX, 3.6f, front - .218f), new Vector3(.50f, .34f, .015f));
                    for (int rail = 0; rail < 6; rail++)
                        metal.Box(new Vector3(serviceX, 3.45f + rail * .06f, front - .236f), new Vector3(.50f, .015f, .025f));
                    // The adjacent tiny optical receiver is environmental art, not a new job,
                    // UI, simulated camera, light, or reward. Its silhouette suggests listening.
                    metal.Box(new Vector3(serviceX + .40f, 3.67f, front - .195f), new Vector3(.055f, .10f, .11f));
                    vents.Box(new Vector3(serviceX + .40f, 3.67f, front - .257f), new Vector3(.035f, .045f, .025f));
                    float plaqueX = -serviceX;
                    boards.Box(new Vector3(plaqueX, 3.58f, front - .151f), new Vector3(1.35f, .33f, .035f));
                    string code = (serial + 17).ToString("000");
                    // LegacyRuntime.ttf is the same retained font as the floor signs. ASCII
                    // text remains readable even on platforms without Cyrillic glyph support.
                    SlopCityInteriorGeometry.Sign(stage, "RELAY " + code + " / 1984\nLINE QUIET / LISTEN FIRST",
                        new Vector3(plaqueX, 3.58f, front - .174f), .055f, Quaternion.identity);
                    stage.GetChild(stage.childCount - 1).name = "Relay_Identification_Plaque";
                    metal.Install(stage, "Weathered_Iron_Servicework", iron, ownedMeshes);
                    boards.Install(stage, "Utility_Enamel_Panels", enamel, ownedMeshes);
                    vents.Install(stage, "Recessed_Relay_Grilles", dark, ownedMeshes);
                    decoratedBuildings++; servicePlaques++;
                }
            }
            catch
            {
                foreach (Transform stage in stages)
                    if (stage) { if (Application.isPlaying) Destroy(stage.gameObject); else DestroyImmediate(stage.gameObject); }
                for (int index = ownedMeshes.Count - 1; index >= initialMeshes; index--)
                {
                    Mesh mesh = ownedMeshes[index]; ownedMeshes.RemoveAt(index);
                    if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
                }
                decoratedBuildings = initialBuildings; servicePlaques = initialPlaques;
                throw;
            }
            prepared = true;
        }

        private Material FindMaterial(string name)
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    if (material && (material.name == name || material.name == name + " [Luminous City]")) return material;
            return null;
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in ownedMeshes)
                if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            ownedMeshes.Clear();
        }

        private sealed class DetailBatch
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();
            internal void Box(Vector3 center, Vector3 size)
            {
                Vector3 half = size * .5f;
                Vector3 a = center + new Vector3(-half.x, -half.y, -half.z);
                Vector3 b = center + new Vector3(half.x, -half.y, -half.z);
                Vector3 c = center + new Vector3(half.x, half.y, -half.z);
                Vector3 d = center + new Vector3(-half.x, half.y, -half.z);
                Vector3 depth = Vector3.forward * size.z;
                Quad(d, c, b, a); Quad(a + depth, b + depth, c + depth, d + depth);
                Quad(a, b, b + depth, a + depth); Quad(b, c, c + depth, b + depth);
                Quad(c, d, d + depth, c + depth); Quad(d, a, a + depth, d + depth);
            }
            private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                uv.Add(Vector2.zero); uv.Add(new Vector2((b - a).magnitude, 0));
                uv.Add(new Vector2((b - a).magnitude, (d - a).magnitude)); uv.Add(new Vector2(0, (d - a).magnitude));
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }
            internal void Install(Transform parent, string name, Material finish, List<Mesh> owned)
            {
                Mesh mesh = new Mesh();
                try { owned.Add(mesh); }
                catch { owned.Remove(mesh); if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); throw; }
                mesh.name = name + "_Batched"; mesh.indexFormat = IndexFormat.UInt16;
                mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                Transform part = SlopCityInteriorGeometry.Root(parent, name);
                part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                Renderer renderer = part.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = finish;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
                part.gameObject.isStatic = true;
            }
        }
    }
}
