#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopCity.Editor
{
    /// <summary>Persisted scene checks for the expanded skyline, safe lift endpoints and subway circulation.</summary>
    public static class SlopCityMetropolisValidation
    {
        public const string ReportPath = "Builds/Reports/metropolis-validation.json";

        public static void ValidateSavedScene()
        {
            Scene scene=EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath,OpenSceneMode.Single);
            SlopCityValidationReport report=Validate(scene);
            string path=Path.GetFullPath(ReportPath); Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,JsonUtility.ToJson(report,true));
            foreach(string warning in report.warnings) Debug.LogWarning("SLOP_CITY_METROPOLIS "+warning);
            report.ThrowIfInvalid();
            Debug.Log("SLOP_CITY_METROPOLIS_VALIDATION_SUCCEEDED "+report.checks+" checks; "+report.warnings.Count+" warnings; "+path);
        }

        public static SlopCityValidationReport Validate(Scene scene)
        {
            var report=new SlopCityValidationReport {unityVersion=Application.unityVersion,checkedAtUtc=DateTime.UtcNow.ToString("O"),scenePath=scene.path};
            Transform[] transforms=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<Transform>(true)).ToArray();
            SlopCityInteractable[] actions=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<SlopCityInteractable>(true)).ToArray();
            CharacterController player=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<CharacterController>(true)).FirstOrDefault();
            Physics.SyncTransforms();
            ValidatePersistedEmission(report,"Assets/Materials/SlopCityCity/Metro_CurtainWallGlass.mat",true);
            ValidatePersistedEmission(report,"Assets/Materials/SlopCityCity/Metro_WarmLighting.mat",false);
            Transform[] towers=transforms.Where(t=>t.name.StartsWith("City_Skyscraper_")).ToArray();
            Transform[] skyline=transforms.Where(t=>t.name.StartsWith("Skyline_Tower_")).ToArray();
            Transform[] citadels=transforms.Where(t=>t.name=="MilitarySF_CommunicationsCitadel").ToArray();
            SlopCityBuildingInterior[] interiors=transforms.Select(t=>t.GetComponent<SlopCityBuildingInterior>()).Where(item=>item).ToArray();
            report.Check(towers.Length>=23,"Expected at least 23 accessible skyscrapers; found "+towers.Length+".");
            report.Check(skyline.Length>=28,"Expected at least 28 background skyline towers; found "+skyline.Length+".");
            foreach(Transform tower in towers)
            {
                SlopCityBuildingInterior interior=tower.GetComponent<SlopCityBuildingInterior>();
                if(interior)
                {
                    report.Check(interior.IsTower && interior.FloorCount>2,
                        "Retrofitted tower requires occupied stories and a physical elevator: "+tower.name);
                    report.Check(interior.LandingFeet(interior.FloorCount-1).y-interior.LandingFeet(0).y>=35,
                        "Retrofitted tower must retain its full-height rooftop destination: "+tower.name);
                    continue;
                }
                Transform core=tower.Find("Structural_Core");
                report.Check(core && core.GetComponent<Renderer>().bounds.size.y>=35,"Tower must rise at least 35m: "+tower.name);
                report.Check(tower.GetComponentsInChildren<SlopCityInteractable>().Count(item=>item.Kind==SlopCityInteractionKind.Building && item.Destination)==2,
                    "Tower requires working street and roof lift controls: "+tower.name);
            }
            if(interiors.Length>0) ValidateRuntimeInteriors(interiors,towers,skyline,citadels,report);
            foreach(SlopCityDistrict district in scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<SlopCityDistrict>()))
                report.Check(towers.Any(tower=>tower.IsChildOf(district.transform)),"District has no skyscraper: "+district.DisplayName);
            ValidateMediaCoverage(report,transforms);
            report.Check(transforms.Count(t=>t.name.StartsWith("GasPump_"))>=2,"Two physical gas pumps are required.");
            report.Check(transforms.Count(t=>t.name.StartsWith("Subway_Station_"))==2,"Two physical subway stations are required.");
            report.Check(transforms.Count(t=>t.name.StartsWith("Subway_Train_"))==2,"Two boardable train carriages are required.");

            foreach(SlopCityInteractable car in actions.Where(item=>item.Kind==SlopCityInteractionKind.Car))
            {
                Collider[] body=car.GetComponentsInChildren<Collider>().Where(collider=>collider.enabled&&!collider.isTrigger).ToArray();
                Bounds envelope=new Bounds(car.transform.position,Vector3.zero);
                foreach(Collider solid in body) envelope.Encapsulate(solid.bounds);
                report.Check(body.Length>=5 && envelope.size.y>1.3f,"Car collision envelope must include body, wheels and roof: "+car.name);
                Vector3 eye=car.transform.TransformPoint(new Vector3(-.22f,1.25f,-.1f));
                report.Check(!Physics.OverlapSphere(eye,.09f,~0,QueryTriggerInteraction.Ignore).Any(collider=>collider.transform.IsChildOf(car.transform)),
                    "Driver camera is inside solid car geometry: "+car.name);
                Vector3[] exits={new Vector3(-1.6f,0,0),new Vector3(1.6f,0,0),new Vector3(0,0,-2.7f),new Vector3(0,0,2.7f)};
                report.Check(exits.Any(offset=>TryFeet(car.transform.TransformPoint(offset),player,out _)),"Parked car has no supported capsule-clear exit: "+car.name);
            }

            foreach(SlopCityInteractable action in actions.Where(item=>item.Destination))
            {
                SlopCityElevatorButton physical=action.GetComponent<SlopCityElevatorButton>();
                if(interiors.Length>0 && physical)
                {
                    report.Check(physical.Action==SlopCityElevatorButtonKind.Call,
                        "Retrofitted route controls must call a physical cabin: "+action.name);
                    // Old Destination markers describe job arrival, not a standing platform while
                    // the physical car is elsewhere. All actual landings are checked by the focused fixture.
                    report.Check(!action.ObjectiveEligible,
                        "Physical route controls must not generate routine delivery objectives: "+action.name);
                    continue;
                }
                bool supported=TryFeet(action.Destination.position,player,out Vector3 feet);
                report.Check(supported,"Lift or train destination lacks supported, capsule-clear feet: "+action.name+" -> "+action.Destination.name+" at "+action.Destination.position);
                report.Check(!action.ObjectiveEligible,"Transit controls must not generate unreachable delivery objectives: "+action.name);
            }
            foreach(Transform station in transforms.Where(t=>t.name.StartsWith("Subway_Station_")))
            {
                Transform platform=station.Cast<Transform>().FirstOrDefault(t=>t.name.StartsWith("Metro_Platform_"));
                SlopCityInteractable trainControl=station.GetComponentsInChildren<SlopCityInteractable>().FirstOrDefault(item=>item.name=="Train_Departure_Control");
                SlopCityInteractable exit=station.GetComponentsInChildren<SlopCityInteractable>().FirstOrDefault(item=>item.name=="Street_Lift_Control");
                report.Check(platform && trainControl && exit,"Station requires platform, departure control and street lift: "+station.name);
                if(platform && trainControl && exit)
                {
                    List<Vector3> reachable=FloodPlatform(station,platform.position,player);
                    report.Check(reachable.Any(point=>ReachableControl(point,trainControl,player,2.0f)),"No physically walkable platform-to-train-control route: "+station.name);
                    report.Check(reachable.Any(point=>ReachableControl(point,exit,player,2.0f)),"No physically walkable platform-to-street-lift route: "+station.name);
                    report.Check(reachable.Any(point=>point.z>station.position.z+2.2f),"Station cannot board the carriage through its center doors: "+station.name);
                }
            }
            // Venue doors are usable obstacles. Test dispatch paths with their physical leaves in
            // the authored open pose, keeping collision enabled and restoring every pose afterward.
            SlopCityDoor[] doors=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<SlopCityDoor>(true)).Where(door=>door.Leaf).ToArray();
            Quaternion[] closedPoses=doors.Select(door=>door.Leaf.localRotation).ToArray();
            try
            {
                for(int i=0;i<doors.Length;i++)
                {
                    var serialized=new SerializedObject(doors[i]);
                    float angle=serialized.FindProperty("openAngle").floatValue;
                    Quaternion closed=serialized.FindProperty("closedLocalRotation").quaternionValue;
                    doors[i].Leaf.localRotation=closed*Quaternion.Euler(0,angle,0);
                }
                Physics.SyncTransforms();
                var graph=new SlopCityWalkGraph(player); Vector3 start=new Vector3(8,0,0); graph.Build(start);
                foreach(SlopCityInteractable item in actions.Where(item=>item.ObjectiveEligible && !item.Destination && item.InteractionPoint.y>=-1 && item.InteractionPoint.y<4))
                {
                    bool reachable=false;
                    for(int ring=0;ring<5&&!reachable;ring++) for(int angle=0;angle<24&&!reachable;angle++)
                    {
                        float radians=angle*Mathf.PI/12, radius=.55f+ring*.60f;
                        Vector3 candidate=item.InteractionPoint+new Vector3(Mathf.Cos(radians)*radius,0,Mathf.Sin(radians)*radius); candidate.y=0;
                        if(graph.TryFeet(candidate,out Vector3 feet) && ReachableControl(feet,item,player,3.4f) && graph.Route(start,feet)!=null) reachable=true;
                    }
                    report.Check(reachable,"No ground-supported capsule route reaches dispatch objective within 3.4m: "
                        +HierarchyPath(item.transform)+" at "+item.InteractionPoint+" ("+item.DisplayName+"). Doors were physically open during this check.");
                }
            }
            finally
            {
                for(int i=0;i<doors.Length;i++) doors[i].Leaf.localRotation=closedPoses[i];
                Physics.SyncTransforms();
            }
            report.passed=report.errors.Count==0;
            return report;
        }

        private static void ValidateRuntimeInteriors(SlopCityBuildingInterior[] interiors,Transform[] towers,Transform[] skyline,Transform[] citadels,SlopCityValidationReport report)
        {
            var ids=new HashSet<string>();
            foreach(SlopCityBuildingInterior interior in interiors)
            {
                report.Check(!string.IsNullOrEmpty(interior.BuildingId) && ids.Add(interior.BuildingId),
                    "Runtime building identities must be unique and stable: "+interior.name);
                report.Check(interior.FloorCount>0 && interior.LocalFloorElevations.Length==interior.FloorCount,
                    "Runtime floor elevations must cover every declared floor: "+interior.BuildingId);
                for(int floor=1;floor<interior.FloorCount;floor++)
                    report.Check(interior.LocalFloorElevations[floor]>interior.LocalFloorElevations[floor-1],
                        "Runtime floor elevations must ascend: "+interior.BuildingId+" floor "+floor);
                if(interior.IsTower && interior.Elevator)
                    report.Check(interior.Elevator.FloorCount==interior.FloorCount,
                        "Physical elevator must serve every declared floor including the roof: "+interior.BuildingId);
                else if(!interior.IsTower) report.Check(interior.PreservedInterior && interior.FloorCount==1,
                    "Authored home/venue interiors must be preserved: "+interior.BuildingId);
            }
            foreach(Transform tower in towers.Concat(skyline).Concat(citadels))
                report.Check(tower.GetComponent<SlopCityBuildingInterior>(),
                    "Every skyline and district tower requires a real interior descriptor: "+tower.name);
            report.Check(interiors.Count(interior=>interior.IsTower)>=towers.Length+skyline.Length+citadels.Length,
                "Runtime interiors must cover the complete visible tower population.");
            report.Check(interiors.Count(interior=>interior.PreservedInterior)>=7,
                "Runtime coverage must preserve all six venues and the spawn home.");
            report.warnings.Add("Runtime descriptors enumerate floors; interiors-elevators-check.json supplies sequential generated-floor physics and moving-elevator verification.");
        }

        private static void ValidatePersistedEmission(SlopCityValidationReport report,string path,bool requiresMap)
        {
            // ValidateSavedScene opens the serialized scene in a fresh batch process;
            // loading its material asset here checks the saved/imported shader state.
            Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
            report.Check(material,"Missing saved emissive material: "+path);
            if(!material) return;
            report.Check(material.IsKeywordEnabled("_EMISSION"),"Saved material lost Standard's _EMISSION keyword after import: "+path);
            report.Check((material.globalIlluminationFlags&MaterialGlobalIlluminationFlags.EmissiveIsBlack)==0,
                "Saved emissive material still has EmissiveIsBlack and can be stripped on shader validation: "+path);
            report.Check(material.GetColor("_EmissionColor").maxColorComponent>.1f,"Saved emissive material has a black emission color: "+path);
            if(requiresMap) report.Check(material.GetTexture("_EmissionMap"),"Curtain-wall glass lost its illuminated-window atlas: "+path);
        }

        private static void ValidateMediaCoverage(SlopCityValidationReport report,Transform[] transforms)
        {
            Transform[] anchors=transforms.Where(item=>item.name.StartsWith("SlopCityMediaScreenAnchor_Channel",StringComparison.Ordinal)).ToArray();
            report.Check(anchors.Length>=48,"Expected at least 48 native media anchors across the expanded metropolis; found "+anchors.Length+".");
            int billboards=anchors.Count(item=>HasAncestor(item,"City_Billboard_")||HasAncestor(item,"Japan_VideoBillboard_"));
            report.Check(billboards>=36,"Expanded metropolis requires at least 36 video billboards; found "+billboards+".");
            report.Check(anchors.Count(item=>HasAncestor(item,"Parked_CityCar"))>=4,"Car dashboards lost native video anchors.");
            report.Check(anchors.Count(item=>HasAncestor(item,"GasPump_"))>=4,"Both faces of the gas pumps require native advertising anchors.");
            report.Check(anchors.Count(item=>HasAncestor(item,"Subway_"))>=4,"Subway platforms and carriages require native video anchors.");
            foreach(Transform anchor in anchors)
            {
                Vector3 scale=anchor.lossyScale;
                report.Check(scale.x>.1f&&scale.y>.1f&&!float.IsInfinity(scale.x)&&!float.IsInfinity(scale.y),
                    "Media anchor has a nonphysical display size: "+HierarchyPath(anchor));
                report.Check(anchor.name.EndsWith("Channel0",StringComparison.Ordinal)||anchor.name.EndsWith("Channel1",StringComparison.Ordinal)
                    ||anchor.name.EndsWith("Channel2",StringComparison.Ordinal),
                    "Authored media channel must be supported by native discovery: "+HierarchyPath(anchor));
            }
        }

        private static bool HasAncestor(Transform item,string prefix)
        {
            for(Transform cursor=item;cursor;cursor=cursor.parent)
                if(cursor.name.StartsWith(prefix,StringComparison.Ordinal)) return true;
            return false;
        }

        private static string HierarchyPath(Transform item)
        {
            string path=item.name;
            while(item.parent) {item=item.parent;path=item.name+"/"+path;}
            return path;
        }
        private static bool TryFeet(Vector3 hint,CharacterController player,out Vector3 feet)
        {
            feet=hint;
            RaycastHit[] floor=Physics.RaycastAll(hint+Vector3.up*.55f,Vector3.down,1.15f,~0,QueryTriggerInteraction.Ignore)
                .Where(hit=>Relevant(hit.collider,player)&&hit.normal.y>.7f).OrderBy(hit=>hit.distance).ToArray();
            if(floor.Length==0) return false;
            feet=floor[0].point+Vector3.up*.025f;
            if(Mathf.Abs(feet.y-hint.y)>.35f) return false;
            return !Physics.OverlapCapsule(feet+Vector3.up*.31f,feet+Vector3.up*1.44f,.25f,~0,QueryTriggerInteraction.Ignore).Any(collider=>Relevant(collider,player));
        }
        private static bool Relevant(Collider collider,CharacterController player)
        {return collider && collider!=player && (!player || !collider.transform.IsChildOf(player.transform));}
        private static bool ReachableControl(Vector3 feet,SlopCityInteractable control,CharacterController player,float range)
        {
            Vector3 eye=feet+Vector3.up*1.62f, delta=control.InteractionPoint-eye;
            if(delta.magnitude>range) return false;
            return !Physics.RaycastAll(eye,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore).Any(hit=>Relevant(hit.collider,player)
                && !hit.collider.transform.IsChildOf(control.transform) && hit.distance<delta.magnitude-.08f);
        }
        private static List<Vector3> FloodPlatform(Transform station,Vector3 start,CharacterController player)
        {
            const int width=29,height=21; var points=new Vector3[width*height]; var valid=new bool[points.Length];
            for(int z=0;z<height;z++) for(int x=0;x<width;x++)
            {
                int index=z*width+x;
                valid[index]=TryFeet(new Vector3(station.position.x-7+x*.5f,start.y,station.position.z-5+z*.5f),player,out points[index]);
            }
            int source=-1; float closest=float.PositiveInfinity;
            for(int i=0;i<points.Length;i++) if(valid[i] && (points[i]-start).sqrMagnitude<closest) {source=i;closest=(points[i]-start).sqrMagnitude;}
            var result=new List<Vector3>(); if(source<0) return result;
            var visited=new HashSet<int>();var queue=new Queue<int>();visited.Add(source);queue.Enqueue(source);
            int[] dx={1,-1,0,0},dz={0,0,1,-1};
            while(queue.Count>0)
            {
                int here=queue.Dequeue();result.Add(points[here]);
                for(int direction=0;direction<4;direction++)
                {
                    int x=here%width+dx[direction],z=here/width+dz[direction];
                    if(x<0||x>=width||z<0||z>=height) continue;
                    int next=z*width+x;if(!valid[next]||visited.Contains(next)||Mathf.Abs(points[here].y-points[next].y)>.30f) continue;
                    Vector3 from=points[here];from.y=Mathf.Max(from.y,points[next].y);
                    Vector3 delta=points[next]-points[here];delta.y=0;
                    if(Physics.CapsuleCastAll(from+Vector3.up*.53f,from+Vector3.up*1.44f,.25f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore).Any(hit=>Relevant(hit.collider,player))) continue;
                    visited.Add(next);queue.Enqueue(next);
                }
            }
            return result;
        }

        public static void Capture()
        {
            EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath,OpenSceneMode.Single);
            var owner=new GameObject("Metropolis_ReviewCamera");Camera camera=owner.AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.Skybox;camera.backgroundColor=new Color(.12f,.17f,.22f);
            camera.nearClipPlane=.04f;camera.farClipPlane=400;camera.fieldOfView=67;camera.allowHDR=true;
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../renders"));Directory.CreateDirectory(folder);
            CaptureFrame(camera,new Vector3(20,1.75f,-12),new Vector3(-6,26,35),Path.Combine(folder,"09-metropolis-street.png"));
            Transform roof=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t=>t.name.StartsWith("Tower_Roof_")).OrderByDescending(t=>t.position.y).FirstOrDefault();
            if(roof) CaptureFrame(camera,roof.position+Vector3.up*1.66f,new Vector3(-38,34,-55),Path.Combine(folder,"10-metropolis-rooftop.png"));
            CaptureFrame(camera,new Vector3(-49,-6.2f,15.9f),new Vector3(-46.3f,-6.25f,23.5f),Path.Combine(folder,"11-metropolis-subway.png"));
            CaptureFrame(camera,new Vector3(-14,1.7f,-45),new Vector3(-9,1.5f,-39),Path.Combine(folder,"12-metropolis-fuel.png"));
            UnityEngine.Object.DestroyImmediate(owner);Debug.Log("SLOP_CITY_METROPOLIS_CAPTURES_COMPLETE "+folder);
        }
        private static void CaptureFrame(Camera camera,Vector3 position,Vector3 target,string path)
        {
            camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position,Vector3.up));
            var buffer=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32);
            var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);RenderTexture previous=RenderTexture.active;
            try {camera.targetTexture=buffer;camera.Render();RenderTexture.active=buffer;image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally {camera.targetTexture=null;RenderTexture.active=previous;buffer.Release();UnityEngine.Object.DestroyImmediate(buffer);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
#endif
