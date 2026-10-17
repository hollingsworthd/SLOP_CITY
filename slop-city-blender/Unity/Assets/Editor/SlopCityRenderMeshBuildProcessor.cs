#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopCity.Editor
{
    /// <summary>Discard immutable facade CPU data in player scenes before it inflates runtime memory.</summary>
    [BuildCallbackVersion(2)]
    public sealed class SlopCityRenderMeshBuildProcessor : IProcessSceneWithReport
    {
        // The existing callback prepares geometry and captures browser bounds.
        // Unity performs internal static batching after scene callbacks finish.
        public int callbackOrder => int.MinValue + 2;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || !BuildPipeline.isBuildingPlayer) return;
            var batching = new HashSet<Mesh>();
            var unbatched = new HashSet<Mesh>();
            var requiredReadable = new HashSet<Mesh>();
            var facades = new List<SlopCityMetricFacade>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                facades.AddRange(root.GetComponentsInChildren<SlopCityMetricFacade>(true));
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    if (!mesh) continue;
                    Renderer renderer = filter.GetComponent<Renderer>();
                    bool willBatch = renderer && renderer.enabled
                        && (GameObjectUtility.GetStaticEditorFlags(filter.gameObject) & StaticEditorFlags.BatchingStatic) != 0;
                    if (willBatch) batching.Add(mesh); else unbatched.Add(mesh);
                }
                // Collider cooking and skin deformation retain their normal
                // data requirements; generated facades use neither subsystem.
                foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
                    if (collider.sharedMesh) requiredReadable.Add(collider.sharedMesh);
                foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (skin.sharedMesh) requiredReadable.Add(skin.sharedMesh);
                foreach (SlopCitySalvageSurface surface in root.GetComponentsInChildren<SlopCitySalvageSurface>(true))
                    if (surface.SourceMesh) requiredReadable.Add(surface.SourceMesh);
            }
            var batchingOnly = new HashSet<Mesh>(batching);
            batchingOnly.ExceptWith(unbatched);
            batchingOnly.ExceptWith(requiredReadable);
            var renderOnly = new HashSet<Mesh>(unbatched);
            renderOnly.ExceptWith(batching);
            renderOnly.ExceptWith(requiredReadable);
            int finalized = 0, detached = 0;
            foreach (SlopCityMetricFacade facade in facades)
            {
                facade.PreparePlayerMeshData(batchingOnly, renderOnly, out int released, out int retired);
                finalized += released; detached += retired;
            }
            Debug.Log("SLOP_CITY_FACADE_CPU_DATA_FINALIZED " + finalized
                + " immutable meshes; " + detached + " static source ownership references retired in " + scene.path);
        }
    }
}
#endif
