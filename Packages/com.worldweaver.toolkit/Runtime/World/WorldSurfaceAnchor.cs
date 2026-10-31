using UnityEngine;
namespace WorldWeaver
{
    /// <summary>Explicit persistent ground clearance; no anchoring of unrelated manually placed objects.</summary>
    public sealed class WorldSurfaceAnchor : MonoBehaviour
    {
        public Terrain Terrain;
        public float Offset;
        public bool AboveWater;
        public float SurfaceHeight()
        {
            if(!Terrain||!Terrain.terrainData)return transform.position.y-Offset;
            float ground=Terrain.SampleHeight(transform.position)+Terrain.transform.position.y;
            var generated=Terrain.GetComponent<GeneratedTerrain>();
            return AboveWater&&generated&&generated.Specification!=null?Mathf.Max(ground,Terrain.transform.position.y+generated.Specification.waterLevel*generated.Specification.height):ground;
        }
        public void Reanchor() { if(!Terrain)return;var position=transform.position;position.y=SurfaceHeight()+Offset;transform.position=position; }
    }
}
