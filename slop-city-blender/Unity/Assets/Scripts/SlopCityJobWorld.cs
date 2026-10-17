using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>Shared geometry for physical work: no media players, per-device materials or frame scans.</summary>
    public sealed class SlopCityJobWorld : MonoBehaviour
    {
        public const int ControlCount = 8;
        public sealed class Site
        {
            public Transform root, movingPart, gauge, treatment, poster, bench, actuator;
            public Transform[] litter = new Transform[3], guard = new Transform[3], hose = new Transform[3];
            public SlopCityJobDevice[] controls = new SlopCityJobDevice[ControlCount];
            public Renderer[] indicators = new Renderer[3];
            public Transform[] pieces = new Transform[3];
            public Light[] lamps = new Light[3];
            public TextMesh status;
            public string dustyId;
            public Vector3 vehicleStaging,dustyLocalPosition;
            public Quaternion dustyLocalRotation;
            public Transform dustyTarget;
            public SlopCityJobDevice remoteControl;
            public Transform remotePane,arrivalRoot,installedRoot;
        }
        private readonly Site[] sites = new Site[40];
        private readonly Collider[] overlaps = new Collider[64];
        private readonly RaycastHit[] groundHits = new RaycastHit[32];
        private MaterialPropertyBlock properties;
        private SlopCitySandbox owner;
        private Vector3 candidateCarStaging;
        public Site Get(int mechanic) => mechanic >= 0 && mechanic < sites.Length ? sites[mechanic] : null;
        public static SlopCityJobWorld Ensure(SlopCitySandbox sandbox)
        {
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<SlopCityJobWorld>(FindObjectsSortMode.None))
                if (candidate.owner == sandbox) return candidate;
            var host = new GameObject("SlopCity_Physical_Worksites");
            var city = GameObject.Find("SlopCity_NineBlocks"); if (city) host.transform.SetParent(city.transform, false);
            var world = host.AddComponent<SlopCityJobWorld>(); world.owner = sandbox;
            SlopCityInteriorGeometry.Initialize();
            for (int i = 0; i < sitesLength; i++) world.Build(i);
            return world;
        }
        public void Release(SlopCitySandbox expectedOwner)
        {
            if(!ReferenceEquals(owner,expectedOwner))return;
            foreach(var device in UnityEngine.Object.FindObjectsByType<SlopCityJobDevice>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(device.World==this&&device.External){if(Application.isPlaying)Destroy(device);else DestroyImmediate(device);}
            gameObject.SetActive(false);owner=null;
            if(Application.isPlaying)Destroy(gameObject);else DestroyImmediate(gameObject);
        }
        private const int sitesLength = 40;
        private void Build(int mechanic)
        {
            Vector3 position;
            if (!FindPlace(mechanic, out position))
            { Debug.LogWarning("No clear physical work site for " + SlopCityJobCatalog.All[mechanic].id); return; }
            var site = new Site(); sites[mechanic] = site;
            site.root = Root(transform, "CityWork_" + SlopCityJobCatalog.All[mechanic].id, position);
            site.root.position = position;
            if(mechanic==13||mechanic==18)
            {
                site.vehicleStaging=candidateCarStaging;
                Box(site.root,"Roadside_Loading_Mark",candidateCarStaging-position+Vector3.up*.012f,new Vector3(4.1f,.008f,1.9f),SlopCityInteriorGeometry.Accent,false);
            }
            Box(site.root, "Service_Plinth", new Vector3(0,.045f,0), new Vector3(2.8f,.09f,1.45f), SlopCityInteriorGeometry.Floor, true);
            site.status = Label(site.root, SlopCityJobCatalog.All[mechanic].title, new Vector3(-1.3f,1.7f,.53f), .065f);
            // Eight separate reachable controls. Their bodies/positions change below by mechanism.
            for (int slot = 0; slot < ControlCount; slot++)
            {
                Vector3 point = new Vector3(-1.16f + (slot % 4) * .76f, .42f + (slot / 4) * .47f, -.43f);
                GameObject control = Box(site.root, "Work_Control_" + slot, point, new Vector3(.43f,.26f,.18f), SlopCityInteriorGeometry.Metal, true);
                site.controls[slot] = control.AddComponent<SlopCityJobDevice>();
                site.controls[slot].Configure(this, mechanic, slot, "Work control " + slot);
            }
            int form = Form(mechanic);
            if (form == 0) // letter supply tray; actual district mailboxes are used as destinations
                for (int i = 0; i < 3; i++)
                {
                    site.pieces[i] = Box(site.root, "Addressed_Letter_" + (i+1), new Vector3(-.86f+i*.86f,1.1f,.25f), new Vector3(.45f,.05f,.3f), SlopCityInteriorGeometry.Wall,false).transform;
                    Label(site.pieces[i], "TO " + (i+1), new Vector3(-.2f,.08f,-.1f), .046f);
                }
            else if (form == 1) // complete removable window and its frame
            {
                for (int side = -1; side <= 1; side+=2)
                {
                    Box(site.root,"Window_Frame",new Vector3(side*.66f,1.05f,.3f),new Vector3(.08f,1.35f,.12f),SlopCityInteriorGeometry.Metal,true);
                    Box(site.root,"Window_Frame",new Vector3(0,1.05f+side*.65f,.3f),new Vector3(1.4f,.08f,.12f),SlopCityInteriorGeometry.Metal,true);
                }
                site.movingPart=Box(site.root,"Removable_Window",new Vector3(0,1.05f,.31f),new Vector3(1.2f,1.2f,.035f),SlopCityInteriorGeometry.Glass,true).transform;
                Dusty(site,mechanic,site.movingPart.gameObject,mechanic==6||mechanic==37?"panel":"window",mechanic==6||mechanic==37?"Removable wall panel":"Work window");
            }
            else if (form == 2) // live lamp circuit, actual lights plus emission
                for (int i = 0; i < 3; i++)
                {
                    Shape(site.root,"Lamp_Stem",new Vector3(-.82f+i*.82f,.95f,.25f),new Vector3(.055f,.8f,.055f),SlopCityInteriorGeometry.Metal,SlopCityInteriorGeometry.UnitCylinder,false);
                    GameObject lens=Shape(site.root,"Lamp_Lens",new Vector3(-.82f+i*.82f,1.8f,.25f),Vector3.one*.24f,SlopCityInteriorGeometry.Accent,SlopCityInteriorGeometry.UnitSphere,false);
                    site.indicators[i]=lens.GetComponent<Renderer>();
                    var lamp=lens.AddComponent<Light>(); lamp.type=LightType.Point;lamp.shadows=LightShadows.None;lamp.range=2.7f;lamp.intensity=.8f;site.lamps[i]=lamp;
                    if(i==0) Dusty(site,mechanic,lens,"lamp","Lamp lens");
                }
            else if (form == 3) // fuses/terminal with three actual removable contacts
            {
                Box(site.root,"Terminal_Case",new Vector3(0,1.05f,.36f),new Vector3(2.25f,.9f,.24f),SlopCityInteriorGeometry.Wall,true);
                for(int i=0;i<3;i++) site.pieces[i]=Box(site.root,"Fuse_"+i,new Vector3(-.65f+i*.65f,1.15f,.18f),new Vector3(.27f,.47f,.15f),SlopCityInteriorGeometry.Accent,false).transform;
            }
            else if (form == 4) // functional valve, measured container and visible flow column
            {
                Shape(site.root,"Water_Pipe",new Vector3(-.6f,.9f,.3f),new Vector3(.18f,.85f,.18f),SlopCityInteriorGeometry.Metal,SlopCityInteriorGeometry.UnitCylinder,true);
                site.movingPart=Shape(site.root,"Valve_Wheel",new Vector3(-.6f,1.35f,.15f),new Vector3(.45f,.04f,.45f),SlopCityInteriorGeometry.Accent,SlopCityInteriorGeometry.UnitCylinder,false).transform;
                site.movingPart.localRotation=Quaternion.Euler(90,0,0);
                Box(site.root,"Graduated_Container",new Vector3(.6f,.56f,.3f),new Vector3(.55f,.8f,.52f),SlopCityInteriorGeometry.Glass,false);
                site.gauge=Box(site.root,"Actual_Water_Level",new Vector3(.6f,.19f,.3f),new Vector3(.43f,.02f,.43f),SlopCityInteriorGeometry.Accent,false).transform;
                for(int i=0;i<5;i++) Label(site.root,(i*25).ToString(),new Vector3(.94f,.2f+i*.18f,.21f),.035f);
            }
            else if (form == 5) // real portable bench, including its seat and hidden lost-item tray
            {
                site.bench=Root(site.root,"Portable_Bench",new Vector3(0,.6f,.25f));
                for(int i=0;i<4;i++) Box(site.bench,"Timber_SeatSlat",new Vector3(0,0,-.2f+i*.13f),new Vector3(1.8f,.09f,.105f),SlopCityInteriorGeometry.Wood,true);
                Box(site.bench,"Timber_BackSlat",new Vector3(0,.34f,.33f),new Vector3(1.8f,.48f,.09f),SlopCityInteriorGeometry.Wood,true);
                Dusty(site,mechanic,site.bench.gameObject,"bench","Portable bench");
                site.movingPart=Box(site.root,"Lost_Item_Under_Bench",new Vector3(.47f,.17f,.3f),new Vector3(.15f,.12f,.12f),SlopCityInteriorGeometry.Accent,true).transform;
            }
            else if (form == 6) // material bays: item count visibly changes each separate stack
                for(int i=0;i<3;i++)
                {
                    Box(site.root,"Material_Intake_"+i,new Vector3(-.88f+i*.88f,.36f,.3f),new Vector3(.65f,.55f,.55f),SlopCityInteriorGeometry.Metal,true);
                    site.pieces[i]=Box(site.root,"Material_Load_"+i,new Vector3(-.88f+i*.88f,.73f,.3f),new Vector3(.45f,.2f,.4f),i==0?SlopCityInteriorGeometry.Metal:i==1?SlopCityInteriorGeometry.Glass:SlopCityInteriorGeometry.Wood,false).transform;
                }
            else if (form == 7) // planting, real hose and removable guard slats
                for(int i=0;i<3;i++)
                {
                    Box(site.root,"Planting_Bed_"+i,new Vector3(-.86f+i*.86f,.26f,.3f),new Vector3(.65f,.45f,.6f),SlopCityInteriorGeometry.Wood,true);
                    site.pieces[i]=Shape(site.root,"Living_Foliage_"+i,new Vector3(-.86f+i*.86f,.9f,.3f),new Vector3(.4f,.6f,.4f),SlopCityInteriorGeometry.Accent,SlopCityInteriorGeometry.UnitSphere,false).transform;
                }
            else if (form == 8) // protected notice / new poster in a real frame
            {
                Box(site.root,"Poster_Frame",new Vector3(0,1.05f,.38f),new Vector3(1.75f,1.2f,.1f),SlopCityInteriorGeometry.Wood,true);
                site.poster=Box(site.root,"Protected_Poster_Face",new Vector3(0,1.05f,.31f),new Vector3(1.59f,1.06f,.025f),SlopCityInteriorGeometry.Wall,false).transform;
                Label(site.poster,"NEIGHBOURS MAKE\nTHIS CITY\n\nTURN THE AMBER VALVE",new Vector3(-.71f,.4f,-.021f),.08f);
                site.movingPart=Box(site.root,"Dusty_Notice_Cover",new Vector3(0,1.05f,.275f),new Vector3(1.59f,1.06f,.015f),SlopCityInteriorGeometry.Glass,false).transform;
                Dusty(site,mechanic,site.movingPart.gameObject,"notice_cover","Dusty notice cover");
            }
            else // antenna / alignment pointer; no new decoder or screen
            {
                site.movingPart=Root(site.root,"Receiver_Antenna",new Vector3(0,.9f,.3f));
                Box(site.movingPart,"Antenna_Mast",new Vector3(0,.35f,0),new Vector3(.08f,.7f,.08f),SlopCityInteriorGeometry.Metal,true);
                Box(site.movingPart,"Directional_Receiver",new Vector3(0,.65f,.25f),new Vector3(.5f,.15f,.7f),SlopCityInteriorGeometry.Accent,false);
                site.treatment=Box(site.root,"Receiver_Source_Marker",new Vector3(1.05f,1.45f,.4f),Vector3.one*.18f,SlopCityInteriorGeometry.Accent,false).transform;
            }
            BuildExtras(mechanic,site);
        }
        private void BuildExtras(int mechanic,Site site)
        {
            if(mechanic==5)
            {
                for(int i=0;i<3;i++)
                {
                    site.pieces[i]=Shape(site.root,"Connected_Branch_"+i,new Vector3(-.85f+i*.85f,.9f,.4f),new Vector3(.12f,.7f,.12f),SlopCityInteriorGeometry.Metal,SlopCityInteriorGeometry.UnitCylinder,true).transform;
                    Label(site.root,"LINE "+(i+1),new Vector3(-1.1f+i*.85f,1.65f,.35f),.05f);
                }
                Box(site.root,"Common_Pressure_Main",new Vector3(0,.35f,.4f),new Vector3(2.1f,.15f,.15f),SlopCityInteriorGeometry.Metal,true);
            }
            if(mechanic==19)for(int i=0;i<3;i++)site.litter[i]=Box(site.root,"Loose_Planter_Litter_"+i,new Vector3(-.86f+i*.86f,.55f,.18f),new Vector3(.21f,.17f,.19f),SlopCityInteriorGeometry.Metal,false).transform;
            if(mechanic==39)for(int i=0;i<3;i++)site.guard[i]=Box(site.root,"Fitted_Guard_Slat_"+i,new Vector3(-.86f+i*.86f,.66f,-.05f),new Vector3(.12f,1.0f,.09f),SlopCityInteriorGeometry.Wood,true).transform;
            if(mechanic==21)for(int i=0;i<3;i++)site.hose[i]=Box(site.root,"Connected_Hose_"+i,new Vector3(-.8f+i*.8f,.12f,.2f),new Vector3(.85f,.055f,.055f),SlopCityInteriorGeometry.Accent,false).transform;
            if(mechanic==23)Box(site.root,"Clear_Bench_Placement_Pad",new Vector3(.6f,.11f,.25f),new Vector3(1.9f,.015f,.85f),SlopCityInteriorGeometry.Accent,false);
            if(mechanic==34)
            {
                site.controls[0].gameObject.SetActive(false);
                site.controls[0]=site.movingPart.gameObject.AddComponent<SlopCityJobDevice>();site.controls[0].Configure(this,mechanic,0,"Owner-tagged object under the bench");
            }
            if(mechanic==35)
            {
                var bulb=Shape(site.root,"Powered_Utility_Lamp",new Vector3(1f,1.65f,.35f),Vector3.one*.18f,SlopCityInteriorGeometry.Accent,SlopCityInteriorGeometry.UnitSphere,false);
                site.lamps[0]=bulb.AddComponent<Light>();site.lamps[0].range=3f;site.lamps[0].shadows=LightShadows.None;site.indicators[0]=bulb.GetComponent<Renderer>();
            }
            if(mechanic==36)
            {
                site.actuator=Shape(site.root,"Requested_Amber_Valve",new Vector3(1.08f,.65f,.12f),new Vector3(.3f,.035f,.3f),SlopCityInteriorGeometry.Accent,SlopCityInteriorGeometry.UnitCylinder,false).transform;
                site.gauge=Box(site.root,"Requested_Valve_Flow",new Vector3(1.08f,.2f,.12f),new Vector3(.12f,.02f,.12f),SlopCityInteriorGeometry.Accent,false).transform;
            }
        }
        public void ShowMailboxContent(Transform mailbox,bool parcel)
        {
            string name=parcel?"Delivered_Parcel":"Delivered_Letters";if(mailbox.Find(name))return;
            Box(mailbox,name,new Vector3(0,.18f,-.14f),parcel?new Vector3(.19f,.15f,.14f):new Vector3(.25f,.02f,.12f),SlopCityInteriorGeometry.Wall,false);
        }
        public void CreateArrivalSocket(int mechanic,Transform destination,bool roof)
        {
            if(!destination)return;CreateArrivalSocket(mechanic,destination.position,destination.rotation,roof);
        }
        public void CreateArrivalSocket(int mechanic,Vector3 position,Quaternion rotation,bool roof)
        {
            var site=Get(mechanic);if(site==null)return;
            EnsureInstalledAssembly(site,mechanic);
            if(site.remoteControl)
            {
                site.remoteControl.transform.parent.SetPositionAndRotation(position+rotation*(roof?Vector3.right*3f:-Vector3.forward*1.15f)+Vector3.up*1.05f,rotation);
                return;
            }
            var root=Root(transform,"Arrival_"+SlopCityJobCatalog.All[mechanic].id,Vector3.zero);site.arrivalRoot=root;
            root.position=position+rotation*(roof?Vector3.right*3f:-Vector3.forward*1.15f)+Vector3.up*1.05f;
            root.rotation=rotation;
            for(int side=-1;side<=1;side+=2)
            {
                Box(root,"Replacement_Frame",new Vector3(side*.66f,0,0),new Vector3(.08f,1.35f,.12f),SlopCityInteriorGeometry.Metal,true);
                Box(root,"Replacement_Frame",new Vector3(0,side*.65f,0),new Vector3(1.4f,.08f,.12f),SlopCityInteriorGeometry.Metal,true);
            }
            var control=Box(root,roof?"Roof_Replacement_Socket":"Platform_Window_Socket",new Vector3(-.82f,0,-.04f),new Vector3(.2f,.25f,.12f),SlopCityInteriorGeometry.Metal,true);
            site.remoteControl=control.AddComponent<SlopCityJobDevice>();site.remoteControl.Configure(this,mechanic,1,roof?"Install carried roof panel":"Install train-carried pane");
            Label(root,roof?"PANEL SOCKET":"WINDOW SOCKET",new Vector3(-.6f,.8f,-.05f),.065f);
        }
        private void EnsureInstalledAssembly(Site site,int mechanic)
        {
            if(site.installedRoot)return;bool roof=mechanic==37;
            site.installedRoot=Root(transform,"Completed_Installation_"+SlopCityJobCatalog.All[mechanic].id,Vector3.zero);
            for(int side=-1;side<=1;side+=2)
            {
                Box(site.installedRoot,"Installed_Frame",new Vector3(side*.66f,0,0),new Vector3(.08f,1.35f,.12f),SlopCityInteriorGeometry.Metal,true);
                Box(site.installedRoot,"Installed_Frame",new Vector3(0,side*.65f,0),new Vector3(1.4f,.08f,.12f),SlopCityInteriorGeometry.Metal,true);
            }
            site.remotePane=Box(site.installedRoot,roof?"Installed_Roof_Panel":"Installed_Window_Pane",Vector3.zero,new Vector3(1.2f,1.2f,.04f),roof?SlopCityInteriorGeometry.Metal:SlopCityInteriorGeometry.Glass,true).transform;
            site.remotePane.gameObject.SetActive(false);site.installedRoot.gameObject.SetActive(false);
        }
        private void Dusty(Site site,int mechanic,GameObject target,string category,string label)
        {
            site.dustyTarget=target.transform;site.dustyLocalPosition=target.transform.localPosition;site.dustyLocalRotation=target.transform.localRotation;
            site.dustyId="job-world/"+SlopCityJobCatalog.All[mechanic].id+"/"+category;
            owner.RegisterJobSalvage(target,site.dustyId,category,label,12,true);
        }
        private static int Form(int i)
        {
            if(i==0||i==2) return 0;if(i==1||i==6||i==15||i==37) return 1;
            if(i==3||i==9||i==11||i==27) return 2;if(i==7||i==10||i==4) return 3;
            if(i==5||i==8||i==13||i==22||i==30||i==32||i==35) return 4;
            if(i==12||i==23||i==34||i==38) return 5;if(i==14||i==16||i==18) return 6;
            if(i==19||i==20||i==21||i==24||i==31||i==33||i==39) return 7;
            if(i==17||i==36) return 8;return 9;
        }
        public void Render(int mechanic,SlopCitySandbox.UniqueJobRecord state,string instruction,string[] labels)
        {
            Site site=Get(mechanic);if(site==null)return;
            site.status.text=SlopCityJobCatalog.All[mechanic].title+"\n"+instruction;
            for(int i=0;i<ControlCount;i++)
            {
                bool enabled=labels!=null&&i<labels.Length&&!string.IsNullOrEmpty(labels[i]);
                if(mechanic==34&&i==0&&(state.issued&1)!=0)enabled=false;
                if((mechanic==1||mechanic==37)&&i==1)enabled=false;
                if((mechanic==0||mechanic==2)&&i>=1&&i<=3)enabled=false;
                site.controls[i].gameObject.SetActive(enabled);
                if(enabled)site.controls[i].GetComponent<SlopCityInteractable>().Configure(SlopCityInteractionKind.Inspect,labels[i],true);
            }
            if(site.movingPart&&Form(mechanic)==4)site.movingPart.localRotation=Quaternion.Euler(90,0,state.values[0]*3.6f);
            if(site.movingPart&&Form(mechanic)==9)site.movingPart.localRotation=Quaternion.Euler(0,state.values[0]*15f,0);
            if(site.gauge){float f=Mathf.Clamp01(state.values[1]/100f);site.gauge.localScale=new Vector3(.43f,Mathf.Max(.015f,f*.72f),.43f);site.gauge.localPosition=new Vector3(.6f,.19f+f*.36f,.3f);}
            for(int i=0;i<3;i++)
            {
                if(site.pieces[i])
                {
                    if(Form(mechanic)==3)Tint(site.pieces[i].GetComponent<Renderer>(),state.values[i+2]>0?new Color(.2f,.9f,.3f):new Color(.65f,.12f,.05f));
                    if(Form(mechanic)==6)site.pieces[i].localScale=new Vector3(.45f,Mathf.Max(.04f,state.values[i+2]*.12f),.4f);
                    if(Form(mechanic)==7)Tint(site.pieces[i].GetComponent<Renderer>(),Color.Lerp(new Color(.7f,.5f,.1f),new Color(.15f,.8f,.25f),Mathf.Clamp01(state.values[i+2]/100f)));
                    if(Form(mechanic)==0)Tint(site.pieces[i].GetComponent<Renderer>(),(state.values[2]&(1<<i))!=0?new Color(.2f,.8f,.3f):Color.white);
                }
                if(site.lamps[i]){bool on=(state.values[2]&(1<<i))!=0;site.lamps[i].enabled=on;site.lamps[i].intensity=.8f*Mathf.Clamp01((state.values[3]+20)/100f);Tint(site.indicators[i],on?new Color(1f,.9f,.45f):new Color(.12f,.12f,.1f));}
            }
            if(mechanic==1||mechanic==37)
            {
                if((state.installed&1)!=0)
                {
                    EnsureInstalledAssembly(site,mechanic);Quaternion rotation=state.installedRotation;
                    Vector3 position=state.installedPosition+rotation*(mechanic==37?Vector3.right*3f:-Vector3.forward*1.15f)+Vector3.up*1.05f;
                    site.installedRoot.SetPositionAndRotation(position,rotation);site.installedRoot.gameObject.SetActive(true);site.remotePane.gameObject.SetActive(true);
                }
                else if(site.installedRoot){site.remotePane.gameObject.SetActive(false);site.installedRoot.gameObject.SetActive(false);}
                if(site.arrivalRoot)foreach(Transform part in site.arrivalRoot)if(part.name=="Replacement_Frame")part.gameObject.SetActive((state.installed&1)==0||Vector3.Distance(site.arrivalRoot.position,site.installedRoot.position)>.01f);
            }
            if(site.poster)site.poster.gameObject.SetActive(mechanic!=17||state.values[0]>0||(state.installed&1)!=0);
            if(mechanic==36&&site.poster)foreach(var lettering in site.poster.GetComponentsInChildren<TextMesh>(true))lettering.GetComponent<Renderer>().enabled=owner.SalvageCleanStrokes(site.dustyId)>=4;
            if(site.actuator)site.actuator.localRotation=Quaternion.Euler(90,0,state.values[0]*.9f);
            if(mechanic==35&&site.lamps[0]){site.lamps[0].enabled=state.values[3]>=2&&state.values[2]+state.values[3]<=6;site.lamps[0].intensity=Mathf.Min(1f,state.values[3]*.2f);}
            if(mechanic==5)for(int i=0;i<3;i++)if(site.pieces[i])Tint(site.pieces[i].GetComponent<Renderer>(),Color.Lerp(Color.red,Color.green,state.values[i+2]/100f));
            for(int i=0;i<3;i++)
            {
                if(site.litter[i])site.litter[i].gameObject.SetActive((state.values[5]&(1<<i))==0);
                if(site.guard[i]){site.guard[i].gameObject.SetActive(((state.values[5]|state.installed)&(1<<i))!=0);if(site.guard[i].gameObject.activeSelf)Tint(site.guard[i].GetComponent<Renderer>(),(state.values[5]&(1<<i))!=0?Color.white:new Color(.63f,.59f,.5f));}
                if(site.hose[i])site.hose[i].gameObject.SetActive(state.values[5]==3);
            }
        }
        private void Tint(Renderer renderer,Color color)
        {if(!renderer)return;if(properties==null)properties=new MaterialPropertyBlock();renderer.GetPropertyBlock(properties);properties.SetColor("_Color",color);properties.SetColor("_BaseColor",color);SlopCitySalvageItem.ComposeFinish(renderer,properties,color);renderer.SetPropertyBlock(properties);}
        private bool FindPlace(int mechanic,out Vector3 place)
        {
            Vector3 preferred=SlopCityDenseLayout.DistrictPoint(new Vector3(((mechanic%9)%3-1)*40,0,((mechanic%9)/3-1)*40));
            // One deterministic pass, no per-frame scene queries. Existing doors/solid architecture stay clear.
            bool carWork=mechanic==13||mechanic==18;
            int groundPass=0,bodyPass=0,standingPass=0,doorPass=0,crowdPass=0;
            for(int ring=0;ring<3;ring++)for(int x=0;x<(carWork?23:12);x++)for(int z=0;z<(carWork?1:8);z++)
            {
                Vector3 point=preferred+new Vector3(-27+x*(carWork?2.5f:5f)+ring*.7f,1.5f,carWork?SlopCityDenseLayout.PavementSouth-1.7f:-11+z*5f);
                int hits=Physics.RaycastNonAlloc(point,Vector3.down,groundHits,3f,~0,QueryTriggerInteraction.Ignore);
                bool found=false;float height=float.NegativeInfinity;
                for(int h=0;h<hits;h++)if(groundHits[h].normal.y>.9f&&groundHits[h].point.y>height){height=groundHits[h].point.y;found=true;}
                if(!found||Mathf.Abs(height)>.6f)continue;point.y=height+.02f;groundPass++;
                int count=Physics.OverlapBoxNonAlloc(point+new Vector3(0,1.05f,carWork?-.825f:-.65f),new Vector3(1.7f,1f,carWork?1.575f:1.75f),overlaps,Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
                if(count==overlaps.Length)continue;bool clear=true;
                for(int h=0;h<count;h++)if(overlaps[h]&&overlaps[h].bounds.max.y>point.y+.08f){clear=false;break;}
                if(!clear)continue;bodyPass++;
                if(!FrontSupported(point,carWork?.24f:.12f))continue;standingPass++;
                if(!ClearDoors(point))continue;doorPass++;
                if(!ClearCrowd(point)||!ClearCrowd(point-Vector3.forward*1.9f))continue;crowdPass++;
                if(carWork&&!TryRoadsideAccess(point,preferred,out candidateCarStaging))continue;
                place=point;return true;
            }
            if(carWork)Debug.LogWarning("Roadside placement rejected "+mechanic+": ground="+groundPass+", body="+bodyPass+", standing="+standingPass+", doors="+doorPass+", crowd="+crowdPass);
            place=default;return false;
        }
        private bool TryRoadsideAccess(Vector3 point,Vector3 district,out Vector3 staging)
        {
            // Authored shared carriageway: south pavement ends at -13.1m and
            // the previous row's north pavement ends at -21.7m. Keep the car in that lane.
            float streetZ=district.z+(SlopCityDenseLayout.PavementSouth+SlopCityDenseLayout.PavementNorth-SlopCityDenseLayout.RowPitch)*.5f;
            foreach(float shift in CarBayOffsets)
            {
                Vector3 bay=new Vector3(point.x+shift,point.y,streetZ);
                if(!TryCarBayGround(bay,out bay)||Vector3.Distance(bay,point)>7.5f||!CarBaySupported(bay))continue;
                if(!ClearVolume(bay+Vector3.up*.95f,new Vector3(2.1f,.75f,1.05f),bay.y+.2f))continue;
                // A real departure in at least one road direction, not an isolated pocket.
                if(!ClearVolume(bay+new Vector3(2,.95f,0),new Vector3(4.1f,.75f,1.05f),bay.y+.2f)
                    &&!ClearVolume(bay+new Vector3(-2,.95f,0),new Vector3(4.1f,.75f,1.05f),bay.y+.2f))continue;
                bool connected=true;Vector3 front=point-Vector3.forward*1.9f;
                for(int sample=0;sample<=5;sample++)
                    if(!ClearVolume(Vector3.Lerp(front,bay,sample/5f)+Vector3.up*.95f,new Vector3(.26f,.75f,.26f),point.y+.08f)){connected=false;break;}
                if(!connected)continue;staging=bay;return true;
            }
            staging=default;return false;
        }
        private static readonly float[] CarBayOffsets={0,-2,2,-4,4};
        private bool TryCarBayGround(Vector3 candidate,out Vector3 ground)
        {
            int count=Physics.RaycastNonAlloc(candidate+Vector3.up*.4f,Vector3.down,groundHits,1f,~0,QueryTriggerInteraction.Ignore);
            bool found=false;float height=float.NegativeInfinity;
            if(count<groundHits.Length)for(int i=0;i<count;i++)
                if(groundHits[i].normal.y>.9f&&groundHits[i].point.y<=candidate.y+.1f&&groundHits[i].point.y>height){height=groundHits[i].point.y;found=true;}
            ground=candidate;if(!found)return false;ground.y=height+.02f;return true;
        }
        private bool CarBaySupported(Vector3 bay)
        {
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
            {
                int count=Physics.RaycastNonAlloc(bay+new Vector3(x*1.8f,.4f,z*.8f),Vector3.down,groundHits,.8f,~0,QueryTriggerInteraction.Ignore);
                if(count==groundHits.Length)return false;bool supported=false;
                for(int h=0;h<count;h++)if(groundHits[h].normal.y>.9f&&Mathf.Abs(groundHits[h].point.y-bay.y)<.12f){supported=true;break;}
                if(!supported)return false;
            }
            return true;
        }
        private bool ClearVolume(Vector3 center,Vector3 halfExtents,float groundTop)
        {
            int count=Physics.OverlapBoxNonAlloc(center,halfExtents,overlaps,Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length)return false;
            for(int i=0;i<count;i++)if(overlaps[i]&&overlaps[i].bounds.max.y>groundTop)return false;
            return true;
        }
        private bool FrontSupported(Vector3 point,float maximumStep)
        {
            // Reserve an ordinary standing strip in front, not merely the device footprint.
            for(int sample=-1;sample<=1;sample++)
            {
                Vector3 feet=point+new Vector3(sample*1.05f,0,-1.9f);bool supported=false;
                int count=Physics.RaycastNonAlloc(feet+Vector3.up*.4f,Vector3.down,groundHits,.8f,~0,QueryTriggerInteraction.Ignore);
                if(count==groundHits.Length)return false;
                for(int h=0;h<count;h++)if(groundHits[h].normal.y>.9f&&Mathf.Abs(groundHits[h].point.y-point.y)<maximumStep){supported=true;break;}
                if(!supported)return false;
            }
            return true;
        }
        private static bool ClearDoors(Vector3 point)
        {foreach(var door in UnityEngine.Object.FindObjectsByType<SlopCityDoor>(FindObjectsSortMode.None))if(Mathf.Abs(door.InteractionPoint.y-point.y)<3f&&Vector2.Distance(new Vector2(point.x,point.z),new Vector2(door.InteractionPoint.x,door.InteractionPoint.z))<3f)return false;return true;}
        private static bool ClearCrowd(Vector3 point)
        {
            var crowd=SlopCityCrowd.Instance;if(!crowd)return true;
            foreach(var route in crowd.Routes)if(route?.nodes!=null)foreach(var node in route.nodes)
            {
                if(node==null||Mathf.Abs(node.position.y-point.y)>.8f)continue;
                if(Vector2.Distance(new Vector2(node.position.x,node.position.z),new Vector2(point.x,point.z))<2f)return false;
                if(node.neighbors==null)continue;
                foreach(int index in node.neighbors)
                {
                    if(index<0||index>=route.nodes.Length||route.nodes[index]==null)continue;
                    Vector3 a=node.position,b=route.nodes[index].position,d=b-a;float t=d.sqrMagnitude>.0001f?Mathf.Clamp01(Vector3.Dot(point-a,d)/d.sqrMagnitude):0;
                    if((point-(a+d*t)).sqrMagnitude<4f)return false;
                }
            }
            return true;
        }
        private static Transform Root(Transform parent,string name,Vector3 position)
        {var item=new GameObject(name).transform;item.SetParent(parent,false);item.localPosition=position;return item;}
        private static GameObject Box(Transform parent,string name,Vector3 position,Vector3 size,Material material,bool collision)
            =>Shape(parent,name,position,size,material,SlopCityInteriorGeometry.UnitCube,collision);
        private static GameObject Shape(Transform parent,string name,Vector3 position,Vector3 size,Material material,Mesh mesh,bool collision)
        {
            Transform item=Root(parent,name,position);item.localScale=size;item.gameObject.isStatic=false;
            item.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;item.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
            if(collision)item.gameObject.AddComponent<BoxCollider>();return item.gameObject;
        }
        private static TextMesh Label(Transform parent,string text,Vector3 position,float size)
        {
            var root=Root(parent,"Protected_Work_Label",position);root.localRotation=Quaternion.Euler(0,180,0);
            var label=root.gameObject.AddComponent<TextMesh>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.text=text;label.characterSize=size;label.fontSize=32;label.color=Color.white;
            root.GetComponent<MeshRenderer>().sharedMaterial=label.font.material;return label;
        }
    }
}
