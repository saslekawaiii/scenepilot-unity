using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using WorldWeaver.Editor.Assistant;
using Object=UnityEngine.Object;

/// <summary>Opt-in isolated Unity run: actual texture imports, source preservation and Camera.Render.</summary>
public static class MaterialAcceptanceRunner
{
    private static readonly List<string> created=new List<string>();
    public static async void Run()
    {
        string output=Environment.GetEnvironmentVariable("WW_MATERIAL_OUTPUT");
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_MATERIAL_ACCEPTANCE")!="1"||string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException("Explicit isolated batch material acceptance flag and output path required.");
        output=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report=new JObject{["kind"]="actual-offline-and-polyhaven-material-acceptance",["startedUtc"]=DateTime.UtcNow.ToString("o"),["unity"]=Application.unityVersion,
            ["providerFactoryInjected"]=false,["llmInferenceUsed"]=false,["paidApiUsed"]=false,
            ["limitations"]=new JArray("One actual Poly Haven texture set and authored village; not a general art-quality benchmark.","Camera.Render still images do not demonstrate animation, transparent water, HDRP or a Player build.","Internet failures and built-in fallback are reported separately and cannot pass the online acceptance.")};
        var checks=new JObject();var watch=Stopwatch.StartNew();bool passed=false;
        var before=Files("Assets");var source=Files("Packages/com.worldweaver.toolkit");report["source"]=JObject.FromObject(source);
        using(var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4)))
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var village=SceneToolExecutor.Execute(Plan("create_village","Material Village","",new JObject{["houses"]=5,["trees"]=8,["seed"]=42}));Keep(village);
            var world=GameObject.Find("Material Village");var terrain=world.GetComponentInChildren<Terrain>();
            var door=world.GetComponentsInChildren<Renderer>().First(r=>r.name=="Door");
            var offline=WorldMaterialLibrary.Apply(door.gameObject,"builtin:wood",2,3);created.AddRange(offline.CreatedAssets);
            var terrainResult=WorldMaterialLibrary.Apply(terrain.gameObject,"builtin:grass",2,3);created.AddRange(terrainResult.CreatedAssets);
            checks["offlineWoodAppliedWithNormalAndTiling"]=door.sharedMaterial.mainTexture&&door.sharedMaterial.GetTexture("_BumpMap")&&door.sharedMaterial.mainTextureScale==new Vector2(2,3);
            checks["terrainDiffuseNormalAndTiling"]=terrain.terrainData.terrainLayers[0].diffuseTexture&&terrain.terrainData.terrainLayers[0].normalMapTexture&&terrain.terrainData.terrainLayers[0].tileSize==new Vector2(1,2f/3);
            var normalImporter=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(door.sharedMaterial.GetTexture("_BumpMap")));
            checks["normalImporterLinearNormalMap"]=normalImporter.textureType==TextureImporterType.NormalMap&&!normalImporter.sRGBTexture;
            report["offline"]=JObject.FromObject(offline);report["terrain"]=JObject.FromObject(terrainResult);

            var search=await SceneToolExecutor.ExecuteAsync(Plan("material_search","","",new JObject{["query"]="weathered wood",["source"]="polyhaven",["limit"]=2}),token:timeout.Token);
            Keep(search);report["searchTool"]=JObject.FromObject(search);
            var found=JObject.Parse(search.Results.Single().Summary);var candidates=(JArray)found["Materials"];
            string requested=candidates.Count>0?(string)candidates[0]["Id"]:null;
            if(string.IsNullOrEmpty(requested)||!requested.StartsWith("polyhaven:",StringComparison.Ordinal))throw new InvalidOperationException("Actual Poly Haven search returned no online material. "+(string)found["Warning"]);
            checks["actualOnlineCatalogReturnedExactId"]=true;int callbacks=0;
            var imported=await SceneToolExecutor.ExecuteAsync(Plan("material_import","","",new JObject{["material_id"]=requested,["allow_online"]=true}),token:timeout.Token,beforeMutation:()=>callbacks++);
            Keep(imported);report["importTool"]=JObject.FromObject(imported);var importedData=JObject.Parse(imported.Results.Single().Summary);
            string actual=(string)importedData["MaterialId"];bool online=actual==requested&&actual.StartsWith("polyhaven:",StringComparison.Ordinal);
            checks["actualOnlineImportWithoutFallback"]=online;report["mutationCallbacks"]=callbacks;
            if(!online)throw new InvalidOperationException("Online import used a built-in fallback; not an online acceptance pass. "+(string)importedData["Warning"]);
            var applied=SceneToolExecutor.Execute(Plan("material_apply","","id:"+door.gameObject.GetInstanceID(),new JObject{["material_id"]=actual,["tiling_x"]=1.5,["tiling_y"]=2}));Keep(applied);
            report["applyTool"]=JObject.FromObject(applied);
            string matPath=(string)importedData["MaterialPath"];var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
            checks["onlineBaseNormalAndSmoothnessActuallyBound"]=material.mainTexture&&material.GetTexture("_BumpMap")&&material.GetTexture("_MetallicGlossMap");
            checks["onlineAppliedRendererTiling"]=door.sharedMaterial.mainTexture==material.mainTexture&&door.sharedMaterial.mainTextureScale==new Vector2(1.5f,2);
            string receiptPath=Path.GetDirectoryName(matPath)+"/material-source.json";var receipt=JObject.Parse(File.ReadAllText(receiptPath));
            report["onlineProvenance"]=receipt;report["onlineMaterialShader"]=material.shader.name;
            checks["cc0LicenseAndProviderCredit"]=(string)receipt["license"]=="CC0-1.0"&&((string)receipt["credit"]).Contains("Powered by Poly Haven");
            checks["sourceMapsHaveActualVerifiedMd5ShaAndBytes"]=((JArray)receipt["source_maps"]).Count>=3&&((JArray)receipt["source_maps"]).All(m=>((string)m["sha256"]).Length==64&&((string)m["providerMd5"]).Length==32&&(long)m["bytes"]>0);
            var cached=await WorldMaterialLibrary.ImportAsync(actual,false,timeout.Token);
            report["cacheReadback"]=JObject.FromObject(cached);checks["secondImportUsesCacheNoNetworkOrNewAssets"]=cached.CacheHit&&cached.CreatedAssets.Count==0&&cached.MaterialPath==matPath;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.53f,.57f);
            var camera=new GameObject("Material acceptance camera").AddComponent<Camera>();camera.transform.position=new Vector3(39,31,-42);camera.transform.LookAt(new Vector3(0,4,0));
            camera.fieldOfView=50;camera.farClipPlane=1000;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.37f,.54f,.65f);
            report["villageImage"]=Render(camera,Path.ChangeExtension(output,".village.png"));
            var builtinMaterial=AssetDatabase.LoadAssetAtPath<Material>(WorldMaterialLibrary.ImportBuiltIn("builtin:wood").MaterialPath);
            var left=GameObject.CreatePrimitive(PrimitiveType.Cube);var right=GameObject.CreatePrimitive(PrimitiveType.Cube);
            left.name="Builtin wood";right.name="Actual Poly Haven wood";left.layer=right.layer=30;left.transform.position=new Vector3(-1.65f,1,0);right.transform.position=new Vector3(1.65f,1,0);
            foreach(var box in new[]{left,right}){box.transform.localScale=new Vector3(2,2,2);box.transform.rotation=Quaternion.Euler(7,25,0);}
            left.GetComponent<Renderer>().sharedMaterial=builtinMaterial;right.GetComponent<Renderer>().sharedMaterial=material;
            camera.cullingMask=1<<30;camera.transform.position=new Vector3(0,2,-10);camera.transform.LookAt(new Vector3(0,1,0));camera.orthographic=true;camera.orthographicSize=2.5f;camera.backgroundColor=new Color(.045f,.055f,.065f);
            report["comparisonImage"]=Render(camera,Path.ChangeExtension(output,".comparison.png"));report["comparisonLayout"]="Actual Camera.Render: built-in procedural wood cube on the left; the actual downloaded Poly Haven material on the right.";
            checks["sourceAssetsPreservedBeforeCleanup"]=Unchanged(before);checks["packageSourceUnchanged"]=Unchanged(source);
            passed=checks.Properties().All(p=>p.Value.Type==JTokenType.Boolean&&(bool)p.Value);
        }
        catch(Exception error){report["error"]=error.ToString();}
        finally
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            foreach(string path in created.Distinct().Reverse())
            {
                if(Directory.Exists(path)&&Directory.EnumerateFileSystemEntries(path).Any())continue;
                if(File.Exists(path)||Directory.Exists(path))AssetDatabase.DeleteAsset(path);
            }
            checks["originalConsumerAssetBytesPreservedAfterCleanup"]=Unchanged(before);
            checks["createdAssetsRemoved"]=created.All(p=>!File.Exists(p)&&!Directory.Exists(p));
            passed=passed&&checks.Properties().All(p=>p.Value.Type==JTokenType.Boolean&&(bool)p.Value);
            report["checks"]=checks;report["passed"]=passed;report["ms"]=watch.ElapsedMilliseconds;report["finishedUtc"]=DateTime.UtcNow.ToString("o");
            File.WriteAllText(output,report.ToString());EditorApplication.Exit(passed?0:1);
        }
    }
    private static EditorPlan Plan(string tool,string name,string target,JObject args)=>new EditorPlan{actions=new List<EditorCommand>{new EditorCommand{tool=tool,name=name,target=target,args=args}}};
    private static void Keep(EditorBatchResult result)
    {
        created.AddRange(result.Results.SelectMany(r=>r.CreatedAssets));if(!result.Success)throw new InvalidOperationException(result.Error);
    }
    private static Dictionary<string,string> Files(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".DS_Store",StringComparison.Ordinal)).OrderBy(p=>p,StringComparer.Ordinal)
        .ToDictionary(p=>p.Replace('\\','/'),p=>PolyHavenMaterialClient.Hash(File.ReadAllBytes(p)),StringComparer.Ordinal);
    private static bool Unchanged(Dictionary<string,string> files)=>files.All(p=>File.Exists(p.Key)&&PolyHavenMaterialClient.Hash(File.ReadAllBytes(p.Key))==p.Value);
    private static JObject Render(Camera camera,string path)
    {
        var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);var old=camera.targetTexture;var active=RenderTexture.active;
        var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();
            byte[] bytes=pixels.EncodeToPNG();File.WriteAllBytes(path,bytes);
            return new JObject{["path"]=path,["sha256"]=PolyHavenMaterialClient.Hash(bytes),["width"]=1280,["height"]=720,["method"]="Actual Camera.Render; no image compositing or pixel edits"};
        }
        finally{camera.targetTexture=old;RenderTexture.active=active;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);}
    }
}
