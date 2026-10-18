#!/usr/bin/env python3
"""Execute production home-media dispatch and contact activation without Unity.

Deterministic substitutes check routing, identity and timing; this is not physics,
playback, browser gesture or physical-device acceptance.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile

from test_local_agents import block, MONO, MCS, SCRIPTS

STUBS = r'''
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public class Object {public static implicit operator bool(Object o)=>!ReferenceEquals(o,null);}
 public class Transform:Object {public string name="";public Transform parent;public bool IsChildOf(Transform root){for(var p=this;p!=null;p=p.parent)if(p==root)return true;return false;}public T GetComponentInParent<T>() where T:class=>null;}
 public class Camera:Object {}
 public static class Time {public static float time;}
}
namespace SlopCity {
 using UnityEngine;
 public enum SlopCityMediaChannel {Broadcast,Record,Duet}
 public class SlopCityMediaPlayer:Object {public Transform transform=new Transform();public int plays;public SlopCityMediaChannel channel;public void Play(SlopCityMediaChannel c){plays++;channel=c;}}
 public class SlopCityHud:Object {public int opens;public void ShowSourceTitleOnly(){opens++;}}
 public class SlopCityDoor:Object {public bool isActiveAndEnabled=true,IsMoving;public bool Toggle(Transform t)=>true;}
 public class SlopCitySandbox:Object {public bool IsDriving,IsTravelling,IsResting;public Transform target;public int dispatches;public Transform BrowserTarget(Camera c)=>target;public string DispatchContact(){dispatches++;return "Current job";}}
 public class SlopCityCrowd:Object {public static SlopCityCrowd Instance;public bool IsRedContact(int i)=>i==1||i==2;}
 public class SlopCitySignalBeams:Object {public int clears;public void Clear(){clears++;}}
 public class SlopCityFirstPerson:Object {
  enum NearbyAction {None,Sit,Television,Records,Door,Phone}
  bool seated,inspectedBroadcast,sourceOpen;public bool paused;
  public SlopCitySandbox Sandbox=new SlopCitySandbox();SlopCitySandbox sandbox;
  public SlopCityMediaPlayer media=new SlopCityMediaPlayer();public SlopCityHud hud=new SlopCityHud();
  SlopCityMediaChannel selectedChannel;Transform transform=new Transform(),televisionTarget,recordPlayerTarget,standingFeetAnchor;SlopCityDoor nearbyDoor;Camera viewCamera=new Camera();
  public SlopCityFirstPerson(){sandbox=Sandbox;var home=new Transform();televisionTarget=new Transform{parent=home};recordPlayerTarget=new Transform{parent=home};standingFeetAnchor=new Transform{parent=home};}
  void SetPaused(bool value){paused=value;}void RespawnAtCouch(){}void ShowFeedback(string text){}
  public string Aim(string label,bool home){sandbox.target=new Transform{name=label,parent=home?televisionTarget.parent:new Transform()};return FindNearbyAction().ToString();}
  public void Use(string action){PerformInteraction((NearbyAction)Enum.Parse(typeof(NearbyAction),action));}
  // FIRST_METHODS
 }
 public class SlopCityMatrixHunt {
  class Progress {public int contacts;}
  Progress progress=new Progress();HashSet<int> found=new HashSet<int>();public bool ready=true;float signalUntil,nextRoute,nextPresentation;bool showingJobRoute;
  public int saves,choices;public SlopCityFirstPerson player=new SlopCityFirstPerson();SlopCitySignalBeams beams=new SlopCitySignalBeams();
  void ChooseContact(){choices++;}void Save(){saves++;}static int Increment(int v)=>v<int.MaxValue?v+1:v;
  public int Contacts=>progress.contacts;
  // SIGNAL_PROPERTY
  // CONTACT_METHOD
 }
 public static class Checks {
  static int checks;static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
  public static void Main(){
   var player=new SlopCityFirstPerson();
   Check(player.Aim("TV_Screen",true)=="Television","home television is recognized");
   Check(player.Aim("Broadcast_TV_Display",true)=="Television","home broadcast display is recognized");
   Check(player.Aim("TV_Screen",false)=="None","city television does not become home media access");
   Check(player.Aim("Broadcast_TV_Display",false)=="None","city broadcast display does not become home media access");
   Check(player.Aim("Turntable",true)=="Records","home record player is recognized");
   Check(player.Aim("Turntable",false)=="None","other record props do not become the lounge player");
   player.Use("Television");Check(player.paused&&player.hud.opens==1,"TV use opens existing paused media selection");Check(player.media.plays==0,"TV use preserves current playback selection");
   var record=new SlopCityFirstPerson();record.Use("Records");Check(record.media.plays==1&&record.media.channel==SlopCityMediaChannel.Record,"record use selects exact bundled Record channel");Check(!record.paused&&record.hud.opens==0,"record use plays in world without opening selection");
   var hunt=new SlopCityMatrixHunt();SlopCityCrowd.Instance=new SlopCityCrowd();Time.time=10;
   Check(!hunt.SignalVisible,"no signal before contact");Check(!hunt.TryContact(0,out _)&&!hunt.SignalVisible,"ordinary resident cannot activate signal");
   Check(hunt.TryContact(1,out _)&&hunt.SignalVisible&&hunt.Contacts==1,"red contact activates signal and counts once");
   Time.time=15.99f;Check(hunt.SignalVisible,"signal remains within six active seconds");Time.time=16;Check(!hunt.SignalVisible,"signal expires at six seconds");
   Check(hunt.TryContact(1,out _)&&hunt.SignalVisible&&hunt.Contacts==1&&hunt.choices==1,"known contact reactivates without new discovery");
   Check(hunt.player.Sandbox.dispatches==2,"known contact still provides normal dispatch help");
   Time.time=22;Check(!hunt.SignalVisible,"reactivated signal also expires");
   Check(!hunt.TryContact(9,out _)&&!hunt.SignalVisible,"unrelated contact cannot revive expired signal");
   Check(hunt.TryContact(2,out _)&&hunt.Contacts==2&&hunt.SignalVisible,"another red contact works independently");
   hunt.ready=false;Check(!hunt.SignalVisible,"unready component never exposes signal");
   Console.WriteLine("PASS "+checks+" production physical-media/contact-signal checks; deterministic substitutes only");
  }
 }
}
'''


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--scripts-dir', type=Path, default=SCRIPTS)
    parser.add_argument('--mono', type=Path, default=MONO)
    parser.add_argument('--mcs', type=Path, default=MCS)
    args = parser.parse_args()
    scripts = args.scripts_dir
    first = (scripts / 'SlopCityFirstPerson.cs').read_text()
    hunt = (scripts / 'SlopCityMatrixHunt.cs').read_text()
    methods = '\n'.join(block(first, signature) for signature in [
        '        private NearbyAction FindNearbyAction()',
        '        private void PerformInteraction(',
        '        private void ShowMediaControls()',
        '        public void PlayMedia(',
    ])
    prop = next(line.strip() for line in hunt.splitlines() if 'public bool SignalVisible =>' in line)
    fixture = STUBS.replace('// FIRST_METHODS', methods).replace('// SIGNAL_PROPERTY', prop).replace('// CONTACT_METHOD', block(hunt, '        public bool TryContact('))
    # Protect absent entry points as well as exercising actual positive dispatch.
    for name in ['SlopCityPlayerInput.cs', 'SlopCityFirstPerson.cs', 'SlopCityHud.cs', 'SlopCityMatrixHunt.cs', 'SlopCityRabbitTrail.cs', 'SlopCityMobileControls.cs']:
        source = (scripts / name).read_text()
        for forbidden in ['KeyCode.M)', '.mKey.', 'KeyCode.T)', '.tKey.', 'RequestMedia(', 'ConsumeMedia(', 'ReplaySignal(', 'ReplayTransmission(', 'TryIntercept(', 'CanIntercept(', '"TouchMedia"', '"ReplaySignal"']:
            assert forbidden not in source, (name, forbidden)
    assert 'transmission.enabled = !paused && search && search.SignalVisible;' in (scripts / 'SlopCityHud.cs').read_text()
    assert 'if (!SignalVisible)' in hunt and 'if (!SignalVisible ||' in hunt
    assert '!signalOwner.SignalVisible' in (scripts / 'SlopCityRabbitTrail.cs').read_text()
    assert 'case SlopCityMediaChannel.Record: filename = "in-q-tel.mp4";' in (scripts / 'SlopCityMediaPlayer.cs').read_text()
    with tempfile.TemporaryDirectory(prefix='slop-physical-media-signal-') as directory:
        source = Path(directory) / 'Checks.cs'
        source.write_text(fixture)
        executable = Path(directory) / 'Checks.exe'
        subprocess.run([str(args.mono), str(args.mcs), '-langversion:latest', '-define:SLOP_CITY_WEB_INTERACTION_TEST', '-out:' + str(executable), str(source)], check=True)
        subprocess.run([str(args.mono), str(executable)], check=True)


if __name__ == '__main__':
    main()
