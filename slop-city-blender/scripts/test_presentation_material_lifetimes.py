#!/usr/bin/env python3
"""Exercise actual presentation/material helpers with bounded fault substitutes.

Counts owned native-style resources and child hierarchy cleanup; does not measure
Unity, WebKit, GPU or physical-device memory. Native constructors/factories either
return their new object or throw before allocating, as required of the engine API.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from test_native_allocation_rollback import block

SOURCE = Path(__file__).resolve().parents[1] / 'Unity/Assets/Scripts'
HARNESS = r'''
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
using static UnityEngine.Object;
public static class Fault {
 public static int Calls, Fail=-1, Live, Attempts; public static bool After;
 public static void Arm(int fail=-1,bool after=false){Calls=0;Fail=fail;After=after;}
 public static void Touch(){Calls++;if(Calls==Fail){Fail=-1;throw new InvalidOperationException("Injected");}}
 public static void Set<T>(ref T field,T value){if(!After)Touch();field=value;if(After)Touch();}
 public static void Mutation(Action action){if(!After)Touch();action();if(After)Touch();}
}
namespace UnityEngine {
 public class Object {
  public bool dead;string label;
  protected Object(bool own=false){if(own){Fault.Touch();Fault.Live++;owned=true;}}bool owned;
  public static implicit operator bool(Object v)=>!ReferenceEquals(v,null)&&!v.dead&&(!(v is Component)||!((Component)v).gameObject.dead);
  public string name{get=>label;set=>Fault.Set(ref label,value);}
  public static void Destroy(Object v){if(ReferenceEquals(v,null)||v.dead)return;v.dead=true;if(v.owned)Fault.Live--;if(v is GameObject)((GameObject)v).TearDown();}
  public static void DestroyImmediate(Object v)=>Destroy(v);
 }
 public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject.transform;}
 public class GameObject:Object {
  Dictionary<Type,Component> parts=new Dictionary<Type,Component>();public Transform transform;bool active;
  public GameObject(string name,params Type[] types):base(true){transform=new RectTransform{gameObject=this};parts[typeof(RectTransform)]=transform;foreach(var type in types)if(type!=typeof(RectTransform)){var c=(Component)Activator.CreateInstance(type);c.gameObject=this;parts[type]=c;}}
  public T GetComponent<T>() where T:Component {Fault.Touch();return (T)parts[typeof(T)];}
  public void SetActive(bool value)=>Fault.Set(ref active,value);
  public void TearDown(){foreach(var child in new List<Transform>(transform.children))Destroy(child.gameObject);if(transform.parent!=null)transform.parent.children.Remove(transform);}
 }
 public class Transform:Component {
  public Transform parent;public List<Transform> children=new List<Transform>();
  public void SetParent(Transform value,bool stay)=>Fault.Mutation(()=>{if(parent!=null)parent.children.Remove(this);parent=value;if(parent!=null)parent.children.Add(this);});
 }
 public class RectTransform:Transform {
  Vector2 amin,amax,omin,omax;
  public Vector2 anchorMin{set=>Fault.Set(ref amin,value);}public Vector2 anchorMax{set=>Fault.Set(ref amax,value);}
  public Vector2 offsetMin{set=>Fault.Set(ref omin,value);}public Vector2 offsetMax{set=>Fault.Set(ref omax,value);}
 }
 public enum RenderMode{ScreenSpaceOverlay}public enum TextAnchor{MiddleCenter}
 public class Canvas:Component {bool shown;public bool enabled{set=>Fault.Set(ref shown,value);}RenderMode mode;int order;public RenderMode renderMode{set=>Fault.Set(ref mode,value);}public int sortingOrder{set=>Fault.Set(ref order,value);}}
 public struct Vector2 {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}public static Vector2 zero=>new Vector2();public static Vector2 one=>new Vector2(1,1);public static float Distance(Vector2 a,Vector2 b)=>0;}
 public struct Vector3 {public Vector3(float x,float y,float z){}public static Vector3 back=>new Vector3();}
 public struct Color {public Color(float r,float g,float b,float a=1){}public static Color white=>new Color();}
 public struct Rect {public Rect(float x,float y,float w,float h){}}
 public static class Mathf {public static float Clamp01(float n)=>n;}
 public class RenderTexture:Texture{public RenderTexture():base(true){}public void Release(){Fault.Touch();}}
 public class Camera:Object{RenderTexture target;float aspectValue;bool shown;public RenderTexture targetTexture{get=>target;set=>Fault.Set(ref target,value);}public float aspect{set=>Fault.Set(ref aspectValue,value);}public bool enabled{set=>Fault.Set(ref shown,value);}public void ResetAspect()=>Fault.Touch();}
 public static class Debug{public static void LogWarning(string message){}}
 public class Font:Object{}public class Shader:Object {public bool isSupported=true;public static Shader Find(string name)=>new Shader();}
 public static class Resources {public static T GetBuiltinResource<T>(string name) where T:new(){Fault.Touch();return new T();}}
 public static class Application {public static bool isPlaying;}
 public class Texture:Object {public Texture(bool own=false):base(own){}}
 public enum TextureFormat{RGBA32}public enum FilterMode{Bilinear}
 public class Texture2D:Texture {FilterMode filter;public Texture2D(int x,int y,TextureFormat f,bool mip):base(true){}public FilterMode filterMode{set=>Fault.Set(ref filter,value);}public void SetPixels(Color[] p)=>Fault.Touch();public void Apply(bool mip,bool readable)=>Fault.Touch();}
 public class Sprite:Object {public Texture2D texture;Sprite(Texture2D value):base(true){texture=value;}public static Sprite Create(Texture2D t,Rect r,Vector2 p,float pixels)=>new Sprite(t);}
 public class Material:Object {
  Texture tex;Color tint;bool instancing;
  public Material(Shader s):base(true){}public Texture mainTexture{get=>tex;set=>Fault.Set(ref tex,value);}public Color color{set=>Fault.Set(ref tint,value);}
  public bool enableInstancing{set=>Fault.Set(ref instancing,value);}public bool HasProperty(string name){Fault.Touch();return true;}public void SetFloat(string name,float n)=>Fault.Touch();
 }
 public class Mesh:Object {
  Vector3[] verts,norms;Vector2[] uvs;int[] tris;public Mesh():base(true){}
  public Vector3[] vertices{set=>Fault.Set(ref verts,value);}public Vector2[] uv{set=>Fault.Set(ref uvs,value);}public int[] triangles{set=>Fault.Set(ref tris,value);}public Vector3[] normals{set=>Fault.Set(ref norms,value);}
  public void RecalculateBounds()=>Fault.Touch();public void UploadMeshData(bool keep)=>Fault.Touch();
 }
}
namespace UnityEngine.UI {
 public class RawImage:Component {Texture image;public Texture texture{set=>Fault.Set(ref image,value);}Color tint;bool raycast;public Color color{set=>Fault.Set(ref tint,value);}public bool raycastTarget{set=>Fault.Set(ref raycast,value);}}
 public class Text:Component {Font fontValue;string textValue;int size;Color tint;TextAnchor anchor;bool raycast,shown;
  public Font font{set=>Fault.Set(ref fontValue,value);}public string text{set=>Fault.Set(ref textValue,value);}public int fontSize{set=>Fault.Set(ref size,value);}public Color color{set=>Fault.Set(ref tint,value);}
  public TextAnchor alignment{set=>Fault.Set(ref anchor,value);}public bool raycastTarget{set=>Fault.Set(ref raycast,value);}public bool enabled{set=>Fault.Set(ref shown,value);}public RectTransform rectTransform=>(RectTransform)transform;
 }
}
public class Presentation {
 public Transform transform;Canvas presentationCanvas;RawImage worldImage;Text recoveryMessage;Camera worldCamera;RenderTexture worldTexture;bool suspendedCamera,originalAutomaticAspect;float originalAspect,nextAllocation;int lastDisplayWidth,lastDisplayHeight,lastBudget;
__CANVAS__
__RELEASE_WORLD__
 public static void Run(){
  Fault.Arm();var host=new GameObject("host");var p=new Presentation{transform=host.transform};int baseline=Fault.Live;
  Fault.Arm();p.EnsureCanvas();int stages=Fault.Calls;Check.That(Fault.Live==baseline+3,"complete canvas owns exactly three hierarchy objects");
  Fault.Arm(1);p.EnsureCanvas();Check.That(Fault.Calls==0,"complete canvas is reused without allocation or setters");Destroy(host);Check.That(Fault.Live==0,"host destruction cascades through complete canvas");
  foreach(bool after in new[]{false,true})for(int stage=1;stage<=stages;stage++)for(int retry=0;retry<30;retry++){
   Fault.Arm();host=new GameObject("host");p=new Presentation{transform=host.transform};
   Fault.Arm(stage,after);bool failed=false;try{p.EnsureCanvas();}catch(InvalidOperationException){failed=true;}
   Fault.Attempts++;Check.That(failed&&Fault.Live==1,"canvas partial failure leaks at "+stage+" after="+after);
   Fault.Arm();p.EnsureCanvas();Check.That(Fault.Live==4,"canvas retry recovers after "+stage);Destroy(host);Check.That(Fault.Live==0,"canvas recovery teardown");
  }
  // One retained world target remains owned until its explicit destruction,
  // even if native camera/UI detachment throws during component teardown.
  Fault.Arm();host=new GameObject("host");p=new Presentation{transform=host.transform};p.EnsureCanvas();p.worldCamera=new Camera();p.worldTexture=new RenderTexture();p.worldCamera.targetTexture=p.worldTexture;p.suspendedCamera=true;p.originalAutomaticAspect=true;
  Fault.Arm();p.ReleaseWorld();int releaseStages=Fault.Calls;Check.That(Fault.Live==4,"normal ReleaseWorld retires target and preserves reusable canvas");Destroy(host);
  foreach(bool after in new[]{false,true})for(int stage=1;stage<=releaseStages;stage++){
   Fault.Arm();host=new GameObject("host");p=new Presentation{transform=host.transform};p.EnsureCanvas();p.worldCamera=new Camera();p.worldTexture=new RenderTexture();p.worldCamera.targetTexture=p.worldTexture;p.suspendedCamera=true;p.originalAutomaticAspect=true;
   Fault.Arm(stage,after);try{p.ReleaseWorld();}catch(InvalidOperationException){}Check.That(p.worldTexture==null&&Fault.Live==4,"world target remains owned after teardown setter failure "+stage);Fault.Arm();p.ReleaseWorld();Destroy(host);Check.That(Fault.Live==0,"ReleaseWorld retry hierarchy teardown");
  }
  Console.WriteLine("Actual ReleaseWorld: "+releaseStages+" teardown stages, both mutation orders; target retired after each failure");
  Console.WriteLine("Actual EnsureCanvas: "+stages+" stages, both mutation orders x30; includes each unparented/parented SetParent failure, retry, hierarchy teardown");
 }
}
public class Helpers {
__HELPERS__
 static void Exercise(Func<Object> create,Action<Object> release,string label,Action rollback=null){
  Fault.Arm();var obj=create();int stages=Fault.Calls;release(obj);Check.That(Fault.Live==0,label+" successful teardown");
  foreach(bool after in new[]{false,true})for(int stage=1;stage<=stages;stage++)for(int retry=0;retry<30;retry++){
   Fault.Arm(stage,after);bool failed=false;try{create();}catch(InvalidOperationException){failed=true;if(rollback!=null)rollback();}Fault.Attempts++;
   Check.That(failed&&Fault.Live==0,label+" allocation/setter failure stage "+stage);
  }
  Fault.Arm();obj=create();release(obj);Check.That(Fault.Live==0,label+" recovery teardown");Console.WriteLine(label+": "+stages+" stages, both mutation orders x30");
 }
 public static void Run(){foreach(bool playing in new[]{false,true}){Application.isPlaying=playing;
  Exercise(()=>ArtworkMaterial(new Shader(),new Texture()),Release,"Actual brand artwork material");
  Exercise(()=>CreateQuad(),Release,"Actual brand plaque mesh");
  Exercise(()=>MakeDisc(),o=>{var s=(Sprite)o;Destroy(s.texture);Destroy(s);},"Actual mobile disc texture/sprite");
  var owner=new SlopCityUtilityResources();
  Exercise(()=>Material(owner,"fixture",Color.white,.5f),o=>owner.Teardown(),"Actual utility material owner",()=>owner.Teardown());
 }}
}
public class SlopCityUtilityResources {
 readonly List<Material> materials=new List<Material>();
__OWNER__
 public void Teardown(){OnDestroy();}
}
public class Check {public static void That(bool ok,string message){if(!ok)throw new Exception(message);}public static void Main(){Presentation.Run();Helpers.Run();Console.WriteLine("Total fault attempts: "+Fault.Attempts+"; ownership proof only, no device-memory claim");}}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mono', type=Path, required=True)
    args = parser.parse_args()
    mono = args.mono.expanduser().resolve()
    compiler = mono.parents[1] / 'lib/mono/4.5/csc.exe'
    if not mono.is_file() or not compiler.is_file():
        parser.error('Installed Unity Mono/Roslyn required.')
    read = lambda name: (SOURCE / ('SlopCity' + name + '.cs')).read_text()
    branding, utility = read('Branding'), read('WorldUtilities')
    helpers = '\n'.join([block(branding, sig) for sig in [
        'private static Material ArtworkMaterial(', 'private static Mesh CreateQuad(', 'private static void Release(']])
    helpers += '\n' + block(read('MobileControls'), 'private static Sprite MakeDisc(')
    helpers += '\n' + block(utility, 'private static Material Material(')
    owner = utility[utility.index('internal sealed class SlopCityUtilityResources'):]
    code = HARNESS.replace('__RELEASE_WORLD__', '\n'.join(block(read('WebPresentation'), sig) for sig in ['private void ReleaseWorld(', 'private void ReleaseTexture(', 'private static void DisposeTexture('])).replace('__CANVAS__', block(read('WebPresentation'), 'private void EnsureCanvas(')).replace('__HELPERS__', helpers).replace('__OWNER__', '\n'.join(block(owner, sig) for sig in ['internal void Own(', 'private void OnDestroy(']))
    with tempfile.TemporaryDirectory(prefix='slop-presentation-lifetime-') as temp:
        source, executable = Path(temp) / 'Check.cs', Path(temp) / 'Check.exe'
        source.write_text(code)
        subprocess.run([str(mono), str(compiler), '/nologo', '/out:' + str(executable), str(source)], check=True)
        subprocess.run([str(mono), str(executable)], check=True)


if __name__ == '__main__':
    main()
