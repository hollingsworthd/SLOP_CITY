#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
namespace SlopCity.Editor
{
    /// <summary>Production controls/inventory on real camera-targeted Canvases of known size.</summary>
    public static class SlopCityShortViewportSmokeTest
    {
        private const BindingFlags Hidden=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        private static Receipt report;
        public static void Run()
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode||Application.companyName!="SlopCityValidation-v0116")throw new InvalidOperationException("Isolated SlopCityValidation-v0116 batch Edit Mode required.");
            report=new Receipt{startedAtUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion};float scale=Time.timeScale;
            Application.logMessageReceived+=Log;
            try{Case(844,220);Case(390,744);Case(844,290);}
            catch(Exception error){report.errors.Add(error.ToString());}
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Time.timeScale=scale;Application.logMessageReceived-=Log;}
            report.passed=report.errors.Count==0&&report.runtimeErrors.Count==0;report.finishedAtUtc=DateTime.UtcNow.ToString("O");
            string path=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Builds/Reports/short-viewport-check.json");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));Debug.Log("SLOP_CITY_SHORT_VIEWPORT_"+(report.passed?"PASSED":"FAILED")+" "+report.checks+" checks");EditorApplication.Exit(report.passed?0:1);
        }
        private static void Case(int width,int height)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Controlled_Viewport_"+width+"x"+height);var camera=new GameObject("FixtureCamera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.transform.position=new Vector3(0,0,-10);camera.orthographic=true;camera.orthographicSize=height*.5f;
            var target=new RenderTexture(width,height,24);target.Create();camera.targetTexture=target;
            SlopCityMobileControls controls=null;GameObject actor=null;
            try
            {
                var events=new GameObject("FixtureEventSystem",typeof(EventSystem),typeof(StandaloneInputModule));events.transform.SetParent(root.transform);if(!EventSystem.current)Invoke(events.GetComponent<EventSystem>(),"OnEnable");
                actor=new GameObject("Disabled_Player");actor.transform.SetParent(root.transform);var player=actor.AddComponent<SlopCityFirstPerson>();player.enabled=false;var input=actor.GetComponent<SlopCityPlayerInput>();if(!input)input=actor.AddComponent<SlopCityPlayerInput>();Set(player,"playerInput",input);Set(input,"forceTouchControlsForTesting",true);input.SetGameplayEnabled(true);
                var canvas=Canvas("ControlledMobileCanvas",root.transform,camera);controls=SlopCityMobileControls.Create(canvas.transform,input,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")).GetComponent<SlopCityMobileControls>();
                Invoke(controls,"LayoutForViewport",width,height,new Rect(0,0,width,height),1f);UnityEngine.Canvas.ForceUpdateCanvases();
                Check(Mathf.Abs(((RectTransform)canvas.transform).rect.width-width)<.1f&&Mathf.Abs(((RectTransform)canvas.transform).rect.height-height)<.1f,"Real camera-targeted mobile Canvas has exact requested dimensions.");
                var buttons=controls.GetComponentsInChildren<Button>();Check(buttons.Length==10,"Production MobileControls creates all ten owned actions.");Layout(buttons,camera,width,height);
                foreach(string name in new[]{"TouchClean","TouchTake","TouchBag"})
                {
                    Button button=buttons.First(b=>b.name==name);Click(button,camera);bool queued=name=="TouchClean"?input.ConsumeClean():name=="TouchTake"?input.ConsumePickup():input.ConsumeInventory();Check(queued,"Real GraphicRaycaster/pointer dispatch queues "+name+" exactly once.");
                }
                float buttonSize=Mathf.Clamp(Mathf.Min(width,height)*.16f,54,88),gap=buttonSize*.18f,margin=buttonSize*.24f;
                var take=buttons.First(b=>b.name=="TouchTake").GetComponent<RectTransform>();var bag=buttons.First(b=>b.name=="TouchBag").GetComponent<RectTransform>();
                bool compact=width>height&&height<260;
                Check(Mathf.Abs(take.anchoredPosition.x-(-margin-buttonSize*2.5f-gap*2))<.1f,"TAKE stays in the third column in every orientation.");
                Check(Mathf.Abs(bag.anchoredPosition.y-(margin+buttonSize*.5f))<.1f,"BAG stays in the lower row in every orientation.");
                canvas.gameObject.SetActive(false);
                var sandbox=actor.AddComponent<SlopCitySandbox>();for(int i=0;i<9;i++)Check((bool)Invoke(sandbox,"InventoryAdd","layout/"+i,"Recovered layout object "+i,"fixture",1,8,i%5),"Bounded in-memory layout row fits inventory.");
                SlopCitySalvageHud.Ensure(player,sandbox,input,camera);var hud=actor.GetComponent<SlopCitySalvageHud>();var inventoryCanvas=actor.GetComponentsInChildren<UnityEngine.Canvas>(true).Single();Target(inventoryCanvas,camera);
                Invoke(hud,"LayoutForViewport",width,height,new Rect(0,0,width,height),1f);Invoke(hud,"Open",new object[]{null});UnityEngine.Canvas.ForceUpdateCanvases();
                var panel=Get<GameObject>(hud,"panel");Rect box=Rect(panel.GetComponent<RectTransform>(),camera);Check(box.xMin>=-.1&&box.yMin>=-.1&&box.xMax<=width+.1&&box.yMax<=height+.1,"Actual inventory card stays inside controlled viewport.");
                var inventoryButtons=panel.GetComponentsInChildren<Button>().Where(b=>b.gameObject.activeInHierarchy).ToArray();Layout(inventoryButtons,camera,width,height);
                Text summary=Get<Text>(hud,"summary");Check(compact?!summary.text.Contains("\n"):summary.text.Contains("\n"),"Only short landscape compacts the summary; normal detailed bag hint remains.");
                Check(Get<int>(hud,"visibleRows")>=1,"At least one full inventory row remains visible.");Click(inventoryButtons.First(b=>b.name=="CLOSE"),camera);Check(!player.InventoryOpen,"Production CLOSE remains reachable and resumes gameplay.");
                InventoryModal(player,input,sandbox,hud,inventoryCanvas,camera,root.transform);
                report.viewports.Add(width+"x"+height);
            }
            finally
            {
                foreach(var ownedControls in root.GetComponentsInChildren<SlopCityMobileControls>(true))
                {
                    // Runtime teardown uses deferred Destroy; this fixture runs in Edit Mode.
                    Sprite disc=Get<Sprite>(ownedControls,"disc");Set(ownedControls,"disc",null);
                    if(disc){Texture2D texture=disc.texture;Object.DestroyImmediate(disc);if(texture)Object.DestroyImmediate(texture);}
                }
                if(actor){var hud=actor.GetComponent<SlopCitySalvageHud>();if(hud)Invoke(hud,"OnDestroy");}
                foreach(var raycaster in root.GetComponentsInChildren<GraphicRaycaster>(true))Invoke(raycaster,"OnDisable");var events=root.GetComponentInChildren<EventSystem>();if(events)Invoke(events,"OnDisable");camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(root);Time.timeScale=1;
            }
        }
        private static void InventoryModal(SlopCityFirstPerson player,SlopCityPlayerInput input,SlopCitySandbox sandbox,SlopCitySalvageHud bag,UnityEngine.Canvas inventoryCanvas,Camera camera,Transform parent)
        {
            var canvas=Canvas("Production_Gameplay_HUD",parent,camera);
            var hud=canvas.gameObject.AddComponent<SlopCityHud>();hud.Initialize(player,input);
            Set(player,"hud",hud);Set(player,"started",true);Set(sandbox,"user",player.transform);
            hud.SetMenu(false,true);
            var gameplay=Get<GameObject>(hud,"gameplayLayer");var touch=Get<GameObject>(hud,"touchControls");var panel=Get<GameObject>(bag,"panel");
            Check(canvas.enabled&&gameplay.activeInHierarchy&&touch.activeInHierarchy,"Production gameplay HUD and touch controls begin visible.");
            var counter=new GameObject("Physical_Modal_Pawn_Counter",typeof(BoxCollider),typeof(SlopCityPawnCounter));counter.transform.SetParent(parent);counter.transform.position=player.transform.position+Vector3.forward;
            var pawn=counter.GetComponent<SlopCityPawnCounter>();Check(sandbox.CanSellAt(pawn),"Pawn modal uses a real reachable physical counter.");
            foreach(var shop in new SlopCityPawnCounter[]{null,pawn})
            {
                Invoke(bag,"Open",new object[]{shop});
                Check(player.InventoryOpen&&player.IsPaused&&!input.GameplayEnabled,"Opening bag/pawn pauses movement and retains modal ownership.");
                Check(!canvas.enabled&&!gameplay.activeInHierarchy&&!touch.activeInHierarchy,"Bag/pawn immediately hides gameplay labels and all touch actions.");
                Check(inventoryCanvas.enabled&&panel.activeInHierarchy,"Independent inventory Canvas remains visible.");
                hud.SetMenu(false,true);Invoke(hud,"Update");
                Check(!canvas.enabled&&!gameplay.activeInHierarchy&&!touch.activeInHierarchy,"Dismissing the pause card and later HUD updates cannot re-enable gameplay beneath inventory.");
                if(shop)Check(Get<Text>(bag,"heading").text.Contains("PAWN SHOP"),"Reachable pawn uses the actual shop inventory mode.");
                Click(Get<Button>(bag,"close"),camera);Invoke(bag,"Update");
                Check(!panel.activeInHierarchy,"Inventory CLOSE retires its modal panel on the normal HUD update.");
                Check(!player.InventoryOpen&&!player.IsPaused&&input.GameplayEnabled,"Actual inventory CLOSE restores gameplay input.");
                Invoke(hud,"Update");
                Check(canvas.enabled&&gameplay.activeInHierarchy&&touch.activeInHierarchy,"Closing bag/pawn restores the gameplay HUD and touch controls.");
            }
            Set(hud,"immersiveView",true);Invoke(hud,"Update");Check(!canvas.enabled,"Existing immersive preference hides the normal HUD before inventory.");
            Invoke(bag,"Open",new object[]{null});hud.SetMenu(false,true);Invoke(hud,"Update");
            Check(inventoryCanvas.enabled&&panel.activeInHierarchy&&!canvas.enabled&&!touch.activeInHierarchy,"Inventory remains usable over immersive HUD with touch controls suppressed.");
            Click(Get<Button>(bag,"close"),camera);Invoke(bag,"Update");Invoke(hud,"Update");
            Check(!panel.activeInHierarchy,"Immersive inventory CLOSE retires its modal panel.");
            Check(Get<bool>(hud,"immersiveView")&&!canvas.enabled,"Closing inventory preserves the prior immersive HUD preference.");
            Set(hud,"immersiveView",false);Invoke(hud,"Update");
            hud.SetMenu(true,true);Check(canvas.enabled&&!gameplay.activeInHierarchy&&!touch.activeInHierarchy,"Normal pause menu keeps its Canvas without gameplay controls.");
            hud.SetMenu(false,true);hud.ShowSourceTitleOnly();Check(canvas.enabled&&!gameplay.activeInHierarchy&&!touch.activeInHierarchy,"Media modal retains its Canvas and suppresses gameplay controls.");
            hud.HideSource();Check(canvas.enabled&&gameplay.activeInHierarchy&&touch.activeInHierarchy,"Closing media restores normal gameplay visibility.");
        }
        private static UnityEngine.Canvas Canvas(string name,Transform parent,Camera camera)
        {var go=new GameObject(name,typeof(RectTransform),typeof(UnityEngine.Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));go.transform.SetParent(parent,false);var canvas=go.GetComponent<UnityEngine.Canvas>();Target(canvas,camera);return canvas;}
        private static void Target(UnityEngine.Canvas canvas,Camera camera)
        {canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;scaler.scaleFactor=1;canvas.scaleFactor=1;Invoke(canvas.GetComponent<GraphicRaycaster>(),"OnEnable");UnityEngine.Canvas.ForceUpdateCanvases();}
        private static void Layout(Button[] buttons,Camera camera,int width,int height)
        {
            for(int i=0;i<buttons.Length;i++)
            {Rect a=Rect(buttons[i].GetComponent<RectTransform>(),camera);Check(a.xMin>=-.1&&a.yMin>=-.1&&a.xMax<=width+.1&&a.yMax<=height+.1,"Target inside viewport: "+buttons[i].name);
                bool top=buttons[i].name=="TouchPause"||buttons[i].name=="TouchNewJob"||buttons[i].name=="TouchHome";
                float minimum=top?43.2f:54f;Check(a.width>=minimum-.1f&&a.height>=minimum-.1f,"Target retains at least "+minimum+" pixel area: "+buttons[i].name);
                for(int j=0;j<i;j++)Check(!a.Overlaps(Rect(buttons[j].GetComponent<RectTransform>(),camera)),"Targets do not overlap: "+buttons[i].name+"/"+buttons[j].name);}
        }
        private static Rect Rect(RectTransform transform,Camera camera)
        {var corners=new Vector3[4];transform.GetWorldCorners(corners);var a=RectTransformUtility.WorldToScreenPoint(camera,corners[0]);var b=RectTransformUtility.WorldToScreenPoint(camera,corners[2]);return UnityEngine.Rect.MinMaxRect(a.x,a.y,b.x,b.y);}
        private static void Click(Button button,Camera camera)
        {
            // A synchronous Edit Mode Canvas layout has not yet been drawn. UGUI
            // rejects Graphics with native absoluteDepth==-1, so render the real
            // camera-targeted Canvas before exercising its production raycaster.
            UnityEngine.Canvas.ForceUpdateCanvases();camera.Render();
            var pointer=new PointerEventData(EventSystem.current){position=Rect(button.GetComponent<RectTransform>(),camera).center,button=PointerEventData.InputButton.Left};
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
            var canvas=button.GetComponentInParent<UnityEngine.Canvas>();
            var diagnostic=new PointerDiagnostic{target=button.name,screen=Screen.width+"x"+Screen.height,cameraPixels=camera.pixelRect.ToString(),canvasRect=((RectTransform)canvas.transform).rect.ToString(),pointer=pointer.position.ToString(),viewport=camera.ScreenToViewportPoint(pointer.position).ToString()};
            foreach(var hit in hits)diagnostic.hits.Add(hit.gameObject.name+" depth="+hit.depth+" distance="+hit.distance+" rect="+Rect(hit.gameObject.GetComponent<RectTransform>(),camera));
            var graphics=GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas);
            for(int i=0;i<graphics.Count;i++){var graphic=graphics[i];diagnostic.graphics.Add(graphic.name+" depth="+graphic.depth+" culled="+graphic.canvasRenderer.cull+" enabled="+graphic.isActiveAndEnabled+" rect="+Rect(graphic.rectTransform,camera));}
            report.pointerDiagnostics.Add(diagnostic);
            Check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<Button>()==button,"Actual GraphicRaycaster selects intended topmost "+button.name);
            ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
        }
        private static object Invoke(object owner,string name,params object[] values)=>owner.GetType().GetMethod(name,Hidden).Invoke(owner,values);
        private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Hidden).SetValue(owner,value);
        private static T Get<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Hidden).GetValue(owner);
        private static void Check(bool pass,string message){if(!pass)throw new InvalidOperationException(message);report.checks++;}
        private static void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.runtimeErrors.Add(message+"\n"+stack);}
        [Serializable]private sealed class Receipt
        {public bool passed;public int checks;public string startedAtUtc,finishedAtUtc,unityVersion;public List<string> errors=new List<string>(),runtimeErrors=new List<string>(),viewports=new List<string>();public List<PointerDiagnostic> pointerDiagnostics=new List<PointerDiagnostic>();public string scope="Isolated Edit Mode production control/inventory components on actual camera-targeted Canvases at844x220,390x744,844x290. Explicit private viewport-layout inputs; Screen itself is not resized. Canvas is actually rendered before GraphicRaycaster/pointer routing. Target geometry only, no complete game HUD/SIGNAL text, browser/device, gameplay, media, save or memory acceptance.";}
        [Serializable]private sealed class PointerDiagnostic
        {public string target,screen,cameraPixels,canvasRect,pointer,viewport;public List<string> hits=new List<string>(),graphics=new List<string>();}
    }
}
#endif
