#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Bounded saved-scene Matrix loop and physical restaurant acceptance, with isolated persistence.</summary>
    [InitializeOnLoad]
    public static class SlopCityMatrixGameplayCheck
    {
        private const string Active = "SlopCity.MatrixGameplayCheck.Active", Started = Active + ".Started";
        private const string Legacy = "SlopCity.Sandbox.Progress.v1", HuntSave = "SlopCity.MatrixSearch.Progress.v1";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
        private static readonly string[] Keys = { Legacy, Legacy + ".recovery", HuntSave };
        private static Receipt receipt;
        private static SlopCityFirstPerson player;
        private static SlopCitySandbox sandbox;
        private static SlopCityMatrixHunt hunt;
        private static SlopCityCrowd crowd;
        private static Camera camera;
        private static double enteredAt;
        private static int lastFrame = -1;
        private static string Folder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports/matrix-gameplay");

        static SlopCityMatrixGameplayCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Active, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) enteredAt = EditorApplication.timeSinceStartup;
                if (state == PlayModeStateChange.EnteredEditMode) Finish(new Exception("Matrix check Play Mode ended before completion."));
            };
        }

        // Graphics-enabled isolated batch Editor, omit -quit. No scene/import writes.
        // -executeMethod SlopCity.Editor.SlopCityMatrixGameplayCheck.Run
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode
                || !Application.productName.Contains("SlopCityValidation")
                || !Application.persistentDataPath.Contains("SlopCityValidation")
                || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Use an isolated SlopCityValidation product/save identity in a graphics-enabled batch Editor outside Play Mode.");
#if !SLOP_CITY_WEB_INTERACTION_TEST
            throw new InvalidOperationException("Compile with SLOP_CITY_WEB_INTERACTION_TEST for actual browser targeting acceptance.");
#else
            SessionState.SetString(Started, DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(Active, true);
            foreach (string key in Keys)
            {
                SessionState.SetBool(Active + key, PlayerPrefs.HasKey(key));
                SessionState.SetString(Active + key + ".value", PlayerPrefs.GetString(key, ""));
            }
            PlayerPrefs.SetString(Legacy, "{\"credits\":321,\"experience\":654,\"salvage\":7,\"completed\":9,\"water\":3,\"recycling\":2,\"fieldReadings\":4,\"rareFinds\":1,\"discovered\":[\"matrix-fixture-legacy\"]}");
            PlayerPrefs.DeleteKey(Legacy + ".recovery"); PlayerPrefs.DeleteKey(HuntSave); PlayerPrefs.Save();
            receipt = NewReceipt();
            try
            {
                var scene = EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                DisableMedia();
                SlopCityCrowd authored = Object.FindFirstObjectByType<SlopCityCrowd>();
                Require(authored, "Actual saved-scene population exists.");
                SessionState.SetInt(Active + ".population", authored.PopulationCount);
                // Hold automatic native eager construction, then invoke the production
                // player Start once after constraining tower streaming, as entrance checks do.
                Object.FindFirstObjectByType<SlopCityFirstPerson>().enabled = false;
                SlopCityWebGeometryBuildProcessor.ProtectRuntimeBuildingGeometry(scene);
                SlopCityWebDecorativeMerge.Prepare(scene);
                Renderer[] renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
                new GameObject("Matrix_Check_Authored_Browser_Bounds").AddComponent<SlopCityBrowserGeometry>().Capture(renderers);
                SlopCityWebMeshBudget.Prepare(scene, true);
                SlopCityWebSingletonMaterialBuildProcessor.Prepare(scene);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    GameObject[] batch = root.GetComponentsInChildren<MeshRenderer>(true)
                        .Where(renderer => (GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.BatchingStatic) != 0)
                        .Select(renderer => renderer.gameObject).ToArray();
                    if (batch.Length > 0) StaticBatchingUtility.Combine(batch, root);
                }
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
#endif
        }

        private static Receipt NewReceipt() => new Receipt
        {
            startedAtUtc = SessionState.GetString(Started, DateTime.UtcNow.ToString("O")),
            unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
            isolatedDataPath = Application.persistentDataPath
        };

        private static void Tick()
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (receipt == null) receipt = NewReceipt();
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(receipt.startedAtUtc).ToUniversalTime()).TotalSeconds > 60)
                    throw new TimeoutException("Matrix fixture exceeded its 60-second wall-clock limit.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || lastFrame == Time.frameCount) return;
                lastFrame = Time.frameCount;
                if (Work.Count == 0)
                {
                    if (EditorApplication.timeSinceStartup - enteredAt < .3) return;
                    Work.Push(Exercise());
                }
                while (Work.Count > 0)
                {
                    IEnumerator current = Work.Peek();
                    if (!current.MoveNext()) { Work.Pop(); continue; }
                    if (current.Current is IEnumerator nested) { Work.Push(nested); continue; }
                    return;
                }
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static IEnumerator Exercise()
        {
            Application.runInBackground = true; AudioListener.volume = 0;
            player = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            Require(player && !player.enabled, "Saved-scene first-person initialization is held.");
            Set(player, "seated", true);
            GameObject city = GameObject.Find("SlopCity_NineBlocks");
            SlopCityBuildingWorld world = city.GetComponent<SlopCityBuildingWorld>() ?? city.AddComponent<SlopCityBuildingWorld>();
            Set(world, "constrainedBrowser", true);
            world = SlopCityBuildingWorld.Ensure(player); world.enabled = false;
            Invoke(player, "Start");
            player.SendMessage("OnApplicationFocus", true); player.SendMessage("OnApplicationPause", false);
            // Do not call PlayOrResume: its first-play action starts native video.
            Set(player, "started", true); Set(player, "paused", false); Set(player, "seated", false);
            Time.timeScale = 1; player.StopMedia(); DisableMedia();
            camera = player.ViewCamera; sandbox = player.Sandbox; hunt = sandbox.MatrixHunt;
            crowd = SlopCityCrowd.Instance;
            Require(camera && sandbox && hunt && hunt.Ready && crowd, "Production Start installs the hunt, crowd, sandbox and view camera.");
            Require(crowd.PopulationCount == SessionState.GetInt(Active + ".population", -1), "Wardrobe conversion preserves the saved-scene population count.");
            receipt.population = crowd.PopulationCount; receipt.redContacts = crowd.RedContactCount;
            Require(receipt.redContacts >= 2, "At least two distinct women in red are present.");
            int smith = -1, red = hunt.TargetIndex;
            for (int index = 0; index < crowd.PopulationCount; index++)
            {
                SlopCityNpcSnapshot person = crowd.Snapshot(index);
                if (crowd.IsRedContact(index)) Require(person.displayName == "Woman in Red", "Red contact " + index + " has the authored identity.");
                else { Require(person.displayName == "Agent Smith", "Resident " + index + " is named Agent Smith."); if (smith < 0) smith = index; }
            }
            Require(smith >= 0 && crowd.IsRedContact(red), "Both Smith and selected red contact identities resolve.");
            Require(!hunt.TryContact(smith, out _) && hunt.ContactsFound == 0, "Smith contact cannot advance the red-contact search.");
            Require(SlopCitySteakTable.Tables.Count == 2, "Exactly two indoor steak tables are installed.");
            var tables = SlopCitySteakTable.Tables.ToArray();
            SlopCitySteakTable.Ensure(player);
            Require(SlopCitySteakTable.Tables.SequenceEqual(tables), "Repeat Ensure preserves the same two table identities.");
            Require(sandbox.Credits == 321 && sandbox.Experience == 654 && sandbox.Salvage == 7 && sandbox.CompletedContracts == 9,
                "Legacy banked balances and completed jobs load unchanged.");
            string legacyBefore = PlayerPrefs.GetString(Legacy);

            BeamGeometry();
            Require(hunt.Stage == SlopCityMatrixHunt.SearchStage.FindContact, "Actual hunt interception advances to FindContact.");
            int contacts = hunt.ContactsFound;
            Require(AimNpc(red), "A physically clear nearby view of the current moving contact is available.");
            Require(sandbox.IsLookingAtNpc(camera), "Production sandbox targeting recognizes the instanced resident.");
            Require(sandbox.Prompt(camera).Contains("CONTACT"), "Production aim presents the red-contact action.");
            Require(sandbox.Interact(camera), "Production Sandbox.Interact handles the aimed contact.");
            Require(hunt.ContactsFound == contacts + 1 && hunt.Stage == SlopCityMatrixHunt.SearchStage.Dine,
                "Aimed red contact advances once to a real restaurant rendezvous.");
            Require(hunt.TryContact(red, out _) && hunt.ContactsFound == contacts + 1, "Repeated contact does not double-count discovery.");

            CapturePerson(red, "woman-in-red.png");
            CapturePerson(smith, "agent-smith.png");
            SharedWardrobe();
            for (int index = 0; index < tables.Length; index++)
                yield return Restaurant(tables[index], index == 0);

            Require(hunt.ContactsFound == contacts + 1 && hunt.MealsEaten == 1,
                "Only the production-dispatched completed meal advances hunt meal progress.");
            Require(hunt.Stage == SlopCityMatrixHunt.SearchStage.Intercept && hunt.TargetIndex != red && crowd.IsRedContact(hunt.TargetIndex),
                "Finishing dinner chooses another real red contact and returns to interception.");
            Require(hunt.TryContact(red, out _) && hunt.ContactsFound == contacts + 1, "A known contact cannot be rediscovered after the next search begins.");
            Require(hunt.MealSpeedMultiplier > 1 && sandbox.SpeedMultiplier >= hunt.MealSpeedMultiplier,
                "Completed dinner supplies the actual movement pace effect.");
            Require(sandbox.Credits == 321 && sandbox.Experience == 654 && sandbox.Salvage == 7 && sandbox.CompletedContracts == 9
                && sandbox.WaterSupply == 3 && sandbox.Recycling == 2 && sandbox.FieldReadings == 4 && sandbox.RareFindCount == 1,
                "Search and free meals preserve all legacy resource balances and job counts.");
            Require(PlayerPrefs.GetString(Legacy) == legacyBefore, "Search persistence leaves the legacy save bytes unchanged.");
            var reload = new GameObject("Matrix_Check_Reload");
            try
            {
                SlopCityMatrixHunt restored = reload.AddComponent<SlopCityMatrixHunt>(); restored.Configure(player);
                Require(restored.TargetIndex == hunt.TargetIndex && restored.Stage == hunt.Stage
                    && restored.ContactsFound == hunt.ContactsFound && restored.MealsEaten == hunt.MealsEaten,
                    "A fresh hunt component restores its independent contact/meal/target progress.");
                Require(PlayerPrefs.GetString(Legacy) == legacyBefore, "Hunt reload also preserves legacy save bytes.");
            }
            finally { Object.DestroyImmediate(reload); }
        }

        private static IEnumerator Restaurant(SlopCitySteakTable table, bool dispatch)
        {
            Require(table && table.GetComponentInParent<Transform>().parent.name.StartsWith("Venue_", StringComparison.Ordinal),
                table.VenueName + " is on an existing furnished indoor table.");
            Require(table.GetComponent<Collider>() && table.GetComponent<SlopCityInteractable>().Kind == SlopCityInteractionKind.Dining
                && !table.GetComponent<SlopCityInteractable>().ObjectiveEligible, table.VenueName + " retains a real table collider and non-job service target.");
            player.TeleportTo(table.ApproachPoint); camera.transform.LookAt(table.InteractionPoint); ResetTarget();
#if SLOP_CITY_WEB_INTERACTION_TEST
            Transform aimed = sandbox.BrowserTarget(camera, true);
            Require(aimed && aimed.GetComponentInParent<SlopCitySteakTable>() == table,
                table.VenueName + " browser target resolves its actual table service through prepared scenery.");
#endif
            Require(sandbox.Prompt(camera).Contains("Order grilled steak"), table.VenueName + " has its production aim/order prompt.");
            Set(player, "paused", true);
            Require(!table.Use(out _, out bool pausedAte) && !pausedAte && !table.HasMeal, "Pause refuses meal service without creating a plate.");
            Set(player, "paused", false);
            Vector3 approach = player.transform.position;
            // Deliberate out-of-range negative control; no claim of a walking route.
            player.GetComponent<CharacterController>().enabled = false;
            Vector3 displacement = table.transform.parent.forward * 8f;
            player.transform.position += displacement; Physics.SyncTransforms();
            Require(!table.Use(out _, out bool distantAte) && !distantAte && !table.HasMeal, "Out-of-reach direct service cannot create or complete a meal.");
            player.TeleportTo(approach); camera.transform.LookAt(table.InteractionPoint); ResetTarget();
            int meals = hunt.MealsEaten;
            bool ate = false;
            if (dispatch) Require(sandbox.Interact(camera), "Production USE orders the steak at " + table.VenueName + ".");
            else Require(table.Use(out _, out ate) && !ate, "Second restaurant's explicit service API orders without reporting eaten.");
            Require(table.HasMeal && table.BitesRemaining == 3 && ActivePortions(table) == 3
                && table.transform.Find(SlopCitySteakTable.TableName + "/Steak_Plate").gameObject.activeInHierarchy,
                table.VenueName + " physically displays a plate, grilled steak and all three portions.");
            Bounds plateBounds = table.transform.Find(SlopCitySteakTable.TableName + "/Steak_Plate").GetComponent<Renderer>().bounds;
            Bounds tableBounds = table.GetComponent<Collider>().bounds;
            Require(plateBounds.min.y > tableBounds.max.y && plateBounds.min.y - tableBounds.max.y < .02f
                && Vector2.Distance(new Vector2(plateBounds.center.x, plateBounds.center.z), new Vector2(tableBounds.center.x, tableBounds.center.z)) < .02f,
                "The prepared/static-batched scene's plate sits precisely on the actual physical table, rather than combined renderer bounds.");
            Require(!table.Use(out _, out bool spamAte) && !spamAte && table.BitesRemaining == 3, "Immediate repeated order cannot consume a bite.");
            if (dispatch) Capture("restaurant-served.png");
            for (int bite = 0; bite < 3; bite++)
            {
                yield return Delay(SlopCitySteakTable.ActionInterval + .04f);
                camera.transform.LookAt(table.InteractionPoint); ResetTarget(); ate = false;
                if (dispatch) Require(sandbox.Interact(camera), "Production USE takes steak bite " + (bite + 1) + ".");
                else Require(table.Use(out _, out ate) && ate == (bite == 2), "Explicit bite " + (bite + 1) + " reports ate only on completion.");
                Require(table.BitesRemaining == 2 - bite && ActivePortions(table) == 2 - bite, "Visible steak portions match consumed bite " + (bite + 1) + ".");
                if (dispatch)
                {
                    Require(hunt.MealsEaten == meals + (bite == 2 ? 1 : 0), "Hunt meal count advances only for the last real bite.");
                    if (bite == 0) Capture("restaurant-first-bite.png");
                }
            }
            Require(!table.HasMeal && !table.transform.Find(SlopCitySteakTable.TableName + "/Grilled_Greens").gameObject.activeSelf
                && table.transform.Find(SlopCitySteakTable.TableName + "/Steak_Plate").gameObject.activeSelf, "Completed dinner leaves an actual empty plate.");
            Require(!table.Use(out _, out bool repeatedAte) && !repeatedAte, "Completed-meal cooldown prevents immediate repeated food effects.");
            if (dispatch) Capture("restaurant-empty-plate.png");
        }

        private static int ActivePortions(SlopCitySteakTable table) => table.GetComponentsInChildren<Transform>(true)
            .Count(item => item.name.StartsWith("Steak_Bite_", StringComparison.Ordinal) && item.gameObject.activeInHierarchy);

        private static void BeamGeometry()
        {
            SlopCitySignalBeams beams = player.GetComponent<SlopCitySignalBeams>();
            Require(beams, "Production hunt owns its bounded beam component.");
            Vector3 origin = new Vector3(5000, 0, 5000);
            // Scoped synthetic empty-space rays isolate beam geometry from city navigation.
            // Contact and table acceptance below still use actual saved-scene residents/art.
            player.GetComponent<CharacterController>().enabled = false;
            player.transform.position = origin; camera.transform.position = origin + new Vector3(0, 1, -.5f);
            camera.transform.rotation = Quaternion.identity; Physics.SyncTransforms();
            var path = new[] { origin, origin + Vector3.forward * 3f };
            beams.SetPath(path, 0, true, Color.green);
            Require(beams.TryAim(camera, 3.4f, out _), "A nearby center-aimed visible physical beam can be intercepted.");
            camera.transform.rotation = Quaternion.Euler(0, 90, 0);
            Require(!beams.TryAim(camera, 3.4f, out _), "Looking away does not acquire a beam.");
            camera.transform.rotation = Quaternion.identity; camera.transform.position = origin + new Vector3(0, 1, -5f);
            Require(!beams.TryAim(camera, 100f, out _), "Caller-supplied excessive reach cannot bypass the beam's 3.4 metre cap.");
            camera.transform.position = origin + new Vector3(0, 1, -.5f);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.name = "Matrix_Check_Physical_Beam_Blocker";
                wall.transform.position = origin + new Vector3(0, 1, -.25f); wall.transform.localScale = new Vector3(1, 2, .1f); Physics.SyncTransforms();
                Require(!beams.TryAim(camera, 3.4f, out _), "A solid wall between camera and beam blocks acquisition.");
                wall.transform.position = origin + new Vector3(0, 1, 1.5f); Physics.SyncTransforms();
                beams.SetPath(path, 0, true, Color.green);
                var enabled = beams.GetComponentsInChildren<LineRenderer>().Where(line => line.enabled && line.name.StartsWith("Signal beam ")).ToArray();
                Require(enabled.Length == 1 && enabled[0].GetPosition(1).z < wall.GetComponent<Collider>().bounds.min.z,
                    "A wall along the path clips the physical beam before the solid surface.");
                Require(beams.GetComponentsInChildren<LineRenderer>().All(line => !line.GetComponent<Collider>()),
                    "Signal visuals introduce no physical blockers; the existing owner CharacterController remains separate.");
            }
            finally { Object.DestroyImmediate(wall); Physics.SyncTransforms(); }
            beams.SetPath(path, 0, true, Color.green);
            Require(hunt.TryIntercept(camera, out _) && hunt.Stage == SlopCityMatrixHunt.SearchStage.FindContact,
                "The real hunt interception method accepts the scoped clear beam ray.");
            beams.Clear();
            Require(!beams.TryAim(camera, 3.4f, out _), "Cleared beams cannot be intercepted.");
        }

        private static bool AimNpc(int index)
        {
            SlopCityNpcSnapshot person = crowd.Snapshot(index);
            for (int side = 0; side < 12; side++)
            {
                Vector3 offset = Quaternion.Euler(0, person.heading + side * 30, 0) * Vector3.forward * 1.7f;
                Vector3 approach = person.position + offset;
                Collider[] overlaps = Physics.OverlapCapsule(approach + Vector3.up * .4f, approach + Vector3.up * 1.5f, .3f, ~0, QueryTriggerInteraction.Ignore);
                if (overlaps.Any(item => item && !item.transform.IsChildOf(player.transform))) continue;
                player.TeleportTo(approach); camera.transform.LookAt(person.position + Vector3.up * person.height * .6f); ResetTarget();
                if ((player.transform.position - approach).sqrMagnitude > .04f) continue;
                Ray ray = camera.ViewportPointToRay(new Vector3(.5f, .5f));
                if (crowd.TryFindTarget(ray, 3.4f, out int actual, out _) && actual == index && sandbox.IsLookingAtNpc(camera)) return true;
            }
            return false;
        }

        private static void CapturePerson(int index, string file)
        {
            // Physics alone misses decorative columns. Preserve per-renderer authored
            // bounds from before static batching, then conservatively reject viewpoints
            // whose full-body sight lines intersect any opaque visual bounds. A hollow
            // mesh may reject an otherwise usable view; another actual street resident
            // is preferable to claiming that a center collider ray proves a clear image.
            var authored = new Dictionary<Renderer, Bounds>();
            foreach (SlopCityBrowserGeometry source in Object.FindObjectsByType<SlopCityBrowserGeometry>(FindObjectsSortMode.None))
                source.CopyTo(authored);
            var occluders = new List<PortraitOccluder>();
            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.GetComponent<TextMesh>()
                    || renderer.transform.IsChildOf(player.transform)) continue;
                Material[] materials = renderer.sharedMaterials;
                if (materials.Length == 0 || materials.All(material => !material || material.renderQueue >= 3000)) continue;
                Bounds bounds;
                if (!authored.TryGetValue(renderer, out bounds))
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (!filter || !filter.sharedMesh || renderer.isPartOfStaticBatch) continue;
                    bounds = filter.sharedMesh.bounds;
                }
                Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
                Bounds world = new Bounds(matrix.MultiplyPoint3x4(bounds.center), Vector3.zero);
                for (int corner = 0; corner < 8; corner++)
                    world.Encapsulate(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
                occluders.Add(new PortraitOccluder { local = bounds, world = world, inverse = renderer.transform.worldToLocalMatrix });
            }
            bool red = crowd.IsRedContact(index);
            Vector3 preferred = crowd.Snapshot(index).position;
            int[] subjects = Enumerable.Range(0, crowd.PopulationCount).Where(candidate => crowd.IsRedContact(candidate) == red)
                .OrderBy(candidate => candidate == index ? -1f : Mathf.Abs(crowd.Snapshot(candidate).position.y) * 1000f
                    + (crowd.Snapshot(candidate).position - preferred).sqrMagnitude).Take(12).ToArray();
            foreach (int candidate in subjects)
            {
                SlopCityNpcSnapshot person = crowd.Snapshot(candidate);
                PortraitOccluder[] nearby = occluders.Where(item => item.world.SqrDistance(person.position + Vector3.up) < 16f).ToArray();
                for (int step = 0; step < 24; step++)
                {
                    float angle = step == 0 ? 0 : ((step + 1) / 2) * 15f * (step % 2 == 0 ? -1 : 1);
                    Vector3 front = Quaternion.Euler(0, person.heading + angle, 0) * Vector3.forward;
                    Vector3 eye = person.position + front * 2.3f + Vector3.up * 1.05f;
                    Vector3 subject = person.position + Vector3.up * person.height * .54f;
                    if (Physics.CheckSphere(eye, .12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    Vector3 side = Vector3.Cross(Vector3.up, front).normalized;
                    bool blocked = false;
                    // Include face, shoulders, waist/skirt edges and both lower legs.
                    for (int sample = 0; sample < 7 && !blocked; sample++)
                    {
                        float height = sample == 0 ? .91f : sample < 3 ? .73f : sample < 5 ? .43f : .12f;
                        float width = sample == 0 ? 0 : (sample % 2 == 0 ? -1 : 1) * (sample < 5 ? .20f : .11f);
                        Vector3 point = person.position + Vector3.up * person.height * height + side * width;
                        Vector3 delta = point - eye;
                        if (Physics.RaycastAll(eye, delta.normalized, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                            .Any(hit => hit.collider && !hit.collider.transform.IsChildOf(player.transform))) { blocked = true; break; }
                        foreach (PortraitOccluder visual in nearby)
                        {
                            Vector3 localEye = visual.inverse.MultiplyPoint3x4(eye), localPoint = visual.inverse.MultiplyPoint3x4(point);
                            Vector3 localDelta = localPoint - localEye;
                            if (visual.local.Contains(localEye) || (visual.local.IntersectRay(new Ray(localEye, localDelta.normalized), out float distance)
                                && distance < localDelta.magnitude - .015f)) { blocked = true; break; }
                        }
                    }
                    if (blocked) continue;
                    var ray = new Ray(eye, (subject - eye).normalized);
                    if (!crowd.TryFindTarget(ray, 3.4f, out int visible, out _) || visible != candidate) continue;
                    camera.transform.position = eye; camera.transform.LookAt(subject);
                    Capture(file);
                    Require(true, "Portrait " + file + " uses actual resident " + candidate + " (contact under test " + index
                        + "); seven full-body rays clear physical colliders and conservative pre-batch opaque visual bounds. No resident/scenery moved or hidden.");
                    return;
                }
            }
            throw new InvalidOperationException("No full-body unobscured portrait view among the bounded actual " + (red ? "red-contact" : "Smith") + " candidates.");
        }

        private sealed class PortraitOccluder
        {
            internal Bounds local, world;
            internal Matrix4x4 inverse;
        }

        private static void SharedWardrobe()
        {
            object wardrobe = Get(crowd, "wardrobe");
            Require(wardrobe != null, "Rendering the actual crowd creates one shared wardrobe.");
            Type type = wardrobe.GetType();
            foreach (string field in new[] { "suit", "shirt", "tie", "lapels", "lenses", "dress" })
                Require(type.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(wardrobe) is Material,
                    "The crowd shares its " + field + " material across residents.");
            Require(ReferenceEquals(wardrobe, Get(crowd, "wardrobe")), "Multiple real crowd renders retain the same owned wardrobe.");
        }

        private static void Capture(string name)
        {
            const int width = 960, height = 720;
            RenderTexture previousActive = RenderTexture.active, previousTarget = camera.targetTexture;
            float aspect = camera.aspect; bool srgb = GL.sRGBWrite;
            var linear = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear);
            var display = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                Require(linear.Create() && display.Create(), "Bounded capture targets allocate for " + name + ".");
                camera.aspect = width / (float)height; camera.targetTexture = linear; camera.Render();
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear; Graphics.Blit(linear, display); GL.sRGBWrite = srgb;
                RenderTexture.active = display; pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                Directory.CreateDirectory(Folder); File.WriteAllBytes(Path.Combine(Folder, name), pixels.EncodeToPNG()); receipt.images.Add(name);
            }
            finally
            {
                camera.targetTexture = previousTarget; camera.aspect = aspect; RenderTexture.active = previousActive; GL.sRGBWrite = srgb;
                Object.DestroyImmediate(pixels); linear.Release(); display.Release(); Object.DestroyImmediate(linear); Object.DestroyImmediate(display);
            }
        }

        private static void DisableMedia()
        {
            foreach (SlopCityTikTokSync item in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None)) item.enabled = false;
            foreach (SlopCityCityMedia item in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None)) item.enabled = false;
            foreach (SlopCityMediaPlayer item in Object.FindObjectsByType<SlopCityMediaPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) item.enabled = false;
        }
        private static IEnumerator Delay(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }
        private static object Get(object target, string field) => target.GetType().GetField(field, Hidden).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
        private static object Invoke(object target, string method) => target.GetType().GetMethod(method, Hidden).Invoke(target, null);
        private static void ResetTarget()
        {
            Set(sandbox, "nextTargetAt", 0f);
            typeof(SlopCitySandbox).GetField("webTargetFrame", Hidden)?.SetValue(sandbox, -1);
            Physics.SyncTransforms();
        }
        private static void Require(bool result, string message)
        { if (!result) throw new InvalidOperationException(message); receipt.checks.Add(message); }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(Active, false); Work.Clear(); Time.timeScale = 1;
            if (receipt == null) receipt = NewReceipt();
            if (error != null) receipt.errors.Add(error.ToString());
            foreach (SlopCitySandbox item in Object.FindObjectsByType<SlopCitySandbox>(FindObjectsSortMode.None)) Object.DestroyImmediate(item);
            foreach (string key in Keys)
            {
                if (SessionState.GetBool(Active + key, false)) PlayerPrefs.SetString(key, SessionState.GetString(Active + key + ".value", ""));
                else PlayerPrefs.DeleteKey(key);
            }
            PlayerPrefs.Save(); receipt.finishedAtUtc = DateTime.UtcNow.ToString("O"); receipt.passed = receipt.errors.Count == 0;
            Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "matrix-gameplay-check.json"), JsonUtility.ToJson(receipt, true));
            Debug.Log("SLOP_CITY_MATRIX_GAMEPLAY_CHECK " + (receipt.passed ? "PASSED" : "FAILED") + " checks=" + receipt.checks.Count);
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(receipt.passed ? 0 : 1);
        }
        [Serializable] private sealed class Receipt
        {
            public string startedAtUtc, finishedAtUtc, unityVersion, graphicsDevice, isolatedDataPath;
            public string scope = "Actual saved scene with production browser geometry preparation/static batching and Web targeting symbol, production player Start with constrained tower registration, media/network disabled and automatic player input held. Nearby NPC acquisition and one order/three-bite meal use Sandbox.Interact. Second restaurant exercises explicit Use/ate contract. Beam aim, reach and wall clipping use scoped empty-space fixture geometry and real interception core. Screenshots render actual authored city/crowd/menu/meal at 960x720 with linear-to-sRGB conversion. Teleports select bounded contact/table viewpoints; no walking/traversal, audio, creator-media workload, compiled browser or physical-device acceptance is claimed. Isolated product/save identity required; all touched preference keys restored; scene/import settings never saved. 60-second wall-clock limit.";
            public bool passed; public int population, redContacts;
            public List<string> checks = new List<string>(), errors = new List<string>(), images = new List<string>();
        }
    }
}
#endif
