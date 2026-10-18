using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>Installs physical building access in saved cities, without regenerating authored art.</summary>
    public sealed class SlopCityBuildingWorld : MonoBehaviour
    {
        public const float CoreHalfExtent = SlopCityDenseLayout.ColumnPitch + SlopCityDenseLayout.PavementHalfWidth;
        public const float OuterHalfExtent = 141f;
        private static readonly Rect CoreRect = SlopCityDenseLayout.CoreGroundRect;
        private static readonly Rect OuterRect = SlopCityDenseLayout.OuterGroundRect;
        public const int MaximumActiveTowerElevators = 2;
        public const int MaximumActiveElevators = 4; // Two streamed towers plus at most two nearby station mechanisms.
        public const int MaximumActiveFloors = 6;
        private readonly List<SlopCityBuildingInterior> buildings = new List<SlopCityBuildingInterior>();
        private readonly List<SlopCityElevator> stationLifts = new List<SlopCityElevator>();
        private SlopCityFirstPerson player;
        private float nextRefresh;
        private bool geometryChanged;
        private bool installed;
        private bool installationAttempted;
        private bool constrainedBrowser;
        private int detailAdmissionAfterFrame = -1;
        internal bool DeferTowerArchitecture => constrainedBrowser;
        internal int DetailedFloorBudget => constrainedBrowser ? 2 : 3;
        internal const float EntranceAdmissionDistance = 5f, EntranceRetentionDistance = 7f;
        private const float EntranceVerticalMargin = 3f;
        private int TowerBudget => constrainedBrowser ? 1 : MaximumActiveTowerElevators;
        private readonly List<Mesh> streetMeshes = new List<Mesh>();
        private Material streetAsphalt, streetPavement;
        private readonly Dictionary<SlopCityBuildingInterior, float> streamingRetry = new Dictionary<SlopCityBuildingInterior, float>();
        public IReadOnlyList<SlopCityBuildingInterior> Buildings => buildings;
        public int ActiveFloorCount => buildings.Where(item => item).Sum(item => item.LoadedFloorCount);
        public int ActiveElevatorCount => buildings.Count(item => item && item.Elevator && item.Elevator.gameObject.activeInHierarchy)
            + stationLifts.Count(item => item && item.gameObject.activeInHierarchy);
        public IReadOnlyList<SlopCityElevator> StationLifts => stationLifts;

        public static SlopCityBuildingWorld Ensure(SlopCityFirstPerson user)
        {
            GameObject city = GameObject.Find("SlopCity_NineBlocks");
            if (!city) return null;
            SlopCityBuildingWorld world = city.GetComponent<SlopCityBuildingWorld>();
            if (!world) world = city.AddComponent<SlopCityBuildingWorld>();
            world.player = user;
#if UNITY_WEBGL && !UNITY_EDITOR
            SlopCityPlayerInput input = user ? user.GetComponent<SlopCityPlayerInput>() : null;
            world.constrainedBrowser = world.constrainedBrowser || Application.isMobilePlatform || (input && input.UseTouchControls);
#endif
            if (!world.installationAttempted)
            {
                world.installationAttempted = true;
                try { world.Install(); world.installed = true; }
                catch (Exception error)
                {
                    world.enabled = false;
                    Debug.LogWarning("Building access could not initialize: " + error.Message, world);
                    if (user) user.ShowFeedback("Some building access is unavailable. The city remains open; reload to retry.");
                }
            }
            return world.installed ? world : null;
        }

        private void Install()
        {
            SlopCityInteriorGeometry.Initialize();
            SlopCityHumanScale.Prepare(transform);
            streetAsphalt = transform.Find("City_Asphalt")?.GetComponent<Renderer>()?.sharedMaterial;
            foreach (SlopCityDistrict district in GetComponentsInChildren<SlopCityDistrict>(true))
            {
                Transform original = district.transform.Find("Sidewalk") ?? district.transform.Find("Home_Sidewalk_S");
                if (original && original.GetComponent<Renderer>()) { streetPavement = original.GetComponent<Renderer>().sharedMaterial; break; }
            }
            if (!streetAsphalt || !streetPavement) throw new InvalidOperationException("The original street surface materials are missing.");
            SlopCityMetricFacade.Prepare(transform);
            bool citadelReady = true;
            try { SlopCityCitadelInterior.PrepareCitadel(transform); }
            catch (Exception error) { citadelReady = false; Debug.LogWarning("Citadel kept in its authored state: " + error.Message, this); }
            PrepareTowerLayout(transform);
            PrepareServiceBridges(transform);
            SlopCityMetroWorld.Prepare(transform);
            Transform[] all = transform.GetComponentsInChildren<Transform>(true);
            foreach (Transform tower in all.Where(item => item.name.StartsWith("City_Skyscraper_", StringComparison.Ordinal)
                || item.name.StartsWith("Skyline_Tower_", StringComparison.Ordinal)
                || item.name == "MilitarySF_CommunicationsCitadel").OrderBy(item => item.name, StringComparer.Ordinal))
            {
                if (!citadelReady && tower.name == "MilitarySF_CommunicationsCitadel") continue;
                SlopCityBuildingInterior interior = null;
                try
                {
                    interior = tower.gameObject.AddComponent<SlopCityBuildingInterior>();
                    interior.InitializeTower(this, player);
                    buildings.Add(interior);
                }
                catch (Exception error)
                {
                    if (interior) { interior.enabled = false; Destroy(interior); }
                    Debug.LogWarning("Building access unavailable for " + tower.name + ": " + error.Message, tower);
                }
            }
            foreach (Transform venue in all.Where(item => item.name.StartsWith("Venue_", StringComparison.Ordinal) && item.name != "Venue_Sign"))
            {
                SlopCityBuildingInterior interior = venue.gameObject.AddComponent<SlopCityBuildingInterior>();
                Transform district = venue.parent;
                string suffix = venue.name.Substring("Venue_".Length);
                Transform marker = district.Find("CityInterior_" + suffix);
                interior.InitializePreserved(this, suffix.Replace('_', ' '), marker ? marker.position : venue.position,
                    new Vector2(14, 9));
                buildings.Add(interior);
            }
            GameObject lounge = GameObject.Find("SlopCity_Lounge");
            if (lounge)
            {
                Transform floor = lounge.GetComponentsInChildren<Transform>().FirstOrDefault(item => item.name == "Floor_Main");
                Transform anchor = lounge.GetComponentsInChildren<Transform>().FirstOrDefault(item => item.name == "Spawn_Daniel_Stand" || item.name == "Player_Standing"
                    || item.name == "Spawn_Standing" || item.name == "Standing_Feet" || item.name == "Marker_Standing");
                Renderer floorRenderer = floor ? floor.GetComponent<Renderer>() : null;
                Bounds bounds = floorRenderer ? floorRenderer.bounds : new Bounds(lounge.transform.position, new Vector3(12, .1f, 12));
                // The authored player's stand marker is the authoritative circulation anchor.
                if (!anchor) anchor = lounge.GetComponentsInChildren<Transform>().FirstOrDefault(item => item.name.IndexOf("Standing", StringComparison.OrdinalIgnoreCase) >= 0);
                SlopCityBuildingInterior interior = lounge.AddComponent<SlopCityBuildingInterior>();
                interior.InitializePreserved(this, "Home apartment", anchor ? anchor.position : new Vector3(bounds.center.x, bounds.max.y + .02f, bounds.center.z),
                    new Vector2(bounds.size.x, bounds.size.z));
                buildings.Add(interior);
            }
            ExpandStreets();
            InstallStationLifts(all);
            SlopCityLuminousCity.Prepare(transform);
            GeometryChanged();
            RefreshStreaming();
            FlushChanges();
        }

        public void GeometryChanged() { geometryChanged = true; }

        internal bool PrepareWalkingSpace(Vector3 point)
        {
            if (!constrainedBrowser) return true;
            try
            {
                // Stand/arrival enables capsule collision before the next streaming
                // tick. Retire any authored solid core around that destination first.
                foreach (SlopCityBuildingInterior item in buildings)
                    if (item && item.IsTower && item.ContainsHorizontal(point, 1f)) item.EnsureArchitecture();
                FlushChanges();
                return true;
            }
            catch (Exception error)
            {
                Debug.LogWarning("Building arrival could not load: " + error.Message, this);
                if (player) player.ShowFeedback("This entrance could not load. Remain on the supported landing, or use HOME.");
                return false;
            }
        }

        private void OnEnable() { Application.lowMemory += HandleLowMemory; }
        private void OnDisable() { Application.lowMemory -= HandleLowMemory; }
        private void HandleLowMemory()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Trim on the normal streaming tick, never while another floor is being built.
            constrainedBrowser = true;
            nextRefresh = 0;
#endif
        }

        private void Update()
        {
            if (!installed || !player || player.IsPaused) return;
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + .35f;
                RefreshStreaming();
            }
            FlushChanges();
        }

        public void RefreshStreaming()
        {
            if (!player) return;
            buildings.RemoveAll(item => !item);
            Vector3 feet = player.transform.position;
            var candidates = new List<SlopCityBuildingInterior>();
            foreach (SlopCityBuildingInterior item in buildings)
                if (ShouldConsiderTowerDetail(item, feet)) candidates.Add(item);
            candidates.Sort((a, b) =>
            {
                bool occupiedA = a.ContainsInterior(feet), occupiedB = b.ContainsInterior(feet);
                if (occupiedA != occupiedB) return occupiedA ? -1 : 1;
                bool pinA = a.Elevator && a.Elevator.PlayerInside, pinB = b.Elevator && b.Elevator.PlayerInside;
                if (pinA != pinB) return pinA ? -1 : 1;
                float distanceA = a.HorizontalDistanceSquared(feet), distanceB = b.HorizontalDistanceSquared(feet);
                if (a.Elevator) distanceA = Mathf.Max(0f, distanceA - 4f);
                if (b.Elevator) distanceB = Mathf.Max(0f, distanceB - 4f);
                return distanceA.CompareTo(distanceB);
            });
            var keep = new HashSet<SlopCityBuildingInterior>(candidates.Take(TowerBudget));
            // Retire distant empty cars before admitting a new occupied building. Never drop the room under a player.
            bool retiredDetail = false;
            foreach (SlopCityBuildingInterior item in buildings)
            {
                if (!item.IsTower || keep.Contains(item)) continue;
                bool hadDetail = item.Elevator || item.LoadedFloorCount != 0;
                if (item.ReleaseStream() && hadDetail) retiredDetail = true;
            }
            // Destroy retires native resources at frame end. Do not construct the next
            // tower's car/rooms while the previous ones still occupy that same frame.
            if (constrainedBrowser && retiredDetail && Application.isPlaying)
                detailAdmissionAfterFrame = Time.frameCount + 1;
            foreach (SlopCityBuildingInterior item in buildings)
            {
                if (!item.IsTower) continue;
                if (keep.Contains(item))
                {
                    if (!CanAdmitTowerDetail(item, Time.frameCount))
                    { nextRefresh = 0f; continue; }
                    if (streamingRetry.TryGetValue(item, out float retryAt) && Time.unscaledTime < retryAt) continue;
                    try
                    {
                    if (!item.Elevator && buildings.Count(other => other.IsTower && other.Elevator) >= TowerBudget)
                    { item.StreamAround(feet, DetailedFloorBudget); continue; }
                    item.EnsureElevator();
                    item.StreamAround(feet, DetailedFloorBudget);
                    streamingRetry.Remove(item);
                    }
                    catch (Exception error)
                    {
                        streamingRetry[item] = Time.unscaledTime + 10f;
                        Debug.LogWarning("Building detail could not load: " + item.name + ": " + error.Message, item);
                        player.ShowFeedback("This floor could not fully load. Keep to its supported landing, or use HOME in the pause menu.");
                    }
                }
            }
            // Stations are far apart. Their physical mechanisms are active only near the station/rider.
            foreach (SlopCityElevator lift in stationLifts)
            {
                if (!lift) continue;
                bool near = StationNearby(lift, feet);
                if (near) lift.gameObject.SetActive(true);
                else if (lift.CanUnload && lift.Release()) lift.gameObject.SetActive(false);
            }
        }

        internal bool CanAdmitTowerDetail(SlopCityBuildingInterior item, int frame)
            => !constrainedBrowser || (item && item.Elevator) || frame >= detailAdmissionAfterFrame;

        internal bool ShouldConsiderTowerDetail(SlopCityBuildingInterior item, Vector3 feet)
        {
            if (!item || !item.IsTower) return false;
            SlopCityElevator lift = item.Elevator;
            if (!constrainedBrowser)
                return item.ContainsHorizontal(feet, lift ? 9f : 7f) || (lift && !lift.CanUnload);
            // Occupied rooms/roofs and a pinned or busy car always keep support.
            // Ground approach is measured from the physical doorway rather than
            // every side of the tower's large footprint: ordinary street walking
            // beside its back wall must not allocate a car and furnished rooms.
            if (item.ContainsInterior(feet) || (lift && (lift.PlayerInside || !lift.CanUnload))) return true;
            if (player && player.IsSeated) return false;
            Vector3 delta = feet - item.EntranceFeet;
            if (Mathf.Abs(delta.y) > EntranceVerticalMargin) return false;
            float radius = lift ? EntranceRetentionDistance : EntranceAdmissionDistance;
            return delta.x * delta.x + delta.z * delta.z <= radius * radius;
        }

        /// <summary>Original decorative skyline plots intersected at corners. Stable real plots leave clear streets.</summary>
        public static int PrepareTowerLayout(Transform city)
        {
            if (!city) return 0;
            int moved = 0;
            Transform armor = city.Find("City_MilitarySciFiArchitecture");
            foreach (Transform tower in city.GetComponentsInChildren<Transform>(true))
            {
                if (!tower.name.StartsWith("Skyline_Tower_", StringComparison.Ordinal)
                    || !int.TryParse(tower.name.Substring("Skyline_Tower_".Length), out int serial)) continue;
                int ordinal = serial - 23;
                if (ordinal < 0 || ordinal >= 28) continue;
                Vector3 desired = SlopCityDenseLayout.SkylinePoint(ordinal);
                desired = city.TransformPoint(desired);
                if ((tower.position - desired).sqrMagnitude > .00001f) { tower.position = desired; moved++; }
                Transform dressing = armor ? armor.Find("MilitarySF_HorizonTower_" + ordinal.ToString("D2")) : null;
                if (dressing) dressing.position = desired;
            }
            return moved;
        }

        /// <summary>Keep the existing enclosed service links outside occupied rooms; preserve their exterior detail.</summary>
        public static int PrepareServiceBridges(Transform city)
        {
            if (!city) return 0;
            Transform[] towers = city.GetComponentsInChildren<Transform>(true).Where(item => item.name.StartsWith("City_Skyscraper_", StringComparison.Ordinal)).ToArray();
            int changed = 0;
            foreach (Transform bridge in city.GetComponentsInChildren<Transform>(true).Where(item => item.name.StartsWith("Megacity_Elevated_ServiceLink_", StringComparison.Ordinal)).ToArray())
            {
                if (bridge.Find("Runtime_Exterior_Clip_Metadata")) continue;
                Transform deck = bridge.Find("Bridge_Steel_Deck");
                MeshFilter filter = deck ? deck.GetComponent<MeshFilter>() : null;
                if (!filter || !filter.sharedMesh) continue;
                float length = filter.sharedMesh.bounds.size.z;
                var blocked = new List<Vector2>();
                foreach (Transform tower in towers)
                {
                    Transform core = tower.Find("Structural_Core");
                    BoxCollider body = core ? core.GetComponent<BoxCollider>() : null;
                    if (!body) continue;
                    Vector3 center = bridge.InverseTransformPoint(tower.position), size = body.size;
                    float hx = (Mathf.Abs(Vector3.Dot(bridge.right, tower.right)) * size.x + Mathf.Abs(Vector3.Dot(bridge.right, tower.forward)) * size.z) / 2;
                    float hz = (Mathf.Abs(Vector3.Dot(bridge.forward, tower.right)) * size.x + Mathf.Abs(Vector3.Dot(bridge.forward, tower.forward)) * size.z) / 2;
                    if (Mathf.Abs(center.x) > hx + 1.5f) continue;
                    float low = Mathf.Max(-length / 2, center.z - hz - .25f), high = Mathf.Min(length / 2, center.z + hz + .25f);
                    if (high > low) blocked.Add(new Vector2(low, high));
                }
                blocked.Sort((a, b) => a.x.CompareTo(b.x));
                var clear = new List<Vector2>(); float cursor = -length / 2;
                foreach (Vector2 interval in blocked)
                {
                    if (interval.x > cursor + .5f) clear.Add(new Vector2(cursor, interval.x));
                    cursor = Mathf.Max(cursor, interval.y);
                }
                if (cursor < length / 2 - .5f) clear.Add(new Vector2(cursor, length / 2));
                if (clear.Count == 0) { bridge.gameObject.SetActive(false); SlopCityInteriorGeometry.Root(bridge, "Runtime_Exterior_Clip_Metadata"); continue; }
                // A long source link can cross an intermediate building as well as its endpoints.
                // Preserve only the exterior intervals, including all original side/rib/glazing details.
                Vector3 originalPosition = bridge.position, originalScale = bridge.localScale, direction = bridge.forward;
                var segments = new List<Transform> { bridge };
                for (int i = 1; i < clear.Count; i++)
                {
                    Transform copy = UnityEngine.Object.Instantiate(bridge.gameObject, bridge.parent).transform;
                    copy.name = "Runtime_ExteriorBridge_Segment_" + bridge.name + "_" + i;
                    segments.Add(copy);
                }
                for (int i = 0; i < clear.Count; i++)
                {
                    Transform segment = segments[i]; Vector2 interval = clear[i];
                    segment.position = originalPosition + direction * ((interval.x + interval.y) / 2);
                    Vector3 scale = originalScale; scale.z *= (interval.y - interval.x) / length; segment.localScale = scale;
                    SlopCityInteriorGeometry.Root(segment, "Runtime_Exterior_Clip_Metadata");
                }
                changed++;
            }
            return changed;
        }

        private static bool StationNearby(SlopCityElevator lift, Vector3 feet)
        {
            Vector3 delta = feet - lift.transform.position; delta.y = 0;
            return delta.sqrMagnitude < 15f * 15f;
        }

        private void FlushChanges()
        {
            if (!geometryChanged) return;
            geometryChanged = false;
            Physics.SyncTransforms();
            if (player) player.RefreshWorldInteractions();
        }

        private void ExpandStreets()
        {
            Transform root = SlopCityInteriorGeometry.Root(transform, "Runtime_Outer_Streets");
            // Narrow outer streets finish the closely packed blocks. Preserve the home's
            // and stations' holes inside the authored grid; no kilometre-wide apron remains.
            foreach (Rect strip in SlopCityInteriorGeometry.Subtract(OuterRect, CoreRect))
                StreetBox(root, "Neighbourhood_Outer_Asphalt", new Vector3(strip.center.x, -.2f, strip.center.y),
                    new Vector3(strip.width, .2f, strip.height), streetAsphalt);
            foreach (float z in new[] { OuterRect.yMin + 3, CoreRect.yMin - 1.5f, CoreRect.yMax + 1.5f, OuterRect.yMax - 3 })
                StreetBox(root, "Ring_Footway_EW", new Vector3(OuterRect.center.x, -.08f, z), new Vector3(OuterRect.width - 6, .04f, 3), streetPavement);
            foreach (float x in new[] { OuterRect.xMin + 3, CoreRect.xMin - 1.5f, CoreRect.xMax + 1.5f, OuterRect.xMax - 3 })
                StreetBox(root, "Ring_Footway_NS", new Vector3(x, -.08f, OuterRect.center.y), new Vector3(3, .04f, OuterRect.height - 6), streetPavement);
            Transform boundary = transform.Find("City_ScenicBoundary");
            if (boundary)
                foreach (Transform item in boundary.GetComponentsInChildren<Transform>(true))
                    if (item.name == "Retaining_Wall" || item.name == "Boundary_Post" || item.name == "Boundary_LightStrip") item.gameObject.SetActive(false);
            foreach (SlopCityBuildingInterior tower in buildings.Where(item => item.IsTower && (item.BuildingId.StartsWith("Skyline_", StringComparison.Ordinal)
                || item.BuildingId == "MilitarySF_CommunicationsCitadel")))
            {
                Vector2 size = tower.GetFloorSize(0);
                StreetBox(tower.transform, "Outer_Entrance_Walk", new Vector3(0, -.02f, -size.y * .5f - 3),
                    new Vector3(3.4f, .16f, 6.4f), streetPavement);
                Vector3 entrance = transform.InverseTransformPoint(tower.EntranceFeet);
                Vector3 junction = Mathf.Abs(entrance.x) > Mathf.Abs(entrance.z)
                    ? new Vector3(entrance.x < 0 ? CoreRect.xMin - 1.5f : CoreRect.xMax + 1.5f, -.07f, entrance.z)
                    : new Vector3(entrance.x, -.07f, entrance.z < CoreRect.center.y ? CoreRect.yMin - 1.5f : CoreRect.yMax + 1.5f);
                Vector3 delta = junction - entrance; delta.y = 0;
                Transform approach = SlopCityInteriorGeometry.Root(root, "Outer_Tower_Approach_" + tower.BuildingId, (entrance + junction) * .5f);
                approach.localPosition = new Vector3(approach.localPosition.x, -.075f, approach.localPosition.z);
                if (delta.sqrMagnitude > .01f) approach.localRotation = Quaternion.LookRotation(delta);
                StreetBox(approach, "Paved_Access", Vector3.zero, new Vector3(3.4f, .04f, Mathf.Max(1, delta.magnitude)), streetPavement);
            }
            foreach (float x in new[] { OuterRect.xMin, OuterRect.xMax })
                SlopCityInteriorGeometry.Box(root, "Outer_Perimeter_X", new Vector3(x, .55f, OuterRect.center.y), new Vector3(.3f, 1.3f, OuterRect.height), SlopCityInteriorGeometry.Wall, true);
            foreach (float z in new[] { OuterRect.yMin, OuterRect.yMax })
                SlopCityInteriorGeometry.Box(root, "Outer_Perimeter_Z", new Vector3(OuterRect.center.x, .55f, z), new Vector3(OuterRect.width, 1.3f, .3f), SlopCityInteriorGeometry.Wall, true);
        }

        private void InstallStationLifts(Transform[] all)
        {
            Transform[] stations = all.Where(item => item.name.StartsWith("Subway_Station_", StringComparison.Ordinal)).ToArray();
            Transform asphalt = transform.Find("City_Asphalt");
            if (asphalt && stations.Length > 0)
            {
                SlopCityInteriorGeometry.DisableSolid(asphalt);
                Transform patches = SlopCityInteriorGeometry.Root(transform, "Runtime_City_Asphalt_With_Lift_Openings");
                var regions = new List<Rect> { CoreRect };
                foreach (Transform station in stations)
                {
                    Vector3 center = transform.InverseTransformPoint(station.TransformPoint(SlopCityMetroWorld.LiftOffset));
                    Rect hole = new Rect(center.x - 1.18f, center.z - 1.18f, 2.36f, 2.36f);
                    regions = regions.SelectMany(region => SlopCityInteriorGeometry.Subtract(region, hole)).ToList();
                }
                foreach (Rect region in regions)
                    StreetBox(patches, "Asphalt_Shaft_Cutout", new Vector3(region.center.x, -.25f, region.center.y),
                        new Vector3(region.width, .3f, region.height), streetAsphalt);
            }
            foreach (Transform station in stations)
            {
                Transform entry = station.Find("Subway_SurfaceLift");
                if (entry)
                    foreach (Transform piece in entry.GetComponentsInChildren<Transform>())
                        if (piece.name == "Lift_Pad" || piece.name == "Lift_Back" || piece.name == "Lift_Side" || piece.name == "Elevator_Steel_Jamb")
                            SlopCityInteriorGeometry.DisableSolid(piece);
                Transform ceiling = station.Find("Station_Ceiling");
                if (ceiling)
                {
                    SlopCityInteriorGeometry.DisableSolid(ceiling);
                    SlopCityInteriorGeometry.Slab(station, "Station_Ceiling_With_Shaft", -4.25f, new Vector2(15, 11), new Vector2(SlopCityMetroWorld.LiftOffset.x, SlopCityMetroWorld.LiftOffset.z), SlopCityInteriorGeometry.Wall, .3f);
                }
                Transform landing = SlopCityInteriorGeometry.Root(station, "Runtime_Metro_Landings");
                landing.localPosition = SlopCityMetroWorld.LiftOffset;
                SlopCityInteriorGeometry.Slab(landing, "Street_Landing_Pad", .075f, new Vector2(3.1f, 3.2f), Vector2.zero, SlopCityInteriorGeometry.Floor, .1f);
                SlopCityElevator lift = SlopCityElevator.Create(station, SlopCityMetroWorld.LiftOffset, new[] { -7.86f, .075f }, new[] { "PLATFORM", "STREET" }, player, _ => GeometryChanged());
                stationLifts.Add(lift);
                Transform surfaceControl = entry ? entry.Find("Platform_Lift_Control") : null;
                Transform platformControl = station.Find("Street_Lift_Control");
                BindStationControl(surfaceControl, lift, 1, 0, station.TransformPoint(SlopCityMetroWorld.LiftOffset + new Vector3(-.82f, 1.1f, -1.28f)));
                BindStationControl(platformControl, lift, 0, 1, station.TransformPoint(SlopCityMetroWorld.LiftOffset + new Vector3(-.82f, -6.76f, -1.28f)));
            }
        }

        private void BindStationControl(Transform control, SlopCityElevator lift, int landing, int destination, Vector3 position)
        {
            if (!control) return;
            SlopCityInteractable item = control.GetComponent<SlopCityInteractable>();
            if (!item) return;
            control.position = position;
            control.rotation = control.parent.rotation * Quaternion.Euler(0, 180, 0);
            SlopCityElevatorButton button = control.GetComponent<SlopCityElevatorButton>() ?? control.gameObject.AddComponent<SlopCityElevatorButton>();
            button.Configure(lift, SlopCityElevatorButtonKind.Call, landing);
            button.BindRoute(item, destination);
            item.Configure(SlopCityInteractionKind.Subway, item.DisplayName, false);
            item.ConfigureInteractionPoint(control.TransformPoint(new Vector3(0, 0, .13f)));
        }

        private void StreetBox(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            GameObject box = SlopCityInteriorGeometry.Box(parent, name, position, size, material, true);
            streetMeshes.Add(SlopCityInteriorGeometry.ApplyMetricSurface(box));
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in streetMeshes)
                if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            streetMeshes.Clear();
        }
    }

    internal static class SlopCityInteriorGeometry
    {
        internal static Mesh UnitCube => cube;
        internal static Mesh UnitCylinder => cylinder ? cylinder : (cylinder = BorrowPrimitive(PrimitiveType.Cylinder));
        internal static Mesh UnitSphere => sphere ? sphere : (sphere = BorrowPrimitive(PrimitiveType.Sphere));
        private static Mesh cube, cylinder, sphere;
        private static Mesh BorrowPrimitive(PrimitiveType kind)
        {
            GameObject primitive = GameObject.CreatePrimitive(kind);
            try { return primitive.GetComponent<MeshFilter>().sharedMesh; }
            finally { Dispose(primitive); }
        }
        internal static Material Floor, Wall, Wood, Metal, Fabric, Accent, Glass, Asphalt, Text;
        internal static void Initialize()
        {
            if (cube && Floor && Wall && Wood && Metal && Fabric && Accent && Asphalt && Glass && Text) return;
            Shader standard = Shader.Find("Standard");
            Shader textShader = Shader.Find("SlopCity/WorldText") ?? Shader.Find("GUI/Text Shader");
            if (!standard || !standard.isSupported || !textShader || !textShader.isSupported)
                throw new InvalidOperationException("Building materials are unavailable on this graphics device.");
            if (!cube)
            {
                GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
                try { cube = primitive.GetComponent<MeshFilter>().sharedMesh; }
                finally { Dispose(primitive); }
            }
            Material[] previous = { Floor, Wall, Wood, Metal, Fabric, Accent, Asphalt, Glass, Text };
            Material[] next = (Material[])previous.Clone();
            string[] names = { "Interior stone", "Interior plaster", "Interior timber", "Interior steel", "Interior upholstery", "Interior civic enamel", "Outer district asphalt" };
            Color[] colors = { new Color(.34f, .37f, .34f), new Color(.57f, .62f, .57f), new Color(.32f, .20f, .13f),
                new Color(.12f, .19f, .18f), new Color(.22f, .34f, .31f), new Color(.64f, .79f, .58f), new Color(.11f, .13f, .13f) };
            try
            {
                for (int index = 0; index < names.Length; index++)
                {
                    if (next[index]) continue;
                    Material material = next[index] = new Material(standard);
                    material.name = names[index]; material.color = colors[index]; material.enableInstancing = true;
                    if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .2f);
                    if (material.HasProperty("_EmissionColor")) { material.SetColor("_EmissionColor", colors[index] * .28f); material.EnableKeyword("_EMISSION"); }
                }
                if (!next[7])
                {
                    Material glass = next[7] = new Material(standard);
                    glass.name = "Interior glazing"; glass.color = new Color(.15f, .25f, .24f, .09f); glass.enableInstancing = true;
                    glass.SetFloat("_Mode", 3); glass.SetInt("_SrcBlend", (int)BlendMode.One); glass.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    glass.SetInt("_ZWrite", 0); glass.EnableKeyword("_ALPHAPREMULTIPLY_ON"); glass.renderQueue = 3000;
                }
                if (!next[8])
                {
                    Material text = next[8] = new Material(textShader); text.name = "Interior floor signs";
                    Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (font && font.material) text.mainTexture = font.material.mainTexture;
                }
                Floor = next[0]; Wall = next[1]; Wood = next[2]; Metal = next[3]; Fabric = next[4];
                Accent = next[5]; Asphalt = next[6]; Glass = next[7]; Text = next[8];
            }
            catch
            {
                for (int index = 0; index < next.Length; index++)
                    if (next[index] && next[index] != previous[index])
                    { if (Application.isPlaying) UnityEngine.Object.Destroy(next[index]); else UnityEngine.Object.DestroyImmediate(next[index]); }
                throw;
            }
        }
        internal static Transform Root(Transform parent, string name, Vector3 position = default(Vector3))
        {
            Transform root = new GameObject(name).transform; root.SetParent(parent, false); root.localPosition = position; return root;
        }
        internal static GameObject Box(Transform parent, string name, Vector3 point, Vector3 size, Material material, bool solid = false)
        {
            GameObject box = new GameObject(name); box.transform.SetParent(parent, false); box.transform.localPosition = point; box.transform.localScale = size;
            box.AddComponent<MeshFilter>().sharedMesh = cube;
            Renderer renderer = box.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            if (solid) box.AddComponent<BoxCollider>();
            box.isStatic = true;
            return box;
        }
        // Exterior scans are authored in metres. A single 0..1 cube UV stretches a
        // paving tile across a whole block and discards its photographed grain.
        internal static Mesh ApplyMetricSurface(GameObject box)
        {
            MeshFilter filter = box.GetComponent<MeshFilter>();
            Mesh original = filter.sharedMesh;
            Mesh mesh = UnityEngine.Object.Instantiate(original);
            try
            {
                mesh.name = box.name + "_WorldMetreUV";
                MapMetricUV(mesh, box.transform);
                Renderer renderer = box.GetComponent<Renderer>(); renderer.receiveShadows = true;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                filter.sharedMesh = mesh;
                return mesh;
            }
            catch
            {
                // The caller cannot record ownership until this method returns.
                // Keep the original bound and retire a failed native clone here.
                if (Application.isPlaying) UnityEngine.Object.Destroy(mesh); else UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
        }
        internal static void MapMetricUV(Mesh mesh, Transform owner)
        {
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 position = owner.TransformPoint(vertices[i]);
                Vector3 normal = owner.TransformDirection(normals[i]);
                uv[i] = Mathf.Abs(normal.y) > .5f ? new Vector2(position.x, position.z)
                    : Mathf.Abs(normal.x) > .5f ? new Vector2(position.z * -Mathf.Sign(normal.x), position.y)
                    : new Vector2(position.x * Mathf.Sign(normal.z), position.y);
            }
            mesh.uv = uv; mesh.RecalculateTangents();
        }
        internal static void DisableSolid(Transform objectRoot)
        {
            foreach (Renderer renderer in objectRoot.GetComponents<Renderer>()) renderer.enabled = false;
            foreach (Collider collider in objectRoot.GetComponents<Collider>()) collider.enabled = false;
        }
        internal static void Slab(Transform parent, string name, float top, Vector2 size, Vector2 shaft, Material material, float thickness = .16f)
        {
            Rect envelope = new Rect(-size.x / 2, -size.y / 2, size.x, size.y);
            Rect hole = new Rect(shaft.x - 1.18f, shaft.y - 1.18f, 2.36f, 2.36f);
            foreach (Rect region in Subtract(envelope, hole))
                Box(parent, name, new Vector3(region.center.x, top - thickness / 2, region.center.y),
                    new Vector3(region.width, thickness, region.height), material, true);
        }
        internal static IEnumerable<Rect> Subtract(Rect envelope, Rect hole)
        {
            float xmin = Mathf.Max(envelope.xMin, hole.xMin), xmax = Mathf.Min(envelope.xMax, hole.xMax);
            float zmin = Mathf.Max(envelope.yMin, hole.yMin), zmax = Mathf.Min(envelope.yMax, hole.yMax);
            if (xmin >= xmax || zmin >= zmax) { yield return envelope; yield break; }
            if (xmin > envelope.xMin) yield return Rect.MinMaxRect(envelope.xMin, envelope.yMin, xmin, envelope.yMax);
            if (xmax < envelope.xMax) yield return Rect.MinMaxRect(xmax, envelope.yMin, envelope.xMax, envelope.yMax);
            if (zmin > envelope.yMin) yield return Rect.MinMaxRect(xmin, envelope.yMin, xmax, zmin);
            if (zmax < envelope.yMax) yield return Rect.MinMaxRect(xmin, zmax, xmax, envelope.yMax);
        }
        internal static void Sign(Transform parent, string content, Vector3 point, float height, Quaternion? rotation = null)
        {
            Transform root = Root(parent, "Floor_Purpose_Sign", point);
            root.localRotation = rotation ?? Quaternion.identity;
            TextMesh mesh = root.gameObject.AddComponent<TextMesh>();
            mesh.text = content; mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); mesh.fontSize = 48;
            mesh.characterSize = height * 10f / 48f; mesh.color = new Color(.9f, .95f, .85f); mesh.anchor = TextAnchor.MiddleCenter; mesh.alignment = TextAlignment.Center;
            root.gameObject.AddComponent<SlopCityWorldText>().Configure(Text);
            root.gameObject.isStatic = true;
        }
        internal static void Dispose(GameObject item)
        {
            item.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(item); else UnityEngine.Object.DestroyImmediate(item);
        }
    }
}
