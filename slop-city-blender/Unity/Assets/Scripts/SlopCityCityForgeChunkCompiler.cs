using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// A presentation boundary for City Forge. Implementors receive only resolved
    /// catalog IDs and grid coordinates; they may instantiate precompiled road,
    /// facade, collider, LOD, and interior variants, but never accept a scene,
    /// transform, mesh, shader, URL, or player asset from a ChunkPlan.
    /// </summary>
    public interface ISlopCityCityForgeChunkSink
    {
        void ActivateCityForgeChunk(CityForgeResolvedChunk chunk);
        void ReleaseCityForgeChunk(CityForgeCoord coordinate);
    }

    [DisallowMultipleComponent]
    public sealed class SlopCityCityForgeChunkCompiler : MonoBehaviour
    {
        [SerializeField, Range(1, 49)] private int maximumActiveChunks = 25;
        private readonly List<ISlopCityCityForgeChunkSink> sinks = new List<ISlopCityCityForgeChunkSink>();
        private readonly Dictionary<CityForgeCoord, string> compiledHashes = new Dictionary<CityForgeCoord, string>();
        private SlopCityCityForgeStreamingWindow window;
        private int windowCapacity;
        public event Action<CityForgeResolvedChunk> ChunkCompiled;
        public event Action<CityForgeCoord> ChunkReleased;
        public IReadOnlyDictionary<CityForgeCoord, CityForgeResolvedChunk> ActiveChunks { get { return window == null ? null : window.Active; } }

        private void Awake()
        {
            window = new SlopCityCityForgeStreamingWindow(maximumActiveChunks);
            windowCapacity = maximumActiveChunks;
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++) RegisterSink(behaviours[i] as ISlopCityCityForgeChunkSink);
        }

        /// <summary>Lets a scene bootstrap register a precompiled chunk renderer.</summary>
        public void RegisterSink(ISlopCityCityForgeChunkSink sink)
        {
            if (sink != null && !sinks.Contains(sink)) sinks.Add(sink);
        }

        public void UnregisterSink(ISlopCityCityForgeChunkSink sink)
        {
            sinks.Remove(sink);
        }

        public void Reconcile(CityForgeCoord center, IEnumerable<CityForgeResolvedChunk> desired, bool constrainedWebGl)
        {
            int requestedCapacity = constrainedWebGl ? Math.Min(maximumActiveChunks, 9) : maximumActiveChunks;
            if (window == null || windowCapacity != requestedCapacity) ResetWindow(requestedCapacity);
            // The streaming window returns removals first. A sink must synchronously
            // unregister screens/release materials before it creates any replacement.
            List<CityForgeCoord> releases = window.Reconcile(center, desired);
            for (int i = 0; i < releases.Count; i++)
            {
                for (int sink = 0; sink < sinks.Count; sink++) sinks[sink]?.ReleaseCityForgeChunk(releases[i]);
                compiledHashes.Remove(releases[i]);
                ChunkReleased?.Invoke(releases[i]);
            }
            foreach (CityForgeResolvedChunk chunk in window.Active.Values.OrderBy(item => item.baseChunk.coord))
            {
                CityForgeCoord coordinate = chunk.baseChunk.coord;
                string resolvedHash = chunk.resolvedHash ?? string.Empty;
                if (compiledHashes.TryGetValue(coordinate, out string prior) && string.Equals(prior, resolvedHash, StringComparison.Ordinal)) continue;
                // Revision replacements retain their coordinate in the streaming
                // window, so explicitly evict old fixtures before any new chunk
                // can own colliders, screens, or retained media at that location.
                if (compiledHashes.ContainsKey(coordinate))
                {
                    for (int sink = 0; sink < sinks.Count; sink++) sinks[sink]?.ReleaseCityForgeChunk(coordinate);
                    ChunkReleased?.Invoke(coordinate);
                }
                for (int sink = 0; sink < sinks.Count; sink++) sinks[sink]?.ActivateCityForgeChunk(chunk);
                compiledHashes[coordinate] = resolvedHash;
                ChunkCompiled?.Invoke(chunk);
            }
        }

        public void ReleaseAll()
        {
            if (window == null) return;
            List<CityForgeCoord> current = window.Active.Keys.OrderBy(value => value).ToList();
            foreach (CityForgeCoord coordinate in current)
            {
                for (int sink = 0; sink < sinks.Count; sink++) sinks[sink]?.ReleaseCityForgeChunk(coordinate);
                ChunkReleased?.Invoke(coordinate);
            }
            compiledHashes.Clear();
            window = new SlopCityCityForgeStreamingWindow(maximumActiveChunks);
            windowCapacity = maximumActiveChunks;
        }

        private void ResetWindow(int capacity)
        {
            if (window != null)
            {
                foreach (CityForgeCoord coordinate in window.Active.Keys.OrderBy(value => value).ToList())
                {
                    for (int sink = 0; sink < sinks.Count; sink++) sinks[sink]?.ReleaseCityForgeChunk(coordinate);
                    ChunkReleased?.Invoke(coordinate);
                }
                compiledHashes.Clear();
            }
            window = new SlopCityCityForgeStreamingWindow(capacity);
            windowCapacity = capacity;
        }

        private void OnDestroy()
        {
            ReleaseAll();
            sinks.Clear();
        }
    }
}
