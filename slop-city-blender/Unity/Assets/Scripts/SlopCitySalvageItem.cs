using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>A persistent physical object, or an explicitly partitioned facade surface.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCitySalvageItem : MonoBehaviour
    {
        public const int CleaningStrokes = 4;
        [SerializeField] private string stableId, displayName, category;
        [SerializeField] private bool dustProne, pickable;
        [SerializeField] private int baseValue;
        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private int lastClean = -1;
        private bool bindingReady, protectionKnown, protectedContents;
        private SlopCityInteractable[] pulseOwners;
        private Color[] baseColors;
        private float[] baseSmoothness;
        public static event Action<SlopCitySalvageItem> Admitted;

        public string StableId => stableId ?? "";
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name.Replace('_', ' ') : displayName;
        public string Category => category ?? "object";
        public bool DustProne => dustProne;
        public bool Pickable { get { if (!protectionKnown) { protectedContents = HasProtectedContents(transform); protectionKnown = true; } return pickable && !protectedContents; } }
        public bool ValidatePickup() => Pickable && !HasProtectedContents(transform);
        public int BaseValue => Mathf.Clamp(baseValue, 1, 10000);
        public Bounds PlacementBounds
        {
            get
            {
                Bind(); Bounds bounds=new Bounds(Vector3.zero,Vector3.one*.1f); bool found=false;
                foreach (Renderer renderer in renderers)
                {
                    if (!renderer) continue;
                    Bounds local=renderer.localBounds; Matrix4x4 matrix=transform.worldToLocalMatrix*renderer.localToWorldMatrix;
                    for (int corner=0;corner<8;corner++)
                    {
                        Vector3 p=matrix.MultiplyPoint3x4(local.center+Vector3.Scale(local.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                        if (!found) { bounds=new Bounds(p,Vector3.zero);found=true; } else bounds.Encapsulate(p);
                    }
                }
                return bounds;
            }
        }
        public Bounds WorldBounds
        {
            get
            {
                Bind(); Bounds bounds = new Bounds(transform.position, Vector3.zero); bool found = false;
                foreach (Renderer renderer in renderers)
                    if (renderer && renderer.enabled && renderer.gameObject.activeInHierarchy)
                    { if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds); }
                return bounds;
            }
        }

        public bool TryRaycastVisibleBounds(Ray ray, float reach, out float distance)
        {
            distance = reach;
            if (reach <= .02f || !WorldBounds.IntersectRay(ray, out float broadDistance) || broadDistance > reach) return false;
            bool found = false;
            // Separate tops, basins and handles can sit ahead of their base collider.
            // Use each visible part rather than the empty gaps in a whole assembly.
            foreach (Renderer renderer in renderers)
            {
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                    || !renderer.bounds.IntersectRay(ray, out float hit) || hit < .02f || hit > distance) continue;
                distance = hit; found = true;
            }
            return found;
        }

        public void Configure(string id, string label, string type, bool dusty, bool canPickUp, int value)
        {
            stableId = id; displayName = label; category = type; dustProne = dusty;
            pickable = canPickUp; protectionKnown = false; baseValue = Mathf.Clamp(value, 1, 10000);
            Admitted?.Invoke(this);
        }

        private void OnEnable() { if (!string.IsNullOrEmpty(stableId)) Admitted?.Invoke(this); }

        private void Bind()
        {
            if (bindingReady) return;
            bindingReady = true;
            var list = new List<Renderer>();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                if (renderer && renderer.GetComponentInParent<SlopCitySalvageItem>() == this
                    && !IsProtectedArt(renderer.transform) && !renderer.GetComponent<TextMesh>()) list.Add(renderer);
            pulseOwners = GetComponentsInChildren<SlopCityInteractable>(true);
            renderers = list.ToArray(); baseColors = new Color[renderers.Length]; baseSmoothness = new float[renderers.Length];
            block = new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                Material material = renderers[i].sharedMaterial;
                baseColors[i] = material && material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                baseSmoothness[i] = material && material.HasProperty("_Glossiness") ? material.GetFloat("_Glossiness") : .3f;
            }
        }

        public void SetCleanProgress(int strokes)
        {
            if (!dustProne) return;
            strokes = Mathf.Clamp(strokes, 0, CleaningStrokes);
            if (lastClean == strokes) return;
            Bind();
            foreach (var owner in pulseOwners) if (owner) owner.CancelHighlight();
            var parent = GetComponentInParent<SlopCityInteractable>(); if (parent) parent.CancelHighlight();
            lastClean = strokes;
            for (int i=0;i<renderers.Length;i++) ReapplyRenderer(renderers[i]);
        }
        private void FinishBlock(int index, MaterialPropertyBlock properties)
        {
            float clean = Mathf.Max(0,lastClean) / (float)CleaningStrokes;
            Color dust = Color.Lerp(new Color(.44f,.40f,.33f,1f),Color.white,clean);
            Color color = baseColors[index] * dust;
            properties.SetColor("_Color",color);properties.SetColor("_BaseColor",color);
            properties.SetFloat("_Glossiness",Mathf.Lerp(.035f,Mathf.Max(.72f,baseSmoothness[index]),clean));
            properties.SetFloat("_Smoothness",Mathf.Lerp(.035f,.84f,clean));
        }
        private void ReapplyRenderer(Renderer renderer)
        {
            if (!renderer || !dustProne || GetComponent<SlopCitySalvageSurface>()) return;
            Bind(); int index=Array.IndexOf(renderers,renderer);if(index<0)return;
            renderer.GetPropertyBlock(block);FinishBlock(index,block);renderer.SetPropertyBlock(block);
        }
        public static void ReapplyFinish(Renderer renderer)
        { var owner=renderer?renderer.GetComponentInParent<SlopCitySalvageItem>():null;if(owner)owner.ReapplyRenderer(renderer); }
        public static void ComposeFinish(Renderer renderer, MaterialPropertyBlock properties, Color underlyingColor)
        {
            var owner=renderer?renderer.GetComponentInParent<SlopCitySalvageItem>():null;
            if(!owner||!owner.dustProne||owner.GetComponent<SlopCitySalvageSurface>())return;
            owner.Bind();int index=Array.IndexOf(owner.renderers,renderer);if(index<0)return;
            owner.baseColors[index]=underlyingColor;owner.FinishBlock(index,properties);
        }
        public static Color UndustedColor(Renderer renderer, Color fallback)
        {
            var owner=renderer?renderer.GetComponentInParent<SlopCitySalvageItem>():null;
            if(!owner||!owner.dustProne||owner.GetComponent<SlopCitySalvageSurface>())return fallback;
            owner.Bind();int index=Array.IndexOf(owner.renderers,renderer);return index<0?fallback:owner.baseColors[index];
        }

        public static string SourceIdentity(Transform target)
        {
            if (!target) return "";
            var path = new System.Text.StringBuilder();
            for (Transform part = target; part; part = part.parent)
            {
                int ordinal = 0;
                if (part.parent) for (int i = 0; i < part.GetSiblingIndex(); i++)
                    if (part.parent.GetChild(i).name == part.name) ordinal++;
                path.Insert(0, "/" + part.name + "#" + ordinal);
                if (part.name == "SlopCity_NineBlocks") break;
            }
            return path.ToString();
        }

        public static bool IsProtectedArt(Transform target)
        {
            for (Transform part = target; part; part = part.parent)
            {
                string label = part.name.ToLowerInvariant();
                if (part.GetComponent<SlopCityCityScreen>() || part.GetComponent<SlopCityMediaPlayer>()
                    || label.Contains("poster") || label.Contains("public_notice") || label.Contains("perimeter_notice")
                    || label.Contains("service_notice") || label.Contains("broadcast_standby_print")
                    || label.StartsWith("slopcitymediascreenanchor_") || label.Contains("billboard")) return true;
                if (part.name == "SlopCity_NineBlocks") break;
            }
            return false;
        }

        public static bool HasProtectedContents(Transform target)
        {
            if (!target || IsProtectedArt(target)) return true;
            foreach (Transform part in target.GetComponentsInChildren<Transform>(true))
            {
                string label = part.name.ToLowerInvariant();
                if (IsProtectedArt(part) || part.GetComponent<SlopCityElevator>() || part.GetComponent<SlopCityElevatorButton>()
                    || part.GetComponent<SlopCityMetroTrain>() || part.GetComponent<CharacterController>() || part.GetComponent<Camera>()
                    || label.Contains("floor") || label.Contains("ceiling") || label.Contains("sidewalk")
                    || label.Contains("ground") || label.Contains("running_rail") || label.StartsWith("track_")
                    || label.Contains("stair") || label.Contains("elevator") || label.Contains("lift_cabin")) return true;
            }
            return false;
        }
    }
}
