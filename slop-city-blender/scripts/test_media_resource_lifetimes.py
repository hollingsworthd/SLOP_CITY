#!/usr/bin/env python3
"""Fault-inject actual display allocation/retirement methods without Unity launch.

Managed Unity substitutes prove native ownership, not browser/GPU RAM. The
installed Unity Mono compiler is required; no tools or dependencies are installed.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
from test_native_allocation_rollback import block

SOURCE = Path(__file__).resolve().parents[1] / 'Unity/Assets/Scripts'
STUBS = r'''
using System;using System.Collections.Generic;using UnityEngine;using UnityEngine.Video;using static UnityEngine.Object;
public static class Fault {
 public static int Calls,Fail=-1,Live,Attempts;public static bool After,RefuseCreate,ThrowRelease,ThrowRestore,ThrowHide,ThrowStop,ThrowPrelude,ThrowAudioStop,ThrowDetach,ThrowEvent,ThrowDestroySession,ThrowDestroyMaterial,ThrowDestroyTexture,ThrowDestroyAfter;
 public static void Touch(){Calls++;if(Calls==Fail){Fail=-1;throw new InvalidOperationException("Injected");}}
 public static void Set<T>(ref T field,T value){if(After){field=value;Touch();}else{Touch();field=value;}}
 public static void Arm(int n=-1,bool after=false){Calls=0;Fail=n;After=after;RefuseCreate=ThrowRelease=ThrowRestore=ThrowHide=ThrowStop=ThrowPrelude=ThrowAudioStop=ThrowDetach=ThrowEvent=ThrowDestroySession=ThrowDestroyMaterial=ThrowDestroyTexture=ThrowDestroyAfter=false;}
}
namespace UnityEngine {
 public class Object {
  bool dead;string label;protected Object(bool allocate=false){if(allocate){Fault.Touch();Fault.Live++;}}
  public static implicit operator bool(Object value)=>!ReferenceEquals(value,null)&&!value.dead;
  public string name{get=>label;set=>Fault.Set(ref label,value);}
  public static void Destroy(Object value){if(value){bool fail=value is GameObject&&Fault.ThrowDestroySession||value is Material&&Fault.ThrowDestroyMaterial||value is RenderTexture&&Fault.ThrowDestroyTexture;if(fail&&!Fault.ThrowDestroyAfter)throw new InvalidOperationException("Destroy refused");value.dead=true;Fault.Live--;if(fail)throw new InvalidOperationException("Destroy completed then threw");}}
 }
 public class GameObject:Object{public GameObject():base(true){}}
 public class AudioClip:Object{public AudioClip():base(true){}}
 public class AudioSource:Object{public AudioClip clip;public void Stop(){if(Fault.ThrowStop||Fault.ThrowAudioStop)throw new InvalidOperationException("Audio stop");}}
 public class Shader:Object{public bool isSupported=true;}
 public class Texture:Object{protected Texture(bool owned=false):base(owned){}}
 public class Texture2D:Texture{public static readonly Texture2D blackTexture=new Texture2D();}
 public enum FilterMode{Bilinear}public enum TextureWrapMode{Clamp}public enum RenderTextureFormat{ARGB32}
 public class Material:Object {
  Texture picture;public Material(Shader shader):base(true){}
  public Texture mainTexture{get=>picture;set=>Fault.Set(ref picture,value);}
 }
 public class RenderTexture:Texture {
  FilterMode filter;TextureWrapMode wrap;bool mip,auto,created;
  public RenderTexture(int w,int h,int d,RenderTextureFormat format):base(true){}
  public FilterMode filterMode{set=>Fault.Set(ref filter,value);}public TextureWrapMode wrapMode{set=>Fault.Set(ref wrap,value);}
  public bool useMipMap{set=>Fault.Set(ref mip,value);}public bool autoGenerateMips{set=>Fault.Set(ref auto,value);}
  public bool IsCreated()=>created;public bool Create(){Fault.Touch();return created=!Fault.RefuseCreate;}
  public void Release(){if(Fault.ThrowRelease)throw new InvalidOperationException("Release");created=false;}
 }
 public class Renderer:Object{public Material sharedMaterial;public bool enabled=true;}
 public static class Time{public static float unscaledTime;}
 public static class Mathf{public static int Clamp(int n,int low,int high)=>Math.Max(low,Math.Min(n,high));}
 public static class Debug{public static void LogWarning(string message,object owner=null){}}
}
namespace UnityEngine.Video {
 public class VideoPlayer:UnityEngine.Object{
  public delegate void FrameReadyEventHandler(VideoPlayer source,long frame);
  public event Action<VideoPlayer> prepareCompleted{add{}remove{if(Fault.ThrowEvent)throw new InvalidOperationException("Prepare detach");}}
  public event Action<VideoPlayer,string> errorReceived{add{}remove{if(Fault.ThrowEvent)throw new InvalidOperationException("Error detach");}}
  public event Action<VideoPlayer> loopPointReached{add{}remove{if(Fault.ThrowEvent)throw new InvalidOperationException("Loop detach");}}
  public event FrameReadyEventHandler frameReady{add{}remove{if(Fault.ThrowEvent)throw new InvalidOperationException("Frame detach");}}
  public bool sendFrameReadyEvents{set{if(Fault.ThrowEvent)throw new InvalidOperationException("Frame disable");}}
  public UnityEngine.RenderTexture targetTexture{set{if(Fault.ThrowDetach)throw new InvalidOperationException("Target detach");}}
  public void Stop(){if(Fault.ThrowStop)throw new InvalidOperationException("Video stop");}
 }
}
public class SlopCityCityScreen:UnityEngine.Object {
 public bool isActiveAndEnabled=true,AudioOnly,IsFeatured;public int Channel;public Renderer Display=new Renderer();
 public void Tune(int channel,Material material){Fault.Touch();Display.sharedMaterial=material;}
 public void Restore(){if(Fault.ThrowRestore)throw new InvalidOperationException("Restore");Display.sharedMaterial=null;}
}
public class SlopCityTikTokVideo{public string kind;}
public static class SlopCityMediaAudioFocus{public static void Remove(City city){if(Fault.ThrowPrelude)throw new InvalidOperationException("Focus");}public static void Remove(Lounge lounge){if(Fault.ThrowPrelude)throw new InvalidOperationException("Focus");}}
public class City {
 static City Instance;ScreenState audioOwner,desiredAudioOwner,mobilePendingOwner;float audioGain,focusedAudioGain;List<ScreenState> candidates=new List<ScreenState>();
 class Coroutines{public void DisposeAll(){if(Fault.ThrowPrelude)throw new InvalidOperationException("Dispose");}}Coroutines mediaCoroutines=new Coroutines();
 void StopAllCoroutines(){if(Fault.ThrowPrelude)throw new InvalidOperationException("Stop coroutines");}void RefreshMobileLoungePriority(bool a,bool b){if(Fault.ThrowPrelude)throw new InvalidOperationException("Priority");}
 static void StopPlayback(Channel signal){if(Fault.ThrowStop)throw new InvalidOperationException("Video stop");}
 const int ChannelCount=3;bool isActiveAndEnabled=true,MobileWebBudget;
 Shader screenShader=new Shader();List<SlopCityCityScreen> screens=new List<SlopCityCityScreen>();List<ScreenState> displays=new List<ScreenState>();
 Dictionary<SlopCityCityScreen,ScreenState> displayLookup=new Dictionary<SlopCityCityScreen,ScreenState>();Channel[] channels={new Channel(),new Channel(),new Channel()};
 class Channel{public ScreenState owner;public GameObject session;public object player;public AudioSource audio;public AudioClip photoAudio;public int photoGeneration,operation,imageIndex;public bool slideshow,resumeImage,audioLoading,resumeAudio,audioStarted,ready,loading,resolving,advance,hasScreen;public SlopCityTikTokVideo currentVideo,pendingVideo;public RenderTexture texture;public Material material;}class Media{}class ScreenState{public Channel slot;public SlopCityCityScreen screen;public int ordinal,generation,pausedVideoGeneration=-1;public Media fallback;public RenderTexture texture;public Material material;public string pausedVideoKey;public SlopCityTikTokVideo post;public bool needsThumbnail;}
 Media BundledMedia(int n)=>new Media();void AssignNextPost(ScreenState state){Fault.Touch();}
 static void Clear(RenderTexture texture){Fault.Touch();}static void ClearPausedVideo(ScreenState state){state.pausedVideoKey=null;}
 static string VideoKey(SlopCityTikTokVideo post)=>"video";
__CITY__
 void Release(){foreach(var state in displays){Destroy(state.material);DisposeScreenTexture(state.texture);}screens.Clear();displays.Clear();displayLookup.Clear();}
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 public static void Run(){foreach(bool mobile in new[]{false,true}){
  Fault.Arm();var city=new City{MobileWebBudget=mobile};city.RegisterScreen(new SlopCityCityScreen());int stages=Fault.Calls;
  Check(city.screens.Count==1&&city.displays.Count==1&&city.displayLookup.Count==1,"City admission");city.Release();Check(Fault.Live==0,"City normal teardown");
  foreach(bool after in new[]{false,true})for(int stage=1;stage<=stages;stage++)for(int repeat=0;repeat<50;repeat++){
   Fault.Arm(stage,after);city.RegisterScreen(new SlopCityCityScreen());Fault.Attempts++;
   Check(Fault.Live==0&&city.screens.Count==0&&city.displays.Count==0&&city.displayLookup.Count==0,"City failure owns/removes every allocation at "+stage);
  }
  Fault.Arm();Fault.RefuseCreate=true;city.RegisterScreen(new SlopCityCityScreen());if(!mobile)Check(Fault.Live==0&&city.screens.Count==0,"City Create false rollback");city.Release();
  Fault.Arm(3);Fault.ThrowRestore=true;bool failed=false;try{city.RegisterScreen(new SlopCityCityScreen());}catch(InvalidOperationException){failed=true;}Check(failed&&Fault.Live==0,"Restore failure cannot strand unregistered material");
  Fault.Arm();city.RegisterScreen(new SlopCityCityScreen());Check(city.screens.Count==1,"City retries successfully");city.Release();Check(Fault.Live==0,"City recovery teardown");
  Console.WriteLine("City "+(mobile?"mobile":"ordinary")+": "+stages+" failure stages, both mutation orders x50; restore failure and retry passed");
 }
  // ReleaseScreenTexture must retire its target even if material detachment or
  // native Release fails. The assignment before native work also makes retry safe.
  Fault.Arm();var owner=new City();owner.RegisterScreen(new SlopCityCityScreen());var state=owner.displays[0];Fault.Arm(1);Fault.ThrowRelease=true;
  try{ReleaseScreenTexture(state);}catch(InvalidOperationException){}Check(state.texture==null&&Fault.Live==1,"City detach/Release failure still destroys target");
  Fault.Arm();owner.Release();Check(Fault.Live==0,"City remaining material teardown");
  // Actual screen retirement must finish bookkeeping and disposal even when
  // Restore and native Stop fail in the same call. Phone sessions must retire.
  foreach(bool mobile in new[]{false,true})for(int repeat=0;repeat<100;repeat++){
   Fault.Arm();owner=new City{MobileWebBudget=mobile};owner.RegisterScreen(new SlopCityCityScreen());state=owner.displays[0];
   var slot=owner.channels[0];slot.owner=state;state.slot=slot;slot.audio=new AudioSource();slot.photoAudio=new AudioClip();slot.session=new GameObject();
   owner.audioOwner=owner.desiredAudioOwner=owner.mobilePendingOwner=state;owner.candidates.Add(state);
   Fault.ThrowRestore=Fault.ThrowRelease=Fault.ThrowStop=true;
   try{owner.RemoveScreen(state);}catch(InvalidOperationException){}
   Check(owner.screens.Count==0&&owner.displays.Count==0&&owner.displayLookup.Count==0&&owner.candidates.Count==0&&state.texture==null&&state.material==null&&state.slot==null&&slot.owner==null,"Actual RemoveScreen clears ownership despite native exceptions");
   Check(slot.photoAudio==null&&Fault.Live==(mobile?0:1),"RemoveScreen retires photo and phone decoder; ordinary decoder remains bounded channel-owned");
   owner.OnDestroy();Check(Fault.Live==0,"Actual teardown retires retained ordinary decoder");
  }
  // Failure of early cleanup hooks, every Restore, each audio/video Stop and
  // each native texture Release cannot prevent later displays from retiring.
  for(int repeat=0;repeat<100;repeat++){
   Fault.Arm();owner=new City();for(int i=0;i<3;i++)owner.RegisterScreen(new SlopCityCityScreen());
   foreach(var slot in owner.channels){slot.audio=new AudioSource();slot.photoAudio=new AudioClip();slot.session=new GameObject();}
   Fault.ThrowPrelude=Fault.ThrowRestore=Fault.ThrowStop=Fault.ThrowRelease=true;owner.OnDestroy();owner.OnDestroy();
   Check(Fault.Live==0&&owner.screens.Count==0&&owner.displays.Count==0&&owner.displayLookup.Count==0,"Actual OnDestroy continues across multiple simultaneous failures and is idempotent");
  }
  Console.WriteLine("Actual RemoveScreen/OnDestroy: 300 repeated simultaneous Stop/Restore/Release/early-hook failures; all owned resources retired");

 }
}
public class Lounge {
 Renderer screen=new Renderer();Shader screenShader=new Shader();Renderer[] screenLettering=new Renderer[0];
 bool capturedDisplay,originalScreenEnabled,MobileWebBudget=true,webHasPicture;bool[] originalLetteringEnabled;Material originalScreenMaterial,screenMaterial;RenderTexture screenTexture;
 UnityEngine.Video.VideoPlayer player;AudioSource audioSource;GameObject playbackObject;
 int webPlaybackGeneration;bool webAwaitingFirstPicture;float webPrepareSuspendedAt,tenantRecordReleaseUntil;static Lounge mobileDecoderOwner;
 UnityEngine.Video.VideoPlayer.FrameReadyEventHandler webFrameReadyHandler;
 bool IsTenantRecord=false;TenantPool tenantPool;SlopCityCityScreen tenantHomeScreen;
 class TenantPool:UnityEngine.Object{public void UnregisterScreen(SlopCityCityScreen screen){if(Fault.ThrowPrelude)throw new InvalidOperationException("Unregister");}}
 void OnPrepared(UnityEngine.Video.VideoPlayer source){}void OnError(UnityEngine.Video.VideoPlayer source,string error){}void OnEnded(UnityEngine.Video.VideoPlayer source){}
 void Fail(string message){}void SetDisplayVisible(bool visible){if(Fault.ThrowHide)throw new InvalidOperationException("Hide");}
__LOUNGE__
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 public static void Run(){
  var lounge=new Lounge();Fault.Arm();Check(lounge.EnsureDisplay(),"Lounge success");int stages=Fault.Calls;Check(Fault.Live==2,"Lounge owns target/material");lounge.ReleaseDisplay();lounge.ReleaseDisplay();Check(Fault.Live==0,"Lounge normal/idempotent teardown");
  foreach(bool after in new[]{false,true})for(int stage=1;stage<=stages;stage++)for(int repeat=0;repeat<50;repeat++){
   Fault.Arm(stage,after);Check(!lounge.EnsureDisplay(),"Lounge reports failed allocation");Fault.Attempts++;
   Check(Fault.Live==0&&lounge.screenTexture==null&&lounge.screenMaterial==null,"Lounge partial display rollback at "+stage);
  }
  Fault.Arm();Fault.RefuseCreate=true;Check(!lounge.EnsureDisplay()&&Fault.Live==0,"Lounge Create false rolls back");
  Fault.Arm();Check(lounge.EnsureDisplay(),"Lounge recovery");var oldTexture=lounge.screenTexture;var oldMaterial=lounge.screenMaterial;
  Fault.Arm(1);Check(lounge.EnsureDisplay()&&Fault.Calls==0&&ReferenceEquals(oldTexture,lounge.screenTexture)&&ReferenceEquals(oldMaterial,lounge.screenMaterial),"Usable prior picture needs no new allocations or setters");
  Fault.Arm();Destroy(lounge.screenMaterial);lounge.screenMaterial=null;Check(Fault.Live==1,"Existing retained target");
  for(int stage=1;stage<=3;stage++){Fault.Arm(stage,true);Check(!lounge.EnsureDisplay(),"Partial material retry fails");Check(Fault.Live==1&&ReferenceEquals(oldTexture,lounge.screenTexture)&&lounge.screenMaterial==null,"Material failure preserves previous picture");}
  Fault.Arm();Check(lounge.EnsureDisplay(),"Retained picture recovers material");Fault.ThrowHide=Fault.ThrowRelease=true;bool failed=false;try{lounge.ReleaseDisplay();}catch(InvalidOperationException){failed=true;}Check(!failed&&Fault.Live==0&&lounge.screenTexture==null&&lounge.screenMaterial==null,"Hide/Release exceptions retire all display resources without escaping");
  Fault.Arm();Check(lounge.EnsureDisplay(),"Full recovery after cleanup exception");lounge.ReleaseDisplay();Check(Fault.Live==0,"Final lounge teardown");
  for(int repeat=0;repeat<100;repeat++){
   Fault.Arm();lounge.NewPlayback();Check(lounge.EnsureDisplay(),"Playback display exists");
   Fault.ThrowStop=Fault.ThrowAudioStop=Fault.ThrowDetach=Fault.ThrowEvent=true;lounge.ReleasePlayback();
   Check(lounge.playbackObject==null&&lounge.player==null&&lounge.audioSource==null&&Fault.Live==2,"Actual ReleasePlayback retires owner despite simultaneous Stop/target/audio/event failures");
   lounge.ReleaseDisplay();Check(Fault.Live==0,"Remaining picture retires");
  }
  foreach(bool after in new[]{false,true})for(int repeat=0;repeat<100;repeat++){
   Fault.Arm();lounge.NewPlayback();Fault.ThrowDestroySession=true;Fault.ThrowDestroyAfter=after;lounge.ReleasePlayback();
   Check(after?lounge.playbackObject==null&&Fault.Live==0:lounge.playbackObject!=null&&lounge.player!=null&&lounge.audioSource!=null&&Fault.Live==1,"Refused owner stays retryable; already-destroyed owner is cleared");
   Fault.Arm();lounge.ReleasePlayback();lounge.ReleasePlayback();Check(Fault.Live==0,"Exact refused playback owner retires on retry");
   Fault.Arm();Check(lounge.EnsureDisplay(),"Display recovery for refused destroy");Fault.ThrowDestroyMaterial=Fault.ThrowDestroyTexture=true;Fault.ThrowDestroyAfter=after;
   lounge.ReleaseDisplay();Check(after?lounge.screenMaterial==null&&lounge.screenTexture==null&&Fault.Live==0:lounge.screenMaterial!=null&&lounge.screenTexture!=null&&Fault.Live==2,"Display ownership is retained if native destruction is refused");
   Fault.Arm();lounge.ReleaseDisplay();lounge.ReleaseDisplay();Check(Fault.Live==0,"Exact refused display resources retire on retry");
  }
  for(int repeat=0;repeat<100;repeat++){
   Fault.Arm();lounge.NewPlayback();Check(lounge.EnsureDisplay(),"Destroy fixture owns picture");lounge.tenantPool=new TenantPool();
   Fault.ThrowPrelude=Fault.ThrowStop=Fault.ThrowAudioStop=Fault.ThrowDetach=Fault.ThrowEvent=Fault.ThrowHide=Fault.ThrowRelease=true;
   lounge.OnDestroy();lounge.OnDestroy();Check(Fault.Live==0&&lounge.playbackObject==null&&lounge.screenTexture==null&&lounge.screenMaterial==null,"Actual OnDestroy continues through focus/tenant/Stop/event/target/audio/hide/Release failures and remains idempotent");
  }
  Console.WriteLine("Lounge actual playback/display teardown: 400 repeated simultaneous/refused/after-mutation failures; exact owners retry and cleanup continues");
  Console.WriteLine("Lounge: "+stages+" failure stages, both mutation orders x50; prior-picture/Create-false/cleanup/recovery passed");
 }
 void NewPlayback(){playbackObject=new GameObject();player=new UnityEngine.Video.VideoPlayer();audioSource=new AudioSource();mobileDecoderOwner=this;}
}
public class Check {public static void Main(){City.Run();Lounge.Run();if(Fault.Live!=0)throw new Exception("Native resource leak");Console.WriteLine("Repeated failure attempts: "+Fault.Attempts+"; no Unity/browser/native RAM claim");}}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mono', type=Path, required=True)
    parser.add_argument('--lounge-source', type=Path, default=SOURCE / 'SlopCityMediaPlayer.cs')
    args = parser.parse_args()
    mono = args.mono.expanduser().resolve()
    compiler = mono.parents[1] / 'lib/mono/4.5/csc.exe'
    if not mono.is_file() or not compiler.is_file():
        parser.error('Installed Unity Mono/Roslyn required.')
    city = (SOURCE / 'SlopCityCityMedia.cs').read_text()
    lounge = args.lounge_source.read_text()
    # The actual allocation entry refuses to replace a decoder whose Destroy
    # failed; the retained handles tested below remain reachable for retry.
    begin = block(lounge, 'private void BeginPlayback(')
    assert begin.index('if (playbackObject || player || audioSource)') < begin.index('new GameObject(')
    city_code = '\n'.join(block(city, signature) for signature in [
        'public void RegisterScreen(', 'private bool EnsureScreenTexture(',
        'private static void ReleaseScreenTexture(', 'private static void DisposeScreenTexture(',
        'private void RemoveScreen(', 'private void ReleaseSlot(', 'private static void ReleasePhoto(',
        'private static void ReleasePlayback(', 'private void OnDestroy(', 'private static void CleanupMedia('])
    lounge_code = '\n'.join(block(lounge, signature) for signature in [
        'private bool EnsureDisplay(', 'private void ReleasePlayback(', 'private void ReleaseDisplay(',
        'private static bool TryMediaCleanup(', 'private static void DisposeDisplayTexture(', 'private void OnDestroy('])
    source = STUBS.replace('__CITY__', city_code).replace('__LOUNGE__', lounge_code)
    with tempfile.TemporaryDirectory(prefix='slop-media-lifetime-') as temp:
        path, executable = Path(temp) / 'Check.cs', Path(temp) / 'Check.exe'
        path.write_text(source)
        for define in ['', '/define:UNITY_WEBGL']:
            subprocess.run([str(mono), str(compiler), '/nologo', '/out:' + str(executable),
                            *([define] if define else []), str(path)], check=True)
            subprocess.run([str(mono), str(executable)], check=True)


if __name__ == '__main__':
    main()
