using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    public enum SlopCityNpcRole { Commuter, Vendor, Worker, Courier, Security, Resident }
    // Append slots: older saved scenes retain their original mesh indices.
    public enum SlopCityHumanPart { Jacket, Coat, Vest, Pelvis, UpperArm, Forearm, Hand, Thigh, Shin, Shoe, Head, HairShort, HairBob, HairTied, Face, Eyes, Cap, Bag, Phone, VestStripes, GarmentSeams, GarmentPanels, ArmorTorso, ArmorShoulder, ArmorForearm, ArmorShin, Helmet, Visor, UtilityBelt, ShoeSole, HelmetHardware, ChestBadge,
        CyberFaceShell, CyberFaceHardware, CyberFaceSignal, CyberNeck, CyberForearm, CyberArmMechanism, CyberArmSignal, CyberHandHardware, CyberShin, CyberShinMechanism, CyberShinSignal,
        CyberFaceShellLeft, CyberFaceHardwareLeft, CyberFaceSignalLeft }

    [Serializable] public sealed class SlopCityCrowdNode
    {
        public Vector3 position;
        public int[] neighbors = Array.Empty<int>();
    }
    [Serializable] public sealed class SlopCityCrowdRoute
    {
        public string label;
        public int districtIndex;
        public SlopCityCrowdNode[] nodes = Array.Empty<SlopCityCrowdNode>();
    }
    [Serializable] public sealed class SlopCityCrowdVisuals
    {
        public Mesh[] parts = Array.Empty<Mesh>();
        public Material[] clothing = Array.Empty<Material>();
        public Material[] skin = Array.Empty<Material>();
        public Material[] hair = Array.Empty<Material>();
        public Material shoes, eyes, accessories, safety;
        public Material armor, rubber, visor, seams, accent;
        public Material cyberMetal, cyberJoint, cyberSignal;
    }
    [Serializable] public struct SlopCityNpcSeed
    {
        public int route, node, next, appearance;
        public SlopCityNpcRole role;
        public float height, width, speed, phase, heading, pause;
        public string displayName;
        public int residentId;
    }
    public struct SlopCityNpcSnapshot
    {
        public int residentId;
        public bool detailed, presented, interior;
        public Vector3 position;
        public float heading, height, talkCooldown;
        public bool moving;
        public SlopCityNpcRole role;
        public int districtIndex, routeIndex, nodeIndex;
        public string displayName;
    }

    /// <summary>Baked, obstruction-checked pedestrian routes and instanced articulated human meshes.</summary>
    [ExecuteAlways]
    public sealed partial class SlopCityCrowd : MonoBehaviour
    {
        [SerializeField] private SlopCityCrowdVisuals visuals = new SlopCityCrowdVisuals();
        [SerializeField] private SlopCityCrowdRoute[] routes = Array.Empty<SlopCityCrowdRoute>();
        [SerializeField] private SlopCityNpcSeed[] population = Array.Empty<SlopCityNpcSeed>();
        private Agent[] agents = Array.Empty<Agent>();
        private readonly List<DrawBatch> batches = new List<DrawBatch>();
        private readonly Dictionary<long, DrawBatch> batchLookup = new Dictionary<long, DrawBatch>();
        private readonly Plane[] frustum = new Plane[6];
        private readonly RaycastHit[] sweepHits = new RaycastHit[12];
        private readonly int[] gridHeads = new int[70 * 70];
        private float simulationTime;
        private Transform player;
        private SlopCityFirstPerson playerController;
        private bool configured;
        private MatrixWardrobe wardrobe;
        private bool wardrobeAllocationFailed;
        private Material failedWardrobeSource;
        private int redContactCount;
        private readonly SlopCityLocalAgent localAgents = new SlopCityLocalAgent();
        public event Action<string, string> LocalAgentReportRequested;
#if UNITY_WEBGL && !UNITY_EDITOR
        private const float BrowserSimulationStep = .05f;
        private float browserSimulationRemainder;
        private float browserInterpolation = 1f;
#endif
        public static SlopCityCrowd Instance { get; private set; }
        public int PopulationCount { get { EnsureAgents(); return residents.Length; } }
        public int RedContactCount { get { EnsureAgents(); return redContactCount; } }
        public bool IsRedContact(int index)
        {
            EnsureAgents();
            return index >= 0 && index < residents.Length && residents[index].redContact;
        }
        public int MovingCount { get { EnsureAgents(); int count = 0; for(int i=0;i<activeCount;i++) if(agents[i].moving)count++; return count; } }
        public float SimulationTime => simulationTime;
        public IReadOnlyList<SlopCityCrowdRoute> Routes => routes ?? Array.Empty<SlopCityCrowdRoute>();
        public SlopCityCrowdVisuals Visuals => visuals;
        public bool LayoutRevalidated { get; private set; }
        public int RevalidatedNodeCount { get; private set; }
        public int RevalidatedEdgeCount { get; private set; }
        public int RejectedLayoutNodeCount { get; private set; }
        public float LayoutMinimumSeedSpacing { get; private set; }

        private sealed class Agent
        {
            public SlopCityNpcSeed seed;
            public int residentIndex;
            public Vector3 position;
            public int node, next, previous, gridNext, randomState;
            public float heading, phase, pause, talkReady, obstacleCheck, blocked;
            public bool moving, redContact;
#if UNITY_WEBGL && !UNITY_EDITOR
            [System.NonSerialized] public Vector3 previousPosition;
            [System.NonSerialized] public float previousHeading;
#endif
        }
        private sealed class DrawBatch
        {
            public Mesh mesh;
            public Material material;
            public Matrix4x4[] matrices;
            public int count, reserved;
        }

        public void Configure(SlopCityCrowdVisuals meshLibrary, SlopCityCrowdRoute[] bakedRoutes, SlopCityNpcSeed[] people)
        {
            ReleaseWardrobe();
            visuals = meshLibrary ?? new SlopCityCrowdVisuals();
            routes = bakedRoutes ?? Array.Empty<SlopCityCrowdRoute>();
            population = people == null ? Array.Empty<SlopCityNpcSeed>() : (SlopCityNpcSeed[])people.Clone();
            LayoutRevalidated = false;
            RevalidatedNodeCount = RevalidatedEdgeCount = RejectedLayoutNodeCount = 0;
            LayoutMinimumSeedSpacing = 0;
            ResetPopulation(); configured = false; EnsureAgents();
        }

        /// <summary>Move and recheck the baked pavement graph after all physical street geometry moves.</summary>
        public void RemapLayout(Func<int, Vector3, Vector3> remap)
        {
            if (remap == null || routes == null) return;
            if (routes.Length > 64 || PopulationCount > 5000)
                throw new InvalidOperationException("Pedestrian remap exceeds the bounded city population/route budget.");
            // Configure may receive caller-owned arrays, and the saved scene also owns these
            // nodes. Build replacements without mutating either until every person has a home.
            var replacements = new SlopCityCrowdRoute[routes.Length];
            var maps = new int[routes.Length][];
            var hints = new Vector3[routes.Length][];
            CharacterController controller = FindFirstObjectByType<CharacterController>();
            int originalCount = 0, nodeCount = 0, edgeCount = 0;
            for (int routeIndex = 0; routeIndex < routes.Length; routeIndex++)
            {
                SlopCityCrowdRoute route = routes[routeIndex];
                if (route == null || route.nodes == null)
                    throw new InvalidOperationException("Pedestrian remap found a missing route: " + routeIndex);
                originalCount += route.nodes.Length;
                if (originalCount > 65536)
                    throw new InvalidOperationException("Pedestrian remap exceeds the bounded pavement node budget.");
                replacements[routeIndex] = RevalidateRoute(route, remap, controller, out maps[routeIndex], out hints[routeIndex]);
                nodeCount += replacements[routeIndex].nodes.Length;
                foreach (SlopCityCrowdNode node in replacements[routeIndex].nodes) edgeCount += node.neighbors.Length;
            }
            var people = population == null ? Array.Empty<SlopCityNpcSeed>() : (SlopCityNpcSeed[])population.Clone();
            var occupied = new List<Vector3>(people.Length);
            float minimumSpacing = float.PositiveInfinity;
            for (int index = 0; index < people.Length; index++)
            {
                SlopCityNpcSeed seed = people[index];
                if (seed.route < 0 || seed.route >= replacements.Length || seed.node < 0 || seed.node >= maps[seed.route].Length)
                    throw new InvalidOperationException("Pedestrian remap found an invalid seed route/node: " + index);
                SlopCityCrowdNode[] nodes = replacements[seed.route].nodes;
                int preferred = maps[seed.route][seed.node], chosen = -1;
                float nearest = float.PositiveInfinity;
                for (int candidate = 0; candidate < nodes.Length; candidate++)
                {
                    Vector3 point = nodes[candidate].position;
                    bool spaced = true;
                    foreach (Vector3 other in occupied)
                        if ((point - other).sqrMagnitude < .75f * .75f) { spaced = false; break; }
                    if (!spaced) continue;
                    if (candidate == preferred) { chosen = candidate; break; }
                    float distance = (point - hints[seed.route][seed.node]).sqrMagnitude;
                    if (distance < nearest) { nearest = distance; chosen = candidate; }
                }
                if (chosen < 0)
                    throw new InvalidOperationException("Clear connected pavement cannot fit all preserved people at 0.75m spacing in "
                        + replacements[seed.route].label + "; seed " + index + ".");
                Vector3 position = nodes[chosen].position;
                foreach (Vector3 other in occupied) minimumSpacing = Mathf.Min(minimumSpacing, Vector3.Distance(position, other));
                occupied.Add(position);
                int next = seed.next >= 0 && seed.next < maps[seed.route].Length ? maps[seed.route][seed.next] : -1;
                int[] links = nodes[chosen].neighbors;
                if (Array.IndexOf(links, next) < 0) next = links[(int)(((uint)seed.appearance + (uint)index) % (uint)links.Length)];
                seed.node = chosen; seed.next = next;
                Vector3 direction = nodes[next].position - position;
                seed.heading = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                people[index] = seed;
            }
            SlopCityCrowdRoute[] priorRoutes = routes;
            SlopCityNpcSeed[] priorPopulation = population;
            Agent[] priorAgents = agents;
            bool priorConfigured = configured;
            float priorTime = simulationTime;
            try
            {
                routes = replacements; population = people; configured = false;
                EnsureAgents();
            }
            catch
            {
                routes = priorRoutes; population = priorPopulation; agents = priorAgents;
                configured = priorConfigured; simulationTime = priorTime;
                throw;
            }
            LayoutRevalidated = true;
            RevalidatedNodeCount = nodeCount; RevalidatedEdgeCount = edgeCount / 2;
            RejectedLayoutNodeCount = originalCount - nodeCount;
            LayoutMinimumSeedSpacing = people.Length > 1 ? minimumSpacing : 0;
        }

        private static SlopCityCrowdRoute RevalidateRoute(SlopCityCrowdRoute route, Func<int, Vector3, Vector3> remap,
            CharacterController controller, out int[] map, out Vector3[] hints)
        {
            int count = route.nodes.Length;
            hints = new Vector3[count];
            var points = new Vector3[count]; var valid = new bool[count]; var links = new List<int>[count];
            for (int index = 0; index < count; index++)
            {
                links[index] = new List<int>();
                if (route.nodes[index] == null) continue;
                Vector3 point = remap(route.districtIndex, route.nodes[index].position);
                hints[index] = point;
                valid[index] = TryLayoutFeet(point, controller, out points[index]);
            }
            var checkedEdges = new HashSet<long>();
            for (int index = 0; index < count; index++)
            {
                if (!valid[index] || route.nodes[index].neighbors == null) continue;
                foreach (int neighbor in route.nodes[index].neighbors)
                {
                    if (neighbor < 0 || neighbor >= count || neighbor == index || !valid[neighbor]) continue;
                    long key = ((long)Mathf.Min(index, neighbor) << 32) | (uint)Mathf.Max(index, neighbor);
                    if (!checkedEdges.Add(key) || !ClearLayoutEdge(points[index], points[neighbor], controller)) continue;
                    links[index].Add(neighbor); links[neighbor].Add(index);
                }
            }
            var visited = new bool[count]; var largest = new List<int>(); var queue = new Queue<int>();
            for (int start = 0; start < count; start++)
            {
                if (!valid[start] || visited[start] || links[start].Count == 0) continue;
                var component = new List<int>(); queue.Enqueue(start); visited[start] = true;
                while (queue.Count > 0)
                {
                    int node = queue.Dequeue(); component.Add(node);
                    foreach (int neighbor in links[node])
                        if (!visited[neighbor]) { visited[neighbor] = true; queue.Enqueue(neighbor); }
                }
                if (component.Count > largest.Count) largest = component;
            }
            if (largest.Count < 2)
                throw new InvalidOperationException("Pedestrian remap has no supported connected public pavement in " + route.label + ".");
            largest.Sort(); map = new int[count]; Array.Fill(map, -1);
            for (int index = 0; index < largest.Count; index++) map[largest[index]] = index;
            var nodes = new SlopCityCrowdNode[largest.Count];
            for (int index = 0; index < largest.Count; index++)
            {
                int source = largest[index];
                var neighbors = new int[links[source].Count];
                for (int edge = 0; edge < neighbors.Length; edge++) neighbors[edge] = map[links[source][edge]];
                nodes[index] = new SlopCityCrowdNode { position = points[source], neighbors = neighbors };
            }
            return new SlopCityCrowdRoute { label = route.label, districtIndex = route.districtIndex, nodes = nodes };
        }

        private static bool LayoutCollider(Collider collider, CharacterController controller)
            => collider && !collider.isTrigger && collider != controller
                && (!controller || !collider.transform.IsChildOf(controller.transform));

        private static bool TryLayoutFeet(Vector3 hint, CharacterController controller, out Vector3 feet)
        {
            feet = hint;
            if (!Finite(hint.sqrMagnitude)) return false;
            RaycastHit[] hits = Physics.RaycastAll(hint + Vector3.up * .70f, Vector3.down, 1.35f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            foreach (RaycastHit hit in hits)
                if (LayoutCollider(hit.collider, controller) && hit.normal.y > .78f && hit.distance < nearest)
                { nearest = hit.distance; feet = hit.point + Vector3.up * .025f; }
            if (!Finite(nearest) || Mathf.Abs(feet.y - hint.y) > .24f) return false;
            foreach (Collider collider in Physics.OverlapCapsule(feet + Vector3.up * .34f, feet + Vector3.up * 1.64f, .32f,
                ~0, QueryTriggerInteraction.Ignore))
                if (LayoutCollider(collider, controller)) return false;
            return true;
        }

        private static bool ClearLayoutEdge(Vector3 from, Vector3 to, CharacterController controller)
        {
            if (Mathf.Abs(from.y - to.y) > .20f) return false;
            Vector3 delta = to - from;
            // Saved edges are short pavement-grid neighbors. Refuse malformed routes
            // rather than doing unbounded support sampling over arbitrary world distances.
            if (delta.sqrMagnitude < .0001f || delta.sqrMagnitude > 4f) return false;
            for (int step = 1; step <= 3; step++)
            {
                Vector3 hint = Vector3.Lerp(from, to, step / 4f);
                if (!TryLayoutFeet(hint, controller, out Vector3 feet) || Mathf.Abs(feet.y - hint.y) > .10f) return false;
            }
            foreach (RaycastHit hit in Physics.CapsuleCastAll(from + Vector3.up * .34f, from + Vector3.up * 1.64f, .32f,
                delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (LayoutCollider(hit.collider, controller)) return false;
            return true;
        }
        private void OnEnable()
        {
            Instance = this; EnsureAgents();
            Camera.onPreCull -= RenderCrowd; Camera.onPreCull += RenderCrowd;
        }
        private void OnDisable()
        {
            Camera.onPreCull -= RenderCrowd;
            if (Instance == this) Instance = null;
            batches.Clear(); batchLookup.Clear();
            ReleaseWardrobe();
        }
        private void EnsureAgents()
        {
            if (configured) return;
            InitializePopulationRegistry();
        }
        private static uint ContactOrder(SlopCityNpcSeed seed, int index)
        {
            uint value = unchecked((uint)seed.appearance * 747796405u + (uint)index * 2891336453u + 19841984u);
            value = (value ^ (value >> 16)) * 2246822519u;
            return value ^ (value >> 13);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private bool ValidNode(int route, int node) { SlopCityCrowdRoute graph=PopulationRoute(route); return graph != null && graph.nodes != null && node >= 0 && node < graph.nodes.Length && graph.nodes[node] != null && Finite(graph.nodes[node].position.sqrMagnitude); }
        private void Update()
        {
            if (!Application.isPlaying) return;
            if (!playerController)
            {
                playerController = FindFirstObjectByType<SlopCityFirstPerson>();
                if (playerController) player = playerController.transform;
            }
            TryPrepareExpandedPopulation();
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browser simulation runs at 20 Hz. Rendering interpolates positions
            // and headings so reducing CPU work does not step visible movement.
            if (Time.timeScale <= 0 || (playerController && playerController.IsPaused))
            {
                browserSimulationRemainder = 0;
                browserInterpolation = 1;
                return;
            }
            browserSimulationRemainder += Mathf.Min(Time.deltaTime, .1f);
            while (browserSimulationRemainder >= BrowserSimulationStep)
            {
                AdvanceSimulation(BrowserSimulationStep);
                browserSimulationRemainder -= BrowserSimulationStep;
            }
            browserInterpolation = browserSimulationRemainder / BrowserSimulationStep;
#else
            AdvanceSimulation(Time.deltaTime);
#endif
        }

        public void AdvanceSimulation(float deltaTime)
        {
            EnsureAgents();
            if (!Finite(deltaTime) || deltaTime <= 0 || Time.timeScale <= 0 || (playerController && playerController.IsPaused)) return;
            // Keep collision prediction bounded after backgrounding the app.
            float delta = Mathf.Min(deltaTime, .1f); simulationTime += delta;
            RefreshPopulationSlots(delta);
            Array.Fill(gridHeads, -1);
            for (int i = 0; i < activeCount; i++)
            {
                Agent person = agents[i]; int cell = Cell(person.position);
#if UNITY_WEBGL && !UNITY_EDITOR
                person.previousPosition = person.position;
                person.previousHeading = person.heading;
#endif
                person.gridNext = gridHeads[cell]; gridHeads[cell] = i;
            }
            for (int i = 0; i < activeCount; i++)
            {
                Agent person = agents[i]; person.moving = false;
                if (!ValidNode(person.seed.route, person.node) || !ValidNode(person.seed.route, person.next)) continue;
                if (person.pause > 0) { person.pause = Mathf.Max(0, person.pause - delta); continue; }
                Vector3 destination = PopulationRoute(person.seed.route).nodes[person.next].position;
                Vector3 difference = destination - person.position;
                if (difference.sqrMagnitude < .000625f)
                {
                    person.position = destination; person.previous = person.node; person.node = person.next;
                    ChooseNext(person);
                    if (person.seed.role == SlopCityNpcRole.Vendor || person.seed.role == SlopCityNpcRole.Security)
                    {
                        if (Random01(person) < .18f) person.pause = 2 + Random01(person) * 6;
                    }
                    else if (Random01(person) < .025f) person.pause = 1 + Random01(person) * 3;
                    destination = PopulationRoute(person.seed.route).nodes[person.next].position; difference = destination - person.position;
                }
                if (difference.sqrMagnitude < .0001f || person.pause > 0) continue;
                Vector3 direction = difference.normalized;
                float factor = NeighbourSpeed(i, direction);
                if (factor < .10f)
                {
                    person.blocked += delta;
                    if (person.blocked > 1.4f + i % 7 * .13f)
                    {
                        // Return along the same checked edge before choosing another direction.
                        int oldNode = person.node; person.node = person.next; person.next = oldNode;
                        person.previous = -1; person.blocked = 0; person.pause = .08f + (i % 4) * .09f;
                    }
                    continue;
                }
                if (player && (player.position - person.position).sqrMagnitude < 28 * 28)
                {
                    if (simulationTime >= person.obstacleCheck)
                    {
                        person.obstacleCheck = simulationTime + .25f;
                        if (BlockedByWorld(person.position, direction, Mathf.Min(.55f, difference.magnitude))) { person.pause = .35f; continue; }
                    }
                    Vector3 toPlayer = player.position - person.position; toPlayer.y = 0;
                    if (toPlayer.sqrMagnitude < .9f * .9f && Vector3.Dot(toPlayer, direction) > 0) { person.pause = .12f; continue; }
                }
                person.blocked = 0; person.moving = true;
                float step = person.seed.speed * factor * delta;
                person.position = Vector3.MoveTowards(person.position, destination, step);
                person.heading = Mathf.MoveTowardsAngle(person.heading, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, delta * 220);
                person.phase += step * 6.7f;
            }
        }

        private void ChooseNext(Agent person)
        {
            int[] links = PopulationRoute(person.seed.route).nodes[person.node].neighbors;
            if (links == null || links.Length == 0) { person.next = person.node; return; }
            Vector3 forward = Quaternion.Euler(0, person.heading, 0) * Vector3.forward;
            float best = float.NegativeInfinity; int chosen = person.node;
            foreach (int next in links)
            {
                if (!ValidNode(person.seed.route, next)) continue;
                Vector3 direction = (PopulationRoute(person.seed.route).nodes[next].position - person.position).normalized;
                float weight = Random01(person) + Vector3.Dot(forward, direction) * .65f - (next == person.previous && links.Length > 1 ? 1.2f : 0);
                if (weight > best) { best = weight; chosen = next; }
            }
            person.next = chosen;
        }
        private static float Random01(Agent person)
        {
            person.randomState = unchecked(person.randomState * 1664525 + 1013904223);
            return (uint)person.randomState / (float)uint.MaxValue;
        }
        private static int Cell(Vector3 position)
        {
            // A six-metre cell covers the expanded 366m street grid with the same bounded allocation.
            return Mathf.Clamp(Mathf.FloorToInt((position.z + 210) / 6), 0, 69) * 70
                + Mathf.Clamp(Mathf.FloorToInt((position.x + 210) / 6), 0, 69);
        }
        private float NeighbourSpeed(int index, Vector3 direction)
        {
            int visited=0, compared=0;
            Agent current = agents[index]; int cell = Cell(current.position), cx = cell % 70, cz = cell / 70;
            float factor = 1;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || x >= 70 || z < 0 || z >= 70) continue;
                for (int other = gridHeads[z * 70 + x]; other >= 0; other = agents[other].gridNext)
                {
                    if (visited >= MaximumNeighbourVisits) return factor;
                    visited++; MaximumObservedNeighbourVisits=Mathf.Max(MaximumObservedNeighbourVisits,visited);
                    if (other == index) continue;
                    Vector3 difference = agents[other].position - current.position;
                    if (Mathf.Abs(difference.y) > 1 || difference.sqrMagnitude > 1.25f) continue;
                    if (++compared > MaximumNeighbourCandidates) return factor;
                    MaximumObservedNeighbourCandidates=Mathf.Max(MaximumObservedNeighbourCandidates,compared);
                    float ahead = Vector3.Dot(difference, direction);
                    float sideways = (difference - direction * ahead).magnitude;
                    if (ahead > .02f && ahead < .86f && sideways < .44f)
                        factor = Mathf.Min(factor, Mathf.Clamp01((ahead - .42f) / .42f));
                }
            }
            return factor;
        }
        private bool BlockedByWorld(Vector3 feet, Vector3 direction, float distance)
        {
            int count = Physics.CapsuleCastNonAlloc(feet + Vector3.up * .53f, feet + Vector3.up * 1.60f, .26f,
                direction, sweepHits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = sweepHits[i].collider;
                if (!hit || (player && (hit.transform == player || hit.transform.IsChildOf(player)))) continue;
                return true;
            }
            return false;
        }

        public SlopCityNpcSnapshot Snapshot(int index)
        {
            EnsureAgents();
            if(index<0||index>=residents.Length)throw new ArgumentOutOfRangeException(nameof(index));
            Resident record=residents[index]; Agent person=ActiveResident(index);
            return new SlopCityNpcSnapshot { residentId=record.seed.residentId, detailed=person!=null, presented=IsPresented(index), interior=record.home>=0,
                position=person!=null?person.position:LogicalPosition(index), heading=person!=null?person.heading:record.heading,
                height=record.seed.height,moving=person!=null&&person.moving,talkCooldown=Mathf.Max(0,(person!=null?person.talkReady:record.talkReady)-simulationTime),
                role=record.seed.role,districtIndex=ValidNode(record.seed.route,record.node)?PopulationRoute(record.seed.route).districtIndex:-1,
                routeIndex=record.seed.route,nodeIndex=person!=null?person.node:record.node,displayName=record.seed.displayName };
        }
        public SlopCityNpcSnapshot GetSnapshot(int index) => Snapshot(index);
        public string TalkPrompt(int index)
        {
            EnsureAgents();
            if (index < 0 || index >= residents.Length || ActiveResident(index)==null) return "";
            return residents[index].redContact ? "TALK TO WOMAN IN RED" : "TALK TO AGENT SMITH";
        }
        public bool TryFindTarget(Ray ray, float maximumDistance, out int index, out Vector3 point)
        {
            EnsureAgents(); index = -1; point = default; float closest = maximumDistance;
            for (int i = 0; i < activeCount; i++)
            {
                Agent person = agents[i];
                if (!ValidNode(person.seed.route, person.node) || !IsPresented(person.residentIndex)) continue;
                Bounds body = new Bounds(person.position + Vector3.up * person.seed.height * .53f,
                    new Vector3(.48f * person.seed.width, person.seed.height * .92f, .42f * person.seed.width));
                if (!body.IntersectRay(ray, out float distance) || distance < .01f || distance >= closest) continue;
                closest = distance; index = person.residentIndex; point = ray.GetPoint(distance);
            }
            return index >= 0;
        }
        public bool TryTalk(int index, Vector3 observer, out string line)
        {
            EnsureAgents(); line = ""; Agent person=ActiveResident(index); if(person==null || !IsPresented(index))return false;
            if (!ValidNode(person.seed.route, person.node)) return false;
            string label = person.seed.displayName;
            if (person.talkReady > simulationTime)
            {
                line = label + ": Give me a moment — " + Mathf.CeilToInt(person.talkReady - simulationTime) + "s."; return true;
            }
            person.pause = 4.5f; person.moving = false; person.talkReady = simulationTime + 12;
            Vector3 toward = observer - person.position; toward.y = 0;
            if (toward.sqrMagnitude > .001f) person.heading = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
            string[] advice = person.redContact ? RedDialogue : Dialogue[(int)person.seed.role];
            int variant = (int)(((long)person.seed.appearance + Mathf.FloorToInt(simulationTime / 12)) % advice.Length);
            line = person.seed.displayName + ": " + advice[variant]; return true;
        }

        /// <summary>Optional municipal evidence access; ordinary conversation never opens a report.</summary>
        public bool TryRequestLocalAgentReport(int index)
        {
            EnsureAgents();
            if (index < 0 || index >= residents.Length || residents[index].redContact || ActiveResident(index)==null || !IsPresented(index)) return false;
            int localSlot = localAgents.SlotFor(index);
            SlopCitySandbox registry = playerController ? playerController.Sandbox : null;
            if (localSlot < 0 || !registry) return false;
            SlopCityBuildingWorld world = GetComponentInParent<SlopCityBuildingWorld>();
            SlopCityCityMedia media = SlopCityCityMedia.Instance;
            Vector3 position = playerController.transform.position;
            string snapshot = "LOCAL STATE / " + playerController.CurrentDistrictName
                + "\nBuildings: " + (world ? world.Buildings.Count.ToString() : "not ready")
                + "; live floors: " + (world ? world.ActiveFloorCount.ToString() : "unknown")
                + "; lifts: " + (world ? world.ActiveElevatorCount.ToString() : "unknown")
                + "\nCity screens: " + (media ? media.ScreenCount.ToString() : "not ready")
                + "; decoder limit: " + (media ? media.DecoderLimit.ToString() : "unknown")
                + "; city paused: " + (media ? media.IsPaused.ToString() : "unknown")
                + "\nParts: " + registry.Salvage + "; water: " + registry.WaterSupply + "; field readings: " + registry.FieldReadings;
            string report = localAgents.Inspect(localSlot, registry.MunicipalLog, registry.MunicipalState,
                position.x, position.y, position.z, Time.time, snapshot);
            LocalAgentReportRequested?.Invoke("AGENT SMITH / THE MUNICIPAL MATRIX", report);
            return true;
        }
        private static readonly string[] RedDialogue =
        {
            "You noticed the red dress. Keep looking at the streets, not just the screens.",
            "There are others like me across the city. Walk the districts and look carefully.",
            "The suit is a uniform. A choice can stand out, even here."
        };
        private static readonly string[][] Dialogue =
        {
            new[] { "Your route is predictable. The subway connects the platforms below the street lifts.", "A red dress interrupts the pattern. Look across the district before you move on.", "The rooftop lifts are operational. Observation improves with elevation.", "You may use a parked cruiser. The streets still belong to the pedestrians." },
            new[] { "The market remains open. Even this system requires a meal.", "Supply dispensers replenish a work kit. Scarcity is often a matter of attention.", "The advertising channels can be tuned at the gas pumps. Control is local.", "Dispatch has another contract. Complete the current stop to receive it." },
            new[] { "Recover a component. Install it in a terminal. File the reading. Cause and effect.", "The bins replenish salvage with time. Repetition has its uses.", "The lamps have switches. Darkness is a choice you can reverse.", "A hydrant pressure test reports the actual supply. Evidence is preferable to belief." },
            new[] { "Follow the white rabbits. The next transmission identifies your delivery.", "Dispatch permits rerouting. An inaccessible destination serves no purpose.", "A vehicle shortens the distance between districts. Keep the pavement clear.", "Sorted recycling becomes repair parts at the junction. The system wastes very little." },
            new[] { "Keep the station doors clear. The platform lift returns you to street level.", "Every suit looks familiar. Perhaps you should look for something that does not.", "The rooftop landings are public. Stay close to the lift equipment.", "A train journey can be cancelled before arrival. A decision need not be permanent." },
            new[] { "You are looking for a deviation. Red is a useful place to start.", "Each billboard has its own channel controls. Appearances can be changed.", "A fountain and a bench restore you after a long walk. Biological limitations persist.", "There is more than one district. Do not mistake a familiar street for the entire city." }
        };

        private readonly int[] visibleOrder=new int[MaximumPresentedNative];
        private readonly float[] visibleDistance=new float[MaximumPresentedNative];
        private int lastPresentationFrame=-10;
        private bool reserveOnly,reservationFailed;
        private struct PendingPart { public DrawBatch batch; public Matrix4x4 matrix; }
        private readonly PendingPart[] pendingParts=new PendingPart[64];
        private int pendingPartCount;
        public int MaximumPresentedCount
        {
            get {
#if (UNITY_WEBGL && !UNITY_EDITOR) || SLOP_CITY_WEB_INTERACTION_TEST
                return MaximumPresentedBrowser;
#else
                return MaximumPresentedNative;
#endif
            }
        }
        public bool IsResidentPresented(int index)=>IsPresented(index);
        private void RenderCrowd(Camera camera)
        {
            if (!isActiveAndEnabled || !camera || camera.cameraType == CameraType.Preview || !SystemInfo.supportsInstancing) return;
            if(Application.isPlaying && camera != (playerController?playerController.ViewCamera:Camera.main))return;
            EnsureAgents();
            if (visuals == null || visuals.parts == null || visuals.parts.Length < 20 || visuals.clothing == null || visuals.clothing.Length == 0
                || visuals.skin == null || visuals.skin.Length == 0 || visuals.hair == null || visuals.hair.Length == 0) return;
            if (!EnsureWardrobe()) return;
            foreach (DrawBatch batch in batches) batch.count = 0;
            if(presentationSerial==int.MaxValue){Array.Clear(presentationStamp,0,presentationStamp.Length);presentationSerial=0;}
            presentationSerial++;lastPresentationFrame=Time.frameCount;PresentedCount=WholePeopleDeferred=0;
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);Vector3 eye=camera.transform.position;
            int selected=0,eligible=0,budget=MaximumPresentedCount;
            for(int slot=0;slot<activeCount;slot++)
            {
                Agent person=agents[slot];if(!ValidNode(person.seed.route,person.node))continue;
                float distance=(person.position-eye).sqrMagnitude;
#if UNITY_WEBGL && !UNITY_EDITOR
                if(distance>60*60)continue;
#else
                if(distance>135*135)continue;
#endif
                if(!GeometryUtility.TestPlanesAABB(frustum,new Bounds(person.position+Vector3.up*person.seed.height*.5f,new Vector3(.9f,person.seed.height+.15f,.9f))))continue;
                eligible++;
                // Stable logical identity breaks distance ties. A nearby conversation gets priority.
                float priority=person.talkReady>simulationTime&&distance<16f?-1f:distance;
                int insert=selected;
                while(insert>0&&(priority<visibleDistance[insert-1]||(Mathf.Approximately(priority,visibleDistance[insert-1])&&person.seed.residentId<agents[visibleOrder[insert-1]].seed.residentId)))insert--;
                if(insert>=budget)continue;int end=Mathf.Min(selected,budget-1);
                for(int i=end;i>insert;i--){visibleOrder[i]=visibleOrder[i-1];visibleDistance[i]=visibleDistance[i-1];}
                visibleOrder[insert]=slot;visibleDistance[insert]=priority;if(selected<budget)selected++;
            }
            WholePeopleDeferred=eligible-selected;
            for(int index=0;index<selected;index++)
            {
                Agent person=agents[visibleOrder[index]];float distance=(person.position-eye).sqrMagnitude;
#if UNITY_WEBGL && !UNITY_EDITOR
                bool details=distance<12*12;
#else
                bool details=distance<32*32;
#endif
                if(!TryReservePerson(person,details)){WholePeopleDeferred++;continue;}
                CommitReservedPerson();presentationStamp[person.residentIndex]=presentationSerial;PresentedCount++;
            }
            foreach (DrawBatch batch in batches)
                if (batch.count > 0 && batch.mesh && batch.material && batch.material.enableInstancing) Graphics.DrawMeshInstanced(batch.mesh, 0, batch.material, batch.matrices, batch.count,
#if UNITY_WEBGL && !UNITY_EDITOR
                    null, ShadowCastingMode.Off, false, gameObject.layer, camera, LightProbeUsage.Off);
#else
                    null, ShadowCastingMode.On, true, gameObject.layer, camera, LightProbeUsage.BlendProbes);
#endif
        }
        private bool TryReservePerson(Agent person,bool details)
        {
            foreach(DrawBatch batch in batches)batch.reserved=0;pendingPartCount=0;reserveOnly=true;reservationFailed=false;
            try{RenderPerson(person,details);}finally{reserveOnly=false;}
            return !reservationFailed;
        }
        private void CommitReservedPerson()
        {
            // Matrices are calculated once into one shared bounded command buffer, then committed atomically.
            for(int i=0;i<pendingPartCount;i++)
            {PendingPart part=pendingParts[i];part.batch.matrices[part.batch.count++]=part.matrix;}
        }
        private void RenderPerson(Agent person, bool details)
        {
            int look = person.seed.appearance;
            bool red = person.redContact;
            float gait = person.moving ? Mathf.Sin(person.phase * 1.15f) : 0;
            float scale = person.seed.height / 1.76f;
            Vector2 leftFoot = FootPose(person, -1), rightFoot = FootPose(person, 1);
            // Solve the pelvis from two planted feet. The former sine-driven legs swept
            // through the pavement and produced a long, rigid scissor stride.
            float bob = Mathf.Min(0, ReachableHip(leftFoot), ReachableHip(rightFoot));
            if (!person.moving) bob -= (1 + Mathf.Sin(simulationTime * 1.6f + look)) * .0015f;
#if UNITY_WEBGL && !UNITY_EDITOR
            Matrix4x4 root = Matrix4x4.TRS(Vector3.Lerp(person.previousPosition, person.position, browserInterpolation) + Vector3.up * (bob - .025f),
                Quaternion.Euler(0, Mathf.LerpAngle(person.previousHeading, person.heading, browserInterpolation), 0),
#else
            Matrix4x4 root = Matrix4x4.TRS(person.position + Vector3.up * (bob - .025f), Quaternion.Euler(0, person.heading, 0),
#endif
                new Vector3(scale * person.seed.width, scale, scale));
            Material cloth = red ? wardrobe.dress : wardrobe.suit;
            Material pants = red ? visuals.skin[(look / 13) % visuals.skin.Length] : wardrobe.suit;
            Material skin = visuals.skin[(look / 13) % visuals.skin.Length];
            Material hair = visuals.hair[(look / 19) % visuals.hair.Length];
            Add(red ? wardrobe.dressMesh : wardrobe.suitMesh, cloth, root);
            if (!red)
            {
                Add(SlopCityHumanPart.Pelvis, pants, root);
                Add(wardrobe.shirtMesh, wardrobe.shirt, root);
                Add(wardrobe.tieMesh, wardrobe.tie, root);
                if (details) Add(wardrobe.lapelsMesh, wardrobe.lapels, root);
            }
            Matrix4x4 head = root * Matrix4x4.TRS(new Vector3(0, 1.46f, 0),
                Quaternion.Euler(person.moving ? -1.5f : 0, person.moving ? gait * 1.4f : Mathf.Sin(simulationTime * .32f + look) * 6, 0),
                new Vector3(.96f + look % 5 * .012f, 1, .97f + look % 3 * .018f));
            Add(SlopCityHumanPart.Head, skin, head);
            Add(red ? SlopCityHumanPart.HairBob : SlopCityHumanPart.HairShort, hair, head);
            if (details)
            {
                Add(SlopCityHumanPart.Face, hair, head);
                if (red) Add(SlopCityHumanPart.Eyes, visuals.eyes, head);
            }
            // Glasses are a defining silhouette, retained at both existing detail levels.
            if (!red) Add(wardrobe.glassesMesh, wardrobe.lenses, head);
            foreach (int side in Sides)
            {
                Vector2 foot = side < 0 ? leftFoot : rightFoot;
                float down = .91f - (foot.y - bob), distance = Mathf.Clamp(Mathf.Sqrt(down * down + foot.x * foot.x), .08f, .843f);
                float aim = Mathf.Atan2(-foot.x, down);
                float bend = Mathf.Acos(Mathf.Clamp((.43f * .43f + distance * distance - .415f * .415f) / (2 * .43f * distance), -1, 1));
                float thighAngle = (aim - bend) * Mathf.Rad2Deg;
                float knee = 180 - Mathf.Acos(Mathf.Clamp((.43f * .43f + .415f * .415f - distance * distance) / (2 * .43f * .415f), -1, 1)) * Mathf.Rad2Deg;
                Matrix4x4 thigh = root * Matrix4x4.TRS(new Vector3(side * .096f, .91f, 0), Quaternion.Euler(thighAngle, 0, 0), Vector3.one);
                Add(SlopCityHumanPart.Thigh, pants, thigh);
                Matrix4x4 shin = thigh * Matrix4x4.TRS(new Vector3(0, -.43f, 0), Quaternion.Euler(knee, 0, 0), Vector3.one);
                Add(SlopCityHumanPart.Shin, pants, shin);
                Matrix4x4 shoe = shin * Matrix4x4.TRS(new Vector3(0, -.415f, 0), Quaternion.Euler(-thighAngle - knee, 0, 0), new Vector3(.96f, 1, .96f));
                Add(SlopCityHumanPart.Shoe, visuals.shoes, shoe);
                if (details) Add(SlopCityHumanPart.ShoeSole, visuals.rubber, shoe);
                float armSwing = person.moving ? -gait * side * 13 : Mathf.Sin(simulationTime * 1.6f + look) * .8f;
                Matrix4x4 arm = root * Matrix4x4.TRS(new Vector3(side * (red ? .174f : .190f), 1.358f, 0),
                    Quaternion.Euler(armSwing, 0, side * 4), new Vector3(red ? .80f : 1, 1, red ? .80f : 1));
                Add(SlopCityHumanPart.UpperArm, red ? skin : cloth, arm);
                Matrix4x4 forearm = arm * Matrix4x4.TRS(new Vector3(0, -.282f, 0), Quaternion.Euler(-12 - Mathf.Max(0, -armSwing) * .45f, 0, 0), Vector3.one);
                Add(SlopCityHumanPart.Forearm, red ? skin : cloth, forearm);
                Matrix4x4 hand = forearm * Matrix4x4.Translate(new Vector3(0, -.265f, 0));
                Matrix4x4 palm = hand * Matrix4x4.Rotate(Quaternion.Euler(0, side < 0 ? 180 : 0, 0));
                if (details) Add(SlopCityHumanPart.Hand, skin, palm);
            }
        }
        private bool EnsureWardrobe()
        {
            if (wardrobe != null) return true;
            Material source = visuals.clothing[0];
            // A camera callback is not a recovery signal. Reconfiguration, a
            // disable/enable lifecycle or a changed source admits one new try.
            if (wardrobeAllocationFailed && ReferenceEquals(source, failedWardrobeSource)) return false;
            try
            {
                wardrobe = new MatrixWardrobe(source);
                wardrobeAllocationFailed = false; failedWardrobeSource = null;
                return true;
            }
            catch (Exception error)
            {
                wardrobeAllocationFailed = true; failedWardrobeSource = source;
                lastPresentationFrame = -1; PresentedCount = WholePeopleDeferred = 0;
                Debug.LogWarning("Crowd wardrobe allocation failed; retry requires reconfiguration or re-enabling. " + error.GetType().Name, this);
                return false;
            }
        }

        private void ReleaseWardrobe()
        {
            wardrobeAllocationFailed = false; failedWardrobeSource = null;
            Array.Clear(pendingParts,0,pendingParts.Length);pendingPartCount=0;
            batches.Clear(); batchLookup.Clear();
            if (wardrobe == null) return;
            wardrobe.Dispose(); wardrobe = null;
        }

        /// <summary>
        /// A single owned wardrobe for this crowd, independent of serialized mesh slots.
        /// It reuses the scene's shader/textures and anatomical limb/head meshes. No asset,
        /// per-person material, GameObject, collider, animator or update loop is created.
        /// </summary>
        private sealed class MatrixWardrobe : IDisposable
        {
            public readonly Material suit, shirt, tie, dress, lenses, lapels;
            public readonly Mesh suitMesh, shirtMesh, tieMesh, dressMesh, glassesMesh, lapelsMesh;
            private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>(12);

            public MatrixWardrobe(Material source)
            {
                try
                {
                suit = Fabric(source, "Smith black wool", new Color(.045f, .051f, .055f), .16f);
                shirt = Fabric(source, "Smith white shirt", new Color(.92f, .93f, .91f), .12f);
                tie = Fabric(source, "Smith black tie", new Color(.012f, .015f, .017f), .32f);
                lapels = Fabric(source, "Smith satin lapels", new Color(.075f, .082f, .087f), .34f);
                dress = Fabric(source, "Contact crimson dress", new Color(.78f, .012f, .035f), .28f);
                lenses = Fabric(source, "Smith dark glasses", new Color(.014f, .022f, .024f), .78f);
                if (lenses.HasProperty("_Metallic")) lenses.SetFloat("_Metallic", .38f);
                suitMesh = Loft("Smith tailored jacket", new[] {
                    new Vector3(.955f,.167f,.113f), new Vector3(1.05f,.157f,.103f),
                    new Vector3(1.16f,.153f,.097f), new Vector3(1.28f,.181f,.108f),
                    new Vector3(1.355f,.202f,.103f), new Vector3(1.404f,.170f,.080f),
                    new Vector3(1.448f,.066f,.054f) });
                // A shaped adult bodice narrows at the waist and opens into a knee-length
                // skirt. The generous hem clears the original planted-foot walking gait.
                dressMesh = Loft("Contact fitted flared red dress", new[] {
                    new Vector3(.54f,.315f,.28f), new Vector3(.69f,.268f,.226f),
                    new Vector3(.90f,.204f,.142f), new Vector3(1.00f,.177f,.119f),
                    new Vector3(1.13f,.135f,.091f), new Vector3(1.25f,.167f,.112f),
                    new Vector3(1.32f,.178f,.115f), new Vector3(1.385f,.154f,.089f),
                    new Vector3(1.423f,.070f,.054f) });
                var b = new WardrobeMesh();
                FrontPanel(b, new Vector2(-.007f,1.15f), new Vector2(.007f,1.15f),
                    new Vector2(.079f,1.414f), new Vector2(-.079f,1.414f), .008f);
                foreach (int side in Sides)
                {
                    // Separate white collar tips sit above the shirt inset and tie knot.
                    b.Quad(new Vector3(side*.004f,1.404f,.091f), new Vector3(side*.029f,1.363f,.110f),
                        new Vector3(side*.070f,1.415f,.083f), new Vector3(side*.053f,1.441f,.065f), true);
                }
                shirtMesh = Finish(b, "Smith shirt and collar");
                b = new WardrobeMesh();
                b.Quad(new Vector3(-.014f,1.394f,.100f), new Vector3(.014f,1.394f,.100f),
                    new Vector3(.009f,1.418f,.084f), new Vector3(-.009f,1.418f,.084f));
                b.Quad(new Vector3(0,1.177f,.112f), new Vector3(.020f,1.208f,.119f),
                    new Vector3(.010f,1.394f,.104f), new Vector3(-.010f,1.394f,.104f));
                b.Triangle(new Vector3(0,1.177f,.112f), new Vector3(-.010f,1.394f,.104f), new Vector3(-.020f,1.208f,.119f));
                tieMesh = Finish(b, "Smith necktie");
                b = new WardrobeMesh();
                foreach (int side in Sides)
                    FrontPanel(b, new Vector2(side*.008f,1.16f), new Vector2(side*.109f,1.315f),
                        new Vector2(side*.070f,1.416f), new Vector2(side*.047f,1.348f), .012f);
                lapelsMesh = Finish(b, "Smith notched lapels");
                b = new WardrobeMesh();
                foreach (int side in Sides)
                {
                    b.Box(new Vector3(side*.038f,.179f,.082f), new Vector3(.062f,.027f,.012f));
                    b.Box(new Vector3(side*.074f,.181f,.021f), new Vector3(.009f,.008f,.118f));
                }
                b.Box(new Vector3(0,.182f,.083f), new Vector3(.020f,.006f,.011f));
                glassesMesh = Finish(b, "Smith wraparound sunglasses");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }
            private static readonly Vector3[] SuitSurface = {
                new Vector3(.955f,.167f,.113f), new Vector3(1.05f,.157f,.103f),
                new Vector3(1.16f,.153f,.097f), new Vector3(1.28f,.181f,.108f),
                new Vector3(1.355f,.202f,.103f), new Vector3(1.404f,.170f,.080f),
                new Vector3(1.448f,.066f,.054f) };
            private static Vector3 FrontPoint(Vector2 point, float offset)
            {
                Vector3 section = SuitSurface[SuitSurface.Length - 1];
                for (int i = 1; i < SuitSurface.Length; i++)
                    if (point.y <= SuitSurface[i].x)
                    {
                        section = Vector3.Lerp(SuitSurface[i - 1], SuitSurface[i],
                            Mathf.InverseLerp(SuitSurface[i - 1].x, SuitSurface[i].x, point.y));
                        break;
                    }
                return new Vector3(point.x, point.y,
                    section.z * Mathf.Sqrt(Mathf.Max(0, 1 - point.x * point.x / (section.y * section.y))) + offset);
            }
            private static void FrontPanel(WardrobeMesh mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float offset)
            {
                // Follow the fitted torso rather than letting a flat shirt/lapel sink
                // through its curved chest. This runs once for the shared wardrobe.
                const int steps = 8;
                for (int row = 0; row < steps; row++)
                    for (int column = 0; column < steps; column++)
                    {
                        float u = column / (float)steps, v = row / (float)steps;
                        float nextU = (column + 1) / (float)steps, nextV = (row + 1) / (float)steps;
                        mesh.Quad(FrontPoint(Vector2.Lerp(Vector2.Lerp(a,b,u),Vector2.Lerp(d,c,u),v),offset),
                            FrontPoint(Vector2.Lerp(Vector2.Lerp(a,b,nextU),Vector2.Lerp(d,c,nextU),v),offset),
                            FrontPoint(Vector2.Lerp(Vector2.Lerp(a,b,nextU),Vector2.Lerp(d,c,nextU),nextV),offset),
                            FrontPoint(Vector2.Lerp(Vector2.Lerp(a,b,u),Vector2.Lerp(d,c,u),nextV),offset), true);
                    }
            }
            private Material Fabric(Material source, string name, Color color, float smoothness)
            {
                var material = Own(source ? new Material(source) : new Material(Shader.Find("Standard")));
                material.name = name; material.hideFlags = HideFlags.HideAndDontSave;
                material.color = color; material.enableInstancing = true;
                // Reuse the small source albedo. New geometry needs no additional textures
                // or normal-map variants; fixed smoothness prevents a source mask override.
                material.DisableKeyword("_NORMALMAP"); material.DisableKeyword("_DETAIL_MULX2");
                material.DisableKeyword("_METALLICGLOSSMAP"); material.DisableKeyword("_EMISSION");
                if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", null);
                if (material.HasProperty("_DetailNormalMap")) material.SetTexture("_DetailNormalMap", null);
                if (material.HasProperty("_MetallicGlossMap")) material.SetTexture("_MetallicGlossMap", null);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0);
                if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
                return material;
            }
            private Mesh Loft(string name, Vector3[] profile)
            {
                var b = new WardrobeMesh(); const int sides = 24;
                for (int ring = 0; ring < profile.Length - 1; ring++)
                    for (int side = 0; side < sides; side++)
                    {
                        float a = side * Mathf.PI * 2 / sides, c = (side + 1) * Mathf.PI * 2 / sides;
                        b.Quad(Ring(profile[ring], a), Ring(profile[ring + 1], a),
                            Ring(profile[ring + 1], c), Ring(profile[ring], c));
                    }
                // Inset top/bottom caps close view angles without separate renderers.
                for (int side = 0; side < sides; side++)
                {
                    float a = side * Mathf.PI * 2 / sides, c = (side + 1) * Mathf.PI * 2 / sides;
                    b.Triangle(new Vector3(0, profile[0].x, 0), Ring(profile[0], a), Ring(profile[0], c));
                    int top = profile.Length - 1;
                    b.Triangle(new Vector3(0, profile[top].x, 0), Ring(profile[top], c), Ring(profile[top], a));
                }
                return Finish(b, name);
            }
            private static Vector3 Ring(Vector3 profile, float angle)
                => new Vector3(Mathf.Cos(angle) * profile.y, profile.x, Mathf.Sin(angle) * profile.z);
            private Mesh Finish(WardrobeMesh builder, string name)
            {
                return Own(builder.Build(name));
            }
            private T Own<T>(T resource) where T : UnityEngine.Object
            {
                try { owned.Add(resource); return resource; }
                catch { DestroyOwned(resource); throw; }
            }
            private static void DestroyOwned(UnityEngine.Object resource)
            {
                if (!resource) return;
                if (Application.isPlaying) UnityEngine.Object.Destroy(resource);
                else UnityEngine.Object.DestroyImmediate(resource);
            }
            public void Dispose()
            {
                foreach (UnityEngine.Object resource in owned)
                    DestroyOwned(resource);
                owned.Clear();
            }
        }
        private sealed class WardrobeMesh
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<int> triangles = new List<int>();
            private readonly List<Vector2> uv = new List<Vector2>();
            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                uv.Add(new Vector2(a.x + .5f, a.y)); uv.Add(new Vector2(b.x + .5f, b.y)); uv.Add(new Vector2(c.x + .5f, c.y));
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool doubleSided = false)
            {
                Triangle(a,b,c); Triangle(a,c,d);
                if (doubleSided) { Triangle(c,b,a); Triangle(d,c,a); }
            }
            public void Box(Vector3 center, Vector3 size)
            {
                Vector3 h = size * .5f;
                Vector3 a = center + new Vector3(-h.x,-h.y,-h.z), b = center + new Vector3(h.x,-h.y,-h.z);
                Vector3 c = center + new Vector3(h.x,h.y,-h.z), d = center + new Vector3(-h.x,h.y,-h.z);
                Vector3 e = center + new Vector3(-h.x,-h.y,h.z), f = center + new Vector3(h.x,-h.y,h.z);
                Vector3 g = center + new Vector3(h.x,h.y,h.z), k = center + new Vector3(-h.x,h.y,h.z);
                Quad(e,f,g,k); Quad(b,a,d,c); Quad(a,e,k,d); Quad(f,b,c,g); Quad(d,k,g,c); Quad(a,b,f,e);
            }
            public Mesh Build(string name)
            {
                var mesh = new Mesh();
                try
                {
                    mesh.name = name; mesh.hideFlags = HideFlags.HideAndDontSave;
                    mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv);
                    mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.UploadMeshData(true); return mesh;
                }
                catch
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
                    else UnityEngine.Object.DestroyImmediate(mesh);
                    throw;
                }
            }
        }

        private static Vector2 FootPose(Agent person, int side)
        {
            if (!person.moving) return new Vector2(side * .014f, .069f);
            float cycle = Mathf.Repeat(person.phase * 1.15f / (Mathf.PI * 2) + (side < 0 ? .5f : 0), 1);
            float stride = .245f;
            if (cycle < .60f) return new Vector2(Mathf.Lerp(stride, -stride, cycle / .60f), .065f);
            float swing = (cycle - .60f) / .40f;
            return new Vector2(Mathf.Lerp(-stride, stride, Mathf.SmoothStep(0, 1, swing)), .065f + Mathf.Sin(swing * Mathf.PI) * .09f);
        }
        private static float ReachableHip(Vector2 foot)
            => foot.y + Mathf.Sqrt(Mathf.Max(.01f, .837f * .837f - foot.x * foot.x)) - .91f;
        private static readonly int[] Sides = { -1, 1 };
        private void Add(SlopCityHumanPart part, Material material, Matrix4x4 matrix)
        {
            if ((int)part >= visuals.parts.Length) return;
            Add(visuals.parts[(int)part], material, matrix);
        }
        private void Add(Mesh mesh, Material material, Matrix4x4 matrix)
        {
            if (!mesh || !material) return;
            long key = ((long)mesh.GetInstanceID() << 32) ^ (uint)material.GetInstanceID();
            if (!batchLookup.TryGetValue(key, out DrawBatch batch))
            {
                // Shared wardrobe/body parts need only their visible matrices. Grow each batch to
                // its observed visible count, retaining the API's hard 1023 cap.
                batch = new DrawBatch { mesh = mesh, material = material,
                    matrices = new Matrix4x4[16] };
                batchLookup.Add(key, batch); batches.Add(batch);
            }
            if(reserveOnly)
            {
                int needed=batch.count+(++batch.reserved);
                if(needed>1023||pendingPartCount>=pendingParts.Length){reservationFailed=true;return;}
                pendingParts[pendingPartCount++]=new PendingPart{batch=batch,matrix=matrix};
                if(needed>batch.matrices.Length)Array.Resize(ref batch.matrices,Mathf.Min(1023,Mathf.Max(needed,batch.matrices.Length*2)));
                return;
            }
            // Every key/repetition was reserved atomically before any part was emitted.
            if(batch.count>=batch.matrices.Length)throw new InvalidOperationException("Whole-person matrix reservation was not satisfied.");
            batch.matrices[batch.count++]=matrix;
        }
    }
}
