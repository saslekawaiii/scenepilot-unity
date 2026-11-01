using UnityEngine;

namespace WorldWeaver
{
    /// <summary>An accessible ramp and rock tube, with terrain holes and its own collision mesh.</summary>
    public sealed class WorldCave : MonoBehaviour
    {
        public Terrain Terrain;
        public Vector3 Entrance, Interior;
        public Vector3 Size = new Vector3(8,5,24);
        public bool Contains(Vector3 point)
        {
            var p=transform.InverseTransformPoint(point);
            return p.x>-Size.x*.45f && p.x<Size.x*.45f && p.z>5 && p.z<Size.z-1 && p.y>-4 && p.y<2;
        }
    }
}
