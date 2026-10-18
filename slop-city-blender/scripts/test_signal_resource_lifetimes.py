#!/usr/bin/env python3
"""Fault-inject actual signal resource setup and cleanup with Unity substitutes.

Models native identities and parenting, not native driver behavior/device RAM.
No Unity Editor/player or provider actions are used.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from test_native_allocation_rollback import block

SOURCE = Path(__file__).resolve().parents[1] / 'Unity/Assets/Scripts/SlopCitySignalBeams.cs'
HARNESS = r'''
using System;using System.Collections.Generic;using UnityEngine;using UnityEngine.Rendering;using static UnityEngine.Object;
public static class Fault {
 public static int Calls,Fail=-1,Live,Warnings;public static bool After;
 public static void Touch(){if(++Calls==Fail){Fail=-1;throw new InvalidOperationException("Injected native setup failure");}}
 public static void Set(Action mutation){if(After){mutation();Touch();}else{Touch();mutation();}}
 public static void Arm(int stage=-1){Calls=0;Fail=stage;}
}
namespace UnityEngine {
 public class Object {
  bool dead;protected Object(bool native=false){if(native){Fault.Touch();Fault.Live++;}}
  public static implicit operator bool(Object o)=>o!=null&&!o.dead;
  public string name{set{Fault.Set(()=>{});}}public HideFlags hideFlags{set{Fault.Set(()=>{});}}
  public static void Destroy(Object o){if(!o)return;if(o is GameObject g){foreach(var child in g.children.ToArray())Destroy(child);foreach(var component in g.components)Destroy(component);g.transform.parent?.gameObject.children.Remove(g);}o.dead=true;Fault.Live--;}
  public static void DestroyImmediate(Object o)=>Destroy(o);
 }
 public enum HideFlags{HideAndDontSave}public enum LineAlignment{View}
 public class Transform {public GameObject gameObject;public Transform parent;public void SetParent(Transform p,bool world){Fault.Set(()=>{parent=p;p.gameObject.children.Add(gameObject);});}}
 public class GameObject:Object{
  public Transform transform;public readonly List<GameObject> children=new List<GameObject>();public readonly List<Object> components=new List<Object>();
  public GameObject(string label):base(true){transform=new Transform{gameObject=this};}
  public int layer{set{Fault.Set(()=>{});}}
  public T AddComponent<T>() where T:LineRenderer,new(){T line=null;Fault.Set(()=>{line=new T();line.gameObject=this;components.Add(line);});return line;}
 }
 public class Shader:Object{public bool isSupported=true;public static Shader Find(string name){Fault.Touch();return new Shader();}}
 public class Material:Object{
  public Material(Shader shader):base(true){}public Color color{set{Fault.Set(()=>{});}}public int renderQueue{set{Fault.Set(()=>{});}}
  public bool HasProperty(string p)=>true;public void SetInt(string p,int v){Fault.Set(()=>{});}
 }
 public struct Color{public static Color white=>default;}
 public struct Vector3{}
 public class LineRenderer:Object{
  public GameObject gameObject;public LineRenderer():base(true){}
  public Material sharedMaterial{set{Fault.Set(()=>{});}}public bool useWorldSpace{set{Fault.Set(()=>{});}}
  public int positionCount{set{Fault.Set(()=>{});}}public float startWidth{set{Fault.Set(()=>{});}}public float endWidth{set{Fault.Set(()=>{});}}
  public int numCapVertices{set{Fault.Set(()=>{});}}public int numCornerVertices{set{Fault.Set(()=>{});}}
  public ShadowCastingMode shadowCastingMode{set{Fault.Set(()=>{});}}public bool receiveShadows{set{Fault.Set(()=>{});}}
  public LightProbeUsage lightProbeUsage{set{Fault.Set(()=>{});}}public ReflectionProbeUsage reflectionProbeUsage{set{Fault.Set(()=>{});}}
  public LineAlignment alignment{set{Fault.Set(()=>{});}}public bool enabled{set{Fault.Set(()=>{});}}
 }
 public static class Application{public static bool isPlaying;}
 public static class Debug{public static void LogWarning(string text,object owner){Fault.Warnings++;}}
}
namespace UnityEngine.Rendering{public enum CompareFunction{LessEqual}public enum CullMode{Off}public enum BlendMode{SrcAlpha,OneMinusSrcAlpha}public enum RenderQueue{Transparent}public enum ShadowCastingMode{Off}public enum LightProbeUsage{Off}public enum ReflectionProbeUsage{Off}}
class Signal {
 const int MaximumSegments=8;private readonly LineRenderer[] lines=new LineRenderer[MaximumSegments];private LineRenderer pulse;private Material material;
 private bool resourceAllocationFailed;private int count;private float totalLength;private Transform owner;
 private Transform transform=new GameObject("Signal fixture").transform;
 __CODE__
 static int checks;static void Check(bool pass,string why){checks++;if(!pass)throw new Exception(why);}
 private void Dispose(){Release();Destroy(transform.gameObject);}
 public static void Main(){
  foreach(bool playing in new[]{false,true}){
   Application.isPlaying=playing;
   var valid=new Signal();Fault.Arm();Check(valid.EnsureResources(),"Successful setup");int stages=Fault.Calls;
   Check(Fault.Live==20&&valid.transform.gameObject.children.Count==9,"Exactly one shared material and nine line objects/components");
   int calls=Fault.Calls;for(int i=0;i<1000;i++)Check(valid.EnsureResources(),"Warm setup is reusable");Check(Fault.Calls==calls,"Warm setup performs no native work");
   valid.OnDisable();Check(Fault.Live==1&&valid.transform.gameObject.children.Count==0,"Disable releases every line/material");valid.Dispose();Check(Fault.Live==0,"Success fixture releases");
   for(int stage=1;stage<=stages;stage++)foreach(bool after in new[]{false,true}){
    Fault.After=after;var failed=new Signal();Fault.Arm(stage);int warnings=Fault.Warnings;
    Check(!failed.EnsureResources(),"Injected setup stage fails safely: "+stage);
    Check(Fault.Live==1&&failed.transform.gameObject.children.Count==0,"No partial material, component or child retained at stage "+stage+" after="+after);
    calls=Fault.Calls;for(int i=0;i<100;i++)Check(!failed.EnsureResources(),"Failed callback does not retry allocations");
    Check(Fault.Calls==calls&&Fault.Warnings==warnings+1,"Failure warning and allocation occur once");
    if(after)failed.Configure(null);else failed.OnEnable();Fault.Arm();Check(failed.EnsureResources(),"Explicit re-enable recovers after failure");
    failed.OnDisable();Check(Fault.Live==1,"Recovered teardown");failed.Dispose();Check(Fault.Live==0,"Failure fixture releases");
   }
   var cleanup=new Signal();Fault.Arm();Check(cleanup.EnsureResources(),"Cleanup failure fixture ready");Fault.Arm(1);try{cleanup.OnDisable();}catch(InvalidOperationException){}Check(Fault.Live==1,"Renderer hide failure cannot skip finally cleanup");cleanup.Dispose();
   Console.WriteLine("Signal setup: "+stages+" failure stages × before/after mutation; bounded retry, nine-child ownership, explicit recovery, idempotent teardown pass (playing="+playing+").");
  }
  Console.WriteLine(checks+" actual C# signal ownership checks passed; device/native-driver behavior remains separate.");
 }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', type=Path, required=True)
    args = parser.parse_args()
    mono = args.unity.resolve() / 'Contents/Resources/Scripting/MonoBleedingEdge/bin/mono'
    compiler = mono.parents[1] / 'lib/mono/4.5/csc.exe'
    source = SOURCE.read_text()
    code = '\n'.join(block(source, name) for name in ['private bool EnsureResources(', 'private LineRenderer CreateLine(', 'public void Clear(', 'private void Release(', 'private static void DestroyOwned(', 'private void OnEnable(', 'private void OnDisable(', 'public void Configure('])
    assert '!EnsureResources()' in block(source, 'public void SetPath(')
    with tempfile.TemporaryDirectory(prefix='slop-signal-lifetimes-') as temp:
        path, target = Path(temp) / 'Check.cs', Path(temp) / 'Check.exe'
        path.write_text(HARNESS.replace('__CODE__', code))
        subprocess.run([str(mono), str(compiler), '-nologo', '-nowarn:0169,0414,0649', '-out:' + str(target), str(path)], check=True)
        subprocess.run([str(mono), str(target)], check=True)


if __name__ == '__main__':
    main()
