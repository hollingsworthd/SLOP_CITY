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
            float[] scales = { 520f, 680f, 860f, 1100f };
            for (int index = 0; index < depths.Length; index++)
            {
                GameObject layer = GameObject.CreatePrimitive(PrimitiveType.Quad);
                layer.name = "SkyCloud_" + index;
                layer.transform.SetParent(cloudRoot.transform, false);
                layer.transform.localPosition = new Vector3(0f, depths[index], 0f);
                layer.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                layer.transform.localScale = new Vector3(scales[index], scales[index], 1f);
                Collider collider = layer.GetComponent<Collider>();
                if (collider) Destroy(collider);
                Renderer renderer = layer.GetComponent<Renderer>();
                if (renderer) renderer.sharedMaterial = cloudMaterial;
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
            if (!fogOverrideActive) return;
            RenderSettings.fog = savedFog;
            RenderSettings.fogColor = savedFogColor;
            RenderSettings.fogDensity = savedFogDensity;
            RenderSettings.fogMode = savedFogMode;
            fogOverrideActive = false;
            sprawlActive = false;
            if (cloudRoot) cloudRoot.SetActive(false);
        }
    }
}
