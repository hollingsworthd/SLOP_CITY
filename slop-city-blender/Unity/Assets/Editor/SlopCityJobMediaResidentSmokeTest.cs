#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;
using Object=UnityEngine.Object;
namespace SlopCity.Editor
{
    /// <summary>Positive native decoded-media jobs, carried relay arrival and presented resident exchanges.</summary>
    [InitializeOnLoad]
    public static class SlopCityJobMediaResidentSmokeTest
    {
        private const string Active="SlopCity.JobMediaResident.Positive",Started=Active+".Started";
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly string[] Keys={"SlopCity.Sandbox.Progress.v1","SlopCity.Sandbox.Progress.v1.recovery","SlopCity.MatrixSearch.Progress.v1","SlopCity.MatrixSearch.Progress.v1.recovery"};
        private static readonly Stack<IEnumerator> Work=new Stack<IEnumerator>();
        private static readonly Collider[] Overlaps=new Collider[64];private static readonly RaycastHit[] Hits=new RaycastHit[64];
        private static Receipt receipt;private static SlopCityFirstPerson player;private static SlopCitySandbox sandbox;private static CharacterController capsule;private static Camera camera;
        private static SlopCityCityMedia media;private static SlopCityCityScreen screen;private static SlopCityInteractable displayControl;private static SlopCityCrowd crowd,authored;
        private static SlopCityMetroWorld metro;private static GameObject fixture;private static double enteredAt;private static int lastFrame=-1;private static bool started;
        private static readonly Vector3 Feet=new Vector3(0,1000,0);
        static SlopCityJobMediaResidentSmokeTest()
        {
            EditorApplication.update+=Tick;
            EditorApplication.playModeStateChanged+=state=>{if(!SessionState.GetBool(Active,false))return;if(state==PlayModeStateChange.EnteredPlayMode)enteredAt=EditorApplication.timeSinceStartup;if(state==PlayModeStateChange.EnteredEditMode)Finish(new Exception("Positive fixture Play Mode ended early."));};
            Application.logMessageReceived+=(message,stack,type)=>{if(!SessionState.GetBool(Active,false)||type!=LogType.Error&&type!=LogType.Assert&&type!=LogType.Exception)return;if(receipt==null)receipt=NewReceipt();if(message.StartsWith("ArgumentOutOfRangeException:",StringComparison.Ordinal)&&stack.Contains("UnityEditor.Search.SearchDatabase")&&stack.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup")&&!stack.Contains("SlopCity."))receipt.editorDiagnostics.Add(message+"\n"+stack);else receipt.runtimeErrors.Add(message+"\n"+stack);};
        }
        // Graphics-enabled isolated native batch Editor. Omit -quit and -nographics; this runner exits.
        public static void Run()
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode||Application.companyName!="SlopCityValidation-v0116"||!Application.persistentDataPath.Contains("SlopCityValidation-v0116")||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)throw new InvalidOperationException("Isolated graphics-enabled SlopCityValidation-v0116 batch Editor required.");
            SessionState.SetString(Started,DateTime.UtcNow.ToString("O"));SessionState.SetBool(Active,true);receipt=NewReceipt();
            foreach(string key in Keys){SessionState.SetBool(Active+key,PlayerPrefs.HasKey(key));SessionState.SetString(Active+key+".value",PlayerPrefs.GetString(key,""));PlayerPrefs.DeleteKey(key);}PlayerPrefs.Save();
            SessionState.SetFloat(Active+".scale",Time.timeScale);SessionState.SetFloat(Active+".volume",AudioListener.volume);
            string playback=Path.Combine(Application.persistentDataPath,"TikTok/playback.json");SessionState.SetBool(Active+".playbackExists",File.Exists(playback));SessionState.SetString(Active+".playback",File.Exists(playback)?Convert.ToBase64String(File.ReadAllBytes(playback)):"");
            try
            {
                var scene=EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath,OpenSceneMode.Single);
                foreach(var value in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include,FindObjectsSortMode.None))value.enabled=false;
                foreach(var value in Object.FindObjectsByType<SlopCityMediaPlayer>(FindObjectsInactive.Include,FindObjectsSortMode.None))value.enabled=false;
                foreach(var value in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include,FindObjectsSortMode.None))value.enabled=false;
                foreach(var value in Object.FindObjectsByType<SlopCityFirstPerson>(FindObjectsInactive.Include,FindObjectsSortMode.None))value.enabled=false;
                foreach(var value in Object.FindObjectsByType<SlopCityCrowd>(FindObjectsInactive.Include,FindObjectsSortMode.None))value.enabled=false;
                foreach(var value in Object.FindObjectsByType<SlopCityCityScreen>(FindObjectsInactive.Include,FindObjectsSortMode.None))value.enabled=false;
                SlopCityWebGeometryBuildProcessor.ProtectRuntimeBuildingGeometry(scene);EditorApplication.EnterPlaymode();
            }
            catch(Exception exception){Finish(exception);}
        }
        private static Receipt NewReceipt()=>new Receipt{startedAtUtc=SessionState.GetString(Started,DateTime.UtcNow.ToString("O")),unityVersion=Application.unityVersion,graphicsDevice=SystemInfo.graphicsDeviceType+" / "+SystemInfo.graphicsDeviceName};
        private static void Tick()
        {
            if(!SessionState.GetBool(Active,false))return;if(receipt==null)receipt=NewReceipt();
            try
            {
                if((DateTime.UtcNow-DateTime.Parse(receipt.startedAtUtc).ToUniversalTime()).TotalSeconds>240)throw new TimeoutException("Positive fixture exceeded240 wall-clock seconds.");
                if(!EditorApplication.isPlaying||EditorApplication.isCompiling||lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
                if(!started){if(EditorApplication.timeSinceStartup-enteredAt<.5)return;started=true;Work.Push(Exercise());}
                if(sandbox)sandbox.Tick(Mathf.Max(.001f,Time.deltaTime));
                while(Work.Count>0){var task=Work.Peek();if(!task.MoveNext()){Work.Pop();continue;}if(task.Current is IEnumerator nested){Work.Push(nested);continue;}return;}Finish(null);
            }
            catch(Exception exception){Finish(exception);}
        }
        private static IEnumerator Exercise()
        {
            Application.runInBackground=true;Time.timeScale=1;AudioListener.volume=0;
            player=Object.FindFirstObjectByType<SlopCityFirstPerson>();Check(player&&!player.enabled,"Saved player Start/input remain held; native gameplay components are exercised explicitly.");capsule=player.GetComponent<CharacterController>();camera=player.ViewCamera;
            Set(player,"controller",capsule);Set(player,"paused",false);Set(player,"seated",false);Set(player,"externalMovement",false);
            authored=Object.FindFirstObjectByType<SlopCityCrowd>(FindObjectsInactive.Include);Check(authored&&authored.Visuals!=null,"Actual authored complete-body shared visual library exists.");
            var city=GameObject.Find("SlopCity_NineBlocks");var buildings=city.GetComponent<SlopCityBuildingWorld>()??city.AddComponent<SlopCityBuildingWorld>();Set(buildings,"constrainedBrowser",true);buildings=SlopCityBuildingWorld.Ensure(player);Check(buildings,"Actual constrained building/metro geometry initializes.");buildings.enabled=false;
            metro=SlopCityMetroWorld.Ensure(player);Check(metro&&metro.Trains.Count==3,"Actual three-car physical service exists for the relay witness.");foreach(var train in metro.Trains)train.enabled=false;metro.enabled=false;
            // Runtime entrance installation can add new screens. Only this isolated display is eligible for media jobs.
            foreach(var value in Object.FindObjectsByType<SlopCityCityScreen>(FindObjectsInactive.Include,FindObjectsSortMode.None))value.enabled=false;
            fixture=new GameObject("Positive_Job_Media_Resident_Physics");var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(fixture.transform);floor.transform.position=Feet-Vector3.up*.1f;floor.transform.localScale=new Vector3(24,.2f,20);
            var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.name="Positive_Ordinary_Display";quad.transform.SetParent(fixture.transform);quad.transform.position=Feet+new Vector3(0,1.62f,2.5f);quad.transform.localScale=new Vector3(2,1.125f,1);
            screen=quad.AddComponent<SlopCityCityScreen>();screen.Configure(quad.GetComponent<Renderer>(),0);displayControl=quad.AddComponent<SlopCityInteractable>();displayControl.Configure(SlopCityInteractionKind.Screen,"Positive ordinary display",true);displayControl.ConfigureInteractionPoint(quad.transform.position);
            var network=new GameObject("Positive_Pooled_Native_Media");network.transform.SetParent(fixture.transform);media=network.AddComponent<SlopCityCityMedia>();media.enabled=false;media.Configure(fixture.transform,player.transform,Shader.Find("Unlit/Texture"));
            var configuration=JsonUtility.FromJson<OfflineConfiguration>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Media/city-feed.json")));
            Check(configuration!=null&&string.IsNullOrEmpty(configuration.feedUrl)&&(configuration.channels==null||configuration.channels.All(c=>string.IsNullOrEmpty(c.mediaUrl))),"Actual native configuration contains no remote feed/direct-media URL.");
            Place(Feet,0);media.enabled=true;yield return null;Check((bool)Get(media,"initialized")&&media.ScreenCount==1,"Production media Start registers only the isolated ordinary display.");
            var sync=Object.FindFirstObjectByType<SlopCityTikTokSync>(FindObjectsInactive.Include);Check(sync&&!sync.enabled,"Live creator synchronization remains disabled.");
            yield return (IEnumerator)Invoke(sync,"LoadBundledCatalog");Check(sync.Catalog!=null&&sync.Catalog.videos.Length>0,"Production offline loader validates the complete native bundled creator catalog.");
            var selected=sync.Catalog.videos.Where(v=>v.kind=="video"&&v.duration>=12).Take(6).ToArray();Check(selected.Length>=3,"Three or more actual complete offline video entries support bounded decoded observations.");
            media.ApplyTikTokCatalog(new SlopCityTikTokCatalog{videos=selected});Invoke(media,"OnApplicationFocus",true);Invoke(media,"OnApplicationPause",false);media.SetSuspended(false);
            Place(Feet,0);sandbox=player.GetComponent<SlopCitySandbox>()??player.gameObject.AddComponent<SlopCitySandbox>();Set(player,"sandbox",sandbox);sandbox.Initialize(player.transform);
            var work=(SlopCityJobWorld)Get(sandbox,"uniqueWorld");Check(Enumerable.Range(0,40).All(i=>work.Get(i)!=null),"Every one of the40 production worksite placements succeeds.");
            Select(25);int count=sandbox.JobCompletions(SlopCityJobCatalog.All[25].id);UseCurrent();Check(sandbox.CaptureUniqueJobState().records[25].phase==1,"Real display USE retunes the ordinary pooled display and starts motion verification.");
            yield return Decoded("retuned motion job");yield return Until(()=>sandbox.JobCompletions(SlopCityJobCatalog.All[25].id)==count+1,15,"Actual advancing decoded motion did not finish its job.");receipt.positiveJobs.Add(25);
            Select(26);count=sandbox.JobCompletions(SlopCityJobCatalog.All[26].id);UseCurrent();yield return Decoded("requested channel2");Check(screen.Channel==2,"The real ordinary screen reports the requested channel2.");UseCurrent();Check(sandbox.JobCompletions(SlopCityJobCatalog.All[26].id)==count+1,"Production channel verification finishes exactly one job.");receipt.positiveJobs.Add(26);
            Select(29);count=sandbox.JobCompletions(SlopCityJobCatalog.All[29].id);UseCurrent();Check(sandbox.CaptureUniqueJobState().records[29].phase==1,"Relay source starts with an actual transit requirement.");
            var route=((Transform[])Get(sandbox,"uniqueRoutes"))[29];var platform=route?route.GetComponent<SlopCityMetroPlatform>():null;Check(platform,"Relay records an actual origin platform.");
            yield return Ride(platform.StopIndex);Check(sandbox.CaptureUniqueJobState().records[29].phase==2&&sandbox.CaptureUniqueJobState().records[29].ridden,"Only actual carried arrival releases the remote relay stage.");
            Place(Feet,0);UseCurrent();yield return Decoded("remote relay feed");Check(sandbox.JobCompletions(SlopCityJobCatalog.All[29].id)==count+1,"Remote display USE changes its real feed after witnessed transit.");receipt.positiveJobs.Add(29);
            media.SetSuspended(true);quad.SetActive(false);yield return null;
            var npcRoot=new GameObject("Positive_Presented_Resident");npcRoot.transform.SetParent(fixture.transform);crowd=npcRoot.AddComponent<SlopCityCrowd>();
            var nodes=new[]{new SlopCityCrowdNode{position=Feet+Vector3.forward*2,neighbors=new[]{1}},new SlopCityCrowdNode{position=Feet+new Vector3(.6f,0,2),neighbors=new[]{0}}};
            crowd.Configure(authored.Visuals,new[]{new SlopCityCrowdRoute{label="Real isolated supported resident route",nodes=nodes}},new[]{new SlopCityNpcSeed{residentId=1,appearance=100,role=SlopCityNpcRole.Resident,route=0,node=0,next=1,height=1.76f,width=1,speed=.7f}});Set(crowd,"player",player.transform);Set(crowd,"playerController",player);
            Select(38);count=sandbox.JobCompletions(SlopCityJobCatalog.All[38].id);UseCurrent();int wood=sandbox.InventoryCount("wood"),glass=sandbox.InventoryCount("glass");UseResident();
            Check(sandbox.JobCompletions(SlopCityJobCatalog.All[38].id)==count+1&&sandbox.InventoryCount("wood")==wood-2&&sandbox.InventoryCount("glass")==glass+1,"Actually presented reachable resident accepts two owned timber and gives actual glass.");Check(sandbox.CaptureUniqueJobState().records[38].residentId==crowd.GetResidentId(0),"Barter records the observed immutable resident identity.");receipt.positiveJobs.Add(38);
            Select(34);count=sandbox.JobCompletions(SlopCityJobCatalog.All[34].id);UseCurrent();var lost=sandbox.Inventory.FirstOrDefault(e=>e.category=="lost_item");Check(lost!=null,"Real under-bench target supplies an identified lost object.");string lostId=lost.id;UseResident();
            Check(sandbox.JobCompletions(SlopCityJobCatalog.All[34].id)==count+1&&sandbox.Inventory.All(e=>e.id!=lostId),"Actual presented owner accepts the same lost object and completes one return.");receipt.positiveJobs.Add(34);
            Check(receipt.runtimeErrors.Count==0,"No gameplay/decoder runtime error was logged.");
        }
        private static void Select(int mechanic){Check(sandbox.SelectJob(SlopCityJobCatalog.All[mechanic].id),"Production dispatch selects mechanic "+mechanic);}
        private static void UseCurrent(){var target=sandbox.ObjectiveTarget;Check(target&&Approach(target),"A clear physical center ray reaches the current real target.");Check(sandbox.Interact(camera),"Production USE handles that physical target.");}
        private static void UseResident()
        {
            Place(Feet,0);var snapshot=crowd.Snapshot(0);camera.transform.LookAt(snapshot.position+Vector3.up*1.2f);Invoke(crowd,"RenderCrowd",camera);Check(crowd.IsResidentPresented(0),"Complete resident body is actually presented to the approved player camera.");
            Check(crowd.TryFindTarget(new Ray(camera.transform.position,camera.transform.forward),3.4f,out int index,out _)&&index==0,"Ordinary aim and reach acquire the actual presented resident.");Set(sandbox,"nextTargetAt",0f);Check(sandbox.Interact(camera),"Production USE executes the presented resident exchange.");
        }
        private static IEnumerator Ride(int origin)
        {
            // Advance deliberately requires an enabled component. Keep all explicit physics
            // steps in this one frame so automatic Update cannot add an uncounted second step.
            foreach(var item in metro.Trains)item.enabled=true;
            Place(Vector3.up*50,0);SlopCityMetroTrain train=null;
            for(int tick=0;tick<900&&train==null;tick++){foreach(var item in metro.Trains)if(!item.IsMoving&&item.CurrentStop==origin&&item.DoorsOpen){train=item;break;}if(train==null)AdvanceTrains(.05f);}
            Check(train,"A physical train reaches the assigned boarding stop within one headway.");Place(train.transform.TransformPoint(new Vector3(0,.15f,-3)),train.transform.eulerAngles.y);WalkTo(train.transform.TransformPoint(new Vector3(0,.15f,-.8f)),2);
            Check(train.PlayerInside&&capsule.enabled,"Ordinary enabled-capsule movement boards across the open bridge.");
            bool departed=false;for(int tick=0;tick<700;tick++){AdvanceTrains(.05f);capsule.Move(Vector3.down*.006f);departed|=train.IsMoving;if(departed&&!train.IsMoving&&train.CurrentStop==(origin+1)%6)break;}
            Check(departed&&!train.IsMoving&&train.CurrentStop==(origin+1)%6&&train.PlayerInside&&capsule.enabled,"Actual train physics carries the enabled capsule to the next fixed stop.");receipt.carriedLegs++;foreach(var item in metro.Trains)item.enabled=false;yield return null;
        }
        private static void AdvanceTrains(float delta){foreach(var train in metro.Trains)train.Advance(delta);Physics.SyncTransforms();}
        private static void WalkTo(Vector3 target,float seconds){for(int step=0;step<Mathf.CeilToInt(seconds/.02f);step++){Vector3 delta=target-player.transform.position;delta.y=0;capsule.Move(Vector3.ClampMagnitude(delta,1.8f*.02f)+Vector3.down*.01f);Physics.SyncTransforms();}}
        private static IEnumerator Decoded(string label)
        {
            yield return Until(()=>media.GetComponentsInChildren<VideoPlayer>().Any(v=>v.isPrepared&&v.isPlaying&&v.frame>3&&v.texture),40,"No real native decoded signal for "+label);
            var native=media.GetComponentsInChildren<VideoPlayer>().First(v=>v.isPrepared&&v.isPlaying&&v.frame>3&&v.texture);var evidence=new Signal{name=label,url=native.url,firstSeconds=native.time,firstFrame=native.frame};receipt.signals.Add(evidence);
            Check(native.url.StartsWith("file:",StringComparison.OrdinalIgnoreCase),"Decoded source is an offline bundled file.");string path=new Uri(native.url).LocalPath;Check(Path.GetFullPath(path).StartsWith(Path.GetFullPath(Application.streamingAssetsPath)+Path.DirectorySeparatorChar,StringComparison.Ordinal),"Decoded source stays within verified native StreamingAssets.");using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))evidence.sourceSha256=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
            yield return Until(()=>native&&native.isPlaying&&native.frame>evidence.firstFrame&&Math.Abs(native.time-evidence.firstSeconds)>.2,8,"Real decoded frame/time failed to advance for "+label);
            evidence.lastFrame=native.frame;evidence.lastSeconds=native.time;evidence.pixelVariance=Variance(native.targetTexture);Check(evidence.pixelVariance>.0001,"Actual decoded texture contains nonuniform visible pixels.");Check(media.GetComponentsInChildren<VideoPlayer>().Length<=media.DecoderLimit,"Positive job uses the existing bounded decoder pool.");
        }
        private static double Variance(RenderTexture source)
        {
            if(!source||!source.IsCreated())return 0;var old=RenderTexture.active;var small=RenderTexture.GetTemporary(32,18,0,RenderTextureFormat.ARGB32);var pixels=new Texture2D(32,18,TextureFormat.RGB24,false,true);
            try{Graphics.Blit(source,small);RenderTexture.active=small;pixels.ReadPixels(new Rect(0,0,32,18),0,0);pixels.Apply();double sum=0,squares=0;var data=pixels.GetPixels32();foreach(var c in data){double v=(c.r+c.g+c.b)/765d;sum+=v;squares+=v*v;}return squares/data.Length-Math.Pow(sum/data.Length,2);}finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(small);Object.DestroyImmediate(pixels);}
        }
        private static IEnumerator Until(Func<bool> condition,double seconds,string failure){double end=EditorApplication.timeSinceStartup+seconds;while(!condition()){if(EditorApplication.timeSinceStartup>end)throw new TimeoutException(failure);yield return null;}}
        private static bool Approach(SlopCityInteractable site)
        {
            Vector3 point=site.InteractionPoint;
            foreach(float radius in new[]{.65f,1.15f,1.75f,2.4f,2.8f})for(int direction=0;direction<24;direction++)
            {
                Vector3 offset=Quaternion.Euler(0,direction*15f,0)*Vector3.forward*radius;
                if(!Physics.Raycast(point+offset+Vector3.up*.9f,Vector3.down,out RaycastHit support,4,~0,QueryTriggerInteraction.Ignore)||support.normal.y<.8f)continue;
                Vector3 feet=support.point+Vector3.up*.025f;var display=site.GetComponent<SlopCityCityScreen>();if(display&&Vector3.Dot(display.FrontNormal,(feet+Vector3.up*1.62f-display.Display.bounds.center).normalized)<=.03f)continue;int count=Physics.OverlapCapsuleNonAlloc(feet+Vector3.up*.24f,feet+Vector3.up*1.5f,.215f,Overlaps,~0,QueryTriggerInteraction.Ignore);bool blocked=count==Overlaps.Length;for(int n=0;n<count;n++)if(Overlaps[n]&&!Overlaps[n].transform.IsChildOf(player.transform))blocked=true;if(blocked)continue;
                Vector3 eye=feet+Vector3.up*1.62f,aim=point-eye;if(aim.sqrMagnitude<.01f||aim.magnitude>3.4f)continue;count=Physics.RaycastNonAlloc(eye,aim.normalized,Hits,3.4f,~0,QueryTriggerInteraction.Ignore);if(count==Hits.Length)continue;Transform closest=null;float nearest=3.4f;for(int n=0;n<count;n++)if(Hits[n].transform&&!Hits[n].transform.IsChildOf(player.transform)&&Hits[n].distance<nearest){nearest=Hits[n].distance;closest=Hits[n].transform;}if(!closest||closest.GetComponentInParent<SlopCityInteractable>()!=site)continue;
                Place(feet,0);camera.transform.LookAt(point);Set(sandbox,"nextTargetAt",0f);typeof(SlopCitySandbox).GetField("webTargetFrame",Hidden)?.SetValue(sandbox,-1);return true;
            }return false;
        }
        private static void Place(Vector3 position,float yaw){capsule.enabled=false;player.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));capsule.enabled=true;camera.transform.localPosition=Vector3.up*1.62f;camera.transform.localRotation=Quaternion.identity;Physics.SyncTransforms();}
        private static object Get(object owner,string field)=>owner.GetType().GetField(field,Hidden).GetValue(owner);
        private static void Set(object owner,string field,object value)=>owner.GetType().GetField(field,Hidden).SetValue(owner,value);
        private static object Invoke(object owner,string method,params object[] args)=>owner.GetType().GetMethod(method,Hidden).Invoke(owner,args);
        private static void Check(bool success,string message){receipt.checks++;if(!success)throw new InvalidOperationException(message);}
        private static void CaptureDiagnostics()
        {
            try
            {
                receipt.diagnostics.Add("player="+(player?player.transform.position.ToString():"absent")+" camera="+(camera?camera.transform.position.ToString()+" forward="+camera.transform.forward:"absent")+" job="+(sandbox?sandbox.CurrentJobId:"absent"));
                if(metro)foreach(var train in metro.Trains)receipt.diagnostics.Add("train stop="+train.CurrentStop+" next="+train.NextStop+" enabled="+train.isActiveAndEnabled+" moving="+train.IsMoving+" held="+train.IsHeld+" doors="+train.DoorOpenProgress+" trip="+train.TripElapsedSeconds+" aboard="+train.PlayerInside+" pose="+train.transform.position);
                if(!media)return;
                receipt.diagnostics.Add("media="+media.Status+" enabled="+media.isActiveAndEnabled+" menu="+Get(media,"menuSuspended")+" focus="+Get(media,"focusSuspended")+" application="+Get(media,"applicationSuspended")+" manual="+Get(media,"manuallyPaused")+" candidates="+((IList)Get(media,"candidates")).Count);
                if(screen)receipt.diagnostics.Add("screen channel="+screen.Channel+" clip="+screen.ClipKey+" active="+screen.isActiveAndEnabled+" display="+screen.Display.enabled+" front="+screen.FrontNormal);
                foreach(object channel in (Array)Get(media,"channels"))if(channel!=null)
                {
                    var video=(VideoPlayer)Get(channel,"player");receipt.diagnostics.Add("pool"+Get(channel,"index")+" owner="+(Get(channel,"owner")!=null)+" advance="+Get(channel,"advance")+" ready="+Get(channel,"ready")+" loading="+Get(channel,"loading")+" resolving="+Get(channel,"resolving")+" failed="+Get(channel,"failed")+" decoder="+(video?video.url+" prepared="+video.isPrepared+" playing="+video.isPlaying+" frame="+video.frame+" time="+video.time:"none"));
                }
            }
            catch(Exception exception){receipt.diagnostics.Add("Diagnostic capture failed: "+exception.Message);}
        }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Active,false);Work.Clear();if(receipt==null)receipt=NewReceipt();if(error!=null){receipt.errors.Add(error.ToString());CaptureDiagnostics();}
            try
            {
                if(media){media.SetSuspended(true);Object.DestroyImmediate(media.gameObject);}foreach(var value in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsInactive.Include,FindObjectsSortMode.None))Object.DestroyImmediate(value);
                foreach(string key in Keys)if(SessionState.GetBool(Active+key,false))PlayerPrefs.SetString(key,SessionState.GetString(Active+key+".value",""));else PlayerPrefs.DeleteKey(key);PlayerPrefs.Save();
                receipt.savesRestored=Keys.All(key=>PlayerPrefs.HasKey(key)==SessionState.GetBool(Active+key,false)&&(!PlayerPrefs.HasKey(key)||PlayerPrefs.GetString(key)==SessionState.GetString(Active+key+".value","")));
                string playback=Path.Combine(Application.persistentDataPath,"TikTok/playback.json");if(SessionState.GetBool(Active+".playbackExists",false)){Directory.CreateDirectory(Path.GetDirectoryName(playback));File.WriteAllBytes(playback,Convert.FromBase64String(SessionState.GetString(Active+".playback","")));}else if(File.Exists(playback))File.Delete(playback);
                Time.timeScale=SessionState.GetFloat(Active+".scale",1);AudioListener.volume=SessionState.GetFloat(Active+".volume",1);
            }
            catch(Exception exception){receipt.errors.Add(exception.ToString());}
            if(!receipt.savesRestored)receipt.errors.Add("Preference backup restoration failed.");receipt.passed=receipt.errors.Count==0&&receipt.runtimeErrors.Count==0;receipt.finishedAtUtc=DateTime.UtcNow.ToString("O");
            string path=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Builds/Reports/job-media-resident-positive-check.json");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(receipt,true));Debug.Log("SLOP_CITY_JOB_MEDIA_RESIDENT_"+(receipt.passed?"PASSED":"FAILED")+" "+receipt.checks+" checks");EditorApplication.Exit(receipt.passed?0:1);
        }
        [Serializable]private sealed class OfflineConfiguration{public string feedUrl;public OfflineChannel[] channels;}
        [Serializable]private sealed class OfflineChannel{public string mediaUrl;}
        [Serializable]private sealed class Signal{public string name,url,sourceSha256;public long firstFrame,lastFrame;public double firstSeconds,lastSeconds,pixelVariance;}
        [Serializable]private sealed class Receipt
        {public bool passed,savesRestored;public string startedAtUtc,finishedAtUtc,unityVersion,graphicsDevice;public int checks,carriedLegs;public string scope="Graphics-enabled native PlayMode; saved-scene geometry and shared resident visuals, relocated isolated ordinary display, production offline catalog validation and selected unchanged video entries, production pooled local decode, real physics carried relay arrival, reachable presented resident USE and inventory outcomes. No fake observed times/completion fields, no network, source/scene save, speaker audibility, browser/phone or endurance claim.";public List<int> positiveJobs=new List<int>();public List<Signal> signals=new List<Signal>();public List<string> errors=new List<string>(),runtimeErrors=new List<string>(),editorDiagnostics=new List<string>(),diagnostics=new List<string>();}
    }
}
#endif
