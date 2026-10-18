using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    public sealed partial class SlopCitySandbox
    {
        [Serializable] public sealed class UniqueJobState
        {
            public int version = 2;
            public string pendingLegacyCargo;
            public UniqueJobRecord[] records = Array.Empty<UniqueJobRecord>();
            public UniqueVehicleRecord[] vehicles = Array.Empty<UniqueVehicleRecord>();
            public UniqueMailRecord[] mail = Array.Empty<UniqueMailRecord>();
        }
        [Serializable] public sealed class UniqueJobRecord
        {
            public string id, screenId = "", vehicleId = "";
            public int run = 1, phase, issued, residentId = -1;
            public int[] values = new int[24];
            public int[] replacements = new int[16];
            public string[] sourceIds = new string[16];
            public string transportedId = "";
            public bool completed, awarded, ridden;
            public int installed;
            public Vector3 installedPosition;
            public Quaternion installedRotation=Quaternion.identity;
            public double observedTime;
            public Vector3 startPosition;
            public Quaternion arrivalRotation = Quaternion.identity;
        }
        [Serializable] public sealed class UniqueMailRecord
        { public string id; public bool parcel, letters; }
        [Serializable] public sealed class UniqueVehicleRecord
        { public string id; public float fuel = 60f; public float driven; }
        private UniqueJobState uniqueJobState = new UniqueJobState();
        private SlopCityJobWorld uniqueWorld;
        private readonly Transform[] uniqueRoutes = new Transform[40];
        private readonly SlopCityInteractable[] uniqueMailboxes = new SlopCityInteractable[3];
        private readonly SlopCityCityScreen[] uniqueScreens = new SlopCityCityScreen[40];
        private readonly Dictionary<Transform,UniqueVehicleRecord> uniqueVehicleCache = new Dictionary<Transform,UniqueVehicleRecord>();
        private float uniqueWaterTick;
        private readonly float[] uniqueScreenRetryAt = new float[40];
        private const int CleanFinish = 4;

        private void InitializeUniqueJobs()
        {
            RestoreUniqueJobState(progress.uniqueJobs);
            uniqueWorld = SlopCityJobWorld.Ensure(this);
            for (int i=0;i<40;i++){RestoreUniqueSocket(i);RenderUnique(i);}
            RestoreMailContents();
        }
        public UniqueJobState CaptureUniqueJobState()
        { return JsonUtility.FromJson<UniqueJobState>(JsonUtility.ToJson(uniqueJobState)); }
        public void RestoreUniqueJobState(UniqueJobState saved)
        {
            var next=new UniqueJobState { records=new UniqueJobRecord[40] };
            for(int i=0;i<40;i++)
            {
                string id=SlopCityJobCatalog.All[i].id;
                UniqueJobRecord record=saved?.version==2&&saved.records!=null?Array.Find(saved.records,r=>r!=null&&r.id==id):null;
                record=record==null?NewUnique(i,1):JsonUtility.FromJson<UniqueJobRecord>(JsonUtility.ToJson(record));
                record.run=Mathf.Clamp(record.run,1,1000000000);record.phase=Mathf.Clamp(record.phase,0,100);
                if(record.values==null||record.values.Length!=24)record=NewUnique(i,record.run);
                if(record.replacements==null||record.replacements.Length!=16)record.replacements=new int[16];
                if(record.sourceIds==null||record.sourceIds.Length!=16)record.sourceIds=new string[16];
                for(int q=0;q<16;q++){record.replacements[q]=Mathf.Clamp(record.replacements[q],0,3);if(record.sourceIds[q]!=null&&record.sourceIds[q].Length>=2048)record.sourceIds[q]=null;}
                bool legacyInstallation=record.installed==0&&record.completed&&(i==1||i==37)&&record.values[0]>0;
                record.installed&=7;
                if(legacyInstallation){record.installedPosition=record.startPosition;record.installedRotation=record.arrivalRotation;}
                if(record.completed){if((i==1||i==17||i==37)&&record.values[0]>0)record.installed|=1;if(i==39)record.installed|=record.values[5]&7;}
                if((i==1||i==37)&&record.installed!=0&&record.installedRotation==default){record.installedPosition=record.startPosition;record.installedRotation=record.arrivalRotation;}
                if(!Finite(record.installedPosition))record.installedPosition=Vector3.zero;
                if(!ValidRotation(record.installedRotation))record.installedRotation=Quaternion.identity;
                record.transportedId=Bounded(record.transportedId);
                for(int v=0;v<record.values.Length;v++)record.values[v]=Mathf.Clamp(record.values[v],0,10000);
                if(!Finite(record.startPosition))record.startPosition=Vector3.zero;
                if(!ValidRotation(record.arrivalRotation))record.arrivalRotation=Quaternion.identity;
                if(double.IsNaN(record.observedTime)||double.IsInfinity(record.observedTime))record.observedTime=0;
                record.vehicleId=Bounded(record.vehicleId);record.screenId=Bounded(record.screenId);
                next.records[i]=record;
            }
            var vehicles=new List<UniqueVehicleRecord>();var ids=new HashSet<string>();
            if(saved?.vehicles!=null)foreach(var source in saved.vehicles)
                if(source!=null&&vehicles.Count<32&&!string.IsNullOrEmpty(source.id)&&source.id.Length<512&&ids.Add(source.id))
                    vehicles.Add(new UniqueVehicleRecord{id=source.id,fuel=float.IsNaN(source.fuel)||float.IsInfinity(source.fuel)?60f:Mathf.Clamp(source.fuel,0,100),driven=float.IsNaN(source.driven)||float.IsInfinity(source.driven)?0:Mathf.Clamp(source.driven,0,10000000)});
            next.pendingLegacyCargo=saved?.pendingLegacyCargo=="parcel"||saved?.pendingLegacyCargo=="sample"?saved.pendingLegacyCargo:null;
            var mail=new List<UniqueMailRecord>();var mailIds=new HashSet<string>();
            if(saved?.mail!=null)foreach(var source in saved.mail)
                if(source!=null&&mail.Count<32&&!string.IsNullOrEmpty(source.id)&&source.id.Length<2048&&mailIds.Add(source.id))
                    mail.Add(new UniqueMailRecord{id=source.id,parcel=source.parcel,letters=source.letters});
            next.mail=mail.ToArray();next.vehicles=vehicles.ToArray();uniqueJobState=next;uniqueVehicleCache.Clear();
            if(uniqueWorld){for(int i=0;i<40;i++){RestoreUniqueSocket(i);RenderUnique(i);}RestoreMailContents();}
        }
        private static bool Finite(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.x)&&!float.IsInfinity(p.y)&&!float.IsInfinity(p.z)&&p.sqrMagnitude<100000000f;
        private static string Bounded(string s)=>s!=null&&s.Length<512?s:"";
        private static UniqueJobRecord NewUnique(int i,int run)
        {
            var r=new UniqueJobRecord{id=SlopCityJobCatalog.All[i].id,run=run};
            if(i==5){r.values[2]=75;r.values[3]=20;r.values[4]=75;}
            if(i==7){r.values[2]=1;r.values[3]=0;r.values[4]=1;}
            if(i==16){r.values[2]=4;r.values[3]=0;r.values[4]=0;}
            if(i==20){r.values[2]=20;r.values[3]=40;r.values[4]=60;}
            if(i==32)r.values[0]=20;
            if(i==31)r.values[2]=25;
            if(i==33){r.values[2]=90;r.values[3]=25;}
            if(i==11)r.values[2]=1;
            return r;
        }
        private UniqueJobRecord UniqueRecord(int i)=>uniqueJobState.records[i];
        private bool TryBuildUniqueAssignment(SlopCityJobDefinition definition,out List<ContractStep> selected)
        {
            selected=null;if(definition.recipeVersion!=2)return false;
            if(!uniqueWorld)uniqueWorld=SlopCityJobWorld.Ensure(this);
            var site=uniqueWorld.Get(definition.mechanic);if(site==null)return true;
            selected=new List<ContractStep>{new ContractStep{site=site.controls[0].GetComponent<SlopCityInteractable>(),action=SlopCityJobAction.UniqueWork,instruction=definition.steps[0].instruction}};
            return true;
        }
        private void CommitUniqueSelection(SlopCityJobDefinition definition)
        {
            if(definition.recipeVersion!=2)return;
            int i=definition.mechanic;var r=UniqueRecord(i);
            if(r.awarded)
            {var previous=r;uniqueJobState.records[i]=r=NewUnique(i,Mathf.Min(1000000000,r.run+1));r.installed=previous.installed;r.installedPosition=previous.installedPosition;r.installedRotation=previous.installedRotation;}
            uniqueScreens[i]=null;uniqueScreenRetryAt[i]=0;
            if(i==0||i==2)BindMailboxes(i);
            RenderUnique(i);RefreshUniqueWaypoint(i);
        }
        private bool TryRestoreUniqueAssignment()
        {
            if(progress.uniqueJobs==null||progress.uniqueJobs.version!=2)return false;
            var definition=Array.Find(SlopCityJobCatalog.All,d=>d.id==progress.activeJobId);
            if(definition==null||definition.recipeVersion!=2||progress.activeStep!=0||UniqueRecord(definition.mechanic).awarded)return false;
            if(!TryBuildUniqueAssignment(definition,out var selected)||selected==null)return false;
            steps.AddRange(selected);stepIndex=0;currentJobId=definition.id;currentJobOutcome=definition.outcome;contractTitle=definition.title;cargo=null;
            RefreshUniqueWaypoint(definition.mechanic);return true;
        }
        private void MigrateLegacyUniqueCargo()
        {
            if(progress.uniqueJobs?.version==2)return;
            string category=progress.cargo=="SEALED PARCEL"?"parcel":progress.cargo=="SEALED SAMPLE"?"sample":null;
            // The pending field is part of the same versioned archive even when the bag has no slot.
            uniqueJobState.pendingLegacyCargo=category;
            if(category!=null)
            {
                if(InventoryHasId("legacy-job-cargo/"+category)||InventoryAdd("legacy-job-cargo/"+category,"Recovered "+category,category,1,8))uniqueJobState.pendingLegacyCargo=null;
                else Say("Your old "+category+" is preserved in dispatch storage until an inventory slot is free.");
                progress.cargo=null;cargo=null;
            }
            progress.activeStep=0;progress.activeSites=Array.Empty<string>();
        }
        private void RedeemPreservedUniqueCargo()
        {
            string category=uniqueJobState.pendingLegacyCargo;if(string.IsNullOrEmpty(category))return;
            if(InventoryHasId("legacy-job-cargo/"+category)||InventoryAdd("legacy-job-cargo/"+category,"Recovered "+category,category,1,8))
            {uniqueJobState.pendingLegacyCargo=null;Save();Say("Your preserved legacy "+category+" is now in your inventory.");}
        }
        private bool TryExecuteUniqueJob(SlopCityInteractable item,Camera camera,out bool completed)
            =>TryInteractUniqueDevice(item,camera,out completed);
        private bool TryInteractUniqueDevice(SlopCityInteractable item,Camera camera,out bool completed)
        {
            completed=false;var device=item?item.GetComponent<SlopCityJobDevice>():null;
            if(!device||!device.gameObject.activeInHierarchy||device.World!=uniqueWorld||(device.External&&currentJobId!=SlopCityJobCatalog.All[device.Mechanic].id))return false;
            int i=device.Mechanic;var r=UniqueRecord(i);string message;
            bool changed=OperateUnique(i,device.Slot,camera,out message,device);
            RenderUnique(i);RefreshUniqueWaypoint(i);Say(message);
            if(changed&&r.completed&&currentJobId==r.id&&!r.awarded){r.awarded=true;completed=true;}
            return true;
        }
        private string ItemId(UniqueJobRecord r,int source)=>!string.IsNullOrEmpty(r.sourceIds[source])?r.sourceIds[source]:"job/"+r.id+"/"+r.run+"/"+source+"/"+r.replacements[source];
        private bool Issue(UniqueJobRecord r,int source,string category,int quantity,int value,out string message)
        {
            bool issued=(r.issued&(1<<source))!=0;
            if((!issued||!ExactSource(r.id,source))&&(category=="panel"||category=="window"||category=="bench"||category=="lamp"||category=="mailbox"))
            {
                var owned=inventory.Find(entry=>entry.category==category&&entry.quantity>=quantity);
                if(owned!=null){r.sourceIds[source]=owned.id;r.issued|=1<<source;message="Using your owned "+owned.label+"; its identity and finish are preserved.";return true;}
            }
            if((r.issued&(1<<source))!=0)
            {
                if(ExactSource(r.id,source)){message="Buy back the same identified item at the physical pawn counter; its identity is preserved there.";return false;}
                if(InventoryHasId(ItemId(r,source))){message="The identified supply is already in your inventory.";return false;}
                int price=Mathf.Max(4,value*3)*quantity;
                if(r.replacements[source]>=3){message="This site's three paid replacement units are exhausted. Recover a suitable part in the city or choose another assignment.";return false;}
                if(progress.credits<price){message="Replacement stock costs "+price+" credits. Sell owned salvage at the real pawn counter first.";return false;}
                r.replacements[source]++;
            if(!InventoryAdd(ItemId(r,source),"Replacement "+category,category,quantity,value)){r.replacements[source]--;message="Make room for replacement stock.";return false;}
                progress.credits-=price;message="Purchased a new identified replacement for "+price+" credits.";return true;
            }
            if(!InventoryAdd(ItemId(r,source),(category=="letter"?"Letter addressed to "+(source+1):category.Replace('_',' ')),category,quantity,value)){message="Make room in your inventory first.";return false;}
            r.issued|=1<<source;message="Packed "+quantity+" "+category+".";return true;
        }
        private bool Consume(string category,int amount,out string message)
        {if(!InventoryConsume(category,amount)){message="Carry "+amount+" "+category+" before using this socket.";return false;}message="Installed "+category+".";return true;}
        private bool ConsumeExact(UniqueJobRecord r,int source,int quantity,out string message)
        {if(!InventoryHasId(ItemId(r,source))||!InventoryRemove(ItemId(r,source),quantity)){message="This socket needs the same identified item collected for this job.";return false;}message="Identified item fitted.";return true;}
        private static bool Finish(UniqueJobRecord r,out string message,string result)
        {r.completed=true;r.phase=100;message=result;return true;}
        private bool OperateUnique(int i,int slot,Camera camera,out string message,SlopCityJobDevice actual=null)
        {
            var r=UniqueRecord(i);int p=r.phase;message="Use the labelled physical control for the current task.";
            if(r.completed&&(i==11||i==15||i==36)&&slot==0&&!MountedWorkSurface(i))return RequireMountedWorkSurface(i,out message);
            if(r.completed&&!(i==13&&slot==2))return Finish(r,out message,"Work verified in the city.");
            if((i==0||i==2)&&slot==4)
            {
                int address=PendingMailboxAddress(i,r);var box=address>=0&&address<3?uniqueMailboxes[address]:null;
                var identity=box?box.GetComponent<SlopCitySalvageItem>():null;
                if(!identity){message="The physical mailbox is not admitted yet.";return false;}
                if(box.gameObject.activeInHierarchy){message="The addressed mailbox is already in place.";return false;}
                if(!InventoryHasId(identity.StableId)){message="Buy back the same mailbox at the pawn counter, then return it here.";return false;}
                if(!TryPlaceInventoryItem(identity.StableId,box.transform.position,box.transform.rotation)){message="The original mailbox posting must be clear before replacement.";return false;}
                message="The addressed mailbox is back in its physical posting.";return true;
            }
            string restock=SourceCategory(i,slot);
            if(restock!=null&&(r.issued&(1<<slot))!=0&&!InventoryHasId(ItemId(r,slot)))
            {
                bool supplied=Issue(r,slot,restock,SourceCount(i,slot),8,out message);
                if(supplied&&(i==1||i==37)){r.ridden=false;r.transportedId="";r.phase=1;if(i==1)FindUniqueRoute(i);else FindUniqueLift(i);}
                return supplied;
            }
            switch(i)
            {
                case 0:
                    if(p==0&&slot==0&&Issue(r,0,"parcel",1,8,out message)){r.phase=1;return true;}
                    if(p==1&&slot>=1&&slot<=3&&actual&&uniqueMailboxes[slot-1]&&actual.transform==uniqueMailboxes[slot-1].transform){if(slot!=2){message="The parcel is addressed to mailbox 2.";return false;}if(!ConsumeExact(r,0,1,out message))return false;r.values[2]|=2;DepositMail(1,true);return Finish(r,out message,"Addressed parcel secured in mailbox 2.");}break;
                case 1:
                    if(p==0&&slot==0&&Issue(r,0,"window",1,18,out message)){r.phase=1;FindUniqueRoute(i);return true;}
                    if(p==2&&slot==1&&actual==uniqueWorld.Get(i).remoteControl&&r.transportedId==ItemId(r,0)&&ConsumeExact(r,0,1,out message)){r.values[0]=1;RememberInstallation(r);return Finish(r,out message,"The train-carried pane is installed in its frame.");}break;
                case 2:
                    if(p==0&&slot==0)
                    {
                        int count=0;for(int letter=0;letter<3;letter++){if(!Issue(r,letter,"letter",1,2,out message)){for(int old=0;old<count;old++){InventoryRemove(ItemId(r,old),1);r.issued&=~(1<<old);}return false;}count++;}
                        r.phase=1;return true;
                    }
                    if(p==1&&slot>=1&&slot<=3&&actual&&uniqueMailboxes[slot-1]&&actual.transform==uniqueMailboxes[slot-1].transform){int bit=1<<(slot-1);if((r.values[2]&bit)!=0){message="This address already received its letter.";return false;}if(!ConsumeExact(r,slot-1,1,out message))return false;r.values[2]|=bit;DepositMail(slot-1,false);if(r.values[2]==7)return Finish(r,out message,"All three addresses received their letters.");return true;}break;
                case 3:
                    if(p==0&&slot==0&&Issue(r,0,"bulb",1,10,out message)){r.phase=1;return true;}
                    if(p==1&&slot==1&&InventoryCount("bulb")>0&&InventoryAdd(ItemId(r,1),"Worn lamp core","lamp_core",1,4))
                    {InventoryConsume("bulb",1);r.values[2]=1;r.values[3]=100;r.phase=2;message="Replacement installed; the removed core remains yours to return.";return true;}
                    if(p==2&&slot==2&&ConsumeExact(r,1,1,out message))return Finish(r,out message,"The exact removed lamp core reached recovery.");break;
                case 4:
                    if(slot==0&&p==0&&Issue(r,0,"metal",2,3,out message)){r.phase=1;return true;}
                    if(slot==1&&p==1&&Issue(r,1,"gasket",1,4,out message)){r.phase=2;return true;}
                    if(slot==2&&p==2&&InventoryCount("metal")>=2&&InventoryCount("gasket")>=1&&InventoryAdd(ItemId(r,2),"Assembled field kit","kit",1,14))
                    {InventoryConsume("metal",2);InventoryConsume("gasket",1);r.values[2]=r.values[3]=r.values[4]=1;return Finish(r,out message,"Two metal components and a gasket form a usable field kit.");}break;
                case 5:
                    if(p==0&&slot>=0&&slot<=2){r.values[5]|=1<<slot;message="Branch "+(slot+1)+" actual pressure: "+r.values[slot+2]+"%.";if(r.values[5]==7)r.phase=1;return true;}
                    if(p==1&&slot==4){r.values[3]=75;r.values[1]=75;r.phase=2;message="The middle branch now passes full pressure. Test its usable water output.";return true;}
                    if(p==2&&slot==5){progress.water=3;return Finish(r,out message,"The restored branch supplied three actual water doses.");}break;
                case 6:
                    if(p==0&&slot==0)
                    {
                        var owned=inventory.Find(entry=>entry.category=="panel"&&entry.quantity>0);
                        if(owned!=null){r.sourceIds[0]=owned.id;r.issued|=1;r.phase=1;r.values[0]=1;message="Using the actual owned panel; its identity is retained.";return true;}
                        message="Aim at the actual removable panel and use TAKE. Its real identity and removal will be retained.";return false;
                    }
                    if(p==1&&slot==1&&InventoryHasId(ItemId(r,0))&&InventoryCount("metal")<=62&&InventoryCount("wood")<=62)
                    {
                        if(!InventoryAdd(ItemId(r,1),"Recovered panel metal","metal",2,3))return false;
                        if(!InventoryAdd(ItemId(r,2),"Recovered panel timber","wood",1,3)){InventoryRemove(ItemId(r,1),2);return false;}
                        InventoryRemove(ItemId(r,0),1);return Finish(r,out message,"The removed panel became two metal units and one timber unit.");
                    }break;
                case 7:
                    if(p==0&&slot>=1&&slot<=3){r.values[5]|=1<<(slot-1);message="Fuse "+slot+(r.values[slot+1]>0?" conducts power.":" is open circuit.");if(r.values[5]==7)r.phase=1;return true;}
                    if(p==1&&slot==0&&Issue(r,0,"fuse",1,5,out message)){r.phase=2;return true;}
                    if(p==2&&slot==2&&Consume(r,"fuse",1,out message)){r.values[3]=1;return Finish(r,out message,"Only the failed centre fuse was replaced; the circuit conducts.");}break;
                case 8:
                    if(p==0&&slot==1){r.values[0]=0;r.phase=1;message="Valve isolated. No water reaches the gasket.";return true;}
                    if(p==1&&slot==0&&Issue(r,0,"gasket",1,4,out message)){r.phase=2;return true;}
                    if(p==2&&slot==2&&Consume("gasket",1,out message)){r.phase=3;return true;}
                    if(p==3&&slot==3){r.values[0]=100;r.values[1]=100;progress.water=3;return Finish(r,out message,"The replaced gasket holds and water flow is restored.");}break;
                case 9:
                    if(p==0&&slot==1){r.values[2]=0;r.phase=1;message="Power isolated.";return true;}
                    if(p==1&&slot==2){r.values[3]=0;r.phase=2;message="Failed bulb removed from its socket.";return true;}
                    if(p==2&&slot==0&&Issue(r,0,"bulb",1,6,out message)){r.phase=3;return true;}
                    if(p==3&&slot==3&&Consume("bulb",1,out message)){r.values[3]=100;r.phase=4;return true;}
                    if(p==4&&slot==1){r.values[2]=1;return Finish(r,out message,"A new bulb illuminates the street with power restored.");}break;
                case 10:
                    if(p==0&&slot==0&&Issue(r,0,"fuse",1,5,out message)){r.phase=1;return true;}
                    if(p==1&&slot==1&&Consume("fuse",1,out message)){r.values[2]=1;r.values[6]=1;r.phase=2;return true;}
                    if(p==2&&slot==2&&r.values[6]>0&&InventoryAdd(ItemId(r,2),"Load-tested supply","kit",1,12)){r.values[6]--;return Finish(r,out message,"The repaired terminal dispensed a real usable kit.");}break;
                case 11:
                    if(!RequireMountedWorkSurface(i,out message))return false;
                    r.values[3]=Mathf.Min(100,SalvageCleanStrokes(uniqueWorld.Get(i).dustyId)*25);r.values[2]=1;
                    if(r.values[3]>=100)return Finish(r,out message,"The dusty lens is polished and actual lamp output is restored.");message="Use CLEAN on the dusty lamp lens: "+r.values[3]+"% clear.";break;
                case 12:
                    if(SalvageCleanStrokes(uniqueWorld.Get(i).dustyId)<CleanFinish){message="The bench is dusty. Use CLEAN on its slats first.";break;}
                    if(slot==1){var bench=uniqueWorld.Get(i).bench;if(!bench||!bench.gameObject.activeInHierarchy){message="Place the same cleaned bench back on its clear pad before resting.";if(bench&&InventoryHasId(uniqueWorld.Get(i).dustyId)&&TryPlaceInventoryItem(uniqueWorld.Get(i).dustyId,uniqueWorld.Get(i).root.position+new Vector3(0,.6f,.25f),uniqueWorld.Get(i).root.rotation)){message="The cleaned bench is back in place. Sit to recover.";return true;}return false;}boostUntil=Mathf.Max(boostUntil,Time.time+60f);return Finish(r,out message,"Resting on the polished bench grants real movement recovery.");}break;
                case 13:
                    if(slot==2)
                    {
                        if(r.values[7]>=4){message="This pump's four fuel portions are exhausted until its next service round.";return false;}
                        if(progress.credits<5){message="A portable 25-unit fuel can costs five credits. Earn them by selling actual salvage.";return false;}
                        if(!InventoryAdd(ItemId(r,3),"Portable 25-unit fuel can","fuel",1,4)){message="Make one bag slot for the portable fuel can before paying.";return false;}
                        r.issued|=1<<3;r.values[7]++;progress.credits-=5;message="Paid five credits for one portable 25-unit can. Carry it to an empty car and USE.";return true;
                    }
                    if(slot==0){if(r.values[7]>=4){message="This pump reservoir is empty until the next service round.";return false;}Transform car=GetJobVehicle();if(!car||Vector3.Distance(car.position,uniqueWorld.Get(i).root.position)>8){message="Bring a car alongside this pump.";return false;}var v=FuelRecord(car);if(v==null)return false;if(v.fuel>=100){message="This tank is full.";return false;}if(r.values[7]>0&&progress.credits<5){message="Further measured fuel costs five credits.";return false;}if(r.values[7]>0)progress.credits-=5;r.values[7]++;v.fuel=Mathf.Min(100,v.fuel+25);r.vehicleId=v.id;r.startPosition=car.position;r.values[6]=0;r.values[1]=25;r.phase=1;message="25 fuel units dispensed into the car. Drive at least five metres.";return true;}
                    if(p==1&&slot==1&&r.values[6]>=500)return Finish(r,out message,"The refuelled car completed a real driven leg.");break;
                case 14:
                    if(p==0&&slot==0){if(!Issue(r,0,"metal",1,2,out message))return false;if(!Issue(r,1,"glass",1,2,out message)){InventoryRemove(ItemId(r,0),1);r.issued&=~1;return false;}if(!Issue(r,2,"plastic",1,2,out message)){InventoryRemove(ItemId(r,0),1);InventoryRemove(ItemId(r,1),1);r.issued&=~3;return false;}r.phase=1;return true;}
                    if(p==1&&slot>=1&&slot<=3){string category=new[]{"metal","glass","plastic"}[slot-1];if(r.values[slot+1]>0){message="That intake already holds its sorted load.";return false;}if(!Consume(category,1,out message))return false;r.values[slot+1]=1;if(r.values[2]+r.values[3]+r.values[4]==3)return Finish(r,out message,"Three distinct materials occupy their correct recovery bays.");return true;}break;
                case 15:
                    if(!MountedWorkSurface(i)&&InventoryHasId(uniqueWorld.Get(i).dustyId)&&!RequireMountedWorkSurface(i,out message))return false;
                    if(CleanWorkAvailable(i))return Finish(r,out message,"An actual window retains its polished finish and is usable for work.");message="Use CLEAN on the window. Every effective press increases its finish.";break;
                case 16:
                    if(slot==1&&r.values[2]>0&&r.values[3]<2){r.values[2]--;r.values[3]++;message="One physical bundle moved to the left bin.";}
                    else if(slot==2&&r.values[2]>0&&r.values[4]<2){r.values[2]--;r.values[4]++;message="One physical bundle moved to the right bin.";}
                    else {message="Each bin takes two bundles; the conserved source holds "+r.values[2]+".";return false;}
                    if(r.values[2]==0&&r.values[3]==2&&r.values[4]==2)return Finish(r,out message,"Four real bundles fit two and two, with no material lost.");return true;
                case 17:
                    if(p==0&&slot==0&&Issue(r,0,"poster",1,2,out message)){r.phase=1;return true;}
                    if(p==1&&slot==1&&ConsumeExact(r,0,1,out message)){r.values[0]=1;r.installed|=1;return Finish(r,out message,"The new poster is mounted in its protected frame.");}break;
                case 18:
                    if(p==0&&slot==0&&Issue(r,0,"metal",3,3,out message)){r.phase=1;return true;}
                    if(p==1&&slot==1){Transform car=GetJobVehicle();if(!car||Vector3.Distance(car.position,uniqueWorld.Get(i).root.position)>8||!InventoryHasId(ItemId(r,0))){message="Bring the identified salvage and a car to the loading bay.";return false;}var fuel=FuelRecord(car);if(fuel==null){message="This car cannot be admitted to the bounded fuel registry. Choose another supported vehicle.";return false;}r.vehicleId=fuel.id;r.startPosition=car.position;r.phase=2;message="Three owned units are secured as car cargo. Drive twenty metres.";return true;}
                    if(p==2&&slot==2&&r.values[6]>=2000&&ConsumeExact(r,0,3,out message)){r.values[2]=3;return Finish(r,out message,"The same salvage travelled in the car and reached recovery.");}break;
                case 19:
                    if(slot>=0&&slot<3&&(r.values[5]&(1<<slot))==0&&Issue(r,slot,"plastic",1,1,out message)){r.values[5]|=1<<slot;r.values[slot+2]=100;if(r.values[5]==7)return Finish(r,out message,"All physical litter was picked out; the planting bed is accessible.");return true;}break;
                case 20:
                    if(slot==0){progress.water=3;message="Water carrier holds three measured doses.";return true;}
                    if(slot>=1&&slot<=3){int index=slot+1;if(r.values[index]>=80){message="This plant has enough water. Choose a dry plant.";return false;}if(progress.water<=0){message="Refill the carrier first.";return false;}progress.water--;r.values[index]=Mathf.Min(100,r.values[index]+20);message="One dose reaches these roots: "+r.values[index]+"% moisture.";if(r.values[2]>=80&&r.values[3]>=80&&r.values[4]>=80)return Finish(r,out message,"The three different deficits were filled with measured water.");return true;}break;
                case 21:
                    if(p==0&&slot==0&&Issue(r,0,"hose",1,6,out message)){r.phase=1;return true;}
                    if(p==1&&slot==1&&InventoryHasId(ItemId(r,0))){r.values[5]|=1;r.phase=2;message="The hose inlet is connected to the hydrant.";return true;}
                    if(p==2&&slot==2&&ConsumeExact(r,0,1,out message)){r.values[5]|=2;r.phase=3;return true;}
                    if(p==3&&slot==3){r.values[0]=100;r.phase=4;message="Valve open: actual garden moisture rises while flow is connected.";return true;}
                    if(p==4&&slot==4&&r.values[2]>=80)return Finish(r,out message,"The connected hose watered the bed.");break;
                case 22:
                    if(p==0&&slot==0&&Issue(r,0,"filter",1,5,out message)){r.phase=1;return true;}
                    if(p==1&&slot==1&&Consume("filter",1,out message)){r.phase=2;return true;}
                    if(p==2&&slot==2){r.values[1]=Mathf.Min(100,r.values[1]+25);message="Flushed water quality: "+r.values[1]+"%.";if(r.values[1]>=100){progress.water=3;return Finish(r,out message,"The filter runs clear and provides three usable water doses.");}return true;}break;
                case 23:
                    if(slot==1){var bench=uniqueWorld.Get(i).bench;Vector3 point=uniqueWorld.Get(i).root.position+new Vector3(.6f,.6f,.25f);if(!bench){message="Recover and place the same bench back at its seating pad.";return false;}
                        if(InventoryHasId(uniqueWorld.Get(i).dustyId)){if(!TryPlaceInventoryItem(uniqueWorld.Get(i).dustyId,point,siteRotation(i))){message="The placement pad must be clear before returning the same bench.";return false;}r.values[0]=1;return Finish(r,out message,"The same carried bench is placed in its clear seating pad.");}
                        if(Vector3.Distance(bench.position,point)<.3f)return Finish(r,out message,"The same bench occupies its clear seating pad.");
                        var identity=bench.GetComponent<SlopCitySalvageItem>();if(!identity){message="The bench identity is unavailable.";return false;}
                        if(!InventoryAdd(identity.StableId,identity.DisplayName,identity.Category,1,identity.BaseValue,SalvageCleanStrokes(identity.StableId))){message="Make one slot to move the bench safely.";return false;}
                        if(!TryPlaceInventoryItem(identity.StableId,point,siteRotation(i))){InventoryRemove(identity.StableId,1);message="The seating pad is obstructed; the bench remains where it was.";return false;}
                        r.values[0]=1;return Finish(r,out message,"The same bench is saved at its unobstructed seating pad.");}break;
                case 24:
                    if(p==0&&slot==0&&Issue(r,0,"sapling",1,5,out message)){r.phase=1;return true;}
                    if(p==1&&slot==1&&ConsumeExact(r,0,1,out message)){r.values[2]=35;r.phase=2;return true;}
                    if(p==2&&slot==2){if(progress.water<1){message="Refill a water carrier before establishing the sapling.";return false;}progress.water--;r.values[2]=100;return Finish(r,out message,"The same sapling is planted and watered into its bed.");}break;
                case 25:
                    if(p==0&&slot==0){if(!NearJobScreen(i,out message)||!TryTuneJobScreen(ScreenTarget(i),NextJobChannel(i),out message))return false;r.screenId="";r.observedTime=0;r.phase=1;return true;}
                    if(p==1&&slot==1&&ObserveMotion(i,r))return Finish(r,out message,"The same ordinary display produced advancing decoded motion.");break;
                case 26:
                    if(slot==0){if(!NearJobScreen(i,out message)||!TryTuneJobScreen(ScreenTarget(i),2,out message))return false;r.phase=1;return true;}
                    if(p==1&&slot==1&&TryObserveJobScreen(ScreenTarget(i),out _,out int channel,out _,out _)&&channel==2)return Finish(r,out message,"The requested real channel is selected on the ordinary display.");break;
                case 27:
                    if(slot>=1&&slot<=3){int expected=new[]{2,1,3}[Mathf.Clamp(r.phase,0,2)];if(slot!=expected){r.phase=0;r.values[2]=0;message="Circuit reset: use lamps 2, 1, 3.";return true;}r.values[2]|=1<<(slot-1);r.values[3]=100;r.phase++;if(r.phase==3)return Finish(r,out message,"All three physical lamps are lit in circuit order.");message="Circuit contact accepted.";return true;}break;
                case 28:
                    if(slot==0||slot==1){r.values[0]=(r.values[0]+(slot==0?1:23))%24;message="Antenna direction "+(r.values[0]*15)+" degrees.";return true;}
                    if(slot==2&&ReceiverAligned(i,r)){r.values[2]=100;return Finish(r,out message,"The receiver faces its physical source through a clear line.");}break;
                case 29:
                    if(p==0&&slot==0){r.values[2]=1;r.phase=1;FindUniqueRoute(i);message="Source powered. Reach the remote relay by actual transit.";return true;}
                    if(p==2&&slot==1){if(!NearJobScreen(i,out message)||!TryTuneJobScreen(ScreenTarget(i),NextJobChannel(i),out message))return false;r.values[3]=1;return Finish(r,out message,"Source and remote relay are active; the ordinary screen changed feed.");}break;
                case 30:
                    if(slot==1){r.values[1]=Mathf.Min(100,r.values[1]+10);message="The graduated container holds "+r.values[1]+" measured units.";return true;}
                    if(slot==2){r.values[0]=Mathf.Min(100,r.values[0]+5);message="Meter correction: "+r.values[0]+".";return true;}
                    if(slot==3){if(r.values[1]!=50||r.values[0]!=10){message="Fill to 50 measured units and correct the meter by 10.";return false;}return Finish(r,out message,"Actual container volume and corrected meter agree.");}break;
                case 31:
                    if(p==0&&slot==0&&Issue(r,0,"sample",1,3,out message)){r.phase=1;return true;}
                    if(p==1&&slot==1){r.values[2]=25;r.phase=2;message="The physical test strip indicates mineral contamination (amber).";return true;}
                    if(p==2&&slot==3&&ConsumeExact(r,0,1,out message)){if(!InventoryAdd(ItemId(r,1),"Tested water treatment","treatment",1,8)){InventoryAdd(ItemId(r,0),"Sealed sample","sample",1,3);return false;}r.values[2]=100;return Finish(r,out message,"Matching reagent treated the identified sample; usable treatment retained.");}break;
                case 32:
                    if(slot==0||slot==1){r.values[0]=Mathf.Clamp(r.values[0]+(slot==0?10:-10),0,100);r.values[1]=Mathf.RoundToInt(r.values[0]*.8f);message="Actual output pressure: "+r.values[1]+"%.";return true;}
                    if(slot==2&&r.values[1]>=55&&r.values[1]<=65){progress.water=3;return Finish(r,out message,"The valve supplies useful water inside the marked pressure band.");}break;
                case 33:
                    if(p==0&&slot==0&&Issue(r,0,"sample",1,3,out message)){r.values[2]=90;r.phase=1;return true;}
                    if(p==1&&slot==1&&Issue(r,1,"sample",1,3,out message)){r.values[3]=25;r.phase=2;return true;}
                    if(p==2&&slot==2){message="Compared identified strips: source A 90%, source B 25%.";r.phase=3;return true;}
                    if(p==3&&slot==4&&InventoryHasId(ItemId(r,0))&&InventoryHasId(ItemId(r,1))){InventoryRemove(ItemId(r,0),1);InventoryRemove(ItemId(r,1),1);r.values[3]=90;return Finish(r,out message,"The measured poor source B received treatment; both outputs are usable.");}break;
                case 34:
                    if(p==0&&slot==0&&Issue(r,0,"lost_item",1,0,out message)){r.phase=1;r.residentId=SlopCityCrowd.Instance?SlopCityCrowd.Instance.GetResidentId(0):-1;var item=uniqueWorld.Get(i).movingPart;if(item)item.gameObject.SetActive(false);return true;}
                    if(p==1&&slot==1){message="Return the same object to its actual owner, resident "+r.residentId+". Aim at the visible owner and USE.";return false;}break;
                case 35:
                    if(slot==0){r.values[2]=(r.values[2]+1)%7;message="Pump allocation "+r.values[2]+" / shared capacity 6.";return true;}
                    if(slot==1){r.values[3]=(r.values[3]+1)%7;message="Lighting allocation "+r.values[3]+" / shared capacity 6.";return true;}
                    if(slot==2&&r.values[2]>=3&&r.values[3]>=2&&r.values[2]+r.values[3]<=6){r.values[1]=r.values[2]*20;progress.water=3;return Finish(r,out message,"Pump and lighting both operate within the actual shared capacity.");}break;
                case 36:
                    if(!RequireMountedWorkSurface(i,out message))return false;
                    if(SalvageCleanStrokes(uniqueWorld.Get(i).dustyId)<CleanFinish){message="Use CLEAN on the dusty notice cover; preserve the poster beneath.";return false;}
                    if(slot==1){r.values[0]=100;r.values[1]=100;return Finish(r,out message,"The revealed notice requested the amber valve; that physical control is now open.");}break;
                case 37:
                    if(p==0&&slot==0&&Issue(r,0,"panel",1,12,out message)){r.phase=1;FindUniqueLift(i);return true;}
                    if(p==2&&slot==1&&actual==uniqueWorld.Get(i).remoteControl&&r.transportedId==ItemId(r,0)&&ConsumeExact(r,0,1,out message)){r.values[0]=1;RememberInstallation(r);return Finish(r,out message,"The identified replacement panel was carried by lift and installed upstairs.");}break;
                case 38:
                    if(slot==0&&p==0&&Issue(r,0,"wood",2,2,out message)){r.phase=1;return true;}
                    if(p==1&&slot==1&&camera){message="Face a visible neighbour within ordinary reach and USE to offer two timber units.";return false;}
                    break;
                case 39:
                    if(p==0&&slot==0&&Issue(r,0,"wood",3,2,out message)){r.phase=1;return true;}
                    if(p==1&&slot>=1&&slot<=3){int bit=1<<(slot-1);if((r.values[5]&bit)!=0){message="That guard slat is already fitted.";return false;}if(!Consume("wood",1,out message))return false;r.values[5]|=bit;r.installed|=bit;if(r.values[5]==7)r.phase=2;return true;}
                    if(p==2&&slot==4){if(progress.water<1){message="Bring one water dose to the protected planting.";return false;}progress.water--;r.values[2]=100;return Finish(r,out message,"Three owned timber slats form the guard around a watered planting.");}break;
            }
            return false;
        }
        private static void RememberInstallation(UniqueJobRecord record)
        {record.installed|=1;record.installedPosition=record.startPosition;record.installedRotation=record.arrivalRotation;}
        private bool MountedWorkSurface(int i)
        {
            var site=uniqueWorld?uniqueWorld.Get(i):null;
            return site!=null&&site.dustyTarget&&site.dustyTarget.gameObject.activeInHierarchy
                &&Vector3.Distance(site.dustyTarget.position,site.root.TransformPoint(site.dustyLocalPosition))<.1f;
        }
        private bool CleanWorkAvailable(int i)
            =>(MountedWorkSurface(i)&&SalvageCleanStrokes(uniqueWorld.Get(i).dustyId)>=CleanFinish)
                ||(i==15&&inventory.Exists(entry=>entry.category=="window"&&entry.quantity>0&&entry.cleanStrokes>=CleanFinish));
        private bool RequireMountedWorkSurface(int i,out string message)
        {
            message="";if(MountedWorkSurface(i))return true;
            var site=uniqueWorld.Get(i);string id=site.dustyId;
            if(!InventoryHasId(id)){message="Recover this exact surface from your bag or buy it back at the pawn counter before reinstalling it.";return false;}
            if(!TryPlaceInventoryItem(id,site.root.TransformPoint(site.dustyLocalPosition),site.root.rotation*site.dustyLocalRotation))
            {message="Clear the original mounting position before returning the same surface.";return false;}
            message="The exact owned surface is mounted again with its retained finish.";return true;
        }
        private bool Consume(UniqueJobRecord r,string category,int amount,out string message)=>Consume(category,amount,out message);
        internal void HandleUniqueWorldItemEvent(string action,string id,string category,int amount)
        {
            if(uniqueJobState?.records==null||!uniqueWorld)return;
            for(int i=0;i<40;i++)
            {
                var site=uniqueWorld.Get(i);if(site==null||site.dustyId!=id)continue;
                var r=UniqueRecord(i);
                if(i==6&&action=="pickup")
                {r.sourceIds[0]=id;r.issued|=1;r.phase=1;r.values[0]=1;}
                if(action=="clean"||action=="CLEAN"||action=="Clean")
                {
                    int strokes=SalvageCleanStrokes(id);
                    if(i==11){r.values[2]=1;r.values[3]=Mathf.Min(100,strokes*25);if(strokes>=CleanFinish){r.completed=true;r.phase=100;}}
                    if(i==15&&strokes>=CleanFinish){r.completed=true;r.phase=100;}
                }
                if(i==23&&(action=="place"||action=="PLACE"||action=="Place"))
                {var bench=site.bench;if(bench&&Vector3.Distance(bench.position,site.root.position+new Vector3(.6f,.6f,.25f))<.5f){r.values[0]=1;r.completed=true;r.phase=100;}}
                RenderUnique(i);
            }
        }
        private void TickUniqueJobs()
        {
            if(!initialized||!uniqueWorld||!user)return;
            RedeemPreservedUniqueCargo();
            var player=user.GetComponent<SlopCityFirstPerson>();if(player&&player.IsPaused)return;
            int i=UniqueMechanicIndex(currentJobId);if(i<0)return;
            var r=UniqueRecord(i);
            AdvanceUniqueDevices(Time.deltaTime);
            if(i==25&&r.phase==1&&ObserveMotion(i,r)){r.completed=true;r.phase=100;}
            // Root calls this after successful item-event transaction. The event itself never saves.
            if((i==11||i==15)&&r.completed&&!r.awarded&&!CleanWorkAvailable(i)){r.completed=false;r.phase=0;RenderUnique(i);RefreshUniqueWaypoint(i);}
            if(r.completed&&!r.awarded&&HasWaypoint&&steps[stepIndex].action==SlopCityJobAction.UniqueWork)
            {r.awarded=true;AdvanceContract();}
        }
        private void AdvanceUniqueDevices(float dt)
        {
            if(!initialized||!uniqueWorld||float.IsNaN(dt)||float.IsInfinity(dt)||dt<=0)return;
            var player=user?user.GetComponent<SlopCityFirstPerson>():null;if(player&&player.IsPaused)return;
            int i=UniqueMechanicIndex(currentJobId);if(i!=21)return;var r=UniqueRecord(i);
            if(r.phase!=4||r.values[0]<=0||r.values[5]!=3)return;
            uniqueWaterTick+=Mathf.Min(.25f,dt);
            if(uniqueWaterTick>=1f){uniqueWaterTick-=1f;r.values[2]=Mathf.Min(100,r.values[2]+10);RenderUnique(i);}
        }
        private Transform ScreenTarget(int i)
        {
            if(uniqueScreens[i]&&uniqueScreens[i].isActiveAndEnabled&&!uniqueScreens[i].IsFeatured&&!uniqueScreens[i].AudioOnly)return uniqueScreens[i].transform;
            if(Time.unscaledTime<uniqueScreenRetryAt[i])return null;uniqueScreenRetryAt[i]=Time.unscaledTime+.5f;
            var site=uniqueWorld.Get(i);if(site==null)return null;
            SlopCityCityScreen best=null;float distance=float.PositiveInfinity;
            foreach(var screen in FindObjectsByType<SlopCityCityScreen>(FindObjectsSortMode.None))
            {
                if(!screen||!screen.isActiveAndEnabled||screen.IsFeatured||screen.AudioOnly)continue;
                Vector3 origin=UniqueRecord(i).ridden?UniqueRecord(i).startPosition:site.root.position;
                float d=(screen.transform.position-origin).sqrMagnitude;
                if(d<distance){distance=d;best=screen;}
            }
            uniqueScreens[i]=best;return best?best.transform:null;
        }
        private SlopCityInteractable ScreenControl(int i)
        {
            var target=ScreenTarget(i);if(!target)return null;
            SlopCityInteractable best=null;float distance=144f;
            foreach(var site in sites)if(site&&site.Kind==SlopCityInteractionKind.Screen&&!site.Destination)
            {
                var broadcast=site.GetComponentInParent<SlopCityCityScreen>()??site.GetComponentInChildren<SlopCityCityScreen>();
                if(broadcast&&broadcast.AudioOnly)continue;
                float d=(site.transform.position-target.position).sqrMagnitude;if(d<distance){distance=d;best=site;}
            }
            return best;
        }
        private bool NearJobScreen(int i,out string message)
        {var target=ScreenTarget(i);message="Approach the actual ordinary screen and its tuning control.";return target&&user&&Vector3.Distance(user.position,target.position)<20f;}
        private int NextJobChannel(int i)
        {return TryObserveJobScreen(ScreenTarget(i),out _,out int channel,out _,out _)?(channel+1)%3:0;}
        private bool ObserveMotion(int i,UniqueJobRecord r)
        {
            Transform screen=ScreenTarget(i);
            if(!screen||!user||Vector3.Distance(user.position,screen.position)>20f)return false;
            if(!TryObserveJobScreen(screen,out string id,out _,out double time,out bool playing)||!playing)return false;
            if(string.IsNullOrEmpty(r.screenId)){r.screenId=id;r.observedTime=time;return false;}
            if(id!=r.screenId){r.screenId=id;r.observedTime=time;return false;}
            return Math.Abs(time-r.observedTime)>.35;
        }
        private void FindUniqueRoute(int i)
        {
            Transform best=null;float distance=float.PositiveInfinity;
            foreach(var platform in FindObjectsByType<SlopCityMetroPlatform>(FindObjectsSortMode.None))
            {
                float d=user?(platform.transform.position-user.position).sqrMagnitude:0;
                if(d<distance){distance=d;best=platform.transform;}
            }
            uniqueRoutes[i]=best;
        }
        private void FindUniqueLift(int i)
        {
            SlopCityInteractable best=null;float distance=float.PositiveInfinity;
            foreach(var item in sites)
            {
                if(!item||item.Kind!=SlopCityInteractionKind.Building||!item.Destination||item.Destination.position.y<=item.InteractionPoint.y+2f)continue;
                float d=user?(item.InteractionPoint-user.position).sqrMagnitude:0;if(d<distance){distance=d;best=item;}
            }
            uniqueRoutes[i]=best?best.transform:null;
        }
        internal void NotifyUniqueRiddenArrival(SlopCityInteractable route,Transform destination,SlopCityMetroTrain train)
        {
            if(!route||!destination||!train||!train.PlayerInside||train.IsMoving||train.RiderTransform!=user)return;
            var platform=route.GetComponent<SlopCityMetroPlatform>();
            if(!platform||platform.World!=train.World||route.Destination!=destination)return;
            for(int i=0;i<40;i++)
            {
                var r=UniqueRecord(i);if((i!=1&&i!=29)||r.phase!=1||uniqueRoutes[i]!=route.transform)continue;
                r.ridden=true;r.phase=2;r.startPosition=destination.position;r.arrivalRotation=destination.rotation;r.transportedId=ItemId(r,0);
                uniqueScreens[i]=null;uniqueScreenRetryAt[i]=0;
                if(i==1)uniqueWorld.CreateArrivalSocket(i,destination,false);
                RenderUnique(i);RefreshUniqueWaypoint(i);
            }
        }
        internal void NotifyUniqueElevatorArrival(SlopCityInteractable route,SlopCityElevator elevator)
        {
            if(!route||!elevator||!user||!route.Destination||Vector3.Distance(user.position,route.Destination.position)>14f)return;
            var r=UniqueRecord(37);
            if(r.phase==1&&uniqueRoutes[37]==route.transform&&InventoryHasId(ItemId(r,0)))
            {r.ridden=true;r.phase=2;r.startPosition=route.Destination.position;r.arrivalRotation=route.Destination.rotation;r.transportedId=ItemId(r,0);uniqueWorld.CreateArrivalSocket(37,route.Destination,true);RenderUnique(37);RefreshUniqueWaypoint(37);}
        }
        private bool ReceiverAligned(int i,UniqueJobRecord r)
        {
            var site=uniqueWorld.Get(i);if(site?.movingPart==null||!site.treatment)return false;
            Vector3 direction=site.treatment.position-site.movingPart.position;direction.y=0;
            Vector3 forward=Quaternion.Euler(0,r.values[0]*15f,0)*Vector3.forward;
            if(Vector3.Angle(forward,direction)>10f)return false;
            return !Physics.Linecast(site.movingPart.position+Vector3.up*.7f,site.treatment.position,out var hit,~0,QueryTriggerInteraction.Ignore)
                ||hit.transform==site.treatment||hit.transform.IsChildOf(site.root);
        }
        private void RefreshUniqueWaypoint(int i)
        {
            if(currentJobId!=SlopCityJobCatalog.All[i].id||steps.Count==0||stepIndex>=steps.Count)return;
            var r=UniqueRecord(i);var site=uniqueWorld.Get(i);if(site==null)return;
            SlopCityInteractable target=site.controls[Mathf.Clamp(ExpectedControl(i,r),0,SlopCityJobWorld.ControlCount-1)].GetComponent<SlopCityInteractable>();
            if((i==1||i==29||i==37)&&r.phase==1)
            {
                if(!uniqueRoutes[i]){if(i==37)FindUniqueLift(i);else FindUniqueRoute(i);}
                if(uniqueRoutes[i])target=uniqueRoutes[i].GetComponent<SlopCityInteractable>();
            }
            if((i==0||i==2)&&r.phase==1)
            {
                BindMailboxes(i);int address=PendingMailboxAddress(i,r);
                if(address>=0&&address<3&&uniqueMailboxes[address]&&uniqueMailboxes[address].gameObject.activeInHierarchy)target=uniqueMailboxes[address];
            }
            if((i==25||i==26||i==29)&&(i!=29||r.phase==2))
            {
                var external=ScreenControl(i);if(external)
                {
                    var device=external.GetComponent<SlopCityJobDevice>();if(!device)device=external.gameObject.AddComponent<SlopCityJobDevice>();
                    device.BindExternal(uniqueWorld,i,r.phase==0?0:1);target=external;
                }
            }
            if((i==1||i==37)&&r.phase==2&&site.remoteControl)target=site.remoteControl.GetComponent<SlopCityInteractable>();
            steps[stepIndex].site=target;steps[stepIndex].instruction=Instruction(i,r);
        }
        private int ExpectedControl(int i,UniqueJobRecord r)
        {
            if(r.completed)return 0;
            if(i==0||i==2)
            {if(r.phase==0)return 0;int address=PendingMailboxAddress(i,r);return address<0||!uniqueMailboxes[address]||!uniqueMailboxes[address].gameObject.activeInHierarchy?4:address+1;}
            if(i==11||i==15)return 0;
            if(i==36)return MountedWorkSurface(i)&&SalvageCleanStrokes(uniqueWorld.Get(i).dustyId)>=CleanFinish?1:0;
            if(i==12)return SalvageCleanStrokes(uniqueWorld.Get(i).dustyId)>=CleanFinish?1:0;
            if((i==1||i==29||i==37)&&r.phase==2)return 1;
            if(i==9&&r.phase==4)return 1;
            if(i==5)return r.phase==0?0:r.phase==1?4:5;
            if(i==16)return r.values[3]<2?1:2;
            if(i==19)return (r.values[5]&1)==0?0:(r.values[5]&2)==0?1:2;
            if(i==20)return progress.water==0?0:r.values[2]<80?1:r.values[3]<80?2:3;
            if(i==7)return r.phase==0?1:r.phase==1?0:2;
            if(i==27)return new[]{2,1,3}[Mathf.Clamp(r.phase,0,2)];
            return Mathf.Clamp(r.phase,0,7);
        }
        private string Instruction(int i,UniqueJobRecord r)
        {
            if(r.completed)return "The physical work is complete; verify the result";
            if((i==1||i==29)&&r.phase==1)return "Walk aboard the assigned train and ride its next real stop";
            if(i==37&&r.phase==1)return "Call and ride the assigned lift with the replacement panel";
            if((i==11||i==15||i==36)&&!MountedWorkSurface(i)&&!(i==15&&CleanWorkAvailable(i)))return "USE the work control to return the exact owned surface; buy it back at the pawn counter if sold";
            if((i==11||i==15)&&CleanWorkAvailable(i))return "USE the work control to verify the existing polished "+(i==11?"working lens":"window");
            if(i==11||i==15)return "Use CLEAN on the dusty "+(i==11?"lamp lens":"window")+" (four effective strokes)";
            if(i==12&&SalvageCleanStrokes(uniqueWorld.Get(i).dustyId)<CleanFinish)return "Use CLEAN on the dusty bench, then sit";
            if(i==36&&SalvageCleanStrokes(uniqueWorld.Get(i).dustyId)<CleanFinish)return "Use CLEAN on the notice cover; preserve the paper below";
            if(i==25&&r.phase==1)return "Approach the tuned ordinary display and observe its moving picture";
            int control=Mathf.Clamp(ExpectedControl(i,r),0,Labels[i].Length-1);
            return Labels[i][control]+" / phase "+(r.phase+1);
        }
        private static int UniqueMechanicIndex(string id)
        {for(int i=0;i<SlopCityJobCatalog.All.Length;i++)if(SlopCityJobCatalog.All[i].id==id)return i;return -1;}
        private void RestoreUniqueSocket(int i)
        {
            var r=UniqueRecord(i);
            if(!uniqueWorld||(i!=1&&i!=37))return;
            if(r.phase==2&&r.ridden&&!string.IsNullOrEmpty(r.transportedId))uniqueWorld.CreateArrivalSocket(i,r.startPosition,r.arrivalRotation,i==37);
            else if((r.installed&1)!=0)uniqueWorld.CreateArrivalSocket(i,r.installedPosition,r.installedRotation,i==37);
        }
        private void RenderUnique(int i)
        {
            if(!uniqueWorld||uniqueWorld.Get(i)==null||uniqueJobState.records==null||i>=uniqueJobState.records.Length)return;
            var r=UniqueRecord(i);uniqueWorld.Render(i,r,Instruction(i,r),Labels[i]);
        }
        private static readonly string[][] Labels =
        {
            L("Collect addressed parcel|Mailbox 1|Mailbox 2|Mailbox 3|Restore taken mailbox"),
            L("Collect replacement pane|Install the train-carried pane"),
            L("Collect three addressed letters|Address 1|Address 2|Address 3|Restore taken mailbox"),
            L("Collect replacement lamp core|Exchange the installed core|Return removed core"),
            L("Collect two metal parts|Collect gasket|Assemble field kit"),
            L("Measure branch 1|Measure branch 2|Measure branch 3||Clear blocked middle branch|Fill from restored line"),
            L("Remove wall panel|Separate metal and timber"),
            L("Collect spare fuse|Inspect left fuse|Inspect centre fuse|Inspect right fuse"),
            L("Collect gasket|Isolate valve|Install gasket|Reopen valve"),
            L("Collect replacement bulb|Lamp power switch|Remove failed bulb|Install new bulb"),
            L("Collect terminal fuse|Fit terminal fuse|Dispense load-test kit"),
            L("Check lamp lens / use CLEAN"),
            L("Check bench dust / use CLEAN|Sit on the polished bench"),
            L("Refuel nearby car|Verify five metres driven|Buy portable 25-unit fuel can / 5 credits"),
            L("Collect mixed load|Metal intake|Glass intake|Plastic intake"),
            L("Check window finish / use CLEAN"),
            L("Collection source|Move bundle to left bin|Move bundle to right bin"),
            L("Collect fresh poster|Mount in empty frame"),
            L("Collect salvage cargo|Load nearby car|Unload driven cargo"),
            L("Pick left litter|Pick centre litter|Pick right litter"),
            L("Refill measured carrier|Water left roots|Water centre roots|Water right roots"),
            L("Collect hose|Connect hydrant end|Connect garden end|Open water valve|Check flowing bed"),
            L("Collect filter|Replace fountain filter|Flush measured water"),
            L("Portable bench / ordinary TAKE|Move bench into seating pad"),
            L("Collect sapling|Transplant same sapling|Establish roots with water"),
            L("Retune ordinary display|Verify real moving picture"),
            L("Select requested channel 2|Verify channel 2"),
            L("Circuit 2-1-3|Lamp 1|Lamp 2|Lamp 3"),
            L("Rotate receiver clockwise|Rotate receiver anticlockwise|Test source alignment"),
            L("Activate source|Activate remote screen relay"),
            L("Meter calibration|Dispense ten measured units|Increase correction by five|Seal calibrated meter"),
            L("Collect identified sample|Apply physical test strip|Blue reagent|Amber reagent"),
            L("Increase valve opening|Decrease valve opening|Accept pressure 55-65"),
            L("Sample source A|Sample source B|Compare both strips||Treat source B"),
            L("Recover item under bench|Return identified lost object"),
            L("Allocate pump power|Allocate lighting power|Test shared capacity"),
            L("Read dusty notice / use CLEAN|Open requested amber valve"),
            L("Collect replacement roof panel|Install upstairs panel"),
            L("Collect trade timber|Offer two timber to a visible neighbour"),
            L("Collect three guard slats|Fit left slat|Fit centre slat|Fit right slat|Water protected planting")
        };
        private static string[] L(string s)=>s.Split('|');
        private bool TryUniqueResidentInteraction(Camera camera,int resident,out bool completed)
        {
            completed=false;var crowd=SlopCityCrowd.Instance;
            if(!camera||!crowd||!crowd.IsResidentPresented(resident)||!user)return false;
            var snapshot=crowd.Snapshot(resident);if(Vector3.Distance(camera.transform.position,snapshot.position+Vector3.up*.8f)>Reach)return false;
            int i=currentJobId==SlopCityJobCatalog.All[38].id?38:currentJobId==SlopCityJobCatalog.All[34].id?34:-1;if(i<0)return false;
            var r=UniqueRecord(i);if(r.phase!=1||r.completed)return false;
            if(i==38)
            {
                if(InventoryCount("wood")<2){Say("Offer two owned timber units to this neighbour.");return true;}
                if(!InventoryAdd(ItemId(r,1),"Neighbour's glass","glass",1,6)){Say("Make one inventory slot for the neighbour's glass.");return true;}
                InventoryConsume("wood",2);r.residentId=crowd.GetResidentId(resident);
            }
            else
            {
                if(r.residentId>=0&&crowd.GetResidentId(resident)!=r.residentId){Say("This belongs to resident "+r.residentId+".");return true;}
                if(!InventoryRemove(ItemId(r,0),1)){Say("Carry the identified lost object to its owner.");return true;}
            }
            r.completed=r.awarded=true;r.phase=100;completed=true;RenderUnique(i);Say(i==38?"The visible neighbour accepted your timber and gave you glass.":"The owner recovered the same lost object.");return true;
        }
        private bool IsUniqueElevatorTravel(SlopCityInteractable route)
            =>route&&uniqueJobState?.records!=null&&currentJobId==SlopCityJobCatalog.All[37].id&&UniqueRecord(37).phase==1&&uniqueRoutes[37]==route.transform;
        private Quaternion siteRotation(int i)=>uniqueWorld.Get(i).root.rotation;
        private static int SourceCount(int i,int slot)=>slot==0?(i==4||i==38?2:i==18||i==39?3:1):1;
        private static bool ExactSource(string id,int slot)
        {
            int i=UniqueMechanicIndex(id);
            return i==0||i==1||i==2||i==18||i==21||i==24||i==31||i==33||i==34||i==37;
        }
        private void DepositMail(int index,bool parcel)
        {
            if(index<0||index>=uniqueMailboxes.Length||!uniqueMailboxes[index])return;
            var item=uniqueMailboxes[index];if(parcel){item.ReceiveParcel();RememberWork(item);}
            RememberMailContent(item.GetComponent<SlopCitySalvageItem>().StableId,parcel);
            uniqueWorld.ShowMailboxContent(item.transform,parcel);
        }
        private void RememberMailContent(string id,bool parcel)
        {
            if(string.IsNullOrEmpty(id))return;
            var mail=uniqueJobState.mail??Array.Empty<UniqueMailRecord>();
            var record=Array.Find(mail,entry=>entry.id==id);
            if(record==null){if(mail.Length>=32)return;record=new UniqueMailRecord{id=id};Array.Resize(ref mail,mail.Length+1);mail[mail.Length-1]=record;uniqueJobState.mail=mail;}
            if(parcel)record.parcel=true;else record.letters=true;
        }
        private void RestoreMailContents()
        {
            if(!uniqueWorld||uniqueJobState?.records==null)return;
            // Existing v2 saves carry delivered bits and their exact addresses. Never rebind them here.
            foreach(int mechanic in PostalMechanics)
            {var r=UniqueRecord(mechanic);for(int address=0;address<3;address++)if((r.values[2]&(1<<address))!=0)RememberMailContent(r.sourceIds[8+address],mechanic==0);}
            var boxes=FindObjectsByType<SlopCitySalvageItem>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            foreach(var delivered in uniqueJobState.mail)
            {var box=Array.Find(boxes,item=>item.Category=="mailbox"&&item.StableId==delivered.id);if(!box)continue;if(delivered.parcel)uniqueWorld.ShowMailboxContent(box.transform,true);if(delivered.letters)uniqueWorld.ShowMailboxContent(box.transform,false);}
        }
        private static readonly int[] PostalMechanics={0,2};
        private static int PendingMailboxAddress(int mechanic,UniqueJobRecord r)
            =>mechanic==0?1:(r.values[2]&1)==0?0:(r.values[2]&2)==0?1:2;
        private void BindMailboxes(int mechanic)
        {
            var r=UniqueRecord(mechanic);var boxes=new List<SlopCitySalvageItem>();
            foreach(var box in FindObjectsByType<SlopCitySalvageItem>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(box.Category=="mailbox"&&box.name=="Salvage_Mailbox")boxes.Add(box);
            boxes.Sort((a,b)=>StringComparer.Ordinal.Compare(a.StableId,b.StableId));
            var used=new HashSet<string>();for(int i=0;i<3;i++)if(!string.IsNullOrEmpty(r.sourceIds[8+i]))used.Add(r.sourceIds[8+i]);
            for(int i=0;i<3;i++)
            {
                SlopCitySalvageItem box=null;string saved=r.sourceIds[8+i];
                if(!string.IsNullOrEmpty(saved))box=boxes.Find(candidate=>candidate.StableId==saved);
                else
                {
                    box=boxes.Find(candidate=>candidate.gameObject.activeInHierarchy&&!used.Contains(candidate.StableId));
                    if(!box)box=boxes.Find(candidate=>!used.Contains(candidate.StableId));
                    if(box){r.sourceIds[8+i]=box.StableId;used.Add(box.StableId);}
                }
                if(!box){uniqueMailboxes[i]=null;continue;}
                var item=box.GetComponent<SlopCityInteractable>();if(!item)item=box.gameObject.AddComponent<SlopCityInteractable>();
                item.Configure(SlopCityInteractionKind.Inspect,"Address "+(i+1)+" mailbox",true);item.ConfigureInteractionPoint(box.WorldBounds.center);
                var device=box.GetComponent<SlopCityJobDevice>();if(!device)device=box.gameObject.AddComponent<SlopCityJobDevice>();device.BindExternal(uniqueWorld,mechanic,i+1);uniqueMailboxes[i]=item;
            }
        }
        private bool TryUniqueResidentWaypoint(out Vector3 position,out string label)
        {
            position=Vector3.zero;label="";if(!uniqueWorld||!SlopCityCrowd.Instance||currentJobId!=SlopCityJobCatalog.All[34].id)return false;
            var r=UniqueRecord(34);if(r.phase!=1||r.residentId<0)return false;int index=SlopCityCrowd.Instance.FindResident(r.residentId);if(index<0)return false;
            var snapshot=SlopCityCrowd.Instance.Snapshot(index);position=snapshot.position+Vector3.up;label="Return property to "+snapshot.displayName;return true;
        }
        private static readonly int[] FuelTrackedJobs={13,18};
        private static string SourceCategory(int i,int slot)
        {
            if(slot==0){switch(i){case 0:return "parcel";case 1:return "window";case 2:return "letter";case 3:case 9:return "bulb";case 4:case 14:case 18:return "metal";case 6:case 37:return "panel";case 7:case 10:return "fuse";case 8:return "gasket";case 17:return "poster";case 19:return "plastic";case 21:return "hose";case 22:return "filter";case 24:return "sapling";case 31:case 33:return "sample";case 34:return null;case 38:case 39:return "wood";}}
            if(i==2&&slot<3)return "letter";if(i==4&&slot==1)return "gasket";if(i==14&&slot==1)return "glass";if(i==14&&slot==2)return "plastic";if(i==19&&slot<3)return "plastic";if(i==33&&slot==1)return "sample";return null;
        }
        private static string VehicleIdentity(Transform vehicle)
        {
            return SlopCitySalvageItem.SourceIdentity(vehicle);
        }
        private UniqueVehicleRecord FuelRecord(Transform vehicle)
        {
            if(!vehicle)return null;if(uniqueVehicleCache.TryGetValue(vehicle,out var cached))return cached;
            string id=VehicleIdentity(vehicle);UniqueVehicleRecord record=null;
            for(int i=0;i<uniqueJobState.vehicles.Length;i++)if(uniqueJobState.vehicles[i].id==id){record=uniqueJobState.vehicles[i];break;}
            if(record==null)
            {
                if(uniqueJobState.vehicles.Length>=32)
                {
                    // Capacity pressure only: retain all actual fleet records, including empty tanks.
                    // Unknown save identifiers must not exclude real cars or reset their fuel.
                    var liveIds=new HashSet<string>();liveIds.Add(id);
                    foreach(var car in jobVehicles)if(car)liveIds.Add(VehicleIdentity(car.transform));
                    var retained=new List<UniqueVehicleRecord>();
                    foreach(var saved in uniqueJobState.vehicles)if(liveIds.Contains(saved.id))retained.Add(saved);
                    uniqueJobState.vehicles=retained.ToArray();
                    if(uniqueJobState.vehicles.Length>=32)return null;
                }
                record=new UniqueVehicleRecord{id=id};
                var list=new List<UniqueVehicleRecord>(uniqueJobState.vehicles){record};uniqueJobState.vehicles=list.ToArray();
            }
            uniqueVehicleCache[vehicle]=record;return record;
        }
        // Explicit car USE only. Empty cars are handled even when supplies are missing;
        // successful consumption owns its one atomic Save through ObjectTransaction.
        internal bool TryUsePortableJobFuel(Transform target,out string message)
        {
            message="";var car=target?target.GetComponent<SlopCityInteractable>():null;
            if(!car||car.Kind!=SlopCityInteractionKind.Car)return false;
            var tank=FuelRecord(target);if(tank!=null&&tank.fuel>0)return false;
            if(tank==null){message="This car cannot be admitted to the bounded fuel registry.";return true;}
            if(InventoryCount("fuel")<1){message="Tank empty. Buy a five-credit portable fuel can at the roadside pump, carry it here and USE.";return true;}
            bool refilled=ObjectTransaction(()=>
            {
                if(!InventoryConsume("fuel",1))return false;
                tank.fuel=25;
                if(currentJobId==SlopCityJobCatalog.All[13].id)
                {var work=UniqueRecord(13);if(!work.completed){work.vehicleId=tank.id;work.startPosition=target.position;work.phase=1;work.values[1]=25;work.values[6]=0;}}
                NotifyWorldItemEvent("refuel",tank.id,"fuel",25);return true;
            });
            message=refilled?"One owned fuel can supplied 25 units to this car. USE again to drive.":"Refuelling could not be saved; your fuel can and empty tank were retained.";
            return true;
        }
        internal bool CanDriveJobVehicle(Transform vehicle)=>vehicle&&FuelRecord(vehicle)?.fuel>0;
        internal void TickVehicleFuel(Transform vehicle,float actualDistance)
        {
            if(!vehicle||float.IsNaN(actualDistance)||float.IsInfinity(actualDistance)||actualDistance<=0)return;
            var fuel=FuelRecord(vehicle);if(fuel==null)return;float distance=Mathf.Min(100,actualDistance);fuel.fuel=Mathf.Max(0,fuel.fuel-distance*.01f);fuel.driven+=distance;
            foreach(int i in FuelTrackedJobs){var r=UniqueRecord(i);if(r.vehicleId==fuel.id&&r.phase>0&&!r.completed)r.values[6]=Mathf.Min(1000000,r.values[6]+Mathf.RoundToInt(distance*100));}
        }
    }
}
