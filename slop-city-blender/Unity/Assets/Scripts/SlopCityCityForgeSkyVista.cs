using UnityEngine;

namespace SlopCity
{
    /// <summary>Keeps the authored Sky Sprawl sky unobstructed by generated scenery.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityForgeSkyVista : MonoBehaviour
    {
        private Transform followTarget;
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
            SetDistantGroundSuppressed(SlopCityCityForgeTravel.IsSkySprawlPosition(followTarget.position));
        }

        private void OnDisable()
        {
            SetDistantGroundSuppressed(false);
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
    }
}
