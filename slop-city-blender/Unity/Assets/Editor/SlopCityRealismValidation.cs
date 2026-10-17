#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Saved-asset integrity and rendered pixel checks for the physical-surface city pass.</summary>
    public static class SlopCityRealismValidation
    {
        public const string ReportPath = "Builds/Reports/realism-validation.json";
        public const string CaptureReportPath = "Builds/Reports/realism-capture-check.json";

        public static void ValidateSavedScene()
        {
            Scene scene = EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
            Report report = Validate(scene);
            Save(ReportPath, report);
            if (!report.passed) throw new InvalidOperationException("Realism validation failed:\n" + string.Join("\n", report.errors));
            Debug.Log("SLOP_CITY_REALISM_VALIDATION_SUCCEEDED " + report.checks + " checks; " + ReportPath);
        }

        public static Report Validate(Scene scene)
        {
            var report = new Report { checkedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion, scenePath = scene.path };
            Transform[] nodes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            Renderer[] renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
            Material[] materials = renderers.SelectMany(renderer => renderer.sharedMaterials).Where(material => material).Distinct().ToArray();
            Material[] scanned = materials.Where(SlopCitySurfaceLibrary.IsScanned).ToArray();
            report.scannedMaterials = scanned.Length;
            Check(report, scanned.Length >= 10, "Saved scene must use photographic surfaces on both architecture and furnishings.");
            var checkedPacked = new HashSet<string>();
            foreach (Material material in scanned)
            {
                string label = AssetDatabase.GetAssetPath(material);
                Check(report, material.shader && material.shader.name == "Standard" && material.shader.isSupported,
                    "Scanned material has no supported Standard shader: " + label);
                var albedo = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material.mainTexture)) as TextureImporter;
                Texture normalMap = material.GetTexture("_BumpMap");
                var normal = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normalMap)) as TextureImporter;
                Texture packedMap = material.GetTexture("_MetallicGlossMap");
                string packedPath = AssetDatabase.GetAssetPath(packedMap);
                var packed = AssetImporter.GetAtPath(packedPath) as TextureImporter;
                Check(report, albedo && albedo.sRGBTexture && albedo.mipmapEnabled && albedo.anisoLevel >= 4,
                    "Albedo must use sRGB with mipmapped anisotropic filtering: " + label);
                Check(report, normalMap && normal && normal.textureType == TextureImporterType.NormalMap
                    && !normal.sRGBTexture && !normal.flipGreenChannel && !normal.convertToNormalmap
                    && material.IsKeywordEnabled("_NORMALMAP"), "OpenGL normal map import/keyword is incorrect: " + label);
                Check(report, packedMap && packed && !packed.sRGBTexture && material.IsKeywordEnabled("_METALLICGLOSSMAP")
                    && material.GetFloat("_SmoothnessTextureChannel") == 0,
                    "Linear metallic/smoothness alpha map is missing or incorrectly bound: " + label);
                Check(report, material.mainTextureScale.x > 0 && material.mainTextureScale.y > 0
                    && material.mainTextureScale.x <= 10 && material.mainTextureScale.y <= 10,
                    "Material has invalid physical repeat scale: " + label);
                if (!string.IsNullOrEmpty(packedPath) && checkedPacked.Add(packedPath)) ValidatePackedAlpha(report, packedPath);
            }
            report.packedSurfaceSets = checkedPacked.Count;
            Check(report, checkedPacked.Count >= 9, "At least nine distinct physical surface sets must be used in the saved world.");
            ValidateSkyAndCamera(report, scene);
            ValidateRoadUV(report, nodes);
            ValidateGeometry(report, nodes);
            ValidateProvenance(report);
            report.passed = report.errors.Count == 0;
            return report;
        }

        private static void ValidateSkyAndCamera(Report report, Scene scene)
        {
            Material sky = RenderSettings.skybox;
            Cubemap cube = sky && sky.HasProperty("_Tex") ? sky.GetTexture("_Tex") as Cubemap : null;
            var skyImporter = AssetImporter.GetAtPath(SlopCitySurfaceLibrary.SkyPath) as TextureImporter;
            Check(report, sky && sky.shader && sky.shader.name == "Skybox/Cubemap" && sky.shader.isSupported
                && cube && cube.width >= 128, "Saved sky must render the actual photographic HDR cubemap.");
            Check(report, cube && AssetDatabase.GetAssetPath(cube) == SlopCitySurfaceLibrary.SkyPath
                && skyImporter && skyImporter.textureShape == TextureImporterShape.TextureCube && !skyImporter.sRGBTexture,
                "HDR sky cubemap must remain linear and retain its source asset after scene reload.");
            Check(report, RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom
                && RenderSettings.customReflectionTexture == cube, "Default environmental reflection must use the saved scanned HDR sky.");
            Camera camera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .FirstOrDefault(item => item.CompareTag("MainCamera"));
            SlopCityCameraFinish finish = camera ? camera.GetComponent<SlopCityCameraFinish>() : null;
            Check(report, camera && camera.allowHDR && camera.clearFlags == CameraClearFlags.Skybox && finish && finish.enabled,
                "Saved player camera requires HDR, photographic sky and the enabled camera finish.");
            Check(report, camera && camera.renderingPath == RenderingPath.DeferredShading && !camera.allowMSAA,
                "Saved player camera requires deferred per-pixel lighting and post antialiasing.");
            if (finish)
            {
                Shader shader = new SerializedObject(finish).FindProperty("finishShader").objectReferenceValue as Shader;
                Check(report, shader && shader.name == "Hidden/SlopCity/CameraFinish" && shader.isSupported,
                    "Player camera finish must retain a supported serialized shader reference.");
                if (shader)
                {
                    var effect = new Material(shader);
                    // Native rendering uses passes 0–7; the browser-only finish is appended at 8.
                    try { Check(report, !ShaderUtil.ShaderHasError(shader) && effect.passCount == 9,
                        "Camera finish requires all eight native rendering passes and the appended browser pass."); }
                    finally { Object.DestroyImmediate(effect); }
                }
            }
            ReflectionProbe[] probes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ReflectionProbe>(true)).ToArray();
            report.bakedReflectionProbes = probes.Count(probe => probe.mode == ReflectionProbeMode.Custom && probe.customBakedTexture);
            Check(report, probes.Length == 8 && report.bakedReflectionProbes == 8
                && probes.Count(probe => probe.name.StartsWith("Junction_Reflection_", StringComparison.Ordinal)) == 4,
                "Street, four junctions, lounge and both subway stations require eight persisted reflection textures.");
            foreach (ReflectionProbe probe in probes)
                Check(report, probe.customBakedTexture && AssetDatabase.Contains(probe.customBakedTexture) && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(probe.customBakedTexture))
                    && probe.resolution >= 256 && probe.hdr, "Reflection probe contains an unsaved or low-quality texture: " + probe.name);
        }

        private static void ValidateRoadUV(Report report, Transform[] nodes)
        {
            Transform road = nodes.FirstOrDefault(node => node.name == "City_Asphalt");
            MeshFilter filter = road ? road.GetComponent<MeshFilter>() : null;
            Renderer renderer = road ? road.GetComponent<Renderer>() : null;
            Check(report, filter && filter.sharedMesh && renderer && SlopCitySurfaceLibrary.IsScanned(renderer.sharedMaterial),
                "Main road requires a saved mesh and scanned asphalt material.");
            if (!filter || !filter.sharedMesh || !renderer) return;
            Mesh mesh = filter.sharedMesh;
            Vector2[] uv = mesh.uv; Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            Check(report, AssetDatabase.Contains(mesh) && uv.Length == mesh.vertexCount && mesh.tangents.Length == mesh.vertexCount,
                "Metric road UV mesh and its tangent basis must be persisted.");
            if (uv.Length != vertices.Length || normals.Length != vertices.Length) return;
            float maximumError = 0f; int topPairs = 0;
            for (int a = 0; a < vertices.Length; a++)
            {
                if (normals[a].y < .9f) continue;
                for (int b = a + 1; b < vertices.Length; b++)
                {
                    if (normals[b].y < .9f) continue;
                    float metres = Vector3.Distance(road.TransformPoint(vertices[a]), road.TransformPoint(vertices[b]));
                    if (metres < .1f) continue;
                    maximumError = Mathf.Max(maximumError, Mathf.Abs(Vector2.Distance(uv[a], uv[b]) - metres));
                    topPairs++;
                }
            }
            report.maximumRoadUVMetreError = maximumError;
            Check(report, topPairs > 0 && maximumError < .025f,
                "Road texture coordinates must measure real metres instead of stretching one UV tile over the entire city.");
            Vector2 repeat = renderer.sharedMaterial.mainTextureScale;
            Check(report, Mathf.Abs(repeat.x - 1f / 3f) < .001f && Mathf.Abs(repeat.y - 1f / 3f) < .001f,
                "Asphalt repeats must use the scan's measured 3m tile size.");
        }

        private static void ValidateGeometry(Report report, Transform[] nodes)
        {
            MeshFilter[] filters = nodes.Select(node => node.GetComponent<MeshFilter>()).Where(filter => filter && filter.sharedMesh).ToArray();
            Mesh[] beveled = filters.Select(filter => filter.sharedMesh).Where(mesh => mesh.name.StartsWith("Bevel_", StringComparison.Ordinal)
                || mesh.name.StartsWith("BevelV2_", StringComparison.Ordinal)).Distinct().ToArray();
            report.beveledMeshAssets = beveled.Length;
            Check(report, beveled.Length >= 10, "Street fixtures and architecture require distinct persisted bevelled meshes.");
            foreach (Mesh mesh in beveled)
                Check(report, AssetDatabase.Contains(mesh) && mesh.vertexCount > 24 && mesh.uv.Length == mesh.vertexCount
                    && mesh.normals.Length == mesh.vertexCount && mesh.tangents.Length == mesh.vertexCount
                    && mesh.normals.Any(normal => Mathf.Abs(normal.x) > .05f && Mathf.Abs(normal.y) > .05f),
                    "Bevel mesh must contain real rounded geometry, normals, UVs and tangents: " + mesh.name);
            Transform[] cars = nodes.Where(node => node.name == "Parked_CityCar").ToArray();
            report.detailedVehicles = cars.Length;
            Check(report, cars.Length == 4, "Four detailed city vehicles must remain in the saved world.");
            foreach (Transform car in cars)
            {
                MeshFilter body = car.GetComponentsInChildren<MeshFilter>().FirstOrDefault(filter => filter.name == "Car_Body");
                MeshFilter[] wheels = car.GetComponentsInChildren<MeshFilter>().Where(filter => filter.name == "Car_Wheel").ToArray();
                Check(report, body && body.sharedMesh && AssetDatabase.Contains(body.sharedMesh) && body.sharedMesh.vertexCount > 100
                    && body.sharedMesh.name.StartsWith("Sedan_", StringComparison.Ordinal), "Vehicle must use a saved contoured body rather than its former box: " + Hierarchy(car));
                Check(report, wheels.Length == 4 && wheels.All(wheel => wheel.sharedMesh && AssetDatabase.Contains(wheel.sharedMesh)
                    && wheel.sharedMesh.vertexCount > 100), "Vehicle requires four saved radial tyre meshes: " + Hierarchy(car));
                Check(report, car.Find("Car_Cabin_Glazing") && car.Find("Car_Interior"), "Vehicle glazing and detailed interior are missing: " + Hierarchy(car));
            }
            MeshFilter[] trunks = filters.Where(filter => filter.name == "Tree_Trunk").ToArray();
            report.branchingTrees = trunks.Length;
            Check(report, trunks.Length >= 9 && trunks.All(trunk => AssetDatabase.Contains(trunk.sharedMesh)
                && trunk.sharedMesh.name.StartsWith("StreetTree_Branches_", StringComparison.Ordinal) && trunk.sharedMesh.vertexCount > 200),
                "District trees require saved branching silhouettes instead of cylinder trunks.");
            MeshFilter[] leaves = filters.Where(filter => filter.name == "Tree_Canopy").ToArray();
            Check(report, leaves.Length == trunks.Length && leaves.All(leaf => AssetDatabase.Contains(leaf.sharedMesh)
                && leaf.sharedMesh.name.StartsWith("StreetTree_Foliage_", StringComparison.Ordinal) && leaf.sharedMesh.vertexCount > 1000),
                "Each branching tree requires a persisted individual-leaf canopy.");
            Check(report, nodes.Count(node => node.name == "Dystopian_Infrastructure") == 9
                && nodes.Any(node => node.name == "Utility_Junction") && nodes.Any(node => node.name.Contains("Public_Notice")),
                "All districts require saved utility infrastructure and public notices.");
            Transform military = nodes.FirstOrDefault(node => node.name == "City_MilitarySciFiArchitecture");
            try { report.checks += SlopCityMilitarySciFiBuilder.Validate(military ? military.parent : null); }
            catch (Exception exception) { Check(report, false, exception.Message); }
            Transform habitat = nodes.FirstOrDefault(node => node.name == "SlopCity_HabitatInterior");
            Check(report, habitat && habitat.GetComponentsInChildren<MeshFilter>().All(filter => filter.sharedMesh
                && AssetDatabase.Contains(filter.sharedMesh)) && habitat.GetComponentsInChildren<Collider>(true).Length == 0,
                "Habitat geometry must survive scene reload without changing the authored interior collision.");
        }

        private static void ValidatePackedAlpha(Report report, string path)
        {
            string roughnessPath = System.Text.RegularExpressions.Regex.Replace(path,
                @"(?:_metal\d+)?_metallicSmoothness\.png$", "_roughness.jpg");
            Check(report, File.Exists(path) && File.Exists(roughnessPath), "Generated smoothness map or source roughness is missing: " + path);
            if (!File.Exists(path) || !File.Exists(roughnessPath)) return;
            var roughness = new Texture2D(2, 2, TextureFormat.RGB24, false, true);
            var packed = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                bool loaded = ImageConversion.LoadImage(roughness, File.ReadAllBytes(roughnessPath))
                    && ImageConversion.LoadImage(packed, File.ReadAllBytes(path));
                Check(report, loaded && roughness.width == packed.width && roughness.height == packed.height,
                    "Packed roughness-derived map has the wrong image dimensions: " + path);
                if (!loaded || roughness.width != packed.width || roughness.height != packed.height) return;
                Color32[] source = roughness.GetPixels32(), target = packed.GetPixels32();
                bool correct = true;
                for (int index = 0; index < source.Length; index++)
                    if (Math.Abs(target[index].a - (255 - source[index].r)) > 1) { correct = false; break; }
                Check(report, correct, "Smoothness alpha must equal one minus source roughness, not source roughness itself: " + path);
            }
            finally { Object.DestroyImmediate(roughness); Object.DestroyImmediate(packed); }
        }

        private static void ValidateProvenance(Report report)
        {
            string manifestPath = SlopCitySurfaceLibrary.Root + "/provenance.json";
            Check(report, File.Exists(manifestPath) && File.Exists(SlopCitySurfaceLibrary.Root + "/PROVENANCE.txt"),
                "The distributed scan sources require their saved license/provenance manifest.");
            if (!File.Exists(manifestPath)) return;
            Provenance manifest = JsonUtility.FromJson<Provenance>(File.ReadAllText(manifestPath));
            Check(report, manifest != null && manifest.license == "CC0-1.0" && manifest.licenseUrl == "https://polyhaven.com/license"
                && manifest.provider == "Poly Haven" && manifest.assets != null && manifest.assets.Length >= 13,
                "Source provenance must identify Poly Haven, CC0 and every photographic surface/sky set.");
            if (manifest == null || manifest.assets == null) return;
            using (SHA256 hash = SHA256.Create())
            {
                foreach (SourceAsset source in manifest.assets)
                {
                    if (source.maps == null) continue;
                    foreach (SourceMap map in source.maps)
                    {
                        string path = Path.Combine(SlopCitySurfaceLibrary.Root, map.file);
                        bool valid = File.Exists(path) && new FileInfo(path).Length == map.bytes;
                        if (valid)
                        {
                            using (FileStream file = File.OpenRead(path))
                                valid = BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant() == map.sha256;
                        }
                        Check(report, valid && map.url.StartsWith("https://dl.polyhaven.org/", StringComparison.Ordinal),
                            "A photographic source file is missing, modified or lacks the documented source URL: " + map.file);
                        report.sourceFilesChecked++;
                    }
                }
            }
        }

        /// <summary>Render the saved scene with the actual finish, then validate floating-point output before PNG conversion.</summary>
        public static void RenderCapture()
        {
            Scene scene = EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
            Camera playerCamera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .FirstOrDefault(camera => camera.CompareTag("MainCamera"));
            if (!playerCamera) throw new InvalidOperationException("Saved player camera is missing.");
            var report = new CaptureReport { checkedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                scenePath = scene.path, graphicsDevice = SystemInfo.graphicsDeviceType.ToString(), colorSpace = QualitySettings.activeColorSpace.ToString() };
            var owner = new GameObject("Dystopia_AssetReviewCamera");
            foreach (Material material in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .SelectMany(renderer => renderer.sharedMaterials).Where(material => material).Distinct())
                if (!material.shader || !material.shader.isSupported || material.shader.name == "Hidden/InternalErrorShader"
                    || ShaderUtil.ShaderHasError(material.shader))
                    report.errors.Add("Unsupported or erroneous scene shader: " + material.name);
            Camera camera = owner.AddComponent<Camera>();
            camera.CopyFrom(playerCamera);
            camera.clearFlags = CameraClearFlags.Skybox; camera.nearClipPlane = .04f; camera.farClipPlane = 400;
            camera.fieldOfView = 67; camera.allowHDR = true;
            var finish = owner.AddComponent<SlopCityCameraFinish>();
            SlopCityCameraFinish playerFinish = playerCamera.GetComponent<SlopCityCameraFinish>();
            if (playerFinish) EditorUtility.CopySerialized(playerFinish, finish);
            finish.Configure(Shader.Find("Hidden/SlopCity/CameraFinish"));
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../renders"));
            Directory.CreateDirectory(folder);
            try
            {
                CaptureFrame(camera, new Vector3(20,1.75f,-12), new Vector3(-6,26,35), folder, "13-dystopia-street.png", report);
                CaptureFrame(camera, new Vector3(20,1.7f,-12), new Vector3(12,2,38), folder, "14-dystopia-streetlevel.png", report);
                CaptureFrame(camera, new Vector3(-14,1.7f,-45), new Vector3(-9,1.5f,-39), folder, "15-dystopia-fuel.png", report);
                CaptureFrame(camera, new Vector3(-49,-6.2f,15.9f), new Vector3(-46.3f,-6.25f,23.5f), folder, "16-dystopia-subway.png", report);
                CaptureFrame(playerCamera, playerCamera.transform.position, playerCamera.transform.position + playerCamera.transform.forward,
                    folder, "17-dystopia-lounge.png", report);
                Transform car = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>())
                    .Where(node => node.name == "Parked_CityCar").OrderBy(node => node.position.z).FirstOrDefault();
                if (!car) throw new InvalidOperationException("No vehicle exists for the detailed body/interior render.");
                camera.fieldOfView = 52;
                CaptureFrame(camera, car.TransformPoint(new Vector3(-3.4f,1.65f,4.25f)), car.TransformPoint(new Vector3(0,.82f,0)),
                    folder, "18-dystopia-vehicle.png", report);
                camera.fieldOfView = SlopCitySceneBuilder.PlayerVerticalFieldOfView;
                Vector3 driver = car.TransformPoint(new Vector3(-.22f,1.25f,-.1f));
                CaptureFrame(camera, driver, driver + car.forward * 10, folder, "19-dystopia-driver.png", report);
                Transform rooftop = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>())
                    .Where(node => node.name.StartsWith("Tower_Roof_")).OrderByDescending(node => node.position.y).First();
                camera.fieldOfView = 67;
                CaptureFrame(camera, rooftop.position + Vector3.up * 1.66f, new Vector3(-20,18,-28), folder, "20-dystopia-rooftop.png", report);
                CaptureFrame(camera, new Vector3(20,1.7f,-10), new Vector3(20,3.2f,40), folder, "21-megacity-avenue.png", report);
                CaptureFrame(camera, new Vector3(14,1.7f,-10), new Vector3(14,1.6f,10), folder, "22-megacity-promenade.png", report);
                CaptureFrame(camera, new Vector3(40,1.7f,-12), new Vector3(40,2,8), folder, "23-megacity-market.png", report);
                CapturePerson(camera, SlopCityNpcRole.Commuter, folder, "24-civilian-detail.png", report);
                CapturePerson(camera, SlopCityNpcRole.Security, folder, "25-security-detail.png", report);
            }
            catch (Exception exception) { report.errors.Add(exception.ToString()); }
            finally { Object.DestroyImmediate(owner); }
            report.passed = report.errors.Count == 0 && report.frames.Count == 13 && report.frames.All(frame => frame.passed);
            Save(CaptureReportPath, report);
            if (!report.passed) throw new InvalidOperationException("Realism render validation failed. See " + CaptureReportPath);
            Debug.Log("SLOP_CITY_REALISM_CAPTURES_COMPLETE " + folder);
        }

        private static void CaptureFrame(Camera camera, Vector3 position, Vector3 target, string folder, string filename, CaptureReport report)
        {
            const int width = 1600, height = 1000;
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
                throw new NotSupportedException("Floating-point rendering/readback is required for nonfinite pixel validation.");
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            var buffer = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Default)
                { antiAliasing = 1 };
            var resolved = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var ldr = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var raw = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            var png = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture oldTarget = camera.targetTexture;
            var evidence = new FrameEvidence { file = Path.Combine(folder, filename), position = position, target = target, fieldOfView = camera.fieldOfView };
            report.frames.Add(evidence);
            try
            {
                camera.targetTexture = buffer;
                camera.Render();
                // Inspect finished HDR pixels before quantization can hide NaN/Inf.
                Graphics.Blit(buffer, resolved);
                RenderTexture.active = resolved;
                raw.ReadPixels(new Rect(0, 0, width, height), 0, 0); raw.Apply();
                Color[] pixels = raw.GetPixels();
                double sum = 0, squares = 0; int black = 0, white = 0, magenta = 0;
                for (int index = 0; index < pixels.Length; index++)
                {
                    Color color = pixels[index];
                    if (!Finite(color.r) || !Finite(color.g) || !Finite(color.b) || !Finite(color.a))
                    { evidence.nonfinitePixels++; continue; }
                    if (color.r > .65f && color.b > .60f && color.g < .18f && color.r > color.g * 3f && color.b > color.g * 3f) magenta++;
                    double luma = color.r * .2126 + color.g * .7152 + color.b * .0722;
                    sum += luma; squares += luma * luma;
                    if (luma < .003) black++;
                    if (color.r > .98f && color.g > .98f && color.b > .98f) white++;
                }
                evidence.meanLinearLuminance = sum / pixels.Length;
                evidence.luminanceVariance = squares / pixels.Length - evidence.meanLinearLuminance * evidence.meanLinearLuminance;
                evidence.blackFraction = black / (double)pixels.Length;
                evidence.whiteFraction = white / (double)pixels.Length;
                evidence.magentaFraction = magenta / (double)pixels.Length;
                // Magenta is an intentional neon-sign color in this scene. Retain its measured
                // fraction for diagnostics; validate actual shader support above instead of
                // mistaking authored pink lighting for Unity's missing-shader material.
                evidence.passed = evidence.nonfinitePixels == 0
                    && evidence.blackFraction < .93 && evidence.whiteFraction < .98
                    && evidence.meanLinearLuminance > .008 && evidence.luminanceVariance > .0001;
                // Write the tonemapped result through a normal LDR target so the PNG has display gamma.
                bool previousSRGB = GL.sRGBWrite;
                try
                {
                    GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(resolved, ldr);
                }
                finally { GL.sRGBWrite = previousSRGB; }
                RenderTexture.active = ldr;
                png.ReadPixels(new Rect(0, 0, width, height), 0, 0); png.Apply();
                File.WriteAllBytes(evidence.file, png.EncodeToPNG());
                if (!evidence.passed) report.errors.Add("Pixel validation failed for " + filename
                    + ": nonfinite=" + evidence.nonfinitePixels + ", magenta=" + evidence.magentaFraction
                    + ", black=" + evidence.blackFraction + ", white=" + evidence.whiteFraction
                    + ", mean=" + evidence.meanLinearLuminance + ", variance=" + evidence.luminanceVariance);
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = previous;
                buffer.Release(); resolved.Release(); ldr.Release();
                Object.DestroyImmediate(buffer); Object.DestroyImmediate(resolved); Object.DestroyImmediate(ldr);
                Object.DestroyImmediate(raw); Object.DestroyImmediate(png);
            }
        }

        private static void CapturePerson(Camera camera, SlopCityNpcRole role, string folder, string filename, CaptureReport report)
        {
            SlopCityCrowd crowd = Object.FindFirstObjectByType<SlopCityCrowd>();
            if (!crowd) throw new InvalidOperationException("No crowd exists for character render review.");
            for (int index = 0; index < crowd.PopulationCount; index++)
            {
                SlopCityNpcSnapshot person = crowd.Snapshot(index);
                if (person.role != role || person.position.y < 0) continue;
                Vector3 target = person.position + Vector3.up * 1.05f;
                Vector3 front = Quaternion.Euler(0, person.heading + 16, 0) * Vector3.forward;
                Vector3 eye = person.position + Vector3.up * 1.48f + front * 2.7f;
                if (Physics.Linecast(target, eye, ~0, QueryTriggerInteraction.Ignore)) continue;
                bool obscured = false;
                for (int otherIndex = 0; otherIndex < crowd.PopulationCount && !obscured; otherIndex++)
                {
                    if (otherIndex == index) continue;
                    Vector3 other = crowd.Snapshot(otherIndex).position + Vector3.up * 1.1f;
                    Vector3 sight = eye - target;
                    float along = Mathf.Clamp01(Vector3.Dot(other-target, sight) / sight.sqrMagnitude);
                    obscured = Vector3.Distance(other, target + sight * along) < .70f;
                }
                if (obscured) continue;
                camera.fieldOfView = 49;
                CaptureFrame(camera, eye, target, folder, filename, report);
                return;
            }
            throw new InvalidOperationException("No unobstructed character review position for " + role);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static string Hierarchy(Transform node) => node.parent ? Hierarchy(node.parent) + "/" + node.name : node.name;
        private static void Check(Report report, bool condition, string failure)
        { report.checks++; if (!condition) report.errors.Add(failure); }
        private static void Save(string path, object report)
        { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllText(path, JsonUtility.ToJson(report, true)); }

        [Serializable] public sealed class Report
        {
            public bool passed;
            public string checkedAtUtc, unityVersion, scenePath;
            public string scope = "Freshly opened saved scene: photographic map imports and smoothness data, HDR/camera finish, metric road UVs, persisted detailed geometry, baked reflections, and source-license/hash integrity. Gameplay behavior is covered by separate smoke tests.";
            public int checks, scannedMaterials, packedSurfaceSets, bakedReflectionProbes, beveledMeshAssets, detailedVehicles, branchingTrees, sourceFilesChecked;
            public float maximumRoadUVMetreError;
            public List<string> errors = new List<string>();
        }
        [Serializable] private sealed class CaptureReport
        {
            public bool passed;
            public string checkedAtUtc, unityVersion, scenePath, graphicsDevice, colorSpace;
            public string scope = "Saved-scene camera renders with camera finish; actual material shader checks plus pre-PNG float readback checks NaN/infinity, mostly black/white and flat output. Magenta fraction is diagnostic because pink neon is intentional. Thirteen PNGs are generated for visual review, including civilian and security close-ups. This is not a screenshot of a running player and does not prove subjective photorealism.";
            public List<FrameEvidence> frames = new List<FrameEvidence>();
            public List<string> errors = new List<string>();
        }
        [Serializable] private sealed class FrameEvidence
        {
            public bool passed;
            public string file;
            public Vector3 position, target;
            public float fieldOfView;
            public int nonfinitePixels;
            public double meanLinearLuminance, luminanceVariance, blackFraction, whiteFraction, magentaFraction;
        }
        [Serializable] private sealed class Provenance { public string license, licenseUrl, provider; public SourceAsset[] assets; }
        [Serializable] private sealed class SourceAsset { public SourceMap[] maps; }
        [Serializable] private sealed class SourceMap { public string file, sha256, url; public long bytes; }
    }
}
#endif
