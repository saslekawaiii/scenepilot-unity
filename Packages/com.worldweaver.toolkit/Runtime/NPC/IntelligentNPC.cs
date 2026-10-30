using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

namespace WorldWeaver
{
    public enum NpcState { Idle, Thinking, Moving, Following, Waiting, Failed }
    /// <summary>Personality authoring, cancellable dialogue and cooldown-limited high-level planning.</summary>
    [RequireComponent(typeof(Inventory))]
    public sealed class IntelligentNPC : MonoBehaviour
    {
        public string CharacterId = "merchant";
        public string NpcName = "Мирон";
        public string Role = "Торговец";
        [TextArea] public string Personality = "Жадный, осторожный, но способен на сочувствие.";
        [TextArea] public string Biography = "Живёт в деревне Лунный Брод и торгует кристаллами.";
        [TextArea] public string Goals = "Беречь запасы и торговать. После торговли осмотреть House.";
        [TextArea] public string Constraints = "Не отдавать больше одного кристалла за один разговор. Не брать вещи без разрешения.";
        public KnowledgeBase Knowledge;
        public OrchestratorProfile Models;
        public List<WorldEntity> KnownEntities = new List<WorldEntity>();
        public MonoBehaviour[] ContextSources = Array.Empty<MonoBehaviour>();
        public string[] AllowedActions = { "Talk", "Wait", "GiveItem", "MoveTo", "Follow", "Flee", "Interact" };
        [Range(1, 20)] public int TopK = 3;
        [Range(1, 100)] public int MaxTransferPerDecision = 1;
        [Range(1, 20)] public float InteractionDistance = 5;
        public bool AllowTakingItems;
        public bool UseRag = true;
        public bool Autonomous;
        [Range(10, 300)] public float PlanningCooldown = 45;
        public NpcState State { get; private set; }
        public string LastSpeech { get; private set; } = "Готов к разговору.";
        public string LastError { get; private set; }
        public bool Busy { get; private set; }
        private NpcPipeline pipeline;
        private CancellationTokenSource lifetime;
        private WorldEntity follow;
        private float nextPlan, nextFollow;

        private void OnEnable() { lifetime = new CancellationTokenSource(); nextPlan = Time.unscaledTime + PlanningCooldown; }
        private void OnDisable() { lifetime?.Cancel(); if (!Busy) ReleaseServices(); }
        private void OnDestroy() { lifetime?.Cancel(); if (!Busy) ReleaseServices(); }
        private NpcPipeline Services
        {
            get
            {
                if (pipeline == null)
                {
                    if (!Models) throw new InvalidOperationException("Assign an Orchestrator Profile.");
                    pipeline = new NpcPipeline(new ModelRouter(Models), new RagIndex(new OllamaEmbeddingProvider(Models.EmbeddingEndpoint, Models.EmbeddingModel)), new ActionDispatcher());
                }
                return pipeline;
            }
        }
        public Task<PipelineTrace> TalkAsync(string input, CancellationToken token = default) => RunAsync(input, ModelTask.Dialogue, token);
        public Task<PipelineTrace> PlanAsync(CancellationToken token = default) => RunAsync("Выбери следующее действие для своей цели с учётом текущего мира.", ModelTask.Planning, token);
        private async Task<PipelineTrace> RunAsync(string input, ModelTask task, CancellationToken token)
        {
            if (Busy) throw new InvalidOperationException("NPC already has an active request.");
            Busy = true; LastError = null; State = NpcState.Thinking;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime?.Token ?? CancellationToken.None))
            {
                try
                {
                    var trace = await Services.RunAsync(this, input, task, UseRag, linked.Token);
                    LastSpeech = trace.Speech;
                    if (State == NpcState.Thinking) State = NpcState.Idle;
                    return trace;
                }
                catch (OperationCanceledException) { State = NpcState.Idle; LastError = "Cancelled."; throw; }
                catch (Exception error) { State = NpcState.Failed; LastError = error is ProviderException || error is ArgumentException ? error.Message : error.GetType().Name; throw; }
                finally { Busy = false; nextPlan = Time.unscaledTime + PlanningCooldown; if (!this || !isActiveAndEnabled) ReleaseServices(); }
            }
        }
        private async void Update()
        {
            var agent = GetComponent<NavMeshAgent>();
            if (follow && Time.unscaledTime >= nextFollow && agent && agent.isOnNavMesh)
            {
                nextFollow = Time.unscaledTime + .5f;
                if (NavMesh.SamplePosition(follow.transform.position, out var hit, 3, agent.areaMask)) agent.SetDestination(hit.position);
            }
            if (State == NpcState.Moving && agent && agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + .1f) State = NpcState.Idle;
            if (Autonomous && !Busy && Time.unscaledTime >= nextPlan && State != NpcState.Moving && State != NpcState.Following)
            { nextPlan = Time.unscaledTime + PlanningCooldown; try { await PlanAsync(); } catch (Exception) { /* Failure is retained in LastError and TraceLog. */ } }
        }
        public void BeginMovement(WorldEntity target, string action)
        {
            var agent = GetComponent<NavMeshAgent>();
            follow = action == "Follow" ? target : null;
            NavMesh.SamplePosition(WorldAction.Destination(this, target, action), out var hit, 3, agent.areaMask);
            agent.isStopped = false; agent.SetDestination(hit.position);
            State = action == "Follow" ? NpcState.Following : NpcState.Moving;
        }
        public void StopMovement()
        { follow = null; var agent = GetComponent<NavMeshAgent>(); if (agent && agent.isOnNavMesh) { agent.ResetPath(); agent.isStopped = true; } State = NpcState.Waiting; }
        public void ClearMemory() { if (!Busy) pipeline?.ClearMemory(); }
        public async Task ReindexAsync(CancellationToken token) { if (Busy) throw new InvalidOperationException("NPC is busy."); await Services.ReindexAsync(this, token); }
        public void ReleaseServices() { pipeline?.Dispose(); pipeline = null; }
    }
}
