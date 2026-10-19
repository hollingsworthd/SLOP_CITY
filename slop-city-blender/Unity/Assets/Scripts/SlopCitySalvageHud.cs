using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace SlopCity
{
    /// <summary>Bounded inventory UI; a physical counter is required for every sale.</summary>
    public sealed class SlopCitySalvageHud : MonoBehaviour
    {
        private SlopCityFirstPerson player;
        private SlopCitySandbox sandbox;
        private SlopCityPlayerInput input;
        private Camera view;
        private GameObject panel, desktop, canvasObject;
        private bool initialized;
        private Text heading, summary, hint, pageLabel;
        private readonly Text[] labels = new Text[8];
        private readonly Button[] rows = new Button[8];
        private SlopCityPawnCounter pawn;
        private bool showStock, compact;
        private float layoutWidthCss;
        private Font font;
        private int page, visibleRows = 8;
        private RectTransform safe;
        private Button close, previous, next;
        private Rect lastSafe;
        private Vector2 lastSize;
        private float nextPrompt;
        public static void Ensure(SlopCityFirstPerson player, SlopCitySandbox sandbox, SlopCityPlayerInput input, Camera camera)
        {
            var hud = player.GetComponent<SlopCitySalvageHud>();
            if (hud) return;
            hud = player.gameObject.AddComponent<SlopCitySalvageHud>();
            hud.player = player; hud.sandbox = sandbox; hud.input = input; hud.view = camera;
            try { hud.Build(); hud.initialized = true; }
            catch
            {
                // The component exists before Build. Keep a partial panel out of
                // Update/event callbacks, and retire even an unparented canvas.
                hud.enabled = false;
                try { hud.ReleaseUi(); }
                finally { Destroy(hud); }
                throw;
            }
        }
        private void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = canvasObject = new GameObject("SalvageInventoryHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<Canvas>().sortingOrder = 200;
            var scale = canvas.GetComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(960, 720); scale.matchWidthOrHeight = .5f;
            desktop = Rect("InventoryKeys", canvas.transform, new Vector2(.20f, .02f), new Vector2(.80f, .09f)).gameObject;
            hint = Label(desktop.transform, "", Vector2.zero, Vector2.one, 17);
            hint.alignment = TextAnchor.MiddleCenter;
            safe = Rect("SafeArea", canvas.transform, Vector2.zero, Vector2.one);
            panel = Rect("Inventory", safe, new Vector2(.02f, .02f), new Vector2(.98f, .98f)).gameObject;
            panel.AddComponent<Image>().color = new Color(.025f, .05f, .045f, .98f);
            heading = Label(panel.transform, "BAG", new Vector2(.04f,.88f), new Vector2(.75f,.98f), 30);
            summary = Label(panel.transform, "", new Vector2(.04f,.79f), new Vector2(.96f,.89f), 17);
            close = Button(panel.transform, "CLOSE", new Vector2(.76f,.89f), new Vector2(.96f,.97f), () => player.SetInventoryOpen(false));
            for (int index = 0; index < rows.Length; index++)
            {
                int row = index; float top = .78f - index * .077f;
                rows[index] = Button(panel.transform, "", new Vector2(.04f,top-.069f), new Vector2(.96f,top), () => Select(row));
                labels[index] = rows[index].GetComponentInChildren<Text>(); labels[index].alignment = TextAnchor.MiddleLeft;
            }
            previous = Button(panel.transform, "PREVIOUS", new Vector2(.04f,.035f), new Vector2(.29f,.12f), () => { page = Mathf.Max(0,page-1); Refresh(); });
            var mode = Button(panel.transform, "BUY BACK", new Vector2(.31f,.035f), new Vector2(.68f,.12f), () => { if (sandbox.CanSellAt(pawn)) { showStock = !showStock; page = 0; Refresh(); } });
            pageLabel = mode.GetComponentInChildren<Text>();
            next = Button(panel.transform, "NEXT", new Vector2(.71f,.035f), new Vector2(.96f,.12f), () => { page++; Refresh(); });
            sandbox.InventoryRequested += Open; sandbox.InventoryChanged += Refresh;
            Layout();
            panel.SetActive(false);
        }
        private void Open(SlopCityPawnCounter counter)
        { if (!initialized) return; pawn = counter; showStock = false; page = 0; player.SetInventoryOpen(true); panel.SetActive(true); Refresh(); }
        private void Select(int row)
        {
            int index = page * visibleRows + row;
            var list = showStock ? sandbox.PawnStock : sandbox.Inventory;
            if (index < list.Count && sandbox.CanSellAt(pawn))
            { if (showStock) sandbox.BuyBackInventory(list[index].id,pawn); else sandbox.SellInventory(list[index].id,pawn); }
        }
        private void Refresh()
        {
            if (!initialized || !panel) return;
            bool shop = sandbox.CanSellAt(pawn);
            if (!shop) showStock = false;
            var list = showStock ? sandbox.PawnStock : sandbox.Inventory;
            heading.text = shop ? "SIGNAL YARD / PAWN SHOP" : "YOUR BAG";
            string balance = sandbox.Credits + " CREDITS · " + sandbox.InventorySlotsUsed + "/" + SlopCitySandbox.InventoryCapacity + " SLOTS";
            summary.text = compact ? balance + (!shop && layoutWidthCss >= 760f ? " · Sell at Signal Yard" : "")
                : balance + "\n" + (shop ? (showStock ? "Buy back the original item with its condition preserved." : "Select an item to sell one. Polished pieces fetch more.") : "Bring recovered objects to the counter inside Signal Yard to sell them.");
            int pages = Mathf.Max(1,(list.Count+visibleRows-1)/visibleRows); page = Mathf.Clamp(page,0,pages-1);
            pageLabel.text = (shop ? (showStock ? "MY BAG" : "BUY BACK") + "\n" : "") + (page+1)+" / "+pages;
            for (int index = 0; index < rows.Length; index++)
            {
                int at = page * visibleRows + index; bool exists = index < visibleRows && at < list.Count;
                rows[index].gameObject.SetActive(exists); if (!exists) continue;
                var entry = list[at];
                labels[index].text = "  " + entry.label + " ×" + entry.quantity + "   ·   " + (entry.cleanStrokes == 4 ? "POLISHED" : entry.cleanStrokes > 0 ? "CLEAN " + entry.cleanStrokes + "/4" : "AS FOUND")
                    + "   ·   " + (showStock ? sandbox.BuyBackPrice(entry) : entry.UnitPrice) + " CR" + (shop ? (showStock ? "   BUY ONE ›" : "   SELL ONE ›") : "");
                rows[index].interactable = shop;
            }
        }
        private void Update()
        {
            if (!initialized || !player || !input || !panel) return;
            if (lastSafe != Screen.safeArea || lastSize != new Vector2(Screen.width,Screen.height)) { Layout(); Refresh(); }
            if (panel.activeSelf != player.InventoryOpen) panel.SetActive(player.InventoryOpen);
            desktop.SetActive(input.GameplayEnabled && !input.UseTouchControls);
            if (Time.unscaledTime >= nextPrompt)
            {
                nextPrompt = Time.unscaledTime + .15f;
                if (desktop.activeSelf) hint.text = sandbox.SalvagePrompt(view);
                if (panel.activeSelf && !ReferenceEquals(pawn, null) && !sandbox.CanSellAt(pawn)) { pawn = null; Refresh(); }
            }
        }
        private void Layout() => LayoutForViewport(Screen.width, Screen.height, Screen.safeArea, SlopCityWebPresentation.DisplayPixelRatio);

        private void LayoutForViewport(int width, int screenHeight, Rect safeArea, float pixelRatio)
        {
            lastSafe = safeArea; lastSize = new Vector2(width,screenHeight);
            if (width <= 0 || screenHeight <= 0) return;
            compact = width > screenHeight && safeArea.height / Mathf.Max(1f,pixelRatio) < 260f;
            layoutWidthCss = width / Mathf.Max(1f,pixelRatio);
            safe.anchorMin = new Vector2(lastSafe.xMin/width,lastSafe.yMin/screenHeight);
            safe.anchorMax = new Vector2(lastSafe.xMax/width,lastSafe.yMax/screenHeight);
            Canvas.ForceUpdateCanvases();
            float scale = safe.GetComponentInParent<Canvas>().scaleFactor;
            float unit = 54f * pixelRatio / Mathf.Max(.1f,scale);
            float height = ((RectTransform)panel.transform).rect.height;
            Top(heading.rectTransform,.04f,.73f,unit*(compact ? .02f : .1f),unit*(compact ? .45f : 1f));
            Top(close.GetComponent<RectTransform>(),.75f,.96f,unit*(compact ? .05f : .1f),unit);
            Top(summary.rectTransform,.04f,compact ? .73f : .96f,unit*(compact ? .5f : 1.15f),unit*(compact ? .5f : 1.1f));
            float listTop = unit*(compact ? 1.1f : 2.35f), footer = unit*(compact ? 1.1f : 1.25f);
            visibleRows = Mathf.Clamp(Mathf.FloorToInt((height-listTop-footer)/(unit*1.08f)),1,8);
            for (int index=0;index<rows.Length;index++) Top(rows[index].GetComponent<RectTransform>(),.04f,.96f,listTop+index*unit*1.08f,unit);
            Bottom(previous.GetComponent<RectTransform>(),.04f,.29f,unit*(compact ? .05f : .12f),unit);
            Bottom(next.GetComponent<RectTransform>(),.71f,.96f,unit*(compact ? .05f : .12f),unit);
            Bottom((RectTransform)pageLabel.transform.parent,.31f,.68f,unit*(compact ? .05f : .12f),unit);
        }
        private static void Top(RectTransform rect,float left,float right,float offset,float height)
        { rect.anchorMin=new Vector2(left,1);rect.anchorMax=new Vector2(right,1);rect.offsetMin=new Vector2(0,-offset-height);rect.offsetMax=new Vector2(0,-offset); }
        private static void Bottom(RectTransform rect,float left,float right,float offset,float height)
        { rect.anchorMin=new Vector2(left,0);rect.anchorMax=new Vector2(right,0);rect.offsetMin=new Vector2(0,offset);rect.offsetMax=new Vector2(0,offset+height); }
        private RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var item = new GameObject(name,typeof(RectTransform));
            RectTransform rect;
            try { rect = item.GetComponent<RectTransform>(); rect.SetParent(parent,false); }
            catch { Destroy(item); throw; }
            rect.anchorMin=min; rect.anchorMax=max; rect.offsetMin=rect.offsetMax=Vector2.zero; return rect;
        }
        private Text Label(Transform parent,string text,Vector2 min,Vector2 max,int size)
        {
            var label=Rect("Label",parent,min,max).gameObject.AddComponent<Text>(); label.font=font;label.text=text;
            label.color=new Color(.86f,.96f,.88f);label.fontSize=size;label.resizeTextForBestFit=true;label.resizeTextMinSize=10;label.resizeTextMaxSize=size;
            label.alignment=TextAnchor.MiddleLeft;label.raycastTarget=false;return label;
        }
        private Button Button(Transform parent,string label,Vector2 min,Vector2 max,UnityAction click)
        {
            var rect=Rect(label==""?"InventoryItem":label,parent,min,max); rect.gameObject.AddComponent<Image>().color=new Color(.10f,.19f,.15f);
            var button=rect.gameObject.AddComponent<Button>();button.navigation=new Navigation {mode=Navigation.Mode.None}; button.onClick.AddListener(click);
            var text=Label(rect,label,new Vector2(.02f,0),new Vector2(.98f,1),20);text.alignment=TextAnchor.MiddleCenter;return button;
        }
        private void ReleaseUi()
        {
            initialized = false;
            if (sandbox) { sandbox.InventoryRequested -= Open; sandbox.InventoryChanged -= Refresh; }
            if (canvasObject)
            {
                try { canvasObject.SetActive(false); }
                finally { Destroy(canvasObject); }
                canvasObject = null;
            }
        }
        private void OnDestroy() { ReleaseUi(); }
    }
}
