using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Runtime owner for the offline City Forge world.  It holds only accepted
    /// WorldHead/ChunkPlan data and delegates all presentation to a compiler; it
    /// never mutates the authored scene or hot-swaps topology around a player.
    /// Personal-server clients feed the same methods only after authoritative
    /// revision responses have been verified by their private integration.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityForgeController : MonoBehaviour
    {
        [SerializeField, Range(1, 2)] private int activeChunkRadius = 1;
        [SerializeField] private bool constrainedWebGl;
        [SerializeField] private bool automaticallyPersistOffline = true;
        private readonly Dictionary<CityForgeCoord, CityForgeChunkPlan> accepted = new Dictionary<CityForgeCoord, CityForgeChunkPlan>();
        private readonly Dictionary<CityForgeCoord, CityForgeChunkPlan> drafts = new Dictionary<CityForgeCoord, CityForgeChunkPlan>();
        // A bounded, topology-free map window supplied independently from the
        // nearby physical page cache. It is never used by Resolve/StreamAt.
        private readonly Dictionary<CityForgeCoord, CityForgeStrategicChunkSummary> strategicChunks = new Dictionary<CityForgeCoord, CityForgeStrategicChunkSummary>();
        private CityForgeOutcomeIndex outcomeIndex;
        private SlopCityCityForgeCatalog catalog;
        private SlopCityCityForgeResolver resolver;
        private SlopCityCityForgeValidator validator;
        private SlopCityCityForgeChunkCompiler compiler;
        private CityForgeWorldHead head;
        private bool initialized;
        private bool buildMode;
        private readonly SlopCityCityForgeLocalStore localStore = new SlopCityCityForgeLocalStore();

        public CityForgeWorldHead WorldHead { get { return head == null ? null : head.Clone(); } }
        public bool IsBuildMode { get { return buildMode; } }
        public bool IsInitialized { get { return initialized; } }
        public bool HasOfflineWorld { get { return localStore.HasWorld; } }
        public IReadOnlyDictionary<CityForgeCoord, CityForgeChunkPlan> AcceptedChunks { get { return accepted; } }
        public IReadOnlyDictionary<CityForgeCoord, CityForgeStrategicChunkSummary> StrategicChunks { get { return strategicChunks; } }
        /// <summary>Verified compact world-wide outcome presence for cross-site CitySituation prerequisites.</summary>
        public CityForgeOutcomeIndex WorldOutcomeIndex { get { return outcomeIndex == null ? null : outcomeIndex.Clone(); } }
        public event Action<bool> BuildModeChanged;
        public event Action<CityForgeResolvedChunk> ChunkCompiled;
        public event Action<IReadOnlyList<CityForgeCoord>> FreshContextReloadRequested;
        /// <summary>Accepted non-topology updates which only need a bounded presentation refresh.</summary>
        public event Action<IReadOnlyList<CityForgeCoord>> AcceptedPresentationChanged;
        /// <summary>A bounded strategic-summary window changed without any physical stream mutation.</summary>
        public event Action StrategicMapChanged;
        /// <summary>Compact cross-site prerequisite facts changed; callers may safely refresh life presentation at their next stream boundary.</summary>
        public event Action OutcomeIndexChanged;
        public event Action<string> ValidationRefused;
        /// <summary>Offline root exists but could not be verified; callers must offer recovery, never overwrite it.</summary>
        public event Action<string> OfflineRecoveryRequired;

        public void Configure(SlopCityCityForgeChunkCompiler cityCompiler, SlopCityCityForgeCatalog cityCatalog = null)
        {
            catalog = cityCatalog ?? SlopCityCityForgeCatalog.Default;
            resolver = new SlopCityCityForgeResolver(catalog);
            validator = new SlopCityCityForgeValidator(catalog);
            compiler = cityCompiler;
            if (compiler) compiler.ChunkCompiled += HandleCompiled;
        }

        private void Awake()
        {
            if (catalog == null) Configure(GetComponent<SlopCityCityForgeChunkCompiler>());
        }

        private void OnDestroy()
        {
            if (compiler) compiler.ChunkCompiled -= HandleCompiled;
        }

        public void InitializeOffline(string worldSeed = null, CityForgeWorldHead savedHead = null, IEnumerable<CityForgeChunkPlan> savedChunks = null)
        {
            if (catalog == null) Configure(GetComponent<SlopCityCityForgeChunkCompiler>());
            if (savedHead != null)
            {
                CityForgeValidation validation = validator.ValidateWorldHead(savedHead);
                if (!validation.IsValid) throw new InvalidOperationException(validation.Messages.FirstOrDefault() ?? "The saved City Forge world is invalid.");
                head = savedHead.Clone();
            }
            else
            {
                head = new CityForgeWorldHead
                {
                    schemaVersion = SlopCityCityForge.WorldSchemaVersion,
                    catalogHash = catalog.Hash,
                    worldSeed = string.IsNullOrEmpty(worldSeed) ? SlopCityCityForge.Sha256(Guid.NewGuid().ToString("N")) : worldSeed,
                    generatorVersion = catalog.GeneratorVersion,
                    activeWorldRevision = 1,
                    blockSizeCells = SlopCityCityForge.GridSize,
                    cellSizeMeters = SlopCityCityForge.CellSizeMeters,
                    changedChunks = new List<CityForgeChunkHead>()
                };
                head.chunkMapRoot = SlopCityCityForgeCanonical.ChunkMapRoot(head.changedChunks);
            }
            List<CityForgeChunkPlan> loadedPlans = (savedChunks ?? Enumerable.Empty<CityForgeChunkPlan>()).Where(plan => plan != null).Select(plan => plan.Clone()).ToList();
            Dictionary<CityForgeCoord, CityForgeChunkPlan> loadedByCoord = loadedPlans.ToDictionary(plan => plan.coord, plan => plan);
            accepted.Clear();
            strategicChunks.Clear();
            outcomeIndex = null;
            foreach (CityForgeChunkPlan plan in loadedPlans)
            {
                CityForgeValidation validation = validator.ValidateChunkPlan(head, plan, loadedByCoord, true, loadedPlans.SelectMany(item => item.landmarks ?? new List<CityForgeLandmark>()), true, outcomeIndex);
                if (!validation.IsValid) throw new InvalidOperationException(validation.Messages.FirstOrDefault() ?? "A saved City Forge chunk is invalid.");
                accepted[plan.coord] = plan.Clone();
            }
            RebuildHeadIndex();
            initialized = true;
        }

        /// <summary>
        /// Loads a verified accepted world without writing PlayerPrefs. Private
        /// service bridges use this after a World Head/page response; offline
        /// callers use InitializeOffline so their complete sparse index remains
        /// independently sharded on disk.
        /// </summary>
        public bool LoadAcceptedWorld(CityForgeWorldHead authoritativeHead, IEnumerable<CityForgeChunkPlan> authoritativeChunks, bool completeSparseIndex, out CityForgeValidation validation)
        {
            return LoadAcceptedWorld(authoritativeHead, authoritativeChunks, completeSparseIndex, false, out validation);
        }

        /// <summary>
        /// Loads verified hosted pages without persistence. Ordinary neighborhood
        /// streaming passes <c>false</c>; only a known accepted topology
        /// replacement requests the safe fresh-context notification.
        /// </summary>
        public bool LoadAcceptedWorld(CityForgeWorldHead authoritativeHead, IEnumerable<CityForgeChunkPlan> authoritativeChunks, bool completeSparseIndex, bool requestFreshContextReload, out CityForgeValidation validation)
        {
            validation = new CityForgeValidation();
            if (authoritativeHead == null) { validation.Refuse("City Forge needs an accepted World Head."); return false; }
            CityForgeWorldHead incomingHead = authoritativeHead.Clone();
            incomingHead.indexIsComplete = completeSparseIndex;
            CityForgeValidation headValidation = validator == null ? new CityForgeValidation() : validator.ValidateWorldHead(incomingHead);
            foreach (string message in headValidation.Messages) validation.Refuse(message);
            List<CityForgeChunkPlan> plans = (authoritativeChunks ?? Enumerable.Empty<CityForgeChunkPlan>()).Where(item => item != null).Select(item => item.Clone()).ToList();
            Dictionary<CityForgeCoord, CityForgeChunkPlan> byCoord = new Dictionary<CityForgeCoord, CityForgeChunkPlan>();
            foreach (CityForgeChunkPlan plan in plans)
            {
                if (byCoord.ContainsKey(plan.coord)) { validation.Refuse("The accepted City Forge page has duplicate chunks."); continue; }
                byCoord.Add(plan.coord, plan);
            }
            // Fill the complete page/index before validating boundary peers.
            // Validating while this map is still being built would incorrectly
            // reject the first side of an otherwise complete reciprocal pair.
            foreach (CityForgeChunkPlan plan in plans)
            {
                if (!byCoord.ContainsKey(plan.coord)) continue;
                if (string.IsNullOrEmpty(plan.packedBlobHash)) plan.packedBlobHash = SlopCityCityForgeCanonical.Hash(plan);
                CityForgeValidation planValidation = validator.ValidateChunkPlan(incomingHead, plan, byCoord, completeSparseIndex, plans.SelectMany(item => item.landmarks ?? new List<CityForgeLandmark>()), completeSparseIndex, null);
                foreach (string message in planValidation.Messages) validation.Refuse(message);
            }
            if (!validation.IsValid) return false;
            InvalidateStrategicMapIfHeadChanged(incomingHead);
            InvalidateOutcomeIndexIfHeadChanged(incomingHead);
            accepted.Clear();
            foreach (CityForgeChunkPlan plan in plans)
            {
                accepted[plan.coord] = plan.Clone();
            }
            // Nearby page handoffs are not a strategic-map feed. Keeping their
            // summaries here would grow an unbounded cache as a walker crosses
            // the frontier. ApplyStrategicMap owns the separate <=169 window.
            drafts.Clear();
            head = incomingHead;
            if (completeSparseIndex) RebuildHeadIndex();
            initialized = true;
            if (requestFreshContextReload) FreshContextReloadRequested?.Invoke(plans.Select(item => item.coord).OrderBy(item => item).ToList());
            return true;
        }

        /// <summary>Validates a draft set for preview/remote submission only; it never persists or mutates accepted state.</summary>
        public bool ValidateDraftOnly(IEnumerable<CityForgeChunkPlan> proposed, out CityForgeValidation validation)
        {
            validation = new CityForgeValidation();
            if (!initialized) { validation.Refuse("City Forge is still starting."); return false; }
            List<CityForgeChunkPlan> plans = (proposed ?? Enumerable.Empty<CityForgeChunkPlan>()).Where(item => item != null).Select(item => item.Clone()).ToList();
            if (plans.Count == 0 || plans.Count > 4) { validation.Refuse("Choose one nearby block, or one connected street crossing."); return false; }
            Dictionary<CityForgeCoord, CityForgeChunkPlan> atomic = new Dictionary<CityForgeCoord, CityForgeChunkPlan>();
            foreach (CityForgeChunkPlan plan in plans)
            {
                if (!CanDraft(plan.coord) || atomic.ContainsKey(plan.coord)) { validation.Refuse("Each draft must be a unique adjoining block."); continue; }
                atomic.Add(plan.coord, plan);
                long currentRevision = accepted.TryGetValue(plan.coord, out CityForgeChunkPlan prior) ? prior.revision : 0;
                if (plan.parentChunkRevision != currentRevision) validation.Refuse("This block changed while you were planning. Reload it before building.");
            }
            foreach (CityForgeChunkPlan plan in plans)
            {
                CityForgeValidation checkedPlan = validator.ValidateChunkPlan(head, plan, atomic, true, KnownLandmarksFor(atomic), true, outcomeIndex);
                foreach (string message in checkedPlan.Messages) validation.Refuse(message);
                if (accepted.TryGetValue(plan.coord, out CityForgeChunkPlan prior))
                {
                    CityForgeValidation transition = validator.ValidatePlanTransition(prior, plan);
                    foreach (string message in transition.Messages) validation.Refuse(message);
                }
            }
            return validation.IsValid;
        }

        /// <summary>Compiles an unaccepted preview without mutating accepted state, persistence, or streamed ownership.</summary>
        public bool TryResolveDraftPreview(CityForgeChunkPlan proposed, out CityForgeResolvedChunk preview, out CityForgeValidation validation)
        {
            return TryResolveDraftPreview(new[] { proposed }, proposed == null ? default(CityForgeCoord) : proposed.coord, out preview, out validation);
        }

        /// <summary>
        /// Compiles one selected member of an atomic draft set. Boundary roads
        /// therefore preview against their unaccepted reciprocal plan without
        /// publishing either plan or changing streamed accepted topology.
        /// </summary>
        public bool TryResolveDraftPreview(IEnumerable<CityForgeChunkPlan> proposed, CityForgeCoord previewCoordinate, out CityForgeResolvedChunk preview, out CityForgeValidation validation)
        {
            preview = null;
            List<CityForgeChunkPlan> plans = (proposed ?? Enumerable.Empty<CityForgeChunkPlan>()).Where(item => item != null).Select(item => item.Clone()).ToList();
            if (!ValidateDraftOnly(plans, out validation)) return false;
            CityForgeChunkPlan selectedPlan = plans.FirstOrDefault(item => item.coord.Equals(previewCoordinate));
            if (selectedPlan == null) { validation.Refuse("The selected City Forge preview block is missing."); return false; }
            try
            {
                Dictionary<CityForgeCoord, CityForgeChunkPlan> neighbours = NeighbourhoodFor(previewCoordinate, selectedPlan);
                foreach (CityForgeChunkPlan plan in plans) neighbours[plan.coord] = plan;
                preview = resolver.Resolve(head, previewCoordinate, selectedPlan, neighbours, KnownLandmarksFor(neighbours), outcomeIndex);
                return true;
            }
            catch (Exception error) { validation.Refuse(error.Message); preview = null; return false; }
        }

        /// <summary>
        /// Applies only an already-authoritative service commit. It intentionally
        /// bypasses local persistence and never treats a client draft as accepted.
        /// A private bridge supplies the server's new World Head and canonical
        /// chunk revisions after D1 has committed them.
        /// </summary>
        public bool ApplyAuthoritativeCommit(CityForgeWorldHead authoritativeHead, IEnumerable<CityForgeChunkPlan> committedChunks, out CityForgeValidation validation)
        {
            validation = new CityForgeValidation();
            if (authoritativeHead == null) { validation.Refuse("City Forge needs an accepted World Head."); return false; }
            List<CityForgeChunkPlan> plans = (committedChunks ?? Enumerable.Empty<CityForgeChunkPlan>()).Where(item => item != null).Select(item => item.Clone()).ToList();
            if (plans.Count == 0 || plans.Count > 4) { validation.Refuse("The accepted City Forge commit is incomplete."); return false; }
            CityForgeWorldHead incomingHead = authoritativeHead.Clone();
            // Service pages do not carry the complete map. Preserve a complete
            // index only when the bridge explicitly supplied it.
            incomingHead.indexIsComplete = incomingHead.changedChunks != null && incomingHead.changedChunks.Count > 0;
            CityForgeValidation headValidation = validator.ValidateWorldHead(incomingHead);
            foreach (string message in headValidation.Messages) validation.Refuse(message);
            Dictionary<CityForgeCoord, CityForgeChunkPlan> atomic = new Dictionary<CityForgeCoord, CityForgeChunkPlan>();
            foreach (CityForgeChunkPlan plan in plans)
            {
                if (atomic.ContainsKey(plan.coord)) validation.Refuse("The accepted City Forge commit repeats a block.");
                else atomic.Add(plan.coord, plan);
            }
            foreach (CityForgeChunkPlan plan in plans)
            {
                if (string.IsNullOrEmpty(plan.packedBlobHash)) plan.packedBlobHash = SlopCityCityForgeCanonical.Hash(plan);
                CityForgeValidation checkedPlan = validator.ValidateChunkPlan(incomingHead, plan, atomic, false, plans.SelectMany(item => item.landmarks ?? new List<CityForgeLandmark>()), incomingHead.indexIsComplete, null);
                foreach (string message in checkedPlan.Messages) validation.Refuse(message);
            }
            if (!validation.IsValid) return false;
            InvalidateStrategicMapIfHeadChanged(incomingHead);
            InvalidateOutcomeIndexIfHeadChanged(incomingHead);
            foreach (CityForgeChunkPlan plan in plans)
            {
                accepted[plan.coord] = plan.Clone();
                UpdateStrategicSummary(plan);
                drafts.Remove(plan.coord);
            }
            head = incomingHead;
            if (head.indexIsComplete) RebuildHeadIndex();
            initialized = true;
            FreshContextReloadRequested?.Invoke(plans.Select(item => item.coord).OrderBy(item => item).ToList());
            return true;
        }

        /// <summary>
        /// Applies only an already-authoritative CitySituation lifecycle reply.
        /// Unlike a topology commit or generic hosted page load, this proves the
        /// delta contains no roads/lots/buildings/services/screens/landmarks,
        /// preserves any separate local topology draft, rebases its parent and
        /// accepted situations, and emits no fresh-context reload.
        /// </summary>
        public bool ApplyAuthoritativeSituationCommit(CityForgeWorldHead authoritativeHead, IEnumerable<CityForgeChunkPlan> committedChunks, out CityForgeValidation validation)
        {
            validation = new CityForgeValidation();
            if (!initialized || authoritativeHead == null) { validation.Refuse("City Forge needs an accepted CitySituation response."); return false; }
            List<CityForgeChunkPlan> plans = (committedChunks ?? Enumerable.Empty<CityForgeChunkPlan>()).Where(item => item != null).Select(item => item.Clone()).ToList();
            if (plans.Count == 0 || plans.Count > 4) { validation.Refuse("A CitySituation response needs one bounded accepted block set."); return false; }
            CityForgeWorldHead incoming = authoritativeHead.Clone();
            incoming.indexIsComplete = false;
            CityForgeValidation headValidation = validator.ValidateWorldHead(incoming);
            foreach (string message in headValidation.Messages) validation.Refuse(message);
            Dictionary<CityForgeCoord, CityForgeChunkPlan> atomic = new Dictionary<CityForgeCoord, CityForgeChunkPlan>();
            foreach (CityForgeChunkPlan plan in plans)
            {
                if (!accepted.TryGetValue(plan.coord, out CityForgeChunkPlan prior) || atomic.ContainsKey(plan.coord) || plan.parentChunkRevision != prior.revision || !IsSituationOnlyChange(prior, plan)) { validation.Refuse("The CitySituation response changed protected topology or has a stale parent."); continue; }
                atomic.Add(plan.coord, plan);
                if (string.IsNullOrEmpty(plan.packedBlobHash)) plan.packedBlobHash = SlopCityCityForgeCanonical.Hash(plan);
            }
            foreach (CityForgeChunkPlan plan in plans)
            {
                if (!accepted.TryGetValue(plan.coord, out CityForgeChunkPlan prior)) continue;
                // The server is authoritative for cross-site prerequisite facts;
                // a singleton reply still verifies local anchor/schema grammar.
                CityForgeValidation structure = validator.ValidateChunkPlan(incoming, plan, atomic, false, KnownLandmarksFor(atomic), false, null);
                foreach (string message in structure.Messages) validation.Refuse(message);
                CityForgeValidation transition = validator.ValidatePlanTransition(prior, plan);
                foreach (string message in transition.Messages) validation.Refuse(message);
            }
            if (!validation.IsValid) return false;
            InvalidateStrategicMapIfHeadChanged(incoming);
            InvalidateOutcomeIndexIfHeadChanged(incoming);
            foreach (CityForgeChunkPlan plan in plans)
            {
                accepted[plan.coord] = plan.Clone();
                UpdateStrategicSummary(plan);
                if (drafts.TryGetValue(plan.coord, out CityForgeChunkPlan retained)) RebaseSituationDraft(retained, plan);
            }
            head = incoming;
            initialized = true;
            AcceptedPresentationChanged?.Invoke(plans.Select(item => item.coord).OrderBy(item => item).ToList());
            return true;
        }

        /// <summary>
        /// Replaces the bounded distant-map window after the bridge has fetched
        /// and verified it against the active World Head. This never writes a
        /// local save, alters accepted plans, or streams physical topology.
        /// </summary>
        public bool ApplyStrategicMap(CityForgeStrategicMap map, out CityForgeValidation validation)
        {
            validation = new CityForgeValidation();
            if (!initialized || head == null || map == null) { validation.Refuse("City Forge needs an accepted strategic map response."); return false; }
            if (map.worldRevision != head.activeWorldRevision || !string.Equals(map.chunkMapRoot, head.chunkMapRoot, StringComparison.Ordinal))
            {
                validation.Refuse("The strategic map belongs to an older City Forge world revision.");
                return false;
            }
            if (!SlopCityCityForge.IsBuildable(map.centre) || map.radius < 0 || map.radius > 6 || map.chunks == null || map.chunks.Count > SlopCityCityForge.MaximumStrategicSummaryRecords)
            {
                validation.Refuse("The strategic map exceeds its bounded view.");
                return false;
            }
            Dictionary<CityForgeCoord, CityForgeStrategicChunkSummary> replacement = new Dictionary<CityForgeCoord, CityForgeStrategicChunkSummary>();
            foreach (CityForgeStrategicChunkSummary summary in map.chunks)
            {
                if (summary == null || !SlopCityCityForge.IsBuildable(summary.coord) || Math.Abs(summary.coord.x - map.centre.x) > map.radius || Math.Abs(summary.coord.z - map.centre.z) > map.radius
                    || !CityForgeIds.IsRuntimeId(summary.revisionId) || !CityForgeIds.IsHash(summary.snapshotHash)
                    || summary.roadCount < 0 || summary.roadCount > SlopCityCityForge.MaximumRoadRecords
                    || summary.buildingCount < 0 || summary.buildingCount > SlopCityCityForge.MaximumBuildingRecords
                    || summary.serviceCount < 0 || summary.serviceCount > SlopCityCityForge.MaximumServiceRecords
                    || summary.landmarkCount < 0 || summary.landmarkCount > SlopCityCityForge.MaximumLandmarkRecords
                    || replacement.ContainsKey(summary.coord))
                {
                    validation.Refuse("The strategic map contains an invalid changed block summary.");
                    continue;
                }
                replacement.Add(summary.coord, summary.Clone());
            }
            if (!validation.IsValid) return false;
            strategicChunks.Clear();
            foreach (KeyValuePair<CityForgeCoord, CityForgeStrategicChunkSummary> pair in replacement) strategicChunks.Add(pair.Key, pair.Value);
            StrategicMapChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Installs a compact, verified outcome-presence index for cross-site
        /// CitySituation prerequisites. It is presentation/validation metadata
        /// only: it never replaces nearby chunk topology or persists locally.
        /// </summary>
        public bool ApplyOutcomeIndex(CityForgeOutcomeIndex index, out CityForgeValidation validation)
        {
            validation = new CityForgeValidation();
            if (!initialized || head == null || index == null) { validation.Refuse("City Forge needs an accepted outcome index response."); return false; }
            if (index.worldRevision != head.activeWorldRevision || !string.Equals(index.chunkMapRoot, head.chunkMapRoot, StringComparison.Ordinal))
            {
                validation.Refuse("The Cityworks outcome index belongs to an older City Forge world revision.");
                return false;
            }
            if (index.outcomes == null || index.outcomes.Count > SlopCityCityForge.MaximumOutcomeIndexEntries)
            {
                validation.Refuse("The Cityworks outcome index exceeds its bounded catalog.");
                return false;
            }
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (CityForgeOutcomePresence row in index.outcomes)
            {
                if (row == null || !seen.Add(row.outcomeId ?? string.Empty) || !catalog.IsLandmarkOutcome(row.outcomeId)
                    || row.active < 0 || row.active > SlopCityCityForge.MaximumOutcomeStateCount
                    || row.opening < 0 || row.opening > SlopCityCityForge.MaximumOutcomeStateCount
                    || row.established < 0 || row.established > SlopCityCityForge.MaximumOutcomeStateCount)
                {
                    validation.Refuse("The Cityworks outcome index contains an invalid catalog state.");
                    return false;
                }
            }
            outcomeIndex = index.Clone();
            OutcomeIndexChanged?.Invoke();
            return true;
        }

        public bool TryRestoreOffline(out string error)
        {
            error = null;
            if (!localStore.TryLoad(out CityForgeWorldHead savedHead, out List<CityForgeChunkPlan> savedChunks, out error))
            {
                if (localStore.HasWorld && !string.IsNullOrEmpty(error)) OfflineRecoveryRequired?.Invoke(error);
                return false;
            }
            try { InitializeOffline(null, savedHead, savedChunks); return true; }
            catch (Exception) { error = "The saved City Forge world is not compatible with this runtime."; return false; }
        }

        public bool TrySaveOffline(out string error)
        {
            error = null;
            if (!initialized) { error = "City Forge is still starting."; return false; }
            return localStore.TrySave(head, accepted.Values, out error);
        }

        public bool CanDraft(CityForgeCoord coordinate)
        {
            if (!initialized || !SlopCityCityForge.IsBuildable(coordinate)) return false;
            if (SlopCityCityForge.IsStarterRing(coordinate) || accepted.ContainsKey(coordinate)) return true;
            foreach (CityForgeDirection direction in new[] { CityForgeDirection.North, CityForgeDirection.East, CityForgeDirection.South, CityForgeDirection.West })
                if (accepted.ContainsKey(SlopCityCityForge.Neighbour(coordinate, direction))) return true;
            return false;
        }

        public CityForgeChunkPlan BeginDraft(CityForgeCoord coordinate)
        {
            if (!CanDraft(coordinate)) throw new InvalidOperationException("Survey an adjoining accepted block before planning there.");
            if (drafts.TryGetValue(coordinate, out CityForgeChunkPlan current)) return current.Clone();
            CityForgeChunkPlan draft = accepted.TryGetValue(coordinate, out CityForgeChunkPlan saved)
                ? MutableDraftFromAccepted(saved) : NewPlan(coordinate);
            drafts[coordinate] = draft;
            return draft.Clone();
        }

        public bool TrySavePrivateDraft(CityForgeChunkPlan draft, out string message)
        {
            message = null;
            if (draft == null || !CanDraft(draft.coord)) { message = "Survey an adjoining accepted block before planning there."; return false; }
            drafts[draft.coord] = draft.Clone();
            return true;
        }

        /// <summary>
        /// Prepares, but does not accept, the next authored CitySituation state.
        /// Hosted compositions submit this exact plan to their authority; offline
        /// composition may use TryAdvanceSituationOffline after this validation.
        /// </summary>
        public bool TryPrepareSituationTransition(CityForgeCoord coordinate, string situationId, out CityForgeChunkPlan proposed, out CityForgeSituationState next, out CityForgeValidation validation)
        {
            proposed = null;
            next = null;
            validation = new CityForgeValidation();
            if (!initialized || !accepted.TryGetValue(coordinate, out CityForgeChunkPlan current)) { validation.Refuse("This CitySituation is no longer in the streamed city."); return false; }
            CityForgeSituationState existing = (current.situations ?? new List<CityForgeSituationState>()).FirstOrDefault(item => item != null && string.Equals(item.id, situationId, StringComparison.Ordinal));
            if (existing == null) { validation.Refuse("This CitySituation is no longer available."); return false; }
            string state = NextSituationState(existing.state);
            if (string.IsNullOrEmpty(state)) { validation.Refuse("This CitySituation has already reached its final state."); return false; }
            proposed = MutableDraftFromAccepted(current);
            CityForgeSituationState replacement = (proposed.situations ?? new List<CityForgeSituationState>()).FirstOrDefault(item => item != null && string.Equals(item.id, existing.id, StringComparison.Ordinal));
            if (replacement == null) { validation.Refuse("This CitySituation could not be prepared safely."); proposed = null; return false; }
            replacement.state = state;
            next = replacement.Clone();
            CityForgeValidation structure = validator.ValidateChunkPlan(head, proposed, new Dictionary<CityForgeCoord, CityForgeChunkPlan> { { coordinate, proposed } }, false, KnownLandmarksFor(new Dictionary<CityForgeCoord, CityForgeChunkPlan> { { coordinate, proposed } }), true, outcomeIndex);
            foreach (string message in structure.Messages) validation.Refuse(message);
            CityForgeValidation transition = validator.ValidatePlanTransition(current, proposed);
            foreach (string message in transition.Messages) validation.Refuse(message);
            if (!validation.IsValid) { proposed = null; next = null; }
            return validation.IsValid;
        }

        /// <summary>Persists a physical CitySituation USE in offline mode without a topology reload.</summary>
        public bool TryAdvanceSituationOffline(CityForgeCoord coordinate, string situationId, out CityForgeSituationState next, out CityForgeValidation validation)
        {
            if (!TryPrepareSituationTransition(coordinate, situationId, out CityForgeChunkPlan proposed, out next, out validation)) return false;
            if (!TryCommit(new[] { proposed }, false, false, out validation)) { next = null; return false; }
            return true;
        }

        /// <summary>Prepares first-use materialization of a deterministic CitySituation opportunity.</summary>
        public bool TryPrepareSituationMaterialization(CityForgeCoord coordinate, CityForgeSituationOpportunity opportunity, out CityForgeChunkPlan proposed, out CityForgeSituationState created, out CityForgeValidation validation)
        {
            proposed = null;
            created = null;
            validation = new CityForgeValidation();
            if (!initialized || opportunity == null || !accepted.TryGetValue(coordinate, out CityForgeChunkPlan current)) { validation.Refuse("This CitySituation opportunity is no longer in the streamed city."); return false; }
            if (!catalog.IsSituation(opportunity.situationId) || string.IsNullOrEmpty(opportunity.landmarkId) || string.IsNullOrEmpty(opportunity.stableSeed)) { validation.Refuse("This CitySituation opportunity is not approved by the installed catalog."); return false; }
            if (!(current.landmarks ?? new List<CityForgeLandmark>()).Any(item => item != null && string.Equals(item.id, opportunity.landmarkId, StringComparison.Ordinal))) { validation.Refuse("This CitySituation no longer has its Cityworks site."); return false; }
            if ((current.situations ?? new List<CityForgeSituationState>()).Any(item => item != null && string.Equals(item.landmarkId, opportunity.landmarkId, StringComparison.Ordinal) && string.Equals(item.situationId, opportunity.situationId, StringComparison.Ordinal))) { validation.Refuse("This CitySituation is already part of the city."); return false; }
            proposed = MutableDraftFromAccepted(current);
            created = new CityForgeSituationState
            {
                id = SlopCityCityForge.StableId("situation", coordinate, opportunity.landmarkId + "|" + opportunity.situationId),
                situationId = opportunity.situationId,
                landmarkId = opportunity.landmarkId,
                state = "available",
                stableSeed = opportunity.stableSeed
            };
            proposed.situations.Add(created.Clone());
            CityForgeValidation structure = validator.ValidateChunkPlan(head, proposed, new Dictionary<CityForgeCoord, CityForgeChunkPlan> { { coordinate, proposed } }, false, KnownLandmarksFor(new Dictionary<CityForgeCoord, CityForgeChunkPlan> { { coordinate, proposed } }), true, outcomeIndex);
            foreach (string message in structure.Messages) validation.Refuse(message);
            CityForgeValidation transition = validator.ValidatePlanTransition(current, proposed);
            foreach (string message in transition.Messages) validation.Refuse(message);
            if (!validation.IsValid) { proposed = null; created = null; }
            return validation.IsValid;
        }

        /// <summary>Offline first-use persistence for a bounded derived opportunity.</summary>
        public bool TryMaterializeSituationOffline(CityForgeCoord coordinate, CityForgeSituationOpportunity opportunity, out CityForgeSituationState created, out CityForgeValidation validation)
        {
            if (!TryPrepareSituationMaterialization(coordinate, opportunity, out CityForgeChunkPlan proposed, out created, out validation)) return false;
            if (!TryCommit(new[] { proposed }, false, false, out validation)) { created = null; return false; }
            return true;
        }

        /// <summary>
        /// Validates a one-chunk CitySituation-only proposal without requiring
        /// unrelated reciprocal road peers. Hosted bridges use this before an
        /// authority-owned lifecycle mutation; it proves no topology draft is
        /// being smuggled through the lightweight path.
        /// </summary>
        public bool ValidateSituationOnlyDraft(CityForgeChunkPlan proposed, out CityForgeValidation validation)
        {
            validation = new CityForgeValidation();
            if (!initialized || proposed == null || !accepted.TryGetValue(proposed.coord, out CityForgeChunkPlan prior)) { validation.Refuse("This CitySituation block is no longer loaded."); return false; }
            if (proposed.parentChunkRevision != prior.revision || !IsSituationOnlyChange(prior, proposed)) { validation.Refuse("A CitySituation update may not alter roads, lots, buildings, screens, or services."); return false; }
            Dictionary<CityForgeCoord, CityForgeChunkPlan> atomic = new Dictionary<CityForgeCoord, CityForgeChunkPlan> { { proposed.coord, proposed } };
            CityForgeValidation structure = validator.ValidateChunkPlan(head, proposed, atomic, false, KnownLandmarksFor(atomic), true, outcomeIndex);
            foreach (string message in structure.Messages) validation.Refuse(message);
            CityForgeValidation transition = validator.ValidatePlanTransition(prior, proposed);
            foreach (string message in transition.Messages) validation.Refuse(message);
            return validation.IsValid;
        }

        /// <summary>
        /// Commits a single chunk or an atomic set of adjacent chunks. Callers must
        /// include both sides of every edited boundary so roads and utilities cannot
        /// tear on a stream/reload boundary.
        /// </summary>
        public bool TryCommit(IEnumerable<CityForgeChunkPlan> proposed, out CityForgeValidation validation)
        {
            return TryCommit(proposed, true, true, out validation);
        }

        /// <summary>
        /// Internal/public-core commit variant. Boundary-peer bypass is reserved
        /// for a situation-only update with no road, parcel, or building change.
        /// Hosted integrations must wait for ApplyAuthoritativeCommit instead.
        /// </summary>
        public bool TryCommit(IEnumerable<CityForgeChunkPlan> proposed, bool requireBoundaryPeers, bool requestFreshContextReload, out CityForgeValidation validation)
        {
            validation = new CityForgeValidation();
            if (!initialized) { validation.Refuse("City Forge is still starting."); return false; }
            List<CityForgeChunkPlan> plans = (proposed ?? Enumerable.Empty<CityForgeChunkPlan>()).Where(value => value != null).Select(value => value.Clone()).ToList();
            if (plans.Count == 0 || plans.Count > 4) { validation.Refuse("Choose one nearby block, or one connected street crossing."); return false; }
            Dictionary<CityForgeCoord, CityForgeChunkPlan> atomic = new Dictionary<CityForgeCoord, CityForgeChunkPlan>();
            CityForgeWorldHead beforeHead = head.Clone();
            Dictionary<CityForgeCoord, CityForgeChunkPlan> beforeAccepted = accepted.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
            foreach (CityForgeChunkPlan plan in plans)
            {
                if (!CanDraft(plan.coord) || atomic.ContainsKey(plan.coord)) { validation.Refuse("Each accepted plan must be a unique adjoining block."); return false; }
                atomic.Add(plan.coord, plan);
            }
            if (!requireBoundaryPeers)
            {
                foreach (CityForgeChunkPlan plan in plans)
                    if (!accepted.TryGetValue(plan.coord, out CityForgeChunkPlan prior) || !IsSituationOnlyChange(prior, plan)) { validation.Refuse("Only a CitySituation lifecycle change may bypass a reciprocal street command."); return false; }
            }
            foreach (CityForgeChunkPlan plan in plans)
            {
                CityForgeValidation result = validator.ValidateChunkPlan(head, plan, atomic, requireBoundaryPeers, KnownLandmarksFor(atomic), true, outcomeIndex);
                foreach (string message in result.Messages) validation.Refuse(message);
                if (accepted.TryGetValue(plan.coord, out CityForgeChunkPlan prior))
                {
                    CityForgeValidation transition = validator.ValidatePlanTransition(prior, plan);
                    foreach (string message in transition.Messages) validation.Refuse(message);
                }
            }
            if (!validation.IsValid)
            {
                foreach (string message in validation.Messages) ValidationRefused?.Invoke(message);
                return false;
            }

            long nextWorldRevision = head.activeWorldRevision + 1;
            foreach (CityForgeChunkPlan plan in plans)
            {
                long currentRevision = accepted.TryGetValue(plan.coord, out CityForgeChunkPlan prior) ? prior.revision : 0;
                if (plan.parentChunkRevision != currentRevision)
                {
                    validation.Refuse("This block changed while you were planning. Reload it before building.");
                    ValidationRefused?.Invoke(validation.Messages[0]);
                    return false;
                }
                plan.revision = currentRevision + 1;
                // Offline plans are immutable revision records too. A cloned
                // draft may carry the prior id, so always derive the new local
                // revision identity before the index/root is rebuilt.
                plan.revisionId = SlopCityCityForgeCanonical.LocalRevisionId(plan.coord, plan.revision);
                plan.packedBlobHash = SlopCityCityForgeCanonical.Hash(plan);
            }
            foreach (CityForgeChunkPlan plan in plans)
            {
                accepted[plan.coord] = plan.Clone();
                UpdateStrategicSummary(plan);
                if (requestFreshContextReload) drafts.Remove(plan.coord);
                else if (drafts.TryGetValue(plan.coord, out CityForgeChunkPlan retained))
                {
                    // Preserve a separate topology draft across an independent
                    // life consequence. Rebase only its accepted lifecycle and
                    // parent revision; its roads/lots/buildings remain a draft.
                    RebaseSituationDraft(retained, plan);
                }
            }
            head.activeWorldRevision = nextWorldRevision;
            // A local accepted mutation changes the map root. Any hosted
            // strategic window is no longer proof for this head and must be
            // replaced by a later bounded response.
            strategicChunks.Clear();
            outcomeIndex = null;
            foreach (CityForgeChunkPlan plan in plans) UpdateStrategicSummary(plan);
            RebuildHeadIndex();
            if (automaticallyPersistOffline && !TrySaveOffline(out string saveError))
            {
                accepted.Clear();
                foreach (KeyValuePair<CityForgeCoord, CityForgeChunkPlan> prior in beforeAccepted) accepted.Add(prior.Key, prior.Value);
                head = beforeHead;
                validation.Refuse(saveError ?? "This city could not be saved safely on this device.");
                ValidationRefused?.Invoke(validation.Messages.Last());
                return false;
            }
            List<CityForgeCoord> changed = plans.Select(item => item.coord).OrderBy(item => item).ToList();
            if (requestFreshContextReload) FreshContextReloadRequested?.Invoke(changed);
            else AcceptedPresentationChanged?.Invoke(changed);
            return true;
        }

        private static string NextSituationState(string state)
        {
            if (state == "unavailable") return "available";
            if (state == "available") return "active";
            if (state == "active") return "resolved";
            if (state == "resolved") return "archived";
            if (state == "paused") return "available";
            return null;
        }

        private static bool IsSituationOnlyChange(CityForgeChunkPlan previous, CityForgeChunkPlan next)
        {
            if (previous == null || next == null) return false;
            CityForgeChunkPlan left = previous.Clone(), right = next.Clone();
            left.situations = new List<CityForgeSituationState>();
            right.situations = new List<CityForgeSituationState>();
            left.packedBlobHash = right.packedBlobHash = string.Empty;
            // Parent concurrency is checked explicitly at each callsite. It
            // legitimately advances in an accepted lifecycle response and is
            // not a player-authored topology change.
            left.parentChunkRevision = right.parentChunkRevision = 0;
            left.revision = right.revision = 0;
            left.revisionId = right.revisionId = string.Empty;
            return string.Equals(SlopCityCityForgeCanonical.Pack(left), SlopCityCityForgeCanonical.Pack(right), StringComparison.Ordinal);
        }

        /// <summary>
        /// Makes an editable proposal from an immutable accepted revision. This
        /// is the single path for local editor/lifecycle drafts so parent
        /// concurrency is always explicit and revision identities never leak
        /// from one accepted save into the next command.
        /// </summary>
        private static CityForgeChunkPlan MutableDraftFromAccepted(CityForgeChunkPlan acceptedPlan)
        {
            if (acceptedPlan == null) return null;
            CityForgeChunkPlan draft = acceptedPlan.Clone();
            draft.parentChunkRevision = acceptedPlan.revision;
            draft.revision = 0;
            draft.revisionId = string.Empty;
            draft.packedBlobHash = string.Empty;
            return draft;
        }

        private static void RebaseSituationDraft(CityForgeChunkPlan retained, CityForgeChunkPlan acceptedPlan)
        {
            if (retained == null || acceptedPlan == null) return;
            retained.parentChunkRevision = acceptedPlan.revision;
            retained.revision = 0;
            retained.revisionId = string.Empty;
            retained.situations = (acceptedPlan.situations ?? new List<CityForgeSituationState>()).Select(item => item.Clone()).ToList();
            retained.packedBlobHash = string.Empty;
        }

        public CityForgeResolvedChunk Resolve(CityForgeCoord coordinate)
        {
            EnsureInitialized();
            accepted.TryGetValue(coordinate, out CityForgeChunkPlan plan);
            Dictionary<CityForgeCoord, CityForgeChunkPlan> neighbours = NeighbourhoodFor(coordinate, plan);
            return resolver.Resolve(head, coordinate, plan, neighbours, KnownLandmarksFor(neighbours), outcomeIndex);
        }

        public void StreamAt(CityForgeCoord center)
        {
            EnsureInitialized();
            if (!compiler) return;
            List<CityForgeResolvedChunk> requested = new List<CityForgeResolvedChunk>();
            for (int x = -activeChunkRadius; x <= activeChunkRadius; x++)
                for (int z = -activeChunkRadius; z <= activeChunkRadius; z++)
                {
                    CityForgeCoord coordinate = new CityForgeCoord(center.x + x, center.z + z);
                    if (!SlopCityCityForge.IsBuildable(coordinate)) continue;
                    requested.Add(Resolve(coordinate));
                }
            compiler.Reconcile(center, requested, constrainedWebGl);
        }

        public void SetBuildMode(bool enabled)
        {
            EnsureInitialized();
            if (buildMode == enabled) return;
            buildMode = enabled;
            BuildModeChanged?.Invoke(buildMode);
        }

        public bool TryGetAccepted(CityForgeCoord coordinate, out CityForgeChunkPlan plan)
        {
            if (accepted.TryGetValue(coordinate, out CityForgeChunkPlan current)) { plan = current.Clone(); return true; }
            plan = null;
            return false;
        }

        private CityForgeChunkPlan NewPlan(CityForgeCoord coordinate)
        {
            CityForgeBaseChunk baseChunk = new SlopCityCityForgeGenerator(catalog).Generate(head, coordinate);
            return new CityForgeChunkPlan
            {
                schemaVersion = SlopCityCityForge.ChunkSchemaVersion,
                coord = coordinate,
                baseChunkHash = baseChunk.baseHash,
                parentChunkRevision = 0,
                revision = 0,
                roads = new List<CityForgeRoad>(), parcels = new List<CityForgeParcel>(), buildings = new List<CityForgeBuilding>(),
                services = new List<CityForgeService>(), screens = new List<CityForgeScreen>(), landmarks = new List<CityForgeLandmark>(), situations = new List<CityForgeSituationState>()
            };
        }

        private Dictionary<CityForgeCoord, CityForgeChunkPlan> NeighbourhoodFor(CityForgeCoord coordinate, CityForgeChunkPlan center)
        {
            Dictionary<CityForgeCoord, CityForgeChunkPlan> result = new Dictionary<CityForgeCoord, CityForgeChunkPlan>();
            if (center != null) result[coordinate] = center;
            foreach (CityForgeDirection direction in new[] { CityForgeDirection.North, CityForgeDirection.East, CityForgeDirection.South, CityForgeDirection.West })
            {
                CityForgeCoord neighbour = SlopCityCityForge.Neighbour(coordinate, direction);
                if (accepted.TryGetValue(neighbour, out CityForgeChunkPlan plan)) result[neighbour] = plan;
            }
            return result;
        }

        /// <summary>
        /// Builds a compact landmark outcome index for validation/pulse work.
        /// Landmark IDs stay scoped to their own chunk; callers still pass the
        /// local plan separately for anchor resolution.
        /// </summary>
        private List<CityForgeLandmark> KnownLandmarksFor(IDictionary<CityForgeCoord, CityForgeChunkPlan> replacements)
        {
            HashSet<CityForgeCoord> replaced = new HashSet<CityForgeCoord>((replacements ?? new Dictionary<CityForgeCoord, CityForgeChunkPlan>()).Keys);
            List<CityForgeLandmark> result = new List<CityForgeLandmark>();
            foreach (KeyValuePair<CityForgeCoord, CityForgeChunkPlan> pair in accepted)
            {
                if (replaced.Contains(pair.Key)) continue;
                foreach (CityForgeLandmark landmark in pair.Value.landmarks ?? new List<CityForgeLandmark>()) if (landmark != null) result.Add(landmark);
            }
            foreach (KeyValuePair<CityForgeCoord, CityForgeChunkPlan> pair in replacements ?? new Dictionary<CityForgeCoord, CityForgeChunkPlan>())
            {
                IEnumerable<CityForgeLandmark> landmarks = pair.Value == null ? Enumerable.Empty<CityForgeLandmark>() : (pair.Value.landmarks ?? new List<CityForgeLandmark>());
                foreach (CityForgeLandmark landmark in landmarks) if (landmark != null) result.Add(landmark);
            }
            return result;
        }

        private void InvalidateStrategicMapIfHeadChanged(CityForgeWorldHead incoming)
        {
            if (incoming == null || head == null || incoming.activeWorldRevision != head.activeWorldRevision
                || !string.Equals(incoming.chunkMapRoot, head.chunkMapRoot, StringComparison.Ordinal)) strategicChunks.Clear();
        }

        private void InvalidateOutcomeIndexIfHeadChanged(CityForgeWorldHead incoming)
        {
            if (incoming == null || head == null || incoming.activeWorldRevision != head.activeWorldRevision
                || !string.Equals(incoming.chunkMapRoot, head.chunkMapRoot, StringComparison.Ordinal)) outcomeIndex = null;
        }

        private void UpdateStrategicSummary(CityForgeChunkPlan plan)
        {
            if (plan == null || plan.revision < 1 || !SlopCityCityForge.IsBuildable(plan.coord)) return;
            string snapshot = string.IsNullOrEmpty(plan.packedBlobHash) ? SlopCityCityForgeCanonical.Hash(plan) : plan.packedBlobHash;
            if (!CityForgeIds.IsRuntimeId(plan.revisionId) || !CityForgeIds.IsHash(snapshot)) return;
            strategicChunks[plan.coord] = new CityForgeStrategicChunkSummary
            {
                coord = plan.coord,
                revisionId = plan.revisionId,
                snapshotHash = snapshot,
                roadCount = (plan.roads ?? new List<CityForgeRoad>()).Count(item => item != null),
                buildingCount = (plan.buildings ?? new List<CityForgeBuilding>()).Count(item => item != null),
                serviceCount = (plan.services ?? new List<CityForgeService>()).Count(item => item != null),
                landmarkCount = (plan.landmarks ?? new List<CityForgeLandmark>()).Count(item => item != null)
            };
        }

        private void RebuildHeadIndex()
        {
            head.changedChunks = accepted.Values.OrderBy(value => value.coord).Select(value =>
            {
                string snapshot = string.IsNullOrEmpty(value.packedBlobHash) ? SlopCityCityForgeCanonical.Hash(value) : value.packedBlobHash;
                if (string.IsNullOrEmpty(value.revisionId)) value.revisionId = SlopCityCityForgeCanonical.LocalRevisionId(value.coord, Math.Max(1, value.revision));
                return new CityForgeChunkHead
                {
                    coord = value.coord,
                    revisionId = value.revisionId,
                    revision = value.revision,
                    baseChunkHash = value.baseChunkHash,
                    snapshotHash = snapshot
                };
            }).ToList();
            head.chunkMapRoot = SlopCityCityForgeCanonical.ChunkMapRoot(head.changedChunks);
        }

        private void EnsureInitialized()
        {
            if (!initialized) throw new InvalidOperationException("City Forge has not been initialized.");
        }

        private void HandleCompiled(CityForgeResolvedChunk chunk)
        {
            ChunkCompiled?.Invoke(chunk);
        }
    }
}
