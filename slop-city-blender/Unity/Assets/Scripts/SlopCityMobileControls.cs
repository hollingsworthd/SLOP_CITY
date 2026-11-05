using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace SlopCity
{
    /// <summary>Two-thumb controls shared by mobile players and touch browsers.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class SlopCityMobileControls : MonoBehaviour
    {
        private const int ViewportSlopPixels = 12;
        private const float PixelRatioSlop = 0.05f;

        private RectTransform safe, left, ring, rightLook, touchActions;
        private SlopCityPlayerInput input;
        private Rect lastSafe;
        private int lastLayoutWidth, lastLayoutHeight;
        private float lastPixelRatio, lastLayoutScaleFactor, nextSalvageCheck;
        private bool layoutApplied;

        public void RelayoutFromHud() => TryLayout(true);
        private SlopCityTouchStick moveStick;
        private SlopCityTouchLook touchLook;
        private SlopCityTouchHold touchRun;
        private readonly System.Collections.Generic.List<RectTransform> buttons = new System.Collections.Generic.List<RectTransform>();
        private Sprite disc;
        private Text cleanCaption;
        private static readonly string[] CleaningCaptions = { "CLEAN\n0/4", "CLEAN\n1/4", "CLEAN\n2/4", "CLEAN\n3/4", "CLEAN\nDONE" };

        public static GameObject Create(Transform parent, SlopCityPlayerInput source, Font font)
        {
            var root = new GameObject("MobileControls", typeof(RectTransform), typeof(SlopCityMobileControls));
            try
            {
                SlopCityMobileControls controls = root.GetComponent<SlopCityMobileControls>();
                controls.enabled = false;
                root.transform.SetParent(parent, false);
                Fill(root.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
                controls.Build(source, font);
                controls.enabled = true;
                return root;
            }
            catch
            {
                try { root.SetActive(false); }
                finally { Destroy(root); }
                throw;
            }
        }

        private void OnEnable()
        {
            if (isActiveAndEnabled && safe) StartCoroutine(RelayoutWhenCanvasReady());
        }

        private IEnumerator RelayoutWhenCanvasReady()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            layoutApplied = false;
            lastLayoutWidth = lastLayoutHeight = 0;
            TryLayout(true);
            yield return null;
            TryLayout(true);
        }

        private void Build(SlopCityPlayerInput source, Font font)
        {
            input = source;
            safe = Rect("SafeArea", transform);
            disc = MakeDisc();
            rightLook = Rect("RightSideLook", safe);
            Fill(rightLook, new Vector2(.5f, 0), Vector2.one);
            var lookImage = rightLook.gameObject.AddComponent<Image>();
            lookImage.color = Color.clear;
            lookImage.raycastTarget = true;
            touchLook = rightLook.gameObject.AddComponent<SlopCityTouchLook>();
            touchLook.Configure(source);
            touchActions = Rect("TouchActionPad", safe);
            Fill(touchActions, Vector2.zero, Vector2.one);
            touchActions.gameObject.AddComponent<Image>().color = Color.clear;
            touchActions.GetComponent<Image>().raycastTarget = false;
            left = Rect("LeftSideMove", safe);
            Fill(left, Vector2.zero, new Vector2(.48f,.80f));
            left.gameObject.AddComponent<Image>().color = Color.clear;
            ring = Rect("JoystickBase", left);
            ring.anchorMin = ring.anchorMax = new Vector2(.5f,.5f);
            var baseImage = ring.gameObject.AddComponent<Image>();
            baseImage.sprite = disc; baseImage.color = new Color(.78f,.87f,.91f,.16f); baseImage.raycastTarget = false;
            var thumb = Rect("JoystickThumb", ring);
            thumb.anchorMin = thumb.anchorMax = new Vector2(.5f,.5f);
            var thumbImage = thumb.gameObject.AddComponent<Image>();
            thumbImage.sprite = disc; thumbImage.color = new Color(.88f,.94f,.96f,.52f); thumbImage.raycastTarget = false;
            moveStick = left.gameObject.AddComponent<SlopCityTouchStick>();
            moveStick.Configure(source, thumb, ring);
            AddButton("TouchJump", "JUMP", source.RequestJump, font);
            AddButton("TouchInteract", "USE", source.RequestInteract, font);
            var run = AddButton("TouchRun", "RUN", null, font);
            touchRun = run.gameObject.AddComponent<SlopCityTouchHold>();
            touchRun.Configure(source);
            AddButton("TouchPause", "II", source.RequestPause, font);
            AddButton("TouchNewJob", "JOB", source.RequestNewContract, font);
            AddButton("TouchHome", "HOME", source.RequestRespawn, font);
            cleanCaption = AddButton("TouchClean", "CLEAN", source.RequestClean, font).GetComponentInChildren<Text>();
            AddButton("TouchTake", "TAKE", source.RequestPickup, font);
            AddButton("TouchBag", "BAG", source.RequestInventory, font);
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextSalvageCheck && buttons.Count >= 9 && isActiveAndEnabled
                && input && input.UseTouchControls && input.GameplayEnabled)
            {
                nextSalvageCheck = Time.unscaledTime + .15f;
                var player = input ? input.GetComponent<SlopCityFirstPerson>() : null;
                if (player && player.Sandbox)
                {
                    player.Sandbox.SalvageOptions(player.ViewCamera, out _, out bool take, out int cleanStrokes);
                    if (cleanCaption) cleanCaption.text = cleanStrokes < 0 ? "CLEAN"
                        : CleaningCaptions[Mathf.Clamp(cleanStrokes, 0, SlopCitySalvageItem.CleaningStrokes)];
                    buttons[7].GetComponent<Button>().interactable = take;
                }
            }
        }

        private void TryLayout(bool force)
        {
            int width = Screen.width, height = Screen.height;
            Rect safeArea = Screen.safeArea;
            float pixelRatio = SlopCityWebPresentation.DisplayPixelRatio;
            if (width <= 0 || height <= 0) return;
            if (!force && layoutApplied && !ViewportChanged(width, height, safeArea, pixelRatio)) return;
            LayoutForViewport(width, height, safeArea, pixelRatio);
        }

        private bool ViewportChanged(int width, int height, Rect safeArea, float pixelRatio)
        {
            if (!layoutApplied) return true;
            if (Mathf.Abs(lastLayoutWidth - width) >= ViewportSlopPixels
                || Mathf.Abs(lastLayoutHeight - height) >= ViewportSlopPixels) return true;
            if (Mathf.Abs(lastSafe.xMin - safeArea.xMin) >= ViewportSlopPixels
                || Mathf.Abs(lastSafe.yMin - safeArea.yMin) >= ViewportSlopPixels
                || Mathf.Abs(lastSafe.xMax - safeArea.xMax) >= ViewportSlopPixels
                || Mathf.Abs(lastSafe.yMax - safeArea.yMax) >= ViewportSlopPixels) return true;
            float scaleFactor = GetComponentInParent<Canvas>().scaleFactor;
            if (!Mathf.Approximately(lastLayoutScaleFactor, scaleFactor)) return true;
            return !Mathf.Approximately(lastPixelRatio, pixelRatio)
                && Mathf.Abs(lastPixelRatio - pixelRatio) >= PixelRatioSlop;
        }

        private void LayoutForViewport(int width, int height, Rect safeArea, float pixelRatio)
        {
            // Rotation/resizing invalidates held gestures, but a completed button
            // click must remain queued until the player consumes it.
            if (moveStick) moveStick.CancelGesture();
            if (touchLook) touchLook.CancelGesture();
            if (touchRun) touchRun.CancelGesture();
            if (input) input.ResetContinuousTouch();
            safe.anchorMin = new Vector2(safeArea.xMin/width,safeArea.yMin/height);
            safe.anchorMax = new Vector2(safeArea.xMax/width,safeArea.yMax/height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;
            if (rightLook) rightLook.offsetMin = rightLook.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            if (safe.rect.width < 1f || safe.rect.height < 1f) return;
            float pixelsPerUnit = GetComponentInParent<Canvas>().scaleFactor;
            float button = Mathf.Clamp(Mathf.Min(width,height)*.16f,54f*pixelRatio,88f*pixelRatio)/Mathf.Max(.1f,pixelsPerUnit);
            float gap = button*.18f;
            float margin = button*.24f;
            ring.sizeDelta = Vector2.one*button*1.8f;
            ring.GetChild(0).GetComponent<RectTransform>().sizeDelta = Vector2.one*button*.72f;
            float safeWidth = Mathf.Max(left.rect.width, width * (safe.anchorMax.x - safe.anchorMin.x) * .48f);
            float safeHeight = Mathf.Max(left.rect.height, height * (safe.anchorMax.y - safe.anchorMin.y) * .80f);
            ring.anchoredPosition = new Vector2(-safeWidth*.5f+button*1.25f,-safeHeight*.5f+button*1.4f);
            Position(buttons[0],new Vector2(1,0),new Vector2(-margin-button*.5f,margin+button*.5f),button);
            Position(buttons[1],new Vector2(1,0),new Vector2(-margin-button*.5f,margin+button*1.5f+gap),button);
            Position(buttons[2],new Vector2(1,0),new Vector2(-margin-button*1.5f-gap,margin+button*.5f),button);
            float small = button*.80f;
            Position(buttons[6],new Vector2(1,0),new Vector2(-margin-button*1.5f-gap,margin+button*1.5f+gap),button);
            Position(buttons[7],new Vector2(1,0),new Vector2(-margin-button*2.5f-gap*2,margin+button*1.5f+gap),button);
            Position(buttons[8],new Vector2(1,0),new Vector2(-margin-button*2.5f-gap*2,margin+button*.5f),button);
            for (int i=3;i<6;i++)
                Position(buttons[i],new Vector2(1,1),new Vector2(-margin-small*.5f-(i-3)*(small+gap*.65f),-margin-small*.5f),small);
            if (rightLook)
            {
                float padWidth = margin + button * 2.5f + gap * 2 + button;
                float padHeight = margin + button * 1.5f + gap + button;
                rightLook.offsetMin = new Vector2(0f, padHeight);
                rightLook.offsetMax = new Vector2(-padWidth, 0f);
            }
            if (touchActions) touchActions.SetAsLastSibling();
            lastSafe = safeArea;
            lastLayoutWidth = width;
            lastLayoutHeight = height;
            lastPixelRatio = pixelRatio;
            lastLayoutScaleFactor = pixelsPerUnit;
            layoutApplied = true;
        }

        private RectTransform AddButton(string name,string label,UnityAction action,Font font)
        {
            var rect = Rect(name,touchActions ? touchActions : safe);
            var image = rect.gameObject.AddComponent<Image>(); image.sprite=disc; image.color=new Color(.04f,.085f,.105f,.60f);
            var button=rect.gameObject.AddComponent<Button>();
            button.navigation=new Navigation { mode=Navigation.Mode.None };
            if(action!=null)button.onClick.AddListener(action);
            var caption=Rect("Label",rect); Fill(caption,Vector2.zero,Vector2.one);
            var text=caption.gameObject.AddComponent<Text>(); text.font=font;text.text=label;text.alignment=TextAnchor.MiddleCenter;
            text.color=new Color(.92f,.97f,1f,.95f);text.resizeTextForBestFit=true;text.resizeTextMinSize=8;text.resizeTextMaxSize=22;text.raycastTarget=false;
            buttons.Add(rect);return rect;
        }

        private static void Position(RectTransform rect,Vector2 anchor,Vector2 position,float size)
        { rect.anchorMin=rect.anchorMax=anchor;rect.anchoredPosition=position;rect.sizeDelta=Vector2.one*size; }
        private static RectTransform Rect(string name,Transform parent)
        {
            var go=new GameObject(name,typeof(RectTransform));
            try { go.transform.SetParent(parent,false); return go.GetComponent<RectTransform>(); }
            catch { Destroy(go); throw; }
        }
        private static void Fill(RectTransform rect,Vector2 min,Vector2 max)
        { rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=rect.offsetMax=Vector2.zero; }
        private static Sprite MakeDisc()
        {
            var texture=new Texture2D(64,64,TextureFormat.RGBA32,false);
            try
            {
                texture.name="TouchControlDisc";texture.filterMode=FilterMode.Bilinear;
                var pixels=new Color[64*64];
                for(int y=0;y<64;y++)for(int x=0;x<64;x++)
                { float radius=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(32,32));pixels[y*64+x]=new Color(1,1,1,Mathf.Clamp01(32-radius)); }
                texture.SetPixels(pixels);texture.Apply(false,true);
                return Sprite.Create(texture,new Rect(0,0,64,64),new Vector2(.5f,.5f),64);
            }
            catch
            {
                Destroy(texture);
                throw;
            }
        }
        private void OnDisable() { if(input){ input.SetTouchMovement(Vector2.zero);input.RequestSprint(false); } }
        private void OnDestroy(){ if(disc){Destroy(disc.texture);Destroy(disc);} }
    }

    public sealed class SlopCityTouchHold : MonoBehaviour,IPointerDownHandler,IPointerUpHandler
    {
        private SlopCityPlayerInput input;
        private int pointer=int.MinValue;
        private uint resetVersion;
        public void Configure(SlopCityPlayerInput source){input=source;}
        public void OnPointerDown(PointerEventData data)
        {
            ReconcileGesture();
            if((pointer!=int.MinValue&&pointer!=data.pointerId)||!input||!input.GameplayEnabled)return;
            pointer=data.pointerId;resetVersion=input.TouchResetVersion;input.RequestSprint(true);
        }
        private void LateUpdate(){ReconcileGesture();}
        private void ReconcileGesture()
        {
            if(pointer!=int.MinValue&&(!input||!input.GameplayEnabled
                ||resetVersion!=input.TouchResetVersion||!SlopCityPlayerInput.TouchIsActive(pointer)))Release();
        }
        public void OnPointerUp(PointerEventData data){if(pointer==data.pointerId)Release();}
        public void CancelGesture(){Release();}
        private void OnDisable(){Release();}
        private void OnApplicationFocus(bool focused){if(!focused)Release();}
        private void OnApplicationPause(bool paused){if(paused)Release();}
        private void Release(){pointer=int.MinValue;if(input)input.RequestSprint(false);}
    }
}
