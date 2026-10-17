#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace SlopCity.Editor
{
    public static class SlopCityWorldUtilitySmokeTest
    {
        [Serializable] private sealed class Receipt
        {
            public bool passed;
            public string checkedAtUtc, unityVersion;
            public int utilities, rareFinds, municipalTerminals, jobDefinitions, maintenancePanels;
            public int generationSeed, generationRerolls, generatedJobTypes, objectiveCandidates, localRayReachable;
            public List<GeneratedJob> generatedJobs = new List<GeneratedJob>();
            public List<string> missingJobIds = new List<string>(), localRayUnverified = new List<string>();
            public string reachabilityScope;
            public List<string> checks = new List<string>(), errors = new List<string>();
        }
        [Serializable] private sealed class GeneratedJob
        {
            public string id;
            public List<GeneratedStep> steps = new List<GeneratedStep>();
        }
        [Serializable] private sealed class GeneratedStep
        {
            public string action, kind, target;
            public int reuseStep;
            public Vector3 interactionPoint, travelArrival;
        }
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Collider[] overlapBuffer = new Collider[64];
        private static readonly RaycastHit[] rayBuffer = new RaycastHit[64];
        public static void Run()
        {
            var result = new Receipt { checkedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            try
            {
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath);
                SlopCityFirstPerson player = UnityEngine.Object.FindFirstObjectByType<SlopCityFirstPerson>();
                SlopCityWorldUtilities.Ensure(player ? player.transform : null);
                result.utilities = SlopCityWorldUtilities.UtilityCount;
                result.rareFinds = SlopCityWorldUtilities.RareCount;
                result.municipalTerminals = SlopCityWorldUtilities.JunctionCount;
                result.jobDefinitions = SlopCityJobCatalog.All.Length;
                result.maintenancePanels = SlopCityWorldUtilities.MaintenancePanelCount;
                Require(result.maintenancePanels == 23, "Every roof lift landing has an accessible maintenance panel.", result);
                Require(result.utilities == 36, "All nine districts have free supplies, reusable parts, water refill and pressure-test stations.", result);
                Require(result.rareFinds == 4, "Exactly four explicitly authored rare transmissions exist.", result);
                Require(result.municipalTerminals == 9, "Nine existing municipal junctions are usable service terminals.", result);
                Require(SlopCityJobCatalog.ValidateDefinitions().Length == 0 && result.jobDefinitions == 40, "All forty typed job definitions pass resource, cargo and target-reuse validation.", result);
                var ids = new HashSet<string>();
                foreach (SlopCityInteractable item in UnityEngine.Object.FindObjectsByType<SlopCityInteractable>(FindObjectsSortMode.None))
                {
                    if (item.Kind != SlopCityInteractionKind.RareFind) continue;
                    Require(!item.ObjectiveEligible && ids.Add(item.RareId) && !string.IsNullOrWhiteSpace(item.RareStory),
                        "Rare transmission has unique stable ID and lore, and is excluded from routine work: " + item.RareId, result);
                }
                SlopCityWorldUtilities.Ensure(player ? player.transform : null);
                Require(SlopCityWorldUtilities.UtilityCount == result.utilities && SlopCityWorldUtilities.RareCount == result.rareFinds,
                    "Repeated initialization never duplicates utility props or rare finds.", result);
                CheckGeneratedJobs(player, result);
            }
            catch (Exception error) { result.errors.Add(error.ToString()); }
            result.passed = result.errors.Count == 0;
            Directory.CreateDirectory("Builds/Reports");
            File.WriteAllText("Builds/Reports/world-utilities-v0.9.0-check.json", JsonUtility.ToJson(result, true));
            Debug.Log("SLOP_CITY_WORLD_UTILITIES_" + (result.passed ? "PASSED" : "FAILED"));
            EditorApplication.Exit(result.passed ? 0 : 1);
        }
        private static void Require(bool valid, string text, Receipt result)
        { if (valid) result.checks.Add(text); else result.errors.Add(text); }

        private static void CheckGeneratedJobs(SlopCityFirstPerson player, Receipt result)
        {
            if (!player) throw new InvalidOperationException("Saved city has no first-person player.");
            // No Initialize, interaction, simulation or Save calls: existing local
            // progress is neither loaded nor modified by this generation probe.
            GameObject probeRoot = new GameObject("WorldJobGenerationProbe");
            SlopCitySandbox probe = probeRoot.AddComponent<SlopCitySandbox>();
            UnityEngine.Random.State previousRandom = UnityEngine.Random.state;
            try
            {
                FieldInfo userField = typeof(SlopCitySandbox).GetField("user", Members);
                if (userField == null) throw new MissingFieldException("SlopCitySandbox", "user");
                userField.SetValue(probe, player.transform);
                Invoke(probe, "RefreshSites");
                List<SlopCityInteractable> candidates = Read<List<SlopCityInteractable>>(probe, "sites");
                var definitions = new Dictionary<string, SlopCityJobDefinition>();
                foreach (SlopCityJobDefinition definition in SlopCityJobCatalog.All) definitions.Add(definition.id, definition);
                var seen = new HashSet<string>();
                result.generationSeed = 9040;
                UnityEngine.Random.InitState(result.generationSeed);
                for (int reroll = 0; reroll < 2000 && seen.Count < definitions.Count; reroll++)
                {
                    Invoke(probe, "NextContract", false);
                    result.generationRerolls++;
                    if (string.IsNullOrEmpty(probe.CurrentJobId))
                        throw new InvalidOperationException("Saved city failed to generate a job on reroll " + result.generationRerolls + ".");
                    if (!definitions.TryGetValue(probe.CurrentJobId, out SlopCityJobDefinition definition))
                        throw new InvalidOperationException("Generated unknown job ID: " + probe.CurrentJobId);
                    GeneratedJob generated = ValidateGenerated(probe, definition, candidates);
                    if (seen.Add(definition.id))
                    {
                        result.generatedJobs.Add(generated);
                        result.checks.Add("Real-city job generated with compatible actions and targets, valid reuse and arrival conditions: " + definition.id);
                    }
                }
                foreach (string id in definitions.Keys) if (!seen.Contains(id)) result.missingJobIds.Add(id);
                result.missingJobIds.Sort(StringComparer.Ordinal);
                result.generatedJobs.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
                result.generatedJobTypes = seen.Count;
                Require(seen.Count == 40 && result.missingJobIds.Count == 0,
                    "All forty job types actually generate against the saved real city within 2,000 seeded rerolls.", result);
                Require(!Read<bool>(probe, "initialized"),
                    "Generation probe remains uninitialized and never loads, executes or saves player progress.", result);
                CheckLocalReachability(candidates, player.transform, result);
            }
            finally
            {
                UnityEngine.Random.state = previousRandom;
                // OnApplicationQuit only saves initialized sandboxes; this probe
                // never becomes initialized and is removed before Editor exit.
                UnityEngine.Object.DestroyImmediate(probeRoot);
            }
        }

        private static GeneratedJob ValidateGenerated(SlopCitySandbox probe, SlopCityJobDefinition definition,
            List<SlopCityInteractable> candidates)
        {
            IList steps = Read<IList>(probe, "steps");
            if (steps.Count != definition.steps.Length)
                throw new InvalidOperationException(definition.id + ": generated step count differs from its definition.");
            if (Read<string>(probe, "contractTitle") != definition.title
                || Read<string>(probe, "currentJobOutcome") != definition.outcome
                || Read<int>(probe, "stepIndex") != 0)
                throw new InvalidOperationException(definition.id + ": generated title, outcome or initial step index differs from its definition.");
            var generated = new GeneratedJob { id = definition.id };
            var previousSites = new List<SlopCityInteractable>();
            for (int index = 0; index < steps.Count; index++)
            {
                SlopCityJobStepDefinition expected = definition.steps[index];
                SlopCityInteractable site = Read<SlopCityInteractable>(steps[index], "site");
                SlopCityJobAction action = Read<SlopCityJobAction>(steps[index], "action");
                string label = definition.id + " step " + (index + 1);
                if (action != expected.action || Read<string>(steps[index], "instruction") != expected.instruction)
                    throw new InvalidOperationException(label + ": actual operation/instruction differs from its definition.");
                if (!Compatible(site, expected) || !candidates.Contains(site))
                    throw new InvalidOperationException(label + ": target is absent from real-city candidates or does not support the requested operation.");
                if (site.Kind == SlopCityInteractionKind.RareFind || site.Kind == SlopCityInteractionKind.Car)
                    throw new InvalidOperationException(label + ": rare find or vehicle was substituted for an ordinary work target.");
                if (expected.reuseStep >= 0)
                {
                    if (expected.reuseStep >= index || site != previousSites[expected.reuseStep])
                        throw new InvalidOperationException(label + ": explicit target reuse did not preserve the referenced device.");
                }
                else if (previousSites.Contains(site))
                {
                    if (action == SlopCityJobAction.DeliverParcel || action == SlopCityJobAction.DeliverSample)
                        throw new InvalidOperationException(label + ": cargo handoff reused a prior target instead of a distinct recipient.");
                    foreach (SlopCityInteractable alternative in candidates)
                        if (!previousSites.Contains(alternative) && Compatible(alternative, expected)
                            && MatchesArrival(alternative, index, steps, expected))
                            throw new InvalidOperationException(label + ": independent target reused a previous device although a compatible fresh target exists.");
                }
                if (!MatchesArrival(site, index, steps, expected))
                    throw new InvalidOperationException(label + ": upper-level maintenance target is not near the preceding lift arrival at the correct height.");
                generated.steps.Add(new GeneratedStep
                {
                    action = action.ToString(), kind = site.Kind.ToString(), target = Hierarchy(site.transform),
                    reuseStep = expected.reuseStep, interactionPoint = site.InteractionPoint,
                    travelArrival = action == SlopCityJobAction.Travel ? site.Destination.position : Vector3.zero
                });
                previousSites.Add(site);
            }
            return generated;
        }

        private static bool Compatible(SlopCityInteractable site, SlopCityJobStepDefinition step)
        {
            if (!site || Array.IndexOf(step.kinds, site.Kind) < 0 || !SlopCityJobCatalog.Supports(site, step.action)) return false;
            if (step.action != SlopCityJobAction.Travel) return true;
            if (!site.Destination) return false;
            return site.Kind == SlopCityInteractionKind.Subway
                ? site.DisplayName.StartsWith("RIDE", StringComparison.OrdinalIgnoreCase)
                : site.Destination.position.y > site.InteractionPoint.y + 2f;
        }

        private static bool MatchesArrival(SlopCityInteractable site, int index, IList steps, SlopCityJobStepDefinition expected)
        {
            if (index == 0 || expected.action != SlopCityJobAction.MaintainSurface
                || Read<SlopCityJobAction>(steps[index - 1], "action") != SlopCityJobAction.Travel) return true;
            SlopCityInteractable travel = Read<SlopCityInteractable>(steps[index - 1], "site");
            if (!travel || !travel.Destination) return false;
            Vector3 arrival = travel.Destination.position;
            return Mathf.Abs(site.InteractionPoint.y - arrival.y) <= 2.6f
                && Vector2.Distance(new Vector2(site.InteractionPoint.x, site.InteractionPoint.z), new Vector2(arrival.x, arrival.z)) <= 12f;
        }

        private static void CheckLocalReachability(List<SlopCityInteractable> candidates, Transform player, Receipt result)
        {
            Physics.SyncTransforms();
            result.reachabilityScope = "Conservative local diagnostic: flat support, clear 1.72m/0.22m player capsule and nearest physical collider belonging to the target along a center ray within 3.4m. Up to 64 standing positions per candidate. Unverified means no sampled proof; it is not proof of a blocked route. Does not simulate global navigation, opening doors, jumping, travel or renderer-only targeting.";
            var checkedSites = new HashSet<SlopCityInteractable>();
            foreach (SlopCityInteractable site in candidates)
            {
                if (!site || !checkedSites.Add(site)) continue;
                result.objectiveCandidates++;
                if (HasLocalCenterRaySpot(site, player)) result.localRayReachable++;
                else result.localRayUnverified.Add(Hierarchy(site.transform) + " / " + site.Kind + " / interaction " + site.InteractionPoint);
            }
            result.localRayUnverified.Sort(StringComparer.Ordinal);
            result.checks.Add("Bounded physical center-ray reach diagnostic recorded for " + result.objectiveCandidates
                + " real-city objective candidates: " + result.localRayReachable + " locally proven, "
                + result.localRayUnverified.Count + " unverified (listed separately; job matching remains strict).");
        }

        private static bool HasLocalCenterRaySpot(SlopCityInteractable site, Transform player)
        {
            Vector3 point = site.InteractionPoint;
            float[] radii = { .65f, 1.15f, 1.75f, 2.4f };
            foreach (float radius in radii) for (int direction = 0; direction < 16; direction++)
            {
                Vector3 offset = Quaternion.Euler(0, direction * 22.5f, 0) * site.transform.forward * radius;
                Vector3 sample = new Vector3(point.x + offset.x, point.y + .85f, point.z + offset.z);
                if (!Physics.Raycast(sample, Vector3.down, out RaycastHit support, 4f, ~0, QueryTriggerInteraction.Ignore)
                    || support.normal.y < .8f) continue;
                Vector3 feet = support.point + Vector3.up * .04f;
                int overlaps = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * .22f, feet + Vector3.up * 1.50f,
                    .22f, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
                if (overlaps == overlapBuffer.Length) continue;
                bool blocked = false;
                for (int hit = 0; hit < overlaps; hit++)
                    if (overlapBuffer[hit] && !overlapBuffer[hit].transform.IsChildOf(player)) { blocked = true; break; }
                if (blocked) continue;
                Vector3 eye = feet + Vector3.up * 1.62f;
                Vector3 aim = point - eye;
                if (aim.sqrMagnitude < .01f || aim.sqrMagnitude > 3.4f * 3.4f) continue;
                int hitCount = Physics.RaycastNonAlloc(eye, aim.normalized, rayBuffer, 3.4f, ~0, QueryTriggerInteraction.Ignore);
                if (hitCount == rayBuffer.Length) continue;
                Transform nearest = null; float nearestDistance = 3.4f;
                for (int hit = 0; hit < hitCount; hit++)
                {
                    RaycastHit candidate = rayBuffer[hit];
                    if (!candidate.transform || candidate.transform.IsChildOf(player) || candidate.distance >= nearestDistance) continue;
                    nearest = candidate.transform; nearestDistance = candidate.distance;
                }
                if (nearest && nearest.GetComponentInParent<SlopCityInteractable>() == site) return true;
            }
            return false;
        }

        private static string Hierarchy(Transform item)
        {
            string path = item.name;
            for (Transform parent = item.parent; parent; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }

        private static T Read<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, Members);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            return (T)field.GetValue(target);
        }

        private static void Invoke(object target, string name, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(name, Members);
            if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
            method.Invoke(target, args);
        }
    }
}
#endif
