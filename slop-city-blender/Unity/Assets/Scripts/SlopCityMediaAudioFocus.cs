using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>One audible media source across the lounge and city, including browser direct audio.</summary>
    internal static class SlopCityMediaAudioFocus
    {
        private sealed class Request
        {
            public MonoBehaviour source;
            public float score, volume, updatedAt;
        }
        private static readonly List<Request> Requests = new List<Request>(2);
        private static Request active;
        private static float gain, holdUntil;
        private static int evaluatedFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Requests.Clear(); active = null; gain = holdUntil = 0f; evaluatedFrame = -1;
        }

        internal static void Submit(MonoBehaviour source, float score, float volume)
        {
            // Submission runs every frame. A capturing predicate otherwise creates
            // garbage on every update of both the lounge and city mixer.
            Request request = null;
            for (int index = 0; index < Requests.Count; index++)
                if (Requests[index].source == source) { request = Requests[index]; break; }
            if (request == null) { request = new Request { source = source }; Requests.Add(request); }
            request.score = Mathf.Clamp01(score);
            request.volume = Mathf.Clamp01(volume);
            request.updatedAt = Time.unscaledTime;
        }

        internal static void Remove(MonoBehaviour source)
        {
            if (active != null && active.source == source) { active = null; gain = 0f; }
            Requests.RemoveAll(item => !item.source || item.source == source);
        }

        internal static float Gain(MonoBehaviour source)
        {
            if (evaluatedFrame != Time.frameCount)
            {
                evaluatedFrame = Time.frameCount;
                Request best = null;
                foreach (Request request in Requests)
                {
                    if (Score(request) <= .001f) continue;
                    if (best == null || Score(request) > Score(best)) best = request;
                }
                if (Score(active) > .001f && (Time.unscaledTime < holdUntil || best == null
                    || Score(best) < Score(active) * 1.25f)) best = active;
                if (best != active)
                {
                    gain = Mathf.MoveTowards(gain, 0f, Time.unscaledDeltaTime / .18f);
                    // The old source reaches silence before the next source receives any gain.
                    if (gain <= .001f) { gain = 0f; active = best; holdUntil = Time.unscaledTime + .5f; }
                }
                else gain = Mathf.MoveTowards(gain, Score(active), Time.unscaledDeltaTime / .25f);
            }
            return active != null && active.source == source && Score(active) > .001f ? gain : 0f;
        }

        private static float Score(Request request)
            => request != null && request.source && request.source.isActiveAndEnabled
                && Time.unscaledTime - request.updatedAt < .5f ? request.score * request.volume : 0f;
    }
}
