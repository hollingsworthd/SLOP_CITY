#!/usr/bin/env python3
"""Execute actual contact/job instruction lifetimes with managed Unity substitutes.
No Unity player, graphics, browser, device, save mutation or provider work.
"""
import argparse
import importlib.util
from pathlib import Path
import re
import subprocess
import sys
import tempfile
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('ownership',Path(__file__).with_name('test_native_allocation_rollback.py'))
own=importlib.util.module_from_spec(spec);spec.loader.exec_module(own)
STUBS=r'''
using System;using System.Collections.Generic;using UnityEngine;using Object=UnityEngine.Object;
namespace UnityEngine {
 public class Object {public static implicit operator bool(Object o)=>!ReferenceEquals(o,null);}
 public class Transform:Object {public Vector3 position;}
 public class Camera:Object {public Transform transform=new Transform();}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public float sqrMagnitude=>x*x+y*y+z*z;public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
 public struct Color {public Color(float r,float g,float b,float a){}}
 public static class Time {public static float time;}
 public static class Mathf {public static float Abs(float f)=>Math.Abs(f);public static float Atan2(float a,float b)=>(float)Math.Atan2(a,b);public static int RoundToInt(float f)=>(int)Math.Round(f);public const float Rad2Deg=57.29578f;}
}
public class SlopCityInteractable:Object {public string DisplayName;public Transform transform=new Transform();public Vector3 InteractionPoint=>transform.position;}
public class SlopCityDoor:Object {}
public class SlopCityFirstPerson:Object {public SlopCitySandbox Sandbox;public bool IsPaused,IsSeated;public Transform transform=new Transform();}
public class SlopCityCrowd:Object {public static SlopCityCrowd Instance=new SlopCityCrowd();public bool IsRedContact(int i)=>i==1;}
public class SlopCitySignalBeams:Object {public int paths,clears;public void SetPath(IReadOnlyList<Vector3> p,int i,bool b,Color c){paths++;}public void Clear(){clears++;}}
public class SlopCityHintRoute {public float DistanceFromPathSquared(Vector3 point)=>0;public int begins,steps;public bool Finished=true,ReachedTarget=true;public List<Vector3> Points=new List<Vector3>{new Vector3(),new Vector3(0,0,2)};public void Begin(Vector3 a,Vector3 b,Transform c,Transform d){begins++;}public void Step(){steps++;}}
public enum SlopCityJobAction {First,Second}
public class SlopCityJobDefinition {public string id="same-job",title="Repeatable job",outcome="Useful outcome";}
public static class SlopCityJobCatalog {public static SlopCityJobDefinition[] All={new SlopCityJobDefinition()};}
'''
SANDBOX=r'''
public class SlopCitySandbox:Object {
 __CODE__
 private readonly List<ContractStep> steps=new List<ContractStep>();private int stepIndex,contractNumber;private string currentJobId,currentJobOutcome,contractTitle,cargo,lastJobOutcome;
 private bool initialized=true;private Progress progress=new Progress();private Dictionary<string,int> jobHistory=new Dictionary<string,int>();
 private class Progress {public int dispatchCursor,completed;}
 public bool IsDriving,IsResting,IsTravelling,canDispatch=true;public SlopCityMatrixHunt MatrixHunt;public float WaypointDistance=40;
 public string CargoLabel=>cargo??"";public string CurrentJobTitle=>contractTitle;public string CurrentJobOutcome=>currentJobOutcome;
 public Vector3 WaypointPosition=>ObjectiveTarget?ObjectiveTarget.InteractionPoint:new Vector3();public string WaypointLabel=>ObjectiveTarget?ObjectiveTarget.DisplayName:"none";
 private static int AddBounded(int a,int b)=>a+b;private void Save(){}private void Say(string s){}private void CancelTravel(){}private void CommitUniqueSelection(SlopCityJobDefinition d){}private void RefreshSites(){}
 private List<ContractStep> BuildAssignment(SlopCityJobDefinition d)=>canDispatch?new List<ContractStep>{new ContractStep{site=new SlopCityInteractable{DisplayName="First device"},instruction="Take the component"},new ContractStep{site=new SlopCityInteractable{DisplayName="Second device"},instruction="Install the component"}}:null;
 private void NextContract(bool completed){var selected=BuildAssignment(SlopCityJobCatalog.All[0]);if(selected==null){steps.Clear();stepIndex=0;currentJobId=null;}else InstallAssignment(SlopCityJobCatalog.All[0],selected);}
 private int JobCompletions(string id)=>jobHistory.TryGetValue(id,out int value)?value:0;
 public void Advance()=>AdvanceContract();
 // These are the exact only step writes in the full production unique-phase refresher.
 private string phaseInstruction;private string Instruction(int i,object r)=>phaseInstruction;
 public void UniquePhase(SlopCityInteractable target,string instruction){phaseInstruction=instruction;int i=0;object r=null;__UNIQUE_WRITES__}
}
'''
HUNT=r'''
public class SlopCityMatrixHunt:Object {
 __CODE__
 private class Progress {public int contacts;}
 private Progress progress=new Progress();private HashSet<int> found=new HashSet<int>();private SlopCitySandbox instructionSandbox;private object instructionAssignment;
 public bool ready=true,isActiveAndEnabled=true;public SlopCityFirstPerson player;public SlopCitySignalBeams beams=new SlopCitySignalBeams();
 private float signalUntil,nextRoute,nextPresentation;private bool showingJobRoute;
 public string TransmissionText=>"Optional contact";public bool Ready=>ready;
 private void ChooseContact(){}private void Save(){}private static int Increment(int v)=>v+1;
}
'''
RABBIT=r'''
public class SlopCityRabbitTrail:Object {
 __CODE__
 private readonly SlopCityHintRoute route=new SlopCityHintRoute();private SlopCitySandbox sandbox;private SlopCityFirstPerson player;private Camera view=new Camera();private SlopCityInteractable target,plannedRouteTarget,levelEntrance;private SlopCityMatrixHunt signalOwner;private bool paused;
 private float nextRouteAt,nextTransmissionAt,transmissionUntil,nextBeamAt;private Vector3 plannedFrom,plannedTarget;
 private string district,landmark,transmission="Listening",hintedJobId,hintedAction;private object hintedAssignment;private bool instructionBeamActive;private int proximity=-1;private SlopCityDoor messageDoor;
 private void DescribeDestination(){district="Test block";landmark=target.DisplayName;}
 private void DrawTrail(Vector3 feet){signalOwner.PresentJobRoute(route.Points,0);}
 private SlopCityDoor NextDoor(Vector3 feet)=>null;
 private string ComposeTransmission(Vector3 feet,int band)=>"Physical signal / "+sandbox.ObjectiveAction;
 public void Bind(SlopCityFirstPerson p){player=p;sandbox=p.Sandbox;signalOwner=sandbox.MatrixHunt;}
 public void Tick()=>Update();public int RouteBegins=>route.begins;public int RouteSteps=>route.steps;
}
public class Text {public bool enabled;public string text;}
public class HudProbe {
 public Text transmission=new Text();public SlopCityRabbitTrail rabbitTrail;
 public void Render(SlopCitySandbox sandbox,bool paused){SlopCityMatrixHunt search=sandbox.MatrixHunt;__HUD__}
}
'''
CHECKS=r'''
public static class Checks {
 static int checks;static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 public static void Main(){
  var sandbox=new SlopCitySandbox();sandbox.SelectJob("same-job");var player=new SlopCityFirstPerson{Sandbox=sandbox};
  var hunt=new SlopCityMatrixHunt{player=player};sandbox.MatrixHunt=hunt;var rabbit=new SlopCityRabbitTrail();rabbit.Bind(player);var hud=new HudProbe{rabbitTrail=rabbit};
  Time.time=10;Check(!hunt.JobInstructionsVisible&&!hunt.SignalVisible,"No guidance before physical contact");
  Check(!hunt.TryContact(9,out _)&&!hunt.JobInstructionsVisible,"Ordinary residents cannot activate instructions");
  Check(hunt.TryContact(1,out _)&&hunt.JobInstructionsVisible&&hunt.SignalVisible,"Physical red contact binds the exact job");rabbit.Tick();hud.Render(sandbox,false);Check(hud.transmission.enabled,"Contact instruction is displayed");
  object identity=sandbox.CurrentJobAssignment;int beams=hunt.beams.paths;Check(beams>0,"Physical signal emits bounded route");
  Time.time=39.99f;Check(hunt.SignalVisible,"Beam remains visible before thirty seconds");
  Time.time=40;rabbit.Tick();hud.Render(sandbox,false);Check(!hunt.SignalVisible&&hunt.JobInstructionsVisible&&hud.transmission.enabled,"Instruction outlives exact thirty-second beam expiry");
  Check(hud.transmission.text.Contains("Take the component")&&!hud.transmission.text.Contains("beam"),"Expired-beam instructions retain actual current action without promising a beam");
  int begins=rabbit.RouteBegins,steps=rabbit.RouteSteps;
  Time.time=1000;rabbit.Tick();Check(rabbit.RouteBegins==begins&&rabbit.RouteSteps==steps&&hunt.beams.paths==beams,"Persistent instructions neither restart routes nor draw expired beams");
  sandbox.Advance();rabbit.Tick();hud.Render(sandbox,false);Check(ReferenceEquals(identity,sandbox.CurrentJobAssignment)&&hunt.JobInstructionsVisible,"Ordinary stage advance retains exact assignment identity");Check(hud.transmission.text.Contains("Install the component"),"Ordinary step refreshes the instruction immediately");
  for(int mechanic=0;mechanic<40;mechanic++)for(int phase=0;phase<7;phase++){
   sandbox.UniquePhase(new SlopCityInteractable{DisplayName="Phase device"},"Physical phase "+mechanic+"/"+phase);rabbit.Tick();hud.Render(sandbox,false);
   Check(ReferenceEquals(identity,sandbox.CurrentJobAssignment)&&hunt.JobInstructionsVisible&&hud.transmission.text.Contains("Physical phase "+mechanic+"/"+phase),"Unique phase writes preserve assignment and refresh target/action");
  }
  hud.Render(sandbox,true);Check(!hud.transmission.enabled,"Pause hides persistent text");hud.Render(sandbox,false);Check(hud.transmission.enabled&&hunt.JobInstructionsVisible,"Resume preserves same job instruction binding");
  sandbox.Advance();hud.Render(sandbox,false);Check(sandbox.CurrentJobId=="same-job"&&!ReferenceEquals(identity,sandbox.CurrentJobAssignment),"Actual completion can dispatch same catalog job with new step identity");Check(!hunt.JobInstructionsVisible&&!hud.transmission.enabled,"Completed job cannot transfer instructions to its same-ID successor");
  hunt.TryContact(1,out _);identity=sandbox.CurrentJobAssignment;Check(hunt.JobInstructionsVisible,"Known contact can explicitly bind successor");
  sandbox.SelectJob("same-job");Check(!ReferenceEquals(identity,sandbox.CurrentJobAssignment)&&!hunt.JobInstructionsVisible,"Explicit same-catalog replacement also clears instructions");hud.Render(sandbox,false);Check(!hud.transmission.enabled,"Still-live beam clock cannot reveal replacement job instructions");
  sandbox.canDispatch=false;sandbox.Advance();sandbox.Advance();hunt.TryContact(1,out _);Check(!hunt.JobInstructionsVisible,"No reachable assignment never retains a job binding");
  sandbox.canDispatch=true;sandbox.SelectJob("same-job");Check(!hunt.JobInstructionsVisible,"New job after empty dispatch requires another contact");hunt.TryContact(1,out _);hunt.ready=false;Check(!hunt.JobInstructionsVisible&&!hunt.SignalVisible,"Unready hunt hides both");hunt.ready=true;Check(!hunt.JobInstructionsVisible,"Expired binding never revives on readiness");
  Console.WriteLine("PASS "+checks+" actual contact/assignment/HUD/Rabbit instruction checks; thirty-second beams independent, ordinary stages/280unique-phase writes refresh, same-ID completion/replacement clear, pause preserved.");
 }
}
'''
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--unity',type=Path,required=True);args=parser.parse_args()
 files={name:(ROOT/'Unity/Assets/Scripts'/name).read_text() for name in ['SlopCitySandbox.cs','SlopCitySandbox.Jobs.cs','SlopCityMatrixHunt.cs','SlopCityRabbitTrail.cs','SlopCityHud.cs']}
 sandbox=files['SlopCitySandbox.cs'];hunt=files['SlopCityMatrixHunt.cs'];rabbit=files['SlopCityRabbitTrail.cs'];hud=files['SlopCityHud.cs']
 line=lambda source,needle:next(s.strip() for s in source.splitlines() if needle in s)
 block=own.block
 sandbox_code='\n'.join([block(sandbox,'private sealed class ContractStep'),line(sandbox,'public object CurrentJobAssignment'),line(sandbox,'public bool HasWaypoint'),line(sandbox,'public SlopCityInteractable ObjectiveTarget'),line(sandbox,'public string CurrentJobId'),line(sandbox,'public string ObjectiveAction')]+[block(sandbox,sig) for sig in ['private void InstallAssignment(','private void AdvanceContract(','public bool SelectJob(','public string DispatchContact(']])
 unique=block(files['SlopCitySandbox.Jobs.cs'],'private void RefreshUniqueWaypoint(')
 writes=re.findall(r'steps\[stepIndex\]\.(?:site|instruction)\s*=[^;]+;',unique)
 assert len(writes)==2 and not re.search(r'steps\.(?:Clear|Add|AddRange|Remove)',unique)
 hunt_code='\n'.join([line(hunt,'public const float SignalDurationSeconds'),line(hunt,'public bool SignalVisible'),block(hunt,'public bool JobInstructionsVisible')]+[block(hunt,sig) for sig in ['public bool TryContact(','public void PresentJobRoute(','public void ClearJobGuidance(','public void SuspendVisuals(']])
 rabbit_code='\n'.join([line(rabbit,'public string TransmissionText'),line(rabbit,'public float TransmissionOpacity'),line(rabbit,'public static int ProximityBand')]+[block(rabbit,sig) for sig in ['private void Update(','private void RefreshObjective(','private string ComposeJobInstructions(','public static string Bearing(','private void HideTrail(']])
 enabled=re.search(r'transmission.enabled =[^;]+;',hud)[0];text=re.search(r'transmission.text = !transmission.enabled[^;]+;',hud)[0]
 source=STUBS+SANDBOX.replace('__CODE__',sandbox_code).replace('__UNIQUE_WRITES__',''.join(writes))+HUNT.replace('__CODE__',hunt_code)+RABBIT.replace('__CODE__',rabbit_code).replace('__HUD__',enabled+text)+CHECKS
 base=args.unity.resolve()/'Contents/Resources/Scripting/MonoBleedingEdge';mono=base/'bin/mono';compiler=base/'lib/mono/4.5/csc.exe'
 with tempfile.TemporaryDirectory(prefix='slop-job-instructions-') as directory:
  path=Path(directory)/'Checks.cs';path.write_text(source)
  for mode,defines in [('native',[]),('webgl',['-define:UNITY_WEBGL'])]:
   target=Path(directory)/(mode+'.exe');subprocess.run([str(mono),str(compiler),'-nologo','-nowarn:0169,0649,0414','-out:'+str(target),*defines,str(path)],check=True);subprocess.run([str(mono),str(target)],check=True)
if __name__=='__main__':main()
