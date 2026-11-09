using System;
using UnityEngine;

namespace SlopCity
{
    /// <summary>Turns an approved physical CitySituation USE into a bounded lifecycle transition.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityForgeSituationLifecycle : MonoBehaviour
    {
        [SerializeField] private SlopCityCityForgeController controller;
        [SerializeField] private SlopCityCityForgeRuntime runtime;
        [SerializeField] private Transform traveler;
        [SerializeField] private bool offlineFallback = true;

        /// <summary>Hosted code submits this exact prepared clone, then applies only its authoritative response.</summary>
        public event Action<CityForgeCoord, CityForgeChunkPlan, CityForgeSituationState> AuthoritativeTransitionRequested;
        public event Action<CityForgeCoord, CityForgeChunkPlan, CityForgeSituationState> AuthoritativeMaterializationRequested;
        public event Action<CityForgeCoord, CityForgeSituationState> SituationAdvanced;
        public event Action<string> SituationRefused;

        public void Configure(SlopCityCityForgeController cityController, SlopCityCityForgeRuntime cityRuntime, Transform player, bool useOfflineFallback)
        {
            Unsubscribe();
            controller = cityController; runtime = cityRuntime; traveler = player; offlineFallback = useOfflineFallback;
            Subscribe();
        }

        public void SetOfflineFallback(bool value) { offlineFallback = value; }

        private void Awake()
        {
            if (!controller) controller = GetComponent<SlopCityCityForgeController>();
            if (!runtime) runtime = GetComponent<SlopCityCityForgeRuntime>();
            Subscribe();
        }

        private void OnDestroy() { Unsubscribe(); }

        private void Subscribe()
        {
            if (runtime != null)
            {
                runtime.SituationTransitionRequested -= HandleSituationUse;
                runtime.SituationTransitionRequested += HandleSituationUse;
                runtime.SituationOpportunityRequested -= HandleSituationOpportunity;
                runtime.SituationOpportunityRequested += HandleSituationOpportunity;
            }
        }

        private void Unsubscribe()
        {
            if (runtime != null) runtime.SituationTransitionRequested -= HandleSituationUse;
            if (runtime != null) runtime.SituationOpportunityRequested -= HandleSituationOpportunity;
        }

        private void HandleSituationUse(CityForgeCoord coordinate, CityForgeSituationState situation)
        {
            if (controller == null || situation == null) return;
            if (!controller.TryPrepareSituationTransition(coordinate, situation.id, out CityForgeChunkPlan proposal, out CityForgeSituationState next, out CityForgeValidation validation))
            {
                SituationRefused?.Invoke(FirstMessage(validation)); return;
            }
            if (AuthoritativeTransitionRequested != null)
            {
                AuthoritativeTransitionRequested.Invoke(coordinate, proposal.Clone(), next.Clone()); return;
            }
            if (!offlineFallback) { SituationRefused?.Invoke("This CitySituation needs an authorized city owner."); return; }
            if (!controller.TryAdvanceSituationOffline(coordinate, situation.id, out CityForgeSituationState advanced, out validation))
            {
                SituationRefused?.Invoke(FirstMessage(validation)); return;
            }
            if (runtime != null) runtime.RefreshCityForgeSituation(coordinate, advanced);
            SituationAdvanced?.Invoke(coordinate, advanced.Clone());
        }

        private void HandleSituationOpportunity(CityForgeCoord coordinate, CityForgeSituationOpportunity opportunity)
        {
            if (controller == null || opportunity == null) return;
            if (!controller.TryPrepareSituationMaterialization(coordinate, opportunity, out CityForgeChunkPlan proposal, out CityForgeSituationState created, out CityForgeValidation validation))
            {
                SituationRefused?.Invoke(FirstMessage(validation)); return;
            }
            if (AuthoritativeMaterializationRequested != null)
            {
                AuthoritativeMaterializationRequested.Invoke(coordinate, proposal.Clone(), created.Clone()); return;
            }
            if (!offlineFallback) { SituationRefused?.Invoke("This CitySituation needs an authorized city owner."); return; }
            if (!controller.TryMaterializeSituationOffline(coordinate, opportunity, out CityForgeSituationState accepted, out validation))
            {
                SituationRefused?.Invoke(FirstMessage(validation)); return;
            }
            if (runtime != null) runtime.RefreshCityForgeSituation(coordinate, accepted);
            SituationAdvanced?.Invoke(coordinate, accepted.Clone());
        }

        private static string FirstMessage(CityForgeValidation validation)
        {
            return validation != null && validation.Messages != null && validation.Messages.Count > 0 ? validation.Messages[0] : "That CitySituation cannot change now.";
        }

    }
}
