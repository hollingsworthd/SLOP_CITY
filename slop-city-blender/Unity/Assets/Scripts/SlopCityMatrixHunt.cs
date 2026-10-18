using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopCity
{
    /// <summary>Optional persistent contacts and meals alongside authoritative physical city jobs.</summary>
    public sealed class SlopCityMatrixHunt : MonoBehaviour
    {
        public enum SearchStage { Intercept, FindContact, Dine }
        private const string SaveKey = "SlopCity.MatrixSearch.Progress.v1";
        [Serializable] private sealed class Progress
        {
            public int target = -1, stage, contacts, meals, circuits;
            public int[] found = Array.Empty<int>();
        }
        private Progress progress = new Progress();
        private readonly HashSet<int> found = new HashSet<int>();
        private readonly SlopCityHintRoute route = new SlopCityHintRoute();
        private SlopCityFirstPerson player;
        private SlopCitySignalBeams beams;
        private readonly List<SlopCityInteractable> levelAccess = new List<SlopCityInteractable>();
        private float nextRoute, nextPresentation, fedUntil, signalUntil;
        private Vector3 routeOrigin, routeDestination;
        private bool saveAvailable = true, ready, showingJobRoute;
        private SlopCitySandbox instructionSandbox;
        private object instructionAssignment;
        private string clue = "Find a woman in red and use her contact for a brief signal.";
        public bool Ready => ready;
        public bool SignalVisible => ready && Time.time < signalUntil;
        public bool JobInstructionsVisible
        {
            get
            {
                SlopCitySandbox sandbox = player ? player.Sandbox : null;
                if (!ready || !sandbox || instructionAssignment == null
                    || !ReferenceEquals(instructionSandbox, sandbox)
                    || !ReferenceEquals(instructionAssignment, sandbox.CurrentJobAssignment))
                {
                    instructionSandbox = null; instructionAssignment = null;
                    return false;
                }
                return true;
            }
        }
        public SearchStage Stage => (SearchStage)progress.stage;
        public int TargetIndex => progress.target;
        public int ContactsFound => progress.contacts;
        public int MealsEaten => progress.meals;
        public int CircuitsCompleted => progress.circuits;
        public float MealSpeedMultiplier => Time.time < fedUntil ? 2f : 1f;
        public string ProgressText => progress.contacts + " CONTACTS FOUND  /  " + progress.meals + " STEAKS"
            + (Time.time < fedUntil ? "  /  WELL FED" : "");
        public string ObjectiveText => Stage == SearchStage.FindContact
            ? "OPTIONAL CONTACT\nFind a woman in red and use her contact for dispatch help."
            : "OPTIONAL CONTACT\nUse a woman in red for city dispatch help and a brief signal.";
        public string TransmissionText => clue;

        public void Configure(SlopCityFirstPerson owner)
        {
            if (ready) { player = owner; return; }
            player = owner;
            Load();
            ReleaseDinnerGate();
            beams = gameObject.AddComponent<SlopCitySignalBeams>();
            beams.Configure(owner.transform);
            foreach (SlopCityInteractable item in FindObjectsByType<SlopCityInteractable>(FindObjectsSortMode.None))
                if (item.Destination) levelAccess.Add(item);
            ready = true;
            EnsureTarget();
        }

        private void Load()
        {
            try
            {
                string data = PlayerPrefs.GetString(SaveKey, "");
                if (data.Length == 0) return;
                if (data.Length > 65536 || !data.TrimStart().StartsWith("{", StringComparison.Ordinal))
                    throw new FormatException("Invalid search save.");
                Progress saved = JsonUtility.FromJson<Progress>(data);
                if (saved == null || saved.stage < 0 || saved.stage > 2 || saved.contacts < 0 || saved.meals < 0
                    || saved.circuits < 0 || (saved.found != null && saved.found.Length > 5000))
                    throw new FormatException("Invalid search progress.");
                progress = saved;
                if (saved.found != null) foreach (int index in saved.found) if (index >= 0) found.Add(index);
            }
            catch (Exception error)
            {
                // Do not overwrite an unreadable save or disturb the older jobs/resources save.
                saveAvailable = false; progress = new Progress(); found.Clear();
                Debug.LogWarning("Search progress preserved; this session cannot save search changes: " + error.Message);
            }
        }

        private void ReleaseDinnerGate()
        {
            // Old saves paused the entire search at Dine after a contact. Keep
            // every discovery/counter and simply release that obsolete gate.
            if (Stage != SearchStage.Dine) return;
            progress.stage = (int)SearchStage.Intercept;
            progress.target = -1;
            nextRoute = nextPresentation = 0;
            Save();
        }

        private void Save()
        {
            if (!saveAvailable) return;
            try
            {
                progress.found = new int[found.Count]; found.CopyTo(progress.found);
                Array.Sort(progress.found);
                PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(progress)); PlayerPrefs.Save();
            }
            catch (Exception error)
            {
                saveAvailable = false;
                Debug.LogWarning("Search progress could not be saved: " + error.Message);
                if (player) player.ShowFeedback("Search progress is available this session, but could not be saved.");
            }
        }

        public void Tick()
        {
            if (!ready || !player) return;
            SlopCitySandbox sandbox = player.Sandbox;
            if (player.IsPaused || player.IsSeated || (sandbox && (sandbox.IsDriving || sandbox.IsResting || sandbox.IsTravelling)))
            {
                beams.Clear();
                showingJobRoute = false;
                clue = player.IsSeated ? "Stand up and step outside. The women in red move among the Smiths."
                    : "Your contact is remembered. Continue on foot to read the street transmissions.";
                nextRoute = 0; return;
            }
            if (!SignalVisible)
            {
                beams.Clear(); showingJobRoute = false;
                nextRoute = nextPresentation = 0; return;
            }

            if (sandbox && sandbox.HasWaypoint)
            {
                // RabbitTrail presents the checked job route through this same
                // renderer. Do not erase that guide every simulation tick.
                if (!showingJobRoute) beams.Clear();
                nextRoute = nextPresentation = 0;
                return;
            }
            if (showingJobRoute) { beams.Clear(); showingJobRoute = false; }
            EnsureTarget();
            if (!TryDestination(out Vector3 destination, out Transform destinationRoot)) { beams.Clear(); return; }
            Vector3 feet = player.transform.position;
            Vector3 actualDestination = destination;
            SlopCityInteractable access = null;
            if (Mathf.Abs(destination.y - feet.y) > 2.5f)
            {
                float best = float.PositiveInfinity;
                foreach (SlopCityInteractable entry in levelAccess)
                {
                    if (!entry || !entry.Destination || Mathf.Abs(entry.Destination.position.y - destination.y) > 3f
                        || Mathf.Abs(entry.InteractionPoint.y - feet.y) > 2.5f) continue;
                    float distance = (entry.Destination.position - destination).sqrMagnitude;
                    if (distance < best) { best = distance; access = entry; }
                }
                if (access) { destination = access.InteractionPoint; destinationRoot = access.transform; }
            }
            if ((Time.time >= nextRoute && route.Finished) || nextRoute == 0
                || (feet - routeOrigin).sqrMagnitude > 100f || (destination - routeDestination).sqrMagnitude > 16f)
            {
                routeOrigin = feet; routeDestination = destination;
                route.Begin(feet, destination, player.transform, destinationRoot);
                nextRoute = Time.time + 6f;
            }
            route.Step();
            if (Time.time < nextPresentation) return;
            nextPresentation = Time.time + .25f;
            if (route.ReachedTarget && route.Points.Count > 1)
            {
                int nearest = 0; float distance = float.PositiveInfinity;
                for (int i = 0; i < route.Points.Count - 1; i++)
                {
                    float current = (route.Points[i] - feet).sqrMagnitude;
                    if (current < distance) { distance = current; nearest = i; }
                }
                beams.SetPath(route.Points, nearest, true, new Color(.32f, 1f, .69f, .20f));
            }
            else beams.Clear();
            float metres = Vector3.Distance(feet, actualDestination);
            string bearing = SlopCityRabbitTrail.Bearing(feet, actualDestination);
            SlopCityCrowd crowd = SlopCityCrowd.Instance;
            SlopCityNpcSnapshot contact = crowd.Snapshot(progress.target);
            string district = DistrictName(contact.districtIndex);
            clue = Stage == SearchStage.Intercept
                ? "An optional contact is moving in " + district + ". Head " + bearing + "; look for a faint green beam."
                : "Contact / " + district + ", " + Mathf.CeilToInt(metres) + " m " + bearing
                    + ". Find her red dress among the black suits; INTERACT for city dispatch help.";
            if (route.Finished && !route.ReachedTarget)
                clue += " The route is obstructed. Open an entrance or circle the block.";
            if (Mathf.Abs(actualDestination.y - feet.y) > 2.5f)
                clue = "The carrier is " + (actualDestination.y < feet.y ? "below" : "above") + " this level. "
                    + (access ? "Follow the trace to " + access.DisplayName + "; INTERACT to change levels."
                        : "Find a station lift or stairs; the transmission reconnects on that level.");
        }

        private string DistrictName(int index)
        {
            if (player && player.Districts != null && index >= 0 && index < player.Districts.Length && player.Districts[index])
                return player.Districts[index].DisplayName;
            return "the city";
        }

        private void EnsureTarget()
        {
            ReleaseDinnerGate();
            SlopCityCrowd crowd = SlopCityCrowd.Instance;
            if (!crowd || crowd.RedContactCount == 0) return;
            if (progress.target >= 0 && progress.target < crowd.PopulationCount && crowd.IsRedContact(progress.target)
                && !found.Contains(progress.target)) return;
            ChooseContact();
        }

        private void ChooseContact()
        {
            SlopCityCrowd crowd = SlopCityCrowd.Instance;
            if (!crowd || !player) return;
            int best = -1; float distance = float.PositiveInfinity;
            for (int index = 0; index < crowd.PopulationCount; index++)
            {
                if (!crowd.IsRedContact(index) || found.Contains(index)) continue;
                float current = (crowd.Snapshot(index).position - player.transform.position).sqrMagnitude;
                if (current < distance) { distance = current; best = index; }
            }
            if (best < 0 && crowd.RedContactCount > 0)
            {
                found.Clear(); progress.circuits = Increment(progress.circuits);
                for (int index = 0; index < crowd.PopulationCount; index++)
                    if (crowd.IsRedContact(index))
                    {
                        float current = (crowd.Snapshot(index).position - player.transform.position).sqrMagnitude;
                        if (current < distance) { distance = current; best = index; }
                    }
            }
            progress.target = best; progress.stage = (int)SearchStage.Intercept;
            nextRoute = nextPresentation = 0; Save();
        }

        public bool TryDestination(out Vector3 point, out Transform root)
        {
            point = Vector3.zero; root = null;
            SlopCityCrowd crowd = SlopCityCrowd.Instance;
            if (!crowd || progress.target < 0 || progress.target >= crowd.PopulationCount) return false;
            point = crowd.Snapshot(progress.target).position; return true;
        }





        public string ContactPrompt(int index)
        {
            SlopCityCrowd crowd = SlopCityCrowd.Instance;
            if (!crowd || !crowd.IsRedContact(index)) return "";
            return found.Contains(index) ? "ASK CONTACT / CURRENT JOB" : "MEET CONTACT / CITY DISPATCH";
        }

        public bool TryContact(int index, out string message)
        {
            message = ""; SlopCityCrowd crowd = SlopCityCrowd.Instance;
            if (!crowd || !crowd.IsRedContact(index)) return false;
            bool discovered = !found.Contains(index);
            if (discovered)
            {
                found.Add(index); progress.contacts = Increment(progress.contacts);
                // A contact helps dispatch immediately; dinner is never a gate.
                ChooseContact();
                nextRoute = nextPresentation = 0;
                if (beams) beams.Clear();
                showingJobRoute = false;
                Save();
            }
            SlopCitySandbox sandbox = player ? player.Sandbox : null;
            string dispatch = sandbox ? sandbox.DispatchContact() : "Check a dispatch terminal for city work.";
            // Only physical contact binds instructions to this exact assignment.
            // Job steps can change; a later assignment, even of the same type, cannot inherit it.
            instructionSandbox = sandbox;
            instructionAssignment = sandbox ? sandbox.CurrentJobAssignment : null;
            // A physical contact is the only signal activation. Known contacts
            // can repeat it without adding discoveries or resetting progress.
            signalUntil = Time.time + 6f;
            nextRoute = nextPresentation = 0;
            message = (discovered ? "CONTACT FOUND / " : "KNOWN CONTACT / ") + dispatch;
            return true;
        }

        public string OnSteakEaten(SlopCitySteakTable table)
        {
            progress.meals = Increment(progress.meals); fedUntil = Time.time + 120f;
            ReleaseDinnerGate();
            EnsureTarget();
            Save();
            return "STEAK FINISHED / Well fed: double walking, running and driving speed for two minutes. Continue your city work whenever you're ready.";
        }

        /// <summary>The job and optional contact search share one bounded beam pool.</summary>
        public void PresentJobRoute(IReadOnlyList<Vector3> points, int startIndex)
        {
            SlopCitySandbox sandbox = player ? player.Sandbox : null;
            if (!SignalVisible || !isActiveAndEnabled || !beams || !sandbox || !sandbox.HasWaypoint || player.IsPaused || player.IsSeated
                || sandbox.IsDriving || sandbox.IsResting || sandbox.IsTravelling)
            { ClearJobGuidance(); return; }
            showingJobRoute = true;
            beams.SetPath(points, startIndex, true, new Color(.52f, .81f, 1f, .20f));
        }

        public void ClearJobGuidance()
        {
            if (showingJobRoute && beams) beams.Clear();
            showingJobRoute = false;
        }



        public void SuspendVisuals()
        {
            if (beams) beams.Clear();
            showingJobRoute = false;
            nextRoute = nextPresentation = 0;
        }

        private static int Increment(int value) => value < int.MaxValue ? value + 1 : int.MaxValue;
        private void OnDisable() { if (beams) beams.Clear(); }
    }
}
