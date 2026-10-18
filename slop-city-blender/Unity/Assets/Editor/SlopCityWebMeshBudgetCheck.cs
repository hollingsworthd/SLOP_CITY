#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SlopCity.Editor
{
    public static class SlopCityWebMeshBudgetCheck
    {
        public static void Run()
        {
            int checks = 0;
            Scene scene = default;
            Mesh mesh = null, repeated = null;
            Material material = null;
            try
            {
                scene = EditorSceneManager.NewPreviewScene();
                mesh = new Mesh { name = "Exact_Weld_Fixture" };
                Vector3[] positions = { Vector3.zero, Vector3.right, Vector3.up, Vector3.zero, Vector3.right, Vector3.up, Vector3.zero };
                Vector3[] normals = { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward, Vector3.back };
                Vector2[] uv = { Vector2.zero, Vector2.right, Vector2.up, Vector2.one, Vector2.right, Vector2.up, Vector2.zero };
                mesh.SetVertexBufferParams(7, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 2));
                mesh.SetVertexBufferData(positions, 0, 0, 7, 0);
                mesh.SetVertexBufferData(normals, 0, 0, 7, 1);
                mesh.SetVertexBufferData(uv, 0, 0, 7, 2);
                mesh.subMeshCount = 2;
                mesh.SetIndices(new[] { 0, 1, 2, 3, 4, 5 }, MeshTopology.Triangles, 0);
                mesh.SetIndices(new[] { 6, 2, 1 }, MeshTopology.Triangles, 1);
                Bounds before = mesh.bounds;
                var corners = new List<Vector3>(); var cornerNormals = new List<Vector3>(); var cornerUv = new List<Vector2>();
                for (int submesh = 0; submesh < 2; submesh++)
                    foreach (int index in mesh.GetIndices(submesh)) { corners.Add(positions[index]); cornerNormals.Add(normals[index]); cornerUv.Add(uv[index]); }
                Require(SlopCityWebMeshBudget.WeldExact(mesh) == 2 && mesh.vertexCount == 5,
                    "Only two byte-identical vertices merge; UV and hard-normal seams stay distinct.", ref checks);
                int corner = 0;
                positions = mesh.vertices; normals = mesh.normals; uv = mesh.uv;
                for (int submesh = 0; submesh < 2; submesh++)
                    foreach (int index in mesh.GetIndices(submesh))
                    {
                        Require(positions[index] == corners[corner] && normals[index] == cornerNormals[corner] && uv[index] == cornerUv[corner],
                            "All indexed triangle corners preserve their position, normal and UV across separate vertex streams.", ref checks);
                        corner++;
                    }
                Require(mesh.bounds == before && mesh.subMeshCount == 2 && mesh.vertexBufferCount == 3,
                    "Welding preserves bounds, material submeshes and vertex streams.", ref checks);
                Require(SlopCityWebMeshBudget.WeldExact(mesh) == 0, "Welding is idempotent.", ref checks);
                Require(SlopCityWebMeshBudget.PackNormalsTangents(mesh) == 0,
                    "Nonmatching layouts keep their original attributes and precision.", ref checks);
                CheckPacking(ref checks);
                CheckPreparedFacadeBudget(scene, ref checks);
                CheckUniqueStaticBatchSelection(scene, ref checks);
                CheckReplacedLoungeRetirement(scene, ref checks);
                CheckUnbatchedFrameCpuFinalization(scene, ref checks);

                repeated = new Mesh { name = "Repeated_Art_Fixture" };
                var many = new Vector3[192];
                for (int vertex = 0; vertex < many.Length; vertex++) many[vertex] = new Vector3(vertex * .01f, vertex % 2, 0);
                repeated.vertices = many; repeated.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
                material = new Material(Shader.Find("Standard")) { enableInstancing = true };
                var objects = new List<GameObject>();
                for (int index = 0; index < 5; index++)
                {
                    var owner = new GameObject("Repeated_Prop_" + index, typeof(MeshFilter), typeof(MeshRenderer));
                    SceneManager.MoveGameObjectToScene(owner, scene);
                    owner.GetComponent<MeshFilter>().sharedMesh = repeated;
                    owner.GetComponent<MeshRenderer>().sharedMaterial = material;
                    GameObjectUtility.SetStaticEditorFlags(owner, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);
                    objects.Add(owner);
                }
                SlopCityWebMeshBudget.Prepare(scene);
                foreach (GameObject owner in objects)
                {
                    StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(owner);
                    Require((flags & StaticEditorFlags.BatchingStatic) == 0 && (flags & StaticEditorFlags.OccluderStatic) != 0
                        && owner.GetComponent<MeshFilter>().sharedMesh == repeated && owner.GetComponent<MeshRenderer>().sharedMaterial == material,
                        "Repeated art keeps shared mesh/material and occlusion flags while avoiding static vertex copies.", ref checks);
                }
                CheckCrowdGrowth(scene, repeated, material, ref checks);
                CheckCrowdCpuData(scene, ref checks);
                string output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports/web-mesh-budget-check.json");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output, "{\"passed\":true,\"checks\":" + checks + ",\"checkedAtUtc\":\"" + DateTime.UtcNow.ToString("O")
                    + "\",\"scope\":\"Isolated exact vertex-stream/index preservation, direction-only packing fidelity, instancing flags, crowd matrix growth and CPU-copy finalization. No physical mobile validation.\"}");
                Debug.Log("SLOP_CITY_WEB_MESH_BUDGET_CHECK_PASSED " + checks);
            }
            finally
            {
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                if (mesh) UnityEngine.Object.DestroyImmediate(mesh);
                if (repeated) UnityEngine.Object.DestroyImmediate(repeated);
                if (material) UnityEngine.Object.DestroyImmediate(material);
            }
        }
        private static void Require(bool condition, string message, ref int checks)
        { if (!condition) throw new InvalidOperationException(message); checks++; }

        private static void CheckCrowdGrowth(Scene scene, Mesh mesh, Material material, ref int checks)
        {
            var owner = new GameObject("Crowd_Batch_Growth_Fixture", typeof(SlopCityCrowd));
            SceneManager.MoveGameObjectToScene(owner, scene);
            SlopCityCrowd crowd = owner.GetComponent<SlopCityCrowd>();
            var visuals = new SlopCityCrowdVisuals { parts = new[] { mesh } };
            crowd.Configure(visuals, Array.Empty<SlopCityCrowdRoute>(), Array.Empty<SlopCityNpcSeed>());
            const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo add = typeof(SlopCityCrowd).GetMethod("Add", members);
            var batches = (IList)typeof(SlopCityCrowd).GetField("batches", members).GetValue(crowd);
            for (int index = 0; index < 600; index++)
            {
                Matrix4x4 matrix = Matrix4x4.Translate(new Vector3(index, index % 3, -index));
                add.Invoke(crowd, new object[] { SlopCityHumanPart.Jacket, material, matrix });
                if (index == 15 || index == 16 || index == 31 || index == 32 || index == 63 || index == 64 || index == 511 || index == 512)
                {
                    object batch = batches[0];
                    int count = (int)batch.GetType().GetField("count", members).GetValue(batch);
                    var matrices = (Matrix4x4[])batch.GetType().GetField("matrices", members).GetValue(batch);
                    Require(count == index + 1 && matrices.Length >= count && matrices.Length <= 1023,
                        "Crowd batch growth keeps every instance at each 16/32/64/512 capacity boundary within the draw API cap.", ref checks);
                }
            }
            object warmed = batches[0];
            var retained = (Matrix4x4[])warmed.GetType().GetField("matrices", members).GetValue(warmed);
            Require((int)warmed.GetType().GetField("count", members).GetValue(warmed) == 600,
                "Crowd batch contains all 600 submitted instances after repeated growth.", ref checks);
            for (int index = 0; index < 600; index++)
                Require(retained[index].Equals(Matrix4x4.Translate(new Vector3(index, index % 3, -index))),
                    "Growing crowd buffers preserve every previously submitted transform.", ref checks);
            crowd.Configure(visuals, Array.Empty<SlopCityCrowdRoute>(), Array.Empty<SlopCityNpcSeed>());
            var lookup = (IDictionary)typeof(SlopCityCrowd).GetField("batchLookup", members).GetValue(crowd);
            Require(batches.Count == 0 && lookup.Count == 0,
                "Reconfiguring the crowd releases warmed batch caches and their matrix buffers.", ref checks);
            add.Invoke(crowd, new object[] { SlopCityHumanPart.Jacket, material, Matrix4x4.identity });
            object fresh = batches[0];
            Require(((Matrix4x4[])fresh.GetType().GetField("matrices", members).GetValue(fresh)).Length == 16,
                "A newly configured rare batch returns to the small initial allocation.", ref checks);
        }

        private static void CheckPreparedFacadeBudget(Scene scene, ref int checks)
        {
            Mesh retiredSource = null, metric = null, physicsSource = null;
            Material material = null;
            GameObject tower = null, unprepared = null;
            try
            {
                tower = new GameObject("Skyline_Tower_BudgetFixture", typeof(SlopCityMetricFacade));
                unprepared = new GameObject("City_Skyscraper_UnpreparedFixture", typeof(SlopCityMetricFacade));
                SceneManager.MoveGameObjectToScene(tower, scene);
                SceneManager.MoveGameObjectToScene(unprepared, scene);
                var serialized = new SerializedObject(tower.GetComponent<SlopCityMetricFacade>());
                serialized.FindProperty("prepared").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                material = new Material(Shader.Find("Standard"));
                retiredSource = new Mesh { name = "Retired_Facade_Source_Fixture" };
                retiredSource.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                retiredSource.triangles = new[] { 0, 1, 2 };
                Func<GameObject, string, bool, GameObject> part = (parent, name, enabled) =>
                {
                    var result = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                    result.transform.SetParent(parent.transform, false);
                    result.GetComponent<MeshFilter>().sharedMesh = retiredSource;
                    result.GetComponent<MeshRenderer>().sharedMaterial = material;
                    result.GetComponent<MeshRenderer>().enabled = enabled;
                    return result;
                };
                var retired = new List<GameObject>();
                foreach (string name in new[] { "Facade_Frames_01", "Podium_Joints_01", "Podium_Cornice", "Recessed_Floor_Band" })
                    retired.Add(part(tower, name, false));
                var preserved = new List<GameObject>();
                foreach (string name in new[] { "CurtainWall_Panel", "Masonry_WindowBays", "Structural_Core", "Setback_UpperShaft",
                    "Mechanical_Crown", "TV_Transmission", "Dormant_Art" }) preserved.Add(part(tower, name, false));
                preserved.Add(part(tower, "Facade_Frames_Visible", true));
                preserved.Add(part(unprepared, "Facade_Frames_Original", false));
                GameObject physics = part(tower, "Facade_Frames_Physics", false);
                physicsSource = UnityEngine.Object.Instantiate(retiredSource);
                physics.GetComponent<MeshFilter>().sharedMesh = physicsSource;
                physics.AddComponent<MeshCollider>().sharedMesh = physicsSource;

                Vector3[] positions = { new Vector3(125.12345f, .1234567f, 0), Vector3.right, Vector3.up };
                Vector2[] uv = { new Vector2(123.45678f, .12345678f), Vector2.right, Vector2.up };
                metric = new Mesh { name = "Prepared_Metric_Surface_Fixture" };
                metric.vertices = positions;
                metric.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
                metric.tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) };
                metric.uv = uv; metric.triangles = new[] { 0, 1, 2 };
                Bounds bounds = metric.bounds;
                var surfaces = new List<GameObject>();
                foreach (string name in new[] { "Floor_Aligned_Stonework", "Human_Scale_Window_Panes" })
                {
                    GameObject surface = part(tower, name, true);
                    surface.GetComponent<MeshFilter>().sharedMesh = metric;
                    surfaces.Add(surface);
                }
                SlopCityWebMeshBudget.Prepare(scene);
                foreach (GameObject item in retired)
                    Require(!item.GetComponent<MeshFilter>().sharedMesh && item.GetComponent<MeshRenderer>().sharedMaterials.Length == 0
                        && !item.GetComponent<MeshRenderer>().enabled,
                        "Prepared replacement art releases only permanently retired facade mesh/material dependencies.", ref checks);
                foreach (GameObject item in preserved)
                    Require(item.GetComponent<MeshFilter>().sharedMesh == retiredSource && item.GetComponent<MeshRenderer>().sharedMaterial == material,
                        "Runtime facade inputs, dimensions, hardware, media lettering, visible art, unprepared art and collider inputs stay intact.", ref checks);
                Require(physics.GetComponent<MeshFilter>().sharedMesh == physicsSource && physics.GetComponent<MeshRenderer>().sharedMaterial == material,
                    "Collider-backed retired-looking art keeps its original source geometry and material.", ref checks);
                foreach (GameObject surface in surfaces)
                    Require(surface.GetComponent<MeshFilter>().sharedMesh == metric && !metric.isReadable
                        && metric.GetVertexBufferStride(0) == 36 && metric.bounds == bounds && metric.GetIndexCount(0) == 3,
                        "Prepared stonework and panes share the existing direction-only packing and preserve bounds/topology while releasing CPU copies.", ref checks);
                SlopCityWebMeshBudget.Prepare(scene);
                Require(!metric.isReadable && metric.bounds == bounds && !retired[0].GetComponent<MeshFilter>().sharedMesh,
                    "Prepared facade retirement and metric packing are idempotent.", ref checks);
            }
            finally
            {
                if (tower) UnityEngine.Object.DestroyImmediate(tower);
                if (unprepared) UnityEngine.Object.DestroyImmediate(unprepared);
                if (retiredSource) UnityEngine.Object.DestroyImmediate(retiredSource);
                if (metric) UnityEngine.Object.DestroyImmediate(metric);
                if (physicsSource) UnityEngine.Object.DestroyImmediate(physicsSource);
                if (material) UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static void CheckUniqueStaticBatchSelection(Scene scene, ref int checks)
        {
            var objects = new List<GameObject>();
            var meshes = new List<Mesh>();
            Material material = null;
            try
            {
                material = new Material(Shader.Find("Standard")) { enableInstancing = true };
                Vector3[] positions = { new Vector3(125.12345f, .1234567f, 0), Vector3.right, Vector3.up };
                Vector2[] uv = { new Vector2(123.45678f, .12345678f), Vector2.right, Vector2.up };
                Func<Mesh> makeMesh = () =>
                {
                    var result = new Mesh { name = "Unique_Static_Batch_Fixture" };
                    result.vertices = positions; result.uv = uv;
                    result.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
                    result.tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) };
                    result.triangles = new[] { 0, 1, 2 }; meshes.Add(result); return result;
                };
                Func<string, Mesh, GameObject> part = (name, source) =>
                {
                    var result = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                    SceneManager.MoveGameObjectToScene(result, scene); objects.Add(result);
                    result.GetComponent<MeshFilter>().sharedMesh = source;
                    result.GetComponent<MeshRenderer>().sharedMaterial = material;
                    GameObjectUtility.SetStaticEditorFlags(result, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);
                    return result;
                };
                Mesh unique = makeMesh(); Bounds bounds = unique.bounds;
                GameObject uniqueOwner = part("Manufactured_Unique_Batch", unique);
                Mesh frame = makeMesh();
                GameObject facadeOwner = new GameObject("City_Skyscraper_Unique_Batch", typeof(SlopCityMetricFacade));
                SceneManager.MoveGameObjectToScene(facadeOwner, scene); objects.Add(facadeOwner);
                GameObject frameOwner = part("Metric_Reveals_Frames_And_Cornices", frame);
                frameOwner.transform.SetParent(facadeOwner.transform, false);
                var facade = new SerializedObject(facadeOwner.GetComponent<SlopCityMetricFacade>());
                facade.FindProperty("prepared").boolValue = true;
                facade.FindProperty("ownedMeshes").arraySize = 1;
                facade.FindProperty("ownedMeshes").GetArrayElementAtIndex(0).objectReferenceValue = frame;
                facade.ApplyModifiedPropertiesWithoutUndo();
                Mesh shared = makeMesh(); var sharedOwners = new List<GameObject>();
                int excludedVertices = SlopCityWebMeshBudget.SmallRepeatedBatchMaximumVertices + 1;
                var largePositions = new Vector3[excludedVertices]; var largeUv = new Vector2[excludedVertices];
                var largeNormals = new Vector3[excludedVertices]; var largeTangents = new Vector4[excludedVertices];
                for (int vertex = 0; vertex < excludedVertices; vertex++)
                {
                    largePositions[vertex] = new Vector3(vertex * .01f, vertex % 3, 0);
                    largeUv[vertex] = new Vector2(vertex * .01f, vertex % 3);
                    largeNormals[vertex] = Vector3.forward; largeTangents[vertex] = new Vector4(1, 0, 0, 1);
                }
                shared.vertices = largePositions; shared.uv = largeUv; shared.normals = largeNormals; shared.tangents = largeTangents;
                for (int index = 0; index < 4; index++) sharedOwners.Add(part("Manufactured_Shared_Batch_" + index, shared));
                Mesh small = makeMesh(); var smallOwners = new List<GameObject>();
                int admittedVertices = SlopCityWebMeshBudget.SmallRepeatedBatchMaximumVertices;
                var smallPositions = new Vector3[admittedVertices]; var smallUv = new Vector2[admittedVertices];
                var smallNormals = new Vector3[admittedVertices]; var smallTangents = new Vector4[admittedVertices];
                for (int vertex = 0; vertex < admittedVertices; vertex++)
                {
                    smallPositions[vertex] = new Vector3(vertex * .01f, vertex % 3, 0);
                    smallUv[vertex] = new Vector2(vertex * .01f, vertex % 3);
                    smallNormals[vertex] = Vector3.forward; smallTangents[vertex] = new Vector4(1, 0, 0, 1);
                }
                small.vertices = smallPositions; small.uv = smallUv; small.normals = smallNormals; small.tangents = smallTangents;
                for (int index = 0; index < 4; index++) smallOwners.Add(part("Manufactured_Small_Batch_" + index, small));
                GameObject nonstaticSmall = part("Manufactured_Small_Nonstatic", small);
                GameObjectUtility.SetStaticEditorFlags(nonstaticSmall, StaticEditorFlags.OccluderStatic);
                Mesh colliderSource = makeMesh();
                GameObject colliderOwner = part("Manufactured_Collider_Batch", colliderSource);
                colliderOwner.AddComponent<MeshCollider>().sharedMesh = colliderSource;
                Mesh runtimeSource = makeMesh();
                GameObject runtimeOwner = part("Manufactured_Moving_Car_Batch", runtimeSource);
                var car = new GameObject("Parked_CityCar");
                SceneManager.MoveGameObjectToScene(car, scene); objects.Add(car);
                runtimeOwner.transform.SetParent(car.transform, false);
                SlopCityWebMeshBudget.Prepare(scene, true);
                foreach (GameObject owner in new[] { uniqueOwner, frameOwner })
                    Require((GameObjectUtility.GetStaticEditorFlags(owner) & StaticEditorFlags.BatchingStatic) != 0
                        && owner.GetComponent<MeshFilter>().sharedMesh.isReadable,
                        "Only unique immutable art remains static-batch eligible and readable until Unity's scene combiner runs.", ref checks);
                Require(unique.GetVertexBufferStride(0) == 36 && unique.bounds == bounds && unique.GetIndices(0)[2] == 2,
                    "Unique-batch preparation preserves packed geometry, bounds, indices and winding.", ref checks);
                for (int vertex = 0; vertex < positions.Length; vertex++)
                    Require(unique.vertices[vertex].Equals(positions[vertex]) && unique.uv[vertex].Equals(uv[vertex]),
                        "Unique-batch preparation preserves exact positions and texture coordinates.", ref checks);
                facade.Update();
                Require(facade.FindProperty("ownedMeshes").arraySize == 0,
                    "Facade ownership detaches soon-to-be-replaced static source meshes instead of retaining a duplicate after batching.", ref checks);
                foreach (GameObject owner in sharedOwners)
                    Require((GameObjectUtility.GetStaticEditorFlags(owner) & StaticEditorFlags.BatchingStatic) == 0
                        && owner.GetComponent<MeshFilter>().sharedMesh == shared && owner.GetComponent<MeshRenderer>().sharedMaterial == material,
                        "Repeated immutable art just above the configured vertex cap keeps one shared mesh/material and remains outside static expansion.", ref checks);
                foreach (GameObject owner in smallOwners)
                    Require((GameObjectUtility.GetStaticEditorFlags(owner) & StaticEditorFlags.BatchingStatic) != 0
                        && owner.GetComponent<MeshFilter>().sharedMesh == small && small.isReadable,
                        "Repeated originally static art at the configured vertex cap is admitted to bounded static batching and retains data until combination.", ref checks);
                Require((GameObjectUtility.GetStaticEditorFlags(nonstaticSmall) & StaticEditorFlags.BatchingStatic) == 0,
                    "Sharing a small mesh never promotes an originally nonstatic renderer into static batching.", ref checks);
                foreach (GameObject owner in new[] { colliderOwner, runtimeOwner })
                    Require((GameObjectUtility.GetStaticEditorFlags(owner) & StaticEditorFlags.BatchingStatic) == 0,
                        "Physics inputs and movable runtime art remain outside selective static batching.", ref checks);
                Require(colliderSource.isReadable && !shared.isReadable,
                    "Physics retains CPU geometry while repeated immutable render art releases its CPU copy.", ref checks);
                SlopCityWebMeshBudget.Prepare(scene, true);
                facade.Update();
                Require(unique.isReadable && frame.isReadable && !shared.isReadable
                    && facade.FindProperty("ownedMeshes").arraySize == 0
                    && (GameObjectUtility.GetStaticEditorFlags(uniqueOwner) & StaticEditorFlags.BatchingStatic) != 0,
                    "Repeating selective batch preparation preserves source admission, ownership detachment and shared exclusions.", ref checks);
                Require(small.isReadable && (GameObjectUtility.GetStaticEditorFlags(smallOwners[0]) & StaticEditorFlags.BatchingStatic) != 0
                    && (GameObjectUtility.GetStaticEditorFlags(nonstaticSmall) & StaticEditorFlags.BatchingStatic) == 0,
                    "Repeated preparation preserves partial small-mesh admission and the nonstatic shared user.", ref checks);
                long budget = SlopCityWebMeshBudget.SmallRepeatedBatchAdditionalGeometryBudget - 1024;
                Require(SlopCityWebMeshBudget.TryReserveSmallBatchGeometry(256, 4, ref budget)
                    && budget == SlopCityWebMeshBudget.SmallRepeatedBatchAdditionalGeometryBudget,
                    "The small-repeat budget admits an exact configured byte-limit boundary.", ref checks);
                Require(!SlopCityWebMeshBudget.TryReserveSmallBatchGeometry(1, 1, ref budget)
                    && budget == SlopCityWebMeshBudget.SmallRepeatedBatchAdditionalGeometryBudget,
                    "Geometry above the configured byte budget is rejected without consuming or wrapping the budget.", ref checks);
                budget = 0;
                Require(!SlopCityWebMeshBudget.TryReserveSmallBatchGeometry(long.MaxValue, int.MaxValue, ref budget) && budget == 0,
                    "Overflow-sized geometry admission is rejected before multiplication.", ref checks);
            }
            finally
            {
                foreach (GameObject owner in objects) if (owner) UnityEngine.Object.DestroyImmediate(owner);
                foreach (Mesh mesh in meshes) if (mesh) UnityEngine.Object.DestroyImmediate(mesh);
                if (material) UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static void CheckReplacedLoungeRetirement(Scene scene, ref int checks)
        {
            var objects = new List<GameObject>(); var meshes = new List<Mesh>();
            Material material = null;
            try
            {
                material = new Material(Shader.Find("Standard"));
                var mesh = new Mesh { name = "Replaced_Artwork_Fixture" };
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                mesh.triangles = new[] { 0, 1, 2 }; meshes.Add(mesh);
                Func<string, GameObject> root = name =>
                {
                    var result = new GameObject(name); SceneManager.MoveGameObjectToScene(result, scene); objects.Add(result); return result;
                };
                Func<GameObject, string, Mesh, bool, GameObject> part = (parent, name, source, enabled) =>
                {
                    var result = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                    result.transform.SetParent(parent.transform, false); objects.Add(result);
                    result.GetComponent<MeshFilter>().sharedMesh = source;
                    result.GetComponent<MeshRenderer>().sharedMaterial = material;
                    result.GetComponent<MeshRenderer>().enabled = enabled; return result;
                };
                Action<MonoBehaviour> markPrepared = owner =>
                {
                    var serialized = new SerializedObject(owner); serialized.FindProperty("prepared").boolValue = true;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                };
                GameObject lounge = root("SlopCity_Lounge"), city = root("SlopCity_NineBlocks"), unrelated = root("Unrelated_Root");
                SlopCityHumanScale scale = city.AddComponent<SlopCityHumanScale>(); markPrepared(scale);
                for (int index = 0; index < 9; index++)
                {
                    var district = new GameObject("Fixture_District_" + index, typeof(SlopCityDistrict));
                    district.transform.SetParent(city.transform, false);
                    district.GetComponent<SlopCityDistrict>().Configure(index % 3, index / 3, "Fixture", Vector3.zero);
                }
                GameObject habitat = root("SlopCity_HabitatInterior"); habitat.transform.SetParent(lounge.transform, false);
                GameObject replacement = part(habitat, "Habitat_Combined_Fixture", mesh, true);
                var retired = new List<GameObject>(); var preserved = new List<GameObject>();
                foreach (string name in new[] { "HRR_Poster_Fixture", "Poster_Title_Fixture", "Poster_Subtitle_Fixture", "Wall_Record_Fixture" })
                    retired.Add(part(lounge, name, mesh, false));
                foreach (string name in new[] { "City_Original_Fixture", "Roof_Unit_Fixture", "Transmission_Mast_Fixture", "Antenna_Fixture" })
                { GameObject item = part(lounge, name, mesh, true); item.SetActive(false); retired.Add(item); }
                GameObject visible = part(unrelated, "Visible_Shared_User", mesh, true); preserved.Add(visible);
                preserved.Add(part(lounge, "Poster_Title_Visible", mesh, true));
                preserved.Add(part(lounge, "City_Still_Active", mesh, true));
                preserved.Add(part(unrelated, "Poster_Title_Unrelated", mesh, false));
                foreach (string name in new[] { "TV_Broadcaster", "TV_Tagline", "TV_Transmission" }) preserved.Add(part(lounge, name, mesh, false));
                GameObject mediaLetter = part(lounge, "Poster_Title_Playback", mesh, false); preserved.Add(mediaLetter);
                GameObject mediaScreen = part(lounge, "Poster_Subtitle_PlaybackDisplay", mesh, false); preserved.Add(mediaScreen);
                var player = root("Fixture_MediaPlayer").AddComponent<SlopCityMediaPlayer>();
                var media = new SerializedObject(player);
                media.FindProperty("screen").objectReferenceValue = mediaScreen.GetComponent<Renderer>();
                media.FindProperty("screenLettering").arraySize = 1;
                media.FindProperty("screenLettering").GetArrayElementAtIndex(0).objectReferenceValue = mediaLetter.GetComponent<Renderer>();
                media.ApplyModifiedPropertiesWithoutUndo();
                GameObject cityDisplay = part(lounge, "Wall_Record_CityDisplay", mesh, false); preserved.Add(cityDisplay);
                cityDisplay.AddComponent<SlopCityCityScreen>().Configure(cityDisplay.GetComponent<Renderer>(), 0);
                GameObject hiddenParent = root("Temporarily_Hidden_Parent"); hiddenParent.transform.SetParent(lounge.transform, false);
                GameObject inheritedHidden = part(hiddenParent, "City_Only_Ancestor_Hidden", mesh, true); preserved.Add(inheritedHidden);
                hiddenParent.SetActive(false);
                Mesh physics = UnityEngine.Object.Instantiate(mesh); meshes.Add(physics);
                GameObject physicsArt = part(lounge, "Wall_Record_Physics", physics, false);
                root("External_Physics_User").AddComponent<MeshCollider>().sharedMesh = physics;
                Mesh skin = UnityEngine.Object.Instantiate(mesh); meshes.Add(skin);
                GameObject skinArt = part(lounge, "Poster_Title_Skinned", skin, false);
                root("External_Skinned_User").AddComponent<SkinnedMeshRenderer>().sharedMesh = skin;
                BoxCollider box = retired[0].AddComponent<BoxCollider>(); box.size = new Vector3(2, 3, .1f);
                Bounds colliderBounds = box.bounds;
                GameObject cityTower = root("City_Skyscraper_RetirementFixture"); cityTower.transform.SetParent(city.transform, false);
                markPrepared(cityTower.AddComponent<SlopCityMetricFacade>());
                foreach (string name in new[] { "CurtainWall_Panel", "Masonry_WindowBays" }) retired.Add(part(cityTower, name, mesh, false));
                GameObject skyline = root("Skyline_Tower_RetirementFixture"); skyline.transform.SetParent(city.transform, false);
                markPrepared(skyline.AddComponent<SlopCityMetricFacade>());
                foreach (string name in new[] { "CurtainWall_Panel", "Masonry_WindowBays" }) preserved.Add(part(skyline, name, mesh, false));
                Vector3[] positions = mesh.vertices; Vector2[] uv = mesh.uv; int[] indices = mesh.triangles;
                SlopCityWebMeshBudget.Prepare(scene);
                foreach (GameObject item in retired)
                    Require(!item.GetComponent<MeshFilter>().sharedMesh && item.GetComponent<MeshRenderer>().sharedMaterials.Length == 0,
                        "Only source-proven replaced lounge/backdrop/city-facade references retire under their completed replacement roots.", ref checks);
                foreach (GameObject item in preserved)
                    Require(item.GetComponent<MeshFilter>().sharedMesh == mesh && item.GetComponent<MeshRenderer>().sharedMaterial == material,
                        "Visible, unrelated, inherited-hidden, referenced playback/device and skyline trim users retain their original geometry/materials.", ref checks);
                Require(physicsArt.GetComponent<MeshFilter>().sharedMesh == physics && physics.isReadable
                    && skinArt.GetComponent<MeshFilter>().sharedMesh == skin && skin.isReadable,
                    "Collision/skinning references anywhere in the scene protect retired-looking art.", ref checks);
                Require(box && box.size == new Vector3(2, 3, .1f) && box.bounds == colliderBounds && retired[0].transform.parent == lounge.transform,
                    "Retirement preserves original object, transform and box-collider behavior.", ref checks);
                Require(mesh.isReadable && mesh.vertices.Length == positions.Length && mesh.triangles.Length == indices.Length,
                    "Detaching retired users does not destroy or finalize a visible shared mesh.", ref checks);
                for (int vertex = 0; vertex < positions.Length; vertex++)
                    Require(mesh.vertices[vertex].Equals(positions[vertex]) && mesh.uv[vertex].Equals(uv[vertex]),
                        "Visible shared geometry keeps exact positions and texture coordinates.", ref checks);
                for (int index = 0; index < indices.Length; index++)
                    Require(mesh.triangles[index] == indices[index], "Visible shared geometry keeps every original triangle corner and winding.", ref checks);
                SlopCityWebMeshBudget.Prepare(scene);
                Require(!retired[0].GetComponent<MeshFilter>().sharedMesh && visible.GetComponent<MeshFilter>().sharedMesh == mesh,
                    "Retirement is idempotent while visible shared ownership remains intact.", ref checks);
                UnityEngine.Object.DestroyImmediate(replacement);
                GameObject absentHabitat = part(lounge, "Poster_Title_NoReplacement", mesh, false);
                var unprepared = new SerializedObject(scale); unprepared.FindProperty("prepared").boolValue = false;
                unprepared.ApplyModifiedPropertiesWithoutUndo();
                GameObject absentCity = part(lounge, "City_NoPreparedReplacement", mesh, true); absentCity.SetActive(false);
                SlopCityWebMeshBudget.Prepare(scene);
                Require(absentHabitat.GetComponent<MeshFilter>().sharedMesh == mesh && absentCity.GetComponent<MeshFilter>().sharedMesh == mesh,
                    "Missing habitat art or an unprepared replacement city never retires original art.", ref checks);
            }
            finally
            {
                foreach (GameObject owner in objects) if (owner) UnityEngine.Object.DestroyImmediate(owner);
                foreach (Mesh mesh in meshes) if (mesh) UnityEngine.Object.DestroyImmediate(mesh);
                if (material) UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static void CheckUnbatchedFrameCpuFinalization(Scene scene, ref int checks)
        {
            GameObject owner = null, sharedOwner = null;
            Mesh mesh = null; Material material = null;
            try
            {
                owner = new GameObject("Skyline_Tower_CpuFallback", typeof(SlopCityMetricFacade));
                SceneManager.MoveGameObjectToScene(owner, scene);
                var prepared = new SerializedObject(owner.GetComponent<SlopCityMetricFacade>());
                prepared.FindProperty("prepared").boolValue = true;
                prepared.FindProperty("ownedMeshes").arraySize = 0; prepared.ApplyModifiedPropertiesWithoutUndo();
                var art = new GameObject("Metric_Facade"); art.transform.SetParent(owner.transform, false);
                var frame = new GameObject("Metric_Reveals_Frames_And_Cornices", typeof(MeshFilter), typeof(MeshRenderer));
                frame.transform.SetParent(art.transform, false);
                mesh = new Mesh { name = "Unbatched_Frame_Cpu_Fixture" };
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up }; mesh.triangles = new[] { 0, 1, 2 };
                material = new Material(Shader.Find("Standard"));
                frame.GetComponent<MeshFilter>().sharedMesh = mesh; frame.GetComponent<MeshRenderer>().sharedMaterial = material;
                sharedOwner = new GameObject("Visible_Shared_Frame_User", typeof(MeshFilter), typeof(MeshRenderer));
                SceneManager.MoveGameObjectToScene(sharedOwner, scene);
                sharedOwner.GetComponent<MeshFilter>().sharedMesh = mesh; sharedOwner.GetComponent<MeshRenderer>().sharedMaterial = material;
                Bounds bounds = mesh.bounds;
                MethodInfo finalize = typeof(SlopCityMetricFacade).GetMethod("FinalizeUnbatchedFrameCpuData", BindingFlags.Instance | BindingFlags.NonPublic);
                Require(finalize != null, "The Web-only fallback has an explicit fixture entry point without changing Editor/native Awake.", ref checks);
                SlopCityMetricFacade facade = owner.GetComponent<SlopCityMetricFacade>();
                var protectedGeometry = new HashSet<Mesh> { mesh };
                Require(!(bool)finalize.Invoke(facade, new object[] { protectedGeometry }) && mesh.isReadable,
                    "Physics/skinning-protected fallback geometry remains readable.", ref checks);
                prepared.FindProperty("prepared").boolValue = false; prepared.ApplyModifiedPropertiesWithoutUndo();
                Require(!(bool)finalize.Invoke(facade, new object[] { new HashSet<Mesh>() }) && mesh.isReadable,
                    "Unprepared geometry retains its original CPU data.", ref checks);
                prepared.FindProperty("prepared").boolValue = true; prepared.ApplyModifiedPropertiesWithoutUndo();
                Require((bool)finalize.Invoke(facade, new object[] { new HashSet<Mesh>() }) && !mesh.isReadable
                    && mesh.bounds == bounds && mesh.vertexCount == 3 && mesh.GetIndexCount(0) == 3,
                    "A live unbatched frame with an empty ownership list releases only its immutable CPU copy.", ref checks);
                Require(frame.GetComponent<MeshFilter>().sharedMesh == mesh && sharedOwner.GetComponent<MeshFilter>().sharedMesh == mesh
                    && frame.GetComponent<MeshRenderer>().sharedMaterial == material && frame.GetComponent<MeshRenderer>().enabled,
                    "CPU finalization preserves renderer state, material and every visible shared mesh reference.", ref checks);
                prepared.Update();
                Require(prepared.FindProperty("ownedMeshes").arraySize == 0
                    && !(bool)finalize.Invoke(facade, new object[] { new HashSet<Mesh>() }),
                    "Fallback finalization is idempotent and does not reattach detached ownership.", ref checks);
            }
            finally
            {
                if (owner) UnityEngine.Object.DestroyImmediate(owner);
                if (sharedOwner) UnityEngine.Object.DestroyImmediate(sharedOwner);
                if (mesh) UnityEngine.Object.DestroyImmediate(mesh);
                if (material) UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static void CheckPacking(ref int checks)
        {
            var mesh = new Mesh { name = "Immutable_Direction_Packing_Fixture", indexFormat = IndexFormat.UInt32 };
            try
            {
                Vector3[] positions = { new Vector3(125.12345f, .1234567f, 0), Vector3.right, Vector3.up };
                Vector3 direction = new Vector3(.3f, .6f, -.7f).normalized;
                Vector4 tangent = new Vector4(.2f, .8f, .56f, -1);
                Vector3[] normals = { direction, direction, direction };
                Vector4[] tangents = { tangent, tangent, tangent };
                Vector2[] uv = { new Vector2(123.45678f, .12345678f), Vector2.right, Vector2.up };
                mesh.vertices = positions; mesh.normals = normals; mesh.tangents = tangents; mesh.uv = uv;
                mesh.subMeshCount = 2;
                mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
                mesh.SetIndices(new[] { 2, 1, 0 }, MeshTopology.Triangles, 1);
                Bounds bounds = mesh.bounds;
                Require(SlopCityWebMeshBudget.PackNormalsTangents(mesh) == 36 && mesh.GetVertexBufferStride(0) == 36,
                    "Three immutable vertices save exactly 12 bytes each through direction-only packing.", ref checks);
                Vector3[] outputPositions = mesh.vertices, outputNormals = mesh.normals;
                Vector4[] outputTangents = mesh.tangents; Vector2[] outputUv = mesh.uv;
                for (int vertex = 0; vertex < 3; vertex++)
                {
                    Require(outputPositions[vertex].Equals(positions[vertex]) && outputUv[vertex].Equals(uv[vertex]),
                        "Direction packing preserves exact full-precision positions and UVs.", ref checks);
                    Require(Vector3.Distance(outputNormals[vertex], normals[vertex]) < .002f
                        && Vector4.Distance(outputTangents[vertex], tangents[vertex]) < .002f,
                        "Direction packing bounds normal and tangent quantization error to less than 0.002.", ref checks);
                }
                Require(mesh.indexFormat == IndexFormat.UInt32 && mesh.bounds == bounds && mesh.subMeshCount == 2
                    && mesh.GetIndices(0)[0] == 0 && mesh.GetIndices(0)[2] == 2
                    && mesh.GetIndices(1)[0] == 2 && mesh.GetIndices(1)[2] == 0,
                    "Direction packing preserves index width, winding, material submeshes and bounds.", ref checks);
                Require(SlopCityWebMeshBudget.PackNormalsTangents(mesh) == 0,
                    "Already packed layouts do not undergo a second conversion.", ref checks);
                mesh.uv2 = new[] { Vector2.zero, Vector2.right, Vector2.up };
                Require(SlopCityWebMeshBudget.PackNormalsTangents(mesh) == 0 && mesh.uv2.Length == 3,
                    "Meshes with additional UV streams retain their authored layout.", ref checks);
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        private static void CheckCrowdCpuData(Scene scene, ref int checks)
        {
            var mesh = new Mesh { name = "Crowd_CPU_Copy_Fixture" };
            var physics = new Mesh { name = "Crowd_Reserved_Physics_Fixture" };
            try
            {
                Vector3[] positions = { Vector3.zero, Vector3.right, Vector3.up };
                foreach (Mesh item in new[] { mesh, physics })
                { item.vertices = positions; item.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0); }
                Bounds bounds = mesh.bounds;
                var owner = new GameObject("Crowd_CPU_Copy_Owner", typeof(SlopCityCrowd));
                SceneManager.MoveGameObjectToScene(owner, scene);
                SlopCityCrowd crowd = owner.GetComponent<SlopCityCrowd>();
                crowd.Configure(new SlopCityCrowdVisuals { parts = new[] { mesh, mesh, physics } },
                    Array.Empty<SlopCityCrowdRoute>(), Array.Empty<SlopCityNpcSeed>());
                var collider = new GameObject("Crowd_Reserved_Collider", typeof(MeshCollider));
                SceneManager.MoveGameObjectToScene(collider, scene);
                collider.GetComponent<MeshCollider>().sharedMesh = physics;
                SlopCityWebMeshBudget.Prepare(scene);
                Require(!mesh.isReadable && mesh.vertexCount == 3 && mesh.GetIndexCount(0) == 3 && mesh.bounds == bounds,
                    "Render-only crowd parts keep GPU geometry, topology and bounds while dropping their CPU copy.", ref checks);
                Require(crowd.Visuals.parts[0] == mesh && crowd.Visuals.parts[1] == mesh,
                    "Repeated crowd part references preserve shared ownership after CPU finalization.", ref checks);
                Require(physics.isReadable && crowd.Visuals.parts[2] == physics,
                    "Parts shared with physics keep their readable data and original ownership.", ref checks);
                SlopCityWebMeshBudget.Prepare(scene);
                Require(!mesh.isReadable && physics.isReadable,
                    "Repeated CPU finalization is idempotent and preserves excluded data.", ref checks);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(physics);
            }
        }

    }
}
#endif
