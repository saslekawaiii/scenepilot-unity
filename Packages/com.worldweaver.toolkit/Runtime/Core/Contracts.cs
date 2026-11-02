using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WorldWeaver
{
    /// <summary>Independent inference routes. Validation always runs deterministically.</summary>
    public enum ModelTask { Terrain, Dialogue, Planning }
    public enum ProviderKind { Groq, OpenAICompatible, Ollama, Pollinations }

    public sealed class ChatMessage
    {
        public readonly string Role;
        public readonly string Content;
        public readonly IReadOnlyList<string> Images;
        public ChatMessage(string role, string content, IEnumerable<string> images = null)
        { Role = role; Content = content; Images = images == null ? new List<string>() : new List<string>(images); }
    }

    public sealed class CompletionRequest
    {
        public readonly List<ChatMessage> Messages = new List<ChatMessage>();
        public JObject Schema;
        public bool Stream;
        /// <summary>Editor-agent function calls; ordinary runtime structured requests keep their existing protocol.</summary>
        public bool UseToolCalls;
        public Action<string> OnContentDelta;
        public Action<string> OnActivity;
    }

    public sealed class CompletionResult
    {
        public string Text, Provider, Model, FinishReason;
        public string WireResponse;
        /// <summary>The provider explicitly reported an output-token limit; Text remains the exact partial response for audit.</summary>
        public bool OutputTruncated;
        public static bool IsOutputLimit(string reason) => string.Equals(reason, "length", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reason, "max_tokens", StringComparison.OrdinalIgnoreCase) || string.Equals(reason, "max_output_tokens", StringComparison.OrdinalIgnoreCase);
        public long ElapsedMs;
        public long FirstTokenMs;
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
        public readonly string WireResponse;
        public ProviderException(string message, int status = 0, bool transient = false, string wireResponse = null) : base(message)
        { Status = status; Transient = transient; WireResponse=wireResponse; }
    }
}
