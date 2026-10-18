#!/usr/bin/env python3
"""Run actual camera allocation methods against fault-injecting Unity substitutes.

This proves ownership/rollback, not device RAM or driver behavior. Requires the
installed Unity Mono compiler; it never installs tools or launches the Editor.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from test_native_allocation_rollback import block

SOURCE = Path(__file__).resolve().parents[1] / 'Unity/Assets/Scripts'
HARNESS = r'''
using System;
using UnityEngine;
using static UnityEngine.Object;
public static class Fault {
 public static int Calls, Fail=-1, Live; public static bool After, Refuse, FailRelease;
 public static void Touch(){Calls++;if(Calls==Fail){Fail=-1;throw new InvalidOperationException("Injected");}}
 public static void Set(){if(After){Touch();}else{Touch();}}
 public static void Reset(int n=-1){Calls=0;Fail=n;Refuse=FailRelease=false;}
}
namespace UnityEngine {
 public class Object {
  bool dead;public static implicit operator bool(Object value)=>!ReferenceEquals(value,null)&&!value.dead;
  protected Object(bool allocate=false){if(allocate){Fault.Touch();Fault.Live++;}}
  public string name{set{Fault.Set();}}
  public static void Destroy(Object value){if(value){value.dead=true;Fault.Live--;}}
  public static void DestroyImmediate(Object value)=>Destroy(value);
 }
 public class Shader:Object{}
 public enum HideFlags{HideAndDontSave}
 public enum FilterMode{Bilinear}
 public enum TextureWrapMode{Clamp}
 public enum RenderTextureFormat{ARGB32}
 public enum RenderTextureReadWrite{Default}
 public static class Application{public static bool isPlaying=true;}
 public static class Debug{public static void LogWarning(string message){}}
 public class Material:Object {
  public Material(Shader shader):base(true){}
  public HideFlags hideFlags{set{Fault.Set();}}
 }
 public class RenderTexture:Object {
  public RenderTexture(int width,int height,int depth,RenderTextureFormat format,RenderTextureReadWrite read):base(true){}
  public FilterMode filterMode{set{Fault.Set();}}
  public TextureWrapMode wrapMode{set{Fault.Set();}}
  public bool useMipMap{set{Fault.Set();}}
  public bool autoGenerateMips{set{Fault.Set();}}
  public int antiAliasing{set{Fault.Set();}}
  public bool Create(){Fault.Touch();return !Fault.Refuse;}
  public void Release(){if(Fault.FailRelease)throw new InvalidOperationException("Release failure");}
 }
}
public class Check {
__CODE__
 static void Assert(bool value,string message){if(!value)throw new Exception(message);}
 static void Exercise(Func<UnityEngine.Object> allocate,Action<UnityEngine.Object> release,string name){
  Fault.Reset();var success=allocate();int stages=Fault.Calls;
  Assert(Fault.Live==1,name+" owns exactly one native object");release(success);
  Assert(Fault.Live==0,name+" successful teardown");
  for(int stage=1;stage<=stages;stage++)for(int attempt=0;attempt<100;attempt++){
   Fault.Reset(stage);bool failed=false;try{allocate();}catch(InvalidOperationException){failed=true;}
   Assert(failed&&Fault.Live==0,name+" repeated allocation/setter failure leaked stage "+stage);
  }
  Fault.Reset();success=allocate();release(success);Assert(Fault.Live==0,name+" recovers after failures");
  Console.WriteLine(name+": "+stages+" fault stages, 100 attempts per stage, recovery/teardown passed");
 }
 public static void Main(){
  foreach(bool playing in new[]{false,true}){
   Application.isPlaying=playing;
   Exercise(()=>CreateFinishMaterial(new Shader()),value=>Destroy(value),"Finish material");
   Exercise(()=>CreateTarget(960,540),value=>DisposeTexture((RenderTexture)value),"World target");
   Fault.Reset();Fault.Refuse=true;bool failed=false;try{CreateTarget(960,540);}catch(InvalidOperationException){failed=true;}
   Assert(failed&&Fault.Live==0,"Create false rolls back target");
   Fault.Reset();var target=CreateTarget(960,540);Fault.FailRelease=true;DisposeTexture(target);DisposeTexture(target);
   Assert(Fault.Live==0,"Release failure still destroys exactly once");
  }
 }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mono', type=Path, required=True)
    args = parser.parse_args()
    mono = args.mono.expanduser().resolve()
    compiler = mono.parents[1] / 'lib/mono/4.5/csc.exe'
    if not mono.is_file() or not compiler.is_file():
        parser.error('Installed Unity Mono/Roslyn required.')
    presentation = (SOURCE / 'SlopCityWebPresentation.cs').read_text()
    finish = (SOURCE / 'SlopCityCameraFinish.cs').read_text()
    target = block(presentation, 'private void EnsureTarget(')
    assert 'candidate = CreateTarget(width, height);' in target
    assert 'finally\n            {\n                DisposeTexture(candidate);' in target
    assert 'material = CreateFinishMaterial(finishShader);' in finish
    code = '\n'.join([block(finish, 'private static Material CreateFinishMaterial('),
                      block(presentation, 'private static RenderTexture CreateTarget('),
                      block(presentation, 'private static void DisposeTexture(')])
    with tempfile.TemporaryDirectory(prefix='slop-camera-lifetime-') as temp:
        source, executable = Path(temp) / 'Check.cs', Path(temp) / 'Check.exe'
        source.write_text(HARNESS.replace('__CODE__', code))
        subprocess.run([str(mono), str(compiler), '/nologo', '/out:' + str(executable), str(source)], check=True)
        subprocess.run([str(mono), str(executable)], check=True)


if __name__ == '__main__':
    main()
