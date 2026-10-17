using UnityEngine;

namespace SlopCity
{
    /// <summary>A named, walkable block in the first nine blocks of the city.</summary>
    public sealed class SlopCityDistrict : MonoBehaviour
    {
        [SerializeField] private int column;
        [SerializeField] private int row;
        [SerializeField] private string displayName;
        [SerializeField] private Vector3 visitPoint;

        public int Column => column;
        public int Row => row;
        public int Index => row * 3 + column;
        public string DisplayName => displayName;
        public Vector3 VisitPoint => visitPoint;
        public Vector3 Center => transform.position;
        public bool ContainsStreetPoint(Vector3 point)
        {
            Vector3 offset = point - Center;
            return Mathf.Abs(offset.x) <= SlopCityDenseLayout.PavementHalfWidth
                && offset.z >= SlopCityDenseLayout.PavementSouth && offset.z <= SlopCityDenseLayout.PavementNorth;
        }

        public void Configure(int column, int row, string label, Vector3 visitPoint)
        {
            this.column = column;
            this.row = row;
            displayName = label;
            this.visitPoint = visitPoint;
        }
    }
}
