using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>A bounded, time-sliced walking route for clues, never for moving the player.</summary>
    public sealed class SlopCityHintRoute
    {
        private sealed class Node
        {
            public Vector2Int cell;
            public Vector3 point;
            public float cost = float.PositiveInfinity;
            public float score;
            public Node parent;
            public bool clear, closed;
        }

        private readonly Dictionary<Vector2Int, Node> nodes = new Dictionary<Vector2Int, Node>();
        private readonly List<Node> open = new List<Node>();
        private readonly List<Vector3> path = new List<Vector3>();
        private readonly RaycastHit[] hits = new RaycastHit[48];
        private readonly Collider[] overlaps = new Collider[48];
        private Transform player, destination;
        private Vector3 origin, target;
        private Vector2Int min, max;
        private int expanded;
        public IReadOnlyList<Vector3> Points => path;
        public bool Finished { get; private set; }
        public bool ReachedTarget { get; private set; }
        public int ExpandedNodes => expanded;

        public void Begin(Vector3 start, Vector3 end, Transform user, Transform targetRoot)
        {
            origin = start; target = end; player = user; destination = targetRoot;
            nodes.Clear(); open.Clear(); path.Clear(); expanded = 0;
            Finished = ReachedTarget = false;
            min = new Vector2Int(Mathf.FloorToInt(Mathf.Min(start.x, end.x)) - 18, Mathf.FloorToInt(Mathf.Min(start.z, end.z)) - 18);
            max = new Vector2Int(Mathf.CeilToInt(Mathf.Max(start.x, end.x)) + 18, Mathf.CeilToInt(Mathf.Max(start.z, end.z)) + 18);
            Vector2Int cell = new Vector2Int(Mathf.RoundToInt(start.x), Mathf.RoundToInt(start.z));
            // Keep the first node at the actual player position, including narrow doorways.
            var first = new Node { cell = cell, point = start, clear = true, cost = 0, score = FlatDistance(start, end) };
            nodes.Add(cell, first); open.Add(first);
        }

        public void Step(int budget = 40)
        {
            if (Finished) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int work = 0; work < budget && timer.Elapsed.TotalMilliseconds < 1.8; work++)
            {
                if (open.Count == 0 || expanded >= 6000) { Finished = true; return; }
                int best = 0;
                for (int i = 1; i < open.Count; i++) if (open[i].score < open[best].score) best = i;
                Node current = open[best]; open.RemoveAt(best);
                if (current.closed) continue;
                current.closed = true; expanded++;
                if (FlatDistance(current.point, target) <= 2.4f && Mathf.Abs(current.point.y - target.y) <= 2.5f
                    && ClearSight(current.point + Vector3.up * 1.35f, target, true))
                {
                    for (Node node = current; node != null; node = node.parent) path.Add(node.point);
                    path.Reverse(); Finished = ReachedTarget = true; return;
                }
                for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && z == 0) continue;
                    Vector2Int key = current.cell + new Vector2Int(x, z);
                    if (key.x < min.x || key.y < min.y || key.x > max.x || key.y > max.y) continue;
                    if (!nodes.TryGetValue(key, out Node next))
                    {
                        next = new Node { cell = key };
                        next.clear = Ground(new Vector3(key.x, origin.y, key.y), out next.point);
                        nodes.Add(key, next);
                    }
                    if (!next.clear || next.closed || Mathf.Abs(next.point.y - current.point.y) > .34f) continue;
                    float cost = current.cost + Vector3.Distance(current.point, next.point);
                    if (cost >= next.cost || !ClearWalk(current.point, next.point)) continue;
                    bool firstVisit = float.IsPositiveInfinity(next.cost);
                    next.cost = cost; next.score = cost + FlatDistance(next.point, target) * 1.12f; next.parent = current;
                    if (firstVisit) open.Add(next);
                }
            }
        }

        /// <summary>Distance to the checked route, rather than distance from its starting point.</summary>
        public float DistanceFromPathSquared(Vector3 point)
        {
            if (!Finished || !ReachedTarget || path.Count < 2) return float.PositiveInfinity;
            float nearest = float.PositiveInfinity;
            // The solver caps expanded nodes at 6,000; scan without extra allocations.
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 edge = path[i] - path[i - 1];
                Vector3 offset = point - path[i - 1];
                float along = edge.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector3.Dot(offset, edge) / edge.sqrMagnitude) : 0f;
                nearest = Mathf.Min(nearest, (offset - edge * along).sqrMagnitude);
            }
            return nearest;
        }

        private bool Ground(Vector3 point, out Vector3 supported)
        {
            supported = point;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * .6f, Vector3.down, hits, 1.5f, ~0, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            float nearest = float.PositiveInfinity; bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (Ignored(hits[i].collider, false) || hits[i].normal.y < .7f || hits[i].distance >= nearest) continue;
                nearest = hits[i].distance; supported = hits[i].point; found = true;
            }
            if (!found) return false;
            count = Physics.OverlapCapsuleNonAlloc(supported + Vector3.up * .43f, supported + Vector3.up * 1.5f,
                .26f, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++) if (!Ignored(overlaps[i], true)) return false;
            return true;
        }

        public bool ClearWalk(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from; float length = delta.magnitude;
            if (length < .01f) return true;
            int count = Physics.CapsuleCastNonAlloc(from + Vector3.up * .43f, from + Vector3.up * 1.5f, .26f,
                delta / length, hits, length, ~0, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            for (int i = 0; i < count; i++) if (!Ignored(hits[i].collider, true)) return false;
            return true;
        }

        public bool ClearSight(Vector3 from, Vector3 to, bool allowDestination = false)
        {
            Vector3 delta = to - from; float length = delta.magnitude;
            if (length < .01f) return true;
            int count = Physics.RaycastNonAlloc(from, delta / length, hits, Mathf.Max(0, length - .06f), ~0, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i].collider;
                if (Ignored(collider, false)) continue;
                if (allowDestination && destination && (collider.transform == destination || collider.transform.IsChildOf(destination))) continue;
                return false;
            }
            return true;
        }

        public SlopCityDoor NearbyDoor(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from; float length = Mathf.Min(delta.magnitude, 4f);
            if (length < .01f) return null;
            int count = Physics.RaycastNonAlloc(from + Vector3.up, delta.normalized, hits, length, ~0, QueryTriggerInteraction.Ignore);
            SlopCityDoor closest = null; float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                SlopCityDoor door = hits[i].collider ? hits[i].collider.GetComponentInParent<SlopCityDoor>() : null;
                if (door && !door.IsOpen && hits[i].distance < nearest) { closest = door; nearest = hits[i].distance; }
            }
            return closest;
        }

        private bool Ignored(Collider collider, bool passDoors)
        {
            if (!collider) return true;
            if (player && (collider.transform == player || collider.transform.IsChildOf(player))) return true;
            // A closed door is a valid route connection, but its clue remains
            // occluded and the transmission explicitly tells the player to open it.
            return passDoors && collider.GetComponentInParent<SlopCityDoor>();
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
