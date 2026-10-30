using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WorldWeaver
{
    /// <summary>Independent inference routes. Validation always runs deterministically.</summary>
    public enum ModelTask { Terrain, Dialogue, Planning }
    public enum ProviderKind { Groq, OpenAICompatible, Ollama }

    public sealed class ChatMessage
    {
        public readonly string Role;
        public readonly string Content;
        public ChatMessage(string role, string content) { Role = role; Content = content; }
    }

    public sealed class CompletionRequest
    {
        public readonly List<ChatMessage> Messages = new List<ChatMessage>();
        public JObject Schema;
    }

    public sealed class CompletionResult
    {
        public string Text, Provider, Model;
        public long ElapsedMs;
        public int InputTokens, OutputTokens, Attempts;
        public double TokensPerSecond;
    }

    /// <summary>Implement this interface to add an inference backend without changing NPC code.</summary>
    public interface ILLMProvider : IDisposable
    {
        Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken);
        Task<bool> HealthAsync(CancellationToken cancellationToken);
    }

    public interface IEmbeddingProvider : IDisposable
    {
        string Identity { get; }
        Task<float[]> EmbedAsync(string text, bool query, CancellationToken cancellationToken);
    }

    public sealed class ProviderException : Exception
    {
        public readonly int Status;
        public readonly bool Transient;
        public ProviderException(string message, int status = 0, bool transient = false) : base(message)
        { Status = status; Transient = transient; }
    }
}
