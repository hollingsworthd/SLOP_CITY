#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Checks saved/generated hand anatomy and actual instanced HDR-forward lighting. No Play Mode or save writes.</summary>
    public static class SlopCityHumanHandCheck
    {
        [Serializable] private sealed class Receipt
        {
            public string unityVersion, graphicsDevice;
            public bool passed;
            public int checks, handVertices, rawInvalidPixels, finishedInvalidPixels;
            public string[] images;
        }
        private static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/Reports"));
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode
                || !Application.companyName.StartsWith("SlopCityValidation", StringComparison.Ordinal)
                || !Application.persistentDataPath.Contains("SlopCityValidation")
                || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("An isolated graphics-enabled SlopCityValidation batch project is required.");
            var report = new Receipt { unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceType.ToString() };
            void Check(bool value, string reason) { report.checks++; if (!value) throw new InvalidOperationException(reason); }
            Mesh generated = null; int exit = 1;
            try
            {
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                var library = Object.FindFirstObjectByType<SlopCityCrowd>().Visuals;
                Mesh hand = library.parts[(int)SlopCityHumanPart.Hand];
                Check(hand && hand.GetTopology(0) == MeshTopology.Triangles, "Saved hand retains triangle anatomy.");
                report.handVertices = hand.vertexCount;
                Check(hand.vertexCount > 1000 && hand.bounds.size.x < .1f && hand.bounds.size.y < .15f,
                    "Detailed hand remains at authored adult scale.");
                generated = SlopCityHumanMeshes.HandMeshForValidation();
                foreach (Mesh mesh in new[] { hand, generated })
                {
                    Vector3[] normals = mesh.normals; Vector4[] tangents = mesh.tangents;
                    Check(normals.Length == mesh.vertexCount && normals.All(n => Finite(n) && Mathf.Abs(n.sqrMagnitude - 1) < .001f),
                        mesh.name + " has finite unit normals, including touching fingertips.");
                    Check(tangents.Length == mesh.vertexCount && tangents.All(t => Finite(t) && Mathf.Abs(new Vector3(t.x,t.y,t.z).sqrMagnitude - 1) < .001f),
                        mesh.name + " retains finite unit tangent directions.");
                    var coincident = new Dictionary<Vector3, List<Vector3>>();
                    Vector3[] positions = mesh.vertices;
                    for (int i = 0; i < positions.Length; i++)
                    {
                        Vector3 p = positions[i]; var key = new Vector3(Mathf.Round(p.x * 100000), Mathf.Round(p.y * 100000), Mathf.Round(p.z * 100000));
                        if (!coincident.TryGetValue(key, out var group)) coincident[key] = group = new List<Vector3>();
                        group.Add(normals[i]);
                    }
                    Check(coincident.Values.Any(group => group.Any(a => group.Any(b => Vector3.Dot(a,b) < -.95f))),
                        "Disconnected touching finger surfaces retain opposing outward normals rather than a fallback direction.");
                }
                Check(hand.vertices.SequenceEqual(generated.vertices) && hand.triangles.SequenceEqual(generated.triangles)
                    && hand.uv.SequenceEqual(generated.uv), "Saved and generated hands preserve the same geometry, topology and UVs.");
                Check(hand.normals.Zip(generated.normals,(a,b)=>(a-b).sqrMagnitude).All(d=>d<1e-10f),
                    "Saved hand carries the corrected generator's smoothing groups.");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.3f,.3f,.3f);
                RenderSettings.fog = false; RenderSettings.skybox = null;
                var crowd = new GameObject("Hand_Anatomy_Fixture").AddComponent<SlopCityCrowd>();
                var nodes = new[] { new SlopCityCrowdNode { position = new Vector3(.5f,0,0), neighbors = new[] {0} },
                    new SlopCityCrowdNode { position = new Vector3(-.5f,0,0), neighbors = new[] {1} } };
                var seeds = new[] { new SlopCityNpcSeed { residentId=100,route=0,node=0,next=0,appearance=0,height=1.76f,width=1,speed=1,pause=60 },
                    new SlopCityNpcSeed { residentId=101,route=0,node=1,next=1,appearance=0,height=1.76f,width=1,speed=1,pause=60 } };
                crowd.Configure(library, new[] { new SlopCityCrowdRoute { label="Hand fixture",nodes=nodes } }, seeds);
                typeof(SlopCityCrowd).GetMethod("EnsureAgents",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(crowd,null);
                var localAgents=(Array)typeof(SlopCityCrowd).GetField("agents",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(crowd);
                var casualResident=localAgents.GetValue(0);casualResident.GetType().GetField("casual").SetValue(casualResident,true);
                var light = new GameObject("Hand_Fixture_Key").AddComponent<Light>(); light.type=LightType.Directional;
                light.intensity=1.5f; light.shadows=LightShadows.None; light.transform.rotation=Quaternion.Euler(30,145,0);
                var camera = new GameObject("Hand_Fixture_Camera").AddComponent<Camera>();
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.13f,.15f,.17f);
                camera.nearClipPlane=.01f; camera.farClipPlane=30; camera.allowHDR=true; camera.allowMSAA=false;
                camera.renderingPath=RenderingPath.Forward; camera.fieldOfView=32;
                camera.transform.position=new Vector3(0,.9f,4); camera.transform.LookAt(new Vector3(0,.9f,0));
                string raw = Path.Combine(Folder,"hands-lit-hdr.png"), finished = Path.Combine(Folder,"hands-forward-hdr.png");
                report.images=new[] {raw,finished}; report.rawInvalidPixels=Capture(camera,raw);
                Check(crowd.PresentedCount == 2, "Actual instanced renderer presents both local residents.");
                var batches=(IList)typeof(SlopCityCrowd).GetField("batches",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(crowd);
                var visitorMeshes=new List<string>();int shirts=0,ties=0,lapels=0,visitorShirts=0,sleeves=0,darkForearms=0,darkHands=0,darkHeads=0;
                bool whiteTop=false,shortSleeves=false;
                Material darkSkin=library.skin[Mathf.Min(4,library.skin.Length-1)];
                foreach(object batch in batches)
                {
                    var type=batch.GetType();var mesh=(Mesh)type.GetField("mesh").GetValue(batch);
                    int count=(int)type.GetField("count").GetValue(batch);var material=(Material)type.GetField("material").GetValue(batch);
                    if(count>0 && mesh.name.StartsWith("Visitor ",StringComparison.Ordinal))visitorMeshes.Add(mesh.name);
                    if(mesh.name=="Smith shirt and collar")shirts+=count;
                    if(mesh.name=="Smith necktie")ties+=count;
                    if(mesh.name=="Smith notched lapels")lapels+=count;
                    if(mesh.name=="Visitor short sleeved white shirt")
                    {visitorShirts+=count;Color tint=material.color;whiteTop=tint.r>.9f&&tint.g>.9f&&tint.b>.9f;}
                    if(mesh.name=="Visitor white short sleeve")
                    {sleeves+=count;shortSleeves=mesh.bounds.min.y>-.18f&&mesh.bounds.max.y<.04f&&mesh.bounds.size.y<.22f;}
                    if(material==darkSkin)
                    {
                        if(mesh==library.parts[(int)SlopCityHumanPart.Forearm])darkForearms+=count;
                        if(mesh==library.parts[(int)SlopCityHumanPart.Hand])darkHands+=count;
                        if(mesh==library.parts[(int)SlopCityHumanPart.Head])darkHeads+=count;
                    }
                }
                visitorMeshes.Sort(StringComparer.Ordinal);
                Check(visitorMeshes.SequenceEqual(new[]{"Visitor blue brimmed hat","Visitor short sleeved white shirt","Visitor white short sleeve"}),
                    "Visitor wears only the white shirt/short sleeves and blue hat; no jacket, torso pinstripes, bare-chest inset or cane.");
                Check(shirts==1&&ties==1&&lapels==1&&visitorShirts==1&&whiteTop,
                    "Only the ordinary Smith wears jacket lapels, collar and tie; the visitor's complete top is white.");
                Check(sleeves==2&&shortSleeves&&darkForearms==2&&darkHands==2&&darkHeads==1&&darkSkin.color.grayscale<library.skin[0].color.grayscale,
                    "Both short sleeves expose real darker-skin forearms and matching hands/face/neck, rather than recolored long sleeves.");
                Check(report.rawInvalidPixels == 0, "Lit hand geometry emits no invalid HDR pixels into the bloom input.");
                var finish = camera.gameObject.AddComponent<SlopCityCameraFinish>();
                typeof(SlopCityCameraFinish).GetField("lightweightFinish",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(finish,true);
                finish.Configure(Shader.Find("Hidden/SlopCity/CameraFinish"));
                // Native lightweight setup defaults to LDR. Restore the desktop Web
                // profile's HDR surface so its exact forward-bloom failure is tested.
                camera.allowHDR=true;
                report.finishedInvalidPixels=Capture(camera,finished);
                Check(report.finishedInvalidPixels == 0, "HDR-forward finish remains finite with visible anatomical hands.");
                report.passed=true; exit=0; Debug.Log("SLOP_CITY_HUMAN_HAND_PASSED " + report.checks + " checks; native HDR-forward renderer, not browser/device acceptance.");
            }
            catch (Exception error) { Debug.LogException(error); }
            finally
            {
                if (generated) Object.DestroyImmediate(generated);
                Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder,"human-hand-check.json"),JsonUtility.ToJson(report,true));
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single); EditorApplication.Exit(exit);
            }
        }
        private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
        private static bool Finite(Vector3 n) => Finite(n.x) && Finite(n.y) && Finite(n.z);
        private static bool Finite(Vector4 n) => Finite(n.x) && Finite(n.y) && Finite(n.z) && Finite(n.w);
        private static int Capture(Camera camera,string path)
        {
            const int width=1000,height=800;
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGBHalf);
            var pixels=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            var png=new Texture2D(width,height,TextureFormat.RGB24,false);
            RenderTexture prior=RenderTexture.active, priorTarget=camera.targetTexture;
            try
            {
                if(!target.Create())throw new InvalidOperationException("Hand HDR target allocation failed.");
                camera.aspect=width/(float)height;camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();Color[] data=pixels.GetPixels();
                int invalid=data.Count(c=>!Finite(c.r)||!Finite(c.g)||!Finite(c.b));
                png.SetPixels(data.Select(c=>new Color(Mathf.LinearToGammaSpace(c.r),Mathf.LinearToGammaSpace(c.g),Mathf.LinearToGammaSpace(c.b),1)).ToArray());png.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,png.EncodeToPNG());return invalid;
            }
            finally
            {
                camera.targetTexture=priorTarget;RenderTexture.active=prior;target.Release();
                Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);Object.DestroyImmediate(png);
            }
        }
    }
}
#endif
