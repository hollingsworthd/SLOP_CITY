using UnityEngine;

namespace SlopCity
{
    /// <summary>A fixed platform work target; trains physically carry riders independently of USE.</summary>
    public sealed class SlopCityMetroPlatform : MonoBehaviour
    {
        [SerializeField] private SlopCityMetroWorld world;
        [SerializeField] private int stopIndex;
        [SerializeField] public string LegacyRouteId = "";
        public SlopCityMetroWorld World => world;
        public int StopIndex => stopIndex;
        public string Status => world ? world.GetPlatformStatus(stopIndex) : "METRO PREPARING";
        public void Configure(SlopCityMetroWorld owner, int index) { world = owner; stopIndex = index; }
    }
}
