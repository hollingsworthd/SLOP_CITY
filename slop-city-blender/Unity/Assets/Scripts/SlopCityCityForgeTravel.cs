using System;
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
            CityForgeCoord entry = StarterEntryCoordinate;
            TravelRequested?.Invoke(entry);
            if (EnsureChunkLoaded != null)
            {
                foreach (Delegate callback in EnsureChunkLoaded.GetInvocationList())
                    if (!(bool)callback.DynamicInvoke(entry)) return false;
            }
            try { controller.StreamAt(entry); }
            catch { return false; }
            // Moving just the root transform leaves a child first-person
            // camera at its previous world position after City Forge has
            // temporarily used it for the strategic map.  Use the shared
            // player teleport so the capsule and eye return together.
            SlopCityFirstPerson firstPerson = traveler.GetComponent<SlopCityFirstPerson>();
            if (firstPerson)
                // Face outward into open sky. Arriving while still looking
                // inward at the Old City made its finite core floor read like
                // default Sprawl terrain.
                firstPerson.TeleportTo(StarterEntryPosition, StarterEntryHeading);
            else
            {
                CharacterController character = traveler.GetComponent<CharacterController>();
                if (character) character.enabled = false;
                traveler.position = StarterEntryPosition;
                if (character) character.enabled = true;
            }
            return true;
        }

    }
}
