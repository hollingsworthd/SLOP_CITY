using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Public/offline composition for City Forge. It is deliberately unaware of
    /// accounts, endpoints, or external bridge types. An external composition
    /// supplies the neutral marker below before this object owns local state.
    /// </summary>
    public sealed class SlopCityCityForgeOfflineBootstrap : MonoBehaviour
    {
        private SlopCityCityForgeController controller;
        private SlopCityCityForgeRuntime runtime;
        private SlopCityCityForgeSafeReload safeReload;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (ExternalCompositionPresent() || FindObjectOfType<SlopCityCityForgeOfflineBootstrap>()) return;
            GameObject root = new GameObject("SlopCityCityForgeOffline");
            root.AddComponent<SlopCityCityForgeOfflineBootstrap>();
        }

        private IEnumerator Start()
        {
            // Let scene-owned public and private bootstraps register first.
            yield return null;
            if (ExternalCompositionPresent()) { Destroy(gameObject); yield break; }
            SlopCityFirstPerson player = FindObjectOfType<SlopCityFirstPerson>();
            if (!player) { Destroy(gameObject); yield break; }

            runtime = gameObject.AddComponent<SlopCityCityForgeRuntime>();
            SlopCityCityForgeChunkCompiler compiler = gameObject.AddComponent<SlopCityCityForgeChunkCompiler>();
            controller = gameObject.AddComponent<SlopCityCityForgeController>();
            SlopCityCityForgeBuildMode buildMode = gameObject.AddComponent<SlopCityCityForgeBuildMode>();
            SlopCityCityForgeTravel travel = gameObject.AddComponent<SlopCityCityForgeTravel>();
            SlopCityCityForgeOfflineStreamFollower follower = gameObject.AddComponent<SlopCityCityForgeOfflineStreamFollower>();
            SlopCityCityForgeSituationLifecycle situations = gameObject.AddComponent<SlopCityCityForgeSituationLifecycle>();
            safeReload = gameObject.AddComponent<SlopCityCityForgeSafeReload>();

            controller.Configure(compiler, SlopCityCityForgeCatalog.Default);
            compiler.RegisterSink(runtime);
            Transform mediaListener = player.ViewCamera != null ? player.ViewCamera.transform : (Camera.main != null ? Camera.main.transform : player.transform);
            runtime.Configure(mediaListener, false);
            travel.Configure(controller, player.transform);
            follower.Configure(controller, player.transform);
            situations.Configure(controller, runtime, player.transform, true);
            buildMode.PlayerNeighbourhoodRequested += follower.RestoreAround;
            safeReload.Configure(controller, player);
            controller.FreshContextReloadRequested += safeReload.ReloadSafely;

            if (!controller.TryRestoreOffline(out string restoreError))
            {
                if (controller.HasOfflineWorld)
                {
                    Debug.LogWarning("City Forge kept its existing offline save unchanged. Export or recover this device copy before starting a new world: " + (restoreError ?? "unreadable save"), this);
                    yield break;
                }
                controller.InitializeOffline();
            }
            // Keep public startup compact: the player can cross physically or use
            // the visible Build City launcher; no mutable authored Old City scene
            // is initialized or replaced here.
            CityForgeCoord entry = new CityForgeCoord(2, 0);
            controller.StreamAt(entry);
            follower.SetKnownCenter(entry);
        }

        private void OnDestroy()
        {
            if (controller != null && safeReload != null) controller.FreshContextReloadRequested -= safeReload.ReloadSafely;
        }

        private static bool ExternalCompositionPresent()
        {
            SlopCityCityForgeCompositionMarker marker = FindObjectOfType<SlopCityCityForgeCompositionMarker>();
            return marker != null && marker.BlocksOfflineBootstrap;
        }
    }

    /// <summary>Neutral opt-out used by an external composition before public offline bootstrap runs.</summary>
    [DisallowMultipleComponent]
    public sealed class SlopCityCityForgeCompositionMarker : MonoBehaviour
    {
        [SerializeField] private bool blocksOfflineBootstrap = true;
        public bool BlocksOfflineBootstrap { get { return blocksOfflineBootstrap; } }
        public void Configure(bool blocks) { blocksOfflineBootstrap = blocks; }
    }

    /// <summary>
    /// Reusable safe reload boundary for accepted topology changes. It gates the
    /// walker, moves a player out of a replaced cell if necessary, then streams
    /// the new bounded neighborhood before restoring normal first-person motion.
    /// A hosted bridge may call this after it has verified an authoritative page.
    /// </summary>
    public sealed class SlopCityCityForgeSafeReload : MonoBehaviour
    {
        private SlopCityCityForgeController controller;
        private SlopCityFirstPerson player;
        private Coroutine running;
        public bool IsReloading { get; private set; }
        public event Action<bool> ReloadStateChanged;

        public void Configure(SlopCityCityForgeController cityController, SlopCityFirstPerson firstPerson)
        {
            controller = cityController; player = firstPerson;
        }

        public void ReloadSafely(IReadOnlyList<CityForgeCoord> changed)
        {
            CityForgeCoord center = player ? CoordinateFor(player.transform.position) : new CityForgeCoord(2, 0);
            ReloadSafely(changed, center);
        }

        /// <summary>Reloads only actual replaced topology while retaining the caller's active streamed center.</summary>
        public void ReloadSafely(IReadOnlyList<CityForgeCoord> changed, CityForgeCoord activeCenter)
        {
            if (!isActiveAndEnabled || controller == null) return;
            if (running != null) return;
            IsReloading = true; ReloadStateChanged?.Invoke(true);
            running = StartCoroutine(ReloadRoutine((changed ?? Array.Empty<CityForgeCoord>()).ToList(), activeCenter));
        }

        private IEnumerator ReloadRoutine(List<CityForgeCoord> changed, CityForgeCoord activeCenter)
        {
            if (!player)
            {
                if (controller.IsInitialized) controller.StreamAt(activeCenter);
                IsReloading = false; ReloadStateChanged?.Invoke(false); running = null; yield break;
            }
            bool wasEnabled = player.enabled;
            player.SetExternalMovement(true);
            if (wasEnabled) player.enabled = false;
            CharacterController capsule = player.GetComponent<CharacterController>();
            if (capsule) capsule.enabled = false;
            CityForgeCoord playerCenter = CoordinateFor(player.transform.position);
            if (changed.Contains(playerCenter))
            {
                // A deterministic public refuge at the editable east starter
                // entry keeps a walker off colliders about to be recompiled.
                activeCenter = new CityForgeCoord(2, 0);
                player.transform.position = new Vector3(148f, 2f, 36f);
            }
            if (capsule) capsule.enabled = true;
            yield return null;
            if (controller.IsInitialized) controller.StreamAt(activeCenter);
            yield return null;
            if (wasEnabled) player.enabled = true;
            player.SetExternalMovement(false);
            IsReloading = false; ReloadStateChanged?.Invoke(false); running = null;
        }

        private static CityForgeCoord CoordinateFor(Vector3 world)
        {
            return new CityForgeCoord(Mathf.FloorToInt(world.x / SlopCityCityForge.ChunkSizeMeters), Mathf.FloorToInt(world.z / SlopCityCityForge.ChunkSizeMeters));
        }
    }
}
