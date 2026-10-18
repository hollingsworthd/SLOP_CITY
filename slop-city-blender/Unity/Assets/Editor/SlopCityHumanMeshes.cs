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
    /// <summary>Shared human anatomy and tailored clothing: shaped mesh surfaces, never visible physics primitives.</summary>
    public static class SlopCityHumanMeshes
    {
        private const string Folder = "Assets/Materials/SlopCityCrowd";
        public static SlopCityCrowdVisuals Build()
        {
            EnsureFolder(); SurfaceMaps.Clear();
            var result = new SlopCityCrowdVisuals { parts = new Mesh[Enum.GetValues(typeof(SlopCityHumanPart)).Length] };
            Set(result, SlopCityHumanPart.Jacket, Torso(false, false));
            Set(result, SlopCityHumanPart.Coat, Torso(true, false));
            Set(result, SlopCityHumanPart.Vest, Torso(false, true));
            var b = new Builder();
            b.Loft(new[] { R(.83f,.145f,.094f), R(.91f,.165f,.11f), R(1.01f,.156f,.095f) });
            Set(result, SlopCityHumanPart.Pelvis, b);
            Set(result, SlopCityHumanPart.UpperArm, Limb(new[] { R(.028f,.016f,.031f),R(.015f,.037f,.048f),R(-.012f,.054f,.062f),R(-.09f,.058f,.059f),R(-.19f,.051f,.05f),R(-.282f,.046f,.046f) }));
            b = Limb(new[] { R(.032f,.047f,.047f),R(-.07f,.051f,.046f),R(-.19f,.039f,.036f),R(-.265f,.032f,.03f) });
            // Inset cloth liners close articulation apertures without enlarging the sleeve silhouette.
            b.Ellipsoid(Vector3.zero,new Vector3(.044f,.047f,.044f),16,10);
            Set(result, SlopCityHumanPart.Forearm, b);
            b = Hand();
            Set(result, SlopCityHumanPart.Hand, b);
            Set(result, SlopCityHumanPart.Thigh, Limb(new[] {R(.025f,.099f,.100f),R(-.10f,.098f,.098f),R(-.28f,.081f,.078f),R(-.43f,.064f,.063f)}));
            b = Limb(new[] {R(.042f,.065f,.062f),R(-.105f,.069f,.065f),R(-.23f,.056f,.051f),R(-.415f,.047f,.044f)});
            // A flexed knee separates the two open trouser tubes. This recessed lining is
            // the same cloth/material as the leg and remains inside its outer radius.
            b.Ellipsoid(Vector3.zero,new Vector3(.063f,.066f,.061f),18,12);
            Set(result, SlopCityHumanPart.Shin, b);
            b = new Builder();
            b.Loft(new[] {new Vector4(-.049f,.046f,.104f,.025f),new Vector4(-.032f,.048f,.117f,.030f),new Vector4(.006f,.046f,.103f,.031f),new Vector4(.052f,.039f,.054f,.000f)},16);
            b.Ellipsoid(new Vector3(0,-.011f,.075f),new Vector3(.041f,.020f,.059f));
            Set(result, SlopCityHumanPart.Shoe, b);
            Set(result, SlopCityHumanPart.Head, SculptedHead());
            Set(result, SlopCityHumanPart.HairShort, Hair(0));
            Set(result, SlopCityHumanPart.HairBob, Hair(1));
            Set(result, SlopCityHumanPart.HairTied, Hair(2));
            b = new Builder();
            foreach(float side in new[]{-1f,1f})
            {
                // The old pupil was taller than the visible sclera, giving a bead-eye
                // silhouette. Keep the iris inside the anatomically shaped lid opening.
                b.Ellipsoid(new Vector3(side*.027f,.179f,FaceDepth(side*.027f,.179f)+.009f),new Vector3(.0034f,.0035f,.0015f),14,8);
                for(int step=0;step<12;step++)
                {
                    Vector3 Brow(float t)
                    {
                        float u=t*2-1,x=side*.028f+u*.014f,y=.194f+(1-u*u)*.003f+u*side*.001f;
                        return new Vector3(x,y,FaceDepth(x,y)+.002f);
                    }
                    b.Tube(Brow(step/12f),Brow((step+1)/12f),.0012f,6);
                }
                float nx=side*.008f;
                b.Ellipsoid(new Vector3(nx,.125f,FaceDepth(nx,.125f)+.001f),new Vector3(.0027f,.0014f,.0014f),10,6);
            }
            for(int step=0;step<20;step++)
            {
                Vector3 Mouth(float t)
                {
                    float x=Mathf.Lerp(-.025f,.025f,t),y=.105f+.0014f*Mathf.Cos(x/.025f*Mathf.PI)-.0011f*Mathf.Exp(-x*x/.000025f);
                    return new Vector3(x,y,FaceDepth(x,y)+.001f);
                }
                b.Tube(Mouth(step/20f),Mouth((step+1)/20f),.00065f,6);
            }
            Set(result, SlopCityHumanPart.Face, b);
            b = new Builder();
            foreach(float side in new[]{-1f,1f})b.Ellipsoid(new Vector3(side*.027f,.179f,FaceDepth(side*.027f,.179f)+.004f),new Vector3(.0115f,.0048f,.0043f),16,10);
            Set(result, SlopCityHumanPart.Eyes, b);
            b = Hair(3);
            b.Ellipsoid(new Vector3(0,.206f,.079f),new Vector3(.084f,.010f,.074f),16,6);
            Set(result, SlopCityHumanPart.Cap, b);
            b = new Builder();
            b.Loft(new[]{new Vector4(1.025f,.073f,.041f,-.17f),new Vector4(1.082f,.121f,.061f,-.17f),
                new Vector4(1.285f,.124f,.063f,-.17f),new Vector4(1.358f,.099f,.047f,-.17f),new Vector4(1.378f,.05f,.028f,-.17f)},20,.64f);
            b.Loft(new[]{new Vector4(1.085f,.081f,.017f,-.232f),new Vector4(1.11f,.094f,.027f,-.232f),
                new Vector4(1.224f,.094f,.027f,-.232f),new Vector4(1.245f,.073f,.014f,-.232f)},16,.62f);
            foreach(float side in new[]{-1f,1f})b.Tube(new Vector3(side*.135f,1.37f,-.13f),new Vector3(side*.12f,1.10f,.12f),.012f,8);
            Set(result, SlopCityHumanPart.Bag, b);
            b = new Builder(); b.Loft(new[]{new Vector4(-.082f,.031f,.006f,.028f),new Vector4(.026f,.031f,.006f,.028f)},8);
            Set(result, SlopCityHumanPart.Phone, b);
            b = new Builder();
            foreach(float side in new[]{-1f,1f})for(int segment=0;segment<10;segment++)
            {
                float y0=Mathf.Lerp(1.10f,1.387f,segment/10f),y1=Mathf.Lerp(1.10f,1.387f,(segment+1)/10f);
                float x0=side*Mathf.Lerp(.089f,.111f,segment/10f),x1=side*Mathf.Lerp(.089f,.111f,(segment+1)/10f);
                b.Quad(OnJacket(x0-.014f,y0,.004f),OnJacket(x0+.014f,y0,.004f),OnJacket(x1+.014f,y1,.004f),OnJacket(x1-.014f,y1,.004f),true);
            }
            for(int segment=0;segment<16;segment++)
            {
                float x0=Mathf.Lerp(-.157f,.157f,segment/16f),x1=Mathf.Lerp(-.157f,.157f,(segment+1)/16f);
                b.Quad(OnJacket(x0,1.15f,.004f),OnJacket(x1,1.15f,.004f),OnJacket(x1,1.179f,.004f),OnJacket(x0,1.179f,.004f),true);
            }
            Set(result, SlopCityHumanPart.VestStripes, b);
            BuildTechnicalWardrobe(result);
            BuildCybernetics(result);
            Color[] colours={C("30383B"),C("404445"),C("465158"),C("5D5952"),C("747368"),C("665B4D"),C("5E4A4B"),C("858276"),C("4F594F"),C("3F4857"),C("92938C"),C("82735B")};
            result.clothing=new Material[colours.Length];
            for(int i=0;i<colours.Length;i++)result.clothing[i]=Material("Crowd_Fabric_"+i,colours[i],0,.11f,true);
            Color[] skin={C("D4B099"),C("B98E73"),C("D3AB85"),C("997059"),C("735143"),C("C89C80")};
            result.skin=new Material[skin.Length];for(int i=0;i<skin.Length;i++)result.skin[i]=Material("Crowd_Skin_"+i,skin[i],0,.29f);
            Color[] hair={C("171B1D"),C("302B29"),C("595653"),C("494033")};
            result.hair=new Material[hair.Length];for(int i=0;i<hair.Length;i++)result.hair[i]=Material("Crowd_Hair_"+i,hair[i],0,.17f);
            result.shoes=Material("Crowd_Footwear",C("222425"),.02f,.20f);
            result.eyes=Material("Crowd_EyeSclera",C("BFB7A9"),0,.45f);
            result.accessories=Material("Crowd_BagsPhones",C("333C3D"),.06f,.36f);
            result.safety=Material("Crowd_ReflectiveTape",C("9EAA98"),.20f,.30f);
            result.armor=Material("Crowd_SecurityCeramic",C("4B5651"),.14f,.30f);
            result.rubber=Material("Crowd_Rubber",C("25292A"),0,.12f);
            result.visor=Material("Crowd_SecurityVisor",C("75664B"),.76f,.64f);
            result.seams=Material("Crowd_Stitching",C("343938"),0,.08f);
            result.accent=Material("Crowd_ServiceInsignia",C("AAA99B"),.18f,.26f);
            result.cyberMetal=Material("Crowd_CyborgBrushedTitanium",C("8F9690"),.82f,.48f);
            result.cyberJoint=Material("Crowd_CyborgCarbonMechanism",C("20282A"),.36f,.34f);
            result.cyberSignal=Material("Crowd_CyborgDiagnosticOptic",C("69B4A1"),.24f,.50f);
            SetCyberSignalEmission(result.cyberSignal);
            AssetDatabase.SaveAssets();return result;
        }

        public static void RefreshSavedCyberSignal()
        {
            Material material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Crowd_CyborgDiagnosticOptic.mat");
            if(!material)throw new InvalidOperationException("Refresh the cyborg visual library before its diagnostic optic.");
            SetCyberSignalEmission(material);AssetDatabase.SaveAssets();
        }
        // Rebuild the anatomical hand alone without changing any mesh identity,
        // saved scene reference, or unrelated authored body/material asset.
        public static void RefreshSavedHumanHands()
        {
            EnsureFolder();
            var library = new SlopCityCrowdVisuals { parts = new Mesh[Enum.GetValues(typeof(SlopCityHumanPart)).Length] };
            Set(library, SlopCityHumanPart.Hand, Hand()); AssetDatabase.SaveAssets();
        }
        internal static Mesh HandMeshForValidation() => Hand().Mesh();
        private static Builder Hand()
        {
            var b = new Builder();
            b.Loft(new[]{R(.020f,.026f,.022f),R(-.014f,.030f,.019f),R(-.032f,.031f,.019f)},16,1,0,false);
            b.Ellipsoid(new Vector3(0,-.034f,.004f), new Vector3(.030f,.037f,.016f),16,12);
            for(int finger=0;finger<4;finger++)
            {
                float x=-.022f+finger*.014f, length=finger==0?.031f:finger==3?.030f:.039f;
                b.Ellipsoid(new Vector3(x,-.074f,.011f),new Vector3(.0078f,length,.010f),10,8);
                b.Ellipsoid(new Vector3(x,-.100f,.018f),new Vector3(.007f,.013f,.009f),10,8);
            }
            b.Ellipsoid(new Vector3(-.032f,-.030f,.017f),new Vector3(.010f,.027f,.012f),12,8);
            return b;
        }
        public static void RefreshSavedHumanHead()
        {
            EnsureFolder();
            var library=new SlopCityCrowdVisuals { parts=new Mesh[Enum.GetValues(typeof(SlopCityHumanPart)).Length] };
            Set(library,SlopCityHumanPart.Head,SculptedHead());AssetDatabase.SaveAssets();
        }
        private static void SetCyberSignalEmission(Material material)
        {
            material.SetColor("_EmissionColor",C("36BDA0")*1.15f);
            // Standard validates this flag when importing/saving the material. Keeping
            // its default EmissiveIsBlack would strip the emission keyword on reload.
            // These minute moving optics have no baked or realtime GI contribution.
            material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;
            material.EnableKeyword("_EMISSION");EditorUtility.SetDirty(material);
        }

        /// <summary>Replace only the saved mesh/material library, retaining every pedestrian and route.</summary>
        public static void RefreshSavedCrowd()
        {
            const string scenePath="Assets/Scenes/SlopCity_CouchSpawn.unity";
            var scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Single);
            SlopCityCrowd crowd=Object.FindFirstObjectByType<SlopCityCrowd>();
            if(!crowd)throw new InvalidOperationException("The saved game has no crowd to refresh.");
            int people=crowd.PopulationCount, routes=crowd.Routes.Count;
            SlopCityCrowdVisuals library=Build();
            // The public Configure method also recreates simulation state. Assign just
            // the serialized visual field so even seed timings/IDs remain untouched.
            var serialized=new SerializedObject(crowd);
            SerializedProperty visuals=serialized.FindProperty("visuals");
            SerializedProperty parts=visuals.FindPropertyRelative("parts");
            parts.arraySize=library.parts.Length;
            for(int index=0;index<library.parts.Length;index++)parts.GetArrayElementAtIndex(index).objectReferenceValue=library.parts[index];
            SetMaterials(visuals,"clothing",library.clothing);SetMaterials(visuals,"skin",library.skin);SetMaterials(visuals,"hair",library.hair);
            SetMaterial(visuals,"shoes",library.shoes);SetMaterial(visuals,"eyes",library.eyes);SetMaterial(visuals,"accessories",library.accessories);
            SetMaterial(visuals,"safety",library.safety);SetMaterial(visuals,"armor",library.armor);SetMaterial(visuals,"rubber",library.rubber);
            SetMaterial(visuals,"visor",library.visor);SetMaterial(visuals,"seams",library.seams);SetMaterial(visuals,"accent",library.accent);
            SetMaterial(visuals,"cyberMetal",library.cyberMetal);SetMaterial(visuals,"cyberJoint",library.cyberJoint);SetMaterial(visuals,"cyberSignal",library.cyberSignal);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if(people!=crowd.PopulationCount||routes!=crowd.Routes.Count)throw new InvalidOperationException("A visual refresh changed pedestrian data.");
            EditorUtility.SetDirty(crowd);EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save the refreshed crowd visual library.");
            Debug.Log("SLOP CITY cyborg visual library refreshed; retained "+people+" citizens and "+routes+" routes.");
        }
        private static void SetMaterial(SerializedProperty visuals,string field,Material material)
            =>visuals.FindPropertyRelative(field).objectReferenceValue=material;
        private static void SetMaterials(SerializedProperty visuals,string field,Material[] materials)
        {
            SerializedProperty array=visuals.FindPropertyRelative(field);array.arraySize=materials.Length;
            for(int index=0;index<materials.Length;index++)array.GetArrayElementAtIndex(index).objectReferenceValue=materials[index];
        }

        private static void BuildCybernetics(SlopCityCrowdVisuals result)
        {
            // Small medical implants contour the existing face. The opposite eye,
            // nose, lips and jaw remain human; these are working civilians, not masks.
            var b=new Builder();
            Vector3 Face(float x,float y,float clearance)=>new Vector3(x,y,FaceDepth(x,y)+clearance);
            b.Plate(new[]{Face(.049f,.135f,.004f),Face(.070f,.143f,.004f),Face(.069f,.213f,.004f),
                Face(.050f,.224f,.004f),Face(.040f,.210f,.004f),Face(.046f,.193f,.004f)},.004f,.0015f);
            b.Plate(new[]{Face(.017f,.164f,.003f),Face(.038f,.161f,.003f),Face(.047f,.177f,.003f),
                Face(.041f,.193f,.003f),Face(.016f,.191f,.003f),Face(.010f,.179f,.003f)},.003f,.001f);
            // A cheek anchor stays beside the nasolabial crease rather than replacing the face.
            b.Plate(new[]{Face(.043f,.118f,.004f),Face(.063f,.128f,.004f),Face(.067f,.145f,.004f),
                Face(.051f,.148f,.004f),Face(.038f,.133f,.004f)},.003f,.001f);
            SetPair(result,SlopCityHumanPart.CyberFaceShell,SlopCityHumanPart.CyberFaceShellLeft,b);
            b=new Builder();
            b.Ellipsoid(Face(.027f,.179f,.007f),new Vector3(.013f,.0073f,.0038f),16,8);
            foreach(Vector2 screw in new[]{new Vector2(.058f,.208f),new Vector2(.056f,.144f),new Vector2(.052f,.130f)})
                b.Ellipsoid(Face(screw.x,screw.y,.007f),new Vector3(.0023f,.0023f,.0018f),8,6);
            b.Tube(Face(.057f,.145f,.008f),Face(.063f,.163f,.008f),.0015f,6);
            b.Tube(Face(.063f,.163f,.008f),Face(.061f,.196f,.008f),.0015f,6);
            b.Ellipsoid(new Vector3(.080f,.148f,-.007f),new Vector3(.007f,.016f,.018f),12,8);
            SetPair(result,SlopCityHumanPart.CyberFaceHardware,SlopCityHumanPart.CyberFaceHardwareLeft,b);
            b=new Builder();
            b.Ellipsoid(Face(.027f,.179f,.011f),new Vector3(.0042f,.0042f,.0022f),12,8);
            for(int diode=0;diode<3;diode++)b.Ellipsoid(Face(.058f,.183f+diode*.005f,.010f),new Vector3(.0015f,.0015f,.001f),8,5);
            SetPair(result,SlopCityHumanPart.CyberFaceSignal,SlopCityHumanPart.CyberFaceSignalLeft,b);
            b=new Builder();
            // The throat itself stays skin. Two recessed connector discs and service
            // leads are attached to the lower neck, behind the jacket collar.
            foreach(float side in new[]{-1f,1f})
            {
                b.Ellipsoid(new Vector3(side*.042f,.014f,-.025f),new Vector3(.009f,.014f,.013f),12,8);
                b.Tube(new Vector3(side*.044f,.030f,-.035f),new Vector3(side*.046f,-.042f,-.031f),.0027f,8);
            }
            Set(result,SlopCityHumanPart.CyberNeck,b);

            // Anatomical forearm envelope: a visible titanium ulna/radius, shaped
            // shell sections, narrow joint apertures, wrist and elbow bearing races.
            b=new Builder();
            b.Loft(new[]{R(.019f,.030f,.031f),R(-.251f,.023f,.023f)},12);
            b.Loft(new[]{R(.011f,.046f,.045f),R(-.036f,.051f,.047f),R(-.095f,.046f,.042f)},16,.80f);
            b.Loft(new[]{R(-.113f,.044f,.041f),R(-.186f,.037f,.035f),R(-.235f,.031f,.030f)},16,.82f);
            b.Loft(new[]{R(-.240f,.033f,.031f),R(-.264f,.032f,.030f)},16);
            b.Ellipsoid(Vector3.zero,new Vector3(.045f,.040f,.041f),16,10);
            foreach(float side in new[]{-1f,1f})
                b.Tube(new Vector3(side*.039f,-.070f,.011f),new Vector3(side*.028f,-.214f,.014f),.004f,8);
            Set(result,SlopCityHumanPart.CyberForearm,b);
            b=new Builder();
            b.Loft(new[]{R(-.096f,.045f,.043f),R(-.112f,.044f,.042f)},16);
            b.Loft(new[]{R(-.233f,.034f,.032f),R(-.240f,.034f,.032f)},16);
            foreach(float side in new[]{-1f,1f})
            {
                b.Ellipsoid(new Vector3(side*.046f,-.006f,0),new Vector3(.003f,.016f,.017f),12,8);
                b.Tube(new Vector3(side*.020f,-.035f,-.040f),new Vector3(side*.014f,-.217f,-.032f),.003f,8);
                for(int screw=0;screw<3;screw++)b.Ellipsoid(new Vector3(side*.028f,-.133f-screw*.025f,.035f),new Vector3(.002f,.002f,.002f),8,5);
            }
            PlateRect(b,0,-.187f,-.143f,.015f,.038f,.003f,.001f);
            Set(result,SlopCityHumanPart.CyberArmMechanism,b);
            b=new Builder();
            PlateRect(b,0,-.171f,-.162f,.008f,.042f,.001f,.0003f);
            Set(result,SlopCityHumanPart.CyberArmSignal,b);
            b=new Builder();
            // Tendons and knuckle caps sit over the already articulated human hand.
            for(int finger=0;finger<4;finger++)
            {
                float x=-.022f+finger*.014f;
                b.Tube(new Vector3(x,-.024f,.020f),new Vector3(x,-.072f,.026f),.0018f,6);
                b.Ellipsoid(new Vector3(x,-.073f,.027f),new Vector3(.0055f,.006f,.003f),10,6);
                b.Ellipsoid(new Vector3(x,-.101f,.027f),new Vector3(.0048f,.004f,.002f),10,6);
            }
            PlateRect(b,0,-.054f,-.027f,.017f,.025f,.002f,.001f);
            Set(result,SlopCityHumanPart.CyberHandHardware,b);

            b=new Builder();
            b.Loft(new[]{R(.025f,.040f,.039f),R(-.405f,.031f,.031f)},12);
            b.Ellipsoid(Vector3.zero,new Vector3(.062f,.059f,.057f),16,10);
            b.Loft(new[]{R(-.040f,.064f,.060f),R(-.116f,.066f,.063f),R(-.231f,.053f,.047f)},16,.84f);
            b.Loft(new[]{R(-.253f,.048f,.044f),R(-.367f,.041f,.039f),R(-.408f,.044f,.041f)},16,.86f);
            foreach(float side in new[]{-1f,1f})
                b.Tube(new Vector3(side*.048f,-.139f,-.015f),new Vector3(side*.034f,-.370f,-.010f),.005f,8);
            Set(result,SlopCityHumanPart.CyberShin,b);
            b=new Builder();
            foreach(float side in new[]{-1f,1f})
            {
                b.Ellipsoid(new Vector3(side*.062f,0,0),new Vector3(.005f,.024f,.024f),12,8);
                b.Tube(new Vector3(side*.028f,-.088f,-.057f),new Vector3(side*.027f,-.366f,-.038f),.004f,8);
            }
            b.Loft(new[]{R(-.231f,.054f,.049f),R(-.252f,.052f,.047f)},16);
            PlateRect(b,0,-.194f,-.115f,.022f,.061f,.003f,.001f);
            for(int seam=0;seam<3;seam++)PlateRect(b,0,-.332f+seam*.018f,-.330f+seam*.018f,.033f,.042f,.002f,.0005f);
            Set(result,SlopCityHumanPart.CyberShinMechanism,b);
            b=new Builder();PlateRect(b,0,-.139f,-.134f,.010f,.065f,.001f,.0003f);
            Set(result,SlopCityHumanPart.CyberShinSignal,b);
        }
        private static void SetPair(SlopCityCrowdVisuals result,SlopCityHumanPart right,SlopCityHumanPart left,Builder builder)
        {
            Set(result,right,builder);builder.MirrorX();Set(result,left,builder);
        }
        private static float FaceDepth(float x,float y)
        {
            Vector4 p=Section(HeadProfile,y);
            float z=p.w+p.z*Mathf.Sqrt(Mathf.Max(0,1-x*x/(p.y*p.y)));
            float G(float cx,float cy,float sx,float sy)=>Mathf.Exp(-((x-cx)*(x-cx)/(sx*sx)+(y-cy)*(y-cy)/(sy*sy)));
            z+=.014f*G(0,.164f,.010f,.033f)+.030f*G(0,.138f,.015f,.014f);
            z+=.007f*G(-.043f,.145f,.023f,.020f)+.007f*G(.043f,.145f,.023f,.020f);
            z-=.006f*G(-.027f,.179f,.017f,.011f)+.006f*G(.027f,.179f,.017f,.011f);
            z+=.003f*G(-.027f,.195f,.021f,.006f)+.003f*G(.027f,.195f,.021f,.006f);
            // Cupid's bow, lower lip, philtrum, chin and the nasolabial fold belong
            // to the skin surface rather than a painted straight mouth line.
            z+=.0045f*G(0,.110f,.021f,.0045f)+.0055f*G(0,.100f,.024f,.004f);
            z-=.002f*G(0,.105f,.024f,.0015f)+.0018f*G(0,.119f,.004f,.008f);
            z-=.0018f*G(-.028f,.125f,.005f,.019f)+.0018f*G(.028f,.125f,.005f,.019f);
            z-=.002f*G(-.025f,.104f,.004f,.005f)+.002f*G(.025f,.104f,.004f,.005f);
            z+=.007f*G(0,.078f,.027f,.012f);
            return z;
        }
        private static Builder SculptedHead()
        {
            var b=new Builder();const int sides=40,rows=32;
            Vector3 Point(int side,int row)
            {
                float y=Mathf.Lerp(-.068f,.278f,row/(float)rows),a=side*Mathf.PI*2/sides;
                Vector4 p=Section(HeadProfile,y);float x=Mathf.Cos(a)*p.y,front=Mathf.Max(0,Mathf.Sin(a));
                float natural=p.w+p.z*Mathf.Sqrt(Mathf.Max(0,1-x*x/(p.y*p.y)));
                float z=Mathf.Sin(a)*p.z+p.w+(FaceDepth(x,y)-natural)*Mathf.Pow(front,6);
                return new Vector3(x,y,z);
            }
            for(int row=0;row<rows;row++)for(int side=0;side<sides;side++)
                b.Quad(Point(side,row),Point(side,row+1),Point(side+1,row+1),Point(side+1,row));
            foreach(float side in new[]{-1f,1f})
            {
                b.Ellipsoid(new Vector3(side*.075f,.145f,-.006f),new Vector3(.011f,.028f,.020f),16,12);
                b.Ellipsoid(new Vector3(side*.081f,.141f,.002f),new Vector3(.004f,.016f,.011f),12,10);
                for(int step=0;step<16;step++)
                {
                    Vector3 Lid(float t,bool upper)
                    {
                        float u=t*2-1,x=side*.027f+u*.0118f;
                        float arch=Mathf.Sqrt(Mathf.Max(0,1-u*u));
                        float y=.179f+(upper?.005f:-.0037f)*arch+u*side*.0008f;
                        return new Vector3(x,y,FaceDepth(x,y)+.007f);
                    }
                    b.Tube(Lid(step/16f,true),Lid((step+1)/16f,true),.0014f,6);
                    b.Tube(Lid(step/16f,false),Lid((step+1)/16f,false),.0011f,6);
                }
            }
            // The main head has metre-scale anatomical sections: its row spacing
            // cannot resolve millimetre lip rolls. Add one small conforming skin
            // patch for each lip, without another material, draw or NPC object.
            const int lipSegments=24,lipRows=3;
            foreach(bool upper in new[]{true,false})
            {
                Vector3 Lip(int segment,int row)
                {
                    float u=segment/(float)lipSegments*2-1,x=u*.025f,v=row/(float)lipRows;
                    float taper=Mathf.Sqrt(Mathf.Max(0,1-u*u));
                    float seam=.105f+.0014f*Mathf.Cos(x/.025f*Mathf.PI)-.0011f*Mathf.Exp(-x*x/.000025f);
                    float y=seam+(upper?1:-1)*(.0007f+v*(upper?.0055f:.0065f)*taper);
                    float roll=Mathf.Sin(v*Mathf.PI)*(upper?.0026f:.0031f)*taper;
                    return new Vector3(x,y,FaceDepth(x,y)+.0004f+roll);
                }
                for(int row=0;row<lipRows;row++)for(int segment=0;segment<lipSegments;segment++)
                    b.Quad(Lip(segment,row),Lip(segment+1,row),Lip(segment+1,row+1),Lip(segment,row+1),true);
            }
            return b;
        }
        private static void BuildTechnicalWardrobe(SlopCityCrowdVisuals result)
        {
            var b=new Builder();
            // Zips, shoulder yokes and pocket stitching are shallow sewn features, not large shiny bands.
            for(int step=0;step<16;step++)
            {
                float a=Mathf.Lerp(1.02f,1.406f,step/16f),c=Mathf.Lerp(1.02f,1.406f,(step+1)/16f);
                b.Tube(OnJacket(0,a,.006f),OnJacket(0,c,.006f),.0017f,6);
                Vector3 backA=OnJacket(0,a,.0055f),backC=OnJacket(0,c,.0055f);backA.z=-backA.z;backC.z=-backC.z;
                b.Tube(backA,backC,.0011f,6);
            }
            for(int step=0;step<18;step++)
            {
                float x0=Mathf.Lerp(-.172f,.172f,step/18f),x1=Mathf.Lerp(-.172f,.172f,(step+1)/18f);
                Vector3 a=OnJacket(x0,1.325f,.006f),c=OnJacket(x1,1.325f,.006f);a.z=-a.z;c.z=-c.z;
                b.Tube(a,c,.0015f,6);
            }
            foreach(float side in new[]{-1f,1f})
                b.Tube(OnJacket(side*.073f,1.075f,.006f),OnJacket(side*.142f,1.081f,.006f),.0018f,6);
            Set(result,SlopCityHumanPart.GarmentSeams,b);
            b=new Builder();
            foreach(float side in new[]{-1f,1f})
            {
                // The garment is curved: a broad planar plate cut through its center and
                // left large triangular patches. Follow the actual torso with a sewn pocket.
                ClothPanel(b,new Vector2(side*.048f,1.225f),new Vector2(side*.130f,1.225f),
                    new Vector2(side*.124f,1.294f),new Vector2(side*.047f,1.297f),.009f);
                ClothPanel(b,new Vector2(side*.048f,1.287f),new Vector2(side*.124f,1.287f),
                    new Vector2(side*.124f,1.301f),new Vector2(side*.047f,1.303f),.011f);
            }
            Set(result,SlopCityHumanPart.GarmentPanels,b);
            b=new Builder();
            foreach(float side in new[]{-1f,1f})
            {
                Vector3[] p={new Vector3(side*.014f,1.203f,.132f),new Vector3(side*.151f,1.225f,.112f),
                    new Vector3(side*.176f,1.339f,.099f),new Vector3(side*.092f,1.392f,.095f),new Vector3(side*.017f,1.361f,.123f)};
                b.Plate(p,.018f,.006f);b.Plate(p,.018f,.006f,true);
            }
            PlateRect(b,0,1.075f,1.135f,.134f,.121f,.016f,.005f);
            PlateRect(b,0,1.150f,1.194f,.142f,.125f,.016f,.005f);
            PlateRect(b,0,1.09f,1.18f,.124f,.12f,.016f,.005f,true);
            Set(result,SlopCityHumanPart.ArmorTorso,b);
            b=new Builder();
            PlateRect(b,0,-.102f,.012f,.052f,.065f,.012f,.004f);
            PlateRect(b,0,-.090f,.012f,.048f,.061f,.010f,.003f,true);
            Set(result,SlopCityHumanPart.ArmorShoulder,b);
            b=new Builder();PlateRect(b,0,-.221f,-.069f,.044f,.053f,.014f,.004f);
            Set(result,SlopCityHumanPart.ArmorForearm,b);
            b=new Builder();PlateRect(b,0,-.283f,-.040f,.054f,.070f,.015f,.005f);
            Set(result,SlopCityHumanPart.ArmorShin,b);
            b=Hair(3);
            foreach(float side in new[]{-1f,1f})
            {
                b.Ellipsoid(new Vector3(side*.084f,.165f,-.011f),new Vector3(.016f,.035f,.036f),16,10);
                b.Plate(new[]{new Vector3(side*.060f,.096f,.056f),new Vector3(side*.086f,.148f,.043f),
                    new Vector3(side*.084f,.215f,.052f),new Vector3(side*.062f,.214f,.087f)},.016f,.004f);
            }
            PlateRect(b,0,.082f,.149f,.058f,.119f,.012f,.005f);
            Set(result,SlopCityHumanPart.Helmet,b);
            b=new Builder();
            b.Plate(new[]{new Vector3(-.062f,.151f,.105f),new Vector3(.062f,.151f,.105f),new Vector3(.078f,.193f,.096f),
                new Vector3(.059f,.216f,.105f),new Vector3(-.059f,.216f,.105f),new Vector3(-.078f,.193f,.096f)},.004f,.001f);
            Set(result,SlopCityHumanPart.Visor,b);
            b=new Builder();
            b.Loft(new[]{R(1.018f,.172f,.114f),R(1.049f,.169f,.111f)},24,.9f);
            foreach(float side in new[]{-1f,1f})PlateRect(b,side*.125f,.982f,1.095f,.037f,.099f,.035f,.006f);
            PlateRect(b,0,1.018f,1.052f,.026f,.12f,.009f,.002f);
            Set(result,SlopCityHumanPart.UtilityBelt,b);
            b=new Builder();
            b.Loft(new[]{new Vector4(-.054f,.047f,.112f,.028f),new Vector4(-.046f,.050f,.116f,.028f),
                new Vector4(-.036f,.048f,.112f,.028f)},20,.75f);
            for(int step=0;step<6;step++)
            {
                float z=Mathf.Lerp(-.060f,.131f,step/5f);
                b.Tube(new Vector3(-.047f,-.047f,z),new Vector3(.047f,-.047f,z),.0025f,6);
            }
            Set(result,SlopCityHumanPart.ShoeSole,b);
            b=new Builder();
            foreach(float side in new[]{-1f,1f})
            {
                b.Ellipsoid(new Vector3(side*.097f,.165f,-.011f),new Vector3(.005f,.023f,.024f),12,8);
                b.Tube(new Vector3(side*.072f,.113f,.076f),new Vector3(side*.080f,.152f,.077f),.004f,8);
            }
            for(int vent=0;vent<4;vent++)PlateRect(b,-.0225f+vent*.015f,.103f,.126f,.003f,.126f,.002f,.0005f);
            Set(result,SlopCityHumanPart.HelmetHardware,b);
            b=new Builder();
            for(int bar=0;bar<3;bar++)PlateRect(b,-.071f+bar*.011f,1.284f,1.312f+bar*.003f,.0034f,.142f,.001f,.0003f);
            PlateRect(b,.064f,1.305f,1.319f,.020f,.142f,.001f,.0003f);
            Set(result,SlopCityHumanPart.ChestBadge,b);
        }
        private static void PlateRect(Builder builder,float x,float y0,float y1,float width,float z,float depth,float bevel,bool back=false)
        {
            float cut=Mathf.Min(.012f,Mathf.Min(width*.22f,(y1-y0)*.22f));
            builder.Plate(new[]{new Vector3(x-width+cut,y0,z),new Vector3(x+width-cut,y0,z),new Vector3(x+width,y0+cut,z),
                new Vector3(x+width,y1-cut,z),new Vector3(x+width-cut,y1,z),new Vector3(x-width+cut,y1,z),
                new Vector3(x-width,y1-cut,z),new Vector3(x-width,y0+cut,z)},depth,bevel,back);
        }

        private static Vector4 R(float y,float radiusX,float radiusZ)=>new Vector4(y,radiusX,radiusZ,0);
        private static Builder Limb(Vector4[] rings) { var b=new Builder();b.Loft(rings,16,1,0,false);return b; }
        private static readonly Vector4[] HeadProfile =
        {
            R(-.068f,.041f,.039f),R(.035f,.043f,.041f),new Vector4(.065f,.047f,.057f,.015f),new Vector4(.09f,.066f,.066f,.005f),
            R(.145f,.076f,.074f),R(.195f,.072f,.078f),new Vector4(.238f,.065f,.067f,-.005f),new Vector4(.266f,.043f,.049f,-.008f),new Vector4(.278f,.011f,.022f,-.008f)
        };
        private static readonly Vector4[] JacketProfile =
        {
            R(.965f,.161f,.109f),R(1.04f,.158f,.100f),R(1.15f,.156f,.097f),R(1.27f,.183f,.108f),R(1.355f,.204f,.099f),R(1.401f,.174f,.078f),R(1.442f,.077f,.054f)
        };
        private static Vector4 Section(Vector4[] profile,float y)
        {
            if(y<=profile[0].x)return profile[0];
            for(int i=1;i<profile.Length;i++)if(y<=profile[i].x)
                return Vector4.Lerp(profile[i-1],profile[i],Mathf.InverseLerp(profile[i-1].x,profile[i].x,y));
            return profile[profile.Length-1];
        }
        private static Vector3 OnJacket(float x,float y,float offset)
        {
            Vector4 section=Section(JacketProfile,y);
            float z=section.z*Mathf.Sqrt(Mathf.Max(0,1-x*x/(section.y*section.y)))+section.w;
            return new Vector3(x,y,z+offset);
        }
        private static Builder Torso(bool coat,bool vest)
        {
            var b=new Builder();var profile=(Vector4[])JacketProfile.Clone();
            if(coat)profile[0]=R(.73f,.196f,.115f);
            b.Loft(profile,28,1,.0028f);
            // Shallow cloth folds stay outside the actual torso surface; each has one outward face.
            foreach(float side in new[]{-1f,1f})
            {
                ClothPanel(b,new Vector2(side*.050f,1.395f),new Vector2(side*.119f,1.414f),
                    new Vector2(side*.094f,1.27f),new Vector2(side*.036f,1.20f),.006f);
                ClothPanel(b,new Vector2(side*.070f,1.074f),new Vector2(side*.142f,1.078f),
                    new Vector2(side*.142f,1.092f),new Vector2(side*.070f,1.088f),.004f);
            }
            // A shaped stand collar covers the lower neck and joins the shoulder line.
            b.Loft(new[]{R(1.401f,.077f,.062f),R(1.453f,.064f,.055f),R(1.474f,.059f,.052f)},20);
            if(vest)b.Loft(new[]{R(1.005f,.174f,.119f),R(1.035f,.176f,.12f)},20);
            return b;
        }
        private static void ClothPanel(Builder builder,Vector2 a,Vector2 b,Vector2 c,Vector2 d,float offset)
        {
            Vector3 Point(int column,int row)
            {
                Vector2 p=Vector2.Lerp(Vector2.Lerp(a,b,column/6f),Vector2.Lerp(d,c,column/6f),row/8f);
                return OnJacket(p.x,p.y,offset);
            }
            for(int row=0;row<8;row++)for(int column=0;column<6;column++)
                builder.Quad(Point(column,row),Point(column+1,row),Point(column+1,row+1),Point(column,row+1),true);
        }
        private static Builder Hair(int style)
        {
            var b=new Builder();const int segments=24, rows=16;
            Vector3 Point(int segment,int row)
            {
                float a=segment*Mathf.PI*2/segments,v=row/(float)rows,front=Mathf.Max(0,Mathf.Sin(a));
                float low=style==1?Mathf.Lerp(.073f,.217f,front*front):Mathf.Lerp(style==2?.154f:.147f,.212f,front);
                low+=Mathf.Sin(a*5+.6f)*.006f+Mathf.Sin(a*9)*.003f;
                if(style==3)
                {
                    float crown=Mathf.Sqrt(Mathf.Max(0,1-v*v));
                    return new Vector3(Mathf.Cos(a)*.084f*crown,.204f+v*.080f,Mathf.Sin(a)*.089f*crown-.006f);
                }
                float top=style==3?.303f:.290f,y=Mathf.Lerp(low,top,v);
                // The shell follows the skull's real section profile, rather than shrinking by strand length.
                Vector4 skull=Section(HeadProfile,Mathf.Max(style==1?.145f:.09f,y-.006f));
                float clearance=style==3?.010f:style==1?.010f:.006f;
                if(style!=3)clearance+=Mathf.Sin(a*13+y*7)*.0015f;
                float taper=y>.282f?Mathf.Sqrt(Mathf.Clamp01((top-y)/(top-.282f))):1;
                return new Vector3(Mathf.Cos(a)*(skull.y+clearance)*taper,y,Mathf.Sin(a)*(skull.z+clearance)*taper+skull.w);
            }
            for(int row=0;row<rows;row++)for(int col=0;col<segments;col++)b.Quad(Point(col,row),Point(col,row+1),Point(col+1,row+1),Point(col+1,row));
            if(style==2)
            {
                b.Loft(new[]{new Vector4(.179f,.030f,.028f,-.097f),new Vector4(.119f,.033f,.028f,-.109f),
                    new Vector4(.057f,.025f,.023f,-.123f),new Vector4(.004f,.014f,.014f,-.132f),new Vector4(-.015f,.006f,.007f,-.127f)},20);
            }
            return b;
        }
        private static void Set(SlopCityCrowdVisuals result,SlopCityHumanPart part,Builder builder)
        {
            Mesh mesh=builder.Mesh();mesh.name="Crowd_"+part;
            string path=Folder+"/"+mesh.name+".asset";Mesh existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);mesh=existing;}
            else AssetDatabase.CreateAsset(mesh,path);
            result.parts[(int)part]=mesh;
        }
        private static Color C(string hex){ColorUtility.TryParseHtmlString("#"+hex,out Color color);return color;}
        private static readonly Dictionary<string,Texture2D> SurfaceMaps=new Dictionary<string,Texture2D>();
        private static Material Material(string name,Color color,float metallic,float gloss,bool fabric=false)
        {
            string path=Folder+"/"+name+".mat";Material material=AssetDatabase.LoadAssetAtPath<Material>(path);bool create=!material;
            if(create)material=new Material(Shader.Find("Standard"));
            string surface=fabric?"Textile":name.Contains("Skin")?"Dermal":name.Contains("Hair")?"Hair":name.Contains("Visor")?"Optic":"Utility";
            material.name=name;material.color=color;material.SetFloat("_Metallic",metallic);material.SetFloat("_Glossiness",gloss);material.enableInstancing=true;
            material.mainTexture=SurfaceMap(surface,"Albedo",metallic,gloss);
            material.SetTexture("_BumpMap",SurfaceMap(surface,"Normal",metallic,gloss));
            material.SetTexture("_MetallicGlossMap",SurfaceMap(surface,"Mask",metallic,gloss));
            material.SetFloat("_GlossMapScale",1);material.SetFloat("_SmoothnessTextureChannel",0);
            material.SetFloat("_BumpScale",fabric?.46f:surface=="Dermal"?.075f:surface=="Hair"?.22f:.16f);
            material.EnableKeyword("_NORMALMAP");material.EnableKeyword("_METALLICGLOSSMAP");
            material.mainTextureScale=surface=="Hair"?new Vector2(5,5):surface=="Dermal"?new Vector2(3,3):Vector2.one;
            if(fabric)
            {
                material.SetTexture("_DetailNormalMap",FabricNormal());material.SetFloat("_DetailNormalMapScale",.14f);
                material.SetTextureScale("_DetailAlbedoMap",new Vector2(48,48));material.EnableKeyword("_DETAIL_MULX2");
            }
            else {material.SetTexture("_DetailNormalMap",null);material.DisableKeyword("_DETAIL_MULX2");}
            if(create)AssetDatabase.CreateAsset(material,path);else EditorUtility.SetDirty(material);return material;
        }
        private static Texture2D SurfaceMap(string surface,string channel,float metallic,float smoothness)
        {
            string key="CrowdV2_"+surface+"_"+channel+(channel=="Mask"?"_"+Mathf.RoundToInt(metallic*100)+"_"+Mathf.RoundToInt(smoothness*100):"");
            if(SurfaceMaps.TryGetValue(key,out Texture2D cached)&&cached)return cached;
            const int size=256;var pixels=new Color[size*size];
            float Noise(int x,int y)
            {
                float u=(x+size)%size/(float)size*Mathf.PI*2,v=(y+size)%size/(float)size*Mathf.PI*2;
                return Mathf.PerlinNoise(7+Mathf.Sin(u)*2.2f+Mathf.Cos(v)*.7f,11+Mathf.Cos(u)*2.2f+Mathf.Sin(v)*.7f);
            }
            float Grain(int x,int y)
            {
                uint h=(uint)(((x+size)%size)*374761393+((y+size)%size)*668265263);h=(h^(h>>13))*1274126177;
                return (h&65535)/65535f;
            }
            float Height(int x,int y)
            {
                float n=Noise(x,y);
                if(surface=="Textile")return n*.34f+Mathf.Sin(x*.14f+y*.053f)*Mathf.Sin(y*.103f)*.10f;
                if(surface=="Dermal")return Grain(x,y)*.18f+n*.10f;
                if(surface=="Hair")return Mathf.Sin(x*.63f+Mathf.Sin(y*.031f)*2.4f)*.15f+n*.06f;
                return n*.12f+Grain(x,y)*.03f;
            }
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float n=Noise(x,y),grain=Grain(x,y);
                if(channel=="Normal")
                {
                    Vector3 normal=new Vector3((Height(x-1,y)-Height(x+1,y))*2.5f,(Height(x,y-1)-Height(x,y+1))*2.5f,1).normalized;
                    pixels[y*size+x]=new Color(normal.x*.5f+.5f,normal.y*.5f+.5f,normal.z*.5f+.5f,1);
                }
                else if(channel=="Mask")
                {
                    float roughVariation=surface=="Textile"?.055f:surface=="Dermal"?.045f:.07f;
                    float wear=surface=="Utility"&&grain>.985f?.055f:0;
                    pixels[y*size+x]=new Color(Mathf.Clamp01(metallic+wear),0,0,Mathf.Clamp01(smoothness+(n-.5f)*roughVariation*2-wear));
                }
                else
                {
                    float value=surface=="Dermal"?.96f+(n-.5f)*.04f:surface=="Hair"?.80f+n*.13f+grain*.025f:.84f+n*.12f+(grain-.5f)*.025f;
                    if(surface=="Textile")value+=(Mathf.Sin(x*Mathf.PI*.5f)*Mathf.Cos(y*Mathf.PI*.5f))*.006f;
                    if(surface=="Optic")value=.94f;
                    pixels[y*size+x]=new Color(value,value,value,1);
                }
            }
            string path=Folder+"/"+key+".png";
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,true,channel!="Albedo");texture.SetPixels(pixels);texture.Apply();
            File.WriteAllBytes(path,texture.EncodeToPNG());Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=channel=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.sRGBTexture=channel=="Albedo";importer.mipmapEnabled=true;importer.convertToNormalmap=false;importer.wrapMode=TextureWrapMode.Repeat;
            importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;importer.maxTextureSize=512;importer.textureCompression=TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);SurfaceMaps[key]=texture;return texture;
        }
        private static Texture2D FabricNormal()
        {
            const string path=Folder+"/Crowd_ClothWeave.png";Texture2D texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(texture)return texture;
            const int size=64;texture=new Texture2D(size,size,TextureFormat.RGBA32,true,true){name="Crowd_ClothWeave",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
            var pixels=new Color[size*size];for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float dx=Mathf.Sin(x*Mathf.PI*.5f)*.19f,dy=Mathf.Sin(y*Mathf.PI*.5f)*.19f;Vector3 n=new Vector3(dx,dy,1).normalized;
                pixels[y*size+x]=new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f,1);
            }
            texture.SetPixels(pixels);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.NormalMap;
            importer.sRGBTexture=false;importer.mipmapEnabled=true;importer.convertToNormalmap=false;importer.wrapMode=TextureWrapMode.Repeat;
            importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        private static void EnsureFolder()
        {
            if(!AssetDatabase.IsValidFolder("Assets/Materials"))AssetDatabase.CreateFolder("Assets","Materials");
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Materials","SlopCityCrowd");
        }
        private sealed class Builder
        {
            private readonly List<Vector3> vertices=new List<Vector3>();
            private readonly List<int> triangles=new List<int>();
            private readonly List<Vector2> uvs=new List<Vector2>();
            private readonly List<int> normalGroups=new List<int>();
            private int normalGroup,nextNormalGroup;
            public void MirrorX()
            {
                for(int index=0;index<vertices.Count;index++)
                {
                    Vector3 point=vertices[index];point.x=-point.x;vertices[index]=point;
                    uvs[index]=new Vector2(point.x+point.z,point.y);
                }
                for(int index=0;index<triangles.Count;index+=3)
                {
                    int swap=triangles[index+1];triangles[index+1]=triangles[index+2];triangles[index+2]=swap;
                }
            }
            private void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {
                int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);
                normalGroups.Add(normalGroup);normalGroups.Add(normalGroup);normalGroups.Add(normalGroup);
                uvs.Add(new Vector2(a.x+a.z,a.y));uvs.Add(new Vector2(b.x+b.z,b.y));uvs.Add(new Vector2(c.x+c.z,c.y));
            }
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,bool frontFacing=false)
            {
                // Duplicating opposite faces before normal welding cancels their normals to black.
                if(frontFacing && Vector3.Cross(b-a,c-a).z<0){Triangle(c,b,a);Triangle(d,c,a);}
                else {Triangle(a,b,c);Triangle(a,c,d);}
            }
            public void Loft(Vector4[] rings,int sides=12,float squareness=1,float wrinkles=0,bool caps=true)
            {
                for(int ring=0;ring<rings.Length-1;ring++)for(int side=0;side<sides;side++)
                {
                    Vector3 Point(int r,int s)
                    {
                        float angle=s*Mathf.PI*2/sides;Vector4 p=rings[r];float x=Mathf.Cos(angle),z=Mathf.Sin(angle);
                        float fold=wrinkles*Mathf.Sin(angle*7+p.x*27)*Mathf.Sin(angle*3-p.x*18);
                        return new Vector3(Mathf.Sign(x)*Mathf.Pow(Mathf.Abs(x),squareness)*(p.y+fold),p.x,Mathf.Sign(z)*Mathf.Pow(Mathf.Abs(z),squareness)*(p.z+fold)+p.w);
                    }
                    // Ring order can ascend or descend for anatomical limbs.
                    if(rings[ring+1].x>rings[ring].x)Quad(Point(ring,side),Point(ring+1,side),Point(ring+1,side+1),Point(ring,side+1));
                    else Quad(Point(ring,side+1),Point(ring+1,side+1),Point(ring+1,side),Point(ring,side));
                }
                // Internal articulation joins overlap. End-cap normals there create a
                // rounded bead at each joint, and the cap face can become visible on bends.
                if(!caps)return;
                for(int end=0;end<2;end++)
                {
                    Vector4 p=rings[end==0?0:rings.Length-1];Vector3 center=new Vector3(0,p.x,p.w);
                    bool up=(end==0)==(rings[0].x>rings[rings.Length-1].x);
                    for(int side=0;side<sides;side++)
                    {
                        float a=side*Mathf.PI*2/sides,c=(side+1)*Mathf.PI*2/sides;
                        Vector3 At(float angle)
                        {
                            float x=Mathf.Cos(angle),z=Mathf.Sin(angle);
                            return center+new Vector3(Mathf.Sign(x)*Mathf.Pow(Mathf.Abs(x),squareness)*p.y,0,Mathf.Sign(z)*Mathf.Pow(Mathf.Abs(z),squareness)*p.z);
                        }
                        Vector3 v0=At(a),v1=At(c);
                        if(up)Triangle(center,v1,v0);else Triangle(center,v0,v1);
                    }
                }
            }
            public void Plate(Vector3[] polygon,float depth,float bevel,bool back=false)
            {
                var points=(Vector3[])polygon.Clone();float area=0;
                for(int i=0;i<points.Length;i++){Vector3 a=points[i],b=points[(i+1)%points.Length];area+=a.x*b.y-b.x*a.y;}
                if(area<0)Array.Reverse(points);
                Vector3 center=Vector3.zero;foreach(Vector3 point in points)center+=point;center/=points.Length;
                Vector3 Transform(Vector3 point)=>back?new Vector3(-point.x,point.y,-point.z):point;
                var inner=new Vector3[points.Length];
                for(int i=0;i<points.Length;i++)
                {
                    Vector3 delta=points[i]-center;float inset=Mathf.Clamp01(bevel/Mathf.Max(.001f,new Vector2(delta.x,delta.y).magnitude));
                    inner[i]=Vector3.Lerp(points[i],center,inset)+Vector3.forward*bevel;
                }
                for(int i=0;i<points.Length;i++)
                {
                    int next=(i+1)%points.Length;
                    Triangle(Transform(center+Vector3.forward*bevel),Transform(inner[i]),Transform(inner[next]));
                    Quad(Transform(inner[i]),Transform(points[i]),Transform(points[next]),Transform(inner[next]));
                    Quad(Transform(points[i]-Vector3.forward*depth),Transform(points[next]-Vector3.forward*depth),Transform(points[next]),Transform(points[i]));
                    Triangle(Transform(center-Vector3.forward*depth),Transform(points[next]-Vector3.forward*depth),Transform(points[i]-Vector3.forward*depth));
                }
            }
            public void Ellipsoid(Vector3 center,Vector3 radius,int sides=14,int rows=10)
            {
                // Weld a surface's UV/triangle seams, never two disconnected shells.
                // Touching fingertips have opposing normals at the same position;
                // combining them produced zero normals and invalid lit HDR pixels.
                int previousGroup=normalGroup;normalGroup=++nextNormalGroup;
                try
                {
                    Vector3 Point(int s,int r){float a=s*Mathf.PI*2/sides,b=r*Mathf.PI/rows;return center+Vector3.Scale(new Vector3(Mathf.Cos(a)*Mathf.Sin(b),Mathf.Cos(b),Mathf.Sin(a)*Mathf.Sin(b)),radius);}
                    for(int row=0;row<rows;row++)for(int side=0;side<sides;side++)Quad(Point(side,row),Point(side+1,row),Point(side+1,row+1),Point(side,row+1));
                }
                finally { normalGroup=previousGroup; }
            }
            public void Tube(Vector3 from,Vector3 to,float radius,int sides)
            {
                Quaternion q=Quaternion.FromToRotation(Vector3.up,(to-from).normalized);
                for(int i=0;i<sides;i++)
                {
                    Vector3 a=q*new Vector3(Mathf.Cos(i*Mathf.PI*2/sides),0,Mathf.Sin(i*Mathf.PI*2/sides))*radius;
                    Vector3 b=q*new Vector3(Mathf.Cos((i+1)*Mathf.PI*2/sides),0,Mathf.Sin((i+1)*Mathf.PI*2/sides))*radius;
                    Quad(from+a,to+a,to+b,from+b);
                }
            }
            public Mesh Mesh()
            {
                var mesh=new Mesh();if(vertices.Count>65535)mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,uvs);mesh.RecalculateNormals();
                Vector3[] normals=mesh.normals;var sums=new Dictionary<(Vector3 position,int group),Vector3>();
                for(int i=0;i<vertices.Count;i++){var key=(Quantized(vertices[i]),normalGroups[i]);sums.TryGetValue(key,out Vector3 sum);sums[key]=sum+normals[i];}
                for(int i=0;i<vertices.Count;i++)normals[i]=sums[(Quantized(vertices[i]),normalGroups[i])].normalized;
                mesh.normals=normals;mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
            }
            private static Vector3 Quantized(Vector3 p)=>new Vector3(Mathf.Round(p.x*100000),Mathf.Round(p.y*100000),Mathf.Round(p.z*100000));
        }
    }
}
#endif
