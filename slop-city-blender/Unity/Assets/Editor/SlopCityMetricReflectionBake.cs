#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Captures final runtime streets into new assets, preserving the authored scene/probes.</summary>
    public static class SlopCityMetricReflectionBake
    {
        public const string Folder = "Assets/Art/Reflections/MetricCity";
        public const string ManifestPath = Folder + "/manifest.json";
        private const int Resolution = 256;
        private static readonly string[] Names = {
            "Street_Reflection", "Junction_Reflection_WS", "Junction_Reflection_WN",
            "Junction_Reflection_ES", "Junction_Reflection_EN",
            "Reflection_Subway_Station_0", "Reflection_Subway_Station_1"
        };

        // Called by the isolated visual fixture after the real building world and shaft cutouts exist.
        public static void BakeFinalScene(Scene scene)
        {
            if (!Application.isBatchMode || !Application.isPlaying
                || !Application.persistentDataPath.Contains("SlopCityValidation"))
                throw new InvalidOperationException("Metric reflection capture requires isolated graphics-enabled validation Play Mode.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Metric reflection capture requires a graphics device.");
            Transform city = SceneObjects(scene).FirstOrDefault(item => item.name == "SlopCity_NineBlocks");
            SlopCityBuildingWorld world = city ? city.GetComponent<SlopCityBuildingWorld>() : null;
            if (!world || world.Buildings.Count == 0 || !city.Find("Runtime_City_Asphalt_With_Lift_Openings"))
                throw new InvalidOperationException("Capture the installed final city, including its actual metro shaft cutouts.");
            ReflectionProbe[] probes = FindProbes(scene);
            Directory.CreateDirectory(Folder);
            var receipt = new Manifest { capturedAtUtc = DateTime.UtcNow.ToString("O"), scenePath = scene.path,
                unityVersion = Application.unityVersion, resolution = Resolution };
            // A failed refresh must not leave a complete manifest pointing at a mixture
            // of previous and newly overwritten generated captures.
            File.WriteAllText(ManifestPath, JsonUtility.ToJson(receipt, true));
            bool directCamera = Environment.GetCommandLineArgs().Contains("-slopCityReflectionCameraCapture");
            // Every capture is a first lighting bounce. Leaving previously captured
            // probes active makes the later captures reflect earlier captures, including
            // any invalid HDR sample they contain. Never bake a probe into itself.
            ReflectionProbe[] allProbes = SceneObjects(scene).SelectMany(item => item.GetComponents<ReflectionProbe>()).ToArray();
            bool[] enabled = allProbes.Select(item => item.enabled).ToArray();
            Renderer[] renderers = SceneObjects(scene).SelectMany(item => item.GetComponents<Renderer>()).ToArray();
            ReflectionProbeUsage[] usages = renderers.Select(item => item.reflectionProbeUsage).ToArray();
            ReflectionProbeMode[] originalModes = probes.Select(item => item.mode).ToArray();
            Texture[] originalTextures = probes.Select(item => item.customBakedTexture).ToArray();
            bool complete = false;
            try
            {
                foreach (ReflectionProbe item in allProbes) item.enabled = false;
                foreach (Renderer item in renderers) item.reflectionProbeUsage = ReflectionProbeUsage.Off;
                foreach (ReflectionProbe probe in probes)
                {
                    string path = Folder + "/" + probe.name + ".exr";
                    ReflectionProbeMode previousMode = probe.mode;
                    Texture previousTexture = probe.customBakedTexture;
                    bool previousDynamic = probe.renderDynamicObjects;
                    int previousResolution = probe.resolution;
                    bool previousHdr = probe.hdr;
                    try
                    {
                        probe.resolution = Resolution; probe.hdr = true;
                        // The final procedural facade and cutout meshes exist in this Play scene.
                        probe.renderDynamicObjects = true;
                        probe.mode = ReflectionProbeMode.Custom;
                        bool baked = false;
                        if (!directCamera)
                        {
                            try { baked = Lightmapping.BakeReflectionProbe(probe, path); }
                            catch (Exception error)
                            { Debug.LogWarning("Metric probe uses direct cubemap capture: " + probe.name + ": " + error.Message); }
                        }
                        Texture texture = null;
                        float maximumRadiance = 0;
                        if (baked)
                        {
                            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                            texture = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                            if (!texture) throw new InvalidOperationException("Baked probe did not import as a cubemap: " + path);
                            maximumRadiance = ValidateImportedCubemap(path);
                            texture = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                        }
                        else
                        {
                            path = Folder + "/" + probe.name + ".asset";
                            texture = CaptureCubemap(probe, path, out maximumRadiance);
                        }
                        probe.mode = ReflectionProbeMode.Custom; probe.customBakedTexture = texture;
                        receipt.probes.Add(new Entry { name = probe.name, texturePath = path,
                            method = baked ? "Lightmapping.BakeReflectionProbe" : "Camera.RenderToCubemap + six-face HDR readback",
                            finiteHdrValidated = true, maximumRadiance = maximumRadiance,
                            position = probe.transform.position, influenceSize = probe.size, influenceCenter = probe.center });
                    }
                    catch
                    {
                        probe.mode = previousMode; probe.customBakedTexture = previousTexture;
                        throw;
                    }
                    finally
                    {
                        probe.renderDynamicObjects = previousDynamic;
                        probe.resolution = previousResolution; probe.hdr = previousHdr;
                    }
                }
                // Publish a complete map only after all seven captures succeed. Authored EXRs are never replaced.
                File.WriteAllText(ManifestPath, JsonUtility.ToJson(receipt, true));
                AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
                complete = true;
                Debug.Log("SLOP_CITY_METRIC_REFLECTIONS_BAKED " + receipt.probes.Count + " " + ManifestPath);
            }
            finally
            {
                if (!complete)
                    for (int index = 0; index < probes.Length; index++)
                        if (probes[index]) { probes[index].mode = originalModes[index]; probes[index].customBakedTexture = originalTextures[index]; }
                for (int index = 0; index < renderers.Length; index++)
                    if (renderers[index]) renderers[index].reflectionProbeUsage = usages[index];
                for (int index = 0; index < allProbes.Length; index++)
                    if (allProbes[index]) allProbes[index].enabled = enabled[index];
            }
        }

        // Invoke after final geometric preparation in the build scene; never saves that scene.
        public static int ApplyBaked(Scene scene)
        {
            if (!File.Exists(ManifestPath)) return 0;
            Manifest receipt = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (receipt == null || receipt.probes == null || receipt.probes.Count != Names.Length
                || receipt.probes.Select(item => item.name).Distinct().Count() != Names.Length)
                throw new InvalidOperationException("Metric reflection manifest is incomplete.");
            ReflectionProbe[] probes = FindProbes(scene);
            var textures = new Dictionary<string, Texture>();
            foreach (string name in Names)
            {
                Entry entry = receipt.probes.SingleOrDefault(item => item.name == name);
                if (entry == null || (entry.texturePath != Folder + "/" + name + ".exr"
                    && entry.texturePath != Folder + "/" + name + ".asset"))
                    throw new InvalidOperationException("Unexpected metric reflection asset for " + name);
                Cubemap texture = AssetDatabase.LoadAssetAtPath<Cubemap>(entry.texturePath);
                // The authored capture remains 256px. Only an active Web export
                // deliberately imports EXR captures at its smaller world-texture cap;
                // serialized direct captures and native/editor use keep the full guard.
                int expectedResolution = Resolution;
                if (BuildPipeline.isBuildingPlayer && EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL
                    && SlopCityWebTextureBudgetScope.ActiveReflectionMaximumSize > 0
                    && entry.texturePath.EndsWith(".exr", StringComparison.OrdinalIgnoreCase))
                    expectedResolution = Mathf.Min(Resolution, SlopCityWebTextureBudgetScope.ActiveReflectionMaximumSize);
                if (!texture || texture.width != expectedResolution)
                    throw new InvalidOperationException("Metric reflection cubemap is missing or has the wrong resolution: " + entry.texturePath);
                // Serialized direct captures retain readable floating-point pixels.
                // Check them at application too: reject a poisoned older capture before
                // it can enter the production scene or a subsequent capture batch.
                if (texture.isReadable)
                    for (int side = 0; side < 6; side++) ValidatePixels(texture.GetPixels((CubemapFace)side), entry.texturePath, side);
                textures.Add(name, texture);
            }
            foreach (ReflectionProbe probe in probes)
            {
                probe.mode = ReflectionProbeMode.Custom;
                probe.customBakedTexture = textures[probe.name];
            }
            return probes.Length;
        }

        private static Cubemap CaptureCubemap(ReflectionProbe probe, string path, out float maximumRadiance)
        {
            maximumRadiance = 0;
            // A standalone capture camera has no HUD, post effects, AudioListener or gameplay behaviours.
            GameObject cameraObject = null;
            RenderTexture target = null;
            Texture2D face = null;
            Cubemap cube = null;
            RenderTexture oldActive = RenderTexture.active;
            try
            {
                cameraObject = new GameObject("Metric_Probe_Capture_Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, probe.gameObject.scene);
                cameraObject.transform.position = probe.transform.position;
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false; camera.allowHDR = true; camera.allowMSAA = false;
                camera.renderingPath = RenderingPath.Forward;
                camera.clearFlags = probe.clearFlags == ReflectionProbeClearFlags.Skybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                camera.backgroundColor = probe.backgroundColor; camera.cullingMask = probe.cullingMask;
                camera.nearClipPlane = Mathf.Max(.01f, probe.nearClipPlane); camera.farClipPlane = probe.farClipPlane;
                camera.useOcclusionCulling = false;
                target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                { dimension = TextureDimension.Cube, useMipMap = false, autoGenerateMips = false, antiAliasing = 1 };
                if (!target.Create() || !camera.RenderToCubemap(target, 63))
                    throw new InvalidOperationException("Graphics device could not capture " + probe.name);
                face = new Texture2D(Resolution, Resolution, TextureFormat.RGBAHalf, false, true);
                cube = new Cubemap(Resolution, TextureFormat.RGBAHalf, true)
                { name = "MetricCity_" + probe.name, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
                for (int side = 0; side < 6; side++)
                {
                    Graphics.SetRenderTarget(target, 0, (CubemapFace)side);
                    face.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0, false);
                    face.Apply(false, false);
                    Color[] pixels = face.GetPixels();
                    maximumRadiance = Mathf.Max(maximumRadiance, ValidatePixels(pixels, probe.name, side));
                    // This is unencoded linear RGBAHalf, never RGBM radiance in alpha.
                    // Forward materials may preserve their authored alpha; discard it
                    // so a subsequent reflection decoder cannot exponentiate it.
                    for (int pixel = 0; pixel < pixels.Length; pixel++) pixels[pixel].a = 1;
                    cube.SetPixels(pixels, (CubemapFace)side);
                }
                cube.Apply(true, false);
                Cubemap existing = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                if (existing)
                {
                    EditorUtility.CopySerialized(cube, existing); EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssetIfDirty(existing);
                    return existing;
                }
                AssetDatabase.CreateAsset(cube, path);
                Cubemap saved = cube; cube = null;
                AssetDatabase.SaveAssetIfDirty(saved);
                return saved;
            }
            finally
            {
                RenderTexture.active = oldActive;
                if (target) { target.Release(); Object.DestroyImmediate(target); }
                if (face) Object.DestroyImmediate(face);
                if (cube) Object.DestroyImmediate(cube);
                if (cameraObject) Object.DestroyImmediate(cameraObject);
            }
        }

        private static float ValidateImportedCubemap(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            bool readable = importer && importer.isReadable;
            try
            {
                if (importer && !readable) { importer.isReadable = true; importer.SaveAndReimport(); }
                Cubemap cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                if (!cube || !cube.isReadable) throw new InvalidOperationException("Cannot verify captured probe HDR: " + path);
                float peak = 0;
                for (int side = 0; side < 6; side++) peak = Mathf.Max(peak, ValidatePixels(cube.GetPixels((CubemapFace)side), path, side));
                return peak;
            }
            finally
            {
                if (importer && importer.isReadable != readable) { importer.isReadable = readable; importer.SaveAndReimport(); }
            }
        }

        private static float ValidatePixels(Color[] pixels, string label, int side)
        {
            float peak = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                Color pixel = pixels[index];
                if (!Finite(pixel.r) || !Finite(pixel.g) || !Finite(pixel.b) || !Finite(pixel.a))
                    throw new InvalidOperationException("Nonfinite HDR probe capture rejected before installation: " + label + " face " + side + " pixel " + index);
                peak = Mathf.Max(peak, Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)));
            }
            return peak;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static IEnumerable<Transform> SceneObjects(Scene scene)
        {
            // IProcessSceneWithReport exposes a valid temporary scene before isLoaded becomes true.
            if (!scene.IsValid()) throw new InvalidOperationException("Metric reflection scene is unavailable.");
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true));
        }
        private static ReflectionProbe[] FindProbes(Scene scene)
        {
            ReflectionProbe[] probes = SceneObjects(scene).SelectMany(item => item.GetComponents<ReflectionProbe>())
                .Where(item => Names.Contains(item.name)).OrderBy(item => item.name, StringComparer.Ordinal).ToArray();
            if (probes.Length != Names.Length || probes.Select(item => item.name).Distinct().Count() != Names.Length)
                throw new InvalidOperationException("Expected the seven distinct metric city/subway reflection probes.");
            return probes;
        }
        [Serializable] private sealed class Manifest
        {
            public string capturedAtUtc, scenePath, unityVersion;
            public int resolution;
            public List<Entry> probes = new List<Entry>();
        }
        [Serializable] private sealed class Entry
        {
            public string name, texturePath, method;
            public bool finiteHdrValidated;
            public float maximumRadiance;
            public Vector3 position, influenceSize, influenceCenter;
        }
    }
}
#endif
