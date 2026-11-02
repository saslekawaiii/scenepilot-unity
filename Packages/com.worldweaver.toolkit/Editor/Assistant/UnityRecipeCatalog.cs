using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor.PackageManager;
using UnityEditor;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Pinned third-party Markdown/C# reference data, never an executor or permission source.</summary>
    public sealed class UnityRecipeCatalog
    {
        readonly string root;
        readonly JObject manifest;
        readonly Dictionary<string,JObject> entries;
        internal const string Guidance="These are reference templates from another host. IRunCommand, ExecutionResult, Unity_RunCommand and source tool names are NOT installed WorldWeaver commands. Inspect installed packages/APIs, adapt the useful method body to execute_editor_code(GameObject target), use real scene targets and Undo, and return actual data. Read prerequisite IDs if helpers are needed; do not pretend those helpers already exist. New runtime MonoBehaviours use the existing source compilation workflow. Reading this recipe never executes code or grants permissions. Search in English when the source uses English API terminology.";
        internal const string Reference=@"find_unity_recipes: empty target; args {query:string,limit?:1..8}. Search the pinned MIT Unity C#/API recipe library by operation, type or domain. Returns actual IDs, signatures and references, not executable tool names. Search English API terms if Russian search is empty.
read_unity_recipe: empty target; args {id:string,offset?:integer}. Read hash-verified Markdown/C# reference, up to 9000 characters per page. Follow nextOffset and prerequisite IDs. Source templates use another IRunCommand host; adapt API code to our actual tools, never call nonexistent source MCP commands.";
        public static UnityRecipeCatalog CreateDefault()
        {
            var info=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UnityRecipeCatalog).Assembly);
            return new UnityRecipeCatalog(Path.Combine(info?.resolvedPath??Path.Combine(Directory.GetCurrentDirectory(),"Packages/com.worldweaver.toolkit"),"Editor/RecipeLibrary"));
        }
        internal UnityRecipeCatalog(string folder)
        {
            root=Path.GetFullPath(folder);var file=Path.Combine(root,"catalog.json");
            if(!File.Exists(file)||new FileInfo(file).Length>2*1024*1024)throw new InvalidDataException("Recipe catalog unavailable or too large.");
            manifest=JObject.Parse(File.ReadAllText(file));entries=new Dictionary<string,JObject>(StringComparer.Ordinal);
            foreach(var row in (manifest["recipes"] as JArray??new JArray()).OfType<JObject>())
            {
                string id=(string)row["id"];
                if(!Regex.IsMatch(id??"",@"^[a-z0-9][a-z0-9-]{0,119}$")||entries.ContainsKey(id)||!Regex.IsMatch((string)row["sha256"]??"","^[a-f0-9]{64}$"))throw new InvalidDataException("Invalid/duplicate recipe metadata.");
                entries.Add(id,row);if(entries.Count>1000)throw new InvalidDataException("Too many recipes.");
            }
        }
        public int Count=>entries.Count;
        public JObject Search(string query,int limit=6)
        {
            var tokens=Regex.Matches((query??"").ToLowerInvariant(),@"[\p{L}\p{N}]{3,}").Cast<Match>().Select(m=>m.Value).Distinct().Take(16).ToArray();
            string canonical=Regex.Replace((query??"").ToLowerInvariant(),@"[^\p{L}\p{N}]","");
            var apiTypes=TypeCache.GetTypesDerivedFrom<Component>().Where(t=>t.IsPublic&&t.Name.Length>=5&&canonical.Contains(t.Name.ToLowerInvariant())).Select(t=>t.FullName).Distinct().Take(8).ToArray();
            var found=new List<(JObject Row,int Score)>();
            foreach(var row in entries.Values)
            {
                string metadata=string.Join(" ",new[]{"id","title","description","domain","signature"}.Select(k=>(string)row[k]??"")).ToLowerInvariant();
                int score=tokens.Sum(t=>metadata.Contains(t)?5:0);
                if(apiTypes.Length>0&&new[]{"component-component-add","component-component-get-properties"}.Contains((string)row["id"]))score+=30;
                if(score==0&&tokens.Length>0)
                {
                    string body=Decode(ReadBytes(row)).ToLowerInvariant();score=tokens.Count(body.Contains);
                }
                if(score>0)found.Add((row,score));
            }
            return new JObject{["totalRecipes"]=entries.Count,["apiTypes"]=new JArray(apiTypes),["apiHint"]=apiTypes.Length>0?"These Component types actually exist in this project. Generic component-add/configuration references apply even if no recipe is named after that type. Use reflect_api for its exact public members.":null,["recipes"]=new JArray(found.OrderByDescending(r=>r.Score).ThenBy(r=>(string)r.Row["id"],StringComparer.Ordinal).Take(Math.Max(1,Math.Min(8,limit))).Select(r=>Summary(r.Row))),["guidance"]=Guidance,["codeExecuted"]=false};
        }
        static JObject Summary(JObject row)=>new JObject{["id"]=row["id"].DeepClone(),["domain"]=row["domain"].DeepClone(),["description"]=row["description"].DeepClone(),["signature"]=row["signature"].DeepClone(),["prerequisites"]=row["references"]?.DeepClone()??new JArray()};
        public JObject Read(string id,int offset=0)
        {
            if(!entries.TryGetValue(id??"",out var row))throw new ArgumentException("Unknown recipe ID; use find_unity_recipes, not a file path.");
            string text=Decode(ReadBytes(row));if(offset<0||offset>text.Length)throw new ArgumentException("Recipe offset is outside the document.");
            int count=Math.Min(9000,text.Length-offset),next=offset+count;
            var result=Summary(row);result["markdown"]=text.Substring(offset,count);result["offset"]=offset;result["totalCharacters"]=text.Length;
            result["nextOffset"]=next<text.Length?(JToken)new JValue(next):JValue.CreateNull();result["sha256"]=row["sha256"].DeepClone();result["source"]=(string)manifest["repository"]+"/blob/"+(string)manifest["commit"]+"/"+(string)row["sourcePath"];
            result["guidance"]=Guidance;result["codeExecuted"]=false;return result;
        }
        byte[] ReadBytes(JObject row)
        {
            string relative=(string)row["path"];
            if(string.IsNullOrEmpty(relative)||relative.Contains('\\')||relative.Split('/').Any(p=>p==".."||p=="."||p.Length==0))throw new InvalidDataException("Unsafe recipe path.");
            string path=Path.GetFullPath(Path.Combine(root,relative));
            if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal)||!path.EndsWith(".md",StringComparison.Ordinal)||!File.Exists(path))throw new InvalidDataException("Recipe source unavailable.");
            for(string p=path;p!=root;p=Path.GetDirectoryName(p))if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked recipe paths are not allowed.");
            if(new FileInfo(path).Length>65536)throw new InvalidDataException("Recipe source too large.");
            byte[] bytes=File.ReadAllBytes(path);string digest;using(var hash=SHA256.Create())digest=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            if(digest!=(string)row["sha256"])throw new InvalidDataException("Recipe integrity mismatch: "+(string)row["id"]);return bytes;
        }
        static string Decode(byte[] bytes)=>new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');
    }
}
