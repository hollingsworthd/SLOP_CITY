#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SlopCity.Editor
{
    /// <summary>Imports the bundled CC0 Poly Haven scans into Unity's Standard PBR workflow.</summary>
    public static class SlopCitySurfaceLibrary
    {
        public const string Root = "Assets/Art/Surfaces";
        public const string SkyPath = Root + "/kloofendal_48d_partly_cloudy_puresky_1k.hdr";
        private static readonly HashSet<string> Prepared = new HashSet<string>();

        /// <summary>
        /// Apply after procedural/base material setup. UVs should represent metres on generated city geometry.
        /// The tiling is the reciprocal of the scan's measured physical width/height, not repeats per object.
        /// Returns false without changing unrecognized material types.
        /// </summary>
        public static bool Apply(Material material, string surfaceName, bool preserveBaseColor = false)
        {
            if (!material) return false;
            if (!TrySurface(surfaceName, out Surface surface)
                && !TrySurface(Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(material)), out surface)) return false;
            Color baseColor = material.color;
            Texture2D albedo = surface.albedoVariant == AlbedoVariant.Original
                ? ImportMap(surface.id + "_albedo.jpg", true, false) : GetRestrainedAlbedo(surface.id, surface.albedoVariant);
            string normalFilename = surface.id + "_normalGL.png";
            if (!File.Exists(Root + "/" + normalFilename)) normalFilename = surface.id + "_normalGL.jpg";
            Texture2D normal = ImportMap(normalFilename, false, true);
            Texture2D occlusion = ImportMap(surface.id + "_occlusion.jpg", false, false);
            Texture2D metallicSmoothness = GetMetallicSmoothness(surface.id, surface.metalness);
            if (!albedo || !normal || !metallicSmoothness)
                throw new InvalidOperationException("Missing bundled photoscan maps for " + surface.id + ".");
            Shader standard = Shader.Find("Standard");
            if (!standard) throw new InvalidOperationException("Standard PBR shader is required for city surfaces.");
            material.shader = standard;
            material.mainTexture = albedo;
            material.mainTextureScale = new Vector2(1f / surface.width, 1f / surface.height);
            material.mainTextureOffset = Vector2.zero;
            material.color = preserveBaseColor || surface.preserveTint ? baseColor : surface.tint;
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", surface.normalStrength);
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap", metallicSmoothness);
            material.SetFloat("_Metallic", 0f); // Metalness, where supplied, is read from the packed map.
            material.SetFloat("_GlossMapScale", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.EnableKeyword("_METALLICGLOSSMAP");
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            material.SetTexture("_OcclusionMap", occlusion);
            material.SetFloat("_OcclusionStrength", .75f);
            material.SetFloat("_SpecularHighlights", 1f);
            material.SetFloat("_GlossyReflections", 1f);
            material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.DisableKeyword("_GLOSSYREFLECTIONS_OFF");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return true;
        }

        public static bool ConfigureStandard(Material material, string surfaceName) => Apply(material, surfaceName);

        public static bool IsScanned(Material material)
        {
            if (!material || !material.mainTexture) return false;
            return AssetDatabase.GetAssetPath(material.mainTexture).StartsWith(Root + "/", StringComparison.Ordinal)
                && material.mainTexture.name.EndsWith("_albedo", StringComparison.Ordinal);
        }

        /// <summary>Measured scan size in metres, suitable for diagnostics and UV-authoring tools.</summary>
        public static Vector2 PhysicalSize(string surfaceName)
        {
            return TrySurface(surfaceName, out Surface surface) ? new Vector2(surface.width, surface.height) : Vector2.one;
        }

        /// <summary>Imports the real daytime HDR panorama as a cubemap for both sky and reflections.</summary>
        public static Cubemap GetSkyCubemap()
        {
            if (!File.Exists(SkyPath)) return null;
            AssetDatabase.ImportAsset(SkyPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(SkyPath) as TextureImporter;
            if (!importer) return null;
            if (importer.textureShape != TextureImporterShape.TextureCube || importer.sRGBTexture
                || importer.generateCubemap != TextureImporterGenerateCubemap.AutoCubemap
                || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.TextureCube;
                importer.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Cubemap>(SkyPath);
        }

        private static Texture2D ImportMap(string filename, bool srgb, bool normal)
        {
            string path = Root + "/" + filename;
            if (!File.Exists(path)) return null;
            if (Prepared.Add(path))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!importer) return null;
                TextureImporterType type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType != type || importer.sRGBTexture != srgb || !importer.mipmapEnabled
                    || importer.maxTextureSize != 1024 || importer.anisoLevel != 8 || importer.isReadable
                    || importer.wrapMode != TextureWrapMode.Repeat || importer.filterMode != FilterMode.Trilinear
                    || (normal && (importer.convertToNormalmap || importer.flipGreenChannel)))
                {
                    importer.textureType = type;
                    importer.textureShape = TextureImporterShape.Texture2D;
                    importer.sRGBTexture = srgb;
                    importer.mipmapEnabled = true;
                    importer.maxTextureSize = 1024;
                    importer.anisoLevel = 8;
                    importer.filterMode = FilterMode.Trilinear;
                    importer.wrapMode = TextureWrapMode.Repeat;
                    importer.isReadable = false;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    if (normal)
                    {
                        // Poly Haven nor_gl is tangent-space +Y, the convention expected by Unity.
                        // It is a real normal map, not a grayscale height map to be converted.
                        importer.convertToNormalmap = false;
                        importer.flipGreenChannel = false;
                    }
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D GetRestrainedAlbedo(string id, AlbedoVariant variant)
        {
            string sourcePath = Root + "/" + id + "_albedo.jpg";
            string derivedName = id + "_" + variant + "V1_albedo.png";
            string path = Root + "/" + derivedName;
            if (!File.Exists(sourcePath)) return null;
            if (!File.Exists(path) || File.GetLastWriteTimeUtc(sourcePath) > File.GetLastWriteTimeUtc(path))
            {
                var source = new Texture2D(2, 2, TextureFormat.RGB24, false);
                Texture2D derived = null;
                try
                {
                    if (!ImageConversion.LoadImage(source, File.ReadAllBytes(sourcePath)))
                        throw new InvalidOperationException("Cannot decode weathering scan: " + sourcePath);
                    Color32[] data = source.GetPixels32();
                    for (int index = 0; index < data.Length; index++)
                    {
                        Color color = data[index];
                        float luminance = color.r * .2126f + color.g * .7152f + color.b * .0722f;
                        if (variant == AlbedoVariant.RestrainedRust)
                        {
                            float gray = .62f + (luminance - .5f) * .18f;
                            color = Color.Lerp(new Color(gray, gray, gray), color, .12f);
                        }
                        else
                        {
                            // Surface wear remains photographed; remove the orange/white rust paint
                            // palette so authored steel/enamel colors are not replaced everywhere.
                            float contrast = variant == AlbedoVariant.NeutralConcrete ? .24f : .08f;
                            float gray = .84f + (luminance - .5f) * contrast;
                            color = new Color(gray, gray, gray);
                        }
                        data[index] = color;
                    }
                    derived = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
                    derived.SetPixels32(data); derived.Apply();
                    File.WriteAllBytes(path, derived.EncodeToPNG());
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    Prepared.Remove(path);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(source);
                    if (derived) UnityEngine.Object.DestroyImmediate(derived);
                }
            }
            return ImportMap(derivedName, true, false);
        }

        private static Texture2D GetMetallicSmoothness(string id, float metalnessOverride)
        {
            string roughnessPath = Root + "/" + id + "_roughness.jpg";
            string metalnessPath = Root + "/" + id + "_metalness.jpg";
            byte overrideValue = (byte)Mathf.RoundToInt(Mathf.Clamp01(metalnessOverride) * 255f);
            string packedName = id + (metalnessOverride >= 0 ? "_metal" + overrideValue : "") + "_metallicSmoothness.png";
            string packedPath = Root + "/" + packedName;
            if (!File.Exists(roughnessPath)) return null;
            if (!File.Exists(packedPath) || File.GetLastWriteTimeUtc(roughnessPath) > File.GetLastWriteTimeUtc(packedPath))
            {
                var roughness = new Texture2D(2, 2, TextureFormat.RGB24, false, true);
                Texture2D metalness = null;
                Texture2D packed = null;
                try
                {
                    // Load the data without sRGB decoding. Unity Standard expects smoothness in alpha.
                    if (!ImageConversion.LoadImage(roughness, File.ReadAllBytes(roughnessPath)))
                        throw new InvalidOperationException("Cannot decode roughness data: " + roughnessPath);
                    Color32[] data = roughness.GetPixels32();
                    Color32[] metals = null;
                    // Keep fabric/stone dielectric even if a scanned package contains a noisy "Metal" map.
                    if (id.Contains("metal") && File.Exists(metalnessPath))
                    {
                        metalness = new Texture2D(2, 2, TextureFormat.RGB24, false, true);
                        if (!ImageConversion.LoadImage(metalness, File.ReadAllBytes(metalnessPath)))
                            throw new InvalidOperationException("Cannot decode metalness data: " + metalnessPath);
                        metals = metalness.GetPixels32();
                        if (metals.Length != data.Length) throw new InvalidOperationException("Metalness map resolution does not match roughness: " + id);
                    }
                    for (int i = 0; i < data.Length; i++)
                        data[i] = new Color32(metalnessOverride >= 0 ? overrideValue : metals == null ? (byte)0 : metals[i].r,
                            0, 0, (byte)(255 - data[i].r));
                    packed = new Texture2D(roughness.width, roughness.height, TextureFormat.RGBA32, false, true);
                    packed.SetPixels32(data); packed.Apply();
                    File.WriteAllBytes(packedPath, packed.EncodeToPNG());
                    AssetDatabase.ImportAsset(packedPath, ImportAssetOptions.ForceSynchronousImport);
                    Prepared.Remove(packedPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(roughness);
                    if (metalness) UnityEngine.Object.DestroyImmediate(metalness);
                    if (packed) UnityEngine.Object.DestroyImmediate(packed);
                }
            }
            return ImportMap(packedName, false, false);
        }

        private static bool TrySurface(string name, out Surface surface)
        {
            string label = (name ?? "").Replace("City_", "").Replace("Metro_", "").Replace("Dystopia_", "").ToLowerInvariant();
            // Existing notice atlases and architectural/vehicle glazing have their own authored optics.
            if (label.Contains("notice") || label.Contains("glass") || label.Contains("glazing") || label.Contains("window"))
            { surface = default; return false; }
            // Authored lounge materials keep their palette; their real grain/weave supplies surface detail.
            if (label.Contains("floor board") || label.Contains("walnut") || label.StartsWith("wood ", StringComparison.Ordinal))
            { surface = new Surface("old_wood_floor", 3f, 3f, .4f, Color.white, true); return true; }
            if (label.Contains("upholstery") || label.Contains("fabric"))
            { surface = new Surface("denim_fabric_03", .2714f, .271f, .32f, Color.white, true); return true; }
            if (label.Contains("rug") || label.Contains("carpet"))
            { surface = new Surface("dirty_carpet", .6f, .6f, .55f, Color.white, true); return true; }
            if (label.Contains("plaster") || label.Contains("ceramic") || label.Contains("paper"))
            { surface = new Surface("grey_plaster_03", 1f, 1f, label.Contains("plaster") ? .65f : .2f, Color.white, true); return true; }
            if (label.Contains("bark"))
            { surface = new Surface("bark_brown_02", 1f, 1f, .95f, Color.white); return true; }
            if (label.Contains("metal ") || label.Contains("brass") || label.Contains("bronze"))
            { surface = new Surface("rusty_metal_02", 1f, 1f, .18f, Color.white, true, AlbedoVariant.NeutralWear, .8f); return true; }
            switch (label)
            {
                case "asphalt": surface = new Surface("asphalt_02", 3f, 3f, .75f, Color.white); return true;
                case "asphalt_repair": surface = new Surface("asphalt_02", 3f, 3f, .68f, new Color(.82f, .83f, .81f)); return true;
                case "concrete":
                case "architecturalconcrete": surface = new Surface("concrete_floor_02", 2f, 2f, .65f, Color.white); return true;
                case "platformconcrete": surface = new Surface("concrete_floor_02", 2f, 2f, .4f, Color.white, true, AlbedoVariant.NeutralConcrete); return true;
                case "sootedconcrete": surface = new Surface("concrete_floor_02", 2f, 2f, .25f, Color.white, true, AlbedoVariant.NeutralConcrete); return true;
                case "limestonecladding": surface = new Surface("grey_plaster_03", 1f, 1f, .5f, Color.white, true); return true;
                case "warmmasonrycladding": surface = new Surface("brick_wall_02", 2f, 2f, .8f, Color.white); return true;
                case "darkconcrete": surface = new Surface("concrete_floor_02", 2f, 2f, .65f, new Color(.64f,.66f,.66f)); return true;
                case "paving": surface = new Surface("brick_pavement_02", 2f, 2f, .85f, Color.white); return true;
                case "brick": surface = new Surface("brick_wall_02", 2f, 2f, .8f, Color.white); return true;
                case "wood": surface = new Surface("old_wood_floor", 3f, 3f, .6f, Color.white); return true;
                case "stationtile": surface = new Surface("long_white_tiles", 1.27f, 1.27f, .7f, Color.white); return true;
                case "metal":
                case "brushedsteel":
                case "steel":
                case "stainlesssteel": surface = new Surface("rusty_metal_02", 1f, 1f, .18f, Color.white, true, AlbedoVariant.NeutralWear, .8f); return true;
                case "weatheredsteel":
                case "rust":
                case "rustedmetal": surface = new Surface("rusty_metal_02", 1f, 1f, .35f, Color.white, true, AlbedoVariant.RestrainedRust); return true;
                case "transitpaint":
                case "transitenamel":
                case "paint": surface = new Surface("rusty_metal_02", 1f, 1f, .12f, Color.white, true, AlbedoVariant.NeutralWear, 0f); return true;
                case "plaster": surface = new Surface("grey_plaster_03", 1f, 1f, .65f, Color.white); return true;
                case "fabric": surface = new Surface("denim_fabric_03", .2714f, .271f, .32f, Color.white, true); return true;
                case "carpet": surface = new Surface("dirty_carpet", .6f, .6f, .55f, Color.white, true); return true;
                case "moss":
                case "grass": surface = new Surface("withered_grass", 2f, 2f, .6f, Color.white); return true;
                case "charcoal":
                case "rubber": surface = new Surface("grey_plaster_03", 1f, 1f, .15f, Color.white, true); return true;
                default: surface = default; return false;
            }
        }

        private enum AlbedoVariant { Original, NeutralWear, RestrainedRust, NeutralConcrete }

        private readonly struct Surface
        {
            public readonly string id;
            public readonly float width, height, normalStrength;
            public readonly Color tint;
            public readonly bool preserveTint;
            public readonly AlbedoVariant albedoVariant;
            public readonly float metalness;
            public Surface(string id, float width, float height, float normalStrength, Color tint, bool preserveTint = false,
                AlbedoVariant albedoVariant = AlbedoVariant.Original, float metalness = -1f)
            {
                this.id = id; this.width = width; this.height = height; this.normalStrength = normalStrength;
                this.tint = tint; this.preserveTint = preserveTint; this.albedoVariant = albedoVariant; this.metalness = metalness;
            }
        }
    }
}
#endif
