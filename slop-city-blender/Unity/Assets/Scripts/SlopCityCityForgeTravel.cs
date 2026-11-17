using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Public Old City→Sprawl entry hook. The fixed Old City pavement reaches
    /// the reserved core boundary, so travel needs no generated bridge or
    /// overlapping seam geometry.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityForgeTravel : MonoBehaviour
    {
        // The entry point sits on the fixed pavement immediately before the
        // first editable cell. Keep travel, fall recovery, and safe reload on
        // this known-solid location; full-size ground beyond x=144 remains
        // player-laid island ground.
        public static readonly CityForgeCoord StarterEntryCoordinate = new CityForgeCoord(2, 0);
        public static readonly Vector3 StarterEntryPosition = new Vector3(141f, 2f, 36f);
        private const float StarterEntryHeading = 90f;

        /// <summary>
        /// Treat the fixed entry apron beside the first editable cell as Sky
        /// Sprawl for sky, fall, and recovery purposes even though it remains
        /// part of the immutable Old City core.
        /// </summary>
        public static bool IsSkySprawlPosition(Vector3 position)
        {
            return SlopCityCityForge.IsBuildableWorldPosition(position) || IsStarterEntryDeck(position);
        }

        public static bool IsStarterEntryDeck(Vector3 position)
        {
            // Keep a small edge allowance so a player stepping from the fixed
            // Old City pavement into the first editable cell retains Sprawl
            // fall recovery instead of being sent to the couch.
            return position.x >= 137f && position.x <= 145f && position.z >= 33.5f && position.z <= 38.5f;
        }
        [SerializeField] private SlopCityCityForgeController controller;
        [SerializeField] private Transform traveler;
        [SerializeField] private KeyCode travelKey = KeyCode.V;

        public event Action<CityForgeCoord> TravelRequested;
        public event Func<CityForgeCoord, bool> EnsureChunkLoaded;

        public void Configure(SlopCityCityForgeController cityController, Transform player)
        {
            controller = cityController; traveler = player;
        }

        private void Awake()
        {
            if (!controller) controller = GetComponent<SlopCityCityForgeController>();
            if (!traveler && Camera.main) traveler = Camera.main.transform.root;
        }

        private void Update()
        {
            if (Input.GetKeyDown(travelKey)) TryEnterStarterSprawl();
        }

        public bool TryEnterStarterSprawl()
        {
            if (!controller || !controller.IsInitialized || !traveler) return false;
            // Any reclaimed core-facing starter block can become the first
            // public shoreline entrance. The old fixed east entry remains a
            // safe recovery deck, but must not strand a player who developed
            // the north, south, or west side first.
            if (!TrySelectBuiltEntry(out CityForgeCoord entry)) return false;
            TravelRequested?.Invoke(entry);
            if (EnsureChunkLoaded != null)
            {
                foreach (Delegate callback in EnsureChunkLoaded.GetInvocationList())
                    if (!(bool)callback.DynamicInvoke(entry)) return false;
            }
            // The entry apron is part of Old City, but the player may only
            // cross into a real accepted island block. Do not promise travel
            // into empty water or an unbuilt starter record.
            if (!controller.HasAcceptedIslandGround(entry)) return false;
            try { controller.StreamAt(entry); }
            catch { return false; }
            if (!controller.TryGetAccepted(entry, out CityForgeChunkPlan plan)
                || !TryFindEntryLanding(entry, plan, out Vector3 landing)) return false;
            // Moving just the root transform leaves a child first-person
            // camera at its previous world position after City Forge has
            // temporarily used it for the strategic map.  Use the shared
            // player teleport so the capsule and eye return together.
            SlopCityFirstPerson firstPerson = traveler.GetComponent<SlopCityFirstPerson>();
            if (firstPerson)
                // Face outward into open sky. Arriving while still looking
                // inward at the Old City made its finite core floor read like
                // default Sprawl terrain.
                firstPerson.TeleportTo(landing, HeadingForEntry(entry));
            else
            {
                CharacterController character = traveler.GetComponent<CharacterController>();
                if (character) character.enabled = false;
                traveler.position = landing;
                if (character) character.enabled = true;
            }
            return true;
        }

        private bool TrySelectBuiltEntry(out CityForgeCoord entry)
        {
            entry = StarterEntryCoordinate;
            float best = float.PositiveInfinity;
            foreach (KeyValuePair<CityForgeCoord, CityForgeChunkPlan> item in controller.AcceptedChunks)
            {
                if (string.IsNullOrEmpty(SlopCityCityForge.OldCityConnectionStubEdge(item.Key))
                    || !controller.HasAcceptedIslandGround(item.Key)) continue;
                Vector3 centre = new Vector3((item.Key.x + .5f) * SlopCityCityForge.ChunkSizeMeters, 0f,
                    (item.Key.z + .5f) * SlopCityCityForge.ChunkSizeMeters);
                float distance = (centre - traveler.position).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                entry = item.Key;
            }
            return !float.IsPositiveInfinity(best);
        }

        private static bool TryFindEntryLanding(CityForgeCoord entry, CityForgeChunkPlan plan, out Vector3 landing)
        {
            landing = default;
            if (plan == null || plan.groundCells == null || plan.groundCells.Count == 0) return false;
            string edge = SlopCityCityForge.OldCityConnectionStubEdge(entry);
            if (string.IsNullOrEmpty(edge)) return false;
            var cells = new List<int>(plan.groundCells);
            cells.Sort((left, right) => EntryCellScore(left, edge).CompareTo(EntryCellScore(right, edge)));
            foreach (int cell in cells)
            {
                if (cell < 0 || cell >= SlopCityCityForge.GridSize * SlopCityCityForge.GridSize) continue;
                int x = cell % SlopCityCityForge.GridSize;
                int z = cell / SlopCityCityForge.GridSize;
                Vector3 probe = new Vector3((entry.x * SlopCityCityForge.ChunkSizeMeters) + (x + .5f) * SlopCityCityForge.CellSizeMeters,
                    260f, (entry.z * SlopCityCityForge.ChunkSizeMeters) + (z + .5f) * SlopCityCityForge.CellSizeMeters);
                bool foundGround = false;
                float groundHeight = float.NegativeInfinity;
                foreach (RaycastHit hit in Physics.RaycastAll(probe, Vector3.down, 320f, ~0, QueryTriggerInteraction.Ignore))
                {
                    // A parcel may carry a tall facade above its reclaimed
                    // cell. Choose the actual ground/road surface, not its
                    // roof, and keep the player below ordinary curb height.
                    if (hit.normal.y <= .5f || hit.point.y < -.5f || hit.point.y > 1.25f || hit.point.y <= groundHeight) continue;
                    groundHeight = hit.point.y;
                    landing = hit.point;
                    foundGround = true;
                }
                if (foundGround) return true;
            }
            return false;
        }

        private static int EntryCellScore(int cell, string edge)
        {
            int x = cell % SlopCityCityForge.GridSize;
            int z = cell / SlopCityCityForge.GridSize;
            // The score first chooses a cell on the core-facing side, then one
            // near the middle of that side so the arrival stays away from a
            // corner and the outside water boundary.
            if (edge.EndsWith(":east", StringComparison.Ordinal)) return (SlopCityCityForge.GridSize - 1 - x) * 100 + Mathf.Abs(z - 4);
            if (edge.EndsWith(":west", StringComparison.Ordinal)) return x * 100 + Mathf.Abs(z - 4);
            if (edge.EndsWith(":north", StringComparison.Ordinal)) return (SlopCityCityForge.GridSize - 1 - z) * 100 + Mathf.Abs(x - 4);
            return z * 100 + Mathf.Abs(x - 4);
        }

        private static float HeadingForEntry(CityForgeCoord entry)
        {
            string edge = SlopCityCityForge.OldCityConnectionStubEdge(entry);
            if (edge != null && edge.EndsWith(":east", StringComparison.Ordinal)) return 270f;
            if (edge != null && edge.EndsWith(":west", StringComparison.Ordinal)) return 90f;
            if (edge != null && edge.EndsWith(":north", StringComparison.Ordinal)) return 180f;
            return 0f;
        }

    }
}
