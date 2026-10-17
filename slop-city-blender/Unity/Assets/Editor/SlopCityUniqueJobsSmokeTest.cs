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
    /// <summary>Isolated actual-scene device/inventory work. Observation-dependent media/residents have a companion fixture.</summary>
    public static class SlopCityUniqueJobsSmokeTest
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private const string Key="SlopCity.Sandbox.Progress.v1";
        private static readonly string[] Keys={Key,Key+".recovery","SlopCity.MatrixSearch.Progress.v1","SlopCity.MatrixSearch.Progress.v1.recovery"};
        private static SlopCitySandbox sandbox;
        private static SlopCityFirstPerson player;
        private static CharacterController capsule;
        private static Camera view;
        private static SlopCityJobWorld world;
        private static SlopCityBuildingWorld buildings;
        private static SlopCityMetroWorld metro;
        private static Receipt receipt;
        public static void Run()
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode||!Application.companyName.Contains("SlopCityValidation"))throw new InvalidOperationException("Unique job fixture requires isolated validation batch Edit Mode.");
            receipt=new Receipt{startedAtUtc=DateTime.UtcNow.ToString("O")};
            var original=Keys.ToDictionary(k=>k,k=>new Backup{exists=PlayerPrefs.HasKey(k),text=PlayerPrefs.GetString(k,"")});
            var random=UnityEngine.Random.state;Application.logMessageReceived+=Log;
            try
            {
                foreach(string k in Keys)PlayerPrefs.DeleteKey(k);
                var scene=EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath,OpenSceneMode.Single);
                foreach(var sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include,FindObjectsSortMode.None))sync.enabled=false;
                foreach(var media in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include,FindObjectsSortMode.None))Object.DestroyImmediate(media);
                player=Object.FindFirstObjectByType<SlopCityFirstPerson>();Check(player,"Authored player exists.");player.enabled=false;
                var gameplayInput=player.GetComponent<SlopCityPlayerInput>();if(!gameplayInput)gameplayInput=player.gameObject.AddComponent<SlopCityPlayerInput>();gameplayInput.SetGameplayEnabled(true);Set(player,"playerInput",gameplayInput);
                capsule=player.GetComponent<CharacterController>();Set(player,"controller",capsule);Set(player,"seated",false);Set(player,"paused",false);Set(player,"externalMovement",false);
                view=player.GetComponentInChildren<Camera>(true);Check(view,"Authored view exists.");Set(player,"viewCamera",view);
                SlopCityWebGeometryBuildProcessor.ProtectRuntimeBuildingGeometry(scene);
                buildings=SlopCityBuildingWorld.Ensure(player);Check(buildings,"Real building/lift world prepared.");buildings.enabled=false;
                sandbox=player.GetComponent<SlopCitySandbox>();if(!sandbox)sandbox=player.gameObject.AddComponent<SlopCitySandbox>();Set(player,"sandbox",sandbox);
                sandbox.TeleportRequested+=(position,heading)=>{capsule.enabled=false;player.transform.SetPositionAndRotation(position,Quaternion.Euler(0,heading,0));capsule.enabled=true;Physics.SyncTransforms();};
                sandbox.MovementOverrideRequested+=held=>Set(player,"externalMovement",held);
                sandbox.Initialize(player.transform,text=>receipt.feedback.Add(text));world=(SlopCityJobWorld)Get(sandbox,"uniqueWorld");metro=SlopCityMetroWorld.Ensure(player);
                Check(SlopCityJobCatalog.All.Length==40&&SlopCityJobCatalog.ValidateDefinitions().Length==0,"Exactly40 valid distinct version2 recipes retain their IDs.");
                for(int i=0;i<40;i++){Check(world.Get(i)!=null,"Physical work site admitted for "+SlopCityJobCatalog.All[i].id);receipt.sites.Add(new SiteReceipt{id=SlopCityJobCatalog.All[i].id,position=world.Get(i).root.position,carStaging=world.Get(i).vehicleStaging});}
                Check(world.GetComponentsInChildren<UnityEngine.Video.VideoPlayer>(true).Length==0,"Work sites add no video players.");
                Check(world.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).Distinct().Count()<=3,"All work geometry shares primitive meshes.");
                for(int i=0;i<40;i++)Exercise(i);
                RepeatAndRecoverCleanWork();
                CompletedPumpOffAssignment();
                PortableFuelRecovery();
                OwnedSupplyQuantityGuard();
                MalformedFuelArchive();
                DurableOutcomeRepeat();
                RestoreVisibleOutcomes();
                SaveRoundtrip();
                Check(receipt.completed.Count==35,"35 core mechanics completed through production actions; five real-observation cases delegated.");
                Check(receipt.exercised.Count==40,"All40 physical mechanics exercised, including observation guards.");
            }
            catch(Exception error){receipt.errors.Add(error.ToString());}
            finally
            {
                if(world&&sandbox)world.Release(sandbox);
                if(sandbox)Object.DestroyImmediate(sandbox);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                foreach(var pair in original)if(pair.Value.exists)PlayerPrefs.SetString(pair.Key,pair.Value.text);else PlayerPrefs.DeleteKey(pair.Key);
                PlayerPrefs.Save();receipt.preferencesRestored=original.All(p=>PlayerPrefs.HasKey(p.Key)==p.Value.exists&&(!p.Value.exists||PlayerPrefs.GetString(p.Key)==p.Value.text));
                UnityEngine.Random.state=random;Application.logMessageReceived-=Log;
            }
            receipt.passed=receipt.errors.Count==0&&receipt.runtimeErrors.Count==0&&receipt.preferencesRestored;receipt.finishedAtUtc=DateTime.UtcNow.ToString("O");
            string path=Argument("-slopCityUniqueJobReport")??"Builds/Reports/world-work-20261016/unique-jobs-check.json";Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(receipt,true));
            Debug.Log("SLOP_CITY_UNIQUE_JOBS_"+(receipt.passed?"PASSED":"FAILED")+" "+receipt.checks+" checks");EditorApplication.Exit(receipt.passed?0:1);
        }
        private static void Exercise(int i)
        {
            ((IList)Get(sandbox,"inventory")).Clear();
            Check(sandbox.SelectJob(SlopCityJobCatalog.All[i].id),"Select actual physical recipe "+i);receipt.exercised.Add(SlopCityJobCatalog.All[i].id);
            switch(i)
            {
                case 0:Use(i,0);Use(i,1);Check(!Record(i).completed,"Wrong postal address cannot complete delivery.");Use(i,2);break;
                case 1:TakeOwnedInstallationPart(i);Use(i,0);Check(Record(i).sourceIds[0]==world.Get(i).dustyId,"Train installation reuses the actual recovered pane identity.");Use(i,1);Check(!Record(i).completed,"Supply stall cannot install an uncarried window.");Ride(i);UseRemote(i);Check(world.Get(i).remotePane.gameObject.activeSelf,"Real arrival socket becomes solid only after installation.");break;
                case 2:Use(i,0);Use(i,1);Use(i,1);Check(!Record(i).completed,"Duplicate address cannot consume another letter.");Use(i,2);Use(i,3);break;
                case 3:Sequence(i,0,1,2);Check(!sandbox.Inventory.Any(e=>e.category=="lamp_core"),"Exact removed core was returned.");break;
                case 4:Sequence(i,0,1,2);Check(sandbox.InventoryCount("kit")==1,"Actual typed field kit is retained.");break;
                case 5:Sequence(i,0,1,2,4,5);Check(sandbox.WaterSupply==3,"Restored branch supplies usable water.");break;
                case 6:TakePanel(i);Use(i,1);Check(sandbox.InventoryCount("metal")==2&&sandbox.InventoryCount("wood")==1,"Removed physical panel converts to conserved materials.");break;
                case 7:Sequence(i,1,2,3,0,1);Check(!Record(i).completed,"Replacing healthy fuse does not complete fault repair.");Use(i,2);break;
                case 8:Sequence(i,1,0,2,3);break;
                case 9:Sequence(i,1,2,0,3,1);Check(world.Get(i).lamps[0].enabled,"Replaced bulb produces actual light.");break;
                case 10:Sequence(i,0,1,2);Check(sandbox.InventoryCount("kit")==1,"Load test dispenses an actual stock unit.");break;
                case 11:Clean(i);Call(sandbox,"TickUniqueJobs");Check(world.Get(i).lamps[0].enabled,"Polished lamp remains powered.");break;
                case 12:Clean(i);Use(i,1);Check(sandbox.SpeedMultiplier>1,"Rest changes actual recovery speed.");break;
                case 13:CarWork(i,false);break;
                case 14:Sequence(i,0,1,2,3);Check(sandbox.InventoryCount("metal")==0&&sandbox.InventoryCount("glass")==0&&sandbox.InventoryCount("plastic")==0,"Three sorted loads were consumed once.");break;
                case 15:Clean(i);Call(sandbox,"TickUniqueJobs");break;
                case 16:Sequence(i,1,1,1);Check(!Record(i).completed,"Capacity refuses a third bundle in the left bin.");Sequence(i,2,2);break;
                case 17:Sequence(i,0,1);Check(world.Get(i).poster.gameObject.activeSelf,"New protected poster becomes visible.");break;
                case 18:CarWork(i,true);break;
                case 19:Sequence(i,0,1,2);Check(world.Get(i).litter.All(t=>!t.gameObject.activeSelf),"Actual litter geometry is removed.");break;
                case 20:Sequence(i,0,1,1,1,0,2,2,3);Check(Record(i).values.Skip(2).Take(3).All(v=>v>=80),"Distinct physical moisture deficits are filled.");break;
                case 21:Sequence(i,0,1,2,3);for(int n=0;n<40;n++)Call(sandbox,"AdvanceUniqueDevices",.25f);Use(i,4);Check(world.Get(i).hose.All(t=>t.gameObject.activeSelf),"Actual hose geometry connects its endpoints.");break;
                case 22:Sequence(i,0,1,2,2,2,2);break;
                case 23:Use(i,1);Check((world.Get(i).bench.position-(world.Get(i).root.position+new Vector3(.6f,.6f,.25f))).magnitude<.1f,"Real bench occupies the saved clear placement pad.");break;
                case 24:Call(sandbox,"ExecuteActionCore",Object.FindObjectsByType<SlopCityInteractable>(FindObjectsSortMode.None).First(x=>x.Kind==SlopCityInteractionKind.Fountain),SlopCityJobAction.FillWater,view,false);Sequence(i,0,1,2);break;
                case 25:Use(i,0);Call(sandbox,"TickUniqueJobs");Check(!Record(i).completed,"Missing pooled decoded frames never fabricate motion success.");receipt.observationGuards.Add(i);return;
                case 26:Use(i,0);Use(i,1);Check(!Record(i).completed,"Missing ordinary screen cannot satisfy requested channel.");receipt.observationGuards.Add(i);return;
                case 27:Sequence(i,1);Check(Record(i).phase==0,"Wrong circuit order resets actual lights.");Sequence(i,2,1,3);break;
                case 28:for(int n=0;n<6;n++)Use(i,0);Use(i,2);break;
                case 29:Use(i,0);Check(!Record(i).completed,"Source alone cannot fabricate remote relay arrival/playback.");receipt.observationGuards.Add(i);return;
                case 30:for(int n=0;n<5;n++)Use(i,1);Sequence(i,2,2,3);break;
                case 31:Sequence(i,0,1,2);Check(!Record(i).completed,"Wrong reagent does not treat sample.");Use(i,3);break;
                case 32:for(int n=0;n<6;n++)Use(i,0);Use(i,2);break;
                case 33:Sequence(i,0,1,2,3);Check(!Record(i).completed,"Untreated source A cannot substitute for affected source B.");Use(i,4);break;
                case 34:Use(i,0);Use(i,1);Check(!Record(i).completed,"A box cannot impersonate the actual property owner.");receipt.observationGuards.Add(i);return;
                case 35:Sequence(i,0,0,0,1,1,2);Check(world.Get(i).lamps[0].enabled,"Balanced allocation powers actual lamp.");break;
                case 36:Use(i,1);Check(!Record(i).completed,"Unrevealed instruction cannot be filed as completed work.");Clean(i);Use(i,1);break;
                case 37:TakeOwnedInstallationPart(i);Use(i,0);Check(Record(i).sourceIds[0]==world.Get(i).dustyId,"Lift installation reuses the actual recovered panel identity.");RideLift(i);UseRemote(i);break;
                case 38:Use(i,0);Use(i,1);Check(!Record(i).completed,"A work control cannot impersonate a presented neighbour.");receipt.observationGuards.Add(i);return;
                case 39:Sequence(i,0,1,2,3);Call(sandbox,"ExecuteActionCore",Object.FindObjectsByType<SlopCityInteractable>(FindObjectsSortMode.None).First(x=>x.Kind==SlopCityInteractionKind.Fountain),SlopCityJobAction.FillWater,view,false);Use(i,4);Check(world.Get(i).guard.All(t=>t.gameObject.activeSelf),"Three actual guard slats remain fitted.");break;
            }
            Check(Record(i).completed,"Mechanic "+i+" reached its physical/resource predicate.");receipt.completed.Add(SlopCityJobCatalog.All[i].id);
        }
        private static void Sequence(int i,params int[] slots){foreach(int slot in slots)Use(i,slot);}
        private static void Use(int i,int slot)
        {
            if((i==0||i==2)&&slot>=1&&slot<=3)
            {UseDevice(((SlopCityInteractable[])Get(sandbox,"uniqueMailboxes"))[slot-1].GetComponent<SlopCityJobDevice>());return;}
            UseDevice(world.Get(i).controls[slot]);
        }
        private static void UseRemote(int i)=>UseDevice(world.Get(i).remoteControl);
        private static void UseDevice(SlopCityJobDevice device)
        {
            Check(device,"Concrete physical control exists.");
            float feet=world.Get(device.Mechanic).root.position.y;
            if(device==world.Get(device.Mechanic).remoteControl)feet=device.transform.position.y-1.05f;
            else if(device.External)feet=device.transform.position.y;
            if(device.gameObject.activeInHierarchy)
                Check(Approach(device.transform.position,()=>{Set(sandbox,"nextTargetAt",0f);return Get(Call(sandbox,"FindTarget",view),"interaction") as SlopCityInteractable==device.GetComponent<SlopCityInteractable>();}),"Supported clear standing approach reaches actual control "+device.Mechanic+"/"+device.Slot);
            else{Place(new Vector3(device.transform.position.x,feet,device.transform.position.z-1.7f));view.transform.LookAt(device.transform.position);}
            object[] args={device.GetComponent<SlopCityInteractable>(),view,false};
            if(device.gameObject.activeInHierarchy)
            {
                Set(sandbox,"nextTargetAt",0f);object aimed=Call(sandbox,"FindTarget",view);
                var selected=Get(aimed,"interaction") as SlopCityInteractable;
                var hitTransform=Get(aimed,"transform") as Transform;
                string hits=string.Join(";",Physics.RaycastAll(view.transform.position,view.transform.forward,3.4f,~0,QueryTriggerInteraction.Ignore).OrderBy(hit=>hit.distance).Select(hit=>hit.transform.name+"@"+hit.distance.ToString("F3")));
                Check(selected==device.GetComponent<SlopCityInteractable>(),"Ordinary aim selects exact physical control "+device.Mechanic+"/"+device.Slot+"; actual="+(selected?selected.DisplayName:"none")+" transform="+(hitTransform?hitTransform.name:"none")+" eye="+view.transform.position+" target="+device.transform.position+" rayhits="+hits);
                Check(sandbox.Interact(view),"Ordinary USE dispatches selected physical control.");return;
            }
            bool handled=(bool)Call(sandbox,"TryInteractUniqueDevice",args);
            if(device.gameObject.activeInHierarchy)Check(handled,"Actual admitted named control is handled.");else Check(!handled,"Inactive proxy cannot perform work.");
            if(handled){if((bool)args[2])Call(sandbox,"AdvanceContract");else Call(sandbox,"Save");}
        }
        private static readonly Collider[] ApproachOverlaps=new Collider[128];
        private static readonly RaycastHit[] ApproachHits=new RaycastHit[128];
        private static bool Approach(Vector3 point,Func<bool> exactTarget)
        {
            foreach(float radius in new[]{1.75f,2.4f,2.8f,1.15f,.65f})for(int direction=0;direction<24;direction++)
            {
                Vector3 offset=Quaternion.Euler(0,180+direction*15f,0)*Vector3.forward*radius;
                int count=Physics.RaycastNonAlloc(point+offset+Vector3.up*.9f,Vector3.down,ApproachHits,4,~0,QueryTriggerInteraction.Ignore);
                if(count==ApproachHits.Length)continue;bool supported=false;RaycastHit support=default;
                for(int h=0;h<count;h++)if(ApproachHits[h].normal.y>.8f&&!ApproachHits[h].transform.IsChildOf(player.transform)&&(!supported||ApproachHits[h].distance<support.distance)){support=ApproachHits[h];supported=true;}
                if(!supported)continue;Vector3 feet=support.point+Vector3.up*.025f;Vector3 eye=feet+Vector3.up*1.62f;if((point-eye).magnitude>3.4f)continue;
                count=Physics.OverlapCapsuleNonAlloc(feet+Vector3.up*.24f,feet+Vector3.up*1.5f,.215f,ApproachOverlaps,~0,QueryTriggerInteraction.Ignore);
                bool blocked=count==ApproachOverlaps.Length;for(int n=0;n<count;n++)if(ApproachOverlaps[n]&&!ApproachOverlaps[n].transform.IsChildOf(player.transform))blocked=true;if(blocked)continue;
                Place(feet);view.transform.LookAt(point);if(exactTarget())return true;
            }
            return false;
        }
        private static void Aim(SlopCitySalvageItem target,int i)
        {Check(Approach(target.WorldBounds.center,()=>{object[] args={view,null};return (bool)Call(sandbox,"FindSalvage",args)&&(string)Get(args[1],"id")==target.StableId;}),"Supported clear normal aim reaches the exact salvage component "+target.StableId);}
        private static void Clean(int i)
        {
            var target=Object.FindObjectsByType<SlopCitySalvageItem>(FindObjectsInactive.Include,FindObjectsSortMode.None).First(x=>x.StableId==world.Get(i).dustyId);
            Aim(target,i);for(int n=0;n<4;n++)Check(sandbox.CleanAimed(view),"CLEAN performs one real dust stroke.");
            Check((int)Call(sandbox,"SalvageCleanStrokes",target.StableId)==4,"Four strokes persist on the same item identity.");
        }
        private static void TakeOwnedInstallationPart(int i)
        {
            var target=world.Get(i).movingPart.GetComponent<SlopCitySalvageItem>();Aim(target,i);
            Check(sandbox.TakeAimed(view)&&!target.gameObject.activeSelf&&sandbox.Inventory.Any(entry=>entry.id==target.StableId),"Ordinary TAKE supplies the same actual installation component.");
        }
        private static void TakePanel(int i)
        {
            var target=world.Get(i).movingPart.GetComponent<SlopCitySalvageItem>();Aim(target,i);object[] aimArgs={view,null};Check((bool)Call(sandbox,"FindSalvage",aimArgs),"Actual panel is reachable by ordinary aim.");string aimedId=(string)Get(aimArgs[1],"id");Check(aimedId==target.StableId,"Ordinary TAKE ray selects exact panel: expected "+target.StableId+" actual "+aimedId);Check(sandbox.TakeAimed(view),"Actual removable panel enters persistent inventory through ordinary TAKE.");
            Check(!target.gameObject.activeSelf&&Record(i).phase==1,"Panel source advances only after actual geometry removal: active="+target.gameObject.activeSelf+", phase="+Record(i).phase+", expected="+target.StableId+", inventory="+string.Join(";",sandbox.Inventory.Select(e=>e.id)));
        }
        private static void Ride(int i)
        {
            var route=((Transform[])Get(sandbox,"uniqueRoutes"))[i].GetComponent<SlopCityMetroPlatform>();Check(route,"An actual platform is assigned.");
            Place(route.transform.position+Vector3.up*20f);SlopCityMetroTrain train=null;
            for(int tick=0;tick<2400&&train==null;tick++)
            {foreach(var t in metro.Trains)t.Advance(.05f);train=metro.Trains.FirstOrDefault(t=>!t.IsMoving&&t.CurrentStop==route.StopIndex&&t.DoorsOpen);}
            Check(train,"An actual automatic train reaches the assigned platform.");Place(train.transform.TransformPoint(new Vector3(0,.15f,.35f)));capsule.Move(Vector3.down*.02f);
            Check(train.PlayerInside,"The enabled capsule is aboard the real carriage.");
            for(int tick=0;tick<600&&Record(i).phase==1;tick++){foreach(var t in metro.Trains)t.Advance(.05f);capsule.Move(Vector3.down*.02f);}
            Check(Record(i).phase==2&&Record(i).ridden&&train.PlayerInside,"Actual carried arrival unlocks the destination socket.");
            var saved=sandbox.CaptureUniqueJobState();sandbox.RestoreUniqueJobState(saved);Check(world.Get(i).remoteControl,"Saved arrival pose reconstructs its exact socket.");
        }
        private static void RideLift(int i)
        {
            var route=((Transform[])Get(sandbox,"uniqueRoutes"))[i].GetComponent<SlopCityInteractable>();Check(route,"An actual upper-level route is assigned.");Place(route.InteractionPoint);buildings.RefreshStreaming();
            var button=route.GetComponent<SlopCityElevatorButton>();Check(button&&button.Elevator,"The assigned building admits its real physical lift.");var lift=button.Elevator;
            Place(lift.Cabin.TransformPoint(new Vector3(0,.035f,0)));sandbox.BeginElevatorTravel(route,lift,lift.FloorCount-1);Check(lift.RequestFloor(lift.FloorCount-1),"Physical lift accepts the destination.");
            for(int tick=0;tick<4000&&Record(i).phase==1;tick++){lift.Tick(.05f);capsule.Move(Vector3.down*.02f);}
            Check(Record(i).phase==2&&Record(i).ridden,"Observed occupied lift arrival unlocks the roof socket.");
        }
        private static void CarWork(int i,bool cargo)
        {
            var car=Object.FindObjectsByType<SlopCityInteractable>(FindObjectsSortMode.None).First(x=>x.Kind==SlopCityInteractionKind.Car);
            // Bounded placement only sets up the fixture. The measured travel below uses production driving.
            Check(world.Get(i).vehicleStaging!=Vector3.zero&&Vector3.Distance(world.Get(i).vehicleStaging,world.Get(i).root.position)<=7.5f,"Fuel/loading work has a real supported carriageway bay within ordinary vehicle reach.");
            car.transform.SetPositionAndRotation(world.Get(i).vehicleStaging,Quaternion.Euler(0,90,0));Physics.SyncTransforms();
            if(cargo)Sequence(i,0,1);else Use(i,0);
            string loadedId=Record(i).vehicleId;var assigned=Object.FindObjectsByType<SlopCityInteractable>(FindObjectsSortMode.None).FirstOrDefault(item=>item.Kind==SlopCityInteractionKind.Car&&SlopCitySalvageItem.SourceIdentity(item.transform)==loadedId);
            Check(assigned,"Actual nearby fuel/loading operation identifies the car that received its resources.");car=assigned;
            var input=player.GetComponent<SlopCityPlayerInput>();input.SetGameplayEnabled(true);Set(sandbox,"input",input);Call(sandbox,"StartDriving",car,view);input.SetTouchMovement(Vector2.up);
            float direction=1;int stopped=0;
            for(int tick=0;tick<480&&Record(i).values[6]<(cargo?2000:500);tick++)
            {Vector3 before=car.transform.position;Call(sandbox,"Drive",.05f);if((car.transform.position-before).sqrMagnitude<.000001f)stopped++;else stopped=0;if(stopped>=40){direction=-direction;input.SetTouchMovement(Vector2.up*direction);stopped=0;}}
            input.SetTouchMovement(Vector2.zero);Call(sandbox,"StopDriving",true);
            Check(Record(i).values[6]>=(cargo?2000:500),"Only actual supported car movement accumulates the required cargo/fuel distance.");Use(i,cargo?2:1);
        }
        private static void RepeatAndRecoverCleanWork()
        {
            var spare=Object.FindObjectsByType<SlopCitySalvageItem>(FindObjectsSortMode.None).First(item=>item.gameObject.activeInHierarchy&&item.Pickable&&item.BaseValue>=24&&item.StableId.StartsWith("salvage.v1",StringComparison.Ordinal)&&!item.GetComponent<SlopCitySalvageSurface>());
            Aim(spare,0);Check(sandbox.TakeAimed(view),"Buyback funds come from an actual removable city object.");
            var shop=Object.FindFirstObjectByType<SlopCityPawnCounter>();Check(shop&&Approach(shop.transform.position,()=>sandbox.CanSellAt(shop)),"Reach the real counter to earn buyback funds.");
            int credits=sandbox.Credits;Check(sandbox.SellInventory(spare.StableId,shop)&&sandbox.Credits>credits,"A second real salvage sale funds the buyback premium without changing credits directly.");
            foreach(int i in new[]{11,15,36})
            {
                int priorRun=Record(i).run;Check(sandbox.SelectJob(SlopCityJobCatalog.All[i].id),"Select second maintenance rotation.");
                Check(Record(i).run==priorRun+1,"Repeat work preserves history and starts a separate run.");Use(i,i==36?1:0);
                Check(Record(i).completed,"Actual previously polished finish is usable without an impossible extra CLEAN stroke.");
                Check(sandbox.SelectJob(SlopCityJobCatalog.All[i].id),"Select exact-surface recovery maintenance.");
                var target=world.Get(i).dustyTarget.GetComponent<SlopCitySalvageItem>();Aim(target,i);
                Check(sandbox.TakeAimed(view)&&!target.gameObject.activeSelf,"Actual maintenance surface may be taken without leaving its geometry behind.");
                var pawn=Object.FindFirstObjectByType<SlopCityPawnCounter>();Check(pawn,"Physical pawn counter exists.");
                Check(Approach(pawn.transform.position,()=>sandbox.CanSellAt(pawn)),"A real supported nearby position reaches the pawn counter.");
                Check(sandbox.SellInventory(target.StableId,pawn),"The exact maintenance surface enters real persistent pawn stock.");
                Use(i,0);Check(!target.gameObject.activeSelf&&!Record(i).completed,"A sold surface cannot be fabricated or awarded as mounted work.");
                Check(Approach(pawn.transform.position,()=>sandbox.CanSellAt(pawn)),"Return physically to the buyback counter.");
                Check(sandbox.BuyBackInventory(target.StableId,pawn),"Buyback restores the same ID and retained finish; credits="+sandbox.Credits+"; feedback="+receipt.feedback.LastOrDefault());
                Use(i,0);Check(target.gameObject.activeSelf&&!sandbox.Inventory.Any(entry=>entry.id==target.StableId)&&Vector3.Distance(target.transform.position,world.Get(i).root.TransformPoint(world.Get(i).dustyLocalPosition))<.01f,"USE reinstalls the exact component at its original clear mount.");
                Check((int)Call(sandbox,"SalvageCleanStrokes",target.StableId)==4,"Reinstallation retains all four actual cleaning strokes.");
                if(i==36)Use(i,1);Check(Record(i).completed,"Recovered polished maintenance surface remains useful to the real mission.");
            }
        }
        private static void CompletedPumpOffAssignment()
        {
            Check(Record(13).completed&&Record(13).awarded,"Completed pump round exists before its independent public-service purchase.");
            Check(sandbox.SelectJob(SlopCityJobCatalog.All[25].id),"Keep a different mission active while visiting the physical pump.");
            string assignment=(string)Get(sandbox,"currentJobId");int credits=sandbox.Credits,stock=Record(13).values[7],cans=sandbox.InventoryCount("fuel");
            Use(13,2);
            Check((string)Get(sandbox,"currentJobId")==assignment&&Record(13).completed&&Record(13).awarded,"Public pump service neither changes the active assignment nor awards old completed work.");
            Check(sandbox.InventoryCount("fuel")==cans+1&&sandbox.Credits==credits-5&&Record(13).values[7]==stock+1,"Ordinary off-assignment USE buys one paid can from remaining finite completed-round stock.");
        }
        private static void PortableFuelRecovery()
        {
            Check(sandbox.SelectJob(SlopCityJobCatalog.All[13].id),"Select the actual finite roadside fuel stock.");
            var car=Object.FindObjectsByType<SlopCityInteractable>(FindObjectsSortMode.None).First(item=>item.Kind==SlopCityInteractionKind.Car);
            // Restored empty-tank checkpoint is setup; refill, payment and interaction use real production actions.
            string id=SlopCitySalvageItem.SourceIdentity(car.transform);var saved=sandbox.CaptureUniqueJobState();var tanks=saved.vehicles.ToList();var tank=tanks.Find(entry=>entry.id==id);
            if(tank==null){tank=new SlopCitySandbox.UniqueVehicleRecord{id=id};tanks.Add(tank);}tank.fuel=0;saved.vehicles=tanks.ToArray();sandbox.RestoreUniqueJobState(saved);
            Check(!(bool)Call(sandbox,"CanDriveJobVehicle",car.transform),"A restored empty tank cannot drive.");
            int before=sandbox.Credits,priorCans=sandbox.InventoryCount("fuel");Use(13,2);Check(sandbox.InventoryCount("fuel")==priorCans+1&&sandbox.Credits==before-5&&Record(13).values[7]==1,"Actual pump USE conserves credits, finite reservoir stock and one owned can.");
            Check(Approach(car.InteractionPoint,()=>{Set(sandbox,"nextTargetAt",0f);return Get(Call(sandbox,"FindTarget",view),"interaction") as SlopCityInteractable==car;}),"Reach the actual stranded car with ordinary aim.");
            Check(sandbox.Interact(view)&&!sandbox.IsDriving,"First ordinary car USE performs the physical refill instead of entering an empty car.");
            var refilled=sandbox.CaptureUniqueJobState().vehicles.First(entry=>entry.id==id);Check(refilled.fuel==25&&sandbox.InventoryCount("fuel")==priorCans,"Exactly one owned can becomes 25 saved fuel units.");
            Check(sandbox.Interact(view)&&sandbox.IsDriving,"Second ordinary USE drives the refuelled actual car.");Call(sandbox,"StopDriving",true);
        }
        private static void OwnedSupplyQuantityGuard()
        {
            var bag=(IList)Get(sandbox,"inventory");bag.Clear();
            Check((bool)Call(sandbox,"InventoryAdd","fixture/one-owned-pane","One recovered pane","window",1,12,4),"One owned polished pane is admitted for quantity guard.");
            var record=new SlopCitySandbox.UniqueJobRecord{id=SlopCityJobCatalog.All[1].id};object[] args={record,0,"window",2,12,""};
            Check((bool)Call(sandbox,"Issue",args),"Fixture source can provide its requested two-piece stock.");
            Check(string.IsNullOrEmpty(record.sourceIds[0])&&sandbox.Inventory.First(entry=>entry.id=="fixture/one-owned-pane").quantity==1,"A one-piece owned supply cannot satisfy a two-piece request or change its finish.");
            bag.Clear();
            Check((bool)Call(sandbox,"InventoryAdd","fixture/owned-pane","Recovered polished pane","window",1,12,4),"Polished owned pane admitted.");
            record=new SlopCitySandbox.UniqueJobRecord{id=SlopCityJobCatalog.All[1].id};int credits=sandbox.Credits;args=new object[]{record,0,"window",1,12,""};
            Check((bool)Call(sandbox,"Issue",args)&&record.sourceIds[0]=="fixture/owned-pane"&&record.replacements[0]==0&&sandbox.Credits==credits&&sandbox.Inventory.First().cleanStrokes==4&&sandbox.Inventory.Count==1,"First issue binds exact owned finish without minting, charging or spending replacement stock.");
            bag.Clear();
        }
        private static void MalformedFuelArchive()
        {
            var original=sandbox.CaptureUniqueJobState();var cars=Object.FindObjectsByType<SlopCityInteractable>(FindObjectsSortMode.None).Where(item=>item.Kind==SlopCityInteractionKind.Car).ToArray();
            Check(cars.Length>=2&&cars.Length<=32,"Actual supported fleet fits the fixed fuel registry.");
            var malformed=sandbox.CaptureUniqueJobState();malformed.vehicles=new SlopCitySandbox.UniqueVehicleRecord[32];
            string preserved=SlopCitySalvageItem.SourceIdentity(cars[0].transform);
            malformed.vehicles[0]=new SlopCitySandbox.UniqueVehicleRecord{id=preserved,fuel=7.5f};
            for(int i=1;i<32;i++)malformed.vehicles[i]=new SlopCitySandbox.UniqueVehicleRecord{id="unknown-save-car/"+i,fuel=0};
            sandbox.RestoreUniqueJobState(malformed);
            Check((bool)Call(sandbox,"CanDriveJobVehicle",cars[1].transform),"Unknown archive identifiers cannot exclude an actual supported car.");
            var restored=sandbox.CaptureUniqueJobState();Check(restored.vehicles.Length<=32&&restored.vehicles.First(record=>record.id==preserved).fuel==7.5f,"Capacity pruning preserves the actual car's exact remaining fuel.");
            Check(restored.vehicles.All(record=>!record.id.StartsWith("unknown-save-car/",StringComparison.Ordinal)),"Only identifiers outside the real cached fleet are pruned.");
            sandbox.RestoreUniqueJobState(original);
        }
        private static void DurableOutcomeRepeat()
        {
            var original=sandbox.CaptureUniqueJobState();
            foreach(int i in new[]{1,17,37,39})
            {
                int installed=Record(i).installed;Check(installed!=0,"Successful physical installation has an independent durable outcome.");
                Vector3 position=Record(i).installedPosition;Check(sandbox.SelectJob(SlopCityJobCatalog.All[i].id),"Select another installation round.");
                Check(Record(i).installed==installed&&Record(i).phase==0&&!Record(i).completed,"A fresh work order preserves the completed physical assembly separately from new stages.");
                if(i==1||i==37)Check(world.Get(i).remotePane.gameObject.activeInHierarchy&&Record(i).installedPosition==position,"New dispatch cannot make the installed exact pane disappear or move.");
                if(i==17)Check(world.Get(i).poster.gameObject.activeInHierarchy,"The previous protected poster remains mounted on the next round.");
                if(i==39)Check(world.Get(i).guard.All(slat=>slat.gameObject.activeInHierarchy),"All fitted guard slats remain in the world on the next round.");
            }
            sandbox.RestoreUniqueJobState(original);
        }
        private static void RestoreVisibleOutcomes()
        {
            var saved=sandbox.CaptureUniqueJobState();string before=JsonUtility.ToJson(saved);
            foreach(var item in Object.FindObjectsByType<SlopCitySalvageItem>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(item.Category=="mailbox")foreach(string name in new[]{"Delivered_Parcel","Delivered_Letters"}){var marker=item.transform.Find(name);if(marker)Object.DestroyImmediate(marker.gameObject);}
            foreach(int mechanic in new[]{1,37}){var site=world.Get(mechanic);if(site.remoteControl)Object.DestroyImmediate(site.remoteControl.transform.parent.gameObject);if(site.installedRoot)Object.DestroyImmediate(site.installedRoot.gameObject);}
            sandbox.RestoreUniqueJobState(saved);
            Check(JsonUtility.ToJson(sandbox.CaptureUniqueJobState())==before,"Visual reconstruction does not change saved destination identities or device progress.");
            foreach(var delivered in saved.mail)
            {var box=Object.FindObjectsByType<SlopCitySalvageItem>(FindObjectsInactive.Include,FindObjectsSortMode.None).First(x=>x.StableId==delivered.id);if(delivered.parcel)Check(box.transform.Find("Delivered_Parcel"),"Saved parcel contents return to their exact mailbox.");if(delivered.letters)Check(box.transform.Find("Delivered_Letters"),"Saved letter contents return to their exact mailbox.");}
            foreach(int mechanic in new[]{1,37})Check(world.Get(mechanic).remotePane&&world.Get(mechanic).remotePane.gameObject.activeSelf,"Completed installed pane survives physical worksite reconstruction.");
        }
        private static void SaveRoundtrip()
        {
            var snapshot=sandbox.CaptureUniqueJobState();string bytes=JsonUtility.ToJson(snapshot);snapshot.records[0].values[0]=9999;
            Check(sandbox.CaptureUniqueJobState().records[0].values[0]!=9999,"Rollback snapshot does not alias live job state.");
            sandbox.RestoreUniqueJobState(JsonUtility.FromJson<SlopCitySandbox.UniqueJobState>(bytes));Check(JsonUtility.ToJson(sandbox.CaptureUniqueJobState())==bytes,"All40 device states survive exact serialization roundtrip.");
        }
        private static SlopCitySandbox.UniqueJobRecord Record(int i)=>((SlopCitySandbox.UniqueJobState)Get(sandbox,"uniqueJobState")).records[i];
        private static void Place(Vector3 point){capsule.enabled=false;player.transform.position=point;capsule.enabled=true;view.transform.localPosition=Vector3.up*1.62f;Physics.SyncTransforms();}
        private static object Get(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
        private static void Set(object o,string name,object value)=>o.GetType().GetField(name,Flags).SetValue(o,value);
        private static object Call(object o,string name,params object[] args)
        {try{return o.GetType().GetMethod(name,Flags).Invoke(o,args);}catch(TargetInvocationException e){throw e.InnerException;}}
        private static void Check(bool ok,string message){receipt.checks++;if(!ok)throw new InvalidOperationException(message);}
        private static void Log(string text,string trace,LogType kind){if(kind==LogType.Error||kind==LogType.Exception||kind==LogType.Assert)receipt.runtimeErrors.Add(text+"\n"+trace);}
        private static string Argument(string key){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        [Serializable]private sealed class SiteReceipt{public string id;public Vector3 position,carStaging;}
        [Serializable]private sealed class Backup{public bool exists;public string text;}
        [Serializable]private sealed class Receipt
        {
            public bool passed,preferencesRestored;public string startedAtUtc,finishedAtUtc;public int checks;
            public string scope="Actual saved scene, bounded setup placements, production physical controls and inventory/resource mutations. Real train/lift carry checked through production ticks. Five media/resident observations require companion fixture. No whole-city walk, network, device/performance/audio claim, source scene save or build.";
            public List<string> feedback=new List<string>();
            public List<SiteReceipt> sites=new List<SiteReceipt>();
            public List<string> exercised=new List<string>(),completed=new List<string>(),errors=new List<string>(),runtimeErrors=new List<string>();public List<int> observationGuards=new List<int>();
        }
    }
}
#endif
