using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SlopCity
{
    /// <summary>
    /// Immutable shared-runtime City Forge catalog.  IDs are intentionally compact
    /// and stable because accepted player state references these, never Unity paths
    /// or object instances.  Updating this catalog requires an explicit compatible
    /// catalog migration before an older world can be activated.
    /// </summary>
    public sealed class SlopCityCityForgeCatalog
    {
        // The reviewed identity is shared with contracts/city-forge.mjs and the
        // service sidecar. It changes only with an explicit compatible catalog
        // migration; clients never choose it.
        public const string ReviewedCatalogHash = "a4d8eaa6b9c52300a3739ffd2dc5d2173b6d51bccbbd30e6eb38586270a291ad";
        private readonly Dictionary<string, CityForgeBuildingArchetype> buildings;
        private readonly Dictionary<string, CityForgeScreenMount> mounts;
        private readonly Dictionary<string, CityForgeLandmarkClass> landmarks;
        private readonly HashSet<string> facades;
        private readonly HashSet<string> roofs;
        private readonly HashSet<string> services;
        private readonly HashSet<string> interiors;
        private readonly HashSet<string> storefronts;
        private readonly HashSet<string> situations;
        private readonly Dictionary<string, CityForgeSituationRequirement> situationRequirements;
        private readonly HashSet<string> screenDevices;

        public IReadOnlyCollection<CityForgeBuildingArchetype> Buildings { get { return buildings.Values; } }
        public IReadOnlyCollection<CityForgeScreenMount> ScreenMounts { get { return mounts.Values; } }
        public IReadOnlyCollection<CityForgeLandmarkClass> LandmarkClasses { get { return landmarks.Values; } }
        public IReadOnlyCollection<string> Facades { get { return facades; } }
        public IReadOnlyCollection<string> Roofs { get { return roofs; } }
        public IReadOnlyCollection<string> Services { get { return services; } }
        public IReadOnlyCollection<string> Interiors { get { return interiors; } }
        public IReadOnlyCollection<string> Situations { get { return situations; } }
        public string Hash { get; private set; }
        public int GeneratorVersion { get; private set; }

        public static SlopCityCityForgeCatalog Default { get; } = CreateDefault();

        private SlopCityCityForgeCatalog(
            IEnumerable<CityForgeBuildingArchetype> buildingValues,
            IEnumerable<string> facadeValues,
            IEnumerable<string> roofValues,
            IEnumerable<string> serviceValues,
            IEnumerable<string> interiorValues,
            IEnumerable<CityForgeScreenMount> mountValues,
            IEnumerable<string> deviceValues,
            IEnumerable<CityForgeLandmarkClass> landmarkValues,
            IEnumerable<string> situationValues,
            int generatorVersion)
        {
            buildings = buildingValues.ToDictionary(value => value.id, StringComparer.Ordinal);
            facades = new HashSet<string>(facadeValues, StringComparer.Ordinal);
            roofs = new HashSet<string>(roofValues, StringComparer.Ordinal);
            services = new HashSet<string>(serviceValues, StringComparer.Ordinal);
            interiors = new HashSet<string>(interiorValues, StringComparer.Ordinal);
            storefronts = new HashSet<string>(new[] { "none", "shutter", "awning", "glazed", "arcade", "market-stall" }, StringComparer.Ordinal);
            mounts = mountValues.ToDictionary(value => value.id, StringComparer.Ordinal);
            screenDevices = new HashSet<string>(deviceValues, StringComparer.Ordinal);
            landmarks = landmarkValues.ToDictionary(value => value.id, StringComparer.Ordinal);
            situations = new HashSet<string>(situationValues, StringComparer.Ordinal);
            situationRequirements = CreateSituationRequirements(landmarkValues);
            GeneratorVersion = generatorVersion;
            Hash = SlopCityCityForge.Sha256(CanonicalCatalog());
        }

        public bool TryGetBuilding(string id, out CityForgeBuildingArchetype value)
        {
            return buildings.TryGetValue(id ?? string.Empty, out value);
        }

        public bool TryGetMount(string id, out CityForgeScreenMount value)
        {
            return mounts.TryGetValue(id ?? string.Empty, out value);
        }

        public bool TryGetLandmark(string id, out CityForgeLandmarkClass value)
        {
            return landmarks.TryGetValue(id ?? string.Empty, out value);
        }

        public bool IsFacade(string id) { return facades.Contains(id ?? string.Empty); }
        public bool IsRoof(string id) { return roofs.Contains(id ?? string.Empty); }
        public bool IsService(string id) { return services.Contains(id ?? string.Empty); }
        public bool IsInterior(string id) { return interiors.Contains(id ?? string.Empty); }
        public bool IsStorefront(string id) { return storefronts.Contains(id ?? string.Empty); }
        public bool IsSituation(string id) { return situations.Contains(id ?? string.Empty); }
        public bool IsScreenDevice(string id) { return screenDevices.Contains(id ?? string.Empty); }
        public bool IsLandmarkOutcome(string id)
        {
            return landmarks.Values.Any(value => value != null && Array.IndexOf(value.outcomes ?? Array.Empty<string>(), id ?? string.Empty) >= 0);
        }

        public bool TryGetSituationRequirement(string situationId, out CityForgeSituationRequirement value)
        {
            return situationRequirements.TryGetValue(situationId ?? string.Empty, out value);
        }

        /// <summary>Exact shared authored anchor/prerequisite grammar for CitySituation records.</summary>
        public bool SituationRequirementsMet(CityForgeSituationState situation, IEnumerable<CityForgeLandmark> values)
        {
            if (situation == null || !TryGetSituationRequirement(situation.situationId, out CityForgeSituationRequirement requirement)) return false;
            List<CityForgeLandmark> landmarks = (values ?? Enumerable.Empty<CityForgeLandmark>()).Where(item => item != null).ToList();
            if (!SituationAnchorMatches(situation, landmarks)) return false;
            CityForgeLandmark anchor = landmarks.FirstOrDefault(item => string.Equals(item.id, situation.landmarkId, StringComparison.Ordinal));
            return SituationRequirementsMet(situation, anchor, landmarks);
        }

        /// <summary>
        /// Checks prerequisites against an accepted-world outcome index while
        /// retaining the chunk-local anchor identity. Landmark IDs are local to
        /// a plan, so a global list must never choose an anchor by id.
        /// </summary>
        public bool SituationRequirementsMet(CityForgeSituationState situation, CityForgeLandmark localAnchor, IEnumerable<CityForgeLandmark> values)
        {
            if (situation == null || localAnchor == null || !TryGetSituationRequirement(situation.situationId, out CityForgeSituationRequirement requirement)
                || !string.Equals(localAnchor.id, situation.landmarkId, StringComparison.Ordinal) || Array.IndexOf(requirement.anchorOutcomes, localAnchor.outcomeId) < 0) return false;
            List<CityForgeLandmark> landmarks = (values ?? Enumerable.Empty<CityForgeLandmark>()).Where(item => item != null).ToList();
            if (situation.state == "archived") return true;
            foreach (string outcome in requirement.requiredOutcomes)
                if (!landmarks.Any(item => string.Equals(item.outcomeId, outcome, StringComparison.Ordinal) && Array.IndexOf(requirement.requiredStates, item.state) >= 0)) return false;
            return true;
        }

        /// <summary>
        /// Checks the same reviewed prerequisite grammar against the compact
        /// authoritative outcome index used by bounded hosted page caches. The
        /// local anchor is deliberately supplied separately because landmark IDs
        /// are only unique inside their owning ChunkPlan.
        /// </summary>
        public bool SituationRequirementsMet(CityForgeSituationState situation, CityForgeLandmark localAnchor, CityForgeOutcomeIndex index)
        {
            if (situation == null || localAnchor == null || index == null || !TryGetSituationRequirement(situation.situationId, out CityForgeSituationRequirement requirement)
                || !string.Equals(localAnchor.id, situation.landmarkId, StringComparison.Ordinal) || Array.IndexOf(requirement.anchorOutcomes, localAnchor.outcomeId) < 0) return false;
            if (situation.state == "archived") return true;
            foreach (string outcome in requirement.requiredOutcomes)
            {
                bool live = false;
                foreach (string state in requirement.requiredStates ?? Array.Empty<string>())
                    if (index.HasLiveOutcome(outcome, state)) { live = true; break; }
                if (!live) return false;
            }
            return true;
        }

        public bool SituationAnchorMatches(CityForgeSituationState situation, IEnumerable<CityForgeLandmark> values)
        {
            if (situation == null || !TryGetSituationRequirement(situation.situationId, out CityForgeSituationRequirement requirement)) return false;
            CityForgeLandmark anchor = (values ?? Enumerable.Empty<CityForgeLandmark>()).FirstOrDefault(item => item != null && string.Equals(item.id, situation.landmarkId, StringComparison.Ordinal));
            return anchor != null && Array.IndexOf(requirement.anchorOutcomes, anchor.outcomeId) >= 0;
        }

        /// <summary>Derives only catalog-approved physical opportunities from accepted Cityworks state.</summary>
        public IEnumerable<CityForgeSituationOpportunity> AvailableSituationOpportunities(CityForgeWorldHead head, CityForgeCoord coordinate, IEnumerable<CityForgeLandmark> localValues, IEnumerable<CityForgeLandmark> worldValues, CityForgeOutcomeIndex outcomeIndex = null)
        {
            List<CityForgeLandmark> localLandmarks = (localValues ?? Enumerable.Empty<CityForgeLandmark>()).Where(item => item != null).ToList();
            List<CityForgeLandmark> requirements = (worldValues ?? localLandmarks).Where(item => item != null).ToList();
            foreach (CityForgeLandmark landmark in localLandmarks.OrderBy(item => item.id, StringComparer.Ordinal))
                foreach (KeyValuePair<string, CityForgeSituationRequirement> pair in situationRequirements.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    if (Array.IndexOf(pair.Value.anchorOutcomes, landmark.outcomeId) < 0) continue;
                    CityForgeSituationState probe = new CityForgeSituationState { situationId = pair.Key, landmarkId = landmark.id, state = "available" };
                    if (outcomeIndex != null ? !SituationRequirementsMet(probe, landmark, outcomeIndex) : !SituationRequirementsMet(probe, landmark, requirements)) continue;
                    string seed = SlopCityCityForge.Sha256((head == null ? string.Empty : head.worldSeed) + "|" + coordinate.x + "|" + coordinate.z + "|" + landmark.id + "|" + pair.Key).Substring(0, 32);
                    yield return new CityForgeSituationOpportunity { landmarkId = landmark.id, situationId = pair.Key, stableSeed = seed };
                }
        }

        public bool IsMountCompatible(CityForgeScreenMount mount, CityForgeBuildingArchetype building)
        {
            if (mount == null || building == null || mount.compatibleTags == null || building.tags == null) return false;
            for (int i = 0; i < mount.compatibleTags.Length; i++)
                for (int j = 0; j < building.tags.Length; j++)
                    if (string.Equals(mount.compatibleTags[i], building.tags[j], StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Exact CITY_FORGE_BUILDING_ZONES mapping from the public contract.</summary>
        public bool IsBuildingAllowedInZone(string blueprintId, CityForgeZone zone)
        {
            switch (blueprintId ?? string.Empty)
            {
                case "walk-up": case "courtyard-home": case "tower-residence": return zone == CityForgeZone.Home;
                case "live-work-block": return zone == CityForgeZone.Home || zone == CityForgeZone.WorkService || zone == CityForgeZone.Trade;
                case "workshop": case "laundry": return zone == CityForgeZone.WorkService;
                case "repair-bay": case "depot": return zone == CityForgeZone.WorkService || zone == CityForgeZone.GreenInfrastructure;
                case "clinic": return zone == CityForgeZone.WorkService || zone == CityForgeZone.Civic;
                case "small-office": return zone == CityForgeZone.WorkService || zone == CityForgeZone.Trade;
                case "corner-shop": case "market-hall": case "exchange": return zone == CityForgeZone.Trade;
                case "cafe": return zone == CityForgeZone.Trade || zone == CityForgeZone.Culture;
                case "kiosk": return zone == CityForgeZone.Trade || zone == CityForgeZone.Civic;
                case "care-station": return zone == CityForgeZone.Civic;
                case "library-room": case "community-hall": return zone == CityForgeZone.Civic || zone == CityForgeZone.Culture;
                case "utility-office": return zone == CityForgeZone.Civic || zone == CityForgeZone.GreenInfrastructure;
                case "screening-room": case "studio": case "venue": case "performance-space": return zone == CityForgeZone.Culture;
                case "archive": return zone == CityForgeZone.Culture || zone == CityForgeZone.Civic;
                case "garden": case "water-node": case "power-relay": return zone == CityForgeZone.GreenInfrastructure;
                case "plaza": return zone == CityForgeZone.GreenInfrastructure || zone == CityForgeZone.Civic;
                case "shelter": return zone == CityForgeZone.GreenInfrastructure || zone == CityForgeZone.Civic;
                case "service-yard": return zone == CityForgeZone.GreenInfrastructure || zone == CityForgeZone.WorkService;
                default: return false;
            }
        }

        /// <summary>Exact CITY_FORGE_BUILDING_SCREEN_MOUNTS compatibility.</summary>
        public bool IsBuildingScreenMount(string blueprintId, string mountId)
        {
            switch (blueprintId ?? string.Empty)
            {
                case "walk-up": case "courtyard-home": case "tower-residence": case "workshop": case "clinic": case "laundry": case "small-office": case "cafe": case "care-station": case "library-room": case "utility-office": case "studio": case "archive": return mountId == "interior-wall-tv";
                case "live-work-block": case "corner-shop": case "market-hall": case "community-hall": case "screening-room": case "venue": case "performance-space": return mountId == "facade-media-wall" || mountId == "interior-wall-tv";
                case "repair-bay": case "depot": case "exchange": case "kiosk": case "power-relay": case "service-yard": return mountId == "facade-media-wall";
                case "garden": case "plaza": case "water-node": case "shelter": return mountId == "plaza-civic-support";
                default: return false;
            }
        }

        /// <summary>Exact CITY_FORGE_BUILDING_SCREEN_SLOTS lookup from the shared contract.</summary>
        public int GetBuildingScreenSlots(string blueprintId)
        {
            return TryGetBuilding(blueprintId, out CityForgeBuildingArchetype building) ? Math.Max(1, building.screenSlots) : 0;
        }

        /// <summary>Exact CITY_FORGE_SITE_SCREEN_SLOTS lookup; each approved public site has one shipped support.</summary>
        public int GetSiteScreenSlots(CityForgeZone zone)
        {
            return zone == CityForgeZone.Civic || zone == CityForgeZone.Culture || zone == CityForgeZone.GreenInfrastructure ? 1 : 0;
        }

        /// <summary>Exact CITY_FORGE_ROAD_SCREEN_SLOTS lookup; each approved road edge has one shipped kiosk support.</summary>
        public int GetRoadScreenSlots(CityForgeRoadClass roadClass)
        {
            switch (roadClass)
            {
                case CityForgeRoadClass.LocalStreet:
                case CityForgeRoadClass.Boulevard:
                case CityForgeRoadClass.ServiceAlley:
                case CityForgeRoadClass.PedestrianRoute:
                case CityForgeRoadClass.TransitServiceCorridor:
                    return 1;
                default:
                    return 0;
            }
        }

        private string CanonicalCatalog()
        {
            StringBuilder value = new StringBuilder();
            value.Append("generator:").Append(GeneratorVersion).Append('\n');
            value.Append("limits:roads:").Append(SlopCityCityForge.MaximumRoadRecords).Append(':')
                .Append("parcels:").Append(SlopCityCityForge.MaximumParcelRecords).Append(':')
                .Append("buildings:").Append(SlopCityCityForge.MaximumBuildingRecords).Append(':')
                .Append("services:").Append(SlopCityCityForge.MaximumServiceRecords).Append(':')
                .Append("screens:").Append(SlopCityCityForge.MaximumScreenRecords).Append(':')
                .Append("landmarks:").Append(SlopCityCityForge.MaximumLandmarkRecords).Append(':')
                .Append("situations:96").Append('\n');
            foreach (CityForgeBuildingArchetype building in buildings.Values.OrderBy(item => item.id, StringComparer.Ordinal))
            {
                value.Append("building:").Append(building.id).Append(':').Append((int)building.zone).Append(':')
                    .Append(building.minimumFloors).Append(':').Append(building.maximumFloors).Append(':')
                    .Append(building.assetCost).Append(':').Append(building.screenSlots).Append(':')
                    .Append(building.heroInteriorEligible ? '1' : '0').Append(':')
                    .Append(string.Join(",", building.tags ?? Array.Empty<string>())).Append('\n');
            }
            value.Append("site-screen-slots:civic:").Append(GetSiteScreenSlots(CityForgeZone.Civic)).Append('\n');
            value.Append("site-screen-slots:culture:").Append(GetSiteScreenSlots(CityForgeZone.Culture)).Append('\n');
            value.Append("site-screen-slots:green-infrastructure:").Append(GetSiteScreenSlots(CityForgeZone.GreenInfrastructure)).Append('\n');
            value.Append("road-screen-slots:local-street:").Append(GetRoadScreenSlots(CityForgeRoadClass.LocalStreet)).Append('\n');
            value.Append("road-screen-slots:boulevard:").Append(GetRoadScreenSlots(CityForgeRoadClass.Boulevard)).Append('\n');
            value.Append("road-screen-slots:service-alley:").Append(GetRoadScreenSlots(CityForgeRoadClass.ServiceAlley)).Append('\n');
            value.Append("road-screen-slots:pedestrian-route:").Append(GetRoadScreenSlots(CityForgeRoadClass.PedestrianRoute)).Append('\n');
            value.Append("road-screen-slots:transit-service-corridor:").Append(GetRoadScreenSlots(CityForgeRoadClass.TransitServiceCorridor)).Append('\n');
            foreach (string facade in facades.OrderBy(item => item, StringComparer.Ordinal)) value.Append("facade:").Append(facade).Append('\n');
            foreach (string roof in roofs.OrderBy(item => item, StringComparer.Ordinal)) value.Append("roof:").Append(roof).Append('\n');
            foreach (string service in services.OrderBy(item => item, StringComparer.Ordinal)) value.Append("service:").Append(service).Append('\n');
            foreach (string interior in interiors.OrderBy(item => item, StringComparer.Ordinal)) value.Append("interior:").Append(interior).Append('\n');
            foreach (CityForgeScreenMount mount in mounts.Values.OrderBy(item => item.id, StringComparer.Ordinal))
                value.Append("mount:").Append(mount.id).Append(':').Append(mount.assetCost).Append(':').Append(mount.screenGroup).Append(':')
                    .Append(string.Join(",", mount.compatibleTags ?? Array.Empty<string>())).Append('\n');
            foreach (string device in screenDevices.OrderBy(item => item, StringComparer.Ordinal)) value.Append("device:").Append(device).Append('\n');
            foreach (CityForgeLandmarkClass landmark in landmarks.Values.OrderBy(item => item.id, StringComparer.Ordinal))
                value.Append("landmark:").Append(landmark.id).Append(':')
                    .Append(string.Join(",", (landmark.compatibleZones ?? Array.Empty<CityForgeZone>()).Select(item => ((int)item).ToString())))
                    .Append(':').Append(string.Join(",", landmark.outcomes ?? Array.Empty<string>())).Append('\n');
            foreach (string situation in situations.OrderBy(item => item, StringComparer.Ordinal)) value.Append("situation:").Append(situation).Append('\n');
            foreach (KeyValuePair<string, CityForgeSituationRequirement> pair in situationRequirements.OrderBy(item => item.Key, StringComparer.Ordinal))
                value.Append("situation-requirement:").Append(pair.Key).Append(':')
                    .Append(string.Join(",", pair.Value.anchorOutcomes ?? Array.Empty<string>())).Append(':')
                    .Append(string.Join(",", pair.Value.requiredOutcomes ?? Array.Empty<string>())).Append(':')
                    .Append(string.Join(",", pair.Value.requiredStates ?? Array.Empty<string>())).Append('\n');
            return value.ToString();
        }

        private static CityForgeSituationRequirement Requirement(string[] anchors, string[] required, params string[] states)
        {
            return new CityForgeSituationRequirement { anchorOutcomes = anchors, requiredOutcomes = required, requiredStates = states };
        }

        /// <summary>Exact CITY_FORGE_SITUATION_REQUIREMENTS map from the shared protocol.</summary>
        private static Dictionary<string, CityForgeSituationRequirement> CreateSituationRequirements(IEnumerable<CityForgeLandmarkClass> values)
        {
            Dictionary<string, CityForgeSituationRequirement> result = new Dictionary<string, CityForgeSituationRequirement>(StringComparer.Ordinal);
            foreach (CityForgeLandmarkClass landmark in values ?? Enumerable.Empty<CityForgeLandmarkClass>())
                foreach (string outcome in landmark.outcomes ?? Array.Empty<string>())
                {
                    result[outcome + "-arrival"] = Requirement(new[] { outcome }, new[] { outcome }, "active", "opening", "established");
                    result[outcome + "-continuance"] = Requirement(new[] { outcome }, new[] { outcome }, "active", "opening", "established");
                }
            result["repair-care-connection"] = Requirement(new[] { "repair-cooperative", "mutual-aid-washhouse" }, new[] { "repair-cooperative", "mutual-aid-washhouse" }, "established");
            result["repair-culture-connection"] = Requirement(new[] { "repair-loft", "local-archive" }, new[] { "repair-loft", "local-archive" }, "established");
            result["repair-access-connection"] = Requirement(new[] { "repair-cooperative", "rider-support-post" }, new[] { "repair-cooperative", "rider-support-post" }, "established");
            result["care-culture-connection"] = Requirement(new[] { "mutual-aid-washhouse", "screening-club" }, new[] { "mutual-aid-washhouse", "screening-club" }, "established");
            result["care-access-connection"] = Requirement(new[] { "care-station-outcome", "rider-support-post" }, new[] { "care-station-outcome", "rider-support-post" }, "established");
            result["care-pressure-connection"] = Requirement(new[] { "mutual-aid-washhouse", "night-office" }, new[] { "mutual-aid-washhouse", "night-office" }, "established");
            result["culture-access-connection"] = Requirement(new[] { "local-archive", "connector-lounge" }, new[] { "local-archive", "connector-lounge" }, "established");
            result["culture-pressure-connection"] = Requirement(new[] { "after-hours-room", "night-office" }, new[] { "after-hours-room", "night-office" }, "established");
            result["access-pressure-connection"] = Requirement(new[] { "signal-workshop", "weather-shelter" }, new[] { "signal-workshop", "weather-shelter" }, "established");
            result["garden-water-connection"] = Requirement(new[] { "street-garden", "water-garden" }, new[] { "street-garden", "water-garden" }, "established");
            result["garden-shelter-connection"] = Requirement(new[] { "green-refuge", "weather-shelter" }, new[] { "green-refuge", "weather-shelter" }, "established");
            result["screen-archive-connection"] = Requirement(new[] { "screening-club", "local-archive" }, new[] { "screening-club", "local-archive" }, "established");
            result["screen-night-connection"] = Requirement(new[] { "screening-club", "night-office" }, new[] { "screening-club", "night-office" }, "established");
            result["transit-trade-connection"] = Requirement(new[] { "rider-support-post", "used-goods-exchange" }, new[] { "rider-support-post", "used-goods-exchange" }, "established");
            result["transit-care-connection"] = Requirement(new[] { "connector-lounge", "care-station-outcome" }, new[] { "connector-lounge", "care-station-outcome" }, "established");
            result["utility-repair-connection"] = Requirement(new[] { "relay-workshop", "machine-bay" }, new[] { "relay-workshop", "machine-bay" }, "established");
            result["utility-weather-connection"] = Requirement(new[] { "water-garden", "weather-shelter" }, new[] { "water-garden", "weather-shelter" }, "established");
            result["home-trade-connection"] = Requirement(new[] { "listening-room", "community-studio" }, new[] { "listening-room", "community-studio" }, "established");
            result["lobby-welcome-connection"] = Requirement(new[] { "welcome-desk", "care-station-outcome" }, new[] { "welcome-desk", "care-station-outcome" }, "established");
            result["signal-roof-connection"] = Requirement(new[] { "signal-workshop", "observation-room" }, new[] { "signal-workshop", "observation-room" }, "established");
            return result;
        }

        private static CityForgeBuildingArchetype Building(string id, CityForgeZone zone, int minimumFloors, int maximumFloors, int assetCost, int screens, bool hero, params string[] tags)
        {
            return new CityForgeBuildingArchetype
            {
                id = id, zone = zone, minimumFloors = minimumFloors, maximumFloors = maximumFloors,
                assetCost = assetCost, screenSlots = screens, heroInteriorEligible = hero, tags = tags
            };
        }

        private static CityForgeLandmarkClass Landmark(string id, CityForgeZone[] zones, params string[] outcomes)
        {
            return new CityForgeLandmarkClass { id = id, compatibleZones = zones, outcomes = outcomes };
        }

        private static SlopCityCityForgeCatalog CreateDefault()
        {
            CityForgeBuildingArchetype[] buildingValues =
            {
                Building("walk-up", CityForgeZone.Home, 2, 5, 6, 1, true, "home", "residence", "street"),
                Building("courtyard-home", CityForgeZone.Home, 2, 4, 8, 2, true, "home", "residence", "courtyard"),
                Building("tower-residence", CityForgeZone.Home, 5, 10, 12, 4, true, "home", "residence", "tower"),
                Building("live-work-block", CityForgeZone.Home, 2, 6, 10, 3, true, "home", "work", "street"),
                Building("workshop", CityForgeZone.WorkService, 1, 3, 7, 1, true, "work", "service", "alley"),
                Building("repair-bay", CityForgeZone.WorkService, 1, 2, 8, 1, false, "work", "service", "vehicle"),
                Building("clinic", CityForgeZone.WorkService, 2, 4, 9, 2, true, "care", "service", "street"),
                Building("laundry", CityForgeZone.WorkService, 1, 2, 6, 1, true, "care", "service", "street"),
                Building("depot", CityForgeZone.WorkService, 1, 3, 9, 1, false, "work", "service", "vehicle"),
                Building("small-office", CityForgeZone.WorkService, 2, 5, 8, 2, true, "work", "service", "street"),
                Building("corner-shop", CityForgeZone.Trade, 1, 3, 7, 2, true, "trade", "street", "media"),
                Building("market-hall", CityForgeZone.Trade, 1, 3, 11, 4, true, "trade", "hall", "media"),
                Building("cafe", CityForgeZone.Trade, 1, 3, 7, 2, true, "trade", "culture", "street"),
                Building("exchange", CityForgeZone.Trade, 1, 3, 8, 2, true, "trade", "service", "street"),
                Building("kiosk", CityForgeZone.Trade, 1, 1, 4, 1, false, "trade", "street", "plaza"),
                Building("care-station", CityForgeZone.Civic, 1, 3, 8, 2, true, "care", "civic", "street"),
                Building("library-room", CityForgeZone.Civic, 1, 3, 8, 2, true, "culture", "civic", "quiet"),
                Building("utility-office", CityForgeZone.Civic, 1, 3, 7, 1, false, "utility", "civic", "service"),
                Building("community-hall", CityForgeZone.Civic, 1, 4, 10, 4, true, "culture", "civic", "hall", "media"),
                Building("screening-room", CityForgeZone.Culture, 1, 3, 9, 5, true, "culture", "media", "hall"),
                Building("studio", CityForgeZone.Culture, 1, 4, 8, 3, true, "culture", "media", "street"),
                Building("venue", CityForgeZone.Culture, 1, 4, 10, 5, true, "culture", "media", "night"),
                Building("archive", CityForgeZone.Culture, 1, 3, 8, 2, true, "culture", "quiet", "media"),
                Building("performance-space", CityForgeZone.Culture, 1, 3, 9, 4, true, "culture", "plaza", "media"),
                Building("garden", CityForgeZone.GreenInfrastructure, 1, 1, 4, 1, false, "green", "plaza", "street"),
                Building("plaza", CityForgeZone.GreenInfrastructure, 1, 1, 5, 4, false, "green", "plaza", "media"),
                Building("water-node", CityForgeZone.GreenInfrastructure, 1, 2, 7, 1, false, "utility", "green", "service"),
                Building("power-relay", CityForgeZone.GreenInfrastructure, 1, 3, 8, 2, false, "utility", "service", "roof"),
                Building("shelter", CityForgeZone.GreenInfrastructure, 1, 2, 6, 1, true, "care", "green", "street"),
                Building("service-yard", CityForgeZone.GreenInfrastructure, 1, 2, 7, 1, false, "utility", "service", "alley")
            };

            string[] facadeValues = { "wet-masonry", "poured-concrete", "glazed-ceramic", "oxidized-metal", "painted-render", "brick-utility", "night-glass", "garden-lattice" };
            string[] roofValues = { "service-roof", "rain-garden", "solar-rack", "water-tank", "relay-array", "lantern-roof", "terrace", "greenhouse", "vent-stack", "civic-canopy", "screen-crown", "utility-bridge" };
            string[] serviceValues = { "care-station", "repair-counter", "laundry-service", "delivery-bay", "neighborhood-office", "market-stall", "food-counter", "exchange-desk", "library-desk", "utility-desk", "community-room", "screening-service", "studio-service", "venue-door", "archive-desk", "garden-steward", "water-service", "power-service", "shelter-desk", "service-yard-bay" };
            string[] interiorValues = { "home-room", "workshop-floor", "repair-counter", "care-room", "laundry-room", "depot-bay", "office-floor", "shop-floor", "market-floor", "cafe-room", "exchange-room", "library-room", "community-room", "screening-room", "studio-room", "venue-room", "archive-room", "performance-room", "utility-room", "shelter-room" };
            CityForgeScreenMount[] mountValues =
            {
                new CityForgeScreenMount { id = "facade-media-wall", compatibleTags = new [] { "media", "street", "trade", "culture" }, assetCost = 2, screenGroup = "street" },
                new CityForgeScreenMount { id = "interior-wall-tv", compatibleTags = new [] { "media", "culture", "care", "home", "hall" }, assetCost = 1, screenGroup = "interior" },
                new CityForgeScreenMount { id = "plaza-civic-support", compatibleTags = new [] { "plaza", "green", "civic", "street" }, assetCost = 2, screenGroup = "street" },
                new CityForgeScreenMount { id = "street-kiosk-screen", compatibleTags = new [] { "street", "service", "trade" }, assetCost = 1, screenGroup = "street" }
            };
            string[] devices = { "public-crt-01", "public-led-01", "public-projector-01", "public-window-01" };
            CityForgeLandmarkClass[] landmarkValues =
            {
                Landmark("home-workroom", new [] { CityForgeZone.Home }, "repair-loft", "listening-room", "green-refuge"),
                Landmark("service-court", new [] { CityForgeZone.WorkService }, "repair-cooperative", "mutual-aid-washhouse", "after-hours-room"),
                Landmark("street-corner", new [] { CityForgeZone.Civic, CityForgeZone.GreenInfrastructure }, "light-sanctuary", "street-garden", "notice-arcade"),
                Landmark("transit-landing", new [] { CityForgeZone.WorkService, CityForgeZone.Civic }, "rider-support-post", "small-exhibition-platform", "connector-lounge"),
                Landmark("roof-reservoir", new [] { CityForgeZone.GreenInfrastructure, CityForgeZone.Home, CityForgeZone.WorkService }, "water-garden", "relay-workshop", "observation-room"),
                Landmark("tradefront", new [] { CityForgeZone.Trade }, "used-goods-exchange", "neighborhood-kitchen", "community-studio"),
                Landmark("utility-alley", new [] { CityForgeZone.GreenInfrastructure, CityForgeZone.WorkService }, "machine-bay", "supply-cache", "mural-passage"),
                Landmark("screen-room", new [] { CityForgeZone.Culture }, "local-archive", "screening-club", "quiet-learning-room"),
                Landmark("back-lobby", new [] { CityForgeZone.Civic, CityForgeZone.Home }, "welcome-desk", "care-station-outcome", "night-office"),
                Landmark("signal-terrace", new [] { CityForgeZone.Civic, CityForgeZone.Culture, CityForgeZone.GreenInfrastructure }, "signal-workshop", "rest-terrace", "weather-shelter")
            };
            List<string> situationValues = new List<string>();
            foreach (CityForgeLandmarkClass landmark in landmarkValues)
                foreach (string outcome in landmark.outcomes) { situationValues.Add(outcome + "-arrival"); situationValues.Add(outcome + "-continuance"); }
            situationValues.AddRange(new[] { "repair-care-connection", "repair-culture-connection", "repair-access-connection", "care-culture-connection", "care-access-connection", "care-pressure-connection", "culture-access-connection", "culture-pressure-connection", "access-pressure-connection", "garden-water-connection", "garden-shelter-connection", "screen-archive-connection", "screen-night-connection", "transit-trade-connection", "transit-care-connection", "utility-repair-connection", "utility-weather-connection", "home-trade-connection", "lobby-welcome-connection", "signal-roof-connection" });
            SlopCityCityForgeCatalog catalog = new SlopCityCityForgeCatalog(buildingValues, facadeValues, roofValues, serviceValues, interiorValues, mountValues, devices, landmarkValues, situationValues, SlopCityCityForge.GeneratorVersion);
            // Keep runtime lookup aligned with the platform-neutral protocol.
            // CanonicalCatalog remains available for development drift checks.
            catalog.Hash = ReviewedCatalogHash;
            return catalog;
        }
    }
}
