using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldWeaver.Editor.Assistant
{
    public sealed partial class ChatController
    {
        async Task AddVisualObservationsAsync(EditorBatchResult batch,AssistantMessage answer,string query,CancellationToken token)
        {
            var captures=batch.Results.Where(r=>r.Success&&r.Tool=="capture_scene").ToArray();if(captures.Length==0||models.FirstOrDefault(m=>m.Key==Settings.ModelKey)?.SupportsVision==true)return;
            await RefreshLocalModelsAsync();token.ThrowIfCancellationRequested();
            var observer=models.Where(m=>m.Provider==ProviderKind.Ollama&&m.Available&&m.AutoDiscovered&&!m.Remote&&m.SupportsVision&&!m.Model.EndsWith(":cloud",StringComparison.OrdinalIgnoreCase)&&Uri.TryCreate(m.Endpoint,UriKind.Absolute,out var uri)&&uri.IsLoopback).OrderBy(m=>m.SizeBytes>0?m.SizeBytes:long.MaxValue).FirstOrDefault();
            foreach(var capture in captures)
            {
                var description=JObject.Parse(capture.Summary);JObject observation;
                try
                {
                    if(observer==null)throw new ArgumentException("No installed local vision model is available. The picture was captured but not inspected.");
                    var attachment=answer.Attachments.Last(a=>a.Sha256==(string)description["Sha256"]);Phase="Осматриваю сцену · "+observer.Model;Notify();
                    var inventory=new JObject{["capture"]=description.DeepClone(),["renderers"]=ProjectContextIndex.ListScene(new JObject{["component"]="Renderer",["limit"]=24}),["terrains"]=ProjectContextIndex.ListScene(new JObject{["component"]="Terrain",["limit"]=8})};
                    observation=await AgentVisualObserver.ObserveAsync(observer,attachment,query,inventory,token);
                }
                catch(Exception error) when(!(error is OperationCanceledException)){observation=new JObject{["status"]="unavailable",["error"]=ChatHistoryStore.Redact(error.Message),["advisoryOnly"]=true};}
                description["visualInspection"]=observation;capture.Summary=description.ToString(Formatting.None);
            }
        }
    }
}
