using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Tier D: logical residency interest for large-world paging (no GPU tiles yet).
    /// Tracks a bounded ring of world bounds so a future atlas can prioritize physical pages.
    /// </summary>
    public static class SlopCityVirtualTexturePaging
    {
        private const int MaxInterests = 8;
        private static readonly Bounds[] interests = new Bounds[MaxInterests];
        private static int count;
        private static int writeIndex;

#if UNITY_WEBGL && !UNITY_EDITOR
        public static bool Enabled => true;
#else
        public static bool Enabled => false;
#endif

        public static int InterestCount => count;

        public static void RegisterInterest(in Bounds worldBounds)
        {
            if (!Enabled) return;
            if (worldBounds.size.sqrMagnitude <= 0f) return;
            interests[writeIndex] = worldBounds;
            writeIndex = (writeIndex + 1) % MaxInterests;
            if (count < MaxInterests) count++;
        }

        public static void ClearInterest()
        {
            count = 0;
            writeIndex = 0;
        }

        public static bool TryGetInterest(int index, out Bounds bounds)
        {
            if (index < 0 || index >= count) { bounds = default; return false; }
            int slot = count < MaxInterests ? index : (writeIndex + index) % MaxInterests;
            bounds = interests[slot];
            return true;
        }
    }
}
