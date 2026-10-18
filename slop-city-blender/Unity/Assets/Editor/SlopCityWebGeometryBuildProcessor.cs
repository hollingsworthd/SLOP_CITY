#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopCity.Editor
{
    /// <summary>Captures browser hit bounds in the temporary build scene, before static batching.</summary>
    [BuildCallbackVersion(24)]
    public sealed class SlopCityWebGeometryBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => int.MinValue;
        public static int CapturedRendererCount { get; private set; }
        public static int ProtectedRendererCount { get; private set; }
        public static void ResetCaptureCount() => CapturedRendererCount = 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Unity also invokes this hook before Editor Play Mode scene batching (null report).
            if (report == null || BuildPipeline.isBuildingPlayer) ProtectRuntimeBuildingGeometry(scene);
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
                Debug.Log("SLOP_CITY_WEB_GEOMETRY_CALLBACK scene=" + scene.path
                    + " platform=" + (report == null ? "no report" : report.summary.platform.ToString())
                    + " buildingPlayer=" + BuildPipeline.isBuildingPlayer);
            if (report == null || report.summary.platform != BuildTarget.WebGL || !BuildPipeline.isBuildingPlayer) return;
            SlopCityWebDecorativeMerge.Prepare(scene);
            var renderers = new List<Renderer>();
            foreach (GameObject root in scene.GetRootGameObjects())
                renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));
            var metadata = new GameObject("Browser_Interaction_Geometry");
            SceneManager.MoveGameObjectToScene(metadata, scene);
            int count = metadata.AddComponent<SlopCityBrowserGeometry>().Capture(renderers.ToArray());
            CapturedRendererCount += count;
            Debug.Log("SLOP_CITY_WEB_GEOMETRY_CAPTURED " + count + " authored renderer bounds in " + scene.path);
            SlopCityWebMeshBudget.Prepare(scene, true);
        }

        /// <summary>Runtime hollowing needs original metric meshes and movable control/equipment transforms.</summary>
        public static int ProtectRuntimeBuildingGeometry(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == "SlopCity_NineBlocks")
                    {
                        SlopCityHumanScale.Prepare(child);
                        SlopCityMetricFacade.Prepare(child);
                        SlopCityCitadelInterior.PrepareCitadel(child);
                        SlopCityBuildingWorld.PrepareTowerLayout(child);
                        SlopCityBuildingWorld.PrepareServiceBridges(child);
                        SlopCityMetroWorld.Prepare(child);
                        SlopCityLuminousCity.Prepare(child, Shader.Find("SlopCity/WetCitySurface"));
                        SlopCitySalvagePreparation.Prepare(child);
                        int reflections = SlopCityMetricReflectionBake.ApplyBaked(scene);
                        if (reflections > 0) Debug.Log("SLOP_CITY_METRIC_REFLECTIONS_APPLIED " + reflections);
                    }
                    else if (child.name == "SlopCity_Lounge")
                        SlopCitySalvagePreparation.PrepareHome(child);
            SlopCityInteriorSmokeTest.CaptureHomeFacadeBeforeBatch(scene);
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    Transform item = renderer.transform;
                    bool train = false;
                    for (Transform ancestor = item; ancestor; ancestor = ancestor.parent)
                        if (ancestor.name.StartsWith("Subway_Train_", System.StringComparison.Ordinal)) { train = true; break; }
                    if (train || SlopCitySalvagePreparation.IsMutable(renderer))
                    {
                        // Cars move and turn after export; retain local meshes and live bounds.
                        GameObjectUtility.SetStaticEditorFlags(item.gameObject, 0);
                        count++;
                        continue;
                    }
                    bool protect = item.name == "Structural_Core" || item.name == "Setback_UpperShaft"
                        || item.name == "Human_Scale_Window_Panes" || item.name == "Floor_Aligned_Stonework";
                    for (Transform ancestor = item; !protect && ancestor; ancestor = ancestor.parent)
                    {
                        string name = ancestor.name;
                        protect = name == "Tower_Lift_Street" || name == "Tower_Lift_Roof"
                            || name == "Platform_Lift_Control" || name == "Street_Lift_Control"
                            || name == "Mechanical_Unit" || name == "HVAC_Louvre"
                            || name == "Roof_Plant_Screen" || name == "Mechanical_Crown" || name == "Crown_Louvre";
                    }
                    if (!protect && (item.name == "CurtainWall_Panel" || item.name == "Masonry_WindowBays")
                        && item.parent && item.parent.name.StartsWith("Skyline_Tower_", System.StringComparison.Ordinal))
                    {
                        Transform core = item.parent.Find("Structural_Core");
                        MeshFilter filter = core ? core.GetComponent<MeshFilter>() : null;
                        if (filter && filter.sharedMesh)
                        {
                            Vector3 size = filter.sharedMesh.bounds.size;
                            protect = Mathf.Abs(item.localPosition.z + size.z * .5f + .018f) < .05f
                                && item.localPosition.y <= size.y;
                        }
                    }
                    if (!protect) continue;
                    StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(item.gameObject);
                    GameObjectUtility.SetStaticEditorFlags(item.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
                    count++;
                }
            Debug.Log("SLOP_CITY_BUILDING_GEOMETRY_PRESERVED " + count + " runtime geometry renderers excluded from static batching.");
            ProtectedRendererCount = count;
            return count;
        }
    }
}
#endif
