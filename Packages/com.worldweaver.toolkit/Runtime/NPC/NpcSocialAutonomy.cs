using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
namespace WorldWeaver
{
    /// <summary>Opt-in nearby NPC conversation. No generated sentence can transfer rewards or override a quest.</summary>
    [RequireComponent(typeof(IntelligentNPC))]
    public sealed class NpcSocialAutonomy : MonoBehaviour
    {
        [SerializeField] bool socialEnabled=true;
        public bool Enabled { get=>socialEnabled; set { socialEnabled=value; if(!value)CancelConversation(); } }
        public bool ApproachNearbyCharacters=true;
        [Range(5,40)] public float SearchRadius=20;
        [Range(2,25)] public float InteractionRadius=8;
        [Range(15,300)] public float CooldownSeconds=60;
        [Range(1,4)] public int MaxTurns=3;
        [TextArea] public string Topic="Обсудите текущие события и свои цели. Можете уважительно не соглашаться: у каждого собственные интересы.";
        public string Status {get;private set;}="Ожидание";
        public bool ConversationActive {get;private set;}
        public IntelligentNPC Partner {get;private set;}
        public bool HoldsMovement => ConversationActive || Time.unscaledTime<movementUntil;
        IntelligentNPC npc;
        CancellationTokenSource lifetime,conversation;
        float nextAttempt,movementUntil;
        bool approaching;
        static readonly HashSet<IntelligentNPC> reserved = new HashSet<IntelligentNPC>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetReservations()=>reserved.Clear();
        void OnEnable() { npc=GetComponent<IntelligentNPC>(); lifetime=new CancellationTokenSource(); nextAttempt=Time.unscaledTime+2+Mathf.Abs(GetInstanceID()%400)/100f; }
        public void RequestConversationSoon() { nextAttempt=Time.unscaledTime; }
        void OnDisable() { StopApproach(); lifetime?.Cancel(); conversation?.Cancel(); if(npc)reserved.Remove(npc); lifetime?.Dispose(); lifetime=null; }
        public void CancelConversation() { conversation?.Cancel(); StopApproach(); nextAttempt=Time.unscaledTime+Mathf.Clamp(CooldownSeconds,15,300); }
        async void Update()
        {
            if((!Enabled||!npc||!npc.isActiveAndEnabled||!npc.AiEnabled||npc.PlayerConversationActive)&&(ConversationActive||approaching))CancelConversation();
            if(!Application.isPlaying||!Enabled||!npc||!npc.isActiveAndEnabled||!npc.AiEnabled||!npc.Models||npc.Busy||npc.PlayerConversationActive||ConversationActive||reserved.Contains(npc)||Time.unscaledTime<nextAttempt)return;
            nextAttempt=Time.unscaledTime+Mathf.Clamp(CooldownSeconds,15,300);
            if(!NpcInferenceScheduler.AmbientBudgetAvailable) { Status="Лимит фоновых запросов"; return; }
            if(approaching&&Time.unscaledTime>=movementUntil) { StopApproach(); Status="Подход к собеседнику завершён по тайм-ауту"; return; }
            var candidate=FindPartner();
            if(!candidate)
            {
                if(approaching) { nextAttempt=Time.unscaledTime+2; return; }
                if(TryApproach())return;
                Status="Рядом нет свободного собеседника"; return;
            }
            approaching=false;
            try { await ConverseAsync(candidate,lifetime.Token); }
            catch(OperationCanceledException) { Status="Разговор прерван: приоритет игрока или персонаж недоступен"; }
            catch(Exception error) { Status="ИИ-разговор недоступен: "+(error is ProviderException||error is InvalidOperationException?error.Message:error.GetType().Name); }
        }
        IntelligentNPC[] Candidates()
        {
            var scope=GetComponentInParent<WorldSaveState>();
            return scope?scope.GetComponentsInChildren<IntelligentNPC>():FindObjectsByType<IntelligentNPC>(FindObjectsSortMode.None);
        }
        IntelligentNPC FindPartner()
        {
            return Candidates().Where(other=>CanConverse(other))
                .OrderBy(other=>Vector3.SqrMagnitude(other.transform.position-transform.position)).ThenBy(other=>other.CharacterId,StringComparer.Ordinal).FirstOrDefault();
        }
        bool Eligible(IntelligentNPC other)
        {
            if(!npc)npc=GetComponent<IntelligentNPC>();
            if(!other||other==npc||string.IsNullOrWhiteSpace(other.CharacterId)||other.CharacterId==npc.CharacterId||!other.isActiveAndEnabled||!other.AiEnabled||!other.Models||other.Busy||other.PlayerConversationActive||reserved.Contains(other))return false;
            var social=other.GetComponent<NpcSocialAutonomy>();
            if(!social||!social.isActiveAndEnabled||!social.Enabled||social.ConversationActive)return false;
            var world=GetComponentInParent<WorldSaveState>();var otherWorld=other.GetComponentInParent<WorldSaveState>();
            return world==otherWorld&&(world||transform.root==other.transform.root)&&gameObject.scene==other.gameObject.scene;
        }
        public bool CanConverse(IntelligentNPC other)
        {
            if(!Eligible(other))return false;
            var social=other.GetComponent<NpcSocialAutonomy>();
            if(Vector3.Distance(transform.position,other.transform.position)>Mathf.Min(Mathf.Clamp(InteractionRadius,2,25),Mathf.Clamp(social.InteractionRadius,2,25)))return false;
            var agent=GetComponent<NavMeshAgent>();
            // A baked navigation surface, when present, prevents conversation through disconnected rooms.
            if(agent&&agent.enabled&&agent.isOnNavMesh&&!WorldNavigation.TryPath(transform.position,other.transform.position,out _))return false;
            return true;
        }
        bool TryApproach()
        {
            var agent=GetComponent<NavMeshAgent>();
            if(!ApproachNearbyCharacters||!agent||!agent.enabled||!agent.isOnNavMesh)return false;
            foreach(var other in Candidates().Where(Eligible).Where(other=>Vector3.Distance(transform.position,other.transform.position)<=Mathf.Clamp(SearchRadius,5,40)).OrderBy(other=>Vector3.SqrMagnitude(other.transform.position-transform.position)))
            {
                var direction=transform.position-other.transform.position; direction.y=0;
                var destination=other.transform.position+(direction.sqrMagnitude>.01f?direction.normalized:Vector3.forward)*2;
                if(!WorldNavigation.TryPath(transform.position,destination,out var path))continue;
                agent.isStopped=false;agent.SetPath(path);approaching=true;movementUntil=Time.unscaledTime+12;nextAttempt=Time.unscaledTime+2;Status="Идёт поговорить с "+other.NpcName;return true;
            }
            return false;
        }
        void StopApproach()
        {
            if(approaching) { var agent=GetComponent<NavMeshAgent>(); if(agent&&agent.enabled&&agent.isOnNavMesh)agent.ResetPath(); }
            approaching=false;movementUntil=0;
        }
        /// <summary>Explicit API also used by acceptance tests; turn count, participants and world scope are revalidated.</summary>
        public async Task<IReadOnlyList<NpcUtterance>> ConverseAsync(IntelligentNPC other,CancellationToken token=default)
        {
            if(!npc)npc=GetComponent<IntelligentNPC>();
            if(!Enabled||!npc||!npc.AiEnabled||!npc.isActiveAndEnabled||npc.PlayerConversationActive||npc.Busy||ConversationActive||reserved.Contains(npc)||!CanConverse(other))throw new InvalidOperationException("Персонажи сейчас не могут разговаривать.");
            var peer=other.GetComponent<NpcSocialAutonomy>(); var result=new List<NpcUtterance>();
            using(var linked=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime?.Token??CancellationToken.None))
            {
                conversation=linked; peer.conversation=linked; ConversationActive=peer.ConversationActive=true; Partner=other; peer.Partner=npc;
                reserved.Add(npc); reserved.Add(other); approaching=peer.approaching=false; StopForConversation(npc); StopForConversation(other);
                try
                {
                    string input="Начни короткий разговор с "+other.NpcName+". Тема: "+(Topic??"").Substring(0,Math.Min(800,(Topic??"").Length));
                    var speaker=npc; var listener=other;
                    int turns=Mathf.Clamp(Mathf.Min(MaxTurns,peer.MaxTurns),1,4);
                    for(int turn=0;turn<turns;turn++)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        if(!Enabled||!peer.Enabled||!isActiveAndEnabled||!peer.isActiveAndEnabled||!speaker||!listener||!speaker.isActiveAndEnabled||!listener.isActiveAndEnabled||!speaker.AiEnabled||!listener.AiEnabled||speaker.PlayerConversationActive||listener.PlayerConversationActive||Vector3.Distance(speaker.transform.position,listener.transform.position)>Mathf.Min(InteractionRadius,peer.InteractionRadius))throw new OperationCanceledException(linked.Token);
                        Status=peer.Status=speaker.NpcName+" обдумывает ответ…";
                        var trace=await speaker.SpeakToNpcAsync(listener,input,linked.Token);
                        if(!trace.Validation.Accepted)throw new InvalidOperationException("Реплика отклонена проверкой действий.");
                        var line=speaker.ConversationHistory.LastOrDefault(h=>h.SpeakerId==speaker.CharacterId&&h.TargetId==listener.CharacterId);
                        if(line!=null) { listener.ObserveUtterance(line); result.Add(line.Copy()); }
                        input=trace.Speech; var previous=speaker; speaker=listener; listener=previous;
                    }
                    Status=peer.Status="Разговор завершён";
                    return result;
                }
                finally
                {
                    reserved.Remove(npc); reserved.Remove(other); ConversationActive=peer.ConversationActive=false; Partner=null; peer.Partner=null;
                    conversation=null; peer.conversation=null;
                    nextAttempt=peer.nextAttempt=Time.unscaledTime+Mathf.Max(Mathf.Clamp(CooldownSeconds,15,300),Mathf.Clamp(peer.CooldownSeconds,15,300));
                    movementUntil=peer.movementUntil=Time.unscaledTime+.5f;
                }
            }
        }
        static void StopForConversation(IntelligentNPC actor)
        {
            var agent=actor.GetComponent<NavMeshAgent>();
            if(agent&&agent.enabled&&agent.isOnNavMesh) { agent.ResetPath(); agent.isStopped=true; }
        }
    }
}
