using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>Four optional street-parked sedans, sharing the saved city's existing car art.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityStreetCars : MonoBehaviour
    {
        public const int MaximumAdditionalCars = 4;
        public const string RootName = "SlopCity_Additional_Street_Cars";
        private static readonly int[] Districts = { 0, 2, 5, 8 };
        private static readonly float[] ParkingX = { -23f, 8f, -10f, 22f, -16f, 16f, -6f, 6f };
        private readonly List<SlopCityInteractable> cars = new List<SlopCityInteractable>(MaximumAdditionalCars);
        private readonly Collider[] overlaps = new Collider[128];
        private readonly RaycastHit[] groundHits = new RaycastHit[32];
        private bool initialized;
        public int AddedCarCount => cars.Count;
        public IReadOnlyList<SlopCityInteractable> Cars => cars;

        public static SlopCityStreetCars Ensure(SlopCityFirstPerson player) => Ensure(player ? player.transform : null);

        public static SlopCityStreetCars Ensure(Transform player)
        {
            SlopCityDistrict[] districts = FindObjectsByType<SlopCityDistrict>(FindObjectsSortMode.None);
            Transform city = null;
            foreach (SlopCityDistrict district in districts)
                if (district.transform.parent && district.transform.parent.name == "SlopCity_NineBlocks"
                    && (!player || district.gameObject.scene == player.gameObject.scene))
                { city = district.transform.parent; break; }
            // Do not introduce city props into unrelated scenes or lightweight fixtures.
            if (!city || (city.lossyScale - Vector3.one).sqrMagnitude > .0001f) return null;
            Transform root = city.Find(RootName);
            if (!root)
            {
                root = new GameObject(RootName).transform;
                root.SetParent(city, false);
            }
            SlopCityStreetCars owner = root.GetComponent<SlopCityStreetCars>();
            if (!owner) owner = root.gameObject.AddComponent<SlopCityStreetCars>();
            owner.Initialize(city, districts, player);
            return owner;
        }

        private void Initialize(Transform city, SlopCityDistrict[] districts, Transform player)
        {
            if (initialized) return;
            initialized = true;
            foreach (SlopCityInteractable item in GetComponentsInChildren<SlopCityInteractable>(true))
                if (item.Kind == SlopCityInteractionKind.Car) cars.Add(item);
            // A repeated Ensure never replaces a driven car or fills an old parking position again.
            if (cars.Count != 0) return;
            var templates = new List<SlopCityInteractable>();
            foreach (SlopCityInteractable item in city.GetComponentsInChildren<SlopCityInteractable>())
                if (item.Kind == SlopCityInteractionKind.Car && item.name == "Parked_CityCar"
                    && !item.transform.IsChildOf(transform) && SafeTemplate(item.transform)) templates.Add(item);
            if (templates.Count == 0) return;
            templates.Sort((a, b) => string.CompareOrdinal(TemplateKey(a.transform), TemplateKey(b.transform)));
            SlopCityDoor[] doors = city.GetComponentsInChildren<SlopCityDoor>();
            SlopCityCrowd crowd = city.GetComponentInChildren<SlopCityCrowd>();
            Physics.SyncTransforms();
            foreach (int districtIndex in Districts)
            {
                SlopCityDistrict district = Array.Find(districts,
                    item => item.transform.parent == city && item.Index == districtIndex);
                if (!district) continue;
                SlopCityInteractable source = templates[cars.Count % templates.Count];
                Bounds envelope = CarBounds(source.transform);
                Quaternion rotation = district.transform.rotation * Quaternion.Euler(0, 90, 0);
                foreach (float x in ParkingX)
                {
                    // The south pavement ends at -13.1m. Parallel parking occupies the carriageway,
                    // leaving a separate sidewalk and the central traffic lane intact.
                    Vector3 candidate = district.transform.TransformPoint(new Vector3(x, 0, -15.5f));
                    if (!TryGround(candidate, out Vector3 ground) || Mathf.Abs(ground.y - district.transform.position.y) > .35f) continue;
                    Vector3 position = ground + Vector3.up * .02f;
                    if (!ClearBody(position, rotation, envelope) || !Supported(position, rotation, envelope)
                        || !ClearDoors(position, rotation, envelope, doors)
                        || !ClearRoutes(position, rotation, envelope, crowd)
                        || !ClearExit(position, rotation)
                        || !ClearDeparture(position, rotation, envelope)) continue;
                    SlopCityInteractable car = CopyCar(source, position, rotation, districtIndex);
                    if (car) cars.Add(car);
                    Physics.SyncTransforms();
                    break;
                }
                if (cars.Count >= MaximumAdditionalCars) break;
            }
        }

        private static string TemplateKey(Transform item) => item.parent.name + "/" + item.name
            + ":" + item.position.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            + "," + item.position.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        private static bool IsMedia(Transform item) => item.name.StartsWith("SlopCityMediaScreenAnchor", StringComparison.Ordinal)
            || item.GetComponent<SlopCityCityScreen>() || item.GetComponent<SlopCityMediaPlayer>();

        private static bool SafeTemplate(Transform source)
        {
            if ((source.lossyScale - Vector3.one).sqrMagnitude > .0001f) return false;
            bool foundBody = false;
            foreach (MeshRenderer renderer in source.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.isPartOfStaticBatch) return false;
                if (renderer.name == "Car_Body" && renderer.enabled && renderer.GetComponent<MeshFilter>()) foundBody = true;
            }
            return foundBody;
        }

        private static bool Useful(Transform source)
        {
            if (!source.gameObject.activeSelf || IsMedia(source)) return false;
            MeshFilter filter = source.GetComponent<MeshFilter>();
            MeshRenderer renderer = source.GetComponent<MeshRenderer>();
            if (filter && filter.sharedMesh && renderer && renderer.enabled) return true;
            foreach (Collider collider in source.GetComponents<Collider>())
                if (collider.enabled && !collider.isTrigger && SupportedCollider(collider)) return true;
            foreach (Transform child in source) if (Useful(child)) return true;
            return false;
        }

        private static bool SupportedCollider(Collider collider) => collider is BoxCollider || collider is SphereCollider
            || collider is CapsuleCollider || collider is MeshCollider;

        private SlopCityInteractable CopyCar(SlopCityInteractable source, Vector3 position, Quaternion rotation, int district)
        {
            var root = new GameObject("Additional_Street_Car_" + district);
            root.SetActive(false);
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(position, rotation);
            try
            {
                foreach (Transform child in source.transform) if (Useful(child)) CopyNode(child, root.transform);
                var item = root.AddComponent<SlopCityInteractable>();
                item.Configure(SlopCityInteractionKind.Car, "City cruiser / street parking", false);
                item.ConfigureInteractionPoint(root.transform.TransformPoint(source.transform.InverseTransformPoint(source.InteractionPoint)));
                root.SetActive(true);
                return item;
            }
            catch (Exception error)
            {
                root.SetActive(false);
                if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
                Debug.LogWarning("Additional street car unavailable; existing city cars retained: " + error.Message, this);
                return null;
            }
        }

        private static void CopyNode(Transform source, Transform parent)
        {
            var clone = new GameObject(source.name);
            clone.layer = source.gameObject.layer;
            clone.isStatic = false;
            Transform target = clone.transform;
            target.SetParent(parent, false);
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
            MeshFilter filter = source.GetComponent<MeshFilter>();
            MeshRenderer renderer = source.GetComponent<MeshRenderer>();
            if (filter && filter.sharedMesh && renderer && renderer.enabled)
            {
                clone.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                MeshRenderer output = clone.AddComponent<MeshRenderer>();
                output.sharedMaterials = renderer.sharedMaterials;
                output.shadowCastingMode = renderer.shadowCastingMode;
                output.receiveShadows = renderer.receiveShadows;
                output.lightProbeUsage = renderer.lightProbeUsage;
                output.reflectionProbeUsage = renderer.reflectionProbeUsage;
                output.motionVectorGenerationMode = renderer.motionVectorGenerationMode;
            }
            foreach (Collider collider in source.GetComponents<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                Collider output = null;
                if (collider is BoxCollider box)
                { var shape = clone.AddComponent<BoxCollider>(); shape.center = box.center; shape.size = box.size; output = shape; }
                else if (collider is SphereCollider sphere)
                { var shape = clone.AddComponent<SphereCollider>(); shape.center = sphere.center; shape.radius = sphere.radius; output = shape; }
                else if (collider is CapsuleCollider capsule)
                { var shape = clone.AddComponent<CapsuleCollider>(); shape.center = capsule.center; shape.radius = capsule.radius; shape.height = capsule.height; shape.direction = capsule.direction; output = shape; }
                else if (collider is MeshCollider mesh && mesh.sharedMesh)
                { var shape = clone.AddComponent<MeshCollider>(); shape.convex = mesh.convex; shape.cookingOptions = mesh.cookingOptions; shape.sharedMesh = mesh.sharedMesh; output = shape; }
                if (output) output.sharedMaterial = collider.sharedMaterial;
            }
            foreach (Transform child in source) if (Useful(child)) CopyNode(child, target);
        }

        private static Bounds CarBounds(Transform source)
        {
            var result = new Bounds(new Vector3(0, .8f, 0), new Vector3(2.2f, 1.6f, 4.1f));
            foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.sharedMesh || IsMedia(filter.transform) || filter.GetComponentInParent<SlopCityCityScreen>()) continue;
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (!renderer || !renderer.enabled) continue;
                Bounds bounds = filter.sharedMesh.bounds;
                Matrix4x4 matrix = source.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                    result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z)));
            }
            return result;
        }

        private bool TryGround(Vector3 point, out Vector3 ground)
        {
            ground = point;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 1.1f, Vector3.down, groundHits, 2f, ~0, QueryTriggerInteraction.Ignore);
            if (count >= groundHits.Length) return false;
            bool found = false; float closest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i];
                if (hit.normal.y < .85f || hit.distance >= closest) continue;
                closest = hit.distance; ground = hit.point; found = true;
            }
            return found;
        }

        private bool ClearBody(Vector3 position, Quaternion rotation, Bounds envelope)
        {
            Vector3 extents = envelope.extents + new Vector3(.25f, 0, .25f);
            int count = Physics.OverlapBoxNonAlloc(position + rotation * envelope.center, extents, overlaps,
                rotation, ~0, QueryTriggerInteraction.Ignore);
            if (count >= overlaps.Length) return false;
            for (int i = 0; i < count; i++)
                if (overlaps[i] && overlaps[i].bounds.max.y > position.y + .20f) return false;
            return true;
        }

        private bool Supported(Vector3 position, Quaternion rotation, Bounds envelope)
        {
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = position + rotation * new Vector3(envelope.center.x + x * envelope.extents.x * .85f, 0,
                        envelope.center.z + z * envelope.extents.z * .85f);
                    if (!TryGround(corner, out Vector3 ground) || Mathf.Abs(ground.y - position.y + .02f) > .12f) return false;
                }
            return true;
        }

        private static bool ClearDoors(Vector3 position, Quaternion rotation, Bounds envelope, SlopCityDoor[] doors)
        {
            Quaternion inverse = Quaternion.Inverse(rotation);
            foreach (SlopCityDoor door in doors)
            {
                Vector3 point = inverse * (door.InteractionPoint - position) - envelope.center;
                if (Mathf.Abs(point.y) > 3f) continue;
                float x = Mathf.Max(0, Mathf.Abs(point.x) - envelope.extents.x);
                float z = Mathf.Max(0, Mathf.Abs(point.z) - envelope.extents.z);
                if (x * x + z * z < 2.75f * 2.75f) return false;
            }
            return true;
        }

        private static bool ClearRoutes(Vector3 position, Quaternion rotation, Bounds envelope, SlopCityCrowd crowd)
        {
            if (!crowd) return true;
            Quaternion inverse = Quaternion.Inverse(rotation);
            Vector3 min = envelope.min - new Vector3(.55f, 0, .55f), max = envelope.max + new Vector3(.55f, 0, .55f);
            foreach (SlopCityCrowdRoute route in crowd.Routes)
            {
                if (route == null || route.nodes == null) continue;
                for (int i = 0; i < route.nodes.Length; i++)
                {
                    SlopCityCrowdNode node = route.nodes[i];
                    if (node == null) continue;
                    Vector3 a = inverse * (node.position - position);
                    if (Mathf.Abs(a.y) < .8f && a.x >= min.x && a.x <= max.x && a.z >= min.z && a.z <= max.z) return false;
                    if (node.neighbors == null) continue;
                    foreach (int neighbor in node.neighbors)
                    {
                        if (neighbor < 0 || neighbor >= route.nodes.Length || route.nodes[neighbor] == null) continue;
                        Vector3 b = inverse * (route.nodes[neighbor].position - position);
                        if (Mathf.Min(a.y, b.y) > .8f || Mathf.Max(a.y, b.y) < -.8f) continue;
                        float low = 0f, high = 1f;
                        if (Clip(a.x, b.x - a.x, min.x, max.x, ref low, ref high)
                            && Clip(a.z, b.z - a.z, min.z, max.z, ref low, ref high)) return false;
                    }
                }
            }
            return true;
        }

        private static bool Clip(float origin, float delta, float min, float max, ref float low, ref float high)
        {
            if (Mathf.Abs(delta) < .00001f) return origin >= min && origin <= max;
            float a = (min - origin) / delta, b = (max - origin) / delta;
            if (a > b) { float swap = a; a = b; b = swap; }
            low = Mathf.Max(low, a); high = Mathf.Min(high, b);
            return low <= high;
        }

        private bool ClearExit(Vector3 position, Quaternion rotation)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 point = position + rotation * new Vector3(side * 1.6f, 0, 0);
                if (!TryGround(point, out Vector3 ground)) continue;
                int count = Physics.OverlapCapsuleNonAlloc(ground + Vector3.up * .28f, ground + Vector3.up * 1.6f,
                    .24f, overlaps, ~0, QueryTriggerInteraction.Ignore);
                if (count == 0) return true;
            }
            return false;
        }

        private bool ClearDeparture(Vector3 position, Quaternion rotation, Bounds envelope)
        {
            foreach (float direction in new[] { 1f, -1f })
            {
                Vector3 middle = position + rotation * Vector3.forward * direction * 2.25f;
                Vector3 departure = position + rotation * Vector3.forward * direction * 4.5f;
                if (ClearBody(middle, rotation, envelope) && Supported(middle, rotation, envelope)
                    && ClearBody(departure, rotation, envelope) && Supported(departure, rotation, envelope)) return true;
            }
            return false;
        }
    }
}
