using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    public enum SlopCityInteractionKind
    {
        Inspect, Bench, Light, Bin, Collectible, Screen, Terminal, Vending,
        Car, GasPump, Building, Subway, Plant, Hydrant, Fountain, RareFind, Dining
    }

    /// <summary>Authored interactions share one interface; untagged scenery is still inspectable.</summary>
    public sealed class SlopCityInteractable : MonoBehaviour
    {
        [SerializeField] private SlopCityInteractionKind kind;
        [SerializeField] private string displayName;
        [SerializeField] private bool objectiveEligible = true;
        [SerializeField] private Transform destination;
        [SerializeField] private Vector3 localInteractionPoint;
        [SerializeField] private bool hasInteractionPoint;
        [SerializeField] private string rareId;
        [SerializeField] private string rareStory;
        private int serviceCount;
        private string serviceStatus;
        private float readyAt;
        private bool switchedOn = true;
        private Renderer pulseRenderer;
        private MaterialPropertyBlock pulseProperties;
        private MaterialPropertyBlock originalProperties;
        private float pulseUntil;

        public SlopCityInteractionKind Kind => kind;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name.Replace('_', ' ') : displayName;
        public bool ObjectiveEligible => objectiveEligible && isActiveAndEnabled && gameObject.activeInHierarchy;
        public Transform Destination => destination;
        public float CooldownRemaining => Mathf.Max(0f, readyAt - Time.time);
        public bool IsOn => switchedOn;
        public string RareId => rareId ?? "";
        public string RareStory => rareStory ?? "";
        public int ServiceCount => serviceCount;
        public string ServiceStatus => serviceStatus ?? "Not serviced this session";

        // Elapsed ages survive save/load without advancing while the game is closed.
        [Serializable]
        public struct WorkState
        {
            public bool initialized;
            public int condition;
            public bool clean, watered;
            public bool powerInitialized, powerOn;
            public int deliveredParcels, acceptedSamples, stockUnits;
            public float wearAgeSeconds, cleanAgeSeconds, waterAgeSeconds;
        }

        private const float WorkInterval = 600f;
        private const int CountLimit = 1000000;
        private WorkState workState;
        private float workClock;
        private bool workClockStarted;
        private bool visualBindingsReady;
        private readonly List<WorkRenderer> workRenderers = new List<WorkRenderer>();
        private readonly List<WorkLight> workLights = new List<WorkLight>();
        private MaterialPropertyBlock workProperties;
        private enum WorkVisual { Lamp, Surface, Foliage }
        private sealed class WorkRenderer
        {
            public Renderer renderer;
            public WorkVisual visual;
            public Color baseColor, color, emission;
            public bool hasBaseColor, hasColor, hasEmission;
        }
        private struct WorkLight { public Light light; public float intensity; }

        public int WorkCondition { get { RefreshWork(); return workState.condition; } }
        public bool IsClean { get { RefreshWork(); return workState.clean; } }
        public bool IsWatered { get { RefreshWork(); return workState.watered; } }
        public int SupplyStock { get { RefreshWork(); return workState.stockUnits; } }
        public string StateDescription
        {
            get
            {
                RefreshWork();
                string state = "condition " + workState.condition + "/100";
                if (kind == SlopCityInteractionKind.Light)
                    state += !switchedOn ? ", off" : workState.condition > 0 ? ", on" : ", power on, lamp failed";
                if (kind == SlopCityInteractionKind.Bench || kind == SlopCityInteractionKind.Bin
                    || kind == SlopCityInteractionKind.Inspect || kind == SlopCityInteractionKind.Building)
                    state += workState.clean ? ", clean" : ", needs cleaning";
                if (kind == SlopCityInteractionKind.Plant) state += workState.watered ? ", watered" : ", dry";
                if (workState.stockUnits > 0 || workState.deliveredParcels > 0 || workState.acceptedSamples > 0)
                    state += ", stock " + workState.stockUnits + ", parcels " + workState.deliveredParcels
                        + ", tested samples " + workState.acceptedSamples;
                return state;
            }
        }

        public WorkState CaptureWorkState()
        {
            RefreshWork();
            workState.powerInitialized = true;
            workState.powerOn = switchedOn;
            return workState;
        }

        public void RestoreWorkState(WorkState state)
        {
            RestorePulse();
            workState = state.initialized ? state : new WorkState { initialized = true, condition = 60 };
            workState.initialized = true;
            switchedOn = workState.powerInitialized ? workState.powerOn : true;
            workState.powerInitialized = true;
            workState.powerOn = switchedOn;
            workState.condition = Mathf.Clamp(workState.condition, 0, 100);
            workState.deliveredParcels = Mathf.Clamp(workState.deliveredParcels, 0, CountLimit);
            workState.acceptedSamples = Mathf.Clamp(workState.acceptedSamples, 0, CountLimit);
            workState.stockUnits = Mathf.Clamp(workState.stockUnits, 0, CountLimit);
            workState.wearAgeSeconds = ValidAge(workState.wearAgeSeconds);
            workState.cleanAgeSeconds = workState.clean ? ValidAge(workState.cleanAgeSeconds) : 0f;
            workState.waterAgeSeconds = workState.watered ? ValidAge(workState.waterAgeSeconds) : 0f;
            workClock = Time.time;
            workClockStarted = true;
            // Apply expired restored ages immediately, without charging time spent outside the game.
            AdvanceWork(0f);
            ApplyWorkVisuals();
        }

        public bool RepairDevice()
        {
            RefreshWork();
            if (workState.condition >= 100) return false;
            RestorePulse();
            workState.condition = 100;
            workState.wearAgeSeconds = 0f;
            ApplyWorkVisuals();
            return true;
        }

        public bool MaintainSurface()
        {
            RefreshWork();
            if (workState.clean) return false;
            RestorePulse();
            workState.clean = true;
            workState.cleanAgeSeconds = 0f;
            ApplyWorkVisuals();
            return true;
        }

        public bool WaterPlant()
        {
            RefreshWork();
            if (workState.watered) return false;
            RestorePulse();
            workState.watered = true;
            workState.waterAgeSeconds = 0f;
            ApplyWorkVisuals();
            return true;
        }

        public void ReceiveParcel()
        {
            RefreshWork();
            workState.deliveredParcels = Mathf.Min(CountLimit, workState.deliveredParcels + 1);
            workState.stockUnits = Mathf.Min(CountLimit, workState.stockUnits + 3);
        }

        public void ReceiveSample()
        {
            RefreshWork();
            workState.acceptedSamples = Mathf.Min(CountLimit, workState.acceptedSamples + 1);
            workState.stockUnits = Mathf.Min(CountLimit, workState.stockUnits + 1);
        }

        public bool SpendStock()
        {
            RefreshWork();
            if (workState.stockUnits <= 0) return false;
            workState.stockUnits--;
            return true;
        }

        private static float ValidAge(float age) => float.IsNaN(age) || float.IsInfinity(age)
            ? 0f : Mathf.Clamp(age, 0f, WorkInterval * 5f);

        private void RefreshWork()
        {
            if (!workState.initialized) { RestoreWorkState(default); return; }
            float now = Time.time;
            float elapsed = workClockStarted ? Mathf.Max(0f, now - workClock) : 0f;
            workClock = now;
            workClockStarted = true;
            if (AdvanceWork(elapsed)) { RestorePulse(); ApplyWorkVisuals(); }
        }

        private bool AdvanceWork(float elapsed)
        {
            int condition = workState.condition;
            bool clean = workState.clean, watered = workState.watered;
            workState.wearAgeSeconds = ValidAge(workState.wearAgeSeconds + elapsed);
            int periods = Mathf.FloorToInt(workState.wearAgeSeconds / WorkInterval);
            if (periods > 0)
            {
                workState.condition = Mathf.Max(0, workState.condition - 20 * periods);
                workState.wearAgeSeconds %= WorkInterval;
            }
            if (workState.clean)
            {
                workState.cleanAgeSeconds = ValidAge(workState.cleanAgeSeconds + elapsed);
                if (workState.cleanAgeSeconds >= WorkInterval) { workState.clean = false; workState.cleanAgeSeconds = 0f; }
            }
            if (workState.watered)
            {
                workState.waterAgeSeconds = ValidAge(workState.waterAgeSeconds + elapsed);
                if (workState.waterAgeSeconds >= WorkInterval) { workState.watered = false; workState.waterAgeSeconds = 0f; }
            }
            return condition != workState.condition || clean != workState.clean || watered != workState.watered;
        }

        public struct SessionState
        {
            public int serviceCount;
            public string serviceStatus;
            public float readyAt;
            public bool switchedOn;
            public WorkState workState;
        }

        public SessionState CaptureSessionState() => new SessionState
        {
            serviceCount = serviceCount, serviceStatus = serviceStatus,
            readyAt = readyAt, switchedOn = switchedOn, workState = CaptureWorkState()
        };

        public void RestoreSessionState(SessionState state)
        {
            serviceCount = state.serviceCount;
            serviceStatus = state.serviceStatus;
            readyAt = state.readyAt;
            WorkState restoredWork = state.workState;
            restoredWork.powerInitialized = true;
            restoredWork.powerOn = state.switchedOn;
            // Session snapshots predate persistent power fields; retain their explicit switch state.
            if (!restoredWork.initialized)
                restoredWork = new WorkState { initialized = true, condition = 60,
                    powerInitialized = true, powerOn = state.switchedOn };
            RestoreWorkState(restoredWork);
        }
        public Vector3 InteractionPoint => hasInteractionPoint
            ? transform.TransformPoint(localInteractionPoint) : transform.position + Vector3.up * .85f;

        public void Configure(SlopCityInteractionKind interactionKind, string label, bool eligible = true)
        {
            kind = interactionKind;
            displayName = label;
            objectiveEligible = eligible;
        }

        public void ConfigureInteractionPoint(Vector3 worldPoint)
        {
            localInteractionPoint = transform.InverseTransformPoint(worldPoint);
            hasInteractionPoint = true;
        }

        public void ConfigureDestination(Transform marker) { destination = marker; }
        public void MarkUsed(float cooldown) { readyAt = Time.time + Mathf.Max(0f, cooldown); }

        public void ConfigureRareFind(string id, string label, string story)
        {
            Configure(SlopCityInteractionKind.RareFind, label, false);
            rareId = id;
            rareStory = story;
        }

        public void RecordService(string status)
        {
            serviceCount++;
            serviceStatus = string.IsNullOrWhiteSpace(status) ? "Service complete" : status;
        }

        public bool ToggleLights()
        {
            RefreshWork();
            RestorePulse();
            switchedOn = !switchedOn;
            workState.powerInitialized = true;
            workState.powerOn = switchedOn;
            ApplyWorkVisuals();
            return switchedOn;
        }

        public void Pulse(Renderer preferred = null)
        {
            RefreshWork();
            RestorePulse();
            pulseRenderer = preferred ? preferred : GetComponentInChildren<Renderer>();
            if (!pulseRenderer || !pulseRenderer.sharedMaterial) return;
            if (pulseProperties == null) pulseProperties = new MaterialPropertyBlock();
            if (originalProperties == null) originalProperties = new MaterialPropertyBlock();
            pulseRenderer.GetPropertyBlock(originalProperties);
            pulseRenderer.GetPropertyBlock(pulseProperties);
            Material material = pulseRenderer.sharedMaterial;
            Color original = EffectiveColor(pulseProperties, material,
                material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color");
            Color highlight = Color.Lerp(original, new Color(.35f, 1f, .84f), .48f);
            pulseProperties.SetColor("_BaseColor", highlight);
            pulseProperties.SetColor("_Color", highlight);
            pulseRenderer.SetPropertyBlock(pulseProperties);
            pulseUntil = Time.time + .3f;
        }

        // Bind only authored work surfaces. Never tint a screen, a whole district, or shared material.
        private void BindWorkVisuals()
        {
            if (visualBindingsReady) return;
            visualBindingsReady = true;
            foreach (Light light in GetComponentsInChildren<Light>(true))
                if (light.GetComponentInParent<SlopCityInteractable>() == this)
                    workLights.Add(new WorkLight { light = light, intensity = light.intensity });
            if (kind == SlopCityInteractionKind.Light)
            {
                foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                    if (renderer.name == "Lamp_Lens") AddWorkRenderer(renderer, WorkVisual.Lamp);
            }
            else if (kind == SlopCityInteractionKind.Bench || kind == SlopCityInteractionKind.Bin)
            {
                foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                    if (renderer.name == "StreetBench_TimberSlats" || renderer.name == "Timber_SeatSlat"
                        || renderer.name == "Timber_BackSlat" || renderer.name == "StreetBin_SlattedMetal")
                        AddWorkRenderer(renderer, WorkVisual.Surface);
            }
            else if (kind == SlopCityInteractionKind.Inspect || kind == SlopCityInteractionKind.Building)
            {
                bool roofPanel = name.StartsWith("Roof_Maintenance_Panel_", StringComparison.Ordinal);
                foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.GetComponentInParent<SlopCityInteractable>() != this) continue;
                    string surface = renderer.name;
                    bool printedNotice = surface == "Pasted_Public_Notice" || surface == "Transit_Public_Notice"
                        || surface == "Perimeter_Notice" || surface == "Service_Notice"
                        || surface.StartsWith("Kanban_Japanese_", StringComparison.Ordinal);
                    bool serviceFace = roofPanel && (surface == "Roof_Service_Face" || surface == "Roof_Service_Latch");
                    if (printedNotice || serviceFace) AddWorkRenderer(renderer, WorkVisual.Surface);
                }
            }
            else if (kind == SlopCityInteractionKind.Plant)
            {
                foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                    if (renderer.name == "Tree_Canopy") AddWorkRenderer(renderer, WorkVisual.Foliage);
                // Authored trees place the trunk and its canopy as same-position siblings.
                if (name == "Tree_Trunk" && transform.parent)
                    foreach (Transform sibling in transform.parent)
                        if (sibling.name == "Tree_Canopy"
                            && (sibling.localPosition - transform.localPosition).sqrMagnitude < .0001f)
                            AddWorkRenderer(sibling.GetComponent<Renderer>(), WorkVisual.Foliage);
            }
        }

        private static Color EffectiveColor(MaterialPropertyBlock block, Material material, string property)
        {
            if (block.HasProperty(property)) return block.GetColor(property);
            return material.HasProperty(property) ? material.GetColor(property) : Color.white;
        }

        private void AddWorkRenderer(Renderer renderer, WorkVisual visual)
        {
            if (!renderer || !renderer.enabled || !renderer.sharedMaterial) return;
            foreach (WorkRenderer existing in workRenderers) if (existing.renderer == renderer) return;
            if (workProperties == null) workProperties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(workProperties);
            Material material = renderer.sharedMaterial;
            workRenderers.Add(new WorkRenderer {
                renderer = renderer, visual = visual,
                hasBaseColor = material.HasProperty("_BaseColor"), hasColor = material.HasProperty("_Color"),
                hasEmission = material.HasProperty("_EmissionColor"),
                baseColor = SlopCitySalvageItem.UndustedColor(renderer, EffectiveColor(workProperties, material, "_BaseColor")),
                color = SlopCitySalvageItem.UndustedColor(renderer, EffectiveColor(workProperties, material, "_Color")),
                emission = EffectiveColor(workProperties, material, "_EmissionColor")
            });
        }

        private void ApplyWorkVisuals()
        {
            BindWorkVisuals();
            float brightness = switchedOn ? workState.condition / 100f : 0f;
            foreach (WorkLight binding in workLights)
            {
                if (!binding.light) continue;
                binding.light.enabled = switchedOn && workState.condition > 0;
                binding.light.intensity = binding.intensity * brightness;
            }
            foreach (WorkRenderer binding in workRenderers)
            {
                if (!binding.renderer) continue;
                // Read the current block each time, preserving properties owned by other systems.
                binding.renderer.GetPropertyBlock(workProperties);
                Color tint = Color.white;
                if (binding.visual == WorkVisual.Lamp) tint = Color.white * Mathf.Lerp(.18f, 1f, brightness);
                else if (binding.visual == WorkVisual.Surface && !workState.clean && !binding.renderer.GetComponentInParent<SlopCitySalvageItem>()) tint = new Color(.62f, .56f, .46f, 1f);
                else if (binding.visual == WorkVisual.Foliage && !workState.watered) tint = new Color(.75f, .67f, .43f, 1f);
                tint.a = 1f;
                if (binding.hasBaseColor) workProperties.SetColor("_BaseColor", binding.baseColor * tint);
                if (binding.hasColor) workProperties.SetColor("_Color", binding.color * tint);
                if (binding.visual == WorkVisual.Lamp && binding.hasEmission)
                    workProperties.SetColor("_EmissionColor", binding.emission * brightness);
                SlopCitySalvageItem.ComposeFinish(binding.renderer,workProperties,(binding.hasColor?binding.color:binding.baseColor)*tint);
                binding.renderer.SetPropertyBlock(workProperties);
            }
        }

        private void Update() { if (pulseRenderer && Time.time >= pulseUntil) RestorePulse(); }
        private void OnDisable() { RestorePulse(); }
        public void CancelHighlight() => RestorePulse();
        private void RestorePulse()
        {
            if (pulseRenderer && originalProperties != null) { pulseRenderer.SetPropertyBlock(originalProperties); SlopCitySalvageItem.ReapplyFinish(pulseRenderer); }
            pulseRenderer = null;
        }
    }
}
