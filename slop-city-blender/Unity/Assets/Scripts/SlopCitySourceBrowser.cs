using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace SlopCity
{
    /// <summary>Offline license reader and an on-demand, unauthenticated public repository reader.</summary>
    public sealed class SlopCitySourceBrowser : MonoBehaviour
    {
        public const string RepositoryUrl = "https://github.com/hollingsworthd/SLOP_CITY";
        private const string ApiRoot = "https://api.github.com/repos/hollingsworthd/SLOP_CITY/contents/";
        private const int ResponseLimit = 2 * 1024 * 1024;
        private const int FileLimit = 256 * 1024;
        private const int PageLimit = 6000; // Stay comfortably below uGUI Text's mesh vertex limit.
        private const int DirectoryLimit = 1000;
        private static readonly Color Paper = new Color(.84f, .89f, .9f);
        private GameObject overlay;
        private RectTransform safeFrame, content;
        private Text pathLabel, statusLabel, pageLabel, title;
        private ScrollRect scroll;
        private Button up, retry, previous, next, license, browse, github, back;
        private Font font;
        private bool desktopTypography;
        private readonly List<string> pages = new List<string>();
        private string currentPath = "";
        private bool fileView, sourceMode;
        private int page, generation;
        private Coroutine pending;
        private UnityWebRequest activeRequest;

        [Serializable] private sealed class Entry
        {
            public string name, path, type, encoding, content, submodule_git_url;
            public long size;
        }
        [Serializable] private sealed class Listing { public Entry[] entries; }

        public void Initialize(Transform menu, Font uiFont, bool useTouchControls = false)
        {
            if (overlay) return;
            bool complete = false;
            try
            {
                font = uiFont;
                desktopTypography = !useTouchControls;
                overlay = Panel(menu, "SourceAndLicense", new Color(.026f, .041f, .055f, 1f)).gameObject;
                Stretch(overlay.GetComponent<RectTransform>());
                safeFrame = Panel(overlay.transform, "SafeArea", Color.clear);
                safeFrame.GetComponent<Image>().raycastTarget = false;
                title = Label(safeFrame, "Title", "SOURCE + LICENSE", 18);
                Place(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -44), new Vector2(-104, 0));
                back = MakeButton(safeFrame, "Back", "MENU", Hide);
                Place(back.GetComponent<RectTransform>(), Vector2.one, Vector2.one, new Vector2(-96, -44), Vector2.zero);
                license = MakeButton(safeFrame, "License", "LICENSE", ShowLicense);
                Place(license.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(.33f, 1), new Vector2(0, -96), new Vector2(-4, -52));
                browse = MakeButton(safeFrame, "Browse", "BROWSE SOURCE", () => Navigate("", false));
                Place(browse.GetComponent<RectTransform>(), new Vector2(.33f, 1), new Vector2(.68f, 1), new Vector2(0, -96), new Vector2(-4, -52));
                github = MakeButton(safeFrame, "GitHub", "OPEN GITHUB", OpenGitHub);
                Place(github.GetComponent<RectTransform>(), new Vector2(.68f, 1), Vector2.one, new Vector2(0, -96), new Vector2(0, -52));
                up = MakeButton(safeFrame, "Up", "UP", GoUp);
                Place(up.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -148), new Vector2(64, -104));
                retry = MakeButton(safeFrame, "Retry", "RETRY", () => Navigate(currentPath, fileView));
                Place(retry.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1), new Vector2(72, -148), new Vector2(148, -104));
                pathLabel = Label(safeFrame, "Path", "License (offline)", 12);
                pathLabel.verticalOverflow = VerticalWrapMode.Truncate;
                Place(pathLabel.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(160, -148), new Vector2(0, -104));
                statusLabel = Label(safeFrame, "Status", "", 12);
                Place(statusLabel.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(0, -208), new Vector2(0, -154));

                RectTransform viewport = Panel(safeFrame, "Reader", new Color(0, 0, 0, .22f));
                Place(viewport, Vector2.zero, Vector2.one, new Vector2(0, 56), new Vector2(0, -214));
                viewport.gameObject.AddComponent<RectMask2D>();
                scroll = viewport.gameObject.AddComponent<ScrollRect>();
                scroll.viewport = viewport; scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
                content.SetParent(viewport, false);
                content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
                content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
                var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(10, 10, 10, 10); layout.spacing = 6;
                layout.childControlWidth = true; layout.childControlHeight = true;
                layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
                content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                scroll.content = content;
                previous = MakeButton(safeFrame, "Previous", "PREVIOUS", () => PresentPage(page - 1));
                Place(previous.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(96, 44));
                next = MakeButton(safeFrame, "Next", "NEXT", () => PresentPage(page + 1));
                Place(next.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-96, 0), new Vector2(0, 44));
                pageLabel = Label(safeFrame, "Page", "", 12); pageLabel.alignment = TextAnchor.MiddleCenter;
                Place(pageLabel.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(100, 0), new Vector2(-100, 44));
                overlay.SetActive(false);
                complete = true;
            }
            finally
            {
                if (!complete && overlay)
                {
                    overlay.SetActive(false);
                    Destroy(overlay);
                    overlay = null;
                }
            }
        }

        public void Show()
        {
            if (!overlay) return;
            overlay.SetActive(true); overlay.transform.SetAsLastSibling();
            ApplySafeArea(); ShowLicense();
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(license.gameObject);
        }

        public void ShowLocalReport(string heading, string report)
        {
            if (!overlay) return;
            CancelPending();
            sourceMode = false;
            currentPath = "";
            fileView = false;
            overlay.SetActive(true); overlay.transform.SetAsLastSibling();
            ApplySafeArea();
            title.text = "THE MUNICIPAL MATRIX";
            pathLabel.text = string.IsNullOrEmpty(heading) ? "Local inspection" : heading;
            pathLabel.font = pathLabel.text.Contains("™") ? SlopCityBrandTypography.Font : font;
            statusLabel.text = "Local simulation evidence captured when opened. LICENSE is offline; BROWSE SOURCE explicitly connects to public GitHub.";
            up.interactable = retry.interactable = false;
            // A report is a bounded snapshot, not a continuously rebuilt UI or a
            // network request. Keep the same plain-text pagination as the license.
            const int maximumReportLength = 32000;
            string bounded = report ?? "No local observations have been recorded.";
            if (bounded.Length > maximumReportLength)
                bounded = bounded.Substring(0, maximumReportLength) + "\n[Report truncated]";
            ShowText(bounded);
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(back.gameObject);
        }

        public void Hide()
        {
            CancelPending();
            if (overlay)
            {
                overlay.SetActive(false);
                if (EventSystem.current)
                    foreach (Button button in overlay.transform.parent.GetComponentsInChildren<Button>())
                        if (button.name == "LicenseAndSource") { EventSystem.current.SetSelectedGameObject(button.gameObject); break; }
            }
        }

        private void Update() { if (overlay && overlay.activeInHierarchy) ApplySafeArea(); }
        private void OnDisable() { CancelPending(); }
        private void OnDestroy()
        {
            CancelPending();
            if (overlay) { if (Application.isPlaying) Destroy(overlay); else DestroyImmediate(overlay); }
        }

        private void ApplySafeArea()
        {
            Rect area = Screen.safeArea;
            float width = Mathf.Max(1, Screen.width), height = Mathf.Max(1, Screen.height);
            safeFrame.anchorMin = new Vector2(area.xMin / width, area.yMin / height);
            safeFrame.anchorMax = new Vector2(area.xMax / width, area.yMax / height);
            safeFrame.offsetMin = new Vector2(12, 12); safeFrame.offsetMax = new Vector2(-12, -12);
            bool compact = safeFrame.rect.height < 420 && safeFrame.rect.width > 500;
            if (compact)
            {
                Place(title.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(0, -44), new Vector2(-104, 0));
                Place(pathLabel.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(240, -44), new Vector2(-104, 0));
                title.rectTransform.offsetMax = new Vector2(235 - safeFrame.rect.width, 0);
                Place(license.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(.20f, 1), new Vector2(0, -96), new Vector2(-4, -52));
                Place(browse.GetComponent<RectTransform>(), new Vector2(.20f, 1), new Vector2(.44f, 1), new Vector2(0, -96), new Vector2(-4, -52));
                Place(github.GetComponent<RectTransform>(), new Vector2(.44f, 1), new Vector2(.68f, 1), new Vector2(0, -96), new Vector2(-4, -52));
                Place(up.GetComponent<RectTransform>(), new Vector2(.68f, 1), new Vector2(.80f, 1), new Vector2(0, -96), new Vector2(-4, -52));
                Place(retry.GetComponent<RectTransform>(), new Vector2(.80f, 1), Vector2.one, new Vector2(0, -96), new Vector2(0, -52));
                Place(statusLabel.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(0, -138), new Vector2(0, -102));
                Place(scroll.viewport, Vector2.zero, Vector2.one, new Vector2(0, 50), new Vector2(0, -144));
            }
            else
            {
                Place(title.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(0, -44), new Vector2(-104, 0));
                Place(license.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(.33f, 1), new Vector2(0, -96), new Vector2(-4, -52));
                Place(browse.GetComponent<RectTransform>(), new Vector2(.33f, 1), new Vector2(.68f, 1), new Vector2(0, -96), new Vector2(-4, -52));
                Place(github.GetComponent<RectTransform>(), new Vector2(.68f, 1), Vector2.one, new Vector2(0, -96), new Vector2(0, -52));
                Place(up.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -148), new Vector2(64, -104));
                Place(retry.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1), new Vector2(72, -148), new Vector2(148, -104));
                Place(pathLabel.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(160, -148), new Vector2(0, -104));
                Place(statusLabel.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(0, -208), new Vector2(0, -154));
                Place(scroll.viewport, Vector2.zero, Vector2.one, new Vector2(0, 56), new Vector2(0, -214));
            }
        }

        private void ShowLicense()
        {
            CancelPending(); sourceMode = false;
            title.text = "SOURCE + LICENSE";
            up.interactable = retry.interactable = false;
            pathLabel.text = "SLOP CITY™ license (offline)";
            pathLabel.font = SlopCityBrandTypography.Font;
            TextAsset notice = Resources.Load<TextAsset>("Licensing/NOTICE");
            TextAsset license = Resources.Load<TextAsset>("Licensing/AGPL-3.0");
            statusLabel.text = "Original game code: AGPL-3.0-or-later with the Unity linking exception. Third-party works keep their separate terms.";
            ShowText((notice ? notice.text : "License notice is unavailable in this build.") + "\n\n"
                + (license ? license.text : "The full license text is unavailable in this build. Open GitHub for the license."));
        }

        private void Navigate(string path, bool isFile)
        {
            if (!overlay || !overlay.activeInHierarchy || !IsSafePath(path)) return;
            CancelPending(); sourceMode = true; currentPath = path; fileView = isFile;
            title.text = "SOURCE + LICENSE";
            up.interactable = path.Length > 0; retry.interactable = true;
            pathLabel.text = "main / " + path;
            pathLabel.font = font;
            statusLabel.text = "Loading public source…";
            ClearContent(); ClearPages();
            pending = StartCoroutine(Fetch(path, isFile, generation));
        }

        private void GoUp()
        {
            int slash = currentPath.LastIndexOf('/');
            Navigate(slash < 0 ? "" : currentPath.Substring(0, slash), false);
        }

        private void OpenGitHub()
        {
            string url = RepositoryUrl;
            if (sourceMode && currentPath.Length > 0)
                url += (fileView ? "/blob/main/" : "/tree/main/") + EncodePath(currentPath);
            Application.OpenURL(url);
        }

        private void CancelPending()
        {
            generation++;
            UnityWebRequest request = activeRequest;
            Coroutine routine = pending;
            activeRequest = null;
            pending = null;
            try { if (request != null) request.Abort(); }
            catch (Exception error) { Debug.LogWarning("Source download cancellation failed: " + error.Message, this); }
            try { if (routine != null) StopCoroutine(routine); }
            catch (Exception error) { Debug.LogWarning("Source reader cancellation failed: " + error.Message, this); }
            finally { DisposeRequest(request); }
        }

        private static void DisposeRequest(UnityWebRequest request, DownloadHandler handler = null)
        {
            try
            {
                if (request != null) request.Dispose();
                else if (handler != null) handler.Dispose();
            }
            catch (Exception error) { Debug.LogWarning("Source download cleanup failed: " + error.Message); }
        }

        private IEnumerator Fetch(string path, bool isFile, int ticket)
        {
            BoundedDownload handler = null;
            UnityWebRequest request = null;
            try
            {
                UnityWebRequestAsyncOperation operation = null;
                string openingError = null;
                try
                {
                    handler = new BoundedDownload(ResponseLimit);
                    request = new UnityWebRequest(ApiRoot + EncodePath(path) + "?ref=main", "GET", handler, null);
                    activeRequest = request;
                    request.timeout = 15; request.redirectLimit = 0;
                    request.SetRequestHeader("Accept", "application/vnd.github+json");
#if !UNITY_WEBGL || UNITY_EDITOR
                    request.SetRequestHeader("User-Agent", "SlopCity-PublicSourceReader");
#endif
                    operation = request.SendWebRequest();
                }
                catch (Exception) { openingError = "Could not start the public source request. Retry or open GitHub."; }
                if (operation != null)
                {
                    while (!operation.isDone && ticket == generation)
                    {
                        if (handler.Overflow)
                        {
                            try { request.Abort(); }
                            catch (Exception error) { Debug.LogWarning("Source download cancellation failed: " + error.Message, this); }
                            break;
                        }
                        yield return null;
                    }
                }
                if (ticket != generation) yield break;
                if (openingError != null) statusLabel.text = openingError;
                else if (handler.Overflow) statusLabel.text = "This response is too large for the reader. Open GitHub to view it.";
                else if (request.responseCode == 404) statusLabel.text = "Public source is unavailable yet, or this path no longer exists. Retry or open GitHub. No sign-in is used here.";
                else if (request.responseCode == 403 || request.responseCode == 429) statusLabel.text = "GitHub's public request limit was reached. Try again later or open GitHub.";
                else if (request.result != UnityWebRequest.Result.Success) statusLabel.text = "Could not load public source (network, browser policy or GitHub error). Retry or open GitHub.";
                else
                {
                    try { PresentResponse(handler.Text, path, isFile); }
                    catch (Exception) { statusLabel.text = "GitHub returned content this reader cannot display. Open GitHub to view it."; }
                }
            }
            finally
            {
                if (ReferenceEquals(activeRequest, request))
                {
                    activeRequest = null;
                    DisposeRequest(request, handler);
                }
                if (ticket == generation) pending = null;
            }
        }

        private void PresentResponse(string json, string path, bool isFile)
        {
            if (!isFile)
            {
                Entry[] entries = ParseDirectory(json, path);
                Array.Sort(entries, (a, b) =>
                {
                    int typeOrder = (a.type == "dir" ? 0 : 1).CompareTo(b.type == "dir" ? 0 : 1);
                    return typeOrder != 0 ? typeOrder : StringComparer.OrdinalIgnoreCase.Compare(a.name, b.name);
                });
                ClearContent();
                foreach (Entry entry in entries)
                {
                    Entry chosen = entry;
                    Button button = MakeButton(content, "Entry", (entry.type == "dir" ? "[DIR] " : "") + entry.name,
                        () => Navigate(chosen.path, chosen.type == "file"));
                    button.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                    button.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
                }
                statusLabel.text = entries.Length + " entries / main. Select a folder or file. Files are read-only; nothing is run.";
                ResetScroll();
            }
            else
            {
                Entry entry = JsonUtility.FromJson<Entry>(json);
                string text;
                if (!TryReadText(entry, path, out text))
                {
                    statusLabel.text = "Binary, oversized or unsupported file. Open GitHub to view or download it.";
                    return;
                }
                statusLabel.text = "Read-only source / main. This public file may differ from the installed game version.";
                ShowText(text.Length == 0 ? "(Empty file)" : text);
            }
        }

        private static Entry[] ParseDirectory(string json, string parent)
        {
            if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith("[", StringComparison.Ordinal)) throw new FormatException();
            Listing listing = JsonUtility.FromJson<Listing>("{\"entries\":" + json + "}");
            if (listing == null || listing.entries == null || listing.entries.Length > DirectoryLimit) throw new FormatException();
            var accepted = new List<Entry>();
            foreach (Entry entry in listing.entries)
            {
                if (entry == null || (entry.type != "dir" && entry.type != "file") || !string.IsNullOrEmpty(entry.submodule_git_url)) continue;
                if (!IsSafePath(entry.path) || string.IsNullOrEmpty(entry.name) || entry.name.IndexOf('/') >= 0) continue;
                string expected = parent.Length == 0 ? entry.name : parent + "/" + entry.name;
                if (!string.Equals(expected, entry.path, StringComparison.Ordinal)) continue;
                accepted.Add(entry);
            }
            return accepted.ToArray();
        }

        private static bool TryReadText(Entry entry, string path, out string text)
        {
            text = null;
            if (entry == null || entry.type != "file" || entry.path != path || !IsSafePath(entry.path)
                || !string.IsNullOrEmpty(entry.submodule_git_url) || entry.encoding != "base64"
                || entry.size < 0 || entry.size > FileLimit || entry.content == null || entry.content.Length > FileLimit * 2) return false;
            try
            {
                byte[] bytes = Convert.FromBase64String(entry.content);
                if (bytes.Length > FileLimit || bytes.LongLength != entry.size) return false;
                string decoded = new UTF8Encoding(false, true).GetString(bytes);
                foreach (char c in decoded) if (char.IsControl(c) && c != '\t' && c != '\n' && c != '\r') return false;
                text = decoded; return true;
            }
            catch (FormatException) { return false; }
            catch (DecoderFallbackException) { return false; }
        }

        private static bool IsSafePath(string path)
        {
            if (path == null || path.Length > 2048 || path.IndexOf('\\') >= 0) return false;
            if (path.Length == 0) return true;
            foreach (char c in path) if (char.IsControl(c)) return false;
            foreach (string segment in path.Split('/')) if (segment.Length == 0 || segment == "." || segment == "..") return false;
            return true;
        }

        private static string EncodePath(string path)
        {
            string[] parts = path.Split('/');
            for (int i = 0; i < parts.Length; i++) parts[i] = Uri.EscapeDataString(parts[i]);
            return string.Join("/", parts);
        }

        private void ShowText(string text)
        {
            pages.Clear();
            for (int start = 0; start < text.Length;)
            {
                int length = Mathf.Min(PageLimit, text.Length - start);
                if (start + length < text.Length)
                {
                    int newline = text.LastIndexOf('\n', start + length - 1, length);
                    if (newline >= start + length / 2) length = newline - start + 1;
                    if (char.IsHighSurrogate(text[start + length - 1])) length--;
                }
                pages.Add(text.Substring(start, length)); start += length;
            }
            if (pages.Count == 0) pages.Add("");
            PresentPage(0);
        }

        private void PresentPage(int selected)
        {
            if (pages.Count == 0) return;
            page = Mathf.Clamp(selected, 0, pages.Count - 1); ClearContent();
            Text body = Label(content, "PlainText", pages[page], 14);
            body.alignment = TextAnchor.UpperLeft; body.horizontalOverflow = HorizontalWrapMode.Wrap;
            previous.interactable = page > 0; next.interactable = page + 1 < pages.Count;
            pageLabel.text = "Page " + (page + 1) + " / " + pages.Count;
            ResetScroll();
        }

        private void ClearPages() { pages.Clear(); previous.interactable = next.interactable = false; pageLabel.text = ""; }
        private void ClearContent()
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject; child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
        }
        private void ResetScroll() { Canvas.ForceUpdateCanvases(); scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; }

        private Text Label(Transform parent, string name, string value, int size)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(parent, false);
            label.font = value.Contains("™") ? SlopCityBrandTypography.Font : font;
            label.fontSize = desktopTypography ? Mathf.Max(16, size) : size;
            label.color = Paper; label.text = value; label.supportRichText = false;
            label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false;
            return label;
        }

        private Button MakeButton(Transform parent, string name, string value, UnityAction action)
        {
            RectTransform rect = Panel(parent, name, new Color(.13f, .21f, .25f, 1));
            Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(action);
            Text text = Label(rect, "Label", value, 12); text.alignment = TextAnchor.MiddleCenter;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            Stretch(text.rectTransform); text.rectTransform.offsetMin = new Vector2(6, 2); text.rectTransform.offsetMax = new Vector2(-6, -2);
            return button;
        }
        private static RectTransform Panel(Transform parent, string name, Color color)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.GetComponent<Image>().color = color; return rect;
        }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 lower, Vector2 upper)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = lower; rect.offsetMax = upper; }

        private sealed class BoundedDownload : DownloadHandlerScript
        {
            private readonly byte[] body;
            private int count;
            public bool Overflow { get; private set; }
            public string Text { get { return new UTF8Encoding(false, true).GetString(body, 0, count); } }
            public BoundedDownload(int limit) : base(new byte[16384]) { body = new byte[limit]; }
            protected override void ReceiveContentLengthHeader(ulong length) { if (length > (ulong)body.Length) Overflow = true; }
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (Overflow || data == null || length < 0 || length > body.Length - count) { Overflow = true; return false; }
                Buffer.BlockCopy(data, 0, body, count, length); count += length; return true;
            }
        }
    }
}
