using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace SlopCity
{
    /// <summary>Clean, standard uGUI overlays: controls, local objectives, menus, and optional touch.</summary>
    public sealed class SlopCityHud : MonoBehaviour
    {
        // Enlarge desktop text and its surrounding layout together. The overlay
        // still renders at display resolution, independently of city detail.
        public const float DesktopUiScale = 1.35f;
        private const float DesktopFontScale = 1.4f;
        [SerializeField] private Text stateLabel;
        [SerializeField] private Text promptLabel;
        [SerializeField] private Text controlsLabel;
        [SerializeField] private Text crosshair;
        private Text objectives;
        private Text menuHeading;
        private Text playLabel;
        private Text sourceHeading;
        private Text sourceDescription;
        private Text mediaPlayLabel;
        private Text mediaVolumeLabel;
        private Text nowPlaying;
        private Image[] districtCells;
        private Text cityProgress;
        private Text districtName;
        private Text homeDistance;
        private Text reputation;
        private Text transmission;
        private SlopCityRabbitTrail rabbitTrail;
        private Text cityMediaStatus;
        private RectTransform mapPointer;
        private GameObject menu;
        private GameObject gameplayLayer;
        private RectTransform menuCard;
        private GameObject sourceCard;
        private GameObject touchControls;
        private GameObject restartButton;
        private Button playButton;
        private Button quitButton;
        private Button licenseButton;
        private SlopCitySourceBrowser sourceBrowser;
        private Button returnToLoungeButton;
        private SlopCityPlayerInput input;
        private SlopCityFirstPerson owner;
        private Font font;
        private bool initialized;
        private bool initializing;
        private bool immersiveView;
        private bool menuStarted;
        private Vector2 lastViewport;
        private Vector2 lastCanvasSize;
        private Rect lastSafeArea;
        private Vector2 lastGuidanceSize;
        private Rect lastGuidanceSafeArea;
        private float lastGuidancePixelRatio;
        private string lastMobileObjective, lastMobileReputation, lastMobileTransmission, lastMobilePrompt;
        private RectTransform sourceViewport;
        private RectTransform desktopMenuViewport;
        private RectTransform desktopMediaViewport;
        private static readonly Color Ink = new Color(.026f,.041f,.055f,.94f);
        private static readonly Color Cream = new Color(.84f,.89f,.9f,1f);
        private static readonly Color Lime = new Color(.56f,.75f,.80f,1f);
        private static readonly Color Muted = new Color(.48f,.59f,.63f,1f);

        public void Configure(Text state, Text prompt, Text controls, Text reticle)
        {
            stateLabel = state; promptLabel = prompt; controlsLabel = controls; crosshair = reticle;
        }

        private void Update()
        {
            if (!initialized) return;
            ApplyResponsiveLayout();
            bool modalVisible = ApplyModalVisibility();
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!modalVisible && Input.GetKeyDown(KeyCode.H)) immersiveView = !immersiveView;
#elif ENABLE_INPUT_SYSTEM
            if (!modalVisible && UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.hKey.wasPressedThisFrame) immersiveView = !immersiveView;
#endif
            ApplyModalVisibility();
        }

        private bool ApplyModalVisibility()
        {
            bool inventoryVisible = owner && owner.InventoryOpen;
            bool menuVisible = (menu && menu.activeSelf) || (sourceCard && sourceCard.activeSelf);
            bool modalVisible = inventoryVisible || menuVisible;
            if (gameplayLayer && gameplayLayer.activeSelf == modalVisible) gameplayLayer.SetActive(!modalVisible);
            if (touchControls && touchControls.activeSelf == modalVisible) touchControls.SetActive(!modalVisible);
            // Inventory owns a separate Canvas. Suppress this entire HUD while it
            // is open, including when SetMenu(false) dismisses the pause card.
            GetComponent<Canvas>().enabled = !inventoryVisible && (menuVisible || !immersiveView);
            return modalVisible;
        }

        // Retain authored labels if a runtime UI allocation fails halfway through.
        private sealed class AuthoredUiState
        {
            private readonly RectTransform rect;
            private readonly Vector2 min, max, pivot, size;
            private readonly Vector3 position, scale;
            private readonly bool active;
            private readonly Text text;
            private readonly Font font;
            private readonly Image image;
            private readonly int fontSize;
            private readonly string value;
            private readonly Color color;
            public AuthoredUiState(RectTransform target)
            {
                rect = target; min = rect.anchorMin; max = rect.anchorMax; pivot = rect.pivot;
                size = rect.sizeDelta; position = rect.anchoredPosition3D; scale = rect.localScale;
                active = rect.gameObject.activeSelf;
                text = rect.GetComponent<Text>(); image = rect.GetComponent<Image>();
                if (text) { font = text.font; fontSize = text.fontSize; value = text.text; color = text.color; }
                else if (image) color = image.color;
            }
            public void Restore()
            {
                if (!rect) return;
                rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot;
                rect.sizeDelta = size; rect.anchoredPosition3D = position; rect.localScale = scale;
                rect.gameObject.SetActive(active);
                if (text) { text.font = font; text.fontSize = fontSize; text.text = value; text.color = color; }
                else if (image) image.color = color;
            }
        }

        public void Initialize(SlopCityFirstPerson player, SlopCityPlayerInput source)
        {
            if (initialized || initializing) return;
            if (!player || !source || !GetComponent<Canvas>() || !(transform is RectTransform))
                throw new System.InvalidOperationException("The city HUD needs its player, input and Canvas before initialization.");
            var originals = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in transform) originals.Add(child);
            var authored = new System.Collections.Generic.List<AuthoredUiState>();
            foreach (RectTransform rect in GetComponentsInChildren<RectTransform>(true)) authored.Add(new AuthoredUiState(rect));
            Shadow originalShadow = promptLabel ? promptLabel.GetComponent<Shadow>() : null;
            GameObject createdEvents = null;
            initializing = true;
            try
            {
                input = source; owner = player;
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                StyleGameplayLayer();
                if (!GetComponent<GraphicRaycaster>()) gameObject.AddComponent<GraphicRaycaster>();
                if (!EventSystem.current)
                {
                    createdEvents = new GameObject("SlopCity_EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                    createdEvents.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                    createdEvents.AddComponent<StandaloneInputModule>();
#endif
                }
                objectives = Label(gameplayLayer.transform, "LocalObjectives", "", 11, TextAnchor.UpperLeft,
                    new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -99), new Vector2(320, 114), Cream);
                objectives.lineSpacing = 1.2f;
                objectives.supportRichText = true;
                reputation = Label(gameplayLayer.transform, "Reputation", "", 10, TextAnchor.UpperLeft,
                    new Vector2(0,1), new Vector2(0,1), new Vector2(32,-76), new Vector2(380,20), Lime);
                transmission = Label(gameplayLayer.transform, "SecretTransmission", "", 11, TextAnchor.UpperLeft,
                    new Vector2(0,1), new Vector2(0,1), new Vector2(32,-185), new Vector2(330,92), Lime);
                transmission.lineSpacing = 1.15f;
                rabbitTrail = new GameObject("White_Rabbit_Clues", typeof(SlopCityRabbitTrail)).GetComponent<SlopCityRabbitTrail>();
                rabbitTrail.Configure(player);
                nowPlaying = Label(gameplayLayer.transform, "NowPlaying", "", 10, TextAnchor.UpperRight,
                    new Vector2(1,1), new Vector2(1,1), new Vector2(-32,-62), new Vector2(295,54), Lime);
                CreateCityMap();
                CreateMenu(player);
                sourceBrowser = gameObject.AddComponent<SlopCitySourceBrowser>();
                sourceBrowser.Initialize(menu.transform, font, source.UseTouchControls);
                CreateSourceCard(player);
                if (!source.UseTouchControls) StyleDesktopGameplay();
                if (source.UseTouchControls) CreateTouchControls(source);
                ApplyResponsiveLayout(true);
                initialized = true;
                ApplyModalVisibility();
            }
            catch
            {
                initialized = false;
                foreach (Transform child in originals)
                    try { if (child) child.SetParent(transform, false); }
                    catch (System.Exception error) { Debug.LogWarning("HUD parent restoration failed: " + error.GetType().Name, this); }
                foreach (AuthoredUiState state in authored)
                    try { state.Restore(); }
                    catch (System.Exception error) { Debug.LogWarning("HUD restoration failed: " + error.GetType().Name, this); }
                // Cleanup follows allocation failure: do not allocate another
                // list just to find the newly created children.
                for (int i = transform.childCount - 1; i >= 0; i--)
                {
                    Transform child = transform.GetChild(i);
                    if (!originals.Contains(child)) RetireUi(child.gameObject);
                }
                if (rabbitTrail) { RetireUi(rabbitTrail.gameObject); rabbitTrail = null; }
                if (sourceBrowser)
                {
                    try { sourceBrowser.enabled = false; }
                    finally { Destroy(sourceBrowser); sourceBrowser = null; }
                }
                RetireUi(createdEvents);
                if (promptLabel && !originalShadow)
                {
                    Shadow shadow = promptLabel.GetComponent<Shadow>();
                    if (shadow) { shadow.enabled = false; Destroy(shadow); }
                }
                owner = null;
                gameplayLayer = menu = sourceCard = touchControls = null;
                menuCard = sourceViewport = desktopMenuViewport = desktopMediaViewport = null;
                lastViewport = lastCanvasSize = lastGuidanceSize = Vector2.zero;
                lastMobileObjective = lastMobileReputation = lastMobileTransmission = lastMobilePrompt = null;
                throw;
            }
            finally { initializing = false; }
        }

        public void RenderSandbox(SlopCitySandbox sandbox, Camera camera, bool paused)
        {
            if (!initialized || !sandbox) return;
            // The current physical job owns the receiver. Contacts and meals are
            // optional activities; they never replace its next action or cargo.
            reputation.gameObject.SetActive(true);
            objectives.verticalOverflow = reputation.verticalOverflow = transmission.verticalOverflow = VerticalWrapMode.Overflow;
            objectives.horizontalOverflow = HorizontalWrapMode.Wrap;
            objectives.lineSpacing = input.UseTouchControls ? 1.05f : 1.2f;
            transmission.lineSpacing = input.UseTouchControls ? 1.05f : 1.15f;
            reputation.lineSpacing = 1f;
            if (promptLabel) promptLabel.verticalOverflow = VerticalWrapMode.Overflow;
            objectives.text = input.UseTouchControls && sandbox.HasWaypoint
                ? "JOB / " + sandbox.CurrentJobTitle + "\n" + sandbox.ObjectiveAction
                    + (string.IsNullOrEmpty(sandbox.CargoLabel) ? "" : "\nCARRYING: " + sandbox.CargoLabel)
                    + "\n" + sandbox.CurrentJobOutcome
                : sandbox.ObjectiveText;
            reputation.text = sandbox.ProgressText + "\n" + sandbox.JobCoverageText;
            rabbitTrail.Present(sandbox, camera, paused);
            SlopCityMatrixHunt search = sandbox.MatrixHunt;
            if (paused && search) search.SuspendVisuals();
            transmission.enabled = !paused && search
                && (sandbox.HasWaypoint ? search.JobInstructionsVisible : search.SignalVisible);
            transmission.text = !transmission.enabled ? "" : sandbox.HasWaypoint ? rabbitTrail.TransmissionText
                : search && search.Ready ? "OPTIONAL CONTACT / " + search.TransmissionText : "Check a dispatch terminal for city work.";
            Color color = Lime; color.a = sandbox.HasWaypoint ? rabbitTrail.TransmissionOpacity : .85f; transmission.color = color;
            if (input.UseTouchControls) LayoutMobileGuidance();
            else LayoutDesktopGuidance();
        }



        public void Render(string state, string prompt, string controls, bool showCrosshair)
        {
            if (stateLabel) stateLabel.text = state;
            if (promptLabel) promptLabel.text = prompt;
            if (initialized && input.UseTouchControls) LayoutMobileGuidance();
            if (controlsLabel) controlsLabel.text = controls;
            if (crosshair) crosshair.enabled = showCrosshair;
        }

        public void SetObjectives(bool stood, bool inspected, bool returned)
        {
            if (!objectives) return;
            objectives.text = (returned ? "<color=#8FBBC7>HOME BASE / COMPLETE</color>" : "<color=#8FBBC7>WELCOME TO THE NEIGHBORHOOD</color>")
                + "\n" + Objective(stood, "Stand from the couch")
                + "\n" + Objective(inspected, "Inspect the broadcast")
                + "\n" + (input.UseTouchControls ? "Tap USE at the exit door to explore the city" : "E at the exit door to explore the city");
        }

        private void StyleGameplayLayer()
        {
            gameplayLayer = new GameObject("VisorTelemetry", typeof(RectTransform));
            ParentUi(gameplayLayer, transform);
            Stretch(gameplayLayer.GetComponent<RectTransform>());
            var original = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in transform) if (child != gameplayLayer.transform) original.Add(child);
            foreach (Transform child in original) child.SetParent(gameplayLayer.transform, false);
            foreach (Text text in gameplayLayer.GetComponentsInChildren<Text>())
            {
                text.color = text == promptLabel ? Cream : Muted;
                if (text.name == "Brand")
                {
                    text.text = "SLOP CITY™"; text.font = SlopCityBrandTypography.Font;
                    text.fontSize = 16; text.rectTransform.anchoredPosition = new Vector2(32,-27);
                }
                if (text.name == "Subtitle") { text.fontSize = 9; text.rectTransform.anchoredPosition = new Vector2(33,-51); }
            }
            if (promptLabel)
            {
                promptLabel.fontSize = 13; promptLabel.rectTransform.anchoredPosition = new Vector2(0,41);
                promptLabel.rectTransform.sizeDelta = new Vector2(840,30);
                var shadow = promptLabel.GetComponent<Shadow>();
                if (!shadow) shadow = promptLabel.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0,0,0,.8f); shadow.effectDistance = new Vector2(1,-1);
            }
            if (controlsLabel) { controlsLabel.fontSize = 9; controlsLabel.rectTransform.anchoredPosition = new Vector2(0,17); }
            if (stateLabel) { stateLabel.fontSize = 10; stateLabel.rectTransform.anchoredPosition = new Vector2(-32,-31); }
            if (crosshair) { crosshair.text = "·"; crosshair.fontSize = 20; crosshair.color = new Color(.7f,.85f,.9f,.65f); }
            Transform backdrop = gameplayLayer.transform.Find("Controls_Backdrop");
            if (backdrop) { backdrop.GetComponent<Image>().color = new Color(.018f,.028f,.035f,.2f); backdrop.GetComponent<RectTransform>().offsetMax = new Vector2(0,64); }
            RectTransform rule = Panel(gameplayLayer.transform,"TelemetryRule",new Vector2(58,2),new Color(.42f,.68f,.76f,.6f));
            rule.anchorMin=rule.anchorMax=rule.pivot=new Vector2(0,1);rule.anchoredPosition=new Vector2(32,-70);rule.GetComponent<Image>().raycastTarget=false;
        }

        private int FontSize(int size)
        {
            return input && !input.UseTouchControls ? Mathf.Max(13,Mathf.RoundToInt(size * DesktopFontScale)) : size;
        }

        private void StyleDesktopGameplay()
        {
            // Scene-authored labels share the same policy as runtime menu labels.
            foreach (string name in new[] { "Brand", "Subtitle", "VisorIdentity" })
            {
                Transform label = gameplayLayer.transform.Find(name);
                if (label) label.GetComponent<Text>().fontSize = FontSize(label.GetComponent<Text>().fontSize);
            }
            if (stateLabel) stateLabel.fontSize = FontSize(stateLabel.fontSize);
            if (controlsLabel) controlsLabel.fontSize = FontSize(controlsLabel.fontSize);
            if (promptLabel) promptLabel.fontSize = FontSize(promptLabel.fontSize);
            if (promptLabel)
            {
                promptLabel.rectTransform.anchoredPosition = new Vector2(0,60);
                promptLabel.rectTransform.sizeDelta = new Vector2(1000,40);
            }
            if (controlsLabel) controlsLabel.rectTransform.sizeDelta = new Vector2(1460,32);
            Transform backdrop = gameplayLayer.transform.Find("Controls_Backdrop");
            if (backdrop) backdrop.GetComponent<RectTransform>().offsetMax = new Vector2(0,88);
            objectives.rectTransform.sizeDelta = new Vector2(440,114);
            transmission.rectTransform.sizeDelta = new Vector2(440,110);
        }

        private void LayoutDesktopGuidance()
        {
            reputation.rectTransform.anchoredPosition = new Vector2(32,-76);
            reputation.rectTransform.sizeDelta = new Vector2(440,Mathf.Max(20,reputation.preferredHeight));
            float objectiveTop = 76 + reputation.rectTransform.sizeDelta.y + 10;
            objectives.rectTransform.anchoredPosition = new Vector2(32,-objectiveTop);
            float height = Mathf.Max(24,objectives.preferredHeight);
            objectives.rectTransform.sizeDelta = new Vector2(440,height);
            transmission.rectTransform.anchoredPosition = new Vector2(32,-objectiveTop - height - 12);
            transmission.rectTransform.sizeDelta = new Vector2(440,Mathf.Max(24,transmission.preferredHeight));
        }

        private void CreateCityMap()
        {
            RectTransform map = Panel(gameplayLayer.transform, "CityMap", new Vector2(166,194), new Color(.024f,.043f,.055f,.48f));
            map.anchorMin = map.anchorMax = map.pivot = Vector2.zero;
            map.anchoredPosition = new Vector2(32,82);
            map.GetComponent<Image>().raycastTarget = false;
            Label(map, "MapTitle", "DISTRICT GRID    N ↑", 9, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(12,-10), new Vector2(148,18), Muted);
            districtCells = new Image[9];
            for (int row = 0; row < 3; row++) for (int col = 0; col < 3; col++)
            {
                int index = row * 3 + col;
                RectTransform cell = Panel(map, "Block_" + index, new Vector2(38,38), new Color(.1f,.16f,.19f,.55f));
                cell.anchoredPosition = new Vector2((col-1)*44,(row-1)*44);
                districtCells[index] = cell.GetComponent<Image>();
                districtCells[index].raycastTarget = false;
                Label(cell, "Label", index == 4 ? "HOME" : (index+1).ToString("00"), 8, TextAnchor.MiddleCenter,
                    new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(input.UseTouchControls ? 38 : 48,25), Muted);
            }
            Text pointer = Label(map, "PlayerPosition", "▲", 13, TextAnchor.MiddleCenter,
                new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(20,20), Cream);
            mapPointer = pointer.rectTransform;
            cityProgress = Label(map, "DistrictProgress", "0 / 9 BLOCKS EXPLORED", 8, TextAnchor.LowerCenter,
                new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(0,10), new Vector2(155,18), Muted);
            districtName = Label(gameplayLayer.transform, "DistrictName", "HOME BLOCK", 10, TextAnchor.LowerLeft,
                Vector2.zero, Vector2.zero, new Vector2(32,285), new Vector2(280,20), Cream);
            homeDistance = Label(gameplayLayer.transform, "HomeDistance", "", 9, TextAnchor.UpperLeft,
                Vector2.zero, new Vector2(0,1), new Vector2(32,72), new Vector2(310,34), Muted);
        }

        public void RenderCity(SlopCityFirstPerson player)
        {
            if (!initialized || districtCells == null) return;
            Vector3 position = player.transform.position;
            foreach (SlopCityDistrict district in player.Districts)
            {
                if (!district) continue;
                int index = district.Row * 3 + district.Column;
                if (index < 0 || index >= districtCells.Length) continue;
                bool current = district.ContainsStreetPoint(position);
                districtCells[index].color = current ? new Color(.26f,.40f,.45f,.8f)
                    : player.HasVisited(index) ? new Color(.13f,.25f,.29f,.6f) : new Color(.1f,.16f,.19f,.55f);
            }
            mapPointer.anchoredPosition = new Vector2(Mathf.Clamp(position.x,-111,111)*.535f,Mathf.Clamp(position.z-8,-75,75)*.792f);
            mapPointer.localRotation = Quaternion.Euler(0,0,-player.transform.eulerAngles.y);
            string progress = player.VisitedBlockCount + " / 9 BLOCKS EXPLORED";
            if (cityProgress.text != progress) cityProgress.text = progress;
            string district = player.CurrentDistrictName;
            if (districtName.text != district) districtName.text = district;
            string distance = player.IsOutside ? Mathf.RoundToInt(player.HomeDistance) + "m FROM HOME  /  R TO RETURN\nE  OPEN DOORS   /   SHIFT  RUN" : "E  OPEN THE CITY DOOR\nUSE THE TV TO CHOOSE MEDIA";
            if (homeDistance.text != distance) homeDistance.text = distance;
        }

        private static string Objective(bool complete, string label)
        {
            return complete ? "<color=#A4BA98>[x] " + label + "</color>" : "[ ] " + label;
        }

        public void SetMenu(bool visible, bool hasStarted)
        {
            if (!initialized) return;
            sourceBrowser.Hide();
            menuStarted = hasStarted;
            menu.SetActive(visible);
            sourceCard.SetActive(false);
            menuHeading.text = hasStarted ? "CONNECTION PAUSED" : "WELCOME HOME, DANIEL";
            playLabel.text = hasStarted ? "RESUME" : "PLAY";
            restartButton.SetActive(hasStarted);
            RectTransform quitRect = quitButton.GetComponent<RectTransform>();
            bool desktopPause = hasStarted && !input.UseTouchControls;
            float actionOffset = desktopPause ? 24 : 0;
            menuCard.sizeDelta = new Vector2(410, hasStarted ? (desktopPause ? 528 : 570) : 522);
            playButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(0,16 + actionOffset);
            restartButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(0,-36 + actionOffset);
            licenseButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(0,(hasStarted ? -87 : -36) + actionOffset);
            quitRect.anchoredPosition = new Vector2(0,(hasStarted ? -139 : -88) + actionOffset);
            quitRect.sizeDelta = new Vector2(350, 40);
            quitButton.GetComponentInChildren<Text>().rectTransform.sizeDelta = quitRect.sizeDelta;
#if UNITY_WEBGL && !UNITY_EDITOR
            quitButton.gameObject.SetActive(hasStarted);
#endif
            if (visible && EventSystem.current && !input.UseTouchControls)
                EventSystem.current.SetSelectedGameObject(playButton.gameObject);
            ApplyResponsiveLayout(true);
            ApplyModalVisibility();
        }

        public void ShowSource(string title, string description)
        {
            sourceHeading.text = title;
            sourceDescription.text = description;
            ShowSourceTitleOnly();
        }

        public void ShowLocalReport(string title, string report)
        {
            if (!initialized || !sourceBrowser) return;
            sourceCard.SetActive(false);
            menu.SetActive(true);
            sourceBrowser.ShowLocalReport(title, report);
            ApplyModalVisibility();
        }

        public void ShowSourceTitleOnly()
        {
            if (!initialized) return;
            sourceBrowser.Hide();
            menu.SetActive(false); sourceCard.SetActive(true);
            ApplyModalVisibility();
            if (EventSystem.current && returnToLoungeButton)
                EventSystem.current.SetSelectedGameObject(returnToLoungeButton.gameObject);
        }

        public void HideSource() { if (sourceCard) sourceCard.SetActive(false); if (initialized) ApplyModalVisibility(); }

        public void RenderMedia(SlopCityMediaPlayer media)
        {
            if (!initialized || !media) return;
            sourceHeading.text = media.HasSelection ? media.Title : "LOUNGE MEDIA";
            sourceDescription.text = media.Status + (media.DurationSeconds > 0
                ? "   /   " + Timestamp(media.TimeSeconds) + " / " + Timestamp(media.DurationSeconds) : "");
            mediaPlayLabel.text = media.IsPlaying || (media.IsLoading && !media.IsPaused) ? "PAUSE" : "PLAY";
            mediaVolumeLabel.text = "VOLUME " + Mathf.RoundToInt(media.Volume * 100f) + "%";
            nowPlaying.text = media.HasSelection ? media.Title + "\n" + media.Status + (input.UseTouchControls ? "" : "   /   M  CONTROLS") : "";
            if (cityMediaStatus && SlopCityCityMedia.Instance)
                cityMediaStatus.text = SlopCityCityMedia.Instance.Status
                    + (SlopCityTikTokSync.Instance ? "\n" + SlopCityTikTokSync.Instance.Status : "");
        }

        private static string Timestamp(double seconds)
        {
            int wholeSeconds = Mathf.Max(0, (int)seconds);
            return (wholeSeconds / 60).ToString("00") + ":" + (wholeSeconds % 60).ToString("00");
        }

        private void CreateMenu(SlopCityFirstPerson player)
        {
            menu = FullScreenPanel("PauseAndStart", new Color(.009f,.018f,.026f,.3f));
            RectTransform panel = menuCard = Panel(menu.transform, "MenuCard", new Vector2(410,522), Ink);
            panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(0,.5f);panel.anchoredPosition=new Vector2(76,0);
            Label(panel, "Brand", "SLOP CITY™", 32, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(30,-30), new Vector2(350,48), Cream);
            Label(panel, "Byline", "© 2026 Machine Publishers, LLC", 10, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(32,-84), new Vector2(350,24), Lime);
            menuHeading = Label(panel, "Heading", "WELCOME HOME, DANIEL", 15, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(32,-133), new Vector2(350,28), Cream);
            Label(panel, "Description", "Keep the city working through 40 kinds of jobs.\nUse a woman in red for a brief dispatch signal.", 12, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(32,-170), new Vector2(340,42), Muted);
            playButton = Button(panel, "PlayResume", "PLAY", new Vector2(0,-10), new Vector2(350,44), player.PlayOrResume, true, true);
            playLabel = playButton.GetComponentInChildren<Text>();
            restartButton = Button(panel, "RestartLounge", "RETURN HOME + NEW JOB", new Vector2(0,-62), new Vector2(350,40), player.RestartLounge, false, true).gameObject;
            licenseButton = Button(panel, "LicenseAndSource", "LICENSE + SOURCE", new Vector2(0,-36), new Vector2(350,44), () => sourceBrowser.Show());
#if UNITY_WEBGL && !UNITY_EDITOR
            quitButton = Button(panel, "Quit", "RETURN TO TITLE", new Vector2(0,-165), new Vector2(350,40), player.QuitGame);
#else
            quitButton = Button(panel, "Quit", "QUIT GAME", new Vector2(0,-165), new Vector2(350,40), player.QuitGame);
#endif
            string controls = input.UseTouchControls
                ? "LEFT THUMB MOVE  /  RIGHT THUMB LOOK\nUSE TO INTERACT  /  CLEAN TO POLISH\nTAKE TO COLLECT  /  BAG FOR INVENTORY"
                : "WASD MOVE   MOUSE LOOK   SHIFT RUN   SPACE JUMP\nE USE   C CLEAN   F TAKE   I BAG\nQ NEXT JOB   R HOME\nH HIDE HUD   ESC PAUSE";
            Label(panel, "Controls", controls, 9, TextAnchor.LowerLeft,
                Vector2.zero, Vector2.zero, new Vector2(32,40), new Vector2(350,46), Muted);
            Label(panel, "Scope", "LOCAL SESSION  /  PROGRESSION SAVED", 8, TextAnchor.LowerLeft,
                Vector2.zero, Vector2.zero, new Vector2(32,18), new Vector2(350,16), Muted);
            RectTransform rule=Panel(panel,"MenuIndex",new Vector2(2,54),Lime);
            rule.anchorMin=rule.anchorMax=rule.pivot=new Vector2(0,1);rule.anchoredPosition=new Vector2(0,-31);rule.GetComponent<Image>().raycastTarget=false;
        }

        private void CreateSourceCard(SlopCityFirstPerson player)
        {
            sourceCard = FullScreenPanel("SourceCard", new Color(.009f,.018f,.026f,.48f));
            RectTransform panel = Panel(sourceCard.transform, "SourceContent", new Vector2(1040,600), Ink);
            Label(panel, "Tag", "CITY SIGNALS  /  LOCAL RECEIVER", 10, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(30,-24), new Vector2(760,22), Lime);
            sourceHeading = Label(panel, "Title", "LOUNGE MEDIA", 21, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(30,-54), new Vector2(960,38), Cream);
            Label(panel, "PlaybackLocation", "LIVING-ROOM TV\n\nChoose the living-room television media here.\nReturn to the city to watch on their screens.", 17, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(30,-130), new Vector2(550,170), Cream);
            sourceDescription = Label(panel, "PlaybackStatus", "Choose a channel to play in the lounge.", 11, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(30,-360), new Vector2(550,70), Muted);
            Button(panel, "Broadcast", "MP BROADCAST", new Vector2(-402,-183), new Vector2(174,38), () => player.PlayMedia(SlopCityMediaChannel.Broadcast));
            Button(panel, "Original", "IN-Q-TEL", new Vector2(-214,-183), new Vector2(174,38), () => player.PlayMedia(SlopCityMediaChannel.Record));
            Button(panel, "Duet", "1984DRH DUET", new Vector2(-26,-183), new Vector2(174,38), () => player.PlayMedia(SlopCityMediaChannel.Duet));
            mediaPlayLabel = Button(panel, "PlayPause", "PLAY", new Vector2(-430,-236), new Vector2(120,38), player.ToggleMedia, true).GetComponentInChildren<Text>();
            Button(panel, "Stop", "STOP", new Vector2(-318,-236), new Vector2(84,38), player.StopMedia);
            Button(panel, "VolumeDown", "−", new Vector2(-245,-236), new Vector2(36,38), () => player.ChangeMediaVolume(-0.1f));
            mediaVolumeLabel = Label(panel, "Volume", "VOLUME 70%", 10, TextAnchor.MiddleCenter,
                new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(-139,-236), new Vector2(148,38), Muted);
            Button(panel, "VolumeUp", "+", new Vector2(-30,-236), new Vector2(36,38), () => player.ChangeMediaVolume(0.1f));
            RectTransform network = Panel(panel, "CityNetwork", new Vector2(352,357), new Color(.037f,.062f,.078f,.8f));
            network.anchoredPosition = new Vector2(317,17);
            Label(network,"NetworkTitle","PUBLIC BROADCAST NETWORK",10,TextAnchor.UpperLeft,new Vector2(0,1),new Vector2(0,1),new Vector2(20,-20),new Vector2(310,20),Lime);
#if UNITY_WEBGL && !UNITY_EDITOR
            const string initialNetworkStatus = "Loading bundled city broadcasts…";
#else
            const string initialNetworkStatus = "Synchronizing public TikTok posts…";
#endif
            cityMediaStatus = Label(network, "NetworkStatus", initialNetworkStatus, 11, TextAnchor.UpperLeft,
                new Vector2(0,1), new Vector2(0,1), new Vector2(20,-54), new Vector2(312,82), Cream);
            Button(network, "DRHFeed", "@1984drh  ↗", new Vector2(0,6), new Vector2(312,36), () => player.OpenCreatorFeed(0));
            Button(network, "MPFeed", "@machine_publishers  ↗", new Vector2(0,-40), new Vector2(312,36), () => player.OpenCreatorFeed(1));
            float networkActionWidth = input.UseTouchControls ? 148 : 160;
#if UNITY_WEBGL && !UNITY_EDITOR
            Button(network, "CreatorFeeds", "CREATOR FEEDS ↗", new Vector2(-82,-88), new Vector2(networkActionWidth,36), () => { if (SlopCityCityMedia.Instance) SlopCityCityMedia.Instance.OpenCreatorPortal(); });
#else
            Button(network, "RefreshTikTok", "SYNC POSTS", new Vector2(-82,-88), new Vector2(networkActionWidth,36), () => { if (SlopCityTikTokSync.Instance) SlopCityTikTokSync.Instance.RequestRefresh(); });
#endif
            Button(network, "NetworkPause", "PAUSE / RESUME", new Vector2(82,-88), new Vector2(networkActionWidth,36), player.ToggleCityMedia);
#if UNITY_WEBGL && !UNITY_EDITOR
            const string feedHint = "Bundled broadcasts play across the city.\nOpen creator feeds for their latest posts.";
#else
            const string feedHint = "Both accounts refresh every five minutes.\nVideos and photo posts rotate across the city.";
#endif
            Label(network, "FeedHint", feedHint, 9, TextAnchor.LowerLeft,
                Vector2.zero,Vector2.zero,new Vector2(20,22),new Vector2(312,36),Muted);
            returnToLoungeButton = Button(panel, "ReturnToLounge", "BACK TO THE CITY", new Vector2(317,-236), new Vector2(352,38), player.PlayOrResume, true, true);
            sourceCard.SetActive(false);
        }

        private void CreateTouchControls(SlopCityPlayerInput source)
        {
            touchControls = SlopCityMobileControls.Create(transform, source, font);
        }

        private void ApplyResponsiveLayout(bool force = false)
        {
            if (!input) return;
            Vector2 viewport = new Vector2(Screen.width, Screen.height);
            Vector2 canvasSize = ((RectTransform)transform).rect.size;
            Rect safeArea = Screen.safeArea;
            if (!force && viewport == lastViewport && canvasSize == lastCanvasSize && safeArea == lastSafeArea) return;
            lastViewport = viewport;
            if (!input.UseTouchControls)
            {
                CanvasScaler desktopScaler = GetComponent<CanvasScaler>();
                if (desktopScaler)
                {
                    desktopScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    desktopScaler.referenceResolution = new Vector2(1600,900) / DesktopUiScale;
                    desktopScaler.matchWidthOrHeight = .5f;
                }
                Canvas.ForceUpdateCanvases();
                Vector2 desktopSize = ((RectTransform)transform).rect.size;
                lastCanvasSize = desktopSize;
                lastSafeArea = safeArea;
                LayoutDesktopCards(desktopSize);
                return;
            }
            bool portrait = Screen.height > Screen.width;
            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = portrait ? new Vector2(360,700) : new Vector2(720,280);
                scaler.matchWidthOrHeight = portrait ? 0 : 1;
            }
            Canvas.ForceUpdateCanvases();
            Vector2 size = ((RectTransform)transform).rect.size;
            // CanvasScaler applies its new scale during Update; its resulting rect size
            // invalidates this cache next frame even when the screen size is unchanged.
            lastCanvasSize = size;
            lastSafeArea = safeArea;
            float width = size.x, top = (Screen.height - safeArea.yMax) * size.y / Mathf.Max(1,Screen.height);
            float bottom = safeArea.yMin * size.y / Mathf.Max(1,Screen.height);
            Transform map = gameplayLayer.transform.Find("CityMap"); if (map) map.gameObject.SetActive(false);
            districtName.gameObject.SetActive(false); homeDistance.gameObject.SetActive(false); nowPlaying.gameObject.SetActive(false);
            if (controlsLabel) controlsLabel.gameObject.SetActive(false);
            Transform subtitle = gameplayLayer.transform.Find("Subtitle"); if (subtitle) subtitle.gameObject.SetActive(false);
            Transform rule = gameplayLayer.transform.Find("TelemetryRule"); if (rule) rule.gameObject.SetActive(false);
            Transform brand = gameplayLayer.transform.Find("Brand"); if (brand) brand.gameObject.SetActive(false);
            reputation.gameObject.SetActive(false);
            if (stateLabel) stateLabel.gameObject.SetActive(false);
            LayoutMobileGuidance();
            LayoutMobileMenu(width,portrait);
            LayoutMobileSource(width,top,bottom);
        }

        private void LayoutDesktopCards(Vector2 viewport)
        {
            // Smaller desktop windows scroll overflowing cards while retaining
            // readable type and the full set of actions.
            LayoutDesktopCard(menu,menuCard,ref desktopMenuViewport,viewport - new Vector2(100,40),true);
            RectTransform media = desktopMediaViewport ? (RectTransform)desktopMediaViewport.GetChild(0)
                : (RectTransform)sourceCard.transform.Find("SourceContent");
            LayoutDesktopCard(sourceCard,media,ref desktopMediaViewport,viewport - new Vector2(48,48),false);
        }

        private static void LayoutDesktopCard(GameObject modal, RectTransform card, ref RectTransform viewport, Vector2 available, bool leftAligned)
        {
            if (!viewport)
            {
                viewport = new GameObject("DesktopCardViewport",typeof(RectTransform),typeof(RectMask2D)).GetComponent<RectTransform>();
                viewport.SetParent(modal.transform,false);
                ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
                scroll.viewport = viewport; scroll.content = card;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 28;
                card.SetParent(viewport,false);
                card.anchorMin = card.anchorMax = card.pivot = new Vector2(0,1);
                card.anchoredPosition = Vector2.zero;
            }
            viewport.anchorMin = viewport.anchorMax = viewport.pivot = new Vector2(leftAligned ? 0 : .5f,.5f);
            viewport.anchoredPosition = new Vector2(leftAligned ? 76 : 0,0);
            viewport.sizeDelta = new Vector2(Mathf.Clamp(available.x,80,card.sizeDelta.x),Mathf.Clamp(available.y,80,card.sizeDelta.y));
            ScrollRect scroller = viewport.GetComponent<ScrollRect>();
            scroller.horizontal = viewport.sizeDelta.x < card.sizeDelta.x;
            scroller.vertical = viewport.sizeDelta.y < card.sizeDelta.y;
        }

        private void LayoutMobileGuidance()
        {
            if (!input || !input.UseTouchControls || !objectives || !transmission) return;
            Vector2 size = ((RectTransform)transform).rect.size;
            bool portrait = Screen.height > Screen.width;
            Rect safe = Screen.safeArea;
            float pixelRatio = SlopCityWebPresentation.DisplayPixelRatio;
            string prompt = promptLabel ? promptLabel.text : "";
            if (size == lastGuidanceSize && safe == lastGuidanceSafeArea && Mathf.Approximately(pixelRatio,lastGuidancePixelRatio)
                && objectives.text == lastMobileObjective && reputation.text == lastMobileReputation
                && transmission.text == lastMobileTransmission && prompt == lastMobilePrompt) return;
            lastGuidanceSize = size; lastGuidanceSafeArea = safe;
            lastGuidancePixelRatio = pixelRatio;
            lastMobileObjective = objectives.text; lastMobileReputation = reputation.text;
            lastMobileTransmission = transmission.text; lastMobilePrompt = prompt;
            float left = safe.xMin * size.x / Mathf.Max(1,Screen.width);
            float right = (Screen.width - safe.xMax) * size.x / Mathf.Max(1,Screen.width);
            float top = (Screen.height - safe.yMax) * size.y / Mathf.Max(1,Screen.height);
            float bottom = safe.yMin * size.y / Mathf.Max(1,Screen.height);
            float textWidth = portrait ? size.x - left - right - 24 : Mathf.Min(380,(size.x - left - right) * .42f);
            if (!portrait)
            {
                // Reserve the gap before the top-right controls, clear of the floating joystick.
                float pixelsPerUnit = Mathf.Max(.1f,GetComponent<Canvas>().scaleFactor);
                float button = Mathf.Clamp(Mathf.Min(Screen.width,Screen.height) * .16f,54f*pixelRatio,88f*pixelRatio) / pixelsPerUnit;
                float controlsLeft = size.x - right - button * (.24f + .8f * 4 + .18f * .65f * 3);
                textWidth = Mathf.Min(textWidth,controlsLeft - left - 118);
            }
            textWidth = Mathf.Max(100,textWidth);
            float down = top + (portrait ? 82 : 12);
            objectives.fontSize = transmission.fontSize = 16;
            objectives.lineSpacing = transmission.lineSpacing = 1.05f;
            objectives.rectTransform.anchoredPosition = new Vector2(12 + left,-down);
            objectives.rectTransform.sizeDelta = new Vector2(textWidth,48);
            float objectiveHeight = Mathf.Max(22,objectives.preferredHeight);
            objectives.rectTransform.sizeDelta = new Vector2(textWidth,objectiveHeight);
            down += objectiveHeight + 6;
            reputation.fontSize = 14; reputation.lineSpacing = 1.05f;
            reputation.rectTransform.anchoredPosition = new Vector2(12 + left,-down);
            reputation.rectTransform.sizeDelta = new Vector2(textWidth,Mathf.Max(22,reputation.preferredHeight));
            down += reputation.rectTransform.sizeDelta.y + 8;
            transmission.rectTransform.anchoredPosition = new Vector2(12 + left,-down);
            transmission.rectTransform.sizeDelta = new Vector2(textWidth,120);
            float transmissionHeight = transmission.enabled ? Mathf.Max(22,transmission.preferredHeight) : 0;
            transmission.rectTransform.sizeDelta = new Vector2(textWidth,transmissionHeight);

            if (promptLabel)
            {
                promptLabel.fontSize = 16; promptLabel.lineSpacing = 1.05f;
                promptLabel.rectTransform.anchorMin = promptLabel.rectTransform.anchorMax = promptLabel.rectTransform.pivot = new Vector2(.5f,0);
                promptLabel.rectTransform.sizeDelta = new Vector2(portrait ? size.x - left - right - 24 : Mathf.Min(350,size.x * .40f),70);
                promptLabel.rectTransform.sizeDelta = new Vector2(promptLabel.rectTransform.sizeDelta.x,Mathf.Max(42,promptLabel.preferredHeight + 8));
                promptLabel.rectTransform.anchoredPosition = new Vector2((left - right) * .5f,bottom + (portrait ? 154 : 16));
            }
        }

        private void LayoutMobileMenu(float viewportWidth, bool portrait)
        {
            if (!menuCard) return;
            float cardWidth = Mathf.Min(portrait ? 440 : 720,viewportWidth - 24);
            float inner = cardWidth - 32;
            menuCard.anchorMin = menuCard.anchorMax = menuCard.pivot = new Vector2(.5f,.5f);
            menuCard.anchoredPosition = Vector2.zero;
            menuCard.sizeDelta = new Vector2(cardWidth,portrait ? (menuStarted ? 548 : 488) : 256);
            foreach (Text text in menuCard.GetComponentsInChildren<Text>(true)) text.fontSize = 16;
            menuCard.Find("Brand").GetComponent<Text>().fontSize = 28;
            menuHeading.fontSize = 18;
            Text help = menuCard.Find("Controls").GetComponent<Text>();
            help.text = "Left thumb: move\nRight thumb: look\nUSE interacts / RUN sprints\nUSE a woman in red for a signal";
            help.alignment = TextAnchor.UpperLeft; help.lineSpacing = 1.15f;
            menuCard.Find("Scope").gameObject.SetActive(portrait);
            if (portrait)
            {
                menuCard.Find("Byline").gameObject.SetActive(true);
                menuCard.Find("Description").gameObject.SetActive(true);
                PlaceTop(menuCard.Find("Brand"),0,16,inner,34);
                PlaceTop(menuCard.Find("Byline"),0,56,inner,22);
                PlaceTop(menuHeading.transform,0,86,inner,24);
                PlaceTop(menuCard.Find("Description"),0,118,inner,46);
                PlaceTop(playButton.transform,0,178,inner,46);
                PlaceTop(restartButton.transform,0,232,inner,46);
                PlaceTop(licenseButton.transform,0,menuStarted ? 286 : 232,inner,46);
                PlaceTop(quitButton.transform,0,menuStarted ? 340 : 286,inner,46);
                PlaceTop(help.transform,0,menuStarted ? 402 : 348,inner,96);
                PlaceTop(menuCard.Find("Scope"),0,menuStarted ? 516 : 462,inner,22);
                menuCard.Find("Scope").GetComponent<Text>().text = "PROGRESS SAVED";
            }
            else
            {
                float column = (inner - 24) * .5f, x = (column + 24) * .5f;
                PlaceTop(menuCard.Find("Brand"),-x,12,column,34);
                PlaceTop(menuCard.Find("Byline"),-x,50,column,22);
                PlaceTop(menuHeading.transform,-x,82,column,24);
                PlaceTop(menuCard.Find("Description"),-x,113,column,44);
                PlaceTop(help.transform,-x,168,column,80);
                help.fontSize = 16; help.lineSpacing = 1f;
                PlaceTop(playButton.transform,x,menuStarted ? 14 : 68,column,46);
                PlaceTop(restartButton.transform,x,70,column,46);
                if (menuStarted)
                {
                    float half = (column - 8) * .5f;
                    PlaceTop(quitButton.transform,x - (half + 8) * .5f,182,half,46);
                    PlaceTop(licenseButton.transform,x + (half + 8) * .5f,182,half,46);
                    licenseButton.GetComponentInChildren<Text>().fontSize = 14;
                }
                else
                {
                    PlaceTop(licenseButton.transform,x,124,column,46);
                    PlaceTop(quitButton.transform,x,180,column,46);
                }
                bool narrow = column < 260;
                menuCard.Find("Byline").gameObject.SetActive(!narrow);
                menuCard.Find("Description").gameObject.SetActive(!narrow);
                if (narrow)
                {
                    PlaceTop(menuHeading.transform,-x,58,column,50);
                    PlaceTop(help.transform,-x,118,column,126);
                    help.text = "Left thumb moves.\nRight thumb looks.\nUSE interacts.\nUSE a woman in red for a signal.";
                }
            }
        }

        private static void PlaceTop(Transform value, float x, float down, float width, float height)
        {
            if (!value) return;
            var rect = (RectTransform)value;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,1);
            rect.anchoredPosition = new Vector2(x,-down); rect.sizeDelta = new Vector2(width,height);
            Button button = value.GetComponent<Button>();
            if (button) button.GetComponentInChildren<Text>().rectTransform.sizeDelta = rect.sizeDelta;
        }

        private void LayoutMobileSource(float viewportWidth, float top, float bottom)
        {
            if (!sourceCard) return;
            RectTransform panel;
            if (!sourceViewport)
            {
                panel = (RectTransform)sourceCard.transform.Find("SourceContent");
                sourceViewport = new GameObject("MobileMediaViewport",typeof(RectTransform),typeof(RectMask2D)).GetComponent<RectTransform>();
                sourceViewport.SetParent(sourceCard.transform,false); Stretch(sourceViewport);
                panel.SetParent(sourceViewport,false);
                ScrollRect scroll = sourceCard.AddComponent<ScrollRect>(); scroll.viewport = sourceViewport; scroll.content = panel;
                scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
                returnToLoungeButton.transform.SetParent(sourceCard.transform,false); returnToLoungeButton.transform.SetAsLastSibling();
            }
            panel = (RectTransform)sourceViewport.GetChild(0);
            sourceViewport.offsetMin = new Vector2(0,66 + bottom); sourceViewport.offsetMax = new Vector2(0,-top);
            float width = Mathf.Min(460,viewportWidth - 24), inner = width - 32;
            foreach (Text text in sourceCard.GetComponentsInChildren<Text>(true)) text.fontSize = 16;
            PlaceTop(panel,0,8,width,1000);
            PlaceTop(panel.Find("Tag"),0,16,inner,24);
            PlaceTop(sourceHeading.transform,0,48,inner,52); sourceHeading.fontSize = 20;
            PlaceTop(panel.Find("PlaybackLocation"),0,112,inner,140);
            float row = 264;
            PlaceTop(sourceDescription.transform,0,row,inner,70);
            float third = (inner - 12) / 3f;
            PlaceTop(panel.Find("Broadcast"),-third - 6,row + 82,third,52);
            PlaceTop(panel.Find("Original"),0,row + 82,third,52);
            PlaceTop(panel.Find("Duet"),third + 6,row + 82,third,52);
            PlaceTop(panel.Find("PlayPause"),-inner / 4f - 3,row + 144,inner / 2f - 6,44);
            PlaceTop(panel.Find("Stop"),inner / 4f + 3,row + 144,inner / 2f - 6,44);
            float volumeX = inner * .5f - 22;
            PlaceTop(panel.Find("VolumeDown"),-volumeX,row + 200,44,42);
            PlaceTop(mediaVolumeLabel.transform,0,row + 200,inner - 100,42);
            PlaceTop(panel.Find("VolumeUp"),volumeX,row + 200,44,42);
            Transform network = panel.Find("CityNetwork"); PlaceTop(network,0,row + 258,inner,394);
            network.Find("NetworkTitle").gameObject.SetActive(false);
            PlaceTop(cityMediaStatus.transform,0,16,inner - 24,66);
            PlaceTop(network.Find("DRHFeed"),0,94,inner - 24,44);
            PlaceTop(network.Find("MPFeed"),0,148,inner - 24,44);
            PlaceTop(network.Find("CreatorFeeds"),-inner / 4f + 6,202,inner / 2f - 18,52);
            PlaceTop(network.Find("RefreshTikTok"),-inner / 4f + 6,202,inner / 2f - 18,52);
            PlaceTop(network.Find("NetworkPause"),inner / 4f - 6,202,inner / 2f - 18,52);
            PlaceTop(network.Find("FeedHint"),0,270,inner - 24,110);
            network.Find("FeedHint").GetComponent<Text>().alignment = TextAnchor.UpperLeft;
            panel.sizeDelta = new Vector2(width,row + 668);
            RectTransform back = (RectTransform)returnToLoungeButton.transform;
            back.anchorMin = back.anchorMax = back.pivot = new Vector2(.5f,0);
            back.anchoredPosition = new Vector2(0,10 + bottom); back.sizeDelta = new Vector2(Mathf.Min(420,viewportWidth - 24),46);
            returnToLoungeButton.GetComponentInChildren<Text>().rectTransform.sizeDelta = back.sizeDelta;
        }

        private void OnDestroy() { if (rabbitTrail) Destroy(rabbitTrail.gameObject); }



        private static void ParentUi(GameObject item, Transform parent)
        {
            try { item.transform.SetParent(parent, false); }
            catch { RetireUi(item); throw; }
        }

        private static void RetireUi(GameObject item)
        {
            if (!item) return;
            try { item.SetActive(false); }
            catch (System.Exception error) { Debug.LogWarning("HUD deactivation failed: " + error.GetType().Name); }
            try { Destroy(item); }
            catch (System.Exception error) { Debug.LogWarning("HUD cleanup failed: " + error.GetType().Name); }
        }

        private GameObject FullScreenPanel(string name, Color color)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            ParentUi(obj, transform);
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
            ParentUi(obj, parent);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f); rect.sizeDelta = size;
            obj.GetComponent<Image>().color = color;
            return rect;
        }

        private Button Button(Transform parent, string name, string text, Vector2 position, Vector2 size, UnityAction action, bool primary = false, bool startsGameplay = false)
        {
            RectTransform rect = Panel(parent, name, size, primary ? new Color(.16f,.28f,.33f,1) : new Color(.073f,.115f,.14f,1));
            rect.anchoredPosition = position;
#if UNITY_WEBGL && !UNITY_EDITOR
            Button button = startsGameplay ? rect.gameObject.AddComponent<SlopCityWebGameplayButton>() : rect.gameObject.AddComponent<Button>();
#else
            Button button = rect.gameObject.AddComponent<Button>();
#endif
            button.onClick.AddListener(action);
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.32f,1.4f,1.42f,1);
            colors.pressedColor = new Color(.6f,.8f,.9f,1);
            colors.selectedColor = new Color(1.2f,1.35f,1.42f,1);
            button.colors = colors;
            Label(rect, "Label", text, 12, TextAnchor.MiddleCenter, new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, size, Cream);
            RectTransform line = Panel(rect,"ActiveEdge",new Vector2(primary ? 3 : 1, size.y-12),primary ? Lime : new Color(.28f,.4f,.45f,.65f));
            line.anchorMin=line.anchorMax=line.pivot=new Vector2(0,.5f);line.anchoredPosition=new Vector2(0,0);line.GetComponent<Image>().raycastTarget=false;
            return button;
        }

        private Text Label(Transform parent, string name, string content, int size, TextAnchor alignment,
            Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 dimensions, Color color)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            ParentUi(obj, parent);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot;
            rect.anchoredPosition = position; rect.sizeDelta = dimensions;
            Text text = obj.GetComponent<Text>();
            text.font = content.Contains("™") ? SlopCityBrandTypography.Font : font;
            text.fontSize = FontSize(size); text.text = content; text.color = color;
            text.alignment = alignment; text.fontStyle = size >= 20 ? FontStyle.Bold : FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    /// <summary>Request browser pointer lock before mouse-up ends the player's gesture.</summary>
    public sealed class SlopCityWebGameplayButton : Button
    {
        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (eventData.button == PointerEventData.InputButton.Left && IsActive() && IsInteractable())
                onClick.Invoke();
        }

        // Keyboard submit still uses Button.OnSubmit; don't invoke a mouse action twice.
        public override void OnPointerClick(PointerEventData eventData) { }
    }
#endif
}
