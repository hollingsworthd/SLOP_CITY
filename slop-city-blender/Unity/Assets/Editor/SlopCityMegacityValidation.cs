#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Persisted CJK signage, city crowd paths and observable population behavior.</summary>
    [InitializeOnLoad]
    public static class SlopCityMegacityValidation
    {
        public const string ReportPath = "Builds/Reports/megacity-validation.json";
        public const string SmokeReportPath = "Builds/Reports/megacity-playback-check.json";
        private const string ActiveKey = "SlopCity.MegacitySmoke.Active";
        private const string StartedKey = "SlopCity.MegacitySmoke.Started";
        private const string SaveKey = "SlopCity.Sandbox.Progress.v1";
        private const string BackupKey = "SlopCity.MegacitySmoke.ProgressBackup";
        private const string BackupExistsKey = "SlopCity.MegacitySmoke.ProgressExisted";
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static Report smoke;
        private static double enteredAt;

        static SlopCityMegacityValidation()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                if (change == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (change == PlayModeStateChange.EnteredEditMode) Finish(new Exception("Play Mode ended before megacity checks completed."));
            };
            Application.logMessageReceived += (message, trace, type) =>
            {
                if (!SessionState.GetBool(ActiveKey, false)
                    || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
                if (smoke == null) smoke = NewReport(SlopCitySceneBuilder.ScenePath);
                string diagnostic = message + "\n" + trace;
                bool editorSearchStartup = message.StartsWith("ArgumentOutOfRangeException:", StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(trace) && trace.Contains("UnityEditor.Search.SearchDatabase")
                    && trace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup") && !trace.Contains("SlopCity.");
                if (editorSearchStartup) smoke.editorDiagnostics.Add(diagnostic);
                else smoke.runtimeErrors.Add(diagnostic);
            };
        }

        public static void ValidateSavedScene()
        {
            Scene scene = EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
            Report report = Validate(scene);
            Save(ReportPath, report);
            report.ThrowIfInvalid();
            Debug.Log("SLOP_CITY_MEGACITY_VALIDATION_SUCCEEDED " + report.checks + " checks; " + ReportPath);
        }

        public static Report Validate(Scene scene)
        {
            var report = NewReport(scene.path);
            report.scope = "Saved Japanese signage assets, licensed font provenance, physical crowd route graph and instancing assets. Runtime behavior is checked separately.";
            report.Check(scene.IsValid() && scene.isLoaded, "Megacity validation requires a loaded scene.");
            if (!scene.IsValid() || !scene.isLoaded) return report;
            ValidateInstancingVariants(report);
            Transform[] nodes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            ValidateSignage(report, nodes);
            ValidateFont(report);
            SlopCityCrowd[] crowds = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SlopCityCrowd>(true)).ToArray();
            report.Check(crowds.Length == 1, "The saved scene must contain exactly one shared instanced population controller.");
            if (crowds.Length == 1) ValidateCrowd(report, crowds[0], scene);
            report.passed = report.errors.Count == 0;
            return report;
        }

        private static void ValidateInstancingVariants(Report report)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            report.Check(assets.Length > 0, "GraphicsSettings must be available to validate population shader variants.");
            if (assets.Length == 0) return;
            SerializedProperty property = new SerializedObject(assets[0]).FindProperty("m_InstancingStripping");
            // Unity serializes this internal enum as an integer: StripUnused=0, StripAll=1, KeepAll=2.
            report.Check(property != null && property.intValue == 2,
                "GraphicsSettings instancing stripping must be KeepAll (2), so DrawMeshInstanced crowd variants survive player builds.");
        }

        private static void ValidateSignage(Report report, Transform[] nodes)
        {
            SignAtlas manifest = ValidateSignManifest(report);
            Transform[] districts = nodes.Where(item => item.name.StartsWith("Japan_District_", StringComparison.Ordinal)).ToArray();
            Transform[] signs = nodes.Where(item => item.name.StartsWith("Japan_Sign_", StringComparison.Ordinal)).ToArray();
            Transform[] billboards = nodes.Where(item => item.name.StartsWith("Japan_VideoBillboard_", StringComparison.Ordinal)).ToArray();
            report.japaneseSignAssemblies = signs.Length;
            report.japaneseVideoBillboards = billboards.Length;
            report.Check(districts.Length >= 9, "Japanese streetscape must cover all nine city districts.");
            report.Check(signs.Length >= 180, "The saved megacity requires at least 180 Japanese sign assemblies.");
            report.Check(billboards.Length >= 27, "The Japanese streetscape requires at least 27 additional native video billboards.");
            var atlasPaths = new HashSet<string>();
            foreach (Transform district in districts)
            {
                report.Check(signs.Count(item => item.IsChildOf(district)) >= 18, "District lacks its Japanese sign density: " + district.name);
                report.Check(billboards.Count(item => item.IsChildOf(district)) >= 3, "District lacks three native Japanese video billboards: " + district.name);
            }
            foreach (Transform sign in signs)
            {
                Renderer[] renderers = sign.GetComponentsInChildren<Renderer>(true);
                bool litArtwork = false;
                foreach (Material material in renderers.SelectMany(renderer => renderer.sharedMaterials).Where(material => material).Distinct())
                {
                    if (!material.HasProperty("_EmissionMap") || !material.GetTexture("_EmissionMap")) continue;
                    Texture atlas = material.GetTexture("_EmissionMap");
                    string path = AssetDatabase.GetAssetPath(atlas);
                    if (string.IsNullOrEmpty(path) || !path.Contains("Japan")) continue;
                    atlasPaths.Add(path);
                    litArtwork |= material.shader && material.shader.isSupported && material.IsKeywordEnabled("_EMISSION")
                        && material.GetColor("_EmissionColor").maxColorComponent > .1f
                        && (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0;
                }
                report.Check(litArtwork, "Japanese sign has no persisted, supported emissive artwork: " + sign.name);
                report.Check(renderers.Any(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy),
                    "Japanese sign has no enabled renderer: " + sign.name);
            }
            foreach (Transform billboard in billboards)
            {
                Transform[] anchors = billboard.GetComponentsInChildren<Transform>(true)
                    .Where(item => item.name.StartsWith("SlopCityMediaScreenAnchor_Channel", StringComparison.Ordinal)).ToArray();
                report.Check(anchors.Length == 1, "Each Japanese video billboard must expose exactly one native screen anchor: " + billboard.name);
            }
            report.japaneseAtlasTextures = atlasPaths.Count;
            report.Check(atlasPaths.Count > 0, "Japanese signage must reference packaged atlas textures rather than a machine-installed font.");
            foreach (string path in atlasPaths)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                report.Check(importer && importer.mipmapEnabled && importer.sRGBTexture && texture && texture.width >= 512 && texture.height >= 512,
                    "Japanese glyph atlas must retain resolution, sRGB import and mipmaps: " + path);
                ValidateAtlasPixels(report, path, manifest);
            }
            TextMesh[] runtimeText = nodes.Select(item => item.GetComponent<TextMesh>()).Where(item => item && item.text.Any(IsJapanese)).ToArray();
            foreach (TextMesh label in runtimeText)
                report.Check(label.font && AssetDatabase.GetAssetPath(label.font).StartsWith("Assets/Art/Fonts/", StringComparison.Ordinal),
                    "Runtime Japanese label relies on a missing/system font: " + label.name);
        }

        private static SignAtlas ValidateSignManifest(Report report)
        {
            const string path = "Assets/Art/JapanSigns/sign-manifest.json";
            report.Check(File.Exists(path), "Japanese sign artwork requires a saved label/UV manifest.");
            if (!File.Exists(path)) return null;
            SignAtlas manifest = JsonUtility.FromJson<SignAtlas>(File.ReadAllText(path));
            report.Check(manifest != null && manifest.schema == 1 && manifest.labels != null && manifest.labels.Length >= 32
                && manifest.entries != null && manifest.entries.Length >= 64 && manifest.atlasSize != null && manifest.atlasSize.Length == 2,
                "Japanese sign manifest must preserve at least 32 labels in both horizontal and vertical atlas layouts.");
            if (manifest == null || manifest.labels == null || manifest.entries == null || manifest.atlasSize == null || manifest.atlasSize.Length != 2) return null;
            report.Check(manifest.missingGlyphs != null && manifest.missingGlyphs.Length == 0,
                "Japanese atlas generation reported missing glyphs.");
            const string fontPath = "Assets/Art/Fonts/NotoSansCJKjp-Bold.otf";
            report.Check(File.Exists(fontPath), "Japanese sign manifest requires its bundled source font.");
            if (!File.Exists(fontPath)) return manifest;
            byte[] font = File.ReadAllBytes(fontPath);
            using (SHA256 hash = SHA256.Create())
                report.Check(BitConverter.ToString(hash.ComputeHash(font)).Replace("-", "").ToLowerInvariant() == manifest.fontSha256,
                    "Japanese sign atlas was generated with a different source font than the bundled licensed font.");
            foreach (char character in string.Concat(manifest.labels).Where(IsJapanese).Distinct())
                report.Check(FontContains(font, character), "Japanese sign text uses an absent source glyph: " + character);
            report.signLabels = manifest.labels.Distinct().Count();
            foreach (string label in manifest.labels)
                report.Check(label.Any(IsJapanese) && manifest.entries.Any(entry => entry.label == label && entry.orientation == "horizontal")
                    && manifest.entries.Any(entry => entry.label == label && entry.orientation == "vertical"),
                    "Japanese sign label requires real CJK glyphs and both intended orientations: " + label);
            foreach (SignEntry entry in manifest.entries)
            {
                int[] rect = entry.rectPixelsTopLeft; float[] uv = entry.uv;
                bool validRect = rect != null && rect.Length == 4 && rect[0] >= 0 && rect[1] >= 0 && rect[2] >= 64 && rect[3] >= 64
                    && rect[0] + rect[2] <= manifest.atlasSize[0] && rect[1] + rect[3] <= manifest.atlasSize[1];
                bool validUV = uv != null && uv.Length == 4 && uv.All(value => !float.IsNaN(value) && !float.IsInfinity(value))
                    && uv[0] >= 0 && uv[1] >= 0 && uv[2] > 0 && uv[3] > 0 && uv[0] + uv[2] <= 1 && uv[1] + uv[3] <= 1;
                report.Check(validRect && validUV, "Japanese atlas entry has an out-of-bounds crop: " + entry.id);
                if (validRect && validUV)
                {
                    float centerX = (rect[0] + rect[2] * .5f) / manifest.atlasSize[0];
                    float centerY = 1f - (rect[1] + rect[3] * .5f) / manifest.atlasSize[1];
                    report.Check(Mathf.Abs(uv[0] + uv[2] * .5f - centerX) < .001f
                        && Mathf.Abs(uv[1] + uv[3] * .5f - centerY) < .001f,
                        "Japanese atlas crop is vertically flipped or selects the wrong sign: " + entry.id);
                }
            }
            return manifest;
        }

        private static void ValidateAtlasPixels(Report report, string path, SignAtlas manifest)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                bool decoded = ImageConversion.LoadImage(image, File.ReadAllBytes(path));
                report.Check(decoded, "Japanese atlas cannot be decoded from its saved file: " + path);
                if (!decoded) return;
                Color32[] pixels = image.GetPixels32();
                int visible = 0, dark = 0; double sum = 0, squared = 0;
                foreach (Color32 pixel in pixels)
                {
                    double value = Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)) / 255d;
                    if (pixel.a > 16 && value > .3d) visible++;
                    if (value < .10d || pixel.a < 16) dark++;
                    sum += value; squared += value * value;
                }
                double variance = squared / pixels.Length - Math.Pow(sum / pixels.Length, 2);
                report.Check(visible > pixels.Length / 100 && dark > pixels.Length / 100 && variance > .002d,
                    "Japanese glyph atlas is empty or uniformly filled: " + path);
                if (manifest != null && Path.GetFileName(path) == manifest.emission)
                {
                    report.Check(image.width == manifest.atlasSize[0] && image.height == manifest.atlasSize[1],
                        "Japanese emission atlas dimensions differ from its authored UV manifest.");
                    foreach (SignEntry entry in manifest.entries)
                    {
                        int[] rect = entry.rectPixelsTopLeft;
                        if (rect == null || rect.Length != 4 || rect[0] < 0 || rect[1] < 0
                            || rect[0] + rect[2] > image.width || rect[1] + rect[3] > image.height) continue;
                        int lit = 0, blank = 0, count = 0;
                        for (int y = rect[1] + 8; y < rect[1] + rect[3] - 8; y += 4)
                            for (int x = rect[0] + 8; x < rect[0] + rect[2] - 8; x += 4)
                            {
                                Color32 pixel = pixels[(image.height - 1 - y) * image.width + x];
                                int luminance = Math.Max(pixel.r, Math.Max(pixel.g, pixel.b));
                                if (luminance > 90 && pixel.a > 16) lit++;
                                if (luminance < 40 || pixel.a < 16) blank++;
                                count++;
                            }
                        report.Check(lit > count / 100 && blank > count / 100,
                            "Japanese sign tile has no visible glyph contrast: " + entry.label + " / " + entry.orientation);
                    }
                }
            }
            finally { Object.DestroyImmediate(image); }
        }

        private static void ValidateFont(Report report)
        {
            const string folder = "Assets/Art/Fonts/";
            const string source = folder + "NotoSansCJKjp-Bold.otf";
            const string license = folder + "OFL-NotoSansCJK.txt";
            const string provenance = folder + "provenance.json";
            report.Check(File.Exists(source) && File.Exists(license) && File.Exists(provenance), "Japanese source font, redistribution license and provenance must be packaged together.");
            if (!File.Exists(source) || !File.Exists(license) || !File.Exists(provenance)) return;
            FontSource record = JsonUtility.FromJson<FontSource>(File.ReadAllText(provenance));
            report.Check(record != null && record.license == "SIL Open Font License 1.1"
                && record.downloadUrl.StartsWith("https://raw.githubusercontent.com/notofonts/noto-cjk/", StringComparison.Ordinal),
                "Japanese source font must retain its official source and open license provenance.");
            byte[] bytes = File.ReadAllBytes(source);
            using (SHA256 hash = SHA256.Create())
                report.Check(record != null && bytes.Length == record.bytes
                    && BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() == record.sha256,
                    "Japanese font bytes differ from the verified official source recorded in provenance.");
            report.Check(File.ReadAllText(license).Contains("SIL OPEN FONT LICENSE Version 1.1"), "Font redistribution license text is missing.");
            // The shipped signs are baked textures. Read the source font's Unicode cmap directly;
            // successful validation must not depend on Unity or the user's machine loading a font.
            foreach (char character in "東京都新宿駅未来電気居酒屋ラーメン地下鉄".Distinct())
                report.Check(FontContains(bytes, character), "Bundled Japanese source font lacks required glyph U+" + ((int)character).ToString("X4") + ".");
        }

        private static bool FontContains(byte[] bytes, int codePoint)
        {
            int tableCount = Read16(bytes, 4), cmap = -1;
            for (int index = 0; index < tableCount; index++)
            {
                int record = 12 + index * 16;
                if (Read32(bytes, record) == 0x636d6170) { cmap = checked((int)Read32(bytes, record + 8)); break; }
            }
            if (cmap < 0) return false;
            int encodings = Read16(bytes, cmap + 2);
            for (int index = 0; index < encodings; index++)
            {
                int record = cmap + 4 + index * 8, platform = Read16(bytes, record);
                if (platform != 0 && platform != 3) continue;
                int table = cmap + checked((int)Read32(bytes, record + 4)), format = Read16(bytes, table);
                if (format == 12)
                {
                    int groups = checked((int)Read32(bytes, table + 12));
                    for (int group = 0; group < groups; group++)
                    {
                        int at = table + 16 + group * 12;
                        uint first = Read32(bytes, at), last = Read32(bytes, at + 4);
                        if (codePoint >= first && codePoint <= last)
                            return Read32(bytes, at + 8) + codePoint - first != 0;
                    }
                }
                else if (format == 4 && codePoint <= 0xffff)
                {
                    int count = Read16(bytes, table + 6) / 2;
                    int endCodes = table + 14, starts = endCodes + count * 2 + 2;
                    int deltas = starts + count * 2, offsets = deltas + count * 2;
                    for (int segment = 0; segment < count; segment++)
                    {
                        int first = Read16(bytes, starts + segment * 2), last = Read16(bytes, endCodes + segment * 2);
                        if (codePoint < first || codePoint > last) continue;
                        int delta = Read16(bytes, deltas + segment * 2), location = offsets + segment * 2;
                        int range = Read16(bytes, location);
                        int glyph = range == 0 ? codePoint : Read16(bytes, location + range + (codePoint - first) * 2);
                        return glyph != 0 && ((glyph + delta) & 0xffff) != 0;
                    }
                }
            }
            return false;
        }
        private static int Read16(byte[] bytes, int offset)
        {
            if (offset < 0 || offset > bytes.Length - 2) throw new InvalidDataException("Truncated Japanese font table.");
            return (bytes[offset] << 8) | bytes[offset + 1];
        }
        private static uint Read32(byte[] bytes, int offset) => ((uint)Read16(bytes, offset) << 16) | (uint)Read16(bytes, offset + 2);

        private static bool IsJapanese(char value) => (value >= '\u3040' && value <= '\u30ff') || (value >= '\u4e00' && value <= '\u9fff');

        private static void ValidateCrowd(Report report, SlopCityCrowd crowd, Scene scene)
        {
            report.population = crowd.PopulationCount;
            report.initialMoving = crowd.MovingCount;
            report.Check(crowd.enabled && crowd.gameObject.activeInHierarchy && report.population >= 400,
                "The saved streets require at least 400 active residents in the shared population controller.");
            report.Check(report.initialMoving >= report.population / 2, "At least half of the initial population must be moving through the city.");
            var roles = new HashSet<SlopCityNpcRole>();
            var districts = new HashSet<int>();
            for (int index = 0; index < report.population; index++)
            {
                SlopCityNpcSnapshot person = crowd.Snapshot(index);
                report.Check(Finite(person.position) && !float.IsNaN(person.heading) && !float.IsInfinity(person.heading)
                    && person.height >= 1.4f && person.height <= 2.2f && !string.IsNullOrEmpty(person.displayName),
                    "Resident has an invalid saved body pose, height or name at index " + index + ".");
                roles.Add(person.role); districts.Add(person.districtIndex);
                report.Check(person.routeIndex >= 0 && person.routeIndex < crowd.Routes.Count
                    && person.nodeIndex >= 0 && person.nodeIndex < crowd.Routes[person.routeIndex].nodes.Length,
                    "Resident refers to an invalid route/node at index " + index + ".");
            }
            report.distinctRoles = roles.Count;
            report.populatedDistricts = districts.Count;
            report.Check(roles.Count >= 6, "The crowd requires all six everyday roles, including residents and security.");
            report.Check(Enumerable.Range(0, 9).All(districts.Contains) && districts.Contains(10) && districts.Contains(11),
                "Residents must populate every street district and both subway stations.");
            ValidateGraph(report, crowd, scene);
            ValidateCrowdAssets(report, crowd);
        }

        private static void ValidateGraph(Report report, SlopCityCrowd crowd, Scene scene)
        {
            Physics.SyncTransforms();
            var excluded = PlayerColliders(scene);
            report.routes = crowd.Routes.Count;
            report.Check(report.routes >= 11, "Crowd must retain pedestrian route graphs for all districts and both stations.");
            for (int routeIndex = 0; routeIndex < crowd.Routes.Count; routeIndex++)
            {
                SlopCityCrowdRoute route = crowd.Routes[routeIndex];
                report.Check(route != null && route.nodes != null && route.nodes.Length >= 2, "Crowd route has fewer than two usable nodes: " + routeIndex);
                if (route == null || route.nodes == null || route.nodes.Length < 2) continue;
                report.routeNodes += route.nodes.Length;
                var visited = new HashSet<int>(); var pending = new Queue<int>();
                pending.Enqueue(0); visited.Add(0);
                while (pending.Count > 0)
                {
                    int here = pending.Dequeue();
                    foreach (int next in route.nodes[here].neighbors ?? Array.Empty<int>())
                        if (next >= 0 && next < route.nodes.Length && visited.Add(next)) pending.Enqueue(next);
                }
                report.Check(visited.Count == route.nodes.Length, "Crowd route contains stranded disconnected nodes: " + route.label);
                for (int nodeIndex = 0; nodeIndex < route.nodes.Length; nodeIndex++)
                {
                    SlopCityCrowdNode node = route.nodes[nodeIndex];
                    report.Check(Finite(node.position) && ClearBody(node.position, excluded),
                        "Crowd route node overlaps final city collision: " + route.label + "/" + nodeIndex + " at " + node.position);
                    report.Check(Supported(node.position, excluded),
                        "Crowd route node is not supported by a walkable floor: " + route.label + "/" + nodeIndex);
                    int[] neighbors = node.neighbors ?? Array.Empty<int>();
                    report.Check(neighbors.Length > 0 && neighbors.Distinct().Count() == neighbors.Length,
                        "Crowd node has no onward route or duplicate links: " + route.label + "/" + nodeIndex);
                    foreach (int next in neighbors)
                    {
                        bool valid = next >= 0 && next < route.nodes.Length && next != nodeIndex;
                        report.Check(valid, "Crowd route has an invalid/self-linked neighbor: " + route.label + "/" + nodeIndex);
                        if (!valid) continue;
                        report.Check((route.nodes[next].neighbors ?? Array.Empty<int>()).Contains(nodeIndex),
                            "Crowd route edge must permit walking back: " + route.label + "/" + nodeIndex + " -> " + next);
                        if (next <= nodeIndex) continue;
                        report.routeEdges++;
                        Vector3 delta = route.nodes[next].position - node.position;
                        report.Check(delta.sqrMagnitude > .01f && Mathf.Abs(delta.y) <= .221f,
                            "Crowd route edge is zero length or steps through an unsupported height change: " + route.label);
                        if (delta.sqrMagnitude > .01f)
                        {
                            report.Check(!Physics.CapsuleCastAll(node.position + Vector3.up * .29f, node.position + Vector3.up * 1.62f,
                                .28f, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore).Any(hit => Relevant(hit.collider, excluded)),
                                "Crowd route edge cuts through final city collision: " + route.label + "/" + nodeIndex + " -> " + next);
                            int samples = Mathf.CeilToInt(delta.magnitude / .5f);
                            bool supported = true;
                            for (int sample = 1; sample < samples && supported; sample++)
                                supported = Supported(Vector3.Lerp(node.position, route.nodes[next].position, sample / (float)samples), excluded);
                            report.Check(supported, "Crowd route crosses a floor gap: " + route.label + "/" + nodeIndex + " -> " + next);
                        }
                    }
                }
            }
        }

        private static void ValidateCrowdAssets(Report report, SlopCityCrowd crowd)
        {
            SlopCityCrowdVisuals visuals = crowd.Visuals;
            report.Check(visuals != null && visuals.parts != null && visuals.parts.Length >= 20 && visuals.parts.All(mesh => mesh)
                && visuals.clothing.Length > 0 && visuals.skin.Length > 0 && visuals.hair.Length > 0
                && visuals.shoes && visuals.eyes && visuals.accessories && visuals.safety,
                "The persisted articulated human library must retain every anatomical, clothing and accessory part required for instanced drawing.");
            var meshes = new HashSet<Mesh>(); var materials = new HashSet<Material>();
            SerializedProperty property = new SerializedObject(crowd).GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (property.objectReferenceValue is Mesh mesh) meshes.Add(mesh);
                if (property.objectReferenceValue is Material material) materials.Add(material);
            }
            report.populationMeshAssets = meshes.Count; report.populationMaterialAssets = materials.Count;
            report.Check(meshes.Count > 0 && meshes.Sum(mesh => mesh.vertexCount) >= 1000,
                "Instanced people require persisted anatomical meshes rather than missing or primitive placeholders.");
            foreach (Mesh mesh in meshes)
                report.Check(AssetDatabase.Contains(mesh) && mesh.normals.Length == mesh.vertexCount && mesh.vertexCount > 0,
                    "Crowd mesh is transient, empty or lacks normal data: " + mesh.name);
            report.Check(materials.Count >= 3, "Population requires distinct persisted body/clothing materials.");
            foreach (Material material in materials)
                report.Check(AssetDatabase.Contains(material) && material.shader && material.shader.isSupported && material.enableInstancing,
                    "Crowd material cannot render persisted GPU instances: " + material.name);
        }

        // Dedicated batch Editor only; omit -quit and -nographics. Existing progression is restored on exit.
        public static void RunSmoke()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Megacity smoke requires a dedicated batch Editor.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start outside Play Mode.");
            SessionState.SetBool(BackupExistsKey, PlayerPrefs.HasKey(SaveKey));
            SessionState.SetString(BackupKey, PlayerPrefs.GetString(SaveKey, ""));
            SessionState.SetString(StartedKey, DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(ActiveKey, true);
            smoke = NewReport(SlopCitySceneBuilder.ScenePath);
            smoke.scope = "Saved scene in Play Mode: observed crowd movement, environmental clearance, game pause, conversation and cooldown. Visual realism remains a separate rendered review.";
            Save(SmokeReportPath, smoke);
            try
            {
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (smoke == null) smoke = NewReport(SlopCitySceneBuilder.ScenePath);
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(StartedKey, DateTime.UtcNow.ToString("O"))).ToUniversalTime()).TotalSeconds > 150d)
                    throw new TimeoutException("Megacity smoke exceeded 150 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (Work.Count == 0)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < .8d) return;
                    Work.Push(ExerciseCrowd());
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
            catch (Exception exception) { Finish(exception); }
        }

        private static IEnumerator ExerciseCrowd()
        {
            Application.runInBackground = true;
            SlopCityCrowd crowd = Object.FindFirstObjectByType<SlopCityCrowd>();
            SlopCityFirstPerson player = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            Require(crowd && player && crowd.PopulationCount >= 400, "Saved metropolis initializes at least 400 residents and its playable controller.");
            smoke.population = crowd.PopulationCount;
            player.SendMessage("OnApplicationFocus", true); player.SendMessage("OnApplicationPause", false);
            player.PlayOrResume();
            yield return Delay(.3d);
            var excluded = PlayerColliders(SceneManager.GetActiveScene());
            Vector3[] before = Snapshots(crowd);
            float firstTime = crowd.SimulationTime;
            yield return Until(() => crowd.SimulationTime >= firstTime + 1.25f, 18d, "Real Update frames did not advance the population simulation.");
            int moved = 0;
            for (int index = 0; index < crowd.PopulationCount; index++)
            {
                SlopCityNpcSnapshot person = crowd.Snapshot(index);
                if (Vector3.Distance(before[index], person.position) > .1f) moved++;
                Require(Finite(person.position) && ClearBody(person.position, excluded), "Resident stays finite and clear of city collision during simulation: " + index);
            }
            smoke.observedMoving = moved;
            Require(crowd.SimulationTime > firstTime + .3f && moved >= crowd.PopulationCount / 3,
                "Real Update frames move a substantial share of residents along their authored routes.");
            var input = player.GetComponent<SlopCityPlayerInput>();
            input.RequestPause();
            yield return Until(() => player.IsPaused, 4d, "Game pause input was not consumed.");
            yield return Delay(.2d);
            before = Snapshots(crowd); firstTime = crowd.SimulationTime;
            crowd.AdvanceSimulation(.5f);
            yield return Delay(.6d);
            Require(Math.Abs(crowd.SimulationTime - firstTime) < .0001f
                && before.Select((position, index) => Vector3.Distance(position, crowd.Snapshot(index).position)).All(distance => distance < .0001f),
                "Ordinary game pause holds all residents and rejects explicit simulation advancement.");
            input.RequestPause();
            yield return Until(() => !player.IsPaused, 4d, "Game resume input was not consumed.");
            yield return Delay(.3d);
            Require(crowd.SimulationTime > firstTime, "Resuming gameplay resumes the crowd simulation.");

            var testedRoles = new HashSet<SlopCityNpcRole>();
            var conversationPoses = new Dictionary<int, Vector3>();
            for (int index = 0; index < crowd.PopulationCount; index++)
            {
                SlopCityNpcSnapshot person = crowd.Snapshot(index);
                if (!testedRoles.Add(person.role)) continue;
                Vector3 observer = person.position + Vector3.forward * 1.2f;
                Require(crowd.TryTalk(index, observer, out string firstLine) && !string.IsNullOrWhiteSpace(firstLine)
                    && firstLine.StartsWith(person.displayName + " / " + person.role.ToString().ToUpperInvariant() + ": ", StringComparison.Ordinal),
                    "A resident in role " + person.role + " provides a real conversation response.");
                SlopCityNpcSnapshot speaking = crowd.Snapshot(index);
                Require(!speaking.moving && speaking.talkCooldown >= 11f,
                    "Conversation stops the selected resident and applies its talk cooldown.");
                Require(Mathf.Abs(Mathf.DeltaAngle(speaking.heading, 0f)) < 1f,
                    "The speaking resident turns to face the nearby observer.");
                Require(crowd.TryTalk(index, observer, out string repeatedLine) && !string.IsNullOrWhiteSpace(repeatedLine)
                    && repeatedLine != firstLine && crowd.Snapshot(index).talkCooldown <= speaking.talkCooldown + .01f,
                    "Immediate repeated conversation returns cooldown feedback without restarting the cooldown.");
                smoke.conversations.Add(person.role + ": " + firstLine);
                conversationPoses[index] = speaking.position;
            }
            Require(testedRoles.Count >= 6, "All six population roles support conversation.");
            firstTime = crowd.SimulationTime;
            yield return Until(() => crowd.SimulationTime > firstTime + .5f, 6d, "Population stalled during conversations.");
            Require(conversationPoses.All(pair => Vector3.Distance(pair.Value, crowd.Snapshot(pair.Key).position) < .001f
                && crowd.Snapshot(pair.Key).talkCooldown < 12f && crowd.Snapshot(pair.Key).talkCooldown > 10f),
                "Speaking residents remain stopped while their conversation cooldown elapses.");
            yield return Until(() => crowd.SimulationTime > firstTime + 6f, 35d, "Population stalled before conversation release.");
            Require(conversationPoses.Any(pair => Vector3.Distance(pair.Value, crowd.Snapshot(pair.Key).position) > .1f),
                "Residents return to walking after their conversation pause finishes.");
            yield return ExercisePlayerConversation(crowd, player, input, excluded);
            Require(!crowd.TryTalk(-1, Vector3.zero, out _) && !crowd.TryTalk(crowd.PopulationCount, Vector3.zero, out _),
                "Invalid resident indices are rejected without a runtime exception.");
            Require(smoke.runtimeErrors.Count == 0, "No unexpected runtime errors occurred during megacity checks.");
        }

        private static IEnumerator ExercisePlayerConversation(SlopCityCrowd crowd, SlopCityFirstPerson player,
            SlopCityPlayerInput input, HashSet<Collider> excluded)
        {
            Camera camera = player.ViewCamera;
            float sensitivity = new SerializedObject(player).FindProperty("mouseSensitivity").floatValue;
            int selected = -1;
            Vector3 observer = default;
            for (int index = 0; index < crowd.PopulationCount && selected < 0; index++)
            {
                SlopCityNpcSnapshot person = crowd.Snapshot(index);
                if (person.talkCooldown > 0 || person.moving || person.position.y < -1f) continue;
                for (int angle = 0; angle < 12; angle++)
                {
                    Vector3 offset = Quaternion.Euler(0, angle * 30f, 0) * Vector3.forward * 1.4f;
                    Vector3 candidate = person.position + offset;
                    if (!ClearBody(candidate, excluded) || !Supported(candidate, excluded)) continue;
                    Vector3 target = person.position + Vector3.up * person.height * .65f;
                    Vector3 eye = candidate + Vector3.up * 1.62f;
                    Vector3 delta = target - eye;
                    if (Physics.RaycastAll(eye, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore)
                        .Any(hit => Relevant(hit.collider, excluded))) continue;
                    if (!crowd.TryFindTarget(new Ray(eye, delta.normalized), delta.magnitude + .3f, out int hitIndex, out _) || hitIndex != index) continue;
                    selected = index; observer = candidate; break;
                }
            }
            Require(selected >= 0, "A resident has a supported, unobstructed player conversation approach within 1.4m.");
            SlopCityNpcSnapshot subject = crowd.Snapshot(selected);
            Vector3 direction = subject.position - observer;
            player.TeleportTo(observer, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
            Require(!player.IsSeated && player.GetComponent<CharacterController>().enabled,
                "The conversation fixture uses the standing player and real CharacterController.");
            double deadline = EditorApplication.timeSinceStartup + 5d;
            while (true)
            {
                subject = crowd.Snapshot(selected);
                Quaternion aim = Quaternion.LookRotation(subject.position + Vector3.up * subject.height * .65f - camera.transform.position);
                float yaw = Mathf.DeltaAngle(player.transform.eulerAngles.y, aim.eulerAngles.y);
                float pitch = Mathf.DeltaAngle(camera.transform.localEulerAngles.x, aim.eulerAngles.x);
                input.AddTouchLook(new Vector2(yaw, -pitch) / (.14f * sensitivity));
                yield return null;
                if (player.Sandbox.IsLookingAtNpc(camera) && crowd.TryFindTarget(new Ray(camera.transform.position, camera.transform.forward),
                    3.4f, out int hitIndex, out _) && hitIndex == selected) break;
                if (EditorApplication.timeSinceStartup >= deadline) throw new TimeoutException("Gameplay look input could not target the nearby resident.");
            }
            Require(crowd.Snapshot(selected).talkCooldown <= 0f, "The player targets a resident who is ready to talk.");
            int credits = player.Sandbox.Credits, contracts = player.Sandbox.CompletedContracts;
            input.RequestInteract();
            yield return Until(() => crowd.Snapshot(selected).talkCooldown > 10f, 4d,
                "Real E/interact input did not reach the targeted NPC through FirstPerson and Sandbox.");
            string feedback = typeof(SlopCityFirstPerson).GetField("feedback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(player) as string;
            Require(!string.IsNullOrEmpty(feedback) && feedback.StartsWith(subject.displayName + " / " + subject.role.ToString().ToUpperInvariant() + ": ", StringComparison.Ordinal),
                "Real E/interact input displays the targeted resident's role-specific dialogue.");
            Require(player.Sandbox.Credits == credits && player.Sandbox.CompletedContracts == contracts,
                "Talking through gameplay input does not award object discovery money or advance an unrelated contract.");
            smoke.conversations.Add("PLAYER E INPUT / " + feedback);
        }

        private static Vector3[] Snapshots(SlopCityCrowd crowd)
        {
            var result = new Vector3[crowd.PopulationCount];
            for (int index = 0; index < result.Length; index++) result[index] = crowd.Snapshot(index).position;
            return result;
        }
        private static HashSet<Collider> PlayerColliders(Scene scene) => new HashSet<Collider>(scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<SlopCityFirstPerson>(true))
            .SelectMany(player => player.GetComponentsInChildren<Collider>(true)));
        private static bool Relevant(Collider collider, HashSet<Collider> excluded) => collider && collider.enabled && !collider.isTrigger && !excluded.Contains(collider);
        private static bool ClearBody(Vector3 position, HashSet<Collider> excluded) =>
            !Physics.OverlapCapsule(position + Vector3.up * .29f, position + Vector3.up * 1.62f, .28f, ~0, QueryTriggerInteraction.Ignore)
                .Any(collider => Relevant(collider, excluded));
        private static bool Supported(Vector3 position, HashSet<Collider> excluded) =>
            Physics.RaycastAll(position + Vector3.up * .32f, Vector3.down, .62f, ~0, QueryTriggerInteraction.Ignore)
                .Any(hit => Relevant(hit.collider, excluded) && hit.normal.y >= .7f && Mathf.Abs(hit.point.y - position.y) <= .12f);
        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        private static IEnumerator Delay(double seconds)
        { double until = EditorApplication.timeSinceStartup + seconds; while (EditorApplication.timeSinceStartup < until) yield return null; }
        private static IEnumerator Until(Func<bool> condition, double seconds, string failure)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (!condition()) { if (EditorApplication.timeSinceStartup >= until) throw new TimeoutException(failure); yield return null; }
        }
        private static void Require(bool condition, string message)
        { smoke.Check(condition, message); if (!condition) throw new InvalidOperationException(message); }
        private static Report NewReport(string scenePath) => new Report
        {
            checkedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion, scenePath = scenePath,
            scope = "Saved scene in Play Mode: observed crowd movement, environmental clearance, game pause, conversation and cooldown. Visual realism remains a separate rendered review."
        };
        private static void Save(string path, Report report)
        { string full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)); File.WriteAllText(full, JsonUtility.ToJson(report, true)); }
        private static void Finish(Exception exception)
        {
            SessionState.SetBool(ActiveKey, false); Work.Clear();
            // The live sandbox saves again in OnApplicationQuit. Remove only the temporary Play Mode
            // instances before restoring the user's exact prior preference, so exiting cannot overwrite it.
            if (Application.isPlaying)
                foreach (SlopCitySandbox sandbox in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsSortMode.None)) Object.DestroyImmediate(sandbox);
            if (SessionState.GetBool(BackupExistsKey, false)) PlayerPrefs.SetString(SaveKey, SessionState.GetString(BackupKey, ""));
            else PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save(); Time.timeScale = 1f;
            if (smoke == null) smoke = NewReport(SlopCitySceneBuilder.ScenePath);
            if (exception != null) smoke.errors.Add(exception.ToString());
            smoke.passed = smoke.errors.Count == 0 && smoke.runtimeErrors.Count == 0;
            smoke.finishedAtUtc = DateTime.UtcNow.ToString("O");
            Save(SmokeReportPath, smoke);
            Debug.Log("SLOP_CITY_MEGACITY_SMOKE_" + (smoke.passed ? "PASSED" : "FAILED") + " " + SmokeReportPath);
            if (exception != null) Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(smoke.passed ? 0 : 1);
        }

        [Serializable] private sealed class FontSource { public string license, downloadUrl, sha256; public int bytes; }
        [Serializable] private sealed class SignAtlas
        {
            public int schema;
            public string fontSha256, albedo, emission;
            public int[] atlasSize;
            public string[] labels, missingGlyphs;
            public SignEntry[] entries;
        }
        [Serializable] private sealed class SignEntry
        { public int id; public string label, orientation; public int[] rectPixelsTopLeft; public float[] uv; }
        [Serializable] public sealed class Report
        {
            public bool passed;
            public int checks, japaneseSignAssemblies, japaneseVideoBillboards, japaneseAtlasTextures, signLabels,
                population, initialMoving, observedMoving, distinctRoles, populatedDistricts, routes, routeNodes, routeEdges,
                populationMeshAssets, populationMaterialAssets;
            public string checkedAtUtc, finishedAtUtc, unityVersion, scenePath, scope;
            public List<string> errors = new List<string>();
            public List<string> warnings = new List<string>();
            public List<string> runtimeErrors = new List<string>();
            public List<string> editorDiagnostics = new List<string>();
            public List<string> conversations = new List<string>();
            public void Check(bool condition, string message) { checks++; if (!condition) errors.Add(message); }
            public void ThrowIfInvalid()
            { passed = errors.Count == 0 && runtimeErrors.Count == 0; if (!passed) throw new InvalidOperationException("Megacity validation failed:\n" + string.Join("\n", errors.Concat(runtimeErrors))); }
        }
    }
}
#endif
