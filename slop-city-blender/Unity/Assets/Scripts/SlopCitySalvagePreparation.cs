using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>Deterministic shared-resource dressing and physical salvage preparation before batching.</summary>
    public sealed class SlopCitySalvagePreparation : MonoBehaviour
    {
        public const string PosterPrefix = "Salvage_Protected_Poster_";
        public const string PawnCounterName = "Salvage_Pawn_Counter";
        public const long MaximumMutableSourceBytes = 48 * 1024 * 1024;
        [SerializeField] private bool prepared;
        [SerializeField] private bool homePrepared;
        [SerializeField] private bool homeDetailsPrepared;
        [SerializeField] private bool dustPrepared;
        private readonly HashSet<int> admittedInteriors = new HashSet<int>();
        private static readonly string[] DustyRoomParts =
        {
            "_Desk", "_Desk_Leg", "_Notebook", "_Coffee_Table", "_Reading_Material", "_Bedside_Table",
            "_Dining_Table", "_Dining_Pedestal", "_Wardrobe", "_Wardrobe_Door", "_Wardrobe_Handle",
            "_Shelf", "_Reception_Worktop", "_Workbench", "_Workbench_Top", "_Tool_Rail",
            "_Mixing_Console", "_Monitor_Speaker", "_Acoustic_Panel", "_Signal_Test_Rack", "_Supplies",
            "_Reception_Desk", "_Kitchen_Sink", "_Countertop", "_Sink_Basin", "_Sink_Tap", "_Cooktop",
            "_Heating_Plate", "_Backsplash", "_Lower_Cabinet_Door", "_Upper_Cabinet",
            "_Refrigerator", "_Refrigerator_Door", "_Refrigerator_Handle", "_Freezer_Division",
            "_Laundry", "_Laundry_Door", "_Laundry_Handle", "_Cleaning_Supplies",
            "_Cleaning_Supplies_Door", "_Cleaning_Supplies_Handle", "_Utility_Door",
            "_Toilet", "_Toilet_Cistern", "_Washbasin", "_Bathroom_Mirror", "_Towel_Rail",
            "_Shower_Riser", "_Shower_Head", "_Shower_Screen", "_Plant"
        };

        public static void Prepare(Transform city)
        {
            if (!city) return;
            var owner = city.GetComponent<SlopCitySalvagePreparation>();
            if (!owner) owner = city.gameObject.AddComponent<SlopCitySalvagePreparation>();
            if (!owner.prepared)
            {
                PrepareSurfaces(city);
                PreparePosters(city);
                GroupCounterParts(city);
                PreparePawnCounter(city);
                PrepareMailboxes(city);
                PrepareLooseObjects(city);
                owner.prepared = true;
            }
            if (!owner.dustPrepared)
            {
                PrepareDustyWorld(city);
                owner.dustPrepared = true;
            }
            owner.AdmitStreamedFurniture(city);
        }

        /// <summary>The authored lounge is a separate scene root, not streamed city furniture.</summary>
        public static void PrepareHome(Transform lounge)
        {
            if (!lounge || lounge.name != "SlopCity_Lounge") return;
            var owner = lounge.GetComponent<SlopCitySalvagePreparation>();
            if (!owner) owner = lounge.gameObject.AddComponent<SlopCitySalvagePreparation>();
            if (!owner.homeDetailsPrepared)
            {
                PrepareHomeAssembly(lounge, "Home_Coffee_Mug", "Coffee mug", "Mug_Ceramic", "Mug_Rim", "Mug_Handle");
                PrepareHomeAssembly(lounge, "Home_Laptop_Casing", "Laptop casing and keyboard", "Laptop_Base", "Laptop_Lid", "Laptop_Trackpad", "Laptop_Key");
                PrepareHomeAssembly(lounge, "Home_Headphones", "Headphones", "Headphones_Headband", "Headphone_Earcup");
                PrepareHomeAssembly(lounge, "Home_Car_Keys", "Car keys", "Car_Key_Fob", "Car_Key_Blade");
                foreach (Transform part in lounge.GetComponentsInChildren<Transform>(true))
                {
                    string label = HomePartMatches(part.name, "Coffee_Table_Book") ? "Coffee table book"
                        : HomePartMatches(part.name, "Blue_Book_Folder") ? "Blue Book folder"
                        : HomePartMatches(part.name, "Outlet_Plate") ? "Outlet plate"
                        : HomePartMatches(part.name, "Lamp_Base") ? "Home lamp base" : null;
                    if (label != null) ConfigureDustyPart(part, label);
                }
                owner.homeDetailsPrepared = true;
            }
            if (owner.homePrepared) return;
            foreach (Transform part in lounge.GetComponentsInChildren<Transform>(true))
            {
                // Match SceneBuilder's authored furniture, never the room shell or media cabinet
                // (the latter's visible replacement is already combined with the habitat shell).
                string label;
                if (part.name == "SC_Couch.Root") label = "Home couch";
                else if (HomePartMatches(part.name, "Coffee_Table_Top")) label = "Coffee table top";
                else if (HomePartMatches(part.name, "Coffee_Table_Leg")) label = "Coffee table leg";
                else if (HomePartMatches(part.name, "Side_Table")) label = "Side table top";
                else if (HomePartMatches(part.name, "Side_Table_Leg")) label = "Side table leg";
                else if (HomePartMatches(part.name, "Record_Cabinet")) label = "Record cabinet";
                else if (HomePartMatches(part.name, "Record_Cabinet_Inset")) label = "Record cabinet inset";
                else if (HomePartMatches(part.name, "Plant_Pot")) label = "Home plant pot";
                else if (HomePartMatches(part.name, "Linen_Lamp_Shade")) label = "Home lamp shade";
                else if (HomePartMatches(part.name, "Lamp_Shade_Bottom")) label = "Home lamp shade rim";
                else if (HomePartMatches(part.name, "Lamp_Stem")) label = "Home lamp stem";
                else continue;
                if (SlopCitySalvageItem.HasProtectedContents(part) || part.GetComponentInParent<SlopCityDoor>()
                    || part.GetComponentInParent<SlopCityElevator>()) continue;
                bool visible = false;
                foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled && !SlopCitySalvageItem.IsProtectedArt(renderer.transform)) { visible = true; break; }
                if (!visible) continue;
                // Cleaning uses the existing per-renderer finish. No reparenting, mesh/material
                // copies or pickup: seat/spawn anchors and furniture collision remain fixed.
                ConfigureItem(part, label, "furniture", true, false, 1);
            }
            owner.homePrepared = true;
        }

        private static void PrepareHomeAssembly(Transform lounge, string name, string label, params string[] authoredParts)
        {
            var parts = new List<Transform>();
            foreach (Transform part in lounge.GetComponentsInChildren<Transform>(true))
                if (!part.GetComponentInParent<SlopCitySalvageItem>() && part.GetComponent<Renderer>()
                    && !SlopCitySalvageItem.HasProtectedContents(part)
                    && authoredParts.Any(authored => HomePartMatches(part.name, authored))) parts.Add(part);
            if (parts.Count == 0) return;
            // The original siblings keep identical local transforms under a new
            // identity-local owner. No geometry or shared material is copied.
            Transform parent = parts[0].parent;
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            foreach (Transform part in parts)
                if (part.parent == parent) part.SetParent(root, false);
            // Coffee liquid, laptop screens and lettering stay outside the finish.
            // Configure last: saved-state admission may immediately cache renderers.
            ConfigureItem(root, label, "household", true, false, 1);
        }

        private static bool HomePartMatches(string name, string authoredName)
        {
            if (name == authoredName) return true;
            if (!name.StartsWith(authoredName + ".", StringComparison.Ordinal)) return false;
            // Blender's repeated parts use a numeric suffix; do not admit similarly named art.
            for (int index = authoredName.Length + 1; index < name.Length; index++)
                if (name[index] < '0' || name[index] > '9') return false;
            return name.Length > authoredName.Length + 1;
        }

        private void AdmitStreamedFurniture(Transform city)
        {
            var live = new HashSet<int>();
            foreach (var room in city.GetComponentsInChildren<SlopCityInteriorFurnishing>(true))
            {
                int id = room.GetInstanceID(); live.Add(id);
                if (!admittedInteriors.Add(id)) continue;
                PrepareFurniture(room.transform);
            }
            admittedInteriors.RemoveWhere(id => !live.Contains(id));
        }

        private static void PrepareFurniture(Transform room)
        {
            foreach (SlopCityInteractable control in room.GetComponentsInChildren<SlopCityInteractable>(true))
            {
                Transform root = control.transform;
                if (root.GetComponent<SlopCitySalvageItem>() || SlopCitySalvageItem.HasProtectedContents(root)
                    || root.GetComponentInParent<SlopCityDoor>() || root.GetComponentInParent<SlopCityElevator>()) continue;
                bool seat = control.Kind == SlopCityInteractionKind.Bench;
                bool objectBox = control.Kind == SlopCityInteractionKind.Collectible
                    || (control.Kind == SlopCityInteractionKind.Vending && root.name.EndsWith("_Supplies", StringComparison.Ordinal));
                if (!seat && !objectBox) continue;
                bool bed = root.name.EndsWith("_Double_Bed", StringComparison.Ordinal), sofa = root.name.EndsWith("_Sofa", StringComparison.Ordinal);
                string prefix = bed ? root.name.Substring(0, root.name.Length - "_Double_Bed".Length)
                    : sofa ? root.name.Substring(0, root.name.Length - "_Sofa".Length) : root.name;
                var names = bed ? new[] { "_Mattress", "_Headboard", "_Blanket", "_Pillow" }
                    : sofa ? new[] { "_Sofa_Back", "_Sofa_Arm", "_Seat_Cushion" } : new[] { "_Back", "_Legs" };
                if (root.parent)
                    foreach (Transform sibling in root.parent.Cast<Transform>().ToArray())
                        if (names.Any(suffix => sibling.name == prefix + suffix) && !SlopCitySalvageItem.HasProtectedContents(sibling)) sibling.SetParent(root, true);
                ConfigureItem(root, control.DisplayName, seat ? "furniture" : "stock", true, true, seat ? 24 : 8);
            }
            // These are separate authored boxes/assemblies, not the material-wide
            // Room_Content_Batch meshes that mix rugs, foliage and small objects.
            foreach (Transform part in room.GetComponentsInChildren<Transform>(true))
                foreach (string suffix in DustyRoomParts)
                    if (part.name.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        ConfigureDustyPart(part, part.name.Replace('_', ' '));
                        break;
                    }
        }

        private static void PrepareDustyWorld(Transform city)
        {
            foreach (Transform part in city.GetComponentsInChildren<Transform>(true))
            {
                string label;
                switch (part.name)
                {
                    case "Rear_Shelf": label = "Shop shelf"; break;
                    case "Stall_Counter": label = "Market counter"; break;
                    case "Stall_Post": label = "Market stall post"; break;
                    case "Stall_Canopy": label = "Market stall canopy"; break;
                    case "Planter_Base": label = "Planter exterior"; break;
                    case "Signal_Equipment": label = "Signal equipment enclosure"; break;
                    case "Utility_Junction": label = "Utility cabinet"; break;
                    case "Crossing_Control": label = "Crossing control enclosure"; break;
                    case "Crossing_Button": label = "Crossing button"; break;
                    case "Signal_Pole": label = "Signal pole"; break;
                    case "Pump_Base": label = "Fuel pump cabinet"; break;
                    case "Pump_Kickplate": label = "Fuel pump kickplate"; break;
                    case "Pump_Crown": label = "Fuel pump crown"; break;
                    case "Nozzle_Holster": label = "Fuel nozzle holster"; break;
                    case "Menu_Display_Case": label = "Menu case exterior"; break;
                    case "Subway_SeatBank_L": case "Subway_SeatBank_R": label = "Subway seating"; break;
                    default: continue;
                }
                ConfigureDustyPart(part, label);
            }
        }

        private static void ConfigureDustyPart(Transform part, string label)
        {
            // Existing salvage assemblies keep their original identity, pickup flags
            // and cached renderer ownership. Never introduce a nested finish owner.
            if (part.GetComponentInParent<SlopCitySalvageItem>() || SlopCitySalvageItem.IsProtectedArt(part)
                || part.GetComponentInParent<SlopCityDoor>() || part.GetComponentInParent<SlopCityElevator>()) return;
            bool visible = false;
            foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled && !renderer.GetComponent<TextMesh>()
                    && !SlopCitySalvageItem.IsProtectedArt(renderer.transform)) { visible = true; break; }
            if (!visible) return;
            // Only explicit furniture/fixture families reach here. Protected notices
            // inside a cabinet remain excluded by the existing per-renderer Bind policy.
            ConfigureItem(part, label, "furniture", true, false, 1);
        }

        public static bool IsMutable(Renderer renderer)
        {
            if (!renderer) return false;
            var item = renderer.GetComponentInParent<SlopCitySalvageItem>();
            return renderer.GetComponent<SlopCitySalvageSurface>() || (item && (item.DustProne || item.Pickable));
        }

        private static void PrepareSurfaces(Transform city)
        {
            foreach (MeshFilter filter in city.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh || !filter.GetComponent<MeshRenderer>() || SlopCitySalvageItem.IsProtectedArt(filter.transform)) continue;
                bool window = filter.name == "Human_Scale_Window_Panes";
                bool panel = filter.name == "Manufactured_MilitarySF_ArmorEnamel" || filter.name == "Manufactured_MilitarySF_CeramicComposite";
                if (!window && !panel) continue;
                bool horizon = false;
                for (Transform ancestor = filter.transform; ancestor && ancestor != city; ancestor = ancestor.parent)
                    if (ancestor.name.Contains("HorizonTower_")) { horizon = true; break; }
                if (horizon) continue;
                var surface = filter.GetComponent<SlopCitySalvageSurface>();
                if (!surface) surface = filter.gameObject.AddComponent<SlopCitySalvageSurface>();
                if (!surface.Configure(filter, window)) { Dispose(surface); continue; }
                ConfigureItem(filter.transform, window ? "Exterior window pane" : "Exterior armor panel", window ? "window" : "panel", true, true, window ? 12 : 18);
            }
            long total = 0;
            foreach (var surface in city.GetComponentsInChildren<SlopCitySalvageSurface>(true)) total += surface.SourceGeometryBytes;
            if (total > MaximumMutableSourceBytes) throw new InvalidOperationException("Mutable source geometry exceeds its fixed 48MiB ceiling: " + total);
        }

        private static void PrepareLooseObjects(Transform city)
        {
            foreach (Transform root in city.GetComponentsInChildren<Transform>(true))
            {
                string name = root.name;
                if (name != "Street_Lamp" && name != "City_Bench" && name != "Litter_Bin" && name != "Windblown_Paper"
                    && name != "Display_Window" && name != "Shelf_Stock" && name != "Service_Counter"
                    && name != "Listening_Table" && name != "PressurePanel") continue;
                if (root.GetComponent<SlopCitySalvageItem>() || SlopCitySalvageItem.HasProtectedContents(root)) continue;
                if (root.GetComponentInParent<SlopCityElevator>() || root.GetComponentInParent<SlopCityMetroTrain>()
                    || root.GetComponentInParent<SlopCityDoor>()) continue;
                if (name == "Street_Lamp") ConfigureItem(root, "Street lamp", "fixture", true, true, 45);
                else if (name == "City_Bench") ConfigureItem(root, "Street bench", "furniture", true, true, 30);
                else if (name == "Litter_Bin") ConfigureItem(root, "Street recycling bin", "container", true, true, 15);
                else if (name == "Windblown_Paper") ConfigureItem(root, "Discarded paper", "litter", false, true, 1);
                else if (name == "Display_Window") ConfigureItem(root, "Shop display window pane", "window", true, true, 16);
                else if (name == "Shelf_Stock") ConfigureItem(root, "Shop stock object", "stock", true, true, 4);
                else if (name == "Service_Counter" && !root.GetComponent<SlopCityPawnCounter>()) ConfigureItem(root, "Service counter", "furniture", true, true, 35);
                else if (name == "Listening_Table") ConfigureItem(root, "Listening table", "furniture", true, true, 22);
                else if (name == "PressurePanel") ConfigureItem(root, "Wall pressure panel", "panel", true, true, 18);
            }
        }

        private static void GroupCounterParts(Transform city)
        {
            foreach (Transform counter in city.GetComponentsInChildren<Transform>(true).Where(item => item.name == "Service_Counter").ToArray())
            {
                if (!counter.parent) continue;
                foreach (string name in new[] { "Counter_Top", "Counter_Signal" })
                {
                    Transform part = counter.parent.Find(name);
                    if (part) part.SetParent(counter, true);
                }
            }
        }

        private static void ConfigureItem(Transform root, string label, string category, bool dusty, bool pickable, int value)
        {
            var item = root.GetComponent<SlopCitySalvageItem>();
            if (!item) item = root.gameObject.AddComponent<SlopCitySalvageItem>();
            // These exact meshes/transforms can change; no static combined range may outlive their owner.
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
            {
                part.gameObject.isStatic = false;
#if UNITY_EDITOR
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(part.gameObject, 0);
#endif
            }
            if (string.IsNullOrEmpty(item.StableId))
                item.Configure("salvage.v1" + SlopCitySalvageItem.SourceIdentity(root), label, category, dusty, pickable, value);
        }

        private static void PreparePosters(Transform city)
        {
            var materials = new Dictionary<int, Material>();
            Mesh quad = null;
            foreach (MeshFilter filter in city.GetComponentsInChildren<MeshFilter>(true))
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                if (!renderer || !filter.sharedMesh || !renderer.sharedMaterial) continue;
                Material material = renderer.sharedMaterial;
                int index = material.name.LastIndexOf("PublicNotice_", StringComparison.Ordinal);
                if (index >= 0 && int.TryParse(material.name.Substring(index + "PublicNotice_".Length), out int variation)
                    && variation >= 0 && variation < 4)
                {
                    materials[variation] = material;
                    if (filter.name == "Pasted_Public_Notice" || filter.name == "Transit_Public_Notice") quad = filter.sharedMesh;
                }
            }
            if (!quad || materials.Count != 4) throw new InvalidOperationException("Salvage posters require the four existing authored PublicNotice atlas materials and quad.");
            int serial = 0;
            foreach (Transform venue in city.GetComponentsInChildren<Transform>(true)
                .Where(item => item.name.StartsWith("Venue_", StringComparison.Ordinal) && item.name != "Venue_Sign").OrderBy(item => item.name, StringComparer.Ordinal).ToArray())
            {
                // Side-wall gaps avoid the existing east-wall Interior_Poster and
                // west-wall public notices, shelving, the front door and service counter.
                foreach (float side in new[] { -1f, 1f })
                    foreach (float z in new[] { -3.7f, 1.2f, 2.4f, 3.6f })
                        foreach (float height in new[] { .65f, 1.6f, 2.6f })
                            Poster(venue, "Venue_" + serial, new Vector3(side * 6.875f, height, z),
                                Vector3.left * side, new Vector2(.66f, .75f), quad, materials[serial++ % 4]);
                foreach (float x in new[] { -.45f, .45f })
                    Poster(venue, "Rear_" + serial, new Vector3(x, 2.75f, -4.365f), Vector3.forward,
                        new Vector2(.66f, .75f), quad, materials[serial++ % 4]);
            }
            SlopCityMetroWorld metro = city.GetComponentInChildren<SlopCityMetroWorld>(true);
            if (metro)
                foreach (Transform station in metro.Stations)
                    foreach (float side in new[] { -1f, 1f })
                        foreach (float z in new[] { -4.8f, -3.7f, -2.6f, -1.5f })
                            Poster(station, "Metro_" + serial, new Vector3(side * 7.285f, -6.4f, z),
                                Vector3.left * side, new Vector2(.7f, .9f), quad, materials[serial++ % 4]);
        }

        private static void Poster(Transform parent, string id, Vector3 position, Vector3 outward,
            Vector2 dimensions, Mesh quad, Material material)
        {
            string label = PosterPrefix + id;
            if (parent.Find(label)) return;
            var poster = new GameObject(label);
            poster.transform.SetParent(parent, false);
            poster.transform.localPosition = position;
            poster.transform.localRotation = Quaternion.LookRotation(-outward, Vector3.up);
            poster.transform.localScale = new Vector3(dimensions.x, dimensions.y, 1);
            poster.AddComponent<MeshFilter>().sharedMesh = quad;
            MeshRenderer renderer = poster.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            poster.isStatic = true;
        }

        private static void PreparePawnCounter(Transform city)
        {
            Transform venue = city.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == "Venue_SignalYard");
            if (!venue) throw new InvalidOperationException("Pawn service requires the existing walk-in SignalYard venue.");
            Transform counter = venue.Find("Service_Counter");
            if (!counter) throw new InvalidOperationException("SignalYard Service_Counter is missing.");
            if (!counter.GetComponent<SlopCityPawnCounter>()) counter.gameObject.AddComponent<SlopCityPawnCounter>();
            // Authored counters are plain geometry. Give this physical service
            // an interaction owner before target selection or optional interception.
            var control = counter.GetComponent<SlopCityInteractable>();
            if (!control)
            {
                control = counter.gameObject.AddComponent<SlopCityInteractable>();
                control.Configure(SlopCityInteractionKind.Inspect, "Signal Yard pawn counter", false);
                Renderer body = counter.GetComponent<Renderer>();
                control.ConfigureInteractionPoint(body ? body.bounds.center : counter.position);
            }
            ConfigureItem(counter, "Signal Yard pawn shop counter", "counter", true, false, 1);
            if (venue.Find(PawnCounterName)) return;
            TextMesh donor = venue.GetComponentsInChildren<TextMesh>(true).FirstOrDefault(text => text.font && text.GetComponent<Renderer>());
            if (!donor) throw new InvalidOperationException("Pawn counter requires an existing shared venue lettering material.");
            var sign = new GameObject(PawnCounterName);
            sign.transform.SetParent(venue, false);
            sign.transform.localPosition = new Vector3(-3.7f, 1.65f, -1.34f);
            sign.transform.localRotation = Quaternion.Euler(0, 180, 0);
            TextMesh text = sign.AddComponent<TextMesh>();
            text.font = donor.font; text.fontSize = 64; text.characterSize = .075f;
            text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
            text.text = "SIGNAL YARD / PAWN SHOP\nSELL RECOVERED OBJECTS";
            text.color = new Color(.9f, .83f, .58f);
            Renderer renderer = text.GetComponent<Renderer>();
            renderer.sharedMaterial = donor.GetComponent<Renderer>().sharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            sign.isStatic = true;
        }

        private static void PrepareMailboxes(Transform city)
        {
            MeshFilter donor = city.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(filter => filter.name == "Lamp_Base" && filter.sharedMesh);
            if (!donor) throw new InvalidOperationException("Physical mailboxes require an existing shared unit-cube prop mesh.");
            Material metal = donor.GetComponent<Renderer>().sharedMaterial;
            foreach (SlopCityDistrict district in city.GetComponentsInChildren<SlopCityDistrict>(true))
            {
                if (district.Index < 0 || district.Index > 8 || district.transform.Find("Salvage_Mailbox")) continue;
                // Existing lamps at the sidewalk edge establish a verified clear curb strip.
                Transform lamp = district.GetComponentsInChildren<Transform>(true).FirstOrDefault(part => part.name == "Street_Lamp");
                Vector3 point = lamp ? lamp.localPosition + new Vector3(0, 0, 1.15f) : new Vector3(-13, 0, -10.85f);
                var root = new GameObject("Salvage_Mailbox").transform; root.SetParent(district.transform, false); root.localPosition = point;
                Box(root, "Mailbox_Pedestal", new Vector3(0, .36f, 0), new Vector3(.17f, .72f, .17f), donor.sharedMesh, metal);
                Box(root, "Mailbox_Body", new Vector3(0, .97f, 0), new Vector3(.68f, .55f, .43f), donor.sharedMesh, metal);
                Box(root, "Mailbox_Lid", new Vector3(0, 1.26f, 0), new Vector3(.73f, .05f, .47f), donor.sharedMesh, metal);
                ConfigureItem(root, "Street mailbox", "mailbox", true, true, 20);
            }
        }

        private static void Box(Transform parent, string name, Vector3 point, Vector3 scale, Mesh cube, Material material)
        {
            var box = new GameObject(name); box.transform.SetParent(parent, false); box.transform.localPosition = point; box.transform.localScale = scale;
            box.AddComponent<MeshFilter>().sharedMesh = cube; box.AddComponent<MeshRenderer>().sharedMaterial = material;
            box.AddComponent<BoxCollider>();
        }
        private static void Dispose(UnityEngine.Object value) { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
