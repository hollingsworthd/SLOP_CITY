#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Shared-mesh anatomy evidence, plus an optional portrait in the real running city.</summary>
    public static class SlopCityCyborgCheck
    {
        private const string ScenePath = "Assets/Scenes/SlopCity_CouchSpawn.unity";
        private static string Folder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports");
        [Serializable] private sealed class Receipt
        {
            public string checkedAtUtc, unityVersion, colorSpace, scope;
            public int productionCitizens, productionRoutes, sharedCyberMeshes, cyberVertices, maximumMeshVertices, fixtureCitizens, checks;
            public bool passed;
            public List<string> assertions = new List<string>();
            public List<string> images = new List<string>();
        }
        [Serializable] private sealed class PortraitReceipt
        {
            public string checkedAtUtc, displayName, role, image;
            public int citizenIndex, districtIndex, routeIndex, nodeIndex;
            public Vector3 feet, cameraPosition;
            public float height, heading;
            public bool moving;
        }

        // Synchronous graphics-enabled batch check; -quit is supported. No network,
        // Play Mode, scene saves, new production objects, or changes to player saves.
        public static void Run()
        {
            if (!Application.isBatchMode || !Application.persistentDataPath.Contains("SlopCityValidation"))
                throw new InvalidOperationException("Cyborg capture requires an isolated SlopCityValidation clone.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Cyborg capture requires graphics; omit -nographics.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start the anatomy fixture outside Play Mode.");
            var report = new Receipt { checkedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                colorSpace = QualitySettings.activeColorSpace.ToString(), scope = "Editor baked-assets and real instanced crowd renderer; staged anatomy lighting, not physical-iPhone or production-dusk evidence." };
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                SlopCityCrowd production = Object.FindFirstObjectByType<SlopCityCrowd>();
                Assert(production, "Saved scene has its existing crowd.", report);
                report.productionCitizens = production.PopulationCount; report.productionRoutes = production.Routes.Count;
                SlopCityCrowdVisuals library = production.Visuals;
                Assert(library.parts.Length == Enum.GetValues(typeof(SlopCityHumanPart)).Length, "Every shared mesh slot is baked into the saved scene.", report);
                for (int index = (int)SlopCityHumanPart.CyberFaceShell; index < library.parts.Length; index++)
                {
                    Mesh mesh = library.parts[index];
                    Assert(mesh && mesh.vertexCount > 0, "Cyborg mesh is present: " + ((SlopCityHumanPart)index), report);
                    Assert(mesh.vertexCount < 12000 && mesh.bounds.size.magnitude < .70f, "Cyborg part stays within its adult anatomy and bounded shared mesh budget: " + mesh.name, report);
                    report.sharedCyberMeshes++; report.cyberVertices += mesh.vertexCount;
                    report.maximumMeshVertices = Math.Max(report.maximumMeshVertices, mesh.vertexCount);
                }
                Assert(report.cyberVertices < 50000, "All cybernetics use fewer than 50,000 shared vertices, independent of population.", report);
                foreach (Material material in new[] { library.cyberMetal, library.cyberJoint, library.cyberSignal })
                    Assert(material && material.enableInstancing, "Cybernetics share one instanced material per surface.", report);
                Assert(library.cyberSignal.IsKeywordEnabled("_EMISSION") && library.cyberSignal.GetColor("_EmissionColor").maxColorComponent < 2
                    && (library.cyberSignal.globalIlluminationFlags&MaterialGlobalIlluminationFlags.EmissiveIsBlack)==0,
                    "Only small diagnostic optics emit restrained light; no per-citizen Light component.", report);
                MirrorCheck(library, SlopCityHumanPart.CyberFaceShell, SlopCityHumanPart.CyberFaceShellLeft, report);
                MirrorCheck(library, SlopCityHumanPart.CyberFaceHardware, SlopCityHumanPart.CyberFaceHardwareLeft, report);
                MirrorCheck(library, SlopCityHumanPart.CyberFaceSignal, SlopCityHumanPart.CyberFaceSignalLeft, report);

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.22f, .25f, .25f);
                RenderSettings.fog = false; RenderSettings.skybox = null;
                var root = new GameObject("Cyborg_Anatomy_Fixture");
                SlopCityCrowd crowd = root.AddComponent<SlopCityCrowd>();
                int[] looks = { 11, 29, 58, 87, 194, 219 };
                var nodes = new SlopCityCrowdNode[looks.Length]; var seeds = new SlopCityNpcSeed[looks.Length];
                for (int index = 0; index < looks.Length; index++)
                {
                    nodes[index] = new SlopCityCrowdNode { position = new Vector3(-2.875f + index * 1.15f, 0, 0), neighbors = new[] { index } };
                    seeds[index] = new SlopCityNpcSeed { route = 0, node = index, next = index, appearance = looks[index],
                        role = (SlopCityNpcRole)index, height = 1.70f + index % 3 * .05f, width = .92f + index % 3 * .06f,
                        speed = 1, heading = index == 5 ? 25 : 0, pause = 60, displayName = "Anatomy fixture " + index };
                }
                crowd.Configure(library, new[] { new SlopCityCrowdRoute { label = "Anatomy fixture", nodes = nodes } }, seeds);
                report.fixtureCitizens = crowd.PopulationCount;
                Assert(root.transform.childCount == 0 && Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length == 0,
                    "Six rendered citizens create no per-citizen objects or lights.", report);
                Light key = new GameObject("Fixture_Key").AddComponent<Light>(); key.type = LightType.Directional;
                // Citizens face +Z. The key must travel toward -Z so it lights their
                // faces; the old -30deg yaw backlit them and concealed the sculpt.
                key.color = new Color(1, .88f, .78f); key.intensity = 1.5f; key.transform.rotation = Quaternion.Euler(30, 145, 0);
                Light rim = new GameObject("Fixture_Rim").AddComponent<Light>(); rim.type = LightType.Directional;
                rim.color = new Color(.48f, .76f, .83f); rim.intensity = .65f; rim.transform.rotation = Quaternion.Euler(35, -25, 0);
                Light fill = new GameObject("Fixture_Fill").AddComponent<Light>(); fill.type = LightType.Directional;
                fill.color = new Color(.76f, .84f, .90f); fill.intensity = .28f; fill.transform.rotation = Quaternion.Euler(10, -150, 0);
                Camera camera = new GameObject("Fixture_AdultCamera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .032f, .035f);
                camera.nearClipPlane = .05f; camera.farClipPlane = 30; camera.allowHDR = true;
                camera.fieldOfView = 35; camera.transform.position = new Vector3(0, 1.03f, 7.1f);
                camera.transform.LookAt(new Vector3(0, .88f, 0));
                string lineup = Path.Combine(Folder, "cyborg-v0.12.0-anatomy.png");
                Capture(camera, lineup); report.images.Add(lineup);
                camera.fieldOfView = 30; camera.transform.position = nodes[0].position + new Vector3(.10f, 1.56f, 1.08f);
                camera.transform.LookAt(nodes[0].position + new Vector3(0, 1.56f, 0));
                string portrait = Path.Combine(Folder, "cyborg-v0.12.0-face.png");
                Capture(camera, portrait); report.images.Add(portrait);
                Assert(crowd.PopulationCount == seeds.Length && crowd.Routes.Count == 1, "Rendering preserves fixture population and route data.", report);
                report.passed = true;
            }
            finally
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, "cyborg-v0.12.0-check.json"), JsonUtility.ToJson(report, true));
                // Discard the temporary fixture, reopen the authored scene unchanged.
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            Debug.Log("Cyborg shared-mesh fixture passed " + report.checks + " checks; " + report.cyberVertices + " additional shared vertices.");
        }

        private static void MirrorCheck(SlopCityCrowdVisuals library, SlopCityHumanPart right, SlopCityHumanPart left, Receipt report)
        {
            Mesh a = library.parts[(int)right], b = library.parts[(int)left];
            Vector3[] verticesA = a.vertices, verticesB = b.vertices, normalsA = a.normals, normalsB = b.normals;
            bool mirrored = verticesA.Length == verticesB.Length;
            for (int index = 0; mirrored && index < verticesA.Length; index++)
            {
                Vector3 point = verticesA[index]; point.x = -point.x;
                Vector3 normal = normalsA[index]; normal.x = -normal.x;
                mirrored = (point - verticesB[index]).sqrMagnitude < .00000001f && (normal - normalsB[index]).sqrMagnitude < .00001f;
            }
            Assert(mirrored, "Opposite temple mesh has reflected geometry and outward normals: " + right, report);
        }
        private static void Assert(bool condition, string label, Receipt report)
        {
            if (!condition) throw new InvalidOperationException(label);
            report.checks++; report.assertions.Add(label);
        }

        /// <summary>Call from a running city audit after production lighting/streaming initializes.</summary>
        public static string CaptureInCityPortrait(Camera camera, string prefix)
        {
            if (!Application.isPlaying || !camera || !Application.persistentDataPath.Contains("SlopCityValidation"))
                throw new InvalidOperationException("An in-city portrait requires the actual running isolated validation scene.");
            SlopCityCrowd crowd = Object.FindFirstObjectByType<SlopCityCrowd>();
            if (!crowd) throw new InvalidOperationException("No production crowd.");
            Vector3 oldPosition = camera.transform.position; Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView; int chosen = -1; float nearest = float.PositiveInfinity; Vector3 position = default, aim = default;
            for (int index = 0; index < crowd.PopulationCount; index++)
            {
                SlopCityNpcSnapshot person = crowd.Snapshot(index);
                if (person.role == SlopCityNpcRole.Security || person.role == SlopCityNpcRole.Worker) continue;
                Vector3 eye = person.position + Vector3.up * (person.height * .90f);
                Vector3 front = Quaternion.Euler(0, person.heading, 0) * Vector3.forward;
                Vector3 observer = person.position + front * 2.4f + Vector3.up * 1.62f;
                if (Physics.Linecast(observer, eye, ~0, QueryTriggerInteraction.Ignore)) continue;
                float distance = (oldPosition - person.position).sqrMagnitude;
                if (distance >= nearest) continue;
                chosen = index; nearest = distance; position = observer; aim = eye - Vector3.up * .18f;
            }
            if (chosen < 0) throw new InvalidOperationException("No visible citizen has an unobstructed front portrait within the city.");
            try
            {
                camera.transform.position = position; camera.transform.LookAt(aim); camera.fieldOfView = 34;
                string path = Path.Combine(Folder, prefix + "-cyborg-street-portrait.png"); Capture(camera, path);
                SlopCityNpcSnapshot subject=crowd.Snapshot(chosen);
                var evidence=new PortraitReceipt { checkedAtUtc=DateTime.UtcNow.ToString("O"), citizenIndex=chosen,
                    displayName=subject.displayName, role=subject.role.ToString(), image=path, districtIndex=subject.districtIndex,
                    routeIndex=subject.routeIndex, nodeIndex=subject.nodeIndex, feet=subject.position, cameraPosition=position,
                    height=subject.height, heading=subject.heading, moving=subject.moving };
                File.WriteAllText(Path.Combine(Folder, prefix + "-cyborg-street-portrait.json"), JsonUtility.ToJson(evidence, true));
                return path;
            }
            finally { camera.transform.SetPositionAndRotation(oldPosition, oldRotation); camera.fieldOfView = oldFov; }
        }

        private static void Capture(Camera camera, string path)
        {
            const int width = 1600, height = 1000;
            RenderTexture previous = RenderTexture.active, oldTarget = camera.targetTexture;
            float oldAspect = camera.aspect;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR);
            var display = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var png = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                if (!target.Create() || !display.Create()) throw new InvalidOperationException("Cyborg capture target allocation failed.");
                camera.aspect = width / (float)height; camera.targetTexture = target; camera.Render();
                bool previousSRGB = GL.sRGBWrite;
                try { GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear; Graphics.Blit(target, display); }
                finally { GL.sRGBWrite = previousSRGB; }
                RenderTexture.active = display; png.ReadPixels(new Rect(0, 0, width, height), 0, 0); png.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, png.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = previous;
                Object.DestroyImmediate(png); target.Release(); display.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(display);
            }
        }
    }
}
#endif
