using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopCity
{
    /// <summary>Eight bounded, depth-tested laser segments over an already checked walking route.</summary>
    public sealed class SlopCitySignalBeams : MonoBehaviour
    {
        public const int MaximumSegments = 8;
        public const float MaximumAimReach = 3.4f;
        private const float BeamHeight = 1f, AimRadius = .22f;
        private readonly LineRenderer[] lines = new LineRenderer[MaximumSegments];
        private readonly Vector3[] starts = new Vector3[MaximumSegments];
        private readonly Vector3[] ends = new Vector3[MaximumSegments];
        private readonly RaycastHit[] hits = new RaycastHit[24];
        private LineRenderer pulse;
        private Material material;
        private Transform owner;
        private int count;
        private float totalLength;
        private Color beamTint;

        public void Configure(Transform playerOwner)
        {
            owner = playerOwner;
            Clear();
        }

        /// <summary>Refresh at the caller's bounded route cadence; never owns or mutates the route.</summary>
        public void SetPath(IReadOnlyList<Vector3> path, int startIndex, bool active, Color tint)
        {
            Clear();
            if (!active || !isActiveAndEnabled || !owner || path == null || path.Count < 2 || !EnsureResources()) return;
            if (!Finite(tint.r) || !Finite(tint.g) || !Finite(tint.b)) return;
            beamTint = new Color(Mathf.Clamp01(tint.r), Mathf.Clamp01(tint.g), Mathf.Clamp01(tint.b), .20f);
            int start = Mathf.Clamp(startIndex, 0, path.Count - 1);
            // An exact sequence of route edges preserves corners; no straight-line shortcut
            // is introduced by the visual guide. Bound both reads and physics work to eight.
            for (int edge = start; edge < path.Count - 1 && edge - start < MaximumSegments; edge++)
            {
                Vector3 a = path[edge] + Vector3.up * BeamHeight;
                Vector3 b = path[edge + 1] + Vector3.up * BeamHeight;
                if (!Finite(a) || !Finite(b)) break;
                Vector3 delta = b - a;
                float length = delta.magnitude;
                if (length < .08f) continue;
                // Route points can span a level transition. Avoid drawing a long beam
                // through floors or across the city if the route is not yet connected.
                if (length > 12f || (a - owner.position).sqrMagnitude > 40f * 40f) break;
                float clear = ClearDistance(a, delta / length, length);
                bool clipped = clear < length;
                if (clipped) b = a + delta / length * Mathf.Max(0, clear - .035f);
                if ((b - a).sqrMagnitude >= .08f * .08f)
                {
                    starts[count] = a; ends[count] = b;
                    LineRenderer line = lines[count];
                    line.startColor = beamTint; line.endColor = beamTint;
                    line.SetPosition(0, a); line.SetPosition(1, b); line.enabled = true;
                    totalLength += Vector3.Distance(a, b); count++;
                }
                if (clipped || count == MaximumSegments) break;
            }
            UpdatePulse();
        }

        public void Clear()
        {
            count = 0; totalLength = 0;
            for (int i = 0; i < lines.Length; i++) if (lines[i]) lines[i].enabled = false;
            if (pulse) pulse.enabled = false;
        }

        /// <summary>Only a visible nearby beam may be acquired; the ray never targets through solids.</summary>
        public bool TryAim(Camera camera, float reach, out Vector3 point)
        {
            point = default;
            if (!isActiveAndEnabled || !camera || count == 0 || !Finite(reach) || reach <= 0) return false;
            float limit = Mathf.Min(reach, MaximumAimReach);
            Vector3 origin = camera.transform.position;
            Vector3 forward = camera.transform.forward;
            Vector3 rayEnd = origin + forward * limit;
            float closest = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                ClosestPoints(origin, rayEnd, starts[i], ends[i], out Vector3 onRay, out Vector3 onBeam);
                Vector3 toBeam = onBeam - origin;
                if ((onRay - onBeam).sqrMagnitude > AimRadius * AimRadius || toBeam.sqrMagnitude > limit * limit
                    || Vector3.Dot(toBeam, forward) <= .03f) continue;
                float distance = toBeam.magnitude;
                if (distance >= closest || distance < .001f) continue;
                // Depth testing protects the picture; an explicit sight test protects USE.
                if (ClearDistance(origin, toBeam / distance, distance) < distance - .015f) continue;
                closest = distance; point = onBeam; found = true;
            }
            return found;
        }

        private float ClearDistance(Vector3 origin, Vector3 direction, float length)
        {
            int found = Physics.RaycastNonAlloc(origin, direction, hits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // A full non-alloc buffer cannot prove a clear path; hide/refuse conservatively.
            if (found == hits.Length) return 0;
            float closest = length;
            for (int i = 0; i < found; i++)
            {
                Collider collider = hits[i].collider;
                if (!collider || collider.isTrigger || (owner && (collider.transform == owner || collider.transform.IsChildOf(owner)))) continue;
                closest = Mathf.Min(closest, hits[i].distance);
            }
            return closest;
        }

        private bool EnsureResources()
        {
            bool ready = material && pulse;
            for (int i = 0; i < lines.Length; i++) ready &= lines[i];
            if (ready) return true;
            Release();
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored");
            if (!shader || !shader.isSupported) return false;
            material = new Material(shader) { name = "Signal laser / shared", hideFlags = HideFlags.HideAndDontSave };
            material.color = Color.white;
            if (material.HasProperty("_ZTest")) material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
            if (material.HasProperty("_Cull")) material.SetInt("_Cull", (int)CullMode.Off);
            // Both shaders consume line vertex colors. Do not fall back to a shader
            // that ignores them and turns the faint tinted guide into opaque white.
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.renderQueue = (int)RenderQueue.Transparent;
            for (int i = 0; i < lines.Length; i++) lines[i] = CreateLine("Signal beam " + i, .011f);
            pulse = CreateLine("Signal traveling pulse", .023f);
            return true;
        }

        private LineRenderer CreateLine(string label, float width)
        {
            var child = new GameObject(label) { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
            child.transform.SetParent(transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = material; line.useWorldSpace = true;
            line.positionCount = 2; line.startWidth = width; line.endWidth = width;
            line.numCapVertices = 2; line.numCornerVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off; line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.alignment = LineAlignment.View; line.enabled = false;
            return line;
        }

        private void Update()
        {
            // SetPath supplies fixed endpoints; only this tiny pulse changes between route updates.
            if (count > 0 && Time.timeScale > 0) UpdatePulse();
        }
        private void UpdatePulse()
        {
            if (!pulse || count == 0 || totalLength < .08f) return;
            float along = Mathf.Repeat(Time.time * 1.7f, totalLength);
            for (int i = 0; i < count; i++)
            {
                float length = Vector3.Distance(starts[i], ends[i]);
                if (along > length) { along -= length; continue; }
                Vector3 direction = (ends[i] - starts[i]) / length;
                Vector3 a = starts[i] + direction * along;
                Vector3 b = starts[i] + direction * Mathf.Min(length, along + .32f);
                Color tint = new Color(beamTint.r, beamTint.g, beamTint.b, .62f);
                pulse.startColor = tint; pulse.endColor = new Color(tint.r, tint.g, tint.b, .08f);
                pulse.SetPosition(0, a); pulse.SetPosition(1, b); pulse.enabled = true; return;
            }
        }

        // Closest points on two finite segments; the first segment is the bounded view ray.
        private static void ClosestPoints(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out Vector3 first, out Vector3 second)
        {
            Vector3 u = b - a, v = d - c, w = a - c;
            float aa = Vector3.Dot(u,u), bb = Vector3.Dot(u,v), cc = Vector3.Dot(v,v);
            float dd = Vector3.Dot(u,w), ee = Vector3.Dot(v,w), denominator = aa * cc - bb * bb;
            float s = denominator > .000001f ? Mathf.Clamp01((bb * ee - cc * dd) / denominator) : 0;
            float t = cc > .000001f ? (bb * s + ee) / cc : 0;
            if (t < 0) { t = 0; s = aa > .000001f ? Mathf.Clamp01(-dd / aa) : 0; }
            else if (t > 1) { t = 1; s = aa > .000001f ? Mathf.Clamp01((bb - dd) / aa) : 0; }
            first = a + u * s; second = c + v * t;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 point) => Finite(point.x) && Finite(point.y) && Finite(point.z);
        private void OnDisable() { Clear(); Release(); }
        private void OnDestroy() { Release(); }
        private void Release()
        {
            Clear();
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i]) DestroyOwned(lines[i].gameObject);
                lines[i] = null;
            }
            if (pulse) DestroyOwned(pulse.gameObject);
            pulse = null;
            if (material) DestroyOwned(material);
            material = null;
        }
        private static void DestroyOwned(UnityEngine.Object resource)
        {
            if (Application.isPlaying) Destroy(resource);
            else DestroyImmediate(resource);
        }
    }
}
