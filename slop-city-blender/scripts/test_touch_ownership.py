#!/usr/bin/env python3
"""Execute production touch owners with deterministic legacy Input snapshots.

Checks missed release/cancel and interrupted gameplay without an Editor or phone.
The touch components and input class are extracted verbatim, not reimplemented.
"""
from pathlib import Path
import argparse
import subprocess
import tempfile
from test_local_agents import block, MONO, MCS, SCRIPTS

STUBS = r'''
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
namespace UnityEngine {
 public class Object {public static implicit operator bool(Object o)=>!ReferenceEquals(o,null);}
 public class MonoBehaviour:Object {public T GetComponent<T>() where T:new()=>new T();}
 public class SerializeField:Attribute {}
 public static class Application {public static bool isMobilePlatform=true;}
 public static class Screen {public static int height=1000;}
 public enum TouchPhase {Began,Moved,Stationary,Ended,Canceled}
 public struct Touch {public int fingerId;public TouchPhase phase;}
 public enum KeyCode {A,D,W,S,E,R,M,Q,Space,Escape,C,F,I,LeftArrow,RightArrow,UpArrow,DownArrow,LeftShift,RightShift}
 public enum CursorLockMode {None,Locked}
 public static class Cursor {public static CursorLockMode lockState;}
 public static class Input {
  public static Touch[] Live=new Touch[0];public static int touchCount=>Live.Length;public static bool touchSupported=true;
  public static Touch GetTouch(int index)=>Live[index];public static bool GetKey(KeyCode key)=>false;
  public static bool GetKeyDown(KeyCode key)=>false;public static float GetAxisRaw(string axis)=>0;
 }
 public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static float Clamp(float v,float min,float max)=>Math.Max(min,Math.Min(max,v));}
 public struct Vector2 {
  public float x,y;public Vector2(float a,float b){x=a;y=b;}public static Vector2 zero=>new Vector2();
  public float sqrMagnitude=>x*x+y*y;public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
  public static Vector2 operator +(Vector2 a,Vector2 b)=>new Vector2(a.x+b.x,a.y+b.y);
  public static Vector2 operator -(Vector2 a,Vector2 b)=>new Vector2(a.x-b.x,a.y-b.y);
  public static Vector2 operator *(Vector2 a,float b)=>new Vector2(a.x*b,a.y*b);
  public static Vector2 operator /(Vector2 a,float b)=>a*(1/b);
  public static Vector2 ClampMagnitude(Vector2 v,float max)=>v.magnitude>max?v*(max/v.magnitude):v;
 }
 public struct Rect {public float width=>100;public float xMin=>-200;public float xMax=>200;public float yMin=>-200;public float yMax=>200;}
 public class RectTransform:Object {public Rect rect;public Vector2 anchoredPosition;}
 public static class RectTransformUtility {public static bool ScreenPointToLocalPointInRectangle(RectTransform r,Vector2 p,object camera,out Vector2 local){local=p;return true;}}
}
namespace UnityEngine.EventSystems {
 public interface IPointerDownHandler {void OnPointerDown(PointerEventData data);}
 public interface IPointerUpHandler {void OnPointerUp(PointerEventData data);}
 public interface IDragHandler {void OnDrag(PointerEventData data);}
 public class PointerEventData {public int pointerId;public Vector2 position,delta;public object pressEventCamera;}
}
namespace SlopCity {
 // ACTUAL_COMPONENTS
 public sealed class Rig {
  public readonly SlopCityPlayerInput input=new SlopCityPlayerInput();public readonly object owner;public readonly string kind;
  public Rig(string name){kind=name;input.SetGameplayEnabled(true);
   if(name=="stick"){var value=new SlopCityTouchStick();value.Configure(input,new RectTransform(),new RectTransform());owner=value;}
   else if(name=="look"){var value=new SlopCityTouchLook();value.Configure(input);owner=value;}
   else {var value=new SlopCityTouchHold();value.Configure(input);owner=value;}
  }
  public void Down(int id){((IPointerDownHandler)owner).OnPointerDown(Data(id,0));}
  public void Drag(int id){if(owner is IDragHandler drag)drag.OnDrag(Data(id,20));}
  public void Up(int id){((IPointerUpHandler)owner).OnPointerUp(Data(id,0));}
  public void Message(string name,params object[] args){owner.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(owner,args);}
  public int Pointer=>(int)owner.GetType().GetField("pointer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
  public bool Active(){if(kind=="look")return input.ReadLook().sqrMagnitude>0;if(kind=="stick")return input.ReadMovement().sqrMagnitude>0;return input.ReadSprint();}
  static PointerEventData Data(int id,float delta)=>new PointerEventData{pointerId=id,position=new Vector2(delta,delta),delta=new Vector2(delta,delta)};
 }
 public static class Checks {
  static int checks;
  static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
  static Touch Touch(int id,TouchPhase phase=TouchPhase.Stationary)=>new Touch{fingerId=id,phase=phase};
  static void InputMessage(Rig rig,string name,params object[] args){typeof(SlopCityPlayerInput).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(rig.input,args);}
  static void Begin(Rig rig,int id){rig.Down(id);rig.Drag(id);Check(rig.Pointer==id&&rig.Active(),rig.kind+" begins");}
  static void Queue(Rig rig){rig.input.RequestInteract();rig.input.RequestJump();rig.input.RequestInventory();rig.input.RequestPause();}
  static void Queued(Rig rig){Check(rig.input.ConsumeInteract(),rig.kind+" recovery preserves USE");Check(rig.input.ConsumeJump(),rig.kind+" recovery preserves JUMP");Check(rig.input.ConsumeInventory(),rig.kind+" recovery preserves BAG");Check(rig.input.ConsumePause(),rig.kind+" recovery preserves PAUSE");}
  public static void Main(){
   foreach(string kind in new[]{"look","stick","hold"}){
    var rig=new Rig(kind);Input.Live=new[]{Touch(11),Touch(22)};Begin(rig,11);
    // Reproduce the reported failure before requiring any newly added callback:
    // a missing owning release must not block the very next thumb press.
    Input.Live=new[]{Touch(22),Touch(33)};rig.Down(33);rig.Drag(33);
    Check(rig.Pointer==33&&rig.Active(),kind+" fresh press replaces absent owner while another thumb remains held");
    rig.Up(33);Input.Live=new[]{Touch(11),Touch(22)};Begin(rig,11);
    rig.Message("LateUpdate");Check(rig.Pointer==11,kind+" stationary live owner retained");
    rig.Down(22);rig.Drag(22);rig.Up(22);Check(rig.Pointer==11,kind+" live owner cannot be stolen or released by another thumb");
    rig.Drag(11);Check(rig.Active(),kind+" stationary owner still responds after second finger");
    // Missing pointer-up while another thumb/button remains on screen.
    Queue(rig);Input.Live=new[]{Touch(22)};rig.Message("LateUpdate");
    Check(rig.Pointer==int.MinValue&&!rig.Active(),kind+" missing exact owner released despite other live finger");Queued(rig);
    Input.Live=new[]{Touch(22),Touch(33)};Begin(rig,33);rig.Up(33);Check(rig.Pointer==int.MinValue&&!rig.Active(),kind+" normal release");
    // First fresh down must repair stale ownership even before LateUpdate runs.
    Input.Live=new[]{Touch(11),Touch(22)};Begin(rig,11);Input.Live=new[]{Touch(22),Touch(33)};Begin(rig,33);rig.Up(33);
    foreach(var phase in new[]{TouchPhase.Canceled,TouchPhase.Ended}){
     Input.Live=new[]{Touch(11),Touch(22)};Begin(rig,11);Input.Live=new[]{Touch(11,phase),Touch(22)};
     rig.Message("LateUpdate");Check(rig.Pointer==int.MinValue&&!rig.Active(),kind+" releases "+phase+" owner");
     Input.Live=new[]{Touch(33),Touch(22)};Begin(rig,33);rig.Up(33);
    }
    // Numeric finger IDs may be reused without an observed empty-touch frame.
    Input.Live=new[]{Touch(11),Touch(22)};Begin(rig,11);rig.Down(11);rig.Drag(11);Check(rig.Pointer==11&&rig.Active(),kind+" same-ID fresh down recovers");rig.Up(11);
    foreach(string reset in new[]{"pause-resume","focus","background","layout"}){
     Input.Live=new[]{Touch(11),Touch(22)};Begin(rig,11);
     if(reset=="pause-resume"){rig.input.SetGameplayEnabled(false);rig.input.SetGameplayEnabled(true);}
     else if(reset=="focus")InputMessage(rig,"OnApplicationFocus",false);
     else if(reset=="background")InputMessage(rig,"OnApplicationPause",true);
     else rig.input.ResetContinuousTouch();
     rig.Drag(11);rig.Message("LateUpdate");Check(rig.Pointer==int.MinValue&&!rig.Active(),kind+" "+reset+" invalidates existing drag even after resume");
     Input.Live=new[]{Touch(33),Touch(22)};Begin(rig,33);rig.Up(33);
    }
    foreach(string message in new[]{"OnDisable","OnApplicationFocus","OnApplicationPause"}){
     Input.Live=new[]{Touch(11),Touch(22)};Begin(rig,11);
     if(message=="OnDisable")rig.Message(message);else rig.Message(message,message=="OnApplicationPause");
     Check(rig.Pointer==int.MinValue&&!rig.Active(),kind+" releases on "+message);
    }
    // Mouse pointer IDs are not real touch identifiers and keep event ownership.
    Input.Live=new Touch[0];Begin(rig,-1);rig.Message("LateUpdate");Check(rig.Pointer==-1,kind+" mouse owner survives absent touch entries");rig.Down(-2);rig.Up(-2);Check(rig.Pointer==-1,kind+" second mouse pointer cannot steal");rig.Up(-1);
   }
   // Independent simultaneous thumbs plus a held RUN button; cancel only look.
   var input=new SlopCityPlayerInput();input.SetGameplayEnabled(true);var stick=new SlopCityTouchStick();stick.Configure(input,new RectTransform(),new RectTransform());
   var look=new SlopCityTouchLook();look.Configure(input);var hold=new SlopCityTouchHold();hold.Configure(input);
   Input.Live=new[]{Touch(1),Touch(2),Touch(3)};
   var left=new PointerEventData{pointerId=1,position=Vector2.zero};var right=new PointerEventData{pointerId=2,delta=new Vector2(15,10)};var run=new PointerEventData{pointerId=3};
   stick.OnPointerDown(left);left.position=new Vector2(20,20);stick.OnDrag(left);look.OnPointerDown(right);look.OnDrag(right);hold.OnPointerDown(run);
   Check(input.ReadMovement().sqrMagnitude>0&&input.ReadLook().sqrMagnitude>0&&input.ReadSprint(),"movement/look/RUN operate simultaneously");
   Input.Live=new[]{Touch(1),Touch(3)};typeof(SlopCityTouchLook).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(look,null);
   Input.Live=new[]{Touch(1),Touch(3),Touch(4)};right.pointerId=4;look.OnPointerDown(right);look.OnDrag(right);
   Check(input.ReadMovement().sqrMagnitude>0&&input.ReadLook().sqrMagnitude>0&&input.ReadSprint(),"look recovers while movement and RUN remain held");
   Console.WriteLine("PASS "+checks+" production input/touch ownership checks; deterministic Unity input snapshots, not physical-device acceptance");
  }
 }
}
'''


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--input-source',type=Path,default=SCRIPTS/'SlopCityPlayerInput.cs')
    parser.add_argument('--controls-source',type=Path,default=SCRIPTS/'SlopCityMobileControls.cs')
    parser.add_argument('--mono',type=Path,default=MONO)
    parser.add_argument('--mcs',type=Path,default=MCS)
    args=parser.parse_args()
    source=args.input_source.read_text();controls=args.controls_source.read_text()
    actual='\n'.join(block(source,'    public sealed class '+name) for name in ['SlopCityPlayerInput','SlopCityTouchStick','SlopCityTouchLook'])
    actual+='\n'+block(controls,'    public sealed class SlopCityTouchHold')
    with tempfile.TemporaryDirectory(prefix='slop-touch-ownership-') as directory:
        path=Path(directory);fixture=path/'Checks.cs';fixture.write_text(STUBS.replace('// ACTUAL_COMPONENTS',actual))
        for label,defines in [('Native','ENABLE_LEGACY_INPUT_MANAGER'),('Web','UNITY_WEBGL,ENABLE_LEGACY_INPUT_MANAGER')]:
            executable=path/(label+'.exe')
            subprocess.run([str(args.mono),str(args.mcs),'-langversion:latest','-define:'+defines,'-out:'+str(executable),str(fixture)],check=True)
            print(label,flush=True)
            subprocess.run([str(args.mono),str(executable)],check=True)


if __name__=='__main__':main()
