using System;
using System.Linq;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Shared XZ occupancy for procedural structures and their approaches.
    /// Does not move explicitly requested cave coordinates or delete obstacles.</summary>
    internal static class WorldSitePlacement
    {
        internal static Rect CaveFootprint(Vector3 at,float width,float length)=>new Rect(at.x-width/2-2,at.z-4,width+4,length+6);
        static Rect Footprint(Bounds bounds)=>Rect.MinMaxRect(bounds.min.x,bounds.min.z,bounds.max.x,bounds.max.z);
        internal static bool Clear(Terrain terrain,Rect footprint,Transform ignore,out string obstruction,bool includeCaves=true)
        {
            var origin=terrain.transform.position;var size=terrain.terrainData.size;
            if(footprint.xMin<origin.x||footprint.xMax>origin.x+size.x||footprint.yMin<origin.z||footprint.yMax>origin.z+size.z){obstruction="terrain boundary";return false;}
            var root=terrain.transform.parent;Physics.SyncTransforms();
            if(includeCaves)foreach(var cave in root.GetComponentsInChildren<WorldCave>())
                if((!ignore||!cave.transform.IsChildOf(ignore))&&footprint.Overlaps(CaveFootprint(cave.transform.position,cave.Size.x,cave.Size.z))){obstruction="cave approach: "+cave.name;return false;}
            foreach(var collider in root.GetComponentsInChildren<Collider>())
            {
                if(!collider.enabled||collider.isTrigger||collider is TerrainCollider||!collider.gameObject.activeInHierarchy||
                    ignore&&collider.transform.IsChildOf(ignore)||collider.GetComponentInParent<WorldPlayer>()||collider.GetComponentInParent<IntelligentNPC>()||collider.GetComponentInParent<WorldCave>())continue;
                if(footprint.Overlaps(Footprint(collider.bounds))){obstruction="physical structure: "+collider.name;return false;}
            }
            obstruction=null;return true;
        }
        internal static bool ActorClear(Terrain terrain,GameObject actor,Vector3 point)
        {
            var data=terrain.terrainData;var origin=terrain.transform.position;int n=data.holesResolution;
            int x=Mathf.Clamp(Mathf.FloorToInt((point.x-origin.x)/data.size.x*n),0,n-1),z=Mathf.Clamp(Mathf.FloorToInt((point.z-origin.z)/data.size.z*n),0,n-1);
            if(data.IsHole(x,z))return false;
            float radius=.38f*Mathf.Max(Mathf.Abs(actor.transform.lossyScale.x),Mathf.Abs(actor.transform.lossyScale.z));
            foreach(var cave in terrain.transform.parent.GetComponentsInChildren<WorldCave>())
                if(new Rect(point.x-radius,point.z-radius,radius*2,radius*2).Overlaps(CaveFootprint(cave.transform.position,cave.Size.x,cave.Size.z)))return false;
            float height=Mathf.Max(radius*2,1.9f*Mathf.Abs(actor.transform.lossyScale.y));
            return !Physics.OverlapCapsule(point+Vector3.up*(radius+.08f),point+Vector3.up*(height-radius),radius,~0,QueryTriggerInteraction.Ignore)
                .Any(c=>c.enabled&&c.gameObject.activeInHierarchy&&!(c is TerrainCollider)&&!c.transform.IsChildOf(actor.transform));
        }
        internal static Vector3 FindHabitatSite(Terrain terrain,Vector3 preferred,Transform ignore,GameObject occupant)
        {
            // The habitat has no caller-specified coordinates: choose the nearest
            // clear bounded site, preserving all existing structures and cave access.
            for(int ring=0;ring<=8;ring++)for(int direction=0;direction<(ring==0?1:16);direction++)
            {
                float angle=direction*Mathf.PI/8;var point=preferred+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(ring*12);
                if(!Clear(terrain,new Rect(point.x-5,point.z-7,10,12),ignore,out _))continue;
                point.y=terrain.SampleHeight(point)+terrain.transform.position.y;if(!ActorClear(terrain,occupant,point))continue;return point;
            }
            throw new ArgumentException("No clear grounded habitat site fits this world. Existing structures and cave approaches were preserved; choose a larger/open area.");
        }
    }
}
