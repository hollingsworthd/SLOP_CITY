#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Bounded saved-city Play Mode render and production mobile UI pointer checks.</summary>
    [InitializeOnLoad]
    public static class SlopCitySalvageVisualSmokeTest
    {
        private const string Active = "SlopCity.SalvageVisual.Active", Backup = "SlopCity.SalvageVisual.Backup", Started = "SlopCity.SalvageVisual.Started";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] Keys = { "SlopCity.Sandbox.Progress.v1", "SlopCity.Sandbox.Progress.v1.recovery", "SlopCity.MatrixSearch.Progress.v1", "SlopCity.MatrixSearch.Progress.v1.recovery" };
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt receipt;
        private static SlopCityFirstPerson player;
        private static SlopCitySandbox sandbox;
        private static SlopCityPlayerInput input;
        private static Camera view;
        private static bool started;
        private static int lastFrame = -1;
        static SlopCitySalvageVisualSmokeTest()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += (message, trace, kind) =>
            {
                if (!SessionState.GetBool(Active, false) || (kind != LogType.Error && kind != LogType.Exception && kind != LogType.Assert)) return;
                if (receipt == null) receipt = NewReceipt();
                string diagnostic = message + "\n" + trace;
                // This pinned Editor's task-owned Search index can fail during
                // startup. Keep the entire diagnostic, separate from game errors.
                if (message.StartsWith("ArgumentOutOfRangeException: Index was out of range", StringComparison.Ordinal)
                    && trace.Contains("UnityEditor.Search.SearchDatabase+<EnumerateAll>")
                    && trace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup")
                    && !trace.Contains("SlopCity.")) receipt.editorDiagnostics.Add(diagnostic);
                else receipt.runtimeErrors.Add(diagnostic);
            };
        }
        public static void Run()
        {
            if (!Application.isBatchMode || Application.companyName != "SlopCityValidation-v0116" || EditorApplication.isPlayingOrWillChangePlaymode
                || !Application.persistentDataPath.Contains(Path.DirectorySeparatorChar + "SlopCityValidation-v0116" + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Visual checks require isolated SlopCityValidation-v0116 batch Edit Mode with graphics.");
            for (int index = 0; index < Keys.Length; index++)
            {
                SessionState.SetBool(Backup + ".Had" + index, PlayerPrefs.HasKey(Keys[index]));
                SessionState.SetString(Backup + index, PlayerPrefs.GetString(Keys[index], ""));
                PlayerPrefs.DeleteKey(Keys[index]);
            }
            SessionState.SetString(Started, DateTime.UtcNow.ToString("O")); SessionState.SetBool(Active, true);
            receipt = NewReceipt();
            try
            {
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                foreach (var sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None)) sync.enabled = false;
                foreach (var media in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None)) media.enabled = false;
                foreach (var lounge in Object.FindObjectsByType<SlopCityMediaPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) lounge.enabled = false;
                foreach (var owner in Object.FindObjectsByType<SlopCityFirstPerson>(FindObjectsInactive.Include, FindObjectsSortMode.None)) owner.enabled = false;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }
        private static Receipt NewReceipt() => new Receipt { startedAtUtc = SessionState.GetString(Started, DateTime.UtcNow.ToString("O")), unityVersion = Application.unityVersion };
        private static void Tick()
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (receipt == null) receipt = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(receipt.startedAtUtc).ToUniversalTime()).TotalSeconds > 180) throw new TimeoutException("Visual checks exceeded three minutes.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                if (!started) { started = true; Work.Push(Exercise()); }
                while (Work.Count > 0)
                {
                    IEnumerator current = Work.Peek();
                    if (!current.MoveNext()) { Work.Pop(); continue; }
                    if (current.Current is IEnumerator child) { Work.Push(child); continue; }
                    return;
                }
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }
        private static IEnumerator Exercise()
        {
            Application.runInBackground = true; Time.timeScale = 1;
            player = Object.FindFirstObjectByType<SlopCityFirstPerson>(); Check(player && !player.enabled, "Saved player autostart remains disabled.");
            view = player.ViewCamera; input = player.GetComponent<SlopCityPlayerInput>();
            Set(input, "forceTouchControlsForTesting", true);
            SlopCityBuildingWorld buildings = SlopCityBuildingWorld.Ensure(player);
            Check(buildings, "Saved-city building access initializes offline."); buildings.enabled = false;
            sandbox = player.GetComponent<SlopCitySandbox>(); if (!sandbox) sandbox = player.gameObject.AddComponent<SlopCitySandbox>();
            sandbox.Initialize(player.transform, _ => { }); Set(player, "sandbox", sandbox);
            Set(player, "started", true); Set(player, "paused", false); Set(player, "seated", false); Set(player, "externalMovement", true);
            player.GetComponent<CharacterController>().enabled = false;
            SlopCityHud hud = Object.FindFirstObjectByType<SlopCityHud>(); Check(hud, "Saved production HUD exists.");
            hud.Initialize(player, input); hud.SetMenu(false, true);
            SlopCitySalvageHud.Ensure(player, sandbox, input, view); input.SetGameplayEnabled(true);
            Check(!Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(sync => sync.enabled), "Sync remains disabled before and during Play Mode.");
            yield return Delay(.25);
            Canvas.ForceUpdateCanvases();
            Check(Screen.width > 0 && Screen.height > 0, "Actual Editor framebuffer has valid dimensions.");
            receipt.resolutions.Add(Screen.width + "x" + Screen.height);

            SlopCitySalvageSurface surface = Object.FindObjectsByType<SlopCitySalvageSurface>(FindObjectsSortMode.None)
                .First(item => item.name == "Human_Scale_Window_Panes");
            int part = Enumerable.Range(0, surface.PartCount).OrderBy(index => surface.PartWorldBounds(index).center.y).First();
            int[] indices = Get<int[]>(surface, "indices"), starts = Get<int[]>(surface, "starts");
            Vector3[] vertices = surface.SourceMesh.vertices; int at = starts[part];
            Vector3 a = vertices[indices[at]], b = vertices[indices[at + 1]], c = vertices[indices[at + 2]];
            Vector3 normal = surface.transform.TransformDirection(Vector3.Cross(b - a, c - a).normalized);
            Vector3 center = surface.transform.TransformPoint((a + b + c) / 3), eye = center + normal * 1.8f;
            player.transform.SetPositionAndRotation(eye - Vector3.up * 1.6f, Quaternion.LookRotation(-normal, Vector3.up));
            Set(player, "yaw", player.transform.eulerAngles.y); Set(player, "pitch", 0f);
            view.transform.localPosition = Vector3.up * 1.6f; view.transform.localRotation = Quaternion.identity;
            Physics.SyncTransforms();
            Call(player, "Update"); yield return Delay(.2);
            Check(surface.TryRaycast(new Ray(view.transform.position, view.transform.forward), 3.4f, out int aimed, out _) && aimed == part, "Bounded saved-city camera aims at one real exterior pane.");
            Button clean = FindButton("TouchClean"), take = FindButton("TouchTake"), bag = FindButton("TouchBag");
            Check(clean && take && bag && clean.interactable && take.interactable, "Production mobile CLEAN/TAKE/BAG buttons are available on the real pane.");
            Controls();
            Color32[] before = RenderPane("pane-before.png");
            for (int stroke = 1; stroke <= 4; stroke++)
            {
                Click(clean); Call(player, "Update"); yield return Delay(.2);
                Check((int)Invoke(sandbox, "SalvageCleanStrokes", surface.PartId(part)) == stroke, "Real CLEAN pointer click advances exactly one stroke.");
            }
            Color32[] after = RenderPane("pane-after.png"); double difference = CenterDifference(before, after);
            receipt.panePixelDifference = difference; Check(difference > .1, "Rendered pane pixels visibly change after four polishing strokes.");
            Check(!clean.interactable, "Fully polished pane disables further CLEAN action.");
            yield return Screenshot("actual-framebuffer-clean.png");
            Click(take); Call(player, "Update"); yield return Delay(.2);
            Check(surface.IsRemoved(part) && sandbox.Inventory.Any(entry => entry.id == surface.PartId(part)), "Real TAKE pointer click removes that pane into inventory.");
            // Extra in-memory layout rows exercise pagination, not additional world pickup claims.
            for (int index = 0; index < 10; index++)
                Check((bool)Invoke(sandbox, "InventoryAdd", "visual-layout/" + index, "Recovered fixture object " + (index + 1), "fixture", 1, 8, index % 5), "Supplemental layout row fits the real inventory cap.");
            Click(bag); Call(player, "Update"); yield return Delay(.2);
            Check(player.InventoryOpen, "Real BAG pointer click opens production inventory.");
            InventoryLayout(); yield return Screenshot("actual-framebuffer-inventory.png");
            Click(FindButton("CLOSE")); yield return Delay(.2); Check(!player.InventoryOpen, "Production CLOSE button resumes gameplay input.");
            input.SetGameplayEnabled(true); Call(player, "Update"); yield return Delay(.2);
            Controls(); yield return Screenshot("actual-framebuffer-controls.png");
            SameFrameFloorIdentity(buildings);
            Check(receipt.runtimeErrors.Count == 0, "No runtime error or assertion occurred.");
        }
        private static void SameFrameFloorIdentity(SlopCityBuildingWorld buildings)
        {
            SlopCityBuildingInterior building = buildings.Buildings.First(item => item && item.IsTower && item.FloorCount > 2);
            Transform original = building.EnsureFloor(1);
            sandbox.RefreshWorldGeometry();
            SlopCitySalvageItem oldItem = original.GetComponentsInChildren<SlopCitySalvageItem>(true).First(item => item.Pickable);
            string id = oldItem.StableId;
            building.ReleaseFloor(1);
            Check(original && !original.gameObject.activeInHierarchy && original.name.StartsWith("Retired_Runtime_Floor_", StringComparison.Ordinal),
                "Released floor is renamed and inactive before deferred Play Mode destruction.");
            Transform replacement = building.EnsureFloor(1);
            Check(replacement && replacement != original, "Same-frame floor admission creates a distinct live generation.");
            sandbox.RefreshWorldGeometry();
            SlopCitySalvageItem newItem = replacement.GetComponentsInChildren<SlopCitySalvageItem>(true).FirstOrDefault(item => item.StableId == id);
            Check(newItem, "Same-frame floor replacement preserves the original furnishing salvage identity.");
            Invoke(sandbox, "AdmitSalvage", oldItem);
            var bindings = Get<Dictionary<string, SlopCitySalvageItem>>(sandbox, "salvageObjects");
            Check(bindings.TryGetValue(id, out SlopCitySalvageItem bound) && bound == newItem,
                "Late retired-generation admission cannot replace the live furnishing binding.");
            receipt.sameFrameFloorIdentity = true;
        }
        private static void Controls()
        {
            var controls = Object.FindFirstObjectByType<SlopCityMobileControls>();
            Check(controls && controls.gameObject.activeInHierarchy, "Actual production mobile controls are visible.");
            var buttons = controls.GetComponentsInChildren<Button>().Where(button => button.gameObject.activeInHierarchy).ToArray();
            string[] expected = { "TouchJump", "TouchInteract", "TouchRun", "TouchMedia", "TouchPause", "TouchNewJob", "TouchHome", "TouchClean", "TouchTake", "TouchBag", "ReplaySignal" };
            string[] actual = buttons.Select(button => button.name).OrderBy(name => name).ToArray();
            Check(actual.SequenceEqual(expected.OrderBy(name => name)), "Production mobile actions plus HUD SIGNAL are present exactly once; actual=" + string.Join(",", actual));
            NoOverlap(buttons); foreach (var button in buttons) InScreen(button.GetComponent<RectTransform>());
        }
        private static void InventoryLayout()
        {
            var hud = Object.FindFirstObjectByType<SlopCitySalvageHud>();
            GameObject panel = Get<GameObject>(hud, "panel"); Check(panel.activeInHierarchy, "Actual inventory card is visible.");
            InScreen(panel.GetComponent<RectTransform>());
            var buttons = panel.GetComponentsInChildren<Button>().Where(button => button.gameObject.activeInHierarchy).ToArray();
            NoOverlap(buttons); foreach (var button in buttons) InScreen(button.GetComponent<RectTransform>());
        }
        private static void NoOverlap(Button[] buttons)
        {
            for (int a = 0; a < buttons.Length; a++)
                for (int b = a + 1; b < buttons.Length; b++)
                {
                    Rect x = ScreenRect(buttons[a].GetComponent<RectTransform>()), y = ScreenRect(buttons[b].GetComponent<RectTransform>());
                    Check(!x.Overlaps(y), "Action buttons do not overlap: " + buttons[a].name + "/" + buttons[b].name);
                }
        }
        private static void InScreen(RectTransform rect)
        {
            Rect box = ScreenRect(rect); Check(box.xMin >= -.5f && box.yMin >= -.5f && box.xMax <= Screen.width + .5f && box.yMax <= Screen.height + .5f, "UI target stays inside actual screen: " + rect.name);
        }
        private static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Camera camera = rect.GetComponentInParent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay ? null : rect.GetComponentInParent<Canvas>().worldCamera;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }
        private static Button FindButton(string name) => Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(button => button.name == name && button.gameObject.activeInHierarchy);
        private static void Click(Button button)
        {
            Check(button && button.interactable && button.gameObject.activeInHierarchy, "Production UI target is clickable: " + (button ? button.name : "missing"));
            Check(EventSystem.current, "Production EventSystem is available for pointer routing.");
            var pointer = new PointerEventData(EventSystem.current) { position = ScreenRect(button.GetComponent<RectTransform>()).center, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, "GraphicRaycaster resolves the intended topmost control.");
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        private static Color32[] RenderPane(string name)
        {
            RenderTexture previous = view.targetTexture, active = RenderTexture.active;
            var target = RenderTexture.GetTemporary(640, 360, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(640, 360, TextureFormat.RGB24, false);
            try
            {
                view.targetTexture = target; view.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); pixels.Apply();
                string path = ImagePath(name); File.WriteAllBytes(path, pixels.EncodeToPNG()); receipt.screenshots.Add(path);
                return pixels.GetPixels32();
            }
            finally { view.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(pixels); }
        }
        private static double CenterDifference(Color32[] a, Color32[] b)
        {
            double total = 0;
            for (int y = 174; y < 186; y++) for (int x = 314; x < 326; x++)
            { int i = y * 640 + x; total += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b); }
            return total / (12 * 12 * 3);
        }
        private static IEnumerator Screenshot(string name)
        {
            // Batch Mode does not run the end-of-frame screenshot callback. Render
            // the actual UI explicitly at the exact measured framebuffer size.
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(canvas => canvas.isActiveAndEnabled && canvas.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            Camera[] cameras = canvases.Select(canvas => canvas.worldCamera).ToArray();
            float[] distances = canvases.Select(canvas => canvas.planeDistance).ToArray();
            RenderTexture previous = view.targetTexture, active = RenderTexture.active;
            int mask = view.cullingMask;
            var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            try
            {
                view.targetTexture = target; view.cullingMask |= 1 << 5;
                foreach (Canvas canvas in canvases)
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = view; canvas.planeDistance = view.nearClipPlane + .1f; }
                Canvas.ForceUpdateCanvases(); view.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                string path = ImagePath(name); File.WriteAllBytes(path, pixels.EncodeToPNG()); receipt.screenshots.Add(path);
            }
            finally
            {
                view.targetTexture = previous; view.cullingMask = mask; RenderTexture.active = active;
                for (int index = 0; index < canvases.Length; index++)
                { canvases[index].renderMode = RenderMode.ScreenSpaceOverlay; canvases[index].worldCamera = cameras[index]; canvases[index].planeDistance = distances[index]; }
                Canvas.ForceUpdateCanvases(); RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(pixels);
            }
            yield return null;
        }
        private static string ImagePath(string name)
        { string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports/SalvageVisual"); Directory.CreateDirectory(folder); return Path.Combine(folder, name); }
        private static IEnumerator Delay(double seconds) { double end = EditorApplication.timeSinceStartup + seconds; while (EditorApplication.timeSinceStartup < end) yield return null; }
        private static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, Hidden).GetValue(owner);
        private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Hidden).SetValue(owner, value);
        private static void Call(object owner, string method) => owner.GetType().GetMethod(method, Hidden).Invoke(owner, null);
        private static object Invoke(object owner, string method, params object[] arguments) => owner.GetType().GetMethod(method, Hidden).Invoke(owner, arguments);
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); receipt.checks++; }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Active, false); Work.Clear();
            if (receipt == null) receipt = NewReceipt(); if (error != null) receipt.errors.Add(error.ToString());
            foreach (var writer in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(writer);
            receipt.savesRestored = true;
            for (int index = 0; index < Keys.Length; index++)
            {
                bool had = SessionState.GetBool(Backup + ".Had" + index, false);
                if (had) PlayerPrefs.SetString(Keys[index], SessionState.GetString(Backup + index, "")); else PlayerPrefs.DeleteKey(Keys[index]);
                receipt.savesRestored &= PlayerPrefs.HasKey(Keys[index]) == had && (!had || PlayerPrefs.GetString(Keys[index]) == SessionState.GetString(Backup + index, ""));
            }
            PlayerPrefs.Save(); Time.timeScale = 1;
            receipt.passed = error == null && receipt.runtimeErrors.Count == 0 && receipt.savesRestored;
            receipt.finishedAtUtc = DateTime.UtcNow.ToString("O");
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports/salvage-visual-check.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(receipt, true));
            Debug.Log("SLOP_CITY_SALVAGE_VISUAL_" + (receipt.passed ? "PASSED" : "FAILED") + " " + receipt.checks + " checks");
            EditorApplication.Exit(receipt.passed ? 0 : 1);
        }
        [Serializable] private sealed class Receipt
        {
            public bool passed, savesRestored, sameFrameFloorIdentity; public int checks; public double panePixelDifference;
            public string startedAtUtc, finishedAtUtc, unityVersion;
            public string screenshotMethod = "Explicit camera RenderTexture at recorded actual Screen dimensions; active production overlay Canvases temporarily use that camera, then all camera/Canvas state is restored. Batch end-of-frame ScreenCapture is unavailable.";
            public string scope = "Actual saved-city Editor/native Play Mode with sync/media/player autostart disabled. Production mobile UI GraphicRaycaster and simulated pointer clicks; normal player Update is invoked explicitly, with bounded camera positioning and no walking simulation. Real four-stroke pane polish and pickup plus inventory layout and screenshots at the recorded actual Editor framebuffer dimensions; ten supplemental in-memory inventory rows test layout only. Batch Editor sizing APIs did not change Screen dimensions; no requested phone dimensions or landscape acceptance are claimed. Browser portrait/landscape acceptance remains separate. No browser/device touch, media decoding, physical traversal or whole-city memory acceptance.";
            public List<string> errors = new List<string>(), runtimeErrors = new List<string>(), editorDiagnostics = new List<string>(), screenshots = new List<string>(), resolutions = new List<string>();
        }
    }
}
#endif
