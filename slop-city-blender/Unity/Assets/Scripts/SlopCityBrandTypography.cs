using UnityEngine;

namespace SlopCity
{
    /// <summary>Bundled branding glyphs; never relies on browser or OS font fallback.</summary>
    public static class SlopCityBrandTypography
    {
        public const string ResourcePath = "Fonts/SlopCityBrandSans-Bold";
        private static Font font;

        public static Font Font
        {
            get
            {
                if (!font) font = Resources.Load<Font>(ResourcePath);
                if (!font || !font.HasCharacter('™'))
                    throw new System.InvalidOperationException("The bundled SLOP CITY™ branding font is missing its trademark glyph.");
                return font;
            }
        }
    }
}
