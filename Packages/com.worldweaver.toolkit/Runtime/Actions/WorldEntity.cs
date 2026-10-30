using UnityEngine;
namespace WorldWeaver
{
    /// <summary>Only registered entities can be referenced by model actions.</summary>
    public sealed class WorldEntity : MonoBehaviour
    {
        public string Id;
        public bool Interactable;
        public int InteractionCount { get; private set; }
        public void Interact() { InteractionCount++; }
    }
}
