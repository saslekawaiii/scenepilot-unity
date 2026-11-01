using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver.Editor.Assistant;
using Object=UnityEngine.Object;

namespace WorldWeaver.Tests
{
    public sealed class WorldMaterialLibraryTests
    {
        private HashSet<string> originalFolders;private int undo;
        [SetUp] public void Setup()
        {
            if(!Application.isBatchMode) Assert.Ignore("Запускайте интеграционные проверки материалов в отдельном batchmode-проекте, чтобы сохранить открытые пользовательские сцены.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            originalFolders=new HashSet<string>(Directory.Exists(WorldMaterialLibrary.Root)?Directory.GetDirectories(WorldMaterialLibrary.Root):Array.Empty<string>(),StringComparer.Ordinal);
            Undo.IncrementCurrentGroup();undo=Undo.GetCurrentGroup();
        }
        [TearDown] public void Cleanup()
        {
            if(!Application.isBatchMode) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Undo.RevertAllDownToGroup(undo);
            if(Directory.Exists(WorldMaterialLibrary.Root))foreach(string path in Directory.GetDirectories(WorldMaterialLibrary.Root).Where(p=>!originalFolders.Contains(p)))AssetDatabase.DeleteAsset(path.Replace('\\','/'));
        }
        [TestCase("builtin:grass")][TestCase("builtin:wood")][TestCase("builtin:roof")][TestCase("builtin:cloth")][TestCase("builtin:stone")]
        public void StarterMaterialImportsWithoutNetworkAndReusesVerifiedCache(string id)
        {
            var imported=WorldMaterialLibrary.ImportBuiltIn(id);var material=AssetDatabase.LoadAssetAtPath<Material>(imported.MaterialPath);
            Assert.That(material,Is.Not.Null);Assert.That(material.mainTexture.width,Is.EqualTo(256));
            Assert.That(material.GetTexture("_BumpMap"),Is.Not.Null);Assert.That(material.GetTexture("_MetallicGlossMap"),Is.Not.Null);
            var layer=AssetDatabase.LoadAssetAtPath<TerrainLayer>(imported.TerrainLayerPath);Assert.That(layer.diffuseTexture,Is.SameAs(material.mainTexture));Assert.That(layer.normalMapTexture,Is.Not.Null);
            Assert.That(WorldMaterialLibrary.IsImported(id),Is.True);var again=WorldMaterialLibrary.ImportBuiltIn(id);
            Assert.That(again.CacheHit,Is.True);Assert.That(again.MaterialPath,Is.EqualTo(imported.MaterialPath));Assert.That(again.CreatedAssets,Is.Empty);
        }
        [Test] public void AllStarterMapsHaveCorrectColorSpaceAndNormalImporter()
        {
            Assert.That(WorldMaterialLibrary.BuiltIns.Length,Is.EqualTo(13));
            foreach(var descriptor in WorldMaterialLibrary.BuiltIns)
            {
                string id=descriptor.Id.Substring("builtin:".Length);
                foreach(string role in new[]{"albedo","normal","metallic_smoothness"})
                {
                    var map=WorldMaterialLibrary.BuiltInTexture(id,role);Assert.That(map.width,Is.EqualTo(256));Assert.That(map.wrapMode,Is.EqualTo(TextureWrapMode.Repeat));
                    var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(map));
                    Assert.That(importer.sRGBTexture,Is.EqualTo(role=="albedo"),id+"/"+role);
                    Assert.That(importer.textureType,Is.EqualTo(role=="normal"?TextureImporterType.NormalMap:TextureImporterType.Default),id+"/"+role);
                }
            }
        }
        [Test] public void ModifiedCachedAssetsArePreservedAndNeverSilentlyOverwritten()
        {
            var first=DedicatedCache("builtin:plaster");var material=AssetDatabase.LoadAssetAtPath<Material>(first.MaterialPath);
            material.color=Color.magenta;EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
            byte[] changed=File.ReadAllBytes(first.MaterialPath);
            var second=WorldMaterialLibrary.ImportBuiltIn("builtin:plaster");Assert.That(second.MaterialPath,Is.Not.EqualTo(first.MaterialPath));
            CollectionAssert.AreEqual(changed,File.ReadAllBytes(first.MaterialPath));Assert.That(material.color,Is.EqualTo(Color.magenta));
        }
        [Test] public void ImportDoesNotSaveAnUnrelatedDirtyConsumerMaterial()
        {
            var old=DedicatedCache("builtin:wood");var consumer=AssetDatabase.LoadAssetAtPath<Material>(old.MaterialPath);
            byte[] before=File.ReadAllBytes(old.MaterialPath);consumer.color=Color.cyan;EditorUtility.SetDirty(consumer);
            WorldMaterialLibrary.ImportBuiltIn("builtin:metal");
            CollectionAssert.AreEqual(before,File.ReadAllBytes(old.MaterialPath));Assert.That(EditorUtility.IsDirty(consumer),Is.True);
        }
        private static MaterialImportResult DedicatedCache(string id)
        {
            var source=WorldMaterialLibrary.ImportBuiltIn(id);string folder=WorldMaterialLibrary.Root+"/zz-material-test-"+Guid.NewGuid().ToString("N");
            Assert.That(AssetDatabase.CreateFolder(WorldMaterialLibrary.Root,Path.GetFileName(folder)),Is.Not.Empty);
            var material=Object.Instantiate(AssetDatabase.LoadAssetAtPath<Material>(source.MaterialPath));var layer=Object.Instantiate(AssetDatabase.LoadAssetAtPath<TerrainLayer>(source.TerrainLayerPath));
            string matPath=folder+"/Material.mat",layerPath=folder+"/Terrain.terrainlayer";AssetDatabase.CreateAsset(material,matPath);AssetDatabase.CreateAsset(layer,layerPath);
            AssetDatabase.SaveAssetIfDirty(material);AssetDatabase.SaveAssetIfDirty(layer);
            var receipt=new JObject{["format"]=1,["material_id"]=id,["shader"]=material.shader.name,["license"]="CC0-1.0",["file_sha256"]=new JObject{
                ["Material.mat"]=PolyHavenMaterialClient.Hash(File.ReadAllBytes(matPath)),["Terrain.terrainlayer"]=PolyHavenMaterialClient.Hash(File.ReadAllBytes(layerPath))}};
            File.WriteAllText(folder+"/material-source.json",receipt.ToString());AssetDatabase.ImportAsset(folder+"/material-source.json",ImportAssetOptions.ForceSynchronousImport);
            return new MaterialImportResult{MaterialId=id,MaterialPath=matPath,TerrainLayerPath=layerPath};
        }
        [Test] public void ApplyingTextureCreatesIsolatedMaterialAndUndoRestoresOriginal()
        {
            var imported=WorldMaterialLibrary.ImportBuiltIn("builtin:wood");var original=AssetDatabase.LoadAssetAtPath<Material>(imported.MaterialPath);
            var a=GameObject.CreatePrimitive(PrimitiveType.Cube);var b=GameObject.CreatePrimitive(PrimitiveType.Cube);
            a.GetComponent<Renderer>().sharedMaterial=original;b.GetComponent<Renderer>().sharedMaterial=original;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();var result=WorldMaterialLibrary.Apply(a,"builtin:wood",3,2);
            var applied=a.GetComponent<Renderer>().sharedMaterial;
            Assert.That(result.AppliedRenderers,Is.EqualTo(1));Assert.That(applied,Is.Not.SameAs(original));
            Assert.That(applied.mainTextureScale,Is.EqualTo(new Vector2(3,2)));Assert.That(original.mainTextureScale,Is.EqualTo(Vector2.one));Assert.That(b.GetComponent<Renderer>().sharedMaterial,Is.SameAs(original));
            Undo.FlushUndoRecordObjects();Undo.RevertAllDownToGroup(group);Assert.That(a.GetComponent<Renderer>().sharedMaterial,Is.SameAs(original));
        }
        [Test] public void TerrainApplyClonesSharedDataPreservesOtherLayerAndUndoRestoresCollider()
        {
            var grass=WorldMaterialLibrary.ImportBuiltIn("builtin:grass");var stone=WorldMaterialLibrary.ImportBuiltIn("builtin:stone");
            var data=new TerrainData{heightmapResolution=33,size=new Vector3(32,10,32),alphamapResolution=16};
            data.terrainLayers=new[]{AssetDatabase.LoadAssetAtPath<TerrainLayer>(grass.TerrainLayerPath),AssetDatabase.LoadAssetAtPath<TerrainLayer>(stone.TerrainLayerPath)};
            var weights=new float[16,16,2];for(int z=0;z<16;z++)for(int x=0;x<16;x++){weights[z,x,0]=.3f;weights[z,x,1]=.7f;}data.SetAlphamaps(0,0,weights);
            var a=Terrain.CreateTerrainGameObject(data);var b=Terrain.CreateTerrainGameObject(data);var originalLayer=data.terrainLayers[1];
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();var result=WorldMaterialLibrary.Apply(a,"builtin:sand",2,4,false,0);var applied=a.GetComponent<Terrain>().terrainData;
            Assert.That(result.AppliedTerrainLayers,Is.EqualTo(1));Assert.That(applied,Is.Not.SameAs(data));Assert.That(b.GetComponent<Terrain>().terrainData,Is.SameAs(data));
            Assert.That(applied.terrainLayers[1],Is.SameAs(originalLayer));Assert.That(applied.terrainLayers[0].tileSize,Is.EqualTo(new Vector2(1,.5f)));
            Assert.That(applied.GetAlphamaps(0,0,1,1)[0,0,0],Is.EqualTo(.3f).Within(.01));Assert.That(a.GetComponent<TerrainCollider>().terrainData,Is.SameAs(applied));
            Undo.FlushUndoRecordObjects();Undo.RevertAllDownToGroup(group);Assert.That(a.GetComponent<Terrain>().terrainData,Is.SameAs(data));Assert.That(a.GetComponent<TerrainCollider>().terrainData,Is.SameAs(data));
            Object.DestroyImmediate(a);Object.DestroyImmediate(b);Object.DestroyImmediate(data);
        }
        [Test] public void ChildRenderersNeedExplicitScopeAndInvalidTilingCannotImportAssets()
        {
            var parent=new GameObject("House");var child=GameObject.CreatePrimitive(PrimitiveType.Cube);child.transform.SetParent(parent.transform);
            Assert.Throws<ArgumentException>(()=>WorldMaterialLibrary.Apply(parent,"builtin:roof"));
            Assert.Throws<ArgumentOutOfRangeException>(()=>WorldMaterialLibrary.Apply(parent,"builtin:roof",float.NaN,1,true));
            var result=WorldMaterialLibrary.Apply(parent,"builtin:roof",1,1,true);Assert.That(result.AppliedRenderers,Is.EqualTo(1));
        }
        [TestCase("builtin:../wood")][TestCase("file:/etc/passwd")][TestCase("polyhaven:a/b")][TestCase("polyhaven:Wood")][TestCase("builtin:missing")]
        public void MaterialIdsRejectTraversalUrlsAndUnknownBuiltin(string id)=>Assert.Throws<ArgumentException>(()=>WorldMaterialLibrary.ValidateMaterialId(id));
        [TestCase("http://api.polyhaven.com/info/wood")][TestCase("https://api.polyhaven.com.evil.test/info/wood")]
        [TestCase("https://127.0.0.1/file/ph-assets/Textures/a.jpg")][TestCase("https://dl.polyhaven.org/file/ph-assets/Textures/a.blend")]
        [TestCase("https://dl.polyhaven.org:444/file/ph-assets/Textures/a.jpg")][TestCase("https://name:secret@dl.polyhaven.org/file/ph-assets/Textures/a.jpg")]
        public void TextureEndpointsRejectOtherHostsSchemesArchivesCredentialsAndPorts(string url)=>Assert.Throws<ArgumentException>(()=>PolyHavenMaterialClient.ValidateEndpoint(new Uri(url)));
        [Test] public async Task RussianBuiltinSearchWorksWithoutOnlineService()
        {
            var result=await WorldMaterialLibrary.SearchAsync("деревянные доски","builtin",8);
            Assert.That(result.Materials.Any(m=>m.Id=="builtin:wood"),Is.True);Assert.That(result.Materials.All(m=>m.Source.Contains("WorldWeaver")),Is.True);
        }
        [Test] public async Task OfflineOnlineIdDoesNotTriggerNetworkOrInventMaterial()
        {
            Assert.ThrowsAsync<InvalidOperationException>(async()=>await WorldMaterialLibrary.ImportAsync("polyhaven:uncached_fixture",false));
            var cancelled=new CancellationTokenSource();cancelled.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async()=>await WorldMaterialLibrary.ImportAsync("builtin:wood",false,cancelled.Token));
            await Task.CompletedTask;
        }
        private sealed class FakeHttp:IMaterialHttpTransport
        {
            public readonly Dictionary<string,byte[]> Data=new Dictionary<string,byte[]>();public readonly List<string> Calls=new List<string>();
            public Task<byte[]> GetAsync(Uri uri,int maxBytes,CancellationToken token)
            {token.ThrowIfCancellationRequested();PolyHavenMaterialClient.ValidateEndpoint(uri);Calls.Add(uri.AbsoluteUri);var data=Data[uri.AbsoluteUri];if(data.Length>maxBytes)throw new IOException("oversize");return Task.FromResult(data);}
            public void Json(string url,JObject data)=>Data[url]=Encoding.UTF8.GetBytes(data.ToString());
        }
        private static byte[] FixturePng()
        {
            var texture=new Texture2D(32,32,TextureFormat.RGBA32,false,true);texture.SetPixels(Enumerable.Repeat(new Color(.2f,.4f,.8f),1024).ToArray());texture.Apply();var result=texture.EncodeToPNG();Object.DestroyImmediate(texture);return result;
        }
        private static FakeHttp DownloadFixture(out JObject files)
        {
            var http=new FakeHttp();http.Json("https://api.polyhaven.com/info/test_wood",new JObject{["type"]=1,["name"]="Test Wood",["tags"]=new JArray("wood")});
            byte[] png=FixturePng();string url="https://dl.polyhaven.org/file/ph-assets/Textures/png/1k/test_wood/test_wood_diff_1k.png";
            http.Data[url]=png;files=new JObject{["Diffuse"]=new JObject{["1k"]=new JObject{["png"]=new JObject{["url"]=url,["md5"]=PolyHavenMaterialClient.Hash(png,false),["size"]=png.Length}}}};
            http.Json("https://api.polyhaven.com/files/test_wood",files);return http;
        }
        [Test] public async Task ProviderUsesActualCatalogUrlsAndVerifiesAllReturnedMapBytes()
        {
            var http=DownloadFixture(out var files);var client=new PolyHavenMaterialClient(http);var result=await client.DownloadAsync("test_wood");
            Assert.That(result.Maps.Count,Is.EqualTo(1));Assert.That(result.Maps[0].Role,Is.EqualTo("albedo"));Assert.That(result.Maps[0].Sha256,Is.EqualTo(PolyHavenMaterialClient.Hash(result.Maps[0].Bytes)));
            Assert.That(result.Descriptor.SourceUrl,Is.EqualTo("https://polyhaven.com/a/test_wood"));Assert.That(result.Descriptor.License,Is.EqualTo("CC0-1.0"));Assert.That(http.Calls.Count,Is.EqualTo(3));
        }
        [Test] public void CorruptChecksumOrUnboundedImageIsRejectedBeforeUnityImport()
        {
            var http=DownloadFixture(out var files);files["Diffuse"]["1k"]["png"]["md5"]=new string('0',32);http.Json("https://api.polyhaven.com/files/test_wood",files);
            Assert.ThrowsAsync<InvalidDataException>(async()=>await new PolyHavenMaterialClient(http).DownloadAsync("test_wood"));
            byte[] header=FixturePng();header[16]=127;header[17]=255;header[18]=255;header[19]=255;
            Assert.Throws<InvalidDataException>(()=>PolyHavenMaterialClient.ValidateImageHeader(header));
            Assert.Throws<InvalidDataException>(()=>PolyHavenMaterialClient.ValidateImageHeader(Encoding.UTF8.GetBytes("<html>not an image</html>")));
        }
        [Test] public void ProviderCannotRedirectTextureImportToAnArbitraryWebHost()
        {
            var http=DownloadFixture(out var files);files["Diffuse"]["1k"]["png"]["url"]="https://example.com/instructions.png";http.Json("https://api.polyhaven.com/files/test_wood",files);
            Assert.ThrowsAsync<ArgumentException>(async()=>await new PolyHavenMaterialClient(http).DownloadAsync("test_wood"));Assert.That(http.Calls.Count,Is.EqualTo(2));
        }
        [Test] public async Task SearchUsesProviderRankingNotInventedSlugsAndCachesTheMetadata()
        {
            var http=new FakeHttp();http.Json("https://api.polyhaven.com/search?q=wood&type=textures&limit=2",new JObject{["results"]=new JArray(new JObject{["slug"]="test_wood"})});
            http.Json("https://api.polyhaven.com/info/test_wood",new JObject{["type"]=1,["name"]="Wood",["tags"]=new JArray("wood")});
            var client=new PolyHavenMaterialClient(http);var result=await client.SearchAsync(" WOOD ",2);Assert.That(result.Single().Id,Is.EqualTo("polyhaven:test_wood"));
            await client.SearchAsync("wood",2);Assert.That(http.Calls.Count,Is.EqualTo(2));
        }
        [Test] public void GeneratedHouseHasTexturedMaterialsUvAndTangentsWithoutAnyDownload()
        {
            var result=SceneToolExecutor.Execute(new EditorPlan{actions=new List<EditorCommand>{new EditorCommand{tool="create_object",name="Textured House",args=new JObject{["kind"]="house",["on_terrain"]=false}}}});
            Assert.That(result.Success,Is.True,result.Error);
            try
            {
                var house=GameObject.Find("Textured House");var wall=house.GetComponentsInChildren<Renderer>().Single(r=>r.name=="Walls");Assert.That(wall.sharedMaterial.mainTexture,Is.Not.Null);
                var roof=house.GetComponentsInChildren<MeshFilter>().Single(r=>r.name=="Gabled roof").sharedMesh;
                Assert.That(roof.uv.Length,Is.EqualTo(roof.vertexCount));Assert.That(roof.tangents.Length,Is.EqualTo(roof.vertexCount));Assert.That(roof.uv.Distinct().Count(),Is.GreaterThan(2));
            }
            finally {foreach(string path in result.Results.SelectMany(r=>r.CreatedAssets).Reverse())AssetDatabase.DeleteAsset(path);}
        }
    }
}
