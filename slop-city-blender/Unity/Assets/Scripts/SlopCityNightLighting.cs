using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>Authored blue partly-cloudy sky, cool daylight and deep street shadows; practical switches retain their state.</summary>
    public sealed class SlopCityNightLighting : MonoBehaviour
    {
        public const float DaylightIntensity = 1.05f;
        public const float SkyExposure = .75f;
        public const float SunElevation = 38f;
        [SerializeField] private Material originalSky;
        [SerializeField] private Material eveningSky;
        private bool ownsEveningSky;
        public Material OriginalSky => originalSky;
        public Material EveningSky => eveningSky;

        public static void Prepare(Transform city)
        {
            if (!city) return;
            SlopCityNightLighting profile = city.GetComponent<SlopCityNightLighting>();
            if (!profile) profile = city.gameObject.AddComponent<SlopCityNightLighting>();
            profile.Apply();
        }

        private void Apply()
        {
            // Apply the same scene profile before reflection baking, build batching and
            // runtime initialization. Absolute values make repeated preparation harmless.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientIntensity = 1;
            RenderSettings.ambientSkyColor = new Color(.42f, .49f, .58f, 1);
            RenderSettings.ambientEquatorColor = new Color(.30f, .36f, .43f, 1);
            RenderSettings.ambientGroundColor = new Color(.12f, .15f, .19f, 1);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(.30f, .41f, .54f, 1);
            RenderSettings.fogDensity = .0026f;

            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                foreach (Light lamp in root.GetComponentsInChildren<Light>(true))
                    if (lamp.type == LightType.Directional && lamp.name == "BlueHour_Daylight")
                    {
                        lamp.intensity = DaylightIntensity;
                        lamp.color = new Color(.87f, .94f, 1f, 1);
                        lamp.transform.rotation = Quaternion.Euler(SunElevation, -58f, 0);
                        RenderSettings.sun = lamp;
                    }

            foreach (Light lamp in GetComponentsInChildren<Light>(true))
            {
                // Existing 15m unshadowed colour spills washed entire facades/roads.
                // Confine them to their actual sign corner; no extra lights or shadows.
                // Absolute values are idempotent, and enabled/culling state is untouched.
                if (lamp.name == "Cyan_Sign_Spill") { lamp.intensity = .70f; lamp.range = 6.5f; }
                else if (lamp.name == "Rose_Sign_Spill") { lamp.intensity = .28f; lamp.range = 6f; }
                else if (lamp.name == "Lamp_Pool")
                {
                    lamp.intensity = .82f;
                    lamp.range = 7.5f;
                    lamp.color = new Color(.91f, .90f, .78f, 1);
                }
                // Furnished rooms, lounge lamps and functional switch devices keep
                // their existing illumination; the darker fill applies around them.
            }

            if (!originalSky && RenderSettings.skybox != eveningSky) originalSky = RenderSettings.skybox;
            if (!eveningSky && originalSky)
            {
                eveningSky = new Material(originalSky);
                ownsEveningSky = true;
            }
            if (eveningSky && originalSky)
            {
                // The retained HDR photograph contains blue sky and real white cumulus.
                // Repeated preparation also replaces serialized dusk-clone properties;
                // the original sky remains untouched for recovery and provenance.
                eveningSky.shader = originalSky.shader;
                eveningSky.CopyPropertiesFromMaterial(originalSky);
                eveningSky.name = "Slop City / Blue Sky With Clouds";
                if (eveningSky.HasProperty("_Exposure")) eveningSky.SetFloat("_Exposure", SkyExposure);
                // Unity's Cubemap sky shader doubles tint: .5 is the neutral multiplier.
                if (eveningSky.HasProperty("_Tint")) eveningSky.SetColor("_Tint", Color.gray);
                RenderSettings.skybox = eveningSky;
            }
        }

        private void OnDestroy()
        {
            if (!eveningSky) return;
            if (RenderSettings.skybox == eveningSky) RenderSettings.skybox = originalSky;
            // Prepared scenes borrow their serialized sky material. Only a
            // clone allocated by this live component belongs to its teardown.
            if (!ownsEveningSky) return;
            if (Application.isPlaying) Destroy(eveningSky); else DestroyImmediate(eveningSky);
        }
    }
}
