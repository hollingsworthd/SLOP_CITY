using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>A fixed physical portrait broadcast facing the home exit; playback belongs to the city pool.</summary>
    public sealed class SlopCityFeaturedBillboard : MonoBehaviour
    {
        public const string RootName = "SlopCity_Home_Featured_Billboard";
        public const string Creator = "1984drh";
        public const string PostId = "7696938454575238430";
        public const float HoldAtSeconds = -1f, HoldSeconds = 0f;
        public const float Width = 5.4f, Height = 9.6f;
        private Material frameMaterial;
        public SlopCityCityScreen Screen { get; private set; }

        public static SlopCityFeaturedBillboard Ensure(Transform city)
        {
            if (!city || city.name != "SlopCity_NineBlocks") return null;
            Transform existing = city.Find(RootName);
            if (existing) return existing.GetComponent<SlopCityFeaturedBillboard>();
            Transform marker = city.Find("CityPath_HomeSidewalk");
            if (!marker) return null;
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            if (!shader) return null;
            var owner = new GameObject(RootName);
            owner.transform.SetParent(city, false);
            // The west face looks toward the actual east-facing loft doorway. Supports
            // sit outside its three-metre walkway; the picture starts above walking headroom.
            owner.transform.position = marker.position + Vector3.up * (7.0f - marker.position.y);
            owner.transform.rotation = Quaternion.Euler(0, 90f, 0);
            var billboard = owner.AddComponent<SlopCityFeaturedBillboard>();
            billboard.frameMaterial = new Material(shader) { name = "Featured broadcast charcoal steel", color = new Color(.025f, .035f, .042f) };
            if (billboard.frameMaterial.HasProperty("_Metallic")) billboard.frameMaterial.SetFloat("_Metallic", .65f);
            if (billboard.frameMaterial.HasProperty("_Glossiness")) billboard.frameMaterial.SetFloat("_Glossiness", .25f);
            billboard.Box("Featured_Back", new Vector3(0, 0, .10f), new Vector3(Width + .22f, Height + .22f, .20f));
            billboard.Box("Featured_Left_Support", new Vector3(-Width * .5f - .22f, -3.45f, .12f), new Vector3(.20f, 6.9f, .26f));
            billboard.Box("Featured_Right_Support", new Vector3(Width * .5f + .22f, -3.45f, .12f), new Vector3(.20f, 6.9f, .26f));
            GameObject picture = GameObject.CreatePrimitive(PrimitiveType.Quad);
            picture.name = "Featured_Creator_Portrait";
            picture.transform.SetParent(owner.transform, false);
            picture.transform.localPosition = new Vector3(0, 0, -.012f);
            picture.transform.localScale = new Vector3(Width, Height, 1f);
            Renderer display = picture.GetComponent<Renderer>(); display.sharedMaterial = billboard.frameMaterial;
            display.shadowCastingMode = ShadowCastingMode.Off; display.receiveShadows = false;
            billboard.Screen = picture.AddComponent<SlopCityCityScreen>();
            billboard.Screen.Configure(display, 0, 14f);
            billboard.Screen.ConfigureFeaturedPost(Creator, PostId, HoldAtSeconds, HoldSeconds);
            var interaction = picture.AddComponent<SlopCityInteractable>();
            interaction.Configure(SlopCityInteractionKind.Screen, "FEATURED CREATOR BROADCAST", false);
            interaction.ConfigureInteractionPoint(picture.transform.TransformPoint(new Vector3(0, -.5f, 0)));
            return billboard;
        }

        private void Box(string label, Vector3 position, Vector3 size)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = label;
            box.transform.SetParent(transform, false); box.transform.localPosition = position; box.transform.localScale = size;
            Renderer renderer = box.GetComponent<Renderer>(); renderer.sharedMaterial = frameMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        private void OnDestroy() { if (frameMaterial) Destroy(frameMaterial); }
    }
}
