#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity.Editor
{
    /// <summary>Physical kanban, shopfront fittings and shared-network screens over the existing street grid.</summary>
    public static class SlopCityJapanBuilder
    {
        private const string Folder = "Assets/Art/JapanSigns";
        private static readonly string[] Labels = {
            "夜市", "深夜食堂", "電脳商店", "機械出版", "未来薬局", "修理工房", "中央線", "地下鉄",
            "都市案内", "喫茶", "ラーメン", "カラオケ", "夢境ホテル", "古書", "中古家電", "東京通信",
            "夜間診療", "無人酒店", "両替", "ゲーム", "携帯修理", "監視区域", "燃料", "居住区",
            "屋上庭園", "電器店", "写真館", "映画館", "駅前市場", "新宿放送", "防災センター", "営業中"
        };
        // Physical faces keep their baked Japanese glyphs. The HUD uses an existing Latin
        // runtime font, so interaction descriptions use matching English translations.
        private static readonly string[] InteractionLabels = {
            "Night market", "Late night diner", "Cyber store", "Machine Publishers", "Future pharmacy", "Repairs", "Central line", "Subway",
            "City information", "Coffee", "Ramen", "Karaoke", "Dream hotel", "Books", "Used electronics", "Tokyo signal",
            "Night clinic", "Automated liquor", "Exchange", "City arcade", "Phone repair", "Surveillance area", "Fuel", "Residential",
            "Roof garden", "Electronics", "Photo studio", "Cinema", "Station market", "Shinjuku broadcast", "Civil defence", "Open"
        };
        private static readonly Color[] Tints = {
            new Color(.23f, .82f, .87f), new Color(.91f, .28f, .51f),
            new Color(1.0f, .64f, .27f), new Color(.79f, .85f, .82f), new Color(.91f, .43f, .23f)
        };
        private static Material frame, rubber, enamel, cloth, brass;
        private static Material[] notices, neon;
        private static readonly Dictionary<int, Mesh> TileMeshes = new Dictionary<int, Mesh>();
        private static int signs, videos;

        public static void Build(Transform city, SlopCityDistrict[] districts)
        {
            if (!city || districts == null || districts.Length != 9)
                throw new ArgumentException("Japanese metropolis requires the existing nine districts.");
            PrepareAssets();
            Transform previous = city.Find("City_JapanMetropolis");
            if (previous) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            Transform root = Root("City_JapanMetropolis", city, Vector3.zero);
            signs = videos = 0;
            foreach (SlopCityDistrict district in districts.OrderBy(item => item.Index))
            {
                Transform block = Root("Japan_District_" + district.Index, root, root.InverseTransformPoint(district.Center));
                Transform[] towers = district.GetComponentsInChildren<Transform>()
                    .Where(item => item.name.StartsWith("City_Skyscraper_")).Take(2).ToArray();
                if (towers.Length < 2)
                    throw new InvalidOperationException("Japanese signs require two existing street towers in " + district.DisplayName);
                for (int i = 0; i < 2; i++) DressTower(block, towers[i], district.Index, i);
                AddEntryCanopy(block, district.Index);
                AddRetailDetail(block, district);
                // Replaced by the suspended, physically backed 夜市 entry fixture.
                foreach (TextMesh legacy in district.GetComponentsInChildren<TextMesh>())
                    if (legacy.text == "THE NIGHT MARKET")
                    {
                        Renderer renderer = legacy.GetComponent<Renderer>();
                        if (renderer) renderer.enabled = false;
                    }
            }
            // These sit well above the four main avenues, supported from adjacent blocks.
            // Every approach gets a readable screen, including the initial northward street view.
            AvenueBillboard(root, new Vector3(20, 12.6f, 25), Vector3.back, 0);
            AvenueBillboard(root, new Vector3(-20, 13.8f, -25), Vector3.forward, 1);
            AvenueBillboard(root, new Vector3(25, 15.2f, -20), Vector3.left, 2);
            AvenueBillboard(root, new Vector3(-25, 14.3f, 20), Vector3.right, 3);
            if (signs != 198 || videos != 40)
                throw new InvalidOperationException("Japanese city fixture count changed: " + signs + " signs / " + videos + " videos.");
            AssetDatabase.SaveAssets();
            Debug.Log("SLOP CITY JAPAN: " + signs + " Japanese signs / " + videos + " additional shared-network video surfaces / 9 districts.");
        }

        private static void DressTower(Transform parent, Transform tower, int district, int ordinal)
        {
            Transform core = tower.Find("Structural_Core");
            if (!core || !core.GetComponent<MeshFilter>()) throw new InvalidOperationException("Tower has no metric structural core.");
            Vector3 size = core.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            float halfX = size.x * .5f, halfZ = size.z * .5f;
            int first = district * 5 + ordinal * 11;
            Action<Vector3, Vector3, Vector2, int, bool> add = (point, outward, dimensions, label, vertical) =>
                Sign(parent, "Japan_Sign_" + district + "_" + signs.ToString("D3"),
                    parent.InverseTransformPoint(tower.TransformPoint(point)), tower.TransformDirection(outward),
                    dimensions, label % Labels.Length, vertical, (district + label) % Tints.Length);

            // Layered blades and bands differ in height, width and mounting depth. Dark housings
            // remain visible around luminous glyphs instead of becoming solid neon rectangles.
            add(new Vector3(-halfX + .2f, 7.6f, -halfZ - .42f), Vector3.back, new Vector2(.76f, 4.5f), first, true);
            add(new Vector3(halfX - .25f, 11.2f, -halfZ - .63f), Vector3.back, new Vector2(.89f, 5.6f), first + 1, true);
            add(new Vector3(halfX + .48f, 7.1f, halfZ * .50f), Vector3.right, new Vector2(.81f, 4.6f), first + 2, true);
            add(new Vector3(-halfX - .42f, 12.0f, -halfZ * .44f), Vector3.left, new Vector2(.72f, 5.5f), first + 3, true);
            add(new Vector3(0, 4.24f, -halfZ - .32f), Vector3.back, new Vector2(size.x * .78f, .92f), first + 4, false);
            add(new Vector3(halfX + .30f, 16.15f, 0), Vector3.right, new Vector2(size.z * .79f, 1.05f), first + 5, false);
            for (int rear = 0; rear < 3; rear++)
                add(new Vector3((rear - 1) * .13f, 5.1f + rear * 3.65f, halfZ + .31f + rear * .035f),
                    Vector3.forward, new Vector2(size.x * (.72f + rear * .03f), .96f), first + 6 + rear, false);
            add(new Vector3(halfX * .45f, 3.45f, -halfZ - .27f), Vector3.back,
                new Vector2(1.12f, .43f), 31 - ordinal * 2, false);

            int channel = (district + ordinal) % 3;
            if (ordinal == 0)
            {
                VideoOnTower(parent, tower, new Vector3(0, 21.4f, -halfZ - .43f), Vector3.back,
                    Mathf.Min(size.x * .95f, 6.3f), district, 0, channel);
                VideoOnTower(parent, tower, new Vector3(halfX + .43f, 12.75f, 0), Vector3.right,
                    Mathf.Min(size.z * .90f, 6.0f), district, 2, (channel + 1) % 3);
            }
            else
            {
                VideoOnTower(parent, tower, new Vector3(0, 18.6f, halfZ + .43f), Vector3.forward,
                    Mathf.Min(size.x * .93f, 6.3f), district, 1, channel);
                VideoOnTower(parent, tower, new Vector3(-halfX - .43f, 21.4f, 0), Vector3.left,
                    Mathf.Min(size.z * .95f, 6.3f), district, 3, (channel + 1) % 3);
            }
            // Occupied building details: compact split-system compressors, guarded fans,
            // drainage and refrigerant lines tucked against already-solid facade surfaces.
            for (int ac = 0; ac < 2; ac++)
            {
                Vector3 point = new Vector3(halfX + .36f, 4.65f + ac * 4.0f, -halfZ * .6f);
                AirConditioner(parent, parent.InverseTransformPoint(tower.TransformPoint(point)), Vector3.right, district + ac);
            }
            Transform conduit = Root("Japan_Facade_Service_Riser", parent,
                parent.InverseTransformPoint(tower.TransformPoint(new Vector3(-halfX - .18f, 0, halfZ - .18f))));
            Tube(conduit, "Copper_Condensate_Riser", new[] { new Vector3(0, 2.7f, 0), new Vector3(0, 17.2f, 0) }, .018f, brass);
        }

        private static void AddEntryCanopy(Transform block, int district)
        {
            Sign(block, "Japan_Sign_" + district + "_" + signs.ToString("D3"), new Vector3(0, 4.88f, -12),
                Vector3.back, new Vector2(3.7f, .89f), district == 5 ? 0 : (district * 3 + 28) % Labels.Length, false, district % Tints.Length);
            // Cable-supported market ident, with no poles placed on the pedestrian corridor.
            Wire(block, new Vector3(-12.7f, 6.1f, -11.8f), new Vector3(12.7f, 6.1f, -11.8f), .85f);
            foreach (float side in new[] { -1f, 1f })
                Tube(block, "Entry_Sign_Suspension", new[] { new Vector3(side * 1.6f, 5.95f, -12), new Vector3(side * 1.6f, 5.38f, -12) }, .011f, frame);
        }

        private static void AddRetailDetail(Transform block, SlopCityDistrict district)
        {
            Transform venue = district.GetComponentsInChildren<Transform>().FirstOrDefault(item => item.name.StartsWith("Venue_"));
            int label = (district.Index * 3 + 1) % Labels.Length;
            if (!venue)
            {
                // The authored lounge and open parks retain their existing construction.
                Sign(block, "Japan_Sign_" + district.Index + "_" + signs.ToString("D3"), new Vector3(-7.4f, 5.7f, -12.05f),
                    Vector3.back, new Vector2(2.45f, .64f), label, false, 3);
                return;
            }
            // Existing storefronts face south; the sign sits above their glazing and clear doorway.
            Vector3 point = block.InverseTransformPoint(venue.TransformPoint(new Vector3(0, 3.84f, 4.72f)));
            Sign(block, "Japan_Sign_" + district.Index + "_" + signs.ToString("D3"), point, Vector3.back,
                new Vector2(8.1f, 1.02f), label, false, (district.Index + 2) % Tints.Length);
            Transform shop = Root("Japan_Retail_Fittings_" + district.Index, block,
                block.InverseTransformPoint(venue.position));
            foreach (float x in new[] { -4.75f, 4.75f })
            {
                Rounded(shop, "Folded_Shop_Awning", new Vector3(x, 2.91f, -5.22f), new Vector3(3.4f, .15f, .76f), .035f, enamel);
                // Noren hangs above 2.5m. Its two narrow fabric panels do not seal entrances.
                for (int curtain = 0; curtain < 5; curtain++)
                    Rounded(shop, "Noren_Fabric_Panel", new Vector3(x - 1.2f + curtain * .60f, 2.73f, -5.60f),
                        new Vector3(.57f, .32f, .016f), .005f, cloth);
                Rounded(shop, "Under_Awning_Fluorescent", new Vector3(x, 2.82f, -5.45f), new Vector3(2.2f, .026f, .035f), .009f, neon[3]);
                // A shallow menu case mounts to the existing facade rather than extending onto paths.
                Rounded(shop, "Menu_Display_Case", new Vector3(x + 1.29f, 1.57f, -4.673f), new Vector3(.43f, .70f, .085f), .018f, frame);
                for (int item = 0; item < 6; item++)
                    Rounded(shop, "Menu_Illuminated_Rule", new Vector3(x + 1.29f, 1.81f - item * .083f, -4.726f),
                        new Vector3(.30f, .018f, .008f), .002f, neon[2]);
                AirConditioner(shop, new Vector3(x, 4.03f, 2.6f), Vector3.back, district.Index);
            }
        }

        private static void Sign(Transform parent, string name, Vector3 position, Vector3 outward, Vector2 size, int label, bool vertical, int tint)
        {
            Transform owner = Root(name, parent, position);
            owner.localRotation = Quaternion.LookRotation(parent.InverseTransformDirection(outward), Vector3.up);
            Rounded(owner, "Kanban_Powdercoat_Housing", new Vector3(0, 0, -.065f), new Vector3(size.x + .12f, size.y + .12f, .16f), .035f, frame);
            Tile(owner, "Kanban_Japanese_" + Labels[label], new Vector3(0, 0, .026f), size, label + (vertical ? 32 : 0), notices[tint]);
            float bracketY = size.y * .32f;
            foreach (float y in new[] { -bracketY, bracketY })
            {
                Rounded(owner, "Galvanized_Mounting_Bracket", new Vector3(0, y, -.30f), new Vector3(Mathf.Min(.55f, size.x * .65f), .048f, .43f), .009f, frame);
                Rounded(owner, "Wall_Bracket_Plate", new Vector3(0, y, -.51f), new Vector3(.15f, .25f, .033f), .008f, frame);
            }
            // Slim exposed neon accents and visible weatherproof wiring accompany the printed face.
            float edgeX = size.x * .5f + .025f;
            if (vertical)
                Tube(owner, "Kanban_Neon_Edge", new[] { new Vector3(-edgeX, -.45f * size.y, .036f), new Vector3(-edgeX, .45f * size.y, .036f) }, .011f, neon[tint]);
            else
                Tube(owner, "Kanban_Neon_Edge", new[] { new Vector3(-.44f * size.x, -.5f * size.y - .025f, .028f), new Vector3(.44f * size.x, -.5f * size.y - .025f, .028f) }, .008f, neon[tint]);
            Tube(owner, "Weatherproof_Power_Lead", new[] { new Vector3(edgeX, -size.y * .32f, -.05f), new Vector3(edgeX + .08f, -size.y * .40f, -.13f), new Vector3(edgeX + .06f, -size.y * .42f, -.51f) }, .009f, rubber);
            SlopCityInteractable action = owner.gameObject.AddComponent<SlopCityInteractable>();
            action.Configure(SlopCityInteractionKind.Inspect, InteractionLabels[label] + " / illuminated neighbourhood sign", false);
            action.ConfigureInteractionPoint(owner.position);
            signs++;
        }

        private static void VideoOnTower(Transform parent, Transform tower, Vector3 position, Vector3 outward, float width, int district, int ordinal, int channel)
        {
            Transform owner = VideoBillboard(parent, "Japan_VideoBillboard_" + district + "_" + ordinal,
                parent.InverseTransformPoint(tower.TransformPoint(position)), tower.TransformDirection(outward), new Vector2(width, width * 9 / 16), channel);
            foreach (float side in new[] { -1f, 1f })
                Rounded(owner, "Wall_Display_Bracket", new Vector3(side * width * .33f, 0, -.39f), new Vector3(.11f, width * .36f, .56f), .015f, frame);
        }

        private static Transform VideoBillboard(Transform parent, string name, Vector3 point, Vector3 outward, Vector2 size, int channel)
        {
            Transform board = Root(name, parent, point);
            board.localRotation = Quaternion.LookRotation(parent.InverseTransformDirection(outward), Vector3.up);
            Rounded(board, "Weatherproof_LED_Display_Chassis", Vector3.zero, new Vector3(size.x + .26f, size.y + .26f, .32f), .055f, frame);
            Rounded(board, "Recessed_Display_Gasket", new Vector3(0, 0, .165f), new Vector3(size.x + .065f, size.y + .065f, .026f), .009f, rubber);
            Transform anchor = Root("SlopCityMediaScreenAnchor_Channel" + channel, board, new Vector3(0, 0, .19f));
            anchor.localScale = new Vector3(size.x, size.y, 1);
            // Japanese station ident is visible while stopped in the editor and while video starts.
            // Runtime screen is only 1.5cm above it and shares the existing three channel decoders.
            Tile(anchor, "Japan_Broadcast_Ident", new Vector3(0, 0, .001f), Vector2.one, 29, notices[channel]);
            for (int vent = 0; vent < 8; vent++)
                Rounded(board, "Display_Cooling_Louvre", new Vector3((vent - 3.5f) * size.x / 10, size.y * .5f + .07f, -.03f), new Vector3(size.x / 15, .035f, .17f), .008f, rubber);
            Tube(board, "LED_Screen_Power_Riser", new[] { new Vector3(size.x * .44f, -size.y * .5f, -.14f), new Vector3(size.x * .44f, -size.y * .5f - .34f, -.14f), new Vector3(size.x * .44f, -size.y * .5f - .39f, -.58f) }, .026f, rubber);
            videos++;
            return board;
        }

        private static void AvenueBillboard(Transform parent, Vector3 point, Vector3 outward, int index)
        {
            Transform board = VideoBillboard(parent, "Japan_VideoBillboard_Avenue_" + index, point, outward, new Vector2(9.35f, 5.26f), index % 3);
            float lower = -3.02f, upper = 3.12f;
            // Real lattice gantry across the street. Supports terminate at adjacent building sides.
            foreach (float y in new[] { lower, upper })
                Tube(board, "Gantry_Chord", new[] { new Vector3(-8.25f, y, -.42f), new Vector3(8.25f, y, -.42f) }, .066f, frame);
            for (int bay = 0; bay < 8; bay++)
                Tube(board, "Gantry_Diagonal", new[] { new Vector3(-8.25f + bay * 2.0625f, lower, -.42f), new Vector3(-8.25f + (bay + 1) * 2.0625f, upper, -.42f) }, .036f, frame);
            foreach (float side in new[] { -1f, 1f })
                Tube(board, "Building_Anchor_Beam", new[] { new Vector3(side * 8.2f, lower, -.42f), new Vector3(side * 8.2f, lower, -2.4f) }, .078f, frame);
            for (int cable = 0; cable < 3; cable++)
                Wire(board, new Vector3(-8.2f, upper + .35f, -.4f - cable * .18f), new Vector3(8.2f, upper + .35f, -.4f - cable * .18f), .24f);
        }

        private static void AirConditioner(Transform parent, Vector3 position, Vector3 outward, int style)
        {
            Transform ac = Root("Japan_SplitSystem_AC", parent, position);
            ac.localRotation = Quaternion.LookRotation(outward, Vector3.up);
            Rounded(ac, "Weathered_Compressor_Casing", Vector3.zero, new Vector3(.97f, .63f, .40f), .041f, enamel);
            Rounded(ac, "Fan_Dark_Recess", new Vector3(-.13f, 0, .208f), new Vector3(.48f, .48f, .022f), .10f, rubber);
            for (int ring = 0; ring < 4; ring++)
            {
                float radius = .055f + ring * .053f;
                var points = new Vector3[25];
                for (int step = 0; step < points.Length; step++)
                {
                    float angle = step / 24f * Mathf.PI * 2;
                    points[step] = new Vector3(-.13f + Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, .235f);
                }
                Tube(ac, "Fan_Guard_Ring", points, .0048f, frame);
            }
            foreach (float angle in new[] { 0f, 60f, 120f })
            {
                float r = angle * Mathf.Deg2Rad;
                Vector3 axis = new Vector3(Mathf.Cos(r), Mathf.Sin(r), 0) * .221f;
                Tube(ac, "Fan_Guard_Spoke", new[] { new Vector3(-.13f, 0, .238f) - axis, new Vector3(-.13f, 0, .238f) + axis }, .006f, frame);
            }
            for (int vent = 0; vent < 7; vent++)
                Rounded(ac, "Compressor_Side_Louvre", new Vector3(.31f, -.20f + vent * .058f, .214f), new Vector3(.16f, .018f, .012f), .003f, rubber);
            foreach (float x in new[] { -.36f, .36f })
                Rounded(ac, "Compressor_Mounting_Rail", new Vector3(x, -.37f, -.07f), new Vector3(.055f, .12f, .61f), .008f, frame);
            Tube(ac, "Insulated_Refrigerant_Loop", new[] { new Vector3(.49f, -.13f, -.02f), new Vector3(.66f, -.13f, -.02f), new Vector3(.71f, -.34f, -.15f), new Vector3(.68f, -.41f, -.44f) }, .024f, rubber);
        }

        private static void PrepareAssets()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            // Re-read saved tile assets each build so UV migrations also repair an existing scene.
            TileMeshes.Clear();
            Texture2D albedo = Texture("JapanKanban-Albedo.png");
            Texture2D emission = Texture("JapanKanban-Emission.png");
            frame = Material("Japan_FrameSteel", new Color(.16f, .18f, .19f), .78f, .39f);
            SlopCitySurfaceLibrary.Apply(frame, "StainlessSteel");
            rubber = Material("Japan_CableRubber", new Color(.025f, .029f, .031f), .01f, .2f);
            enamel = Material("Japan_CompressorEnamel", new Color(.52f, .50f, .44f), .04f, .34f);
            SlopCitySurfaceLibrary.Apply(enamel, "TransitEnamel");
            cloth = Material("Japan_NorenCloth", new Color(.15f, .20f, .22f), .0f, .13f);
            brass = Material("Japan_AgedCopper", new Color(.27f, .22f, .16f), .76f, .32f);
            notices = new Material[Tints.Length]; neon = new Material[Tints.Length];
            for (int i = 0; i < Tints.Length; i++)
            {
                // "Notice" keeps photo-scan remapping from replacing this baked typography.
                notices[i] = Material("Japan_Notice_Kanban_" + i, Tints[i], .02f, .31f);
                notices[i].mainTexture = albedo;
                notices[i].mainTextureScale = Vector2.one;
                notices[i].SetTexture("_EmissionMap", emission);
                notices[i].SetColor("_EmissionColor", Tints[i] * (i == 3 ? 2.3f : 3.5f));
                EnableEmission(notices[i]);
                neon[i] = Material("Japan_Notice_NeonTube_" + i, Tints[i], .05f, .55f);
                neon[i].SetColor("_EmissionColor", Tints[i] * 3.8f);
                EnableEmission(neon[i]);
            }
        }

        private static void EnableEmission(Material material)
        {
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            material.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
        }

        private static Texture2D Texture(string file)
        {
            string path = Folder + "/" + file;
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new FileNotFoundException("Japanese sign atlas is missing: " + path);
            if (importer.maxTextureSize != 4096 || importer.anisoLevel != 8 || !importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.maxTextureSize = 4096; importer.anisoLevel = 8; importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material Material(string name, Color color, float metallic, float gloss)
        {
            string path = Folder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
            material.color = color; material.SetFloat("_Metallic", metallic); material.SetFloat("_Glossiness", gloss);
            material.enableInstancing = true; EditorUtility.SetDirty(material); return material;
        }

        private static void Tile(Transform parent, string name, Vector3 position, Vector2 size, int tile, Material material)
        {
            if (!TileMeshes.TryGetValue(tile, out Mesh mesh) || !mesh)
            {
                string path = Folder + "/KanbanTile_" + tile.ToString("D2") + ".asset";
                mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                bool vertical = tile >= 32; int index = tile % 32;
                int w = vertical ? 256 : 1024, h = vertical ? 1024 : 256;
                int x = index % (vertical ? 16 : 4) * w;
                int y = vertical ? 2048 + index / 16 * h : index / 4 * h;
                float left = (x + 2) / 4096f, right = (x + w - 2) / 4096f;
                float bottom = (4096 - y - h + 2) / 4096f, top = (4096 - y - 2) / 4096f;
                if (!mesh)
                {
                    mesh = new Mesh { name = "Japanese_Kanban_Tile_" + tile };
                    mesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) };
                    mesh.normals = Enumerable.Repeat(Vector3.forward, 4).ToArray();
                    mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                    mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path);
                }
                // The front is +Z, so a camera looking at that front sees local -X on its right.
                // Reverse U (not the atlas itself) to read correctly from the outward face.
                // Apply this to previously persisted meshes too, retaining their asset GUIDs.
                mesh.uv = new[] { new Vector2(right, bottom), new Vector2(left, bottom), new Vector2(left, top), new Vector2(right, top) };
                mesh.RecalculateTangents(); EditorUtility.SetDirty(mesh);
                TileMeshes[tile] = mesh;
            }
            Transform face = Root(name, parent, position); face.localScale = new Vector3(size.x, size.y, 1);
            face.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer renderer = face.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            face.gameObject.isStatic = true;
        }

        private static void Wire(Transform parent, Vector3 start, Vector3 end, float sag)
        {
            var points = new Vector3[17];
            for (int i = 0; i < points.Length; i++)
            { float t = i / 16f; points[i] = Vector3.Lerp(start, end, t) - Vector3.up * 4 * t * (1 - t) * sag; }
            Tube(parent, "Japan_Sagging_Supply_Cable", points, .012f, rubber);
        }
        private static Transform Root(string name, Transform parent, Vector3 point)
        { var obj = new GameObject(name); obj.transform.SetParent(parent, false); obj.transform.localPosition = point; return obj.transform; }
        private static GameObject Rounded(Transform parent, string name, Vector3 point, Vector3 size, float radius, Material material)
        { return SlopCityArchitecturalMeshes.RoundedBox(parent, name, point, size, radius, material, false); }
        private static void Tube(Transform parent, string name, Vector3[] points, float radius, Material material)
        { SlopCityArchitecturalMeshes.Tube(parent, name, points, radius, material, false); }
    }
}
#endif
