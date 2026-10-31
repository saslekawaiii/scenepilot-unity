using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Explicit demo creation. Existing saved scenes and build entries are preserved.</summary>
    public static class VillageDemoBuilder
    {
        [MenuItem("Tools/WorldWeaver/Создать играбельную деревню",priority=2)]
        public static void Create()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Дождитесь Edit Mode и завершения компиляции.");
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var plan=new EditorPlan{reply="Демонстрационный сценарий",actions={
                new EditorCommand{tool="create_village",name="Деревня WorldWeaver",args=new JObject{["houses"]=5,["trees"]=12,["width"]=128,["seed"]=42,["player"]=true,["quest"]=true}},
                new EditorCommand{tool="save_scene",args=new JObject{["path"]="Assets/WorldWeaverGenerated/Scenes/WorldWeaverVillage.unity"}}
            }};
            var batch=SceneToolExecutor.Execute(plan);if(!batch.Success)throw new InvalidOperationException(batch.Error);
            string path=EditorSceneManager.GetActiveScene().path;
            if(!EditorBuildSettings.scenes.Any(s=>s.path==path))EditorBuildSettings.scenes=EditorBuildSettings.scenes.Concat(new[]{new EditorBuildSettingsScene(path,true)}).ToArray();
            var world=UnityEngine.Object.FindFirstObjectByType<WorldSaveState>();Selection.activeGameObject=world?world.gameObject:null;
            if(!Application.isBatchMode)WorldWeaverChatWindow.ShowWindow();
            Debug.Log("Играбельная деревня сохранена: "+path+". Play: WASD, мышь, E — взаимодействие; Save/Load в игровом интерфейсе.");
        }
    }
}
