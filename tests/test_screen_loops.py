#!/usr/bin/env python3
"""Execute production C# screen selection with bounded managed API substitutes.

Requires an existing Unity Mono/Roslyn installation; installs nothing. No Unity
Editor, decoder, graphics, network or physical device runs. Compiler outputs are
temporary, and all extracted selection/tuning/catalog methods remain unchanged.
"""
import argparse
from pathlib import Path
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'slop-city-blender/Unity/Assets/Scripts/SlopCityCityMedia.cs'


def method(source, signature):
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
    raise AssertionError('Unclosed production method: ' + signature)


HARNESS = r'''
using System;using System.Collections.Generic;using System.Linq;
public static class Time {public static float unscaledTime=10;}
public class Transform {public SlopCityCityScreen screen;public string path;}
public static class SlopCitySalvageItem {public static string SourceIdentity(Transform transform)=>transform.path;}
public class SlopCityFeaturedBillboard {public const float HoldAtSeconds=-1,HoldSeconds=10;public static implicit operator bool(SlopCityFeaturedBillboard value)=>value!=null;}
public class SlopCityTikTokAccount {public string creator;public bool complete;public int discoveredCount,totalReported=-1;}
public class SlopCityTikTokVideo {public string id,creator="1984drh",title="video",localFile="",mediaUrl="";public long createTime;}
public class SlopCityTikTokCatalog {
 public int version=2;public SlopCityTikTokVideo[] videos;public SlopCityTikTokAccount[] accounts;
 public bool TryGetFeaturedVideo(out SlopCityTikTokVideo post){post=null;return false;}
}
public class SlopCityCityScreen {
 public Transform transform;public string FeaturedClipKey="",clip="";public int Channel=2;
 public bool IsFeatured=>FeaturedClipKey.Length>0;public string ClipTitle=>clip;
 public void AssignClip(string key,string title){clip=key;}
 public void Tune(int channel,object material){Channel=channel;}
 public T GetComponentInParent<T>(bool inactive)where T:class=>null;
 public void ConfigureFeaturedPost(string creator,string id,float at,float hold){FeaturedClipKey=creator+"/"+id;}
 public static implicit operator bool(SlopCityCityScreen screen)=>screen!=null;
}
public class Harness {
 private const int ChannelCount=3,MaximumScreenLoop=__LIMIT__;
 private static readonly string[] Profiles={"1984drh","machine_publishers"};
 private class Media {public string localFile="legacy.mp4",title="legacy";}
 private class Retry {public float after;}
 private class ScreenState {
  public SlopCityCityScreen screen;public SlopCityTikTokVideo post;public List<SlopCityTikTokVideo> program;
  public int programIndex=-1,programChannel=-1,ordinal,generation;public bool needsThumbnail;
  public Media fallback=new Media();public Channel slot;public object material=new object();
  public string pausedVideoKey;public double pausedVideoTime;public int pausedVideoGeneration=-1;
 }
 private class Channel {
  public int index,operation;public List<SlopCityTikTokVideo> playlist=new List<SlopCityTikTokVideo>();
  public ScreenState owner;public bool advance,ready,loading,resolving,failed,hasScreen;
  public SlopCityTikTokVideo currentVideo,pendingVideo;public object texture,material;
 }
 private readonly Channel[] channels={new Channel{index=0},new Channel{index=1},new Channel{index=2}};
 private readonly List<ScreenState> displays=new List<ScreenState>();
 private readonly Dictionary<SlopCityCityScreen,ScreenState> displayLookup=new Dictionary<SlopCityCityScreen,ScreenState>();
 private readonly Dictionary<string,Retry> retries=new Dictionary<string,Retry>(StringComparer.Ordinal);
 private bool initialized=true,MobileWebBudget;private float nextScreenUpdate;private SlopCityTikTokCatalog pendingCatalog;
 private ScreenState audioOwner,desiredAudioOwner;private float audioGain;private int stopped,released;
 private static bool ValidCatalogVideo(SlopCityTikTokVideo post)=>post!=null&&!string.IsNullOrEmpty(post.id)&&(post.creator=="1984drh"||post.creator=="machine_publishers");
 private static string VideoKey(SlopCityTikTokVideo post)=>post.creator+"/"+post.id;
 private static string PostTitle(SlopCityTikTokVideo post)=>post.title;
 private void PruneScreens(){}
 private SlopCityCityScreen FindTuningScreen(Transform target)=>target?.screen;
 private void StopPlayback(Channel signal){stopped++;}
 private void ReleasePhoto(Channel signal){}
 private void ReleasePlayback(Channel signal){released++;}
 __METHODS__
 private int checks;
 private void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 private static string Keys(IEnumerable<SlopCityTikTokVideo> posts)=>string.Join(",",posts.Select(VideoKey));
 private static List<SlopCityTikTokVideo> Pool(int count,string creator="1984drh")=>Enumerable.Range(0,count).Select(i=>new SlopCityTikTokVideo{id="clip_"+i,creator=creator}).ToList();
 private ScreenState Screen(string path,int channel=2,int ordinal=0){var screen=new SlopCityCityScreen{Channel=channel,transform=new Transform{path=path}};screen.transform.screen=screen;var state=new ScreenState{screen=screen,ordinal=ordinal};displays.Add(state);displayLookup[screen]=state;return state;}
 private void Advance(ScreenState display){var slot=new Channel{owner=display};AdvanceScreen(slot);Check(slot.advance,"Advance schedules existing bounded playback path");}
 private void Catalog(List<SlopCityTikTokVideo> posts,SlopCityTikTokAccount[] accounts=null)=>ApplyTikTokCatalog(new SlopCityTikTokCatalog{videos=posts.ToArray(),accounts=accounts});
 private void Exercise(){
  var drh=Pool(20);var machine=Pool(12,"machine_publishers");var pool=drh.Concat(machine).ToList();Catalog(pool);
  var a=Screen("/city/street/tv_a");AssignNextPost(a);var program=a.program;string original=Keys(program);
  Check(program.Count==5&&program.Distinct().Count()==5&&program.All(pool.Contains),"A TV holds five distinct existing references");
  for(int cycle=0;cycle<10;cycle++)for(int i=1;i<=5;i++){Advance(a);Check(a.post==program[i%5],"A TV repeats the same shuffled subset order");Check(ReferenceEquals(program,a.program)&&Keys(program)==original,"Advancement retains the bounded program");}
  var reload=Screen("/city/street/tv_a",ordinal:777);AssignNextPost(reload);Check(Keys(reload.program)==original&&reload.post==program[0],"Hierarchy identity ignores registration order and other screen assignments");
  var empty=new HashSet<string>();Check(Keys(BuildScreenLoop(pool,"stable",2,empty))==Keys(BuildScreenLoop(pool.AsEnumerable().Reverse().ToList(),"stable",2,empty)),"Catalogue enumeration order cannot reshuffle a TV");
  var programs=new HashSet<string>();var starts=new HashSet<string>();for(int i=0;i<128;i++){var screen=Screen("/city/tv_"+i);AssignNextPost(screen);programs.Add(Keys(screen.program));starts.Add(VideoKey(screen.post));Check(screen.program.Count==5&&screen.program.All(pool.Contains),"Independent screen programs stay bounded");}
  Check(programs.Count>100&&starts.Count>10,"Different TVs vary their subsets and starts");
  for(int channel=0;channel<3;channel++){
   var s=Screen("/city/channels/"+channel,channel);AssignNextPost(s);for(int i=0;i<20;i++){Check(channel==2||s.post.creator==(channel==0?"machine_publishers":"1984drh"),"Creator tuning remains meaningful through every loop");Advance(s);}
  }
  var tuned=Screen("/city/tuned",0);AssignNextPost(tuned);var prior=tuned.program;
  Check(TryTuneScreen(tuned.screen.transform,out string message)&&tuned.screen.Channel==1&&tuned.post.creator=="1984drh"&&!ReferenceEquals(prior,tuned.program),"Player tuning invalidates and rebuilds the matching channel program");
  Check(TryTuneJobScreen(tuned.screen.transform,0,out message)&&tuned.post.creator=="machine_publishers","Requested-channel job tuning keeps its real channel identity");
  prior=tuned.program;Check(TryTuneJobScreen(tuned.screen.transform,0,out message)&&ReferenceEquals(prior,tuned.program),"Already-selected job channel is an idempotent no-op");
  Check(!TryTuneJobScreen(tuned.screen.transform,3,out message)&&!TryTuneJobScreen(null,0,out message),"Invalid job channel/target stays rejected");
  var featured=Screen("/city/featured");featured.screen.FeaturedClipKey=VideoKey(pool[0]);AssignNextPost(featured);Check(featured.post==pool[0]&&featured.program==null,"Dedicated featured broadcast remains exactly pinned");Advance(featured);Check(featured.post==pool[0],"Featured advance cannot shuffle its pin");
  Check(!TryTuneScreen(featured.screen.transform,out message)&&!TryTuneJobScreen(featured.screen.transform,0,out message),"Player and job tuning both reject featured displays");
  for(int i=0;i<20;i++)Check(!BuildScreenLoop(pool,"reserved"+i,2,new HashSet<string>{featured.screen.FeaturedClipKey}).Contains(pool[0]),"Featured pin never enters an ordinary program");
  var retry=Screen("/city/retry");AssignNextPost(retry);var retryProgram=retry.program;retries[VideoKey(retryProgram[1])]=new Retry{after=20};Advance(retry);Check(retry.post==retryProgram[2],"Backoff skips an unavailable item within the same subset");
  foreach(var post in retryProgram)retries[VideoKey(post)]=new Retry{after=20};int cursor=retry.programIndex;Advance(retry);Check(retry.post==null&&retry.screen.clip=="archive/legacy.mp4","All-failed ordinary program retains the existing bundled fallback");Check(retry.programIndex==cursor&&ReferenceEquals(retryProgram,retry.program),"Backoff neither refills nor discards a cached subset");Time.unscaledTime=21;Advance(retry);Check(retry.post==retryProgram[(cursor+1)%5],"Recovered clips resume the same loop");retries.Clear();
  foreach(int count in new[]{0,1,2,4,5,6,256}){var values=Pool(count);var result=BuildScreenLoop(values,"boundary",2,empty);Check(result.Count==Math.Min(5,count)&&result.All(values.Contains),"Empty/small/large catalogues obey the five-reference cap");}
  var duplicates=new List<SlopCityTikTokVideo>{null,pool[1],pool[1],new SlopCityTikTokVideo{id=pool[1].id},pool[2]};Check(BuildScreenLoop(duplicates,"duplicates",2,empty).Count==2,"Null/duplicate entries cannot duplicate a program");
  Check(BuildScreenLoop(drh,"fallback",0,empty).All(post=>post.creator=="1984drh"),"An empty creator pool still falls back to available combined posts");
  Check(BuildScreenLoop(new List<SlopCityTikTokVideo>{machine[0],drh[0]},"short",0,empty).Count==1,"A short preferred creator pool remains independently bounded");
  a.pausedVideoKey="old";a.pausedVideoTime=12;a.pausedVideoGeneration=99;Advance(a);Check(a.pausedVideoKey==null&&a.pausedVideoTime==0&&a.pausedVideoGeneration==-1,"Explicit advancement clears the old-source bookmark");
  var retained=a.program;var slot=new Channel{owner=a,ready=true,currentVideo=a.post,hasScreen=true};a.slot=slot;MobileWebBudget=true;ReleaseSlot(slot);Check(a.slot==null&&slot.owner==null&&ReferenceEquals(retained,a.program)&&released>0,"Phone decoder retirement releases playback while preserving its program");MobileWebBudget=false;
  // Reconciliation must not globally displace overlapping independently chosen clips.
  a.post=reload.post=program[0];ReconcileScreens();Check(a.post==program[0]&&reload.post==program[0],"Catalogue reconciliation preserves valid overlapping per-screen assignments");
  var current=a.post;slot=new Channel{owner=a,ready=true};a.slot=slot;int before=stopped;Catalog(pool.AsEnumerable().Reverse().ToList());Check(a.post==current&&a.program==null&&stopped==before,"Metadata refresh invalidates future selection without stopping a valid active clip");Advance(a);Check(a.program!=null&&a.program.Count==5,"Next clip boundary rebuilds the program");
  current=a.post;before=stopped;var changed=Pool(8);changed.ForEach(post=>post.id="new_"+post.id);Catalog(changed);Check(a.post==current&&a.slot==slot&&slot.ready&&stopped==before,"Ready removed media finishes without interruption after catalogue replacement");Advance(a);Check(a.program.All(post=>post.id.StartsWith("new_"))&&a.post.id.StartsWith("new_"),"New boundary uses only the replaced catalogue");
  retained=a.program;Catalog(new List<SlopCityTikTokVideo>());Check(ReferenceEquals(a.program,retained),"Ambiguous empty refresh retains usable program and retry state");
  Catalog(new List<SlopCityTikTokVideo>(),new[]{new SlopCityTikTokAccount{creator="1984drh",complete=true,discoveredCount=0,totalReported=0},new SlopCityTikTokAccount{creator="machine_publishers",complete=true,discoveredCount=0,totalReported=0}});Advance(a);Check(a.program.Count==0&&a.post==null,"Authoritative terminal empty catalogues clear at the next boundary");
  Console.WriteLine(checks+" actual C# public screen-loop assertions passed; no Unity/browser/device validation.");
 }
 public static void Main(){new Harness().Exercise();}
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mono', type=Path, required=True, help='Existing Unity Mono executable; no installation is performed.')
    args = parser.parse_args()
    mono = args.mono.expanduser().resolve()
    compiler = mono.parents[1] / 'lib/mono/4.5/csc.exe'
    if not mono.is_file() or not compiler.is_file():
        parser.error('An installed Mono/Roslyn compiler is required.')
    source = SOURCE.read_text()
    signatures = ['private void AssignNextPost(', 'private static List<SlopCityTikTokVideo> BuildScreenLoop(',
                  'private static uint ScreenProgramRank(', 'private void AdvanceScreen(',
                  'public bool TryTuneScreen(', 'public bool TryTuneJobScreen(',
                  'private void ReconcileScreens(', 'public void ApplyTikTokCatalog(',
                  'private void PruneRetries(', 'private static bool ConfirmsEmptyAccount(',
                  'private void ReleaseSlot(', 'private static void ClearPausedVideo(']
    methods = '\n'.join(method(source, signature) for signature in signatures)
    limit = re.search(r'private const int MaximumScreenLoop = (\d+);', source).group(1)
    assert int(limit) == 5
    assert 'program' not in method(source, 'private void ReleaseSlot(')
    assert 'new VideoPlayer' not in methods and 'new RenderTexture' not in methods
    fixture = HARNESS.replace('__METHODS__', methods).replace('__LIMIT__', limit)
    with tempfile.TemporaryDirectory(prefix='slop-city-public-screen-loops-') as directory:
        folder = Path(directory)
        program, output = folder / 'Harness.cs', folder / 'ScreenLoops.exe'
        program.write_text(fixture)
        result = subprocess.run([str(mono), str(compiler), '-nologo', '-out:' + str(output), str(program)], capture_output=True, text=True)
        if result.returncode:
            raise RuntimeError(result.stdout + result.stderr)
        result = subprocess.run([str(mono), str(output)], capture_output=True, text=True)
        if result.returncode:
            raise RuntimeError(result.stdout + result.stderr)
        print(result.stdout.strip())


if __name__ == '__main__':
    main()
