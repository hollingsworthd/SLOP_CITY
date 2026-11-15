using System;
using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Public Old City→Sprawl entry bridge. It creates a small derived physical
    /// crossing across the authored perimeter gap and offers a reusable safe
    /// travel hook; no Old City transform or authored scene is modified.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityForgeTravel : MonoBehaviour
    {
        // The narrow entry gangway is an intentional safe arrival point, not
        // default Sprawl ground. Keep travel, fall recovery, and safe reload
        // on the same known-solid spot. It stops at the first editable chunk:
        // all full-size ground beyond it must be player-laid island ground.
        public static readonly CityForgeCoord StarterEntryCoordinate = new CityForgeCoord(2, 0);
        public static readonly Vector3 StarterEntryPosition = new Vector3(141f, 2f, 36f);
        private const float StarterEntryHeading = 90f;

        /// <summary>
        /// The crossing slightly overlaps the immutable Old City grid. Treat
        /// that physical deck as Sky Sprawl for sky, fall, and recovery
        /// purposes even though it is not an editable chunk.
        /// </summary>
        public static bool IsSkySprawlPosition(Vector3 position)
        {
            return SlopCityCityForge.IsBuildableWorldPosition(position) || IsStarterEntryDeck(position);
        }

        public static bool IsStarterEntryDeck(Vector3 position)
        {
            // Matches the physical gangway made in EnsureBridge(), plus a
            // small edge allowance so a player stepping off it retains Sprawl
            // fall recovery instead of being sent to the couch.
            return position.x >= 137f && position.x <= 145f && position.z >= 33.5f && position.z <= 38.5f;
        }
        [SerializeField] private SlopCityCityForgeController controller;
        [SerializeField] private Transform traveler;
        [SerializeField] private KeyCode travelKey = KeyCode.V;
        private GameObject bridge;

        public event Action<CityForgeCoord> TravelRequested;
        public event Func<CityForgeCoord, bool> EnsureChunkLoaded;

        public void Configure(SlopCityCityForgeController cityController, Transform player)
        {
            controller = cityController; traveler = player; EnsureBridge();
        }

        private void Awake()
        {
            if (!controller) controller = GetComponent<SlopCityCityForgeController>();
            if (!traveler && Camera.main) traveler = Camera.main.transform.root;
            EnsureBridge();
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

        private void EnsureBridge()
        {
            if (bridge) return;
            bridge = new GameObject("CityForge_StarterSprawlBridge"); bridge.transform.SetParent(transform, false);
            // A clear, narrow gangway crosses the Old City seam. Do not put
            // a deck, rails, or starter pad into the editable chunk: those
            // looked like a default floor and prevented the player from
            // walking straight into the cloud-filled void. The bridge ends
            // exactly at x=144, where player-authored 8m island cells begin.
            Build("OpenSkyGangway", new Vector3(141f, -.12f, 36f), new Vector3(6f, .24f, 2.2f));
        }

        private void Build(string name, Vector3 position, Vector3 scale)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube); piece.name = name; piece.transform.SetParent(bridge.transform, false); piece.transform.position = position; piece.transform.localScale = scale;
        }
    }
}
