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
        // The small starter deck is an intentional safe arrival point, not
        // default Sprawl ground. Keep travel, fall recovery, and safe reload
        // on the same known-solid spot.
        public static readonly CityForgeCoord StarterEntryCoordinate = new CityForgeCoord(2, 0);
        public static readonly Vector3 StarterEntryPosition = new Vector3(148f, 2f, 36f);
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
            CharacterController character = traveler.GetComponent<CharacterController>();
            if (character) character.enabled = false;
            traveler.position = StarterEntryPosition;
            if (character) character.enabled = true;
            return true;
        }

        private void EnsureBridge()
        {
            if (bridge) return;
            bridge = new GameObject("CityForge_StarterSprawlBridge"); bridge.transform.SetParent(transform, false);
            // Extend just far enough beyond the Old City seam for the entry
            // spawn to be safely above the void. The surrounding Sprawl stays
            // open sky until the player lays island ground in City Forge.
            Build("Deck", new Vector3(145f, -.12f, 36f), new Vector3(14f, .24f, 8f));
            Build("NorthRail", new Vector3(145f, 1f, 39.5f), new Vector3(14f, 2f, .16f));
            Build("SouthRail", new Vector3(145f, 1f, 32.5f), new Vector3(14f, 2f, .16f));
        }

        private void Build(string name, Vector3 position, Vector3 scale)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube); piece.name = name; piece.transform.SetParent(bridge.transform, false); piece.transform.position = position; piece.transform.localScale = scale;
        }
    }
}
