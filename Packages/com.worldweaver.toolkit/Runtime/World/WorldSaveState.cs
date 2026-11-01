using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;
namespace WorldWeaver
{
    /// <summary>Versioned, bounded save of this world's inventory, quests and scoped episodic memory.</summary>
    public sealed class WorldSaveState : MonoBehaviour
    {
        public string WorldId = Guid.NewGuid().ToString("N");
        [Serializable] public sealed class ActorState
        {
            public string Id; public float X,Y,Z;
            public Dictionary<string,int> Inventory;
            public List<KnowledgeDocument> Memory;
            public List<NpcRelationship> Relationships;
            public List<NpcUtterance> Conversation;
            public NpcAiState Ai;
        }
        [Serializable] public sealed class NpcAiState
        {
            public bool Enabled,Autonomous,Social;
            public string NpcName,Role,Personality,Biography,Goals,Constraints,SocialTopic;
        }
        [Serializable] public sealed class QuestState { public string Id; public DeliveryQuestStage Stage; }
        [Serializable] public sealed class ClockState { public int Day;public float Hour,DaySeconds;public bool Running; }
        [Serializable] public sealed class SaveData { public ClockState Clock;public List<WorldEventRecord> Events;public int Version=1; public string WorldId; public List<ActorState> Actors=new List<ActorState>(); public List<QuestState> Quests=new List<QuestState>(); }
        public string SavePath
        {
            get { if (!Guid.TryParseExact(WorldId,"N",out _)) throw new InvalidOperationException("Invalid world save identity."); return Path.Combine(Application.persistentDataPath,"WorldWeaver",WorldId+".json"); }
        }
        private WorldPlace[] Actors() => GetComponentsInChildren<WorldPlace>(true).Where(p => p.GetComponent<Inventory>()).ToArray();
        public SaveData Capture()
        {
            var result = new SaveData { WorldId=WorldId };
            foreach (var place in Actors())
            {
                var npc = place.GetComponent<IntelligentNPC>(); if (npc && npc.Busy) throw new InvalidOperationException("Дождитесь ответа персонажей перед сохранением.");
                var p=place.transform.position; result.Actors.Add(new ActorState { Id=place.PersistentId,X=p.x,Y=p.y,Z=p.z,Inventory=place.GetComponent<Inventory>().Snapshot(),Memory=npc?npc.ExportMemory():new List<KnowledgeDocument>(),Conversation=npc?npc.ExportConversation():null,Ai=npc?CaptureAi(npc):null,Relationships=place.GetComponent<NpcRelationships>()?.Relations.Select(r=>new NpcRelationship{ActorId=r.ActorId,Trust=r.Trust}).ToList() });
            }
            result.Quests = GetComponentsInChildren<DeliveryQuest>(true).Select(q => new QuestState { Id=q.QuestId,Stage=q.Stage }).ToList();
            var clock=GetComponent<WorldClock>();if(clock)result.Clock=new ClockState {Day=clock.Day,Hour=clock.Hour,DaySeconds=clock.DaySeconds,Running=clock.Running};
            var events=GetComponent<WorldEventJournal>();if(events)result.Events=events.Events.Select(e=>new WorldEventRecord {Id=e.Id,Kind=e.Kind,Actor=e.Actor,Text=e.Text,Day=e.Day,Hour=e.Hour}).ToList();
            return result;
        }
        public void Restore(SaveData saved)
        {
            if (saved == null || saved.Version != 1 || saved.WorldId != WorldId || saved.Actors == null || saved.Quests == null || saved.Actors.Count>100 || saved.Quests.Count>100) throw new ArgumentException("Сохранение относится к другому миру или повреждено.");
            var actors=Actors().ToDictionary(p=>p.PersistentId); var quests=GetComponentsInChildren<DeliveryQuest>(true).ToDictionary(q=>q.QuestId);
            if(saved.Actors.Select(a=>a?.Id).Distinct().Count()!=saved.Actors.Count || saved.Quests.Select(q=>q?.Id).Distinct().Count()!=saved.Quests.Count) throw new ArgumentException("Duplicate save identities.");
            if(saved.Clock!=null&&(!GetComponent<WorldClock>()||saved.Clock.Day<1||saved.Clock.Day>1000000||!Finite(saved.Clock.Hour)||saved.Clock.Hour<0||saved.Clock.Hour>=24||!Finite(saved.Clock.DaySeconds)||saved.Clock.DaySeconds<30||saved.Clock.DaySeconds>3600))throw new ArgumentException("Invalid saved world clock.");
            if(saved.Events!=null&&(saved.Events.Count>128||saved.Events.Any(e=>e==null||string.IsNullOrWhiteSpace(e.Id)||e.Text==null||e.Text.Length>500||e.Day<1||!Finite(e.Hour)||e.Hour<0||e.Hour>=24)))throw new ArgumentException("Invalid world event history.");
            // Validate everything before mutating anything.
            foreach(var actor in saved.Actors)
            {
                if(actor==null || actor.Id==null || !actors.TryGetValue(actor.Id,out var place) || !Finite(actor.X)||!Finite(actor.Y)||!Finite(actor.Z) || actor.Inventory==null || actor.Inventory.Count>1000 || actor.Inventory.Any(p=>string.IsNullOrWhiteSpace(p.Key)||p.Key.Length>120||p.Value<0) || actor.Inventory.Sum(p=>(long)p.Value)>place.GetComponent<Inventory>().Capacity) throw new ArgumentException("Invalid actor state.");
                if(actor.Relationships!=null&&(actor.Relationships.Count>32||actor.Relationships.Any(r=>r==null||string.IsNullOrWhiteSpace(r.ActorId)||r.ActorId.Length>160||r.Trust< -100||r.Trust>100)||actor.Relationships.Select(r=>r.ActorId).Distinct().Count()!=actor.Relationships.Count))throw new ArgumentException("Invalid NPC relationships.");
                var npc=place.GetComponent<IntelligentNPC>();
                if(actor.Conversation!=null&&(!npc||!IntelligentNPC.ValidConversation(actor.Conversation)||actor.Conversation.Any(line=>line.SpeakerId!=npc.CharacterId&&line.TargetId!=npc.CharacterId)))throw new ArgumentException("Invalid scoped NPC transcript.");
                if(actor.Ai!=null&&(!npc||!ValidAi(actor.Ai)))throw new ArgumentException("Invalid saved NPC personality.");
                if(npc && (npc.Busy || actor.Memory==null || actor.Memory.Count>32 || actor.Memory.Any(d=>d==null||d.Scope!=npc.CharacterId||d.Kind==MemoryKind.StaticKnowledge||!Enum.IsDefined(typeof(MemoryKind),d.Kind)||d.Text==null||d.Text.Length>900||string.IsNullOrWhiteSpace(d.Id)))) throw new ArgumentException("Invalid scoped NPC memory.");
            }
            foreach(var quest in saved.Quests) if(quest==null||quest.Id==null||!quests.ContainsKey(quest.Id)||!Enum.IsDefined(typeof(DeliveryQuestStage),quest.Stage)) throw new ArgumentException("Invalid quest state.");
            foreach(var actor in saved.Actors)
            {
                var place=actors[actor.Id]; var inv=place.GetComponent<Inventory>();
                foreach(var old in inv.Snapshot()) if(old.Value>0) inv.Remove(old.Key,old.Value);
                foreach(var item in actor.Inventory) inv.Add(item.Key,item.Value);
                var motor=place.GetComponent<CharacterController>(); bool enabled=motor&&motor.enabled; if(enabled)motor.enabled=false;
                var destination=new Vector3(actor.X,actor.Y,actor.Z); var agent=place.GetComponent<NavMeshAgent>();
                if(agent&&agent.enabled&&agent.isOnNavMesh){agent.ResetPath();agent.Warp(destination);}else place.transform.position=destination;
                if(enabled)motor.enabled=true;
                var npc=place.GetComponent<IntelligentNPC>(); if(npc) { npc.RestoreMemory(actor.Memory); if(actor.Conversation!=null)npc.RestoreConversation(actor.Conversation); if(actor.Ai!=null)RestoreAi(npc,actor.Ai); }
                var relations=place.GetComponent<NpcRelationships>();if(relations&&actor.Relationships!=null)relations.Relations=actor.Relationships.Select(r=>new NpcRelationship{ActorId=r.ActorId,Trust=r.Trust}).ToList();
            }
            foreach(var quest in saved.Quests)quests[quest.Id].Stage=quest.Stage;
            var events=GetComponent<WorldEventJournal>();if(events&&saved.Events!=null)events.Events=saved.Events.Select(e=>new WorldEventRecord {Id=e.Id,Kind=e.Kind,Actor=e.Actor,Text=e.Text,Day=e.Day,Hour=e.Hour}).ToList();
            if(saved.Clock!=null)
            {
                var clock=GetComponent<WorldClock>();if(events)events.SuppressRecording=true;
                try{clock.Running=saved.Clock.Running;clock.DaySeconds=saved.Clock.DaySeconds;clock.SetTime(saved.Clock.Hour,saved.Clock.Day);foreach(var gate in GetComponentsInChildren<WorldGate>())gate.ApplyClock();}
                finally{if(events)events.SuppressRecording=false;}
            }
            foreach(var routine in GetComponentsInChildren<NpcDailyRoutine>())routine.ResetAfterLoad();
        }
        static NpcAiState CaptureAi(IntelligentNPC npc)
        {
            var social=npc.GetComponent<NpcSocialAutonomy>();
            var state=new NpcAiState { Enabled=npc.AiEnabled,Autonomous=npc.Autonomous,Social=social&&social.enabled&&social.Enabled,NpcName=npc.NpcName,Role=npc.Role,Personality=npc.Personality,Biography=npc.Biography,Goals=npc.Goals,Constraints=npc.Constraints,SocialTopic=social?social.Topic:"" };
            if(!ValidAi(state))throw new InvalidOperationException("Поля личности NPC допускают до 4000 символов; проверьте имя и тему разговора перед сохранением.");
            return state;
        }
        static bool ValidAi(NpcAiState state)=>Short(state.NpcName,160)&&Short(state.Role,4000)&&Short(state.Personality,4000)&&Short(state.Biography,4000)&&Short(state.Goals,4000)&&Short(state.Constraints,4000)&&Short(state.SocialTopic,800);
        static bool Short(string value,int limit)=>value!=null&&value.Length<=limit;
        static void RestoreAi(IntelligentNPC npc,NpcAiState state)
        {
            npc.NpcName=state.NpcName; npc.Role=state.Role; npc.Personality=state.Personality; npc.Biography=state.Biography; npc.Goals=state.Goals; npc.Constraints=state.Constraints;
            npc.AiEnabled=state.Enabled; npc.Autonomous=state.Autonomous;
            var social=npc.GetComponent<NpcSocialAutonomy>(); if(social) { social.Enabled=social.enabled=state.Social; social.Topic=state.SocialTopic; }
        }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value)&&Mathf.Abs(value)<=100000;
        public void Save()
        {
            string path=SavePath; Directory.CreateDirectory(Path.GetDirectoryName(path)); string temporary=path+".tmp";
            try { File.WriteAllText(temporary,JsonConvert.SerializeObject(Capture(),Formatting.Indented)); if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path); }
            finally { if(File.Exists(temporary))File.Delete(temporary); }
        }
        public void Load()
        {
            string path=SavePath; if(!File.Exists(path)||new FileInfo(path).Length>1024*1024)throw new InvalidOperationException("Сохранение отсутствует или слишком велико.");
            Restore(JsonConvert.DeserializeObject<SaveData>(File.ReadAllText(path),new JsonSerializerSettings { TypeNameHandling=TypeNameHandling.None,MaxDepth=16,MissingMemberHandling=MissingMemberHandling.Error }));
        }
    }
}
