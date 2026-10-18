using System;
using System.Collections.Generic;
using UnityEngine;
namespace SlopCity
{
    public sealed partial class SlopCityCrowd
    {
        public const int TargetSmithCount=1984, TargetRedContactCount=42, TargetIndoorRedCount=9;
        public const int MaximumDetailedResidents=432, MaximumPresentedBrowser=192, MaximumPresentedNative=256;
        public const int MaximumCoarseUpdates=32, MaximumAdmissionVisits=32, MaximumAdmissions=4, MaximumRetirements=8;
        public const int MaximumNeighbourVisits=128, MaximumNeighbourCandidates=32;
        private const float AdmissionRadius=72f, RetirementRadius=90f;
        private struct Resident
        {
            public SlopCityNpcSeed seed;
            public Vector3 position;
            public int node,next,randomState,home;
            public float heading,phase,pause,talkReady,lastUpdate;
            public bool redContact;
        }
        private sealed class InteriorHome
        {
            public SlopCityBuildingInterior building;
            public int floor;
            public Transform root;
            public SlopCityCrowdRoute route;
        }
        private Resident[] residents=Array.Empty<Resident>();
        private int[] residentSlots=Array.Empty<int>(),presentationStamp=Array.Empty<int>();
        private Dictionary<int,int> residentIds=new Dictionary<int,int>();
        private readonly Dictionary<int,float> rememberedCooldowns=new Dictionary<int,float>();
        private List<InteriorHome> interiorHomes=new List<InteriorHome>();
        private Dictionary<long,int> homeLookup=new Dictionary<long,int>();
        private readonly Collider[] populationOverlaps=new Collider[24];
        private int activeCount,coarseCursor,admissionCursor,presentationSerial;
        private bool expandedPopulation,localAgentsInitialized;
        private float nextPopulationDiscovery;
        private int populationDiscoveryFailures;
        private const int MaximumPopulationDiscoveryAttempts=3;
        public int RegisteredCount { get { EnsureAgents();return residents.Length; } }
        public int SmithCount { get { EnsureAgents();return residents.Length-redContactCount; } }
        public int DetailedCount=>activeCount;
        public int PresentedCount {get;private set;}
        public int InteractiveCount=>PresentedCount;
        public int InteriorResidentCount {get;private set;}
        public int AdmittedInteriorRouteCount {get;private set;}
        public int LastCoarseUpdates {get;private set;}
        public int LastAdmissionVisits {get;private set;}
        public int LastAdmissions {get;private set;}
        public int LastRetirements {get;private set;}
        public int MaximumObservedNeighbourVisits {get;private set;}
        public int MaximumObservedNeighbourCandidates {get;private set;}
        public int WholePeopleDeferred {get;private set;}
        public int GetResidentId(int index){EnsureAgents();return index>=0&&index<residents.Length?residents[index].seed.residentId:0;}
        public int FindResident(int stableId){EnsureAgents();return residentIds.TryGetValue(stableId,out int index)?index:-1;}
        public bool IsResidentDetailed(int index)=>ActiveResident(index)!=null;
        private Agent ActiveResident(int index)=>index>=0&&index<residentSlots.Length&&residentSlots[index]>=0?agents[residentSlots[index]]:null;
        private bool IsPresented(int index)=>isActiveAndEnabled&&lastPresentationFrame>=Time.frameCount-1&&index>=0&&index<presentationStamp.Length&&presentationStamp[index]==presentationSerial&&presentationSerial>0;
        private SlopCityCrowdRoute PopulationRoute(int index)
        {
            if(index<0)return null;
            if(index<routes.Length)return routes[index];
            int home=index-routes.Length;return home<interiorHomes.Count&&interiorHomes[home].root&&interiorHomes[home].root.gameObject.activeInHierarchy?interiorHomes[home].route:null;
        }
        private void ResetPopulation()
        {
            residents=Array.Empty<Resident>();residentSlots=Array.Empty<int>();presentationStamp=Array.Empty<int>();residentIds.Clear();
            interiorHomes.Clear();homeLookup.Clear();rememberedCooldowns.Clear();expandedPopulation=localAgentsInitialized=false;activeCount=0;simulationTime=0;populationDiscoveryFailures=0;nextPopulationDiscovery=0;
        }
        private SlopCityNpcSeed NormalizedSeed(SlopCityNpcSeed seed,bool red)
        {
            seed.displayName=red?"Woman in Red":"Agent Smith";seed.appearance&=int.MaxValue;
            if((int)seed.role<0||(int)seed.role>=Dialogue.Length)seed.role=SlopCityNpcRole.Resident;
            seed.height=Finite(seed.height)?Mathf.Clamp(seed.height,.8f,2.5f):1.76f;seed.width=Finite(seed.width)?Mathf.Clamp(seed.width,.4f,2f):1f;
            seed.speed=Finite(seed.speed)?Mathf.Clamp(seed.speed,0,4):1f;seed.heading=Finite(seed.heading)?Mathf.Repeat(seed.heading,360):0;
            seed.phase=Finite(seed.phase)?seed.phase:0;seed.pause=Finite(seed.pause)?Mathf.Clamp(seed.pause,0,60):0;
            if(!ValidNode(seed.route,seed.next))seed.next=seed.node;return seed;
        }
        private void InitializePopulationRegistry()
        {
            if(population==null)population=Array.Empty<SlopCityNpcSeed>();if(routes==null)routes=Array.Empty<SlopCityCrowdRoute>();
            Resident[] prior=residents;rememberedCooldowns.Clear();
            for(int i=0;i<prior.Length;i++){Agent live=ActiveResident(i);rememberedCooldowns[prior[i].seed.residentId]=live!=null?live.talkReady:prior[i].talkReady;}
            residents=new Resident[population.Length];residentSlots=new int[population.Length];presentationStamp=new int[population.Length];Array.Fill(residentSlots,-1);residentIds.Clear();
            agents=new Agent[Mathf.Min(MaximumDetailedResidents,population.Length)];activeCount=0;redContactCount=0;
            var fallback=new Dictionary<int,int>();
            for(int i=0;i<population.Length;i++)
            {
                SlopCityNpcSeed seed=population[i];seed.residentId=SlopCityResidentIdentity.Resolve(seed);population[i]=seed;
                if(!SlopCityResidentIdentity.IsAuthored(seed.residentId)&&ValidNode(seed.route,seed.node))
                {int district=routes[seed.route].districtIndex;if(!fallback.TryGetValue(district,out int old)||ContactOrder(seed,i)<ContactOrder(population[old],old))fallback[district]=i;}
            }
            var fallbackReds=new HashSet<int>(fallback.Values);
            for(int i=0;i<population.Length;i++)
            {
                SlopCityNpcSeed seed=population[i];bool red=SlopCityResidentIdentity.IsAuthored(seed.residentId)?SlopCityResidentIdentity.IsRed(seed.residentId):fallbackReds.Contains(i);
                seed=NormalizedSeed(seed,red);if(!residentIds.TryAdd(seed.residentId,i))throw new InvalidOperationException("Duplicate immutable resident ID: "+seed.residentId);
                var record=new Resident{seed=seed,node=seed.node,next=seed.next,heading=seed.heading,phase=seed.phase,pause=seed.pause,randomState=173+seed.residentId*139,redContact=red,home=-1,lastUpdate=simulationTime};
                if(rememberedCooldowns.TryGetValue(seed.residentId,out float cooldown))record.talkReady=cooldown;
                if(ValidNode(seed.route,seed.node))record.position=routes[seed.route].nodes[seed.node].position;
                residents[i]=record;if(red)redContactCount++;
                if(activeCount<agents.Length&&ValidNode(seed.route,seed.node))AdmitResident(i,false);
            }
            if(!localAgentsInitialized){localAgents.Select(population,routes);localAgentsInitialized=true;}
            interiorHomes.Clear();homeLookup.Clear();AdmittedInteriorRouteCount=InteriorResidentCount=0;
            configured=true;expandedPopulation=false;Array.Clear(pendingParts,0,pendingParts.Length);pendingPartCount=0;batches.Clear();batchLookup.Clear();
        }
        private void TryPrepareExpandedPopulation()
        {
            if(expandedPopulation||!Application.isPlaying||populationDiscoveryFailures>=MaximumPopulationDiscoveryAttempts||Time.unscaledTime<nextPopulationDiscovery)return;
            nextPopulationDiscovery=Time.unscaledTime+1;
            SlopCityBuildingWorld world=GetComponentInParent<SlopCityBuildingWorld>();
            if(!world||world.Buildings.Count==0)return;
            try { PreparePopulation(world); }
            catch(Exception error)
            {
                populationDiscoveryFailures++;
                nextPopulationDiscovery=Time.unscaledTime+(1<<populationDiscoveryFailures);
                // Keep the authored registry usable and stop retrying a permanently failed expansion.
                Debug.LogWarning("Additional population unavailable (attempt "+populationDiscoveryFailures+"/"+MaximumPopulationDiscoveryAttempts+"): "+error.GetType().Name,this);
            }
        }
        /// <summary>One registry expansion; no extra meshes/materials/physics objects are allocated per resident.</summary>
        public void PreparePopulation(SlopCityBuildingWorld world)
        {
            EnsureAgents();if(expandedPopulation||!world||world.Buildings.Count==0)return;
            var buildings=new List<SlopCityBuildingInterior>();foreach(var building in world.Buildings)if(building)buildings.Add(building);
            buildings.Sort((a,b)=>string.CompareOrdinal(HierarchyId(a.transform),HierarchyId(b.transform)));
            // Build every fallible registry operation off to the side. A failed attempt
            // must leave both live body mappings and home/identity registries unchanged.
            var nextHomes=new List<InteriorHome>();
            var nextHomeLookup=new Dictionary<long,int>();
            var nextIds=new Dictionary<int,int>(residentIds);
            int max=0;foreach(var building in buildings)max=Mathf.Max(max,building.OccupiedFloorCount);
            for(int floor=0;floor<max;floor++)foreach(var building in buildings)
                if(floor<building.OccupiedFloorCount)
                {int home=nextHomes.Count;nextHomes.Add(new InteriorHome{building=building,floor=floor});nextHomeLookup.Add(HomeKey(building,floor),home);}
            int original=residents.Length,smiths=original-redContactCount;
            bool authoredCity=original==432;for(int id=1;id<=432&&authoredCity;id++)authoredCity=residentIds.ContainsKey(id);
            int addedSmiths=authoredCity?Mathf.Max(0,TargetSmithCount-smiths*4):0;
            int addedReds=authoredCity?Mathf.Max(0,TargetRedContactCount-redContactCount):0;
            int count=original+smiths*3+addedSmiths+addedReds;
            if(count>5000)throw new InvalidOperationException("Logical population exceeds the registry ceiling.");
            var nextResidents=new Resident[count];Array.Copy(residents,nextResidents,original);
            var slots=new int[count];Array.Fill(slots,-1);
            var stamps=new int[count];
            int next=original,indoor=0;
            for(int source=0;source<original;source++)if(!nextResidents[source].redContact)
                for(int copy=0;copy<3;copy++)
                {
                    Resident record=nextResidents[source];record.seed.residentId=100000+record.seed.residentId*4+copy;
                    record.seed.appearance=(int)(ContactOrder(record.seed,copy)&int.MaxValue);record.seed.displayName="Agent Smith";record.redContact=false;record.talkReady=rememberedCooldowns.TryGetValue(record.seed.residentId,out float ready)?ready:0;record.pause=0;record.randomState=record.seed.residentId*139+173;record.lastUpdate=simulationTime;
                    if(copy<2&&nextHomes.Count>0){record.home=indoor++%nextHomes.Count;record.seed.route=routes.Length+record.home;record.node=record.next=0;record.position=nextHomes[record.home].building.LandingFeet(nextHomes[record.home].floor);}
                    else{record.home=-1;SlopCityCrowdRoute graph=PopulationRoute(record.seed.route);record.node=Mathf.Abs(record.seed.residentId)%graph.nodes.Length;record.next=graph.nodes[record.node].neighbors[0];record.position=graph.nodes[record.node].position;}
                    if(!nextIds.TryAdd(record.seed.residentId,next))throw new InvalidOperationException("Duplicate generated resident ID.");nextResidents[next++]=record;
                }
            // Keep the preceding1695 records/IDs in their exact order. Supplements use
            // separate fixed ranges; no existing Smith is converted into a red contact.
            if(addedSmiths>0||addedReds>0)
            {
                var smithTemplates=new List<int>(smiths);var redTemplates=new List<int>(redContactCount);
                for(int source=0;source<original;source++)if(nextResidents[source].redContact)redTemplates.Add(source);else smithTemplates.Add(source);
                for(int added=0;added<addedSmiths;added++)
                {
                    Resident record=Supplement(nextResidents[smithTemplates[added%smithTemplates.Count]],200001+added,false,added);
                    if(nextHomes.Count>0)
                    {
                        record.home=indoor++%nextHomes.Count;record.seed.route=routes.Length+record.home;record.node=record.next=0;
                        record.position=nextHomes[record.home].building.LandingFeet(nextHomes[record.home].floor);
                    }
                    else PlaceSupplementOnStreet(ref record);
                    AddSupplement(ref next,record,nextResidents,nextIds);
                }
                // One rare contact per distinct ground-floor building. Farthest-first
                // selection spreads them across the city; floors retain ordinary streaming.
                var redHomes=SelectRedHomes(TargetIndoorRedCount,nextHomes);
                for(int added=0;added<addedReds;added++)
                {
                    Resident record=Supplement(nextResidents[redTemplates[added%redTemplates.Count]],300001+added,true,1000+added);
                    int indoorIndex=added-(addedReds-TargetIndoorRedCount);
                    if(indoorIndex>=0&&indoorIndex<redHomes.Count)
                    {
                        record.home=redHomes[indoorIndex];record.seed.route=routes.Length+record.home;record.node=record.next=0;
                        record.position=nextHomes[record.home].building.LandingFeet(0);indoor++;
                    }
                    else PlaceSupplementOnStreet(ref record);
                    AddSupplement(ref next,record,nextResidents,nextIds);
                }
            }
            if(authoredCity)PlacePublicRedContactsAboveground(nextResidents);
            // Only now retire relocated original bodies. All allocations/route validation
            // above succeeded; refresh slot/stamp copies after the pool's swap-removals.
            for(int index=0;index<original;index++)
                if(residents[index].seed.route!=nextResidents[index].seed.route&&residentSlots[index]>=0)
                {
                    RetireResident(residentSlots[index]);
                    Resident live=residents[index],placed=nextResidents[index];
                    placed.phase=live.phase;placed.pause=live.pause;placed.talkReady=live.talkReady;placed.randomState=live.randomState;
                    nextResidents[index]=placed;
                }
            Array.Copy(residentSlots,slots,residentSlots.Length);Array.Copy(presentationStamp,stamps,presentationStamp.Length);
            residents=nextResidents;residentSlots=slots;presentationStamp=stamps;residentIds=nextIds;
            interiorHomes=nextHomes;homeLookup=nextHomeLookup;redContactCount+=addedReds;
            InteriorResidentCount=indoor;expandedPopulation=true;populationDiscoveryFailures=0;
            // Building callbacks can precede this registry during startup. Discover existing admitted roots once.
            foreach(var building in buildings)
            {
                if(building.PreservedInterior){AdmitInteriorFloor(building,0,building.transform);continue;}
                foreach(Transform child in building.transform)
                    if(child.name.StartsWith("Runtime_Floor_",StringComparison.Ordinal)&&int.TryParse(child.name.Substring(14),out int floor))AdmitInteriorFloor(building,floor,child);
            }
        }
        private bool AbovegroundPopulationRoute(int route)
        {
            if(route<0||route>=routes.Length)return false;var graph=routes[route];
            if(graph==null||graph.nodes==null||graph.nodes.Length<2||graph.districtIndex<0||graph.districtIndex>8)return false;
            foreach(var node in graph.nodes)
            {
                if(node==null||!Finite(node.position.sqrMagnitude)||node.position.y<-.5f||node.neighbors==null||node.neighbors.Length==0)return false;
                foreach(int link in node.neighbors)if(link<0||link>=graph.nodes.Length||graph.nodes[link]==null)return false;
            }
            return true;
        }
        private void PlacePublicRedContactsAboveground(Resident[] records)
        {
            var counts=new int[routes.Length];
            for(int index=0;index<records.Length;index++)
            {Resident r=records[index];if(r.redContact&&r.home<0&&AbovegroundPopulationRoute(r.seed.route))counts[r.seed.route]++;}
            for(int index=0;index<records.Length;index++)
            {
                Resident r=records[index];if(!r.redContact||r.home>=0||AbovegroundPopulationRoute(r.seed.route))continue;
                int route=-1;for(int candidate=0;candidate<routes.Length;candidate++)
                    if(AbovegroundPopulationRoute(candidate)&&(route<0||counts[candidate]<counts[route]))route=candidate;
                if(route<0)throw new InvalidOperationException("No supported aboveground street route for a red contact.");
                r.seed.route=route;PlaceSupplementOnStreet(ref r);r.lastUpdate=simulationTime;
                records[index]=r;counts[route]++;
                // Normal bounded admission validates free physical space; no
                // relocated body is forced into an occupied street node here.
            }
        }
        private List<int> SelectRedHomes(int count,List<InteriorHome> homes)
        {
            var selected=new List<int>(count);
            for(int selection=0;selection<count;selection++)
            {
                int best=-1;float bestDistance=-1;
                for(int home=0;home<homes.Count;home++)
                {
                    InteriorHome candidate=homes[home];if(candidate.floor!=0||!candidate.building||selected.Contains(home))continue;
                    Vector3 point=candidate.building.EntranceFeet;float nearest=selected.Count==0?point.sqrMagnitude:float.PositiveInfinity;
                    foreach(int old in selected)nearest=Mathf.Min(nearest,(point-homes[old].building.EntranceFeet).sqrMagnitude);
                    if(nearest>bestDistance){best=home;bestDistance=nearest;}
                }
                if(best<0)break;selected.Add(best);
            }
            if(selected.Count!=count)throw new InvalidOperationException("Not enough distinct enterable buildings for indoor red contacts.");
            return selected;
        }
        private Resident Supplement(Resident template,int id,bool red,int variation)
        {
            template.seed.residentId=id;template.seed.appearance=(int)(ContactOrder(template.seed,variation)&int.MaxValue);
            template.seed.displayName=red?"Woman in Red":"Agent Smith";template.redContact=red;
            template.talkReady=rememberedCooldowns.TryGetValue(id,out float ready)?ready:0;template.pause=0;
            template.randomState=unchecked(id*139+173);template.lastUpdate=simulationTime;return template;
        }
        private void PlaceSupplementOnStreet(ref Resident record)
        {
            record.home=-1;SlopCityCrowdRoute graph=PopulationRoute(record.seed.route);
            int start=(int)((uint)record.seed.residentId*17u%(uint)graph.nodes.Length);
            record.node=start;record.next=graph.nodes[start].neighbors[0];record.position=graph.nodes[start].position;
            Vector3 direction=graph.nodes[record.next].position-record.position;
            record.heading=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
        }
        private void AddSupplement(ref int next,Resident record,Resident[] records,Dictionary<int,int> ids)
        {
            if(!ids.TryAdd(record.seed.residentId,next))throw new InvalidOperationException("Duplicate supplemental resident ID.");
            records[next++]=record;
        }
        private static string HierarchyId(Transform item){string value=item.name;for(Transform p=item.parent;p;p=p.parent)value=p.name+"/"+value;return value;}
        private static long HomeKey(SlopCityBuildingInterior building,int floor)=>((long)building.GetInstanceID()<<32)^(uint)floor;
        private Vector3 LogicalPosition(int index)
        {
            Resident record=residents[index];if(record.home>=0&&PopulationRoute(record.seed.route)==null){InteriorHome home=interiorHomes[record.home];return home.building?home.building.LandingFeet(home.floor):record.position;}return record.position;
        }
        private void AdmitResident(int resident,bool validate)
        {
            if(activeCount>=agents.Length||residentSlots[resident]>=0)return;Resident r=residents[resident];
            if(!ValidNode(r.seed.route,r.node)||!ValidNode(r.seed.route,r.next))return;
            if(validate)
            {
                if(player&&(player.position-r.position).sqrMagnitude<.9f*.9f)return;
                if(!TryPopulationFeet(r.position,out Vector3 feet))return;r.position=feet;
                for(int i=0;i<activeCount;i++)if((agents[i].position-r.position).sqrMagnitude<.75f*.75f)return;
            }
            Agent person=agents[activeCount]??new Agent();agents[activeCount]=person;person.seed=r.seed;person.residentIndex=resident;person.position=r.position;
            person.node=r.node;person.next=r.next;person.previous=-1;person.heading=r.heading;person.phase=r.phase;person.pause=r.pause;person.talkReady=r.talkReady;person.randomState=r.randomState;person.redContact=r.redContact;person.moving=false;person.blocked=person.obstacleCheck=0;
#if UNITY_WEBGL && !UNITY_EDITOR
            person.previousPosition=person.position;person.previousHeading=person.heading;
#endif
            residentSlots[resident]=activeCount++;
        }
        private void RetireResident(int slot)
        {
            Agent person=agents[slot];int id=person.residentIndex;Resident r=residents[id];r.position=person.position;r.node=person.node;r.next=person.next;r.heading=person.heading;r.phase=person.phase;r.pause=person.pause;r.talkReady=person.talkReady;r.randomState=person.randomState;r.lastUpdate=simulationTime;residents[id]=r;residentSlots[id]=-1;presentationStamp[id]=0;
            int last=--activeCount;Agent available=agents[last];agents[last]=person;
            if(slot!=last){agents[slot]=available;residentSlots[available.residentIndex]=slot;}
        }
        private void RefreshPopulationSlots(float delta)
        {
            LastCoarseUpdates=LastAdmissionVisits=LastAdmissions=LastRetirements=0;if(!expandedPopulation||residents.Length==0)return;
            for(int slot=activeCount-1;slot>=0&&LastRetirements<MaximumRetirements;slot--)
            {
                Agent p=agents[slot];Resident r=residents[p.residentIndex];bool retiredFloor=r.home>=0&&PopulationRoute(p.seed.route)==null;
                if(retiredFloor||(player&&(p.position-player.position).sqrMagnitude>RetirementRadius*RetirementRadius))
                {RetireResident(slot);LastRetirements++;}
            }
            for(int work=0;work<MaximumCoarseUpdates&&residents.Length>0;work++)
            {
                int index=coarseCursor++%residents.Length;if(coarseCursor>=residents.Length)coarseCursor=0;LastCoarseUpdates++;
                if(residentSlots[index]>=0)continue;AdvanceLogical(index);
            }
            if(!player)return;
            for(int visit=0;visit<MaximumAdmissionVisits;visit++)
            {
                int index=admissionCursor++%residents.Length;if(admissionCursor>=residents.Length)admissionCursor=0;LastAdmissionVisits++;
                if(activeCount>=agents.Length||LastAdmissions>=MaximumAdmissions)continue;
                if(residentSlots[index]>=0||!ValidNode(residents[index].seed.route,residents[index].node))continue;
                if((residents[index].position-player.position).sqrMagnitude>AdmissionRadius*AdmissionRadius)continue;
                int before=activeCount;AdmitResident(index,true);if(activeCount>before)LastAdmissions++;
            }
        }
        private void AdvanceLogical(int index)
        {
            Resident r=residents[index];SlopCityCrowdRoute graph=PopulationRoute(r.seed.route);
            float elapsed=Mathf.Clamp(simulationTime-r.lastUpdate,0,3.2f);r.lastUpdate=simulationTime;
            if(graph==null||graph.nodes.Length<2){residents[index]=r;return;}
            if(!ValidNode(r.seed.route,r.node)||!ValidNode(r.seed.route,r.next)){r.node=Mathf.Abs(r.seed.residentId)%graph.nodes.Length;r.next=graph.nodes[r.node].neighbors[0];r.position=graph.nodes[r.node].position;}
            if(r.pause>0){float waiting=Mathf.Min(r.pause,elapsed);r.pause-=waiting;elapsed-=waiting;}
            float distance=r.seed.speed*elapsed;
            for(int hop=0;hop<4&&distance>0;hop++)
            {
                Vector3 destination=graph.nodes[r.next].position;float length=Vector3.Distance(r.position,destination);
                if(length>distance){r.position=Vector3.MoveTowards(r.position,destination,distance);break;}
                r.position=destination;distance-=length;r.node=r.next;int[] links=graph.nodes[r.node].neighbors;r.randomState=unchecked(r.randomState*1664525+1013904223);r.next=links[(int)((uint)r.randomState%(uint)links.Length)];
            }
            Vector3 direction=graph.nodes[r.next].position-r.position;if(direction.sqrMagnitude>.0001f)r.heading=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
            residents[index]=r;
        }
        private bool TryPopulationFeet(Vector3 hint,out Vector3 feet)
        {
            feet=hint;int count=Physics.RaycastNonAlloc(hint+Vector3.up*.7f,Vector3.down,sweepHits,1.35f,~0,QueryTriggerInteraction.Ignore);if(count==sweepHits.Length)return false;
            float nearest=float.PositiveInfinity;CharacterController controller=playerController?playerController.GetComponent<CharacterController>():null;
            for(int i=0;i<count;i++){RaycastHit hit=sweepHits[i];if(LayoutCollider(hit.collider,controller)&&hit.normal.y>.78f&&hit.distance<nearest){nearest=hit.distance;feet=hit.point+Vector3.up*.025f;}}
            if(!Finite(nearest)||Mathf.Abs(feet.y-hint.y)>.24f)return false;
            count=Physics.OverlapCapsuleNonAlloc(feet+Vector3.up*.34f,feet+Vector3.up*1.64f,.32f,populationOverlaps,~0,QueryTriggerInteraction.Ignore);if(count==populationOverlaps.Length)return false;
            for(int i=0;i<count;i++)if(LayoutCollider(populationOverlaps[i],controller))return false;return true;
        }
    }
}
