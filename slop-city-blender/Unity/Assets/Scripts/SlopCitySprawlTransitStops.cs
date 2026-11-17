using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Derives the usable Sprawl subway registry from accepted City Forge data.
    /// It never creates a station, road, or passenger: a stop exists only after
    /// a player-built transit landing attaches to a continuous, grounded
    /// transit-service corridor. The crowd owns routines and materialization;
    /// this bridge only supplies the verified network facts it needs.
    /// </summary>
    [DefaultExecutionOrder(-18)]
    [DisallowMultipleComponent]
    public sealed class SlopCitySprawlTransitStops : MonoBehaviour
    {
        public const int MaximumNearbyVisualCommuters = 16;
        private const float RefreshSeconds = 1.5f;
        private const int MaximumLandingAttachmentCells = 3;

        private readonly List<SlopCitySprawlTransitStop> stops = new List<SlopCitySprawlTransitStop>();
        private SlopCityCityForgeController controller;
        private SlopCityCrowd crowd;
        private SlopCityCrowd publishedCrowd;
        private float nextRefresh;
        private bool dirty = true;

        public IReadOnlyList<SlopCitySprawlTransitStop> Stops { get { return stops; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<SlopCitySprawlTransitStops>()) return;
            new GameObject("SlopCitySprawlTransitStops").AddComponent<SlopCitySprawlTransitStops>();
        }

        private void OnDestroy()
        {
            if (controller != null) controller.AcceptedPresentationChanged -= MarkDirty;
        }

        private void Update()
        {
            Bind();
            if (controller == null || !controller.IsInitialized || (Time.unscaledTime < nextRefresh && !dirty)) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            dirty = false;
            RebuildStops();
        }

        /// <summary>Allows a composition to use its known crowd/controller without a scene search.</summary>
        public void Configure(SlopCityCityForgeController cityController, SlopCityCrowd cityCrowd)
        {
            if (controller != cityController)
            {
                if (controller != null) controller.AcceptedPresentationChanged -= MarkDirty;
                controller = cityController;
                if (controller != null) controller.AcceptedPresentationChanged += MarkDirty;
            }
            crowd = cityCrowd;
            dirty = true;
        }

        /// <summary>
        /// Supplies only the small presentation request a renderer needs. The
        /// census remains logical; callers must never instantiate one body or
        /// vehicle per record in the growing city.
        /// </summary>
        public void GetNearbyVisualCommutes(Vector3 origin, float radius, List<SlopCitySprawlCommute> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            if (crowd == null) return;
            crowd.GetSprawlCommutesNear(origin, Mathf.Clamp(radius, 1f, 160f), MaximumNearbyVisualCommuters, output);
        }

        private void Bind()
        {
            if (controller == null)
            {
                controller = FindFirstObjectByType<SlopCityCityForgeController>();
                if (controller != null) controller.AcceptedPresentationChanged += MarkDirty;
            }
            if (crowd == null) crowd = FindFirstObjectByType<SlopCityCrowd>();
        }

        private void MarkDirty(IReadOnlyList<CityForgeCoord> changed)
        {
            dirty = true;
        }

        private void RebuildStops()
        {
            // A neighborhood page is not a world census. Replacing the stop
            // registry from one would silently erase distant operational stops
            // and let a hosted client make routing decisions from partial data.
            CityForgeWorldHead head = controller.WorldHead;
            if (head == null || !head.indexIsComplete || head.changedChunks == null
                || controller.AcceptedChunks.Count != head.changedChunks.Count) return;
            List<CityForgeChunkPlan> plans = AcceptedPlans(controller.AcceptedChunks);
            Dictionary<CellNode, HashSet<CellNode>> corridors = BuildGroundedTransitCorridors(plans);
            Dictionary<CellNode, string> lineForNode = BuildLineIds(corridors);
            List<SlopCitySprawlTransitStop> replacement = BuildStops(plans, lineForNode);
            bool changed = !SameStops(stops, replacement);
            if (changed)
            {
                stops.Clear();
                stops.AddRange(replacement);
            }
            // Do not rebuild the complete sparse census every registry poll.
            // A new/rebound crowd still receives the current verified facts.
            if (crowd != null && (changed || crowd != publishedCrowd))
            {
                crowd.SetSprawlTransitStops(stops);
                publishedCrowd = crowd;
            }
        }

        private static bool SameStops(List<SlopCitySprawlTransitStop> left, List<SlopCitySprawlTransitStop> right)
        {
            if (left == null || right == null || left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++)
            {
                SlopCitySprawlTransitStop a = left[index], b = right[index];
                if (a == null || b == null || !string.Equals(a.id, b.id, StringComparison.Ordinal)
                    || !string.Equals(a.lineId, b.lineId, StringComparison.Ordinal) || !a.coord.Equals(b.coord) || a.cell != b.cell)
                    return false;
            }
            return true;
        }

        private static List<CityForgeChunkPlan> AcceptedPlans(IReadOnlyDictionary<CityForgeCoord, CityForgeChunkPlan> accepted)
        {
            var values = new List<CityForgeChunkPlan>();
            if (accepted != null)
                foreach (KeyValuePair<CityForgeCoord, CityForgeChunkPlan> item in accepted)
                    if (HasIslandGround(item.Value)) values.Add(item.Value);
            values.Sort((left, right) => left.coord.CompareTo(right.coord));
            return values;
        }

        private static Dictionary<CellNode, HashSet<CellNode>> BuildGroundedTransitCorridors(List<CityForgeChunkPlan> plans)
        {
            var ground = new HashSet<CellNode>();
            foreach (CityForgeChunkPlan plan in plans)
                foreach (int cell in plan.groundCells ?? new List<int>())
                    if (cell >= 0 && cell < SlopCityCityForge.GridSize * SlopCityCityForge.GridSize)
                        ground.Add(CellFor(plan.coord, cell));

            var links = new Dictionary<CellNode, HashSet<CellNode>>();
            foreach (CityForgeChunkPlan plan in plans)
                foreach (CityForgeRoad road in plan.roads ?? new List<CityForgeRoad>())
                {
                    if (road == null || road.roadClass != CityForgeRoadClass.TransitServiceCorridor
                        || !SlopCityCityForge.RoadHasIslandGround(plan, road)) continue;
                    CellNode from = CellFor(plan.coord, road.cellX, road.cellZ);
                    CellNode to = Neighbour(from, road.direction);
                    // In addition to the plan validator, require the actual
                    // accepted ground at both endpoints. A distant or unpaged
                    // neighbor must not become an imaginary subway link.
                    if (!ground.Contains(from) || !ground.Contains(to)) continue;
                    AddLink(links, from, to);
                }
            return links;
        }

        private static Dictionary<CellNode, string> BuildLineIds(Dictionary<CellNode, HashSet<CellNode>> links)
        {
            var result = new Dictionary<CellNode, string>();
            var pending = new List<CellNode>(links.Keys);
            pending.Sort();
            foreach (CellNode origin in pending)
            {
                if (result.ContainsKey(origin)) continue;
                var component = new List<CellNode>();
                var queue = new Queue<CellNode>();
                queue.Enqueue(origin);
                result.Add(origin, string.Empty);
                while (queue.Count > 0)
                {
                    CellNode node = queue.Dequeue();
                    component.Add(node);
                    if (!links.TryGetValue(node, out HashSet<CellNode> neighbours)) continue;
                    foreach (CellNode neighbour in neighbours)
                        if (!result.ContainsKey(neighbour))
                        {
                            result.Add(neighbour, string.Empty);
                            queue.Enqueue(neighbour);
                        }
                }
                component.Sort();
                string lineId = "sprawl-line-" + SlopCityCityForge.Sha256(component[0].x.ToString(CultureInfo.InvariantCulture)
                    + "|" + component[0].z.ToString(CultureInfo.InvariantCulture)).Substring(0, 20);
                foreach (CellNode node in component) result[node] = lineId;
            }
            return result;
        }

        private static List<SlopCitySprawlTransitStop> BuildStops(List<CityForgeChunkPlan> plans,
            Dictionary<CellNode, string> lineForNode)
        {
            var values = new List<SlopCitySprawlTransitStop>();
            foreach (CityForgeChunkPlan plan in plans)
            {
                var parcels = new Dictionary<string, CityForgeParcel>(StringComparer.Ordinal);
                foreach (CityForgeParcel parcel in plan.parcels ?? new List<CityForgeParcel>())
                    if (parcel != null && !string.IsNullOrEmpty(parcel.id)) parcels[parcel.id] = parcel;
                foreach (CityForgeLandmark landmark in plan.landmarks ?? new List<CityForgeLandmark>())
                {
                    if (!OperationalTransitLanding(landmark) || !parcels.TryGetValue(landmark.parcelId ?? string.Empty, out CityForgeParcel parcel)) continue;
                    if (!TryAttachTransitLine(plan.coord, parcel.cells, lineForNode, out string lineId, out int cell)) continue;
                    if (!CityForgeIds.IsId(landmark.id)) continue;
                    values.Add(new SlopCitySprawlTransitStop
                    {
                        id = SlopCityCityForge.StableId("transit", plan.coord, landmark.id),
                        lineId = lineId,
                        coord = plan.coord,
                        cell = cell
                    });
                }
            }
            values.Sort((left, right) =>
            {
                int compare = string.CompareOrdinal(left.lineId, right.lineId);
                return compare != 0 ? compare : string.CompareOrdinal(left.id, right.id);
            });
            return values;
        }

        private static bool TryAttachTransitLine(CityForgeCoord coordinate, List<int> cells,
            Dictionary<CellNode, string> lineForNode, out string lineId, out int selectedCell)
        {
            lineId = string.Empty;
            selectedCell = -1;
            if (cells == null || cells.Count == 0 || lineForNode.Count == 0) return false;
            var orderedCells = new List<int>();
            foreach (int cell in cells)
                if (cell >= 0 && cell < SlopCityCityForge.GridSize * SlopCityCityForge.GridSize) orderedCells.Add(cell);
            orderedCells.Sort();
            for (int distance = 0; distance <= MaximumLandingAttachmentCells; distance++)
                foreach (int cell in orderedCells)
                {
                    CellNode centre = CellFor(coordinate, cell);
                    for (int dx = -distance; dx <= distance; dx++)
                    {
                        int remaining = distance - Math.Abs(dx);
                        if (TryLine(centre.Offset(dx, remaining), lineForNode, out lineId)
                            || (remaining != 0 && TryLine(centre.Offset(dx, -remaining), lineForNode, out lineId)))
                        {
                            selectedCell = cell;
                            return true;
                        }
                    }
                }
            return false;
        }

        private static bool TryLine(CellNode node, Dictionary<CellNode, string> lines, out string lineId)
        {
            if (lines.TryGetValue(node, out lineId) && !string.IsNullOrEmpty(lineId)) return true;
            lineId = string.Empty;
            return false;
        }

        private static bool OperationalTransitLanding(CityForgeLandmark landmark)
        {
            if (landmark == null || !string.Equals(landmark.classId, "transit-landing", StringComparison.Ordinal)) return false;
            return landmark.state == "active" || landmark.state == "opening" || landmark.state == "established";
        }

        /// <summary>Cars are allowed only on real grounded roads, never on pedestrian routes or water.</summary>
        public static bool IsVehicleRoad(CityForgeChunkPlan plan, CityForgeRoad road)
        {
            return road != null && road.roadClass != CityForgeRoadClass.PedestrianRoute
                && SlopCityCityForge.RoadHasIslandGround(plan, road);
        }

        private static bool HasIslandGround(CityForgeChunkPlan plan)
        {
            return plan != null && plan.groundCells != null && plan.groundCells.Count > 0;
        }

        private static CellNode CellFor(CityForgeCoord coordinate, int cell)
        {
            return CellFor(coordinate, cell % SlopCityCityForge.GridSize, cell / SlopCityCityForge.GridSize);
        }

        private static CellNode CellFor(CityForgeCoord coordinate, int x, int z)
        {
            return new CellNode((long)coordinate.x * SlopCityCityForge.GridSize + x,
                (long)coordinate.z * SlopCityCityForge.GridSize + z);
        }

        private static CellNode Neighbour(CellNode node, CityForgeDirection direction)
        {
            switch (direction)
            {
                case CityForgeDirection.North: return node.Offset(0, 1);
                case CityForgeDirection.East: return node.Offset(1, 0);
                case CityForgeDirection.South: return node.Offset(0, -1);
                case CityForgeDirection.West: return node.Offset(-1, 0);
                default: return node;
            }
        }

        private static void AddLink(Dictionary<CellNode, HashSet<CellNode>> links, CellNode from, CellNode to)
        {
            if (from.Equals(to)) return;
            if (!links.TryGetValue(from, out HashSet<CellNode> fromLinks))
            {
                fromLinks = new HashSet<CellNode>();
                links.Add(from, fromLinks);
            }
            if (!links.TryGetValue(to, out HashSet<CellNode> toLinks))
            {
                toLinks = new HashSet<CellNode>();
                links.Add(to, toLinks);
            }
            fromLinks.Add(to);
            toLinks.Add(from);
        }

        private struct CellNode : IEquatable<CellNode>, IComparable<CellNode>
        {
            public readonly long x;
            public readonly long z;

            public CellNode(long cellX, long cellZ) { x = cellX; z = cellZ; }
            public CellNode Offset(long offsetX, long offsetZ) { return new CellNode(x + offsetX, z + offsetZ); }
            public bool Equals(CellNode other) { return x == other.x && z == other.z; }
            public override bool Equals(object value) { return value is CellNode && Equals((CellNode)value); }
            public override int GetHashCode() { unchecked { return x.GetHashCode() * 486187739 + z.GetHashCode(); } }
            public int CompareTo(CellNode other) { int compare = x.CompareTo(other.x); return compare != 0 ? compare : z.CompareTo(other.z); }
        }
    }
}
