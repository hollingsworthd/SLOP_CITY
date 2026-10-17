#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    public static class SlopCityWebSingletonMaterialCheck
    {
        private static int checks;
        [Serializable] private sealed class Receipt
        {
            public string startedAtUtc, completedAtUtc, unityVersion, error;
            public bool success;
            public int checks;
            public SlopCityWebSingletonMaterialBuildProcessor.Result actualScene;
        }

        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Singleton material checks require a dedicated batch Editor outside Play Mode.");
            var receipt = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            int exit = 0;
            try
            {
                Check(!SlopCityWebSingletonMaterialBuildProcessor.ShouldProcess(false, true, BuildTarget.WebGL), "Editor scene callbacks cannot change materials.");
                Check(!SlopCityWebSingletonMaterialBuildProcessor.ShouldProcess(true, false, BuildTarget.WebGL), "A report outside a player build cannot change materials.");
                Check(!SlopCityWebSingletonMaterialBuildProcessor.ShouldProcess(true, true, BuildTarget.StandaloneOSX), "Native builds cannot change materials.");
                Check(SlopCityWebSingletonMaterialBuildProcessor.ShouldProcess(true, true, BuildTarget.WebGL), "Only an actual Web player build admits variants.");
                Check(new SlopCityWebSingletonMaterialBuildProcessor().callbackOrder == int.MinValue + 1,
                    "Material selection occurs after geometry preparation/capture and before facade finalization/static batching.");
                ExactVariants();
                GuardExceptions();
                Scene scene = EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                SlopCityWebGeometryBuildProcessor.ProtectRuntimeBuildingGeometry(scene);
                SlopCityWebDecorativeMerge.Prepare(scene);
                MeshRenderer[] renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
                var metadata = new GameObject("SingletonFixtureBrowserBounds").AddComponent<SlopCityBrowserGeometry>();
                metadata.Capture(renderers);
                SlopCityWebMeshBudget.Prepare(scene, true);
                receipt.actualScene = SlopCityWebSingletonMaterialBuildProcessor.Prepare(scene);
                Check(receipt.actualScene.changedRenderers > 0, "Actual prepared unsaved city admits singleton variants under the ownership guards.");
                Check(scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)).Count() == renderers.Length,
                    "Actual city retains every renderer identity/count.");
                var again = SlopCityWebSingletonMaterialBuildProcessor.Prepare(scene);
                Check(again.changedRenderers == 0 && again.materialVariants == 0, "Actual scene preparation is idempotent.");
                receipt.success = true;
            }
            catch (Exception error) { receipt.error = error.ToString(); exit = 1; }
            finally
            {
                receipt.checks = checks; receipt.completedAtUtc = DateTime.UtcNow.ToString("O");
                Directory.CreateDirectory("Builds/Reports");
                File.WriteAllText("Builds/Reports/web-singleton-material-check.json", JsonUtility.ToJson(receipt, true));
            }
            Debug.Log("Web singleton material checks: " + checks + "; success=" + receipt.success);
            EditorApplication.Exit(exit);
        }

        private static void ExactVariants()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var materials = new List<Material>(); var meshes = new List<Mesh>();
            Material source = Source(); materials.Add(source);
            Mesh a = Triangle(), b = Triangle(), repeated = Triangle(); meshes.AddRange(new[] { a, b, repeated });
            try
            {
                MeshRenderer first = Render("SingletonA", a, source), second = Render("SingletonB", b, source);
                MeshRenderer repeat1 = Render("RepeatedA", repeated, source), repeat2 = Render("RepeatedB", repeated, source);
                first.transform.position = new Vector3(2, 3, 4); first.transform.rotation = Quaternion.Euler(15, 35, -20);
                first.transform.localScale = new Vector3(1, 2, .5f);
                first.gameObject.AddComponent<BoxCollider>().size = new Vector3(2, 1, 3);
                var metadata = new GameObject("BoundsOnly").AddComponent<SlopCityBrowserGeometry>();
                metadata.Capture(new Renderer[] { first, second, repeat1, repeat2 });
                Matrix4x4 transform = first.localToWorldMatrix;
                var flags = GameObjectUtility.GetStaticEditorFlags(first.gameObject);
                var result = SlopCityWebSingletonMaterialBuildProcessor.Prepare(scene);
                Material variant = first.sharedMaterial; materials.Add(variant);
                Check(result.changedRenderers == 2 && result.changedSlots == 2 && result.materialVariants == 1,
                    "Different singleton meshes share exactly one material variant per original material.");
                Check(variant != source && second.sharedMaterial == variant && !variant.enableInstancing,
                    "Only singleton slots receive the non-instanced variant.");
                Check(source.enableInstancing && repeat1.sharedMaterial == source && repeat2.sharedMaterial == source,
                    "Repeated scenery and original material keep instancing and shared identity.");
                EqualProperties(source, variant);
                Check(first.GetComponent<MeshFilter>().sharedMesh == a && first.localToWorldMatrix == transform
                    && first.GetComponent<BoxCollider>().size == new Vector3(2, 1, 3)
                    && GameObjectUtility.GetStaticEditorFlags(first.gameObject) == flags,
                    "Exact mesh/transforms/collision/static flags stay unchanged, including nonuniform scale.");
                var bounds = new Dictionary<Renderer, Bounds>(); metadata.CopyTo(bounds);
                Check(bounds.Count == 4 && bounds.ContainsKey(first) && bounds[first] == a.bounds,
                    "Browser bounds-only ownership retains its original renderer/geometry mapping.");
                result = SlopCityWebSingletonMaterialBuildProcessor.Prepare(scene);
                Check(result.changedRenderers == 0 && result.materialVariants == 0, "Repeated preparation allocates no new variants.");
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                foreach (Mesh mesh in meshes) Object.DestroyImmediate(mesh);
                foreach (Material material in materials) Object.DestroyImmediate(material);
            }
        }

        private static void GuardExceptions()
        {
            for (int guard = 0; guard < 15; guard++)
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Material source = Source(); Mesh mesh = Triangle(), extra = Triangle();
                MeshRenderer renderer = Render("Guarded", mesh, source);
                var parent = new GameObject("Owner"); renderer.transform.SetParent(parent.transform, false);
                try
                {
                    if (guard == 0) renderer.gameObject.SetActive(false);
                    if (guard == 1) renderer.enabled = false;
                    if (guard == 2) { var block = new MaterialPropertyBlock(); block.SetFloat("_Glossiness", .7f); renderer.SetPropertyBlock(block); }
                    if (guard == 3) renderer.additionalVertexStreams = extra;
                    if (guard == 4) parent.AddComponent<Rigidbody>();
                    if (guard == 5) parent.AddComponent<Animator>();
                    if (guard == 6) parent.AddComponent<SlopCityInteractable>();
                    if (guard == 7) parent.AddComponent<SlopCityDoor>();
                    if (guard == 8) parent.AddComponent<SlopCityElevator>();
                    if (guard == 9)
                    {
                        var profile = parent.AddComponent<SlopCityLuminousCity>();
                        using (var serialized = new SerializedObject(profile))
                        {
                            var pairs = serialized.FindProperty("materialPairs"); pairs.arraySize = 1;
                            pairs.GetArrayElementAtIndex(0).FindPropertyRelative("source").objectReferenceValue = source;
                            // A material-pair owner must keep its exact live slot.
                            serialized.ApplyModifiedPropertiesWithoutUndo();
                        }
                    }
                    if (guard == 10)
                    {
                        var display = parent.AddComponent<SlopCityCityScreen>();
                        using (var serialized = new SerializedObject(display))
                        { serialized.FindProperty("display").objectReferenceValue = renderer; serialized.ApplyModifiedPropertiesWithoutUndo(); }
                    }
                    if (guard == 11) new GameObject("PhysicsOwner").AddComponent<MeshCollider>().sharedMesh = mesh;
                    if (guard == 12) { MeshRenderer duplicate = Render("InactivePeer", mesh, source); duplicate.gameObject.SetActive(false); }
                    if (guard == 13)
                    {
                        var skin = new GameObject("CrowdOrSkinOwner").AddComponent<SkinnedMeshRenderer>();
                        mesh.bindposes = new[] { Matrix4x4.identity };
                        var weight = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
                        mesh.boneWeights = new[] { weight, weight, weight };
                        skin.bones = new[] { skin.transform }; skin.sharedMesh = mesh;
                    }
                    if (guard == 14) source.shader = Shader.Find("Unlit/Texture");
                    var result = SlopCityWebSingletonMaterialBuildProcessor.Prepare(scene);
                    Check(result.changedRenderers == 0 && result.materialVariants == 0 && renderer.sharedMaterial == source
                        && source.enableInstancing && renderer.GetComponent<MeshFilter>().sharedMesh == mesh,
                        "Playback/script/material/physics/skin/hidden-peer/movable/property/unsupported exceptions preserve original ownership.");
                }
                finally
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    Object.DestroyImmediate(mesh); Object.DestroyImmediate(extra); Object.DestroyImmediate(source);
                }
            }
        }

        private static void EqualProperties(Material source, Material variant)
        {
            Check(source.name == variant.name && source.shader == variant.shader && source.renderQueue == variant.renderQueue
                && source.globalIlluminationFlags == variant.globalIlluminationFlags && source.doubleSidedGI == variant.doubleSidedGI
                && source.shaderKeywords.OrderBy(x => x).SequenceEqual(variant.shaderKeywords.OrderBy(x => x)),
                "Shader/name/render queue/GI and all shader keywords remain exact.");
            for (int property = 0; property < source.shader.GetPropertyCount(); property++)
            {
                string name = source.shader.GetPropertyName(property);
                ShaderPropertyType type = source.shader.GetPropertyType(property);
                if (type == ShaderPropertyType.Color) Check(source.GetColor(name) == variant.GetColor(name), "Exact authored color " + name);
                else if (type == ShaderPropertyType.Vector) Check(source.GetVector(name) == variant.GetVector(name), "Exact authored vector " + name);
                else if (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range) Check(source.GetFloat(name) == variant.GetFloat(name), "Exact authored scalar " + name);
                else if (type == ShaderPropertyType.Int) Check(source.GetInteger(name) == variant.GetInteger(name), "Exact authored integer " + name);
                else if (type == ShaderPropertyType.Texture)
                    Check(source.GetTexture(name) == variant.GetTexture(name) && source.GetTextureScale(name) == variant.GetTextureScale(name)
                        && source.GetTextureOffset(name) == variant.GetTextureOffset(name), "Exact texture identity/scale/offset " + name);
            }
        }

        private static Material Source()
        {
            var material = new Material(Shader.Find("Standard")) { name = "ExactSingletonSource", enableInstancing = true,
                color = new Color(.3f, .5f, .7f, .8f), renderQueue = 2237, doubleSidedGI = true };
            material.SetFloat("_Glossiness", .37f); material.SetFloat("_Metallic", .63f);
            material.SetTexture("_MainTex", Texture2D.whiteTexture); material.SetTextureScale("_MainTex", new Vector2(2.5f, 3.7f));
            material.SetTextureOffset("_MainTex", new Vector2(.13f, -.27f)); material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(.2f, .1f, .03f));
            return material;
        }
        private static Mesh Triangle()
        {
            var mesh = new Mesh { name = "ExactSingletonGeometry" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static MeshRenderer Render(string name, Mesh mesh, Material material)
        {
            var node = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            node.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(node, StaticEditorFlags.BatchingStatic);
            return renderer;
        }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
    }
}
#endif
