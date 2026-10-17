using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopCity
{
    /// <summary>Metric plot retrofit for the saved city. Run before batching and building access.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityHumanScale : MonoBehaviour
    {
        public const float PlotFactor = 3f;
        // Geometry size is deliberately independent of the close street layout.
        [SerializeField] private bool prepared;
        [SerializeField] private int widenedTowers;
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        public bool Prepared => prepared;
        public int WidenedTowerCount => widenedTowers;

        private sealed class Tower
        {
            public Transform root;
            public Vector3 originalPosition, dimensions;
            public float widthFactor = PlotFactor;
            public Matrix4x4 toLocal;
        }

        public static void Prepare(Transform city)
        {
            if (!city) return;
            SlopCityHumanScale owner = city.GetComponent<SlopCityHumanScale>();
            if (owner && owner.prepared) return;
            if (!owner) owner = city.gameObject.AddComponent<SlopCityHumanScale>();
            owner.Apply(city);
        }

        public static Vector3 PlotPoint(Vector3 point)
            => new Vector3(point.x * PlotFactor, point.y, point.z * PlotFactor);

        private void Apply(Transform city)
        {
            Transform[] all = city.GetComponentsInChildren<Transform>(true);
            SlopCityDistrict[] districts = city.GetComponentsInChildren<SlopCityDistrict>(true);
            if (districts.Length != 9) throw new InvalidOperationException("Metric city layout requires the nine saved districts.");
            var oldPositions = all.ToDictionary(item => item, item => item.position);
            var districtOffsets = new Dictionary<int, Vector3>();
            var originalDistrictCenters = new Dictionary<int, Vector3>();
            var districtTransforms = new Dictionary<int, Transform>();
            var stationOffsets = new Dictionary<int, Vector3>();
            var towers = new List<Tower>();
            foreach (Transform item in all)
            {
                if (!item.name.StartsWith("City_Skyscraper_", StringComparison.Ordinal)) continue;
                Transform core = item.Find("Structural_Core");
                MeshFilter filter = core ? core.GetComponent<MeshFilter>() : null;
                if (!filter || !filter.sharedMesh) throw new InvalidOperationException("Metric tower core missing: " + item.name);
                if ((item.lossyScale - Vector3.one).sqrMagnitude > .0001f)
                    throw new InvalidOperationException("Metric towers require unit ancestor scale: " + item.name);
                towers.Add(new Tower { root = item, originalPosition = item.position, dimensions = filter.sharedMesh.bounds.size,
                    toLocal = item.worldToLocalMatrix });
            }
            // A saved player's batched mesh cannot be safely rewritten. Build and Editor Play
            // preparation run this pass before batching; exported scenes retain this marker.
            foreach (Tower tower in towers)
                foreach (Transform child in Children(tower.root))
                    if (!MetricTowerHardware(child.name) && !child.GetComponent<TextMesh>())
                        foreach (MeshFilter filter in child.GetComponentsInChildren<MeshFilter>(true)) RequireReadable(filter);
            var undo = new List<Action>();
            int originalMeshCount = ownedMeshes.Count;
            var expandedMeshes = new Dictionary<Mesh, Mesh>();
            try
            {
                foreach (SlopCityDistrict district in districts)
                {
                    Vector3 oldCenter = oldPositions[district.transform];
                    originalDistrictCenters[district.Index] = oldCenter;
                    districtTransforms[district.Index] = district.transform;
                    Vector3 newCenter = city.TransformPoint(SlopCityDenseLayout.DistrictPoint(city.InverseTransformPoint(oldCenter)));
                    Vector3 delta = newCenter - oldCenter;
                    districtOffsets[district.Index] = delta;
                    int column = district.Column, row = district.Row;
                    string label = district.DisplayName;
                    Vector3 visit = district.VisitPoint;
                    undo.Add(() => district.Configure(column, row, label, visit));
                    Move(district.transform, newCenter, undo);
                    district.Configure(column, row, label, visit + delta);
                    foreach (Transform child in Children(district.transform))
                    {
                        if (child.name == "Sidewalk" || child.name.StartsWith("Curb_", StringComparison.Ordinal)
                            || child.name == "Sidewalk_Joint" || child.name.StartsWith("Home_Sidewalk_", StringComparison.Ordinal))
                            SetActive(child.gameObject, false, undo);
                    }
                    var fixturePositions = Children(district.transform).ToDictionary(item => item, item => item.localPosition);
                    var fixtureScales = Children(district.transform).ToDictionary(item => item, item => item.localScale);
                    undo.Add(() => { foreach (var item in fixturePositions) if (item.Key) item.Key.localPosition = item.Value;
                        foreach (var item in fixtureScales) if (item.Key) item.Key.localScale = item.Value; });
                    SlopCityDenseLayout.ApplyGroundFixtures(district.transform);
                    if (district.Index != 2 && district.Index != 4 && district.Index != 5)
                        foreach (Transform notice in district.GetComponentsInChildren<Transform>(true))
                        {
                            if (notice.name != "Pasted_Public_Notice") continue;
                            Vector3 point = notice.localPosition; point.x = -Mathf.Abs(point.x);
                            MoveLocal(notice, point, undo);
                            Quaternion rotation = notice.localRotation;
                            undo.Add(() => { if (notice) notice.localRotation = rotation; });
                            // Venue roots face 180 degrees: local west is the retained
                            // right-hand service alley, opposite the new occupied party wall.
                            notice.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
                        }
                    Transform visitAnchor = district.transform.Find("CityVisitPoint_" + district.Index);
                    if (visitAnchor) district.Configure(column, row, label, visitAnchor.position);
                    Transform atmosphere = district.transform.Find("Megacity_StreetAtmosphere");
                    if (atmosphere)
                        foreach (Transform light in Children(atmosphere))
                        { Vector3 point = light.localPosition; point.x = Mathf.Sign(point.x) * 7.8f; MoveLocal(light, point, undo); }
                    RebuildPavement(district, undo);
                }
                foreach (Tower tower in towers)
                {
                    Transform parent = tower.root.parent;
                    Vector3 originalLocal = parent.InverseTransformPoint(oldPositions[tower.root]);
                    // The parent has already moved; use the original district origin instead.
                    SlopCityDistrict district = tower.root.GetComponentInParent<SlopCityDistrict>();
                    if (district) originalLocal = parent.InverseTransformVector(oldPositions[tower.root] - oldPositions[parent]);
                    Vector3 targetSize = SlopCityDenseLayout.TowerSize(district.Index, originalLocal, tower.dimensions);
                    tower.widthFactor = targetSize.x / tower.dimensions.x;
                    Move(tower.root, parent.TransformPoint(SlopCityDenseLayout.TowerPoint(district.Index, originalLocal, targetSize)), undo);
                    WidenTower(tower, undo, expandedMeshes);
                }
                PrepareJapan(city, oldPositions, towers, districtOffsets, undo);
                PrepareMilitary(city, oldPositions, towers, undo, expandedMeshes);
                foreach (Transform station in all.Where(item => item.name.StartsWith("Subway_Station_", StringComparison.Ordinal)))
                {
                    Vector3 original = oldPositions[station];
                    Vector3 next = city.TransformPoint(SlopCityDenseLayout.StreetPoint(city.InverseTransformPoint(original)));
                    Move(station, next, undo);
                    if (int.TryParse(station.name.Substring("Subway_Station_".Length), out int index)) stationOffsets[10 + index] = next - original;
                }
                foreach (Transform root in Children(city))
                {
                    if (root.name == "CityPath_HomeSidewalk")
                        // The featured portrait remains directly opposite the unchanged
                        // loft door, mounted in front of the moved occupied tower.
                        MoveLocal(root, new Vector3(SlopCityDenseLayout.HomeFeaturedMount,
                            root.localPosition.y, root.localPosition.z), undo);
                    else if (root.name == "City_Asphalt")
                    {
                        ScaleField(root, new Vector3(SlopCityDenseLayout.CoreGroundRect.width / 122f, 1, SlopCityDenseLayout.CoreGroundRect.height / 122f), undo, expandedMeshes);
                        MoveLocal(root, new Vector3(SlopCityDenseLayout.CoreGroundRect.center.x, root.localPosition.y, SlopCityDenseLayout.CoreGroundRect.center.y), undo);
                    }
                    else if (root.name.StartsWith("Megacity_Elevated_ServiceLink_", StringComparison.Ordinal))
                        ReplotBridge(root, towers, oldPositions, undo, expandedMeshes);
                    else if (root.name == "City_StreetNetwork") PrepareRoads(root, undo);
                    else if (root.name == "City_ScenicBoundary") PrepareBoundary(root, undo, expandedMeshes);
                    else if (root.name == "City_StreetDetails") PrepareStreetDetails(root, oldPositions, undo);
                    else if (root.name == "Megacity_Rainwater")
                        foreach (Transform patch in Children(root)) Move(patch, city.TransformPoint(SlopCityDenseLayout.StreetPoint(city.InverseTransformPoint(oldPositions[patch]))), undo);
                    else if (root.name.StartsWith("CityPath_", StringComparison.Ordinal)
                        && root.name != "CityPath_HomeGate" && root.name != "CityPath_HomeSidewalk")
                        Move(root, city.TransformPoint(SlopCityDenseLayout.StreetPoint(city.InverseTransformPoint(oldPositions[root]))), undo);
                }
                AlignReflectionProbes(city, undo);
                // All surrounding shells must occupy their final plots before testing pedestrians.
                foreach (Transform item in all)
                    if (item.name.StartsWith("Skyline_Tower_", StringComparison.Ordinal)
                        || item.name.StartsWith("MilitarySF_HorizonTower_", StringComparison.Ordinal)
                        || item.name == "MilitarySF_CommunicationsCitadel")
                    {
                        Vector3 previous = item.position; undo.Add(() => { if (item) item.position = previous; });
                        if (item.name == "MilitarySF_CommunicationsCitadel") item.position = city.TransformPoint(SlopCityDenseLayout.CitadelPoint);
                    }
                SlopCityBuildingWorld.PrepareTowerLayout(city);
                // Recheck translated pedestrian routes against the enlarged, closely packed shells.
                Physics.SyncTransforms();
                SlopCityCrowd crowd = city.GetComponentInChildren<SlopCityCrowd>(true);
                if (crowd) crowd.RemapLayout((index, point) =>
                {
                    if (districtTransforms.TryGetValue(index, out Transform district))
                    {
                        // Saved .9m public pavement graphs actually span -14.3..13.6,
                        // rather than the unattained +/-15 block endpoints. Replot their
                        // source nodes once onto the new street edges; people and all
                        // subway fittings keep their ordinary metric dimensions.
                        Vector3 local = district.InverseTransformVector(point - originalDistrictCenters[index]);
                        float x = (local.x + 14.3f) / 27.9f, z = (local.z + 14.3f) / 27.9f;
                        // The physical corner posts extend .11m past the wall. A .32m
                        // route-validation capsule needs more than the footway midpoint's
                        // .425m offset: retain .07m clearance beyond the post and capsule.
                        // .50m stays on both narrow footways and leaves all people full size.
                        const float facadeRouteOffset = .50f;
                        float sideRoute = SlopCityDenseLayout.SideBuildToLine + facadeRouteOffset;
                        Vector3 replotted = new Vector3(Mathf.Lerp(-sideRoute, sideRoute, x), local.y,
                            Mathf.Lerp(SlopCityDenseLayout.SouthBuildToLine - facadeRouteOffset,
                                SlopCityDenseLayout.NorthBuildToLine + facadeRouteOffset, z));
                        // Largest transformed neighbour step is 1.936m: retain the crowd's
                        // existing 2m bound, capsule casts, support checks and seed spacing.
                        return district.TransformPoint(replotted);
                    }
                    return point + (stationOffsets.TryGetValue(index, out Vector3 delta) ? delta : Vector3.zero);
                });
                widenedTowers = towers.Count;
                prepared = true;
                Physics.SyncTransforms();
            }
            catch
            {
                for (int i = undo.Count - 1; i >= 0; i--) undo[i]();
                for (int i = ownedMeshes.Count - 1; i >= originalMeshCount; i--)
                { Dispose(ownedMeshes[i]); ownedMeshes.RemoveAt(i); }
                throw;
            }
        }

        private void WidenTower(Tower tower, List<Action> undo, Dictionary<Mesh, Mesh> cache)
        {
            foreach (Transform child in Children(tower.root))
            {
                if (MetricTowerHardware(child.name) || child.GetComponent<TextMesh>())
                {
                    Vector3 point = child.localPosition;
                    bool entrance = child.name.StartsWith("Lobby_", StringComparison.Ordinal) || child.name == "Tower_Lift_Street";
                    Vector3 next = MountedPoint(point, tower.dimensions, tower.widthFactor);
                    if (entrance) next.x = point.x;
                    TextMesh label = child.GetComponent<TextMesh>();
                    if (label && child.name.StartsWith("City_Sign_", StringComparison.Ordinal)
                        && label.text.IndexOf(" / ROOF TERRACE", StringComparison.Ordinal) >= 0)
                    {
                        // The .035m old mount sits inside restored cladding and the armored
                        // entrance brow (.96m). Keep the adult-scale label on its front face.
                        next.z = -tower.dimensions.z * PlotFactor * .5f - 1.025f;
                    }
                    MoveLocal(child, next, undo);
                }
                else ScaleField(child, new Vector3(tower.widthFactor, 1, PlotFactor), undo, cache);
            }
        }

        private static bool MetricTowerHardware(string name)
            => name.StartsWith("Lobby_", StringComparison.Ordinal) || name.StartsWith("Tower_Lift_", StringComparison.Ordinal)
                || name.StartsWith("Tower_Lobby_", StringComparison.Ordinal) || name.StartsWith("Tower_Roof_", StringComparison.Ordinal)
                || name == "Mechanical_Unit" || name == "HVAC_Louvre" || name == "Roof_Plant_Screen"
                || name == "Aviation_Light";

        private static Vector3 MountedPoint(Vector3 point, Vector3 dimensions, float widthFactor)
            => new Vector3(MountedCoordinate(point.x, dimensions.x / 2, widthFactor), point.y, MountedCoordinate(point.z, dimensions.z / 2));
        private static float MountedCoordinate(float value, float half, float factor = PlotFactor)
            => Mathf.Abs(value) >= half - .7f ? Mathf.Sign(value) * (half * factor + Mathf.Abs(value) - half) : value * factor;

        private void PrepareJapan(Transform city, Dictionary<Transform, Vector3> old, List<Tower> towers,
            Dictionary<int, Vector3> offsets, List<Action> undo)
        {
            Transform japan = city.Find("City_JapanMetropolis");
            if (!japan) return;
            foreach (Transform block in Children(japan))
            {
                if (!block.name.StartsWith("Japan_District_", StringComparison.Ordinal))
                { Move(block, city.TransformPoint(SlopCityDenseLayout.StreetPoint(city.InverseTransformPoint(old[block]))), undo); continue; }
                if (!int.TryParse(block.name.Substring("Japan_District_".Length), out int index) || !offsets.TryGetValue(index, out Vector3 delta)) continue;
                Move(block, old[block] + delta, undo);
                foreach (Transform fixture in Children(block))
                {
                    Tower mounted = null; float nearest = float.PositiveInfinity;
                    foreach (Tower tower in towers)
                    {
                        SlopCityDistrict district = tower.root.GetComponentInParent<SlopCityDistrict>();
                        if (!district || district.Index != index) continue;
                        Vector3 local = tower.toLocal.MultiplyPoint3x4(old[fixture]);
                        if (Mathf.Abs(local.x) > tower.dimensions.x / 2 + 2.1f || Mathf.Abs(local.z) > tower.dimensions.z / 2 + 2.1f) continue;
                        if (local.y < 3.3f && fixture.name != "Japan_Facade_Service_Riser") continue;
                        float distance = (old[fixture] - tower.originalPosition).sqrMagnitude;
                        if (distance < nearest) { nearest = distance; mounted = tower; }
                    }
                    if (mounted != null)
                        Move(fixture, mounted.root.TransformPoint(MountedPoint(mounted.toLocal.MultiplyPoint3x4(old[fixture]), mounted.dimensions, mounted.widthFactor)), undo);
                }
            }
        }

        private void PrepareMilitary(Transform city, Dictionary<Transform, Vector3> old, List<Tower> towers,
            List<Action> undo, Dictionary<Mesh, Mesh> cache)
        {
            Transform armor = city.Find("City_MilitarySciFiArchitecture");
            if (!armor) return;
            foreach (Transform fixture in Children(armor))
            {
                if (fixture.name.StartsWith("MilitarySF_LocalTower_", StringComparison.Ordinal))
                {
                    Tower source = towers.OrderBy(tower => (tower.originalPosition - old[fixture]).sqrMagnitude).FirstOrDefault();
                    if (source == null || (source.originalPosition - old[fixture]).sqrMagnitude > .05f)
                        throw new InvalidOperationException("Detached tower architecture has no source: " + fixture.name);
                    Move(fixture, source.root.position, undo);
                    // Material batches contain disconnected manufactured panels. Widen their
                    // frontage while retaining the normal extrusion, chamfers and mount offsets;
                    // scaling both axes would bury unchanged Japanese signs/media behind .81m armor.
                    PrepareMilitaryPanels(fixture, source.dimensions, source.widthFactor, undo);
                }
                else if (fixture.name == "MilitarySF_North_TransitSpine" || fixture.name == "MilitarySF_West_TransitSpine")
                {
                    MoveLocal(fixture, fixture.name == "MilitarySF_North_TransitSpine"
                        ? new Vector3(0, 0, 111 + SlopCityDenseLayout.SkylineExpansionZ)
                        : new Vector3(-132.75f - SlopCityDenseLayout.SkylineExpansionX, 0, SlopCityDenseLayout.CoreGroundRect.center.y), undo);
                    Transform existing = fixture.Find("Dense_Transit_Pier_Colliders");
                    Transform piers = SlopCityDenseLayout.EnsureTransitPierColliders(fixture);
                    if (!existing && piers) undo.Add(() => { if (piers) Dispose(piers.gameObject); });
                }
                // Horizon tower dressing is kept metric and follows PrepareTowerLayout.
                // The citadel remains metric; its preparation sets the shared outer plot.
            }
        }

        private void PrepareMilitaryPanels(Transform owner, Vector3 dimensions, float widthFactor, List<Action> undo)
        {
            foreach (MeshFilter filter in owner.GetComponentsInChildren<MeshFilter>(true))
            {
                RequireReadable(filter);
                Mesh original = filter.sharedMesh;
                if (!original) continue;
                Mesh mesh = Instantiate(original); mesh.name = original.name + "_MetricMounts";
                ownedMeshes.Add(mesh);
                Matrix4x4 toOwner = owner.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Matrix4x4 toMesh = toOwner.inverse;
                Vector3[] vertices = original.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = toOwner.MultiplyPoint3x4(vertices[i]);
                int[] groups = ConnectedPieces(vertices, original.triangles);
                Dictionary<int, Bounds> bounds = PieceBounds(vertices, groups);
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = toMesh.MultiplyPoint3x4(MilitaryPanelPoint(vertices[i], bounds[Find(groups, i)], dimensions, widthFactor));
                mesh.vertices = vertices;
                // The position map is piecewise. Rebuild actual triangle normals instead of
                // applying a single inverse-scale normal to independently mounted panels.
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                Renderer renderer = filter.GetComponent<Renderer>();
                Material material = renderer ? renderer.sharedMaterial : null;
                if (material && material.mainTexture && material.mainTexture.name.EndsWith("_albedo", StringComparison.Ordinal))
                    SlopCityInteriorGeometry.MapMetricUV(mesh, filter.transform);
                else mesh.RecalculateTangents();
                AssignMesh(filter, original, mesh, undo);
            }
            // Current authored military batches have no box colliders. Preserve the same
            // dimensional map if a real panel collider is present, including transactional undo.
            foreach (BoxCollider box in owner.GetComponentsInChildren<BoxCollider>(true))
            {
                Vector3 center = box.center, size = box.size;
                undo.Add(() => { if (box) { box.center = center; box.size = size; } });
                Matrix4x4 toOwner = owner.worldToLocalMatrix * box.transform.localToWorldMatrix;
                Matrix4x4 toBox = toOwner.inverse;
                var corners = new Vector3[8];
                var source = new Bounds(toOwner.MultiplyPoint3x4(center), Vector3.zero);
                for (int i = 0; i < corners.Length; i++)
                {
                    Vector3 corner = center + Vector3.Scale(size * .5f, new Vector3((i & 1) == 0 ? -1 : 1,
                        (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    corners[i] = toOwner.MultiplyPoint3x4(corner); source.Encapsulate(corners[i]);
                }
                var mapped = new Bounds(toBox.MultiplyPoint3x4(MilitaryPanelPoint(corners[0], source, dimensions, widthFactor)), Vector3.zero);
                foreach (Vector3 corner in corners) mapped.Encapsulate(toBox.MultiplyPoint3x4(MilitaryPanelPoint(corner, source, dimensions, widthFactor)));
                box.center = mapped.center; box.size = mapped.size;
            }
        }

        private static Vector3 MilitaryPanelPoint(Vector3 point, Bounds piece, Vector3 dimensions, float widthFactor)
        {
            Vector3 center = piece.center;
            float hx = dimensions.x * .5f, hz = dimensions.z * .5f;
            bool xFace = Mathf.Abs(center.x) >= hx - .30f;
            bool zFace = Mathf.Abs(center.z) >= hz - .30f;
            // A thin cheek can sit near a corner while still belonging to one facade.
            // Its tangential span must grow; only genuinely two-sided corner hardware
            // retains both horizontal dimensions.
            if (xFace && zFace && piece.size.x > piece.size.z * 2f) xFace = false;
            else if (xFace && zFace && piece.size.z > piece.size.x * 2f) zFace = false;
            if (xFace && zFace)
            {
                // Corner columns and diagonal courses keep their manufactured section and
                // offset from the corner, rather than becoming three times thicker.
                Vector3 mounted = MountedPoint(center, dimensions, widthFactor);
                return mounted + point - center;
            }
            if (xFace)
                return new Vector3(MountedCoordinate(center.x, hx, widthFactor) + point.x - center.x, point.y, SpanCoordinate(point.z, hz));
            if (zFace)
                return new Vector3(SpanCoordinate(point.x, hx, widthFactor), point.y, MountedCoordinate(center.z, hz) + point.z - center.z);
            // Broad roof hoods widen across the building; portions beyond the shell retain
            // their original metric overhang instead of tripling the eave projection.
            return new Vector3(SpanCoordinate(point.x, hx, widthFactor), point.y, SpanCoordinate(point.z, hz));
        }

        private static float SpanCoordinate(float value, float half, float factor = PlotFactor)
            => Mathf.Abs(value) <= half ? value * factor
                : Mathf.Sign(value) * (half * factor + Mathf.Abs(value) - half);

        private static int[] ConnectedPieces(Vector3[] vertices, int[] triangles)
        {
            var groups = new int[vertices.Length];
            var coincident = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i] = i;
                Vector3 p = vertices[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 10000), Mathf.RoundToInt(p.y * 10000), Mathf.RoundToInt(p.z * 10000));
                if (coincident.TryGetValue(key, out int other)) Union(groups, i, other); else coincident[key] = i;
            }
            for (int i = 0; i < triangles.Length; i += 3)
            { Union(groups, triangles[i], triangles[i + 1]); Union(groups, triangles[i], triangles[i + 2]); }
            return groups;
        }

        private static Dictionary<int, Bounds> PieceBounds(Vector3[] vertices, int[] groups)
        {
            var bounds = new Dictionary<int, Bounds>();
            for (int i = 0; i < vertices.Length; i++)
            {
                int key = Find(groups, i);
                if (bounds.TryGetValue(key, out Bounds piece)) { piece.Encapsulate(vertices[i]); bounds[key] = piece; }
                else bounds[key] = new Bounds(vertices[i], Vector3.zero);
            }
            return bounds;
        }

        private void RebuildPavement(SlopCityDistrict district, List<Action> undo)
        {
            Transform homeSource = district.transform.Find("Home_Sidewalk_S");
            Transform source = district.transform.Find("Sidewalk") ?? homeSource;
            Renderer renderer = source ? source.GetComponent<Renderer>() : null;
            Material material = renderer ? renderer.sharedMaterial : null;
            if (!material) throw new InvalidOperationException("Neighbourhood pavement material missing.");
            SlopCityInteriorGeometry.Initialize();
            Transform replacement = SlopCityInteriorGeometry.Root(district.transform, "Dense_Block_Pavement");
            undo.Add(() => { if (replacement) Dispose(replacement.gameObject); });
            Rect footprint = Rect.MinMaxRect(-SlopCityDenseLayout.PavementHalfWidth, SlopCityDenseLayout.PavementSouth, SlopCityDenseLayout.PavementHalfWidth, SlopCityDenseLayout.PavementNorth);
            var areas = new List<Rect> { footprint };
            if (district.Index == 4)
            {
                GameObject floor = GameObject.Find("Floor_Main");
                Renderer home = floor ? floor.GetComponent<Renderer>() : null;
                if (!home) throw new InvalidOperationException("Human-scale pavement requires the authored home floor.");
                Vector3 min = district.transform.InverseTransformPoint(home.bounds.min), max = district.transform.InverseTransformPoint(home.bounds.max);
                areas = SlopCityInteriorGeometry.Subtract(footprint, Rect.MinMaxRect(min.x - .025f, min.z - .025f, max.x + .025f, max.z + .025f)).ToList();
            }
            Transform asphalt = district.transform.parent.Find("City_Asphalt");
            Material yardMaterial = asphalt ? asphalt.GetComponent<Renderer>()?.sharedMaterial : null;
            if (!yardMaterial) throw new InvalidOperationException("Service courtyard surface material missing.");
            // Separate the public footway from the retained inner courts. Full-size room
            // footprints and practical courtyard operations survive without a broad pale plaza.
            foreach (Rect area in areas)
                PavementBox(replacement, "Neighbourhood_ServiceYard", new Vector3(area.center.x, -.04f, area.center.y),
                    new Vector3(area.width, .12f, area.height), yardMaterial, true);
            var sidewalks = new[]
            {
                Rect.MinMaxRect(footprint.xMin, footprint.yMin, -SlopCityDenseLayout.SideBuildToLine, footprint.yMax),
                Rect.MinMaxRect(SlopCityDenseLayout.SideBuildToLine, footprint.yMin, footprint.xMax, footprint.yMax),
                Rect.MinMaxRect(-SlopCityDenseLayout.SideBuildToLine, footprint.yMin, SlopCityDenseLayout.SideBuildToLine, SlopCityDenseLayout.SouthBuildToLine),
                Rect.MinMaxRect(-SlopCityDenseLayout.SideBuildToLine, SlopCityDenseLayout.NorthBuildToLine, SlopCityDenseLayout.SideBuildToLine, footprint.yMax)
            };
            foreach (Rect sidewalk in sidewalks)
                foreach (Rect area in areas)
                {
                    Rect section = Rect.MinMaxRect(Mathf.Max(sidewalk.xMin, area.xMin), Mathf.Max(sidewalk.yMin, area.yMin),
                        Mathf.Min(sidewalk.xMax, area.xMax), Mathf.Min(sidewalk.yMax, area.yMax));
                    if (section.width <= .001f || section.height <= .001f) continue;
                    PavementBox(replacement, "Neighbourhood_Sidewalk", new Vector3(section.center.x, .021f, section.center.y),
                        new Vector3(section.width, .002f, section.height), material, false);
                }
            foreach (float x in new[] { footprint.xMin + .08f, footprint.xMax - .08f })
                PavementBox(replacement, "Low_Curb_NS", new Vector3(x, .025f, footprint.center.y), new Vector3(.16f, .07f, footprint.height), material, true);
            foreach (float z in new[] { footprint.yMin + .08f, footprint.yMax - .08f })
                PavementBox(replacement, "Low_Curb_EW", new Vector3(0, .025f, z), new Vector3(footprint.width, .07f, .16f), material, true);
        }

        private void PavementBox(Transform parent, string name, Vector3 point, Vector3 size, Material material, bool solid)
        {
            GameObject box = SlopCityInteriorGeometry.Box(parent, name, point, size, material, solid);
            ownedMeshes.Add(SlopCityInteriorGeometry.ApplyMetricSurface(box));
        }

        private static void AlignReflectionProbes(Transform city, List<Action> undo)
        {
            foreach (GameObject root in city.gameObject.scene.GetRootGameObjects())
                foreach (ReflectionProbe probe in root.GetComponentsInChildren<ReflectionProbe>(true))
                {
                    if (probe.name != "Street_Reflection" && !probe.name.StartsWith("Junction_Reflection_", StringComparison.Ordinal)
                        && !probe.name.StartsWith("Reflection_Subway_Station_", StringComparison.Ordinal)) continue;
                    Vector3 size = probe.size, center = probe.center;
                    undo.Add(() => { if (probe) { probe.size = size; probe.center = center; } });
                    Move(probe.transform, city.TransformPoint(SlopCityDenseLayout.StreetPoint(city.InverseTransformPoint(probe.transform.position))), undo);
                    if (probe.name == "Street_Reflection")
                    {
                        Rect ground = SlopCityDenseLayout.OuterGroundRect;
                        probe.size = new Vector3(ground.width + 8, 155, ground.height + 8);
                        probe.center = probe.transform.InverseTransformPoint(city.TransformPoint(new Vector3(ground.center.x, 55, ground.center.y)));
                    }
                    else if (probe.name.StartsWith("Junction_Reflection_", StringComparison.Ordinal))
                        probe.size = new Vector3(SlopCityDenseLayout.ColumnPitch + 36, 22, SlopCityDenseLayout.RowPitch + 30);
                }
        }

        private void ReplotBridge(Transform bridge, List<Tower> towers, Dictionary<Transform, Vector3> old,
            List<Action> undo, Dictionary<Mesh, Mesh> cache)
        {
            MeshFilter deck = bridge.Find("Bridge_Steel_Deck")?.GetComponent<MeshFilter>();
            if (!deck || !deck.sharedMesh) return;
            float length = deck.sharedMesh.bounds.size.z;
            Vector3 center = old[bridge];
            Tower a = towers.OrderBy(tower => (tower.originalPosition - (center - bridge.forward * length / 2)).sqrMagnitude).First();
            Tower b = towers.OrderBy(tower => (tower.originalPosition - (center + bridge.forward * length / 2)).sqrMagnitude).First();
            Vector3 delta = b.root.position - a.root.position; delta.y = 0;
            // Retain the cabin-width bridge; only its span follows the paired buildings.
            foreach (Transform child in Children(bridge)) ScaleField(child, new Vector3(1, 1, delta.magnitude / length), undo, cache);
            Move(bridge, (a.root.position + b.root.position) / 2 + Vector3.up * center.y, undo);
            Quaternion rotation = bridge.rotation; undo.Add(() => { if (bridge) bridge.rotation = rotation; });
            bridge.rotation = Quaternion.LookRotation(delta);
        }

        private void PrepareBoundary(Transform root, List<Action> undo, Dictionary<Mesh, Mesh> cache)
        {
            // The outer streets now continue into adjacent blocks rather than a distant fenced plaza.
            // Keep the authored signs/posts; BuildingWorld opens the old wall line for circulation.
            foreach (Transform side in Children(root))
                foreach (Transform fixture in Children(side))
                {
                    if (fixture.name == "Retaining_Wall" || fixture.name == "Boundary_LightStrip")
                        SetActive(fixture.gameObject, false, undo);
                    else Move(fixture, root.TransformPoint(SlopCityDenseLayout.StreetPoint(root.InverseTransformPoint(fixture.position))), undo);
                }
        }

        private static void PrepareStreetDetails(Transform root, Dictionary<Transform, Vector3> old, List<Action> undo)
        {
            Transform[] grates = Children(root).Where(item => item.name == "Drain_Grate").ToArray();
            foreach (Transform fixture in Children(root))
            {
                Vector3 source = old[fixture];
                if (fixture.name == "Drain_Slot")
                {
                    Transform grate = grates.OrderBy(item => (old[item] - source).sqrMagnitude).FirstOrDefault();
                    if (grate && (old[grate] - source).sqrMagnitude < 1f)
                    { Move(fixture, SlopCityDenseLayout.StreetPoint(old[grate]) + source - old[grate], undo); continue; }
                }
                MoveLocal(fixture, SlopCityDenseLayout.StreetPoint(fixture.localPosition), undo);
            }
        }

        private void PrepareRoads(Transform root, List<Action> undo)
        {
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                RequireReadable(filter);
                Mesh source = filter.sharedMesh;
                if (!source) continue;
                Mesh mesh = Instantiate(source);
                // Vertex/triangle reads and uploads can fail under memory pressure.
                // Register the native clone before that work so Apply's rollback owns it.
                ownedMeshes.Add(mesh); mesh.name = source.name + "_MetricPlots";
                Vector3[] vertices = mesh.vertices;
                if (filter.name == "Street_RoadMarkings")
                {
                    // Each combined paint stroke retains its narrow metric width. Disconnected
                    // faces are joined by coincident coordinates before moving stroke centres.
                    int[] groups = new int[vertices.Length];
                    for (int i = 0; i < groups.Length; i++) groups[i] = i;
                    var coincident = new Dictionary<Vector3Int, int>();
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 p = vertices[i]; var key = new Vector3Int(Mathf.RoundToInt(p.x * 10000), Mathf.RoundToInt(p.y * 10000), Mathf.RoundToInt(p.z * 10000));
                        if (coincident.TryGetValue(key, out int other)) Union(groups, i, other); else coincident[key] = i;
                    }
                    int[] triangles = mesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3) { Union(groups, triangles[i], triangles[i + 1]); Union(groups, triangles[i], triangles[i + 2]); }
                    var bounds = new Dictionary<int, Bounds>();
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        int key = Find(groups, i);
                        if (bounds.TryGetValue(key, out Bounds b)) { b.Encapsulate(vertices[i]); bounds[key] = b; }
                        else bounds[key] = new Bounds(vertices[i], Vector3.zero);
                    }
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Bounds b = bounds[Find(groups, i)]; Vector3 local = vertices[i] - b.center;
                        // Paint strokes and crossing widths stay metric on the shared lanes.
                        if (b.size.x > 20 || b.size.z > 20)
                            vertices[i] = SlopCityDenseLayout.StreetPoint(vertices[i]);
                        else vertices[i] = SlopCityDenseLayout.StreetPoint(b.center) + local;
                    }
                }
                else for (int i = 0; i < vertices.Length; i++) vertices[i] = SlopCityDenseLayout.StreetPoint(vertices[i]);
                mesh.vertices = vertices; mesh.RecalculateBounds();
                AssignMesh(filter, source, mesh, undo);
            }
        }

        private void ScaleField(Transform root, Vector3 factors, List<Action> undo, Dictionary<Mesh, Mesh> cache)
        {
            Transform[] members = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform item in members)
            {
                MoveLocal(item, Vector3.Scale(item.localPosition, factors), undo);
                MeshFilter filter = item.GetComponent<MeshFilter>();
                if (filter && filter.sharedMesh)
                {
                    RequireReadable(filter);
                    Mesh original = filter.sharedMesh;
                    Mesh widened;
                    Renderer surface = item.GetComponent<Renderer>();
                    Material material = surface ? surface.sharedMaterial : null;
                    bool scanned = material && material.mainTexture && material.mainTexture.name.EndsWith("_albedo", StringComparison.Ordinal);
                    // World-coordinate UVs cannot share a clone across differently placed surfaces.
                    bool standard = !scanned && factors == new Vector3(PlotFactor, 1, PlotFactor);
                    if (!standard || !cache.TryGetValue(original, out widened))
                    {
                        widened = Instantiate(original);
                        ownedMeshes.Add(widened); widened.name = original.name + "_MetricPlots";
                        Vector3[] vertices = widened.vertices;
                        for (int i = 0; i < vertices.Length; i++) vertices[i] = Vector3.Scale(vertices[i], factors);
                        widened.vertices = vertices;
                        Vector3[] normals = widened.normals;
                        for (int i = 0; i < normals.Length; i++) normals[i] = new Vector3(normals[i].x / factors.x, normals[i].y / factors.y, normals[i].z / factors.z).normalized;
                        widened.normals = normals;
                        Vector4[] tangents = widened.tangents;
                        for (int i = 0; i < tangents.Length; i++)
                        { Vector3 tangent = Vector3.Scale(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z), factors).normalized; tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w); }
                        widened.tangents = tangents;
                        if (scanned) SlopCityInteriorGeometry.MapMetricUV(widened, item);
                        widened.RecalculateBounds(); if (standard) cache.Add(original, widened);
                    }
                    AssignMesh(filter, original, widened, undo);
                }
                foreach (BoxCollider box in item.GetComponents<BoxCollider>())
                {
                    Vector3 size = box.size, center = box.center;
                    undo.Add(() => { if (box) { box.size = size; box.center = center; } });
                    box.size = Vector3.Scale(size, factors); box.center = Vector3.Scale(center, factors);
                }
            }
        }

        private static void AssignMesh(MeshFilter filter, Mesh original, Mesh next, List<Action> undo)
        {
            undo.Add(() => { if (filter) filter.sharedMesh = original; }); filter.sharedMesh = next;
            foreach (MeshCollider collider in filter.GetComponents<MeshCollider>())
                if (collider.sharedMesh == original)
                { undo.Add(() => { if (collider) collider.sharedMesh = original; }); collider.sharedMesh = next; }
        }
        private static void RequireReadable(MeshFilter filter)
        {
            Renderer renderer = filter.GetComponent<Renderer>();
            if (filter.sharedMesh && (!filter.sharedMesh.isReadable || (renderer && renderer.isPartOfStaticBatch)))
                throw new InvalidOperationException("Prepare metric city plots before static batching: " + filter.name);
        }
        private static Transform[] Children(Transform parent)
        { var list = new Transform[parent.childCount]; for (int i = 0; i < list.Length; i++) list[i] = parent.GetChild(i); return list; }
        private static void Move(Transform item, Vector3 point, List<Action> undo)
        { Vector3 original = item.localPosition; undo.Add(() => { if (item) item.localPosition = original; }); item.position = point; }
        private static void MoveLocal(Transform item, Vector3 point, List<Action> undo)
        { Vector3 original = item.localPosition; undo.Add(() => { if (item) item.localPosition = original; }); item.localPosition = point; }
        private static void SetActive(GameObject item, bool active, List<Action> undo)
        { bool original = item.activeSelf; undo.Add(() => { if (item) item.SetActive(original); }); item.SetActive(active); }
        private static int Find(int[] groups, int item)
        { while (groups[item] != item) { groups[item] = groups[groups[item]]; item = groups[item]; } return item; }
        private static void Union(int[] groups, int a, int b)
        { groups[Find(groups, b)] = Find(groups, a); }
        private static void Dispose(UnityEngine.Object item)
        { if (!item) return; if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
        private void OnDestroy() { foreach (Mesh mesh in ownedMeshes) Dispose(mesh); ownedMeshes.Clear(); }
    }
}
