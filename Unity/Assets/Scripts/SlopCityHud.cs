using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace SlopCity
{
    /// <summary>Clean, standard uGUI overlays: controls, local objectives, menus, and optional touch.</summary>
    public sealed class SlopCityHud : MonoBehaviour
    {
        [SerializeField] private Text stateLabel;
        [SerializeField] private Text promptLabel;
        [SerializeField] private Text controlsLabel;
        [SerializeField] private Text crosshair;
        private Text objectives;
        private Text menuHeading;
        private Text playLabel;
        private Text sourceHeading;
        private Text sourceDescription;
        private GameObject menu;
        private GameObject sourceCard;
        private GameObject touchControls;
        private GameObject restartButton;
        private Button playButton;
        private Button quitButton;
        private Button returnToLoungeButton;
        private SlopCityPlayerInput input;
        private Font font;
        private bool initialized;
        private static readonly Color Ink = new Color(0.035f, 0.065f, 0.06f, 0.96f);
        private static readonly Color Cream = new Color(0.91f, 0.91f, 0.82f, 1f);
        private static readonly Color Lime = new Color(0.80f, 0.91f, 0.49f, 1f);
        private static readonly Color Muted = new Color(0.64f, 0.72f, 0.66f, 1f);

        public void Configure(Text state, Text prompt, Text controls, Text reticle)
        {
            stateLabel = state; promptLabel = prompt; controlsLabel = controls; crosshair = reticle;
        }

        public void Initialize(SlopCityFirstPerson player, SlopCityPlayerInput source)
        {
            if (initialized) return;
            initialized = true; input = source;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!GetComponent<GraphicRaycaster>()) gameObject.AddComponent<GraphicRaycaster>();
            if (!EventSystem.current)
            {
                var events = new GameObject("SlopCity_EventSystem", typeof(EventSystem));
                events.AddComponent<StandaloneInputModule>();
            }
            objectives = Label(transform, "LocalObjectives", "", 14, TextAnchor.UpperLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(38, -106), new Vector2(420, 132), Cream);
            objectives.lineSpacing = 1.3f;
            objectives.supportRichText = true;
            CreateMenu(player);
            CreateSourceCard(player);
            if (source.UseTouchControls) CreateTouchControls(source);
        }

        public void Render(string state, string prompt, string controls, bool showCrosshair)
        {
            if (stateLabel) stateLabel.text = state;
            if (promptLabel) promptLabel.text = prompt;
            if (controlsLabel) controlsLabel.text = controls;
            if (crosshair) crosshair.enabled = showCrosshair;
        }

        public void SetObjectives(bool stood, bool inspected, bool returned)
        {
            if (!objectives) return;
            objectives.text = (returned ? "<color=#CDE87D>HOME BASE / COMPLETE</color>" : "<color=#CDE87D>YOUR FIRST LOOP</color>")
                + "\n" + Objective(stood, "Stand from the couch")
                + "\n" + Objective(inspected, "Inspect the broadcast")
                + "\n" + Objective(returned, "Return to the couch");
        }

        private static string Objective(bool complete, string label)
        {
            return complete ? "<color=#A4BA98>[x] " + label + "</color>" : "[ ] " + label;
        }

        public void SetMenu(bool visible, bool hasStarted)
        {
            if (!initialized) return;
            menu.SetActive(visible);
            sourceCard.SetActive(false);
            if (touchControls) touchControls.SetActive(!visible);
            menuHeading.text = hasStarted ? "TAKE A BREATHER." : "WELCOME HOME, DANIEL.";
            playLabel.text = hasStarted ? "RESUME" : "PLAY";
            restartButton.SetActive(hasStarted);
            RectTransform quitRect = quitButton.GetComponent<RectTransform>();
            quitRect.anchoredPosition = new Vector2(hasStarted ? 152 : 0, -82);
            quitRect.sizeDelta = new Vector2(hasStarted ? 286 : 590, 49);
            quitButton.GetComponentInChildren<Text>().rectTransform.sizeDelta = quitRect.sizeDelta;
            if (visible && EventSystem.current && !input.UseTouchControls)
                EventSystem.current.SetSelectedGameObject(playButton.gameObject);
        }

        public void ShowSource(string title, string description)
        {
            sourceHeading.text = title;
            sourceDescription.text = description;
            ShowSourceTitleOnly();
        }

        public void ShowSourceTitleOnly()
        {
            if (!initialized) return;
            menu.SetActive(false); sourceCard.SetActive(true);
            if (touchControls) touchControls.SetActive(false);
            if (EventSystem.current && returnToLoungeButton)
                EventSystem.current.SetSelectedGameObject(returnToLoungeButton.gameObject);
        }

        public void HideSource() { if (sourceCard) sourceCard.SetActive(false); }

        private void CreateMenu(SlopCityFirstPerson player)
        {
            menu = FullScreenPanel("PauseAndStart", new Color(0.015f, 0.030f, 0.027f, 0.86f));
            RectTransform panel = Panel(menu.transform, "MenuCard", new Vector2(680, 586), Ink);
            Label(panel, "Brand", "SLOP CITY", 43, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(42,-32), new Vector2(590,63), Cream);
            Label(panel, "Byline", "1984 DRH  /  PRESENTED BY HOOD RICH RECORDS", 12, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(44,-96), new Vector2(590,28), Lime);
            menuHeading = Label(panel, "Heading", "WELCOME HOME, DANIEL.", 23, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(43,-150), new Vector2(590,38), Cream);
            Label(panel, "Description", "Your couch. Your broadcast. Your corner of the city.\nA small first-person lounge to explore at your own pace.", 16, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(44,-194), new Vector2(590,65), Muted);
            playButton = Button(panel, "PlayResume", "PLAY", new Vector2(0,-13), new Vector2(590,54), player.PlayOrResume, true);
            playLabel = playButton.GetComponentInChildren<Text>();
            restartButton = Button(panel, "RestartLounge", "RESTART LOUNGE", new Vector2(-151,-82), new Vector2(286,49), player.RestartLounge).gameObject;
            quitButton = Button(panel, "Quit", "QUIT GAME", new Vector2(152,-82), new Vector2(286,49), player.QuitGame);
            string controls = input.UseTouchControls
                ? "LEFT PAD TO MOVE  /  DRAG RIGHT TO LOOK\nINTERACT TO STAND, SIT, OR INSPECT  /  COUCH TO RESET"
                : "WASD / ARROWS TO MOVE  /  MOUSE TO LOOK\nE TO INTERACT  /  R TO RETURN TO COUCH  /  ESC TO PAUSE";
            Label(panel, "Controls", controls, 12, TextAnchor.MiddleCenter,
                new Vector2(0.5f,0), new Vector2(0.5f,0), new Vector2(0,53), new Vector2(594,55), Muted);
            Label(panel, "Scope", "LOCAL SINGLE-PLAYER LOUNGE", 10, TextAnchor.LowerCenter,
                new Vector2(0.5f,0), new Vector2(0.5f,0), new Vector2(0,25), new Vector2(590,20), new Color(.43f,.53f,.46f,1));
        }

        private void CreateSourceCard(SlopCityFirstPerson player)
        {
            sourceCard = FullScreenPanel("SourceCard", new Color(0.015f, 0.030f, 0.027f, 0.78f));
            RectTransform panel = Panel(sourceCard.transform, "SourceContent", new Vector2(670,475), Ink);
            Label(panel, "Tag", "SIGNAL RECEIVED", 12, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(38,-31), new Vector2(590,23), Lime);
            sourceHeading = Label(panel, "Title", "", 30, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(38,-77), new Vector2(594,60), Cream);
            sourceDescription = Label(panel, "Description", "", 17, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(40,-151), new Vector2(582,157), Muted);
            Button(panel, "OpenOriginal", "OPEN ORIGINAL TIKTOK", new Vector2(0,-107), new Vector2(590,48), player.OpenSelectedSource, true);
            returnToLoungeButton = Button(panel, "ReturnToLounge", "BACK TO LOUNGE", new Vector2(0,-169), new Vector2(590,46), player.PlayOrResume);
            sourceCard.SetActive(false);
        }

        private void CreateTouchControls(SlopCityPlayerInput source)
        {
            touchControls = new GameObject("MobileControls", typeof(RectTransform));
            touchControls.transform.SetParent(transform, false);
            RectTransform touchRect = touchControls.GetComponent<RectTransform>();
            Stretch(touchRect);
            var look = new GameObject("DragToLook", typeof(RectTransform), typeof(Image), typeof(SlopCityTouchLook));
            look.transform.SetParent(touchControls.transform, false);
            RectTransform lookRect = look.GetComponent<RectTransform>();
            lookRect.anchorMin = new Vector2(.44f,.23f); lookRect.anchorMax = new Vector2(1f,.86f);
            lookRect.offsetMin = lookRect.offsetMax = Vector2.zero;
            look.GetComponent<Image>().color = Color.clear;
            look.GetComponent<SlopCityTouchLook>().Configure(source);
            Label(touchControls.transform, "LookHint", "DRAG TO LOOK", 11, TextAnchor.MiddleCenter,
                new Vector2(.76f,.24f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(240,26), Muted);
            var stick = new GameObject("MovePad", typeof(RectTransform), typeof(Image), typeof(SlopCityTouchStick));
            stick.transform.SetParent(touchControls.transform, false);
            RectTransform stickRect = stick.GetComponent<RectTransform>();
            stickRect.anchorMin = stickRect.anchorMax = new Vector2(0,0);
            stickRect.pivot = new Vector2(.5f,.5f); stickRect.anchoredPosition = new Vector2(131,183); stickRect.sizeDelta = new Vector2(158,158);
            stick.GetComponent<Image>().color = new Color(.09f,.19f,.15f,.55f);
            RectTransform thumb = Panel(stick.transform, "Thumb", new Vector2(48,48), new Color(.77f,.88f,.49f,.75f));
            thumb.GetComponent<Image>().raycastTarget = false;
            stick.GetComponent<SlopCityTouchStick>().Configure(source, thumb);
            Label(touchControls.transform, "MoveHint", "MOVE", 11, TextAnchor.MiddleCenter,
                Vector2.zero, new Vector2(.5f,.5f), new Vector2(131,87), new Vector2(180,25), Muted);
            Button touchInteract = Button(touchControls.transform, "TouchInteract", "INTERACT", Vector2.zero, new Vector2(158,67), source.RequestInteract, true);
            AnchorButton(touchInteract, new Vector2(1,0), new Vector2(-117,169));
            Button touchReset = Button(touchControls.transform, "TouchCouch", "COUCH", Vector2.zero, new Vector2(126,52), source.RequestRespawn);
            AnchorButton(touchReset, new Vector2(1,0), new Vector2(-289,166));
            Button touchPause = Button(touchControls.transform, "TouchPause", "PAUSE", Vector2.zero, new Vector2(105,42), source.RequestPause);
            AnchorButton(touchPause, new Vector2(1,1), new Vector2(-92,-104));
        }

        private static void AnchorButton(Button button, Vector2 anchor, Vector2 position)
        {
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = position;
        }

        private GameObject FullScreenPanel(string name, Color color)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(transform, false);
            Stretch(obj.GetComponent<RectTransform>());
            obj.GetComponent<Image>().color = color;
            return obj;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private RectTransform Panel(Transform parent, string name, Vector2 size, Color color)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f); rect.sizeDelta = size;
            obj.GetComponent<Image>().color = color;
            return rect;
        }

        private Button Button(Transform parent, string name, string text, Vector2 position, Vector2 size, UnityAction action, bool primary = false)
        {
            RectTransform rect = Panel(parent, name, size, primary ? Lime : new Color(.12f,.21f,.17f,1));
            rect.anchoredPosition = position;
            Button button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(action);
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f,1f,.85f,1);
            colors.pressedColor = new Color(.65f,.77f,.48f,1);
            colors.selectedColor = new Color(.93f,1f,.81f,1);
            button.colors = colors;
            Label(rect, "Label", text, 15, TextAnchor.MiddleCenter, new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, size, primary ? Ink : Cream);
            return button;
        }

        private Text Label(Transform parent, string name, string content, int size, TextAnchor alignment,
            Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 dimensions, Color color)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot;
            rect.anchoredPosition = position; rect.sizeDelta = dimensions;
            Text text = obj.GetComponent<Text>();
            text.font = font; text.fontSize = size; text.text = content; text.color = color;
            text.alignment = alignment; text.fontStyle = size >= 20 ? FontStyle.Bold : FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
