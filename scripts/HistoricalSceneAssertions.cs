using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;

// Fixture expectations derived from pre-existing user history; never sent to the model.
public static class HistoricalSceneAssertions
{
    public static JObject Evaluate(JObject expectations)
    {
        var all=ProjectContextIndex.Objects().ToArray();var checks=new JObject();
        foreach(var item in (expectations["absent"] as JArray??new JArray()).OfType<JObject>())
        {
            string id=(string)item["objectId"],batch=(string)item["batchId"];
            var present=all.Where(g=>g.GetComponent<GeneratedSceneObject>()).Where(g=>{var marker=g.GetComponent<GeneratedSceneObject>();return !string.IsNullOrEmpty(id)&&marker.ObjectId==id||!string.IsNullOrEmpty(batch)&&marker.BatchId==batch;}).ToArray();
            checks[(string)item["check"]]=present.Length==0;
        }
        foreach(var item in (expectations["preserved"] as JArray??new JArray()).OfType<JObject>())
        {
            string id=(string)item["objectId"],parent=(string)item["parentId"];
            var go=all.FirstOrDefault(g=>g.GetComponent<GeneratedSceneObject>()?.ObjectId==id);bool inside=string.IsNullOrEmpty(parent);
            if(go)for(var t=go.transform.parent;t;t=t.parent)if(t.GetComponent<GeneratedSceneObject>()?.ObjectId==parent)inside=true;
            checks[(string)item["check"]]=go&&inside&&go.activeInHierarchy&&go.GetComponent<Renderer>()&&go.GetComponent<Renderer>().enabled;
        }
        return checks;
    }
}
