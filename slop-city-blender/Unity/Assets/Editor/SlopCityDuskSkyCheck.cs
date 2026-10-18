#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Sky-only readback through the actual game camera's existing finish; no production state survives.</summary>
    public static class SlopCityDuskSkyCheck
    {
        public static void Capture(Camera productionCamera, string prefix)
        {
            if (!Application.isPlaying || !productionCamera)
                throw new InvalidOperationException("Dusk sky evidence requires the actual production camera in graphics Play Mode.");
            if (string.IsNullOrWhiteSpace(prefix) || prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || prefix.Contains("/") || prefix.Contains("\\")) throw new ArgumentException("Use a filename prefix.");
            Camera view = productionCamera;
            Skybox overrideSky = view.GetComponent<Skybox>();
            Material effectiveSky = overrideSky && overrideSky.enabled && overrideSky.material ? overrideSky.material : RenderSettings.skybox;
            SlopCityCameraFinish finish = view.GetComponent<SlopCityCameraFinish>();
            var receipt = new Receipt
            {
                capturedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceType.ToString(), camera = view.name,
                initialClearFlags = view.clearFlags.ToString(), cameraSkyOverride = overrideSky && overrideSky.enabled && overrideSky.material,
                effectiveSky = effectiveSky ? effectiveSky.name : "", shader = effectiveSky && effectiveSky.shader ? effectiveSky.shader.name : "",
                shaderSupported = effectiveSky && effectiveSky.shader && effectiveSky.shader.isSupported,
                productionFinishEnabled = finish && finish.enabled, colorSpace = QualitySettings.activeColorSpace.ToString()
            };
            Vector3 savedPosition = view.transform.localPosition;
            Quaternion savedRotation = view.transform.localRotation;
            int savedMask = view.cullingMask;
            CameraClearFlags savedFlags = view.clearFlags;
            RenderTexture savedTarget = view.targetTexture, savedActive = RenderTexture.active;
            float savedAspect = view.aspect;
            bool savedSRGB = GL.sRGBWrite;
            const int height = 720;
            int width = Mathf.Clamp(Mathf.RoundToInt(height * savedAspect), 720, 1800);
            RenderTexture target = null, display = null;
            Texture2D pixels = null;
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports");
            Directory.CreateDirectory(folder);
            try
            {
                target = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
                display = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                if (!target.Create() || !display.Create()) throw new InvalidOperationException("Sky readback targets did not allocate.");
                view.cullingMask = 0; view.clearFlags = CameraClearFlags.Skybox; view.targetTexture = target;
                // Keep the production finish and exact material; only hide the world
                // and change the look direction. No exposure/cloud settings are altered.
                Vector3 sun = effectiveSky && effectiveSky.HasProperty("_SunDirection")
                    ? (Vector3)effectiveSky.GetVector("_SunDirection")
                    : RenderSettings.sun ? -RenderSettings.sun.transform.forward : new Vector3(.82f, .24f, -.51f);
                Vector3 azimuth = Vector3.ProjectOnPlane(sun, Vector3.up).normalized;
                if (azimuth.sqrMagnitude < .001f) azimuth = Vector3.forward;
                Vector3 low = azimuth * Mathf.Cos(14f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(14f * Mathf.Deg2Rad);
                Vector3 zenith = (Vector3.up + azimuth * .12f).normalized;
                foreach (var shot in new[] { new Shot { label = "sun-14deg", forward = low, up = Vector3.up },
                    new Shot { label = "tilted-zenith", forward = zenith, up = azimuth } })
                {
                    view.transform.rotation = Quaternion.LookRotation(shot.forward, shot.up);
                    view.Render();
                    GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(target, display);
                    GL.sRGBWrite = savedSRGB;
                    RenderTexture.active = display;
                    pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                    string path = Path.Combine(folder, prefix + "-sky-" + shot.label + ".png");
                    File.WriteAllBytes(path, pixels.EncodeToPNG());
                    receipt.frames.Add(Measure(pixels.GetPixels(), width, height, shot.label, path, shot.forward));
                }
                Frame zenithFrame = receipt.frames[1];
                receipt.visibleVariation = zenithFrame.channelRange > .035 && zenithFrame.luminanceSigma > .008;
                receipt.visibleCloudContrast = receipt.visibleVariation && zenithFrame.meanHorizontalCurvature > .002;
                receipt.productionSkyActive = receipt.initialClearFlags == CameraClearFlags.Skybox.ToString()
                    && receipt.shader == "SlopCity/CloudedDusk" && receipt.shaderSupported && receipt.productionFinishEnabled;
            }
            catch (Exception error) { receipt.error = error.ToString(); throw; }
            finally
            {
                view.transform.localPosition = savedPosition; view.transform.localRotation = savedRotation;
                view.cullingMask = savedMask; view.clearFlags = savedFlags; view.targetTexture = savedTarget;
                RenderTexture.active = savedActive; GL.sRGBWrite = savedSRGB;
                if (target) { target.Release(); Object.DestroyImmediate(target); }
                if (display) { display.Release(); Object.DestroyImmediate(display); }
                if (pixels) Object.DestroyImmediate(pixels);
                receipt.restored = view.cullingMask == savedMask && view.clearFlags == savedFlags && view.targetTexture == savedTarget
                    && view.transform.localPosition == savedPosition && Quaternion.Angle(view.transform.localRotation, savedRotation) < .001f
                    && Mathf.Abs(view.aspect - savedAspect) < .0001f
                    && RenderTexture.active == savedActive && GL.sRGBWrite == savedSRGB;
                File.WriteAllText(Path.Combine(folder, prefix + "-sky-check.json"), JsonUtility.ToJson(receipt, true));
            }
        }

        private static double Luma(Color pixel) => pixel.r * .2126 + pixel.g * .7152 + pixel.b * .0722;
        private static Frame Measure(Color[] pixels, int width, int height, string label, string path, Vector3 direction)
        {
            // Crop edges where vignette is strongest. Horizontal second differences
            // detect irregular cloud structure while rejecting a uniform colour or
            // a linear horizon gradient; they remain visual evidence, not sky physics.
            double sum = 0, squares = 0, curvature = 0;
            Color low = Color.white, high = Color.black;
            int count = 0, curvatureCount = 0, offset = Mathf.Max(2, width / 16);
            for (int y = height / 5; y < height * 4 / 5; y++)
                for (int x = width / 5; x < width * 4 / 5; x++)
                {
                    Color color = pixels[y * width + x]; double luma = Luma(color);
                    sum += luma; squares += luma * luma; count++;
                    low.r = Mathf.Min(low.r, color.r); low.g = Mathf.Min(low.g, color.g); low.b = Mathf.Min(low.b, color.b);
                    high.r = Mathf.Max(high.r, color.r); high.g = Mathf.Max(high.g, color.g); high.b = Mathf.Max(high.b, color.b);
                    curvature += Math.Abs(luma - (Luma(pixels[y * width + x - offset]) + Luma(pixels[y * width + x + offset])) * .5);
                    curvatureCount++;
                }
            double mean = sum / count;
            return new Frame { label = label, path = path, width = width, height = height, direction = direction,
                meanDisplayLuminance = mean, luminanceSigma = Math.Sqrt(Math.Max(0, squares / count - mean * mean)),
                meanHorizontalCurvature = curvature / curvatureCount, minColor = low, maxColor = high,
                channelRange = Mathf.Max(high.r - low.r, Mathf.Max(high.g - low.g, high.b - low.b)) };
        }

        private sealed class Shot { public string label; public Vector3 forward, up; }
        [Serializable] private sealed class Receipt
        {
            public string capturedAtUtc, unityVersion, graphicsDevice, camera, initialClearFlags, effectiveSky, shader, colorSpace, error;
            public bool cameraSkyOverride, shaderSupported, productionFinishEnabled, productionSkyActive, visibleVariation, visibleCloudContrast, restored;
            public string scope = "Sky-only production-camera render with the existing sky material and image finish. Linear HDR output converted to display sRGB. Cloud flags are bounded image-contrast diagnostics; inspect the two PNGs. No material, lighting, finish or scene assets changed. Does not establish browser/phone shader output.";
            public List<Frame> frames = new List<Frame>();
        }
        [Serializable] private sealed class Frame
        {
            public string label, path; public int width, height; public Vector3 direction; public Color minColor, maxColor;
            public double meanDisplayLuminance, luminanceSigma, meanHorizontalCurvature; public float channelRange;
        }
    }
}
#endif
