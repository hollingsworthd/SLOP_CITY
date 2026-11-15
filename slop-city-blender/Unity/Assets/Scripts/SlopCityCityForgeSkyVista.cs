using UnityEngine;

namespace SlopCity
{
    /// <summary>Soft cloud layers and mist below the Sky Sprawl buildable frontier.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityForgeSkyVista : MonoBehaviour
    {
        private Transform followTarget;
        private GameObject cloudRoot;
        private SlopCityCityForgeController controller;
        private bool buildModeActive;
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
        // The authored ground participates in static batching.  Toggling its
        // MeshRenderer alone is not reliable on native builds: its baked
        // draw can survive even after Renderer.enabled is false.  Hide the
        // actual object while the Sky Sprawl is active, then restore precisely
        // the scene's prior active state when the player returns to Old City.
        private GameObject distantGroundObject;
        private bool distantGroundWasActive;
        private bool distantGroundSuppressed;

        public void Configure(Transform target)
        {
            followTarget = target;
            AttachController();
            EnsureClouds();
        }

        private void AttachController()
        {
            SlopCityCityForgeController next = GetComponent<SlopCityCityForgeController>() ?? FindFirstObjectByType<SlopCityCityForgeController>();
            if (controller == next) return;
            if (controller) controller.BuildModeChanged -= HandleBuildModeChanged;
            controller = next;
            if (controller)
            {
                controller.BuildModeChanged += HandleBuildModeChanged;
                buildModeActive = controller.IsBuildMode;
            }
        }

        private void HandleBuildModeChanged(bool active)
        {
            buildModeActive = active;
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
            // The first loose bank sits close enough to be visible from the
            // lip of an island. The remaining banks continue far below it, so
            // a fall is a long descent through clouds rather than an immediate
            // fog reset or a distant empty blue void.
            float[] depths = { -16f, -48f, -95f, -150f, -215f };
            float[] spans = { 120f, 190f, 260f, 330f, 410f };
            for (int index = 0; index < depths.Length; index++)
            {
                // Solid transparent quads read as an endless ground plane,
                // especially on a shallow first-person camera.  Use a loose
                // bank of collider-free cloud puffs instead, so the player
                // sees open air between layers while falling through them.
                int puffCount = 8 + index * 3;
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
            // The real cloud banks belong to the first-person Sky Sprawl.
            // Build Mode has its own dark planning surface and must not use a
            // top-down view of those clouds as its map background.
            AttachController();
            Vector3 position = followTarget.position;
            // Keep the cloud field active over the public entry deck as well
            // as editable chunks; the deck physically straddles the core seam.
            bool sprawl = SlopCityCityForgeTravel.IsSkySprawlPosition(position);
            bool showWorldClouds = sprawl && !buildModeActive;
            if (showWorldClouds && !fogOverrideActive)
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
            else if (!showWorldClouds && fogOverrideActive)
            {
                RenderSettings.fog = savedFog;
                RenderSettings.fogColor = savedFogColor;
                RenderSettings.fogDensity = savedFogDensity;
                RenderSettings.fogMode = savedFogMode;
                fogOverrideActive = false;
            }

            SetDistantGroundSuppressed(sprawl);

            if (cloudRoot && cloudRoot.activeSelf != showWorldClouds) cloudRoot.SetActive(showWorldClouds);
            if (cloudRoot && showWorldClouds)
            {
                cloudRoot.transform.position = new Vector3(position.x, 0f, position.z);
            }

            if (showWorldClouds && fogOverrideActive)
            {
                float depth = Mathf.Clamp01((24f - position.y) / 120f);
                RenderSettings.fogDensity = Mathf.Lerp(.0065f, .022f, depth);
            }
        }

        private void OnDisable()
        {
            if (controller) controller.BuildModeChanged -= HandleBuildModeChanged;
            controller = null;
            SetDistantGroundSuppressed(false);
            if (fogOverrideActive)
            {
                RenderSettings.fog = savedFog;
                RenderSettings.fogColor = savedFogColor;
                RenderSettings.fogDensity = savedFogDensity;
                RenderSettings.fogMode = savedFogMode;
                fogOverrideActive = false;
            }
            if (cloudRoot) cloudRoot.SetActive(false);
        }

        private void SetDistantGroundSuppressed(bool suppress)
        {
            if (suppress)
            {
                // Cache at the time it is first needed rather than in Awake:
                // the scene object can be enabled after this runtime bootstrap.
                if (!distantGroundObject)
                {
                    distantGroundObject = GameObject.Find("Distant_City_Ground");
                    if (distantGroundObject) distantGroundWasActive = distantGroundObject.activeSelf;
                }
                if (distantGroundObject && distantGroundObject.activeSelf)
                {
                    distantGroundObject.SetActive(false);
                }
                distantGroundSuppressed = true;
                return;
            }

            if (!distantGroundSuppressed) return;
            if (distantGroundObject) distantGroundObject.SetActive(distantGroundWasActive);
            distantGroundSuppressed = false;
        }
    }
}
