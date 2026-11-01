using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;
using Object=UnityEngine.Object;

/// <summary>External fresh-consumer audit: no local package links, network inference or source changes.</summary>
public static class CleanInstall13Audit
{
    public static void Run()
    {
        string output=Environment.GetEnvironmentVariable("WW_CLEAN13_OUTPUT");
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_CLEAN13_ACCEPTANCE")!="1"||string.IsNullOrWhiteSpace(output))throw new InvalidOperationException("Explicit clean-consumer batch acceptance flags are required.");
        var report=new JObject{["kind"]="actual-fresh-tarball-consumer-compile-and-asset-loading",["startedUtc"]=DateTime.UtcNow.ToString("o"),["unity"]=Application.unityVersion,
            ["sourceCommit"]=null,["networkInferenceUsed"]=false,["onlineTextureDownloadUsed"]=false,["renderingAcceptance"]=false,
            ["limitations"]=new JArray("This run checks actual UPM installation, resource loading and a persisted minimal scene; it does not render a screenshot or execute a Player build.","Compiler-error count must be attached from the full Unity log by the calling process; the audit records the compile state at execution.")};
        var checks=new JObject();bool passed=false;
        try
        {
            output=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,report.ToString());
            string proofPath=Environment.GetEnvironmentVariable("WW_CLEAN13_PACKAGE_PROOF");
            if(string.IsNullOrWhiteSpace(proofPath)||!File.Exists(proofPath))throw new FileNotFoundException("Supply WW_CLEAN13_PACKAGE_PROOF pointing to results/upm-package-1.3.json.");
            proofPath=Path.GetFullPath(proofPath);var proof=JObject.Parse(File.ReadAllText(proofPath));var files=(JArray)proof["files"];
            string sourceCommit=(string)proof["sourceCommit"],expectedCommit=Environment.GetEnvironmentVariable("WW_CLEAN13_EXPECTED_SOURCE");
            Require(sourceCommit!=null&&sourceCommit.Length==40&&sourceCommit.All(c=>(c>='0'&&c<='9')||(c>='a'&&c<='f'))&&sourceCommit==expectedCommit,"WW_CLEAN13_EXPECTED_SOURCE must match the archive proof's full frozen source commit.");
            Require(files!=null&&files.Count>0&&files.Count<=4096&&files.Count==(int)proof["snapshotFileCount"]&&files.Count==(int)proof["archivedFileCount"],"The archive proof must contain its exact bounded snapshot/archive file set.");
            int expectedFileCount=files.Count;report["sourceCommit"]=sourceCommit;report["expectedFileCount"]=expectedFileCount;report["archiveProofSha256"]=Hash(File.ReadAllBytes(proofPath));
            string archiveRoot=Path.GetDirectoryName(Path.GetDirectoryName(proofPath));
            string archivePath=Path.GetFullPath(Path.Combine(archiveRoot,(string)proof["archive"]));
            Require(archivePath.StartsWith(archiveRoot+Path.DirectorySeparatorChar,StringComparison.Ordinal),"Archive path escapes the proof root.");
            var archiveBytes=File.ReadAllBytes(archivePath);Require(archiveBytes.Length<=32*1024*1024,"Distribution archive exceeds audit bound.");
            checks["distributionArchiveBytesMatchProof"]=archiveBytes.LongLength==(long)proof["bytes"]&&Hash(archiveBytes)==(string)proof["sha256"];
            Require((bool)checks["distributionArchiveBytesMatchProof"],"The distribution tarball does not match the archive proof.");
            byte[] originalManifest=ReadTarManifest(archiveBytes);report["distributionArchive"]=archivePath;
            var info=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SceneToolExecutor).Assembly);
            Require(info!=null&&!string.IsNullOrWhiteSpace(info.resolvedPath),"WorldWeaver package could not be resolved.");
            string package=Path.GetFullPath(info.resolvedPath);report["packageInfo"]=new JObject{["name"]=info.name,["version"]=info.version,["source"]=info.source.ToString(),["resolvedPath"]=package,["packageId"]=info.packageId};
            checks["packageNameAndVersion"]=(info.name=="com.worldweaver.toolkit"&&info.version=="1.3.0");
            checks["packageInstalledFromLocalTarball"]=(info.source.ToString()=="LocalTarball");
            var expected=files.ToDictionary(f=>(string)f["path"],f=>(string)f["sha256"],StringComparer.Ordinal);
            var actual=PackageHashes(package);
            report["installedFileCount"]=actual.Count;
            checks["allInstalledPathsMatchArchiveProof"]=actual.Count==expectedFileCount&&expected.Count==expectedFileCount&&expected.Keys.All(actual.ContainsKey);
            checks["allNonManifestInstalledFileBytesMatchArchiveProof"]=expected.Count(f=>f.Key!="package.json")==expectedFileCount-1&&expected.Where(f=>f.Key!="package.json").All(f=>actual.TryGetValue(f.Key,out var hash)&&hash==f.Value);
            report["exactNonManifestFileByteCount"]=expectedFileCount-1;
            checks["originalDistributionManifestBytesMatchArchiveProof"]=Hash(originalManifest)==expected["package.json"];
            Require((bool)checks["originalDistributionManifestBytesMatchArchiveProof"],"The original package manifest does not match the archive proof.");
            var manifestAccounting=ManifestAccounting(originalManifest,File.ReadAllBytes(Path.Combine(package,"package.json")),archiveBytes,package,info.name);
            report["installedManifestAccounting"]=manifestAccounting;
            checks["installedManifestContentMatchesExceptValidatedUpmFingerprint"]=(bool)manifestAccounting["accepted"];
            checks["compileStateReadyAtExecution"]=!EditorApplication.isCompiling&&!EditorApplication.isUpdating;

            int textureCount=0;
            foreach(var item in WorldMaterialLibrary.BuiltIns)
            {
                string id=item.Id.Substring("builtin:".Length);
                foreach(string role in new[]{"albedo","normal","metallic_smoothness"})
                {
                    var map=WorldMaterialLibrary.BuiltInTexture(id,role);Require(map&&map.width==256&&map.height==256,"Starter texture missing: "+id+"/"+role);
                    var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(map))as TextureImporter;
                    Require(importer!=null&&importer.sRGBTexture==(role=="albedo")&&importer.textureType==(role=="normal"?TextureImporterType.NormalMap:TextureImporterType.Default),"Texture importer mismatch: "+id+"/"+role);
                    textureCount++;
                }
            }
            report["loadedStarterTextureMaps"]=textureCount;checks["all39StarterMapsAndImportSettingsLoad"]=textureCount==39;
            var body=Resources.Load<Font>("WorldWeaver/UI/Fonts/NotoSans");var heading=Resources.Load<Font>("WorldWeaver/UI/Fonts/CormorantGaramond");
            checks["bothDialogueFontsLoad"]=body&&heading;
            checks["dialogueThemeAndStylesheetLoad"]=Resources.Load<ThemeStyleSheet>("WorldWeaver/UI/DialogueTheme")&&Resources.Load<StyleSheet>("WorldWeaver/UI/Dialogue");
            checks["editorChatStylesheetLoads"]=AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.worldweaver.toolkit/Editor/Assistant/Chat.uss")!=null;
            string schema=EditorToolSchema.CommandSchema().ToString(),decoder=EditorToolSchema.DecoderCommandSchema().ToString();
            checks["newMaterialAndNpcToolsPresentInBothSchemas"]=new[]{"material_search","material_import","material_apply","configure_npc_ai","draft_script"}.All(tool=>schema.Contains(tool)&&decoder.Contains(tool));

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var marker=new GameObject("Existing clean-consumer marker");
            var plan=new EditorPlan{actions=new List<EditorCommand>{
                new EditorCommand{tool="create_world",name="Clean Install World",args=new JObject{["width"]=32,["length"]=32,["height"]=12,["resolution"]=33,["waterLevel"]=0,["trees"]=0,["rocks"]=0,["buildings"]=0}},
                new EditorCommand{tool="create_object",name="Clean Install House",target="@Clean Install World",args=new JObject{["kind"]="house",["on_terrain"]=false,["position"]=new JArray(0,3,0)}}}};
            var result=SceneToolExecutor.Execute(plan);report["sceneToolResult"]=JObject.FromObject(result);Require(result.Success,result.Error);
            checks["actualWorldAndHouseToolsWork"]=GameObject.Find("Clean Install World")&&GameObject.Find("Clean Install House");
            ValidateScene(checks);
            Undo.FlushUndoRecordObjects();Undo.PerformUndo();
            checks["undoPreservesExistingSceneMarker"]=marker&&!GameObject.Find("Clean Install World")&&!GameObject.Find("Clean Install House");
            result=SceneToolExecutor.Execute(plan);Require(result.Success,result.Error);
            const string scene="Assets/CleanInstall-WorldWeaver13.unity";Require(EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),scene),"Could not save consumer scene.");EditorSceneManager.OpenScene(scene);ValidateScene(checks);
            checks["sceneReloadRetainsTexturedWorldAndConsumerMarker"]=GameObject.Find("Existing clean-consumer marker")&&GameObject.Find("Clean Install House")&&GameObject.Find("Clean Install World");
            report["savedScene"]=scene;
            var after=PackageHashes(package);
            checks["allInstalledBytesUnchangedAfterSceneGeneration"]=actual.Count==after.Count&&actual.All(f=>after.TryGetValue(f.Key,out var hash)&&hash==f.Value);
            passed=checks.Properties().All(p=>p.Value.Type==JTokenType.Boolean&&(bool)p.Value);
        }
        catch(Exception error){report["error"]=error.ToString();}
        finally
        {
            report["checks"]=checks;report["passed"]=passed;report["finishedUtc"]=DateTime.UtcNow.ToString("o");
            try{Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));File.WriteAllText(output,report.ToString());}
            catch(Exception error){UnityEngine.Debug.LogError("Cannot write clean-consumer audit: "+error);passed=false;}
            finally{EditorApplication.Exit(passed?0:1);}
        }
    }
    static void ValidateScene(JObject checks)
    {
        var world=GameObject.Find("Clean Install World");var house=GameObject.Find("Clean Install House");Require(world&&house,"World/house scene objects missing.");
        var terrain=world.GetComponentInChildren<Terrain>();Require(terrain&&terrain.terrainData,"Generated Terrain missing.");
        checks["terrainLayersHaveResolvedColorAndNormalMaps"]=terrain.terrainData.terrainLayers.Length==3&&terrain.terrainData.terrainLayers.All(l=>l&&l.diffuseTexture&&l.normalMapTexture);
        var renderers=world.GetComponentsInChildren<Renderer>(true);
        checks["allRenderersHaveSupportedNonErrorMaterials"]=renderers.Length>0&&renderers.All(r=>r.sharedMaterials.Length>0&&r.sharedMaterials.All(m=>m&&m.shader&&m.shader.name!="Hidden/InternalErrorShader"&&(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null||m.shader.isSupported)));
        var walls=house.GetComponentsInChildren<Renderer>().Single(r=>r.name=="Walls");var roof=house.GetComponentsInChildren<Renderer>().Single(r=>r.name=="Gabled roof");
        checks["houseWallAndRoofHaveActualPbrTextureMaps"]=new[]{walls,roof}.All(r=>r.sharedMaterial.mainTexture&&r.sharedMaterial.GetTexture("_BumpMap")&&r.sharedMaterial.GetTexture("_MetallicGlossMap"));
        var mesh=roof.GetComponent<MeshFilter>().sharedMesh;checks["roofUvAndTangentsPersist"]=mesh.uv.Length==mesh.vertexCount&&mesh.tangents.Length==mesh.vertexCount;
    }
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static Dictionary<string,string> PackageHashes(string package)=>Directory.GetFiles(package,"*",SearchOption.AllDirectories).Where(p=>Path.GetFileName(p)!=".DS_Store").ToDictionary(p=>Relative(package,p),p=>Hash(File.ReadAllBytes(p)),StringComparer.Ordinal);
    static JObject ManifestAccounting(byte[] original,byte[] installed,byte[] archive,string package,string packageName)
    {
        var settings=new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error};
        var before=JObject.Parse(Encoding.UTF8.GetString(original),settings);var after=JObject.Parse(Encoding.UTF8.GetString(installed),settings);
        Require(before.Property("_fingerprint")==null,"The distribution manifest unexpectedly contains a cache fingerprint.");
        string expectedFingerprint;using(var md5=MD5.Create())expectedFingerprint=BitConverter.ToString(md5.ComputeHash(archive)).Replace("-","").ToLowerInvariant();
        var field=after.Property("_fingerprint");string fingerprint=field?.Value.Type==JTokenType.String?(string)field.Value:null;
        bool fullFingerprintValid=fingerprint!=null&&fingerprint.Length==32&&fingerprint.All(c=>(c>='0'&&c<='9')||(c>='a'&&c<='f'))&&fingerprint==expectedFingerprint;
        bool directoryValid=Path.GetFileName(package.TrimEnd(Path.DirectorySeparatorChar))==packageName+"@"+expectedFingerprint.Substring(0,12);
        bool exactlyOneAddedField=field!=null&&after.Properties().Count()==before.Properties().Count()+1;
        if(field!=null)field.Remove();bool sameContent=JToken.DeepEquals(before,after);
        return new JObject{["originalDistributionSha256"]=Hash(original),["installedManifestSha256"]=Hash(installed),["literalBytesEqual"]=original.SequenceEqual(installed),
            ["allowedCacheField"]="_fingerprint",["observedFingerprint"]=fingerprint,["expectedArchiveMd5Fingerprint"]=expectedFingerprint,
            ["full32LowercaseHexFingerprintMatchesArchiveMd5"]=fullFingerprintValid,["exactCacheDirectoryMatchesFingerprintPrefix"]=directoryValid,
            ["exactlyOneAddedField"]=exactlyOneAddedField,["allOriginalManifestContentUnchanged"]=sameContent,
            ["accepted"]=fullFingerprintValid&&directoryValid&&exactlyOneAddedField&&sameContent,
            ["scope"]="Only the observed UPM cache fingerprint is accounted for; distribution bytes remain bound by SHA256 and all installed bytes are compared before/after scene generation."};
    }
    static byte[] ReadTarManifest(byte[] archive)
    {
        byte[] tar;using(var input=new MemoryStream(archive))using(var gzip=new GZipStream(input,CompressionMode.Decompress))using(var output=new MemoryStream())
        {
            var buffer=new byte[32768];int read;while((read=gzip.Read(buffer,0,buffer.Length))>0){Require(output.Length+read<=32*1024*1024,"Expanded archive exceeds audit bound.");output.Write(buffer,0,read);}tar=output.ToArray();
        }
        byte[] manifest=null;int cursor=0,entries=0;
        while(cursor+512<=tar.Length)
        {
            bool zero=true;for(int i=0;i<512;i++)if(tar[cursor+i]!=0){zero=false;break;}if(zero)break;
            Require(++entries<=4096,"Too many archive entries.");
            string name=Encoding.ASCII.GetString(tar,cursor,100).TrimEnd('\0');string prefix=Encoding.ASCII.GetString(tar,cursor+345,155).TrimEnd('\0');if(prefix.Length>0)name=prefix+"/"+name;
            string sizeText=Encoding.ASCII.GetString(tar,cursor+124,12).Trim('\0',' ');long size=Convert.ToInt64(sizeText.Length==0?"0":sizeText,8);
            Require(size>=0&&size<=32*1024*1024&&cursor+512+size<=tar.Length,"Invalid bounded tar entry.");
            if(name=="package/package.json")
            {
                Require(manifest==null&&(tar[cursor+156]==0||tar[cursor+156]==(byte)'0')&&size<=65536,"Invalid or duplicate distribution manifest entry.");
                manifest=new byte[(int)size];Buffer.BlockCopy(tar,cursor+512,manifest,0,(int)size);
            }
            cursor=checked(cursor+512+(int)(((size+511)/512)*512));
        }
        Require(manifest!=null,"The distribution tarball has no package/package.json entry.");return manifest;
    }
    static string Relative(string root,string path)=>path.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\','/');
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();}
}
