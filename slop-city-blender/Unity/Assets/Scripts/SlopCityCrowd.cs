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
        private static readonly RaycastHit[] layoutGroundHits = new RaycastHit[16];
        private static readonly RaycastHit[] layoutCapsuleHits = new RaycastHit[12];
        private readonly int[] gridHeads = new int[70 * 70];
        private float simulationTime;
        private Transform player;
        private SlopCityFirstPerson playerController;
        private SlopCityPlayerInput playerInput;
        private bool configured;
        private MatrixWardrobe wardrobe;
        private bool wardrobeAllocationFailed;
        private bool renderingAllocationFailed;
        private float renderingRetryAt;
#if UNITY_WEBGL && !UNITY_EDITOR
        private int worldSweepCastsThisStep;
#endif
        private Material failedWardrobeSource;
        private int redContactCount;
        private SlopCityLocalAgent localAgents = new SlopCityLocalAgent();
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
            public bool moving, redContact, casual;
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
            public Vector4[] hipPoses;
            public MaterialPropertyBlock properties;
            public bool poseCapacityChanged;
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
            bool priorConfigured = configured;
            try
            {
                routes = replacements; population = people; configured = false;
                EnsureAgents();
            }
            catch
            {
                // Registry construction commits atomically; only these inputs changed.
                routes = priorRoutes; population = priorPopulation;
                configured = priorConfigured;
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
            int count = Physics.RaycastNonAlloc(hint + Vector3.up * .70f, Vector3.down, layoutGroundHits, 1.35f,
                ~0, QueryTriggerInteraction.Ignore);
            if (count == layoutGroundHits.Length) return false;
            float nearest = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = layoutGroundHits[index];
                if (LayoutCollider(hit.collider, controller) && hit.normal.y > .78f && hit.distance < nearest)
                { nearest = hit.distance; feet = hit.point + Vector3.up * .025f; }
            }
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
            int blockers = Physics.CapsuleCastNonAlloc(from + Vector3.up * .34f, from + Vector3.up * 1.64f, .32f,
                delta.normalized, layoutCapsuleHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            if (blockers == layoutCapsuleHits.Length) return false;
            for (int index = 0; index < blockers; index++)
                if (LayoutCollider(layoutCapsuleHits[index].collider, controller)) return false;
            return true;
        }
        private void OnEnable()
        {
            EnsureAgents();
            Instance = this;
            Camera.onPreCull -= RenderCrowd; Camera.onPreCull += RenderCrowd;
        }
        private void OnDisable()
        {
            Camera.onPreCull -= RenderCrowd;
            if (Instance == this) Instance = null;
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
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!playerInput && playerController) playerInput = playerController.GetComponent<SlopCityPlayerInput>();
            constrainedCrowd = Application.isMobilePlatform || (playerInput && playerInput.UseTouchControls);
            worldSweepCastsThisStep = 0;
#endif
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
#if UNITY_WEBGL && !UNITY_EDITOR
            if (constrainedCrowd)
            {
                if (worldSweepCastsThisStep >= 8) return false;
                worldSweepCastsThisStep++;
            }
#endif
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
        private struct TouchPresentation { public Agent person; public float priority; public bool details; }
        private readonly TouchPresentation[] touchVisible = new TouchPresentation[MaximumTouchPresented];
        private int lastPresentationFrame=-10;
        private bool reserveOnly,reservationFailed;
        public const int MaximumTouchPresented = 96, MaximumTouchDetailed = 12, MaximumTouchVertices = 1500000;
        private bool constrainedCrowd;
        private int reservedVertices, detailedPresented;
        public int SubmittedVertexCount { get; private set; }
        private struct PendingPart { public DrawBatch batch; public Matrix4x4 matrix; public Vector4 hipPose; }
        private readonly PendingPart[] pendingParts=new PendingPart[64];
        private int pendingPartCount;
        public int MaximumPresentedCount
        {
            get {
#if (UNITY_WEBGL && !UNITY_EDITOR) || SLOP_CITY_WEB_INTERACTION_TEST
                return constrainedCrowd ? MaximumTouchPresented : MaximumPresentedBrowser;
#else
                return MaximumPresentedNative;
#endif
            }
        }
        public bool IsResidentPresented(int index)=>IsPresented(index);
        private void LatchRenderingFailure()
        {
            renderingAllocationFailed = true;
            renderingRetryAt = Time.unscaledTime + 5f;
        }
        private void ReleaseIdleInstanceBuffers()
        {
            for (int i = 0; i < batches.Count; i++)
            {
                DrawBatch batch = batches[i];
                int length = batch.matrices == null ? 0 : batch.matrices.Length;
                int used = batch.count;
                if (length <= 32 || used * 4 >= length) continue;
                int next = Mathf.Max(16, used * 2);
                if (next >= length) continue;
                Array.Resize(ref batch.matrices, next);
                if (batch.hipPoses != null)
                {
                    Array.Resize(ref batch.hipPoses, next);
                    batch.poseCapacityChanged = true;
                }
            }
        }
        private void RenderCrowd(Camera camera)
        {
            if (renderingAllocationFailed)
            {
                if (Time.unscaledTime < renderingRetryAt) return;
                renderingAllocationFailed = false;
            }
            if (!isActiveAndEnabled || !camera || camera.cameraType == CameraType.Preview || !SystemInfo.supportsInstancing) return;
            if(Application.isPlaying && camera != (playerController?playerController.ViewCamera:Camera.main))return;
            EnsureAgents();
            if (visuals == null || visuals.parts == null || visuals.parts.Length < 20 || visuals.clothing == null || visuals.clothing.Length == 0
                || visuals.skin == null || visuals.skin.Length == 0 || visuals.hair == null || visuals.hair.Length == 0) return;
            if (!EnsureWardrobe()) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!playerInput && playerController) playerInput = playerController.GetComponent<SlopCityPlayerInput>();
            constrainedCrowd = Application.isMobilePlatform || (playerInput && playerInput.UseTouchControls);
#else
            constrainedCrowd = false;
#endif
            SubmittedVertexCount = detailedPresented = 0;
            ReleaseIdleInstanceBuffers();
            foreach (DrawBatch batch in batches) { batch.count = 0; batch.reserved = 0; }
            if(presentationSerial==int.MaxValue){Array.Clear(presentationStamp,0,presentationStamp.Length);presentationSerial=0;}
            presentationSerial++;lastPresentationFrame=Time.frameCount;PresentedCount=WholePeopleDeferred=0;
            try
            {
                GeometryUtility.CalculateFrustumPlanes(camera, frustum);Vector3 eye=camera.transform.position;
                if (constrainedCrowd)
                {
                    RenderTouchCrowd(camera, eye);
                    DrawBatches(camera);
                    return;
                }
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
                DrawBatches(camera);
            }
            catch (Exception error)
            {
                // Camera callbacks recur every frame. A partial reservation or draw
                // failure must not retry with corrupted batch state or overrun buffers.
                LatchRenderingFailure();
                Array.Clear(pendingParts, 0, pendingParts.Length); pendingPartCount = 0;
                foreach (DrawBatch batch in batches) { batch.count = 0; batch.reserved = 0; }
                lastPresentationFrame = -1; PresentedCount = WholePeopleDeferred = 0;
                Debug.LogWarning("Crowd rendering stopped after an unexpected failure; retry requires reconfiguration or re-enabling. "
                    + error.GetType().Name, this);
            }
        }
        private void RenderTouchCrowd(Camera camera, Vector3 eye)
        {
            // One bounded nearest-first ordering admits complete local residents
            // without spending detail/vertex capacity on partially drawn bodies.
            int selected = 0, eligible = 0;
            try
            {
                for (int slot = 0; slot < activeCount; slot++)
                {
                    Agent person = agents[slot];
                    if (!ValidNode(person.seed.route, person.node)) continue;
                    float distance = (person.position - eye).sqrMagnitude;
                    if (distance > 60 * 60 || !GeometryUtility.TestPlanesAABB(frustum,
                        new Bounds(person.position + Vector3.up * person.seed.height * .5f, new Vector3(.9f, person.seed.height + .15f, .9f)))) continue;
                    eligible++;
                    SelectTouchPerson(person, distance, ref selected);
                }

                WholePeopleDeferred = eligible - selected;
                for (int index = 0; index < selected; index++)
                {
                    TouchPresentation choice = touchVisible[index];
                    if (!TryReservePerson(choice.person, choice.details)) { WholePeopleDeferred++; continue; }
                    CommitReservedPerson();
                    presentationStamp[choice.person.residentIndex] = presentationSerial;
                    PresentedCount++;
                }
            }
            finally
            {
                // Do not retain residents in the reusable frame buffer between draws.
                Array.Clear(touchVisible, 0, touchVisible.Length);
            }
        }
        private void SelectTouchPerson(Agent person, float distance, ref int selected)
        {
            float priority = person.talkReady > simulationTime && distance < 16f ? -1f : distance;
            int insert = selected;
            while (insert > 0 && (priority < touchVisible[insert - 1].priority ||
                (Mathf.Approximately(priority, touchVisible[insert - 1].priority) &&
                 person.seed.residentId < touchVisible[insert - 1].person.seed.residentId))) insert--;
            if (insert >= touchVisible.Length) return;
            int end = Mathf.Min(selected, touchVisible.Length - 1);
            for (int index = end; index > insert; index--) touchVisible[index] = touchVisible[index - 1];
            touchVisible[insert] = new TouchPresentation { person = person, priority = priority, details = distance < 12 * 12 };
            if (selected < touchVisible.Length) selected++;
        }
        private void DrawBatches(Camera camera)
        {
            if (renderingAllocationFailed) return;
            foreach (DrawBatch batch in batches)
            {
                if (batch.count <= 0 || !batch.mesh || !batch.material || !batch.material.enableInstancing) continue;
                if (batch.count > batch.matrices.Length || batch.count > 1023)
                {
                    LatchRenderingFailure();
                    Debug.LogWarning("Crowd instance count exceeded its reserved matrix buffer; retry requires reconfiguration or re-enabling.", this);
                    return;
                }
                if (batch.properties != null)
                {
                    // Unity fixes vector-array capacity on its first assignment.
                    // Reset only after growth so a larger visible batch is not truncated.
                    if (batch.poseCapacityChanged) { batch.properties.Clear(); batch.poseCapacityChanged = false; }
                    batch.properties.SetVectorArray("_HipPose", batch.hipPoses);
                }
                Graphics.DrawMeshInstanced(batch.mesh, 0, batch.material, batch.matrices, batch.count,
#if UNITY_WEBGL && !UNITY_EDITOR
                        batch.properties, ShadowCastingMode.Off, false, gameObject.layer, camera, LightProbeUsage.Off);
#else
                        batch.properties, ShadowCastingMode.On, true, gameObject.layer, camera, LightProbeUsage.BlendProbes);
#endif
            }
        }
        private bool TryReservePerson(Agent person,bool details)
        {
            // Reserve complete people in camera-distance order. A dense close group
            // falls back to its shared silhouette before any farther person is deferred.
            details = details && (!constrainedCrowd || detailedPresented < MaximumTouchDetailed);
            if (ReservePerson(person, details)) { if (details) detailedPresented++; return true; }
            return constrainedCrowd && details && ReservePerson(person, false);
        }
        private bool ReservePerson(Agent person, bool details)
        {
            if (renderingAllocationFailed) return false;
            foreach(DrawBatch batch in batches)batch.reserved=0;
            pendingPartCount=reservedVertices=0;reserveOnly=true;reservationFailed=false;
            try { RenderPerson(person, details); }
            catch (Exception error)
            {
                // Matrix/batch growth is attempted from a recurring camera
                // callback. A failed admission must not retry every frame.
                LatchRenderingFailure();
                Array.Clear(pendingParts, 0, pendingParts.Length); pendingPartCount = 0;
                foreach (DrawBatch batch in batches) batch.reserved = 0;
                lastPresentationFrame = -1; PresentedCount = WholePeopleDeferred = 0;
                Debug.LogWarning("Crowd draw buffers could not grow; retry requires reconfiguration or re-enabling. "
                    + error.GetType().Name, this);
                return false;
            }
            finally { reserveOnly = false; }
            return !reservationFailed;
        }
        private void CommitReservedPerson()
        {
            // Grow instance buffers only after the whole person is accepted.
            // A rejected reservation must not keep the larger arrays.
            for (int i = 0; i < pendingPartCount; i++)
            {
                DrawBatch batch = pendingParts[i].batch;
                int needed = batch.count + batch.reserved;
                if (needed <= batch.matrices.Length) continue;
                int capacity = Mathf.Min(1023, Mathf.Max(needed, batch.matrices.Length * 2));
                Array.Resize(ref batch.matrices, capacity);
                if (batch.hipPoses != null)
                {
                    Array.Resize(ref batch.hipPoses, capacity);
                    batch.poseCapacityChanged = true;
                }
            }
            SubmittedVertexCount += reservedVertices;
            for(int i=0;i<pendingPartCount;i++)
            {PendingPart part=pendingParts[i];int slot=part.batch.count++;part.batch.matrices[slot]=part.matrix;if(part.batch.hipPoses!=null)part.batch.hipPoses[slot]=part.hipPose;}
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
            // Use binary overloads: the three-argument params overload creates
            // a temporary float[] for every presented body on every frame.
            float bob = Mathf.Min(0, Mathf.Min(ReachableHip(leftFoot), ReachableHip(rightFoot)));
            if (!person.moving) bob -= (1 + Mathf.Sin(simulationTime * 1.6f + look)) * .0015f;
            Vector2 leftLeg=LegPose(leftFoot,bob),rightLeg=LegPose(rightFoot,bob);
#if UNITY_WEBGL && !UNITY_EDITOR
            Matrix4x4 root = Matrix4x4.TRS(Vector3.Lerp(person.previousPosition, person.position, browserInterpolation) + Vector3.up * (bob - .025f),
                Quaternion.Euler(0, Mathf.LerpAngle(person.previousHeading, person.heading, browserInterpolation), 0),
#else
            Matrix4x4 root = Matrix4x4.TRS(person.position + Vector3.up * (bob - .025f), Quaternion.Euler(0, person.heading, 0),
#endif
                new Vector3(scale * person.seed.width, scale, scale));
            Material cloth = person.casual ? wardrobe.shirt : red ? wardrobe.dress : wardrobe.suit;
            Material pants = person.casual ? wardrobe.visitorSuit : red ? visuals.skin[(look / 13) % visuals.skin.Length] : wardrobe.suit;
            Material skin = visuals.skin[person.casual ? Mathf.Min(4, visuals.skin.Length - 1) : (look / 13) % visuals.skin.Length];
            Material hair = visuals.hair[(look / 19) % visuals.hair.Length];
            float leftHip=leftLeg.x*Mathf.Deg2Rad,rightHip=rightLeg.x*Mathf.Deg2Rad;
            Vector4 hipPose=new Vector4(Mathf.Sin(leftHip),Mathf.Cos(leftHip),Mathf.Sin(rightHip),Mathf.Cos(rightHip));
            Vector4 bodyClothPose=hipPose;
            if(red)
            {
                // The front and rear of a fitted skirt follow the leading and
                // trailing leg, keeping its narrow hem joined during a stride.
                float front=Mathf.Min(leftHip,rightHip),back=Mathf.Max(leftHip,rightHip);
                bodyClothPose=new Vector4(Mathf.Sin(front),Mathf.Cos(front),Mathf.Sin(back),Mathf.Cos(back));
            }
            bool coarse = constrainedCrowd && !details;
            Add(person.casual ? wardrobe.visitorShirtMesh : red ? (coarse ? wardrobe.coarseDressMesh : wardrobe.dressMesh) : wardrobe.suitMesh, cloth, root,bodyClothPose);
            if(red)Add(coarse ? wardrobe.coarseNecklineMesh : wardrobe.necklineMesh,skin,root);
            if (!red)
            {
                if(wardrobe.animatedTrousers || coarse)
                {
                    Add(coarse ? wardrobe.coarseTrouserMesh : wardrobe.trouserYokeMesh,person.casual?wardrobe.trouserBlue:wardrobe.trouserBlack,root,
                        hipPose);
                }
                else Add(SlopCityHumanPart.Pelvis,pants,root);
                if (!person.casual)
                {
                    Add(wardrobe.shirtMesh, wardrobe.shirt, root);
                    Add(wardrobe.tieMesh, wardrobe.tie, root);
                }
                if (details && !person.casual) Add(wardrobe.lapelsMesh, wardrobe.lapels, root);
            }
            Matrix4x4 head = root * Matrix4x4.TRS(new Vector3(0, 1.46f, 0),
                Quaternion.Euler(person.moving ? -1.5f : 0, person.moving ? gait * 1.4f : Mathf.Sin(simulationTime * .32f + look) * 6, 0),
                new Vector3(.96f + look % 5 * .012f, 1, .97f + look % 3 * .018f));
            if(coarse)Add(wardrobe.coarseHeadMesh, skin, head);
            else Add(SlopCityHumanPart.Head, skin, head);
            // Distant bodies keep the suit, tie and glasses. Hair and limbs are
            // small shared lofts, so the sculpted human meshes stay on the nearby set.
            if(coarse)Add(wardrobe.coarseHairMesh, hair, head);
            else Add(red ? SlopCityHumanPart.HairBob : SlopCityHumanPart.HairShort, hair, head);
            if (details)
            {
                Add(SlopCityHumanPart.Face, hair, head);
                if (red) Add(SlopCityHumanPart.Eyes, visuals.eyes, head);
            }
            if (person.casual)
            {
                Add(wardrobe.visitorHatMesh, wardrobe.visitorSuit, head);
            }
            // Glasses are a defining silhouette, retained at both existing detail levels.
            if (!red) Add(wardrobe.glassesMesh, wardrobe.lenses, head);
            foreach (int side in Sides)
            {
                Vector2 foot = side < 0 ? leftFoot : rightFoot;
                Vector2 leg=side<0?leftLeg:rightLeg;float thighAngle=leg.x,knee=leg.y;
                Matrix4x4 thigh = root * Matrix4x4.TRS(new Vector3(side * .096f, .91f, 0), Quaternion.Euler(thighAngle, 0, 0), Vector3.one);
                if(red||(!wardrobe.animatedTrousers && !coarse))Add(PresentedMesh(coarse, wardrobe.coarseThighMesh, SlopCityHumanPart.Thigh), pants, thigh);
                Matrix4x4 shin = thigh * Matrix4x4.TRS(new Vector3(0, -.43f, 0), Quaternion.Euler(knee, 0, 0), Vector3.one);
                Add(PresentedMesh(coarse, wardrobe.coarseShinMesh, SlopCityHumanPart.Shin), pants, shin);
                Matrix4x4 shoe = shin * Matrix4x4.TRS(new Vector3(0, -.415f, 0), Quaternion.Euler(-thighAngle - knee, 0, 0), new Vector3(.96f, 1, .96f));
                Add(PresentedMesh(coarse, wardrobe.coarseShoeMesh, SlopCityHumanPart.Shoe), visuals.shoes, shoe);
                if (details) Add(SlopCityHumanPart.ShoeSole, visuals.rubber, shoe);
                float armSwing = person.moving ? -gait * side * 13 : Mathf.Sin(simulationTime * 1.6f + look) * .8f;
                Matrix4x4 arm = root * Matrix4x4.TRS(new Vector3(side * (red ? .174f : .190f), 1.358f, 0),
                    Quaternion.Euler(armSwing, 0, side * 4), new Vector3(red ? .80f : 1, 1, red ? .80f : 1));
                Add(PresentedMesh(coarse, wardrobe.coarseUpperArmMesh, SlopCityHumanPart.UpperArm), red || person.casual ? skin : cloth, arm);
                if (person.casual) Add(wardrobe.visitorSleeveMesh, cloth, arm);
                Matrix4x4 forearm = arm * Matrix4x4.TRS(new Vector3(0, -.282f, 0), Quaternion.Euler(-12 - Mathf.Max(0, -armSwing) * .45f, 0, 0), Vector3.one);
                Add(PresentedMesh(coarse, wardrobe.coarseForearmMesh, SlopCityHumanPart.Forearm), red || person.casual ? skin : cloth, forearm);
                Matrix4x4 hand = forearm * Matrix4x4.Translate(new Vector3(0, -.265f, 0));
                Matrix4x4 palm = hand * Matrix4x4.Rotate(Quaternion.Euler(0, side < 0 ? 180 : 0, 0));
                if (details) Add(SlopCityHumanPart.Hand, skin, palm);
            }
        }
        private bool EnsureWardrobe()
        {
            bool retired = MatrixWardrobe.DrainRetired();
            if (wardrobe != null) return true;
            // A returning component must not overlap a new complete wardrobe
            // with native meshes/materials still queued for deferred destruction.
            if (!retired) return false;
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
            renderingAllocationFailed = false; renderingRetryAt = 0;
            Array.Clear(pendingParts,0,pendingParts.Length);pendingPartCount=0;
            ReleaseDrawBatches();
            if (wardrobe != null) { wardrobe.Dispose(); wardrobe = null; }
            // A refused destroy stays in the static owner list until a later
            // release or camera pass. Drain even when this crowd has no wardrobe.
            MatrixWardrobe.DrainRetired();
        }

        private void ReleaseDrawBatches()
        {
            // Instancing buffers grow to the busiest frame and otherwise stay there.
            // Drop them with the wardrobe so the managed peak does not outlive it.
            for (int index = 0; index < batches.Count; index++)
            {
                DrawBatch batch = batches[index];
                batch.matrices = Array.Empty<Matrix4x4>();
                batch.hipPoses = null;
                batch.properties = null;
                batch.mesh = null;
                batch.material = null;
                batch.count = batch.reserved = 0;
            }
            batches.Clear();
            batches.TrimExcess();
            batchLookup.Clear();
            batchLookup.TrimExcess();
        }

        /// <summary>
        /// A single owned wardrobe for this crowd, independent of serialized mesh slots.
        /// It reuses the scene's shader/textures and anatomical limb/head meshes. No asset,
        /// per-person material, GameObject, collider, animator or update loop is created.
        /// </summary>
        private sealed class MatrixWardrobe : IDisposable
        {
            private static readonly List<MatrixWardrobe> Owners = new List<MatrixWardrobe>();
            public readonly Material suit, shirt, tie, dress, lenses, lapels, visitorSuit, trouserBlack, trouserBlue;
            public readonly bool animatedTrousers;
            public readonly Mesh suitMesh, shirtMesh, tieMesh, dressMesh, necklineMesh, glassesMesh, lapelsMesh, visitorHatMesh, visitorShirtMesh, visitorSleeveMesh, trouserYokeMesh;
            public readonly Mesh coarseHeadMesh, coarseDressMesh, coarseNecklineMesh, coarseTrouserMesh;
            public readonly Mesh coarseHairMesh, coarseUpperArmMesh, coarseForearmMesh, coarseThighMesh, coarseShinMesh, coarseShoeMesh;
            private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>(12);
            private UnityEngine.Object pendingOwner;
            private bool retired, cleanupWarning;
            private int lastCleanupFrame = -1;
            private int releaseAfterFrame, cleanupRefusals;

            public MatrixWardrobe(Material source)
            {
                // Register the managed owner before any native allocation. Even a
                // constructor failure or refused Destroy keeps its exact handles.
                Owners.Add(this);
                try
                {
                visitorSuit = Fabric(source, "Visitor midnight blue wool", new Color(.025f,.12f,.36f), .22f);
                suit = Fabric(source, "Smith black wool", new Color(.045f, .051f, .055f), .16f);
                Shader trouserShader=Resources.Load<Shader>("SlopCityTailoredTrousers");
                animatedTrousers=trouserShader&&trouserShader.isSupported&&SystemInfo.supportsInstancing;
                if(animatedTrousers)
                {
                    trouserBlack=Own(new Material(suit));trouserBlack.shader=trouserShader;trouserBlack.name="Smith articulated trouser cloth";
                    trouserBlue=Own(new Material(visitorSuit));trouserBlue.shader=trouserShader;trouserBlue.name="Visitor articulated blue trouser cloth";
                }
                else {trouserBlack=suit;trouserBlue=visitorSuit;}

                shirt = Fabric(source, "Smith white shirt", new Color(.92f, .93f, .91f), .12f);
                tie = Fabric(source, "Smith black tie", new Color(.008f, .008f, .008f), .08f);
                tie.SetFloat("_SpecularHighlights", 0); tie.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                lapels = Fabric(source, "Smith satin lapels", new Color(.075f, .082f, .087f), .34f);
                dress = Fabric(source, "Contact crimson dress", new Color(.78f, .012f, .035f), .28f);
                if(animatedTrousers)dress.shader=trouserShader;
                lenses = Fabric(source, "Smith dark glasses", new Color(.014f, .022f, .024f), .78f);
                if (lenses.HasProperty("_Metallic")) lenses.SetFloat("_Metallic", .38f);
                suitMesh = Loft("Smith tailored jacket", new[] {
                    new Vector3(.955f,.167f,.113f), new Vector3(1.05f,.157f,.103f),
                    new Vector3(1.16f,.153f,.097f), new Vector3(1.28f,.181f,.108f),
                    new Vector3(1.355f,.202f,.103f), new Vector3(1.404f,.170f,.080f),
                    new Vector3(1.448f,.066f,.054f) });
                dressMesh = ContactDress(false);
                necklineMesh = ContactDress(true);
                coarseDressMesh = ContactDress(false, true);
                coarseNecklineMesh = ContactDress(true, true);
                coarseTrouserMesh = TrouserYoke(true);
                // Match the authored head's neck/chin/skull silhouette. At a distance,
                // the tiny sculpted facial surfaces do not need 4,864 triangles.
                coarseHeadMesh = Loft("Distant shared head", new[] {
                    new Vector3(-.068f,.041f,.039f), new Vector3(.035f,.043f,.041f),
                    new Vector3(.065f,.047f,.057f), new Vector3(.09f,.066f,.066f),
                    new Vector3(.145f,.076f,.074f), new Vector3(.195f,.072f,.078f),
                    new Vector3(.238f,.065f,.067f), new Vector3(.266f,.043f,.049f),
                    new Vector3(.278f,.011f,.022f) });
                // Same joint lengths as the sculpted limbs, with eight sides.
                // Phone streets instance these instead of the authored human meshes.
                coarseHairMesh = Loft("Distant shared hair", new[] {
                    new Vector3(.12f,.072f,.082f), new Vector3(.20f,.086f,.096f), new Vector3(.29f,.028f,.04f) }, 8);
                coarseUpperArmMesh = Loft("Distant shared upper arm", new[] {
                    new Vector3(.028f,.037f,.048f), new Vector3(-.12f,.054f,.058f), new Vector3(-.282f,.046f,.046f) }, 8);
                coarseForearmMesh = Loft("Distant shared forearm", new[] {
                    new Vector3(.03f,.044f,.044f), new Vector3(-.12f,.038f,.038f), new Vector3(-.265f,.032f,.03f) }, 8);
                coarseThighMesh = Loft("Distant shared thigh", new[] {
                    new Vector3(.025f,.055f,.055f), new Vector3(-.2f,.062f,.058f), new Vector3(-.43f,.048f,.046f) }, 8);
                coarseShinMesh = Loft("Distant shared shin", new[] {
                    new Vector3(.04f,.046f,.046f), new Vector3(-.2f,.038f,.036f), new Vector3(-.415f,.032f,.03f) }, 8);
                var distantShoe = new WardrobeMesh();
                distantShoe.Box(new Vector3(0f, -.02f, .07f), new Vector3(.09f, .045f, .16f));
                coarseShoeMesh = Finish(distantShoe, "Distant shared shoe");
                var b = new WardrobeMesh();
                FrontPanel(b, new Vector2(-.028f,1.04f), new Vector2(.028f,1.04f),
                    new Vector2(.079f,1.414f), new Vector2(-.079f,1.414f), .008f);
                foreach (int side in Sides)
                {
                    // Separate white collar tips sit above the shirt inset and tie knot.
                    b.Quad(new Vector3(side*.004f,1.404f,.091f), new Vector3(side*.029f,1.363f,.110f),
                        new Vector3(side*.070f,1.415f,.083f), new Vector3(side*.053f,1.441f,.065f), true);
                }
                shirtMesh = Finish(b, "Smith shirt and collar");
                visitorShirtMesh = Loft("Visitor short sleeved white shirt", new[] {
                    new Vector3(.965f,.168f,.114f), new Vector3(1.05f,.158f,.104f),
                    new Vector3(1.16f,.154f,.098f), new Vector3(1.28f,.182f,.109f),
                    new Vector3(1.355f,.203f,.104f), new Vector3(1.404f,.171f,.081f),
                    new Vector3(1.44f,.067f,.055f) });
                // Short fabric shells cover only the upper arm. The unchanged
                // anatomy beneath, exposed lower upper-arm, forearm and hand all
                // use the same darker natural skin as the visitor's face/neck.
                visitorSleeveMesh = Loft("Visitor white short sleeve", new[] {
                    new Vector3(-.165f,.057f,.057f), new Vector3(-.09f,.061f,.062f),
                    new Vector3(-.012f,.057f,.065f), new Vector3(.015f,.040f,.051f),
                    new Vector3(.030f,.019f,.034f) });
                b = new WardrobeMesh();
                // The former flat tie sank behind the shirt's curved chest. A
                // thin ribbon follows every torso profile break above both the
                // shirt (.008) and lapels (.012), continuously down to the navel.
                const float tieOffset=.024f;
                b.Triangle(FrontPoint(new Vector2(0,1.055f),tieOffset),
                    FrontPoint(new Vector2(.012f,1.082f),tieOffset),FrontPoint(new Vector2(-.012f,1.082f),tieOffset));
                Vector2[] ribbon = { new Vector2(1.082f,.012f),new Vector2(1.16f,.011f),
                    new Vector2(1.28f,.010f),new Vector2(1.355f,.009f),new Vector2(1.394f,.0085f),
                    new Vector2(1.404f,.011f),new Vector2(1.419f,.015f) };
                for(int row=1;row<ribbon.Length;row++)
                {
                    Vector2 low=ribbon[row-1],high=ribbon[row];
                    b.Quad(FrontPoint(new Vector2(-low.y,low.x),tieOffset),FrontPoint(new Vector2(low.y,low.x),tieOffset),
                        FrontPoint(new Vector2(high.y,high.x),tieOffset),FrontPoint(new Vector2(-high.y,high.x),tieOffset));
                }
                tieMesh = Finish(b, "Smith necktie");
                trouserYokeMesh = TrouserYoke();
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
                visitorHatMesh = Loft("Visitor blue brimmed hat", new[] { new Vector3(.253f,.165f,.14f),new Vector3(.267f,.165f,.14f),new Vector3(.275f,.10f,.09f),new Vector3(.370f,.092f,.078f),new Vector3(.395f,.075f,.066f) });
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
            private Mesh Loft(string name, Vector3[] profile, int sides = 24)
            {
                var b = new WardrobeMesh();
                sides = Mathf.Clamp(sides, 6, 24);
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
            private Mesh ContactDress(bool skinInset, bool coarse = false)
            {
                // One connected bodice carries paired rounded volume in the
                // fabric itself. A lowered sweetheart edge exposes only the
                // upper chest; the matching skin inset closes into the neck.
                var b=new WardrobeMesh();int sides=coarse?12:36,rows=coarse?12:36;
                Vector3[] profile={new Vector3(.54f,.179f,.135f),new Vector3(.69f,.205f,.152f),
                    new Vector3(.90f,.225f,.15f),new Vector3(1.00f,.183f,.119f),
                    new Vector3(1.13f,.135f,.091f),new Vector3(1.22f,.158f,.103f),
                    new Vector3(1.285f,.173f,.105f),new Vector3(1.345f,.170f,.087f),
                    new Vector3(1.375f,.173f,.077f),new Vector3(1.395f,.151f,.068f),
                    new Vector3(1.423f,.091f,.057f),new Vector3(1.448f,.066f,.054f)};
                float Neckline(float angle)
                {
                    float front=Mathf.Max(0,Mathf.Sin(angle)),x=Mathf.Cos(angle);
                    return 1.385f-.017f*Mathf.Abs(x)-front*(.019f+.073f*Mathf.Exp(-x*x/.14f));
                }
                Vector3 Surface(int row,int column)
                {
                    float authoredRow=row*(coarse?3:1);
                    float angle=(column*(coarse?3:1))*Mathf.PI*2/36,t=authoredRow/36f;
                    float edge=Neckline(angle);
                    // Keep the skirt bounded while putting enough rings across
                    // the curved bodice to avoid a pointed low-resolution outline.
                    float y=skinInset?Mathf.Lerp(edge-.003f,1.448f,t)
                        :authoredRow<=5?Mathf.Lerp(.54f,.78f,authoredRow/5f)
                        :authoredRow<=17?Mathf.Lerp(.78f,1.0f,(authoredRow-5)/12f):Mathf.Lerp(1.0f,edge,(authoredRow-17)/19f);
                    Vector3 section=profile[profile.Length-1];
                    for(int i=1;i<profile.Length;i++)if(y<=profile[i].x)
                    {
                        float blend=Mathf.InverseLerp(profile[i-1].x,profile[i].x,y);
                        Vector3 a=profile[Mathf.Max(0,i-2)],c=profile[i-1],d=profile[i],e=profile[Mathf.Min(profile.Length-1,i+1)];
                        section=.5f*((2*c)+(-a+d)*blend+(2*a-5*c+4*d-e)*blend*blend+(-a+3*c-3*d+e)*blend*blend*blend);
                        break;
                    }
                    float x=Mathf.Cos(angle)*section.y,z=Mathf.Sin(angle)*section.z;
                    if(z>0)
                    {
                        float left=Mathf.Exp(-(x+.075f)*(x+.075f)/(.075f*.075f));
                        float right=Mathf.Exp(-(x-.075f)*(x-.075f)/(.075f*.075f));
                        z+=.055f*(left+right)*Mathf.Exp(-(y-1.265f)*(y-1.265f)/(.09f*.09f))*Mathf.Sin(angle);
                    }
                    if(z<0)
                    {
                        // Rounded seat volume remains part of the opaque cloth
                        // surface; it blends broadly through both hips and waist.
                        float left=Mathf.Exp(-(x+.070f)*(x+.070f)/(.090f*.090f));
                        float right=Mathf.Exp(-(x-.070f)*(x-.070f)/(.090f*.090f));
                        z-=.030f*(left+right)*Mathf.Exp(-(y-.89f)*(y-.89f)/(.11f*.11f))*(-Mathf.Sin(angle));
                    }
                    if(skinInset){x*=.995f;z*=.995f;}
                    return new Vector3(x,y,z);
                }
                for(int row=0;row<rows;row++)for(int side=0;side<sides;side++)
                    b.Quad(Surface(row,side),Surface(row+1,side),Surface(row+1,side+1),Surface(row,side+1));
                Mesh mesh=Finish(b,skinInset?"Contact exposed upper chest":"Contact shaped sweetheart red dress",true,!skinInset,!skinInset);
                if(!skinInset)mesh.bounds=new Bounds(new Vector3(0,.99f,0),new Vector3(.8f,1.2f,1.4f));
                return mesh;
            }
            private Mesh TrouserYoke(bool coarse = false)
            {
                // Four connected cloth panels form the waist, rounded front,
                // inseam and upper legs as one skin-weighted garment. A shader
                // follows the same hip angles as the existing animated shins.
                var b=new WardrobeMesh();int across=coarse?6:12,rows=coarse?12:24;
                Vector3 Surface(int row,int column,int side,int front)
                {
                    float authoredRow = row*(coarse?2:1);
                    float y=authoredRow<=16?Mathf.Lerp(1.015f,.755f,authoredRow/16f):Mathf.Lerp(.755f,.48f,(authoredRow-16)/8f);
                    float u=column/(float)across;
                    float leg=Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.015f,.81f,y));
                    // Keep inside thigh edges on their own side of the center:
                    // crossed panels previously folded the fly into a sharp ridge.
                    float radiusX=Mathf.Lerp(.094f,.064f,Mathf.InverseLerp(.81f,.48f,y));
                    float radiusZ=Mathf.Lerp(.100f,.063f,Mathf.InverseLerp(.81f,.48f,y));
                    float legX=.096f-radiusX*Mathf.Cos(u*Mathf.PI);
                    float x=side*Mathf.Lerp(.158f*u,legX,leg);
                    float depth=Mathf.Lerp(.097f*Mathf.Sqrt(Mathf.Max(0,1-u*u)),radiusZ*Mathf.Sin(u*Mathf.PI),leg);
                    if(front>0)
                    {
                        // Carry fabric across the top of the inseam instead of
                        // collapsing its center onto the inner leg tube. This
                        // produces a broad low drape, not a pinched vertical fly.
                        float bridge=Mathf.Exp(-(y-.90f)*(y-.90f)/(.15f*.15f))
                            *Mathf.SmoothStep(0,1,Mathf.InverseLerp(.72f,.85f,y));
                        float drape=.110f*Mathf.Sqrt(Mathf.Max(0,1-x*x/(.22f*.22f)));
                        depth=Mathf.Lerp(depth,drape,bridge*Mathf.Exp(-x*x/(.12f*.12f))*(1-u*u*(3-2*u)));
                        // A soft, downward resting contour stays within the
                        // continuous garment with a broad upper transition.
                        float shift=Mathf.Lerp(.004f,.012f,Mathf.InverseLerp(.94f,.78f,y));
                        float width=Mathf.Lerp(.125f,.090f,Mathf.InverseLerp(.94f,.78f,y));
                        float shape=.025f*Mathf.Exp(-(x-shift)*(x-shift)/(width*width)
                            -(y-.835f)*(y-.835f)/(.088f*.088f));
                        depth+=shape*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.99f,.93f,y))
                            *Mathf.SmoothStep(0,1,Mathf.InverseLerp(.745f,.79f,y))
                            *Mathf.Sin((u*.5f+.5f)*Mathf.PI);
                    }
                    return new Vector3(x,y,front*depth);
                }
                foreach(int side in Sides)foreach(int front in Sides)
                    for(int row=0;row<rows;row++)for(int column=0;column<across;column++)
                    {
                        Vector3 a=Surface(row,column,side,front),c=Surface(row+1,column,side,front),
                            d=Surface(row+1,column+1,side,front),e=Surface(row,column+1,side,front);
                        if(side*front>0)b.Quad(a,c,d,e);else b.Quad(e,d,c,a);
                    }
                // Close only the front/back center above the inseam. Below it,
                // each leg has its own opening, with no overlapping thigh tube.
                foreach(int front in Sides)for(int row=0;row<rows;row++)
                {
                    Vector3 a=Surface(row,0,-1,front),c=Surface(row+1,0,-1,front),
                        d=Surface(row+1,0,1,front),e=Surface(row,0,1,front);
                    if(a.z*front<.000001f&&c.z*front<.000001f)continue;
                    if(front>0)b.Quad(a,c,d,e);else b.Quad(e,d,c,a);
                }
                return Finish(b,"Tailored continuous trousers",true,true);
            }
            private static Vector3 Ring(Vector3 profile, float angle)
                => new Vector3(Mathf.Cos(angle) * profile.y, profile.x, Mathf.Sin(angle) * profile.z);
            private Mesh Finish(WardrobeMesh builder, string name, bool smooth = false, bool hipWeights = false, bool joinedSkirt = false)
            {
                return builder.Build(name, this, smooth, hipWeights, joinedSkirt);
            }
            internal T Own<T>(T resource) where T : UnityEngine.Object
            {
                // A list growth failure must retain the just-allocated native
                // handle too; the failed constructor will retire this owner.
                pendingOwner = resource;
                owned.Add(resource);
                pendingOwner = null;
                return resource;
            }
            private static void DestroyOwned(UnityEngine.Object resource)
            {
                if (!resource) return;
                if (Application.isPlaying) UnityEngine.Object.Destroy(resource);
                else UnityEngine.Object.DestroyImmediate(resource);
            }
            public void Dispose()
            {
                retired = true;
                if (Application.isPlaying) releaseAfterFrame = Mathf.Max(releaseAfterFrame, Time.frameCount + 1);
                Cleanup();
            }
            private void Cleanup()
            {
                if (Application.isPlaying && lastCleanupFrame == Time.frameCount) return;
                lastCleanupFrame = Time.frameCount;
                bool refused = false;
                for (int index = owned.Count - 1; index >= 0; index--)
                {
                    try { DestroyOwned(owned[index]); if (!owned[index]) owned.RemoveAt(index); }
                    catch (Exception error) { refused = true; WarnCleanup(error); }
                }
                try { DestroyOwned(pendingOwner); if (!pendingOwner) pendingOwner = null; }
                catch (Exception error) { refused = true; WarnCleanup(error); }
                if (!refused) { cleanupRefusals = 0; return; }
                if (cleanupRefusals < 120) cleanupRefusals++;
                if (cleanupRefusals < 120) return;
                bool stillHeld = false;
                for (int index = owned.Count - 1; index >= 0; index--)
                {
                    UnityEngine.Object resource = owned[index];
                    if (!resource) { owned.RemoveAt(index); continue; }
                    try { DestroyImmediate(resource, true); }
                    catch (Exception error) { WarnCleanup(error); stillHeld = true; continue; }
                    if (resource) { stillHeld = true; continue; }
                    owned.RemoveAt(index);
                }
                if (stillHeld) return;
                pendingOwner = null;
                cleanupRefusals = 0;
            }
            private void WarnCleanup(Exception error)
            {
                if (cleanupWarning) return;
                cleanupWarning = true;
                Debug.LogWarning("Crowd wardrobe cleanup is pending; native ownership is retained: " + error.GetType().Name);
            }
            internal static bool DrainRetired()
            {
                bool complete = true;
                for (int index = Owners.Count - 1; index >= 0; index--)
                {
                    MatrixWardrobe owner = Owners[index];
                    if (!owner.retired) continue;
                    owner.Cleanup();
                    if (owner.owned.Count == 0 && !owner.pendingOwner
                        && (!Application.isPlaying || Time.frameCount >= owner.releaseAfterFrame)) Owners.RemoveAt(index);
                    else complete = false;
                }
                return complete;
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
            public Mesh Build(string name, MatrixWardrobe owner, bool smooth = false, bool hipWeights = false, bool joinedSkirt = false)
            {
                Mesh mesh = owner.Own(new Mesh());
                try
                {
                    mesh.name = name; mesh.hideFlags = HideFlags.HideAndDontSave;
                    mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv);
                    mesh.RecalculateNormals();
                    if(smooth)
                    {
                        Vector3[] normals=mesh.normals;var sums=new Dictionary<Vector3,Vector3>();
                        Vector3 Key(Vector3 p)=>new Vector3(Mathf.Round(p.x*100000),Mathf.Round(p.y*100000),Mathf.Round(p.z*100000));
                        for(int i=0;i<vertices.Count;i++){Vector3 key=Key(vertices[i]);sums.TryGetValue(key,out Vector3 sum);sums[key]=sum+normals[i];}
                        for(int i=0;i<vertices.Count;i++)
                        {
                            Vector3 sum=sums[Key(vertices[i])];
                            if(sum.sqrMagnitude<.000001f)throw new InvalidOperationException("Cloth surface has no valid outward normal.");
                            normals[i]=sum.normalized;
                        }
                        mesh.normals=normals;
                    }
                    mesh.RecalculateBounds();
                    if(hipWeights)
                    {
                        var weights=new List<Vector2>(vertices.Count);
                        foreach(Vector3 p in vertices)
                        {
                            float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.0f,.82f,p.y));
                            float right=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.05f,.05f,p.x));
                            right=Mathf.Lerp(right,p.x<0?0:1,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.87f,.80f,p.y)));
                            // A fitted skirt stays joined around the legs. Its
                            // front/rear follow the leading/trailing leg supplied
                            // in the shared pose vector, with smooth side blends.
                            if(joinedSkirt)right=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.025f,.025f,p.z));
                            weights.Add(new Vector2(weight*(1-right),weight*right));
                        }
                        mesh.SetUVs(1,weights);mesh.RecalculateTangents();
                        // The planar cloth UV seam can collapse a UV triangle.
                        // Supply a finite orthogonal tangent even at that seam
                        // before the shader blends the tangent with the hip pose.
                        Vector3[] n=mesh.normals;Vector4[] tangent=mesh.tangents;
                        for(int i=0;i<tangent.Length;i++)
                        {
                            Vector3 t=new Vector3(tangent[i].x,tangent[i].y,tangent[i].z);
                            t-=n[i]*Vector3.Dot(t,n[i]);
                            if(float.IsNaN(t.sqrMagnitude)||float.IsInfinity(t.sqrMagnitude)||t.sqrMagnitude<.000001f)
                                t=Vector3.Cross(Mathf.Abs(n[i].y)<.9f?Vector3.up:Vector3.right,n[i]);
                            t.Normalize();tangent[i]=new Vector4(t.x,t.y,t.z,tangent[i].w<0?-1:1);
                        }
                        mesh.tangents=tangent;
                        mesh.bounds=new Bounds(new Vector3(0,.75f,0),new Vector3(.44f,.62f,1f));
                    }
                    mesh.UploadMeshData(true); return mesh;
                }
                catch
                {
                    // The constructor's registered wardrobe already owns this
                    // exact allocation, including failures in its first setter.
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
        private static Vector2 LegPose(Vector2 foot,float bob)
        {
            float down=.91f-(foot.y-bob),distance=Mathf.Clamp(Mathf.Sqrt(down*down+foot.x*foot.x),.08f,.843f);
            float aim=Mathf.Atan2(-foot.x,down);
            float bend=Mathf.Acos(Mathf.Clamp((.43f*.43f+distance*distance-.415f*.415f)/(2*.43f*distance),-1,1));
            float knee=180-Mathf.Acos(Mathf.Clamp((.43f*.43f+.415f*.415f-distance*distance)/(2*.43f*.415f),-1,1))*Mathf.Rad2Deg;
            return new Vector2((aim-bend)*Mathf.Rad2Deg,knee);
        }
        private static float ReachableHip(Vector2 foot)
            => foot.y + Mathf.Sqrt(Mathf.Max(.01f, .837f * .837f - foot.x * foot.x)) - .91f;
        private static readonly int[] Sides = { -1, 1 };
        private Mesh PresentedMesh(bool coarse, Mesh standIn, SlopCityHumanPart part)
        {
            if (coarse && standIn) return standIn;
            int index = (int)part;
            return visuals.parts != null && (uint)index < (uint)visuals.parts.Length ? visuals.parts[index] : null;
        }
        private void Add(SlopCityHumanPart part, Material material, Matrix4x4 matrix)
        {
            if ((int)part >= visuals.parts.Length) return;
            Add(visuals.parts[(int)part], material, matrix);
        }
        private void Add(Mesh mesh, Material material, Matrix4x4 matrix, Vector4 hipPose = default)
        {
            if (!mesh || !material || (reserveOnly && reservationFailed)) return;
            if (reserveOnly)
            {
                int vertices = mesh.vertexCount;
                if (constrainedCrowd && vertices > MaximumTouchVertices - SubmittedVertexCount - reservedVertices)
                { reservationFailed = true; return; }
                reservedVertices += vertices;
            }
            long key = ((long)mesh.GetInstanceID() << 32) ^ (uint)material.GetInstanceID();
            if (!batchLookup.TryGetValue(key, out DrawBatch batch))
            {
                // Shared wardrobe/body parts need only their visible matrices. Grow each batch to
                // its observed visible count, retaining the API's hard 1023 cap.
                batch = new DrawBatch { mesh = mesh, material = material,
                    matrices = new Matrix4x4[16],
                    hipPoses=(wardrobe != null && wardrobe.animatedTrousers && (mesh==wardrobe.trouserYokeMesh||mesh==wardrobe.coarseTrouserMesh||mesh==wardrobe.dressMesh||mesh==wardrobe.coarseDressMesh))?new Vector4[16]:null,
                    properties=(wardrobe != null && wardrobe.animatedTrousers && (mesh==wardrobe.trouserYokeMesh||mesh==wardrobe.coarseTrouserMesh||mesh==wardrobe.dressMesh||mesh==wardrobe.coarseDressMesh))?new MaterialPropertyBlock():null };
                batchLookup.Add(key, batch);
                try { batches.Add(batch); }
                catch { batchLookup.Remove(key); throw; }
            }
            if(reserveOnly)
            {
                int needed=batch.count+(++batch.reserved);
                if(needed>1023||pendingPartCount>=pendingParts.Length){reservationFailed=true;return;}
                pendingParts[pendingPartCount++]=new PendingPart{batch=batch,matrix=matrix,hipPose=hipPose};
                return;
            }
            // Every key/repetition was reserved atomically before any part was emitted.
            if(batch.count>=batch.matrices.Length)throw new InvalidOperationException("Whole-person matrix reservation was not satisfied.");
            int slot=batch.count++;batch.matrices[slot]=matrix;
            if(batch.hipPoses!=null)batch.hipPoses[slot]=hipPose;
        }
    }
}
