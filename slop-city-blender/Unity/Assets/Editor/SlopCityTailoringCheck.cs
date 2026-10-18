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
using Object=UnityEngine.Object;
namespace SlopCity.Editor
{
    /// <summary>Actual shared crowd meshes/poses under HDR-forward lighting; no Play Mode or saved-scene writes.</summary>
    public static class SlopCityTailoringCheck
    {
        [Serializable] private sealed class Receipt
        {
            public bool passed;public int checks,maximumInvalidPixels,yokeVertices;
            public string unityVersion,graphicsDevice;public List<string> images=new List<string>();
            public List<float> tieToShirtRatios=new List<float>();
        }
        private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static object Get(object target,string name)=>target.GetType().GetField(name,Fields).GetValue(target);
        private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Fields).SetValue(target,value);
        public static void Run()
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode
                ||!Application.companyName.StartsWith("SlopCityValidation",StringComparison.Ordinal)
                ||!Application.persistentDataPath.Contains("SlopCityValidation")||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)
                throw new InvalidOperationException("An isolated graphics-enabled SlopCityValidation project is required.");
            var report=new Receipt{unityVersion=Application.unityVersion,graphicsDevice=SystemInfo.graphicsDeviceType.ToString()};
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../Builds/Reports"));int exit=1;
            void Check(bool value,string message){report.checks++;if(!value)throw new InvalidOperationException(message);}
            Camera.CameraCallback draw=null;
            try
            {
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath,OpenSceneMode.Single);
                var library=Object.FindFirstObjectByType<SlopCityCrowd>().Visuals;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.3f,.3f,.3f);
                RenderSettings.fog=false;RenderSettings.skybox=null;
                var crowd=new GameObject("Tailoring_Fixture").AddComponent<SlopCityCrowd>();crowd.enabled=false;
                crowd.Configure(library,Array.Empty<SlopCityCrowdRoute>(),Array.Empty<SlopCityNpcSeed>());
                typeof(SlopCityCrowd).GetMethod("EnsureWardrobe",Fields).Invoke(crowd,null);
                var wardrobe=Get(crowd,"wardrobe");Check(wardrobe!=null,"Shared wardrobe allocates successfully.");
                var yoke=(Mesh)Get(wardrobe,"trouserYokeMesh");report.yokeVertices=yoke.vertexCount;
                Check(yoke.vertexCount<=8192&&yoke.bounds.size.x<=.45f&&yoke.bounds.min.y>.4f&&yoke.bounds.max.z>=.49f&&yoke.HasVertexAttribute(VertexAttribute.TexCoord1),
                    "One bounded skin-weighted garment spans waist to knees with conservative animated bounds.");
                Vector4[] positions=ReadAttribute(yoke,VertexAttribute.Position),normals=ReadAttribute(yoke,VertexAttribute.Normal),
                    tangents=ReadAttribute(yoke,VertexAttribute.Tangent),weights=ReadAttribute(yoke,VertexAttribute.TexCoord1);
                Check(normals.All(n=>Finite(n.x)&&Finite(n.y)&&Finite(n.z)&&Mathf.Abs(((Vector3)n).magnitude-1)<.001f)
                    &&tangents.All(t=>Finite(t.x)&&Finite(t.y)&&Finite(t.z)&&Mathf.Abs(((Vector3)t).magnitude-1)<.001f&&Mathf.Abs(t.w)==1),
                    "Actual uploaded GPU normals and tangents are finite unit vectors, including UV seams.");
                Check(weights.All(w=>w.x>=0&&w.y>=0&&w.x+w.y<=1.00001f),"Actual uploaded skin weights are normalized and nonnegative.");
                Check((bool)Get(wardrobe,"animatedTrousers"),"The actual renderer supports the instanced articulated garment shader.");
                var shader=(Material)Get(wardrobe,"trouserBlack");
                Check(!ShaderUtil.ShaderHasError(shader.shader),"The native Built-In Standard cloth shader compiles without errors.");
                var dressMesh=(Mesh)Get(wardrobe,"dressMesh");
                Vector4[] dressVertices=ReadAttribute(dressMesh,VertexAttribute.Position);
                Vector4[] chestVertices=ReadAttribute((Mesh)Get(wardrobe,"necklineMesh"),VertexAttribute.Position);
                Vector4 bustPeak=dressVertices.Where(p=>p.y>1.18f&&p.y<1.40f).OrderByDescending(p=>p.z).First();
                Check(dressMesh.vertexCount<=8192&&bustPeak.y>1.23f&&bustPeak.y<1.30f&&bustPeak.z>.13f&&bustPeak.z<.20f,
                    "The shared covered bodice has a bounded rounded projection lower on the chest.");
                float hem=dressVertices.Where(p=>p.y<.56f).Max(p=>Mathf.Abs(p.x));
                float hips=dressVertices.Where(p=>p.y>.85f&&p.y<.96f).Max(p=>Mathf.Abs(p.x));
                Vector4 seat=dressVertices.Where(p=>p.y>.78f&&p.y<1.0f).OrderBy(p=>p.z).First();
                Check(hem<hips*.85f&&hem<.18f&&seat.z<-.16f&&seat.z>-.21f&&seat.y>.83f&&seat.y<.95f,
                    "The opaque sheath has a narrower hem than hips and a broad rounded covered seat.");
                Check(chestVertices.Where(p=>p.y>1.37f).All(p=>p.z<.10f)
                    &&chestVertices.Where(p=>Mathf.Abs(p.x)>.145f).All(p=>p.y<1.405f),
                    "Upper chest projection stays shallow and the lateral shoulder blends down into the arm cap.");
                int[] clothIndices=ReadIndices(yoke);int frontTriangles=0;
                for(int i=0;i<clothIndices.Length;i+=3)
                {
                    Vector3 a=positions[clothIndices[i]],b=positions[clothIndices[i+1]],c=positions[clothIndices[i+2]];
                    if(new[]{a,b,c}.All(p=>p.y>.84f&&p.y<.97f&&Mathf.Abs(p.x)<.035f&&p.z>.025f))
                    {
                        if(Vector3.Cross(b-a,c-a).z<-.0000001f)
                            throw new InvalidOperationException("The fly contains a folded backward-facing triangle.");
                        frontTriangles++;
                    }
                }
                Check(frontTriangles>4,"Actual uploaded fly triangles remain forward-facing instead of crossing at the inseam.");
                var hat=(Mesh)Get(wardrobe,"visitorHatMesh");var hairMesh=library.parts[(int)SlopCityHumanPart.HairShort];
                Check(hat.bounds.min.y<hairMesh.bounds.max.y-.02f&&hat.bounds.min.y>hairMesh.bounds.center.y,
                    "The worn brim overlaps the saved hair crown instead of floating above it.");
                var tie=(Mesh)Get(wardrobe,"tieMesh");var black=(Material)Get(wardrobe,"tie");
                Check(tie.bounds.min.y<1.06f&&tie.bounds.max.y>1.41f&&tie.bounds.size.x<.032f&&black.color.maxColorComponent<.01f,
                    "The slim uniformly black tie runs from knot down to the navel.");
                Check(tie.vertexCount<100,"Conforming tie remains one small shared ribbon.");
                var agentType=typeof(SlopCityCrowd).GetNestedType("Agent",BindingFlags.NonPublic);
                object Person(float x,bool remote,bool red)
                {
                    object body=Activator.CreateInstance(agentType,true);
                    Set(body,"seed",new SlopCityNpcSeed{height=1.76f,width=1,appearance=0,residentId=100+(int)((x+1)*10)});
                    Set(body,"position",new Vector3(x,0,0));Set(body,"remote",remote);Set(body,"redContact",red);return body;
                }
                object[] people={Person(-.9f,false,false),Person(0,true,false),Person(.9f,false,true)};
                var light=new GameObject("Tailoring_Key").AddComponent<Light>();light.type=LightType.Directional;
                light.intensity=1.5f;light.shadows=LightShadows.None;light.transform.rotation=Quaternion.Euler(30,145,0);
                var camera=new GameObject("Tailoring_Camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.13f,.15f,.17f);camera.orthographic=true;camera.orthographicSize=1f;
                camera.nearClipPlane=.01f;camera.farClipPlane=30;camera.allowHDR=true;camera.allowMSAA=false;camera.renderingPath=RenderingPath.Forward;
                camera.transform.position=new Vector3(0,.94f,5);camera.transform.LookAt(new Vector3(0,.94f,0));
                var batches=(IList)Get(crowd,"batches");
                var reserve=typeof(SlopCityCrowd).GetMethod("TryReservePerson",Fields);
                var commit=typeof(SlopCityCrowd).GetMethod("CommitReservedPerson",Fields);
                bool isolateHip=false;
                draw=target=>
                {
                    if(target!=camera)return;
                    foreach(object batch in batches)Set(batch,"count",0);
                    foreach(object person in people)
                    {
                        if(!(bool)reserve.Invoke(crowd,new[]{person,(object)true}))throw new InvalidOperationException("Whole-person reservation failed.");
                        commit.Invoke(crowd,null);
                    }
                    var armCounts=new List<(object batch,int count)>();
                    if(isolateHip)foreach(object batch in batches)
                    {
                        Mesh mesh=(Mesh)Get(batch,"mesh");
                        if(mesh==library.parts[(int)SlopCityHumanPart.UpperArm]||mesh==library.parts[(int)SlopCityHumanPart.Forearm]||mesh==library.parts[(int)SlopCityHumanPart.Hand])
                        {armCounts.Add((batch,(int)Get(batch,"count")));Set(batch,"count",0);}
                    }
                    try{typeof(SlopCityCrowd).GetMethod("DrawBatches",Fields).Invoke(crowd,new object[]{camera});}
                    finally{foreach(var arm in armCounts)Set(arm.batch,"count",arm.count);}
                };
                Camera.onPreCull+=draw;
                SlopCityCameraFinish finish=null;
                foreach(var pose in new[]{("front",0f,false),("three-quarter",45f,false),("side",90f,false),("back",180f,false),("back-three-quarter",225f,false),("walking-front",0f,true),("walking-three-quarter",45f,true),("walking-side",90f,true),("walking-back",180f,true),("maximum-stride-three-quarter",45f,true),("maximum-stride-side",90f,true),("maximum-stride-back-three-quarter",225f,true)})
                {
                    foreach(object person in people){Set(person,"heading",pose.Item2);Set(person,"moving",pose.Item3);Set(person,"phase",pose.Item1.StartsWith("maximum-stride",StringComparison.Ordinal)?2.076183f:1.35f);}
                    if(finish)finish.enabled=false;
                    camera.allowHDR=true;
                    Color[] pixels=Capture(camera,Path.Combine(folder,"tailoring-"+pose.Item1+"-raw.png"),report);
                    if(pose.Item1=="front")
                    {
                        int cloth=0,dress=0;object tieBatch=null;
                        foreach(object batch in batches)
                        {
                            Mesh mesh=(Mesh)Get(batch,"mesh");int count=(int)Get(batch,"count");
                            if(mesh==yoke)cloth+=count;if(mesh==(Mesh)Get(wardrobe,"dressMesh"))dress+=count;
                            if(mesh==tie&&count>0)tieBatch=batch;
                        }
                        Check(cloth==2&&dress==1,"The shared trouser cloth serves Smith and visitor only; the red contact uses the shaped red dress with skin legs.");
                        // Diagnostic only: omit foreground arm occluders while
                        // retaining the real dress and both underlying thighs.
                        // Main front/side/walking images always retain every limb.
                        Color[] hipPixels;
                        try{isolateHip=true;hipPixels=Capture(camera,Path.Combine(folder,"tailoring-hip-occlusion-diagnostic.png"),report);}
                        finally{isolateHip=false;}
                        object redBatch=batches.Cast<object>().First(b=>(Mesh)Get(b,"mesh")==dressMesh&&(int)Get(b,"count")==1);
                        Matrix4x4 redRoot=((Matrix4x4[])Get(redBatch,"matrices"))[0];int exposedHip=0;
                        for(float y=.885f;y<=.95f;y+=.002f)for(float x=-.195f;x<=.195f;x+=.002f)
                        {
                            Vector3 view=camera.WorldToViewportPoint(redRoot.MultiplyPoint3x4(new Vector3(x,y,.15f)));
                            int px=Mathf.Clamp(Mathf.RoundToInt(view.x*Width),0,Width-1),py=Mathf.Clamp(Mathf.RoundToInt(view.y*Height),0,Height-1);
                            Color c=hipPixels[py*Width+px];if(c.r>.08f&&c.g>c.r*.35f&&c.b>c.r*.25f)exposedHip++;
                        }
                        Check(exposedHip==0,"Arm-isolated diagnostic retains both thighs and has no skin-colored hip pixels through the dress.");
                        var root=((Matrix4x4[])Get(tieBatch,"matrices"))[0];
                        var point=wardrobe.GetType().GetMethod("FrontPoint",BindingFlags.Static|BindingFlags.NonPublic);
                        foreach(float height in new[]{1.10f,1.20f,1.29f,1.37f})
                        {
                            Vector3 local=(Vector3)point.Invoke(null,new object[]{new Vector2(0,height),.024f});
                            Vector3 adjacent=(Vector3)point.Invoke(null,new object[]{new Vector2(.024f,height),.008f});
                            float blackValue=Sample(pixels,camera.WorldToViewportPoint(root.MultiplyPoint3x4(local)));
                            float whiteValue=Sample(pixels,camera.WorldToViewportPoint(root.MultiplyPoint3x4(adjacent)));
                            float ratio=blackValue/Mathf.Max(.001f,whiteValue);report.tieToShirtRatios.Add(ratio);
                            Check(whiteValue>.003f&&blackValue<.02f&&ratio<.18f,"Actual tie pixels remain black above the white shirt at height "+height+" (no hidden middle).");
                        }
                    }
                    foreach(string materialName in new[]{"suit","visitorSuit"})
                    {
                        Material pants=(Material)Get(wardrobe,materialName);
                        object shinBatch=batches.Cast<object>().First(b=>(Mesh)Get(b,"mesh")==library.parts[(int)SlopCityHumanPart.Shin]
                            &&(Material)Get(b,"material")==pants&&(int)Get(b,"count")==2);
                        Material clothMaterial=(Material)Get(wardrobe,materialName=="suit"?"trouserBlack":"trouserBlue");
                        object clothBatch=batches.Cast<object>().First(b=>(Mesh)Get(b,"mesh")==yoke&&(Material)Get(b,"material")==clothMaterial);
                        var root=((Matrix4x4[])Get(clothBatch,"matrices"))[0];var hipPose=((Vector4[])Get(clothBatch,"hipPoses"))[0];
                        var shins=(Matrix4x4[])Get(shinBatch,"matrices");
                        for(int side=0;side<2;side++)
                        {
                            float sin=side==0?hipPose.x:hipPose.z,cos=side==0?hipPose.y:hipPose.w;
                            Vector3 expected=new Vector3((side==0?-1:1)*.096f,.91f-.43f*cos,-.43f*sin);
                            Vector3 actual=root.inverse.MultiplyPoint3x4(shins[side].GetColumn(3));
                            if(Vector3.Distance(expected,actual)>.00001f)throw new InvalidOperationException("Uploaded garment hip pose differs from the actual shin matrix.");
                        }
                    }
                    if(!finish)
                    {
                        finish=camera.gameObject.AddComponent<SlopCityCameraFinish>();
                        typeof(SlopCityCameraFinish).GetField("lightweightFinish",Fields).SetValue(finish,true);
                        finish.Configure(Shader.Find("Hidden/SlopCity/CameraFinish"));
                    }
                    finish.enabled=true;camera.allowHDR=true;
                    Capture(camera,Path.Combine(folder,"tailoring-"+pose.Item1+".png"),report);
                }
                // Verify shader skinning against the same hip solutions used by
                // the actual shin matrices across a complete walking cycle.
                var foot=typeof(SlopCityCrowd).GetMethod("FootPose",BindingFlags.Static|BindingFlags.NonPublic);
                var leg=typeof(SlopCityCrowd).GetMethod("LegPose",BindingFlags.Static|BindingFlags.NonPublic);
                var hip=typeof(SlopCityCrowd).GetMethod("ReachableHip",BindingFlags.Static|BindingFlags.NonPublic);
                float RotateY(Vector3 v,float a)=>Mathf.Cos(a)*v.y-Mathf.Sin(a)*v.z;
                float RotateZ(Vector3 v,float a)=>Mathf.Sin(a)*v.y+Mathf.Cos(a)*v.z;
                Vector3 Rotate(Vector3 v,float a)=>new Vector3(v.x,RotateY(v,a),RotateZ(v,a));
                foreach(Mesh garment in new[]{yoke,(Mesh)Get(wardrobe,"dressMesh")})
                {
                    positions=ReadAttribute(garment,VertexAttribute.Position);normals=ReadAttribute(garment,VertexAttribute.Normal);
                    tangents=ReadAttribute(garment,VertexAttribute.Tangent);weights=ReadAttribute(garment,VertexAttribute.TexCoord1);
                    Check(normals.All(n=>Finite(n.x)&&Finite(n.y)&&Finite(n.z)&&Mathf.Abs(((Vector3)n).magnitude-1)<.001f)
                        &&tangents.All(t=>Finite(t.x)&&Finite(t.y)&&Finite(t.z)&&Mathf.Abs(((Vector3)t).magnitude-1)<.001f&&Mathf.Abs(t.w)==1)
                        &&weights.All(w=>w.x>=0&&w.y>=0&&w.x+w.y<=1.00001f),
                        "Both shared articulated garments upload finite unit normals/tangents and normalized weights.");
                for(int phase=0;phase<80;phase++)
                {
                    Set(people[0],"moving",true);Set(people[0],"phase",phase*Mathf.PI*2/80/1.15f);
                    Vector2 l=(Vector2)foot.Invoke(null,new[]{people[0],(object)(-1)}),r=(Vector2)foot.Invoke(null,new[]{people[0],(object)1});
                    float bob=Mathf.Min(0,(float)hip.Invoke(null,new object[]{l}),(float)hip.Invoke(null,new object[]{r}));
                    float la=((Vector2)leg.Invoke(null,new object[]{l,bob})).x*Mathf.Deg2Rad;
                    float ra=((Vector2)leg.Invoke(null,new object[]{r,bob})).x*Mathf.Deg2Rad;
                    for(int i=0;i<positions.Length;i++)
                    {
                        Vector3 p=positions[i],lp=new Vector3(-.096f,.91f,0),rp=new Vector3(.096f,.91f,0);Vector4 w=weights[i];
                        Vector3 point=p*(1-w.x-w.y)+(Rotate(p-lp,la)+lp)*w.x+(Rotate(p-rp,ra)+rp)*w.y;
                        Vector3 n=(Vector3)normals[i]*(1-w.x-w.y)+Rotate(normals[i],la)*w.x+Rotate(normals[i],ra)*w.y;
                        Vector3 t=(Vector3)tangents[i]*(1-w.x-w.y)+Rotate(tangents[i],la)*w.x+Rotate(tangents[i],ra)*w.y;
                        if(!garment.bounds.Contains(point)||n.sqrMagnitude<.2f||t.sqrMagnitude<.2f)
                            throw new InvalidOperationException("Animated cloth exceeded its bound or lost a valid normal/tangent.");
                    }
                }
                }
                Check(true,"Uploaded garment hip poses agree with actual shin joint matrices in every rendered angle and maximum-stride pose.");
                Check(true,"All actual uploaded vertices remain within conservative bounds with finite nonzero normal/tangent blends across 80 gait phases.");
                // Force a capacity failure at the final paired shoe key. Neither
                // the previous pose vectors nor any body count may be committed.
                object shoeBatch=batches.Cast<object>().First(b=>(Mesh)Get(b,"mesh")==library.parts[(int)SlopCityHumanPart.Shoe]);
                foreach(object batch in batches)Set(batch,"count",0);
                Set(shoeBatch,"count",1022);
                int[] before=batches.Cast<object>().Select(b=>(int)Get(b,"count")).ToArray();
                Vector4[][] beforePoses=batches.Cast<object>().Where(b=>Get(b,"hipPoses")!=null)
                    .Select(b=>(Vector4[])((Vector4[])Get(b,"hipPoses")).Clone()).ToArray();
                bool accepted=(bool)reserve.Invoke(crowd,new[]{people[0],(object)true});
                Check(!accepted&&before.SequenceEqual(batches.Cast<object>().Select(b=>(int)Get(b,"count")))
                    &&beforePoses.Zip(batches.Cast<object>().Where(b=>Get(b,"hipPoses")!=null),
                        (a,b)=>a.SequenceEqual((Vector4[])Get(b,"hipPoses"))).All(x=>x),
                    "Whole-person capacity rejection commits no partial body or hip pose.");
                foreach(object batch in batches)Set(batch,"count",0);
                int ownedMeshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,ownedMaterials=Resources.FindObjectsOfTypeAll<Material>().Length;
                for(int iteration=0;iteration<20;iteration++)
                {
                    foreach(object batch in batches)Set(batch,"count",0);
                    foreach(object person in people){Check((bool)reserve.Invoke(crowd,new[]{person,(object)true}),"A complete warmed-up body reserves.");commit.Invoke(crowd,null);}
                }
                Check(ownedMeshes==Resources.FindObjectsOfTypeAll<Mesh>().Length&&ownedMaterials==Resources.FindObjectsOfTypeAll<Material>().Length,
                    "Repeated presentation allocates no per-person meshes or materials.");
                Set(wardrobe,"animatedTrousers",false);
                foreach(object batch in batches)Set(batch,"count",0);
                foreach(object person in people){if(!(bool)reserve.Invoke(crowd,new[]{person,(object)true}))throw new InvalidOperationException("Fallback reservation failed.");commit.Invoke(crowd,null);}
                Check(batches.Cast<object>().Where(b=>(Mesh)Get(b,"mesh")==yoke).All(b=>(int)Get(b,"count")==0)
                    &&batches.Cast<object>().Where(b=>(Mesh)Get(b,"mesh")==library.parts[(int)SlopCityHumanPart.Thigh]).Sum(b=>(int)Get(b,"count"))==6,
                    "Unsupported-shader fallback retains the original complete pelvis/thigh bodies and red skin legs.");
                Set(wardrobe,"animatedTrousers",true);
                Check(report.maximumInvalidPixels==0,"All static and walking angles retain finite HDR lighting and finish.");
                report.passed=true;exit=0;Debug.Log("SLOP_CITY_TAILORING_PASSED "+report.checks+" checks; native posed HDR-forward renders, not browser/device acceptance.");
            }
            catch(Exception e){Debug.LogException(e);}
            finally
            {
                if(draw!=null)Camera.onPreCull-=draw;
                Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"tailoring-check.json"),JsonUtility.ToJson(report,true));
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.Exit(exit);
            }
        }
        private static int[] ReadIndices(Mesh mesh)
        {
            using(var buffer=mesh.GetIndexBuffer())
            {
                var request=AsyncGPUReadback.Request(buffer);request.WaitForCompletion();
                if(request.hasError)throw new InvalidOperationException("Garment index GPU readback failed.");
                byte[] data=request.GetData<byte>().ToArray();int stride=mesh.indexFormat==IndexFormat.UInt32?4:2;
                var values=new int[(int)mesh.GetIndexCount(0)];
                for(int i=0;i<values.Length;i++)values[i]=stride==4?(int)BitConverter.ToUInt32(data,i*stride):BitConverter.ToUInt16(data,i*stride);
                return values;
            }
        }
        private static Vector4[] ReadAttribute(Mesh mesh,VertexAttribute attribute)
        {
            if(mesh.GetVertexAttributeFormat(attribute)!=VertexAttributeFormat.Float32)
                throw new InvalidOperationException("Unexpected uploaded garment attribute format.");
            int stream=mesh.GetVertexAttributeStream(attribute),offset=mesh.GetVertexAttributeOffset(attribute),
                stride=mesh.GetVertexBufferStride(stream),dimension=mesh.GetVertexAttributeDimension(attribute);
            using(var buffer=mesh.GetVertexBuffer(stream))
            {
                var request=AsyncGPUReadback.Request(buffer);request.WaitForCompletion();
                if(request.hasError)throw new InvalidOperationException("Garment GPU readback failed.");
                byte[] data=request.GetData<byte>().ToArray();var values=new Vector4[mesh.vertexCount];
                for(int i=0;i<values.Length;i++)for(int component=0;component<dimension;component++)
                    values[i][component]=BitConverter.ToSingle(data,i*stride+offset+component*4);
                return values;
            }
        }
        private const int Width=1500,Height=1000;
        private static float Sample(Color[] pixels,Vector3 viewport)
        {
            int x=Mathf.Clamp(Mathf.RoundToInt(viewport.x*Width),1,Width-2),y=Mathf.Clamp(Mathf.RoundToInt(viewport.y*Height),1,Height-2);
            float sum=0;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)sum+=pixels[(y+dy)*Width+x+dx].grayscale;return sum/9;
        }
        private static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        private static Color[] Capture(Camera camera,string path,Receipt report)
        {
            var target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGBHalf);
            var pixels=new Texture2D(Width,Height,TextureFormat.RGBAFloat,false,true);var png=new Texture2D(Width,Height,TextureFormat.RGB24,false);
            var prior=RenderTexture.active;var priorTarget=camera.targetTexture;
            try
            {
                if(!target.Create())throw new InvalidOperationException("Tailoring HDR target allocation failed.");
                camera.aspect=Width/(float)Height;camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,Width,Height),0,0);pixels.Apply();Color[] data=pixels.GetPixels();
                report.maximumInvalidPixels=Math.Max(report.maximumInvalidPixels,data.Count(c=>!Finite(c.r)||!Finite(c.g)||!Finite(c.b)));
                png.SetPixels(data.Select(c=>new Color(Mathf.LinearToGammaSpace(c.r),Mathf.LinearToGammaSpace(c.g),Mathf.LinearToGammaSpace(c.b),1)).ToArray());png.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,png.EncodeToPNG());report.images.Add(path);return data;
            }
            finally{camera.targetTexture=priorTarget;RenderTexture.active=prior;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);Object.DestroyImmediate(png);}
        }
    }
}
#endif
