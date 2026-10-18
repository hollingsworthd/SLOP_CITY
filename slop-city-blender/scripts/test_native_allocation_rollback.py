#!/usr/bin/env python3
"""Inject deterministic failures into actual C# native-resource ownership methods.

Uses managed Unity API substitutes, never real OOM, Unity, graphics or providers.
Temporary Roslyn outputs stay outside the project. Real native/device QA is separate.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'Unity/Assets/Scripts'


def block(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == '{':
            depth += 1
        elif source[index] == '}':
            depth -= 1
            if depth == 0:
                return source[start:index + 1]
    raise AssertionError('Unclosed block: ' + signature)


STUBS = r'''
using System;using System.Collections.Generic;using UnityEngine;using static UnityEngine.Object;
public static class Fault {
 public static int Calls,FailAt=-1,Live,Destroyed,Warnings;public static bool After;
 public static void Touch(){Calls++;if(Calls==FailAt){FailAt=-1;throw new InvalidOperationException("Injected native API failure");}}
 public static void Arm(int at=-1,bool after=false){Calls=0;FailAt=at;After=after;}
 public static void Set<T>(ref T field,T value){if(After){field=value;Touch();}else{Touch();field=value;}}
}
namespace UnityEngine {
 public class Object {
  internal bool destroyed;private string label;private HideFlags flags;private static int nextId;private int id=++nextId;public int GetInstanceID()=>id;
  protected Object(bool native=false){if(native){Fault.Touch();Fault.Live++;}}
  public string name {get=>label;set=>Fault.Set(ref label,value);}
  public HideFlags hideFlags {get=>flags;set=>Fault.Set(ref flags,value);}
  public static implicit operator bool(Object o)=>!ReferenceEquals(o,null)&&!o.destroyed;
  public static void Destroy(Object o){if(!o)return;o.destroyed=true;Fault.Live--;Fault.Destroyed++;}
  public static void DestroyImmediate(Object o)=>Destroy(o);
  public static Mesh Instantiate(Mesh source)=>new Mesh{vertexCount=source.vertexCount,bounds=source.bounds};
 }
 public enum HideFlags{HideAndDontSave}
 public static class Application{public static bool isPlaying=true;}
 public static class Debug{public static void LogWarning(string message,object context){Fault.Warnings++;}}
 public class Shader:Object{public bool isSupported=true;public static Shader Find(string n)=>new Shader();}
 public static class Resources{public static T Load<T>(string name) where T:new()=>new T();}
 public static class SystemInfo{public static bool supportsInstancing=true;}
 public struct Color{public Color(float r,float g,float b){}}
 public struct Color32{public Color32(byte r,byte g,byte b,byte a){}}
 public struct Bounds{public Bounds(Vector3 center,Vector3 size){}}
 public struct Vector2{
  public float x,y;public Vector2(float a,float b){x=a;y=b;}
  public static Vector2 Lerp(Vector2 a,Vector2 b,float t)=>new Vector2(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t);
 }
 public struct Vector3{
  public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator *(Vector3 a,float f)=>new Vector3(a.x*f,a.y*f,a.z*f);
  public static Vector3 operator *(float f,Vector3 a)=>a*f;
  public static Vector3 operator -(Vector3 a)=>a*-1;
  public static Vector3 operator -(Vector3 a,Vector3 b)=>a+-b;
  public static Vector3 up=>new Vector3(0,1,0);public static Vector3 right=>new Vector3(1,0,0);public static Vector3 one=>new Vector3(1,1,1);
  public float sqrMagnitude=>x*x+y*y+z*z;public Vector3 normalized=>sqrMagnitude>0?this*(1/(float)Math.Sqrt(sqrMagnitude)):this;
  public void Normalize(){this=normalized;}
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
  public static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
  public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>new Vector3(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t);
 }
 public struct Vector4{public float x,y,z,w;public Vector4(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}}
 public struct Quaternion{public static Quaternion Euler(float x,float y,float z)=>default;}
 public struct Matrix4x4{public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s)=>default;public static Matrix4x4 Translate(Vector3 v)=>default;public static Matrix4x4 Rotate(Quaternion q)=>default;public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b)=>default;}
 public class MaterialPropertyBlock{public void SetVectorArray(string n,Vector4[] values){}}
 public static class Mathf{
  public const float PI=(float)Math.PI;public static float Cos(float n)=>(float)Math.Cos(n);public static float Sin(float n)=>(float)Math.Sin(n);
  public static float Sqrt(float n)=>(float)Math.Sqrt(n);public static float Max(float a,float b)=>Math.Max(a,b);
  public static float InverseLerp(float a,float b,float t)=>a==b?0:Clamp((t-a)/(b-a),0,1);
  public static float Round(float a)=>(float)Math.Round(a);public static float Abs(float a)=>Math.Abs(a);
  public static float Exp(float a)=>(float)Math.Exp(a);public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp(t,0,1);
  public static float Min(float a,float b)=>Math.Min(a,b);public static float Min(params float[] values){float r=values[0];foreach(float v in values)r=Min(r,v);return r;}
  public static int Min(int a,int b)=>Math.Min(a,b);public static int Max(int a,int b)=>Math.Max(a,b);
  public static float Clamp(float a,float b,float c)=>Max(b,Min(a,c));
  public static float SmoothStep(float a,float b,float t){t=Clamp(t,0,1);return a+(b-a)*t*t*(3-2*t);}
  public const float Deg2Rad=PI/180,Rad2Deg=180/PI;
  public static float Atan2(float a,float b)=>(float)Math.Atan2(a,b);public static float Acos(float a)=>(float)Math.Acos(a);
  public static float Repeat(float a,float b)=>a-(float)Math.Floor(a/b)*b;
  public static float LerpAngle(float a,float b,float t)=>Lerp(a,b,t);
 }
 public class Material:Object{
  private Color tint;private bool instancing;public Color color{get=>tint;set=>Fault.Set(ref tint,value);}public bool enableInstancing{get=>instancing;set=>Fault.Set(ref instancing,value);}
  private Shader activeShader;public Shader shader{get=>activeShader;set=>Fault.Set(ref activeShader,value);}
  public Material(Material source):base(true){}public Material(Shader shader):base(true){}
  public bool HasProperty(string n)=>true;public void SetFloat(string n,float value)=>Fault.Touch();
  public void SetTexture(string n,object value)=>Fault.Touch();public void DisableKeyword(string n)=>Fault.Touch();public void EnableKeyword(string n)=>Fault.Touch();
 }
 public class Mesh:Object{
  public int vertexCount=3;public bool isReadable=true;public Bounds bounds;
  private Color32[] colors;public Color32[] colors32{get=>colors;set=>Fault.Set(ref colors,value);}
  private Vector3[] positions=new Vector3[0],normalData=new Vector3[0];private Vector4[] tangentData=new Vector4[0];
  public Vector3[] vertices=>positions;public Vector3[] normals{get=>normalData;set=>Fault.Set(ref normalData,value);}
  public Vector4[] tangents{get=>tangentData;set=>Fault.Set(ref tangentData,value);}
  public Mesh():base(true){}public void MarkDynamic()=>Fault.Touch();
  public void RecalculateTangents(){Fault.Touch();tangentData=new Vector4[vertexCount];}
  public void SetVertices(List<Vector3> v){Fault.Touch();positions=v.ToArray();vertexCount=v.Count;}public void SetTriangles(List<int> i,int s,bool b=true)=>Fault.Touch();
  public void SetUVs(int s,List<Vector2> u)=>Fault.Touch();public void RecalculateNormals(){Fault.Touch();normalData=new Vector3[vertexCount];for(int i=0;i<vertexCount;i++)normalData[i]=Vector3.up;}
  public void RecalculateBounds()=>Fault.Touch();public void UploadMeshData(bool b)=>Fault.Touch();
 }
 public class MeshFilter:Object{private Mesh mesh;public Mesh sharedMesh{get=>mesh;set=>Fault.Set(ref mesh,value);}}
 public class MeshCollider:Object{
  private Mesh mesh;private bool active=true;
  public Mesh sharedMesh{get=>mesh;set=>Fault.Set(ref mesh,value);}public bool enabled{get=>active;set=>Fault.Set(ref active,value);}
 }
}
'''

CROWD = r'''
public class CrowdHarness {
 private static readonly int[] Sides={-1,1};
 private MatrixWardrobe wardrobe;private bool wardrobeAllocationFailed;private Material failedWardrobeSource;
 private class Visuals{public Material[] clothing;}private Visuals visuals;
 private object[] pendingParts=new object[2];private int pendingPartCount,lastPresentationFrame,PresentedCount,WholePeopleDeferred;
 private List<object> batches=new List<object>();private Dictionary<int,object> batchLookup=new Dictionary<int,object>();
__CODE__
 public static void Run(){
  Fault.Arm();var source=new Material(Shader.Find("test"));int baseline=Fault.Live;
  Fault.Arm();var success=new MatrixWardrobe(source);int stages=Fault.Calls;
  Check(Fault.Live==baseline+24,"A completed wardrobe owns nine materials and fifteen shared meshes including four coarse meshes");
  success.Dispose();success.Dispose();Check(Fault.Live==baseline,"Successful disposal is idempotent");
  for(int mode=0;mode<2;mode++)for(int stage=1;stage<=stages;stage++){
   Fault.Arm(stage,mode==1);bool failed=false;try{new MatrixWardrobe(source);}catch(InvalidOperationException){failed=true;}
   Check(failed&&Fault.Live==baseline,"Every constructor/mesh setter fault rolls back all native ownership");
  }
  var crowd=new CrowdHarness{visuals=new Visuals{clothing=new[]{source}}};Fault.Arm(5);int warnings=Fault.Warnings;
  Check(!crowd.EnsureWardrobe(),"A render allocation failure is caught");int calls=Fault.Calls;
  for(int i=0;i<1000;i++)Check(!crowd.EnsureWardrobe(),"Camera callbacks cannot repeat a failed allocation");
  Check(Fault.Calls==calls&&Fault.Warnings==warnings+1&&Fault.Live==baseline,"Repeated callbacks allocate/log nothing and leak nothing");
  crowd.ReleaseWardrobe();Fault.Arm();Check(crowd.EnsureWardrobe(),"Explicit lifecycle release allows recovery");crowd.ReleaseWardrobe();Check(Fault.Live==baseline,"Recovered wardrobe releases completely");
  Fault.Arm(3);Check(!crowd.EnsureWardrobe(),"Second injected failure latches");
  Fault.Arm();var replacement=new Material(Shader.Find("changed"));crowd.visuals.clothing[0]=replacement;
  Check(crowd.EnsureWardrobe(),"A genuinely changed source permits one fresh construction");crowd.ReleaseWardrobe();Destroy(replacement);Destroy(source);
  Check(Fault.Live==0,"Only authored materials survive until their explicit test-owner cleanup");
  Console.WriteLine("Wardrobe: "+stages+" failure stages in both mutation orders; callback suppression and recovery passed.");
 }
 public static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
'''

SALVAGE = r'''
public class SalvageHarness {
 private const long MaximumOwnedBytes=64*1024*1024;private static long ownedBytes;
 private Mesh source,working;private MeshFilter target;private MeshCollider collision;
 private Color32[] colors;private List<int> scratch;private bool[] removed;private byte[] clean;
 private int PartCount=>1;private int[] indices={0,1,2},fixedIndices=new int[0],starts={0},lengths={3};
 private long editBytes=128,workingReservationBytes,failedAdmissionOwnedBytes;
 private bool workingAllocationFailed,originalCollisionEnabled=true;private int indexRebuildCount;
__CODE__
 private static SalvageHarness Fixture(){
  Fault.Arm();var result=new SalvageHarness{source=new Mesh(),target=new MeshFilter(),collision=new MeshCollider()};
  result.target.sharedMesh=result.source;result.collision.sharedMesh=result.source;result.State();return result;
 }
 private static void Check(bool b,string message)=>CrowdHarness.Check(b,message);
 private void AssertUnpublished(){Check(working==null&&colors==null&&scratch==null&&workingReservationBytes==0,"Failed edit leaves no partial owned state");Check(target.sharedMesh==source&&collision.sharedMesh==source&&collision.enabled,"Failed external binding restores original mesh and collision");}
 private void Cleanup(){ReleaseWorking();Destroy(source);}
 public static void Run(){
  var success=Fixture();int baseline=Fault.Live;Fault.Arm();Check(success.EnsureWorking(),"Normal edit commits");int stages=Fault.Calls;
  Check(ownedBytes==128&&success.workingReservationBytes==128&&success.colors.Length==3&&success.scratch.Count==3,"Complete state owns one exact reservation");
  success.ReleaseWorking();success.ReleaseWorking();Check(ownedBytes==0&&Fault.Live==baseline,"Repeated cleanup releases the committed reservation exactly once");success.Cleanup();
  for(int mode=0;mode<2;mode++)for(int stage=1;stage<=stages;stage++){
   var item=Fixture();baseline=Fault.Live;Fault.Arm(stage,mode==1);Check(!item.EnsureWorking(),"Injected edit failure is rejected");
   item.AssertUnpublished();Check(ownedBytes==0&&Fault.Live==baseline,"Fault rollback balances reservation/native resource");
   int calls=Fault.Calls;Check(!item.EnsureWorking()&&Fault.Calls==calls,"Failure does not retry allocation each saved-state tick");
   Fault.Arm();item.OnEnable();Check(item.EnsureWorking(),"Explicit component recovery can retry");item.Cleanup();Check(ownedBytes==0&&Fault.Live==0,"Recovered edit releases all resources");
  }
  var external=Fixture();Fault.Arm();Check(external.EnsureWorking(),"External-destruction fixture commits");Destroy(external.working);external.ReleaseWorking();external.ReleaseWorking();
  Check(ownedBytes==0&&external.target.sharedMesh==external.source,"A destroyed working mesh still releases the recorded reservation once");external.Cleanup();
  var first=Fixture();var second=Fixture();Fault.Arm();Check(first.EnsureWorking(),"Other owner reserves budget");
  Fault.Arm(1);Check(!second.EnsureWorking(),"Failure observes currently occupied budget");
  Fault.Arm();Check(!second.EnsureWorking(),"Unchanged budget does not cause retries");first.ReleaseWorking();
  Check(second.EnsureWorking(),"A meaningful budget release permits a retry");first.Cleanup();second.Cleanup();
  var full=Fixture();ownedBytes=MaximumOwnedBytes;Fault.Arm();Check(!full.EnsureWorking()&&Fault.Calls==0,"Full global budget refuses before native allocation");ownedBytes=0;full.Cleanup();
  var saved=Fixture();Fault.Arm();Check(saved.EnsureWorking(false),"Saved-state staging commits a complete mesh before later rebuild");
  Check(saved.working&&saved.colors!=null&&saved.scratch!=null&&ownedBytes==128,"No-rebuild admission also reserves exactly once");saved.Cleanup();
  Check(ownedBytes==0&&Fault.Live==0,"All independent surface owners balance the budget");
  Console.WriteLine("Salvage: "+stages+" failure stages in both mutation orders; publication, recovery and exactly-once accounting passed.");
 }
}
public static class Entry{public static void Main(){CrowdHarness.Run();SalvageHarness.Run();}}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    runtime = parser.add_mutually_exclusive_group(required=True)
    runtime.add_argument('--mono', type=Path, help='Path to the installed Unity Mono executable.')
    runtime.add_argument('--unity', type=Path, help='Path to the installed Unity executable or Unity.app bundle.')
    args = parser.parse_args()
    if args.mono:
        mono = args.mono.expanduser().resolve()
    else:
        unity = args.unity.expanduser().resolve()
        if unity.name.endswith('.app'):
            contents = unity / 'Contents'
        elif unity.parent.name == 'MacOS':
            contents = unity.parent.parent
        else:
            contents = None
        mono = ((contents / 'Resources/Scripting/MonoBleedingEdge/bin/mono') if contents
                else (unity.parent / 'Data/MonoBleedingEdge/bin/mono'))
    compiler = mono.parents[1] / 'lib/mono/4.5/csc.exe'
    if not mono.is_file() or not compiler.is_file():
        parser.error('Installed Unity Mono/Roslyn required; supply --unity or --mono. No runtime is installed by this check.')
    crowd = (SOURCE / 'SlopCityCrowd.cs').read_text()
    crowd_code = '\n'.join(block(crowd, signature) for signature in [
        'private bool EnsureWardrobe(', 'private void ReleaseWardrobe(',
        'private sealed class MatrixWardrobe', 'private sealed class WardrobeMesh'])
    assert 'if (!EnsureWardrobe()) return;' in block(crowd, 'private void RenderCrowd(')
    surface = (SOURCE / 'SlopCitySalvageSurface.cs').read_text()
    surface_code = '\n'.join(block(surface, signature) for signature in [
        'private void State(', 'private void OnEnable(', 'private bool EnsureWorking(',
        'private void BuildIndices(', 'private void PublishCollision(',
        'private void RebuildIndices(', 'private void ReleaseWorking(', 'private static void Dispose('])
    assert 'ReleaseWorking();' in block(surface, 'private void OnDestroy(')
    assert 'ReleaseWardrobe();' in block(crowd, 'private void OnDisable(')
    assert 'ReleaseWardrobe();' in block(crowd, 'public void Configure(')
    code = STUBS + CROWD.replace('__CODE__', crowd_code) + SALVAGE.replace('__CODE__', surface_code)
    with tempfile.TemporaryDirectory(prefix='slop-city-allocation-rollback-') as directory:
        work = Path(directory)
        fixture, executable = work / 'Harness.cs', work / 'Rollback.exe'
        fixture.write_text(code)
        compiled = subprocess.run([str(mono), str(compiler), '-nologo', '-out:' + str(executable), str(fixture)], capture_output=True, text=True)
        if compiled.returncode:
            raise RuntimeError(compiled.stdout + compiled.stderr)
        result = subprocess.run([str(mono), str(executable)], capture_output=True, text=True)
        if result.returncode:
            raise RuntimeError(result.stdout + result.stderr)
        print(result.stdout.strip())
    print('Actual C# ownership methods passed injected managed faults; no Unity/native/device acceptance.')


if __name__ == '__main__':
    main()
