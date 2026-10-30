using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WorldWeaver
{
    /// <summary>Explicit task routing. Never silently replaces a failed provider with a mock.</summary>
    public interface IModelRouter : IDisposable
    {
        Task<CompletionResult> CompleteAsync(ModelTask task, CompletionRequest request, CancellationToken token);
    }
    public sealed class ModelRouter : IModelRouter
    {
        private readonly OrchestratorProfile routes;
        private readonly Dictionary<ModelProfile, ILLMProvider> providers = new Dictionary<ModelProfile, ILLMProvider>();
        public ModelRouter(OrchestratorProfile routes) { this.routes = routes; }
        public async Task<CompletionResult> CompleteAsync(ModelTask task, CompletionRequest request, CancellationToken token)
        {
            var profile = routes.Resolve(task);
            if (!providers.TryGetValue(profile, out var provider)) providers.Add(profile, provider = profile.CreateProvider());
            return await provider.CompleteAsync(request, token);
        }
        public void Dispose() { foreach (var provider in providers.Values) provider.Dispose(); providers.Clear(); }
    }
}
