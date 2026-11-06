#if UNITY_EDITOR || UNITY_WEBGL
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>Authored renderer bounds retained only in browser build scenes.</summary>
    [AddComponentMenu("")]
    public sealed class SlopCityBrowserGeometry : MonoBehaviour
    {
        [Serializable]
        private struct Entry
        {
            public Renderer renderer;
            public Bounds localBounds;
        }

        // Keep the serialized layout identical in the Editor and Web player.
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

#if UNITY_EDITOR
        public int Capture(Renderer[] renderers)
        {
            var captured = new List<Entry>(renderers.Length);
            foreach (Renderer renderer in renderers)
            {
                if (!renderer) continue;
                if (renderer.isPartOfStaticBatch)
                    throw new InvalidOperationException("Browser interaction geometry must be captured before static batching: " + renderer.name);
                Bounds bounds = renderer.localBounds;
                if (renderer.TryGetComponent<MeshFilter>(out MeshFilter filter) && filter.sharedMesh)
                    bounds = filter.sharedMesh.bounds;
                captured.Add(new Entry { renderer = renderer, localBounds = bounds });
            }
            entries = captured.ToArray();
            return entries.Length;
        }
#endif

        public void CopyTo(Dictionary<Renderer, Bounds> bounds)
        {
            foreach (Entry entry in entries)
                if (entry.renderer) bounds[entry.renderer] = entry.localBounds;
        }

        /// <summary>Copy only bounds that can be reached by the local browser scenery index.</summary>
        public void CopyNearTo(Dictionary<Renderer, Bounds> bounds, Vector3 origin, float maximumSquaredDistance)
        {
            foreach (Entry entry in entries)
                if (entry.renderer && entry.renderer.gameObject.activeInHierarchy
                    && WorldBounds(entry.renderer.transform,entry.localBounds).SqrDistance(origin) <= maximumSquaredDistance)
                    bounds[entry.renderer] = entry.localBounds;
        }

        // Static batching can replace Renderer.bounds with a batch-wide AABB.
        // The serialized pre-batch local bound is the only safe locality measure.
        private static Bounds WorldBounds(Transform transform, Bounds local)
        {
            Matrix4x4 matrix = transform.localToWorldMatrix;
            Vector3 x = matrix.MultiplyVector(Vector3.right * local.extents.x);
            Vector3 y = matrix.MultiplyVector(Vector3.up * local.extents.y);
            Vector3 z = matrix.MultiplyVector(Vector3.forward * local.extents.z);
            return new Bounds(matrix.MultiplyPoint3x4(local.center),
                new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2f);
        }
    }
}
#endif
