using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>World-occluded text using one saved material and the current dynamic font atlas.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMesh), typeof(MeshRenderer))]
    public sealed class SlopCityWorldText : MonoBehaviour
    {
        [SerializeField] private Material material;
        private TextMesh textMesh;
        private Renderer textRenderer;
        private MaterialPropertyBlock properties;
        private Font requestedFont;
        private string requestedText;
        private int requestedSize;
        private FontStyle requestedStyle;
        private Texture currentAtlas;
        private bool subscribed;
        private static readonly int MainTexture = Shader.PropertyToID("_MainTex");
        private static readonly Queue<SlopCityWorldText> pendingFontRebuild = new Queue<SlopCityWorldText>();
        private static readonly HashSet<SlopCityWorldText> pendingFontRebuildSet = new HashSet<SlopCityWorldText>();
        private const int MaxFontRebuildsPerFrame = 8;
#if UNITY_WEBGL && !UNITY_EDITOR
        private float nextBrowserRefresh;
#endif

        public void Configure(Material material)
        {
            this.material = material;
            Refresh(true);
        }

        private void OnEnable()
        {
            if (!subscribed)
            {
                Font.textureRebuilt += OnFontTextureRebuilt;
                subscribed = true;
            }
            Refresh(true);
#if UNITY_WEBGL && !UNITY_EDITOR
            // Spread the city's signs across frames instead of polling every font
            // and renderer binding on every frame.
            nextBrowserRefresh = Time.unscaledTime + Mathf.Abs(GetInstanceID() % 20) * .01f;
#endif
        }

        // TextMesh can replace its renderer material after a font/text edit.
        // ExecuteAlways also refreshes edited signs without entering Play Mode.
#if UNITY_WEBGL && !UNITY_EDITOR
        private void LateUpdate()
        {
            DrainFontRebuildQueue();
            if (Time.unscaledTime < nextBrowserRefresh) return;
            nextBrowserRefresh = Time.unscaledTime + .2f;
            Refresh(false);
        }
#else
        private void LateUpdate()
        {
            DrainFontRebuildQueue();
            Refresh(false);
        }
#endif

        private static void DrainFontRebuildQueue()
        {
            for (int i = 0; i < MaxFontRebuildsPerFrame && pendingFontRebuild.Count > 0; i++)
            {
                SlopCityWorldText sign = pendingFontRebuild.Dequeue();
                if (!sign || !pendingFontRebuildSet.Remove(sign)) continue;
                sign.Refresh(true);
            }
        }

        private void EnqueueFontRebuild()
        {
            if (pendingFontRebuildSet.Add(this))
                pendingFontRebuild.Enqueue(this);
        }

        private void Refresh(bool force)
        {
            if (!textMesh) textMesh = GetComponent<TextMesh>();
            if (!textRenderer) textRenderer = GetComponent<Renderer>();
            if (!textMesh || !textRenderer || !material || !textMesh.font) return;
            Font font = textMesh.font;
            bool changed = requestedFont != font || requestedText != textMesh.text
                || requestedSize != textMesh.fontSize || requestedStyle != textMesh.fontStyle;
            if (changed)
            {
                // Cache before requesting: a font rebuild may synchronously invoke us.
                requestedFont = font; requestedText = textMesh.text;
                requestedSize = textMesh.fontSize; requestedStyle = textMesh.fontStyle;
                if (font.dynamic)
                    font.RequestCharactersInTexture(requestedText, Mathf.Max(1, requestedSize), requestedStyle);
            }
            bool materialChanged = textRenderer.sharedMaterial != material;
            if (materialChanged) textRenderer.sharedMaterial = material;
            Texture atlas = font.material ? font.material.mainTexture : null;
            if (force || changed || materialChanged || currentAtlas != atlas)
            {
                currentAtlas = atlas;
                if (properties == null) properties = new MaterialPropertyBlock();
                textRenderer.GetPropertyBlock(properties);
                properties.SetTexture(MainTexture, atlas ? atlas : Texture2D.whiteTexture);
                textRenderer.SetPropertyBlock(properties);
            }
        }

        private void OnFontTextureRebuilt(Font font)
        {
            if (textMesh && textMesh.font == font) EnqueueFontRebuild();
        }

        private void OnDisable()
        {
            pendingFontRebuildSet.Remove(this);
            if (subscribed) Font.textureRebuilt -= OnFontTextureRebuilt;
            subscribed = false;
            // The shared saved material owns no transient resources. Restore the
            // native font atlas when this helper is explicitly disabled/removed.
            if (textRenderer && textRenderer.sharedMaterial == material && textMesh && textMesh.font)
            {
                textRenderer.sharedMaterial = textMesh.font.material;
                textRenderer.SetPropertyBlock(null);
            }
            properties = null;
            currentAtlas = null;
        }
    }
}
