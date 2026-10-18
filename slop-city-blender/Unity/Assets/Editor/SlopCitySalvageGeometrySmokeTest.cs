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
    /// <summary>Actual prepared pane/panel mutation, stable IDs and protected physical dressing.</summary>
    public static class SlopCitySalvageGeometrySmokeTest
    {
        private static Receipt receipt;
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || Application.companyName != "SlopCityValidation-v0116")
                throw new InvalidOperationException("Salvage checks require isolated SlopCityValidation-v0116 batch Edit Mode.");
            receipt = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O") };
            var random = UnityEngine.Random.state;
            Application.logMessageReceived += Log;
            try
            {
                var scene = EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath, OpenSceneMode.Single);
                foreach (var sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include, FindObjectsSortMode.None)) sync.enabled = false;
                foreach (var media in Object.FindObjectsByType<SlopCityCityMedia>(FindObjectsInactive.Include, FindObjectsSortMode.None)) media.enabled = false;
                foreach (var player in Object.FindObjectsByType<SlopCityFirstPerson>(FindObjectsInactive.Include, FindObjectsSortMode.None)) player.enabled = false;
                SlopCityWebGeometryBuildProcessor.ProtectRuntimeBuildingGeometry(scene);
                Transform city = GameObject.Find("SlopCity_NineBlocks").transform;
                SlopCitySalvagePreparation.Prepare(city);
                var surfaces = city.GetComponentsInChildren<SlopCitySalvageSurface>(true);
                Check(surfaces.Length >= 51 && surfaces.Count(surface => surface.name == "Human_Scale_Window_Panes") == 51, "Every actual prepared city pane mesh is partitioned.");
                receipt.surfaces = surfaces.Length; receipt.logicalParts = surfaces.Sum(surface => surface.PartCount);
                receipt.sourceGeometryBytes = surfaces.Sum(surface => surface.SourceGeometryBytes);
                Check(receipt.sourceGeometryBytes <= SlopCitySalvagePreparation.MaximumMutableSourceBytes, "Aggregate retained mutable source geometry fits its fixed ceiling.");
                Check(SlopCitySalvageSurface.OwnedBytes == 0, "Preparation allocates no owned runtime edit geometry.");
                string[] identities = surfaces.SelectMany(surface => Enumerable.Range(0, surface.PartCount).Select(surface.PartId)).ToArray();
                Check(identities.Distinct().Count() == identities.Length, "Every actual pane/panel has a distinct durable logical ID.");
                SlopCitySalvagePreparation.Prepare(city);
                Check(city.GetComponentsInChildren<SlopCitySalvageSurface>(true).SequenceEqual(surfaces), "Repeated Prepare does not duplicate surface components.");
                Check(surfaces.SelectMany(surface => Enumerable.Range(0, surface.PartCount).Select(surface.PartId)).SequenceEqual(identities), "Repeated preparation preserves all logical part IDs.");
                var sourceHashes = surfaces.ToDictionary(surface => surface,
                    surface => SlopCityDetailMeshCodec.ComputeHash(SlopCityDetailMeshCodec.Encode(surface.SourceMesh)));
                SlopCityWebDecorativeMerge.Prepare(scene);
                SlopCityWebMeshBudget.Prepare(scene, true);
                foreach (var surface in surfaces)
                {
                    Check(surface.SourceMesh.isReadable && surface.GetComponent<MeshFilter>().sharedMesh == surface.SourceMesh,
                        "Web mesh preparation preserves the exact readable mutable source identity.");
                    Check(SlopCityDetailMeshCodec.ComputeHash(SlopCityDetailMeshCodec.Encode(surface.SourceMesh)) == sourceHashes[surface],
                        "Web preparation never packs, welds or replaces mutable indices/vertex data.");
                }
                receipt.webPreparationChecked = true;
                foreach (var surface in surfaces)
                {
                    Check(!surface.GetComponentsInParent<Transform>().Any(part => part.name.Contains("HorizonTower_")), "Horizon backdrop remains immutable.");
                    Check(surface.GetComponent<SlopCitySalvageItem>().Pickable, "A mutable surface never contains protected floor/media/poster/elevator content.");
                    Check(GameObjectUtility.GetStaticEditorFlags(surface.gameObject) == 0, "Mutable surface is excluded from static batching.");
                    Exercise(surface);
                    receipt.peakOwnedBytes = Math.Max(receipt.peakOwnedBytes, SlopCitySalvageSurface.OwnedBytes);
                }
                Check(receipt.peakOwnedBytes <= SlopCitySalvageSurface.MaximumOwnedBytes, "All lazy owned meshes/colors/index scratch remain within fixed ceiling.");
                Dressing(city);
                StreamedSofa(city);
                Check(receipt.runtimeErrors.Count == 0, "No runtime errors or assertions.");
            }
            catch (Exception error) { receipt.errors.Add(error.ToString()); }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                receipt.finalOwnedBytes = SlopCitySalvageSurface.OwnedBytes;
                if (receipt.finalOwnedBytes != 0) receipt.errors.Add("Owned edit mesh budget was not fully released.");
                receipt.finalSharedFinishes = SlopCitySalvageSurface.SharedFinishCount;
                if (receipt.finalSharedFinishes != 0) receipt.errors.Add("Shared polish material cache was not fully released.");
                UnityEngine.Random.state = random;
                Application.logMessageReceived -= Log;
            }
            receipt.passed = receipt.errors.Count == 0 && receipt.runtimeErrors.Count == 0;
            receipt.finishedAtUtc = DateTime.UtcNow.ToString("O");
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Reports/salvage-geometry-check.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(receipt, true));
            Debug.Log("SLOP_CITY_SALVAGE_GEOMETRY_" + (receipt.passed ? "PASSED" : "FAILED") + " " + receipt.checks + " checks");
            EditorApplication.Exit(receipt.passed ? 0 : 1);
        }
        private static void Exercise(SlopCitySalvageSurface surface)
        {
            Mesh source = surface.SourceMesh;
            Check(surface.TryGetPart(surface.PartId(0), out int resolved) && resolved == 0
                && surface.TryGetPart(surface.PartId(surface.PartCount - 1), out resolved) && resolved == surface.PartCount - 1,
                "Saved first/last IDs resolve to their exact logical piece without enumerating generated IDs.");
            Check(!surface.TryGetPart(surface.PartId(0) + "x", out _) && !surface.TryGetPart("other/" + surface.PartId(0), out _),
                "Foreign and malformed saved IDs cannot resolve a piece.");
            string original = SlopCityDetailMeshCodec.ComputeHash(SlopCityDetailMeshCodec.Encode(source));
            int[] indices = Get<int[]>(surface, "indices"), starts = Get<int[]>(surface, "starts");
            Vector3[] vertices = source.vertices;
            int first = starts[0]; Vector3 a = vertices[indices[first]], b = vertices[indices[first + 1]], c = vertices[indices[first + 2]];
            Vector3 center = surface.transform.TransformPoint((a + b + c) / 3);
            Vector3 normal = surface.transform.TransformDirection(Vector3.Cross(b - a, c - a).normalized);
            Ray ray = new Ray(center + normal * .2f, -normal);
            Physics.SyncTransforms();
            Check(surface.TryRaycast(ray, .4f, out int hit, out _) && hit == 0, "Triangle ray resolves exact original logical piece: " + surface.name);
            Check(surface.SetRemoved(0, true), "Actual piece removal succeeds within owned budget.");
            Mesh working = surface.WorkingMesh; Check(working && working != source, "First edit owns one mesh, preserving immutable source.");
            Check(working.GetIndexCount(0) < source.GetIndexCount(0), "Removal actually removes indices.");
            Check(!surface.TryRaycast(ray, .4f, out hit, out _) || hit != 0, "Removed physical triangles no longer target the piece.");
            Check(surface.SetRemoved(0, true) && surface.WorkingMesh == working, "Repeated removal creates no mesh copy.");
            Check(SlopCityDetailMeshCodec.ComputeHash(SlopCityDetailMeshCodec.Encode(source)) == original, "Removal preserves all original source bytes.");
            Check(surface.SetRemoved(0, false) && surface.WorkingMesh == working, "Restoration reuses the same bounded working mesh.");
            Check(working.GetIndexCount(0) == source.GetIndexCount(0), "Restoration restores the exact triangle count.");
            Check(working.vertices.SequenceEqual(source.vertices) && working.normals.SequenceEqual(source.normals) && working.uv.SequenceEqual(source.uv), "Every original vertex/normal/UV value remains exact.");
            Physics.SyncTransforms();
            Check(surface.TryRaycast(ray, .4f, out hit, out _) && hit == 0, "Restored physical triangle resolves the same logical ID.");
            int chosen = surface.PartCount > 1 ? 1 : 0;
            surface.SetCleanProgress(chosen, 2);
            Color32[] colors = working.colors32;
            Check(colors[indices[starts[chosen]]].r < 255 && colors[indices[starts[chosen]]].r > 0, "Partial strokes visibly polish the actual selected part.");
            if (chosen != 0) Check(colors[indices[starts[0]]].r == 255, "Cleaning leaves the neighboring piece unchanged.");
            surface.SetCleanProgress(chosen, 4);
            Check(working.colors32[indices[starts[chosen]]].r == 0, "Four strokes fully polish the selected part.");
            Check(surface.WorkingMesh == working, "Cleaning allocates no additional mesh.");
            int[] lengths = Get<int[]>(surface, "lengths");
            int beforeRebuilds = surface.IndexRebuildCount;
            long owned = SlopCitySalvageSurface.OwnedBytes, expectedIndices = (long)source.GetIndexCount(0);
            surface.BeginSavedStateRestore();
            for (int part = 0; part < surface.PartCount; part++)
            {
                bool removed = part % 3 == 0;
                Check(surface.ApplySavedState(part, 4, removed), "Saved part state is accepted into a deferred restore.");
                if (removed) expectedIndices -= lengths[part];
            }
            Check(surface.IndexRebuildCount == beforeRebuilds, "A multi-part saved restore performs no intermediate geometry/collider rebuild.");
            Check(surface.EndSavedStateRestore() && surface.IndexRebuildCount == beforeRebuilds + 1,
                "All persisted pieces flush in exactly one geometry/collider rebuild.");
            Check(working.GetIndexCount(0) == (ulong)expectedIndices && working.colors32[indices[starts[chosen]]].r == 0,
                "Batched restore physically removes the expected triangles and preserves polished vertex state.");
            Physics.SyncTransforms();
            Check(!surface.TryRaycast(ray, .4f, out hit, out _) || hit != 0, "Batched saved removal also retires actual piece collision.");
            surface.BeginSavedStateRestore();
            for (int part = 0; part < surface.PartCount; part++) surface.ApplySavedState(part, 0, false);
            Check(surface.EndSavedStateRestore() && surface.IndexRebuildCount == beforeRebuilds + 2
                && working.GetIndexCount(0) == source.GetIndexCount(0), "A second batch restores all triangles in one rebuild.");
            Check(surface.WorkingMesh == working && SlopCitySalvageSurface.OwnedBytes == owned,
                "Batched replay retains one owned mesh and no additional persistent edit buffers.");
        }
        private static void StreamedSofa(Transform city)
        {
            var player = Object.FindFirstObjectByType<SlopCityFirstPerson>();
            SlopCityBuildingWorld world = SlopCityBuildingWorld.Ensure(player);
            Check(world, "Actual saved-city building access prepares the bounded streamed furniture case."); world.enabled = false;
            SlopCityBuildingInterior building = world.Buildings.First(item => item && item.IsTower && item.FloorCount > 2);
            Transform floor = building.EnsureFloor(0);
            Transform sofa = floor.GetComponentsInChildren<Transform>(true).First(item => item.name.EndsWith("_Sofa", StringComparison.Ordinal));
            string prefix = sofa.name.Substring(0, sofa.name.Length - "_Sofa".Length);
            Transform[] pieces = floor.GetComponentsInChildren<Transform>(true).Where(item => item.name == prefix + "_Sofa_Back"
                || item.name == prefix + "_Sofa_Arm" || item.name == prefix + "_Seat_Cushion").ToArray();
            Check(pieces.Length == 5, "Real streamed sofa has its back, two arms and two cushions.");
            Matrix4x4[] before = pieces.Select(item => item.localToWorldMatrix).ToArray();
            SlopCitySalvagePreparation.Prepare(city);
            Check(sofa.GetComponent<SlopCitySalvageItem>() && sofa.GetComponent<SlopCitySalvageItem>().Pickable, "Streamed sofa body is one removable physical inventory assembly.");
            for (int index = 0; index < pieces.Length; index++)
            {
                Check(pieces[index].parent == sofa, "Every sofa accessory shares body removal ownership.");
                Matrix4x4 after = pieces[index].localToWorldMatrix;
                for (int value = 0; value < 16; value++) Check(Mathf.Abs(before[index][value] - after[value]) < .0001f, "Grouping preserves actual accessory world geometry.");
            }
            sofa.gameObject.SetActive(false);
            Check(pieces.All(item => !item.gameObject.activeInHierarchy), "Removing the complete sofa cannot leave floating arms or cushions.");
            sofa.gameObject.SetActive(true);
            building.ReleaseFloor(0);
        }
        private static void Dressing(Transform city)
        {
            var posters = city.GetComponentsInChildren<Transform>(true).Where(part => part.name.StartsWith(SlopCitySalvagePreparation.PosterPrefix, StringComparison.Ordinal)).ToArray();
            Check(posters.Length == 204, "Exactly 204 additional physical posters exist.");
            Check(posters.All(part => !part.GetComponent<Collider>() && SlopCitySalvageItem.IsProtectedArt(part)), "Every new poster is protected and creates no physical obstacle.");
            Check(posters.Select(part => part.GetComponent<MeshFilter>().sharedMesh).Distinct().Count() == 1
                && posters.Select(part => part.GetComponent<Renderer>().sharedMaterial).Distinct().Count() == 4, "Posters reuse one existing quad and four atlas materials.");
            var mailboxes = city.GetComponentsInChildren<SlopCitySalvageItem>(true).Where(item => item.Category == "mailbox").ToArray();
            Check(mailboxes.Length == 9 && mailboxes.All(item => item.Pickable), "Nine actual physical mailboxes have stable pickup metadata.");
            foreach (var box in mailboxes)
                Check(box.GetComponentsInChildren<BoxCollider>(true).Length == 3 && box.GetComponentsInChildren<MeshFilter>(true).All(filter => filter.sharedMesh), "Mailbox retains body/lid/pedestal geometry and colliders.");
            var pawn = city.GetComponentsInChildren<SlopCityPawnCounter>(true);
            Check(pawn.Length == 1 && pawn[0].name == "Service_Counter" && !pawn[0].GetComponent<SlopCitySalvageItem>().Pickable, "Pawn service uses the existing preserved SignalYard counter.");
            Check(city.GetComponentsInChildren<TextMesh>(true).Any(text => text.name == SlopCitySalvagePreparation.PawnCounterName && text.text.Contains("PAWN SHOP")), "Physical pawn sign identifies the service.");
        }
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, Hidden).GetValue(owner);
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); receipt.checks++; }
        private static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception) receipt.runtimeErrors.Add(message + "\n" + trace); }
        [Serializable] private sealed class Receipt
        {
            public bool passed, webPreparationChecked; public int checks, surfaces, logicalParts, finalSharedFinishes;
            public long sourceGeometryBytes, peakOwnedBytes, finalOwnedBytes;
            public string startedAtUtc, finishedAtUtc;
            public string scope = "Actual prepared city in isolated Edit Mode. Bounded direct per-piece triangle rays and real mesh/collider mutations; no Play Mode, device memory measurement, network or whole-city physical traversal.";
            public List<string> errors = new List<string>(), runtimeErrors = new List<string>();
        }
    }
}
#endif
