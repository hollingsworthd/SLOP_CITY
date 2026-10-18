using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>Small, shared utility props and four deliberate finds for saved city scenes.</summary>
    public static class SlopCityWorldUtilities
    {
        private const string RootName = "SlopCity_Work_Utilities";
        private static readonly Collider[] overlaps = new Collider[64];
        private static Mesh cubeMesh;
        public static int UtilityCount { get; private set; }
        public static int RareCount { get; private set; }
        public static int JunctionCount { get; private set; }
        public static int MaintenancePanelCount { get; private set; }

        public static void Ensure(Transform player)
        {
            // Never add a city to a smoke fixture, preview scene or unrelated world.
            SlopCityDistrict[] districts = UnityEngine.Object.FindObjectsByType<SlopCityDistrict>(FindObjectsSortMode.None);
            Transform city = null;
            foreach (SlopCityDistrict district in districts)
                if (district && district.transform.parent && district.transform.parent.name == "SlopCity_NineBlocks")
                { city = district.transform.parent; break; }
            if (!city) { UtilityCount = RareCount = JunctionCount = MaintenancePanelCount = 0; return; }

            JunctionCount = 0;
            foreach (SlopCityInteractable item in city.GetComponentsInChildren<SlopCityInteractable>())
            {
                if (!item || item.name != "Utility_Junction") continue;
                SlopCityDistrict district = item.GetComponentInParent<SlopCityDistrict>();
                if (!district) continue;
                // The builder's cabinet door faces south; its center is never a destination.
                item.Configure(SlopCityInteractionKind.Terminal, district.DisplayName + " municipal signal junction");
                item.ConfigureInteractionPoint(item.transform.TransformPoint(new Vector3(0, .9f, -.25f)));
                JunctionCount++;
            }

            Transform existing = city.Find(RootName);
            if (existing) { Count(existing); return; }
            Physics.SyncTransforms();
            Transform root = new GameObject(RootName).transform;
            root.SetParent(city, false);
            var resources = root.gameObject.AddComponent<SlopCityUtilityResources>();
            try
            {
                Material steel = Material(resources, "Municipal weathered steel", new Color(.22f, .28f, .27f), .45f);
                Material ceramic = Material(resources, "Municipal ceramic", new Color(.64f, .68f, .61f), .12f);
                Material dark = Material(resources, "Municipal control face", new Color(.035f, .065f, .060f), .08f);
                Material accent = Material(resources, "Municipal label paint", new Color(.59f, .81f, .68f), .1f);

                foreach (SlopCityDistrict district in districts)
                {
                    if (!district || district.transform.parent != city) continue;
                    // Builder-owned southern sidewalk: the sign ends at x=-6.4, the
                    // central entry aisle begins at x=-2. Props face the street and
                    // leave the promenade, existing benches, trees and doors clear.
                    AddUtility(root, district, new Vector3(-5.35f, 0, -13.65f), SlopCityInteractionKind.Fountain,
                        "Municipal_Water_Refill_", "water refill station", new Vector3(.65f, 1.02f, .50f), player,
                        steel, ceramic, dark, accent);
                    AddUtility(root, district, new Vector3(-4.05f, 0, -13.65f), SlopCityInteractionKind.Vending,
                        "Municipal_Supply_Vending_", "free municipal supply dispenser", new Vector3(.74f, 1.48f, .57f), player,
                        steel, ceramic, dark, accent);
                    AddUtility(root, district, new Vector3(-2.75f, 0, -13.65f), SlopCityInteractionKind.Collectible,
                        "Municipal_Salvage_Crate_", "reusable parts crate", new Vector3(.70f, .58f, .54f), player,
                        steel, ceramic, dark, accent);
                    AddUtility(root, district, new Vector3(3.25f, 0, -13.65f), SlopCityInteractionKind.Hydrant,
                        "Municipal_Pressure_Hydrant_", "water pressure test hydrant", new Vector3(.62f, .78f, .46f), player,
                        steel, ceramic, dark, accent);
                }

                foreach (Transform landing in city.GetComponentsInChildren<Transform>())
                    if (landing.name.StartsWith("Tower_Roof_", StringComparison.Ordinal))
                        AddMaintenancePanel(root, landing, player, steel, dark, accent);

                // Stable, explicit lore locations. These are fiction and are never ordinary job targets.
                Transform roof = Find(city, "Tower_Roof_0");
                if (roof) AddRare(root, roof.position + roof.TransformVector(new Vector3(.95f, 0, 0)),
                    roof.TransformDirection(Vector3.back), "roof-first-carrier", "Unregistered rooftop receiver",
                    "A weathered receiver holds a transmission: the first courier carried no parcel, only a frequency. Someone still answers after the city's clocks stop.", player, steel, dark, accent);
                foreach (SlopCityDistrict district in districts)
                    if (district && district.Index == 2 && district.transform.parent == city)
                        AddRare(root, district.transform.TransformPoint(new Vector3(-2.6f, .02f, 2.0f)),
                            district.transform.TransformDirection(Vector3.back), "canal-dry-channel", "Canal maintenance memory",
                            "A maintenance recording remembers water flowing before the advertisements arrived. Its last instruction is simple: leave one channel open for people who have nothing to sell.", player, steel, dark, accent);
                Transform station = Find(city, "Subway_Station_0");
                if (station) AddRare(root, station.TransformPoint(new Vector3(-3.4f, -7.86f, -4.6f)),
                    station.TransformDirection(Vector3.forward), "metro-unsent-ticket", "Undelivered transit recorder",
                    "A passenger left this recorder beside the rear platform wall. Every ticket names tomorrow, but the recorded voice asks someone to remember yesterday's station.", player, steel, dark, accent);
                Transform readingRoom = Find(city, "Venue_BlueBook");
                if (readingRoom) AddRare(root, readingRoom.TransformPoint(new Vector3(3.5f, .88f, 1.15f)),
                    readingRoom.TransformDirection(Vector3.back), "bluebook-margin-note", "Blue Book margin cartridge",
                    "A reader hid a message inside a blank catalogue cartridge: the city's official story ends at the margin. Keep the pages that nobody numbered.", player, steel, dark, accent);
                Physics.SyncTransforms();
                Count(root);
            }
            catch (Exception error)
            {
                root.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(root.gameObject);
                else UnityEngine.Object.DestroyImmediate(root.gameObject);
                UtilityCount = RareCount = MaintenancePanelCount = 0;
                Debug.LogWarning("City utilities could not fully initialize: " + error.Message);
            }
        }

        private static void Count(Transform root)
        {
            UtilityCount = RareCount = MaintenancePanelCount = 0;
            foreach (SlopCityInteractable item in root.GetComponentsInChildren<SlopCityInteractable>())
                if (item.Kind == SlopCityInteractionKind.RareFind) RareCount++;
                else if (item.name.StartsWith("Roof_Maintenance_Panel_", StringComparison.Ordinal)) MaintenancePanelCount++;
                else UtilityCount++;
        }

        private static Transform Find(Transform city, string name)
        {
            foreach (Transform candidate in city.GetComponentsInChildren<Transform>())
                if (candidate.name == name) return candidate;
            return null;
        }

        private static bool Ground(Vector3 candidate, out Vector3 ground)
        {
            ground = candidate;
            if (!Physics.Raycast(candidate + Vector3.up * .45f, Vector3.down, out RaycastHit hit,
                .90f, ~0, QueryTriggerInteraction.Ignore) || hit.normal.y < .8f) return false;
            if (Mathf.Abs(hit.point.y - candidate.y) > .30f) return false;
            ground.y = hit.point.y + .015f;
            return true;
        }

        private static bool Clear(Vector3 center, Vector3 halfSize, Quaternion rotation, Transform player)
        {
            int count = Physics.OverlapBoxNonAlloc(center, halfSize, overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlaps[i];
                if (!collider || !collider.enabled || (player && collider.transform.IsChildOf(player))) continue;
                return false;
            }
            return true;
        }

        private static bool Approach(Vector3 ground, Vector3 outward, Transform player)
        {
            if (!Ground(ground + outward * 1.0f, out Vector3 standing)) return false;
            return Clear(standing + Vector3.up * .94f, new Vector3(.30f, .86f, .30f), Quaternion.identity, player);
        }

        private static void AddUtility(Transform root, SlopCityDistrict district, Vector3 local,
            SlopCityInteractionKind kind, string prefix, string label, Vector3 size, Transform player,
            Material steel, Material ceramic, Material dark, Material accent)
        {
            Vector3 outward = district.transform.TransformDirection(Vector3.back);
            Quaternion rotation = Quaternion.LookRotation(outward);
            if (!Ground(district.transform.TransformPoint(local), out Vector3 ground)
                || !Clear(ground + Vector3.up * (size.y * .5f + .03f), size * .5f + new Vector3(.08f, -.03f, .08f), rotation, player)
                || !Approach(ground, outward, player)) return;
            Transform prop = new GameObject(prefix + district.Index).transform;
            prop.SetParent(root, false); prop.position = ground; prop.rotation = rotation;
            if (kind != SlopCityInteractionKind.Hydrant)
                Box(prop, "Service_Housing", new Vector3(0, size.y * .5f, 0), size, steel, true);
            if (kind == SlopCityInteractionKind.Fountain)
            {
                Box(prop, "Refill_Basin", new Vector3(0, size.y, .08f), new Vector3(.70f, .12f, .58f), ceramic);
                Box(prop, "Refill_Spout", new Vector3(0, size.y + .13f, -.12f), new Vector3(.10f, .18f, .18f), steel);
                Box(prop, "Refill_Label", new Vector3(0, .70f, .257f), new Vector3(.42f, .09f, .015f), accent);
            }
            else if (kind == SlopCityInteractionKind.Vending)
            {
                Box(prop, "Supply_Display", new Vector3(0, 1.04f, .296f), new Vector3(.55f, .50f, .015f), dark);
                Box(prop, "Supply_Slot", new Vector3(0, .46f, .299f), new Vector3(.46f, .18f, .025f), dark);
                Box(prop, "Free_Supply_Button", new Vector3(.19f, .74f, .306f), new Vector3(.10f, .10f, .03f), accent);
            }
            else if (kind == SlopCityInteractionKind.Hydrant)
            {
                Box(prop, "Hydrant_Barrel", new Vector3(0, .35f, 0), new Vector3(.28f, .70f, .28f), steel);
                Box(prop, "Hydrant_Foot", new Vector3(0, .07f, 0), new Vector3(.39f, .14f, .39f), ceramic);
                Box(prop, "Hydrant_Valve_Cap", new Vector3(0, .72f, 0), new Vector3(.35f, .12f, .35f), accent);
                Box(prop, "Hydrant_Side_Outlet", new Vector3(0, .44f, 0), new Vector3(.61f, .15f, .15f), steel);
                Box(prop, "Hydrant_Pressure_Dial", new Vector3(0, .53f, .19f), new Vector3(.17f, .17f, .08f), ceramic);
                BoxCollider body = prop.gameObject.AddComponent<BoxCollider>();
                body.center = new Vector3(0, size.y * .5f, 0); body.size = size;
            }
            else
            {
                Box(prop, "Crate_Lid", new Vector3(0, .59f, 0), new Vector3(.73f, .08f, .57f), ceramic);
                Box(prop, "Parts_Label", new Vector3(0, .35f, .28f), new Vector3(.43f, .12f, .018f), accent);
            }
            SlopCityInteractable item = prop.gameObject.AddComponent<SlopCityInteractable>();
            item.Configure(kind, district.DisplayName + " " + label);
            item.ConfigureInteractionPoint(prop.TransformPoint(new Vector3(0, Mathf.Min(size.y * .8f, 1.15f), size.z * .5f + .035f)));
            Physics.SyncTransforms();
        }

        private static void AddMaintenancePanel(Transform root, Transform landing, Transform player,
            Material steel, Material dark, Material accent)
        {
            Vector3 candidate = landing.position + landing.TransformVector(new Vector3(.75f, 0, .90f));
            Vector3 outward = landing.TransformDirection(Vector3.back);
            Vector3 ground;
            SlopCityBuildingInterior building = landing.GetComponentInParent<SlopCityBuildingInterior>();
            if (building && building.IsTower)
            {
                // The old roof marker predates the real shaft. Prefer front corners with
                // an inward-facing service aisle, retaining the stable panel identity.
                int roof = building.FloorCount - 1;
                Vector2 roofSize = building.GetFloorSize(roof);
                float hx = roofSize.x * .5f, hz = roofSize.y * .5f;
                float roofTop = building.LocalFloorElevations[roof];
                bool placed = false;
                foreach (float frontInset in new[] { .90f, 1.40f, 1.90f })
                {
                    foreach (float side in new[] { 1f, -1f })
                    {
                        candidate = building.transform.TransformPoint(new Vector3(side * (hx - .60f), roofTop + .015f, -hz + frontInset));
                        outward = building.transform.TransformDirection(side > 0f ? Vector3.left : Vector3.right);
                        if (!MaintenanceRoofClearance(building, candidate, outward)
                            || !TryMaintenancePlacement(candidate, outward, player, out ground)) continue;
                        placed = true;
                        break;
                    }
                    if (placed) break;
                }
                if (!placed)
                {
                    candidate = landing.position + landing.TransformVector(new Vector3(.75f, 0, .90f));
                    outward = landing.TransformDirection(Vector3.back);
                    if (!MaintenanceRoofClearance(building, candidate, outward)
                        || !TryMaintenancePlacement(candidate, outward, player, out ground)) return;
                }
                else if (!Ground(candidate, out ground)) return;
            }
            else if (!TryMaintenancePlacement(candidate, outward, player, out ground)) return;
            Quaternion rotation = Quaternion.LookRotation(outward);
            Vector3 size = new Vector3(.46f, 1.02f, .22f);
            string serial = landing.name.Substring("Tower_Roof_".Length);
            Transform prop = new GameObject("Roof_Maintenance_Panel_" + serial).transform;
            prop.SetParent(root, false); prop.position = ground; prop.rotation = rotation;
            Box(prop, "Roof_Service_Cabinet", new Vector3(0, .51f, 0), size, steel, true);
            Box(prop, "Roof_Service_Face", new Vector3(0, .69f, .12f), new Vector3(.34f, .28f, .015f), dark);
            Box(prop, "Roof_Service_Latch", new Vector3(.13f, .38f, .14f), new Vector3(.045f, .13f, .03f), accent);
            SlopCityInteractable item = prop.gameObject.AddComponent<SlopCityInteractable>();
            item.Configure(SlopCityInteractionKind.Inspect, "Roof terrace " + serial + " weatherproof service panel");
            item.ConfigureInteractionPoint(prop.TransformPoint(new Vector3(0, .72f, .15f)));
            Physics.SyncTransforms();
        }

        private static bool TryMaintenancePlacement(Vector3 candidate, Vector3 outward, Transform player, out Vector3 ground)
        {
            return Ground(candidate, out ground)
                && Clear(ground + Vector3.up * .55f, new Vector3(.31f, .49f, .19f), Quaternion.LookRotation(outward), player)
                && Approach(ground, outward, player);
        }

        private static bool MaintenanceRoofClearance(SlopCityBuildingInterior building, Vector3 candidate, Vector3 outward)
        {
            Vector3 local = building.transform.InverseTransformPoint(candidate);
            Vector3 shaft = building.ShaftLocalCenter;
            // Include the cabinet's footprint/margin around the complete shaft and the
            // clear arrival/doorway strip. A service cabinet cannot become a landing blocker.
            if (Mathf.Abs(local.x - shaft.x) < 1.55f && Mathf.Abs(local.z - shaft.z) < 1.55f) return false;
            Vector3 landing = building.transform.InverseTransformPoint(building.LandingFeet(building.FloorCount - 1));
            if (Mathf.Abs(local.x - landing.x) < .95f && Mathf.Abs(local.z - landing.z) < .85f) return false;
            Vector3 approach = building.transform.InverseTransformPoint(candidate + outward);
            return Mathf.Abs(approach.x - shaft.x) >= 1.48f || Mathf.Abs(approach.z - shaft.z) >= 1.48f;
        }

        private static void AddRare(Transform root, Vector3 candidate, Vector3 outward, string id, string label,
            string story, Transform player, Material steel, Material dark, Material accent)
        {
            Quaternion rotation = Quaternion.LookRotation(outward);
            Vector3 size = new Vector3(.38f, .29f, .28f);
            if (!Ground(candidate, out Vector3 ground)
                || !Clear(ground + Vector3.up * .17f, size * .5f, rotation, player)) return;
            // A tabletop find is reached from the aisle below its supporting table.
            Vector3 approach = ground + outward * 1.0f;
            if (!Ground(approach, out Vector3 standing))
            {
                approach.y -= .86f;
                if (!Ground(approach, out standing)) return;
            }
            if (!Clear(standing + Vector3.up * .94f, new Vector3(.30f, .86f, .30f), Quaternion.identity, player)) return;
            Transform prop = new GameObject("Rare_Record_" + id).transform;
            prop.SetParent(root, false); prop.position = ground; prop.rotation = rotation;
            Box(prop, "Archived_Receiver", new Vector3(0, .145f, 0), size, steel, true);
            Box(prop, "Archive_Readout", new Vector3(0, .17f, .15f), new Vector3(.27f, .11f, .015f), dark);
            Box(prop, "Archive_Seal", new Vector3(.10f, .07f, .16f), new Vector3(.07f, .045f, .018f), accent);
            SlopCityInteractable item = prop.gameObject.AddComponent<SlopCityInteractable>();
            item.ConfigureRareFind("slopcity-lore-" + id, label, story);
            item.ConfigureInteractionPoint(prop.TransformPoint(new Vector3(0, .20f, .17f)));
            Physics.SyncTransforms();
        }

        private static Material Material(SlopCityUtilityResources owner, string label, Color color, float metallic)
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            if (!shader || !shader.isSupported) throw new InvalidOperationException("Utility material shader is unavailable.");
            var material = new Material(shader);
            owner.Own(material);
            material.name = label; material.color = color; material.enableInstancing = true;
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .25f);
            return material;
        }

        private static void Box(Transform parent, string label, Vector3 position, Vector3 size, Material material, bool collision = false)
        {
            // One simple body collider per prop; decorative faces never obstruct movement.
            GameObject shape = new GameObject(label);
            shape.transform.SetParent(parent, false); shape.transform.localPosition = position; shape.transform.localScale = size;
            // Reuse Unity's built-in primitive mesh without leaving deferred decorative colliders.
            if (!cubeMesh)
            {
                GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cubeMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
                primitive.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(primitive);
                else UnityEngine.Object.DestroyImmediate(primitive);
            }
            shape.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
            Renderer renderer = shape.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (collision) shape.AddComponent<BoxCollider>();
        }
    }

    // Runtime material ownership follows the generated root through scene unloads.
    internal sealed class SlopCityUtilityResources : MonoBehaviour
    {
        private readonly List<Material> materials = new List<Material>();
        internal void Own(Material material) { materials.Add(material); }
        private void OnDestroy()
        {
            foreach (Material material in materials)
                if (material) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
            materials.Clear();
        }
    }
}
