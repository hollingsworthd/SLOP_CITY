using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SlopCity
{
    /// <summary>Result of a local or server-side validation without leaking raw input.</summary>
    public sealed class CityForgeValidation
    {
        private readonly List<string> messages = new List<string>();
        public bool IsValid { get { return messages.Count == 0; } }
        public IReadOnlyList<string> Messages { get { return messages; } }
        public void Refuse(string message)
        {
            if (!string.IsNullOrEmpty(message) && messages.Count < 24) messages.Add(message);
        }
    }

    [Serializable]
    public sealed class CityForgeBaseChunk
    {
        public CityForgeCoord coord;
        public string baseHash = string.Empty;
        public List<CityForgeRoad> roads = new List<CityForgeRoad>();
        public string terrainId = string.Empty;
        public string parcelGrammarId = string.Empty;
        public string skylineId = string.Empty;
        public string lifeThemeId = string.Empty;
        public Dictionary<string, string> boundary = new Dictionary<string, string>(StringComparer.Ordinal);
        public int stableSeed;
    }

    [Serializable]
    public sealed class CityForgeResolvedScreen
    {
        public string screenId = string.Empty;
        public string hostId = string.Empty;
        public string mountId = string.Empty;
        public string deviceId = string.Empty;
        public string screenGroup = string.Empty;
        /// <summary>Compiler-derived stable fixture position; it is never supplied by a plan.</summary>
        public int slotIndex;
        /// <summary>Approved number of fixtures available on this host/mount kind.</summary>
        public int slotCount = 1;
        public bool catalogIntegrated;
    }

    [Serializable]
    public sealed class CityForgeResolvedChunk
    {
        public CityForgeBaseChunk baseChunk;
        public CityForgeChunkPlan acceptedPlan;
        public string resolvedHash = string.Empty;
        public List<CityForgeRoad> roads = new List<CityForgeRoad>();
        public List<CityForgeResolvedScreen> screens = new List<CityForgeResolvedScreen>();
        public CityForgeCityPulse pulse = new CityForgeCityPulse();
    }

    /// <summary>
    /// Pure deterministic base generator. All choices are catalog IDs and all
    /// cross-border choices are seeded from the shared boundary key, never the
    /// order in which either neighbouring chunk was streamed.
    /// </summary>
    public sealed class SlopCityCityForgeGenerator
    {
        private readonly SlopCityCityForgeCatalog catalog;

        public SlopCityCityForgeGenerator(SlopCityCityForgeCatalog cityCatalog)
        {
            catalog = cityCatalog ?? throw new ArgumentNullException("cityCatalog");
        }

        // This is a direct C# port of contracts/city-forge.mjs
        // generateBaseChunk(). FNV-1a, xorshift32, canonical edge ordering, and
        // canonical JSON must remain byte-identical across service, WebGL, and
        // offline clients; changing it is a generator-version migration.
        public CityForgeBaseChunk Generate(CityForgeWorldHead head, CityForgeCoord coord)
        {
            if (head == null) throw new ArgumentNullException("head");
            if (!SlopCityCityForge.IsCoordinateInRange(coord)) throw new InvalidOperationException("The requested frontier coordinate is invalid.");
            EnsureHeadCompatible(head, catalog);
            uint state = Seed32(head.worldSeed + "|" + head.generatorVersion.ToString(CultureInfo.InvariantCulture) + "|" + head.catalogHash + "|" + coord.ToString());
            string terrain = Sample(ref state, new[] { "wet-paving", "service-concrete", "garden-grade", "old-masonry" });
            string grammar = Sample(ref state, new[] { "perimeter", "courtyard", "spine", "laneway" });
            string skyline = Sample(ref state, new[] { "low-rise", "mixed-rise", "tower-punctuated", "civic-open" });
            string life = Sample(ref state, new[] { "repair", "care", "culture", "exchange" });
            CityForgeBaseChunk chunk = new CityForgeBaseChunk
            {
                coord = coord,
                stableSeed = unchecked((int)(state & 0x7fffffffu)),
                terrainId = terrain,
                parcelGrammarId = grammar,
                skylineId = skyline,
                lifeThemeId = life
            };
            string[] edges = { "0,8:north", "8,0:east", "0,0:south", "0,0:west" };
            for (int index = 0; index < edges.Length; index++)
            {
                string roadClass = OldCityConnectionStub(coord, edges[index]) ?? BoundaryStub(head.worldSeed, head.generatorVersion, head.catalogHash, coord, edges[index]);
                if (roadClass == null) continue;
                string direction = edges[index].Substring(edges[index].IndexOf(':') + 1);
                chunk.boundary[direction] = roadClass;
                CityForgeRoad road = RepresentativeRoad(direction, roadClass);
                chunk.roads.Add(road);
            }
            chunk.baseHash = BaseChunkHash(head, chunk);
            return chunk;
        }

        public bool IsCoreStub(CityForgeWorldHead head, CityForgeCoord coord, CityForgeDirection direction, int offset)
        {
            if (!SlopCityCityForge.IsProtectedOldCity(SlopCityCityForge.Neighbour(coord, direction))) return false;
            CityForgeBaseChunk baseChunk = Generate(head, coord);
            return baseChunk.roads.Any(road => road.IsBoundary && road.direction == direction && road.BoundaryOffset == offset);
        }

        public static string CanonicalBoundaryKey(CityForgeCoord coordinate, string edge)
        {
            string[] parts = edge.Split(':');
            string[] cell = parts[0].Split(',');
            int cellX = int.Parse(cell[0], CultureInfo.InvariantCulture);
            int cellZ = int.Parse(cell[1], CultureInfo.InvariantCulture);
            string direction = parts[1];
            int otherX = coordinate.x, otherZ = coordinate.z, otherCellX = cellX, otherCellZ = cellZ;
            string otherDirection;
            if (direction == "north") { otherDirection = "south"; if (cellZ == 8) { otherZ++; otherCellZ = 0; } else otherCellZ++; }
            else if (direction == "south") { otherDirection = "north"; if (cellZ == 0) { otherZ--; otherCellZ = 8; } else otherCellZ--; }
            else if (direction == "east") { otherDirection = "west"; if (cellX == 8) { otherX++; otherCellX = 0; } else otherCellX++; }
            else { otherDirection = "east"; if (cellX == 0) { otherX--; otherCellX = 8; } else otherCellX--; }
            string a = coordinate.ToString() + ":" + edge;
            string b = otherX.ToString(CultureInfo.InvariantCulture) + "," + otherZ.ToString(CultureInfo.InvariantCulture) + ":" + otherCellX.ToString(CultureInfo.InvariantCulture) + "," + otherCellZ.ToString(CultureInfo.InvariantCulture) + ":" + otherDirection;
            return string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
        }

        private static string BoundaryStub(string seed, int version, string catalogHash, CityForgeCoord coord, string edge)
        {
            uint value = Seed32(seed + "|" + version.ToString(CultureInfo.InvariantCulture) + "|" + catalogHash + "|" + CanonicalBoundaryKey(coord, edge));
            if (value % 7u != 0u) return null;
            string[] roads = { "local-street", "boulevard", "service-alley", "pedestrian-route", "transit-service-corridor" };
            return roads[(int)((value >> 8) % (uint)roads.Length)];
        }

        /// <summary>Matches oldCityConnectionStub() in contracts/city-forge.mjs.</summary>
        private static string OldCityConnectionStub(CityForgeCoord coord, string edge)
        {
            return string.Equals(edge, SlopCityCityForge.OldCityConnectionStubEdge(coord), StringComparison.Ordinal)
                ? SlopCityCityForge.OldCityConnectionRoadClass : null;
        }

        private static CityForgeRoad RepresentativeRoad(string direction, string roadClass)
        {
            CityForgeRoad road = new CityForgeRoad { roadClass = ParseRoadClass(roadClass) };
            if (direction == "north") { road.cellX = 0; road.cellZ = 8; road.direction = CityForgeDirection.North; }
            else if (direction == "east") { road.cellX = 8; road.cellZ = 0; road.direction = CityForgeDirection.East; }
            else if (direction == "south") { road.cellX = 0; road.cellZ = 0; road.direction = CityForgeDirection.South; }
            else { road.cellX = 0; road.cellZ = 0; road.direction = CityForgeDirection.West; }
            return road;
        }

        private static CityForgeRoadClass ParseRoadClass(string value)
        {
            if (value == "boulevard") return CityForgeRoadClass.Boulevard;
            if (value == "service-alley") return CityForgeRoadClass.ServiceAlley;
            if (value == "pedestrian-route") return CityForgeRoadClass.PedestrianRoute;
            if (value == "transit-service-corridor") return CityForgeRoadClass.TransitServiceCorridor;
            return CityForgeRoadClass.LocalStreet;
        }

        private static uint Seed32(string value)
        {
            uint hash = 0x811c9dc5u;
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            for (int i = 0; i < bytes.Length; i++) { hash ^= bytes[i]; hash = unchecked(hash * 0x01000193u); }
            return hash == 0u ? 1u : hash;
        }

        private static uint Next32(uint state)
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            return value;
        }

        private static string Sample(ref uint state, string[] choices)
        {
            state = Next32(state);
            return choices[(int)(state % (uint)choices.Length)];
        }

        private static string BaseChunkHash(CityForgeWorldHead head, CityForgeBaseChunk chunk)
        {
            StringBuilder json = new StringBuilder(512);
            json.Append('{');
            Property(json, "availableScreenMounts", ArrayJson(new[] { "facade-media-wall", "interior-wall-tv", "plaza-civic-support", "street-kiosk-screen" }), true);
            StringBuilder boundary = new StringBuilder("{");
            bool first = true;
            foreach (KeyValuePair<string, string> item in chunk.boundary.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (!first) boundary.Append(',');
                first = false;
                boundary.Append(JsonString(item.Key)).Append(':').Append(JsonString(item.Value));
            }
            boundary.Append('}');
            Property(json, "boundary", boundary.ToString(), false);
            Property(json, "catalogHash", JsonString(head.catalogHash), false);
            Property(json, "generatorVersion", head.generatorVersion.ToString(CultureInfo.InvariantCulture), false);
            Property(json, "landmarkClasses", ArrayJson(new[] { "home-workroom", "service-court", "street-corner", "transit-landing", "roof-reservoir", "tradefront", "utility-alley", "screen-room", "back-lobby", "signal-terrace" }), false);
            Property(json, "lifeThemeId", JsonString(chunk.lifeThemeId), false);
            Property(json, "parcelGrammarId", JsonString(chunk.parcelGrammarId), false);
            Property(json, "protected", SlopCityCityForge.IsProtectedOldCity(chunk.coord) ? "true" : "false", false);
            Property(json, "schemaVersion", SlopCityCityForge.WorldSchemaVersion.ToString(CultureInfo.InvariantCulture), false);
            Property(json, "skylineId", JsonString(chunk.skylineId), false);
            Property(json, "starterRing", SlopCityCityForge.IsStarterRing(chunk.coord) ? "true" : "false", false);
            Property(json, "terrainId", JsonString(chunk.terrainId), false);
            Property(json, "worldSeed", JsonString(head.worldSeed), false);
            Property(json, "x", chunk.coord.x.ToString(CultureInfo.InvariantCulture), false);
            Property(json, "z", chunk.coord.z.ToString(CultureInfo.InvariantCulture), false);
            json.Append('}');
            return SlopCityCityForge.Sha256(json.ToString());
        }

        private static void Property(StringBuilder builder, string name, string value, bool first)
        {
            if (!first) builder.Append(',');
            builder.Append(JsonString(name)).Append(':').Append(value);
        }

        private static string ArrayJson(IEnumerable<string> values)
        {
            return "[" + string.Join(",", values.Select(JsonString).ToArray()) + "]";
        }

        private static string JsonString(string value)
        {
            StringBuilder builder = new StringBuilder("\"");
            foreach (char c in value ?? string.Empty)
            {
                if (c == '\\') builder.Append("\\\\");
                else if (c == '\"') builder.Append("\\\"");
                else if (c == '\n') builder.Append("\\n");
                else if (c == '\r') builder.Append("\\r");
                else if (c == '\t') builder.Append("\\t");
                else if (c < 32) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else builder.Append(c);
            }
            return builder.Append('\"').ToString();
        }

        public static void EnsureHeadCompatible(CityForgeWorldHead head, SlopCityCityForgeCatalog catalog)
        {
            if (head.schemaVersion != SlopCityCityForge.WorldSchemaVersion || head.generatorVersion != catalog.GeneratorVersion
                || !string.Equals(head.catalogHash, catalog.Hash, StringComparison.Ordinal) || string.IsNullOrEmpty(head.worldSeed)
                || head.blockSizeCells != SlopCityCityForge.GridSize || head.cellSizeMeters != SlopCityCityForge.CellSizeMeters)
                throw new InvalidOperationException("This City Forge world is not compatible with the installed catalog.");
        }
    }

    /// <summary>Canonical serialization used for hashes, command comparisons, and local shards.</summary>
    public static class SlopCityCityForgeCanonical
    {
        public static string Pack(CityForgeChunkPlan plan)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            // Port of canonicalJson(normalizeChunkPlan(value)) in
            // contracts/city-forge.mjs. Keep lexical key order and compact JSON.
            StringBuilder builder = new StringBuilder(4096);
            builder.Append('{');
            JsonProperty(builder, "baseChunkHash", JsonString(plan.baseChunkHash), true);
            JsonProperty(builder, "buildings", BuildingsJson(plan.buildings), false);
            JsonProperty(builder, "coord", "{\"x\":" + plan.coord.x.ToString(CultureInfo.InvariantCulture) + ",\"z\":" + plan.coord.z.ToString(CultureInfo.InvariantCulture) + "}", false);
            JsonProperty(builder, "demolishedIds", StringsJson((plan.demolishedIds ?? new List<string>()).Distinct().OrderBy(item => item, StringComparer.Ordinal)), false);
            JsonProperty(builder, "landmarks", LandmarksJson(plan.landmarks), false);
            JsonProperty(builder, "parentChunkRevision", plan.parentChunkRevision.ToString(CultureInfo.InvariantCulture), false);
            JsonProperty(builder, "parcels", ParcelsJson(plan.parcels), false);
            JsonProperty(builder, "removedRoadEdges", RemovedRoadsJson(plan), false);
            JsonProperty(builder, "roads", RoadsJson(plan.roads), false);
            JsonProperty(builder, "screens", ScreensJson(plan.screens), false);
            JsonProperty(builder, "services", ServicesJson(plan.services), false);
            JsonProperty(builder, "situations", SituationsJson(plan.situations), false);
            builder.Append('}');
            return builder.ToString();
        }

        public static string Hash(CityForgeChunkPlan plan) { return SlopCityCityForge.Sha256(Pack(plan)); }

        public static string ChunkMapRoot(IEnumerable<CityForgeChunkHead> heads)
        {
            // Exact port of contracts/city-forge.mjs chunkMapRoot().  This is a
            // canonical JSON array, not a private newline grammar: hosted and
            // offline worlds must derive the same root for the same sparse map.
            List<CityForgeChunkHead> values = (heads ?? Enumerable.Empty<CityForgeChunkHead>()).Where(value => value != null).ToList();
            if (values.Count > 1000000) throw new InvalidOperationException("The City Forge sparse map is too large.");
            foreach (CityForgeChunkHead head in values)
            {
                if (!SlopCityCityForge.IsCoordinateInRange(head.coord) || !CityForgeIds.IsRuntimeId(head.revisionId) || !CityForgeIds.IsHash(head.snapshotHash))
                    throw new InvalidOperationException("The City Forge sparse map contains an invalid entry.");
            }
            StringBuilder builder = new StringBuilder(values.Count * 150 + 2);
            builder.Append('[');
            bool first = true;
            foreach (CityForgeChunkHead head in values.OrderBy(value => value.coord.x).ThenBy(value => value.coord.z).ThenBy(value => value.revisionId, StringComparer.Ordinal))
            {
                if (!first) builder.Append(',');
                first = false;
                // canonicalJson sorts object keys: coord, hash, revisionId.
                builder.Append("{\"coord\":{\"x\":").Append(head.coord.x.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"z\":").Append(head.coord.z.ToString(CultureInfo.InvariantCulture))
                    .Append("},\"hash\":").Append(JsonString(head.snapshotHash))
                    .Append(",\"revisionId\":").Append(JsonString(head.revisionId)).Append('}');
            }
            builder.Append(']');
            return SlopCityCityForge.Sha256(builder.ToString());
        }

        public static string LocalRevisionId(CityForgeCoord coordinate, long revision)
        {
            if (revision < 1) throw new ArgumentOutOfRangeException("revision");
            string x = coordinate.x < 0 ? "n" + (-((long)coordinate.x)).ToString(CultureInfo.InvariantCulture) : "p" + coordinate.x.ToString(CultureInfo.InvariantCulture);
            string z = coordinate.z < 0 ? "n" + (-((long)coordinate.z)).ToString(CultureInfo.InvariantCulture) : "p" + coordinate.z.ToString(CultureInfo.InvariantCulture);
            return "chunk-" + x + "-" + z + "-r" + revision.ToString(CultureInfo.InvariantCulture);
        }

        private static string RoadsJson(IEnumerable<CityForgeRoad> rows)
        {
            IEnumerable<CityForgeRoad> ordered = (rows ?? Enumerable.Empty<CityForgeRoad>()).Where(row => row != null).OrderBy(row => Edge(row), StringComparer.Ordinal).ThenBy(row => RoadClass(row.roadClass), StringComparer.Ordinal);
            return "[" + string.Join(",", ordered.Select(row => "{\"class\":" + JsonString(RoadClass(row.roadClass)) + ",\"edge\":" + JsonString(Edge(row)) + "}").ToArray()) + "]";
        }

        private static string RemovedRoadsJson(CityForgeChunkPlan plan)
        {
            IEnumerable<string> values = (plan.removedRoadEdges ?? new List<string>())
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal);
            return StringsJson(values);
        }

        private static string ParcelsJson(IEnumerable<CityForgeParcel> rows)
        {
            IEnumerable<CityForgeParcel> ordered = (rows ?? Enumerable.Empty<CityForgeParcel>()).Where(row => row != null).OrderBy(row => row.id, StringComparer.Ordinal);
            return "[" + string.Join(",", ordered.Select(row => "{\"cells\":" + StringsJson((row.cells ?? new List<int>()).Select(Cell).OrderBy(cell => cell, StringComparer.Ordinal)) + ",\"id\":" + JsonString(row.id) + ",\"zone\":" + JsonString(Zone(row.zone)) + "}").ToArray()) + "]";
        }

        private static string BuildingsJson(IEnumerable<CityForgeBuilding> rows)
        {
            IEnumerable<CityForgeBuilding> ordered = (rows ?? Enumerable.Empty<CityForgeBuilding>()).Where(row => row != null).OrderBy(row => row.id, StringComparer.Ordinal);
            return "[" + string.Join(",", ordered.Select(row =>
            {
                StringBuilder item = new StringBuilder("{\"blueprintId\":").Append(JsonString(row.blueprintId)).Append(",\"entranceEdge\":").Append(JsonString(Direction(row.entranceEdge))).Append(",\"facadeId\":").Append(JsonString(row.facadeId))
                    .Append(",\"floors\":").Append(row.floors.ToString(CultureInfo.InvariantCulture)).Append(",\"id\":").Append(JsonString(row.id));
                if (!string.IsNullOrEmpty(row.interiorRole)) item.Append(",\"interiorRole\":").Append(JsonString(row.interiorRole));
                item.Append(",\"parcelId\":").Append(JsonString(row.parcelId)).Append(",\"roofId\":").Append(JsonString(row.roofId));
                if (!string.IsNullOrEmpty(row.storefrontId)) item.Append(",\"storefrontId\":").Append(JsonString(row.storefrontId));
                item.Append('}');
                return item.ToString();
            }).ToArray()) + "]";
        }

        private static string ServicesJson(IEnumerable<CityForgeService> rows)
        {
            IEnumerable<CityForgeService> ordered = (rows ?? Enumerable.Empty<CityForgeService>()).Where(row => row != null).OrderBy(row => row.id, StringComparer.Ordinal);
            return "[" + string.Join(",", ordered.Select(row => "{\"id\":" + JsonString(row.id) + ",\"parcelId\":" + JsonString(row.parcelId) + ",\"type\":" + JsonString(row.type) + "}").ToArray()) + "]";
        }

        private static string ScreensJson(IEnumerable<CityForgeScreen> rows)
        {
            IEnumerable<CityForgeScreen> ordered = (rows ?? Enumerable.Empty<CityForgeScreen>()).Where(row => row != null).OrderBy(row => row.screenId, StringComparer.Ordinal);
            return "[" + string.Join(",", ordered.Select(row => "{\"deviceId\":" + JsonString(row.deviceId) + ",\"hostId\":" + JsonString(row.hostId) + ",\"screenId\":" + JsonString(row.screenId) + ",\"screenMountId\":" + JsonString(row.screenMountId) + "}").ToArray()) + "]";
        }

        private static string LandmarksJson(IEnumerable<CityForgeLandmark> rows)
        {
            IEnumerable<CityForgeLandmark> ordered = (rows ?? Enumerable.Empty<CityForgeLandmark>()).Where(row => row != null).OrderBy(row => row.id, StringComparer.Ordinal);
            return "[" + string.Join(",", ordered.Select(row =>
            {
                StringBuilder item = new StringBuilder("{\"classId\":").Append(JsonString(row.classId)).Append(",\"id\":").Append(JsonString(row.id))
                    .Append(",\"outcomeId\":").Append(JsonString(row.outcomeId)).Append(",\"parcelId\":").Append(JsonString(row.parcelId));
                if (!string.IsNullOrEmpty(row.stableSeed)) item.Append(",\"stableSeed\":").Append(JsonString(row.stableSeed));
                return item.Append(",\"state\":").Append(JsonString(row.state)).Append('}').ToString();
            }).ToArray()) + "]";
        }

        private static string SituationsJson(IEnumerable<CityForgeSituationState> rows)
        {
            IEnumerable<CityForgeSituationState> ordered = (rows ?? Enumerable.Empty<CityForgeSituationState>()).Where(row => row != null).OrderBy(row => row.id, StringComparer.Ordinal);
            return "[" + string.Join(",", ordered.Select(row => "{\"id\":" + JsonString(row.id) + ",\"landmarkId\":" + JsonString(row.landmarkId) + ",\"situationId\":" + JsonString(row.situationId) + ",\"stableSeed\":" + JsonString(row.stableSeed) + ",\"state\":" + JsonString(row.state) + "}").ToArray()) + "]";
        }

        private static string StringsJson(IEnumerable<string> values)
        {
            return "[" + string.Join(",", (values ?? Enumerable.Empty<string>()).Select(JsonString).ToArray()) + "]";
        }

        private static string Cell(int value)
        {
            return (value % SlopCityCityForge.GridSize).ToString(CultureInfo.InvariantCulture) + "," + (value / SlopCityCityForge.GridSize).ToString(CultureInfo.InvariantCulture);
        }

        private static string Edge(CityForgeRoad road) { return road.cellX.ToString(CultureInfo.InvariantCulture) + "," + road.cellZ.ToString(CultureInfo.InvariantCulture) + ":" + Direction(road.direction); }
        private static string Direction(CityForgeDirection value) { return value == CityForgeDirection.North ? "north" : value == CityForgeDirection.East ? "east" : value == CityForgeDirection.South ? "south" : "west"; }
        private static string RoadClass(CityForgeRoadClass value) { return value == CityForgeRoadClass.Boulevard ? "boulevard" : value == CityForgeRoadClass.ServiceAlley ? "service-alley" : value == CityForgeRoadClass.PedestrianRoute ? "pedestrian-route" : value == CityForgeRoadClass.TransitServiceCorridor ? "transit-service-corridor" : "local-street"; }
        private static string Zone(CityForgeZone value) { return value == CityForgeZone.WorkService ? "work-service" : value == CityForgeZone.Trade ? "trade" : value == CityForgeZone.Civic ? "civic" : value == CityForgeZone.Culture ? "culture" : value == CityForgeZone.GreenInfrastructure ? "green-infrastructure" : "home"; }
        private static void JsonProperty(StringBuilder builder, string key, string value, bool first) { if (!first) builder.Append(','); builder.Append(JsonString(key)).Append(':').Append(value); }
        private static string JsonString(string value)
        {
            StringBuilder builder = new StringBuilder("\"");
            foreach (char c in value ?? string.Empty) { if (c == '\\') builder.Append("\\\\"); else if (c == '\"') builder.Append("\\\""); else if (c == '\n') builder.Append("\\n"); else if (c == '\r') builder.Append("\\r"); else if (c == '\t') builder.Append("\\t"); else if (c < 32) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); else builder.Append(c); }
            return builder.Append('\"').ToString();
        }


        private static void AppendRoads(StringBuilder builder, IEnumerable<CityForgeRoad> values)
        {
            builder.Append("roads[");
            foreach (CityForgeRoad value in (values ?? Enumerable.Empty<CityForgeRoad>()).OrderBy(item => item.LocalKey, StringComparer.Ordinal))
                builder.Append(value.cellX).Append(',').Append(value.cellZ).Append(',').Append((int)value.direction).Append(',').Append((int)value.roadClass).Append('|');
            builder.Append("];\n");
        }

        private static void AppendParcels(StringBuilder builder, IEnumerable<CityForgeParcel> values)
        {
            builder.Append("parcels[");
            foreach (CityForgeParcel value in (values ?? Enumerable.Empty<CityForgeParcel>()).OrderBy(item => item.id, StringComparer.Ordinal))
                builder.Append(value.id).Append(',').Append((int)value.zone).Append(',').Append((int)value.entranceEdge).Append(',')
                    .Append(string.Join(",", (value.cells ?? new List<int>()).OrderBy(cell => cell))).Append('|');
            builder.Append("];\n");
        }

        private static void AppendBuildings(StringBuilder builder, IEnumerable<CityForgeBuilding> values)
        {
            builder.Append("buildings[");
            foreach (CityForgeBuilding value in (values ?? Enumerable.Empty<CityForgeBuilding>()).OrderBy(item => item.id, StringComparer.Ordinal))
                builder.Append(value.id).Append(',').Append(value.parcelId).Append(',').Append(value.blueprintId).Append(',').Append(value.facadeId).Append(',').Append(value.roofId)
                    .Append(',').Append(value.floors).Append(',').Append((int)value.entranceEdge).Append('|');
            builder.Append("];\n");
        }

        private static void AppendServices(StringBuilder builder, IEnumerable<CityForgeService> values)
        {
            builder.Append("services[");
            foreach (CityForgeService value in (values ?? Enumerable.Empty<CityForgeService>()).OrderBy(item => item.id, StringComparer.Ordinal))
                builder.Append(value.id).Append(',').Append(value.type).Append(',').Append(value.parcelId).Append('|');
            builder.Append("];\n");
        }

        private static void AppendScreens(StringBuilder builder, IEnumerable<CityForgeScreen> values)
        {
            builder.Append("screens[");
            foreach (CityForgeScreen value in (values ?? Enumerable.Empty<CityForgeScreen>()).OrderBy(item => item.screenId, StringComparer.Ordinal))
                builder.Append(value.screenId).Append(',').Append(value.hostId).Append(',').Append(value.screenMountId).Append(',').Append(value.deviceId).Append('|');
            builder.Append("];\n");
        }

        private static void AppendLandmarks(StringBuilder builder, IEnumerable<CityForgeLandmark> values)
        {
            builder.Append("landmarks[");
            foreach (CityForgeLandmark value in (values ?? Enumerable.Empty<CityForgeLandmark>()).OrderBy(item => item.id, StringComparer.Ordinal))
                builder.Append(value.id).Append(',').Append(value.parcelId).Append(',').Append(value.classId).Append(',').Append(value.outcomeId)
                    .Append(',').Append(value.state).Append(',').Append(value.stableSeed).Append('|');
            builder.Append("];\n");
        }

        private static void AppendSituations(StringBuilder builder, IEnumerable<CityForgeSituationState> values)
        {
            builder.Append("situations[");
            foreach (CityForgeSituationState value in (values ?? Enumerable.Empty<CityForgeSituationState>()).OrderBy(item => item.id, StringComparer.Ordinal))
                builder.Append(value.id).Append(',').Append(value.situationId).Append(',').Append(value.landmarkId).Append(',').Append(value.state).Append(',').Append(value.stableSeed).Append('|');
            builder.Append("];\n");
        }
    }

    /// <summary>
    /// Validates only catalog-bounded, grid-based state. The service repeats this
    /// logic authoritatively; client validation is preview assistance, not trust.
    /// </summary>
    public sealed class SlopCityCityForgeValidator
    {
        private readonly SlopCityCityForgeCatalog catalog;
        private readonly SlopCityCityForgeGenerator generator;
        private static readonly HashSet<string> LandmarkStates = new HashSet<string>(new[] { "unavailable", "discovered", "diagnosing", "chosen", "active", "opening", "established", "paused", "renovating" }, StringComparer.Ordinal);
        private static readonly HashSet<string> SituationStates = new HashSet<string>(new[] { "unavailable", "available", "active", "resolved", "paused", "archived" }, StringComparer.Ordinal);

        public SlopCityCityForgeValidator(SlopCityCityForgeCatalog cityCatalog)
        {
            catalog = cityCatalog ?? throw new ArgumentNullException("cityCatalog");
            generator = new SlopCityCityForgeGenerator(catalog);
        }

        public CityForgeValidation ValidateWorldHead(CityForgeWorldHead head)
        {
            CityForgeValidation validation = new CityForgeValidation();
            if (head == null) { validation.Refuse("This city plan is missing its world information."); return validation; }
            if (head.schemaVersion != SlopCityCityForge.WorldSchemaVersion) validation.Refuse("This city plan needs a compatible game update.");
            if (head.generatorVersion != catalog.GeneratorVersion || !string.Equals(head.catalogHash, catalog.Hash, StringComparison.Ordinal)) validation.Refuse("This city plan uses a different approved catalog.");
            if (!IsWorldSeed(head.worldSeed)) validation.Refuse("This city has an invalid generation seed.");
            if (head.blockSizeCells != SlopCityCityForge.GridSize || head.cellSizeMeters != SlopCityCityForge.CellSizeMeters) validation.Refuse("This city has an unsupported grid.");
            if (head.changedChunks == null || head.changedChunks.Count > 1000000) validation.Refuse("This city index is too large to load safely.");
            else if (head.indexIsComplete)
            {
                HashSet<CityForgeCoord> seen = new HashSet<CityForgeCoord>();
                foreach (CityForgeChunkHead changed in head.changedChunks)
                {
                    if (!SlopCityCityForge.IsBuildable(changed.coord) || !seen.Add(changed.coord)) validation.Refuse("This city index contains an invalid chunk.");
                    if (changed.revision < 1 || !CityForgeIds.IsRuntimeId(changed.revisionId) || !CityForgeIds.IsHash(changed.baseChunkHash) || !CityForgeIds.IsHash(changed.snapshotHash)) validation.Refuse("This city index contains an invalid revision.");
                }
                if (validation.IsValid && !string.Equals(head.chunkMapRoot, SlopCityCityForgeCanonical.ChunkMapRoot(head.changedChunks), StringComparison.Ordinal)) validation.Refuse("This city index no longer matches its accepted chunks.");
            }
            return validation;
        }

        private static bool IsWorldSeed(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 16 || value.Length > 128) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'f') || (c >= '0' && c <= '9'))) return false;
            }
            return true;
        }

        /// <param name="neighbourPlans">Only changed neighbour plans participating in the proposed atomic command.</param>
        public CityForgeValidation ValidateChunkPlan(CityForgeWorldHead head, CityForgeChunkPlan plan, IDictionary<CityForgeCoord, CityForgeChunkPlan> neighbourPlans, bool requireBoundaryPeers = true, IEnumerable<CityForgeLandmark> worldLandmarks = null, bool requireSituationPrerequisites = true, CityForgeOutcomeIndex outcomeIndex = null)
        {
            CityForgeValidation validation = ValidateWorldHead(head);
            if (plan == null) { validation.Refuse("This chunk plan is missing."); return validation; }
            if (!SlopCityCityForge.IsBuildable(plan.coord)) validation.Refuse("The Old City cannot be changed.");
            if (plan.schemaVersion != SlopCityCityForge.ChunkSchemaVersion) validation.Refuse("This chunk plan needs a compatible game update.");
            if (plan.parentChunkRevision < 0 || plan.revision < 0) validation.Refuse("This chunk revision is invalid.");
            if (validation.IsValid || head != null && !string.IsNullOrEmpty(head.worldSeed))
            {
                try
                {
                    CityForgeBaseChunk generated = generator.Generate(head, plan.coord);
                    if (!string.Equals(plan.baseChunkHash, generated.baseHash, StringComparison.Ordinal)) validation.Refuse("This draft was made for a different frontier block. Reload it before building.");
                }
                catch { validation.Refuse("This frontier block cannot be generated safely."); }
            }
            ValidateLimits(plan, validation);
            ValidateRoads(head, plan, neighbourPlans, validation, requireBoundaryPeers);
            Dictionary<string, CityForgeParcel> parcels = ValidateParcels(plan, validation);
            List<CityForgeRoad> effectiveRoads = EffectiveRoads(head, plan);
            Dictionary<string, CityForgeBuilding> buildings = ValidateBuildings(plan, parcels, effectiveRoads, validation);
            ValidateServices(plan, parcels, effectiveRoads, validation);
            ValidateScreens(plan, buildings, validation);
            ValidateLandmarks(plan, parcels, validation);
            ValidateSituations(plan, validation, worldLandmarks, requireSituationPrerequisites, outcomeIndex);
            if (validation.IsValid)
            {
                string packed = SlopCityCityForgeCanonical.Pack(plan);
                if (Encoding.UTF8.GetByteCount(packed) > SlopCityCityForge.MaximumPackedChunkBytes) validation.Refuse("This block has too much detail to save safely.");
                else if (!string.IsNullOrEmpty(plan.packedBlobHash) && !string.Equals(plan.packedBlobHash, SlopCityCityForge.Sha256(packed), StringComparison.Ordinal)) validation.Refuse("This draft changed before it could be accepted. Reload it before building.");
            }
            return validation;
        }

        /// <summary>Mirrors the authored Cityworks/CitySituation state machines in the shared contract.</summary>
        public CityForgeValidation ValidatePlanTransition(CityForgeChunkPlan previous, CityForgeChunkPlan next)
        {
            CityForgeValidation validation = new CityForgeValidation();
            if (next == null) { validation.Refuse("The next City Forge plan is missing."); return validation; }
            Dictionary<string, CityForgeLandmark> oldLandmarks = (previous?.landmarks ?? new List<CityForgeLandmark>()).Where(item => item != null).ToDictionary(item => item.id, item => item, StringComparer.Ordinal);
            foreach (CityForgeLandmark landmark in next.landmarks ?? new List<CityForgeLandmark>())
            {
                if (landmark == null) continue;
                if (oldLandmarks.TryGetValue(landmark.id, out CityForgeLandmark old))
                {
                    if (old.classId != landmark.classId || old.outcomeId != landmark.outcomeId || old.parcelId != landmark.parcelId || !CanTransitionLandmark(old.state, landmark.state)) validation.Refuse("A Cityworks project must keep its identity and follow its authored lifecycle.");
                }
                else if (landmark.state != "unavailable" && landmark.state != "discovered" && landmark.state != "diagnosing" && landmark.state != "chosen") validation.Refuse("A new Cityworks project must begin at an authored state.");
            }
            Dictionary<string, CityForgeSituationState> oldSituations = (previous?.situations ?? new List<CityForgeSituationState>()).Where(item => item != null).ToDictionary(item => item.id, item => item, StringComparer.Ordinal);
            foreach (CityForgeSituationState situation in next.situations ?? new List<CityForgeSituationState>())
            {
                if (situation == null) continue;
                if (oldSituations.TryGetValue(situation.id, out CityForgeSituationState old))
                {
                    if (old.situationId != situation.situationId || old.landmarkId != situation.landmarkId || old.stableSeed != situation.stableSeed || !CanTransitionSituation(old.state, situation.state)) validation.Refuse("A CitySituation must keep its identity and follow its authored lifecycle.");
                }
                else if (situation.state != "unavailable" && situation.state != "available") validation.Refuse("A new CitySituation must begin at an authored state.");
            }
            return validation;
        }

        private static bool CanTransitionLandmark(string from, string to)
        {
            if (from == to) return true;
            if (from == "unavailable") return to == "discovered";
            if (from == "discovered") return to == "diagnosing" || to == "paused";
            if (from == "diagnosing") return to == "chosen" || to == "paused";
            if (from == "chosen") return to == "active" || to == "paused" || to == "renovating";
            if (from == "active") return to == "opening" || to == "paused" || to == "renovating";
            if (from == "opening") return to == "established" || to == "paused" || to == "renovating";
            if (from == "established") return to == "paused" || to == "renovating";
            if (from == "paused") return to == "diagnosing" || to == "chosen" || to == "active" || to == "opening" || to == "established" || to == "renovating";
            return from == "renovating" && (to == "active" || to == "opening" || to == "established" || to == "paused");
        }
        private static bool CanTransitionSituation(string from, string to)
        {
            if (from == to) return true;
            if (from == "unavailable") return to == "available" || to == "archived";
            if (from == "available") return to == "active" || to == "paused" || to == "archived";
            if (from == "active") return to == "resolved" || to == "paused" || to == "archived";
            if (from == "resolved") return to == "archived";
            if (from == "paused") return to == "available" || to == "active" || to == "resolved" || to == "archived";
            return false;
        }

        private static void ValidateLimits(CityForgeChunkPlan plan, CityForgeValidation validation)
        {
            if (plan.roads == null || plan.roads.Count > SlopCityCityForge.MaximumRoadRecords) validation.Refuse("This block has too many street changes.");
            if (plan.removedRoadEdges == null || plan.removedRoadEdges.Count > SlopCityCityForge.MaximumRoadRecords) validation.Refuse("This block has too many removed streets.");
            if (plan.parcels == null || plan.parcels.Count > SlopCityCityForge.MaximumParcelRecords) validation.Refuse("This block has too many lots.");
            if (plan.buildings == null || plan.buildings.Count > SlopCityCityForge.MaximumBuildingRecords) validation.Refuse("This block has too many buildings.");
            if (plan.services == null || plan.services.Count > SlopCityCityForge.MaximumServiceRecords) validation.Refuse("This block has too many services.");
            if (plan.screens == null || plan.screens.Count > SlopCityCityForge.MaximumScreenRecords) validation.Refuse("This block has too many screen records.");
            if (plan.landmarks == null || plan.landmarks.Count > SlopCityCityForge.MaximumLandmarkRecords) validation.Refuse("This block has too many landmark projects.");
            if (plan.situations == null || plan.situations.Count > 96) validation.Refuse("This block has too many active city situations.");
            if (plan.demolishedIds == null || plan.demolishedIds.Count > SlopCityCityForge.MaximumBuildingRecords + SlopCityCityForge.MaximumServiceRecords) validation.Refuse("This block has too many demolition records.");
        }

        private void ValidateRoads(CityForgeWorldHead head, CityForgeChunkPlan plan, IDictionary<CityForgeCoord, CityForgeChunkPlan> neighbourPlans, CityForgeValidation validation, bool requireBoundaryPeers)
        {
            if (plan.roads == null || plan.removedRoadEdges == null) return;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> removed = new HashSet<string>(plan.removedRoadEdges, StringComparer.Ordinal);
            foreach (string edge in removed)
            {
                if (!TryRoadEdge(edge, out int ignoredX, out int ignoredZ, out CityForgeDirection ignoredDirection) || !string.Equals(edge, CanonicalRoadEdge(edge), StringComparison.Ordinal)) validation.Refuse("A removed street must use a canonical local edge.");
                if (!TryRoadEdge(edge, out int removedX, out int removedZ, out CityForgeDirection removedDirection)) continue;
                CityForgeRoad removal = new CityForgeRoad { cellX = removedX, cellZ = removedZ, direction = removedDirection };
                if (!removal.IsBoundary) continue;
                CityForgeCoord neighbour = SlopCityCityForge.Neighbour(plan.coord, removedDirection);
                if (SlopCityCityForge.IsProtectedOldCity(neighbour))
                {
                    validation.Refuse("A player street cannot remove a protected Old City connection.");
                    continue;
                }
                if (neighbourPlans == null || !neighbourPlans.TryGetValue(neighbour, out CityForgeChunkPlan other))
                {
                    if (requireBoundaryPeers) validation.Refuse("A street removal crossing this block edge must be accepted with the neighboring block.");
                    continue;
                }
                if (!HasRemovedReciprocal(other, SlopCityCityForge.Opposite(removedDirection), removal.BoundaryOffset))
                    validation.Refuse("A street removal must match on both sides of the block edge.");
            }
            for (int i = 0; i < plan.roads.Count; i++)
            {
                CityForgeRoad road = plan.roads[i];
                if (road == null || road.cellX < 0 || road.cellX >= SlopCityCityForge.GridSize || road.cellZ < 0 || road.cellZ >= SlopCityCityForge.GridSize
                    || !Enum.IsDefined(typeof(CityForgeDirection), road.direction) || !Enum.IsDefined(typeof(CityForgeRoadClass), road.roadClass) || !seen.Add(road.LocalKey))
                {
                    validation.Refuse("One of these street changes is not on the block grid.");
                    continue;
                }
                if (!string.Equals(RoadEdge(road), CanonicalRoadEdge(RoadEdge(road)), StringComparison.Ordinal)) validation.Refuse("A street must use a canonical local edge.");
                if (removed.Contains(RoadEdge(road))) { validation.Refuse("A street cannot also be removed."); continue; }
                if (!road.IsBoundary) continue;
                CityForgeCoord neighbour = SlopCityCityForge.Neighbour(plan.coord, road.direction);
                if (SlopCityCityForge.IsProtectedOldCity(neighbour))
                {
                    validation.Refuse("A player street cannot cross into the protected Old City.");
                    continue;
                }
                if (neighbourPlans == null || !neighbourPlans.TryGetValue(neighbour, out CityForgeChunkPlan other))
                {
                    if (requireBoundaryPeers) validation.Refuse("A street crossing this block edge must be accepted with the neighboring block.");
                    continue;
                }
                CityForgeRoad reciprocal = FindReciprocal(other, SlopCityCityForge.Opposite(road.direction), road.BoundaryOffset);
                if (reciprocal == null || reciprocal.roadClass != road.roadClass)
                    validation.Refuse("The road, sidewalk, and utility connection must match on both sides of this block edge.");
            }
        }

        private static CityForgeRoad FindReciprocal(CityForgeChunkPlan plan, CityForgeDirection direction, int offset)
        {
            if (plan == null || plan.roads == null) return null;
            for (int i = 0; i < plan.roads.Count; i++)
            {
                CityForgeRoad candidate = plan.roads[i];
                if (candidate != null && candidate.IsBoundary && candidate.direction == direction && candidate.BoundaryOffset == offset) return candidate;
            }
            return null;
        }

        private static bool HasRemovedReciprocal(CityForgeChunkPlan plan, CityForgeDirection direction, int offset)
        {
            if (plan == null) return false;
            foreach (string edge in plan.removedRoadEdges ?? new List<string>())
            {
                if (!TryRoadEdge(edge, out int x, out int z, out CityForgeDirection candidateDirection)) continue;
                CityForgeRoad candidate = new CityForgeRoad { cellX = x, cellZ = z, direction = candidateDirection };
                if (candidate.IsBoundary && candidate.direction == direction && candidate.BoundaryOffset == offset) return true;
            }
            return false;
        }

        private Dictionary<string, CityForgeParcel> ValidateParcels(CityForgeChunkPlan plan, CityForgeValidation validation)
        {
            Dictionary<string, CityForgeParcel> values = new Dictionary<string, CityForgeParcel>(StringComparer.Ordinal);
            HashSet<int> occupied = new HashSet<int>();
            if (plan.parcels == null) return values;
            foreach (CityForgeParcel parcel in plan.parcels)
            {
                if (parcel == null || !CityForgeIds.IsId(parcel.id) || !Enum.IsDefined(typeof(CityForgeZone), parcel.zone) || values.ContainsKey(parcel.id))
                {
                    validation.Refuse("One of these lots has an invalid identity.");
                    continue;
                }
                values.Add(parcel.id, parcel);
                if (parcel.cells == null || parcel.cells.Count == 0 || parcel.cells.Count > SlopCityCityForge.GridSize * SlopCityCityForge.GridSize || parcel.cells.Distinct().Count() != parcel.cells.Count)
                {
                    validation.Refuse("A lot must use one or more unique grid cells.");
                    continue;
                }
                bool cellError = false;
                foreach (int cell in parcel.cells)
                {
                    if (cell < 0 || cell >= SlopCityCityForge.GridSize * SlopCityCityForge.GridSize || !occupied.Add(cell)) cellError = true;
                }
                if (cellError) validation.Refuse("Lots cannot overlap.");
            }
            return values;
        }

        private Dictionary<string, CityForgeBuilding> ValidateBuildings(CityForgeChunkPlan plan, Dictionary<string, CityForgeParcel> parcels, IEnumerable<CityForgeRoad> effectiveRoads, CityForgeValidation validation)
        {
            Dictionary<string, CityForgeBuilding> values = new Dictionary<string, CityForgeBuilding>(StringComparer.Ordinal);
            HashSet<string> occupiedParcels = new HashSet<string>(StringComparer.Ordinal);
            if (plan.buildings == null) return values;
            foreach (CityForgeBuilding building in plan.buildings)
            {
                CityForgeBuildingArchetype archetype;
                if (building == null || !CityForgeIds.IsId(building.id) || values.ContainsKey(building.id) || !parcels.TryGetValue(building.parcelId ?? string.Empty, out CityForgeParcel parcel)
                    || !catalog.TryGetBuilding(building.blueprintId, out archetype) || !catalog.IsFacade(building.facadeId) || !catalog.IsRoof(building.roofId)
                    || !Enum.IsDefined(typeof(CityForgeDirection), building.entranceEdge))
                {
                    validation.Refuse("A building must use an approved lot and building kit.");
                    continue;
                }
                values.Add(building.id, building);
                if (!occupiedParcels.Add(building.parcelId ?? string.Empty) || !catalog.IsBuildingAllowedInZone(building.blueprintId, parcel.zone) || building.floors < 1 || building.floors > 24 || !HasRoadFrontage(effectiveRoads, parcel, building.entranceEdge))
                    validation.Refuse("This building does not fit the selected lot.");
                if (!string.IsNullOrEmpty(building.interiorRole) && !catalog.IsInterior(building.interiorRole)) validation.Refuse("This building needs an approved interior role.");
                if (!string.IsNullOrEmpty(building.storefrontId) && !catalog.IsStorefront(building.storefrontId)) validation.Refuse("This building needs an approved storefront.");
            }
            return values;
        }

        private void ValidateServices(CityForgeChunkPlan plan, Dictionary<string, CityForgeParcel> parcels, IEnumerable<CityForgeRoad> effectiveRoads, CityForgeValidation validation)
        {
            if (plan.services == null) return;
            HashSet<string> values = new HashSet<string>(StringComparer.Ordinal);
            foreach (CityForgeService service in plan.services)
            {
                if (service == null || !CityForgeIds.IsId(service.id) || !values.Add(service.id) || !catalog.IsService(service.type) || !parcels.TryGetValue(service.parcelId ?? string.Empty, out CityForgeParcel parcel) || !HasRoadFrontage(effectiveRoads, parcel, null))
                    validation.Refuse("A service needs an approved type and a valid lot.");
            }
        }

        private void ValidateScreens(CityForgeChunkPlan plan, Dictionary<string, CityForgeBuilding> buildings, CityForgeValidation validation)
        {
            if (plan.screens == null) return;
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, List<CityForgeScreen>> slots = new Dictionary<string, List<CityForgeScreen>>(StringComparer.Ordinal);
            foreach (CityForgeScreen screen in plan.screens)
            {
                if (screen == null || !CityForgeIds.IsId(screen.screenId) || !ids.Add(screen.screenId)
                    || !catalog.TryGetMount(screen.screenMountId, out CityForgeScreenMount mount)
                    || !catalog.IsScreenDevice(screen.deviceId)
                    || !TryScreenHostCapacity(plan, buildings, screen, out int capacity))
                {
                    validation.Refuse("A screen needs an available approved mounting point.");
                    continue;
                }
                string slot = screen.hostId ?? string.Empty;
                if (!slots.TryGetValue(slot, out List<CityForgeScreen> grouped)) { grouped = new List<CityForgeScreen>(); slots.Add(slot, grouped); }
                grouped.Add(screen);
                if (grouped.Count > capacity) validation.Refuse("That physical screen host has no remaining approved fixtures.");
            }
        }

        /// <summary>
        /// Plans deliberately persist no transform or raw slot. A stable sort by
        /// screen ID chooses the physical fixture later; this gate merely bounds
        /// how many approved fixtures a shipped host can expose.
        /// </summary>
        private bool TryScreenHostCapacity(CityForgeChunkPlan plan, Dictionary<string, CityForgeBuilding> buildings, CityForgeScreen screen, out int capacity)
        {
            capacity = 0;
            if (buildings.TryGetValue(screen.hostId ?? string.Empty, out CityForgeBuilding building))
            {
                if (!catalog.TryGetBuilding(building.blueprintId, out CityForgeBuildingArchetype archetype) || !catalog.IsBuildingScreenMount(building.blueprintId, screen.screenMountId)) return false;
                capacity = catalog.GetBuildingScreenSlots(building.blueprintId);
                return capacity > 0;
            }
            if (screen.screenMountId == "plaza-civic-support" && !string.IsNullOrEmpty(screen.hostId) && screen.hostId.StartsWith("site-", StringComparison.Ordinal))
            {
                string parcelId = screen.hostId.Substring(5);
                CityForgeParcel parcel = (plan.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && item.id == parcelId);
                if (!CityForgeIds.IsId(parcelId) || parcel == null) return false;
                capacity = catalog.GetSiteScreenSlots(parcel.zone);
                return capacity > 0;
            }
            if (screen.screenMountId == "street-kiosk-screen" && TryRoadHost(screen.hostId, out string edge))
            {
                CityForgeRoad road = (plan.roads ?? new List<CityForgeRoad>()).FirstOrDefault(item => item != null && RoadEdge(item) == edge);
                if (road == null) return false;
                capacity = catalog.GetRoadScreenSlots(road.roadClass);
                return capacity > 0;
            }
            return false;
        }

        private void ValidateLandmarks(CityForgeChunkPlan plan, Dictionary<string, CityForgeParcel> parcels, CityForgeValidation validation)
        {
            if (plan.landmarks == null) return;
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> occupiedParcels = new HashSet<string>(StringComparer.Ordinal);
            foreach (CityForgeLandmark landmark in plan.landmarks)
            {
                CityForgeLandmarkClass type;
                if (landmark == null || !CityForgeIds.IsId(landmark.id) || !ids.Add(landmark.id)
                    || !parcels.TryGetValue(landmark.parcelId ?? string.Empty, out CityForgeParcel parcel) || !occupiedParcels.Add(landmark.parcelId ?? string.Empty) || !catalog.TryGetLandmark(landmark.classId, out type)
                    || type.outcomes == null || Array.IndexOf(type.outcomes, landmark.outcomeId) < 0 || !LandmarkStates.Contains(landmark.state ?? string.Empty)
                    || type.compatibleZones == null || Array.IndexOf(type.compatibleZones, parcel.zone) < 0
                    || (!string.IsNullOrEmpty(landmark.stableSeed) && !IsWorldSeed(landmark.stableSeed)))
                    validation.Refuse("This landmark needs a compatible built place and a known future.");
            }
        }

        private void ValidateSituations(CityForgeChunkPlan plan, CityForgeValidation validation, IEnumerable<CityForgeLandmark> worldLandmarks, bool requirePrerequisites, CityForgeOutcomeIndex outcomeIndex)
        {
            if (plan.situations == null || plan.situations.Count > 96) { validation.Refuse("This block has too many active city situations."); return; }
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            List<CityForgeLandmark> landmarkRows = (plan.landmarks ?? new List<CityForgeLandmark>()).Where(item => item != null).ToList();
            List<CityForgeLandmark> requirements = (worldLandmarks ?? landmarkRows).Where(item => item != null).ToList();
            HashSet<string> landmarks = new HashSet<string>(landmarkRows.Select(item => item.id), StringComparer.Ordinal);
            HashSet<string> templatePerLandmark = new HashSet<string>(StringComparer.Ordinal);
            foreach (CityForgeSituationState situation in plan.situations)
            {
                CityForgeLandmark anchor = situation == null ? null : landmarkRows.FirstOrDefault(item => string.Equals(item.id, situation.landmarkId, StringComparison.Ordinal));
                bool prerequisitesMet = outcomeIndex != null
                    ? catalog.SituationRequirementsMet(situation, anchor, outcomeIndex)
                    : catalog.SituationRequirementsMet(situation, anchor, requirements);
                if (situation == null || !CityForgeIds.IsId(situation.id) || !catalog.IsSituation(situation.situationId) || !landmarks.Contains(situation.landmarkId ?? string.Empty) || !SituationStates.Contains(situation.state ?? string.Empty) || !IsWorldSeed(situation.stableSeed) || !ids.Add(situation.id) || !templatePerLandmark.Add((situation.landmarkId ?? string.Empty) + "\u0000" + (situation.situationId ?? string.Empty)) || !catalog.SituationAnchorMatches(situation, landmarkRows) || (requirePrerequisites && !prerequisitesMet)) validation.Refuse("A city situation needs its authored outcome and established prerequisites.");
            }
        }

        private static bool IsConnected(IList<int> cells)
        {
            if (cells == null || cells.Count == 0) return false;
            HashSet<int> remaining = new HashSet<int>(cells);
            Queue<int> queue = new Queue<int>();
            int first = cells[0];
            queue.Enqueue(first);
            remaining.Remove(first);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int x = cell % SlopCityCityForge.GridSize;
                int z = cell / SlopCityCityForge.GridSize;
                int[] neighbours = { x > 0 ? cell - 1 : -1, x < SlopCityCityForge.GridSize - 1 ? cell + 1 : -1, z > 0 ? cell - SlopCityCityForge.GridSize : -1, z < SlopCityCityForge.GridSize - 1 ? cell + SlopCityCityForge.GridSize : -1 };
                for (int i = 0; i < neighbours.Length; i++) if (neighbours[i] >= 0 && remaining.Remove(neighbours[i])) queue.Enqueue(neighbours[i]);
            }
            return remaining.Count == 0;
        }

        private List<CityForgeRoad> EffectiveRoads(CityForgeWorldHead head, CityForgeChunkPlan plan)
        {
            Dictionary<string, CityForgeRoad> roads = new Dictionary<string, CityForgeRoad>(StringComparer.Ordinal);
            foreach (string removed in plan.removedRoadEdges ?? new List<string>())
                if (TryRoadEdge(removed, out int x, out int z, out CityForgeDirection direction)) roads.Remove(RoadLocalKey(x, z, direction));
            foreach (CityForgeRoad road in plan.roads ?? new List<CityForgeRoad>())
                if (road != null) roads[road.LocalKey] = road.Clone();
            return roads.Values.ToList();
        }

        private static bool HasRoadFrontage(IEnumerable<CityForgeRoad> roads, CityForgeParcel parcel, CityForgeDirection? requiredDirection)
        {
            if (parcel == null || parcel.cells == null) return false;
            foreach (int cell in parcel.cells)
            {
                int x = cell % SlopCityCityForge.GridSize;
                int z = cell / SlopCityCityForge.GridSize;
                if (requiredDirection.HasValue)
                {
                    if (HasRoadAt(roads, x, z, requiredDirection.Value)) return true;
                }
                else if (HasRoadAt(roads, x, z, CityForgeDirection.North) || HasRoadAt(roads, x, z, CityForgeDirection.East) || HasRoadAt(roads, x, z, CityForgeDirection.South) || HasRoadAt(roads, x, z, CityForgeDirection.West)) return true;
            }
            return false;
        }

        private static bool HasRoadAt(IEnumerable<CityForgeRoad> roads, int x, int z, CityForgeDirection edge)
        {
            if (roads == null) return false;
            string canonical = CanonicalRoadEdge(x.ToString(CultureInfo.InvariantCulture) + "," + z.ToString(CultureInfo.InvariantCulture) + ":" + DirectionName(edge));
            foreach (CityForgeRoad road in roads)
                if (road != null && RoadEdge(road) == canonical) return true;
            return false;
        }

        private static string RoadEdge(CityForgeRoad road) { return road.cellX.ToString(CultureInfo.InvariantCulture) + "," + road.cellZ.ToString(CultureInfo.InvariantCulture) + ":" + DirectionName(road.direction); }
        private static string RoadLocalKey(int x, int z, CityForgeDirection direction) { return x.ToString(CultureInfo.InvariantCulture) + ":" + z.ToString(CultureInfo.InvariantCulture) + ":" + ((int)direction).ToString(CultureInfo.InvariantCulture); }
        private static string DirectionName(CityForgeDirection direction) { return direction == CityForgeDirection.North ? "north" : direction == CityForgeDirection.East ? "east" : direction == CityForgeDirection.South ? "south" : "west"; }
        private static bool TryRoadEdge(string value, out int x, out int z, out CityForgeDirection direction)
        {
            x = z = 0; direction = CityForgeDirection.North;
            if (string.IsNullOrEmpty(value) || value.Length < 9 || value[1] != ',' || value[3] != ':' || value[0] < '0' || value[0] > '8' || value[2] < '0' || value[2] > '8') return false;
            string side = value.Substring(4);
            if (side == "north") direction = CityForgeDirection.North;
            else if (side == "east") direction = CityForgeDirection.East;
            else if (side == "south") direction = CityForgeDirection.South;
            else if (side == "west") direction = CityForgeDirection.West;
            else return false;
            x = value[0] - '0'; z = value[2] - '0'; return true;
        }
        private static string CanonicalRoadEdge(string value)
        {
            if (!TryRoadEdge(value, out int x, out int z, out CityForgeDirection direction)) return string.Empty;
            if (direction == CityForgeDirection.West && x > 0) { x--; direction = CityForgeDirection.East; }
            if (direction == CityForgeDirection.South && z > 0) { z--; direction = CityForgeDirection.North; }
            return x.ToString(CultureInfo.InvariantCulture) + "," + z.ToString(CultureInfo.InvariantCulture) + ":" + DirectionName(direction);
        }
        private static bool TryRoadHost(string host, out string edge)
        {
            edge = null;
            if (string.IsNullOrEmpty(host) || !host.StartsWith("road-", StringComparison.Ordinal)) return false;
            string[] parts = host.Substring(5).Split('-');
            if (parts.Length != 3 || parts[0].Length != 1 || parts[1].Length != 1 || parts[0][0] < '0' || parts[0][0] > '8' || parts[1][0] < '0' || parts[1][0] > '8') return false;
            string direction = parts[2];
            if (direction != "north" && direction != "east" && direction != "south" && direction != "west") return false;
            edge = parts[0] + "," + parts[1] + ":" + direction;
            return true;
        }
    }

    /// <summary>Combines deterministic base data and an already validated sparse delta.</summary>
    public sealed class SlopCityCityForgeResolver
    {
        private readonly SlopCityCityForgeCatalog catalog;
        private readonly SlopCityCityForgeGenerator generator;
        private readonly SlopCityCityForgeValidator validator;

        public SlopCityCityForgeResolver(SlopCityCityForgeCatalog cityCatalog)
        {
            catalog = cityCatalog ?? throw new ArgumentNullException("cityCatalog");
            generator = new SlopCityCityForgeGenerator(catalog);
            validator = new SlopCityCityForgeValidator(catalog);
        }

        public CityForgeResolvedChunk Resolve(CityForgeWorldHead head, CityForgeCoord coord, CityForgeChunkPlan plan, IDictionary<CityForgeCoord, CityForgeChunkPlan> atomicNeighbours = null, IEnumerable<CityForgeLandmark> worldLandmarks = null, CityForgeOutcomeIndex outcomeIndex = null)
        {
            CityForgeBaseChunk baseChunk = generator.Generate(head, coord);
            if (plan == null)
            {
                CityForgeResolvedChunk untouched = new CityForgeResolvedChunk { baseChunk = baseChunk, roads = baseChunk.roads.Select(item => item.Clone()).ToList() };
                untouched.resolvedHash = SlopCityCityForge.Sha256(baseChunk.baseHash + "\nuntouched");
                untouched.pulse = CityForgeCityPulse.Resolve(head, untouched, catalog, worldLandmarks, outcomeIndex);
                return untouched;
            }
            // A streamed server page may not contain every adjacent accepted
            // chunk. The bridge verified its authoritative boundary before this
            // presentation resolver receives it, so missing off-page peers are
            // not treated as a local draft error here.
            CityForgeValidation validation = validator.ValidateChunkPlan(head, plan, atomicNeighbours ?? new Dictionary<CityForgeCoord, CityForgeChunkPlan>(), false, worldLandmarks, worldLandmarks != null || outcomeIndex != null, outcomeIndex);
            if (!validation.IsValid) throw new InvalidOperationException(validation.Messages.FirstOrDefault() ?? "This chunk plan is invalid.");
            Dictionary<string, CityForgeRoad> roads = baseChunk.roads.ToDictionary(item => item.LocalKey, item => item.Clone(), StringComparer.Ordinal);
            foreach (string removed in plan.removedRoadEdges ?? new List<string>())
                if (TryRoadEdge(removed, out int removedX, out int removedZ, out CityForgeDirection removedDirection)) roads.Remove(RoadLocalKey(removedX, removedZ, removedDirection));
            foreach (CityForgeRoad delta in plan.roads)
            {
                roads[delta.LocalKey] = delta.Clone();
            }
            CityForgeResolvedChunk resolved = new CityForgeResolvedChunk
            {
                baseChunk = baseChunk,
                acceptedPlan = plan.Clone(),
                roads = roads.Values.OrderBy(item => item.LocalKey, StringComparer.Ordinal).ToList()
            };
            resolved.screens = ResolveScreens(plan);
            resolved.pulse = CityForgeCityPulse.Resolve(head, resolved, catalog, worldLandmarks, outcomeIndex);
            resolved.resolvedHash = SlopCityCityForge.Sha256(baseChunk.baseHash + "\n" + SlopCityCityForgeCanonical.Hash(plan) + "\n" + string.Join("|", resolved.screens.Select(item => item.screenId).OrderBy(item => item, StringComparer.Ordinal)));
            return resolved;
        }

        private List<CityForgeResolvedScreen> ResolveScreens(CityForgeChunkPlan plan)
        {
            List<CityForgeResolvedScreen> resolved = new List<CityForgeResolvedScreen>();
            // Screen plans contain no player-authored position. The shared
            // protocol assigns fixtures by ordinal screen ID across each host,
            // irrespective of mount choice, so a kit never grows past its
            // reviewed physical screen capacity.
            Dictionary<string, List<CityForgeScreen>> playerByHost = (plan.screens ?? new List<CityForgeScreen>())
                .Where(screen => screen != null)
                .GroupBy(screen => screen.hostId ?? string.Empty, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderBy(screen => screen.screenId, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<CityForgeScreen>> group in playerByHost.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                int capacity = ScreenHostCapacity(plan, group.Key);
                for (int index = 0; index < group.Value.Count && index < capacity; index++)
                {
                    CityForgeScreen screen = group.Value[index];
                    if (!catalog.TryGetMount(screen.screenMountId, out CityForgeScreenMount mount)) continue;
                    resolved.Add(new CityForgeResolvedScreen
                    {
                        screenId = screen.screenId,
                        hostId = screen.hostId,
                        mountId = screen.screenMountId,
                        deviceId = screen.deviceId,
                        screenGroup = mount.screenGroup,
                        slotIndex = index,
                        slotCount = capacity,
                        catalogIntegrated = false
                    });
                }
            }
            foreach (CityForgeBuilding building in plan.buildings ?? new List<CityForgeBuilding>())
            {
                if (!catalog.TryGetBuilding(building.blueprintId, out CityForgeBuildingArchetype archetype)) continue;
                int capacity = Math.Max(1, archetype.screenSlots);
                int claimed = playerByHost.TryGetValue(building.id ?? string.Empty, out List<CityForgeScreen> playerScreens) ? Math.Min(capacity, playerScreens.Count) : 0;
                // Catalog-integrated fixtures occupy only the unclaimed end of
                // the same reviewed physical kit. This prevents a player record
                // and a fallback television from ever owning one surface.
                for (int i = claimed; i < capacity; i++)
                {
                    string fixture = "fixture-" + (i + 1).ToString(CultureInfo.InvariantCulture);
                    resolved.Add(new CityForgeResolvedScreen
                    {
                        screenId = SlopCityCityForge.StableId("screen", plan.coord, building.id + "-" + fixture),
                        hostId = building.id,
                        mountId = "catalog-" + fixture,
                        deviceId = "public-crt-01",
                        screenGroup = archetype.tags != null && archetype.tags.Contains("media") ? "interior" : "street",
                        slotIndex = i,
                        slotCount = capacity,
                        catalogIntegrated = true
                    });
                }
            }
            return resolved.OrderBy(item => item.screenId, StringComparer.Ordinal).ToList();
        }

        private int ScreenHostCapacity(CityForgeChunkPlan plan, string hostId)
        {
            CityForgeBuilding building = (plan.buildings ?? new List<CityForgeBuilding>()).FirstOrDefault(item => item != null && item.id == hostId);
            if (building != null) return catalog.GetBuildingScreenSlots(building.blueprintId);
            if (!string.IsNullOrEmpty(hostId) && hostId.StartsWith("site-", StringComparison.Ordinal))
            {
                string parcelId = hostId.Substring(5);
                CityForgeParcel parcel = (plan.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && item.id == parcelId);
                return parcel == null ? 0 : catalog.GetSiteScreenSlots(parcel.zone);
            }
            if (TryResolvedRoadHost(hostId, out string edge))
            {
                CityForgeRoad road = (plan.roads ?? new List<CityForgeRoad>()).FirstOrDefault(item => item != null && RoadEdge(item) == edge);
                return road == null ? 0 : catalog.GetRoadScreenSlots(road.roadClass);
            }
            return 0;
        }

        private static bool TryResolvedRoadHost(string host, out string edge)
        {
            edge = null;
            if (string.IsNullOrEmpty(host) || !host.StartsWith("road-", StringComparison.Ordinal)) return false;
            string[] parts = host.Substring(5).Split('-');
            if (parts.Length != 3 || parts[0].Length != 1 || parts[1].Length != 1 || parts[0][0] < '0' || parts[0][0] > '8' || parts[1][0] < '0' || parts[1][0] > '8') return false;
            if (parts[2] != "north" && parts[2] != "east" && parts[2] != "south" && parts[2] != "west") return false;
            edge = parts[0] + "," + parts[1] + ":" + parts[2];
            return true;
        }

        private static bool TryRoadEdge(string value, out int x, out int z, out CityForgeDirection direction)
        {
            x = z = 0; direction = CityForgeDirection.North;
            if (string.IsNullOrEmpty(value) || value.Length < 9 || value[1] != ',' || value[3] != ':' || value[0] < '0' || value[0] > '8' || value[2] < '0' || value[2] > '8') return false;
            string side = value.Substring(4);
            if (side == "north") direction = CityForgeDirection.North;
            else if (side == "east") direction = CityForgeDirection.East;
            else if (side == "south") direction = CityForgeDirection.South;
            else if (side == "west") direction = CityForgeDirection.West;
            else return false;
            x = value[0] - '0'; z = value[2] - '0'; return true;
        }
        private static string RoadLocalKey(int x, int z, CityForgeDirection direction) { return x.ToString(CultureInfo.InvariantCulture) + ":" + z.ToString(CultureInfo.InvariantCulture) + ":" + ((int)direction).ToString(CultureInfo.InvariantCulture); }
    }

    /// <summary>Deterministic consequences, not a money or timer system.</summary>
    [Serializable]
    public sealed class CityForgeCityPulse
    {
        public int access;
        public int care;
        public int culture;
        public int pressure;
        public List<string> residentRoles = new List<string>();
        /// <summary>Accepted CitySituation records, retained with their authored lifecycle state.</summary>
        public List<CityForgeSituationState> persistedSituations = new List<CityForgeSituationState>();
        public List<string> availableSituations = new List<string>();
        /// <summary>Derived catalog opportunities that physical use may materialize into accepted state.</summary>
        public List<CityForgeSituationOpportunity> availableSituationOpportunities = new List<CityForgeSituationOpportunity>();

        public static CityForgeCityPulse Resolve(CityForgeWorldHead head, CityForgeResolvedChunk chunk, SlopCityCityForgeCatalog catalog, IEnumerable<CityForgeLandmark> worldLandmarks = null, CityForgeOutcomeIndex outcomeIndex = null)
        {
            CityForgeCityPulse pulse = new CityForgeCityPulse();
            if (chunk == null || chunk.acceptedPlan == null) return pulse;
            foreach (CityForgeBuilding building in chunk.acceptedPlan.buildings ?? new List<CityForgeBuilding>())
            {
                if (!catalog.TryGetBuilding(building.blueprintId, out CityForgeBuildingArchetype archetype)) continue;
                AddTags(pulse, archetype.tags);
                pulse.residentRoles.Add(ResidentRole(head.worldSeed, chunk.baseChunk.coord, building.id, archetype.tags));
            }
            foreach (CityForgeService service in chunk.acceptedPlan.services ?? new List<CityForgeService>())
            {
                if (service.type.Contains("care")) pulse.care++;
                else if (service.type.Contains("culture") || service.type.Contains("screen")) pulse.culture++;
                else pulse.access++;
            }
            List<CityForgeLandmark> landmarks = (chunk.acceptedPlan.landmarks ?? new List<CityForgeLandmark>()).Where(item => item != null).ToList();
            HashSet<string> persistedKeys = new HashSet<string>((chunk.acceptedPlan.situations ?? new List<CityForgeSituationState>()).Where(item => item != null).Select(item => (item.landmarkId ?? string.Empty) + "\u0000" + (item.situationId ?? string.Empty)), StringComparer.Ordinal);
            foreach (CityForgeLandmark landmark in landmarks)
            {
                if (landmark.state == "established" || landmark.state == "opening" || landmark.state == "active")
                {
                    pulse.culture++;
                }
            }
            foreach (CityForgeSituationOpportunity opportunity in catalog.AvailableSituationOpportunities(head, chunk.baseChunk.coord, landmarks, worldLandmarks ?? landmarks, outcomeIndex))
            {
                string key = (opportunity.landmarkId ?? string.Empty) + "\u0000" + (opportunity.situationId ?? string.Empty);
                if (persistedKeys.Contains(key)) continue;
                pulse.availableSituations.Add(opportunity.situationId);
                pulse.availableSituationOpportunities.Add(opportunity);
            }
            foreach (CityForgeSituationState situation in chunk.acceptedPlan.situations ?? new List<CityForgeSituationState>())
            {
                if (situation == null || string.IsNullOrEmpty(situation.situationId)) continue;
                pulse.persistedSituations.Add(situation.Clone());
                if (situation.state == "archived" || situation.state == "unavailable") continue;
                pulse.availableSituations.Add(situation.situationId);
                if (situation.state == "active")
                {
                    if (situation.situationId.Contains("care") || situation.situationId.Contains("water")) pulse.care++;
                    else if (situation.situationId.Contains("culture") || situation.situationId.Contains("screen") || situation.situationId.Contains("archive")) pulse.culture++;
                    else pulse.access++;
                }
                else if (situation.state == "paused") pulse.pressure++;
            }
            pulse.residentRoles = pulse.residentRoles.Distinct().OrderBy(value => value, StringComparer.Ordinal).ToList();
            pulse.persistedSituations = pulse.persistedSituations.OrderBy(value => value.id, StringComparer.Ordinal).ToList();
            pulse.availableSituations = pulse.availableSituations.Distinct().OrderBy(value => value, StringComparer.Ordinal).ToList();
            pulse.availableSituationOpportunities = pulse.availableSituationOpportunities.OrderBy(value => value.landmarkId, StringComparer.Ordinal).ThenBy(value => value.situationId, StringComparer.Ordinal).ToList();
            return pulse;
        }

        private static void AddTags(CityForgeCityPulse pulse, IEnumerable<string> tags)
        {
            foreach (string tag in tags ?? Enumerable.Empty<string>())
            {
                if (tag == "care" || tag == "green") pulse.care++;
                else if (tag == "culture" || tag == "media" || tag == "plaza") pulse.culture++;
                else if (tag == "night") pulse.pressure++;
                else pulse.access++;
            }
        }

        private static string ResidentRole(string seed, CityForgeCoord coord, string buildingId, IEnumerable<string> tags)
        {
            string[] roles = { "practical-technician", "reluctant-tenant", "night-organizer", "skeptical-neighbor", "reliable-supplier", "itinerant-performer", "building-caretaker" };
            string source = (seed ?? string.Empty) + "\n" + coord + "\n" + (buildingId ?? string.Empty) + "\n" + string.Join(",", tags ?? Enumerable.Empty<string>());
            int index = int.Parse(SlopCityCityForge.Sha256(source).Substring(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture) % roles.Length;
            return roles[index];
        }
    }

    /// <summary>
    /// Fixed view-zone admission. Hardware can be plentiful; only winners are
    /// allowed to register with SlopCityCityMedia, so screen count never scales
    /// decoders, retained pictures, audio sources, or request managers.
    /// </summary>
    public static class SlopCityCityForgeMediaAdmission
    {
        public sealed class Candidate
        {
            public string screenId = string.Empty;
            public bool chunkLoaded;
            public bool visible;
            public bool eligible;
            public bool unobstructed;
            public float distance;
            public float facing;
        }

        public static List<Candidate> Admit(IEnumerable<Candidate> candidates, bool constrainedWebGl)
        {
            int capacity = constrainedWebGl ? 1 : 3;
            return (candidates ?? Enumerable.Empty<Candidate>())
                .Where(candidate => candidate != null && candidate.chunkLoaded && candidate.visible && candidate.eligible && candidate.unobstructed && !string.IsNullOrEmpty(candidate.screenId))
                .OrderByDescending(candidate => candidate.facing)
                .ThenBy(candidate => candidate.distance)
                .ThenBy(candidate => candidate.screenId, StringComparer.Ordinal)
                .Take(capacity).ToList();
        }
    }

    /// <summary>Bounded runtime neighbourhood with eviction-before-replacement semantics.</summary>
    public sealed class SlopCityCityForgeStreamingWindow
    {
        private readonly Dictionary<CityForgeCoord, CityForgeResolvedChunk> active = new Dictionary<CityForgeCoord, CityForgeResolvedChunk>();
        private readonly int maximumChunks;
        public IReadOnlyDictionary<CityForgeCoord, CityForgeResolvedChunk> Active { get { return active; } }

        public SlopCityCityForgeStreamingWindow(int maximumActiveChunks = 25)
        {
            if (maximumActiveChunks < 1 || maximumActiveChunks > 49) throw new ArgumentOutOfRangeException("maximumActiveChunks");
            maximumChunks = maximumActiveChunks;
        }

        public List<CityForgeCoord> Reconcile(CityForgeCoord center, IEnumerable<CityForgeResolvedChunk> desired)
        {
            Dictionary<CityForgeCoord, CityForgeResolvedChunk> wanted = (desired ?? Enumerable.Empty<CityForgeResolvedChunk>())
                .Where(chunk => chunk != null && chunk.baseChunk != null)
                .OrderBy(chunk => DistanceSquared(center, chunk.baseChunk.coord))
                .ThenBy(chunk => chunk.baseChunk.coord)
                .Take(maximumChunks)
                .ToDictionary(chunk => chunk.baseChunk.coord, chunk => chunk);
            List<CityForgeCoord> removed = active.Keys.Where(coord => !wanted.ContainsKey(coord)).OrderBy(coord => DistanceSquared(center, coord)).Reverse().ToList();
            // Callers release physical ownership synchronously for every returned
            // coordinate before creating any new geometry, screens, or interiors.
            foreach (CityForgeCoord coord in removed) active.Remove(coord);
            foreach (KeyValuePair<CityForgeCoord, CityForgeResolvedChunk> pair in wanted) active[pair.Key] = pair.Value;
            return removed;
        }

        private static int DistanceSquared(CityForgeCoord a, CityForgeCoord b)
        {
            long dx = a.x - b.x;
            long dz = a.z - b.z;
            long distance = dx * dx + dz * dz;
            return distance > int.MaxValue ? int.MaxValue : (int)distance;
        }
    }

    /// <summary>Private draft undo/redo history. No draft state is visible to a visitor.</summary>
    public sealed class SlopCityCityForgeDraftHistory
    {
        private readonly List<CityForgeChunkPlan> undo = new List<CityForgeChunkPlan>();
        private readonly List<CityForgeChunkPlan> redo = new List<CityForgeChunkPlan>();
        public CityForgeChunkPlan Current { get; private set; }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }

        public SlopCityCityForgeDraftHistory(CityForgeChunkPlan initial)
        {
            Current = initial == null ? null : initial.Clone();
        }

        public void Replace(CityForgeChunkPlan next)
        {
            if (next == null) throw new ArgumentNullException("next");
            if (Current != null) undo.Add(Current.Clone());
            Current = next.Clone();
            redo.Clear();
        }

        public CityForgeChunkPlan Undo()
        {
            if (!CanUndo) return Current == null ? null : Current.Clone();
            if (Current != null) redo.Add(Current.Clone());
            Current = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            return Current.Clone();
        }

        public CityForgeChunkPlan Redo()
        {
            if (!CanRedo) return Current == null ? null : Current.Clone();
            if (Current != null) undo.Add(Current.Clone());
            Current = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            return Current.Clone();
        }
    }
}
