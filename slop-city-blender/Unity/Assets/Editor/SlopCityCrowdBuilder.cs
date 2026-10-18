#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Population routes are baked against the completed city's real colliders.</summary>
    public static class SlopCityCrowdBuilder
    {
        private const float Spacing = .9f, Radius = .32f;
        private static readonly string[] Names = { "Aya", "Rin", "Sora", "Akira", "Ken", "Yu", "Hana", "Nao", "Mei", "Jin", "Rei", "Emi", "Tao", "Leon", "Sam", "Mina", "Jun", "Kai", "Aki", "Yuna", "Noa", "Ren", "Haru", "Mari" };
        private static CharacterController player;

        public static SlopCityCrowd Build(Transform parent = null, int population = 432)
        {
            SlopCityCrowd existing = Object.FindFirstObjectByType<SlopCityCrowd>();
            if (existing) Object.DestroyImmediate(existing.gameObject);
            player = Object.FindFirstObjectByType<CharacterController>();
            Physics.SyncTransforms();
            var routes = new List<SlopCityCrowdRoute>();
            SlopCityDistrict[] districts = Object.FindObjectsByType<SlopCityDistrict>(FindObjectsSortMode.None).OrderBy(d => d.Index).ToArray();
            foreach (SlopCityDistrict district in districts)
            {
                SlopCityCrowdRoute route = Bake(district.DisplayName, district.Index, district.Center,
                    new Vector2(-14.3f, -14.3f), new Vector2(14.3f, 14.3f));
                if (route.nodes.Length < 25) throw new InvalidOperationException("Not enough clear pedestrian pavement in " + district.DisplayName);
                routes.Add(route);
            }
            Transform[] stations = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(t => t.name.StartsWith("Subway_Station_", StringComparison.Ordinal)).OrderBy(t => t.name).ToArray();
            for (int i = 0; i < stations.Length; i++)
            {
                SlopCityCrowdRoute route = Bake("Subway platform " + (i + 1), 10 + i,
                    stations[i].position + Vector3.down * 7.82f, new Vector2(-6.7f, -4.8f), new Vector2(6.7f, .7f));
                if (route.nodes.Length < 12) throw new InvalidOperationException("Not enough clear subway platform for passengers: " + stations[i].name);
                routes.Add(route);
            }
            if (routes.Count == 0) throw new InvalidOperationException("Build the city before baking its crowd.");
            int desired = Mathf.Clamp(population, 1, 500);
            var seeds = new List<SlopCityNpcSeed>();
            var random = new System.Random(19841984);
            int subwayCount = stations.Length == 0 ? 0 : Mathf.Min(18, desired / 20);
            int streetTotal = desired - subwayCount * stations.Length;
            for (int routeIndex = 0; routeIndex < routes.Count; routeIndex++)
            {
                SlopCityCrowdRoute route = routes[routeIndex];
                int count = routeIndex < districts.Length ? streetTotal / districts.Length + (routeIndex < streetTotal % districts.Length ? 1 : 0) : subwayCount;
                var available = Enumerable.Range(0, route.nodes.Length).OrderBy(_ => random.Next()).ToList();
                // Keep many people on visible pavements at the front/edges, while still filling plazas.
                if (routeIndex < districts.Length)
                {
                    Vector3 center = districts[routeIndex].Center;
                    available = available.OrderBy(index => { Vector3 p = route.nodes[index].position - center; return p.z < -8 || Mathf.Abs(p.x) > 10 ? 0 : 1; }).ToList();
                }
                var occupied = new List<Vector3>(); int picked = 0;
                foreach (int nodeIndex in available)
                {
                    if (picked >= count) break;
                    SlopCityCrowdNode node = route.nodes[nodeIndex];
                    if (node.neighbors.Length == 0 || occupied.Any(p => (p - node.position).sqrMagnitude < .82f * .82f)) continue;
                    occupied.Add(node.position);
                    int next = node.neighbors[random.Next(node.neighbors.Length)];
                    Vector3 direction = route.nodes[next].position - node.position;
                    SlopCityNpcRole role = Role(route.districtIndex, picked);
                    int look = random.Next(0, 1200);
                    seeds.Add(new SlopCityNpcSeed { route = routeIndex, node = nodeIndex, next = next, appearance = look,
                        role = role, height = 1.58f + (float)random.NextDouble() * .29f, width = .86f + (float)random.NextDouble() * .18f,
                        speed = (role == SlopCityNpcRole.Courier ? 1.32f : .82f) + (float)random.NextDouble() * .35f,
                        phase = (float)random.NextDouble() * Mathf.PI * 2,
                        heading = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg,
                        pause = picked % 5 == 0 ? 1 + (float)random.NextDouble() * 7 : 0,
                        displayName = Names[(look + seeds.Count) % Names.Length] });
                    picked++;
                }
                if (picked < count) throw new InvalidOperationException("Pedestrian spacing permits only " + picked + " of " + count + " requested people in " + route.label);
            }
            var root = new GameObject("SlopCity_Crowd_" + seeds.Count);
            if (parent) root.transform.SetParent(parent, false);
            SlopCityCrowd crowd = root.AddComponent<SlopCityCrowd>();
            crowd.Configure(SlopCityHumanMeshes.Build(), routes.ToArray(), seeds.ToArray());
            EditorUtility.SetDirty(crowd);
            Debug.Log("SLOP_CITY_CROWD_BAKED " + seeds.Count + " people / " + routes.Count + " pedestrian areas / " + routes.Sum(r => r.nodes.Length) + " checked nodes");
            return crowd;
        }

        private static SlopCityNpcRole Role(int district, int number)
        {
            if (district >= 10) return number % 9 == 0 ? SlopCityNpcRole.Security : number % 7 == 0 ? SlopCityNpcRole.Worker : SlopCityNpcRole.Commuter;
            if (district == 5 && number % 3 == 0) return SlopCityNpcRole.Vendor;
            if ((district == 1 || district == 6) && number % 3 == 0) return SlopCityNpcRole.Worker;
            if (district == 2 && number % 3 == 0) return SlopCityNpcRole.Resident;
            return (SlopCityNpcRole)((number + district) % 6);
        }

        private static SlopCityCrowdRoute Bake(string label, int district, Vector3 origin, Vector2 min, Vector2 max)
        {
            int width = Mathf.FloorToInt((max.x - min.x) / Spacing) + 1, depth = Mathf.FloorToInt((max.y - min.y) / Spacing) + 1;
            int count = width * depth; var sampled = new Vector3[count]; var valid = new bool[count]; var links = new List<int>[count];
            for (int z = 0; z < depth; z++) for (int x = 0; x < width; x++)
            {
                int index = z * width + x; links[index] = new List<int>();
                Vector3 hint = origin + new Vector3(min.x + x * Spacing, 0, min.y + z * Spacing);
                valid[index] = TryFeet(hint, out sampled[index]);
            }
            for (int z = 0; z < depth; z++) for (int x = 0; x < width; x++)
            {
                int index = z * width + x; if (!valid[index]) continue;
                foreach (int other in new[] { x + 1 < width ? index + 1 : -1, z + 1 < depth ? index + width : -1 })
                {
                    if (other < 0 || !valid[other] || !ClearEdge(sampled[index], sampled[other])) continue;
                    links[index].Add(other); links[other].Add(index);
                }
            }
            // Only use the largest connected public pavement component. Closed rooms stay unpopulated.
            var visited = new bool[count]; var largest = new List<int>();
            for (int start = 0; start < count; start++)
            {
                if (!valid[start] || visited[start] || links[start].Count == 0) continue;
                var component = new List<int>(); var queue = new Queue<int>(); queue.Enqueue(start); visited[start] = true;
                while (queue.Count > 0)
                {
                    int node = queue.Dequeue(); component.Add(node);
                    foreach (int other in links[node]) if (!visited[other]) { visited[other] = true; queue.Enqueue(other); }
                }
                if (component.Count > largest.Count) largest = component;
            }
            largest.Sort(); var map = new Dictionary<int, int>();
            for (int i = 0; i < largest.Count; i++) map.Add(largest[i], i);
            var result = new SlopCityCrowdNode[largest.Count];
            for (int i = 0; i < largest.Count; i++) result[i] = new SlopCityCrowdNode { position = sampled[largest[i]], neighbors = links[largest[i]].Select(node => map[node]).ToArray() };
            return new SlopCityCrowdRoute { label = label, districtIndex = district, nodes = result };
        }
        private static bool Relevant(Collider collider) => collider && !collider.isTrigger && collider != player
            && (!player || !collider.transform.IsChildOf(player.transform));
        private static bool TryFeet(Vector3 hint, out Vector3 feet)
        {
            feet = hint;
            RaycastHit[] hits = Physics.RaycastAll(hint + Vector3.up * .70f, Vector3.down, 1.35f, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => Relevant(hit.collider) && hit.normal.y > .78f).OrderBy(hit => hit.distance).ToArray();
            if (hits.Length == 0) return false;
            feet = hits[0].point + Vector3.up * .025f;
            if (Mathf.Abs(feet.y - hint.y) > .24f) return false;
            return !Physics.OverlapCapsule(feet + Vector3.up * .34f, feet + Vector3.up * 1.64f, Radius,
                ~0, QueryTriggerInteraction.Ignore).Any(Relevant);
        }
        private static bool ClearEdge(Vector3 from, Vector3 to)
        {
            if (Mathf.Abs(from.y - to.y) > .20f) return false;
            for (int step = 1; step <= 3; step++)
                if (!TryFeet(Vector3.Lerp(from, to, step / 4f), out Vector3 supported) || Mathf.Abs(supported.y - Mathf.Lerp(from.y, to.y, step / 4f)) > .10f) return false;
            Vector3 delta = to - from;
            return !Physics.CapsuleCastAll(from + Vector3.up * .34f, from + Vector3.up * 1.64f, Radius,
                delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore).Any(hit => Relevant(hit.collider));
        }
    }
}
#endif
