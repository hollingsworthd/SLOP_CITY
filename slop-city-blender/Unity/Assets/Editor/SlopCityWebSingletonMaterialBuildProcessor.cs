#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Temporary Web scene variants avoid instancing arrays for singleton geometry.</summary>
    [BuildCallbackVersion(1)]
    public sealed class SlopCityWebSingletonMaterialBuildProcessor : IProcessSceneWithReport
    {
        // Geometry preparation/capture is int.MinValue. Count its final mesh
        // identities before facade finalization (+2) and Unity's static batching.
        public int callbackOrder => int.MinValue + 1;

        [System.Serializable]
        public sealed class Result
        {
            public int meshFilters, singletonMeshes, changedRenderers, changedSlots, materialVariants;
            public int repeatedFilters, ownedGeometrySkipped, guardedRenderers, ownedMaterialSlotsSkipped;
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (!ShouldProcess(report != null, BuildPipeline.isBuildingPlayer,
                report == null ? BuildTarget.NoTarget : report.summary.platform)) return;
            Prepare(scene);
        }

        public static bool ShouldProcess(bool hasReport, bool buildingPlayer, BuildTarget platform)
            => hasReport && buildingPlayer && platform == BuildTarget.WebGL;

        // The standalone callback above owns the Web/player guard. Public for a
        // focused unsaved-scene fixture; never writes source materials or scenes.
        public static Result Prepare(Scene scene)
        {
            var result = new Result();
            var filters = new List<MeshFilter>();
            var consumers = new Dictionary<Mesh, int>();
            var owned = new HashSet<Object>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!filter.sharedMesh) continue;
                    filters.Add(filter);
                    consumers.TryGetValue(filter.sharedMesh, out int count);
                    consumers[filter.sharedMesh] = count + 1;
                }
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    // Browser geometry only reads captured bounds. Its renderer
                    // references remain valid because this changes material slots only.
                    if (!component || component is Transform || component is MeshFilter || component is MeshRenderer
                        || component is SlopCityBrowserGeometry) continue;
                    using (var serialized = new SerializedObject(component))
                    {
                        SerializedProperty property = serialized.GetIterator();
                        while (property.Next(true))
                            if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue)
                                owned.Add(property.objectReferenceValue);
                    }
                }
            }
            result.meshFilters = filters.Count;
            foreach (int count in consumers.Values) if (count == 1) result.singletonMeshes++;
            var variants = new Dictionary<Material, Material>();
            foreach (MeshFilter filter in filters)
            {
                if (consumers[filter.sharedMesh] != 1) { result.repeatedFilters++; continue; }
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (owned.Contains(filter) || owned.Contains(filter.sharedMesh) || (renderer && owned.Contains(renderer)))
                { result.ownedGeometrySkipped++; continue; }
                if (!Eligible(filter, renderer)) { result.guardedRenderers++; continue; }
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material source = materials[slot];
                    if (!source || !source.enableInstancing || !source.shader
                        || (source.shader.name != "Standard" && source.shader.name != "SlopCity/WetCitySurface")) continue;
                    // Playback, crowd, facade and LuminousCity material pairs keep
                    // their exact shared ownership and runtime reconfiguration.
                    if (owned.Contains(source)) { result.ownedMaterialSlotsSkipped++; continue; }
                    if (!variants.TryGetValue(source, out Material variant))
                    {
                        variant = new Material(source) { name = source.name, enableInstancing = false };
                        variants.Add(source, variant);
                    }
                    materials[slot] = variant;
                    result.changedSlots++;
                    changed = true;
                }
                if (!changed) continue;
                renderer.sharedMaterials = materials;
                result.changedRenderers++;
            }
            result.materialVariants = variants.Count;
            Debug.Log("SLOP_CITY_WEB_SINGLETON_MATERIALS " + JsonUtility.ToJson(result));
            return result;
        }

        private static bool Eligible(MeshFilter filter, MeshRenderer renderer)
        {
            if (!renderer || !renderer.enabled || !filter.gameObject.activeInHierarchy
                || filter.sharedMesh.vertexCount == 0 || renderer.HasPropertyBlock() || renderer.additionalVertexStreams) return false;
            foreach (Component component in filter.GetComponents<Component>())
                if (!(component is Transform) && !(component is MeshFilter) && !(component is MeshRenderer)
                    && !(component is Collider)) return false;
            for (Transform item = filter.transform; item; item = item.parent)
            {
                if (item.GetComponent<SlopCityDoor>() || item.GetComponent<SlopCityElevator>()
                    || item.GetComponent<Rigidbody>() || item.GetComponent<Animator>() || item.GetComponent<Animation>()
                    || item.GetComponent<SlopCityInteractable>() || item.name == "Parked_CityCar"
                    || item.name == "Subway_Train_0" || item.name == "Subway_Train_1"
                    || item.name == "Subway_SurfaceLift") return false;
            }
            return true;
        }
    }
}
#endif
