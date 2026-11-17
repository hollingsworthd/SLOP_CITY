using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace SlopCity
{
    /// <summary>Logical travel only; a mode never fabricates a vehicle or station.</summary>
    public enum SlopCitySprawlTravelMode { Walk, Car, Subway }
    public enum SlopCitySprawlRoutinePhase { Home, HomeToWork, Work, WorkToFood, Food, FoodToWork, WorkToLeisure, Leisure, LeisureToHome }
    public enum SlopCitySprawlRouteStageKind { LeaveDoor, WalkStreet, BoardCar, DriveStreet, ParkCar, WalkToStation, BoardSubway, RideSubway, ExitSubway, WalkFromStation, ArriveDoor }
    public enum SlopCitySprawlPlaceKind { Building, Service, Landmark, TransitStop }

    /// <summary>
    /// A physical transit owner supplies these after it has verified a built
    /// landing and a grounded line. A transit road alone is never a station.
    /// </summary>
    [Serializable]
    public sealed class SlopCitySprawlTransitStop
    {
        public string id = string.Empty;
        public string lineId = string.Empty;
        public CityForgeCoord coord;
        public int cell = -1;
        public SlopCitySprawlTransitStop Clone() => new SlopCitySprawlTransitStop { id = id ?? string.Empty, lineId = lineId ?? string.Empty, coord = coord, cell = cell };
    }

    public sealed class SlopCitySprawlPlace
    {
        public string id = string.Empty;
        public SlopCitySprawlPlaceKind kind;
        public CityForgeCoord coord;
        public int cell;
        public int routeIndex;
        public int routeNode;
        /// <summary>Accepted sidewalk/door-side position, never a water coordinate.</summary>
        public Vector3 sidewalkPosition;
    }

    public sealed class SlopCitySprawlRouteStage
    {
        public SlopCitySprawlRouteStageKind kind;
        public Vector3 from;
        public Vector3 to;
        /// <summary>Indices in the commute route's accepted sidewalk graph.</summary>
        public int[] streetNodes = Array.Empty<int>();
        /// <summary>World points resolved only from <see cref="streetNodes"/>.</summary>
        public Vector3[] sidewalkPoints = Array.Empty<Vector3>();
    }

    /// <summary>Small presenter-facing description of one virtual resident's current trip.</summary>
    public sealed class SlopCitySprawlCommute
    {
        public string virtualResidentId = string.Empty;
        public SlopCitySprawlTravelMode mode;
        public SlopCitySprawlPlace from;
        public SlopCitySprawlPlace to;
        public int routeIndex;
        public int[] streetNodes = Array.Empty<int>();
        public SlopCitySprawlRouteStage[] routeStages = Array.Empty<SlopCitySprawlRouteStage>();
        public float progress01;
        public bool isTraveling;
    }

    /// <summary>Stable places and exact paths for a virtual Sprawl resident.</summary>
    public sealed class SlopCitySprawlRoutine
    {
        public string virtualResidentId = string.Empty;
        public bool ladyInRed;
        public SlopCitySprawlPlace home, work, food, leisure;
        public SlopCitySprawlCommute homeToWork, workToFood, foodToWork, workToLeisure, leisureToHome;

        public SlopCitySprawlRoutinePhase PhaseAt(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            if (hour < 7f) return SlopCitySprawlRoutinePhase.Home;
            if (hour < 9f) return SlopCitySprawlRoutinePhase.HomeToWork;
            if (hour < 12f) return SlopCitySprawlRoutinePhase.Work;
            if (hour < 13f) return SlopCitySprawlRoutinePhase.WorkToFood;
            if (hour < 14f) return SlopCitySprawlRoutinePhase.Food;
            if (hour < 17f) return SlopCitySprawlRoutinePhase.FoodToWork;
            if (hour < 19f) return SlopCitySprawlRoutinePhase.WorkToLeisure;
            if (hour < 22f) return SlopCitySprawlRoutinePhase.Leisure;
            return SlopCitySprawlRoutinePhase.LeisureToHome;
        }

        public SlopCitySprawlPlace PlaceAt(float hour)
        {
            switch (PhaseAt(hour))
            {
                case SlopCitySprawlRoutinePhase.Home:
                case SlopCitySprawlRoutinePhase.LeisureToHome: return home;
                case SlopCitySprawlRoutinePhase.HomeToWork:
                case SlopCitySprawlRoutinePhase.Work:
                case SlopCitySprawlRoutinePhase.FoodToWork: return work;
                case SlopCitySprawlRoutinePhase.WorkToFood:
                case SlopCitySprawlRoutinePhase.Food: return food;
                default: return leisure;
            }
        }

        public SlopCitySprawlCommute CommuteAt(float hour)
        {
            switch (PhaseAt(hour))
            {
                case SlopCitySprawlRoutinePhase.HomeToWork: return homeToWork;
                case SlopCitySprawlRoutinePhase.WorkToFood: return workToFood;
                case SlopCitySprawlRoutinePhase.FoodToWork: return foodToWork;
                case SlopCitySprawlRoutinePhase.WorkToLeisure: return workToLeisure;
                case SlopCitySprawlRoutinePhase.LeisureToHome: return leisureToHome;
                default: return null;
            }
        }

        public float PhaseProgress(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            if (hour >= 7f && hour < 9f) return (hour - 7f) * .5f;
            if (hour >= 12f && hour < 13f) return hour - 12f;
            if (hour >= 14f && hour < 17f) return (hour - 14f) / 3f;
            if (hour >= 17f && hour < 19f) return (hour - 17f) * .5f;
            if (hour >= 22f) return (hour - 22f) * .5f;
            return 1f;
        }
    }

    /// <summary>
    /// Sparse City Forge census. It stores one row per accepted street-connected
    /// ground cell, not one row per NPC, so it does not inherit the legacy
    /// 5,000-resident registry ceiling. The caller must provide a complete
    /// accepted snapshot for a whole-city census; a nearby stream page is not a
    /// substitute for the authoritative sparse index.
    /// </summary>
    public sealed class SlopCitySprawlPopulationCensus
    {
        public const int ReferenceBlocks = 9;
        public const int ReferenceSmiths = 1984;
        public const int ReferenceLadiesInRed = 42;
        public const int GroundCellSquareMeters = SlopCityCityForge.CellSizeMeters * SlopCityCityForge.CellSizeMeters;
        public const int ReferenceGroundSquareMeters = ReferenceBlocks * SlopCityCityForge.ChunkSizeMeters * SlopCityCityForge.ChunkSizeMeters;
        public const int ReferenceGroundCells = ReferenceGroundSquareMeters / GroundCellSquareMeters;
        public const int ChunkGroundCells = SlopCityCityForge.GridSize * SlopCityCityForge.GridSize;
        public const int MaximumNearbyCommutes = 128;
        public const int MaximumNearbyCandidateCells = 24;
        public const int MaximumRoutineSamplesPerQuery = 48;
        public const float MaximumNearbyRadiusMeters = 160f;
        private const int MaximumRoutineCache = 256;

        private readonly Dictionary<CellKey, GroundCell> cells = new Dictionary<CellKey, GroundCell>();
        private readonly List<GroundCell> routedCells = new List<GroundCell>();
        private readonly List<GroundCell> materializableCells = new List<GroundCell>();
        // Fixed local lookup: commute presentation must never linearly scan the
        // whole island merely because the player is standing in one neighborhood.
        private readonly Dictionary<CityForgeCoord, List<GroundCell>> materializableByChunk = new Dictionary<CityForgeCoord, List<GroundCell>>();
        private readonly List<Component> components = new List<Component>();
        private readonly List<SlopCityCrowdRoute> routes = new List<SlopCityCrowdRoute>();
        private readonly List<SlopCitySprawlTransitStop> transitStops = new List<SlopCitySprawlTransitStop>();
        // Routine construction performs several graph searches. Keep only a small
        // deterministic cache so nearby visual refreshes do not repeat that work
        // for the same virtual residents each frame.
        private readonly Dictionary<string, SlopCitySprawlRoutine> routineCache = new Dictionary<string, SlopCitySprawlRoutine>(StringComparer.Ordinal);
        private readonly Queue<string> routineCacheOrder = new Queue<string>();
        private Dictionary<EdgeKey, int> nodeByEdge;
        private List<StreetNode> nodes;
        private long acceptedGroundCells, logicalSmiths, logicalLadies, deferredStreetlessSmiths, deferredStreetlessLadies;

        public long AcceptedGroundCells => acceptedGroundCells;
        public long AcceptedGroundSquareMeters => acceptedGroundCells * GroundCellSquareMeters;
        public long StreetConnectedGroundCells => routedCells.Count;
        public long StreetConnectedGroundSquareMeters => (long)routedCells.Count * GroundCellSquareMeters;
        public long MaterializableGroundCells => materializableCells.Count;
        public long LogicalSmithCount => logicalSmiths;
        public long LogicalLadyInRedCount => logicalLadies;
        public long LogicalPopulationCount => logicalSmiths + logicalLadies;
        /// <summary>Accepted ground contributes density even before its first street exists; these remain virtual until a grounded street component is built.</summary>
        public long DeferredStreetlessSmithCount => deferredStreetlessSmiths;
        public long DeferredStreetlessLadyInRedCount => deferredStreetlessLadies;
        public IReadOnlyList<SlopCityCrowdRoute> Routes => routes;
        public IReadOnlyList<SlopCitySprawlTransitStop> TransitStops => transitStops;

        private SlopCitySprawlPopulationCensus() { }

        public static SlopCitySprawlPopulationCensus Build(IEnumerable<CityForgeChunkPlan> acceptedPlans, IEnumerable<SlopCitySprawlTransitStop> verifiedTransitStops = null)
        {
            var census = new SlopCitySprawlPopulationCensus();
            List<CityForgeChunkPlan> plans = (acceptedPlans ?? Enumerable.Empty<CityForgeChunkPlan>())
                .Where(plan => plan != null && SlopCityCityForge.IsBuildable(plan.coord))
                .GroupBy(plan => plan.coord).Select(group => group.OrderByDescending(plan => plan.revision).First())
                .OrderBy(plan => plan.coord).ToList();
            census.CollectGround(plans);
            census.BuildStreetTopology(plans);
            census.BuildPlaces(plans);
            census.AttachTransitStops(verifiedTransitStops);
            census.AllocateVirtualResidents();
            return census;
        }

        private void CollectGround(IEnumerable<CityForgeChunkPlan> plans)
        {
            foreach (CityForgeChunkPlan plan in plans)
                foreach (int cell in (plan.groundCells ?? new List<int>()).Where(value => value >= 0 && value < ChunkGroundCells).Distinct().OrderBy(value => value))
                {
                    CellKey key = KeyFor(plan.coord, cell);
                    if (cells.ContainsKey(key)) continue;
                    cells.Add(key, new GroundCell { key = key, coord = plan.coord, localCell = cell });
                    acceptedGroundCells++;
                }
        }

        private void BuildStreetTopology(IEnumerable<CityForgeChunkPlan> plans)
        {
            var records = new Dictionary<EdgeKey, List<RoadRecord>>();
            foreach (CityForgeChunkPlan plan in plans)
                foreach (CityForgeRoad road in (plan.roads ?? new List<CityForgeRoad>()).Where(value => value != null).OrderBy(value => value.LocalKey, StringComparer.Ordinal))
                {
                    if (road.cellX < 0 || road.cellX >= SlopCityCityForge.GridSize || road.cellZ < 0 || road.cellZ >= SlopCityCityForge.GridSize) continue;
                    CellKey first = KeyFor(plan.coord, road.cellX + road.cellZ * SlopCityCityForge.GridSize);
                    CellKey second = Neighbour(first, road.direction);
                    // No accepted ground on either side means water/void. A lone
                    // cross-boundary record also cannot route into an unloaded
                    // neighbor or the protected Old City.
                    if (!cells.ContainsKey(first) || !cells.ContainsKey(second)) continue;
                    EdgeKey edge = new EdgeKey(first, second);
                    if (!records.TryGetValue(edge, out List<RoadRecord> values)) { values = new List<RoadRecord>(); records.Add(edge, values); }
                    values.Add(new RoadRecord { roadClass = road.roadClass, crossesChunk = road.IsBoundary });
                }
            nodes = new List<StreetNode>(); nodeByEdge = new Dictionary<EdgeKey, int>();
            foreach (KeyValuePair<EdgeKey, List<RoadRecord>> pair in records.OrderBy(pair => pair.Key))
            {
                if (pair.Value.Any(value => value.crossesChunk) && pair.Value.Count < 2) continue;
                int index = nodes.Count;
                nodes.Add(new StreetNode { edge = pair.Key, roadClass = pair.Value.Select(value => value.roadClass).OrderBy(value => (int)value).First(), position = EdgePosition(pair.Key) });
                nodeByEdge.Add(pair.Key, index);
                cells[pair.Key.first].streetNodes.Add(index);
                cells[pair.Key.second].streetNodes.Add(index);
            }
            foreach (GroundCell cell in cells.Values)
            {
                cell.streetNodes.Sort();
                for (int a = 0; a < cell.streetNodes.Count; a++) for (int b = a + 1; b < cell.streetNodes.Count; b++)
                { nodes[cell.streetNodes[a]].neighbours.Add(cell.streetNodes[b]); nodes[cell.streetNodes[b]].neighbours.Add(cell.streetNodes[a]); }
            }
            BuildComponents();
        }

        private void BuildComponents()
        {
            var seen = new bool[nodes.Count];
            for (int start = 0; start < nodes.Count; start++)
            {
                if (seen[start]) continue;
                var component = new Component { routeIndex = routes.Count };
                var queue = new Queue<int>(); queue.Enqueue(start); seen[start] = true;
                while (queue.Count > 0)
                {
                    int node = queue.Dequeue(); component.globalNodes.Add(node);
                    foreach (int next in nodes[node].neighbours) if (!seen[next]) { seen[next] = true; queue.Enqueue(next); }
                }
                component.globalNodes.Sort((left, right) => nodes[left].edge.CompareTo(nodes[right].edge));
                int componentIndex = components.Count;
                for (int local = 0; local < component.globalNodes.Count; local++)
                {
                    StreetNode node = nodes[component.globalNodes[local]]; node.component = componentIndex; node.routeNode = local;
                    if (node.roadClass != CityForgeRoadClass.PedestrianRoute) component.hasVehicleRoad = true;
                }
                var routeNodes = new SlopCityCrowdNode[component.globalNodes.Count];
                for (int local = 0; local < component.globalNodes.Count; local++)
                {
                    StreetNode source = nodes[component.globalNodes[local]];
                    routeNodes[local] = new SlopCityCrowdNode { position = source.position, neighbors = source.neighbours.Where(next => nodes[next].component == componentIndex).Select(next => nodes[next].routeNode).Distinct().OrderBy(next => next).ToArray() };
                }
                CellKey key = nodes[component.globalNodes[0]].edge.first;
                CityForgeCoord coord = cells[key].coord;
                routes.Add(new SlopCityCrowdRoute { label = "Sprawl sidewalks " + coord.x + "," + coord.z, districtIndex = 1000 + component.routeIndex, nodes = routeNodes });
                components.Add(component);
            }
            foreach (GroundCell cell in cells.Values)
            {
                if (cell.streetNodes.Count == 0) continue;
                StreetNode node = nodes[cell.streetNodes[0]];
                cell.component = node.component; cell.routeNode = node.routeNode; cell.anchor = node.position;
                components[cell.component].cells.Add(cell); routedCells.Add(cell);
            }
            routedCells.Sort((left, right) => left.key.CompareTo(right.key));
            foreach (Component component in components) component.cells.Sort((left, right) => left.key.CompareTo(right.key));
        }

        private void BuildPlaces(IEnumerable<CityForgeChunkPlan> plans)
        {
            foreach (CityForgeChunkPlan plan in plans)
            {
                var parcels = (plan.parcels ?? new List<CityForgeParcel>()).Where(value => value != null)
                    .GroupBy(value => value.id ?? string.Empty).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                foreach (CityForgeBuilding building in (plan.buildings ?? new List<CityForgeBuilding>()).Where(value => value != null).OrderBy(value => value.id, StringComparer.Ordinal))
                    if (parcels.TryGetValue(building.parcelId ?? string.Empty, out CityForgeParcel parcel) && TryFindParcelNode(plan.coord, parcel, building.entranceEdge, out int node)) AddBuilding(plan.coord, parcel, building, node);
                foreach (CityForgeService service in (plan.services ?? new List<CityForgeService>()).Where(value => value != null).OrderBy(value => value.id, StringComparer.Ordinal))
                    if (parcels.TryGetValue(service.parcelId ?? string.Empty, out CityForgeParcel parcel) && TryFindParcelNode(plan.coord, parcel, null, out int node)) AddService(plan.coord, parcel, service, node);
                foreach (CityForgeLandmark landmark in (plan.landmarks ?? new List<CityForgeLandmark>()).Where(value => value != null).OrderBy(value => value.id, StringComparer.Ordinal))
                    if (parcels.TryGetValue(landmark.parcelId ?? string.Empty, out CityForgeParcel parcel) && TryFindParcelNode(plan.coord, parcel, null, out int node)) AddLandmark(plan.coord, parcel, landmark, node);
            }
            foreach (Component component in components) FinalizePlaces(component);
        }

        private bool TryFindParcelNode(CityForgeCoord coord, CityForgeParcel parcel, CityForgeDirection? preferred, out int node)
        {
            node = -1;
            if (parcel == null || parcel.cells == null) return false;
            List<int> localCells = parcel.cells.Where(value => value >= 0 && value < ChunkGroundCells).Distinct().OrderBy(value => value).ToList();
            if (preferred.HasValue)
                foreach (int local in localCells)
                {
                    CellKey first = KeyFor(coord, local);
                    if (nodeByEdge.TryGetValue(new EdgeKey(first, Neighbour(first, preferred.Value)), out node)) return true;
                }
            foreach (int local in localCells)
            {
                if (!cells.TryGetValue(KeyFor(coord, local), out GroundCell ground) || ground.streetNodes.Count == 0) continue;
                node = ground.streetNodes[0]; return true;
            }
            return false;
        }

        private void AddBuilding(CityForgeCoord coord, CityForgeParcel parcel, CityForgeBuilding building, int globalNode)
        {
            StreetNode node = nodes[globalNode]; Component component = components[node.component];
            SlopCitySprawlPlace place = Place("building:" + coord.x + ":" + coord.z + ":" + (building.id ?? string.Empty), SlopCitySprawlPlaceKind.Building, coord, FirstCell(parcel), component.routeIndex, node.routeNode, node.position);
            component.built.Add(place);
            if (parcel.zone == CityForgeZone.Home) component.homes.Add(place);
            if (parcel.zone == CityForgeZone.WorkService || parcel.zone == CityForgeZone.Trade || parcel.zone == CityForgeZone.Civic) component.work.Add(place);
            if (parcel.zone == CityForgeZone.Trade || FoodBuilding(building)) component.food.Add(place);
            if (parcel.zone == CityForgeZone.Culture || parcel.zone == CityForgeZone.Civic || LeisureBuilding(building)) component.leisure.Add(place);
        }

        private void AddService(CityForgeCoord coord, CityForgeParcel parcel, CityForgeService service, int globalNode)
        {
            StreetNode node = nodes[globalNode]; Component component = components[node.component];
            SlopCitySprawlPlace place = Place("service:" + coord.x + ":" + coord.z + ":" + (service.id ?? string.Empty), SlopCitySprawlPlaceKind.Service, coord, FirstCell(parcel), component.routeIndex, node.routeNode, node.position);
            component.built.Add(place); component.work.Add(place);
            if (FoodService(service.type)) component.food.Add(place);
            if (LeisureService(service.type)) component.leisure.Add(place);
        }

        private void AddLandmark(CityForgeCoord coord, CityForgeParcel parcel, CityForgeLandmark landmark, int globalNode)
        {
            StreetNode node = nodes[globalNode]; Component component = components[node.component];
            SlopCitySprawlPlace place = Place("landmark:" + coord.x + ":" + coord.z + ":" + (landmark.id ?? string.Empty), SlopCitySprawlPlaceKind.Landmark, coord, FirstCell(parcel), component.routeIndex, node.routeNode, node.position);
            component.built.Add(place); component.leisure.Add(place);
            if (landmark.classId == "home-workroom" || landmark.classId == "back-lobby") component.homes.Add(place);
            if (landmark.classId == "service-court" || landmark.classId == "tradefront") component.work.Add(place);
        }

        private void FinalizePlaces(Component component)
        {
            // A bare pavement cell is not a fabricated home, shop, job, or
            // venue. A component without a physical built place remains
            // logical/deferred until the player builds one.
            component.homeChoices = FirstNonEmpty(component.homes, component.built);
            component.workChoices = FirstNonEmpty(component.work, component.built, component.homeChoices);
            component.foodChoices = FirstNonEmpty(component.food, component.built, component.workChoices, component.homeChoices);
            component.leisureChoices = FirstNonEmpty(component.leisure, component.built, component.foodChoices, component.homeChoices);
            if(component.homeChoices.Count>0&&component.workChoices.Count>0&&component.foodChoices.Count>0&&component.leisureChoices.Count>0)
                materializableCells.AddRange(component.cells);
        }

        private void AttachTransitStops(IEnumerable<SlopCitySprawlTransitStop> verifiedStops)
        {
            foreach (SlopCitySprawlTransitStop source in (verifiedStops ?? Enumerable.Empty<SlopCitySprawlTransitStop>()).Where(value => value != null && !string.IsNullOrEmpty(value.id) && !string.IsNullOrEmpty(value.lineId)).OrderBy(value => value.id, StringComparer.Ordinal))
            {
                if (source.cell < 0 || source.cell >= ChunkGroundCells || !cells.TryGetValue(KeyFor(source.coord, source.cell), out GroundCell cell) || cell.component < 0) continue;
                SlopCitySprawlTransitStop copy = source.Clone(); transitStops.Add(copy);
                components[cell.component].stops.Add(new TransitStop { source = copy, routeNode = cell.routeNode, position = cell.anchor });
            }
        }

        private void AllocateVirtualResidents()
        {
            // 9 full chunks contain 729 eight-metre cells: 46,656 m². Allocate
            // the exact quotient against that denominator, then use deterministic
            // per-cell ranked bonus slots for the fractional people. The census
            // remains one cell record, not a growing NPC array.
            BuildMaterializableSpatialIndex();
            logicalSmiths = acceptedGroundCells * ReferenceSmiths / ReferenceGroundCells;
            logicalLadies = acceptedGroundCells * ReferenceLadiesInRed / ReferenceGroundCells;
            foreach (GroundCell cell in cells.Values) { cell.smithCount = 0; cell.ladyCount = 0; }
            if (materializableCells.Count == 0)
            {
                deferredStreetlessSmiths = logicalSmiths; deferredStreetlessLadies = logicalLadies;
                return;
            }
            // The area quota is deliberately based on every accepted island cell;
            // physical allocation is then confined to real sidewalk components.
            // That keeps an incomplete streetless plot out of water/void while a
            // newly connected street can expose its already-accounted census.
            long smithBase = logicalSmiths / materializableCells.Count;
            int smithBonus = (int)(logicalSmiths % materializableCells.Count);
            foreach (GroundCell cell in materializableCells) cell.smithCount = smithBase;
            foreach (GroundCell cell in Ranked("smith").Take(smithBonus)) cell.smithCount++;
            long ladyBase = logicalLadies / materializableCells.Count;
            int ladyBonus = (int)(logicalLadies % materializableCells.Count);
            foreach (GroundCell cell in materializableCells) cell.ladyCount = ladyBase;
            foreach (GroundCell cell in Ranked("red").Take(ladyBonus)) cell.ladyCount++;
        }

        private IEnumerable<GroundCell> Ranked(string salt)
        {
            return materializableCells.OrderBy(cell => StableHash(salt + ":" + cell.key.x + ":" + cell.key.z)).ThenBy(cell => cell.key);
        }

        public bool TryGetRoutine(string virtualResidentId, out SlopCitySprawlRoutine routine)
        {
            routine = null;
            if (!TryParseId(virtualResidentId, out CellKey key, out bool red, out long slot) || !cells.TryGetValue(key, out GroundCell cell) || cell.component < 0) return false;
            if (red ? slot < 0 || slot >= cell.ladyCount : slot < 0 || slot >= cell.smithCount) return false;
            if (routineCache.TryGetValue(virtualResidentId, out routine)) return true;
            Component component = components[cell.component];
            SlopCitySprawlPlace home = Pick(component.homeChoices, virtualResidentId, "home", null);
            SlopCitySprawlPlace work = Pick(component.workChoices, virtualResidentId, "work", home);
            SlopCitySprawlPlace food = Pick(component.foodChoices, virtualResidentId, "food", work);
            SlopCitySprawlPlace leisure = Pick(component.leisureChoices, virtualResidentId, "leisure", food);
            if (home == null || work == null || food == null || leisure == null) return false;
            routine = new SlopCitySprawlRoutine
            {
                virtualResidentId = virtualResidentId, ladyInRed = red, home = home, work = work, food = food, leisure = leisure,
                homeToWork = BuildCommute(component, virtualResidentId, home, work),
                workToFood = BuildCommute(component, virtualResidentId, work, food),
                foodToWork = BuildCommute(component, virtualResidentId, food, work),
                workToLeisure = BuildCommute(component, virtualResidentId, work, leisure),
                leisureToHome = BuildCommute(component, virtualResidentId, leisure, home)
            };
            CacheRoutine(routine);
            return true;
        }

        public bool TryGetCurrentCommute(string virtualResidentId, float hour, out SlopCitySprawlCommute commute)
        {
            commute = null;
            if (!TryGetRoutine(virtualResidentId, out SlopCitySprawlRoutine routine)) return false;
            commute = CurrentCommute(routine, hour); return true;
        }

        public void GetCommutesNear(Vector3 origin, float radius, int maximum, float hour, List<SlopCitySprawlCommute> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear(); int limit = Mathf.Clamp(maximum, 0, MaximumNearbyCommutes);
            if (limit == 0 || radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius)) return;
            float queryRadius = Mathf.Min(radius, MaximumNearbyRadiusMeters);
            // First pick a deterministic, bounded local sample using only cheap
            // anchor distances. Do not construct routes while scanning every cell
            // in a growing city: route construction includes graph searches.
            int cellLimit = Mathf.Min(MaximumNearbyCandidateCells, Mathf.Max(8, limit * 2));
            List<NearbyCell> cellsNear = SelectNearbyCells(origin, queryRadius, cellLimit);
            var candidates = new List<NearbyCandidate>(cellsNear.Count * 8);
            foreach (NearbyCell nearby in cellsNear)
            {
                AddNearbyCandidates(nearby.cell, false, nearby.distance, candidates);
                AddNearbyCandidates(nearby.cell, true, nearby.distance, candidates);
            }
            candidates.Sort(NearbyCandidate.Compare);
            int sampleLimit = Mathf.Min(MaximumRoutineSamplesPerQuery, Mathf.Max(8, limit * 2));
            var selected = new List<Nearby>(limit + 1);
            for (int index = 0; index < candidates.Count && index < sampleLimit; index++)
                AddNearby(candidates[index].id, origin, hour, limit, selected);
            selected.Sort(Nearby.Compare); foreach (Nearby value in selected) output.Add(value.commute);
        }

        private List<NearbyCell> SelectNearbyCells(Vector3 origin, float radius, int limit)
        {
            var selected = new List<NearbyCell>(limit + 1);
            float range = radius * radius;
            int minimumX = Mathf.FloorToInt((origin.x - radius) / SlopCityCityForge.ChunkSizeMeters);
            int maximumX = Mathf.FloorToInt((origin.x + radius) / SlopCityCityForge.ChunkSizeMeters);
            int minimumZ = Mathf.FloorToInt((origin.z - radius) / SlopCityCityForge.ChunkSizeMeters);
            int maximumZ = Mathf.FloorToInt((origin.z + radius) / SlopCityCityForge.ChunkSizeMeters);
            for (int x = minimumX; x <= maximumX; x++) for (int z = minimumZ; z <= maximumZ; z++)
            {
                if (!materializableByChunk.TryGetValue(new CityForgeCoord(x, z), out List<GroundCell> cellsInChunk)) continue;
                foreach (GroundCell cell in cellsInChunk)
                {
                    float distance = (cell.anchor - origin).sqrMagnitude;
                    if (distance > range) continue;
                    var value = new NearbyCell { cell = cell, distance = distance };
                    int insert = selected.Count;
                    while (insert > 0 && NearbyCell.Compare(value, selected[insert - 1]) < 0) insert--;
                    if (insert >= limit) continue;
                    selected.Insert(insert, value);
                    if (selected.Count > limit) selected.RemoveAt(selected.Count - 1);
                }
            }
            return selected;
        }

        private void BuildMaterializableSpatialIndex()
        {
            materializableByChunk.Clear();
            foreach (GroundCell cell in materializableCells)
            {
                // Street anchors on a reciprocal chunk edge lie exactly on that
                // edge, so index by their rendered world position rather than
                // only by the source plan's coordinate.
                CityForgeCoord bucket = new CityForgeCoord(
                    Mathf.FloorToInt(cell.anchor.x / SlopCityCityForge.ChunkSizeMeters),
                    Mathf.FloorToInt(cell.anchor.z / SlopCityCityForge.ChunkSizeMeters));
                if (!materializableByChunk.TryGetValue(bucket, out List<GroundCell> cellsInChunk))
                {
                    cellsInChunk = new List<GroundCell>();
                    materializableByChunk.Add(bucket, cellsInChunk);
                }
                cellsInChunk.Add(cell);
            }
            foreach (List<GroundCell> cellsInChunk in materializableByChunk.Values)
                cellsInChunk.Sort((left, right) => left.key.CompareTo(right.key));
        }

        private void AddNearby(string id, Vector3 origin, float hour, int limit, List<Nearby> selected)
        {
            if (!TryGetCurrentCommute(id, hour, out SlopCitySprawlCommute commute)) return;
            Vector3 point = (commute.to ?? commute.from).sidewalkPosition;
            var value = new Nearby { commute = commute, distance = (point - origin).sqrMagnitude, tie = StableHash(id) };
            int insert = selected.Count; while (insert > 0 && Nearby.Compare(value, selected[insert - 1]) < 0) insert--;
            if (insert >= limit) return; selected.Insert(insert, value); if (selected.Count > limit) selected.RemoveAt(selected.Count - 1);
        }

        private static void AddNearbyCandidates(GroundCell cell, bool red, float distance, List<NearbyCandidate> output)
        {
            long count = red ? cell.ladyCount : cell.smithCount;
            int samples = (int)Math.Min(3L, count);
            for (long slot = 0; slot < samples; slot++) AddNearbyCandidate(cell, red, slot, distance, output);
            if (count > samples) AddNearbyCandidate(cell, red, count - 1, distance, output);
        }

        private static void AddNearbyCandidate(GroundCell cell, bool red, long slot, float distance, List<NearbyCandidate> output)
        {
            string id = VirtualId(cell.key, red, slot);
            output.Add(new NearbyCandidate { id = id, distance = distance, tie = StableHash(id) });
        }

        private static SlopCitySprawlCommute CurrentCommute(SlopCitySprawlRoutine routine, float hour)
        {
            SlopCitySprawlCommute source = routine.CommuteAt(hour);
            if (source == null)
            {
                SlopCitySprawlPlace place = routine.PlaceAt(hour);
                return new SlopCitySprawlCommute { virtualResidentId = routine.virtualResidentId, mode = SlopCitySprawlTravelMode.Walk, from = place, to = place, routeIndex = place.routeIndex, progress01 = 1f, isTraveling = false };
            }
            return new SlopCitySprawlCommute { virtualResidentId = source.virtualResidentId, mode = source.mode, from = source.from, to = source.to, routeIndex = source.routeIndex, streetNodes = source.streetNodes, routeStages = source.routeStages, progress01 = routine.PhaseProgress(hour), isTraveling = true };
        }

        private SlopCitySprawlCommute BuildCommute(Component component, string residentId, SlopCitySprawlPlace from, SlopCitySprawlPlace to)
        {
            int[] direct = FindPath(component, from.routeNode, to.routeNode) ?? new[] { from.routeNode };
            if (TryBuildSubway(component, residentId, from, to, direct, out SlopCitySprawlCommute subway)) return subway;
            bool car = direct.Length >= 8 && PathUsesOnlyVehicleRoad(component, direct) && StableHash(residentId + ":car") % 3UL == 0UL;
            var stages = new List<SlopCitySprawlRouteStage> { Stage(SlopCitySprawlRouteStageKind.LeaveDoor, from.sidewalkPosition, from.sidewalkPosition, Array.Empty<int>()) };
            if (car)
            {
                stages.Add(Stage(SlopCitySprawlRouteStageKind.BoardCar, from.sidewalkPosition, from.sidewalkPosition, Array.Empty<int>()));
                stages.Add(Stage(SlopCitySprawlRouteStageKind.DriveStreet, from.sidewalkPosition, to.sidewalkPosition, direct));
                stages.Add(Stage(SlopCitySprawlRouteStageKind.ParkCar, to.sidewalkPosition, to.sidewalkPosition, Array.Empty<int>()));
            }
            else stages.Add(Stage(SlopCitySprawlRouteStageKind.WalkStreet, from.sidewalkPosition, to.sidewalkPosition, direct));
            stages.Add(Stage(SlopCitySprawlRouteStageKind.ArriveDoor, to.sidewalkPosition, to.sidewalkPosition, Array.Empty<int>()));
            SlopCitySprawlRouteStage[] routeStages = stages.ToArray(); PopulatePolylines(component, routeStages);
            return new SlopCitySprawlCommute { virtualResidentId = residentId, mode = car ? SlopCitySprawlTravelMode.Car : SlopCitySprawlTravelMode.Walk, from = from, to = to, routeIndex = component.routeIndex, streetNodes = direct, routeStages = routeStages, isTraveling = true };
        }

        private bool TryBuildSubway(Component component, string residentId, SlopCitySprawlPlace from, SlopCitySprawlPlace to, int[] direct, out SlopCitySprawlCommute commute)
        {
            commute = null;
            if (direct.Length < 12 || component.stops.Count < 2 || StableHash(residentId + ":subway") % 3UL != 0UL) return false;
            TransitStop first = null, second = null; int[] toFirst = null, fromSecond = null; int shortest = int.MaxValue;
            foreach (TransitStop origin in component.stops) foreach (TransitStop destination in component.stops)
            {
                if (ReferenceEquals(origin, destination) || !string.Equals(origin.source.lineId, destination.source.lineId, StringComparison.Ordinal)) continue;
                int[] walkA = FindPath(component, from.routeNode, origin.routeNode), walkB = FindPath(component, destination.routeNode, to.routeNode);
                if (walkA == null || walkB == null) continue;
                int score = walkA.Length + walkB.Length;
                if (score < shortest || score == shortest && string.CompareOrdinal(origin.source.id, first == null ? string.Empty : first.source.id) < 0)
                { first = origin; second = destination; toFirst = walkA; fromSecond = walkB; shortest = score; }
            }
            if (first == null) return false;
            SlopCitySprawlPlace stationA = Place("transit:" + first.source.id, SlopCitySprawlPlaceKind.TransitStop, first.source.coord, first.source.cell, component.routeIndex, first.routeNode, first.position);
            SlopCitySprawlPlace stationB = Place("transit:" + second.source.id, SlopCitySprawlPlaceKind.TransitStop, second.source.coord, second.source.cell, component.routeIndex, second.routeNode, second.position);
            SlopCitySprawlRouteStage[] routeStages = new[]
            {
                Stage(SlopCitySprawlRouteStageKind.LeaveDoor, from.sidewalkPosition, from.sidewalkPosition, Array.Empty<int>()),
                Stage(SlopCitySprawlRouteStageKind.WalkToStation, from.sidewalkPosition, stationA.sidewalkPosition, toFirst),
                Stage(SlopCitySprawlRouteStageKind.BoardSubway, stationA.sidewalkPosition, stationA.sidewalkPosition, Array.Empty<int>()),
                Stage(SlopCitySprawlRouteStageKind.RideSubway, stationA.sidewalkPosition, stationB.sidewalkPosition, Array.Empty<int>()),
                Stage(SlopCitySprawlRouteStageKind.ExitSubway, stationB.sidewalkPosition, stationB.sidewalkPosition, Array.Empty<int>()),
                Stage(SlopCitySprawlRouteStageKind.WalkFromStation, stationB.sidewalkPosition, to.sidewalkPosition, fromSecond),
                Stage(SlopCitySprawlRouteStageKind.ArriveDoor, to.sidewalkPosition, to.sidewalkPosition, Array.Empty<int>())
            };
            PopulatePolylines(component, routeStages);
            commute = new SlopCitySprawlCommute
            {
                virtualResidentId = residentId, mode = SlopCitySprawlTravelMode.Subway, from = from, to = to, routeIndex = component.routeIndex, streetNodes = direct, isTraveling = true,
                routeStages = routeStages
            };
            return true;
        }

        private int[] FindPath(Component component, int start, int destination)
        {
            if (component == null || start < 0 || destination < 0 || start >= component.globalNodes.Count || destination >= component.globalNodes.Count) return null;
            if (start == destination) return new[] { start };
            SlopCityCrowdRoute route = routes[component.routeIndex]; int[] previous = new int[route.nodes.Length]; Array.Fill(previous, -1);
            var pending = new Queue<int>(); pending.Enqueue(start); previous[start] = start;
            while (pending.Count > 0 && previous[destination] < 0)
            {
                int current = pending.Dequeue();
                foreach (int next in route.nodes[current].neighbors ?? Array.Empty<int>())
                    if (next >= 0 && next < previous.Length && previous[next] < 0) { previous[next] = current; pending.Enqueue(next); }
            }
            if (previous[destination] < 0) return null;
            var result = new List<int>();
            for (int current = destination; current != start; current = previous[current]) result.Add(current);
            result.Add(start); result.Reverse(); return result.ToArray();
        }

        private bool PathUsesOnlyVehicleRoad(Component component, int[] path)
        {
            if(path==null||path.Length==0)return false;
            foreach (int local in path)
                if (local < 0 || local >= component.globalNodes.Count || nodes[component.globalNodes[local]].roadClass == CityForgeRoadClass.PedestrianRoute) return false;
            return true;
        }

        private static SlopCitySprawlRouteStage Stage(SlopCitySprawlRouteStageKind kind, Vector3 from, Vector3 to, int[] path)
        {
            return new SlopCitySprawlRouteStage { kind = kind, from = from, to = to, streetNodes = path ?? Array.Empty<int>() };
        }

        /// <summary>Copies only resolved sidewalk points. No caller receives a cross-water straight-line route.</summary>
        public bool GetCommutePolyline(SlopCitySprawlCommute commute, List<Vector3> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            if (commute == null || commute.routeIndex < 0 || commute.routeIndex >= routes.Count) return false;
            SlopCityCrowdRoute route = routes[commute.routeIndex];
            foreach (int node in commute.streetNodes ?? Array.Empty<int>())
                if (node >= 0 && node < route.nodes.Length && route.nodes[node] != null) output.Add(route.nodes[node].position);
            return output.Count > 0;
        }

        private void PopulatePolylines(Component component, IEnumerable<SlopCitySprawlRouteStage> stages)
        {
            SlopCityCrowdRoute route = routes[component.routeIndex];
            foreach (SlopCitySprawlRouteStage stage in stages)
                stage.sidewalkPoints = (stage.streetNodes ?? Array.Empty<int>()).Where(node => node >= 0 && node < route.nodes.Length && route.nodes[node] != null).Select(node => route.nodes[node].position).ToArray();
        }

        private static SlopCitySprawlPlace Pick(List<SlopCitySprawlPlace> choices, string id, string salt, SlopCitySprawlPlace avoid)
        {
            if (choices == null || choices.Count == 0) return null;
            int index = (int)(StableHash(id + ":" + salt) % (ulong)choices.Count); SlopCitySprawlPlace result = choices[index];
            return choices.Count > 1 && ReferenceEquals(result, avoid) ? choices[(index + 1) % choices.Count] : result;
        }

        private void CacheRoutine(SlopCitySprawlRoutine routine)
        {
            if (routine == null || string.IsNullOrEmpty(routine.virtualResidentId) || routineCache.ContainsKey(routine.virtualResidentId)) return;
            while (routineCache.Count >= MaximumRoutineCache && routineCacheOrder.Count > 0)
                routineCache.Remove(routineCacheOrder.Dequeue());
            if (routineCache.Count >= MaximumRoutineCache)
            {
                routineCache.Clear();
                routineCacheOrder.Clear();
            }
            routineCache.Add(routine.virtualResidentId, routine);
            routineCacheOrder.Enqueue(routine.virtualResidentId);
        }

        private static List<SlopCitySprawlPlace> FirstNonEmpty(params List<SlopCitySprawlPlace>[] choices)
        {
            foreach (List<SlopCitySprawlPlace> choice in choices) if (choice != null && choice.Count > 0) return choice;
            return new List<SlopCitySprawlPlace>();
        }

        private static SlopCitySprawlPlace Place(string id, SlopCitySprawlPlaceKind kind, CityForgeCoord coord, int cell, int routeIndex, int routeNode, Vector3 point)
        {
            return new SlopCitySprawlPlace { id = id, kind = kind, coord = coord, cell = cell, routeIndex = routeIndex, routeNode = routeNode, sidewalkPosition = point };
        }

        private static int FirstCell(CityForgeParcel parcel)
        {
            return parcel == null || parcel.cells == null ? -1 : parcel.cells.Where(value => value >= 0 && value < ChunkGroundCells).DefaultIfEmpty(-1).Min();
        }

        private static bool FoodBuilding(CityForgeBuilding building)
        {
            string value = building == null ? string.Empty : building.blueprintId ?? string.Empty;
            return value == "cafe" || value == "market-hall" || value == "corner-shop" || value == "kiosk" || value == "exchange" || !string.IsNullOrEmpty(building == null ? null : building.storefrontId);
        }
        private static bool LeisureBuilding(CityForgeBuilding building)
        {
            string value = building == null ? string.Empty : building.blueprintId ?? string.Empty;
            return value == "library-room" || value == "community-hall" || value == "screening-room" || value == "studio" || value == "venue" || value == "archive" || value == "performance-space";
        }
        private static bool FoodService(string value) => value == "food-counter" || value == "market-stall" || value == "exchange-desk";
        private static bool LeisureService(string value) => value == "library-desk" || value == "community-room" || value == "screening-service" || value == "studio-service" || value == "venue-door" || value == "archive-desk";

        private static CellKey KeyFor(CityForgeCoord coord, int local) => new CellKey((long)coord.x * SlopCityCityForge.GridSize + local % SlopCityCityForge.GridSize, (long)coord.z * SlopCityCityForge.GridSize + local / SlopCityCityForge.GridSize);
        private static CellKey Neighbour(CellKey key, CityForgeDirection direction)
        {
            if (direction == CityForgeDirection.North) return new CellKey(key.x, key.z + 1);
            if (direction == CityForgeDirection.East) return new CellKey(key.x + 1, key.z);
            if (direction == CityForgeDirection.South) return new CellKey(key.x, key.z - 1);
            return new CellKey(key.x - 1, key.z);
        }
        private static Vector3 EdgePosition(EdgeKey edge)
        {
            float x = edge.first.x == edge.second.x ? (edge.first.x + .5f) * SlopCityCityForge.CellSizeMeters : (edge.first.x + edge.second.x + 1f) * SlopCityCityForge.CellSizeMeters * .5f;
            float z = edge.first.z == edge.second.z ? (edge.first.z + .5f) * SlopCityCityForge.CellSizeMeters : (edge.first.z + edge.second.z + 1f) * SlopCityCityForge.CellSizeMeters * .5f;
            return new Vector3(x, .11f, z);
        }
        private static string VirtualId(CellKey key, bool red, long slot) => "sprawl:" + (red ? "red" : "smith") + ":" + key.x.ToString(CultureInfo.InvariantCulture) + ":" + key.z.ToString(CultureInfo.InvariantCulture) + ":" + slot.ToString(CultureInfo.InvariantCulture) + ":v1";
        private static bool TryParseId(string id, out CellKey key, out bool red, out long slot)
        {
            key = default(CellKey); red = false; slot = -1; string[] parts = string.IsNullOrEmpty(id) ? Array.Empty<string>() : id.Split(':');
            if (parts.Length != 6 || parts[0] != "sprawl" || (parts[1] != "smith" && parts[1] != "red") || parts[5] != "v1" || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long x) || !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long z) || !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out slot)) return false;
            key = new CellKey(x, z); red = parts[1] == "red"; return true;
        }
        private static ulong StableHash(string value)
        {
            ulong hash = 14695981039346656037UL; foreach (char item in value ?? string.Empty) { hash ^= item; hash *= 1099511628211UL; } return hash;
        }

        private sealed class GroundCell
        {
            public CellKey key;
            public CityForgeCoord coord;
            public int localCell;
            public readonly List<int> streetNodes = new List<int>();
            public int component = -1, routeNode = -1;
            public long smithCount, ladyCount;
            public Vector3 anchor;
        }
        private sealed class StreetNode
        {
            public EdgeKey edge;
            public CityForgeRoadClass roadClass;
            public Vector3 position;
            public readonly HashSet<int> neighbours = new HashSet<int>();
            public int component = -1, routeNode = -1;
        }
        private sealed class RoadRecord { public CityForgeRoadClass roadClass; public bool crossesChunk; }
        private sealed class TransitStop { public SlopCitySprawlTransitStop source; public int routeNode; public Vector3 position; }
        private sealed class Component
        {
            public int routeIndex;
            public bool hasVehicleRoad;
            public readonly List<int> globalNodes = new List<int>();
            public readonly List<GroundCell> cells = new List<GroundCell>();
            public readonly List<SlopCitySprawlPlace> built = new List<SlopCitySprawlPlace>();
            public readonly List<SlopCitySprawlPlace> homes = new List<SlopCitySprawlPlace>();
            public readonly List<SlopCitySprawlPlace> work = new List<SlopCitySprawlPlace>();
            public readonly List<SlopCitySprawlPlace> food = new List<SlopCitySprawlPlace>();
            public readonly List<SlopCitySprawlPlace> leisure = new List<SlopCitySprawlPlace>();
            public readonly List<TransitStop> stops = new List<TransitStop>();
            public List<SlopCitySprawlPlace> homeChoices, workChoices, foodChoices, leisureChoices;
        }
        private struct Nearby
        {
            public SlopCitySprawlCommute commute;
            public float distance;
            public ulong tie;
            public static int Compare(Nearby left, Nearby right)
            {
                int compare = left.distance.CompareTo(right.distance); return compare != 0 ? compare : left.tie.CompareTo(right.tie);
            }
        }
        private struct NearbyCell
        {
            public GroundCell cell;
            public float distance;
            public static int Compare(NearbyCell left, NearbyCell right)
            {
                int compare = left.distance.CompareTo(right.distance);
                return compare != 0 ? compare : left.cell.key.CompareTo(right.cell.key);
            }
        }
        private struct NearbyCandidate
        {
            public string id;
            public float distance;
            public ulong tie;
            public static int Compare(NearbyCandidate left, NearbyCandidate right)
            {
                int compare = left.distance.CompareTo(right.distance);
                return compare != 0 ? compare : left.tie.CompareTo(right.tie);
            }
        }
        private struct CellKey : IEquatable<CellKey>, IComparable<CellKey>
        {
            public readonly long x, z;
            public CellKey(long x, long z) { this.x = x; this.z = z; }
            public bool Equals(CellKey other) => x == other.x && z == other.z;
            public override bool Equals(object value) => value is CellKey && Equals((CellKey)value);
            public override int GetHashCode() { unchecked { return ((int)x * 486187739) ^ (int)(x >> 32) ^ ((int)z * 16777619) ^ (int)(z >> 32); } }
            public int CompareTo(CellKey other) { int compare = x.CompareTo(other.x); return compare != 0 ? compare : z.CompareTo(other.z); }
        }
        private struct EdgeKey : IEquatable<EdgeKey>, IComparable<EdgeKey>
        {
            public readonly CellKey first, second;
            public EdgeKey(CellKey a, CellKey b) { if (a.CompareTo(b) <= 0) { first = a; second = b; } else { first = b; second = a; } }
            public bool Equals(EdgeKey other) => first.Equals(other.first) && second.Equals(other.second);
            public override bool Equals(object value) => value is EdgeKey && Equals((EdgeKey)value);
            public override int GetHashCode() { unchecked { return first.GetHashCode() * 486187739 ^ second.GetHashCode(); } }
            public int CompareTo(EdgeKey other) { int compare = first.CompareTo(other.first); return compare != 0 ? compare : second.CompareTo(other.second); }
        }
    }
}
