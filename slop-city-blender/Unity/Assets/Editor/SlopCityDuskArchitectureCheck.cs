#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopCity.Editor
{
    /// <summary>Physical street proportions and occupied architectural geometry in the prepared production city.</summary>
    public static class SlopCityDuskArchitectureCheck
    {
        [Serializable]
        public sealed class Receipt
        {
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
            public float northSouthCarriageway, eastWestCarriageway, sideFootway, crossFootway;
            public int supportedRoadSamples, supportedFootwaySamples, architecturalBuildings, occupiedStories;
        }

        public static Receipt Inspect(Transform city)
        {
            var result = new Receipt();
            void Check(bool condition, string text)
            { result.checks.Add(text); if (!condition) result.errors.Add(text); }
            if (!city) { Check(false, "Prepared production city exists."); return result; }
            Physics.SyncTransforms();
            SlopCityDistrict[] districts = city.GetComponentsInChildren<SlopCityDistrict>(true);
            Check(districts.Length == 9, "All nine actual occupied districts remain present.");
            foreach (SlopCityDistrict district in districts)
            {
                SlopCityDistrict east = districts.FirstOrDefault(item => item.Column == district.Column + 1 && item.Row == district.Row);
                SlopCityDistrict north = districts.FirstOrDefault(item => item.Row == district.Row + 1 && item.Column == district.Column);
                if (east)
                {
                    float road = east.Center.x - district.Center.x - 2 * SlopCityDenseLayout.PavementHalfWidth;
                    result.northSouthCarriageway = road;
                    Check(Mathf.Abs(road - 9f) < .01f, district.name + " to " + east.name + " has an actual 9m north/south carriageway.");
                    Vector3 point = district.Center + new Vector3(SlopCityDenseLayout.PavementHalfWidth + road * .5f, 0, 6);
                    foreach (float offset in new[] { -road * .4f, 0, road * .4f })
                    {
                        bool supported = Supported(point + Vector3.right * offset);
                        if (supported) result.supportedRoadSamples++;
                        Check(supported, district.name + " north/south roadway remains physically supported at offset " + offset.ToString("F2") + "m.");
                    }
                }
                if (north)
                {
                    float road = north.Center.z - district.Center.z - (SlopCityDenseLayout.PavementNorth - SlopCityDenseLayout.PavementSouth);
                    result.eastWestCarriageway = road;
                    Check(Mathf.Abs(road - 8.6f) < .01f, district.name + " to " + north.name + " has an actual 8.6m east/west carriageway.");
                    Vector3 point = district.Center + new Vector3(0, 0, SlopCityDenseLayout.PavementNorth + road * .5f);
                    foreach (float offset in new[] { -road * .4f, 0, road * .4f })
                    {
                        bool supported = Supported(point + Vector3.forward * offset);
                        if (supported) result.supportedRoadSamples++;
                        Check(supported, district.name + " east/west roadway remains physically supported at offset " + offset.ToString("F2") + "m.");
                    }
                }
                Transform pavement = district.transform.Find("Dense_Block_Pavement");
                Check(pavement, district.name + " has its real replacement paving geometry.");
                if (!pavement) continue;
                foreach (Transform piece in pavement)
                {
                    if (piece.name != "Neighbourhood_Sidewalk") continue;
                    bool side = Mathf.Abs(piece.localPosition.x) > SlopCityDenseLayout.SideBuildToLine;
                    float width = side ? piece.localScale.x : piece.localScale.z;
                    if (side) result.sideFootway = width; else result.crossFootway = width;
                    Check(Mathf.Abs(width - (side ? .85f : .9f)) < .01f,
                        district.name + " rendered " + (side ? "side" : "cross") + " footway is " + width.ToString("F2") + "m wide.");
                    Vector3 sample = piece.position;
                    sample.y = district.Center.y;
                    bool supported = Supported(sample);
                    if (supported) result.supportedFootwaySamples++;
                    Check(supported, district.name + " thin footway retains a real supporting ground collider.");
                }
            }
            foreach (SlopCityMetricFacade facade in city.GetComponentsInChildren<SlopCityMetricFacade>(true))
            {
                SlopCityBuildingInterior interior = facade.GetComponent<SlopCityBuildingInterior>();
                Check(facade.Prepared && interior && interior.IsTower && interior.OccupiedFloorCount == facade.OccupiedStories,
                    facade.name + " has a usable interior for every visible occupied facade story.");
                if (!interior) continue;
                result.occupiedStories += interior.OccupiedFloorCount;
                Check(interior.LocalFloorElevations.Length == facade.OccupiedStories + 1 && interior.FloorCount == facade.OccupiedStories + 1,
                    facade.name + " retains all occupied elevator destinations plus its real roof landing.");
                Mesh frames = facade.ArchitecturalFrameSource;
                Transform core = facade.transform.Find("Structural_Core");
                Mesh lowerMesh = core ? core.GetComponent<MeshFilter>()?.sharedMesh : null;
                Transform upper = facade.transform.Find("Setback_UpperShaft");
                Mesh upperMesh = upper ? upper.GetComponent<MeshFilter>()?.sharedMesh : null;
                Check(frames && frames.isReadable && lowerMesh, facade.name + " retains inspectable production architectural frame geometry.");
                if (!frames || !frames.isReadable || !lowerMesh) continue;
                Vector3 lowerSize = lowerMesh.bounds.size, upperSize = upperMesh ? upperMesh.bounds.size : Vector3.zero;
                float height = lowerSize.y + upperSize.y;
                bool crown = false, deepRib = false;
                float maximumProjection = 0;
                foreach (Vector3 vertex in frames.vertices)
                {
                    if (vertex.y > height + .4f) crown = true;
                    // Lower-shaft cornices overhang the setback slab by centimetres;
                    // measure them against their own lower footprint, not the inset shaft.
                    Vector3 size = upperSize.y > 0 && vertex.y >= lowerSize.y + .5f ? upperSize : lowerSize;
                    float projection = Mathf.Max(Mathf.Abs(vertex.x) - size.x * .5f, Mathf.Abs(vertex.z) - size.z * .5f);
                    maximumProjection = Mathf.Max(maximumProjection, projection);
                    if (vertex.y > 3.3f && vertex.y < height - 1f && projection >= .31f) deepRib = true;
                }
                Check(crown && frames.bounds.max.y < height + .8f && frames.bounds.max.y >= height + .58f,
                    facade.name + " has a bounded mechanical parapet silhouette without an extra occupied story.");
                Check(deepRib && maximumProjection <= .40f,
                    facade.name + " has projecting architectural ribs/ledges above head height, within 40cm of its structural face.");
                Check(facade.transform.Find("Metric_Facade").GetComponentsInChildren<Collider>(true).Length == 0,
                    facade.name + " decorative architecture introduces no doorway, footway or roof collision hazards.");
                result.architecturalBuildings++;
            }
            Check(result.architecturalBuildings >= 51, "Metric architectural detailing covers the full original core and outer skyline tower population.");
            return result;
        }

        private static bool Supported(Vector3 point)
        {
            RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * .45f, Vector3.down, .85f, ~0, QueryTriggerInteraction.Ignore);
            return hits.Any(hit => hit.normal.y > .85f && hit.point.y >= point.y - .2f && hit.point.y <= point.y + .10f);
        }
    }
}
#endif
