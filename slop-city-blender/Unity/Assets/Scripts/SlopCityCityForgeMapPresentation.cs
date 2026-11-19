using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopCity
{
    /// <summary>Bounded visual layers which have a direct physical Forge counterpart.</summary>
    public enum CityForgeMapFeatureKind { Ground, Road, BuildingFacade, BuildingRoof, Service, Landmark, Screen }

    /// <summary>
    /// A pure local-chunk descriptor for top-down consumers. It contains only
    /// coordinates, catalog-derived dimensions, finish colors, and approved
    /// screen host facts; it never accepts a player transform, material, mesh,
    /// or scene object.
    /// </summary>
    public sealed class CityForgeMapFeature
    {
        public string id = string.Empty;
        public CityForgeMapFeatureKind kind;
        public Vector3 localPosition;
        public Vector2 worldSize;
        public float worldHeight;
        public float elevation;
        public Color color;
        public string blueprintId = string.Empty;
        public string facadeId = string.Empty;
        public string roofId = string.Empty;
        public int floors;
        public CityForgeDirection entranceEdge;
        public string roadLocalKey = string.Empty;
        public CityForgeRoadClass roadClass;
        public string parcelId = string.Empty;
        public string serviceType = string.Empty;
        public string outcomeId = string.Empty;
        public string state = string.Empty;
        public string screenHostId = string.Empty;
        public string screenMountId = string.Empty;
        public string deviceId = string.Empty;
        public string screenGroup = string.Empty;
        public int screenSlotIndex;
        public int screenSlotCount;
        public bool catalogIntegrated;
    }

    /// <summary>
    /// The sole top-down projection of an accepted resolved chunk. Both the
    /// HUD and strategic view consume these facts, while street play and review
    /// use the same parcel, road, finish, and host calculations in the runtime.
    /// </summary>
    public static class SlopCityCityForgeMapPresentation
    {
        private sealed class Host
        {
            public Vector3 position;
            public Vector3 front;
            public float screenWidth;
            public float screenDepth;
            public bool building;
        }

        public static IReadOnlyList<CityForgeMapFeature> FromResolvedChunk(CityForgeResolvedChunk chunk)
        {
            if (chunk == null || chunk.acceptedPlan == null) return Array.Empty<CityForgeMapFeature>();
            return FromPlan(chunk.acceptedPlan, chunk.roads, chunk.screens);
        }

        /// <summary>
        /// Compatibility projection for a fetched strategic plan page. Callers
        /// with a resolved chunk must use <see cref="FromResolvedChunk"/>, so
        /// catalog-integrated screens are represented whenever they are known.
        /// </summary>
        public static IReadOnlyList<CityForgeMapFeature> FromPlan(CityForgeChunkPlan plan)
        {
            if (plan == null) return Array.Empty<CityForgeMapFeature>();
            List<CityForgeResolvedScreen> screens = (plan.screens ?? new List<CityForgeScreen>())
                .Where(item => item != null)
                .OrderBy(item => item.screenId, StringComparer.Ordinal)
                .Select(item => new CityForgeResolvedScreen
                {
                    screenId = item.screenId,
                    hostId = item.hostId,
                    mountId = item.screenMountId,
                    deviceId = item.deviceId,
                    slotIndex = 0,
                    slotCount = 1,
                    catalogIntegrated = false
                }).ToList();
            return FromPlan(plan, (plan.roads ?? new List<CityForgeRoad>())
                .Where(road => SlopCityCityForge.RoadHasIslandGround(plan, road)), screens);
        }

        private static IReadOnlyList<CityForgeMapFeature> FromPlan(CityForgeChunkPlan plan,
            IEnumerable<CityForgeRoad> resolvedRoads, IEnumerable<CityForgeResolvedScreen> resolvedScreens)
        {
            if (plan == null) return Array.Empty<CityForgeMapFeature>();
            List<CityForgeMapFeature> features = new List<CityForgeMapFeature>();
            Dictionary<string, CityForgeParcel> parcels = (plan.parcels ?? new List<CityForgeParcel>())
                .Where(item => item != null && !string.IsNullOrEmpty(item.id))
                .GroupBy(item => item.id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            Dictionary<string, Host> hosts = new Dictionary<string, Host>(StringComparer.Ordinal);

            foreach (int cell in (plan.groundCells ?? new List<int>())
                .Where(item => item >= 0 && item < SlopCityCityForge.GridSize * SlopCityCityForge.GridSize)
                .Distinct().OrderBy(item => item))
            {
                int x = cell % SlopCityCityForge.GridSize;
                int z = cell / SlopCityCityForge.GridSize;
                features.Add(Feature("ground-" + cell, CityForgeMapFeatureKind.Ground,
                    new Vector3((x + .5f) * SlopCityCityForge.CellSizeMeters, 0f, (z + .5f) * SlopCityCityForge.CellSizeMeters),
                    Vector2.one * SlopCityCityForge.CellSizeMeters, .02f, new Color(.31f, .52f, .34f)));
            }

            foreach (CityForgeRoad road in (resolvedRoads ?? Enumerable.Empty<CityForgeRoad>())
                .Where(item => item != null).OrderBy(item => item.LocalKey, StringComparer.Ordinal))
            {
                bool northSouth = road.direction == CityForgeDirection.North || road.direction == CityForgeDirection.South;
                Vector3 edge = SlopCityCityForgePresentation.RoadEdgeCentre(road);
                CityForgeMapFeature roadFeature = Feature("road-" + road.LocalKey, CityForgeMapFeatureKind.Road, edge,
                    northSouth ? new Vector2(SlopCityCityForge.CellSizeMeters, 1.2f) : new Vector2(1.2f, SlopCityCityForge.CellSizeMeters),
                    .12f, SlopCityCityForgePresentation.RoadColor(road.roadClass));
                roadFeature.roadLocalKey = road.LocalKey;
                roadFeature.roadClass = road.roadClass;
                features.Add(roadFeature);
                hosts[RoadHost(road)] = new Host
                {
                    position = edge + SlopCityCityForgePresentation.RoadNormal(road.direction) * 1.48f,
                    front = SlopCityCityForgePresentation.RoadNormal(road.direction),
                    screenWidth = 2f,
                    screenDepth = 2f
                };
            }

            foreach (CityForgeBuilding building in (plan.buildings ?? new List<CityForgeBuilding>())
                .Where(item => item != null).OrderBy(item => item.id, StringComparer.Ordinal))
            {
                if (!parcels.TryGetValue(building.parcelId ?? string.Empty, out CityForgeParcel parcel)
                    || parcel.cells == null || parcel.cells.Count == 0) continue;
                SlopCityCityForgePresentation.ParcelBounds(parcel.cells, out Vector3 center, out float parcelWidth, out float parcelDepth);
                float worldWidth = Mathf.Max(4f, parcelWidth * .75f);
                float worldDepth = Mathf.Max(4f, parcelDepth * .75f);
                float worldHeight = Mathf.Clamp(building.floors, 1, 24) * 2.8f;
                float mapMassingHeight = Mathf.Clamp(worldHeight * .035f, .16f, .84f);
                Vector2 footprint = new Vector2(worldWidth, worldDepth);
                CityForgeMapFeature facade = Feature("facade-" + building.id, CityForgeMapFeatureKind.BuildingFacade, center, footprint,
                    .22f, SlopCityCityForgePresentation.FacadeColor(building.facadeId));
                CityForgeMapFeature roof = Feature("roof-" + building.id, CityForgeMapFeatureKind.BuildingRoof, center,
                    new Vector2(Mathf.Max(2.8f, worldWidth - .48f), Mathf.Max(2.8f, worldDepth - .48f)),
                    .22f + mapMassingHeight + .06f, SlopCityCityForgePresentation.RoofColor(building.roofId));
                ApplyBuildingFacts(facade, building, worldHeight);
                ApplyBuildingFacts(roof, building, worldHeight);
                features.Add(facade);
                features.Add(roof);
                hosts[building.id] = new Host
                {
                    position = center,
                    front = EntranceNormal(building.entranceEdge),
                    screenWidth = building.entranceEdge == CityForgeDirection.North || building.entranceEdge == CityForgeDirection.South ? worldWidth : worldDepth,
                    screenDepth = building.entranceEdge == CityForgeDirection.North || building.entranceEdge == CityForgeDirection.South ? worldDepth : worldWidth,
                    building = true
                };
            }

            foreach (CityForgeParcel parcel in parcels.Values
                .Where(item => item.zone == CityForgeZone.Civic || item.zone == CityForgeZone.Culture || item.zone == CityForgeZone.GreenInfrastructure)
                .OrderBy(item => item.id, StringComparer.Ordinal))
            {
                hosts["site-" + parcel.id] = new Host { position = SlopCityCityForgePresentation.ParcelCentre(parcel.cells), screenWidth = 4f, screenDepth = 4f };
            }

            foreach (CityForgeService service in (plan.services ?? new List<CityForgeService>())
                .Where(item => item != null).OrderBy(item => item.id, StringComparer.Ordinal))
            {
                if (!parcels.TryGetValue(service.parcelId ?? string.Empty, out CityForgeParcel parcel)) continue;
                CityForgeMapFeature marker = Feature("service-" + service.id, CityForgeMapFeatureKind.Service,
                    SlopCityCityForgePresentation.ParcelCentre(parcel.cells), Vector2.one * 1.45f, .40f,
                    SlopCityCityForgePresentation.ServiceColor(service.type));
                marker.parcelId = service.parcelId;
                marker.serviceType = service.type;
                features.Add(marker);
            }

            foreach (CityForgeLandmark landmark in (plan.landmarks ?? new List<CityForgeLandmark>())
                .Where(item => item != null).OrderBy(item => item.id, StringComparer.Ordinal))
            {
                if (!parcels.TryGetValue(landmark.parcelId ?? string.Empty, out CityForgeParcel parcel)) continue;
                CityForgeMapFeature marker = Feature("landmark-" + landmark.id, CityForgeMapFeatureKind.Landmark,
                    SlopCityCityForgePresentation.ParcelCentre(parcel.cells), Vector2.one * 2.05f, .50f,
                    SlopCityCityForgePresentation.LandmarkColor(landmark.outcomeId, landmark.state));
                marker.parcelId = landmark.parcelId;
                marker.outcomeId = landmark.outcomeId;
                marker.state = landmark.state;
                features.Add(marker);
            }

            foreach (CityForgeResolvedScreen screen in (resolvedScreens ?? Enumerable.Empty<CityForgeResolvedScreen>())
                .Where(item => item != null).OrderBy(item => item.screenId, StringComparer.Ordinal))
            {
                if (!hosts.TryGetValue(screen.hostId ?? string.Empty, out Host host)) continue;
                CityForgeMapFeature marker = Feature("screen-" + screen.screenId, CityForgeMapFeatureKind.Screen,
                    ScreenPosition(host, screen), SlopCityCityForgePresentation.ScreenMapSize(screen), .58f,
                    SlopCityCityForgePresentation.DeviceColor(screen.deviceId));
                marker.screenHostId = screen.hostId;
                marker.screenMountId = screen.mountId;
                marker.deviceId = screen.deviceId;
                marker.screenGroup = screen.screenGroup;
                marker.screenSlotIndex = screen.slotIndex;
                marker.screenSlotCount = screen.slotCount;
                marker.catalogIntegrated = screen.catalogIntegrated;
                features.Add(marker);
            }
            return features;
        }

        private static CityForgeMapFeature Feature(string id, CityForgeMapFeatureKind kind, Vector3 position, Vector2 size, float elevation, Color color)
        {
            return new CityForgeMapFeature { id = id ?? string.Empty, kind = kind, localPosition = position, worldSize = size, elevation = elevation, color = color };
        }

        private static void ApplyBuildingFacts(CityForgeMapFeature feature, CityForgeBuilding building, float worldHeight)
        {
            if (feature == null || building == null) return;
            feature.blueprintId = building.blueprintId;
            feature.facadeId = building.facadeId;
            feature.roofId = building.roofId;
            feature.floors = building.floors;
            feature.entranceEdge = building.entranceEdge;
            feature.worldHeight = worldHeight;
        }

        private static Vector3 ScreenPosition(Host host, CityForgeResolvedScreen screen)
        {
            if (host == null) return Vector3.zero;
            if (!host.building) return host.position;
            int count = Mathf.Max(1, screen.slotCount);
            int index = Mathf.Clamp(screen.slotIndex, 0, count - 1);
            int columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(count)), 1, 3);
            int column = index % columns;
            float lateral = (column - (columns - 1) * .5f) * Mathf.Min(2.6f, host.screenWidth / Mathf.Max(2, columns + 1));
            Vector3 side = new Vector3(-host.front.z, 0f, host.front.x);
            bool interior = (!screen.catalogIntegrated && screen.mountId == "interior-wall-tv")
                || (screen.catalogIntegrated && string.Equals(screen.screenGroup, "interior", StringComparison.Ordinal));
            if (interior) return host.position + side * lateral;
            Vector3 outward = screen.catalogIntegrated ? -host.front : host.front;
            float halfExtent = host.screenDepth * .5f;
            return host.position + side * lateral + outward * (halfExtent + .08f);
        }

        private static Vector3 EntranceNormal(CityForgeDirection direction)
        {
            if (direction == CityForgeDirection.North) return Vector3.forward;
            if (direction == CityForgeDirection.East) return Vector3.right;
            if (direction == CityForgeDirection.West) return Vector3.left;
            return Vector3.back;
        }

        private static string RoadHost(CityForgeRoad road)
        {
            string direction = road.direction == CityForgeDirection.North ? "north" : road.direction == CityForgeDirection.East ? "east" : road.direction == CityForgeDirection.South ? "south" : "west";
            return "road-" + road.cellX + "-" + road.cellZ + "-" + direction;
        }
    }
}
