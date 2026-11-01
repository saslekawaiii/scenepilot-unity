using UnityEngine;
namespace WorldWeaver
{
    /// <summary>One explicit scene reference; generated UI does not invoke arbitrary method names.</summary>
    public sealed class WorldUIButton : MonoBehaviour
    {
        public GameObject Target;
        public void Toggle(){if(Target)Target.SetActive(!Target.activeSelf);}
    }
}
