using System;
using System.Reflection;
using SlopCity;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Checks bundled glyph geometry and the actual HUD label font assignment.</summary>
public static class SlopCityBrandTypographyCheck
{
    [MenuItem("SLOP CITY™/Validate Branding Glyphs")]
    public static void Run()
    {
        Font font = SlopCityBrandTypography.Font;
        foreach (char value in "SLOP CITY™SLOPCITY.org© 2026 Machine Publishers, LLC")
            Check(font.HasCharacter(value), "Bundled branding character U+" + ((int)value).ToString("X4"));
        foreach (int size in new[] { 16, 28, 32, 45 })
        {
            font.RequestCharactersInTexture("™", size, FontStyle.Bold);
            CharacterInfo glyph;
            Check(font.GetCharacterInfo('™', out glyph, size, FontStyle.Bold)
                && glyph.advance > 0 && glyph.maxX > glyph.minX && glyph.maxY > glyph.minY,
                "Trademark has visible glyph geometry at " + size + "px");
        }
        var root = new GameObject("TemporaryBrandTypographyCheck", typeof(RectTransform), typeof(Canvas), typeof(SlopCityHud));
        try
        {
            var hud = root.GetComponent<SlopCityHud>();
            const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            Font legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            typeof(SlopCityHud).GetField("font", privateInstance).SetValue(hud, legacy);
            MethodInfo label = typeof(SlopCityHud).GetMethod("Label", privateInstance);
            Func<string, Text> create = value => (Text)label.Invoke(hud, new object[] {
                root.transform, "Brand", value, 32, TextAnchor.UpperLeft,
                Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(600, 80), Color.white });
            Text brand = create("SLOP CITY™");
            Check(brand.font == font && brand.text == "SLOP CITY™", "Menu branding uses the exact trademark string and bundled font");
            Check(create("Ordinary HUD text").font == legacy, "Ordinary HUD typography remains unchanged");
            Canvas.ForceUpdateCanvases();
            Check(brand.cachedTextGenerator.characterCountVisible >= "SLOP CITY™".Length,
                "uGUI generates the complete branded label");
            Type snapshotType = typeof(SlopCityHud).GetNestedType("AuthoredUiState", BindingFlags.NonPublic);
            object snapshot = Activator.CreateInstance(snapshotType, new object[] { brand.rectTransform });
            brand.font = legacy; brand.text = "Changed label";
            snapshotType.GetMethod("Restore").Invoke(snapshot, null);
            Check(brand.font == font && brand.text == "SLOP CITY™",
                "Authored UI rollback restores the exact font and label");
            brand.text = "SLOP CITY"; brand.font = legacy;
            typeof(SlopCityHud).GetMethod("StyleGameplayLayer", privateInstance).Invoke(hud, null);
            Check(brand.text == "SLOP CITY™" && brand.font == font,
                "Authored gameplay branding gains the trademark and bundled font");
            var reader = root.AddComponent<SlopCitySourceBrowser>();
            reader.Initialize(root.transform, legacy);
            reader.Show();
            Text path = root.transform.Find("SourceAndLicense/SafeArea/Path").GetComponent<Text>();
            Check(path.text.Contains("SLOP CITY™") && path.font == font,
                "Offline legal heading renders its trademark with the bundled font");
            foreach (Text text in root.GetComponentsInChildren<Text>(true))
                if (text.text.Contains("™")) Check(text.font == font, "Every visible trademark label uses the bundled font");
            reader.ShowLocalReport("Local inspection", "Local observations");
            Check(path.font == legacy, "Ordinary diagnostic headings restore their existing font");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Debug.Log("SlopCityBrandTypographyCheck PASS: bundled branding coverage, trademark geometry and HUD assignment. Visual player verification remains separate.");
    }

    private static void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException("Brand typography check failed: " + description);
    }
}
