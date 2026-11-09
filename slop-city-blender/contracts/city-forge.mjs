/**
 * Shared, platform-neutral City Forge protocol.
 *
 * This module deliberately has no account, media, Unity, or database knowledge.
 * It defines the bounded values which may cross an offline save, a personal-world
 * API, and the shared runtime.  A city plan is a sparse delta, never a scene.
 */

export const CITY_FORGE_SCHEMA_VERSION = 2;
// Version 2 adds the non-negotiable, Old-City-facing base stubs.  A World Head
// must not silently reinterpret the v1 base hashes as though those links had
// always existed.
export const CITY_FORGE_GENERATOR_VERSION = 2;
// Reviewed content identity for this immutable one-shot catalog.  Runtime
// compatibility records carry this value; changing any catalog contract needs
// an explicit compatible catalog migration and a new reviewed identity.
export const CITY_FORGE_CATALOG_HASH = 'a4d8eaa6b9c52300a3739ffd2dc5d2173b6d51bccbbd30e6eb38586270a291ad';
export const CITY_FORGE_CHUNK_CELLS = 9;
export const CITY_FORGE_CELL_METERS = 8;
export const CITY_FORGE_CHUNK_METERS = CITY_FORGE_CHUNK_CELLS * CITY_FORGE_CELL_METERS;
// Server pages are one coordinate only. Keep the transport envelope aligned
// with the WebGL parser/local page cap: a maximum-size canonical plan (120 KiB)
// plus its compact head/page envelope remains safely below 128 KiB.
export const CITY_FORGE_PAGE_BYTES = Object.freeze({ local: 131072, server: 131072 });
export const CITY_FORGE_LIMITS = Object.freeze({
  coordinate: 1_000_000_000,
  roads: 180,
  parcels: 48,
  parcelCells: 81,
  buildings: 48,
  services: 48,
  // A chunk may expose all of its catalog-defined player fixture positions:
  // 48 five-slot buildings, their 48 compatible civic/culture/green site
  // supports, and every one of the 180 canonical road supports.
  // This is a per-chunk serialized-hardware ceiling, not a citywide TV quota;
  // moving media remains independently bounded by the admission layer.
  screens: 468,
  landmarks: 12,
  situations: 96,
  commandBytes: 131072,
  chunkBytes: 122880,
  pageItems: 100,
});

export const CITY_FORGE_ROAD_CLASSES = Object.freeze([
  'local-street', 'boulevard', 'service-alley', 'pedestrian-route', 'transit-service-corridor',
]);
/** The only fixed road class that may meet the immutable Old City boundary. */
export const CITY_FORGE_OLD_CITY_CONNECTION_CLASS = 'transit-service-corridor';
export const CITY_FORGE_ZONES = Object.freeze(['home', 'work-service', 'trade', 'civic', 'culture', 'green-infrastructure']);
export const CITY_FORGE_FACADES = Object.freeze([
  'wet-masonry', 'poured-concrete', 'glazed-ceramic', 'oxidized-metal',
  'painted-render', 'brick-utility', 'night-glass', 'garden-lattice',
]);
export const CITY_FORGE_ROOFS = Object.freeze([
  'service-roof', 'rain-garden', 'solar-rack', 'water-tank', 'relay-array', 'lantern-roof',
  'terrace', 'greenhouse', 'vent-stack', 'civic-canopy', 'screen-crown', 'utility-bridge',
]);
export const CITY_FORGE_BUILDINGS = Object.freeze([
  'walk-up', 'courtyard-home', 'tower-residence', 'live-work-block', 'workshop',
  'repair-bay', 'clinic', 'laundry', 'depot', 'small-office', 'corner-shop', 'market-hall',
  'cafe', 'exchange', 'kiosk', 'care-station', 'library-room', 'utility-office',
  'community-hall', 'screening-room', 'studio', 'venue', 'archive', 'performance-space',
  'garden', 'plaza', 'water-node', 'power-relay', 'shelter', 'service-yard',
]);
/** Approved interaction/service roles. A stored plan may never invent one. */
export const CITY_FORGE_SERVICE_TYPES = Object.freeze([
  'care-station', 'repair-counter', 'laundry-service', 'delivery-bay', 'neighborhood-office',
  'market-stall', 'food-counter', 'exchange-desk', 'library-desk', 'utility-desk',
  'community-room', 'screening-service', 'studio-service', 'venue-door', 'archive-desk',
  'garden-steward', 'water-service', 'power-service', 'shelter-desk', 'service-yard-bay',
]);
export const CITY_FORGE_INTERIOR_ROLES = Object.freeze([
  'home-room', 'workshop-floor', 'repair-counter', 'care-room', 'laundry-room',
  'depot-bay', 'office-floor', 'shop-floor', 'market-floor', 'cafe-room',
  'exchange-room', 'library-room', 'community-room', 'screening-room', 'studio-room',
  'venue-room', 'archive-room', 'performance-room', 'utility-room', 'shelter-room',
]);
export const CITY_FORGE_STOREFRONTS = Object.freeze(['none', 'shutter', 'awning', 'glazed', 'arcade', 'market-stall']);
/** One reviewed zone map for planners, API validation, and runtime compilers. */
export const CITY_FORGE_BUILDING_ZONES = Object.freeze({
  'walk-up': Object.freeze(['home']), 'courtyard-home': Object.freeze(['home']), 'tower-residence': Object.freeze(['home']), 'live-work-block': Object.freeze(['home', 'work-service', 'trade']),
  workshop: Object.freeze(['work-service']), 'repair-bay': Object.freeze(['work-service', 'green-infrastructure']), clinic: Object.freeze(['work-service', 'civic']), laundry: Object.freeze(['work-service']), depot: Object.freeze(['work-service', 'green-infrastructure']), 'small-office': Object.freeze(['work-service', 'trade']),
  'corner-shop': Object.freeze(['trade']), 'market-hall': Object.freeze(['trade']), cafe: Object.freeze(['trade', 'culture']), exchange: Object.freeze(['trade']), kiosk: Object.freeze(['trade', 'civic']),
  'care-station': Object.freeze(['civic']), 'library-room': Object.freeze(['civic', 'culture']), 'utility-office': Object.freeze(['civic', 'green-infrastructure']), 'community-hall': Object.freeze(['civic', 'culture']), 'screening-room': Object.freeze(['culture']), studio: Object.freeze(['culture']), venue: Object.freeze(['culture']), archive: Object.freeze(['culture', 'civic']), 'performance-space': Object.freeze(['culture']),
  garden: Object.freeze(['green-infrastructure']), plaza: Object.freeze(['green-infrastructure', 'civic']), 'water-node': Object.freeze(['green-infrastructure']), 'power-relay': Object.freeze(['green-infrastructure']), shelter: Object.freeze(['green-infrastructure', 'civic']), 'service-yard': Object.freeze(['green-infrastructure', 'work-service']),
});
export const CITY_FORGE_SCREEN_MOUNTS = Object.freeze([
  'facade-media-wall', 'interior-wall-tv', 'plaza-civic-support', 'street-kiosk-screen',
]);
export const CITY_FORGE_SCREEN_DEVICES = Object.freeze([
  'public-crt-01', 'public-led-01', 'public-projector-01', 'public-window-01',
]);
/**
 * Fixed physical fixture availability.  A player stores only a stable host and
 * mount ID; the compiler derives the actual support, orientation, collider,
 * material, audio range, and screen group from this shipped catalog.
 */
export const CITY_FORGE_BUILDING_SCREEN_MOUNTS = Object.freeze({
  'walk-up': Object.freeze(['interior-wall-tv']),
  'courtyard-home': Object.freeze(['interior-wall-tv']),
  'tower-residence': Object.freeze(['interior-wall-tv']),
  'live-work-block': Object.freeze(['facade-media-wall', 'interior-wall-tv']),
  workshop: Object.freeze(['interior-wall-tv']),
  'repair-bay': Object.freeze(['facade-media-wall']),
  clinic: Object.freeze(['interior-wall-tv']),
  laundry: Object.freeze(['interior-wall-tv']),
  depot: Object.freeze(['facade-media-wall']),
  'small-office': Object.freeze(['interior-wall-tv']),
  'corner-shop': Object.freeze(['facade-media-wall', 'interior-wall-tv']),
  'market-hall': Object.freeze(['facade-media-wall', 'interior-wall-tv']),
  cafe: Object.freeze(['interior-wall-tv']),
  exchange: Object.freeze(['facade-media-wall']),
  kiosk: Object.freeze(['facade-media-wall']),
  'care-station': Object.freeze(['interior-wall-tv']),
  'library-room': Object.freeze(['interior-wall-tv']),
  'utility-office': Object.freeze(['interior-wall-tv']),
  'community-hall': Object.freeze(['facade-media-wall', 'interior-wall-tv']),
  'screening-room': Object.freeze(['facade-media-wall', 'interior-wall-tv']),
  studio: Object.freeze(['interior-wall-tv']),
  venue: Object.freeze(['facade-media-wall', 'interior-wall-tv']),
  archive: Object.freeze(['interior-wall-tv']),
  'performance-space': Object.freeze(['facade-media-wall', 'interior-wall-tv']),
  garden: Object.freeze(['plaza-civic-support']),
  plaza: Object.freeze(['plaza-civic-support']),
  'water-node': Object.freeze(['plaza-civic-support']),
  'power-relay': Object.freeze(['facade-media-wall']),
  shelter: Object.freeze(['plaza-civic-support']),
  'service-yard': Object.freeze(['facade-media-wall']),
});
/**
 * The number of shipped physical player-screen positions on a building host.
 * Screen records never carry a transform or a caller-chosen slot: positions
 * are allocated by stable `screenId` order within the host. Keep this exact
 * map in parity with SlopCityCityForgeCatalog's `screenSlots` values.
 */
export const CITY_FORGE_BUILDING_SCREEN_SLOTS = Object.freeze({
  'walk-up': 1,
  'courtyard-home': 2,
  'tower-residence': 4,
  'live-work-block': 3,
  workshop: 1,
  'repair-bay': 1,
  clinic: 2,
  laundry: 1,
  depot: 1,
  'small-office': 2,
  'corner-shop': 2,
  'market-hall': 4,
  cafe: 2,
  exchange: 2,
  kiosk: 1,
  'care-station': 2,
  'library-room': 2,
  'utility-office': 1,
  'community-hall': 4,
  'screening-room': 5,
  studio: 3,
  venue: 5,
  archive: 2,
  'performance-space': 4,
  garden: 1,
  plaza: 4,
  'water-node': 1,
  'power-relay': 2,
  shelter: 1,
  'service-yard': 1,
});
/** `site-<parcel-id>` names a non-building civic/plaza support. */
export const CITY_FORGE_SITE_SCREEN_ZONES = Object.freeze(['civic', 'culture', 'green-infrastructure']);
/** One physical plaza support is currently compiled for each allowed site. */
export const CITY_FORGE_SITE_SCREEN_SLOTS = Object.freeze({
  civic: 1,
  culture: 1,
  'green-infrastructure': 1,
});
/** One physical kiosk support is currently compiled for each road host. */
export const CITY_FORGE_ROAD_SCREEN_SLOTS = Object.freeze({
  'local-street': 1,
  boulevard: 1,
  'service-alley': 1,
  'pedestrian-route': 1,
  'transit-service-corridor': 1,
});
export const CITY_FORGE_LANDMARKS = Object.freeze([
  'home-workroom', 'service-court', 'street-corner', 'transit-landing', 'roof-reservoir',
  'tradefront', 'utility-alley', 'screen-room', 'back-lobby', 'signal-terrace',
]);
/** One catalog-defined Cityworks placement grammar; a landmark cannot turn an
 * unrelated lot into an arbitrary scenario host. */
export const CITY_FORGE_LANDMARK_ZONES = Object.freeze({
  'home-workroom': Object.freeze(['home']),
  'service-court': Object.freeze(['work-service']),
  'street-corner': Object.freeze(['civic', 'green-infrastructure']),
  'transit-landing': Object.freeze(['work-service', 'civic']),
  'roof-reservoir': Object.freeze(['green-infrastructure', 'home', 'work-service']),
  tradefront: Object.freeze(['trade']),
  'utility-alley': Object.freeze(['green-infrastructure', 'work-service']),
  'screen-room': Object.freeze(['culture']),
  'back-lobby': Object.freeze(['civic', 'home']),
  'signal-terrace': Object.freeze(['civic', 'culture', 'green-infrastructure']),
});
export const CITY_FORGE_LANDMARK_OUTCOMES = Object.freeze({
  'home-workroom': Object.freeze(['repair-loft', 'listening-room', 'green-refuge']),
  'service-court': Object.freeze(['repair-cooperative', 'mutual-aid-washhouse', 'after-hours-room']),
  'street-corner': Object.freeze(['light-sanctuary', 'street-garden', 'notice-arcade']),
  'transit-landing': Object.freeze(['rider-support-post', 'small-exhibition-platform', 'connector-lounge']),
  'roof-reservoir': Object.freeze(['water-garden', 'relay-workshop', 'observation-room']),
  'tradefront': Object.freeze(['used-goods-exchange', 'neighborhood-kitchen', 'community-studio']),
  'utility-alley': Object.freeze(['machine-bay', 'supply-cache', 'mural-passage']),
  'screen-room': Object.freeze(['local-archive', 'screening-club', 'quiet-learning-room']),
  'back-lobby': Object.freeze(['welcome-desk', 'care-station-outcome', 'night-office']),
  'signal-terrace': Object.freeze(['signal-workshop', 'rest-terrace', 'weather-shelter']),
});
export const CITY_FORGE_CITYWORKS_STATES = Object.freeze([
  'unavailable', 'discovered', 'diagnosing', 'chosen', 'active', 'opening', 'established', 'paused', 'renovating',
]);
export const CITY_FORGE_SITUATION_STATES = Object.freeze([
  'unavailable', 'available', 'active', 'resolved', 'paused', 'archived',
]);
/**
 * Cityworks and situations are authored finite-state systems, not generic
 * quest flags.  Initial placement is intentionally narrow; later transitions
 * are checked against the preceding accepted Chunk Plan by both offline and
 * hosted resolvers.
 */
export const CITY_FORGE_CITYWORKS_INITIAL_STATES = Object.freeze(['unavailable', 'discovered', 'diagnosing', 'chosen']);
export const CITY_FORGE_CITYWORKS_TRANSITIONS = Object.freeze({
  unavailable: Object.freeze(['discovered']),
  discovered: Object.freeze(['diagnosing', 'paused']),
  diagnosing: Object.freeze(['chosen', 'paused']),
  chosen: Object.freeze(['active', 'paused', 'renovating']),
  active: Object.freeze(['opening', 'paused', 'renovating']),
  opening: Object.freeze(['established', 'paused', 'renovating']),
  established: Object.freeze(['paused', 'renovating']),
  paused: Object.freeze(['diagnosing', 'chosen', 'active', 'opening', 'established', 'renovating']),
  renovating: Object.freeze(['active', 'opening', 'established', 'paused']),
});
export const CITY_FORGE_SITUATION_INITIAL_STATES = Object.freeze(['unavailable', 'available']);
export const CITY_FORGE_SITUATION_TRANSITIONS = Object.freeze({
  unavailable: Object.freeze(['available', 'archived']),
  available: Object.freeze(['active', 'paused', 'archived']),
  active: Object.freeze(['resolved', 'paused', 'archived']),
  resolved: Object.freeze(['archived']),
  paused: Object.freeze(['available', 'active', 'resolved', 'archived']),
  archived: Object.freeze([]),
});
const outcomeSituationEntries = Object.values(CITY_FORGE_LANDMARK_OUTCOMES)
  .flatMap(outcomes => outcomes.flatMap(outcome => [
    `${outcome}-arrival`, `${outcome}-continuance`,
  ]));
const crossSituationEntries = [
  'repair-care-connection', 'repair-culture-connection', 'repair-access-connection', 'care-culture-connection',
  'care-access-connection', 'care-pressure-connection', 'culture-access-connection', 'culture-pressure-connection',
  'access-pressure-connection', 'garden-water-connection', 'garden-shelter-connection', 'screen-archive-connection',
  'screen-night-connection', 'transit-trade-connection', 'transit-care-connection', 'utility-repair-connection',
  'utility-weather-connection', 'home-trade-connection', 'lobby-welcome-connection', 'signal-roof-connection',
];
const frozenSituationRequirement = (anchorOutcomes, requiredOutcomes, requiredStates) => Object.freeze({
  anchorOutcomes: Object.freeze([...anchorOutcomes]),
  requiredOutcomes: Object.freeze([...requiredOutcomes]),
  requiredStates: Object.freeze([...requiredStates]),
});
const outcomeSituationRequirements = Object.fromEntries(Object.values(CITY_FORGE_LANDMARK_OUTCOMES)
  .flatMap(outcomes => outcomes.flatMap(outcome => [
    [`${outcome}-arrival`, frozenSituationRequirement([outcome], [outcome], ['active', 'opening', 'established'])],
    [`${outcome}-continuance`, frozenSituationRequirement([outcome], [outcome], ['active', 'opening', 'established'])],
  ])));
const crossSituationRequirements = Object.freeze({
  'repair-care-connection': frozenSituationRequirement(['repair-cooperative', 'mutual-aid-washhouse'], ['repair-cooperative', 'mutual-aid-washhouse'], ['established']),
  'repair-culture-connection': frozenSituationRequirement(['repair-loft', 'local-archive'], ['repair-loft', 'local-archive'], ['established']),
  'repair-access-connection': frozenSituationRequirement(['repair-cooperative', 'rider-support-post'], ['repair-cooperative', 'rider-support-post'], ['established']),
  'care-culture-connection': frozenSituationRequirement(['mutual-aid-washhouse', 'screening-club'], ['mutual-aid-washhouse', 'screening-club'], ['established']),
  'care-access-connection': frozenSituationRequirement(['care-station-outcome', 'rider-support-post'], ['care-station-outcome', 'rider-support-post'], ['established']),
  'care-pressure-connection': frozenSituationRequirement(['mutual-aid-washhouse', 'night-office'], ['mutual-aid-washhouse', 'night-office'], ['established']),
  'culture-access-connection': frozenSituationRequirement(['local-archive', 'connector-lounge'], ['local-archive', 'connector-lounge'], ['established']),
  'culture-pressure-connection': frozenSituationRequirement(['after-hours-room', 'night-office'], ['after-hours-room', 'night-office'], ['established']),
  'access-pressure-connection': frozenSituationRequirement(['signal-workshop', 'weather-shelter'], ['signal-workshop', 'weather-shelter'], ['established']),
  'garden-water-connection': frozenSituationRequirement(['street-garden', 'water-garden'], ['street-garden', 'water-garden'], ['established']),
  'garden-shelter-connection': frozenSituationRequirement(['green-refuge', 'weather-shelter'], ['green-refuge', 'weather-shelter'], ['established']),
  'screen-archive-connection': frozenSituationRequirement(['screening-club', 'local-archive'], ['screening-club', 'local-archive'], ['established']),
  'screen-night-connection': frozenSituationRequirement(['screening-club', 'night-office'], ['screening-club', 'night-office'], ['established']),
  'transit-trade-connection': frozenSituationRequirement(['rider-support-post', 'used-goods-exchange'], ['rider-support-post', 'used-goods-exchange'], ['established']),
  'transit-care-connection': frozenSituationRequirement(['connector-lounge', 'care-station-outcome'], ['connector-lounge', 'care-station-outcome'], ['established']),
  'utility-repair-connection': frozenSituationRequirement(['relay-workshop', 'machine-bay'], ['relay-workshop', 'machine-bay'], ['established']),
  'utility-weather-connection': frozenSituationRequirement(['water-garden', 'weather-shelter'], ['water-garden', 'weather-shelter'], ['established']),
  'home-trade-connection': frozenSituationRequirement(['listening-room', 'community-studio'], ['listening-room', 'community-studio'], ['established']),
  'lobby-welcome-connection': frozenSituationRequirement(['welcome-desk', 'care-station-outcome'], ['welcome-desk', 'care-station-outcome'], ['established']),
  'signal-roof-connection': frozenSituationRequirement(['signal-workshop', 'observation-room'], ['signal-workshop', 'observation-room'], ['established']),
});
/**
 * Eighty authored situation templates: two consequence hooks for each of the
 * thirty landmark outcomes, plus twenty cross-site situations.  A plan stores
 * only one of these stable IDs, a seed, an anchor landmark, and lifecycle
 * state—never generated writing, arbitrary objectives, or free-form payloads.
 */
export const CITY_FORGE_SITUATIONS = Object.freeze([...outcomeSituationEntries, ...crossSituationEntries]);
/** Every situation has an authored anchor and prerequisite set. */
export const CITY_FORGE_SITUATION_REQUIREMENTS = Object.freeze({
  ...outcomeSituationRequirements,
  ...crossSituationRequirements,
});

const ID = /^[a-z][a-z0-9-]{0,63}$/;
const SHA = /^[a-f0-9]{64}$/;
const CELL = /^([0-8]),([0-8])$/;
const EDGE = /^([0-8]),([0-8]):(north|east|south|west)$/;
const DIRECTIONS = Object.freeze(['north', 'east', 'south', 'west']);
const ROAD_SET = new Set(CITY_FORGE_ROAD_CLASSES);
const ZONE_SET = new Set(CITY_FORGE_ZONES);
const FACADE_SET = new Set(CITY_FORGE_FACADES);
const ROOF_SET = new Set(CITY_FORGE_ROOFS);
const BUILDING_SET = new Set(CITY_FORGE_BUILDINGS);
const SERVICE_SET = new Set(CITY_FORGE_SERVICE_TYPES);
const INTERIOR_SET = new Set(CITY_FORGE_INTERIOR_ROLES);
const STOREFRONT_SET = new Set(CITY_FORGE_STOREFRONTS);
const MOUNT_SET = new Set(CITY_FORGE_SCREEN_MOUNTS);
const DEVICE_SET = new Set(CITY_FORGE_SCREEN_DEVICES);
const LANDMARK_SET = new Set(CITY_FORGE_LANDMARKS);
const CITYWORKS_STATE_SET = new Set(CITY_FORGE_CITYWORKS_STATES);
const SITUATION_SET = new Set(CITY_FORGE_SITUATIONS);
const SITUATION_STATE_SET = new Set(CITY_FORGE_SITUATION_STATES);
const SITE_SCREEN_ZONE_SET = new Set(CITY_FORGE_SITE_SCREEN_ZONES);
const hasOwn = (object, key) => Object.prototype.hasOwnProperty.call(object, key);
const plainObject = value => value !== null && typeof value === 'object' && !Array.isArray(value)
  && (Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null);
const textBytes = value => new TextEncoder().encode(value).byteLength;

function fail(errors, path, message) { errors.push({ path, message }); }
function exactObject(value, path, required, optional, errors) {
  if (!plainObject(value)) { fail(errors, path, 'must be an object'); return false; }
  const allowed = new Set([...required, ...optional]);
  for (const key of required) if (!hasOwn(value, key)) fail(errors, `${path}.${key}`, 'is required');
  for (const key of Object.keys(value)) if (!allowed.has(key)) fail(errors, `${path}.${key}`, 'is not allowed');
  return true;
}
function safeInteger(value, path, min, max, errors) {
  if (!Number.isSafeInteger(value) || value < min || value > max) {
    fail(errors, path, `must be an integer between ${min} and ${max}`);
    return false;
  }
  return true;
}
function identifier(value, path, errors) {
  if (typeof value !== 'string' || !ID.test(value)) { fail(errors, path, 'must be a bounded catalog identifier'); return false; }
  return true;
}
function digest(value, path, errors) {
  if (typeof value !== 'string' || !SHA.test(value)) { fail(errors, path, 'must be a SHA-256 digest'); return false; }
  return true;
}
function boundedArray(value, path, max, errors) {
  if (!Array.isArray(value) || value.length > max) { fail(errors, path, `must be an array of at most ${max} entries`); return null; }
  return value;
}
function uniqueIds(rows, field, path, errors) {
  const seen = new Set();
  for (let index = 0; index < rows.length; index += 1) {
    const id = rows[index]?.[field];
    if (typeof id === 'string') {
      if (seen.has(id)) fail(errors, `${path}[${index}].${field}`, 'must be unique in this chunk');
      seen.add(id);
    }
  }
  return seen;
}
/** A core coordinate is protected forever; the surrounding twenty chunks are immediate starter territory. */
export function isOldCityCoordinate(x, z) { return x >= -2 && x <= 1 && z >= -2 && z <= 1; }
export function isStarterRingCoordinate(x, z) { return x >= -3 && x <= 2 && z >= -3 && z <= 2 && !isOldCityCoordinate(x, z); }
export function isReservedCoordinate(x, z) { return isOldCityCoordinate(x, z) || isStarterRingCoordinate(x, z); }
export function coordinateKey(x, z) { return `${x},${z}`; }
export function parseCoordinateKey(value) {
  const match = typeof value === 'string' && /^(-?\d+),(-?\d+)$/.exec(value);
  if (!match) return null;
  const x = Number(match[1]), z = Number(match[2]);
  return Number.isSafeInteger(x) && Number.isSafeInteger(z) && Math.abs(x) <= CITY_FORGE_LIMITS.coordinate && Math.abs(z) <= CITY_FORGE_LIMITS.coordinate ? { x, z } : null;
}

/**
 * Return the one generated road edge through which a starter block connects to
 * the authored Old City, or null when the coordinate does not share a side
 * with it. Corners deliberately have no core-facing edge. The selected edges
 * are the same canonical representatives used by generateBaseChunk().
 */
export function oldCityConnectionStub(x, z) {
  if (x === -3 && z >= -2 && z <= 1) return Object.freeze({ edge: '8,0:east', class: CITY_FORGE_OLD_CITY_CONNECTION_CLASS });
  if (x === 2 && z >= -2 && z <= 1) return Object.freeze({ edge: '0,0:west', class: CITY_FORGE_OLD_CITY_CONNECTION_CLASS });
  if (z === -3 && x >= -2 && x <= 1) return Object.freeze({ edge: '0,8:north', class: CITY_FORGE_OLD_CITY_CONNECTION_CLASS });
  if (z === 2 && x >= -2 && x <= 1) return Object.freeze({ edge: '0,0:south', class: CITY_FORGE_OLD_CITY_CONNECTION_CLASS });
  return null;
}

function edgeEndpoint(x, z, edge) {
  const match = EDGE.exec(edge);
  if (!match) return null;
  const cellX = Number(match[1]), cellZ = Number(match[2]), direction = match[3];
  let otherX = x, otherZ = z, otherCellX = cellX, otherCellZ = cellZ, otherDirection;
  if (direction === 'north') { otherDirection = 'south'; if (cellZ === CITY_FORGE_CHUNK_CELLS - 1) { otherZ += 1; otherCellZ = 0; } else otherCellZ += 1; }
  if (direction === 'south') { otherDirection = 'north'; if (cellZ === 0) { otherZ -= 1; otherCellZ = CITY_FORGE_CHUNK_CELLS - 1; } else otherCellZ -= 1; }
  if (direction === 'east') { otherDirection = 'west'; if (cellX === CITY_FORGE_CHUNK_CELLS - 1) { otherX += 1; otherCellX = 0; } else otherCellX += 1; }
  if (direction === 'west') { otherDirection = 'east'; if (cellX === 0) { otherX -= 1; otherCellX = CITY_FORGE_CHUNK_CELLS - 1; } else otherCellX -= 1; }
  return { a: { x, z, edge }, b: { x: otherX, z: otherZ, edge: `${otherCellX},${otherCellZ}:${otherDirection}` } };
}

/** The same physical border always has exactly one lexicographically ordered key. */
export function canonicalBoundaryEdgeKey(x, z, edge) {
  const pair = edgeEndpoint(x, z, edge);
  if (!pair) throw new TypeError('Invalid City Forge edge.');
  const a = `${coordinateKey(pair.a.x, pair.a.z)}:${pair.a.edge}`;
  const b = `${coordinateKey(pair.b.x, pair.b.z)}:${pair.b.edge}`;
  return a < b ? `${a}|${b}` : `${b}|${a}`;
}

export function isBoundaryEdge(edge) {
  const match = EDGE.exec(edge);
  if (!match) return false;
  const x = Number(match[1]), z = Number(match[2]);
  return (match[3] === 'north' && z === 8) || (match[3] === 'south' && z === 0)
    || (match[3] === 'east' && x === 8) || (match[3] === 'west' && x === 0);
}

function validateRoads(rows, path, errors) {
  const values = boundedArray(rows, path, CITY_FORGE_LIMITS.roads, errors) ?? [];
  const seen = new Set();
  for (let index = 0; index < values.length; index += 1) {
    const row = values[index], p = `${path}[${index}]`;
    if (!exactObject(row, p, ['edge', 'class'], [], errors)) continue;
    if (typeof row.edge !== 'string' || !EDGE.test(row.edge) || canonicalRoadEdge(row.edge) !== row.edge) fail(errors, `${p}.edge`, 'must be a canonical local cardinal grid edge');
    if (!ROAD_SET.has(row.class)) fail(errors, `${p}.class`, 'must be an approved road class');
    if (seen.has(row.edge)) fail(errors, `${p}.edge`, 'must occur once per chunk');
    seen.add(row.edge);
  }
}
/** Store internal edges once, from their south/west cell; keep outer edges outward. */
export function canonicalRoadEdge(edge) {
  const match = EDGE.exec(edge ?? '');
  if (!match) throw new TypeError('Invalid City Forge road edge.');
  let x = Number(match[1]), z = Number(match[2]), direction = match[3];
  if (direction === 'west' && x > 0) { x -= 1; direction = 'east'; }
  if (direction === 'south' && z > 0) { z -= 1; direction = 'north'; }
  return `${x},${z}:${direction}`;
}
function validateRemovedRoadEdges(rows, roads, path, errors) {
  const values = boundedArray(rows, path, CITY_FORGE_LIMITS.roads, errors) ?? [];
  const active = new Set((roads ?? []).map(row => row?.edge));
  const seen = new Set();
  for (let index = 0; index < values.length; index += 1) {
    const edge = values[index], p = `${path}[${index}]`;
    if (typeof edge !== 'string' || !EDGE.test(edge) || canonicalRoadEdge(edge) !== edge) fail(errors, p, 'must be a canonical local cardinal grid edge');
    if (seen.has(edge)) fail(errors, p, 'must occur once per chunk');
    if (active.has(edge)) fail(errors, p, 'cannot also be an active road');
    seen.add(edge);
  }
}
function validateParcels(rows, path, errors) {
  const values = boundedArray(rows, path, CITY_FORGE_LIMITS.parcels, errors) ?? [];
  uniqueIds(values, 'id', path, errors);
  const occupied = new Set();
  for (let index = 0; index < values.length; index += 1) {
    const row = values[index], p = `${path}[${index}]`;
    if (!exactObject(row, p, ['id', 'cells', 'zone'], [], errors)) continue;
    identifier(row.id, `${p}.id`, errors);
    if (!ZONE_SET.has(row.zone)) fail(errors, `${p}.zone`, 'must be an approved parcel use');
    const cells = boundedArray(row.cells, `${p}.cells`, CITY_FORGE_LIMITS.parcelCells, errors) ?? [];
    if (!cells.length) fail(errors, `${p}.cells`, 'must contain at least one cell');
    const local = new Set();
    for (const cell of cells) {
      if (typeof cell !== 'string' || !CELL.test(cell)) { fail(errors, `${p}.cells`, 'must use local 0–8 grid cells'); continue; }
      if (local.has(cell) || occupied.has(cell)) fail(errors, `${p}.cells`, 'may not overlap another parcel');
      local.add(cell); occupied.add(cell);
    }
  }
}
function parcelHasRoadFrontage(parcel, roads, entranceEdge = null) {
  const activeEdges = new Set((roads ?? []).map(row => row?.edge));
  for (const cell of parcel?.cells ?? []) {
    const [x, z] = String(cell).split(',').map(Number);
    for (const direction of DIRECTIONS) {
      if (entranceEdge !== null && direction !== entranceEdge) continue;
      if (activeEdges.has(canonicalRoadEdge(`${x},${z}:${direction}`))) return true;
    }
  }
  return false;
}
function validateBuildings(rows, parcels, roads, path, errors) {
  const values = boundedArray(rows, path, CITY_FORGE_LIMITS.buildings, errors) ?? [];
  uniqueIds(values, 'id', path, errors);
  const parcelIds = new Set(parcels.map(row => row?.id));
  const occupiedParcels = new Set();
  for (let index = 0; index < values.length; index += 1) {
    const row = values[index], p = `${path}[${index}]`;
    if (!exactObject(row, p, ['id', 'parcelId', 'blueprintId', 'facadeId', 'roofId', 'floors', 'entranceEdge'], ['interiorRole', 'storefrontId'], errors)) continue;
    identifier(row.id, `${p}.id`, errors); identifier(row.parcelId, `${p}.parcelId`, errors);
    const parcel = parcels.find(candidate => candidate?.id === row.parcelId);
    if (!parcelIds.has(row.parcelId)) fail(errors, `${p}.parcelId`, 'must refer to a parcel in this chunk');
    else if (occupiedParcels.has(row.parcelId)) fail(errors, `${p}.parcelId`, 'may hold only one building');
    occupiedParcels.add(row.parcelId);
    if (!BUILDING_SET.has(row.blueprintId)) fail(errors, `${p}.blueprintId`, 'must be a shipped building blueprint');
    if (!CITY_FORGE_BUILDING_ZONES[row.blueprintId]?.includes((parcels.find(parcel => parcel?.id === row.parcelId))?.zone)) fail(errors, `${p}.blueprintId`, 'must fit the selected parcel use');
    if (!FACADE_SET.has(row.facadeId)) fail(errors, `${p}.facadeId`, 'must be a shipped facade');
    if (!ROOF_SET.has(row.roofId)) fail(errors, `${p}.roofId`, 'must be a shipped roof or utility module');
    safeInteger(row.floors, `${p}.floors`, 1, 24, errors);
    if (!DIRECTIONS.includes(row.entranceEdge)) fail(errors, `${p}.entranceEdge`, 'must face a cardinal street edge');
    else if (!parcelHasRoadFrontage(parcel, roads, row.entranceEdge)) fail(errors, `${p}.entranceEdge`, 'must face an active street edge');
    if (hasOwn(row, 'interiorRole') && !INTERIOR_SET.has(row.interiorRole)) fail(errors, `${p}.interiorRole`, 'must be a shipped interior role');
    if (hasOwn(row, 'storefrontId') && !STOREFRONT_SET.has(row.storefrontId)) fail(errors, `${p}.storefrontId`, 'must be a shipped storefront');
  }
}
function validateServices(rows, parcels, roads, path, errors) {
  const values = boundedArray(rows, path, CITY_FORGE_LIMITS.services, errors) ?? [];
  uniqueIds(values, 'id', path, errors);
  const parcelIds = new Set(parcels.map(row => row?.id));
  for (let index = 0; index < values.length; index += 1) {
    const row = values[index], p = `${path}[${index}]`;
    if (!exactObject(row, p, ['id', 'type', 'parcelId'], [], errors)) continue;
    identifier(row.id, `${p}.id`, errors); identifier(row.parcelId, `${p}.parcelId`, errors);
    if (!SERVICE_SET.has(row.type)) fail(errors, `${p}.type`, 'must be a shipped service role');
    const parcel = parcels.find(candidate => candidate?.id === row.parcelId);
    if (!parcelIds.has(row.parcelId)) fail(errors, `${p}.parcelId`, 'must refer to a parcel in this chunk');
    else if (!parcelHasRoadFrontage(parcel, roads)) fail(errors, `${p}.parcelId`, 'must have public street access');
  }
}
function screenRoadHost(hostId) {
  const match = /^road-([0-8])-([0-8])-(north|east|south|west)$/.exec(hostId ?? '');
  return match ? `${match[1]},${match[2]}:${match[3]}` : null;
}
function screenSiteHost(hostId) {
  const match = /^site-([a-z][a-z0-9-]{0,58})$/.exec(hostId ?? '');
  return match ? match[1] : null;
}
/**
 * Player-authored hardware may target a shipped building fixture, a derived
 * civic/plaza site fixture (`site-<parcel-id>`), or a derived road fixture
 * (`road-<x>-<z>-<direction>`). The returned capacity is a property of that
 * shipped host, never a caller-selected physical slot. The grammar deliberately
 * leaves no place for transforms, scene paths, raw media IDs, or arbitrary
 * host names.
 */
export function screenHostSpec(hostId, screenMountId, buildings, parcels, roads) {
  const building = new Map((buildings ?? []).map(row => [row?.id, row])).get(hostId);
  if (building) {
    const screenSlots = CITY_FORGE_BUILDING_SCREEN_SLOTS[building.blueprintId];
    return CITY_FORGE_BUILDING_SCREEN_MOUNTS[building.blueprintId]?.includes(screenMountId) && Number.isSafeInteger(screenSlots) && screenSlots > 0
      ? Object.freeze({ hostKey: hostId, screenSlots })
      : null;
  }
  if (screenMountId === 'plaza-civic-support') {
    const parcelId = screenSiteHost(hostId);
    const parcel = new Map((parcels ?? []).map(row => [row?.id, row])).get(parcelId);
    const screenSlots = parcel && CITY_FORGE_SITE_SCREEN_SLOTS[parcel.zone];
    return parcel && SITE_SCREEN_ZONE_SET.has(parcel.zone) && Number.isSafeInteger(screenSlots) && screenSlots > 0
      ? Object.freeze({ hostKey: hostId, screenSlots })
      : null;
  }
  if (screenMountId === 'street-kiosk-screen') {
    const edge = screenRoadHost(hostId);
    const road = edge && (roads ?? []).find(row => row?.edge === edge);
    const screenSlots = road && CITY_FORGE_ROAD_SCREEN_SLOTS[road.class];
    return road && Number.isSafeInteger(screenSlots) && screenSlots > 0
      ? Object.freeze({ hostKey: hostId, screenSlots })
      : null;
  }
  return null;
}

/** Backward-compatible boolean host guard for callers that need no capacity. */
export function validateScreenHost(hostId, screenMountId, buildings, parcels, roads) {
  return screenHostSpec(hostId, screenMountId, buildings, parcels, roads) !== null;
}

/**
 * Derive physical player-screen slots without persisting a raw slot ID. Every
 * host's screens receive positions by ordinal `screenId` order, regardless of
 * request order or mount type. `admitted: false` means the record exceeds the
 * finite set of catalog-defined positions and must be rejected by a validator.
 */
export function screenSlotAssignments(rows, buildings, parcels, roads) {
  const groups = new Map();
  for (const row of rows ?? []) {
    const spec = screenHostSpec(row?.hostId, row?.screenMountId, buildings, parcels, roads);
    if (!spec || typeof row?.screenId !== 'string') return null;
    const group = groups.get(spec.hostKey) ?? { spec, rows: [] };
    group.rows.push(row);
    groups.set(spec.hostKey, group);
  }
  const compare = (left, right) => left < right ? -1 : left > right ? 1 : 0;
  const assignments = [];
  for (const [hostKey, group] of [...groups.entries()].sort(([left], [right]) => compare(left, right))) {
    for (const [slotIndex, row] of [...group.rows].sort((left, right) => compare(left.screenId, right.screenId)).entries()) {
      assignments.push(Object.freeze({
        screenId: row.screenId,
        hostId: hostKey,
        screenMountId: row.screenMountId,
        slotIndex,
        slotCount: group.spec.screenSlots,
        admitted: slotIndex < group.spec.screenSlots,
      }));
    }
  }
  return Object.freeze(assignments);
}

function validateScreens(rows, buildings, parcels, roads, path, errors) {
  const values = boundedArray(rows, path, CITY_FORGE_LIMITS.screens, errors) ?? [];
  uniqueIds(values, 'screenId', path, errors);
  const candidates = [];
  for (let index = 0; index < values.length; index += 1) {
    const row = values[index], p = `${path}[${index}]`;
    if (!exactObject(row, p, ['screenId', 'hostId', 'screenMountId', 'deviceId'], [], errors)) continue;
    identifier(row.screenId, `${p}.screenId`, errors); identifier(row.hostId, `${p}.hostId`, errors);
    if (!MOUNT_SET.has(row.screenMountId)) fail(errors, `${p}.screenMountId`, 'must be an approved physical mount');
    if (!DEVICE_SET.has(row.deviceId)) fail(errors, `${p}.deviceId`, 'must be an approved physical device');
    if (!screenHostSpec(row.hostId, row.screenMountId, buildings, parcels, roads)) fail(errors, `${p}.hostId`, 'must refer to a compatible shipped building, civic/plaza, or road host');
    else candidates.push({ row, index });
  }
  const assignments = screenSlotAssignments(candidates.map(candidate => candidate.row), buildings, parcels, roads);
  const assignmentsById = new Map((assignments ?? []).map(assignment => [assignment.screenId, assignment]));
  for (const candidate of candidates) {
    const assignment = assignmentsById.get(candidate.row.screenId);
    if (assignment && !assignment.admitted)
      fail(errors, `${path}[${candidate.index}].screenId`, `exceeds this shipped host's ${assignment.slotCount} physical screen slots`);
  }
}
function validateLandmarks(rows, parcels, path, errors) {
  const values = boundedArray(rows, path, CITY_FORGE_LIMITS.landmarks, errors) ?? [];
  uniqueIds(values, 'id', path, errors);
  const parcelsById = new Map((parcels ?? []).filter(row => row?.id).map(row => [row.id, row]));
  const occupiedParcels = new Set();
  for (let index = 0; index < values.length; index += 1) {
    const row = values[index], p = `${path}[${index}]`;
    if (!exactObject(row, p, ['id', 'classId', 'outcomeId', 'parcelId', 'state'], ['stableSeed'], errors)) continue;
    identifier(row.id, `${p}.id`, errors); identifier(row.classId, `${p}.classId`, errors); identifier(row.outcomeId, `${p}.outcomeId`, errors); identifier(row.parcelId, `${p}.parcelId`, errors);
    if (!LANDMARK_SET.has(row.classId) || !CITY_FORGE_LANDMARK_OUTCOMES[row.classId]?.includes(row.outcomeId)) fail(errors, p, 'must use a compatible shipped landmark outcome');
    const parcel = parcelsById.get(row.parcelId);
    if (!parcel) fail(errors, `${p}.parcelId`, 'must refer to a parcel in this chunk');
    else if (!CITY_FORGE_LANDMARK_ZONES[row.classId]?.includes(parcel.zone)) fail(errors, `${p}.parcelId`, 'must use a catalog-compatible Cityworks parcel');
    if (occupiedParcels.has(row.parcelId)) fail(errors, `${p}.parcelId`, 'may host one Cityworks project');
    occupiedParcels.add(row.parcelId);
    if (!CITYWORKS_STATE_SET.has(row.state)) fail(errors, `${p}.state`, 'must be a valid Cityworks state');
    if (hasOwn(row, 'stableSeed') && (typeof row.stableSeed !== 'string' || !/^[a-f0-9]{16,128}$/.test(row.stableSeed))) fail(errors, `${p}.stableSeed`, 'must be a stable hexadecimal seed');
  }
}
/**
 * Returns true only when a stored situation is anchored to its authored
 * outcome and all of its shipped prerequisites are live. Cross-site rules use
 * established landmarks; ordinary outcome follow-ons may begin while their
 * project is active or opening. Archived records remain historical evidence
 * and are allowed to outlive a later paused/renovating project.
 */
export function situationRequirementsMet(situation, landmarks) {
  const requirement = CITY_FORGE_SITUATION_REQUIREMENTS[situation?.situationId];
  if (!requirement) return false;
  const values = (landmarks ?? []).filter(Boolean);
  const anchor = values.find(landmark => landmark.id === situation?.landmarkId);
  if (!anchor || !requirement.anchorOutcomes.includes(anchor.outcomeId)) return false;
  if (situation?.state === 'archived') return true;
  return requirement.requiredOutcomes.every(outcome => values.some(landmark =>
    landmark.outcomeId === outcome && requirement.requiredStates.includes(landmark.state)));
}
/** Deterministically derives only consequences supported by accepted landmarks. */
export function availableSituationTemplates(plans) {
  const landmarks = (plans ?? []).flatMap(plan => plan?.landmarks ?? []).filter(Boolean);
  const available = new Set();
  for (const landmark of landmarks) {
    for (const [situationId, requirement] of Object.entries(CITY_FORGE_SITUATION_REQUIREMENTS)) {
      if (!requirement.anchorOutcomes.includes(landmark.outcomeId)) continue;
      if (situationRequirementsMet({ situationId, landmarkId: landmark.id, state: 'available' }, landmarks)) available.add(situationId);
    }
  }
  return Object.freeze([...available].sort());
}
// A ChunkPlan carries only its local anchor. Remote prerequisite outcomes are
// verified by the authoritative sparse-world index (and by a complete offline
// index); rejecting them here would make a valid cross-chunk connection
// impossible to page or transport one block at a time.
function validateSituations(rows, landmarks, path, errors, worldLandmarks = null) {
  const values = boundedArray(rows, path, CITY_FORGE_LIMITS.situations, errors) ?? [];
  uniqueIds(values, 'id', path, errors);
  const landmarkIds = new Set(landmarks.map(row => row?.id));
  const occupiedTemplates = new Set();
  for (let index = 0; index < values.length; index += 1) {
    const row = values[index], p = `${path}[${index}]`;
    if (!exactObject(row, p, ['id', 'situationId', 'landmarkId', 'state', 'stableSeed'], [], errors)) continue;
    identifier(row.id, `${p}.id`, errors);
    if (!SITUATION_SET.has(row.situationId)) fail(errors, `${p}.situationId`, 'must be an approved CitySituation template');
    identifier(row.landmarkId, `${p}.landmarkId`, errors);
    if (!landmarkIds.has(row.landmarkId)) fail(errors, `${p}.landmarkId`, 'must refer to a landmark in this chunk');
    if (!SITUATION_STATE_SET.has(row.state)) fail(errors, `${p}.state`, 'must be a valid CitySituation lifecycle state');
    if (typeof row.stableSeed !== 'string' || !/^[a-f0-9]{16,128}$/.test(row.stableSeed)) fail(errors, `${p}.stableSeed`, 'must be a stable hexadecimal seed');
    const requirement = CITY_FORGE_SITUATION_REQUIREMENTS[row.situationId];
    const anchor = landmarks.find(landmark => landmark?.id === row.landmarkId);
    if (requirement && anchor && !requirement.anchorOutcomes.includes(anchor.outcomeId)) fail(errors, `${p}.situationId`, 'must use its authored landmark outcome');
    if (worldLandmarks !== null && SITUATION_SET.has(row.situationId) && landmarkIds.has(row.landmarkId) && SITUATION_STATE_SET.has(row.state)
      && !situationRequirementsMet(row, worldLandmarks)) fail(errors, `${p}.situationId`, 'must meet its established cross-site prerequisites');
    const templateKey = `${row.landmarkId}\u0000${row.situationId}`;
    if (occupiedTemplates.has(templateKey)) fail(errors, `${p}.situationId`, 'may occur once for a landmark');
    occupiedTemplates.add(templateKey);
  }
}

export function validateWorldHead(value, { requireRevision = true } = {}) {
  const errors = [];
  const required = ['schemaVersion', 'catalogHash', 'worldSeed', 'generatorVersion', 'chunkMapRoot', 'grid'];
  if (requireRevision) required.push('worldRevision');
  if (!exactObject(value, '$', required, [], errors)) return { valid: false, errors };
  if (value.schemaVersion !== CITY_FORGE_SCHEMA_VERSION) fail(errors, '$.schemaVersion', 'is not supported');
  digest(value.catalogHash, '$.catalogHash', errors);
  if (typeof value.worldSeed !== 'string' || !/^[a-f0-9]{16,128}$/.test(value.worldSeed)) fail(errors, '$.worldSeed', 'must be a stable hexadecimal world seed');
  if (value.generatorVersion !== CITY_FORGE_GENERATOR_VERSION) fail(errors, '$.generatorVersion', 'is not supported');
  digest(value.chunkMapRoot, '$.chunkMapRoot', errors);
  if (requireRevision) safeInteger(value.worldRevision, '$.worldRevision', 1, Number.MAX_SAFE_INTEGER, errors);
  if (exactObject(value.grid, '$.grid', ['chunkCells', 'cellMeters'], [], errors)) {
    if (value.grid.chunkCells !== CITY_FORGE_CHUNK_CELLS) fail(errors, '$.grid.chunkCells', 'must be 9');
    if (value.grid.cellMeters !== CITY_FORGE_CELL_METERS) fail(errors, '$.grid.cellMeters', 'must be 8');
  }
  return { valid: errors.length === 0, errors };
}

/** Validate raw client data; it contains no transforms, scene data, URLs, media IDs, or free text. */
export function validateChunkPlan(value, { requireRevision = false, allowReservedStarter = true, worldLandmarks = null } = {}) {
  const errors = [];
  const required = ['coord', 'baseChunkHash', 'parentChunkRevision', 'roads', 'removedRoadEdges', 'parcels', 'buildings', 'services', 'screens', 'landmarks', 'situations', 'demolishedIds'];
  if (requireRevision) required.push('revision');
  if (!exactObject(value, '$', required, [], errors)) return { valid: false, errors };
  if (exactObject(value.coord, '$.coord', ['x', 'z'], [], errors)) {
    safeInteger(value.coord.x, '$.coord.x', -CITY_FORGE_LIMITS.coordinate, CITY_FORGE_LIMITS.coordinate, errors);
    safeInteger(value.coord.z, '$.coord.z', -CITY_FORGE_LIMITS.coordinate, CITY_FORGE_LIMITS.coordinate, errors);
    if (isOldCityCoordinate(value.coord.x, value.coord.z)) fail(errors, '$.coord', 'is inside the protected Old City');
    if (!allowReservedStarter && isStarterRingCoordinate(value.coord.x, value.coord.z)) fail(errors, '$.coord', 'is a reserved starter coordinate');
  }
  digest(value.baseChunkHash, '$.baseChunkHash', errors);
  safeInteger(value.parentChunkRevision, '$.parentChunkRevision', 0, Number.MAX_SAFE_INTEGER, errors);
  if (requireRevision) safeInteger(value.revision, '$.revision', 1, Number.MAX_SAFE_INTEGER, errors);
  validateRoads(value.roads, '$.roads', errors);
  validateRemovedRoadEdges(value.removedRoadEdges, value.roads, '$.removedRoadEdges', errors);
  const coreStub = Number.isSafeInteger(value?.coord?.x) && Number.isSafeInteger(value?.coord?.z)
    ? oldCityConnectionStub(value.coord.x, value.coord.z) : null;
  if (coreStub) {
    const roads = Array.isArray(value.roads) ? value.roads : [];
    const removedRoadEdges = Array.isArray(value.removedRoadEdges) ? value.removedRoadEdges : [];
    if (roads.some(road => road?.edge === coreStub.edge))
      fail(errors, '$.roads', 'cannot replace an immutable Old City connection stub');
    if (removedRoadEdges.includes(coreStub.edge))
      fail(errors, '$.removedRoadEdges', 'cannot remove an immutable Old City connection stub');
  }
  validateParcels(value.parcels, '$.parcels', errors);
  validateBuildings(value.buildings, value.parcels ?? [], value.roads ?? [], '$.buildings', errors);
  validateServices(value.services, value.parcels ?? [], value.roads ?? [], '$.services', errors);
  validateScreens(value.screens, value.buildings ?? [], value.parcels ?? [], value.roads ?? [], '$.screens', errors);
  validateLandmarks(value.landmarks, value.parcels ?? [], '$.landmarks', errors);
  validateSituations(value.situations, value.landmarks ?? [], '$.situations', errors, worldLandmarks);
  const demolished = boundedArray(value.demolishedIds, '$.demolishedIds', CITY_FORGE_LIMITS.buildings + CITY_FORGE_LIMITS.services, errors) ?? [];
  if (new Set(demolished).size !== demolished.length || !demolished.every(entry => typeof entry === 'string' && ID.test(entry))) fail(errors, '$.demolishedIds', 'must contain unique local structure identifiers');
  try { if (textBytes(canonicalJson(value)) > CITY_FORGE_LIMITS.chunkBytes) fail(errors, '$', 'exceeds the packed chunk-plan limit'); } catch { fail(errors, '$', 'must be serializable'); }
  return { valid: errors.length === 0, errors };
}

export class CityForgeValidationError extends Error {
  constructor(errors) { super('Invalid City Forge state'); this.name = 'CityForgeValidationError'; this.errors = errors; }
}
export function assertWorldHead(value, options) { const result = validateWorldHead(value, options); if (!result.valid) throw new CityForgeValidationError(result.errors); return freeze(clone(value)); }
export function assertChunkPlan(value, options) { const result = validateChunkPlan(value, options); if (!result.valid) throw new CityForgeValidationError(result.errors); return freeze(clone(value)); }

export function canTransitionCityworks(from, to) {
  return from === to || CITY_FORGE_CITYWORKS_TRANSITIONS[from]?.includes(to) === true;
}
export function canTransitionSituation(from, to) {
  return from === to || CITY_FORGE_SITUATION_TRANSITIONS[from]?.includes(to) === true;
}
/**
 * Check stateful Cityworks changes against the prior accepted plan.  This is
 * intentionally separate from shape validation: drafts may be new, but an
 * accepted revision may not jump a landmark or situation through its authored
 * lifecycle just by replacing a JSON blob.
 */
export function validatePlanTransitions(previous, next) {
  const errors = [];
  const prior = validateChunkPlan(previous), candidate = validateChunkPlan(next);
  if (!prior.valid) errors.push(...prior.errors.map(error => ({ ...error, path: `previous${error.path}` })));
  if (!candidate.valid) errors.push(...candidate.errors.map(error => ({ ...error, path: `next${error.path}` })));
  if (errors.length) return { valid: false, errors };
  const priorLandmarks = new Map(previous.landmarks.map(row => [row.id, row]));
  const priorSituations = new Map(previous.situations.map(row => [row.id, row]));
  for (const landmark of next.landmarks) {
    const earlier = priorLandmarks.get(landmark.id);
    if (earlier) {
      if (earlier.classId !== landmark.classId || earlier.outcomeId !== landmark.outcomeId || earlier.parcelId !== landmark.parcelId)
        errors.push({ path: '$.landmarks', message: 'an accepted Cityworks identity cannot be repurposed' });
      else if (!canTransitionCityworks(earlier.state, landmark.state))
        errors.push({ path: '$.landmarks', message: 'a Cityworks project must follow its authored lifecycle' });
    } else if (!CITY_FORGE_CITYWORKS_INITIAL_STATES.includes(landmark.state)) {
      errors.push({ path: '$.landmarks', message: 'a new Cityworks project must begin at an authored starting state' });
    }
  }
  for (const situation of next.situations) {
    const earlier = priorSituations.get(situation.id);
    if (earlier) {
      if (earlier.situationId !== situation.situationId || earlier.landmarkId !== situation.landmarkId || earlier.stableSeed !== situation.stableSeed)
        errors.push({ path: '$.situations', message: 'an accepted situation identity cannot be repurposed' });
      else if (!canTransitionSituation(earlier.state, situation.state))
        errors.push({ path: '$.situations', message: 'a situation must follow its authored lifecycle' });
    } else if (!CITY_FORGE_SITUATION_INITIAL_STATES.includes(situation.state)) {
      errors.push({ path: '$.situations', message: 'a new situation must begin at an authored starting state' });
    }
  }
  return { valid: errors.length === 0, errors };
}

function clone(value) { return JSON.parse(JSON.stringify(value)); }
function freeze(value) { if (value && typeof value === 'object') { for (const child of Object.values(value)) freeze(child); Object.freeze(value); } return value; }
function canonicalValue(value) {
  if (Array.isArray(value)) return value.map(canonicalValue);
  if (plainObject(value)) return Object.fromEntries(Object.keys(value).sort().map(key => [key, canonicalValue(value[key])]));
  return value;
}
export function canonicalJson(value) { return JSON.stringify(canonicalValue(value)); }
/**
 * Exact reviewed input for the catalog identity carried by World Heads and
 * runtime compatibility declarations. Keep this data-only: IDs, bounded
 * placement grammar, and authored finite-state rules—not implementation code.
 */
export function cityForgeCatalogFingerprintInput() {
  return {
    schemaVersion: CITY_FORGE_SCHEMA_VERSION,
    generatorVersion: CITY_FORGE_GENERATOR_VERSION,
    limits: CITY_FORGE_LIMITS,
    roads: CITY_FORGE_ROAD_CLASSES,
    zones: CITY_FORGE_ZONES,
    facades: CITY_FORGE_FACADES,
    roofs: CITY_FORGE_ROOFS,
    buildings: CITY_FORGE_BUILDINGS,
    buildingZones: CITY_FORGE_BUILDING_ZONES,
    services: CITY_FORGE_SERVICE_TYPES,
    interiors: CITY_FORGE_INTERIOR_ROLES,
    storefronts: CITY_FORGE_STOREFRONTS,
    screenMounts: CITY_FORGE_SCREEN_MOUNTS,
    screenDevices: CITY_FORGE_SCREEN_DEVICES,
    buildingScreenMounts: CITY_FORGE_BUILDING_SCREEN_MOUNTS,
    buildingScreenSlots: CITY_FORGE_BUILDING_SCREEN_SLOTS,
    siteScreenZones: CITY_FORGE_SITE_SCREEN_ZONES,
    siteScreenSlots: CITY_FORGE_SITE_SCREEN_SLOTS,
    roadScreenSlots: CITY_FORGE_ROAD_SCREEN_SLOTS,
    landmarks: CITY_FORGE_LANDMARKS,
    landmarkOutcomes: CITY_FORGE_LANDMARK_OUTCOMES,
    cityworksStates: CITY_FORGE_CITYWORKS_STATES,
    cityworksInitialStates: CITY_FORGE_CITYWORKS_INITIAL_STATES,
    cityworksTransitions: CITY_FORGE_CITYWORKS_TRANSITIONS,
    situations: CITY_FORGE_SITUATIONS,
    situationRequirements: CITY_FORGE_SITUATION_REQUIREMENTS,
    situationStates: CITY_FORGE_SITUATION_STATES,
    situationInitialStates: CITY_FORGE_SITUATION_INITIAL_STATES,
    situationTransitions: CITY_FORGE_SITUATION_TRANSITIONS,
  };
}
function ordered(rows, key) { return [...rows].sort((a, b) => String(key(a)).localeCompare(String(key(b)))); }
/**
 * Array order is never a player-facing City Forge choice. Sort every sparse
 * record by its stable identity before hashing so retries and independent
 * clients produce the same packed blob.
 */
export function normalizeChunkPlan(value) {
  const plan = clone(assertChunkPlan(value));
  plan.roads = ordered(plan.roads, row => `${row.edge}\u0000${row.class}`);
  plan.removedRoadEdges = [...plan.removedRoadEdges].sort();
  plan.parcels = ordered(plan.parcels, row => row.id).map(row => ({ ...row, cells: [...row.cells].sort() }));
  plan.buildings = ordered(plan.buildings, row => row.id);
  plan.services = ordered(plan.services, row => row.id);
  plan.screens = ordered(plan.screens, row => row.screenId);
  plan.landmarks = ordered(plan.landmarks, row => row.id);
  plan.situations = ordered(plan.situations, row => row.id);
  plan.demolishedIds = [...plan.demolishedIds].sort();
  return plan;
}
export function canonicalChunkPlan(value) { return canonicalJson(normalizeChunkPlan(value)); }
export async function sha256Hex(value) {
  const bytes = new TextEncoder().encode(typeof value === 'string' ? value : canonicalJson(value));
  const digestBytes = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
  return Array.from(digestBytes, byte => byte.toString(16).padStart(2, '0')).join('');
}
export async function chunkPlanHash(value) { return sha256Hex(canonicalChunkPlan(value)); }
export async function chunkMapRoot(entries) {
  if (!Array.isArray(entries) || entries.length > 1_000_000) throw new TypeError('Invalid sparse chunk map.');
  const canonical = entries.map(entry => {
    if (!plainObject(entry) || !plainObject(entry.coord) || !Number.isSafeInteger(entry.coord.x) || !Number.isSafeInteger(entry.coord.z)
      || typeof entry.revisionId !== 'string' || !ID.test(entry.revisionId) || typeof entry.hash !== 'string' || !SHA.test(entry.hash)) throw new TypeError('Invalid sparse chunk map entry.');
    return { coord: { x: entry.coord.x, z: entry.coord.z }, revisionId: entry.revisionId, hash: entry.hash };
  }).sort((a, b) => a.coord.x - b.coord.x || a.coord.z - b.coord.z || a.revisionId.localeCompare(b.revisionId));
  return sha256Hex(canonical);
}

// A compact 32-bit deterministic source for catalog selection. It deliberately
// avoids BigInt so the exact offline planner also works in older WebGL browsers.
// Persisted base/chunk hashes still use SHA-256 above.
function seed32(value) {
  let hash = 0x811c9dc5;
  for (const byte of new TextEncoder().encode(value)) { hash ^= byte; hash = Math.imul(hash, 0x01000193); }
  return (hash >>> 0) || 1;
}
function next32(state) { let value = state >>> 0; value ^= value << 13; value ^= value >>> 17; value ^= value << 5; return value >>> 0; }
function sample(state, values) { const next = next32(state); return { state: next, value: values[next % values.length] }; }
function boundaryStub(seed, generatorVersion, catalogHash, x, z, edge) {
  const key = canonicalBoundaryEdgeKey(x, z, edge);
  const value = seed32(`${seed}|${generatorVersion}|${catalogHash}|${key}`);
  // A stable sparse base graph leaves room for authored player streets while
  // ensuring frontier neighbours derive exactly the same border stubs.
  return value % 7 === 0 ? CITY_FORGE_ROAD_CLASSES[(value >>> 8) % CITY_FORGE_ROAD_CLASSES.length] : null;
}

/** Deterministic, catalog-only base chunk; no player choice is stored here. */
export async function generateBaseChunk({ worldSeed, generatorVersion, catalogHash, x, z }) {
  const provisional = { schemaVersion: CITY_FORGE_SCHEMA_VERSION, worldSeed, generatorVersion, catalogHash, x, z };
  const headValidation = validateWorldHead({ schemaVersion: CITY_FORGE_SCHEMA_VERSION, catalogHash, worldSeed, generatorVersion, worldRevision: 1, chunkMapRoot: '0'.repeat(64), grid: { chunkCells: 9, cellMeters: 8 } });
  if (!headValidation.valid || !Number.isSafeInteger(x) || !Number.isSafeInteger(z) || Math.abs(x) > CITY_FORGE_LIMITS.coordinate || Math.abs(z) > CITY_FORGE_LIMITS.coordinate) throw new CityForgeValidationError(headValidation.errors.length ? headValidation.errors : [{ path: '$.coord', message: 'is invalid' }]);
  let state = seed32(`${worldSeed}|${generatorVersion}|${catalogHash}|${x},${z}`);
  const terrain = sample(state, ['wet-paving', 'service-concrete', 'garden-grade', 'old-masonry']); state = terrain.state;
  const parcelGrammar = sample(state, ['perimeter', 'courtyard', 'spine', 'laneway']); state = parcelGrammar.state;
  const skyline = sample(state, ['low-rise', 'mixed-rise', 'tower-punctuated', 'civic-open']); state = skyline.state;
  const life = sample(state, ['repair', 'care', 'culture', 'exchange']);
  const boundary = {};
  const coreStub = oldCityConnectionStub(x, z);
  for (const edge of ['0,8:north', '8,0:east', '0,0:south', '0,0:west']) {
    // Supply a canonical representative for each side. Detailed edge records
    // are made by player deltas, while this is enough for strategic planning.
    const road = coreStub?.edge === edge ? coreStub.class : boundaryStub(worldSeed, generatorVersion, catalogHash, x, z, edge);
    if (road) boundary[edge.split(':')[1]] = road;
  }
  const result = {
    ...provisional,
    protected: isOldCityCoordinate(x, z),
    starterRing: isStarterRingCoordinate(x, z),
    terrainId: terrain.value,
    parcelGrammarId: parcelGrammar.value,
    skylineId: skyline.value,
    lifeThemeId: life.value,
    boundary,
    availableScreenMounts: CITY_FORGE_SCREEN_MOUNTS,
    landmarkClasses: CITY_FORGE_LANDMARKS,
  };
  return Object.freeze({ ...result, baseChunkHash: await sha256Hex(result) });
}

/** Verify every explicit cross-chunk delta has an equal and opposite peer. */
export function validateBoundarySymmetry(plans) {
  const errors = [];
  if (!Array.isArray(plans)) return { valid: false, errors: [{ path: '$', message: 'must be a list of plans' }] };
  const byCoord = new Map();
  for (let index = 0; index < plans.length; index += 1) {
    const validation = validateChunkPlan(plans[index]);
    if (!validation.valid) errors.push(...validation.errors.map(error => ({ ...error, path: `[${index}]${error.path}` })));
    const coord = plans[index]?.coord;
    if (coord && Number.isSafeInteger(coord.x) && Number.isSafeInteger(coord.z)) byCoord.set(coordinateKey(coord.x, coord.z), plans[index]);
  }
  const borders = new Map();
  for (const plan of plans) {
    if (!plan?.coord || !Array.isArray(plan.roads)) continue;
    for (const road of plan.roads) {
      if (!isBoundaryEdge(road?.edge)) continue;
      const key = canonicalBoundaryEdgeKey(plan.coord.x, plan.coord.z, road.edge);
      const found = borders.get(key) ?? [];
      found.push({ coordinate: coordinateKey(plan.coord.x, plan.coord.z), kind: 'road', road });
      borders.set(key, found);
    }
    for (const edge of plan.removedRoadEdges ?? []) {
      if (!isBoundaryEdge(edge)) continue;
      const key = canonicalBoundaryEdgeKey(plan.coord.x, plan.coord.z, edge);
      const found = borders.get(key) ?? [];
      found.push({ coordinate: coordinateKey(plan.coord.x, plan.coord.z), kind: 'removed', edge });
      borders.set(key, found);
    }
  }
  for (const [key, rows] of borders) {
    if (rows.length !== 2 || rows[0].coordinate === rows[1].coordinate || rows[0].kind !== rows[1].kind
      || rows[0].kind === 'road' && rows[0].road.class !== rows[1].road.class) {
      errors.push({ path: '$.roads', message: `boundary ${key} must be committed symmetrically` });
    }
  }
  return { valid: errors.length === 0, errors };
}

/** Stable life hooks, never a currency economy or player-visible optimization bar. */
export function resolveCityPulse(plans, seed = '0') {
  const tags = { access: 0, care: 0, culture: 0, pressure: 0 };
  for (const plan of plans ?? []) {
    for (const road of plan?.roads ?? []) {
      if (road.class === 'boulevard' || road.class === 'transit-service-corridor') tags.access += 2;
      if (road.class === 'pedestrian-route') tags.culture += 1;
      if (road.class === 'service-alley') tags.pressure += 1;
    }
    for (const service of plan?.services ?? []) {
      if (/care|clinic|water/.test(service.type)) tags.care += 2;
      if (/culture|screen|library/.test(service.type)) tags.culture += 2;
      if (/utility|relay|power/.test(service.type)) tags.access += 1;
    }
    for (const landmark of plan?.landmarks ?? []) {
      const outcome = landmark.outcomeId ?? '';
      if (/care|garden|washhouse|shelter/.test(outcome)) tags.care += 3;
      if (/screen|studio|archive|listening|exhibition|mural/.test(outcome)) tags.culture += 3;
      if (/repair|relay|supply|support|workshop/.test(outcome)) tags.access += 3;
      if (/night|after-hours|weather/.test(outcome)) tags.pressure += 2;
    }
  }
  const state = seed32(`${seed}|${canonicalJson(tags)}`);
  const theme = ['repair-regulars', 'care-network', 'culture-evening', 'working-tension'][state % 4];
  return Object.freeze({ tags: Object.freeze(tags), theme, variation: (state >>> 8) % 1024, situations: availableSituationTemplates(plans) });
}
