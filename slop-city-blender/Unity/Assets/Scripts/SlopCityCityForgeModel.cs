using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SlopCity
{
    /// <summary>
    /// Stable, grid-only data shared by the offline and authenticated City Forge
    /// resolvers.  Nothing in this file is a Unity scene, prefab, transform, URL,
    /// or player-provided asset.  Runtime presentation is deliberately a separate
    /// concern from an accepted ChunkPlan.
    /// </summary>
    public static class SlopCityCityForge
    {
        public const int WorldSchemaVersion = 2;
        public const int ChunkSchemaVersion = 2;
        // Keep this aligned with contracts/city-forge.mjs. Version 2 adds the
        // fixed Old-City-facing base stubs, so v1 hashes must not be reused.
        public const int GeneratorVersion = 2;
        public const string OldCityConnectionRoadClass = "transit-service-corridor";
        public const int GridSize = 9;
        public const int CellSizeMeters = 8;
        public const int ChunkSizeMeters = GridSize * CellSizeMeters;
        public const int MinimumChunkCoordinate = -1000000000;
        public const int MaximumChunkCoordinate = 1000000000;
        public const int MaximumPackedChunkBytes = 122880;
        public const int MaximumWorldPageBytes = 131072;
        public const int MaximumRoadRecords = 180;
        public const int MaximumParcelRecords = 48;
        public const int MaximumBuildingRecords = 48;
        public const int MaximumServiceRecords = 48;
        // 48 five-slot buildings + 48 civic supports + 180 road hosts. This is
        // a per-chunk parser/streaming ceiling, never a citywide TV quota.
        public const int MaximumScreenRecords = 468;
        public const int MaximumLandmarkRecords = 12;
        /// <summary>13 × 13 is the bounded Build Mode strategic-map window.</summary>
        public const int MaximumStrategicSummaryRecords = 169;
        /// <summary>Thirty reviewed Cityworks outcomes, each at most 12M times in the bounded sparse index.</summary>
        public const int MaximumOutcomeIndexEntries = 30;
        public const long MaximumOutcomeStateCount = 12000000L;

        public static readonly CityForgeCoord CoreMinimum = new CityForgeCoord(-2, -2);
        public static readonly CityForgeCoord CoreMaximum = new CityForgeCoord(1, 1);
        public static readonly CityForgeCoord StarterMinimum = new CityForgeCoord(-3, -3);
        public static readonly CityForgeCoord StarterMaximum = new CityForgeCoord(2, 2);

        public static bool IsCoordinateInRange(CityForgeCoord coordinate)
        {
            return coordinate.x >= MinimumChunkCoordinate && coordinate.x <= MaximumChunkCoordinate
                && coordinate.z >= MinimumChunkCoordinate && coordinate.z <= MaximumChunkCoordinate;
        }

        /// <summary>The immutable 4 × 4 Old City envelope.</summary>
        public static bool IsProtectedOldCity(CityForgeCoord coordinate)
        {
            return coordinate.x >= CoreMinimum.x && coordinate.x <= CoreMaximum.x
                && coordinate.z >= CoreMinimum.z && coordinate.z <= CoreMaximum.z;
        }

        /// <summary>The immediately editable 20-chunk all-sides-and-corners ring.</summary>
        public static bool IsStarterRing(CityForgeCoord coordinate)
        {
            return coordinate.x >= StarterMinimum.x && coordinate.x <= StarterMaximum.x
                && coordinate.z >= StarterMinimum.z && coordinate.z <= StarterMaximum.z
                && !IsProtectedOldCity(coordinate);
        }

        public static bool IsBuildable(CityForgeCoord coordinate)
        {
            return IsCoordinateInRange(coordinate) && !IsProtectedOldCity(coordinate);
        }

        /// <summary>
        /// The canonical representative edge of a starter block's immutable
        /// Old City connection, or null when it does not share a core side.
        /// Corner blocks intentionally have no core-facing boundary.
        /// </summary>
        public static string OldCityConnectionStubEdge(CityForgeCoord coordinate)
        {
            if (coordinate.x == StarterMinimum.x && coordinate.z >= CoreMinimum.z && coordinate.z <= CoreMaximum.z) return "8,0:east";
            if (coordinate.x == StarterMaximum.x && coordinate.z >= CoreMinimum.z && coordinate.z <= CoreMaximum.z) return "0,0:west";
            if (coordinate.z == StarterMinimum.z && coordinate.x >= CoreMinimum.x && coordinate.x <= CoreMaximum.x) return "0,8:north";
            if (coordinate.z == StarterMaximum.z && coordinate.x >= CoreMinimum.x && coordinate.x <= CoreMaximum.x) return "0,0:south";
            return null;
        }

        public static bool IsCardinalNeighbour(CityForgeCoord a, CityForgeCoord b)
        {
            return Math.Abs(a.x - b.x) + Math.Abs(a.z - b.z) == 1;
        }

        public static CityForgeCoord Neighbour(CityForgeCoord coordinate, CityForgeDirection direction)
        {
            switch (direction)
            {
                case CityForgeDirection.North: return new CityForgeCoord(coordinate.x, coordinate.z + 1);
                case CityForgeDirection.East: return new CityForgeCoord(coordinate.x + 1, coordinate.z);
                case CityForgeDirection.South: return new CityForgeCoord(coordinate.x, coordinate.z - 1);
                case CityForgeDirection.West: return new CityForgeCoord(coordinate.x - 1, coordinate.z);
                default: return coordinate;
            }
        }

        public static CityForgeDirection Opposite(CityForgeDirection direction)
        {
            switch (direction)
            {
                case CityForgeDirection.North: return CityForgeDirection.South;
                case CityForgeDirection.East: return CityForgeDirection.West;
                case CityForgeDirection.South: return CityForgeDirection.North;
                case CityForgeDirection.West: return CityForgeDirection.East;
                default: throw new ArgumentOutOfRangeException("direction");
            }
        }

        /// <summary>
        /// A boundary key has exactly one representation from either side.  The
        /// coordinate describes the lower/left owner; offset is in [0, 8].
        /// </summary>
        public static string BoundaryKey(CityForgeCoord coordinate, CityForgeDirection direction, int offset)
        {
            if (offset < 0 || offset >= GridSize) throw new ArgumentOutOfRangeException("offset");
            switch (direction)
            {
                case CityForgeDirection.East:
                    return string.Format(CultureInfo.InvariantCulture, "v:{0}:{1}:{2}", coordinate.x + 1, coordinate.z, offset);
                case CityForgeDirection.West:
                    return string.Format(CultureInfo.InvariantCulture, "v:{0}:{1}:{2}", coordinate.x, coordinate.z, offset);
                case CityForgeDirection.North:
                    return string.Format(CultureInfo.InvariantCulture, "h:{0}:{1}:{2}", coordinate.x, coordinate.z + 1, offset);
                case CityForgeDirection.South:
                    return string.Format(CultureInfo.InvariantCulture, "h:{0}:{1}:{2}", coordinate.x, coordinate.z, offset);
                default:
                    throw new ArgumentOutOfRangeException("direction");
            }
        }

        public static string StableId(string prefix, CityForgeCoord coordinate, string localId)
        {
            if (!CityForgeIds.IsId(prefix) || !CityForgeIds.IsId(localId)) throw new ArgumentException("Stable IDs must be catalog identifiers.");
            // Runtime-derived fixture IDs share the public local-ID grammar: no
            // underscores, paths, or unbounded coordinate text. A digest keeps
            // identity stable across streaming while staying within the shared
            // 64-character public identifier grammar.
            int digestLength = Math.Min(40, CityForgeIds.MaximumPublicIdLength - prefix.Length - 1);
            if (digestLength < 1) throw new ArgumentException("Stable ID prefix leaves no room for a derived digest.");
            return prefix + "-" + Sha256(string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}", prefix, coordinate.x, coordinate.z, localId)).Substring(0, digestLength);
        }

        public static string Sha256(string value)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                StringBuilder builder = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }
    }

    public static class CityForgeIds
    {
        /// <summary>Exact upper bound of the shared <c>[a-z][a-z0-9-]{0,63}</c> grammar.</summary>
        public const int MaximumPublicIdLength = 64;

        /// <summary>Small protocol vector usable by smoke tests without a JSON parser.</summary>
        public static bool HasSharedLengthBoundary
        {
            get
            {
                string maximum = "a" + new string('0', MaximumPublicIdLength - 1);
                return IsId(maximum) && !IsId(maximum + "0");
            }
        }

        public static bool IsId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaximumPublicIdLength) return false;
            if (value[0] < 'a' || value[0] > 'z') return false;
            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) return false;
            }
            return true;
        }

        public static bool IsHash(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'f') || (c >= '0' && c <= '9'))) return false;
            }
            return true;
        }

        /// <summary>Accepted service revision IDs additionally allow underscores (for example <c>chunk_…</c>).</summary>
        public static bool IsRuntimeId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64 || !((value[0] >= 'a' && value[0] <= 'z') || (value[0] >= '0' && value[0] <= '9'))) return false;
            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
            }
            return true;
        }
    }

    [Serializable]
    public struct CityForgeCoord : IEquatable<CityForgeCoord>, IComparable<CityForgeCoord>
    {
        public int x;
        public int z;

        public CityForgeCoord(int chunkX, int chunkZ)
        {
            x = chunkX;
            z = chunkZ;
        }

        public bool Equals(CityForgeCoord other) { return x == other.x && z == other.z; }
        public override bool Equals(object obj) { return obj is CityForgeCoord && Equals((CityForgeCoord)obj); }
        public override int GetHashCode() { unchecked { return x * 486187739 + z; } }
        public int CompareTo(CityForgeCoord other)
        {
            int compare = x.CompareTo(other.x);
            return compare != 0 ? compare : z.CompareTo(other.z);
        }
        public override string ToString() { return x.ToString(CultureInfo.InvariantCulture) + "," + z.ToString(CultureInfo.InvariantCulture); }
    }

    public enum CityForgeDirection { North, East, South, West }
    public enum CityForgeRoadClass { LocalStreet, Boulevard, ServiceAlley, PedestrianRoute, TransitServiceCorridor }
    public enum CityForgeZone { Home, WorkService, Trade, Civic, Culture, GreenInfrastructure }
    public enum CityForgeLandmarkPhase { Unavailable, Discovered, Diagnosing, Chosen, Active, Opening, Established, Paused, Renovating }

    [Serializable]
    public sealed class CityForgeWorldHead
    {
        public int schemaVersion = SlopCityCityForge.WorldSchemaVersion;
        public string catalogHash = string.Empty;
        public string worldSeed = string.Empty;
        public int generatorVersion = SlopCityCityForge.GeneratorVersion;
        public long activeWorldRevision;
        public string chunkMapRoot = string.Empty;
        public int blockSizeCells = SlopCityCityForge.GridSize;
        public int cellSizeMeters = SlopCityCityForge.CellSizeMeters;
        public List<CityForgeChunkHead> changedChunks = new List<CityForgeChunkHead>();
        // A nearby authoritative page intentionally does not materialize the
        // entire sparse index. This local-only flag prevents a partial page
        // from being mistaken for a complete offline World Head snapshot.
        public bool indexIsComplete = true;

        public CityForgeWorldHead Clone()
        {
            CityForgeWorldHead clone = new CityForgeWorldHead();
            clone.schemaVersion = schemaVersion;
            clone.catalogHash = catalogHash;
            clone.worldSeed = worldSeed;
            clone.generatorVersion = generatorVersion;
            clone.activeWorldRevision = activeWorldRevision;
            clone.chunkMapRoot = chunkMapRoot;
            clone.blockSizeCells = blockSizeCells;
            clone.cellSizeMeters = cellSizeMeters;
            clone.changedChunks = changedChunks == null ? new List<CityForgeChunkHead>() : changedChunks.Select(value => value.Clone()).ToList();
            clone.indexIsComplete = indexIsComplete;
            return clone;
        }
    }

    [Serializable]
    public sealed class CityForgeChunkHead
    {
        public CityForgeCoord coord;
        // Opaque, protocol-level revision ID.  It participates in the sparse
        // map root and must never be replaced by a local numeric version.
        public string revisionId = string.Empty;
        public long revision;
        public string baseChunkHash = string.Empty;
        public string snapshotHash = string.Empty;

        public CityForgeChunkHead Clone()
        {
            return new CityForgeChunkHead { coord = coord, revisionId = revisionId, revision = revision, baseChunkHash = baseChunkHash, snapshotHash = snapshotHash };
        }
    }

    /// <summary>
    /// Compact, topology-free representation of one accepted changed chunk for
    /// the distant Build Mode map. It contains only verified revision identity
    /// and bounded catalog record counts; it never carries a plan, transform,
    /// player text, URL, or physical asset.
    /// </summary>
    [Serializable]
    public sealed class CityForgeStrategicChunkSummary
    {
        public CityForgeCoord coord;
        public string revisionId = string.Empty;
        public string snapshotHash = string.Empty;
        public int roadCount;
        public int buildingCount;
        public int serviceCount;
        public int landmarkCount;

        public CityForgeStrategicChunkSummary Clone()
        {
            return new CityForgeStrategicChunkSummary
            {
                coord = coord, revisionId = revisionId, snapshotHash = snapshotHash,
                roadCount = roadCount, buildingCount = buildingCount,
                serviceCount = serviceCount, landmarkCount = landmarkCount
            };
        }
    }

    /// <summary>
    /// One bounded strategic-map response. It is deliberately separate from
    /// WorldHead/ChunkPlan pages so far-away authored blocks can be represented
    /// without evicting nearby accepted topology or causing a physical stream
    /// replacement.
    /// </summary>
    [Serializable]
    public sealed class CityForgeStrategicMap
    {
        public long worldRevision;
        public string chunkMapRoot = string.Empty;
        public CityForgeCoord centre;
        public int radius;
        public List<CityForgeStrategicChunkSummary> chunks = new List<CityForgeStrategicChunkSummary>();

        public CityForgeStrategicMap Clone()
        {
            return new CityForgeStrategicMap
            {
                worldRevision = worldRevision,
                chunkMapRoot = chunkMapRoot,
                centre = centre,
                radius = radius,
                chunks = chunks == null ? new List<CityForgeStrategicChunkSummary>() : chunks.Where(value => value != null).Select(value => value.Clone()).ToList()
            };
        }
    }

    /// <summary>Aggregate live-state counts for one reviewed Cityworks outcome.</summary>
    [Serializable]
    public sealed class CityForgeOutcomePresence
    {
        public string outcomeId = string.Empty;
        public long active;
        public long opening;
        public long established;

        public CityForgeOutcomePresence Clone()
        {
            return new CityForgeOutcomePresence { outcomeId = outcomeId, active = active, opening = opening, established = established };
        }
    }

    /// <summary>
    /// Compact accepted-world outcome index used for CitySituation prerequisites.
    /// It deliberately contains no landmark IDs: those are chunk-local, while
    /// the anchor remains verified against the local accepted ChunkPlan.
    /// </summary>
    [Serializable]
    public sealed class CityForgeOutcomeIndex
    {
        public long worldRevision;
        public string chunkMapRoot = string.Empty;
        public List<CityForgeOutcomePresence> outcomes = new List<CityForgeOutcomePresence>();

        public CityForgeOutcomeIndex Clone()
        {
            return new CityForgeOutcomeIndex
            {
                worldRevision = worldRevision,
                chunkMapRoot = chunkMapRoot,
                outcomes = outcomes == null ? new List<CityForgeOutcomePresence>() : outcomes.Where(value => value != null).Select(value => value.Clone()).ToList()
            };
        }

        public bool HasLiveOutcome(string outcomeId, string state)
        {
            CityForgeOutcomePresence value = (outcomes ?? new List<CityForgeOutcomePresence>()).FirstOrDefault(item => item != null && string.Equals(item.outcomeId, outcomeId, StringComparison.Ordinal));
            if (value == null) return false;
            if (state == "active") return value.active > 0;
            if (state == "opening") return value.opening > 0;
            if (state == "established") return value.established > 0;
            return false;
        }
    }

    [Serializable]
    public sealed class CityForgeChunkPlan
    {
        public int schemaVersion = SlopCityCityForge.ChunkSchemaVersion;
        public CityForgeCoord coord;
        public string baseChunkHash = string.Empty;
        public long parentChunkRevision;
        public long revision;
        // Filled by a hosted authority or derived by the offline sparse store.
        // It is intentionally separate from the monotonically increasing
        // numeric revision because the shared map root commits opaque IDs.
        public string revisionId = string.Empty;
        public List<CityForgeRoad> roads = new List<CityForgeRoad>();
        // Wire-format removals for deterministic base roads. Runtime editor
        // flags are derived into this bounded canonical edge list on save.
        public List<string> removedRoadEdges = new List<string>();
        public List<CityForgeParcel> parcels = new List<CityForgeParcel>();
        public List<CityForgeBuilding> buildings = new List<CityForgeBuilding>();
        public List<CityForgeService> services = new List<CityForgeService>();
        public List<CityForgeScreen> screens = new List<CityForgeScreen>();
        public List<CityForgeLandmark> landmarks = new List<CityForgeLandmark>();
        public List<CityForgeSituationState> situations = new List<CityForgeSituationState>();
        public List<string> demolishedIds = new List<string>();
        public string packedBlobHash = string.Empty;

        public CityForgeChunkPlan Clone()
        {
            CityForgeChunkPlan clone = new CityForgeChunkPlan();
            clone.schemaVersion = schemaVersion;
            clone.coord = coord;
            clone.baseChunkHash = baseChunkHash;
            clone.parentChunkRevision = parentChunkRevision;
            clone.revision = revision;
            clone.revisionId = revisionId;
            clone.roads = roads == null ? new List<CityForgeRoad>() : roads.Select(value => value.Clone()).ToList();
            clone.removedRoadEdges = removedRoadEdges == null ? new List<string>() : new List<string>(removedRoadEdges);
            clone.parcels = parcels == null ? new List<CityForgeParcel>() : parcels.Select(value => value.Clone()).ToList();
            clone.buildings = buildings == null ? new List<CityForgeBuilding>() : buildings.Select(value => value.Clone()).ToList();
            clone.services = services == null ? new List<CityForgeService>() : services.Select(value => value.Clone()).ToList();
            clone.screens = screens == null ? new List<CityForgeScreen>() : screens.Select(value => value.Clone()).ToList();
            clone.landmarks = landmarks == null ? new List<CityForgeLandmark>() : landmarks.Select(value => value.Clone()).ToList();
            clone.situations = situations == null ? new List<CityForgeSituationState>() : situations.Select(value => value.Clone()).ToList();
            clone.demolishedIds = demolishedIds == null ? new List<string>() : new List<string>(demolishedIds);
            clone.packedBlobHash = packedBlobHash;
            return clone;
        }
    }

    [Serializable]
    public sealed class CityForgeRoad
    {
        public int cellX;
        public int cellZ;
        public CityForgeDirection direction;
        public CityForgeRoadClass roadClass;

        public CityForgeRoad Clone() { return new CityForgeRoad { cellX = cellX, cellZ = cellZ, direction = direction, roadClass = roadClass }; }
        public bool IsBoundary
        {
            get
            {
                return (direction == CityForgeDirection.West && cellX == 0)
                    || (direction == CityForgeDirection.East && cellX == SlopCityCityForge.GridSize - 1)
                    || (direction == CityForgeDirection.South && cellZ == 0)
                    || (direction == CityForgeDirection.North && cellZ == SlopCityCityForge.GridSize - 1);
            }
        }
        public int BoundaryOffset
        {
            get { return direction == CityForgeDirection.North || direction == CityForgeDirection.South ? cellX : cellZ; }
        }
        public string LocalKey
        {
            get { return cellX.ToString(CultureInfo.InvariantCulture) + ":" + cellZ.ToString(CultureInfo.InvariantCulture) + ":" + ((int)direction).ToString(CultureInfo.InvariantCulture); }
        }
    }

    [Serializable]
    public sealed class CityForgeParcel
    {
        public string id = string.Empty;
        public CityForgeZone zone;
        public List<int> cells = new List<int>();
        public CityForgeDirection entranceEdge;

        public CityForgeParcel Clone() { return new CityForgeParcel { id = id, zone = zone, cells = cells == null ? new List<int>() : new List<int>(cells), entranceEdge = entranceEdge }; }
    }

    [Serializable]
    public sealed class CityForgeBuilding
    {
        public string id = string.Empty;
        public string parcelId = string.Empty;
        public string blueprintId = string.Empty;
        public string facadeId = string.Empty;
        public string roofId = string.Empty;
        public int floors;
        public CityForgeDirection entranceEdge;
        public string interiorRole = string.Empty;
        public string storefrontId = string.Empty;

        public CityForgeBuilding Clone()
        {
            return new CityForgeBuilding { id = id, parcelId = parcelId, blueprintId = blueprintId, facadeId = facadeId, roofId = roofId, floors = floors, entranceEdge = entranceEdge, interiorRole = interiorRole, storefrontId = storefrontId };
        }
    }

    [Serializable]
    public sealed class CityForgeService
    {
        public string id = string.Empty;
        public string type = string.Empty;
        public string parcelId = string.Empty;

        public CityForgeService Clone() { return new CityForgeService { id = id, type = type, parcelId = parcelId }; }
    }

    /// <summary>Only stable IDs are persisted. All physical behavior comes from the catalog.</summary>
    [Serializable]
    public sealed class CityForgeScreen
    {
        public string screenId = string.Empty;
        public string hostId = string.Empty;
        public string screenMountId = string.Empty;
        public string deviceId = string.Empty;

        public CityForgeScreen Clone() { return new CityForgeScreen { screenId = screenId, hostId = hostId, screenMountId = screenMountId, deviceId = deviceId }; }
    }

    [Serializable]
    public sealed class CityForgeLandmark
    {
        public string id = string.Empty;
        public string parcelId = string.Empty;
        public string classId = string.Empty;
        public string outcomeId = string.Empty;
        // `unavailable`, `discovered`, `diagnosing`, `chosen`, `active`,
        // `opening`, `established`, `paused`, or `renovating`.
        public string state = string.Empty;
        public string stableSeed = string.Empty;

        public CityForgeLandmark Clone() { return new CityForgeLandmark { id = id, parcelId = parcelId, classId = classId, outcomeId = outcomeId, state = state, stableSeed = stableSeed }; }
    }

    [Serializable]
    public sealed class CityForgeSituationState
    {
        public string id = string.Empty;
        public string situationId = string.Empty;
        public string landmarkId = string.Empty;
        public string state = string.Empty;
        public string stableSeed = string.Empty;

        public CityForgeSituationState Clone() { return new CityForgeSituationState { id = id, situationId = situationId, landmarkId = landmarkId, state = state, stableSeed = stableSeed }; }
    }

    /// <summary>
    /// A derived, catalog-approved opportunity that may be materialized as a
    /// persisted CitySituation by physical use. It is not wire state and carries
    /// no text, transform, media, or arbitrary rule input.
    /// </summary>
    [Serializable]
    public sealed class CityForgeSituationOpportunity
    {
        public string situationId = string.Empty;
        public string landmarkId = string.Empty;
        public string stableSeed = string.Empty;

        public CityForgeSituationOpportunity Clone() { return new CityForgeSituationOpportunity { situationId = situationId, landmarkId = landmarkId, stableSeed = stableSeed }; }
    }

    /// <summary>Shipped authored requirements for one bounded CitySituation template.</summary>
    public sealed class CityForgeSituationRequirement
    {
        public string[] anchorOutcomes = Array.Empty<string>();
        public string[] requiredOutcomes = Array.Empty<string>();
        public string[] requiredStates = Array.Empty<string>();
    }

    [Serializable]
    public sealed class CityForgeBuildingArchetype
    {
        public string id = string.Empty;
        public CityForgeZone zone;
        public int minimumFloors;
        public int maximumFloors;
        public int assetCost;
        public int screenSlots;
        public bool heroInteriorEligible;
        public string[] tags = Array.Empty<string>();
    }

    [Serializable]
    public sealed class CityForgeScreenMount
    {
        public string id = string.Empty;
        public string[] compatibleTags = Array.Empty<string>();
        public int assetCost;
        public string screenGroup = string.Empty;
    }

    [Serializable]
    public sealed class CityForgeLandmarkClass
    {
        public string id = string.Empty;
        public CityForgeZone[] compatibleZones = Array.Empty<CityForgeZone>();
        public string[] outcomes = Array.Empty<string>();
    }
}
