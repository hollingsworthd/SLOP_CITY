using System;
using System.Collections.Generic;
using UnityEngine;
namespace SlopCity
{
    /// <summary>Building lifecycle bridge. No floor is retained or loaded merely to present a resident.</summary>
    public static class SlopCityInteriorPopulation
    {
        public static void RegisterVenue(SlopCityBuildingInterior owner)=>AdmitFloor(owner,0,owner?owner.transform:null);
        public static void AdmitFloor(SlopCityBuildingInterior owner,int floor,Transform root)
        {if(SlopCityCrowd.Instance)SlopCityCrowd.Instance.AdmitInteriorFloor(owner,floor,root);}
        public static void RetireFloor(SlopCityBuildingInterior owner,int floor)
        {if(SlopCityCrowd.Instance)SlopCityCrowd.Instance.RetireInteriorFloor(owner,floor);}
    }
    public sealed partial class SlopCityCrowd
    {
        public const int MaximumInteriorNodeProbes=96,MaximumInteriorEdgeChecks=256;
        public int LastInteriorNodeProbes {get;private set;}
        public int LastInteriorEdgeChecks {get;private set;}
        public void AdmitInteriorFloor(SlopCityBuildingInterior building,int floor,Transform root)
        {
            if(!expandedPopulation||!building||!root||!homeLookup.TryGetValue(HomeKey(building,floor),out int homeIndex))return;
            InteriorHome home=interiorHomes[homeIndex];if(home.root==root&&home.route!=null)return;
            Physics.SyncTransforms();LastInteriorNodeProbes=LastInteriorEdgeChecks=0;
            SlopCityCrowdRoute graph=BuildInteriorRoute(building,floor,root);
            home.root=root;home.route=graph;AdmittedInteriorRouteCount=0;foreach(var h in interiorHomes)if(h.route!=null)AdmittedInteriorRouteCount++;
            if(graph==null)return;
            for(int index=population.Length;index<residents.Length;index++)
            {
                Resident r=residents[index];if(r.home!=homeIndex)continue;
                if(residentSlots[index]>=0){RetireResident(residentSlots[index]);r=residents[index];}
                r.node=Mathf.Abs(r.seed.residentId)%graph.nodes.Length;int[] links=graph.nodes[r.node].neighbors;
                r.next=links[(int)((uint)r.seed.residentId%(uint)links.Length)];r.position=graph.nodes[r.node].position;r.lastUpdate=simulationTime;
                residents[index]=r;
            }
        }
        public void RetireInteriorFloor(SlopCityBuildingInterior building,int floor)
        {
            if(!expandedPopulation||!building||!homeLookup.TryGetValue(HomeKey(building,floor),out int homeIndex))return;
            InteriorHome home=interiorHomes[homeIndex];home.route=null;home.root=null;
            for(int slot=activeCount-1;slot>=0;slot--)if(residents[agents[slot].residentIndex].home==homeIndex)RetireResident(slot);
            AdmittedInteriorRouteCount=0;foreach(var h in interiorHomes)if(h.route!=null)AdmittedInteriorRouteCount++;
        }
        private SlopCityCrowdRoute BuildInteriorRoute(SlopCityBuildingInterior building,int floor,Transform root)
        {
            var points=new List<Vector3>(MaximumInteriorNodeProbes);var markers=new List<Transform>();
            foreach(Transform marker in root.GetComponentsInChildren<Transform>(true))
                if(marker.name.StartsWith("Room_Circulation_Point_",StringComparison.Ordinal)||marker.name=="Marker_Corridor")markers.Add(marker);
            if(markers.Count==0)return null;
            for(int marker=0;marker<markers.Count&&LastInteriorNodeProbes<MaximumInteriorNodeProbes;marker++)
                for(int z=-1;z<=1&&LastInteriorNodeProbes<MaximumInteriorNodeProbes;z++)
                    for(int x=-1;x<=1&&LastInteriorNodeProbes<MaximumInteriorNodeProbes;x++)
                    {
                        LastInteriorNodeProbes++;Vector3 hint=markers[marker].position+building.transform.TransformVector(new Vector3(x*.8f,0,z*.8f));
                        Vector3 local=building.transform.InverseTransformPoint(hint);
                        // Never make the lift or its vertical aperture an occupied route node.
                        if(!building.PreservedInterior&&Mathf.Abs(local.x-building.ShaftLocalCenter.x)<1.5f&&Mathf.Abs(local.z-building.ShaftLocalCenter.z)<1.5f)continue;
                        if(!TryPopulationFeet(hint,out Vector3 feet))continue;
                        bool duplicate=false;foreach(Vector3 other in points)if((other-feet).sqrMagnitude<.1f){duplicate=true;break;}
                        if(!duplicate)points.Add(feet);
                    }
            if(points.Count<2)return null;
            var links=new List<int>[points.Count];for(int i=0;i<links.Length;i++)links[i]=new List<int>(4);
            for(int a=0;a<points.Count;a++)for(int b=a+1;b<points.Count;b++)
            {
                Vector3 delta=points[b]-points[a];if(delta.sqrMagnitude>1.2f*1.2f||Mathf.Abs(delta.y)>.12f)continue;
                if(LastInteriorEdgeChecks>=MaximumInteriorEdgeChecks)continue;LastInteriorEdgeChecks++;
                if(!ClearPopulationEdge(points[a],points[b]))continue;links[a].Add(b);links[b].Add(a);
            }
            // Closed room doors are genuine boundaries: use one supported connected component, never hop rooms.
            var seen=new bool[points.Count];var best=new List<int>();var queue=new Queue<int>();
            for(int start=0;start<points.Count;start++)
            {
                if(seen[start]||links[start].Count==0)continue;var component=new List<int>();seen[start]=true;queue.Enqueue(start);
                while(queue.Count>0){int n=queue.Dequeue();component.Add(n);foreach(int next in links[n])if(!seen[next]){seen[next]=true;queue.Enqueue(next);}}
                if(component.Count>best.Count)best=component;
            }
            if(best.Count<2)return null;int[] map=new int[points.Count];Array.Fill(map,-1);for(int i=0;i<best.Count;i++)map[best[i]]=i;
            var nodes=new SlopCityCrowdNode[best.Count];for(int i=0;i<best.Count;i++){int source=best[i];int[] neighbors=new int[links[source].Count];for(int edge=0;edge<neighbors.Length;edge++)neighbors[edge]=map[links[source][edge]];nodes[i]=new SlopCityCrowdNode{position=points[source],neighbors=neighbors};}
            return new SlopCityCrowdRoute{label=building.BuildingId+" / floor "+floor+" / interior circulation",districtIndex=-1,nodes=nodes};
        }
        private bool ClearPopulationEdge(Vector3 from,Vector3 to)
        {
            Vector3 delta=to-from;float distance=delta.magnitude;if(distance<.01f)return false;
            for(int step=1;step<=3;step++){Vector3 hint=Vector3.Lerp(from,to,step/4f);if(!TryPopulationFeet(hint,out Vector3 feet)||Mathf.Abs(feet.y-hint.y)>.1f)return false;}
            int count=Physics.CapsuleCastNonAlloc(from+Vector3.up*.34f,from+Vector3.up*1.64f,.32f,delta/distance,sweepHits,distance,~0,QueryTriggerInteraction.Ignore);
            if(count==sweepHits.Length)return false;CharacterController controller=playerController?playerController.GetComponent<CharacterController>():null;
            for(int i=0;i<count;i++)if(LayoutCollider(sweepHits[i].collider,controller))return false;return true;
        }
    }
}
