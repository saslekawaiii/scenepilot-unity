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
        async Task AddVisualObservationsAsync(EditorBatchResult batch,AssistantMessage answer,CancellationToken token)
        {
            var captures=batch.Results.Where(r=>r.Success&&r.Tool=="capture_scene").ToArray();if(captures.Length==0||models.FirstOrDefault(m=>m.Key==Settings.ModelKey)?.SupportsVision==true)return;
            await RefreshLocalModelsAsync();token.ThrowIfCancellationRequested();
            var observer=AgentVisualObserver.SelectLocalObserver(models);
            foreach(var capture in captures)
            {
                var description=JObject.Parse(capture.Summary);JObject observation;
                try
                {
                    if(observer==null)throw new ArgumentException("No installed local vision model is available. The picture was captured but not inspected.");
                    var attachment=answer.Attachments.Last(a=>a.Sha256==(string)description["Sha256"]);Phase="Осматриваю сцену · "+observer.Model;Notify();
                    var inventory=AgentVisualObserver.SceneInventory();inventory["capture"]=description.DeepClone();
                    observation=await AgentVisualObserver.ObserveAsync(observer,attachment,inventory,token);
                }
                catch(Exception error) when(!(error is OperationCanceledException)){observation=new JObject{["status"]="unavailable",["error"]=ChatHistoryStore.Redact(error.Message),["advisoryOnly"]=true};}
                description["visualInspection"]=observation;capture.Summary=description.ToString(Formatting.None);
            }
        }
    }
}
