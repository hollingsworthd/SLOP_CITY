using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>Soft cloud layers and mist below the Sky Sprawl buildable frontier.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityForgeSkyVista : MonoBehaviour
    {
        private Transform followTarget;
        private GameObject cloudRoot;
        private bool sprawlActive;
        private bool savedFog;
        private bool fogOverrideActive;
        private Color savedFogColor;
        private float savedFogDensity;
        private FogMode savedFogMode;
        // The authored scene includes this huge distant visual mesh. It has no
        // collider, so it never stopped a fall, but it made the Sky Sprawl
        // look like it still had an infinite floor. Keep the authored city
        // intact everywhere else and suppress only that renderer while the
        // player is in the sky-island frontier.
        private readonly List<Renderer> distantGroundRenderers = new List<Renderer>();
        private readonly List<bool> distantGroundWasEnabled = new List<bool>();
        private bool distantGroundSuppressed;

        public void Configure(Transform target)
        {
            followTarget = target;
            EnsureClouds();
        }

        private void EnsureClouds()
        {
            if (cloudRoot) return;
            cloudRoot = new GameObject("CityForgeSkyVista_Clouds");
            cloudRoot.transform.SetParent(transform, false);
            Material cloudMaterial = new Material(Shader.Find("Standard"))
            {
                color = new Color(.92f, .95f, .98f, .68f),
                hideFlags = HideFlags.DontSave
            };
            cloudMaterial.SetFloat("_Mode", 3f);
            cloudMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            cloudMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            cloudMaterial.SetInt("_ZWrite", 0);
            cloudMaterial.DisableKeyword("_ALPHATEST_ON");
            cloudMaterial.EnableKeyword("_ALPHABLEND_ON");
            cloudMaterial.renderQueue = 3000;
            float[] depths = { -55f, -90f, -140f, -210f };
            float[] spans = { 190f, 250f, 320f, 400f };
            for (int index = 0; index < depths.Length; index++)
            {
                // Solid transparent quads read as an endless ground plane,
                // especially on a shallow first-person camera.  Use a loose
                // bank of collider-free cloud puffs instead, so the player
                // sees open air between layers while falling through them.
                int puffCount = 12 + index * 3;
                for (int puff = 0; puff < puffCount; puff++)
                {
                    float seed = puff * 2.3999632f + index * .71f;
                    float radius = spans[index] * (.16f + .62f * ((puff * 37 + index * 19) % 100) / 100f);
                    float width = 26f + ((puff * 17 + index * 11) % 19) * 2.4f;
                    float height = 7f + ((puff * 29 + index * 7) % 7) * 1.4f;
                    GameObject cloud = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    cloud.name = "SkyCloud_" + index + "_" + puff;
                    cloud.transform.SetParent(cloudRoot.transform, false);
                    cloud.transform.localPosition = new Vector3(Mathf.Cos(seed) * radius,
                        depths[index] + ((puff % 5) - 2) * 2.2f, Mathf.Sin(seed) * radius);
                    cloud.transform.localScale = new Vector3(width, height, width * (.72f + (puff % 4) * .10f));
                    Collider collider = cloud.GetComponent<Collider>();
                    if (collider) { collider.enabled = false; Destroy(collider); }
                    Renderer renderer = cloud.GetComponent<Renderer>();
                    if (renderer)
                    {
                        renderer.sharedMaterial = cloudMaterial;
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                    }
                }
            }
            cloudRoot.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!followTarget) return;
            Vector3 position = followTarget.position;
            // Keep the cloud field active over the public entry deck as well
            // as editable chunks; the deck physically straddles the core seam.
            bool sprawl = SlopCityCityForgeTravel.IsSkySprawlPosition(position);
            if (sprawl != sprawlActive)
            {
                sprawlActive = sprawl;
                if (cloudRoot) cloudRoot.SetActive(sprawl);
                if (sprawl && !fogOverrideActive)
                {
                    savedFog = RenderSettings.fog;
                    savedFogColor = RenderSettings.fogColor;
                    savedFogDensity = RenderSettings.fogDensity;
                    savedFogMode = RenderSettings.fogMode;
                    RenderSettings.fog = true;
                    RenderSettings.fogMode = FogMode.Exponential;
                    RenderSettings.fogColor = new Color(.78f, .84f, .92f);
                    RenderSettings.fogDensity = .0065f;
                    fogOverrideActive = true;
                }
                else if (!sprawl && fogOverrideActive)
                {
                    RenderSettings.fog = savedFog;
                    RenderSettings.fogColor = savedFogColor;
                    RenderSettings.fogDensity = savedFogDensity;
                    RenderSettings.fogMode = savedFogMode;
                    fogOverrideActive = false;
                }
            }

            SetDistantGroundSuppressed(sprawl);

            if (cloudRoot && sprawl)
            {
                cloudRoot.transform.position = new Vector3(position.x, 0f, position.z);
            }

            if (sprawl && fogOverrideActive)
            {
                float depth = Mathf.Clamp01((24f - position.y) / 120f);
                RenderSettings.fogDensity = Mathf.Lerp(.0065f, .022f, depth);
            }
        }

        private void OnDisable()
        {
            SetDistantGroundSuppressed(false);
            if (fogOverrideActive)
            {
                RenderSettings.fog = savedFog;
                RenderSettings.fogColor = savedFogColor;
                RenderSettings.fogDensity = savedFogDensity;
                RenderSettings.fogMode = savedFogMode;
                fogOverrideActive = false;
            }
            sprawlActive = false;
            if (cloudRoot) cloudRoot.SetActive(false);
        }

        private void SetDistantGroundSuppressed(bool suppress)
        {
            if (suppress)
            {
                // Cache at the time it is first needed rather than in Awake:
                // the scene object can be enabled after this runtime bootstrap.
                if (distantGroundRenderers.Count == 0)
                {
                    GameObject distantGround = GameObject.Find("Distant_City_Ground");
                    if (distantGround)
                    {
                        foreach (Renderer renderer in distantGround.GetComponentsInChildren<Renderer>(true))
                        {
                            if (!renderer) continue;
                            distantGroundRenderers.Add(renderer);
                            distantGroundWasEnabled.Add(renderer.enabled);
                        }
                    }
                }
                foreach (Renderer renderer in distantGroundRenderers)
                    if (renderer) renderer.enabled = false;
                distantGroundSuppressed = true;
                return;
            }

            if (!distantGroundSuppressed) return;
            for (int index = 0; index < distantGroundRenderers.Count; index++)
            {
                Renderer renderer = distantGroundRenderers[index];
                if (renderer) renderer.enabled = distantGroundWasEnabled[index];
            }
            distantGroundRenderers.Clear();
            distantGroundWasEnabled.Clear();
            distantGroundSuppressed = false;
        }
    }
}
