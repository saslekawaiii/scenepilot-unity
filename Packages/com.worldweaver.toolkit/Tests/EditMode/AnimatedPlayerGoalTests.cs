using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class AnimatedPlayerGoalTests
    {
        internal const string Request="Создай персонажа: сделай главного героя не используй примитив. используй реальную модель. добавь ей анимации ходьбы, передвижения и тд";
        static Type Goal=>typeof(ChatController).Assembly.GetType("WorldWeaver.Editor.Assistant.AgentAnimatedPlayerGoal",true);
        static object Invoke(Type type,string method,params object[] args)=>type.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
        [SetUp] public void Setup()
        {
            if(!Application.isBatchMode)Assert.Ignore("Uses an empty disposable batch scene.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        }
        [TestCase(Request)]
        [TestCase("Create a playable main character using a real model and walk animations.")]
        public void RequestedRealHeroGetsAnIndependentRequirementAndCompactTools(string text)
        {
            var task=AgentTaskVerifier.Create(text,true);
            Assert.That(task.Requirements.Any(r=>r.Kind=="animated_player"),Is.True);
            var tools=(HashSet<string>)Invoke(typeof(ChatController),"SelectTools",text,AssistantMode.Execute);
            Assert.That(tools,Does.Contain("create_animated_player"));
            Assert.That(tools,Does.Not.Contain("create_npc").And.Not.Contain("create_player").And.Not.Contain("draft_script"));
            Assert.That((HashSet<string>)Invoke(typeof(ChatController),"SelectTools",text,AssistantMode.Ask),Does.Not.Contain("create_animated_player"));
        }
        [Test] public void ExplanationOrNegatedCreationDoesNotGrantCharacterMutation()
        {
            Assert.That(AgentTaskVerifier.Create(Request,false).Requirements.Any(r=>r.Kind=="animated_player"),Is.False);
            Assert.That((bool)Invoke(Goal,"Requested","Не создавай главного героя с анимациями."),Is.False);
        }
        [Test] public void EmptyCompletionOrPrimitiveReceiptCannotPassTheRealCharacterGoal()
        {
            var answer=new AssistantMessage {Task=AgentTaskVerifier.Create(Request,true)};
            Func<IReadOnlyList<AgentCheckResult>> verify=()=> (IReadOnlyList<AgentCheckResult>)Invoke(typeof(ChatController),"VerifyTask",answer);
            Assert.That(verify().Single(c=>answer.Task.Requirements.Any(r=>r.Kind=="animated_player"&&r.Id==c.RequirementId)).Passed,Is.False);
            var primitive=GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                answer.Steps.Add(new EditorCommandResult{Tool="create_npc",Success=true,Target="id:"+primitive.GetInstanceID(),Summary="real model created"});
                Assert.That(verify().All(c=>c.Passed),Is.False,"Prose and a primitive tool cannot satisfy a real model requirement.");
            }
            finally {UnityEngine.Object.DestroyImmediate(primitive);}
        }
        [Test] public void GoalSchemaRemovesPrimitiveSubstitutesWithoutAddingPermissions()
        {
            var task=AgentTaskVerifier.Create(Request,true);
            var schema=EditorPlan.Schema(false,new[]{"create_npc","inspect_object"});
            Invoke(Goal,"Bind",schema,task);
            var tools=((JArray)schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName).ToArray();
            Assert.That(tools,Is.EquivalentTo(new[]{"inspect_object"}));
            var plan=new EditorPlan{actions=new List<EditorCommand>{new EditorCommand{tool="create_npc",name="Hero",target="",args=new JObject()}}};
            var error=Assert.Throws<TargetInvocationException>(()=>Invoke(Goal,"Validate",task,plan));
            Assert.That(error.InnerException,Is.TypeOf<ArgumentException>());
        }
        [TestCase("Создай главного героя с анимациями и добавь куб рядом.","create_object")]
        [TestCase("Создай главного героя с анимациями и сохрани сцену.","save_scene")]
        [TestCase("Создай главного героя с анимациями и добавь двух NPC.","create_npc")]
        public void CompoundCharacterRequestKeepsOtherRequestedToolsAndCannotFinishEarly(string text,string tool)
        {
            Assert.That((bool)Invoke(Goal,"SimpleRequest",text),Is.False);
            var tools=(HashSet<string>)Invoke(typeof(ChatController),"SelectTools",text,AssistantMode.Execute);
            Assert.That(tools,Does.Contain(tool).And.Contain("create_animated_player"));
            var task=AgentTaskVerifier.Create(text,true);var schema=EditorPlan.Schema(false,tools);Invoke(Goal,"Bind",schema,task);
            Assert.That(((JArray)schema["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Select(EditorToolSchema.ToolName),Does.Contain(tool));
        }
        [Test] public void ValidEmptyCharacterNameUsesDocumentedDefaultRatherThanRejectingThePlan()
        {
            var plan=new EditorPlan{actions=new List<EditorCommand>{new EditorCommand{tool="create_animated_player",name="",target="",args=new JObject{["preset"]="adventurer"}}}};
            Assert.DoesNotThrow(()=>SceneToolExecutor.Validate(plan));
        }
        [Test] public void FullSceneTransactionKeepsImportedHierarchyOwnedAndSatisfiesActualRequest()
        {
            var plan=new EditorPlan{actions=new List<EditorCommand>{new EditorCommand{tool="create_animated_player",name="",target="",args=new JObject{["preset"]="adventurer"}}}};
            EditorBatchResult result=null;Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();
            try
            {
                result=SceneToolExecutor.Execute(plan);Assert.IsTrue(result.Success,result.Error);
                var hero=SceneToolExecutor.ResolveTarget(result.Results.Single().Target);
                Assert.IsNotNull(hero.GetComponent<WorldPlayer>());
                Assert.That(hero.GetComponentsInChildren<Transform>(true).All(t=>t.GetComponent<GeneratedSceneObject>()),Is.True,"Imported mesh and bones must participate in ownership, Undo and object budgets.");
                var answer=new AssistantMessage{Task=AgentTaskVerifier.Create(Request,true)};answer.Steps.AddRange(result.Results);
                var checks=(IReadOnlyList<AgentCheckResult>)Invoke(typeof(ChatController),"VerifyTask",answer);
                Assert.That(checks.Count,Is.GreaterThan(0));Assert.That(checks.All(c=>c.Passed),Is.True);
                Undo.FlushUndoRecordObjects();Undo.RevertAllDownToGroup(undo);
                Assert.That(UnityEngine.Object.FindObjectsByType<WorldPlayer>(FindObjectsSortMode.None),Is.Empty);
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var assets=result?.Results.SelectMany(s=>s.CreatedAssets).Distinct().ToArray()??Array.Empty<string>();
                foreach(string path in assets.Reverse())if(AssetDatabase.LoadMainAssetAtPath(path))AssetDatabase.DeleteAsset(path);
                foreach(string folder in assets.Select(p=>Path.GetDirectoryName(p).Replace('\\','/')).Distinct())
                    if(AssetDatabase.IsValidFolder(folder)&&AssetDatabase.FindAssets("",new[]{folder}).Length==0)AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
