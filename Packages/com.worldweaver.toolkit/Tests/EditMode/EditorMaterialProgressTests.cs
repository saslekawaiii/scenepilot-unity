using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver.Editor.Assistant;
namespace WorldWeaver.Tests
{
    public sealed class EditorMaterialProgressTests
    {
        [Test] public async System.Threading.Tasks.Task ExistingMaterialEditCountsAsProgressButAnIdenticalAssignmentDoesNot()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.AddComponent<GeneratedSceneObject>();var mat=new Material(Shader.Find("UI/Default"));cube.GetComponent<Renderer>().sharedMaterial=mat;
            using(var controller=new ChatController(historyFolder:"UserSettings/MaterialProgress-"+Guid.NewGuid().ToString("N")))
            {
                controller.Current.Settings.AllowEditorCode=true;controller.Current.Settings.Mode=AssistantMode.Execute;
                var context=new AgentExecutionContext{Session=controller.Current,Message=new AssistantMessage()};
                var command=new EditorCommand{tool="execute_editor_code",target=ProjectContextIndex.StableTarget(cube),args=new JObject{["code"]="target.GetComponent<Renderer>().sharedMaterial.color=Color.red; return target.GetComponent<Renderer>().sharedMaterial.color;"}};
                var plan=new EditorPlan{actions={command}};
                var changed=await SceneToolExecutor.ExecuteAsync(plan,null,context,default);Assert.IsTrue(changed.Success,changed.Error);Assert.AreEqual(Color.red,mat.color);Assert.IsTrue((bool)JObject.Parse(changed.Results.Single().Summary)["sceneChanged"],"Material properties are part of implementation state even when renderer references remain the same");
                var unchanged=await SceneToolExecutor.ExecuteAsync(plan,null,context,default);Assert.IsTrue(unchanged.Success,unchanged.Error);Assert.IsFalse((bool)JObject.Parse(unchanged.Results.Single().Summary)["sceneChanged"],"An unchanged material assignment is not new progress");
            }
            UnityEngine.Object.DestroyImmediate(mat);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        }
    }
}
