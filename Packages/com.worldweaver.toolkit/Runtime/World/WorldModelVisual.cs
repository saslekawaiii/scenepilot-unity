using UnityEngine;
namespace WorldWeaver
{
    /// <summary>Visual ownership is separate from an actor's identity, controls and memory.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldModelVisual:MonoBehaviour
    {
        public Transform Visual;
        public string SourcePath;
    }
}
