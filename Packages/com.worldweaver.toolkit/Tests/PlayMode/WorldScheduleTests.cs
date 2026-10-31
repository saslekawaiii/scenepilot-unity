using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace WorldWeaver.Tests
{
    public sealed class WorldScheduleTests
    {
        [Test] public void DailyIntervalsRespectOpeningClosingAndMidnight()
        {
            Assert.IsTrue(WorldClock.InPeriod(6,6,20));Assert.IsFalse(WorldClock.InPeriod(20,6,20));Assert.IsTrue(WorldClock.InPeriod(23,20,6));Assert.IsTrue(WorldClock.InPeriod(2,20,6));Assert.IsFalse(WorldClock.InPeriod(12,20,6));
        }
        [UnityTest] public IEnumerator DayNightScheduleActuallyWalksBetweenDifferentNamedPlaces()
        {
            var root=new GameObject("Daily schedule world");var clock=root.AddComponent<WorldClock>();var journal=root.AddComponent<WorldEventJournal>();
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root.transform);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(16,1,12);
            var square=NewPlace(root,"square",new Vector3(-3,0,0));var home=NewPlace(root,"home",new Vector3(3,0,0));var nav=root.AddComponent<WorldNavigation>();Assert.IsTrue(nav.Rebuild(),nav.LastError);
            var actor=new GameObject("Daily resident");actor.transform.SetParent(root.transform);actor.transform.position=square.transform.position;var npc=actor.AddComponent<IntelligentNPC>();npc.CharacterId="daily-resident";var agent=actor.AddComponent<NavMeshAgent>();agent.radius=.35f;agent.speed=5;agent.acceleration=30;
            var routine=actor.AddComponent<NpcDailyRoutine>();routine.Clock=clock;routine.DayPlace=square;routine.NightPlace=home;
            yield return new WaitForSeconds(.6f);Assert.AreEqual(square,routine.CurrentGoal);Assert.IsTrue(routine.GoalReached);
            clock.SetTime(22);yield return new WaitForSeconds(.5f);Assert.AreEqual(home,routine.CurrentGoal);Assert.IsFalse(routine.GoalReached);Assert.AreEqual(home,routine.ActualDestination);
            float deadline=Time.time+4;while(Time.time<deadline&&!routine.GoalReached)yield return null;Assert.IsTrue(routine.GoalReached,routine.Status);Assert.Less(Vector3.Distance(actor.transform.position,home.ArrivalPosition),.6f);yield return new WaitForSeconds(.5f);
            Assert.IsTrue(npc.ExportMemory().Any(m=>m.Text.Contains("Прибыл в home")));Assert.IsTrue(journal.Events.Any(e=>e.Kind=="npc_arrived"&&e.Text.Contains("home")));
            clock.SetTime(8);yield return new WaitForSeconds(.5f);Assert.AreEqual(square,routine.CurrentGoal);Assert.IsFalse(routine.GoalReached);deadline=Time.time+4;while(Time.time<deadline&&!routine.GoalReached)yield return null;Assert.IsTrue(routine.GoalReached,routine.Status);Assert.Less(Vector3.Distance(actor.transform.position,square.ArrivalPosition),.6f);
            Object.Destroy(root);yield return null;
        }
        [UnityTest] public IEnumerator GateCarvingBlocksRouteRoutineUsesFallbackAndSavedClockTrustEventsReload()
        {
            var root=new GameObject("Schedule test world");var save=root.AddComponent<WorldSaveState>();string file=save.SavePath;var clock=root.AddComponent<WorldClock>();clock.SetTime(22);var events=root.AddComponent<WorldEventJournal>();
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root.transform);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(20,1,30);
            foreach(float side in new[]{-1f,1f}){var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(root.transform);wall.transform.position=new Vector3(side*5.75f,1.5f,0);wall.transform.localScale=new Vector3(8.5f,3,1);}
            var inside=NewPlace(root,"inside",new Vector3(0,0,-4));var outside=NewPlace(root,"outside",new Vector3(0,0,4));var fallback=NewPlace(root,"fallback",new Vector3(0,0,-8));inside.Connections=new[]{outside};outside.Connections=new[]{inside};
            var gateObject=new GameObject("North gate");gateObject.transform.SetParent(root.transform);var panel=GameObject.CreatePrimitive(PrimitiveType.Cube);panel.transform.SetParent(gateObject.transform);panel.transform.position=new Vector3(0,1.5f,0);panel.transform.localScale=new Vector3(3,3,1);var obstacle=panel.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Box;obstacle.size=Vector3.one;obstacle.carving=true;obstacle.carveOnlyStationary=true;obstacle.carvingTimeToStationary=0;
            var gate=gateObject.AddComponent<WorldGate>();gate.Clock=clock;gate.Panel=panel;gate.Inside=inside;gate.Outside=outside;gate.ApplyClock();
            var nav=root.AddComponent<WorldNavigation>();Assert.IsTrue(nav.Rebuild(),nav.LastError);
            var actor=new GameObject("guard");actor.transform.SetParent(root.transform);actor.transform.position=inside.transform.position;actor.AddComponent<WorldPlace>().Key="guard";var npc=actor.AddComponent<IntelligentNPC>();npc.CharacterId="schedule-guard";var agent=actor.AddComponent<NavMeshAgent>();agent.radius=.35f;agent.speed=5;agent.acceleration=30;var relationships=actor.AddComponent<NpcRelationships>();relationships.ChangeTrust("player-id",10);
            var routine=actor.AddComponent<NpcDailyRoutine>();routine.Clock=clock;routine.DayPlace=outside;routine.NightPlace=outside;routine.BlockedFallback=fallback;npc.ContextSources=new MonoBehaviour[]{clock,gate,routine,relationships,events};
            yield return new WaitForSeconds(.8f);
            Assert.IsFalse(gate.IsOpen);Assert.IsTrue(panel.GetComponent<Collider>().enabled&&panel.activeInHierarchy);Assert.IsFalse(WorldNavigation.TryPath(inside.transform.position,outside.transform.position,out _));Assert.IsFalse(routine.GoalReached);Assert.AreEqual(fallback,routine.ActualDestination);Assert.IsNotNull(routine.BlockedReason);Assert.Less(actor.transform.position.z,0);Assert.IsEmpty(nav.ValidateConnections(),"A deliberately closed gate is not a malformed graph.");
            Assert.IsFalse(npc.ExportMemory().Any(m=>m.Text.Contains("Прибыл в outside")),"Blocked destination cannot become a completed goal in memory.");save.Save();int savedEventCount=events.Events.Count;
            clock.SetTime(8);yield return new WaitForSeconds(.5f);Assert.IsTrue(gate.IsOpen);Assert.IsFalse(panel.activeSelf);Assert.IsTrue(WorldNavigation.TryPath(inside.transform.position,outside.transform.position,out _));
            float deadline=Time.time+5;while(Time.time<deadline&&!routine.GoalReached)yield return null;Assert.IsTrue(routine.GoalReached,routine.Status);Assert.Less(Vector3.Distance(actor.transform.position,outside.transform.position),.6f);yield return new WaitForSeconds(.5f);Assert.IsTrue(npc.ExportMemory().Any(m=>m.Text.Contains("Прибыл в outside")));
            relationships.ChangeTrust("player-id",-20);events.Record("test","later event");save.Load();Assert.AreEqual(22,clock.Hour);Assert.AreEqual(10,relationships.Trust("player-id"));Assert.AreEqual(savedEventCount,events.Events.Count,"Load must not invent a new gate event.");
            yield return new WaitForSeconds(.7f);Assert.IsFalse(gate.IsOpen);Assert.IsFalse(WorldNavigation.TryPath(inside.transform.position,outside.transform.position,out _));Assert.IsFalse(routine.GoalReached);Assert.IsNotNull(routine.BlockedReason);
            var context=NpcPipeline.BuildContext(npc,new List<KnowledgeChunk>()).ToString();StringAssert.Contains("Primary destination is unreachable",context);StringAssert.Contains("22",context);
            File.Delete(file);Object.Destroy(root);yield return null;
        }
        static WorldPlace NewPlace(GameObject root,string key,Vector3 position)
        {var go=new GameObject(key);go.transform.SetParent(root.transform);go.transform.position=position;var place=go.AddComponent<WorldPlace>();place.Key=key;return place;}
    }
}
