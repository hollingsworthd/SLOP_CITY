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
        private const float GeometrySettleSeconds = .12f;

        private RectTransform safe, left, ring, rightLook, touchActions;
        private SlopCityPlayerInput input;
        private Rect lastSafe;
        private int lastLayoutWidth, lastLayoutHeight;
        private float lastPixelRatio, lastLayoutScaleFactor, nextSalvageCheck, geometrySettleAt;
        private bool layoutApplied, geometrySettlePending;
        private uint layoutGeneration = 1;
#if UNITY_WEBGL && !UNITY_EDITOR
        // Keep controls on their last known-good Canvas geometry while WebKit
        // replaces the canvas backing store. The HUD follows the same rule;
        // controls also update independently, so they need their own gate.
        private bool viewportTransitionActive;
#endif

        // HUD can ask after its scaler changes, but it must not evict a just-started
        // two-thumb gesture when the effective geometry did not change.
        public void RelayoutFromHud() => TryLayout(false);
        private SlopCityTouchStick moveStick;
        private SlopCityTouchLook touchLook;
        private SlopCityTouchHold touchRun;
        private readonly System.Collections.Generic.List<RectTransform> buttons = new System.Collections.Generic.List<RectTransform>();
        private readonly System.Collections.Generic.List<SlopCityTouchAction> actions = new System.Collections.Generic.List<SlopCityTouchAction>();
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
            geometrySettlePending = false;
            lastLayoutWidth = lastLayoutHeight = 0;
            TryLayout(false);
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
            ApplyTouchLayoutGeneration();
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
            TryLayout(false);
        }

        private void TryLayout(bool force)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (HoldLastLayoutForViewportTransition()) return;
            // The first stable tick after a browser transition must lay out once,
            // even if its dimensions happen to fall within the normal slop.
            bool settledViewportTransition = viewportTransitionActive;
            if (settledViewportTransition)
            {
                viewportTransitionActive = false;
                geometrySettlePending = true;
                geometrySettleAt = Time.unscaledTime + GeometrySettleSeconds;
            }
#else
            const bool settledViewportTransition = false;
#endif
            int width = Screen.width, height = Screen.height;
            Rect safeArea = Screen.safeArea;
            float pixelRatio = SlopCityWebPresentation.DisplayPixelRatio;
            if (width <= 0 || height <= 0) return;
            bool changed = !layoutApplied || ViewportChanged(width, height, safeArea, pixelRatio);
            if (changed || force || settledViewportTransition)
            {
                // Apply each intermediate browser geometry immediately, but do not
                // throw away held controls until the visual viewport has stopped
                // settling. Mobile browsers commonly report several dimensions
                // while rotating or dismissing their chrome.
                bool hadLayout = layoutApplied;
                LayoutForViewport(width, height, safeArea, pixelRatio);
                // Entering the browser transition already invalidated every
                // pre-transition gesture. Do not repeatedly advance generation
                // while installing its one settled replacement layout.
                if (hadLayout && changed && !settledViewportTransition)
                {
                    unchecked { layoutGeneration++; if (layoutGeneration == 0) layoutGeneration = 1; }
                    geometrySettlePending = true;
                    geometrySettleAt = Time.unscaledTime + GeometrySettleSeconds;
                    ApplyTouchLayoutGeneration();
                }
                return;
            }
            if (geometrySettlePending && Time.unscaledTime >= geometrySettleAt)
            {
                geometrySettlePending = false;
                ApplyTouchLayoutGeneration();
                CancelHeldGestures();
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>Defers every control relayout until the browser canvas has a stable geometry.</summary>
        private bool HoldLastLayoutForViewportTransition()
        {
            if (!input || !input.UseTouchControls || !SlopCityWebPresentation.IsViewportTransition) return false;
            // Re-enabling the HUD starts a fresh layout pass and clears its settle
            // marker. Treat that as a new hold even if the old transition flag was
            // retained while the GameObject was inactive.
            if (!viewportTransitionActive || !geometrySettlePending)
            {
                viewportTransitionActive = true;
                unchecked { layoutGeneration++; if (layoutGeneration == 0) layoutGeneration = 1; }
                geometrySettlePending = true;
                ApplyTouchLayoutGeneration();
                // Button actions are suppressed by the generation update; clear
                // continuous owners now rather than allowing stale coordinates to
                // move, look, or sprint while the canvas is changing.
                CancelHeldGestures();
            }
            // A transition may emit several resize events. Keep actions suppressed
            // until a short interval after its final stable geometry is installed.
            geometrySettleAt = Time.unscaledTime + GeometrySettleSeconds;
            return true;
        }
#endif

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

        private void ApplyTouchLayoutGeneration()
        {
            if (moveStick) moveStick.SetLayoutGeneration(layoutGeneration);
            if (touchLook) touchLook.SetLayoutGeneration(layoutGeneration);
            if (touchRun) touchRun.SetLayoutGeneration(layoutGeneration);
            foreach (SlopCityTouchAction action in actions)
            {
                if (!action) continue;
                action.SetLayoutGeneration(layoutGeneration);
                action.SetSuppressed(geometrySettlePending);
            }
        }

        private void CancelHeldGestures()
        {
            // Cancel only touches that predate the settled geometry. A player who
            // began a new two-thumb gesture after the final relayout keeps it.
            if (moveStick) moveStick.CancelGestureBeforeLayout(layoutGeneration);
            if (touchLook) touchLook.CancelGestureBeforeLayout(layoutGeneration);
            if (touchRun) touchRun.CancelGestureBeforeLayout(layoutGeneration);
        }

        private void LayoutForViewport(int width, int height, Rect safeArea, float pixelRatio)
        {
            safe.anchorMin = new Vector2(safeArea.xMin/width,safeArea.yMin/height);
            safe.anchorMax = new Vector2(safeArea.xMax/width,safeArea.yMax/height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;
            if (rightLook) rightLook.offsetMin = rightLook.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            if (safe.rect.width < 1f || safe.rect.height < 1f) return;
            float pixelsPerUnit = GetComponentInParent<Canvas>().scaleFactor;
            // RectTransform coordinates are Canvas-local.  Do not mix backing-pixel
            // Screen dimensions with them: on high-DPR displays that can place
            // controls outside their safe-area parents.  The move surface is only
            // the lower-left portion of the safe area; action buttons belong to
            // the full safe area, so never size or align them from `left`.
            float safeWidth = safe.rect.width;
            float safeHeight = safe.rect.height;
            float moveWidth = left.rect.width;
            float moveHeight = left.rect.height;
            const float gapRatio = .18f, marginRatio = .24f, smallRatio = .80f;
            float preferredButton = Mathf.Clamp(Mathf.Min(safeWidth, safeHeight) * .16f, 44f, 72f);
            float actionWidth = safeWidth * .5f;
            float threeColumnLimit = ActionButtonLimit(actionWidth, safeHeight, 3, 2, gapRatio, marginRatio, smallRatio);
            float twoColumnLimit = ActionButtonLimit(actionWidth, safeHeight, 2, 3, gapRatio, marginRatio, smallRatio);
            // Keep the lower action cluster wholly in the right safe-area half.
            // On unusually narrow screens, two columns/three rows preserve a
            // usable target instead of shifting the third column over the stick.
            bool compactActions = twoColumnLimit > threeColumnLimit;
            float actionLimit = compactActions ? twoColumnLimit : threeColumnLimit;
            float button = Mathf.Max(1f, Mathf.Min(preferredButton, actionLimit));
            float gap = button*.18f;
            float margin = button*.24f;
            float ringSize = Mathf.Min(button * 1.8f,
                Mathf.Max(1f, Mathf.Min(moveWidth, moveHeight) - button * .4f));
            ring.sizeDelta = Vector2.one*ringSize;
            ring.GetChild(0).GetComponent<RectTransform>().sizeDelta = Vector2.one*button*.72f;
            float ringRadius = ringSize * .5f;
            float ringX = Mathf.Clamp(-moveWidth*.5f+button*1.25f,
                -moveWidth*.5f+ringRadius, moveWidth*.5f-ringRadius);
            float ringY = Mathf.Clamp(-moveHeight*.5f+button*1.4f,
                -moveHeight*.5f+ringRadius, moveHeight*.5f-ringRadius);
            ring.anchoredPosition = new Vector2(ringX, ringY);
            PositionAction(buttons[0], 0, 0, margin, gap, button); // JUMP
            PositionAction(buttons[1], 0, 1, margin, gap, button); // USE
            if (compactActions)
            {
                PositionAction(buttons[2], 0, 2, margin, gap, button); // RUN
                PositionAction(buttons[6], 1, 2, margin, gap, button); // CLEAN
                PositionAction(buttons[7], 1, 1, margin, gap, button); // TAKE
                PositionAction(buttons[8], 1, 0, margin, gap, button); // BAG
            }
            else
            {
                PositionAction(buttons[2], 1, 0, margin, gap, button); // RUN
                PositionAction(buttons[6], 1, 1, margin, gap, button); // CLEAN
                PositionAction(buttons[7], 2, 1, margin, gap, button); // TAKE
                PositionAction(buttons[8], 2, 0, margin, gap, button); // BAG
            }
            float small = button*.80f;
            for (int i=3;i<6;i++)
                Position(buttons[i],new Vector2(1,1),new Vector2(-margin-small*.5f-(i-3)*(small+gap*.65f),-margin-small*.5f),small);
            // Action buttons are ordered above this layer and win their own
            // raycasts.  Clipping this target around them creates a negative-width
            // look surface on narrow portrait devices and leaves a sliver in
            // landscape, so keep the whole safe-area right half available.
            if (rightLook) rightLook.offsetMin = rightLook.offsetMax = Vector2.zero;
            if (touchActions) touchActions.SetAsLastSibling();
            lastSafe = safeArea;
            lastLayoutWidth = width;
            lastLayoutHeight = height;
            lastPixelRatio = pixelRatio;
            lastLayoutScaleFactor = pixelsPerUnit;
            layoutApplied = true;
        }

        private static float ActionButtonLimit(float actionWidth, float safeHeight, int columns, int rows,
            float gapRatio, float marginRatio, float smallRatio)
        {
            // Lower actions reserve one inter-group gap below the top utility
            // row, whose controls are `smallRatio` of an action button.
            float widthLimit = actionWidth / (columns + (columns - 1) * gapRatio + marginRatio);
            float heightLimit = safeHeight / (rows + (rows - 1) * gapRatio + gapRatio + 2f * marginRatio + smallRatio);
            return Mathf.Max(0f, Mathf.Min(widthLimit, heightLimit));
        }

        private static void PositionAction(RectTransform rect, int column, int row,
            float margin, float gap, float size)
        {
            Position(rect, new Vector2(1, 0), new Vector2(
                -margin - size * (.5f + column) - gap * column,
                margin + size * (.5f + row) + gap * row), size);
        }

        private RectTransform AddButton(string name,string label,UnityAction action,Font font)
        {
            var rect = Rect(name,touchActions ? touchActions : safe);
            var image = rect.gameObject.AddComponent<Image>(); image.sprite=disc; image.color=new Color(.04f,.085f,.105f,.60f);
            var button=rect.gameObject.AddComponent<Button>();
            button.navigation=new Navigation { mode=Navigation.Mode.None };
            if(action!=null)
            {
                var touchAction=rect.gameObject.AddComponent<SlopCityTouchAction>();
                touchAction.Configure(input,action);
                actions.Add(touchAction);
            }
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

    /// <summary>Rejects a button tap that began before a mobile layout changed.</summary>
    public sealed class SlopCityTouchAction : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
    {
        private SlopCityPlayerInput input;
        private UnityAction action;
        private Button button;
        private int pointer = int.MinValue;
        private uint layoutGeneration, gestureGeneration;
        private bool suppressed;

        public void Configure(SlopCityPlayerInput source, UnityAction value)
        {
            input = source;
            action = value;
            button = GetComponent<Button>();
        }

        public void SetLayoutGeneration(uint value)
        {
            layoutGeneration = value;
            if (pointer != int.MinValue && gestureGeneration != layoutGeneration) Disarm();
        }

        public void SetSuppressed(bool value)
        {
            suppressed = value;
            if (value) Disarm();
        }

        public void OnPointerDown(PointerEventData data)
        {
            Disarm();
            if (data == null || suppressed || !input || !input.GameplayEnabled || !button || !button.interactable) return;
            pointer = data.pointerId;
            gestureGeneration = layoutGeneration;
        }

        public void OnPointerClick(PointerEventData data)
        {
            bool accepted = data != null && pointer == data.pointerId && !suppressed
                && gestureGeneration == layoutGeneration && input && input.GameplayEnabled
                && button && button.interactable;
            Disarm();
            if (accepted) action?.Invoke();
        }

        public void CancelGesture() { Disarm(); }
        private void OnDisable() { Disarm(); }
        private void OnApplicationFocus(bool focused) { if (!focused) Disarm(); }
        private void OnApplicationPause(bool paused) { if (paused) Disarm(); }
        private void Disarm() { pointer = int.MinValue; }
    }

    public sealed class SlopCityTouchHold : MonoBehaviour,IPointerDownHandler,IPointerUpHandler
    {
        private SlopCityPlayerInput input;
        private int pointer=int.MinValue;
        private uint resetVersion;
        private uint layoutGeneration, gestureGeneration;
        public void Configure(SlopCityPlayerInput source){input=source;}
        public void SetLayoutGeneration(uint value)
        {
            layoutGeneration=value;
            if(pointer!=int.MinValue&&gestureGeneration!=layoutGeneration&&input)input.RequestSprint(false);
        }
        public void CancelGestureBeforeLayout(uint currentGeneration)
        { if(pointer!=int.MinValue&&gestureGeneration!=currentGeneration)Release(); }
        public void OnPointerDown(PointerEventData data)
        {
            if(data.pointerId<0)return;
            ReconcileGesture();
            if(pointer!=int.MinValue&&gestureGeneration!=layoutGeneration)Release();
            if((pointer!=int.MinValue&&pointer!=data.pointerId)||!input||!input.GameplayEnabled)return;
            pointer=data.pointerId;gestureGeneration=layoutGeneration;resetVersion=input.TouchResetVersion;input.RequestSprint(true);
        }
        private void LateUpdate(){ReconcileGesture();}
        private void ReconcileGesture()
        {
            if(pointer!=int.MinValue&&(!input||!input.GameplayEnabled
                ||resetVersion!=input.TouchResetVersion||!SlopCityPlayerInput.TouchIsActive(pointer)))Release();
        }
        public void OnPointerUp(PointerEventData data){if(data.pointerId>=0&&pointer==data.pointerId)Release();}
        public void CancelGesture(){Release();}
        private void OnDisable(){Release();}
        private void OnApplicationFocus(bool focused){if(!focused)Release();}
        private void OnApplicationPause(bool paused){if(paused)Release();}
        private void Release(){pointer=int.MinValue;if(input)input.RequestSprint(false);}
    }
}
