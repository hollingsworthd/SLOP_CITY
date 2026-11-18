using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopCity
{
    public interface ISlopCityCityForgeDraftPreviewSink
    {
        void ShowCityForgeDraftPreview(CityForgeResolvedChunk chunk);
        void ClearCityForgeDraftPreview();
    }

    /// <summary>Bounded top-down strategic representation; it never becomes physical accepted topology.</summary>
    public interface ISlopCityCityForgeStrategicMapSink
    {
        void ShowCityForgeStrategicMap(CityForgeWorldHead head, CityForgeCoord center, int radius, IReadOnlyDictionary<CityForgeCoord, CityForgeChunkPlan> acceptedPlans);
        void ClearCityForgeStrategicMap();
    }

    /// <summary>
    /// Optional extension for sinks that can render a separately fetched,
    /// bounded distant-summary window. Keeping it separate preserves the base
    /// sink for integrations that only know local accepted ChunkPlans.
    /// </summary>
    public interface ISlopCityCityForgeStrategicSummarySink : ISlopCityCityForgeStrategicMapSink
    {
        void ShowCityForgeStrategicMap(CityForgeWorldHead head, CityForgeCoord center, int radius,
            IReadOnlyDictionary<CityForgeCoord, CityForgeChunkPlan> acceptedPlans,
            IReadOnlyDictionary<CityForgeCoord, CityForgeStrategicChunkSummary> strategicSummaries);
    }

    /// <summary>
    /// A presentation-only planning layer. It receives bounded, catalog-backed
    /// plan data from Build Mode and never registers fixtures, colliders, media,
    /// or accepted world state.
    /// </summary>
    public interface ISlopCityCityForgeBuildOverlaySink
    {
        void ShowCityForgeBuildOverlay(CityForgeWorldHead head, CityForgeCoord selected,
            CityForgeChunkPlan draft, CityForgeCoord? hoveredCell,
            CityForgeDirection roadDirection, SlopCityCityForgeBuildMode.View view,
            bool draftIsValid);
        void ClearCityForgeBuildOverlay();
    }

    /// <summary>
    /// Concrete, catalog-only presentation sink for streamed City Forge chunks.
    /// The data model supplies IDs and grid coordinates only; this component
    /// derives every GameObject, collider, scale, orientation, and screen mount
    /// from shipped code.  It never reads a transform, URL, mesh, material, or
    /// asset reference from a player plan.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityForgeRuntime : MonoBehaviour, ISlopCityCityForgeChunkSink, ISlopCityCityForgeDraftPreviewSink, ISlopCityCityForgeStrategicSummarySink, ISlopCityCityForgeBuildOverlaySink
    {
        [SerializeField] private Transform listener;
        [SerializeField] private bool constrainedWebGl;
        [SerializeField, Range(.1f, 1f)] private float admissionInterval = .2f;
        [SerializeField, Range(8f, 72f)] private float screenViewRange = 48f;
        private readonly Dictionary<CityForgeCoord, ChunkInstance> chunks = new Dictionary<CityForgeCoord, ChunkInstance>();
        private readonly Dictionary<SlopCityCityScreen, ScreenFixture> fixtures = new Dictionary<SlopCityCityScreen, ScreenFixture>();
        private readonly List<SlopCityCityForgeMediaAdmission.Candidate> candidateBuffer = new List<SlopCityCityForgeMediaAdmission.Candidate>();
        private readonly HashSet<string> admittedIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<SlopCityCityForgeFixture, InteriorPortal> interiorEntries = new Dictionary<SlopCityCityForgeFixture, InteriorPortal>();
        private readonly Dictionary<SlopCityCityForgeFixture, InteriorPortal> interiorReturns = new Dictionary<SlopCityCityForgeFixture, InteriorPortal>();
        private readonly Dictionary<SlopCityCityForgeFixture, CityForgeSituationState> persistedSituationFixtures = new Dictionary<SlopCityCityForgeFixture, CityForgeSituationState>();
        private readonly Dictionary<SlopCityCityForgeFixture, CityForgeSituationOpportunity> opportunitySituationFixtures = new Dictionary<SlopCityCityForgeFixture, CityForgeSituationOpportunity>();
        private static readonly MaterialPropertyBlock colorProperties = new MaterialPropertyBlock();
        private float nextAdmission;
        private ChunkInstance preview;
        private GameObject strategicMap;
        // One collider-free mesh covers the bounded strategic window. A grid
        // per chunk is useful planning feedback, but thousands of cube
        // primitives would turn a map-only view into a mobile allocation
        // spike.
        private Mesh strategicGridMesh;
        private Material strategicGridMaterial;
        private GameObject buildOverlay;
        private Material buildOverlayMaterial;
        private bool buildOverlayPresentationActive;

        public int ActiveChunkCount { get { return chunks.Count; } }
        public int ActiveFixtureCount { get { return fixtures.Count; } }
        /// <summary>Raised only for a catalog-derived physical fixture; no arbitrary player text or scene data crosses this boundary.</summary>
        public event Action<SlopCityCityForgeFixture> FixtureInteracted;
        /// <summary>Raised from the existing touch/first-person fixture path for a persisted authored CitySituation.</summary>
        public event Action<CityForgeSituationState> SituationInteractionRequested;
        /// <summary>
        /// Coordinate-bearing companion for a persisted CitySituation USE. A
        /// public offline lifecycle handler may persist it; a hosted bridge can
        /// instead prepare and submit the resulting canonical chunk delta.
        /// </summary>
        public event Action<CityForgeCoord, CityForgeSituationState> SituationTransitionRequested;
        public event Action<CityForgeCoord, CityForgeSituationOpportunity> SituationOpportunityRequested;

        private void Awake()
        {
            // City Forge fixtures are normal SlopCityInteractables. First-person,
            // keyboard, and touch use the established Sandbox interaction path;
            // no parallel raw-key route owns their use behavior.
        }

        private SlopCityCityForgeSkyVista skyVista;

        public void Configure(Transform cityListener, bool useConstrainedWebGl)
        {
            listener = cityListener;
            constrainedWebGl = useConstrainedWebGl;
            if (!skyVista) skyVista = GetComponent<SlopCityCityForgeSkyVista>() ?? gameObject.AddComponent<SlopCityCityForgeSkyVista>();
            if (skyVista) skyVista.Configure(cityListener);
            RefreshMediaAdmission(true);
        }

        public void SetListener(Transform cityListener)
        {
            listener = cityListener;
            RefreshMediaAdmission(true);
        }

        public void ActivateCityForgeChunk(CityForgeResolvedChunk chunk)
        {
            if (chunk == null || chunk.baseChunk == null) return;
            CityForgeCoord coord = chunk.baseChunk.coord;
            // Compiler updates can replace an accepted revision under the same
            // coordinate. Revoke the old physical fixtures synchronously first.
            ReleaseCityForgeChunk(coord);
            GameObject root = new GameObject("CityForgeChunk_" + coord.x + "_" + coord.z);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = ChunkOrigin(coord);
            root.SetActive(false);
            ChunkInstance instance = new ChunkInstance { root = root, coord = coord, resolvedHash = chunk.resolvedHash ?? string.Empty };
            try
            {
                BuildScenicCityContinuation(instance, chunk);
                BuildGround(instance, chunk);
                BuildRoads(instance, chunk.roads);
                BuildBuildings(instance, chunk);
                BuildServices(instance, chunk);
                BuildLandmarks(instance, chunk);
                BuildLife(instance, chunk);
                BuildScreens(instance, chunk);
                // Build Mode is a clean planning surface, not a second scene
                // layered over street geometry. A chunk that arrives while the
                // editor is open remains safely streamed but visually hidden
                // until the player returns to first person.
                root.SetActive(!buildOverlayPresentationActive);
                chunks.Add(coord, instance);
            }
            catch (Exception error)
            {
                Revoke(instance);
                Destroy(root);
                Debug.LogWarning("City Forge chunk presentation was unavailable: " + error.GetType().Name, this);
            }
            RefreshMediaAdmission(true);
        }

        public void ReleaseCityForgeChunk(CityForgeCoord coordinate)
        {
            if (!chunks.TryGetValue(coordinate, out ChunkInstance instance)) return;
            chunks.Remove(coordinate);
            Revoke(instance);
            if (instance.root) Destroy(instance.root);
            RefreshMediaAdmission(true);
        }

        public void ReleaseAllCityForgeChunks()
        {
            foreach (ChunkInstance instance in chunks.Values.ToArray())
            {
                Revoke(instance);
                if (instance.root) Destroy(instance.root);
            }
            chunks.Clear();
            fixtures.Clear();
            admittedIds.Clear();
        }

        /// <summary>
        /// Applies a persisted CitySituation state to its already-streamed
        /// fixture in place. This is deliberately not a compiler reconcile:
        /// no building, road, collider, or interior is replaced around a walker.
        /// </summary>
        public bool RefreshCityForgeSituation(CityForgeCoord coordinate, CityForgeSituationState situation)
        {
            if (situation == null) return false;
            SlopCityCityForgeFixture target = persistedSituationFixtures.Keys.FirstOrDefault(item => item != null && item.Chunk.Equals(coordinate) && string.Equals(persistedSituationFixtures[item].id, situation.id, StringComparison.Ordinal));
            if (target != null)
            {
                persistedSituationFixtures[target] = situation.Clone();
                UpdateSituationFixture(target, situation);
                return true;
            }
            target = opportunitySituationFixtures.Keys.FirstOrDefault(item => item != null && item.Chunk.Equals(coordinate)
                && string.Equals(opportunitySituationFixtures[item].landmarkId, situation.landmarkId, StringComparison.Ordinal)
                && string.Equals(opportunitySituationFixtures[item].situationId, situation.situationId, StringComparison.Ordinal));
            if (target == null) return false;
            opportunitySituationFixtures.Remove(target);
            persistedSituationFixtures[target] = situation.Clone();
            UpdateSituationFixture(target, situation);
            return true;
        }

        private static void UpdateSituationFixture(SlopCityCityForgeFixture fixture, CityForgeSituationState situation)
        {
            fixture.UpdatePresentation(situation.situationId, "Situation " + situation.state + " / " + situation.situationId);
            int variant = SituationVariant(situation.situationId);
            foreach (Renderer renderer in fixture.GetComponentsInChildren<Renderer>(true)) SetRendererColor(renderer, LandmarkStateColor(variant, situation.state));
        }

        /// <summary>Renders a separate, unadmitted preview subtree; accepted chunks are never replaced.</summary>
        public void ShowCityForgeDraftPreview(CityForgeResolvedChunk chunk)
        {
            ClearCityForgeDraftPreview();
            if (chunk == null || chunk.baseChunk == null) return;
            GameObject root = new GameObject("CityForgeDraftPreview_" + chunk.baseChunk.coord);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = ChunkOrigin(chunk.baseChunk.coord) + Vector3.up * .18f;
            root.SetActive(false);
            preview = new ChunkInstance { root = root, coord = chunk.baseChunk.coord, resolvedHash = chunk.resolvedHash };
            try { BuildGround(preview, chunk); BuildRoads(preview, chunk.roads); BuildBuildings(preview, chunk); BuildServices(preview, chunk); BuildLandmarks(preview, chunk); BuildLife(preview, chunk); root.SetActive(true); }
            catch { if (root) Destroy(root); preview = null; }
        }

        public void ClearCityForgeDraftPreview()
        {
            if (preview == null) return;
            Revoke(preview);
            if (preview.root) Destroy(preview.root);
            preview = null;
        }

        public void ShowCityForgeStrategicMap(CityForgeWorldHead head, CityForgeCoord center, int radius, IReadOnlyDictionary<CityForgeCoord, CityForgeChunkPlan> acceptedPlans)
        {
            ShowCityForgeStrategicMap(head, center, radius, acceptedPlans, null);
        }

        public void ShowCityForgeStrategicMap(CityForgeWorldHead head, CityForgeCoord center, int radius,
            IReadOnlyDictionary<CityForgeCoord, CityForgeChunkPlan> acceptedPlans,
            IReadOnlyDictionary<CityForgeCoord, CityForgeStrategicChunkSummary> strategicSummaries)
        {
            ClearCityForgeStrategicMap();
            if (head == null) return;
            int boundedRadius = Mathf.Clamp(radius, 1, 6);
            strategicMap = new GameObject("CityForgeStrategicMap"); strategicMap.transform.SetParent(transform, false);
            // Every visible buildable block presents its real 9 × 9 planning
            // grid. This remains a bounded strategic layer, not physical
            // chunk geometry or an unbounded frontier allocation.
            BuildStrategicGrid(strategicMap.transform, center, boundedRadius);
            SlopCityCityForgeGenerator generator = new SlopCityCityForgeGenerator(SlopCityCityForgeCatalog.Default);
            for (int x = -boundedRadius; x <= boundedRadius; x++)
                for (int z = -boundedRadius; z <= boundedRadius; z++)
                {
                    CityForgeCoord coordinate = new CityForgeCoord(center.x + x, center.z + z);
                    if (!SlopCityCityForge.IsBuildable(coordinate)) continue;
                    CityForgeBaseChunk baseChunk = generator.Generate(head, coordinate);
                    // The strategic view is also sky-island based: an empty
                    // block gets a faint address outline, never a filled
                    // default-ground tile. The selected block is rendered
                    // once by its draft overlay; other accepted blocks render
                    // exactly at their authored grid locations here.
                    GameObject tile = new GameObject("Skyline_" + baseChunk.skylineId);
                    tile.transform.SetParent(strategicMap.transform, false);
                    tile.transform.localPosition = ChunkOrigin(coordinate) + new Vector3(36f, 0f, 36f);
                    BuildStrategicChunkOutline(tile.transform, SkylineColor(baseChunk.skylineId));
                    // The selected block already has an exact cell-for-cell
                    // draft overlay. Both accepted-detail and distant-summary
                    // impostors belong only to the other visible blocks;
                    // otherwise summary bars and masses cover its grid.
                    if (!coordinate.Equals(center))
                    {
                        if (acceptedPlans != null && acceptedPlans.TryGetValue(coordinate, out CityForgeChunkPlan acceptedPlan) && acceptedPlan != null)
                            BuildStrategicAcceptedImpostor(tile.transform, acceptedPlan);
                        else if (strategicSummaries != null && strategicSummaries.TryGetValue(coordinate, out CityForgeStrategicChunkSummary summary) && summary != null)
                            BuildStrategicSummaryImpostor(tile.transform, summary);
                    }
                }
        }

        public void ClearCityForgeStrategicMap()
        {
            if (strategicMap)
            {
                strategicMap.SetActive(false);
                Destroy(strategicMap); strategicMap = null;
            }
            if (strategicGridMesh) { Destroy(strategicGridMesh); strategicGridMesh = null; }
        }

        /// <summary>
        /// Draws the current editor state directly over the selected 9 × 9
        /// block. This is deliberately a low-cost map treatment rather than a
        /// second compiled world: a draft can be invalid, but it must still be
        /// intelligible and safe to revise.
        /// </summary>
        public void ShowCityForgeBuildOverlay(CityForgeWorldHead head, CityForgeCoord selected,
            CityForgeChunkPlan draft, CityForgeCoord? hoveredCell,
            CityForgeDirection roadDirection, SlopCityCityForgeBuildMode.View view,
            bool draftIsValid)
        {
            DestroyBuildOverlay(false);
            if (head == null || draft == null || !draft.coord.Equals(selected)) return;

            SetChunkPresentationForBuildOverlay(true);

            buildOverlay = new GameObject("CityForgeBuildOverlay_" + selected);
            buildOverlay.transform.SetParent(transform, false);
            buildOverlay.transform.localPosition = ChunkOrigin(selected);
            Color status = draftIsValid ? new Color(.55f, .94f, .42f) : new Color(1f, .57f, .18f);
            // The bounded strategic map already supplies the continuous 9 × 9
            // grid across every visible buildable block. A separate dark slab
            // and status border here made one selected chunk look like a
            // required placement box, even though clicks raycast directly to
            // any visible grid cell.
            BuildEditorGround(buildOverlay.transform, draft);
            BuildEditorLots(buildOverlay.transform, draft);
            BuildEditorRoads(buildOverlay.transform, draft, status);
            BuildEditorBuildings(buildOverlay.transform, draft);
            BuildEditorServicesAndLife(buildOverlay.transform, draft);
            BuildEditorScreens(buildOverlay.transform, draft);
            if (hoveredCell.HasValue)
                BuildEditorHover(buildOverlay.transform, hoveredCell.Value, roadDirection, status, view);
        }

        public void ClearCityForgeBuildOverlay()
        {
            DestroyBuildOverlay(true);
        }

        private void DestroyBuildOverlay(bool restorePhysicalChunks)
        {
            if (buildOverlay)
            {
                // A draft can change several times in one frame. Deactivate
                // the outgoing layer immediately so stale geometry never
                // flashes through the next state while Unity schedules Destroy.
                buildOverlay.SetActive(false);
                Destroy(buildOverlay);
                buildOverlay = null;
            }
            if (restorePhysicalChunks) SetChunkPresentationForBuildOverlay(false);
        }

        private void SetChunkPresentationForBuildOverlay(bool visible)
        {
            if (buildOverlayPresentationActive == visible) return;
            buildOverlayPresentationActive = visible;
            foreach (ChunkInstance instance in chunks.Values)
                if (instance != null && instance.root) instance.root.SetActive(!visible);
        }

        private void BuildEditorLots(Transform root, CityForgeChunkPlan draft)
        {
            foreach (CityForgeParcel parcel in (draft.parcels ?? new List<CityForgeParcel>()).Where(item => item != null && item.cells != null).OrderBy(item => item.id, StringComparer.Ordinal))
            {
                Color color = EditorZoneColor(parcel.zone);
                foreach (int item in parcel.cells.Where(cell => cell >= 0 && cell < SlopCityCityForge.GridSize * SlopCityCityForge.GridSize).OrderBy(cell => cell))
                {
                    int x = item % SlopCityCityForge.GridSize, z = item / SlopCityCityForge.GridSize;
                    EditorPrimitive("Lot_" + parcel.id + "_" + item, PrimitiveType.Cube, root,
                        new Vector3((x + .5f) * SlopCityCityForge.CellSizeMeters, .325f, (z + .5f) * SlopCityCityForge.CellSizeMeters),
                        new Vector3(SlopCityCityForge.CellSizeMeters - .34f, .045f, SlopCityCityForge.CellSizeMeters - .34f), color);
                }
            }
        }

        private void BuildEditorRoads(Transform root, CityForgeChunkPlan draft, Color status)
        {
            foreach (CityForgeRoad road in (draft.roads ?? new List<CityForgeRoad>()).Where(item => item != null).OrderBy(item => item.LocalKey, StringComparer.Ordinal))
            {
                Vector3 edge = RoadEdgeCentre(road) + Vector3.up * .365f;
                bool northSouth = road.direction == CityForgeDirection.North || road.direction == CityForgeDirection.South;
                Vector3 size = northSouth
                    ? new Vector3(SlopCityCityForge.CellSizeMeters - .28f, .065f, 1.05f)
                    : new Vector3(1.05f, .065f, SlopCityCityForge.CellSizeMeters - .28f);
                EditorPrimitive("DraftRoad_" + road.LocalKey, PrimitiveType.Cube, root, edge, size, Color.Lerp(RoadColor(road.roadClass), status, .18f));
            }
        }

        private void BuildEditorBuildings(Transform root, CityForgeChunkPlan draft)
        {
            foreach (CityForgeBuilding building in (draft.buildings ?? new List<CityForgeBuilding>()).Where(item => item != null).OrderBy(item => item.id, StringComparer.Ordinal))
            {
                CityForgeParcel parcel = (draft.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && item.id == building.parcelId);
                if (parcel == null || parcel.cells == null || parcel.cells.Count == 0) continue;
                Vector3 center = ParcelCentre(parcel.cells);
                float span = Mathf.Clamp(Mathf.Sqrt(parcel.cells.Count) * SlopCityCityForge.CellSizeMeters * .72f, 3.2f, 17f);
                Color color = Color.Lerp(FacadeColor(building.facadeId), Color.white, .22f);
                EditorPrimitive("DraftBuilding_" + building.id, PrimitiveType.Cube, root, center + Vector3.up * .43f, new Vector3(span, .11f, span), color);
                Vector3 direction = RoadNormal(building.entranceEdge);
                EditorPrimitive("DraftEntrance_" + building.id, PrimitiveType.Cube, root, center + direction * (span * .47f) + Vector3.up * .50f, new Vector3(1.2f, .06f, 1.2f), new Color(.92f, .95f, .73f));
            }
        }

        private void BuildEditorServicesAndLife(Transform root, CityForgeChunkPlan draft)
        {
            foreach (CityForgeService service in (draft.services ?? new List<CityForgeService>()).Where(item => item != null).OrderBy(item => item.id, StringComparer.Ordinal))
            {
                CityForgeParcel parcel = (draft.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && item.id == service.parcelId);
                if (parcel == null) continue;
                EditorPrimitive("DraftService_" + service.id, PrimitiveType.Cylinder, root, ParcelCentre(parcel.cells) + Vector3.up * .54f, new Vector3(.7f, .08f, .7f), new Color(.31f, .70f, .96f));
            }
            foreach (CityForgeLandmark landmark in (draft.landmarks ?? new List<CityForgeLandmark>()).Where(item => item != null).OrderBy(item => item.id, StringComparer.Ordinal))
            {
                CityForgeParcel parcel = (draft.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && item.id == landmark.parcelId);
                if (parcel == null) continue;
                EditorPrimitive("DraftCityworks_" + landmark.id, PrimitiveType.Cylinder, root, ParcelCentre(parcel.cells) + Vector3.up * .60f, new Vector3(1.05f, .10f, 1.05f), new Color(.98f, .69f, .24f));
            }
        }

        private void BuildEditorScreens(Transform root, CityForgeChunkPlan draft)
        {
            foreach (CityForgeScreen screen in (draft.screens ?? new List<CityForgeScreen>()).Where(item => item != null).OrderBy(item => item.screenId, StringComparer.Ordinal))
            {
                CityForgeBuilding building = (draft.buildings ?? new List<CityForgeBuilding>()).FirstOrDefault(item => item != null && item.id == screen.hostId);
                CityForgeParcel parcel = building == null ? null : (draft.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && item.id == building.parcelId);
                Vector3 marker = parcel == null ? new Vector3(36f, .65f, 36f) : ParcelCentre(parcel.cells) + Vector3.up * .65f;
                EditorPrimitive("DraftScreen_" + screen.screenId, PrimitiveType.Cube, root, marker, new Vector3(.9f, .09f, .9f), new Color(.83f, .37f, .93f));
            }
        }

        private void BuildEditorHover(Transform root, CityForgeCoord hovered, CityForgeDirection direction, Color status, SlopCityCityForgeBuildMode.View view)
        {
            int x = Mathf.Clamp(hovered.x, 0, SlopCityCityForge.GridSize - 1), z = Mathf.Clamp(hovered.z, 0, SlopCityCityForge.GridSize - 1);
            float cell = SlopCityCityForge.CellSizeMeters;
            Vector3 cellCenter = new Vector3((x + .5f) * cell, .71f, (z + .5f) * cell);
            Color hover = view == SlopCityCityForgeBuildMode.View.Streets ? status
                : view == SlopCityCityForgeBuildMode.View.Ground ? new Color(.72f, .84f, .64f)
                : new Color(.67f, .86f, .98f);
            EditorPrimitive("HoveredCell", PrimitiveType.Cube, root, cellCenter, new Vector3(cell - .5f, .045f, cell - .5f), hover);
            if (view != SlopCityCityForgeBuildMode.View.Streets) return;
            CityForgeRoad road = new CityForgeRoad { cellX = x, cellZ = z, direction = direction };
            Vector3 edge = RoadEdgeCentre(road) + Vector3.up * .76f;
            bool northSouth = direction == CityForgeDirection.North || direction == CityForgeDirection.South;
            EditorPrimitive("RoadDirectionCue", PrimitiveType.Cube, root, edge,
                northSouth ? new Vector3(cell - .18f, .08f, 1.2f) : new Vector3(1.2f, .08f, cell - .18f), status);
        }

        private GameObject EditorPrimitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            GameObject value = Primitive(name, type, parent, position, scale);
            Collider collider = value.GetComponent<Collider>();
            if (collider) collider.enabled = false;
            DisableShadow(value);
            Renderer renderer = value.GetComponent<Renderer>();
            Material material = BuildOverlayMaterial();
            if (renderer && material) renderer.sharedMaterial = material;
            SetEditorRendererColor(renderer, color);
            return value;
        }

        private Material BuildOverlayMaterial()
        {
            if (buildOverlayMaterial) return buildOverlayMaterial;
            // The build overlay is information design, not world geometry:
            // use an unlit shader whenever it is available so the plan stays
            // readable under night, interior, and weather lighting alike.
            Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            if (!shader) return null;
            buildOverlayMaterial = new Material(shader) { name = "City Forge planning overlay", hideFlags = HideFlags.DontSave };
            if (buildOverlayMaterial.HasProperty("_Metallic")) buildOverlayMaterial.SetFloat("_Metallic", 0f);
            if (buildOverlayMaterial.HasProperty("_Glossiness")) buildOverlayMaterial.SetFloat("_Glossiness", 0f);
            if (buildOverlayMaterial.HasProperty("_EmissionColor"))
            {
                buildOverlayMaterial.SetColor("_EmissionColor", Color.white);
                buildOverlayMaterial.EnableKeyword("_EMISSION");
            }
            return buildOverlayMaterial;
        }

        private static Color EditorZoneColor(CityForgeZone zone)
        {
            switch (zone)
            {
                case CityForgeZone.Home: return new Color(.37f, .66f, .54f);
                case CityForgeZone.WorkService: return new Color(.36f, .58f, .77f);
                case CityForgeZone.Trade: return new Color(.82f, .57f, .28f);
                case CityForgeZone.Civic: return new Color(.45f, .48f, .82f);
                case CityForgeZone.Culture: return new Color(.69f, .43f, .75f);
                default: return new Color(.36f, .71f, .42f);
            }
        }

        /// <summary>
        /// A bounded, collider-free distant summary of accepted sparse data. It
        /// deliberately uses only counts and approved IDs; physical geometry is
        /// still created solely by the nearby streaming compiler.
        /// </summary>
        private static void BuildStrategicAcceptedImpostor(Transform tile, CityForgeChunkPlan plan)
        {
            // Ground is deliberately sparse in the Sky Sprawl. Render each
            // accepted island cell directly on the strategic map so this view
            // agrees with both the build grid and the walkable world.
            foreach (int item in (plan.groundCells ?? new List<int>()).Where(cell => cell >= 0 && cell < SlopCityCityForge.GridSize * SlopCityCityForge.GridSize).Distinct().OrderBy(cell => cell))
            {
                int x = item % SlopCityCityForge.GridSize, z = item / SlopCityCityForge.GridSize;
                GameObject ground = Primitive("AcceptedGroundMap_" + item, PrimitiveType.Cube, tile,
                    new Vector3((x + .5f) * SlopCityCityForge.CellSizeMeters - 36f, .09f, (z + .5f) * SlopCityCityForge.CellSizeMeters - 36f),
                    new Vector3(SlopCityCityForge.CellSizeMeters, .04f, SlopCityCityForge.CellSizeMeters));
                Collider collider = ground.GetComponent<Collider>(); if (collider) collider.enabled = false;
                SetRendererColor(ground.GetComponent<Renderer>(), new Color(.54f, .67f, .48f));
            }
            Vector3 centreOffset = new Vector3(SlopCityCityForge.ChunkSizeMeters * .5f, 0f, SlopCityCityForge.ChunkSizeMeters * .5f);
            Dictionary<string, CityForgeParcel> parcels = (plan.parcels ?? new List<CityForgeParcel>())
                .Where(item => item != null && !string.IsNullOrEmpty(item.id))
                .ToDictionary(item => item.id, item => item, StringComparer.Ordinal);
            Dictionary<string, Vector3> screenHosts = new Dictionary<string, Vector3>(StringComparer.Ordinal);

            foreach (CityForgeRoad road in (plan.roads ?? new List<CityForgeRoad>()).Where(item => item != null && SlopCityCityForge.RoadHasIslandGround(plan, item)).OrderBy(item => item.LocalKey, StringComparer.Ordinal))
            {
                Vector3 edge = RoadEdgeCentre(road) - centreOffset;
                bool northSouth = road.direction == CityForgeDirection.North || road.direction == CityForgeDirection.South;
                GameObject street = Primitive("AcceptedRoadMap_" + road.LocalKey, PrimitiveType.Cube, tile, edge + Vector3.up * .13f,
                    northSouth ? new Vector3(SlopCityCityForge.CellSizeMeters - .4f, .10f, 1.35f) : new Vector3(1.35f, .10f, SlopCityCityForge.CellSizeMeters - .4f));
                Collider collider = street.GetComponent<Collider>(); if (collider) collider.enabled = false;
                SetRendererColor(street.GetComponent<Renderer>(), RoadColor(road.roadClass));
                screenHosts[RoadHost(road)] = edge + Vector3.up * .32f;
            }

            foreach (CityForgeBuilding building in (plan.buildings ?? new List<CityForgeBuilding>()).Where(item => item != null).OrderBy(item => item.id, StringComparer.Ordinal))
            {
                if (!parcels.TryGetValue(building.parcelId ?? string.Empty, out CityForgeParcel parcel) || parcel.cells == null || parcel.cells.Count == 0) continue;
                Vector3 position = ParcelCentre(parcel.cells) - centreOffset;
                float span = Mathf.Clamp(Mathf.Sqrt(parcel.cells.Count) * SlopCityCityForge.CellSizeMeters * .55f, 2.8f, 10f);
                float height = Mathf.Clamp(building.floors * .30f, .55f, 7.2f);
                GameObject mass = Primitive("AcceptedBuildingMap_" + building.id, PrimitiveType.Cube, tile, position + Vector3.up * (height * .5f + .16f), new Vector3(span, height, span));
                Collider collider = mass.GetComponent<Collider>(); if (collider) collider.enabled = false;
                SetRendererColor(mass.GetComponent<Renderer>(), KitColor(BuildingVariant(building.blueprintId), .40f, .62f));
                screenHosts[building.id] = position + Vector3.up * (height + .30f);
            }

            foreach (CityForgeParcel parcel in parcels.Values.Where(item => item.zone == CityForgeZone.Civic || item.zone == CityForgeZone.Culture || item.zone == CityForgeZone.GreenInfrastructure))
                screenHosts["site-" + parcel.id] = ParcelCentre(parcel.cells) - centreOffset + Vector3.up * .34f;

            foreach (CityForgeService service in (plan.services ?? new List<CityForgeService>()).Where(item => item != null).OrderBy(item => item.id, StringComparer.Ordinal))
            {
                if (!parcels.TryGetValue(service.parcelId ?? string.Empty, out CityForgeParcel parcel)) continue;
                GameObject marker = Primitive("AcceptedServiceMap_" + service.id, PrimitiveType.Cylinder, tile, ParcelCentre(parcel.cells) - centreOffset + Vector3.up * .42f, new Vector3(.72f, .28f, .72f));
                Collider collider = marker.GetComponent<Collider>(); if (collider) collider.enabled = false;
                SetRendererColor(marker.GetComponent<Renderer>(), KitColor(ServiceVariant(service.type), .54f, .72f));
            }

            foreach (CityForgeLandmark landmark in (plan.landmarks ?? new List<CityForgeLandmark>()).Where(item => item != null).OrderBy(item => item.id, StringComparer.Ordinal))
            {
                if (!parcels.TryGetValue(landmark.parcelId ?? string.Empty, out CityForgeParcel parcel)) continue;
                int variant = OutcomeVariant(landmark.outcomeId);
                GameObject marker = Primitive("AcceptedCityworksMap_" + landmark.id, PrimitiveType.Cylinder, tile, ParcelCentre(parcel.cells) - centreOffset + Vector3.up * .52f, new Vector3(1.02f, .38f, 1.02f));
                Collider collider = marker.GetComponent<Collider>(); if (collider) collider.enabled = false;
                SetRendererColor(marker.GetComponent<Renderer>(), LandmarkStateColor(variant, landmark.state));
            }

            foreach (CityForgeScreen screen in (plan.screens ?? new List<CityForgeScreen>()).Where(item => item != null).OrderBy(item => item.screenId, StringComparer.Ordinal))
            {
                if (!screenHosts.TryGetValue(screen.hostId ?? string.Empty, out Vector3 position)) continue;
                GameObject marker = Primitive("AcceptedScreenMap_" + screen.screenId, PrimitiveType.Cube, tile, position, new Vector3(.62f, .16f, .62f));
                Collider collider = marker.GetComponent<Collider>(); if (collider) collider.enabled = false;
                SetRendererColor(marker.GetComponent<Renderer>(), new Color(.83f, .37f, .93f));
            }
        }

        private static void BuildStrategicChunkOutline(Transform tile, Color color)
        {
            Color outline = Color.Lerp(color, Color.white, .26f);
            float half = SlopCityCityForge.ChunkSizeMeters * .5f;
            foreach (Vector3 item in new[]
            {
                new Vector3(0f, .025f, -half), new Vector3(0f, .025f, half),
                new Vector3(-half, .025f, 0f), new Vector3(half, .025f, 0f)
            })
            {
                bool horizontal = Mathf.Abs(item.z) > 0f;
                GameObject edge = Primitive("StrategicSkyOutline", PrimitiveType.Cube, tile, item,
                    horizontal ? new Vector3(SlopCityCityForge.ChunkSizeMeters, .025f, .10f) : new Vector3(.10f, .025f, SlopCityCityForge.ChunkSizeMeters));
                Collider collider = edge.GetComponent<Collider>(); if (collider) collider.enabled = false;
                SetRendererColor(edge.GetComponent<Renderer>(), outline);
            }
        }

        /// <summary>
        /// Renders the 9 × 9 cells for every buildable coordinate in the
        /// current 13 × 13 strategic window. The mesh is presentation only:
        /// it has no collider, plan, fixture, or streaming ownership and is
        /// released with the bounded map window.
        /// </summary>
        private void BuildStrategicGrid(Transform root, CityForgeCoord center, int radius)
        {
            if (!root || radius < 0) return;
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            float chunk = SlopCityCityForge.ChunkSizeMeters;
            float cell = SlopCityCityForge.CellSizeMeters;
            // Draw above map terrain so every cell remains legible after a
            // player lays ground, rather than relying on inset tile seams.
            const float gridHeight = .52f;
            const float lineWidth = .055f;
            for (int x = -radius; x <= radius; x++)
                for (int z = -radius; z <= radius; z++)
                {
                    CityForgeCoord coordinate = new CityForgeCoord(center.x + x, center.z + z);
                    if (!SlopCityCityForge.IsBuildable(coordinate)) continue;
                    Vector3 origin = ChunkOrigin(coordinate);
                    for (int index = 0; index <= SlopCityCityForge.GridSize; index++)
                    {
                        float offset = index * cell;
                        AddStrategicGridBar(vertices, triangles,
                            new Vector3(origin.x + offset, gridHeight, origin.z + chunk * .5f), lineWidth, chunk);
                        AddStrategicGridBar(vertices, triangles,
                            new Vector3(origin.x + chunk * .5f, gridHeight, origin.z + offset), chunk, lineWidth);
                    }
                }
            if (vertices.Count == 0) return;
            GameObject grid = new GameObject("CityForgeStrategicGrid");
            grid.transform.SetParent(root, false);
            MeshFilter filter = grid.AddComponent<MeshFilter>();
            MeshRenderer renderer = grid.AddComponent<MeshRenderer>();
            strategicGridMesh = new Mesh { name = "City Forge strategic cell grid", hideFlags = HideFlags.DontSave };
            strategicGridMesh.SetVertices(vertices);
            strategicGridMesh.SetTriangles(triangles, 0, true);
            strategicGridMesh.RecalculateBounds();
            filter.sharedMesh = strategicGridMesh;
            renderer.sharedMaterial = BuildStrategicGridMaterial();
            SetEditorRendererColor(renderer, new Color(.12f, .42f, .60f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void AddStrategicGridBar(List<Vector3> vertices, List<int> triangles, Vector3 center, float width, float depth)
        {
            int first = vertices.Count;
            float halfWidth = width * .5f;
            float halfDepth = depth * .5f;
            vertices.Add(new Vector3(center.x - halfWidth, center.y, center.z - halfDepth));
            vertices.Add(new Vector3(center.x - halfWidth, center.y, center.z + halfDepth));
            vertices.Add(new Vector3(center.x + halfWidth, center.y, center.z + halfDepth));
            vertices.Add(new Vector3(center.x + halfWidth, center.y, center.z - halfDepth));
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
        }

        private Material BuildStrategicGridMaterial()
        {
            if (strategicGridMaterial) return strategicGridMaterial;
            Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            if (!shader) return null;
            strategicGridMaterial = new Material(shader) { name = "City Forge strategic grid", hideFlags = HideFlags.DontSave };
            if (strategicGridMaterial.HasProperty("_Metallic")) strategicGridMaterial.SetFloat("_Metallic", 0f);
            if (strategicGridMaterial.HasProperty("_Glossiness")) strategicGridMaterial.SetFloat("_Glossiness", 0f);
            if (strategicGridMaterial.HasProperty("_EmissionColor"))
            {
                strategicGridMaterial.SetColor("_EmissionColor", Color.white);
                strategicGridMaterial.EnableKeyword("_EMISSION");
            }
            return strategicGridMaterial;
        }

        private static void BuildStrategicSummaryImpostor(Transform tile, CityForgeStrategicChunkSummary summary)
        {
            BuildStrategicCounts(tile, summary.roadCount, summary.buildingCount, summary.serviceCount, summary.landmarkCount);
        }

        private static void BuildStrategicCounts(Transform tile, int roads, int buildings, int services, int landmarks)
        {
            if (roads > 0)
            {
                GameObject street = Primitive("AcceptedRoadMap", PrimitiveType.Cube, tile, new Vector3(0f, .06f, 0f), new Vector3(56f, .08f, Mathf.Clamp(roads, 1, 18) * .26f));
                Collider collider = street.GetComponent<Collider>(); if (collider) collider.enabled = false;
                SetRendererColor(street.GetComponent<Renderer>(), new Color(.47f, .47f, .42f));
            }
            if (buildings > 0)
            {
                float height = .35f + Mathf.Min(3.2f, buildings * .09f);
                GameObject skyline = Primitive("AcceptedBuildingMap", PrimitiveType.Cube, tile, new Vector3(0f, height * .5f, 0f), new Vector3(30f + Mathf.Min(22f, buildings), height, 20f + Mathf.Min(18f, buildings * .7f)));
                Collider collider = skyline.GetComponent<Collider>(); if (collider) collider.enabled = false;
                SetRendererColor(skyline.GetComponent<Renderer>(), new Color(.29f, .50f, .45f));
            }
            if (services + landmarks > 0)
            {
                GameObject pulse = Primitive("AcceptedLifeMap", PrimitiveType.Cylinder, tile, new Vector3(0f, .35f, 0f), new Vector3(.5f + (services + landmarks) * .05f, .35f, .5f + (services + landmarks) * .05f));
                Collider collider = pulse.GetComponent<Collider>(); if (collider) collider.enabled = false;
                SetRendererColor(pulse.GetComponent<Renderer>(), landmarks > 0 ? new Color(.73f, .51f, .22f) : new Color(.28f, .50f, .72f));
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < nextAdmission) return;
            nextAdmission = Time.unscaledTime + Mathf.Clamp(admissionInterval, .1f, 1f);
            RefreshMediaAdmission(false);
        }

        private void OnDisable() { RevokeAllMedia(); }
        private void OnDestroy()
        {
            ClearCityForgeBuildOverlay(); ClearCityForgeStrategicMap(); ClearCityForgeDraftPreview(); ReleaseAllCityForgeChunks();
            if (buildOverlayMaterial) Destroy(buildOverlayMaterial);
            if (strategicGridMaterial) Destroy(strategicGridMaterial);
        }

        /// <summary>
        /// Untouched City Forge coordinates remain player-owned: they have no
        /// accepted ground, streets, fixtures, or save records.  They still
        /// need the deterministic Forge city fabric that makes the Sprawl read
        /// as a continuation of the Old City at street level.  A real accepted
        /// ChunkPlan replaces this bounded presentation outright.
        /// </summary>
        private void BuildScenicCityContinuation(ChunkInstance instance, CityForgeResolvedChunk chunk)
        {
            if (instance == null || !instance.root || chunk == null || chunk.baseChunk == null
                || chunk.acceptedPlan != null || !SlopCityCityForge.IsBuildable(instance.coord)) return;

            CityForgeBaseChunk baseChunk = chunk.baseChunk;
            const float chunkSize = SlopCityCityForge.ChunkSizeMeters;
            GameObject cityscape = new GameObject("ScenicCityContinuation");
            cityscape.transform.SetParent(instance.root.transform, false);

            ScenicPrimitive("ScenicTerrain", PrimitiveType.Cube, cityscape.transform,
                new Vector3(chunkSize * .5f, -.46f, chunkSize * .5f), new Vector3(chunkSize, .16f, chunkSize),
                ScenicTerrainColor(baseChunk.terrainId));
            // This is presentation-owned bounded travel support, never a
            // saved plan or a substitute for a player-authored street.
            BuildScenicTravelSurface(cityscape.transform, chunkSize);

            CityForgeRoadClass avenue = ScenicRoadClass(baseChunk);
            BuildScenicAvenue(cityscape.transform, "ScenicAvenue_Centre_NS", new Vector3(chunkSize * .5f, -.29f, chunkSize * .5f), chunkSize, true, avenue);
            BuildScenicAvenue(cityscape.transform, "ScenicAvenue_Centre_EW", new Vector3(chunkSize * .5f, -.29f, chunkSize * .5f), chunkSize, false, avenue);
            // East/north ownership emits each shared boundary once.
            BuildScenicAvenue(cityscape.transform, "ScenicAvenue_East", new Vector3(chunkSize, -.29f, chunkSize * .5f), chunkSize, true, avenue);
            BuildScenicAvenue(cityscape.transform, "ScenicAvenue_North", new Vector3(chunkSize * .5f, -.29f, chunkSize), chunkSize, false, avenue);

            uint seed = StableHash(baseChunk.baseHash + "|scenic-city");
            Vector3[] blocks =
            {
                new Vector3(18f, 0f, 18f), new Vector3(54f, 0f, 18f),
                new Vector3(18f, 0f, 54f), new Vector3(54f, 0f, 54f)
            };
            for (int index = 0; index < blocks.Length; index++)
                BuildScenicBuilding(cityscape.transform, baseChunk, index, blocks[index]);

            BuildScenicLamp(cityscape.transform, "ScenicLamp_SW", new Vector3(31.7f, -.19f, 31.7f), seed + 1u);
            BuildScenicLamp(cityscape.transform, "ScenicLamp_SE", new Vector3(40.3f, -.19f, 31.7f), seed + 2u);
            BuildScenicLamp(cityscape.transform, "ScenicLamp_NW", new Vector3(31.7f, -.19f, 40.3f), seed + 3u);
            BuildScenicLamp(cityscape.transform, "ScenicLamp_NE", new Vector3(40.3f, -.19f, 40.3f), seed + 4u);
        }

        private static void BuildScenicAvenue(Transform parent, string name, Vector3 centre, float length, bool northSouth, CityForgeRoadClass roadClass)
        {
            Vector3 pavement = northSouth ? new Vector3(8.2f, .08f, length) : new Vector3(length, .08f, 8.2f);
            Vector3 asphalt = northSouth ? new Vector3(4.8f, .10f, length) : new Vector3(length, .10f, 4.8f);
            ScenicPrimitive(name + "_Footway", PrimitiveType.Cube, parent, centre, pavement, new Color(.29f, .31f, .30f));
            ScenicPrimitive(name + "_Asphalt", PrimitiveType.Cube, parent, centre + Vector3.up * .08f, asphalt, RoadColor(roadClass));
            Vector3 marking = northSouth ? new Vector3(.15f, .025f, length) : new Vector3(length, .025f, .15f);
            ScenicPrimitive(name + "_Marking", PrimitiveType.Cube, parent, centre + Vector3.up * .15f, marking, new Color(.76f, .68f, .43f), true);
        }

        private static void BuildScenicBuilding(Transform parent, CityForgeBaseChunk baseChunk, int index, Vector3 position)
        {
            uint seed = StableHash(baseChunk.baseHash + "|building|" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            float span = 24f - (float)(seed % 4u) * .7f;
            float height = ScenicBuildingHeight(baseChunk.skylineId, seed, index);
            GameObject shell = new GameObject("ScenicBuilding_" + index);
            shell.transform.SetParent(parent, false);
            shell.transform.localPosition = position;
            shell.transform.localRotation = Quaternion.Euler(0f, (float)((seed >> 3) % 4u) * 90f, 0f);
            string kit = BuildingKits[(int)(seed % (uint)BuildingKits.Length)];
            string facade = ScenicFacadeKits[(int)((seed >> 7) % (uint)ScenicFacadeKits.Length)];
            string roof = ScenicRoofKits[(int)((seed >> 13) % (uint)ScenicRoofKits.Length)];
            BuildBuildingShell(shell.transform, span, height, kit);
            ApplyBuildingFinish(shell.transform, facade, roof);
            BuildScenicWindowBands(shell.transform, span, height, seed);
            DisableScenicColliders(shell.transform);
        }

        private static float ScenicBuildingHeight(string skylineId, uint seed, int index)
        {
            if (skylineId == "tower-punctuated") return index == (int)(seed % 4u) ? 38f : 18f + (float)(seed % 4u) * 2.4f;
            if (skylineId == "mixed-rise") return 18f + (float)(seed % 6u) * 2.4f;
            if (skylineId == "civic-open") return 10f + (float)(seed % 4u) * 1.8f;
            return 12f + (float)(seed % 5u) * 1.9f;
        }

        private static void BuildScenicWindowBands(Transform parent, float span, float height, uint seed)
        {
            Color glass = Color.Lerp(new Color(.08f, .17f, .24f), new Color(.19f, .43f, .46f), (float)(seed & 3u) / 3f);
            float low = Mathf.Clamp(height * .34f, 3.1f, height - 2.4f);
            float high = Mathf.Clamp(height * .70f, low + 1.1f, height - 1.2f);
            foreach (float elevation in new[] { low, high })
            {
                ScenicPrimitive("ScenicWindowFront", PrimitiveType.Cube, parent, new Vector3(0f, elevation, -span * .5f - .14f), new Vector3(span * .66f, .34f, .08f), glass, true);
                ScenicPrimitive("ScenicWindowRear", PrimitiveType.Cube, parent, new Vector3(0f, elevation, span * .5f + .14f), new Vector3(span * .66f, .34f, .08f), glass, true);
                ScenicPrimitive("ScenicWindowWest", PrimitiveType.Cube, parent, new Vector3(-span * .5f - .14f, elevation, 0f), new Vector3(.08f, .34f, span * .66f), glass, true);
                ScenicPrimitive("ScenicWindowEast", PrimitiveType.Cube, parent, new Vector3(span * .5f + .14f, elevation, 0f), new Vector3(.08f, .34f, span * .66f), glass, true);
            }
        }

        private static void BuildScenicLamp(Transform parent, string name, Vector3 position, uint seed)
        {
            ScenicPrimitive(name + "_Pole", PrimitiveType.Cylinder, parent, position + Vector3.up * 2.0f,
                new Vector3(.13f, 2.0f, .13f), new Color(.16f, .20f, .22f));
            ScenicPrimitive(name + "_Head", PrimitiveType.Sphere, parent, position + Vector3.up * 4.05f, Vector3.one * .34f,
                Color.Lerp(new Color(.74f, .62f, .28f), new Color(.48f, .75f, .82f), (float)(seed & 1u)));
        }

        private static GameObject ScenicPrimitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color, bool disableShadow = false)
        {
            GameObject value = Primitive(name, type, parent, position, scale);
            Collider collider = value.GetComponent<Collider>(); if (collider) collider.enabled = false;
            Renderer renderer = value.GetComponent<Renderer>(); if (renderer) SetRendererColor(renderer, color);
            if (disableShadow) DisableShadow(value);
            return value;
        }

        private static void BuildScenicTravelSurface(Transform parent, float chunkSize)
        {
            GameObject surface = new GameObject("ScenicTravelSurface", typeof(BoxCollider));
            surface.transform.SetParent(parent, false);
            surface.transform.localPosition = new Vector3(chunkSize * .5f, -.16f, chunkSize * .5f);
            surface.GetComponent<BoxCollider>().size = new Vector3(chunkSize, .32f, chunkSize);
        }

        private static void DisableScenicColliders(Transform root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) if (collider) collider.enabled = false;
        }

        private static Color ScenicTerrainColor(string terrain)
        {
            if (terrain == "garden-grade") return new Color(.23f, .34f, .24f);
            if (terrain == "old-masonry") return new Color(.31f, .28f, .25f);
            if (terrain == "service-concrete") return new Color(.26f, .28f, .28f);
            return new Color(.22f, .27f, .29f);
        }

        private static CityForgeRoadClass ScenicRoadClass(CityForgeBaseChunk chunk)
        {
            if (chunk != null && chunk.skylineId == "tower-punctuated") return CityForgeRoadClass.Boulevard;
            if (chunk != null && chunk.lifeThemeId == "culture") return CityForgeRoadClass.PedestrianRoute;
            if (chunk != null && chunk.lifeThemeId == "repair") return CityForgeRoadClass.ServiceAlley;
            return CityForgeRoadClass.LocalStreet;
        }

        private void BuildGround(ChunkInstance instance, CityForgeResolvedChunk chunk)
        {
            List<int> cells = chunk?.acceptedPlan?.groundCells;
            if (cells == null || cells.Count == 0) return;
            float cell = SlopCityCityForge.CellSizeMeters;
            foreach (int item in cells.Where(value => value >= 0 && value < SlopCityCityForge.GridSize * SlopCityCityForge.GridSize).Distinct().OrderBy(value => value))
            {
                int x = item % SlopCityCityForge.GridSize, z = item / SlopCityCityForge.GridSize;
                // A player-authored ground cell must meet every neighbouring
                // ground cell flush.  The inset belongs only to the rock
                // underside, so a contiguous parcel has no artificial seam
                // or apparent no-build gap around its sites.
                Vector3 center = new Vector3((x + .5f) * cell, 0f, (z + .5f) * cell);
                GameObject top = Primitive("IslandTop_" + item, PrimitiveType.Cube, instance.root.transform,
                    center + Vector3.down * .16f, new Vector3(cell, .32f, cell));
                Renderer topRenderer = top.GetComponent<Renderer>();
                if (topRenderer) SetRendererColor(topRenderer, new Color(.31f, .52f, .34f));
                DisableShadow(top);

                GameObject underside = Primitive("IslandUnderside_" + item, PrimitiveType.Cube, instance.root.transform,
                    center + Vector3.down * .76f, new Vector3(cell - .92f, .88f, cell - .92f));
                Renderer undersideRenderer = underside.GetComponent<Renderer>();
                if (undersideRenderer) SetRendererColor(undersideRenderer, new Color(.13f, .18f, .24f));
                DisableShadow(underside);
            }
        }

        private void BuildEditorGround(Transform root, CityForgeChunkPlan draft)
        {
            foreach (int item in (draft.groundCells ?? new List<int>()).Where(cell => cell >= 0 && cell < SlopCityCityForge.GridSize * SlopCityCityForge.GridSize).Distinct().OrderBy(cell => cell))
            {
                int x = item % SlopCityCityForge.GridSize, z = item / SlopCityCityForge.GridSize;
                EditorPrimitive("DraftGround_" + item, PrimitiveType.Cube, root,
                    new Vector3((x + .5f) * SlopCityCityForge.CellSizeMeters, .305f, (z + .5f) * SlopCityCityForge.CellSizeMeters),
                    new Vector3(SlopCityCityForge.CellSizeMeters, .04f, SlopCityCityForge.CellSizeMeters), new Color(.62f, .68f, .58f));
            }
        }

        private void BuildRoads(ChunkInstance instance, IEnumerable<CityForgeRoad> roads)
        {
            int vehicles = 0;
            foreach (CityForgeRoad road in (roads ?? Enumerable.Empty<CityForgeRoad>()).Where(item => item != null).OrderBy(item => item.LocalKey, StringComparer.Ordinal))
            {
                // A road record names a grid edge, rather than a grid-cell
                // centre. Matching reciprocal boundary records therefore meet
                // at exactly the same physical line.
                Vector3 edge = RoadEdgeCentre(road);
                bool northSouth = road.direction == CityForgeDirection.North || road.direction == CityForgeDirection.South;
                Vector3 pavementSize = northSouth ? new Vector3(SlopCityCityForge.CellSizeMeters, .08f, 2.05f) : new Vector3(2.05f, .08f, SlopCityCityForge.CellSizeMeters);
                Vector3 stripSize = northSouth ? new Vector3(SlopCityCityForge.CellSizeMeters, .1f, 1.2f) : new Vector3(1.2f, .1f, SlopCityCityForge.CellSizeMeters);
                GameObject pavement = Primitive("Sidewalk_" + road.LocalKey, PrimitiveType.Cube, instance.root.transform, edge + Vector3.up * .015f, pavementSize);
                Renderer pavementRenderer = pavement.GetComponent<Renderer>(); if (pavementRenderer) SetRendererColor(pavementRenderer, new Color(.22f, .24f, .24f));
                GameObject strip = Primitive("Road_" + road.LocalKey, PrimitiveType.Cube, instance.root.transform, edge + Vector3.up * .075f, stripSize);
                Renderer stripRenderer = strip.GetComponent<Renderer>(); if (stripRenderer) SetRendererColor(stripRenderer, RoadColor(road.roadClass));
                Vector3 normal = RoadNormal(road.direction);
                for (int curb = -1; curb <= 1; curb += 2)
                {
                    Vector3 curbSize = northSouth ? new Vector3(SlopCityCityForge.CellSizeMeters, .14f, .12f) : new Vector3(.12f, .14f, SlopCityCityForge.CellSizeMeters);
                    GameObject edgeCurb = Primitive("Curb_" + road.LocalKey + "_" + curb, PrimitiveType.Cube, instance.root.transform, edge + normal * curb * 1.02f + Vector3.up * .11f, curbSize);
                    DisableShadow(edgeCurb);
                }
                DisableShadow(strip);
                // A small bounded set of actual drivable fixtures makes the
                // compiled boulevard/service graph usable by the existing
                // Sandbox car path, rather than treating roads as labels only.
                if (vehicles < 2 && !road.IsBoundary && road.roadClass != CityForgeRoadClass.PedestrianRoute && (StableHash(instance.coord.x + ":" + instance.coord.z + ":" + road.LocalKey) & 7u) == 0u)
                {
                    Vector3 tangent = northSouth ? Vector3.right : Vector3.forward;
                    GameObject car = Primitive("ForgeStreetCar_" + vehicles, PrimitiveType.Cube, instance.root.transform, edge + tangent * 1.7f + Vector3.up * .62f, new Vector3(1.65f, 1.05f, 3.45f));
                    car.transform.localRotation = Quaternion.LookRotation(tangent, Vector3.up);
                    SetRendererColor(car.GetComponent<Renderer>(), KitColor((int)(StableHash(road.LocalKey) % 30u), .38f, .56f));
                    AddFixture(car, "vehicle", "street-car", "City Forge street car", instance.coord);
                    vehicles++;
                }
            }
        }

        private void BuildBuildings(ChunkInstance instance, CityForgeResolvedChunk chunk)
        {
            if (chunk.acceptedPlan == null) return;
            foreach (CityForgeBuilding building in (chunk.acceptedPlan.buildings ?? new List<CityForgeBuilding>()).Where(item => item != null).OrderBy(item => item.id, StringComparer.Ordinal))
            {
                CityForgeParcel parcel = (chunk.acceptedPlan.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && item.id == building.parcelId);
                if (parcel == null || parcel.cells == null || parcel.cells.Count == 0) continue;
                Vector3 centre = ParcelCentre(parcel.cells);
                float span = Mathf.Clamp(Mathf.Sqrt(parcel.cells.Count) * SlopCityCityForge.CellSizeMeters * .75f, 4f, 18f);
                float height = Mathf.Clamp(building.floors, 1, 24) * 2.8f;
                GameObject shell = Primitive("Building_" + building.id, PrimitiveType.Cube, instance.root.transform,
                    new Vector3(centre.x, 0f, centre.z), Vector3.one);
                shell.transform.localRotation = Quaternion.Euler(0f, EntranceYaw(building.entranceEdge), 0f);
                Renderer shellRenderer = shell.GetComponent<Renderer>(); if (shellRenderer) shellRenderer.enabled = false;
                Collider shellCollider = shell.GetComponent<Collider>(); if (shellCollider) shellCollider.enabled = false;
                BuildBuildingShell(shell.transform, span, height, building.blueprintId);
                ApplyBuildingFinish(shell.transform, building.facadeId, building.roofId);
                // This is a ground-height opening in the front facade rather
                // than a child of a solid cube, so normal street raycasts reach
                // it before any structural wall collider.
                GameObject entry = Primitive("Entry_" + building.id, PrimitiveType.Cube, instance.root.transform,
                    instance.root.transform.InverseTransformPoint(shell.transform.TransformPoint(new Vector3(0f, 1.05f, -span * .5f - .14f))), new Vector3(1.45f, 2.1f, .18f));
                SlopCityCityForgeFixture entryFixture = AddFixture(entry, "interior", string.IsNullOrEmpty(building.interiorRole) ? building.blueprintId : building.interiorRole, "Enter " + building.blueprintId, instance.coord);
                Transform room = BuildInterior(instance, building, centre, span, entryFixture, entry.transform.position, shell.transform.localRotation);
                instance.hosts[building.id] = new ScreenHostLayout { exterior = shell.transform, interior = room, span = span, height = height, building = true, blueprintId = building.blueprintId };
                if (!string.IsNullOrEmpty(building.storefrontId) && building.storefrontId != "none")
                {
                    float canopy = building.storefrontId == "arcade" ? 1.8f : building.storefrontId == "market-stall" ? 2.2f : 1.2f;
                    GameObject frontage = Primitive("Storefront_" + building.storefrontId, PrimitiveType.Cube, shell.transform,
                        new Vector3(0f, 1.8f, -span * .5f - .12f), new Vector3(Mathf.Min(span, canopy * 2f), building.storefrontId == "glazed" ? 2.1f : .45f, .25f));
                    DisableShadow(frontage);
                }
            }
            foreach (CityForgeParcel parcel in (chunk.acceptedPlan.parcels ?? new List<CityForgeParcel>()).Where(item => item != null))
            {
                if (parcel.zone != CityForgeZone.Civic && parcel.zone != CityForgeZone.Culture && parcel.zone != CityForgeZone.GreenInfrastructure) continue;
                GameObject site = new GameObject("Site_" + parcel.id);
                site.transform.SetParent(instance.root.transform, false);
                site.transform.localPosition = ParcelCentre(parcel.cells);
                instance.hosts["site-" + parcel.id] = new ScreenHostLayout { exterior = site.transform, span = 4f, height = 4f, site = true };
            }
            foreach (CityForgeRoad road in chunk.roads ?? new List<CityForgeRoad>())
            {
                if (road == null) continue;
                string id = RoadHost(road);
                if (string.IsNullOrEmpty(id)) continue;
                GameObject host = new GameObject("RoadHost_" + id);
                host.transform.SetParent(instance.root.transform, false);
                Vector3 normal = RoadNormal(road.direction);
                Vector3 curbside = RoadEdgeCentre(road) + normal * 1.48f;
                host.transform.localPosition = curbside + Vector3.up * 2.15f;
                host.transform.localRotation = Quaternion.LookRotation(normal, Vector3.up);
                GameObject pole = Primitive("RoadKioskPole", PrimitiveType.Cylinder, instance.root.transform, curbside + Vector3.up * 1.05f, new Vector3(.18f, 1.05f, .18f));
                GameObject basePad = Primitive("RoadKioskPad", PrimitiveType.Cube, instance.root.transform, curbside + Vector3.up * .08f, new Vector3(.9f, .16f, .65f));
                DisableShadow(pole); DisableShadow(basePad);
                instance.hosts[id] = new ScreenHostLayout { exterior = host.transform, span = 2f, height = 3f, road = true };
            }
        }

        private static void BuildBuildingShell(Transform parent, float span, float height, string blueprintId)
        {
            float wall = .22f;
            float doorway = Mathf.Clamp(span * .28f, 1.6f, 2.4f);
            float sideWidth = Mathf.Max(.45f, (span - doorway) * .5f);
            // Side and rear walls retain collision; split front pieces and the
            // lintel leave a true catalog-derived doorway at street level.
            Primitive("WestWall", PrimitiveType.Cube, parent, new Vector3(-span * .5f, height * .5f, 0f), new Vector3(wall, height, span));
            Primitive("EastWall", PrimitiveType.Cube, parent, new Vector3(span * .5f, height * .5f, 0f), new Vector3(wall, height, span));
            Primitive("RearWall", PrimitiveType.Cube, parent, new Vector3(0f, height * .5f, span * .5f), new Vector3(span, height, wall));
            Primitive("FrontLeft", PrimitiveType.Cube, parent, new Vector3(-(doorway + sideWidth) * .5f, height * .5f, -span * .5f), new Vector3(sideWidth, height, wall));
            Primitive("FrontRight", PrimitiveType.Cube, parent, new Vector3((doorway + sideWidth) * .5f, height * .5f, -span * .5f), new Vector3(sideWidth, height, wall));
            Primitive("FrontLintel", PrimitiveType.Cube, parent, new Vector3(0f, Mathf.Max(2.25f, height - .55f), -span * .5f), new Vector3(doorway, Mathf.Min(1.1f, height * .35f), wall));
            Primitive("Roof_" + (blueprintId ?? "kit"), PrimitiveType.Cube, parent, new Vector3(0f, height + wall * .5f, 0f), new Vector3(span + wall, wall, span + wall));
            int style = BuildingVariant(blueprintId);
            BuildKitMassing(parent, span, height, style);
        }

        /// <summary>
        /// Catalog kit IDs deliberately produce recognizably different
        /// silhouettes, while plans remain IDs and dimensions rather than
        /// arbitrary player meshes or transforms.
        /// </summary>
        private static void BuildKitMassing(Transform parent, float span, float height, int style)
        {
            float roofY = height + .35f;
            switch (style % 5)
            {
                case 0:
                    Primitive("RoofObservatory", PrimitiveType.Cylinder, parent, new Vector3(0f, roofY + .35f, 0f), new Vector3(.78f, .52f, .78f));
                    Primitive("RoofLantern", PrimitiveType.Sphere, parent, new Vector3(0f, roofY + .92f, 0f), Vector3.one * .54f);
                    break;
                case 1:
                    Primitive("RoofSetback", PrimitiveType.Cube, parent, new Vector3(0f, roofY + 1.0f, 0f), new Vector3(Mathf.Max(2.2f, span * .56f), 1.9f, Mathf.Max(2.2f, span * .56f)));
                    Primitive("RoofCrown", PrimitiveType.Cylinder, parent, new Vector3(0f, roofY + 2.15f, 0f), new Vector3(.66f, .28f, .66f));
                    break;
                case 2:
                    float towerOffset = Mathf.Min(span * .22f, 2.3f);
                    Primitive("RoofTowerWest", PrimitiveType.Cube, parent, new Vector3(-towerOffset, roofY + 1.25f, .06f), new Vector3(Mathf.Max(1.15f, span * .26f), 2.4f, Mathf.Max(1.15f, span * .26f)));
                    Primitive("RoofTowerEast", PrimitiveType.Cube, parent, new Vector3(towerOffset, roofY + .92f, -.14f), new Vector3(Mathf.Max(1.1f, span * .23f), 1.75f, Mathf.Max(1.1f, span * .23f)));
                    break;
                case 3:
                    Primitive("StreetArcadeCanopy", PrimitiveType.Cube, parent, new Vector3(0f, 2.45f, -span * .5f - .66f), new Vector3(span + .85f, .22f, 1.35f));
                    Primitive("RoofServiceStack", PrimitiveType.Cube, parent, new Vector3(0f, roofY + .62f, span * .12f), new Vector3(Mathf.Max(1.8f, span * .38f), 1.1f, Mathf.Max(1.8f, span * .38f)));
                    break;
                default:
                    Primitive("RoofGardenTerrace", PrimitiveType.Cube, parent, new Vector3(0f, roofY + .20f, 0f), new Vector3(span * .72f, .30f, span * .72f));
                    Primitive("RoofWaterTank", PrimitiveType.Cylinder, parent, new Vector3(-Mathf.Min(span * .18f, 1.8f), roofY + .98f, 0f), new Vector3(.72f, .68f, .72f));
                    Primitive("RoofSolarRack", PrimitiveType.Cube, parent, new Vector3(Mathf.Min(span * .18f, 1.8f), roofY + .57f, 0f), new Vector3(Mathf.Max(1.4f, span * .28f), .22f, Mathf.Max(1.4f, span * .36f)));
                    break;
            }
        }

        private void BuildServices(ChunkInstance instance, CityForgeResolvedChunk chunk)
        {
            if (chunk.acceptedPlan == null) return;
            int count = 0;
            foreach (CityForgeService service in (chunk.acceptedPlan.services ?? new List<CityForgeService>()).Where(item => item != null).OrderBy(item => item.id, StringComparer.Ordinal))
            {
                if (count++ >= SlopCityCityForge.MaximumServiceRecords) break;
                CityForgeParcel parcel = (chunk.acceptedPlan.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && item.id == service.parcelId);
                if (parcel == null) continue;
                int variant = ServiceVariant(service.type);
                PrimitiveType shape = variant % 4 == 0 ? PrimitiveType.Cylinder : variant % 4 == 1 ? PrimitiveType.Cube : variant % 4 == 2 ? PrimitiveType.Sphere : PrimitiveType.Capsule;
                GameObject station = Primitive("Service_" + service.id, shape, instance.root.transform, ParcelCentre(parcel.cells) + new Vector3(0f, .85f, 0f), new Vector3(1.0f + (variant % 3) * .16f, .85f + (variant % 2) * .2f, 1.0f + (variant % 3) * .16f));
                Renderer renderer = station.GetComponent<Renderer>(); if (renderer) SetRendererColor(renderer, KitColor(variant, .48f, .72f));
                Primitive("ServiceAccent", variant % 2 == 0 ? PrimitiveType.Cube : PrimitiveType.Cylinder, station.transform, new Vector3(0f, 1.05f, 0f), new Vector3(.35f + (variant % 3) * .08f, .35f, .35f));
                AddFixture(station, ServiceFixtureKind(service.type), service.type, "Service " + service.type, instance.coord);
            }
        }

        private void BuildLandmarks(ChunkInstance instance, CityForgeResolvedChunk chunk)
        {
            if (chunk.acceptedPlan == null) return;
            foreach (CityForgeLandmark landmark in (chunk.acceptedPlan.landmarks ?? new List<CityForgeLandmark>()).Where(item => item != null).OrderBy(item => item.id, StringComparer.Ordinal))
            {
                CityForgeParcel parcel = (chunk.acceptedPlan.parcels ?? new List<CityForgeParcel>()).FirstOrDefault(item => item != null && item.id == landmark.parcelId);
                if (parcel == null) continue;
                int variant = OutcomeVariant(landmark.outcomeId);
                float height = 2.0f + (variant % 5) * .65f;
                PrimitiveType shape = variant % 5 == 0 ? PrimitiveType.Cylinder : variant % 5 == 1 ? PrimitiveType.Cube : variant % 5 == 2 ? PrimitiveType.Sphere : variant % 5 == 3 ? PrimitiveType.Capsule : PrimitiveType.Cylinder;
                GameObject monument = Primitive("Cityworks_" + landmark.id, shape, instance.root.transform, ParcelCentre(parcel.cells) + new Vector3(0f, height * .5f, 0f), new Vector3(1.1f + (variant % 4) * .24f, height * .5f, 1.1f + (variant % 4) * .24f));
                Renderer monumentRenderer = monument.GetComponent<Renderer>(); if (monumentRenderer) SetRendererColor(monumentRenderer, LandmarkStateColor(variant, landmark.state));
                BuildLandmarkAccent(monument.transform, variant, landmark.state);
                AddFixture(monument, "cityworks", landmark.outcomeId, "Cityworks " + landmark.state + " / " + landmark.outcomeId, instance.coord);
            }
        }

        private void BuildLife(ChunkInstance instance, CityForgeResolvedChunk chunk)
        {
            CityForgeCityPulse pulse = chunk.pulse ?? new CityForgeCityPulse();
            int resident = 0;
            foreach (string role in (pulse.residentRoles ?? new List<string>()).Where(item => !string.IsNullOrEmpty(item)).OrderBy(item => item, StringComparer.Ordinal).Take(8))
            {
                uint hash = StableHash(role + "|" + instance.coord);
                int variant = (int)(hash % 7u);
                GameObject marker = Primitive("Resident_" + resident++, PrimitiveType.Capsule, instance.root.transform, new Vector3(6f + hash % 60, 1f, 6f + (hash / 97u) % 60), new Vector3(.45f, 1f, .45f));
                Renderer residentRenderer = marker.GetComponent<Renderer>(); if (residentRenderer) SetRendererColor(residentRenderer, KitColor(variant + 7, .32f, .68f));
                Primitive("ResidentRoleMarker", variant % 2 == 0 ? PrimitiveType.Cube : PrimitiveType.Cylinder, marker.transform, new Vector3(0f, 1.15f, 0f), new Vector3(.24f, .16f, .24f));
                AddFixture(marker, "resident", role, role.Replace('-', ' '), instance.coord);
            }
            int persisted = 0;
            foreach (CityForgeSituationState record in (pulse.persistedSituations ?? new List<CityForgeSituationState>()).Where(item => item != null && item.state != "archived" && item.state != "unavailable").OrderBy(item => item.id, StringComparer.Ordinal).Take(12))
            {
                uint hash = StableHash(record.situationId + "|" + record.stableSeed + "|" + instance.coord);
                Vector3 position = new Vector3(5f + hash % 62, 1.05f, 5f + (hash / 131u) % 62);
                GameObject marker = SituationFixture("Situation_" + persisted++, instance.root.transform, position, record.situationId, record.state);
                SlopCityCityForgeFixture fixture = AddFixture(marker, "situation", record.situationId, "Situation " + record.state + " / " + record.situationId, instance.coord);
                if (fixture != null) persistedSituationFixtures[fixture] = record.Clone();
            }
            int situation = 0;
            foreach (CityForgeSituationOpportunity opportunity in (pulse.availableSituationOpportunities ?? new List<CityForgeSituationOpportunity>()).Where(item => item != null && !string.IsNullOrEmpty(item.situationId)).OrderBy(item => item.landmarkId, StringComparer.Ordinal).ThenBy(item => item.situationId, StringComparer.Ordinal).Take(12))
            {
                uint hash = StableHash(opportunity.situationId + "|" + opportunity.stableSeed + "|" + instance.coord);
                GameObject marker = SituationFixture("SituationOpportunity_" + situation++, instance.root.transform, new Vector3(5f + hash % 62, 1.05f, 5f + (hash / 131u) % 62), opportunity.situationId, "available");
                SlopCityCityForgeFixture fixture = AddFixture(marker, "situation", opportunity.situationId, "Situation available / " + opportunity.situationId, instance.coord);
                if (fixture != null) opportunitySituationFixtures[fixture] = opportunity.Clone();
            }
            GameObject pulseMarker = Primitive("CityPulse", PrimitiveType.Cube, instance.root.transform, new Vector3(4f, .35f, 4f), new Vector3(1.2f, .7f, 1.2f));
            AddFixture(pulseMarker, "pulse", "city-pulse", "Pulse A" + pulse.access + " C" + pulse.care + " U" + pulse.culture, instance.coord);
        }

        private Transform BuildInterior(ChunkInstance instance, CityForgeBuilding building, Vector3 parcelCentre, float span, SlopCityCityForgeFixture entry, Vector3 exteriorArrival, Quaternion orientation)
        {
            if (entry == null) return null;
            // A streamed interior is derived from the approved kit and parcel,
            // suspended directly above its shell so it has no dependence on an
            // authored scene path or a player-supplied transform.
            GameObject room = new GameObject("Interior_" + building.id); room.transform.SetParent(instance.root.transform, false);
            room.transform.localPosition = parcelCentre + new Vector3(0f, 14f, 0f);
            room.transform.localRotation = orientation;
            float width = Mathf.Clamp(span * .7f, 4f, 10f);
            Primitive("Floor", PrimitiveType.Cube, room.transform, Vector3.zero, new Vector3(width, .25f, width));
            Primitive("NorthWall", PrimitiveType.Cube, room.transform, new Vector3(0f, 2f, width * .5f), new Vector3(width, 4f, .2f));
            Primitive("SouthWall", PrimitiveType.Cube, room.transform, new Vector3(0f, 2f, -width * .5f), new Vector3(width, 4f, .2f));
            Primitive("EastWall", PrimitiveType.Cube, room.transform, new Vector3(width * .5f, 2f, 0f), new Vector3(.2f, 4f, width));
            Primitive("WestWall", PrimitiveType.Cube, room.transform, new Vector3(-width * .5f, 2f, 0f), new Vector3(.2f, 4f, width));
            BuildInteriorKit(room.transform, building.interiorRole, width);
            GameObject returnControl = Primitive("ReturnToStreet", PrimitiveType.Cylinder, room.transform, new Vector3(0f, .7f, width * .24f), new Vector3(.6f, .7f, .6f));
            SlopCityCityForgeFixture returnFixture = AddFixture(returnControl, "interior-return", "return-to-street", "Return to street", instance.coord);
            // Route both door controls through the game's standard interaction
            // and touch pipeline. The marker contains no player data: it is a
            // derived point inside this streamed public kit.
            GameObject interiorDestination = new GameObject("InteriorArrival"); interiorDestination.transform.SetParent(room.transform, false); interiorDestination.transform.localPosition = new Vector3(0f, 1.2f, -width * .22f);
            GameObject exteriorDestination = new GameObject("StreetArrival"); exteriorDestination.transform.SetParent(instance.root.transform, false); exteriorDestination.transform.localPosition = instance.root.transform.InverseTransformPoint(exteriorArrival);
            SlopCityInteractable entryControl = entry.GetComponent<SlopCityInteractable>(); if (entryControl) entryControl.ConfigureDestination(interiorDestination.transform);
            SlopCityInteractable returnControlInteraction = returnFixture ? returnFixture.GetComponent<SlopCityInteractable>() : null; if (returnControlInteraction) returnControlInteraction.ConfigureDestination(exteriorDestination.transform);
            InteriorPortal portal = new InteriorPortal
            {
                chunk = instance.coord,
                interiorPosition = room.transform.TransformPoint(new Vector3(0f, 1.2f, -width * .22f)),
                exteriorPosition = instance.root.transform.TransformPoint(parcelCentre + new Vector3(0f, 1.2f, -span * .72f))
            };
            interiorEntries[entry] = portal; interiorReturns[returnFixture] = portal;
            return room.transform;
        }

        private static void ApplyBuildingFinish(Transform root, string facadeId, string roofId)
        {
            Color facade = FacadeColor(facadeId);
            Color roof = RoofColor(roofId);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer) continue;
                SetRendererColor(renderer, renderer.name.StartsWith("Roof", StringComparison.Ordinal) ? roof : facade);
            }
        }

        private static void BuildInteriorKit(Transform room, string interiorRole, float width)
        {
            int variant = InteriorVariant(interiorRole);
            PrimitiveType primary = variant % 3 == 0 ? PrimitiveType.Cube : variant % 3 == 1 ? PrimitiveType.Cylinder : PrimitiveType.Sphere;
            GameObject furnishing = Primitive("InteriorKit_" + (interiorRole ?? "default"), primary, room, new Vector3(-width * .22f, .62f, 0f), new Vector3(1.1f + (variant % 3) * .2f, .85f, .9f));
            GameObject counter = Primitive("InteriorWorkSurface", PrimitiveType.Cube, room, new Vector3(width * .22f, .55f, 0f), new Vector3(1.4f, 1.1f, .62f));
            Renderer first = furnishing.GetComponent<Renderer>(); if (first) SetRendererColor(first, InteriorColor(interiorRole));
            Renderer second = counter.GetComponent<Renderer>(); if (second) SetRendererColor(second, Color.Lerp(InteriorColor(interiorRole), Color.white, .18f));
        }

        private static float EntranceYaw(CityForgeDirection edge)
        {
            if (edge == CityForgeDirection.North) return 180f;
            if (edge == CityForgeDirection.East) return -90f;
            if (edge == CityForgeDirection.West) return 90f;
            return 0f;
        }

        private SlopCityCityForgeFixture AddFixture(GameObject target, string kind, string catalogId, string label, CityForgeCoord coord)
        {
            if (!target) return null;
            SlopCityInteractable interactable = target.AddComponent<SlopCityInteractable>();
            interactable.Configure(InteractionKind(kind), label, false);
            interactable.ConfigureInteractionPoint(target.transform.position);
            SlopCityCityForgeFixture fixture = target.AddComponent<SlopCityCityForgeFixture>();
            fixture.Configure(kind, catalogId, label, coord);
            fixture.Interacted += HandleFixtureInteraction;
            if (target.transform.childCount < 2) CreateFixtureLabel(target.transform, label);
            return fixture;
        }

        private void HandleFixtureInteraction(SlopCityCityForgeFixture fixture)
        {
            // Door fixtures use SlopCityInteractable.ConfigureDestination(), so
            // keyboard and touch follow the shared first-person/Sandbox travel
            // path. This event is only the catalog-state hook on that action.
            if (fixture != null && persistedSituationFixtures.TryGetValue(fixture, out CityForgeSituationState situation))
            {
                CityForgeSituationState copy = situation.Clone();
                SituationInteractionRequested?.Invoke(copy.Clone());
                SituationTransitionRequested?.Invoke(fixture.Chunk, copy);
            }
            else if (fixture != null && opportunitySituationFixtures.TryGetValue(fixture, out CityForgeSituationOpportunity opportunity))
            {
                SituationOpportunityRequested?.Invoke(fixture.Chunk, opportunity.Clone());
            }
            FixtureInteracted?.Invoke(fixture);
        }

        private static GameObject SituationFixture(string name, Transform parent, Vector3 position, string situationId, string state)
        {
            // A small shipped kit vocabulary maps catalog IDs to visibly distinct
            // opportunities without accepting player geometry or text.
            int variant = SituationVariant(situationId);
            bool civic = situationId.Contains("care") || situationId.Contains("repair") || situationId.Contains("utility");
            bool culture = situationId.Contains("culture") || situationId.Contains("screen") || situationId.Contains("archive");
            PrimitiveType shape = civic ? PrimitiveType.Cylinder : culture ? PrimitiveType.Cube : PrimitiveType.Sphere;
            float scale = (state == "active" ? 1.15f : state == "paused" ? .75f : .95f) + (variant % 3) * .04f;
            GameObject root = Primitive(name, shape, parent, position, new Vector3(scale, civic ? 1.2f : scale, scale));
            Renderer renderer = root.GetComponent<Renderer>(); if (renderer) SetRendererColor(renderer, LandmarkStateColor(variant, state));
            if (state == "active")
            {
                GameObject beacon = Primitive("ActiveBeacon", PrimitiveType.Cylinder, root.transform, new Vector3(0f, 1.25f, 0f), new Vector3(.16f, .5f, .16f));
                DisableShadow(beacon);
            }
            return root;
        }

        private static SlopCityInteractionKind InteractionKind(string kind)
        {
            if (kind == "interior" || kind == "interior-return") return SlopCityInteractionKind.Building;
            if (kind == "service" || kind == "service-repair" || kind == "service-care" || kind == "cityworks" || kind == "situation") return SlopCityInteractionKind.Terminal;
            if (kind == "service-water") return SlopCityInteractionKind.Fountain;
            if (kind == "service-food") return SlopCityInteractionKind.Vending;
            if (kind == "service-garden") return SlopCityInteractionKind.Plant;
            if (kind == "vehicle") return SlopCityInteractionKind.Car;
            if (kind == "resident") return SlopCityInteractionKind.Bench;
            if (kind == "pulse") return SlopCityInteractionKind.Light;
            return SlopCityInteractionKind.Inspect;
        }

        private static void CreateFixtureLabel(Transform parent, string label)
        {
            GameObject item = new GameObject("Label", typeof(TextMesh)); item.transform.SetParent(parent, false); item.transform.localPosition = new Vector3(0f, 1.4f, 0f); item.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            TextMesh text = item.GetComponent<TextMesh>(); text.text = label; text.characterSize = .12f; text.fontSize = 28; text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
        }

        private void BuildScreens(ChunkInstance instance, CityForgeResolvedChunk chunk)
        {
            foreach (CityForgeResolvedScreen resolved in (chunk.screens ?? new List<CityForgeResolvedScreen>()).Where(item => item != null).OrderBy(item => item.screenId, StringComparer.Ordinal))
            {
                if (!instance.hosts.TryGetValue(resolved.hostId ?? string.Empty, out ScreenHostLayout host)
                    || !TryScreenPlacement(host, resolved, out ScreenPlacement placement)) continue;
                GameObject support = new GameObject("ScreenSupport_" + resolved.screenId);
                support.transform.SetParent(placement.parent, false);
                support.transform.localPosition = placement.localPosition;
                support.transform.localRotation = placement.localRotation;
                BuildScreenBezel(support.transform, placement.scale, resolved.deviceId);
                GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
                surface.name = "CityForgeScreen_" + resolved.screenId;
                surface.transform.SetParent(support.transform, false);
                // CityScreen fronts point along local -Z, so the media quad
                // sits in front of its backing/bars rather than behind them.
                surface.transform.localPosition = Vector3.back * .06f;
                surface.transform.localRotation = Quaternion.identity;
                surface.transform.localScale = placement.scale;
                // The parent root is inactive while components are added. Set this
                // before first OnEnable so future screen types can never default
                // into the media registry during a streamed construction frame.
                SlopCityCityScreen screen = surface.AddComponent<SlopCityCityScreen>();
                screen.SetMediaScope(SlopCityScreenMediaScope.PlayerSprawl);
                screen.ConfigureScreenGroup(resolved.screenGroup);
                screen.Configure(surface.GetComponent<Renderer>(), StableChannel(resolved.screenId), resolved.screenGroup == "interior" ? 10f : 16f);
                screen.SetMediaAdmission(false);
                ScreenFixture fixture = new ScreenFixture { screenId = resolved.screenId, admissionId = AdmissionId(instance.coord, resolved.screenId), screen = screen, chunk = instance.coord, root = surface, support = support.transform, exterior = placement.exterior, group = resolved.screenGroup ?? string.Empty };
                instance.fixtures.Add(fixture);
                fixtures[screen] = fixture;
            }
        }

        private static bool TryScreenPlacement(ScreenHostLayout host, CityForgeResolvedScreen screen, out ScreenPlacement placement)
        {
            placement = default(ScreenPlacement);
            if (host == null || host.exterior == null || screen == null) return false;
            int count = Mathf.Max(1, screen.slotCount);
            int index = Mathf.Clamp(screen.slotIndex, 0, count - 1);
            SlotGrid(index, count, out int column, out int row, out int columns);
            bool wantsInterior = !screen.catalogIntegrated && screen.mountId == "interior-wall-tv";
            bool catalogInterior = screen.catalogIntegrated && string.Equals(screen.screenGroup, "interior", StringComparison.Ordinal);
            if (host.building && (wantsInterior || catalogInterior) && host.interior != null)
            {
                float width = Mathf.Clamp(host.span * .7f, 4f, 10f);
                float spacing = Mathf.Min(2.1f, width / Mathf.Max(2, columns + 1));
                float x = (column - (columns - 1) * .5f) * spacing;
                bool oppositeWall = screen.catalogIntegrated;
                placement.parent = host.interior;
                placement.localPosition = new Vector3(x, 2.0f + row * 1.35f, oppositeWall ? -width * .5f + .14f : width * .5f - .14f);
                // CityScreen's authored front normal is local -Z. The north-wall
                // player fixture therefore faces into the room (-Z), while the
                // catalog fallback on the south wall faces north (+Z).
                placement.localRotation = oppositeWall ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                placement.scale = new Vector3(Mathf.Min(1.5f, spacing * .78f), 1f, 1f);
                placement.exterior = false;
                return true;
            }
            if (host.building)
            {
                float spacing = Mathf.Min(2.6f, host.span / Mathf.Max(2, columns + 1));
                float x = (column - (columns - 1) * .5f) * spacing;
                bool rearFacade = screen.catalogIntegrated;
                placement.parent = host.exterior;
                placement.localPosition = new Vector3(x, Mathf.Min(host.height - 1.5f, 3.1f + row * 1.55f), rearFacade ? host.span * .5f + .08f : -host.span * .5f - .08f);
                // The player facade is outside the front (-Z) wall, so its
                // local -Z front already faces the street. Fallbacks use the
                // opposite facade and rotate to face +Z.
                placement.localRotation = rearFacade ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                placement.scale = new Vector3(Mathf.Min(2.4f, spacing * .82f), 1.35f, 1f);
                placement.exterior = true;
                return true;
            }
            if (host.site)
            {
                placement.parent = host.exterior;
                placement.localPosition = new Vector3(0f, 2.6f, 0f);
                placement.localRotation = Quaternion.identity;
                placement.scale = new Vector3(2.1f, 1.35f, 1f);
                placement.exterior = true;
                return true;
            }
            if (host.road)
            {
                placement.parent = host.exterior;
                placement.localPosition = Vector3.zero;
                placement.localRotation = Quaternion.identity;
                placement.scale = new Vector3(1.35f, .9f, 1f);
                placement.exterior = true;
                return true;
            }
            return false;
        }

        private static void SlotGrid(int index, int count, out int column, out int row, out int columns)
        {
            columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, count))), 1, 3);
            column = index % columns;
            row = index / columns;
        }

        private static void BuildScreenBezel(Transform support, Vector3 screenScale, string deviceId)
        {
            float width = screenScale.x, height = screenScale.y;
            const float thickness = .09f;
            Color color = DeviceColor(deviceId);
            GameObject[] bars =
            {
                Primitive("ScreenFrameTop", PrimitiveType.Cube, support, new Vector3(0f, height * .5f + thickness, 0f), new Vector3(width + thickness * 2f, thickness * 2f, thickness)),
                Primitive("ScreenFrameBottom", PrimitiveType.Cube, support, new Vector3(0f, -height * .5f - thickness, 0f), new Vector3(width + thickness * 2f, thickness * 2f, thickness)),
                Primitive("ScreenFrameLeft", PrimitiveType.Cube, support, new Vector3(-width * .5f - thickness, 0f, 0f), new Vector3(thickness * 2f, height, thickness)),
                Primitive("ScreenFrameRight", PrimitiveType.Cube, support, new Vector3(width * .5f + thickness, 0f, 0f), new Vector3(thickness * 2f, height, thickness))
            };
            foreach (GameObject bar in bars)
            {
                Renderer renderer = bar.GetComponent<Renderer>(); if (renderer) SetRendererColor(renderer, color);
            }
        }

        private void RefreshMediaAdmission(bool immediate)
        {
            if (!immediate && Time.unscaledTime < nextAdmission) return;
            if (immediate) nextAdmission = Time.unscaledTime + Mathf.Clamp(admissionInterval, .1f, 1f);
            Transform view = listener;
            if (!view && Camera.main) view = Camera.main.transform;
            candidateBuffer.Clear();
            admittedIds.Clear();
            if (view)
            {
                foreach (ScreenFixture fixture in fixtures.Values.ToArray())
                {
                    if (fixture == null || !fixture.screen || !fixture.root) continue;
                    Vector3 point = fixture.screen.Display ? fixture.screen.Display.bounds.center : fixture.root.transform.position;
                    Vector3 to = point - view.position;
                    float distance = to.magnitude;
                    Vector3 direction = distance > .01f ? to / distance : view.forward;
                    bool visible = Vector3.Dot(view.forward, direction) > .05f;
                    bool facing = Vector3.Dot(fixture.screen.FrontNormal, -direction) > .08f;
                    candidateBuffer.Add(new SlopCityCityForgeMediaAdmission.Candidate
                    {
                        screenId = fixture.admissionId,
                        chunkLoaded = chunks.ContainsKey(fixture.chunk),
                        visible = visible,
                        eligible = distance <= screenViewRange,
                        unobstructed = visible && IsUnobstructed(view.position, point, fixture),
                        distance = distance,
                        facing = facing ? Vector3.Dot(fixture.screen.FrontNormal, -direction) : -1f
                    });
                }
                foreach (SlopCityCityForgeMediaAdmission.Candidate winner in SlopCityCityForgeMediaAdmission.Admit(candidateBuffer, constrainedWebGl)) admittedIds.Add(winner.screenId);
            }
            foreach (ScreenFixture fixture in fixtures.Values.ToArray())
                if (fixture != null && fixture.screen) fixture.screen.SetMediaAdmission(admittedIds.Contains(fixture.admissionId));
        }

        private static bool IsUnobstructed(Vector3 from, Vector3 target, ScreenFixture fixture)
        {
            if (fixture == null || !fixture.root) return false;
            Vector3 line = target - from;
            float distance = line.magnitude;
            if (distance <= .01f) return true;
            if (!Physics.Raycast(from, line / distance, out RaycastHit hit, distance - .02f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
            if (hit.transform == fixture.root.transform || hit.transform.IsChildOf(fixture.root.transform)) return true;
            // A derived mount frame is part of this fixture, not a building-wide
            // exemption. Exterior targets are physically placed beyond their
            // facade, so allowing only that support cannot tunnel through walls.
            return fixture.support != null && (hit.transform == fixture.support || hit.transform.IsChildOf(fixture.support));
        }

        private void Revoke(ChunkInstance instance)
        {
            if (instance == null) return;
            foreach (SlopCityCityForgeFixture fixture in interiorEntries.Keys.Where(item => item == null || interiorEntries[item].chunk.Equals(instance.coord)).ToArray()) interiorEntries.Remove(fixture);
            foreach (SlopCityCityForgeFixture fixture in interiorReturns.Keys.Where(item => item == null || interiorReturns[item].chunk.Equals(instance.coord)).ToArray()) interiorReturns.Remove(fixture);
            foreach (SlopCityCityForgeFixture fixture in persistedSituationFixtures.Keys.Where(item => item == null || item.Chunk.Equals(instance.coord)).ToArray()) persistedSituationFixtures.Remove(fixture);
            foreach (SlopCityCityForgeFixture fixture in opportunitySituationFixtures.Keys.Where(item => item == null || item.Chunk.Equals(instance.coord)).ToArray()) opportunitySituationFixtures.Remove(fixture);
            foreach (ScreenFixture fixture in instance.fixtures)
            {
                if (fixture == null || !fixture.screen) continue;
                fixture.screen.SetMediaAdmission(false);
                if (SlopCityCityMedia.Instance) SlopCityCityMedia.Instance.UnregisterScreen(fixture.screen);
                fixtures.Remove(fixture.screen);
            }
            instance.fixtures.Clear();
        }

        private void RevokeAllMedia()
        {
            foreach (ScreenFixture fixture in fixtures.Values.ToArray()) if (fixture != null && fixture.screen) fixture.screen.SetMediaAdmission(false);
            admittedIds.Clear();
        }

        private static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale)
        {
            GameObject value = GameObject.CreatePrimitive(type);
            value.name = name;
            value.transform.SetParent(parent, false);
            value.transform.localPosition = localPosition;
            value.transform.localScale = localScale;
            return value;
        }

        private static void DisableShadow(GameObject value)
        {
            Renderer renderer = value ? value.GetComponent<Renderer>() : null;
            if (!renderer) return;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        /// <summary>Colors Unity primitive renderers without cloning their shared material per streamed fixture.</summary>
        private static void SetRendererColor(Renderer renderer, Color color)
        {
            if (!renderer) return;
            Material shared = renderer.sharedMaterial;
            if (shared == null) return;
            colorProperties.Clear();
            renderer.GetPropertyBlock(colorProperties);
            if (shared.HasProperty("_Color")) colorProperties.SetColor("_Color", color);
            if (shared.HasProperty("_BaseColor")) colorProperties.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(colorProperties);
        }

        private static void SetEditorRendererColor(Renderer renderer, Color color)
        {
            if (!renderer) return;
            Material shared = renderer.sharedMaterial;
            if (shared == null) return;
            colorProperties.Clear();
            renderer.GetPropertyBlock(colorProperties);
            if (shared.HasProperty("_Color")) colorProperties.SetColor("_Color", color);
            if (shared.HasProperty("_BaseColor")) colorProperties.SetColor("_BaseColor", color);
            if (shared.HasProperty("_EmissionColor"))
                colorProperties.SetColor("_EmissionColor", new Color(color.r * 1.45f, color.g * 1.45f, color.b * 1.45f, 1f));
            renderer.SetPropertyBlock(colorProperties);
        }
        private static Vector3 ChunkOrigin(CityForgeCoord coord) { return new Vector3(coord.x * SlopCityCityForge.ChunkSizeMeters, 0f, coord.z * SlopCityCityForge.ChunkSizeMeters); }
        private static Vector3 ParcelCentre(IEnumerable<int> cells)
        {
            List<int> values = (cells ?? Enumerable.Empty<int>()).Where(value => value >= 0 && value < 81).ToList();
            if (values.Count == 0) return new Vector3(36f, 0f, 36f);
            return new Vector3(values.Average(value => (value % 9 + .5f) * SlopCityCityForge.CellSizeMeters), 0f, values.Average(value => (value / 9 + .5f) * SlopCityCityForge.CellSizeMeters));
        }
        private static Vector3 RoadEdgeCentre(CityForgeRoad road)
        {
            float cell = SlopCityCityForge.CellSizeMeters;
            float x = (road.cellX + .5f) * cell;
            float z = (road.cellZ + .5f) * cell;
            if (road.direction == CityForgeDirection.North) z = (road.cellZ + 1) * cell;
            else if (road.direction == CityForgeDirection.South) z = road.cellZ * cell;
            else if (road.direction == CityForgeDirection.East) x = (road.cellX + 1) * cell;
            else if (road.direction == CityForgeDirection.West) x = road.cellX * cell;
            return new Vector3(x, 0f, z);
        }
        private static Vector3 RoadNormal(CityForgeDirection direction)
        {
            if (direction == CityForgeDirection.North) return Vector3.forward;
            if (direction == CityForgeDirection.South) return Vector3.back;
            if (direction == CityForgeDirection.East) return Vector3.right;
            return Vector3.left;
        }
        private static Color RoadColor(CityForgeRoadClass roadClass)
        {
            if (roadClass == CityForgeRoadClass.Boulevard) return new Color(.12f, .14f, .16f);
            if (roadClass == CityForgeRoadClass.TransitServiceCorridor) return new Color(.15f, .17f, .19f);
            if (roadClass == CityForgeRoadClass.PedestrianRoute) return new Color(.34f, .30f, .24f);
            if (roadClass == CityForgeRoadClass.ServiceAlley) return new Color(.25f, .26f, .25f);
            return new Color(.19f, .20f, .20f);
        }
        private static int StableChannel(string id) { return (int)(StableHash(id) % 3u); }
        // These maps deliberately enumerate only the reviewed catalog IDs. They
        // are a presentation lookup, not a place where plan input can name an
        // arbitrary prefab, material, transform, or behavior.
        private static readonly string[] BuildingKits = { "walk-up", "courtyard-home", "tower-residence", "live-work-block", "workshop", "repair-bay", "clinic", "laundry", "depot", "small-office", "corner-shop", "market-hall", "cafe", "exchange", "kiosk", "care-station", "library-room", "utility-office", "community-hall", "screening-room", "studio", "venue", "archive", "performance-space", "garden", "plaza", "water-node", "power-relay", "shelter", "service-yard" };
        // Scenic continuation chooses only the same fixed catalog vocabulary as
        // accepted buildings; no player input reaches these presentation IDs.
        private static readonly string[] ScenicFacadeKits = { "wet-masonry", "poured-concrete", "glazed-ceramic", "oxidized-metal", "painted-render", "brick-utility", "night-glass", "garden-lattice" };
        private static readonly string[] ScenicRoofKits = { "service-roof", "rain-garden", "solar-rack", "water-tank", "relay-array", "lantern-roof", "terrace", "greenhouse", "vent-stack", "civic-canopy", "screen-crown", "utility-bridge" };
        private static readonly string[] InteriorKits = { "home-room", "workshop-floor", "repair-counter", "care-room", "laundry-room", "depot-bay", "office-floor", "shop-floor", "market-floor", "cafe-room", "exchange-room", "library-room", "community-room", "screening-room", "studio-room", "venue-room", "archive-room", "performance-room", "utility-room", "shelter-room" };
        private static readonly string[] ServiceKits = { "care-station", "repair-counter", "laundry-service", "delivery-bay", "neighborhood-office", "market-stall", "food-counter", "exchange-desk", "library-desk", "utility-desk", "community-room", "screening-service", "studio-service", "venue-door", "archive-desk", "garden-steward", "water-service", "power-service", "shelter-desk", "service-yard-bay" };
        private static readonly string[] OutcomeKits = { "repair-loft", "listening-room", "green-refuge", "repair-cooperative", "mutual-aid-washhouse", "after-hours-room", "light-sanctuary", "street-garden", "notice-arcade", "rider-support-post", "small-exhibition-platform", "connector-lounge", "water-garden", "relay-workshop", "observation-room", "used-goods-exchange", "neighborhood-kitchen", "community-studio", "machine-bay", "supply-cache", "mural-passage", "local-archive", "screening-club", "quiet-learning-room", "welcome-desk", "care-station-outcome", "night-office", "signal-workshop", "rest-terrace", "weather-shelter" };
        private static int BuildingVariant(string id) { int value = Array.IndexOf(BuildingKits, id ?? string.Empty); return value < 0 ? 0 : value; }
        private static int InteriorVariant(string id) { int value = Array.IndexOf(InteriorKits, id ?? string.Empty); return value < 0 ? 0 : value; }
        private static int ServiceVariant(string id) { int value = Array.IndexOf(ServiceKits, id ?? string.Empty); return value < 0 ? 0 : value; }
        private static int OutcomeVariant(string id) { int value = Array.IndexOf(OutcomeKits, id ?? string.Empty); return value < 0 ? 0 : value; }
        private static int SituationVariant(string id)
        {
            for (int index = 0; index < OutcomeKits.Length; index++) if (!string.IsNullOrEmpty(id) && id.StartsWith(OutcomeKits[index], StringComparison.Ordinal)) return index;
            return (int)(StableHash(id) % 30u);
        }
        private static Color KitColor(int variant, float saturation = .42f, float value = .72f) { return Color.HSVToRGB((variant % 30) / 30f, saturation, value); }
        private static Color FacadeColor(string id)
        {
            switch (id)
            {
                case "poured-concrete": return new Color(.42f, .44f, .43f);
                case "glazed-ceramic": return new Color(.24f, .48f, .51f);
                case "oxidized-metal": return new Color(.27f, .48f, .42f);
                case "painted-render": return new Color(.62f, .43f, .36f);
                case "brick-utility": return new Color(.47f, .23f, .19f);
                case "night-glass": return new Color(.10f, .19f, .30f);
                case "garden-lattice": return new Color(.31f, .52f, .31f);
                default: return new Color(.40f, .35f, .30f);
            }
        }
        private static Color RoofColor(string id)
        {
            switch (id)
            {
                case "rain-garden": return new Color(.20f, .39f, .22f);
                case "solar-rack": return new Color(.12f, .20f, .31f);
                case "water-tank": return new Color(.18f, .35f, .48f);
                case "relay-array": return new Color(.36f, .31f, .49f);
                case "lantern-roof": return new Color(.63f, .46f, .20f);
                case "terrace": return new Color(.42f, .34f, .27f);
                case "greenhouse": return new Color(.27f, .51f, .44f);
                case "vent-stack": return new Color(.34f, .37f, .36f);
                case "civic-canopy": return new Color(.33f, .42f, .54f);
                case "screen-crown": return new Color(.42f, .20f, .45f);
                case "utility-bridge": return new Color(.23f, .29f, .31f);
                default: return new Color(.30f, .31f, .30f);
            }
        }
        private static Color InteriorColor(string id) { return KitColor(InteriorVariant(id), .30f, .64f); }
        private static string ServiceFixtureKind(string type)
        {
            if (type == "water-service") return "service-water";
            if (type == "food-counter" || type == "market-stall") return "service-food";
            if (type == "garden-steward") return "service-garden";
            if (type.Contains("repair") || type.Contains("power") || type.Contains("utility")) return "service-repair";
            if (type.Contains("care") || type.Contains("laundry") || type.Contains("shelter")) return "service-care";
            return "service";
        }
        private static Color LandmarkStateColor(int variant, string state)
        {
            Color baseColor = KitColor(variant, .48f, .76f);
            if (state == "active" || state == "opening" || state == "established") return Color.Lerp(baseColor, Color.white, .22f);
            if (state == "paused") return Color.Lerp(baseColor, Color.gray, .58f);
            if (state == "resolved" || state == "archived") return Color.Lerp(baseColor, Color.black, .35f);
            return Color.Lerp(baseColor, Color.gray, .24f);
        }
        private static void BuildLandmarkAccent(Transform root, int variant, string state)
        {
            int count = 1 + variant % 3;
            for (int index = 0; index < count; index++)
            {
                float angle = 360f * index / count;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (.75f + (variant % 3) * .14f);
                GameObject accent = Primitive("OutcomeAccent_" + index, variant % 2 == 0 ? PrimitiveType.Cube : PrimitiveType.Cylinder, root, offset + Vector3.up * .35f, new Vector3(.22f, .7f, .22f));
                Renderer renderer = accent.GetComponent<Renderer>(); if (renderer) SetRendererColor(renderer, LandmarkStateColor(variant + index, state));
            }
        }
        private static Color DeviceColor(string id)
        {
            if (id == "public-led-01") return new Color(.14f, .24f, .3f);
            if (id == "public-projector-01") return new Color(.27f, .25f, .18f);
            if (id == "public-window-01") return new Color(.12f, .28f, .24f);
            return new Color(.11f, .11f, .13f);
        }
        private static Color SkylineColor(string id)
        {
            uint hash = StableHash(id); return new Color(.08f + (hash & 31) / 255f, .12f + ((hash >> 5) & 31) / 255f, .14f + ((hash >> 10) & 31) / 255f, .82f);
        }
        private static uint StableHash(string value)
        {
            uint hash = 2166136261u;
            foreach (byte valueByte in System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty)) { hash ^= valueByte; hash *= 16777619u; }
            return hash;
        }
        private static string RoadHost(CityForgeRoad road)
        {
            string direction = road.direction == CityForgeDirection.North ? "north" : road.direction == CityForgeDirection.East ? "east" : road.direction == CityForgeDirection.South ? "south" : "west";
            return "road-" + road.cellX + "-" + road.cellZ + "-" + direction;
        }
        private static string AdmissionId(CityForgeCoord coord, string screenId) { return coord.x + ":" + coord.z + ":" + (screenId ?? string.Empty); }

        private sealed class ChunkInstance
        {
            public CityForgeCoord coord;
            public GameObject root;
            public string resolvedHash;
            public readonly Dictionary<string, ScreenHostLayout> hosts = new Dictionary<string, ScreenHostLayout>(StringComparer.Ordinal);
            public readonly List<ScreenFixture> fixtures = new List<ScreenFixture>();
        }
        private sealed class ScreenHostLayout
        {
            public Transform exterior;
            public Transform interior;
            public float span;
            public float height;
            public bool building;
            public bool site;
            public bool road;
            public string blueprintId;
        }
        private sealed class ScreenFixture
        {
            public string screenId;
            public string admissionId;
            public SlopCityCityScreen screen;
            public CityForgeCoord chunk;
            public GameObject root;
            public Transform support;
            public bool exterior;
            public string group;
        }
        private struct ScreenPlacement
        {
            public Transform parent;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 scale;
            public bool exterior;
        }
        private struct InteriorPortal
        {
            public CityForgeCoord chunk;
            public Vector3 interiorPosition;
            public Vector3 exteriorPosition;
        }
    }

    /// <summary>Bounded physical interaction marker derived exclusively from City Forge catalog state.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityForgeFixture : MonoBehaviour
    {
        [SerializeField] private string fixtureKind = string.Empty;
        [SerializeField] private string catalogId = string.Empty;
        [SerializeField] private string displayLabel = string.Empty;
        [SerializeField] private CityForgeCoord chunk;
        private SlopCityInteractable interactable;
        private int observedServiceCount;
        private float lastDispatchAt = -10f;
        public string FixtureKind { get { return fixtureKind; } }
        public string CatalogId { get { return catalogId; } }
        public string DisplayLabel { get { return displayLabel; } }
        public CityForgeCoord Chunk { get { return chunk; } }
        public event Action<SlopCityCityForgeFixture> Interacted;

        public void Configure(string kind, string id, string label, CityForgeCoord coordinate)
        {
            fixtureKind = kind ?? string.Empty; catalogId = id ?? string.Empty; displayLabel = label ?? string.Empty; chunk = coordinate;
            interactable = GetComponent<SlopCityInteractable>();
            observedServiceCount = interactable ? interactable.ServiceCount : 0;
        }

        /// <summary>Updates only catalog-derived runtime presentation; no plan text or arbitrary content is accepted.</summary>
        public void UpdatePresentation(string id, string label)
        {
            catalogId = id ?? string.Empty;
            displayLabel = label ?? string.Empty;
            foreach (TextMesh text in GetComponentsInChildren<TextMesh>(true)) text.text = displayLabel;
        }

        public void TryInteract()
        {
            if (Time.unscaledTime - lastDispatchAt < .03f) return;
            lastDispatchAt = Time.unscaledTime;
            Interacted?.Invoke(this);
        }
        private void LateUpdate()
        {
            if (!interactable) interactable = GetComponent<SlopCityInteractable>();
            if (!interactable) return;
            int current = interactable.ServiceCount;
            if (current <= observedServiceCount) return;
            observedServiceCount = current;
            TryInteract();
        }
    }

}
