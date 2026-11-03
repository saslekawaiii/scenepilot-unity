using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Read-only dependency export for a scene actually produced by the public agent.
public static class ExportAgentResult
{
    static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    public static void Run()
    {
        string output=Environment.GetEnvironmentVariable("WW_EXPORT_OUTPUT");var report=new JObject{["sceneRepairByHarness"]=false};
        try
        {
            string project=Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName);
            if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE")!="1"||project!=Path.GetFullPath(Environment.GetEnvironmentVariable("WW_AGENT_PROJECT")??""))throw new InvalidOperationException("Isolated agent-result project required.");
            string scene=Environment.GetEnvironmentVariable("WW_EXPORT_SCENE");if(!scene.StartsWith("Assets/",StringComparison.Ordinal)||!File.Exists(scene))throw new ArgumentException("Saved agent scene required.");
            string before=Hash(scene);EditorSceneManager.OpenScene(scene);
            var paths=new HashSet<string>(AssetDatabase.GetDependencies(scene,true).Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal)&&File.Exists(p)),StringComparer.Ordinal);
            const string web="Assets/WorldWeaverGenerated/Web";if(Directory.Exists(web))foreach(string p in Directory.GetFiles(web,"*",SearchOption.AllDirectories))paths.Add(p.Replace('\\','/'));
            foreach(string p in paths.ToArray())
            {
                if(File.Exists(p+".meta"))paths.Add(p+".meta");
                string folder=Path.GetDirectoryName(p);
                while(!string.IsNullOrEmpty(folder)&&folder!="Assets")
                {string meta=folder.Replace('\\','/')+".meta";if(File.Exists(meta))paths.Add(meta);folder=Path.GetDirectoryName(folder);}
            }
            report["scene"]=scene;report["sceneSha256"]=before;report["dependencies"]=new JArray(paths.OrderBy(p=>p,StringComparer.Ordinal).Select(p=>new JObject{["path"]=p,["bytes"]=new FileInfo(p).Length,["sha256"]=Hash(p)}));
            report["sceneUnchanged"]=before==Hash(scene);report["passed"]=(bool)report["sceneUnchanged"];
        }
        catch(Exception error){report["passed"]=false;report["error"]=error.ToString();}
        File.WriteAllText(output,report.ToString());EditorApplication.Exit((bool)report["passed"]?0:1);
    }
}
