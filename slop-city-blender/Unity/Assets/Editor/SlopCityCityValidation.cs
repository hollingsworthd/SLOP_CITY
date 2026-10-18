#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopCity.Editor
{
    public static class SlopCityCityValidation
    {
        public static void Validate(Scene scene, SlopCityValidationReport report)
        {
            var roots = scene.GetRootGameObjects();
            var districts = roots.SelectMany(root => root.GetComponentsInChildren<SlopCityDistrict>(true)).ToArray();
            var doors = roots.SelectMany(root => root.GetComponentsInChildren<SlopCityDoor>(true)).ToArray();
            var player = roots.SelectMany(root => root.GetComponentsInChildren<CharacterController>(true)).FirstOrDefault();
            report.Check(districts.Length == 9, "City must contain exactly nine districts.");
            report.Check(districts.Select(d => d.Index).Distinct().Count() == 9, "District grid indices must be unique.");
            Physics.SyncTransforms();
            var graph = new SlopCityWalkGraph(player);
            graph.Build(new Vector3(8f, 0f, 0f));
            report.Check(graph.ReachableCount > 1000, "City streets must form a connected walkable area.");
            foreach (var district in districts)
            {
                report.Check(district.Column >= 0 && district.Column < 3 && district.Row >= 0 && district.Row < 3
                    && district.Index == district.Row * 3 + district.Column, "District grid coordinates are invalid: " + district.name);
                Vector3 expected = new Vector3((district.Column - 1) * 40f, 0f, (district.Row - 1) * 40f);
                report.Check(SlopCityWalkGraph.FlatDistance(expected, district.Center) < .1f,
                    "District center differs from the 3x3, 40-metre grid: " + district.name);
                report.Check(!string.IsNullOrWhiteSpace(district.DisplayName), "District requires a display name: " + district.name);
                report.Check(graph.Route(new Vector3(8f, 0f, 0f), district.VisitPoint) != null,
                    "No ground-supported capsule route connects the home street to " + district.DisplayName);
            }
            report.Check(doors.Length >= 7, "Expected the home door and six enterable storefront doors.");
            foreach (var door in doors)
            {
                report.Check(door.Leaf && door.Blocker, "Door hinge/leaf and blocker references are required: " + door.name);
                report.Check(!door.IsOpen && !door.IsMoving, "Doors must be saved closed and stationary: " + door.name);
                if (!door.Leaf || !door.Blocker) continue;
                report.Check(door.Blocker.enabled && !door.Blocker.isTrigger && door.Blocker.transform.IsChildOf(door.Leaf),
                    "Door blocker must be solid, enabled, and follow its leaf: " + door.name);
                Bounds bounds = door.Blocker.bounds;
                Vector3 normal = bounds.size.x < bounds.size.z ? Vector3.right : Vector3.forward;
                report.Check(door.Blocker.Raycast(new Ray(bounds.center - normal * 2f, normal), out _, 4f),
                    "Closed door must block a ray through its aperture: " + door.name);
            }
        }
    }

    /// <summary>Finite capsule-clear graph over real scene physics; no navigation or teleport assumptions.</summary>
    internal sealed class SlopCityWalkGraph
    {
        private const int Half = 60, Width = Half * 2 + 1, Count = Width * Width;
        private readonly CharacterController player;
        private readonly bool[] sampled = new bool[Count], walkable = new bool[Count], reachable = new bool[Count];
        private readonly Vector3[] feet = new Vector3[Count];
        private readonly byte[] checkedEdges = new byte[Count], clearEdges = new byte[Count];
        private static readonly int[] Dx = { 1, -1, 0, 0 }, Dz = { 0, 0, 1, -1 };
        public int ReachableCount { get; private set; }

        public SlopCityWalkGraph(CharacterController ignoredPlayer) { player = ignoredPlayer; }
        public static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
        private bool Relevant(Collider collider) => collider && collider != player
            && (!player || !collider.transform.IsChildOf(player.transform));

        public bool TryFeet(Vector3 point, out Vector3 foot)
        {
            foot = point;
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(point.x, .95f, point.z), Vector3.down, 2f,
                ~0, QueryTriggerInteraction.Ignore).Where(hit => Relevant(hit.collider) && hit.normal.y > .7f)
                .OrderBy(hit => hit.distance).ToArray();
            if (hits.Length == 0) return false;
            foot = hits[0].point + Vector3.up * .025f;
            Vector3 bottom = foot + Vector3.up * .24f, top = foot + Vector3.up * 1.5f;
            return !Physics.OverlapCapsule(bottom, top, .22f, ~0, QueryTriggerInteraction.Ignore).Any(Relevant);
        }

        public bool ClearSegment(Vector3 from, Vector3 to)
        {
            if (Mathf.Abs(from.y - to.y) > .3f) return false;
            Vector3 delta = to - from; delta.y = 0f;
            if (delta.magnitude < .001f) return true;
            // CharacterController's step offset permits low pavement edges.
            float level = Mathf.Max(from.y, to.y);
            Vector3 bottom = new Vector3(from.x, level + .52f, from.z);
            Vector3 top = new Vector3(from.x, level + 1.5f, from.z);
            return !Physics.CapsuleCastAll(bottom, top, .22f, delta.normalized, delta.magnitude,
                ~0, QueryTriggerInteraction.Ignore).Any(hit => Relevant(hit.collider));
        }

        private bool Sample(int index)
        {
            if (!sampled[index])
            {
                sampled[index] = true;
                walkable[index] = TryFeet(new Vector3(index % Width - Half, 0f, index / Width - Half), out feet[index]);
            }
            return walkable[index];
        }

        private int Neighbor(int index, int direction)
        {
            int x = index % Width + Dx[direction], z = index / Width + Dz[direction];
            if (x < 0 || x >= Width || z < 0 || z >= Width) return -1;
            int other = z * Width + x;
            if ((checkedEdges[index] & (1 << direction)) == 0)
            {
                bool clear = Sample(index) && Sample(other) && ClearSegment(feet[index], feet[other]);
                checkedEdges[index] |= (byte)(1 << direction);
                checkedEdges[other] |= (byte)(1 << (direction ^ 1));
                if (clear) { clearEdges[index] |= (byte)(1 << direction); clearEdges[other] |= (byte)(1 << (direction ^ 1)); }
            }
            return (clearEdges[index] & (1 << direction)) != 0 ? other : -1;
        }

        private int Nearest(Vector3 point, bool requireReachable)
        {
            if (!TryFeet(point, out Vector3 exact)) return -1;
            int best = -1; float distance = float.PositiveInfinity;
            int cx = Mathf.RoundToInt(point.x) + Half, cz = Mathf.RoundToInt(point.z) + Half;
            for (int z = Math.Max(0, cz - 2); z <= Math.Min(Width - 1, cz + 2); z++)
                for (int x = Math.Max(0, cx - 2); x <= Math.Min(Width - 1, cx + 2); x++)
                {
                    int index = z * Width + x;
                    if ((requireReachable && !reachable[index]) || !Sample(index)) continue;
                    float value = FlatDistance(point, feet[index]);
                    if (value < distance && ClearSegment(exact, feet[index])) { best = index; distance = value; }
                }
            return best;
        }

        public void Build(Vector3 start)
        {
            int source = Nearest(start, false);
            if (source < 0) return;
            var queue = new Queue<int>(); queue.Enqueue(source); reachable[source] = true; ReachableCount = 1;
            while (queue.Count > 0)
            {
                int here = queue.Dequeue();
                for (int direction = 0; direction < 4; direction++)
                {
                    int next = Neighbor(here, direction);
                    if (next < 0 || reachable[next]) continue;
                    reachable[next] = true; ReachableCount++; queue.Enqueue(next);
                }
            }
        }

        public List<Vector3> Route(Vector3 from, Vector3 to)
        {
            int source = Nearest(from, true), target = Nearest(to, true);
            if (source < 0 || target < 0) return null;
            var previous = Enumerable.Repeat(-1, Count).ToArray();
            var queue = new Queue<int>(); queue.Enqueue(source); previous[source] = source;
            while (queue.Count > 0 && previous[target] < 0)
            {
                int here = queue.Dequeue();
                for (int direction = 0; direction < 4; direction++)
                {
                    int next = Neighbor(here, direction);
                    if (next < 0 || previous[next] >= 0) continue;
                    previous[next] = here; queue.Enqueue(next);
                }
            }
            if (previous[target] < 0) return null;
            var path = new List<Vector3>();
            for (int current = target; current != source; current = previous[current]) path.Add(feet[current]);
            path.Add(feet[source]); path.Reverse();
            if (TryFeet(to, out Vector3 exact)) path.Add(exact);
            return path;
        }
    }
}
#endif
