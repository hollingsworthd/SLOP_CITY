using System;
using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Offline-only normal streaming follower. It advances the bounded visual
    /// window after a walker is safely inside a new Sprawl block; it never loads
    /// authority, replaces an accepted revision, teleports the player, or emits
    /// a fresh-context topology reload request. Hosted composition owns page
    /// acquisition and should not attach this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityForgeOfflineStreamFollower : MonoBehaviour
    {
        [SerializeField] private SlopCityCityForgeController controller;
        [SerializeField] private Transform traveler;
        [SerializeField, Range(1f, 32f)] private float boundaryHysteresisMeters = 10f;
        private CityForgeCoord center;
        private bool hasCenter;

        public bool IsFollowing { get { return hasCenter; } }
        public CityForgeCoord StreamCenter { get { return center; } }
        public event Action<CityForgeCoord> StreamCenterChanged;

        public void Configure(SlopCityCityForgeController cityController, Transform player)
        {
            controller = cityController;
            traveler = player;
            hasCenter = false;
        }

        private void Awake()
        {
            if (!controller) controller = GetComponent<SlopCityCityForgeController>();
        }

        private void Update()
        {
            if (controller == null || !controller.IsInitialized || traveler == null) return;
            CityForgeCoord playerCoordinate = CoordinateFor(traveler.position);
            // The authored Old City is not a mutable City Forge coordinate. Keep
            // the entry window stable until the player uses the public bridge.
            if (!SlopCityCityForge.IsBuildable(playerCoordinate)) return;

            if (!hasCenter)
            {
                SetCenter(playerCoordinate);
                return;
            }
            if (playerCoordinate.Equals(center) || !IsSafelyInside(playerCoordinate, traveler.position)) return;
            SetCenter(playerCoordinate);
        }

        /// <summary>Lets a public travel bridge immediately establish its known-safe entry center.</summary>
        public void SetKnownCenter(CityForgeCoord coordinate)
        {
            if (!SlopCityCityForge.IsBuildable(coordinate)) return;
            hasCenter = true;
            center = coordinate;
        }

        /// <summary>Returns the normal offline window to a known walking location after top-down editing.</summary>
        public void RestoreAround(CityForgeCoord coordinate)
        {
            if (SlopCityCityForge.IsBuildable(coordinate)) { SetCenter(coordinate); return; }
            // Build Mode can be opened while the player is still inside the
            // immutable Old City. In that case there is no mutable coordinate
            // to stream, so restore the known Sprawl entry/window rather than
            // leaving a distant edited window physically resident.
            RestoreKnownCenter();
        }

        public void RestoreKnownCenter()
        {
            if (hasCenter && SlopCityCityForge.IsBuildable(center)) { SetCenter(center); return; }
            SetCenter(new CityForgeCoord(2, 0));
        }

        private void SetCenter(CityForgeCoord next)
        {
            try { controller.StreamAt(next); }
            catch { return; }
            hasCenter = true;
            center = next;
            StreamCenterChanged?.Invoke(center);
        }

        private bool IsSafelyInside(CityForgeCoord coordinate, Vector3 position)
        {
            // Teleports and large recovery moves should not retain an obsolete
            // window merely because their destination is close to an edge.
            if (Mathf.Abs(coordinate.x - center.x) > 1 || Mathf.Abs(coordinate.z - center.z) > 1) return true;
            float chunk = SlopCityCityForge.ChunkSizeMeters;
            float inset = Mathf.Clamp(boundaryHysteresisMeters, 1f, chunk * .45f);
            if (coordinate.x > center.x && position.x < coordinate.x * chunk + inset) return false;
            if (coordinate.x < center.x && position.x > (coordinate.x + 1) * chunk - inset) return false;
            if (coordinate.z > center.z && position.z < coordinate.z * chunk + inset) return false;
            if (coordinate.z < center.z && position.z > (coordinate.z + 1) * chunk - inset) return false;
            return true;
        }

        private static CityForgeCoord CoordinateFor(Vector3 world)
        {
            return new CityForgeCoord(Mathf.FloorToInt(world.x / SlopCityCityForge.ChunkSizeMeters), Mathf.FloorToInt(world.z / SlopCityCityForge.ChunkSizeMeters));
        }
    }
}
