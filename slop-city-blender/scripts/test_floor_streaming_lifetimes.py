#!/usr/bin/env python3
"""Fault-test actual floor retirement/admission methods with managed substitutes.

No Unity execution, OOM induction, browser or physical-device work. Tests ownership
and budget decisions under failures before and after mutation.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from test_native_allocation_rollback import block

SOURCE = Path(__file__).resolve().parents[1] / 'Unity/Assets/Scripts'
COMMON = r'''
using System;using System.Collections.Generic;using System.Linq;using UnityEngine;
namespace UnityEngine {
 public class Object {public bool dead;public static implicit operator bool(Object o)=>!ReferenceEquals(o,null)&&!o.dead;}
 public class Transform:Object {public GameObject gameObject;public string name;public Vector3 position;public int GetInstanceID()=>1;public T[] GetComponentsInChildren<T>() where T:class=>gameObject.items.OfType<T>().ToArray();public T GetComponent<T>() where T:class=>gameObject.items.OfType<T>().FirstOrDefault();public Vector3 InverseTransformPoint(Vector3 v)=>v;}
 public class GameObject:Object {public Transform transform;public bool active=true;public readonly List<object> items=new List<object>();public GameObject(){transform=new Transform{gameObject=this};}public void SetActive(bool value){active=value;}public bool activeInHierarchy=>active&&!dead;}
 public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}}
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 up=>new Vector3(0,1,0);public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);}
 public static class Time {public static int frameCount;public static float unscaledTime;}
 public static class Application {public static bool isPlaying=true;}
 public static class Mathf {public static float Abs(float f)=>Math.Abs(f);public static int Max(int a,int b)=>Math.Max(a,b);public static float Max(float a,float b)=>Math.Max(a,b);}
 public static class Debug {public static int warnings;public static void LogWarning(string m,object owner){warnings++;}}
}
public static class Fault {
 public static readonly HashSet<string> labels=new HashSet<string>();public static bool after,persistent,refuseDestroy;
 public static void Arm(bool afterMutation,bool repeat,params string[] stages){labels.Clear();foreach(var s in stages)labels.Add(s);after=afterMutation;persistent=repeat;}
 public static void Hit(string stage,Action mutation){if(labels.Contains(stage)&&!after)Throw(stage);mutation();if(labels.Contains(stage)&&after)Throw(stage);}
 static void Throw(string stage){if(!persistent)labels.Remove(stage);throw new InvalidOperationException("Injected "+stage);}
 public static void Clear(){labels.Clear();refuseDestroy=false;}
}
public static class Checks {public static int count;public static void That(bool value,string why){count++;if(!value)throw new Exception(why);}}
'''
FLOOR = r'''
public class SlopCityInteractable {public string name="device";public struct SessionState{}public SessionState CaptureSessionState(){Fault.Hit("capture",()=>{});return default;}public void RestoreSessionState(SessionState s){}}
public class SlopCityInteriorFurnishing {public bool released;public void Release(){Fault.Hit("furnish",()=>released=true);}}
public static class SlopCityInteriorPopulation {public static readonly HashSet<Transform> admitted=new HashSet<Transform>();public static void AdmitFloor(Floor b,int f,Transform root){Fault.Hit("admit",()=>admitted.Add(root));}public static void RetireFloor(Floor b,int f){Fault.Hit("population",()=>admitted.RemoveWhere(t=>t.gameObject.owner==b&&t.gameObject.floor==f));}}
public static class SlopCityInteriorBroadcast {public static void ReleaseFloor(Transform root){Fault.Hit("broadcast",()=>{});}}
public static class SlopCityInteriorGeometry {
 public static int live,created;public static object Floor,Wall;
 public static Transform Root(Transform parent,string name,Vector3 point){var root=new GameObject();root.owner=parent.gameObject.owner;root.floor=int.Parse(name.Substring(name.LastIndexOf('_')+1));root.transform.name=name;root.items.Add(new SlopCityInteractable());root.items.Add(new SlopCityInteriorFurnishing());live++;created++;return root.transform;}
 public static void Slab(Transform root,string name,float f,Vector2 a,Vector2 b,object m,float thickness=.16f){}
 public static void Dispose(GameObject root){if(Fault.refuseDestroy){root.active=false;throw new InvalidOperationException("deactivated before refused destroy");}Fault.Hit("dispose",()=>{if(!root.dead){root.dead=root.transform.dead=true;root.active=false;live--;}});}
}
public class SlopCityElevator:UnityEngine.Object {public GameObject gameObject=new GameObject();public bool IsMoving;public int TargetFloor,CurrentFloor;public bool ReleaseEmpty()=>true;}
public enum SlopCityElevatorButtonKind {Call}
public class SlopCityElevatorButton:UnityEngine.Object {public string name;public void Configure(object e,SlopCityElevatorButtonKind k,int f){}}
public class SlopCityFirstPerson:UnityEngine.Object {public Transform transform=new GameObject().transform;}
public class SlopCityBuildingWorld:UnityEngine.Object {public bool DeferTowerArchitecture=true;public int DetailedFloorBudget=2;public void GeometryChanged(){}}
public class Floor:UnityEngine.Object {
 private SlopCityBuildingWorld world=new SlopCityBuildingWorld();private SlopCityFirstPerson player;
 private bool architectureReady,PreservedInterior,floorRetirementWarning;private Transform failedFloorRoot;private int failedFloorIndex=-1,floorAdmissionAfterFrame=-1,streamingFloor=-1;
 private readonly Dictionary<int,Transform> loaded=new Dictionary<int,Transform>();private readonly Dictionary<string,SlopCityInteractable.SessionState> utilityState=new Dictionary<string,SlopCityInteractable.SessionState>();
 private readonly List<SlopCityElevatorButton> routeButtons=new List<SlopCityElevatorButton>();
 public int FloorCount=>4;public float[] LocalFloorElevations={0,3.2f,6.4f,9.6f};public Vector3 ShaftLocalCenter;public SlopCityElevator Elevator;private Transform transform;
 public Floor(){transform=new GameObject().transform;transform.gameObject.owner=this;}
 void EnsureArchitecture(){}Vector2 GetFloorSize(int floor)=>new Vector2(10,10);void BuildRooms(Transform root,int floor,Vector2 size){Fault.Hit("build",()=>{});}void BuildRoof(Transform root,Vector2 size){Fault.Hit("build",()=>{});}
 bool ContainsHorizontal(Vector3 point,float margin)=>false;bool ContainsInterior(Vector3 point)=>false;bool ReleaseArchitecture()=>true;
 public int Owned=>loaded.Count+(failedFloorRoot?1:0);public bool FailedOwned=>failedFloorRoot;public Transform RootAt(int f)=>loaded.TryGetValue(f,out Transform root)?root:null;
 // FLOOR_METHODS
 public static void Run(){
  foreach(bool after in new[]{false,true})foreach(string stage in new[]{"capture","population","broadcast","furnish","dispose"}){
   Fault.Clear();Time.frameCount+=10;var b=new Floor();b.EnsureFloor(0);Fault.Arm(after,false,stage);b.ReleaseFloor(0);
   Checks.That(SlopCityInteriorGeometry.live<=1&&b.Owned<=1,"one root maximum after retirement fault "+stage);
   Fault.Clear();b.ReleaseFloor(0);Checks.That(SlopCityInteriorGeometry.live==0&&b.Owned==0,"retirement retry releases exact root "+stage);
  }
  Fault.Clear();Time.frameCount+=10;var inactive=new Floor();inactive.EnsureFloor(0);Fault.refuseDestroy=true;inactive.ReleaseFloor(0);
  Checks.That(inactive.Owned==1&&!inactive.IsFloorLoaded(0),"deactivated retained floor is not advertised as usable support");int prior=SlopCityInteriorGeometry.created;
  try{inactive.EnsureFloor(0);}catch(InvalidOperationException){}Checks.That(SlopCityInteriorGeometry.created==prior,"inactive retained root cannot be duplicated");
  Fault.Clear();inactive.ReleaseFloor(0);Checks.That(SlopCityInteriorGeometry.live==0,"inactive retirement retry recovers");
  foreach(bool after in new[]{false,true}){
   Fault.Clear();Time.frameCount+=10;var b=new Floor();b.EnsureFloor(0);b.EnsureFloor(1);int initial=SlopCityInteriorGeometry.created;
   Fault.Arm(after,true,"capture");for(int i=0;i<12;i++){b.StreamAround(new Vector3(0,9.6f,0),2);Checks.That(b.Owned==2&&SlopCityInteriorGeometry.created==initial,"failed streaming retirement cannot allocate replacement rooms");}
   Checks.That(!b.PrepareFloor(3)&&b.Owned==2,"arrival cannot exceed floor budget after failed retirement");Checks.That(!b.ReleaseStream()&&b.Owned==2,"tower retirement reports retained room ownership");
   Fault.Clear();b.ReleaseStream();Checks.That(SlopCityInteriorGeometry.live==0,"streaming recovery releases retained rooms");
   foreach(string stage in new[]{"build","admit"}){
    Time.frameCount+=10;var c=new Floor();Fault.Arm(after,false,stage);try{c.EnsureFloor(1);}catch(InvalidOperationException){}
    Checks.That(c.Owned==0&&SlopCityInteriorGeometry.live==0&&SlopCityInteriorPopulation.admitted.Count==0,"failed floor transaction releases root/registry "+stage);
   }
   Time.frameCount+=10;var d=new Floor();Fault.Arm(after,true,"build","dispose");try{d.EnsureFloor(1);}catch(InvalidOperationException){}
   int failedCreated=SlopCityInteriorGeometry.created;Checks.That(d.FailedOwned||SlopCityInteriorGeometry.live==0,"failed disposal retains native root owner");
   for(int i=0;i<12;i++){try{d.EnsureFloor(2);}catch(InvalidOperationException){}Checks.That(SlopCityInteriorGeometry.created==failedCreated&&SlopCityInteriorGeometry.live<=1,"failed cleanup blocks repeated floor allocation");}
   Fault.Clear();try{d.EnsureFloor(2);}catch(InvalidOperationException){}Checks.That(!d.FailedOwned&&SlopCityInteriorGeometry.live==0,"cleanup recovery releases failed root before replacement frame");
   Time.frameCount++;d.EnsureFloor(2);d.ReleaseFloor(2);Checks.That(SlopCityInteriorGeometry.live==0,"fresh next-frame admission recovers normally");
  }
  Console.WriteLine("PASS "+Checks.count+" actual floor retirement/admission ownership checks");
 }
}
class Entry{static void Main(){Floor.Run();}}
'''
WORLD = r'''
public class SlopCityFirstPerson:UnityEngine.Object {public Transform transform=new GameObject().transform;public void ShowFeedback(string s){}}
public class SlopCityElevator:UnityEngine.Object {public GameObject gameObject=new GameObject();public bool PlayerInside,CanUnload=true;public bool Release()=>true;}
public class SlopCityBuildingInterior:UnityEngine.Object {
 public bool IsTower=true,consider,allowRelease;public int LoadedFloorCount=2,streams,ensures;public string name="tower";public SlopCityElevator Elevator;
 public bool ContainsInterior(Vector3 feet)=>false;public float HorizontalDistanceSquared(Vector3 feet)=>0;
 public bool ReleaseStream(){if(!allowRelease)return false;Elevator=null;LoadedFloorCount=0;return true;}
 public void EnsureElevator(){ensures++;Elevator=new SlopCityElevator();}public void StreamAround(Vector3 feet,int budget){streams++;LoadedFloorCount=budget;}
}
public class World:UnityEngine.Object {
 List<SlopCityBuildingInterior> buildings=new List<SlopCityBuildingInterior>();List<SlopCityElevator> stationLifts=new List<SlopCityElevator>();
 SlopCityFirstPerson player=new SlopCityFirstPerson();bool constrainedBrowser=true;int TowerBudget=>1;int DetailedFloorBudget=>2;int detailAdmissionAfterFrame=-1;float nextRefresh;
 Dictionary<SlopCityBuildingInterior,float> streamingRetry=new Dictionary<SlopCityBuildingInterior,float>();
 bool ShouldConsiderTowerDetail(SlopCityBuildingInterior b,Vector3 feet)=>b.consider;bool StationNearby(SlopCityElevator lift,Vector3 feet)=>false;
 // WORLD_METHODS
 public static void Main(){
  var world=new World();var old=new SlopCityBuildingInterior{Elevator=new SlopCityElevator()};var candidate=new SlopCityBuildingInterior{consider=true,LoadedFloorCount=0};world.buildings.Add(old);world.buildings.Add(candidate);
  for(int i=0;i<12;i++){Time.frameCount++;world.RefreshStreaming();}
  Checks.That(old.Elevator&&old.LoadedFloorCount==2,"blocked old tower keeps its support and resources");Checks.That(candidate.ensures==0&&candidate.streams==0&&candidate.LoadedFloorCount==0,"budget-blocked candidate does not allocate furnished floors");
  old.allowRelease=true;world.RefreshStreaming();Checks.That(candidate.streams==0,"deferred destruction prevents same-frame replacement");Time.frameCount++;world.RefreshStreaming();Checks.That(candidate.ensures==1&&candidate.streams==1,"next-frame admission recovers after old retirement");
  Console.WriteLine("PASS "+Checks.count+" actual world tower admission budget checks");
 }
}
'''

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity',type=Path,required=True)
    args=parser.parse_args()
    base=args.unity.resolve()/'Contents/Resources/Scripting/MonoBleedingEdge'
    mono=base/'bin/mono';compiler=base/'lib/mono/4.5/csc.exe'
    interior=(SOURCE/'SlopCityBuildingInterior.cs').read_text()
    world=(SOURCE/'SlopCityBuildingWorld.cs').read_text()
    floor_methods='\n'.join(block(interior,sig) for sig in ['public Transform EnsureFloor(','public void ReleaseFloor(','private bool ReleaseFailedFloor(','private void WarnFloorRetirement(','internal void NoteFloorRetirement(','private bool PrepareFloor(','internal void StreamAround(','internal bool ReleaseStream('])
    floor_methods+='\n'+next(line.strip() for line in interior.splitlines() if 'private void DeferFloorAdmission(' in line)
    floor_methods+='\n'+interior[interior.index('        internal bool CanAdmitFloorDetail('):interior.index('        private bool PrepareFloor(')]
    floor_methods+='\n'+interior[interior.index('        public bool IsFloorLoaded('):interior.index('        public Transform EnsureFloor(')]
    world_methods=block(world,'public void RefreshStreaming(')+'\n'+world[world.index('        internal bool CanAdmitTowerDetail('):world.index('        internal bool ShouldConsiderTowerDetail(')]
    common_floor=COMMON.replace('public readonly List<object> items=', 'public Floor owner;public int floor;public readonly List<object> items=')
    with tempfile.TemporaryDirectory(prefix='slop-floor-lifetimes-') as directory:
        for label,source in [('floor',common_floor+FLOOR.replace('// FLOOR_METHODS',floor_methods)),('world',COMMON+WORLD.replace('// WORLD_METHODS',world_methods))]:
            path=Path(directory)/(label+'.cs');target=Path(directory)/(label+'.exe');path.write_text(source)
            result=subprocess.run([str(mono),str(compiler),'-nologo','-nowarn:0169,0649,0414','-out:'+str(target),str(path)],capture_output=True,text=True)
            if result.returncode:raise RuntimeError(result.stdout+result.stderr)
            result=subprocess.run([str(mono),str(target)],capture_output=True,text=True)
            if result.returncode:raise RuntimeError(result.stdout+result.stderr)
            print(result.stdout.strip())
if __name__=='__main__':main()
