using System;
using System.Collections.Generic;
using System.Linq;
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
        public Texture2D Portrait;
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
        public string[] PlanningActions = { "MoveTo", "Follow", "Flee", "Wait", "Interact" };
        [Range(1, 20)] public int TopK = 3;
        [Range(1, 100)] public int MaxTransferPerDecision = 1;
        [Range(1, 20)] public float InteractionDistance = 5;
        public bool AllowTakingItems;
        public bool UseRag = true;
        public bool Autonomous;
        [SerializeField] private bool aiEnabled = true;
        public bool AiEnabled
        {
            get => aiEnabled;
            set { aiEnabled=value; if(!value) { activeRequest?.Cancel(); GetComponent<NpcSocialAutonomy>()?.CancelConversation(); SetStatus("ИИ отключён"); } else SetStatus("Готов к разговору"); }
        }
        public bool PlayerConversationActive { get; private set; }
        public string AiStatus { get; private set; } = "Готов к разговору";
        public event Action<NpcUtterance> SpeechReceived;
        public event Action<string> AiStatusChanged;
        [SerializeField] private List<NpcUtterance> conversationHistory = new List<NpcUtterance>();
        public IReadOnlyList<NpcUtterance> ConversationHistory => conversationHistory.Select(line=>line.Copy()).ToList();
        public List<NpcUtterance> ExportConversation() => conversationHistory.Select(line=>line.Copy()).ToList();
        public static bool ValidConversation(IEnumerable<NpcUtterance> saved)
        {
            var rows=(saved??Array.Empty<NpcUtterance>()).ToList();
            return rows.Count<=32 && rows.All(row=>row!=null&&!string.IsNullOrWhiteSpace(row.Id)&&row.Id.Length<=64&&!string.IsNullOrWhiteSpace(row.SpeakerId)&&row.SpeakerId.Length<=160&&row.SpeakerName!=null&&row.SpeakerName.Length<=160&&row.TargetId!=null&&row.TargetId.Length<=160&&!string.IsNullOrWhiteSpace(row.Text)&&row.Text.Length<=3000&&row.Timestamp!=null&&row.Timestamp.Length<=40&&DateTime.TryParse(row.Timestamp,out _)&&!(row.IsPlayer&&row.IsAi)) && rows.Select(row=>row.Id).Distinct().Count()==rows.Count;
        }
        public void RestoreConversation(IEnumerable<NpcUtterance> saved)
        {
            if(Busy)throw new InvalidOperationException("NPC is busy.");
            var rows=(saved??Array.Empty<NpcUtterance>()).ToList();
            if(!ValidConversation(rows)||rows.Any(line=>line.SpeakerId!=CharacterId&&line.TargetId!=CharacterId))throw new ArgumentException("Invalid scoped NPC transcript.");
            conversationHistory=rows.Select(line=>line.Copy()).ToList();
        }
        public void ObserveUtterance(NpcUtterance line)
        {
            if(!ValidConversation(new[]{line})||(line.SpeakerId!=CharacterId&&line.TargetId!=CharacterId))throw new ArgumentException("Invalid scoped NPC utterance.");
            if(conversationHistory.Any(old=>old.Id==line.Id))return;
            conversationHistory.Add(line.Copy()); while(conversationHistory.Count>32)conversationHistory.RemoveAt(0);
            try { SpeechReceived?.Invoke(line.Copy()); } catch(Exception error) { Debug.LogWarning("NPC dialogue listener failed: "+error.GetType().Name); }
        }
        void RecordUtterance(string speakerId,string speakerName,string target,string text,bool isPlayer,bool isAi)
        {
            if(string.IsNullOrWhiteSpace(text))return;
            ObserveUtterance(new NpcUtterance { Id=Guid.NewGuid().ToString("N"),SpeakerId=Limit(speakerId,160),SpeakerName=Limit(speakerName,160),TargetId=Limit(target,160),Text=Limit(text,3000),Timestamp=DateTime.UtcNow.ToString("O"),IsPlayer=isPlayer,IsAi=isAi });
        }
        static string Limit(string value,int length)=>(value??"").Substring(0,Math.Min(length,(value??"").Length));
        void SetStatus(string status) { AiStatus=status; try { AiStatusChanged?.Invoke(status); } catch(Exception error) { Debug.LogWarning("NPC status listener failed: "+error.GetType().Name); } }
        public void BeginPlayerConversation()
        {
            PlayerConversationActive=true; if(runningAmbient)activeRequest?.Cancel(); NpcInferenceScheduler.PrioritizePlayer(); GetComponent<NpcSocialAutonomy>()?.CancelConversation();
            HaltNavigation(GetComponent<NavMeshAgent>());
        }
        internal static void HaltNavigation(NavMeshAgent agent)
        {
            if(!agent||!agent.enabled||!agent.isOnNavMesh)return;
            agent.ResetPath();agent.isStopped=true;
            // Also clear residual simulation velocity so a conversation pause does not coast along the old path.
            agent.velocity=Vector3.zero;agent.nextPosition=agent.transform.position;
        }
        public void EndPlayerConversation() { PlayerConversationActive=false; if(Busy&&!runningAmbient)activeRequest?.Cancel(); nextPlan=Time.unscaledTime+PlanningCooldown; }

        [Range(10, 300)] public float PlanningCooldown = 45;
        public NpcState State { get; private set; }
        public string LastSpeech { get; private set; } = "Готов к разговору.";
        public string LastError { get; private set; }
        public bool Busy { get; private set; }
        [SerializeField] private List<KnowledgeDocument> persistentMemory = new List<KnowledgeDocument>();
        public List<KnowledgeDocument> ExportMemory() => pipeline != null ? pipeline.ExportMemory() : new List<KnowledgeDocument>(persistentMemory);
        public void RestoreMemory(IEnumerable<KnowledgeDocument> saved)
        {
            if (Busy) throw new InvalidOperationException("NPC is busy.");
            var next = new List<KnowledgeDocument>(saved ?? Array.Empty<KnowledgeDocument>());
            if (next.Count > 32 || next.Exists(d => d == null || d.Scope != CharacterId || d.Kind == MemoryKind.StaticKnowledge || !Enum.IsDefined(typeof(MemoryKind),d.Kind) || d.Text == null || d.Text.Length > 900 || string.IsNullOrWhiteSpace(d.Id))) throw new ArgumentException("Invalid scoped NPC memory.");
            persistentMemory = next; pipeline?.RestoreMemory(next,CharacterId);
        }
        public void RememberEvent(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var next = ExportMemory(); next.Add(new KnowledgeDocument { Id=Guid.NewGuid().ToString("N"), Scope=CharacterId,Title="Событие",Kind=MemoryKind.Episodic,Text=text.Substring(0,Math.Min(900,text.Length)) });
            while (next.Count > 32) next.RemoveAt(0); RestoreMemory(next);
        }
        private NpcPipeline pipeline;
        private CancellationTokenSource lifetime, activeRequest;
        private bool runningAmbient;
        private WorldEntity follow;
        private float nextPlan, nextFollow;

        private void OnEnable() { lifetime?.Dispose(); lifetime = new CancellationTokenSource(); nextPlan = Time.unscaledTime + PlanningCooldown; }
        private void OnDisable() { PlayerConversationActive=false; GetComponent<NpcSocialAutonomy>()?.CancelConversation(); lifetime?.Cancel(); activeRequest?.Cancel(); if (!Busy) ReleaseServices(); }
        private void OnDestroy() { lifetime?.Cancel(); lifetime?.Dispose(); lifetime=null; if (!Busy) ReleaseServices(); }
        private NpcPipeline Services
        {
            get
            {
                if (pipeline == null)
                {
                    if (!Models) throw new InvalidOperationException("Assign an Orchestrator Profile.");
                    pipeline = new NpcPipeline(new ModelRouter(Models), new RagIndex(new OllamaEmbeddingProvider(Models.EmbeddingEndpoint, Models.EmbeddingModel)), new ActionDispatcher());
                    pipeline.RestoreMemory(persistentMemory,CharacterId);
                }
                return pipeline;
            }
        }
        /// <summary>Allows a host to supply an inference backend. Ownership transfers to this NPC; no mock fallback is created.</summary>
        public void ConfigureInference(IModelRouter router,IEmbeddingProvider embedding)
        {
            if(router==null||embedding==null)throw new ArgumentNullException(router==null?nameof(router):nameof(embedding));
            if(Busy)throw new InvalidOperationException("NPC is busy.");
            ReleaseServices(); pipeline=new NpcPipeline(router,new RagIndex(embedding),new ActionDispatcher()); pipeline.RestoreMemory(persistentMemory,CharacterId);
        }
        public Task<PipelineTrace> TalkAsync(string input, CancellationToken token = default) => SpeakToPlayerAsync(input,token);
        public async Task<PipelineTrace> SpeakToPlayerAsync(string input,CancellationToken token=default)
        {
            ValidateInput(input); NpcInferenceScheduler.PrioritizePlayer();
            if(Busy&&runningAmbient)
            {
                activeRequest?.Cancel();
                while(Busy) { token.ThrowIfCancellationRequested(); if(!this||!isActiveAndEnabled)throw new OperationCanceledException(); await Task.Yield(); }
            }
            return await RunAsync(input,ModelTask.Dialogue,false,new NpcDialogueContext {SpeakerId="Player",SpeakerName="Вы",IsPlayer=true},token);
        }
        public Task<PipelineTrace> SpeakToNpcAsync(IntelligentNPC partner,string input,CancellationToken token=default)
        {
            if(!partner||!partner.isActiveAndEnabled||!partner.AiEnabled)throw new InvalidOperationException("Собеседник недоступен.");
            ValidateInput(input);
            return RunAsync(input,ModelTask.Dialogue,true,new NpcDialogueContext {SpeakerId=partner.CharacterId,SpeakerName=partner.NpcName,IsPlayer=false},token);
        }
        public Task<PipelineTrace> PlanAsync(CancellationToken token = default) => RunAsync("Выбери следующее действие для своей цели с учётом текущего мира.", ModelTask.Planning,true,null,token);
        void ValidateInput(string input)
        {
            if(!AiEnabled)throw new InvalidOperationException("ИИ этого персонажа отключён. Включите ИИ в настройках NPC.");
            if(!isActiveAndEnabled)throw new InvalidOperationException("Персонаж недоступен.");
            if(string.IsNullOrWhiteSpace(input)||input.Length>3000)throw new ArgumentException("Введите от 1 до 3000 символов.");
        }
        private async Task<PipelineTrace> RunAsync(string input, ModelTask task,bool ambient,NpcDialogueContext dialogue,CancellationToken token)
        {
            ValidateInput(input);
            if(Busy)throw new InvalidOperationException("NPC already has an active request.");
            if(ambient&&PlayerConversationActive)throw new OperationCanceledException("Игрок разговаривает с персонажем.");
            Busy=true; runningAmbient=ambient; LastError=null; State=NpcState.Thinking;
            using(var linked=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime?.Token??CancellationToken.None))
            {
                activeRequest=linked;
                try
                {
                    SetStatus(ambient?"Фоновый разговор ожидает модель…":"Ожидаю локальную модель…");
                    using(var lease=await NpcInferenceScheduler.AcquireAsync(ambient,linked.Token))
                    {
                        lease.Token.ThrowIfCancellationRequested();
                        if(!this||!isActiveAndEnabled||!AiEnabled||(ambient&&PlayerConversationActive))throw new OperationCanceledException();
                        if(dialogue!=null&&dialogue.IsPlayer)RecordUtterance(dialogue.SpeakerId,dialogue.SpeakerName,CharacterId,input,true,false);
                        SetStatus("Обдумывает ответ…");
                        // Ambient turns use bounded lexical knowledge and recent memory, avoiding a second embedding model swap on small machines.
                        var trace=await Services.RunAsync(this,input,task,UseRag&&!ambient,lease.Token,true,dialogue,()=>lease.TryReserveRepair());
                        // Validation may already have committed before cancellable memory indexing. Retain that evidence,
                        // but a closed/disabled conversation must never publish a new utterance or a fresh ready status.
                        if(lease.Token.IsCancellationRequested||!this||!isActiveAndEnabled||!AiEnabled)
                        {
                            trace.ReplySuppressed=true;
                            trace.Error=string.IsNullOrEmpty(trace.Error)?"Decision finished; reply publication cancelled.":trace.Error+" Reply publication cancelled.";
                            LastError="Cancelled after decision; validated effects are retained.";
                            if(State==NpcState.Thinking)State=NpcState.Idle;
                            if(this&&isActiveAndEnabled)SetStatus(AiEnabled?"Ответ отменён":"ИИ отключён");
                            return trace;
                        }
                        if(!string.IsNullOrWhiteSpace(trace.Speech))LastSpeech=trace.Speech;
                        if(dialogue!=null)RecordUtterance(CharacterId,NpcName,dialogue.SpeakerId,trace.Speech,false,trace.Validation!=null&&trace.Validation.Accepted);
                        if(State==NpcState.Thinking)State=NpcState.Idle;
                        SetStatus(trace.Validation!=null&&trace.Validation.Accepted?"Ответ готов":"Действие отклонено проверкой");
                        return trace;
                    }
                }
                catch(OperationCanceledException) { State=NpcState.Idle; LastError="Cancelled."; SetStatus(AiEnabled?"Ответ отменён":"ИИ отключён"); throw; }
                catch(Exception error)
                {
                    State=NpcState.Failed; LastError=error is ProviderException||error is ArgumentException||error is InvalidOperationException?error.Message:error.GetType().Name;
                    SetStatus("ИИ недоступен: "+LastError); throw;
                }
                finally
                {
                    if(pipeline!=null)persistentMemory=pipeline.ExportMemory(); activeRequest=null; Busy=false; runningAmbient=false; nextPlan=Time.unscaledTime+PlanningCooldown;
                    if(this&&isActiveAndEnabled)SetStatus(AiStatus);
                    if(!this||!isActiveAndEnabled)ReleaseServices();
                }
            }
        }
        private async void Update()
        {
            if(!AiEnabled&&Busy&&activeRequest!=null&&!activeRequest.IsCancellationRequested)activeRequest.Cancel();
            var agent = GetComponent<NavMeshAgent>();
            if (!PlayerConversationActive && !(GetComponent<NpcSocialAutonomy>()?.HoldsMovement??false) && follow && Time.unscaledTime >= nextFollow && agent && agent.isOnNavMesh)
            {
                nextFollow = Time.unscaledTime + .5f;
                if (NavMesh.SamplePosition(follow.transform.position, out var hit, 3, agent.areaMask)) agent.SetDestination(hit.position);
            }
            if (State == NpcState.Moving && agent && agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + .1f) State = NpcState.Idle;
            if (AiEnabled && Autonomous && !Busy && !PlayerConversationActive && !(GetComponent<NpcSocialAutonomy>()?.HoldsMovement??false) && Time.unscaledTime >= nextPlan && State != NpcState.Moving && State != NpcState.Following)
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
        { follow = null; HaltNavigation(GetComponent<NavMeshAgent>()); State = NpcState.Waiting; }
        public void ClearMemory() { if (!Busy) { persistentMemory.Clear(); pipeline?.ClearMemory(); } }
        public async Task ReindexAsync(CancellationToken token) { if (Busy) throw new InvalidOperationException("NPC is busy."); await Services.ReindexAsync(this, token); }
        public void ReleaseServices() { if (pipeline != null) persistentMemory = pipeline.ExportMemory(); pipeline?.Dispose(); pipeline = null; }
    }
}
