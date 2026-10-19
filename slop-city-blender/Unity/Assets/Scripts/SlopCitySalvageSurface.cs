using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>Logical connected pieces share one immutable source and one lazy owned edit mesh.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class SlopCitySalvageSurface : MonoBehaviour
    {
        public const long MaximumChunkBytes = 2 * 1024 * 1024;
        public const long MaximumOwnedBytes = 64 * 1024 * 1024;
        public const int MaximumPartsPerChunk = 4096;
        [SerializeField] private Mesh source;
        [SerializeField] private MeshFilter target;
        [SerializeField] private MeshRenderer display;
        [SerializeField] private MeshCollider collision;
        [SerializeField] private Bounds[] parts = Array.Empty<Bounds>();
        [SerializeField] private int[] starts = Array.Empty<int>(), lengths = Array.Empty<int>(), indices = Array.Empty<int>();
        [SerializeField] private string[] suffixes = Array.Empty<string>();
        [SerializeField] private int[] fixedIndices = Array.Empty<int>();
        [SerializeField] private int[] originalTriangleParts = Array.Empty<int>();
        [SerializeField] private Material authoredMaterial, polishMaterial;
        [SerializeField] private long geometryBytes, editBytes;
        [SerializeField] private bool originalCollisionEnabled;
        [SerializeField] private bool originalForceRenderingOff;
        private bool[] removed;
        private byte[] clean;
        private Mesh working;
        private List<int> scratch;
        private Color32[] colors;
        private bool materialBound;
        private bool pendingSavedState;
        private int savedStateRestoreDepth, indexRebuildCount;
        private bool savedStateRestoreDirty;
        private float nextAdmissionRetry;
        private long workingReservationBytes;
        private bool workingAllocationFailed;
        private long failedAdmissionOwnedBytes;
        private static long ownedBytes;
        private sealed class Finish { public Material material; public int users; public bool owned; }
        private static readonly Dictionary<Material, Finish> Finishes = new Dictionary<Material, Finish>();
        public int PartCount => parts.Length;
        public Mesh SourceMesh => source;
        public Mesh WorkingMesh => working;
        public long SourceGeometryBytes => geometryBytes;
        public static long OwnedBytes => ownedBytes;
        public static int SharedFinishCount => Finishes.Count;
        public int IndexRebuildCount => indexRebuildCount;

        public bool Configure(MeshFilter filter, bool window)
        {
            if (source) return true;
            if (!filter || !filter.sharedMesh || !filter.sharedMesh.isReadable || filter.sharedMesh.subMeshCount != 1
                || filter.sharedMesh.GetTopology(0) != MeshTopology.Triangles) return false;
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (!renderer || renderer.sharedMaterials.Length != 1 || !renderer.sharedMaterial) return false;
            Mesh mesh = filter.sharedMesh;
            long bytes = mesh.GetIndexCount(0) * (mesh.indexFormat == IndexFormat.UInt16 ? 2L : 4L);
            foreach (var attribute in mesh.GetVertexAttributes()) bytes += (long)mesh.vertexCount * attribute.dimension * FormatBytes(attribute.format);
            if (bytes > MaximumChunkBytes) throw new InvalidOperationException("Mutable mesh exceeds its explicit chunk ceiling: " + filter.name);
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var groups = new int[vertices.Length];
            var coincident = new Dictionary<Vector3Int, int>();
            for (int index = 0; index < vertices.Length; index++)
            {
                groups[index] = index;
                Vector3 p = vertices[index];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 10000), Mathf.RoundToInt(p.y * 10000), Mathf.RoundToInt(p.z * 10000));
                if (coincident.TryGetValue(key, out int other)) Union(groups, index, other); else coincident[key] = index;
            }
            for (int index = 0; index < triangles.Length; index += 3)
            { Union(groups, triangles[index], triangles[index + 1]); Union(groups, triangles[index], triangles[index + 2]); }
            var bounds = new Dictionary<int, Bounds>();
            var grouped = new Dictionary<int, List<int>>();
            for (int index = 0; index < triangles.Length; index++)
            {
                int vertex = triangles[index], group = Find(groups, vertex);
                if (!grouped.TryGetValue(group, out var list)) grouped[group] = list = new List<int>();
                list.Add(vertex);
                if (bounds.TryGetValue(group, out Bounds box)) { box.Encapsulate(vertices[vertex]); bounds[group] = box; }
                else bounds[group] = new Bounds(vertices[vertex], Vector3.zero);
            }
            var ordered = new List<int>(grouped.Keys);
            ordered.Sort((left, right) =>
            {
                Vector3 a = bounds[left].center, b = bounds[right].center;
                int compare = a.x.CompareTo(b.x); if (compare == 0) compare = a.y.CompareTo(b.y);
                if (compare == 0) compare = a.z.CompareTo(b.z); return compare == 0 ? left.CompareTo(right) : compare;
            });
            var boxes = new List<Bounds>(); var firsts = new List<int>(); var counts = new List<int>();
            var ids = new List<string>(); var mutable = new List<int>(); var retained = new List<int>();
            var unique = new HashSet<string>();
            var partByGroup = new Dictionary<int, int>();
            foreach (int group in ordered)
            {
                Bounds box = bounds[group]; Vector3 size = box.size;
                // Only genuine vertical facade panes/panels. Horizontal armor decks,
                // huge combined masses and thin printed marks are never floor pickups.
                bool eligible = window || (size.y > .18f && Mathf.Max(size.x, size.z) > .15f
                    && size.y >= Mathf.Min(size.x, size.z) * 1.5f && size.y < 8f && Mathf.Max(size.x, size.z) < 12f);
                if (!eligible) { retained.AddRange(grouped[group]); continue; }
                string id = PositionKey(box.center) + "_" + PositionKey(box.size);
                if (!unique.Add(id)) throw new InvalidOperationException("Ambiguous coincident salvage parts: " + filter.name);
                partByGroup[group] = boxes.Count;
                boxes.Add(box); firsts.Add(mutable.Count); counts.Add(grouped[group].Count); ids.Add(id);
                mutable.AddRange(grouped[group]);
            }
            if (boxes.Count == 0) return false;
            if (boxes.Count > MaximumPartsPerChunk) throw new InvalidOperationException("Mutable mesh has too many logical pieces: " + filter.name);
            source = mesh; target = filter; display = renderer; geometryBytes = bytes;
            parts = boxes.ToArray(); starts = firsts.ToArray(); lengths = counts.ToArray(); suffixes = ids.ToArray();
            indices = mutable.ToArray(); fixedIndices = retained.ToArray();
            originalTriangleParts = new int[triangles.Length / 3];
            for (int triangle = 0; triangle < originalTriangleParts.Length; triangle++)
                originalTriangleParts[triangle] = partByGroup.TryGetValue(Find(groups, triangles[triangle * 3]), out int part) ? part : -1;
            // Both the readable working Mesh color stream and our retained upload
            // array own four bytes per vertex. The index scratch is separate too.
            editBytes = geometryBytes + (long)mesh.vertexCount * 8 + (long)triangles.Length * sizeof(int);
            collision = filter.GetComponent<MeshCollider>();
            if (!collision) collision = filter.gameObject.AddComponent<MeshCollider>();
            collision.sharedMesh = source; collision.convex = false; collision.isTrigger = false;
            originalCollisionEnabled = collision.enabled;
            originalForceRenderingOff = display.forceRenderingOff;
            authoredMaterial = renderer.sharedMaterial;
            BindMaterial();
            return true;
        }

        private void Awake() { BindMaterial(); }
        private void OnEnable() { workingAllocationFailed = false; }
        private void BindMaterial()
        {
            if (materialBound || !authoredMaterial || !display) return;
            Material previous = display.sharedMaterial;
            Material candidate = null;
            Finish finish = null;
            bool created = false;
            try
            {
                if (!Finishes.TryGetValue(authoredMaterial, out finish))
                {
                    Shader shader = Shader.Find("SlopCity/SalvagePolish");
                    if (!shader) throw new InvalidOperationException("Salvage polish shader is missing.");
                    bool supplied = polishMaterial && polishMaterial.shader == shader;
                    Material material = polishMaterial;
                    if (!supplied)
                    {
                        // Keep the local owner before any fallible native setter/cache admission.
                        candidate = material = new Material(authoredMaterial);
                        material.name = authoredMaterial.name + "_SalvagePolish";
                        material.shader = shader;
                        material.SetFloat("_HasMetallicMap", authoredMaterial.GetTexture("_MetallicGlossMap") ? 1 : 0);
                        material.SetFloat("_HasNormalMap", authoredMaterial.GetTexture("_BumpMap") ? 1 : 0);
                        material.SetFloat("_HasOcclusionMap", authoredMaterial.GetTexture("_OcclusionMap") ? 1 : 0);
                        material.SetFloat("_WetFilmCoverage", 0);
                    }
                    finish = new Finish { material = material, owned = !supplied };
                    created = true;
                    Finishes.Add(authoredMaterial, finish);
                }
                display.sharedMaterial = finish.material;
                finish.users++;
                polishMaterial = finish.material;
                materialBound = true;
            }
            catch
            {
                try
                {
                    // A native setter can throw after changing its external binding.
                    if (finish != null && display && display.sharedMaterial == finish.material) display.sharedMaterial = previous;
                }
                finally
                {
                    if (created && Finishes.TryGetValue(authoredMaterial, out Finish cached) && ReferenceEquals(cached, finish))
                        Finishes.Remove(authoredMaterial);
                    if (candidate) Dispose(candidate);
                }
                throw;
            }
        }

        public string PartId(int part)
        {
            ValidatePart(part);
            var item = GetComponent<SlopCitySalvageItem>();
            return (item ? item.StableId : SlopCitySalvageItem.SourceIdentity(transform)) + "/piece/" + suffixes[part];
        }
        /// <summary>Resolve a persisted ID without constructing every possible part ID.</summary>
        public bool TryGetPart(string id, out int part)
        {
            part = -1;
            var item = GetComponent<SlopCitySalvageItem>();
            if (!item || string.IsNullOrEmpty(id)) return false;
            string owner = item.StableId;
            const string marker = "/piece/";
            int offset = owner.Length + marker.Length;
            if (id.Length <= offset || string.CompareOrdinal(id, 0, owner, 0, owner.Length) != 0
                || string.CompareOrdinal(id, owner.Length, marker, 0, marker.Length) != 0) return false;
            int length = id.Length - offset;
            for (int index = 0; index < suffixes.Length; index++)
                if (suffixes[index].Length == length && string.CompareOrdinal(id, offset, suffixes[index], 0, length) == 0)
                { part = index; return true; }
            return false;
        }
        public Bounds PartWorldBounds(int part)
        {
            ValidatePart(part);
            Bounds local = parts[part]; Matrix4x4 matrix = transform.localToWorldMatrix;
            Vector3 x = matrix.MultiplyVector(Vector3.right * local.extents.x);
            Vector3 y = matrix.MultiplyVector(Vector3.up * local.extents.y);
            Vector3 z = matrix.MultiplyVector(Vector3.forward * local.extents.z);
            return new Bounds(matrix.MultiplyPoint3x4(local.center),
                new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2);
        }
        public bool TryRaycast(Ray ray, float reach, out int part, out float distance)
        {
            part = -1; distance = 0;
            if (!collision || !collision.enabled || !collision.gameObject.activeInHierarchy || reach <= 0
                || !collision.Raycast(ray, out RaycastHit hit, reach)) return false;
            State();
            if (!working)
            {
                if (hit.triangleIndex < 0 || hit.triangleIndex >= originalTriangleParts.Length) return false;
                part = originalTriangleParts[hit.triangleIndex]; distance = hit.distance; return part >= 0;
            }
            int triangle = hit.triangleIndex, offset = 0;
            for (int index = 0; index < PartCount; index++)
            {
                if (removed[index]) continue;
                int count = lengths[index] / 3;
                if (triangle >= offset && triangle < offset + count) { part = index; distance = hit.distance; return true; }
                offset += count;
            }
            return false; // Protected residual geometry is a real blocker, never an item.
        }
        public bool IsRemoved(int part) { ValidatePart(part); State(); return removed[part]; }
        public bool SetRemoved(int part, bool value)
        {
            ValidatePart(part); State();
            if (removed[part] == value) return true;
            if (!EnsureWorking()) return false;
            removed[part] = value; RebuildIndices(); return true;
        }
        /// <summary>Saved removals fail closed while an edit budget is unavailable; source art never resurrects.</summary>
        public bool ApplySavedState(int part, int strokes, bool value)
        {
            ValidatePart(part); State();
            byte polished = (byte)Mathf.Clamp(strokes, 0, 4);
            if (!pendingSavedState && removed[part] == value && clean[part] == polished) return true;
            removed[part] = value; clean[part] = polished;
            if (savedStateRestoreDepth > 0) { savedStateRestoreDirty = true; return true; }
            return FlushSavedState();
        }
        /// <summary>Collect persisted pieces before a single mesh/collider upload.</summary>
        public void BeginSavedStateRestore() { State(); savedStateRestoreDepth++; }
        public bool EndSavedStateRestore()
        {
            if (savedStateRestoreDepth <= 0) throw new InvalidOperationException("No saved surface restore is open.");
            savedStateRestoreDepth--;
            if (savedStateRestoreDepth > 0) return true;
            if (!savedStateRestoreDirty) return !pendingSavedState;
            savedStateRestoreDirty = false;
            return FlushSavedState();
        }
        private bool FlushSavedState()
        {
            if (!EnsureWorking(false))
            {
                pendingSavedState = true;
                if (display) display.forceRenderingOff = true;
                if (collision) collision.enabled = false;
                return false;
            }
            ApplyAllColors(); RebuildIndices(); pendingSavedState = false;
            if (display) display.forceRenderingOff = originalForceRenderingOff;
            return true;
        }
        private void LateUpdate()
        {
            if (!pendingSavedState || Time.unscaledTime < nextAdmissionRetry) return;
            nextAdmissionRetry = Time.unscaledTime + 1;
            if (savedStateRestoreDepth == 0) FlushSavedState();
        }
        public void SetCleanProgress(int part, int strokes)
        {
            ValidatePart(part); State();
            byte value = (byte)Mathf.Clamp(strokes, 0, 4);
            if (clean[part] == value) return;
            if (!EnsureWorking()) throw new InvalidOperationException("Salvage edit geometry budget is full.");
            clean[part] = value;
            for (int index = starts[part]; index < starts[part] + lengths[part]; index++)
            {
                byte dirt = (byte)(255 - value * 255 / 4); colors[indices[index]] = new Color32(dirt, 255, 255, 255);
            }
            working.colors32 = colors;
        }
        private void ApplyAllColors()
        {
            for (int part = 0; part < PartCount; part++)
            {
                byte dirt = (byte)(255 - clean[part] * 255 / 4);
                for (int index = starts[part]; index < starts[part] + lengths[part]; index++)
                    colors[indices[index]] = new Color32(dirt, 255, 255, 255);
            }
            working.colors32 = colors;
        }
        private void State()
        {
            if (removed != null) return;
            var nextRemoved = new bool[PartCount]; var nextClean = new byte[PartCount];
            removed = nextRemoved; clean = nextClean;
        }
        private bool EnsureWorking(bool rebuild = true)
        {
            if (working) return true;
            if (!source || !source.isReadable || !target || !collision || editBytes <= 0
                || editBytes > MaximumOwnedBytes || ownedBytes > MaximumOwnedBytes - editBytes) return false;
            // Failed allocation is retried only after other edits release budget
            // or this component is explicitly enabled again, not on every tick.
            if (workingAllocationFailed && ownedBytes >= failedAdmissionOwnedBytes) return false;
            Mesh candidate = null;
            Mesh previousTarget = target.sharedMesh, previousCollision = collision.sharedMesh;
            bool previousCollisionEnabled = collision.enabled;
            long reservation = editBytes;
            ownedBytes += reservation;
            try
            {
                var nextColors = new Color32[source.vertexCount];
                for (int index = 0; index < nextColors.Length; index++) nextColors[index] = new Color32(255, 255, 255, 255);
                var nextScratch = new List<int>(checked(indices.Length + fixedIndices.Length));
                candidate = Instantiate(source);
                candidate.name = source.name + "_OwnedSalvageEdit"; candidate.MarkDynamic();
                candidate.colors32 = nextColors;
                if (rebuild) BuildIndices(candidate, nextScratch);
                target.sharedMesh = candidate;
                if (rebuild) PublishCollision(candidate, nextScratch.Count);
                // All allocation, mesh setters and external bindings succeeded.
                // Publish the complete owned state and transfer the reservation.
                working = candidate; colors = nextColors; scratch = nextScratch;
                workingReservationBytes = reservation; reservation = 0;
                workingAllocationFailed = false;
                if (rebuild) indexRebuildCount++;
                return true;
            }
            catch (Exception error)
            {
                try
                {
                    if (target && target.sharedMesh == candidate) target.sharedMesh = previousTarget;
                    if (collision)
                    {
                        collision.sharedMesh = previousCollision;
                        collision.enabled = previousCollisionEnabled;
                    }
                }
                finally
                {
                    try { if (candidate) Dispose(candidate); }
                    finally { ownedBytes -= reservation; }
                }
                workingAllocationFailed = true; failedAdmissionOwnedBytes = ownedBytes;
                Debug.LogWarning("Salvage edit allocation failed; retry requires released edit budget or re-enabling. " + error.GetType().Name, this);
                return false;
            }
        }
        private void BuildIndices(Mesh mesh, List<int> result)
        {
            result.Clear();
            for (int part = 0; part < PartCount; part++)
                if (!removed[part])
                    for (int index = starts[part]; index < starts[part] + lengths[part]; index++) result.Add(indices[index]);
            result.AddRange(fixedIndices);
            mesh.SetTriangles(result, 0, false);
            mesh.bounds = source.bounds;
        }
        private void PublishCollision(Mesh mesh, int count)
        {
            collision.sharedMesh = null;
            collision.enabled = originalCollisionEnabled && count > 0;
            if (count > 0) collision.sharedMesh = mesh;
        }
        private void RebuildIndices()
        {
            BuildIndices(working, scratch);
            PublishCollision(working, scratch.Count);
            indexRebuildCount++;
        }
        private void ReleaseWorking()
        {
            Mesh retired = working;
            working = null; colors = null; scratch = null;
            try
            {
                if (ReferenceEquals(retired, null)) return;
                if (target && target.sharedMesh == retired) target.sharedMesh = source;
                if (collision && (collision.sharedMesh == retired || !collision.sharedMesh)) collision.sharedMesh = source;
                if (collision) collision.enabled = originalCollisionEnabled;
            }
            finally
            {
                try { if (retired) Dispose(retired); }
                finally
                {
                    long reservation = workingReservationBytes; workingReservationBytes = 0;
                    ownedBytes -= reservation;
                }
            }
        }
        private void OnDestroy()
        {
            // A failed native mesh/renderer/collider restore must not leave this
            // destroyed surface counted as a user of a shared finish forever.
            try { ReleaseWorking(); }
            finally
            {
                try { if (display) display.forceRenderingOff = originalForceRenderingOff; }
                finally
                {
                    try { if (pendingSavedState && collision) collision.enabled = originalCollisionEnabled; }
                    finally
                    {
                        // Parent material owners may already have destroyed their authored
                        // replacement during scene teardown. Dictionary identity still exists.
                        if (materialBound && !ReferenceEquals(authoredMaterial, null) && Finishes.TryGetValue(authoredMaterial, out Finish finish))
                        {
                            materialBound = false;
                            if (--finish.users == 0) { Finishes.Remove(authoredMaterial); if (finish.owned && finish.material) Dispose(finish.material); }
                        }
                    }
                }
            }
        }
        private void ValidatePart(int part) { if (part < 0 || part >= PartCount) throw new ArgumentOutOfRangeException(nameof(part)); }
        private static string PositionKey(Vector3 p) => Mathf.RoundToInt(p.x * 10000) + "," + Mathf.RoundToInt(p.y * 10000) + "," + Mathf.RoundToInt(p.z * 10000);
        private static int Find(int[] groups, int index) { while (groups[index] != index) { groups[index] = groups[groups[index]]; index = groups[index]; } return index; }
        private static void Union(int[] groups, int a, int b) { groups[Find(groups, b)] = Find(groups, a); }
        private static int FormatBytes(VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32: case VertexAttributeFormat.UInt32: case VertexAttributeFormat.SInt32: return 4;
                case VertexAttributeFormat.Float16: case VertexAttributeFormat.UNorm16: case VertexAttributeFormat.SNorm16: case VertexAttributeFormat.UInt16: case VertexAttributeFormat.SInt16: return 2;
                default: return 1;
            }
        }
        private static void Dispose(UnityEngine.Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
