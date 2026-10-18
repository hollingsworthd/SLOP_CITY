#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Classic visual recovery evidence from the production scene and actual adult observer; based on October 10 reference footage.</summary>
    [InitializeOnLoad]
    public static class SlopCityClassicVisualCheck
    {
        private const string Active = "SlopCity.ClassicVisual.Active", Started = "SlopCity.ClassicVisual.Started";
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Receipt report;
        private static SlopCityFirstPerson player;
        private static CharacterController capsule;
        private static Camera camera;
        private static SlopCityLuminousCity luminousProfile;
        private static bool started, finishing;
        private static int lastFrame = -1;
        private static double enteredAt;
        private const string DefaultPrefix = "classic-recovery-20261014";
        private static string Prefix => SessionState.GetString("SlopCity.ClassicVisual.Prefix", DefaultPrefix);
        private static string Folder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports");

        static SlopCityClassicVisualCheck()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Active, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (state == PlayModeStateChange.EnteredEditMode && !finishing) Finish(new InvalidOperationException("Visual capture Play Mode ended early."));
            };
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (!SessionState.GetBool(Active, false) || report == null) return;
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    if (message.Contains("Search index") || message.Contains("SearchIndexer") || (stack ?? "").Contains("UnityEditor.Search.SearchDatabase")) report.editorDiagnostics.Add(message + "\n" + stack);
                    else report.runtimeErrors.Add(message + "\n" + stack);
                }
                if (type == LogType.Warning) report.warnings.Add(message);
            };
        }

        // Dedicated clone, graphics enabled, StandaloneOSX; omit -quit (runner exits itself).
        // Optional -slopCityVisualPrefix NAME sets report/image filenames without changing source.
        // Optional -slopCityBakeMetricReflections bakes final moved city probes in this isolated clone.
        public static void Run()
        {
            if (!Application.isBatchMode || !Application.persistentDataPath.Contains("SlopCityValidation"))
                throw new InvalidOperationException("Visual restoration capture requires an isolated SlopCityValidation cloned project/save identity.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start outside Play Mode.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics-enabled Unity is required; omit -nographics.");
            string[] args = Environment.GetCommandLineArgs();
            int arg = Array.IndexOf(args, "-slopCityVisualPrefix");
            string prefix = arg >= 0 && arg + 1 < args.Length ? args[arg + 1] : DefaultPrefix;
            if (prefix.Any(character => !(char.IsLetterOrDigit(character) || character == '-' || character == '.')))
                throw new ArgumentException("Visual prefix may contain letters, digits, dots and hyphens only.");
            SessionState.SetString("SlopCity.ClassicVisual.Prefix", prefix);
            SessionState.SetString(Started, DateTime.UtcNow.ToString("O"));
            SessionState.SetString("SlopCity.ClassicVisual.PreBatchUV", "");
            Material skySource = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/SlopCityGenerated/CityOvercastSky.mat");
            SessionState.SetString("SlopCity.ClassicVisual.AuthoredSky", JsonUtility.ToJson(new SkyOrigin
            { assetPath = AssetDatabase.GetAssetPath(skySource), exposure = skySource && skySource.HasProperty("_Exposure") ? skySource.GetFloat("_Exposure") : -1,
                tint = skySource && skySource.HasProperty("_Tint") ? skySource.GetColor("_Tint") : Color.clear }));
            SessionState.SetBool(Active, true); report = NewReceipt(); Write();
            try
            {
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                // No network or decoder requests during architectural review. Authored screens retain their scene materials.
                foreach (SlopCityTikTokSync sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None)) sync.enabled = false;
                foreach (SlopCityCityMedia media in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None)) media.enabled = false;
                foreach (SlopCityMediaPlayer media in Object.FindObjectsByType<SlopCityMediaPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) media.enabled = false;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }

        private static Receipt NewReceipt() => new Receipt
        {
            startedAtUtc = SessionState.GetString(Started, DateTime.UtcNow.ToString("O")),
            unityVersion = Application.unityVersion, scenePath = SlopCitySceneBuilder.ScenePath,
            isolatedDataPath = Application.persistentDataPath, graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
            graphicsName = SystemInfo.graphicsDeviceName, shaderLevel = SystemInfo.graphicsShaderLevel,
            renderTargetCount = SystemInfo.supportedRenderTargetCount, hdrTargets = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf),
            colorSpace = QualitySettings.activeColorSpace.ToString(),
            metricReflectionBakeRequested = Environment.GetCommandLineArgs().Contains("-slopCityBakeMetricReflections")
        };

        private static void Update()
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (report == null) report = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(report.startedAtUtc).ToUniversalTime()).TotalMinutes > 8) throw new TimeoutException("Visual capture exceeded eight minutes.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                if (!started)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < 1) return;
                    started = true; Work.Push(Exercise());
                }
                while (Work.Count > 0)
                {
                    IEnumerator current = Work.Peek();
                    if (!current.MoveNext()) { Work.Pop(); continue; }
                    if (current.Current is IEnumerator nested) { Work.Push(nested); continue; }
                    return;
                }
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static IEnumerator Exercise()
        {
            AudioListener.volume = 0; Application.runInBackground = true;
            player = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            Check(player && player.Sandbox, "Saved production scene initializes its actual first-person player.");
            capsule = player.GetComponent<CharacterController>(); camera = player.ViewCamera;
            Check(capsule && camera, "Player has its actual capsule and camera.");
            report.playerHeight = capsule.height; report.playerRadius = capsule.radius;
            report.eyeHeight = new SerializedObject(player).FindProperty("standingEyeHeight").floatValue;
            Check(Mathf.Abs(report.eyeHeight - 1.62f) < .001f && Mathf.Abs(capsule.height - 1.72f) < .001f
                && (player.transform.lossyScale - Vector3.one).sqrMagnitude < .00001f, "Observer retains normal 1.72m body and 1.62m adult eye height.");
            report.productionCallbackRendererCount = SlopCityWebGeometryBuildProcessor.ProtectedRendererCount;
            Check(report.productionCallbackRendererCount > 0, "Production Play Mode callback prepared metric city before batching.");
            // Controller startup has already installed production streaming/utilities/crowds. Only autonomous input is frozen.
            player.enabled = false;
            // Freeze user input while allowing real city/crowd updates. Clear only pause state; media remains disabled/suspended.
            FieldInfo pausedField = typeof(SlopCityFirstPerson).GetField("paused", BindingFlags.Instance | BindingFlags.NonPublic);
            if (pausedField == null) throw new InvalidOperationException("Fixture cannot enable normal crowd updates without the production pause state.");
            pausedField.SetValue(player, false);
            report.playerPaused = player.IsPaused;
            Check(!report.playerPaused, "Input-frozen fixture leaves the real city and crowd simulation unpaused.");
            Time.timeScale = 1;
            for (int frame = 0; frame < 12; frame++) yield return null;
            if (report.metricReflectionBakeRequested)
            {
                Write();
                SlopCityMetricReflectionBake.BakeFinalScene(player.gameObject.scene);
                report.metricReflectionBakeCompleted = true;
                for (int frame = 0; frame < 8; frame++) yield return null;
            }
            // Fast batch frames can represent only milliseconds of simulation. Let the
            // actual unpaused people walk for eight game seconds before framing a view.
            float settlingStart = Time.time;
            int settlingFrame = Time.frameCount;
            while (Time.time < settlingStart + 8f) yield return null;
            report.initialCrowdSettlingGameSeconds = Time.time - settlingStart;
            report.initialCrowdSettlingFrames = Time.frameCount - settlingFrame;
            Inventory();
            MeasureLayout();
            MeasureLuminousState();
            MeasureStreetAtmosphere();
            MeasureNightSky();
            RecordCheck(Vector3.Distance(new Vector3(RenderSettings.fogColor.r, RenderSettings.fogColor.g, RenderSettings.fogColor.b),
                new Vector3(.32156864f, .39607844f, .45882353f)) < .0001f && Mathf.Abs(RenderSettings.fogDensity - .0032f) < .00001f,
                "Reference blue-gray fog matches the original saved scene.");
            SlopCityCameraFinish classicFinish = camera.GetComponent<SlopCityCameraFinish>();
            RecordCheck(classicFinish && Mathf.Abs(classicFinish.Exposure - 1.04f) < .0001f
                && Mathf.Abs(classicFinish.ContactStrength - 1.65f) < .0001f && Mathf.Abs(classicFinish.BloomStrength - .08f) < .0001f
                && Mathf.Abs(classicFinish.ReflectionStrength - .32f) < .0001f && Mathf.Abs(classicFinish.HeightHaze - .0055f) < .0001f,
                "Native camera uses the identified original artistic profile with retained allocation safety.");
            RecordCheck(luminousProfile.ConcreteMaterialCount == 0 && SlopCityLuminousCity.EmissionMultiplier == 1f,
                "Authored concrete palette and original sign/window radiance are retained.");
            yield return View("avenue", SlopCityDenseLayout.StreetPoint(new Vector3(20, -.08f, -10)),
                SlopCityDenseLayout.StreetPoint(new Vector3(20, 3.2f, 40)), "21-megacity-avenue.png", 67);
            Vector3 promenade = SlopCityDenseLayout.StreetPoint(new Vector3(20, .02f, -10));
            promenade.x -= SlopCityDenseLayout.SharedLaneWidth * .5f + SlopCityDenseLayout.FrontageSidewalkWidth * .5f;
            yield return View("promenade", promenade, promenade + new Vector3(0, 1.6f, 24), "22-megacity-promenade.png", 67);
            yield return View("market", SlopCityDenseLayout.StreetPoint(new Vector3(40, .02f, -12)),
                SlopCityDenseLayout.StreetPoint(new Vector3(40, 2f, 8)), "23-megacity-market.png", 67);
            SlopCityDistrict garden = Object.FindObjectsByType<SlopCityDistrict>(FindObjectsSortMode.None).First(item => item.Index == 2);
            Renderer water = garden.GetComponentsInChildren<Renderer>(true).First(item => item.name == "Canal_Water");
            yield return View("water-garden", garden.transform.TransformPoint(new Vector3(-7, .025f, 1.8f)),
                water.bounds.center + new Vector3(3, .15f, 0), "Canal Gardens: physical shallow water and authored optics", 67);
            SlopCityBuildingWorld world = SlopCityBuildingWorld.Ensure(player);
            yield return View("home-exit", new Vector3(5.2f, .025f, 0),
                new Vector3(13, 3.4f, 0), "Actual home exit: moved occupied tower encloses featured portrait", 67);
            yield return View("home-alley-south", new Vector3(8, .025f, 0),
                new Vector3(8, 2.2f, -16), "Actual route turns south through the enclosed home alley", 67);
            yield return View("reference-crossing", SlopCityDenseLayout.StreetPoint(new Vector3(20, -.08f, 15)),
                SlopCityDenseLayout.StreetPoint(new Vector3(35, 4f, 30)), "TikTok reference-30.png: crossing and varied blue-gray modular facades", 67);
            Vector3 towerObserver = SlopCityDenseLayout.StreetPoint(new Vector3(20, -.08f, 5));
            yield return View("reference-towers", towerObserver,
                towerObserver + new Vector3(-6, 12f, 35), "TikTok reference-20.png: upward facade variety and cyan/rose/gold signs", 67);
            yield return View("rain-pools-a", promenade, promenade + new Vector3(0, .1f, 7),
                "Actual rain shader at a fixed supported street camera; compare with rain-pools-b", 67);
            float rainSampleStart = Time.time;
            while (Time.time - rainSampleStart < .9f) yield return null;
            yield return View("rain-pools-b", promenade, promenade + new Vector3(0, .1f, 7),
                "Same street camera after time advances; shader animation plus normal scene simulation", 67);
            SlopCityBuildingInterior building = world.Buildings.Where(item => item.IsTower && item.BuildingId.StartsWith("City_Skyscraper_", StringComparison.Ordinal))
                .OrderBy(item => Vector3.Distance(item.EntranceFeet, new Vector3(0, 0, 0))).First();
            yield return View("near-facade", building.EntranceFeet - building.transform.forward * 3f,
                building.transform.position + Vector3.up * 7f, "Full-size floor-aligned facade: " + building.BuildingId, 67);
            Check(report.frames.All(frame => frame.fullNativeFinish), "All representative images retained supported full native finish without fallback.");
        }

        private static IEnumerator StreetWallViews(SlopCityBuildingWorld world)
        {
            var districts = new HashSet<int>();
            foreach (SlopCityBuildingInterior building in world.Buildings
                .Where(item => item.BuildingId.StartsWith("City_Skyscraper_", StringComparison.Ordinal))
                .OrderBy(item => item.GetComponentInParent<SlopCityDistrict>().Index == 4 ? 0 : 1))
            {
                SlopCityDistrict district = building.GetComponentInParent<SlopCityDistrict>();
                if (!district || districts.Contains(district.Index)) continue;
                Vector2 size = building.GetFloorSize(0);
                bool captured = false;
                for (int side = 0; side < 4 && !captured; side++)
                foreach (float fraction in new[] { 0f, -.25f, .25f })
                {
                    if (captured) break;
                    bool acrossX = side < 2; float sign = side % 2 == 0 ? -1 : 1;
                    Vector3 hint = building.transform.TransformPoint(acrossX
                        ? new Vector3(sign * (size.x * .5f + 1f), 0, size.y * fraction)
                        : new Vector3(size.x * fraction, 0, sign * (size.y * .5f + 1f)));
                    hint.y = .025f;
                    if (!Supported(hint, out Vector3 feet, out _)) continue;
                    Vector3 axis = acrossX ? building.transform.right : building.transform.forward;
                    Vector3 eye = feet + Vector3.up * report.eyeHeight;
                    if (!PhysicalStreetWall(eye, axis, out RaycastHit left, out SlopCityBuildingInterior leftOwner)
                        || !PhysicalStreetWall(eye, -axis, out RaycastHit right, out SlopCityBuildingInterior rightOwner)
                        || leftOwner == rightOwner) continue;
                    SlopCityDistrict leftDistrict = leftOwner.GetComponentInParent<SlopCityDistrict>();
                    SlopCityDistrict rightDistrict = rightOwner.GetComponentInParent<SlopCityDistrict>();
                    // Both walls must belong to occupied internal district frontages;
                    // an outer skyline passage facing the edge of the world does not show the requested street enclosure.
                    if (!leftDistrict || !rightDistrict || leftDistrict == rightDistrict) continue;
                    float width = left.distance + right.distance;
                    if (width < 2f || width > 14f) continue;
                    Vector3 along = Vector3.Cross(Vector3.up, axis).normalized;
                    if (Vector3.Dot(along, world.transform.position - feet) < 0) along = -along;
                    int aligned = 0, samples = 0;
                    for (float offset = -3f; offset <= 3.01f; offset += .5f)
                    {
                        Vector3 upper = feet + along * offset + Vector3.up * 4.5f; samples++;
                        if (PhysicalStreetWall(upper, axis, out _, out _) && PhysicalStreetWall(upper, -axis, out _, out _)) aligned++;
                    }
                    report.streetWalls.Add(new StreetWall { district = district.Index, feet = feet, along = along,
                        leftBuilding = leftOwner.BuildingId, rightBuilding = rightOwner.BuildingId,
                        wallToWallWidth = width, upperWallSamples = samples, opposedUpperWallSamples = aligned });
                    yield return View("streetwall-" + district.Index, feet, feet + along * 18f + Vector3.up * 4.5f,
                        "User zero-setback row reference /Users/daniel/Desktop/images.jpeg; actual opposing occupied facades", 67);
                    districts.Add(district.Index); captured = true;
                }
                if (districts.Count == 2) break;
            }
            Check(districts.Count == 2, "Two different districts provide supported adult-eye views between independently ray-measured physical street walls.");
        }

        private static bool PhysicalStreetWall(Vector3 origin, Vector3 direction, out RaycastHit wall, out SlopCityBuildingInterior owner)
        {
            foreach (RaycastHit hit in Physics.RaycastAll(origin, direction, 14f, ~0, QueryTriggerInteraction.Ignore).OrderBy(item => item.distance))
            {
                if (!Relevant(hit.collider) || Vector3.Dot(hit.normal, -direction) < .7f) continue;
                SlopCityBuildingInterior building = hit.collider.GetComponentInParent<SlopCityBuildingInterior>();
                Transform shell = building ? building.transform.Find("Runtime_Hollow_Building_Shell") : null;
                if (!shell || !hit.collider.transform.IsChildOf(shell)) continue;
                wall = hit; owner = building; return true;
            }
            wall = default; owner = null; return false;
        }

        private static IEnumerator View(string label, Vector3 hint, Vector3 target, string reference, float fov)
        {
            Vector3 feet = FindSupported(hint);
            player.TeleportTo(feet); Physics.SyncTransforms();
            camera.fieldOfView = fov; camera.transform.LookAt(target);
            // Allow production atmosphere/crowd/streaming updates and normal camera renders, not a single pre-update scene snapshot.
            int firstFrame = Time.frameCount;
            for (int frame = 0; frame < 16; frame++) yield return null;
            Check(Supported(feet, out Vector3 settled, out string support) && Vector3.Distance(settled, feet) < .05f,
                label + " has real solid ground and adult capsule clearance after scene updates.");
            Check(Mathf.Abs(camera.transform.position.y - feet.y - 1.62f) < .001f, label + " captures actual standing eye height.");
            var evidence = new Frame { label = label, comparisonReference = reference, requestedFeet = hint, feet = feet, support = support,
                position = camera.transform.position, rotation = camera.transform.rotation, target = target, fieldOfView = fov, simulationTime = Time.time,
                framesSettled = Time.frameCount - firstFrame, path = Path.Combine(Folder, Prefix + "-" + label + ".png") };
            Capture(evidence); report.frames.Add(evidence); Write();
        }

        private static Vector3 FindSupported(Vector3 hint)
        {
            if (Supported(hint, out Vector3 feet, out _)) return feet;
            // Bounded alternative on the same local street/venue, retaining the requested look target. Record exact displacement.
            for (float radius = .5f; radius <= 5f; radius += .5f)
                for (int angle = 0; angle < 16; angle++)
                {
                    float theta = angle * Mathf.PI / 8;
                    Vector3 candidate = hint + new Vector3(Mathf.Sin(theta) * radius, 0, Mathf.Cos(theta) * radius);
                    if (Supported(candidate, out feet, out _)) return feet;
                }
            throw new InvalidOperationException("No supported adult observer within five metres of requested view: " + hint);
        }

        private static bool Relevant(Collider item) => item && item.enabled && !item.isTrigger && item != capsule && !item.transform.IsChildOf(player.transform);
        private static bool Supported(Vector3 hint, out Vector3 feet, out string support)
        {
            feet = hint; support = "";
            RaycastHit[] hits = Physics.RaycastAll(hint + Vector3.up * .5f, Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => Relevant(hit.collider) && hit.normal.y > .7f).OrderBy(hit => hit.distance).ToArray();
            if (hits.Length == 0 || Mathf.Abs(hits[0].point.y - hint.y) > .30f) return false;
            feet = hits[0].point + Vector3.up * .025f; support = hits[0].collider.name;
            return !Physics.OverlapCapsule(feet + Vector3.up * (capsule.radius + .04f), feet + Vector3.up * (capsule.height - capsule.radius),
                capsule.radius * .94f, ~0, QueryTriggerInteraction.Ignore).Any(Relevant);
        }

        private static void Inventory()
        {
            luminousProfile = Object.FindFirstObjectByType<SlopCityLuminousCity>();
            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            report.lighting = new Lighting { ambientMode = RenderSettings.ambientMode.ToString(), ambientIntensity = RenderSettings.ambientIntensity,
                ambientLight = RenderSettings.ambientLight, ambientSky = RenderSettings.ambientSkyColor, ambientEquator = RenderSettings.ambientEquatorColor,
                ambientGround = RenderSettings.ambientGroundColor, fogEnabled = RenderSettings.fog, fogColor = RenderSettings.fogColor, fogDensity = RenderSettings.fogDensity,
                skybox = RenderSettings.skybox ? RenderSettings.skybox.name : "", defaultReflectionIntensity = RenderSettings.reflectionIntensity,
                shadowDistance = QualitySettings.shadowDistance, pixelLightCount = QualitySettings.pixelLightCount };
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                report.lights.Add(new LightInfo { name = light.name, hierarchy = Hierarchy(light.transform), type = light.type.ToString(), enabled = light.isActiveAndEnabled,
                    position = light.transform.position, rotation = light.transform.rotation, color = light.color, intensity = light.intensity, range = light.range,
                    shadows = light.shadows.ToString(), cullingMask = light.cullingMask });
            Material[] materials = renderers.SelectMany(item => item.sharedMaterials).Where(item => item).Distinct().ToArray();
            report.materialCount = materials.Length; report.texturedMaterialCount = materials.Count(item => item.mainTexture);
            report.emissiveMaterialCount = materials.Count(item => item.IsKeywordEnabled("_EMISSION"));
            foreach (Material material in materials)
                if (!material.shader || !material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
                    report.materialProblems.Add(material.name);
            RecordCheck(report.materialProblems.Count == 0, "Scene materials retain supported shaders.");
            foreach (SlopCityMetricFacade facade in Object.FindObjectsByType<SlopCityMetricFacade>(FindObjectsSortMode.None))
            {
                Renderer pane = facade.GetComponentsInChildren<Renderer>(true).FirstOrDefault(item => item.name == "Human_Scale_Window_Panes");
                Renderer source = facade.GetComponentsInChildren<Renderer>(true).FirstOrDefault(item => item.name == "CurtainWall_Panel" || item.name == "Masonry_WindowBays");
                Material authored = OriginalMaterial(source ? source.sharedMaterial : null);
                var entry = new Facade { building = facade.name, stories = facade.OccupiedStories,
                    source = Describe(authored), active = Describe(pane ? pane.sharedMaterial : null) };
                entry.authoredTextureRetained = authored && pane && pane.sharedMaterial
                    && authored.mainTexture && authored.mainTexture == pane.sharedMaterial.mainTexture;
                report.facades.Add(entry);
            }
            RecordCheck(report.facades.Count >= 51 && report.facades.All(item => item.authoredTextureRetained),
                "Every metric tower facade retains its authored textured window material.");
            Renderer asphalt = renderers.FirstOrDefault(item => item.name == "City_Asphalt");
            Material originalAsphalt = OriginalMaterial(asphalt ? asphalt.sharedMaterial : null);
            string snapshotJson = SessionState.GetString("SlopCity.ClassicVisual.PreBatchUV", "{}");
            UVSnapshot snapshot = JsonUtility.FromJson<UVSnapshot>(string.IsNullOrEmpty(snapshotJson) ? "{}" : snapshotJson);
            report.preBatchUVSamples = snapshot?.samples?.Count ?? 0;
            foreach (Renderer surface in renderers.Where(IsSurface))
            {
                var entry = new Surface { name = surface.name, path = Hierarchy(surface.transform), material = Describe(surface.sharedMaterial),
                    bounds = surface.bounds, uvUnitsPerMetre = UVDensity(surface), receivesShadows = surface.receiveShadows, uvMeasurementStage = "runtime readable mesh" };
                if (entry.uvUnitsPerMetre < 0)
                {
                    UVSample measured = snapshot?.samples?.FirstOrDefault(sample => sample.path == entry.path
                        && Vector3.Distance(sample.bounds.center, entry.bounds.center) < .001f
                        && Vector3.Distance(sample.bounds.size, entry.bounds.size) < .001f);
                    if (measured != null)
                    {
                        entry.uvUnitsPerMetre = measured.uvUnitsPerMetre; entry.uvMeasurementStage = "production geometry prepared; before Unity static batching";
                        entry.preBatchMesh = measured.mesh; entry.preBatchVertexCount = measured.vertexCount;
                    }
                    else entry.uvMeasurementStage = "unreadable runtime mesh; matching pre-batch measurement unavailable";
                }
                if (surface.name == "Asphalt_Shaft_Cutout" || surface.name == "Neighbourhood_Outer_Asphalt" || surface.name == "Neighbourhood_ServiceYard")
                {
                    entry.authoredAsphaltRetained = originalAsphalt && surface.sharedMaterial
                        && surface.sharedMaterial.mainTexture == originalAsphalt.mainTexture && surface.sharedMaterial.mainTexture
                        && TextureProperty(surface.sharedMaterial, "_BumpMap") && TextureProperty(surface.sharedMaterial, "_MetallicGlossMap")
                        && TextureProperty(surface.sharedMaterial, "_BumpMap") == TextureProperty(originalAsphalt, "_BumpMap")
                        && TextureProperty(surface.sharedMaterial, "_MetallicGlossMap") == TextureProperty(originalAsphalt, "_MetallicGlossMap");
                    RecordCheck(entry.authoredAsphaltRetained, "Production runtime road retains authored asphalt colour, normal and gloss maps.");
                }
                report.surfaces.Add(entry);
            }
            RecordCheck(report.surfaces.Any(item => item.name == "Asphalt_Shaft_Cutout"), "Runtime road with actual elevator openings exists.");
            foreach (Surface surface in report.surfaces)
                RecordCheck(Mathf.Abs(surface.uvUnitsPerMetre - 1f) < .05f && surface.receivesShadows,
                    surface.path + " uses world-metre UVs and receives authored lighting/shadows.");
            foreach (ReflectionProbe probe in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                report.probes.Add(new Probe { name = probe.name, position = probe.transform.position, bounds = probe.bounds,
                    mode = probe.mode.ToString(), texture = probe.texture ? probe.texture.name : "",
                    textureAssetPath = probe.texture ? AssetDatabase.GetAssetPath(probe.texture) : "", textureAvailable = probe.texture,
                    boxProjection = probe.boxProjection, importance = probe.importance });
            foreach (float x in new[] { -20f, 20f }) foreach (float z in new[] { -20f, 20f })
            {
                Vector3 point = SlopCityDenseLayout.StreetPoint(new Vector3(x, 3.3f, z));
                Probe nearest = report.probes.Where(item => item.name.StartsWith("Junction_Reflection_", StringComparison.Ordinal))
                    .OrderBy(item => Vector3.Distance(item.position, point)).FirstOrDefault();
                report.junctions.Add(new Junction { expected = point, probe = nearest?.name ?? "", distance = nearest == null ? -1 : Vector3.Distance(nearest.position, point),
                    supported = nearest != null && nearest.textureAvailable && Vector3.Distance(nearest.position, point) < .05f });
            }
            RecordCheck(report.junctions.All(item => item.supported), "Authored junction reflection textures follow all four dense street junctions.");
        }

        private static void MeasureLayout()
        {
            SlopCityBuildingWorld world = SlopCityBuildingWorld.Ensure(player);
            SlopCityBuildingInterior[] towers = world.Buildings.Where(item => item.IsTower).ToArray();
            var footprint = new Layout { columnPitch = SlopCityDenseLayout.ColumnPitch, rowPitch = SlopCityDenseLayout.RowPitch,
                buildingCount = world.Buildings.Count, towerCount = towers.Length,
                occupiedStories = world.Buildings.Sum(item => item.OccupiedFloorCount), stops = world.Buildings.Sum(item => item.FloorCount),
                baselineV0112GroundArea = 332f * 285f };
            Check(footprint.buildingCount == 59 && footprint.towerCount == 52 && footprint.occupiedStories == 1259 && footprint.stops == 1311,
                "Compact appearance preserves all 59 buildings, 1259 occupied stories and 1311 stops.");
            foreach (SlopCityBuildingInterior tower in towers)
            {
                Vector2 size = tower.GetFloorSize(0); footprint.totalTowerFootprintArea += size.x * size.y;
                Vector3[] corners = { new Vector3(-size.x * .5f, 0, -size.y * .5f), new Vector3(size.x * .5f, 0, -size.y * .5f),
                    new Vector3(-size.x * .5f, 0, size.y * .5f), new Vector3(size.x * .5f, 0, size.y * .5f) };
                foreach (Vector3 corner in corners)
                {
                    Vector3 point = tower.transform.TransformPoint(corner);
                    if (!footprint.hasOccupiedBounds) { footprint.occupiedTowerBounds = new Bounds(point, Vector3.zero); footprint.hasOccupiedBounds = true; }
                    else footprint.occupiedTowerBounds.Encapsulate(point);
                }
            }
            Renderer[] ground = world.GetComponentsInChildren<Renderer>(true).Where(item => item.enabled && item.gameObject.activeInHierarchy
                && (item.name == "Asphalt_Shaft_Cutout" || item.name == "Neighbourhood_Outer_Asphalt")).ToArray();
            Check(ground.Length > 0, "Visual layout measurement uses actual rendered road support geometry.");
            footprint.supportedGroundBounds = ground[0].bounds;
            foreach (Renderer surface in ground.Skip(1)) footprint.supportedGroundBounds.Encapsulate(surface.bounds);
            footprint.supportedGroundArea = footprint.supportedGroundBounds.size.x * footprint.supportedGroundBounds.size.z;
            footprint.groundAreaRatioToV0112 = footprint.supportedGroundArea / footprint.baselineV0112GroundArea;
            footprint.declaredGroundBounds = SlopCityDenseLayout.GroundBounds;
            RecordCheck(Mathf.Abs(footprint.supportedGroundBounds.size.x - footprint.declaredGroundBounds.size.x) < .05f
                && Mathf.Abs(footprint.supportedGroundBounds.size.z - footprint.declaredGroundBounds.size.z) < .05f
                && Mathf.Abs(footprint.supportedGroundBounds.center.x - footprint.declaredGroundBounds.center.x) < .05f
                && Mathf.Abs(footprint.supportedGroundBounds.center.z - footprint.declaredGroundBounds.center.z) < .05f,
                "Measured final road envelope agrees with the current compact layout, retaining physical support rather than a ground-plane scaling shortcut.");
            report.layout = footprint;
        }

        private static void MeasureLuminousState()
        {
            SlopCityLuminousCity profile = Object.FindFirstObjectByType<SlopCityLuminousCity>();
            Check(profile && profile.Prepared, "The production shared luminous profile initializes on the final physical city.");
            report.luminous = new Luminous { sharedClones = profile.SharedCloneCount, emissiveMaterials = profile.EmissiveMaterialCount,
                wetMaterials = profile.WetMaterialCount, concreteMaterials = profile.ConcreteMaterialCount, requestedEmissionMultiplier = SlopCityLuminousCity.EmissionMultiplier };
            RecordCheck(profile.MaterialPairs.Select(item => item.source).Distinct().Count() == profile.SharedCloneCount
                && profile.MaterialPairs.Select(item => item.replacement).Distinct().Count() == profile.SharedCloneCount,
                "Luminous surfaces use one bounded shared replacement per source material.");
            foreach (SlopCityLuminousCity.MaterialPair pair in profile.MaterialPairs)
            {
                Check(pair.source && pair.replacement, "Luminous material pair preserves usable original and replacement references.");
                var state = new LuminousMaterial { source = Describe(pair.source), replacement = Describe(pair.replacement), emissive = pair.emissive, wet = pair.wet, concrete = pair.concrete };
                state.mapsRetained = pair.source.mainTexture == pair.replacement.mainTexture
                    && TextureProperty(pair.source, "_BumpMap") == TextureProperty(pair.replacement, "_BumpMap")
                    && TextureProperty(pair.source, "_MetallicGlossMap") == TextureProperty(pair.replacement, "_MetallicGlossMap")
                    && TextureProperty(pair.source, "_OcclusionMap") == TextureProperty(pair.replacement, "_OcclusionMap")
                    && pair.source.mainTextureScale == pair.replacement.mainTextureScale;
                RecordCheck(state.mapsRetained, pair.source.name + " retains authored maps and texture scale in its shared luminous material.");
                if (pair.emissive)
                {
                    Color source = state.source.emissionColor, replacement = state.replacement.emissionColor;
                    float sourcePeak = Mathf.Max(source.r, Mathf.Max(source.g, source.b));
                    state.measuredEmissionMultiplier = sourcePeak > .0001f ? Mathf.Max(replacement.r, Mathf.Max(replacement.g, replacement.b)) / sourcePeak : 0;
                    state.expectedEmissionMultiplier = SlopCityLuminousCity.EmissionScale(pair.source);
                    Vector3 expected = new Vector3(source.r, source.g, source.b) * state.expectedEmissionMultiplier;
                    Vector3 actual = new Vector3(replacement.r, replacement.g, replacement.b);
                    RecordCheck(Vector3.Distance(actual, expected) <= Mathf.Max(.0001f, expected.magnitude * .0001f),
                        pair.source.name + " has its selective source-based emission factor without changing its hue.");
                }
                if (pair.wet && pair.replacement.HasProperty("_WetFilmCoverage"))
                {
                    state.wetReflectionGain = pair.replacement.GetFloat("_WetReflectionGain");
                    RecordCheck(Mathf.Abs(state.replacement.wetFilmCoverage - (pair.replacement.GetFloat("_IsWater") > .5f ? 1f : SlopCityLuminousCity.WetFilmCoverage)) < .0001f
                        && Mathf.Abs(state.replacement.wetFilmSmoothness - SlopCityLuminousCity.WetFilmSmoothness) < .0001f
                        && Mathf.Abs(state.wetReflectionGain - SlopCityLuminousCity.WetReflectionGain) < .0001f,
                        pair.source.name + " carries the production wet-film profile with preserved authored texture maps.");
                }
                report.luminous.materials.Add(state);
            }
        }

        private static void MeasureNightSky()
        {
            SlopCityNightLighting profile = Object.FindFirstObjectByType<SlopCityNightLighting>();
            Check(profile && profile.OriginalSky && profile.EveningSky, "Production authored overcast profile retains an authored sky and one separate shared sky clone.");
            SkyOrigin origin = JsonUtility.FromJson<SkyOrigin>(SessionState.GetString("SlopCity.ClassicVisual.AuthoredSky", "{}"));
            report.nightSky = new NightSky { original = Describe(profile.OriginalSky), evening = Describe(profile.EveningSky), authored = origin,
                originalExposure = profile.OriginalSky.GetFloat("_Exposure"), eveningExposure = profile.EveningSky.GetFloat("_Exposure"),
                originalTint = profile.OriginalSky.GetColor("_Tint"), eveningTint = profile.EveningSky.GetColor("_Tint") };
            RecordCheck(profile.OriginalSky != profile.EveningSky && RenderSettings.skybox == profile.EveningSky
                && AssetDatabase.GetAssetPath(profile.OriginalSky) == origin.assetPath && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(profile.EveningSky)),
                "Recovered lighting uses the cloned runtime sky while retaining the actual authored material identity.");
            RecordCheck(Mathf.Abs(report.nightSky.originalExposure - origin.exposure) < .0001f
                && ((Vector4)report.nightSky.originalTint - (Vector4)origin.tint).sqrMagnitude < .000001f,
                "Night preparation leaves the authored sky exposure and tint unchanged from the fixture's pre-Play snapshot.");
            RecordCheck(Mathf.Abs(report.nightSky.eveningExposure - SlopCityNightLighting.SkyExposure) < .0001f,
                "Installed authored overcast sky uses the production authored exposure.");
            Light[] daylight = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(item => item.name == "BlueHour_Daylight" && item.type == LightType.Directional).ToArray();
            RecordCheck(daylight.Length == 1 && Mathf.Abs(daylight[0].intensity - SlopCityNightLighting.DaylightIntensity) < .0001f,
                "The actual authored daylight receives the production authored intensity.");
        }

        private static void MeasureStreetAtmosphere()
        {
            SlopCityStreetAtmosphere profile = Object.FindFirstObjectByType<SlopCityStreetAtmosphere>();
            Check(profile && profile.Prepared, "Production street utility history initializes on the actual occupied facade roots.");
            report.atmosphere = new Atmosphere { decoratedBuildings = profile.DecoratedBuildings, servicePlaques = profile.ServicePlaques,
                detailMeshCount = profile.DetailMeshCount, maximumWallProjection = SlopCityStreetAtmosphere.MaximumWallProjection };
            Transform[] stages = profile.GetComponentsInChildren<Transform>(true).Where(item => item.name == "Street_Wall_Utility_History").ToArray();
            RecordCheck(stages.Length == profile.DecoratedBuildings && stages.Length >= 51
                && profile.ServicePlaques == stages.Length && profile.DetailMeshCount == stages.Length * 3,
                "Shared street detail has exactly three bounded mesh batches and one service plaque per decorated metric tower.");
            foreach (Transform stage in stages)
            {
                Transform building = stage.parent;
                SlopCityMetricFacade facade = building ? building.GetComponent<SlopCityMetricFacade>() : null;
                MeshFilter core = building ? building.Find("Structural_Core")?.GetComponent<MeshFilter>() : null;
                Check(facade && facade.Prepared && core && core.sharedMesh, "Street detail belongs to an existing prepared physical facade.");
                Check(stage.GetComponentsInChildren<Collider>(true).Length == 0 && stage.GetComponentsInChildren<Light>(true).Length == 0
                    && stage.GetComponentsInChildren<SlopCityCityScreen>(true).Length == 0 && stage.GetComponentsInChildren<SlopCityInteractable>(true).Length == 0,
                    building.name + " utility history adds no street collision, lights, screen replacement or unsupported gameplay operation.");
                float front = -core.sharedMesh.bounds.size.z * .5f;
                var detail = new AtmosphereBuilding { building = building.name, front = front };
                foreach (Renderer renderer in stage.GetComponentsInChildren<Renderer>(true))
                {
                    Bounds bounds = renderer.bounds;
                    foreach (float x in new[] { bounds.min.x, bounds.max.x })
                    foreach (float y in new[] { bounds.min.y, bounds.max.y })
                    foreach (float z in new[] { bounds.min.z, bounds.max.z })
                    {
                        Vector3 vertex = building.InverseTransformPoint(new Vector3(x, y, z));
                        detail.measuredWallProjection = Mathf.Max(detail.measuredWallProjection, front - vertex.z);
                        detail.lowestDetailHeight = Mathf.Min(detail.lowestDetailHeight, vertex.y);
                    }
                }
                report.atmosphere.buildings.Add(detail);
                RecordCheck(detail.measuredWallProjection <= SlopCityStreetAtmosphere.MaximumWallProjection + .01f && detail.lowestDetailHeight >= 3.25f,
                    building.name + " installed detail remains above adult headroom and within the bounded front-wall projection.");
            }
        }

        private static bool IsSurface(Renderer item) => item.enabled && item.gameObject.activeInHierarchy
            && (item.name == "Asphalt_Shaft_Cutout" || item.name == "Neighbourhood_Outer_Asphalt" || item.name == "Neighbourhood_ServiceYard" || item.name == "Neighbourhood_Sidewalk"
                || item.name.StartsWith("Low_Curb_", StringComparison.Ordinal) || item.name.StartsWith("Ring_Footway_", StringComparison.Ordinal)
                || item.name == "Outer_Entrance_Walk" || item.name == "Paved_Access");

        // Called after the actual production geometry callback, while generated pavement is still readable.
        // Read-only, session-scoped measurements; no flags, meshes or materials are changed by this callback.
        public static void CapturePreBatchUV(Scene scene)
        {
            if (!SessionState.GetBool(Active, false)) return;
            var snapshot = new UVSnapshot();
            foreach (Renderer surface in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).Where(IsSurface))
            {
                MeshFilter filter = surface.GetComponent<MeshFilter>();
                snapshot.samples.Add(new UVSample { path = Hierarchy(surface.transform), bounds = surface.bounds, uvUnitsPerMetre = UVDensity(surface),
                    mesh = filter && filter.sharedMesh ? filter.sharedMesh.name : "", vertexCount = filter && filter.sharedMesh ? filter.sharedMesh.vertexCount : 0 });
            }
            SessionState.SetString("SlopCity.ClassicVisual.PreBatchUV", JsonUtility.ToJson(snapshot));
        }

        private static Texture TextureProperty(Material material, string property) => material && material.HasProperty(property) ? material.GetTexture(property) : null;
        private static Material OriginalMaterial(Material material) => material && luminousProfile ? luminousProfile.GetSource(material) ?? material : material;
        private static MaterialInfo Describe(Material material)
        {
            string textureProperty = material && material.HasProperty("_MainTex") ? "_MainTex"
                : material && material.HasProperty("_Tex") ? "_Tex" : "";
            Texture main = string.IsNullOrEmpty(textureProperty) ? null : material.GetTexture(textureProperty);
            return new MaterialInfo
            {
            name = material ? material.name : "", assetPath = material ? AssetDatabase.GetAssetPath(material) : "", shader = material && material.shader ? material.shader.name : "",
            mainTexture = main ? main.name : "", mainTexturePath = main ? AssetDatabase.GetAssetPath(main) : "", mainTextureProperty = textureProperty,
            normalTexture = TextureProperty(material, "_BumpMap") ? TextureProperty(material, "_BumpMap").name : "",
            glossTexture = TextureProperty(material, "_MetallicGlossMap") ? TextureProperty(material, "_MetallicGlossMap").name : "",
            emissive = material && material.IsKeywordEnabled("_EMISSION"), textureScale = string.IsNullOrEmpty(textureProperty) ? Vector2.zero : material.GetTextureScale(textureProperty),
            emissionColor = material && material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black,
            glossiness = material && material.HasProperty("_Glossiness") ? material.GetFloat("_Glossiness") : -1,
            wetFilmCoverage = material && material.HasProperty("_WetFilmCoverage") ? material.GetFloat("_WetFilmCoverage") : -1,
            wetFilmSmoothness = material && material.HasProperty("_WetFilmSmoothness") ? material.GetFloat("_WetFilmSmoothness") : -1
            };
        }
        private static string Hierarchy(Transform item) => item.parent ? Hierarchy(item.parent) + "/" + item.name : item.name;
        private static float UVDensity(Renderer renderer)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>(); Mesh mesh = filter ? filter.sharedMesh : null;
            if (!mesh || !mesh.isReadable || mesh.uv.Length != mesh.vertexCount) return -1;
            Vector3[] vertices = mesh.vertices; Vector2[] uv = mesh.uv; int[] triangles = mesh.triangles; var ratios = new List<float>();
            int stride = Mathf.Max(3, (triangles.Length / 1800 / 3) * 3);
            for (int index = 0; index + 2 < triangles.Length; index += stride)
                for (int edge = 0; edge < 3; edge++)
                {
                    int a = triangles[index + edge], b = triangles[index + (edge + 1) % 3];
                    float length = Vector3.Distance(renderer.transform.TransformPoint(vertices[a]), renderer.transform.TransformPoint(vertices[b]));
                    float textureLength = Vector2.Distance(uv[a], uv[b]);
                    if (length > .01f && textureLength > .0001f) ratios.Add(textureLength / length);
                }
            if (ratios.Count == 0) return -1; ratios.Sort(); return ratios[ratios.Count / 2];
        }

        private static bool ReadBool(SlopCityCameraFinish finish, string name) => (bool)(typeof(SlopCityCameraFinish)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(finish) ?? false);
        private static void Capture(Frame evidence)
        {
            SlopCityCameraFinish finish = camera.GetComponent<SlopCityCameraFinish>();
            Check(finish && finish.enabled, "Actual game camera finish remains enabled for " + evidence.label + ".");
            const int width = 1600, height = 1000;
            RenderTexture previous = RenderTexture.active, oldTarget = camera.targetTexture;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR);
            var display = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var png = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                if (!target.Create()) throw new InvalidOperationException("Visual target allocation failed.");
                if (!display.Create()) throw new InvalidOperationException("Display readback target allocation failed.");
                camera.targetTexture = target; camera.Render();
                // Match the original realism capture: convert finished linear HDR to display sRGB before PNG encoding.
                // Reading RGB24 directly from HDR encodes linear samples as display bytes and falsely darkens the image.
                bool previousSRGB = GL.sRGBWrite;
                try { GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear; Graphics.Blit(target, display); }
                finally { GL.sRGBWrite = previousSRGB; }
                RenderTexture.active = display;
                png.ReadPixels(new Rect(0, 0, width, height), 0, 0); png.Apply();
                Directory.CreateDirectory(Folder); File.WriteAllBytes(evidence.path, png.EncodeToPNG());
                Color[] pixels = png.GetPixels(); double sum = 0, square = 0; int bright = 0, clipped = 0, dark = 0;
                foreach (Color pixel in pixels)
                {
                    double luma = pixel.r * .2126 + pixel.g * .7152 + pixel.b * .0722; sum += luma; square += luma * luma;
                    if (luma > .9) bright++; if (Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)) >= .999f) clipped++; if (luma < .005) dark++;
                }
                evidence.meanDisplayLuminance = sum / pixels.Length; evidence.displayVariance = square / pixels.Length - evidence.meanDisplayLuminance * evidence.meanDisplayLuminance;
                evidence.brightPixelFraction = bright / (double)pixels.Length; evidence.clippedChannelFraction = clipped / (double)pixels.Length;
                evidence.nearBlackPixelFraction = dark / (double)pixels.Length;
                evidence.foreground = MeasureRegion(pixels, width, height, new Rect(0, 0, 1, .25f));
                evidence.centralFacades = MeasureRegion(pixels, width, height, new Rect(.1f, .4f, .8f, .4f));
                evidence.cameraProfile = ReadCameraProfile(finish);
                evidence.forwardClearance = CapsuleClearance(evidence.feet, camera.transform.forward);
                evidence.leftClearance = CapsuleClearance(evidence.feet, -camera.transform.right);
                evidence.rightClearance = CapsuleClearance(evidence.feet, camera.transform.right);
                evidence.crowd = MeasureVisibleCrowd();
                evidence.renderingPath = camera.actualRenderingPath.ToString(); evidence.hdr = camera.allowHDR;
                evidence.lightweightFinish = ReadBool(finish, "lightweightFinish"); evidence.allocationWarning = ReadBool(finish, "allocationWarning");
                Shader shader = Shader.Find("Hidden/SlopCity/CameraFinish"); evidence.finishShaderSupported = shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader);
                evidence.fullNativeFinish = finish.enabled && evidence.finishShaderSupported && !evidence.lightweightFinish && !evidence.allocationWarning
                    && camera.actualRenderingPath == RenderingPath.DeferredShading;
                Check(evidence.meanDisplayLuminance > .008 && evidence.displayVariance > .0001, evidence.label + " produced a nonblank rendered image.");
            }
            finally { camera.targetTexture = oldTarget; RenderTexture.active = previous; target.Release(); display.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(display); Object.DestroyImmediate(png); }
        }

        private static RegionLuminance MeasureRegion(Color[] pixels, int width, int height, Rect normalized)
        {
            var measured = new RegionLuminance { viewportRect = normalized };
            double sum = 0, square = 0; int dark = 0, bright = 0;
            int x0 = Mathf.FloorToInt(normalized.xMin * width), x1 = Mathf.CeilToInt(normalized.xMax * width);
            int y0 = Mathf.FloorToInt(normalized.yMin * height), y1 = Mathf.CeilToInt(normalized.yMax * height);
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
            {
                Color pixel = pixels[y * width + x];
                double luma = pixel.r * .2126 + pixel.g * .7152 + pixel.b * .0722;
                sum += luma; square += luma * luma; measured.samples++;
                if (luma < .1) dark++; if (luma > .8) bright++;
            }
            measured.meanDisplayLuminance = sum / measured.samples;
            measured.displayVariance = square / measured.samples - measured.meanDisplayLuminance * measured.meanDisplayLuminance;
            measured.darkPixelFraction = dark / (double)measured.samples;
            measured.brightPixelFraction = bright / (double)measured.samples;
            return measured;
        }

        private static CameraProfile ReadCameraProfile(SlopCityCameraFinish finish)
        {
            var serialized = new SerializedObject(finish);
            return new CameraProfile { exposure = ReadProfile(serialized, "exposure"), bloomStrength = ReadProfile(serialized, "bloomStrength"),
                reflectionStrength = ReadProfile(serialized, "reflectionStrength"), contactStrength = ReadProfile(serialized, "contactStrength"),
                heightHaze = ReadProfile(serialized, "heightHaze") };
        }
        private static float ReadProfile(SerializedObject target, string name) => target.FindProperty(name)?.floatValue ?? -1;
        private static float CapsuleClearance(Vector3 feet, Vector3 direction)
        {
            direction.y = 0; if (direction.sqrMagnitude < .001f) return -1;
            RaycastHit[] hits = Physics.CapsuleCastAll(feet + Vector3.up * (capsule.radius + .04f), feet + Vector3.up * (capsule.height - capsule.radius),
                capsule.radius * .94f, direction.normalized, 10f, ~0, QueryTriggerInteraction.Ignore).Where(item => Relevant(item.collider)).ToArray();
            return hits.Length == 0 ? 10f : hits.Min(item => item.distance);
        }
        private static CrowdVisibility MeasureVisibleCrowd()
        {
            // Crowd bodies are drawn with instancing; measure actual simulation positions, never move people for a picture.
            SlopCityCrowd crowd = Object.FindFirstObjectByType<SlopCityCrowd>();
            var visibility = new CrowdVisibility { population = crowd ? crowd.PopulationCount : 0 };
            if (!crowd) return visibility;
            visibility.positionsAvailable = true; visibility.simulationTime = crowd.SimulationTime;
            visibility.moving = crowd.MovingCount; visibility.instancingSupported = SystemInfo.supportsInstancing;
            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(camera);
            for (int index = 0; index < crowd.PopulationCount; index++)
            {
                SlopCityNpcSnapshot person = crowd.Snapshot(index); Vector3 feet = person.position;
                float distance = Vector3.Distance(camera.transform.position, feet);
                if (distance < 20f) visibility.withinTwentyMetres++;
                if (distance <= 135f && GeometryUtility.TestPlanesAABB(frustum,
                    new Bounds(feet + Vector3.up * person.height * .5f, new Vector3(.9f, person.height + .15f, .9f)))) visibility.eligibleNativeDrawBodies++;
                Vector3 head = feet + Vector3.up * 1.3f, viewport = camera.WorldToViewportPoint(head);
                if (viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) continue;
                visibility.inFrustum++;
                Vector3 delta = head - camera.transform.position;
                bool blocked = Physics.RaycastAll(camera.transform.position, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore)
                    .Any(hit => Relevant(hit.collider));
                if (!blocked) visibility.unoccluded++;
            }
            return visibility;
        }

        private static void RecordCheck(bool condition, string message) { report.checks.Add(message); if (!condition) report.errors.Add(message); }
        private static void Check(bool condition, string message) { report.checks.Add(message); if (!condition) throw new InvalidOperationException(message); }
        private static void Write() { Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, Prefix + "-check.json"), JsonUtility.ToJson(report, true)); }
        private static void Finish(Exception error)
        {
            if (finishing) return; finishing = true;
            if (report == null) report = NewReceipt();
            if (error != null) report.errors.Add(error.ToString());
            report.finishedAtUtc = DateTime.UtcNow.ToString("O"); report.passed = report.errors.Count == 0 && report.runtimeErrors.Count == 0 && report.frames.Count == 11;
            Write(); SessionState.SetBool(Active, false);
            Debug.Log("SLOP_CITY_CLASSIC_VISUAL_COMPLETE " + report.passed + " " + Path.Combine(Folder, Prefix + "-check.json"));
            EditorApplication.Exit(report.passed ? 0 : 1);
        }

        [Serializable] private sealed class Receipt
        {
            public bool passed, hdrTargets, metricReflectionBakeRequested, metricReflectionBakeCompleted, playerPaused;
            public string startedAtUtc, finishedAtUtc, unityVersion, scenePath, isolatedDataPath, graphicsDevice, graphicsName, colorSpace;
            public string scope = "Isolated graphics-enabled native Editor Play Mode with saved scene and production geometry callback. Actual 1.62m standing player camera with authored full native finish; player input is frozen while its pause state is cleared so real city/crowd simulation updates, real supported capsule positions and sixteen settled production frames per view. Avenue/promenade/market and reference crossing/tower compositions adapt the original capture positions to full-size occupied buildings; exact requested and final transforms recorded. Media components/network sync disabled only in fixture, retaining authored world screens; audio muted. Optional explicit bake flag writes current metric probe cubemaps only in the isolated clone and records resulting texture asset paths. PNG readback converts finished linear HDR to display sRGB without adjusting game lighting/exposure. Unreadable statically batched pavement meshes use exact hierarchy-and-world-bounds matched UV measurements captured by a validation-only scene callback immediately after production geometry preparation and before static batching; runtime-generated streets are measured directly. Material/UV/probe facts support visual review, not a claim of subjective art quality. Does not validate standalone/WebGL appearance, physical phones, live media, every floor or long-duration memory. No scene/source asset/save from the normal user identity is modified.";
            public Lighting lighting; public List<LightInfo> lights = new List<LightInfo>();
            public Layout layout;
            public Luminous luminous;
            public Atmosphere atmosphere;
            public NightSky nightSky;
            public int preBatchUVSamples;
            public int initialCrowdSettlingFrames;
            public float initialCrowdSettlingGameSeconds;
            public int shaderLevel, renderTargetCount, productionCallbackRendererCount, materialCount, texturedMaterialCount, emissiveMaterialCount;
            public float playerHeight, playerRadius, eyeHeight;
            public List<string> checks = new List<string>(), errors = new List<string>(), runtimeErrors = new List<string>(), editorDiagnostics = new List<string>(), warnings = new List<string>(), materialProblems = new List<string>();
            public List<Frame> frames = new List<Frame>(); public List<Facade> facades = new List<Facade>(); public List<Surface> surfaces = new List<Surface>();
            public List<Probe> probes = new List<Probe>(); public List<Junction> junctions = new List<Junction>();
            public List<StreetWall> streetWalls = new List<StreetWall>();
        }
        [Serializable] private sealed class Lighting { public string ambientMode, skybox; public float ambientIntensity, fogDensity, defaultReflectionIntensity, shadowDistance; public Color ambientLight, ambientSky, ambientEquator, ambientGround, fogColor; public bool fogEnabled; public int pixelLightCount; }
        [Serializable] private sealed class LightInfo { public string name, hierarchy, type, shadows; public Vector3 position; public Quaternion rotation; public Color color; public float intensity, range; public int cullingMask; public bool enabled; }
        [Serializable] private sealed class UVSnapshot { public List<UVSample> samples = new List<UVSample>(); }
        [Serializable] private sealed class UVSample { public string path, mesh; public Bounds bounds; public float uvUnitsPerMetre; public int vertexCount; }
        [Serializable] private sealed class MaterialInfo { public string name, assetPath, shader, mainTexture, mainTexturePath, mainTextureProperty, normalTexture, glossTexture; public bool emissive; public Vector2 textureScale; public Color emissionColor; public float glossiness, wetFilmCoverage, wetFilmSmoothness; }
        [Serializable] private sealed class Facade { public string building; public int stories; public MaterialInfo source, active; public bool authoredTextureRetained; }
        [Serializable] private sealed class Surface { public string name, path, uvMeasurementStage, preBatchMesh; public int preBatchVertexCount; public MaterialInfo material; public Bounds bounds; public float uvUnitsPerMetre; public bool authoredAsphaltRetained, receivesShadows; }
        [Serializable] private sealed class Probe { public string name, mode, texture, textureAssetPath; public Vector3 position; public Bounds bounds; public bool textureAvailable, boxProjection; public int importance; }
        [Serializable] private sealed class Junction { public Vector3 expected; public string probe; public float distance; public bool supported; }
        [Serializable] private sealed class StreetWall { public int district, upperWallSamples, opposedUpperWallSamples; public string leftBuilding, rightBuilding; public Vector3 feet, along; public float wallToWallWidth; }
        [Serializable] private sealed class Frame
        {
            public string label, comparisonReference, path, support, renderingPath;
            public Vector3 requestedFeet, feet, position, target; public Quaternion rotation; public float fieldOfView, simulationTime; public int framesSettled;
            public bool fullNativeFinish, lightweightFinish, allocationWarning, finishShaderSupported, hdr;
            public double meanDisplayLuminance, displayVariance, brightPixelFraction, clippedChannelFraction, nearBlackPixelFraction;
            public float forwardClearance, leftClearance, rightClearance;
            public CameraProfile cameraProfile;
            public CrowdVisibility crowd;
            public RegionLuminance foreground, centralFacades;
        }
        [Serializable] private sealed class RegionLuminance { public Rect viewportRect; public int samples; public double meanDisplayLuminance, displayVariance, darkPixelFraction, brightPixelFraction; }
        [Serializable] private sealed class CameraProfile { public float exposure, bloomStrength, reflectionStrength, contactStrength, heightHaze; }
        [Serializable] private sealed class SkyOrigin { public string assetPath; public float exposure; public Color tint; }
        [Serializable] private sealed class NightSky { public MaterialInfo original, evening; public SkyOrigin authored; public float originalExposure, eveningExposure; public Color originalTint, eveningTint; }
        [Serializable] private sealed class Atmosphere { public int decoratedBuildings, servicePlaques, detailMeshCount; public float maximumWallProjection; public List<AtmosphereBuilding> buildings = new List<AtmosphereBuilding>(); }
        [Serializable] private sealed class AtmosphereBuilding { public string building; public float front, measuredWallProjection; public float lowestDetailHeight = float.MaxValue; }
        [Serializable] private sealed class CrowdVisibility { public bool positionsAvailable, instancingSupported; public int population, moving, withinTwentyMetres, inFrustum, unoccluded, eligibleNativeDrawBodies; public float simulationTime; }
        [Serializable] private sealed class Layout
        {
            public int buildingCount, towerCount, occupiedStories, stops;
            public bool hasOccupiedBounds;
            public Bounds occupiedTowerBounds, supportedGroundBounds, declaredGroundBounds;
            public float columnPitch, rowPitch, supportedGroundArea, baselineV0112GroundArea, groundAreaRatioToV0112, totalTowerFootprintArea;
        }
        [Serializable] private sealed class Luminous { public int sharedClones, emissiveMaterials, wetMaterials, concreteMaterials; public float requestedEmissionMultiplier; public List<LuminousMaterial> materials = new List<LuminousMaterial>(); }
        [Serializable] private sealed class LuminousMaterial { public MaterialInfo source, replacement; public bool emissive, wet, concrete, mapsRetained; public float expectedEmissionMultiplier, measuredEmissionMultiplier, wetReflectionGain; }
    }

    // One order after SlopCityWebGeometryBuildProcessor. This measures actual production geometry before Unity combines meshes.
    public sealed class SlopCityClassicPreBatchUVSnapshot : IProcessSceneWithReport
    {
        public int callbackOrder => int.MinValue + 1;
        public void OnProcessScene(Scene scene, BuildReport report) => SlopCityClassicVisualCheck.CapturePreBatchUV(scene);
    }
}
#endif
