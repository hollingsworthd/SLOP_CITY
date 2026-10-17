#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity.Editor
{
    /// <summary>Dense, physically detailed city dressing. Transit and roof decks remain real, collidable spaces.</summary>
    public static class SlopCityMetropolisBuilder
    {
        private const string Folder = "Assets/Materials/SlopCityCity";
        private static Material steel, concrete, glass, litGlass, warm, rubber, paint, tile, water, limestone, masonryCladding, bronze, safety, seatFabric, stainless, transitEnamel, transitGlass, platformConcrete, sootedConcrete;
        private static Texture2D grain, masonry, windowColor, windowEmission;
        private static int towerSerial;

        public static void BeginBuild()
        {
            towerSerial = 0;
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Materials", "SlopCityCity");
            grain = Texture("Aggregate", 128, 128, false);
            masonry = Texture("Masonry", 128, 128, false);
            windowColor = Texture("CurtainWall_RealisticV2", 512, 1024, false);
            windowEmission = Texture("CurtainWallEmission_RealisticV2", 512, 1024, true);
            steel = Material("BrushedSteel", new Color(.22f,.27f,.30f), .78f, .72f);
            concrete = Material("ArchitecturalConcrete", new Color(.44f,.46f,.44f), .04f, .35f);
            concrete.mainTexture = masonry; concrete.mainTextureScale = new Vector2(3, 3);
            glass = Material("ReflectiveGlass", new Color(.43f,.51f,.55f), .35f, .88f);
            limestone = Material("LimestoneCladding",new Color(.62f,.60f,.54f),.03f,.32f);
            masonryCladding = Material("WarmMasonryCladding",new Color(.37f,.31f,.27f),.03f,.30f);
            bronze = Material("AnodizedBronze",new Color(.29f,.27f,.23f),.76f,.51f);
            safety = Material("SafetyYellow",new Color(.70f,.55f,.16f),.04f,.32f);
            seatFabric = Material("AgedTransitUpholstery",new Color(.19f,.23f,.23f),.01f,.24f);
            stainless=Material("StainlessSteel",new Color(.48f,.50f,.50f),.82f,.46f);
            transitEnamel=Material("TransitEnamel",new Color(.48f,.51f,.49f),.20f,.35f);
            platformConcrete=Material("PlatformConcrete",new Color(.31f,.32f,.30f),.02f,.18f);
            sootedConcrete=Material("SootedConcrete",new Color(.27f,.29f,.27f),.01f,.12f);
            transitGlass=Material("TransitWindowGlass",new Color(.30f,.37f,.38f,.14f),.05f,.90f);
            transitGlass.SetFloat("_Mode",3);transitGlass.SetInt("_SrcBlend",(int)BlendMode.One);
            transitGlass.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);transitGlass.SetInt("_ZWrite",0);
            transitGlass.SetOverrideTag("RenderType","Transparent");transitGlass.DisableKeyword("_ALPHATEST_ON");
            transitGlass.DisableKeyword("_ALPHABLEND_ON");transitGlass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            transitGlass.renderQueue=3000;EditorUtility.SetDirty(transitGlass);
            litGlass = Material("CurtainWallGlass", Color.white, .30f, .82f);
            litGlass.mainTexture = windowColor;
            ConfigureEmission(litGlass, new Color(.72f, .68f, .59f), windowEmission);
            warm = Material("WarmLighting", new Color(1,.88f,.69f), .05f, .4f, 1.1f);
            rubber = Material("Rubber", new Color(.032f,.038f,.04f), .02f, .2f);
            paint = Material("TransitPaint", new Color(.66f,.69f,.66f), .48f, .64f);
            tile = Material("StationTile", new Color(.58f,.61f,.59f), .04f, .63f);
            tile.mainTexture = masonry; tile.mainTextureScale = new Vector2(5, 3);
            water = Material("RainPuddles", new Color(.10f,.16f,.19f), .72f, .97f);
        }

        public static void AddSurfaceDetail(Material material, string name)
        {
            if (!grain) return;
            if (name == "Asphalt" || name == "Concrete" || name == "DarkConcrete" || name == "Paving" || name == "Brick" || name == "Wood")
            {
                material.mainTexture = name == "Brick" || name == "Paving" ? masonry : grain;
                material.mainTextureScale = name == "Asphalt" ? new Vector2(42,42) : new Vector2(4,4);
                material.SetFloat("_Glossiness", name == "Asphalt" ? .43f : name == "Wood" ? .34f : .27f);
            }
        }

        public static void Build(Transform city, SlopCityDistrict[] districts, Bounds loungeFloor)
        {
            // Fill previously low-rise blocks while leaving the south entrances and center aisles intact.
            BuildTower(districts[0].transform, new Vector3(10.8f,0,10.8f), new Vector3(5.3f,76,5.3f), Color.white, true);
            BuildTower(districts[1].transform, new Vector3(10.8f,0,11.7f), new Vector3(5.3f,70,5.3f), Color.white, true);
            BuildTower(districts[8].transform, new Vector3(-10.8f,0,10.8f), new Vector3(5.3f,79,5.3f), Color.white, true);
            BuildTower(districts[2].transform, new Vector3(-11.4f,0,11.6f), new Vector3(4.8f,60,4.8f), Color.white, true);
            BuildTower(districts[2].transform, new Vector3(11.4f,0,11.6f), new Vector3(4.8f,83,4.8f), Color.white, true);
            BuildTower(districts[5].transform, new Vector3(-11.3f,0,12.0f), new Vector3(4.8f,71,4.8f), Color.white, true);
            BuildTower(districts[5].transform, new Vector3(11.3f,0,12.0f), new Vector3(4.8f,54,4.8f), Color.white, true);
            // The home towers are placed outside the immutable authored lounge footprint.
            float homeDepth = Mathf.Min(5.0f, 14.5f - loungeFloor.max.z - .7f);
            if (homeDepth >= 2.8f)
            {
                BuildTower(districts[4].transform, new Vector3(-11,0,14.5f-homeDepth/2), new Vector3(5.5f,66,homeDepth), Color.white, true);
                BuildTower(districts[4].transform, new Vector3(11,0,14.5f-homeDepth/2), new Vector3(5.5f,88,homeDepth), Color.white, true);
            }
            foreach (int index in new[] { 0, 1, 3, 6, 7, 8 })
                BuildTower(districts[index].transform, new Vector3(0,0,12.1f), new Vector3(7.0f,48 + index*5,4.4f), Color.white, true);

            RemoveTreesInsideTowerFootprints(city);
            RelocateObstructedLamps(city);
            BuildDistantSkyline(city);
            BuildTransit(city);
            BuildFuelCourt(districts[1].transform);
            BuildStreetDetails(city);
            foreach (int index in new[] {0,1,2,3,4,5,6,7,8})
                BuildBillboard(districts[index].transform, new Vector3(index == 3 ? -11 : 10.3f,0,-9.2f), index % 2);
        }

        public static void BuildTower(Transform parent, Vector3 point, Vector3 size, Color tint, bool accessible)
        {
            int serial=towerSerial++;
            Transform tower=Root((accessible?"City_Skyscraper_":"Skyline_Tower_")+serial,parent,point);
            if(!accessible) {BuildHorizonTower(tower,size,serial);return;}
            // Narrow existing plots read as 11–15 storey infill. Broad high-rises live on
            // larger plots beyond the playable street grid, preserving all walk routes.
            size.y=Mathf.Clamp(Mathf.Round((size.y*.40f+14)/3.2f)*3.2f,35.2f,48.0f);
            float height=size.y,halfX=size.x/2,halfZ=size.z/2;
            int style=serial%4;Material cladding=style==1?limestone:style==2?masonryCladding:concrete;
            Rounded(tower,"Structural_Core",new Vector3(0,height/2,0),size,.12f,cladding,true);
            BuildFacadeRhythm(tower,size,0,serial,style,true);
            Rounded(tower,"Rooftop_Deck",new Vector3(0,height+.07f,0),new Vector3(size.x+.12f,.14f,size.z+.12f),.04f,concrete);
            foreach(float x in new[]{-halfX,halfX})
            {
                Rounded(tower,"Roof_Parapet",new Vector3(x,height+.51f,0),new Vector3(.13f,.92f,size.z),.035f,cladding);
                Box(tower,"Metal_Coping",new Vector3(x,height+.99f,0),new Vector3(.19f,.04f,size.z+.1f),steel,false);
            }
            foreach(float z in new[]{-halfZ,halfZ})
            {
                Rounded(tower,"Roof_Parapet",new Vector3(0,height+.51f,z),new Vector3(size.x,.92f,.13f),.035f,cladding);
                Box(tower,"Metal_Coping",new Vector3(0,height+.99f,z),new Vector3(size.x+.1f,.04f,.19f),steel,false);
            }
            Rounded(tower,"Mechanical_Unit",new Vector3(halfX*.58f,height+.65f,halfZ*.5f),new Vector3(1.2f,1.1f,.8f),.055f,steel);
            for(int vent=0;vent<7;vent++)
                Box(tower,"HVAC_Louvre",new Vector3(halfX*.58f,height+.22f+vent*.125f,halfZ*.5f-.415f),new Vector3(.97f,.040f,.027f),rubber,false);
            Rounded(tower,"Roof_Plant_Screen",new Vector3(halfX*.50f,height+.62f,halfZ-.35f),new Vector3(size.x*.38f,.94f,.17f),.04f,cladding,false);
            if(serial%4==0)
            {
                SlopCityArchitecturalMeshes.Tube(tower,"Antenna_Mast",new[]{new Vector3(halfX*.72f,height+.5f,halfZ*.7f),new Vector3(halfX*.72f,height+3.6f,halfZ*.7f)},.028f,steel);
                Rounded(tower,"Aviation_Light",new Vector3(halfX*.72f,height+3.65f,halfZ*.7f),new Vector3(.08f,.12f,.08f),.03f,warm,false);
            }
            // Deep stone reveals and metal framing give the street floor a real entrance scale.
            Rounded(tower,"Lobby_Entrance_Recess",new Vector3(0,1.48f,-halfZ-.025f),new Vector3(2.12f,2.9f,.10f),.035f,rubber,false);
            foreach(float side in new[]{-1f,1f})
            {
                Box(tower,"Lobby_Jamb",new Vector3(side*1.13f,1.53f,-halfZ-.09f),new Vector3(.15f,3.06f,.18f),limestone,false);
                Box(tower,"Lobby_Glazing",new Vector3(side*.54f,1.44f,-halfZ-.087f),new Vector3(.96f,2.7f,.025f),glass,false);
                Box(tower,"Lobby_DoorRail",new Vector3(side*.05f,1.45f,-halfZ-.12f),new Vector3(.05f,2.8f,.04f),bronze,false);
            }
            Rounded(tower,"Lobby_Canopy",new Vector3(0,3.05f,-halfZ-.43f),new Vector3(Mathf.Min(size.x,3.5f),.16f,.95f),.045f,steel);
            Box(tower,"Lobby_Downlight",new Vector3(0,2.96f,-halfZ-.60f),new Vector3(1.5f,.025f,.045f),warm,false);
            Transform street=Root("Tower_Lobby_"+serial,tower,new Vector3(0,.08f,-halfZ-.8f));
            Transform roof=Root("Tower_Roof_"+serial,tower,new Vector3(-halfX*.25f,height+.17f,-halfZ*.4f));
            string label="Tower "+(serial+1).ToString("00");
            Terminal(tower,"Tower_Lift_Street",new Vector3(0,1.22f,-halfZ-.13f),Vector3.back,label+" / ROOF LIFT",SlopCityInteractionKind.Building,roof);
            Terminal(tower,"Tower_Lift_Roof",new Vector3(-halfX+.2f,height+1.2f,0),Vector3.right,"RETURN TO STREET",SlopCityInteractionKind.Building,street);
            Label(tower,(101+serial)+" / ROOF TERRACE",new Vector3(0,3.42f,-halfZ-.035f),Vector3.back,.095f);
        }

        private static void BuildFacadeRhythm(Transform tower,Vector3 size,float bottom,int serial,int style,bool podium)
        {
            float lower=podium?3.8f:bottom,top=bottom+size.y,facadeHeight=top-lower;
            float hx=size.x/2,hz=size.z/2;
            float bay=style==1?1.55f:style==2?1.4f:style==3?1.65f:1.35f,pier=style==0?.052f:style==1?.23f:style==2?.18f:.22f;
            int countX=Mathf.Max(2,Mathf.RoundToInt(size.x/bay)),countZ=Mathf.Max(2,Mathf.RoundToInt(size.z/bay));
            Facade(tower,new Vector3(0,(lower+top)/2,-hz-.018f),Vector3.back,size.x,facadeHeight,serial,countX,style);
            Facade(tower,new Vector3(0,(lower+top)/2,hz+.018f),Vector3.forward,size.x,facadeHeight,serial+2,countX,style);
            Facade(tower,new Vector3(-hx-.018f,(lower+top)/2,0),Vector3.left,size.z,facadeHeight,serial+3,countZ,style);
            Facade(tower,new Vector3(hx+.018f,(lower+top)/2,0),Vector3.right,size.z,facadeHeight,serial+5,countZ,style);
            Material frame=style==0?bronze:style==1?limestone:style==2?masonryCladding:concrete;
            var frames=new List<GameObject>();
            float bandHeight=style==0?.10f:style==1?.38f:style==2?.22f:.32f;
            for(float y=lower;y<top-.3f;y+=3.2f)
                frames.Add(Box(tower,"Recessed_Floor_Band",new Vector3(0,y,0),new Vector3(size.x+.13f,bandHeight,size.z+.13f),frame,false));
            for(int x=0;x<=countX;x++)
            {
                float xx=-hx+x*size.x/countX;
                foreach(float z in new[]{-hz-.055f,hz+.055f}) frames.Add(Box(tower,"Vertical_Facade_Pier",new Vector3(xx,(lower+top)/2,z),new Vector3(pier,facadeHeight,.16f),frame,false));
            }
            for(int z=0;z<=countZ;z++)
            {
                float zz=-hz+z*size.z/countZ;
                foreach(float x in new[]{-hx-.055f,hx+.055f}) frames.Add(Box(tower,"Vertical_Facade_Pier",new Vector3(x,(lower+top)/2,zz),new Vector3(.16f,facadeHeight,pier),frame,false));
            }
            frames.Add(Box(tower,"Roof_Cornice",new Vector3(0,top-.17f,0),new Vector3(size.x+.18f,.34f,size.z+.18f),frame,false));
            CombineDetails(tower,frames,frame,"Facade_Frames_"+serial);
            if(podium)
            {
                var joints=new List<GameObject>();
                for(float y=.6f;y<3.8f;y+=.62f)
                    joints.Add(Box(tower,"Stone_Bed_Joint",new Vector3(0,y,-hz-.007f),new Vector3(size.x,.012f,.018f),rubber,false));
                CombineDetails(tower,joints,rubber,"Podium_Joints_"+serial);
                Box(tower,"Podium_Cornice",new Vector3(0,3.72f,0),new Vector3(size.x+.14f,.18f,size.z+.14f),frame,false);
            }
        }

        private static void BuildHorizonTower(Transform tower,Vector3 size,int serial)
        {
            int style=serial%4;float lowerHeight=Mathf.Floor(size.y*.76f/3.2f)*3.2f;
            Material cladding=style==1?limestone:style==2?masonryCladding:concrete;
            Rounded(tower,"Structural_Core",new Vector3(0,lowerHeight/2,0),new Vector3(size.x,lowerHeight,size.z),.20f,cladding,false);
            BuildFacadeRhythm(tower,new Vector3(size.x,lowerHeight,size.z),0,serial,style,false);
            float upperHeight=size.y-lowerHeight;
            Vector3 upperSize=new Vector3(size.x*(style==0?.83f:.70f),upperHeight,size.z*(style==3?.68f:.80f));
            Rounded(tower,"Setback_UpperShaft",new Vector3(0,lowerHeight+upperHeight/2,0),upperSize,.20f,cladding,false);
            BuildFacadeRhythm(tower,upperSize,lowerHeight,serial+97,style,false);
            Rounded(tower,"Mechanical_Crown",new Vector3(0,size.y+1.6f,0),new Vector3(upperSize.x*.56f,3.2f,upperSize.z*.64f),.16f,steel,false);
            for(int tier=0;tier<4;tier++) Box(tower,"Crown_Louvre",new Vector3(0,size.y+.55f+tier*.55f,-upperSize.z*.32f-.02f),new Vector3(upperSize.x*.53f,.15f,.10f),rubber,false);
        }

        private static void RemoveTreesInsideTowerFootprints(Transform city)
        {
            // Existing trees use sibling primitives; prune only overlapping decorative foliage/trunks.
            var towers = new List<Bounds>();
            foreach(Transform child in city.GetComponentsInChildren<Transform>())
                if(child.name=="Structural_Core" && child.parent.name.StartsWith("City_Skyscraper")) towers.Add(child.GetComponent<Renderer>().bounds);
            var remove=new HashSet<GameObject>();
            foreach(Transform child in city.GetComponentsInChildren<Transform>())
            {
                if(!child.name.StartsWith("Tree_") && child.name!="Planter_Base" && child.name!="Planter_Green") continue;
                Renderer renderer=child.GetComponent<Renderer>();
                if(!renderer) continue;
                foreach(Bounds bounds in towers)
                    if(bounds.Intersects(renderer.bounds))
                    {
                        remove.Add(child.gameObject);
                        if(child.name.StartsWith("Tree_"))
                            foreach(Transform sibling in child.parent)
                            {
                                Vector3 offset=sibling.localPosition-child.localPosition;offset.y=0;
                                if(sibling.name.StartsWith("Tree_") && offset.sqrMagnitude<.001f) remove.Add(sibling.gameObject);
                            }
                        break;
                    }
            }
            foreach(GameObject item in remove) Object.DestroyImmediate(item);
        }

        private static void RelocateObstructedLamps(Transform city)
        {
            Bounds[] masses = System.Array.ConvertAll(
                System.Array.FindAll(city.GetComponentsInChildren<Transform>(), t=>t.name=="Structural_Core" && t.parent.name.StartsWith("City_Skyscraper")),
                t=>t.GetComponent<Renderer>().bounds);
            foreach(Transform lamp in city.GetComponentsInChildren<Transform>())
            {
                if(lamp.name!="Street_Lamp") continue;
                foreach(Bounds mass in masses)
                    if(mass.Contains(lamp.position+Vector3.up))
                    {
                        lamp.localPosition=new Vector3(13.5f,0,5.8f);
                        break;
                    }
            }
        }

        private static void BuildDistantSkyline(Transform city)
        {
            Transform skyline=Root("Metropolitan_Horizon",city,Vector3.zero);
            // Scenery beyond the existing map boundary adds depth without creating unreachable objectives.
            for(int side=0;side<4;side++)
                for(int index=0;index<7;index++)
                {
                    Vector3 point=new Vector3(-84+index*28,0,94+(index%2)*21);
                    point=Quaternion.Euler(0,side*90,0)*point;
                    BuildTower(skyline,point,new Vector3(18+index%3*6,68+((index*23+side*31)%84),19+index%2*10),Color.white,false);
                }
        }

        private static void BuildFuelCourt(Transform block)
        {
            Transform court=Root("MotorCourt_FuelStation",block,new Vector3(-10,0,-.3f));
            Rounded(court,"Fuel_Island",new Vector3(0,.10f,0),new Vector3(3.2f,.20f,5.5f),.07f,concrete);
            Rounded(court,"Fuel_Canopy",new Vector3(0,3.7f,0),new Vector3(4.5f,.34f,7),.095f,paint);
            Box(court,"Canopy_Fascia_Reveal",new Vector3(0,3.64f,0),new Vector3(4.53f,.055f,7.03f),steel,false);
            Box(court,"Canopy_Soffit",new Vector3(0,3.51f,0),new Vector3(4.12f,.04f,6.65f),rubber,false);
            foreach(float z in new[]{-2.25f,2.25f})
            {
                Rounded(court,"Canopy_Column",new Vector3(0,1.8f,z),new Vector3(.20f,3.6f,.20f),.045f,steel);
                Rounded(court,"Column_FootPlate",new Vector3(0,.23f,z),new Vector3(.36f,.06f,.36f),.02f,steel,false);
                foreach(float x in new[]{-1.1f,1.1f})
                {
                    Rounded(court,"Canopy_Luminaire",new Vector3(x,3.48f,z),new Vector3(.58f,.055f,.62f),.035f,paint,false);
                    Box(court,"Canopy_Lamp_Diffuser",new Vector3(x,3.445f,z),new Vector3(.40f,.012f,.43f),warm,false);
                }
            }
            foreach(float x in new[]{-1.30f,1.30f}) foreach(float z in new[]{-2.40f,2.40f})
            {
                SlopCityArchitecturalMeshes.Tube(court,"Protective_Bollard_"+x+"_"+z,new[]{new Vector3(x,.20f,z),new Vector3(x,1.02f,z)},.065f,steel);
                SlopCityArchitecturalMeshes.Tube(court,"Bollard_Collar_"+x+"_"+z,new[]{new Vector3(x,.78f,z),new Vector3(x,.89f,z)},.069f,safety);
            }
            for(int i=0;i<2;i++)
            {
                Transform pump=Root("GasPump_"+i,court,new Vector3(0,.2f,i==0?-1.3f:1.3f));
                Rounded(pump,"Pump_Base",new Vector3(0,.44f,0),new Vector3(.79f,.88f,.59f),.065f,paint);
                Rounded(pump,"Pump_Kickplate",new Vector3(0,.10f,0),new Vector3(.82f,.20f,.62f),.035f,steel,false);
                Rounded(pump,"Pump_Meter",new Vector3(0,1.16f,0),new Vector3(.97f,.60f,.66f),.070f,rubber);
                Rounded(pump,"Pump_Crown",new Vector3(0,1.52f,0),new Vector3(1.04f,.15f,.70f),.055f,paint);
                foreach(float face in new[]{-1f,1f})
                {
                    Rounded(pump,"Meter_Bezel",new Vector3(0,1.20f,face*.334f),new Vector3(.79f,.47f,.024f),.025f,steel,false);
                    AddMediaAnchor(pump,new Vector3(0,1.20f,face*.350f),face<0?Vector3.back:Vector3.forward,new Vector2(.73f,.41f),i%2);
                    Rounded(pump,"Fuel_Readout",new Vector3(-.08f,.74f,face*.306f),new Vector3(.47f,.15f,.028f),.014f,rubber,false);
                    Label(pump,"00.00 / GAL",new Vector3(-.08f,.75f,face*.324f),face<0?Vector3.back:Vector3.forward,.035f);
                    Box(pump,"Payment_Slot",new Vector3(.265f,.75f,face*.31f),new Vector3(.12f,.024f,.032f),rubber,false);
                    Label(pump,"93  PREMIUM",new Vector3(0,.51f,face*.308f),face<0?Vector3.back:Vector3.forward,.05f);
                    for(int slot=0;slot<5;slot++) Box(pump,"Cabinet_Vent",new Vector3(-.16f+slot*.08f,.24f,face*.31f),new Vector3(.025f,.12f,.012f),rubber,false);
                    foreach(float x in new[]{-.33f,.33f}) foreach(float y in new[]{.34f,.84f})
                        Rounded(pump,"Cabinet_Fastener",new Vector3(x,y,face*.31f),new Vector3(.024f,.024f,.012f),.005f,steel,false);
                }
                Rounded(pump,"Nozzle_Holster",new Vector3(.47f,.93f,0),new Vector3(.10f,.33f,.23f),.025f,steel,false);
                GameObject handle=Rounded(pump,"Nozzle_Grip",new Vector3(.58f,.91f,-.015f),new Vector3(.12f,.28f,.095f),.035f,rubber,false);
                handle.transform.localRotation=Quaternion.Euler(0,0,-23);
                SlopCityArchitecturalMeshes.Tube(pump,"Nozzle_Spout",new[]{new Vector3(.62f,1.04f,0),new Vector3(.60f,1.17f,0),new Vector3(.49f,1.23f,0),new Vector3(.43f,1.21f,0)},.022f,steel);
                var hose=new Vector3[17];
                for(int segment=0;segment<hose.Length;segment++)
                {
                    float t=segment/(float)(hose.Length-1);
                    hose[segment]=new Vector3(.47f+.39f*Mathf.Sin(t*Mathf.PI),1.23f*(1-t)+.76f*t-.83f*Mathf.Sin(t*Mathf.PI),.02f);
                }
                SlopCityArchitecturalMeshes.Tube(pump,"Reinforced_Fuel_Hose",hose,.027f,rubber);
                var action=pump.gameObject.AddComponent<SlopCityInteractable>();
                action.Configure(SlopCityInteractionKind.GasPump,"Fuel pump "+(i+1));
                action.ConfigureInteractionPoint(pump.TransformPoint(new Vector3(.50f,1.05f,-.4f)));
            }
            Label(court,"MOTOR COURT / FUEL + SIGNAL",new Vector3(0,3.72f,-3.525f),Vector3.back,.105f);
            Label(court,"AUTHORIZED FUEL DISPENSARY / 24 HOUR MONITORING",new Vector3(0,3.55f,-3.535f),Vector3.back,.045f);
        }

        private static void BuildTransit(Transform city)
        {
            Transform network=Root("SlopCity_Subway",city,Vector3.zero);
            var stations=new Transform[2];var platforms=new Transform[2];var surfaces=new Transform[2];
            for(int i=0;i<2;i++)
            {
                stations[i]=Root("Subway_Station_"+i,network,i==0?new Vector3(-48,0,20):new Vector3(48,0,-20));
                platforms[i]=Root("Metro_Platform_"+i,stations[i],new Vector3(0,-7.80f,-1.2f));
                surfaces[i]=Root("Metro_Surface_"+i,stations[i],new Vector3(0,.07f,-3.0f));
            }
            for(int i=0;i<2;i++)
            {
                Transform station=stations[i];string stationName=i==0?"SIGNAL YARD":"NIGHT MARKET";
                Transform entry=Root("Subway_SurfaceLift",station,Vector3.zero);
                Rounded(entry,"Lift_Pad",new Vector3(0,.025f,0),new Vector3(3.1f,.1f,3.2f),.035f,concrete);
                Rounded(entry,"Lift_Back",new Vector3(0,1.35f,1.45f),new Vector3(3.1f,2.7f,.18f),.045f,steel);
                foreach(float side in new[]{-1.46f,1.46f})
                {
                    Box(entry,"Lift_Side",new Vector3(side,1.35f,0),new Vector3(.10f,2.7f,3),glass);
                    foreach(float z in new[]{-1.43f,1.43f}) Rounded(entry,"Elevator_Steel_Jamb",new Vector3(side,1.4f,z),new Vector3(.17f,2.8f,.17f),.025f,steel);
                    Box(entry,"Elevator_Rail",new Vector3(side,.80f,0),new Vector3(.14f,.055f,2.8f),bronze,false);
                }
                Rounded(entry,"Metro_Canopy",new Vector3(0,2.8f,0),new Vector3(3.5f,.24f,3.7f),.070f,steel);
                Box(entry,"Metro_Light",new Vector3(0,2.65f,-1.6f),new Vector3(2.3f,.035f,.06f),warm,false);
                Label(entry,"M / "+stationName,new Vector3(0,2.83f,-1.89f),Vector3.back,.125f);
                Label(entry,"CIVIC TRANSIT / LINE 01",new Vector3(0,2.55f,-1.64f),Vector3.back,.055f);
                Terminal(entry,"Platform_Lift_Control",new Vector3(0,1.15f,-1.64f),Vector3.back,"LIFT TO SUBWAY",SlopCityInteractionKind.Subway,platforms[i]);
                Box(station,"Station_Floor",new Vector3(0,-8,0),new Vector3(15,.28f,11),platformConcrete);
                Box(station,"Station_Ceiling",new Vector3(0,-4.4f,0),new Vector3(15,.30f,11),sootedConcrete);
                Box(station,"Platform_RearWall",new Vector3(0,-6.25f,-5.4f),new Vector3(15,3.5f,.20f),tile);
                foreach(float x in new[]{-7.4f,7.4f}) Box(station,"Platform_EndWall",new Vector3(x,-6.25f,0),new Vector3(.20f,3.5f,11),concrete);
                Box(station,"Tunnel_BackWall",new Vector3(0,-6.25f,5.4f),new Vector3(15,3.5f,.20f),concrete);
                Box(station,"Tile_Dado",new Vector3(0,-6.55f,-5.275f),new Vector3(14.6f,.12f,.026f),bronze,false);
                Box(station,"Tile_Skirting",new Vector3(0,-7.65f,-5.27f),new Vector3(14.6f,.30f,.045f),steel,false);
                for(int seam=0;seam<22;seam++) Box(station,"Tile_Expansion_Joint",new Vector3(-7.1f+seam*.68f,-6.24f,-5.286f),new Vector3(.009f,3.1f,.012f),rubber,false);
                Box(station,"Platform_SafetyStrip",new Vector3(0,-7.837f,1.08f),new Vector3(14,.018f,.30f),safety,false);
                for(int panel=0;panel<32;panel++) Box(station,"Tactile_Rib",new Vector3(-6.8f+panel*.44f,-7.823f,1.08f),new Vector3(.018f,.014f,.27f),steel,false);
                foreach(float railZ in new[]{2.1f,4.1f}) Box(station,"Running_Rail",new Vector3(0,-7.832f,railZ),new Vector3(14.8f,.025f,.065f),steel,false);
                for(int rib=0;rib<6;rib++)
                {
                    var points=new Vector3[17];
                    for(int point=0;point<points.Length;point++)
                    {float z=-5.2f+point*.65f;points[point]=new Vector3(-6.5f+rib*2.6f,-4.65f-.23f*Mathf.Pow(z/5.2f,2),z);}
                    SlopCityArchitecturalMeshes.Tube(station,"Ceiling_Structural_Rib_"+rib,points,.036f,steel);
                }
                foreach(float x in new[]{-6.65f,6.65f})
                {
                    Box(station,"Column_Web",new Vector3(x,-6.2f,-1.8f),new Vector3(.07f,3.3f,.30f),steel);
                    foreach(float z in new[]{-1.96f,-1.64f}) Rounded(station,"Column_Flange",new Vector3(x,-6.2f,z),new Vector3(.35f,3.3f,.045f),.012f,steel);
                    Rounded(station,"Column_Base",new Vector3(x,-7.80f,-1.8f),new Vector3(.48f,.12f,.48f),.02f,steel);
                }
                for(int cable=0;cable<3;cable++) SlopCityArchitecturalMeshes.Tube(station,"Service_Conduit_"+cable,new[]{new Vector3(-7.2f,-4.8f-cable*.13f,-5.08f),new Vector3(7.2f,-4.8f-cable*.13f,-5.08f)},.024f,steel);
                for(int lamp=0;lamp<4;lamp++)
                {
                    Transform luminaire=Root("Platform_Luminaire_"+i+"_"+lamp,station,new Vector3(-5+lamp*3.4f,-4.68f,-2.4f));
                    Rounded(luminaire,"Light_Housing",Vector3.zero,new Vector3(2.35f,.11f,.35f),.03f,steel,false);
                    foreach(float z in new[]{-.09f,.09f}) SlopCityArchitecturalMeshes.Tube(luminaire,"Fluorescent_Tube_"+z,new[]{new Vector3(-1.05f,-.075f,z),new Vector3(1.05f,-.075f,z)},.019f,warm);
                }
                Light pool=Root("Station_Ambient",station,new Vector3(0,-5.8f,-1.8f)).gameObject.AddComponent<Light>();
                pool.type=LightType.Point;pool.range=13;pool.intensity=.90f;pool.shadows=LightShadows.None;pool.color=new Color(.79f,.84f,.82f);
                Terminal(station,"Street_Lift_Control",new Vector3(-5.5f,-6.65f,-4.9f),Vector3.forward,"LIFT TO STREET",SlopCityInteractionKind.Subway,surfaces[i]);
                Rounded(station,"Station_Nameplate",new Vector3(0,-5.39f,-5.245f),new Vector3(6.3f,.62f,.055f),.025f,rubber,false);
                Label(station,stationName+" / LINE 01",new Vector3(0,-5.40f,-5.207f),Vector3.forward,.21f);
                Rounded(station,"Platform_Media_Frame",new Vector3(3.3f,-6.24f,-5.245f),new Vector3(3.92f,2.26f,.06f),.04f,steel,false);
                AddMediaAnchor(station,new Vector3(3.3f,-6.24f,-5.20f),Vector3.forward,new Vector2(3.8f,2.14f),i);
                Label(station,"SERVICE NOTICE / YOUR JOURNEY IS MONITORED",new Vector3(-2.2f,-7.2f,-5.23f),Vector3.forward,.075f);
                BuildTrain(station,i,platforms[1-i]);
            }
        }

        private static void BuildTrain(Transform station,int index,Transform destination)
        {
            Transform train=Root("Subway_Train_"+index,station,new Vector3(0,-7.86f,3.10f));
            Box(train,"Train_Floor",new Vector3(0,.055f,0),new Vector3(12,.11f,3.65f),rubber);
            Rounded(train,"Train_Roof",new Vector3(0,2.78f,0),new Vector3(12,.28f,3.65f),.125f,transitEnamel);
            Rounded(train,"Train_RearWall",new Vector3(0,1.4f,1.78f),new Vector3(12,2.8f,.12f),.045f,transitEnamel);
            foreach(float side in new[]{-5.94f,5.94f})
            {
                Rounded(train,"Train_EndWall",new Vector3(side,1.4f,0),new Vector3(.14f,2.8f,3.65f),.06f,transitEnamel);
                Box(train,"End_Door_Recess",new Vector3(side-(Mathf.Sign(side)*.08f),1.42f,0),new Vector3(.025f,2.26f,.82f),stainless,false);
                Box(train,"End_Door_Window",new Vector3(side-(Mathf.Sign(side)*.098f),1.72f,0),new Vector3(.021f,.66f,.53f),glass,false);
            }
            foreach(float side in new[]{-1f,1f})
            {
                Rounded(train,"Train_LowerBody",new Vector3(side*3.5f,.56f,-1.78f),new Vector3(5,1.07f,.12f),.045f,stainless);
                Rounded(train,"Train_Header",new Vector3(side*3.5f,2.49f,-1.78f),new Vector3(5,.34f,.12f),.05f,transitEnamel);
                Box(train,"Train_Beltline",new Vector3(side*3.5f,1.08f,-1.845f),new Vector3(5,.07f,.035f),bronze,false);
                for(int pane=0;pane<3;pane++)
                {
                    float x=side*(1.82f+pane*1.57f);
                    RoundedWindow(train,"Train_Window_"+side+"_"+pane,new Vector3(x,1.72f,-1.825f),new Vector2(1.34f,1.055f),.085f);
                    // Pillars and sill actually frame the windows instead of a single blank body slab.
                    Box(train,"Train_WindowPier",new Vector3(x+side*.77f,1.73f,-1.78f),new Vector3(.12f,1.5f,.11f),transitEnamel);
                }
                Box(train,"Door_Jamb",new Vector3(side*1.025f,1.41f,-1.78f),new Vector3(.095f,2.72f,.16f),stainless);
                Transform seating=Root("Subway_SeatBank_"+(side<0?"L":"R"),train,Vector3.zero);
                for(int seat=0;seat<6;seat++)
                {
                    float x=side*(1.87f+seat*.56f);
                    Rounded(seating,"Moulded_Seat",new Vector3(x,.52f,1.18f),new Vector3(.52f,.15f,.60f),.065f,seatFabric);
                    GameObject back=Rounded(seating,"Moulded_Seat_Back",new Vector3(x,.91f,1.46f),new Vector3(.52f,.64f,.12f),.05f,seatFabric);
                    back.transform.localRotation=Quaternion.Euler(10,0,0);
                }
                Box(seating,"Seat_Continuous_Rail",new Vector3(side*3.4f,.36f,1.2f),new Vector3(3.8f,.08f,.08f),stainless,false);
                foreach(float x in new[]{side*1.59f,side*5.14f}) SlopCityArchitecturalMeshes.Tube(seating,"Seat_EndRail_"+x,new[]{new Vector3(x,.35f,1.16f),new Vector3(x,.78f,1.16f),new Vector3(x,.79f,1.50f)},.020f,stainless);
                SlopCityInteractable seats=seating.gameObject.AddComponent<SlopCityInteractable>();
                seats.Configure(SlopCityInteractionKind.Bench,"Subway seating",false);
                seats.ConfigureInteractionPoint(train.TransformPoint(new Vector3(side*3.4f,.84f,1.10f)));
                SlopCityArchitecturalMeshes.Tube(train,"Grab_Pole_"+side,new[]{new Vector3(side*2.0f,.13f,.25f),new Vector3(side*2.0f,2.60f,.25f)},.021f,stainless);
            }
            SlopCityArchitecturalMeshes.Tube(train,"Overhead_GrabRail",new[]{new Vector3(-5.45f,2.55f,.25f),new Vector3(5.45f,2.55f,.25f)},.023f,stainless);
            for(int grip=0;grip<8;grip++)
            {
                var points=new Vector3[13];float x=-4.55f+grip*1.30f;
                for(int vertex=0;vertex<points.Length;vertex++)
                {float angle=vertex*Mathf.PI*2/12;points[vertex]=new Vector3(x+Mathf.Sin(angle)*.10f,2.34f+Mathf.Cos(angle)*.16f,.25f);}
                SlopCityArchitecturalMeshes.Tube(train,"Passenger_HandLoop_"+grip,points,.012f,rubber);
            }
            Rounded(train,"Train_DoorLintel",new Vector3(0,2.62f,-1.78f),new Vector3(2,.30f,.15f),.035f,stainless);
            Box(train,"Boarding_Bridge",new Vector3(0,.035f,-2.02f),new Vector3(1.9f,.07f,.62f),stainless);
            foreach(float side in new[]{-1f,1f}) Box(train,"Door_Warning_Edge",new Vector3(side*.96f,1.41f,-1.87f),new Vector3(.023f,2.36f,.015f),safety,false);
            Rounded(train,"Passenger_Media_Frame",new Vector3(0,1.8f,1.74f),new Vector3(2.84f,1.66f,.06f),.045f,rubber,false);
            AddMediaAnchor(train,new Vector3(0,1.8f,1.697f),Vector3.back,new Vector2(2.7f,1.52f),1-index);
            Terminal(train,"Train_Departure_Control",new Vector3(4.9f,1.30f,.4f),Vector3.left,"RIDE TO "+(index==0?"NIGHT MARKET":"SIGNAL YARD"),SlopCityInteractionKind.Subway,destination);
            Rounded(train,"Departure_Console_Post",new Vector3(4.9f,.51f,.4f),new Vector3(.095f,.81f,.095f),.018f,stainless);
            Label(train,"01 / "+(index==0?"NIGHT MARKET":"SIGNAL YARD"),new Vector3(0,2.65f,-1.87f),Vector3.back,.085f);
            Label(train,"WATCH THE GAP",new Vector3(0,.12f,-2.15f),Vector3.up,.07f);
            Light interior=Root("Carriage_Light",train,new Vector3(0,2.45f,0)).gameObject.AddComponent<Light>();
            interior.type=LightType.Point;interior.range=8;interior.intensity=.78f;interior.shadows=LightShadows.None;interior.color=new Color(.95f,.95f,.87f);
            foreach(float side in new[]{-1f,1f}) SlopCityArchitecturalMeshes.Tube(train,"Interior_LightTube_"+side,new[]{new Vector3(-5.4f,2.60f,side*.89f),new Vector3(5.4f,2.60f,side*.89f)},.015f,warm);
        }

        private static void BuildStreetDetails(Transform city)
        {
            Transform details=Root("City_StreetDetails",city,Vector3.zero);
            for(int i=0;i<12;i++)
            {
                float x=i%2==0?-20:20, z=-53+(i/2)*19;
                Puddle(details,i,new Vector3(x+(i%3-1)*2.0f,-.086f,z),new Vector2(1.8f+(i%3)*.5f,.55f));
                Box(details,"Drain_Grate",new Vector3(x+4.25f,-.085f,z+1.5f),new Vector3(.48f,.012f,.78f),steel,false);
                for(int slot=0;slot<5;slot++) Box(details,"Drain_Slot",new Vector3(x+4.25f,-.076f,z+1.23f+slot*.13f),new Vector3(.40f,.005f,.035f),rubber,false);
            }
            foreach(float x in new[]{-20f,20f}) foreach(float z in new[]{-20f,20f})
            {
                Transform signal=Root("Traffic_Signal",details,new Vector3(x-4.35f,0,z-4.35f));
                Box(signal,"Signal_Pole",new Vector3(0,2.6f,0),new Vector3(.12f,5.2f,.12f),steel);
                Box(signal,"Signal_Arm",new Vector3(1.3f,5.1f,0),new Vector3(2.7f,.13f,.13f),steel);
                Box(signal,"Signal_Housing",new Vector3(2.5f,4.55f,0),new Vector3(.42f,1.1f,.30f),rubber);
                for(int lens=0;lens<3;lens++) Box(signal,"Traffic_Lens",new Vector3(2.5f,4.25f+lens*.30f,-.16f),new Vector3(.22f,.22f,.035f),lens==0?warm:glass,false);
                Box(signal,"Crossing_Control",new Vector3(0,1.1f,-.13f),new Vector3(.22f,.32f,.12f),rubber);
                Box(signal,"Crossing_Button",new Vector3(0,1.10f,-.2f),new Vector3(.11f,.11f,.02f),warm,false);
                signal.gameObject.AddComponent<SlopCityInteractable>().Configure(SlopCityInteractionKind.Terminal,"Crossing signal");
                GameObject cover=GameObject.CreatePrimitive(PrimitiveType.Cylinder);cover.name="CastIron_Manhole";
                cover.transform.SetParent(details,false);cover.transform.localPosition=new Vector3(x+1.3f,-.082f,z-1.3f);cover.transform.localScale=new Vector3(.85f,.011f,.85f);
                cover.GetComponent<Renderer>().sharedMaterial=steel;Object.DestroyImmediate(cover.GetComponent<Collider>());cover.isStatic=true;
                for(int groove=-2;groove<=2;groove++) Box(details,"Manhole_CastRib",new Vector3(x+1.3f+groove*.13f,-.067f,z-1.3f),new Vector3(.025f,.008f,.61f-Mathf.Abs(groove)*.10f),rubber,false);
            }
        }

        private static void Puddle(Transform parent,int index,Vector3 point,Vector2 size)
        {
            const int sides=32;var vertices=new Vector3[sides+1];var uv=new Vector2[sides+1];var triangles=new int[sides*3];
            vertices[0]=Vector3.zero;uv[0]=new Vector2(.5f,.5f);
            for(int side=0;side<sides;side++)
            {
                float angle=side*Mathf.PI*2/sides;
                float irregular=.79f+.11f*Mathf.Sin(angle*3+index)+.09f*Mathf.Cos(angle*7+index*2);
                vertices[side+1]=new Vector3(Mathf.Sin(angle)*size.x*.5f*irregular,0,Mathf.Cos(angle)*size.y*.5f*irregular);
                uv[side+1]=new Vector2(vertices[side+1].x/size.x+.5f,vertices[side+1].z/size.y+.5f);
                triangles[side*3]=0;triangles[side*3+1]=side+1;triangles[side*3+2]=(side+1)%sides+1;
            }
            Mesh mesh=new Mesh {name="Rainwater_"+index};mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
            string path=Folder+"/Rainwater_"+index+".asset";Mesh stored=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(stored) {EditorUtility.CopySerialized(mesh,stored);Object.DestroyImmediate(mesh);mesh=stored;} else AssetDatabase.CreateAsset(mesh,path);
            Transform puddle=Root("Rain_Puddle",parent,point);puddle.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            Renderer renderer=puddle.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=water;renderer.shadowCastingMode=ShadowCastingMode.Off;puddle.gameObject.isStatic=true;
        }

        private static void BuildBillboard(Transform block,Vector3 point,int channel)
        {
            Transform board=Root("City_Billboard_"+channel,block,point);
            foreach(float x in new[]{-1.85f,1.85f})
            {
                Rounded(board,"Steel_Support",new Vector3(x,4.2f,.12f),new Vector3(.19f,8.4f,.23f),.035f,steel);
                Rounded(board,"Post_Footing",new Vector3(x,.15f,.12f),new Vector3(.43f,.30f,.43f),.035f,concrete);
                foreach(float bolt in new[]{-.13f,.13f}) Rounded(board,"Base_AnchorBolt",new Vector3(x+bolt,.32f,.12f),new Vector3(.035f,.04f,.035f),.006f,steel,false);
            }
            Rounded(board,"Billboard_Frame",new Vector3(0,7.9f,0),new Vector3(6.65f,3.86f,.30f),.070f,rubber);
            AddMediaAnchor(board,new Vector3(0,7.9f,-.17f),Vector3.back,new Vector2(6.4f,3.6f),channel);
            foreach(float y in new[]{6.25f,9.55f}) Box(board,"Rear_WeldedBeam",new Vector3(0,y,.24f),new Vector3(6.36f,.15f,.14f),steel,false);
            for(int brace=0;brace<3;brace++)
            {
                float x=-2.7f+brace*2.7f;
                SlopCityArchitecturalMeshes.Tube(board,"Diagonal_Frame_Brace",new[]{new Vector3(x-1.15f,6.35f,.28f),new Vector3(x+1.15f,9.45f,.28f)},.025f,steel);
            }
            Box(board,"Maintenance_Walkway",new Vector3(0,5.87f,.75f),new Vector3(6.2f,.08f,1.10f),steel,false);
            SlopCityArchitecturalMeshes.Tube(board,"Catwalk_Guardrail",new[]{new Vector3(-3.02f,5.92f,1.22f),new Vector3(-3.02f,6.77f,1.22f),new Vector3(3.02f,6.77f,1.22f),new Vector3(3.02f,5.92f,1.22f)},.025f,steel);
            foreach(float x in new[]{-1.5f,0,1.5f}) SlopCityArchitecturalMeshes.Tube(board,"Catwalk_Rail_Post",new[]{new Vector3(x,5.92f,1.22f),new Vector3(x,6.77f,1.22f)},.018f,steel);
            foreach(float x in new[]{2.12f,2.54f}) SlopCityArchitecturalMeshes.Tube(board,"Service_Ladder_Rail",new[]{new Vector3(x,1.5f,.43f),new Vector3(x,6.6f,.43f)},.024f,steel);
            for(int rung=0;rung<17;rung++) SlopCityArchitecturalMeshes.Tube(board,"Service_Ladder_Rung",new[]{new Vector3(2.12f,1.65f+rung*.285f,.43f),new Vector3(2.54f,1.65f+rung*.285f,.43f)},.018f,steel);
            Rounded(board,"Broadcast_Identification_Plate",new Vector3(0,5.67f,-.04f),new Vector3(6.3f,.27f,.075f),.024f,steel,false);
            Label(board,channel==0?"@machine_publishers":"@1984drh",new Vector3(0,5.68f,-.085f),Vector3.back,.12f);
            SlopCityArchitecturalMeshes.Tube(board,"Monitoring_Camera_Bracket",new[]{new Vector3(2.8f,9.8f,.02f),new Vector3(2.8f,10.05f,-.32f)},.025f,steel);
            Rounded(board,"Monitoring_Camera_Housing",new Vector3(2.8f,10.05f,-.48f),new Vector3(.24f,.15f,.38f),.045f,paint,false);
            SlopCityArchitecturalMeshes.Tube(board,"Monitoring_Camera_Lens",new[]{new Vector3(2.8f,10.05f,-.67f),new Vector3(2.8f,10.05f,-.71f)},.052f,rubber);
            Rounded(board,"Broadcast_ServiceCabinet",new Vector3(0,.56f,0),new Vector3(.42f,1.12f,.32f),.04f,steel);
            Terminal(board,"Billboard_Tuning_Console",new Vector3(0,1.1f,-.25f),Vector3.back,"STREET BROADCAST",SlopCityInteractionKind.Screen,null);
        }

        public static void AddMediaAnchor(Transform parent, Vector3 point, Vector3 outward, Vector2 size, int channel)
        {
            Transform anchor=Root("SlopCityMediaScreenAnchor_Channel"+channel,parent,point);
            anchor.localRotation=Quaternion.LookRotation(outward,Vector3.up);
            anchor.localScale=new Vector3(size.x,size.y,1);
        }

        private static void Terminal(Transform parent,string name,Vector3 point,Vector3 outward,string label,SlopCityInteractionKind kind,Transform destination)
        {
            Transform terminal=Root(name,parent,point);
            terminal.localRotation=Quaternion.LookRotation(outward,Vector3.up);
            Rounded(terminal,"Control_Cabinet",Vector3.zero,new Vector3(.58f,.76f,.18f),.035f,steel);
            Rounded(terminal,"Control_Display",new Vector3(0,.09f,.101f),new Vector3(.43f,.38f,.025f),.01f,rubber,false);
            Rounded(terminal,"Use_Button",new Vector3(0,-.22f,.105f),new Vector3(.15f,.075f,.03f),.012f,warm,false);
            Label(terminal,label.Replace(" / ","\n"),new Vector3(0,.10f,.12f),Vector3.forward,.045f);
            var action=terminal.gameObject.AddComponent<SlopCityInteractable>();
            action.Configure(kind,label,destination==null);
            action.ConfigureInteractionPoint(terminal.TransformPoint(new Vector3(0,0,.13f)));
            if(destination) action.ConfigureDestination(destination);
        }

        private static void Facade(Transform parent,Vector3 point,Vector3 outward,float width,float height,int seed,int columns,int style)
        {
            Transform panel=Root(style==0?"CurtainWall_Panel":"Masonry_WindowBays",parent,point);
            panel.localRotation=Quaternion.LookRotation(-outward,Vector3.up);
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            if(style==0)
            {
                AddWindowQuad(vertices,uv,triangles,-width/2,-height/2,width,height,
                    new Vector2((seed%4)*.25f,(seed%8)*.125f),new Vector2(columns*.25f,height/25.6f));
            }
            else
            {
                float pitch=width/columns,windowWidth=pitch*(style==1?.58f:style==2?.66f:.46f);
                float windowHeight=style==1?1.88f:style==2?2.05f:1.60f;
                int rows=Mathf.Max(1,Mathf.FloorToInt(height/3.2f));
                for(int row=0;row<rows;row++) for(int column=0;column<columns;column++)
                {
                    float x=-width/2+(column+.5f)*pitch-windowWidth/2;
                    float y=-height/2+row*3.2f+.80f;
                    int roomX=(column+seed+row*3)%4,roomY=(row+seed+column*5)%8;
                    // Each opening samples one room; surrounding opaque masonry stays visible.
                    AddWindowQuad(vertices,uv,triangles,x,y,windowWidth,windowHeight,
                        new Vector2(roomX*.25f+.012f,roomY*.125f+.027f),new Vector2(.226f,.090f));
                }
            }
            Mesh mesh=new Mesh {name="Architectural_WindowSurface"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();
            string path=Folder+"/Facade_"+towerSerial+"_"+seed+"_"+outward.x+"_"+outward.z+".asset";
            Mesh stored=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(stored) {EditorUtility.CopySerialized(mesh,stored);Object.DestroyImmediate(mesh);mesh=stored;}
            else AssetDatabase.CreateAsset(mesh,path);
            panel.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            panel.gameObject.AddComponent<MeshRenderer>().sharedMaterial=litGlass;panel.gameObject.isStatic=true;
        }
        private static void AddWindowQuad(List<Vector3> vertices,List<Vector2> uv,List<int> triangles,float x,float y,float width,float height,Vector2 offset,Vector2 span)
        {
            int index=vertices.Count;
            vertices.AddRange(new[]{new Vector3(x,y,0),new Vector3(x+width,y,0),new Vector3(x,y+height,0),new Vector3(x+width,y+height,0)});
            uv.AddRange(new[]{offset,offset+new Vector2(span.x,0),offset+new Vector2(0,span.y),offset+span});
            triangles.AddRange(new[]{index,index+2,index+1,index+2,index+3,index+1});
        }

        private static void RoundedWindow(Transform parent,string name,Vector3 center,Vector2 size,float radius)
        {
            const int perCorner=7;var rim=new List<Vector3>();
            for(int corner=0;corner<4;corner++)
            {
                Vector2 origin=new Vector2((corner==0||corner==3?1:-1)*(size.x/2-radius),(corner<2?1:-1)*(size.y/2-radius));
                for(int step=0;step<perCorner;step++)
                {
                    float angle=(corner*90+step*90f/(perCorner-1))*Mathf.Deg2Rad;
                    rim.Add(new Vector3(origin.x+Mathf.Cos(angle)*radius,origin.y+Mathf.Sin(angle)*radius,0));
                }
            }
            var border=new Vector3[rim.Count+1];
            for(int i=0;i<rim.Count;i++) border[i]=rim[i]+center+Vector3.forward*.021f;
            border[rim.Count]=border[0];
            SlopCityArchitecturalMeshes.Tube(parent,name+"_OpenGasket",border,.030f,rubber);
            var vertices=new Vector3[rim.Count+1];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];var triangles=new int[rim.Count*3];
            vertices[0]=Vector3.zero;uv[0]=new Vector2(.5f,.5f);normals[0]=Vector3.back;
            for(int i=0;i<rim.Count;i++)
            {
                vertices[i+1]=rim[i];normals[i+1]=Vector3.back;uv[i+1]=new Vector2(rim[i].x/size.x+.5f,rim[i].y/size.y+.5f);
                triangles[i*3]=0;triangles[i*3+1]=(i+1)%rim.Count+1;triangles[i*3+2]=i+1;
            }
            Mesh mesh=new Mesh {name="Shaped_Transit_Glazing"};mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateBounds();mesh.RecalculateTangents();
            string path=Folder+"/ShapedTransitGlazing.asset";Mesh stored=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(stored) {Object.DestroyImmediate(mesh);mesh=stored;} else AssetDatabase.CreateAsset(mesh,path);
            Transform pane=Root(name+"_Glazing",parent,center);pane.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            Renderer renderer=pane.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=transitGlass;renderer.shadowCastingMode=ShadowCastingMode.Off;pane.gameObject.isStatic=true;
        }

        private static void CombineDetails(Transform parent,List<GameObject> pieces,Material material,string name)
        {
            var combines=new CombineInstance[pieces.Count];
            for(int i=0;i<pieces.Count;i++) combines[i]=new CombineInstance {mesh=pieces[i].GetComponent<MeshFilter>().sharedMesh,transform=parent.worldToLocalMatrix*pieces[i].transform.localToWorldMatrix};
            Mesh mesh=new Mesh {name=name}; mesh.CombineMeshes(combines,true,true); mesh.RecalculateBounds();mesh.RecalculateTangents();
            string path=Folder+"/TowerDetail_"+parent.name+"_"+name+".asset";
            Mesh stored=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(stored) { EditorUtility.CopySerialized(mesh,stored); Object.DestroyImmediate(mesh); mesh=stored; }
            else AssetDatabase.CreateAsset(mesh,path);
            Transform frames=Root(name,parent,Vector3.zero);
            frames.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            frames.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
            frames.gameObject.isStatic=true;
            foreach(GameObject piece in pieces) Object.DestroyImmediate(piece);
        }

        private static Texture2D Texture(string name,int width,int height,bool emission)
        {
            string path=Folder+"/Surface_"+name+".asset";
            Texture2D existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path); if(existing) return existing;
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,true,emission) {name=name,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=4};
            var colors=new Color[width*height];
            var random=new System.Random(39819);
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                float noise=(float)random.NextDouble(); Color color;
                if(name.StartsWith("CurtainWall"))
                {
                    int cellX=width/4,cellY=height/8;
                    int col=x/cellX,row=y/cellY,px=(x%cellX)*64/cellX,py=(y%cellY)*64/cellY;
                    int room=(col*71+row*37+row*col*13)%23;
                    bool frame=px<2||px>61||py<12||py>61,blind=py%6==0;
                    float light=room<18?0:room<21?.10f:.22f;
                    if(emission) color=frame?Color.black:new Color(light,light*(room%2==0?.92f:.98f),light*.84f)*(blind?.78f:1f);
                    else
                    {
                        float reflect=.28f+(1f-py/64f)*.12f;
                        color=frame?new Color(.26f,.28f,.28f):new Color(reflect*.94f,reflect,reflect*1.05f)*(blind?.94f:1f);
                        if(room>=18&&!frame) color=Color.Lerp(color,new Color(.47f,.44f,.38f),.12f);
                    }
                }
                else
                {
                    float value=.76f+noise*.24f;
                    if(name=="Masonry")
                    {
                        bool seam=y%32<2||(x+(y/32%2)*32)%64<2;
                        if(seam) value*=.67f;
                    }
                    colors[y*width+x]=new Color(value,value,value,1); continue;
                }
                colors[y*width+x]=color;
            }
            texture.SetPixels(colors); texture.Apply(true,false); AssetDatabase.CreateAsset(texture,path); return texture;
        }

        private static Material Material(string name,Color color,float metallic,float smooth,float emission=0)
        {
            string path=Folder+"/Metro_"+name+".mat";
            Material result=AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create=!result; if(create) result=new Material(Shader.Find("Standard"));
            result.name=name; result.color=color; result.enableInstancing=true;
            result.SetFloat("_Metallic",metallic); result.SetFloat("_Glossiness",smooth);
            if(emission>0) ConfigureEmission(result,color*emission,null);
            else
            {
                result.SetColor("_EmissionColor",Color.black);
                result.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                result.DisableKeyword("_EMISSION");
            }
            if(create) AssetDatabase.CreateAsset(result,path); else EditorUtility.SetDirty(result);
            return result;
        }

        private static void ConfigureEmission(Material material,Color color,Texture emissionMap)
        {
            material.SetTexture("_EmissionMap",emissionMap);
            material.SetColor("_EmissionColor",color);
            // Standard's import-time validation drops _EMISSION if EmissiveIsBlack
            // survives from a new/default material. Persist the GI flag along with
            // its HDR color, then enable the local shader keyword as the final step.
            material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
            material.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
        }

        private static Transform Root(string name,Transform parent,Vector3 point)
        { var value=new GameObject(name).transform; value.SetParent(parent,false); value.localPosition=point; return value; }
        private static GameObject Rounded(Transform parent,string name,Vector3 point,Vector3 size,float radius,Material material,bool collision=true)
        {return SlopCityArchitecturalMeshes.RoundedBox(parent,name,point,size,radius,material,collision);}
        private static GameObject Box(Transform parent,string name,Vector3 point,Vector3 size,Material material,bool collision=true)
        {
            GameObject value=GameObject.CreatePrimitive(PrimitiveType.Cube); value.name=name;
            value.transform.SetParent(parent,false); value.transform.localPosition=point; value.transform.localScale=size;
            value.GetComponent<Renderer>().sharedMaterial=material; value.isStatic=true;
            if(!collision) {Object.DestroyImmediate(value.GetComponent<Collider>());value.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;}
            return value;
        }
        private static void Cable(Transform parent,string name,Vector3[] points,float width,Material material)
        {
            Transform cable=Root(name,parent,Vector3.zero); LineRenderer line=cable.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace=false; line.positionCount=points.Length; line.SetPositions(points); line.widthMultiplier=width;
            line.sharedMaterial=material; line.numCornerVertices=3; line.numCapVertices=3;
        }
        private static void Label(Transform parent,string content,Vector3 point,Vector3 outward,float scale)
        {
            Transform sign=Root("City_Sign_"+content.Split('\n')[0],parent,point);
            sign.localRotation=Quaternion.LookRotation(-outward,Mathf.Abs(Vector3.Dot(outward,Vector3.up))>.9f?Vector3.forward:Vector3.up);
            TextMesh label=sign.gameObject.AddComponent<TextMesh>(); label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize=64;label.characterSize=scale*10/64;label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;
            label.text=content;label.color=new Color(.88f,.91f,.86f);
            label.GetComponent<Renderer>().sharedMaterial=label.font.material;
            label.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            sign.gameObject.AddComponent<SlopCityWorldText>().Configure(SlopCityCityBuilder.GetWorldTextMaterial());
        }
    }
}
#endif
