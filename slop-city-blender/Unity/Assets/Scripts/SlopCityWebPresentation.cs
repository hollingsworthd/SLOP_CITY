using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

namespace SlopCity
{
    /// <summary>Browser UI uses the display pixels while the world keeps a separate render budget.</summary>
    public sealed class SlopCityWebPresentation : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int SlopCity_GetSceneRenderHeight();
        [DllImport("__Internal")] private static extern float SlopCity_GetDisplayPixelRatio();
        [DllImport("__Internal")] private static extern void SlopCity_ReportPresentation(int displayWidth, int displayHeight, int worldWidth, int worldHeight, float pixelRatio);
#endif
        private Camera worldCamera;
        private RenderTexture worldTexture;
        private Canvas presentationCanvas;
        private RawImage worldImage;
        private float originalAspect;
        private bool originalAutomaticAspect;
        private int lastDisplayWidth, lastDisplayHeight, lastBudget = -1;
        private float lastPixelRatio;
        private float nextAllocation;
        private int fallbackHeight;
        private bool suspendedCamera;
        private bool allocationWarning;
        private Text recoveryMessage;

        private void OnEnable() { Application.lowMemory += ReduceMemory; }
        private void ReduceMemory()
        {
            fallbackHeight = 540;
            lastBudget = -1;
            nextAllocation = 0;
        }

        public static float DisplayPixelRatio
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                try
                {
                    float ratio = SlopCity_GetDisplayPixelRatio();
                    return float.IsNaN(ratio) || float.IsInfinity(ratio) ? 1f : Mathf.Max(1f, ratio);
                }
                catch (EntryPointNotFoundException) { return 1f; }
                catch (DllNotFoundException) { return 1f; }
#else
                return 1f;
#endif
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var host = new GameObject("SlopCityBrowserPresentation");
            DontDestroyOnLoad(host);
            host.AddComponent<SlopCityWebPresentation>();
        }
#endif

        private static int SceneRenderHeight()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                int budget = SlopCity_GetSceneRenderHeight();
                return budget == 0 || budget == 540 || budget == 720 || budget == 1080 ? budget : 720;
            }
            catch (EntryPointNotFoundException) { return 720; }
            catch (DllNotFoundException) { return 720; }
#else
            return 720;
#endif
        }

        private void LateUpdate() { RefreshPresentation(); }

        private void RefreshPresentation()
        {
            Camera current = Camera.main;
            // Camera.main omits the camera we deliberately disabled while recovering.
            if (!current && suspendedCamera && worldCamera && worldCamera.gameObject.activeInHierarchy) current = worldCamera;
            if (current != worldCamera)
            {
                ReleaseWorld();
                if (!current || current.targetTexture) return;
                worldCamera = current;
                originalAspect = current.aspect;
                originalAutomaticAspect = Mathf.Approximately(originalAspect, Screen.width / (float)Mathf.Max(1, Screen.height));
            }
            if (!worldCamera || (!worldCamera.isActiveAndEnabled && !suspendedCamera))
            {
                if (presentationCanvas) presentationCanvas.enabled = false;
                return;
            }
            EnsureTarget(Screen.width, Screen.height, SceneRenderHeight(), DisplayPixelRatio);
        }

        private void EnsureTarget(int displayWidth, int displayHeight, int budget, float pixelRatio)
        {
            if (!worldCamera || displayWidth <= 0 || displayHeight <= 0) return;
            if (worldTexture && worldTexture.IsCreated() && presentationCanvas && worldImage && recoveryMessage && !suspendedCamera && lastDisplayWidth == displayWidth && lastDisplayHeight == displayHeight
                && lastBudget == budget && Mathf.Approximately(lastPixelRatio, pixelRatio))
            {
                if (presentationCanvas) presentationCanvas.enabled = true;
                return;
            }
            if (Time.unscaledTime < nextAllocation) return;
            int effectiveBudget = fallbackHeight > 0 ? (budget > 0 ? Mathf.Min(budget, fallbackHeight) : fallbackHeight) : budget;
            float scale = effectiveBudget > 0 ? Mathf.Min(1f, effectiveBudget / (float)displayHeight, effectiveBudget * (16f / 9f) / displayWidth) : 1f;
            int maximum = Mathf.Max(1, SystemInfo.maxTextureSize);
            scale = Mathf.Min(scale, maximum / (float)displayWidth, maximum / (float)displayHeight);
            int width = Mathf.Max(1, Mathf.RoundToInt(displayWidth * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(displayHeight * scale));
            RenderTexture candidate = null;
            try
            {
                if (!worldTexture || !worldTexture.IsCreated() || worldTexture.width != width || worldTexture.height != height)
                {
                    candidate = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                    {
                        name = "SlopCityBrowserWorld", filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp, useMipMap = false,
                        autoGenerateMips = false, antiAliasing = 1
                    };
                    if (!candidate.Create()) throw new InvalidOperationException("The city render target could not be allocated.");
                }
                // Keep the previous usable target until the replacement and canvas exist.
                EnsureCanvas();
                RenderTexture target = candidate ? candidate : worldTexture;
                worldCamera.targetTexture = target;
                if (suspendedCamera) { worldCamera.enabled = true; suspendedCamera = false; }
                worldCamera.aspect = displayWidth / (float)displayHeight;
                worldImage.texture = target;
                presentationCanvas.enabled = true;
                if (recoveryMessage) recoveryMessage.enabled = false;
                lastDisplayWidth = displayWidth;
                lastDisplayHeight = displayHeight;
                lastBudget = budget;
                lastPixelRatio = pixelRatio;
                allocationWarning = false;
                if (candidate)
                {
                    RenderTexture previous = worldTexture;
                    worldTexture = candidate;
                    candidate = null;
                    DisposeTexture(previous);
                }
            }
            catch (Exception error)
            {
                fallbackHeight = fallbackHeight == 0 ? 540 : 320;
                nextAllocation = Time.unscaledTime + 5f;
                if (!allocationWarning)
                {
                    allocationWarning = true;
                    Debug.LogWarning("City rendering is recovering at reduced detail: " + error.Message, this);
                }
                try
                {
                    bool usable = worldTexture && worldTexture.IsCreated();
                    worldCamera.targetTexture = usable ? worldTexture : null;
                    if (!usable)
                    {
                        // Avoid an accidental full-resolution world while its budgeted
                        // target is unavailable. The HUD/menu canvases remain available.
                        worldCamera.enabled = false;
                        suspendedCamera = true;
                    }
                    EnsureCanvas();
                    worldImage.texture = usable ? worldTexture : null;
                    worldImage.color = usable ? Color.white : Color.black;
                    recoveryMessage.enabled = !usable;
                    presentationCanvas.enabled = true;
                }
                catch (Exception)
                {
                    // Creating the recovery canvas can fail for the same allocation
                    // reason. Keep the bounded retry instead of throwing every frame.
                    if (presentationCanvas) presentationCanvas.enabled = false;
                }
                return;
            }
            finally
            {
                DisposeTexture(candidate);
            }
            worldImage.color = Color.white;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { SlopCity_ReportPresentation(displayWidth, displayHeight, width, height, pixelRatio); }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
#endif
        }

        private void EnsureCanvas()
        {
            if (presentationCanvas && worldImage && recoveryMessage) return;
            GameObject canvasObject = null;
            try
            {
                canvasObject = new GameObject("BrowserWorldPresentation", typeof(RectTransform), typeof(Canvas));
                canvasObject.SetActive(false);
                canvasObject.transform.SetParent(transform, false);
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = -1000;
                var imageObject = new GameObject("WorldImage", typeof(RectTransform), typeof(RawImage));
                imageObject.transform.SetParent(canvasObject.transform, false);
                var rect = imageObject.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                RawImage image = imageObject.GetComponent<RawImage>();
                image.color = Color.white;
                image.raycastTarget = false;
                var messageObject = new GameObject("GraphicsRecovery", typeof(RectTransform), typeof(Text));
                messageObject.transform.SetParent(canvasObject.transform, false);
                Text message = messageObject.GetComponent<Text>();
                message.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                message.text = "Graphics memory is unavailable. Retrying at lower city detail…\nIf this persists, reload the page.";
                message.fontSize = 22;
                message.color = Color.white;
                message.alignment = TextAnchor.MiddleCenter;
                message.raycastTarget = false;
                message.rectTransform.anchorMin = new Vector2(.1f, .35f);
                message.rectTransform.anchorMax = new Vector2(.9f, .65f);
                message.rectTransform.offsetMin = message.rectTransform.offsetMax = Vector2.zero;
                message.enabled = false;
                // Publish only the complete canvas, so a failed allocation is retryable.
                if (presentationCanvas)
                {
                    presentationCanvas.gameObject.SetActive(false);
                    Destroy(presentationCanvas.gameObject);
                }
                presentationCanvas = canvas;
                worldImage = image;
                recoveryMessage = message;
                canvasObject.SetActive(true);
                canvasObject = null;
                // No GraphicRaycaster: this display never owns mouse or touch input.
            }
            finally
            {
                if (canvasObject) { canvasObject.SetActive(false); Destroy(canvasObject); }
            }
        }

        private static void DisposeTexture(RenderTexture texture)
        {
            if (!texture) return;
            try { texture.Release(); }
            catch (Exception error) { Debug.LogWarning("City render target cleanup failed: " + error.Message); }
            finally { if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture); }
        }

        private void ReleaseTexture()
        {
            RenderTexture previous = worldTexture;
            worldTexture = null;
            DisposeTexture(previous);
        }

        private void ReleaseWorld()
        {
            if (worldCamera)
            {
                if (suspendedCamera) worldCamera.enabled = true;
                if (worldCamera.targetTexture == worldTexture) worldCamera.targetTexture = null;
                if (originalAutomaticAspect) worldCamera.ResetAspect(); else worldCamera.aspect = originalAspect;
            }
            worldCamera = null;
            suspendedCamera = false;
            if (worldImage) worldImage.texture = null;
            if (presentationCanvas) presentationCanvas.enabled = false;
            ReleaseTexture();
            lastDisplayWidth = lastDisplayHeight = 0;
            lastBudget = -1;
            nextAllocation = 0;
        }

        private void OnDisable() { Application.lowMemory -= ReduceMemory; ReleaseWorld(); }
        private void OnDestroy() { ReleaseWorld(); }
    }
}
