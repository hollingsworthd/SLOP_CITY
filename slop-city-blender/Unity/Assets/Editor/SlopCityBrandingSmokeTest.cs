#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace SlopCity.Editor
{
    /// <summary>Dedicated batch Editor; use -quit. Does not regenerate or save the scene.</summary>
    public static class SlopCityBrandingSmokeTest
    {
        [System.Serializable] private sealed class Receipt
        {
            public bool passed;
            public string checkedAtUtc, unityVersion;
            public int signs, renderers;
            public System.Collections.Generic.List<string> checks = new System.Collections.Generic.List<string>();
            public System.Collections.Generic.List<string> errors = new System.Collections.Generic.List<string>();
            public System.Collections.Generic.List<Placement> placements = new System.Collections.Generic.List<Placement>();
        }
        [System.Serializable] private sealed class Placement
        {
            public string name, artwork;
            public Vector3 position, outward;
            public Vector2 size;
        }

        public static void Run()
        {
            var receipt = new Receipt { checkedAtUtc = System.DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            try
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath);
                SlopCityBranding branding = SlopCityBranding.Ensure(scene);
                Require(branding && branding.SignCount == 6, "Six fixed brand plaques install in the saved world.", receipt);
                receipt.signs = branding.SignCount;
                Require(branding.GetComponentsInChildren<Collider>().Length == 0
                    && branding.GetComponentsInChildren<AudioSource>().Length == 0
                    && branding.GetComponentsInChildren<Canvas>().Length == 0
                    && branding.GetComponentsInChildren<SlopCityInteractable>().Length == 0,
                    "Brand plaques add no collisions, audio, HUD or gameplay interaction.", receipt);
                Require(SlopCityBranding.Ensure(scene) == branding && branding.SignCount == 6,
                    "Repeated initialization preserves the same six plaques.", receipt);
                Physics.SyncTransforms();
                var meshes = new HashSet<Mesh>();
                var materials = new HashSet<Material>();
                foreach (Transform plaque in branding.transform)
                {
                    Transform face = plaque.Find("Official_Artwork");
                    MeshRenderer renderer = face.GetComponent<MeshRenderer>();
                    Texture texture = renderer.sharedMaterial.mainTexture;
                    Vector3 scale = face.localScale;
                    Require(Mathf.Abs(scale.x / scale.y - (float)texture.width / texture.height) < .001f,
                        "Official artwork keeps its full original aspect ratio: " + plaque.name, receipt);
                    Vector3 outward = -plaque.forward;
                    Require(Physics.Raycast(plaque.position + outward * .08f, -outward, .18f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                        "Plaque mounts just in front of a physical wall: " + plaque.name, receipt);
                    receipt.placements.Add(new Placement { name = plaque.name, artwork = texture.name,
                        position = plaque.position, outward = outward, size = new Vector2(scale.x, scale.y) });
                    foreach (MeshRenderer part in plaque.GetComponentsInChildren<MeshRenderer>())
                    {
                        meshes.Add(part.GetComponent<MeshFilter>().sharedMesh);
                        materials.Add(part.sharedMaterial);
                        receipt.renderers++;
                    }
                }
                Require(meshes.Count == 1 && materials.Count == 3 && receipt.renderers == 12,
                    "Six plaques reuse one quad and three materials, with no per-frame scripts.", receipt);
                VerifyArtwork("SlopCityLogo.png", "7196e44a1750811ecc5e8115300b934c7979caba3bbfc70d7dfb6477c22ebb7d", receipt);
                VerifyArtwork("MachinePublishersLogo.png", "464d4484e44c91e6ebe5820ceaa78416e692b8ebe46d9d2fca5915e2d82256b0", receipt);
            }
            catch (System.Exception error) { receipt.errors.Add(error.ToString()); }
            receipt.passed = receipt.errors.Count == 0;
            string directory = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "Builds/Reports");
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "branding-check.json"), JsonUtility.ToJson(receipt, true));
            Debug.Log("SLOP_CITY_BRANDING_SMOKE_" + (receipt.passed ? "PASSED" : "FAILED"));
            if (!receipt.passed) throw new System.InvalidOperationException(string.Join("\n", receipt.errors));
        }

        private static void VerifyArtwork(string name, string expected, Receipt receipt)
        {
            string path = System.IO.Path.Combine(Application.dataPath, "Resources/Brand", name);
            using (var hash = System.Security.Cryptography.SHA256.Create())
            {
                string actual = System.BitConverter.ToString(hash.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
                Require(actual == expected, "Bundled source bytes exactly match the supplied official artwork: " + name, receipt);
            }
        }

        private static void Require(bool condition, string message, Receipt receipt)
        {
            if (!condition) throw new System.InvalidOperationException(message);
            receipt.checks.Add(message);
        }
    }
}
#endif
