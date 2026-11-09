using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SlopCity
{
    /// <summary>
    /// Strict, platform-neutral JSON boundary for the public City Forge model.
    /// Unity JsonUtility is deliberately unsuitable here because it silently
    /// drops unknown fields. This parser rejects them before data reaches a
    /// controller, resolver, local store, or physical chunk compiler.
    /// </summary>
    public static class SlopCityCityForgeWire
    {
        public const long MaximumSafeInteger = 9007199254740991L;
        // Hosted delivery is deliberately one coordinate per response. Bridges
        // build a nearby working set by composing independently verified pages.
        public const int MaximumPageRadius = 0;
        public const int MaximumApiPageBytes = SlopCityCityForge.MaximumWorldPageBytes;

        public static bool TryLoadWorldHeadJson(string json, out CityForgeWorldHead head, out string error)
        {
            head = null;
            if (!ParseObject(json, out Dictionary<string, object> raw, out error)) return false;
            return ReadWorldHead(raw, "$", out head, out error);
        }

        public static string ToWireJson(CityForgeWorldHead head)
        {
            if (head == null || head.schemaVersion != SlopCityCityForge.WorldSchemaVersion || head.generatorVersion != SlopCityCityForge.GeneratorVersion
                || head.activeWorldRevision < 1 || head.activeWorldRevision > MaximumSafeInteger || !CityForgeIds.IsHash(head.catalogHash)
                || !CityForgeIds.IsHash(head.chunkMapRoot) || !IsSeed(head.worldSeed)
                || head.blockSizeCells != SlopCityCityForge.GridSize || head.cellSizeMeters != SlopCityCityForge.CellSizeMeters)
                throw new InvalidOperationException("The World Head is not compatible with City Forge.");
            return "{\"catalogHash\":" + Quote(head.catalogHash) + ",\"chunkMapRoot\":" + Quote(head.chunkMapRoot)
                + ",\"generatorVersion\":" + SlopCityCityForge.GeneratorVersion.ToString(CultureInfo.InvariantCulture) + ",\"grid\":{\"cellMeters\":8,\"chunkCells\":9},\"schemaVersion\":2,\"worldRevision\":"
                + head.activeWorldRevision.ToString(CultureInfo.InvariantCulture) + ",\"worldSeed\":" + Quote(head.worldSeed) + "}";
        }

        public static bool TryLoadChunkPlanJson(string json, out CityForgeChunkPlan plan, out string error)
        {
            plan = null;
            if (Encoding.UTF8.GetByteCount(json ?? string.Empty) > SlopCityCityForge.MaximumPackedChunkBytes) { error = "The City Forge chunk exceeds its bounded size."; return false; }
            if (!ParseObject(json, out Dictionary<string, object> raw, out error)) return false;
            return ReadPlan(raw, "$", out plan, out error);
        }

        /// <summary>Returns canonicalChunkPlan() bytes shared with the web/service contract.</summary>
        public static string ToWireJson(CityForgeChunkPlan plan, bool includeRevision = false)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            string canonical = SlopCityCityForgeCanonical.Pack(plan);
            if (!TryLoadChunkPlanJson(canonical, out CityForgeChunkPlan checkedPlan, out string error)) throw new InvalidOperationException(error);
            canonical = SlopCityCityForgeCanonical.Pack(checkedPlan);
            if (!includeRevision) return canonical;
            if (plan.revision < 1 || plan.revision > MaximumSafeInteger) throw new InvalidOperationException("The City Forge chunk revision is invalid.");
            int marker = canonical.IndexOf(",\"roads\":", StringComparison.Ordinal);
            if (marker < 0) throw new InvalidOperationException("The City Forge canonical plan is malformed.");
            return canonical.Substring(0, marker) + ",\"revision\":" + plan.revision.ToString(CultureInfo.InvariantCulture) + canonical.Substring(marker);
        }

        /// <summary>Validate a full sparse index against its World Head hash.</summary>
        public static bool TryVerifyChunkMapRoot(CityForgeWorldHead head, IEnumerable<CityForgeChunkHead> entries, out string error)
        {
            error = null;
            if (head == null) { error = "The City Forge World Head is missing."; return false; }
            try
            {
                if (!string.Equals(head.chunkMapRoot, SlopCityCityForgeCanonical.ChunkMapRoot(entries), StringComparison.Ordinal)) { error = "The sparse chunk index does not match its World Head."; return false; }
                return true;
            }
            catch (Exception) { error = "The sparse chunk index is invalid."; return false; }
        }

        private static bool ReadWorldHead(Dictionary<string, object> raw, string path, out CityForgeWorldHead head, out string error)
        {
            head = null;
            string[] fields = { "schemaVersion", "catalogHash", "worldSeed", "generatorVersion", "worldRevision", "chunkMapRoot", "grid" };
            if (!Exact(raw, fields, Array.Empty<string>(), path, out error)
                || !Integer(raw, "schemaVersion", 2, 2, path + ".schemaVersion", out long schema, out error)
                || !String(raw, "catalogHash", path + ".catalogHash", out string catalog, out error)
                || !String(raw, "worldSeed", path + ".worldSeed", out string seed, out error)
                || !Integer(raw, "generatorVersion", SlopCityCityForge.GeneratorVersion, SlopCityCityForge.GeneratorVersion, path + ".generatorVersion", out long generator, out error)
                || !Integer(raw, "worldRevision", 1, MaximumSafeInteger, path + ".worldRevision", out long revision, out error)
                || !String(raw, "chunkMapRoot", path + ".chunkMapRoot", out string root, out error)
                || !Object(raw, "grid", path + ".grid", out Dictionary<string, object> grid, out error)
                || !Exact(grid, new[] { "chunkCells", "cellMeters" }, Array.Empty<string>(), path + ".grid", out error)
                || !Integer(grid, "chunkCells", 9, 9, path + ".grid.chunkCells", out long cells, out error)
                || !Integer(grid, "cellMeters", 8, 8, path + ".grid.cellMeters", out long meters, out error)) return false;
            if (!CityForgeIds.IsHash(catalog) || !CityForgeIds.IsHash(root) || !IsSeed(seed)) { error = path + " contains an invalid catalog, seed, or map root."; return false; }
            head = new CityForgeWorldHead { schemaVersion = (int)schema, catalogHash = catalog, worldSeed = seed, generatorVersion = (int)generator, activeWorldRevision = revision, chunkMapRoot = root, blockSizeCells = (int)cells, cellSizeMeters = (int)meters, changedChunks = new List<CityForgeChunkHead>(), indexIsComplete = false };
            return true;
        }

        private static bool ReadPlan(Dictionary<string, object> raw, string path, out CityForgeChunkPlan plan, out string error)
        {
            plan = null;
            string[] required = { "coord", "baseChunkHash", "parentChunkRevision", "roads", "removedRoadEdges", "parcels", "buildings", "services", "screens", "landmarks", "situations", "demolishedIds" };
            if (!Exact(raw, required, new[] { "revision" }, path, out error)
                || !Coord(raw, "coord", path + ".coord", out CityForgeCoord coord, out error)
                || !String(raw, "baseChunkHash", path + ".baseChunkHash", out string baseHash, out error)
                || !Integer(raw, "parentChunkRevision", 0, MaximumSafeInteger, path + ".parentChunkRevision", out long parent, out error)) return false;
            if (SlopCityCityForge.IsProtectedOldCity(coord) || !CityForgeIds.IsHash(baseHash)) { error = path + " has a protected coordinate or invalid base hash."; return false; }
            long revision = 0;
            if (raw.ContainsKey("revision") && !Integer(raw, "revision", 1, MaximumSafeInteger, path + ".revision", out revision, out error)) return false;
            if (!ReadArray(raw, "roads", path + ".roads", SlopCityCityForge.MaximumRoadRecords, out List<object> rawRoads, out error)
                || !ReadArray(raw, "removedRoadEdges", path + ".removedRoadEdges", SlopCityCityForge.MaximumRoadRecords, out List<object> removed, out error)
                || !ReadArray(raw, "parcels", path + ".parcels", SlopCityCityForge.MaximumParcelRecords, out List<object> rawParcels, out error)
                || !ReadArray(raw, "buildings", path + ".buildings", SlopCityCityForge.MaximumBuildingRecords, out List<object> rawBuildings, out error)
                || !ReadArray(raw, "services", path + ".services", SlopCityCityForge.MaximumServiceRecords, out List<object> rawServices, out error)
                || !ReadArray(raw, "screens", path + ".screens", SlopCityCityForge.MaximumScreenRecords, out List<object> rawScreens, out error)
                || !ReadArray(raw, "landmarks", path + ".landmarks", SlopCityCityForge.MaximumLandmarkRecords, out List<object> rawLandmarks, out error)
                || !ReadArray(raw, "situations", path + ".situations", 96, out List<object> rawSituations, out error)
                || !ReadArray(raw, "demolishedIds", path + ".demolishedIds", SlopCityCityForge.MaximumBuildingRecords + SlopCityCityForge.MaximumServiceRecords, out List<object> demolished, out error)) return false;

            if (!ReadRoads(rawRoads, path + ".roads", out List<CityForgeRoad> roads, out HashSet<string> roadEdges, out error)
                || !ReadRemovedRoads(removed, roadEdges, path + ".removedRoadEdges", out List<string> removedEdges, out error)
                || !ReadParcels(rawParcels, path + ".parcels", out List<CityForgeParcel> parcels, out Dictionary<string, CityForgeParcel> parcelById, out error)
                || !ReadBuildings(rawBuildings, parcelById, path + ".buildings", out List<CityForgeBuilding> buildings, out Dictionary<string, CityForgeBuilding> buildingById, out error)
                || !ReadServices(rawServices, parcelById, path + ".services", out List<CityForgeService> services, out error)
                || !ReadScreens(rawScreens, buildings, parcels, roads, path + ".screens", out List<CityForgeScreen> screens, out error)
                || !ReadLandmarks(rawLandmarks, parcelById, path + ".landmarks", out List<CityForgeLandmark> landmarks, out error)
                || !ReadSituations(rawSituations, landmarks, path + ".situations", out List<CityForgeSituationState> situations, out error)
                || !ReadDemolitions(demolished, path + ".demolishedIds", out List<string> demolishedIds, out error)) return false;

            plan = new CityForgeChunkPlan { schemaVersion = SlopCityCityForge.ChunkSchemaVersion, coord = coord, baseChunkHash = baseHash, parentChunkRevision = parent, revision = revision, roads = roads, removedRoadEdges = removedEdges, parcels = parcels, buildings = buildings, services = services, screens = screens, landmarks = landmarks, situations = situations, demolishedIds = demolishedIds };
            string canonical = SlopCityCityForgeCanonical.Pack(plan);
            if (Encoding.UTF8.GetByteCount(canonical) > SlopCityCityForge.MaximumPackedChunkBytes) { plan = null; error = path + " exceeds the City Forge chunk bound."; return false; }
            plan.packedBlobHash = SlopCityCityForge.Sha256(canonical);
            return true;
        }

        private static bool ReadRoads(List<object> raw, string path, out List<CityForgeRoad> roads, out HashSet<string> edges, out string error)
        {
            roads = new List<CityForgeRoad>(); edges = new HashSet<string>(StringComparer.Ordinal); error = null;
            for (int index = 0; index < raw.Count; index++)
            {
                string itemPath = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (!(raw[index] is Dictionary<string, object> row) || !Exact(row, new[] { "edge", "class" }, Array.Empty<string>(), itemPath, out error)
                    || !String(row, "edge", itemPath + ".edge", out string edge, out error) || !String(row, "class", itemPath + ".class", out string roadClass, out error)
                    || !ParseEdge(edge, out int x, out int z, out CityForgeDirection direction) || !string.Equals(edge, CanonicalRoadEdge(edge), StringComparison.Ordinal) || !RoadClass(roadClass, out CityForgeRoadClass typed) || !edges.Add(edge)) { error = itemPath + " is not an approved unique road."; return false; }
                roads.Add(new CityForgeRoad { cellX = x, cellZ = z, direction = direction, roadClass = typed });
            }
            return true;
        }

        private static bool ReadRemovedRoads(List<object> raw, HashSet<string> roads, string path, out List<string> values, out string error)
        {
            values = new List<string>(); error = null; HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < raw.Count; index++)
            {
                if (!(raw[index] is string edge) || !ParseEdge(edge, out int unusedX, out int unusedZ, out CityForgeDirection unusedDirection)
                    || !string.Equals(edge, CanonicalRoadEdge(edge), StringComparison.Ordinal) || roads.Contains(edge) || !seen.Add(edge))
                { error = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "] is not a canonical removed road edge."; return false; }
                values.Add(edge);
            }
            return true;
        }

        private static bool ReadParcels(List<object> raw, string path, out List<CityForgeParcel> parcels, out Dictionary<string, CityForgeParcel> byId, out string error)
        {
            parcels = new List<CityForgeParcel>(); byId = new Dictionary<string, CityForgeParcel>(StringComparer.Ordinal); error = null; HashSet<int> occupied = new HashSet<int>();
            for (int index = 0; index < raw.Count; index++)
            {
                string itemPath = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (!(raw[index] is Dictionary<string, object> row) || !Exact(row, new[] { "id", "cells", "zone" }, Array.Empty<string>(), itemPath, out error)
                    || !String(row, "id", itemPath + ".id", out string id, out error) || !String(row, "zone", itemPath + ".zone", out string zone, out error)
                    || !CityForgeIds.IsId(id) || byId.ContainsKey(id) || !Zone(zone, out CityForgeZone typed)
                    || !ReadArray(row, "cells", itemPath + ".cells", 81, out List<object> cells, out error) || cells.Count == 0) { error = itemPath + " is not an approved parcel."; return false; }
                CityForgeParcel parcel = new CityForgeParcel { id = id, zone = typed, cells = new List<int>(), entranceEdge = CityForgeDirection.North };
                foreach (object rawCell in cells)
                {
                    if (!(rawCell is string cell) || !ParseCell(cell, out int x, out int z)) { error = itemPath + ".cells must use local 0–8 grid cells."; return false; }
                    int number = x + z * SlopCityCityForge.GridSize;
                    if (!occupied.Add(number) || parcel.cells.Contains(number)) { error = itemPath + ".cells overlap another parcel."; return false; }
                    parcel.cells.Add(number);
                }
                byId.Add(id, parcel); parcels.Add(parcel);
            }
            return true;
        }

        private static bool ReadBuildings(List<object> raw, Dictionary<string, CityForgeParcel> parcels, string path, out List<CityForgeBuilding> buildings, out Dictionary<string, CityForgeBuilding> byId, out string error)
        {
            buildings = new List<CityForgeBuilding>(); byId = new Dictionary<string, CityForgeBuilding>(StringComparer.Ordinal); error = null; HashSet<string> occupiedParcels = new HashSet<string>(StringComparer.Ordinal);
            SlopCityCityForgeCatalog catalog = SlopCityCityForgeCatalog.Default;
            for (int index = 0; index < raw.Count; index++)
            {
                string itemPath = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                string[] required = { "id", "parcelId", "blueprintId", "facadeId", "roofId", "floors", "entranceEdge" };
                if (!(raw[index] is Dictionary<string, object> row) || !Exact(row, required, new[] { "interiorRole", "storefrontId" }, itemPath, out error)
                    || !String(row, "id", itemPath + ".id", out string id, out error) || !String(row, "parcelId", itemPath + ".parcelId", out string parcelId, out error)
                    || !String(row, "blueprintId", itemPath + ".blueprintId", out string blueprint, out error) || !String(row, "facadeId", itemPath + ".facadeId", out string facade, out error)
                    || !String(row, "roofId", itemPath + ".roofId", out string roof, out error) || !Integer(row, "floors", 1, 24, itemPath + ".floors", out long floors, out error)
                    || !String(row, "entranceEdge", itemPath + ".entranceEdge", out string entrance, out error) || !CityForgeIds.IsId(id) || byId.ContainsKey(id)
                    || !parcels.ContainsKey(parcelId) || !occupiedParcels.Add(parcelId) || !catalog.TryGetBuilding(blueprint, out CityForgeBuildingArchetype unused) || !catalog.IsBuildingAllowedInZone(blueprint, parcels[parcelId].zone) || !catalog.IsFacade(facade) || !catalog.IsRoof(roof) || !Direction(entrance, out CityForgeDirection typedEntrance))
                { error = itemPath + " is not an approved building."; return false; }
                string interior = string.Empty;
                if (row.ContainsKey("interiorRole") && (!String(row, "interiorRole", itemPath + ".interiorRole", out interior, out error) || !catalog.IsInterior(interior))) return false;
                string storefront = string.Empty;
                if (row.ContainsKey("storefrontId") && (!String(row, "storefrontId", itemPath + ".storefrontId", out storefront, out error) || !catalog.IsStorefront(storefront))) return false;
                CityForgeBuilding building = new CityForgeBuilding { id = id, parcelId = parcelId, blueprintId = blueprint, facadeId = facade, roofId = roof, floors = (int)floors, entranceEdge = typedEntrance, interiorRole = interior, storefrontId = storefront };
                byId.Add(id, building); buildings.Add(building);
                // Parcel entrance is runtime-only and is never serialized. The
                // building owns the shared wire direction, so use it solely as a
                // fallback for presentation/collision helpers.
                parcels[parcelId].entranceEdge = typedEntrance;
            }
            return true;
        }

        private static bool ReadServices(List<object> raw, Dictionary<string, CityForgeParcel> parcels, string path, out List<CityForgeService> services, out string error)
        {
            services = new List<CityForgeService>(); error = null; HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < raw.Count; index++)
            {
                string itemPath = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (!(raw[index] is Dictionary<string, object> row) || !Exact(row, new[] { "id", "type", "parcelId" }, Array.Empty<string>(), itemPath, out error)
                    || !String(row, "id", itemPath + ".id", out string id, out error) || !String(row, "type", itemPath + ".type", out string type, out error)
                    || !String(row, "parcelId", itemPath + ".parcelId", out string parcelId, out error) || !CityForgeIds.IsId(id) || !ids.Add(id)
                    || !parcels.ContainsKey(parcelId) || !SlopCityCityForgeCatalog.Default.IsService(type)) { error = itemPath + " is not an approved service."; return false; }
                services.Add(new CityForgeService { id = id, type = type, parcelId = parcelId });
            }
            return true;
        }

        private static bool ReadScreens(List<object> raw, List<CityForgeBuilding> buildings, List<CityForgeParcel> parcels, List<CityForgeRoad> roads, string path, out List<CityForgeScreen> screens, out string error)
        {
            screens = new List<CityForgeScreen>(); error = null; HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, int> capacities = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < raw.Count; index++)
            {
                string itemPath = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (!(raw[index] is Dictionary<string, object> row) || !Exact(row, new[] { "screenId", "hostId", "screenMountId", "deviceId" }, Array.Empty<string>(), itemPath, out error)
                    || !String(row, "screenId", itemPath + ".screenId", out string id, out error) || !String(row, "hostId", itemPath + ".hostId", out string host, out error)
                    || !String(row, "screenMountId", itemPath + ".screenMountId", out string mount, out error) || !String(row, "deviceId", itemPath + ".deviceId", out string device, out error)
                    || !CityForgeIds.IsId(id) || !ids.Add(id) || !SlopCityCityForgeCatalog.Default.TryGetMount(mount, out CityForgeScreenMount unused) || !SlopCityCityForgeCatalog.Default.IsScreenDevice(device)
                    || !TryScreenHostCapacity(host, mount, buildings, parcels, roads, out int capacity)) { error = itemPath + " is not an approved physical screen fixture."; return false; }
                screens.Add(new CityForgeScreen { screenId = id, hostId = host, screenMountId = mount, deviceId = device });
                capacities[host] = capacity;
                paths[id] = itemPath;
            }
            // The shared contract assigns a derived physical slot by ordinal
            // screen ID across a host (not separately for each mount). No raw
            // slot or transform is accepted on the wire.
            foreach (IGrouping<string, CityForgeScreen> group in screens.GroupBy(item => item.hostId ?? string.Empty, StringComparer.Ordinal))
            {
                List<CityForgeScreen> ordered = group.OrderBy(item => item.screenId, StringComparer.Ordinal).ToList();
                int capacity = capacities[group.Key];
                if (ordered.Count > capacity)
                {
                    error = paths[ordered[capacity].screenId] + ".screenId exceeds this shipped host's " + capacity.ToString(CultureInfo.InvariantCulture) + " physical screen slots.";
                    return false;
                }
            }
            return true;
        }

        private static bool ReadLandmarks(List<object> raw, Dictionary<string, CityForgeParcel> parcels, string path, out List<CityForgeLandmark> landmarks, out string error)
        {
            landmarks = new List<CityForgeLandmark>(); error = null; HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal), occupiedParcels = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < raw.Count; index++)
            {
                string itemPath = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (!(raw[index] is Dictionary<string, object> row) || !Exact(row, new[] { "id", "classId", "outcomeId", "parcelId", "state" }, new[] { "stableSeed" }, itemPath, out error)
                    || !String(row, "id", itemPath + ".id", out string id, out error) || !String(row, "classId", itemPath + ".classId", out string classId, out error)
                    || !String(row, "outcomeId", itemPath + ".outcomeId", out string outcome, out error) || !String(row, "parcelId", itemPath + ".parcelId", out string parcelId, out error)
                    || !String(row, "state", itemPath + ".state", out string state, out error) || !CityForgeIds.IsId(id) || !ids.Add(id) || !parcels.ContainsKey(parcelId) || !occupiedParcels.Add(parcelId)
                    || !SlopCityCityForgeCatalog.Default.TryGetLandmark(classId, out CityForgeLandmarkClass type) || type.outcomes == null || Array.IndexOf(type.outcomes, outcome) < 0 || !LandmarkState(state)) { error = itemPath + " is not an approved Cityworks record."; return false; }
                string seed = string.Empty;
                if (row.ContainsKey("stableSeed") && (!String(row, "stableSeed", itemPath + ".stableSeed", out seed, out error) || !IsSeed(seed))) return false;
                landmarks.Add(new CityForgeLandmark { id = id, classId = classId, outcomeId = outcome, parcelId = parcelId, state = state, stableSeed = seed });
            }
            return true;
        }

        private static bool ReadSituations(List<object> raw, List<CityForgeLandmark> landmarks, string path, out List<CityForgeSituationState> situations, out string error)
        {
            situations = new List<CityForgeSituationState>(); error = null; HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal), pairs = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> landmarkIds = new HashSet<string>((landmarks ?? new List<CityForgeLandmark>()).Where(item => item != null).Select(item => item.id), StringComparer.Ordinal);
            for (int index = 0; index < raw.Count; index++)
            {
                string itemPath = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (!(raw[index] is Dictionary<string, object> row) || !Exact(row, new[] { "id", "situationId", "landmarkId", "state", "stableSeed" }, Array.Empty<string>(), itemPath, out error)
                    || !String(row, "id", itemPath + ".id", out string id, out error) || !String(row, "situationId", itemPath + ".situationId", out string situation, out error)
                    || !String(row, "landmarkId", itemPath + ".landmarkId", out string landmark, out error) || !String(row, "state", itemPath + ".state", out string state, out error)
                    || !String(row, "stableSeed", itemPath + ".stableSeed", out string seed, out error) || !CityForgeIds.IsId(id) || !ids.Add(id)
                    || !SlopCityCityForgeCatalog.Default.IsSituation(situation) || !landmarkIds.Contains(landmark) || !SituationState(state) || !IsSeed(seed) || !pairs.Add(landmark + "\u0000" + situation)) { error = itemPath + " is not an approved CitySituation."; return false; }
                CityForgeSituationState parsed = new CityForgeSituationState { id = id, situationId = situation, landmarkId = landmark, state = state, stableSeed = seed };
                // A singleton hosted page cannot prove remote cross-site
                // prerequisites. Keep strict local anchor grammar here; the
                // controller/server accepted-world index checks outcomes once
                // the bounded authoritative neighborhood is available.
                if (!SlopCityCityForgeCatalog.Default.SituationAnchorMatches(parsed, landmarks)) { error = itemPath + ".situationId must use its authored landmark outcome."; return false; }
                situations.Add(parsed);
            }
            return true;
        }

        private static bool ReadDemolitions(List<object> raw, string path, out List<string> values, out string error)
        {
            values = new List<string>(); error = null; HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < raw.Count; index++)
            {
                if (!(raw[index] is string id) || !CityForgeIds.IsId(id) || !seen.Add(id)) { error = path + "[" + index.ToString(CultureInfo.InvariantCulture) + "] is not an approved unique structure identity."; return false; }
                values.Add(id);
            }
            return true;
        }

        private static bool TryScreenHostCapacity(string host, string mount, IEnumerable<CityForgeBuilding> buildings, IEnumerable<CityForgeParcel> parcels, IEnumerable<CityForgeRoad> roads, out int capacity)
        {
            capacity = 0;
            SlopCityCityForgeCatalog catalog = SlopCityCityForgeCatalog.Default;
            CityForgeBuilding building = (buildings ?? Enumerable.Empty<CityForgeBuilding>()).FirstOrDefault(item => item != null && item.id == host);
            if (building != null)
            {
                if (!catalog.IsBuildingScreenMount(building.blueprintId, mount)) return false;
                capacity = catalog.GetBuildingScreenSlots(building.blueprintId);
                return capacity > 0;
            }
            if (mount == "plaza-civic-support" && host != null && host.StartsWith("site-", StringComparison.Ordinal))
            {
                string id = host.Substring(5);
                CityForgeParcel parcel = (parcels ?? Enumerable.Empty<CityForgeParcel>()).FirstOrDefault(item => item != null && item.id == id);
                if (!CityForgeIds.IsId(id) || parcel == null) return false;
                capacity = catalog.GetSiteScreenSlots(parcel.zone);
                return capacity > 0;
            }
            if (mount == "street-kiosk-screen" && TryRoadHost(host, out string edge))
            {
                CityForgeRoad road = (roads ?? Enumerable.Empty<CityForgeRoad>()).FirstOrDefault(item => item != null && Edge(item) == edge);
                if (road == null) return false;
                capacity = catalog.GetRoadScreenSlots(road.roadClass);
                return capacity > 0;
            }
            return false;
        }

        private static bool TryRoadHost(string value, out string edge)
        {
            edge = null;
            if (string.IsNullOrEmpty(value) || !value.StartsWith("road-", StringComparison.Ordinal)) return false;
            string[] parts = value.Substring(5).Split('-');
            if (parts.Length != 3 || parts[0].Length != 1 || parts[1].Length != 1 || parts[0][0] < '0' || parts[0][0] > '8' || parts[1][0] < '0' || parts[1][0] > '8' || !Direction(parts[2], out CityForgeDirection direction)) return false;
            edge = parts[0] + "," + parts[1] + ":" + DirectionName(direction); return true;
        }

        private static bool RoadClass(string value, out CityForgeRoadClass result)
        {
            result = CityForgeRoadClass.LocalStreet;
            if (value == "local-street") return true;
            if (value == "boulevard") { result = CityForgeRoadClass.Boulevard; return true; }
            if (value == "service-alley") { result = CityForgeRoadClass.ServiceAlley; return true; }
            if (value == "pedestrian-route") { result = CityForgeRoadClass.PedestrianRoute; return true; }
            if (value == "transit-service-corridor") { result = CityForgeRoadClass.TransitServiceCorridor; return true; }
            return false;
        }
        private static bool Zone(string value, out CityForgeZone result)
        {
            result = CityForgeZone.Home;
            if (value == "home") return true;
            if (value == "work-service") { result = CityForgeZone.WorkService; return true; }
            if (value == "trade") { result = CityForgeZone.Trade; return true; }
            if (value == "civic") { result = CityForgeZone.Civic; return true; }
            if (value == "culture") { result = CityForgeZone.Culture; return true; }
            if (value == "green-infrastructure") { result = CityForgeZone.GreenInfrastructure; return true; }
            return false;
        }
        private static bool Direction(string value, out CityForgeDirection result)
        {
            result = CityForgeDirection.North;
            if (value == "north") return true;
            if (value == "east") { result = CityForgeDirection.East; return true; }
            if (value == "south") { result = CityForgeDirection.South; return true; }
            if (value == "west") { result = CityForgeDirection.West; return true; }
            return false;
        }
        private static string DirectionName(CityForgeDirection value) { return value == CityForgeDirection.North ? "north" : value == CityForgeDirection.East ? "east" : value == CityForgeDirection.South ? "south" : "west"; }
        private static bool LandmarkState(string value) { return value == "unavailable" || value == "discovered" || value == "diagnosing" || value == "chosen" || value == "active" || value == "opening" || value == "established" || value == "paused" || value == "renovating"; }
        private static bool SituationState(string value) { return value == "unavailable" || value == "available" || value == "active" || value == "resolved" || value == "paused" || value == "archived"; }
        private static bool IsSeed(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 16 || value.Length > 128) return false;
            for (int index = 0; index < value.Length; index++) if (!((value[index] >= 'a' && value[index] <= 'f') || (value[index] >= '0' && value[index] <= '9'))) return false;
            return true;
        }
        private static bool RuntimeId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64 || !((value[0] >= 'a' && value[0] <= 'z') || (value[0] >= '0' && value[0] <= '9'))) return false;
            for (int index = 1; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') || character == '_' || character == '-')) return false;
            }
            return true;
        }

        private static bool ParseCell(string value, out int x, out int z)
        {
            x = z = 0;
            if (string.IsNullOrEmpty(value) || value.Length != 3 || value[1] != ',' || value[0] < '0' || value[0] > '8' || value[2] < '0' || value[2] > '8') return false;
            x = value[0] - '0'; z = value[2] - '0'; return true;
        }
        private static bool ParseEdge(string value, out int x, out int z, out CityForgeDirection direction)
        {
            x = z = 0; direction = CityForgeDirection.North;
            if (string.IsNullOrEmpty(value) || value.Length < 9 || value[1] != ',' || value[3] != ':' || value[0] < '0' || value[0] > '8' || value[2] < '0' || value[2] > '8' || !Direction(value.Substring(4), out direction)) return false;
            x = value[0] - '0'; z = value[2] - '0'; return true;
        }
        private static string Edge(CityForgeRoad value) { return value.cellX.ToString(CultureInfo.InvariantCulture) + "," + value.cellZ.ToString(CultureInfo.InvariantCulture) + ":" + DirectionName(value.direction); }
        private static string CanonicalRoadEdge(string value)
        {
            if (!ParseEdge(value, out int x, out int z, out CityForgeDirection direction)) return string.Empty;
            if (direction == CityForgeDirection.West && x > 0) { x--; direction = CityForgeDirection.East; }
            if (direction == CityForgeDirection.South && z > 0) { z--; direction = CityForgeDirection.North; }
            return x.ToString(CultureInfo.InvariantCulture) + "," + z.ToString(CultureInfo.InvariantCulture) + ":" + DirectionName(direction);
        }
        private static string Quote(string value)
        {
            StringBuilder builder = new StringBuilder("\"");
            foreach (char character in value ?? string.Empty)
            {
                if (character == '\\') builder.Append("\\\\"); else if (character == '\"') builder.Append("\\\""); else if (character == '\n') builder.Append("\\n"); else if (character == '\r') builder.Append("\\r"); else if (character == '\t') builder.Append("\\t");
                else if (character < 32) builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture)); else builder.Append(character);
            }
            return builder.Append('\"').ToString();
        }

        private static bool ParseObject(string json, out Dictionary<string, object> value, out string error)
        {
            value = null;
            if (!CityForgeJson.TryParse(json, out object parsed, out error)) return false;
            value = parsed as Dictionary<string, object>;
            if (value != null) return true;
            error = "City Forge JSON must be an object."; return false;
        }
        private static bool Exact(Dictionary<string, object> value, string[] required, string[] optional, string path, out string error)
        {
            error = null;
            if (value == null) { error = path + " must be an object."; return false; }
            foreach (string key in required) if (!value.ContainsKey(key)) { error = path + "." + key + " is required."; return false; }
            foreach (string key in value.Keys) if (!required.Contains(key) && !optional.Contains(key)) { error = path + "." + key + " is not allowed."; return false; }
            return true;
        }
        private static bool Object(Dictionary<string, object> value, string key, string path, out Dictionary<string, object> result, out string error)
        {
            result = null; error = null;
            if (!value.TryGetValue(key, out object raw) || !(raw is Dictionary<string, object> objectValue)) { error = path + " must be an object."; return false; }
            result = objectValue; return true;
        }
        private static bool ReadArray(Dictionary<string, object> value, string key, string path, int maximum, out List<object> result, out string error)
        {
            result = null; error = null;
            if (!value.TryGetValue(key, out object raw) || !(raw is List<object> array) || array.Count > maximum) { error = path + " must be a bounded array."; return false; }
            result = array; return true;
        }
        private static bool String(Dictionary<string, object> value, string key, string path, out string result, out string error)
        {
            result = null; error = null;
            if (!value.TryGetValue(key, out object raw) || !(raw is string text)) { error = path + " must be a string."; return false; }
            result = text; return true;
        }
        private static bool StringOrNull(Dictionary<string, object> value, string key, string path, out string result, out string error)
        {
            result = null; error = null;
            if (!value.TryGetValue(key, out object raw)) { error = path + " is required."; return false; }
            if (raw == null) return true;
            if (!(raw is string text)) { error = path + " must be a string or null."; return false; }
            result = text; return true;
        }
        private static bool Integer(Dictionary<string, object> value, string key, long minimum, long maximum, string path, out long result, out string error)
        {
            result = 0; error = null;
            if (!value.TryGetValue(key, out object raw)) { error = path + " is required."; return false; }
            if (!(raw is CityForgeJsonNumber number) || string.IsNullOrEmpty(number.text) || number.text.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0 || !long.TryParse(number.text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result) || result < minimum || result > maximum) { error = path + " must be a bounded integer."; return false; }
            return true;
        }
        private static bool Coord(Dictionary<string, object> value, string key, string path, out CityForgeCoord result, out string error)
        {
            result = new CityForgeCoord(); error = null;
            if (!Object(value, key, path, out Dictionary<string, object> coordinate, out error) || !Exact(coordinate, new[] { "x", "z" }, Array.Empty<string>(), path, out error)
                || !Integer(coordinate, "x", SlopCityCityForge.MinimumChunkCoordinate, SlopCityCityForge.MaximumChunkCoordinate, path + ".x", out long x, out error)
                || !Integer(coordinate, "z", SlopCityCityForge.MinimumChunkCoordinate, SlopCityCityForge.MaximumChunkCoordinate, path + ".z", out long z, out error)) return false;
            result = new CityForgeCoord((int)x, (int)z); return true;
        }

        /// <summary>
        /// Parses the bounded hosted-page DTO used by the private bridge without
        /// taking any dependency on tenant/authentication code. It accepts the
        /// canonical stored `plan` shape and the API's friendly `delta` shape,
        /// then verifies every returned base/snapshot hash before exposing plans.
        /// </summary>
        public static bool TryLoadApiPageJson(string json, out CityForgeApiPage page, out string error)
        {
            return TryLoadApiPageJson(json, SlopCityCityForgeCatalog.Default, out page, out error);
        }

        public static bool TryLoadApiPageJson(string json, SlopCityCityForgeCatalog catalog, out CityForgeApiPage page, out string error)
        {
            page = null;
            if (Encoding.UTF8.GetByteCount(json ?? string.Empty) > MaximumApiPageBytes) { error = "The City Forge page exceeds its transport bound."; return false; }
            if (!ParseObject(json, out Dictionary<string, object> root, out error) || !root.ContainsKey("worldHead")) return false;
            if (root["worldHead"] == null)
            {
                if (!Exact(root, new[] { "worldHead", "canEdit" }, Array.Empty<string>(), "$", out error) || !(root["canEdit"] is bool emptyCanEdit)) { error = "The empty City Forge status is invalid."; return false; }
                page = new CityForgeApiPage { worldHead = null, canEdit = emptyCanEdit, radius = 0, chunks = new List<CityForgeApiPageChunk>() };
                return true;
            }
            if (!Exact(root, new[] { "worldHead", "page" }, new[] { "canEdit" }, "$", out error)
                || !Object(root, "worldHead", "$.worldHead", out Dictionary<string, object> rawHead, out error)
                || !Object(root, "page", "$.page", out Dictionary<string, object> rawPage, out error)) return false;
            if (!ReadApiHead(rawHead, out CityForgeWorldHead head, out string revisionId, out error)
                || !Exact(rawPage, new[] { "centre", "radius", "chunks" }, Array.Empty<string>(), "$.page", out error)
                || !Coord(rawPage, "centre", "$.page.centre", out CityForgeCoord centre, out error)
                || !Integer(rawPage, "radius", 0, MaximumPageRadius, "$.page.radius", out long radiusLong, out error)
                || !ReadArray(rawPage, "chunks", "$.page.chunks", (int)((radiusLong * 2 + 1) * (radiusLong * 2 + 1)), out List<object> chunks, out error)) return false;
            if (chunks.Count != 1) { error = "A hosted City Forge page must contain exactly one chunk."; return false; }
            bool canEdit = false;
            if (root.ContainsKey("canEdit"))
            {
                if (!(root["canEdit"] is bool value)) { error = "$.canEdit must be a boolean."; return false; }
                canEdit = value;
            }
            SlopCityCityForgeGenerator generator;
            try { generator = new SlopCityCityForgeGenerator(catalog ?? SlopCityCityForgeCatalog.Default); SlopCityCityForgeGenerator.EnsureHeadCompatible(head, catalog ?? SlopCityCityForgeCatalog.Default); }
            catch { error = "The hosted City Forge page needs a compatible catalog."; return false; }
            int radius = (int)radiusLong;
            CityForgeApiPage parsed = new CityForgeApiPage { worldHead = head, worldRevisionId = revisionId, centre = centre, radius = radius, canEdit = canEdit };
            HashSet<CityForgeCoord> seen = new HashSet<CityForgeCoord>();
            for (int index = 0; index < chunks.Count; index++)
            {
                if (!(chunks[index] is Dictionary<string, object> row) || !ReadApiChunk(row, "$.page.chunks[" + index.ToString(CultureInfo.InvariantCulture) + "]", out CityForgeApiPageChunk chunk, out error)) return false;
                if (!seen.Add(chunk.coord) || Math.Abs(chunk.coord.x - centre.x) > radius || Math.Abs(chunk.coord.z - centre.z) > radius || SlopCityCityForge.IsProtectedOldCity(chunk.coord)) { error = "The hosted City Forge page has an out-of-bounds chunk."; return false; }
                CityForgeBaseChunk baseChunk;
                try { baseChunk = generator.Generate(head, chunk.coord); }
                catch { error = "A hosted City Forge chunk cannot be generated."; return false; }
                if (!string.Equals(baseChunk.baseHash, chunk.baseChunkHash, StringComparison.Ordinal)) { error = "A hosted City Forge chunk has a different base hash."; return false; }
                if (chunk.plan != null)
                {
                    if (!chunk.plan.coord.Equals(chunk.coord) || !string.Equals(chunk.plan.baseChunkHash, chunk.baseChunkHash, StringComparison.Ordinal)
                        || !string.Equals(SlopCityCityForgeCanonical.Hash(chunk.plan), chunk.snapshotSha256, StringComparison.Ordinal)) { error = "A hosted City Forge chunk has a different snapshot hash."; return false; }
                    chunk.plan.revision = chunk.version; chunk.plan.revisionId = chunk.revisionId;
                }
                parsed.chunks.Add(chunk);
            }
            page = parsed;
            return true;
        }

        /// <summary>
        /// Parses a bounded, topology-free strategic-map window. The caller
        /// supplies the already verified current World Head so a stale map page
        /// cannot be shown as if it represented a newer sparse world. Unlike a
        /// ChunkPlan page, this carries only approved record counts and revision
        /// fingerprints; it never triggers physical stream changes.
        /// </summary>
        public static bool TryLoadStrategicMapJson(string json, CityForgeWorldHead expectedHead, out CityForgeStrategicMap map, out string error)
        {
            map = null;
            if (Encoding.UTF8.GetByteCount(json ?? string.Empty) > MaximumApiPageBytes) { error = "The City Forge strategic map exceeds its transport bound."; return false; }
            if (expectedHead == null) { error = "The expected City Forge World Head is missing."; return false; }
            if (!ParseObject(json, out Dictionary<string, object> root, out error)
                || !Exact(root, new[] { "worldRevision", "chunkMapRoot", "centre", "radius", "chunks" }, Array.Empty<string>(), "$", out error)
                || !Integer(root, "worldRevision", 1, MaximumSafeInteger, "$.worldRevision", out long revision, out error)
                || !String(root, "chunkMapRoot", "$.chunkMapRoot", out string mapRoot, out error)
                || !Coord(root, "centre", "$.centre", out CityForgeCoord centre, out error)
                || !Integer(root, "radius", 0, 6, "$.radius", out long radius, out error)
                || !ReadArray(root, "chunks", "$.chunks", SlopCityCityForge.MaximumStrategicSummaryRecords, out List<object> rows, out error)) return false;
            if (!CityForgeIds.IsHash(mapRoot) || !SlopCityCityForge.IsBuildable(centre) || revision != expectedHead.activeWorldRevision || !string.Equals(mapRoot, expectedHead.chunkMapRoot, StringComparison.Ordinal))
            {
                error = "The City Forge strategic map does not match the current World Head.";
                return false;
            }
            int boundedRadius = (int)radius;
            int extent = boundedRadius * 2 + 1;
            if (rows.Count > extent * extent) { error = "The City Forge strategic map contains too many blocks for its view."; return false; }
            HashSet<CityForgeCoord> seen = new HashSet<CityForgeCoord>();
            CityForgeStrategicMap parsed = new CityForgeStrategicMap { worldRevision = revision, chunkMapRoot = mapRoot, centre = centre, radius = boundedRadius };
            for (int index = 0; index < rows.Count; index++)
            {
                string path = "$.chunks[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (!(rows[index] is Dictionary<string, object> row)
                    || !Exact(row, new[] { "coord", "revisionId", "snapshotHash", "roadCount", "buildingCount", "serviceCount", "landmarkCount" }, Array.Empty<string>(), path, out error)
                    || !Coord(row, "coord", path + ".coord", out CityForgeCoord coord, out error)
                    || !String(row, "revisionId", path + ".revisionId", out string revisionId, out error)
                    || !String(row, "snapshotHash", path + ".snapshotHash", out string snapshotHash, out error)
                    || !Integer(row, "roadCount", 0, SlopCityCityForge.MaximumRoadRecords, path + ".roadCount", out long roads, out error)
                    || !Integer(row, "buildingCount", 0, SlopCityCityForge.MaximumBuildingRecords, path + ".buildingCount", out long buildings, out error)
                    || !Integer(row, "serviceCount", 0, SlopCityCityForge.MaximumServiceRecords, path + ".serviceCount", out long services, out error)
                    || !Integer(row, "landmarkCount", 0, SlopCityCityForge.MaximumLandmarkRecords, path + ".landmarkCount", out long landmarks, out error)) return false;
                if (!SlopCityCityForge.IsBuildable(coord) || Math.Abs(coord.x - centre.x) > boundedRadius || Math.Abs(coord.z - centre.z) > boundedRadius
                    || !seen.Add(coord) || !RuntimeId(revisionId) || !CityForgeIds.IsHash(snapshotHash))
                {
                    error = path + " is not a bounded accepted City Forge summary.";
                    return false;
                }
                parsed.chunks.Add(new CityForgeStrategicChunkSummary
                {
                    coord = coord, revisionId = revisionId, snapshotHash = snapshotHash,
                    roadCount = (int)roads, buildingCount = (int)buildings,
                    serviceCount = (int)services, landmarkCount = (int)landmarks
                });
            }
            map = parsed;
            return true;
        }

        /// <summary>Serializes the strict, bounded strategic-map DTO for public bridge tests and adapters.</summary>
        public static string ToStrategicMapJson(CityForgeStrategicMap map)
        {
            if (map == null || map.worldRevision < 1 || map.worldRevision > MaximumSafeInteger || !CityForgeIds.IsHash(map.chunkMapRoot) || !SlopCityCityForge.IsBuildable(map.centre)
                || map.radius < 0 || map.radius > 6 || map.chunks == null || map.chunks.Count > SlopCityCityForge.MaximumStrategicSummaryRecords)
                throw new InvalidOperationException("The City Forge strategic map is invalid.");
            List<object> rows = new List<object>();
            HashSet<CityForgeCoord> seen = new HashSet<CityForgeCoord>();
            foreach (CityForgeStrategicChunkSummary summary in map.chunks.OrderBy(item => item == null ? new CityForgeCoord(int.MinValue, int.MinValue) : item.coord))
            {
                if (summary == null || !SlopCityCityForge.IsBuildable(summary.coord) || Math.Abs(summary.coord.x - map.centre.x) > map.radius || Math.Abs(summary.coord.z - map.centre.z) > map.radius
                    || !seen.Add(summary.coord) || !RuntimeId(summary.revisionId) || !CityForgeIds.IsHash(summary.snapshotHash)
                    || summary.roadCount < 0 || summary.roadCount > SlopCityCityForge.MaximumRoadRecords
                    || summary.buildingCount < 0 || summary.buildingCount > SlopCityCityForge.MaximumBuildingRecords
                    || summary.serviceCount < 0 || summary.serviceCount > SlopCityCityForge.MaximumServiceRecords
                    || summary.landmarkCount < 0 || summary.landmarkCount > SlopCityCityForge.MaximumLandmarkRecords)
                    throw new InvalidOperationException("The City Forge strategic block summary is invalid.");
                rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "coord", new Dictionary<string, object>(StringComparer.Ordinal) { { "x", new CityForgeJsonNumber(summary.coord.x.ToString(CultureInfo.InvariantCulture)) }, { "z", new CityForgeJsonNumber(summary.coord.z.ToString(CultureInfo.InvariantCulture)) } } },
                    { "revisionId", summary.revisionId }, { "snapshotHash", summary.snapshotHash },
                    { "roadCount", new CityForgeJsonNumber(summary.roadCount.ToString(CultureInfo.InvariantCulture)) },
                    { "buildingCount", new CityForgeJsonNumber(summary.buildingCount.ToString(CultureInfo.InvariantCulture)) },
                    { "serviceCount", new CityForgeJsonNumber(summary.serviceCount.ToString(CultureInfo.InvariantCulture)) },
                    { "landmarkCount", new CityForgeJsonNumber(summary.landmarkCount.ToString(CultureInfo.InvariantCulture)) }
                });
            }
            return CanonicalJson(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "worldRevision", new CityForgeJsonNumber(map.worldRevision.ToString(CultureInfo.InvariantCulture)) }, { "chunkMapRoot", map.chunkMapRoot },
                { "centre", new Dictionary<string, object>(StringComparer.Ordinal) { { "x", new CityForgeJsonNumber(map.centre.x.ToString(CultureInfo.InvariantCulture)) }, { "z", new CityForgeJsonNumber(map.centre.z.ToString(CultureInfo.InvariantCulture)) } } },
                { "radius", new CityForgeJsonNumber(map.radius.ToString(CultureInfo.InvariantCulture)) }, { "chunks", rows }
            });
        }

        /// <summary>
        /// Parses the compact accepted-world outcome index used to prove
        /// cross-site CitySituation prerequisites when only a nearby chunk page
        /// is resident. Landmark IDs never appear here because they are local to
        /// a plan; this is outcome/state presence only.
        /// </summary>
        public static bool TryLoadOutcomeIndexJson(string json, CityForgeWorldHead expectedHead, out CityForgeOutcomeIndex index, out string error)
        {
            index = null;
            if (Encoding.UTF8.GetByteCount(json ?? string.Empty) > MaximumApiPageBytes) { error = "The City Forge outcome index exceeds its transport bound."; return false; }
            if (expectedHead == null) { error = "The expected City Forge World Head is missing."; return false; }
            if (!ParseObject(json, out Dictionary<string, object> root, out error)
                || !Exact(root, new[] { "worldRevision", "chunkMapRoot", "outcomes" }, Array.Empty<string>(), "$", out error)
                || !Integer(root, "worldRevision", 1, MaximumSafeInteger, "$.worldRevision", out long revision, out error)
                || !String(root, "chunkMapRoot", "$.chunkMapRoot", out string mapRoot, out error)
                || !ReadArray(root, "outcomes", "$.outcomes", SlopCityCityForge.MaximumOutcomeIndexEntries, out List<object> rows, out error)) return false;
            if (!CityForgeIds.IsHash(mapRoot) || revision != expectedHead.activeWorldRevision || !string.Equals(mapRoot, expectedHead.chunkMapRoot, StringComparison.Ordinal))
            {
                error = "The City Forge outcome index does not match the current World Head.";
                return false;
            }
            SlopCityCityForgeCatalog catalog = SlopCityCityForgeCatalog.Default;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            CityForgeOutcomeIndex parsed = new CityForgeOutcomeIndex { worldRevision = revision, chunkMapRoot = mapRoot };
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                string path = "$.outcomes[" + rowIndex.ToString(CultureInfo.InvariantCulture) + "]";
                if (!(rows[rowIndex] is Dictionary<string, object> row)
                    || !Exact(row, new[] { "outcomeId", "active", "opening", "established" }, Array.Empty<string>(), path, out error)
                    || !String(row, "outcomeId", path + ".outcomeId", out string outcomeId, out error)
                    || !Integer(row, "active", 0, SlopCityCityForge.MaximumOutcomeStateCount, path + ".active", out long active, out error)
                    || !Integer(row, "opening", 0, SlopCityCityForge.MaximumOutcomeStateCount, path + ".opening", out long opening, out error)
                    || !Integer(row, "established", 0, SlopCityCityForge.MaximumOutcomeStateCount, path + ".established", out long established, out error)
                    || !catalog.IsLandmarkOutcome(outcomeId) || !seen.Add(outcomeId))
                {
                    error = path + " is not a reviewed Cityworks outcome presence.";
                    return false;
                }
                parsed.outcomes.Add(new CityForgeOutcomePresence { outcomeId = outcomeId, active = active, opening = opening, established = established });
            }
            index = parsed;
            return true;
        }

        /// <summary>Serializes the strict compact outcome-index DTO for public bridge adapters and tests.</summary>
        public static string ToOutcomeIndexJson(CityForgeOutcomeIndex index)
        {
            if (index == null || index.worldRevision < 1 || index.worldRevision > MaximumSafeInteger || !CityForgeIds.IsHash(index.chunkMapRoot)
                || index.outcomes == null || index.outcomes.Count > SlopCityCityForge.MaximumOutcomeIndexEntries)
                throw new InvalidOperationException("The City Forge outcome index is invalid.");
            SlopCityCityForgeCatalog catalog = SlopCityCityForgeCatalog.Default;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<object> rows = new List<object>();
            foreach (CityForgeOutcomePresence row in index.outcomes.OrderBy(item => item == null ? string.Empty : item.outcomeId, StringComparer.Ordinal))
            {
                if (row == null || !catalog.IsLandmarkOutcome(row.outcomeId) || !seen.Add(row.outcomeId)
                    || row.active < 0 || row.active > SlopCityCityForge.MaximumOutcomeStateCount
                    || row.opening < 0 || row.opening > SlopCityCityForge.MaximumOutcomeStateCount
                    || row.established < 0 || row.established > SlopCityCityForge.MaximumOutcomeStateCount)
                    throw new InvalidOperationException("The City Forge outcome presence is invalid.");
                rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "outcomeId", row.outcomeId }, { "active", new CityForgeJsonNumber(row.active.ToString(CultureInfo.InvariantCulture)) },
                    { "opening", new CityForgeJsonNumber(row.opening.ToString(CultureInfo.InvariantCulture)) }, { "established", new CityForgeJsonNumber(row.established.ToString(CultureInfo.InvariantCulture)) }
                });
            }
            return CanonicalJson(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "worldRevision", new CityForgeJsonNumber(index.worldRevision.ToString(CultureInfo.InvariantCulture)) }, { "chunkMapRoot", index.chunkMapRoot }, { "outcomes", rows }
            });
        }

        /// <summary>
        /// Parses one durable, unaccepted hosted draft without taking a dependency
        /// on tenant authentication or storage. The caller supplies the accepted
        /// world/block identity that the stored draft must still target; a stale
        /// draft cannot be restamped onto a newer block revision or base hash.
        /// A successful empty response returns <c>true</c> with a null draft.
        /// </summary>
        public static bool TryLoadApiDraftJson(string json, CityForgeCoord coord, string baseChunkHash, long parentChunkRevision, long parentWorldRevision, out CityForgeApiDraft draft, out string error)
        {
            draft = null;
            if (Encoding.UTF8.GetByteCount(json ?? string.Empty) > MaximumApiPageBytes) { error = "The City Forge draft exceeds its transport bound."; return false; }
            if (!SlopCityCityForge.IsBuildable(coord) || !CityForgeIds.IsHash(baseChunkHash) || parentChunkRevision < 0 || parentChunkRevision > MaximumSafeInteger || parentWorldRevision < 1 || parentWorldRevision > MaximumSafeInteger)
            {
                error = "The expected City Forge draft parent is invalid.";
                return false;
            }
            if (!ParseObject(json, out Dictionary<string, object> root, out error) || !Exact(root, new[] { "draft" }, Array.Empty<string>(), "$", out error)) return false;
            if (root["draft"] == null) return true;
            if (!(root["draft"] is Dictionary<string, object> raw)
                || !Exact(raw, new[] { "coord", "version", "baseChunkHash", "parentChunkRevision", "parentWorldRevision", "delta", "updatedAt" }, Array.Empty<string>(), "$.draft", out error)
                || !Coord(raw, "coord", "$.draft.coord", out CityForgeCoord returnedCoord, out error)
                || !Integer(raw, "version", 1, MaximumSafeInteger, "$.draft.version", out long version, out error)
                || !String(raw, "baseChunkHash", "$.draft.baseChunkHash", out string storedBaseHash, out error)
                || !Integer(raw, "parentChunkRevision", 0, MaximumSafeInteger, "$.draft.parentChunkRevision", out long storedParentChunkRevision, out error)
                || !Integer(raw, "parentWorldRevision", 1, MaximumSafeInteger, "$.draft.parentWorldRevision", out long storedParentWorldRevision, out error)
                || !Integer(raw, "updatedAt", 0, MaximumSafeInteger, "$.draft.updatedAt", out long updatedAt, out error)) return false;
            if (!returnedCoord.Equals(coord) || !CityForgeIds.IsHash(storedBaseHash)
                || !string.Equals(storedBaseHash, baseChunkHash, StringComparison.Ordinal)
                || storedParentChunkRevision != parentChunkRevision || storedParentWorldRevision != parentWorldRevision)
            {
                error = "The durable City Forge draft no longer matches its accepted parent.";
                return false;
            }
            if (!(raw["delta"] is Dictionary<string, object> delta)
                || !ReadFriendlyDelta(delta, raw["coord"], raw["baseChunkHash"], storedParentChunkRevision, "$.draft.delta", out CityForgeChunkPlan plan, out error)) return false;
            if (!plan.coord.Equals(coord) || !string.Equals(plan.baseChunkHash, baseChunkHash, StringComparison.Ordinal) || plan.parentChunkRevision != parentChunkRevision)
            {
                error = "The durable City Forge draft no longer matches its accepted block.";
                return false;
            }
            draft = new CityForgeApiDraft { coord = coord, version = version, baseChunkHash = storedBaseHash, parentChunkRevision = storedParentChunkRevision, parentWorldRevision = storedParentWorldRevision, updatedAt = updatedAt, plan = plan };
            return true;
        }

        /// <summary>
        /// Compatibility overload deliberately fails closed. Durable drafts now
        /// require a verified parent World revision in addition to the chunk
        /// base/revision; callers must use the overload above.
        /// </summary>
        public static bool TryLoadApiDraftJson(string json, CityForgeCoord coord, string baseChunkHash, long parentChunkRevision, out CityForgeApiDraft draft, out string error)
        {
            draft = null;
            error = "A durable City Forge draft requires its expected parent World revision.";
            return false;
        }

        private static bool ReadApiHead(Dictionary<string, object> raw, out CityForgeWorldHead head, out string revisionId, out string error)
        {
            head = null; revisionId = null;
            string[] fields = { "revisionId", "version", "schemaVersion", "worldSeed", "generatorVersion", "catalogHash", "chunkMapRoot", "grid", "updatedAt" };
            if (!Exact(raw, fields, Array.Empty<string>(), "$.worldHead", out error)
                || !String(raw, "revisionId", "$.worldHead.revisionId", out revisionId, out error)
                || !Integer(raw, "version", 1, MaximumSafeInteger, "$.worldHead.version", out long version, out error)) return false;
            if (!RuntimeId(revisionId)) { error = "$.worldHead.revisionId is invalid."; return false; }
            if (!(raw["updatedAt"] is CityForgeJsonNumber timestamp) || !long.TryParse(timestamp.text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long time) || time < 0 || time > MaximumSafeInteger) { error = "$.worldHead.updatedAt must be a bounded integer."; return false; }
            Dictionary<string, object> direct = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "schemaVersion", raw["schemaVersion"] }, { "catalogHash", raw["catalogHash"] }, { "worldSeed", raw["worldSeed"] }, { "generatorVersion", raw["generatorVersion"] },
                { "worldRevision", new CityForgeJsonNumber(version.ToString(CultureInfo.InvariantCulture)) }, { "chunkMapRoot", raw["chunkMapRoot"] }, { "grid", raw["grid"] }
            };
            return ReadWorldHead(direct, "$.worldHead", out head, out error);
        }

        private static bool ReadApiChunk(Dictionary<string, object> raw, string path, out CityForgeApiPageChunk chunk, out string error)
        {
            chunk = null;
            string[] required = { "coord", "revisionId", "version", "baseChunkHash", "snapshotSha256", "updatedAt" };
            if (!Exact(raw, required, new[] { "plan", "delta" }, path, out error)
                || !Coord(raw, "coord", path + ".coord", out CityForgeCoord coord, out error)
                || !Integer(raw, "version", 0, MaximumSafeInteger, path + ".version", out long version, out error)
                || !String(raw, "baseChunkHash", path + ".baseChunkHash", out string baseHash, out error)
                || !StringOrNull(raw, "revisionId", path + ".revisionId", out string revisionId, out error)
                || !StringOrNull(raw, "snapshotSha256", path + ".snapshotSha256", out string snapshot, out error)) return false;
            if (!CityForgeIds.IsHash(baseHash)) { error = path + ".baseChunkHash is invalid."; return false; }
            bool hasPlan = raw.ContainsKey("plan") && raw["plan"] != null;
            bool hasDelta = raw.ContainsKey("delta") && raw["delta"] != null;
            CityForgeChunkPlan plan = null;
            if (version == 0)
            {
                if (revisionId != null || snapshot != null || hasPlan || hasDelta) { error = path + " cannot contain an accepted revision before its first save."; return false; }
            }
            else
            {
                if (!RuntimeId(revisionId) || !CityForgeIds.IsHash(snapshot) || (!hasPlan && !hasDelta)) { error = path + " is missing its accepted revision."; return false; }
                CityForgeChunkPlan friendly = null;
                if (hasPlan)
                {
                    if (!(raw["plan"] is Dictionary<string, object> rawPlan) || !ReadPlan(rawPlan, path + ".plan", out plan, out error)) return false;
                }
                if (hasDelta)
                {
                    if (!(raw["delta"] is Dictionary<string, object> rawDelta) || !ReadFriendlyDelta(rawDelta, raw["coord"], raw["baseChunkHash"], version - 1, path + ".delta", out friendly, out error)) return false;
                }
                if (plan == null) plan = friendly;
                else if (friendly != null && !string.Equals(SlopCityCityForgeCanonical.Pack(plan), SlopCityCityForgeCanonical.Pack(friendly), StringComparison.Ordinal)) { error = path + " has different plan and delta records."; return false; }
            }
            long? updated = null;
            if (raw["updatedAt"] != null)
            {
                if (!(raw["updatedAt"] is CityForgeJsonNumber time) || !long.TryParse(time.text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long timestamp) || timestamp < 0 || timestamp > MaximumSafeInteger) { error = path + ".updatedAt must be a bounded integer or null."; return false; }
                updated = timestamp;
            }
            chunk = new CityForgeApiPageChunk { coord = coord, revisionId = revisionId, version = version, baseChunkHash = baseHash, snapshotSha256 = snapshot, plan = plan, updatedAt = updated };
            return true;
        }

        private static bool ReadFriendlyDelta(Dictionary<string, object> delta, object rawCoord, object rawBaseHash, long parentRevision, string path, out CityForgeChunkPlan plan, out string error)
        {
            plan = null;
            string[] fields = { "roads", "removedRoadEdges", "parcels", "buildings", "services", "screens", "landmarks", "demolitions", "situations" };
            if (!Exact(delta, fields, Array.Empty<string>(), path, out error)) return false;
            Dictionary<string, object> full = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "coord", rawCoord }, { "baseChunkHash", rawBaseHash }, { "parentChunkRevision", new CityForgeJsonNumber(parentRevision.ToString(CultureInfo.InvariantCulture)) },
                { "roads", delta["roads"] }, { "removedRoadEdges", delta["removedRoadEdges"] }, { "parcels", delta["parcels"] }, { "buildings", delta["buildings"] },
                { "services", delta["services"] }, { "screens", delta["screens"] }, { "landmarks", delta["landmarks"] }, { "situations", delta["situations"] }, { "demolishedIds", delta["demolitions"] }
            };
            return ReadPlan(full, path, out plan, out error);
        }

        /// <summary>Serializes the API-friendly bounded delta; it contains no hashes, IDs, transforms, URLs, or media values beyond the shared plan vocabulary.</summary>
        public static string ToFriendlyDeltaJson(CityForgeChunkPlan plan)
        {
            if (!CityForgeJson.TryParse(ToWireJson(plan), out object raw, out string error) || !(raw is Dictionary<string, object> canonical)) throw new InvalidOperationException(error ?? "The City Forge plan is invalid.");
            Dictionary<string, object> delta = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "roads", canonical["roads"] }, { "removedRoadEdges", canonical["removedRoadEdges"] }, { "parcels", canonical["parcels"] }, { "buildings", canonical["buildings"] },
                { "services", canonical["services"] }, { "screens", canonical["screens"] }, { "landmarks", canonical["landmarks"] }, { "demolitions", canonical["demolishedIds"] }, { "situations", canonical["situations"] }
            };
            return CanonicalJson(delta);
        }

        /// <summary>
        /// Builds the durable-draft upsert payload with immutable accepted-parent
        /// binding. Servers store and echo these values verbatim; they must never
        /// restamp a draft to the caller's newer chunk/world revision.
        /// </summary>
        public static string ToFriendlyDraftJson(CityForgeChunkPlan plan, long parentWorldRevision)
        {
            if (plan == null || !SlopCityCityForge.IsBuildable(plan.coord) || !CityForgeIds.IsHash(plan.baseChunkHash)
                || plan.parentChunkRevision < 0 || plan.parentChunkRevision > MaximumSafeInteger
                || parentWorldRevision < 1 || parentWorldRevision > MaximumSafeInteger)
                throw new InvalidOperationException("The City Forge draft parent is invalid.");
            if (!CityForgeJson.TryParse(ToFriendlyDeltaJson(plan), out object delta, out string error)) throw new InvalidOperationException(error ?? "The City Forge draft is invalid.");
            return CanonicalJson(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "coord", new Dictionary<string, object>(StringComparer.Ordinal) { { "x", new CityForgeJsonNumber(plan.coord.x.ToString(CultureInfo.InvariantCulture)) }, { "z", new CityForgeJsonNumber(plan.coord.z.ToString(CultureInfo.InvariantCulture)) } } },
                { "baseChunkHash", plan.baseChunkHash }, { "parentChunkRevision", new CityForgeJsonNumber(plan.parentChunkRevision.ToString(CultureInfo.InvariantCulture)) },
                { "parentWorldRevision", new CityForgeJsonNumber(parentWorldRevision.ToString(CultureInfo.InvariantCulture)) }, { "delta", delta }
            });
        }

        /// <summary>Builds the bounded API commit DTO from validated public plans.</summary>
        public static string ToFriendlyCommitJson(string mutationId, long expectedWorldVersion, IEnumerable<CityForgeChunkPlan> plans)
        {
            if (!MutationId(mutationId) || expectedWorldVersion < 1 || expectedWorldVersion > MaximumSafeInteger) throw new InvalidOperationException("The City Forge commit identity is invalid.");
            List<object> chunks = new List<object>();
            HashSet<CityForgeCoord> coordinates = new HashSet<CityForgeCoord>();
            foreach (CityForgeChunkPlan plan in (plans ?? Enumerable.Empty<CityForgeChunkPlan>()).Where(item => item != null).OrderBy(item => item.coord))
            {
                if (!coordinates.Add(plan.coord) || plan.parentChunkRevision < 0 || plan.parentChunkRevision > MaximumSafeInteger) throw new InvalidOperationException("The City Forge commit repeats or has an invalid chunk.");
                if (!CityForgeJson.TryParse(ToFriendlyDeltaJson(plan), out object delta, out string error)) throw new InvalidOperationException(error);
                chunks.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "coord", new Dictionary<string, object>(StringComparer.Ordinal) { { "x", new CityForgeJsonNumber(plan.coord.x.ToString(CultureInfo.InvariantCulture)) }, { "z", new CityForgeJsonNumber(plan.coord.z.ToString(CultureInfo.InvariantCulture)) } } },
                    { "expectedChunkVersion", new CityForgeJsonNumber(plan.parentChunkRevision.ToString(CultureInfo.InvariantCulture)) }, { "delta", delta }
                });
            }
            if (chunks.Count == 0 || chunks.Count > 4) throw new InvalidOperationException("A City Forge commit must contain one to four chunks.");
            string result = CanonicalJson(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "mutationId", mutationId }, { "expectedWorldVersion", new CityForgeJsonNumber(expectedWorldVersion.ToString(CultureInfo.InvariantCulture)) }, { "chunks", chunks }
            });
            if (Encoding.UTF8.GetByteCount(result) > 131072) throw new InvalidOperationException("The City Forge commit exceeds its bounded transport size.");
            return result;
        }

        private static bool MutationId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 16 || value.Length > 100) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index]; if (!((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') || character == '_' || character == '-')) return false;
            }
            return true;
        }

        private static string CanonicalJson(object value)
        {
            StringBuilder output = new StringBuilder(); AppendCanonical(value, output); return output.ToString();
        }
        private static void AppendCanonical(object value, StringBuilder output)
        {
            if (value == null) { output.Append("null"); return; }
            if (value is string text) { output.Append(Quote(text)); return; }
            if (value is CityForgeJsonNumber number) { output.Append(number.text); return; }
            if (value is bool logical) { output.Append(logical ? "true" : "false"); return; }
            if (value is List<object> array) { output.Append('['); for (int index = 0; index < array.Count; index++) { if (index > 0) output.Append(','); AppendCanonical(array[index], output); } output.Append(']'); return; }
            if (value is Dictionary<string, object> map)
            {
                output.Append('{'); bool first = true;
                foreach (KeyValuePair<string, object> item in map.OrderBy(item => item.Key, StringComparer.Ordinal)) { if (!first) output.Append(','); first = false; output.Append(Quote(item.Key)).Append(':'); AppendCanonical(item.Value, output); }
                output.Append('}'); return;
            }
            throw new InvalidOperationException("Unsupported City Forge JSON value.");
        }
    }

    /// <summary>One verified singleton hosted page, suitable for aggregation by a private bridge.</summary>
    [Serializable]
    public sealed class CityForgeApiPage
    {
        public CityForgeWorldHead worldHead;
        public string worldRevisionId = string.Empty;
        public CityForgeCoord centre;
        public int radius;
        public bool canEdit;
        public List<CityForgeApiPageChunk> chunks = new List<CityForgeApiPageChunk>();

        public IEnumerable<CityForgeChunkPlan> AcceptedPlans { get { return chunks.Where(item => item != null && item.plan != null).Select(item => item.plan.Clone()); } }
    }

    [Serializable]
    public sealed class CityForgeApiPageChunk
    {
        public CityForgeCoord coord;
        public string revisionId;
        public long version;
        public string baseChunkHash;
        public string snapshotSha256;
        public CityForgeChunkPlan plan;
        public long? updatedAt;

        public CityForgeChunkHead ToChunkHead()
        {
            return plan == null ? null : new CityForgeChunkHead { coord = coord, revisionId = revisionId, revision = version, baseChunkHash = baseChunkHash, snapshotHash = snapshotSha256 };
        }
    }

    /// <summary>
    /// A verified but unaccepted player draft. It has no authority, media, URL,
    /// transform, or tenant fields; callers must still submit its friendly delta
    /// to their authoritative commit endpoint.
    /// </summary>
    [Serializable]
    public sealed class CityForgeApiDraft
    {
        public CityForgeCoord coord;
        /// <summary>Durable draft-record version, distinct from the accepted chunk revision.</summary>
        public long version;
        /// <summary>Immutable accepted parent binding echoed by the durable draft record.</summary>
        public string baseChunkHash;
        public long parentChunkRevision;
        public long parentWorldRevision;
        public long updatedAt;
        public CityForgeChunkPlan plan;
    }

    internal sealed class CityForgeJsonNumber
    {
        public readonly string text;
        public CityForgeJsonNumber(string value) { text = value; }
    }

    /// <summary>Minimal strict JSON reader; values are bounded immediately by the wire mapper.</summary>
    internal sealed class CityForgeJson
    {
        private readonly string source;
        private int position;
        private CityForgeJson(string value) { source = value ?? string.Empty; }

        public static bool TryParse(string value, out object result, out string error)
        {
            CityForgeJson reader = new CityForgeJson(value);
            result = null; error = null;
            try
            {
                reader.SkipWhitespace(); result = reader.ReadValue(); reader.SkipWhitespace();
                if (reader.position != reader.source.Length) throw reader.Fail("Unexpected trailing JSON.");
                return true;
            }
            catch (FormatException exception) { error = exception.Message; return false; }
        }

        private object ReadValue()
        {
            SkipWhitespace();
            if (position >= source.Length) throw Fail("Unexpected end of JSON.");
            char value = source[position];
            if (value == '{') return ReadObject();
            if (value == '[') return ReadArray();
            if (value == '\"') return ReadString();
            if (value == 't' && TakeWord("true")) return true;
            if (value == 'f' && TakeWord("false")) return false;
            if (value == 'n' && TakeWord("null")) return null;
            if (value == '-' || (value >= '0' && value <= '9')) return ReadNumber();
            throw Fail("Expected a JSON value.");
        }

        private Dictionary<string, object> ReadObject()
        {
            Expect('{'); SkipWhitespace();
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
            if (Take('}')) return result;
            while (true)
            {
                SkipWhitespace(); if (position >= source.Length || source[position] != '\"') throw Fail("Expected an object key.");
                string key = ReadString(); SkipWhitespace(); Expect(':'); object value = ReadValue();
                if (result.ContainsKey(key)) throw Fail("Duplicate JSON property.");
                result.Add(key, value); SkipWhitespace();
                if (Take('}')) return result;
                Expect(',');
            }
        }

        private List<object> ReadArray()
        {
            Expect('['); SkipWhitespace(); List<object> result = new List<object>();
            if (Take(']')) return result;
            while (true)
            {
                result.Add(ReadValue()); SkipWhitespace();
                if (Take(']')) return result;
                Expect(',');
            }
        }

        private string ReadString()
        {
            Expect('\"'); StringBuilder result = new StringBuilder();
            while (position < source.Length)
            {
                char value = source[position++];
                if (value == '\"') return result.ToString();
                if (value < 32) throw Fail("Control character in JSON string.");
                if (value != '\\') { result.Append(value); continue; }
                if (position >= source.Length) throw Fail("Incomplete JSON escape.");
                char escaped = source[position++];
                if (escaped == '\"' || escaped == '\\' || escaped == '/') result.Append(escaped);
                else if (escaped == 'b') result.Append('\b'); else if (escaped == 'f') result.Append('\f'); else if (escaped == 'n') result.Append('\n'); else if (escaped == 'r') result.Append('\r'); else if (escaped == 't') result.Append('\t');
                else if (escaped == 'u')
                {
                    if (position + 4 > source.Length) throw Fail("Incomplete unicode escape.");
                    string hex = source.Substring(position, 4); position += 4;
                    if (!ushort.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort unit)) throw Fail("Invalid unicode escape.");
                    result.Append((char)unit);
                }
                else throw Fail("Invalid JSON escape.");
            }
            throw Fail("Unclosed JSON string.");
        }

        private CityForgeJsonNumber ReadNumber()
        {
            int start = position;
            Take('-');
            if (Take('0')) { }
            else
            {
                if (position >= source.Length || source[position] < '1' || source[position] > '9') throw Fail("Invalid JSON number.");
                while (position < source.Length && source[position] >= '0' && source[position] <= '9') position++;
            }
            if (Take('.')) { if (position >= source.Length || source[position] < '0' || source[position] > '9') throw Fail("Invalid JSON number."); while (position < source.Length && source[position] >= '0' && source[position] <= '9') position++; }
            if (position < source.Length && (source[position] == 'e' || source[position] == 'E')) { position++; if (position < source.Length && (source[position] == '+' || source[position] == '-')) position++; if (position >= source.Length || source[position] < '0' || source[position] > '9') throw Fail("Invalid JSON number."); while (position < source.Length && source[position] >= '0' && source[position] <= '9') position++; }
            return new CityForgeJsonNumber(source.Substring(start, position - start));
        }

        private void SkipWhitespace() { while (position < source.Length && (source[position] == ' ' || source[position] == '\t' || source[position] == '\r' || source[position] == '\n')) position++; }
        private bool Take(char value) { if (position < source.Length && source[position] == value) { position++; return true; } return false; }
        private bool TakeWord(string value) { if (position + value.Length > source.Length || string.CompareOrdinal(source, position, value, 0, value.Length) != 0) return false; position += value.Length; return true; }
        private void Expect(char value) { if (!Take(value)) throw Fail("Expected '" + value + "'."); }
        private FormatException Fail(string message) { return new FormatException(message + " (at character " + position.ToString(CultureInfo.InvariantCulture) + ")."); }
    }
}
