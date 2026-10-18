using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SlopCity;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Isolated reader checks: no scene, player, saves, network or build operations.</summary>
public static class SlopCitySourceBrowserCheck
{
    private static int checks;
    private static readonly Type Reader = typeof(SlopCitySourceBrowser);
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [MenuItem("Slop City/Validate Source Reader")]
    public static void Run()
    {
        checks = 0;
        Check(SlopCitySourceBrowser.RepositoryUrl == "https://github.com/hollingsworthd/SLOP_CITY", "Public game source repository");
        Check((string)Reader.GetField("ApiRoot", Private).GetRawConstantValue()
            == "https://api.github.com/repos/hollingsworthd/SLOP_CITY/contents/", "Source browsing stays on the public repository");
        Check((bool)Call("IsSafePath", ""), "Root path");
        Check((bool)Call("IsSafePath", "slop-city-blender/Unity/Assets/Scripts/test.cs"), "Nested path");
        foreach (string path in new[] { "../secret", "folder/../secret", "/leading", "trailing/", "a//b", "a\\b", "a\nb", "." })
            Check(!(bool)Call("IsSafePath", path), "Reject unsafe path");
        Check((string)Call("EncodePath", "a b/x?y#z%") == "a%20b/x%3Fy%23z%25", "Escape URL syntax per segment");
        Array entries = (Array)Call("ParseDirectory", "[{\"name\":\"ok.cs\",\"path\":\"src/ok.cs\",\"type\":\"file\"},"
            + "{\"name\":\"link\",\"path\":\"src/link\",\"type\":\"symlink\"},"
            + "{\"name\":\"foreign\",\"path\":\"elsewhere/foreign\",\"type\":\"file\"},"
            + "{\"name\":\"sub\",\"path\":\"src/sub\",\"type\":\"file\",\"submodule_git_url\":\"https://other.invalid\"}]", "src");
        Check(entries.Length == 1, "Only direct normal repository children");
        Check(Throws(() => Call("ParseDirectory", "{\"message\":\"error\"}", "")), "Reject non-directory response");
        var oversizedList = new string[1001];
        for (int i = 0; i < oversizedList.Length; i++) oversizedList[i] = "{\"name\":\"x\",\"path\":\"x\",\"type\":\"file\"}";
        Check(Throws(() => Call("ParseDirectory", "[" + string.Join(",", oversizedList) + "]", "")), "Bound directory count");
        Check(ReadFile(Encoding.UTF8.GetBytes("<b>literal</b>\nλ"), "f", "f", "base64", 17), "Read plain UTF-8");
        Check(!ReadFile(new byte[] { 0xff }, "f", "f", "base64", 1), "Reject invalid UTF-8");
        Check(!ReadFile(new byte[] { 0 }, "f", "f", "base64", 1), "Reject binary NUL");
        Check(!ReadFile(new byte[] { 65 }, "f", "other", "base64", 1), "Reject mismatched file path");
        Check(!ReadFile(new byte[] { 65 }, "f", "f", "none", 1), "Reject unsupported encoding");
        Check(!ReadFile(new byte[] { 65 }, "f", "f", "base64", 262145), "Bound declared file bytes");
        Check(!ReadFile(new byte[] { 65 }, "f", "f", "base64", 2), "Verify decoded byte count");
        Check(ReadFile(new byte[0], "f", "f", "base64", 0), "Accept empty file");
        Type bounded = Reader.GetNestedType("BoundedDownload", BindingFlags.NonPublic);
        object handler = Activator.CreateInstance(bounded, new object[] { 8 });
        try
        {
            MethodInfo receive = bounded.GetMethod("ReceiveData", Private);
            Check((bool)receive.Invoke(handler, new object[] { new byte[8], 8 }), "Accept exact response cap");
            Check(!(bool)receive.Invoke(handler, new object[] { new byte[1], 1 }), "Reject excess bytes before copy");
            Check((bool)bounded.GetProperty("Overflow").GetValue(handler), "Report overflow");
        }
        finally { ((IDisposable)handler).Dispose(); }
        handler = Activator.CreateInstance(bounded, new object[] { 8 });
        try
        {
            bounded.GetMethod("ReceiveContentLengthHeader", Private).Invoke(handler, new object[] { (ulong)9 });
            Check((bool)bounded.GetProperty("Overflow").GetValue(handler), "Reject oversized content length");
        }
        finally { ((IDisposable)handler).Dispose(); }
        var canvasObject = new GameObject("TemporarySourceReaderCheck", typeof(RectTransform), typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var menu = new GameObject("TemporaryMenu", typeof(RectTransform)); menu.transform.SetParent(canvasObject.transform, false);
        RectTransform menuRect = menu.GetComponent<RectTransform>(); menuRect.anchorMin = Vector2.zero; menuRect.anchorMax = Vector2.one;
        menuRect.offsetMin = menuRect.offsetMax = Vector2.zero;
        var reader = canvasObject.AddComponent<SlopCitySourceBrowser>();
        try
        {
            reader.Initialize(menu.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            reader.Show();
            Check(Field(reader, "activeRequest") == null, "Opening offline license does not request network");
            Text heading = (Text)Field(reader, "pathLabel");
            Check(heading.text.Contains("SLOP CITY™") && heading.font == SlopCityBrandTypography.Font,
                "Offline legal heading uses the bundled trademark glyph");
            TextAsset license = Resources.Load<TextAsset>("Licensing/AGPL-3.0");
            TextAsset notice = Resources.Load<TextAsset>("Licensing/NOTICE");
            Check(license && license.text.Contains("GNU AFFERO GENERAL PUBLIC LICENSE"), "Bundled full AGPL");
            Check(notice && notice.text.Contains("Unity"), "Bundled exception notice");
            IList licensePages = (IList)Field(reader, "pages");
            Check(licensePages.Count > 1, "Full license is paginated");
            var joined = new StringBuilder();
            foreach (string part in licensePages) { Check(part.Length <= 6000, "Page is below mesh limit"); joined.Append(part); }
            Check(joined.ToString() == notice.text + "\n\n" + license.text, "License pagination retains all text");
            string longText = new string('x', 5999) + "😀" + new string('y', 20000);
            Reader.GetMethod("ShowText", Private).Invoke(reader, new object[] { longText });
            joined.Clear();
            foreach (string part in (IList)Field(reader, "pages"))
            {
                Check(part.Length <= 6000 && !char.IsHighSurrogate(part[part.Length - 1]), "Page avoids splitting surrogate pair"); joined.Append(part);
            }
            Check(joined.ToString() == longText, "Source pagination retains all text");
            foreach (Text text in menu.GetComponentsInChildren<Text>(true)) Check(!text.supportRichText, "Untrusted text is plain");
            reader.ShowLocalReport("Local inspection", "Read-only local observations");
            Check(heading.font == Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
                "Ordinary local report headings restore the original UI font");
            Check(Field(reader, "activeRequest") == null, "Local reports do not request network");
            int before = (int)Field(reader, "generation"); reader.Hide();
            Check((int)Field(reader, "generation") > before, "Hide invalidates stale attempts");
            Check(!((GameObject)Field(reader, "overlay")).activeSelf, "Hide returns to menu");
        }
        finally { UnityEngine.Object.DestroyImmediate(canvasObject); }
        Debug.Log("SlopCitySourceBrowserCheck PASS: " + checks + " focused checks. No network, scene, player or save access.");
    }

    private static object Field(object instance, string name) { return Reader.GetField(name, Private).GetValue(instance); }
    private static object Call(string name, params object[] args) { return Reader.GetMethod(name, Private).Invoke(null, args); }
    private static bool ReadFile(byte[] bytes, string path, string requested, string encoding, int size)
    {
        Type entry = Reader.GetNestedType("Entry", BindingFlags.NonPublic);
        object value = Activator.CreateInstance(entry, true);
        entry.GetField("path").SetValue(value, path); entry.GetField("type").SetValue(value, "file");
        entry.GetField("encoding").SetValue(value, encoding); entry.GetField("size").SetValue(value, (long)size);
        entry.GetField("content").SetValue(value, Convert.ToBase64String(bytes));
        object[] args = { value, requested, null };
        return (bool)Reader.GetMethod("TryReadText", Private).Invoke(null, args);
    }
    private static bool Throws(Action action) { try { action(); return false; } catch (TargetInvocationException) { return true; } }
    private static void Check(bool ok, string description) { checks++; if (!ok) throw new InvalidOperationException("Source reader check failed: " + description); }
}
