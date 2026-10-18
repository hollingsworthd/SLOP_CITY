#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SlopCity.Editor
{
    /// <summary>Isolated Editor compositor regression. Does not open, regenerate or save game scenes.</summary>
    public static class SlopCityWebPresentationCheck
    {
        [Serializable] private sealed class Receipt
        {
            public bool passed;
            public string checkedAtUtc, unityVersion;
            public string scope = "Isolated Editor RenderTexture allocation, camera projection, UI separation, lifecycle and native-install policy. Browser raster sharpness, mobile hardware and WebGL rendering are verified separately.";
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public List<Observation> resolutions = new List<Observation>();
        }

        [Serializable] private sealed class Observation
        {
            public int displayWidth, displayHeight, budget, worldWidth, worldHeight;
            public float cameraAspect;
        }

        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            var receipt = new Receipt { checkedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            Scene scene = default;
            try
            {
                scene = EditorSceneManager.NewPreviewScene();
                var cameraObject = new GameObject("PresentationCheckCamera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>();
                camera.transform.SetPositionAndRotation(new Vector3(2, 3, -4), Quaternion.Euler(11, 37, 0));
                camera.nearClipPlane = .17f;
                camera.fieldOfView = 67f;
                camera.aspect = 1920f / 1080f;
                Ray centerBefore = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
                Ray sideBefore = camera.ViewportPointToRay(new Vector3(.75f, .5f, 0));

                var hudObject = new GameObject("IndependentGameplayHud", typeof(RectTransform), typeof(Canvas));
                SceneManager.MoveGameObjectToScene(hudObject, scene);
                var hud = hudObject.GetComponent<Canvas>();
                hud.renderMode = RenderMode.ScreenSpaceOverlay;
                hud.sortingOrder = 100;
                var owner = new GameObject("PresentationCheckOwner", typeof(SlopCityWebPresentation));
                SceneManager.MoveGameObjectToScene(owner, scene);
                var presentation = owner.GetComponent<SlopCityWebPresentation>();
                Set(presentation, "worldCamera", camera);
                Set(presentation, "originalAspect", camera.aspect);
                Set(presentation, "originalAutomaticAspect", false);

                Require(Mathf.Approximately(SlopCityWebPresentation.DisplayPixelRatio, 1f),
                    "Native/Editor display pixel ratio stays at one.", receipt);
                var install = typeof(SlopCityWebPresentation).GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                Require(install == null, "Browser presentation has no automatic installation method in the Editor/native compilation.", receipt);

                RenderTexture initial = Present(presentation, camera, 1920, 1080, 720, receipt);
                Require(initial.width == 1280 && initial.height == 720,
                    "1920 by 1080 display keeps the world at 1280 by 720 in Fast mode.", receipt);
                Require(Mathf.Approximately(camera.aspect, 1920f / 1080f),
                    "World camera retains display aspect independently of the texture budget.", receipt);
                Require(SameRay(centerBefore, camera.ViewportPointToRay(new Vector3(.5f, .5f, 0))),
                    "Center-bullseye ray remains unchanged after redirecting the world to a texture.", receipt);
                Require(SameRay(sideBefore, camera.ViewportPointToRay(new Vector3(.75f, .5f, 0))),
                    "Off-center projection remains unchanged at the same display aspect.", receipt);

                Canvas display = Get<Canvas>(presentation, "presentationCanvas");
                RawImage image = Get<RawImage>(presentation, "worldImage");
                Require(display && display.enabled && display.renderMode == RenderMode.ScreenSpaceOverlay && display.sortingOrder < hud.sortingOrder,
                    "World presentation is a screen overlay beneath the separate gameplay HUD.", receipt);
                Require(image && image.texture == initial && !image.raycastTarget
                    && display.GetComponentsInChildren<GraphicRaycaster>(true).Length == 0,
                    "World display neither owns a raycast surface nor installs an input raycaster.", receipt);
                Require(!hud.transform.IsChildOf(camera.transform) && !hud.transform.IsChildOf(display.transform)
                    && hud.renderMode == RenderMode.ScreenSpaceOverlay && !hud.worldCamera,
                    "Gameplay HUD stays outside the world camera and its texture presentation.", receipt);
                hud.enabled = false;
                Require(display.enabled && image.texture == camera.targetTexture,
                    "Hiding gameplay HUD leaves the world presentation enabled.", receipt);
                hud.enabled = true;

                for (int i = 0; i < 20; i++) Ensure(presentation, 1920, 1080, 720, 1f);
                Require(camera.targetTexture == initial && initial.IsCreated(),
                    "Unchanged presentation dimensions reuse the same allocated world texture.", receipt);
                Ensure(presentation, 1920, 1080, 720, 2f);
                Require(camera.targetTexture == initial && initial.IsCreated(),
                    "A pixel-ratio report change alone does not reallocate an unchanged world target.", receipt);

                RenderTexture low = Present(presentation, camera, 1920, 1080, 540, receipt);
                Require(low.width == 960 && low.height == 540 && low != initial && !initial,
                    "Switching to Low mode produces a 960 by 540 world and destroys the superseded target.", receipt);
                Require(image.texture == low && camera.targetTexture == low,
                    "Both display and camera reference the replacement target after a budget change.", receipt);
                RenderTexture full = Present(presentation, camera, 1920, 1080, 0, receipt);
                Require(full.width == 1920 && full.height == 1080 && !low,
                    "Native world mode uses the full display backing and releases the Low target.", receipt);

                RenderTexture portrait = Present(presentation, camera, 1170, 2256, 540, receipt);
                Require(portrait.height <= 540 && portrait.width <= 960 && !full
                    && Mathf.Approximately(camera.aspect, 1170f / 2256f),
                    "Portrait resize keeps the world budget bounded while preserving exact display aspect.", receipt);
                Require(image.texture == portrait && camera.targetTexture == portrait,
                    "Portrait resize hands the same live texture to camera and display.", receipt);
                Require(SameRay(centerBefore, camera.ViewportPointToRay(new Vector3(.5f, .5f, 0))),
                    "Portrait resizing preserves the center interaction ray.", receipt);
                Ensure(presentation, 1170, 2256, 540, 1f);
                Require(camera.targetTexture == portrait,
                    "A settled portrait presentation reuses its existing texture.", receipt);
                Require(hud.enabled && hud.sortingOrder == 100 && hud.renderMode == RenderMode.ScreenSpaceOverlay,
                    "Budget and orientation changes preserve the independent HUD configuration.", receipt);

                Invoke(presentation, "OnDisable");
                Require(!camera.targetTexture && !portrait && !image.texture && !display.enabled,
                    "Disabling presentation detaches the camera, clears the image and destroys the world target.", receipt);
                Require(Mathf.Approximately(camera.aspect, 1920f / 1080f),
                    "Presentation cleanup restores the original explicit camera aspect.", receipt);
                Require(hud.enabled, "Presentation cleanup leaves the independent HUD enabled.", receipt);
                Invoke(presentation, "OnDisable");
                Require(!camera.targetTexture && !display.enabled,
                    "Repeated cleanup is safe and does not reconnect a destroyed texture.", receipt);
            }
            catch (Exception error) { receipt.errors.Add(error.ToString()); }
            finally
            {
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
            receipt.passed = receipt.errors.Count == 0;
            string directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "web-v0.9.3-presentation-check.json"), JsonUtility.ToJson(receipt, true));
            Debug.Log("SLOP_CITY_WEB_PRESENTATION_CHECK_" + (receipt.passed ? "PASSED" : "FAILED"));
            if (!receipt.passed) throw new InvalidOperationException(string.Join("\n", receipt.errors));
        }

        private static RenderTexture Present(SlopCityWebPresentation presentation, Camera camera, int width, int height, int budget, Receipt receipt)
        {
            Ensure(presentation, width, height, budget, 1f);
            RenderTexture target = camera.targetTexture;
            Require(target && target.IsCreated(), "World target is allocated for display " + width + " by " + height + ", budget " + budget + ".", receipt);
            receipt.resolutions.Add(new Observation { displayWidth = width, displayHeight = height, budget = budget,
                worldWidth = target.width, worldHeight = target.height, cameraAspect = camera.aspect });
            return target;
        }

        private static bool SameRay(Ray a, Ray b)
        {
            return Vector3.Distance(a.origin, b.origin) < .0001f && Vector3.Distance(a.direction, b.direction) < .0001f;
        }

        private static void Ensure(SlopCityWebPresentation presentation, int width, int height, int budget, float ratio)
        {
            MethodInfo method = typeof(SlopCityWebPresentation).GetMethod("EnsureTarget", PrivateInstance);
            if (method == null) throw new MissingMethodException("SlopCityWebPresentation.EnsureTarget");
            method.Invoke(presentation, new object[] { width, height, budget, ratio });
        }

        private static void Invoke(SlopCityWebPresentation presentation, string name)
        {
            MethodInfo method = typeof(SlopCityWebPresentation).GetMethod(name, PrivateInstance);
            if (method == null) throw new MissingMethodException("SlopCityWebPresentation." + name);
            method.Invoke(presentation, null);
        }

        private static T Get<T>(SlopCityWebPresentation presentation, string name) where T : UnityEngine.Object
        {
            FieldInfo field = typeof(SlopCityWebPresentation).GetField(name, PrivateInstance);
            if (field == null) throw new MissingFieldException("SlopCityWebPresentation", name);
            return field.GetValue(presentation) as T;
        }

        private static void Set(SlopCityWebPresentation presentation, string name, object value)
        {
            FieldInfo field = typeof(SlopCityWebPresentation).GetField(name, PrivateInstance);
            if (field == null) throw new MissingFieldException("SlopCityWebPresentation", name);
            field.SetValue(presentation, value);
        }

        private static void Require(bool condition, string message, Receipt receipt)
        {
            if (!condition) throw new InvalidOperationException(message);
            receipt.checks.Add(message);
        }
    }
}
#endif
