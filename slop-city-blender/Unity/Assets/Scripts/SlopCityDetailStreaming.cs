#if UNITY_EDITOR || UNITY_WEBGL
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>Bounded, same-origin leases for immutable browser decoration. Physics and materials stay in the scene.</summary>
    [AddComponentMenu("")]
    public sealed class SlopCityDetailStreaming : MonoBehaviour
    {
        public const long ResidentBudgetBytes = 24L * 1024 * 1024;
        public const float AdmissionDistance = 65f, RetirementDistance = 90f;
        [Serializable]
        public sealed class DetailEntry
        {
            public MeshFilter target;
            public MeshRenderer renderer;
            public Mesh farProxy;
            public Bounds localBounds;
            public string fileName, sha256;
            public int payloadBytes, geometryBytes;
        }
        private sealed class Lease
        {
            public Mesh mesh;
            public int generation, failures;
            public float retryAt;
        }
        [SerializeField] private DetailEntry[] entries = Array.Empty<DetailEntry>();
        private Lease[] leases;
        private long residentBytes;
        private int pendingIndex = -1;
        private UnityWebRequest pendingRequest;
        private BoundedDownload pendingDownload;
        private Coroutine pendingRoutine;
#if UNITY_WEBGL && !UNITY_EDITOR
        private Camera view;
        private float nextTick;
#endif
        private bool testing, disposed;
        public IReadOnlyList<DetailEntry> Entries => entries;
        public long ResidentBytes => residentBytes;
        public int PendingIndex => pendingIndex;
        public bool IsResident(int index) => leases != null && index >= 0 && index < leases.Length && leases[index].mesh;
#if UNITY_EDITOR
        public void SetBuildEntries(DetailEntry[] value) { entries = value ?? Array.Empty<DetailEntry>(); }
#endif
        private void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (disposed || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + .2f;
            if (!view) view = Camera.main;
            if (view) Select(view.transform.position, Time.unscaledTime, true);
#endif
        }
        private void EnsureLeases()
        {
            if (leases != null) return;
            leases = new Lease[entries.Length];
            for (int i = 0; i < leases.Length; i++) leases[i] = new Lease();
        }
        public static Bounds WorldBounds(Transform target, Bounds local)
        {
            Matrix4x4 matrix = target.localToWorldMatrix;
            Vector3 x = matrix.MultiplyVector(new Vector3(local.extents.x, 0, 0));
            Vector3 y = matrix.MultiplyVector(new Vector3(0, local.extents.y, 0));
            Vector3 z = matrix.MultiplyVector(new Vector3(0, 0, local.extents.z));
            Vector3 extent = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(matrix.MultiplyPoint3x4(local.center), extent * 2f);
        }
        private float DistanceSquared(int index, Vector3 position)
        {
            DetailEntry entry = entries[index];
            return entry.target ? WorldBounds(entry.target.transform, entry.localBounds).SqrDistance(position) : float.PositiveInfinity;
        }
        private void Select(Vector3 position, float now, bool network)
        {
            if (disposed) return;
            EnsureLeases();
            if (pendingIndex >= 0 && DistanceSquared(pendingIndex, position) > RetirementDistance * RetirementDistance)
                Retire(pendingIndex);
            bool retired = false;
            for (int i = 0; i < entries.Length; i++)
            {
                float distance = DistanceSquared(i, position);
                if (leases[i].mesh && distance > RetirementDistance * RetirementDistance) { Retire(i); retired = true; }
                if (distance > RetirementDistance * RetirementDistance) { leases[i].failures = 0; leases[i].retryAt = 0; }
            }
            // Destruction is deferred in a player. Never allocate a replacement in that same frame.
            if (retired || pendingIndex >= 0) return;
            int best = -1; float closest = AdmissionDistance * AdmissionDistance;
            for (int i = 0; i < entries.Length; i++)
            {
                if (leases[i].mesh || leases[i].failures >= 3 || now < leases[i].retryAt || !ValidEntry(entries[i])) continue;
                float distance = DistanceSquared(i, position);
                if (distance < closest) { closest = distance; best = i; }
            }
            if (best < 0) return;
            long wanted = entries[best].geometryBytes;
            if (residentBytes + wanted > ResidentBudgetBytes)
            {
                float margin = Mathf.Sqrt(closest) + 8f;
                int farthest = -1; float distance = margin * margin;
                for (int i = 0; i < entries.Length; i++)
                    if (leases[i].mesh && DistanceSquared(i, position) > distance)
                    { distance = DistanceSquared(i, position); farthest = i; }
                if (farthest >= 0) Retire(farthest);
                return;
            }
            int token = Begin(best);
            if (network && token >= 0) pendingRoutine = StartCoroutine(Load(best, token));
        }
        private static bool ValidEntry(DetailEntry entry)
        {
            return entry != null && entry.target && entry.renderer && entry.payloadBytes > 0
                && entry.payloadBytes <= SlopCityDetailMeshCodec.MaximumPayloadBytes
                && entry.geometryBytes > 0 && entry.geometryBytes <= entry.payloadBytes
                && SlopCityDetailMeshCodec.ValidHash(entry.sha256)
                && entry.fileName == "detail-" + entry.sha256 + ".mesh.unityweb";
        }
        private int Begin(int index)
        {
            EnsureLeases();
            if (disposed || index < 0 || index >= entries.Length || pendingIndex >= 0 || leases[index].mesh
                || !ValidEntry(entries[index]) || residentBytes + entries[index].geometryBytes > ResidentBudgetBytes) return -1;
            pendingIndex = index;
            return ++leases[index].generation;
        }
        private IEnumerator Load(int index, int token)
        {
            DetailEntry entry = entries[index];
            pendingDownload = new BoundedDownload(entry.payloadBytes);
            pendingRequest = new UnityWebRequest(Application.streamingAssetsPath.TrimEnd('/') + "/Geometry/" + entry.fileName,
                UnityWebRequest.kHttpVerbGET) { downloadHandler = pendingDownload, timeout = 15 };
            UnityWebRequestAsyncOperation operation;
            try { operation = pendingRequest.SendWebRequest(); }
            catch (Exception) { Failed(index, token); ClearRequest(); yield break; }
            while (!operation.isDone) yield return null;
            if (pendingIndex == index && leases[index].generation == token)
            {
                if (pendingRequest.result == UnityWebRequest.Result.Success && pendingDownload.Complete)
                    Complete(index, token, pendingDownload.Bytes);
                else Failed(index, token);
            }
            ClearRequest();
        }
        private bool Complete(int index, int token, byte[] bytes)
        {
            EnsureLeases();
            if (disposed || pendingIndex != index || index < 0 || index >= entries.Length || leases[index].generation != token) return false;
            Mesh mesh = null;
            try
            {
                DetailEntry entry = entries[index];
                if (!ValidEntry(entry) || residentBytes + entry.geometryBytes > ResidentBudgetBytes)
                    throw new InvalidDataException("Detail admission exceeds its resident budget.");
                mesh = SlopCityDetailMeshCodec.Decode(bytes, entry.sha256, entry.payloadBytes, out int geometryBytes);
                if (geometryBytes != entry.geometryBytes || !entry.target || entry.target.sharedMesh != entry.farProxy)
                    throw new InvalidDataException("Detail geometry or binding changed during its request.");
                entry.target.sharedMesh = mesh;
                leases[index].mesh = mesh;
                residentBytes += geometryBytes;
                leases[index].failures = 0;
                mesh = null;
                pendingIndex = -1;
#if UNITY_WEBGL && !UNITY_EDITOR
                // Fill the nearby budget one request at a time without imposing
                // another polling interval after every successful small surface.
                nextTick = 0;
#endif
                return true;
            }
            catch (Exception)
            {
                if (mesh) DestroyOwned(mesh);
                Failed(index, token);
                return false;
            }
        }
        private void Failed(int index, int token)
        {
            if (index < 0 || index >= entries.Length || leases[index].generation != token) return;
            leases[index].failures++;
            leases[index].retryAt = Time.unscaledTime + Mathf.Min(60f, 2f * Mathf.Pow(2f, leases[index].failures));
            if (pendingIndex == index) pendingIndex = -1;
        }
        private void ClearRequest()
        {
            if (pendingRequest != null) { pendingRequest.Dispose(); pendingRequest = null; }
            pendingDownload = null; pendingRoutine = null;
        }
        private void CancelPending()
        {
            if (pendingIndex >= 0) { leases[pendingIndex].generation++; pendingIndex = -1; }
            if (pendingRoutine != null) { StopCoroutine(pendingRoutine); pendingRoutine = null; }
            if (pendingRequest != null) pendingRequest.Abort();
            ClearRequest();
        }
        private void Retire(int index)
        {
            EnsureLeases();
            if (index < 0 || index >= entries.Length) return;
            if (pendingIndex == index) CancelPending();
            Lease lease = leases[index]; lease.generation++;
            if (!lease.mesh) return;
            DetailEntry entry = entries[index];
            if (entry.target && entry.target.sharedMesh == lease.mesh) entry.target.sharedMesh = entry.farProxy;
            residentBytes -= entry.geometryBytes;
            DestroyOwned(lease.mesh); lease.mesh = null;
        }
        private static void DestroyOwned(Mesh mesh)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { DestroyImmediate(mesh); return; }
#endif
            Destroy(mesh);
        }
        private void OnDisable()
        {
            if (leases == null) return;
            CancelPending();
            for (int i = 0; i < entries.Length; i++) Retire(i);
        }
        private void OnDestroy() { OnDisable(); disposed = true; }
        public void InitializeForTesting() { testing = true; EnsureLeases(); }
        public int BeginForTesting(int index) { if (!testing) throw new InvalidOperationException("Initialize test routing first."); return Begin(index); }
        public bool CompleteForTesting(int index, int token, byte[] bytes) { if (!testing) throw new InvalidOperationException("Initialize test routing first."); return Complete(index, token, bytes); }
        public void RetireForTesting(int index) { if (!testing) throw new InvalidOperationException("Initialize test routing first."); Retire(index); }
        public void TickForTesting(Vector3 position, float now) { if (!testing) throw new InvalidOperationException("Initialize test routing first."); Select(position, now, false); }
        private sealed class BoundedDownload : DownloadHandlerScript
        {
            private readonly byte[] bytes;
            private int received;
            public BoundedDownload(int length) : base(new byte[32 * 1024]) { bytes = new byte[length]; }
            public byte[] Bytes => bytes;
            public bool Complete => received == bytes.Length;
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (data == null || length < 0 || length > bytes.Length - received) return false;
                Buffer.BlockCopy(data, 0, bytes, received, length); received += length; return true;
            }
            protected override void ReceiveContentLengthHeader(ulong length)
            {
                // ReceiveData remains the hard bound even if a host omits or lies about this header.
                if (length > (ulong)bytes.Length) received = bytes.Length + 1;
            }
        }
    }

    /// <summary>Versioned exact vertex/index streams; the compiled manifest authenticates every bounded payload.</summary>
    public static class SlopCityDetailMeshCodec
    {
        public const int MaximumPayloadBytes = 4 * 1024 * 1024;
        private const int Magic = 0x53434431, Version = 1;
        private sealed class Layout
        {
            public int vertexCount, indexBytes, indexOffset, geometryBytes;
            public IndexFormat indexFormat;
            public VertexAttributeDescriptor[] attributes;
            public int[] strides, offsets, lengths;
            public SubMeshDescriptor[] submeshes;
            public Bounds bounds;
        }
        public static bool ValidHash(string hash)
        {
            if (hash == null || hash.Length != 64) return false;
            foreach (char value in hash) if (!(value >= '0' && value <= '9') && !(value >= 'a' && value <= 'f')) return false;
            return true;
        }
        public static string ComputeHash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
#if UNITY_EDITOR
        public static byte[] Encode(Mesh source)
        {
            if (!source || source.vertexCount <= 0 || source.blendShapeCount != 0 || source.bindposes.Length != 0)
                throw new InvalidDataException("Only immutable unskinned detail meshes may stream.");
            using (var output = new MemoryStream())
            using (var writer = new BinaryWriter(output))
            using (Mesh.MeshDataArray data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(source))
            {
                VertexAttributeDescriptor[] attributes = source.GetVertexAttributes();
                byte[] indices = data[0].GetIndexData<byte>().ToArray();
                writer.Write(Magic); writer.Write(Version); writer.Write(source.vertexCount);
                writer.Write(attributes.Length); writer.Write(source.vertexBufferCount); writer.Write((int)source.indexFormat);
                writer.Write(indices.Length); writer.Write(source.subMeshCount);
                foreach (VertexAttributeDescriptor attribute in attributes)
                { writer.Write((int)attribute.attribute); writer.Write((int)attribute.format); writer.Write(attribute.dimension); writer.Write(attribute.stream); }
                for (int stream = 0; stream < source.vertexBufferCount; stream++)
                {
                    byte[] bytes = data[0].GetVertexData<byte>(stream).ToArray();
                    writer.Write(source.GetVertexBufferStride(stream)); writer.Write(bytes.Length); writer.Write(bytes);
                }
                writer.Write(indices);
                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    SubMeshDescriptor descriptor = source.GetSubMesh(sub);
                    writer.Write(descriptor.indexStart); writer.Write(descriptor.indexCount); writer.Write(descriptor.baseVertex);
                    writer.Write(descriptor.firstVertex); writer.Write(descriptor.vertexCount); writer.Write((int)descriptor.topology);
                    WriteBounds(writer, descriptor.bounds);
                }
                WriteBounds(writer, source.bounds);
                byte[] payload = output.ToArray();
                if (payload.Length > MaximumPayloadBytes) throw new InvalidDataException("Detail payload exceeds four MiB.");
                Parse(payload); // Reject an unsupported/partial export before any source binding is stripped.
                return payload;
            }
        }
        private static void WriteBounds(BinaryWriter writer, Bounds bounds)
        {
            writer.Write(bounds.center.x); writer.Write(bounds.center.y); writer.Write(bounds.center.z);
            writer.Write(bounds.extents.x); writer.Write(bounds.extents.y); writer.Write(bounds.extents.z);
        }
#endif
        public static Mesh Decode(byte[] payload, string expectedHash, int expectedBytes, bool discardCpuData = true)
            => Decode(payload, expectedHash, expectedBytes, out _, discardCpuData);
        public static Mesh Decode(byte[] payload, string expectedHash, int expectedBytes, out int geometryBytes, bool discardCpuData = true)
        {
            geometryBytes = 0;
            if (payload == null || expectedBytes <= 0 || expectedBytes > MaximumPayloadBytes || payload.Length != expectedBytes
                || !ValidHash(expectedHash) || ComputeHash(payload) != expectedHash) throw new InvalidDataException("Detail payload integrity failed.");
            Layout layout = Parse(payload);
            Mesh mesh = new Mesh { name = "Owned_Browser_Detail_" + expectedHash.Substring(0, 12) };
            try
            {
                mesh.SetVertexBufferParams(layout.vertexCount, layout.attributes);
                for (int stream = 0; stream < layout.strides.Length; stream++)
                {
                    if (mesh.GetVertexBufferStride(stream) != layout.strides[stream]) throw new InvalidDataException("Detail vertex layout changed.");
                    mesh.SetVertexBufferData(payload, layout.offsets[stream], 0, layout.lengths[stream], stream, MeshUpdateFlags.DontRecalculateBounds);
                }
                mesh.SetIndexBufferParams(layout.indexBytes / (layout.indexFormat == IndexFormat.UInt16 ? 2 : 4), layout.indexFormat);
                mesh.SetIndexBufferData(payload, layout.indexOffset, 0, layout.indexBytes, MeshUpdateFlags.DontRecalculateBounds);
                // Unity 6000.3's bulk SetSubMeshes path resets firstVertex to zero
                // for a nonzero-baseVertex submesh. The individual setter retains
                // the authored descriptors as well as the exact index bytes.
                mesh.subMeshCount = layout.submeshes.Length;
                for (int sub = 0; sub < layout.submeshes.Length; sub++)
                    mesh.SetSubMesh(sub, layout.submeshes[sub], MeshUpdateFlags.DontRecalculateBounds);
                mesh.bounds = layout.bounds;
                mesh.UploadMeshData(discardCpuData);
                geometryBytes = layout.geometryBytes;
                return mesh;
            }
            catch
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEngine.Object.DestroyImmediate(mesh); else UnityEngine.Object.Destroy(mesh);
#else
                UnityEngine.Object.Destroy(mesh);
#endif
                throw;
            }
        }
        private static Layout Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 64 || bytes.Length > MaximumPayloadBytes) throw new InvalidDataException("Invalid detail payload length.");
            using (var input = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(input))
            {
                if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version) throw new InvalidDataException("Unknown detail format.");
                var result = new Layout { vertexCount = reader.ReadInt32() };
                int attributeCount = reader.ReadInt32(), streams = reader.ReadInt32(), format = reader.ReadInt32();
                result.indexBytes = reader.ReadInt32(); int submeshes = reader.ReadInt32();
                if (result.vertexCount <= 0 || result.vertexCount > 1000000 || attributeCount < 1 || attributeCount > 16
                    || streams < 1 || streams > 4 || format < 0 || format > 1 || result.indexBytes <= 0
                    || result.indexBytes > bytes.Length || result.indexBytes % (format == 0 ? 2 : 4) != 0 || submeshes < 1 || submeshes > 32)
                    throw new InvalidDataException("Invalid detail layout counts.");
                result.indexFormat = (IndexFormat)format;
                result.attributes = new VertexAttributeDescriptor[attributeCount];
                var seen = new HashSet<int>(); bool position = false;
                for (int i = 0; i < attributeCount; i++)
                {
                    int attribute = reader.ReadInt32(), vertexFormat = reader.ReadInt32(), dimension = reader.ReadInt32(), stream = reader.ReadInt32();
                    if (!Enum.IsDefined(typeof(VertexAttribute), attribute) || !Enum.IsDefined(typeof(VertexAttributeFormat), vertexFormat)
                        || !seen.Add(attribute) || dimension < 1 || dimension > 4 || stream < 0 || stream >= streams
                        || attribute == (int)VertexAttribute.BlendIndices || attribute == (int)VertexAttribute.BlendWeight)
                        throw new InvalidDataException("Unsupported detail vertex attribute.");
                    if (attribute == (int)VertexAttribute.Position) position = vertexFormat == (int)VertexAttributeFormat.Float32 && dimension == 3;
                    result.attributes[i] = new VertexAttributeDescriptor((VertexAttribute)attribute, (VertexAttributeFormat)vertexFormat, dimension, stream);
                }
                if (!position) throw new InvalidDataException("Detail position data is missing.");
                result.strides = new int[streams]; result.offsets = new int[streams]; result.lengths = new int[streams];
                for (int stream = 0; stream < streams; stream++)
                {
                    int stride = reader.ReadInt32(), length = reader.ReadInt32();
                    if (stride <= 0 || stride > 256 || (long)stride * result.vertexCount != length || length > input.Length - input.Position)
                        throw new InvalidDataException("Invalid detail vertex byte range.");
                    result.strides[stream] = stride; result.lengths[stream] = length; result.offsets[stream] = (int)input.Position;
                    result.geometryBytes = checked(result.geometryBytes + length); input.Position += length;
                }
                if (result.indexBytes > input.Length - input.Position) throw new InvalidDataException("Invalid detail index byte range.");
                result.indexOffset = (int)input.Position; input.Position += result.indexBytes;
                result.geometryBytes = checked(result.geometryBytes + result.indexBytes);
                result.submeshes = new SubMeshDescriptor[submeshes];
                int indexSize = format == 0 ? 2 : 4, totalIndices = result.indexBytes / indexSize;
                for (int sub = 0; sub < submeshes; sub++)
                {
                    int start = reader.ReadInt32(), count = reader.ReadInt32(), baseVertex = reader.ReadInt32();
                    int firstVertex = reader.ReadInt32(), vertices = reader.ReadInt32(), topology = reader.ReadInt32();
                    Bounds bounds = ReadBounds(reader);
                    if (start < 0 || count < 0 || start > totalIndices - count || count % 3 != 0 || topology != (int)MeshTopology.Triangles
                        || firstVertex < 0 || vertices < 0 || firstVertex > result.vertexCount - vertices)
                        throw new InvalidDataException("Invalid detail submesh range.");
                    for (int i = start; i < start + count; i++)
                    {
                        int offset = result.indexOffset + i * indexSize;
                        long index = format == 0 ? BitConverter.ToUInt16(bytes, offset) : BitConverter.ToUInt32(bytes, offset);
                        index += baseVertex;
                        if (index < 0 || index >= result.vertexCount) throw new InvalidDataException("Detail index exceeds its vertices.");
                    }
                    result.submeshes[sub] = new SubMeshDescriptor(start, count, MeshTopology.Triangles)
                        { baseVertex = baseVertex, firstVertex = firstVertex, vertexCount = vertices, bounds = bounds };
                }
                result.bounds = ReadBounds(reader);
                if (input.Position != input.Length) throw new InvalidDataException("Unexpected trailing detail bytes.");
                return result;
            }
        }
        private static Bounds ReadBounds(BinaryReader reader)
        {
            var values = new float[6];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = reader.ReadSingle();
                if (float.IsNaN(values[i]) || float.IsInfinity(values[i]) || Mathf.Abs(values[i]) > 100000f || (i >= 3 && values[i] < 0))
                    throw new InvalidDataException("Invalid detail bounds.");
            }
            return new Bounds(new Vector3(values[0], values[1], values[2]), new Vector3(values[3], values[4], values[5]) * 2f);
        }
    }
}
#endif
