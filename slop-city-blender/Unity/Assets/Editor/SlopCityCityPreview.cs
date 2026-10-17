#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SlopCity.Editor
{
    /// <summary>Offscreen renders of the generated environment for asset review.</summary>
    public static class SlopCityCityPreview
    {
        public static void Render()
        {
            EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath);
            var cameraObject = new GameObject("City_AssetReviewCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.075f,.115f,.14f);
            camera.nearClipPlane = .05f; camera.farClipPlane = 400f;
            camera.fieldOfView = 52f; camera.allowHDR = true;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath,"../../renders"));
            Directory.CreateDirectory(folder);
            camera.fieldOfView = 42f;
            Capture(camera, new Vector3(117,110,-134), new Vector3(0,0,0), Path.Combine(folder,"04-city-overview.png"));
            camera.fieldOfView = 52f;
            Capture(camera, new Vector3(20,1.72f,-9), new Vector3(20,1.72f,32), Path.Combine(folder,"05-city-street.png"));
            Capture(camera, new Vector3(1.8f,1.7f,0), new Vector3(6,1.6f,0), Path.Combine(folder,"06-home-door.png"));
            Object.DestroyImmediate(cameraObject);
            Debug.Log("SLOP_CITY_ENVIRONMENT_RENDERS_COMPLETE " + folder);
        }

        private static void Capture(Camera camera, Vector3 position, Vector3 target, string path)
        {
            camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position,Vector3.up));
            var buffer = new RenderTexture(1600,1100,24,RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1600,1100,TextureFormat.RGB24,false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = buffer;
                camera.Render();
                RenderTexture.active = buffer;
                pixels.ReadPixels(new Rect(0,0,1600,1100),0,0);
                pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                buffer.Release();
                Object.DestroyImmediate(buffer);
                Object.DestroyImmediate(pixels);
            }
        }
    }
}
#endif
