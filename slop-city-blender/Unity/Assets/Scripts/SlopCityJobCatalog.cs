using System.Collections.Generic;
using A = SlopCity.SlopCityJobAction;
using K = SlopCity.SlopCityInteractionKind;

namespace SlopCity
{
    public enum SlopCityJobAction
    {
        CollectParcel, DeliverParcel, RecoverPart, Repair, CollectRecycling, Recycle,
        FillWater, WaterPlant, PressureTest, SwitchOn, SwitchOff, Tune, Diagnose,
        Upload, Rest, Supply, CollectSample, DeliverSample, ReadNotice, FileReport,
        Travel, MaintainSurface, UniqueWork
    }

    public sealed class SlopCityJobStepDefinition
    {
        public SlopCityJobAction action;
        public string instruction;
        public SlopCityInteractionKind[] kinds;
        // References an earlier step's actual target. Other steps prefer different targets.
        public int reuseStep = -1;
    }

    public sealed class SlopCityJobDefinition
    {
        public string id, title, outcome;
        public int mechanic = -1, recipeVersion;
        public SlopCityJobStepDefinition[] steps;
    }

    /// <summary>
    /// Useful work recipes, separate from target selection, inventory, saves and rare discoveries.
    /// Completing work records its outcome; these definitions do not award cash or XP.
    /// </summary>
    public static class SlopCityJobCatalog
    {
        public static readonly SlopCityJobDefinition[] All =
        {
            Unique(0, "parcel-courier", "Mailbox courier", "An addressed parcel is secured in the matching mailbox.", "Match the parcel address to its mailbox"),
            Unique(1, "transit-courier", "Glass on the rails", "The same replacement pane travelled by train and was installed.", "Carry a replacement window aboard the train"),
            Unique(2, "addressed-delivery", "Three addresses", "Three physical letters reached their individually addressed slots.", "Sort letters by their visible addresses"),
            Unique(3, "return-courier", "Core exchange", "A worn lamp core was exchanged and returned for recovery.", "Exchange a lamp core and return the removed part"),
            Unique(4, "service-crate", "Build a field kit", "Typed parts have become one usable field kit.", "Assemble the field kit from its components"),
            Unique(5, "diagnostic-dispatch", "Follow the leak", "The blocked utility branch now passes water.", "Trace the connected pressure line"),
            Unique(6, "spare-part", "Panel reclamation", "A removable wall panel became conserved repair materials.", "Remove a wall panel and separate its materials"),
            Unique(7, "diagnostic-repair", "Find the failed fuse", "The terminal has a correct working fuse.", "Inspect and replace the failed fuse"),
            Unique(8, "pressure-repair", "Valve and gasket", "The isolated line was resealed and reopened.", "Isolate the valve before changing its gasket"),
            Unique(9, "lamp-isolation", "Safe bulb replacement", "The lamp has a replacement bulb and restored power.", "Isolate power and change the lamp bulb"),
            Unique(10, "repair-verification", "Real load test", "The repaired terminal dispensed a usable stock unit.", "Repair the terminal and test its real output"),
            Unique(11, "ordered-lamp-service", "Clear the lens", "The dusty lamp lens is clear and its light is useful again.", "Polish the lamp lens with CLEAN"),
            Unique(12, "hardware-cleanup", "A place to rest", "The polished bench has been used for real recovery.", "Polish the dusty bench, then sit"),
            Unique(13, "pump-service-round", "Fuel for the road", "A car took measured fuel and completed a short driven leg.", "Bring a car to the pump and refuel it"),
            Unique(14, "recycling-handoff", "Material sorting", "Metal, glass and plastic reached their matching intakes.", "Sort each material into its own recovery intake"),
            Unique(15, "surface-cleanup", "Window polishing", "A dusty window has retained its polished finish.", "Polish the window with repeated CLEAN presses"),
            Unique(16, "two-bin-round", "Balance the collection", "Conserved material fits within both collection capacities.", "Balance the loads between the two bins"),
            Unique(17, "posted-cleanup", "Post a new voice", "A fresh poster occupies an empty frame; existing posters remain.", "Mount a new poster in the empty frame"),
            Unique(18, "district-recycling", "Salvage by car", "The loaded salvage was physically driven to recovery.", "Load salvage, drive, and unload at the recovery bay"),
            Unique(19, "garden-cleanup", "Free the roots", "Physical litter no longer obstructs the planting bed.", "Pick the litter out of the planter"),
            Unique(20, "three-plant-round", "Water by need", "Three plants received measured water without overfilling.", "Measure each plant's different watering need"),
            Unique(21, "irrigation-record", "Connect the garden", "The connected hose supplies the planting bed.", "Connect both hose ends and open the valve"),
            Unique(22, "water-quality-route", "Filter and flush", "The fountain filter is restored and its output runs clear.", "Replace the filter, then flush the fountain"),
            Unique(23, "garden-seating", "Move a bench", "The same portable bench is placed in a clear seating pad.", "Place the bench inside the seating pad"),
            Unique(24, "posted-garden-round", "Give a sapling roots", "The carried sapling is planted and watered.", "Transplant the sapling and establish its roots"),
            Unique(25, "signal-diagnostic", "Pictures that move", "An ordinary screen has shown actual advancing decoded motion.", "Retune an ordinary screen and verify moving pictures"),
            Unique(26, "requested-channel", "Requested programme", "The requested available channel is on an ordinary screen.", "Match the requested channel on the ordinary screen"),
            Unique(27, "lighting-cycle", "Light the circuit", "The three lamps are on in the required circuit sequence.", "Operate the physical lamps in circuit order"),
            Unique(28, "reception-audit", "Point the receiver", "The receiver faces its real source through a clear sightline.", "Align the receiver antenna with its source"),
            Unique(29, "remote-broadcast", "Bring up the relay", "The source and remote relay have been activated in order.", "Activate the source and reach its remote relay"),
            Unique(30, "pump-display-service", "Calibrate the meter", "A measured container has calibrated actual pump output.", "Compare the pump meter with the graduated container"),
            Unique(31, "sample-courier", "Field chemistry", "The identified sample received the matching field treatment.", "Test the sample and choose its matching reagent"),
            Unique(32, "pressure-sample", "Set the pressure", "The valve supplies water within the marked pressure range.", "Adjust the physical valve to the pressure band"),
            Unique(33, "requested-sample-transfer", "Compare the sources", "Two identified samples exposed the source that needed treatment.", "Compare both sources and treat the affected one"),
            Unique(34, "fountain-seating-check", "Lost beneath the bench", "The exact owner-tagged lost object has been returned.", "Find the lost object beneath the seating"),
            Unique(35, "utilities-inspection", "Share the power", "Pump and lighting both operate within the shared power budget.", "Balance power between the pump and lights"),
            Unique(36, "notice-surface-audit", "Read through the dust", "The dusty notice is readable and its physical request fulfilled.", "Clean the notice cover and follow its request"),
            Unique(37, "landing-maintenance", "Panel above the city", "A replacement panel was carried by lift and installed upstairs.", "Carry the replacement panel through the real lift"),
            Unique(38, "notice-round", "Neighbourhood barter", "A real neighbour exchanged specific owned goods.", "Offer the requested goods to a visible neighbour"),
            Unique(39, "garden-supply-round", "Build a planter guard", "Salvaged slats form a real guard around a watered planting.", "Fit salvaged slats to the planter guard"),
        };

        private static SlopCityJobDefinition Unique(int mechanic, string id, string title, string outcome, string instruction)
        {
            return new SlopCityJobDefinition { id = id, title = title, outcome = outcome, mechanic = mechanic, recipeVersion = 2,
                steps = new[] { Step(A.UniqueWork, instruction, K.Inspect) } };
        }

        public static bool Supports(SlopCityInteractable site, SlopCityJobAction action)
        {
            return site && site.isActiveAndEnabled && site.gameObject.activeInHierarchy
                && (action == A.UniqueWork ? site.GetComponent<SlopCityJobDevice>() != null : SupportsKind(site.Kind, action, site.Destination != null));
        }

        private static bool SupportsKind(K kind, A action, bool hasDestination)
        {
            if (action == A.UniqueWork) return kind == K.Inspect;
            if (action == A.Travel)
                return hasDestination && (kind == K.Building || kind == K.Subway);
            // A lift or train console has a physical transit role, never a generic work-desk role.
            if (hasDestination) return false;
            switch (action)
            {
                case A.CollectParcel: case A.DeliverParcel: case A.Upload:
                case A.DeliverSample: case A.FileReport: return kind == K.Terminal;
                case A.RecoverPart: return kind == K.Bin || kind == K.Collectible;
                case A.Repair: return kind == K.Terminal || kind == K.Light || kind == K.GasPump || kind == K.Hydrant;
                case A.CollectRecycling: return kind == K.Bin;
                case A.Recycle: return kind == K.Bin || kind == K.Terminal;
                case A.FillWater: return kind == K.Fountain || kind == K.Hydrant;
                case A.WaterPlant: return kind == K.Plant;
                case A.PressureTest: return kind == K.Hydrant || kind == K.GasPump;
                case A.SwitchOn: case A.SwitchOff: return kind == K.Light;
                case A.Tune: return kind == K.Screen || kind == K.GasPump || kind == K.Subway;
                case A.Diagnose: return kind == K.Terminal || kind == K.Light || kind == K.GasPump || kind == K.Hydrant || kind == K.Screen;
                case A.Rest: return kind == K.Bench || kind == K.Fountain;
                case A.Supply: return kind == K.Vending || kind == K.Fountain;
                case A.CollectSample: return kind == K.Plant || kind == K.Fountain || kind == K.Hydrant;
                case A.ReadNotice: return kind == K.Inspect || kind == K.Terminal || kind == K.Building;
                case A.MaintainSurface: return kind == K.Bench || kind == K.Bin || kind == K.Inspect || kind == K.Building;
                default: return false;
            }
        }

        /// <summary>Checks recipe compatibility and that prerequisites are supplied before consumption.</summary>
        public static string[] ValidateDefinitions()
        {
            var errors = new List<string>();
            var ids = new HashSet<string>();
            foreach (SlopCityJobDefinition job in All)
            {
                if (job == null || string.IsNullOrWhiteSpace(job.id))
                { errors.Add("Job has no ID."); continue; }
                if (!ids.Add(job.id)) errors.Add(job.id + ": duplicate ID.");
                if (string.IsNullOrWhiteSpace(job.title) || string.IsNullOrWhiteSpace(job.outcome))
                    errors.Add(job.id + ": missing title or outcome.");
                if (job.recipeVersion == 2)
                {
                    if (job.mechanic < 0 || job.mechanic >= 40 || job.steps == null || job.steps.Length != 1 || job.steps[0].action != A.UniqueWork)
                        errors.Add(job.id + ": invalid physical mechanic recipe.");
                    continue;
                }
                if (job.steps == null || job.steps.Length < 2 || job.steps.Length > 5)
                { errors.Add(job.id + ": expected two to five steps."); continue; }
                int parcels = 0, parts = 0, recycling = 0, water = 0, samples = 0, readings = 0;
                for (int i = 0; i < job.steps.Length; i++)
                {
                    SlopCityJobStepDefinition step = job.steps[i];
                    string label = job.id + " step " + (i + 1);
                    if (step == null || step.kinds == null || step.kinds.Length == 0)
                    { errors.Add(label + ": no target kinds."); continue; }
                    if (string.IsNullOrWhiteSpace(step.instruction)) errors.Add(label + ": no instruction.");
                    var kinds = new HashSet<K>();
                    foreach (K kind in step.kinds)
                    {
                        if (!kinds.Add(kind)) errors.Add(label + ": duplicate target kind.");
                        if (!SupportsKind(kind, step.action, step.action == A.Travel))
                            errors.Add(label + ": unsupported action/target kind.");
                    }
                    if (step.reuseStep < -1 || step.reuseStep >= i) errors.Add(label + ": invalid target reference.");
                    else if (step.reuseStep >= 0)
                    {
                        SlopCityJobStepDefinition prior = job.steps[step.reuseStep];
                        bool compatible = prior != null && prior.kinds != null;
                        if (compatible)
                        {
                            // Every possible referenced target must support the later action.
                            foreach (K kind in prior.kinds)
                                if (!kinds.Contains(kind) || !SupportsKind(kind, step.action, prior.action == A.Travel))
                                    compatible = false;
                        }
                        if (!compatible) errors.Add(label + ": incompatible reused target.");
                    }
                    switch (step.action)
                    {
                        case A.CollectParcel:
                            if (parcels != 0 || samples != 0) errors.Add(label + ": cargo already carried.");
                            parcels++; break;
                        case A.DeliverParcel: Consume(ref parcels, label, "parcel", errors); break;
                        case A.RecoverPart: parts++; break;
                        case A.Repair: Consume(ref parts, label, "replacement part", errors); break;
                        case A.CollectRecycling: recycling++; break;
                        case A.Recycle:
                            if (recycling <= 0) errors.Add(label + ": missing recycling load.");
                            parts += recycling; recycling = 0; break;
                        case A.FillWater: water = 3; break;
                        case A.WaterPlant: Consume(ref water, label, "water charge", errors); break;
                        case A.Supply:
                            water = 3;
                            if (parts < 2) parts = 2;
                            break;
                        case A.CollectSample:
                            if (samples != 0 || parcels != 0) errors.Add(label + ": cargo already carried.");
                            samples++; break;
                        case A.DeliverSample: Consume(ref samples, label, "sample", errors); break;
                        case A.Diagnose: case A.PressureTest: case A.ReadNotice:
                        case A.MaintainSurface: case A.Tune: readings++; break;
                        case A.Upload: case A.FileReport:
                            if (readings == 0) errors.Add(label + ": no reading to file.");
                            readings = 0; break;
                    }
                }
                // Supplies and optional readings stay in the field kit; paperwork never gates a job.
                if (parcels != 0 || recycling != 0 || samples != 0)
                    errors.Add(job.id + ": unfinished cargo.");
            }
            return errors.ToArray();
        }

        private static void Consume(ref int resource, string label, string name, List<string> errors)
        {
            if (resource <= 0) errors.Add(label + ": missing " + name + ".");
            else resource--;
        }

        private static SlopCityJobDefinition Job(string id, string title, string outcome, params SlopCityJobStepDefinition[] steps)
        { return new SlopCityJobDefinition { id = id, title = title, outcome = outcome, steps = steps }; }

        private static SlopCityJobStepDefinition Step(A action, string instruction, params K[] kinds)
        { return new SlopCityJobStepDefinition { action = action, instruction = instruction, kinds = kinds }; }

        private static SlopCityJobStepDefinition Reuse(A action, string instruction, int reuseStep, params K[] kinds)
        { return new SlopCityJobStepDefinition { action = action, instruction = instruction, kinds = kinds, reuseStep = reuseStep }; }
    }
}
