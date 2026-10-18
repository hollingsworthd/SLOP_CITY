using System;

namespace SlopCity
{
    /// <summary>Authored offline inspectors: existing residents, real operation evidence, no inference service.</summary>
    public sealed class SlopCityLocalAgent
    {
        public const int MaximumAgents = 3;
        private readonly int[] residents = { -1, -1, -1 };
        private readonly long[] inspectionStart = new long[MaximumAgents];
        private readonly float[] inspectionTime = new float[MaximumAgents];
        private readonly bool[] inspecting = new bool[MaximumAgents];
        private static readonly int[] Districts = { 4, 5, 7 };
        private static readonly string[] Names = { "Agent Smith / World", "Agent Smith / Signals", "Agent Smith / Works" };
        private static readonly string[] Prompts = { "SEE THE MATRIX / AGENT SMITH / WORLD", "SEE THE MATRIX / AGENT SMITH / SIGNALS", "SEE THE MATRIX / AGENT SMITH / WORKS" };
        private static readonly string[] Tasks = {
            "Inspect a notice or run diagnostics on a nearby device, then return for an evidence check.",
            "Try a physical screen's channel control, then return. The report checks the request, not its acceptance or playback.",
            "Install a component at a municipal junction, then return. Collect free supplies first if your kit is empty."
        };
        public static string Name(int slot) => Names[slot];
        public static string Prompt(int slot) => Prompts[slot];

        public void Select(SlopCityNpcSeed[] population, SlopCityCrowdRoute[] routes)
        {
            for (int slot = 0; slot < MaximumAgents; slot++)
            { residents[slot] = -1; inspecting[slot] = false; }
            if (population == null || routes == null) return;
            for (int slot = 0; slot < MaximumAgents; slot++)
                for (int index = 0; index < population.Length; index++)
                {
                    SlopCityNpcSeed person = population[index];
                    if (SlotFor(index) >= 0 || person.route < 0 || person.route >= routes.Length) continue;
                    SlopCityCrowdRoute route = routes[person.route];
                    if (route == null || route.districtIndex != Districts[slot] || route.nodes == null
                        || person.node < 0 || person.node >= route.nodes.Length || route.nodes[person.node] == null
                        || route.nodes[person.node].neighbors == null || route.nodes[person.node].neighbors.Length == 0) continue;
                    residents[slot] = index;
                    break;
                }
        }

        public int SlotFor(int resident)
        {
            if (resident < 0) return -1;
            for (int slot = 0; slot < MaximumAgents; slot++) if (residents[slot] == resident) return slot;
            return -1;
        }

        public string Inspect(int slot, SlopCityMunicipalLog log, SlopCityMunicipalState state,
            float x, float y, float z, float seconds, string snapshot)
        {
            if (slot < 0 || slot >= MaximumAgents) throw new ArgumentOutOfRangeException(nameof(slot));
            string evidence;
            SlopCityMunicipalOutcome outcome;
            if (!inspecting[slot])
            {
                inspectionStart[slot] = log.LatestSequence;
                inspectionTime[slot] = seconds;
                inspecting[slot] = true;
                outcome = SlopCityMunicipalOutcome.Attempted;
                evidence = "Optional inspection started. " + Tasks[slot];
            }
            else
            {
                bool matched = false;
                SlopCityMunicipalEntry observed = default;
                for (int offset = 0; log.TryGetNewest(offset, out SlopCityMunicipalEntry entry); offset++)
                {
                    if (entry.sequence <= inspectionStart[slot]) break;
                    if (entry.actor == "Player" && entry.seconds <= inspectionTime[slot] + 180f && Matches(slot, entry))
                    { observed = entry; matched = true; break; }
                }
                outcome = matched ? SlopCityMunicipalOutcome.Observed : SlopCityMunicipalOutcome.Incomplete;
                evidence = matched ? "Observed #" + observed.sequence + " / " + observed.action + " at " + observed.device
                    + (slot == 1 ? ". Request recorded; acceptance and playback not verified." : ". State transition recorded.")
                    : log.OldestSequence > inspectionStart[slot] + 1 ? "Evidence window rolled over; this inspection is incomplete."
                    : seconds - inspectionTime[slot] > 180f ? "Inspection expired without matching evidence."
                    : "No matching operation recorded yet. " + Tasks[slot];
                if (matched || seconds - inspectionTime[slot] > 180f || log.OldestSequence > inspectionStart[slot] + 1)
                    inspecting[slot] = false;
            }
            log.Record(Names[slot], "InspectTheMatrix", "Local simulation inspection", x, y, z, outcome,
                state, state, evidence, seconds);
            return Names[slot] + " / THE MATRIX\nAuthored local inspector; no model, API or automatic code repairs.\n\n"
                + snapshot + "\n\n" + evidence + "\nTask window: 180 seconds of play; paused time does not count.\n\n"
                + "Pause → LICENSE + SOURCE opens the bundled AGPL reader. Browsing repository source is optional and needs a connection.\n\n"
                + "CODE MAP / SlopCitySandbox.ExecuteAction → ExecuteActionCore performs work; SlopCityInteractable.RecordService holds device status. "
                + "SlopCityBuildingWorld streams floors/lifts; SlopCityCityMedia owns city playback. "
                + "These readings come from this running game; public repository files may describe a different revision. Public game source uses AGPL-3.0-or-later with the Unity linking exception.\n\n"
                + log.FormatRecent();
        }

        private static bool Matches(int slot, SlopCityMunicipalEntry entry)
        {
            if (slot == 0) return entry.outcome == SlopCityMunicipalOutcome.Completed
                && (entry.action == "ReadNotice" || entry.action == "Diagnose")
                && entry.after.serviceCount > entry.before.serviceCount;
            if (slot == 1) return entry.action == "Tune" && entry.outcome == SlopCityMunicipalOutcome.Requested;
            return entry.action == "Repair" && entry.outcome == SlopCityMunicipalOutcome.Completed
                && entry.after.parts == entry.before.parts - 1 && entry.after.serviceCount > entry.before.serviceCount;
        }
    }
}
