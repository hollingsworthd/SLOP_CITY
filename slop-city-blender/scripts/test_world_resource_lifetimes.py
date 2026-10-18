#!/usr/bin/env python3
"""Fault-test actual world resource methods without launching Unity or inducing OOM.

Managed substitutes inject failures before/after mutation and owner-list insertion.
This checks ownership, not native memory use, driver behavior or phone acceptance.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from test_native_allocation_rollback import STUBS, block

SOURCE = Path(__file__).resolve().parents[1] / 'Unity/Assets/Scripts'

EXTRA = r'''
public class FaultList<T>:List<T>{public new void Add(T value){if(Fault.After){base.Add(value);Fault.Touch();}else{Fault.Touch();base.Add(value);}}}
public class FaultDictionary<K,V>:Dictionary<K,V>{public new void Add(K key,V value){if(Fault.After){base.Add(key,value);Fault.Touch();}else{Fault.Touch();base.Add(key,value);}}}
namespace UnityEngine.Rendering {public enum IndexFormat{UInt16,UInt32}public enum ShadowCastingMode{Off,On}}
namespace UnityEngine {
 public struct CombineInstance{}
 public static class Time{public static float unscaledTime;}
 public class Renderer:Object{private Material material;public Material sharedMaterial{get=>material;set=>Fault.Set(ref material,value);}public bool forceRenderingOff;public bool isPartOfStaticBatch;public ShadowCastingMode shadowCastingMode;public bool receiveShadows;}
 public class MeshRenderer:Renderer{}
 public class Transform:Object{public Matrix4x4 worldToLocalMatrix,localToWorldMatrix;public GameObject gameObject;public Transform parent;public List<Transform> children=new List<Transform>();Vector3 point;public Vector3 localPosition{get=>point;set=>Fault.Set(ref point,value);}public void SetParent(Transform value,bool keep){Fault.Touch();parent=value;value.children.Add(this);}public Vector3 InverseTransformPoint(Vector3 value)=>value;}
 public class GameObject:Object{
  public Transform transform;public bool isStatic;public GameObject(string name=""):base(true){transform=new Transform{gameObject=this};}
  public T AddComponent<T>() where T:new(){Fault.Touch();return new T();}
 }
 public class Coroutine{public System.Collections.IEnumerator routine;}
}
public static class SlopCityInteriorGeometry{__ROOT__}
namespace UnityEngine.Networking{
 public class DownloadHandlerScript:UnityEngine.Object,IDisposable{
  public int Disposals;public DownloadHandlerScript(byte[] buffer):base(true){}
  protected virtual bool ReceiveData(byte[] data,int length)=>true;protected virtual void ReceiveContentLengthHeader(ulong length){}
  public void Feed(){ReceiveData(new byte[64],64);}public void Dispose(){Disposals++;Destroy(this);}
 }
 public class UnityWebRequestAsyncOperation{public static bool Done=true;public bool isDone{get{Fault.Touch();return Done;}}}
 public class UnityWebRequest:UnityEngine.Object,IDisposable{
  public const string kHttpVerbGET="GET";public enum Result{Success,ConnectionError}
  bool dispose=true;DownloadHandlerScript handler;int limit;public int Disposals;
  public bool disposeDownloadHandlerOnDispose{get=>dispose;set=>Fault.Set(ref dispose,value);}
  public DownloadHandlerScript downloadHandler{get=>handler;set=>Fault.Set(ref handler,value);}
  public int timeout{get=>limit;set=>Fault.Set(ref limit,value);}
  public Result result{get{Fault.Touch();return Result.Success;}}
  public UnityWebRequest(string url,string method):base(true){}
  public UnityWebRequestAsyncOperation SendWebRequest(){Fault.Touch();handler.Feed();return new UnityWebRequestAsyncOperation();}
  public void Abort(){Fault.Touch();}public void Dispose(){Disposals++;if(dispose&&handler!=null)handler.Dispose();Destroy(this);}
 }
}
public static class Assert {
 public static void That(bool value,string message){if(!value)throw new Exception(message+" (live="+Fault.Live+", stage="+Fault.Calls+")");}
 public static void Sweep(string label,Action action){
  Fault.Arm();action();int stages=Fault.Calls;That(Fault.Live==0,label+" success cleanup");
  for(int mode=0;mode<2;mode++)for(int stage=1;stage<=stages;stage++)for(int repeat=0;repeat<3;repeat++){
   Fault.Arm(stage,mode==1);try{action();}catch(InvalidOperationException){}
   That(Fault.Live==0,label+" failed cleanup "+stage+" mode "+mode);
  }
  Fault.Arm();action();That(Fault.Live==0,label+" recovery");
  Console.WriteLine(label+": "+stages+" fault stages x both mutation orders x3, recovery passed");
 }
}
'''

STEAK = r'''
class Steak {
 const int BitesPerMeal=3;
 static void Release(Object value){Destroy(value);}
 __CODE__
 public static void Run(){Assert.Sweep("Shared meal geometry",()=>{Geometry item=null;try{item=new Geometry();}finally{if(item!=null){item.Dispose();item.Dispose();}}});}
}
'''
SALVAGE = r'''
class Surface {
 private sealed class Finish {public Material material;public int users;public bool owned;}
 static FaultDictionary<Material,Finish> Finishes=new FaultDictionary<Material,Finish>();
 Material authoredMaterial,polishMaterial;Renderer display;MeshCollider collision;bool materialBound,originalForceRenderingOff,pendingSavedState,originalCollisionEnabled;
 static void Dispose(Object item)=>Destroy(item);void ReleaseWorking(){}
 __CODE__
 public static void Run(){
  Assert.Sweep("Salvage finish binding",()=>{
   int stage=Fault.FailAt;bool after=Fault.After;Fault.Arm();
   var source=new Material(new Shader());var display=new Renderer();display.sharedMaterial=source;
   var item=new Surface{authoredMaterial=source,display=display};Fault.Arm(stage,after);
   try{item.BindMaterial();}finally{Fault.FailAt=-1;item.OnDestroy();Assert.That(Finishes.Count==0,"Failed or retired binding leaves no cached finish");Destroy(source);}
  });
  Fault.Arm();var authored=new Material(new Shader());var first=new Surface{authoredMaterial=authored,display=new Renderer()};var second=new Surface{authoredMaterial=authored,display=new Renderer()};
  first.BindMaterial();second.BindMaterial();Assert.That(Finishes.Count==1&&Fault.Live==2,"Shared finish reused");first.OnDestroy();Assert.That(Fault.Live==2,"Other live user retains finish");second.OnDestroy();second.OnDestroy();Destroy(authored);Assert.That(Fault.Live==0,"Last user disposes once");
 }
}
'''
FURNISH = r'''
class Furnishing {
 Dictionary<Material,List<CombineInstance>> groups=new Dictionary<Material,List<CombineInstance>>();
 FaultList<Mesh> meshes=new FaultList<Mesh>();List<GameObject> cleanProps=new List<GameObject>();Dictionary<string,int> propOrdinals=new Dictionary<string,int>();Transform cleanProp,transform;string cleanLabel;
 __CODE__
 public static void Run(){Assert.Sweep("Streamed furnishing",()=>{
  int stage=Fault.FailAt;bool after=Fault.After;Fault.Arm();var root=new GameObject();var material=new Material(new Shader());
  var item=new Furnishing{transform=root.transform};item.groups.Add(material,new List<CombineInstance>{new CombineInstance()});Fault.Arm(stage,after);
  try{item.Complete();}finally{Fault.FailAt=-1;item.Release();item.Release();Destroy(root);Destroy(material);}
 });}
}
'''
INSTALL = r'''
class __CLASS__ {
 List<Vector3> vertices=new List<Vector3>{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,1,0)};
 List<Vector3> normals=new List<Vector3>{Vector3.up,Vector3.up,Vector3.up};List<Vector2> uv=new List<Vector2>();List<int> triangles=new List<int>{0,1,2};
 static void DisposeMesh(Mesh mesh)=>Destroy(mesh);
 __CODE__
 public static void Run(){Assert.Sweep("__CLASS__",()=>{
  int stage=Fault.FailAt;bool after=Fault.After;Fault.Arm();var parent=new GameObject();var material=new Material(new Shader());var owned=new FaultList<Mesh>();Fault.Arm(stage,after);
  try{new __CLASS__().Install(parent.transform,"part",material,owned);}
  finally{Fault.FailAt=-1;foreach(var mesh in owned)Destroy(mesh);Destroy(parent);Destroy(material);}
 });}
}
'''
METRO = r'''
class Metro {
 const int StationCount=2;Transform transform;Mesh[] trackMeshes=new Mesh[0];bool ownsTrackMeshes;
 static float LegLength(int leg)=>2;static void SampleLeg(int leg,float t,out Vector3 position,out Quaternion direction){position=new Vector3(t*5,0,0);direction=default;}
 static void GetStopPose(int stop,out Vector3 position,out Quaternion direction){position=new Vector3(100,0,0);direction=default;}
 static Transform Root(Transform parent,string name,Vector3 point)=>SlopCityInteriorGeometry.Root(parent,name);
 static void Dispose(Object item)=>Destroy(item);
 __CODE__
 public static void Run(){Assert.Sweep("Metro track transaction",()=>{
  int stage=Fault.FailAt;bool after=Fault.After;Fault.Arm();var root=new GameObject();var material=new Material(new Shader());var item=new Metro{transform=root.transform};Fault.Arm(stage,after);
  try{item.BuildTrack(null,material,material,material);}
  finally{Fault.FailAt=-1;item.OnDestroy();Destroy(root);Destroy(material);}
 });}
}
'''
DETAIL = r'''
class Detail {
 class DetailEntry{public int payloadBytes=64;public string fileName="mesh.bin";}
 class Lease{public int generation=1,failures;public float retryAt;}
 DetailEntry[] entries={new DetailEntry()};Lease[] leases={new Lease()};int pendingIndex=0;UnityWebRequest pendingRequest;BoundedDownload pendingDownload;Coroutine pendingRoutine;
 void StopCoroutine(Coroutine coroutine){(coroutine.routine as IDisposable)?.Dispose();}
 bool Complete(int index,int token,byte[] bytes){pendingIndex=-1;return true;}
 __CODE__
 public static void Run(){
  Assert.Sweep("Detail request lifecycle",()=>{
   var item=new Detail();var routine=item.Load(0,1);
   try{while(routine.MoveNext()){};}finally{Fault.FailAt=-1;(routine as IDisposable)?.Dispose();item.ClearRequest();Assert.That(item.pendingIndex==-1&&item.pendingRequest==null&&item.pendingDownload==null,"Failed request releases pending admission");}
  });
  for(int mode=0;mode<2;mode++){
   Fault.Arm();UnityWebRequestAsyncOperation.Done=false;var item=new Detail();var routine=item.Load(0,1);Assert.That(routine.MoveNext(),"Request waits");
   var request=item.pendingRequest;var download=item.pendingDownload;item.pendingRoutine=new Coroutine{routine=routine};Fault.Arm(mode==0?-1:1);
   try{item.CancelPending();}catch(InvalidOperationException){}finally{Fault.FailAt=-1;item.ClearRequest();}
   Assert.That(item.pendingIndex==-1&&Fault.Live==0&&request.Disposals==1&&download.Disposals==1,"Cancel/abort failure releases request and handler exactly once");
  }
  UnityWebRequestAsyncOperation.Done=true;
 }
}
'''

CITADEL = r"""
class Citadel {
 bool prepared;Bounds originalBounds,preparedBounds;int sourceTriangles,resultTriangles;
 FaultList<Mesh> ownedMeshes=new FaultList<Mesh>();Transform transform=new Transform();MeshFilter[] filters;
 T[] GetComponentsInChildren<T>(bool unused)=>(T[])(object)filters;
 static void Encapsulate(ref Bounds target,Bounds source,Matrix4x4 matrix,ref bool first){first=false;}
 static Mesh Subtract(Mesh source,Matrix4x4 matrix,Bounds[] volumes,out int before,out int after){before=after=1;return new Mesh();}
 __PREPARE__
 static Mesh FinishMesh(Mesh source){
  var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var uv2=new List<Vector2>();var colors=new List<Color>();
  bool uv2Available=true,colorsAvailable=true,normalsAvailable=false;var submeshes=new[]{new List<int>()};
  __TAIL__
 }
 public static void Run(){
  Assert.Sweep("Citadel replacement transaction",()=>{
   int stage=Fault.FailAt;bool after=Fault.After;Fault.Arm();var first=new Mesh();var second=new Mesh();var item=new Citadel{filters=new[]{new MeshFilter(),new MeshFilter()}};
   item.filters[0].sharedMesh=first;item.filters[1].sharedMesh=second;Fault.Arm(stage,after);
   try{item.Prepare();}finally{Fault.FailAt=-1;
    if(!item.prepared)Assert.That(item.filters[0].sharedMesh==first&&item.filters[1].sharedMesh==second,"Failed citadel restores original bindings");
    foreach(var mesh in item.ownedMeshes)Destroy(mesh);Destroy(first);Destroy(second);
   }
  });
  Assert.Sweep("Citadel mesh setup",()=>{int stage=Fault.FailAt;bool after=Fault.After;Fault.Arm();var source=new Mesh();Fault.Arm(stage,after);Mesh mesh=null;try{mesh=FinishMesh(source);}finally{Fault.FailAt=-1;Destroy(mesh);Destroy(source);}});
 }
}
class Entrance {
 FaultList<Mesh> ownedFacadeMeshes=new FaultList<Mesh>();
 Mesh Create(int serial){__CODE__ return mesh;}
 Mesh Clone(Mesh source,int serial){__CLONE__ return mesh;}
 public static void Run(){foreach(bool clone in new[]{false,true})Assert.Sweep("Entrance owner admission "+clone,()=>{
  int stage=Fault.FailAt;bool after=Fault.After;Fault.Arm();var source=new Mesh();var item=new Entrance();Fault.Arm(stage,after);
  try{if(clone)item.Clone(source,1);else item.Create(1);}finally{Fault.FailAt=-1;foreach(var mesh in item.ownedFacadeMeshes)Destroy(mesh);Destroy(source);}
 });}
}
"""

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    runtime = parser.add_mutually_exclusive_group(required=True)
    runtime.add_argument('--mono', type=Path)
    runtime.add_argument('--unity', type=Path)
    args = parser.parse_args()
    if args.mono:
        mono = args.mono.expanduser().resolve()
    else:
        unity = args.unity.expanduser().resolve()
        contents = unity / 'Contents' if unity.name.endswith('.app') else unity.parent.parent if unity.parent.name == 'MacOS' else None
        mono = contents / 'Resources/Scripting/MonoBleedingEdge/bin/mono' if contents else unity.parent / 'Data/MonoBleedingEdge/bin/mono'
    compiler = mono.parents[1] / 'lib/mono/4.5/csc.exe'
    if not mono.is_file() or not compiler.is_file():
        parser.error('Installed Unity Mono/Roslyn required; no tools are installed by this check.')
    def read(name): return (SOURCE / ('SlopCity' + name + '.cs')).read_text()
    stubs = STUBS.replace('using System;using System.Collections.Generic;', 'using Object=UnityEngine.Object;using System.Collections;using UnityEngine.Networking;using UnityEngine.Rendering;using System;using System.Collections.Generic;')
    stubs = stubs.replace('internal bool destroyed;', 'internal bool destroyed;internal bool tracked;')
    stubs = stubs.replace('if(native){Fault.Touch();Fault.Live++;}', 'if(native){Fault.Touch();Fault.Live++;tracked=true;}')
    stubs = stubs.replace('o.destroyed=true;Fault.Live--;Fault.Destroyed++;', 'o.destroyed=true;if(o is GameObject go)foreach(var child in go.transform.children.ToArray())Destroy(child.gameObject);if(o.tracked)Fault.Live--;Fault.Destroyed++;')
    stubs = stubs.replace('public static bool isPlaying=true;', 'public static bool isPlaying=true;public static string streamingAssetsPath="assets";')
    stubs = stubs.replace('public struct Bounds{public Bounds(Vector3 center,Vector3 size){}', 'public struct Bounds{public Vector3 max=>default;public Bounds(Vector3 center,Vector3 size){}')
    stubs = stubs.replace('public class MeshFilter:Object{', 'public class MeshFilter:Object{public Transform transform=new Transform();public T GetComponent<T>() where T:class=>null;')
    stubs = stubs.replace('public struct Color{', 'public struct Color{public static Color operator *(Color c,float v)=>c;')
    stubs = stubs.replace('public float x,y;public Vector2', 'public static Vector2 zero=>default;public static Vector2 operator +(Vector2 a,Vector2 b)=>new Vector2(a.x+b.x,a.y+b.y);public static Vector2 operator /(Vector2 a,float v)=>new Vector2(a.x/v,a.y/v);public float x,y;public Vector2')
    stubs = stubs.replace('public float sqrMagnitude=>', 'public static Vector3 zero=>default;public static Vector3 forward=>new Vector3(0,0,1);public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;public float magnitude=>(float)Math.Sqrt(sqrMagnitude);public float sqrMagnitude=>')
    stubs = stubs.replace('public struct Quaternion{', 'public struct Quaternion{public static Vector3 operator *(Quaternion q,Vector3 v)=>v;')
    stubs = stubs.replace('public const float PI=', 'public static float Pow(float a,float b)=>(float)Math.Pow(a,b);public static int CeilToInt(float n)=>(int)Math.Ceiling(n);public const float PI=')
    stubs = stubs.replace('public bool HasProperty', 'public Object GetTexture(string n)=>null;public void SetColor(string n,Color v)=>Fault.Touch();public bool HasProperty')
    stubs = stubs.replace('public Vector3[] vertices=>positions;', 'public Vector3[] vertices{get=>positions;set=>Fault.Set(ref positions,value);}')
    stubs = stubs.replace('public Mesh():base(true){}', 'IndexFormat format;public IndexFormat indexFormat{get=>format;set=>Fault.Set(ref format,value);}public void SetNormals(List<Vector3> v)=>Fault.Touch();public void CombineMeshes(CombineInstance[] v,bool a,bool b)=>Fault.Touch();public int subMeshCount;public void SetColors(List<Color> v)=>Fault.Touch();public Mesh():base(true){}')
    steak = read('SteakTable')
    geometry = block(steak,'private sealed class Geometry').replace('List<Object> owned = new List<Object>(24)', 'FaultList<Object> owned = new FaultList<Object>()')
    root = block(read('BuildingWorld'),'internal static Transform Root(')
    code = stubs + EXTRA.replace('__ROOT__',root) + STEAK.replace('__CODE__', geometry+'\n'+block(steak,'private sealed class MeshBuilder'))
    surface = read('SalvageSurface')
    code += SALVAGE.replace('__CODE__', '\n'.join(block(surface, sig) for sig in ['private void BindMaterial(', 'private void OnDestroy(']))
    furnish = read('InteriorFurnishing')
    code += FURNISH.replace('__CODE__', '\n'.join(block(furnish,sig) for sig in ['public void Complete(', 'internal void Release(']))
    for file, name in [('MetricFacade','MetricSurface'),('StreetAtmosphere','StreetDetail')]:
        method = block(read(file),'internal void Install(').replace('List<Mesh> owned','FaultList<Mesh> owned')
        code += INSTALL.replace('__CLASS__',name).replace('__CODE__',method)
    metro = read('MetroWorld')
    methods = '\n'.join(block(metro,sig) for sig in ['private void BuildTrack(', 'private static void MakeStrip(', 'private sealed class Strip', 'private void OnDestroy('])
    methods = methods.replace('new List<Mesh>()','new FaultList<Mesh>()').replace('List<Mesh> owned','FaultList<Mesh> owned')
    code += METRO.replace('__CODE__',methods)
    detail = read('DetailStreaming')
    code += DETAIL.replace('__CODE__','\n'.join(block(detail,sig) for sig in ['private IEnumerator Load(', 'private void Failed(', 'private void ClearRequest(', 'private void CancelPending(', 'private sealed class BoundedDownload']))
    citadel=read('CitadelInterior')
    prepare=block(citadel,'private void Prepare(').replace('new Dictionary<MeshFilter, Mesh>()','new FaultDictionary<MeshFilter, Mesh>()')
    subtract=block(citadel,'private static Mesh Subtract(')
    tail=subtract[subtract.index('            var mesh = new Mesh();'):].rsplit('}',1)[0]
    entrance=read('BuildingInterior')
    opening=entrance.index('                Mesh mesh = new Mesh();',entrance.index('private void TrimGroundFacade'))
    end=entrance.index('                mesh.SetVertices',opening)
    clone_open=entrance.index('            Mesh mesh = Instantiate(source);')
    clone_end=entrance.index('            mesh.indexFormat',clone_open)
    code += CITADEL.replace('__PREPARE__',prepare).replace('__TAIL__',tail).replace('__CODE__',entrance[opening:end]).replace('__CLONE__',entrance[clone_open:clone_end])
    code += '\nclass Entry{static void Main(){Steak.Run();Surface.Run();Furnishing.Run();MetricSurface.Run();StreetDetail.Run();Metro.Run();Detail.Run();Citadel.Run();Entrance.Run();}}'
    with tempfile.TemporaryDirectory(prefix='slop-city-world-lifetimes-') as directory:
        work=Path(directory);fixture=work/'Harness.cs';target=work/'Lifetimes.exe';fixture.write_text(code)
        result=subprocess.run([str(mono),str(compiler),'-nologo','-nowarn:0169,0649,0414','-out:'+str(target),str(fixture)],capture_output=True,text=True)
        if result.returncode: raise RuntimeError(result.stdout+result.stderr)
        result=subprocess.run([str(mono),str(target)],capture_output=True,text=True)
        if result.returncode: raise RuntimeError(result.stdout+result.stderr)
        print(result.stdout.strip())
    print('Actual C# world ownership methods passed managed fault injection; native/device acceptance remains separate.')

if __name__ == '__main__':
    main()
