using UnityEngine;

namespace SlopCity
{
    /// <summary>Keeps the authored skyline clear and gives the reclaimed Sprawl an ocean edge.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityForgeSkyVista : MonoBehaviour
    {
        private Transform followTarget;
        // This is one camera-relative visual tile, not streamed ground. It
        // makes the unbuilt exterior read as ocean without creating a walkable
        // surface or implying city fabric beyond the reclaimed island.
        private const float OceanSpan = 576f;
        private const float OceanElevation = -.42f;
        private GameObject oceanBody;
        private Material oceanMaterial;
        // The authored scene includes this huge distant visual mesh. It has no
        // collider, so it never stopped a fall, but it made the Sky Sprawl
        // look like it still had an infinite floor. Keep the authored city
        // intact everywhere else and suppress only that renderer while the
        // player is in the sky-island frontier.
        // The authored ground participates in static batching.  Toggling its
        // MeshRenderer alone is not reliable on native builds: its baked
        // draw can survive even after Renderer.enabled is false.  Hide the
        // actual object while the Sky Sprawl is active, then restore precisely
        // the scene's prior active state when the player returns to Old City.
        private GameObject distantGroundObject;
        private bool distantGroundWasActive;
        private bool distantGroundSuppressed;

        public void Configure(Transform target)
        {
            followTarget = target;
        }

        private void LateUpdate()
        {
            if (!followTarget) return;
            bool islandContext = SlopCityCityForgeTravel.IsSkySprawlPosition(followTarget.position);
            SetDistantGroundSuppressed(islandContext);
            SetOceanVisible(islandContext);
        }

        private void OnDisable()
        {
            SetDistantGroundSuppressed(false);
            SetOceanVisible(false);
        }

        private void SetOceanVisible(bool visible)
        {
            if (!visible)
            {
                if (oceanBody && oceanBody.activeSelf) oceanBody.SetActive(false);
                return;
            }

            EnsureOcean();
            if (!oceanBody) return;
            Vector3 point = followTarget ? followTarget.position : transform.position;
            oceanBody.transform.position = new Vector3(SnapOcean(point.x), OceanElevation, SnapOcean(point.z));
            if (!oceanBody.activeSelf) oceanBody.SetActive(true);
        }

        private void EnsureOcean()
        {
            if (oceanBody) return;
            oceanBody = GameObject.CreatePrimitive(PrimitiveType.Plane);
            oceanBody.name = "SlopCity_IslandOceanVista";
            oceanBody.transform.SetParent(transform, false);
            // Unity's Plane primitive is ten metres square before transform scale.
            oceanBody.transform.localScale = Vector3.one * (OceanSpan / 10f);
            oceanBody.layer = gameObject.layer;

            Collider collider = oceanBody.GetComponent<Collider>();
            if (collider)
            {
                collider.enabled = false;
                if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            }

            Renderer renderer = oceanBody.GetComponent<Renderer>();
            Shader shader = Shader.Find("Standard");
            if (!shader) shader = Shader.Find("Unlit/Color");
            if (!renderer || !shader) return;
            oceanMaterial = new Material(shader) { name = "Slop City / Island Ocean" };
            Color water = new Color(.025f, .105f, .145f, 1f);
            if (oceanMaterial.HasProperty("_Color")) oceanMaterial.SetColor("_Color", water);
            if (oceanMaterial.HasProperty("_BaseColor")) oceanMaterial.SetColor("_BaseColor", water);
            if (oceanMaterial.HasProperty("_Metallic")) oceanMaterial.SetFloat("_Metallic", .24f);
            if (oceanMaterial.HasProperty("_Glossiness")) oceanMaterial.SetFloat("_Glossiness", .76f);
            renderer.sharedMaterial = oceanMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static float SnapOcean(float value)
        {
            return Mathf.Floor(value / OceanSpan) * OceanSpan + OceanSpan * .5f;
        }

        private void SetDistantGroundSuppressed(bool suppress)
        {
            if (suppress)
            {
                // Cache at the time it is first needed rather than in Awake:
                // the scene object can be enabled after this runtime bootstrap.
                if (!distantGroundObject)
                {
                    distantGroundObject = GameObject.Find("Distant_City_Ground");
                    if (distantGroundObject) distantGroundWasActive = distantGroundObject.activeSelf;
                }
                if (distantGroundObject && distantGroundObject.activeSelf)
                {
                    distantGroundObject.SetActive(false);
                }
                distantGroundSuppressed = true;
                return;
            }

            if (!distantGroundSuppressed) return;
            if (distantGroundObject) distantGroundObject.SetActive(distantGroundWasActive);
            distantGroundSuppressed = false;
        }

        private void OnDestroy()
        {
            if (oceanBody) DestroyOwned(oceanBody);
            if (oceanMaterial) DestroyOwned(oceanMaterial);
        }

        private static void DestroyOwned(UnityEngine.Object item)
        {
            if (!item) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(item); else UnityEngine.Object.DestroyImmediate(item);
        }
    }
}
