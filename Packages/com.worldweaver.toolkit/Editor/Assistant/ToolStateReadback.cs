using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Report actual resulting values so the agent can detect omissions without guessing from component names.</summary>
    internal static class ToolStateReadback
    {
        static JArray V(Vector3 v)=>new JArray(v.x,v.y,v.z);
        internal static void Add(JObject report,GameObject go)
        {
            var filter=go.GetComponent<MeshFilter>();
            if(filter&&filter.sharedMesh)
            {
                var mesh=filter.sharedMesh;var bounds=mesh.bounds;
                long indices=Enumerable.Range(0,mesh.subMeshCount).Sum(i=>(long)mesh.GetIndexCount(i));
                report["mesh"]=new JObject{["vertices"]=mesh.vertexCount,["indexCount"]=indices,["localSize"]=V(bounds.size),["worldPosition"]=V(go.transform.position),["collider"]=go.GetComponent<MeshCollider>()&&go.GetComponent<MeshCollider>().sharedMesh==mesh};
            }
            var body=go.GetComponent<Rigidbody>();
            if(body)
            {
                var collider=go.GetComponent<Collider>();
                report["physics"]=new JObject{["massKg"]=body.mass,["useGravity"]=body.useGravity,["kinematic"]=body.isKinematic,["positionMetres"]=V(go.transform.position),["collider"]=collider?collider.GetType().Name:null,["bounce"]=collider&&collider.sharedMaterial?collider.sharedMaterial.bounciness:0};
            }
            var rect=go.GetComponent<RectTransform>();
            if(rect)
            {
                var canvas=go.GetComponentInParent<Canvas>();var scaler=canvas?canvas.GetComponent<CanvasScaler>():null;
                var ui=new JObject{["units"]="reference Canvas pixels, NOT world metres",["anchoredPosition"]=new JArray(rect.anchoredPosition.x,rect.anchoredPosition.y),["size"]=new JArray(rect.sizeDelta.x,rect.sizeDelta.y),["active"]=go.activeSelf};
                ui["actualRectSize"]=new JArray(rect.rect.width,rect.rect.height);
                if(scaler)ui["referenceResolution"]=new JArray(scaler.referenceResolution.x,scaler.referenceResolution.y);
                var button=go.GetComponent<Button>();
                if(button)
                {
                    var action=go.GetComponent<WorldUIButton>();
                    bool bound=action&&action.Target&&Enumerable.Range(0,button.onClick.GetPersistentEventCount()).Any(i=>button.onClick.GetPersistentTarget(i)==action&&button.onClick.GetPersistentMethodName(i)==nameof(WorldUIButton.Toggle));
                    ui["toggleBound"]=bound;ui["toggleTarget"]=action&&action.Target?ProjectContextIndex.StableTarget(action.Target):null;
                    if(!bound)ui["nextCapability"]="configure_ui on this button with toggle_target set to the actual owned panel target. No C# script is needed for a simple toggle.";
                }
                report["ui"]=ui;
            }
        }
    }
}
