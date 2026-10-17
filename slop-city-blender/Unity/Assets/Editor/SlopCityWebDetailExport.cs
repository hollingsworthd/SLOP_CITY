#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Exports only positive immutable decoration from the temporary Web scene before static batching.</summary>
    [BuildCallbackVersion(1)]
    public sealed class SlopCityWebDetailExport : IProcessSceneWithReport
    {
        public int callbackOrder => int.MinValue + 3;
        private static Scope active;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || !BuildPipeline.isBuildingPlayer || report.summary.platform != BuildTarget.WebGL) return;
            if (active == null) throw new BuildFailedException("Browser detail export requires its reversible build scope.");
            SlopCityDetailStreaming owner = Extract(scene, active.Staging);
            active.Record(owner);
        }

        /// <summary>No AssetDatabase imports and no source-scene writes. Commit copies only verified successful output.</summary>
        public sealed class Scope : IDisposable
        {
            internal readonly string Staging;
            private readonly string output;
            private readonly Dictionary<string, SlopCityDetailStreaming.DetailEntry> files = new Dictionary<string, SlopCityDetailStreaming.DetailEntry>();
            private bool extracted, completed;
            public Scope(string outputPath)
            {
                if (active != null) throw new InvalidOperationException("Only one detail export scope may be active.");
                output = Path.GetFullPath(outputPath);
                Staging = output + ".detail-stage-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(Staging);
                active = this;
            }
            internal void Record(SlopCityDetailStreaming owner)
            {
                extracted = true;
                foreach (SlopCityDetailStreaming.DetailEntry entry in owner.Entries) files[entry.fileName] = entry;
            }
            public void Complete()
            {
                if (!extracted || completed) throw new BuildFailedException("Detail export did not run freshly; use a clean Web build.");
                string destination = Path.Combine(output, "StreamingAssets", "Geometry");
                Directory.CreateDirectory(destination);
                foreach (var file in files)
                {
                    string source = Path.Combine(Staging, "StreamingAssets", "Geometry", file.Key);
                    byte[] bytes = File.ReadAllBytes(source);
                    if (bytes.Length != file.Value.payloadBytes || SlopCityDetailMeshCodec.ComputeHash(bytes) != file.Value.sha256)
                        throw new BuildFailedException("Detail staging payload changed after scene export.");
                    File.WriteAllBytes(Path.Combine(destination, file.Key), bytes);
                }
                // Old generated detail files are never allowed to leak into a replacement package.
                foreach (string file in Directory.GetFiles(destination))
                    if (!files.ContainsKey(Path.GetFileName(file)))
                    {
                        string name = Path.GetFileName(file);
                        const string prefix = "detail-", suffix = ".mesh.unityweb";
                        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)
                            || !SlopCityDetailMeshCodec.ValidHash(name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length)))
                            throw new BuildFailedException("Unexpected detail resource in the output: " + name);
                        File.Delete(file); // Only our proven generated content-addressed sidecars in this build output.
                    }
                completed = true;
            }
            public void Dispose()
            {
                if (active == this) active = null;
                if (Directory.Exists(Staging)) Directory.Delete(Staging, true);
            }
        }

        public static SlopCityDetailStreaming Extract(Scene scene, string directory)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                SlopCityDetailStreaming existing = root.GetComponent<SlopCityDetailStreaming>();
                if (existing) return existing;
            }
            var references = new HashSet<Object>();
            var required = new HashSet<Mesh>();
            var filters = new List<MeshFilter>();
            var usages = new Dictionary<Mesh, int>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true)) if (collider.sharedMesh) required.Add(collider.sharedMesh);
                foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (skin.sharedMesh) required.Add(skin.sharedMesh);
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!filter.sharedMesh) continue;
                    filters.Add(filter); usages.TryGetValue(filter.sharedMesh, out int count); usages[filter.sharedMesh] = count + 1;
                }
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (!component || component is Transform || component is MeshFilter || component is MeshRenderer || component is SlopCityBrowserGeometry) continue;
                    using (var serialized = new SerializedObject(component))
                    {
                        SerializedProperty property = serialized.GetIterator();
                        while (property.Next(true))
                            if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue)
                            {
                                Object value = property.objectReferenceValue;
                                // This is the one known serialized mesh owner; it explicitly relinquishes a frame below.
                                if (component is SlopCityMetricFacade && value is Mesh && property.propertyPath.StartsWith("ownedMeshes.Array.data[", StringComparison.Ordinal)) continue;
                                references.Add(value);
                            }
                    }
                }
            }
            var entries = new List<SlopCityDetailStreaming.DetailEntry>();
            var sources = new List<Mesh>();
            var proxies = new List<Mesh>();
            string destination = Path.Combine(Path.GetFullPath(directory), "StreamingAssets", "Geometry");
            Directory.CreateDirectory(destination);
            bool frameFound = false, acFound = false, signFound = false, billboardFound = false;
            // Stage and validate every payload/proxy first. A partial extraction can never become a successful build.
            try
            {
                foreach (MeshFilter filter in filters)
                {
                    if (!IsCandidateName(filter.name)) continue;
                    if (SlopCitySalvagePreparation.IsMutable(filter.GetComponent<Renderer>())) continue;
                    if (!CanStream(filter, references, required) || usages[filter.sharedMesh] != 1)
                        throw new BuildFailedException("Detail candidate has unsupported ownership or rendering: " + Hierarchy(filter.transform));
                    Mesh source = filter.sharedMesh;
                    MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                    byte[] payload = SlopCityDetailMeshCodec.Encode(source);
                    string hash = SlopCityDetailMeshCodec.ComputeHash(payload), filename = "detail-" + hash + ".mesh.unityweb";
                    Mesh proxy = null;
                    if (filter.name == "Metric_Reveals_Frames_And_Cornices")
                    {
                        Transform tower = filter.transform.parent.parent;
                        MeshFilter core = tower.Find("Structural_Core")?.GetComponent<MeshFilter>();
                        MeshFilter upper = tower.Find("Setback_UpperShaft")?.GetComponent<MeshFilter>();
                        if (!core || !core.sharedMesh) throw new BuildFailedException("Frame candidate lacks its structural dimensions.");
                        float roof = core.sharedMesh.bounds.size.y + (upper && upper.sharedMesh ? upper.sharedMesh.bounds.size.y : 0f);
                        proxy = CreateRooflineProxy(source, roof);
                        if (!proxy || proxy.vertexCount == 0) throw new BuildFailedException("Frame roofline proxy would be empty.");
                        proxies.Add(proxy); frameFound = true;
                    }
                    acFound |= filter.name.StartsWith("Manufactured_Web_AC_", StringComparison.Ordinal);
                    signFound |= filter.name.StartsWith("Manufactured_Web_Passive_Sign_", StringComparison.Ordinal);
                    billboardFound |= filter.name.StartsWith("Manufactured_Web_Passive_Billboard_", StringComparison.Ordinal);
                    int geometry = GeometryBytes(source);
                    if (geometry > SlopCityDetailMeshCodec.MaximumPayloadBytes || geometry <= 0) throw new BuildFailedException("Detail geometry exceeds its per-request bound.");
                    entries.Add(new SlopCityDetailStreaming.DetailEntry { target = filter, renderer = renderer, farProxy = proxy,
                        localBounds = source.bounds, fileName = filename, sha256 = hash, payloadBytes = payload.Length, geometryBytes = geometry });
                    sources.Add(source);
                    string path = Path.Combine(destination, filename);
                    if (File.Exists(path) && SlopCityDetailMeshCodec.ComputeHash(File.ReadAllBytes(path)) != hash)
                        throw new BuildFailedException("A detail content-addressed file has conflicting bytes.");
                    File.WriteAllBytes(path, payload);
                }
                if (!frameFound || !acFound || !signFound || !billboardFound)
                    throw new BuildFailedException("Detail export is missing one or more required prepared art families.");
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    SlopCityMetricFacade facade = entry.target.GetComponentInParent<SlopCityMetricFacade>();
                    if (facade && entry.target.name == "Metric_Reveals_Frames_And_Cornices") facade.DetachStreamedFrame(sources[i]);
                    entry.target.sharedMesh = entry.farProxy;
                    GameObjectUtility.SetStaticEditorFlags(entry.target.gameObject,
                        GameObjectUtility.GetStaticEditorFlags(entry.target.gameObject) & ~StaticEditorFlags.BatchingStatic);
                }
                GameObject metadata = new GameObject("Browser_Immutable_Detail_Streaming");
                SceneManager.MoveGameObjectToScene(metadata, scene);
                var owner = metadata.AddComponent<SlopCityDetailStreaming>(); owner.SetBuildEntries(entries.ToArray());
                long gross = 0, fallback = 0;
                foreach (var entry in entries) { gross += entry.geometryBytes; if (entry.farProxy) fallback += GeometryBytes(entry.farProxy); }
                Debug.Log("SLOP_CITY_WEB_DETAIL_EXPORT bindings=" + entries.Count + " grossGeometryBytes=" + gross
                    + " permanentProxyBytes=" + fallback + " residentBudgetBytes=" + SlopCityDetailStreaming.ResidentBudgetBytes);
                return owner;
            }
            catch
            {
                foreach (Mesh proxy in proxies) if (proxy) Object.DestroyImmediate(proxy);
                throw;
            }
        }
        public static bool IsCandidateName(string name)
            => name == "Metric_Reveals_Frames_And_Cornices" || name.StartsWith("Manufactured_Web_AC_", StringComparison.Ordinal)
                || name.StartsWith("Manufactured_Web_Passive_Sign_", StringComparison.Ordinal)
                || name.StartsWith("Manufactured_Web_Passive_Billboard_", StringComparison.Ordinal);
        public static bool CanStream(MeshFilter filter, HashSet<Object> referenced, HashSet<Mesh> requiredMeshes)
        {
            if (!filter || !filter.sharedMesh || referenced == null || requiredMeshes == null || !IsCandidateName(filter.name)) return false;
            if (SlopCitySalvagePreparation.IsMutable(filter.GetComponent<Renderer>())) return false;
            Mesh mesh = filter.sharedMesh; MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (!renderer || !renderer.enabled || !filter.gameObject.activeInHierarchy || renderer.forceRenderingOff || renderer.isPartOfStaticBatch
                || renderer.additionalVertexStreams || renderer.HasPropertyBlock() || mesh.vertexCount == 0 || mesh.blendShapeCount != 0
                || mesh.bindposes.Length != 0 || requiredMeshes.Contains(mesh) || referenced.Contains(mesh) || referenced.Contains(filter) || referenced.Contains(renderer)
                || renderer.lightProbeProxyVolumeOverride || HasLightmap(renderer.lightmapIndex) || HasLightmap(renderer.realtimeLightmapIndex)
                || filter.GetComponents<Component>().Length != 3 || filter.transform.childCount != 0) return false;
            if (renderer.sharedMaterials.Length != mesh.subMeshCount) return false;
            foreach (Material material in renderer.sharedMaterials)
                if (!material || !material.shader || material.renderQueue >= 3000) return false;
            for (int sub = 0; sub < mesh.subMeshCount; sub++) if (mesh.GetTopology(sub) != MeshTopology.Triangles) return false;
            Transform parent = filter.transform.parent;
            if (!parent) return false;
            bool frame = filter.name == "Metric_Reveals_Frames_And_Cornices";
            if (frame)
            {
                SlopCityMetricFacade facade = filter.GetComponentInParent<SlopCityMetricFacade>();
                if (parent.name != "Metric_Facade" || !parent.parent || !facade || !facade.Prepared
                    || !(parent.parent.name.StartsWith("City_Skyscraper_", StringComparison.Ordinal) || parent.parent.name.StartsWith("Skyline_Tower_", StringComparison.Ordinal))) return false;
            }
            else if (filter.name.StartsWith("Manufactured_Web_AC_", StringComparison.Ordinal))
            { if (parent.name != "Japan_SplitSystem_AC") return false; }
            else if (filter.name.StartsWith("Manufactured_Web_Passive_Sign_", StringComparison.Ordinal))
            { if (!parent.name.StartsWith("Japan_Sign_", StringComparison.Ordinal)) return false; }
            else if (!parent.name.StartsWith("Japan_VideoBillboard_", StringComparison.Ordinal)) return false;
            bool japan = false;
            for (Transform item = filter.transform; item; item = item.parent)
            {
                if (item.GetComponent<SlopCityDoor>() || item.GetComponent<SlopCityElevator>() || item.GetComponent<Rigidbody>()
                    || item.GetComponent<Animator>() || item.GetComponent<Animation>()) return false;
                SlopCityInteractable action = item.GetComponent<SlopCityInteractable>();
                if (action && !(item == parent && parent.name.StartsWith("Japan_Sign_", StringComparison.Ordinal) && action.Kind == SlopCityInteractionKind.Inspect)) return false;
                japan |= item.name == "City_JapanMetropolis";
                if (item.name == "SlopCity_NineBlocks") return frame || japan;
            }
            return false;
        }
        private static bool HasLightmap(int index) => index >= 0 && index != 65534 && index != 65535;
        private static string Hierarchy(Transform item) => item.parent ? Hierarchy(item.parent) + "/" + item.name : item.name;
        public static int GeometryBytes(Mesh mesh)
        {
            long total = 0;
            using (Mesh.MeshDataArray data = MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                for (int stream = 0; stream < mesh.vertexBufferCount; stream++) total += data[0].GetVertexData<byte>(stream).Length;
                total += data[0].GetIndexData<byte>().Length;
            }
            return checked((int)total);
        }
        private static Mesh CreateRooflineProxy(Mesh source, float roof)
        {
            if (!source.HasVertexAttribute(VertexAttribute.Position) || source.GetVertexAttributeFormat(VertexAttribute.Position) != VertexAttributeFormat.Float32
                || source.GetVertexAttributeDimension(VertexAttribute.Position) != 3) throw new BuildFailedException("Frame position layout is unsupported.");
            using (Mesh.MeshDataArray data = MeshUtility.AcquireReadOnlyMeshData(source))
            {
                int streams = source.vertexBufferCount, positionStream = source.GetVertexAttributeStream(VertexAttribute.Position);
                var raw = new byte[streams][]; var strides = new int[streams];
                for (int stream = 0; stream < streams; stream++) { raw[stream] = data[0].GetVertexData<byte>(stream).ToArray(); strides[stream] = source.GetVertexBufferStride(stream); }
                byte[] indices = data[0].GetIndexData<byte>().ToArray(); int indexSize = source.indexFormat == IndexFormat.UInt16 ? 2 : 4;
                int positionOffset = source.GetVertexAttributeOffset(VertexAttribute.Position);
                var originals = new List<int>(); var remap = new Dictionary<int, int>(); var submeshes = new List<int[]>();
                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    var selected = new List<int>(); SubMeshDescriptor descriptor = source.GetSubMesh(sub);
                    for (int index = descriptor.indexStart; index < descriptor.indexStart + descriptor.indexCount; index += 3)
                    {
                        int a = ReadIndex(indices, index, indexSize) + descriptor.baseVertex;
                        int b = ReadIndex(indices, index + 1, indexSize) + descriptor.baseVertex;
                        int c = ReadIndex(indices, index + 2, indexSize) + descriptor.baseVertex;
                        if (BitConverter.ToSingle(raw[positionStream], a * strides[positionStream] + positionOffset + 4) < roof + .015f
                            || BitConverter.ToSingle(raw[positionStream], b * strides[positionStream] + positionOffset + 4) < roof + .015f
                            || BitConverter.ToSingle(raw[positionStream], c * strides[positionStream] + positionOffset + 4) < roof + .015f) continue;
                        foreach (int vertex in new[] { a, b, c })
                        {
                            if (!remap.TryGetValue(vertex, out int replacement)) { replacement = originals.Count; originals.Add(vertex); remap.Add(vertex, replacement); }
                            selected.Add(replacement);
                        }
                    }
                    submeshes.Add(selected.ToArray());
                }
                if (originals.Count == 0) return null;
                var mesh = new Mesh { name = "Permanent_Exact_Metric_Roofline", indexFormat = source.indexFormat };
                try
                {
                    mesh.SetVertexBufferParams(originals.Count, source.GetVertexAttributes());
                    for (int stream = 0; stream < streams; stream++)
                    {
                        var bytes = new byte[originals.Count * strides[stream]];
                        for (int vertex = 0; vertex < originals.Count; vertex++) Buffer.BlockCopy(raw[stream], originals[vertex] * strides[stream], bytes, vertex * strides[stream], strides[stream]);
                        mesh.SetVertexBufferData(bytes, 0, 0, bytes.Length, stream, MeshUpdateFlags.DontRecalculateBounds);
                    }
                    mesh.subMeshCount = submeshes.Count;
                    for (int sub = 0; sub < submeshes.Count; sub++) mesh.SetIndices(submeshes[sub], MeshTopology.Triangles, sub, false);
                    mesh.RecalculateBounds(); mesh.UploadMeshData(true);
                    return mesh;
                }
                catch { Object.DestroyImmediate(mesh); throw; }
            }
        }
        private static int ReadIndex(byte[] bytes, int index, int size)
            => size == 2 ? BitConverter.ToUInt16(bytes, index * size) : checked((int)BitConverter.ToUInt32(bytes, index * size));
    }
}
#endif
