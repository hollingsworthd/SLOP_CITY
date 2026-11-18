import {createHash} from 'node:crypto';
import {readFileSync} from 'node:fs';
import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {
  CITY_FORGE_BUILDINGS, CITY_FORGE_BUILDING_SCREEN_SLOTS, CITY_FORGE_CATALOG_HASH, CITY_FORGE_FACADES, CITY_FORGE_GENERATOR_VERSION, CITY_FORGE_LANDMARKS, CITY_FORGE_OLD_CITY_CONNECTION_CLASS, CITY_FORGE_ROOFS, CITY_FORGE_SITUATIONS,
  CITY_FORGE_SCREEN_MOUNTS, CITY_FORGE_CHUNK_CELLS, canonicalBoundaryEdgeKey,
  canTransitionCityworks, canTransitionSituation, canonicalJson, chunkMapRoot, chunkPlanHash, cityForgeCatalogFingerprintInput, generateBaseChunk, isOldCityCoordinate,
  isStarterRingCoordinate, oldCityConnectionStub, resolveCityPulse, validateBoundarySymmetry,
  screenSlotAssignments, validateChunkPlan, validatePlanTransitions, validateScreenHost, validateWorldHead,
} from '../contracts/city-forge.mjs';

const hash = 'a'.repeat(64);
const head = () => ({
  schemaVersion: 2, catalogHash: hash, worldSeed: '0123456789abcdef', generatorVersion: CITY_FORGE_GENERATOR_VERSION,
  worldRevision: 1, chunkMapRoot: 'b'.repeat(64), grid: { chunkCells: 9, cellMeters: 8 },
});
const plan = (x = 2, z = 0) => ({
  coord: { x, z }, baseChunkHash: hash, parentChunkRevision: 0,
  roads: [{ edge: '0,0:north', class: 'local-street' }],
  removedRoadEdges: [],
  groundCells: [0, 1, 9],
  parcels: [{ id: 'parcel-a', cells: ['0,0', '1,0'], zone: 'trade' }],
  buildings: [{ id: 'building-a', parcelId: 'parcel-a', blueprintId: 'corner-shop', facadeId: 'wet-masonry', roofId: 'service-roof', floors: 2, entranceEdge: 'north', storefrontId: 'arcade' }],
  services: [{ id: 'service-a', type: 'care-station', parcelId: 'parcel-a' }],
  screens: [{ screenId: 'screen-a', hostId: 'building-a', screenMountId: 'facade-media-wall', deviceId: 'public-crt-01' }],
  landmarks: [{ id: 'landmark-a', classId: 'tradefront', outcomeId: 'used-goods-exchange', parcelId: 'parcel-a', state: 'established' }],
  situations: [{ id: 'situation-a', situationId: 'used-goods-exchange-arrival', landmarkId: 'landmark-a', state: 'available', stableSeed: '0123456789abcdef' }],
  demolishedIds: [],
});

test('City Forge catalog delivers its bounded one-shot content inventory', () => {
  assert.equal(CITY_FORGE_CHUNK_CELLS, 9);
  assert.equal(CITY_FORGE_BUILDINGS.length, 30);
  assert.equal(CITY_FORGE_FACADES.length, 8);
  assert.equal(CITY_FORGE_ROOFS.length, 12);
  assert.equal(CITY_FORGE_LANDMARKS.length, 10);
  assert.equal(CITY_FORGE_SITUATIONS.length, 80);
  assert.equal(CITY_FORGE_SCREEN_MOUNTS.length >= 3, true);
  assert.deepEqual(Object.keys(CITY_FORGE_BUILDING_SCREEN_SLOTS).sort(), [...CITY_FORGE_BUILDINGS].sort(), 'every shipped building declares its physical screen capacity');
  assert.equal(Object.values(CITY_FORGE_BUILDING_SCREEN_SLOTS).every(value => Number.isInteger(value) && value >= 1 && value <= 5), true);
  assert.equal(createHash('sha256').update(canonicalJson(cityForgeCatalogFingerprintInput())).digest('hex'), CITY_FORGE_CATALOG_HASH);
});

test('City Forge keeps map clicks separate from the bounded toolbar', () => {
  const source = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeBuildMode.cs', import.meta.url), 'utf8');
  const mapPointer = source.slice(source.indexOf('private void UpdateMapMousePointer'), source.indexOf('private void UpdateHoveredCell'));
  const launcher = source.slice(source.indexOf('private void RefreshLauncher()'), source.indexOf('private void RequestLauncherOpen()'));
  assert.match(source, /new GameObject\("CityForgeToolbar", typeof\(RectTransform\), typeof\(Image\)\)/);
  assert.match(source, /buildUiSurface = toolbar\.GetComponent<RectTransform>\(\)/);
  assert.match(source, /buildUiButtons\.AddRange\(toolbar\.GetComponentsInChildren<Button>\(true\)\)/);
  assert.doesNotMatch(source, /root\.AddComponent<Image>\(\)/, 'the screen-sized Canvas root must never become a map-blocking graphic');
  assert.match(mapPointer, /mapMousePressed = !mouseStartedOverBuildUi;/);
  assert.doesNotMatch(mapPointer, /IsPointerOverGameObject/, 'unrelated HUD raycasters must not swallow map presses');
  assert.match(mapPointer, /Vector2 pointer = CurrentCityForgePointer\(\);/, 'map clicks must use the same canvas point as the toolbar');
  assert.match(mapPointer, /!BuildUiSurfaceContains\(pointer\)/, 'releasing over the toolbar must not edit through it');
  assert.doesNotMatch(source, /enabled = !UsesNativeMacMapPointer/, 'macOS must retain the normal UGUI raycasters');
  assert.match(source, /LegacyMouseGestureUnavailable\(\)/, 'native desktop input may be a guarded fallback, not the only pointer route');
  assert.match(launcher, /launcherButton\.gameObject\.SetActive\(!launcherCursorLocked\)/, 'a locked first-person cursor must not receive an intrusive center button');
  assert.doesNotMatch(launcher, /new Vector2\(\.5f, \.5f\)/, 'the launcher must not return to the center of the viewport');
  assert.match(launcher, /Press B to build your city/, 'the locked-pointer entry route must remain explicit');
});

test('City Forge maps browser pointer events into backing-canvas pixels for every mouse route', () => {
  const source = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeBuildMode.cs', import.meta.url), 'utf8');
  const template = readFileSync(new URL('../Unity/Assets/WebGLTemplates/SlopCity/index.html', import.meta.url), 'utf8');
  const bridge = readFileSync(new URL('../Unity/Assets/Plugins/WebGL/SlopCityPresentation.jslib', import.meta.url), 'utf8');
  const mapperStart = template.indexOf('function slopCityCanvasPoint(event)');
  const mapperEnd = template.indexOf('function trackSlopCityCanvasPointer(event)', mapperStart);
  assert.ok(mapperStart >= 0 && mapperEnd > mapperStart, 'the WebGL template must expose the canvas mapper');
  const context = {
    canvas: {
      width: 2000,
      height: 1000,
      getBoundingClientRect() { return { left: 100, bottom: 450, width: 500, height: 250 }; },
    },
    Number,
  };
  vm.runInNewContext(`${template.slice(mapperStart, mapperEnd)}; result = slopCityCanvasPoint({ clientX: 350, clientY: 325 });`, context);
  assert.equal(context.result.x, 1000, 'DOM x must scale into backing pixels');
  assert.equal(context.result.y, 500, 'DOM top-left y must become lower-left backing pixels');
  assert.equal(context.result.valid, true);
  context.canvas.getBoundingClientRect = () => ({ left: 100, bottom: 450, width: 0, height: 250 });
  vm.runInNewContext(`${template.slice(mapperStart, mapperEnd)}; result = slopCityCanvasPoint({ clientX: 350, clientY: 325 });`, context);
  assert.equal(context.result, null, 'an invalid canvas rectangle must fall back to Unity input');

  assert.match(template, /\(clientX - rect\.left\) \* width \/ rect\.width/);
  assert.match(template, /\(rect\.bottom - clientY\) \* height \/ rect\.height/);
  assert.match(template, /window\.addEventListener\("pointerup", onSlopCityTrackedPointerUp, true\)/,
    'an outside release must retain its actual release coordinate');
  assert.match(template, /resetSlopCityCanvasPointer\(\);\n      if \(graphicsLost \|\| sessionFailed\) return;/,
    'pointer lock must invalidate the bridge before Unity falls back');
  assert.match(bridge, /SlopCity_HasCanvasPointer/);
  assert.match(bridge, /SlopCity_GetCanvasPointerX/);
  assert.match(bridge, /SlopCity_GetCanvasPointerY/);
  assert.match(source, /\[DllImport\("__Internal"\)\] private static extern int SlopCity_HasCanvasPointer\(\);/);
  assert.match(source, /private static Vector2 CurrentCityForgePointer\(\)/);
  assert.match(source, /return Input\.mousePosition;/, 'custom WebGL hosts and native players keep the ordinary input fallback');

  const onGui = source.slice(source.indexOf('private void OnGUI()'), source.indexOf('private static bool UsesNativeMacMapPointer()'));
  const mapPointer = source.slice(source.indexOf('private void UpdateMapMousePointer'), source.indexOf('private void UpdateHoveredCell'));
  const hover = source.slice(source.indexOf('private void UpdateHoveredCell'), source.indexOf('private void PanMap'));
  const launcher = source.slice(source.indexOf('private void UpdateLauncherPointerActivation()'), source.indexOf('private bool LauncherContains'));
  const toolbar = source.slice(source.indexOf('private bool UpdateBuildUiPointerActivation()'), source.indexOf('private bool BuildUiSurfaceContains'));
  for (const [name, route] of Object.entries({ onGui, mapPointer, hover, launcher, toolbar })) {
    assert.match(route, /CurrentCityForgePointer\(\)/, `${name} must share the canvas-pixel pointer route`);
  }
});

test('City Forge starts browser drafting wider and keeps web controls readable without changing Mac defaults', () => {
  const source = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeBuildMode.cs', import.meta.url), 'utf8');
  const awake = source.slice(source.indexOf('private void Awake()'), source.indexOf('private void Update()'));
  const button = source.slice(source.indexOf('private static Button Button('), source.indexOf('private void SetUi('));

  assert.match(source, /NativeInitialTopDownHeight = 130f/);
  assert.match(source, /WebGlInitialTopDownHeight = 280f/);
  assert.match(source, /\[SerializeField, Range\(40f, 280f\)\] private float topDownHeight = NativeInitialTopDownHeight;/,
    'the browser default must use the existing shared 40–280m zoom bounds without changing native framing');
  assert.match(awake, /#if UNITY_WEBGL && !UNITY_EDITOR[\s\S]*?topDownHeight = WebGlInitialTopDownHeight;/,
    'only browser players should receive the wider initial Forge survey');
  assert.match(source, /buildCamera\.orthographicSize = topDownHeight \* \.55f/,
    'the wider initial height must drive the actual drafting camera');
  assert.match(button, /size\.y = Mathf\.Max\(size\.y, 40f\);/,
    'browser action targets must remain comfortably clickable');
  assert.match(button, /const int buttonFontSize = 15;/);
  assert.match(button, /const int buttonFontSize = 13;/,
    'the native desktop geometry remains the established Forge baseline');
  assert.match(source, /scaler\.referenceResolution = new Vector2\(1280, 720\);/,
    'controls must not shrink a wide row by changing the shared scaler');
});

test('City Forge maps display input through the camera viewport when browser presentation uses a render target', () => {
  const buildMode = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeBuildMode.cs', import.meta.url), 'utf8');
  const presentation = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityWebPresentation.cs', import.meta.url), 'utf8');
  const projection = buildMode.slice(
    buildMode.indexOf('private bool TryGetMapViewportPoint('),
    buildMode.indexOf('private bool TryGetMapCell('),
  );
  const mapCell = buildMode.slice(
    buildMode.indexOf('private bool TryGetMapCell('),
    buildMode.indexOf('private void CaptureMapCell('),
  );

  assert.match(presentation, /worldCamera\.targetTexture = target;/,
    'the browser presentation deliberately renders the world through a bounded target');
  assert.match(projection, /screenPosition\.x \/ displayWidth/);
  assert.match(projection, /screenPosition\.y \/ displayHeight/);
  assert.match(projection, /Rect cameraRect = buildCamera\.rect;/);
  assert.match(mapCell, /TryGetMapViewportPoint\(screenPosition, out Vector3 viewportPoint\)/);
  assert.match(mapCell, /buildCamera\.ViewportPointToRay\(viewportPoint\)/,
    'map hit testing must follow the displayed camera viewport, not target pixels');
  assert.doesNotMatch(mapCell, /ScreenPointToRay/,
    'display pixels are not camera-target pixels in the WebGL presentation');
});

test('City Forge grids its bounded strategic window and retains one safe cross-chunk action', () => {
  const buildMode = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeBuildMode.cs', import.meta.url), 'utf8');
  const runtime = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeRuntime.cs', import.meta.url), 'utf8');
  const strategic = runtime.slice(
    runtime.indexOf('public void ShowCityForgeStrategicMap(CityForgeWorldHead head, CityForgeCoord center, int radius,'),
    runtime.indexOf('public void ClearCityForgeStrategicMap()'),
  );
  const strategicGrid = runtime.slice(
    runtime.indexOf('private void BuildStrategicGrid('),
    runtime.indexOf('private static void BuildStrategicSummaryImpostor'),
  );
  const overlay = runtime.slice(
    runtime.indexOf('public void ShowCityForgeBuildOverlay('),
    runtime.indexOf('public void ClearCityForgeBuildOverlay()'),
  );
  const click = buildMode.slice(
    buildMode.indexOf('private void ClickMapCell(CityForgeCoord coordinate, int x, int z)'),
    buildMode.indexOf('private void ToggleStreet'),
  );
  const select = buildMode.slice(
    buildMode.indexOf('public bool SelectChunk(CityForgeCoord coordinate)'),
    buildMode.indexOf('public void SelectView'),
  );
  const toolChanges = buildMode.slice(
    buildMode.indexOf('private void CycleRoadClass()'),
    buildMode.indexOf('public bool TryMergeLots'),
  );

  assert.match(strategic, /int boundedRadius = Mathf\.Clamp\(radius, 1, 6\);/, 'the visible planning grid must remain a bounded 13 by 13 window');
  assert.match(strategic, /BuildStrategicGrid\([^;]*boundedRadius[^;]*\);/, 'the strategic window must receive one shared cell-grid overlay');
  assert.match(strategicGrid, /for \(int \w+ = -radius; \w+ <= radius; \w+\+\+\)/, 'the shared grid must cover every chunk in the visible window');
  assert.match(strategicGrid, /if \(!SlopCityCityForge\.IsBuildable\(coordinate\)\) continue;/, 'the immutable Old City must remain grid-free');
  assert.match(strategicGrid, /for \(int \w+ = 0; \w+ <= SlopCityCityForge\.GridSize; \w+\+\+\)/, 'each buildable chunk must receive all of its 9 by 9 cell lines');
  assert.match(strategicGrid, /SlopCityCityForge\.CellSizeMeters/, 'strategic grid lines must use the same cell geometry as placement');
  assert.doesNotMatch(overlay, /BuildEditorGrid\(/, 'the selected draft must not add a second grid box over the shared strategic grid');
  assert.doesNotMatch(runtime, /PlanSurface|PlanBorder|private void BuildEditorGrid/, 'direct placement must not show a dark selected-chunk slab or border');
  assert.match(strategic, /if \(!coordinate\.Equals\(center\)\)\s*\{\s*if \(acceptedPlans[\s\S]*?BuildStrategicAcceptedImpostor[\s\S]*?else if \(strategicSummaries[\s\S]*?BuildStrategicSummaryImpostor/, 'the selected draft block must not fall through to a distant-summary mass or road bar');

  assert.doesNotMatch(click, /if \(view == View\.Ground\)[\s\S]*?Click inside the highlighted block/, 'Ground must not discard a click merely because it lands outside the previously selected block');
  assert.match(click, /QueueMapAction\(coordinate, x, z\);/, 'an outside visible-cell click must retain its target while the destination loads');
  assert.match(click, /SelectChunk\(coordinate, out \w+\)/, 'the retained action must select/load its clicked block');
  assert.match(buildMode, /private bool hasPendingMapAction;/, 'the queued action must retain explicit state');
  assert.match(buildMode, /public event Action<CityForgeCoord> ChunkNavigationCanceled;/, 'the public editor must be able to invalidate hosted navigation');
  assert.match(select, /ApplyPendingMapAction\(\);/, 'the queued click must run after the target draft is available, without a second click');
  assert.match(buildMode, /ChunkNavigationCanceled\?\.Invoke\(coordinate\);/, 'canceling a queued action must invalidate its hosted navigation');
  assert.match(click, /pendingMapActionExtendsLotFootprint = Input\.GetKey/, 'a delayed lot action must retain its Shift modifier');
  assert.match(toolChanges, /CancelPendingMapAction\(\); roadClass =/, 'tool changes must cancel, not reinterpret, a queued action');
});

test('City Forge physical ground has no artificial seams and its island exterior suppresses false land', () => {
  const runtime = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeRuntime.cs', import.meta.url), 'utf8');
  const ground = runtime.slice(runtime.indexOf('private void BuildGround('), runtime.indexOf('private void BuildEditorGround('));
  const skyVista = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeSkyVista.cs', import.meta.url), 'utf8');

  assert.match(ground, /new Vector3\(cell, \.32f, cell\)/, 'the physical top must fill its complete 8m grid cell');
  assert.doesNotMatch(ground, /cell - \.20f/, 'valid neighbouring ground cells must not leave a visual no-build slit');
  assert.match(ground, /new Vector3\(cell - \.92f, \.88f, cell - \.92f\)/, 'only the island underside remains inset at a true exterior edge');
  assert.doesNotMatch(skyVista, /CityForgeSkyVista_Clouds|SkyCloud_|CreatePrimitive\(PrimitiveType\.Sphere\)|RenderSettings\.fog/, 'the runtime must not generate cloud puffs or cloud fog');
  assert.match(skyVista, /bool islandContext = SlopCityCityForgeTravel\.IsSkySprawlPosition\(followTarget\.position\);/);
  assert.match(skyVista, /SetDistantGroundSuppressed\(islandContext\);/, 'removing fake clouds must not restore the authored distant floor outside the island');
  assert.match(skyVista, /SetOceanVisible\(islandContext\);/, 'the exterior should read as non-walkable water rather than generated city land');
});

test('City Forge streams the deterministic street-level continuation around player-owned plans', () => {
  const runtime = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeRuntime.cs', import.meta.url), 'utf8');
  const controller = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeController.cs', import.meta.url), 'utf8');
  const bootstrap = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeOfflineBootstrap.cs', import.meta.url), 'utf8');
  const follower = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeOfflineStreamFollower.cs', import.meta.url), 'utf8');
  const activate = runtime.slice(runtime.indexOf('public void ActivateCityForgeChunk'), runtime.indexOf('public void ReleaseCityForgeChunk'));
  const scenic = runtime.slice(runtime.indexOf('private void BuildScenicCityContinuation'), runtime.indexOf('private void BuildGround('));
  const acceptedInfill = runtime.slice(runtime.indexOf('private static void BuildScenicAcceptedInfill'), runtime.indexOf('private static void BuildScenicAvenue'));
  const stream = controller.slice(controller.indexOf('public void StreamAt('), controller.indexOf('public void SetBuildMode('));

  assert.ok(activate.indexOf('BuildScenicCityContinuation(instance, chunk);') < activate.indexOf('BuildGround(instance, chunk)'),
    'the visual continuation must resolve before accepted player geometry is rendered');
  assert.match(scenic, /if \(chunk\.acceptedPlan != null\)[\s\S]*?BuildScenicAcceptedInfill\(instance, chunk\);/,
    'ground-only accepted islands must receive safe Forge visual infill');
  assert.match(scenic, /ScenicAvenue_Centre_NS|ScenicAvenue_Centre_EW/);
  assert.match(scenic, /ScenicBuilding_|ScenicWindowFront|ScenicLamp_/,
    'the restored ground view must include deterministic buildings, windows, and street lamps');
  assert.match(scenic, /BuildScenicTravelSurface/);
  assert.match(acceptedInfill, /ScenicReservedCells\(plan, chunk\.roads\)/,
    'accepted-plan geometry must reserve its physical area before scenic infill is chosen');
  assert.match(acceptedInfill, /if \(!IsScenicAcceptedInfillEligible\(plan, chunk\)\) return;/,
    'scenic infill must stop before any authored or interactive plan content is built');
  assert.match(acceptedInfill, /plan\.roads[\s\S]*?plan\.parcels[\s\S]*?plan\.buildings[\s\S]*?plan\.services[\s\S]*?plan\.landmarks[\s\S]*?plan\.screens[\s\S]*?plan\.situations/,
    'roads, lots, buildings, services, landmarks, screens, and situations must all suppress approximate scenery');
  assert.match(acceptedInfill, /chunk\.roads[\s\S]*?chunk\.screens/,
    'resolved road hosts and screens must suppress infill even when their plan source is indirect');
  assert.match(acceptedInfill, /residentRoles[\s\S]*?persistedSituations[\s\S]*?availableSituations[\s\S]*?availableSituationOpportunities/,
    'resident and situation fixture records must suppress infill rather than rely on hashed placement estimates');
  assert.match(acceptedInfill, /reserved\.Add\(0\)/,
    'the fixed CityPulse fixture cell must remain reserved in an otherwise ground-only plan');
  assert.match(acceptedInfill, /plan\.groundCells \?\? new List<int>\(\)\)\.Where\(IsScenicCell\)\) reserved\.Add\(cell\)/,
    'every accepted ground cell must remain owned by the plan, never dressed as an unrecorded street or facade');
  const infillSelection = acceptedInfill.slice(acceptedInfill.indexOf('private static int FindScenicInfillCell'), acceptedInfill.indexOf('private static bool IsScenicCell'));
  assert.doesNotMatch(infillSelection, /plan\.groundCells/,
    'the compact fallback may use only an unclaimed non-ground cell');
  assert.match(acceptedInfill, /BuildScenicAvenue[\s\S]*?BuildScenicInfillBuildingIfClear[\s\S]*?BuildScenicLamp/,
    'safe accepted-plan infill must retain the Forge street, facade, and lamp language');
  assert.match(acceptedInfill, /BuildScenicInfillFacade[\s\S]*?DisableScenicColliders/,
    'the sparse partial-plan fallback must remain visual-only');
  assert.doesNotMatch(acceptedInfill, /BuildScenicTravelSurface/,
    'accepted-plan infill must never create a whole-chunk travel collider beneath a player-shaped island');
  assert.doesNotMatch(acceptedInfill, /AddFixture\(/,
    'accepted-plan infill must not create a persisted interaction or media host');
  assert.doesNotMatch(acceptedInfill, /plan\.(?:groundCells|roads|parcels|buildings)\.(?:Add|Remove|Clear)/,
    'presentation must not mutate an accepted plan or its save data');
  assert.match(runtime, /ScenicFacadeKits/);
  assert.match(runtime, /ScenicRoofKits/);
  assert.doesNotMatch(stream, /!HasAcceptedIslandGround\(coordinate\)/,
    'the bounded compiler window must include the unaccepted visual continuation');
  assert.match(bootstrap, /controller\.StreamAt\(entry\);[\s\S]*?follower\.SetKnownCenter\(entry\);/);
  assert.doesNotMatch(follower, /HasAcceptedIslandGround/,
    'the offline follower must keep the presentation window alive before the first accepted ground cell');
});

test('City Forge map ground meets the fixed Old City core without duplicate layers', () => {
  const runtime = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeRuntime.cs', import.meta.url), 'utf8');
  const layout = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityDenseLayout.cs', import.meta.url), 'utf8');
  const humanScale = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityHumanScale.cs', import.meta.url), 'utf8');
  const travel = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeTravel.cs', import.meta.url), 'utf8');
  const strategic = runtime.slice(
    runtime.indexOf('public void ShowCityForgeStrategicMap(CityForgeWorldHead head, CityForgeCoord center, int radius,'),
    runtime.indexOf('public void ClearCityForgeStrategicMap()'),
  );
  const acceptedGround = runtime.slice(runtime.indexOf('private static void BuildStrategicAcceptedImpostor'), runtime.indexOf('private static void BuildStrategicChunkOutline'));
  const editorGround = runtime.slice(runtime.indexOf('private void BuildEditorGround('), runtime.indexOf('private void BuildRoads('));

  assert.match(layout, /CityForgeCoreGroundRect = Rect\.MinMaxRect\([\s\S]*CoreMinimum\.x \* SlopCityCityForge\.ChunkSizeMeters[\s\S]*CoreMaximum\.z \+ 1\) \* SlopCityCityForge\.ChunkSizeMeters/, 'the fixed pavement must use the protected core’s exact world limits');
  assert.match(layout, /OuterGroundRect = CityForgeCoreGroundRect;/, 'the Old City support apron must not use asymmetric hand-tuned bounds');
  assert.match(humanScale, /CityForgeCoreGroundRect\.xMin \+ 5\.85f/, 'the west transit spine must remain within the protected core instead of crossing into a starter cell');
  assert.match(strategic, /if \(!coordinate\.Equals\(center\)\)\s*\{[\s\S]*?BuildStrategicAcceptedImpostor[\s\S]*?BuildStrategicSummaryImpostor/, 'the selected chunk must not draw either accepted-map or distant-summary geometry over its draft ground');
  assert.match(acceptedGround, /new Vector3\(SlopCityCityForge\.CellSizeMeters, \.04f, SlopCityCityForge\.CellSizeMeters\)/, 'accepted map ground must fill its complete cell');
  assert.match(editorGround, /new Vector3\(SlopCityCityForge\.CellSizeMeters, \.04f, SlopCityCityForge\.CellSizeMeters\)/, 'draft map ground must fill the same complete cell');
  assert.match(runtime, /const float gridHeight = \.52f;/, 'the shared grid must remain visible over full-cell map ground');
  assert.doesNotMatch(travel, /OpenSkyGangway|CityForge_StarterSprawlBridge|EnsureBridge\(/, 'the fixed core apron replaces the overlapping generated entry bridge');
});

test('protected core and all-sides starter ring have exact coordinate semantics', () => {
  assert.equal(isOldCityCoordinate(-2, -2), true);
  assert.equal(isOldCityCoordinate(1, 1), true);
  assert.equal(isOldCityCoordinate(2, 0), false);
  assert.equal(isStarterRingCoordinate(-3, -3), true);
  assert.equal(isStarterRingCoordinate(2, 2), true);
  assert.equal(isStarterRingCoordinate(0, 0), false);
  assert.equal(isStarterRingCoordinate(3, 0), false);
});

test('every Old-City-facing starter block has an immutable generated connection stub', async () => {
  const stubs = [
    ...[-2, -1, 0, 1].map(z => ({ x: -3, z, edge: '8,0:east', direction: 'east' })),
    ...[-2, -1, 0, 1].map(z => ({ x: 2, z, edge: '0,0:west', direction: 'west' })),
    ...[-2, -1, 0, 1].map(x => ({ x, z: -3, edge: '0,8:north', direction: 'north' })),
    ...[-2, -1, 0, 1].map(x => ({ x, z: 2, edge: '0,0:south', direction: 'south' })),
  ];
  assert.equal(oldCityConnectionStub(-3, -3), null, 'starter corners do not touch the core');
  assert.equal(oldCityConnectionStub(3, 0), null, 'frontier chunks do not touch the core');
  for (const seed of ['0123456789abcdef', 'fedcba9876543210']) for (const stub of stubs) {
    assert.deepEqual(oldCityConnectionStub(stub.x, stub.z), { edge: stub.edge, class: CITY_FORGE_OLD_CITY_CONNECTION_CLASS });
    const base = await generateBaseChunk({ worldSeed: seed, generatorVersion: CITY_FORGE_GENERATOR_VERSION, catalogHash: hash, x: stub.x, z: stub.z });
    assert.equal(base.boundary[stub.direction], CITY_FORGE_OLD_CITY_CONNECTION_CLASS);

    const replaced = plan(stub.x, stub.z);
    replaced.roads.push({ edge: stub.edge, class: CITY_FORGE_OLD_CITY_CONNECTION_CLASS });
    assert.equal(validateChunkPlan(replaced).valid, false, 'a player delta cannot replace a fixed core connection');
    const removed = plan(stub.x, stub.z);
    removed.removedRoadEdges.push(stub.edge);
    assert.equal(validateChunkPlan(removed).valid, false, 'a player delta cannot remove a fixed core connection');
  }
});

test('World Head and sparse chunk plans validate only bounded catalog state', () => {
  assert.equal(validateWorldHead(head()).valid, true);
  assert.equal(validateWorldHead({ ...head(), generatorVersion: 1 }).valid, false, 'v1 base hashes cannot be silently reinterpreted after fixed core stubs were added');
  assert.equal(validateChunkPlan(plan()).valid, true);
  for (const change of [
    value => { value.coord = { x: 0, z: 0 }; },
    value => { value.rawTransform = [1, 2, 3]; },
    value => { value.screens[0].url = 'https://example.invalid'; },
    value => { value.screens.push({ ...value.screens[0], screenId: 'screen-b' }, { ...value.screens[0], screenId: 'screen-c' }); },
    value => { value.removedRoadEdges = ['0,0:north']; },
    value => { value.buildings[0].blueprintId = 'arbitrary-prefab'; },
    value => { value.buildings[0].storefrontId = 'arbitrary-storefront'; },
    value => { value.parcels[0].zone = 'home'; },
    value => { value.services[0].type = 'arbitrary-service'; },
    value => { value.situations[0].situationId = 'made-up-quest'; },
    value => { value.landmarks[0].classId = 'street-corner'; value.landmarks[0].outcomeId = 'street-garden'; },
    value => { value.parcels[0].cells.push('9,9'); },
  ]) {
    const candidate = plan(); change(candidate);
    assert.equal(validateChunkPlan(candidate).valid, false);
  }
});

test('screen hosts derive stable catalog slots across building, civic/plaza, and road fixtures', async () => {
  const candidate = plan();
  candidate.screens = [
    { screenId: 'screen-z', hostId: 'building-a', screenMountId: 'facade-media-wall', deviceId: 'public-crt-01' },
    { screenId: 'screen-a', hostId: 'building-a', screenMountId: 'interior-wall-tv', deviceId: 'public-led-01' },
    { screenId: 'screen-site', hostId: 'site-parcel-civic', screenMountId: 'plaza-civic-support', deviceId: 'public-led-01' },
    { screenId: 'screen-road', hostId: 'road-0-0-north', screenMountId: 'street-kiosk-screen', deviceId: 'public-window-01' },
  ];
  candidate.parcels.push({ id: 'parcel-civic', cells: ['2,0'], zone: 'civic' });
  candidate.groundCells.push(2);
  assert.equal(validateChunkPlan(candidate).valid, true);
  const assignments = screenSlotAssignments(candidate.screens, candidate.buildings, candidate.parcels, candidate.roads)
    .filter(item => item.hostId === 'building-a')
    .map(({ screenId, slotIndex, slotCount, admitted }) => ({ screenId, slotIndex, slotCount, admitted }));
  assert.deepEqual(assignments, [
    { screenId: 'screen-a', slotIndex: 0, slotCount: 2, admitted: true },
    { screenId: 'screen-z', slotIndex: 1, slotCount: 2, admitted: true },
  ]);
  const reordered = structuredClone(candidate);
  reordered.screens.reverse();
  assert.equal(validateChunkPlan(reordered).valid, true);
  assert.equal(await chunkPlanHash(candidate), await chunkPlanHash(reordered), 'input order cannot choose a physical screen position');

  const sameMount = plan();
  sameMount.screens.push({ ...sameMount.screens[0], screenId: 'screen-b' });
  assert.equal(validateChunkPlan(sameMount).valid, true, 'a catalog host can use two same-mount fixtures when it has two shipped positions');
  const overflow = structuredClone(candidate);
  overflow.screens.push({ screenId: 'screen-m', hostId: 'building-a', screenMountId: 'facade-media-wall', deviceId: 'public-crt-01' });
  assert.equal(validateChunkPlan(overflow).valid, false, 'a third player screen cannot exceed corner-shop capacity');

  const siteOverflow = structuredClone(candidate);
  siteOverflow.screens.push({ screenId: 'screen-site-b', hostId: 'site-parcel-civic', screenMountId: 'plaza-civic-support', deviceId: 'public-led-01' });
  assert.equal(validateChunkPlan(siteOverflow).valid, false, 'the shipped civic support has one screen position');
  const roadOverflow = structuredClone(candidate);
  roadOverflow.screens.push({ screenId: 'screen-road-b', hostId: 'road-0-0-north', screenMountId: 'street-kiosk-screen', deviceId: 'public-window-01' });
  assert.equal(validateChunkPlan(roadOverflow).valid, false, 'the shipped road kiosk has one screen position');
  assert.equal(validateScreenHost('road-0-0-north', 'street-kiosk-screen', candidate.buildings, candidate.parcels, candidate.roads), true);
  candidate.screens.find(screen => screen.screenId === 'screen-road').hostId = 'road-8-8-west';
  assert.equal(validateChunkPlan(candidate).valid, false);
});

test('Cityworks and situations have durable authored lifecycle transitions', () => {
  const previous = plan();
  previous.landmarks[0].state = 'active';
  previous.situations[0].state = 'available';
  const next = structuredClone(previous);
  next.landmarks[0].state = 'opening';
  next.situations[0].state = 'active';
  assert.equal(canTransitionCityworks('chosen', 'active'), true);
  assert.equal(canTransitionSituation('available', 'active'), true);
  assert.equal(validatePlanTransitions(previous, next).valid, true);
  next.landmarks[0].state = 'established';
  assert.equal(validatePlanTransitions(previous, next).valid, false);
});

test('canonical boundary keys are symmetric and cross-boundary roads require matching commits', () => {
  assert.equal(canonicalBoundaryEdgeKey(2, 0, '8,4:east'), canonicalBoundaryEdgeKey(3, 0, '0,4:west'));
  const west = plan(2, 0); west.roads.push({ edge: '8,4:east', class: 'boulevard' });
  const east = plan(3, 0); east.roads.push({ edge: '0,4:west', class: 'boulevard' });
  assert.equal(validateBoundarySymmetry([west, east]).valid, true);
  east.roads.find(road => road.edge === '0,4:west').class = 'local-street';
  assert.equal(validateBoundarySymmetry([west, east]).valid, false);
});

test('frontier generation and sparse map hashes are deterministic across reload order', async () => {
  const inputs = { worldSeed: '0123456789abcdef', generatorVersion: CITY_FORGE_GENERATOR_VERSION, catalogHash: hash, x: 7, z: -9 };
  const a = await generateBaseChunk(inputs), b = await generateBaseChunk({ ...inputs });
  assert.deepEqual(a, b);
  assert.notEqual(a.baseChunkHash, (await generateBaseChunk({ ...inputs, x: 8 })).baseChunkHash);
  const onePlan = plan(2, 0); onePlan.roads.push({ edge: '1,0:north', class: 'boulevard' });
  const one = await chunkPlanHash(onePlan);
  const reordered = plan(2, 0); reordered.roads = [{ edge: '1,0:north', class: 'boulevard' }, ...reordered.roads];
  assert.equal(one, await chunkPlanHash(reordered));
  const rootA = await chunkMapRoot([{ coord: { x: 3, z: 0 }, revisionId: 'chunk-b', hash: 'c'.repeat(64) }, { coord: { x: 2, z: 0 }, revisionId: 'chunk-a', hash: 'd'.repeat(64) }]);
  const rootB = await chunkMapRoot([{ coord: { x: 2, z: 0 }, revisionId: 'chunk-a', hash: 'd'.repeat(64) }, { coord: { x: 3, z: 0 }, revisionId: 'chunk-b', hash: 'c'.repeat(64) }]);
  assert.equal(rootA, rootB);
});

test('City Pulse produces deterministic non-economic life hooks from accepted plan state', () => {
  assert.deepEqual(resolveCityPulse([plan()], 'same-world'), resolveCityPulse([plan()], 'same-world'));
  assert.equal(Object.keys(resolveCityPulse([plan()]).tags).length, 4);
});
