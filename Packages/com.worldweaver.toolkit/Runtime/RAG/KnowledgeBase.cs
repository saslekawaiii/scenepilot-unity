using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WorldWeaver
{
    public enum MemoryKind { StaticKnowledge, Episodic, Conversation }
    [Serializable]
    public sealed class KnowledgeDocument
    {
        public string Id, Scope, Title;
        [TextArea(3, 15)] public string Text;
        public MemoryKind Kind;
    }

    /// <summary>Authored TXT/MD content, never credentials or provider responses containing keys.</summary>
    [CreateAssetMenu(menuName = "WorldWeaver/Knowledge Base")]
    public sealed class KnowledgeBase : ScriptableObject
    {
        public List<KnowledgeDocument> Documents = new List<KnowledgeDocument>();
    }

    public sealed class KnowledgeChunk
    {
        public string Id, DocumentId, Scope, Title, Text;
        public MemoryKind Kind;
        public int Offset;
        public float[] Vector;
        public float Score;
    }

    public static class TextChunker
    {
        /// <summary>Fixed character window with overlap. Identifiers include scope and content hash.</summary>
        public static List<KnowledgeChunk> Split(KnowledgeDocument document, int size = 500, int overlap = 80)
        {
            if (size < 32 || overlap < 0 || overlap >= size) throw new ArgumentException("Invalid chunk window.");
            if (document == null || string.IsNullOrWhiteSpace(document.Scope)) throw new ArgumentException("Knowledge requires a character scope.");
            var result = new List<KnowledgeChunk>();
            string text = (document.Text ?? "").Replace("\r\n", "\n");
            for (int start = 0; start < text.Length; start += size - overlap)
            {
                string part = text.Substring(start, Math.Min(size, text.Length - start));
                if (!string.IsNullOrWhiteSpace(part))
                    result.Add(new KnowledgeChunk { Id = Digest(document.Scope + "\n" + document.Id + "\n" + start + "\n" + part),
                        DocumentId = document.Id, Scope = document.Scope, Title = document.Title, Text = part, Kind = document.Kind, Offset = start });
                if (start + size >= text.Length) break;
            }
            return result;
        }
        public static string Digest(string text)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>Replaceable vector backend. Scope filtering must precede scoring.</summary>
    public interface IVectorStore
    {
        void Upsert(KnowledgeChunk chunk);
        List<KnowledgeChunk> Search(float[] query, string scope, MemoryKind kind, int topK);
        void RemoveDocument(string documentId, string scope);
        void Clear();
        int Count { get; }
    }

    public sealed class InMemoryVectorStore : IVectorStore
    {
        private readonly Dictionary<string, KnowledgeChunk> chunks = new Dictionary<string, KnowledgeChunk>();
        private int dimensions;
        public int Count => chunks.Count;
        public void Upsert(KnowledgeChunk chunk)
        {
            if (chunk.Vector == null || chunk.Vector.Length == 0) throw new ArgumentException("Missing vector.");
            if (dimensions != 0 && chunk.Vector.Length != dimensions) throw new ArgumentException("Embedding dimensions changed. Reindex required.");
            dimensions = chunk.Vector.Length;
            chunks[chunk.Id] = chunk;
        }
        public List<KnowledgeChunk> Search(float[] query, string scope, MemoryKind kind, int topK)
        {
            if (topK < 1 || topK > 20) throw new ArgumentException("topK must be between 1 and 20.");
            if (dimensions != 0 && query.Length != dimensions) throw new ArgumentException("Embedding dimensions differ.");
            return chunks.Values.Where(c => c.Scope == scope && c.Kind == kind)
                .Select(c => new KnowledgeChunk { Id = c.Id, DocumentId = c.DocumentId, Scope = c.Scope, Title = c.Title,
                    Text = c.Text, Kind = c.Kind, Offset = c.Offset, Score = Cosine(query, c.Vector) })
                .OrderByDescending(c => c.Score).ThenBy(c => c.Id, StringComparer.Ordinal).Take(topK).ToList();
        }
        public static float Cosine(float[] a, float[] b)
        {
            if (a.Length != b.Length) throw new ArgumentException("Vector dimensions differ.");
            double dot = 0, aa = 0, bb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (float.IsNaN(a[i]) || float.IsInfinity(a[i]) || float.IsNaN(b[i]) || float.IsInfinity(b[i])) throw new ArgumentException("Non-finite vector.");
                dot += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i];
            }
            return aa == 0 || bb == 0 ? 0 : (float)(dot / Math.Sqrt(aa * bb));
        }
        public void RemoveDocument(string documentId, string scope)
        { foreach (var key in chunks.Where(kv => kv.Value.DocumentId == documentId && kv.Value.Scope == scope).Select(kv => kv.Key).ToArray()) chunks.Remove(key); }
        public void Clear() { chunks.Clear(); dimensions = 0; }
    }

    /// <summary>Per-NPC bounded memory, semantic retrieval and atomic static reindexing.</summary>
    public sealed class RagIndex : IDisposable
    {
        private readonly IEmbeddingProvider embedding;
        private IVectorStore store;
        private readonly Func<IVectorStore> storeFactory;
        private readonly Queue<KnowledgeDocument> memory = new Queue<KnowledgeDocument>();
        public int Count => store.Count;
        public RagIndex(IEmbeddingProvider embedding, Func<IVectorStore> storeFactory = null)
        {
            this.embedding = embedding; this.storeFactory = storeFactory ?? (() => new InMemoryVectorStore()); store = this.storeFactory();
        }
        public async Task ReindexAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken token)
        {
            var next = storeFactory();
            foreach (var doc in documents.Concat(memory))
                foreach (var chunk in TextChunker.Split(doc))
                {
                    token.ThrowIfCancellationRequested();
                    chunk.Vector = await embedding.EmbedAsync(chunk.Text, false, token); next.Upsert(chunk);
                }
            token.ThrowIfCancellationRequested(); store = next;
        }
        public async Task<List<KnowledgeChunk>> RetrieveAsync(string text, string scope, int topK, CancellationToken token)
        {
            var vector = await embedding.EmbedAsync(text, true, token);
            var result = store.Search(vector, scope, MemoryKind.StaticKnowledge, topK);
            result.AddRange(store.Search(vector, scope, MemoryKind.Episodic, 2));
            result.AddRange(store.Search(vector, scope, MemoryKind.Conversation, 2));
            return result;
        }
        public async Task RememberAsync(string scope, string text, MemoryKind kind, CancellationToken token)
        {
            if (kind == MemoryKind.StaticKnowledge) throw new ArgumentException("Use reindex for static knowledge.");
            var doc = new KnowledgeDocument { Id = Guid.NewGuid().ToString("N"), Scope = scope, Title = kind.ToString(), Kind = kind,
                Text = text.Substring(0, Math.Min(text.Length, 900)) };
            var chunks = TextChunker.Split(doc);
            foreach (var chunk in chunks) chunk.Vector = await embedding.EmbedAsync(chunk.Text, false, token);
            token.ThrowIfCancellationRequested();
            foreach (var chunk in chunks) store.Upsert(chunk);
            memory.Enqueue(doc);
            while (memory.Count > 32) { var expired = memory.Dequeue(); store.RemoveDocument(expired.Id, expired.Scope); }
        }
        public void Clear() { store.Clear(); memory.Clear(); }
        public void Dispose() { embedding.Dispose(); }
    }
}
