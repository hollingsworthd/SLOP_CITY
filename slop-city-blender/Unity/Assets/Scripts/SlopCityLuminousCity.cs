using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>Authored architectural palette and selective sign radiance; original material assets remain untouched.</summary>
    public sealed class SlopCityLuminousCity : MonoBehaviour
    {
        public const float EmissionMultiplier = 1f;
        public const float WindowEmissionMultiplier = 1f;
        public const float SignEmissionMultiplier = 1f;
        public const float PracticalEmissionMultiplier = 1f;
        public const float WetFilmCoverage = .78f;
        public const float WetFilmSmoothness = .94f;
        public const float WetReflectionGain = 1f;

        [Serializable]
        public sealed class MaterialPair
        {
            public Material source;
            public Material replacement;
            public bool emissive;
            public bool wet;
            public bool concrete;
        }

        [SerializeField] private Shader wetSurfaceShader;
        [SerializeField] private List<MaterialPair> materialPairs = new List<MaterialPair>();
        [SerializeField] private bool prepared;
        public bool Prepared => prepared;
        public int SharedCloneCount => materialPairs.Count;
        public IReadOnlyList<MaterialPair> MaterialPairs => materialPairs;
        public int EmissiveMaterialCount
        {
            get { int count = 0; foreach (MaterialPair pair in materialPairs) if (pair.emissive) count++; return count; }
        }
        public int WetMaterialCount
        {
            get { int count = 0; foreach (MaterialPair pair in materialPairs) if (pair.wet) count++; return count; }
        }
        public int ConcreteMaterialCount
        {
            get { int count = 0; foreach (MaterialPair pair in materialPairs) if (pair.concrete) count++; return count; }
        }

        public Material GetSource(Material replacement)
        {
            foreach (MaterialPair pair in materialPairs)
                if (pair.replacement == replacement) return pair.source;
            return null;
        }

        public static void Prepare(Transform city, Shader wetShader = null)
        {
            if (!city) return;
            SlopCityNightLighting.Prepare(city);
            SlopCityStreetAtmosphere.Prepare(city);
            SlopCityLuminousCity profile = city.GetComponent<SlopCityLuminousCity>();
            if (!profile) profile = city.gameObject.AddComponent<SlopCityLuminousCity>();
            if (wetShader) profile.wetSurfaceShader = wetShader;
            if (!profile.wetSurfaceShader) profile.wetSurfaceShader = Shader.Find("SlopCity/WetCitySurface");
            profile.Apply();
        }

        private void Apply()
        {
            if (!wetSurfaceShader)
                throw new InvalidOperationException("The shared wet-city shader must be retained in the build scene.");
            // Saved scenes can contain an earlier release's clones. Always derive the
            // current finish from the original source, including those serialized clones.
            // This never multiplies a replacement again or allocates per renderer.
            var replacements = new Dictionary<Material, Material>();
            var completed = new HashSet<Material>();
            foreach (MaterialPair pair in materialPairs)
            {
                if (!pair.source || !pair.replacement)
                    throw new InvalidOperationException("The luminous city lost a shared material reference.");
                ConfigureReplacement(pair);
                replacements.Add(pair.source, pair.replacement);
                completed.Add(pair.replacement);
            }
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (SlopCitySalvagePreparation.IsMutable(renderer)) continue;
                Material[] slots = renderer.sharedMaterials;
                bool changed = false;
                for (int index = 0; index < slots.Length; index++)
                {
                    Material source = slots[index];
                    if (!source || completed.Contains(source)) continue;
                    if (!replacements.TryGetValue(source, out Material replacement))
                    {
                        bool wet = IsWetSurface(source);
                        bool emissive = source.HasProperty("_EmissionColor") && source.IsKeywordEnabled("_EMISSION")
                            && Peak(source.GetColor("_EmissionColor")) > .0001f;
                        bool concrete = false;
                        if (!wet && !emissive && !concrete) continue;
                        replacement = new Material(source) { name = source.name + " [Luminous City]" };
                        var pair = new MaterialPair { source = source, replacement = replacement, wet = wet, emissive = emissive, concrete = concrete };
                        ConfigureReplacement(pair);
                        materialPairs.Add(pair);
                        replacements.Add(source, replacement);
                        completed.Add(replacement);
                    }
                    slots[index] = replacement;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = slots;
            }
            prepared = true;
        }

        private void ConfigureReplacement(MaterialPair pair)
        {
            Material source = pair.source, replacement = pair.replacement;
            replacement.shader = source.shader;
            replacement.CopyPropertiesFromMaterial(source);
            pair.concrete = false;
            if (pair.emissive)
            {
                Color radiance = source.GetColor("_EmissionColor");
                float scale = EmissionScale(source);
                replacement.SetColor("_EmissionColor", new Color(radiance.r * scale,
                    radiance.g * scale, radiance.b * scale, radiance.a));
            }
            // Copying the original restores its scanned colour, reflectivity and
            // occlusion; full-size masonry/glass architecture retains its own identity.
            if (!pair.wet) return;
            bool water = source.name == "Rainwater" || source.name == "City_Water";
            replacement.shader = wetSurfaceShader;
            replacement.renderQueue = source.renderQueue;
            replacement.SetFloat("_WetFilmCoverage", water ? 1f : WetFilmCoverage);
            replacement.SetFloat("_WetFilmSmoothness", WetFilmSmoothness);
            replacement.SetFloat("_WetReflectionGain", WetReflectionGain);
            replacement.SetFloat("_HasMetallicMap", source.GetTexture("_MetallicGlossMap") ? 1 : 0);
            replacement.SetFloat("_HasOcclusionMap", source.GetTexture("_OcclusionMap") ? 1 : 0);
            replacement.SetFloat("_HasNormalMap", source.GetTexture("_BumpMap") ? 1 : 0);
            replacement.SetFloat("_IsWater", water ? 1 : 0);
            replacement.SetFloat("_RippleAmplitude", water ? .008f : .004f);
            if (source.GetFloat("_Mode") >= 2)
                replacement.SetOverrideTag("RenderType", "Transparent");
        }

        public static float EmissionScale(Material source)
        {
            if (!source) return 1f;
            if (source.name == "CurtainWallGlass") return WindowEmissionMultiplier;
            // Preserve authored tube/sign/window radiance without whitening their
            // distinctive cyan, rose and warm accents through a global multiplier.
            if (source.name.StartsWith("Japan_Notice_NeonTube_", StringComparison.Ordinal)) return EmissionMultiplier;
            if (source.name.StartsWith("Japan_Notice_Kanban_", StringComparison.Ordinal)) return SignEmissionMultiplier;
            return PracticalEmissionMultiplier;
        }

        private static bool IsWetSurface(Material material)
        {
            // Interior floors, carpet, wood and sidewalk stone retain their own finish.
            // Runtime street cutouts share City_Asphalt, keeping their original maps.
            return material.name == "City_Asphalt" || material.name == "City_Water" || material.name == "Rainwater";
        }
        private static float Peak(Color color) { return Mathf.Max(color.r, Mathf.Max(color.g, color.b)); }

        private void OnDestroy()
        {
            // Restore surviving geometry when the profile itself is removed. Scene
            // unload may already have destroyed those renderers/materials.
            var originals = new Dictionary<Material, Material>();
            foreach (MaterialPair pair in materialPairs)
                if (pair.replacement && pair.source) originals[pair.replacement] = pair.source;
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer) continue;
                Material[] slots = renderer.sharedMaterials;
                bool changed = false;
                for (int index = 0; index < slots.Length; index++)
                    if (slots[index] && originals.TryGetValue(slots[index], out Material original))
                    { slots[index] = original; changed = true; }
                if (changed) renderer.sharedMaterials = slots;
            }
            foreach (MaterialPair pair in materialPairs)
            {
                if (!pair.replacement) continue;
                if (Application.isPlaying) Destroy(pair.replacement); else DestroyImmediate(pair.replacement);
            }
            materialPairs.Clear();
        }
    }
}
