using UnityEngine;
namespace WorldWeaver
{
    /// <summary>Compatibility bridge for saved demo scenes; presentation is shared with the playable world.</summary>
    public sealed class DemoDialogue : MonoBehaviour
    {
        public IntelligentNPC Merchant;
        public Inventory PlayerInventory;
        public OrchestratorProfile LocalRoutes, CloudRoutes;
        WorldConversationUI view;
        void Start()
        {
            if (!Merchant) return;
            var host = new GameObject("WorldWeaver · разговор"); host.transform.SetParent(transform, false);
            view = host.AddComponent<WorldConversationUI>(); view.PlayerInventory = PlayerInventory;
            view.Open(Merchant, PlayerInventory);
        }
        void OnDisable() { if (view) view.Close(); }
    }
}
