# Population registry — source prepared for v1.0.1

Implemented source, **October 16, 2026**. The current population is **2,026 logical residents: 1,984 Agent Smiths and 42 women in red**. This is the source prepared for v1.0.1, not a claim that this release is published or has passed physical-device acceptance. The October 14 proposal below is retained as a historical plan; its descriptions of the former runtime are not current behavior.

## Implemented population and accessibility

The authored 432 identities, including the original 11 red contacts, remain in place. The 1,263 previously generated Smith identities also keep their order and IDs. The supplement adds 300 Smiths (`200001`–`200300`) and 31 women (`300001`–`300031`); it does not convert an existing Smith into a woman.

- **33 women use the existing aboveground street routes**, balanced at three or four per district across all nine street districts. No red contact is assigned to a subway-platform route. The original platform women retain their identities and relocate through logical metadata after their former body slots are retired.
- **Nine women occupy nine distinct enterable buildings**, selected across the map by farthest-first ground-floor locations. They use connected, supported circulation routes when their floor is admitted, rather than appearing in lift shafts or unloaded rooms.
- **1,151 residents have interior homes: 1,142 Smiths and nine women.** A logical home does not keep its furnished floor resident. Admission validates actual floor support, headroom, furniture clearance and route connectivity. Floor retirement immediately releases its bodies and interaction targets while retaining resident identity.

`SlopCityCrowdPopulation` owns the logical registry and reusable body slots. `SlopCityInteriorPopulation` connects actual building admission/retirement to those routes. `FirstPerson.Start` prepares the complete population after street revalidation and before Matrix search loads progress. Search considers all 42 red identities and starts another circuit only after all distinct contacts have been found.

## Fixed workload and resource limits

| Work | Current limit |
| --- | --- |
| Detailed simulation/body slots | 432 maximum, independently of the 2,026-record registry |
| Complete bodies presented to the approved gameplay camera | 192 on Web; 256 native |
| Coarse record updates / admission candidates | 32 / 32 per simulation step |
| Admissions / normal distance retirements | 4 / 8 per simulation step; floor retirement releases its affected bodies immediately |
| Neighbor visits / eligible neighbor candidates | 128 / 32 per detailed agent |
| Interior route validation | 96 candidate-node probes and 256 edge checks per admission |
| Nearby admission / distance retirement | 72 m / 90 m |
| Simulation timing | Web uses 0.05 s steps; an individual supplied step is capped at 0.1 s; pause prevents advancement |

Simulation, rendering and aimed-body queries use the bounded detailed set. Far logical progression is staggered and caps elapsed progression/hops rather than replaying every missed tick after backgrounding. Admission requires free capacity and supported, unoccupied space; logical residents wait when capacity or geometry is unavailable. Targeting requires a currently presented body, so a released slot cannot silently redirect an old resident target to its replacement.

Added residents reuse shared anatomical meshes, one owned shared wardrobe, bounded instanced matrix batches and fixed query buffers. They add logical metadata, not individual GameObjects, colliders, animators, per-person mesh/material copies or media decoders. Whole-person rendering reserves every required mesh/material key before committing matrices, including repeated parts and aliases; a saturated batch defers the whole person. The existing media budgets are independent and unchanged.

## Verification and limits

The isolated saved-scene population fixture passes **6,253 checks**, with **zero runtime errors and all four save/recovery keys restored**. It verifies exact counts and stable IDs, **33 actual aboveground street women across nine districts, nine reachable indoor women and zero transit women**, balanced street distribution, and retirement of the original two admitted platform bodies before relocation. It also validates supported connected interior nodes, normal bounded body admission/presentation and unobstructed reachable targeting/conversation for all nine indoor women, floor retirement/readmission, stale target refusal, pause/hitch limits, whole-person overflow and warm mesh/material ownership. Actual player startup retains all 11 old discoveries and the full search circuit advances only after 42 distinct contacts.

The final run records 1,077 scheduler steps, 70 validated interior-node samples and a maximum of 256 native presented bodies. The Web cap is source-reviewed, and both runtime platform branches and the separate Editor assembly compile. This is not a new phone/browser rendering, frame-time, sustained process-memory or complete street-to-every-room traversal result. The observer uses bounded fixture positioning at supported clear standing locations; the fixture does not walk every complete approach from the street.

Durable evidence: `Builds/Reports/world-work-20261016/population-gameplay-check.json`, `population-provenance.json`, `population-independent-review.json`, `SlopCityPopulationSmokeTest.log` and `population-tested-source/`. Superseded evidence remains in `population-superseded-1695-2462/` and `population-superseded-public-route-5858/`.

## Historical plan — October 14, 2026: 432 residents to 4,320

The following is the original dated proposal, preserved for context. Its “current” statements describe October 14, before the registry/slot implementation above; its 4,320 target and acceptance gates are not an implemented or verified population claim.

Status: architecture and acceptance plan, October 14, 2026. No population increase or runtime implementation accompanies this document. The mobile process-memory failure remains the immediate release constraint. Registering ten times as many residents must not create ten times as many simulated or rendered bodies.

### Current implementation and limits

The saved `SlopCity_CouchSpawn` scene contains 432 residents on eleven routes: 44 in each of nine districts and 18 on each of two subway platforms. They use shared articulated meshes and instanced materials, rather than one GameObject hierarchy per resident. The three local Smith inspectors reuse existing residents; they add no people, meshes, materials or decoders.

| Area | Current source behavior | Scaling implication |
| --- | --- | --- |
| Construction | `SlopCityCrowdBuilder.Build` defaults to 432 and clamps requests to 1–500; initial seeds require 0.82 m separation. | Raising the clamp is not a scaling implementation. Existing pavement need not fit 4,320 simultaneous bodies. |
| Layout validation | `RemapLayout` rejects more than 5,000 people, 64 routes or 65,536 source nodes; it preserves at least 0.75 m seed spacing. It searches occupied seeds and candidates and calculates pairwise minimum spacing. | The 5,000 guard is an input ceiling, not demonstrated capacity. Pairwise distances alone rise from 93,096 at 432 to 9,329,040 at 4,320. These are calculated counts, not timings. |
| Identity | A seed has route/node, appearance, role and display name. Runtime array index identifies the resident; names and appearances can repeat. | Array order and current route are unsuitable durable identities. |
| Simulation | Every resident is inserted into a 70×70 linked-cell grid and advanced; Web uses a 0.05 s step, native advances each frame. Neighbor speed queries examine adjacent cells. Nearby obstacle checks use a reusable 12-hit array. | A spatial index exists, but all people still update. Packing ten times as many residents into the same cells can increase neighbor work faster than population. |
| Rendering | Each non-preview camera scans every resident. Web range/detail thresholds are 60/12 m; native thresholds are 135/32 m. Frustum tests reduce submitted bodies. | Distance is not a hard body, draw, matrix or CPU budget. Additional cameras repeat the scan. |
| Batch storage | Each mesh/material batch starts with 16 matrices and grows to 1,023. Appends beyond capacity are omitted. | At higher density this can omit individual body parts. A future cap must admit or omit complete people before emitting parts. |
| Interaction | `TryFindTarget` scans all residents; existing player targeting enforces bounded reach and occlusion. | Future queries should use nearby admitted bodies, preserving the same visibility rules. Logical residents without a body must not be invisible interaction targets. |

`Matrix4x4` is 64 bytes; 1,023 matrices consume 65,472 bytes per batch before array/object overhead. This describes storage, not total native graphics cost. The grid heads use 19,600 bytes. Current lazy matrix growth allocates when a new visible maximum is reached; a future steady-state zero-allocation claim requires warm-up measurements. No current figure establishes 4,320-resident performance.

### One authoritative resident registry, bounded presentation

Introduce this separation before increasing population:

1. **Resident registry:** 4,320 lightweight records with immutable resident ID, authored identity/appearance, home/work locations, schedule, current logical district/route, deterministic random state and last simulation time. Avoid strings or managed objects duplicated per tick. A resident exists in the registry even when no body is admitted.
2. **Simulation slots:** a bounded set of detailed agents borrowing registry IDs. Slots contain current route edge, movement/interpolation and near-neighbor state. Releasing a slot commits logical state to its resident record; it does not delete that resident or reset its identity.
3. **Presentation slots:** a separately bounded selection of complete bodies for an approved camera. Shared geometry/material ownership stays unchanged. Do not add resident cameras, decoders, textures or individual mesh copies.
4. **Interaction selection:** queries only the nearby presented body set, then retain existing line-of-sight, reach and cooldown checks. Pin the person currently being addressed until the interaction finishes or normal range/occlusion rules end it.

Expose registered, detailed-simulated, coarse-updated, presented and interactive counts separately. “Population: 4,320” must not imply that all 4,320 bodies are simultaneously visible or individually stepping physics.

### Stable identity and save compatibility

Assign existing 432 residents explicit immutable IDs through a versioned authored migration table. Validate uniqueness and preserve the mapping when arrays are reordered. Do not derive IDs from display name, appearance, current position, district, runtime instance ID or mutable array index. New records receive new IDs; they never reuse a deleted ID during migration.

Keep old banked credits, XP, completed jobs and device state untouched. Do not introduce per-resident persistence merely to implement crowd presentation. If resident state is later saved, use a versioned schema and preserve unknown/newer records on failed migration. Bind Smith inspector roles to resident IDs once IDs exist; their current indices 176, 220 and 308 describe this scene revision only. Municipal evidence should distinguish resident ID, device ID and presentation slot; human-readable names remain labels.

### Ownership seams required before pooling

These are implementation requirements, not interfaces already present in the game:

- **Identity versus body handle:** a registry lookup uses resident ID. An aimed body handle also carries slot generation, validated again at use time with current reach and occlusion. Releasing/reassigning a slot invalidates its old handles even if the array index still exists. A stale input must fail or reacquire through the ordinary targeting path, never silently operate on a replacement resident.
- **Inspector and conversation lifetime:** current `EnsureAgents` recreates all `Agent` objects and calls `SlopCityLocalAgent.Select`; `Select` clears pending inspections. That is initialization behavior, not a suitable promotion/demotion API. Future inspector tasks, evidence sequence/time boundaries and conversation cooldowns belong to stable resident/inspector state outside reusable body slots. Returning a Smith to view must not restart, transfer or complete its inspection. A slot release commits movement state without calling whole-crowd initialization.
- **Municipal evidence ownership:** current log entries copy bounded labels, coordinates and before/after values into a 32-entry session ring; they do not hold scene object references. Future actor/device IDs must accompany these snapshots. Never resolve an old entry's actor or device through a current slot, rewrite its historical labels when a slot is reused, or treat the journal as authoritative device state. Explicit operations still read/change the actual device/resource owner and record the observed result. Failed/stale admission does not constitute a completed municipal operation.
- **Layout versus population membership:** `RemapLayout` currently assigns a distinct, physically spaced node to every seed. Do not send all 4,320 registry records through this simultaneous-body assignment. Validate the route graph separately; assign occupied positions only to admitted detailed agents. A logical resident waiting for capacity remains a valid registry member, not a missing seed or an overlapping body.

Before connecting these seams to gameplay, test slot A release/reuse by resident B between prompt and USE; Smith demotion/re-entry during an inspection; old journal reading after both resident and device view recycling; and registry reorder while a conversation is pinned. Identity, task deadline and copied evidence must remain unchanged, and stale actions must not affect B.

### Spatial cells and bounded update tiers

Index logical locations separately from detailed local collision cells. Query cells around the player and eligible gameplay camera rather than scanning all registered residents for every camera. Reuse cell arrays, queues and visible-ID buffers. Explicitly handle residents outside the current fixed-grid bounds instead of silently treating a boundary cell as unlimited capacity.

Use three tiers with hysteresis and fixed work quotas:

| Tier | Required behavior | Budget rule |
| --- | --- | --- |
| Near | Existing full-size route movement, neighbor avoidance, obstacle checks and Web interpolation. | Retain the current 20 Hz Web behavior for admitted residents; cap detailed residents and neighbor candidates independently. |
| Middle | Coarse progression on validated route edges; no detailed limb matrices or per-resident capsule casts until admitted. | Stagger records across scheduler buckets. Set frequency from measured transition/error limits, not an assumed universal number. |
| Far | Schedule/route progress represented by timestamps and deterministic state. No body, physics query or renderer registration. | Advance a bounded number of records per frame or evaluate elapsed time on demand; never catch up every missed tick after backgrounding. |

Promotion requires a free slot and a validated unoccupied route position. If the area is full, retain the resident logically and delay admission; never overlap bodies, spawn inside geometry or teleport an existing visible resident to make room. Demotion is hysteretic and commits state once. Pause, report reading and focus loss retain existing game-time semantics. Train/elevator travel changes logical location through actual completed travel state, not a fabricated instantaneous physical body.

Keep first implementation quotas at or below the measured current population workload. Choose actual near/middle ranges, slot counts and per-frame quotas only after baseline profiling on the affected phone and desktop. Increasing registry capacity is independent of increasing any active budget.

Apply quotas to discovery, admission attempts, route/occupancy validation, neighbor candidates and demotion commits as well as movement ticks. A fixed active count alone does not bound a frame that scans/sorts 4,320 records or retries every blocked promotion. Keep reusable queues with explicit overflow/backpressure and fair deferred progress. A dense-cell or background-resume fixture must record these separate work counters and show each stays within its configured maximum; legitimate waiting residents must eventually be serviced when capacity becomes available.

### Whole-person rendering and interaction budgets

Select complete residents before calling `RenderPerson`. Use stable, deterministic priority: active conversation/interaction, nearby visible residents, then distance and resident ID; apply hysteresis so small camera movements do not flicker bodies. A pinned person still obeys ordinary occlusion and reach.

Reserve capacity for every part of an admitted person. If a draw reaches the API's 1,023-instance limit, use bounded spill batches within an explicit draw/matrix budget or stop admitting whole people. Never let `Add` overflow silently erase arms, heads or clothing. Keep full-size bodies and the current articulated near geometry. Any future lower-detail full-body representation requires explicit art validation; scaling is not permission to shrink residents or show disembodied parts.

The reservation must count the actual `(mesh, material)` keys used by that person's current appearance, role and detail tier, including repeated left/right parts and any shared mesh aliases. Make admission atomic across every required batch before emitting matrices. Test full and reduced-detail branches for all authored roles, with a batch one matrix short: either every requested part is emitted or that whole person is deferred. Spill buffers must also be preallocated/accounted, rather than growing without a draw or memory limit.

The present shoe batch is a concrete limiting case: each person emits two instances. 432 fully visible people require 864 shoe matrices, while 512 require 1,024 and exceed the existing 1,023-instance capacity. Whole-person admission must precede raising active counts. Do not interpret 511 as an approved performance budget; keep the current workload until measurement supports a change.

`Sandbox.Target` currently caches `npcIndex`. Once slots can be recycled, cached prompts and use actions must validate immutable resident ID plus slot generation; a reused index must never redirect an interaction to a different person.

Define which cameras may render crowd. A report snapshot must not create another camera or render pass. Secondary camera support needs a bounded camera-specific visible set and accounted matrices; preview/reflection cameras must not multiply unbudgeted crowd work.

### Implementation order and release gates

1. **Establish a baseline:** resolve the current crash and record actual route, phone model/OS, HTTPS browser versus installed PWA, frame time, total process footprint where available, Wasm size, requested GL buffers/textures, draws and crowd counts. Unity tracked allocation is not total process memory.
2. **Add identity/registry contract:** introduce ID/schema and deterministic migration fixtures with 432 records; change neither visible count nor movement. Check old saves and Smith/municipal identity after reorder.
3. **Separate slots and admission at 432:** preserve current appearance and patrol behavior with explicit caps. Add whole-person overflow handling, spatial interaction queries and reusable buffers. Compare actual walking/occlusion with the baseline.
4. **Add update tiers at 432:** fault-test slot exhaustion, occupied promotion points, time jumps, pause/resume, travel, route retirement and interrupted handoff. No resident may duplicate, disappear from the registry or acquire another resident's dialogue/task state.
5. **Increase registered records to 4,320:** keep simulation/render/interaction limits unchanged. Test deterministic distributed schedules and full cells. Expand active quotas only in separate measured changes.

Acceptance must include:

- 4,320 unique stable IDs; registry reorder and migration preserve all original identities and save balances. No arbitrary cap increase is treated as a pass.
- Explicit per-frame limits hold under a dense street, rapid camera turns, district crossings, subway travel and building/elevator return. Whole bodies remain intact when budgets saturate.
- Detailed agents never overlap validated admission points; delayed logical residents remain counted and eventually become eligible without starvation.
- Smith optional tasks and municipal operation evidence remain attached to the correct actor/device, with no automatic rewards or false completed repairs.
- After warm-up, no crowd-managed allocations in ordinary frames; report generation may allocate only on explicit interaction. Record scheduler work, query candidates, spill draws and high-water memory.
- Physical HTTPS and installed-PWA walks cover home exit/featured video, Market Quarter, Publishers, subway and return; camera turns, report open/close, pause/focus and orientation changes remain usable. Media retains its existing independent decoder budget.
- Compare sustained frame time and actual process footprint with the same 432-resident route. Reject a change that erodes the phone's demonstrated memory headroom, even if logical population reaches 4,320.

This plan prepares a measurable path to ten times the registered population. It makes no claim that ten times the simultaneously active or visible population is safe on current hardware.
