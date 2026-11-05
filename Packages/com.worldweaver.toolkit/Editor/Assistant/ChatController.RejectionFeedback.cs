using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldWeaver.Editor.Assistant
{
    public sealed partial class ChatController
    {
        const string HostWireContractPrefix="SCENEPILOT HOST RESPONSE CONTRACT: ";
        internal static string HostWireContractMessage(bool nativeWire)=>HostWireContractPrefix+(nativeWire
            ? "Use native function calls with FLAT parameters from their current schemas; do not print EditorPlan JSON. End with finish_task when the full outcome is ready."
            : "Reply ONLY an EditorPlan JSON instance {reply,complete,actions}. Every action requires {tool,name,target,args}; unused name/target are empty strings. Native function calls are disabled for this response. Never output a schema.");
        internal static void ApplyActiveWireContract(CompletionRequest request,bool nativeWire)
        {
            request.UseToolCalls=nativeWire;string contract=HostWireContractMessage(nativeWire);
            bool found=false;
            for(int i=0;i<request.Messages.Count;i++)
            {
                var message=request.Messages[i];
                if(message.Role!="system"||!(message.Content??"").StartsWith(HostWireContractPrefix,StringComparison.Ordinal))continue;
                request.Messages[i]=new ChatMessage("system",contract);found=true;
            }
            if(!found)request.Messages.Add(new ChatMessage("system",contract));
        }
        static string BoundedRejectionData(string value,int limit=2400)
        {
            value=value??"";
            return value.Length<=limit?value:value.Substring(0,limit)+" [bounded actual observation; original remains in its durable step]";
        }
        internal static string[] AdvertisedRepairTools(CompletionRequest request,bool nativeWire)
        {
            if(nativeWire)return AgentFunctionProtocol.Definitions(request.Schema).Select(d=>(string)d["function"]["name"]).Distinct().ToArray();
            return (request.Schema?["properties"]?["actions"]?["items"]?["anyOf"] as JArray??new JArray()).OfType<JObject>().Select(EditorToolSchema.ToolName).Where(t=>!string.IsNullOrEmpty(t)).Distinct().ToArray();
        }
        static JArray RejectionObservations(AssistantMessage answer,string error)
        {
            var rows=new JArray();var steps=answer?.Steps;
            if(steps==null||steps.Count==0)return rows;
            var indices=new List<int>();
            foreach(Match match in Regex.Matches(error??"",@"durable (?:task )?step ([0-9]{1,6})",RegexOptions.CultureInvariant))
                if(int.TryParse(match.Groups[1].Value,out int index)&&index>=0&&index<steps.Count)indices.Add(index);
            int failedEditor=LatestFailedEditorCode(steps);if(failedEditor>=0)indices.Add(failedEditor);
            int failed=steps.FindLastIndex(s=>!s.Success);if(failed>=0)indices.Add(failed);
            int changed=LastBehaviorChange(steps);if(changed>=0)indices.Add(changed);
            indices.Add(steps.Count-1);
            foreach(int index in indices.Distinct().Take(5))
            {
                var step=steps[index];var row=new JObject{["index"]=index,["tool"]=step.Tool,["success"]=step.Success,["changesApplied"]=step.ChangesApplied,["target"]=step.Target,["historicalBeforeLastChange"]=index<changed,["error"]=BoundedRejectionData(step.Error)};
                if(step.Tool=="execute_editor_code")
                {
                    try
                    {
                        var measured=JObject.Parse(step.Summary??"");
                        // Put the actual return value before broad scene read-back.
                        // A successful compiled snippet may be a measurement or may
                        // change files while still failing its intended outcome.
                        row["observation"]=new JObject{["compiled"]=measured["compiled"],["executed"]=measured["executed"],
                            ["sceneChanged"]=measured["sceneChanged"],["projectChanged"]=measured["projectChanged"],
                            ["returnedData"]=BoundedRejectionData((string)measured["result"]),["sourceHash"]=measured["sourceHash"],
                            ["diagnostics"]=BoundedRejectionData(measured["diagnostics"]?.ToString(Formatting.None)),
                            ["diagnosticSourceExcerpts"]=measured["diagnosticSourceExcerpts"]?.DeepClone(),
                            ["failureScope"]=step.Success?null:"This failed Editor snippet only; it is not current project compilation status."};
                    }
                    catch(JsonException){row["observation"]=BoundedRejectionData(step.Summary);}
                }
                else row["observation"]=BoundedRejectionData(ToolEvidenceForModel(step));
                rows.Add(row);
            }
            return rows;
        }
        internal static string NativePhaseRecoveryGuidance(CompletionRequest request)
        {
            var tools=AdvertisedRepairTools(request,true);
            return "The previous proposal mixed dependent phases, compilation or discovery. Nothing from that rejected response executed. This recovery turn retains the host's narrowed phase constraints and actual advertised native functions. Use one standalone permitted function with FLAT parameters; receive its real result before dependent creation, attachment or verification. Preserve the ORIGINAL request and observed asset identifiers; never invent targets or resource IDs. A source draft must be a complete cohesive file and requires actual compilation before attachment. A resource search/import result must be observed before applying it. Do not repeat a whole rejected batch or claim its later stages ran. Current functions (data): "+JsonConvert.SerializeObject(tools)+".";
        }
        internal static void AddRejectedStepFeedback(CompletionRequest request,AssistantMessage answer,string error,string originalRequest,bool readOnlyRequest,bool nativeWire)
        {
            ApplyActiveWireContract(request,nativeWire);
            string contract=nativeWire
                ? "The previous native step was rejected before any action from that response executed. Continue with native function calls using FLAT parameters from the CURRENT advertised schemas, without an args wrapper. Do not output a textual JSON plan. Reuse the measured observations below; do not rerun the rejected operation or count another read as implementation. Choose a different justified diagnostic or repair. finish_task reports a truthful completed/partial/blocked outcome; completion still requires actual evidence."
                : "The previous plan was rejected before any action from it executed. Return ONLY an EditorPlan JSON instance, not a schema: {\"reply\":\"text\",\"complete\":false,\"actions\":[...]}. Use the current action schemas and actual observations to correct the next justified step. Do not repeat rejected or completed operations. Use actions:[] and complete:true only when the original request is actually satisfied or its unresolved outcome is disclosed.";
            if(readOnlyRequest)contract+=nativeWire
                ? " This is a read-only request: use advertised read-only observations or finish_task with the supported explanation. Do not modify scenes, source or permissions."
                : " This is a read-only request: do not modify scenes or source; use actions:[] and complete:true for a supported explanation.";
            var data=new JObject{["activeWireContract"]=nativeWire?"native_function_calls":"EditorPlan_JSON",["advertisedTools"]=new JArray(AdvertisedRepairTools(request,nativeWire)),
                ["hostError"]=error??"",["relevantActualObservations"]=RejectionObservations(answer,error),["originalUserRequest"]=originalRequest??""};
            request.Messages.Add(new ChatMessage("user",ChatHistoryStore.Redact(contract+"\nHOST REJECTION DATA:\n"+data.ToString(Formatting.None))));
        }
    }
}
