#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SlopCity.Editor
{
    /// <summary>
    /// FBX contract: metres, Unity Y-up after import. Seated is an eye point;
    /// Stand is a feet point. Export marker empties with their exact names.
    /// No Unity project settings or packages are modified by this builder.
    /// </summary>
    public static class SlopCitySceneBuilder
    {
        public const string ModelPath = "Assets/Art/SlopCity_Lounge.fbx";
        public const string ScenePath = "Assets/Scenes/SlopCity_CouchSpawn.unity";
        public const string MaterialsFolder = "Assets/Materials/SlopCityGenerated";
        public const string SeatedMarker = "Spawn_Daniel_Seated";
        public const string StandingMarker = "Spawn_Daniel_Stand";
        public const string LookMarker = "LookAt_Daniel_Seated";
        public const string TvMarker = "Interact_TV";
        public const string RadioMarker = "Interact_Radio";
        public const string LampMarker = "Light_TableLamp";
        public const string HeadsetRigName = "Headset_Daniel_Rig";
        public const float PlayerVerticalFieldOfView = 59f;
        public const float PlayerNearClip = 0.01f;
        public const string FloorName = "Floor_Main";
        public const string CollisionPrefix = "COL_";
        public const string CouchPrefix = "SC_Couch";
        private static readonly string[] FurnitureCollisionNames =
        {
            "Coffee_Table_Top", "Coffee_Table_Leg", "TV_Console", "Broadcast_TV_Frame",
            "Record_Cabinet", "Side_Table", "Side_Table_Leg", "Plant_Pot"
        };

        // Optional material-name contract. Unlisted FBX materials retain their
        // imported base colour/texture while changing to built-in Standard.
        private static readonly PaletteEntry[] Palette =
        {
            // Names from the companion Blender lounge's authored materials.
            new PaletteEntry("Upholstery | midnight teal", "346766", 0f, 0.08f),
            new PaletteEntry("Upholstery | shadow teal", "204B4B", 0f, 0.04f),
            new PaletteEntry("Couch | sage piping", "78A59B", 0f, 0.18f),
            new PaletteEntry("Fabric | burnt tangerine", "C4673E", 0f, 0.05f),
            new PaletteEntry("Fabric | warm ochre", "DEB75B", 0f, 0.05f),
            new PaletteEntry("Wood | walnut", "594135", 0f, 0.4f),
            new PaletteEntry("Throw | oatmeal boucle", "D8CEB7", 0f, 0.02f),
            new PaletteEntry("Ceramic | bone", "EDE4CD", 0f, 0.67f),
            new PaletteEntry("Plaster | blue green", "244A4D", 0f, 0.06f),
            new PaletteEntry("Equipment | charcoal", "1A2326", 0f, 0.42f),
            new PaletteEntry("Metal | worn brass", "B3A37A", 0.65f, 0.72f),
            new PaletteEntry("Metal | gunmetal", "4B5D61", 0.75f, 0.68f),
            new PaletteEntry("Signal | phosphor", "DCF99C", 0f, 0.55f, 2.7f),
            new PaletteEntry("Sign | warm orange", "FFAC66", 0f, 0.6f, 3f),
            new PaletteEntry("Sign | cool cyan", "79B9B3", 0f, 0.5f, 1.4f),
            new PaletteEntry("Screen | dark phosphor", "142E2C", 0f, 0.64f, 0.1f),
            new PaletteEntry("City | amber windows", "D6AF69", 0f, 0.5f, 1.8f),
            new PaletteEntry("City | teal windows", "609C9A", 0f, 0.5f, 1.25f),
            new PaletteEntry("Lamp shade | linen", "EFCE93", 0f, 0.18f, 0.28f),
            new PaletteEntry("Rug | undyed wool", "CABD9C", 0f, 0.01f),
            new PaletteEntry("Rug | indigo edge", "425F64", 0f, 0.05f),
            new PaletteEntry("Vinyl | black lacquer", "091319", 0f, 0.8f),
            // Optional names supported for future replacement assets.
            new PaletteEntry("Couch_Upholstery", "6D7450", 0f, 0.08f),
            new PaletteEntry("Couch_Wood", "35251D", 0f, 0.2f),
            new PaletteEntry("Floor_Wood", "635A46", 0f, 0.18f),
            new PaletteEntry("Wall_Plaster", "555951", 0f, 0.05f),
            new PaletteEntry("Metal_Dark", "202727", 0.7f, 0.35f),
            new PaletteEntry("Screen_Emission", "94BE77", 0f, 0.25f, 0.8f),
            new PaletteEntry("Lamp_Shade", "E5B679", 0f, 0.1f, 0.18f)
        };

        [MenuItem("SLOP CITY/Build Couch Spawn Scene")]
        public static void BuildScene()
        {
            if (Application.isBatchMode)
            {
                BuildSceneForAutomation();
                return;
            }
            try { BuildSceneInternal(true); }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorUtility.DisplayDialog("SLOP CITY — scene build stopped", error.Message, "OK");
            }
        }

        /// <summary>No dialogs. Replaces only the named generated scene/material assets.</summary>
        public static Scene BuildSceneForAutomation()
        {
            return BuildSceneInternal(false);
        }

        /// <summary>Player builds are dialog-free in batch mode and protect manual Editor work.</summary>
        public static Scene BuildSceneForPlayerBuild()
        {
            return BuildSceneInternal(!Application.isBatchMode);
        }

        private static Scene BuildSceneInternal(bool interactive)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building the scene.");
            if (!File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, ModelPath)))
                throw new InvalidOperationException("Missing " + ModelPath + ". Export the Blender lounge into Assets/Art before building.");

            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureImporter();
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!model) throw new InvalidOperationException("Unity could not import " + ModelPath + ".");
            RequireUniqueTransform(model.transform, SeatedMarker);
            RequireUniqueTransform(model.transform, StandingMarker);
            RequireUniqueTransform(model.transform, HeadsetRigName);
            ValidateFloorScale(RequireFloorBounds(model.transform));
            Shader standard = Shader.Find("Standard");
            if (!standard) throw new InvalidOperationException("The built-in Standard shader is unavailable.");
            if (GraphicsSettings.currentRenderPipeline != null)
                throw new InvalidOperationException("SLOP CITY requires the Built-In Render Pipeline.");

            if (interactive)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return default(Scene);
                if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Replace generated scene?",
                    "This rebuilds " + ScenePath + ". Duplicate the scene first to retain manual edits.", "Rebuild", "Cancel")) return default(Scene);
            }

            EnsureFolder("Assets/Scenes");
            EnsureFolder(MaterialsFolder);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject lounge = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (!lounge) throw new InvalidOperationException("Could not instantiate the lounge FBX.");
            PrefabUtility.UnpackPrefabInstance(lounge, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            lounge.name = "SlopCity_Lounge";
            lounge.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            lounge.transform.localScale = Vector3.one;

            Transform seat = RequireUniqueTransform(lounge.transform, SeatedMarker);
            Transform stand = RequireUniqueTransform(lounge.transform, StandingMarker);
            Transform look = FindUniqueOptional(lounge.transform, LookMarker);
            Transform tv = FindUniqueOptional(lounge.transform, TvMarker);
            Transform radio = FindUniqueOptional(lounge.transform, RadioMarker);
            Transform lamp = FindUniqueOptional(lounge.transform, LampMarker);
            Transform headset = RequireUniqueTransform(lounge.transform, HeadsetRigName);
            Bounds floor = RequireFloorBounds(lounge.transform);
            ValidateFloorScale(floor);

            AssignStandardMaterials(lounge, standard);
            CreateGeometryColliders(lounge);
            CreateRoomPerimeter(floor);
            CreateLighting(lamp, floor);
            SlopCityHud hud = CreateHud();
            GameObject player = CreatePlayer(seat, stand, look, tv, radio, headset, hud);
            Physics.SyncTransforms();
            SlopCitySceneValidation.Validate(scene).ThrowIfInvalid();
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Unity could not save " + ScenePath + ".");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            if (interactive)
            {
                Selection.activeGameObject = player;
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
            }
            Debug.Log("SLOP CITY: generated and validated " + ScenePath + ". Clean first-person couch spawn is ready.");
            return scene;
        }

        private static void ValidateFloorScale(Bounds floor)
        {
            if (floor.size.x < 4f || floor.size.x > 30f || floor.size.z < 4f || floor.size.z > 30f)
                throw new InvalidOperationException("Floor_Main is not room-sized after FBX import (" + floor.size + "). Expected metres with an unscaled model root.");
        }

        private static void ConfigureImporter()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (!importer) throw new InvalidOperationException("The lounge file was not recognized as an FBX model.");
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
        }

        private static Transform RequireUniqueTransform(Transform root, string exactName)
        {
            Transform result = FindUniqueOptional(root, exactName);
            if (!result)
                throw new InvalidOperationException("The FBX is missing required marker '" + exactName + "'. Keep the named Blender Empty in the export, then re-export. The builder will not invent a spawn position.");
            return result;
        }

        private static Transform FindUniqueOptional(Transform root, string exactName)
        {
            Transform found = null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(child.name, exactName, StringComparison.Ordinal)) continue;
                if (found) throw new InvalidOperationException("Duplicate FBX marker '" + exactName + "'. Keep exactly one and re-export.");
                found = child;
            }
            return found;
        }

        private static Bounds RequireFloorBounds(Transform root)
        {
            Transform floor = RequireUniqueTransform(root, FloorName);
            Renderer[] renderers = floor.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException("Floor_Main must contain a floor mesh so room bounds can be generated.");
            Bounds result = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) result.Encapsulate(renderers[i].bounds);
            return result;
        }

        private static void CreateGeometryColliders(GameObject lounge)
        {
            foreach (MeshFilter filter in lounge.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh || !IsCollisionGeometry(filter.transform, lounge.transform)) continue;
                MeshCollider collider = filter.GetComponent<MeshCollider>();
                if (!collider) collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
                filter.gameObject.isStatic = true;
            }
        }

        private static bool IsCollisionGeometry(Transform candidate, Transform root)
        {
            // Decorative couch piping/stitches do not need hundreds of tiny colliders.
            string[] decorationTokens = { "Stitch", "Piping", "Welt", "Fringe", "MakersLabel", "Hem", "Tack" };
            if (candidate.name.StartsWith(CouchPrefix, StringComparison.Ordinal))
                foreach (string token in decorationTokens)
                    if (candidate.name.IndexOf(token, StringComparison.Ordinal) >= 0) return false;

            for (Transform node = candidate; node && node != root.parent; node = node.parent)
            {
                if (node.name == FloorName || node.name.StartsWith(CollisionPrefix, StringComparison.Ordinal)
                    || node.name.StartsWith(CouchPrefix, StringComparison.Ordinal)) return true;
                foreach (string furnitureName in FurnitureCollisionNames)
                {
                    // Blender uses .001 etc. for repeated legs and similar pieces.
                    if (node.name == furnitureName || node.name.StartsWith(furnitureName + ".", StringComparison.Ordinal)) return true;
                }
                if (node == root) break;
            }
            return false;
        }

        private static void CreateRoomPerimeter(Bounds floor)
        {
            GameObject boundsRoot = new GameObject("Lounge_RoomBoundary_Colliders");
            const float thickness = 0.18f;
            const float height = 3.2f;
            float centerY = floor.max.y + height * 0.5f;
            AddBox(boundsRoot.transform, "West", new Vector3(floor.min.x - thickness * 0.5f, centerY, floor.center.z), new Vector3(thickness, height, floor.size.z + thickness * 2f));
            AddBox(boundsRoot.transform, "East", new Vector3(floor.max.x + thickness * 0.5f, centerY, floor.center.z), new Vector3(thickness, height, floor.size.z + thickness * 2f));
            AddBox(boundsRoot.transform, "North", new Vector3(floor.center.x, centerY, floor.max.z + thickness * 0.5f), new Vector3(floor.size.x, height, thickness));
            AddBox(boundsRoot.transform, "South", new Vector3(floor.center.x, centerY, floor.min.z - thickness * 0.5f), new Vector3(floor.size.x, height, thickness));
        }

        private static void AddBox(Transform parent, string name, Vector3 center, Vector3 size)
        {
            GameObject box = new GameObject(name);
            box.transform.SetParent(parent, false);
            box.transform.position = center;
            BoxCollider collider = box.AddComponent<BoxCollider>();
            collider.size = size;
            box.isStatic = true;
        }

        private static GameObject CreatePlayer(Transform seat, Transform stand, Transform look,
            Transform tv, Transform radio, Transform headset, SlopCityHud hud)
        {
            GameObject player = new GameObject("Daniel_FirstPerson");
            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 1.72f;
            controller.radius = 0.22f;
            controller.center = Vector3.up * 0.86f;
            controller.skinWidth = 0.025f;
            controller.stepOffset = 0.25f;
            controller.slopeLimit = 45f;
            controller.minMoveDistance = 0f;
            controller.enabled = false;
            player.transform.position = seat.position;

            GameObject cameraObject = new GameObject("Daniel_FirstPersonCamera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(player.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = PlayerVerticalFieldOfView;
            camera.nearClipPlane = PlayerNearClip;
            camera.farClipPlane = 250f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Hex("202C35");
            camera.allowHDR = true;
            cameraObject.AddComponent<AudioListener>();
            Transform target = look ? look : tv;
            Vector3 forward = target ? target.position - seat.position : seat.forward;
            if (forward.sqrMagnitude > 0.001f) cameraObject.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            AttachHeadsetToCamera(headset, camera, seat);

            SlopCityFirstPerson movement = player.AddComponent<SlopCityFirstPerson>();
            movement.Configure(seat, stand, look, tv, radio, camera, hud);
            return player;
        }

        private static void AttachHeadsetToCamera(Transform headset, Camera camera, Transform seatedEye)
        {
            // The hidden equipment rig has -Z forward and +Y up in its authored local
            // space. FBX already converted that into Unity world space. Keep
            // the imported pose relative to the initial player camera instead
            // of applying another axis conversion or resetting local rotation.
            if ((headset.position - seatedEye.position).sqrMagnitude > 0.01f)
                Debug.LogWarning("SLOP CITY: Headset_Daniel_Rig is more than 10 cm from the seated eye. Re-export it at Spawn_Daniel_Seated for the intended visor framing.");
            headset.SetParent(camera.transform, true);

            foreach (Transform child in headset.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.isStatic = false;
                foreach (Collider collider in child.GetComponents<Collider>())
                    UnityEngine.Object.DestroyImmediate(collider);
            }
            foreach (Renderer renderer in headset.GetComponentsInChildren<Renderer>(true))
            {
                // Retain authored equipment for future views while keeping the
                // local first-person scene clear of dark curved visor geometry.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false; // Clean local first-person view; equipment remains for future avatars.
            }
        }

        private static void CreateLighting(Transform lampMarker, Bounds floor)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Hex("566372") * 0.6f;
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Hex("283740");
            RenderSettings.fogDensity = 0.009f;

            GameObject lights = new GameObject("Lounge_Lighting");
            Light moon = AddLight(lights.transform, "Moonlight", LightType.Directional, Hex("9BB9D5"), 0.55f);
            moon.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            moon.shadows = LightShadows.Soft;
            RenderSettings.sun = moon;
            Light lamp = AddLight(lights.transform, "Warm_TableLamp", LightType.Point, Hex("FFC17B"), 1.9f);
            lamp.transform.position = lampMarker ? lampMarker.position : floor.center + new Vector3(-2.5f, 1.8f, 0.4f);
            lamp.range = 5.5f;
            lamp.shadows = LightShadows.Soft;
            lamp.shadowBias = 0.03f;
            if (!lampMarker) Debug.LogWarning("SLOP CITY: Optional Light_TableLamp marker absent; using a room-relative warm light.");
            Light roomFill = AddLight(lights.transform, "Warm_InteriorFill", LightType.Point, Hex("FFE4BD"), 0.65f);
            roomFill.transform.position = new Vector3(floor.center.x, floor.max.y + 2.7f, floor.center.z);
            roomFill.range = 7f;
            roomFill.shadows = LightShadows.None;
        }

        private static Light AddLight(Transform parent, string name, LightType type, Color color, float intensity)
        {
            GameObject source = new GameObject(name);
            source.transform.SetParent(parent, false);
            Light light = source.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            return light;
        }

        private static SlopCityHud CreateHud()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!font) throw new InvalidOperationException("Unity's LegacyRuntime.ttf font was not available.");
            GameObject overlay = new GameObject("SlopCity_Overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = overlay.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;

            CreateText(overlay.transform, font, "Brand", "SLOP CITY", 20, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(38f, -32f), new Vector2(310f, 31f), Hex("E8E3CF"));
            CreateText(overlay.transform, font, "Subtitle", "HOOD RICH RECORDS / FIRST PERSON", 10, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(39f, -62f), new Vector2(340f, 24f), Hex("CCDB8A"));
            CreateText(overlay.transform, font, "VisorIdentity", "1984 DRH   /   VR LINK   /   2026", 11, TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(620f, 26f), new Color(0.8f, 0.92f, 0.7f, 0.8f));
            Text state = CreateText(overlay.transform, font, "State", "DANIEL / SEATED AT HOME", 10, TextAnchor.UpperRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-38f, -38f), new Vector2(330f, 26f), Hex("D6C6A3"));

            GameObject bottom = new GameObject("Controls_Backdrop", typeof(RectTransform), typeof(Image));
            bottom.transform.SetParent(overlay.transform, false);
            RectTransform bottomRect = bottom.GetComponent<RectTransform>();
            bottomRect.anchorMin = new Vector2(0f, 0f);
            bottomRect.anchorMax = new Vector2(1f, 0f);
            bottomRect.pivot = new Vector2(0.5f, 0f);
            bottomRect.offsetMin = Vector2.zero;
            bottomRect.offsetMax = new Vector2(0f, 92f);
            Image background = bottom.GetComponent<Image>();
            background.color = new Color(0.05f, 0.065f, 0.05f, 0.44f);
            background.raycastTarget = false;

            Text prompt = CreateText(overlay.transform, font, "Prompt", "[E] GET UP FROM THE COUCH", 16, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 58f), new Vector2(1380f, 38f), Hex("D5ED83"));
            Text controls = CreateText(overlay.transform, font, "Controls", "CLICK TO LOOK AROUND     E  INTERACT     R  COUCH RESPAWN", 11, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(1460f, 30f), Hex("B0B69E"));
            Text reticle = CreateText(overlay.transform, font, "Reticle", "+", 16, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 24f), new Color(0.84f, 0.93f, 0.51f, 0.7f));
            reticle.enabled = false;
            SlopCityHud hud = overlay.AddComponent<SlopCityHud>();
            hud.Configure(state, prompt, controls, reticle);
            return hud;
        }

        private static Text CreateText(Transform parent, Font font, string name, string content, int size,
            TextAnchor alignment, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 dimensions, Color color)
        {
            GameObject label = new GameObject(name, typeof(RectTransform), typeof(Text));
            label.transform.SetParent(parent, false);
            RectTransform rect = label.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            Text text = label.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = size >= 18 ? FontStyle.Bold : FontStyle.Normal;
            text.text = content;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void AssignStandardMaterials(GameObject lounge, Shader standard)
        {
            var cache = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (Renderer renderer in lounge.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    string originalName = source ? source.name : "Default_Concrete";
                    if (!cache.TryGetValue(originalName, out Material material))
                    {
                        string fileName = SanitizeName(originalName) + "_" + Hash128.Compute(originalName).ToString().Substring(0, 8) + ".mat";
                        string path = MaterialsFolder + "/" + fileName;
                        material = AssetDatabase.LoadAssetAtPath<Material>(path);
                        bool create = !material;
                        if (create) material = new Material(standard);
                        else material.shader = standard;
                        material.name = Path.GetFileNameWithoutExtension(path);
                        Color baseColor = source && source.HasProperty("_Color") ? source.GetColor("_Color") : Hex("666B5B");
                        if (source && source.HasProperty("_BaseColor")) baseColor = source.GetColor("_BaseColor");
                        Color emission = source && source.HasProperty("_EmissionColor") ? source.GetColor("_EmissionColor") : Color.black;
                        float metallic = 0f, smoothness = 0.2f;
                        foreach (PaletteEntry entry in Palette)
                        {
                            if (!MaterialNameMatches(originalName, entry.Name)) continue;
                            baseColor = Hex(entry.HexColor);
                            metallic = entry.Metallic;
                            smoothness = entry.Smoothness;
                            emission = baseColor * entry.Emission;
                            break;
                        }
                        material.color = baseColor;
                        material.SetFloat("_Metallic", metallic);
                        material.SetFloat("_Glossiness", smoothness);
                        Texture texture = source ? source.mainTexture : null;
                        material.mainTexture = texture;
                        material.SetColor("_EmissionColor", emission);
                        bool isEmissive = emission.maxColorComponent > 0f;
                        // StandardShaderGUI uses this flag when revalidating a saved
                        // material. Leaving EmissiveIsBlack set removes _EMISSION on
                        // reload even if its HDR colour and keyword were assigned.
                        // None enables visible self-emission without requesting GI baking.
                        material.globalIlluminationFlags = isEmissive
                            ? MaterialGlobalIlluminationFlags.None
                            : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                        if (isEmissive) material.EnableKeyword("_EMISSION");
                        else material.DisableKeyword("_EMISSION");
                        if (create) AssetDatabase.CreateAsset(material, path);
                        else EditorUtility.SetDirty(material);
                        cache.Add(originalName, material);
                    }
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static string SanitizeName(string source)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) source = source.Replace(invalid, '_');
            // Keep generated material assets portable to later Windows builds.
            foreach (char invalid in "<>:\"/\\|?*") source = source.Replace(invalid, '_');
            return source;
        }

        internal static bool MaterialNameMatches(string importedName, string authoredName)
        {
            // FBX import changes Blender's " | " separators into " _ ". Normalize
            // both sides, while retaining whole-word boundaries and clone suffixes.
            string imported = NormalizeMaterialName(importedName);
            string authored = NormalizeMaterialName(authoredName);
            return imported == authored || imported.StartsWith(authored + " ", StringComparison.Ordinal);
        }

        private static string NormalizeMaterialName(string name)
        {
            var normalized = new StringBuilder(name.Length);
            bool separator = false;
            foreach (char character in name)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    separator = normalized.Length > 0;
                    continue;
                }
                if (separator) normalized.Append(' ');
                normalized.Append(char.ToLowerInvariant(character));
                separator = false;
            }
            return normalized.ToString();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Color Hex(string hex)
        {
            Color color;
            return ColorUtility.TryParseHtmlString("#" + hex, out color) ? color : Color.white;
        }

        private readonly struct PaletteEntry
        {
            public readonly string Name;
            public readonly string HexColor;
            public readonly float Metallic;
            public readonly float Smoothness;
            public readonly float Emission;
            public PaletteEntry(string name, string hex, float metallic, float smoothness, float emission = 0f)
            {
                Name = name;
                HexColor = hex;
                Metallic = metallic;
                Smoothness = smoothness;
                Emission = emission;
            }
        }
    }
}
#endif
