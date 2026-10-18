#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SlopCity.Editor
{
    /// <summary>Deterministic physics checks for shared, unobtrusive contract guidance.</summary>
    public static class SlopCityHintSmokeTest
    {
        private const string ReportPath = "Builds/Reports/contract-hint-check.json";
        [Serializable] private sealed class Receipt
        {
            public bool passed;
            public string unityVersion = Application.unityVersion;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
        }

        // Dedicated batch Editor with -quit; no Play Mode and no PlayerPrefs writes.
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Hint checks require a dedicated batch Editor.");
            var report = new Receipt(); GameObject fixture = null;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                fixture = new GameObject("Hint_Physics_Fixture");
                GameObject ground = Box(fixture.transform,"Ground",new Vector3(0,-.1f,0),new Vector3(24,.2f,24));
                GameObject wall = Box(fixture.transform,"Wall",new Vector3(0,1,0),new Vector3(8,2,.4f));
                var user = new GameObject("Player"); user.transform.SetParent(fixture.transform); user.transform.position = new Vector3(0,0,-9);
                var target = new GameObject("Destination"); target.transform.SetParent(fixture.transform); target.transform.position = new Vector3(0,.85f,9);
                Physics.SyncTransforms();
                var route = new SlopCityHintRoute();
                RunRoute(route,user.transform.position,target.transform.position,user.transform,target.transform);
                Check(report,route.ReachedTarget,"A ground-supported route reaches a destination beyond a solid wall.");
                bool around = false;
                for (int i = 0; i < route.Points.Count; i++)
                {
                    if (Mathf.Abs(route.Points[i].x) > 4.25f) around = true;
                    if (i > 0) CheckSegment(route,route.Points[i - 1],route.Points[i]);
                }
                Check(report,around,"The rabbit route goes around the wall instead of pointing through it.");
                Check(report,!route.ClearSight(user.transform.position + Vector3.up * 1.4f,target.transform.position),
                    "World clues behind the wall fail the visibility check.");
                wall.SetActive(false);
                GameObject gate = Box(fixture.transform,"Door",new Vector3(0,1,0),new Vector3(1.4f,2,.2f));
                var door = gate.AddComponent<SlopCityDoor>(); door.Configure(gate.transform,gate.GetComponent<Collider>(),gate.transform.position,"Fixture entrance");
                Physics.SyncTransforms();
                RunRoute(route,new Vector3(0,0,-2),new Vector3(0,.85f,5),user.transform,target.transform);
                Check(report,route.ReachedTarget,"An operable door remains a usable route connection.");
                Check(report,route.NearbyDoor(new Vector3(0,0,-2),new Vector3(0,0,2)) == door,
                    "The next closed doorway is identified for an explicit entrance transmission.");
                Check(report,!route.ClearSight(new Vector3(0,1.4f,-2),new Vector3(0,.1f,2)),
                    "A closed door hides any stencil beyond it.");
                gate.SetActive(false); Physics.SyncTransforms();
                RunRoute(route,Vector3.zero,new Vector3(0,12,0),user.transform,target.transform);
                Check(report,!route.ReachedTarget,"A destination on another level cannot be mistaken for a street-level arrival.");
                ground.SetActive(false); Physics.SyncTransforms();
                RunRoute(route,Vector3.zero,new Vector3(0,.85f,8),user.transform,target.transform);
                Check(report,!route.ReachedTarget && route.Points.Count == 0,"No breadcrumb route crosses unsupported ground.");
                Check(report,route.ExpandedNodes <= 6000,"Search has a strict work bound when no route exists.");
                Check(report,SlopCityRabbitTrail.Bearing(Vector3.zero,Vector3.forward) == "north"
                    && SlopCityRabbitTrail.Bearing(Vector3.zero,Vector3.left) == "west",
                    "Transmission bearings match the city's north and west axes.");
                Check(report,SlopCityRabbitTrail.ProximityBand(3.4f) == 0 && SlopCityRabbitTrail.ProximityBand(3.41f) == 1
                    && SlopCityRabbitTrail.ProximityBand(80f) == 3,"Qualitative proximity bands preserve the interaction reach boundary.");
                CheckReusedTargetGuidance(report, fixture.transform);
                report.passed = true; Debug.Log("SLOP_CITY_HINT_CHECK_PASSED");
            }
            catch (Exception error) { report.errors.Add(error.ToString()); throw; }
            finally
            {
                if (fixture) UnityEngine.Object.DestroyImmediate(fixture);
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)); File.WriteAllText(ReportPath,JsonUtility.ToJson(report,true));
            }
        }

        private static void CheckReusedTargetGuidance(Receipt report, Transform fixture)
        {
            // No Initialize/action calls: this focused scheduling fixture never reads or writes saves.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var root = new GameObject("Reused_target_guidance"); root.transform.SetParent(fixture);
            var sandbox = root.AddComponent<SlopCitySandbox>();
            var trail = root.AddComponent<SlopCityRabbitTrail>();
            var device = new GameObject("Shared broadcast device"); device.transform.SetParent(fixture);
            var target = device.AddComponent<SlopCityInteractable>();
            target.Configure(SlopCityInteractionKind.Screen, "Shared broadcast device");
            Type stepType = typeof(SlopCitySandbox).GetNestedType("ContractStep", BindingFlags.NonPublic);
            var steps = (IList)typeof(SlopCitySandbox).GetField("steps", flags).GetValue(sandbox);
            foreach (string instruction in new[] { "Diagnose the screen", "Retune the same screen" })
            {
                object step = Activator.CreateInstance(stepType, true);
                stepType.GetField("site").SetValue(step, target);
                stepType.GetField("instruction").SetValue(step, instruction);
                steps.Add(step);
            }
            typeof(SlopCitySandbox).GetField("currentJobId", flags).SetValue(sandbox, "same-device-service");
            trail.Present(sandbox, null, true);
            typeof(SlopCityRabbitTrail).GetField("target", flags).SetValue(trail, target);
            MethodInfo refresh = typeof(SlopCityRabbitTrail).GetMethod("RefreshObjective", flags);
            refresh.Invoke(trail, null);
            float transmissionDeadline = Time.time + 22f, routeDeadline = Time.time + 6f;
            typeof(SlopCityRabbitTrail).GetField("nextTransmissionAt", flags).SetValue(trail, transmissionDeadline);
            typeof(SlopCityRabbitTrail).GetField("nextRouteAt", flags).SetValue(trail, routeDeadline);
            refresh.Invoke(trail, null);
            Check(report,(float)typeof(SlopCityRabbitTrail).GetField("nextTransmissionAt", flags).GetValue(trail) == transmissionDeadline,
                "An unchanged stage preserves the subtle transmission interval.");
            typeof(SlopCitySandbox).GetField("stepIndex", flags).SetValue(sandbox, 1);
            refresh.Invoke(trail, null);
            Check(report,(float)typeof(SlopCityRabbitTrail).GetField("nextTransmissionAt", flags).GetValue(trail) == 0f
                && (int)typeof(SlopCityRabbitTrail).GetField("proximity", flags).GetValue(trail) == -1,
                "A new action at the same device immediately schedules fresh guidance.");
            Check(report,(float)typeof(SlopCityRabbitTrail).GetField("nextRouteAt", flags).GetValue(trail) == routeDeadline,
                "A same-device stage change preserves the existing rabbit route.");
            typeof(SlopCityRabbitTrail).GetField("nextTransmissionAt", flags).SetValue(trail, transmissionDeadline);
            typeof(SlopCitySandbox).GetField("currentJobId", flags).SetValue(sandbox, "new-dispatch-same-device");
            refresh.Invoke(trail, null);
            Check(report,(float)typeof(SlopCityRabbitTrail).GetField("nextTransmissionAt", flags).GetValue(trail) == 0f,
                "A new dispatch refreshes guidance even when its device and instruction are unchanged.");
        }

        private static void RunRoute(SlopCityHintRoute route,Vector3 from,Vector3 to,Transform user,Transform target)
        {
            route.Begin(from,to,user,target);
            for (int i = 0; i < 1000 && !route.Finished; i++) route.Step(128);
            if (!route.Finished) throw new InvalidOperationException("Hint search did not finish within its bounded fixture budget.");
        }
        private static void CheckSegment(SlopCityHintRoute route,Vector3 a,Vector3 b)
        { if (!route.ClearWalk(a,b)) throw new InvalidOperationException("The selected route contains a blocked walking segment."); }
        private static void Check(Receipt report,bool condition,string message)
        { if (!condition) throw new InvalidOperationException(message); report.checks.Add(message); }
        private static GameObject Box(Transform parent,string name,Vector3 position,Vector3 size)
        {
            GameObject value = GameObject.CreatePrimitive(PrimitiveType.Cube); value.name = name;
            value.transform.SetParent(parent); value.transform.position = position; value.transform.localScale = size; return value;
        }
    }
}
#endif
