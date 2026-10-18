using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>Room TVs and radios are physical clients of the shared city media pool.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityInteriorBroadcast : MonoBehaviour
    {
        public const float PictureWidth = 1.25f, PictureHeight = .703125f;
        private const float RoomInnerFace = 1.21f;
        private SlopCityCityScreen screen;
        private SlopCityCityMedia registeredMedia;
        public SlopCityCityScreen Screen => screen;
        public bool IsRadio { get; private set; }

        public static void AttachRoom(Transform floor, string roomId, int side, float width,
            float south, float north, int channel, bool withRadio)
        {
            if (!floor || string.IsNullOrEmpty(roomId) || (side != -1 && side != 1)
                || float.IsNaN(width) || float.IsInfinity(width) || width < 4f
                || float.IsNaN(south) || float.IsInfinity(south) || float.IsNaN(north)
                || float.IsInfinity(north) || north - south < 5f) return;
            // High wall mounts clear the actual 2.15m wardrobes and 2.06m
            // bookcases, while staying below the 3.04m ceiling. Every room has
            // a real picture. The west/north sanitary room keeps
            // its TV in the dry staff/laundry area, south of the enclosed bath.
            float z = side < 0 && roomId == "WestNorth" ? south + 1.35f : (south + north) * .5f;
            Attach(floor, "Room_Broadcast_" + roomId,
                new Vector3(side * (RoomInnerFace + width - .10f), 2.57f, z), side, channel, false);
            // Only the two east rooms receive radios: never inside a bathroom.
            // A shallow high wall shelf clears bookcases/worktops. Its offset
            // leaves the TV picture unobstructed and keeps desk approaches free.
            if (withRadio && side > 0)
                Attach(floor, "Room_Radio_" + roomId,
                    new Vector3(side * (RoomInnerFace + width - .10f), 2.25f, (south + north) * .5f - 1.10f), side, channel, true);
        }

        private static SlopCityInteriorBroadcast Attach(Transform floor, string name,
            Vector3 point, int side, int channel, bool radio)
        {
            Transform existing = floor.Find(name);
            if (existing) return existing.GetComponent<SlopCityInteriorBroadcast>();
            Transform root = SlopCityInteriorGeometry.Root(floor, name, point);
            root.localRotation = Quaternion.Euler(0f, side * 90f, 0f);
            var prop = root.gameObject.AddComponent<SlopCityInteriorBroadcast>();
            prop.IsRadio = radio;
            // Room dimensions retain a .24m facade allowance. A real bracket
            // spans that gap to the inner glazing face instead of floating.
            SlopCityInteriorGeometry.Box(root, "Broadcast_Wall_Bracket", new Vector3(0f, 0f, .19f),
                new Vector3(.22f, .12f, .22f), SlopCityInteriorGeometry.Metal);
            if (radio)
            {
                GameObject body = SlopCityInteriorGeometry.Box(root, "Radio_Cabinet", new Vector3(0f, 0f, -.09f),
                    new Vector3(.48f, .26f, .18f), SlopCityInteriorGeometry.Wood, true);
                SlopCityInteriorGeometry.Box(root, "Radio_Speaker", new Vector3(-.065f, 0f, -.185f),
                    new Vector3(.29f, .19f, .012f), SlopCityInteriorGeometry.Fabric);
                SlopCityInteriorGeometry.Box(root, "Radio_Tuner", new Vector3(.158f, .035f, -.19f),
                    new Vector3(.08f, .07f, .016f), SlopCityInteriorGeometry.Accent);
                SlopCityInteriorGeometry.Box(root, "Radio_Shelf", new Vector3(0f, -.15f, .065f),
                    new Vector3(.54f, .035f, .43f), SlopCityInteriorGeometry.Metal);
                prop.screen = root.gameObject.AddComponent<SlopCityCityScreen>();
                prop.screen.ConfigureAudioOnly();
                prop.screen.Configure(body.GetComponent<Renderer>(), channel, 5f);
                // Audio-only is configured by the shared screen API. The cabinet
                // keeps its shared material and owns no extra video/texture/audio.
                var control = body.AddComponent<SlopCityInteractable>();
                control.Configure(SlopCityInteractionKind.Screen, "ROOM RADIO / PLAY OR PAUSE", false);
                control.ConfigureInteractionPoint(root.TransformPoint(new Vector3(0f, 0f, -.20f)));
            }
            else
            {
                SlopCityInteriorGeometry.Box(root, "Television_Frame", new Vector3(0f, 0f, .045f),
                    new Vector3(PictureWidth + .10f, PictureHeight + .10f, .08f), SlopCityInteriorGeometry.Metal, true);
                GameObject picture = GameObject.CreatePrimitive(PrimitiveType.Quad);
                picture.name = "Television_Picture"; picture.transform.SetParent(root, false);
                picture.transform.localPosition = new Vector3(0f, 0f, -.005f);
                picture.transform.localScale = new Vector3(PictureWidth, PictureHeight, 1f);
                Renderer display = picture.GetComponent<Renderer>();
                display.sharedMaterial = SlopCityInteriorGeometry.Metal;
                display.shadowCastingMode = ShadowCastingMode.Off; display.receiveShadows = false;
                prop.screen = root.gameObject.AddComponent<SlopCityCityScreen>();
                prop.screen.Configure(display, channel, 7f);
                var control = picture.AddComponent<SlopCityInteractable>();
                control.Configure(SlopCityInteractionKind.Screen, "ROOM TV / CITY BROADCAST", false);
                control.ConfigureInteractionPoint(picture.transform.position);
            }
            return prop;
        }

        private void Start()
        {
            registeredMedia = SlopCityCityMedia.Instance;
            if (registeredMedia) registeredMedia.RegisterScreen(screen);
        }

        public void Release()
        {
            SlopCityCityMedia media = registeredMedia ? registeredMedia : SlopCityCityMedia.Instance;
            if (media) media.UnregisterScreen(screen);
            registeredMedia = null;
        }

        public static void ReleaseFloor(Transform floor)
        {
            if (!floor) return;
            foreach (SlopCityInteriorBroadcast prop in floor.GetComponentsInChildren<SlopCityInteriorBroadcast>(true)) prop.Release();
        }
        private void OnDisable() { Release(); }
        private void OnDestroy() { Release(); }
    }
}
