#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity.Editor
{
    /// <summary>Deterministic, native geometry for nine connected blocks and six walk-in venues.</summary>
    public static class SlopCityCityBuilder
    {
        public const float BlockPitch = 40f;
        public const float CityExtent = 61f;
        public const string MaterialsFolder = "Assets/Materials/SlopCityCity";
        public const string WorldTextMaterialPath = MaterialsFolder + "/WorldText.mat";
        private static Material worldTextMaterial;
        private static readonly string[] Names =
        {
            "Vibe Lab", "Motor Court", "Canal Gardens",
            "Hood Rich Row", "Home Block", "Market Quarter",
            "Signal Yard", "Machine Publishers", "Blue Book Plaza"
        };

        public static SlopCityDistrict[] Build(Transform lounge, Bounds loungeFloor)
        {
            HideOldBackdrop(lounge);
            SlopCityMetropolisBuilder.BeginBuild();
            var palette = new Palette();
            Transform city = NewRoot("SlopCity_NineBlocks", null, Vector3.zero);
            Box(city, "City_Asphalt", new Vector3(0, -.25f, 0), new Vector3(122, .30f, 122), palette.Asphalt);
            CreateRoads(city, palette);
            CreateBoundary(city, palette);
            var districts = new SlopCityDistrict[9];
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    int index = row * 3 + column;
                    Transform block = NewRoot("District_" + index + "_" + Slug(Names[index]), city,
                        new Vector3((column - 1) * BlockPitch, 0, (row - 1) * BlockPitch));
                    if (index == 4) HomeSidewalk(block, loungeFloor, palette);
                    else Box(block, "Sidewalk", new Vector3(0, -.04f, 0), new Vector3(30, .12f, 30), palette.Concrete);
                    BlockEdges(block, palette);
                    Vector3 visit = block.position + new Vector3(0, .08f, -12);
                    Marker(block, "CityVisitPoint_" + index, visit);
                    SlopCityDistrict district = block.gameObject.AddComponent<SlopCityDistrict>();
                    district.Configure(column, row, Names[index], visit);
                    districts[index] = district;
                    DistrictSign(block, Names[index], index + 1, palette);
                    // Keep the direct walk from the southern sidewalk to every venue clear.
                    if (index != 4)
                        Box(block, "Front_Promenade", new Vector3(0, .025f, -8), new Vector3(4, .01f, 13), palette.Paving, false);
                    Marker(city, "CityPath_SouthApproach_" + index, block.position + new Vector3(0, -.08f, -20));
                    switch (index)
                    {
                        case 0: VibeLab(block, palette); break;
                        case 1: MotorCourt(block, palette); break;
                        case 2: CanalGardens(block, palette); break;
                        case 3: HoodRich(block, palette); break;
                        case 4: HomeBlock(block, loungeFloor, palette); break;
                        case 5: Market(block, palette); break;
                        case 6: SignalYard(block, palette); break;
                        case 7: Publishers(block, palette); break;
                        case 8: BlueBook(block, palette); break;
                    }
                    StreetLamp(block, new Vector3(-13, 0, -12), palette);
                    StreetLamp(block, new Vector3(13, 0, 12), palette);
                    Bench(block, new Vector3(9, .02f, -12), 0, palette);
                    Bin(block, new Vector3(12.5f, .02f, -10), palette);
                }
            }
            Marker(city, "CityPath_HomeGate", new Vector3(8, .08f, 0));
            Marker(city, "CityPath_HomeSidewalk", new Vector3(14, .08f, 0));
            Marker(city, "CityPath_HomeStreet", new Vector3(20, -.08f, 0));
            SlopCityMetropolisBuilder.Build(city, districts, loungeFloor);
            // Infill construction removes trees that intersect a tower. Keep one street tree
            // in each district on its clear southern sidewalk instead of losing its planting.
            foreach (SlopCityDistrict district in districts)
                if (!Array.Exists(district.GetComponentsInChildren<Transform>(), item => item.name == "Tree_Trunk"))
                    Tree(district.transform, new Vector3(-11, .02f, -11), palette);
            return districts;
        }

        private static void HideOldBackdrop(Transform lounge)
        {
            foreach (Transform child in lounge.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name;
                if (name.StartsWith("City_", StringComparison.Ordinal) || name.StartsWith("Roof_Unit", StringComparison.Ordinal)
                    || name.StartsWith("Transmission_Mast", StringComparison.Ordinal) || name.StartsWith("Antenna_", StringComparison.Ordinal))
                    child.gameObject.SetActive(false);
            }
        }

        private static void CreateRoads(Transform city, Palette p)
        {
            Transform roads = NewRoot("City_StreetNetwork", city, Vector3.zero);
            foreach (float lane in new[] { -20f, 20f })
            {
                for (int at = -58; at <= 58; at += 4)
                {
                    if (Mathf.Abs(Mathf.Abs(at) - 20) < 8) continue;
                    Box(roads, "Lane_Dash_NS", new Vector3(lane, -.093f, at), new Vector3(.09f, .008f, 1.6f), p.RoadPaint, false);
                    Box(roads, "Lane_Dash_EW", new Vector3(at, -.093f, lane), new Vector3(1.6f, .008f, .09f), p.RoadPaint, false);
                }
            }
            foreach (float x in new[] { -20f, 20f })
                foreach (float z in new[] { -20f, 20f })
                {
                    Marker(city, "CityPath_Intersection_" + x + "_" + z, new Vector3(x, -.08f, z));
                    for (int stripe = -4; stripe <= 4; stripe++)
                        foreach (float side in new[] { -6.4f, 6.4f })
                        {
                            Box(roads, "Crosswalk_EW", new Vector3(x + stripe, -.089f, z + side), new Vector3(.48f, .014f, 1.8f), p.RoadPaint, false);
                            Box(roads, "Crosswalk_NS", new Vector3(x + side, -.089f, z + stripe), new Vector3(1.8f, .014f, .48f), p.RoadPaint, false);
                        }
                }
            foreach (float lane in new[] { -57f, 57f })
            {
                Box(roads, "OuterStreet_Line_NS", new Vector3(lane, -.092f, 0), new Vector3(.07f, .01f, 112), p.RoadPaint, false);
                Box(roads, "OuterStreet_Line_EW", new Vector3(0, -.092f, lane), new Vector3(112, .01f, .07f), p.RoadPaint, false);
            }
            SlopCityStreetMeshes.Combine(roads, "Street_RoadMarkings", p.RoadPaint);
            Material repair=SlopCityStreetMeshes.Material("Asphalt_Repair",new Color(.13f,.13f,.12f),.02f,.19f);
            for(int patch=0;patch<18;patch++)
            {
                GameObject area=SlopCityStreetMeshes.Shape(roads,"Street_ResurfacedPatch",SlopCityStreetMeshes.AsphaltPatch(patch%4),repair);
                area.transform.localPosition=new Vector3((patch%2==0?-20:20)+(patch%3==0?-2.1f:2.1f),-.096f,-51+(patch/2)*12.5f);
                area.transform.localRotation=Quaternion.Euler(0,patch*29%60,0);
            }
            SlopCityStreetMeshes.Combine(roads,"Street_AsphaltRepairs",repair);
        }

        private static void CreateBoundary(Transform city, Palette p)
        {
            Transform edge = NewRoot("City_ScenicBoundary", city, Vector3.zero);
            for (int side = 0; side < 4; side++)
            {
                Transform wall = NewRoot("Boundary_" + side, edge, Vector3.zero);
                wall.localRotation = Quaternion.Euler(0, side * 90, 0);
                Box(wall, "Retaining_Wall", new Vector3(0, 1, 61), new Vector3(122, 2.2f, .4f), p.DarkTeal);
                Box(wall, "Boundary_LightStrip", new Vector3(0, 2.14f, 60.74f), new Vector3(122, .035f, .06f), p.Metal, false);
                for (int post = -60; post <= 60; post += 10)
                    Box(wall, "Boundary_Post", new Vector3(post, 1.35f, 60.8f), new Vector3(.34f, 2.7f, .65f), p.Charcoal);
                Sign(wall, "SLOP CITY / THE CITY NEVER SLEEPS", new Vector3(0, 1.3f, 60.72f), Vector3.back, .25f, p.Gold);
            }
        }

        private static void BlockEdges(Transform block, Palette p)
        {
            // Low curbs can be stepped over everywhere; nothing fences off a block.
            foreach(int side in new[]{0,1,2,3})
            {
                GameObject curb=SlopCityStreetMeshes.Shape(block,"Curb_"+new[]{"W","E","N","S"}[side],SlopCityStreetMeshes.Curb(),p.Paving);
                curb.transform.localPosition=side<2?new Vector3(side==0?-14.92f:14.92f,.025f,0):new Vector3(0,.025f,side==2?14.92f:-14.92f);
                if(side>=2)curb.transform.localRotation=Quaternion.Euler(0,90,0);
                curb.AddComponent<BoxCollider>().size=new Vector3(.16f,.07f,30);
            }
            // Expansion joints make the paving readable without adding collision seams.
            for (int line = -12; line <= 12; line += 3)
            {
                Box(block, "Sidewalk_Joint", new Vector3(line, .022f, -13.8f), new Vector3(.025f, .003f, 1.8f), p.DarkConcrete, false);
                Box(block, "Sidewalk_Joint", new Vector3(13.8f, .022f, line), new Vector3(1.8f, .003f, .025f), p.DarkConcrete, false);
            }
        }

        private static void DistrictSign(Transform block, string name, int number, Palette p)
        {
            Transform sign = NewRoot("District_Wayfinding", block, new Vector3(-8.5f, .02f, -12.3f));
            Box(sign, "Wayfinding_Pylon", new Vector3(0, 1.35f, 0), new Vector3(4.2f, 2.7f, .23f), p.Charcoal);
            Box(sign, "Wayfinding_Accent", new Vector3(-1.9f, 1.35f, -.13f), new Vector3(.07f, 2.4f, .025f), p.Cyan, false);
            Sign(sign, "0" + number + "  /  SLOP CITY", new Vector3(.05f, 2.06f, -.135f), Vector3.back, .18f, p.Gold);
            Sign(sign, name.ToUpperInvariant().Replace(" ", "\n"), new Vector3(.12f, 1.3f, -.135f), Vector3.back, .31f, p.Cream);
            Sign(sign, "WALK IN. TUNE IN.", new Vector3(.05f, .42f, -.135f), Vector3.back, .11f, p.Cyan);
        }

        private static void HomeBlock(Transform b, Bounds loungeFloor, Palette p)
        {
            // All exterior dressing remains outside the original room footprint.
            Box(b, "Home_EastWalk", new Vector3((loungeFloor.max.x + 15) / 2, .028f, 0),
                new Vector3(15 - loungeFloor.max.x, .015f, 3), p.Paving, false);
            Box(b, "Home_SouthWalk", new Vector3(0, .027f, -9), new Vector3(4, .014f, 9), p.Paving, false);
            Tree(b, new Vector3(-10, .02f, -8), p);
            Tree(b, new Vector3(-10, .02f, 9), p);
            Tree(b, new Vector3(10, .02f, 9), p);
            Bench(b, new Vector3(-9, .02f, -3), 90, p);
            Planter(b, new Vector3(9, .02f, -5.6f), new Vector2(4, 1.8f), p);
            Planter(b, new Vector3(-8, .02f, 5.5f), new Vector2(4, 1.8f), p);
            StreetLamp(b, new Vector3(12.4f, 0, -3.5f), p);
            Box(b, "Home_EntryPlinth", new Vector3(9, .44f, 3), new Vector3(3.8f, .84f, .35f), p.DarkTeal);
            Sign(b, "DANIEL / HOME BASE", new Vector3(9, .53f, 2.8f), Vector3.back, .20f, p.Gold);
        }

        private static void HomeSidewalk(Transform block, Bounds loungeFloor, Palette p)
        {
            // The authored lounge floor is slightly lower than the street paving.
            // Cut the paving around its footprint to preserve every floorboard.
            float west = loungeFloor.min.x - .02f, east = loungeFloor.max.x + .02f;
            float south = loungeFloor.min.z - .02f, north = loungeFloor.max.z + .02f;
            Box(block, "Home_Sidewalk_S", new Vector3(0, -.04f, (-15 + south) / 2), new Vector3(30, .12f, south + 15), p.Concrete);
            Box(block, "Home_Sidewalk_N", new Vector3(0, -.04f, (15 + north) / 2), new Vector3(30, .12f, 15 - north), p.Concrete);
            Box(block, "Home_Sidewalk_W", new Vector3((-15 + west) / 2, -.04f, (south + north) / 2),
                new Vector3(west + 15, .12f, north - south), p.Concrete);
            Box(block, "Home_Sidewalk_E", new Vector3((15 + east) / 2, -.04f, (south + north) / 2),
                new Vector3(15 - east, .12f, north - south), p.Concrete);
        }

        private static void SignalYard(Transform b, Palette p)
        {
            Storefront(b, "SignalYard", "SIGNAL YARD", "KEEP THE CITY CONNECTED", p.Teal, p.Cyan, p);
            Tower(b, new Vector3(-10, 0, 9), new Vector3(5, 13, 6), p.DarkTeal, p);
            Transform mast = NewRoot("Signal_Mast", b, new Vector3(10, .02f, 7));
            Box(mast, "Mast_Base", new Vector3(0, .55f, 0), new Vector3(3, 1.1f, 3), p.DarkConcrete);
            Cylinder(mast, "Aerial_Shaft", new Vector3(0, 7, 0), .14f, 13, p.Metal);
            for (int tier = 0; tier < 4; tier++)
            {
                Box(mast, "Aerial_Crossarm", new Vector3(0, 7 + tier * 1.7f, 0), new Vector3(4 - tier * .7f, .10f, .10f), p.Metal, false);
                Box(mast, "Aerial_Signal", new Vector3(0, 7 + tier * 1.7f, 0), new Vector3(.15f, .3f, .18f), p.Amber, false);
            }
            for (int i = 0; i < 3; i++)
            {
                Box(b, "Signal_Equipment", new Vector3(9.5f, .8f, -2 - i * 2.3f), new Vector3(2, 1.55f, 1.2f), p.Charcoal);
                for (int light = 0; light < 4; light++)
                    Box(b, "Signal_LED", new Vector3(8.48f, .6f + light * .2f, -2 - i * 2.3f), new Vector3(.025f, .035f, .45f), p.Cyan, false);
            }
            Tree(b, new Vector3(-11, .02f, -6), p);
        }

        private static void Publishers(Transform b, Palette p)
        {
            Storefront(b, "MachinePublishers", "MACHINE PUBLISHERS", "FROM THEIR MOUTH. TO YOURS.", p.DarkTeal, p.Lime, p);
            Tower(b, new Vector3(-9.5f, 0, 10), new Vector3(7, 17, 7), p.Teal, p);
            Tower(b, new Vector3(9.5f, 0, 10), new Vector3(6, 12, 7), p.DarkTeal, p);
            Box(b, "Publisher_Monolith", new Vector3(9.5f, 2, -5.5f), new Vector3(3, 4, .55f), p.Charcoal);
            Sign(b, "MP\nLIVE", new Vector3(9.5f, 2.2f, -5.8f), Vector3.back, .7f, p.Lime);
            Box(b, "Publisher_Signal", new Vector3(9.5f, 4.08f, -5.5f), new Vector3(3.2f, .06f, .7f), p.Lime, false);
            Tree(b, new Vector3(-11, .02f, -6), p);
            Bench(b, new Vector3(7.5f, .02f, -9), 0, p);
        }

        private static void BlueBook(Transform b, Palette p)
        {
            Storefront(b, "BlueBook", "BLUE BOOK", "THE READING ROOM / OPEN LATE", p.Blue, p.Cyan, p);
            Tower(b, new Vector3(10, 0, 10), new Vector3(6, 10, 6), p.Blue, p);
            Tree(b, new Vector3(-11, .02f, 8), p);
            Tree(b, new Vector3(-11, .02f, -4), p);
            Box(b, "Reading_Plinth", new Vector3(9.5f, .22f, -5), new Vector3(5, .4f, 5), p.DarkConcrete);
            for (int page = 0; page < 3; page++)
            {
                Transform sculpture = NewRoot("Book_Sculpture_" + page, b, new Vector3(8.5f + page, 1.7f, -5));
                sculpture.localRotation = Quaternion.Euler(0, 12 - page * 12, -18 + page * 18);
                Box(sculpture, "Book_Cover", Vector3.zero, new Vector3(.28f, 2.4f, 1.7f), page == 1 ? p.Gold : p.Blue);
                Box(sculpture, "Book_Pages", new Vector3(.17f, 0, 0), new Vector3(.08f, 2.1f, 1.5f), p.Cream, false);
            }
            Bench(b, new Vector3(7.5f, .02f, -9), 0, p);
        }

        private static void HoodRich(Transform b, Palette p)
        {
            Storefront(b, "HoodRich", "HOOD RICH RECORDS", "ALL SLOP. ALL THE TIME.", p.Brick, p.Amber, p);
            Tower(b, new Vector3(-10, 0, 10), new Vector3(6, 9, 7), p.Brick, p);
            Tower(b, new Vector3(10, 0, 10), new Vector3(6, 12, 6), p.Teal, p);
            Transform disc = NewRoot("Record_Sculpture", b, new Vector3(10, 1.65f, -5));
            GameObject vinyl = Cylinder(disc, "Vinyl", Vector3.zero, 1.3f, .18f, p.Charcoal);
            vinyl.transform.localRotation = Quaternion.Euler(90, 0, 0);
            GameObject label = Cylinder(disc, "Label", new Vector3(0, 0, -.11f), .43f, .025f, p.Amber, false);
            label.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Box(b, "Record_Base", new Vector3(10, .33f, -5), new Vector3(2.7f, .65f, 1.1f), p.DarkConcrete);
            Tree(b, new Vector3(-11, .02f, -6), p);
            Car(b, new Vector3(-10, .02f, 1), 0, p.Brick, p);
        }

        private static void VibeLab(Transform b, Palette p)
        {
            Storefront(b, "VibeLab", "VIBE LAB", "SMALL HOURS / BIG IDEAS", p.DarkTeal, p.Pink, p);
            Tower(b, new Vector3(-10, 0, 10), new Vector3(6, 11, 6), p.Blue, p);
            Tree(b, new Vector3(10, .02f, 10), p);
            Box(b, "Listening_Stage", new Vector3(9, .24f, -4), new Vector3(6, .44f, 5), p.DarkConcrete);
            for (int i = 0; i < 5; i++)
                Box(b, "Sound_Sculpture", new Vector3(7 + i, 1 + .35f * (i % 3), -2.6f),
                    new Vector3(.35f, 1.8f + .7f * (i % 3), .35f), i % 2 == 0 ? p.Pink : p.Cyan);
            Bench(b, new Vector3(9, .02f, -8), 0, p);
            Tree(b, new Vector3(-11, .02f, -6), p);
        }

        private static void MotorCourt(Transform b, Palette p)
        {
            Storefront(b, "MotorCourt", "MOTOR COURT", "NIGHT DESK / REST AND REPAIR", p.Brick, p.Gold, p);
            for (int bay = 0; bay < 3; bay++)
            {
                float z = -7 + bay * 6.5f;
                Box(b, "Parking_Bay", new Vector3(10.5f, .027f, z), new Vector3(5.5f, .012f, 4.5f), p.Asphalt, false);
                Box(b, "Parking_Line", new Vector3(10.5f, .037f, z - 2.1f), new Vector3(5.5f, .008f, .08f), p.RoadPaint, false);
                Car(b, new Vector3(10.5f, .02f, z), 90, bay == 1 ? p.Teal : p.Gold, p);
            }
            Tower(b, new Vector3(-10, 0, 9), new Vector3(6, 7, 7), p.Brick, p);
            Box(b, "Motor_SignPole", new Vector3(-10, 3.5f, -4), new Vector3(.2f, 7, .2f), p.Metal);
            Box(b, "Motor_Sign", new Vector3(-10, 5.5f, -4), new Vector3(4, 2.6f, .25f), p.Charcoal);
            Sign(b, "VACANCY\n24 HOURS", new Vector3(-10, 5.5f, -4.15f), Vector3.back, .36f, p.Amber);
        }

        private static void Market(Transform b, Palette p)
        {
            Box(b, "Market_Paving", new Vector3(0, .027f, 3), new Vector3(25, .014f, 19), p.Paving, false);
            for (int row = 0; row < 3; row++)
                foreach (int side in new[] { -1, 1 })
                    MarketStall(b, new Vector3(side * 7, .02f, -5 + row * 7), side == 1 ? 90 : -90,
                        row == 0 ? "NIGHT BITES" : row == 1 ? "FOUND OBJECTS" : "CITY FLOWERS", row == 1 ? p.Teal : p.Brick, p);
            Tree(b, new Vector3(-11, .02f, 12), p);
            Tree(b, new Vector3(11, .02f, 12), p);
            for (int z = -4; z <= 10; z += 7)
            {
                Box(b, "Market_String", new Vector3(0, 4, z), new Vector3(20, .035f, .035f), p.Metal, false);
                for (int bulb = -8; bulb <= 8; bulb += 2)
                    Sphere(b, "Market_Bulb", new Vector3(bulb, 3.88f, z), new Vector3(.17f, .23f, .17f), p.Amber, false);
            }
            Sign(b, "THE NIGHT MARKET", new Vector3(0, 3.35f, -7), Vector3.back, .4f, p.Gold);
        }

        private static void CanalGardens(Transform b, Palette p)
        {
            Box(b, "Garden_Lawn", new Vector3(0, .025f, 3), new Vector3(26, .01f, 20), p.Moss, false);
            // The water is a shallow ornamental channel with a floor, not a fall hazard.
            Box(b, "Canal_Basin", new Vector3(0, .14f, 6), new Vector3(23, .24f, 3.8f), p.DarkConcrete);
            Box(b, "Canal_Water", new Vector3(0, .27f, 6), new Vector3(22.5f, .025f, 3.3f), p.Water, false);
            Box(b, "Garden_Bridge", new Vector3(0, .30f, 6), new Vector3(3.4f, .16f, 5.4f), p.Wood);
            Box(b, "Garden_BridgeStep_S", new Vector3(0, .12f, 3.1f), new Vector3(3.4f, .20f, .7f), p.Wood);
            Box(b, "Garden_BridgeStep_N", new Vector3(0, .12f, 8.9f), new Vector3(3.4f, .20f, .7f), p.Wood);
            foreach (float x in new[] { -10f, -6f, 6f, 10f })
            {
                Tree(b, new Vector3(x, .02f, 11), p);
                Planter(b, new Vector3(x, .02f, -.5f), new Vector2(2.4f, 2), p);
            }
            Bench(b, new Vector3(-7, .02f, -5), 0, p);
            Bench(b, new Vector3(7, .02f, -5), 0, p);
            // Leave 2.55 m beneath the overhead panel, including capsule step-up clearance.
            Sign(b, "SLOW SIGNAL / CANAL GARDENS", new Vector3(0, 3.15f, 12), Vector3.back, .25f, p.Gold);
            Box(b, "Garden_SignFrame", new Vector3(0, 3.10f, 12.1f), new Vector3(7, 1.1f, .2f), p.DarkTeal);
        }

        private static void Storefront(Transform block, string slug, string title, string subtitle, Material facade, Material accent, Palette p)
        {
            Transform room = NewRoot("Venue_" + slug, block, new Vector3(0, .02f, 4));
            room.localRotation = Quaternion.Euler(0, 180, 0); // Local +Z points out of the front door.
            const float width = 14, depth = 9, height = 3.5f, aperture = 1.8f;
            Box(room, "Interior_Floor", new Vector3(0, .025f, 0), new Vector3(width, .05f, depth), p.Wood);
            Box(room, "Rear_Wall", new Vector3(0, height / 2, -depth / 2), new Vector3(width, height, .22f), facade);
            Box(room, "West_Wall", new Vector3(-width / 2, height / 2, 0), new Vector3(.22f, height, depth), facade);
            Box(room, "East_Wall", new Vector3(width / 2, height / 2, 0), new Vector3(.22f, height, depth), facade);
            float sectionWidth = (width - aperture) / 2;
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * (width + aperture) / 4;
                // A real glazed aperture reveals the furnished room instead of a window painted onto a wall.
                Box(room,"Front_Wall_Sill",new Vector3(x,.405f,depth/2),new Vector3(sectionWidth,.81f,.26f),facade);
                Box(room,"Front_Wall_Head",new Vector3(x,2.995f,depth/2),new Vector3(sectionWidth,1.01f,.26f),facade);
                float pier=(sectionWidth-4.4f)/2;
                foreach(float edge in new[]{-1f,1f})
                {
                    Box(room,"Front_Wall_Pier",new Vector3(x+edge*(2.2f+pier/2),1.65f,depth/2),new Vector3(pier,1.68f,.26f),facade);
                    Box(room,"Shop_WindowJamb",new Vector3(x+edge*2.23f,1.65f,depth/2+.09f),new Vector3(.075f,1.77f,.12f),p.Metal,false);
                }
                Box(room,"Display_Window",new Vector3(x,1.65f,depth/2+.01f),new Vector3(4.4f,1.65f,.025f),p.ShopGlass);
                Box(room,"Shop_WindowSill",new Vector3(x,.817f,depth/2+.12f),new Vector3(4.62f,.11f,.30f),p.DarkConcrete,false);
                Box(room,"Shop_WindowHeader",new Vector3(x,2.51f,depth/2+.10f),new Vector3(4.62f,.085f,.21f),p.Metal,false);
                foreach(float mullion in new[]{-1.45f,0f,1.45f})
                    Box(room,"Window_Mullion",new Vector3(x+mullion,1.65f,depth/2+.055f),new Vector3(.055f,1.69f,.09f),p.Metal,false);
                Sign(room,side==-1?"OPEN LATE":"COME INSIDE",new Vector3(x,2.10f,depth/2+.075f),Vector3.forward,.12f,p.Cream);
                Box(room,"Window_Kickplate",new Vector3(x,.64f,depth/2+.141f),new Vector3(4.45f,.08f,.015f),p.Metal,false);
            }
            Box(room, "Door_Lintel", new Vector3(0, 3.03f, depth / 2), new Vector3(aperture, .94f, .22f), facade);
            Box(room, "Flat_Roof", new Vector3(0, height + .08f, 0), new Vector3(width + .35f, .16f, depth + .35f), p.DarkConcrete);
            foreach(float side in new[]{-1f,1f})
            {
                Box(room,"Roof_Parapet",new Vector3(side*6.98f,3.72f,0),new Vector3(.16f,.32f,depth+.20f),facade);
                Cylinder(room,"Rainwater_Downpipe",new Vector3(side*6.76f,1.72f,depth/2+.20f),.046f,3.45f,p.Metal,false);
                for(int strap=0;strap<3;strap++)
                    Box(room,"Downpipe_Bracket",new Vector3(side*6.76f,.55f+strap*1.07f,depth/2+.15f),new Vector3(.14f,.034f,.18f),p.Metal,false);
                Box(room,"Facade_BaseCourse",new Vector3(side*(width+aperture)/4,.15f,depth/2+.04f),new Vector3(sectionWidth,.20f,.34f),p.DarkConcrete,false);
            }
            Box(room,"Roof_DripEdge",new Vector3(0,3.60f,depth/2+.20f),new Vector3(width+.42f,.038f,.24f),p.Metal,false);
            foreach(float side in new[]{-1f,1f})
            {
                Box(room,"Shop_RoofPlant",new Vector3(side*5.85f,3.98f,2.85f),new Vector3(1.38f,.62f,.96f),p.Metal,false);
                Box(room,"Vent_Recess",new Vector3(side*5.85f,3.99f,3.342f),new Vector3(1.15f,.43f,.012f),p.Charcoal,false);
                for(int louvre=0;louvre<7;louvre++)
                    Box(room,"Vent_Louvre",new Vector3(side*5.85f,3.81f+louvre*.058f,3.357f),new Vector3(1.14f,.014f,.035f),p.Metal,false);
                Cylinder(room,"Aircon_Drain",new Vector3(side*6.57f,2.95f,depth/2+.16f),.013f,1.23f,p.Metal,false);
            }
            Box(room, "Canopy", new Vector3(0, 2.94f, depth / 2 + .8f), new Vector3(width + .4f, .12f, 1.7f), p.Charcoal);
            Box(room, "Canopy_Neon", new Vector3(0, 2.91f, depth / 2 + 1.67f), new Vector3(width + .4f, .055f, .045f), accent, false);
            Box(room, "Venue_Sign", new Vector3(0, 3.85f, depth / 2), new Vector3(12.6f, 1.0f, .2f), p.Charcoal);
            Sign(room, title, new Vector3(0, 3.97f, depth / 2 + .12f), Vector3.forward, .37f, accent);
            Sign(room, subtitle, new Vector3(0, 3.60f, depth / 2 + .12f), Vector3.forward, .11f, p.Cream);

            Transform door = NewRoot("CityDoor_" + slug, room, new Vector3(0, .05f, depth / 2));
            Transform hinge = NewRoot("Door_Hinge", door, new Vector3(-aperture / 2, 0, 0));
            GameObject leaf = Box(hinge, "Door_Leaf", new Vector3(aperture / 2, 1.25f, 0), new Vector3(aperture - .06f, 2.5f, .12f), p.Teal);
            leaf.isStatic = false;
            GameObject pane = Box(hinge, "Door_Pane", new Vector3(aperture / 2, 1.55f, .07f), new Vector3(1.35f, 1.15f, .035f), p.Glass, false);
            pane.isStatic = false;
            GameObject handle = Box(hinge, "Door_Handle", new Vector3(aperture - .20f, 1.12f, .12f), new Vector3(.065f, .35f, .08f), accent, false);
            handle.isStatic = false;
            SlopCityDoor component = door.gameObject.AddComponent<SlopCityDoor>();
            component.Configure(hinge, leaf.GetComponent<Collider>(), door.TransformPoint(new Vector3(0, 1.2f, .15f)), title, 95f);
            Marker(block, "CityEntrance_" + slug, block.position + new Vector3(0, .08f, -3));
            Marker(block, "CityInterior_" + slug, block.position + new Vector3(0, .08f, 1.5f));

            // Furniture deliberately leaves a 3m-wide entry aisle and rear circulation.
            Box(room, "Service_Counter", new Vector3(-3.7f, .60f, -1.9f), new Vector3(4.1f, 1.1f, 1.05f), p.Teal);
            Box(room, "Counter_Top", new Vector3(-3.7f, 1.18f, -1.9f), new Vector3(4.3f, .10f, 1.25f), p.Wood);
            Box(room, "Counter_Signal", new Vector3(-3.7f, .77f, -1.34f), new Vector3(3.5f, .035f, .035f), accent, false);
            for (int shelf = 0; shelf < 3; shelf++)
            {
                Box(room, "Rear_Shelf", new Vector3(-3.7f, .75f + shelf * .7f, -4.0f), new Vector3(4.3f, .10f, .55f), p.Wood);
                for (int item = 0; item < 7; item++)
                    Box(room, "Shelf_Stock", new Vector3(-5.4f + item * .54f, 1 + shelf * .7f, -4.0f),
                        new Vector3(.34f, .42f + .08f * (item % 2), .30f), item % 3 == 0 ? p.Gold : item % 3 == 1 ? p.Brick : p.Blue, false);
            }
            Bench(room, new Vector3(4.5f, .05f, -1.8f), -90, p);
            Box(room, "Listening_Table", new Vector3(3.5f, .46f, 1.5f), new Vector3(2.5f, .8f, 1.1f), p.Wood);
            Box(room, "Interior_Poster", new Vector3(6.87f, 2, -1), new Vector3(.04f, 1.6f, 2.5f), accent, false);
            Sign(room, "SLOP\nCITY", new Vector3(6.82f, 2, -1), Vector3.left, .48f, p.Charcoal);
            Box(room, "Ceiling_Light", new Vector3(0, 3.4f, 0), new Vector3(3, .035f, .55f), p.Amber, false);
            Light light = NewRoot("Interior_Lamp", room, new Vector3(0, 2.9f, 0)).gameObject.AddComponent<Light>();
            light.type = LightType.Point; light.range = 11; light.intensity = 1.5f;
            light.color = new Color(1, .77f, .49f); light.shadows = LightShadows.None;
        }

        private static void Tower(Transform b, Vector3 basePoint, Vector3 dimensions, Material facade, Palette p)
        {
            SlopCityMetropolisBuilder.BuildTower(b, basePoint,
                new Vector3(dimensions.x, dimensions.y * 3.6f + 12, dimensions.z), facade.color, true);
        }

        private static void MarketStall(Transform b, Vector3 point, float yaw, string title, Material fabric, Palette p)
        {
            Transform stall = NewRoot("Market_Stall", b, point);
            stall.localRotation = Quaternion.Euler(0, yaw, 0);
            foreach (float x in new[] { -2.1f, 2.1f })
                foreach (float z in new[] { -1.1f, 1.1f })
                    Box(stall, "Stall_Post", new Vector3(x, 1.65f, z), new Vector3(.08f, 3.3f, .08f), p.Metal);
            GameObject canopy=SlopCityStreetMeshes.Shape(stall,"Stall_Canopy",SlopCityStreetMeshes.MarketCanopy(),fabric);
            BoxCollider shelter=canopy.AddComponent<BoxCollider>();shelter.center=new Vector3(0,3.10f,0);shelter.size=new Vector3(4.6f,.18f,2.8f);
            Box(stall, "Stall_Counter", new Vector3(0, .54f, .5f), new Vector3(4, 1.05f, .9f), p.Wood);
            Sign(stall, title, new Vector3(0, 2.65f, 1.15f), Vector3.forward, .20f, p.Gold);
            for (int item = 0; item < 7; item++)
                Sphere(stall, "Market_Produce", new Vector3(-1.5f + item * .5f, 1.2f, .5f), new Vector3(.32f, .3f, .32f), item % 2 == 0 ? p.Gold : p.Brick, false);
        }

        private static void StreetLamp(Transform b, Vector3 point, Palette p)
        {
            Transform lamp = NewRoot("Street_Lamp", b, point);
            lamp.gameObject.AddComponent<SlopCityInteractable>().Configure(SlopCityInteractionKind.Light, "Street lamp");
            GameObject pole=SlopCityStreetMeshes.Shape(lamp,"Lamp_Pole",SlopCityStreetMeshes.TaperedPole(),p.Metal);
            CapsuleCollider poleCollider=pole.AddComponent<CapsuleCollider>();poleCollider.center=Vector3.up*2.3f;poleCollider.height=4.6f;poleCollider.radius=.075f;
            Box(lamp, "Lamp_Base", new Vector3(0, .22f, 0), new Vector3(.35f, .4f, .35f), p.Charcoal);
            Box(lamp, "Lamp_Head", new Vector3(.45f, 4.55f, 0), new Vector3(1.15f, .16f, .42f), p.Charcoal);
            Box(lamp, "Lamp_Lens", new Vector3(.45f, 4.46f, 0), new Vector3(.90f, .025f, .32f), p.Amber, false);
            Light light = NewRoot("Lamp_Pool", lamp, new Vector3(.4f, 4.25f, 0)).gameObject.AddComponent<Light>();
            light.type = LightType.Point; light.range = 10; light.intensity = .9f;
            light.color = new Color(1, .71f, .41f); light.shadows = LightShadows.None;
        }

        private static void Bench(Transform parent, Vector3 point, float yaw, Palette p)
        {
            Transform bench = NewRoot("City_Bench", parent, point);
            bench.localRotation = Quaternion.Euler(0, yaw, 0);
            bench.gameObject.AddComponent<SlopCityInteractable>().Configure(SlopCityInteractionKind.Bench, "Street bench");
            GameObject seat=Box(bench, "Bench_Seat", new Vector3(0, .46f, 0), new Vector3(2.5f, .12f, .65f), p.Wood);
            GameObject back=Box(bench, "Bench_Back", new Vector3(0, .87f, .30f), new Vector3(2.5f, .7f, .10f), p.Wood);
            seat.GetComponent<Renderer>().enabled=false;back.GetComponent<Renderer>().enabled=false;
            for(int slat=0;slat<6;slat++)
                Box(bench,"Timber_SeatSlat",new Vector3(0,.49f,-.265f+slat*.106f),new Vector3(2.48f,.065f,.089f),p.Wood,false);
            for(int slat=0;slat<5;slat++)
            {
                GameObject board=Box(bench,"Timber_BackSlat",new Vector3(0,.66f+slat*.105f,.29f+(slat-2)*.015f),new Vector3(2.48f,.084f,.062f),p.Wood,false);
                board.transform.localRotation=Quaternion.Euler(-8,0,0);
            }
            foreach (float x in new[] { -.95f, .95f })
            {
                Box(bench, "Bench_Leg", new Vector3(x, .23f, 0), new Vector3(.13f, .46f, .55f), p.Metal);
                Box(bench,"Cast_Iron_BackSupport",new Vector3(x,.78f,.35f),new Vector3(.062f,.64f,.072f),p.Metal,false);
                Box(bench,"Cast_Iron_ArmRest",new Vector3(x,.71f,-.025f),new Vector3(.067f,.06f,.58f),p.Metal,false);
                Box(bench,"ArmRest_Support",new Vector3(x,.60f,-.23f),new Vector3(.048f,.22f,.048f),p.Metal,false);
                foreach(float z in new[]{-.21f,.21f})
                    Cylinder(bench,"Bench_Bolt",new Vector3(x,.53f,z),.016f,.007f,p.Alloy,false);
            }
            SlopCityStreetMeshes.Combine(bench,"StreetBench_TimberSlats",p.Wood);
            SlopCityStreetMeshes.Combine(bench,"StreetBench_CastFrame",p.Metal);
            SlopCityStreetMeshes.Combine(bench,"StreetBench_Bolts",p.Alloy);
        }

        private static void Bin(Transform b, Vector3 point, Palette p)
        {
            Transform bin=NewRoot("Litter_Bin",b,point);
            CapsuleCollider solid=bin.gameObject.AddComponent<CapsuleCollider>();solid.center=Vector3.up*.50f;solid.height=1;solid.radius=.30f;
            bin.gameObject.AddComponent<SlopCityInteractable>().Configure(SlopCityInteractionKind.Bin,"Street recycling bin");
            Cylinder(bin,"Bin_Liner",new Vector3(0,.47f,0),.268f,.82f,p.Rubber,false);
            for(int slat=0;slat<24;slat++)
            {
                float a=slat*Mathf.PI*2/24;
                GameObject bar=Box(bin,"Bin_SteelSlat",new Vector3(Mathf.Cos(a)*.285f,.52f,Mathf.Sin(a)*.285f),new Vector3(.039f,.89f,.022f),p.Metal,false);
                bar.transform.localRotation=Quaternion.Euler(0,-a*Mathf.Rad2Deg+90,0);
            }
            foreach(float y in new[]{.09f,.94f})
            {
                GameObject band=SlopCityStreetMeshes.Shape(bin,"Bin_Rim",SlopCityStreetMeshes.Ring("StreetBin_RolledBand",.288f,.022f),p.Metal);
                band.transform.localPosition=Vector3.up*y;
            }
            SlopCityStreetMeshes.Combine(bin,"StreetBin_SlattedMetal",p.Metal);
        }

        private static void Tree(Transform b, Vector3 point, Palette p)
        {
            Planter(b, point, new Vector2(2.6f, 2.6f), p);
            int variant=Mathf.Abs(Mathf.RoundToInt(point.x*3+point.z*7))%4;
            GameObject trunk=SlopCityStreetMeshes.Shape(b,"Tree_Trunk",SlopCityStreetMeshes.TreeBranches(variant),p.Bark);
            trunk.transform.localPosition=point;
            CapsuleCollider trunkCollider=trunk.AddComponent<CapsuleCollider>();trunkCollider.center=new Vector3(0,1.75f,0);trunkCollider.height=3.1f;trunkCollider.radius=.17f;
            SlopCityInteractable tree = trunk.AddComponent<SlopCityInteractable>();
            tree.Configure(SlopCityInteractionKind.Plant, "Street tree");
            tree.ConfigureInteractionPoint(b.TransformPoint(point + Vector3.up * 1.05f));
            GameObject leaves=SlopCityStreetMeshes.Shape(b,"Tree_Canopy",SlopCityStreetMeshes.TreeLeaves(variant),p.Leaves);
            leaves.transform.localPosition=point;
        }

        private static void Planter(Transform b, Vector3 point, Vector2 size, Palette p)
        {
            Box(b, "Planter_Base", point + Vector3.up * .25f, new Vector3(size.x, .50f, size.y), p.DarkConcrete);
            Box(b, "Planter_Green", point + Vector3.up * .51f, new Vector3(size.x - .2f, .05f, size.y - .2f), p.Moss, false);
        }

        private static void Car(Transform b, Vector3 point, float yaw, Material paint, Palette p)
        {
            Transform car = NewRoot("Parked_CityCar", b, point);
            car.localRotation = Quaternion.Euler(0, yaw, 0);
            Color baseColor=Color.Lerp(paint.color,new Color(.25f,.26f,.25f),.48f);
            Material finish=SlopCityStreetMeshes.Material("AutomotivePaint_"+ColorUtility.ToHtmlStringRGB(baseColor),baseColor,.46f,.87f);
            GameObject body=SlopCityStreetMeshes.Shape(car,"Car_Body",SlopCityStreetMeshes.SedanBody(),finish);
            BoxCollider bodyCollider=body.AddComponent<BoxCollider>();bodyCollider.center=new Vector3(0,.61f,0);bodyCollider.size=new Vector3(1.77f,.68f,3.87f);
            GameObject roof=SlopCityStreetMeshes.Shape(car,"Car_Roof",SlopCityStreetMeshes.SedanRoof(),finish);
            BoxCollider roofCollider=roof.AddComponent<BoxCollider>();roofCollider.center=new Vector3(0,1.53f,-.23f);roofCollider.size=new Vector3(1.32f,.085f,.90f);
            SlopCityStreetMeshes.Shape(car,"Car_Cabin_Glazing",SlopCityStreetMeshes.SedanGlass(),p.AutomotiveGlass);
            Transform trim=NewRoot("Car_TrimAssembly",car,Vector3.zero);
            Transform interior=NewRoot("Car_Interior",car,Vector3.zero);
            foreach(float side in new[]{-1f,1f})
            {
                SlopCityStreetMeshes.Shape(trim,"Car_A_Pillar",SlopCityStreetMeshes.Beam(new Vector3(side*.80f,.97f,.82f),new Vector3(side*.62f,1.50f,.24f),.022f,"Sedan_A_Pillar_"+(side<0?"L":"R")),finish);
                SlopCityStreetMeshes.Shape(trim,"Car_C_Pillar",SlopCityStreetMeshes.Beam(new Vector3(side*.806f,.966f,-1.10f),new Vector3(side*.626f,1.50f,-.70f),.037f,"Sedan_C_Pillar_"+(side<0?"L":"R")),finish);
                SlopCityStreetMeshes.Shape(trim,"Car_B_Pillar",SlopCityStreetMeshes.Beam(new Vector3(side*.808f,.973f,-.22f),new Vector3(side*.634f,1.506f,-.22f),.021f,"Sedan_B_Pillar_"+(side<0?"L":"R")),p.Rubber);
                Box(trim,"Window_WeatherSeal",new Vector3(side*.807f,.976f,-.12f),new Vector3(.018f,.019f,1.88f),p.Rubber,false);
                Box(trim,"Window_Chrome",new Vector3(side*.812f,.990f,-.12f),new Vector3(.010f,.006f,1.86f),p.Alloy,false);
                SlopCityStreetMeshes.Shape(trim,"Door_Handle",SlopCityStreetMeshes.Beam(new Vector3(side*.929f,.851f,-.54f),new Vector3(side*.929f,.851f,-.39f),.012f,"Sedan_DoorHandle_"+(side<0?"L":"R")),p.Alloy);
                Box(trim,"Sill_Skirt",new Vector3(side*.869f,.325f,0),new Vector3(.10f,.072f,1.45f),p.Rubber,false);
                GameObject mirror=SlopCityStreetMeshes.Shape(trim,"Car_Mirror",SlopCityStreetMeshes.Ellipsoid("Sedan_MirrorHousing",new Vector3(.135f,.060f,.098f)),finish);
                mirror.transform.localPosition=new Vector3(side*.965f,1.035f,.565f);
                mirror.transform.localRotation=Quaternion.Euler(0,side*14,0);
                GameObject mirrorGlass=SlopCityStreetMeshes.Shape(trim,"Mirror_Glass",SlopCityStreetMeshes.Ellipsoid("Sedan_MirrorFace",new Vector3(.098f,.042f,.009f)),p.Alloy);
                mirrorGlass.transform.localPosition=new Vector3(side*.96f,1.035f,.473f);
                SlopCityStreetMeshes.Shape(trim,"Mirror_Stem",SlopCityStreetMeshes.Beam(new Vector3(side*.813f,.996f,.59f),new Vector3(side*.94f,1.029f,.55f),.022f,"Sedan_MirrorStem_"+(side<0?"L":"R")),p.Rubber);
                Box(interior,"Seat_Cushion",new Vector3(side*.38f,.78f,-.38f),new Vector3(.58f,.12f,.62f),p.Upholstery,false);
                GameObject seatBack=Box(interior,"Seat_Back",new Vector3(side*.38f,1.01f,-.63f),new Vector3(.55f,.53f,.14f),p.Upholstery,false);
                seatBack.transform.localRotation=Quaternion.Euler(-8,0,0);
                Box(interior,"Seat_Headrest",new Vector3(side*.38f,1.31f,-.70f),new Vector3(.29f,.19f,.13f),p.Upholstery,false);
            }
            Box(interior,"Car_Dashboard",new Vector3(0,1.00f,.60f),new Vector3(1.45f,.17f,.29f),p.Upholstery,false);
            Box(interior,"Center_Console",new Vector3(0,.84f,-.05f),new Vector3(.19f,.16f,.74f),p.Upholstery,false);
            GameObject steering=SlopCityStreetMeshes.Shape(interior,"Steering_Wheel",SlopCityStreetMeshes.Ring("Sedan_SteeringWheel",.144f,.017f),p.Upholstery);
            steering.transform.localPosition=new Vector3(-.38f,1.113f,.375f);steering.transform.localRotation=Quaternion.Euler(69,0,0);
            foreach(float x in new[]{-.54f,.09f})
                SlopCityStreetMeshes.Shape(trim,"Windshield_Wiper",SlopCityStreetMeshes.Beam(new Vector3(x,1.02f,.807f),new Vector3(x+.34f,1.025f,.825f),.008f,"Sedan_Wiper_"+(x<0?"L":"R")),p.Rubber);
            SlopCityMetropolisBuilder.AddMediaAnchor(car,new Vector3(.17f,1.17f,.43f),Vector3.back,new Vector2(.48f,.27f),0);
            foreach(float x in new[]{-.92f,.92f}) foreach(float z in new[]{-1.17f,1.17f})
            {
                GameObject wheel=SlopCityStreetMeshes.Shape(car,"Car_Wheel",SlopCityStreetMeshes.Tire(),p.Rubber,true);
                wheel.transform.localPosition=new Vector3(x,.38f,z);
                GameObject rim=SlopCityStreetMeshes.Shape(car,"Car_AlloyRim",SlopCityStreetMeshes.Rim(),p.Alloy);
                rim.transform.localPosition=new Vector3(x,.38f,z);
            }
            Transform fittings=NewRoot("Car_FrontRearFittings",car,Vector3.zero);
            SlopCityStreetMeshes.Shape(fittings,"Recessed_Grille",SlopCityStreetMeshes.SedanGrille(false),p.Rubber);
            SlopCityStreetMeshes.Shape(fittings,"Curved_GrilleSlats",SlopCityStreetMeshes.SedanGrille(true),p.Alloy);
            SlopCityStreetMeshes.Shape(fittings,"Recessed_LampHousings",SlopCityStreetMeshes.SedanLampUnits(true),p.Rubber);
            SlopCityStreetMeshes.Shape(fittings,"Swept_HeadlampLenses",SlopCityStreetMeshes.SedanLampUnits(false),p.Headlamp);
            SlopCityStreetMeshes.Shape(fittings,"Headlamp_ProjectorOptics",SlopCityStreetMeshes.SedanLampOptics(),p.Alloy);
            SlopCityStreetMeshes.Shape(fittings,"Wraparound_TailLenses",SlopCityStreetMeshes.SedanTailLamps(),p.BrakeLamp);
            SlopCityStreetMeshes.Shape(trim,"Stamped_BodySeams",SlopCityStreetMeshes.SedanPanelSeams(),p.Rubber);
            SlopCityStreetMeshes.Shape(trim,"Rolled_WheelArchLips",SlopCityStreetMeshes.SedanWheelArchLips(),finish);
            Box(fittings,"Front_LicensePlate",new Vector3(0,.650f,1.989f),new Vector3(.39f,.113f,.012f),p.Cream,false);
            Box(fittings,"Rear_LicensePlate",new Vector3(0,.659f,-1.970f),new Vector3(.39f,.125f,.012f),p.Cream,false);
            Sign(car,"SC 1984",new Vector3(0,.652f,1.999f),Vector3.forward,.034f,p.Charcoal);
            Sign(car,"SC 1984",new Vector3(0,.661f,-1.981f),Vector3.back,.034f,p.Charcoal);
            foreach(Material material in new[]{finish,p.Rubber,p.Alloy}) SlopCityStreetMeshes.Combine(trim,"Sedan_Trim_"+material.name,material);
            SlopCityStreetMeshes.Combine(interior,"Sedan_Interior",p.Upholstery);
            foreach(Material material in new[]{p.Rubber,p.Alloy,p.Headlamp,p.BrakeLamp,p.Cream})
                SlopCityStreetMeshes.Combine(fittings,"Sedan_Fittings_"+material.name,material);
            SlopCityInteractable drive=car.gameObject.AddComponent<SlopCityInteractable>();
            drive.Configure(SlopCityInteractionKind.Car,"City cruiser");
            drive.ConfigureInteractionPoint(car.TransformPoint(new Vector3(-1.05f,1.05f,-.2f)));
            foreach(Transform part in car.GetComponentsInChildren<Transform>()) part.gameObject.isStatic=false;
        }

        private static Transform NewRoot(string name, Transform parent, Vector3 position)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            return obj.transform;
        }

        private static void Marker(Transform parent, string name, Vector3 worldPosition)
        {
            Transform marker = NewRoot(name, parent, Vector3.zero);
            marker.position = worldPosition;
        }

        private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collision = true)
        { return Primitive(parent, name, PrimitiveType.Cube, position, size, material, collision); }
        private static GameObject Sphere(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collision = true)
        { return Primitive(parent, name, PrimitiveType.Sphere, position, size, material, collision); }
        private static GameObject Cylinder(Transform parent, string name, Vector3 position, float radius, float height, Material material, bool collision = true)
        { return Primitive(parent, name, PrimitiveType.Cylinder, position, new Vector3(radius * 2, height / 2, radius * 2), material, collision); }

        private static GameObject Primitive(Transform parent, string name, PrimitiveType kind, Vector3 position, Vector3 size, Material material, bool collision)
        {
            GameObject obj = GameObject.CreatePrimitive(kind);
            obj.name = name; obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position; obj.transform.localScale = size;
            obj.isStatic = true;
            Renderer renderer = obj.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            if (!collision)
            {
                UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            return obj;
        }

        private static void Sign(Transform parent, string content, Vector3 position, Vector3 outward, float characterSize, Material color)
        {
            Transform sign = NewRoot("City_Sign_" + content.Split('\n')[0], parent, position);
            sign.localRotation = Quaternion.LookRotation(-outward, Vector3.up);
            TextMesh text = sign.gameObject.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 64; text.characterSize = characterSize * 10f / text.fontSize;
            text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
            text.text = content; text.color = color.color; text.lineSpacing = 1.08f;
            Renderer renderer = text.GetComponent<Renderer>(); renderer.sharedMaterial = text.font.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            sign.gameObject.AddComponent<SlopCityWorldText>().Configure(GetWorldTextMaterial());
        }

        public static Material GetWorldTextMaterial()
        {
            if (worldTextMaterial) return worldTextMaterial;
            Shader shader = Shader.Find("SlopCity/WorldText");
            if (!shader) throw new InvalidOperationException("The SlopCity/WorldText shader is required for city signs.");
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(MaterialsFolder)) AssetDatabase.CreateFolder("Assets/Materials", "SlopCityCity");
            worldTextMaterial = AssetDatabase.LoadAssetAtPath<Material>(WorldTextMaterialPath);
            if (!worldTextMaterial)
            {
                worldTextMaterial = new Material(shader) { name = "SlopCity_WorldText" };
                AssetDatabase.CreateAsset(worldTextMaterial, WorldTextMaterialPath);
            }
            else if (worldTextMaterial.shader != shader)
            {
                worldTextMaterial.shader = shader;
                EditorUtility.SetDirty(worldTextMaterial);
            }
            return worldTextMaterial;
        }

        private static string Slug(string source) { return source.Replace(" ", ""); }

        private sealed class Palette
        {
            public readonly Material Asphalt, Concrete, DarkConcrete, Paving, RoadPaint, Charcoal, Metal,
                DarkTeal, Teal, Brick, Blue, Wood, Cream, Gold, Cyan, Amber, Lime, Pink, Glass, Foliage, Moss, Water,
                AutomotiveGlass, ShopGlass, Rubber, Alloy, Upholstery, Bark, Leaves, Headlamp, BrakeLamp;
            public Palette()
            {
                if (!AssetDatabase.IsValidFolder(MaterialsFolder)) AssetDatabase.CreateFolder("Assets/Materials", "SlopCityCity");
                Asphalt = Make("Asphalt", "242522"); Concrete = Make("Concrete", "858780");
                DarkConcrete = Make("DarkConcrete", "434541"); Paving = Make("Paving", "93938A");
                RoadPaint = Make("RoadPaint", "B6B3A4"); Charcoal = Make("Charcoal", "252826");
                Metal = Make("Metal", "646663", 0, .55f); DarkTeal = Make("DarkTeal", "3C4441");
                Teal = Make("Teal", "5A6562"); Brick = Make("Brick", "745C50"); Blue = Make("Blue", "4F5B60");
                Wood = Make("Wood", "66564A"); Cream = Make("Cream", "CAC4B2"); Gold = Make("Gold", "B7A174");
                Cyan = Make("Cyan", "ADBFC0", .20f); Amber = Make("Amber", "D3BD89", .25f);
                Lime = Make("Lime", "A7AE8B", .15f); Pink = Make("Pink", "AD998F", .08f);
                Glass = Make("Glass", "39464A", 0, .25f, .7f);
                Foliage = Make("Foliage", "394638"); Moss = Make("Moss", "626C4F");
                Water = Make("Water", "374A4D", 0, .4f, .9f);
                AutomotiveGlass=SlopCityStreetMeshes.Material("Automotive_Glazing",new Color(.19f,.26f,.28f,.64f),.25f,.97f,true);
                ShopGlass=SlopCityStreetMeshes.Material("Shopfront_Glazing",new Color(.52f,.57f,.57f,.13f),.12f,.89f,true);
                Rubber=SlopCityStreetMeshes.Material("Automotive_Rubber",new Color(.032f,.034f,.035f),.02f,.24f);
                Alloy=SlopCityStreetMeshes.Material("Brushed_Alloy",new Color(.45f,.47f,.46f),.85f,.61f);
                Upholstery=SlopCityStreetMeshes.Material("Automotive_Upholstery",new Color(.075f,.081f,.078f),.03f,.28f);
                Bark=SlopCityStreetMeshes.Material("StreetTree_Bark",new Color(.18f,.15f,.105f),0,.17f);
                SlopCityStreetMeshes.BarkSurface(Bark);
                Leaves=SlopCityStreetMeshes.Material("StreetTree_Leaves",new Color(.20f,.27f,.105f),0,.28f);
                Headlamp=SlopCityStreetMeshes.Material("Automotive_LampLens",new Color(.34f,.40f,.43f),.58f,.96f);
                BrakeLamp=SlopCityStreetMeshes.Material("Automotive_BrakeLens",new Color(.31f,.021f,.013f),.3f,.80f);
            }
            private static Material Make(string name, string hex, float emission = 0, float metallic = 0, float gloss = .18f)
            {
                string path = MaterialsFolder + "/" + name + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool create = !material;
                if (create) material = new Material(Shader.Find("Standard"));
                material.name = "City_" + name;
                material.enableInstancing = true;
                ColorUtility.TryParseHtmlString("#" + hex, out Color color); material.color = color;
                material.SetFloat("_Metallic", metallic); material.SetFloat("_Glossiness", gloss);
                material.SetColor("_EmissionColor", color * emission);
                material.globalIlluminationFlags = emission > 0 ? MaterialGlobalIlluminationFlags.None : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                if (emission > 0) material.EnableKeyword("_EMISSION"); else material.DisableKeyword("_EMISSION");
                SlopCityMetropolisBuilder.AddSurfaceDetail(material, name);
                if (create) AssetDatabase.CreateAsset(material, path); else EditorUtility.SetDirty(material);
                return material;
            }
        }
    }
}
#endif
