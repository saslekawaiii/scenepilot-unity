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
using UnityEngine.UIElements;
using Object=UnityEngine.Object;
namespace WorldWeaver.Tests
{
    /// <summary>Public runtime API and actual UI tree checks. Programmatic quest invocation is not native click evidence.</summary>
    public sealed class ConversationUiPlayTests
    {
        readonly List<Object> owned=new List<Object>();
        T Keep<T>(T value)where T:Object {owned.Add(value);return value;}
        [UnityTearDown] public IEnumerator Cleanup() {foreach(var obj in owned)if(obj)Object.Destroy(obj);owned.Clear();yield return null;}
        WorldPlayer Player(out WorldSaveState world)
        {
            var root=Keep(new GameObject("Dialogue UI world"));world=root.AddComponent<WorldSaveState>();
            var go=new GameObject("Player");go.transform.SetParent(root.transform);go.transform.position=Vector3.up*3;go.AddComponent<WorldPlace>();return go.AddComponent<WorldPlayer>();
        }
        IntelligentNPC Actor(WorldSaveState world,string id,Vector3 position,IModelRouter router=null)
        {
            var go=new GameObject(id);go.transform.SetParent(world.transform);go.transform.position=position;var npc=go.AddComponent<IntelligentNPC>();npc.CharacterId=id;npc.NpcName=id;npc.UseRag=false;
            npc.ConfigureInference(router??new ReplyRouter(),new NoEmbedding());go.AddComponent<WorldPlace>();return npc;
        }
        static void InvokeQuest(WorldConversationUI ui)=>typeof(WorldConversationUI).GetMethod("PerformQuestAction",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ui,null);
        [UnityTest] public IEnumerator OpeningNearestConversationFreezesActualPlayerUpdateAndDoesNotAcceptQuest()
        {
            var player=Player(out var world);var npc=Actor(world,"giver",new Vector3(1,3,0));var recipient=Actor(world,"recipient",new Vector3(2,3,0));
            var quest=world.gameObject.AddComponent<DeliveryQuest>();quest.Giver=npc.GetComponent<WorldPlace>();quest.Recipient=recipient.GetComponent<WorldPlace>();
            Assert.IsTrue(player.OpenNearestConversation());Assert.AreEqual(npc,player.Conversation.ActiveNpc);Assert.IsTrue(npc.PlayerConversationActive);
            var start=player.transform.position;yield return new WaitForSeconds(.25f);Assert.Less(Vector3.Distance(start,player.transform.position),.001f,"Open conversation must prevent WorldPlayer.Update from applying movement and gravity.");
            Assert.AreEqual(DeliveryQuestStage.NotStarted,quest.Stage);Assert.AreEqual(0,player.GetComponent<Inventory>().Count(quest.LetterItem));
            Assert.IsNotNull(player.Conversation.View.Q<TextField>("conversation-input"));Assert.IsNotNull(player.Conversation.View.Q<Button>("conversation-send"));
            player.Conversation.Close();Assert.IsFalse(npc.PlayerConversationActive);
            float dropDeadline=Time.realtimeSinceStartup+4;int resumedFrames=0;
            while((resumedFrames<20||player.transform.position.y>start.y-.05f)&&Time.realtimeSinceStartup<dropDeadline)
            { yield return null;resumedFrames++; }
            TestContext.WriteLine("Actual resumed player update: observedFrames="+resumedFrames+", verticalDropMetres="+(start.y-player.transform.position.y));
            Assert.GreaterOrEqual(resumedFrames,20,"Observe actual WorldPlayer.Update frames instead of relying on a short wall-clock wait in batchmode.");
            Assert.Less(player.transform.position.y,start.y-.05f,"Closing must release the movement guard; gravity must produce a meaningful measured drop.");
        }
        [UnityTest] public IEnumerator OnlyActualNpcToNpcUtterancesBecomeAmbientCaptions()
        {
            var player=Player(out var world);var npc=Actor(world,"guard",new Vector3(1,3,0));var peer=Actor(world,"merchant",new Vector3(2,3,0));
            var ui=player.Conversation;var bubbles=ui.GetComponent<NpcAmbientBubbles>();
            typeof(NpcAmbientBubbles).GetMethod("UpdateSubscriptions",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(bubbles,null);
            foreach(string target in new[]{"Player","player"})
            {
                npc.ObserveUtterance(new NpcUtterance {Id=Guid.NewGuid().ToString("N"),SpeakerId=npc.CharacterId,SpeakerName=npc.NpcName,TargetId=target,Text="Ответ игроку",IsAi=true,Timestamp=DateTime.UtcNow.ToString("o")});
                Assert.AreEqual(0,ui.View.Query<VisualElement>(className:"ambient-speech").ToList().Count);
            }
            npc.ObserveUtterance(new NpcUtterance {Id=Guid.NewGuid().ToString("N"),SpeakerId=npc.CharacterId,SpeakerName=npc.NpcName,TargetId=peer.CharacterId,Text="Я не согласен: мост следует закрыть до ремонта.",IsAi=true,Timestamp=DateTime.UtcNow.ToString("o")});
            Assert.AreEqual(1,ui.View.Query<VisualElement>(className:"ambient-speech").ToList().Count);
            StringAssert.Contains("Я не согласен",ui.View.Q<Label>(className:"ambient-text").text);
            yield return null;
        }
        [UnityTest] public IEnumerator UnexpectedReplyFlowsThroughNpcApiIntoVisiblePlainTextHistory()
        {
            var player=Player(out var world);var npc=Actor(world,"guard",new Vector3(1,3,0));player.OpenNearestConversation();
            var task=player.Conversation.SendAsync("Не выбираю заготовку: <b>облако</b> спорит с мостом. Что ответишь?");
            while(!task.IsCompleted)yield return null;
            Assert.IsTrue(task.GetAwaiter().GetResult().Validation.Accepted);yield return null;
            Assert.AreEqual(2,npc.ConversationHistory.Count);Assert.IsTrue(npc.ConversationHistory[0].IsPlayer);Assert.IsTrue(npc.ConversationHistory[1].IsAi);
            var text=player.Conversation.View.Query<Label>(className:"speech-text").ToList();Assert.AreEqual(2,text.Count);
            Assert.IsTrue(text.All(label=>!label.enableRichText));StringAssert.Contains("<b>облако</b>",text[0].text);StringAssert.Contains("Поговорим",text[1].text);
            Assert.IsTrue(player.Conversation.View.Q<TextField>("conversation-input").enabledInHierarchy);Assert.IsNull(player.Conversation.LastError);
        }
        [UnityTest] public IEnumerator ClosingPendingRequestCancelsAndNextNpcCannotReceiveStaleReply()
        {
            var player=Player(out var world);var first=Actor(world,"guard",new Vector3(1,3,0),new BlockingRouter());var second=Actor(world,"merchant",new Vector3(2,3,0));
            var ui=player.Conversation;ui.Open(first,player.GetComponent<Inventory>());var pending=ui.SendAsync("Ответь очень подробно");Assert.IsTrue(first.Busy);ui.Close();
            ui.Open(second,player.GetComponent<Inventory>());var next=ui.SendAsync("Привет, торговец!");
            while(!pending.IsCompleted||!next.IsCompleted)yield return null;
            bool cancelled=false;try{pending.GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
            Assert.IsTrue(cancelled);Assert.IsTrue(next.GetAwaiter().GetResult().Validation.Accepted);yield return null;
            Assert.AreEqual(second,ui.ActiveNpc);Assert.IsTrue(ui.IsOpen);Assert.IsFalse(first.Busy);Assert.IsFalse(first.PlayerConversationActive);
            Assert.AreEqual(0,first.ConversationHistory.Count(line=>line.IsAi));Assert.AreEqual(2,second.ConversationHistory.Count);
            var labels=ui.View.Query<Label>(className:"speech-text").ToList();Assert.AreEqual(2,labels.Count);StringAssert.Contains("торговец",labels[0].text);Assert.IsFalse(labels.Any(label=>label.text.Contains("очень подробно")));
        }
        [UnityTest] public IEnumerator DisablingOrDestroyingActiveNpcClosesDialogueAndReleasesPlayer()
        {
            var player=Player(out var world);var first=Actor(world,"guard",new Vector3(1,3,0));var social=first.gameObject.AddComponent<NpcSocialAutonomy>();var ui=player.Conversation;ui.Open(first);first.enabled=false;
            yield return new WaitForSeconds(.35f);Assert.IsFalse(ui.IsOpen);Assert.IsNull(ui.ActiveNpc);Assert.IsFalse(first.PlayerConversationActive);Assert.IsFalse(social.HoldsMovement);Assert.IsFalse(social.ConversationActive);
            var second=Actor(world,"merchant",new Vector3(2,3,0));ui.Open(second);Object.Destroy(second.gameObject);
            yield return new WaitForSeconds(.35f);Assert.IsFalse(ui.IsOpen);Assert.IsNull(ui.ActiveNpc);
        }
        [UnityTest] public IEnumerator ComponentDisableDuringPostDecisionEmbeddingCancelsActualUnityLifecycleAndSuppressesReply()
        {
            var player=Player(out var world);var npc=Actor(world,"guard",new Vector3(1,3,0));npc.UseRag=true;
            var embedding=new DisableDuringMemoryEmbedding(()=>npc.enabled=false);npc.ConfigureInference(new PostDecisionTalkRouter(),embedding);
            var utterances=new List<NpcUtterance>();var statuses=new List<string>();npc.SpeechReceived+=line=>utterances.Add(line);npc.AiStatusChanged+=value=>statuses.Add(value);
            string oldSpeech=npc.LastSpeech;var ui=player.Conversation;ui.Open(npc,player.GetComponent<Inventory>());
            var pending=ui.SendAsync("Ответь, не меняя мир.");while(!pending.IsCompleted)yield return null;var trace=pending.GetAwaiter().GetResult();
            Assert.IsTrue(embedding.MemoryIndexingStarted,"Component disable must happen inside post-decision embedding, after a valid Talk decision.");
            Assert.IsTrue(embedding.CancellationObserved,"In actual PlayMode, IntelligentNPC.OnDisable must cancel the active linked token.");
            Assert.IsTrue(trace.Validation.Accepted);Assert.AreEqual(1,trace.Validation.Executed.Count);StringAssert.StartsWith("Talk(",trace.Validation.Executed[0]);
            Assert.IsTrue(trace.ReplySuppressed);Assert.AreSame(trace,TraceLog.Entries.Last());StringAssert.Contains("memory indexing cancelled",trace.Error);
            Assert.AreEqual(oldSpeech,npc.LastSpeech);Assert.AreEqual(1,utterances.Count);Assert.IsTrue(utterances[0].IsPlayer);Assert.AreEqual(0,npc.ConversationHistory.Count(line=>line.IsAi));Assert.IsFalse(npc.Busy);Assert.IsFalse(statuses.Contains("Ответ готов"));
            yield return new WaitForSeconds(.35f);Assert.IsFalse(ui.IsOpen);Assert.IsNull(ui.ActiveNpc);Assert.IsFalse(npc.PlayerConversationActive);
        }
        [UnityTest] public IEnumerator ExplicitQuestActionChangesInventoryOnceWhileOpenDialogueAloneDoesNot()
        {
            var player=Player(out var world);var giver=Actor(world,"giver",new Vector3(1,3,0));var recipient=Actor(world,"recipient",new Vector3(2,3,0));
            var quest=world.gameObject.AddComponent<DeliveryQuest>();quest.Giver=giver.GetComponent<WorldPlace>();quest.Recipient=recipient.GetComponent<WorldPlace>();var inventory=player.GetComponent<Inventory>();var ui=player.Conversation;
            ui.Open(giver,inventory);Assert.AreEqual(DeliveryQuestStage.NotStarted,quest.Stage);Assert.AreEqual(0,inventory.Count(quest.LetterItem));
            var button=ui.View.Q<Button>(className:"quest-action");Assert.IsTrue(button.enabledInHierarchy);StringAssert.Contains("взять письмо",button.text);
            InvokeQuest(ui);Assert.AreEqual(DeliveryQuestStage.CarryingLetter,quest.Stage);Assert.AreEqual(1,inventory.Count(quest.LetterItem));
            ui.Open(recipient,inventory);Assert.AreEqual(0,inventory.Count(quest.RewardItem));InvokeQuest(ui);
            Assert.AreEqual(DeliveryQuestStage.Completed,quest.Stage);Assert.AreEqual(quest.RewardAmount,inventory.Count(quest.RewardItem));Assert.AreEqual(0,inventory.Count(quest.LetterItem));
            InvokeQuest(ui);Assert.AreEqual(quest.RewardAmount,inventory.Count(quest.RewardItem));Assert.IsFalse(ui.View.Q<Button>(className:"quest-action").enabledInHierarchy);
            yield return null;
        }
        sealed class PostDecisionTalkRouter:IModelRouter
        {
            public Task<CompletionResult> CompleteAsync(ModelTask task,CompletionRequest request,CancellationToken token)
            {
                token.ThrowIfCancellationRequested();return Task.FromResult(new CompletionResult {Text="{\"speech\":\"Эта реплика не должна появиться после отключения.\",\"actions\":[{\"type\":\"Talk\",\"target\":\"\",\"item\":\"\",\"amount\":0}]}",Provider="explicit-post-decision-ui-fixture"});
            }
            public void Dispose(){}
        }
        sealed class DisableDuringMemoryEmbedding:IEmbeddingProvider
        {
            readonly Action disable;public bool MemoryIndexingStarted,CancellationObserved;
            public DisableDuringMemoryEmbedding(Action disable)=>this.disable=disable;
            public string Identity=>"explicit-post-decision-embedding-fixture";
            public Task<float[]> EmbedAsync(string text,bool query,CancellationToken token)
            {
                if(!query) {MemoryIndexingStarted=true;disable();CancellationObserved=token.IsCancellationRequested;token.ThrowIfCancellationRequested();}
                return Task.FromResult(new[]{1f,0f});
            }
            public void Dispose(){}
        }
        sealed class ReplyRouter:IModelRouter
        {
            public Task<CompletionResult> CompleteAsync(ModelTask task,CompletionRequest request,CancellationToken token) {token.ThrowIfCancellationRequested();return Task.FromResult(new CompletionResult {Text=new JObject {["speech"]="Поговорим об этом своими словами.",["actions"]=new JArray()}.ToString(),Provider="explicit-ui-test-fixture"});}
            public void Dispose(){}
        }
        sealed class BlockingRouter:IModelRouter
        {
            public async Task<CompletionResult> CompleteAsync(ModelTask task,CompletionRequest request,CancellationToken token) {await Task.Delay(30000,token);throw new InvalidOperationException("Cancellation must stop this fixture.");}
            public void Dispose(){}
        }
        sealed class NoEmbedding:IEmbeddingProvider
        {
            public string Identity=>"not-required-for-ui";
            public Task<float[]> EmbedAsync(string text,bool query,CancellationToken token)=>throw new InvalidOperationException("UI fixture must not call embeddings.");
            public void Dispose(){}
        }
    }
}
