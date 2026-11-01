using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Ordinary API defaults, resolved on a copy. Explicit names and the original model audit are preserved.</summary>
    internal static class SceneCreationDefaults
    {
        static string Label(EditorCommand c)
        {
            switch(c.tool)
            {
                case "create_world":return "Мир";
                case "create_village":return "Деревня";
                case "create_npc":return "Персонаж";
                case "create_object":return (string)c.args?["kind"]=="sphere"?"Сфера":"Объект";
                case "scatter_objects":return "Группа объектов";
                case "create_camera":return "Камера";
                case "set_lighting":return string.IsNullOrEmpty(c.target)?"Свет":null;
                case "instantiate_prefab":return "Префаб";
                case "create_node":return "Узел";
                case "create_mesh":return "Меш";
                case "create_cave":return "Пещера";
                case "create_resource":return (string)c.args?["kind"]=="sample"?"Образец":"Кислород";
                case "create_ui_element":return (string)c.args?["kind"]=="canvas"?"Интерфейс":(string)c.args?["kind"]=="panel"?"Панель":(string)c.args?["kind"]=="button"?"Кнопка":(string)c.args?["kind"]=="slider"?"Ползунок":"Текст";
                default:return null;
            }
        }
        internal static EditorPlan Resolve(EditorPlan original)
        {
            if(original?.actions==null||!original.actions.Any(c=>c!=null&&c.name==""&&Label(c)!=null))return original;
            var scene=SceneManager.GetActiveScene();
            var names=scene.IsValid()&&scene.isLoaded?scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Select(t=>t.name).ToHashSet():new HashSet<string>();
            foreach(var c in original.actions.Where(c=>c!=null&&!string.IsNullOrEmpty(c.name)))names.Add(c.name);
            var result=new EditorPlan{reply=original.reply,complete=original.complete};
            foreach(var c in original.actions)
            {
                string label=c!=null&&c.name==""?Label(c):null;
                if(label==null){result.actions.Add(c);continue;}
                string name=label;for(int i=2;names.Contains(name);i++)name=label+" "+i;
                names.Add(name);result.actions.Add(new EditorCommand{tool=c.tool,name=name,target=c.target,args=c.args});
            }
            return result;
        }
    }
}
