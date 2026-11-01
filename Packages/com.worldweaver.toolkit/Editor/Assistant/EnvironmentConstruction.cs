using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    internal static class EnvironmentConstruction
    {
        internal static void Configure(EditorCommand c,SceneBuildTransaction tx,GameObject world)
        {
            var a=c.args;bool night=(bool?)a["night"]??false;
            Color sky=a["sky_color"]==null?(night?new Color(.003f,.005f,.012f):new Color(.49f,.70f,.84f)):ConstructiveTools.C(a["sky_color"]);
            foreach(var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(camera.gameObject.scene==world.scene){Undo.RecordObject(camera,"Set environment background");camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=sky;camera.farClipPlane=Mathf.Max(camera.farClipPlane,700);}
            foreach(var light in world.GetComponentsInChildren<Light>(true))
                if(light.type==LightType.Directional){Undo.RecordObject(light,"Set environment light");light.intensity=(float?)a["light_intensity"]??(night?.65f:1.25f);light.color=night?new Color(.66f,.75f,1):new Color(1,.96f,.88f);}
            int count=(int?)a["stars"]??(night?1400:0),seed=(int?)a["seed"]??42;
            var previous=world.GetComponentInChildren<WorldStarfield>();
            if(previous)Undo.DestroyObjectImmediate(previous.gameObject);
            if(count==0)return;
            var go=tx.Empty("Starfield",world.transform,"starfield");var field=Undo.AddComponent<WorldStarfield>(go);field.StarCount=count;field.Seed=seed;
            var random=new System.Random(seed);var vertices=new List<Vector3>();var indices=new List<int>();var uvs=new List<Vector2>();var colors=new List<Color>();
            for(int i=0;i<count;i++)
            {
                float z=(float)(random.NextDouble()*2-1),angle=(float)(random.NextDouble()*Math.PI*2),r=Mathf.Sqrt(1-z*z);
                var direction=new Vector3(r*Mathf.Cos(angle),z,r*Mathf.Sin(angle));
                var right=Vector3.Cross(direction,Mathf.Abs(direction.y)>.95f?Vector3.right:Vector3.up).normalized;var up=Vector3.Cross(right,direction).normalized;
                float size=.14f+(float)Math.Pow(random.NextDouble(),5)*.55f,brightness=.38f+(float)random.NextDouble()*.62f;int start=vertices.Count;
                vertices.Add(direction*500-right*size-up*size);vertices.Add(direction*500-right*size+up*size);vertices.Add(direction*500+right*size+up*size);vertices.Add(direction*500+right*size-up*size);
                uvs.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right});indices.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
                for(int k=0;k<4;k++)colors.Add(new Color(brightness,brightness,brightness,1));
            }
            var mesh=tx.Asset(new Mesh{name="Seeded starfield",vertices=vertices.ToArray(),triangles=indices.ToArray(),uv=uvs.ToArray(),colors=colors.ToArray()},"Starfield mesh");mesh.RecalculateBounds();
            var shader=Shader.Find("WorldWeaver/Stars");if(!shader)throw new InvalidOperationException("Starfield shader is missing.");
            Undo.AddComponent<MeshFilter>(go).sharedMesh=mesh;var renderer=Undo.AddComponent<MeshRenderer>(go);renderer.sharedMaterial=tx.Asset(new Material(shader),"Starfield material",".mat");renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
    }
}
