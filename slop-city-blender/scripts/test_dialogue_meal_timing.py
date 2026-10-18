#!/usr/bin/env python3
"""Execute actual NPC feedback, signal, meal and car-speed expressions in C#.

Managed substitutes prove routing/timing; they do not prove UI appearance, native
physics, audio, device stability or game-save acceptance.
"""
import argparse
from pathlib import Path
import re
import subprocess
import tempfile
from test_native_allocation_rollback import block

ROOT = Path(__file__).resolve().parents[1]
SCRIPTS = ROOT / 'Unity/Assets/Scripts'
HARNESS = r'''
using System;using System.Collections.Generic;using UnityEngine;using Object=UnityEngine.Object;using SlopCitySandbox=Sandbox;
namespace UnityEngine {
 public class Object{public static implicit operator bool(Object o)=>o!=null;}
 public class Transform:Object{public Vector3 position;}
 public class Camera:Object{public Transform transform=new Transform();}
 public struct Vector3{}
 public struct Vector2{public float y;}
 public static class Time{public static float time,unscaledTime;}
 public static class Mathf{public static float Max(float a,float b)=>Math.Max(a,b);public static float MoveTowards(float a,float b,float step)=>Math.Abs(b-a)<=step?b:a+Math.Sign(b-a)*step;}
}
class First {
 private string feedback;private float feedbackUntil,nextBrowserHudRefresh;
 __FIRST__
 public float Until=>feedbackUntil;public string Text=>feedback;public bool Visible=>Time.unscaledTime<feedbackUntil;
}
class SlopCityCrowd:Object {
 public static SlopCityCrowd Instance=new SlopCityCrowd();public bool IsResidentPresented(int i)=>true;public bool IsRedContact(int i)=>i==1;
 public bool TryTalk(int i,Vector3 point,out string line){line="Ordinary resident dialogue";return true;}
}
class SlopCitySteakTable{}
class SlopCitySignalBeams:Object{public void Clear(){}}
class Player:Object{public Sandbox Sandbox;}
class Hunt:Object {
 class Progress{public int contacts,meals;}
 private Progress progress=new Progress();private HashSet<int> found=new HashSet<int>();
 private SlopCitySandbox instructionSandbox;private object instructionAssignment;
 private bool ready=true,showingJobRoute;private float signalUntil,fedUntil,nextRoute,nextPresentation;
 private SlopCitySignalBeams beams=new SlopCitySignalBeams();public Player player=new Player();
 private void ChooseContact(){}private void Save(){}private void ReleaseDinnerGate(){}private void EnsureTarget(){}
 private static int Increment(int i)=>i+1;
 __HUNT__
 public bool Hint(bool paused){Hunt search=this;Sandbox sandbox=player.Sandbox;return __HINT__;}
}
class Sandbox:Object {
 class Target{public int npcIndex;public bool isNpc=true;}
 public bool HasWaypoint=true;public object CurrentJobAssignment=new object();
 public Hunt MatrixHunt;private Action<string> feedback,npcDialogue;private bool npcDialogueActive;
 private bool municipalOperationActive,persistenceUnavailable;private string municipalOperationMessage;
 private const string PersistenceNotice="Persistence unavailable";private float boostUntil;public float vehicleSpeed;
 public bool unique,throwUnique;private void AdvanceContract(){}private bool Save()=>true;
 public Sandbox(First view){feedback=view.ShowFeedback;npcDialogue=view.ShowNpcDialogue;}
 private bool TryUniqueResidentInteraction(Camera camera,int index,out bool completed){completed=false;if(throwUnique)throw new InvalidOperationException();if(unique)Say("Unique resident barter");return unique;}
 public bool Interact(int index){Target target=new Target{npcIndex=index};Camera camera=new Camera();__NPC__ return false;}
 public void SystemMessage(string message){Say(message);}
 public void Rest(){boostUntil=Time.time+45;}
 public string DispatchContact()=>"Follow the faint signal";
 public void Drive(float throttle,float dt){Vector2 axes=new Vector2{y=throttle};__DRIVE__}
 __SANDBOX__
}
static class Check {
 static int checks;static void Assert(bool b,string why){checks++;if(!b)throw new Exception(why);}
 static void Near(float a,float b,string why)=>Assert(Math.Abs(a-b)<.0001f,why);
 public static void Main(){
  Time.time=Time.unscaledTime=10;var view=new First();var box=new Sandbox(view);var hunt=new Hunt();hunt.player.Sandbox=box;box.MatrixHunt=hunt;
  box.SystemMessage("Normal");Near(view.Until,14.5f,"Ordinary feedback stays4.5 seconds");
  box.Interact(0);Near(view.Until,21.25f,"Every ordinary NPC dialogue lasts11.25 seconds");
  box.unique=true;box.Interact(0);Near(view.Until,21.25f,"Unique NPC branch uses extended dialogue");box.unique=false;
  box.Interact(1);Near(view.Until,21.25f,"Lady contact dialogue lasts2.5x");Assert(view.Text.Contains("CONTACT FOUND"),"Actual contact text delivered");
  for(int tick=0;tick<24;tick++){Time.time=10+tick*.25f;Time.unscaledTime=Time.time;Assert(hunt.SignalVisible&&hunt.Hint(false)&&view.Visible,"Hint/dialogue stay throughout entire six-second signal window");}
  Time.time=Time.unscaledTime=16;Assert(!hunt.SignalVisible&&hunt.Hint(false)&&view.Visible,"Beam expires at6 while current-job instruction and dialogue remain");
  Time.unscaledTime=21.249f;Assert(view.Visible,"NPC dialogue visible until11.25");Time.unscaledTime=21.25f;Assert(!view.Visible,"NPC dialogue expires exactly at11.25");
  Time.time=Time.unscaledTime=30;box.Interact(1);Assert(view.Text.Contains("KNOWN CONTACT")&&hunt.Hint(false),"Known Lady reactivates window and dialogue");Assert(!hunt.Hint(true),"Pause still hides hint");
  box.SystemMessage("After NPC");Near(view.Until,34.5f,"NPC scope never lengthens later system feedback");
  box.throwUnique=true;try{box.Interact(0);}catch(InvalidOperationException){}box.throwUnique=false;box.SystemMessage("After failure");Near(view.Until,34.5f,"Exception unwinds NPC feedback scope");
  Time.time=0;Near(hunt.MealSpeedMultiplier,1,"New session is unboosted");Near(box.SpeedMultiplier,1,"New walking/running baseline");
  box.Drive(1,100);Near(box.vehicleSpeed,13,"Unboosted forward speed");box.Drive(-1,100);Near(box.vehicleSpeed,-5,"Unboosted reverse speed");
  Time.time=10;hunt.OnSteakEaten(null);Near(hunt.MealSpeedMultiplier,2,"Meal doubles unboosted movement");Near(box.SpeedMultiplier,2,"Both walking/running multiplier doubles");
  box.Rest();Near(box.SpeedMultiplier,2,"Rest boost does not stack on meal");
  box.vehicleSpeed=0;box.Drive(1,1);Near(box.vehicleSpeed,18,"Meal doubles forward acceleration");box.Drive(1,100);Near(box.vehicleSpeed,26,"Meal doubles forward cap");
  box.vehicleSpeed=0;box.Drive(-1,.1f);Near(box.vehicleSpeed,-1.8f,"Meal doubles reverse acceleration");box.Drive(-1,100);Near(box.vehicleSpeed,-10,"Meal doubles reverse cap");
  box.vehicleSpeed=10;box.Drive(0,.5f);Near(box.vehicleSpeed,4,"Meal braking scales with speed");
  Time.time=100;hunt.OnSteakEaten(null);Near(hunt.MealSpeedMultiplier,2,"Repeated meal refreshes without stacking");
  Time.time=219.999f;Near(hunt.MealSpeedMultiplier,2,"Meal active until refreshed120 seconds");Time.time=220;Near(hunt.MealSpeedMultiplier,1,"Meal expires exactly at120 seconds");Near(box.SpeedMultiplier,1,"Walk/run expire to baseline");box.Drive(1,100);Near(box.vehicleSpeed,13,"Driving cap returns to baseline");
  var fresh=new Hunt();Near(fresh.MealSpeedMultiplier,1,"Fresh game never inherits meal boost");
  Console.WriteLine(checks+" checks: actual NPC routing/duration, full signal hint window, nonstacking120sec2x walk/run/car and car acceleration pass.");
 }
}
'''


def expression(source, name):
    return re.search(r'(?:public|private) (?:bool|float) ' + name + r' => ([^;]+);', source)[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', type=Path, required=True)
    args = parser.parse_args()
    mono = args.unity.resolve() / 'Contents/Resources/Scripting/MonoBleedingEdge/bin/mono'
    compiler = mono.parents[1] / 'lib/mono/4.5/csc.exe'
    first = (SCRIPTS / 'SlopCityFirstPerson.cs').read_text()
    sandbox = (SCRIPTS / 'SlopCitySandbox.cs').read_text()
    hunt = (SCRIPTS / 'SlopCityMatrixHunt.cs').read_text()
    hud = (SCRIPTS / 'SlopCityHud.cs').read_text()
    code = re.search(r'private const float FeedbackDuration[^;]+;', first)[0] + '\n' + '\n'.join(block(first, s) for s in ['public void ShowFeedback(', 'public void ShowNpcDialogue(', 'private void ShowFeedbackFor('])
    source = HARNESS.replace('__FIRST__', code)
    source = source.replace('__HUNT__', 'public bool SignalVisible => ' + expression(hunt, 'SignalVisible') + '; public float MealSpeedMultiplier => ' + expression(hunt, 'MealSpeedMultiplier') + ';\n' + block(hunt, 'public bool JobInstructionsVisible') + block(hunt, 'public bool TryContact(') + block(hunt, 'public string OnSteakEaten('))
    source = source.replace('__NPC__', block(block(sandbox, 'public bool Interact('), 'if (target.isNpc && SlopCityCrowd.Instance)'))
    source = source.replace('__SANDBOX__', block(sandbox, 'private void Say(') + '\nprivate float MealSpeedMultiplier => ' + expression(sandbox, 'MealSpeedMultiplier') + '; public float SpeedMultiplier => ' + expression(sandbox, 'SpeedMultiplier') + ';')
    drive = sandbox[sandbox.index('            float mealSpeed = MealSpeedMultiplier;'):sandbox.index('            Transform car =', sandbox.index('            float mealSpeed = MealSpeedMultiplier;'))]
    source = source.replace('__DRIVE__', drive).replace('__HINT__', re.search(r'transmission.enabled = ([^;]+);', hud)[1])
    assert 'sandbox.Initialize(transform, ShowFeedback, ShowNpcDialogue);' in first
    assert 'if (onNpcDialogue != null) npcDialogue = onNpcDialogue;' in block(sandbox, 'public void Initialize(')
    assert 'feedback = onFeedback; npcDialogue = onNpcDialogue;' in sandbox
    assert 'direction * (playerInput.ReadSprint() ? sprintingSpeed : walkingSpeed) * (sandbox ? sandbox.SpeedMultiplier : 1f)' in first
    assert 'MealSpeedMultiplier' not in (SCRIPTS / 'SlopCityMetroWorld.cs').read_text()
    with tempfile.TemporaryDirectory(prefix='slop-dialogue-meal-') as temp:
        path = Path(temp) / 'Check.cs'
        path.write_text(source)
        for mode, defines in [('native', []), ('browser', ['-define:UNITY_WEBGL'])]:
            target = Path(temp) / (mode + '.exe')
            subprocess.run([str(mono), str(compiler), '-nologo', '-nowarn:0169,0414,0649,0162', '-out:' + str(target), *defines, str(path)], check=True)
            subprocess.run([str(mono), str(target)], check=True)


if __name__ == '__main__':
    main()
