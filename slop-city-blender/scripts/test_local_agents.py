#!/usr/bin/env python3
"""Compile real local-inspection/log classes and actual Sandbox operation bodies.
Uses deterministic Unity substitutes: no editor, renderer, browser or network run.
Also compiles the full real Crowd source against the pinned Unity engine API.
"""
from pathlib import Path
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
MONO = ROOT.parent / '.tools/Unity-6000.3.26f1/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin/mono'
MCS = MONO.parents[1] / 'lib/mono/4.5/mcs.exe'
SCRIPTS = ROOT / 'Unity/Assets/Scripts'


def block(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 0
    for index in range(opening, len(source)):
        depth += (source[index] == '{') - (source[index] == '}')
        if depth == 0:
            return source[start:index + 1]
    raise AssertionError(signature)


STUBS = r'''
using System;
namespace UnityEngine {
 public class Object { public static implicit operator bool(Object o){return !ReferenceEquals(o,null);} }
 public struct Vector3 {public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;} public static Vector3 zero=>new Vector3(); public float sqrMagnitude=>x*x+y*y+z*z;public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
 public class Transform:Object {public Vector3 position,eulerAngles,localPosition;}
 public class Camera:Object {public Transform transform=new Transform();}
 public static class Time {public static float time=10;}
 public static class Mathf {public static int Max(int a,int b)=>Math.Max(a,b);public static float Max(float a,float b)=>Math.Max(a,b);public static int Min(int a,int b)=>Math.Min(a,b);}
}
namespace SlopCity {
 using UnityEngine;
 public struct SlopCityNpcSeed {public int route,node;public string displayName;}
 public class SlopCityCrowdNode {public int[] neighbors;}
 public class SlopCityCrowdRoute {public int districtIndex;public SlopCityCrowdNode[] nodes;}
 public enum SlopCityInteractionKind {Inspect,Bench,Light,Bin,Collectible,Screen,Terminal,Vending,Car,GasPump,Building,Subway,Plant,Hydrant,Fountain,RareFind}
 public class SlopCityInteractable:Object {
  public bool Supported=true,IsOn=true;public string DisplayName="Test junction";public Transform transform=new Transform(),Destination;public Vector3 InteractionPoint=>transform.position;
  public float CooldownRemaining;public int ServiceCount;public string ServiceStatus;public SlopCityInteractionKind Kind;public SlopCityElevatorButton button;
  public T GetComponent<T>() where T:class=>button as T;
  public void MarkUsed(float v){CooldownRemaining=v;}public void RecordService(string s){ServiceCount++;ServiceStatus=s;}public void ToggleLights(){IsOn=!IsOn;}
 }
 public class SlopCityElevator:Object {}
 public class SlopCityElevatorButton:Object {public string Activate(OperationHarness h){return "Elevator control acknowledged";}}
 public static class SlopCityJobCatalog {public static bool Supports(SlopCityInteractable i,SlopCityJobAction a)=>i&&i.Supported;}
 public class SlopCityCityMedia:Object {public static SlopCityCityMedia Instance;public string TuneScreen(Transform t)=>"Channel assignment requested";}
}
'''

HARNESS = r'''
using System;
using UnityEngine;
namespace SlopCity {
public class OperationHarness {
 private class Progress {public int salvage,water,recycling,fieldReadings;}
 private Progress progress=new Progress();
 private readonly SlopCityMunicipalLog municipalLog=new SlopCityMunicipalLog();
 private bool municipalOperationActive,persistenceUnavailable;private string municipalOperationMessage;
 private const string PersistenceNotice="Saving unavailable";
 private string cargo;private float boostUntil,transitRemaining,nextTargetAt;private string transitLabel;
 private Transform user=new Transform(),transitDestination;private SlopCityInteractable restingAt,pendingTravelStep,municipalTransitSite;
 private SlopCityElevator pendingElevator;private Camera activeCamera;private Vector3 savedCameraOffset;
 private Action<string> feedback;public Action<Vector3,float> TeleportRequested;public Action<bool> MovementOverrideRequested;
 public int Salvage=>progress.salvage;public int WaterSupply=>progress.water;public int Recycling=>progress.recycling;public int FieldReadings=>progress.fieldReadings;
 public bool IsTravelling=>transitRemaining>0;public bool ThrowSave;public int Saves;public SlopCityMunicipalLog Log=>municipalLog;
 private void Save(){Saves++;if(ThrowSave)throw new InvalidOperationException("injected");}
 private static string InspectDescription(SlopCityInteractable i)=>"Device inspected";
 private void ClearElevatorTravel(){pendingElevator=null;}
 public bool Invoke(SlopCityInteractable i,SlopCityJobAction a)=>ExecuteAction(i,a,null,false);
 public void SetParts(int p){progress.salvage=p;}public void SetWater(int p){progress.water=p;}
 public void SetReaderCount(int p){progress.fieldReadings=p;}
 public void SetCargo(string p){cargo=p;}
 // ACTUAL_METHODS
}
public static class Harness {
 static int checks;
 static void Check(bool v,string name){checks++;if(!v)throw new Exception(name);}
 static SlopCityMunicipalEntry Last(SlopCityMunicipalLog l){l.TryGetNewest(0,out var e);return e;}
 public static void Main(){
  var h=new OperationHarness();var device=new SlopCityInteractable();
  Check(!h.Invoke(device,SlopCityJobAction.Repair),"repair without parts is blocked");Check(Last(h.Log).outcome==SlopCityMunicipalOutcome.Blocked,"blocked outcome");Check(device.ServiceCount==0&&h.Saves==0,"blocked operation changes no device/save");
  h.SetParts(2);Check(h.Invoke(device,SlopCityJobAction.Repair),"actual repair core");var repair=Last(h.Log);Check(repair.before.parts==2&&repair.after.parts==1,"actual resource consumption");Check(repair.before.serviceCount==0&&repair.after.serviceCount==1,"authoritative service transition");
  device.CooldownRemaining=0;Check(h.Invoke(device,SlopCityJobAction.Diagnose),"real diagnostic action");Check(Last(h.Log).after.readings==1,"real reading added");Check(!h.Invoke(device,SlopCityJobAction.Diagnose),"cooldown rejects repeated reading");Check(Last(h.Log).outcome==SlopCityMunicipalOutcome.Blocked,"cooldown outcome");
  Check(h.Invoke(device,SlopCityJobAction.Tune),"tune handled");Check(Last(h.Log).outcome==SlopCityMunicipalOutcome.Requested,"tune never claims verified playback");
  Check(h.Invoke(device,SlopCityJobAction.FillWater),"fill action");Check(h.WaterSupply==3&&Last(h.Log).after.water==3,"actual water state");
  device.Kind=SlopCityInteractionKind.Plant;device.CooldownRemaining=0;Check(h.Invoke(device,SlopCityJobAction.WaterPlant),"watering action");Check(h.WaterSupply==2,"watering consumes water");
  device.Kind=SlopCityInteractionKind.Light;Check(h.Invoke(device,SlopCityJobAction.SwitchOff),"actual light action");Check(Last(h.Log).before.hasPower&&Last(h.Log).before.powerOn&&!Last(h.Log).after.powerOn,"logical light transition matches actual state");
  device.Supported=false;Check(!h.Invoke(device,SlopCityJobAction.Supply),"unsupported operation");Check(Last(h.Log).outcome==SlopCityMunicipalOutcome.Blocked,"unsupported log");device.Supported=true;
  h.ThrowSave=true;try{h.Invoke(device,SlopCityJobAction.FillWater);throw new Exception("exception swallowed");}catch(InvalidOperationException){}Check(Last(h.Log).outcome==SlopCityMunicipalOutcome.Failed,"throws remain visible and failure is recorded");h.ThrowSave=false;
  device.Destination=new Transform{position=new Vector3(10,0,0)};h.TeleportRequested=(p,y)=>{};Check(h.Invoke(device,SlopCityJobAction.Travel),"original callback return behavior retained");Check(Last(h.Log).outcome==SlopCityMunicipalOutcome.Blocked,"unmoved passenger cannot be logged arrived");
  device.Kind=SlopCityInteractionKind.Subway;device.DisplayName="RIDE TO MARKET";Check(!h.Invoke(device,SlopCityJobAction.Travel),"journey remains async");Check(Last(h.Log).outcome==SlopCityMunicipalOutcome.InProgress,"journey is in progress");h.CancelTravel();Check(Last(h.Log).outcome==SlopCityMunicipalOutcome.Incomplete,"cancel does not invent arrival");
  var log=new SlopCityMunicipalLog();for(int i=0;i<100;i++)log.Record("Player","Repair","Terminal",1,2,3,SlopCityMunicipalOutcome.Completed,new SlopCityMunicipalState{parts=2},new SlopCityMunicipalState{parts=1,serviceCount=1},"real state",i);
  Check(log.Count==32&&log.OldestSequence==69&&log.LatestSequence==100,"bounded journal rollover");Check(!log.TryGetNewest(32,out var missing),"out of range journal");Check(log.FormatRecent().Contains("#100")&&log.FormatRecent().Contains("#69 ")&&!log.FormatRecent().Contains("#68 "),"all retained entries inspectable");Check(log.FormatRecent().Contains("power n/a→n/a"),"nonpower devices never reported powered off");
  log.Record(new string('a',100),"A","D",0,0,0,SlopCityMunicipalOutcome.Observed,default,default,new string('e',1000),1);Check(Last(log).actor.Length==48&&Last(log).evidence.Length==220,"bounded strings");
  var agents=new SlopCityLocalAgent();var seeds=new[]{new SlopCityNpcSeed{route=0,node=0,displayName="unchanged"},new SlopCityNpcSeed{route=1,node=0},new SlopCityNpcSeed{route=2,node=0}};
  var routes=new[]{new SlopCityCrowdRoute{districtIndex=4,nodes=new[]{new SlopCityCrowdNode{neighbors=new[]{0}}}},new SlopCityCrowdRoute{districtIndex=5,nodes=new[]{new SlopCityCrowdNode{neighbors=new[]{0}}}},new SlopCityCrowdRoute{districtIndex=7,nodes=new[]{new SlopCityCrowdNode{neighbors=new[]{0}}}}};
  agents.Select(seeds,routes);for(int i=0;i<3;i++)Check(agents.SlotFor(i)==i,"existing resident selected");Check(seeds[0].displayName=="unchanged","saved population not mutated");Check(agents.SlotFor(-1)==-1,"invalid resident");
  for(int slot=0;slot<3;slot++){
   var journal=new SlopCityMunicipalLog();string start=agents.Inspect(slot,journal,default,1,2,3,0,"actual state");Check(start.Contains("Authored local")&&Last(journal).outcome==SlopCityMunicipalOutcome.Attempted,"honest optional task");
   agents.Inspect(slot,journal,default,1,2,3,1,"actual state");Check(Last(journal).outcome==SlopCityMunicipalOutcome.Incomplete,"no fabricated task success");
   journal.Record("Player",slot==0?"Diagnose":slot==1?"Tune":"Repair","Actual device",1,2,3,slot==1?SlopCityMunicipalOutcome.Requested:SlopCityMunicipalOutcome.Completed,new SlopCityMunicipalState{parts=2},new SlopCityMunicipalState{parts=1,serviceCount=1},"actual evidence",2);
   string report=agents.Inspect(slot,journal,default,1,2,3,3,"actual state");Check(Last(journal).outcome==SlopCityMunicipalOutcome.Observed,"task grounded in real operation");Check(report.Contains("Actual device")&&report.Contains("public repository files may describe a different revision"),"device evidence and source provenance");
   Check(slot!=1||report.Contains("acceptance and playback not verified"),"media observation has honest limit");
  }
  agents.Select(seeds,routes);var timeout=new SlopCityMunicipalLog();agents.Inspect(0,timeout,default,0,0,0,0,"");agents.Inspect(0,timeout,default,0,0,0,181,"");Check(Last(timeout).evidence.Contains("expired"),"bounded task lifetime");
  agents.Select(seeds,routes);var bad=new SlopCityMunicipalLog();agents.Inspect(2,bad,default,0,0,0,0,"");bad.Record("Player","Repair","Device",0,0,0,SlopCityMunicipalOutcome.Completed,new SlopCityMunicipalState{parts=2},new SlopCityMunicipalState{parts=2,serviceCount=1},"no consumption",1);agents.Inspect(2,bad,default,0,0,0,2,"");Check(Last(bad).outcome==SlopCityMunicipalOutcome.Incomplete,"method success alone cannot prove repair invariant");
  for(int i=0;i<40;i++)bad.Record("Player","Supply","Device",0,0,0,SlopCityMunicipalOutcome.Completed,default,default,"",3);agents.Inspect(2,bad,default,0,0,0,4,"");Check(Last(bad).evidence.Contains("rolled over"),"lost evidence is incomplete not false success");
  agents.Select(null,null);Check(agents.SlotFor(0)==-1,"no fabricated resident when population absent");
  agents.Select(seeds,routes);var late=new SlopCityMunicipalLog();agents.Inspect(0,late,default,0,0,0,0,"");late.Record("Player","Diagnose","Device",0,0,0,SlopCityMunicipalOutcome.Completed,default,new SlopCityMunicipalState{serviceCount=1},"",181);agents.Inspect(0,late,default,0,0,0,182,"");Check(Last(late).outcome==SlopCityMunicipalOutcome.Incomplete&&Last(late).evidence.Contains("expired"),"late operation cannot complete expired inspection");
  Console.WriteLine("PASS "+checks+" actual operation, ring and local inspection checks");
 }
}}
'''


def main():
    guids = []
    for filename in ('SlopCityLocalAgent.cs.meta', 'SlopCityMunicipalLog.cs.meta'):
        metadata = (SCRIPTS / filename).read_bytes()
        assert b'\n' in metadata and b'\\n' not in metadata, 'Metadata must contain actual newlines: ' + filename
        match = re.search(rb'^guid: ([0-9a-f]{32})$', metadata, re.MULTILINE)
        assert match, 'Unity GUID must contain exactly32 lowercase hex digits: ' + filename
        guids.append(match.group(1))
    assert len(set(guids)) == len(guids), 'Local source metadata GUIDs must be unique'
    for guid in guids:
        matches = sum(b'guid: ' + guid in path.read_bytes() for path in (ROOT / 'Unity/Assets').rglob('*.meta'))
        assert matches == 1, 'New source GUID must be unique throughout Assets'
    print('PASS source metadata actual newlines, exact32-hex GUIDs and Assets-wide uniqueness')
    sandbox = (SCRIPTS / 'SlopCitySandbox.cs').read_text()
    methods = '\n'.join(block(sandbox, signature) for signature in (
        '        private bool ExecuteAction(', '        private bool ExecuteActionCore(',
        '        private bool RecordReading(', '        private string Tune(',
        '        private void RecordMunicipal(', '        private void Say(',
        '        public void CancelTravel('))
    state_start = sandbox.index('        private SlopCityMunicipalState CaptureMunicipalState(')
    methods += sandbox[state_start:sandbox.index('        private void RecordMunicipal(', state_start)]
    enum = block((SCRIPTS / 'SlopCityJobCatalog.cs').read_text(), '    public enum SlopCityJobAction')
    crowd = (SCRIPTS / 'SlopCityCrowd.cs').read_text()
    assert 'localAgents.Select(population, routes)' in crowd
    assert 'localAgents.SlotFor' not in block(crowd, '        public void AdvanceSimulation(')
    assert 'LocalAgentReportRequested?.Invoke' in block(crowd, '        public bool TryTalk(')
    with tempfile.TemporaryDirectory(prefix='slop-local-agents-') as directory:
        work = Path(directory)
        (work / 'Stubs.cs').write_text(STUBS + '\nnamespace SlopCity {\n' + enum + '\n}\n')
        (work / 'Harness.cs').write_text(HARNESS.replace('// ACTUAL_METHODS', methods))
        sources = [SCRIPTS / 'SlopCityMunicipalLog.cs', SCRIPTS / 'SlopCityLocalAgent.cs', work / 'Stubs.cs', work / 'Harness.cs']
        subprocess.run([str(MONO), str(MCS), '-langversion:latest', '-out:' + str(work / 'Checks.exe'), *map(str, sources)], check=True)
        subprocess.run([str(MONO), str(work / 'Checks.exe')], check=True)
        (work / 'CrowdApiStubs.cs').write_text('''using System.Collections.Generic;using UnityEngine;namespace SlopCity {
public class SlopCityFirstPerson:MonoBehaviour {public bool IsPaused;public SlopCitySandbox Sandbox;public string CurrentDistrictName;}
public class SlopCitySandbox:MonoBehaviour {public SlopCityMunicipalLog MunicipalLog=new SlopCityMunicipalLog();public SlopCityMunicipalState MunicipalState;public int Salvage,WaterSupply,FieldReadings;}
public class SlopCityBuildingWorld:MonoBehaviour {public List<object> Buildings=new List<object>();public int ActiveFloorCount,ActiveElevatorCount;}
public class SlopCityCityMedia:MonoBehaviour {public static SlopCityCityMedia Instance;public int ScreenCount,DecoderLimit;public bool IsPaused;}
}''')
        contents = MONO.parents[4]
        references = list((contents / 'Resources/Scripting/Managed/UnityEngine').glob('*.dll'))
        references.append(contents / 'Resources/Scripting/MonoBleedingEdge/lib/mono/unityjit-macos/Facades/netstandard.dll')
        for label, defines in [('Native', []), ('Web', ['-define:UNITY_WEBGL'])]:
            subprocess.run([str(MONO), str(MCS), '-langversion:latest', '-target:library',
                            '-out:' + str(work / ('Crowd' + label + '.dll')), *defines,
                            *['-r:' + str(path) for path in references], str(work / 'CrowdApiStubs.cs'),
                            str(SCRIPTS / 'SlopCityCrowd.cs'), str(SCRIPTS / 'SlopCityLocalAgent.cs'),
                            str(SCRIPTS / 'SlopCityMunicipalLog.cs')], check=True)
        print('PASS full actual Crowd source compiles against pinned native/Web Unity API (game-context substitutes)')
        compiler = MONO.parents[1] / 'lib/mono/4.5/csc.exe'
        all_sources = list(SCRIPTS.rglob('*.cs'))
        for label, defines, managed in [
            ('Native', [], contents / 'Resources/Scripting/Managed/UnityEngine'),
            ('Web', ['-define:UNITY_WEBGL'], contents / 'PlaybackEngines/WebGLSupport/Variations/nondevelopment/Data/Managed')
        ]:
            refs = list(managed.glob('UnityEngine*.dll')) + [references[-1], ROOT / 'Unity/Library/ScriptAssemblies/UnityEngine.UI.dll']
            result = subprocess.run([str(MONO), str(compiler), '-nologo', '-langversion:preview', '-target:library',
                                     '-out:' + str(work / ('Game' + label + '.dll')), *defines,
                                     *['-r:' + str(path) for path in refs], *map(str, all_sources)],
                                    capture_output=True, text=True)
            if result.returncode:
                raise RuntimeError(result.stdout + result.stderr)
            print('PASS complete ' + label + ' API compilation: ' + str(len(all_sources))
                  + ' actual game sources; ' + str((result.stdout + result.stderr).count('warning CS'))
                  + ' existing serialized/unassigned-field warnings')


if __name__ == '__main__':
    main()
