using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    internal static class SurvivalConstruction
    {
        internal static void Configure(EditorCommand c,SceneBuildTransaction tx,GameObject player,Func<string,GameObject> resolve)
        {
            var world=resolve((string)c.args["world"]);var terrain=world.GetComponentInChildren<Terrain>();
            var env=world.GetComponent<WorldEnvironment>()??Undo.AddComponent<WorldEnvironment>(world);Undo.RecordObject(env,"Configure survival environment");env.Terrain=terrain;var marker=world.GetComponent<GeneratedSceneObject>();if(env.Profile=="custom"&&marker&&!string.IsNullOrEmpty(marker.Style))env.Profile=marker.Style;
            var survival=player.GetComponent<WorldSurvival>()??Undo.AddComponent<WorldSurvival>(player);Undo.RecordObject(survival,"Configure survival");survival.Environment=env;survival.OxygenSeconds=(float?)c.args["oxygen_seconds"]??180;
            var control=player.GetComponent<WorldPlayer>();Undo.RecordObject(control,"Set local player gravity");control.UseLocalGravity=true;control.Gravity=(float?)c.args["gravity"]??(env.Profile=="moon"?1.62f:9.81f);
            Vector3 at=WorldSitePlacement.FindHabitatSite(terrain,survival.Habitat?survival.Habitat.position:player.transform.position,survival.Habitat);
            if(!survival.Habitat)
            {
                var habitat=tx.Empty("База восстановления",world.transform,"habitat");habitat.transform.position=at;survival.Habitat=habitat.transform;
                var shell=tx.Material("Habitat panels",new Color(.74f,.80f,.84f));var floor=tx.Material("Habitat floor stone",new Color(.24f,.29f,.33f));
                tx.Primitive("Floor",PrimitiveType.Cube,habitat.transform,floor,new Vector3(0,-.12f,0),new Vector3(8,.24f,8));
                tx.Primitive("Roof",PrimitiveType.Cube,habitat.transform,shell,new Vector3(0,4.2f,0),new Vector3(8,.25f,8));
                tx.Primitive("Back",PrimitiveType.Cube,habitat.transform,shell,new Vector3(0,2,4),new Vector3(8,4,.25f));
                foreach(int side in new[]{-1,1})
                {tx.Primitive("Side",PrimitiveType.Cube,habitat.transform,shell,new Vector3(side*4,2,0),new Vector3(.25f,4,8));tx.Primitive("Airlock side",PrimitiveType.Cube,habitat.transform,shell,new Vector3(side*2.8f,2,-4),new Vector3(2.4f,4,.25f));}
                tx.Primitive("Airlock lintel",PrimitiveType.Cube,habitat.transform,shell,new Vector3(0,3.6f,-4),new Vector3(3.2f,.8f,.25f));
                var lamp=tx.Empty("Recovery light",habitat.transform,"light");lamp.transform.localPosition=new Vector3(0,3.5f,0);var light=Undo.AddComponent<Light>(lamp);light.type=LightType.Point;light.color=new Color(.36f,1,.79f);light.range=10;light.intensity=2;
            }
            Undo.RecordObject(survival.Habitat,"Place habitat at a clear grounded site");survival.Habitat.position=at;
            survival.Spawn=survival.Habitat.position+Vector3.up*.1f;
            Undo.RecordObject(player.transform,"Ground player in survival world");player.transform.position=survival.Spawn;EditorUtility.SetDirty(survival);EditorUtility.SetDirty(control);
        }
        internal static GameObject Resource(EditorCommand c,SceneBuildTransaction tx,GameObject world)
        {
            var position=ConstructiveTools.V(c.args["position"]);var terrain=world.GetComponentInChildren<Terrain>();position.y=terrain.SampleHeight(position)+terrain.transform.position.y+.5f;
            bool sample=(string)c.args["kind"]=="sample";
            var go=tx.Primitive(c.name,sample?PrimitiveType.Sphere:PrimitiveType.Cylinder,world.transform,tx.Material(sample?"Sample mineral":"Oxygen reserve",sample?new Color(.4f,.7f,1):new Color(.15f,.85f,.55f)),position,new Vector3(.5f,.5f,.5f));
            go.GetComponent<GeneratedSceneObject>().Kind="resource";Undo.AddComponent<WorldResourceNode>(go).IsSample=sample;return go;
        }
    }
}
