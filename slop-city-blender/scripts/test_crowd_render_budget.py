#!/usr/bin/env python3
"""Run actual crowd C# reservation/render/geometry methods with managed Unity substitutes.

Exercises shared LOD geometry, whole-person budgets, repeated camera turns and
batch growth without Unity, graphics, native RAM measurements or device claims.
"""
import argparse
import importlib.util
from pathlib import Path
import re
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('ownership', Path(__file__).with_name('test_native_allocation_rollback.py'))
ownership = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ownership)

INTEGRATION_STUBS = r'''
namespace UnityEngine {
 public enum CameraType{Game,Preview}
 public class Transform{public Vector3 position;}
 public class Camera:Object{public static Camera main;public CameraType cameraType;public Transform transform=new Transform();public int facing=1;}
 public class GameObject{public int layer;}
 public struct Plane{public int direction;}
 public static class Time{public static float unscaledDeltaTime=.05f;public static int frameCount=1;}
 public static class GeometryUtility{
  public static void CalculateFrustumPlanes(Camera c,Plane[] p){p[0].direction=c.facing;}
  public static bool TestPlanesAABB(Plane[] p,Bounds b)=>b.center.z*p[0].direction>=-b.size.z*.5f;
 }
 public static class Graphics{
  public static void DrawMeshInstanced(Mesh mesh,int submesh,Material material,Matrix4x4[] matrices,int count,MaterialPropertyBlock properties,UnityEngine.Rendering.ShadowCastingMode shadows,bool receive,int layer,Camera camera,UnityEngine.Rendering.LightProbeUsage probes){}
 }
}
namespace UnityEngine.Rendering{public enum ShadowCastingMode{Off,On}public enum LightProbeUsage{Off,BlendProbes}}
public class SlopCityPlayerInput:UnityEngine.Object{public bool UseTouchControls=true;}
public class SlopCityFirstPerson:UnityEngine.Object{public Camera ViewCamera;public T GetComponent<T>() where T:new()=>new T();}
'''

HARNESS = r'''
public sealed class RenderHarness {
 private MatrixWardrobe wardrobe;private SlopCityCrowdVisuals visuals;
 private bool reserveOnly,reservationFailed,constrainedCrowd;
 private int pendingPartCount,reservedVertices,detailedPresented;
 public int SubmittedVertexCount {get;private set;}
 private float simulationTime,browserInterpolation=1;
 private readonly PendingPart[] pendingParts=new PendingPart[64];
 private readonly List<DrawBatch> batches=new List<DrawBatch>();
 private readonly Dictionary<long,DrawBatch> batchLookup=new Dictionary<long,DrawBatch>();
 private static readonly int[] Sides={-1,1};
 private readonly TouchPresentation[] touchVisible=new TouchPresentation[MaximumTouchPresented];
 private readonly Plane[] frustum=new Plane[6];
 private readonly int[] visibleOrder=new int[MaximumPresentedNative];
 private readonly float[] visibleDistance=new float[MaximumPresentedNative];
 private int[] presentationStamp=new int[256];private int presentationSerial,lastPresentationFrame,activeCount;
 private int PresentedCount,WholePeopleDeferred;
 private Agent[] agents=new Agent[0];private bool isActiveAndEnabled=true;
 private SlopCityFirstPerson playerController;private SlopCityPlayerInput playerInput;
 private GameObject gameObject=new GameObject();
 private void EnsureAgents(){}private bool EnsureWardrobe()=>wardrobe!=null;
 private bool ValidNode(int route,int node)=>true;
 __CODE__
 private static void Check(bool pass,string why){if(!pass)throw new Exception(why);}
 private void Reset(){foreach(var batch in batches)batch.count=0;SubmittedVertexCount=detailedPresented=0;}
 private int MatrixCount(){int n=0;foreach(var batch in batches)n+=batch.count;return n;}
 private void AssertBudget(){
  int vertices=0;foreach(var batch in batches){vertices+=batch.count*batch.mesh.vertexCount;Check(batch.count<=batch.matrices.Length&&batch.matrices.Length<=1023,"Bounded reusable instance storage");}
  Check(vertices==SubmittedVertexCount,"Only committed whole-person vertices count toward frame budget");
  if(constrainedCrowd)Check(vertices<=MaximumTouchVertices&&detailedPresented<=MaximumTouchDetailed,"Touch frame vertex/detail ceilings");
 }
 private Agent Person(int i)=>new Agent{seed=new SlopCityNpcSeed{appearance=i,height=1.76f,width=1,phase=i*.13f},redContact=i%19==0,casual=i%37==0,moving=true,phase=i*.13f};
 private bool HasDetailedHead(Agent person){
  foreach(var batch in batches)if(batch.mesh==visuals.parts[(int)SlopCityHumanPart.Head])
   for(int index=0;index<batch.count;index++)if(Math.Abs(batch.matrices[index].translation.x-person.position.x)<.001f&&Math.Abs(batch.matrices[index].translation.z-person.position.z)<.001f)return true;
  return false;
 }
 private void Frame(Camera camera){
  Time.frameCount++;
#if UNITY_WEBGL
  Application.isMobilePlatform=true;RenderCrowd(camera);
#else
  Reset();PresentedCount=WholePeopleDeferred=0;presentationSerial++;
  GeometryUtility.CalculateFrustumPlanes(camera,frustum);RenderTouchCrowd(camera,camera.transform.position);
#endif
 }
 private void CheckLocalPresentation(){
  var camera=new Camera();Camera.main=camera;constrainedCrowd=true;
  agents=new Agent[28];activeCount=agents.Length;
  for(int i=0;i<agents.Length;i++){
   agents[i]=Person(13);agents[i].casual=false;agents[i].redContact=false;
   agents[i].seed.residentId=100+i;agents[i].residentIndex=i;
   agents[i].position=new Vector3(i*.01f,0,(i<14?1:-1)*(5+(i%14)*.25f));
#if UNITY_WEBGL
   agents[i].previousPosition=agents[i].position;
#endif
  }
  Frame(camera);AssertBudget();
  Check(PresentedCount==14&&detailedPresented==MaximumTouchDetailed,"Local frame preserves complete bodies and shared detail cap");
  Check(HasDetailedHead(agents[0])&&!HasDetailedHead(agents[13]),"Near residents get detail before farther residents");
  Check(presentationStamp[0]==presentationSerial&&presentationStamp[14]!=presentationSerial,"Only visible admitted residents become interactive");
  foreach(var slot in touchVisible)Check(slot.person==null,"Frame ordering releases resident references after use");
  camera.facing=-1;Frame(camera);AssertBudget();
  Check(PresentedCount==14&&presentationStamp[14]==presentationSerial&&presentationStamp[0]!=presentationSerial,"Turning updates resident presentation identity");
  for(int i=0;i<100;i++){camera.facing=(i%2==0?1:-1);Frame(camera);}
  long allocated=GC.GetAllocatedBytesForCurrentThread();int live=Fault.Live;
  for(int i=0;i<1000;i++){camera.facing=(i%2==0?1:-1);Frame(camera);AssertBudget();}
  Check(GC.GetAllocatedBytesForCurrentThread()==allocated&&Fault.Live==live,"Camera-turn rendering allocates nothing after warmup");
  camera.facing=1;Application.isMobilePlatform=false;RenderCrowd(camera);AssertBudget();
  Check(!constrainedCrowd&&detailedPresented==14,"Ordinary rendering retains full resident detail");
  agents=new Agent[100];activeCount=agents.Length;
  for(int i=0;i<agents.Length;i++){
   agents[i]=Person(13);agents[i].casual=false;agents[i].redContact=false;agents[i].seed.residentId=100+i;agents[i].residentIndex=i;
   agents[i].position=new Vector3(i*.01f,0,14+i*.03f);
#if UNITY_WEBGL
   agents[i].previousPosition=agents[i].position;
#endif
  }
  constrainedCrowd=true;Frame(camera);AssertBudget();
  Check(PresentedCount<=MaximumTouchPresented&&WholePeopleDeferred>0,"Nearest complete residents survive body and vertex ceilings");
  Check(presentationStamp[0]==presentationSerial&&presentationStamp[99]!=presentationSerial,"Farthest residents cannot steal nearer selection");
  activeCount=0;agents=new Agent[0];
  Console.WriteLine("Local ordering, camera turns, resident stamps, body/detail/vertex ceilings and 1,000 allocation-free frames passed.");
 }
 public static void Main(){
  Fault.Arm();var h=new RenderHarness{constrainedCrowd=true};
  var source=new Material(Shader.Find("fixture"));h.wardrobe=new MatrixWardrobe(source);
  h.visuals=new SlopCityCrowdVisuals{parts=new Mesh[46],skin=new Material[6],hair=new Material[4],clothing=new[]{source}};
  int[] sizes={__MESH_COUNTS__};for(int i=0;i<sizes.Length;i++)h.visuals.parts[i]=new Mesh{vertexCount=sizes[i]};
  for(int i=0;i<6;i++)h.visuals.skin[i]=new Material(source);for(int i=0;i<4;i++)h.visuals.hair[i]=new Material(source);
  h.visuals.shoes=new Material(source);h.visuals.eyes=new Material(source);h.visuals.rubber=new Material(source);
  Check(h.wardrobe.coarseHeadMesh.vertexCount*8<h.visuals.parts[(int)SlopCityHumanPart.Head].vertexCount,"Distant heads materially reduce submitted vertices");
  Check(h.wardrobe.coarseDressMesh.vertexCount*8<=h.wardrobe.dressMesh.vertexCount,"Distant dress samples the same surface at lower resolution");
  Check(h.wardrobe.coarseTrouserMesh.vertexCount*3<h.wardrobe.trouserYokeMesh.vertexCount,"Distant trousers reduce submitted vertices");
  // The lower detail grids reuse exact high-detail sample locations rather than
  // replacing the new anatomy with a different garment shape.
  foreach(var pair in new[]{new[]{h.wardrobe.coarseDressMesh,h.wardrobe.dressMesh},new[]{h.wardrobe.coarseNecklineMesh,h.wardrobe.necklineMesh},new[]{h.wardrobe.coarseTrouserMesh,h.wardrobe.trouserYokeMesh}}){
   var full=new HashSet<Vector3>(pair[1].vertices);foreach(var v in pair[0].vertices)Check(full.Contains(v),"Coarse garment vertices retain exact authored surface samples");
  }
  var people=new Agent[MaximumTouchPresented];for(int i=0;i<people.Length;i++)people[i]=h.Person(i*13);
  // Changing which residents face the camera visits every skin/hair/garment
  // combination. After warmup, body turnover must allocate no native objects,
  // additional draw keys or larger retained matrix arrays.
  int live=Fault.Live,stableBatches=0,stableCapacity=0;long allocatedAfterWarmup=0;
  for(int frame=0;frame<2000;frame++){
   h.Reset();for(int rank=0;rank<people.Length;rank++){
    var person=people[(rank+frame)%people.Length];person.phase+=.05f;
    int before=h.MatrixCount();bool admitted=h.TryReservePerson(person,rank<24);
    Check(h.MatrixCount()==before,"Reservations never publish partial bodies");if(admitted)h.CommitReservedPerson();else Check(h.MatrixCount()==before,"Deferral leaves every batch intact");
   }
   h.AssertBudget();Check(Fault.Live==live,"Long camera-turn stress creates no native meshes or materials");
   int capacity=0;foreach(var batch in h.batches)capacity+=batch.matrices.Length;
   if(frame==999){stableBatches=h.batches.Count;stableCapacity=capacity;allocatedAfterWarmup=GC.GetAllocatedBytesForCurrentThread();}
   if(frame>999)Check(stableBatches==h.batches.Count&&stableCapacity==capacity,"Retained draw keys/capacities plateau after warmup");
  }
  Check(GC.GetAllocatedBytesForCurrentThread()==allocatedAfterWarmup,"Warmed rendering creates no managed allocations per resident/frame");
  Console.WriteLine("Touch: 2,000 camera turns; "+stableBatches+" shared draw keys / "+stableCapacity+" retained matrices; no native resource growth or warmed managed allocations.");
  // A full detail request near the limit gets a complete coarse replacement.
  var smith=h.Person(13);h.Reset();Check(h.ReservePerson(smith,true),"Full detail fixture reserves");int fullCost=h.reservedVertices;
  Check(h.ReservePerson(smith,false),"Coarse fixture reserves");int coarseCost=h.reservedVertices;Check(coarseCost<fullCost,"Whole-body coarse path reduces work");
  h.Reset();h.SubmittedVertexCount=MaximumTouchVertices-coarseCost;
  Check(h.TryReservePerson(smith,true)&&h.reservedVertices==coarseCost,"Detailed overflow falls back to complete coarse person");
  h.CommitReservedPerson();Check(h.SubmittedVertexCount==MaximumTouchVertices,"Exact vertex boundary admits a whole person");
  int saved=h.MatrixCount();Check(!h.TryReservePerson(smith,false)&&h.MatrixCount()==saved,"Over-budget person cannot leak partial limbs");
  h.Reset();for(int i=0;i<MaximumTouchDetailed+1;i++){Check(h.TryReservePerson(smith,true),"Dense close group keeps complete bodies");h.CommitReservedPerson();}
  Check(h.detailedPresented==MaximumTouchDetailed,"Full detail body ceiling falls back without dropping closest people");h.AssertBudget();
  // Browser desktop/native retains its existing full-detail mesh path/budgets.
  h.Reset();h.constrainedCrowd=false;for(int i=0;i<MaximumTouchDetailed+8;i++){Check(h.TryReservePerson(smith,true),"Ordinary rendering retains existing detailed admission");h.CommitReservedPerson();}
  Check(h.detailedPresented==MaximumTouchDetailed+8,"Touch detail ceiling never changes ordinary rendering");h.AssertBudget();
  Console.WriteLine("Geometry: full/coarse Smith="+fullCost+"/"+coarseCost+" vertices; exact surface samples, boundary fallback, atomic deferral and ordinary detail passed.");
  h.CheckLocalPresentation();
  h.wardrobe.Dispose();foreach(var mesh in h.visuals.parts)Destroy(mesh);foreach(var material in h.visuals.skin)Destroy(material);foreach(var material in h.visuals.hair)Destroy(material);Destroy(h.visuals.shoes);Destroy(h.visuals.eyes);Destroy(h.visuals.rubber);Destroy(source);
  Check(Fault.Live==0,"Every authored fixture and owned full/coarse mesh/material releases");
 }
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', type=Path, required=True)
    args = parser.parse_args()
    mono = args.unity.resolve() / 'Contents/Resources/Scripting/MonoBleedingEdge/bin/mono'
    compiler = mono.parents[1] / 'lib/mono/4.5/csc.exe'
    crowd = (ROOT / 'Unity/Assets/Scripts/SlopCityCrowd.cs').read_text()
    signatures = [
        'private sealed class Agent', 'private sealed class DrawBatch', 'private struct PendingPart',
        'private struct TouchPresentation', 'private void RenderTouchCrowd(', 'private void SelectTouchPerson(',
        'public int MaximumPresentedCount', 'private void RenderCrowd(', 'private void DrawBatches(',
        'private bool TryReservePerson(', 'private bool ReservePerson(', 'private void CommitReservedPerson(',
        'private void RenderPerson(', 'private sealed class MatrixWardrobe', 'private sealed class WardrobeMesh',
        'private void Add(SlopCityHumanPart', 'private void Add(Mesh',
    ]
    helpers = crowd[crowd.index('private static Vector2 FootPose('):crowd.index('private static readonly int[] Sides')]
    constants = re.search(r'public const int MaximumTouchPresented[^;]+;', crowd)[0]
    declarations = '\n'.join(ownership.block(crowd, token) for token in [
        'public enum SlopCityNpcRole', 'public enum SlopCityHumanPart',
        'public sealed class SlopCityCrowdVisuals', 'public struct SlopCityNpcSeed',
    ])
    # Serialized enum slot order is the actual source contract.
    parts = re.search(r'public enum SlopCityHumanPart\s*\{([^}]+)', crowd)[1].replace('\n', '').split(',')
    sizes = []
    for part in parts:
        asset = (ROOT / 'Unity/Assets/Materials/SlopCityCrowd' / ('Crowd_' + part.strip() + '.asset')).read_text()
        sizes.append(re.search(r'm_VertexCount: (\d+)', asset)[1])
    population = (ROOT / 'Unity/Assets/Scripts/SlopCityCrowdPopulation.cs').read_text()
    constants += '\n' + re.search(r'public const int MaximumDetailedResidents[^;]+;', population)[0]
    code = constants + '\n' + helpers + '\n' + '\n'.join(ownership.block(crowd, token) for token in signatures)
    stubs = ownership.STUBS.replace('using System;using System.Collections.Generic;', 'using System;using UnityEngine.Rendering;using System.Collections.Generic;')
    stubs = stubs.replace('public static bool isPlaying=true;', 'public static bool isPlaying=true,isMobilePlatform;')
    stubs = stubs.replace('public struct Bounds{public Bounds(Vector3 center,Vector3 size){}}', 'public struct Bounds{public Vector3 center,size;public Bounds(Vector3 center,Vector3 size){this.center=center;this.size=size;}}')
    stubs = stubs.replace('public static class Mathf{', 'public static class Mathf{public static bool Approximately(float a,float b)=>Math.Abs(a-b)<.000001f;')
    stubs = stubs.replace('public struct Vector3{', 'public struct Vector3{public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a-b).sqrMagnitude);')
    stubs = stubs.replace('public struct Matrix4x4{public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s)=>default;public static Matrix4x4 Translate(Vector3 v)=>default;public static Matrix4x4 Rotate(Quaternion q)=>default;public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b)=>default;}', 'public struct Matrix4x4{public Vector3 translation;public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s)=>new Matrix4x4{translation=p};public static Matrix4x4 Translate(Vector3 v)=>new Matrix4x4{translation=v};public static Matrix4x4 Rotate(Quaternion q)=>default;public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b)=>new Matrix4x4{translation=a.translation+b.translation};}')
    source = stubs + INTEGRATION_STUBS + declarations + HARNESS.replace('__CODE__', code).replace('__MESH_COUNTS__', ','.join(sizes))
    with tempfile.TemporaryDirectory(prefix='slop-city-crowd-render-') as directory:
        fixture = Path(directory) / 'Crowd.cs'
        fixture.write_text(source)
        for mode, defines in [('native', []), ('browser', ['-define:UNITY_WEBGL'])]:
            target = Path(directory) / (mode + '.exe')
            subprocess.run([str(mono), str(compiler), '-nologo', '-nowarn:0169,0649,0414', '-out:' + str(target), *defines, str(fixture)], check=True)
            subprocess.run([str(mono), str(target)], check=True)
    print('Actual C# crowd methods passed managed stress in native/browser branches; device/graphics acceptance remains separate.')


if __name__ == '__main__':
    main()
