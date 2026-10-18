#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Exact passive authored assemblies merged only in temporary Web player scenes.</summary>
    public static class SlopCityWebDecorativeMerge
    {
        private static readonly Dictionary<string, int> ExpectedParts = new Dictionary<string, int>
        {
            { "Weathered_Compressor_Casing", 1 }, { "Fan_Dark_Recess", 1 }, { "Fan_Guard_Ring", 4 },
            { "Fan_Guard_Spoke", 3 }, { "Compressor_Side_Louvre", 7 }, { "Compressor_Mounting_Rail", 2 },
            { "Insulated_Refrigerant_Loop", 1 }
        };
        public sealed class Result
        {
            public int assemblies, sourceRenderers, mergedRenderers, skippedAssemblies;
            public int passiveAssemblies, passiveSourceRenderers, passiveMergedRenderers;
        }
        private sealed class Group
        {
            public MeshRenderer renderer;
            public readonly List<CombineInstance> pieces = new List<CombineInstance>();
        }

        // Caller owns the WebGL/player guard. Do not register a standalone scene callback:
        // preparation must precede browser bounds capture, packing and Unity static batching.
        public static Result Prepare(Scene scene)
        {
            var result = new Result();
            var references = new HashSet<Object>();
            var physics = new HashSet<Mesh>();
            var candidates = new List<Transform>();
            var passive = new List<Transform>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true)) if (collider.sharedMesh) physics.Add(collider.sharedMesh);
                foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (skin.sharedMesh) physics.Add(skin.sharedMesh);
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (!component || component is Transform || component is MeshRenderer || component is MeshFilter) continue;
                    using (var serialized = new SerializedObject(component))
                    {
                        SerializedProperty property = serialized.GetIterator();
                        while (property.Next(true))
                            if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue)
                                references.Add(property.objectReferenceValue);
                    }
                }
                foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                {
                    if (item.name == "Japan_SplitSystem_AC" && UnderJapanCity(item)) candidates.Add(item);
                    if (PassiveFamily(item) != null && PassiveAncestry(item)) passive.Add(item);
                }
            }
            foreach (Transform candidate in candidates)
            {
                if (MutableAssembly(candidate)) { result.skippedAssemblies++; continue; }
                if (!TryMerge(candidate, references, physics, out int outputs)) { result.skippedAssemblies++; continue; }
                result.assemblies++; result.sourceRenderers += 19; result.mergedRenderers += outputs;
            }
            foreach (Transform assembly in passive)
            {
                if (MutableAssembly(assembly)) continue;
                int sources, outputs;
                if (!TryMergePassive(assembly, references, physics, out sources, out outputs)) continue;
                result.passiveAssemblies++; result.passiveSourceRenderers += sources; result.passiveMergedRenderers += outputs;
                result.assemblies++; result.sourceRenderers += sources; result.mergedRenderers += outputs;
            }
            Debug.Log("SLOP_CITY_WEB_DECORATIVE_MERGE assemblies=" + result.assemblies + " sourceRenderers=" + result.sourceRenderers
                + " mergedRenderers=" + result.mergedRenderers + " skippedAssemblies=" + result.skippedAssemblies
                + " passiveAssemblies=" + result.passiveAssemblies + " passiveSources=" + result.passiveSourceRenderers
                + " passiveOutputs=" + result.passiveMergedRenderers);
            return result;
        }

        private static bool MutableAssembly(Transform root)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                if (SlopCitySalvagePreparation.IsMutable(renderer)) return true;
            return false;
        }

        private static string PassiveFamily(Transform item)
        {
            string name = item.name;
            if (name.StartsWith("Japan_Sign_", StringComparison.Ordinal)) return "Sign";
            if (name.StartsWith("Japan_VideoBillboard_", StringComparison.Ordinal)) return "Billboard";
            if (name.StartsWith("Megacity_Elevated_ServiceLink_", StringComparison.Ordinal)
                || name.StartsWith("Runtime_ExteriorBridge_Segment_Megacity_Elevated_ServiceLink_", StringComparison.Ordinal)) return "Bridge";
            if (name == "Subway_Station_0" || name == "Subway_Station_1") return "Station";
            if (name == "Subway_Train_0" || name == "Subway_Train_1") return "Train";
            if (name == "Subway_SeatBank_L" || name == "Subway_SeatBank_R") return "Seats";
            if (name.StartsWith("Platform_Luminaire_", StringComparison.Ordinal)) return "Luminaire";
            return null;
        }

        private static bool PassiveAncestry(Transform assembly)
        {
            string family = PassiveFamily(assembly);
            foreach (Component component in assembly.GetComponents<Component>())
                if (!(component is Transform) && !(component is SlopCityInteractable)) return false;
            bool japan = false, subway = false;
            for (Transform item = assembly; item; item = item.parent)
            {
                if (item.GetComponent<SlopCityDoor>() || item.GetComponent<SlopCityElevator>() || item.GetComponent<Rigidbody>()
                    || item.GetComponent<Animator>() || item.GetComponent<Animation>() || item.name == "Parked_CityCar"
                    || item.name == "Subway_SurfaceLift") return false;
                SlopCityInteractable action = item.GetComponent<SlopCityInteractable>();
                // The authored printed signs inspect in place; seat banks only rest
                // the player. Preserve their root component and fixed interaction point.
                if (action && !(item == assembly && ((family == "Sign" && action.Kind == SlopCityInteractionKind.Inspect)
                    || (family == "Seats" && action.Kind == SlopCityInteractionKind.Bench)))) return false;
                japan |= item.name == "City_JapanMetropolis";
                subway |= item.name == "SlopCity_Subway";
                if (item.name == "SlopCity_NineBlocks")
                    return (family == "Sign" || family == "Billboard") ? japan
                        : (family == "Bridge" || subway);
            }
            return false;
        }

        private static bool PassivePart(string family, string name)
        {
            if (family == "Sign") return name == "Kanban_Powdercoat_Housing" || name == "Galvanized_Mounting_Bracket"
                || name == "Wall_Bracket_Plate" || name == "Weatherproof_Power_Lead";
            if (family == "Billboard") return name == "Weatherproof_LED_Display_Chassis" || name == "Recessed_Display_Gasket"
                || name == "Display_Cooling_Louvre" || name == "LED_Screen_Power_Riser" || name == "Wall_Display_Bracket"
                || name == "Gantry_Chord" || name == "Gantry_Diagonal" || name == "Building_Anchor_Beam";
            if (family == "Bridge") return name == "Bridge_Continuous_Sill" || name == "Bridge_LightStrip"
                || name == "Bridge_Frame" || name == "Bridge_Truss";
            if (family == "Station") return name == "Tile_Dado" || name == "Tile_Skirting" || name == "Tile_Expansion_Joint"
                || name == "Tactile_Rib" || name == "Running_Rail" || name == "Station_Nameplate" || name == "Platform_Media_Frame"
                || name.StartsWith("Ceiling_Structural_Rib_", StringComparison.Ordinal) || name.StartsWith("Service_Conduit_", StringComparison.Ordinal);
            if (family == "Train") return name == "End_Door_Recess" || name == "Train_Beltline" || name == "Overhead_GrabRail"
                || name == "Door_Warning_Edge" || name == "Passenger_Media_Frame"
                || name.StartsWith("Grab_Pole_", StringComparison.Ordinal) || name.StartsWith("Passenger_HandLoop_", StringComparison.Ordinal)
                || (name.StartsWith("Train_Window_", StringComparison.Ordinal) && name.EndsWith("_OpenGasket", StringComparison.Ordinal))
                || name.StartsWith("Interior_LightTube_", StringComparison.Ordinal);
            if (family == "Seats") return name == "Seat_Continuous_Rail" || name.StartsWith("Seat_EndRail_", StringComparison.Ordinal);
            if (family == "Luminaire") return name == "Light_Housing" || name.StartsWith("Fluorescent_Tube_", StringComparison.Ordinal);
            return false;
        }

        private static bool PassiveEligible(Transform child, HashSet<Object> references, HashSet<Mesh> physics)
        {
            if (child.childCount != 0 || !child.gameObject.activeSelf || child.GetComponents<Component>().Length != 3
                || child.localScale.x <= 0f || child.localScale.y <= 0f || child.localScale.z <= 0f) return false;
            MeshFilter filter = child.GetComponent<MeshFilter>(); MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            Mesh mesh = filter ? filter.sharedMesh : null;
            return renderer && renderer.enabled && !renderer.forceRenderingOff && !renderer.additionalVertexStreams && !renderer.isPartOfStaticBatch
                && mesh && mesh.isReadable && KnownVertexChannels(mesh) && mesh.subMeshCount == 1
                && mesh.GetTopology(0) == MeshTopology.Triangles && mesh.vertexCount > 0 && mesh.blendShapeCount == 0 && mesh.bindposes.Length == 0
                && !physics.Contains(mesh) && !references.Contains(mesh) && !references.Contains(filter) && !references.Contains(renderer)
                && !renderer.HasPropertyBlock() && renderer.sharedMaterials.Length == 1 && renderer.sharedMaterial && renderer.sharedMaterial.shader
                && renderer.sharedMaterial.renderQueue < 3000 && !HasLightmap(renderer.lightmapIndex) && !HasLightmap(renderer.realtimeLightmapIndex)
                && !renderer.lightProbeProxyVolumeOverride && renderer.sharedMaterial.GetTag("DisableBatching", false, "False") != "True"
                && (GameObjectUtility.GetStaticEditorFlags(child.gameObject) & StaticEditorFlags.BatchingStatic) != 0;
        }

        private static bool SameVertexChannels(Mesh a, Mesh b)
        {
            VertexAttributeDescriptor[] left = a.GetVertexAttributes(), right = b.GetVertexAttributes();
            if (left.Length != right.Length) return false;
            for (int index = 0; index < left.Length; index++)
                if (left[index].attribute != right[index].attribute || left[index].format != right[index].format
                    || left[index].dimension != right[index].dimension) return false;
            return true;
        }

        private static bool TryMergePassive(Transform assembly, HashSet<Object> references, HashSet<Mesh> physics, out int sources, out int outputs)
        {
            sources = outputs = 0;
            if (!assembly.gameObject.activeInHierarchy) return false;
            string family = PassiveFamily(assembly);
            var groups = new List<Group>();
            foreach (Transform child in assembly)
            {
                if (!PassivePart(family, child.name) || !PassiveEligible(child, references, physics)) continue;
                MeshFilter filter = child.GetComponent<MeshFilter>(); MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                Group group = groups.Find(existing => SameRendererState(existing.renderer, renderer)
                    && SameVertexChannels(existing.pieces[0].mesh, filter.sharedMesh));
                if (group == null) { group = new Group { renderer = renderer }; groups.Add(group); }
                group.pieces.Add(new CombineInstance { mesh = filter.sharedMesh, transform = assembly.worldToLocalMatrix * child.localToWorldMatrix });
            }
            groups.RemoveAll(group => group.pieces.Count < 2);
            if (groups.Count == 0) return false;
            var meshes = new List<Mesh>(); var created = new List<GameObject>(); var retired = new List<GameObject>();
            try
            {
                foreach (Group group in groups)
                {
                    Mesh mesh = new Mesh { name = "Manufactured_Web_Passive_" + family };
                    meshes.Add(mesh);
                    int vertices = 0; foreach (CombineInstance piece in group.pieces) vertices = checked(vertices + piece.mesh.vertexCount);
                    mesh.indexFormat = vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                    mesh.CombineMeshes(group.pieces.ToArray(), true, true, false); mesh.RecalculateBounds();
                    GameObject output = new GameObject("Manufactured_Web_Passive_" + family + "_" + meshes.Count);
                    created.Add(output); output.transform.SetParent(assembly, false);
                    output.AddComponent<MeshFilter>().sharedMesh = mesh;
                    CopyRendererState(group.renderer, output.AddComponent<MeshRenderer>());
                    GameObjectUtility.SetStaticEditorFlags(output, GameObjectUtility.GetStaticEditorFlags(group.renderer.gameObject));
                    // Identify originals by state/layout and exact positive part names.
                    // Keep every placement object and all excluded live bindings.
                    foreach (Transform child in assembly)
                        if (PassivePart(family, child.name) && PassiveEligible(child, references, physics))
                        {
                            MeshFilter filter = child.GetComponent<MeshFilter>(); MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                            if (SameRendererState(group.renderer, renderer) && SameVertexChannels(group.pieces[0].mesh, filter.sharedMesh)) retired.Add(child.gameObject);
                        }
                }
                foreach (GameObject original in retired)
                {
                    Object.DestroyImmediate(original.GetComponent<MeshRenderer>());
                    Object.DestroyImmediate(original.GetComponent<MeshFilter>());
                }
                sources = retired.Count; outputs = created.Count;
                return true;
            }
            catch
            {
                foreach (GameObject output in created) if (output) Object.DestroyImmediate(output);
                foreach (Mesh mesh in meshes) if (mesh) Object.DestroyImmediate(mesh);
                throw;
            }
        }

        private static bool UnderJapanCity(Transform item)
        {
            bool japan = false;
            for (Transform parent = item; parent; parent = parent.parent)
            {
                if (parent.GetComponent<SlopCityInteractable>() || parent.GetComponent<SlopCityDoor>() || parent.GetComponent<Rigidbody>()
                    || parent.GetComponent<Animator>() || parent.GetComponent<Animation>()) return false;
                japan |= parent.name == "City_JapanMetropolis";
                if (parent.name == "SlopCity_NineBlocks") return japan;
            }
            return false;
        }

        private static bool TryMerge(Transform assembly, HashSet<Object> references, HashSet<Mesh> physics, out int outputs)
        {
            outputs = 0;
            if (!assembly.gameObject.activeInHierarchy || assembly.childCount != 19 || assembly.GetComponents<Component>().Length != 1) return false;
            var names = new Dictionary<string, int>();
            var groups = new List<Group>();
            var filters = new List<MeshFilter>();
            var renderers = new List<MeshRenderer>();
            foreach (Transform child in assembly)
            {
                if (!ExpectedParts.ContainsKey(child.name) || child.childCount != 0 || child.localScale != Vector3.one
                    || !child.gameObject.activeSelf || child.GetComponents<Component>().Length != 3) return false;
                MeshFilter filter = child.GetComponent<MeshFilter>(); MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                Mesh mesh = filter ? filter.sharedMesh : null;
                if (!renderer || !renderer.enabled || renderer.forceRenderingOff || renderer.additionalVertexStreams || renderer.isPartOfStaticBatch || !mesh || !mesh.isReadable || !KnownVertexChannels(mesh) || mesh.subMeshCount != 1
                    || mesh.GetTopology(0) != MeshTopology.Triangles || mesh.vertexCount == 0 || mesh.blendShapeCount != 0
                    || mesh.bindposes.Length != 0 || physics.Contains(mesh) || references.Contains(mesh)
                    || references.Contains(filter) || references.Contains(renderer) || renderer.HasPropertyBlock()
                    || renderer.sharedMaterials.Length != 1 || !renderer.sharedMaterial || !renderer.sharedMaterial.shader
                    || renderer.sharedMaterial.renderQueue >= 3000 || HasLightmap(renderer.lightmapIndex) || HasLightmap(renderer.realtimeLightmapIndex)
                    || renderer.lightProbeProxyVolumeOverride || renderer.sharedMaterial.GetTag("DisableBatching", false, "False") == "True") return false;
                if ((GameObjectUtility.GetStaticEditorFlags(child.gameObject) & StaticEditorFlags.BatchingStatic) == 0) return false;
                names.TryGetValue(child.name, out int count); names[child.name] = count + 1;
                Group group = null;
                foreach (Group existing in groups) if (SameRendererState(existing.renderer, renderer)) { group = existing; break; }
                if (group == null) { group = new Group { renderer = renderer }; groups.Add(group); }
                group.pieces.Add(new CombineInstance { mesh = mesh, transform = assembly.worldToLocalMatrix * child.localToWorldMatrix });
                filters.Add(filter); renderers.Add(renderer);
            }
            foreach (var expected in ExpectedParts) if (!names.TryGetValue(expected.Key, out int count) || count != expected.Value) return false;
            if (groups.Count >= renderers.Count) return false;
            var created = new List<GameObject>(); var meshes = new List<Mesh>();
            try
            {
                foreach (Group group in groups)
                {
                    Mesh mesh = new Mesh { name = "Manufactured_Web_AC_Decoration" };
                    meshes.Add(mesh);
                    int vertices = 0; foreach (CombineInstance piece in group.pieces) vertices += piece.mesh.vertexCount;
                    mesh.indexFormat = vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                    mesh.CombineMeshes(group.pieces.ToArray(), true, true, false);
                    mesh.RecalculateBounds();
                    GameObject output = new GameObject("Manufactured_Web_AC_Decoration_" + meshes.Count);
                    created.Add(output); output.transform.SetParent(assembly, false);
                    output.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = output.AddComponent<MeshRenderer>();
                    CopyRendererState(group.renderer, renderer);
                    GameObjectUtility.SetStaticEditorFlags(output, GameObjectUtility.GetStaticEditorFlags(group.renderer.gameObject));
                }
                // Original transforms remain: source runtime lookup and authored placement
                // survive, while no old renderer/filter retains extra bindings or source meshes.
                foreach (MeshRenderer renderer in renderers) Object.DestroyImmediate(renderer);
                foreach (MeshFilter filter in filters) Object.DestroyImmediate(filter);
                outputs = created.Count;
                return true;
            }
            catch
            {
                foreach (GameObject output in created) if (output) Object.DestroyImmediate(output);
                foreach (Mesh mesh in meshes) if (mesh) Object.DestroyImmediate(mesh);
                throw; // Reject a partially changed export rather than silently hide an art failure.
            }
        }

        private static bool KnownVertexChannels(Mesh mesh)
        {
            if (!mesh.HasVertexAttribute(VertexAttribute.Position) || !mesh.HasVertexAttribute(VertexAttribute.Normal)
                || !mesh.HasVertexAttribute(VertexAttribute.Tangent) || !mesh.HasVertexAttribute(VertexAttribute.TexCoord0)) return false;
            foreach (VertexAttributeDescriptor channel in mesh.GetVertexAttributes())
                if (channel.attribute != VertexAttribute.Position && channel.attribute != VertexAttribute.Normal
                    && channel.attribute != VertexAttribute.Tangent && channel.attribute != VertexAttribute.TexCoord0
                    && channel.attribute != VertexAttribute.TexCoord1) return false;
            if (mesh.HasVertexAttribute(VertexAttribute.TexCoord1) && mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord1) != 2) return false;
            return mesh.GetVertexAttributeDimension(VertexAttribute.Position) == 3 && mesh.GetVertexAttributeDimension(VertexAttribute.Normal) == 3
                && mesh.GetVertexAttributeDimension(VertexAttribute.Tangent) == 4 && mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0) == 2;
        }

        private static bool HasLightmap(int index) => index >= 0 && index < 65534;

        private static bool SameRendererState(MeshRenderer a, MeshRenderer b)
            => a.sharedMaterial == b.sharedMaterial && a.shadowCastingMode == b.shadowCastingMode && a.receiveShadows == b.receiveShadows
                && a.lightProbeUsage == b.lightProbeUsage && a.reflectionProbeUsage == b.reflectionProbeUsage && a.probeAnchor == b.probeAnchor
                && a.motionVectorGenerationMode == b.motionVectorGenerationMode && a.allowOcclusionWhenDynamic == b.allowOcclusionWhenDynamic
                && a.gameObject.layer == b.gameObject.layer && a.sortingLayerID == b.sortingLayerID && a.sortingOrder == b.sortingOrder
                && a.renderingLayerMask == b.renderingLayerMask;
        private static void CopyRendererState(MeshRenderer source, MeshRenderer target)
        {
            target.sharedMaterial = source.sharedMaterial; target.shadowCastingMode = source.shadowCastingMode; target.receiveShadows = source.receiveShadows;
            target.lightProbeUsage = source.lightProbeUsage; target.reflectionProbeUsage = source.reflectionProbeUsage; target.probeAnchor = source.probeAnchor;
            target.motionVectorGenerationMode = source.motionVectorGenerationMode; target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            target.gameObject.layer = source.gameObject.layer; target.sortingLayerID = source.sortingLayerID; target.sortingOrder = source.sortingOrder;
            target.renderingLayerMask = source.renderingLayerMask;
        }
    }
}
#endif
