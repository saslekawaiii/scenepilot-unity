using UnityEngine;

namespace WorldWeaver
{
    public sealed class WorldResourceNode : MonoBehaviour
    {
        public bool IsSample;
        public float Oxygen=35;
        public bool Collected;
        public bool Collect(WorldSurvival survival)
        {
            if(!survival || Collected || !survival.State.Alive || Vector3.Distance(transform.position,survival.transform.position)>3)return false;
            Collected=true;
            if(IsSample)survival.State.Samples++;else survival.State.Supply(Oxygen);
            foreach(var renderer in GetComponentsInChildren<Renderer>())renderer.enabled=false;
            foreach(var collider in GetComponentsInChildren<Collider>())collider.enabled=false;
            return true;
        }
    }
}
