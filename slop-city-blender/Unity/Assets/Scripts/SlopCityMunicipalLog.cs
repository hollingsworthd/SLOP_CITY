using System;
using System.Text;

namespace SlopCity
{
    public enum SlopCityMunicipalOutcome { Attempted, Completed, Blocked, InProgress, Requested, Observed, Failed, Incomplete }

    public struct SlopCityMunicipalState
    {
        public int parts, water, recycling, readings, serviceCount;
        public bool hasPower, powerOn;
    }

    public struct SlopCityMunicipalEntry
    {
        public long sequence;
        public double seconds;
        public string actor, action, device, evidence;
        public float x, y, z;
        public SlopCityMunicipalOutcome outcome;
        public SlopCityMunicipalState before, after;
    }

    /// <summary>Bounded session evidence, never a second simulation or a player-save archive.</summary>
    public sealed class SlopCityMunicipalLog
    {
        public const int Capacity = 32;
        private readonly SlopCityMunicipalEntry[] entries = new SlopCityMunicipalEntry[Capacity];
        private int count, next;
        private long sequence;
        public int Count => count;
        public long LatestSequence => sequence;
        public long OldestSequence => count == 0 ? sequence + 1 : entries[(next - count + Capacity) % Capacity].sequence;

        public void Record(string actor, string action, string device, float x, float y, float z,
            SlopCityMunicipalOutcome outcome, SlopCityMunicipalState before, SlopCityMunicipalState after,
            string evidence, double seconds)
        {
            entries[next] = new SlopCityMunicipalEntry { sequence = ++sequence, seconds = seconds,
                actor = Bound(actor, 48), action = Bound(action, 48), device = Bound(device, 100),
                evidence = Bound(evidence, 220), x = x, y = y, z = z, outcome = outcome, before = before, after = after };
            next = (next + 1) % Capacity;
            count = Math.Min(count + 1, Capacity);
        }

        public bool TryGetNewest(int offset, out SlopCityMunicipalEntry entry)
        {
            entry = default;
            if (offset < 0 || offset >= count) return false;
            entry = entries[(next - 1 - offset + Capacity) % Capacity];
            return true;
        }

        public string FormatRecent(int maximumEntries = Capacity)
        {
            var text = new StringBuilder(2048);
            text.Append("MUNICIPAL LOG / SESSION EVIDENCE\n");
            text.Append("Retained entries: ").Append(count).Append('/').Append(Capacity).Append(" (oldest rolls off).\n");
            text.Append("Only recorded local operations. Requests do not prove playback or arrival.\n");
            text.Append("Device state and resources remain authoritative; this log awards nothing and is not saved.\n\n");
            int shown = Math.Min(count, Math.Max(0, Math.Min(maximumEntries, Capacity)));
            if (shown == 0) text.Append("No operations recorded this session. Inspect or use a nearby city device.\n");
            for (int index = 0; index < shown; index++)
            {
                TryGetNewest(index, out SlopCityMunicipalEntry entry);
                text.Append('#').Append(entry.sequence).Append("  ").Append(entry.outcome)
                    .Append(" / ").Append(entry.action).Append(" / ").Append(entry.actor).Append('\n');
                text.Append(entry.device).Append(" @ ").Append(entry.x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(", ").Append(entry.y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(", ").Append(entry.z.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(" / t+").Append(entry.seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append("s\n");
                text.Append(entry.evidence).Append('\n');
                text.Append("parts ").Append(entry.before.parts).Append("→").Append(entry.after.parts)
                    .Append("; water ").Append(entry.before.water).Append("→").Append(entry.after.water)
                    .Append("; recycling ").Append(entry.before.recycling).Append("→").Append(entry.after.recycling)
                    .Append("; readings ").Append(entry.before.readings).Append("→").Append(entry.after.readings)
                    .Append("; services ").Append(entry.before.serviceCount).Append("→").Append(entry.after.serviceCount)
                    .Append("; power ").Append(entry.before.hasPower ? entry.before.powerOn ? "on" : "off" : "n/a").Append("→")
                    .Append(entry.after.hasPower ? entry.after.powerOn ? "on" : "off" : "n/a").Append("\n\n");
            }
            return text.ToString();
        }

        private static string Bound(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = value.Replace('\r', ' ').Replace('\n', ' ');
            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }
    }
}
