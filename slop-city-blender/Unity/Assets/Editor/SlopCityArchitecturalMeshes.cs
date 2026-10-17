#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity.Editor
{
    /// <summary>Metric bevels and swept metalwork; saved meshes survive standalone builds.</summary>
    public static class SlopCityArchitecturalMeshes
    {
        private const string Folder="Assets/Materials/SlopCityCity";
        private static readonly Dictionary<string,Mesh> cache=new Dictionary<string,Mesh>();

        public static GameObject RoundedBox(Transform parent,string name,Vector3 position,Vector3 size,float radius,Material material,bool collision=true)
        {
            EnsureFolder();
            radius=Mathf.Min(radius,Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.45f);
            string key="BevelV2_"+Format(size.x)+"_"+Format(size.y)+"_"+Format(size.z)+"_"+Format(radius);
            if(!cache.TryGetValue(key,out Mesh mesh) || !mesh)
            {
                mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/"+key+".asset");
                if(!mesh) {mesh=BuildRoundedBox(size,radius);mesh.name=key;AssetDatabase.CreateAsset(mesh,Folder+"/"+key+".asset");}
                cache[key]=mesh;
            }
            var obj=new GameObject(name);obj.transform.SetParent(parent,false);obj.transform.localPosition=position;
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=material;
            if(collision) obj.AddComponent<BoxCollider>().size=size;
            obj.isStatic=true;return obj;
        }

        public static GameObject Tube(Transform parent,string name,Vector3[] points,float radius,Material material,bool collision=false)
        {
            EnsureFolder();
            const int sides=10;
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            float length=0;
            for(int point=0;point<points.Length;point++)
            {
                if(point>0) length+=Vector3.Distance(points[point-1],points[point]);
                Vector3 tangent=(points[Mathf.Min(point+1,points.Length-1)]-points[Mathf.Max(0,point-1)]).normalized;
                Vector3 across=Vector3.Cross(tangent,Mathf.Abs(Vector3.Dot(tangent,Vector3.up))>.95f?Vector3.forward:Vector3.up).normalized;
                Vector3 vertical=Vector3.Cross(tangent,across).normalized;
                for(int side=0;side<=sides;side++)
                {
                    float angle=side*Mathf.PI*2/sides;Vector3 normal=across*Mathf.Cos(angle)+vertical*Mathf.Sin(angle);
                    vertices.Add(points[point]+normal*radius);normals.Add(normal);uv.Add(new Vector2(side/(float)sides,length));
                    if(point==0||side==sides) continue;
                    int here=point*(sides+1)+side,previous=here-(sides+1);
                    triangles.AddRange(new[]{previous,previous+1,here,previous+1,here+1,here});
                }
            }
            var mesh=new Mesh {name=name};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();mesh.RecalculateTangents();
            // Include the complete metric path: repeated service-conduit names may have
            // different bends and must never overwrite another instance's saved mesh.
            uint hash=2166136261;
            string signature=radius.ToString("R",CultureInfo.InvariantCulture);
            foreach(Vector3 point in points) signature+="|"+point.x.ToString("R",CultureInfo.InvariantCulture)+","+point.y.ToString("R",CultureInfo.InvariantCulture)+","+point.z.ToString("R",CultureInfo.InvariantCulture);
            foreach(char c in signature) hash=unchecked((hash^c)*16777619);
            string key="TubeShapeV2_"+hash.ToString("x8");
            string path=Folder+"/"+key+".asset";
            if(!cache.TryGetValue(key,out Mesh existing)||!existing) existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing) {Object.DestroyImmediate(mesh);mesh=existing;} else AssetDatabase.CreateAsset(mesh,path);
            cache[key]=mesh;
            var obj=new GameObject(name);obj.transform.SetParent(parent,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial=material;
            if(collision) obj.AddComponent<MeshCollider>().sharedMesh=mesh;
            obj.isStatic=true;return obj;
        }

        private static void EnsureFolder()
        {
            if(!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets","Materials");
            if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Materials","SlopCityCity");
        }
        private static string Format(float value) {return value.ToString("0.###",CultureInfo.InvariantCulture).Replace('.','p');}
        private static Mesh BuildRoundedBox(Vector3 size,float radius)
        {
            Vector3 half=size*.5f,inner=half-Vector3.one*radius;
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            Vector3[] outward={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
            foreach(Vector3 normal in outward)
            {
                Vector3 u=Mathf.Abs(normal.y)>.5f?Vector3.right:Vector3.Cross(Vector3.up,normal);
                Vector3 v=Vector3.Cross(normal,u);
                float hu=Vector3.Dot(new Vector3(Mathf.Abs(u.x),Mathf.Abs(u.y),Mathf.Abs(u.z)),half);
                float hv=Vector3.Dot(new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z)),half);
                float hn=Vector3.Dot(new Vector3(Mathf.Abs(normal.x),Mathf.Abs(normal.y),Mathf.Abs(normal.z)),half);
                float[] us=Axis(hu,radius),vs=Axis(hv,radius);int start=vertices.Count;
                for(int y=0;y<vs.Length;y++) for(int x=0;x<us.Length;x++)
                {
                    Vector3 outer=normal*hn+u*us[x]+v*vs[y];
                    Vector3 clamp=new Vector3(Mathf.Clamp(outer.x,-inner.x,inner.x),Mathf.Clamp(outer.y,-inner.y,inner.y),Mathf.Clamp(outer.z,-inner.z,inner.z));
                    Vector3 smooth=(outer-clamp).normalized;vertices.Add(clamp+smooth*radius);normals.Add(smooth);uv.Add(new Vector2(us[x]+hu,vs[y]+hv));
                    if(x>=us.Length-1||y>=vs.Length-1) continue;
                    int a=start+y*us.Length+x;triangles.AddRange(new[]{a,a+1,a+us.Length,a+1,a+us.Length+1,a+us.Length});
                }
            }
            var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
        }
        private static float[] Axis(float half,float radius)
        {return new[]{-half,-half+radius*.27f,-half+radius,half-radius,half-radius*.27f,half};}
    }
}
#endif
