import {createHash} from 'node:crypto';
import {readFileSync} from 'node:fs';
import test from 'node:test';
import assert from 'node:assert/strict';
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
  assert.match(mapPointer, /!BuildUiSurfaceContains\(Input\.mousePosition\)/, 'releasing over the toolbar must not edit through it');
  assert.doesNotMatch(source, /enabled = !UsesNativeMacMapPointer/, 'macOS must retain the normal UGUI raycasters');
  assert.match(source, /LegacyMouseGestureUnavailable\(\)/, 'native desktop input may be a guarded fallback, not the only pointer route');
  assert.match(launcher, /launcherButton\.gameObject\.SetActive\(!launcherCursorLocked\)/, 'a locked first-person cursor must not receive an intrusive center button');
  assert.doesNotMatch(launcher, /new Vector2\(\.5f, \.5f\)/, 'the launcher must not return to the center of the viewport');
  assert.match(launcher, /Press B to build your city/, 'the locked-pointer entry route must remain explicit');
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

test('City Forge physical ground has no artificial seams and the Sky Sprawl uses its authored sky', () => {
  const runtime = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeRuntime.cs', import.meta.url), 'utf8');
  const ground = runtime.slice(runtime.indexOf('private void BuildGround('), runtime.indexOf('private void BuildEditorGround('));
  const skyVista = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeSkyVista.cs', import.meta.url), 'utf8');

  assert.match(ground, /new Vector3\(cell, \.32f, cell\)/, 'the physical top must fill its complete 8m grid cell');
  assert.doesNotMatch(ground, /cell - \.20f/, 'valid neighbouring ground cells must not leave a visual no-build slit');
  assert.match(ground, /new Vector3\(cell - \.92f, \.88f, cell - \.92f\)/, 'only the island underside remains inset at a true exterior edge');
  assert.doesNotMatch(skyVista, /CityForgeSkyVista_Clouds|SkyCloud_|CreatePrimitive\(PrimitiveType\.Sphere\)|RenderSettings\.fog/, 'the runtime must not generate cloud puffs or cloud fog');
  assert.match(skyVista, /SetDistantGroundSuppressed\(SlopCityCityForgeTravel\.IsSkySprawlPosition\(followTarget\.position\)\);/, 'removing fake clouds must not restore the authored distant floor in the Sky Sprawl');
});

test('City Forge map ground meets the fixed Old City core without duplicate layers', () => {
  const runtime = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeRuntime.cs', import.meta.url), 'utf8');
  const layout = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityDenseLayout.cs', import.meta.url), 'utf8');
  const travel = readFileSync(new URL('../Unity/Assets/Scripts/SlopCityCityForgeTravel.cs', import.meta.url), 'utf8');
  const strategic = runtime.slice(
    runtime.indexOf('public void ShowCityForgeStrategicMap(CityForgeWorldHead head, CityForgeCoord center, int radius,'),
    runtime.indexOf('public void ClearCityForgeStrategicMap()'),
  );
  const acceptedGround = runtime.slice(runtime.indexOf('private static void BuildStrategicAcceptedImpostor'), runtime.indexOf('private static void BuildStrategicChunkOutline'));
  const editorGround = runtime.slice(runtime.indexOf('private void BuildEditorGround('), runtime.indexOf('private void BuildRoads('));

  assert.match(layout, /CityForgeCoreGroundRect = Rect\.MinMaxRect\([\s\S]*CoreMinimum\.x \* SlopCityCityForge\.ChunkSizeMeters[\s\S]*CoreMaximum\.z \+ 1\) \* SlopCityCityForge\.ChunkSizeMeters/, 'the fixed pavement must use the protected core’s exact world limits');
  assert.match(layout, /OuterGroundRect = CityForgeCoreGroundRect;/, 'the Old City support apron must not use asymmetric hand-tuned bounds');
  assert.match(strategic, /!coordinate\.Equals\(center\) && acceptedPlans/, 'the selected chunk must not draw both accepted-map and draft ground');
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
