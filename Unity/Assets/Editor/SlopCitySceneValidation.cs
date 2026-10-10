#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SlopCity.Editor
{
    [Serializable]
    public sealed class SlopCityValidationReport
    {
        public bool passed;
        public string unityVersion;
        public string checkedAtUtc;
        public string scenePath;
        public int checks;
        public int activeRenderers;
        public int meshColliders;
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();

        public void Check(bool condition, string error)
        {
            checks++;
            if (!condition) errors.Add(error);
        }

        public void ThrowIfInvalid()
        {
            passed = errors.Count == 0;
            if (!passed) throw new InvalidOperationException("SLOP CITY scene validation failed:\n- " + string.Join("\n- ", errors));
        }
    }

    /// <summary>Structural/physics checks; this does not claim to replace rendered or Play Mode QA.</summary>
    public static class SlopCitySceneValidation
    {
        public static SlopCityValidationReport Validate(Scene scene)
        {
            var result = new SlopCityValidationReport
            {
                unityVersion = Application.unityVersion,
                checkedAtUtc = DateTime.UtcNow.ToString("O"),
                scenePath = scene.path
            };
            result.Check(scene.IsValid() && scene.isLoaded, "A loaded scene is required.");
            if (!scene.IsValid() || !scene.isLoaded) return result;
            GameObject[] roots = scene.GetRootGameObjects();
            Transform[] transforms = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            Transform seat = Unique(transforms, SlopCitySceneBuilder.SeatedMarker, result);
            Transform stand = Unique(transforms, SlopCitySceneBuilder.StandingMarker, result);
            Transform floor = Unique(transforms, SlopCitySceneBuilder.FloorName, result);
            Transform headset = Unique(transforms, SlopCitySceneBuilder.HeadsetRigName, result);
            Transform look = Unique(transforms, SlopCitySceneBuilder.LookMarker, result);
            Transform tv = Unique(transforms, SlopCitySceneBuilder.TvMarker, result);
            Transform records = Unique(transforms, SlopCitySceneBuilder.RadioMarker, result);
            result.Check(tv && records, "Both source-interaction targets must exist.");

            Camera[] cameras = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true)).Where(camera => camera.enabled && camera.gameObject.activeInHierarchy).ToArray();
            result.Check(cameras.Length == 1, "Expected exactly one active first-person camera, found " + cameras.Length + ".");
            AudioListener[] listeners = roots.SelectMany(root => root.GetComponentsInChildren<AudioListener>(true)).Where(listener => listener.enabled && listener.gameObject.activeInHierarchy).ToArray();
            result.Check(listeners.Length == 1, "Expected exactly one active AudioListener.");
            SlopCityFirstPerson[] players = roots.SelectMany(root => root.GetComponentsInChildren<SlopCityFirstPerson>(true)).ToArray();
            result.Check(players.Length == 1, "Expected exactly one Daniel first-person controller.");

            if (players.Length == 1)
            {
                var serialized = new SerializedObject(players[0]);
                foreach (string field in new[] { "seatedEyeAnchor", "standingFeetAnchor", "viewCamera", "hud" })
                {
                    SerializedProperty property = serialized.FindProperty(field);
                    result.Check(property != null && property.objectReferenceValue, "Player reference is missing: " + field + ".");
                }
                CharacterController capsule = players[0].GetComponent<CharacterController>();
                result.Check(capsule, "Player requires a CharacterController.");
                if (capsule && stand)
                {
                    result.Check(!capsule.enabled, "Saved scene must begin seated with the standing capsule disabled.");
                    Physics.SyncTransforms();
                    Vector3 bottom = stand.position + Vector3.up * capsule.radius;
                    Vector3 top = stand.position + Vector3.up * (capsule.height - capsule.radius);
                    Collider[] overlaps = Physics.OverlapCapsule(bottom, top, capsule.radius * 0.9f, ~0, QueryTriggerInteraction.Ignore)
                        .Where(collider => collider != capsule).ToArray();
                    result.Check(overlaps.Length == 0, "Standing spawn is obstructed by: " + string.Join(", ", overlaps.Select(collider => collider.name)) + ".");
                }
            }

            if (cameras.Length == 1 && seat)
            {
                Camera camera = cameras[0];
                result.Check(Vector3.Distance(camera.transform.position, seat.position) < 0.02f, "Saved camera must start at the seated eye marker.");
                result.Check(Mathf.Abs(camera.fieldOfView - SlopCitySceneBuilder.PlayerVerticalFieldOfView) < 0.1f, "First-person camera field of view differs from the authored framing.");
                result.Check(camera.nearClipPlane <= 0.011f && camera.nearClipPlane >= 0.005f, "Camera near clip must preserve nearby first-person geometry.");
                if (look)
                    result.Check(Vector3.Angle(camera.transform.forward, look.position - seat.position) < 1f, "Initial camera is not facing the seated look target.");
                if (headset)
                {
                    result.Check(headset.IsChildOf(camera.transform), "Headset equipment must follow the player camera.");
                    Renderer[] equipmentRenderers = headset.GetComponentsInChildren<Renderer>(true);
                    result.Check(equipmentRenderers.Length > 0, "Headset rig is missing its authored meshes.");
                    result.Check(equipmentRenderers.All(renderer => !renderer.enabled), "Local first-person headset renderers must stay disabled for a clear view.");
                    result.Check(headset.GetComponentsInChildren<Collider>(true).Length == 0, "Camera equipment must not have colliders.");
                }
            }

            if (floor)
            {
                MeshCollider[] floorColliders = floor.GetComponentsInChildren<MeshCollider>(true);
                result.Check(floorColliders.Length > 0 && floorColliders.All(collider => collider.sharedMesh), "Floor must have a non-empty static mesh collider.");
                if (stand && floorColliders.Length > 0)
                {
                    var ray = new Ray(stand.position + Vector3.up * 0.2f, Vector3.down);
                    bool topFaceHit = floorColliders.Any(collider => collider.Raycast(ray, out RaycastHit hit, 0.8f) && hit.normal.y > 0.5f);
                    result.Check(topFaceHit, "Standing marker must lie above an upward-facing floor collision surface.");
                }
                Renderer[] floorRenderers = floor.GetComponentsInChildren<Renderer>(true);
                result.Check(floorRenderers.Any(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy), "Floor geometry must be visible in the generated scene.");
            }

            Renderer[] renderers = roots.SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
            Renderer[] activeRenderers = renderers.Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
            result.activeRenderers = activeRenderers.Length;
            result.Check(result.activeRenderers > 20, "Too little active lounge geometry survived import.");
            string[] invalidMaterials = activeRenderers.Where(renderer => renderer.sharedMaterials.Length == 0
                || renderer.sharedMaterials.Any(material => !material || !material.shader || material.shader.name != "Standard"))
                .Select(renderer => renderer.name).ToArray();
            result.Check(invalidMaterials.Length == 0, "Renderers have missing/non-Standard materials: " + string.Join(", ", invalidMaterials.Take(20)) + ".");
            string[] emptyMeshes = activeRenderers.Select(renderer => renderer.GetComponent<MeshFilter>()).Where(filter => filter)
                .Where(filter => !filter.sharedMesh || filter.sharedMesh.vertexCount == 0).Select(filter => filter.name).ToArray();
            result.Check(emptyMeshes.Length == 0, "Renderer meshes are empty: " + string.Join(", ", emptyMeshes.Take(20)) + ".");
            ValidatePalette(activeRenderers, result);
            result.meshColliders = roots.Sum(root => root.GetComponentsInChildren<MeshCollider>(true).Length);
            result.Check(result.meshColliders > 5, "Expected floor, structural and furniture collision geometry.");
            result.Check(transforms.Count(transform => transform.name == "Lounge_RoomBoundary_Colliders") == 1, "Generated room boundary is missing or duplicated.");
            Text[] labels = roots.SelectMany(root => root.GetComponentsInChildren<Text>(true)).ToArray();
            result.Check(labels.Length >= 5 && labels.All(label => label.font), "HUD labels require valid built-in fonts.");
            result.Check(!transforms.Any(transform => transform.name.StartsWith("Visor_Left", StringComparison.Ordinal) || transform.name.StartsWith("Visor_Right", StringComparison.Ordinal)), "Removed visor HUD decorations must not be recreated.");
            result.warnings.Add("Structural and physics checks do not verify final GPU rendering or interactive Play Mode behavior.");
            result.passed = result.errors.Count == 0;
            return result;
        }

        private static void ValidatePalette(Renderer[] renderers, SlopCityValidationReport result)
        {
            Material[] materials = renderers.SelectMany(renderer => renderer.sharedMaterials).Where(material => material).Distinct().ToArray();
            ValidatePaletteMaterial(materials, "Signal | phosphor", new Color(220f / 255f, 249f / 255f, 156f / 255f), 2f, result);
            ValidatePaletteMaterial(materials, "Screen | dark phosphor", new Color(20f / 255f, 46f / 255f, 44f / 255f), 0.005f, result);
            ValidatePaletteMaterial(materials, "Upholstery | midnight teal", new Color(52f / 255f, 103f / 255f, 102f / 255f), 0f, result);
        }

        private static void ValidatePaletteMaterial(Material[] materials, string authoredName, Color expectedColor, float minimumEmission,
            SlopCityValidationReport result)
        {
            Material[] matching = materials.Where(material => SlopCitySceneBuilder.MaterialNameMatches(material.name, authoredName)).ToArray();
            result.Check(matching.Length > 0, "Expected visible authored palette material: " + authoredName + ".");
            if (matching.Length == 0) return;
            result.Check(matching.All(material => material.HasProperty("_Color") && ColorMatches(material.color, expectedColor)),
                "Authored palette colour was not applied to " + authoredName + "; inspect FBX material-name normalization.");
            if (minimumEmission > 0f)
                result.Check(matching.All(material => material.HasProperty("_EmissionColor") && material.IsKeywordEnabled("_EMISSION")
                    && material.GetColor("_EmissionColor").maxColorComponent >= minimumEmission
                    && (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0),
                    "Expected visible emission is missing from " + authoredName + ". Required max RGB >= " + minimumEmission
                    + ". Material state: " + string.Join("; ", matching.Select(DescribeEmission)));
        }

        private static string DescribeEmission(Material material)
        {
            bool hasEmission = material.HasProperty("_EmissionColor");
            Color emission = hasEmission ? material.GetColor("_EmissionColor") : Color.clear;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "name='{0}', path='{1}', shader='{2}', HasProperty={3}, IsKeywordEnabled={4}, RGB=({5:F4},{6:F4},{7:F4}), keywords=[{8}], GI={9}",
                material.name, AssetDatabase.GetAssetPath(material), material.shader ? material.shader.name : "<missing>", hasEmission,
                material.IsKeywordEnabled("_EMISSION"), emission.r, emission.g, emission.b,
                string.Join(",", material.shaderKeywords), material.globalIlluminationFlags);
        }

        private static bool ColorMatches(Color actual, Color expected)
        {
            return Mathf.Abs(actual.r - expected.r) < 0.02f && Mathf.Abs(actual.g - expected.g) < 0.02f
                && Mathf.Abs(actual.b - expected.b) < 0.02f;
        }

        private static Transform Unique(Transform[] transforms, string name, SlopCityValidationReport result)
        {
            Transform[] found = transforms.Where(transform => transform.name == name).ToArray();
            result.Check(found.Length == 1, "Expected exactly one '" + name + "', found " + found.Length + ".");
            return found.Length == 1 ? found[0] : null;
        }
    }
}
#endif
