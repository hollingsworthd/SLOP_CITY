#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Bounded, offline verification of the actual saved city's Web detail export and exact mesh reconstruction.</summary>
    public static class SlopCityWebDetailStreamingSmokeTest
    {
        private static int checks;
        private static Receipt receipt;
        private static string scratch;
        private static readonly List<Mesh> owned = new List<Mesh>();

        // Dedicated isolated Editor; this never saves a scene or changes import settings.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Detail export checks require a dedicated batch Editor outside Play Mode.");
            checks = 0;
            receipt = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            scratch = Path.Combine(Path.GetTempPath(), "SlopCityDetailCheck-" + Guid.NewGuid().ToString("N"));
            int exit = 0;
            try
            {
                Directory.CreateDirectory(scratch);
                CodecCases();
                EligibilityCases();
                ActualScene();
                receipt.success = true;
            }
            catch (Exception error) { receipt.error = error.ToString(); exit = 1; }
            finally
            {
                foreach (Mesh mesh in owned) if (mesh) Object.DestroyImmediate(mesh);
                owned.Clear();
                if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
                receipt.checks = checks; receipt.completedAtUtc = DateTime.UtcNow.ToString("O");
                Directory.CreateDirectory("Builds/Reports");
                File.WriteAllText("Builds/Reports/web-detail-streaming-check.json", JsonUtility.ToJson(receipt, true));
            }
            Debug.Log("SLOP_CITY_WEB_DETAIL_STREAMING_CHECK checks=" + checks + " success=" + receipt.success);
            EditorApplication.Exit(exit);
        }

        private static void CodecCases()
        {
            Mesh source = AttributeFixture(); owned.Add(source);
            string expected = MeshFingerprint(source);
            byte[] payload = SlopCityDetailMeshCodec.Encode(source);
            Mesh decoded = SlopCityDetailMeshCodec.Decode(payload, SlopCityDetailMeshCodec.ComputeHash(payload), payload.Length, false);
            owned.Add(decoded);
            receipt.codecMismatch = MeshDifference(source, decoded);
            Check(MeshFingerprint(decoded) == expected,
                "Codec preserves independently captured raw mixed-format/multiple-stream vertex bytes, UInt32 indices, submesh descriptors and bounds. " + receipt.codecMismatch);
            Check(decoded.vertexBufferCount == 3 && decoded.subMeshCount == 2,
                "Codec preserves authored stream and material-submesh membership.");
            byte[] damaged = (byte[])payload.Clone(); damaged[damaged.Length / 2] ^= 0x41;
            Reject(() => SlopCityDetailMeshCodec.Decode(damaged, SlopCityDetailMeshCodec.ComputeHash(payload), payload.Length),
                "A payload changed in transit is rejected by its published digest.");
            Reject(() => SlopCityDetailMeshCodec.Decode(payload, SlopCityDetailMeshCodec.ComputeHash(payload), payload.Length + 1),
                "Unexpected payload length is rejected before mesh admission.");
            byte[] header = (byte[])payload.Clone(); header[0] ^= 0xff;
            Reject(() => SlopCityDetailMeshCodec.Decode(header, SlopCityDetailMeshCodec.ComputeHash(header), header.Length),
                "Even a digest-consistent invalid format header is rejected.");
            foreach (int length in new[] { 0, 1, 8, payload.Length / 2, payload.Length - 1 })
            {
                byte[] truncated = payload.Take(length).ToArray();
                Reject(() => SlopCityDetailMeshCodec.Decode(truncated, SlopCityDetailMeshCodec.ComputeHash(truncated), truncated.Length),
                    "A self-consistent truncated payload cannot manufacture a partly usable mesh.");
            }
            byte[] oversized = new byte[SlopCityDetailMeshCodec.MaximumPayloadBytes + 1];
            Reject(() => SlopCityDetailMeshCodec.Decode(oversized, SlopCityDetailMeshCodec.ComputeHash(oversized), oversized.Length),
                "A payload above the transport allocation limit is rejected.");
            Check(MeshFingerprint(source) == expected, "Encoding and rejected decodes leave the native/source mesh unchanged.");
        }

        private static Mesh AttributeFixture()
        {
            var mesh = new Mesh { name = "Detail_Raw_Attribute_Fixture", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertexBufferParams(6,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4, 1),
                new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.SNorm8, 4, 1),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 2));
            mesh.SetVertexBufferData(new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one, Vector3.forward, Vector3.left }, 0, 0, 6, 0);
            var directions = new byte[48];
            for (int index = 0; index < directions.Length; index++) directions[index] = (byte)(index * 13 + 7);
            mesh.SetVertexBufferData(directions, 0, 0, directions.Length, 1);
            var colorUv = new byte[72];
            for (int vertex = 0; vertex < 6; vertex++)
            {
                colorUv[vertex * 12] = (byte)(vertex * 31); colorUv[vertex * 12 + 1] = 29;
                colorUv[vertex * 12 + 2] = 219; colorUv[vertex * 12 + 3] = 255;
                Buffer.BlockCopy(BitConverter.GetBytes(vertex * .125f), 0, colorUv, vertex * 12 + 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(-vertex * .25f), 0, colorUv, vertex * 12 + 8, 4);
            }
            mesh.SetVertexBufferData(colorUv, 0, 0, colorUv.Length, 2);
            mesh.SetIndexBufferParams(6, IndexFormat.UInt32);
            mesh.SetIndexBufferData(new uint[] { 0, 1, 2, 0, 2, 3 }, 0, 0, 6);
            var bounds = new Bounds(new Vector3(.2f, .3f, .1f), new Vector3(4, 5, 6));
            mesh.subMeshCount = 2;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, 3) { baseVertex = 0, firstVertex = 0, vertexCount = 3, bounds = bounds }, MeshUpdateFlags.DontRecalculateBounds);
            mesh.SetSubMesh(1, new SubMeshDescriptor(3, 3) { baseVertex = 2, firstVertex = 2, vertexCount = 4, bounds = bounds }, MeshUpdateFlags.DontRecalculateBounds);
            mesh.bounds = bounds;
            return mesh;
        }

        private static void EligibilityCases()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            Mesh mesh = AttributeFixture(); owned.Add(mesh);
            Material material = new Material(Shader.Find("Standard"));
            string original = MeshFingerprint(mesh);
            try
            {
                for (int guard = -1; guard < 19; guard++)
                {
                    var root = new GameObject("SlopCity_NineBlocks"); SceneManager.MoveGameObjectToScene(root, scene);
                    var japan = new GameObject("City_JapanMetropolis"); japan.transform.SetParent(root.transform, false);
                    var assembly = new GameObject("Japan_Sign_Test"); assembly.transform.SetParent(japan.transform, false);
                    assembly.AddComponent<SlopCityInteractable>().Configure(SlopCityInteractionKind.Inspect, "Preserved sign", false);
                    var art = new GameObject("Manufactured_Web_Passive_Sign_Test", typeof(MeshFilter), typeof(MeshRenderer));
                    art.transform.SetParent(assembly.transform, false);
                    MeshFilter filter = art.GetComponent<MeshFilter>(); filter.sharedMesh = mesh;
                    MeshRenderer renderer = art.GetComponent<MeshRenderer>(); renderer.sharedMaterials = new[] { material, material };
                    var references = new HashSet<Object>(); var required = new HashSet<Mesh>();
                    try
                    {
                        if (guard == 0) art.AddComponent<BoxCollider>();
                        if (guard == 1) required.Add(mesh);
                        if (guard == 2) references.Add(mesh);
                        if (guard == 3) references.Add(filter);
                        if (guard == 4) references.Add(renderer);
                        if (guard == 5) renderer.enabled = false;
                        if (guard == 6) art.SetActive(false);
                        if (guard == 7) renderer.forceRenderingOff = true;
                        if (guard == 8) { var block = new MaterialPropertyBlock(); block.SetFloat("_Glossiness", .5f); renderer.SetPropertyBlock(block); }
                        if (guard == 9) renderer.additionalVertexStreams = mesh;
                        if (guard == 10) renderer.lightmapIndex = 0;
                        if (guard == 11) renderer.sharedMaterials = new[] { material };
                        if (guard == 12) new GameObject("Mutable child").transform.SetParent(art.transform, false);
                        if (guard == 13) assembly.AddComponent<Rigidbody>().isKinematic = true;
                        if (guard == 14) assembly.AddComponent<Animator>();
                        if (guard == 15) assembly.GetComponent<SlopCityInteractable>().Configure(SlopCityInteractionKind.Bench, "Functional device", false);
                        if (guard == 16) art.name = "Unknown_Art";
                        if (guard == 17) japan.name = "Unknown_City";
                        if (guard == 18) material.renderQueue = 3000;
                        Check(SlopCityWebDetailExport.CanStream(filter, references, required) == (guard < 0),
                            "Eligibility admits positive passive sign art and rejects collision, referenced, mutable, inactive, special-rendering and unknown-ancestry exceptions (case " + guard + ").");
                        Check(filter.sharedMesh == mesh && MeshFingerprint(mesh) == original,
                            "An eligibility decision cannot alter the original source geometry or binding.");
                    }
                    finally { material.renderQueue = -1; Object.DestroyImmediate(root); }
                }
            }
            finally { Object.DestroyImmediate(material); EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void ActualScene()
        {
            string scenePath = SlopCitySceneBuilder.ScenePath;
            string sourceHash = FileHash(scenePath);
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            SlopCityWebGeometryBuildProcessor.ProtectRuntimeBuildingGeometry(scene);
            SlopCityWebDecorativeMerge.Prepare(scene);
            var bounds = new GameObject("Detail_Check_Authored_Browser_Bounds");
            SceneManager.MoveGameObjectToScene(bounds, scene);
            bounds.AddComponent<SlopCityBrowserGeometry>().Capture(scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray());
            SlopCityWebMeshBudget.Prepare(scene, true);
            SlopCityWebSingletonMaterialBuildProcessor.Prepare(scene);
            var before = new SceneSnapshot(scene);
            MeshFilter guarded = before.filters.Keys.First(item => SlopCityWebDetailExport.IsCandidateName(item.name));
            BoxCollider unexpectedCollider = guarded.gameObject.AddComponent<BoxCollider>();
            bool rejected = false;
            try { SlopCityWebDetailExport.Extract(scene, Path.Combine(scratch, "rejected")); }
            catch (Exception) { rejected = true; }
            finally { Object.DestroyImmediate(unexpectedCollider); }
            Check(rejected, "A real positive candidate gaining physical ownership fails the whole export before committing any stripping.");
            before.AssertUnchanged(new HashSet<MeshFilter>());
            SlopCityDetailStreaming owner = SlopCityWebDetailExport.Extract(scene, scratch);
            Check(owner && owner.Entries.Count > 100, "Actual prepared authored city exports a substantial positive detail workload.");
            var selected = new HashSet<MeshFilter>();
            var decodedByPath = new Dictionary<string, string>(StringComparer.Ordinal);
            long bytes = 0;
            foreach (var entry in owner.Entries)
            {
                Check(entry.target && entry.renderer && selected.Add(entry.target), "Each manifest entry owns one unique preserved physical renderer/filter pair.");
                FilterState state = before.filters[entry.target];
                Check(entry.renderer == state.renderer && entry.renderer.sharedMaterials.SequenceEqual(state.materials),
                    "Every streamed authored surface keeps the exact material identities and renderer object.");
                Check(entry.localBounds == state.mesh.bounds && entry.geometryBytes > 0 && entry.payloadBytes > 0,
                    "Manifest retains the source local bounds and positive bounded geometry costs.");
                Check((GameObjectUtility.GetStaticEditorFlags(entry.target.gameObject) & StaticEditorFlags.BatchingStatic) == 0,
                    "Streamed meshes cannot become retained Unity static-batch copies.");
                Check(entry.target.sharedMesh == entry.farProxy,
                    "Exported filter retains only its explicit lightweight fallback, never its detailed source mesh.");
                if (entry.farProxy)
                {
                    Check(entry.farProxy != state.mesh && entry.farProxy.vertexCount < state.mesh.vertexCount,
                        "Distant roofline fallback is separate geometry with fewer vertices than the real detailed art.");
                    Check(GeometryTop(entry.farProxy) >= GeometryTop(state.mesh) - .01f,
                        "Frame fallback preserves the authored roofline height rather than dropping the entire silhouette.");
                    receipt.proxies++;
                }
                string path = Path.Combine(scratch, "StreamingAssets/Geometry", entry.fileName);
                Check(File.Exists(path) && new FileInfo(path).Length == entry.payloadBytes && FileHash(path) == entry.sha256,
                    "Every exported local transport file matches the manifest size and digest.");
                if (!decodedByPath.TryGetValue(path, out string fingerprint))
                {
                    Mesh mesh = SlopCityDetailMeshCodec.Decode(File.ReadAllBytes(path), entry.sha256, entry.payloadBytes, false);
                    try { fingerprint = MeshFingerprint(mesh); }
                    finally { Object.DestroyImmediate(mesh); }
                    decodedByPath.Add(path, fingerprint);
                }
                bytes += entry.geometryBytes;
                Check(fingerprint == before.meshes[state.mesh],
                    "Actual exported raw vertex/index bytes, attribute formats, bounds and submesh descriptors reconstruct the original saved-scene mesh exactly.");
                Check(MeshFingerprint(state.mesh) == before.meshes[state.mesh],
                    "Exporting the Web copy never rewrites the original native/source mesh's bytes.");
            }
            var stripped = new HashSet<Mesh>(selected.Select(item => before.filters[item].mesh));
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (!component) continue;
                    using (var serialized = new SerializedObject(component))
                    {
                        SerializedProperty property = serialized.GetIterator();
                        while (property.Next(true))
                            if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is Mesh mesh)
                                Check(!stripped.Contains(mesh), "No serialized scene component, including the manifest, retains an exported detailed source mesh.");
                    }
                }
            before.AssertUnchanged(selected);
            Check(FileHash(scenePath) == sourceHash, "Preparation/export never rewrites the saved authored scene on disk.");
            Check(owner.Entries.Any(item => item.farProxy) && owner.Entries.Any(item => !item.farProxy),
                "Actual export covers framed skyline art and passive street equipment.");
            int entries = owner.Entries.Count;
            SlopCityDetailStreaming repeated = SlopCityWebDetailExport.Extract(scene, scratch);
            Check(repeated == owner && repeated.Entries.Count == entries,
                "Repeated extraction is idempotent and preserves the one existing streaming owner and manifest.");
            RuntimeOwnership(owner, before);
            receipt.entries = entries; receipt.uniquePayloads = decodedByPath.Count; receipt.geometryBytes = bytes;
        }

        private static void RuntimeOwnership(SlopCityDetailStreaming owner, SceneSnapshot before)
        {
            owner.InitializeForTesting();
            Check(SlopCityDetailStreaming.ResidentBudgetBytes == 24L * 1024 * 1024 && owner.ResidentBytes == 0 && owner.PendingIndex == -1,
                "Actual runtime starts empty with the explicit 24 MiB geometry residency ceiling.");
            var first = owner.Entries[0]; byte[] bytes = Payload(first);
            int token = owner.BeginForTesting(0);
            Check(token >= 0 && owner.PendingIndex == 0 && owner.BeginForTesting(1) < 0 && owner.PendingIndex == 0,
                "A second request cannot allocate another pending payload or replace the first request's ownership.");
            owner.RetireForTesting(0);
            Check(owner.PendingIndex == -1 && !owner.CompleteForTesting(0, token, bytes)
                && owner.ResidentBytes == 0 && first.target.sharedMesh == first.farProxy,
                "Retiring a pending request invalidates its completion and leaves the fallback intact.");
            token = owner.BeginForTesting(0);
            Check(token >= 0 && !owner.CompleteForTesting(0, token - 1, bytes) && owner.PendingIndex == 0,
                "A stale generation cannot consume a newer pending lease.");
            Check(owner.CompleteForTesting(0, token, bytes) && owner.IsResident(0)
                && MeshFingerprint(first.target.sharedMesh) == before.meshes[before.filters[first.target].mesh],
                "Current request admission binds the real exact authored geometry to its original physical renderer.");
            Mesh resident = first.target.sharedMesh;
            Check(!resident.isReadable && resident != before.filters[first.target].mesh && owner.BeginForTesting(0) < 0,
                "The owned resident is GPU-ready without a retained readable CPU copy or duplicate lease.");
            owner.RetireForTesting(0);
            Check(!resident && first.target.sharedMesh == first.farProxy && owner.ResidentBytes == 0,
                "Retirement destroys the owned mesh and restores its original skyline fallback.");
            token = owner.BeginForTesting(0);
            byte[] corrupt = (byte[])bytes.Clone(); corrupt[corrupt.Length / 2] ^= 1;
            Check(!owner.CompleteForTesting(0, token, corrupt) && owner.PendingIndex == -1 && owner.ResidentBytes == 0
                && first.target.sharedMesh == first.farProxy, "Corrupt completion releases the pending slot and fails closed without changing the world.");
            token = owner.BeginForTesting(0);
            first.target.sharedMesh = before.filters[first.target].mesh;
            Check(!owner.CompleteForTesting(0, token, bytes) && first.target.sharedMesh == before.filters[first.target].mesh
                && owner.ResidentBytes == 0, "An unrelated owner changing a target during download cannot be overwritten by a late completion.");
            first.target.sharedMesh = first.farProxy;

            bool reachedCap = false;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                for (int index = 0; index < owner.Entries.Count; index++)
                {
                    var entry = owner.Entries[index];
                    bool fits = owner.ResidentBytes + entry.geometryBytes <= SlopCityDetailStreaming.ResidentBudgetBytes;
                    long previous = owner.ResidentBytes;
                    token = owner.BeginForTesting(index);
                    if (fits)
                    {
                        Check(token >= 0 && owner.CompleteForTesting(index, token, Payload(entry)),
                            "Every affordable admission succeeds through the real completion path.");
                        Check(owner.ResidentBytes == previous + entry.geometryBytes && owner.IsResident(index),
                            "Each admission accounts for its exact vertex and index bytes once.");
                    }
                    else
                    {
                        reachedCap = true;
                        Check(token < 0 && owner.PendingIndex == -1 && !owner.IsResident(index)
                            && entry.target.sharedMesh == entry.farProxy, "Full residency refuses a new pending allocation and preserves the distant fallback.");
                    }
                    AssertBudget(owner);
                }
                for (int index = 0; index < owner.Entries.Count; index++)
                {
                    Mesh ownedMesh = owner.IsResident(index) ? owner.Entries[index].target.sharedMesh : null;
                    owner.RetireForTesting(index);
                    Check(!ownedMesh && owner.Entries[index].target.sharedMesh == owner.Entries[index].farProxy,
                        "Repeated retirement destroys only runtime-owned art and restores the persistent fallback.");
                    AssertBudget(owner);
                }
                Check(owner.ResidentBytes == 0 && owner.PendingIndex == -1, "Every complete load/retire circuit returns to zero geometry residency and no pending request.");
            }
            Check(reachedCap, "The actual authored city workload exceeds the budget and exercises real rejection at the residency ceiling.");
            Vector3 near = SlopCityDetailStreaming.WorldBounds(first.target.transform, first.localBounds).center;
            owner.TickForTesting(near, 1000f);
            int selected = owner.PendingIndex;
            Check(selected >= 0, "Ordinary distance selection requests nearby real geometry without a network fixture.");
            var chosen = owner.Entries[selected];
            Check(SlopCityDetailStreaming.WorldBounds(chosen.target.transform, chosen.localBounds).SqrDistance(near)
                < SlopCityDetailStreaming.AdmissionDistance * SlopCityDetailStreaming.AdmissionDistance,
                "Selection uses the real transformed authored bounds within the admission distance.");
            owner.TickForTesting(new Vector3(100000, 100000, 100000), 1001f);
            Check(owner.PendingIndex == -1 && owner.ResidentBytes == 0,
                "Moving away cancels pending geometry before its transfer can finish.");
            token = owner.BeginForTesting(0);
            Check(token >= 0 && owner.CompleteForTesting(0, token, bytes), "Detail can be reacquired after cancellation and earlier failures.");
            InvokeDisable(owner);
            Check(owner.PendingIndex == -1 && owner.ResidentBytes == 0 && first.target.sharedMesh == first.farProxy,
                "The production disable callback retires every lease, restores fallback geometry and cancels pending work.");
            RetryCircuit(first, bytes, near);
        }

        private static void RetryCircuit(SlopCityDetailStreaming.DetailEntry entry, byte[] valid, Vector3 near)
        {
            var root = new GameObject("Detail_Check_Isolated_Retry_Owner");
            var owner = root.AddComponent<SlopCityDetailStreaming>(); owner.SetBuildEntries(new[] { entry }); owner.InitializeForTesting();
            byte[] corrupt = (byte[])valid.Clone(); corrupt[corrupt.Length / 2] ^= 1;
            try
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    int token = owner.BeginForTesting(0);
                    Check(token >= 0 && !owner.CompleteForTesting(0, token, corrupt), "A bad payload releases each failed request without admitting geometry.");
                    owner.TickForTesting(near, 0f);
                    Check(owner.PendingIndex == -1 && owner.ResidentBytes == 0, "Distance selection respects failure backoff instead of allocating another immediate request.");
                }
                owner.TickForTesting(near, 1000f);
                Check(owner.PendingIndex == -1 && entry.target.sharedMesh == entry.farProxy,
                    "Three consecutive failed payloads stop retries even after the backoff timer expires.");
                owner.TickForTesting(new Vector3(100000, 100000, 100000), 1001f);
                owner.TickForTesting(near, 1002f);
                int pending = owner.PendingIndex;
                Check(pending == 0, "Leaving the detail area resets its bounded failure circuit for a later visit.");
                owner.RetireForTesting(0);
            }
            finally { InvokeDisable(owner); Object.DestroyImmediate(root); }
        }

        // This fixture stays in Edit Mode. SendMessage routes through Unity's
        // behaviour scheduler, which asserts for a non-ExecuteAlways component
        // even though it can still dispatch the message. Invoke the exact
        // production callback directly and verify its observable cleanup above.
        private static void InvokeDisable(SlopCityDetailStreaming owner)
        {
            MethodInfo callback = typeof(SlopCityDetailStreaming).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
            if (callback == null) throw new MissingMethodException(typeof(SlopCityDetailStreaming).FullName, "OnDisable");
            callback.Invoke(owner, null);
        }

        private static byte[] Payload(SlopCityDetailStreaming.DetailEntry entry)
            => File.ReadAllBytes(Path.Combine(scratch, "StreamingAssets/Geometry", entry.fileName));
        private static void AssertBudget(SlopCityDetailStreaming owner)
        {
            long expected = 0;
            for (int index = 0; index < owner.Entries.Count; index++) if (owner.IsResident(index)) expected += owner.Entries[index].geometryBytes;
            Check(owner.ResidentBytes == expected && expected <= SlopCityDetailStreaming.ResidentBudgetBytes,
                "Independent live-lease accounting reconciles every vertex/index byte within the hard geometry cap.");
        }

        private sealed class SceneSnapshot
        {
            public readonly Dictionary<MeshFilter, FilterState> filters = new Dictionary<MeshFilter, FilterState>();
            public readonly Dictionary<Mesh, string> meshes = new Dictionary<Mesh, string>();
            private readonly Dictionary<Transform, TransformState> transforms = new Dictionary<Transform, TransformState>();
            private readonly Dictionary<Component, string> controls = new Dictionary<Component, string>();
            private readonly Dictionary<SlopCityMetricFacade, Mesh[]> facadeOwnership = new Dictionary<SlopCityMetricFacade, Mesh[]>();
            public SceneSnapshot(Scene scene)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform item in root.GetComponentsInChildren<Transform>(true)) transforms.Add(item, new TransformState(item));
                    foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        filters.Add(filter, new FilterState(filter));
                        if (filter.sharedMesh && !meshes.ContainsKey(filter.sharedMesh)) meshes.Add(filter.sharedMesh, MeshFingerprint(filter.sharedMesh));
                    }
                    foreach (Component component in root.GetComponentsInChildren<Component>(true))
                        if (component && !(component is Transform) && !(component is Renderer) && !(component is MeshFilter))
                        {
                            controls.Add(component, ControlJson(component));
                            if (component is SlopCityMetricFacade facade) facadeOwnership.Add(facade, OwnedMeshes(facade));
                        }
                }
            }
            public void AssertUnchanged(HashSet<MeshFilter> selected)
            {
                foreach (var item in transforms) Check(item.Key && item.Value.EqualsCurrent(item.Key), "Every original parent, transform, layer and active state survives export.");
                foreach (var item in controls) Check(item.Key && ControlJson(item.Key) == item.Value,
                    "Every original collider, door, media binding, interaction and other control retains its exact serialized state.");
                var streamedSources = new HashSet<Mesh>(selected.Select(item => filters[item].mesh));
                foreach (var item in facadeOwnership) Check(OwnedMeshes(item.Key).SequenceEqual(item.Value.Where(mesh => !streamedSources.Contains(mesh))),
                    "Facade ownership relinquishes only the exact exported frame identity and preserves every other owned mesh.");
                foreach (var item in filters)
                {
                    FilterState state = item.Value;
                    Check(item.Key && (!state.renderer || state.renderer && state.renderer.enabled == state.enabled
                        && state.renderer.sharedMaterials.SequenceEqual(state.materials)), "Original renderer enabled states and material identities remain exact.");
                    if (!selected.Contains(item.Key)) Check(item.Key.sharedMesh == state.mesh
                        && GameObjectUtility.GetStaticEditorFlags(item.Key.gameObject) == state.flags,
                        "Every ineligible mesh binding and its static flags remain unchanged.");
                    else Check((GameObjectUtility.GetStaticEditorFlags(item.Key.gameObject) & ~StaticEditorFlags.BatchingStatic)
                        == (state.flags & ~StaticEditorFlags.BatchingStatic), "Export changes only the eligible object's batching flag.");
                }
            }
            private static string ControlJson(Component component)
            {
                string json = EditorJsonUtility.ToJson(component);
                return component is SlopCityMetricFacade ? Regex.Replace(json, "\"ownedMeshes\":\\[[^\\]]*\\]", "\"ownedMeshes\":[]") : json;
            }
            private static Mesh[] OwnedMeshes(SlopCityMetricFacade facade)
            {
                using (var serialized = new SerializedObject(facade))
                {
                    SerializedProperty items = serialized.FindProperty("ownedMeshes"); var result = new Mesh[items.arraySize];
                    for (int index = 0; index < result.Length; index++) result[index] = items.GetArrayElementAtIndex(index).objectReferenceValue as Mesh;
                    return result;
                }
            }
        }
        private sealed class FilterState
        {
            public Mesh mesh; public MeshRenderer renderer; public Material[] materials; public bool enabled; public StaticEditorFlags flags;
            public FilterState(MeshFilter filter)
            {
                mesh = filter.sharedMesh; renderer = filter.GetComponent<MeshRenderer>(); materials = renderer ? renderer.sharedMaterials : Array.Empty<Material>();
                enabled = renderer && renderer.enabled; flags = GameObjectUtility.GetStaticEditorFlags(filter.gameObject);
            }
        }
        private sealed class TransformState
        {
            private readonly Transform parent; private readonly Vector3 position, scale; private readonly Quaternion rotation;
            private readonly bool active; private readonly int layer;
            public TransformState(Transform item) { parent = item.parent; position = item.localPosition; rotation = item.localRotation; scale = item.localScale; active = item.gameObject.activeSelf; layer = item.gameObject.layer; }
            public bool EqualsCurrent(Transform item) => item.parent == parent && item.localPosition == position && item.localRotation == rotation
                && item.localScale == scale && item.gameObject.activeSelf == active && item.gameObject.layer == layer;
        }

        // Independent byte capture, deliberately not implemented with the production encoder.
        private static string MeshDifference(Mesh expected, Mesh actual)
        {
            var differences = new List<string>();
            using (Mesh.MeshDataArray a = MeshUtility.AcquireReadOnlyMeshData(expected))
            using (Mesh.MeshDataArray b = MeshUtility.AcquireReadOnlyMeshData(actual))
            {
                if (a[0].vertexCount != b[0].vertexCount) differences.Add("vertexCount: " + a[0].vertexCount + " -> " + b[0].vertexCount);
                if (a[0].vertexBufferCount != b[0].vertexBufferCount) differences.Add("vertexBufferCount: " + a[0].vertexBufferCount + " -> " + b[0].vertexBufferCount);
                if (a[0].indexFormat != b[0].indexFormat) differences.Add("indexFormat: " + a[0].indexFormat + " -> " + b[0].indexFormat);
                VertexAttributeDescriptor[] attributesA = expected.GetVertexAttributes(), attributesB = actual.GetVertexAttributes();
                if (attributesA.Length != attributesB.Length) differences.Add("attributeCount: " + attributesA.Length + " -> " + attributesB.Length);
                for (int index = 0; index < Mathf.Min(attributesA.Length, attributesB.Length); index++)
                    if (!attributesA[index].Equals(attributesB[index])) differences.Add("attribute[" + index + "]: " + attributesA[index] + " -> " + attributesB[index]);
                for (int stream = 0; stream < Mathf.Min(a[0].vertexBufferCount, b[0].vertexBufferCount); stream++)
                {
                    if (expected.GetVertexBufferStride(stream) != actual.GetVertexBufferStride(stream)) differences.Add("stride[" + stream + "]: " + expected.GetVertexBufferStride(stream) + " -> " + actual.GetVertexBufferStride(stream));
                    ByteDifference(differences, "stream[" + stream + "]", a[0].GetVertexData<byte>(stream).ToArray(), b[0].GetVertexData<byte>(stream).ToArray());
                }
                ByteDifference(differences, "indices", a[0].GetIndexData<byte>().ToArray(), b[0].GetIndexData<byte>().ToArray());
                if (a[0].subMeshCount != b[0].subMeshCount) differences.Add("subMeshCount: " + a[0].subMeshCount + " -> " + b[0].subMeshCount);
                for (int sub = 0; sub < Mathf.Min(a[0].subMeshCount, b[0].subMeshCount); sub++)
                {
                    SubMeshDescriptor x = a[0].GetSubMesh(sub), y = b[0].GetSubMesh(sub);
                    string source = SubmeshText(x), target = SubmeshText(y);
                    if (source != target) differences.Add("submesh[" + sub + "]: " + source + " -> " + target);
                }
            }
            if (BoundsText(expected.bounds) != BoundsText(actual.bounds)) differences.Add("mesh.bounds: " + BoundsText(expected.bounds) + " -> " + BoundsText(actual.bounds));
            return differences.Count == 0 ? "No field differences." : string.Join("; ", differences);
        }
        private static void ByteDifference(List<string> differences, string label, byte[] a, byte[] b)
        {
            if (a.Length != b.Length) differences.Add(label + ".length: " + a.Length + " -> " + b.Length);
            for (int index = 0; index < Mathf.Min(a.Length, b.Length); index++)
                if (a[index] != b[index])
                {
                    differences.Add(label + " first byte mismatch at " + index + ": " + a[index] + " -> " + b[index]
                        + ", SHA256 " + Hash(a) + " -> " + Hash(b)); break;
                }
        }
        private static string SubmeshText(SubMeshDescriptor sub) => "indexStart=" + sub.indexStart + ",indexCount=" + sub.indexCount
            + ",topology=" + sub.topology + ",baseVertex=" + sub.baseVertex + ",firstVertex=" + sub.firstVertex + ",vertexCount=" + sub.vertexCount
            + ",bounds=" + BoundsText(sub.bounds);
        private static string BoundsText(Bounds bounds) => string.Join(",", new[] { bounds.center.x, bounds.center.y, bounds.center.z,
            bounds.size.x, bounds.size.y, bounds.size.z }.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        private static float GeometryTop(Mesh mesh)
        {
            using (Mesh.MeshDataArray data = MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
            {
                data[0].GetVertices(vertices); float top = float.NegativeInfinity;
                for (int index = 0; index < vertices.Length; index++) top = Mathf.Max(top, vertices[index].y);
                return top;
            }
        }
        private static string MeshFingerprint(Mesh mesh)
        {
            using (var bytes = new MemoryStream())
            using (var output = new BinaryWriter(bytes))
            using (Mesh.MeshDataArray data = MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                Mesh.MeshData source = data[0];
                output.Write(source.vertexCount); output.Write(source.vertexBufferCount); output.Write((int)source.indexFormat); Write(output, mesh.bounds);
                VertexAttributeDescriptor[] attributes = mesh.GetVertexAttributes(); output.Write(attributes.Length);
                foreach (var attribute in attributes) { output.Write((int)attribute.attribute); output.Write((int)attribute.format); output.Write(attribute.dimension); output.Write(attribute.stream); }
                for (int stream = 0; stream < source.vertexBufferCount; stream++)
                { byte[] buffer = source.GetVertexData<byte>(stream).ToArray(); output.Write(buffer.Length); output.Write(buffer); }
                byte[] indices = source.GetIndexData<byte>().ToArray(); output.Write(indices.Length); output.Write(indices);
                output.Write(source.subMeshCount);
                for (int index = 0; index < source.subMeshCount; index++)
                {
                    SubMeshDescriptor sub = source.GetSubMesh(index);
                    output.Write(sub.indexStart); output.Write(sub.indexCount); output.Write((int)sub.topology); output.Write(sub.baseVertex);
                    output.Write(sub.firstVertex); output.Write(sub.vertexCount); Write(output, sub.bounds);
                }
                output.Flush(); return Hash(bytes.ToArray());
            }
        }
        private static void Write(BinaryWriter writer, Bounds bounds)
        { foreach (float value in new[] { bounds.center.x, bounds.center.y, bounds.center.z, bounds.size.x, bounds.size.y, bounds.size.z }) writer.Write(value); }
        private static string Hash(byte[] bytes) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string FileHash(string path) { using (SHA256 sha = SHA256.Create()) using (FileStream file = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
        private static void Reject(Func<Mesh> action, string message)
        {
            Mesh unexpected = null; bool rejected = false;
            try { unexpected = action(); } catch (Exception) { rejected = true; }
            finally { if (unexpected) Object.DestroyImmediate(unexpected); }
            Check(rejected, message);
        }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
        [Serializable] private sealed class Receipt
        {
            public string startedAtUtc, completedAtUtc, unityVersion, error, codecMismatch;
            public bool success; public int checks, entries, uniquePayloads, proxies; public long geometryBytes;
            public string scope = "Actual saved scene exact Web detail export, ownership guards, codec, cap, cancellation and repeated lease checks. Offline Editor fixture; no physical-device or sustained-memory acceptance.";
        }
    }
}
#endif
