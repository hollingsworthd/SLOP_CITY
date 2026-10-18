using UnityEngine;

namespace SlopCity
{
    public enum SlopCityElevatorButtonKind { Call, Floor, PreviousPage, NextPage, Open, Close }

    /// <summary>A physical, aim-selected control. An unbound lift panel never becomes a teleport.</summary>
    public sealed class SlopCityElevatorButton : MonoBehaviour
    {
        private SlopCityElevator elevator;
        private SlopCityElevatorButtonKind action;
        private int floorIndex = -1;
        private SlopCityInteractable route;
        private int routeDestination = -1;
        private TextMesh label;

        public SlopCityElevator Elevator => elevator;
        public int FloorIndex => floorIndex;
        public SlopCityElevatorButtonKind Action => action;
        public string Prompt
        {
            get
            {
                if (!elevator) return "ELEVATOR / APPROACH THE BUILDING ENTRANCE";
                switch (action)
                {
                    case SlopCityElevatorButtonKind.Call: return "CALL ELEVATOR / " + elevator.FloorLabel(floorIndex);
                    case SlopCityElevatorButtonKind.Floor: return "GO TO " + elevator.FloorLabel(floorIndex);
                    case SlopCityElevatorButtonKind.PreviousPage: return "PREVIOUS FLOORS / " + elevator.PageLabel;
                    case SlopCityElevatorButtonKind.NextPage: return "NEXT FLOORS / " + elevator.PageLabel;
                    case SlopCityElevatorButtonKind.Open: return "OPEN ELEVATOR DOORS";
                    default: return "CLOSE ELEVATOR DOORS";
                }
            }
        }

        public void Configure(SlopCityElevator owner, SlopCityElevatorButtonKind kind, int index = -1)
        {
            elevator = owner; action = kind; floorIndex = index;
            var item = GetComponent<SlopCityInteractable>();
            if (!item) item = gameObject.AddComponent<SlopCityInteractable>();
            item.Configure(item.Destination ? item.Kind : SlopCityInteractionKind.Building,
                item.Destination ? item.DisplayName : name.Replace('_', ' '), false);
            item.ConfigureInteractionPoint(transform.position);
            UpdateLabel();
        }

        public void Bind(SlopCityElevator owner, int landingFloor) => Configure(owner, SlopCityElevatorButtonKind.Call, landingFloor);
        public void BindRoute(SlopCityInteractable origin, int destinationFloor) { route = origin; routeDestination = destinationFloor; }
        public void SetLabel(TextMesh value) { label = value; UpdateLabel(); }

        internal void SetFloor(int index)
        {
            floorIndex = index;
            gameObject.SetActive(elevator && index >= 0 && index < elevator.FloorCount);
            UpdateLabel();
        }

        private void UpdateLabel()
        {
            if (!label) return;
            if (action == SlopCityElevatorButtonKind.Floor)
                label.text = floorIndex < 0 ? "" : elevator && (floorIndex == 0 || floorIndex == elevator.FloorCount - 1)
                    ? elevator.FloorLabel(floorIndex) : floorIndex.ToString("00");
        }

        public string Activate(SlopCitySandbox sandbox)
        {
            if (!elevator) return "The elevator is loading. Approach the entrance and call again.";
            if (sandbox)
            {
                SlopCityInteractable candidate = route && sandbox.ObjectiveTarget == route ? route : sandbox.ObjectiveTarget;
                SlopCityElevatorButton routeControl = candidate ? candidate.GetComponent<SlopCityElevatorButton>() : null;
                if (routeControl && routeControl.elevator == elevator && routeControl.routeDestination >= 0)
                    sandbox.BeginElevatorTravel(candidate, elevator, routeControl.routeDestination);
            }
            switch (action)
            {
                case SlopCityElevatorButtonKind.Call:
                    return elevator.RequestFloor(floorIndex) ? "Elevator called to " + elevator.FloorLabel(floorIndex) + ". Board and choose a floor inside." : "That landing is unavailable.";
                case SlopCityElevatorButtonKind.Floor:
                    return elevator.RequestFloor(floorIndex) ? elevator.FloorLabel(floorIndex) + " selected. Stand clear of the doors." : "That floor is unavailable.";
                case SlopCityElevatorButtonKind.PreviousPage: elevator.ChangePage(-1); return "Floor buttons / " + elevator.PageLabel;
                case SlopCityElevatorButtonKind.NextPage: elevator.ChangePage(1); return "Floor buttons / " + elevator.PageLabel;
                case SlopCityElevatorButtonKind.Open: return elevator.OpenDoors() ? "Doors opening." : "Doors stay locked while the elevator travels.";
                default: return elevator.CloseDoors() ? "Doors closing. The doorway must remain clear." : "Doors stay locked while the elevator travels.";
            }
        }
    }
}
