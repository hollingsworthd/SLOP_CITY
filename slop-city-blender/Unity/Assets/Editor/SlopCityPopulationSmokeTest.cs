#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace SlopCity.Editor
{
    /// <summary>Logical identity, bounded complete bodies and supported admitted interior routes.</summary>
    public static class SlopCityPopulationSmokeTest
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static readonly string[] Keys={"SlopCity.Sandbox.Progress.v1","SlopCity.Sandbox.Progress.v1.recovery","SlopCity.MatrixSearch.Progress.v1","SlopCity.MatrixSearch.Progress.v1.recovery"};
        private static readonly int[] Reds={1,46,98,166,209,254,271,308,372,402,418};
        private static Receipt receipt;private static SlopCityCrowd crowd;private static SlopCityFirstPerson player;private static Camera camera;private static CharacterController capsule;private static SlopCityBuildingWorld world;
        public static void Run()
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode||Application.companyName!="SlopCityValidation-v0116"||!Application.persistentDataPath.Contains("SlopCityValidation-v0116"))throw new InvalidOperationException("Isolated SlopCityValidation-v0116 batch Edit Mode required.");
            receipt=new Receipt{startedAtUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion};
            var backups=Keys.ToDictionary(k=>k,k=>new Backup{exists=PlayerPrefs.HasKey(k),value=PlayerPrefs.GetString(k,"")});float scale=Time.timeScale;var random=UnityEngine.Random.state;
            Application.logMessageReceived+=Log;
            try
            {
                foreach(string key in Keys)PlayerPrefs.DeleteKey(key);
                const string banked="{\"credits\":123,\"experience\":456,\"salvage\":7,\"completed\":9,\"water\":2,\"rareFinds\":1}";
                const string contacts="{\"target\":98,\"stage\":0,\"contacts\":2,\"meals\":1,\"circuits\":0,\"found\":[1,46]}";
                PlayerPrefs.SetString(Keys[0],banked);PlayerPrefs.SetString(Keys[2],contacts);PlayerPrefs.Save();
                var scene=EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath,OpenSceneMode.Single);
                foreach(var sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include,FindObjectsSortMode.None))sync.enabled=false;
                foreach(var media in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include,FindObjectsSortMode.None))media.enabled=false;
                player=Object.FindFirstObjectByType<SlopCityFirstPerson>();Check(player,"Saved player exists.");player.enabled=false;capsule=player.GetComponent<CharacterController>();camera=player.ViewCamera;
                Set(player,"controller",capsule);Set(player,"paused",false);Set(player,"seated",false);Set(player,"externalMovement",false);Time.timeScale=1;
                crowd=Object.FindFirstObjectByType<SlopCityCrowd>();Check(crowd&&crowd.PopulationCount==432&&crowd.RedContactCount==11,"Authored registry has432 residents and11 original red contacts before expansion.");
                for(int i=0;i<432;i++){Check(crowd.GetResidentId(i)==i+1,"Legacy migration assigns fixed ID at original index "+i);Check(crowd.IsRedContact(i)==Reds.Contains(i),"Original contact index remains unchanged "+i);}
                ReorderMigration();
                SlopCityWebGeometryBuildProcessor.ProtectRuntimeBuildingGeometry(scene);
                GameObject city=GameObject.Find("SlopCity_NineBlocks");world=city.GetComponent<SlopCityBuildingWorld>()??city.AddComponent<SlopCityBuildingWorld>();Set(world,"constrainedBrowser",true);world=SlopCityBuildingWorld.Ensure(player);Check(world,"Constrained physical building world installs.");world.enabled=false;
                Set(crowd,"playerController",player);Set(crowd,"player",player.transform);crowd.PreparePopulation(world);
                Check(crowd.RegisteredCount==2026&&crowd.SmithCount==1984&&crowd.RedContactCount==42,"Exactly1984 Smiths and42 women are registered.");
                Check(crowd.InteriorResidentCount==1151,"1142 Smiths and9 rare red contacts have real building/floor homes.");
                int[] ids=Enumerable.Range(0,crowd.PopulationCount).Select(crowd.GetResidentId).ToArray();Check(ids.Distinct().Count()==2026,"All2026 immutable IDs are unique.");
                for(int i=0;i<432;i++)Check(ids[i]==i+1&&crowd.IsRedContact(i)==Reds.Contains(i),"Expansion retains original IDs/contact save indices "+i);
                SupplementalIdentities(ids);
                crowd.PreparePopulation(world);Check(ids.SequenceEqual(Enumerable.Range(0,crowd.PopulationCount).Select(crowd.GetResidentId)),"Repeated PreparePopulation does not duplicate or reorder residents.");
                InteriorRoutes();
                IndoorRedContacts();
                DenseWholeBodies();
                Check(ids.SequenceEqual(Enumerable.Range(0,crowd.PopulationCount).Select(crowd.GetResidentId)),"Floor/body recycling preserves all registry IDs.");
                Check(PlayerPrefs.GetString(Keys[0])==banked&&PlayerPrefs.GetString(Keys[2])==contacts&&!PlayerPrefs.HasKey(Keys[1])&&!PlayerPrefs.HasKey(Keys[3]),"Population changes preserve banked balances, contact indices and both independent save bytes exactly.");
                LegacyContactStartup(ids);
                Check(receipt.runtimeErrors.Count==0,"No runtime error, assertion or exception was logged.");
            }
            catch(Exception e){receipt.errors.Add(e.ToString());}
            finally
            {
                foreach(var sandbox in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsInactive.Include,FindObjectsSortMode.None))Object.DestroyImmediate(sandbox);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                foreach(var pair in backups)if(pair.Value.exists)PlayerPrefs.SetString(pair.Key,pair.Value.value);else PlayerPrefs.DeleteKey(pair.Key);PlayerPrefs.Save();
                receipt.savesRestored=backups.All(p=>PlayerPrefs.HasKey(p.Key)==p.Value.exists&&(!p.Value.exists||PlayerPrefs.GetString(p.Key)==p.Value.value));if(!receipt.savesRestored)receipt.errors.Add("Four progression/recovery keys were not restored.");
                Time.timeScale=scale;UnityEngine.Random.state=random;Application.logMessageReceived-=Log;
            }
            receipt.passed=receipt.errors.Count==0&&receipt.runtimeErrors.Count==0;receipt.finishedAtUtc=DateTime.UtcNow.ToString("O");
            string path=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Builds/Reports/population-gameplay-check.json");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(receipt,true));
            Debug.Log("SLOP_CITY_POPULATION_"+(receipt.passed?"PASSED":"FAILED")+" "+receipt.checks+" checks");EditorApplication.Exit(receipt.passed?0:1);
        }
        private static void SupplementalIdentities(int[] ids)
        {
            int next=432;
            for(int old=0;old<432;old++)if(!Reds.Contains(old))for(int copy=0;copy<3;copy++)
            {Check(ids[next]==100000+(old+1)*4+copy&&!crowd.IsRedContact(next),"Existing generated Smith ID/order is retained.");next++;receipt.retainedGeneratedSmiths++;}
            Check(next==1695,"All1263 previously generated Smiths remain unchanged.");
            for(int added=0;added<300;added++)Check(ids[1695+added]==200001+added&&!crowd.IsRedContact(1695+added)&&crowd.Snapshot(1695+added).interior,"New Smith has a distinct stable ID and an interior home.");
            var streetDistricts=new HashSet<int>();var streetCounts=new Dictionary<int,int>();int street=0,indoor=0,transit=0;
            for(int index=0;index<crowd.PopulationCount;index++)if(crowd.IsRedContact(index))
            {
                var snapshot=crowd.Snapshot(index);if(snapshot.interior){indoor++;continue;}street++;streetDistricts.Add(snapshot.districtIndex);
                if(snapshot.districtIndex<0||snapshot.districtIndex>8||snapshot.position.y<-.5f)transit++;
                if(!streetCounts.ContainsKey(snapshot.districtIndex))streetCounts[snapshot.districtIndex]=0;streetCounts[snapshot.districtIndex]++;
                Check(snapshot.routeIndex>=0&&snapshot.routeIndex<crowd.Routes.Count&&snapshot.districtIndex>=0&&snapshot.districtIndex<=8&&snapshot.position.y>=-.5f&&SupportedClear(snapshot.position),"Every street woman occupies supported clear ABOVEGROUND district pavement, never a subway graph.");
                if(index==402||index==418)Check(!crowd.IsResidentDetailed(index)&&!crowd.IsResidentPresented(index),"Originally admitted subway woman retires her old body/presentation before street relocation.");
            }
            for(int added=0;added<31;added++)Check(ids[1995+added]==300001+added&&crowd.IsRedContact(1995+added)&&crowd.Snapshot(1995+added).interior==(added>=22),"New red stable ID retains the33 street/nine interior split.");
            Check(street==33&&indoor==9&&streetDistricts.Count==9&&transit==0,"Exactly33 aboveground street women span nine districts, nine live indoors and none uses transit routes.");
            Check(streetCounts.Values.Max()-streetCounts.Values.Min()<=1,"Aboveground street contact distribution stays balanced at three or four women per district.");
            receipt.streetRedContacts=street;receipt.indoorRedContacts=indoor;receipt.streetRedDistricts=streetDistricts.Count;receipt.transitRedContacts=transit;
        }
        private static void IndoorRedContacts()
        {
            IList homes=(IList)Get(crowd,"interiorHomes");Array records=(Array)Get(crowd,"residents");var buildings=new HashSet<SlopCityBuildingInterior>();
            for(int index=2017;index<2026;index++)
            {
                int homeIndex=(int)Get(records.GetValue(index),"home");object home=homes[homeIndex];var building=(SlopCityBuildingInterior)Get(home,"building");int floor=(int)Get(home,"floor");
                Check(building&&floor==0&&buildings.Add(building),"Each rare indoor woman has a distinct enterable ground-floor building.");
                Transform root=building.EnsureFloor(floor);SlopCityInteriorPopulation.AdmitFloor(building,floor,root);
                var graph=(SlopCityCrowdRoute)Get(home,"route");Check(graph!=null&&graph.nodes.Length>=2,"Indoor red contact has an admitted connected circulation route: "+building.BuildingId);
                foreach(var node in graph.nodes)Check(SupportedClear(node.position),"Indoor red route has real floor, headroom and no furniture overlap.");
                Place(graph.nodes[0].position+Vector3.right*3);Set(player,"paused",false);
                for(int tick=0;tick<500&&!crowd.IsResidentDetailed(index);tick++){crowd.AdvanceSimulation(.05f);Budgets(crowd);}
                Check(crowd.IsResidentDetailed(index)&&crowd.IsRedContact(index),"Loaded interior admits an actual red woman through the normal bounded scheduler.");
                var person=crowd.Snapshot(index);bool reached=false;
                for(int side=0;side<16&&!reached;side++)
                {
                    float angle=side*Mathf.PI/8;Vector3 feet=person.position+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*1.8f;
                    if(!SupportedClear(feet))continue;Place(feet);camera.transform.position=feet+Vector3.up*1.6f;camera.transform.LookAt(person.position+Vector3.up*1.2f);Invoke(crowd,"RenderCrowd",camera);
                    Ray ray=new Ray(camera.transform.position,camera.transform.forward);
                    if(!crowd.TryFindTarget(ray,3.4f,out int selected,out Vector3 point)||selected!=index)continue;
                    bool blocked=false;foreach(var hit in Physics.RaycastAll(ray,Vector3.Distance(ray.origin,point),~0,QueryTriggerInteraction.Ignore))if(hit.collider!=capsule&&!hit.transform.IsChildOf(player.transform)){blocked=true;break;}
                    reached=!blocked&&crowd.IsResidentPresented(index)&&crowd.TryTalk(index,ray.origin,out _);
                }
                Check(reached,"Normal presented-body ray and unobstructed supported standing position reach the indoor red contact.");receipt.reachableIndoorRedContacts++;
                int id=crowd.GetResidentId(index);Place(Vector3.up*30);SlopCityInteriorPopulation.RetireFloor(building,floor);
                Check(!crowd.IsResidentDetailed(index)&&!crowd.IsResidentPresented(index)&&!crowd.TryTalk(index,camera.transform.position,out _),"Retiring the contact floor releases body and refuses stale invisible conversation.");
                SlopCityInteriorPopulation.AdmitFloor(building,floor,root);Check(crowd.GetResidentId(index)==id&&crowd.IsRedContact(index),"Readmission keeps indoor red identity and role.");
                if(!building.PreservedInterior)building.ReleaseFloor(floor);
            }
            Check(buildings.Count==9,"Rare indoor contacts occupy nine distinct buildings.");
        }
        private static void LegacyContactStartup(int[] ids)
        {
            string banked=PlayerPrefs.GetString(Keys[0]),search=PlayerPrefs.GetString(Keys[2]);
            try
            {
                PlayerPrefs.SetString(Keys[2],"{\"target\":98,\"stage\":0,\"contacts\":11,\"meals\":7,\"circuits\":2,\"found\":["+string.Join(",",Reds)+"]}");PlayerPrefs.Save();
                Invoke(player,"Awake");Invoke(player,"Start");
                Check(crowd.PopulationCount==2026&&crowd.RedContactCount==42&&ids.SequenceEqual(Enumerable.Range(0,crowd.PopulationCount).Select(crowd.GetResidentId)),"Actual FirstPerson.Start prepares the full stable population before search loading.");
                var hunt=player.Sandbox.MatrixHunt;var found=(HashSet<int>)Get(hunt,"found");
                Check(hunt.Ready&&hunt.ContactsFound==11&&hunt.MealsEaten==7&&hunt.CircuitsCompleted==2&&found.Count==11&&Reds.All(found.Contains),"All11 old discoveries survive startup without prematurely restarting the42-contact circuit.");
                Check(hunt.TargetIndex>=1995&&crowd.IsRedContact(hunt.TargetIndex),"Search selects one of31 newly available red identities.");
                for(int index=1995;index<2026;index++)
                {
                    Check(hunt.TryContact(index,out _),"The real search accepts each new red identity.");
                    if(index<2025)Check(hunt.CircuitsCompleted==2&&Reds.All(found.Contains),"Circuit waits until all42 distinct contacts have been found.");
                }
                Check(hunt.ContactsFound==42&&hunt.CircuitsCompleted==3&&found.Count==0,"The42nd distinct discovery starts exactly one new search circuit.");receipt.searchCircuitContacts=42;
            }
            finally{PlayerPrefs.SetString(Keys[0],banked);PlayerPrefs.SetString(Keys[2],search);PlayerPrefs.Save();}
        }
        private static void ReorderMigration()
        {
            var seeds=(SlopCityNpcSeed[])Get(crowd,"population");var reordered=(SlopCityNpcSeed[])seeds.Clone();Array.Reverse(reordered);for(int i=0;i<reordered.Length;i++)reordered[i].residentId=0;
            var go=new GameObject("Resident_ID_Reorder_Probe");
            try
            {
                var copy=go.AddComponent<SlopCityCrowd>();copy.Configure(crowd.Visuals,crowd.Routes.ToArray(),reordered);
                for(int i=0;i<432;i++){int index=copy.FindResident(i+1);Check(index==431-i,"Reordered authored record retains immutable resident ID "+(i+1));Check(copy.IsRedContact(index)==Reds.Contains(i),"Red role follows fixed ID after reorder.");}
            }
            finally{Object.DestroyImmediate(go);Invoke(crowd,"OnEnable");}
        }
        private static void InteriorRoutes()
        {
            IList homes=(IList)Get(crowd,"interiorHomes");int routeHomes=0;
            for(int i=0;i<homes.Count;i++)
            {
                var graph=(SlopCityCrowdRoute)Get(homes[i],"route");if(graph==null)continue;routeHomes++;
                foreach(var node in graph.nodes){Check(SupportedClear(node.position),"Admitted indoor route node is supported and capsule-clear.");receipt.interiorNodes++;}
            }
            Check(routeHomes>0,"At least one preserved venue provides a real supported interior circulation route.");receipt.initialInteriorRoutes=routeHomes;
            var tower=world.Buildings.First(b=>b&&b.IsTower&&b.OccupiedFloorCount>1);Transform root=tower.EnsureFloor(1);
            object home=null;int homeIndex=-1;
            for(int i=0;i<homes.Count;i++)if((SlopCityBuildingInterior)Get(homes[i],"building")==tower&&(int)Get(homes[i],"floor")==1){home=homes[i];homeIndex=i;break;}
            Check(home!=null,"Tower occupied floor has a stable logical population home.");var route=(SlopCityCrowdRoute)Get(home,"route");Check(route!=null&&route.nodes.Length>=2,"Actual furnished tower floor admits a connected physical indoor route.");
            Check(crowd.LastInteriorNodeProbes<=SlopCityCrowd.MaximumInteriorNodeProbes&&crowd.LastInteriorEdgeChecks<=SlopCityCrowd.MaximumInteriorEdgeChecks,"Interior route validation has bounded probes/edge checks.");
            foreach(var node in route.nodes){Check(SupportedClear(node.position),"Tower node has real support/headroom and furniture clearance.");Vector3 local=tower.transform.InverseTransformPoint(node.position);Check(Mathf.Abs(local.x-tower.ShaftLocalCenter.x)>=1.5f||Mathf.Abs(local.z-tower.ShaftLocalCenter.z)>=1.5f,"Indoor route excludes lift shaft.");receipt.interiorNodes++;}
            Place(route.nodes[0].position+Vector3.right*2f);int first=-1;
            for(int tick=0;tick<500;tick++)
            {
                crowd.AdvanceSimulation(.05f);Budgets(crowd);Array records=(Array)Get(crowd,"residents");
                for(int index=432;index<crowd.PopulationCount;index++)if((int)Get(records.GetValue(index),"home")==homeIndex&&crowd.IsResidentDetailed(index)){first=index;break;}
                if(first>=0)break;
            }
            Check(first>=0,"A logical Smith is admitted as an actual detailed resident on the loaded tower floor.");
            Vector3 before=crowd.Snapshot(first).position;for(int i=0;i<80;i++){crowd.AdvanceSimulation(.05f);Budgets(crowd);}Check((crowd.Snapshot(first).position-before).sqrMagnitude>.0001f,"Indoor Smith physically moves on the connected route.");
            camera.transform.position=crowd.Snapshot(first).position+new Vector3(0,1.6f,-2);camera.transform.LookAt(crowd.Snapshot(first).position+Vector3.up*1.2f);Invoke(crowd,"RenderCrowd",camera);
            Check(crowd.IsResidentPresented(first),"The actual indoor Smith is presented by the normal instanced renderer.");
            int registered=crowd.RegisteredCount,admitted=crowd.AdmittedInteriorRouteCount;SlopCityInteriorPopulation.AdmitFloor(tower,1,root);
            Check(crowd.RegisteredCount==registered&&crowd.AdmittedInteriorRouteCount==admitted,"Repeated floor admission is idempotent.");
            int immutable=crowd.GetResidentId(first);Place(Vector3.up*30f);tower.ReleaseFloor(1);
            Check(!crowd.IsResidentDetailed(first)&&!crowd.IsResidentPresented(first)&&crowd.GetResidentId(first)==immutable,"Floor retirement releases body/target immediately while retaining resident identity.");
            Check(!crowd.TryTalk(first,camera.transform.position,out _),"An unpresented retired resident cannot become an invisible interaction target.");
            root=tower.EnsureFloor(1);Check(crowd.GetResidentId(first)==immutable&&crowd.RegisteredCount==registered,"Readmission preserves all logical identities without adding residents.");
            float time=crowd.SimulationTime;Set(player,"paused",true);crowd.AdvanceSimulation(1000f);Set(player,"paused",false);Check(crowd.SimulationTime==time,"Pause prevents population catch-up.");
            crowd.AdvanceSimulation(1000f);Check(Mathf.Abs(crowd.SimulationTime-time-.1f)<.001f,"A long hitch is one bounded0.1-second simulation step.");Budgets(crowd);
        }
        private static void DenseWholeBodies()
        {
            var root=new GameObject("Dense_Logical_Presentation_Probe");GameObject ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.SetParent(root.transform);ground.transform.position=new Vector3(0,199.9f,0);ground.transform.localScale=new Vector3(24,.2f,20);
            var cameraRoot=new GameObject("Dense_Approved_Camera");cameraRoot.transform.SetParent(root.transform);Camera view=cameraRoot.AddComponent<Camera>();view.transform.position=new Vector3(0,225,0);view.transform.rotation=Quaternion.Euler(90,0,0);view.fieldOfView=110;
            try
            {
                const int count=450;var nodes=new SlopCityCrowdNode[count];var seeds=new SlopCityNpcSeed[count];
                for(int i=0;i<count;i++){int x=i%25,z=i/25;var links=new List<int>();if(x>0)links.Add(i-1);if(x<24)links.Add(i+1);if(z>0)links.Add(i-25);if(z<17)links.Add(i+25);nodes[i]=new SlopCityCrowdNode{position=new Vector3((x-12)*.8f,200.025f,(z-8)*.8f),neighbors=links.ToArray()};seeds[i]=new SlopCityNpcSeed{residentId=500000+i,route=0,node=i,next=links[0],appearance=i*13,role=SlopCityNpcRole.Resident,height=1.76f,width=1,speed=1};}
                var dense=root.AddComponent<SlopCityCrowd>();dense.Configure(crowd.Visuals,new[]{new SlopCityCrowdRoute{label="Physical dense floor",districtIndex=99,nodes=nodes}},seeds);
                Check(dense.PopulationCount==450&&dense.DetailedCount==432,"Registry overflow retains450 identities with only432 reusable detailed slots.");
                Invoke(dense,"RenderCrowd",view);Check(dense.PresentedCount<=dense.MaximumPresentedCount&&dense.WholePeopleDeferred>0,"Dense camera admits a bounded set of complete people before matrix emission.");receipt.maximumPresented=dense.PresentedCount;
                AtomicWholePerson(dense,false);AtomicWholePerson(dense,true);AliasReservation(dense);
                // The public target is a resident ID/index, never a recycled body slot.
                int originalId=dense.GetResidentId(0);int slot=((int[])Get(dense,"residentSlots"))[0];Invoke(dense,"RetireResident",slot);Invoke(dense,"AdmitResident",440,false);
                Check(dense.GetResidentId(0)==originalId&&!dense.IsResidentDetailed(0)&&dense.IsResidentDetailed(440),"Slot reuse by resident440 does not redirect resident0.");
                Check(!dense.TryTalk(0,view.transform.position,out _),"Stale released target refuses interaction after slot reuse.");
                Check(dense.DetailedCount<=432,"Body reuse remains within the original workload ceiling.");
                foreach(object batch in (IList)Get(dense,"batches"))Set(batch,"count",0);
                Invoke(dense,"RenderCrowd",view);int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,materials=Resources.FindObjectsOfTypeAll<Material>().Length;
                for(int tick=0;tick<40;tick++){dense.AdvanceSimulation(.05f);Invoke(dense,"RenderCrowd",view);}
                Check(Resources.FindObjectsOfTypeAll<Mesh>().Length==meshes&&Resources.FindObjectsOfTypeAll<Material>().Length==materials,"Additional residents/ticks/rendering allocate no per-person meshes or materials after warm-up.");
            }
            finally{Object.DestroyImmediate(root);Invoke(crowd,"OnEnable");}
        }
        private static void AtomicWholePerson(SlopCityCrowd subject,bool details)
        {
            object person=((Array)Get(subject,"agents")).GetValue(0);IList batches=(IList)Get(subject,"batches");foreach(object batch in batches)Set(batch,"count",0);
            Invoke(subject,"TryReservePerson",person,details);object shoe=null;
            foreach(object batch in batches)if((Mesh)Get(batch,"mesh")==subject.Visuals.parts[(int)SlopCityHumanPart.Shoe]&&(Material)Get(batch,"material")==subject.Visuals.shoes)shoe=batch;
            Check(shoe!=null,"Actual shoe matrix key participates in reservation.");Set(shoe,"count",1022);
            int[] before=batches.Cast<object>().Select(b=>(int)Get(b,"count")).ToArray();bool result=(bool)Invoke(subject,"TryReservePerson",person,details);
            Check(!result&&before.SequenceEqual(batches.Cast<object>().Select(b=>(int)Get(b,"count"))),"One shoe matrix short defers the whole "+(details?"detailed":"distant")+" person without emitting partial limbs.");receipt.atomicCases++;
            foreach(object batch in batches)Set(batch,"count",0);
        }
        private static void AliasReservation(SlopCityCrowd subject)
        {
            Mesh[] parts=subject.Visuals.parts;Mesh thigh=parts[(int)SlopCityHumanPart.Thigh],shin=parts[(int)SlopCityHumanPart.Shin],shoe=parts[(int)SlopCityHumanPart.Shoe];Material shoes=subject.Visuals.shoes;
            try
            {
                object wardrobe=Get(subject,"wardrobe");Material suit=(Material)Get(wardrobe,"suit");parts[(int)SlopCityHumanPart.Shin]=parts[(int)SlopCityHumanPart.Shoe]=thigh;subject.Visuals.shoes=suit;
                object person=((Array)Get(subject,"agents")).Cast<object>().First(p=>!(bool)Get(p,"redContact"));IList batches=(IList)Get(subject,"batches");foreach(object batch in batches)Set(batch,"count",0);Invoke(subject,"TryReservePerson",person,true);
                object shared=batches.Cast<object>().First(b=>(Mesh)Get(b,"mesh")==thigh&&(Material)Get(b,"material")==suit);int repetitions=(bool)Get(wardrobe,"animatedTrousers")?4:6;Check((int)Get(shared,"reserved")==repetitions,"Reservation counts actual aliased shin/shoe keys plus thighs only in the unsupported-shader fallback.");Set(shared,"count",1024-repetitions);
                Check(!(bool)Invoke(subject,"TryReservePerson",person,true),"Aliased limb matrices with one fewer free slot defer the entire person.");receipt.atomicCases++;
            }
            finally{parts[(int)SlopCityHumanPart.Thigh]=thigh;parts[(int)SlopCityHumanPart.Shin]=shin;parts[(int)SlopCityHumanPart.Shoe]=shoe;subject.Visuals.shoes=shoes;foreach(object batch in (IList)Get(subject,"batches"))Set(batch,"count",0);}
        }
        private static void Budgets(SlopCityCrowd subject)
        {
            Check(subject.DetailedCount<=432&&subject.LastCoarseUpdates<=32&&subject.LastAdmissionVisits<=32&&subject.LastAdmissions<=4&&subject.LastRetirements<=8,"Detailed/coarse/admission/retirement work remains within fixed budgets.");
            Check(subject.MaximumObservedNeighbourVisits<=128&&subject.MaximumObservedNeighbourCandidates<=32,"Detailed neighbour visits/candidates remain bounded.");receipt.schedulerSteps++;
        }
        private static bool SupportedClear(Vector3 feet)
        {
            if(!Physics.Raycast(feet+Vector3.up*.2f,Vector3.down,out RaycastHit hit,.5f,~0,QueryTriggerInteraction.Ignore)||hit.normal.y<.75f)return false;
            foreach(Collider c in Physics.OverlapCapsule(feet+Vector3.up*.34f,feet+Vector3.up*1.64f,.32f,~0,QueryTriggerInteraction.Ignore))if(c!=capsule&&!c.transform.IsChildOf(player.transform))return false;return true;
        }
        private static void Place(Vector3 point){capsule.enabled=false;player.transform.position=point;capsule.enabled=true;Physics.SyncTransforms();}
        private static object Get(object owner,string field)=>owner.GetType().GetField(field,Hidden).GetValue(owner);
        private static void Set(object owner,string field,object value)=>owner.GetType().GetField(field,Hidden).SetValue(owner,value);
        private static object Invoke(object owner,string method,params object[] args)=>owner.GetType().GetMethod(method,Hidden).Invoke(owner,args);
        private static void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);receipt.checks++;}
        private static void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Assert||type==LogType.Exception)receipt.runtimeErrors.Add(text+"\n"+stack);}
        private sealed class Backup{public bool exists;public string value;}
        [Serializable]private sealed class Receipt
        {
            public bool passed,savesRestored;public string startedAtUtc,finishedAtUtc,unityVersion;public int checks,interiorNodes,initialInteriorRoutes,schedulerSteps,maximumPresented,atomicCases,retainedGeneratedSmiths,streetRedContacts,indoorRedContacts,streetRedDistricts,reachableIndoorRedContacts,searchCircuitContacts,transitRedContacts;
            public string scope="Isolated synchronous batch Edit Mode; actual saved-city migration and constrained building geometry, explicit production simulation/render methods and a separate physical dense floor. Tests2026 registry/1984Smiths/42 red contacts (33 aboveground street/nine distinct buildings/zero transit), immutable IDs/reorder, bounded detailed/render/coarse/neighbour work, connected supported interior routes and retirement, complete-person matrix reservation including aliased mesh keys, stale slot targets and warm mesh/material ownership. Includes explicit actual FirstPerson.Start ordering with11 old discoveries and42-contact circuit; indoor contacts use normal presented-body targeting from supported unobstructed reach. Four save keys restored, no scene save/network/media. This is not a physical-device, sustained memory, frame-time or whole-city walkthrough claim.";
            public List<string> errors=new List<string>(),runtimeErrors=new List<string>();
        }
    }
}
#endif
