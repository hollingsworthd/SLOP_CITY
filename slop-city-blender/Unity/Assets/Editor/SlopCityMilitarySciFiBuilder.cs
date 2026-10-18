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
    /// <summary>
    /// Original civic-defense architecture: exterior load paths, armored mechanical crowns
    /// and elevated infrastructure. Existing interiors, lifts, media and walking surfaces stay intact.
    /// Small manufactured parts are baked into metric meshes per material and per structure.
    /// </summary>
    public static class SlopCityMilitarySciFiBuilder
    {
        private const string Folder = "Assets/Art/MilitarySciFi";
        private const string RootName = "City_MilitarySciFiArchitecture";
        private static Material armor, ceramic, steel, dark, marking, cyan, amber;

        public static void Build(Transform city)
        {
            if (!city) throw new ArgumentNullException(nameof(city));
            int screens = CountScreens(city);
            Transform previous = city.Find(RootName);
            if (previous) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            PrepareMaterials();
            Transform root = Root(RootName, city, Vector3.zero);
            Transform[] towers = city.GetComponentsInChildren<Transform>()
                .Where(item => item.name.StartsWith("City_Skyscraper_", StringComparison.Ordinal)).ToArray();
            Transform[] horizon = city.GetComponentsInChildren<Transform>()
                .Where(item => item.name.StartsWith("Skyline_Tower_", StringComparison.Ordinal)).ToArray();
            for (int i = 0; i < towers.Length; i++) LocalTower(root, towers[i], i);
            for (int i = 0; i < horizon.Length; i++) HorizonTower(root, horizon[i], i);
            Viaduct(root, "MilitarySF_North_TransitSpine", new Vector3(0, 0, 78), Quaternion.identity, 176, 47);
            Viaduct(root, "MilitarySF_West_TransitSpine", new Vector3(-79, 0, 0), Quaternion.Euler(0, 90, 0), 184, 61);
            CommunicationsCitadel(root, new Vector3(-14, 0, 153));
            if (CountScreens(city) != screens) throw new InvalidOperationException("Architectural pass changed the live screen anchors.");
            int checks = Validate(city);
            AssetDatabase.SaveAssets();
            Debug.Log("SLOP CITY MILITARY SCI-FI: " + towers.Length + " armored street towers / " + horizon.Length
                + " structural skyline towers / 2 elevated transit spines / original communications citadel. "
                + root.GetComponentsInChildren<Renderer>().Length + " batched renderers; " + checks + " architecture checks.");
        }

        public static int Validate(Transform city)
        {
            Transform root = city ? city.Find(RootName) : null;
            if (!root) throw new InvalidOperationException("Military sci-fi architecture is missing.");
            Transform[] nodes = root.GetComponentsInChildren<Transform>();
            if (nodes.Count(node => node.name.StartsWith("MilitarySF_LocalTower_", StringComparison.Ordinal)) < 20)
                throw new InvalidOperationException("Military architecture must cover the existing street skyline.");
            if (nodes.Count(node => node.name.StartsWith("MilitarySF_HorizonTower_", StringComparison.Ordinal)) < 28)
                throw new InvalidOperationException("Distant structural architecture is incomplete.");
            if (root.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("Architectural dressing introduced an unvalidated collision obstacle.");
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length < 100 || renderers.Length > 1000)
                throw new InvalidOperationException("Military architecture batching/render density is outside its budget.");
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
                if (!filter.sharedMesh || !AssetDatabase.Contains(filter.sharedMesh))
                    throw new InvalidOperationException("Military geometry was not saved for the standalone player.");
            if (CountScreens(city) < 61) throw new InvalidOperationException("The existing native city video network was lost.");
            return 7 + root.GetComponentsInChildren<MeshFilter>().Length;
        }

        private static void LocalTower(Transform parent, Transform tower, int index)
        {
            Vector3 size = Size(tower.Find("Structural_Core"));
            float hx = size.x * .5f, hz = size.z * .5f, height = size.y;
            Transform owner = Root("MilitarySF_LocalTower_" + index.ToString("D2"), parent, parent.InverseTransformPoint(tower.position));
            owner.rotation = tower.rotation;
            var batch = new Batch(owner);
            Material shell = index % 3 == 1 ? ceramic : armor;
            // The load-bearing corners stand outside the existing sign blades. Splayed feet
            // return into the tower above head height, keeping all street approaches open.
            foreach (float x in new[] { -1f, 1f }) foreach (float z in new[] { -1f, 1f })
            {
                Vector3 corner = new Vector3(x * hx, 0, z * hz);
                Vector3 outward = new Vector3(x, 0, z).normalized;
                Vector3 foot = corner + outward * .98f + Vector3.up * 5.20f;
                Vector3 head = corner + outward * .98f + Vector3.up * (height + 3.40f);
                batch.Beam(steel, corner + Vector3.up * 3.74f, foot, .40f, .40f);
                batch.Beam(dark, foot, head, .56f, .43f);
                int courses = Mathf.CeilToInt((head.y - foot.y) / 6.4f);
                float step = (head.y - foot.y) / courses;
                Quaternion facing = Quaternion.LookRotation(outward);
                for (int course = 0; course < courses; course++)
                {
                    Vector3 point = foot + Vector3.up * ((course + .5f) * step) + outward * .045f;
                    batch.Plate(shell, point, new Vector3(.80f, step - .17f, .43f), .19f, .045f, facing);
                    batch.Plate(steel, point + Vector3.up * (step * .37f) + outward * .24f,
                        new Vector3(.58f, .09f, .044f), .02f, .009f, facing);
                    // Restrained operational indicators terminate at panel joints, not full-height neon.
                    if (course == 1 || course == courses - 1)
                        batch.Plate(index % 4 == 0 ? amber : cyan, point - Vector3.up * (step * .34f) + outward * .25f,
                            new Vector3(.047f, .67f, .028f), .01f, .005f, facing);
                }
                if (index % 4 == 3 && x == z)
                {
                    Vector3 roofCorner = corner + outward * 1.0f + Vector3.up * (height + 3.4f);
                    Vector3 crownTip = corner + outward * .50f + Vector3.up * (height + 7.4f);
                    batch.Beam(shell, roofCorner, crownTip, .82f, .57f);
                    batch.Beam(steel, crownTip, new Vector3(x * hx * .40f, height + 3.55f, z * hz * .40f), .30f, .35f);
                }
            }
            // A crown floats over the accessible roof on the external structural frame.
            // Its lowest underside is 3.0m above the roof; lift arrivals and roof decks stay clear.
            float crownY = height + 3.55f;
            if (index % 4 == 0 || index % 4 == 3)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    batch.Beam(shell, new Vector3(-hx - .72f, crownY, side * (hz + .72f)),
                        new Vector3(hx + .72f, crownY, side * (hz + .72f)), .63f, .77f);
                    batch.Beam(steel, new Vector3(side * (hx + .72f), crownY, -hz - .72f),
                        new Vector3(side * (hx + .72f), crownY, hz + .72f), .31f, .47f);
                }
            }
            else if (index % 4 == 1)
            {
                foreach (float side in new[] { -1f, 1f })
                    batch.Profile(shell, new[] { new Vector2(-hx - .72f, 0), new Vector2(hx + .72f, 0),
                        new Vector2(hx + .15f, 2.25f), new Vector2(-hx + .35f, .65f) }, .95f, .075f,
                        Matrix4x4.TRS(new Vector3(0, crownY, side * (hz + .72f)), Quaternion.identity, Vector3.one));
            }
            else
            {
                // A solid, offset plant hood instead of another matching open cage.
                batch.Profile(shell, new[] { new Vector2(-hx - .72f, 0), new Vector2(hx + .72f, 0),
                    new Vector2(hx + .72f, 2.30f), new Vector2(hx * .25f, 3.7f),
                    new Vector2(-hx - .72f, 2.1f) }, size.z * .66f, .095f,
                    Matrix4x4.TRS(new Vector3(0, crownY, hz * .14f), Quaternion.identity, Vector3.one));
                ServiceBay(batch, new Vector3(0, crownY + 1.22f, -hz * .52f - .08f), Vector3.back,
                    size.x * .61f, 1.53f, index);
            }
            LocalFacadePanels(batch, size, index, shell);
            if (index % 4 == 0)
            {
                // Solid perimeter buttresses visually terminate this family of towers.
                // Their inner faces remain beyond the roof parapet and lift approach.
                foreach (float side in new[] { -1f, 1f })
                    batch.Profile(shell, new[] { new Vector2(-hz - .55f, 0), new Vector2(hz + .55f, 0),
                        new Vector2(hz + .55f, 6.0f), new Vector2(hz * .14f, 8.1f),
                        new Vector2(-hz - .55f, 6.9f) }, .73f, .075f,
                        Matrix4x4.TRS(new Vector3(side * (hx + .69f), height - 3.4f, 0), Quaternion.Euler(0, 90, 0), Vector3.one));
            }
            // Upper mechanical band is above every Japanese wall display, avoiding media occlusion.
            float beltY = Mathf.Max(25.0f, height * .73f);
            foreach (float side in new[] { -1f, 1f })
            {
                batch.Plate(shell, new Vector3(0, beltY, side * (hz + .23f)), new Vector3(size.x + .9f, .52f, .40f), .17f, .045f);
                batch.Plate(shell, new Vector3(side * (hx + .23f), beltY, 0), new Vector3(size.z + .9f, .52f, .40f), .17f, .045f, Quaternion.Euler(0, 90, 0));
                ServiceBay(batch, new Vector3(0, height - 4.9f, side * (hz + .12f)), side < 0 ? Vector3.back : Vector3.forward,
                    Mathf.Min(2.6f, size.x * .51f), 2.75f, index);
                ServiceBay(batch, new Vector3(side * (hx + .12f), height - 4.9f, 0), side < 0 ? Vector3.left : Vector3.right,
                    Mathf.Min(2.6f, size.z * .51f), 2.75f, index + 1);
            }
            Entrance(batch, hx, hz, index);
            batch.Save();
            var interaction = owner.gameObject.AddComponent<SlopCityInteractable>();
            interaction.Configure(SlopCityInteractionKind.Inspect, "Civic defense tower / structural armor and service plant", false);
            interaction.ConfigureInteractionPoint(tower.TransformPoint(new Vector3(0, 1.6f, -hz - .8f)));
        }

        private static void LocalFacadePanels(Batch batch, Vector3 size, int index, Material shell)
        {
            // Deep spandrels and asymmetric service cheeks break the uninterrupted glass
            // grid. Their furthest face is 0.27m from the wall, behind all screen/sign faces.
            for (int face = 0; face < 4; face++)
            {
                Vector3 outward = face == 0 ? Vector3.back : face == 1 ? Vector3.right : face == 2 ? Vector3.forward : Vector3.left;
                float width = face % 2 == 0 ? size.x : size.z;
                float depth = face % 2 == 0 ? size.z : size.x;
                Quaternion rotation = Quaternion.LookRotation(outward);
                Matrix4x4 frame = Matrix4x4.TRS(outward * (depth * .5f + .16f), rotation, Vector3.one);
                int style = (index + face) % 3;
                float cheekWidth = width * (style == 0 ? .22f : .15f);
                foreach (float side in new[] { -1f, 1f })
                {
                    float panelWidth = side < 0 && style == 2 ? cheekWidth * 1.7f : cheekWidth;
                    float x = side * (width * .5f - panelWidth * .5f);
                    for (float bottom = 4.9f; bottom < size.y - 3; bottom += 9.6f)
                    {
                        float top = Mathf.Min(bottom + 9.43f, size.y - .48f);
                        batch.Plate(style == 1 ? armor : shell, frame.MultiplyPoint3x4(new Vector3(x, (bottom + top) * .5f, 0)),
                            new Vector3(panelWidth, top - bottom, .20f), .16f, .023f, rotation);
                    }
                }
                float start = style == 0 ? 7.0f : 5.4f;
                for (float y = start; y < size.y - 1.6f; y += style == 2 ? 6.4f : 3.2f)
                {
                    float band = style == 2 ? 1.08f : style == 0 ? .74f : .39f;
                    batch.Plate(shell, frame.MultiplyPoint3x4(new Vector3(0, y, .018f)),
                        new Vector3(width - .18f, band, .19f), .14f, .025f, rotation);
                    // A dark manufactured seam rather than a large uniform illuminated pane.
                    batch.Plate(steel, frame.MultiplyPoint3x4(new Vector3(0, y + band * .39f, .124f)),
                        new Vector3(width * .74f, .045f, .023f), .01f, .005f, rotation);
                }
            }
        }

        private static void Entrance(Batch batch, float hx, float hz, int index)
        {
            float width = Mathf.Min(hx * 1.7f, 3.45f);
            Quaternion forward = Quaternion.Euler(0, 180, 0);
            // An armored brow occupies the existing canopy zone, with an open central lift approach.
            batch.Profile(armor, new[] { new Vector2(-width * .5f, 0), new Vector2(width * .5f, 0),
                new Vector2(width * .5f - .28f, .37f), new Vector2(-width * .5f + .36f, .55f) }, 1.0f, .045f,
                Matrix4x4.TRS(new Vector3(0, 3.10f, -hz - .46f), Quaternion.identity, Vector3.one));
            batch.Plate(dark, new Vector3(0, 3.09f, -hz - .89f), new Vector3(width - .37f, .13f, .07f), .024f, .01f, forward);
            batch.Plate(marking, new Vector3(0, 3.025f, -hz - .91f), new Vector3(width * .5f, .019f, .036f), .004f, .002f, forward);
            foreach (float side in new[] { -1f, 1f })
            {
                batch.Plate(steel, new Vector3(side * 1.135f, 1.66f, -hz - .13f), new Vector3(.13f, 2.38f, .14f), .04f, .017f);
                batch.Plate(amber, new Vector3(side * 1.135f, 2.37f, -hz - .215f), new Vector3(.03f, .30f, .025f), .009f, .003f);
            }
            StencilNumber(batch, new Vector3(width * .29f, 3.34f, -hz - .982f), Vector3.back, index + 1, .35f);
        }

        private static void HorizonTower(Transform parent, Transform tower, int index)
        {
            Vector3 size = Size(tower.Find("Structural_Core"));
            Transform upper = tower.Find("Setback_UpperShaft");
            float height = upper ? upper.localPosition.y + Size(upper).y * .5f : size.y;
            float hx = size.x * .5f, hz = size.z * .5f;
            Transform owner = Root("MilitarySF_HorizonTower_" + index.ToString("D2"), parent, parent.InverseTransformPoint(tower.position));
            owner.rotation = tower.rotation;
            var batch = new Batch(owner);
            Material shell = index % 4 == 0 ? ceramic : armor;
            foreach (float x in new[] { -1f, 1f }) foreach (float z in new[] { -1f, 1f })
            {
                Vector3 bottom = new Vector3(x * (hx + .48f), 2.1f, z * (hz + .48f));
                Vector3 top = new Vector3(x * (hx + .48f), height + 7, z * (hz + .48f));
                batch.Beam(steel, bottom, top, 1.26f, 1.5f);
                for (int tier = 0; tier < 6; tier++)
                {
                    float y = 4 + (tier + .5f) * (height - 4) / 6;
                    Vector3 outward = new Vector3(x, 0, z).normalized;
                    batch.Plate(shell, new Vector3(bottom.x, y, bottom.z) + outward * .3f,
                        new Vector3(2.2f, (height - 4) / 6 - .44f, 1.2f), .45f, .10f, Quaternion.LookRotation(outward));
                }
                if (index % 4 == 2 || (index % 4 == 3 && x == z))
                    batch.Beam(shell, top, new Vector3(x * hx * .5f, height + 16 + index % 3 * 2, z * hz * .52f), 1.3f, 1.7f);
            }
            foreach (float band in new[] { .33f, .69f, 1f })
            {
                float y = height * band + (band == 1 ? 6.3f : 0);
                foreach (float side in new[] { -1f, 1f })
                {
                    batch.Plate(shell, new Vector3(0, y, side * (hz + .7f)), new Vector3(size.x + 2.6f, 1.45f, 1.5f), .45f, .12f);
                    batch.Plate(shell, new Vector3(side * (hx + .7f), y, 0), new Vector3(size.z + 2.6f, 1.45f, 1.5f), .45f, .12f, Quaternion.Euler(0, 90, 0));
                }
            }
            foreach (float side in new[] { -1f, 1f })
            {
                ServiceBay(batch, new Vector3(0, height - 12, side * (hz + .22f)), side < 0 ? Vector3.back : Vector3.forward, size.x * .50f, 7.5f, index);
                batch.Plate(steel, new Vector3(0, 5.5f, side * (hz + .15f)), new Vector3(size.x * .8f, 8, .8f), .75f, .12f);
            }
            HorizonMassing(batch, size, height, index, shell);
            // A stepped technical crown replaces the appearance of a single flat-topped box.
            batch.Plate(dark, new Vector3(0, height + 7.6f, 0), new Vector3(size.x * .55f, 5.7f, size.z * .57f), .65f, .18f);
            if (index % 4 == 0)
                batch.Profile(shell, new[] { new Vector2(-hx * .62f, 0), new Vector2(hx * .62f, 0),
                    new Vector2(hx * .45f, 6.7f), new Vector2(-hx * .30f, 9.4f) }, size.z * .4f, .14f,
                    Matrix4x4.TRS(new Vector3(0, height + 9.6f, 0), Quaternion.identity, Vector3.one));
            else if (index % 4 == 3)
            {
                batch.Plate(steel, new Vector3(-hx * .18f, height + 18, hz * .12f), new Vector3(2.5f, 26, 3.2f), .41f, .14f);
                batch.Plate(amber, new Vector3(-hx * .18f, height + 31.1f, hz * .12f), new Vector3(.38f, .16f, .38f), .045f, .018f);
            }
            batch.Save();
        }

        private static void HorizonMassing(Batch batch, Vector3 size, float height, int index, Material shell)
        {
            // Window areas become intentional vertical slots between solid manufactured
            // shafts. These broad off-grid towers can carry much larger masses than street plots.
            for (int face = 0; face < 4; face++)
            {
                Vector3 outward = face == 0 ? Vector3.back : face == 1 ? Vector3.right : face == 2 ? Vector3.forward : Vector3.left;
                float width = face % 2 == 0 ? size.x : size.z;
                float depth = face % 2 == 0 ? size.z : size.x;
                Quaternion rotation = Quaternion.LookRotation(outward);
                float bottom = 10 + index % 3 * 3;
                float top = height * (index % 3 == 0 ? .89f : .79f);
                foreach (float side in new[] { -1f, 1f })
                {
                    float center = side * width * .315f, panelWidth = width * (index % 3 == 2 ? .26f : .31f);
                    var profile = new[] { new Vector2(center - panelWidth / 2, bottom), new Vector2(center + panelWidth / 2, bottom),
                        new Vector2(center + panelWidth / 2, top - 3.4f), new Vector2(center + panelWidth * .12f, top),
                        new Vector2(center - panelWidth / 2, top - 1.6f) };
                    batch.Profile(shell, profile, 1.10f, .11f,
                        Matrix4x4.TRS(outward * (depth * .5f + .42f), rotation, Vector3.one));
                    for (float y = bottom + 7.5f; y < top - 5; y += 11.2f)
                        batch.Plate(steel, outward * (depth * .5f + 1.01f) + rotation * new Vector3(center, y, 0),
                            new Vector3(panelWidth * .70f, .21f, .09f), .04f, .018f, rotation);
                }
                // Broad intermediate service floors interrupt the repeated window wallpaper.
                for (int tier = 0; tier < 3; tier++)
                {
                    float y = height * (.23f + tier * .205f);
                    batch.Plate(index % 2 == 0 ? ceramic : steel, outward * (depth * .5f + .32f) + Vector3.up * y,
                        new Vector3(width + .4f, 2.7f + index % 3 * .42f, .86f), .52f, .09f, rotation);
                }
            }
            int family = index % 4;
            float hx = size.x * .5f, hz = size.z * .5f;
            if (family == 0 || family == 2)
            {
                // Unequal shoulders make a legible block-scale silhouette, with weight
                // carried back into the original core rather than floating add-on boxes.
                foreach (float side in new[] { -1f, 1f })
                {
                    float y = height * (side < 0 ? .42f : .56f), top = height + (side < 0 ? 3 : 11);
                    float wing = family == 0 ? 4.0f : 2.9f;
                    var profile = new[] { new Vector2(-wing / 2, y), new Vector2(wing / 2, y + 8),
                        new Vector2(wing / 2, top - 7), new Vector2(wing * .10f, top), new Vector2(-wing / 2, top - 2) };
                    batch.Profile(shell, profile, size.z * .61f, .16f,
                        Matrix4x4.TRS(new Vector3(side * (hx + wing * .20f), 0, 0), Quaternion.identity, Vector3.one));
                    ServiceBay(batch, new Vector3(side * (hx + wing * .20f), top - 12, -hz * .315f), Vector3.back,
                        wing * .68f, 5.8f, index);
                }
            }
            else if (family == 1)
            {
                batch.Profile(shell, new[] { new Vector2(-hx - 2.2f, 0), new Vector2(hx + 2.2f, 0),
                    new Vector2(hx + 1.4f, 6.5f), new Vector2(-hx * .2f, 10.2f), new Vector2(-hx - 2.2f, 6.5f) },
                    size.z * .81f, .19f, Matrix4x4.TRS(new Vector3(0, height + 2.7f, 0), Quaternion.identity, Vector3.one));
            }
            else
            {
                // A second structural blade makes this family noticeably asymmetric.
                batch.Profile(ceramic, new[] { new Vector2(-3.1f, 0), new Vector2(3.1f, 0), new Vector2(3.1f, height * .66f),
                    new Vector2(-1.7f, height * .84f), new Vector2(-3.1f, height * .82f) }, size.z * .42f, .15f,
                    Matrix4x4.TRS(new Vector3(-hx - 1.5f, 0, hz * .16f), Quaternion.identity, Vector3.one));
            }
        }

        private static void Viaduct(Transform parent, string name, Vector3 point, Quaternion rotation, float span, float height)
        {
            Transform owner = Root(name, parent, point); owner.localRotation = rotation;
            var batch = new Batch(owner);
            batch.Plate(dark, new Vector3(0, height, 0), new Vector3(span, 3.9f, 8.1f), 1.2f, .16f);
            batch.Plate(steel, new Vector3(0, height - 2.2f, 0), new Vector3(span + 1.5f, .90f, 9.2f), .43f, .13f);
            batch.Plate(armor, new Vector3(0, height + 2.4f, .7f), new Vector3(span, 1.35f, 7.2f), .48f, .12f);
            const int bays = 16;
            for (int bay = 0; bay < bays; bay++)
            {
                float step = span / bays, x = -span / 2 + (bay + .5f) * step;
                foreach (float side in new[] { -1f, 1f })
                {
                    Quaternion facing = side < 0 ? Quaternion.Euler(0, 180, 0) : Quaternion.identity;
                    batch.Plate(bay % 4 == 0 ? ceramic : armor, new Vector3(x, height - .35f, side * 4.13f),
                        new Vector3(step - .23f, 2.45f, .37f), .52f, .08f, facing);
                    batch.Plate(steel, new Vector3(x, height + 1.55f, side * 4.13f), new Vector3(step - .21f, .48f, .22f), .12f, .035f, facing);
                    batch.Plate(bay % 5 == 0 ? amber : cyan, new Vector3(x, height + 1.56f, side * 4.255f),
                        new Vector3(step * .68f, .074f, .042f), .02f, .01f, facing);
                    batch.Beam(steel, new Vector3(x - step * .45f, height - 2.6f, side * 3.6f),
                        new Vector3(x + step * .45f, height - 6.2f, side * 3.6f), .31f, .37f);
                }
                batch.Beam(armor, new Vector3(x, height - 6.2f, -3.6f), new Vector3(x, height - 6.2f, 3.6f), .4f, .6f);
            }
            foreach (float x in new[] { -span * .40f, -span * .14f, span * .14f, span * .40f })
            {
                // Every pylon is beyond the playable street grid. No footprint overlaps a route.
                batch.Profile(armor, new[] { new Vector2(-4.6f, 0), new Vector2(4.6f, 0), new Vector2(2.9f, height - 12),
                    new Vector2(5.3f, height - 2.7f), new Vector2(-5.3f, height - 2.7f), new Vector2(-2.9f, height - 12) }, 8.6f, .14f,
                    Matrix4x4.TRS(new Vector3(x, 0, 0), Quaternion.identity, Vector3.one));
                batch.Plate(steel, new Vector3(x, 2.4f, 0), new Vector3(11.0f, 4.8f, 11.2f), 1.1f, .18f);
                batch.Plate(dark, new Vector3(x, height * .45f, -4.37f), new Vector3(1.9f, height * .55f, .16f), .34f, .04f);
                batch.Plate(ceramic, new Vector3(x - .90f, height * .45f, -4.47f), new Vector3(.18f, height * .44f, .09f), .045f, .02f);
                StencilNumber(batch, new Vector3(x, height - 8.2f, -4.48f), Vector3.back, 40 + (int)(x + span / 2) / 10, 1.15f);
            }
            batch.Save();
        }

        private static void CommunicationsCitadel(Transform parent, Vector3 point)
        {
            Transform owner = Root("MilitarySF_CommunicationsCitadel", parent, point);
            var batch = new Batch(owner);
            batch.Profile(armor, new[] { new Vector2(-17, 0), new Vector2(17, 0), new Vector2(17, 111),
                new Vector2(11, 142), new Vector2(11, 172), new Vector2(-7, 184), new Vector2(-17, 163) }, 29, .23f, Matrix4x4.identity);
            // The split vertical recess and staggered outriggers create a readable landmark.
            batch.Plate(dark, new Vector3(0, 93, -14.57f), new Vector3(8.7f, 139, .25f), 1.2f, .07f);
            foreach (float side in new[] { -1f, 1f })
            {
                batch.Beam(ceramic, new Vector3(side * 15, 4, -15), new Vector3(side * 12, 167, -15), 1.35f, 1.60f);
                batch.Beam(steel, new Vector3(side * 21, 0, 6), new Vector3(side * 15, 124, 6), 3.9f, 4.8f);
                for (int tier = 0; tier < 16; tier++)
                    batch.Plate(tier % 6 == 0 ? amber : cyan, new Vector3(side * 3.17f, 33 + tier * 8.1f, -14.75f),
                        new Vector3(.11f, 3.7f, .07f), .045f, .013f);
                ServiceBay(batch, new Vector3(side * 17.2f, 125, 0), side < 0 ? Vector3.left : Vector3.right, 19, 15, (int)side + 1);
            }
            for (int tier = 0; tier < 8; tier++)
            {
                float y = 29 + tier * 16;
                batch.Plate(steel, new Vector3(0, y, -14.9f), new Vector3(30.4f, .62f, .80f), .25f, .09f);
                batch.Plate(ceramic, new Vector3(0, y + 5, 0), new Vector3(39 - tier * 1.7f, 3.1f, 34), .92f, .14f);
            }
            batch.Plate(dark, new Vector3(0, 186.5f, 0), new Vector3(6, 16, 6), .5f, .13f);
            batch.Plate(steel, new Vector3(0, 197.6f, 0), new Vector3(1.3f, 12, 1.3f), .2f, .08f);
            batch.Plate(amber, new Vector3(0, 203.7f, 0), new Vector3(.50f, .20f, .50f), .08f, .03f);
            batch.Save();
        }

        private static void ServiceBay(Batch batch, Vector3 point, Vector3 outward, float width, float height, int variant)
        {
            Quaternion rotation = Quaternion.LookRotation(outward);
            Matrix4x4 frame = Matrix4x4.TRS(point, rotation, Vector3.one);
            batch.Plate(steel, point, new Vector3(width + .28f, height + .30f, .30f), .27f, .045f, rotation);
            batch.Plate(dark, frame.MultiplyPoint3x4(new Vector3(0, 0, .17f)), new Vector3(width, height, .045f), .20f, .017f, rotation);
            int vents = Mathf.Clamp(Mathf.RoundToInt(height / .31f), 5, 22);
            for (int vent = 0; vent < vents; vent++)
            {
                float y = -height * .39f + vent * height * .78f / (vents - 1);
                batch.Plate(armor, frame.MultiplyPoint3x4(new Vector3(0, y, .23f)), new Vector3(width * .86f, height * .055f, .24f),
                    .03f, .014f, rotation * Quaternion.Euler(-28, 0, 0));
            }
            foreach (float side in new[] { -1f, 1f })
                batch.Plate(variant % 3 == 0 ? amber : cyan, frame.MultiplyPoint3x4(new Vector3(side * width * .46f, 0, .215f)),
                    new Vector3(.022f, height * .45f, .027f), .007f, .005f, rotation);
        }

        private static void StencilNumber(Batch batch, Vector3 point, Vector3 outward, int value, float height)
        {
            // Small embossed equipment IDs, built as stencil geometry and attached to a real surface.
            // These deliberately use no borrowed insignia or franchise typography.
            int[] masks = { 0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f };
            Matrix4x4 frame = Matrix4x4.TRS(point, Quaternion.LookRotation(outward), Vector3.one);
            float width = height * .48f, thick = height * .09f;
            for (int digit = 0; digit < 2; digit++)
            {
                int number = digit == 0 ? (value / 10) % 10 : value % 10;
                // +Z-facing surfaces read local -X from left to right.
                float x = (digit == 0 ? 1 : -1) * height * .31f;
                for (int segment = 0; segment < 7; segment++)
                {
                    if ((masks[number] & (1 << segment)) == 0) continue;
                    bool horizontal = segment == 0 || segment == 3 || segment == 6;
                    float xx = horizontal ? x : x + ((segment == 1 || segment == 2) ? -width / 2 : width / 2);
                    float yy = segment == 0 ? height / 2 : segment == 3 ? -height / 2 : segment == 6 ? 0
                        : ((segment == 1 || segment == 5) ? height / 4 : -height / 4);
                    batch.Plate(marking, frame.MultiplyPoint3x4(new Vector3(xx, yy, .006f)),
                        new Vector3(horizontal ? width : thick, horizontal ? thick : height * .37f, .012f),
                        thick * .25f, .002f, Quaternion.LookRotation(outward));
                }
            }
        }

        private static void PrepareMaterials()
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            armor = Surface("MilitarySF_ArmorEnamel", new Color(.18f, .215f, .23f), "TransitEnamel");
            ceramic = Surface("MilitarySF_CeramicComposite", new Color(.39f, .415f, .40f), "TransitEnamel");
            steel = Surface("MilitarySF_AnodizedStructuralMetal", new Color(.14f, .165f, .18f), "StainlessSteel");
            dark = Surface("MilitarySF_RecessRubber", new Color(.027f, .033f, .037f), "Rubber");
            marking = Surface("MilitarySF_StencilEnamel", new Color(.59f, .62f, .59f), "TransitEnamel");
            cyan = Emissive("MilitarySF_Notice_OperationalCyan", new Color(.18f, .57f, .64f), 1.65f);
            amber = Emissive("MilitarySF_Notice_CautionAmber", new Color(.91f, .47f, .15f), 1.85f);
        }
        private static Material Surface(string name, Color color, string source)
        {
            Material material = Material(name); material.color = color;
            SlopCitySurfaceLibrary.Apply(material, source, true); material.enableInstancing = true;
            // Roughness remains scan-derived, avoiding polished plastic highlights on armored skins.
            EditorUtility.SetDirty(material); return material;
        }
        private static Material Emissive(string name, Color color, float intensity)
        {
            Material material = Material(name); material.color = color;
            material.SetFloat("_Metallic", .08f); material.SetFloat("_Glossiness", .38f);
            material.SetColor("_EmissionColor", color * intensity);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            material.EnableKeyword("_EMISSION"); material.enableInstancing = true; EditorUtility.SetDirty(material); return material;
        }
        private static Material Material(string name)
        {
            string path = Folder + "/" + name + ".mat"; Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
            return material;
        }
        private static int CountScreens(Transform city) => city.GetComponentsInChildren<Transform>(true)
            .Count(item => item.name.StartsWith("SlopCityMediaScreenAnchor_Channel", StringComparison.Ordinal));
        private static Vector3 Size(Transform shape)
        { if (!shape || !shape.GetComponent<MeshFilter>()) throw new InvalidOperationException("Existing tower geometry has no metric mesh."); return shape.GetComponent<MeshFilter>().sharedMesh.bounds.size; }
        private static Transform Root(string name, Transform parent, Vector3 point)
        { var obj = new GameObject(name); obj.transform.SetParent(parent, false); obj.transform.localPosition = point; return obj.transform; }

        /// <summary>Hard-surface extrusion with genuine chamfer faces and metre UVs. No primitive cubes.</summary>
        private sealed class Batch
        {
            private readonly Transform root;
            private readonly Dictionary<Material, Geometry> groups = new Dictionary<Material, Geometry>();
            public Batch(Transform owner) { root = owner; }
            public void Beam(Material material, Vector3 a, Vector3 b, float width, float depth)
            {
                Vector3 vector = b - a; if (vector.sqrMagnitude < .000001f) return;
                Plate(material, (a + b) * .5f, new Vector3(width, vector.magnitude, depth), Mathf.Min(width, depth) * .19f,
                    Mathf.Min(width, depth) * .10f, Quaternion.FromToRotation(Vector3.up, vector.normalized));
            }
            public void Plate(Material material, Vector3 point, Vector3 size, float cut, float bevel, Quaternion rotation = default)
            {
                if (rotation == default(Quaternion)) rotation = Quaternion.identity;
                float x = size.x * .5f, y = size.y * .5f;
                cut = Mathf.Min(cut, Mathf.Min(x, y) * .78f);
                var polygon = new[] { new Vector2(-x + cut, -y), new Vector2(x - cut, -y), new Vector2(x, -y + cut),
                    new Vector2(x, y - cut), new Vector2(x - cut, y), new Vector2(-x + cut, y), new Vector2(-x, y - cut), new Vector2(-x, -y + cut) };
                Profile(material, polygon, size.z, bevel, Matrix4x4.TRS(point, rotation, Vector3.one));
            }
            public void Profile(Material material, Vector2[] polygon, float depth, float bevel, Matrix4x4 matrix)
            {
                if (!groups.TryGetValue(material, out Geometry geometry)) groups.Add(material, geometry = new Geometry());
                Vector2 center = Vector2.zero; foreach (Vector2 point in polygon) center += point; center /= polygon.Length;
                float minRadius = polygon.Min(point => Vector2.Distance(point, center));
                bevel = Mathf.Min(bevel, Mathf.Min(depth * .35f, minRadius * .12f));
                float front = depth * .5f, back = -front;
                // Offset each face by an actual metric distance. Scaling towards the centroid
                // makes the bevel almost disappear across long, narrow architectural panels.
                var inset = new Vector2[polygon.Length];
                for (int i = 0; i < polygon.Length; i++)
                {
                    Vector2 previous = (polygon[i] - polygon[(i + polygon.Length - 1) % polygon.Length]).normalized;
                    Vector2 next = (polygon[(i + 1) % polygon.Length] - polygon[i]).normalized;
                    Vector2 inwardPrevious = new Vector2(-previous.y, previous.x);
                    Vector2 inwardNext = new Vector2(-next.y, next.x);
                    inset[i] = polygon[i] + (inwardPrevious + inwardNext) *
                        (bevel / Mathf.Max(.05f, 1f + Vector2.Dot(inwardPrevious, inwardNext)));
                }
                for (int i = 0; i < polygon.Length; i++)
                {
                    int next = (i + 1) % polygon.Length;
                    Vector2 a = polygon[i], b = polygon[next], ia = inset[i], ib = inset[next];
                    geometry.Triangle(new Vector3(center.x, center.y, front), new Vector3(ia.x, ia.y, front), new Vector3(ib.x, ib.y, front), matrix);
                    geometry.Triangle(new Vector3(center.x, center.y, back), new Vector3(ib.x, ib.y, back), new Vector3(ia.x, ia.y, back), matrix);
                    geometry.Quad(new Vector3(ia.x, ia.y, front), new Vector3(a.x, a.y, front - bevel), new Vector3(b.x, b.y, front - bevel), new Vector3(ib.x, ib.y, front), matrix);
                    geometry.Quad(new Vector3(a.x, a.y, front - bevel), new Vector3(a.x, a.y, back + bevel), new Vector3(b.x, b.y, back + bevel), new Vector3(b.x, b.y, front - bevel), matrix);
                    geometry.Quad(new Vector3(a.x, a.y, back + bevel), new Vector3(ia.x, ia.y, back), new Vector3(ib.x, ib.y, back), new Vector3(b.x, b.y, back + bevel), matrix);
                }
            }
            public void Save()
            {
                foreach (var group in groups)
                {
                    Mesh mesh = group.Value.Build(); mesh.name = root.name + "_" + group.Key.name;
                    string path = Folder + "/" + mesh.name + ".asset";
                    Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (saved) { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); mesh = saved; EditorUtility.SetDirty(saved); }
                    else AssetDatabase.CreateAsset(mesh, path);
                    var obj = new GameObject("Manufactured_" + group.Key.name); obj.transform.SetParent(root, false);
                    obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = group.Key;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                    renderer.shadowCastingMode = ShadowCastingMode.On; obj.isStatic = true;
                }
            }
        }
        private sealed class Geometry
        {
            private readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();
            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Matrix4x4 matrix)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                if (normal.sqrMagnitude < .5f) return;
                int start = vertices.Count;
                foreach (Vector3 p in new[] { a, b, c })
                {
                    vertices.Add(matrix.MultiplyPoint3x4(p)); normals.Add(matrix.MultiplyVector(normal).normalized);
                    uv.Add(Mathf.Abs(normal.z) >= Mathf.Abs(normal.x) && Mathf.Abs(normal.z) >= Mathf.Abs(normal.y) ? new Vector2(p.x, p.y)
                        : Mathf.Abs(normal.y) >= Mathf.Abs(normal.x) ? new Vector2(p.x, p.z) : new Vector2(p.z, p.y));
                }
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Matrix4x4 matrix)
            { Triangle(a, b, c, matrix); Triangle(a, c, d, matrix); }
            public Mesh Build()
            {
                var mesh = new Mesh { indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
            }
        }
    }
}
#endif
