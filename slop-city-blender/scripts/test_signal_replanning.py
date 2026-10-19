#!/usr/bin/env python3
"""Exercise production signal presentation/replanning methods with deterministic routes.

The route substitute controls pending/success/failure; this is not Unity physics,
rendering, browser or physical-device acceptance.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile

from test_local_agents import block, MONO, MCS, SCRIPTS

STUBS = r'''
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public class Object { public static implicit operator bool(Object o)=>!ReferenceEquals(o,null); }
 public class MonoBehaviour:Object {public bool isActiveAndEnabled=true;}
 public struct Vector3 {
  public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;}
  public static Vector3 zero=>new Vector3(); public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z; public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b); public float sqrMagnitude=>x*x+y*y+z*z;
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
  public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a-b).sqrMagnitude);
 }
 public class Transform:Object {public Vector3 position;}
 public class Camera:Object {public Transform transform=new Transform();}
 public struct Color {public Color(float a,float b,float c,float d){}}
 public static class Time {public static float time;}
 public static class Mathf {public static float Abs(float v)=>Math.Abs(v);public static float Clamp01(float v)=>Math.Max(0,Math.Min(1,v));public static float Min(float a,float b)=>Math.Min(a,b);public static int CeilToInt(float v)=>(int)Math.Ceiling(v);}
}
namespace SlopCity {
 using UnityEngine;
 public class SlopCityDoor:Object {}
 public class SlopCityInteractable:Object {public Transform Destination,transform=new Transform();public Vector3 InteractionPoint;public string DisplayName="device";}
 public class SlopCityNpcSnapshot {public int districtIndex;}
 public class SlopCityCrowd:Object {public static SlopCityCrowd Instance=new SlopCityCrowd();public SlopCityNpcSnapshot Snapshot(int i)=>new SlopCityNpcSnapshot();}
 public class SlopCityFirstPerson:Object {public SlopCitySandbox Sandbox;public Transform transform=new Transform();public bool IsPaused,IsSeated;}
 public class SlopCitySandbox:Object {
  public bool HasWaypoint,IsDriving,IsResting,IsTravelling; public SlopCityMatrixHunt MatrixHunt;
  public Vector3 WaypointPosition;public float WaypointDistance=20;public SlopCityInteractable ObjectiveTarget=new SlopCityInteractable{InteractionPoint=new Vector3(25,0,0)};
  public string CurrentJobId="job",ObjectiveAction="Repair";public object CurrentJobAssignment=new object();
 }
 public class SlopCityHintRoute {
  public bool Finished,ReachedTarget,CompleteNext,Success=true;public int begins,steps;
  public List<Vector3> Points=new List<Vector3>();List<Vector3> path=>Points;Vector3 origin,target;
  // ROUTE_METHODS
  public void Begin(Vector3 from,Vector3 to,Transform player,Transform root){begins++;Finished=ReachedTarget=false;Points.Clear();origin=from;target=to;}
  public void Step(){steps++;if(!CompleteNext)return;CompleteNext=false;Finished=true;ReachedTarget=Success;if(Success){Points.Add(origin);Points.Add(target);}}
 }
 public class SlopCitySignalBeams:Object {
  public bool visible;public int clears,presents;
  public void Clear(){visible=false;clears++;}
  public void SetPath(IReadOnlyList<Vector3> points,int start,bool active,Color tint){visible=active&&points.Count>1;presents++;}
 }
 public class SlopCityMatrixHunt:MonoBehaviour {
  public enum SearchStage {Intercept,FindContact,Dine}
  public SearchStage Stage;public class Progress {public int target;}Progress progress=new Progress();
  public bool ready=true,showingJobRoute; public float signalUntil=30,nextRoute,nextPresentation;
  public Transform routeDestinationRoot,destinationRoot;public Vector3 routeOrigin,routeDestination,destination=new Vector3(25,0,0);public bool hasDestination=true;
  public SlopCityFirstPerson player;public SlopCitySignalBeams beams=new SlopCitySignalBeams();
  public SlopCityHintRoute route=new SlopCityHintRoute();public string clue;
  readonly List<SlopCityInteractable> levelAccess=new List<SlopCityInteractable>();
  SlopCitySandbox instructionSandbox;object instructionAssignment;
  public SlopCityMatrixHunt(){player=new SlopCityFirstPerson{Sandbox=new SlopCitySandbox()};player.Sandbox.MatrixHunt=this;Bind();}
  public void Bind(){instructionSandbox=player.Sandbox;instructionAssignment=player.Sandbox.CurrentJobAssignment;}
  void EnsureTarget(){} public bool TryDestination(out Vector3 point,out Transform root){point=destination;root=destinationRoot;return hasDestination;}
  string DistrictName(int i)=>"district";
  // MATRIX_METHODS
 }
 public class SlopCityRabbitTrail:MonoBehaviour {
  public SlopCityHintRoute route=new SlopCityHintRoute();public SlopCitySandbox sandbox;public SlopCityFirstPerson player;
  Camera view=new Camera(); public SlopCityInteractable target,plannedRouteTarget,levelEntrance;
  public SlopCityMatrixHunt signalOwner;public bool paused;
  float nextRouteAt,nextTransmissionAt,transmissionUntil,nextBeamAt;
  Vector3 plannedFrom,plannedTarget;string district,landmark,transmission,hintedJobId,hintedAction;object hintedAssignment;
  bool instructionBeamActive;int proximity=-1;SlopCityDoor messageDoor;
  public SlopCityRabbitTrail(){signalOwner=new SlopCityMatrixHunt();player=signalOwner.player;sandbox=player.Sandbox;sandbox.HasWaypoint=true;}
  void DescribeDestination(){}string ComposeJobInstructions(Vector3 feet,int band)=>"job";string ComposeTransmission(Vector3 feet,int band)=>"signal";
  SlopCityDoor NextDoor(Vector3 feet)=>null;int NearestPoint(Vector3 feet)=>0;
  public static int ProximityBand(float d)=>1;public static string Bearing(Vector3 a,Vector3 b)=>"north";
  public void Frame(){Update();}
  // RABBIT_METHODS
 }
 public static class Checks {
  static int checks;static void Check(bool v,string message){checks++;if(!v)throw new Exception(message);}
  static void TimeAt(float time){Time.time=time;}
  static SlopCityMatrixHunt Contact(){TimeAt(0);var h=new SlopCityMatrixHunt();h.beams.visible=true;return h;}
  static SlopCityRabbitTrail Job(){TimeAt(0);return new SlopCityRabbitTrail();}
  public static void Main(){
   var geometry=new SlopCityHintRoute();geometry.Finished=geometry.ReachedTarget=true;geometry.Points.Add(new Vector3(0,0,0));geometry.Points.Add(new Vector3(20,0,0));
   Check(geometry.DistanceFromPathSquared(new Vector3(15,0,3))==9,"deviation is measured from route segments, not route origin");
   Check(geometry.DistanceFromPathSquared(new Vector3(25,0,0))==25,"deviation beyond route end is bounded to segment endpoint");
   var h=Contact();h.Tick();Check(h.route.begins==1&&!h.beams.visible,"actual contact replan clears beam");
   h.route.CompleteNext=true;TimeAt(.3f);h.Tick();Check(h.beams.visible,"successful contact path presents beam");
   TimeAt(7);h.player.transform.position=new Vector3(12,0,0);h.destination=new Vector3(31,0,0);h.Tick();
   Check(h.route.begins==1&&h.beams.visible,"contact on-route travel and small target movement do not reroute");
   TimeAt(9);h.player.transform.position=new Vector3(12,0,9);h.Tick();Check(h.route.begins==2&&!h.beams.visible,"true contact route deviation clears and recalculates");
   TimeAt(9.3f);h.player.transform.position=new Vector3(30,0,30);h.Tick();Check(h.route.begins==2,"movement cannot repeatedly restart pending contact search");
   TimeAt(9.6f);h.route.CompleteNext=true;h.route.Success=false;h.Tick();Check(!h.beams.visible,"failed contact query has no guide");
   TimeAt(14);h.Tick();Check(h.route.begins==2,"contact failed query waits six seconds before retry");
   TimeAt(16);h.route.CompleteNext=true;h.route.Success=true;h.Tick();Check(h.route.begins==3&&h.beams.visible,"contact failed query retries after cooldown");
   h.player.IsPaused=true;h.Tick();Check(!h.beams.visible,"pause clears contact guide");h.player.IsPaused=false;
   TimeAt(30);h.beams.visible=true;h.Tick();Check(!h.beams.visible,"expiry clears contact guide");
   h=Contact();h.hasDestination=false;h.Tick();Check(!h.beams.visible,"missing contact destination clears guide");
   h=Contact();h.Tick();h.route.CompleteNext=true;TimeAt(.3f);h.Tick();h.destination=new Vector3(34,0,0);TimeAt(7);h.Tick();Check(h.route.begins==2&&!h.beams.visible,"large contact target displacement triggers actual replan and clear");
   h=Contact();h.destinationRoot=new Transform();h.Tick();h.route.CompleteNext=true;TimeAt(.3f);h.Tick();
   h.destinationRoot=new Transform();TimeAt(.4f);h.Tick();Check(h.route.begins==2&&!h.beams.visible,"contact entrance identity change clears and replans before cadence");
   h.destinationRoot=new Transform();TimeAt(.5f);h.Tick();Check(h.route.begins==3&&!h.beams.visible,"contact entrance identity change invalidates even a pending query");
   var r=Job();r.Frame();r.route.CompleteNext=true;TimeAt(.3f);r.Frame();Check(r.signalOwner.beams.visible,"successful job route is presented");
   r.player.transform.position=new Vector3(15,0,0);r.sandbox.ObjectiveTarget.InteractionPoint=new Vector3(29,0,0);TimeAt(7);r.Frame();
   Check(r.route.begins==1&&r.signalOwner.beams.visible,"on-route job travel, small target motion and old timer do not reroute");
   r.player.transform.position=new Vector3(15,0,9);TimeAt(9);r.Frame();Check(r.route.begins==2&&!r.signalOwner.beams.visible,"true job route deviation clears before new query");
   r.player.transform.position=new Vector3(30,0,30);TimeAt(9.3f);r.Frame();Check(r.route.begins==2,"movement cannot repeatedly restart pending job query");
   r.route.CompleteNext=true;r.route.Success=false;TimeAt(9.6f);r.Frame();Check(!r.signalOwner.beams.visible,"failed job query does not display old guide");
   TimeAt(14);r.Frame();Check(r.route.begins==2,"job failed query waits six seconds before retry");TimeAt(16);r.Frame();Check(r.route.begins==3,"job failed query retries after cooldown");
   r=Job();r.Frame();r.route.CompleteNext=true;TimeAt(.3f);r.Frame();r.sandbox.ObjectiveTarget.InteractionPoint=new Vector3(34,0,0);TimeAt(7);r.Frame();Check(r.route.begins==2&&!r.signalOwner.beams.visible,"large job target displacement clears and recalculates");
   r=Job();r.Frame();r.route.CompleteNext=true;TimeAt(.3f);r.Frame();r.sandbox.ObjectiveTarget=new SlopCityInteractable();TimeAt(.6f);r.Frame();Check(!r.signalOwner.beams.visible&&r.route.begins==2,"new objective clears old guide immediately");
   r=Job();r.Frame();r.route.CompleteNext=true;TimeAt(.3f);r.Frame();r.sandbox.HasWaypoint=false;r.Frame();Check(!r.signalOwner.beams.visible,"completion clears job guide");
   r=Job();r.Frame();r.route.CompleteNext=true;TimeAt(.3f);r.Frame();r.sandbox.CurrentJobAssignment=new object();r.Frame();Check(!r.signalOwner.beams.visible,"new assignment cannot inherit previous contact guide");
   r=Job();r.Frame();r.route.CompleteNext=true;TimeAt(.3f);r.Frame();r.paused=true;r.Frame();Check(!r.signalOwner.beams.visible,"pause clears job guide");
   r=Job();r.Frame();r.route.CompleteNext=true;TimeAt(.3f);r.Frame();TimeAt(30);r.Frame();Check(!r.signalOwner.beams.visible,"expiry clears job guide");
   Console.WriteLine("PASS "+checks+" production signal replan/presentation checks; deterministic substitutes only");
  }
 }
}
'''

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--scripts-dir',type=Path,default=SCRIPTS)
    parser.add_argument('--mono',type=Path,default=MONO)
    parser.add_argument('--mcs',type=Path,default=MCS)
    args=parser.parse_args()
    hunt=(args.scripts_dir/'SlopCityMatrixHunt.cs').read_text()
    rabbit=(args.scripts_dir/'SlopCityRabbitTrail.cs').read_text()
    route=(args.scripts_dir/'SlopCityHintRoute.cs').read_text()
    matrix='\n'.join(block(hunt,sig) for sig in ['        public bool JobInstructionsVisible','        public void Tick()','        public void PresentJobRoute(','        public void ClearJobGuidance()'])
    matrix+='\n'+next(line.strip() for line in hunt.splitlines() if 'public bool SignalVisible =>' in line)
    trail='\n'.join(block(rabbit,sig) for sig in ['        private void Update()','        private void RefreshObjective()','        private void DrawTrail(','        private void HideTrail()'])
    fixture=STUBS.replace('// MATRIX_METHODS',matrix).replace('// RABBIT_METHODS',trail).replace('// ROUTE_METHODS',block(route,'        public float DistanceFromPathSquared('))
    with tempfile.TemporaryDirectory(prefix='slop-signal-replan-') as directory:
        source=Path(directory)/'Checks.cs';source.write_text(fixture)
        exe=Path(directory)/'Checks.exe'
        subprocess.run([str(args.mono),str(args.mcs),'-langversion:latest','-out:'+str(exe),str(source)],check=True)
        subprocess.run([str(args.mono),str(exe)],check=True)

if __name__=='__main__':
    main()
