using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>Metric window bays share the occupied floor datum, including setback stories.</summary>
    public sealed class SlopCityMetricFacade : MonoBehaviour
    {
        public const float StoryPitch = 3.2f;
        public const float FloorDatum = .08f;
        public const float WindowSill = .95f;
        public const float WindowHead = 2.65f;
        [SerializeField] private List<Mesh> ownedMeshes = new List<Mesh>();
        [SerializeField] private bool prepared;
        [SerializeField] private int occupiedStories;
#if !UNITY_EDITOR
        // Reuse collection storage across the city's facades, without retaining
        // scene objects after each finalization or allocating a city-sized array
        // once per building during startup.
        private static readonly List<MeshFilter> finalizationFilters = new List<MeshFilter>();
        private static readonly List<MeshCollider> finalizationColliders = new List<MeshCollider>();
        private static readonly List<SkinnedMeshRenderer> finalizationSkins = new List<SkinnedMeshRenderer>();
        private static readonly HashSet<Mesh> liveMeshes = new HashSet<Mesh>();
        private static readonly HashSet<Mesh> physicsMeshes = new HashSet<Mesh>();
#endif
        public bool Prepared => prepared;
        public int OccupiedStories => occupiedStories;
#if UNITY_EDITOR
        public Mesh ArchitecturalFrameSource
        {
            get
            {
                foreach (Mesh mesh in ownedMeshes)
                    if (mesh && mesh.name == "Metric_Reveals_Frames_And_Cornices_MetricMesh") return mesh;
                return null;
            }
        }
#endif

        private void Awake()
        {
            ReleasePreparedCpuData();
        }

        private void ReleasePreparedCpuData()
        {
#if !UNITY_EDITOR
            // Build-scene preparation and static batching still need readable
            // vertices. Once the prepared facade reaches a player, its meshes
            // are immutable render art: building access reads only their bounds
            // and trims the separate original CurtainWall/Masonry facade meshes.
            // Keep Editor geometry readable for visual and interior validation.
            if (!prepared) return;
            bool pending = false;
            foreach (Mesh mesh in ownedMeshes)
                if (mesh && mesh.isReadable) { pending = true; break; }
#if UNITY_WEBGL
            // Unity can decline a selected static source after its ownership reference
            // was detached. That live filter still needs its immutable CPU copy retired.
            pending |= UnbatchedFrameCpuSource() != null;
#endif
            if (!pending) return;
            Transform city = transform;
            while (city.parent && city.name != "SlopCity_NineBlocks") city = city.parent;
            try
            {
                city.GetComponentsInChildren(true, finalizationFilters);
                city.GetComponentsInChildren(true, finalizationColliders);
                city.GetComponentsInChildren(true, finalizationSkins);
                foreach (MeshFilter filter in finalizationFilters)
                    if (filter.sharedMesh) liveMeshes.Add(filter.sharedMesh);
                foreach (SkinnedMeshRenderer skin in finalizationSkins)
                    if (skin.sharedMesh) liveMeshes.Add(skin.sharedMesh);
                foreach (MeshCollider collider in finalizationColliders)
                    if (collider.sharedMesh)
                    { liveMeshes.Add(collider.sharedMesh); physicsMeshes.Add(collider.sharedMesh); }
#if UNITY_WEBGL
                // The fallback may share its source with a collider/skin outside the
                // city hierarchy. Respect all scene users before finalizing that copy.
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    root.GetComponentsInChildren(true, finalizationColliders);
                    root.GetComponentsInChildren(true, finalizationSkins);
                    foreach (MeshCollider collider in finalizationColliders)
                        if (collider.sharedMesh) physicsMeshes.Add(collider.sharedMesh);
                    foreach (SkinnedMeshRenderer skin in finalizationSkins)
                        if (skin.sharedMesh) physicsMeshes.Add(skin.sharedMesh);
                }
#endif
                for (int index = ownedMeshes.Count - 1; index >= 0; index--)
                {
                    Mesh mesh = ownedMeshes[index];
                    if (!mesh) { ownedMeshes.RemoveAt(index); continue; }
                    if (!liveMeshes.Contains(mesh))
                    {
                        // Static batching replaces the filter with a combined
                        // mesh. Retire our now-unused source rather than upload
                        // a second GPU copy solely to discard its CPU data.
                        ownedMeshes.RemoveAt(index);
                        Destroy(mesh);
                    }
                    else if (!physicsMeshes.Contains(mesh) && mesh.isReadable)
                        mesh.UploadMeshData(true);
                }
#if UNITY_WEBGL
                FinalizeUnbatchedFrameCpuData(physicsMeshes);
#endif
            }
            finally
            {
                finalizationFilters.Clear(); finalizationColliders.Clear(); finalizationSkins.Clear();
                liveMeshes.Clear(); physicsMeshes.Clear();
            }
#endif
        }

#if UNITY_EDITOR || UNITY_WEBGL
        private Mesh UnbatchedFrameCpuSource()
        {
            if (!prepared) return null;
            Transform frame = transform.Find("Metric_Facade/Metric_Reveals_Frames_And_Cornices");
            MeshFilter filter = frame ? frame.GetComponent<MeshFilter>() : null;
            MeshRenderer renderer = frame ? frame.GetComponent<MeshRenderer>() : null;
            Mesh mesh = filter ? filter.sharedMesh : null;
            return renderer && !renderer.isPartOfStaticBatch && mesh && mesh.isReadable ? mesh : null;
        }

        // Editor fixtures may exercise this helper explicitly. Production Editor and
        // native Awake retain their existing paths; only a Web player calls it there.
        internal bool FinalizeUnbatchedFrameCpuData(HashSet<Mesh> requiredReadable)
        {
            Mesh mesh = UnbatchedFrameCpuSource();
            if (!mesh || requiredReadable == null || requiredReadable.Contains(mesh)) return false;
            mesh.UploadMeshData(true);
            return true;
        }
#endif

#if UNITY_EDITOR
        /// <summary>The temporary Web scene transfers one immutable frame to the bounded detail owner.</summary>
        public void DetachStreamedFrame(Mesh frame)
        {
            if (!prepared || !frame) throw new InvalidOperationException("Only prepared architectural frames may transfer ownership.");
            // Static preparation may already have retired this source ownership.
            // Remove only this exact identity; panes/stone and readable entrance inputs stay owned.
            ownedMeshes.RemoveAll(mesh => mesh == frame);
        }

        /// <summary>Runs only on the temporary player build scene after geometry preparation.</summary>
        public void PreparePlayerMeshData(HashSet<Mesh> batchingSources, HashSet<Mesh> renderOnlySources,
            out int finalized, out int detached)
        {
            finalized = detached = 0;
            if (!prepared) return;
            for (int index = ownedMeshes.Count - 1; index >= 0; index--)
            {
                Mesh mesh = ownedMeshes[index];
                if (!mesh) continue;
                if (batchingSources.Contains(mesh))
                {
                    // Leave the live MeshFilter's source readable for Unity's
                    // subsequent static batching. Once its filter is replaced,
                    // this ownership list must not keep the unused source in
                    // the serialized player scene alongside the combined mesh.
                    ownedMeshes.RemoveAt(index);
                    detached++;
                }
                else if (renderOnlySources.Contains(mesh) && mesh.isReadable)
                {
                    mesh.UploadMeshData(true);
                    finalized++;
                }
            }
        }
#endif

        public static void Prepare(Transform city)
        {
            if (!city) return;
            SlopCityInteriorGeometry.Initialize();
            foreach (Transform tower in city.GetComponentsInChildren<Transform>(true))
            {
                if (!tower.name.StartsWith("City_Skyscraper_", StringComparison.Ordinal)
                    && !tower.name.StartsWith("Skyline_Tower_", StringComparison.Ordinal)) continue;
                SlopCityMetricFacade facade = tower.GetComponent<SlopCityMetricFacade>();
                if (!facade) facade = tower.gameObject.AddComponent<SlopCityMetricFacade>();
                if (!facade.prepared) facade.Build();
            }
        }

        private void Build()
        {
            Transform core = transform.Find("Structural_Core");
            MeshFilter filter = core ? core.GetComponent<MeshFilter>() : null;
            if (!filter || !filter.sharedMesh) throw new InvalidOperationException("Metric facade needs its original structural dimensions.");
            Vector3 lower = filter.sharedMesh.bounds.size;
            Transform upperRoot = transform.Find("Setback_UpperShaft");
            MeshFilter upperFilter = upperRoot ? upperRoot.GetComponent<MeshFilter>() : null;
            Vector3 upper = upperFilter && upperFilter.sharedMesh ? upperFilter.sharedMesh.bounds.size : Vector3.zero;
            float height = lower.y + upper.y;
            occupiedStories = Mathf.FloorToInt((height + .01f) / StoryPitch);
            // Recover each tower's authored kit before retiring its old, miniature rows.
            // Shared materials retain their scanned albedo/normal/roughness maps and room atlas.
            Material finish = core.GetComponent<Renderer>() ? core.GetComponent<Renderer>().sharedMaterial : SlopCityInteriorGeometry.Wall;
            Material glazing = null, frameFinish = null;
            bool curtain = false;
            foreach (Transform piece in transform)
            {
                Renderer source = piece.GetComponent<Renderer>();
                if (!source) continue;
                if (piece.name == "CurtainWall_Panel" || piece.name == "Masonry_WindowBays")
                { glazing = source.sharedMaterial; curtain |= piece.name == "CurtainWall_Panel"; }
                if (piece.name.StartsWith("Facade_Frames_", StringComparison.Ordinal)) frameFinish = source.sharedMaterial;
            }
            if (!glazing || !frameFinish)
                throw new InvalidOperationException("Metric facade needs its original window atlas and architectural frame materials: " + name);
            int style = curtain ? 0 : finish && finish.name.IndexOf("Limestone", StringComparison.OrdinalIgnoreCase) >= 0 ? 1
                : finish && finish.name.IndexOf("Masonry", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 3;
            int separator = name.LastIndexOf('_');
            int.TryParse(separator >= 0 ? name.Substring(separator + 1) : "0", out int serial);
            var stone = new Surface();
            var panes = new Surface(false);
            var frames = new Surface(false);
            SlopCityDistrict district = GetComponentInParent<SlopCityDistrict>();
            bool joinedVenue = district && district.Index != 2 && district.Index != 4 && district.Index != 5
                && transform.parent == district.transform && transform.localPosition.x < -.01f;
            // The joined venue is an actual occupied room, not an alley. Its west wall
            // shares this tower's east face over nine metres of the ground story.
            float partyNear = SlopCityDenseLayout.SouthBuildToLine - transform.localPosition.z;
            float partyFar = partyNear + 9f;
            for (int floor = 0; floor < occupiedStories; floor++)
            {
                Vector3 size = upper.y > 0 && floor * StoryPitch >= lower.y - .01f ? upper : lower;
                float bottom = floor * StoryPitch + FloorDatum;
                float top = floor == occupiedStories - 1 ? height : bottom + StoryPitch;
                Face(stone, panes, frames, new Vector3(0, 0, -size.z * .5f), Vector3.left, size.x, bottom, top, floor == 0, style, serial, floor);
                Face(stone, panes, frames, new Vector3(0, 0, size.z * .5f), Vector3.right, size.x, bottom, top, false, style, serial + 2, floor);
                Face(stone, panes, frames, new Vector3(-size.x * .5f, 0, 0), Vector3.forward, size.z, bottom, top, false, style, serial + 3, floor);
                if (joinedVenue && bottom < 3.52f)
                {
                    // The east face's right coordinate runs toward negative Z. Clip
                    // decoration itself, including projecting returns/cornices, so it
                    // cannot emerge through the adjoining .22m venue wall into the room.
                    stone.ClipPartyWall(Vector3.back, -partyFar, -partyNear, 3.52f);
                    panes.ClipPartyWall(Vector3.back, -partyFar, -partyNear, 3.52f);
                    frames.ClipPartyWall(Vector3.back, -partyFar, -partyNear, 3.52f);
                }
                Face(stone, panes, frames, new Vector3(size.x * .5f, 0, 0), Vector3.back, size.z, bottom, top, false, style, serial + 5, floor);
                stone.ClearPartyWall(); panes.ClearPartyWall(); frames.ClearPartyWall();
            }
            // A parapet belongs to the roof, not another apparent window story. Keep
            // it at the exterior edge so lift arrivals and roof equipment stay clear.
            Vector3 roofSize = upper.y > 0 ? upper : lower;
            Roofline(frames, roofSize, height, style, serial);
            Transform stage = SlopCityInteriorGeometry.Root(transform, "Metric_Facade");
            int initialMeshes = ownedMeshes.Count;
            try
            {
                // Curtain towers keep dark glass spandrels; masonry/stone towers retain their
                // own opaque cladding. No materials or textures are copied per bay or building.
                stone.Install(stage, "Floor_Aligned_Stonework", curtain ? glazing : finish, ownedMeshes);
                // All panes remain one identifiable mesh of quads at the real occupied-story
                // sill/head. One-sided exterior art leaves occupants' outward view unobstructed.
                panes.Install(stage, "Human_Scale_Window_Panes", glazing, ownedMeshes);
                frames.Install(stage, "Metric_Reveals_Frames_And_Cornices", frameFinish, ownedMeshes);
                foreach (Transform piece in transform)
                {
                    string name = piece.name;
                    if (name == "CurtainWall_Panel" || name == "Masonry_WindowBays"
                        || name.StartsWith("Facade_Frames_", StringComparison.Ordinal)
                        || name.StartsWith("Podium_Joints_", StringComparison.Ordinal)
                        || name == "Podium_Cornice" || name == "Recessed_Floor_Band")
                        SlopCityInteriorGeometry.DisableSolid(piece);
                }
                prepared = true;
                ReleasePreparedCpuData();
            }
            catch
            {
                SlopCityInteriorGeometry.Dispose(stage.gameObject);
                for (int i = ownedMeshes.Count - 1; i >= initialMeshes; i--)
                { DisposeMesh(ownedMeshes[i]); ownedMeshes.RemoveAt(i); }
                throw;
            }
        }

        private static void Face(Surface stone, Surface panes, Surface frames, Vector3 shell, Vector3 right,
            float width, float bottom, float top, bool entrance, int style, int seed, int floor)
        {
            // Full-size bays keep the original kit's proportions without multiplying tiny
            // old windows across every widened frontage. Widths remain .97–1.66m.
            float pitch = style == 0 ? 1.8f : style == 1 ? 2.1f : style == 2 ? 2.0f : 2.1f;
            int bays = Mathf.Max(3, Mathf.RoundToInt(width / pitch));
            float bay = width / bays;
            float proportion = style == 0 ? .92f : style == 1 ? .58f : style == 2 ? .66f : .46f;
            float sill = bottom + WindowSill, head = Mathf.Min(bottom + WindowHead, top - .20f);
            Vector3 outward = Vector3.Cross(right, Vector3.up);
            // Keep the pane outside the .12m physical shell. Reveal depth comes from the
            // decorative frame, never a thicker collision wall or a smaller room.
            Vector3 origin = shell + outward * .13f;
            Vector3 windowOrigin = shell + outward * .071f;
            // A dark room cell supplies the original curtain-wall spandrel identity without
            // introducing extra apparent window rows. Opaque kits use metre-scaled UVs.
            if (style == 0) stone.UseRoom(new Vector2(.262f, .027f), new Vector2(.226f, .090f));
            Band(stone, origin, right, -width * .5f, width * .5f, bottom, sill, entrance);
            Band(stone, origin, right, -width * .5f, width * .5f, head, top, entrance);
            for (int i = 0; i < bays; i++)
            {
                float left = -width * .5f + i * bay, end = left + bay;
                float margin = bay * (1f - proportion) * .5f;
                float paneLeft = left + margin, paneRight = end - margin;
                // Adjacent margins share one pier instead of two touching quads.
                float pierLeft = i == 0 ? left : left - margin;
                Band(stone, origin, right, pierLeft, paneLeft, sill, head, entrance);
                if (i == bays - 1) Band(stone, origin, right, paneRight, end, sill, head, entrance);
                // Independent occupied rooms break the old repeated diagonal light pattern.
                // Every cell still samples the same authored room/curtain atlas; its material
                // and emission remain shared with the original architectural kit.
                uint room = (uint)(seed * 73856093) ^ (uint)(floor * 19349663) ^ (uint)(i * 83492791);
                room ^= room >> 13; room *= 1274126177; room ^= room >> 16;
                int roomX = (int)(room & 3), roomY = (int)((room >> 3) & 7);
                panes.UseRoom(new Vector2(roomX * .25f + .012f, roomY * .125f + .027f), new Vector2(.226f, .090f));
                if (entrance && paneLeft < 1.2f && paneRight > -1.2f)
                {
                    if (paneLeft < -1.2f) Window(stone, panes, frames, origin, windowOrigin, right, paneLeft, -1.2f, sill, head, style);
                    if (paneRight > 1.2f) Window(stone, panes, frames, origin, windowOrigin, right, 1.2f, paneRight, sill, head, style);
                }
                else Window(stone, panes, frames, origin, windowOrigin, right, paneLeft, paneRight, sill, head, style);
            }
            // Cornices follow real slabs; vertical mullions retain their narrow adult-scale
            // sections as the number of bays grows across the widened building.
            float bandHeight = style == 0 ? .10f : style == 1 ? .24f : style == 2 ? .16f : .22f;
            float projection = style == 0 ? .18f : .24f;
            frames.Prism(shell + outward * .071f, right, -width * .5f, width * .5f,
                top - bandHeight, top, projection - .071f);
            if (style != 0 && floor == 2)
            {
                // A recognisable historic street base, below the much taller occupied
                // shaft. Closely stacked mouldings and corbels replace a flat floor stripe.
                frames.Prism(shell + outward * .071f, right, -width * .5f, width * .5f,
                    top - bandHeight - .12f, top - bandHeight - .04f, .19f);
                frames.Prism(shell + outward * .071f, right, -width * .5f, width * .5f,
                    top - .04f, top + .055f, .24f);
                for (int i = 0; i <= bays; i++)
                {
                    float x = Mathf.Lerp(-width * .5f + .12f, width * .5f - .12f, (float)i / bays);
                    frames.Prism(shell + outward * .071f, right, x - .07f, x + .07f,
                        top - bandHeight - .27f, top - bandHeight, .12f);
                }
            }
            if (style == 0)
                for (int i = 0; i <= bays; i++)
                {
                    float x = -width * .5f + i * bay;
                    // Avoid putting a decorative vertical rail through the lobby doorway.
                    if (entrance && x > -1.24f && x < 1.24f) continue;
                    frames.Prism(shell + outward * .071f, right, x - .026f, x + .026f,
                        bottom, top - bandHeight, .10f);
                }
            ArchitecturalRibs(frames, shell, right, width, bottom, top, bays, style, seed, floor);
        }

        private static void ArchitecturalRibs(Surface frames, Vector3 shell, Vector3 right,
            float width, float bottom, float top, int bays, int style, int seed, int floor)
        {
            // Tall, continuous pier ribs make the occupied shaft read as substantial
            // architecture from street height. They sit on real bay divisions, keep
            // glazing clear and begin above pedestrians, signage and lobby openings.
            if (floor == 0) return;
            Vector3 outward = Vector3.Cross(right, Vector3.up);
            int rhythm = style == 0 ? 4 : 3;
            float thickness = style == 0 ? .075f : style == 3 ? .20f : .13f;
            float relief = style == 0 ? .23f : style == 3 ? .32f : .27f;
            Vector3 face = shell + outward * .071f;
            for (int pier = 0; pier <= bays; pier++)
            {
                if (pier != 0 && pier != bays && (pier + seed) % rhythm != 0) continue;
                float x = Mathf.Clamp(-width * .5f + pier * width / bays,
                    -width * .5f + thickness * .5f, width * .5f - thickness * .5f);
                frames.Prism(face, right, x - thickness * .5f, x + thickness * .5f,
                    bottom, top, relief - .071f);
            }
            // Occasional deep shadow ledges group actual floors into larger volumes.
            // Their height is restricted to the slab band below the next window sill.
            if ((floor + seed % 3) % 6 == 5)
            {
                frames.Prism(face, right, -width * .5f, width * .5f,
                    top - .28f, top - .06f, .27f);
                frames.Prism(face, right, -width * .5f, width * .5f,
                    top - .045f, top + .055f, .31f);
            }
        }

        private static void Roofline(Surface frames, Vector3 size, float height, int style, int serial)
        {
            // Three restrained skyline profiles: ribbed concrete battlements, a thin
            // industrial lantern rim, and stepped masonry caps. The retained central
            // mechanical crown/masts remain the tall part of each individual silhouette.
            for (int side = 0; side < 4; side++)
            {
                Vector3 right = side == 0 ? Vector3.left : side == 1 ? Vector3.right
                    : side == 2 ? Vector3.forward : Vector3.back;
                Vector3 shell = side == 0 ? new Vector3(0, 0, -size.z * .5f)
                    : side == 1 ? new Vector3(0, 0, size.z * .5f)
                    : side == 2 ? new Vector3(-size.x * .5f, 0, 0) : new Vector3(size.x * .5f, 0, 0);
                float width = side < 2 ? size.x : size.z;
                Vector3 face = shell + Vector3.Cross(right, Vector3.up) * .071f;
                frames.Prism(face, right, -width * .5f, width * .5f, height + .02f, height + .17f, .19f);
                int profile = (serial + style) % 3;
                if (profile == 0)
                {
                    int piers = Mathf.Max(3, Mathf.RoundToInt(width / 3.8f));
                    for (int pier = 0; pier <= piers; pier++)
                    {
                        float x = Mathf.Lerp(-width * .5f + .12f, width * .5f - .12f, (float)pier / piers);
                        frames.Prism(face, right, x - .11f, x + .11f,
                            height + .17f, height + .78f, .24f);
                    }
                }
                else if (profile == 1)
                {
                    frames.Prism(face, right, -width * .5f, width * .5f,
                        height + .40f, height + .47f, .11f);
                    frames.Prism(face, right, -width * .5f, width * .5f,
                        height + .70f, height + .77f, .11f);
                }
                else
                {
                    frames.Prism(face, right, -width * .5f, width * .5f,
                        height + .20f, height + .40f, .14f);
                    frames.Prism(face, right, -width * .5f + .24f, width * .5f - .24f,
                        height + .43f, height + .59f, .08f);
                }
            }
        }

        private static void Window(Surface stone, Surface panes, Surface frames, Vector3 face, Vector3 recessed,
            Vector3 right, float left, float end, float sill, float head, int style)
        {
            panes.Quad(recessed, right, left, end, sill, head);
            // Full-depth reveal returns make the masonry bays legible from the pavement.
            Vector3 a = recessed + right * left + Vector3.up * sill;
            Vector3 b = recessed + right * end + Vector3.up * sill;
            Vector3 c = recessed + right * end + Vector3.up * head;
            Vector3 d = recessed + right * left + Vector3.up * head;
            if (style == 0) return; // Curtain glazing gets relief from its continuous bronze mullions.
            Vector3 depth = face - recessed;
            // Only the inward-facing reveal face is visible; omit its hidden reverse.
            stone.Return(a + depth, b + depth, b, a, true);
            stone.Return(b + depth, c + depth, c, b, true);
            stone.Return(c + depth, d + depth, d, c, true);
            stone.Return(d + depth, a + depth, a, d, true);
            // One metric sill is enough to catch light. The opaque piers already form the
            // vertical casing; boxing every jamb would multiply mobile mesh memory.
            Vector3 rim = recessed + Vector3.Cross(right, Vector3.up) * .08f;
            frames.Quad(rim, right, left, end, sill, sill + .045f);
            frames.Return(a + (rim - recessed), b + (rim - recessed), b, a);
            if (sill < StoryPitch * 3)
            {
                // Low street stories carry the reference's individual sash casings and
                // projecting lintels. Flat jamb faces bound mesh memory on mobile and do
                // not close the glass opening or add any collision to the public pavement.
                Vector3 casing = face + Vector3.Cross(right, Vector3.up) * .035f;
                float border = style == 1 ? .095f : .065f;
                frames.Quad(casing, right, left - border, left, sill - .035f, head + .075f);
                frames.Quad(casing, right, end, end + border, sill - .035f, head + .075f);
                frames.Quad(casing, right, left - border, end + border, head, head + .11f);
                Vector3 cap = casing + Vector3.Cross(right, Vector3.up) * .06f;
                frames.Quad(cap, right, left - border - .025f, end + border + .025f, head + .095f, head + .15f);
                frames.Quad(rim, right, (left + end) * .5f - .016f, (left + end) * .5f + .016f, sill, head);
                frames.Quad(rim, right, left, end, sill + (head - sill) * .52f, sill + (head - sill) * .52f + .025f);
            }
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in ownedMeshes) DisposeMesh(mesh);
            ownedMeshes.Clear();
        }

        private static void DisposeMesh(Mesh mesh)
        {
            if (!mesh) return;
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }

        private static void Band(Surface target, Vector3 origin, Vector3 right, float left, float end,
            float bottom, float top, bool entrance)
        {
            if (!entrance || bottom >= 2.70f || end <= -1.2f || left >= 1.2f)
            { target.Quad(origin, right, left, end, bottom, top); return; }
            if (left < -1.2f) target.Quad(origin, right, left, -1.2f, bottom, top);
            if (end > 1.2f) target.Quad(origin, right, 1.2f, end, bottom, top);
            if (top > 2.70f) target.Quad(origin, right, Mathf.Max(left, -1.2f), Mathf.Min(end, 1.2f), 2.70f, top);
        }

        private sealed class Surface
        {
            private readonly bool doubleSided;
            internal Surface(bool doubleSided = true) { this.doubleSided = doubleSided; }
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();
            private bool roomUv;
            private Vector2 roomOffset, roomSpan;
            private bool partyWall;
            private Vector3 partyRight;
            private float partyLow, partyHigh, partyHeight;
            private struct ArtVertex
            {
                internal Vector3 point;
                internal Vector2 texture;
                internal ArtVertex(Vector3 point, Vector2 texture) { this.point = point; this.texture = texture; }
            }
            internal void ClipPartyWall(Vector3 right, float low, float high, float height)
            { partyWall = true; partyRight = right; partyLow = low; partyHigh = high; partyHeight = height; }
            internal void ClearPartyWall() { partyWall = false; }
            internal void UseRoom(Vector2 offset, Vector2 span)
            { roomUv = true; roomOffset = offset; roomSpan = span; }
            internal void Quad(Vector3 origin, Vector3 right, float left, float end, float bottom, float top)
            {
                if (end <= left || top <= bottom) return;
                AddQuad(origin + right * left + Vector3.up * bottom,
                    origin + right * end + Vector3.up * bottom,
                    origin + right * end + Vector3.up * top,
                    origin + right * left + Vector3.up * top,
                    new Vector2(left, bottom), new Vector2(end, bottom),
                    new Vector2(end, top), new Vector2(left, top));
            }
            internal void Return(Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool singleSided = false)
            {
                AddQuad(a, b, c, d, Vector2.zero, new Vector2((b - a).magnitude, 0),
                    new Vector2((b - a).magnitude, (d - a).magnitude), new Vector2(0, (d - a).magnitude), singleSided);
            }
            internal void Prism(Vector3 origin, Vector3 right, float left, float end, float bottom, float top, float depth)
            {
                if (end <= left || top <= bottom) return;
                Vector3 forward = Vector3.Cross(right, Vector3.up) * depth;
                Vector3 a = origin + right * left + Vector3.up * bottom;
                Vector3 b = origin + right * end + Vector3.up * bottom;
                Vector3 c = origin + right * end + Vector3.up * top;
                Vector3 d = origin + right * left + Vector3.up * top;
                Quad(origin + forward, right, left, end, bottom, top);
                Return(a, b, b + forward, a + forward);
                Return(b, c, c + forward, b + forward);
                Return(c, d, d + forward, c + forward);
                Return(d, a, a + forward, d + forward);
            }
            private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, bool singleSided = false)
            {
                Vector3 normal = Vector3.Cross(b - a, d - a).normalized;
                if (partyWall)
                {
                    if (roomUv)
                    { ua = roomOffset; ub = roomOffset + new Vector2(roomSpan.x, 0); uc = roomOffset + roomSpan; ud = roomOffset + new Vector2(0, roomSpan.y); }
                    var polygon = new List<ArtVertex> { new ArtVertex(a, ua), new ArtVertex(b, ub), new ArtVertex(c, uc), new ArtVertex(d, ud) };
                    // Three disjoint exterior regions subtract the hidden rectangle.
                    // Interpolated UVs retain metre texture density and room atlas cells
                    // when a window happens to cross either edge of the shared wall.
                    AddPolygon(Clip(polygon, partyRight, partyLow, false), normal, singleSided);
                    AddPolygon(Clip(polygon, partyRight, partyHigh, true), normal, singleSided);
                    List<ArtVertex> above = Clip(Clip(polygon, partyRight, partyLow, true), partyRight, partyHigh, false);
                    AddPolygon(Clip(above, Vector3.up, partyHeight, true), normal, singleSided);
                    return;
                }
                for (int side = 0; side < (doubleSided && !singleSided ? 2 : 1); side++)
                {
                    int first = vertices.Count;
                    vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                    for (int n = 0; n < 4; n++) normals.Add(side == 0 ? normal : -normal);
                    if (roomUv)
                    {
                        uv.Add(roomOffset); uv.Add(roomOffset + new Vector2(roomSpan.x, 0));
                        uv.Add(roomOffset + roomSpan); uv.Add(roomOffset + new Vector2(0, roomSpan.y));
                    }
                    else { uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud); }
                    int x = side == 0 ? 1 : 3, y = side == 0 ? 3 : 1;
                    triangles.Add(first); triangles.Add(first + x); triangles.Add(first + 2);
                    triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + y);
                }
            }
            private static List<ArtVertex> Clip(List<ArtVertex> polygon, Vector3 axis, float edge, bool greater)
            {
                var result = new List<ArtVertex>();
                if (polygon.Count == 0) return result;
                ArtVertex previous = polygon[polygon.Count - 1];
                float previousDistance = Vector3.Dot(previous.point, axis) - edge;
                bool previousInside = greater ? previousDistance >= 0 : previousDistance <= 0;
                foreach (ArtVertex current in polygon)
                {
                    float distance = Vector3.Dot(current.point, axis) - edge;
                    bool inside = greater ? distance >= 0 : distance <= 0;
                    if (inside != previousInside)
                    {
                        float amount = previousDistance / (previousDistance - distance);
                        result.Add(new ArtVertex(Vector3.LerpUnclamped(previous.point, current.point, amount),
                            Vector2.LerpUnclamped(previous.texture, current.texture, amount)));
                    }
                    if (inside) result.Add(current);
                    previous = current; previousDistance = distance; previousInside = inside;
                }
                return result;
            }
            private void AddPolygon(List<ArtVertex> polygon, Vector3 normal, bool singleSided)
            {
                if (polygon.Count < 3) return;
                for (int side = 0; side < (doubleSided && !singleSided ? 2 : 1); side++)
                {
                    int first = vertices.Count;
                    foreach (ArtVertex vertex in polygon)
                    { vertices.Add(vertex.point); uv.Add(vertex.texture); normals.Add(side == 0 ? normal : -normal); }
                    for (int index = 1; index < polygon.Count - 1; index++)
                    {
                        if (Vector3.Cross(polygon[index].point - polygon[0].point,
                            polygon[index + 1].point - polygon[0].point).sqrMagnitude < 1e-12f) continue;
                        triangles.Add(first); triangles.Add(first + (side == 0 ? index : index + 1));
                        triangles.Add(first + (side == 0 ? index + 1 : index));
                    }
                }
            }
            internal void Install(Transform parent, string name, Material finish, List<Mesh> owned)
            {
                Mesh mesh = new Mesh();
                try { owned.Add(mesh); }
                catch { owned.Remove(mesh); DisposeMesh(mesh); throw; }
                mesh.name = name + "_MetricMesh";
                mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                GameObject part = new GameObject(name);
                try
                {
                    part.transform.SetParent(parent, false);
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    Renderer renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterial = finish;
                    renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
                    part.isStatic = true;
                }
                catch { if (Application.isPlaying) Destroy(part); else DestroyImmediate(part); throw; }
            }
        }
    }
}
