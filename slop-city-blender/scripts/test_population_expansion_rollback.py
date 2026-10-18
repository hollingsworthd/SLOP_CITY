#!/usr/bin/env python3
"""Execute actual population expansion/retry C# with deterministic Unity substitutes.

Checks failed initialization ownership and successful identities/body mappings. No
Unity player, native heap, graphics, network, save or device acceptance is claimed.
"""
import argparse
import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('ownership', Path(__file__).with_name('test_native_allocation_rollback.py'))
ownership = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ownership)

STUBS = r'''
using System;using System.Collections;using System.Collections.Generic;using System.Linq;using UnityEngine;
namespace UnityEngine {
 public class Object {private static int next;private int id=++next;public int GetInstanceID()=>id;public static implicit operator bool(Object o)=>o!=null;}
 public class Transform:Object,IEnumerable<Transform>{public string name="Building";public Transform parent;public Vector3 position;public IEnumerator<Transform> GetEnumerator(){yield break;}IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();}
 public struct Vector3{public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public float sqrMagnitude=>x*x+y*y+z*z;public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
 public static class Mathf{public static int Max(int a,int b)=>Math.Max(a,b);public static int Abs(int a)=>Math.Abs(a);public static float Min(float a,float b)=>Math.Min(a,b);public static float Atan2(float a,float b)=>(float)Math.Atan2(a,b);public const float Rad2Deg=57.29578f;}
 public static class Application{public static bool isPlaying=true;}
 public static class Time{public static float unscaledTime;}
 public static class Debug{public static int warnings;public static void LogWarning(string message,object context){warnings++;}}
}
public sealed class SlopCityBuildingInterior:UnityEngine.Object {
 public Transform transform=new Transform();public int OccupiedFloorCount=2;public bool PreservedInterior;
 public Vector3 EntranceFeet=>transform.position;
 public static int calls,failAt=-1;
 public Vector3 LandingFeet(int floor){if(++calls==failAt)throw new InvalidOperationException("Injected landing failure");return new Vector3(transform.position.x,floor*3.2f,transform.position.z);}
}
public sealed class SlopCityBuildingWorld:UnityEngine.Object{public List<SlopCityBuildingInterior> Buildings=new List<SlopCityBuildingInterior>();}
'''
HARNESS = r'''
public sealed class Harness {
 __CODE__
 private SlopCityBuildingWorld world;private SlopCityCrowdRoute[] routes;private Agent[] agents;
 private float simulationTime=12;private int activeCount,redContactCount;private bool expandedPopulation,localAgentsInitialized;
 private int coarseCursor,admissionCursor,presentationSerial;private float nextPopulationDiscovery;
 private int populationDiscoveryFailures;private const int MaximumPopulationDiscoveryAttempts=3;
 private Resident[] residents;private int[] residentSlots,presentationStamp;
 private Dictionary<int,int> residentIds=new Dictionary<int,int>();
 private readonly Dictionary<int,float> rememberedCooldowns=new Dictionary<int,float>();
 private List<InteriorHome> interiorHomes=new List<InteriorHome>();private Dictionary<long,int> homeLookup=new Dictionary<long,int>();
 private int InteriorResidentCount,AdmittedInteriorRouteCount;
 private void EnsureAgents(){}
 private T GetComponentInParent<T>() where T:class=>world as T;
 private SlopCityCrowdRoute PopulationRoute(int i)=>i>=0&&i<routes.Length?routes[i]:null;
 private void AdmitInteriorFloor(SlopCityBuildingInterior building,int floor,Transform root){}
 private static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
 private static uint ContactOrder(SlopCityNpcSeed seed,int i)=>unchecked((uint)seed.appearance*747796405u+(uint)i*2891336453u);
 private static int checks;
 private static void Check(bool pass,string message){checks++;if(!pass)throw new Exception(message);}
 private void Initialize(int buildings=9){
  world=new SlopCityBuildingWorld();for(int i=0;i<buildings;i++)AddBuilding(i);
  routes=new SlopCityCrowdRoute[10];for(int i=0;i<routes.Length;i++)routes[i]=new SlopCityCrowdRoute{districtIndex=i,nodes=new[]{new SlopCityCrowdNode{position=new Vector3(i,i==9?-3:0,0),neighbors=new[]{1}},new SlopCityCrowdNode{position=new Vector3(i,i==9?-3:0,2),neighbors=new[]{0}}}};
  residents=new Resident[432];residentSlots=new int[432];Array.Fill(residentSlots,-1);presentationStamp=new int[432];redContactCount=11;
  for(int i=0;i<432;i++){var seed=new SlopCityNpcSeed{residentId=i+1,appearance=i,route=i<2?9:i%9,height=1.76f,width=1};residents[i]=new Resident{seed=seed,redContact=i<11,home=-1,node=0,next=1,position=routes[seed.route].nodes[0].position};residentIds.Add(i+1,i);}
  agents=new Agent[432];int[] active={0,1,20};activeCount=3;
  for(int slot=0;slot<3;slot++){int i=active[slot];agents[slot]=new Agent{residentIndex=i,seed=residents[i].seed,position=residents[i].position,node=0,next=1,phase=2+slot,pause=3+slot,talkReady=4+slot,randomState=5+slot};residentSlots[i]=slot;presentationStamp[i]=7;}
 }
 private void AddBuilding(int i){world.Buildings.Add(new SlopCityBuildingInterior{transform=new Transform{name="Building_"+i,position=new Vector3(i*10,0,i*7)}});}
 private string Snapshot()=>string.Join("|",residents.Select(r=>r.seed.residentId+":"+r.seed.route+":"+r.home+":"+r.position.x+":"+r.position.y+":"+r.position.z+":"+r.phase+":"+r.talkReady))
  +"/"+string.Join(",",residentSlots)+"/"+string.Join(",",presentationStamp)+"/"+string.Join(",",residentIds.Select(k=>k.Key+":"+k.Value))+"/"+interiorHomes.Count+"/"+homeLookup.Count+"/"+redContactCount+"/"+activeCount+"/"+InteriorResidentCount+"/"+expandedPopulation+"/"+string.Join("|",agents.Take(activeCount).Select(a=>a.residentIndex+":"+a.position.x+":"+a.position.y+":"+a.position.z+":"+a.phase+":"+a.pause+":"+a.talkReady+":"+a.randomState));
 private void Failure(Action attempt,string label,Type expected=null){
  var oldResidents=residents;var oldSlots=residentSlots;var oldStamps=presentationStamp;var oldIds=residentIds;var oldHomes=interiorHomes;var oldLookup=homeLookup;string before=Snapshot();
  bool failed=false;try{attempt();}catch(Exception error){if(error.GetType()!=(expected??typeof(InvalidOperationException)))throw;failed=true;}
  Check(failed,label+" actually fails");Check(ReferenceEquals(oldResidents,residents)&&ReferenceEquals(oldSlots,residentSlots)&&ReferenceEquals(oldStamps,presentationStamp)&&ReferenceEquals(oldIds,residentIds)&&ReferenceEquals(oldHomes,interiorHomes)&&ReferenceEquals(oldLookup,homeLookup),label+" preserves live container identities");
  Check(Snapshot()==before,label+" preserves all resident IDs/slots/stamps/homes/counters");
 }
 private void Success(){
  SlopCityBuildingInterior.failAt=-1;PreparePopulation(world);
  Check(expandedPopulation&&residents.Length==2026&&residentIds.Count==2026&&redContactCount==42,"Exact successful population totals");
  Check(interiorHomes.Count==18&&homeLookup.Count==18&&InteriorResidentCount==1151,"Exact homes and indoor counts");
  for(int i=0;i<432;i++)Check(residents[i].seed.residentId==i+1&&residents[i].redContact==(i<11),"Original identity/order/contact preserved");
  int next=432;for(int i=11;i<432;i++)for(int copy=0;copy<3;copy++)Check(residents[next++].seed.residentId==100000+(i+1)*4+copy,"Existing generated identity preserved");
  for(int i=0;i<300;i++)Check(residents[next++].seed.residentId==200001+i,"Smith supplement ID preserved");
  for(int i=0;i<31;i++)Check(residents[next++].seed.residentId==300001+i,"Red supplement ID preserved");
  Check(activeCount==1&&residentSlots[0]==-1&&residentSlots[1]==-1&&residentSlots[20]==0&&agents[0].residentIndex==20,"Relocated live bodies retired and swap mappings preserved");
  Check(residents[0].phase==2&&residents[1].phase==3&&residents[0].talkReady==4&&residents[1].talkReady==5,"Retirement retains live cooldown and animation state");
  Check(presentationStamp[0]==0&&presentationStamp[1]==0&&presentationStamp[20]==7,"Only relocated presentation stamps retired");
  Check(residents.Count(r=>r.redContact&&r.home>=0)==9&&residents.Count(r=>r.redContact&&r.home<0)==33,"Red street/interior split preserved");
  string before=Snapshot();PreparePopulation(world);Check(before==Snapshot(),"Repeated successful preparation is idempotent");
 }
 public static void Main(){
  var h=new Harness();h.Initialize();
  foreach(int stage in new[]{1,100,842,1142,1151}){SlopCityBuildingInterior.calls=0;SlopCityBuildingInterior.failAt=stage;h.Failure(()=>h.PreparePopulation(h.world),"Landing allocation stage "+stage);}
  SlopCityBuildingInterior.failAt=-1;
  h.world.Buildings.Add(h.world.Buildings[0]);h.Failure(()=>h.PreparePopulation(h.world),"Duplicate home",typeof(ArgumentException));h.world.Buildings.RemoveAt(h.world.Buildings.Count-1);
  // The real late failure happens after home/array/generated-ID preparation.
  var late=new Harness();late.Initialize(8);for(int i=0;i<100;i++)late.Failure(()=>late.PreparePopulation(late.world),"Repeated missing red home "+i);
  string pristine=late.Snapshot();int warnings=Debug.warnings;
  for(int i=0;i<1000;i++){Time.unscaledTime=i;late.TryPrepareExpandedPopulation();}
  Check(Debug.warnings-warnings==3&&late.populationDiscoveryFailures==3,"Automatic discovery makes only three failed attempts");Check(late.Snapshot()==pristine,"Exhausted automatic retries retain no registry/home growth");
  late.AddBuilding(8);late.Success();Check(late.populationDiscoveryFailures==0,"Explicit later successful setup clears failure count");
  // Fail after all supplements when aboveground relocation cannot choose a route.
  for(int i=0;i<h.routes.Length;i++)h.routes[i].districtIndex=-1;
  h.Failure(()=>h.PreparePopulation(h.world),"Late relocation failure");
  for(int i=0;i<h.routes.Length;i++)h.routes[i].districtIndex=i;
  h.Success();h.populationDiscoveryFailures=3;h.nextPopulationDiscovery=100;
  h.ResetPopulation();Check(h.residents.Length==0&&h.residentSlots.Length==0&&h.presentationStamp.Length==0&&h.residentIds.Count==0&&h.interiorHomes.Count==0&&h.homeLookup.Count==0&&h.populationDiscoveryFailures==0&&h.nextPopulationDiscovery==0,"Reconfiguration resets registries and retry suppression");
  Console.WriteLine(checks+" checks: actual expansion failures/retries retain no partial registry; exact2026 identities/homes/body mappings and later success pass.");
 }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', type=Path, required=True)
    args = parser.parse_args()
    resources = args.unity.resolve() / 'Contents/Resources/Scripting/MonoBleedingEdge'
    mono = resources / 'bin/mono'
    compiler = resources / 'lib/mono/4.5/csc.exe'
    crowd = (ROOT / 'Unity/Assets/Scripts/SlopCityCrowd.cs').read_text()
    population = (ROOT / 'Unity/Assets/Scripts/SlopCityCrowdPopulation.cs').read_text()
    declarations = '\n'.join(ownership.block(crowd, token) for token in [
        'public enum SlopCityNpcRole', 'public sealed class SlopCityCrowdNode',
        'public sealed class SlopCityCrowdRoute', 'public struct SlopCityNpcSeed'])
    members = '\n'.join(ownership.block(population, token) for token in [
        'private struct Resident', 'private sealed class InteriorHome',
        'private void TryPrepareExpandedPopulation(', 'public void PreparePopulation(',
        'private bool AbovegroundPopulationRoute(', 'private void PlacePublicRedContactsAboveground(',
        'private List<int> SelectRedHomes(', 'private Resident Supplement(',
        'private void PlaceSupplementOnStreet(', 'private void AddSupplement(',
        'private void RetireResident(', 'private void ResetPopulation('])
    helpers = population[population.index('private static string HierarchyId('):population.index('private Vector3 LogicalPosition(')]
    # Keep retry budgets sourced from production, so changing the guard is exercised.
    import re
    limit = re.search(r'private const int MaximumPopulationDiscoveryAttempts=(\d+);', population)[1]
    source = STUBS + declarations + HARNESS.replace('__CODE__', ownership.block(crowd, 'private sealed class Agent') + members + helpers)
    source = source.replace('MaximumPopulationDiscoveryAttempts=3;', 'MaximumPopulationDiscoveryAttempts=' + limit + ';')
    constants = re.search(r'public const int TargetSmithCount[^;]+;', population)[0]
    source = source.replace('public sealed class Harness {', 'public sealed class Harness { ' + constants)
    with tempfile.TemporaryDirectory(prefix='slop-city-population-rollback-') as directory:
        path = Path(directory) / 'Population.cs'
        path.write_text(source)
        for mode, defines in [('native', []), ('browser', ['-define:UNITY_WEBGL'])]:
            target = Path(directory) / (mode + '.exe')
            subprocess.run([str(mono), str(compiler), '-nologo', '-nowarn:0169,0649,0414', '-out:' + str(target), *defines, str(path)], check=True)
            subprocess.run([str(mono), str(target)], check=True)
    print('Actual C# population preparation and discovery retry branches passed; Unity/device heap acceptance remains separate.')


if __name__ == '__main__':
    main()
