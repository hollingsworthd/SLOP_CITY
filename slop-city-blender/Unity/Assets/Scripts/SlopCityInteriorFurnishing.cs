using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>Floor-owned room contents: shared-primitive cleaning units and batched floor/plant decoration.</summary>
    public sealed class SlopCityInteriorFurnishing : MonoBehaviour
    {
        private readonly Dictionary<Material, List<CombineInstance>> groups = new Dictionary<Material, List<CombineInstance>>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly Dictionary<string, int> propOrdinals = new Dictionary<string, int>();
        private readonly List<GameObject> cleanProps = new List<GameObject>();
        private Transform cleanProp;
        private string roomId, cleanLabel;
        private bool cleanupWarning;
        public int DetailPieces { get; private set; }
        public int BatchCount => meshes.Count;
        public int CleanablePropCount => cleanProps.Count;

        private void Item(string name, Vector3 point, Vector3 size, Material finish, float yaw = 0, Mesh shape = null)
        {
            bool standalone = !cleanProp && IsDustyDetail(name);
            if (standalone) BeginCleanProp(name);
            if (cleanProp)
            {
                // The owner is identity-local to the floor, so this is exactly the
                // original CombineInstance TRS. Only ownership/presentation changes.
                var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                try
                {
                    part.transform.SetParent(cleanProp, false);
                    part.transform.localPosition = point;
                    part.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                    part.transform.localScale = size;
                    part.GetComponent<MeshFilter>().sharedMesh = shape ? shape : SlopCityInteriorGeometry.UnitCube;
                    Renderer renderer = part.GetComponent<Renderer>();
                    renderer.sharedMaterial = finish;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                }
                catch { SlopCityInteriorGeometry.Dispose(part); throw; }
                DetailPieces++;
                if (standalone) EndCleanProp();
                return;
            }
            // Rugs, foliage and display faces retain their shared-material batches.
            if (!groups.TryGetValue(finish, out List<CombineInstance> batch))
                groups.Add(finish, batch = new List<CombineInstance>());
            batch.Add(new CombineInstance { mesh = shape ? shape : SlopCityInteriorGeometry.UnitCube, transform = Matrix4x4.TRS(point, Quaternion.Euler(0, yaw, 0), size) });
            DetailPieces++;
        }

        private static bool IsDustyDetail(string name)
        {
            switch (name)
            {
                case "Alarm clock": case "Chopping board": case "Stacked dishes":
                case "Folded towel": case "Soap dispenser": case "Reception keyboard":
                case "In tray": case "Visitor ledger": case "Packing scale": case "Binding press":
                case "Soldering station": case "Parts / paper tray": case "Keyboard": case "Mouse":
                case "Rack dial": case "Labelled file / stock carton": return true;
                default: return false;
            }
        }

        private void BeginCleanProp(string label)
        {
            if (cleanProp) throw new InvalidOperationException("Nested room cleaning units are not supported.");
            propOrdinals.TryGetValue(label, out int ordinal);
            propOrdinals[label] = ordinal + 1;
            var owner = new GameObject("Room_Detail_" + roomId + "_" + label.Replace(' ', '_').Replace('/', '_') + "_" + ordinal);
            try
            {
                owner.transform.SetParent(transform, false);
                cleanProps.Add(owner);
                cleanProp = owner.transform;
                cleanLabel = label;
            }
            catch
            {
                cleanProps.Remove(owner);
                SlopCityInteriorGeometry.Dispose(owner);
                throw;
            }
        }

        private void EndCleanProp()
        {
            // Configure emits the existing admission event, which may immediately
            // cache renderer ownership and apply saved strokes. Finish all parts first.
            var item = cleanProp.gameObject.AddComponent<SlopCitySalvageItem>();
            item.Configure("salvage.v1" + SlopCitySalvageItem.SourceIdentity(cleanProp), cleanLabel, "household", true, false, 1);
            cleanProp = null;
            cleanLabel = null;
        }

        private void Round(string name, Vector3 point, Vector3 dimensions, Material finish, bool ball = false)
        {
            // Unity's borrowed cylinder is two units tall; sphere diameter is one.
            if (!ball) dimensions.y *= .5f;
            Item(name, point, dimensions, finish, 0, ball ? SlopCityInteriorGeometry.UnitSphere : SlopCityInteriorGeometry.UnitCylinder);
        }

        private void Book(Vector3 point, int variation)
        {
            BeginCleanProp("Book");
            Item("Book", point, new Vector3(.30f, .045f, .23f), variation % 2 == 0 ? SlopCityInteriorGeometry.Accent : SlopCityInteriorGeometry.Fabric, variation * 7);
            Item("Book pages", point + Vector3.up * .009f, new Vector3(.275f, .021f, .225f), SlopCityInteriorGeometry.Wall, variation * 7);
            EndCleanProp();
        }

        private void Mug(Vector3 point)
        {
            BeginCleanProp("Coffee mug");
            Round("Mug", point, new Vector3(.09f, .11f, .09f), SlopCityInteriorGeometry.Wall);
            Round("Mug opening", point + Vector3.up * .057f, new Vector3(.072f, .008f, .072f), SlopCityInteriorGeometry.Metal);
            Round("Mug handle", point + Vector3.right * .06f, new Vector3(.035f, .08f, .035f), SlopCityInteriorGeometry.Wall, true);
            EndCleanProp();
        }

        public void Decorate(string id, string role, string roomLabel, int side, float width, float south, float north, int variation)
        {
            roomId = id;
            propOrdinals.Clear();
            float x = side * (1.21f + width - 1.45f);
            Material metal = SlopCityInteriorGeometry.Metal, wall = SlopCityInteriorGeometry.Wall, accent = SlopCityInteriorGeometry.Accent;
            if (roomLabel == "BEDROOM / WRITING")
            {
                float bedside = side * (1.21f + width - 2.3f);
                BeginCleanProp("Bedside lamp");
                Round("Bedside lamp stem", new Vector3(bedside, .84f, south + .5f), new Vector3(.07f, .42f, .07f), metal);
                Round("Bedside shade", new Vector3(bedside, 1.07f, south + .5f), new Vector3(.30f, .22f, .30f), wall);
                EndCleanProp();
                Item("Alarm clock", new Vector3(bedside, .68f, south + .69f), new Vector3(.22f, .12f, .06f), accent);
                Book(new Vector3(side * (1.21f + width - 1.55f), .83f, north - .5f), variation);
                Item("Bedroom rug", new Vector3(x, .006f, south + 3.0f), new Vector3(1.8f, .012f, 1.0f), SlopCityInteriorGeometry.Fabric);
                return;
            }
            if (roomLabel.Contains("KITCHEN") || roomLabel.Contains("WASH") || roomLabel.Contains("LAUNDRY"))
            {
                x = side * (1.21f + width - 1.5f);
                BeginCleanProp("Kettle");
                Round("Kettle", new Vector3(x - .57f, 1.12f, south + .42f), new Vector3(.22f, .27f, .22f), metal, true);
                Round("Kettle lid", new Vector3(x - .57f, 1.27f, south + .42f), new Vector3(.16f, .035f, .16f), accent);
                Round("Kettle spout", new Vector3(x - .43f, 1.18f, south + .42f), new Vector3(.15f, .06f, .06f), metal, true);
                EndCleanProp();
                Item("Chopping board", new Vector3(x + .55f, .984f, south + .42f), new Vector3(.38f, .026f, .28f), SlopCityInteriorGeometry.Wood);
                Round("Stacked dishes", new Vector3(x + .80f, 1.01f, south + .56f), new Vector3(.22f, .065f, .22f), wall);
                Mug(new Vector3(x + .67f, 1.04f, south + .28f));
                if (side < 0)
                {
                    float bathX = side * (1.21f + width - 1.45f), bathZ = north - 1.55f;
                    Item("Folded towel", new Vector3(bathX + 1.30f, 1.15f, bathZ - .75f), new Vector3(.04f, .46f, .40f), SlopCityInteriorGeometry.Fabric);
                    Item("Soap dispenser", new Vector3(bathX - 1.00f, .94f, bathZ - .65f), new Vector3(.065f, .14f, .065f), accent);
                }
                return;
            }
            if (roomLabel.Contains("LIVING") || roomLabel.Contains("WAITING"))
            {
                x = side * (1.21f + width - 1.65f);
                float z = north - 2f;
                Mug(new Vector3(x + .35f, .55f, z));
                Book(new Vector3(x - .26f, .53f, z), variation);
                Item("Floor rug", new Vector3(x, .006f, z - .2f), new Vector3(2.5f, .012f, 1.8f), SlopCityInteriorGeometry.Fabric);
                Vector3 plant = new Vector3(side * (1.21f + width - .5f), .93f, north - 2.7f);
                Round("Plant stem", plant - Vector3.up * .10f, new Vector3(.035f, .47f, .035f), SlopCityInteriorGeometry.Wood);
                for (int crown = 0; crown < 3; crown++)
                    Round("Planter foliage", plant + new Vector3((crown - 1) * .11f, crown == 1 ? .15f : 0, .04f * crown),
                        new Vector3(.25f, .40f, .21f), SlopCityInteriorGeometry.Fabric, true);
                return;
            }
            if (roomLabel.Contains("RECEPTION"))
            {
                x = side * (1.21f + width - 1.55f);
                Item("Reception keyboard", new Vector3(x - .55f, 1.075f, south + 1.20f), new Vector3(.42f, .025f, .14f), wall);
                Item("In tray", new Vector3(x + .65f, 1.10f, south + 1.45f), new Vector3(.38f, .07f, .29f), metal);
                Item("Visitor ledger", new Vector3(x + .20f, 1.09f, south + 1.36f), new Vector3(.34f, .035f, .26f), wall);
                return;
            }
            bool workshop = role.IndexOf("workshop", StringComparison.OrdinalIgnoreCase) >= 0 || role.Contains("Repair") || role.Contains("sorting");
            if (workshop)
            {
                for (int tool = 0; tool < 4; tool++)
                {
                    // The rail's room-facing surface is south+.26m. Place complete
                    // grips/shafts beyond it so the rail cannot hide the new tools.
                    BeginCleanProp("Hanging tool");
                    Item("Hanging tool grip", new Vector3(x - .72f + tool * .45f, 1.54f, south + .29f), new Vector3(.055f, .19f, .035f), accent);
                    Item("Hanging tool shaft", new Vector3(x - .72f + tool * .45f, 1.36f, south + .29f), new Vector3(.025f, .18f, .025f), metal);
                    EndCleanProp();
                }
                string task = role.Contains("Market") ? "Packing scale" : role.Contains("Print") ? "Binding press" : "Soldering station";
                Item(task, new Vector3(x + .63f, 1.03f, south + .6f), new Vector3(.42f, .15f, .30f), metal);
                Item("Parts / paper tray", new Vector3(x + .55f, 1.14f, south + .6f), new Vector3(.30f, .08f, .20f), wall);
                return;
            }
            Item("Keyboard", new Vector3(x + .22f, .825f, south + .82f), new Vector3(.44f, .025f, .14f), wall);
            Item("Mouse", new Vector3(x + .54f, .835f, south + .83f), new Vector3(.06f, .04f, .10f), metal);
            Mug(new Vector3(x - .55f, .86f, south + .70f));
            if (role.Contains("Music"))
            {
                BeginCleanProp("Microphone and stand");
                Round("Microphone stand", new Vector3(x - .67f, 1.02f, north - .55f), new Vector3(.025f, .42f, .025f), metal);
                Round("Microphone", new Vector3(x - .67f, 1.26f, north - .55f), new Vector3(.07f, .14f, .07f), metal, true);
                EndCleanProp();
                BeginCleanProp("Mixing controls");
                for (int fader = 0; fader < 6; fader++)
                    Item("Mixing fader", new Vector3(x - .45f + fader * .18f, .907f, north - .53f), new Vector3(.035f, .025f, .075f), wall);
                EndCleanProp();
            }
            else if (role.Contains("Signal") || role.Contains("Broadcast"))
            {
                for (int module = 0; module < 4; module++)
                {
                    Item("Rack display", new Vector3(x - .35f, .38f + module * .4f, north - .834f), new Vector3(.4f, .13f, .015f), SlopCityInteriorGeometry.Fabric);
                    Item("Rack dial", new Vector3(x + .38f, .38f + module * .4f, north - .842f), new Vector3(.09f, .09f, .04f), wall);
                }
            }
            else
            {
                bool shelf = role.IndexOf("archive", StringComparison.OrdinalIgnoreCase) >= 0 || role.Contains("Records")
                    || role.Contains("Reading") || role.Contains("library") || role.Contains("stockroom");
                for (int folder = 0; folder < 6; folder++)
                    Item("Labelled file / stock carton", new Vector3(x - .65f + folder * .25f, shelf ? .815f : .96f, north - (shelf ? .28f : .53f)),
                        new Vector3(.17f, .32f, .32f), folder % 3 == variation ? accent : wall);
            }
        }

        public void Complete()
        {
            foreach (var group in groups)
            {
                Mesh mesh = new Mesh();
                // Own before fallible native processing. Floor unload and failed builds
                // share this component's cleanup, just like normal streamed furniture.
                try { meshes.Add(mesh); }
                catch { meshes.Remove(mesh); if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); throw; }
                mesh.name = "Streamed_Interior_Details";
                mesh.CombineMeshes(group.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                // These finished detail batches are render-only. Retaining another
                // CPU copy for every streamed room needlessly grows the browser heap.
                mesh.UploadMeshData(true);
                Transform batch = SlopCityInteriorGeometry.Root(transform, "Room_Content_Batch");
                batch.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                Renderer renderer = batch.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = group.Key; renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
            }
            groups.Clear();
        }

        // Floor retirement also runs in editor/build scenes, where this ordinary
        // MonoBehaviour may never receive OnDestroy because it has not awakened.
        // Release native batch ownership explicitly at that same disposal boundary.
        internal void Release()
        {
            // Native cleanup of one batch must not skip every remaining room owner.
            for (int index = meshes.Count - 1; index >= 0; index--)
            {
                try
                {
                    Mesh mesh = meshes[index];
                    if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
                    meshes.RemoveAt(index);
                }
                catch (Exception error) { WarnCleanup(error); }
            }
            groups.Clear();
            for (int index = cleanProps.Count - 1; index >= 0; index--)
            {
                try
                {
                    if (cleanProps[index]) SlopCityInteriorGeometry.Dispose(cleanProps[index]);
                    cleanProps.RemoveAt(index);
                }
                catch (Exception error) { WarnCleanup(error); }
            }
            propOrdinals.Clear(); cleanProp = null; cleanLabel = null;
            // Floor retirement retains this component/root when native disposal
            // is refused, instead of destroying its last handle and admitting more.
            if (meshes.Count != 0 || cleanProps.Count != 0)
                throw new InvalidOperationException("Room content cleanup is still pending.");
        }

        private void WarnCleanup(Exception error)
        {
            if (cleanupWarning) return;
            cleanupWarning = true;
            Debug.LogWarning("Room content cleanup is pending: " + error.GetType().Name, this);
        }

        private void OnDestroy() { Release(); }
    }
}
