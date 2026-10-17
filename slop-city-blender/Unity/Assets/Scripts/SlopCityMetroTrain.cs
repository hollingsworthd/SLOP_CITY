using System;
using UnityEngine;

namespace SlopCity
{
    /// <summary>One automatic physical carriage. Timetable time stops when its swept path is blocked.</summary>
    [DefaultExecutionOrder(-45)]
    public sealed class SlopCityMetroTrain : MonoBehaviour
    {
        public const float DwellSeconds = 8f;
        public const float TravelSeconds = 10f;
        public const float CycleSeconds = 108f;
        private const float DoorSeconds = .8f;
        private readonly Collider[] overlaps = new Collider[64];
        private readonly RaycastHit[] sweeps = new RaycastHit[64];
        private SlopCityMetroWorld world;
        private SlopCityFirstPerson player;
        private CharacterController passenger;
        private Transform model, leftDoor, rightDoor, bridge;
        private BoxCollider leftLeaf, rightLeaf;
        private Collider[] ownColliders;
        private bool[] ignoredPairs;
        private bool configured, moving, held, journeyValid;
        private int stop, journeyOrigin;
        private float dwellElapsed, tripElapsed, openProgress = 1f;
        private Vector3 leftClosed = new Vector3(-.475f, 1.36f, -1.78f);
        private Vector3 rightClosed = new Vector3(.475f, 1.36f, -1.78f);

        public SlopCityMetroWorld World => world;
        public Transform RiderTransform => player ? player.transform : null;
        public int CurrentStop => stop;
        public int NextStop => (stop + 1) % SlopCityMetroWorld.StationCount;
        public bool IsMoving => moving;
        public bool IsHeld => held;
        public bool DoorsOpen => !moving && openProgress > .999f;
        public float DoorProgress => openProgress;
        public float DoorOpenProgress => openProgress;
        public float TripElapsedSeconds => tripElapsed;
        public float SecondsUntilDeparture => moving ? TravelSeconds - tripElapsed + DwellSeconds : Mathf.Max(0f, DwellSeconds - dwellElapsed);
        public event Action<SlopCityMetroTrain, int, int> RiderArrived;

        public bool PlayerInside
        {
            get
            {
                if (!PassengerActive || !model) return false;
                Vector3 center = model.InverseTransformPoint(passenger.transform.TransformPoint(passenger.center));
                float radius = passenger.radius * Mathf.Max(passenger.transform.lossyScale.x, passenger.transform.lossyScale.z);
                float feet = center.y - passenger.height * .5f * passenger.transform.lossyScale.y;
                return Mathf.Abs(center.x) + radius < 5.88f && Mathf.Abs(center.z) + radius < 1.73f
                    && feet > -.2f && feet < 1.2f;
            }
        }
        private bool PassengerActive => player && passenger && passenger.enabled && passenger.gameObject.activeInHierarchy && !player.IsSeated;

        public void Configure(SlopCityMetroWorld route, SlopCityFirstPerson rider, Transform carriage, int startStop)
        {
            if (configured) return;
            if (!route || !carriage || carriage != transform) throw new ArgumentException("Metro component must belong to its carriage root.");
            world = route; player = rider; model = carriage;
            passenger = rider ? rider.GetComponent<CharacterController>() : null;
            stop = ((startStop % SlopCityMetroWorld.StationCount) + SlopCityMetroWorld.StationCount) % SlopCityMetroWorld.StationCount;
            world.GetStopPose(stop, out Vector3 position, out Quaternion rotation);
            model.SetPositionAndRotation(position, rotation);
            Renderer donor = model.GetComponentInChildren<Renderer>(true);
            Material material = donor ? donor.sharedMaterial : null;
            leftDoor = MakeLeaf("Metro_SlidingDoor_Left", leftClosed, material, out leftLeaf);
            rightDoor = MakeLeaf("Metro_SlidingDoor_Right", rightClosed, material, out rightLeaf);
            Transform[] descendants = model.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                descendants[i].gameObject.isStatic = false;
                if (descendants[i].name == "Boarding_Bridge") bridge = descendants[i];
            }
            ownColliders = model.GetComponentsInChildren<Collider>(true);
            ignoredPairs = new bool[ownColliders.Length];
            PositionDoors(1f); configured = true;
            Physics.SyncTransforms();
        }

        private Transform MakeLeaf(string label, Vector3 position, Material material, out BoxCollider collider)
        {
            GameObject leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.name = label; leaf.layer = gameObject.layer;
            leaf.transform.SetParent(model, false);
            leaf.transform.localPosition = position;
            leaf.transform.localScale = new Vector3(.95f, 2.5f, .08f);
            if (material) leaf.GetComponent<Renderer>().sharedMaterial = material;
            collider = leaf.GetComponent<BoxCollider>();
            return leaf.transform;
        }

        private void Update() => Advance(Time.deltaTime);

        /// <summary>Deterministic production step; long frames cap at .25 seconds, swept steps at .0125 seconds.</summary>
        public void Advance(float deltaSeconds)
        {
            if (!configured || !isActiveAndEnabled || deltaSeconds <= 0f || float.IsNaN(deltaSeconds)
                || float.IsInfinity(deltaSeconds) || (player && player.IsPaused)) return;
            float remaining = Mathf.Min(.25f, deltaSeconds);
            while (remaining > .000001f)
            {
                // Far empty cars still sweep their path, but need only one query/pose update per frame.
                // Within 30m retain fine carry/door steps, well outside the maximum .25s swept travel.
                bool nearby = player && (player.transform.position - model.position).sqrMagnitude < 900f;
                float dt = nearby ? Mathf.Min(.0125f, remaining) : remaining; remaining -= dt;
                if (moving) AdvanceTravel(dt); else AdvanceDwell(dt);
            }
        }

        private void AdvanceDwell(float dt)
        {
            held = DoorwayOccupied;
            dwellElapsed = Mathf.Min(DwellSeconds, dwellElapsed + dt);
            bool closing = dwellElapsed >= DwellSeconds - DoorSeconds;
            float next = Mathf.MoveTowards(openProgress, closing ? 0f : 1f, dt / DoorSeconds);
            if (closing && (DoorwayOccupied || LeafTouches(leftLeaf, leftClosed + Vector3.left * next)
                || LeafTouches(rightLeaf, rightClosed + Vector3.right * next)))
            {
                held = true;
                // Reopen fully, then give a fresh two-second boarding interval before another close.
                dwellElapsed = DwellSeconds - DoorSeconds - 2f;
                next = Mathf.MoveTowards(openProgress, 1f, dt / DoorSeconds);
            }
            PositionDoors(next);
            if (dwellElapsed < DwellSeconds || openProgress > .0001f || DoorwayOccupied) return;
            journeyValid = PlayerInside; journeyOrigin = stop;
            tripElapsed = 0f; moving = true;
            if (bridge) bridge.gameObject.SetActive(false);
        }

        private bool DoorwayOccupied
        {
            get
            {
                if (!PassengerActive) return false;
                Vector3 center = model.InverseTransformPoint(passenger.transform.TransformPoint(passenger.center));
                float radius = passenger.radius * Mathf.Max(passenger.transform.lossyScale.x, passenger.transform.lossyScale.z);
                float halfHeight = passenger.height * .5f * passenger.transform.lossyScale.y;
                return Mathf.Abs(center.x) < 1.02f + radius && Mathf.Abs(center.z + 1.78f) < radius + .16f
                    && center.y + halfHeight > .12f && center.y - halfHeight < 2.64f;
            }
        }

        private bool LeafTouches(BoxCollider leaf, Vector3 localPosition)
        {
            return PassengerActive && Physics.ComputePenetration(leaf, model.TransformPoint(localPosition), leaf.transform.rotation,
                passenger, passenger.transform.position, passenger.transform.rotation, out _, out float depth) && depth > .001f;
        }

        private void PositionDoors(float progress)
        {
            if (leftDoor.localPosition == leftClosed + Vector3.left * progress
                && rightDoor.localPosition == rightClosed + Vector3.right * progress) { openProgress = progress; return; }
            openProgress = progress;
            leftDoor.localPosition = leftClosed + Vector3.left * progress;
            rightDoor.localPosition = rightClosed + Vector3.right * progress;
            Physics.SyncTransforms();
        }

        private void AdvanceTravel(float dt)
        {
            float nextTime = Mathf.Min(TravelSeconds, tripElapsed + dt);
            world.SampleLeg(stop, nextTime / TravelSeconds, out Vector3 position, out Quaternion rotation);
            bool aboard = PlayerInside;
            if (!aboard) journeyValid = false;
            if (!PathClear(position, rotation, aboard) || !TryMove(position, rotation, aboard)) { held = true; return; }
            held = false; tripElapsed = nextTime;
            if (tripElapsed < TravelSeconds - .00001f) return;
            int origin = journeyOrigin;
            bool arrivedWithRider = journeyValid && PlayerInside;
            stop = NextStop; moving = false; dwellElapsed = tripElapsed = 0f; journeyValid = false;
            if (bridge) bridge.gameObject.SetActive(true);
            if (arrivedWithRider)
            {
                world.NotifyRiderArrival(this, origin, stop);
                RiderArrived?.Invoke(this, origin, stop);
            }
        }

        private bool PathClear(Vector3 position, Quaternion rotation, bool aboard)
        {
            // Narrow raised body excludes the supported track/rails; small swept steps include curve rotation.
            Vector3 half = new Vector3(5.93f, 1.18f, 1.72f);
            Vector3 offset = Vector3.up * 1.43f;
            Vector3 currentCenter = model.position + model.rotation * offset;
            Vector3 targetCenter = position + rotation * offset;
            float turn = Quaternion.Angle(model.rotation, rotation) * Mathf.Deg2Rad;
            Vector3 sweepHalf = half + new Vector3(1f, 0f, 1f) * (6.2f * turn);
            Vector3 delta = targetCenter - currentCenter;
            if (delta.sqrMagnitude > .0000001f)
            {
                int count = Physics.BoxCastNonAlloc(currentCenter, sweepHalf, delta.normalized, sweeps, model.rotation,
                    delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
                if (count == sweeps.Length) return false;
                for (int i = 0; i < count; i++)
                    if (ExternalBlocker(sweeps[i].collider, aboard) && sweeps[i].normal.y < .7f) return false;
            }
            int total = Physics.OverlapBoxNonAlloc(targetCenter, half, overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
            if (total == overlaps.Length) return false;
            for (int i = 0; i < total; i++) if (ExternalBlocker(overlaps[i], aboard)) return false;
            return true;
        }

        private bool ExternalBlocker(Collider collider, bool aboard)
        {
            if (!collider || collider.transform.IsChildOf(model)) return false;
            if (aboard && collider == passenger) return false;
            return true;
        }

        private bool TryMove(Vector3 position, Quaternion rotation, bool aboard)
        {
            if (!aboard) { model.SetPositionAndRotation(position, rotation); Physics.SyncTransforms(); return true; }
            Vector3 oldPosition = model.position, oldPassenger = player.transform.position;
            Quaternion oldRotation = model.rotation;
            Vector3 localPassenger = Quaternion.Inverse(oldRotation) * (oldPassenger - oldPosition);
            Vector3 desired = position + rotation * localPassenger;
            float yaw = Mathf.DeltaAngle(oldRotation.eulerAngles.y, rotation.eulerAngles.y);
            IgnoreOwnCollisions(true);
            bool success;
            try
            {
                model.SetPositionAndRotation(position, rotation); Physics.SyncTransforms();
                player.MoveWithVehicle(desired - oldPassenger, yaw);
                success = (player.transform.position - desired).sqrMagnitude <= .0004f;
                if (!success)
                {
                    model.SetPositionAndRotation(oldPosition, oldRotation); Physics.SyncTransforms();
                    player.MoveWithVehicle(oldPassenger - player.transform.position, -yaw);
                }
            }
            finally { IgnoreOwnCollisions(false); }
            return success;
        }

        private void IgnoreOwnCollisions(bool ignore)
        {
            if (!passenger) return;
            for (int i = 0; i < ownColliders.Length; i++)
            {
                Collider collider = ownColliders[i];
                if (!collider || collider == passenger || collider.isTrigger || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                if (ignore)
                {
                    ignoredPairs[i] = Physics.GetIgnoreCollision(passenger, collider);
                    if (!ignoredPairs[i]) Physics.IgnoreCollision(passenger, collider, true);
                }
                else if (!ignoredPairs[i]) Physics.IgnoreCollision(passenger, collider, false);
            }
        }

        /// <summary>Lower-bound timetable estimate; obstructions can extend it, never skip physical travel.</summary>
        public float SecondsUntilStop(int destination)
        {
            if (destination < 0 || destination >= SlopCityMetroWorld.StationCount) return float.PositiveInfinity;
            if (!moving && destination == stop) return 0f;
            int legs = (destination - stop + SlopCityMetroWorld.StationCount) % SlopCityMetroWorld.StationCount;
            if (legs == 0) legs = SlopCityMetroWorld.StationCount;
            float first = moving ? TravelSeconds - tripElapsed : SecondsUntilDeparture + TravelSeconds;
            return first + (legs - 1) * (DwellSeconds + TravelSeconds);
        }
    }
}
