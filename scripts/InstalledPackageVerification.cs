using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;

/// <summary>Read-only verification of a resolved package. Does not send prompts or edit scenes.</summary>
public static class InstalledPackageVerification
{
    public static void Run()
    {
        string output=Environment.GetEnvironmentVariable("WW_INSTALL_OUTPUT");
        var report=new JObject{["startedUtc"]=DateTime.UtcNow.ToString("O"),["passed"]=false,["uiVisuallyVerified"]=false,["sceneMutations"]=false};
        try
        {
            string project=Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName);
            if(!Application.isBatchMode||project!=Path.GetFullPath(Environment.GetEnvironmentVariable("WW_INSTALL_PROJECT")??""))throw new InvalidOperationException("Exact named project required.");
            if(string.IsNullOrEmpty(output)||File.Exists(output))throw new IOException("Unique output required.");
            var proof=JObject.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("WW_INSTALL_PROOF")));
            var info=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WorldPlayer).Assembly);
            report["version"]=info.version;report["packagePath"]=info.resolvedPath;report["sourceCommit"]=proof["sourceCommit"].DeepClone();
            var files=(JObject)proof["files"];
            int rawEqual=0;var generatedMetadata=new JArray();
            bool equal=files.Properties().All(p=>
            {
                string path=Path.Combine(info.resolvedPath,p.Name);if(!File.Exists(path))return false;byte[] raw=File.ReadAllBytes(path);
                if(Hash(raw)==(string)p.Value){rawEqual++;return true;}
                if(p.Name!="package.json")return false;
                var json=JObject.Parse(Encoding.UTF8.GetString(raw));
                if(!Regex.IsMatch((string)json["_fingerprint"]??"",@"^[a-f0-9]{32}$"))return false;
                json.Remove("_fingerprint");bool metadataOnly=Hash(Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.Indented)+"\n"))==(string)p.Value;
                if(metadataOnly)generatedMetadata.Add("package.json:_fingerprint (UPM-generated)");return metadataOnly;
            });
            report["packageFilesMatchArchive"]=equal;report["verifiedFileCount"]=files.Count;report["rawByteEqualFileCount"]=rawEqual;report["normalizedGeneratedMetadata"]=generatedMetadata;
            var skills=AgentSkillCatalog.CreateDefault().List();report["skills"]=skills.Count;report["allSkillsTrusted"]=skills.All(s=>s.Trusted);
            report["recipes"]=UnityRecipeCatalog.CreateDefault().Count;
            var definitions=AgentFunctionProtocol.Definitions(EditorPlan.Schema());
            report["nativeFunctionCount"]=definitions.Count;
            string expectedVersion=(string)proof["version"]??"1.5.0";
            string[] expected=expectedVersion=="1.6.0"?new[]{"execute_editor_code","reflect_api","model_search","model_import","replace_model","read_task_step"}:new[]{"execute_editor_code","reflect_api","find_unity_recipes","read_unity_recipe"};
            report["extensionToolsPresent"]=expected.All(name=>definitions.Any(t=>(string)t["function"]["name"]==name));
            report["passed"]=equal&&info.version==expectedVersion&&skills.Count==114&&skills.All(s=>s.Trusted)&&UnityRecipeCatalog.CreateDefault().Count==466&&(bool)report["extensionToolsPresent"];
        }
        catch(Exception e){report["error"]=e.ToString();}
        report["finishedUtc"]=DateTime.UtcNow.ToString("O");
        if(!string.IsNullOrEmpty(output))File.WriteAllText(output,report.ToString(Newtonsoft.Json.Formatting.Indented));
        EditorApplication.Exit((bool)report["passed"]?0:1);
    }
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
}
