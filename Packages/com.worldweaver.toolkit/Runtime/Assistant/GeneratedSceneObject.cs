using UnityEngine;
namespace WorldWeaver
{
    /// <summary>Explicit ownership of an assistant-created object; names never confer ownership.</summary>
    [DisallowMultipleComponent]
    public sealed class GeneratedSceneObject : MonoBehaviour
    {
        public string Kind;
        public string BatchId;
        public bool SurfaceAnchored;
        public string Style;
    }
}
