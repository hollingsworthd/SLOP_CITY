#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    // Isolated temporary-clone probe, graphics enabled. Run with -executeMethod
    // SlopCity.Editor.SlopCityVertexPackingProbe.Run -quit (no -nographics).
    public static class SlopCityVertexPackingProbe
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct PackedVertex
        {
            public Vector3 position;
            public ushort nx, ny, nz, nw;
            public ushort tx, ty, tz, tw;
            public Vector2 uv;
        }
        [Serializable] private sealed class Receipt
        {
            public string startedAtUtc, finishedAtUtc, unityVersion, graphicsDevice, inputIndexFormat, outputIndexFormat;
            public bool passed, packingPreserved, batched, outputReadable, half4Supported;
            public int inputStride, outputStride, outputVertexCount, outputIndexCount, litPixelsBefore, litPixelsAfter;
            public float maxPositionError, maxNormalError, maxTangentError, maxUvError, meanPixelDifference;
            public string[] inputAttributes, outputAttributes;
            public string scope = "Two readable four-vertex meshes with float32 positions/UV0 and Float16 vec4 normals/tangents, UInt16 indices; one rotated renderer; runtime StaticBatchingUtility.Combine. Measures actual resulting layout/index format, transformed vertex/norm/tangent/UV fidelity, bounds and Standard material render before/after. Editor graphics backend only; this does not establish Web build-time batching or WebGL hardware compatibility.";
            public string[] primarySources = {
                "https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.SetVertexBufferParams.html",
                "https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mathf.FloatToHalf.html",
                "https://docs.unity3d.com/6000.3/Documentation/ScriptReference/StaticBatchingUtility.Combine.html",
                "https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.GetVertexBufferStride.html",
                "https://docs.unity3d.com/6000.3/Documentation/ScriptReference/MeshUtility.AcquireReadOnlyMeshData.html" };
            public List<string> checks = new List<string>(), errors = new List<string>(), runtimeErrors = new List<string>();
        }
        private static Receipt report;
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); report.checks.Add(message); }
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Use a dedicated graphics-enabled batch Editor outside Play Mode.");
            report = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName };
            string directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports");
            Directory.CreateDirectory(directory);
            Scene scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null; Material material = null; RenderTexture target = null; Texture2D readback = null;
            var meshes = new HashSet<Mesh>();
            Application.LogCallback logging = (message, trace, type) => {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    report.runtimeErrors.Add(message + "\n" + trace);
            };
            Application.logMessageReceived += logging;
            try
            {
                report.half4Supported = SystemInfo.SupportsVertexAttributeFormat(VertexAttributeFormat.Float16, 4);
                Check(report.half4Supported, "Graphics backend supports Float16 four-component vertex attributes.");
                Check(Marshal.SizeOf(typeof(PackedVertex)) == 36, "Managed packed vertex is exactly 36 bytes.");
                root = new GameObject("PackingProbe_PrivateRoot"); SceneManager.MoveGameObjectToScene(root, scene);
                Shader shader = Shader.Find("Standard"); Check(shader && shader.isSupported, "Built-in Standard shader is supported.");
                material = new Material(shader) { color = new Color(.8f, .75f, .65f, 1) };
                material.SetFloat("_Metallic", 0); material.SetFloat("_Glossiness", .15f);
                var filters = new MeshFilter[2]; var renderers = new MeshRenderer[2];
                var expectedPositions = new List<Vector3>(); var expectedNormals = new List<Vector3>();
                var expectedTangents = new List<Vector4>(); var expectedUvs = new List<Vector2>();
                for (int index = 0; index < 2; index++)
                {
                    var child = new GameObject("PackedQuad_" + index); child.transform.SetParent(root.transform, false);
                    child.transform.localPosition = new Vector3(index == 0 ? -.8f : .8f, 0, 0);
                    child.transform.localRotation = Quaternion.Euler(0, index == 0 ? 0 : 23f, 0);
                    Mesh mesh = CreateMesh(); meshes.Add(mesh);
                    filters[index] = child.AddComponent<MeshFilter>(); filters[index].sharedMesh = mesh;
                    renderers[index] = child.AddComponent<MeshRenderer>(); renderers[index].sharedMaterial = material;
                    child.isStatic = true;
                    Check(mesh.isReadable && mesh.GetVertexBufferStride(0) == 36 && mesh.indexFormat == IndexFormat.UInt16,
                        "Input " + index + " retains readable 36-byte vertices and UInt16 indices.");
                    var positions = mesh.vertices; var normals = new List<Vector3>(); mesh.GetNormals(normals);
                    var tangents = new List<Vector4>(); mesh.GetTangents(tangents); var uv = mesh.uv;
                    for (int vertex = 0; vertex < positions.Length; vertex++)
                    {
                        expectedPositions.Add(child.transform.TransformPoint(positions[vertex]));
                        expectedNormals.Add(child.transform.TransformDirection(normals[vertex]));
                        Vector3 tangent = child.transform.TransformDirection(new Vector3(tangents[vertex].x, tangents[vertex].y, tangents[vertex].z));
                        expectedTangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, tangents[vertex].w)); expectedUvs.Add(uv[vertex]);
                    }
                }
                report.inputStride = filters[0].sharedMesh.GetVertexBufferStride(0);
                report.inputAttributes = Describe(filters[0].sharedMesh); report.inputIndexFormat = filters[0].sharedMesh.indexFormat.ToString();
                var lightOwner = new GameObject("PackingProbe_Light"); lightOwner.transform.SetParent(root.transform, false);
                Light light = lightOwner.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f;
                lightOwner.transform.rotation = Quaternion.Euler(25, -20, 0);
                var cameraOwner = new GameObject("PackingProbe_Camera"); cameraOwner.transform.SetParent(root.transform, false);
                Camera camera = cameraOwner.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
                camera.transform.localPosition = new Vector3(0, 0, -4); camera.transform.localRotation = Quaternion.identity;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.orthographic = true; camera.orthographicSize = 1.4f; camera.nearClipPlane = .1f; camera.farClipPlane = 10;
                target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                readback = new Texture2D(256, 256, TextureFormat.RGB24, false);
                Color32[] before = Capture(camera, target, readback, Path.Combine(directory, "vertex-packing-probe-before.png"));
                report.litPixelsBefore = LitPixels(before);
                Check(report.litPixelsBefore > 1000, "Standard material renders the packed input geometry with lit surface pixels.");
                StaticBatchingUtility.Combine(new[] { filters[0].gameObject, filters[1].gameObject }, root);
                Mesh combined = filters[0].sharedMesh; meshes.Add(combined); meshes.Add(filters[1].sharedMesh);
                report.batched = renderers[0].isPartOfStaticBatch && renderers[1].isPartOfStaticBatch;
                Check(report.batched && combined == filters[1].sharedMesh && combined.vertexCount == 8,
                    "StaticBatchingUtility creates one shared eight-vertex internal batch.");
                report.outputStride = 0;
                for (int stream = 0; stream < combined.vertexBufferCount; stream++) report.outputStride += combined.GetVertexBufferStride(stream);
                report.outputAttributes = Describe(combined); report.outputIndexFormat = combined.indexFormat.ToString();
                report.outputVertexCount = combined.vertexCount;
                for (int submesh = 0; submesh < combined.subMeshCount; submesh++) report.outputIndexCount += (int)combined.GetIndexCount(submesh);
                report.outputReadable = combined.isReadable;
                report.packingPreserved = report.outputStride == 36
                    && combined.GetVertexAttributeFormat(VertexAttribute.Normal) == VertexAttributeFormat.Float16
                    && combined.GetVertexAttributeFormat(VertexAttribute.Tangent) == VertexAttributeFormat.Float16;
                Color32[] after = Capture(camera, target, readback, Path.Combine(directory, "vertex-packing-probe-after.png"));
                report.litPixelsAfter = LitPixels(after);
                double difference = 0;
                for (int pixel = 0; pixel < before.Length; pixel++) difference += Math.Abs((int)before[pixel].r - after[pixel].r)
                    + Math.Abs((int)before[pixel].g - after[pixel].g) + Math.Abs((int)before[pixel].b - after[pixel].b);
                report.meanPixelDifference = (float)(difference / (before.Length * 3d * 255d));
                Check(report.litPixelsAfter > 1000 && report.meanPixelDifference < .02f, "Standard shader rendering remains visually equivalent after batching.");
                Vector3[] outputPositions, outputNormals; Vector4[] outputTangents; Vector2[] outputUvs;
                // Runtime CPU readability can be released by batching. The documented
                // Editor API bypasses that flag for read-only inspection; it does not
                // modify output layout/readability or re-enable runtime CPU retention.
                using (Mesh.MeshDataArray readOnly = MeshUtility.AcquireReadOnlyMeshData(combined))
                using (var positions = new NativeArray<Vector3>(8, Allocator.Temp))
                using (var normals = new NativeArray<Vector3>(8, Allocator.Temp))
                using (var tangents = new NativeArray<Vector4>(8, Allocator.Temp))
                using (var uvs = new NativeArray<Vector2>(8, Allocator.Temp))
                {
                    Check(readOnly.Length == 1 && readOnly[0].vertexCount == 8, "Editor read-only mesh data inspects the unreadable combined batch without changing it.");
                    Mesh.MeshData data = readOnly[0];
                    data.GetVertices(positions); data.GetNormals(normals); data.GetTangents(tangents); data.GetUVs(0, uvs);
                    outputPositions = positions.ToArray(); outputNormals = normals.ToArray();
                    outputTangents = tangents.ToArray(); outputUvs = uvs.ToArray();
                }
                Check(outputNormals.Length == 8 && outputTangents.Length == 8 && outputUvs.Length == 8, "Combined batch retains all normal, tangent and UV entries.");
                for (int vertex = 0; vertex < 8; vertex++)
                {
                    int closest = 0; float error = float.MaxValue;
                    for (int index = 0; index < expectedPositions.Count; index++)
                    {
                        float distance = Vector3.Distance(outputPositions[vertex], expectedPositions[index]);
                        if (distance < error) { error = distance; closest = index; }
                    }
                    report.maxPositionError = Mathf.Max(report.maxPositionError, error);
                    report.maxNormalError = Mathf.Max(report.maxNormalError, Vector3.Distance(outputNormals[vertex], expectedNormals[closest]));
                    report.maxTangentError = Mathf.Max(report.maxTangentError, Vector4.Distance(outputTangents[vertex], expectedTangents[closest]));
                    report.maxUvError = Mathf.Max(report.maxUvError, Vector2.Distance(outputUvs[vertex], expectedUvs[closest]));
                    Check(combined.bounds.Contains(outputPositions[vertex]), "Combined bounds include vertex " + vertex + ".");
                }
                Check(report.maxPositionError < .00001f && report.maxUvError == 0f, "Batching preserves full-precision positions and UVs.");
                Check(report.maxNormalError < .002f && report.maxTangentError < .002f, "Transformed normals and tangents retain bounded half-precision error.");

            }
            catch (Exception exception) { report.errors.Add(exception.ToString()); }
            finally
            {
                if (root) Object.DestroyImmediate(root);
                foreach (Mesh mesh in meshes) if (mesh) Object.DestroyImmediate(mesh);
                if (material) Object.DestroyImmediate(material);
                if (target) { target.Release(); Object.DestroyImmediate(target); }
                if (readback) Object.DestroyImmediate(readback);
                EditorSceneManager.ClosePreviewScene(scene); Application.logMessageReceived -= logging;
                report.passed = report.errors.Count == 0 && report.runtimeErrors.Count == 0; report.finishedAtUtc = DateTime.UtcNow.ToString("O");
                File.WriteAllText(Path.Combine(directory, "vertex-packing-probe.json"), JsonUtility.ToJson(report, true));
                Debug.Log("SLOP_CITY_VERTEX_PACKING_PROBE " + JsonUtility.ToJson(report));
            }
            if (!report.passed) throw new InvalidOperationException("Vertex packing probe failed; inspect its receipt.");
        }
        private static Mesh CreateMesh()
        {
            var mesh = new Mesh { name = "Float16 normal tangent quad" };
            mesh.SetVertexBufferParams(4, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float16, 4),
                new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float16, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
            Vector3[] positions = { new Vector3(-.65f, -.8f, 0), new Vector3(-.65f, .8f, 0), new Vector3(.65f, .8f, 0), new Vector3(.65f, -.8f, 0) };
            Vector2[] uv = { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            var vertices = new PackedVertex[4];
            for (int index = 0; index < 4; index++) vertices[index] = new PackedVertex { position = positions[index],
                nz = Mathf.FloatToHalf(-1), tx = Mathf.FloatToHalf(1), tw = Mathf.FloatToHalf(-1), uv = uv[index] };
            mesh.SetVertexBufferData(vertices, 0, 0, 4);
            mesh.SetIndexBufferParams(6, IndexFormat.UInt16);
            mesh.SetIndexBufferData(new ushort[] { 0, 1, 2, 0, 2, 3 }, 0, 0, 6);
            mesh.subMeshCount = 1; mesh.SetSubMesh(0, new SubMeshDescriptor(0, 6, MeshTopology.Triangles));
            mesh.RecalculateBounds(); return mesh;
        }
        private static string[] Describe(Mesh mesh)
        {
            var result = new List<string>();
            foreach (VertexAttributeDescriptor attribute in mesh.GetVertexAttributes())
                result.Add(attribute.attribute + ":" + attribute.format + "x" + attribute.dimension + "@" + attribute.stream);
            return result.ToArray();
        }
        private static Color32[] Capture(Camera camera, RenderTexture target, Texture2D readback, string png)
        {
            camera.Render(); RenderTexture previous = RenderTexture.active;
            try { RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); readback.Apply(); }
            finally { RenderTexture.active = previous; }
            File.WriteAllBytes(png, readback.EncodeToPNG()); return readback.GetPixels32();
        }
        private static int LitPixels(Color32[] pixels)
        { int count = 0; foreach (Color32 pixel in pixels) if (pixel.r + pixel.g + pixel.b > 60) count++; return count; }
    }
}
#endif
