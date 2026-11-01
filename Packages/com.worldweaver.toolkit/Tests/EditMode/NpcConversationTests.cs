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
        async Task PostDecisionCancellation(string mode)
        {
            var npc=Actor("guard");npc.UseRag=true;npc.BeginPlayerConversation();
            string previous=npc.LastSpeech;var events=new List<NpcUtterance>();var statuses=new List<string>();npc.SpeechReceived+=line=>events.Add(line);npc.AiStatusChanged+=value=>statuses.Add(value);
            var embeddings=new CancelDuringMemoryEmbedding(()=>
            {
                if(mode=="close")npc.EndPlayerConversation();
                else npc.AiEnabled=false;
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
        IntelligentNPC TransferActor(out Inventory player)
        {
            var npc=Actor("guard");npc.GetComponent<Inventory>().Add("Coin",5);
            var go=Keep(new GameObject("Player"));player=go.AddComponent<Inventory>();var entity=go.AddComponent<WorldEntity>();entity.Id="Player";npc.KnownEntities.Add(entity);return npc;
        }
        static string Batch(string speech,string talkTarget="",bool transfer=false)=>new JObject { ["speech"]=speech,["actions"]=transfer?new JArray(new JObject { ["type"]="GiveItem",["target"]="Player",["item"]="Coin",["amount"]=1 },new JObject { ["type"]="Talk",["target"]=talkTarget,["item"]="",["amount"]=0 }):new JArray(new JObject { ["type"]="Talk",["target"]=talkTarget,["item"]="",["amount"]=0 }) }.ToString();
        [UnityTest] public IEnumerator RejectedWholeBatchRepairsOnceBeforeAnyMutationAndCommitsTransferExactlyOnce()=>Await(RepairAtomicTransfer());
        async Task RepairAtomicTransfer()
        {
            var npc=TransferActor(out var player);string rejected=Batch("Unexecuted proposal must not become memory","guard",true),corrected=Batch("Вот одна монета.","",true);
            var router=new ScriptedRouter((call,request,token)=>
            {
                if(call==2) { Assert.AreEqual(0,player.Count("Coin"));Assert.AreEqual(5,npc.GetComponent<Inventory>().Count("Coin"));StringAssert.Contains("Wait/Talk have no target",request.Messages.Last().Content); }
                return Task.FromResult(Completion(call,call==1?rejected:corrected));
            });npc.ConfigureInference(router,new RejectEmbedding());
            var trace=await npc.SpeakToPlayerAsync("Передай одну монету и поговори со мной.");
            Assert.AreEqual(2,router.Calls);Assert.AreEqual(2,trace.Attempts.Count);Assert.IsFalse(trace.Attempts[0].Validation.Accepted);Assert.IsEmpty(trace.Attempts[0].Validation.Executed);Assert.AreEqual(rejected,trace.Attempts[0].Raw);Assert.IsTrue(trace.Attempts[1].IsRepair);
            Assert.IsTrue(trace.Validation.Accepted);Assert.AreEqual(2,trace.Validation.Executed.Count);Assert.AreEqual(1,player.Count("Coin"));Assert.AreEqual(4,npc.GetComponent<Inventory>().Count("Coin"));Assert.AreEqual(corrected,trace.Raw);Assert.AreEqual(1,trace.Completion.Attempts);Assert.AreEqual(2,trace.ModelRequestStarts);Assert.AreEqual(2,trace.CompletedModelRequests);Assert.AreEqual(30,trace.CompletedInputTokens);Assert.AreEqual(60,trace.CompletedOutputTokens);Assert.AreEqual(2,trace.CompletedReportedProviderAttempts);Assert.IsTrue(trace.Attempts.All(attempt=>attempt.Completion.Attempts==1));
            Assert.AreEqual(1,npc.ConversationHistory.Count(line=>line.IsAi));Assert.IsFalse(npc.ExportMemory().Any(doc=>doc.Text.Contains("Unexecuted proposal")));
            Assert.IsTrue(JToken.DeepEquals(router.Requests[0].Schema,router.Requests[1].Schema));Assert.AreEqual(router.Requests[0].Messages[1].Content,router.Requests[1].Messages[1].Content);
        }
        [UnityTest] public IEnumerator BlankPlayerDialogueRepairsBeforeTransferAndCommitsCorrectedDecisionOnce()=>Await(RepairBlankPlayerDialogue());
        async Task RepairBlankPlayerDialogue()
        {
            var npc=TransferActor(out var player);int aiEvents=0;npc.SpeechReceived+=line=>{if(line.IsAi)aiEvents++;};
            string blank=Batch("","",true),corrected=Batch("Вот одна монета.","",true);
            var router=new ScriptedRouter((call,request,token)=>
            {
                if(call==2)
                {
                    Assert.AreEqual(0,player.Count("Coin"));Assert.AreEqual(5,npc.GetComponent<Inventory>().Count("Coin"));
                    Assert.AreEqual(0,aiEvents);Assert.IsEmpty(npc.ExportMemory());
                    StringAssert.Contains("Dialogue speech",request.Messages.Last().Content);
                }
                return Task.FromResult(Completion(call,call==1?blank:corrected));
            });npc.ConfigureInference(router,new RejectEmbedding());
            var trace=await npc.SpeakToPlayerAsync("Передай одну монету и объясни ответ.");
            Assert.AreEqual(2,router.Calls);Assert.AreEqual(2,trace.ModelRequestStarts);Assert.AreEqual(blank,trace.Attempts[0].Raw);
            Assert.AreEqual("validation_rejected",trace.Attempts[0].Status);Assert.IsFalse(trace.Attempts[0].Validation.Accepted);Assert.IsEmpty(trace.Attempts[0].Validation.Executed);
            Assert.IsTrue(trace.Validation.Accepted);Assert.AreEqual(2,trace.Validation.Executed.Count);Assert.AreEqual(1,player.Count("Coin"));Assert.AreEqual(4,npc.GetComponent<Inventory>().Count("Coin"));
            Assert.AreEqual(1,aiEvents);Assert.AreEqual("Вот одна монета.",trace.Speech);Assert.AreEqual(1,npc.ConversationHistory.Count(line=>line.IsAi));
            Assert.IsTrue(JToken.DeepEquals(router.Requests[0].Schema,router.Requests[1].Schema));Assert.AreEqual(router.Requests[0].Messages[1].Content,router.Requests[1].Messages[1].Content);
        }
        [UnityTest] public IEnumerator TwoBlankDialogueDecisionsReturnDiagnosticWithoutEffectsOrFakeAiSpeech()=>Await(RejectTwoBlankDialogueDecisions());
        async Task RejectTwoBlankDialogueDecisions()
        {
            var npc=TransferActor(out var player);int aiEvents=0;npc.SpeechReceived+=line=>{if(line.IsAi)aiEvents++;};
            var router=new ScriptedRouter((call,request,token)=>Task.FromResult(Completion(call,Batch(call==1?"":" \t\r\n","",true))));npc.ConfigureInference(router,new RejectEmbedding());
            var trace=await npc.SpeakToPlayerAsync("Не теряй мою неожиданную реплику.");
            Assert.AreEqual(2,router.Calls);Assert.IsFalse(trace.Validation.Accepted);Assert.IsEmpty(trace.Validation.Executed);Assert.IsTrue(trace.Attempts.All(attempt=>attempt.Status=="validation_rejected"));
            Assert.AreEqual(0,player.Count("Coin"));Assert.AreEqual(5,npc.GetComponent<Inventory>().Count("Coin"));Assert.IsEmpty(npc.ExportMemory());Assert.AreEqual(0,aiEvents);Assert.AreEqual(0,npc.ConversationHistory.Count(line=>line.IsAi));
            StringAssert.Contains("2 attempt(s)",trace.Error);StringAssert.Contains("Dialogue speech",trace.Error);Assert.IsNotEmpty(trace.Speech);
            Assert.AreEqual("Действие отклонено проверкой",npc.AiStatus);Assert.AreEqual("Не теряй мою неожиданную реплику.",npc.ConversationHistory.First(line=>line.IsPlayer).Text);Assert.IsFalse(npc.Busy);
        }
        [UnityTest] public IEnumerator BlankPeerDialogueUsesOneRepairAndPublishesOnlyCorrectedReply()=>Await(RepairBlankPeerDialogue());
        async Task RepairBlankPeerDialogue()
        {
            var npc=Actor("guard");var peer=Actor("merchant");
            var router=new ScriptedRouter((call,request,token)=>Task.FromResult(Completion(call,new JObject { ["speech"]=call==1?" \t":"Давай проверять путников, сохраняя проход для торговли.",["actions"]=new JArray() }.ToString())));npc.ConfigureInference(router,new RejectEmbedding());
            var trace=await npc.SpeakToNpcAsync(peer,"Как сохранить торговлю без риска?");
            Assert.AreEqual(2,router.Calls);Assert.IsTrue(trace.Attempts[1].IsRepair);Assert.IsFalse(trace.Attempts[0].Validation.Accepted);Assert.IsTrue(trace.Validation.Accepted);
            Assert.AreEqual(1,npc.ConversationHistory.Count);Assert.IsTrue(npc.ConversationHistory[0].IsAi);Assert.AreEqual("merchant",npc.ConversationHistory[0].TargetId);Assert.AreEqual(trace.Speech,npc.ConversationHistory[0].Text);
            StringAssert.Contains("speech обязательно содержит непустой ответ",router.Requests[0].Messages[0].Content);
        }
        [UnityTest] public IEnumerator AutonomousPlanningMayStillWaitWithoutSpeechOrDialogueRepair()=>Await(BlankPlanningWait());
        async Task BlankPlanningWait()
        {
            var npc=Actor("guard");var router=new ScriptedRouter((call,request,token)=>Task.FromResult(Completion(call,new JObject { ["speech"]="",["actions"]=new JArray(new JObject { ["type"]="Wait",["target"]="",["item"]="",["amount"]=0 }) }.ToString())));npc.ConfigureInference(router,new RejectEmbedding());
            var trace=await npc.PlanAsync();
            Assert.AreEqual(1,router.Calls);Assert.AreEqual(1,trace.ModelRequestStarts);Assert.IsTrue(trace.Validation.Accepted);Assert.AreEqual(1,trace.Validation.Executed.Count);StringAssert.StartsWith("Wait(",trace.Validation.Executed[0]);
            Assert.AreEqual("",trace.Speech);Assert.IsEmpty(npc.ConversationHistory);StringAssert.DoesNotContain("speech обязательно содержит непустой ответ",router.Requests[0].Messages[0].Content);
        }
        [UnityTest] public IEnumerator ParseFailureRepairsWithStrictSchemaAndKeepsOriginalRawAudit()=>Await(RepairParse());
        async Task RepairParse()
        {
            var npc=Actor("guard");var router=new ScriptedRouter((call,request,token)=>Task.FromResult(Completion(call,call==1?"{":Batch("Теперь корректная реплика."))));npc.ConfigureInference(router,new RejectEmbedding());
            var trace=await npc.SpeakToPlayerAsync("Продолжим разговор.");Assert.IsTrue(trace.Validation.Accepted);Assert.AreEqual(2,router.Calls);Assert.AreEqual("{",trace.Attempts[0].Raw);StringAssert.Contains("Decision parsing failed",trace.Attempts[0].Validation.Reason);Assert.IsNotEmpty(trace.Attempts[0].Error);StringAssert.Contains("validation_reason",router.Requests[1].Messages.Last().Content);
            var talk=((JArray)router.Requests[1].Schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Single(branch=>(string)branch["properties"]["type"]["enum"][0]=="Talk");
            Assert.AreEqual("",(string)talk["properties"]["target"]["enum"][0]);Assert.AreEqual("",(string)talk["properties"]["item"]["enum"][0]);Assert.AreEqual(0,(int)talk["properties"]["amount"]["enum"][0]);Assert.AreEqual(4,(int)router.Requests[1].Schema["properties"]["actions"]["maxItems"]);
        }
        [UnityTest] public IEnumerator TwoRejectedDecisionsStopWithoutMutationOrTeachingInvalidReplyToMemory()=>Await(RepairFails());
        async Task RepairFails()
        {
            var npc=TransferActor(out var player);var router=new ScriptedRouter((call,request,token)=>Task.FromResult(Completion(call,Batch("Unexecuted proposal "+call,"guard",true))));npc.ConfigureInference(router,new RejectEmbedding());
            var trace=await npc.SpeakToPlayerAsync("Дай монету.");Assert.AreEqual(2,router.Calls);Assert.AreEqual(2,trace.Attempts.Count);Assert.IsFalse(trace.Validation.Accepted);Assert.IsEmpty(trace.Validation.Executed);Assert.AreEqual(0,player.Count("Coin"));Assert.AreEqual(5,npc.GetComponent<Inventory>().Count("Coin"));StringAssert.Contains("2 attempt(s)",trace.Error);StringAssert.Contains("Wait/Talk have no target",trace.Error);Assert.AreEqual(0,npc.ConversationHistory.Count(line=>line.IsAi));Assert.IsEmpty(npc.ExportMemory());
        }
        [UnityTest] public IEnumerator ProviderFailureNeverTriggersDecisionRepair()=>Await(NoProviderRepair());
        async Task NoProviderRepair()
        {
            var npc=Actor("guard");var router=new ScriptedRouter((call,request,token)=>throw new ProviderException("Explicit offline fixture"));npc.ConfigureInference(router,new RejectEmbedding());
            bool failed=false;try {await npc.SpeakToPlayerAsync("Привет");}catch(ProviderException){failed=true;}
            Assert.IsTrue(failed);Assert.AreEqual(1,router.Calls);Assert.AreEqual(1,TraceLog.Entries.Last().ModelRequestStarts);Assert.AreEqual(0,TraceLog.Entries.Last().CompletedModelRequests);Assert.AreEqual("failed",TraceLog.Entries.Last().Attempts[0].Status);Assert.IsNull(TraceLog.Entries.Last().CompletedReportedProviderAttempts);Assert.AreEqual(0,npc.ConversationHistory.Count(line=>line.IsAi));
        }
        [UnityTest] public IEnumerator CancellationDuringRepairCannotDispatchOriginalOrCorrectedTransfer()=>Await(CancelRepair());
        async Task CancelRepair()
        {
            var npc=TransferActor(out var player);var router=new ScriptedRouter(async(call,request,token)=>
            {
                if(call==1)return Completion(call,Batch("Unexecuted proposal","guard",true));
                await Task.Delay(30000,token);return Completion(call,Batch("Эта реплика отменена.","",true));
            });npc.ConfigureInference(router,new RejectEmbedding());
            using(var cancellation=new CancellationTokenSource())
            {
                var pending=npc.SpeakToPlayerAsync("Дай монету",cancellation.Token);Assert.AreEqual(2,router.Calls);Assert.IsTrue(npc.Busy);Assert.AreEqual(0,player.Count("Coin"));cancellation.Cancel();
                bool cancelled=false;try{await pending;}catch(OperationCanceledException){cancelled=true;}
                Assert.IsTrue(cancelled);Assert.AreEqual(0,player.Count("Coin"));Assert.AreEqual(5,npc.GetComponent<Inventory>().Count("Coin"));Assert.AreEqual(2,TraceLog.Entries.Last().Attempts.Count);Assert.AreEqual(2,TraceLog.Entries.Last().ModelRequestStarts);Assert.AreEqual(1,TraceLog.Entries.Last().CompletedModelRequests);Assert.AreEqual("cancelled",TraceLog.Entries.Last().Attempts[1].Status);Assert.IsNull(TraceLog.Entries.Last().Attempts[1].Completion);Assert.IsNotEmpty(TraceLog.Entries.Last().Attempts[1].Error);Assert.IsFalse(TraceLog.Entries.Last().Attempts[0].Validation.Accepted);Assert.AreEqual(0,npc.ConversationHistory.Count(line=>line.IsAi));Assert.IsFalse(npc.Busy);
            }
        }
        [UnityTest] public IEnumerator AmbientRepairConsumesAdditionalRequestBudgetAndCannotExceedFourStarts()=>Await(RepairBudget());
        async Task RepairBudget()
        {
            for(int i=0;i<3;i++)(await NpcInferenceScheduler.AcquireAsync(true,CancellationToken.None)).Dispose();
            var npc=Actor("guard");var peer=Actor("merchant");var router=new ScriptedRouter((call,request,token)=>Task.FromResult(Completion(call,Batch("Некорректный адресат.","merchant"))));npc.ConfigureInference(router,new RejectEmbedding());
            var trace=await npc.SpeakToNpcAsync(peer,"Обсудим мост.");Assert.AreEqual(1,router.Calls);Assert.IsFalse(trace.Validation.Accepted);StringAssert.Contains("repair request budget unavailable",trace.Error);Assert.IsFalse(NpcInferenceScheduler.AmbientBudgetAvailable);Assert.IsEmpty(npc.ExportMemory());
        }
        [UnityTest] public IEnumerator PeerContextExcludesUnrelatedPlayerConversationWhileKeepingOwnVerifiedEpisodes()=>Await(PeerMemoryScope());
        async Task PeerMemoryScope()
        {
            var npc=Actor("guard");var peer=Actor("merchant");var router=new ScriptedRouter((call,request,token)=>Task.FromResult(Completion(call,Batch("Обсудим торговлю."))));npc.ConfigureInference(router,new RejectEmbedding());
            npc.RestoreMemory(new[]{new KnowledgeDocument {Id="past-player",Scope="guard",Kind=MemoryKind.Conversation,Text="Игрок говорил о FANTASY_WEATHER_SUNDOG. Ответ NPC не доказывает реальную погоду."},new KnowledgeDocument {Id="public-event",Scope="guard",Kind=MemoryKind.Episodic,Text="VERIFIED_GATE_EVENT: ворота действительно открыты."}});
            npc.ObserveUtterance(new NpcUtterance {Id="player-line",SpeakerId="Player",SpeakerName="Вы",TargetId="guard",Text="FANTASY_WEATHER_SUNDOG",IsPlayer=true,Timestamp=DateTime.UtcNow.ToString("O")});
            await npc.SpeakToNpcAsync(peer,"Обсудим торговлю.");
            string context=(string)JObject.Parse(router.Requests[0].Messages[1].Content)["context"].ToString();StringAssert.DoesNotContain("FANTASY_WEATHER_SUNDOG",context);StringAssert.Contains("VERIFIED_GATE_EVENT",context);StringAssert.Contains("не присваивай роль",router.Requests[0].Messages[0].Content);
        }
        [UnityTest] public IEnumerator RepairCannotEscalateNpcPeerPermissionsToInventoryTransfer()=>Await(RepairScope());
        async Task RepairScope()
        {
            var npc=TransferActor(out var player);var peer=Actor("merchant");var router=new ScriptedRouter((call,request,token)=>Task.FromResult(Completion(call,call==1?Batch("Спорю.","merchant"):Batch("Дам монету.","",true))));npc.ConfigureInference(router,new RejectEmbedding());
            var trace=await npc.SpeakToNpcAsync(peer,"Давай обсудим мост.");Assert.AreEqual(2,router.Calls);Assert.IsFalse(trace.Validation.Accepted);Assert.AreEqual(0,player.Count("Coin"));Assert.AreEqual(5,npc.GetComponent<Inventory>().Count("Coin"));Assert.IsTrue(JToken.DeepEquals(router.Requests[0].Schema,router.Requests[1].Schema));
            Assert.AreEqual(2,((JArray)router.Requests[1].Schema["properties"]["actions"]["items"]["anyOf"]).Count);Assert.IsFalse(trace.Validation.Accepted);
        }
        static CompletionResult Completion(int number,string raw)=>new CompletionResult {Text=raw,Provider="explicit-repair-test-fixture",Model="test-only",Attempts=1,InputTokens=number*10,OutputTokens=number*20};
        sealed class ScriptedRouter:IModelRouter
        {
            readonly Func<int,CompletionRequest,CancellationToken,Task<CompletionResult>> complete;
            public readonly List<CompletionRequest> Requests=new List<CompletionRequest>();public int Calls;
            public ScriptedRouter(Func<int,CompletionRequest,CancellationToken,Task<CompletionResult>> complete)=>this.complete=complete;
            public Task<CompletionResult> CompleteAsync(ModelTask task,CompletionRequest request,CancellationToken token) {Calls++;Requests.Add(request);return complete(Calls,request,token);}
            public void Dispose(){}
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
