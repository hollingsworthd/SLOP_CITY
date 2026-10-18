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
using Object = UnityEngine.Object;
namespace SlopCity.Editor
{
    /// <summary>Production ray targeting, visible finish properties, money/save atomicity and completed touch actions.</summary>
    public static class SlopCitySalvageInventorySmokeTest
    {
        private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly string[] Keys = { "SlopCity.Sandbox.Progress.v1", "SlopCity.Sandbox.Progress.v1.recovery", "SlopCity.MatrixSearch.Progress.v1", "SlopCity.MatrixSearch.Progress.v1.recovery" };
        private static Receipt report; private static SlopCityFirstPerson player; private static SlopCitySandbox sandbox;
        private static Camera camera; private static CharacterController controller; private static GameObject probes;
        private static readonly List<Material> OwnedMaterials = new List<Material>();
        private static readonly Vector3 Feet = new Vector3(0,1000,0);
        private static FieldInfo Store => typeof(SlopCitySandbox).GetField("ProgressStoreForTesting", Fields);
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || Application.companyName != "SlopCityValidation-v0116" || !Application.persistentDataPath.Contains("SlopCityValidation-v0116"))
                throw new InvalidOperationException("Isolated SlopCityValidation-v0116 batch Edit Mode required.");
            report = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            var backups = Keys.ToDictionary(k=>k,k=>new Backup { exists=PlayerPrefs.HasKey(k), value=PlayerPrefs.GetString(k,"") });
            var priorStore = Store.GetValue(null); float scale=Time.timeScale; var random=UnityEngine.Random.state;
            Application.logMessageReceived += Log;
            try
            {
                foreach (string key in Keys) PlayerPrefs.DeleteKey(key);
                PlayerPrefs.SetString(Keys[0],"{\"credits\":1000,\"experience\":0,\"salvage\":0,\"completed\":0,\"water\":0}"); PlayerPrefs.Save();
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath,OpenSceneMode.Single);
                foreach (var sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include,FindObjectsSortMode.None)) sync.enabled=false;
                foreach (var media in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include,FindObjectsSortMode.None)) media.enabled=false;
                player=Object.FindFirstObjectByType<SlopCityFirstPerson>(); Check(player,"Saved-scene player exists."); player.enabled=false;
                controller=player.GetComponent<CharacterController>(); camera=player.ViewCamera;
                Set(player,"controller",controller); Set(player,"paused",false); Set(player,"seated",false); Set(player,"externalMovement",false); Time.timeScale=1;
                probes=new GameObject("Salvage_Inventory_Isolated_Physics_Probe");
                Place(); Reload();
                var cube=Cube("Dusty_Lamp","fixture/lamp",true);
                var renderer=cube.GetComponent<Renderer>(); var block=new MaterialPropertyBlock(); block.SetFloat("_FixturePreserved",.731f); renderer.SetPropertyBlock(block);
                cube.SetCleanProgress(0); renderer.GetPropertyBlock(block); float previous=block.GetFloat("_Glossiness");
                for(int stroke=1;stroke<=4;stroke++)
                {
                    Check(sandbox.CleanAimed(camera),"Production CLEAN succeeds for dusty stroke "+stroke);
                    renderer.GetPropertyBlock(block); Check(block.GetFloat("_Glossiness")>previous,"Each stroke visibly increases material finish smoothness."); previous=block.GetFloat("_Glossiness");
                    Check(Mathf.Abs(block.GetFloat("_FixturePreserved")-.731f)<.0001f,"Cleaning preserves unrelated renderer property ownership.");
                }
                Check(!sandbox.CleanAimed(camera),"A polished object cannot repeatedly create cleaning progress.");
                sandbox.SalvageOptions(camera,out bool clean,out bool take); Check(!clean&&take,"Contextual CLEAN disappears at four strokes while TAKE remains available.");
                Check(sandbox.TakeAimed(camera),"Production TAKE picks the physically aimed removable lamp.");
                Check(!cube.gameObject.activeSelf&&!cube.GetComponent<Collider>().gameObject.activeInHierarchy,"Taken object is inactive (its collider is absent from active physics).");
                Check(!Physics.Raycast(camera.transform.position,camera.transform.forward,3f),"Taken collider no longer blocks the real aiming ray.");
                Check(sandbox.Inventory.Count==1&&sandbox.Inventory[0].id=="fixture/lamp"&&sandbox.Inventory[0].cleanStrokes==4,"Inventory retains exact identity and four-stroke condition.");
                int salePrice=sandbox.Inventory[0].UnitPrice; string saved=PlayerPrefs.GetString(Keys[0]);
                Object.DestroyImmediate(cube.gameObject); Reload(); cube=Cube("Dusty_Lamp","fixture/lamp",true);
                Check(!cube.gameObject.activeSelf&&sandbox.Inventory.Count==1&&sandbox.Inventory[0].id=="fixture/lamp","Reload and same-ID re-admission do not resurrect a bagged object.");
                Check(PlayerPrefs.GetString(Keys[0])==saved,"Reload/admission do not rewrite authoritative saved object bytes.");
                var plain=Cube("Plain_Mailbox","fixture/plain",false);
                sandbox.SalvageOptions(camera,out clean,out take); Check(!clean&&take&&!sandbox.CleanAimed(camera),"A non-dust-prone object offers normal pickup without a cleaning requirement."); Object.DestroyImmediate(plain.gameObject);
                foreach(string name in new[]{"Protected_Floor","Protected_Elevator","Protected_Poster","Protected_Billboard"})
                {
                    var item=Cube(name,"fixture/"+name,false); Check(!item.ValidatePickup()&&!sandbox.TakeAimed(camera)&&item.gameObject.activeSelf,"Protected physical object refuses pickup: "+name); Object.DestroyImmediate(item.gameObject);
                }
                FinishOwnership();
                PawnTransactions(salePrice);
                CapacityAndRollback();
                PawnStockRotation();
                InputLifecycle();
                Check(report.runtimeErrors.Count==0,"No runtime errors or exceptions were logged.");
            }
            catch(Exception exception) { report.errors.Add(exception.ToString()); }
            finally
            {
                Store.SetValue(null,priorStore);
                foreach(var value in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsInactive.Include,FindObjectsSortMode.None)) Object.DestroyImmediate(value);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                foreach(var material in OwnedMaterials)if(material)Object.DestroyImmediate(material);OwnedMaterials.Clear();
                foreach(var pair in backups) if(pair.Value.exists)PlayerPrefs.SetString(pair.Key,pair.Value.value);else PlayerPrefs.DeleteKey(pair.Key); PlayerPrefs.Save();
                report.savesRestored=backups.All(p=>PlayerPrefs.HasKey(p.Key)==p.Value.exists&&(!p.Value.exists||PlayerPrefs.GetString(p.Key)==p.Value.value));
                if(!report.savesRestored)report.errors.Add("Four save/recovery keys were not restored.");
                Time.timeScale=scale;UnityEngine.Random.state=random;Application.logMessageReceived-=Log;
            }
            report.passed=report.errors.Count==0&&report.runtimeErrors.Count==0;report.finishedAtUtc=DateTime.UtcNow.ToString("O");
            string path=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Builds/Reports/salvage-inventory-check.json");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));
            Debug.Log("SLOP_CITY_SALVAGE_INVENTORY_"+(report.passed?"PASSED":"FAILED")+" "+report.checks+" checks");EditorApplication.Exit(report.passed?0:1);
        }
        private static void FinishOwnership()
        {
            var bench=Cube("Timber_SeatSlat","fixture/finish-bench",true);
            var surface=bench.gameObject.AddComponent<SlopCityInteractable>();surface.Configure(SlopCityInteractionKind.Bench,"Fixture bench");
            var renderer=bench.GetComponent<Renderer>();var block=new MaterialPropertyBlock();bench.SetCleanProgress(0);
            surface.Pulse(renderer);Check(Get(surface,"pulseRenderer")!=null,"Production highlight begins on the dusty physical surface.");
            Check(sandbox.CleanAimed(camera),"A real CLEAN stroke succeeds while the surface is highlighted.");renderer.GetPropertyBlock(block);float gloss=block.GetFloat("_Glossiness");Color color=block.GetColor("_Color");
            Set(surface,"pulseUntil",-1f);Invoke(surface,"Update");Invoke(surface,"RestorePulse");renderer.GetPropertyBlock(block);
            Check(Mathf.Abs(block.GetFloat("_Glossiness")-gloss)<.0001f&&Near(block.GetColor("_Color"),color),"Expired highlight restoration retains the new clean color and gloss.");
            for(int stroke=1;stroke<4;stroke++)Check(sandbox.CleanAimed(camera),"Finish remaining bench stroke "+stroke);
            renderer.GetPropertyBlock(block);gloss=block.GetFloat("_Glossiness");color=block.GetColor("_Color");
            surface.RestoreWorkState(new SlopCityInteractable.WorkState{initialized=true,condition=60,clean=false,powerInitialized=true,powerOn=true});renderer.GetPropertyBlock(block);
            Check(Mathf.Abs(block.GetFloat("_Glossiness")-gloss)<.0001f&&Near(block.GetColor("_Color"),color),"Bench work-visual refresh retains its actual four-stroke finish even with legacy clean=false.");
            Object.DestroyImmediate(bench.gameObject);
            var lamp=Cube("Lamp_Lens","fixture/finish-lamp",true);renderer=lamp.GetComponent<Renderer>();
            var shader=Shader.Find("Standard");Check(shader,"Standard fixture finish/emission shader is available.");var material=new Material(shader);OwnedMaterials.Add(material);
            material.SetColor("_Color",Color.white);material.SetColor("_EmissionColor",new Color(.8f,.6f,.2f));renderer.sharedMaterial=material;
            var light=lamp.gameObject.AddComponent<Light>();light.intensity=2f;var device=lamp.gameObject.AddComponent<SlopCityInteractable>();device.Configure(SlopCityInteractionKind.Light,"Fixture lamp");
            device.RestoreWorkState(new SlopCityInteractable.WorkState{initialized=true,condition=100,powerInitialized=true,powerOn=true});
            for(int stroke=0;stroke<4;stroke++)Check(sandbox.CleanAimed(camera),"Finish powered lamp stroke "+stroke);
            renderer.GetPropertyBlock(block);gloss=block.GetFloat("_Glossiness");Color on=block.GetColor("_Color");Color emission=block.GetColor("_EmissionColor");
            Invoke(device,"ApplyWorkVisuals");renderer.GetPropertyBlock(block);Check(Mathf.Abs(block.GetFloat("_Glossiness")-gloss)<.0001f&&Near(block.GetColor("_Color"),on),"Powered lamp work visuals retain the polished finish.");
            Check(!device.ToggleLights(),"Production lamp power toggles off.");renderer.GetPropertyBlock(block);
            Check(!light.enabled&&light.intensity==0&&emission.maxColorComponent>0&&block.GetColor("_EmissionColor").maxColorComponent<.0001f,"Power off still disables real light output and emission after cleaning.");
            Check(Mathf.Abs(block.GetFloat("_Glossiness")-gloss)<.0001f&&block.GetColor("_Color").grayscale<on.grayscale*.3f,"A powered-off polished lamp retains gloss while its color correctly dims.");
            Object.DestroyImmediate(lamp.gameObject);
        }
        private static bool Near(Color a,Color b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)+Mathf.Abs(a.a-b.a)<.001f;
        private static void PawnTransactions(int salePrice)
        {
            var counter=GameObject.CreatePrimitive(PrimitiveType.Cube);counter.name="Fixture_Pawn_Counter";counter.transform.SetParent(probes.transform);counter.transform.position=Feet+new Vector3(0,1.3f,2);counter.transform.localScale=new Vector3(1,1,.5f);var pawn=counter.AddComponent<SlopCityPawnCounter>();Physics.SyncTransforms();
            Check(sandbox.CanSellAt(pawn),"Nearby visible physical counter admits inventory sales.");
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(probes.transform);wall.transform.position=Feet+new Vector3(0,1.3f,1);wall.transform.localScale=new Vector3(3,3,.15f);Physics.SyncTransforms();
            Check(!sandbox.CanSellAt(pawn)&&!sandbox.SellInventory("fixture/lamp",pawn),"A real intervening wall blocks pawn sales.");Object.DestroyImmediate(wall);Physics.SyncTransforms();
            counter.transform.position+=Vector3.forward*10;Physics.SyncTransforms();Check(!sandbox.CanSellAt(pawn),"A remote counter cannot sell inventory.");counter.transform.position-=Vector3.forward*10;Physics.SyncTransforms();
            int before=sandbox.Credits;
            Fault(()=>sandbox.SellInventory("fixture/lamp",pawn),()=>sandbox.Credits==before&&sandbox.Inventory.Count==1&&sandbox.PawnStock.Count==0,"sale");
            Check(sandbox.SellInventory("fixture/lamp",pawn)&&sandbox.Credits==before+salePrice&&sandbox.Inventory.Count==0&&sandbox.PawnStock.Count==1,"One real item sale transfers one item and its exact credits.");
            Check(!sandbox.SellInventory("fixture/lamp",pawn)&&sandbox.Credits==before+salePrice,"Repeated sale cannot duplicate money.");
            Reload();
            Check(sandbox.Inventory.Count==0&&sandbox.PawnStock.Count==1&&sandbox.PawnStock[0].id=="fixture/lamp"&&sandbox.PawnStock[0].cleanStrokes==4,"Sold stock retains exact original identity and condition across reload.");
            var readmitted=Cube("Readmitted_Sold_Lamp","fixture/lamp",true);Check(!readmitted.gameObject.activeSelf,"Re-admission cannot resurrect an original while it belongs to pawn stock.");Object.DestroyImmediate(readmitted.gameObject);
            int buyPrice=sandbox.BuyBackPrice(sandbox.PawnStock[0]);int soldBalance=sandbox.Credits;
            Fault(()=>sandbox.BuyBackInventory("fixture/lamp",pawn),()=>sandbox.Credits==soldBalance&&sandbox.Inventory.Count==0&&sandbox.PawnStock.Count==1,"buyback");
            Check(sandbox.BuyBackInventory("fixture/lamp",pawn)&&sandbox.Credits==soldBalance-buyPrice,"Buyback debits the exact displayed price.");
            Check(sandbox.Inventory.Count==1&&sandbox.Inventory[0].id=="fixture/lamp"&&sandbox.Inventory[0].cleanStrokes==4&&sandbox.Inventory[0].baseValue==20&&sandbox.PawnStock.Count==0,"Buyback preserves original identity, value and polished condition.");
            Check(!sandbox.BuyBackInventory("fixture/lamp",pawn)&&sandbox.Credits==soldBalance-buyPrice,"Repeated buyback cannot duplicate items or debit credits.");
            Reload(); Check(sandbox.Inventory.Count==1&&sandbox.PawnStock.Count==0&&sandbox.Inventory[0].cleanStrokes==4,"Buyback stock and exact condition survive save/reload.");
            Object.DestroyImmediate(counter);
        }
        private static void CapacityAndRollback()
        {
            string saved=PlayerPrefs.GetString(Keys[0]);
            for(int index=1;index<SlopCitySandbox.InventoryCapacity;index++) Check((bool)Invoke(sandbox,"InventoryAdd","fixture/capacity/"+index,"Capacity item","test",1,1,0),"Fill one bounded inventory slot.");
            Check(sandbox.InventorySlotsUsed==64&&!(bool)Invoke(sandbox,"InventoryAdd","fixture/overflow","Overflow","test",1,1,0),"The sixty-fifth inventory slot is refused.");
            var full=Cube("Full_Bag_Lamp","fixture/full",true);Check(!sandbox.TakeAimed(camera)&&full.gameObject.activeSelf&&sandbox.InventorySlotsUsed==64,"A full bag leaves the aimed world object and bag unchanged.");Object.DestroyImmediate(full.gameObject);
            Check(PlayerPrefs.GetString(Keys[0])==saved,"Refused capacity mutation preserves existing saved bytes.");Reload();Check(sandbox.InventorySlotsUsed==1,"Unsaved fixture capacity setup does not leak across reload.");
            var cube=Cube("Rollback_Lamp","fixture/rollback",true);
            var renderer=cube.GetComponent<Renderer>();var block=new MaterialPropertyBlock();cube.SetCleanProgress(0);renderer.GetPropertyBlock(block);float gloss=block.GetFloat("_Glossiness");
            Fault(()=>sandbox.CleanAimed(camera),()=>{renderer.GetPropertyBlock(block);return (int)Invoke(sandbox,"SalvageCleanStrokes",cube.StableId)==0&&Mathf.Abs(block.GetFloat("_Glossiness")-gloss)<.0001f;},"clean");
            Fault(()=>sandbox.TakeAimed(camera),()=>cube.gameObject.activeSelf&&sandbox.InventorySlotsUsed==1&&sandbox.Inventory.All(e=>e.id!=cube.StableId),"pickup");
            Check(Physics.Raycast(camera.transform.position,camera.transform.forward,out RaycastHit hit,3)&&hit.transform==cube.transform,"Failed pickup restores the actual physical collider.");
            Check(sandbox.CleanAimed(camera),"A normal save succeeds after the injected failure is removed.");Object.DestroyImmediate(cube.gameObject);
        }
        private static void Fault(Func<bool> operation,Func<bool> unchanged,string label)
        {
            string before=PlayerPrefs.GetString(Keys[0]);
            Store.SetValue(null,(Action<string,string>)((key,json)=>{PlayerPrefs.SetString(key,json);throw new IOException("Fixture forced storage failure after pending cache write.");}));
            try { Check(!operation(),"Injected "+label+" save failure reports failure.");Check(unchanged(),"Injected "+label+" save failure rolls back money, inventory and world condition.");Check(PlayerPrefs.GetString(Keys[0])==before,"Injected "+label+" failure restores exact previous PlayerPrefs pending bytes.");PlayerPrefs.Save();Check(PlayerPrefs.GetString(Keys[0])==before,"Restored "+label+" bytes remain intact after cache flush.");report.rollbackCases++; }
            finally { Store.SetValue(null,null); }
        }
        private static void PawnStockRotation()
        {
            const string ownedPane="fixture/ordinary-owned-window";
            Place();var physicalPane=Cube("Recovery_Window",ownedPane,true);physicalPane.Configure(ownedPane,"Ordinary recovered window","window",true,true,15);
            for(int stroke=0;stroke<3;stroke++)Check(sandbox.CleanAimed(camera),"Clean the physically recovered ordinary pane before assignment.");
            Check(sandbox.TakeAimed(camera)&&!physicalPane.gameObject.activeSelf&&sandbox.Inventory.Any(e=>e.id==ownedPane&&e.cleanStrokes==3),"Ordinary TAKE retains a real recovered pane and its exact three-stroke condition.");
            Check(sandbox.SelectJob(SlopCityJobCatalog.All[1].id),"Select real pane transport assignment for recovery protection.");UseWorkControl(1,0);
            var pending=sandbox.CaptureUniqueJobState().records[1];
            Check(pending.phase==1&&!pending.completed&&!pending.awarded&&pending.sourceIds[0]==ownedPane,"Production supply interaction binds the ordinary pane's exact ID without minting a duplicate.");
            // A real spare-fuse job can finish with another compatible fuse, leaving its
            // originally issued serial in stock. It must then be eligible for retirement.
            Check(sandbox.SelectJob(SlopCityJobCatalog.All[7].id),"Select physical fuse repair for completed-stock retirement.");
            UseWorkControl(7,1);UseWorkControl(7,2);UseWorkControl(7,3);UseWorkControl(7,0);
            var issued=sandbox.Inventory.FirstOrDefault(e=>e.category=="fuse");Check(issued!=null,"Production supply issues a real identified spare fuse.");string spare=issued.id;
            var counter=GameObject.CreatePrimitive(PrimitiveType.Cube);counter.name="Rotation_Pawn_Counter";counter.transform.SetParent(probes.transform);counter.transform.position=Feet+new Vector3(0,1.3f,2);counter.transform.localScale=new Vector3(1,1,.5f);var pawn=counter.AddComponent<SlopCityPawnCounter>();Place();Physics.SyncTransforms();
            Check(sandbox.SellInventory(ownedPane,pawn)&&sandbox.SellInventory(spare,pawn),"Unfinished exact pane and issued spare enter actual pawn stock through normal sales.");
            Check((bool)Invoke(sandbox,"NeedsPawnRecovery",ownedPane)&&(bool)Invoke(sandbox,"NeedsPawnRecovery",spare),"Both unfinished issued serials are protected, including the ordinary non-job-prefix pane.");
            Check((bool)Invoke(sandbox,"InventoryAdd","fixture/compatible-fuse","Compatible recovered fuse","fuse",1,2,0),"Provide another compatible physical-recovery supply for the fuse job.");UseWorkControl(7,2);
            var complete=sandbox.CaptureUniqueJobState().records[7];Check(complete.completed&&complete.awarded&&!(bool)Invoke(sandbox,"NeedsPawnRecovery",spare),"Real completed fuse repair releases its old unused issued stock from permanent protection.");
            Place();Physics.SyncTransforms();
            int serial=0;while(sandbox.PawnStock.Count<128)
            {
                string id="fixture/stock-rotation/"+serial++;
                Check((bool)Invoke(sandbox,"InventoryAdd",id,"Ordinary rotation stock","test",1,1,0)&&sandbox.SellInventory(id,pawn),"A real one-item sale fills one bounded pawn stock slot.");
            }
            Check(sandbox.PawnStock.Count==128&&sandbox.PawnStock.Any(e=>e.id==ownedPane)&&sandbox.PawnStock.Any(e=>e.id==spare),"Full128-slot stock retains both serials until actual capacity pressure.");
            int before=sandbox.Credits;const string overflow="fixture/stock-overflow";
            Check((bool)Invoke(sandbox,"InventoryAdd",overflow,"Next recovered object","test",1,1,0)&&sandbox.SellInventory(overflow,pawn),"A sale beyond128 unique entries succeeds by rotating recoverable stock.");
            Check(sandbox.PawnStock.Count==128&&sandbox.PawnStock.Any(e=>e.id==ownedPane)&&sandbox.PawnStock.All(e=>e.id!=spare)&&sandbox.PawnStock.Any(e=>e.id==overflow)&&sandbox.Credits==before+1,"Capacity pressure evicts old completed-job stock, protects unfinished exact pane, and credits only the new sale.");
            const string next="fixture/stock-overflow-2";
            Check((bool)Invoke(sandbox,"InventoryAdd",next,"Another recovered object","test",1,1,0)&&sandbox.SellInventory(next,pawn),"Further ordinary stock rotation remains possible.");
            Check(sandbox.PawnStock.Count==128&&sandbox.PawnStock.All(e=>e.id!="fixture/stock-rotation/0")&&sandbox.PawnStock.Any(e=>e.id==ownedPane),"Oldest ordinary stock rotates while the unfinished serial remains recoverable.");
            Reload();Check(sandbox.PawnStock.Count==128&&sandbox.CaptureUniqueJobState().records[1].sourceIds[0]==ownedPane&&sandbox.PawnStock.Any(e=>e.id==ownedPane)&&(bool)Invoke(sandbox,"NeedsPawnRecovery",ownedPane),"Full stock and unfinished exact identity protection survive a true save/reload.");
            int price=sandbox.BuyBackPrice(sandbox.PawnStock.First(e=>e.id==ownedPane));before=sandbox.Credits;
            Check(sandbox.BuyBackInventory(ownedPane,pawn)&&sandbox.Credits==before-price&&sandbox.Inventory.Any(e=>e.id==ownedPane&&e.cleanStrokes==3&&e.baseValue==15),"Protected pane remains buyable with its exact original condition and debit after capacity rotation.");
            Object.DestroyImmediate(counter);Place();report.stockRotationSales=serial+2;
        }
        private static void UseWorkControl(int mechanic,int slot)
        {
            var world=(SlopCityJobWorld)Get(sandbox,"uniqueWorld");var item=world.Get(mechanic).controls[slot].GetComponent<SlopCityInteractable>();bool aimed=false;Vector3 point=item.InteractionPoint;
            foreach(float radius in new[]{.65f,1.15f,1.75f,2.4f,2.8f})
            {
                for(int direction=0;direction<24&&!aimed;direction++)
                {
                    Vector3 offset=Quaternion.Euler(0,direction*15f,0)*Vector3.forward*radius;
                    if(!Physics.Raycast(point+offset+Vector3.up*.9f,Vector3.down,out RaycastHit support,4,~0,QueryTriggerInteraction.Ignore)||support.normal.y<.8f)continue;
                    Vector3 feet=support.point+Vector3.up*.025f;bool blocked=false;
                    foreach(var obstacle in Physics.OverlapCapsule(feet+Vector3.up*.24f,feet+Vector3.up*1.5f,.215f,~0,QueryTriggerInteraction.Ignore))if(!obstacle.transform.IsChildOf(player.transform))blocked=true;
                    if(blocked)continue;Vector3 eye=feet+Vector3.up*1.62f,aim=point-eye;if(aim.magnitude>3.4f||aim.sqrMagnitude<.01f)continue;
                    var hits=Physics.RaycastAll(eye,aim.normalized,3.4f,~0,QueryTriggerInteraction.Ignore).Where(h=>!h.transform.IsChildOf(player.transform)).OrderBy(h=>h.distance).ToArray();if(hits.Length==0||hits[0].transform.GetComponentInParent<SlopCityInteractable>()!=item)continue;
                    controller.enabled=false;player.transform.SetPositionAndRotation(feet,Quaternion.identity);controller.enabled=true;camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(aim));Physics.SyncTransforms();Set(sandbox,"nextTargetAt",0f);typeof(SlopCitySandbox).GetField("webTargetFrame",Fields)?.SetValue(sandbox,-1);
                    object target=Invoke(sandbox,"FindTarget",camera);if((SlopCityInteractable)Get(target,"interaction")!=item)continue;aimed=true;
                }
                if(aimed)break;
            }
            Check(aimed&&sandbox.Interact(camera),"Ordinary aim/USE operates the physical work control "+mechanic+"/"+slot);
        }
        private static void InputLifecycle()
        {
            var go=new GameObject("Inventory_Input_Probe");go.transform.SetParent(probes.transform);var input=go.AddComponent<SlopCityPlayerInput>();input.SetGameplayEnabled(true);
            input.RequestClean();input.RequestPickup();input.RequestInventory();input.ResetContinuousTouch();
            Check(input.ConsumeClean()&&input.ConsumePickup()&&input.ConsumeInventory(),"Layout refresh preserves all three completed CLEAN/TAKE/BAG taps.");
            Check(!input.ConsumeClean()&&!input.ConsumePickup()&&!input.ConsumeInventory(),"Completed input is consumed exactly once.");
            foreach(string method in new[]{"OnApplicationPause","OnApplicationFocus","OnDisable"})
            {
                input.SetGameplayEnabled(true);input.RequestClean();input.RequestPickup();input.RequestInventory();
                if(method=="OnDisable")Invoke(input,method);else Invoke(input,method,method=="OnApplicationPause");
                Check(!input.ConsumeClean()&&!input.ConsumePickup()&&!input.ConsumeInventory(),method+" clears queued salvage actions.");
            }
            input.SetGameplayEnabled(false);input.RequestClean();input.RequestPickup();Check(!input.ConsumeClean()&&!input.ConsumePickup(),"Paused gameplay cannot enqueue CLEAN or TAKE.");input.RequestInventory();Check(input.ConsumeInventory(),"BAG can close the inventory while gameplay is paused.");
        }
        private static SlopCitySalvageItem Cube(string name,string id,bool dusty)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(probes.transform);go.transform.position=Feet+new Vector3(0,1.4f,2);go.transform.localScale=Vector3.one*.6f;
            var item=go.AddComponent<SlopCitySalvageItem>();item.Configure(id,name,"lamp",dusty,true,20);Physics.SyncTransforms();return item;
        }
        private static void Place()
        { controller.enabled=false;player.transform.SetPositionAndRotation(Feet,Quaternion.identity);controller.enabled=true;camera.transform.SetPositionAndRotation(Feet+Vector3.up*1.4f,Quaternion.identity);Physics.SyncTransforms(); }
        private static void Reload()
        {
            var previous=sandbox?sandbox:player.GetComponent<SlopCitySandbox>();
            if(previous)
            {
                foreach(var world in Object.FindObjectsByType<SlopCityJobWorld>(FindObjectsInactive.Include,FindObjectsSortMode.None))world.Release(previous);
                Object.DestroyImmediate(previous);
            }
            sandbox=player.gameObject.AddComponent<SlopCitySandbox>();sandbox.Initialize(player.transform);
            Check(Object.FindObjectsByType<SlopCityJobWorld>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==1,"Component-only reload retains exactly one owned physical-worksite host.");
        }
        private static object Get(object owner,string field)=>owner.GetType().GetField(field,Fields).GetValue(owner);
        private static void Set(object owner,string field,object value)=>owner.GetType().GetField(field,Fields).SetValue(owner,value);
        private static object Invoke(object owner,string method,params object[] args)=>owner.GetType().GetMethod(method,Fields).Invoke(owner,args);
        private static void Check(bool pass,string message){report.checks++;if(!pass)throw new InvalidOperationException(message);}
        private static void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Assert||type==LogType.Exception)report.runtimeErrors.Add(text+"\n"+stack);}
        private sealed class Backup { public bool exists; public string value; }
        [Serializable] private sealed class Receipt
        { public bool passed,savesRestored;public int checks,rollbackCases,stockRotationSales;public string startedAtUtc,finishedAtUtc,unityVersion;public string scope="Batch EditMode production physics rays and finish-property/money/save/input checks; no network, video, scene save, physical phone, or GPU pixel acceptance.";public List<string> errors=new List<string>(),runtimeErrors=new List<string>(); }
    }
}
#endif
