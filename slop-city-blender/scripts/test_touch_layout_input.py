#!/usr/bin/env python3
"""Execute the real input class and layout's real cancellation prefix.

Deterministic Unity substitutes verify queued actions and held state, not browser
pointer delivery or physical door operation. No Unity editor invocation is used.
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
namespace UnityEngine {
 public class Object { public static implicit operator bool(Object value)=>!ReferenceEquals(value,null); }
 public class MonoBehaviour:Object {}
 public class SerializeField:Attribute {}
 public static class Application { public static bool isMobilePlatform=false; }
 public static class Screen { public static int height=1000; }
 public static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b); }
 public struct Vector2 {
  public float x,y;public Vector2(float a,float b){x=a;y=b;}
  public static Vector2 zero=>new Vector2();public float sqrMagnitude=>x*x+y*y;
  public static Vector2 operator +(Vector2 a,Vector2 b)=>new Vector2(a.x+b.x,a.y+b.y);
  public static Vector2 operator *(Vector2 a,float b)=>new Vector2(a.x*b,a.y*b);
  public static Vector2 ClampMagnitude(Vector2 v,float maximum){float n=(float)Math.Sqrt(v.sqrMagnitude);return n>maximum?v*(maximum/n):v;}
 }
}
namespace UnityEngine.EventSystems {}
namespace SlopCity {
 using UnityEngine;
 public sealed class SlopCityTouchStick {
  readonly SlopCityPlayerInput input;public int cancellations;
  public SlopCityTouchStick(SlopCityPlayerInput value){input=value;}
  public void CancelGesture(){cancellations++;input.SetTouchMovement(Vector2.zero);}
 }
 public sealed class SlopCityTouchLook {public int cancellations;public void CancelGesture(){cancellations++;}}
 public sealed class SlopCityTouchHold {
  readonly SlopCityPlayerInput input;public int cancellations;
  public SlopCityTouchHold(SlopCityPlayerInput value){input=value;}
  public void CancelGesture(){cancellations++;input.RequestSprint(false);}
 }
 // ACTUAL_INPUT
 public sealed class LayoutHarness {
  readonly SlopCityPlayerInput input;
  public readonly SlopCityTouchStick stick;public readonly SlopCityTouchLook look=new SlopCityTouchLook();public readonly SlopCityTouchHold hold;
  public LayoutHarness(SlopCityPlayerInput value){input=value;stick=new SlopCityTouchStick(value);hold=new SlopCityTouchHold(value);}
  T[] GetComponentsInChildren<T>() {
   if(typeof(T)==typeof(SlopCityTouchStick))return new[]{(T)(object)stick};
   if(typeof(T)==typeof(SlopCityTouchLook))return new[]{(T)(object)look};
   if(typeof(T)==typeof(SlopCityTouchHold))return new[]{(T)(object)hold};
   throw new Exception("Unexpected gesture type");
  }
  public void Apply(){ // ACTUAL_LAYOUT_RESET
  }
 }
 public static class Checks {
  static int checks;
  static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
  static void QueueAll(SlopCityPlayerInput input){input.RequestInteract();input.RequestJump();input.RequestRespawn();input.RequestNewContract();input.RequestPause();input.SetTouchMovement(new Vector2(1,.5f));input.AddTouchLook(new Vector2(20,30));input.RequestSprint(true);}
  static void ContinuousClear(SlopCityPlayerInput input,string label){Check(input.ReadMovement().sqrMagnitude==0,label+" movement");Check(input.ReadLook().sqrMagnitude==0,label+" look");Check(!input.ReadSprint(),label+" sprint");}
  static void ActionsClear(SlopCityPlayerInput input,string label){Check(!input.ConsumeInteract(),label+" use");Check(!input.ConsumeJump(),label+" jump");Check(!input.ConsumeRespawn(),label+" home");Check(!input.ConsumeNewContract(),label+" job");Check(!input.ConsumePause(),label+" pause");}
  static void Message(SlopCityPlayerInput input,string method,params object[] arguments){typeof(SlopCityPlayerInput).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(input,arguments);}
  public static void Main(){
   var input=new SlopCityPlayerInput();input.SetGameplayEnabled(true);QueueAll(input);
   Check(input.ReadMovement().sqrMagnitude>0&&input.ReadSprint(),"held input initially active");
   var layout=new LayoutHarness(input);layout.Apply();
   Check(input.GameplayEnabled,"layout preserves enabled state");
   Check(layout.stick.cancellations==1&&layout.look.cancellations==1&&layout.hold.cancellations==1,"layout cancels every held gesture owner");
   Check(input.ConsumeInteract(),"completed USE survives resize");Check(!input.ConsumeInteract(),"USE consumed once");
   Check(input.ConsumeJump(),"completed JUMP survives resize");Check(!input.ConsumeJump(),"JUMP consumed once");
   Check(input.ConsumeRespawn(),"completed HOME survives resize");Check(!input.ConsumeRespawn(),"HOME consumed once");

   Check(input.ConsumeNewContract(),"completed JOB survives resize");Check(!input.ConsumeNewContract(),"JOB consumed once");
   Check(input.ConsumePause(),"completed PAUSE survives resize");Check(!input.ConsumePause(),"PAUSE consumed once");
   ContinuousClear(input,"layout");layout.Apply();ActionsClear(input,"repeat layout adds no actions");
   QueueAll(input);input.SetGameplayEnabled(false);Check(!input.GameplayEnabled,"pause disables gameplay");
   input.SetGameplayEnabled(true);ActionsClear(input,"pause clears pending actions");ContinuousClear(input,"pause");
   QueueAll(input);input.SetGameplayEnabled(true);ActionsClear(input,"same-state SetGameplayEnabled retains full clearing contract");ContinuousClear(input,"same-state reset");
   foreach(string method in new[]{"OnApplicationFocus","OnApplicationPause","OnDisable"}){
    QueueAll(input);if(method=="OnApplicationFocus")Message(input,method,false);else if(method=="OnApplicationPause")Message(input,method,true);else Message(input,method);
    ActionsClear(input,method);ContinuousClear(input,method);
   }
   input.SetGameplayEnabled(false);input.RequestInteract();input.RequestJump();input.RequestRespawn();input.RequestNewContract();
   layout.Apply();Check(!input.GameplayEnabled,"layout never enables paused gameplay");input.SetGameplayEnabled(true);ActionsClear(input,"inactive actions never reappear");
   Console.WriteLine("PASS "+checks+" actual input/layout checks; Unity substitutes, no browser or door-operation validation");
  }
 }
}
'''


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--input-source', type=Path, default=SCRIPTS / 'SlopCityPlayerInput.cs')
    parser.add_argument('--controls-source', type=Path, default=SCRIPTS / 'SlopCityMobileControls.cs')
    parser.add_argument('--mono', type=Path, default=MONO)
    parser.add_argument('--mcs', type=Path, default=MCS)
    parser.add_argument('--unity-ui', type=Path, default=SCRIPTS.parents[1] / 'Library/ScriptAssemblies/UnityEngine.UI.dll')
    args = parser.parse_args()
    input_source = args.input_source.read_text()
    controls_source = args.controls_source.read_text()
    actual_input = block(input_source, '    public sealed class SlopCityPlayerInput')
    layout = block(controls_source, '        private void LayoutForViewport(')
    # Execute the exact production cancellation statements before any UI geometry.
    reset = layout[layout.index('{') + 1:layout.index('            lastSafe =')]
    fixture = STUBS.replace('// ACTUAL_INPUT', actual_input).replace('// ACTUAL_LAYOUT_RESET', reset)
    with tempfile.TemporaryDirectory(prefix='slop-touch-layout-') as directory:
        path = Path(directory)
        (path / 'Checks.cs').write_text(fixture)
        for label, defines in [('Native', []), ('Web', ['-define:UNITY_WEBGL'])]:
            executable = path / (label + '.exe')
            subprocess.run([str(args.mono), str(args.mcs), '-langversion:latest', *defines,
                            '-out:' + str(executable), str(path / 'Checks.cs')], check=True)
            print(label, flush=True)
            subprocess.run([str(args.mono), str(executable)], check=True)
        # Compile both complete production components against the pinned engine.
        # Stub only unrelated rendering/salvage providers, leaving real touch,
        # event-system, rect-transform and UI contracts checked by Unity DLLs.
        (path / 'Presentation.cs').write_text(r'''namespace SlopCity {
 public static class SlopCityWebPresentation { public static float DisplayPixelRatio => 1f; }
 public sealed class SlopCityFirstPerson : UnityEngine.MonoBehaviour { public SlopCitySandbox Sandbox; public UnityEngine.Camera ViewCamera; }
 public sealed class SlopCitySandbox : UnityEngine.Object { public void SalvageOptions(UnityEngine.Camera camera,out bool clean,out bool take,out int strokes) { clean=take=false;strokes=0; } }
 public static class SlopCitySalvageItem { public const int CleaningStrokes=4; }
}''')
        contents = args.mono.resolve().parents[4]
        references = list((contents / 'Resources/Scripting/Managed/UnityEngine').glob('*.dll'))
        references.append(contents / 'Resources/Scripting/MonoBleedingEdge/lib/mono/unityjit-macos/Facades/netstandard.dll')
        references.append(args.unity_ui)
        for label, defines in [('Native', ['-define:ENABLE_LEGACY_INPUT_MANAGER']),
                               ('Web', ['-define:UNITY_WEBGL,ENABLE_LEGACY_INPUT_MANAGER'])]:
            subprocess.run([str(args.mono), str(args.mcs), '-langversion:latest', '-target:library',
                            *defines, '-out:' + str(path / (label + 'Components.dll')),
                            *['-r:' + str(reference) for reference in references],
                            str(args.input_source), str(args.controls_source),
                            str(path / 'Presentation.cs')], check=True)
        print('PASS complete PlayerInput and MobileControls compile against pinned Native/Web Unity API')


if __name__ == '__main__':
    main()
