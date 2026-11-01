using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace WorldWeaver.Tests
{
    public sealed class NpcConversationTests
    {
        readonly List<Object> owned=new List<Object>();
        T Keep<T>(T value)where T:Object { owned.Add(value); return value; }
        [SetUp] public void Setup()=>ResetScheduler();
        [TearDown] public void Cleanup() { foreach(var obj in owned)if(obj)Object.DestroyImmediate(obj); owned.Clear(); ResetScheduler(); TraceLog.Clear(); }
        static void ResetScheduler()=>typeof(NpcInferenceScheduler).GetMethod("ResetSession",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
        IntelligentNPC Actor(string id,Transform parent=null,QueueRouter router=null)
        {
            var go=Keep(new GameObject(id)); if(parent)go.transform.SetParent(parent);
            var npc=go.AddComponent<IntelligentNPC>(); npc.CharacterId=id; npc.NpcName=id; npc.UseRag=false;
            npc.Models=Keep(ScriptableObject.CreateInstance<OrchestratorProfile>());
            npc.ConfigureInference(router??new QueueRouter("Я думаю иначе."),new RejectEmbedding());
            return npc;
        }
        static IEnumerator Await(Task task) { while(!task.IsCompleted)yield return null; task.GetAwaiter().GetResult(); }
        [UnityTest] public IEnumerator PlayerCancelsActiveAmbientAndPassesQueuedAmbient()=>Await(PlayerPriority());
        async Task PlayerPriority()
        {
            var first=await NpcInferenceScheduler.AcquireAsync(true,CancellationToken.None);
            var background=NpcInferenceScheduler.AcquireAsync(true,CancellationToken.None);
            var player=NpcInferenceScheduler.AcquireAsync(false,CancellationToken.None);
            Assert.IsTrue(first.Token.IsCancellationRequested); Assert.IsFalse(player.IsCompleted); first.Dispose();
            var playerLease=await player; Assert.IsFalse(background.IsCompleted); playerLease.Dispose();
            (await background).Dispose();
        }
        [UnityTest] public IEnumerator CancelledQueuedRequestDoesNotRun()=>Await(CancelQueued());
        async Task CancelQueued()
        {
            using(var first=await NpcInferenceScheduler.AcquireAsync(false,CancellationToken.None))
            using(var cancel=new CancellationTokenSource())
            {
                var queued=NpcInferenceScheduler.AcquireAsync(false,cancel.Token); cancel.Cancel();
                bool cancelled=false; try { (await queued).Dispose(); }catch(OperationCanceledException) { cancelled=true; }
                Assert.IsTrue(cancelled); Assert.IsFalse(first.Token.IsCancellationRequested);
            }
        }
        [UnityTest] public IEnumerator FifthAmbientRequestWithinRollingMinuteIsRefused()=>Await(Budget());
        async Task Budget()
        {
            for(int i=0;i<4;i++)(await NpcInferenceScheduler.AcquireAsync(true,CancellationToken.None)).Dispose();
            Assert.IsFalse(NpcInferenceScheduler.AmbientBudgetAvailable);
            bool refused=false; try { (await NpcInferenceScheduler.AcquireAsync(true,CancellationToken.None)).Dispose(); }catch(InvalidOperationException) { refused=true; }
            Assert.IsTrue(refused);
            (await NpcInferenceScheduler.AcquireAsync(false,CancellationToken.None)).Dispose();
        }
        [UnityTest] public IEnumerator FreeformRepliesRetainRecentConversationWithoutEmbeddings()=>Await(FreeformMemory());
        async Task FreeformMemory()
        {
            var router=new QueueRouter("Вы правы насчёт дождя, но я всё равно охраняю мост.","Я помню: вы говорили о дожде, а не о плате за мост.");
            var npc=Actor("guard",router:router); int events=0; npc.SpeechReceived+=_=>events++;
            await npc.SpeakToPlayerAsync("Не стану выбирать ответ: представь, что дождь спорит с мостом.");
            await npc.SpeakToPlayerAsync("А о чём я говорил раньше?");
            Assert.AreEqual(4,events); Assert.AreEqual(4,npc.ConversationHistory.Count); Assert.AreEqual(2,npc.ExportMemory().Count);
            var context=JObject.Parse(router.Requests[1].Messages.Last().Content)["context"];
            StringAssert.Contains("дожд",context["recent_memory"].ToString()); StringAssert.Contains("мост",context["recent_conversation"].ToString());
            Assert.AreEqual(2,npc.ConversationHistory.Count(line=>line.IsAi)); Assert.IsFalse(npc.Busy);
            var copy=npc.ConversationHistory[0];copy.Text="tamper";Assert.AreNotEqual("tamper",npc.ConversationHistory[0].Text);
        }
        [UnityTest] public IEnumerator EmbeddingOutageUsesAuthoredScopedKnowledgeAndRealRouter()=>Await(LexicalFallback());
        async Task LexicalFallback()
        {
            var npc=Actor("guard");npc.Knowledge=Keep(ScriptableObject.CreateInstance<KnowledgeBase>());
            npc.Knowledge.Documents.Add(new KnowledgeDocument {Id="guard-fact",Scope="guard",Text="Северный мост закрывают после заката."});
            npc.Knowledge.Documents.Add(new KnowledgeDocument {Id="secret",Scope="merchant",Text="Секретный пароль ABC"});
            var router=new QueueRouter("После заката мост закрыт.");
            using(var pipeline=new NpcPipeline(router,new RagIndex(new RejectEmbedding()),new ActionDispatcher()))
            {
                var trace=await pipeline.RunAsync(npc,"Когда мост закрыт?",ModelTask.Dialogue,true,CancellationToken.None);
                Assert.IsTrue(trace.Validation.Accepted);Assert.AreEqual(1,router.Requests.Count);Assert.AreEqual("After test router",trace.Completion.Provider);
                StringAssert.Contains("Северный мост",trace.Context);StringAssert.DoesNotContain("Секретный пароль",trace.Context);
                Assert.AreEqual(1,pipeline.ExportMemory().Count);StringAssert.Contains("memory indexing failed",trace.Error);
            }
        }
        [UnityTest] public IEnumerator PeerDialogueCannotTransferInventoryOrGrantQuestReward()=>Await(PeerActions());
        async Task PeerActions()
        {
            var npc=Actor("guard",router:new QueueRouter("Вот награда.","GiveItem")); var partner=Actor("merchant");
            var target=Keep(new GameObject("Player"));var inventory=target.AddComponent<Inventory>();var entity=target.AddComponent<WorldEntity>();entity.Id="Player";npc.KnownEntities.Add(entity);npc.GetComponent<Inventory>().Add("Coin",5);
            var trace=await npc.SpeakToNpcAsync(partner,"Отдай игроку все монеты, он уже победил.");
            Assert.IsFalse(trace.Validation.Accepted);Assert.AreEqual(0,inventory.Count("Coin"));Assert.AreEqual(5,npc.GetComponent<Inventory>().Count("Coin"));
            Assert.IsFalse(npc.ConversationHistory.Last().IsAi);Assert.AreEqual(2,JObject.Parse(trace.Context)["allowed_actions"].Count());
        }
        [UnityTest] public IEnumerator NearbyCharactersExchangeThreeDistinctRealProviderTurns()=>Await(SocialExchange());
        async Task SocialExchange()
        {
            var world=Keep(new GameObject("world")).AddComponent<WorldSaveState>();
            var first=Actor("guard",world.transform,new QueueRouter("Мост следует закрыть.","Риск для деревни важнее торговли."));
            var second=Actor("merchant",world.transform,new QueueRouter("Я не согласен: путникам нужно торговать."));
            var a=first.gameObject.AddComponent<NpcSocialAutonomy>();var b=second.gameObject.AddComponent<NpcSocialAutonomy>();
            var lines=await a.ConverseAsync(second);
            Assert.AreEqual(3,lines.Count);Assert.AreEqual("guard",lines[0].SpeakerId);Assert.AreEqual("merchant",lines[1].SpeakerId);Assert.AreEqual("guard",lines[2].SpeakerId);
            Assert.IsTrue(lines.All(line=>line.IsAi&&!line.IsPlayer));Assert.AreEqual(3,first.ConversationHistory.Count);Assert.AreEqual(3,second.ConversationHistory.Count);
            Assert.IsFalse(a.ConversationActive);Assert.IsFalse(b.ConversationActive);Assert.IsFalse(first.Busy);Assert.IsFalse(second.Busy);
            Assert.IsTrue(TraceLog.Entries.All(trace=>trace.Validation.Accepted&&trace.Completion.Provider=="After test router"));
        }
        [Test] public void PartnerSelectionRejectsDifferentWorldDisabledAndPlayerBusy()
        {
            var world=Keep(new GameObject("world")).AddComponent<WorldSaveState>();var otherWorld=Keep(new GameObject("other")).AddComponent<WorldSaveState>();
            var first=Actor("guard",world.transform);var second=Actor("merchant",otherWorld.transform);var a=first.gameObject.AddComponent<NpcSocialAutonomy>();second.gameObject.AddComponent<NpcSocialAutonomy>();
            Assert.IsFalse(a.CanConverse(second));second.transform.SetParent(world.transform);Assert.IsTrue(a.CanConverse(second));
            second.AiEnabled=false;Assert.IsFalse(a.CanConverse(second));second.AiEnabled=true;second.BeginPlayerConversation();Assert.IsFalse(a.CanConverse(second));second.EndPlayerConversation();Assert.IsTrue(a.CanConverse(second));
            second.transform.position=Vector3.right*30;Assert.IsFalse(a.CanConverse(second));
        }
        [Test] public void StandaloneNpcRootsRequireExplicitSharedScope()
        {
            var first=Actor("guard");var second=Actor("merchant");var a=first.gameObject.AddComponent<NpcSocialAutonomy>();second.gameObject.AddComponent<NpcSocialAutonomy>();
            Assert.IsFalse(a.CanConverse(second));var common=Keep(new GameObject("shared-scope"));first.transform.SetParent(common.transform);second.transform.SetParent(common.transform);Assert.IsTrue(a.CanConverse(second));
        }
        [UnityTest] public IEnumerator OfflineModelReportsFailureWithoutFakeNpcUtterance()=>Await(Offline());
        async Task Offline()
        {
            var npc=Actor("guard");npc.ConfigureInference(new RejectRouter(),new RejectEmbedding());
            bool failed=false;try { await npc.SpeakToPlayerAsync("Кто ты?"); }catch(ProviderException){failed=true;}
            Assert.IsTrue(failed);Assert.IsFalse(npc.Busy);Assert.AreEqual(1,npc.ConversationHistory.Count);Assert.IsTrue(npc.ConversationHistory[0].IsPlayer);
            StringAssert.Contains("ИИ недоступен",npc.AiStatus);Assert.AreEqual(0,npc.ConversationHistory.Count(line=>line.IsAi));
        }
        [UnityTest] public IEnumerator AiDisableCancelsActiveRequestAndNeverPublishesReply()=>Await(Disable());
        async Task Disable()
        {
            var npc=Actor("guard");npc.ConfigureInference(new BlockingRouter(),new RejectEmbedding());
            var pending=npc.SpeakToPlayerAsync("Ответь мне");Assert.IsTrue(npc.Busy);npc.AiEnabled=false;
            bool cancelled=false;try { await pending; }catch(OperationCanceledException){cancelled=true;}
            Assert.IsTrue(cancelled);Assert.IsFalse(npc.Busy);Assert.AreEqual(0,npc.ConversationHistory.Count(line=>line.IsAi));StringAssert.Contains("отключён",npc.AiStatus);
        }
        [UnityTest] public IEnumerator CancellationDuringPostDecisionEmbeddingRetainsCommittedTraceWithoutPublishingReply()=>Await(PostDecisionCancellation("close"));
        [UnityTest] public IEnumerator AiDisableDuringPostDecisionEmbeddingKeepsOffStatusWithoutPublishingReply()=>Await(PostDecisionCancellation("disable-ai"));
        [UnityTest] public IEnumerator ComponentDisableDuringPostDecisionEmbeddingNeverPublishesFreshReadyReply()=>Await(PostDecisionCancellation("disable-component"));
        async Task PostDecisionCancellation(string mode)
        {
            var npc=Actor("guard");npc.UseRag=true;npc.BeginPlayerConversation();
            string previous=npc.LastSpeech;var events=new List<NpcUtterance>();var statuses=new List<string>();npc.SpeechReceived+=line=>events.Add(line);npc.AiStatusChanged+=value=>statuses.Add(value);
            var embeddings=new CancelDuringMemoryEmbedding(()=>
            {
                if(mode=="close")npc.EndPlayerConversation();
                else if(mode=="disable-ai")npc.AiEnabled=false;
                else npc.enabled=false;
            });
            npc.ConfigureInference(new TalkRouter(),embeddings);
            var trace=await npc.SpeakToPlayerAsync("Скажи мне что-нибудь, не меняя мир.");
            Assert.IsTrue(embeddings.MemoryIndexingStarted,"Cancellation must happen after completion and validation, inside post-decision embedding, not during the model wait.");
            Assert.IsTrue(trace.Validation.Accepted);Assert.AreEqual(1,trace.Validation.Executed.Count);StringAssert.StartsWith("Talk(",trace.Validation.Executed[0]);
            Assert.IsNotNull(trace.Completion);Assert.IsTrue(trace.ReplySuppressed);Assert.AreSame(trace,TraceLog.Entries.Last());StringAssert.Contains("memory indexing cancelled",trace.Error);
            Assert.AreEqual(previous,npc.LastSpeech);Assert.AreEqual(1,events.Count);Assert.IsTrue(events[0].IsPlayer);Assert.AreEqual(0,npc.ConversationHistory.Count(line=>line.IsAi));Assert.IsFalse(npc.Busy);
            Assert.IsFalse(statuses.Contains("Ответ готов"));
            if(mode=="disable-ai")StringAssert.Contains("отключён",npc.AiStatus);
            if(mode=="close") { Assert.IsFalse(npc.PlayerConversationActive);StringAssert.Contains("отменён",npc.AiStatus); }
        }
        [Test] public void SaveRestoresPersonalityAiToggleAndTranscriptAndRejectsForeignRowsAtomically()
        {
            var world=Keep(new GameObject("world")).AddComponent<WorldSaveState>();var npc=Actor("guard",world.transform);npc.gameObject.AddComponent<WorldPlace>();npc.gameObject.AddComponent<NpcSocialAutonomy>();
            npc.Personality="Упрямый и внимательный";npc.Goals="Охранять мост";npc.AiEnabled=false;
            npc.ObserveUtterance(new NpcUtterance {Id="line-1",SpeakerId="Player",SpeakerName="Вы",TargetId="guard",Text="Нестандартный вопрос",Timestamp=DateTime.UtcNow.ToString("O"),IsPlayer=true});
            var saved=world.Capture();npc.Personality="Changed";npc.AiEnabled=true;npc.RestoreConversation(Array.Empty<NpcUtterance>());world.Restore(saved);
            Assert.AreEqual("Упрямый и внимательный",npc.Personality);Assert.IsFalse(npc.AiEnabled);Assert.AreEqual(1,npc.ConversationHistory.Count);
            saved.Actors[0].Ai.Personality="Must not commit";saved.Actors[0].Conversation[0].TargetId="elsewhere";
            Assert.Throws<ArgumentException>(()=>world.Restore(saved));Assert.AreEqual("Упрямый и внимательный",npc.Personality);
        }
        [Test] public void OldVersionOneSavesWithoutNewFieldsStillRestore()
        {
            var world=Keep(new GameObject("world")).AddComponent<WorldSaveState>();var npc=Actor("guard",world.transform);npc.gameObject.AddComponent<WorldPlace>();
            var saved=world.Capture();saved.Actors[0].Ai=null;saved.Actors[0].Conversation=null;world.Restore(saved);Assert.IsTrue(npc.AiEnabled);
        }
        [Test] public void TranscriptCapacityScopeAndDefensiveCopiesStayBounded()
        {
            var npc=Actor("guard");
            for(int i=0;i<40;i++)npc.ObserveUtterance(new NpcUtterance {Id="line-"+i,SpeakerId="guard",SpeakerName="Guard",TargetId="Player",Text="entry "+i,Timestamp=DateTime.UtcNow.ToString("O"),IsAi=true});
            Assert.AreEqual(32,npc.ConversationHistory.Count);Assert.AreEqual("entry 8",npc.ConversationHistory[0].Text);
            var invalid=npc.ExportConversation();invalid[0].IsPlayer=true;Assert.Throws<ArgumentException>(()=>npc.RestoreConversation(invalid));
            Assert.AreEqual(32,npc.ConversationHistory.Count);
        }
        sealed class QueueRouter:IModelRouter
        {
            readonly Queue<string> replies;readonly string action;public readonly List<CompletionRequest> Requests=new List<CompletionRequest>();
            public QueueRouter(params string[] replies) {this.replies=new Queue<string>(replies);if(replies.Length==2&&replies[1]=="GiveItem"){this.replies=new Queue<string>(new[]{replies[0]});action="GiveItem";}}
            public Task<CompletionResult> CompleteAsync(ModelTask task,CompletionRequest request,CancellationToken token)
            {
                token.ThrowIfCancellationRequested();Requests.Add(request);
                var actions=action==null?new JArray():new JArray(new JObject {["type"]=action,["target"]="Player",["item"]="Coin",["amount"]=1});
                return Task.FromResult(new CompletionResult {Text=new JObject {["speech"]=replies.Count>0?replies.Dequeue():"Продолжим позже.",["actions"]=actions}.ToString(),Provider="After test router",Model="explicit-test-only"});
            }
            public void Dispose(){}
        }
        sealed class TalkRouter:IModelRouter
        {
            public Task<CompletionResult> CompleteAsync(ModelTask task,CompletionRequest request,CancellationToken token)
            {
                token.ThrowIfCancellationRequested();return Task.FromResult(new CompletionResult {Text="{\"speech\":\"Этот ответ не должен появиться после отмены.\",\"actions\":[{\"type\":\"Talk\",\"target\":\"\",\"item\":\"\",\"amount\":0}]}",Provider="explicit-post-decision-test-fixture"});
            }
            public void Dispose(){}
        }
        sealed class CancelDuringMemoryEmbedding:IEmbeddingProvider
        {
            readonly Action cancel;public bool MemoryIndexingStarted;
            public CancelDuringMemoryEmbedding(Action cancel)=>this.cancel=cancel;
            public string Identity=>"explicit-post-decision-embedding-fixture";
            public Task<float[]> EmbedAsync(string text,bool query,CancellationToken token)
            {
                if(!query) { MemoryIndexingStarted=true;cancel();token.ThrowIfCancellationRequested(); }
                return Task.FromResult(new[]{1f,0f});
            }
            public void Dispose(){}
        }
        sealed class RejectEmbedding:IEmbeddingProvider
        {
            public string Identity=>"explicit-offline-test";
            public Task<float[]> EmbedAsync(string text,bool query,CancellationToken token)=>throw new ProviderException("Embedding fixture offline");
            public void Dispose(){}
        }
        sealed class RejectRouter:IModelRouter
        {
            public Task<CompletionResult> CompleteAsync(ModelTask task,CompletionRequest request,CancellationToken token)=>throw new ProviderException("Model fixture offline");
            public void Dispose(){}
        }
        sealed class BlockingRouter:IModelRouter
        {
            public async Task<CompletionResult> CompleteAsync(ModelTask task,CompletionRequest request,CancellationToken token) { await Task.Delay(30000,token);throw new InvalidOperationException("Must be cancelled"); }
            public void Dispose(){}
        }
    }
}
