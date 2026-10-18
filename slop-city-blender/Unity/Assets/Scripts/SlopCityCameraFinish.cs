using UnityEngine;

namespace SlopCity
{
    /// <summary>HDR city finish, with bounded forward bloom on browser/mobile renderers.</summary>
    [ExecuteAlways, RequireComponent(typeof(Camera))]
    public sealed class SlopCityCameraFinish : MonoBehaviour
    {
        [SerializeField] private Shader finishShader;
        public const int BrowserBloomMaximumDimension = 512;
        public const int BrowserBloomMaximumBytes = BrowserBloomMaximumDimension * BrowserBloomMaximumDimension * 8 * 2;
        [SerializeField, Range(.2f, 2f)] private float exposure = 1.04f;
#if UNITY_WEBGL && !UNITY_EDITOR
#pragma warning disable 0414 // Keep the authored scene layout; these effects are native-only.
#endif
        [SerializeField, Range(0f, 3f)] private float contactStrength = 1.4f;
        [SerializeField, Range(0f, .3f)] private float bloomStrength = .08f;
        [SerializeField, Range(0f, 1f)] private float reflectionStrength = .38f;
        [SerializeField, Range(0f, .02f)] private float heightHaze = .0032f;
#if UNITY_WEBGL && !UNITY_EDITOR
#pragma warning restore 0414
#endif
        private Material material;
        private Camera view;
        private bool lightweightFinish;
        private bool optionalBloomDisabled;
        private bool allocationWarning;
        public float Exposure => exposure;
        public float BloomStrength => bloomStrength;
        public float ReflectionStrength => reflectionStrength;
        public float ContactStrength => contactStrength;
        public float HeightHaze => heightHaze;
        public bool OptionalBloomDisabled => optionalBloomDisabled;
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern int SlopCityUsesTouch();
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern int SlopCity_IsDeferredRenderingDiagnosticsEnabled();
        private bool mobileBrowser;
        private bool browserProfileInitialized;
        private bool diagnosticDeferred;
        private struct BrowserLight
        {
            public Light light;
            public int cullingMask;
        }
        private BrowserLight[] browserLights;
        private float nextLightBudget;
#endif

        public void Configure(Shader shader)
        {
            finishShader = shader;
            Initialize();
        }

        private void OnEnable()
        {
            Application.lowMemory += ReduceMemory;
            Initialize();
        }

        private void ReduceMemory()
        {
            lightweightFinish = true;
            optionalBloomDisabled = true;
            if (!view) return;
            view.allowHDR = false;
            view.depthTextureMode = DepthTextureMode.None;
            view.renderingPath = RenderingPath.Forward;
        }
        private void Initialize()
        {
            view = GetComponent<Camera>();
            // The saved scene retains the previous release's serialized values. This
            // release profile must apply to that camera as well as newly added cameras.
            exposure = 1.04f;
            contactStrength = 1.4f;
            bloomStrength = .08f;
            reflectionStrength = .38f;
            heightHaze = .0032f;
            view.farClipPlane = Mathf.Max(view.farClipPlane, 900f);
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!browserProfileInitialized)
            {
                browserProfileInitialized = true;
                mobileBrowser = Application.isMobilePlatform;
                // Use the same phone/iPad/coarse-touch detection as the controls,
                // including iPad's desktop-style user agent. Camera initialization
                // may happen before SlopCityPlayerInput.Awake.
                try { mobileBrowser |= SlopCityUsesTouch() != 0; }
                catch (System.EntryPointNotFoundException) { }
                catch (System.DllNotFoundException) { }
                // Explicit QA selects the path before the first camera frame. The
                // normal Web profile and all memory-pressure recovery stay Forward.
                try { diagnosticDeferred = SlopCity_IsDeferredRenderingDiagnosticsEnabled() != 0; }
                catch (System.EntryPointNotFoundException) { }
                catch (System.DllNotFoundException) { }
            }
            // WebKit can terminate the process before a lowMemory callback. Select
            // the LDR target immediately on phones instead of first allocating a
            // full-resolution half-float camera surface and waiting for recovery.
            view.allowHDR = !mobileBrowser && !lightweightFinish && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);
            view.allowMSAA = false;
            view.renderingPath = diagnosticDeferred && !lightweightFinish
                ? RenderingPath.DeferredShading : RenderingPath.Forward;
            view.depthTextureMode = DepthTextureMode.None;
            view.farClipPlane = 900f; // Preserve the full-size skyline beyond the expanded street districts.
            QualitySettings.pixelLightCount = 2;
            QualitySettings.shadows = ShadowQuality.HardOnly;
            QualitySettings.shadowDistance = 24f;
            QualitySettings.shadowResolution = ShadowResolution.Low;
            QualitySettings.shadowCascades = 0;
            QualitySettings.antiAliasing = 0;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable; // Preserve authored road/paving detail at grazing angles.
            QualitySettings.realtimeReflectionProbes = false;
#else
            lightweightFinish |= Application.isMobilePlatform || SystemInfo.supportedRenderTargetCount < 4
                || SystemInfo.graphicsShaderLevel < 35;
            view.allowHDR = !lightweightFinish && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);
            view.allowMSAA = false;
            view.renderingPath = lightweightFinish ? RenderingPath.Forward : RenderingPath.DeferredShading;
            view.depthTextureMode = lightweightFinish ? DepthTextureMode.None : DepthTextureMode.DepthNormals;
#endif
            if (!finishShader) finishShader = Shader.Find("Hidden/SlopCity/CameraFinish");
            if (!material && finishShader && finishShader.isSupported)
                material = CreateFinishMaterial(finishShader);
        }

        private static Material CreateFinishMaterial(Shader shader)
        {
            Material result = null;
            try
            {
                result = new Material(shader);
                result.hideFlags = HideFlags.HideAndDontSave;
                return result;
            }
            catch
            {
                if (result) { if (Application.isPlaying) Destroy(result); else DestroyImmediate(result); }
                throw;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void Update()
        {
            if (!view || Time.unscaledTime < nextLightBudget) return;
            nextLightBudget = Time.unscaledTime + .5f;
            if (browserLights == null)
            {
                Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                browserLights = new BrowserLight[lights.Length];
                for (int i = 0; i < lights.Length; i++)
                {
                    Light lamp = lights[i];
                    browserLights[i] = new BrowserLight { light = lamp, cullingMask = lamp.cullingMask };
                    if (lamp.type == LightType.Directional)
                    {
                        lamp.shadows = LightShadows.Hard;
                        lamp.shadowCustomResolution = 1024;
                    }
                    else lamp.shadows = LightShadows.None;
                }
            }
            // Preserve Light.enabled for gameplay switches, but render at most eight
            // nearby local lights. Distant emissive signs retain their authored glow.
            foreach (BrowserLight entry in browserLights)
                if (entry.light && entry.light.type != LightType.Directional) entry.light.cullingMask = 0;
            for (int slot = 0; slot < 8; slot++)
            {
                int best = -1;
                float bestScore = float.PositiveInfinity;
                for (int i = 0; i < browserLights.Length; i++)
                {
                    BrowserLight entry = browserLights[i];
                    Light lamp = entry.light;
                    if (!lamp || !lamp.isActiveAndEnabled || lamp.type == LightType.Directional
                        || entry.cullingMask == 0 || lamp.cullingMask != 0) continue;
                    float distance = (lamp.transform.position - view.transform.position).sqrMagnitude;
                    float reach = lamp.range + 28f;
                    if (distance > reach * reach) continue;
                    float score = distance - lamp.range * lamp.range * .35f;
                    if (score < bestScore) { best = i; bestScore = score; }
                }
                if (best < 0) break;
                browserLights[best].light.cullingMask = browserLights[best].cullingMask;
            }
        }
#endif

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            RenderTexture occlusion = null, contactBlur = null, bloom = null, blur = null;
            RenderTexture reflection = null, reflectedBlur = null, graded = null;
            try
            {
                if (!material) Initialize();
                if (!material) { Graphics.Blit(source, destination); return; }
                material.SetFloat("_Exposure", exposure);
                material.SetFloat("_BloomStrength", 0);
                material.SetTexture("_BloomTex", Texture2D.blackTexture);
#if UNITY_WEBGL && !UNITY_EDITOR
                PrepareForwardBloom(source, ref bloom, ref blur);
                Graphics.Blit(source, destination, material, 8);
#else
                if (lightweightFinish)
                {
                    PrepareForwardBloom(source, ref bloom, ref blur);
                    Graphics.Blit(source, destination, material, 8);
                    return;
                }
                int width = Mathf.Max(1, source.width / 2), height = Mathf.Max(1, source.height / 2);
                RenderTextureFormat hdr = view.allowHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
                occlusion = Acquire(width, height, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                contactBlur = Acquire(width, height, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                bloom = Acquire(width / 2 + 1, height / 2 + 1, hdr);
                blur = Acquire(bloom.width, bloom.height, hdr);
                reflection = Acquire(bloom.width, bloom.height, hdr);
                reflectedBlur = Acquire(bloom.width, bloom.height, hdr);
                graded = Acquire(source.width, source.height, hdr);
                float tangent = Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad);
                material.SetVector("_ViewScale", new Vector4(tangent * view.aspect, tangent, view.farClipPlane, contactStrength));
                material.SetFloat("_Exposure", exposure);
                material.SetFloat("_BloomStrength", bloomStrength);
                material.SetFloat("_ReflectionStrength", reflectionStrength);
                material.SetFloat("_HeightHaze", heightHaze);
                material.SetFloat("_BloomThreshold", 1.05f);
                Bounds ground = SlopCityDenseLayout.GroundBounds;
                material.SetVector("_WetGroundBounds", new Vector4(ground.min.x, ground.max.x, ground.min.z, ground.max.z));
                material.SetMatrix("_SlopViewToWorld", view.cameraToWorldMatrix);
                material.SetVector("_EyeWorld", view.transform.position);
                material.SetColor("_HazeColor", new Color(.24f, .35f, .48f, 1));
                material.SetFloat("_DeferredAvailable", view.actualRenderingPath == RenderingPath.DeferredShading ? 1 : 0);
                Graphics.Blit(source, occlusion, material, 0);
                material.SetVector("_BlurDirection", new Vector4(1f / width, 0, 0, 0));
                Graphics.Blit(occlusion, contactBlur, material, 6);
                material.SetVector("_BlurDirection", new Vector4(0, 1f / height, 0, 0));
                Graphics.Blit(contactBlur, occlusion, material, 6);
                Graphics.Blit(source, reflection, material, 4);
                material.SetVector("_BlurDirection", new Vector4(1f / reflection.width, 0, 0, 0));
                Graphics.Blit(reflection, reflectedBlur, material, 5);
                material.SetVector("_BlurDirection", new Vector4(0, 1f / reflection.height, 0, 0));
                Graphics.Blit(reflectedBlur, reflection, material, 5);
                Graphics.Blit(source, bloom, material, 1);
                material.SetVector("_BlurDirection", new Vector4(1f / bloom.width, 0, 0, 0));
                Graphics.Blit(bloom, blur, material, 2);
                material.SetVector("_BlurDirection", new Vector4(0, 1f / bloom.height, 0, 0));
                Graphics.Blit(blur, bloom, material, 2);
                material.SetTexture("_ContactTex", occlusion);
                material.SetTexture("_BloomTex", bloom);
                material.SetTexture("_ReflectionTex", reflection);
                Graphics.Blit(source, graded, material, 3);
                Graphics.Blit(graded, destination, material, 7);
#endif
            }
            catch (System.Exception exception)
            {
                // A failed optional effect must not strand the camera's output or retry
                // the same large allocations on every frame.
                ReduceMemory();
                if (!allocationWarning)
                {
                    allocationWarning = true;
                    Debug.LogWarning("City effects reduced after a rendering failure: " + exception.Message, this);
                }
                try { Graphics.Blit(source, destination); }
                catch (System.Exception fallback)
                {
                    enabled = false;
                    Debug.LogWarning("City post-processing disabled: " + fallback.Message, this);
                }
            }
            finally
            {
                Release(occlusion); Release(bloom); Release(blur); Release(contactBlur);
                Release(reflection); Release(reflectedBlur); Release(graded);
            }
        }

        private void PrepareForwardBloom(RenderTexture source, ref RenderTexture bloom, ref RenderTexture blur)
        {
            if (optionalBloomDisabled) return;
            // Two quarter-resolution targets, independently capped for high-DPI phones
            // and Native city resolution. No full-resolution intermediate or depth map.
            int maximumDimension = BrowserBloomMaximumDimension;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (mobileBrowser) maximumDimension = 256;
#endif
            float scale = Mathf.Min(.25f, (float)maximumDimension / Mathf.Max(source.width, source.height));
            int width = Mathf.Clamp(Mathf.CeilToInt(source.width * scale), 1, maximumDimension);
            int height = Mathf.Clamp(Mathf.CeilToInt(source.height * scale), 1, maximumDimension);
            RenderTextureFormat format = view.allowHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            bloom = Acquire(width, height, format);
            blur = Acquire(width, height, format);
            // An LDR-only host can still make bright signs glow; allocation pressure
            // removes this optional pass without changing in-world emissive materials.
            material.SetFloat("_BloomThreshold", view.allowHDR ? 1.05f : .70f);
            Graphics.Blit(source, bloom, material, 1);
            material.SetVector("_BlurDirection", new Vector4(1.6f / width, 0, 0, 0));
            Graphics.Blit(bloom, blur, material, 2);
            material.SetVector("_BlurDirection", new Vector4(0, 1.6f / height, 0, 0));
            Graphics.Blit(blur, bloom, material, 2);
            material.SetTexture("_BloomTex", bloom);
            material.SetFloat("_BloomStrength", .08f);
        }

        private static RenderTexture Acquire(int width, int height, RenderTextureFormat format,
            RenderTextureReadWrite readWrite = RenderTextureReadWrite.Default)
        {
            RenderTexture texture = RenderTexture.GetTemporary(width, height, 0, format, readWrite);
            try
            {
                // A fresh pooled target can defer hardware creation until first use.
                // Request it explicitly before interpreting that state as allocation failure.
                if (!texture || (!texture.IsCreated() && !texture.Create()))
                    throw new System.InvalidOperationException("The graphics device could not allocate an effects target.");
                texture.filterMode = FilterMode.Bilinear;
                return texture;
            }
            catch { Release(texture); throw; }
        }

        private static void Release(RenderTexture texture)
        {
            if (!texture) return;
            try { RenderTexture.ReleaseTemporary(texture); }
            catch (System.Exception error) { Debug.LogWarning("City effects target cleanup failed: " + error.Message); }
        }

        private void OnDisable()
        {
            Application.lowMemory -= ReduceMemory;
#if UNITY_WEBGL && !UNITY_EDITOR
            // The effect can disable itself after a fallback blit failure. Return every
            // surviving lamp's gameplay mask instead of leaving budgeted lamps hidden.
            if (browserLights != null)
            {
                foreach (BrowserLight entry in browserLights)
                    if (entry.light) entry.light.cullingMask = entry.cullingMask;
                browserLights = null;
            }
#endif
            if (!material) return;
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            material = null;
        }
    }
}
