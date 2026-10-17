using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>Metric infill plots around the existing human-sized home and neighbourhood venues.</summary>
    public static class SlopCityDenseLayout
    {
        public const float ColumnPitch = 69.7f;
        public const float RowPitch = 47f;
        // Keep the full-size buildings at their established frontage datum. Road
        // width comes from the distance between plots, never shrinking a building.
        public const float SideBuildToLine = 29.5f;
        public const float FrontageSidewalkWidth = .85f;
        public const float SouthSidewalkWidth = .9f;
        public const float PavementHalfWidth = SideBuildToLine + FrontageSidewalkWidth;
        public const float SouthBuildToLine = -12.2f;
        public const float NorthBuildToLine = 24.4f;
        public const float PavementSouth = SouthBuildToLine - SouthSidewalkWidth;
        public const float PavementNorth = NorthBuildToLine + SouthSidewalkWidth;
        public const float OrdinaryLeftTowerWidth = 22.5f;
        public const float OrdinaryRightTowerWidth = 21f;
        public const float HomeTowerWidth = 16.5f;
        public const float HomeAlleyWall = SideBuildToLine - HomeTowerWidth;
        public const float HomeFeaturedMount = HomeAlleyWall - .9f;
        public const float SharedLaneWidth = ColumnPitch - PavementHalfWidth * 2;
        public const float CrossStreetWidth = RowPitch - (PavementNorth - PavementSouth);
        public const float SkylineExpansionX = ColumnPitch - 63f;
        public const float SkylineExpansionZ = RowPitch - 43.2f;
        public const float PedestrianGap = 1.5f;
        public const float TravelGap = 3f;
        public static readonly Rect CoreGroundRect = Rect.MinMaxRect(-ColumnPitch - PavementHalfWidth,
            -RowPitch + PavementSouth - 3, ColumnPitch + PavementHalfWidth, RowPitch + PavementNorth + 3);
        public static readonly Rect OuterGroundRect = Rect.MinMaxRect(-141 - SkylineExpansionX,
            -102 - SkylineExpansionZ, 132 + SkylineExpansionX, 138 + SkylineExpansionZ);
        public static Bounds GroundBounds => new Bounds(new Vector3(OuterGroundRect.center.x, 0, OuterGroundRect.center.y),
            new Vector3(OuterGroundRect.width, 2, OuterGroundRect.height));
        public static Vector3 CitadelPoint => new Vector3(-109.75f - SkylineExpansionX, 0, 112 + SkylineExpansionZ);

        // These are the unchanged authored full-size skyline footprints. Only the vacant
        // margins between their slots are quartered, subject to a real pedestrian passage.
        public static Vector3 SkylinePoint(int ordinal)
        {
            int side = ordinal / 7, index = ordinal % 7;
            float slot = SkylineSlot(index, side == 0 || side == 2);
            return side == 0 ? new Vector3(slot, 0, 89 + SkylineExpansionZ)
                : side == 1 ? new Vector3(109.75f + SkylineExpansionX, 0, 6 - slot)
                : side == 2 ? new Vector3(-slot, 0, -77 - SkylineExpansionZ)
                : new Vector3(-109.75f - SkylineExpansionX, 0, 6 + slot);
        }
        private static float SkylineSlot(int index, bool width)
        {
            float[] sizes = width ? new[] { 18f, 24f, 30f, 18f, 24f, 30f, 18f }
                : new[] { 19f, 29f, 19f, 29f, 19f, 29f, 19f };
            float originalPitch = width ? 32 : 30;
            float total = 0, distance = 0;
            for (int i = 0; i < 6; i++)
            {
                float occupied = (sizes[i] + sizes[i + 1]) * .5f;
                // The column neighbours present door brows and armored rear frames into
                // the same passage. Their 2.5m core gap leaves real capsule clearance;
                // parallel row fronts need only the ordinary 1.5m pedestrian alley.
                float clear = width ? PedestrianGap : 2.5f;
                float step = occupied + Mathf.Max(clear, (originalPitch - occupied) * .25f);
                total += step;
                if (i < index) distance += step;
            }
            return distance - total * .5f;
        }

        /// <summary>Move district origins without stretching their contents.</summary>
        public static Vector3 DistrictPoint(Vector3 source)
            => new Vector3(source.x * ColumnPitch / 40f, source.y, source.z * RowPitch / 40f);

        /// <summary>
        /// Replot the saved street grid: real carriageways between full-size plots.
        /// The original district centres stay authoritative; the north pavement is deeper than the south.
        /// </summary>
        public static Vector3 StreetPoint(Vector3 source)
            => new Vector3(StreetCoordinate(source.x, ColumnPitch, -PavementHalfWidth, PavementHalfWidth),
                source.y, StreetCoordinate(source.z, RowPitch, PavementSouth, PavementNorth));

        private static float StreetCoordinate(float value, float pitch, float low, float high)
        {
            int district = Mathf.Clamp(Mathf.FloorToInt((value + 20f) / 40f), -1, 1);
            float local = value - district * 40f;
            float gapScale = (pitch - (high - low)) / 10f;
            float next = local < -15f ? low + (local + 15f) * gapScale
                : local > 15f ? high + (local - 15f) * gapScale
                : local < 0 ? local * (-low / 15f) : local * (high / 15f);
            return district * pitch + next;
        }

        /// <summary>Occupied infill joins the retained venue; every added metre belongs to usable floors.</summary>
        public static Vector3 TowerSize(int districtIndex, Vector3 sourceLocal, Vector3 sourceSize)
        {
            Vector3 size = SlopCityHumanScale.PlotPoint(sourceSize);
            bool ordinary = districtIndex != 2 && districtIndex != 4 && districtIndex != 5;
            if (ordinary && Mathf.Abs(sourceLocal.x) > .01f)
                size.x = Mathf.Max(size.x, sourceLocal.x < 0 ? OrdinaryLeftTowerWidth : OrdinaryRightTowerWidth);
            return size;
        }

        /// <summary>Use the already widened shell dimensions; never shrink a person, room or tower.</summary>
        public static Vector3 TowerPoint(int districtIndex, Vector3 sourceLocal, Vector3 widenedSize)
        {
            if (Mathf.Abs(sourceLocal.x) < .01f)
            {
                // Signal Yard originally had no right tower. Replot its existing occupied
                // north tower into that missing street wall, keeping its identity/floors.
                if (districtIndex == 6)
                    return new Vector3(SideBuildToLine - widenedSize.x * .5f, sourceLocal.y, SouthBuildToLine + widenedSize.z * .5f);
                return new Vector3(0, sourceLocal.y, NorthBuildToLine - widenedSize.z * .5f);
            }
            bool openVenue = districtIndex == 2 || districtIndex == 5;
            // Align the actual building face with the back of the sidewalk. Occupied
            // left infill meets the venue; right infill leaves its 1.5m service passage.
            // The existing home towers enclose the east-facing loft exit as well as
            // meeting the southern footway. Canal and market retain working courts.
            float depth = openVenue ? 8f : SouthBuildToLine + widenedSize.z * .5f;
            return new Vector3(Mathf.Sign(sourceLocal.x) * (SideBuildToLine - widenedSize.x * .5f), sourceLocal.y, depth);
        }

        /// <summary>Keep the motor court's assemblies intact while clearing the enlarged adjacent facade.</summary>
        public static void ApplyGroundFixtures(Transform district)
        {
            if (!district) return;
            SlopCityDistrict identity = district.GetComponent<SlopCityDistrict>();
            if (!identity) return;
            RelocateTreeAssemblies(district, identity.Index);
            foreach (Transform fixture in district)
            {
                Vector3 point = fixture.localPosition;
                int index = identity.Index;
                bool ordinary = index != 2 && index != 4 && index != 5;
                if (ordinary && fixture.name.StartsWith("Venue_", System.StringComparison.Ordinal))
                    // Move every wall, door, furnishing, branding mount and local use point
                    // together. Authored venues are 9m deep and their front faces south.
                    fixture.localPosition = new Vector3(point.x, point.y, SouthBuildToLine + 4.5f);
                else if (ordinary && (fixture.name.StartsWith("CityEntrance_", System.StringComparison.Ordinal)
                    || fixture.name.StartsWith("CityInterior_", System.StringComparison.Ordinal)))
                    // Authored venue arrival/circulation anchors are district siblings.
                    // Their destinations must follow the whole room's 11.7m translation.
                    fixture.localPosition = point + new Vector3(0, 0, SouthBuildToLine + .5f);
                else if (ordinary && fixture.name == "Front_Promenade")
                {
                    // This old 13m paving strip was the visible forecourt. Its useful
                    // route is now the real sidewalk outside the relocated front door.
                    fixture.localPosition = new Vector3(0, point.y, PavementSouth + SouthSidewalkWidth * .5f);
                    Vector3 scale = fixture.localScale; scale.z *= SouthSidewalkWidth / 13f; fixture.localScale = scale;
                }
                else if (index == 4 && fixture.name == "Home_EastWalk")
                {
                    // End the three-metre walk before the new physical west facade;
                    // the home alley turns south to the existing public footway.
                    Vector3 scale = fixture.localScale;
                    float start = point.x - scale.x * .5f, end = HomeFeaturedMount - .35f;
                    scale.x = end - start;
                    fixture.localPosition = new Vector3((start + end) * .5f, point.y, point.z);
                    fixture.localScale = scale;
                }
                else if (index == 4 && fixture.name == "Street_Lamp" && point.z < 0)
                    // Move complete switch/pole/head/light assemblies. The original
                    // eastern head and western base would intersect occupied shells.
                    fixture.localPosition = new Vector3(point.x < 0 ? -11.45f : 11.45f, point.y,
                        point.x < 0 ? -10.1f : point.z);
                else if (index == 4 && fixture.name == "Litter_Bin")
                    fixture.localPosition = new Vector3(11.65f, point.y, point.z);
                else if (index == 4 && fixture.name.StartsWith("City_Billboard_", System.StringComparison.Ordinal))
                    // Its brace assembly is wider than the picture. Keep all supports,
                    // maintenance hardware and tuning controls outside the new core.
                    fixture.localPosition = new Vector3(8.5f, point.y, point.z);
                else if (fixture.name == "Street_Lamp" && ordinary)
                    fixture.localPosition = new Vector3(point.x < 0 ? -5.8f : 5.8f, point.y, point.z < 0 ? 1f : 9f);
                else if (fixture.name == "Street_Lamp" && (index == 2 || index == 5)
                    && Mathf.Abs(point.x - 13.5f) < .01f && Mathf.Abs(point.z - 5.8f) < .01f)
                    fixture.localPosition = new Vector3(12.7f, point.y, point.z);
                else if (ordinary && fixture.name.StartsWith("City_Billboard_", System.StringComparison.Ordinal))
                    fixture.localPosition = new Vector3(5.3f, point.y, SouthBuildToLine - .45f);
                else if (index == 1 && fixture.name == "MotorCourt_FuelStation")
                    fixture.localPosition = new Vector3(-3.5f, point.y, 4.3f);
                else if (index == 1 && (fixture.name == "Parked_CityCar" || fixture.name == "Parking_Bay" || fixture.name == "Parking_Line"))
                {
                    // Whole original cars/paint assemblies form street parking, rather
                    // than being buried in the newly aligned occupied building cores.
                    int bay = Mathf.Clamp(Mathf.RoundToInt((point.z + (fixture.name == "Parking_Line" ? 2.1f : 0) + 7) / 6.5f), 0, 2);
                    fixture.localPosition = new Vector3(10 + bay * 6f, point.y, fixture.name == "Parking_Line" ? -16.4f : -15.5f);
                }
                else if (index == 0 && (fixture.name == "Listening_Stage" || fixture.name == "Sound_Sculpture"))
                    fixture.localPosition = point + new Vector3(-9, 0, 8.5f);
                else if (index == 3 && (fixture.name == "Record_Sculpture" || fixture.name == "Record_Base"))
                    fixture.localPosition = point + new Vector3(-6, 0, 9f);
                else if (index == 3 && fixture.name == "Parked_CityCar")
                    fixture.localPosition = new Vector3(-24.5f, point.y, -15.5f);
                else if (index == 6 && fixture.name == "Signal_Mast")
                    fixture.localPosition = new Vector3(3.8f, point.y, 6);
                else if (index == 6 && (fixture.name == "Signal_Equipment" || fixture.name == "Signal_LED"))
                {
                    int row = Mathf.Clamp(Mathf.RoundToInt((-point.z - 2) / 2.3f), 0, 2);
                    fixture.localPosition = new Vector3(-3.5f + point.x - 9.5f, point.y, 1 + row * 3f);
                }
                else if (index == 7 && (fixture.name == "Publisher_Monolith" || fixture.name == "Publisher_Signal"
                    || fixture.name == "City_Sign_MP"))
                    fixture.localPosition = point + new Vector3(-5.5f, 0, 9.5f);
                else if (index == 8 && (fixture.name == "Reading_Plinth" || fixture.name.StartsWith("Book_Sculpture_", System.StringComparison.Ordinal)))
                    fixture.localPosition = point + new Vector3(-9.5f, 0, 9.5f);
                else if (fixture.name == "City_Bench" && ordinary)
                    fixture.localPosition = new Vector3(point.x < 0 ? -5.5f : 5.5f, point.y, point.z < -10 ? 10f : -.9f);
                else if (fixture.name == "Litter_Bin" && ordinary)
                    fixture.localPosition = new Vector3(6f, point.y, 7.5f);
                else if (fixture.name == "Motor_SignPole" || fixture.name == "Motor_Sign")
                    fixture.localPosition = point + new Vector3(5.5f, 0, 3.5f);
                else if (ordinary && fixture.name == "District_Wayfinding")
                    fixture.localPosition = new Vector3(point.x, point.y, SouthBuildToLine - .2f);
                else if (ordinary && fixture.name.StartsWith("CityVisitPoint_", System.StringComparison.Ordinal))
                    fixture.localPosition = new Vector3(point.x, point.y, SouthBuildToLine - .9f);
                else if (fixture.name == "Dystopian_Infrastructure")
                    // Includes its poles, wire meshes, working cabinet and windblown
                    // paper; move the complete kerb assembly without stretching it.
                    fixture.localPosition = point + new Vector3(-15.6f, 0, 0);
            }
        }

        private static void RelocateTreeAssemblies(Transform district, int index)
        {
            // Authored trees have four separate direct siblings, rather than a shared root.
            // Snapshot their original world positions before any piece moves, and translate
            // its canopy, planter base and soil together without changing their relative heights.
            var original = new Dictionary<Transform, Vector3>();
            foreach (Transform fixture in district) original.Add(fixture, fixture.position);
            foreach (var tree in original)
            {
                if (tree.Key.name != "Tree_Trunk") continue;
                Vector3 local = district.InverseTransformPoint(tree.Value), target = local;
                bool ordinary = index != 2 && index != 4 && index != 5;
                if (ordinary && Mathf.Abs(local.x) > 8)
                    target = new Vector3(Mathf.Sign(local.x) * 5.5f, local.y, local.z < 0 ? 1f : 7.2f);
                else if (index == 4 && local.z > 7) target.z = 6.5f;
                else continue;
                Vector3 delta = district.TransformPoint(target) - tree.Value;
                foreach (var piece in original)
                {
                    string name = piece.Key.name;
                    if (name != "Tree_Trunk" && name != "Tree_Canopy" && name != "Planter_Base" && name != "Planter_Green") continue;
                    Vector3 separation = piece.Value - tree.Value; separation.y = 0;
                    if (separation.sqrMagnitude < .000001f) piece.Key.position = piece.Value + delta;
                }
            }
        }

        /// <summary>Make the existing full-size transit pier bases solid without resizing their artwork.</summary>
        public static Transform EnsureTransitPierColliders(Transform spine)
        {
            if (!spine) return null;
            float span;
            if (spine.name == "MilitarySF_North_TransitSpine") span = 176f;
            else if (spine.name == "MilitarySF_West_TransitSpine") span = 184f;
            else return null;
            const string rootName = "Dense_Transit_Pier_Colliders";
            Transform root = spine.Find(rootName);
            if (root) return root;
            root = new GameObject(rootName).transform;
            root.SetParent(spine, false);
            float[] fractions = { -.40f, -.14f, .14f, .40f };
            for (int index = 0; index < fractions.Length; index++)
            {
                Transform pier = new GameObject("Transit_Pier_Base_" + index).transform;
                pier.SetParent(root, false);
                pier.localPosition = new Vector3(span * fractions[index], 2.4f, 0);
                BoxCollider solid = pier.gameObject.AddComponent<BoxCollider>();
                solid.size = new Vector3(11f, 4.8f, 11.2f);
            }
            return root;
        }
    }
}
