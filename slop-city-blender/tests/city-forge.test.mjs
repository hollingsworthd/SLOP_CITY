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

test('City Forge starts focused and keeps browser and mobile controls readable', () => {
  const source = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeBuildMode.cs', import.meta.url), 'utf8');
  const awake = source.slice(source.indexOf('private void Awake()'), source.indexOf('private void Update()'));
  const button = source.slice(source.indexOf('private Button Button('), source.indexOf('private InputField IntegerInput('));
  const canvas = source.slice(source.indexOf('private void ConfigureCityForgeCanvas('), source.indexOf('private static Color CityForgeToolbarColor()'));
  const touchLayout = source.slice(source.indexOf('private void LayoutTouchCityForgeUi(bool force)'), source.indexOf('private static void LayoutTouchRect('));

  assert.match(source, /InitialTopDownHeight = 130f/);
  assert.doesNotMatch(source, /WebGlInitialTopDownHeight/,
    'browser planning must begin at the same focused scale as the established editor');
  assert.match(source, /\[SerializeField, Range\(40f, 280f\)\] private float topDownHeight = InitialTopDownHeight;/,
    'the focused default must preserve the existing 40–280m zoom bounds');
  assert.doesNotMatch(awake, /topDownHeight\s*=/,
    'opening a browser game must not replace the focused serialized default with a distant survey');
  assert.match(source, /buildCamera\.orthographicSize = topDownHeight \* \.55f/,
    'the focused initial height must drive the actual drafting camera');
  assert.match(button, /size\.y = Mathf\.Max\(size\.y, 40f\);/,
    'browser action targets must remain comfortably clickable');
  assert.match(button, /buttonFontSize = 15;/);
  assert.match(button, /int buttonFontSize = 13;/,
    'the native desktop geometry remains the established Forge baseline');
  assert.match(source, /scaler\.referenceResolution = new Vector2\(1280, 720\);/,
    'controls must not shrink a wide row by changing the shared scaler');
  assert.match(button, /if \(UsesTouchCityForgeControls\(\)\)[\s\S]*?size\.y = Mathf\.Max\(size\.y, 44f\);/,
    'native touch controls must retain a comfortably tappable minimum target');
  assert.match(canvas, /if \(UsesTouchCityForgeControls\(\)\)[\s\S]*?bool portrait = Screen\.height >= Screen\.width;/,
    'the instance canvas configuration must select a mobile layout from the current orientation');
  assert.match(canvas, /scaler\.referenceResolution = portrait \? new Vector2\(360, 700\) : new Vector2\(720, 360\);/,
    'phone portrait and landscape controls must receive their intended reference sizes');
  assert.match(canvas, /scaler\.matchWidthOrHeight = portrait \? 0f : 1f;/,
    'mobile controls must scale against the stable axis for each orientation');
  assert.match(touchLayout, /float targetHeight = Mathf\.Max\(48f, 44f \/ canvasScale\);/,
    'the live touch relayout must preserve a 44px physical minimum even on a compact scaled native surface');
  assert.match(touchLayout, /int columns = landscape \? 6 : 3;/,
    'the touch toolbar must reflow rather than rely on one desktop-wide row');
  assert.match(canvas, /target\.pixelPerfect = true;/,
    'browser Forge controls must align to display pixels rather than the low-detail world target');
  assert.match(canvas, /scaler\.dynamicPixelsPerUnit = Mathf\.Clamp\(SlopCityWebPresentation\.DisplayPixelRatio, 1f, 2f\);/,
    'browser Forge text must request a dense dynamic atlas on high-density displays');
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

test('City Forge omits visual strategic grids and retains one safe cross-chunk action', () => {
  const buildMode = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeBuildMode.cs', import.meta.url), 'utf8');
  const runtime = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeRuntime.cs', import.meta.url), 'utf8');
  const strategic = runtime.slice(
    runtime.indexOf('public void ShowCityForgeStrategicMap(CityForgeWorldHead head, CityForgeCoord center, int radius,'),
    runtime.indexOf('public void ClearCityForgeStrategicMap()'),
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

  assert.match(strategic, /int boundedRadius = Mathf\.Clamp\(radius, 1, 6\);/, 'strategic summaries must remain bounded even without a visual planning grid');
  assert.doesNotMatch(runtime, /CityForgeStrategicGrid|BuildStrategicGrid|RefreshCityForgeStrategicGrid|ISlopCityCityForgeStrategicGridSink/,
    'the strategic map must not allocate, draw, or refresh a visible grid while panning');
  assert.doesNotMatch(strategic, /BuildStrategicChunkOutline/,
    'the strategic map must not substitute a coarse block lattice for the removed grid');
  assert.doesNotMatch(overlay, /BuildEditorGrid\(/, 'the selected draft must not add a replacement grid box');
  assert.doesNotMatch(runtime, /PlanSurface|PlanBorder|private void BuildEditorGrid/, 'direct placement must not show a dark selected-chunk slab or border');
  assert.match(strategic, /if \(!SlopCityCityForge\.IsBuildable\(coordinate\) \|\| coordinate\.Equals\(center\)\) continue;/, 'the selected draft block must not fall through to a distant-summary mass or road bar');
  assert.match(strategic, /if \(acceptedPlans[\s\S]*?BuildStrategicAcceptedImpostor[\s\S]*?else if \(strategicSummaries[\s\S]*?BuildStrategicSummaryImpostor/, 'accepted and summarized neighbouring blocks must remain visible without grid geometry');

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

test('City Forge uses one planning layer and restores streamed chunks after editing', () => {
  const buildMode = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeBuildMode.cs', import.meta.url), 'utf8');
  const runtime = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeRuntime.cs', import.meta.url), 'utf8');
  const activate = runtime.slice(runtime.indexOf('public void ActivateCityForgeChunk'), runtime.indexOf('public void ReleaseCityForgeChunk'));
  const overlay = runtime.slice(runtime.indexOf('public void ShowCityForgeBuildOverlay('), runtime.indexOf('public void ClearCityForgeBuildOverlay()'));
  const clear = runtime.slice(runtime.indexOf('public void ClearCityForgeBuildOverlay()'), runtime.indexOf('private void BuildEditorLots('));
  const camera = buildMode.slice(buildMode.indexOf('private void CaptureCamera()'), buildMode.indexOf('private void FocusCamera()'));
  const ui = buildMode.slice(buildMode.indexOf('private void CreateUi()'), buildMode.indexOf('private void RegisterViewControl(View scope, Button button)'));
  const toolbarColor = buildMode.slice(buildMode.indexOf('private static Color CityForgeToolbarColor()'), buildMode.indexOf('private static Color RoadColor('));

  assert.match(runtime, /private bool buildOverlayPresentationActive;/);
  assert.match(activate, /root\.SetActive\(!buildOverlayPresentationActive\);/,
    'a chunk streamed during editing must remain compiled but hidden from the planning map');
  assert.match(overlay, /DestroyBuildOverlay\(false\);[\s\S]*?SetChunkPresentationForBuildOverlay\(true\);/,
    'the draft layer must hide physical street-level roots before it draws the map representation');
  assert.match(clear, /DestroyBuildOverlay\(true\);/,
    'leaving the editor must restore the retained physical chunks for first-person play');
  assert.match(runtime, /private void SetChunkPresentationForBuildOverlay\(bool active\)[\s\S]*?instance\.root\.SetActive\(!active\);/,
    'the presentation switch must keep every streamed chunk resident while toggling only visibility');
  assert.doesNotMatch(camera, /clearFlags|backgroundColor/,
    'Build Mode must preserve the active camera presentation instead of producing a dark map with a rewritten clear state');
  assert.match(ui, /new GameObject\("CityForgeToolbar", typeof\(RectTransform\), typeof\(Image\)\)/,
    'only the bounded toolbar, rather than the screen-wide canvas root, owns an opaque panel');
  assert.match(ui, /panel\.color = CityForgeToolbarColor\(\);/);
  assert.match(toolbarColor, /return new Color\(\.025f, \.045f, \.06f, 1f\);/,
    'the browser toolbar remains opaque while the map owns the view behind it');
});

test('City Forge supports rectangular building taps, queued dimensions, and tap-to-demolish mode', () => {
  const source = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeBuildMode.cs', import.meta.url), 'utf8');
  const click = source.slice(source.indexOf('private void ClickMapCell(CityForgeCoord coordinate, int x, int z)'), source.indexOf('private void ToggleStreet'));
  const queue = source.slice(source.indexOf('private void QueueMapAction('), source.indexOf('private void ClearPendingMapAction()'));
  const pending = source.slice(source.indexOf('private void ApplyPendingMapAction()'), source.indexOf('private void ToggleStreet'));
  const building = source.slice(source.indexOf('private void AddBuilding(int x, int z, int width, int length, bool demolish)'), source.indexOf('private void AddService('));
  const placement = source.slice(source.indexOf('private bool PlaceBuildingOnParcel('), source.indexOf('public bool TryPlaceService('));
  const controls = source.slice(source.indexOf('private void CreateUi()'), source.indexOf('private void RegisterViewControl(View scope, Button button)'));
  const input = source.slice(source.indexOf('private InputField IntegerInput('), source.indexOf('private bool UsesTouchCityForgeControls()'));
  const demolition = source.slice(source.indexOf('private void ToggleBuildingDemolition()'), source.indexOf('private void CycleLotZone()'));
  const nextId = source.slice(source.indexOf('private static string NextId(CityForgeChunkPlan source, string prefix)'), source.indexOf('private string Seed('));

  assert.match(source, /private InputField buildingWidthInput, buildingLengthInput;/);
  assert.match(controls, /buildingWidthInput = IntegerInput\(toolbar\.transform, "Width",[\s\S]*?SetBuildingFootprintWidth\)/);
  assert.match(controls, /buildingLengthInput = IntegerInput\(toolbar\.transform, "Length",[\s\S]*?SetBuildingFootprintLength\)/);
  assert.match(input, /field\.contentType = InputField\.ContentType\.IntegerNumber;/,
    'footprint dimensions must be direct numeric inputs, including in native mobile builds');
  assert.match(input, /field\.text = Mathf\.Clamp\(initialValue, 1, SlopCityCityForge\.GridSize\)\.ToString\(\);/);
  assert.match(input, /if \(UsesTouchCityForgeControls\(\)\) size\.y = Mathf\.Max\(size\.y, 44f\);/,
    'dimension inputs must be touch-sized too');
  assert.match(click, /ApplyMapAction\(view, x, z, Input\.GetKey[\s\S]*?buildingFootprintWidth, buildingFootprintLength, demolishBuildings\);/,
    'a same-block tap must pass the active dimensions and demolition mode to the edit action');
  assert.match(source, /private int pendingMapActionBuildingWidth = 1;/);
  assert.match(source, /private int pendingMapActionBuildingLength = 1;/);
  assert.match(source, /private bool pendingMapActionDemolishBuildings;/);
  assert.match(queue, /pendingMapActionBuildingWidth = buildingFootprintWidth;/);
  assert.match(queue, /pendingMapActionBuildingLength = buildingFootprintLength;/);
  assert.match(queue, /pendingMapActionDemolishBuildings = demolishBuildings;/,
    'a cross-chunk tap must retain its original construction or demolition intent while data loads');
  assert.match(pending, /int buildingWidth = pendingMapActionBuildingWidth;/);
  assert.match(pending, /int buildingLength = pendingMapActionBuildingLength;/);
  assert.match(pending, /bool demolish = pendingMapActionDemolishBuildings;/);
  assert.match(pending, /ApplyMapAction\(actionView, x, z, extendsLotFootprint, buildingWidth, buildingLength, demolish\);/);
  assert.match(building, /if \(demolish\) \{ DemolishBuildingAt\(x, z\); return; \}/,
    'Demolish is a placement-mode tap on the selected footprint, not a global undo-like action');
  assert.match(building, /private bool TryPlaceBuildingFootprint\(int x, int z, int requestedWidth, int requestedLength\)/);
  assert.match(building, /int width = Mathf\.Clamp\(requestedWidth, 1, SlopCityCityForge\.GridSize\);/);
  assert.match(building, /int length = Mathf\.Clamp\(requestedLength, 1, SlopCityCityForge\.GridSize\);/);
  assert.match(building, /List<int> cells = new List<int>\(width \* length\);/);
  assert.match(building, /for \(int cellZ = z; cellZ < z \+ length; cellZ\+\+\)[\s\S]*?for \(int cellX = x; cellX < x \+ width; cellX\+\+\) cells\.Add\(cellX \+ cellZ \* SlopCityCityForge\.GridSize\);/,
    'the two inputs must expand to the complete rectangular parcel-cell footprint');
  assert.match(building, /List<CityForgeParcel> remainingParcels = null;/);
  assert.match(building, /else if \(footprint\.IsSubsetOf\(existingCells\)[\s\S]*?List<List<int>> fragments = SplitCellsIntoRectangles\(/,
    'a smaller rectangle may be carved from one larger empty lot rather than being forced back to the whole lot');
  assert.match(building, /parcelCount \+ fragments\.Count > SlopCityCityForge\.MaximumParcelRecords/);
  assert.match(building, /foreach \(List<int> fragment in fragments\)[\s\S]*?remaining\.id = NextId\(idSource, "parcel"\);/);
  assert.match(building, /remaining\.cells = fragment;/);
  assert.match(placement, /foreach \(CityForgeParcel remaining in remainingParcels \?\? Enumerable\.Empty<CityForgeParcel>\(\)\)[\s\S]*?next\.parcels\.Add\(remaining\.Clone\(\)\);/,
    'the unbuilt remainder must stay in separate non-overlapping parcels instead of a ring-shaped lot centered under the new building');
  assert.match(nextId, /\.Concat\(source\?\.demolishedIds \?\? new List<string>\(\)\)/,
    'demolished identities must remain tombstoned so a rebuilt footprint receives a new identity');
  assert.match(building, /private void DemolishBuildingAt\(int x, int z\)/);
  assert.match(controls, /demolishButton\.onClick\.AddListener\(ToggleBuildingDemolition\);/);
  assert.match(demolition, /demolishBuildings = !demolishBuildings;/);
  assert.match(source, /SetButtonLabel\(demolishButton, demolishBuildings \? "Demolish: ON" : "Demolish: OFF"\);/);
  assert.doesNotMatch(source, /DemolishLast/,
    'the obsolete "demolish whichever building came last" interaction must not return');
});

test('City Forge keeps slow touch pans and focused footprint entry from becoming placement actions', () => {
  const source = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeBuildMode.cs', import.meta.url), 'utf8');
  const update = source.slice(source.indexOf('private void Update()'), source.indexOf('private void OnGUI()'));
  const click = source.slice(source.indexOf('private void ClickMapCell(CityForgeCoord coordinate, int x, int z)'), source.indexOf('private void QueueMapAction('));
  const move = source.slice(source.indexOf('private void MoveCamera()'), source.indexOf('private void OnApplicationFocus('));
  const layout = source.slice(source.indexOf('private void LayoutTouchCityForgeUi(bool force)'), source.indexOf('private static void LayoutTouchRect('));

  assert.match(source, /private Vector2 touchStartPosition;/);
  assert.match(move, /touchStartPosition = touch\.position;/);
  assert.match(move, /\(touch\.position - touchStartPosition\)\.sqrMagnitude >= MapDragThresholdPixels \* MapDragThresholdPixels/,
    'a series of small touch deltas must still count as a pan before release can place or demolish');
  assert.match(click, /if \(view == View\.Buildings && !demolishBuildings\) CommitBuildingFootprintInputs\(\);/,
    'a map tap must commit the latest width and length before it snapshots or applies the building action');
  assert.match(source, /private static bool IsTextInputFocused\(\)/);
  assert.match(update, /bool textInputFocused = IsTextInputFocused\(\);/);
  assert.match(update, /if \(!textInputFocused\)[\s\S]*?KeyCode\.Alpha1[\s\S]*?KeyCode\.Alpha6/,
    'number-row view shortcuts must not steal direct numeric footprint input');
  assert.match(layout, /Canvas\.ForceUpdateCanvases\(\);[\s\S]*?canvasWidth <= 0f \|\| canvasHeight <= 0f \|\| canvas\.scaleFactor <= 0f/,
    'the first touch layout must wait for a real canvas before caching it');
  assert.match(layout, /canvasSize == lastBuildUiCanvasSize && Mathf\.Approximately\(canvasScale, lastBuildUiCanvasScale\)/,
    'rotation or a CanvasScaler change must trigger a fresh safe-area layout rather than reuse stale bounds');
});

test('City Forge center-carve remainders stay usable outside the new building', () => {
  const source = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeBuildMode.cs', import.meta.url), 'utf8');
  const split = source.slice(source.indexOf('private static List<List<int>> SplitCellsIntoRectangles('), source.indexOf('private void DemolishBuildingAt('));
  const candidate = plan();
  candidate.groundCells = [0, 1, 2, 9, 10, 11, 18, 19, 20];
  candidate.roads = [{ edge: '1,1:north', class: 'local-street' }];
  candidate.parcels = [
    { id: 'parcel-building', cells: ['1,1'], zone: 'home' },
    { id: 'parcel-top', cells: ['0,2', '1,2', '2,2'], zone: 'home' },
    { id: 'parcel-left', cells: ['0,0', '0,1'], zone: 'home' },
    { id: 'parcel-right', cells: ['2,0', '2,1'], zone: 'home' },
    { id: 'parcel-bottom', cells: ['1,0'], zone: 'home' },
  ];
  candidate.buildings = [{ id: 'building-center', parcelId: 'parcel-building', blueprintId: 'walk-up', facadeId: 'wet-masonry', roofId: 'service-roof', floors: 2, entranceEdge: 'north' }];
  candidate.services = [{ id: 'service-top', parcelId: 'parcel-top', type: 'care-station' }];
  candidate.screens = [];
  candidate.landmarks = [];
  candidate.situations = [];
  candidate.demolishedIds = [];

  assert.match(split, /List<int> rectangle = new List<int>\(width \* length\);/);
  assert.match(split, /for \(int z = startZ; z < startZ \+ length; z\+\+\)[\s\S]*?for \(int x = startX; x < startX \+ width; x\+\+\)/,
    'the remaining cells must be emitted as actual rectangles, not one ring-shaped parcel');
  assert.equal(validateChunkPlan(candidate).valid, true,
    'a center-carved building and a service on its independent remainder must remain a valid non-overlapping plan');
  const buildingCells = new Set(candidate.parcels.find(parcel => parcel.id === 'parcel-building').cells);
  for (const parcel of candidate.parcels.filter(parcel => parcel.id !== 'parcel-building'))
    assert.equal(parcel.cells.some(cell => buildingCells.has(cell)), false, 'each remainder must exclude the building footprint');
});

test('City Forge renders rectangular building bounds in draft, strategic, physical, and screen presentations', () => {
  const runtime = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeRuntime.cs', import.meta.url), 'utf8');
  const editor = runtime.slice(runtime.indexOf('private void BuildEditorBuildings('), runtime.indexOf('private void BuildEditorServicesAndLife('));
  const strategic = runtime.slice(runtime.indexOf('private static void BuildStrategicAcceptedImpostor('), runtime.indexOf('private static void BuildStrategicSummaryImpostor('));
  const physical = runtime.slice(runtime.indexOf('private void BuildBuildings('), runtime.indexOf('private static void BuildBuildingShell(Transform parent, float span'));
  const shell = runtime.slice(runtime.indexOf('private static void BuildBuildingShell(Transform parent, float width, float depth'), runtime.indexOf('/// <summary>', runtime.indexOf('private static void BuildBuildingShell(Transform parent, float width, float depth')));
  const interior = runtime.slice(runtime.indexOf('private Transform BuildInterior('), runtime.indexOf('private static void ApplyBuildingFinish('));
  const screens = runtime.slice(runtime.indexOf('private static bool TryScreenPlacement('), runtime.indexOf('private static void SlotGrid('));
  const bounds = runtime.slice(runtime.indexOf('private static void ParcelBounds('), runtime.indexOf('private static Vector3 RoadEdgeCentre('));

  assert.match(runtime, /New footprints are rectangles, while legacy irregular parcels retain[\s\S]*?bounded enclosing shape/,
    'legacy arbitrary parcel cell lists must continue to render as stable bounded geometry');
  assert.match(bounds, /int minX = values\.Min\(value => value % SlopCityCityForge\.GridSize\);/);
  assert.match(bounds, /int maxX = values\.Max\(value => value % SlopCityCityForge\.GridSize\);/);
  assert.match(bounds, /int minZ = values\.Min\(value => value \/ SlopCityCityForge\.GridSize\);/);
  assert.match(bounds, /int maxZ = values\.Max\(value => value \/ SlopCityCityForge\.GridSize\);/);
  assert.match(bounds, /width = \(maxX - minX \+ 1\) \* cell;/);
  assert.match(bounds, /depth = \(maxZ - minZ \+ 1\) \* cell;/);
  assert.match(editor, /ParcelBounds\(parcel\.cells, out Vector3 center, out float parcelWidth, out float parcelDepth\);/);
  assert.match(editor, /new Vector3\(width, \.11f, depth\)/,
    'the editor overlay must show independent X and Z spans instead of a one-cell marker');
  assert.match(strategic, /ParcelBounds\(parcel\.cells, out Vector3 center, out float parcelWidth, out float parcelDepth\);/);
  assert.match(strategic, /new Vector3\(width, height, depth\)/,
    'accepted strategic impostors must preserve the rectangle after the editor closes');
  assert.match(physical, /float worldWidth = Mathf\.Max\(4f, parcelWidth \* \.75f\);/);
  assert.match(physical, /float worldDepth = Mathf\.Max\(4f, parcelDepth \* \.75f\);/,
    'the physical compiler must not cap a multi-cell parcel back down to a one-cell-looking shell');
  assert.match(physical, /float shellWidth = entranceFacesNorthSouth \? worldWidth : worldDepth;/);
  assert.match(physical, /float shellDepth = entranceFacesNorthSouth \? worldDepth : worldWidth;/,
    'east/west entrances may rotate the shell without rotating the world footprint out of its parcel');
  assert.match(physical, /BuildBuildingShell\(shell\.transform, shellWidth, shellDepth, height, building\.blueprintId\);/);
  assert.match(physical, /BuildInterior\(instance, building, centre, shellWidth, shellDepth,/);
  assert.match(physical, /new ScreenHostLayout \{ exterior = shell\.transform, interior = room, width = shellWidth, depth = shellDepth,/);
  assert.match(shell, /new Vector3\(wall, height, depth\)/);
  assert.match(shell, /new Vector3\(width, height, wall\)/,
    'physical walls and roof must use separate width and depth dimensions');
  assert.match(interior, /float width = Mathf\.Max\(4f, shellWidth \* \.7f\);/);
  assert.match(interior, /float depth = Mathf\.Max\(4f, shellDepth \* \.7f\);/);
  assert.match(screens, /float width = Mathf\.Max\(4f, host\.width \* \.7f\);/);
  assert.match(screens, /float depth = Mathf\.Max\(4f, host\.depth \* \.7f\);/,
    'interior and exterior screen hosts must respect the rectangular shell dimensions');
});

test('City Forge physical ground has no artificial seams and its island exterior suppresses false land', () => {
  const runtime = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeRuntime.cs', import.meta.url), 'utf8');
  const ground = runtime.slice(runtime.indexOf('private void BuildGround('), runtime.indexOf('private void BuildEditorGround('));
  const skyVista = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeSkyVista.cs', import.meta.url), 'utf8');

  assert.match(ground, /new Vector3\(cell, \.32f, cell\)/, 'the physical top must fill its complete 8m grid cell');
  assert.doesNotMatch(ground, /cell - \.20f/, 'valid neighbouring ground cells must not leave a visual no-build slit');
  assert.match(ground, /new Vector3\(cell - \.92f, \.88f, cell - \.92f\)/, 'only the island underside remains inset at a true exterior edge');
  assert.doesNotMatch(skyVista, /CityForgeSkyVista_Clouds|SkyCloud_|CreatePrimitive\(PrimitiveType\.Sphere\)|RenderSettings\.fog/, 'the runtime must not generate cloud puffs or cloud fog');
  assert.match(skyVista, /bool mapContext = HasActiveOverheadMapCamera\(\);/,
    'Build Mode must be recognised even while the first-person root remains in Old City');
  assert.match(skyVista, /bool islandContext = mapContext \|\| SlopCityCityForgeTravel\.IsSkySprawlPosition\(followTarget\.position\);/);
  assert.match(skyVista, /private static bool HasActiveOverheadMapCamera\(\)[\s\S]*?Camera mapCamera = Camera\.main;[\s\S]*?mapCamera\.orthographic/);
  assert.match(skyVista, /SetDistantGroundSuppressed\(islandContext\);/, 'removing fake clouds must not restore the authored distant floor outside the island');
  assert.match(skyVista, /SetOceanVisible\(islandContext\);/, 'the exterior should read as non-walkable water rather than generated city land');
  assert.match(skyVista, /private const float MapOceanSpan = 1536f;/,
    'the browser map must cover its wider overhead viewport without exposing a moving water edge');
  assert.match(skyVista, /bool mapMode = mapCamera && mapCamera\.enabled && mapCamera\.gameObject\.activeInHierarchy && mapCamera\.orthographic;/,
    'only an active overhead map may switch ocean presentation');
  assert.match(skyVista, /oceanBody\.transform\.localScale = Vector3\.one \* \(\(mapMode \? MapOceanSpan : OceanSpan\) \/ 10f\);/,
    'the map ocean must cover the planner on every supported target');
  assert.match(skyVista, /SetBrowserOceanStyle\(mapMode\);/,
    'the map must not use the street-level glossy ocean material');
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
  const acceptedGround = runtime.slice(runtime.indexOf('private static void BuildStrategicAcceptedImpostor'), runtime.indexOf('private static void BuildStrategicSummaryImpostor'));
  const editorGround = runtime.slice(runtime.indexOf('private void BuildEditorGround('), runtime.indexOf('private void BuildRoads('));

  assert.match(layout, /CityForgeCoreGroundRect = Rect\.MinMaxRect\([\s\S]*CoreMinimum\.x \* SlopCityCityForge\.ChunkSizeMeters[\s\S]*CoreMaximum\.z \+ 1\) \* SlopCityCityForge\.ChunkSizeMeters/, 'the fixed pavement must use the protected core’s exact world limits');
  assert.match(layout, /OuterGroundRect = CityForgeCoreGroundRect;/, 'the Old City support apron must not use asymmetric hand-tuned bounds');
  assert.match(humanScale, /CityForgeCoreGroundRect\.xMin \+ 5\.85f/, 'the west transit spine must remain within the protected core instead of crossing into a starter cell');
  assert.match(strategic, /if \(!SlopCityCityForge\.IsBuildable\(coordinate\) \|\| coordinate\.Equals\(center\)\) continue;[\s\S]*?BuildStrategicAcceptedImpostor[\s\S]*?BuildStrategicSummaryImpostor/, 'the selected chunk must not draw either accepted-map or distant-summary geometry over its draft ground');
  assert.match(acceptedGround, /new Vector3\(SlopCityCityForge\.CellSizeMeters, \.04f, SlopCityCityForge\.CellSizeMeters\)/, 'accepted map ground must fill its complete cell');
  assert.match(editorGround, /new Vector3\(SlopCityCityForge\.CellSizeMeters, \.04f, SlopCityCityForge\.CellSizeMeters\)/, 'draft map ground must fill the same complete cell');
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
