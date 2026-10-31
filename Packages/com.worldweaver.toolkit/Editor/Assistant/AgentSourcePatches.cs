using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Reviewed exact-match edits of existing Assets C# with conflict detection and actual compiler evidence.</summary>
    [InitializeOnLoad]
    public static class AgentSourcePatches
    {
        const string StatePath="UserSettings/WorldWeaverPatchCompilation.json";
        [Serializable] sealed class FileChange{public string Path,BeforeHash,AfterHash,BeforeBytes,AfterBytes;}
        [Serializable] sealed class Import{public string Id,SessionId,MessageId,HistoryFolder,Outcome;public bool Pending,Confirmed,Success;public List<FileChange> Files=new List<FileChange>();public List<string> Errors=new List<string>();public List<string> CompiledAssemblies=new List<string>();}
        public static event Action Changed;
        static AgentSourcePatches()
        {
            CompilationPipeline.compilationStarted+=_=>{var state=Read();if(state?.Pending==true){state.Errors.Clear();state.CompiledAssemblies=new List<string>();Save(state);}};
            CompilationPipeline.assemblyCompilationFinished+=(assembly,messages)=>
            {
                var state=Read();if(state?.Pending!=true)return;
                foreach(var error in messages.Where(m=>m.type==CompilerMessageType.Error).Take(20-state.Errors.Count))state.Errors.Add(ChatHistoryStore.Redact(error.file+"("+error.line+","+error.column+"): "+error.message));if(!messages.Any(m=>m.type==CompilerMessageType.Error)){if(state.CompiledAssemblies==null)state.CompiledAssemblies=new List<string>();state.CompiledAssemblies.Add(assembly);}Save(state);
            };
            CompilationPipeline.compilationFinished+=_=>Finish();
            string pendingAtLoad=Read()?.Pending==true?Read()?.Id:null;
            EditorApplication.delayCall+=()=>
            {
                var state=Read();if(state?.Pending!=true||state.Id!=pendingAtLoad||EditorApplication.isCompiling)return;
                state.Pending=false;state.Confirmed=false;state.Success=false;state.Outcome="Compiler completion was not observed after reload; no successful compilation is inferred.";Save(state);UpdateHistory(state);Changed?.Invoke();
            };
        }
        static string Hash(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
        public static string SourceHash(string path)=>Hash(File.ReadAllBytes(ValidatePath(path)));
        static string ValidatePath(string path)
        {
            if(path==null||!path.StartsWith("Assets/",StringComparison.Ordinal)||!path.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)||path.Contains("..")||path.Contains("//")||path.Contains("/./")||path.Contains('\\')||path.Contains(':')||path.Any(char.IsControl)||!File.Exists(path)||new FileInfo(path).Length>96*1024)throw new ArgumentException("Patch requires an existing Assets/...cs file <=96KiB.");
            string part=Path.GetFullPath(path);string root=Path.GetFullPath("Assets");
            for(string current=part;;current=Path.GetDirectoryName(current))
            {if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Symlinked source paths are not supported.");if(current==root)break;}
            return path;
        }
        static void Atomic(string path,byte[] data)
        {Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+".wwtmp";File.WriteAllBytes(tmp,data);if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
        static Import Read(){try{return File.Exists(StatePath)?JsonConvert.DeserializeObject<Import>(File.ReadAllText(StatePath)):null;}catch(IOException){return null;}catch(JsonException){return null;}}
        static void Save(Import state)=>Atomic(StatePath,Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(state,Formatting.Indented)));
        public static bool Pending=>Read()?.Pending==true;
        public static string CompilationContext(){var state=Read();return state==null?"No patch compilation recorded.":JsonConvert.SerializeObject(new{state.Id,state.Pending,state.Confirmed,state.Success,state.Outcome,state.Errors,Files=state.Files.Select(f=>new{f.Path,f.AfterHash})});}
        public static JObject CommandSchema()=>new JObject{["type"]="object",["additionalProperties"]=false,["required"]=new JArray("tool","name","target","args"),["properties"]=new JObject{
            ["tool"]=new JObject{["type"]="string",["const"]="patch_script"},["name"]=new JObject{["type"]="string"},["target"]=new JObject{["type"]="string",["enum"]=new JArray("")},
            ["args"]=new JObject{["type"]="object",["additionalProperties"]=false,["required"]=new JArray("path","expected_sha256","find","replace"),["properties"]=new JObject{
                ["path"]=new JObject{["type"]="string",["maxLength"]=240},["expected_sha256"]=new JObject{["type"]="string",["pattern"]="^[a-f0-9]{64}$"},
                ["find"]=new JObject{["type"]="string",["minLength"]=1,["maxLength"]=18000},["replace"]=new JObject{["type"]="string",["maxLength"]=18000},["description"]=new JObject{["type"]="string",["maxLength"]=1000}}}}};
        public const string ToolInstructions="patch_script: target empty,args {path:existing Assets/...cs,expected_sha256:current source SHA256,find:exact nonempty unique existing text,replace:new text,description?:purpose}. Read source and its SHA first. Entire 1–4 file patch is reviewed before import; original hashes are checked again at commit, no overwrite after manual edits. Small exact patches preserve all other text. Actual compiler results are returned; code executes with normal Unity privileges.";
        public static void Validate(EditorCommand command)
        {
            if(command.tool!="patch_script"||!string.IsNullOrEmpty(command.target)||command.args.Properties().Any(p=>!new[]{"path","expected_sha256","find","replace","description"}.Contains(p.Name)))throw new ArgumentException("Invalid patch contract.");
            foreach(string key in new[]{"path","expected_sha256","find","replace"})if(command.args[key]?.Type!=JTokenType.String)throw new ArgumentException("Patch "+key+" must be a string.");
            string path=ValidatePath((string)command.args["path"]),expected=(string)command.args["expected_sha256"],find=(string)command.args["find"],replace=(string)command.args["replace"];
            try { var decoded=new UTF8Encoding(false,true).GetString(File.ReadAllBytes(path));if(decoded.IndexOf('\0')>=0)throw new ArgumentException("Only UTF8 C# source is supported by this patch operation."); }
            catch(DecoderFallbackException){throw new ArgumentException("Only UTF8 C# source is supported by this patch operation.");}
            if(command.args["description"]!=null&&(command.args["description"].Type!=JTokenType.String||((string)command.args["description"]).Length>1000))throw new ArgumentException("Patch description must be <=1000 characters.");
            if(expected!=SourceHash(path))throw new InvalidOperationException("Source changed since read/review. Read it again and create a new patch.");
            if(string.IsNullOrEmpty(find)||find.Length>18000||replace.Length>18000||replace.Contains("```")||ChatHistoryStore.Redact(replace)!=replace)throw new ArgumentException("Invalid replacement text or embedded credential.");
            var source=File.ReadAllText(path);int first=source.IndexOf(find,StringComparison.Ordinal);
            if(first<0||source.IndexOf(find,first+1,StringComparison.Ordinal)>=0)throw new ArgumentException("Find text must match exactly once. Include enough surrounding context.");
            if(source.Length-find.Length+replace.Length>96000)throw new ArgumentException("Patched file exceeds size budget.");
        }
        public static List<EditorCommandResult> Apply(IReadOnlyList<EditorCommand> commands,string sessionId,string messageId,string historyFolder)
        {
            if(commands.Count<1||commands.Count>4||commands.Select(c=>(string)c.args["path"]).Select(p=>p.Normalize(NormalizationForm.FormC)).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=commands.Count)throw new ArgumentException("A patch review contains1–4 unique source files.");
            if(Pending||ScriptDrafts.Pending)throw new InvalidOperationException("Finish current compilation first.");foreach(var c in commands)Validate(c);
            var state=new Import{Id=Guid.NewGuid().ToString("N"),SessionId=sessionId,MessageId=messageId,HistoryFolder=historyFolder,Pending=true};
            foreach(var c in commands)
            {
                string path=(string)c.args["path"],find=(string)c.args["find"],replacement=(string)c.args["replace"];byte[] before=File.ReadAllBytes(path);
                if(Hash(before)!=(string)c.args["expected_sha256"])throw new InvalidOperationException("Source changed during patch preparation.");
                string source=File.ReadAllText(path);int index=source.IndexOf(find,StringComparison.Ordinal);string result=source.Substring(0,index)+replacement+source.Substring(index+find.Length);
                // Preserve BOM and newline bytes outside the exact textual edit for normal UTF8 source.
                bool bom=before.Length>=3&&before[0]==239&&before[1]==187&&before[2]==191;var encoding=new UTF8Encoding(bom);byte[] content=encoding.GetBytes(result);byte[] after=bom?encoding.GetPreamble().Concat(content).ToArray():content;
                if(after.Length>96*1024)throw new ArgumentException("Patched UTF8 bytes exceed the 96KiB source budget.");
                state.Files.Add(new FileChange{Path=path,BeforeHash=Hash(before),AfterHash=Hash(after),BeforeBytes=Convert.ToBase64String(before),AfterBytes=Convert.ToBase64String(after)});
            }
            Save(state);var written=new List<FileChange>();
            try
            {
                foreach(var file in state.Files){if(SourceHash(file.Path)!=file.BeforeHash)throw new InvalidOperationException("Source changed during patch preparation.");written.Add(file);Atomic(file.Path,Convert.FromBase64String(file.AfterBytes));}
            }
            catch(Exception error)
            {
                var conflicts=new List<string>();
                foreach(var file in written.AsEnumerable().Reverse())
                {
                    try { if(File.Exists(file.Path)&&SourceHash(file.Path)==file.AfterHash)Atomic(file.Path,Convert.FromBase64String(file.BeforeBytes));else conflicts.Add(file.Path); }
                    catch(Exception rollback) { conflicts.Add(file.Path+": "+rollback.GetType().Name); }
                }
                state.Pending=false;state.Outcome=conflicts.Count==0?"Patch write failed; previous bytes restored.":"Patch write failed; later changes were preserved or rollback failed: "+string.Join(", ",conflicts);Save(state);
                if(conflicts.Count>0)throw new InvalidOperationException(state.Outcome,error);throw;
            }
            EditorApplication.delayCall+=()=>{AssetDatabase.Refresh();CompilationPipeline.RequestScriptCompilation();};
            return state.Files.Select(f=>new EditorCommandResult{Success=true,Tool="patch_script",Target=f.Path,SourceHashes=new Dictionary<string,string>{{f.Path,f.AfterHash}},Summary="Просмотренная точечная правка записана; ожидается настоящая компиляция."}).ToList();
        }
        static void Finish()
        {
            var state=Read();if(state?.Pending!=true)return;state.Pending=false;state.Confirmed=true;
            state.Success=state.Errors.Count==0&&state.Files.All(f=>File.Exists(f.Path)&&SourceHash(f.Path)==f.AfterHash);
            state.Outcome=state.Success?"Unity compiled the reviewed source changes without errors.":state.Errors.Count>0?string.Join("\n",state.Errors):"Source changed during compilation; result is not confirmed.";Save(state);UpdateHistory(state);Changed?.Invoke();
        }
        static void UpdateHistory(Import state)
        {
            var store=new ChatHistoryStore(state.HistoryFolder);var session=store.Load().FirstOrDefault(s=>s.Id==state.SessionId);var answer=session?.Messages.FirstOrDefault(m=>m.Id==state.MessageId);if(answer==null)return;
            answer.Steps.Add(new EditorCommandResult{Tool="compile_patch",Success=state.Confirmed&&state.Success,SourceHashes=state.Confirmed&&state.Success?AgentCompilationEvidence.Confirm(answer,state.CompiledAssemblies):new Dictionary<string,string>(),Target=string.Join(",",state.Files.Select(f=>f.Path)),Summary=state.Outcome,Error=state.Success?null:state.Outcome});answer.Status=state.Success?"Правки скомпилированы":"Нужно исправить код";answer.IsError=!state.Success;store.Save(session);
        }
    }
}
