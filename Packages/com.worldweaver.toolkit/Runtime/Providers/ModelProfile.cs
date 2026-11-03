using System;
using UnityEngine;

namespace WorldWeaver
{
    public enum OpenAIResponseMode { JsonObject, JsonSchema, Text }
    /// <summary>Public connection parameters. Only the NAME of an environment variable is serialized.</summary>
    [CreateAssetMenu(menuName = "WorldWeaver/Model Profile")]
    public sealed class ModelProfile : ScriptableObject
    {
        public ProviderKind Provider = ProviderKind.Ollama;
        public string Endpoint = "http://127.0.0.1:11434";
        public string Model = "qwen3.5:2b";
        public string ApiKeyEnvironmentVariable = "";
        [Range(1, 300)] public int TimeoutSeconds = 90;
        [Range(0, 3)] public int Retries = 1;
        [Range(0, 2)] public float Temperature = 0;
        [Range(64, 8192)] public int MaxTokens = 768;
        [Range(1024, 32768)] public int ContextWindow = 4096;
        public bool StrictSchema = true;
        [Tooltip("Allow provider-default reasoning for Ollama cloud models. Local models keep the bounded non-thinking path.")]
        public bool EnableCloudReasoning;
        public OpenAIResponseMode ResponseMode = OpenAIResponseMode.JsonObject;

        public void Validate()
        {
            if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Use HTTPS or loopback HTTP, without credentials, query or fragment.");
            if (string.IsNullOrWhiteSpace(Model) || Model.Length > 200) throw new ArgumentException("Model ID is required.");
            if(!Enum.IsDefined(typeof(OpenAIResponseMode),ResponseMode))throw new ArgumentException("Invalid response mode.");
            if (TimeoutSeconds < 1 || TimeoutSeconds > 300 || Retries < 0 || Retries > 3 ||
                MaxTokens < 64 || MaxTokens > 8192 || ContextWindow < 1024 || ContextWindow > 32768 || float.IsNaN(Temperature) || Temperature < 0 || Temperature > 2)
                throw new ArgumentException("Invalid model limits.");
            if (ApiKeyEnvironmentVariable.Length > 128 || ApiKeyEnvironmentVariable.Contains(" "))
                throw new ArgumentException("Specify an environment variable name, never an API key.");
            if (ApiKeyEnvironmentVariable.StartsWith("sk-", StringComparison.Ordinal) || ApiKeyEnvironmentVariable.StartsWith("gsk_", StringComparison.Ordinal))
                throw new ArgumentException("An API key must never be saved in a profile.");
        }

        public ILLMProvider CreateProvider()
        {
            Validate();
            return Provider == ProviderKind.Ollama ? (ILLMProvider)new LocalProvider(this) : new OpenAICompatibleProvider(this);
        }
    }

}
