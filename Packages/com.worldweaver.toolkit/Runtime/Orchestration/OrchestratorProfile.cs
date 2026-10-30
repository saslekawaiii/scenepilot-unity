using System;
using UnityEngine;
namespace WorldWeaver
{
    [CreateAssetMenu(menuName = "WorldWeaver/Orchestrator Profile")]
    public sealed class OrchestratorProfile : ScriptableObject
    {
        public ModelProfile Terrain, Dialogue, Planning;
        public string EmbeddingEndpoint = "http://127.0.0.1:11434";
        public string EmbeddingModel = "embeddinggemma";
        public ModelProfile Resolve(ModelTask task)
        {
            var profile = task == ModelTask.Terrain ? Terrain : task == ModelTask.Planning ? Planning : Dialogue;
            if (!profile) throw new InvalidOperationException("Missing route for " + task);
            return profile;
        }
    }
}
