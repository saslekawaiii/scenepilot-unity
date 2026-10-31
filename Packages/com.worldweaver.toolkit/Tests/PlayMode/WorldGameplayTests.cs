using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace WorldWeaver.Tests
{
    public sealed class WorldGameplayTests
    {
        [UnityTest]
        public IEnumerator NamedPatrolUsesCompletePathAroundObstacle()
        {
            var root=new GameObject("Gameplay navigation test");
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root.transform);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(20,1,20);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(root.transform);wall.transform.position=new Vector3(0,1.5f,0);wall.transform.localScale=new Vector3(1,3,9);
            var nav=root.AddComponent<WorldNavigation>();Assert.IsTrue(nav.Rebuild(),nav.LastError);
            var start=new Vector3(-4,0,0);var finish=new Vector3(4,0,0);
            Assert.IsTrue(WorldNavigation.TryPath(start,finish,out var path));Assert.GreaterOrEqual(path.corners.Length,3,"A path must go around the wall.");
            var first=new GameObject("start");first.transform.SetParent(root.transform);first.transform.position=start;var p1=first.AddComponent<WorldPlace>();p1.Key="start";
            var second=new GameObject("finish");second.transform.SetParent(root.transform);second.transform.position=finish;var p2=second.AddComponent<WorldPlace>();p2.Key="finish";
            var npc=new GameObject("guard");npc.transform.SetParent(root.transform);npc.transform.position=start;var agent=npc.AddComponent<NavMeshAgent>();agent.radius=.35f;agent.speed=8;agent.acceleration=30;
            var behavior=npc.AddComponent<SceneBehavior>();behavior.Mode=SceneBehaviorKind.Patrol;behavior.NamedWaypoints=new[]{p2,p1};behavior.Speed=8;
            float deadline=Time.time+10;bool movedAround=false;
            while(Time.time<deadline&&behavior.CompletedLegs<1)
            {
                yield return null;
                if(Mathf.Abs(npc.transform.position.z)>4.5f)movedAround=true;
                Assert.IsFalse(Mathf.Abs(npc.transform.position.x)<.45f&&Mathf.Abs(npc.transform.position.z)<4.4f,"Agent crossed the wall.");
            }
            Assert.IsTrue(movedAround);Assert.GreaterOrEqual(behavior.CompletedLegs,1,behavior.LastNavigationError);Assert.IsNull(behavior.LastNavigationError);
            Object.Destroy(root);yield return null;
        }
        [UnityTest]
        public IEnumerator UnreachablePatrolDoesNotMoveThroughEmptySpace()
        {
            var root=new GameObject("Disconnected navigation test");
            foreach(float x in new[]{-6f,6f}) { var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root.transform);floor.transform.position=new Vector3(x,-.5f,0);floor.transform.localScale=new Vector3(4,1,4); }
            var nav=root.AddComponent<WorldNavigation>();Assert.IsTrue(nav.Rebuild(),nav.LastError);
            var npc=new GameObject("npc");npc.transform.SetParent(root.transform);npc.transform.position=new Vector3(-6,0,0);npc.AddComponent<NavMeshAgent>();var behavior=npc.AddComponent<SceneBehavior>();behavior.Mode=SceneBehaviorKind.Patrol;behavior.Waypoints=new[]{new Vector3(6,0,0),new Vector3(-6,0,0)};
            yield return new WaitForSeconds(.7f);Assert.That(Vector3.Distance(npc.transform.position,new Vector3(-6,0,0)),Is.LessThan(.2f));Assert.AreEqual("Destination is unreachable.",behavior.LastNavigationError);
            Object.Destroy(root);yield return null;
        }
        [UnityTest]
        public IEnumerator DeliveryInventoryRewardAndScopedMemorySurviveDiskReload()
        {
            var root=new GameObject("Saved quest world");var save=root.AddComponent<WorldSaveState>();string path=save.SavePath;
            var giver=new GameObject("giver");giver.transform.SetParent(root.transform);var gp=giver.AddComponent<WorldPlace>();gp.Key="giver";var gn=giver.AddComponent<IntelligentNPC>();gn.CharacterId="giver-unique";
            var recipient=new GameObject("recipient");recipient.transform.SetParent(root.transform);recipient.transform.position=Vector3.right*8;var rp=recipient.AddComponent<WorldPlace>();rp.Key="guard";var rn=recipient.AddComponent<IntelligentNPC>();rn.CharacterId="guard-unique";
            var player=new GameObject("player");player.transform.SetParent(root.transform);player.AddComponent<WorldPlace>();var inv=player.AddComponent<Inventory>();
            var questObject=new GameObject("quest");questObject.transform.SetParent(root.transform);var quest=questObject.AddComponent<DeliveryQuest>();quest.Giver=gp;quest.Recipient=rp;
            Assert.IsTrue(quest.Interact(gp,inv));Assert.AreEqual(1,inv.Count("Письмо"));Assert.IsFalse(quest.Interact(gp,inv));Assert.AreEqual(1,inv.Count("Письмо"));
            save.Save();Assert.IsTrue(File.Exists(path));player.transform.position=rp.transform.position;Assert.IsTrue(quest.Interact(rp,inv));Assert.AreEqual(5,inv.Count("Монета"));Assert.IsFalse(quest.Interact(rp,inv));Assert.AreEqual(5,inv.Count("Монета"));
            save.Load();Assert.AreEqual(DeliveryQuestStage.CarryingLetter,quest.Stage);Assert.AreEqual(1,inv.Count("Письмо"));Assert.AreEqual(0,inv.Count("Монета"));Assert.AreEqual(0,rn.ExportMemory().Count);
            player.transform.position=rp.transform.position;Assert.IsTrue(quest.Interact(rp,inv));save.Save();quest.Stage=DeliveryQuestStage.NotStarted;inv.Remove("Монета",5);rn.ClearMemory();save.Load();
            Assert.AreEqual(DeliveryQuestStage.Completed,quest.Stage);Assert.AreEqual(5,inv.Count("Монета"));Assert.AreEqual(1,rn.ExportMemory().Count);Assert.AreEqual("guard-unique",rn.ExportMemory()[0].Scope);
            Assert.Throws<ArgumentException>(()=>gn.RestoreMemory(rn.ExportMemory()));Assert.IsFalse(quest.Interact(rp,inv));Assert.AreEqual(5,inv.Count("Монета"));
            File.Delete(path);Object.Destroy(root);yield return null;
        }
    }
}
