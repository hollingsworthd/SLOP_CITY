#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Reusable authored-scale street meshes, persisted for scene reload and player builds.</summary>
    public static class SlopCityStreetMeshes
    {
        private const string Folder = "Assets/Materials/SlopCityCity/StreetMeshes";

        public static GameObject Shape(Transform parent, string label, Mesh mesh, Material material, bool collision = false)
        {
            var item = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(parent, false);
            item.GetComponent<MeshFilter>().sharedMesh = mesh;
            item.GetComponent<Renderer>().sharedMaterial = material;
            if (collision) { var collider = item.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; collider.convex = true; }
            item.isStatic = true;
            return item;
        }

        public static Mesh SedanBody()
        {
            var b=new Builder();var rings=new List<Vector3[]>();
            const int sections=72;
            for(int i=0;i<=sections;i++)
            {
                float z=-1.96f+i*3.92f/sections;
                float end=Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.35f,1.96f,Mathf.Abs(z)));
                float width=SedanWidth(z);
                float top=HoodHeight(z),lower=.29f;
                foreach(float axle in new[]{-1.17f,1.17f})
                {
                    float dz=Mathf.Abs(z-axle);
                    if(dz<.43f)lower=Mathf.Max(lower,.38f+Mathf.Sqrt(.43f*.43f-dz*dz));
                }
                float height=Mathf.Max(.045f,top-lower);
                var profile=new[]{new Vector2(-.76f,0),new Vector2(.76f,0),new Vector2(.96f,.13f),new Vector2(1,.42f),
                    new Vector2(.998f,.77f),new Vector2(.94f,.97f),new Vector2(.72f,1.04f),new Vector2(.36f,1.095f),
                    new Vector2(0,1.11f),new Vector2(-.36f,1.095f),new Vector2(-.72f,1.04f),new Vector2(-.94f,.97f),
                    new Vector2(-.998f,.77f),new Vector2(-1,.42f),new Vector2(-.96f,.13f)};
                var ring=new Vector3[profile.Length];
                for(int j=0;j<ring.Length;j++)
                {
                    float bow=-Mathf.Sign(z)*.145f*profile[j].x*profile[j].x*end;
                    float y=profile[j].y>1?top+(profile[j].y-1)*.55f:lower+height*profile[j].y;
                    ring[j]=new Vector3(profile[j].x*width,y,z+bow);
                }
                if(rings.Count>0)for(int j=0;j<ring.Length;j++)b.Quad(rings[rings.Count-1][j],rings[rings.Count-1][(j+1)%ring.Length],ring[(j+1)%ring.Length],ring[j]);
                rings.Add(ring);
            }
            for(int j=1;j<rings[0].Length-1;j++) {b.Triangle(rings[0][0],rings[0][j+1],rings[0][j]);b.Triangle(rings[sections][0],rings[sections][j],rings[sections][j+1]);}
            Mesh mesh=b.Mesh();WeldNormals(mesh);return Persist("Sedan_PressedBody",mesh);
        }

        private static float SedanWidth(float z)
        {
            float end=Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.35f,1.96f,Mathf.Abs(z)));
            float fender=.075f*Mathf.Exp(-Mathf.Pow((Mathf.Abs(z)-1.17f)/.43f,2));
            return Mathf.Lerp(.91f+fender,.735f,end);
        }
        private static float HoodHeight(float z) => z>.73f ? Mathf.Lerp(.93f,.685f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.73f,1.96f,z)))
            :z<-.98f?Mathf.Lerp(.925f,.75f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.98f,1.96f,-z))):.93f;
        private static float NoseDepth(float x) => 1.968f-.145f*(x/.735f)*(x/.735f);

        public static Mesh SedanRoof()
        {
            var b=new Builder();
            Vector3 Point(int c,int r)
            {
                float u=c/20f,v=r/10f;
                float z=Mathf.Lerp(-.70f,.24f,v),w=.63f-Mathf.Pow(Mathf.Abs(v-.5f)*2,4)*.018f;
                float x=Mathf.Lerp(-w,w,u),y=1.51f+Mathf.Sin(u*Mathf.PI)*.057f-Mathf.Pow(Mathf.Abs(v-.5f)*2,4)*.015f;
                return new Vector3(x,y,z);
            }
            for(int row=0;row<10;row++)for(int col=0;col<20;col++)
            {
                b.Quad(Point(col,row),Point(col,row+1),Point(col+1,row+1),Point(col+1,row));
                b.Quad(Point(col,row)-Vector3.up*.023f,Point(col+1,row)-Vector3.up*.023f,Point(col+1,row+1)-Vector3.up*.023f,Point(col,row+1)-Vector3.up*.023f);
            }
            for(int col=0;col<20;col++)
            {
                b.Quad(Point(col,0),Point(col+1,0),Point(col+1,0)-Vector3.up*.023f,Point(col,0)-Vector3.up*.023f);
                b.Quad(Point(col+1,10),Point(col,10),Point(col,10)-Vector3.up*.023f,Point(col+1,10)-Vector3.up*.023f);
            }
            for(int row=0;row<10;row++)
            {
                b.Quad(Point(0,row+1),Point(0,row),Point(0,row)-Vector3.up*.023f,Point(0,row+1)-Vector3.up*.023f);
                b.Quad(Point(20,row),Point(20,row+1),Point(20,row+1)-Vector3.up*.023f,Point(20,row)-Vector3.up*.023f);
            }
            Mesh mesh=b.Mesh();WeldNormals(mesh);return Persist("Sedan_Roof",mesh);
        }

        public static Mesh SedanLampUnits(bool housing)
        {
            var b=new Builder();
            foreach(float side in new[]{-1f,1f})for(int strip=0;strip<12;strip++)for(int row=0;row<3;row++)
            {
                Vector3 Point(int c,int r)
                {
                    float u=c/12f,v=r/3f,x=Mathf.Lerp(housing?.257f:.275f,housing?.79f:.77f,u);
                    float lower=.576f+u*.018f-(housing?.008f:0),upper=.734f-u*.030f+(housing?.008f:0);
                    float y=Mathf.Lerp(lower,upper,v),z=NoseDepth(x)+.021f+Mathf.Sin(u*Mathf.PI)*.006f+(housing?0:.008f);
                    return new Vector3(side*x,y,z);
                }
                if(side>0)b.Quad(Point(strip,row),Point(strip+1,row),Point(strip+1,row+1),Point(strip,row+1));
                else b.Quad(Point(strip+1,row),Point(strip,row),Point(strip,row+1),Point(strip+1,row+1));
            }
            Mesh mesh=b.Mesh();WeldNormals(mesh);return Persist(housing?"Sedan_RecessedLampHousing":"Sedan_SweptLampLenses",mesh);
        }

        public static Mesh SedanGrille(bool slats)
        {
            var b=new Builder();
            for(int strip=0;strip<18;strip++)
            {
                float x0=-.54f+strip*1.08f/18,x1=-.54f+(strip+1)*1.08f/18;
                if(slats)for(int row=0;row<6;row++)
                {
                    float y=.397f+row*.031f;
                    b.Tube(new Vector3(x0,y,NoseDepth(x0)+.037f),new Vector3(x1,y,NoseDepth(x1)+.037f),.0045f,.0045f,6);
                }
                else b.Quad(new Vector3(x0,.379f,NoseDepth(x0)+.028f),new Vector3(x1,.379f,NoseDepth(x1)+.028f),new Vector3(x1,.574f,NoseDepth(x1)+.028f),new Vector3(x0,.574f,NoseDepth(x0)+.028f));
            }
            return Persist(slats?"Sedan_CurvedGrilleSlats":"Sedan_GrilleOpening",b.Mesh());
        }

        public static Mesh SedanPanelSeams()
        {
            var b=new Builder();
            foreach(float side in new[]{-1f,1f})
            {
                for(int strip=0;strip<22;strip++)
                {
                    float z0=.80f+strip*1.0f/22,z1=.80f+(strip+1)*1.0f/22;
                    Vector3 Point(float z)
                    {
                        float x=side*Mathf.Lerp(.62f,.39f,z-.80f),q=Mathf.Abs(x)/SedanWidth(z);
                        float crown=q<.36f?Mathf.Lerp(.11f,.095f,q/.36f):Mathf.Lerp(.095f,.04f,(q-.36f)/.36f);
                        return new Vector3(x,HoodHeight(z)+crown*.55f+.003f,z);
                    }
                    b.Tube(Point(z0),Point(z1),.004f,.004f,5);
                }
                Vector3[] seam={new Vector3(side*.90f,.36f,.69f),new Vector3(side*.91f,.77f,.69f),new Vector3(side*.856f,.948f,.62f),new Vector3(side*.856f,.948f,-.24f),new Vector3(side*.91f,.77f,-.25f),new Vector3(side*.90f,.36f,-.25f)};
                for(int i=0;i<seam.Length-1;i++)b.Tube(seam[i],seam[i+1],.0045f,.0045f,5);
            }
            for(int strip=0;strip<26;strip++)
            {
                float x0=-.82f+strip*1.64f/26,x1=-.82f+(strip+1)*1.64f/26;
                b.Tube(new Vector3(x0,.338f,NoseDepth(x0)+.024f),new Vector3(x1,.338f,NoseDepth(x1)+.024f),.005f,.005f,5);
            }
            return Persist("Sedan_BodyPanelGaps",b.Mesh());
        }

        public static Mesh SedanWheelArchLips()
        {
            var b=new Builder();
            foreach(float side in new[]{-1f,1f})foreach(float axle in new[]{-1.17f,1.17f})
                for(int strip=0;strip<32;strip++)
                {
                    float a0=strip*Mathf.PI/32,a1=(strip+1)*Mathf.PI/32;
                    float z0=axle+Mathf.Cos(a0)*.435f,z1=axle+Mathf.Cos(a1)*.435f;
                    Vector3 p0=new Vector3(side*(SedanWidth(z0)+.008f),.40f+Mathf.Sin(a0)*.435f,z0);
                    Vector3 p1=new Vector3(side*(SedanWidth(z1)+.008f),.40f+Mathf.Sin(a1)*.435f,z1);
                    b.Tube(p0,p1,.012f,.012f,7);
                }
            Mesh mesh=b.Mesh();WeldNormals(mesh);return Persist("Sedan_RolledWheelArchLips",mesh);
        }

        public static Mesh SedanGlass()
        {
            var mesh = new Builder();
            foreach(bool front in new[]{true,false})for(int row=0;row<12;row++)for(int col=0;col<24;col++)
            {
                Vector3 Point(int c,int r)
                {
                    float u=c/24f,v=r/12f;
                    float x=Mathf.Lerp(-1,1,u)*Mathf.Lerp(.795f,.612f,v);
                    float y=Mathf.Lerp(.982f,1.493f,v)+Mathf.Sin(u*Mathf.PI)*Mathf.Lerp(.018f,.047f,v);
                    float z=front?Mathf.Lerp(.818f,.238f,v):Mathf.Lerp(-1.092f,-.699f,v);
                    z+=(front?1:-1)*Mathf.Sin(u*Mathf.PI)*.047f*Mathf.Sin((1-v)*Mathf.PI*.5f);
                    return new Vector3(x,y,z);
                }
                if(front)mesh.Quad(Point(col,row),Point(col+1,row),Point(col+1,row+1),Point(col,row+1));
                else mesh.Quad(Point(col+1,row),Point(col,row),Point(col,row+1),Point(col+1,row+1));
            }
            foreach (float side in new[] { -1f, 1f })for(int row=0;row<6;row++)for(int col=0;col<18;col++)
            {
                Vector3 Point(int c,int r)
                {
                    float u=c/18f,v=r/6f;
                    float bottomZ=Mathf.Lerp(-1.045f,.774f,u),topZ=Mathf.Lerp(-.66f,.205f,u);
                    float x=side*(Mathf.Lerp(.805f,.628f,v)+Mathf.Sin(v*Mathf.PI)*.009f);
                    return new Vector3(x,Mathf.Lerp(.981f,1.498f,v),Mathf.Lerp(bottomZ,topZ,v));
                }
                if(side>0)mesh.Quad(Point(col+1,row),Point(col,row),Point(col,row+1),Point(col+1,row+1));
                else mesh.Quad(Point(col,row),Point(col+1,row),Point(col+1,row+1),Point(col,row+1));
            }
            Mesh glazing=mesh.Mesh();WeldNormals(glazing);return Persist("Sedan_Glazing",glazing);
        }

        public static Mesh SedanLampOptics()
        {
            var b=new Builder();
            foreach(float side in new[]{-1f,1f})foreach(float center in new[]{.42f,.655f})
            {
                float radius=center<.5f?.053f:.034f;
                Vector3 Point(float a,float r) {float x=side*(center+Mathf.Cos(a)*r);return new Vector3(x,.651f+Mathf.Sin(a)*r*.88f,NoseDepth(x)+.039f);}
                for(int i=0;i<24;i++)
                {
                    float a=i*Mathf.PI*2/24,c=(i+1)*Mathf.PI*2/24;
                    Vector3 inner=Point(a,radius*.73f),outer=Point(a,radius),nextInner=Point(c,radius*.73f),nextOuter=Point(c,radius);
                    if(side>0)b.Quad(inner,outer,nextOuter,nextInner);else b.Quad(nextInner,nextOuter,outer,inner);
                }
            }
            return Persist("Sedan_ProjectorOptics",b.Mesh());
        }

        public static Mesh SedanTailLamps()
        {
            var b=new Builder();
            foreach(float side in new[]{-1f,1f})for(int strip=0;strip<12;strip++)
            {
                Vector3 Point(int c,bool top)
                {
                    float u=c/12f,x=side*Mathf.Lerp(.29f,.75f,u);
                    return new Vector3(x,(top?.792f:.68f)-u*.036f,-NoseDepth(x)-.018f);
                }
                if(side>0)b.Quad(Point(strip+1,false),Point(strip,false),Point(strip,true),Point(strip+1,true));
                else b.Quad(Point(strip,false),Point(strip+1,false),Point(strip+1,true),Point(strip,true));
            }
            Mesh mesh=b.Mesh();WeldNormals(mesh);return Persist("Sedan_WraparoundTailLenses",mesh);
        }

        public static Mesh Ellipsoid(string name,Vector3 radii)
        {
            var b=new Builder();
            for(int row=0;row<10;row++)for(int col=0;col<24;col++)
            {
                Vector3 Point(int c,int r)
                {
                    float phi=c*Mathf.PI*2/24,theta=r*Mathf.PI/10;
                    return Vector3.Scale(new Vector3(Mathf.Sin(theta)*Mathf.Cos(phi),Mathf.Cos(theta),Mathf.Sin(theta)*Mathf.Sin(phi)),radii);
                }
                b.Quad(Point(col,row),Point(col+1,row),Point(col+1,row+1),Point(col,row+1));
            }
            Mesh mesh=b.Mesh();WeldNormals(mesh);return Persist(name,mesh);
        }

        public static Mesh Tire()
        {
            var b = new Builder();
            const int around = 32, across = 12;
            for (int i = 0; i < around; i++) for (int j = 0; j < across; j++)
            {
                Vector3 Point(int u,int v)
                {
                    float a=u*Mathf.PI*2/around, c=v*Mathf.PI*2/across;
                    float r=.303f+Mathf.Cos(c)*.074f;
                    return new Vector3(Mathf.Sin(c)*.115f,Mathf.Cos(a)*r,Mathf.Sin(a)*r);
                }
                b.Quad(Point(i,j),Point(i+1,j),Point(i+1,j+1),Point(i,j+1));
            }
            Mesh mesh=b.Mesh(); WeldNormals(mesh);
            return Persist("Sedan_RadialTire",mesh);
        }

        public static Mesh Rim()
        {
            var b=new Builder();
            for(int ring=0;ring<32;ring++)for(int section=0;section<8;section++)
            {
                Vector3 Point(int u,int v)
                {
                    float a=u*Mathf.PI*2/32, c=v*Mathf.PI*2/8;
                    float r=.227f+Mathf.Cos(c)*.016f;
                    return new Vector3(Mathf.Sin(c)*.080f,Mathf.Cos(a)*r,Mathf.Sin(a)*r);
                }
                b.Quad(Point(ring,section),Point(ring+1,section),Point(ring+1,section+1),Point(ring,section+1));
            }
            foreach(float side in new[]{-1f,1f})
            {
                float x=side*.082f;
                b.Tube(new Vector3(x-side*.012f,0,0),new Vector3(x,0,0),.065f,.065f,16);
                for(int spoke=0;spoke<5;spoke++)
                {
                    float a=spoke*Mathf.PI*2/5;
                    b.Tube(new Vector3(x,Mathf.Cos(a)*.04f,Mathf.Sin(a)*.04f),
                        new Vector3(x,Mathf.Cos(a+.20f)*.223f,Mathf.Sin(a+.20f)*.223f),.035f,.025f,4);
                }
            }
            return Persist("Sedan_AlloyWheel",b.Mesh());
        }

        public static Mesh TreeBranches(int variant)
        {
            var b=new Builder();
            var random=new System.Random(731+variant*97);
            Vector3[] trunk={Vector3.zero,new Vector3(.045f,1.35f,.025f),new Vector3(-.10f,2.65f,.08f),new Vector3(.08f,3.7f,.02f),new Vector3(-.06f,4.8f,.10f)};
            for(int i=0;i<trunk.Length-1;i++) b.Tube(trunk[i],trunk[i+1],.19f-i*.037f,.15f-i*.036f,10);
            for(int i=0;i<15;i++)
            {
                float a=i*2.39996f+variant*.6f;
                float height=1.85f+(i%5)*.42f;
                float spread=1.05f+(float)random.NextDouble()*.75f;
                Vector3 from=new Vector3(-.04f,height,.06f);
                Vector3 bend=from+new Vector3(Mathf.Cos(a)*spread*.63f,.66f,Mathf.Sin(a)*spread*.63f);
                Vector3 end=from+new Vector3(Mathf.Cos(a)*spread,1.27f+(float)random.NextDouble()*.3f,Mathf.Sin(a)*spread);
                b.Tube(from,bend,.075f,.041f,7); b.Tube(bend,end,.042f,.012f,6);
                for(int twig=0;twig<3;twig++)
                {
                    Vector3 start=Vector3.Lerp(bend,end,.28f+twig*.25f);
                    Vector3 tip=start+new Vector3(Mathf.Cos(a+(twig-1)*.7f)*.55f,.34f,Mathf.Sin(a+(twig-1)*.7f)*.55f);
                    b.Tube(start,tip,.024f,.005f,5);
                }
            }
            return Persist("StreetTree_Branches_"+variant,b.Mesh());
        }

        public static Mesh TreeLeaves(int variant)
        {
            var b=new Builder(); var random=new System.Random(9713+variant*97);
            for(int cluster=0;cluster<26;cluster++)
            {
                float a=cluster*2.39996f+variant*.6f;
                float spread=cluster<21?.85f+(float)random.NextDouble()*.85f:.4f;
                Vector3 center=new Vector3(Mathf.Cos(a)*spread,3.1f+(cluster%6)*.30f,Mathf.Sin(a)*spread);
                for(int leaf=0;leaf<38;leaf++)
                {
                    Vector3 p=center+new Vector3((float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f)*1.18f;
                    Quaternion rotation=Quaternion.Euler((float)random.NextDouble()*80-40,(float)random.NextDouble()*360,(float)random.NextDouble()*60-30);
                    float length=.16f+(float)random.NextDouble()*.17f, width=length*.42f;
                    Vector3 tip=rotation*Vector3.forward*length, side=rotation*Vector3.right*width, ridge=rotation*Vector3.up*.025f;
                    b.Triangle(p-tip*.5f,p+side,p+ridge); b.Triangle(p+side,p+tip,p+ridge);
                    b.Triangle(p+tip,p-side,p+ridge); b.Triangle(p-side,p-tip*.5f,p+ridge);
                    b.Triangle(p+ridge,p+side,p-tip*.5f); b.Triangle(p+ridge,p+tip,p+side);
                    b.Triangle(p+ridge,p-side,p+tip); b.Triangle(p+ridge,p-tip*.5f,p-side);
                }
            }
            return Persist("StreetTree_Foliage_"+variant,b.Mesh());
        }

        public static Mesh TaperedPole() { var b=new Builder(); b.Tube(Vector3.zero,Vector3.up*4.5f,.08f,.042f,12); return Persist("Streetlamp_Taper",b.Mesh()); }

        public static Mesh Curb() => Persist("StreetCurb_Chamfered",Loft(new[]{new Vector4(-15,.08f,-.035f,.035f),new Vector4(15,.08f,-.035f,.035f)},.019f));

        public static Mesh AsphaltPatch(int variant)
        {
            var b=new Builder();var random=new System.Random(519+variant*113);
            var edge=new Vector3[17];
            for(int i=0;i<16;i++) {float a=i*Mathf.PI*2/16, r=.77f+(float)random.NextDouble()*.23f;edge[i]=new Vector3(Mathf.Cos(a)*r*1.75f,0,Mathf.Sin(a)*r*.58f);}
            edge[16]=edge[0];for(int i=0;i<16;i++)b.Triangle(Vector3.zero,edge[i+1],edge[i]);
            return Persist("Asphalt_RepairPatch_"+variant,b.Mesh());
        }

        public static Mesh MarketCanopy()
        {
            var b=new Builder();
            b.Quad(new Vector3(-2.3f,3.05f,1.4f),new Vector3(2.3f,3.05f,1.4f),new Vector3(2.3f,3.48f,0),new Vector3(-2.3f,3.48f,0));
            b.Quad(new Vector3(2.3f,3.05f,-1.4f),new Vector3(-2.3f,3.05f,-1.4f),new Vector3(-2.3f,3.48f,0),new Vector3(2.3f,3.48f,0));
            foreach(float side in new[]{-1f,1f})for(int i=0;i<32;i++)
            {
                float x0=-2.3f+i*4.6f/32,x1=-2.3f+(i+1)*4.6f/32;
                float y0=2.87f-Mathf.Sin(i*Mathf.PI/4)*.032f,y1=2.87f-Mathf.Sin((i+1)*Mathf.PI/4)*.032f;
                if(side>0)b.Quad(new Vector3(x0,y0,side*1.4f),new Vector3(x1,y1,side*1.4f),new Vector3(x1,3.05f,side*1.4f),new Vector3(x0,3.05f,side*1.4f));
                else b.Quad(new Vector3(x1,y1,side*1.4f),new Vector3(x0,y0,side*1.4f),new Vector3(x0,3.05f,side*1.4f),new Vector3(x1,3.05f,side*1.4f));
            }
            b.Triangle(new Vector3(-2.3f,3.05f,-1.4f),new Vector3(-2.3f,3.05f,1.4f),new Vector3(-2.3f,3.48f,0));
            b.Triangle(new Vector3(2.3f,3.05f,1.4f),new Vector3(2.3f,3.05f,-1.4f),new Vector3(2.3f,3.48f,0));
            return Persist("Market_WeatheredCanvasAwning",b.Mesh());
        }

        public static Mesh Ring(string name,float radius,float thickness)
        {
            var b=new Builder();
            for(int ring=0;ring<32;ring++)for(int section=0;section<8;section++)
            {
                Vector3 Point(int u,int v)
                {
                    float a=u*Mathf.PI*2/32,c=v*Mathf.PI*2/8,r=radius+Mathf.Cos(c)*thickness;
                    return new Vector3(Mathf.Cos(a)*r,Mathf.Sin(c)*thickness,Mathf.Sin(a)*r);
                }
                b.Quad(Point(ring,section),Point(ring,section+1),Point(ring+1,section+1),Point(ring+1,section));
            }
            Mesh mesh=b.Mesh();WeldNormals(mesh);return Persist(name,mesh);
        }

        public static void BarkSurface(Material material)
        {
            EnsureFolder();const int width=256,height=512;string path=Folder+"/BarkRelief.asset",colorPath=Folder+"/BarkDiffuse.asset";
            Texture2D normal=AssetDatabase.LoadAssetAtPath<Texture2D>(path),diffuse=AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
            if(!normal||!diffuse)
            {
                float[] values=new float[width*height];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    float warp=Mathf.PerlinNoise(x*.033f,y*.011f)*9;
                    float ridge=Mathf.Pow(Mathf.Abs(Mathf.Sin(x*.24f+warp)),.45f);
                    values[y*width+x]=ridge*.68f+Mathf.PerlinNoise(x*.32f,y*.11f)*.22f;
                }
                var nc=new Color[values.Length];var dc=new Color[values.Length];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    float dx=values[y*width+(x+1)%width]-values[y*width+(x+width-1)%width];
                    float dy=values[((y+1)%height)*width+x]-values[((y+height-1)%height)*width+x];
                    Vector3 n=new Vector3(-dx*3,-dy*3,1).normalized;nc[y*width+x]=new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f,1);
                    float shade=.46f+values[y*width+x]*.54f;dc[y*width+x]=new Color(shade,shade,shade);
                }
                if(!normal) {normal=new Texture2D(width,height,TextureFormat.RGBA32,true,true);normal.name="BarkRelief";normal.SetPixels(nc);normal.Apply();AssetDatabase.CreateAsset(normal,path);}
                if(!diffuse) {diffuse=new Texture2D(width,height,TextureFormat.RGBA32,true,false);diffuse.name="BarkDiffuse";diffuse.SetPixels(dc);diffuse.Apply();AssetDatabase.CreateAsset(diffuse,colorPath);}
            }
            normal.wrapMode=diffuse.wrapMode=TextureWrapMode.Repeat;normal.anisoLevel=diffuse.anisoLevel=4;
            material.mainTexture=diffuse;material.mainTextureScale=new Vector2(2.5f,1f);
            material.SetTexture("_BumpMap",normal);material.SetTextureScale("_BumpMap",new Vector2(2.5f,1));material.SetFloat("_BumpScale",.75f);material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
        }

        public static void Combine(Transform parent, string assetName, Material material)
        {
            var source=new List<MeshFilter>();
            foreach(MeshFilter filter in parent.GetComponentsInChildren<MeshFilter>())
                if(filter.sharedMesh && filter.GetComponent<Renderer>().sharedMaterial==material && !filter.GetComponent<Collider>()) source.Add(filter);
            if(source.Count<2) return;
            var parts=new CombineInstance[source.Count];
            for(int i=0;i<source.Count;i++) parts[i]=new CombineInstance { mesh=source[i].sharedMesh,transform=parent.worldToLocalMatrix*source[i].transform.localToWorldMatrix };
            var mesh=new Mesh {name=assetName}; mesh.CombineMeshes(parts,true,true); mesh.RecalculateBounds();
            Vector3[] vertices=mesh.vertices,normals=mesh.normals;var uv=new Vector2[vertices.Length];
            for(int i=0;i<vertices.Length;i++)
            {
                Vector3 n=normals[i],v=vertices[i];
                uv[i]=Mathf.Abs(n.y)>Mathf.Abs(n.x)&&Mathf.Abs(n.y)>Mathf.Abs(n.z)?new Vector2(v.x,v.z)
                    :Mathf.Abs(n.x)>Mathf.Abs(n.z)?new Vector2(v.z,v.y):new Vector2(v.x,v.y);
            }
            mesh.uv=uv;mesh.RecalculateTangents();
            Shape(parent,assetName,Persist(assetName,mesh),material);
            foreach(MeshFilter filter in source) Object.DestroyImmediate(filter.gameObject);
        }

        public static Mesh Beam(Vector3 from,Vector3 to,float radius,string name)
        { var b=new Builder();b.Tube(from,to,radius,radius,8);return Persist(name,b.Mesh()); }

        public static Material Material(string label,Color color,float metallic,float smoothness,bool transparent=false)
        {
            EnsureFolder();
            string path=Folder+"/"+label+".mat";
            Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create=!material;
            if(create) material=new Material(Shader.Find("Standard"));
            material.name=label;material.color=color;material.SetFloat("_Metallic",metallic);material.SetFloat("_Glossiness",smoothness);material.enableInstancing=true;
            if(transparent)
            {
                material.SetFloat("_Mode",3);material.SetInt("_SrcBlend",(int)BlendMode.One);material.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite",0);material.DisableKeyword("_ALPHATEST_ON");material.DisableKeyword("_ALPHABLEND_ON");material.EnableKeyword("_ALPHAPREMULTIPLY_ON");material.renderQueue=3000;
            }
            if(create) AssetDatabase.CreateAsset(material,path); else EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh Loft(Vector4[] sections,float bevel)
        {
            var b=new Builder();var rings=new Vector3[sections.Length][];
            for(int i=0;i<sections.Length;i++)
            {
                Vector4 s=sections[i]; float inset=Mathf.Min(bevel,(s.w-s.z)*.24f);
                rings[i]=new[]{new Vector3(-s.y+inset,s.z,s.x),new Vector3(s.y-inset,s.z,s.x),
                    new Vector3(s.y,s.z+inset,s.x),new Vector3(s.y,s.w-inset,s.x),new Vector3(s.y-inset,s.w,s.x),
                    new Vector3(-s.y+inset,s.w,s.x),new Vector3(-s.y,s.w-inset,s.x),new Vector3(-s.y,s.z+inset,s.x)};
                if(i==0) continue;
                for(int j=0;j<8;j++) b.Quad(rings[i-1][j],rings[i-1][(j+1)%8],rings[i][(j+1)%8],rings[i][j]);
            }
            for(int j=1;j<7;j++) { b.Triangle(rings[0][0],rings[0][j+1],rings[0][j]);b.Triangle(rings[rings.Length-1][0],rings[rings.Length-1][j],rings[rings.Length-1][j+1]); }
            Mesh mesh=b.Mesh();WeldNormals(mesh);return mesh;
        }

        private static void WeldNormals(Mesh mesh)
        {
            var sums=new Dictionary<Vector3,Vector3>();Vector3[] vertices=mesh.vertices,normals=mesh.normals;
            for(int i=0;i<vertices.Length;i++) { Vector3 key=vertices[i];sums.TryGetValue(key,out Vector3 sum);sums[key]=sum+normals[i]; }
            for(int i=0;i<vertices.Length;i++) normals[i]=sums[vertices[i]].normalized;
            mesh.normals=normals;mesh.RecalculateTangents();
        }

        private static Mesh Persist(string label,Mesh mesh)
        {
            EnsureFolder();string path=Folder+"/"+label+".asset";mesh.name=label;
            Mesh existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing) {EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;}
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        private static void EnsureFolder()
        {
            if(!AssetDatabase.IsValidFolder("Assets/Materials"))AssetDatabase.CreateFolder("Assets","Materials");
            if(!AssetDatabase.IsValidFolder(SlopCityCityBuilder.MaterialsFolder))AssetDatabase.CreateFolder("Assets/Materials","SlopCityCity");
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(SlopCityCityBuilder.MaterialsFolder,"StreetMeshes");
        }

        private sealed class Builder
        {
            private readonly List<Vector3> vertices=new List<Vector3>();private readonly List<Vector2> uv=new List<Vector2>();private readonly List<int> triangles=new List<int>();
            public void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {
                TriangleUv(a,b,c,new Vector2(a.x,a.z),new Vector2(b.x,b.z),new Vector2(c.x,c.z));
            }
            private void TriangleUv(Vector3 a,Vector3 b,Vector3 c,Vector2 ta,Vector2 tb,Vector2 tc)
            {
                int start=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);uv.Add(ta);uv.Add(tb);uv.Add(tc);
                triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
            }
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d) {Triangle(a,b,c);Triangle(a,c,d);}
            public void Tube(Vector3 from,Vector3 to,float startRadius,float endRadius,int sides)
            {
                Quaternion rotation=Quaternion.FromToRotation(Vector3.up,(to-from).normalized);
                for(int i=0;i<sides;i++)
                {
                    float a=i*Mathf.PI*2/sides,c=(i+1)*Mathf.PI*2/sides;
                    Vector3 p=rotation*new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),q=rotation*new Vector3(Mathf.Cos(c),0,Mathf.Sin(c));
                    Vector3 va=from+p*startRadius,vb=to+p*endRadius,vc=to+q*endRadius,vd=from+q*startRadius;
                    float u0=i*2*Mathf.PI*startRadius/sides,u1=(i+1)*2*Mathf.PI*startRadius/sides,v0=from.y,v1=v0+Vector3.Distance(from,to);
                    TriangleUv(va,vb,vc,new Vector2(u0,v0),new Vector2(u0,v1),new Vector2(u1,v1));
                    TriangleUv(va,vc,vd,new Vector2(u0,v0),new Vector2(u1,v1),new Vector2(u1,v0));
                    Triangle(from,from+p*startRadius,from+q*startRadius);Triangle(to,to+q*endRadius,to+p*endRadius);
                }
            }
            public Mesh Mesh()
            {
                var mesh=new Mesh();if(vertices.Count>65535)mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
            }
        }
    }
}
#endif
