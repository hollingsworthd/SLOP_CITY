#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Browser Tier-D capability mask (WebCodecs, OffscreenCanvas, transferControlToOffscreen).
    /// Unity VideoPlayer remains authoritative; flags gate future backends only.
    /// </summary>
    internal static class SlopCityBrowserCapabilities
    {
        private const int FlagWebCodecs = 1;
        private const int FlagOffscreenCanvas = 2;
        private const int FlagVideoFrame = 4;
        private const int FlagOffscreenTransfer = 8;

        [DllImport("__Internal")]
        private static extern void SlopCityProbeBrowserMediaCaps();

        [DllImport("__Internal")]
        private static extern int SlopCityBrowserMediaCapsMask();

        private static bool probed;
        private static int mask;

        public static bool WebCodecs => (mask & FlagWebCodecs) != 0;
        public static bool OffscreenCanvas => (mask & FlagOffscreenCanvas) != 0;
        public static bool VideoFrame => (mask & FlagVideoFrame) != 0;
        public static bool OffscreenTransfer => (mask & FlagOffscreenTransfer) != 0;

        public static void ProbeOnce()
        {
            if (probed) return;
            probed = true;
            try
            {
                SlopCityProbeBrowserMediaCaps();
                mask = SlopCityBrowserMediaCapsMask();
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("Browser capability probe failed: " + error.GetType().Name);
            }
        }
    }
}
#endif
