#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SlopCity.Editor
{
    /// <summary>Temporary Web build scene only: preserve geometry while avoiding repeated static copies.</summary>
    public static class SlopCityWebMeshBudget
    {
        public const int SmallRepeatedBatchMaximumVertices = 256;
        public const long SmallRepeatedBatchAdditionalGeometryBudget = 64L * 1024 * 1024;
        public static void Prepare(Scene scene, bool batchUniqueImmutable = false)
        {
            var protectedRenderers = CollectPlaybackRenderers(scene);
            var protectedGeometry = CollectRequiredGeometry(scene);
            RetireReplacedFacadeReferences(scene, protectedRenderers, protectedGeometry);
            RetireReplacedLoungeReferences(scene, protectedRenderers, protectedGeometry);
            var filters = new Dictionary<Mesh, List<MeshFilter>>();
            var physics = new HashSet<Mesh>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
                    if (collider.sharedMesh) physics.Add(collider.sharedMesh);
                foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (skin.sharedMesh) physics.Add(skin.sharedMesh);
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    if (!mesh) continue;
                    if (!filters.TryGetValue(mesh, out List<MeshFilter> list)) filters[mesh] = list = new List<MeshFilter>();
                    list.Add(filter);
                }
            }
            long avoided = 0, welded = 0, packed = 0, releasedCpu = 0;
            int instanced = 0, compacted = 0, packedMeshes = 0, finalizedMeshes = 0;
            var finalizedRenderMeshes = new HashSet<Mesh>();
            var batchingSources = new HashSet<Mesh>();
            var fullyBatchedSources = new HashSet<Mesh>();
            int uniqueBatchRenderers = 0, detachedBatchSources = 0;
            int smallBatchRenderers = 0;
            long smallBatchGeometryBudgetUsed = 0;
            foreach (var entry in filters)
            {
                Mesh mesh = entry.Key;
                bool mutable = protectedGeometry.Contains(mesh);
                foreach (MeshFilter filter in entry.Value)
                    mutable |= SlopCitySalvagePreparation.IsMutable(filter.GetComponent<Renderer>());
                if (physics.Contains(mesh) || mutable)
                {
                    if (batchUniqueImmutable)
                        foreach (MeshFilter filter in entry.Value) DisableStaticBatching(filter);
                    continue;
                }
                // Repeated immutable art, including small facade elements, can use
                // authored instancing shaders without per-instance static vertex copies.
                var groups = new Dictionary<Material, List<MeshFilter>>();
                foreach (MeshFilter filter in entry.Value)
                    if (EligibleForInstancing(filter, mesh))
                    {
                        Material shared = filter.GetComponent<MeshRenderer>().sharedMaterial;
                        if (!groups.TryGetValue(shared, out List<MeshFilter> list)) groups[shared] = list = new List<MeshFilter>();
                        list.Add(filter);
                    }
                bool reused = false;
                var instancingAdmitted = new HashSet<MeshFilter>();
                if (mesh.vertexCount > 0)
                {
                    foreach (List<MeshFilter> eligible in groups.Values)
                    {
                        if (eligible.Count < 4) continue;
                        foreach (MeshFilter filter in eligible)
                        {
                            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(filter.gameObject);
                            // Small-copy admission below must still see the original
                            // static flag; all larger repeated art remains instanced.
                            if (!batchUniqueImmutable || mesh.vertexCount > SmallRepeatedBatchMaximumVertices)
                                GameObjectUtility.SetStaticEditorFlags(filter.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
                            instancingAdmitted.Add(filter);
                        }
                        avoided += (long)eligible.Count * VertexBytes(mesh);
                        instanced += eligible.Count;
                        reused = true;
                    }
                }
                bool immutable = true;
                foreach (MeshFilter filter in entry.Value)
                    if (!filter.name.StartsWith("Manufactured_", StringComparison.Ordinal)
                        && filter.name != "Metric_Reveals_Frames_And_Cornices"
                        && !IsPreparedMetricSurface(filter)) { immutable = false; break; }
                bool uniqueBatch = batchUniqueImmutable && immutable && entry.Value.Count == 1
                    && EligibleForUniqueBatching(entry.Value[0], mesh);
                var smallBatch = new HashSet<MeshFilter>();
                if (batchUniqueImmutable && entry.Value.Count > 1 && mesh.vertexCount > 0
                    && mesh.vertexCount <= SmallRepeatedBatchMaximumVertices)
                {
                    foreach (MeshFilter filter in entry.Value)
                        if (EligibleForStaticBatching(filter, mesh)) smallBatch.Add(filter);
                    if (smallBatch.Count < 2) smallBatch.Clear();
                    long geometry = VertexBytes(mesh);
                    for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                        geometry += (long)mesh.GetIndexCount(submesh) * (mesh.indexFormat == IndexFormat.UInt16 ? 2 : 4);
                    // Charge every combined copy, even if the original later becomes
                    // unreachable. This deliberately overestimates added geometry.
                    if (!TryReserveSmallBatchGeometry(geometry, smallBatch.Count, ref smallBatchGeometryBudgetUsed)) smallBatch.Clear();
                }
                bool pendingBatch = uniqueBatch || smallBatch.Count > 0;
                if (batchUniqueImmutable)
                    foreach (MeshFilter filter in entry.Value)
                        if (!uniqueBatch && !smallBatch.Contains(filter)) DisableStaticBatching(filter);
                if (smallBatch.Count > 0) smallBatchRenderers += smallBatch.Count;
                foreach (MeshFilter filter in smallBatch)
                    if (instancingAdmitted.Contains(filter)) { instanced--; avoided -= VertexBytes(mesh); }
                int remainingInstanced = instancingAdmitted.Count;
                foreach (MeshFilter filter in smallBatch) if (instancingAdmitted.Contains(filter)) remainingInstanced--;
                if (reused && remainingInstanced > 0) avoided -= VertexBytes(mesh); // One shared render mesh remains.
                if (!immutable || !mesh.isReadable)
                {
                    if (pendingBatch)
                    {
                        batchingSources.Add(mesh);
                        if (uniqueBatch || smallBatch.Count == entry.Value.Count) fullyBatchedSources.Add(mesh);
                    }
                    continue;
                }
                // Asset-backed meshes must be copied; generated scene meshes retain
                // their ownership identity so facade lists cannot serialize old copies.
                Mesh candidate = EditorUtility.IsPersistent(mesh) ? UnityEngine.Object.Instantiate(mesh) : mesh;
                try
                {
                    int before = candidate.vertexCount;
                    int strideBefore = VertexStride(candidate);
                    int removed = WeldExact(candidate);
                    long packedBytes = PackNormalsTangents(candidate);
                    if (candidate != mesh)
                        foreach (MeshFilter filter in entry.Value) filter.sharedMesh = candidate;
                    welded += (long)(before - candidate.vertexCount) * strideBefore;
                    if (removed > 0) compacted++;
                    packed += packedBytes;
                    if (packedBytes > 0) packedMeshes++;
                    // These named manufactured/frame meshes are immutable render art;
                    // runtime hollowing consumes separate CurtainWall/Masonry sources.
                    // Finalize unbatched immutable art below; selected static sources
                    // retain their streams until Unity combines them. Keep collider,
                    // skinned and runtime inputs readable.
                    long cpuBytes = VertexBytes(candidate);
                    for (int submesh = 0; submesh < candidate.subMeshCount; submesh++)
                        cpuBytes += (long)candidate.GetIndexCount(submesh) * (candidate.indexFormat == IndexFormat.UInt16 ? 2 : 4);
                    if (pendingBatch)
                    {
                        // Unique sources avoid instance multiplication; admitted small
                        // copies are charged to the explicit geometry budget above.
                        // Unity's scene combiner still needs their CPU streams here.
                        batchingSources.Add(candidate);
                        if (uniqueBatch || smallBatch.Count == entry.Value.Count) fullyBatchedSources.Add(candidate);
                        if (uniqueBatch) uniqueBatchRenderers++;
                    }
                    else
                    {
                        candidate.UploadMeshData(true);
                        finalizedRenderMeshes.Add(candidate);
                        releasedCpu += cpuBytes;
                        finalizedMeshes++;
                    }
                }
                catch
                {
                    if (candidate != mesh) UnityEngine.Object.DestroyImmediate(candidate);
                    throw; // A partially changed build must never be published.
                }
            }
            Debug.Log("SLOP_CITY_WEB_MESH_BUDGET instancedRenderers=" + instanced
                + " avoidedStaticVertexBytes=" + avoided + " weldedMeshes=" + compacted + " exactWeldVertexBytes=" + welded
                + " packedMeshes=" + packedMeshes + " normalTangentPackedBytes=" + packed
                + " finalizedRenderMeshes=" + finalizedMeshes + " releasedCpuBytes=" + releasedCpu);
            // Facades retain ownership lists in addition to their MeshFilters. Only
            // finalize live render-only owned art, leaving structural hollowing and
            // physics meshes intact. Prepared metric facades never change vertices.
            var renderOnly = new HashSet<Mesh>(finalizedRenderMeshes);
            foreach (var entry in filters)
                if (!physics.Contains(entry.Key))
                    foreach (MeshFilter filter in entry.Value)
                        if (filter.sharedMesh && !batchingSources.Contains(filter.sharedMesh)) renderOnly.Add(filter.sharedMesh);
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (SlopCityMetricFacade facade in root.GetComponentsInChildren<SlopCityMetricFacade>(true))
                {
                    facade.PreparePlayerMeshData(fullyBatchedSources, renderOnly, out _, out int detached);
                    detachedBatchSources += detached;
                }
            ReleaseCrowdCpuData(scene, filters, physics);
            Debug.Log("SLOP_CITY_WEB_UNIQUE_STATIC_BATCHING enabled=" + batchUniqueImmutable
                + " renderers=" + uniqueBatchRenderers + " detachedFacadeSources=" + detachedBatchSources
                + " smallRepeatedRenderers=" + smallBatchRenderers + " smallRepeatedGeometryBudgetUsed=" + smallBatchGeometryBudgetUsed
                + " smallRepeatedGeometryBudgetMaximum=" + SmallRepeatedBatchAdditionalGeometryBudget);
        }

        private static void DisableStaticBatching(MeshFilter filter)
        {
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(filter.gameObject);
            GameObjectUtility.SetStaticEditorFlags(filter.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
        }

        internal static bool TryReserveSmallBatchGeometry(long geometryBytes, int copies, ref long used)
        {
            if (geometryBytes < 0 || copies < 0 || used < 0 || used > SmallRepeatedBatchAdditionalGeometryBudget) return false;
            long remaining = SmallRepeatedBatchAdditionalGeometryBudget - used;
            if (copies != 0 && geometryBytes > remaining / copies) return false;
            used += geometryBytes * copies;
            return true;
        }

        private static bool EligibleForUniqueBatching(MeshFilter filter, Mesh mesh)
        {
            return mesh.isReadable && EligibleForStaticBatching(filter, mesh);
        }

        private static bool EligibleForStaticBatching(MeshFilter filter, Mesh mesh)
        {
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (!renderer || !renderer.enabled || !filter.gameObject.activeInHierarchy
                || mesh.vertexCount == 0 || mesh.vertexCount > 65535
                || (GameObjectUtility.GetStaticEditorFlags(filter.gameObject) & StaticEditorFlags.BatchingStatic) == 0) return false;
            foreach (Material material in renderer.sharedMaterials)
                if (!material || !material.shader || !material.shader.isSupported
                    || material.GetTag("DisableBatching", false, "False") == "True") return false;
            for (Transform parent = filter.transform; parent; parent = parent.parent)
            {
                SlopCityInteractable item = parent.GetComponent<SlopCityInteractable>();
                if (parent.name == "Parked_CityCar" || parent.GetComponent<SlopCityDoor>() || parent.GetComponent<Rigidbody>()
                    || parent.GetComponent<Animator>() || parent.GetComponent<Animation>()
                    || (item && item.Kind == SlopCityInteractionKind.Car)) return false;
            }
            return true;
        }

        private static bool IsPreparedMetricSurface(MeshFilter filter)
        {
            if (filter.name != "Floor_Aligned_Stonework" && filter.name != "Human_Scale_Window_Panes") return false;
            SlopCityMetricFacade facade = filter.GetComponentInParent<SlopCityMetricFacade>();
            // These replacements are immutable after the build's metric preparation.
            // Entrance trimming reads the separate original CurtainWall/Masonry mesh;
            // the building installer consumes only these replacements' bounds.
            return facade && facade.Prepared;
        }

        private static HashSet<Renderer> CollectPlaybackRenderers(Scene scene)
        {
            var result = new HashSet<Renderer>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (SlopCityCityScreen screen in root.GetComponentsInChildren<SlopCityCityScreen>(true))
                    if (screen.Display) result.Add(screen.Display);
                foreach (SlopCityMediaPlayer player in root.GetComponentsInChildren<SlopCityMediaPlayer>(true))
                {
                    var serialized = new SerializedObject(player);
                    SerializedProperty display = serialized.FindProperty("screen");
                    if (display != null && display.objectReferenceValue is Renderer renderer) result.Add(renderer);
                    SerializedProperty lettering = serialized.FindProperty("screenLettering");
                    if (lettering == null || !lettering.isArray) continue;
                    for (int index = 0; index < lettering.arraySize; index++)
                        if (lettering.GetArrayElementAtIndex(index).objectReferenceValue is Renderer letter) result.Add(letter);
                }
            }
            return result;
        }

        private static HashSet<Mesh> CollectRequiredGeometry(Scene scene)
        {
            var result = new HashSet<Mesh>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
                    if (collider.sharedMesh) result.Add(collider.sharedMesh);
                foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (skin.sharedMesh) result.Add(skin.sharedMesh);
                foreach (SlopCitySalvageSurface surface in root.GetComponentsInChildren<SlopCitySalvageSurface>(true))
                    if (surface.SourceMesh) result.Add(surface.SourceMesh);
            }
            return result;
        }

        private static bool ClearRetiredRenderReferences(MeshFilter filter, MeshRenderer renderer,
            HashSet<Renderer> protectedRenderers, HashSet<Mesh> protectedGeometry)
        {
            if (!filter || !filter.sharedMesh || !renderer || protectedRenderers.Contains(renderer)
                || protectedGeometry.Contains(filter.sharedMesh)) return false;
            // Keep renderer/object identities, transforms, colliders and captured
            // browser hit bounds. Another visible user may still own this mesh.
            filter.sharedMesh = null;
            renderer.sharedMaterials = Array.Empty<Material>();
            return true;
        }

        private static void RetireReplacedLoungeReferences(Scene scene, HashSet<Renderer> protectedRenderers,
            HashSet<Mesh> protectedGeometry)
        {
            Transform lounge = null, city = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "SlopCity_Lounge") lounge = root.transform;
                else if (root.name == "SlopCity_NineBlocks") city = root.transform;
            }
            if (!lounge) return;
            Transform habitat = lounge.Find("SlopCity_HabitatInterior");
            bool habitatReady = false;
            if (habitat)
                foreach (MeshRenderer renderer in habitat.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.enabled && renderer.gameObject.activeInHierarchy
                        && renderer.name.StartsWith("Habitat_Combined_", StringComparison.Ordinal)
                        && renderer.TryGetComponent<MeshFilter>(out MeshFilter filter) && filter.sharedMesh)
                    { habitatReady = true; break; }
            SlopCityHumanScale scale = city ? city.GetComponent<SlopCityHumanScale>() : null;
            bool cityReady = city && scale && scale.Prepared && city.GetComponentsInChildren<SlopCityDistrict>(true).Length == 9;
            int retired = 0;
            foreach (MeshRenderer renderer in lounge.GetComponentsInChildren<MeshRenderer>(true))
            {
                string name = renderer.name;
                // These are the exact replacement boundaries in HabitatBuilder and
                // CityBuilder.HideOldBackdrop. Actual TV playback lettering belongs
                // to a different explicit list and is also protected by references.
                bool replacedHabitat = habitatReady && !renderer.enabled
                    && (name.StartsWith("HRR_Poster", StringComparison.Ordinal) || name.StartsWith("Poster_Title", StringComparison.Ordinal)
                        || name.StartsWith("Poster_Subtitle", StringComparison.Ordinal) || name.StartsWith("Wall_Record_", StringComparison.Ordinal));
                bool replacedBackdrop = cityReady && !renderer.gameObject.activeSelf
                    && (name.StartsWith("City_", StringComparison.Ordinal) || name.StartsWith("Roof_Unit", StringComparison.Ordinal)
                        || name.StartsWith("Transmission_Mast", StringComparison.Ordinal) || name.StartsWith("Antenna_", StringComparison.Ordinal));
                if ((!replacedHabitat && !replacedBackdrop) || name == "TV_Broadcaster" || name == "TV_Tagline" || name == "TV_Transmission") continue;
                if (ClearRetiredRenderReferences(renderer.GetComponent<MeshFilter>(), renderer, protectedRenderers, protectedGeometry)) retired++;
            }
            Debug.Log("SLOP_CITY_WEB_RETIRED_LOUNGE_REFERENCES renderers=" + retired
                + " habitatReady=" + habitatReady + " cityReady=" + cityReady);
        }

        private static void RetireReplacedFacadeReferences(Scene scene, HashSet<Renderer> protectedRenderers,
            HashSet<Mesh> protectedGeometry)
        {
            int retired = 0;
            var sources = new HashSet<Mesh>();
            long bytes = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (SlopCityMetricFacade facade in root.GetComponentsInChildren<SlopCityMetricFacade>(true))
                {
                    if (!facade.Prepared) continue;
                    foreach (Transform piece in facade.transform)
                    {
                        string name = piece.name;
                        bool replacedCityWindow = facade.name.StartsWith("City_Skyscraper_", StringComparison.Ordinal)
                            && (name == "CurtainWall_Panel" || name == "Masonry_WindowBays");
                        if (!name.StartsWith("Facade_Frames_", StringComparison.Ordinal)
                            && !name.StartsWith("Podium_Joints_", StringComparison.Ordinal)
                            && name != "Podium_Cornice" && name != "Recessed_Floor_Band" && !replacedCityWindow) continue;
                        MeshFilter filter = piece.GetComponent<MeshFilter>();
                        MeshRenderer renderer = piece.GetComponent<MeshRenderer>();
                        if (!filter || !filter.sharedMesh || !renderer || renderer.enabled) continue;
                        if (protectedRenderers.Contains(renderer) || protectedGeometry.Contains(filter.sharedMesh)) continue;
                        Mesh mesh = filter.sharedMesh;
                        if (sources.Add(mesh) && mesh.isReadable)
                        {
                            bytes += VertexBytes(mesh);
                            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                                bytes += (long)mesh.GetIndexCount(submesh) * (mesh.indexFormat == IndexFormat.UInt16 ? 2 : 4);
                        }
                        // MetricFacade.Build has already replaced this art and disabled
                        // it. Runtime shell installation only disables it again; it never
                        // reads these rows. Keep the objects/names for that traversal,
                        // but stop serializing their retired vertex/material dependencies.
                        // Skyline CurtainWall/Masonry remains an entrance-trim input.
                        // Structural bounds, roof hardware and playback lettering are
                        // deliberately outside this allowlist.
                        if (ClearRetiredRenderReferences(filter, renderer, protectedRenderers, protectedGeometry)) retired++;
                    }
                }
            Debug.Log("SLOP_CITY_WEB_RETIRED_FACADE_REFERENCES renderers=" + retired
                + " distinctMeshes=" + sources.Count + " sourceGeometryBytes=" + bytes);
        }

        private static void ReleaseCrowdCpuData(Scene scene, Dictionary<Mesh, List<MeshFilter>> filters, HashSet<Mesh> requiredReadable)
        {
            // Crowd animation changes instance matrices, never mesh vertices. These
            // parts are not skinned and have no physics geometry. Avoid retaining a
            // second CPU vertex/index copy in the browser for its whole lifetime.
            var finalized = new Dictionary<Mesh, Mesh>();
            long released = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (SlopCityCrowd crowd in root.GetComponentsInChildren<SlopCityCrowd>(true))
                {
                    Mesh[] parts = crowd.Visuals?.parts;
                    if (parts == null) continue;
                    for (int part = 0; part < parts.Length; part++)
                    {
                        Mesh source = parts[part];
                        if (!source || !source.isReadable || filters.ContainsKey(source) || requiredReadable.Contains(source)) continue;
                        if (finalized.TryGetValue(source, out Mesh previous)) { parts[part] = previous; continue; }
                        Mesh candidate = EditorUtility.IsPersistent(source) ? UnityEngine.Object.Instantiate(source) : source;
                        try
                        {
                            candidate.name = source.name;
                            long bytes = VertexBytes(candidate);
                            for (int submesh = 0; submesh < candidate.subMeshCount; submesh++)
                                bytes += (long)candidate.GetIndexCount(submesh) * (candidate.indexFormat == IndexFormat.UInt16 ? 2 : 4);
                            candidate.UploadMeshData(true);
                            parts[part] = candidate;
                            finalized.Add(source, candidate);
                            released += bytes;
                        }
                        catch
                        {
                            if (candidate != source) UnityEngine.Object.DestroyImmediate(candidate);
                            throw;
                        }
                    }
                }
            Debug.Log("SLOP_CITY_WEB_CROWD_CPU_DATA_FINALIZED meshes=" + finalized.Count + " releasedCpuBytes=" + released);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct PackedVertex
        {
            public Vector3 position;
            public ushort nx, ny, nz, nw, tx, ty, tz, tw;
            public Vector2 uv;
        }

        /// <summary>Immutable art only: unit directions use half precision; positions and UVs stay float32.</summary>
        public static long PackNormalsTangents(Mesh mesh)
        {
            if (!mesh || !mesh.isReadable || mesh.blendShapeCount != 0 || mesh.GetVertexAttributes().Length != 4
                || !SystemInfo.SupportsVertexAttributeFormat(VertexAttributeFormat.Float16, 4)) return 0;
            VertexAttribute[] semantics = { VertexAttribute.Position, VertexAttribute.Normal, VertexAttribute.Tangent, VertexAttribute.TexCoord0 };
            int[] dimensions = { 3, 3, 4, 2 };
            for (int attribute = 0; attribute < semantics.Length; attribute++)
                if (!mesh.HasVertexAttribute(semantics[attribute]) || mesh.GetVertexAttributeFormat(semantics[attribute]) != VertexAttributeFormat.Float32
                    || mesh.GetVertexAttributeDimension(semantics[attribute]) != dimensions[attribute]) return 0;
            Vector3[] positions = mesh.vertices, normals = mesh.normals;
            Vector4[] tangents = mesh.tangents; Vector2[] uv = mesh.uv;
            int count = mesh.vertexCount;
            if (positions.Length != count || normals.Length != count || tangents.Length != count || uv.Length != count) return 0;
            var vertices = new PackedVertex[count];
            for (int vertex = 0; vertex < count; vertex++)
            {
                Vector3 normal = normals[vertex]; Vector4 tangent = tangents[vertex];
                if (!Direction(normal.x) || !Direction(normal.y) || !Direction(normal.z)
                    || !Direction(tangent.x) || !Direction(tangent.y) || !Direction(tangent.z) || !Direction(tangent.w)) return 0;
                vertices[vertex] = new PackedVertex { position = positions[vertex], uv = uv[vertex],
                    nx = Mathf.FloatToHalf(normal.x), ny = Mathf.FloatToHalf(normal.y), nz = Mathf.FloatToHalf(normal.z),
                    tx = Mathf.FloatToHalf(tangent.x), ty = Mathf.FloatToHalf(tangent.y), tz = Mathf.FloatToHalf(tangent.z), tw = Mathf.FloatToHalf(tangent.w) };
            }
            int oldStride = VertexStride(mesh);
            var indices = new int[mesh.subMeshCount][];
            var topology = new MeshTopology[mesh.subMeshCount];
            Bounds bounds = mesh.bounds;
            for (int submesh = 0; submesh < indices.Length; submesh++)
            { indices[submesh] = mesh.GetIndices(submesh, true); topology[submesh] = mesh.GetTopology(submesh); }
            mesh.SetVertexBufferParams(count,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float16, 4),
                new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float16, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
            mesh.SetVertexBufferData(vertices, 0, 0, count, 0, MeshUpdateFlags.DontRecalculateBounds);
            mesh.subMeshCount = indices.Length;
            for (int submesh = 0; submesh < indices.Length; submesh++) mesh.SetIndices(indices[submesh], topology[submesh], submesh, false);
            mesh.bounds = bounds;
            return (long)count * (oldStride - VertexStride(mesh));
        }
        private static bool Direction(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) <= 1.0001f;

        private static bool EligibleForInstancing(MeshFilter filter, Mesh mesh)
        {
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (!renderer || !renderer.enabled || !filter.gameObject.activeInHierarchy || mesh.subMeshCount != 1
                || (GameObjectUtility.GetStaticEditorFlags(filter.gameObject) & StaticEditorFlags.BatchingStatic) == 0)
                return false;
            Material[] materials = renderer.sharedMaterials;
            return materials.Length == 1 && materials[0] && materials[0].enableInstancing
                && materials[0].shader && materials[0].shader.isSupported
                && materials[0].GetTag("DisableBatching", false, "False") != "True";
        }

        private static int VertexStride(Mesh mesh)
        {
            int bytes = 0;
            for (int stream = 0; stream < mesh.vertexBufferCount; stream++) bytes += mesh.GetVertexBufferStride(stream);
            return bytes;
        }
        private static long VertexBytes(Mesh mesh) => (long)mesh.vertexCount * VertexStride(mesh);

        /// <summary>Byte-identical vertices only. UV seams, hard normals, tangents and all streams remain distinct.</summary>
        public static int WeldExact(Mesh mesh)
        {
            if (!mesh || !mesh.isReadable || mesh.blendShapeCount != 0
                || mesh.HasVertexAttribute(VertexAttribute.BlendIndices) || mesh.HasVertexAttribute(VertexAttribute.BlendWeight)) return 0;
            int count = mesh.vertexCount;
            VertexAttributeDescriptor[] attributes = mesh.GetVertexAttributes();
            var streams = new byte[mesh.vertexBufferCount][];
            var strides = new int[streams.Length];
            using (Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(mesh))
                for (int stream = 0; stream < streams.Length; stream++)
                {
                    strides[stream] = mesh.GetVertexBufferStride(stream);
                    streams[stream] = data[0].GetVertexData<byte>(stream).ToArray();
                }
            var unique = new Dictionary<VertexKey, int>(count);
            var originals = new List<int>(count);
            var mapping = new int[count];
            for (int vertex = 0; vertex < count; vertex++)
            {
                var key = new VertexKey(streams, strides, vertex);
                if (!unique.TryGetValue(key, out int replacement))
                { replacement = originals.Count; unique.Add(key, replacement); originals.Add(vertex); }
                mapping[vertex] = replacement;
            }
            if (originals.Count == count) return 0;
            var indices = new int[mesh.subMeshCount][];
            var topology = new MeshTopology[mesh.subMeshCount];
            Bounds bounds = mesh.bounds;
            for (int submesh = 0; submesh < indices.Length; submesh++)
            {
                indices[submesh] = mesh.GetIndices(submesh, true);
                topology[submesh] = mesh.GetTopology(submesh);
                for (int index = 0; index < indices[submesh].Length; index++) indices[submesh][index] = mapping[indices[submesh][index]];
            }
            mesh.SetVertexBufferParams(originals.Count, attributes);
            for (int stream = 0; stream < streams.Length; stream++)
            {
                var buffer = new byte[originals.Count * strides[stream]];
                for (int vertex = 0; vertex < originals.Count; vertex++)
                    Buffer.BlockCopy(streams[stream], originals[vertex] * strides[stream], buffer, vertex * strides[stream], strides[stream]);
                mesh.SetVertexBufferData(buffer, 0, 0, buffer.Length, stream, MeshUpdateFlags.DontRecalculateBounds);
            }
            mesh.subMeshCount = indices.Length;
            for (int submesh = 0; submesh < indices.Length; submesh++)
                mesh.SetIndices(indices[submesh], topology[submesh], submesh, false);
            mesh.bounds = bounds;
            return count - originals.Count;
        }

        private readonly struct VertexKey : IEquatable<VertexKey>
        {
            private readonly byte[][] streams;
            private readonly int[] strides;
            private readonly int vertex;
            public VertexKey(byte[][] streams, int[] strides, int vertex)
            { this.streams = streams; this.strides = strides; this.vertex = vertex; }
            public bool Equals(VertexKey other)
            {
                for (int stream = 0; stream < streams.Length; stream++)
                    for (int offset = 0; offset < strides[stream]; offset++)
                        if (streams[stream][vertex * strides[stream] + offset]
                            != other.streams[stream][other.vertex * other.strides[stream] + offset]) return false;
                return true;
            }
            public override bool Equals(object other) => other is VertexKey key && Equals(key);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    for (int stream = 0; stream < streams.Length; stream++)
                        for (int offset = 0; offset < strides[stream]; offset++) hash = hash * 31 + streams[stream][vertex * strides[stream] + offset];
                    return hash;
                }
            }
        }
    }
}
#endif
