using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>Weathered red telephone booths and local TVs at the four interior street junctions.</summary>
    public sealed class SlopCityPhoneBooths : MonoBehaviour
    {
        private Material enamel,metal,display,glass,ivory;
        public static SlopCityPhoneBooths Ensure(SlopCityFirstPerson player)
        {
            var existing=FindFirstObjectByType<SlopCityPhoneBooths>();if(existing)return existing;
            Transform city=GameObject.Find("SlopCity_NineBlocks")?.transform;if(!city)return null;
            var owner=new GameObject("SlopCity_Street_Telephones");owner.transform.SetParent(city,false);
            var phones=owner.AddComponent<SlopCityPhoneBooths>();phones.Build(city);return phones;
        }

        private void Build(Transform city)
        {
            Shader shader=Shader.Find("Standard")??Shader.Find("Unlit/Color");if(!shader)return;
            enamel=new Material(shader){name="London telephone booth red enamel",color=new Color(.64f,.025f,.035f)};
            metal=new Material(shader){name="Telephone brushed silver",color=new Color(.44f,.52f,.57f)};
            display=new Material(shader){name="Unpowered phone display",color=new Color(.055f,.065f,.06f)};
            ivory=new Material(shader){name="Telephone sign ivory",color=new Color(.94f,.91f,.81f)};
            glass=new Material(shader){name="Telephone booth clear glass",color=new Color(.63f,.80f,.84f,.20f)};
            glass.SetFloat("_Mode",3);glass.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);glass.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
            glass.SetInt("_ZWrite",0);glass.EnableKeyword("_ALPHABLEND_ON");glass.renderQueue=(int)RenderQueue.Transparent;
            foreach(Transform junction in city)
            {
                if(!junction.name.StartsWith("CityPath_Intersection_",System.StringComparison.Ordinal))continue;
                var booth=new GameObject("Red_Telephone_Booth_"+junction.name);booth.transform.SetParent(transform,false);
                // Northwest pavement corner: outside both building build-to lines.
                booth.transform.position=junction.position+new Vector3(-SlopCityDenseLayout.SharedLaneWidth*.5f-.30f,.11f,-SlopCityDenseLayout.CrossStreetWidth*.5f-.25f);
                // Keep the established pavement footprint and interaction identity.
                Box(booth.transform,"Red_plinth",new Vector3(0,.06f,0),new Vector3(.80f,.12f,.76f),enamel);
                for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
                    Box(booth.transform,"Red_corner_post",new Vector3(x*.35f,1.13f,z*.31f),new Vector3(.065f,2.14f,.065f),enamel);
                GlazedFace(booth.transform,"Front_glass_door",new Vector3(0,0,-.32f),0);
                GlazedFace(booth.transform,"Rear_glass_panel",new Vector3(0,0,.32f),180);
                GlazedFace(booth.transform,"Left_glass_panel",new Vector3(-.35f,0,0),90);
                GlazedFace(booth.transform,"Right_glass_panel",new Vector3(.35f,0,0),-90);
                Box(booth.transform,"Roof_cornice",new Vector3(0,2.26f,0),new Vector3(.82f,.10f,.78f),enamel);
                var crown=GameObject.CreatePrimitive(PrimitiveType.Sphere);crown.name="Domed_red_crown";crown.transform.SetParent(booth.transform,false);
                crown.transform.localPosition=new Vector3(0,2.30f,0);crown.transform.localScale=new Vector3(.80f,.28f,.74f);
                crown.GetComponent<Renderer>().sharedMaterial=enamel;crown.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                TelephoneSign(booth.transform,new Vector3(0,2.12f,-.355f),0);
                TelephoneSign(booth.transform,new Vector3(0,2.12f,.355f),180);
                TelephoneSign(booth.transform,new Vector3(-.385f,2.12f,0),90);
                TelephoneSign(booth.transform,new Vector3(.385f,2.12f,0),-90);
                Box(booth.transform,"Telephone",new Vector3(0,1.4f,.21f),new Vector3(.42f,.58f,.13f),metal);
                Box(booth.transform,"Unpowered_display",new Vector3(0,1.53f,.135f),new Vector3(.28f,.15f,.025f),display);
                Box(booth.transform,"Handset",new Vector3(-.14f,1.30f,.10f),new Vector3(.065f,.25f,.06f),enamel);
                for(int x=0;x<3;x++)for(int y=0;y<3;y++)Box(booth.transform,"Key",new Vector3(.01f+x*.045f,1.38f-y*.045f,.13f),new Vector3(.025f,.025f,.025f),enamel);

                Box(booth.transform,"Booth_TV_Case",new Vector3(0,1.82f,.095f),new Vector3(.50f,.32f,.16f),metal);
                var picture=GameObject.CreatePrimitive(PrimitiveType.Quad);picture.name="Booth_TV_Picture";picture.transform.SetParent(booth.transform,false);
                picture.transform.localPosition=new Vector3(0,1.82f,.008f);picture.transform.localScale=new Vector3(.44f,.2475f,1);
                var renderer=picture.GetComponent<Renderer>();renderer.sharedMaterial=metal;renderer.shadowCastingMode=ShadowCastingMode.Off;
                var screen=picture.AddComponent<SlopCityCityScreen>();screen.Configure(renderer,2,10f);
                if(SlopCityCityMedia.Instance)SlopCityCityMedia.Instance.RegisterScreen(screen);
                // Shared cleaning owns the finish; the booth cannot be taken and
                // the TV picture remains protected from dust/material overrides.
                var finish=booth.AddComponent<SlopCitySalvageItem>();finish.Configure("phone-booth/"+junction.name,"Old red telephone booth","street-furniture",true,false,1);
                finish.SetCleanProgress(0);

            }
        }
        private void GlazedFace(Transform parent,string name,Vector3 point,float yaw)
        {
            var face=new GameObject(name);face.transform.SetParent(parent,false);face.transform.localPosition=point;face.transform.localRotation=Quaternion.Euler(0,yaw,0);
            Box(face.transform,"Glass",new Vector3(0,1.12f,0),new Vector3(.64f,1.64f,.012f),glass);
            Box(face.transform,"Lower_red_panel",new Vector3(0,.20f,0),new Vector3(.68f,.24f,.045f),enamel);
            Box(face.transform,"Telephone_header",new Vector3(0,2.12f,0),new Vector3(.70f,.24f,.055f),enamel);
            for(int column=-1;column<=1;column+=2)
                Box(face.transform,"Red_glazing_bar",new Vector3(column*.108f,1.12f,-.012f),new Vector3(.022f,1.66f,.025f),enamel);
            for(int row=0;row<5;row++)
                Box(face.transform,"Red_glazing_bar",new Vector3(0,.30f+row*.41f,-.012f),new Vector3(.67f,.025f,.025f),enamel);
        }
        private void TelephoneSign(Transform parent,Vector3 point,float yaw)
        {
            var sign=new GameObject("TELEPHONE_sign");sign.transform.SetParent(parent,false);sign.transform.localPosition=point;sign.transform.localRotation=Quaternion.Euler(0,yaw,0);
            Box(sign.transform,"Ivory_sign",new Vector3(0,0,.004f),new Vector3(.64f,.15f,.016f),ivory);
            var letters=new GameObject("TELEPHONE");letters.transform.SetParent(sign.transform,false);letters.transform.localPosition=new Vector3(0,0,-.007f);
            var text=letters.AddComponent<TextMesh>();text.text="TELEPHONE";text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.fontSize=40;text.characterSize=.025f;text.color=new Color(.07f,.045f,.035f);
        }
        private static void Box(Transform parent,string name,Vector3 point,Vector3 size,Material material)
        {
            var shape=GameObject.CreatePrimitive(PrimitiveType.Cube);shape.name=name;shape.transform.SetParent(parent,false);shape.transform.localPosition=point;shape.transform.localScale=size;
            var renderer=shape.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
        }
        private void OnDestroy(){Release(enamel);Release(metal);Release(display);Release(glass);Release(ivory);}
        private static void Release(Material value){if(!value)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
    }
}
