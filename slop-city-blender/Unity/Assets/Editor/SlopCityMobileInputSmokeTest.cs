#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SlopCity.Editor
{
    [InitializeOnLoad]
    public static class SlopCityMobileInputSmokeTest
    {
        [Serializable] private sealed class Receipt
        { public bool passed; public string checkedAtUtc; public int checks; public List<string> errors=new List<string>(); }
        private static Receipt report;
        private static void Check(bool success,string reason){report.checks++;if(!success)report.errors.Add(reason);}
        private const string ActiveKey="SlopCity.MobileSmoke.Active";
        static SlopCityMobileInputSmokeTest(){ EditorApplication.update+=Tick; }
        public static void Run()
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Use a dedicated batch Editor for mobile input checks.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            SessionState.SetBool(ActiveKey,true);
            EditorApplication.EnterPlaymode();
        }
        private static void Tick()
        {
            if(!SessionState.GetBool(ActiveKey,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
            SessionState.SetBool(ActiveKey,false); RunChecks();
        }
        private static void RunChecks()
        {
            report=new Receipt {checkedAtUtc=DateTime.UtcNow.ToString("O")};
            try
            {
                var go=new GameObject("MobileInputFixture");
                var input=go.AddComponent<SlopCityPlayerInput>();
                typeof(SlopCityPlayerInput).GetField("forceTouchControlsForTesting",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(input,true);
                Check(input.UseTouchControls,"Touch fixture activates touch input.");
                input.SetGameplayEnabled(true);
                input.SetTouchMovement(new Vector2(1,1));
                Check(Mathf.Abs(input.ReadMovement().magnitude-1)<.001f,"Diagonal touch movement is normalized.");
                input.RequestJump();Check(input.ConsumeJump(),"Jump button reaches gameplay.");Check(!input.ConsumeJump(),"Jump fires once per press.");
                input.RequestInteract();Check(input.ConsumeInteract(),"Use button reaches interaction.");Check(!input.ConsumeInteract(),"Use fires once per press.");
                Check(typeof(SlopCityPlayerInput).GetMethod("ConsumeMedia")==null && typeof(SlopCityPlayerInput).GetMethod("RequestMedia")==null,
                    "No global keyboard or touch media action bypasses the living-room television.");
                input.RequestNewContract();Check(input.ConsumeNewContract(),"Job button requests new contract.");Check(!input.ConsumeNewContract(),"Job fires once per press.");
                input.RequestRespawn();Check(input.ConsumeRespawn(),"Home button reaches return action.");
                input.RequestSprint(true);Check(input.ReadSprint(),"Run can be held with movement.");input.RequestSprint(false);Check(!input.ReadSprint(),"Run releases.");
                input.AddTouchLook(new Vector2(12,8));Vector2 look=input.ReadLook();
                Check(look.x>0&&look.y>0,"Look drag reaches camera with correct axes.");Check(input.ReadLook()==Vector2.zero,"Look delta consumes once.");
                input.RequestJump();input.RequestInteract();input.AddTouchLook(Vector2.one);input.RequestSprint(true);
                input.SetGameplayEnabled(false);input.RequestJump();input.RequestInteract();input.SetTouchMovement(Vector2.one);
                Check(input.ReadMovement()==Vector2.zero&&!input.ReadSprint()&&input.ReadLook()==Vector2.zero,"Paused gameplay blocks movement, sprint and look.");
                Check(!input.ConsumeJump()&&!input.ConsumeInteract(),"Paused controls do not queue gameplay actions.");
                input.RequestPause();Check(input.ConsumePause(),"Pause action remains available independently.");
                input.SetGameplayEnabled(true);Check(!input.ConsumeJump()&&input.ReadMovement()==Vector2.zero,"Resume does not reuse a held action.");
                var events=new GameObject("EventSystem",typeof(EventSystem)).GetComponent<EventSystem>();
                var canvas=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
                canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
                var controls=SlopCityMobileControls.Create(canvas.transform,input,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                Canvas.ForceUpdateCanvases();
                Check(controls.GetComponentsInChildren<Button>().Length==9,"Mobile overlay includes jump/use/run/pause/job/home/clean/take/bag without global media access.");
                var stick=controls.GetComponentInChildren<SlopCityTouchStick>();var drag=controls.GetComponentInChildren<SlopCityTouchLook>();
                Check(stick&&drag,"Independent left movement and right look surfaces exist.");
                var stickRect=stick.GetComponent<RectTransform>();
                var center=RectTransformUtility.WorldToScreenPoint(null,stickRect.position);
                // Synthetic pointer events are mouse-like: no real Input.GetTouch entry.
                // Actual touch liveness/cancel recovery is covered by test_touch_ownership.py.
                var first=new PointerEventData(events){pointerId=-10,position=center};
                var second=new PointerEventData(events){pointerId=-11,position=center};
                stick.OnPointerDown(first);first.position+=new Vector2(60,30);stick.OnDrag(first);
                Vector2 before=input.ReadMovement();Check(before.magnitude>0,"Left drag moves player.");
                second.position+=new Vector2(-100,0);stick.OnPointerDown(second);stick.OnDrag(second);stick.OnPointerUp(second);
                Check(Vector2.Distance(before,input.ReadMovement())<.001f,"Second finger cannot steal or release the movement stick.");
                var right=new PointerEventData(events){pointerId=-11,delta=new Vector2(14,-6)};
                drag.OnPointerDown(right);drag.OnDrag(right);
                Check(input.ReadLook().x>0&&input.ReadMovement().magnitude>0,"Two fingers move and look simultaneously.");
                stick.OnPointerUp(first);Check(input.ReadMovement()==Vector2.zero,"Movement stops on owning finger release.");
                drag.OnPointerUp(right);drag.OnDrag(right);Check(input.ReadLook()==Vector2.zero,"Look stops on owning finger release.");
                var hold=controls.GetComponentInChildren<SlopCityTouchHold>();
                hold.OnPointerDown(first);Check(input.ReadSprint(),"Run hold starts sprint.");hold.OnPointerUp(second);Check(input.ReadSprint(),"Other finger cannot cancel held run.");hold.OnPointerUp(first);Check(!input.ReadSprint(),"Run ends when owning finger releases.");
                stick.OnPointerDown(first);first.position+=Vector2.one*30;stick.OnDrag(first);hold.OnPointerDown(second);
                hold.CancelGesture();hold.OnPointerDown(first);hold.OnPointerUp(second);
                Check(input.ReadSprint(),"A fresh finger can own RUN after rotation cancels prior gestures.");hold.OnPointerUp(first);
                // Invalidate ownership through the input reset alone: controls may
                // remain active while pause/focus/layout interrupts a gesture.
                // No component focus callback or pointer-up is delivered here.
                foreach (string interruption in new[]{"pause", "focus", "layout"})
                {
                    stick.CancelGesture();drag.CancelGesture();hold.CancelGesture();
                    stick.OnPointerDown(first);first.position+=Vector2.one*30;stick.OnDrag(first);
                    drag.OnPointerDown(second);second.delta=new Vector2(14,-6);drag.OnDrag(second);
                    hold.OnPointerDown(first);
                    Check(input.ReadMovement().magnitude>0&&input.ReadLook().magnitude>0&&input.ReadSprint(),interruption+" fixture starts all held inputs.");
                    if(interruption=="pause"){input.SetGameplayEnabled(false);input.SetGameplayEnabled(true);}
                    else if(interruption=="focus")typeof(SlopCityPlayerInput).GetMethod("OnApplicationFocus",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(input,new object[]{false});
                    else input.ResetContinuousTouch();
                    stick.OnDrag(first);drag.OnDrag(second);
                    typeof(SlopCityTouchHold).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(hold,null);
                    Check(input.ReadMovement()==Vector2.zero&&input.ReadLook()==Vector2.zero&&!input.ReadSprint(),interruption+" invalidates stale ownership without pointer-up.");
                    stick.OnPointerDown(first);first.position+=Vector2.one*20;stick.OnDrag(first);
                    drag.OnPointerDown(second);drag.OnDrag(second);hold.OnPointerDown(first);
                    Check(input.ReadMovement().magnitude>0&&input.ReadLook().magnitude>0&&input.ReadSprint(),interruption+" permits fresh presses with reused pointer IDs.");
                }
                controls.SetActive(false);Check(input.ReadMovement()==Vector2.zero&&!input.ReadSprint(),"Opening menu clears held mobile controls.");
                input.SetTouchMovement(Vector2.one);input.AddTouchLook(Vector2.one);input.RequestJump();input.RequestSprint(true);input.RequestPause();
                typeof(SlopCityPlayerInput).GetMethod("OnApplicationFocus",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(input,new object[]{false});
                Check(input.ReadMovement()==Vector2.zero&&input.ReadLook()==Vector2.zero&&!input.ReadSprint()&&!input.ConsumeJump()&&!input.ConsumePause(),"App focus loss clears all touch state.");
                UnityEngine.Object.DestroyImmediate(canvas);UnityEngine.Object.DestroyImmediate(events.gameObject);UnityEngine.Object.DestroyImmediate(go);
            }
            catch(Exception error){report.errors.Add(error.ToString());}
            report.passed=report.errors.Count==0;
            Directory.CreateDirectory("Builds/Reports");File.WriteAllText("Builds/Reports/mobile-input-check.json",JsonUtility.ToJson(report,true));
            Debug.Log("SLOP_CITY_MOBILE_INPUT "+JsonUtility.ToJson(report));EditorApplication.Exit(report.passed?0:1);
        }
    }
}
#endif
