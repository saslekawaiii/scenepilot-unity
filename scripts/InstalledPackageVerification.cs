using System;
using System.IO;
using System.Linq;
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
            bool equal=files.Properties().All(p=>File.Exists(Path.Combine(info.resolvedPath,p.Name))&&Hash(File.ReadAllBytes(Path.Combine(info.resolvedPath,p.Name)))==(string)p.Value);
            report["packageFilesMatchArchive"]=equal;report["verifiedFileCount"]=files.Count;
            var skills=AgentSkillCatalog.CreateDefault().List();report["skills"]=skills.Count;report["allSkillsTrusted"]=skills.All(s=>s.Trusted);
            report["recipes"]=UnityRecipeCatalog.CreateDefault().Count;
            var definitions=AgentFunctionProtocol.Definitions(EditorPlan.Schema());
            report["nativeFunctionCount"]=definitions.Count;
            string[] expected={"execute_editor_code","reflect_api","find_unity_recipes","read_unity_recipe"};
            report["extensionToolsPresent"]=expected.All(name=>definitions.Any(t=>(string)t["function"]["name"]==name));
            report["passed"]=equal&&info.version=="1.5.0"&&skills.Count==114&&skills.All(s=>s.Trusted)&&UnityRecipeCatalog.CreateDefault().Count==466&&(bool)report["extensionToolsPresent"];
        }
        catch(Exception e){report["error"]=e.ToString();}
        report["finishedUtc"]=DateTime.UtcNow.ToString("O");
        if(!string.IsNullOrEmpty(output))File.WriteAllText(output,report.ToString(Newtonsoft.Json.Formatting.Indented));
        EditorApplication.Exit((bool)report["passed"]?0:1);
    }
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
}
