using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SlopCity
{
    /// <summary>Six fixed physical plaques carrying the supplied, unmodified brand artwork.</summary>
    public sealed class SlopCityBranding : MonoBehaviour
    {
        public const string RootName = "SlopCity_Physical_Branding";
        private Mesh quad;
        private Material cityMaterial, publisherMaterial, backingMaterial;
        public int SignCount => transform.childCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Ensure(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Ensure(scene); }

        public static SlopCityBranding Ensure(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            Transform lounge = null, city = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "SlopCity_Lounge") lounge = root.transform;
                if (root.name == "SlopCity_NineBlocks") city = root.transform;
            }
            // Never install decorations in a unit fixture, menu scene or another game.
            if (!lounge || !city) return null;
            Transform existing = city.Find(RootName);
            if (existing) return existing.GetComponent<SlopCityBranding>();

            Texture2D cityLogo = Resources.Load<Texture2D>("Brand/SlopCityLogo");
            Texture2D publisherLogo = Resources.Load<Texture2D>("Brand/MachinePublishersLogo");
            Shader shader = Resources.Load<Shader>("Brand/BrandSign");
            if (!cityLogo || !publisherLogo || !shader || !shader.isSupported)
            {
                Debug.LogWarning("SLOP CITY: Physical brand artwork is unavailable.");
                return null;
            }
            var owner = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(owner, scene);
            owner.transform.SetParent(city, false);
            var branding = owner.AddComponent<SlopCityBranding>();
            branding.quad = CreateQuad();
            branding.cityMaterial = ArtworkMaterial(shader, cityLogo);
            branding.publisherMaterial = ArtworkMaterial(shader, publisherLogo);
            branding.backingMaterial = new Material(shader)
            {
                name = "Brand plaque charcoal backing", color = new Color(.035f, .047f, .047f, 1f)
            };

            // Lounge's original HRR poster occupies the middle of the west wall.
            // These plaques occupy the empty southern end and the wall left of the TV.
            branding.Add(lounge, "Lounge_SlopCity", new Vector3(-4.135f, 2.10f, -2.05f), Vector3.right, 1.80f, branding.cityMaterial);
            branding.Add(lounge, "Lounge_MachinePublishers", new Vector3(.15f, 2.05f, -3.345f), Vector3.forward, .70f, branding.publisherMaterial);

            // Venues have a rear wall at z=-4.5; the left side carries shelving,
            // while the east-wall poster, doors and service counters stay clear.
            branding.Add(Find(city, "Venue_SignalYard"), "SignalYard_SlopCity", new Vector3(3.10f, 2.05f, -4.345f), Vector3.forward, 2.10f, branding.cityMaterial);
            branding.Add(Find(city, "Venue_MachinePublishers"), "Publishers_MachinePublishers", new Vector3(3.10f, 2.05f, -4.345f), Vector3.forward, 1.15f, branding.publisherMaterial);

            // Left of the metro screens (which begin at x=1.4), below the station
            // nameplates and above the service notice. Lift consoles are at x=-5.5.
            branding.Add(Find(city, "Subway_Station_0"), "SignalYardMetro_SlopCity", new Vector3(-2.20f, -6.35f, -5.23f), Vector3.forward, 2.00f, branding.cityMaterial);
            branding.Add(Find(city, "Subway_Station_1"), "NightMarketMetro_MachinePublishers", new Vector3(-2.20f, -6.30f, -5.23f), Vector3.forward, .90f, branding.publisherMaterial);
            return branding;
        }

        private static Transform Find(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        private void Add(Transform anchor, string label, Vector3 localPoint, Vector3 localOutward, float width, Material artwork)
        {
            if (!anchor) return;
            var plaque = new GameObject(label);
            plaque.transform.SetParent(transform, false);
            plaque.transform.position = anchor.TransformPoint(localPoint);
            Vector3 outward = anchor.TransformDirection(localOutward).normalized;
            plaque.transform.rotation = Quaternion.LookRotation(-outward, anchor.up);
            Texture texture = artwork.mainTexture;
            float height = width * texture.height / texture.width;
            Surface(plaque.transform, "Backing", new Vector3(0, 0, .012f), new Vector2(width + .08f, height + .08f), backingMaterial);
            Surface(plaque.transform, "Official_Artwork", Vector3.zero, new Vector2(width, height), artwork);
        }

        private void Surface(Transform parent, string label, Vector3 point, Vector2 size, Material material)
        {
            var surface = new GameObject(label);
            surface.transform.SetParent(parent, false);
            surface.transform.localPosition = point;
            surface.transform.localScale = new Vector3(size.x, size.y, 1f);
            surface.AddComponent<MeshFilter>().sharedMesh = quad;
            MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static Material ArtworkMaterial(Shader shader, Texture texture)
            => new Material(shader) { name = "Official " + texture.name, mainTexture = texture, color = Color.white };

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "Shared physical brand plaque" };
            mesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0), new Vector3(.5f, -.5f, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private void OnDestroy()
        {
            Release(cityMaterial); Release(publisherMaterial); Release(backingMaterial); Release(quad);
        }

        private static void Release(Object asset)
        {
            if (!asset) return;
            if (Application.isPlaying) Destroy(asset);
            else DestroyImmediate(asset);
        }
    }
}

