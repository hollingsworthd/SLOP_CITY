using UnityEngine;

namespace SlopCity
{
    /// <summary>Physical job routes and transmissions through the existing bounded signal renderer.</summary>
    public sealed class SlopCityRabbitTrail : MonoBehaviour
    {
        private readonly SlopCityHintRoute route = new SlopCityHintRoute();
        private SlopCitySandbox sandbox;
        private SlopCityFirstPerson player;
        private Camera view;
        private SlopCityInteractable target, plannedRouteTarget;
        private SlopCityMatrixHunt signalOwner;
        private bool paused = true;
        private float nextRouteAt, nextTransmissionAt, transmissionUntil, nextBeamAt;
        private Vector3 plannedTarget;
        private string district, landmark, transmission = "The job receiver is listening.";
        private string hintedJobId, hintedAction;
        private object hintedAssignment;
        private bool instructionBeamActive;
        private SlopCityInteractable levelEntrance;
        private int proximity = -1;
        private SlopCityDoor messageDoor;
        public string TransmissionText => (Time.time < transmissionUntil ? "[ CITY DISPATCH ]" : "[ CURRENT JOB ]") + "\n" + transmission;
        public float TransmissionOpacity => Time.time < transmissionUntil ? 1f : .6f;

        public void Configure(SlopCityFirstPerson owner)
        {
            player = owner;
            enabled = true;
        }

        public void Present(SlopCitySandbox source, Camera camera, bool isPaused)
        {
            if (sandbox != source) HideTrail();
            sandbox = source; view = camera; paused = isPaused;
            signalOwner = sandbox ? sandbox.MatrixHunt : null;
            if (paused) HideTrail();
        }



        private void Update()
        {
            if (paused || !sandbox || !player || !view || !sandbox.HasWaypoint) { HideTrail(); return; }
            if (!signalOwner || !signalOwner.JobInstructionsVisible) { HideTrail(); return; }
            RefreshObjective();
            Vector3 feet = player.transform.position;
            if (player.IsSeated || sandbox.IsDriving || sandbox.IsResting || sandbox.IsTravelling)
            {
                HideTrail();
                transmission = player.IsSeated ? "Stand up to follow the current job through the city."
                    : "Your job and cargo are kept. Continue on foot to follow the dispatch beam.";
                nextTransmissionAt = 0f;
                return;
            }
            if (!signalOwner.SignalVisible)
            {
                // Keep the job instruction after the physical signal expires,
                // without continuing route searches or submitting expired beams.
                HideTrail();
                int instructionBand = ProximityBand(sandbox.WaypointDistance);
                if (instructionBeamActive || instructionBand != proximity || Time.time >= nextTransmissionAt)
                {
                    instructionBeamActive = false; messageDoor = null; proximity = instructionBand;
                    transmission = ComposeJobInstructions(feet, instructionBand);
                    transmissionUntil = Time.time + 9f; nextTransmissionAt = Time.time + 22f;
                }
                return;
            }
            if (!instructionBeamActive)
            {
                instructionBeamActive = true;
                nextRouteAt = nextTransmissionAt = 0f;
            }
            bool anotherLevel = Mathf.Abs(sandbox.WaypointPosition.y - feet.y) > 2.5f;
            SlopCityInteractable routeTarget = anotherLevel && levelEntrance ? levelEntrance : target;
            if (!routeTarget) { HideTrail(); return; }
            Vector3 routeDestination = routeTarget.InteractionPoint;
            bool recalculate = nextRouteAt == 0f || plannedRouteTarget != routeTarget;
            if (!recalculate && route.Finished && Time.time >= nextRouteAt)
            {
                // Walking along the checked path does not invalidate it. Tiny
                // movement and a stale timer alone must not erase the signal.
                nextRouteAt = Time.time + 2f;
                recalculate = !route.ReachedTarget || route.DistanceFromPathSquared(feet) > 8f * 8f
                    || (routeDestination - plannedTarget).sqrMagnitude > 8f * 8f;
            }
            if (recalculate)
            {
                HideTrail();
                plannedRouteTarget = routeTarget;
                plannedTarget = routeDestination;
                route.Begin(feet, plannedTarget, player.transform, routeTarget.transform);
                nextRouteAt = Time.time + 2f;
            }
            // An unfinished query is bounded by the solver; movement cannot
            // repeatedly restart it before it produces a route or a failure.
            bool wasFinished = route.Finished;
            route.Step();
            if (!wasFinished && route.Finished)
            {
                nextTransmissionAt = 0f;
                if (!route.ReachedTarget) nextRouteAt = Time.time + 6f;
            }
            DrawTrail(feet);
            SlopCityDoor door = NextDoor(feet);
            if (door != messageDoor) { messageDoor = door; nextTransmissionAt = 0f; }
            int band = ProximityBand(sandbox.WaypointDistance);
            if (band != proximity || Time.time >= nextTransmissionAt)
            {
                proximity = band;
                transmission = ComposeTransmission(feet, band);
                transmissionUntil = Time.time + 9f; nextTransmissionAt = Time.time + 22f;
            }
        }

        private void RefreshObjective()
        {
            SlopCityInteractable nextTarget = sandbox.ObjectiveTarget;
            string jobId = sandbox.CurrentJobId, action = sandbox.ObjectiveAction;
            object assignment = sandbox.CurrentJobAssignment;
            bool targetChanged = target != nextTarget;
            if (!targetChanged && ReferenceEquals(hintedAssignment, assignment) && hintedJobId == jobId && hintedAction == action) return;
            hintedAssignment = assignment; hintedJobId = jobId; hintedAction = action;
            // A repair, retune or restart can be a new stage at the same device.
            // Refresh its instruction now without restarting an unchanged route.
            nextTransmissionAt = nextBeamAt = 0f; proximity = -1;
            if (targetChanged)
            {
                HideTrail(); plannedRouteTarget = null;
                target = nextTarget; nextRouteAt = 0f;
                DescribeDestination();
            }
        }

        private void DescribeDestination()
        {
            if (!target) return;
            SlopCityDistrict block = target.GetComponentInParent<SlopCityDistrict>();
            if (!block)
            {
                float nearest = float.PositiveInfinity;
                foreach (SlopCityDistrict candidate in player.Districts)
                {
                    if (!candidate) continue;
                    float distance = (candidate.Center - target.InteractionPoint).sqrMagnitude;
                    if (distance < nearest) { nearest = distance; block = candidate; }
                }
            }
            district = block ? block.DisplayName : "the neighborhood";
            landmark = target.DisplayName;
            levelEntrance = null; float nearestLanding = float.PositiveInfinity;
            foreach (SlopCityInteractable entrance in FindObjectsByType<SlopCityInteractable>(FindObjectsSortMode.None))
            {
                if (!entrance.Destination || Mathf.Abs(entrance.Destination.position.y - target.InteractionPoint.y) > 3f) continue;
                float score = (entrance.Destination.position - target.InteractionPoint).sqrMagnitude;
                if (score < nearestLanding) { nearestLanding = score; levelEntrance = entrance; }
            }
            if (!block) return;
            float best = 225f;
            foreach (SlopCityInteractable item in block.GetComponentsInChildren<SlopCityInteractable>())
            {
                if (item == target || item.Kind != SlopCityInteractionKind.Building) continue;
                float distance = (item.InteractionPoint - target.InteractionPoint).sqrMagnitude;
                if (distance < best) { best = distance; landmark = target.DisplayName + " near " + item.DisplayName; }
            }
        }

        private string ComposeTransmission(Vector3 feet, int band)
        {
            SlopCityDoor door = messageDoor;
            if (door) return "The trail passes through " + door.DisplayName + ". Aim at the door and use INTERACT to open it.\n" + sandbox.ObjectiveAction + ".";
            string place = district + ": " + landmark + ".";
            if (Mathf.Abs(sandbox.WaypointPosition.y - feet.y) > 2.5f)
                return place + "\nThe signal is " + (sandbox.WaypointPosition.y > feet.y ? "above" : "below") + " street level. "
                    + (levelEntrance ? "Follow the beam to " + levelEntrance.DisplayName + "; INTERACT to change levels."
                        : "Find the stairs or an access lift; the dispatch beam reconnects on that level.");
            if (band == 0 && route.ClearSight(view.transform.position, sandbox.WaypointPosition, true))
                return "Signal found — " + target.DisplayName + ".\nAim at it and INTERACT: " + sandbox.ObjectiveAction.ToLowerInvariant() + ".";
            if (route.Finished && !route.ReachedTarget)
                return place + "\nSignal masked by a structure. Look for an entrance or circle the block; the trail will reconnect.";
            return place + "\n" + (band <= 1 ? "Very close. Check the next corner; follow the last faint beam."
                : band == 2 ? "The signal is on this block. Follow the faint dispatch beam."
                : "Head " + Bearing(feet, sandbox.WaypointPosition) + ". Faint beams mark the passages.");
        }

        private string ComposeJobInstructions(Vector3 feet, int band)
        {
            string place = district + ": " + landmark + ".";
            string action = sandbox.ObjectiveAction + ".";
            if (Mathf.Abs(sandbox.WaypointPosition.y - feet.y) > 2.5f)
                return place + "\nThe job is " + (sandbox.WaypointPosition.y > feet.y ? "above" : "below") + " this level. "
                    + (levelEntrance ? "Use " + levelEntrance.DisplayName + " to change levels."
                        : "Find the stairs or an access lift.") + "\n" + action;
            return place + "\n" + (band <= 1 ? "" : "Head " + Bearing(feet, sandbox.WaypointPosition) + ". ") + action;
        }

        private SlopCityDoor NextDoor(Vector3 feet)
        {
            if (!route.ReachedTarget) return null;
            int start = NearestPoint(feet);
            for (int i = start; i < route.Points.Count && i < start + 5; i++)
            {
                SlopCityDoor door = route.NearbyDoor(feet, route.Points[i]);
                if (door) return door;
            }
            return null;
        }

        private int NearestPoint(Vector3 feet)
        {
            int closest = 0; float distance = float.PositiveInfinity;
            for (int i = 0; i < route.Points.Count; i++)
            {
                float d = (route.Points[i] - feet).sqrMagnitude;
                if (d < distance) { distance = d; closest = i; }
            }
            return closest;
        }

        private void DrawTrail(Vector3 feet)
        {
            if (!signalOwner) signalOwner = sandbox ? sandbox.MatrixHunt : null;
            if (!signalOwner) return;
            if (!route.ReachedTarget || route.Points.Count < 2) { HideTrail(); return; }
            if (Time.time < nextBeamAt) return;
            nextBeamAt = Time.time + .25f;
            // Borrow the same eight-segment pool as the optional contact search.
            // SignalBeams clips every route edge against physical obstructions.
            signalOwner.PresentJobRoute(route.Points, NearestPoint(feet));
        }

        public static int ProximityBand(float distance) => distance <= 3.4f ? 0 : distance <= 12f ? 1 : distance <= 35f ? 2 : 3;
        public static string Bearing(Vector3 from, Vector3 to)
        {
            float angle = Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
            string[] names = { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };
            return names[(Mathf.RoundToInt(angle / 45f) + 8) % 8];
        }

        private void HideTrail()
        {
            if (signalOwner) signalOwner.ClearJobGuidance();
            nextBeamAt = 0;
        }
        private void OnDisable() { HideTrail(); }
        private void OnDestroy() { HideTrail(); }
    }
}
