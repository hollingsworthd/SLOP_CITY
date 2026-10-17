#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SlopCity.Editor
{
    /// <summary>Reduce world texture allocations in a Web export without changing native imports or artwork.</summary>
    public sealed class SlopCityWebTextureBudgetScope : IDisposable
    {
        public const int ReflectionMaximumSize = 128;
        public static int ActiveReflectionMaximumSize { get; private set; }
        private bool ownsActiveScope;
        private readonly List<OriginalImport> originals = new List<OriginalImport>();
        public readonly List<Entry> entries = new List<Entry>();

        public SlopCityWebTextureBudgetScope()
        {
            if (ActiveReflectionMaximumSize != 0)
                throw new InvalidOperationException("A Web texture export scope is already active.");
            ActiveReflectionMaximumSize = ReflectionMaximumSize;
            ownsActiveScope = true;
            try
            {
                string[] folders = {
                    "Assets/Art/Surfaces", "Assets/Materials/SlopCityCrowd",
                    "Assets/Art/WorldRendering", "Assets/Art/Reflections/MetricCity"
                };
                var visited = new HashSet<string>(StringComparer.Ordinal);
                foreach (string guid in AssetDatabase.FindAssets("t:Texture", folders))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!visited.Add(path)) continue;
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (!importer) continue; // Serialized meshes/textures have no texture importer.
                    bool cube = importer.textureShape == TextureImporterShape.TextureCube;
                    bool sky = path == SlopCitySurfaceLibrary.SkyPath;
                    bool reflection = path.EndsWith(".exr", StringComparison.OrdinalIgnoreCase) && cube;
                    if (path.StartsWith("Assets/Art/WorldRendering/", StringComparison.Ordinal) && !reflection) continue;
                    if (path.StartsWith("Assets/Art/Reflections/", StringComparison.Ordinal) && !reflection) continue;
                    int cap = sky ? 256 : reflection ? ReflectionMaximumSize : 512;
                    TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings("WebGL");
                    int previousMax = settings.overridden ? settings.maxTextureSize : importer.maxTextureSize;
                    Texture before = AssetDatabase.LoadAssetAtPath<Texture>(path);
                    var entry = new Entry { path = path, maximumSize = Mathf.Min(previousMax, cap),
                        previousWidth = before ? before.width : 0, previousHeight = before ? before.height : 0 };
                    originals.Add(new OriginalImport { path = path, metadata = File.ReadAllBytes(path + ".meta") });
                    settings.name = "WebGL";
                    settings.overridden = true;
                    settings.maxTextureSize = entry.maximumSize;
                    if (sky || reflection)
                    {
                        // RGB9E5 retains HDR radiance in four bytes per texel, with no
                        // BC6H hardware requirement or half-float alpha channel. Validate
                        // the installed editor's Web importer before requesting this format.
                        bool packedHdr = TextureImporter.IsPlatformTextureFormatValid(
                            importer.textureType, BuildTarget.WebGL, TextureImporterFormat.RGB9E5);
                        settings.format = packedHdr ? TextureImporterFormat.RGB9E5 : TextureImporterFormat.Automatic;
                        settings.textureCompression = TextureImporterCompression.Uncompressed;
                        settings.crunchedCompression = false;
                        entry.packedHdrRequested = packedHdr;
                    }
                    importer.SetPlatformTextureSettings(settings);
                    importer.SaveAndReimport();
                    Texture after = AssetDatabase.LoadAssetAtPath<Texture>(path);
                    entry.width = after ? after.width : 0;
                    entry.height = after ? after.height : 0;
                    entry.importFormat = (settings.format == TextureImporterFormat.Automatic
                        ? importer.GetAutomaticFormat("WebGL") : settings.format).ToString();
                    if (after is Texture2D image) entry.runtimeFormat = image.format.ToString();
                    else if (after is Cubemap cubemap) entry.runtimeFormat = cubemap.format.ToString();
                    entries.Add(entry);
                }
                Debug.Log("SLOP_CITY_WEB_TEXTURE_BUDGET " + entries.Count
                    + " world imports; surface/crowd512, sky256, reflection128; native metadata restored after export.");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            // Retire the build-only policy even when a restoration import throws.
            // Repeated Dispose must not reset a later scope's active policy.
            if (ownsActiveScope)
            {
                ActiveReflectionMaximumSize = 0;
                ownsActiveScope = false;
            }
            // Restore exact metadata, including any unrelated platform overrides and
            // source GUIDs. Restore every file even if one restoration import fails.
            var errors = new List<Exception>();
            for (int index = originals.Count - 1; index >= 0; index--)
            {
                OriginalImport original = originals[index];
                try
                {
                    File.WriteAllBytes(original.path + ".meta", original.metadata);
                    AssetDatabase.ImportAsset(original.path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                }
                catch (Exception error) { errors.Add(error); }
            }
            originals.Clear();
            if (errors.Count > 0) throw new AggregateException("World texture source metadata restoration failed.", errors);
        }

        private sealed class OriginalImport
        {
            public string path;
            public byte[] metadata;
        }

        [Serializable]
        public sealed class Entry
        {
            public string path;
            public int maximumSize;
            public int previousWidth;
            public int previousHeight;
            public int width;
            public int height;
            public bool packedHdrRequested;
            public string importFormat;
            public string runtimeFormat;
        }
    }
}
#endif
