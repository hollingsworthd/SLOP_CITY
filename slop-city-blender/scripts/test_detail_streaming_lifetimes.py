#!/usr/bin/env python3
"""Fault-inject actual detail streaming lifetimes; models ownership, not device RAM.

Uses installed Unity Mono only. No Editor/player, download, or browser is started.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from test_native_allocation_rollback import block

SOURCE = Path(__file__).resolve().parents[1] / 'Unity/Assets/Scripts/SlopCityDetailStreaming.cs'
HARNESS = r'''
using System;using System.Collections;using System.Collections.Generic;using System.Reflection;using UnityEngine;
public static class Fault {
 public static readonly HashSet<string> Fail=new HashSet<string>();public static bool After;public static int Native,Warnings,DestroyQueued;public static int GeometryBytes=16;public static readonly List<UnityEngine.Object> Queue=new List<UnityEngine.Object>();
 public static void Before(string op){if(!After&&Fail.Remove(op))throw new InvalidOperationException(op);}
 public static void AfterCall(string op){if(After&&Fail.Remove(op))throw new InvalidOperationException(op);}
 public static void Arm(bool after,params string[] ops){Fail.Clear();After=after;foreach(var op in ops)Fail.Add(op);}
 public static void Frame(){foreach(var o in Queue)o.Free();Queue.Clear();Time.frameCount++;}
}
namespace UnityEngine {
 public class Object {
  bool dead,queued;protected Object(bool native=false){if(native)Fault.Native++;}public string name{set{Fault.Before("MeshName");Fault.AfterCall("MeshName");}}
  public void Free(){if(dead)return;dead=true;Fault.Native--;}
  public static implicit operator bool(Object o)=>!ReferenceEquals(o,null)&&!o.dead;
  public static bool operator ==(Object a,Object b)=>ReferenceEquals(a,b)||(!(bool)a&&!(bool)b);public static bool operator !=(Object a,Object b)=>!(a==b);
  public override bool Equals(object other)=>this==(other as Object);public override int GetHashCode()=>base.GetHashCode();
  public static void Destroy(Object o){if(!o)return;Fault.Before("Destroy");if(!o.queued){o.queued=true;Fault.Queue.Add(o);Fault.DestroyQueued++;}Fault.AfterCall("Destroy");}
  public static void DestroyImmediate(Object o){if(!o)return;Fault.Before("Destroy");o.Free();Fault.AfterCall("Destroy");}
 }
 public class AddComponentMenu:Attribute{public AddComponentMenu(string s){}}public class SerializeField:Attribute{}
 public class MonoBehaviour:Object{
  public Coroutine StartCoroutine(IEnumerator routine){var c=new Coroutine(routine);routine.MoveNext();return c;}
  public void StopCoroutine(Coroutine routine){Fault.Before("Stop");(routine.Routine as IDisposable)?.Dispose();Fault.AfterCall("Stop");}
 }
 public class Coroutine{public readonly IEnumerator Routine;public Coroutine(IEnumerator value){Routine=value;}}
 public class Mesh:Object{public Mesh():base(true){}
  static void Step(string key){Fault.Before(key);Fault.AfterCall(key);}
  public void SetVertexBufferParams(int n,UnityEngine.Rendering.VertexAttributeDescriptor[] d){Step("VertexParams");}public int GetVertexBufferStride(int stream){Step("Stride");return 12;}
  public void SetVertexBufferData(byte[] bytes,int a,int b,int c,int stream,UnityEngine.Rendering.MeshUpdateFlags flags){Step("VertexData");}
  public void SetIndexBufferParams(int n,UnityEngine.Rendering.IndexFormat f){Step("IndexParams");}public void SetIndexBufferData(byte[] bytes,int a,int b,int c,UnityEngine.Rendering.MeshUpdateFlags f){Step("IndexData");}
  public int subMeshCount{set{Step("SubCount");}}public void SetSubMesh(int n,UnityEngine.Rendering.SubMeshDescriptor d,UnityEngine.Rendering.MeshUpdateFlags f){Step("SubMesh");}
  public Bounds bounds{set{Step("Bounds");}}public void UploadMeshData(bool discard){Step("Upload");}
 }
 public class MeshRenderer:Object{}
 public class MeshFilter:Object{Mesh value;public Transform transform=new Transform();public Mesh sharedMesh{get=>value;set{Fault.Before("Restore");this.value=value;Fault.AfterCall("Restore");}}}
 public class Transform{public Vector3 position;public Matrix4x4 localToWorldMatrix;}
 public class Camera:Object{public static Camera main;public Transform transform=new Transform();}
 public struct Matrix4x4{public Vector3 MultiplyVector(Vector3 v)=>v;public Vector3 MultiplyPoint3x4(Vector3 v)=>v;}
 public struct Vector3{public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 operator *(Vector3 v,float n)=>new Vector3(v.x*n,v.y*n,v.z*n);}
 public struct Bounds{public Vector3 extents,center;public Bounds(Vector3 c,Vector3 size){center=c;extents=size*.5f;}public float SqrDistance(Vector3 point)=>0;}
 public static class Mathf{public static float Abs(float v)=>Math.Abs(v);public static float Min(float a,float b)=>Math.Min(a,b);public static float Pow(float a,float b)=>(float)Math.Pow(a,b);public static float Sqrt(float v)=>(float)Math.Sqrt(v);}
 public static class Time{public static float unscaledTime;public static int frameCount;}
 public static class Application{public static bool isPlaying;public static string streamingAssetsPath=>"https://offline.example/StreamingAssets";}
 public static class Debug{public static void LogWarning(string message,object owner){Fault.Warnings++;}}
}
namespace UnityEngine.Rendering{public struct VertexAttributeDescriptor{}public struct SubMeshDescriptor{}public enum IndexFormat{UInt16,UInt32}public enum MeshUpdateFlags{DontRecalculateBounds}}
namespace UnityEngine.Networking {
 public class DownloadHandlerScript:IDisposable{bool disposed;public DownloadHandlerScript(byte[] b){Fault.Native++;}public void Dispose(){Fault.Before("DownloadDispose");if(!disposed){disposed=true;Fault.Native--;}Fault.AfterCall("DownloadDispose");}protected virtual bool ReceiveData(byte[] d,int n)=>true;protected virtual void ReceiveContentLengthHeader(ulong n){}}
 public class UnityWebRequestAsyncOperation{public bool isDone;}
 public class UnityWebRequest:IDisposable{bool disposed;public const string kHttpVerbGET="GET";public enum Result{Success,Failure}public Result result;public bool disposeDownloadHandlerOnDispose;public DownloadHandlerScript downloadHandler;public int timeout;public UnityWebRequest(string url,string method){Fault.Native++;}public UnityWebRequestAsyncOperation SendWebRequest()=>new UnityWebRequestAsyncOperation();public void Abort(){Fault.Before("Abort");Fault.AfterCall("Abort");}public void Dispose(){Fault.Before("RequestDispose");if(!disposed){disposed=true;Fault.Native--;}Fault.AfterCall("RequestDispose");}}
}
namespace SlopCity{public static class SlopCityDetailMeshCodec{
 public const int MaximumPayloadBytes=4194304;public static bool ValidHash(string hash)=>hash?.Length==64;public static string ComputeHash(byte[] bytes)=>new string('a',64);
 private sealed class Layout{public int vertexCount=1,indexBytes=4,indexOffset=12,geometryBytes=Fault.GeometryBytes;public UnityEngine.Rendering.IndexFormat indexFormat;public UnityEngine.Rendering.VertexAttributeDescriptor[] attributes=new UnityEngine.Rendering.VertexAttributeDescriptor[1];public int[] strides={12},offsets={0},lengths={12};public UnityEngine.Rendering.SubMeshDescriptor[] submeshes=new UnityEngine.Rendering.SubMeshDescriptor[1];public Bounds bounds;}
 private static Layout Parse(byte[] bytes)=>new Layout();
 __DECODE__
}}

public static class Check {
 static int checks;static void Require(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static FieldInfo Field(string key)=>typeof(SlopCity.SlopCityDetailStreaming).GetField(key,BindingFlags.Instance|BindingFlags.NonPublic);
 static object Get(object owner,string key)=>Field(key).GetValue(owner);static void Set(object owner,string key,object value)=>Field(key).SetValue(owner,value);
 static object Call(object owner,string key,params object[] args){try{return typeof(SlopCity.SlopCityDetailStreaming).GetMethod(key,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,args);}catch(TargetInvocationException error){throw error.InnerException;}}
 static SlopCity.SlopCityDetailStreaming Fixture(out SlopCity.SlopCityDetailStreaming.DetailEntry[] entries,out IEnumerator pending){
  var owner=new SlopCity.SlopCityDetailStreaming();entries=new SlopCity.SlopCityDetailStreaming.DetailEntry[3];
  for(int i=0;i<3;i++){var proxy=new Mesh();entries[i]=new SlopCity.SlopCityDetailStreaming.DetailEntry{target=new MeshFilter{sharedMesh=proxy},renderer=new MeshRenderer(),farProxy=proxy,payloadBytes=16,geometryBytes=16,sha256=new string('a',64),fileName="detail-"+new string('a',64)+".mesh.unityweb"};}
  Set(owner,"entries",entries);owner.InitializeForTesting();
  for(int i=0;i<2;i++){int token=owner.BeginForTesting(i);Require(token>=0&&owner.CompleteForTesting(i,token,new byte[16]),"Fixture resident admission");}
  int lease=owner.BeginForTesting(2);pending=(IEnumerator)Call(owner,"Load",2,lease);Require(pending.MoveNext(),"Actual Load starts bounded request and handler");Set(owner,"pendingRoutine",new Coroutine(pending));return owner;
 }
 public static void Main(){
  foreach(bool playing in new[]{false,true})foreach(bool after in new[]{false,true})foreach(string failure in new[]{"Abort","Stop","RequestDispose","DownloadDispose","Restore","Destroy","all"})for(int cycle=0;cycle<30;cycle++){
   Fault.Arm(false);Application.isPlaying=playing;SlopCity.SlopCityDetailStreaming.DetailEntry[] entries;IEnumerator pending;var owner=Fixture(out entries,out pending);int baseline=3;
   Require(Fault.Native==baseline+4,"Two meshes, one native request, one handler");int staleToken=1;
   Fault.Arm(after,failure=="all"?new[]{"Abort","Stop","RequestDispose","DownloadDispose"}:new[]{failure});
   try{Call(owner,"OnDisable");}catch(Exception){}
   bool refusedDestroy=failure=="Destroy"&&!after;
   Require(owner.PendingIndex==-1,"Disable invalidates old admission even if cancellation throws");
   Require(owner.ResidentBytes==(failure=="Destroy"?16:0),"Every independent resident retires; refused destruction remains accounted: "+failure+" after="+after+" playing="+playing+" bytes="+owner.ResidentBytes);
#if UNITY_EDITOR
   if(playing)Require(owner.BeginForTesting(2)<0,"Deferred mesh destruction bars same-frame replacement");
#else
   Require(owner.BeginForTesting(2)<0,"Deferred mesh destruction bars same-frame replacement");
#endif
   Fault.Frame();
   Require(!owner.IsResident(1),"Failure on first resident cannot abandon later residents");
   if(refusedDestroy)Require(owner.IsResident(0),"Rejected native destruction retains exact owner for retry");
   // Retry cleanup without faults, then re-enable/admit a new generation. Any
   // retained old request/coroutine must retire before a new one is admitted.
   Fault.Arm(false);Call(owner,"OnEnable");Fault.Frame();Require(Fault.Native==baseline&&owner.ResidentBytes==0,"Retry retires all native resources exactly once");
   int token=owner.BeginForTesting(2);Require(token>staleToken,"Re-enabled owner admits fresh generation");
   Require(!owner.CompleteForTesting(2,staleToken,new byte[16]),"Old callback cannot bind or spend new generation budget");
   Require(owner.CompleteForTesting(2,token,new byte[16]),"Re-enabled owner successfully binds detail");
   Call(owner,"OnDisable");Call(owner,"OnDisable");Fault.Frame();Require(owner.ResidentBytes==0&&Fault.Native==baseline,"Repeated disable stays idempotent after recovery");
   Call(owner,"OnDestroy");Require(owner.BeginForTesting(2)<0,"Destroyed owner cannot admit another mesh");
   foreach(var entry in entries)UnityEngine.Object.DestroyImmediate(entry.farProxy);Require(Fault.Native==0,"Borrowed source proxies remain caller-owned and fixture releases");
  }
  {
   Fault.Arm(false);SlopCity.SlopCityDetailStreaming.DetailEntry[] entries;IEnumerator pending;var owner=Fixture(out entries,out pending);
   Fault.Arm(false,"Stop");Call(owner,"OnDisable");Fault.Frame();int live=Fault.Native,warnings=Fault.Warnings;
   for(int attempt=0;attempt<1000;attempt++){Fault.Arm(false,"Stop");Require(owner.BeginForTesting(2)<0,"A still-owned coroutine bars another request");Require(Fault.Native==live,"Refused cancellation cannot grow native requests or meshes");}
   Require(Fault.Warnings==warnings,"Repeated refused cleanup does not grow warning traffic");
   Fault.Arm(false);int token=owner.BeginForTesting(2);Require(token>=0&&owner.CompleteForTesting(2,token,new byte[16]),"Direct admission retries cleanup safely after cancellation recovers");
   Call(owner,"OnDestroy");Fault.Frame();foreach(var entry in entries)UnityEngine.Object.DestroyImmediate(entry.farProxy);Require(Fault.Native==0,"Persistent cancellation fixture releases");
  }
  foreach(bool after in new[]{false,true})foreach(string failure in new[]{"Restore","MeshName","VertexParams","Stride","VertexData","IndexParams","IndexData","SubCount","SubMesh","Bounds","Upload"})for(int cycle=0;cycle<30;cycle++){
   Fault.Arm(false);SlopCity.SlopCityDetailStreaming.DetailEntry[] entries;IEnumerator pending;var owner=Fixture(out entries,out pending);Call(owner,"OnDisable");Fault.Frame();int token=owner.BeginForTesting(2);
   Fault.Arm(after,failure,"Destroy");Require(!owner.CompleteForTesting(2,token,new byte[16]),"Decode/binding failure is rejected: "+failure);
   Require(owner.ResidentBytes==16,"Refused rollback destruction retains exact decoded geometry accounting: "+failure);
   Require(Get(owner,"leases")!=null,"Decoded owner remains reachable");Fault.Frame();Fault.Arm(false);Call(owner,"OnDestroy");Fault.Frame();Require(owner.ResidentBytes==0&&Fault.Native==3,"Destruction retry releases rejected decoded mesh: "+failure);
   foreach(var entry in entries)UnityEngine.Object.DestroyImmediate(entry.farProxy);Require(Fault.Native==0,"Rollback fixture leaves no native survivor");
  }
  {
   Fault.Arm(false);SlopCity.SlopCityDetailStreaming.DetailEntry[] entries;IEnumerator pending;var owner=Fixture(out entries,out pending);Call(owner,"OnDisable");Fault.Frame();int token=owner.BeginForTesting(2);int before=Fault.Native;Fault.GeometryBytes=32;
   Require(!owner.CompleteForTesting(2,token,new byte[16]),"Geometry/reservation mismatch rejected before allocation");Require(Fault.Native==before&&owner.ResidentBytes==0,"Invalid geometry cannot allocate outside its reserved bytes");Fault.GeometryBytes=16;Call(owner,"OnDestroy");foreach(var entry in entries)UnityEngine.Object.DestroyImmediate(entry.farProxy);Require(Fault.Native==0,"Mismatch fixture releases");
  }
  Console.WriteLine(checks+" actual detail ownership checks passed; deferred destruction is flushed explicitly, not treated as immediate native reclamation.");
 }
}
'''

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', type=Path, required=True)
    parser.add_argument('--source', type=Path, default=SOURCE)
    args = parser.parse_args()
    mono = args.unity.resolve() / 'Contents/Resources/Scripting/MonoBleedingEdge/bin/mono'
    compiler = mono.parents[1] / 'lib/mono/4.5/csc.exe'
    source = args.source.read_text()
    source = source.replace('using System.Collections.Generic;', 'using System.Collections.Generic;\nusing System.Reflection;')
    owner = source[:source.index('    /// <summary>Versioned exact vertex/index streams;')] + '}\n#endif\n'
    with tempfile.TemporaryDirectory(prefix='slop-detail-lifetimes-') as temp:
        path = Path(temp) / 'Check.cs'
        decode = block(source, 'internal static Mesh DecodeOwned(')
        path.write_text(owner + '\n' + HARNESS.replace('__DECODE__', decode).replace('using System;using System.Collections;using System.Collections.Generic;using System.Reflection;using UnityEngine;',''))
        for define in ['UNITY_EDITOR', 'UNITY_WEBGL']:
            target = Path(temp) / (define + '.exe')
            subprocess.run([str(mono), str(compiler), '-nologo', '-nowarn:0169,0414,0649', '-define:' + define, '-out:' + str(target), str(path)], check=True)
            subprocess.run([str(mono), str(target)], check=True)

if __name__ == '__main__':
    main()
