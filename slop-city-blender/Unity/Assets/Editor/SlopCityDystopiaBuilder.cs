#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity.Editor
{
    /// <summary>Street-scale infrastructure and authored notices tying all districts to the same worn city.</summary>
    public static class SlopCityDystopiaBuilder
    {
        private const string Folder = "Assets/Art/Dystopia";
        private static Material metal, cable, housing, lens, paper;
        private static Material[] notices;

        public static void Build(SlopCityDistrict[] districts)
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            metal = Material("WeatheredSteel", new Color(.35f, .37f, .34f), .65f, .38f);
            cable = Material("CableRubber", new Color(.038f, .042f, .038f), .05f, .26f);
            housing = Material("AgedPlaster", new Color(.64f, .62f, .52f), .05f, .3f);
            lens = Material("SurveillanceLens", new Color(.025f, .05f, .055f), .7f, .95f);
            paper = Material("DiscardedPaper", new Color(.53f, .49f, .36f), 0, .08f);
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/PublicNotices.png");
            var importer = AssetImporter.GetAtPath(Folder + "/PublicNotices.png") as TextureImporter;
            if (importer && (importer.anisoLevel != 8 || !importer.mipmapEnabled))
            { importer.anisoLevel = 8; importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp; importer.SaveAndReimport(); }
            notices = new Material[4];
            for (int index = 0; index < notices.Length; index++)
            {
                notices[index] = Material("PublicNotice_" + index, new Color(.86f, .86f, .82f), 0, .1f);
                notices[index].mainTexture = atlas;
                notices[index].mainTextureScale = new Vector2(.5f, .5f);
                notices[index].mainTextureOffset = new Vector2(index % 2 * .5f, index < 2 ? .5f : 0);
                EditorUtility.SetDirty(notices[index]);
            }
            foreach (SlopCityDistrict district in districts)
            {
                Transform root = Root("Dystopian_Infrastructure", district.transform, Vector3.zero);
                int index = district.Row * 3 + district.Column;
                UtilityPole(root, new Vector3(-15.55f, 0, -8), index);
                UtilityPole(root, new Vector3(-15.55f, 0, 11), index + 1);
                for (int wire = 0; wire < 3; wire++)
                    SaggingWire(root, new Vector3(-15.55f + wire * .19f, 6.30f, -8), new Vector3(-15.55f + wire * .19f, 6.30f, 11), .65f);
                // Small service enclosures live at the curb, away from storefront doors and dispatch markers.
                Transform box = Root("Utility_Junction", root, new Vector3(-14.45f, 0, -6.5f));
                Rounded(box, "Electrical_Cabinet", new Vector3(0, .67f, 0), new Vector3(.68f, 1.34f, .39f), .035f, metal, true);
                Rounded(box, "Cabinet_Door", new Vector3(0, .70f, -.205f), new Vector3(.58f, 1.10f, .025f), .008f, housing);
                for (int vent = 0; vent < 7; vent++)
                    Rounded(box, "Vent_Slot", new Vector3(0, .91f + vent * .037f, -.224f), new Vector3(.39f, .015f, .012f), .003f, cable);
                Rounded(box, "Service_Latch", new Vector3(.20f, .57f, -.237f), new Vector3(.038f, .12f, .032f), .008f, metal);
                Poster(box, "Service_Notice", new Vector3(0, .48f, -.234f), Vector3.back, .33f, index % 4);
                SlopCityInteractable service = box.gameObject.AddComponent<SlopCityInteractable>();
                service.Configure(SlopCityInteractionKind.Inspect, "Municipal signal junction / access logged", false);
                service.ConfigureInteractionPoint(box.TransformPoint(new Vector3(0, .9f, -.25f)));
                Transform venue = district.GetComponentsInChildren<Transform>().FirstOrDefault(item => item.name.StartsWith("Venue_"));
                if (venue)
                {
                    for (int poster = 0; poster < 3; poster++)
                        Poster(venue, "Pasted_Public_Notice", new Vector3(7.122f, 1.48f, -2.4f + poster * 1.2f), Vector3.right, 1.02f, (index + poster) % 4);
                    CameraRig(venue, new Vector3(-6.75f, 3.02f, 4.72f), 195);
                    SaggingWire(venue, new Vector3(-6.5f, 3.58f, 4.73f), new Vector3(6.5f, 3.58f, 4.73f), .19f);
                    for (int fixture = 0; fixture < 2; fixture++)
                        Extractor(venue, new Vector3(-4.5f + fixture * 7, 3.83f, -1.3f));
                }
                // Paper and dirt collect by kerbs; thin non-colliding detail does not create trip hazards.
                for (int litter = 0; litter < 6; litter++)
                {
                    GameObject sheet = Rounded(root, "Windblown_Paper", new Vector3(-13.9f + (litter % 3) * .43f, .029f, -9.2f + litter * .41f),
                        new Vector3(.18f + litter * .016f, .002f, .26f), .0006f, paper);
                    sheet.transform.localRotation = Quaternion.Euler(0, 31 * litter + index * 17, 0);
                }
            }
            Transform city = districts[0].transform.parent;
            // The skyline needs an actual ground plane when viewed from accessible roofs;
            // otherwise the distant buildings appear suspended over the skybox's lower half.
            GameObject horizonGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
            horizonGround.name = "Distant_City_Ground";
            horizonGround.transform.SetParent(city, false);
            horizonGround.transform.localPosition = new Vector3(0, -.39f, 0);
            horizonGround.transform.localScale = new Vector3(1800, .2f, 1800);
            horizonGround.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SlopCityCityBuilder.MaterialsFolder + "/Asphalt.mat");
            UnityEngine.Object.DestroyImmediate(horizonGround.GetComponent<Collider>());
            horizonGround.isStatic = true;
            foreach (Transform station in city.GetComponentsInChildren<Transform>().Where(item => item.name.StartsWith("Subway_Station_")).ToArray())
            {
                for (int poster = 0; poster < 4; poster++)
                    Poster(station, "Transit_Public_Notice", new Vector3(-5.4f + poster * 3.25f, -6.1f, -5.285f), Vector3.forward, 1.27f, poster);
                CameraRig(station, new Vector3(5.8f, -4.95f, -5.15f), 0);
            }
            foreach (Transform boundary in city.GetComponentsInChildren<Transform>().Where(item => item.name.StartsWith("Boundary_") && item.name.Length == 10).ToArray())
                for (int notice = 0; notice < 10; notice++)
                    Poster(boundary, "Perimeter_Notice", new Vector3(-52 + notice * 11.6f, 1.15f, 60.785f), Vector3.back, 1.35f, notice % 4);
            // A printed station ident remains under each live surface during scene previews and decoder startup.
            // Native video is still supplied by the existing CityMedia player on top of this physical backing.
            foreach (Transform anchor in city.GetComponentsInChildren<Transform>().Where(item => item.name.StartsWith("SlopCityMediaScreenAnchor_Channel")).ToArray())
            {
                float aspect = anchor.localScale.x / anchor.localScale.y;
                Poster(anchor, "Broadcast_Standby_Print", new Vector3(0, 0, -.004f), Vector3.forward, 1, anchor.name.EndsWith("1") ? 1 : 0);
                Transform print = anchor.GetChild(anchor.childCount - 1);
                print.localScale = new Vector3(1 / aspect, 1, 1);
            }
            AssetDatabase.SaveAssets();
        }

        private static void UtilityPole(Transform parent, Vector3 position, int index)
        {
            Transform pole = Root("Utility_And_Surveillance_Pole", parent, position);
            SlopCityArchitecturalMeshes.Tube(pole, "Galvanized_Mast", new[] { Vector3.zero, new Vector3(0, 6.65f, 0) }, .065f, metal, true);
            Rounded(pole, "Cable_Crossarm", new Vector3(.19f, 6.18f, 0), new Vector3(1.05f, .055f, .07f), .01f, metal);
            for (int insulator = 0; insulator < 3; insulator++)
                Rounded(pole, "Porcelain_Insulator", new Vector3(insulator * .19f, 6.27f, 0), new Vector3(.075f, .16f, .075f), .023f, housing);
            CameraRig(pole, new Vector3(.14f, 4.75f, .04f), index * 63);
        }

        private static void CameraRig(Transform parent, Vector3 position, float yaw)
        {
            Transform rig = Root("CCTV_Surveillance", parent, position);
            rig.localRotation = Quaternion.Euler(12, yaw, 0);
            Rounded(rig, "Wall_Bracket", new Vector3(0, 0, 0), new Vector3(.12f, .24f, .06f), .012f, metal);
            Rounded(rig, "Camera_Arm", new Vector3(0, -.07f, .21f), new Vector3(.05f, .06f, .40f), .01f, metal);
            Rounded(rig, "Weatherproof_Housing", new Vector3(0, .025f, .45f), new Vector3(.22f, .17f, .43f), .04f, housing);
            Rounded(rig, "Camera_Visor", new Vector3(0, .13f, .50f), new Vector3(.28f, .025f, .50f), .008f, metal);
            Rounded(rig, "Lens_Glass", new Vector3(0, .027f, .672f), new Vector3(.125f, .095f, .012f), .027f, lens);
        }

        private static void Extractor(Transform parent, Vector3 position)
        {
            Transform unit = Root("Roof_Extractor", parent, position);
            Rounded(unit, "Weathered_HVAC", new Vector3(0, .24f, 0), new Vector3(1.4f, .48f, .95f), .045f, metal);
            for (int slot = 0; slot < 12; slot++)
                Rounded(unit, "Intake_Fin", new Vector3(-.55f + slot * .10f, .29f, -.48f), new Vector3(.038f, .30f, .028f), .006f, cable);
            SlopCityArchitecturalMeshes.Tube(unit, "Ductwork", new[] { new Vector3(.60f, .25f, .1f), new Vector3(1, .25f, .1f), new Vector3(1, .05f, .1f) }, .13f, metal);
        }

        private static void SaggingWire(Transform parent, Vector3 start, Vector3 end, float sag)
        {
            Vector3[] points = new Vector3[17];
            for (int i = 0; i < points.Length; i++)
            { float t = i / 16f; points[i] = Vector3.Lerp(start, end, t) - Vector3.up * (4 * t * (1 - t) * sag); }
            SlopCityArchitecturalMeshes.Tube(parent, "Suspended_Utility_Cable", points, .012f, cable);
        }

        private static void Poster(Transform parent, string name, Vector3 point, Vector3 outward, float size, int index)
        {
            GameObject poster = GameObject.CreatePrimitive(PrimitiveType.Quad);
            poster.name = name;
            poster.transform.SetParent(parent, false);
            poster.transform.localPosition = point;
            poster.transform.localRotation = Quaternion.LookRotation(-outward, Vector3.up);
            poster.transform.localScale = new Vector3(size, size, 1);
            UnityEngine.Object.DestroyImmediate(poster.GetComponent<Collider>());
            Renderer renderer = poster.GetComponent<Renderer>();
            renderer.sharedMaterial = notices[index];
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            poster.isStatic = !poster.GetComponentsInParent<SlopCityInteractable>()
                .Any(action => action.Kind == SlopCityInteractionKind.Car);
        }

        private static GameObject Rounded(Transform parent, string name, Vector3 position, Vector3 size, float radius, Material material, bool collision = false)
        { return SlopCityArchitecturalMeshes.RoundedBox(parent, name, position, size, radius, material, collision); }
        private static Transform Root(string name, Transform parent, Vector3 position)
        { var owner = new GameObject(name); owner.transform.SetParent(parent, false); owner.transform.localPosition = position; return owner.transform; }
        private static Material Material(string name, Color color, float metallic, float gloss)
        {
            string path = Folder + "/Dystopia_" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
            material.color = color; material.SetFloat("_Metallic", metallic); material.SetFloat("_Glossiness", gloss);
            material.enableInstancing = true; EditorUtility.SetDirty(material); return material;
        }
    }
}
#endif
