using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
namespace WorldWeaver.Tests
{
    /// <summary>Opt-in tests with real inference. WW_REAL_TESTS=1; no mock or scripted action fallback.</summary>
    public sealed class RealProviderIntegration
    {
        [UnityTest] public IEnumerator RealLocalPipeline() => Run(false);
        [UnityTest] public IEnumerator RealFreeCloudPipeline() => Run(true);
        private static IEnumerator Run(bool cloud)
        {
            if (Environment.GetEnvironmentVariable("WW_REAL_TESTS") != "1") Assert.Ignore("Set WW_REAL_TESTS=1 for actual network inference.");
            var owned = new List<Object>();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor); floor.transform.position = new Vector3(28,14,-25); floor.transform.localScale = new Vector3(35,1,28);
            var navObj = new GameObject("Real test NavMesh"); owned.Add(navObj); navObj.SetActive(false); var nav = navObj.AddComponent<DemoNavigation>(); nav.Surfaces = new[]{floor.GetComponent<MeshFilter>()}; navObj.SetActive(true);
            var obj = new GameObject("Real Merchant");owned.Add(obj);obj.transform.position=new Vector3(23,14.5f,-25);obj.AddComponent<NavMeshAgent>();var npc=obj.AddComponent<IntelligentNPC>();npc.GetComponent<Inventory>().Add("Crystal",10);
            var player=new GameObject("Player");owned.Add(player);player.transform.position=new Vector3(25,14.5f,-27);var inventory=player.AddComponent<Inventory>();var playerEntity=player.AddComponent<WorldEntity>();playerEntity.Id="Player";
            var house=new GameObject("House");owned.Add(house);house.transform.position=new Vector3(34,14.5f,-21);var houseEntity=house.AddComponent<WorldEntity>();houseEntity.Id="House";
            npc.KnownEntities.Add(playerEntity);npc.KnownEntities.Add(houseEntity);
            var local=ScriptableObject.CreateInstance<ModelProfile>();owned.Add(local);local.Model="qwen2.5-coder:latest";
            var dialogue=local;
            if(cloud){dialogue=ScriptableObject.CreateInstance<ModelProfile>();owned.Add(dialogue);dialogue.Provider=ProviderKind.Pollinations;dialogue.Endpoint="https://text.pollinations.ai";dialogue.Model="openai-fast";dialogue.StrictSchema=false;dialogue.TimeoutSeconds=120;dialogue.Retries=2;}
            var routes=ScriptableObject.CreateInstance<OrchestratorProfile>();owned.Add(routes);routes.Terrain=local;routes.Planning=local;routes.Dialogue=dialogue;npc.Models=routes;
            var knowledge=ScriptableObject.CreateInstance<KnowledgeBase>();owned.Add(knowledge);knowledge.Documents=VillageKnowledge.Documents();npc.Knowledge=knowledge;
            yield return null;
            var initial=npc.transform.position;
            var report=new JObject{["provider"]=cloud?"Pollinations":"Ollama",["unity_version"]=Application.unityVersion,["timestamp"]=DateTime.UtcNow.ToString("O"),["inventory_before"]=10};
            var task=Scenario(npc,report);
            while(!task.IsCompleted)yield return null;
            if(task.IsFaulted)report["error"]=task.Exception.GetBaseException().GetType().Name+": "+task.Exception.GetBaseException().Message;
            yield return new WaitForSeconds(3);
            report["inventory_after"]=npc.GetComponent<Inventory>().Count("Crystal");report["player_crystals"]=inventory.Count("Crystal");report["distance_moved"]=Vector3.Distance(initial,npc.transform.position);
            report["success"]=!task.IsFaulted&&inventory.Count("Crystal")==1&&Vector3.Distance(initial,npc.transform.position)>.3f;
            var root=Directory.GetParent(Application.dataPath).Parent.FullName;File.WriteAllText(Path.Combine(root,"results/end-to-end-"+(cloud?"cloud":"local")+".json"),report.ToString(Formatting.Indented));
            foreach(var item in owned)if(item)Object.Destroy(item);
            yield return null;
            Assert.IsTrue((bool)report["success"],report.ToString(Formatting.Indented));
        }
        private static async Task Scenario(IntelligentNPC npc,JObject report)
        {
            var turns = new JArray(); report["dialogue_turns"] = turns;
            foreach (var proposal in new[] {
                "Мне нужен один Crystal для больного брата. Я верну долг после урожая. Если решишь помочь, передай один кристалл игроку Player сейчас.",
                "Понимаю твои сомнения. Согласен на долг в семь серебряных монет за один Crystal. Обязуюсь вернуть до второго вечернего колокола. Дай только один, чтобы не рисковать всем запасом.",
                "Договорились: один Crystal за семь серебряных монет в долг до второго вечернего колокола. Принимаю твои условия. Можешь передать один Crystal игроку Player и записать долг?" })
            {
                var trace = await npc.TalkAsync(proposal); turns.Add(JToken.FromObject(trace)); report["dialogue"] = JToken.FromObject(trace);
                if (npc.GetComponent<Inventory>().Count("Crystal") < 10) break;
            }
            var plan=await npc.PlanAsync();report["planning"]=JToken.FromObject(plan);
        }
    }
}
