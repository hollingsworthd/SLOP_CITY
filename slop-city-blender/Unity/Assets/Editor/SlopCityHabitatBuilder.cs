#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Original inhabited habitat shell. Cosmetic geometry never changes the authored player/door collision contract.</summary>
    public static class SlopCityHabitatBuilder
    {
        private const string Folder = "Assets/Materials/SlopCityHabitat";
        private static Material shell, panel, trim, rubber, edge, lamp, amber;
        private static int labelIndex;

        public static void Build(Transform lounge)
        {
            if (!lounge) throw new ArgumentNullException(nameof(lounge));
            Transform previous = lounge.Find("SlopCity_HabitatInterior");
            if (previous) Object.DestroyImmediate(previous.gameObject);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Materials", "SlopCityHabitat");
            shell = Material("Habitat_PressureShell", "TransitEnamel", new Color(.34f,.39f,.40f), .27f);
            panel = Material("Habitat_ServicePanel", "TransitEnamel", new Color(.49f,.52f,.50f), .32f);
            trim = Material("Habitat_StructuralMetal", "Metal", new Color(.16f,.20f,.22f), .35f);
            rubber = Material("Habitat_Gasket", "Rubber", new Color(.038f,.052f,.057f), .09f);
            edge = Material("Habitat_BrushedEdge", "Metal", new Color(.43f,.47f,.47f), .48f);
            amber = Material("Habitat_WarningEnamel", "TransitEnamel", new Color(.58f,.38f,.13f), .23f);
            lamp = Material("Habitat_LightDiffuser", "", new Color(.82f,.89f,.90f), .37f);
            lamp.SetColor("_EmissionColor", new Color(.72f,.86f,.9f) * 1.4f); lamp.EnableKeyword("_EMISSION");
            labelIndex = 0;
            Transform root = new GameObject("SlopCity_HabitatInterior").transform;
            root.SetParent(lounge, false);
            Renderer floor = Find(lounge, "Floor_Main");
            Renderer ceiling = Find(lounge, "COL_Ceiling");
            if (!floor || !ceiling) throw new InvalidOperationException("Habitat requires the authored lounge floor and ceiling.");
            Bounds bounds = floor.bounds;
            float ground = bounds.max.y + .04f, top = ceiling.bounds.min.y;
            foreach (string name in new[] { "COL_Wall_Left", "COL_Wall_Front" })
            {
                Renderer wall = Find(lounge, name);
                if (wall) Wall(root, name, wall.bounds, bounds.center, ground, top);
            }
            // The right wall was split around the physical doorway by SceneBuilder.
            GameObject entrance = GameObject.Find("Home_Entrance");
            if (entrance)
                foreach (Renderer wall in entrance.GetComponentsInChildren<Renderer>())
                    if (wall.name == "Home_Wall_North" || wall.name == "Home_Wall_South")
                        Wall(root, wall.name, wall.bounds, bounds.center, ground, top);
            Ceiling(root, bounds, top);
            Window(root, lounge, ground);
            Cabinet(root, lounge);
            DoorDress(root, entrance);
            // Replace flat oversized poster slabs with small service markings; all colliders/interaction anchors stay intact.
            foreach (Renderer item in lounge.GetComponentsInChildren<Renderer>(true))
                if (item.name.StartsWith("HRR_Poster", StringComparison.Ordinal) || item.name.StartsWith("Poster_Title", StringComparison.Ordinal)
                    || item.name.StartsWith("Poster_Subtitle", StringComparison.Ordinal) || item.name.StartsWith("Wall_Record_", StringComparison.Ordinal))
                    item.enabled = false;
            Batch(root);
            AssetDatabase.SaveAssets();
        }

        private static void Wall(Transform root, string id, Bounds wall, Vector3 roomCenter, float bottom, float top)
        {
            bool acrossX = wall.size.x > wall.size.z;
            Vector3 normal = acrossX ? Vector3.forward * Mathf.Sign(roomCenter.z - wall.center.z)
                : Vector3.right * Mathf.Sign(roomCenter.x - wall.center.x);
            float width = acrossX ? wall.size.x : wall.size.z;
            float depth = acrossX ? wall.size.z : wall.size.x;
            Transform plane = new GameObject("Habitat_" + id).transform;
            plane.SetParent(root, false);
            plane.SetPositionAndRotation(new Vector3(wall.center.x, bottom, wall.center.z) + normal * (depth * .5f + .008f), Quaternion.LookRotation(normal));
            float height = top - bottom;
            int count = Mathf.Max(1, Mathf.RoundToInt(width / 1.25f));
            float pitch = width / count;
            for (int i = 0; i < count; i++)
            {
                float x = -width * .5f + (i + .5f) * pitch;
                Box(plane,"PressurePanel",new Vector3(x,height*.5f,0),new Vector3(pitch-.025f,height-.1f,.036f),.012f,shell);
                Box(plane,"RecessGasket",new Vector3(x,height*.52f,.025f),new Vector3(pitch-.13f,height*.66f,.02f),.009f,rubber);
                Box(plane,"ServiceCover",new Vector3(x,height*.52f,.043f),new Vector3(pitch-.17f,height*.66f-.035f,.018f),.007f,panel);
                // Split covers, hinge pads and flush fastening points make this read as assembled hardware.
                Box(plane,"CoverSeam",new Vector3(x,height*.52f+.26f,.055f),new Vector3(pitch-.22f,.012f,.006f),.002f,trim);
                foreach (float side in new[] {-1f,1f})
                {
                    float sx=x+side*(pitch*.5f-.13f);
                    foreach(float y in new[]{.52f,height-.66f})
                    {
                        Box(plane,"FastenerSocket",new Vector3(sx,y,.059f),new Vector3(.037f,.037f,.006f),.006f,rubber);
                        Box(plane,"Fastener",new Vector3(sx,y,.063f),new Vector3(.017f,.017f,.005f),.006f,edge);
                    }
                }
                float serviceY = .21f;
                Box(plane,"KickVentWell",new Vector3(x,serviceY,.025f),new Vector3(pitch-.18f,.19f,.023f),.008f,rubber);
                for(int v=0;v<5;v++) Box(plane,"KickVentFin",new Vector3(x,serviceY-.068f+v*.034f,.043f),new Vector3(pitch-.24f,.013f,.032f),.005f,trim);
                if(i%2==0)
                {
                    Box(plane,"ServicePull",new Vector3(x+pitch*.26f,1.48f,.072f),new Vector3(.048f,.17f,.024f),.012f,trim);
                    Decal(plane,"HAB / "+(++labelIndex).ToString("00"),new Vector3(x-pitch*.29f,2.16f,.064f),.042f,new Color(.19f,.24f,.25f));
                    Box(plane,"SafetyIndex",new Vector3(x-pitch*.25f,.47f,.062f),new Vector3(.17f,.023f,.006f),.002f,amber);
                }
            }
            for(int i=0;i<=count;i++)
            {
                float x=-width*.5f+i*pitch;
                Box(plane,"BulkheadWeb",new Vector3(x,height*.5f,.052f),new Vector3(.055f,height,.072f),.018f,trim);
                Box(plane,"BulkheadSpine",new Vector3(x,height*.5f,.095f),new Vector3(.019f,height-.07f,.014f),.006f,edge);
            }
            Box(plane,"BaseRail",new Vector3(0,.063f,.042f),new Vector3(width,.086f,.073f),.018f,trim);
            Box(plane,"TopServiceTrunk",new Vector3(0,height-.19f,.092f),new Vector3(width,.20f,.17f),.033f,trim);
            Box(plane,"DiffuserRecess",new Vector3(0,height-.27f,.184f),new Vector3(width-.12f,.037f,.017f),.006f,rubber);
            Box(plane,"ContinuousPractical",new Vector3(0,height-.27f,.196f),new Vector3(width-.21f,.014f,.014f),.004f,lamp);
        }

        private static void Ceiling(Transform root, Bounds floor, float top)
        {
            float xmin=floor.min.x+.19f,xmax=floor.max.x-.19f,zmin=floor.min.z+.17f,zmax=floor.max.z-.17f;
            float width=xmax-xmin,depth=zmax-zmin;
            int bands=Mathf.CeilToInt(depth/1.05f);
            for(int i=0;i<bands;i++)
            {
                float z=zmin+(i+.5f)*depth/bands;
                Box(root,"CeilingCassette",new Vector3(floor.center.x,top-.022f,z),new Vector3(width,.026f,depth/bands-.035f),.009f,shell);
                Box(root,"CeilingRecess",new Vector3(floor.center.x,top-.045f,z),new Vector3(width-.54f,.018f,depth/bands-.15f),.007f,panel);
            }
            foreach(float z in new[]{zmin+.18f,floor.center.z-.63f,floor.center.z+.86f,zmax-.18f})
            {
                Box(root,"CeilingCrossmember",new Vector3(floor.center.x,top-.12f,z),new Vector3(width,.16f,.15f),.035f,trim);
                Box(root,"CrossmemberFlange",new Vector3(floor.center.x,top-.207f,z),new Vector3(width-.18f,.022f,.19f),.01f,edge);
                foreach(float side in new[]{-1f,1f})
                {
                    var gusset=Box(root,"CornerGusset",new Vector3(floor.center.x+side*(width*.5f-.16f),top-.26f,z),new Vector3(.43f,.10f,.19f),.026f,trim);
                    gusset.transform.rotation=Quaternion.Euler(0,0,side*38f);
                }
            }
            foreach(float x in new[]{floor.center.x-1.62f,floor.center.x+1.62f})
            {
                Box(root,"LightHousing",new Vector3(x,top-.095f,floor.center.z),new Vector3(.14f,.10f,depth-.45f),.025f,rubber);
                Box(root,"CeilingLight",new Vector3(x,top-.153f,floor.center.z),new Vector3(.048f,.014f,depth-.63f),.006f,lamp);
                SlopCityArchitecturalMeshes.Tube(root,"CeilingSupply",new[]{new Vector3(x+.16f,top-.11f,zmin+.1f),new Vector3(x+.16f,top-.11f,zmax-.1f)},.021f,trim);
            }
        }

        private static void Window(Transform root, Transform lounge, float bottom)
        {
            Renderer sill=Find(lounge,"COL_Window_Sill");
            if(!sill) return;
            Bounds b=sill.bounds;
            Vector3 room=Find(lounge,"Floor_Main").bounds.center;
            Vector3 normal=Vector3.forward*Mathf.Sign(room.z-b.center.z);
            Transform plane=new GameObject("Habitat_WindowServices").transform;
            plane.SetParent(root,false);plane.SetPositionAndRotation(new Vector3(b.center.x,bottom,b.center.z)+normal*(b.size.z*.5f+.02f),Quaternion.LookRotation(normal));
            Box(plane,"WindowServiceBase",new Vector3(0,.37f,0),new Vector3(b.size.x-.2f,.61f,.056f),.016f,shell);
            for(int i=0;i<8;i++)
            {
                float x=(i-3.5f)*.94f;
                Box(plane,"WindowServiceDoor",new Vector3(x,.36f,.047f),new Vector3(.88f,.43f,.035f),.015f,panel);
                Box(plane,"WindowServicePull",new Vector3(x+.29f,.36f,.071f),new Vector3(.026f,.13f,.018f),.008f,trim);
            }
            // Fine mullion overlays preserve the clear exterior sightline instead of adding opaque glass.
            foreach(Renderer post in lounge.GetComponentsInChildren<Renderer>())
            {
                if(!post.name.StartsWith("Window_Frame_Post",StringComparison.Ordinal)) continue;
                Bounds frame=post.bounds;
                Box(root,"WindowPressureMullion",frame.center,new Vector3(.09f,frame.size.y,.16f),.021f,trim);
            }
        }

        private static void Cabinet(Transform root, Transform lounge)
        {
            Renderer source=Find(lounge,"TV_Console");
            Transform seat=lounge.GetComponentsInChildren<Transform>().FirstOrDefault(item=>item.name==SlopCitySceneBuilder.SeatedMarker);
            if(!source||!seat) return;
            Bounds b=source.bounds;
            Vector3 toward=seat.position-b.center;
            Vector3 normal=b.size.x<b.size.z?Vector3.right*Mathf.Sign(toward.x):Vector3.forward*Mathf.Sign(toward.z);
            float width=Mathf.Abs(normal.x)>.5f?b.size.z:b.size.x,depth=Mathf.Abs(normal.x)>.5f?b.size.x:b.size.z;
            Transform cabinet=new GameObject("Habitat_MediaCabinet").transform;cabinet.SetParent(root,false);cabinet.SetPositionAndRotation(b.center,Quaternion.LookRotation(normal));
            source.enabled=false;
            foreach(Renderer drawer in lounge.GetComponentsInChildren<Renderer>())
                if(drawer.name.StartsWith("TV_Console_Door",StringComparison.Ordinal)) drawer.enabled=false;
            Box(cabinet,"EquipmentCabinet",Vector3.zero,new Vector3(width,b.size.y,depth),.039f,trim);
            Box(cabinet,"CabinetTop",new Vector3(0,b.size.y*.5f+.004f,0),new Vector3(width+.025f,.025f,depth+.025f),.012f,edge);
            Box(cabinet,"CabinetFaceSeal",new Vector3(0,.008f,depth*.5f+.007f),new Vector3(width-.09f,b.size.y-.085f,.015f),.006f,rubber);
            for(int i=0;i<2;i++)
            {
                float x=(i-.5f)*(width*.46f);
                Box(cabinet,"StorageDrawer",new Vector3(x,.036f,depth*.5f+.019f),new Vector3(width*.44f,b.size.y-.17f,.023f),.014f,shell);
                Box(cabinet,"RecessedGrip",new Vector3(x,.12f,depth*.5f+.036f),new Vector3(width*.27f,.024f,.013f),.006f,rubber);
                Box(cabinet,"DrawerLatch",new Vector3(x+width*.15f,-.13f,depth*.5f+.04f),new Vector3(.032f,.048f,.013f),.006f,edge);
            }
            Decal(cabinet,"LOCAL RELAY  /  1984",new Vector3(-width*.38f,-b.size.y*.36f,depth*.5f+.032f),.026f,new Color(.57f,.66f,.66f));
        }

        private static void DoorDress(Transform root, GameObject entrance)
        {
            if(!entrance) return;
            foreach(Renderer r in entrance.GetComponentsInChildren<Renderer>())
            {
                if(r.name=="Home_Door_Frame"||r.name=="Home_Door_Header"||r.name=="Home_Door_Leaf"||r.name=="Home_Door_RecessedPanel") r.sharedMaterial=shell;
                else if(r.name=="Home_Door_Handle") r.sharedMaterial=edge;
            }
            Renderer lintel=entrance.GetComponentsInChildren<Renderer>().FirstOrDefault(r=>r.name=="Home_Door_Lintel");
            if(!lintel) return;
            Bounds b=lintel.bounds;
            Box(root,"DoorPressureLintel",new Vector3(b.min.x-.052f,b.min.y+.12f,b.center.z),new Vector3(.12f,.17f,b.size.z+.08f),.027f,trim);
            Box(root,"DoorStatusStrip",new Vector3(b.min.x-.118f,b.min.y+.12f,b.center.z),new Vector3(.012f,.022f,b.size.z-.12f),.004f,lamp);
        }

        private static Renderer Find(Transform root,string name) => root.GetComponentsInChildren<Renderer>(true).FirstOrDefault(item=>item.name==name);
        private static GameObject Box(Transform parent,string name,Vector3 position,Vector3 size,float bevel,Material material)
            => SlopCityArchitecturalMeshes.RoundedBox(parent,name,position,size,bevel,material,false);

        private static Material Material(string name,string surface,Color color,float smoothness)
        {
            string path=Folder+"/"+name+".mat";
            Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Standard")){name=name};AssetDatabase.CreateAsset(material,path);}
            material.color=color;
            if(!string.IsNullOrEmpty(surface)) SlopCitySurfaceLibrary.Apply(material,surface,true);
            material.SetFloat("_Glossiness",smoothness);material.SetFloat("_GlossMapScale",smoothness);
            material.SetFloat("_BumpScale",.16f);material.enableInstancing=true;
            EditorUtility.SetDirty(material);return material;
        }

        private static void Decal(Transform parent,string text,Vector3 position,float size,Color color)
        {
            var obj=new GameObject("Habitat_ServiceMark",typeof(TextMesh));obj.transform.SetParent(parent,false);obj.transform.localPosition=position;
            obj.transform.localRotation=Quaternion.Euler(0,180,0);
            TextMesh label=obj.GetComponent<TextMesh>();label.text=text;label.fontSize=48;label.characterSize=size*10/48;
            label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.color=color;label.anchor=TextAnchor.UpperLeft;
            obj.AddComponent<SlopCityWorldText>().Configure(SlopCityCityBuilder.GetWorldTextMaterial());
        }

        private static void Batch(Transform root)
        {
            MeshFilter[] originals=root.GetComponentsInChildren<MeshFilter>();
            foreach(var group in originals.Where(filter=>filter.sharedMesh&&filter.GetComponent<MeshRenderer>()).GroupBy(filter=>filter.GetComponent<MeshRenderer>().sharedMaterial))
            {
                var parts=group.Select(filter=>new CombineInstance{mesh=filter.sharedMesh,transform=root.worldToLocalMatrix*filter.transform.localToWorldMatrix}).ToArray();
                var mesh=new Mesh{name="Habitat_Combined_"+group.Key.name,indexFormat=IndexFormat.UInt32};
                mesh.CombineMeshes(parts,true,true);mesh.RecalculateBounds();mesh.RecalculateTangents();
                string path=Folder+"/"+mesh.name+".asset";
                Mesh existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject(mesh.name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=group.Key;renderer.reflectionProbeUsage=ReflectionProbeUsage.BlendProbesAndSkybox;go.isStatic=true;
            }
            foreach(MeshFilter filter in originals) {Object.DestroyImmediate(filter.GetComponent<MeshRenderer>());Object.DestroyImmediate(filter);}
        }
    }
}
#endif
