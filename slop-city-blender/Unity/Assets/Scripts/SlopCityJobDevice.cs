using UnityEngine;

namespace SlopCity
{
    /// <summary>A named physical control, socket or material source in a bounded work site.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityJobDevice : MonoBehaviour
    {
        public SlopCityJobWorld World { get; private set; }
        public int Mechanic { get; private set; }
        public int Slot { get; private set; }
        public bool External { get; private set; }
        public void BindExternal(SlopCityJobWorld world, int mechanic, int slot)
        { World=world; Mechanic=mechanic; Slot=slot; External=true; }
        public void Configure(SlopCityJobWorld world, int mechanic, int slot, string label)
        {
            World = world; Mechanic = mechanic; Slot = slot;
            var item = GetComponent<SlopCityInteractable>(); if (!item) item = gameObject.AddComponent<SlopCityInteractable>();
            item.Configure(SlopCityInteractionKind.Inspect, label, true);
            item.ConfigureInteractionPoint(transform.position);
        }
    }
}
