#!/usr/bin/env python3
"""Run actual browser target selection with deterministic physics/bounds substitutes.

This checks selection policy, not Unity physics or triangle-accurate occlusion.
Optional --source lets the preserved pre-repair source demonstrate the regression.
"""
from pathlib import Path
import argparse
import subprocess
import tempfile

from test_local_agents import block, MONO, MCS, SCRIPTS

STUBS = r'''
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public class Object { public static implicit operator bool(Object o)=>!ReferenceEquals(o,null); }
 public class GameObject { public bool activeInHierarchy=true; }
 public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;} public float magnitude=>1; public static Vector3 operator /(Vector3 v,float d)=>v; }
 public struct Matrix4x4 { public static Matrix4x4 identity=>new Matrix4x4(); public Vector3 MultiplyVector(Vector3 v)=>v; public Vector3 MultiplyPoint3x4(Vector3 v)=>v; }
 public struct Ray { public Vector3 origin,direction; public Ray(Vector3 a,Vector3 b){origin=a;direction=b;} public Vector3 GetPoint(float d)=>new Vector3(0,0,d); }
 public struct Bounds { public float distance; public Bounds(float d){distance=d;} public bool IntersectRay(Ray r,out float d){d=distance;return distance>=0;} }
 public class Transform:Object { public string name; public Vector3 position; public Matrix4x4 localToWorldMatrix,worldToLocalMatrix; public SlopCity.SlopCityInteractable interaction; public SlopCity.SlopCityDoor door; public Renderer renderer; public bool IsChildOf(Transform t)=>false; public T GetComponentInParent<T>() where T:class=>(interaction as T)??(door as T); public T GetComponent<T>() where T:class=>renderer as T; }
 public class Renderer:Object { public Transform transform=new Transform(); public GameObject gameObject=new GameObject(); public bool enabled=true; public Collider collider; public T GetComponent<T>() where T:class=>null; public T GetComponentInParent<T>() where T:class=>transform.GetComponentInParent<T>(); public bool TryGetComponent<T>(out T c) where T:class {c=collider as T;return c!=null;} }
 public class Collider:Object { public bool enabled=true,isTrigger; }
 public class TextMesh:Object {}
 public class Camera:Object { public bool orthographic; public Transform transform=new Transform(); public Ray ViewportPointToRay(Vector3 p)=>new Ray(transform.position,new Vector3(0,0,1)); }
 public struct RaycastHit { public Transform transform; public float distance; public Vector3 point; }
 public enum QueryTriggerInteraction { Ignore }
 public static class Time { public static int frameCount=1; public static float time=1; }
 public static class Physics { public static RaycastHit[] hits=new RaycastHit[0]; public static int RaycastNonAlloc(Ray r,RaycastHit[] target,float max,int mask,QueryTriggerInteraction q){Array.Copy(hits,target,hits.Length);return hits.Length;} public static RaycastHit[] RaycastAll(Ray r,float max,int mask,QueryTriggerInteraction q)=>hits; }
}
namespace SlopCity {
 using UnityEngine;
 public enum SlopCityInteractionKind { Inspect, Bench, Light, Bin, Collectible, Screen, Terminal, Vending, Car, GasPump, Building, Subway, Plant, Hydrant, Fountain, RareFind }
 public class SlopCityInteractable:Object { public SlopCityInteractionKind Kind; }
 public class SlopCityDoor:Object { public bool isActiveAndEnabled=true,IsOpen; }
 public class SlopCityCrowd:Object { public static SlopCityCrowd Instance; public Transform transform=new Transform(); public bool TryFindTarget(Ray r,float d,out int index,out Vector3 p){index=-1;p=default;return false;} }
 public class SlopCitySandbox {
  const float Reach=3.4f;
  struct Target { public Transform transform; public SlopCityInteractable interaction; public Renderer renderer; public Vector3 point; public bool isNpc; public int npcIndex; }
  Transform user=new Transform(); Camera activeCamera,webTargetCamera; Target aimed; Matrix4x4 webTargetTransform; Ray webTargetRay; int webTargetFrame=-1; float nextTargetAt;
  RaycastHit[] webTargetHits=new RaycastHit[64]; Renderer[] scenery; Bounds[] webSceneryBounds,webSceneryLocalBounds; bool[] webSceneryHasLocalBounds; List<int> webSceneryCandidates=new List<int>();
  void GatherWebScenery(Ray r,float d){}
  public string Select(RaycastHit[] hits,params Renderer[] renderers){Physics.hits=hits;scenery=renderers;webSceneryBounds=new Bounds[renderers.Length];webSceneryLocalBounds=new Bounds[renderers.Length];webSceneryHasLocalBounds=new bool[renderers.Length];for(int i=0;i<renderers.Length;i++){webSceneryCandidates.Add(i);webSceneryBounds[i]=webSceneryLocalBounds[i]=new Bounds(Distances[renderers[i]]);webSceneryHasLocalBounds[i]=true;}return FindBrowserTarget(new Camera()).transform?.name??"none";}
  static Dictionary<Renderer,float> Distances=new Dictionary<Renderer,float>();
  public static Renderer Decoration(string name,float distance,SlopCityInteractable owner=null,SlopCityDoor door=null){var r=new Renderer();r.transform.name=name;r.transform.interaction=owner;r.transform.door=door;Distances[r]=distance;return r;}
  public static RaycastHit Solid(string name,float distance,SlopCityInteractable owner=null,SlopCityDoor door=null){return new RaycastHit{transform=new Transform{name=name,interaction=owner,door=door},distance=distance};}
  // Actual production methods inserted below.
'''

CASES = r'''
 }
 public static class Check {
  static int checks;
  static SlopCityInteractable Action(SlopCityInteractionKind kind)=>new SlopCityInteractable{Kind=kind};
  static void Expect(string expected,RaycastHit[] hits,params Renderer[] renderers){checks++;string actual=new SlopCitySandbox().Select(hits,renderers);if(actual!=expected)throw new Exception("case "+checks+": expected "+expected+", got "+actual);}
  public static void Main(){
   var terminal=Action(SlopCityInteractionKind.Terminal);var inspect=Action(SlopCityInteractionKind.Inspect);var screen=Action(SlopCityInteractionKind.Screen);var other=Action(SlopCityInteractionKind.Terminal);
   var cabinet=SlopCitySandbox.Solid("cabinet",1.678919f,terminal);
   var hollow=SlopCitySandbox.Decoration("hollow military AABB",.173720f,inspect);
   var door=SlopCitySandbox.Decoration("same-owner door",1.647570f,terminal);
   Expect("same-owner door",new[]{cabinet},hollow,door);
   Expect("same-owner door",new[]{cabinet},door,hollow);
   Expect("cabinet",new[]{cabinet},hollow);
   Expect("cabinet",new[]{cabinet},SlopCitySandbox.Decoration("untagged empty AABB",.1f));
   Expect("screen",new[]{cabinet},hollow,SlopCitySandbox.Decoration("screen",1f,screen));
   Expect("nearer functional panel",new[]{cabinet},SlopCitySandbox.Decoration("nearer functional panel",1f,other));
   var wall=SlopCitySandbox.Solid("real solid wall",.5f);
   Expect("real solid wall",new[]{cabinet,wall},door,SlopCitySandbox.Decoration("screen behind wall",1f,screen));
   Expect("real solid wall",new[]{wall,cabinet},door);
   Expect("screen in front of wall",new[]{wall,cabinet},SlopCitySandbox.Decoration("screen in front of wall",.25f,screen));
   Expect("ordinary scenery",new RaycastHit[0],SlopCitySandbox.Decoration("ordinary scenery",1f,inspect));
   Expect("ordinary untagged scenery",new RaycastHit[0],SlopCitySandbox.Decoration("ordinary untagged scenery",1f));
   Expect("poster before inspected wall",new[]{SlopCitySandbox.Solid("inspected wall",2f,inspect)},SlopCitySandbox.Decoration("poster before inspected wall",1f,inspect));
   Expect("cabinet",new[]{cabinet},SlopCitySandbox.Decoration("tied screen",1.67890f,screen));
   Expect("none",new[]{SlopCitySandbox.Solid("out of reach",3.5f,terminal)},SlopCitySandbox.Decoration("far scenery",3.5f,inspect));
   var disabled=SlopCitySandbox.Decoration("disabled",.1f,screen);disabled.enabled=false;
   Expect("cabinet",new[]{cabinet},disabled);
   var colliderlessMiss=SlopCitySandbox.Decoration("own collider missed by exact ray",.1f,inspect);colliderlessMiss.collider=new Collider();
   Expect("cabinet",new[]{cabinet},colliderlessMiss);
   var physicalDoor=new SlopCityDoor();
   var closedLeaf=SlopCitySandbox.Solid("closed physical door leaf",1.8f,null,physicalDoor);
   Expect("closed physical door leaf",new[]{closedLeaf},hollow);
   Expect("closed physical door leaf",new[]{closedLeaf},SlopCitySandbox.Decoration("untagged hollow bounds",.1f));
   var genericParentDoor=SlopCitySandbox.Solid("physical door under generic parent",1.8f,inspect,physicalDoor);
   Expect("physical door under generic parent",new[]{genericParentDoor},hollow);
   Expect("physical door under generic parent",new[]{genericParentDoor},SlopCitySandbox.Decoration("same generic parent hollow mesh",.1f,inspect));
   var handle=SlopCitySandbox.Decoration("same physical door handle",1.75f,null,physicalDoor);
   Expect("same physical door handle",new[]{closedLeaf},hollow,handle);
   Expect("same physical door handle",new[]{closedLeaf},handle,hollow);
   physicalDoor.IsOpen=true;
   var openLeaf=SlopCitySandbox.Solid("open physical door leaf",1.2f,null,physicalDoor);
   Expect("open physical door leaf",new[]{openLeaf},hollow);
   Expect("open door handle",new[]{openLeaf},SlopCitySandbox.Decoration("open door handle",1.1f,null,physicalDoor),hollow);
   Expect("real solid wall",new[]{closedLeaf,wall},handle);
   Expect("real solid wall",new[]{wall,openLeaf},SlopCitySandbox.Decoration("open door behind wall",1.1f,null,physicalDoor));
   Expect("nearer screen before door",new[]{closedLeaf},hollow,SlopCitySandbox.Decoration("nearer screen before door",1f,screen));
   Expect("nearer screen before door",new[]{genericParentDoor},SlopCitySandbox.Decoration("nearer screen before door",1f,screen),hollow);
   physicalDoor.isActiveAndEnabled=false;
   Expect("hollow military AABB",new[]{closedLeaf},hollow);
   Console.WriteLine("PASS "+checks+" actual FindBrowserTarget policy cases; controlled physics/bounds substitutes, not physical occlusion QA");
  }
 }
}
'''


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, default=SCRIPTS / 'SlopCitySandbox.cs')
    args = parser.parse_args()
    source = args.source.read_text()
    methods = '\n'.join(block(source, signature) for signature in (
        '        private Target FindBrowserTarget(', '        private static bool BrowserRendererHit('))
    signature = '        private static bool BrowserDecorationCanReplaceSolidTarget('
    if signature in source:
        start = source.index(signature)
        opening = source.find('{', start)
        arrow = source.find('=>', start)
        methods += block(source, signature) if opening >= 0 and (arrow < 0 or opening < arrow) else source[start:source.index(';', start) + 1]
    with tempfile.TemporaryDirectory(prefix='slop-target-priority-') as directory:
        path = Path(directory)
        fixture = path / 'Fixture.cs'
        fixture.write_text(STUBS + methods + CASES)
        executable = path / 'Fixture.exe'
        subprocess.run([str(MONO), str(MCS), '-langversion:latest', '-out:' + str(executable), str(fixture)], check=True)
        subprocess.run([str(MONO), str(executable)], check=True)


if __name__ == '__main__':
    main()
