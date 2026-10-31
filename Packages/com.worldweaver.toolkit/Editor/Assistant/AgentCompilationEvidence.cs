using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor.Compilation;

namespace WorldWeaver.Editor.Assistant
{
    internal static class AgentCompilationEvidence
    {
        // A successful assembly compilation also validates unchanged reviewed files in that assembly.
        // Only source bytes actually written by this task and still matching their recorded hash qualify.
        internal static Dictionary<string,string> Confirm(AssistantMessage message,IEnumerable<string> compiledAssemblies)
        {
            var assemblies=new HashSet<string>((compiledAssemblies??Array.Empty<string>()).Select(Path.GetFileNameWithoutExtension),StringComparer.Ordinal);
            var written=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var step in message.Steps.Where(s=>s.Success&&(s.Tool=="draft_script"||s.Tool=="patch_script")))
                foreach(var pair in step.SourceHashes??new Dictionary<string,string>())written[pair.Key]=pair.Value;
            var verified=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var pair in written)
            {
                if(!pair.Key.StartsWith("Assets/",StringComparison.Ordinal)||!File.Exists(pair.Key))continue;
                string assembly=Path.GetFileNameWithoutExtension(CompilationPipeline.GetAssemblyNameFromScriptPath(pair.Key));
                if(!assemblies.Contains(assembly))continue;
                using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(pair.Key))).Replace("-","").ToLowerInvariant()==pair.Value)verified[pair.Key]=pair.Value;
            }
            return verified;
        }
    }
}
