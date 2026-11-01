using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace WorldWeaver.Tests
{
    public sealed class NpcSocialMovementTests
    {
        readonly List<Object> owned=new List<Object>();
        T Keep<T>(T value)where T:Object { owned.Add(value);return value; }
        [UnityTearDown] public IEnumerator Cleanup() { foreach(var obj in owned)if(obj)Object.Destroy(obj);owned.Clear();yield return null; }
        GameObject World()
        {
            var root=Keep(new GameObject("Social movement world"));root.AddComponent<WorldSaveState>();
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root.transform);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(30,1,16);
            var nav=root.AddComponent<WorldNavigation>();Assert.IsTrue(nav.Rebuild(),nav.LastError);return root;
        }
        IntelligentNPC Actor(GameObject root,string id,Vector3 position,out NpcSocialAutonomy social,out NavMeshAgent agent)
        {
            var go=new GameObject(id);go.transform.SetParent(root.transform);go.transform.position=position;var npc=go.AddComponent<IntelligentNPC>();npc.CharacterId=id;npc.NpcName=id;npc.UseRag=false;
            npc.Models=Keep(ScriptableObject.CreateInstance<OrchestratorProfile>());npc.ConfigureInference(new NoRewardRouter(),new NoEmbedding());
            agent=go.AddComponent<NavMeshAgent>();agent.speed=5;agent.acceleration=30;agent.radius=.35f;agent.stoppingDistance=.2f;
            social=go.AddComponent<NpcSocialAutonomy>();social.InteractionRadius=3;social.SearchRadius=20;social.MaxTurns=1;
            return npc;
        }
        [UnityTest] public IEnumerator NpcActuallyApproachesOnNavMeshAndPlayerConversationStopsIt()
        {
            var root=World();var first=Actor(root,"guard",Vector3.left*6,out var a,out var agent);var other=Actor(root,"merchant",Vector3.right*6,out var b,out _);
            var patrol=first.gameObject.AddComponent<SceneBehavior>();patrol.Mode=SceneBehaviorKind.Patrol;patrol.Speed=5;patrol.Waypoints=new[]{Vector3.left*12,Vector3.left*6};
            b.enabled=false;yield return null;b.enabled=true;
            a.RequestConversationSoon();var start=first.transform.position;float deadline=Time.unscaledTime+2;
            while(Time.unscaledTime<deadline&&Vector3.Distance(first.transform.position,start)<1)yield return null;
            Assert.Greater(first.transform.position.x,start.x+1,"The social approach must actually move toward the neighbor despite an existing patrol that wants to move in the opposite direction.");Assert.IsTrue(a.HoldsMovement);
            var stopped=first.transform.position;first.BeginPlayerConversation();
            Assert.IsFalse(a.HoldsMovement);Assert.IsTrue(first.PlayerConversationActive);Assert.IsTrue(agent.isStopped);Assert.IsFalse(agent.hasPath);
            float observeUntil=Time.realtimeSinceStartup+.6f,observeDeadline=Time.realtimeSinceStartup+4;int observedFrames=0;float maximumDrift=0;
            while((observedFrames<20||Time.realtimeSinceStartup<observeUntil)&&Time.realtimeSinceStartup<observeDeadline)
            {
                yield return null;observedFrames++;maximumDrift=Mathf.Max(maximumDrift,Vector3.Distance(first.transform.position,stopped));
                Assert.IsTrue(first.PlayerConversationActive);Assert.IsTrue(agent.isStopped);Assert.IsFalse(agent.hasPath);Assert.Less(maximumDrift,.05f,"A talking NPC must stop immediately and remain stopped while the existing patrol keeps updating. Velocity="+agent.velocity+", simulatedDelta="+(agent.nextPosition-stopped)+", observedFrames="+observedFrames);
            }
            TestContext.WriteLine("Actual NPC pause: observedFrames="+observedFrames+", maximumDriftMetres="+maximumDrift+", finalVelocity="+agent.velocity);
            Assert.GreaterOrEqual(observedFrames,20);Assert.Less(maximumDrift,.05f);
            first.EndPlayerConversation();
        }
        [UnityTest] public IEnumerator DisablingSocialAutonomyStopsItsApproachAndReleasesRoutineOwnership()
        {
            var root=World();var first=Actor(root,"guard",Vector3.left*6,out var a,out var agent);Actor(root,"merchant",Vector3.right*6,out _,out _);
            yield return null;a.RequestConversationSoon();float deadline=Time.unscaledTime+2;
            while(Time.unscaledTime<deadline&&!agent.hasPath)yield return null;
            Assert.IsTrue(agent.hasPath);Assert.IsTrue(a.HoldsMovement);
            a.Enabled=false;yield return null;Assert.IsFalse(a.HoldsMovement);Assert.IsFalse(agent.hasPath);Assert.IsFalse(a.ConversationActive);
            Assert.IsFalse(first.Busy);Assert.IsEmpty(first.ConversationHistory,"Navigation itself must not fabricate an NPC utterance.");
        }
        sealed class NoRewardRouter:IModelRouter
        {
            public Task<CompletionResult> CompleteAsync(ModelTask task,CompletionRequest request,CancellationToken token) {token.ThrowIfCancellationRequested();return Task.FromResult(new CompletionResult {Text=new JObject {["speech"]="Нам есть о чём поговорить.",["actions"]=new JArray()}.ToString(),Provider="explicit-navigation-test-fixture"});}
            public void Dispose(){}
        }
        sealed class NoEmbedding:IEmbeddingProvider
        {
            public string Identity=>"not-required-for-navigation";
            public Task<float[]> EmbedAsync(string text,bool query,CancellationToken token)=>throw new InvalidOperationException("This navigation fixture must not use embeddings.");
            public void Dispose(){}
        }
    }
}
