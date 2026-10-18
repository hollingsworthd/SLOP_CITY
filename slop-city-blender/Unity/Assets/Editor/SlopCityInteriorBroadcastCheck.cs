#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Exact production room construction, placement and stable streamed device identities.</summary>
    public static class SlopCityInteriorBroadcastCheck
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string ReportPath = "Builds/Reports/interior-broadcast-check.json";
        private static Receipt receipt;

        // Dedicated graphics-enabled batch Editor, no saved scene/player/network is loaded.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Interior broadcast check requires a dedicated batch Editor outside Play Mode.");
            receipt = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            GameObject fixture = null;
            try
            {
                Type geometry = typeof(SlopCityBuildingInterior).Assembly.GetType("SlopCity.SlopCityInteriorGeometry");
                geometry.GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                fixture = new GameObject("Interior_Broadcast_Check");
                var building = fixture.AddComponent<SlopCityBuildingInterior>();
                typeof(SlopCityBuildingInterior).GetField("serial", Hidden).SetValue(building, 7);
                typeof(SlopCityBuildingInterior).GetProperty("ShaftLocalCenter").SetValue(building, new Vector3(0f, 0f, 4.9f));
                MethodInfo build = typeof(SlopCityBuildingInterior).GetMethod("BuildRooms", Hidden);
                foreach (string purpose in new[] { "Residential apartments", "Music studios", "Publisher offices", "Market workshops" })
                    foreach (Vector2 dimensions in new[] { new Vector2(12.5f, 12.5f), new Vector2(34f, 29f) })
                    {
                        typeof(SlopCityBuildingInterior).GetProperty("Purpose").SetValue(building, purpose);
                        Transform floor = new GameObject("Runtime_Floor_01").transform;
                        floor.SetParent(fixture.transform, false);
                        build.Invoke(building, new object[] { floor, 1, dimensions });
                        Physics.SyncTransforms();
                        var props = floor.GetComponentsInChildren<SlopCityInteriorBroadcast>();
                        Check(props.Length == 6 && props.Count(prop => prop.IsRadio) == 2, "Production floor contains four TVs and two radios: " + purpose + " / " + dimensions);
                        Check(props.All(prop => prop.Screen && prop.Screen.transform == prop.transform), "Device-root screen owns housing descendants for occlusion.");
                        Check(props.All(prop => prop.GetComponentsInChildren<VideoPlayer>(true).Length == 0
                            && prop.GetComponentsInChildren<AudioSource>(true).Length == 0), "Room props create no decoder or audio source outside the shared pool.");
                        foreach (var prop in props)
                        {
                            Vector3 local = prop.transform.localPosition;
                            Check(Mathf.Abs(local.x) > dimensions.x * .5f - .4f && Mathf.Abs(local.z) < dimensions.y * .5f - .35f,
                                "Device stays at the outer room wall, clear of circulation and doorways.");
                            Check(Vector3.Dot(prop.Screen.FrontNormal, local.x > 0 ? Vector3.left : Vector3.right) > .99f,
                                "Device face points inward into its room.");
                            Check(prop.Screen.AudibleRange <= 7f, "Room media keeps a short local audible range.");
                            if (prop.IsRadio) Check(local.x > 0 && prop.Screen.AudioOnly, "Radios stay outside sanitary rooms and use the shared audio-only path.");
                            else Check(prop.Screen.Display.bounds.min.y > 2.21f && prop.Screen.Display.bounds.max.y < 2.93f,
                                "Wall picture clears worktops and stays below the ceiling.");
                            foreach (Renderer renderer in prop.GetComponentsInChildren<Renderer>())
                                Check(renderer.bounds.max.y < 3.04f, "Every housing/shelf stays below the occupied-floor ceiling.");
                            foreach (Collider furniture in floor.GetComponentsInChildren<Collider>().Where(value => value.name.EndsWith("_Storage_Shelf") || value.name.EndsWith("_Wardrobe")))
                                Check(!furniture.bounds.Intersects(prop.Screen.Display.bounds), "Media devices clear actual bookcase and wardrobe envelopes.");
                            foreach (Collider collider in prop.GetComponentsInChildren<Collider>())
                                Check(collider.bounds.min.x > 1.21f || collider.bounds.max.x < -1.21f, "No device collider intrudes into the corridor.");
                        }
                        var identities = props.Select(prop => SlopCitySalvageItem.SourceIdentity(prop.Screen.transform)).OrderBy(value => value).ToArray();
                        var original = props.Select(prop => prop.Screen.Display.sharedMaterial).ToArray();
                        foreach (int side in new[] { -1, 1 })
                            foreach (int room in new[] { 0, 1 })
                            {
                                float south = room == 0 ? -dimensions.y * .5f + .24f : .14f;
                                float north = room == 0 ? -.14f : dimensions.y * .5f - .24f;
                                SlopCityInteriorBroadcast.AttachRoom(floor, (side < 0 ? "West" : "East") + (room == 0 ? "South" : "North"), side,
                                    dimensions.x * .5f - .24f - 1.21f, south, north, 0, side > 0);
                            }
                        Check(floor.GetComponentsInChildren<SlopCityInteriorBroadcast>().Length == 6, "Repeated room attachment is idempotent.");
                        SlopCityInteriorBroadcast.ReleaseFloor(floor);
                        SlopCityInteriorBroadcast.ReleaseFloor(floor);
                        Check(props.Select((prop, index) => prop.Screen.Display.sharedMaterial == original[index]).All(value => value), "Idempotent release preserves authored prop materials.");
                        ReleaseFurnishing(floor);
                        Object.DestroyImmediate(floor.gameObject);
                        floor = new GameObject("Runtime_Floor_01").transform; floor.SetParent(fixture.transform, false);
                        build.Invoke(building, new object[] { floor, 1, dimensions });
                        Check(floor.GetComponentsInChildren<SlopCityInteriorBroadcast>().Select(prop => SlopCitySalvageItem.SourceIdentity(prop.Screen.transform)).OrderBy(value => value).SequenceEqual(identities),
                            "Reloaded floor retains exact device identities for stable independent media subsets.");
                        SlopCityInteriorBroadcast.ReleaseFloor(floor); ReleaseFurnishing(floor); Object.DestroyImmediate(floor.gameObject);
                    }
                receipt.passed = true;
            }
            catch (Exception error) { receipt.error = error.ToString(); }
            finally
            {
                if (fixture) Object.DestroyImmediate(fixture);
                receipt.completedAtUtc = DateTime.UtcNow.ToString("O");
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)); File.WriteAllText(ReportPath, JsonUtility.ToJson(receipt, true));
            }
            EditorApplication.Exit(receipt.passed ? 0 : 1);
        }
        private static void ReleaseFurnishing(Transform floor)
        {
            SlopCityInteriorFurnishing furnishing = floor.GetComponent<SlopCityInteriorFurnishing>();
            if (!furnishing) return;
            MethodInfo release = typeof(SlopCityInteriorFurnishing).GetMethod("Release", Hidden);
            if (release == null) throw new MissingMethodException(typeof(SlopCityInteriorFurnishing).FullName, "Release");
            release.Invoke(furnishing, null);
        }
        private static void Check(bool value, string description) { receipt.checks++; if (!value) throw new InvalidOperationException(description); }
        [Serializable] private sealed class Receipt
        {
            public string startedAtUtc, completedAtUtc, unityVersion, error;
            public bool passed;
            public int checks;
        }
    }
}
#endif
