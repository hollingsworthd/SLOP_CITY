using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>Hollows the original manufactured landmark while retaining its facade, taper and mast.</summary>
    public sealed class SlopCityCitadelInterior : MonoBehaviour
    {
        private const string LandmarkName = "MilitarySF_CommunicationsCitadel";
        [SerializeField] private bool prepared;
        [SerializeField] private Bounds originalBounds;
        [SerializeField] private Bounds preparedBounds;
        [SerializeField] private int sourceTriangles;
        [SerializeField] private int resultTriangles;
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        public bool Prepared => prepared;
        public Bounds OriginalBounds => originalBounds;
        public Bounds PreparedBounds => preparedBounds;
        public int SourceTriangles => sourceTriangles;
        public int ResultTriangles => resultTriangles;

        private struct Vertex
        {
            public Vector3 position, citadelPosition, normal;
            public Vector2 uv, uv2;
            public Color color;
            public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex
            {
                position = Vector3.LerpUnclamped(a.position, b.position, t),
                citadelPosition = Vector3.LerpUnclamped(a.citadelPosition, b.citadelPosition, t),
                normal = Vector3.LerpUnclamped(a.normal, b.normal, t).normalized,
                uv = Vector2.LerpUnclamped(a.uv, b.uv, t),
                uv2 = Vector2.LerpUnclamped(a.uv2, b.uv2, t),
                color = Color.LerpUnclamped(a.color, b.color, t)
            };
        }

        /// <summary>
        /// Call on readable authored scene geometry before static batching. The serialized preparation
        /// marker makes the runtime installer idempotent in both exported and older saved scenes.
        /// </summary>
        public static void PrepareCitadel(Transform city)
        {
            if (!city) return;
            Transform landmark = null;
            foreach (Transform item in city.GetComponentsInChildren<Transform>(true))
                if (item.name == LandmarkName) { landmark = item; break; }
            if (!landmark) return;
            SlopCityCitadelInterior preparation = landmark.GetComponent<SlopCityCitadelInterior>();
            if (!preparation) preparation = landmark.gameObject.AddComponent<SlopCityCitadelInterior>();
            if (!preparation.prepared) preparation.Prepare();
            landmark.position = city.TransformPoint(SlopCityDenseLayout.CitadelPoint);
        }

        private void Prepare()
        {
            // These are interior voids, inset from the 34x29 lower body and the 21.5x29
            // upper room footprint. Cutting only through y172 preserves the asymmetric crown.
            Bounds[] voids =
            {
                new Bounds(new Vector3(0, (111f + .08f) * .5f, 0), new Vector3(33.5f, 111f - .08f, 28.5f)),
                new Bounds(new Vector3(0, (172f + 111f) * .5f, 0), new Vector3(21f, 172f - 111f, 28.5f)),
                // Cut through the complete southern facade/trim thickness, without cutting the rear.
                new Bounds(new Vector3(0, 1.30f, -17.5f), new Vector3(1.8f, 2.60f, 8f))
            };
            bool first = true;
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh source = filter.sharedMesh;
                if (!source) continue;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (!source.isReadable || (renderer && renderer.isPartOfStaticBatch))
                    throw new InvalidOperationException("Prepare the communications citadel before static batching using its readable authored meshes.");
                Matrix4x4 toCitadel = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Encapsulate(ref originalBounds, source.bounds, toCitadel, ref first);
            }
            if (first) throw new InvalidOperationException("The communications citadel has no authored mesh geometry.");
            bool preparedFirst = true;
            var replacements = new Dictionary<MeshFilter, Mesh>();
            var originals = new Dictionary<MeshFilter, Mesh>();
            sourceTriangles = resultTriangles = 0;
            try
            {
                foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh source = filter.sharedMesh;
                    if (!source) continue;
                    Matrix4x4 toCitadel = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    Mesh hollow = Subtract(source, toCitadel, voids, out int before, out int after);
                    sourceTriangles += before; resultTriangles += after;
                    replacements.Add(filter, hollow);
                    originals.Add(filter, source);
                    Encapsulate(ref preparedBounds, hollow.bounds, toCitadel, ref preparedFirst);
                }
                if (resultTriangles <= 0 || preparedBounds.max.y < originalBounds.max.y - .001f)
                    throw new InvalidOperationException("Citadel preparation must retain its original architectural crown and mast.");
                foreach (var replacement in replacements)
                {
                    replacement.Key.sharedMesh = replacement.Value;
                    ownedMeshes.Add(replacement.Value);
                }
                prepared = true;
            }
            catch
            {
                foreach (var original in originals) if (original.Key) original.Key.sharedMesh = original.Value;
                foreach (Mesh mesh in replacements.Values)
                {
                    ownedMeshes.Remove(mesh);
                    if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
                }
                sourceTriangles = resultTriangles = 0;
                throw;
            }
        }

        private static Mesh Subtract(Mesh source, Matrix4x4 toCitadel, Bounds[] voids, out int before, out int after)
        {
            Vector3[] positions = source.vertices, sourceNormals = source.normals;
            Vector2[] sourceUv = source.uv, sourceUv2 = source.uv2;
            Color[] sourceColors = source.colors;
            bool normalsAvailable = sourceNormals.Length == positions.Length;
            bool uvAvailable = sourceUv.Length == positions.Length, uv2Available = sourceUv2.Length == positions.Length;
            bool colorsAvailable = sourceColors.Length == positions.Length;
            var vertices = new List<Vector3>(); var normals = new List<Vector3>();
            var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var colors = new List<Color>();
            var submeshes = new List<int>[source.subMeshCount];
            before = after = 0;
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                var output = submeshes[submesh] = new List<int>();
                int[] triangles = source.GetTriangles(submesh);
                before += triangles.Length / 3;
                for (int index = 0; index + 2 < triangles.Length; index += 3)
                {
                    var triangle = new List<Vertex>(3);
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int id = triangles[index + corner];
                        triangle.Add(new Vertex
                        {
                            position = positions[id], citadelPosition = toCitadel.MultiplyPoint3x4(positions[id]),
                            normal = normalsAvailable ? sourceNormals[id] : Vector3.up,
                            uv = uvAvailable ? sourceUv[id] : Vector2.zero,
                            uv2 = uv2Available ? sourceUv2[id] : Vector2.zero,
                            color = colorsAvailable ? sourceColors[id] : Color.white
                        });
                    }
                    var pieces = new List<List<Vertex>> { triangle };
                    foreach (Bounds volume in voids)
                    {
                        var remaining = new List<List<Vertex>>();
                        foreach (List<Vertex> polygon in pieces) SubtractBox(polygon, volume, remaining);
                        pieces = remaining;
                        if (pieces.Count == 0) break;
                    }
                    foreach (List<Vertex> polygon in pieces)
                    {
                        for (int corner = 1; corner + 1 < polygon.Count; corner++)
                        {
                            Vertex a = polygon[0], b = polygon[corner], c = polygon[corner + 1];
                            if (Vector3.Cross(b.position - a.position, c.position - a.position).sqrMagnitude < 1e-12f) continue;
                            int first = vertices.Count;
                            foreach (Vertex vertex in new[] { a, b, c })
                            {
                                vertices.Add(vertex.position); normals.Add(vertex.normal); uv.Add(vertex.uv);
                                if (uv2Available) uv2.Add(vertex.uv2);
                                if (colorsAvailable) colors.Add(vertex.color);
                            }
                            output.Add(first); output.Add(first + 1); output.Add(first + 2); after++;
                        }
                    }
                }
            }
            var mesh = new Mesh { name = source.name + "_AccessibleCitadel", indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try
            {
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv);
                if (uv2Available) mesh.SetUVs(1, uv2);
                if (colorsAvailable) mesh.SetColors(colors);
                mesh.subMeshCount = submeshes.Length;
                for (int i = 0; i < submeshes.Length; i++) mesh.SetTriangles(submeshes[i], i, false);
                if (!normalsAvailable) mesh.RecalculateNormals();
                mesh.RecalculateBounds(); mesh.RecalculateTangents();
                return mesh;
            }
            catch
            {
                if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
                throw;
            }
        }

        private static void SubtractBox(List<Vertex> polygon, Bounds volume, List<List<Vertex>> output)
        {
            if (polygon.Count < 3) return;
            Bounds bounds = new Bounds(polygon[0].citadelPosition, Vector3.zero);
            foreach (Vertex vertex in polygon) bounds.Encapsulate(vertex.citadelPosition);
            if (!bounds.Intersects(volume)) { output.Add(polygon); return; }
            List<Vertex> inside = polygon;
            // Partition into disjoint outside polygons, carrying only the remaining inside
            // polygon forward. The final inside fragment is the room void and is discarded.
            for (int plane = 0; plane < 6 && inside.Count >= 3; plane++)
            {
                int axis = plane / 2;
                float coordinate = plane % 2 == 0 ? volume.min[axis] : volume.max[axis];
                float sign = plane % 2 == 0 ? 1f : -1f;
                Split(inside, axis, coordinate, sign, out List<Vertex> keepInside, out List<Vertex> outside);
                if (outside.Count >= 3) output.Add(outside);
                inside = keepInside;
            }
        }

        private static void Split(List<Vertex> polygon, int axis, float coordinate, float sign,
            out List<Vertex> inside, out List<Vertex> outside)
        {
            inside = new List<Vertex>(); outside = new List<Vertex>();
            Vertex previous = polygon[polygon.Count - 1];
            float previousDistance = sign * (previous.citadelPosition[axis] - coordinate);
            foreach (Vertex current in polygon)
            {
                float distance = sign * (current.citadelPosition[axis] - coordinate);
                bool previousInside = previousDistance >= 0f, currentInside = distance >= 0f;
                if (previousInside != currentInside)
                {
                    Vertex crossing = Vertex.Lerp(previous, current, previousDistance / (previousDistance - distance));
                    AddUnique(inside, crossing); AddUnique(outside, crossing);
                }
                AddUnique(currentInside ? inside : outside, current);
                previous = current; previousDistance = distance;
            }
            TrimClosingDuplicate(inside); TrimClosingDuplicate(outside);
        }

        private static void AddUnique(List<Vertex> polygon, Vertex vertex)
        {
            if (polygon.Count == 0 || (polygon[polygon.Count - 1].position - vertex.position).sqrMagnitude > 1e-12f)
                polygon.Add(vertex);
        }

        private static void TrimClosingDuplicate(List<Vertex> polygon)
        {
            if (polygon.Count > 1 && (polygon[0].position - polygon[polygon.Count - 1].position).sqrMagnitude <= 1e-12f)
                polygon.RemoveAt(polygon.Count - 1);
        }

        private static void Encapsulate(ref Bounds bounds, Bounds local, Matrix4x4 matrix, ref bool first)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(new Vector3((corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y, (corner & 4) == 0 ? local.min.z : local.max.z));
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
        }

        private void OnDestroy()
        {
            // Authored assets and meshes loaded from a prepared player scene remain borrowed.
            // Only transient meshes produced by this live component belong to its cleanup.
            foreach (Mesh mesh in ownedMeshes)
                if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            ownedMeshes.Clear();
        }
    }
}
