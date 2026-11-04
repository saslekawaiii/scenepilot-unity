using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;
// Offline URP lifecycle regression. Does not repair or represent an agent artifact.
[InitializeOnLoad] public static class MaterialCheckpointAcceptanceRunner
{
    static bool ticking;static JObject report;static string Output=>Environment.GetEnvironmentVariable("WW_MATERIAL_CHECKPOINT_OUTPUT");
    static MaterialCheckpointAcceptanceRunner(){if(!AssetDatabase.IsAssetImportWorkerProcess())EditorApplication.update+=Tick;}
    public static void Run()
    {
        if(Environment.GetEnvironmentVariable("WW_MATERIAL_CHECKPOINT_QA")!="1"||!Application.isBatchMode||File.Exists(Output))throw new InvalidOperationException("Fresh explicit offline fixture required");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var shader=Shader.Find("Universal Render Pipeline/Lit");if(!shader)throw new InvalidOperationException("This fixture requires real URP");
        var before=RenderMaterialInitialization.Capture();var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="PipelineFixture";cube.AddComponent<GeneratedSceneObject>();var material=new Material(shader);material.SetColor("_BaseColor",new Color(.55f,.4f,.25f));material.SetColor("_Color",Color.gray);cube.GetComponent<Renderer>().sharedMaterial=material;
        new GameObject("FixtureCamera").AddComponent<Camera>().transform.position=new Vector3(0,0,-4);
        if(!AssetDatabase.IsValidFolder("Assets/WorldWeaverGenerated/Materials"))AssetDatabase.CreateFolder("Assets/WorldWeaverGenerated","Materials");
        string ownedPath="Assets/WorldWeaverGenerated/Materials/ProbeOwned-"+Guid.NewGuid().ToString("N")+".mat";
        var owned=new Material(shader);owned.SetColor("_BaseColor",new Color(.2f,.3f,.4f));owned.SetColor("_Color",Color.white);AssetDatabase.CreateAsset(owned,ownedPath);
        var second=GameObject.CreatePrimitive(PrimitiveType.Cube);second.name="OwnedMaterialFixture";second.transform.position=Vector3.right*3;second.GetComponent<Renderer>().sharedMaterial=owned;
        string foreignPath="Assets/ProbeForeign-"+Guid.NewGuid().ToString("N")+".mat";var foreign=new Material(shader);foreign.SetColor("_BaseColor",Color.blue);foreign.SetColor("_Color",Color.red);AssetDatabase.CreateAsset(foreign,foreignPath);
        var foreignObject=GameObject.CreatePrimitive(PrimitiveType.Cube);foreignObject.GetComponent<Renderer>().sharedMaterial=foreign;string foreignState=EditorJsonUtility.ToJson(foreign),foreignBytes=File.ReadAllText(foreignPath);
        RenderMaterialInitialization.InitializeChanged(before);
        bool foreignUntouched=EditorJsonUtility.ToJson(foreign)==foreignState&&File.ReadAllText(foreignPath)==foreignBytes;
        bool ownedSaved=!EditorUtility.IsDirty(owned)&&Mathf.Abs(owned.GetColor("_BaseColor").r-owned.GetColor("_Color").r)<.00001f;
        UnityEngine.Object.DestroyImmediate(foreignObject);AssetDatabase.DeleteAsset(foreignPath);
        string checkpoint=AgentCheckpoints.Begin(Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"));AgentCheckpoints.Seal(checkpoint);
        var status=AgentCheckpoints.ExtensionStatus(checkpoint);var job=AgentEditorJobs.RunRuntimeProbe("var m=GameObject.Find(\"PipelineFixture\").GetComponent<Renderer>().sharedMaterial;var a=m.GetColor(\"_BaseColor\");var b=m.GetColor(\"_Color\");probe.Record(\"colors\",new{baseR=a.r,aliasR=b.r});probe.Check(Mathf.Abs(a.r-b.r)<.00001f,\"URP legacy alias initialized\",new{baseR=a.r,aliasR=b.r});yield return null;",5);
        report=new JObject{["checkpoint"]=checkpoint,["job"]=job.Id,["beforePlayCanExtend"]=(bool)status["canExtend"],["foreignMaterialUntouched"]=foreignUntouched,["ownedMaterialInitializedAndSaved"]=ownedSaved,["completed"]=false};Save();
    }
    static void Tick()
    {
        if(ticking||string.IsNullOrEmpty(Output)||!File.Exists(Output)||AssetDatabase.IsAssetImportWorkerProcess()||EditorApplication.isCompiling||EditorApplication.isUpdating)return;ticking=true;
        try
        {
            if(report==null)report=JObject.Parse(File.ReadAllText(Output));if((bool)report["completed"]||AgentEditorJobs.Busy||EditorApplication.isPlayingOrWillChangePlaymode)return;
            var job=AgentEditorJobs.Load((string)report["job"]);var after=AgentCheckpoints.ExtensionStatus((string)report["checkpoint"]);report["jobSuccess"]=job.Success;report["observations"]=job.ProbeObservations;report["afterPlayCanExtend"]=(bool)after["canExtend"];report["afterPlayConflicts"]=after["conflicts"].DeepClone();
            var cube=GameObject.Find("PipelineFixture");var pos=cube.transform.position;cube.transform.position+=Vector3.right;EditorSceneManager.MarkSceneDirty(cube.scene);report["manualTransformBlocked"]=!(bool)AgentCheckpoints.ExtensionStatus((string)report["checkpoint"])["canExtend"];cube.transform.position=pos;
            cube.GetComponent<Renderer>().sharedMaterial.SetColor("_BaseColor",Color.green);report["manualMaterialBlocked"]=!(bool)AgentCheckpoints.ExtensionStatus((string)report["checkpoint"])["canExtend"];
            report["passed"]=(bool)report["foreignMaterialUntouched"]&&(bool)report["ownedMaterialInitializedAndSaved"]&&(bool)report["beforePlayCanExtend"]&&job.Success&&(bool)report["afterPlayCanExtend"]&&(bool)report["manualTransformBlocked"]&&(bool)report["manualMaterialBlocked"];report["completed"]=true;Save();EditorApplication.Exit((bool)report["passed"]?0:1);
        }
        catch(Exception e){if(report==null)report=new JObject();report["completed"]=true;report["passed"]=false;report["error"]=e.ToString();Save();EditorApplication.Exit(1);}
        finally{ticking=false;}
    }
    static void Save()=>File.WriteAllText(Output,report.ToString());
}
