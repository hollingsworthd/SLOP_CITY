using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SlopCity
{
    /// <summary>Six fixed public platforms, continuous physical track and one bounded three-train service.</summary>
    public sealed class SlopCityMetroWorld : MonoBehaviour
    {
        public const int StationCount = 6;
        public const int TrainCount = 3;
        public const float DwellSeconds = 8f, TravelSeconds = 10f;
        public static readonly Vector3 LiftOffset = new Vector3(-4.5f, 0, -2.5f);
        private static readonly string[] Names = { "SIGNAL YARD", "CENTRAL NORTH", "EAST GATE", "NIGHT MARKET", "HOME SOUTH", "WEST GATE" };
        [SerializeField] private bool prepared;
        [SerializeField] private Transform[] stations = Array.Empty<Transform>();
        [SerializeField] private Transform[] destinations = Array.Empty<Transform>();
        [SerializeField] private SlopCityInteractable[] platforms = Array.Empty<SlopCityInteractable>();
        [SerializeField] private Transform[] models = Array.Empty<Transform>();
        [SerializeField] private Mesh[] trackMeshes = Array.Empty<Mesh>();
        [SerializeField] private TextMesh[] statusLabels = Array.Empty<TextMesh>();
        [SerializeField] private Light[] localStationLights = Array.Empty<Light>();
        private SlopCityFirstPerson player;
        private readonly List<SlopCityMetroTrain> trains = new List<SlopCityMetroTrain>(TrainCount);
        private bool initialized;
        private bool ownsTrackMeshes;
        private float nextStatus;
        public IReadOnlyList<Transform> Stations => stations;
        public IReadOnlyList<Transform> Destinations => destinations;
        public IReadOnlyList<SlopCityInteractable> Platforms => platforms;
        public IReadOnlyList<SlopCityMetroTrain> Trains => trains;
        public bool Prepared => prepared;

        private void Update()
        {
            if (!initialized || !player || Time.unscaledTime < nextStatus) return;
            nextStatus = Time.unscaledTime + .5f;
            for (int index = 0; index < statusLabels.Length; index++)
            {
                TextMesh label = statusLabels[index]; if (!label) continue;
                int stop = index / 2;
                string text = GetStationName(stop) + " / LINE 01\n" + GetPlatformStatus(stop);
                if (label.text != text) label.text = text;
            }
            foreach (Light light in localStationLights)
                if (light) light.enabled = player.transform.position.y < -3 && (player.transform.position - light.transform.position).sqrMagnitude < 22 * 22;
        }

        /// <summary>Run after HumanScale.Prepare, before static batching. Also supports older saved exports at runtime.</summary>
        public static SlopCityMetroWorld Prepare(Transform city)
        {
            if (!city) return null;
            Transform network = city.Find("SlopCity_Subway");
            if (!network) return null;
            var world = network.GetComponent<SlopCityMetroWorld>() ?? network.gameObject.AddComponent<SlopCityMetroWorld>();
            if (!world.prepared) world.Build(city);
            return world;
        }

        public static SlopCityMetroWorld Ensure(SlopCityFirstPerson owner)
        {
            if (!owner) return null;
            GameObject city = GameObject.Find("SlopCity_NineBlocks");
            SlopCityMetroWorld world = city ? Prepare(city.transform) : null;
            if (!world) return null;
            world.player = owner;
            if (world.initialized) return world;
            for (int index = 0; index < world.models.Length; index++)
            {
                Transform model = world.models[index];
                if (!model) continue;
                var train = model.GetComponent<SlopCityMetroTrain>() ?? model.gameObject.AddComponent<SlopCityMetroTrain>();
                train.Configure(world, owner, model, index * 2);
                world.trains.Add(train);
            }
            world.initialized = true;
            return world;
        }

        public string GetStationName(int index) => Names[Wrap(index)];
        public float LegTravelSeconds(int index) => TravelSeconds;
        public float LegLength(int index)
        {
            float length = 0; SampleLeg(index, 0, out Vector3 prior, out _);
            for (int sample = 1; sample <= 32; sample++) { SampleLeg(index, sample / 32f, out Vector3 next, out _); length += Vector3.Distance(prior, next); prior = next; }
            return length;
        }

        public void GetStopPose(int index, out Vector3 position, out Quaternion rotation)
        {
            Transform station = stations[Wrap(index)];
            position = station.TransformPoint(new Vector3(0, -7.86f, 3.1f)); rotation = station.rotation;
        }

        public void SampleLeg(int from, float t, out Vector3 point, out Quaternion rotation)
        {
            from = Wrap(from); int to = Wrap(from + 1); t = Mathf.Clamp01(t);
            GetStopPose(from, out Vector3 a, out Quaternion qa); GetStopPose(to, out Vector3 d, out Quaternion qd);
            bool turning = Vector3.Dot(qa * Vector3.right, qd * Vector3.right) < 0;
            float handle = turning ? 35f : Vector3.Distance(a, d) / 3f;
            Vector3 b = a + qa * Vector3.right * handle, c = d - qd * Vector3.right * handle;
            float u = 1 - t;
            point = u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
            Vector3 tangent = 3 * u * u * (b - a) + 6 * u * t * (c - b) + 3 * t * t * (d - c);
            rotation = tangent.sqrMagnitude > .0001f ? Quaternion.LookRotation(Vector3.Cross(tangent.normalized, Vector3.up), Vector3.up) : qa;
        }

        public string GetPlatformStatus(int index)
        {
            index = Wrap(index); float seconds = float.PositiveInfinity;
            foreach (SlopCityMetroTrain train in trains)
            {
                if (!train) continue;
                if (!train.IsMoving && train.CurrentStop == index && train.DoorProgress > .95f)
                    return "TRAIN BOARDING / NEXT " + GetStationName(index + 1) + " / DEPARTS IN " + Mathf.CeilToInt(train.SecondsUntilDeparture) + "s";
                seconds = Mathf.Min(seconds, train.SecondsUntilStop(index));
            }
            return float.IsPositiveInfinity(seconds) ? "AUTOMATIC METRO / " + GetStationName(index + 1)
                : "NEXT TRAIN " + Mathf.CeilToInt(Mathf.Max(0, seconds)) + "s / " + GetStationName(index + 1);
        }

        public void NotifyRiderArrival(SlopCityMetroTrain train, int origin, int arrival)
        {
            if (!player || !player.Sandbox || !train || train.World != this || !trains.Contains(train)
                || train.IsMoving || !train.PlayerInside || train.CurrentStop != arrival
                || origin < 0 || origin >= StationCount || arrival != Wrap(origin + 1)) return;
            player.Sandbox.NotifyMetroArrival(platforms[origin], destinations[arrival], train);
        }

        private static int Wrap(int index) => (index % StationCount + StationCount) % StationCount;

        private void Build(Transform city)
        {
            Transform oldWest = transform.Find("Subway_Station_0"), oldEast = transform.Find("Subway_Station_1");
            if (!oldWest || !oldEast) throw new InvalidOperationException("Metro requires the two preserved authored station roots.");
            Transform westTrain = oldWest.Find("Subway_Train_0"), eastTrain = oldEast.Find("Subway_Train_1");
            if (!westTrain || !eastTrain) throw new InvalidOperationException("Metro requires both original authored carriage models.");
            string westLegacy = LegacyId(westTrain.Find("Train_Departure_Control")), eastLegacy = LegacyId(eastTrain.Find("Train_Departure_Control"));
            Renderer floorSource = oldWest.Find("Station_Floor").GetComponent<Renderer>();
            Renderer wallSource = oldWest.Find("Platform_RearWall").GetComponent<Renderer>();
            Renderer railSource = oldWest.GetComponentsInChildren<Renderer>(true).First(item => item.name == "Running_Rail");
            Mesh cube = oldWest.Find("Station_Floor").GetComponent<MeshFilter>().sharedMesh;
            Material floor = floorSource.sharedMaterial, wall = wallSource.sharedMaterial, metal = railSource.sharedMaterial;
            TextMesh lettering = oldWest.GetComponentsInChildren<TextMesh>(true).FirstOrDefault(item => item.font && item.GetComponent<Renderer>());
            Vector3 west = city.InverseTransformPoint(oldWest.position), east = city.InverseTransformPoint(oldEast.position);
            float centerX = (west.x + east.x) * .5f;
            stations = new Transform[StationCount]; stations[0] = oldWest; stations[3] = oldEast;
            stations[1] = NewStation("Subway_Station_2", city.TransformPoint(new Vector3(centerX, 0, west.z)), false, cube, floor, wall, metal);
            stations[2] = NewStation("Subway_Station_3", city.TransformPoint(new Vector3(east.x, 0, west.z)), false, cube, floor, wall, metal);
            stations[4] = NewStation("Subway_Station_4", city.TransformPoint(new Vector3(centerX, 0, east.z)), true, cube, floor, wall, metal);
            stations[5] = NewStation("Subway_Station_5", city.TransformPoint(new Vector3(west.x, 0, east.z)), true, cube, floor, wall, metal);
            oldEast.rotation = city.rotation * Quaternion.Euler(0, 180, 0);
            platforms = new SlopCityInteractable[StationCount]; destinations = new Transform[StationCount];
            var labels = new List<TextMesh>(); var lights = new List<Light>();
            for (int index = 0; index < StationCount; index++)
            {
                Transform station = stations[index];
                OpenTrackEnds(station, cube, wall);
                Transform platform = station.Cast<Transform>().FirstOrDefault(item => item.name.StartsWith("Metro_Platform_", StringComparison.Ordinal));
                if (!platform) platform = Root(station, "Metro_Platform_" + index, Vector3.zero);
                platform.localPosition = LiftOffset + new Vector3(0, -7.835f, -1.55f);
                destinations[index] = platform;
                Transform surface = station.Cast<Transform>().FirstOrDefault(item => item.name.StartsWith("Metro_Surface_", StringComparison.Ordinal));
                if (!surface) surface = Root(station, "Metro_Surface_" + index, Vector3.zero);
                surface.localPosition = LiftOffset + new Vector3(0, .1f, -1.55f);
                Transform entry = station.Find("Subway_SurfaceLift"); if (entry) entry.localPosition = LiftOffset;
                Transform down = entry ? entry.Find("Platform_Lift_Control") : null;
                Transform up = station.Find("Street_Lift_Control");
                if (down && down.GetComponent<SlopCityInteractable>()) down.GetComponent<SlopCityInteractable>().ConfigureDestination(platform);
                if (up && up.GetComponent<SlopCityInteractable>()) up.GetComponent<SlopCityInteractable>().ConfigureDestination(surface);
                Transform console = index == 0 ? westTrain.Find("Train_Departure_Control") : index == 3 ? eastTrain.Find("Train_Departure_Control") : null;
                if (!console) console = Box(station, "Train_Departure_Control", new Vector3(3.5f, -6.65f, -.5f), new Vector3(.45f, .55f, .18f), cube, metal).transform;
                console.SetParent(station, false); console.localPosition = new Vector3(3.5f, -6.65f, -.5f); console.localRotation = Quaternion.identity;
                SlopCityInteractable item = console.GetComponent<SlopCityInteractable>() ?? console.gameObject.AddComponent<SlopCityInteractable>();
                item.Configure(SlopCityInteractionKind.Subway, "RIDE TO " + GetStationName(index + 1), false);
                item.ConfigureInteractionPoint(console.position + station.forward * .13f);
                var stop = console.GetComponent<SlopCityMetroPlatform>() ?? console.gameObject.AddComponent<SlopCityMetroPlatform>();
                stop.Configure(this, index); stop.LegacyRouteId = index == 0 ? westLegacy : index == 3 ? eastLegacy : "";
                platforms[index] = item;
                foreach (TextMesh text in console.GetComponentsInChildren<TextMesh>(true)) text.text = "NEXT " + GetStationName(index + 1) + "\nAUTOMATIC METRO";
                if (lettering)
                {
                    labels.Add(Label(station, "Metro_Platform_Live_Service", new Vector3(0, -5.8f, -5.23f), Quaternion.Euler(0, 180, 0), lettering, index));
                    labels.Add(Label(station, "Metro_Street_Live_Service", LiftOffset + new Vector3(0, 2.45f, -1.91f), Quaternion.identity, lettering, index));
                }
                if (index != 0 && index != 3)
                {
                    Light light = Root(station, "Metro_Local_NoShadow_Light", new Vector3(0, -5, -1)).gameObject.AddComponent<Light>();
                    light.type = LightType.Point; light.range = 11; light.intensity = .65f; light.shadows = LightShadows.None; light.color = new Color(.8f, .87f, .84f); light.enabled = false; lights.Add(light);
                }
            }
            statusLabels = labels.ToArray(); localStationLights = lights.ToArray();
            for (int index = 0; index < StationCount; index++) platforms[index].ConfigureDestination(destinations[Wrap(index + 1)]);
            // Keep the two original carriage objects/materials. The third is an exact shared-geometry
            // clone made before any player, decoder, physical train controller or session exists.
            Transform third = Instantiate(westTrain.gameObject, transform, false).transform; third.name = "Subway_Train_2";
            PrepareThirdMedia(third);
            westTrain.SetParent(transform, true); eastTrain.SetParent(transform, true);
            models = new[] { westTrain, eastTrain, third };
            for (int index = 0; index < models.Length; index++)
            {
                GetStopPose(index * 2, out Vector3 position, out Quaternion rotation); models[index].SetPositionAndRotation(position, rotation);
                foreach (SlopCityInteractable control in models[index].GetComponentsInChildren<SlopCityInteractable>(true))
                    if (control.Kind == SlopCityInteractionKind.Subway && control.Destination) control.Configure(control.Kind, control.DisplayName, false);
                foreach (Transform part in models[index].GetComponentsInChildren<Transform>(true)) part.gameObject.isStatic = false;
            }
            BuildTrack(cube, floor, wall, metal);
            // Rotation of the existing south platform must rotate its baked commuters too.
            SlopCityCrowd crowd = city.GetComponentInChildren<SlopCityCrowd>(true);
            if (crowd) crowd.RemapLayout((district, point) => district == 11 ? oldEast.position + Quaternion.Euler(0, 180, 0) * (point - oldEast.position) : point);
            prepared = true; Physics.SyncTransforms();
            if (SlopCityCityMedia.Instance) SlopCityCityMedia.Instance.RegisterAnchors(transform);
        }

        private Transform NewStation(string name, Vector3 position, bool south, Mesh cube, Material floor, Material wall, Material metal)
        {
            Transform station = Root(transform, name, Vector3.zero); station.position = position; station.rotation = transform.rotation * Quaternion.Euler(0, south ? 180 : 0, 0);
            Box(station, "Station_Floor", new Vector3(0, -8, 0), new Vector3(15, .28f, 11), cube, floor);
            Box(station, "Station_Ceiling", new Vector3(0, -4.4f, 0), new Vector3(15, .3f, 11), cube, wall);
            Box(station, "Platform_RearWall", new Vector3(0, -6.25f, -5.4f), new Vector3(15, 3.5f, .2f), cube, wall);
            Transform entry = Root(station, "Subway_SurfaceLift", LiftOffset);
            Box(entry, "Metro_Canopy", new Vector3(0, 2.8f, 0), new Vector3(3.4f, .2f, 3.5f), cube, metal);
            CreateLiftControl(entry, "Platform_Lift_Control", new Vector3(0, 1.15f, -1.6f), cube, metal, "LIFT TO SUBWAY");
            CreateLiftControl(station, "Street_Lift_Control", new Vector3(-5, -6.7f, -4), cube, metal, "LIFT TO STREET");
            return station;
        }

        private static void CreateLiftControl(Transform root, string name, Vector3 position, Mesh cube, Material material, string label)
        {
            GameObject control = Box(root, name, position, new Vector3(.35f, .45f, .16f), cube, material);
            control.AddComponent<SlopCityInteractable>().Configure(SlopCityInteractionKind.Subway, label, false);
        }

        private static void OpenTrackEnds(Transform station, Mesh cube, Material material)
        {
            foreach (Transform child in station.Cast<Transform>().ToArray())
                if (child.name == "Platform_EndWall" || child.name == "Tunnel_BackWall") Disable(child);
            foreach (float x in new[] { -7.4f, 7.4f })
                Box(station, "Platform_EndWall_With_Track_Opening", new Vector3(x, -6.25f, -3f), new Vector3(.2f, 3.5f, 5), cube, material);
            Box(station, "Tunnel_BackWall_Clear_Of_Train", new Vector3(0, -6.25f, 6.8f), new Vector3(15, 3.5f, .2f), cube, material);
        }

        private void BuildTrack(Mesh cube, Material floor, Material wall, Material metal)
        {
            var meshes = new List<Mesh>();
            Transform root = null;
            try
            {
                root = Root(transform, "Continuous_Metro_Track", Vector3.zero);
                for (int leg = 0; leg < StationCount; leg++)
                {
                    Strip bed = new Strip(), rails = new Strip(), enclosure = new Strip();
                    int segments = Mathf.Max(24, Mathf.CeilToInt(LegLength(leg) / 2f));
                    for (int segment = 0; segment < segments; segment++)
                    {
                        SampleLeg(leg, segment / (float)segments, out Vector3 a, out Quaternion qa);
                        SampleLeg(leg, (segment + 1f) / segments, out Vector3 b, out Quaternion qb);
                        Vector3 za = qa * Vector3.forward, zb = qb * Vector3.forward;
                        Vector3 up = Vector3.up;
                        bed.Quad(a + za * 3.2f - up * .01f, b + zb * 3.2f - up * .01f, b - zb * 3.2f - up * .01f, a - za * 3.2f - up * .01f);
                        foreach (float side in new[] { -1f, 1f })
                        {
                            Vector3 ar = a + za * side + up * .06f, br = b + zb * side + up * .06f;
                            rails.Quad(ar + za * .04f, br + zb * .04f, br - zb * .04f, ar - za * .04f);
                            rails.Quad(ar - za * .04f, br - zb * .04f, br - zb * .04f - up * .06f, ar - za * .04f - up * .06f);
                            rails.Quad(ar + za * .04f - up * .06f, br + zb * .04f - up * .06f, br + zb * .04f, ar + za * .04f);
                        }
                        enclosure.Quad(a - za * 3.6f + up * 3.38f, b - zb * 3.6f + up * 3.38f, b + zb * 3.6f + up * 3.38f, a + za * 3.6f + up * 3.38f);
                        // The outer wall remains continuous behind each train. Only the
                        // platform-facing wall opens, so an arriving passenger can board.
                        enclosure.Quad(a + za * 3.6f, a + za * 3.6f + up * 3.38f, b + zb * 3.6f + up * 3.38f, b + zb * 3.6f);
                        GetStopPose(leg, out Vector3 departure, out _); GetStopPose(leg + 1, out Vector3 arrival, out _);
                        Vector3 middle = (a + b) * .5f;
                        if (Vector3.Distance(middle, departure) < 10 || Vector3.Distance(middle, arrival) < 10) continue;
                        enclosure.Quad(a - za * 3.6f + up * 3.38f, a - za * 3.6f, b - zb * 3.6f, b - zb * 3.6f + up * 3.38f);
                    }
                    MakeStrip(root, "Track_Bed_" + leg, bed, floor, meshes);
                    MakeStrip(root, "Running_Rails_" + leg, rails, metal, meshes);
                    MakeStrip(root, "Tunnel_Envelope_" + leg, enclosure, wall, meshes);
                }
                trackMeshes = meshes.ToArray();
                ownsTrackMeshes = true;
            }
            catch
            {
                foreach (Mesh mesh in meshes) if (mesh) Dispose(mesh);
                if (root) Dispose(root.gameObject);
                throw;
            }
        }

        private static TextMesh Label(Transform station, string name, Vector3 position, Quaternion rotation, TextMesh donor, int stop)
        {
            Transform root = Root(station, name, position); root.localRotation = rotation;
            TextMesh label = root.gameObject.AddComponent<TextMesh>(); label.font = donor.font; label.fontSize = 64; label.characterSize = .022f;
            label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = new Color(.85f, .94f, .87f);
            label.text = Names[stop] + " / LINE 01\nAUTOMATIC METRO";
            Renderer renderer = label.GetComponent<Renderer>(); renderer.sharedMaterial = donor.GetComponent<Renderer>().sharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            root.gameObject.AddComponent<SlopCityWorldText>().Configure(renderer.sharedMaterial);
            return label;
        }

        private void OnDestroy()
        {
            // Only meshes generated by this live instance are owned. Deserialized build
            // meshes may be shared/asset-backed and never acquire this transient flag.
            if (ownsTrackMeshes) foreach (Mesh mesh in trackMeshes) if (mesh) Dispose(mesh);
        }

        private static void MakeStrip(Transform root, string name, Strip strip, Material material, List<Mesh> owned)
        {
            if (strip.vertices.Count == 0) return;
            Mesh mesh = new Mesh();
            try { owned.Add(mesh); }
            catch { owned.Remove(mesh); Dispose(mesh); throw; }
            mesh.name = name; mesh.SetVertices(strip.vertices); mesh.SetTriangles(strip.triangles, 0); mesh.SetUVs(0, strip.uv); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            GameObject target = new GameObject(name);
            try
            {
                target.transform.SetParent(root, false);
                // Vertices were sampled in world space; put them in this transform's local space once.
                Vector3[] vertices = mesh.vertices; for (int index = 0; index < vertices.Length; index++) vertices[index] = target.transform.InverseTransformPoint(vertices[index]);
                mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                target.AddComponent<MeshFilter>().sharedMesh = mesh; target.AddComponent<MeshRenderer>().sharedMaterial = material;
                target.AddComponent<MeshCollider>().sharedMesh = mesh; target.isStatic = true;
            }
            catch { Dispose(target); throw; }
        }

        private sealed class Strip
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<int> triangles = new List<int>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int start = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                float length = Vector3.Distance(a, b), width = Vector3.Distance(a, d);
                uv.Add(Vector2.zero); uv.Add(new Vector2(length, 0)); uv.Add(new Vector2(length, width)); uv.Add(new Vector2(0, width));
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2); triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }
        }

        private static string LegacyId(Transform item)
        {
            if (!item) return "";
            string path = item.name; for (Transform parent = item.parent; parent; parent = parent.parent) path = parent.name + "/" + path;
            Vector3 p = item.position; return path + ":" + Mathf.RoundToInt(p.x * 10) + "," + Mathf.RoundToInt(p.y * 10) + "," + Mathf.RoundToInt(p.z * 10);
        }

        private static void PrepareThirdMedia(Transform model)
        {
            // Preserve the authored media anchor. If a late Ensure cloned a live
            // display, retire that copied surface synchronously and recreate it
            // under the anchor with fresh, independently owned media resources.
            foreach (SlopCityCityScreen screen in model.GetComponentsInChildren<SlopCityCityScreen>(true))
            {
                screen.gameObject.SetActive(false);
                screen.transform.SetParent(null, true);
                Dispose(screen.gameObject);
            }
            foreach (Light light in model.GetComponentsInChildren<Light>(true)) Dispose(light.gameObject);
        }

        private static Transform Root(Transform parent, string name, Vector3 position)
        {
            var root = new GameObject(name);
            try { root.transform.SetParent(parent, false); root.transform.localPosition = position; return root.transform; }
            catch { Dispose(root); throw; }
        }
        private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Mesh cube, Material material)
        {
            GameObject box = new GameObject(name); box.transform.SetParent(parent, false); box.transform.localPosition = position; box.transform.localScale = size;
            box.AddComponent<MeshFilter>().sharedMesh = cube; box.AddComponent<MeshRenderer>().sharedMaterial = material; box.AddComponent<BoxCollider>(); box.isStatic = true; return box;
        }
        private static void Disable(Transform piece)
        {
            foreach (Renderer renderer in piece.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (Collider collider in piece.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        }
        private static void Dispose(Object item) { if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
    }

}
