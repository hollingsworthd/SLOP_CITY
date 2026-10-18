#!/usr/bin/env python3
"""Compile actual MediaPlayer and extracted City arbitration with managed Unity substitutes.
No Unity editor, browser, renderer or native decoder is run. A separate native-defined
compilation checks the shared source's conditional branches. See the fixture README.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = ROOT / 'scripts/tests/web_media_arbitration'
PROJECT = next((parent for parent in [ROOT, *ROOT.parents] if (parent / '.tools/Unity-6000.3.26f1').is_dir()), ROOT.parent)


def extract_method(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == '{':
            depth += 1
        elif source[index] == '}':
            depth -= 1
            if depth == 0:
                return source[start:index + 1]
    raise AssertionError('Unclosed method: ' + signature)


PAUSE_HARNESS = r'''
using System; using System.Collections.Generic; using UnityEngine; using UnityEngine.Video;
namespace UnityEngine {
 public static class Mathf {public static int Min(int left,int right)=>Math.Min(left,right);}
 public class Texture {
  public bool released,destroyed;
  public static implicit operator bool(Texture value)=>value!=null&&!value.destroyed;
 }
 public sealed class RenderTexture:Texture {public void Release(){released=true;}}
 public sealed class Texture2D:Texture {public static Texture blackTexture=new Texture();}
 public sealed class Material {
  public Texture mainTexture;public static implicit operator bool(Material value)=>value!=null;
 }
}
namespace UnityEngine.Video {
 public sealed class VideoPlayer {
  public bool isPrepared=true,canSetTime=true;public double time,length=42.042;
  public static implicit operator bool(VideoPlayer value)=>value!=null;
 }
}
public sealed class SlopCityTikTokVideo {public string creator="owner",id="selected",kind="video";}
public sealed class SlopCityCityScreen {public bool AudioOnly;public static implicit operator bool(SlopCityCityScreen value)=>value!=null;}
public class Harness {
 private sealed class ScreenState {
  public SlopCityCityScreen screen=new SlopCityCityScreen();public SlopCityTikTokVideo post;public Channel slot;public int generation=7;
  public bool paused;public string pausedVideoKey;public double pausedVideoTime;
  public int pausedVideoGeneration=-1;
  public Material material=new Material();public RenderTexture texture;public bool needsThumbnail;
 }
 private sealed class Channel {
  public ScreenState owner;public VideoPlayer player;public bool ready=true,slideshow;
  public SlopCityTikTokVideo currentVideo;
 }
 private Dictionary<SlopCityCityScreen,ScreenState> displayLookup=new Dictionary<SlopCityCityScreen,ScreenState>();
 private readonly List<ScreenState> displays=new List<ScreenState>(),candidates=new List<ScreenState>();
 private bool MobileWebBudget=>true;private const int MobileScreenTextureLimit=8;
 private static void Destroy(Texture value){value.destroyed=true;}
 private bool EnsureScreenTexture(ScreenState display){if(!display.texture)display.texture=new RenderTexture();return true;}
 private float nextScreenUpdate;private int releases;
 // Decoder retirement substitute: production ReleaseSlot/StopPlayback are checked
 // separately by the source integration assertions below. No player survives it.
 private void ReleaseSlot(Channel channel){releases++;channel.player.time=0;channel.player=null;channel.owner.slot=null;channel.owner=null;}
__METHODS__
 private static bool ValidPlaybackSeconds(double value)
__VALID_SECONDS__
 private static string VideoKey(SlopCityTikTokVideo video)
__VIDEO_KEY__
 private int checks;
 private void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 private ScreenState Fixture(out SlopCityCityScreen screen,double time=18){
  screen=new SlopCityCityScreen();var display=new ScreenState{post=new SlopCityTikTokVideo()};
  displayLookup[screen]=display;displays.Add(display);Bind(display,time);return display;
 }
 private static Channel Bind(ScreenState display,double time=0){
  var channel=new Channel{owner=display,currentVideo=display.post,player=new VideoPlayer{time=time}};
  display.slot=channel;return channel;
 }
 private void Exercise(){
  SlopCityCityScreen screen;var display=Fixture(out screen);var original=display.slot.player;
  SetScreenPaused(screen,true);
  Check(display.slot==null&&releases==1&&display.paused,"Pause retires the owned slot");
  Check(display.pausedVideoTime==18&&display.pausedVideoGeneration==7,"Pause captures the prepared offset and generation before Stop");
  SetScreenPaused(screen,false);var resumed=Bind(display);RestorePausedVideo(resumed,resumed.player);
  Check(resumed.player!=original&&resumed.player.time==18,"A replacement decoder restores the same-post timestamp");
  Check(display.pausedVideoKey==null,"Successful restore consumes the bookmark");
  resumed.player.time=22;RestorePausedVideo(resumed,resumed.player);Check(resumed.player.time==22,"A duplicate ready event cannot seek backward");

  display=Fixture(out screen);display.texture=new RenderTexture();var oldTexture=display.texture;original=display.slot.player;
  SetScreenPaused(screen,true);candidates.Clear();RefreshMobileScreenTextures();
  Check(display.texture==null&&oldTexture.released&&oldTexture.destroyed,"The actual mobile texture-pool path releases paused native texture ownership");
  Check(display.generation==8&&display.pausedVideoGeneration==8&&display.pausedVideoTime==18,"Poster cancellation advances generation while preserving the same-post pause bookmark");
  SetScreenPaused(screen,false);candidates.Add(display);RefreshMobileScreenTextures();resumed=Bind(display);
  Check(display.texture!=null&&display.texture!=oldTexture,"The mobile pool recreates a destination before preparing the replacement decoder");
  RestorePausedVideo(resumed,original);Check(resumed.player.time==0&&display.pausedVideoTime==18,"The retired decoder cannot seek or consume the texture-retirement bookmark");
  RestorePausedVideo(resumed,resumed.player);Check(resumed.player.time==18,"Pause, mobile interest retirement and Play restore the original timestamp");
  candidates.Clear();

  display=Fixture(out screen);SetScreenPaused(screen,true);ReleaseScreenTexture(display);ReleaseScreenTexture(display);
  resumed=Bind(display);RestorePausedVideo(resumed,resumed.player);Check(resumed.player.time==18,"Repeated same-post texture cancellation preserves one valid bookmark");
  display=Fixture(out screen);SetScreenPaused(screen,true);display.post=new SlopCityTikTokVideo{id="other"};ReleaseScreenTexture(display);
  resumed=Bind(display);RestorePausedVideo(resumed,resumed.player);Check(resumed.player.time==0&&display.pausedVideoKey==null,"Texture retirement never transfers a bookmark to a changed clip");
  display=Fixture(out screen);SetScreenPaused(screen,true);ResetScreenPlayback(screen);ReleaseScreenTexture(display);
  resumed=Bind(display);RestorePausedVideo(resumed,resumed.player);Check(resumed.player.time==0&&display.pausedVideoKey==null,"Texture retirement cannot resurrect an explicit Stop bookmark");
  display=Fixture(out screen);SetScreenPaused(screen,true);display.generation++;ReleaseScreenTexture(display);
  resumed=Bind(display);RestorePausedVideo(resumed,resumed.player);Check(resumed.player.time==0&&display.pausedVideoKey==null,"An already stale selection generation remains invalid across texture retirement");

  display=Fixture(out screen);SetScreenPaused(screen,true);SetScreenPaused(screen,false);
  var preparing=Bind(display);preparing.ready=false;preparing.player.isPrepared=false;
  SetScreenPaused(screen,true);Check(display.pausedVideoTime==18,"Pause during replacement preparation keeps the pending timestamp");
  SetScreenPaused(screen,false);resumed=Bind(display);RestorePausedVideo(resumed,resumed.player);Check(resumed.player.time==18,"Repeated pause/play restores once ready");

  display=Fixture(out screen);ResetScreenPlayback(screen);Check(display.slot==null&&display.pausedVideoKey==null,"Explicit Stop retires and discards position");
  SetScreenPaused(screen,false);resumed=Bind(display);RestorePausedVideo(resumed,resumed.player);Check(resumed.player.time==0,"Play after explicit Stop starts at zero");
  display=Fixture(out screen);SetScreenPaused(screen,true);ResetScreenPlayback(screen);Check(display.pausedVideoKey==null,"Stop after Pause also clears the pending position");

  display=Fixture(out screen);SetScreenPaused(screen,true);display.post=new SlopCityTikTokVideo{id="other"};resumed=Bind(display);
  RestorePausedVideo(resumed,resumed.player);Check(resumed.player.time==0&&display.pausedVideoKey==null,"A different post never inherits the previous offset");
  display=Fixture(out screen);SetScreenPaused(screen,true);display.generation++;resumed=Bind(display);
  RestorePausedVideo(resumed,resumed.player);Check(resumed.player.time==0,"A loop/new selection with the same post rejects the old generation");

  display=Fixture(out screen);SetScreenPaused(screen,true);resumed=Bind(display);var stale=new VideoPlayer();
  RestorePausedVideo(resumed,stale);Check(stale.time==0&&display.pausedVideoTime==18,"A stale decoder callback neither seeks nor consumes the current bookmark");
  var actual=resumed.player;resumed.owner=new ScreenState();RestorePausedVideo(resumed,actual);
  Check(actual.time==0&&display.pausedVideoTime==18,"A callback whose slot owner changed cannot seek");

  foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,-1d,0d,42.042,3601d}){
   display=Fixture(out screen,invalid);SetScreenPaused(screen,true);
   Check(display.pausedVideoKey==null,"Nonfinite/out-of-duration/oversized positions are discarded");
  }
  foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,0d,-1d,3601d}){
   display=Fixture(out screen);display.slot.player.length=invalid;SetScreenPaused(screen,true);
   Check(display.pausedVideoKey==null,"Unusable prepared duration cannot create a bookmark");
  }
  display=Fixture(out screen);SetScreenPaused(screen,true);resumed=Bind(display);resumed.player.length=10;
  RestorePausedVideo(resumed,resumed.player);Check(resumed.player.time==0&&display.pausedVideoKey==null,"A shorter replacement stream cannot receive an out-of-range seek");
  display=Fixture(out screen);SetScreenPaused(screen,true);resumed=Bind(display);resumed.player.canSetTime=false;
  RestorePausedVideo(resumed,resumed.player);Check(resumed.player.time==0,"An unseekable stream is never assigned time");
  display=Fixture(out screen);display.post.kind="slideshow";display.slot.slideshow=true;SetScreenPaused(screen,true);
  Check(display.pausedVideoKey==null,"Images/audio slideshows have no video bookmark");
  display=Fixture(out screen);SetScreenPaused(screen,true);resumed=Bind(display);resumed.slideshow=true;
  RestorePausedVideo(resumed,resumed.player);Check(resumed.player.time==0,"An audio/image slot is never sought");
  Console.WriteLine(checks+" actual C# pause-position assertions passed; no Unity/browser validation.");
 }
 public static void Main(){new Harness().Exercise();}
}
'''

BUDGET_HARNESS = r'''
namespace SlopCity { public static class BudgetHarness {
 static int checks;
 static void Check(bool value,string label){if(!value)throw new System.Exception(label);checks++;}
 static byte[] Png(int w,int h){var b=new byte[24];b[0]=137;b[1]=80;b[2]=78;b[3]=71;b[12]=73;b[13]=72;b[14]=68;b[15]=82;for(int n=0;n<4;n++){b[16+n]=(byte)(w>>(24-n*8));b[20+n]=(byte)(h>>(24-n*8));}return b;}
 static bool Image(int w,int h){int x,y;return SlopCityMediaTransfer.ImageSizeIsSafe(Png(w,h),out x,out y);}
 static bool Wave(int frames,int channels,int rate){using(var s=new System.IO.MemoryStream()){using(var w=new System.IO.BinaryWriter(s,System.Text.Encoding.UTF8,true)){w.Write(0x46464952u);w.Write(36+frames*channels*2);w.Write(0x45564157u);w.Write(0x20746d66u);w.Write(16);w.Write((short)1);w.Write((short)channels);w.Write(rate);w.Write(rate*channels*2);w.Write((short)(channels*2));w.Write((short)16);w.Write(0x61746164u);w.Write(frames*channels*2);}s.SetLength(44+frames*channels*2);s.Position=0;int c,r,f,o;return SlopCityMediaTransfer.WaveIsSafe(s,out c,out r,out f,out o);}}
 public static void Main(){
  Check(Image(841,1280),"largest audited bundled still remains admitted");
  Check(Image(2048,1024),"pixel boundary admitted");Check(!Image(2048,1025),"pixel expansion rejected");
  Check(!Image(2049,1),"edge cap independent of total pixels");Check(!Image(0,5),"empty dimensions rejected");Check(!Image(-1,5),"negative dimensions rejected");
  Check(!Image(4096,2048),"old 32MiB RGBA extreme rejected before texture decode");
  var image=Png(1,1);System.Array.Resize(ref image,SlopCityMediaTransfer.MaxImageBytes+1);int x,y;
  Check(!SlopCityMediaTransfer.ImageSizeIsSafe(image,out x,out y),"compressed byte cap independent of dimensions");
  Check(Wave(1349596,1,22050),"largest audited bundled audio remains admitted");
  Check(Wave(1048576,2,48000),"8MiB float upload boundary admitted");
  Check(!Wave(1048577,2,48000),"float expansion rejected before AudioClip allocation");
  Check(!Wave(180*8000+1,1,8000),"duration cap applies even at low sample rate");
  Check(!Wave(300*48000,2,48000),"old stereo five-minute extreme rejected");
  System.Console.WriteLine(checks+" actual C# decode admission assertions passed; no native decoder/device validation.");
 }
}}
'''


def run_csharp_fixture(mono, compiler, work, name, source):
    fixture, output = work / (name + '.cs'), work / (name + '.exe')
    fixture.write_text(source)
    built = subprocess.run([str(mono), str(compiler), '-nologo', '-out:' + str(output), str(fixture)], capture_output=True, text=True)
    if built.returncode:
        raise RuntimeError(built.stdout + built.stderr)
    result = subprocess.run([str(mono), str(output)], check=True, capture_output=True, text=True)
    print(result.stdout, end='')


def verify_pause_and_decode_budgets(city, catalog, mono, work):
    signatures = ['public void SetScreenPaused(', 'public void ResetScreenPlayback(',
                  'private static void ClearPausedVideo(', 'private static void CapturePausedVideo(',
                  'private static void RestorePausedVideo(', 'private static void ReleaseScreenTexture(',
                  'private void RefreshMobileScreenTextures(']
    methods = '\n'.join(extract_method(city, signature) for signature in signatures)
    expressions = {}
    for name, result_type in [('ValidPlaybackSeconds', 'bool'), ('VideoKey', 'string')]:
        start = city.index('private static ' + result_type + ' ' + name + '(')
        expressions[name] = city[city.index('=>', start):city.index(';', start) + 1]
    pause = extract_method(city, 'public void SetScreenPaused(')
    assert pause.index('CapturePausedVideo(display)') < pause.index('ReleaseSlot(display.slot)')
    prepare = extract_method(city, 'private void PrepareChannel(')
    assert prepare.index('operation != signal.operation') < prepare.index('RestorePausedVideo(signal, source)')
    assert prepare.index('signal.currentVideo = signal.pendingVideo') < prepare.index('RestorePausedVideo(signal, source)') < prepare.index('ApplyPlayback();')
    assert 'ClearPausedVideo(display);' in extract_method(city, 'private void AssignNextPost(')
    assert 'AssignNextPost(signal.owner)' in extract_method(city, 'private void AdvanceScreen(')
    retire = extract_method(city, 'private void ReleaseSlot(')
    assert 'StopPlayback(signal);' in retire and 'if (MobileWebBudget) ReleasePlayback(signal);' in retire
    assert 'new VideoPlayer' not in methods and 'new RenderTexture' not in methods
    compiler = mono.parents[1] / 'lib/mono/4.5/csc.exe'
    pause_source = PAUSE_HARNESS.replace('__METHODS__', methods).replace('__VALID_SECONDS__', expressions['ValidPlaybackSeconds']).replace('__VIDEO_KEY__', expressions['VideoKey'])
    run_csharp_fixture(mono, compiler, work, 'PausePosition', pause_source)
    constants_start = catalog.index('        internal const int MaxImageBytes')
    constants_end = catalog.index('        internal static UnityWebRequest Get(', constants_start)
    pure_methods = '\n'.join(extract_method(catalog, signature) for signature in (
        'internal static bool ImageSizeIsSafe(', 'internal static bool WaveIsSafe('))
    start = catalog.index('private static int BigEndian(')
    big_endian = catalog[start:catalog.index(';', start) + 1]
    budget_source = 'using System; using System.IO; using System.Text; namespace SlopCity { internal static class SlopCityMediaTransfer {\n' + catalog[constants_start:constants_end] + pure_methods + big_endian + '}}\n' + BUDGET_HARNESS
    run_csharp_fixture(mono, compiler, work, 'DecodeBudgets', budget_source)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--media-source', type=Path, default=ROOT / 'Unity/Assets/Scripts/SlopCityMediaPlayer.cs')
    parser.add_argument('--city-source', type=Path, default=ROOT / 'Unity/Assets/Scripts/SlopCityCityMedia.cs')
    parser.add_argument('--mono', type=Path, default=PROJECT / '.tools/Unity-6000.3.26f1/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin/mono')
    parser.add_argument('--expect-failure', help='Require a baseline failure containing this exact text.')
    args = parser.parse_args()
    city = args.city_source.read_text()
    assert city.index('RefreshMobileLoungePriority(strongest != null && strongest.aimed') < city.index('int decoderBudget = MobileWebBudget'), 'Aim priority must precede slot budget'
    methods = '\n'.join(extract_method(city, signature) for signature in (
        '        private void RefreshMobileLoungePriority(',
        '        private void BindSlot(',
        '        public bool ReleaseMobileDecodersForLounge('))
    has_priority_reset = 'bool immediate = false' in methods
    if has_priority_reset:
        assert 'if (Suspended || manuallyPaused) RefreshMobileLoungePriority(false, true);' in extract_method(city, '        private void ApplyPlayback(')
        for signature in ('        private void OnDisable(', '        private void OnDestroy('):
            assert 'RefreshMobileLoungePriority(false, true);' in extract_method(city, signature)
    cancel_priority = 'RefreshMobileLoungePriority(false, true);' if has_priority_reset else 'RefreshMobileLoungePriority(false);'
    stub = '''using UnityEngine; using UnityEngine.Video; namespace SlopCity {
public class SlopCityCityMedia:MonoBehaviour {
public class SlopCityCityScreen {public bool AudioOnly;}
public static SlopCityCityMedia Instance; private Transform viewer; private SlopCityMediaPlayer loungeMedia;
public bool MobileWebBudget; private ScreenState desiredAudioOwner; private Channel[] channels=new Channel[1];
private bool mobileLoungePriority,mobileLoungePriorityRequest; private float mobileLoungePrioritySince;
public void SetViewer(Transform v){viewer=v;}
public static void DrawImage(Texture source,RenderTexture destination){}
// Legacy cases exercise established ownership after confirmed focus. Focus sampling
// and actual elapsed timing are exercised independently by the regression below.
public void Aim(bool v){float now=Time.unscaledTime;Time.unscaledTime=now-1;RefreshMobileLoungePriority(v);Time.unscaledTime=now;RefreshMobileLoungePriority(v);}
public void SampleAim(bool v){RefreshMobileLoungePriority(v);}
public void CancelPriority(){CANCEL_PRIORITY}
public void AddCityDecoder(){var session=new GameObject("CityDecoder");channels[0]=new Channel{owner=new ScreenState(),session=session,player=session.AddComponent<VideoPlayer>()};}
private class ScreenState {public SlopCityCityScreen screen=new SlopCityCityScreen();public Channel slot;public RenderTexture texture=null;public Material material=null;}
private class Channel {public ScreenState owner;public RenderTexture texture;public Material material;public bool hasScreen,advance,failed;public float nextAttempt;public GameObject session;public VideoPlayer player;}
private void ReleaseSlot(Channel channel){if(channel.session)Destroy(channel.session);channel.session=null;channel.player=null;channel.owner=null;}
private bool EnsureScreenTexture(ScreenState display){return true;}
public float BindAdmission(){var channel=new Channel();BindSlot(channel,new ScreenState());return channel.nextAttempt;}
''' .replace('CANCEL_PRIORITY', cancel_priority) + methods + '\n}}'
    compiler = args.mono.parents[1] / 'lib/mono/4.5/mcs.exe'
    with tempfile.TemporaryDirectory(prefix='slop-media-arbitration-') as directory:
        work = Path(directory)
        generated = work / 'City.cs'
        generated.write_text(stub)
        shared = [str(FIXTURE / 'UnityStubs.cs'), str(generated), str(args.media_source)]
        native = subprocess.run([str(args.mono), str(compiler), '-target:library', '-out:' + str(work / 'Native.dll'), *shared], capture_output=True, text=True)
        if native.returncode:
            print(native.stdout + native.stderr)
            return native.returncode
        built = subprocess.run([str(args.mono), str(compiler), '-define:UNITY_WEBGL', '-out:' + str(work / 'Web.exe'), *shared, str(FIXTURE / 'Harness.cs')], capture_output=True, text=True)
        if built.returncode:
            print(built.stdout + built.stderr)
            return built.returncode
        result = subprocess.run([str(args.mono), str(work / 'Web.exe')], capture_output=True, text=True)
        output = result.stdout + result.stderr
        if args.expect_failure:
            if result.returncode and args.expect_failure in output:
                print('Expected baseline regression reproduced: ' + args.expect_failure)
                return 0
            print(output)
            return 1
        print(output, end='')
        if result.returncode:
            return result.returncode
        catalog = (ROOT / 'Unity/Assets/Scripts/SlopCityTikTokCatalog.cs').read_text()
        verify_pause_and_decode_budgets(city, catalog, args.mono, work)
        return 0


if __name__ == '__main__':
    raise SystemExit(main())
