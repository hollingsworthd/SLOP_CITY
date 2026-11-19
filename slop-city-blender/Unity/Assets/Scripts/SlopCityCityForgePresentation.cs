using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Shared, catalog-bounded visual facts for every City Forge view. Street
    /// compilation, draft review, and the aerial HUD consume this class rather
    /// than independently approximating saved plan geometry or finish IDs.
    /// </summary>
    public static class SlopCityCityForgePresentation
    {
        private static readonly string[] ServiceKits = { "care-station", "repair-counter", "laundry-service", "delivery-bay", "neighborhood-office", "market-stall", "food-counter", "exchange-desk", "library-desk", "utility-desk", "community-room", "screening-service", "studio-service", "venue-door", "archive-desk", "garden-steward", "water-service", "power-service", "shelter-desk", "service-yard-bay" };
        private static readonly string[] OutcomeKits = { "repair-loft", "listening-room", "green-refuge", "repair-cooperative", "mutual-aid-washhouse", "after-hours-room", "light-sanctuary", "street-garden", "notice-arcade", "rider-support-post", "small-exhibition-platform", "connector-lounge", "water-garden", "relay-workshop", "observation-room", "used-goods-exchange", "neighborhood-kitchen", "community-studio", "machine-bay", "supply-cache", "mural-passage", "local-archive", "screening-club", "quiet-learning-room", "welcome-desk", "care-station-outcome", "night-office", "signal-workshop", "rest-terrace", "weather-shelter" };

        public static Vector3 ParcelCentre(IEnumerable<int> cells)
        {
            List<int> values = (cells ?? Enumerable.Empty<int>())
                .Where(value => value >= 0 && value < SlopCityCityForge.GridSize * SlopCityCityForge.GridSize)
                .ToList();
            if (values.Count == 0)
                return new Vector3(SlopCityCityForge.ChunkSizeMeters * .5f, 0f, SlopCityCityForge.ChunkSizeMeters * .5f);
            return new Vector3(
                values.Average(value => (value % SlopCityCityForge.GridSize + .5f) * SlopCityCityForge.CellSizeMeters),
                0f,
                values.Average(value => (value / SlopCityCityForge.GridSize + .5f) * SlopCityCityForge.CellSizeMeters));
        }

        /// <summary>
        /// Converts the approved grid parcel to the same bounded rectangular
        /// envelope used by the physical building compiler. Legacy irregular
        /// parcels intentionally retain that stable enclosing representation.
        /// </summary>
        public static void ParcelBounds(IEnumerable<int> cells, out Vector3 centre, out float width, out float depth)
        {
            List<int> values = (cells ?? Enumerable.Empty<int>())
                .Where(value => value >= 0 && value < SlopCityCityForge.GridSize * SlopCityCityForge.GridSize)
                .ToList();
            float cell = SlopCityCityForge.CellSizeMeters;
            if (values.Count == 0)
            {
                centre = new Vector3(SlopCityCityForge.ChunkSizeMeters * .5f, 0f, SlopCityCityForge.ChunkSizeMeters * .5f);
                width = depth = cell;
                return;
            }
            int minimumX = values.Min(value => value % SlopCityCityForge.GridSize);
            int maximumX = values.Max(value => value % SlopCityCityForge.GridSize);
            int minimumZ = values.Min(value => value / SlopCityCityForge.GridSize);
            int maximumZ = values.Max(value => value / SlopCityCityForge.GridSize);
            width = (maximumX - minimumX + 1) * cell;
            depth = (maximumZ - minimumZ + 1) * cell;
            centre = new Vector3((minimumX + maximumX + 1) * cell * .5f, 0f, (minimumZ + maximumZ + 1) * cell * .5f);
        }

        public static Vector3 RoadEdgeCentre(CityForgeRoad road)
        {
            if (road == null) return new Vector3(SlopCityCityForge.ChunkSizeMeters * .5f, 0f, SlopCityCityForge.ChunkSizeMeters * .5f);
            float cell = SlopCityCityForge.CellSizeMeters;
            float x = (road.cellX + .5f) * cell;
            float z = (road.cellZ + .5f) * cell;
            if (road.direction == CityForgeDirection.North) z = (road.cellZ + 1) * cell;
            else if (road.direction == CityForgeDirection.South) z = road.cellZ * cell;
            else if (road.direction == CityForgeDirection.East) x = (road.cellX + 1) * cell;
            else if (road.direction == CityForgeDirection.West) x = road.cellX * cell;
            return new Vector3(x, 0f, z);
        }

        public static Vector3 RoadNormal(CityForgeDirection direction)
        {
            if (direction == CityForgeDirection.North) return Vector3.forward;
            if (direction == CityForgeDirection.South) return Vector3.back;
            if (direction == CityForgeDirection.East) return Vector3.right;
            return Vector3.left;
        }

        public static Color RoadColor(CityForgeRoadClass roadClass)
        {
            if (roadClass == CityForgeRoadClass.Boulevard) return new Color(.12f, .14f, .16f);
            if (roadClass == CityForgeRoadClass.TransitServiceCorridor) return new Color(.15f, .17f, .19f);
            if (roadClass == CityForgeRoadClass.PedestrianRoute) return new Color(.34f, .30f, .24f);
            if (roadClass == CityForgeRoadClass.ServiceAlley) return new Color(.25f, .26f, .25f);
            return new Color(.19f, .20f, .20f);
        }

        public static Color FacadeColor(string id)
        {
            switch (id)
            {
                case "poured-concrete": return new Color(.42f, .44f, .43f);
                case "glazed-ceramic": return new Color(.24f, .48f, .51f);
                case "oxidized-metal": return new Color(.27f, .48f, .42f);
                case "painted-render": return new Color(.62f, .43f, .36f);
                case "brick-utility": return new Color(.47f, .23f, .19f);
                case "night-glass": return new Color(.10f, .19f, .30f);
                case "garden-lattice": return new Color(.31f, .52f, .31f);
                default: return new Color(.40f, .35f, .30f);
            }
        }

        public static Color RoofColor(string id)
        {
            switch (id)
            {
                case "rain-garden": return new Color(.20f, .39f, .22f);
                case "solar-rack": return new Color(.12f, .20f, .31f);
                case "water-tank": return new Color(.18f, .35f, .48f);
                case "relay-array": return new Color(.36f, .31f, .49f);
                case "lantern-roof": return new Color(.63f, .46f, .20f);
                case "terrace": return new Color(.42f, .34f, .27f);
                case "greenhouse": return new Color(.27f, .51f, .44f);
                case "vent-stack": return new Color(.34f, .37f, .36f);
                case "civic-canopy": return new Color(.33f, .42f, .54f);
                case "screen-crown": return new Color(.42f, .20f, .45f);
                case "utility-bridge": return new Color(.23f, .29f, .31f);
                default: return new Color(.30f, .31f, .30f);
            }
        }

        public static Color DeviceColor(string id)
        {
            if (id == "public-led-01") return new Color(.14f, .24f, .30f);
            if (id == "public-projector-01") return new Color(.27f, .25f, .18f);
            if (id == "public-window-01") return new Color(.12f, .28f, .24f);
            return new Color(.11f, .11f, .13f);
        }

        public static Vector2 ScreenMapSize(CityForgeResolvedScreen screen)
        {
            if (screen != null && string.Equals(screen.screenGroup, "interior", StringComparison.Ordinal)) return new Vector2(1.18f, .76f);
            return new Vector2(1.35f, .82f);
        }

        public static Color ServiceColor(string type)
        {
            return CatalogColor(CatalogIndex(ServiceKits, type), .48f, .72f);
        }

        public static Color LandmarkColor(string outcomeId, string state)
        {
            Color baseColor = CatalogColor(CatalogIndex(OutcomeKits, outcomeId), .48f, .76f);
            if (state == "active" || state == "opening" || state == "established") return Color.Lerp(baseColor, Color.white, .22f);
            if (state == "paused") return Color.Lerp(baseColor, Color.gray, .58f);
            if (state == "resolved" || state == "archived") return Color.Lerp(baseColor, Color.black, .35f);
            return Color.Lerp(baseColor, Color.gray, .24f);
        }

        private static int CatalogIndex(string[] catalog, string value)
        {
            int index = Array.IndexOf(catalog, value ?? string.Empty);
            return index < 0 ? 0 : index;
        }

        private static Color CatalogColor(int variant, float saturation, float value)
        {
            return Color.HSVToRGB((variant % 30) / 30f, saturation, value);
        }
    }
}
