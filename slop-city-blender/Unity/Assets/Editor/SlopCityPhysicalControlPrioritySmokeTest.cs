#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
namespace SlopCity.Editor
{
    /// <summary>Real interceptable beams cannot steal physical work or pawn actions.</summary>
    public static class SlopCityPhysicalControlPrioritySmokeTest
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] Keys = { "SlopCity.Sandbox.Progress.v1", "SlopCity.Sandbox.Progress.v1.recovery", "SlopCity.MatrixSearch.Progress.v1", "SlopCity.MatrixSearch.Progress.v1.recovery" };
        private static Receipt report;
        private static SlopCityFirstPerson player;
        private static SlopCitySandbox sandbox;
        private static SlopCityMatrixHunt hunt;
        private static SlopCitySignalBeams beams;
        private static Camera camera;
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || Application.companyName != "SlopCityValidation-v0116"
                || !Application.persistentDataPath.Contains("SlopCityValidation-v0116")) throw new InvalidOperationException("Isolated SlopCityValidation-v0116 batch Edit Mode required.");
            report = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
#if SLOP_CITY_WEB_INTERACTION_TEST || (UNITY_WEBGL && !UNITY_EDITOR)
            report.targetRouting = "Web";
#else
            report.targetRouting = "Native";
#endif
            var backups = Keys.ToDictionary(key => key, key => new Backup { had = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetString(key, "") });
            var random = UnityEngine.Random.state; float scale = Time.timeScale;
            SlopCityCrowd previousCrowd = SlopCityCrowd.Instance;
            Application.logMessageReceived += Log;
            try
            {
                foreach (string key in Keys) PlayerPrefs.DeleteKey(key);
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                foreach (var sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None)) sync.enabled = false;
                foreach (var media in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None)) media.enabled = false;
                foreach (var media in Object.FindObjectsByType<SlopCityMediaPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) media.enabled = false;
                player = Object.FindFirstObjectByType<SlopCityFirstPerson>(); Check(player, "Saved-city player exists."); player.enabled = false;
                player.GetComponent<CharacterController>().enabled = false;
                camera = player.ViewCamera; Set(player,"paused",false);Set(player,"seated",false);
                sandbox = player.GetComponent<SlopCitySandbox>(); if (!sandbox) sandbox = player.gameObject.AddComponent<SlopCitySandbox>();
                Set(player,"sandbox",sandbox);sandbox.Initialize(player.transform);
                // A genuine optional intercept requires no active waypoint. Suppress
                // only that assignment in this fixture; do not fake hunt readiness,
                // beam visibility, CanIntercept or the actual physical operations.
                ((IList)Get(sandbox,"steps")).Clear(); Check(!sandbox.HasWaypoint, "Fixture has no active job waypoint competing with optional contact guidance.");
                var crowd = Object.FindFirstObjectByType<SlopCityCrowd>(); Check(crowd, "Saved city has its real resident registry.");
                typeof(SlopCityCrowd).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).SetValue(null,crowd);
                sandbox.StartMatrixSearch(player);hunt=sandbox.MatrixHunt;beams=Get<SlopCitySignalBeams>(hunt,"beams");
                Check(hunt.Ready && hunt.Stage==SlopCityMatrixHunt.SearchStage.Intercept && hunt.TargetIndex>=0 && crowd.IsRedContact(hunt.TargetIndex), "Production search initializes in its real intercept stage with an actual red contact.");
                var world=Get<SlopCityJobWorld>(sandbox,"uniqueWorld");
                var supply=world.Get(0).controls[0].GetComponent<SlopCityInteractable>();
                Check(supply && supply.Kind==SlopCityInteractionKind.Inspect && supply.GetComponent<SlopCityJobDevice>(), "Actual parcel desk is a physical Inspect work control.");
                Check(Aim(supply), "Bounded camera ray reaches the actual saved-city parcel desk.");
                Beam(); string searchBefore=PlayerPrefs.GetString(Keys[2],"");
                Check(!sandbox.Prompt(camera).Contains("INTERCEPT TRANSMISSION"), "Exact work-control prompt wins while a real beam can also be intercepted.");
                Check(sandbox.InventoryCount("parcel")==0 && sandbox.Interact(camera) && sandbox.InventoryCount("parcel")==1
                    && sandbox.CaptureUniqueJobState().records[0].phase==1, "Real USE performs the parcel desk's inventory and world-work transition.");
                Check(hunt.Stage==SlopCityMatrixHunt.SearchStage.Intercept && PlayerPrefs.GetString(Keys[2],"")==searchBefore,
                    "Work action does not consume the optional intercept or mutate search persistence.");

                var pawn=Object.FindFirstObjectByType<SlopCityPawnCounter>();var counter=pawn.GetComponent<SlopCityInteractable>();
                Check(pawn && counter && counter.Kind==SlopCityInteractionKind.Inspect, "Actual Signal Yard pawn counter is a physical Inspect control.");
                Check(Aim(counter) && sandbox.CanSellAt(pawn), "Actual pawn counter has a clear nearby sight line for this bounded camera.");
                Beam(); bool opened=false; SlopCityPawnCounter openedAt=null;
                sandbox.InventoryRequested+=(at)=>{opened=true;openedAt=at;};
                Check(sandbox.Prompt(camera).Contains("PAWN SHOP"), "Pawn prompt wins over an independently interceptable beam.");
                Check(sandbox.Interact(camera) && opened && openedAt==pawn, "Real USE opens inventory at the actual sale-enabled physical counter.");
                Check(hunt.Stage==SlopCityMatrixHunt.SearchStage.Intercept && PlayerPrefs.GetString(Keys[2],"")==searchBefore,
                    "Pawn action preserves the optional intercept and search save.");

                var probe=GameObject.CreatePrimitive(PrimitiveType.Cube);probe.name="Ordinary_Inspect_Intercept_Probe";probe.transform.position=new Vector3(0,1001.4f,2);probe.transform.localScale=Vector3.one*.4f;
                var ordinary=probe.AddComponent<SlopCityInteractable>();ordinary.Configure(SlopCityInteractionKind.Inspect,"Ordinary condition check",false);
                ordinary.ConfigureInteractionPoint(probe.transform.position);sandbox.RefreshWorldGeometry();
                Check(Aim(ordinary), "A separate ordinary Inspect collider is physically aimed in the isolated probe area.");Beam();
                Check(sandbox.Prompt(camera)=="INTERCEPT TRANSMISSION", "Ordinary inspection retains its existing optional intercept prompt.");
                Check(sandbox.Interact(camera) && hunt.Stage==SlopCityMatrixHunt.SearchStage.FindContact,
                    "Real ordinary-inspection USE consumes the visible beam and advances actual search state.");
                Check(PlayerPrefs.GetString(Keys[2],"")!=searchBefore && sandbox.InventoryCount("parcel")==1,
                    "Ordinary intercept persists search progress without changing the issued parcel.");
                Check(report.runtimeErrors.Count==0,"No runtime errors or assertions.");
            }
            catch(Exception error){report.errors.Add(error.ToString());}
            finally
            {
                // Edit Mode-created MonoBehaviours need explicit lifecycle cleanup;
                // invoke the same release used on real disable/player teardown.
                if (beams) Invoke(beams,"OnDisable");
                foreach(var writer in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsInactive.Include,FindObjectsSortMode.None))Object.DestroyImmediate(writer);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                typeof(SlopCityCrowd).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).SetValue(null,previousCrowd ? previousCrowd : null);
                foreach(var pair in backups){if(pair.Value.had)PlayerPrefs.SetString(pair.Key,pair.Value.value);else PlayerPrefs.DeleteKey(pair.Key);}
                PlayerPrefs.Save();report.savesRestored=backups.All(pair=>PlayerPrefs.HasKey(pair.Key)==pair.Value.had&&(!pair.Value.had||PlayerPrefs.GetString(pair.Key)==pair.Value.value));
                Time.timeScale=scale;UnityEngine.Random.state=random;Application.logMessageReceived-=Log;
            }
            report.passed=report.errors.Count==0&&report.runtimeErrors.Count==0&&report.savesRestored;report.finishedAtUtc=DateTime.UtcNow.ToString("O");
            string output=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Builds/Reports/physical-control-priority-check.json");Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("SLOP_CITY_PHYSICAL_CONTROL_PRIORITY_"+(report.passed?"PASSED":"FAILED")+" "+report.checks+" checks");EditorApplication.Exit(report.passed?0:1);
        }
        private static bool Aim(SlopCityInteractable wanted)
        {
            Vector3 point=wanted.InteractionPoint;
            foreach(float distance in new[]{1.6f,2.2f,2.8f})for(int angle=0;angle<24;angle++)
            {
                Vector3 eye=point+Quaternion.Euler(0,angle*15,0)*Vector3.forward*distance;
                player.transform.SetPositionAndRotation(eye-Vector3.up*1.4f,Quaternion.identity);camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(point-eye));Physics.SyncTransforms();
                var hits=Physics.RaycastAll(eye,camera.transform.forward,3.4f,~0,QueryTriggerInteraction.Ignore).Where(hit=>!hit.transform.IsChildOf(player.transform)).OrderBy(hit=>hit.distance).ToArray();
                if(hits.Length==0||hits[0].transform.GetComponentInParent<SlopCityInteractable>()!=wanted)continue;
                Invalidate();object target=Invoke(sandbox,"FindTarget",camera);
                if((SlopCityInteractable)Get(target,"interaction")==wanted)return true;
            }
            return false;
        }
        private static void Beam()
        {
            Vector3 center=camera.transform.position+camera.transform.forward*.7f;
            beams.SetPath(new[]{center-camera.transform.right*.45f-Vector3.up,center+camera.transform.right*.45f-Vector3.up},0,true,Color.green);
            Invalidate();Check(beams.TryAim(camera,3.4f,out _)&&hunt.CanIntercept(camera),"Production beam sight/aim and hunt state prove a real simultaneous intercept opportunity.");
        }
        private static void Invalidate(){Set(sandbox,"nextTargetAt",0f);typeof(SlopCitySandbox).GetField("webTargetFrame",Fields)?.SetValue(sandbox,-1);}
        private static object Get(object owner,string name)=>owner.GetType().GetField(name,Fields).GetValue(owner);
        private static T Get<T>(object owner,string name)=>(T)Get(owner,name);
        private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Fields).SetValue(owner,value);
        private static object Invoke(object owner,string name,params object[] values)=>owner.GetType().GetMethod(name,Fields).Invoke(owner,values);
        private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);report.checks++;}
        private static void Log(string message,string trace,LogType kind){if(kind==LogType.Error||kind==LogType.Assert||kind==LogType.Exception)report.runtimeErrors.Add(message+"\n"+trace);}
        private sealed class Backup{public bool had;public string value;}
        [Serializable]private sealed class Receipt
        {
            public bool passed,savesRestored;public int checks;public string startedAtUtc,finishedAtUtc,unityVersion,targetRouting;
            public string scope="Isolated saved-city Edit Mode, sync/media/player Start disabled. Actual parcel desk and pawn counter, real production beam sight/aim/interception, and ordinary Inspect probe. Assignment waypoints suppressed only to permit an optional intercept opportunity; bounded camera positioning is not physical traversal. No Unity Play Mode, browser/device, playback or memory acceptance.";
            public List<string> errors=new List<string>(),runtimeErrors=new List<string>();
        }
    }
}
#endif
