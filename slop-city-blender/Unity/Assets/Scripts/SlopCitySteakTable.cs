using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SlopCity
{
    /// <summary>Free physical meals in two furnished indoor venues. No economy or save ownership.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCitySteakTable : MonoBehaviour
    {
        public const string TableName = "Steak_Service_Setting";
        public const int BitesPerMeal = 3;
        public const float ActionInterval = .8f;
        public const float MealCooldown = 30f;
        private static readonly List<SlopCitySteakTable> tables = new List<SlopCitySteakTable>(2);
        private static readonly IReadOnlyList<SlopCitySteakTable> readOnlyTables = tables.AsReadOnly();
        private static Geometry geometry;
        private static int resourceUsers;
        private SlopCityFirstPerson owner;
        private SlopCityInteractable target;
        private Transform setting;
        private GameObject plate, garnish;
        private readonly GameObject[] portions = new GameObject[BitesPerMeal];
        private int bitesRemaining;
        private float readyAt;
        private bool initialized, served;
        private bool ownsTarget;
        private SlopCityInteractionKind priorKind;
        private string priorLabel;
        private bool priorEligibility;
        private Vector3 priorPoint;

        public static IReadOnlyList<SlopCitySteakTable> Tables => readOnlyTables;
        public string VenueName { get; private set; }
        public Vector3 InteractionPoint => setting ? setting.TransformPoint(new Vector3(0f, .87f, 0f)) : transform.position;
        public Vector3 ApproachPoint => setting ? setting.TransformPoint(new Vector3(-1.95f, .04f, 0f)) : transform.position;
        public int BitesRemaining => bitesRemaining;
        public bool HasMeal => served && bitesRemaining > 0;
        public string Prompt
        {
            get
            {
                if (Time.time < readyAt) return bitesRemaining > 0 ? "Steak served / enjoy your meal" : "Grill preparing next serving";
                if (bitesRemaining == 3) return "Eat steak / first bite";
                if (bitesRemaining == 2) return "Eat steak / second bite";
                if (bitesRemaining == 1) return "Eat steak / finish your meal";
                return "Order grilled steak / FREE";
            }
        }

        /// <summary>Idempotent saved-scene installation; never furnishes towers or rebuilds authored art.</summary>
        public static void Ensure(SlopCityFirstPerson player)
        {
            if (!player) return;
            Scene scene = player.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) return;
            Transform city = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "SlopCity_NineBlocks") { city = root.transform; break; }
            if (!city) return;
            AddVenue(player, city.Find("District_0_VibeLab/Venue_VibeLab"), "Vibe Lab Steakhouse", "VIBE LAB\nSTEAKHOUSE");
            AddVenue(player, city.Find("District_8_BlueBookPlaza/Venue_BlueBook"), "Blue Book Cafe", "BLUE BOOK CAFE\nSTEAK & READING");
        }

        private static void AddVenue(SlopCityFirstPerson player, Transform venue, string label, string sign)
        {
            if (!venue) return;
            Transform furniture = venue.Find("Listening_Table");
            if (!furniture) return;
            SlopCitySteakTable existing = furniture.GetComponent<SlopCitySteakTable>();
            if (existing) { existing.owner = player; return; }
            Collider surface = furniture.GetComponent<Collider>();
            // Static batching can replace renderer bounds with a combined city's
            // bounds. The authored physical table collider retains its exact extent.
            if (!surface || !surface.enabled) return;
            furniture.gameObject.AddComponent<SlopCitySteakTable>().Initialize(player, venue, label, sign, surface.bounds);
        }

        private void Initialize(SlopCityFirstPerson player, Transform venue, string label, string sign, Bounds tableBounds)
        {
            owner = player;
            VenueName = label;
            if (geometry == null) geometry = new Geometry();
            resourceUsers++;
            initialized = true;
            tables.Add(this);
            setting = new GameObject(TableName).transform;
            setting.SetParent(transform, false);
            setting.position = new Vector3(tableBounds.center.x, tableBounds.max.y - .83f + .005f, tableBounds.center.z);
            setting.rotation = venue.rotation;
            Vector3 inherited = transform.lossyScale;
            setting.localScale = new Vector3(1f / inherited.x, 1f / inherited.y, 1f / inherited.z);
            target = GetComponent<SlopCityInteractable>();
            ownsTarget = !target;
            if (!target) target = gameObject.AddComponent<SlopCityInteractable>();
            else
            {
                priorKind = target.Kind; priorLabel = target.DisplayName;
                priorEligibility = target.ObjectiveEligible; priorPoint = target.InteractionPoint;
            }
            target.Configure(SlopCityInteractionKind.Dining, label + " / steak service", false);
            target.ConfigureInteractionPoint(InteractionPoint);
            AddMenu(venue, sign);
            Shape(setting, "Fork_And_Knife", geometry.cutlery, geometry.silver);
            plate = Shape(setting, "Steak_Plate", geometry.plate, geometry.ceramic).gameObject;
            plate.SetActive(false);
            garnish = Shape(setting, "Grilled_Greens", geometry.garnish, geometry.green).gameObject;
            garnish.SetActive(false);
            for (int index = 0; index < BitesPerMeal; index++)
            {
                Transform portion = Shape(setting, "Steak_Bite_" + (index + 1), geometry.steak[index], geometry.meat);
                portions[index] = portion.gameObject;
                Shape(portion, "Sear_And_Grill_Marks", geometry.marks[index], geometry.charcoal);
                portions[index].SetActive(false);
            }
        }

        private void AddMenu(Transform venue, string sign)
        {
            // Borrow this venue's existing readable sign font/material; do not
            // allocate another texture atlas, fetch art or replace its original signs.
            TextMesh authored = venue.GetComponentInChildren<TextMesh>(true);
            if (!authored || !authored.font) return;
            Shape(setting, "Steakhouse_Menu_Board", geometry.menu, geometry.metal);
            Transform lettering = new GameObject("Steakhouse_Menu_Lettering").transform;
            lettering.SetParent(setting, false);
            lettering.localPosition = new Vector3(.75f, 1.13f, -.041f);
            // Table is reached from its west end; the menu faces that approach.
            lettering.localRotation = Quaternion.Euler(0, 90, 0);
            TextMesh text = lettering.gameObject.AddComponent<TextMesh>();
            text.font = authored.font;
            text.fontSize = 64; text.characterSize = .010f;
            text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
            text.color = new Color(.97f, .85f, .59f);
            text.text = sign + "\nGRILLED STEAK / FREE\nORDER, THEN EAT\nUSE THE TABLE";
            Renderer renderer = text.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Material letteringMaterial = authored.GetComponent<Renderer>().sharedMaterial;
            renderer.sharedMaterial = letteringMaterial;
            lettering.gameObject.AddComponent<SlopCityWorldText>().Configure(letteringMaterial);
        }

        /// <summary>Order once, then take three distinct bites. Only the final bite reports ate.</summary>
        public bool Use(out string message, out bool ate)
        {
            ate = false;
            if (!initialized || !isActiveAndEnabled || !owner || owner.IsPaused || owner.IsSeated)
            { message = "Stand at the restaurant's dining table to order."; return false; }
            Vector3 eye = owner.ViewCamera ? owner.ViewCamera.transform.position : owner.transform.position + Vector3.up * 1.6f;
            if ((eye - InteractionPoint).sqrMagnitude > 3.4f * 3.4f)
            { message = "Come closer to the dining table."; return false; }
            if (Time.time < readyAt)
            {
                message = bitesRemaining > 0 ? "Take a moment between bites." : "The grill is preparing the next serving. Your empty plate remains on the table.";
                return false;
            }
            if (bitesRemaining == 0)
            {
                served = true;
                bitesRemaining = BitesPerMeal;
                plate.SetActive(true); garnish.SetActive(true);
                for (int index = 0; index < portions.Length; index++) portions[index].SetActive(true);
                readyAt = Time.time + ActionInterval;
                target.RecordService("Free grilled steak served on the dining table");
                message = "A grilled steak and greens are on your plate. USE the table to take a bite.";
                return true;
            }
            portions[BitesPerMeal - bitesRemaining].SetActive(false);
            bitesRemaining--;
            readyAt = Time.time + ActionInterval;
            if (bitesRemaining > 0)
            {
                message = bitesRemaining == 2 ? "First bite eaten. Two bites remain on the plate." : "Second bite eaten. One bite remains.";
                return true;
            }
            garnish.SetActive(false);
            readyAt = Time.time + MealCooldown;
            target.RecordService("Grilled steak meal finished; plate empty");
            message = "Steak finished. The plate is empty. You're fed and ready to explore.";
            ate = true;
            return true;
        }

        private static Transform Shape(Transform parent, string label, Mesh mesh, Material material)
        {
            Transform shape = new GameObject(label).transform;
            shape.SetParent(parent, false);
            shape.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = shape.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return shape;
        }

        private void OnDestroy()
        {
            tables.Remove(this);
            if (!initialized) return;
            initialized = false;
            if (setting) Release(setting.gameObject);
            if (target)
            {
                if (ownsTarget) Release(target);
                else
                {
                    target.Configure(priorKind, priorLabel, priorEligibility);
                    target.ConfigureInteractionPoint(priorPoint);
                }
            }
            if (--resourceUsers == 0 && geometry != null) { geometry.Dispose(); geometry = null; }
        }

        private static void Release(Object asset)
        {
            if (!asset) return;
            if (Application.isPlaying) Destroy(asset); else DestroyImmediate(asset);
        }

        // All mesh vertices are authored once in metres. Both tables share the
        // same owned resources; a bite only hides its portion, with no Update work.
        private sealed class Geometry
        {
            internal readonly Material metal, silver, ceramic, meat, charcoal, green;
            internal readonly Mesh menu, cutlery, plate, garnish;
            internal readonly Mesh[] steak = new Mesh[BitesPerMeal], marks = new Mesh[BitesPerMeal];
            private readonly List<Object> owned = new List<Object>(24);

            internal Geometry()
            {
                try
                {
                    Shader shader = Shader.Find("Standard");
                    metal = Material(shader, "Steakhouse menu iron", new Color(.075f, .09f, .085f));
                    silver = Material(shader, "Steakhouse cutlery", new Color(.65f, .68f, .64f), .75f);
                    ceramic = Material(shader, "Steakhouse glazed plate", new Color(.91f, .86f, .73f));
                    meat = Material(shader, "Seared ribeye", new Color(.34f, .13f, .065f));
                    charcoal = Material(shader, "Steak grill marks", new Color(.065f, .035f, .02f));
                    green = Material(shader, "Grilled greens", new Color(.23f, .40f, .09f));
                    var build = new MeshBuilder();
                    build.Box(new Vector3(.78f, 1.13f, 0), new Vector3(.035f, .52f, .72f));
                    build.Box(new Vector3(.78f, .85f, 0), new Vector3(.10f, .04f, .52f));
                    menu = Mesh("Shared steakhouse table menu", build);
                    build = new MeshBuilder();
                    // Fork with four tines, and a broad knife beside the plate.
                    build.Box(new Vector3(-.29f, .831f, -.025f), new Vector3(.019f, .012f, .23f));
                    build.Box(new Vector3(-.29f, .831f, .102f), new Vector3(.055f, .012f, .045f));
                    for (int tine = 0; tine < 4; tine++)
                        build.Box(new Vector3(-.313f + .015f * tine, .831f, .147f), new Vector3(.008f, .012f, .065f));
                    build.Box(new Vector3(.29f, .831f, -.07f), new Vector3(.023f, .016f, .16f));
                    build.Box(new Vector3(.282f, .831f, .09f), new Vector3(.04f, .012f, .16f));
                    cutlery = Mesh("Shared steak cutlery", build);
                    build = new MeshBuilder();
                    build.Disc(new Vector3(0, .83f, 0), .245f, .009f, 32);
                    build.Ring(new Vector3(0, .844f, 0), .21f, .245f, 32);
                    plate = Mesh("Shared steak dinner plate", build);
                    build = new MeshBuilder();
                    for (int stem = 0; stem < 3; stem++)
                        build.Box(new Vector3(-.085f + stem * .07f, .855f, .165f), new Vector3(.022f, .018f, .095f));
                    garnish = Mesh("Shared steak greens", build);

                    var outline = new List<Vector2>
                    {
                        new Vector2(-.175f, -.045f), new Vector2(-.15f, -.105f),
                        new Vector2(-.055f, -.125f), new Vector2(.065f, -.105f),
                        new Vector2(.155f, -.055f), new Vector2(.17f, .005f),
                        new Vector2(.12f, .09f), new Vector2(.035f, .105f),
                        new Vector2(-.07f, .08f), new Vector2(-.155f, .035f)
                    };
                    for (int index = 0; index < BitesPerMeal; index++)
                    {
                        float min = -.18f + index * .12f, max = min + .12f;
                        List<Vector2> slice = Clip(Clip(outline, min, true), max, false);
                        build = new MeshBuilder(); build.Extrude(slice, .85f, .895f);
                        steak[index] = Mesh("Shared ribeye portion " + index, build);
                        build = new MeshBuilder();
                        // Each slice retains its own char lines as earlier bites disappear.
                        for (int mark = 0; mark < 2; mark++)
                            build.Box(new Vector3(min + .035f + mark * .045f, .897f, -.008f), new Vector3(.009f, .003f, index == 1 ? .175f : .105f));
                        marks[index] = Mesh("Shared grill marks " + index, build);
                    }
                }
                catch { Dispose(); throw; }
            }

            private Material Material(Shader shader, string label, Color color, float metallic = 0f)
            {
                var material = new Material(shader);
                try { owned.Add(material); }
                catch { owned.Remove(material); Release(material); throw; }
                material.name = label; material.color = color; material.enableInstancing = true;
                material.SetFloat("_Metallic", metallic); material.SetFloat("_Glossiness", .23f);
                // Small shared emissive fill keeps the meal readable under the market canopy.
                material.SetColor("_EmissionColor", color * .16f); material.EnableKeyword("_EMISSION");
                return material;
            }

            private Mesh Mesh(string label, MeshBuilder build)
            {
                var mesh = new Mesh();
                try { owned.Add(mesh); }
                catch { owned.Remove(mesh); Release(mesh); throw; }
                mesh.name = label;
                mesh.SetVertices(build.vertices); mesh.SetTriangles(build.triangles, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.UploadMeshData(true);
                return mesh;
            }

            internal void Dispose()
            {
                for (int index = 0; index < owned.Count; index++) Release(owned[index]);
                owned.Clear();
            }

            private static List<Vector2> Clip(List<Vector2> polygon, float edge, bool keepGreater)
            {
                var clipped = new List<Vector2>(polygon.Count + 2);
                for (int index = 0; index < polygon.Count; index++)
                {
                    Vector2 a = polygon[index], b = polygon[(index + 1) % polygon.Count];
                    bool aInside = keepGreater ? a.x >= edge : a.x <= edge;
                    bool bInside = keepGreater ? b.x >= edge : b.x <= edge;
                    if (aInside) clipped.Add(a);
                    if (aInside != bInside) clipped.Add(Vector2.Lerp(a, b, (edge - a.x) / (b.x - a.x)));
                }
                return clipped;
            }
        }

        private sealed class MeshBuilder
        {
            internal readonly List<Vector3> vertices = new List<Vector3>();
            internal readonly List<int> triangles = new List<int>();
            private void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int index = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
            }
            private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            { Triangle(a, b, c); Triangle(a, c, d); }
            internal void Box(Vector3 center, Vector3 size)
            {
                Vector3 h = size * .5f;
                Vector3 a = center + new Vector3(-h.x, -h.y, -h.z), b = center + new Vector3(h.x, -h.y, -h.z);
                Vector3 c = center + new Vector3(h.x, -h.y, h.z), d = center + new Vector3(-h.x, -h.y, h.z);
                Vector3 up = Vector3.up * size.y;
                Quad(a, b, c, d); Quad(d + up, c + up, b + up, a + up);
                Quad(a + up, b + up, b, a); Quad(b + up, c + up, c, b);
                Quad(c + up, d + up, d, c); Quad(d + up, a + up, a, d);
            }
            internal void Disc(Vector3 center, float radius, float height, int sides)
            {
                var outline = new List<Vector2>(sides);
                for (int index = 0; index < sides; index++)
                { float angle = index * Mathf.PI * 2 / sides; outline.Add(new Vector2(Mathf.Cos(angle) * radius + center.x, Mathf.Sin(angle) * radius + center.z)); }
                Extrude(outline, center.y, center.y + height);
            }
            internal void Ring(Vector3 center, float inner, float outer, int sides)
            {
                for (int index = 0; index < sides; index++)
                {
                    float a = index * Mathf.PI * 2 / sides, b = (index + 1) * Mathf.PI * 2 / sides;
                    Vector3 av = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), bv = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b));
                    Quad(center + av * inner, center + bv * inner, center + bv * outer, center + av * outer);
                }
            }
            internal void Extrude(List<Vector2> polygon, float bottom, float top)
            {
                if (polygon.Count < 3) return;
                Vector2 center = Vector2.zero;
                for (int index = 0; index < polygon.Count; index++) center += polygon[index];
                center /= polygon.Count;
                for (int index = 0; index < polygon.Count; index++)
                {
                    Vector2 a = polygon[index], b = polygon[(index + 1) % polygon.Count];
                    Vector3 lowA = new Vector3(a.x, bottom, a.y), lowB = new Vector3(b.x, bottom, b.y);
                    Vector3 highA = new Vector3(a.x, top, a.y), highB = new Vector3(b.x, top, b.y);
                    Triangle(new Vector3(center.x, top, center.y), highB, highA);
                    Triangle(new Vector3(center.x, bottom, center.y), lowA, lowB);
                    Quad(lowA, highA, highB, lowB);
                }
            }
        }
    }
}
