using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>
    /// Owns the city-wide, deterministic daylight cycle. It changes only Unity's global
    /// lighting and sky settings, so normal renderers in streamed accepted City Forge
    /// chunks take part without a per-chunk lighting pass or a City Forge dependency.
    /// </summary>
    public sealed class SlopCityNightLighting : MonoBehaviour
    {
        public enum CityDayPhase { Dawn, Day, Dusk, Night }

        public const float DaylightIntensity = 1.05f;
        public const float SkyExposure = .75f;
        public const float SunElevation = 38f;
        public const float GameDaySeconds = 480f;
        public const float InitialClockHour = 9.5f;

        private const int CycleStepsPerDay = 720;
        private const float MoonDistance = 320f;
        private const float MoonDiameter = 28f;
        private static readonly Color DaySky = new Color(.42f, .49f, .58f, 1);
        private static readonly Color DayEquator = new Color(.30f, .36f, .43f, 1);
        private static readonly Color DayGround = new Color(.12f, .15f, .19f, 1);
        private static readonly Color DayFog = new Color(.30f, .41f, .54f, 1);
        private static readonly Color NightSky = new Color(.025f, .045f, .11f, 1);
        private static readonly Color NightEquator = new Color(.035f, .055f, .12f, 1);
        private static readonly Color NightGround = new Color(.008f, .012f, .025f, 1);
        private static readonly Color NightFog = new Color(.025f, .04f, .09f, 1);
        private static readonly Color DaylightColor = new Color(.87f, .94f, 1f, 1);
        private static readonly Color DawnLightColor = new Color(1f, .54f, .32f, 1);
        private static readonly Color DuskLightColor = new Color(.92f, .38f, .29f, 1);

        [SerializeField] private Material originalSky;
        [SerializeField] private Material eveningSky;
        private bool ownsEveningSky;
        private Light cachedDaylight;
        private bool ownsDaylight;
        private GameObject moonBody;
        private Light moonlight;
        private Material moonMaterial;
        private Transform cachedView;
        private SlopCityCrowd cachedCrowd;
        private int cycleTick = -1;

        public Material OriginalSky => originalSky;
        public Material EveningSky => eveningSky;
        public float GameClockHours => StableClock01(Application.isPlaying ? Time.time : 0f) * 24f;

        /// <summary>
        /// Converts game simulation time to a repeatable time of day. Time.time freezes
        /// with the game pause, so opening menus never advances dawn, dusk, or night.
        /// </summary>
        public static float StableClock01(float gameSeconds)
        {
            return Mathf.Repeat(InitialClockHour / 24f + Mathf.Max(0f, gameSeconds) / GameDaySeconds, 1f);
        }

        public static CityDayPhase PhaseAt(float clock01)
        {
            float hour = Mathf.Repeat(clock01, 1f) * 24f;
            if (hour < 5f || hour >= 20f) return CityDayPhase.Night;
            if (hour < 7f) return CityDayPhase.Dawn;
            if (hour < 17f) return CityDayPhase.Day;
            return CityDayPhase.Dusk;
        }

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
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            EnsureDaylight();

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
                // Repeated preparation also replaces serialized clone properties; the
                // original sky remains untouched for recovery and provenance.
                eveningSky.shader = originalSky.shader;
                eveningSky.CopyPropertiesFromMaterial(originalSky);
                eveningSky.name = "Slop City / Day Night Sky";
                RenderSettings.skybox = eveningSky;
            }

            cycleTick = -1;
            ApplyCycle(StableClock01(Application.isPlaying ? Time.time : 0f));
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            // The global profile advances in bounded fixed steps instead of doing a
            // per-frame world scan. There is no wall-clock, random, or chunk state here.
            if (!cachedDaylight) EnsureDaylight();
            float clock01 = StableClock01(Time.time);
            int nextTick = Mathf.FloorToInt(clock01 * CycleStepsPerDay);
            if (nextTick == cycleTick) return;
            cycleTick = nextTick;
            ApplyCycle(clock01);
        }

        private void ApplyCycle(float clock01)
        {
            float hour = Mathf.Repeat(clock01, 1f) * 24f;
            // Resident routines share this paused simulation clock. Keep the
            // lookup cached so a lighting tick never turns into a world scan.
            if (!cachedCrowd) cachedCrowd = FindFirstObjectByType<SlopCityCrowd>();
            if (cachedCrowd) cachedCrowd.SetSprawlRoutineHour(hour);
            CityDayPhase phase = PhaseAt(clock01);
            float daylight = DaylightWeight(hour);
            float night = 1f - daylight;
            float twilight = TwilightWeight(hour, phase);

            Color ambientSky = Color.Lerp(NightSky, DaySky, daylight);
            Color ambientEquator = Color.Lerp(NightEquator, DayEquator, daylight);
            Color ambientGround = Color.Lerp(NightGround, DayGround, daylight);
            Color fog = Color.Lerp(NightFog, DayFog, daylight);
            if (twilight > 0f)
            {
                Color horizon = phase == CityDayPhase.Dawn
                    ? new Color(.72f, .32f, .24f, 1)
                    : new Color(.36f, .15f, .42f, 1);
                ambientSky = Color.Lerp(ambientSky, horizon, twilight * .24f);
                fog = Color.Lerp(fog, horizon, twilight * .15f);
            }

            RenderSettings.ambientIntensity = Mathf.Lerp(.30f, 1f, daylight);
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = Mathf.Lerp(.0041f, .0026f, daylight);

            Vector3 sunDirection = CelestialDirection(clock01);
            if (cachedDaylight)
            {
                cachedDaylight.enabled = daylight > .001f;
                cachedDaylight.intensity = DaylightIntensity * daylight;
                cachedDaylight.color = DaylightColorFor(phase, twilight);
                cachedDaylight.transform.rotation = CelestialRotation(sunDirection);
                RenderSettings.sun = cachedDaylight;
            }

            if (eveningSky)
            {
                if (eveningSky.HasProperty("_Exposure"))
                    eveningSky.SetFloat("_Exposure", Mathf.Lerp(.14f, SkyExposure, daylight));
                if (eveningSky.HasProperty("_Tint"))
                    eveningSky.SetColor("_Tint", Color.Lerp(new Color(.16f, .20f, .38f, 1), Color.gray, daylight));
            }

            UpdateMoon(-sunDirection, night);
        }

        private void EnsureDaylight()
        {
            if (!cachedDaylight)
            {
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    foreach (Light lamp in root.GetComponentsInChildren<Light>(true))
                    {
                        if (lamp.type != LightType.Directional || lamp.name != "BlueHour_Daylight") continue;
                        cachedDaylight = lamp;
                        break;
                    }
                    if (cachedDaylight) break;
                }
            }

            // The authored city has BlueHour_Daylight. A single runtime fallback keeps
            // the public core valid for additive/offline scenes without adding lights
            // per streamed chunk.
            if (cachedDaylight || !Application.isPlaying) return;
            GameObject fallback = new GameObject("BlueHour_Daylight");
            fallback.transform.SetParent(transform, false);
            cachedDaylight = fallback.AddComponent<Light>();
            cachedDaylight.type = LightType.Directional;
            cachedDaylight.shadows = LightShadows.Soft;
            ownsDaylight = true;
        }

        private void UpdateMoon(Vector3 moonDirection, float night)
        {
            if (!Application.isPlaying) return;
            if (night <= .015f && !moonBody) return;
            EnsureMoon();
            if (!moonBody || !moonlight) return;

            bool visible = night > .015f;
            if (moonBody.activeSelf != visible) moonBody.SetActive(visible);
            if (!visible) return;

            if (!cachedView || !cachedView.gameObject.activeInHierarchy)
            {
                Camera view = Camera.main;
                cachedView = view ? view.transform : null;
            }
            Vector3 anchor = cachedView ? cachedView.position : transform.position;
            moonBody.transform.position = anchor + moonDirection.normalized * MoonDistance;
            moonlight.transform.rotation = CelestialRotation(moonDirection);
            moonlight.intensity = .13f * night;
            moonlight.enabled = true;
        }

        private void EnsureMoon()
        {
            if (moonBody) return;

            moonBody = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            moonBody.name = "SlopCity_NightMoon";
            moonBody.transform.SetParent(transform, false);
            moonBody.transform.localScale = Vector3.one * MoonDiameter;
            moonBody.layer = gameObject.layer;

            Collider collider = moonBody.GetComponent<Collider>();
            if (collider)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            Renderer renderer = moonBody.GetComponent<Renderer>();
            if (renderer)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                Shader shader = Shader.Find("Unlit/Color");
                if (!shader) shader = Shader.Find("Standard");
                if (shader)
                {
                    moonMaterial = new Material(shader);
                    moonMaterial.name = "Slop City / Moon";
                    if (moonMaterial.HasProperty("_Color")) moonMaterial.SetColor("_Color", new Color(.82f, .89f, 1f, 1));
                    if (moonMaterial.HasProperty("_EmissionColor"))
                    {
                        moonMaterial.EnableKeyword("_EMISSION");
                        moonMaterial.SetColor("_EmissionColor", new Color(.42f, .52f, 1f, 1));
                    }
                    renderer.sharedMaterial = moonMaterial;
                }
            }

            moonlight = moonBody.AddComponent<Light>();
            moonlight.type = LightType.Directional;
            moonlight.color = new Color(.42f, .52f, 1f, 1);
            moonlight.shadows = LightShadows.None;
            moonlight.cullingMask = ~0;
            moonBody.SetActive(false);
        }

        private static float DaylightWeight(float hour)
        {
            float sunrise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5f, 7f, hour));
            float sunset = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(17f, 20f, hour));
            return Mathf.Min(sunrise, sunset);
        }

        private static float TwilightWeight(float hour, CityDayPhase phase)
        {
            if (phase == CityDayPhase.Dawn) return Mathf.Sin(Mathf.InverseLerp(5f, 7f, hour) * Mathf.PI);
            if (phase == CityDayPhase.Dusk) return Mathf.Sin(Mathf.InverseLerp(17f, 20f, hour) * Mathf.PI);
            return 0f;
        }

        private static Color DaylightColorFor(CityDayPhase phase, float twilight)
        {
            if (phase == CityDayPhase.Dawn) return Color.Lerp(DaylightColor, DawnLightColor, twilight);
            if (phase == CityDayPhase.Dusk) return Color.Lerp(DaylightColor, DuskLightColor, twilight);
            return DaylightColor;
        }

        private static Vector3 CelestialDirection(float clock01)
        {
            float angle = (Mathf.Repeat(clock01, 1f) - .25f) * Mathf.PI * 2f;
            float altitude = Mathf.Sin(angle) * SunElevation * Mathf.Deg2Rad;
            Vector3 arc = new Vector3(Mathf.Cos(angle), Mathf.Sin(altitude), 0f).normalized;
            return Quaternion.Euler(0f, -58f, 0f) * arc;
        }

        private static Quaternion CelestialRotation(Vector3 direction)
        {
            Vector3 forward = -direction.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .98f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(forward, up);
        }

        private void OnDestroy()
        {
            if (RenderSettings.skybox == eveningSky) RenderSettings.skybox = originalSky;
            if (moonBody) DestroyOwned(moonBody);
            if (moonMaterial) DestroyOwned(moonMaterial);
            if (ownsDaylight && cachedDaylight) DestroyOwned(cachedDaylight.gameObject);
            if (ownsEveningSky && eveningSky) DestroyOwned(eveningSky);
        }

        private static void DestroyOwned(Object item)
        {
            if (!item) return;
            if (Application.isPlaying) Object.Destroy(item); else Object.DestroyImmediate(item);
        }
    }
}
