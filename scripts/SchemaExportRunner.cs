using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using WorldWeaver.Editor.Assistant;

public static class SchemaExportRunner
{
    static int exitFrames;
    static DateTime exitNotBefore;
    public static void Run()
    {
        string output=Environment.GetEnvironmentVariable("WW_SCHEMA_OUTPUT");if(string.IsNullOrEmpty(output))throw new ArgumentException("WW_SCHEMA_OUTPUT required.");
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"editor-command-schema-1.2.json"),EditorToolSchema.CommandSchema().ToString());File.WriteAllText(Path.Combine(output,"editor-command-schema-decoder-1.2.json"),EditorToolSchema.DecoderCommandSchema().ToString());
        var package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ChatController).Assembly);var hashes=new JObject();
        foreach(string name in new[]{"EditorToolSchema.cs","WorldGameplayTools.cs","AgentProjectTools.cs","AgentSourcePatches.cs"})
        {using(var sha=SHA256.Create())hashes[name]=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(package.resolvedPath,"Editor/Assistant",name)))).Replace("-","").ToLowerInvariant();}
        File.WriteAllText(Path.Combine(output,"editor-schema-export-1.2.json"),new JObject{["unity"]=UnityEngine.Application.unityVersion,["utc"]=DateTime.UtcNow.ToString("o"),["packageVersion"]=package.version,["generatorSources"]=hashes,["toolCount"]=((JArray)EditorToolSchema.CommandSchema()["anyOf"]).Select(t=>EditorToolSchema.ToolName((JObject)t)).Distinct().Count()}.ToString());
        // Exiting inside startup's executeMethod can crash native teardown after successful writes.
        // Return to the initialized editor loop before requesting a clean process exit.
        exitNotBefore=DateTime.UtcNow.AddSeconds(5);EditorApplication.delayCall+=()=>EditorApplication.update+=ExitWhenReady;
    }
    static void ExitWhenReady()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||DateTime.UtcNow<exitNotBefore)return;
        if(++exitFrames<3)return;EditorApplication.update-=ExitWhenReady;EditorApplication.Exit(0);
    }
}
