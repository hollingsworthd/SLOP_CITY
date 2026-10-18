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
    }
}
#endif
