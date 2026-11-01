using UnityEngine;

namespace WorldWeaver
{
    /// <summary>A serialized star mesh follows the viewing camera without gameplay collision.</summary>
    [ExecuteAlways]
    public sealed class WorldStarfield : MonoBehaviour
    {
        public int StarCount;
        public int Seed;
        void LateUpdate()
        {
            Camera camera=Camera.main;
            if(!camera||!camera.isActiveAndEnabled)
                foreach(var candidate in Camera.allCameras)if(candidate.isActiveAndEnabled&&candidate.gameObject.scene==gameObject.scene){camera=candidate;break;}
            if(camera)transform.position=camera.transform.position;
        }
    }
}
