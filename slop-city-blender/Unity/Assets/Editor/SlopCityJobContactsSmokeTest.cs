#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Saved-city optional contacts/meals and legacy dinner-gate migration beside an active job.</summary>
    public static class SlopCityJobContactsSmokeTest
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Legacy = "SlopCity.Sandbox.Progress.v1", Search = "SlopCity.MatrixSearch.Progress.v1";
        private static readonly string[] Keys = { Legacy, Legacy + ".recovery", Search, Search + ".recovery" };
        private static Receipt receipt;
        private static SlopCityFirstPerson player;
        private static SlopCitySandbox sandbox;
        private static SlopCityMatrixHunt hunt;
        private static Camera camera;
        private static CharacterController capsule;
        private static int reports;

        // Batch Edit Mode only, isolated company. No Start, Play Mode, network, decoder or gameplay frame loop.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode
                || Application.companyName != "SlopCityValidation-v0116"
                || !Application.persistentDataPath.Contains(Path.DirectorySeparatorChar + "SlopCityValidation-v0116" + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Contact compatibility requires isolated SlopCityValidation-v0116 batch Edit Mode.");
            receipt = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            var backups = Keys.ToDictionary(key => key, key => new Backup { existed = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetString(key, "") });
            UnityEngine.Random.State random = UnityEngine.Random.state;
            Application.logMessageReceived += CaptureLog;
            try
            {
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                foreach (SlopCityTikTokSync sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None)) sync.enabled = false;
                foreach (SlopCityCityMedia media in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None)) media.enabled = false;
                player = Object.FindFirstObjectByType<SlopCityFirstPerson>(); Check(player, "Actual saved city has its authored player."); player.enabled = false;
                capsule = player.GetComponent<CharacterController>();
                camera = new GameObject("Contact_fixture_camera_no_Start").AddComponent<Camera>(); camera.transform.SetParent(player.transform, false); camera.transform.localPosition = Vector3.up * 1.62f;
                Set(player, "viewCamera", camera); Set(player, "paused", false); Set(player, "seated", false);
                SlopCityCrowd crowd = Object.FindFirstObjectByType<SlopCityCrowd>(); Check(crowd && crowd.PopulationCount > 0, "Real authored residents exist.");
                typeof(SlopCityCrowd).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).SetValue(null, crowd);
                receipt.population = crowd.PopulationCount; receipt.redContacts = crowd.RedContactCount;
                int oldContact = Enumerable.Range(0, crowd.PopulationCount).First(crowd.IsRedContact);
                int smith = Enumerable.Range(0, crowd.PopulationCount).First(index => !crowd.IsRedContact(index));
                PlayerPrefs.SetString(Legacy, "{\"credits\":231,\"experience\":478,\"salvage\":5,\"completed\":12,\"water\":2,\"recycling\":2,\"fieldReadings\":4,\"rareFinds\":1,\"discovered\":[\"rare:contacts-legacy-fixture\"]}");
                PlayerPrefs.DeleteKey(Legacy + ".recovery");
                PlayerPrefs.SetString(Search, "{\"target\":" + oldContact + ",\"stage\":2,\"contacts\":3,\"meals\":2,\"circuits\":1,\"found\":[" + oldContact + "]}");
                PlayerPrefs.Save();
                sandbox = player.gameObject.AddComponent<SlopCitySandbox>(); Set(player, "sandbox", sandbox);
                sandbox.Initialize(player.transform); sandbox.MunicipalReportRequested += (_, __) => reports++;
                Check(sandbox.SelectJob("parcel-courier"), "Production dispatch selects a real saved-city courier job.");
                Check(AimAt(sandbox.ObjectiveTarget.InteractionPoint, sandbox.ObjectiveTarget), "A sampled physical center ray reaches the actual collecting desk.");
                Check(sandbox.Interact(camera) && sandbox.CargoLabel == "SEALED PARCEL" && Stage() == 1, "Production USE creates an active delivery and carried parcel before contact checks.");
                string job = sandbox.CurrentJobId, title = sandbox.CurrentJobTitle, savedLegacy = PlayerPrefs.GetString(Legacy);
                SlopCityInteractable recipient = sandbox.ObjectiveTarget;
                sandbox.StartMatrixSearch(player); hunt = sandbox.MatrixHunt;
                Check(hunt.Ready && hunt.Stage != SlopCityMatrixHunt.SearchStage.Dine && hunt.TargetIndex != oldContact,
                    "An old Dine save releases its obsolete dinner gate and chooses another real contact.");
                Check(hunt.ContactsFound == 3 && hunt.MealsEaten == 2 && hunt.CircuitsCompleted == 1,
                    "Dinner-gate migration preserves every existing contact, meal and circuit counter.");
                Check(hunt.ContactPrompt(oldContact).Contains("CURRENT JOB") && hunt.TryContact(oldContact, out string known) && known.Contains(title)
                    && hunt.ContactsFound == 3, "A known woman supplies current real dispatch details without counting another discovery.");
                int red = hunt.TargetIndex;
                Check(crowd.IsRedContact(red) && hunt.TryContact(red, out string found) && found.Contains(title)
                    && hunt.ContactsFound == 4 && hunt.Stage != SlopCityMatrixHunt.SearchStage.Dine,
                    "A newly discovered actual woman immediately provides real work help without requiring dinner.");
                Check(hunt.TargetIndex != red && crowd.IsRedContact(hunt.TargetIndex), "New contact advances the optional search to a distinct real resident.");
                Check(hunt.TryContact(red, out _) && hunt.ContactsFound == 4 && !hunt.TryContact(smith, out _),
                    "Repeated contact cannot duplicate a discovery and a Smith cannot become a red contact.");
                Check(sandbox.CurrentJobId == job && sandbox.ObjectiveTarget == recipient && Stage() == 1 && sandbox.CargoLabel == "SEALED PARCEL",
                    "Known/new/invalid contact interactions preserve the current work, target, stage and carried cargo.");
                Check(PlayerPrefs.GetString(Legacy) == savedLegacy, "Optional contact persistence leaves the active job save bytes unchanged.");
                SlopCitySteakTable.Ensure(player); var tables = SlopCitySteakTable.Tables.ToArray();
                Check(tables.Length == 2, "Two real furnished indoor restaurant tables remain available.");
                SlopCitySteakTable.Ensure(player); Check(SlopCitySteakTable.Tables.SequenceEqual(tables), "Repeated restaurant installation preserves the two physical table identities.");
                foreach (SlopCitySteakTable table in tables) Meal(table);
                Check(hunt.MealsEaten == 4 && hunt.ContactsFound == 4 && hunt.Stage != SlopCityMatrixHunt.SearchStage.Dine,
                    "Both optional three-bite meals count once without adding contacts or reinstating a dinner gate.");
                Check(hunt.MealSpeedMultiplier > 1 && sandbox.SpeedMultiplier >= hunt.MealSpeedMultiplier, "Completed steak supplies its real bounded movement recovery effect.");
                Check(sandbox.CurrentJobId == job && sandbox.ObjectiveTarget == recipient && Stage() == 1 && sandbox.CargoLabel == "SEALED PARCEL"
                    && sandbox.Credits == 231 && sandbox.Experience == 478 && sandbox.Salvage == 5 && sandbox.CompletedContracts == 12
                    && sandbox.WaterSupply == 2 && sandbox.Recycling == 2 && sandbox.FieldReadings == 4 && sandbox.RareFindCount == 1,
                    "Optional meals preserve all active work, cargo, legacy balances and resource quantities.");
                Check(PlayerPrefs.GetString(Legacy) == savedLegacy, "Steak persistence also leaves the active job save bytes unchanged.");
                var reloadRoot = new GameObject("Optional_contact_reload_probe");
                try
                {
                    var restored = reloadRoot.AddComponent<SlopCityMatrixHunt>(); restored.Configure(player);
                    Check(restored.TargetIndex == hunt.TargetIndex && restored.ContactsFound == 4 && restored.MealsEaten == 4
                        && restored.CircuitsCompleted == hunt.CircuitsCompleted && restored.Stage != SlopCityMatrixHunt.SearchStage.Dine,
                        "A fresh search component restores actual contact/meal progress and its ungated next target.");
                }
                finally { Object.DestroyImmediate(reloadRoot); }
                Check(reports == 0, "No contact, migration or meal opens or requires a municipal report.");
                Check(receipt.runtimeErrors.Count == 0, "No runtime error, exception or assertion was logged.");
            }
            catch (Exception error) { receipt.errors.Add(error.ToString()); }
            finally
            {
                foreach (SlopCitySandbox writer in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(writer);
                foreach (SlopCityMatrixHunt writer in Object.FindObjectsByType<SlopCityMatrixHunt>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(writer);
                foreach (SlopCitySteakTable table in SlopCitySteakTable.Tables.ToArray()) Object.DestroyImmediate(table);
                foreach (var pair in backups) { if (pair.Value.existed) PlayerPrefs.SetString(pair.Key, pair.Value.value); else PlayerPrefs.DeleteKey(pair.Key); }
                PlayerPrefs.Save(); receipt.savesRestored = backups.All(pair => PlayerPrefs.HasKey(pair.Key) == pair.Value.existed && (!pair.Value.existed || PlayerPrefs.GetString(pair.Key) == pair.Value.value));
                if (!receipt.savesRestored) receipt.errors.Add("Touched preference existence/bytes were not restored.");
                UnityEngine.Random.state = random; Application.logMessageReceived -= CaptureLog;
            }
            receipt.passed = receipt.errors.Count == 0 && receipt.runtimeErrors.Count == 0; receipt.finishedAtUtc = DateTime.UtcNow.ToString("O");
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports/jobs-optional-contacts-check.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(receipt, true));
            Debug.Log("SLOP_CITY_JOB_CONTACTS_" + (receipt.passed ? "PASSED" : "FAILED") + " " + receipt.checks + " checks");
            EditorApplication.Exit(receipt.passed ? 0 : 1);
        }

        private static void Meal(SlopCitySteakTable table)
        {
            var target = table.GetComponent<SlopCityInteractable>();
            Check(target && target.Kind == SlopCityInteractionKind.Dining && !target.ObjectiveEligible && table.GetComponent<Collider>(),
                table.VenueName + ": the meal remains on its real physical non-job indoor table.");
            Check(AimAt(table.InteractionPoint, target), table.VenueName + ": a sampled physical ray reaches actual table service.");
            Set(player, "paused", true); Check(!table.Use(out _, out bool paused) && !paused && !table.HasMeal, "Pause cannot create or eat a meal."); Set(player, "paused", false);
            Vector3 approach = player.transform.position; capsule.enabled = false; player.transform.position += Vector3.right * 10; capsule.enabled = true;
            Check(!table.Use(out _, out bool distant) && !distant && !table.HasMeal, "Out-of-reach service cannot create or eat a meal.");
            capsule.enabled = false; player.transform.position = approach; capsule.enabled = true; camera.transform.LookAt(table.InteractionPoint); ResetTarget();
            int meals = hunt.MealsEaten;
            Check(sandbox.Interact(camera) && table.BitesRemaining == 3 && ActivePortions(table) == 3, "Production USE serves three visible steak portions on the physical plate.");
            Check(!table.Use(out _, out bool spam) && !spam && table.BitesRemaining == 3, "The real action cooldown refuses an immediate extra bite.");
            for (int bite = 0; bite < 3; bite++)
            {
                // Explicit fixture clock injection: tests the production bite/ate contract, not wall-clock waiting.
                Set(table, "readyAt", Time.time); camera.transform.LookAt(table.InteractionPoint); ResetTarget();
                Check(sandbox.Interact(camera) && table.BitesRemaining == 2 - bite && ActivePortions(table) == 2 - bite,
                    "Production USE removes exactly one visible steak portion for bite " + (bite + 1) + ".");
                Check(hunt.MealsEaten == meals + (bite == 2 ? 1 : 0), "Only the last actual bite grants one meal effect.");
                receipt.bites++;
            }
            Check(!table.HasMeal && !table.Use(out _, out bool repeated) && !repeated, "An empty plate and the real completed-meal cooldown prevent duplicate food effects.");
            Check(table.transform.Find(SlopCitySteakTable.TableName + "/Steak_Plate").gameObject.activeSelf
                && !table.transform.Find(SlopCitySteakTable.TableName + "/Grilled_Greens").gameObject.activeSelf,
                "Finished food leaves its actual empty plate and no remaining garnish.");
            receipt.meals++;
        }

        private static bool AimAt(Vector3 point, SlopCityInteractable desired)
        {
            var overlaps = new Collider[128]; var hits = new RaycastHit[128];
            foreach (float radius in new[] { .8f, 1.2f, 1.95f, 2.6f, 2.9f }) for (int angle = 0; angle < 24; angle++)
            {
                Vector3 offset = Quaternion.Euler(0, angle * 15, 0) * Vector3.forward * radius;
                if (!Physics.Raycast(new Vector3(point.x + offset.x, point.y + .9f, point.z + offset.z), Vector3.down,
                    out RaycastHit ground, 4, ~0, QueryTriggerInteraction.Ignore) || ground.normal.y < .8f) continue;
                Vector3 feet = ground.point + Vector3.up * .025f;
                int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * .24f, feet + Vector3.up * 1.50f, .215f, overlaps, ~0, QueryTriggerInteraction.Ignore);
                if (count == overlaps.Length || overlaps.Take(count).Any(hit => hit && !hit.transform.IsChildOf(player.transform))) continue;
                Vector3 eye = feet + Vector3.up * 1.62f, aim = point - eye;
                if (aim.sqrMagnitude < .01f || aim.sqrMagnitude > 3.4f * 3.4f) continue;
                count = Physics.RaycastNonAlloc(eye, aim.normalized, hits, 3.4f, ~0, QueryTriggerInteraction.Ignore);
                if (count == hits.Length) continue;
                RaycastHit nearest = hits.Take(count).Where(hit => hit.transform && !hit.transform.IsChildOf(player.transform)).OrderBy(hit => hit.distance).FirstOrDefault();
                if (!nearest.transform || nearest.transform.GetComponentInParent<SlopCityInteractable>() != desired) continue;
                capsule.enabled = false; player.transform.position = feet; capsule.enabled = true;
                camera.transform.localPosition = Vector3.up * 1.62f; camera.transform.LookAt(point); Physics.SyncTransforms(); ResetTarget();
                object target = Invoke(sandbox, "FindTarget", camera);
                SlopCityInteractable actual = (SlopCityInteractable)target.GetType().GetField("interaction", Hidden).GetValue(target);
                if (actual == desired || (actual && actual.transform.IsChildOf(desired.transform))) return true;
            }
            return false;
        }

        private static int ActivePortions(SlopCitySteakTable table) => table.GetComponentsInChildren<Transform>(true).Count(item => item.name.StartsWith("Steak_Bite_", StringComparison.Ordinal) && item.gameObject.activeInHierarchy);
        private static int Stage() => (int)typeof(SlopCitySandbox).GetField("stepIndex", Hidden).GetValue(sandbox);
        private static void ResetTarget() { Set(sandbox, "nextTargetAt", 0f); typeof(SlopCitySandbox).GetField("webTargetFrame", Hidden)?.SetValue(sandbox, -1); }
        private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Hidden).SetValue(owner, value);
        private static object Invoke(object owner, string method, params object[] arguments) => owner.GetType().GetMethod(method, Hidden).Invoke(owner, arguments);
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); receipt.checks++; }
        private static void CaptureLog(string message, string trace, LogType kind) { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) receipt.runtimeErrors.Add(message + "\n" + trace); }
        private sealed class Backup { public bool existed; public string value; }
        [Serializable] private sealed class Receipt
        {
            public bool passed, savesRestored;
            public string startedAtUtc, finishedAtUtc, unityVersion;
            public int checks, population, redContacts, bites, meals;
            public string scope = "Isolated batch Edit Mode, actual saved city/population and furnished restaurant tables. Production public contact API validates old Dine migration, dispatch help and active-cargo preservation; contact acquisition rays/moving traversal are not exercised. One actual parcel collection and both complete order/three-bite meals use production Sandbox.Interact with bounded physically clear local camera/capsule positions. Action clocks are explicitly injected between bites; normal immediate-repeat and completed-meal cooldowns are checked. No Start, Play Mode, network, decoder, scene saves, full walking routes, rendered screenshots, audio or physical-device acceptance. All four progression/recovery keys restored.";
            public List<string> errors = new List<string>(), runtimeErrors = new List<string>();
        }
    }
}
#endif
