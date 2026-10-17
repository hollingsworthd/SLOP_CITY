using System;
using System.Collections.Generic;
using System.Linq;
namespace UnityEngine {
 [AttributeUsage(AttributeTargets.Field)] public class SerializeField:Attribute{}
 [AttributeUsage(AttributeTargets.Field)] public class RangeAttribute:Attribute { public RangeAttribute(float a,float b){} }
 public class Object { public string name; public bool destroyed; public static List<Object> All=new List<Object>(); public Object(){All.Add(this);} public static implicit operator bool(Object o){return o!=null&&!o.destroyed;} public static void Destroy(Object o){if(o==null)return;o.destroyed=true;var go=o as GameObject;if(go!=null){foreach(var c in go.components)c.destroyed=true;foreach(var child in go.transform.children.ToArray())Destroy(child.gameObject);}} public static T FindFirstObjectByType<T>() where T:Object{return All.OfType<T>().FirstOrDefault(o=>o);} }
 public class GameObject:Object { public Transform transform;public List<Component> components=new List<Component>();public GameObject(string n=""){name=n;transform=new Transform{gameObject=this};}public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};components.Add(c);return c;}public T GetComponent<T>() where T:class{return components.OfType<T>().FirstOrDefault();} }
 public class Component:Object { public GameObject gameObject;public Transform transform{get{return gameObject.transform;}}public T GetComponentInParent<T>() where T:class{return transform.GetComponentInParent<T>();} }
 public class MonoBehaviour:Component{public bool isActiveAndEnabled=true;}
 public class Transform:Object {public GameObject gameObject;public new string name{get{return gameObject.name;}set{gameObject.name=value;}}public Transform parent;public List<Transform> children=new List<Transform>();public Vector3 position;public Vector3 forward=Vector3.forward;public void SetParent(Transform p,bool unused){parent=p;p.children.Add(this);}public bool IsChildOf(Transform p){for(var t=this;t!=null;t=t.parent)if(t==p)return true;return false;}public T GetComponentInParent<T>() where T:class{for(var t=this;t!=null;t=t.parent){var found=t.gameObject.GetComponent<T>();if(found!=null)return found;}return null;}public Vector3 TransformDirection(Vector3 v){return v;} }
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 forward=new Vector3(0,0,1),back=new Vector3(0,0,-1);public float sqrMagnitude{get{return x*x+y*y+z*z;}}public float magnitude{get{return (float)Math.Sqrt(sqrMagnitude);}}public Vector3 normalized{get{return magnitude>0?this/magnitude:new Vector3();}}public static Vector3 operator-(Vector3 v){return new Vector3(-v.x,-v.y,-v.z);}public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}public static Vector3 operator/(Vector3 v,float s){return new Vector3(v.x/s,v.y/s,v.z/s);}public static float Dot(Vector3 a,Vector3 b){return a.x*b.x+a.y*b.y+a.z*b.z;} }
 public struct Bounds {public Vector3 center;}
 public struct Plane{}
 public class Camera:Component {public static Camera main;public bool isActiveAndEnabled=true;}
 public class Renderer:Component {public bool enabled=true;public Material sharedMaterial;public Bounds bounds;}
 public class Shader:Object {public bool isSupported=true;}
 public class Texture:Object{public int width=360,height=640;}
 public enum RenderTextureFormat{ARGB32}public enum FilterMode{Bilinear}public enum TextureWrapMode{Clamp}
 public class RenderTexture:Texture {public static RenderTexture active;public bool created;public FilterMode filterMode;public TextureWrapMode wrapMode;public bool useMipMap,autoGenerateMips;public RenderTexture(int w,int h,int d,RenderTextureFormat f){}public bool IsCreated(){return created;}public bool Create(){created=true;return true;}public void Release(){created=false;}}
 public class Material:Object {public Texture mainTexture;public Material(Shader s){} }
 public struct Color {public static Color black=new Color();}
 public static class GL {public static void Clear(bool a,bool b,Color c){} }
 public enum AudioRolloffMode{Custom}public enum AudioSourceCurveType{CustomRolloff}
 public class AnimationCurve {public static AnimationCurve Linear(float a,float b,float c,float d){return new AnimationCurve();}}
 public class AudioSource:Component {public bool playOnAwake;public float spatialBlend,minDistance,maxDistance,dopplerLevel,volume;public AudioRolloffMode rolloffMode;public void SetCustomCurve(AudioSourceCurveType t,AnimationCurve c){}public void Stop(){} }
 public enum QueryTriggerInteraction{Ignore}public struct RaycastHit{public Transform transform;}
 public static class Physics {public const int DefaultRaycastLayers=-1;public static RaycastHit[] Hits=new RaycastHit[0];public static RaycastHit[] RaycastAll(Vector3 p,Vector3 d,float n,int mask,QueryTriggerInteraction t){return Hits;}}
 public static class GeometryUtility {public static bool Visible=true;public static void CalculateFrustumPlanes(Camera c,Plane[] p){}public static bool TestPlanesAABB(Plane[] p,Bounds b){return Visible;}}
 public static class Time {public static float unscaledTime;}
 public static class Application {public static string streamingAssetsPath="http://local/StreamingAssets";}
 public static class Mathf {public static float Clamp01(float v){return Math.Max(0,Math.Min(1,v));}public static float Max(float a,float b){return Math.Max(a,b);}public static float InverseLerp(float a,float b,float v){return Clamp01((v-a)/(b-a));}}
 public static class Debug {public static void LogWarning(string s,Object o=null){} }
}
namespace UnityEngine.Video {
 using UnityEngine;
 public enum VideoSource{Url}public enum VideoRenderMode{RenderTexture,APIOnly}public enum VideoAspectRatio{FitInside}public enum VideoTimeUpdateMode{UnscaledGameTime}public enum VideoAudioOutputMode{Direct,AudioSource}
 public class VideoPlayer:Component {public static bool DelayPrepare;public delegate void EventHandler(VideoPlayer p);public delegate void ErrorEventHandler(VideoPlayer p,string s);public delegate void FrameReadyEventHandler(VideoPlayer player,long frame);public event FrameReadyEventHandler frameReady;public bool sendFrameReadyEvents;public Texture texture=new Texture();public event EventHandler prepareCompleted,loopPointReached;public event ErrorEventHandler errorReceived;public bool playOnAwake,isLooping,waitForFirstFrame,isPrepared,isPlaying;public VideoSource source;public VideoRenderMode renderMode;public RenderTexture targetTexture;public VideoAspectRatio aspectRatio;public VideoTimeUpdateMode timeUpdateMode;public VideoAudioOutputMode audioOutputMode;public ushort controlledAudioTrackCount,audioTrackCount=1;public uint width=360,height=640;public double time,length=65.83;public string url;public void EnableAudioTrack(ushort i,bool e){}public void SetTargetAudioSource(ushort i,AudioSource a){}public void SetDirectAudioVolume(ushort i,float v){}public void SetDirectAudioMute(ushort i,bool m){}public void Prepare(){if(!DelayPrepare)CompletePrepare();}public void CompletePrepare(){isPrepared=true;if(prepareCompleted!=null)prepareCompleted(this);}public void Play(){isPlaying=true;}public void Pause(){isPlaying=false;}public void Stop(){isPlaying=false;isPrepared=false;}}
}
namespace SlopCity {
 using UnityEngine;
 public class SlopCityPlayerInput:Component {public bool UseTouchControls=true;}
 public class SlopCityFirstPerson:Component {public SlopCityMediaPlayer Media;}
 public static class SlopCityMediaAudioFocus {public static void Submit(MonoBehaviour s,float score,float v){}public static float Gain(MonoBehaviour s){return 1;}public static void Remove(MonoBehaviour s){} }
 public static class BaselineExtension {public static void SetMobileCityPriority(this SlopCityMediaPlayer player,bool prioritized){} }
}
