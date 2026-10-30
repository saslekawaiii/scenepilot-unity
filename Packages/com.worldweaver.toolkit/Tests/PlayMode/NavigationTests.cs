using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
namespace WorldWeaver.Tests
{
    public sealed class NavigationTests
    {
        [UnityTest] public IEnumerator MoveAndFollowUseNavMesh()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = new Vector3(28, 14, -25); floor.transform.localScale = new Vector3(35, 1, 28);
            var navObject = new GameObject("Nav"); navObject.SetActive(false); var nav = navObject.AddComponent<DemoNavigation>(); nav.Surfaces = new[] { floor.GetComponent<MeshFilter>() }; navObject.SetActive(true);
            var obj = new GameObject("Npc"); obj.transform.position = new Vector3(23, 14.5f, -25); var agent = obj.AddComponent<NavMeshAgent>(); var npc = obj.AddComponent<IntelligentNPC>();
            var target = new GameObject("House"); target.transform.position = new Vector3(29, 14.5f, -25); var entity = target.AddComponent<WorldEntity>(); entity.Id = "House"; npc.KnownEntities.Add(entity);
            yield return null; Assert.IsTrue(agent.isOnNavMesh);
            var report = new ActionDispatcher().Execute(npc, new NpcDecision { speech = "Иду", actions = new[] { new ActionCommand { type = "MoveTo", target = "House", item = "", amount = 0 } } });
            Assert.IsTrue(report.Accepted, report.Reason); var start = obj.transform.position;
            yield return new WaitForSeconds(1); Assert.Greater(Vector3.Distance(start, obj.transform.position), .2f); Assert.AreEqual(NpcState.Moving, npc.State);
            var follow = new ActionDispatcher().Execute(npc, new NpcDecision { speech = "Следую", actions = new[] { new ActionCommand { type = "Follow", target = "House", item = "", amount = 0 } } });
            Assert.IsTrue(follow.Accepted); Assert.AreEqual(NpcState.Following, npc.State); npc.StopMovement(); Assert.IsTrue(agent.isStopped);
            Object.Destroy(obj); Object.Destroy(target); Object.Destroy(navObject); Object.Destroy(floor); yield return null;
        }
        [UnityTest] public IEnumerator GiveItemInPlayMode()
        {
            var obj = new GameObject("Npc"); var npc = obj.AddComponent<IntelligentNPC>(); npc.GetComponent<Inventory>().Add("Crystal", 10);
            var player = new GameObject("Player"); var inventory = player.AddComponent<Inventory>(); var entity = player.AddComponent<WorldEntity>(); entity.Id = "Player"; npc.KnownEntities.Add(entity);
            var result = new ActionDispatcher().Execute(npc, new NpcDecision { speech = "Держи", actions = new[] { new ActionCommand { type = "GiveItem", target = "Player", item = "Crystal", amount = 1 } } });
            Assert.IsTrue(result.Accepted); Assert.AreEqual(9, npc.GetComponent<Inventory>().Count("Crystal")); Assert.AreEqual(1, inventory.Count("Crystal"));
            Object.Destroy(obj); Object.Destroy(player); yield return null;
        }
    }
}
