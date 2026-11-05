using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldWeaver.Editor.Assistant
{
    // A bounded view of the ACTUAL compiler input, not a proposed correction.
    // No source is executed, rewritten, persisted or recovered from inference audit.
    internal static class DiagnosticSourceExcerpts
    {
        const int MaximumCharacters=6000,MaximumWindows=8,LineCharacters=300;
        static readonly Regex Location=new Regex(@"\bLine\s+(\d+)(?:,\s*column\s+(\d+))?(?:\s+\(generated\s+line\s+(\d+)\))?",RegexOptions.CultureInvariant);
        static readonly Regex Literals=new Regex(@"(?:\$?@|@\$)""(?:""""|[^""])*""|\$?""(?:\\.|[^""\\\r\n])*(?:""|(?=\r?$))",RegexOptions.Multiline|RegexOptions.CultureInvariant);
        static readonly Regex SensitivePrefix=new Regex(@"(?:\b\w*(?:api_?key|password|secret|token|authorization)\w*\s*(?:=|:|\()\s*|\[\s*""[^""]*(?:api_?key|password|secret|token|authorization)[^""]*""\s*\]\s*=\s*)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        static readonly Regex SensitiveWord=new Regex(@"api_?key|password|secret|authorization",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        static string Mask(string source,out bool redacted)
        {
            bool changed=false;
            string safe=Literals.Replace(source,match=>
            {
                string prefix=source.Substring(Math.Max(0,match.Index-180),Math.Min(180,match.Index));
                if(ChatHistoryStore.Redact(match.Value)==match.Value&&!SensitivePrefix.IsMatch(prefix)&&!SensitiveWord.IsMatch(match.Value))return match.Value;
                changed=true;return new string(match.Value.Select(c=>c=='\r'||c=='\n'||c=='"'||c=='\\'||c=='@'||c=='$'?c:'*').ToArray());
            });
            // Catch configured/pattern secrets outside ordinary literals as well.
            // Preserve line structure and mark any line whose display width changed.
            string scrubbed=ChatHistoryStore.Redact(safe);redacted=changed||scrubbed!=safe;return scrubbed;
        }
        internal static JObject Build(string source,IEnumerable<string> diagnostics)
        {
            source=source??"";string safe=Mask(source,out bool redacted);var originalLines=source.Replace("\r\n","\n").Replace('\r','\n').Split('\n');var lines=safe.Replace("\r\n","\n").Replace('\r','\n').Split('\n');var windows=new JArray();var seen=new HashSet<string>();
            var result=new JObject{["sourceHash"]=TextChunker.Digest(source),["sourceKind"]="actual_generated_editor_snippet",["lineNumbering"]="One-based generated compiler-source lines and columns; not user-body offsets.",["legacyMappingNote"]="Older Line N diagnostics subtract6 from generated source and clamp to1. A clamped legacy1 is ambiguous; header lines are shown without inventing an exact pointer.",["redacted"]=redacted,["excerptsAvailable"]=false,["windows"]=windows,["truncated"]=false,["characterLimit"]=MaximumCharacters};
            foreach(string diagnostic in (diagnostics??Enumerable.Empty<string>()).Take(64))
            {
                var match=Location.Match(diagnostic??"");if(!match.Success||!int.TryParse(match.Groups[1].Value,out int reported))continue;
                int? column=match.Groups[2].Success&&int.TryParse(match.Groups[2].Value,out int col)?col:(int?)null;
                int? exact=match.Groups[3].Success&&int.TryParse(match.Groups[3].Value,out int generated)?generated:reported>1?reported+6:(int?)null;
                int center=exact??Math.Min(4,lines.Length);if(center<1||center>lines.Length)continue;
                if(!seen.Add(center+":"+column))continue;if(windows.Count>=MaximumWindows){result["truncated"]=true;break;}
                int first=exact.HasValue?Math.Max(1,center-2):1,last=exact.HasValue?Math.Min(lines.Length,center+2):Math.Min(lines.Length,9);var rows=new JArray();bool clipped=false;
                for(int number=first;number<=last;number++)
                {
                    string text=lines[number-1];bool widthPreserved=number<=originalLines.Length&&text.Length==originalLines[number-1].Length;bool errorLine=exact==number;
                    int start=errorLine&&column.HasValue&&widthPreserved?Math.Max(0,Math.Min(text.Length-LineCharacters,column.Value-1-LineCharacters/2)):0;start=Math.Max(0,start);int length=Math.Min(LineCharacters,text.Length-start);bool cut=start>0||start+length<text.Length;clipped|=cut;
                    var row=new JObject{["line"]=number,["text"]=text.Substring(start,length),["isDiagnosticLine"]=errorLine,["startColumn"]=start+1,["clipped"]=cut};
                    if(errorLine&&column.HasValue){row["diagnosticColumn"]=column.Value;row["displayPointerColumn"]=widthPreserved&&column.Value-1>=start&&column.Value-1<=start+length?(JToken)(column.Value-start):JValue.CreateNull();}
                    rows.Add(row);
                }
                var window=new JObject{["compilerReportedLine"]=reported,["generatedDiagnosticLine"]=exact.HasValue?(JToken)exact.Value:JValue.CreateNull(),["diagnosticColumn"]=column.HasValue?(JToken)column.Value:JValue.CreateNull(),["startLine"]=first,["endLine"]=last,["lines"]=rows};
                windows.Add(window);if(result.ToString(Formatting.None).Length>MaximumCharacters){windows.RemoveAt(windows.Count-1);result["truncated"]=true;break;}if(clipped)result["truncated"]=true;
            }
            result["excerptsAvailable"]=windows.Count>0;return result;
        }
    }
}
