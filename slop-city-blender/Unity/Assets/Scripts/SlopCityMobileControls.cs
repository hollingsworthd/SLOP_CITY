using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace SlopCity
{
    /// <summary>Two-thumb controls shared by mobile players and touch browsers.</summary>
    public sealed class SlopCityMobileControls : MonoBehaviour
    {
        private RectTransform safe, left, ring;
        private SlopCityPlayerInput input;
        private Rect lastSafe;
        private Vector2 lastSize;
        private float lastPixelRatio, nextSalvageCheck;
        private readonly System.Collections.Generic.List<RectTransform> buttons = new System.Collections.Generic.List<RectTransform>();
        private Sprite disc;
        private Text cleanCaption;
        private static readonly string[] CleaningCaptions = { "CLEAN\n0/4", "CLEAN\n1/4", "CLEAN\n2/4", "CLEAN\n3/4", "CLEAN\nDONE" };

        public static GameObject Create(Transform parent, SlopCityPlayerInput source, Font font)
        {
            var root = new GameObject("MobileControls", typeof(RectTransform), typeof(SlopCityMobileControls));
            root.transform.SetParent(parent, false);
            Fill(root.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            root.GetComponent<SlopCityMobileControls>().Build(source, font);
            return root;
        }

        private void Build(SlopCityPlayerInput source, Font font)
        {
            input = source;
            safe = Rect("SafeArea", transform);
            disc = MakeDisc();
            var look = Rect("RightSideLook", safe);
            Fill(look, new Vector2(.5f, 0), Vector2.one);
            look.gameObject.AddComponent<Image>().color = Color.clear;
            look.gameObject.AddComponent<SlopCityTouchLook>().Configure(source);
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
            left.gameObject.AddComponent<SlopCityTouchStick>().Configure(source,thumb,ring);
            AddButton("TouchJump", "JUMP", source.RequestJump, font);
            AddButton("TouchInteract", "USE", source.RequestInteract, font);
            var run = AddButton("TouchRun", "RUN", null, font);
            run.gameObject.AddComponent<SlopCityTouchHold>().Configure(source);
            AddButton("TouchMedia", "MEDIA", source.RequestMedia, font);
            AddButton("TouchPause", "II", source.RequestPause, font);
            AddButton("TouchNewJob", "JOB", source.RequestNewContract, font);
            AddButton("TouchHome", "HOME", source.RequestRespawn, font);
            cleanCaption = AddButton("TouchClean", "CLEAN", source.RequestClean, font).GetComponentInChildren<Text>();
            AddButton("TouchTake", "TAKE", source.RequestPickup, font);
            AddButton("TouchBag", "BAG", source.RequestInventory, font);
            Layout();
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextSalvageCheck && buttons.Count >= 10)
            {
                nextSalvageCheck = Time.unscaledTime + .15f;
                var player = input ? input.GetComponent<SlopCityFirstPerson>() : null;
                if (player && player.Sandbox)
                {
                    player.Sandbox.SalvageOptions(player.ViewCamera, out _, out bool take, out int cleanStrokes);
                    // Keep CLEAN responsive so an out-of-reach or unsuitable target
                    // explains what to do instead of silently swallowing the tap.
                    if (cleanCaption) cleanCaption.text = cleanStrokes < 0 ? "CLEAN"
                        : CleaningCaptions[Mathf.Clamp(cleanStrokes, 0, SlopCitySalvageItem.CleaningStrokes)];
                    buttons[8].GetComponent<Button>().interactable = take;
                }
            }
            RectTransform rect = (RectTransform)transform;
            if (lastSafe != Screen.safeArea || lastSize != rect.rect.size
                || !Mathf.Approximately(lastPixelRatio,SlopCityWebPresentation.DisplayPixelRatio)) Layout();
        }

        private void Layout() => LayoutForViewport(Screen.width, Screen.height, Screen.safeArea, SlopCityWebPresentation.DisplayPixelRatio);

        private void LayoutForViewport(int width, int height, Rect safeArea, float pixelRatio)
        {
            // Rotation/resizing invalidates held gestures, but a completed button
            // click must remain queued until the player consumes it.
            foreach (var stick in GetComponentsInChildren<SlopCityTouchStick>()) stick.CancelGesture();
            foreach (var look in GetComponentsInChildren<SlopCityTouchLook>()) look.CancelGesture();
            foreach (var hold in GetComponentsInChildren<SlopCityTouchHold>()) hold.CancelGesture();
            if (input) input.ResetContinuousTouch();
            lastSafe = safeArea;
            lastSize = ((RectTransform)transform).rect.size;
            if (width <= 0 || height <= 0) return;
            safe.anchorMin = new Vector2(lastSafe.xMin/width,lastSafe.yMin/height);
            safe.anchorMax = new Vector2(lastSafe.xMax/width,lastSafe.yMax/height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            // Pixel-sized targets keep a comfortable thumb area even on narrow portrait screens.
            float pixelsPerUnit = GetComponentInParent<Canvas>().scaleFactor;
            lastPixelRatio = pixelRatio;
            float button = Mathf.Clamp(Mathf.Min(width,height)*.16f,54f*pixelRatio,88f*pixelRatio)/Mathf.Max(.1f,pixelsPerUnit);
            float gap = button*.18f;
            float margin = button*.24f;
            ring.sizeDelta = Vector2.one*button*1.8f;
            ring.GetChild(0).GetComponent<RectTransform>().sizeDelta = Vector2.one*button*.72f;
            // The first touch anywhere in the left region relocates this joystick.
            ring.anchoredPosition = new Vector2(-left.rect.width*.5f+button*1.25f,-left.rect.height*.5f+button*1.4f);
            Position(buttons[0],new Vector2(1,0),new Vector2(-margin-button*.5f,margin+button*.5f),button);
            Position(buttons[1],new Vector2(1,0),new Vector2(-margin-button*.5f,margin+button*1.5f+gap),button);
            Position(buttons[2],new Vector2(1,0),new Vector2(-margin-button*1.5f-gap,margin+button*.5f),button);
            float small = button*.80f;
            Position(buttons[7],new Vector2(1,0),new Vector2(-margin-button*1.5f-gap,margin+button*1.5f+gap),button);
            // Keep all six lower actions in three columns and two rows.
            Position(buttons[8],new Vector2(1,0),new Vector2(-margin-button*2.5f-gap*2,margin+button*1.5f+gap),button);
            Position(buttons[9],new Vector2(1,0),new Vector2(-margin-button*2.5f-gap*2,margin+button*.5f),button);
            for (int i=3;i<7;i++)
                Position(buttons[i],new Vector2(1,1),new Vector2(-margin-small*.5f-(i-3)*(small+gap*.65f),-margin-small*.5f),small);
        }

        private RectTransform AddButton(string name,string label,UnityAction action,Font font)
        {
            var rect = Rect(name,safe);
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
        { var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return go.GetComponent<RectTransform>(); }
        private static void Fill(RectTransform rect,Vector2 min,Vector2 max)
        { rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=rect.offsetMax=Vector2.zero; }
        private static Sprite MakeDisc()
        {
            var texture=new Texture2D(64,64,TextureFormat.RGBA32,false) { name="TouchControlDisc",filterMode=FilterMode.Bilinear };
            try
            {
                var pixels=new Color[64*64];
                for(int y=0;y<64;y++)for(int x=0;x<64;x++)
                { float radius=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(32,32));pixels[y*64+x]=new Color(1,1,1,Mathf.Clamp01(32-radius)); }
                texture.SetPixels(pixels);texture.Apply(false,true);
                return Sprite.Create(texture,new Rect(0,0,64,64),new Vector2(.5f,.5f),64);
            }
            catch
            {
                // Build has not received the sprite yet, so OnDestroy cannot own
                // this texture if pixel upload or sprite creation fails.
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
        public void Configure(SlopCityPlayerInput source){input=source;}
        public void OnPointerDown(PointerEventData data)
        {if(pointer!=int.MinValue||!input||!input.GameplayEnabled)return;pointer=data.pointerId;input.RequestSprint(true);}
        public void OnPointerUp(PointerEventData data){if(pointer==data.pointerId)Release();}
        public void CancelGesture(){Release();}
        private void OnDisable(){Release();}
        private void OnApplicationFocus(bool focused){if(!focused)Release();}
        private void Release(){pointer=int.MinValue;if(input)input.RequestSprint(false);}
    }
}
