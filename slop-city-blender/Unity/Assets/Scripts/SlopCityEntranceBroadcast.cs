using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>A permanent street display; decoding and pictures belong to the existing city media pool.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityEntranceBroadcast : MonoBehaviour
    {
        public const string RootName = "Entrance_City_Broadcast";
        public const float Width = 1.9f, Height = 1.07f, PictureBottom = 3.10f;
        private SlopCityCityScreen screen;
        private SlopCityCityMedia registeredMedia;
        public SlopCityCityScreen Screen => screen;

        public static SlopCityEntranceBroadcast Attach(Transform entrance, float depth)
        {
            if (!entrance || float.IsNaN(depth) || float.IsInfinity(depth) || depth <= 0f) return null;
            Transform existing = entrance.Find(RootName);
            if (existing) return existing.GetComponent<SlopCityEntranceBroadcast>();
            Transform root = SlopCityInteriorGeometry.Root(entrance, RootName,
                new Vector3(0f, PictureBottom + Height * .5f, -depth * .5f - .15f));
            var broadcast = root.gameObject.AddComponent<SlopCityEntranceBroadcast>();
            // The frame's lower edge is 3.05 m, above the entrance lettering and
            // the complete door swing. Its shared steel finish owns no extra material.
            SlopCityInteriorGeometry.Box(root, "Entrance_Broadcast_Frame", new Vector3(0f, 0f, .06f),
                new Vector3(Width + .10f, Height + .10f, .10f), SlopCityInteriorGeometry.Metal, true);
            GameObject picture = GameObject.CreatePrimitive(PrimitiveType.Quad);
            picture.name = "Entrance_Broadcast_Picture";
            picture.transform.SetParent(root, false);
            picture.transform.localPosition = new Vector3(0f, 0f, -.001f);
            picture.transform.localScale = new Vector3(Width, Height, 1f);
            Renderer display = picture.GetComponent<Renderer>();
            display.sharedMaterial = SlopCityInteriorGeometry.Metal;
            display.shadowCastingMode = ShadowCastingMode.Off;
            display.receiveShadows = false;
            broadcast.screen = picture.AddComponent<SlopCityCityScreen>();
            // Unity's unrotated quad faces local -Z, toward the street.
            broadcast.screen.Configure(display, 2, 14f);
            var interaction = picture.AddComponent<SlopCityInteractable>();
            interaction.Configure(SlopCityInteractionKind.Screen, "ENTRANCE / CITY BROADCAST", false);
            interaction.ConfigureInteractionPoint(picture.transform.TransformPoint(new Vector3(0f, -.5f, 0f)));
            return broadcast;
        }

        private void Start()
        {
            // Before media Start, discovery picks up this screen once its channels
            // exist. After media Start, registration admits it immediately. The
            // manager's duplicate guard makes both initialization orders safe.
            registeredMedia = SlopCityCityMedia.Instance;
            if (registeredMedia) registeredMedia.RegisterScreen(screen);
        }

        private void OnDestroy()
        {
            SlopCityCityMedia media = registeredMedia ? registeredMedia : SlopCityCityMedia.Instance;
            if (media) media.UnregisterScreen(screen);
        }
    }
}
