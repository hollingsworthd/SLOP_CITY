using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>A physical display with its own assigned post and retained picture.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityScreen : MonoBehaviour
    {
        [SerializeField] private Renderer display;
        [SerializeField] private int channel;
        [SerializeField] private bool audioOnly;
        private static readonly HashSet<SlopCityCityScreen> ActiveScreens = new HashSet<SlopCityCityScreen>();
        [SerializeField] private float audibleRange = 14f;
        [SerializeField] private string featuredCreator = "", featuredPostId = "";
        [SerializeField] private float featuredHoldAtSeconds = -1f, featuredHoldSeconds = 10f;
        private Material originalMaterial;
        private bool capturedMaterial;
        private string clipKey = "", clipTitle = "";

        public Renderer Display => display;
        public int Channel => channel;
        public bool AudioOnly => audioOnly;
        public float AudibleRange => audibleRange;
        public float FocusedAudibleRange => Mathf.Max(audibleRange, 30f);
        public string ClipKey => clipKey;
        public string ClipTitle => clipTitle;
        public bool IsFeatured => !string.IsNullOrEmpty(featuredCreator) && !string.IsNullOrEmpty(featuredPostId);
        public string FeaturedClipKey => IsFeatured ? featuredCreator + "/" + featuredPostId : "";
        public float FeaturedHoldAtSeconds => featuredHoldAtSeconds;
        public float FeaturedHoldSeconds => featuredHoldSeconds;

        public void ConfigureFeaturedPost(string creator, string postId, float holdAtSeconds, float holdSeconds = 10f)
        {
            bool changed = featuredCreator != creator || featuredPostId != postId;
            featuredCreator = creator; featuredPostId = postId;
            featuredHoldAtSeconds = float.IsNaN(holdAtSeconds) || float.IsInfinity(holdAtSeconds) ? -1f : holdAtSeconds;
            featuredHoldSeconds = float.IsNaN(holdSeconds) || float.IsInfinity(holdSeconds) ? 10f : Mathf.Max(0f, holdSeconds);
            if (changed && SlopCityCityMedia.Instance) SlopCityCityMedia.Instance.RefreshScreenAssignment(this);
        }
        // Authored displays are Unity quads, whose visible face points along local -Z.
        public Vector3 FrontNormal => display ? display.transform.TransformDirection(Vector3.back).normalized : -transform.forward;

        internal Vector3 ClosestSurfacePoint(Vector3 position)
        {
            if (!display) return transform.position;
            // Work in the quad's local bounds, rather than its world AABB, so rotated
            // and tall displays measure distance from their actual screen surface.
            Vector3 local = display.transform.InverseTransformPoint(position);
            Bounds bounds = display.localBounds;
            local.x = Mathf.Clamp(local.x, bounds.min.x, bounds.max.x);
            local.y = Mathf.Clamp(local.y, bounds.min.y, bounds.max.y);
            local.z = bounds.center.z;
            return display.transform.TransformPoint(local);
        }

        internal bool TryAimPoint(Transform listener, out Vector3 point)
        {
            point = display ? display.bounds.center : transform.position;
            if (audioOnly || !display || !listener) return false;
            var ray = new Ray(listener.position, listener.forward);
            var plane = new Plane(FrontNormal, point);
            if (Vector3.Dot(FrontNormal, listener.forward) >= -.03f
                || !plane.Raycast(ray, out float distance) || distance > FocusedAudibleRange) return false;
            point = ray.GetPoint(distance);
            Vector3 local = display.transform.InverseTransformPoint(point);
            Bounds bounds = display.localBounds;
            return local.x >= bounds.min.x && local.x <= bounds.max.x
                && local.y >= bounds.min.y && local.y <= bounds.max.y;
        }

        internal void AssignClip(string key, string title)
        {
            clipKey = key ?? "";
            clipTitle = title ?? "";
        }

        public void Configure(Renderer renderer, int selectedChannel = 0, float range = 14f)
        {
            if (SlopCityCityMedia.Instance) SlopCityCityMedia.Instance.UnregisterScreen(this);
            Restore();
            display = renderer;
            channel = Mathf.Max(0, selectedChannel);
            audibleRange = Mathf.Clamp(range, 3f, 30f);
            TryRegister();
        }

        /// <summary>Use assigned video audio without replacing the prop material or retaining a picture.</summary>
        public void ConfigureAudioOnly()
        {
            if (audioOnly) return;
            if (SlopCityCityMedia.Instance) SlopCityCityMedia.Instance.UnregisterScreen(this);
            Restore();
            audioOnly = true;
            TryRegister();
        }

        internal void Tune(int selectedChannel, Material material)
        {
            channel = selectedChannel;
            if (audioOnly || !display) return;
            if (!capturedMaterial)
            {
                originalMaterial = display.sharedMaterial;
                capturedMaterial = true;
            }
            channel = selectedChannel;
            display.sharedMaterial = material;
            display.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            display.receiveShadows = false;
        }

        internal void Restore()
        {
            if (capturedMaterial && display) display.sharedMaterial = originalMaterial;
            capturedMaterial = false;
        }

        private void TryRegister()
        {
            if (isActiveAndEnabled && display && SlopCityCityMedia.Instance)
                SlopCityCityMedia.Instance.RegisterScreen(this);
        }

        internal static void RegisterActiveScreens(SlopCityCityMedia media)
        {
            foreach (SlopCityCityScreen screen in ActiveScreens)
                if (screen) media.RegisterScreen(screen);
        }

        private void OnEnable() { ActiveScreens.Add(this); TryRegister(); }
        private void Start() { TryRegister(); }
        private void OnDisable()
        {
            ActiveScreens.Remove(this);
            if (SlopCityCityMedia.Instance) SlopCityCityMedia.Instance.UnregisterScreen(this);
        }
        private void OnDestroy() { OnDisable(); Restore(); }
    }
}
