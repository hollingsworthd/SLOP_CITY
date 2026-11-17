using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Small local presentation layer for the virtual Sprawl census. Cars and
    /// pedestrians are sampled from verified sidewalk/road polylines only; the
    /// class never creates collision, traffic, track, or a train in unbuilt
    /// water. Subway trips receive a brief rider marker only at an actual
    /// transit landing; their board/ride/exit stages remain owned by the
    /// verified census.
    /// </summary>
    [DefaultExecutionOrder(-16)]
    [DisallowMultipleComponent]
    public sealed class SlopCitySprawlCommuteVisuals : MonoBehaviour
    {
        public const int MaximumCarVisuals = 6;
        public const int MaximumPedestrianVisuals = 12;
        public const int MaximumLandingRiderMarkers = 8;
        private const float NearbyRadiusMeters = 112f;
        private const float RefreshSeconds = .10f;
        // The census reports one normalized trip progress value. These fixed,
        // deterministic windows reserve the middle of a subway journey for its
        // authoritative logical ride stage rather than inventing a train.
        private const float SubwayWalkToStationEnd = .20f;
        private const float SubwayBoardEnd = .25f;
        private const float SubwayExitStart = .75f;
        private const float SubwayWalkFromStationStart = .80f;

        private readonly List<SlopCitySprawlCommute> commutes = new List<SlopCitySprawlCommute>();
        private readonly Dictionary<string, CarVisual> cars = new Dictionary<string, CarVisual>(StringComparer.Ordinal);
        private readonly Dictionary<string, PedestrianVisual> pedestrians = new Dictionary<string, PedestrianVisual>(StringComparer.Ordinal);
        private readonly Dictionary<string, RiderMarker> riders = new Dictionary<string, RiderMarker>(StringComparer.Ordinal);
        private SlopCitySprawlTransitStops transit;
        private SlopCityCityForgeController controller;
        private SlopCityFirstPerson player;
        private Material carMaterial, smithMaterial, redMaterial;
        private int serial;
        private float nextRefresh;

        public int ActiveCarVisuals { get; private set; }
        public int ActivePedestrianVisuals { get; private set; }
        public int ActiveLandingRiderMarkers { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<SlopCitySprawlCommuteVisuals>()) return;
            new GameObject("SlopCitySprawlCommuteVisuals").AddComponent<SlopCitySprawlCommuteVisuals>();
        }

        private void Update()
        {
            Bind();
            if (transit == null || controller == null || !controller.IsInitialized || player == null) return;
            if (player.IsPaused) { HideAll(); return; }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            RefreshVisuals();
        }

        private void OnDestroy()
        {
            foreach (CarVisual car in cars.Values) if (car.root) Destroy(car.root);
            foreach (PedestrianVisual pedestrian in pedestrians.Values) if (pedestrian.root) Destroy(pedestrian.root);
            foreach (RiderMarker rider in riders.Values) if (rider.root) Destroy(rider.root);
            if (carMaterial) Destroy(carMaterial);
            if (smithMaterial) Destroy(smithMaterial);
            if (redMaterial) Destroy(redMaterial);
        }

        private void Bind()
        {
            if (transit == null) transit = FindFirstObjectByType<SlopCitySprawlTransitStops>();
            if (controller == null) controller = FindFirstObjectByType<SlopCityCityForgeController>();
            if (player == null) player = FindFirstObjectByType<SlopCityFirstPerson>();
        }

        private void RefreshVisuals()
        {
            serial++;
            ActiveCarVisuals = 0;
            ActivePedestrianVisuals = 0;
            ActiveLandingRiderMarkers = 0;
            commutes.Clear();
            transit.GetNearbyVisualCommutes(player.transform.position, NearbyRadiusMeters, commutes);
            foreach (SlopCitySprawlCommute commute in commutes)
            {
                if (commute == null || !commute.isTraveling || string.IsNullOrEmpty(commute.virtualResidentId)) continue;
                if (commute.mode == SlopCitySprawlTravelMode.Car && ActiveCarVisuals < MaximumCarVisuals
                    && TryDrivePose(commute, out Vector3 carPosition, out Quaternion carRotation))
                {
                    CarVisual car = GetCar(commute.virtualResidentId);
                    car.root.transform.SetPositionAndRotation(carPosition, carRotation);
                    car.root.SetActive(true);
                    car.serial = serial;
                    ActiveCarVisuals++;
                }
                else if ((commute.mode == SlopCitySprawlTravelMode.Walk || commute.mode == SlopCitySprawlTravelMode.Subway)
                    && ActivePedestrianVisuals < MaximumPedestrianVisuals
                    && TryPedestrianPose(commute, out Vector3 pedestrianPosition, out Quaternion pedestrianRotation))
                {
                    PedestrianVisual pedestrian = GetPedestrian(commute.virtualResidentId);
                    pedestrian.root.transform.SetPositionAndRotation(pedestrianPosition, pedestrianRotation);
                    pedestrian.root.SetActive(true);
                    pedestrian.serial = serial;
                    ActivePedestrianVisuals++;
                }
                else if (commute.mode == SlopCitySprawlTravelMode.Subway && ActiveLandingRiderMarkers < MaximumLandingRiderMarkers
                    && TryLandingRiderPose(commute, out Vector3 riderPosition, out Quaternion riderRotation))
                {
                    RiderMarker rider = GetRider(commute.virtualResidentId);
                    rider.root.transform.SetPositionAndRotation(riderPosition, riderRotation);
                    rider.root.SetActive(true);
                    rider.serial = serial;
                    ActiveLandingRiderMarkers++;
                }
            }
            RetireStale();
        }

        private bool TryPedestrianPose(SlopCitySprawlCommute commute, out Vector3 position, out Quaternion rotation)
        {
            position = default(Vector3); rotation = Quaternion.identity;
            float progress = Mathf.Clamp01(commute.progress01);
            foreach (SlopCitySprawlRouteStage stage in commute.routeStages ?? Array.Empty<SlopCitySprawlRouteStage>())
            {
                if (stage == null || stage.sidewalkPoints == null || stage.sidewalkPoints.Length < 2) continue;
                float stageProgress;
                switch (stage.kind)
                {
                    case SlopCitySprawlRouteStageKind.WalkStreet:
                        stageProgress = progress;
                        break;
                    case SlopCitySprawlRouteStageKind.WalkToStation:
                        if (commute.mode != SlopCitySprawlTravelMode.Subway || progress >= SubwayWalkToStationEnd) continue;
                        stageProgress = progress / SubwayWalkToStationEnd;
                        break;
                    case SlopCitySprawlRouteStageKind.WalkFromStation:
                        if (commute.mode != SlopCitySprawlTravelMode.Subway || progress < SubwayWalkFromStationStart) continue;
                        stageProgress = (progress - SubwayWalkFromStationStart) / (1f - SubwayWalkFromStationStart);
                        break;
                    default:
                        continue;
                }
                // Every point, including the intervening street nodes, must
                // belong to accepted ground before a walker is made visible.
                if (!GroundedPolyline(stage.sidewalkPoints)) return false;
                return SamplePolyline(stage.sidewalkPoints, stageProgress, out position, out rotation);
            }
            return false;
        }

        private bool TryDrivePose(SlopCitySprawlCommute commute, out Vector3 position, out Quaternion rotation)
        {
            position = default(Vector3); rotation = Quaternion.identity;
            SlopCitySprawlRouteStage drive = null;
            foreach (SlopCitySprawlRouteStage stage in commute.routeStages ?? Array.Empty<SlopCitySprawlRouteStage>())
                if (stage != null && stage.kind == SlopCitySprawlRouteStageKind.DriveStreet && stage.sidewalkPoints != null && stage.sidewalkPoints.Length >= 2)
                { drive = stage; break; }
            if (drive == null || !GroundedPolyline(drive.sidewalkPoints)) return false;
            return SamplePolyline(drive.sidewalkPoints, commute.progress01, out position, out rotation);
        }

        private bool TryLandingRiderPose(SlopCitySprawlCommute commute, out Vector3 position, out Quaternion rotation)
        {
            position = default(Vector3); rotation = Quaternion.identity;
            SlopCitySprawlRouteStage board = null, exit = null;
            foreach (SlopCitySprawlRouteStage stage in commute.routeStages ?? Array.Empty<SlopCitySprawlRouteStage>())
            {
                if (stage == null) continue;
                if (stage.kind == SlopCitySprawlRouteStageKind.BoardSubway) board = stage;
                else if (stage.kind == SlopCitySprawlRouteStageKind.ExitSubway) exit = stage;
            }
            float progress = Mathf.Clamp01(commute.progress01);
            SlopCitySprawlRouteStage selected = progress >= SubwayWalkToStationEnd && progress < SubwayBoardEnd ? board
                : progress >= SubwayExitStart && progress < SubwayWalkFromStationStart ? exit : null;
            if (selected == null || !GroundedPoint(selected.from)) return false;
            position = selected.from;
            Vector3 direction = selected.to - selected.from; direction.y = 0f;
            if (direction.sqrMagnitude > .0001f) rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            return true;
        }

        private bool GroundedPolyline(Vector3[] points)
        {
            foreach (Vector3 point in points) if (!GroundedPoint(point)) return false;
            return true;
        }

        private bool GroundedPoint(Vector3 point)
        {
            CityForgeCoord coord = new CityForgeCoord(Mathf.FloorToInt(point.x / SlopCityCityForge.ChunkSizeMeters),
                Mathf.FloorToInt(point.z / SlopCityCityForge.ChunkSizeMeters));
            if (!SlopCityCityForge.IsBuildable(coord)) return true;
            if (!controller.TryGetAccepted(coord, out CityForgeChunkPlan plan) || plan.groundCells == null) return false;
            float localX = point.x - coord.x * SlopCityCityForge.ChunkSizeMeters;
            float localZ = point.z - coord.z * SlopCityCityForge.ChunkSizeMeters;
            int cellX = Mathf.FloorToInt(localX / SlopCityCityForge.CellSizeMeters);
            int cellZ = Mathf.FloorToInt(localZ / SlopCityCityForge.CellSizeMeters);
            if (cellX < 0 || cellX >= SlopCityCityForge.GridSize || cellZ < 0 || cellZ >= SlopCityCityForge.GridSize) return false;
            return plan.groundCells.Contains(cellX + cellZ * SlopCityCityForge.GridSize);
        }

        private static bool SamplePolyline(Vector3[] points, float progress, out Vector3 position, out Quaternion rotation)
        {
            position = default(Vector3); rotation = Quaternion.identity;
            float length = 0f;
            for (int index = 1; index < points.Length; index++) length += Vector3.Distance(points[index - 1], points[index]);
            if (length <= .001f) return false;
            float remaining = Mathf.Clamp01(progress) * length;
            for (int index = 1; index < points.Length; index++)
            {
                Vector3 from = points[index - 1], to = points[index];
                float segment = Vector3.Distance(from, to);
                if (segment <= .0001f) continue;
                if (remaining > segment) { remaining -= segment; continue; }
                position = Vector3.Lerp(from, to, remaining / segment) + Vector3.up * .03f;
                Vector3 direction = to - from; direction.y = 0f;
                rotation = direction.sqrMagnitude > .0001f ? Quaternion.LookRotation(direction.normalized, Vector3.up) : Quaternion.identity;
                return true;
            }
            Vector3 lastDirection = points[points.Length - 1] - points[points.Length - 2]; lastDirection.y = 0f;
            position = points[points.Length - 1] + Vector3.up * .03f;
            rotation = lastDirection.sqrMagnitude > .0001f ? Quaternion.LookRotation(lastDirection.normalized, Vector3.up) : Quaternion.identity;
            return true;
        }

        private CarVisual GetCar(string id)
        {
            if (cars.TryGetValue(id, out CarVisual value)) return value;
            if (cars.Count >= MaximumCarVisuals)
            {
                string reusableId = null; CarVisual reusable = null;
                foreach (KeyValuePair<string, CarVisual> item in cars)
                    if (item.Value.serial != serial) { reusableId = item.Key; reusable = item.Value; break; }
                if (reusable != null)
                {
                    cars.Remove(reusableId); cars.Add(id, reusable);
                    return reusable;
                }
            }
            GameObject root = new GameObject("SprawlCommuterCar_" + cars.Count);
            root.transform.SetParent(transform, false); SetIgnoreRaycast(root);
            Material paint = MaterialFor(ref carMaterial, new Color(.10f, .17f, .20f));
            MakeBox(root.transform, "Body", new Vector3(0f, .43f, 0f), new Vector3(1.72f, .62f, 3.55f), paint);
            MakeBox(root.transform, "Cabin", new Vector3(0f, .93f, -.10f), new Vector3(1.37f, .48f, 1.76f), paint);
            Transform driver = MakeCapsule(root.transform, "DriverSmith", new Vector3(0f, 1.06f, -.24f), new Vector3(.27f, .38f, .27f), MaterialFor(ref smithMaterial, new Color(.055f, .061f, .066f)));
            value = new CarVisual { root = root, driver = driver }; cars.Add(id, value); return value;
        }

        private PedestrianVisual GetPedestrian(string id)
        {
            if (pedestrians.TryGetValue(id, out PedestrianVisual value)) return value;
            if (pedestrians.Count >= MaximumPedestrianVisuals)
            {
                string reusableId = null; PedestrianVisual reusable = null;
                foreach (KeyValuePair<string, PedestrianVisual> item in pedestrians)
                    if (item.Value.serial != serial) { reusableId = item.Key; reusable = item.Value; break; }
                if (reusable != null)
                {
                    pedestrians.Remove(reusableId); pedestrians.Add(id, reusable);
                    return reusable;
                }
            }
            GameObject root = new GameObject("SprawlRoutePedestrian_" + pedestrians.Count);
            root.transform.SetParent(transform, false); SetIgnoreRaycast(root);
            bool red = id.IndexOf(":red:", StringComparison.Ordinal) >= 0;
            Material clothing = red ? MaterialFor(ref redMaterial, new Color(.58f, .06f, .08f)) : MaterialFor(ref smithMaterial, new Color(.055f, .061f, .066f));
            MakeCapsule(root.transform, red ? "RouteWalkerRed" : "RouteWalkerSmith", new Vector3(0f, .87f, 0f), new Vector3(.34f, .87f, .34f), clothing);
            value = new PedestrianVisual { root = root }; pedestrians.Add(id, value); return value;
        }

        private RiderMarker GetRider(string id)
        {
            if (riders.TryGetValue(id, out RiderMarker value)) return value;
            if (riders.Count >= MaximumLandingRiderMarkers)
            {
                string reusableId = null; RiderMarker reusable = null;
                foreach (KeyValuePair<string, RiderMarker> item in riders)
                    if (item.Value.serial != serial) { reusableId = item.Key; reusable = item.Value; break; }
                if (reusable != null)
                {
                    riders.Remove(reusableId); riders.Add(id, reusable);
                    return reusable;
                }
            }
            GameObject root = new GameObject("SprawlTransitLandingRider_" + riders.Count);
            root.transform.SetParent(transform, false); SetIgnoreRaycast(root);
            bool red = id.IndexOf(":red:", StringComparison.Ordinal) >= 0;
            Material clothing = red ? MaterialFor(ref redMaterial, new Color(.58f, .06f, .08f)) : MaterialFor(ref smithMaterial, new Color(.055f, .061f, .066f));
            MakeCapsule(root.transform, red ? "LandingRiderRed" : "LandingRiderSmith", new Vector3(0f, .87f, 0f), new Vector3(.34f, .87f, .34f), clothing);
            value = new RiderMarker { root = root }; riders.Add(id, value); return value;
        }

        private void RetireStale()
        {
            foreach (CarVisual car in cars.Values) if (car.serial != serial && car.root) car.root.SetActive(false);
            foreach (PedestrianVisual pedestrian in pedestrians.Values) if (pedestrian.serial != serial && pedestrian.root) pedestrian.root.SetActive(false);
            foreach (RiderMarker rider in riders.Values) if (rider.serial != serial && rider.root) rider.root.SetActive(false);
        }

        private void HideAll()
        {
            foreach (CarVisual car in cars.Values) if (car.root) car.root.SetActive(false);
            foreach (PedestrianVisual pedestrian in pedestrians.Values) if (pedestrian.root) pedestrian.root.SetActive(false);
            foreach (RiderMarker rider in riders.Values) if (rider.root) rider.root.SetActive(false);
            ActiveCarVisuals = 0;
            ActivePedestrianVisuals = 0;
            ActiveLandingRiderMarkers = 0;
        }

        private static Transform MakeBox(Transform parent, string label, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = label; SetIgnoreRaycast(part); part.transform.SetParent(parent, false);
            part.transform.localPosition = position; part.transform.localScale = scale;
            Collider collider = part.GetComponent<Collider>(); if (collider) collider.enabled = false;
            Renderer renderer = part.GetComponent<Renderer>(); if (renderer && material) renderer.sharedMaterial = material;
            return part.transform;
        }

        private static Transform MakeCapsule(Transform parent, string label, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            part.name = label; SetIgnoreRaycast(part); part.transform.SetParent(parent, false);
            part.transform.localPosition = position; part.transform.localScale = scale;
            Collider collider = part.GetComponent<Collider>(); if (collider) collider.enabled = false;
            Renderer renderer = part.GetComponent<Renderer>(); if (renderer && material) renderer.sharedMaterial = material;
            return part.transform;
        }

        private static void SetIgnoreRaycast(GameObject item)
        {
            int layer = LayerMask.NameToLayer("Ignore Raycast");
            if (layer >= 0) item.layer = layer;
        }

        private static Material MaterialFor(ref Material target, Color color)
        {
            if (target) return target;
            Shader shader = Shader.Find("Standard");
            if (!shader) shader = Shader.Find("Sprites/Default");
            if (!shader) return null;
            target = new Material(shader) { color = color };
            return target;
        }

        private sealed class CarVisual
        {
            public GameObject root;
            public Transform driver;
            public int serial;
        }

        private sealed class PedestrianVisual
        {
            public GameObject root;
            public int serial;
        }

        private sealed class RiderMarker
        {
            public GameObject root;
            public int serial;
        }
    }
}
