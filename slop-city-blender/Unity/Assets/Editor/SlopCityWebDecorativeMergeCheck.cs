#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    public static class SlopCityWebDecorativeMergeCheck
    {
        private static int checks;
        private static readonly string[] Names = { "Weathered_Compressor_Casing", "Fan_Dark_Recess", "Fan_Guard_Ring", "Fan_Guard_Ring", "Fan_Guard_Ring", "Fan_Guard_Ring", "Fan_Guard_Spoke", "Fan_Guard_Spoke", "Fan_Guard_Spoke", "Compressor_Side_Louvre", "Compressor_Side_Louvre", "Compressor_Side_Louvre", "Compressor_Side_Louvre", "Compressor_Side_Louvre", "Compressor_Side_Louvre", "Compressor_Side_Louvre", "Compressor_Mounting_Rail", "Compressor_Mounting_Rail", "Insulated_Refrigerant_Loop" };
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Decorative merge requires a dedicated batch Editor outside Play Mode.");
            var receipt = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            int exit = 0;
            try
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ExactGeometry(scene);
                GuardExceptions(scene);
                PassiveGeometry(scene);
                // Read and mutate only this unsaved Editor scene copy. Never save source assets/scenes.
                scene = EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                int originals = scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<MeshRenderer>(true).Length);
                var result = SlopCityWebDecorativeMerge.Prepare(scene);
                Check(result.assemblies - result.passiveAssemblies == 48 && result.sourceRenderers - result.passiveSourceRenderers == 912
                    && result.mergedRenderers - result.passiveMergedRenderers == 144, "Actual authored city retains the exact48 compressor912-to144 conversion.");
                Check(result.passiveSourceRenderers - result.passiveMergedRenderers >= 500, "Positive passive assemblies remove at least500 further renderer registrations without dropping art.");
                Check(scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<MeshRenderer>(true).Length) == originals
                    - result.sourceRenderers + result.mergedRenderers, "Actual city renderer count exactly reconciles all committed assembly conversions.");
                receipt.actualAssemblies = result.assemblies; receipt.actualSourceRenderers = result.sourceRenderers; receipt.actualMergedRenderers = result.mergedRenderers;
                receipt.passiveAssemblies = result.passiveAssemblies; receipt.passiveSourceRenderers = result.passiveSourceRenderers; receipt.passiveMergedRenderers = result.passiveMergedRenderers;
                receipt.success = true;
            }
            catch (Exception error) { receipt.error = error.ToString(); exit = 1; }
            finally
            {
                receipt.checks = checks; receipt.completedAtUtc = DateTime.UtcNow.ToString("O");
                Directory.CreateDirectory("Builds/Reports"); File.WriteAllText("Builds/Reports/web-decorative-merge-check.json", JsonUtility.ToJson(receipt, true));
            }
            Debug.Log("Web decorative merge checks: " + checks + "; success=" + receipt.success);
            EditorApplication.Exit(exit);
        }

        private static void ExactGeometry(Scene scene)
        {
            var meshes = new List<Mesh>(); var materials = new List<Material>();
            GameObject city = FixtureRoot();
            try
            {
                Transform assembly = Assembly(city.transform.Find("City_JapanMetropolis"), meshes, materials);
                Transform[] originals = assembly.Cast<Transform>().ToArray();
                var before = Triangles(assembly);
                Material[] originalMaterials = materials.ToArray();
                var result = SlopCityWebDecorativeMerge.Prepare(scene);
                Check(result.assemblies == 1 && result.sourceRenderers == 19 && result.mergedRenderers == 3, "Strict decorative whitelist merges only three identical material/state groups.");
                var after = Triangles(assembly);
                Check(before.Count == after.Count, "Every original material group remains.");
                foreach (var group in before)
                {
                    Check(after.TryGetValue(group.Key, out List<Vertex> vertices) && vertices.Count == group.Value.Count, "Every indexed triangle/vertex remains within its exact material identity.");
                    for (int index = 0; index < group.Value.Count; index++)
                    {
                        Vertex a = group.Value[index], b = vertices[index];
                        Check((a.point - b.point).sqrMagnitude < 1e-9f && (a.normal - b.normal).sqrMagnitude < 1e-9f
                            && (a.tangent - b.tangent).sqrMagnitude < 1e-9f && (a.uv - b.uv).sqrMagnitude < 1e-12f,
                            "World position/normal/tangent handedness and full UV coordinates match original indexed art.");
                    }
                }
                foreach (Transform original in originals) Check(original && original.parent == assembly && !original.GetComponent<MeshFilter>() && !original.GetComponent<MeshRenderer>(), "Every original placement Transform remains without redundant rendering bindings.");
                Check(originalMaterials.SequenceEqual(materials), "No material is cloned or modified.");
                result = SlopCityWebDecorativeMerge.Prepare(scene);
                Check(result.assemblies == 0 && assembly.GetComponentsInChildren<MeshRenderer>().Length == 3, "Repeated preparation is idempotent.");
            }
            finally { DestroyOutputs(city); foreach (Mesh mesh in meshes) Object.DestroyImmediate(mesh); foreach (Material material in materials) Object.DestroyImmediate(material); }
        }

        private static void GuardExceptions(Scene scene)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                var meshes = new List<Mesh>(); var materials = new List<Material>(); GameObject city = FixtureRoot(); GameObject external = new GameObject("ExternalOwner");
                try
                {
                    Transform assembly = Assembly(city.transform.Find("City_JapanMetropolis"), meshes, materials);
                    Transform first = assembly.GetChild(0); MeshRenderer renderer = first.GetComponent<MeshRenderer>();
                    if (guard == 0) first.gameObject.AddComponent<BoxCollider>();
                    if (guard == 1) first.localScale = new Vector3(1, 2, 1);
                    if (guard == 2) assembly.gameObject.AddComponent<SlopCityInteractable>();
                    if (guard == 3) external.AddComponent<MeshCollider>().sharedMesh = first.GetComponent<MeshFilter>().sharedMesh;
                    if (guard == 4)
                    {
                        var display = external.AddComponent<SlopCityCityScreen>(); var serialized = new SerializedObject(display);
                        serialized.FindProperty("display").objectReferenceValue = renderer; serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                    if (guard == 5) { var block = new MaterialPropertyBlock(); block.SetFloat("_Glossiness", .7f); renderer.SetPropertyBlock(block); }
                    if (guard == 6) first.name = "Unknown_Art";
                    if (guard == 7) renderer.enabled = false;
                    var result = SlopCityWebDecorativeMerge.Prepare(scene);
                    Check(result.assemblies == 0 && assembly.GetComponentsInChildren<MeshRenderer>(true).Length == 19, "Collider/mutable/reference/material-block/unknown/inactive exceptions preserve original ownership and art.");
                    Check(first.GetComponent<MeshFilter>() && first.GetComponent<MeshRenderer>(), "Rejected assemblies retain their original bindings.");
                }
                finally { Object.DestroyImmediate(external); DestroyOutputs(city); foreach (Mesh mesh in meshes) Object.DestroyImmediate(mesh); foreach (Material material in materials) Object.DestroyImmediate(material); }
            }
        }

        private static void PassiveGeometry(Scene scene)
        {
            string[] roots = { "Japan_Sign_0_001", "Japan_VideoBillboard_0_0", "Megacity_Elevated_ServiceLink_0",
                "Subway_Station_0", "Subway_Train_0", "Subway_SeatBank_L", "Platform_Luminaire_0_0" };
            string[] parts = { "Galvanized_Mounting_Bracket", "Display_Cooling_Louvre", "Bridge_Frame",
                "Tile_Expansion_Joint", "Passenger_HandLoop_0", "Seat_EndRail_0", "Fluorescent_Tube_0" };
            for (int family = 0; family < roots.Length; family++)
            {
                var meshes = new List<Mesh>(); var materials = new List<Material>(); GameObject city = FixtureRoot();
                try
                {
                    Transform subway = new GameObject("SlopCity_Subway").transform; subway.SetParent(city.transform, false);
                    Transform parent = family < 2 ? city.transform.Find("City_JapanMetropolis") : family == 2 ? city.transform : subway;
                    Transform assembly = new GameObject(roots[family]).transform; assembly.SetParent(parent, false);
                    assembly.localPosition = new Vector3(-2, 6, 3); assembly.localRotation = Quaternion.Euler(0, -45, 0);
                    if (family == 0 || family == 5)
                    {
                        var action = assembly.gameObject.AddComponent<SlopCityInteractable>();
                        action.Configure(family == 0 ? SlopCityInteractionKind.Inspect : SlopCityInteractionKind.Bench, "Preserved root interaction", false);
                        action.ConfigureInteractionPoint(assembly.position);
                    }
                    Material material = new Material(Shader.Find("Standard")); materials.Add(material);
                    var originals = new List<Transform>();
                    for (int index = 0; index < 3; index++)
                    {
                        GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube); Mesh borrowed = primitive.GetComponent<MeshFilter>().sharedMesh;
                        Mesh mesh = new Mesh { vertices = borrowed.vertices, normals = borrowed.normals, uv = borrowed.uv, uv2 = borrowed.uv2, triangles = borrowed.triangles };
                        mesh.RecalculateTangents(); Object.DestroyImmediate(primitive); meshes.Add(mesh);
                        var child = new GameObject(parts[family]); child.transform.SetParent(assembly, false);
                        child.transform.localPosition = new Vector3(index * .13f, index * .07f, index * -.05f);
                        child.transform.localScale = new Vector3(1.2f, .7f, .25f); child.transform.localRotation = Quaternion.Euler(0, index * 7, 0);
                        child.AddComponent<MeshFilter>().sharedMesh = mesh; child.AddComponent<MeshRenderer>().sharedMaterial = material;
                        GameObjectUtility.SetStaticEditorFlags(child, StaticEditorFlags.BatchingStatic); originals.Add(child.transform);
                    }
                    var protectedPart = new GameObject(parts[family]); protectedPart.transform.SetParent(assembly, false);
                    protectedPart.AddComponent<MeshFilter>().sharedMesh = meshes[0]; protectedPart.AddComponent<MeshRenderer>().sharedMaterial = material;
                    protectedPart.AddComponent<BoxCollider>(); GameObjectUtility.SetStaticEditorFlags(protectedPart, StaticEditorFlags.BatchingStatic);
                    var before = Triangles(assembly); Bounds beforeBounds = GeometryBounds(assembly);
                    SlopCityInteractable interaction = assembly.GetComponent<SlopCityInteractable>();
                    Vector3 point = interaction ? interaction.InteractionPoint : Vector3.zero;
                    var result = SlopCityWebDecorativeMerge.Prepare(scene);
                    Check(result.passiveAssemblies == 1 && result.passiveSourceRenderers == 3 && result.passiveMergedRenderers == 1, "Every positive family merges only eligible passive leaves.");
                    var after = Triangles(assembly);
                    // The protected collider stays first after merged output is appended.
                    // Compare complete vertex tuples independently of renderer ordering.
                    foreach (var group in before)
                    {
                        List<Vertex> remaining = new List<Vertex>(after[group.Key]);
                        foreach (Vertex vertex in group.Value)
                        {
                            int match = remaining.FindIndex(candidate => SameVertex(vertex, candidate));
                            Check(match >= 0, "Positive family preserves every world vertex/normal/tangent/UV0/UV1.");
                            remaining.RemoveAt(match);
                        }
                        Check(remaining.Count == 0, "Positive family adds no duplicate geometry.");
                    }
                    Bounds afterBounds = GeometryBounds(assembly);
                    Check((beforeBounds.center - afterBounds.center).sqrMagnitude < 1e-8f && (beforeBounds.size - afterBounds.size).sqrMagnitude < 1e-8f, "Positive-family complete geometry bounds remain exact.");
                    foreach (Transform original in originals) Check(original && !original.GetComponent<MeshRenderer>() && !original.GetComponent<MeshFilter>(), "Positive family retains placement transforms while retiring bindings.");
                    Check(protectedPart.GetComponent<MeshRenderer>() && protectedPart.GetComponent<MeshFilter>() && protectedPart.GetComponent<BoxCollider>(), "Excluded collision geometry keeps all bindings.");
                    Check(!interaction || (interaction == assembly.GetComponent<SlopCityInteractable>() && interaction.InteractionPoint == point), "Original inspect/rest action and fixed interaction point survive.");
                    result = SlopCityWebDecorativeMerge.Prepare(scene);
                    Check(result.passiveAssemblies == 0, "Passive preparation is idempotent.");
                }
                finally { DestroyOutputs(city); foreach (Mesh mesh in meshes) Object.DestroyImmediate(mesh); foreach (Material material in materials) Object.DestroyImmediate(material); }
            }
        }

        private static bool SameVertex(Vertex a, Vertex b) => (a.point - b.point).sqrMagnitude < 1e-8f && (a.normal - b.normal).sqrMagnitude < 1e-8f
            && (a.tangent - b.tangent).sqrMagnitude < 1e-8f && (a.uv - b.uv).sqrMagnitude < 1e-12f && (a.uv2 - b.uv2).sqrMagnitude < 1e-12f;

        private static Bounds GeometryBounds(Transform root)
        {
            Bounds result = default; bool first = true;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                // Combined renderer AABBs deliberately have coarser culling. Compare
                // actual world vertices, not a rotated local AABB's empty corners.
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 point = renderer.transform.TransformPoint(vertex);
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                    else result.Encapsulate(point);
                }
            }
            return result;
        }

        private static GameObject FixtureRoot()
        {
            GameObject city = new GameObject("SlopCity_NineBlocks"); city.transform.position = new Vector3(15, 4, -8); city.transform.rotation = Quaternion.Euler(0, 28, 0);
            var japan = new GameObject("City_JapanMetropolis"); japan.transform.SetParent(city.transform, false); return city;
        }
        private static Transform Assembly(Transform parent, List<Mesh> meshes, List<Material> materials)
        {
            Shader shader = Shader.Find("Standard"); if (!shader) throw new InvalidOperationException("Standard shader missing.");
            for (int index = 0; index < 3; index++) materials.Add(new Material(shader) { name = "Fixture_AC_" + index });
            var root = new GameObject("Japan_SplitSystem_AC").transform; root.SetParent(parent, false); root.localPosition = new Vector3(-2, 6, 3); root.localRotation = Quaternion.Euler(0, -45, 0);
            for (int index = 0; index < Names.Length; index++)
            {
                GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube); Mesh borrowed = primitive.GetComponent<MeshFilter>().sharedMesh;
                // Match the authored generator's four streams, independently of any
                // optional lightmap UV channel shipped in Unity's builtin primitive.
                Mesh mesh = new Mesh { vertices = borrowed.vertices, normals = borrowed.normals, uv = borrowed.uv, triangles = borrowed.triangles };
                mesh.RecalculateTangents(); Object.DestroyImmediate(primitive); meshes.Add(mesh);
                var child = new GameObject(Names[index]); child.transform.SetParent(root, false); child.transform.localPosition = new Vector3(index * .035f, (index % 4) * .05f, index * -.009f); child.transform.localRotation = Quaternion.Euler(index, index * 2, 0);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = child.AddComponent<MeshRenderer>(); renderer.sharedMaterial = materials[Names[index] == "Weathered_Compressor_Casing" ? 0 : Names[index].StartsWith("Fan_Guard", StringComparison.Ordinal) || Names[index] == "Compressor_Mounting_Rail" ? 1 : 2];
                GameObjectUtility.SetStaticEditorFlags(child, StaticEditorFlags.BatchingStatic);
            }
            return root;
        }
        private struct Vertex { public Vector3 point, normal; public Vector4 tangent; public Vector2 uv, uv2; }
        private static Dictionary<Material, List<Vertex>> Triangles(Transform root)
        {
            var result = new Dictionary<Material, List<Vertex>>();
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh; Material material = renderer.sharedMaterial;
                if (!result.TryGetValue(material, out List<Vertex> vertices)) result.Add(material, vertices = new List<Vertex>());
                Vector3[] p = mesh.vertices, n = mesh.normals; Vector4[] t = mesh.tangents; Vector2[] uv = mesh.uv, uv2 = mesh.uv2;
                Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
                foreach (int index in mesh.triangles)
                {
                    Vector3 tangent = matrix.MultiplyVector(new Vector3(t[index].x, t[index].y, t[index].z)).normalized;
                    vertices.Add(new Vertex { point = matrix.MultiplyPoint3x4(p[index]), normal = matrix.inverse.transpose.MultiplyVector(n[index]).normalized,
                        tangent = new Vector4(tangent.x, tangent.y, tangent.z, t[index].w), uv = uv[index], uv2 = uv2.Length == p.Length ? uv2[index] : Vector2.zero });
                }
            }
            return result;
        }
        private static void DestroyOutputs(GameObject root)
        {
            Mesh[] generated = root.GetComponentsInChildren<MeshFilter>(true).Where(filter => filter.name.StartsWith("Manufactured_Web_AC", StringComparison.Ordinal)
                || filter.name.StartsWith("Manufactured_Web_Passive", StringComparison.Ordinal)).Select(filter => filter.sharedMesh).ToArray();
            Object.DestroyImmediate(root); foreach (Mesh mesh in generated) Object.DestroyImmediate(mesh);
        }
        private static void Check(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); checks++; }
        [Serializable] private sealed class Receipt { public string startedAtUtc, completedAtUtc, unityVersion, error; public bool success; public int checks, actualAssemblies, actualSourceRenderers, actualMergedRenderers, passiveAssemblies, passiveSourceRenderers, passiveMergedRenderers; }
    }
}
#endif
