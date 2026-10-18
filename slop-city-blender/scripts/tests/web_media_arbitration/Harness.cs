using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Video;
using SlopCity;
class Harness {
 static int checks;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 static BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Field(object o,string name){return o.GetType().GetField(name,F).GetValue(o);}
 static void Call(object o,string name){o.GetType().GetMethod(name,F).Invoke(o,null);}
 static int Live(){return UnityEngine.Object.All.OfType<VideoPlayer>().Count(v=>v);}
 static void Refresh(SlopCityMediaPlayer m){Call(m,"RefreshAudioInterest");Call(m,"RefreshWebInterest");}
 static SlopCityMediaPlayer Setup(bool touch,out GameObject controller,out SlopCityCityMedia city,out Renderer screen){
  UnityEngine.Object.All.Clear();Physics.Hits=new RaycastHit[0];GeometryUtility.Visible=true;Time.unscaledTime=0;
  controller=new GameObject("Daniel_FirstPerson");controller.AddComponent<SlopCityPlayerInput>().UseTouchControls=touch;
  var camera=new GameObject("Camera");camera.transform.SetParent(controller.transform,false);Camera.main=camera.AddComponent<Camera>();
  var home=new GameObject("Lounge");var tv=new GameObject("TV");tv.transform.SetParent(home.transform,false);tv.transform.position=new Vector3(0,0,4);
  screen=tv.AddComponent<Renderer>();screen.bounds=new Bounds{center=tv.transform.position};
  var record=new GameObject("Records");record.transform.SetParent(home.transform,false);record.transform.position=new Vector3(-2,0,3);
  var media=controller.AddComponent<SlopCityMediaPlayer>();controller.AddComponent<SlopCityFirstPerson>().Media=media;
  media.Configure(screen,new Renderer[0],tv.transform,record.transform,new Shader());
  city=new GameObject("City").AddComponent<SlopCityCityMedia>();SlopCityCityMedia.Instance=city;city.SetViewer(controller.transform);city.MobileWebBudget=touch;
  media.Play(SlopCityMediaChannel.Broadcast);return media;
 }
 static void CheckPriorityStability(){
  GameObject controller;SlopCityCityMedia city;Renderer screen;var media=Setup(true,out controller,out city,out screen);
  var original=Field(media,"player");
  city.SampleAim(true);
  Check(Live()==1&&original==Field(media,"player"),"Brief city aim must not churn lounge decoder before confirmation");
  Time.unscaledTime=.2f;city.SampleAim(false);Refresh(media);Call(media,"RefreshMobileDecoderOwnership");
  Time.unscaledTime=.4f;city.SampleAim(true);Time.unscaledTime=.6f;city.SampleAim(false);
  Check(Live()==1&&original==Field(media,"player")&&UnityEngine.Object.All.OfType<VideoPlayer>().Count()==1,"Repeated sub-confirmation aim changes retain the original lounge decoder");
  Time.unscaledTime=1;city.SampleAim(true);Time.unscaledTime=1.399f;city.SampleAim(true);
  Check(Live()==1&&original==Field(media,"player"),"Deliberate city focus keeps existing owner until the .4 second confirmation");
  Time.unscaledTime=1.4f;city.SampleAim(true);
  Check(Live()==0&&(bool)Field(media,"webCityPriority")&&!SlopCityMediaPlayer.MobileDecoderInUse,"Deliberate city focus wins at .4 seconds without overlapping native decoders");
  Check(Math.Abs(city.BindAdmission()-1.65f)<.0001f,"Confirmed city handoff retains the additional .25 second native-retirement barrier");
  Time.unscaledTime=1.7f;city.AddCityDecoder();var held=UnityEngine.Object.All.OfType<VideoPlayer>().Single(v=>v);
  for(int i=0;i<6;i++){
   Time.unscaledTime=1.8f+i*.2f;city.SampleAim(i%2!=0);Refresh(media);Call(media,"RefreshMobileDecoderOwnership");
   Check(Live()==1&&held&&UnityEngine.Object.All.OfType<VideoPlayer>().Count()==2,"Walking aim flicker cannot repeatedly rebuild city or lounge decoders");
  }
  Time.unscaledTime=3;city.SampleAim(false);Time.unscaledTime=3.79f;city.SampleAim(false);
  Refresh(media);Call(media,"RefreshMobileDecoderOwnership");
  Check(Live()==1&&held&&(bool)Field(media,"webCityPriority"),"Brief city focus loss retains confirmed city ownership for less than .8 seconds");
  Time.unscaledTime=3.8f;city.SampleAim(false);Refresh(media);Call(media,"RefreshMobileDecoderOwnership");
  Check(Live()==0&&!held&&SlopCityMediaPlayer.MobileDecoderInUse&&media.IsLoading,"A sustained .8 second focus loss releases city once and reserves returning lounge");
  Time.unscaledTime=4.1f;Call(media,"RefreshMobileDecoderOwnership");
  Check(Live()==1&&media.IsPlaying&&UnityEngine.Object.All.OfType<VideoPlayer>().Count()==3,"Confirmed return constructs exactly one selected lounge decoder after retirement");
  // Authoritative pause/focus/disable paths use this same immediate reset.
  city.Aim(true);Time.unscaledTime=4.2f;city.SampleAim(false);city.CancelPriority();
  Check(!(bool)Field(media,"webCityPriority"),"Suspension reset clears confirmed priority immediately without gaze grace");
  Time.unscaledTime=4.3f;city.SampleAim(true);Time.unscaledTime=4.6f;city.SampleAim(true);
  Check(!(bool)Field(media,"webCityPriority"),"Suspension reset removes old pending timestamps before resumed focus");
  Time.unscaledTime=4.7f;city.SampleAim(true);
  Check((bool)Field(media,"webCityPriority"),"Resumed deliberate focus confirms within a fresh .4 seconds");
 }
 static void Main(){
  GameObject controller;SlopCityCityMedia city;Renderer screen;var media=Setup(true,out controller,out city,out screen);
  Check(Live()==1&&SlopCityMediaPlayer.MobileDecoderInUse&&media.IsPlaying,"Initial lounge holds exactlyone playing phone decoder");
  var texture=media.ScreenTexture;
  Camera.main.transform.position=new Vector3(0,0,-10);
  var wall=new GameObject("Home_Wall");Physics.Hits=new[]{new RaycastHit{transform=wall.transform}};
  Refresh(media);Check(!(bool)Field(media,"webRelevant")&&(float)Field(media,"audioScore")==0,"Occluded nearby TV must not reserve city decoder");
  Call(media,"RefreshMobileDecoderOwnership");Time.unscaledTime=3;Call(media,"RefreshMobileDecoderOwnership");
  Check(Live()==0&&!SlopCityMediaPlayer.MobileDecoderInUse&&media.HasSelection&&media.ScreenTexture==texture,"Hidden TV retirement keeps selected channel and last world picture");
  Camera.main.transform.position=new Vector3();Physics.Hits=new RaycastHit[0];GeometryUtility.Visible=false;Camera.main.transform.forward=Vector3.back;
  Refresh(media);Check((bool)Field(media,"webRelevant")&&(float)Field(media,"audioScore")>0,"Retired decoder can regain couch eligibility while looking away");
  Call(media,"RefreshMobileDecoderOwnership");Check(Live()==1&&media.IsPlaying,"Returning couch reconstructs one selected decoder");
  city.Aim(true);Check(Live()==0&&!SlopCityMediaPlayer.MobileDecoderInUse&&(bool)Field(media,"webCityPriority")&&media.ScreenTexture==texture,"Direct city aim preempts nearby lounge and retains TV image");
  Time.unscaledTime=10;float admit=city.BindAdmission();Check(Math.Abs(admit-10.25f)<.0001f&&10<admit&&10.2f<admit,"Actual mobile BindSlot defers new decoder preparation beyond sameframe and next0.2s interest tick");
  city.AddCityDecoder();Refresh(media);Call(media,"RefreshMobileDecoderOwnership");Check(Live()==1&&!SlopCityMediaPlayer.MobileDecoderInUse,"Stable city aim suppresses automatic nearbyTV reacquisition");
  media.Play(SlopCityMediaChannel.Record);Check(Live()==1&&media.SelectedChannel==SlopCityMediaChannel.Record&&media.HasSelection,"Channel controls retain selection without stealing city decoder");
  city.Aim(false);Refresh(media);Call(media,"RefreshMobileDecoderOwnership");
  Check(Live()==0&&SlopCityMediaPlayer.MobileDecoderInUse&&media.IsLoading,"Returning lounge reserves sole slot without constructing decoder during city releaseframe");
  Time.unscaledTime=10.2f;Call(media,"RefreshMobileDecoderOwnership");Check(Live()==0,"Returning lounge still waits before .25s admission deadline");
  Time.unscaledTime=10.3f;Call(media,"RefreshMobileDecoderOwnership");Check(Live()==1&&SlopCityMediaPlayer.MobileDecoderInUse&&media.IsPlaying&&media.SelectedChannel==SlopCityMediaChannel.Record,"Releasing city aim returns one decoder to selected couch channel after retirement barrier");
  var home=screen.transform.parent;var upholstery=new GameObject("SC_Couch_Blanket");upholstery.transform.SetParent(home,false);Physics.Hits=new[]{new RaycastHit{transform=upholstery.transform}};
  Refresh(media);Check((float)Field(media,"audioScore")>0,"Existing couch upholstery exemption still preserves nearby record audio");
  media.SetSuspended(true);Refresh(media);Check((float)Field(media,"audioScore")==0,"Interface suspension keeps media ineligible");
  media.Stop();Check(Live()==0&&!SlopCityMediaPlayer.MobileDecoderInUse,"Stop clears single-owner lifecycle");
  media=Setup(false,out controller,out city,out screen);media.SetMobileCityPriority(true);
  Check(Live()==1&&media.IsPlaying&&!(bool)Field(media,"webCityPriority"),"Desktop retains playing decoder when mobile priority requested");
  city.Aim(true);Check(Live()==1&&media.IsPlaying,"Desktop city path preserves previous allocation policy");
  Time.unscaledTime=10;Check(city.BindAdmission()==0,"Actual desktop BindSlot keeps previous immediate admission policy");
  VideoPlayer.DelayPrepare=true;media=Setup(true,out controller,out city,out screen);
  GeometryUtility.Visible=false;Camera.main.transform.forward=Vector3.back;
  var pending=UnityEngine.Object.All.OfType<VideoPlayer>().Single(v=>v);
  Time.unscaledTime=3;Call(media,"Update");Time.unscaledTime=6;Call(media,"Update");
  Check(Live()==1&&media.IsLoading&&pending==Field(media,"player")&&(bool)Field(media,"webRelevant"),"Slow couch preparation while looking away keeps one original eligible decoder");
  pending.CompletePrepare();Check(media.IsPlaying&&Live()==1,"Slow couch preparation completes and plays without retirement/restart churn");
  VideoPlayer.DelayPrepare=false;
  media=Setup(true,out controller,out city,out screen);city.Aim(true);city.AddCityDecoder();city.Aim(false);Refresh(media);Call(media,"RefreshMobileDecoderOwnership");
  media.TogglePause();Time.unscaledTime=1;Call(media,"Update");
  Check(Live()==0&&!SlopCityMediaPlayer.MobileDecoderInUse&&media.IsPaused,"Pause cancels waiting admission without stale decoder construction");
  media.TogglePause();Refresh(media);Call(media,"RefreshMobileDecoderOwnership");Check(Live()==1&&media.IsPlaying,"Explicit resume can reacquire selected channel after cancellation");
  city.Aim(true);city.AddCityDecoder();city.Aim(false);Refresh(media);Call(media,"RefreshMobileDecoderOwnership");
  media.Play(SlopCityMediaChannel.Duet);Check(Live()==0&&media.IsLoading,"Replacing pending channel retains release deadline");
  Time.unscaledTime=2;Call(media,"Update");Check(Live()==1&&media.IsPlaying&&media.SelectedChannel==SlopCityMediaChannel.Duet,"Pending channel replacement starts only current selection");
  city.Aim(true);city.AddCityDecoder();city.Aim(false);Refresh(media);Call(media,"RefreshMobileDecoderOwnership");media.SetSuspended(true);Time.unscaledTime=3;Call(media,"Update");
  Check(Live()==0&&!SlopCityMediaPlayer.MobileDecoderInUse,"Suspending menu cancels pending admission");media.SetSuspended(false);Refresh(media);Call(media,"RefreshMobileDecoderOwnership");
  city.Aim(true);city.AddCityDecoder();city.Aim(false);Refresh(media);Call(media,"RefreshMobileDecoderOwnership");media.Stop();Time.unscaledTime=4;Call(media,"Update");
  Check(Live()==0&&!media.HasSelection&&!SlopCityMediaPlayer.MobileDecoderInUse,"Stop cancels pending admission and clears selection");
  media=Setup(true,out controller,out city,out screen);city.Aim(true);city.AddCityDecoder();city.Aim(false);Refresh(media);Call(media,"RefreshMobileDecoderOwnership");
  media.GetType().GetMethod("OnApplicationFocus",F).Invoke(media,new object[]{false});Time.unscaledTime=1;Call(media,"Update");
  Check(Live()==0&&!SlopCityMediaPlayer.MobileDecoderInUse,"Focus loss cancels pending admission");
  media=Setup(true,out controller,out city,out screen);city.Aim(true);city.AddCityDecoder();city.Aim(false);Refresh(media);Call(media,"RefreshMobileDecoderOwnership");
  media.GetType().GetMethod("OnApplicationPause",F).Invoke(media,new object[]{true});Time.unscaledTime=1;Call(media,"Update");
  Check(Live()==0&&!SlopCityMediaPlayer.MobileDecoderInUse,"Application pause cancels pending admission");
  media=Setup(true,out controller,out city,out screen);city.Aim(true);city.AddCityDecoder();city.Aim(false);Refresh(media);Call(media,"RefreshMobileDecoderOwnership");
  city.Aim(true);Time.unscaledTime=1;Call(media,"Update");
  Check(Live()==0&&!SlopCityMediaPlayer.MobileDecoderInUse&&media.HasSelection,"Re-aiming city cancels pending lounge decoder and keeps selection");
  media=Setup(true,out controller,out city,out screen);city.Aim(true);city.AddCityDecoder();city.Aim(false);Refresh(media);Call(media,"RefreshMobileDecoderOwnership");
  Call(media,"OnDisable");Time.unscaledTime=1;Call(media,"Update");
  Check(Live()==0&&!SlopCityMediaPlayer.MobileDecoderInUse&&!media.HasSelection,"Disabling owner cancels pending admission");
  CheckPriorityStability();
  Console.WriteLine("{\"passed\":true,\"checks\":"+checks+",\"scope\":\"Complete Web MediaPlayer plus actual City priority, admission and release methods; managed Unity substitutes, no renderer/browser\"}");
 }
}
