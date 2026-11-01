using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace WorldWeaver.Editor.Assistant
{
    [Serializable] public sealed class MaterialImportResult
    {
        public string MaterialId, RequestedMaterialId, MaterialPath, TerrainLayerPath, Warning = "", Credit = "";
        public List<string> CreatedAssets = new List<string>();
        public bool CacheHit;
    }
    [Serializable] public sealed class MaterialApplyResult
    {
        public string MaterialId, MaterialPath, Warning = "", Credit = "";
        public int AppliedRenderers, AppliedTerrainLayers;
        public List<string> CreatedAssets = new List<string>();
    }
    /// <summary>Offline starter library plus an explicit, bounded CC0 Poly Haven import pipeline.</summary>
    public static class WorldMaterialLibrary
    {
        public const string Root = "Assets/WorldWeaverGenerated/Materials";
        public const string PackageTextures = "Packages/com.worldweaver.toolkit/Runtime/Materials/Textures/";
        public static MaterialTransferProgress LastTransfer {get;private set;}
        public static event Action<MaterialTransferProgress> TransferProgress;
        private static readonly PolyHavenMaterialClient online = new PolyHavenMaterialClient(new MaterialHttpTransport(progress:new Progress<MaterialTransferProgress>(OnTransfer)));
        private static void OnTransfer(MaterialTransferProgress value)
        {
            if(value.Bytes<=MaterialHttpTransport.RangeChunkBytes)LastTransfer=null;
            if(LastTransfer!=null&&LastTransfer.File==value.File&&LastTransfer.Bytes>value.Bytes)return;
            LastTransfer=value;TransferProgress?.Invoke(value);
        }
        private sealed class BuiltIn
        {
            public string Id, Name, Tags; public Color Color; public float Tile;
            public BuiltIn(string id,string name,string tags,Color color,float tile=2) { Id=id;Name=name;Tags=tags;Color=color;Tile=tile; }
        }
        private static readonly BuiltIn[] starter = {
            new BuiltIn("grass","Луговая трава","grass meadow green ground трава земля луг",new Color32(74,105,51,255),4),
            new BuiltIn("stone","Необработанный камень","stone rock grey cliff foundation камень скала фундамент",new Color32(121,126,120,255)),
            new BuiltIn("plaster","Известковая штукатурка","plaster wall beige stucco штукатурка стена",new Color32(209,191,155,255)),
            new BuiltIn("wood","Деревянные доски","wood planks timber door gate дерево доски дверь ворота",new Color32(137,93,53,255)),
            new BuiltIn("roof","Черепичная кровля","roof tiles terracotta red крыша черепица кровля",new Color32(119,60,44,255)),
            new BuiltIn("metal","Матовый металл","metal steel iron металл сталь железо",new Color32(133,142,146,255)),
            new BuiltIn("sand","Песок и грунтовая дорога","sand soil road dirt desert песок грунт дорога пустыня",new Color32(199,163,105,255),4),
            new BuiltIn("snow","Снег","snow ice white alpine снег лёд белый",new Color32(224,232,232,255),4),
            new BuiltIn("bark","Кора дерева","bark tree trunk кора дерево ствол",new Color32(102,74,46,255)),
            new BuiltIn("cloth","Ткань","cloth fabric coat hat ткань одежда плащ шляпа",new Color32(163,163,163,255),1),
            new BuiltIn("leather","Кожа","leather satchel bag кожа сумка",new Color32(112,80,52,255),1),
            new BuiltIn("water","Рябь на воде","water lake ripple вода озеро",new Color32(57,122,143,255),6),
            new BuiltIn("cobblestone","Брусчатка","cobblestone square paving pavement брусчатка площадь мостовая",new Color32(139,132,113,255),3),
        };
        public static string ValidateMaterialId(string materialId)
        {
            string[] parts = (materialId ?? "").Split(':');
            if (parts.Length != 2 || (parts[0] != "builtin" && parts[0] != "polyhaven")) throw new ArgumentException("Use a material ID returned by material_search: builtin:<id> or polyhaven:<id>.");
            PolyHavenMaterialClient.ValidateSlug(parts[1]);
            if (parts[0] == "builtin" && !starter.Any(b => b.Id == parts[1])) throw new ArgumentException("Unknown built-in material: " + materialId);
            return materialId;
        }
        public static MaterialDescriptor[] BuiltIns => starter.Select(b => new MaterialDescriptor { Id="builtin:"+b.Id,Name=b.Name,Tags=b.Tags,
            Source="WorldWeaver — встроенная библиотека",SourceUrl="https://creativecommons.org/publicdomain/zero/1.0/",Imported=IsImported("builtin:"+b.Id) }).ToArray();
        public static async Task<MaterialSearchResult> SearchAsync(string query,string source="any",int limit=8,CancellationToken token=default)
        {
            query=(query??"").Trim().ToLowerInvariant();
            if(query.Length>100 || query.Length==0) throw new ArgumentException("Texture search needs 1–100 characters.");
            if(limit<1||limit>8) throw new ArgumentOutOfRangeException(nameof(limit));
            if(source!="any"&&source!="builtin"&&source!="polyhaven") throw new ArgumentException("source must be any, builtin or polyhaven.");
            token.ThrowIfCancellationRequested(); var result=new MaterialSearchResult();
            if(source!="polyhaven")
            {
                var words=query.Split(new[]{' ','-','_'},StringSplitOptions.RemoveEmptyEntries);
                result.Materials.AddRange(BuiltIns.Select(b=>new{Item=b,Score=words.Count(w=>(b.Id+" "+b.Name+" "+b.Tags).IndexOf(w,StringComparison.OrdinalIgnoreCase)>=0)})
                    .Where(b=>b.Score>0).OrderByDescending(b=>b.Score).ThenBy(b=>b.Item.Id).Take(limit).Select(b=>b.Item));
            }
            if(source!="builtin")
            {
                try
                {
                    // Keep room for online alternatives even when a short query matches many starter entries.
                    int localCount=source=="any"?Math.Min(result.Materials.Count,Math.Max(1,limit/2)):0;
                    if(result.Materials.Count>localCount)result.Materials.RemoveRange(localCount,result.Materials.Count-localCount);
                    var found=limit>localCount?await online.SearchAsync(query,limit-localCount,token):Array.Empty<MaterialDescriptor>();
                    foreach(var item in found) { item.Imported=IsImported(item.Id);result.Materials.Add(item); }
                }
                catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                catch(Exception error)when(error is HttpRequestException || error is IOException || error is OperationCanceledException)
                {
                    result.Warning="Poly Haven недоступен. Встроенные материалы работают без интернета. "+error.GetType().Name;
                    if(result.Materials.Count==0&&source=="any")result.Materials.AddRange(BuiltIns.Take(limit));
                }
            }
            return result;
        }
        public static bool IsImported(string materialId)
        {
            ValidateMaterialId(materialId); return FindCached(materialId)!=null;
        }
        public static MaterialImportResult ImportBuiltIn(string materialId,Action beforeMutation=null)
        {
            ValidateMaterialId(materialId);
            if(!materialId.StartsWith("builtin:",StringComparison.Ordinal))throw new ArgumentException("The offline importer accepts built-in IDs only.");
            var cached=FindCached(materialId);if(cached!=null)return cached;
            var definition=starter.Single(b=>"builtin:"+b.Id==materialId);
            beforeMutation?.Invoke();
            using(var tx=new MaterialAssets(materialId))
            {
                var material=new Material(LitShader()){name=definition.Name};
                ConfigureBuiltIn(material,definition.Id,Color.white,false);
                string materialPath=tx.Create(material,"Material.mat");
                var layer=new TerrainLayer { name=definition.Name,diffuseTexture=BuiltInTexture(definition.Id,"albedo"),normalMapTexture=BuiltInTexture(definition.Id,"normal"),
                    normalScale=.6f,tileSize=Vector2.one*definition.Tile,metallic=definition.Id=="metal"?.9f:0,smoothness=definition.Id=="water"?.84f:.16f };
                string layerPath=tx.Create(layer,"Terrain.terrainlayer");
                return tx.Finish(materialId,materialPath,layerPath,"WorldWeaver original procedural textures — CC0-1.0",new JArray());
            }
        }
        public static async Task<MaterialImportResult> ImportAsync(string materialId,bool allowOnline,CancellationToken token=default,Action beforeMutation=null)
        {
            ValidateMaterialId(materialId); token.ThrowIfCancellationRequested();
            var cached=FindCached(materialId);if(cached!=null)return cached;
            if(materialId.StartsWith("builtin:",StringComparison.Ordinal))return ImportBuiltIn(materialId,beforeMutation);
            if(!allowOnline)throw new InvalidOperationException("The Poly Haven material is not cached. Use material_import with allow_online=true or choose builtin:<id>.");
            MaterialDownload downloaded;
            try { downloaded=await online.DownloadAsync(materialId.Substring("polyhaven:".Length),token); }
            catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
            catch(Exception error)when(error is HttpRequestException || error is OperationCanceledException || error is IOException&&!(error is InvalidDataException))
            {
                var fallback=ImportBuiltIn(FallbackId(materialId),beforeMutation);fallback.RequestedMaterialId=materialId;
                fallback.Warning="Poly Haven недоступен; применим встроенный материал "+fallback.MaterialId+". "+error.GetType().Name+": "+new string((error.Message??"").Take(240).ToArray());return fallback;
            }
            token.ThrowIfCancellationRequested();
            beforeMutation?.Invoke();
            using(var tx=new MaterialAssets(materialId))
            {
                var maps=new Dictionary<string,Texture2D>();var provenance=new JArray();
                foreach(var map in downloaded.Maps)
                {
                    token.ThrowIfCancellationRequested();
                    string path=tx.Write(map.Role+map.Extension,map.Bytes);
                    ConfigureTexture(path,map.Role); maps[map.Role]=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if(!maps[map.Role])throw new InvalidDataException("Unity could not import "+map.Role+" texture.");
                    provenance.Add(new JObject{["role"]=map.Role,["url"]=map.Url,["providerMd5"]=map.Md5,["sha256"]=map.Sha256,["bytes"]=map.Bytes.Length});
                }
                var packed=PackMetallicSmoothness(downloaded.Maps);
                if(packed!=null)
                {
                    string packedPath=tx.Write("metallic_smoothness.png",packed);ConfigureTexture(packedPath,"metallic_smoothness");
                    maps["metallic_smoothness"]=AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath);
                }
                var material=new Material(LitShader()){name=downloaded.Descriptor.Name};ConfigureMaps(material,maps,Color.white,1,1);
                string materialPath=tx.Create(material,"Material.mat");
                var layer=new TerrainLayer{name=downloaded.Descriptor.Name,diffuseTexture=maps["albedo"],normalMapTexture=maps.TryGetValue("normal",out var normal)?normal:null,
                    tileSize=Vector2.one*2,normalScale=.6f,smoothness=.2f};
                string layerPath=tx.Create(layer,"Terrain.terrainlayer");
                var result=tx.Finish(materialId,materialPath,layerPath,"Powered by Poly Haven — CC0-1.0 — "+downloaded.Descriptor.SourceUrl,provenance);
                result.Credit="Powered by Poly Haven";return result;
            }
        }
        private static string FallbackId(string id)
        {
            string value=id.ToLowerInvariant();
            if(value.Contains("wood")||value.Contains("plank"))return "builtin:wood";
            if(value.Contains("grass")||value.Contains("moss"))return "builtin:grass";
            if(value.Contains("roof")||value.Contains("tile"))return "builtin:roof";
            if(value.Contains("sand")||value.Contains("ground")||value.Contains("soil"))return "builtin:sand";
            if(value.Contains("metal")||value.Contains("steel"))return "builtin:metal";
            if(value.Contains("plaster")||value.Contains("wall"))return "builtin:plaster";
            return "builtin:stone";
        }
        public static MaterialApplyResult Apply(GameObject target,string materialId,float tilingX=1,float tilingY=1,bool includeChildren=false,int terrainLayerIndex=0,bool manageUndo=true)
        {
            ValidateMaterialId(materialId);
            if(!target||!target.scene.IsValid())throw new ArgumentException("An explicit loaded scene object is required.");
            if(!FiniteTiling(tilingX)||!FiniteTiling(tilingY))throw new ArgumentOutOfRangeException(nameof(tilingX),"Texture tiling must be finite and between .05 and 100.");
            var terrain=target.GetComponent<Terrain>();
            var renderers=includeChildren?target.GetComponentsInChildren<Renderer>(true):target.GetComponents<Renderer>();
            if(!terrain&&renderers.Length==0)throw new ArgumentException("Target has no Renderer; enable include_children for a building root, or target its Terrain explicitly.");
            if(terrain&&(!terrain.terrainData||terrainLayerIndex<0||terrainLayerIndex>7||terrainLayerIndex>=Math.Max(1,terrain.terrainData.terrainLayers.Length)))
                throw new ArgumentException("Choose an existing terrain layer index (0–7); an empty Terrain accepts index 0.");
            // Unity may reset/reassign the control textures when TerrainData is made into a new
            // asset or terrainLayers changes. Capture the actual paint before either operation.
            float[,,] terrainWeights=null;
            if(terrain&&terrain.terrainData.terrainLayers.Length>0)
            {
                var original=terrain.terrainData;
                if((long)original.alphamapWidth*original.alphamapHeight*original.alphamapLayers>8L*1024*1024)
                    throw new InvalidOperationException("Terrain paint exceeds the 32 MiB material-edit budget; reduce alphamap resolution before applying a texture.");
                terrainWeights=original.GetAlphamaps(0,0,original.alphamapWidth,original.alphamapHeight);
            }
            var imported=FindCached(materialId);
            if(imported==null&&materialId.StartsWith("builtin:",StringComparison.Ordinal))imported=ImportBuiltIn(materialId);
            if(imported==null)throw new InvalidOperationException("Import the online material first with material_import; material_apply does not access the internet.");
            var result=new MaterialApplyResult{MaterialId=materialId,Credit=materialId.StartsWith("polyhaven:",StringComparison.Ordinal)?"Powered by Poly Haven":""};
            if(!imported.CacheHit)result.CreatedAssets.AddRange(imported.CreatedAssets);
            if(manageUndo)Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();
            try
            {
                using(var tx=new MaterialAssets(materialId+" application"))
                {
                    if(renderers.Length>0)
                    {
                        var material=Object.Instantiate(AssetDatabase.LoadAssetAtPath<Material>(imported.MaterialPath));material.name="WorldWeaver "+materialId;
                        SetTiling(material,tilingX,tilingY);result.MaterialPath=tx.Create(material,"Applied.mat");
                        foreach(var renderer in renderers)
                        {
                            Undo.RecordObject(renderer,"WorldWeaver textured material");
                            renderer.sharedMaterials=Enumerable.Repeat(material,Math.Max(1,renderer.sharedMaterials.Length)).ToArray();
                            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);EditorUtility.SetDirty(renderer);result.AppliedRenderers++;
                        }
                    }
                    if(terrain)
                    {
                        // Clone TerrainData so an explicit scene edit cannot mutate another Terrain sharing it.
                        var data=Object.Instantiate(terrain.terrainData);data.name=terrain.terrainData.name+" — material";
                        tx.Create(data,"TerrainData.asset");
                        var layer=Object.Instantiate(AssetDatabase.LoadAssetAtPath<TerrainLayer>(imported.TerrainLayerPath));
                        layer.tileSize=new Vector2(2/tilingX,2/tilingY);tx.Create(layer,"Applied.terrainlayer");
                        var layers=data.terrainLayers;
                        if(layers.Length==0)layers=new[]{layer};else layers[terrainLayerIndex]=layer;
                        data.terrainLayers=layers;
                        if(terrain.terrainData.terrainLayers.Length==0)
                        {
                            int n=data.alphamapResolution;var weights=new float[n,n,1];for(int z=0;z<n;z++)for(int x=0;x<n;x++)weights[z,x,0]=1;data.SetAlphamaps(0,0,weights);
                        }
                        else data.SetAlphamaps(0,0,terrainWeights);
                        Undo.RecordObject(terrain,"WorldWeaver textured terrain");terrain.terrainData=data;EditorUtility.SetDirty(terrain);
                        var collider=terrain.GetComponent<TerrainCollider>();if(collider){Undo.RecordObject(collider,"WorldWeaver textured terrain collider");collider.terrainData=data;EditorUtility.SetDirty(collider);PrefabUtility.RecordPrefabInstancePropertyModifications(collider);}
                        PrefabUtility.RecordPrefabInstancePropertyModifications(terrain);EditorUtility.SetDirty(data);result.AppliedTerrainLayers=1;
                    }
                    result.CreatedAssets.AddRange(tx.CompleteFiles());
                }
                return result;
            }
            catch
            {
                if(manageUndo){Undo.FlushUndoRecordObjects();Undo.RevertAllDownToGroup(undo);}
                if(!imported.CacheHit)RemoveCreated(imported.CreatedAssets);throw;
            }
        }
        private static bool FiniteTiling(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value)&&value>=.05f&&value<=100;
        internal static string SemanticBuiltIn(string key)
        {
            string value=(key??"").ToLowerInvariant();
            if(value.Contains("foliage"))return "grass";
            if(value.Contains("bark"))return "bark";
            if(value.Contains("wood")||value.Contains("gate"))return "wood";
            if(value.Contains("plaster"))return "plaster";
            if(value.Contains("roof"))return "roof";
            if(value.Contains("stone")||value.Contains("rock"))return "stone";
            if(value.Contains("square"))return "cobblestone";
            if(value.Contains("road"))return "sand";
            if(value.Contains("leather"))return "leather";
            if(value.Contains("coat")||value.Contains("hat"))return "cloth";
            if(value.Contains("lake"))return "water";
            return null;
        }
        public static Texture2D BuiltInTexture(string id,string role)
        {
            if(!starter.Any(b=>b.Id==id)||!(role=="albedo"||role=="normal"||role=="metallic_smoothness"))throw new ArgumentException("Unknown built-in texture map.");
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(PackageTextures+id+"_"+role+".png");
            if(!texture)throw new InvalidOperationException("Built-in texture is missing: "+id+"/"+role);return texture;
        }
        internal static void ConfigureBuiltIn(Material material,string id,Color desiredColor,bool matchTint=true)
        {
            var entry=starter.Single(b=>b.Id==id);
            var tint=matchTint?new Color(desiredColor.r/entry.Color.r,desiredColor.g/entry.Color.g,desiredColor.b/entry.Color.b,desiredColor.a):desiredColor;
            ConfigureMaps(material,new Dictionary<string,Texture2D>{{"albedo",BuiltInTexture(id,"albedo")},{"normal",BuiltInTexture(id,"normal")},{"metallic_smoothness",BuiltInTexture(id,"metallic_smoothness")}},tint,1,1);
        }
        private static Shader LitShader()
        {
            var pipeline=GraphicsSettings.currentRenderPipeline;
            if(pipeline&&pipeline.GetType().Name.IndexOf("Universal",StringComparison.OrdinalIgnoreCase)<0)throw new NotSupportedException("The material library supports Built-in and URP; HDRP needs a shader adapter.");
            var shader=Shader.Find(pipeline?"Universal Render Pipeline/Lit":"Standard");
            if(!shader)throw new InvalidOperationException("A supported Lit shader is unavailable.");return shader;
        }
        private static void ConfigureMaps(Material material,Dictionary<string,Texture2D> maps,Color tint,float x,float y)
        {
            if(material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",maps["albedo"]);
            if(material.HasProperty("_MainTex"))material.SetTexture("_MainTex",maps["albedo"]);
            if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",tint);
            if(material.HasProperty("_Color"))material.SetColor("_Color",tint);
            if(maps.TryGetValue("normal",out var normal)&&material.HasProperty("_BumpMap")){material.SetTexture("_BumpMap",normal);material.EnableKeyword("_NORMALMAP");if(material.HasProperty("_BumpScale"))material.SetFloat("_BumpScale",.6f);}
            if(maps.TryGetValue("metallic_smoothness",out var packed)&&material.HasProperty("_MetallicGlossMap"))
            {
                material.SetTexture("_MetallicGlossMap",packed);material.EnableKeyword(material.shader.name.StartsWith("Universal Render Pipeline/",StringComparison.Ordinal)?"_METALLICSPECGLOSSMAP":"_METALLICGLOSSMAP");
                if(material.HasProperty("_GlossMapScale"))material.SetFloat("_GlossMapScale",1);if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",1);
                if(material.HasProperty("_SmoothnessTextureChannel"))material.SetFloat("_SmoothnessTextureChannel",0);
            }
            if(maps.TryGetValue("occlusion",out var ao)&&material.HasProperty("_OcclusionMap"))
            {material.SetTexture("_OcclusionMap",ao);if(material.shader.name.StartsWith("Universal Render Pipeline/",StringComparison.Ordinal))material.EnableKeyword("_OCCLUSIONMAP");}
            SetTiling(material,x,y);
        }
        private static void SetTiling(Material material,float x,float y)
        {
            foreach(string property in new[]{"_BaseMap","_MainTex","_BumpMap","_MetallicGlossMap","_OcclusionMap"})
                if(material.HasProperty(property))material.SetTextureScale(property,new Vector2(x,y));
        }
        private static void ConfigureTexture(string path,string role)
        {
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path)as TextureImporter;if(importer==null)throw new InvalidDataException("Texture importer unavailable: "+path);
            importer.textureType=role=="normal"?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.sRGBTexture=role=="albedo";importer.convertToNormalmap=false;importer.wrapMode=TextureWrapMode.Repeat;
            importer.mipmapEnabled=true;importer.isReadable=false;importer.maxTextureSize=1024;importer.textureCompression=TextureImporterCompression.Compressed;
            importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.SaveAndReimport();
        }
        private static byte[] PackMetallicSmoothness(List<MaterialMapDownload> maps)
        {
            var rough=maps.FirstOrDefault(m=>m.Role=="roughness");var metal=maps.FirstOrDefault(m=>m.Role=="metallic");
            if(rough==null&&metal==null)return null;
            var temporary=new List<Texture2D>();
            try
            {
                Func<MaterialMapDownload,Texture2D> decode=map=>{if(map==null)return null;var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);temporary.Add(texture);if(!ImageConversion.LoadImage(texture,map.Bytes))throw new InvalidDataException("Texture decoding failed.");return texture;};
                var r=decode(rough);var m=decode(metal);int width=r?r.width:m.width,height=r?r.height:m.height;
                if(r&&m&&(r.width!=m.width||r.height!=m.height))throw new InvalidDataException("PBR maps must use the same 1k dimensions.");
                var packed=new Texture2D(width,height,TextureFormat.RGBA32,false,true);temporary.Add(packed);
                var rp=r?r.GetPixels32():null;var mp=m?m.GetPixels32():null;var values=new Color32[width*height];
                for(int i=0;i<values.Length;i++)values[i]=new Color32(mp==null?(byte)0:mp[i].r,0,0,rp==null?(byte)64:(byte)(255-rp[i].r));
                packed.SetPixels32(values);packed.Apply();return packed.EncodeToPNG();
            }
            finally {foreach(var texture in temporary)Object.DestroyImmediate(texture);}
        }
        private static MaterialImportResult FindCached(string id)
        {
            EnsureSafePath(Root);if(!Directory.Exists(Root))return null;
            foreach(string directory in Directory.GetDirectories(Root).Select(d=>d.Replace('\\','/')).OrderByDescending(d=>d,StringComparer.Ordinal).Take(1024))
            {
                EnsureSafePath(directory);string receiptPath=directory+"/material-source.json";
                if(!File.Exists(receiptPath))continue;EnsureSafePath(receiptPath);
                if(new FileInfo(receiptPath).Length>65536)continue;
                JObject receipt;try{receipt=JObject.Parse(File.ReadAllText(receiptPath));}catch(Newtonsoft.Json.JsonException){continue;}
                if((string)receipt["material_id"]!=id||(int?)receipt["format"]!=1||(string)receipt["shader"]!=LitShader().name)continue;
                var hashes=receipt["file_sha256"]as JObject;if(hashes==null||hashes["Material.mat"]==null||hashes["Terrain.terrainlayer"]==null||hashes.Count>20)continue;
                bool valid=true;
                foreach(var item in hashes.Properties())
                {
                    if(Path.GetFileName(item.Name)!=item.Name||item.Name.Contains("..")||item.Name.IndexOfAny(new[]{'/','\\'})>=0){valid=false;break;}
                    string file=directory+"/"+item.Name;EnsureSafePath(file);
                    if(!File.Exists(file)||new FileInfo(file).Length>PolyHavenMaterialClient.MaxImageBytes||PolyHavenMaterialClient.Hash(File.ReadAllBytes(file))!=(string)item.Value){valid=false;break;}
                }
                string mat=directory+"/Material.mat",layer=directory+"/Terrain.terrainlayer";
                var material=valid?AssetDatabase.LoadAssetAtPath<Material>(mat):null;var terrainLayer=valid?AssetDatabase.LoadAssetAtPath<TerrainLayer>(layer):null;
                if(valid&&material&&terrainLayer&&!EditorUtility.IsDirty(material)&&!EditorUtility.IsDirty(terrainLayer))
                    return new MaterialImportResult{MaterialId=id,RequestedMaterialId=id,MaterialPath=mat,TerrainLayerPath=layer,CacheHit=true,Credit=id.StartsWith("polyhaven:",StringComparison.Ordinal)?"Powered by Poly Haven":""};
            }
            return null;
        }
        private static void EnsureSafePath(string path)
        {
            string root=Path.GetFullPath("Assets"),full=Path.GetFullPath(path);
            if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&full!=root)throw new IOException("Material writes must remain inside project Assets.");
            for(string current=full;current!=null;current=Path.GetDirectoryName(current))
            {
                if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Symlinked material paths are not supported.");
                if(File.Exists(current+".meta")&&(File.GetAttributes(current+".meta")&FileAttributes.ReparsePoint)!=0)throw new IOException("Symlinked material metadata is not supported.");
                if(current==root)break;
            }
        }
        private static void RemoveCreated(IEnumerable<string> files)
        {
            foreach(string path in files.Reverse())
            {
                EnsureSafePath(path);
                if(Directory.Exists(path))
                {
                    // A parent created by this operation may now contain another successful import.
                    if(!Directory.EnumerateFileSystemEntries(path).Any())AssetDatabase.DeleteAsset(path);
                }
                else if(File.Exists(path)&&!AssetDatabase.DeleteAsset(path))
                {
                    File.Delete(path);if(File.Exists(path+".meta"))File.Delete(path+".meta");
                }
            }
        }
        private sealed class MaterialAssets:IDisposable
        {
            private readonly string folder;private readonly List<string> files=new List<string>();private readonly List<string> parents=new List<string>();private bool complete;
            internal MaterialAssets(string id)
            {
                EnsureSafePath(Root);string parent="Assets";
                foreach(string part in new[]{"WorldWeaverGenerated","Materials"})
                {
                    string next=parent+"/"+part;
                    if(!AssetDatabase.IsValidFolder(next))
                    {
                        if(string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent,part))){RemoveCreated(parents);throw new IOException("Cannot create material folder.");}
                        parents.Add(next);
                    }
                    parent=next;
                }
                string name=DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+"_"+Guid.NewGuid().ToString("N").Substring(0,8);
                folder=Root+"/"+name;EnsureSafePath(folder);if(string.IsNullOrEmpty(AssetDatabase.CreateFolder(Root,name))){RemoveCreated(parents);throw new IOException("Cannot create material import folder.");}
            }
            internal string Write(string name,byte[] bytes)
            {
                string path=folder+"/"+name;EnsureSafePath(path);if(File.Exists(path))throw new IOException("Material import will never overwrite assets.");
                files.Add(path);File.WriteAllBytes(path,bytes);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);return path;
            }
            internal string Create(Object asset,string name)
            {
                string path=folder+"/"+name;EnsureSafePath(path);files.Add(path);AssetDatabase.CreateAsset(asset,path);
                if(!AssetDatabase.LoadMainAssetAtPath(path))throw new IOException("Material asset read-back failed.");return path;
            }
            internal MaterialImportResult Finish(string id,string materialPath,string layerPath,string credit,JArray sourceMaps)
            {
                SaveCreatedAssets();var hashes=new JObject();
                foreach(string path in files)
                {
                    hashes[Path.GetFileName(path)]=PolyHavenMaterialClient.Hash(File.ReadAllBytes(path));
                    if(File.Exists(path+".meta"))hashes[Path.GetFileName(path)+".meta"]=PolyHavenMaterialClient.Hash(File.ReadAllBytes(path+".meta"));
                }
                var receipt=new JObject{["format"]=1,["material_id"]=id,["license"]="CC0-1.0",["shader"]=LitShader().name,["credit"]=credit,["imported_utc"]=DateTime.UtcNow.ToString("o"),["file_sha256"]=hashes,["source_maps"]=sourceMaps};
                Write("material-source.json",System.Text.Encoding.UTF8.GetBytes(receipt.ToString()));
                return new MaterialImportResult{MaterialId=id,RequestedMaterialId=id,MaterialPath=materialPath,TerrainLayerPath=layerPath,CreatedAssets=CompleteFiles()};
            }
            private void SaveCreatedAssets()
            {
                foreach(string path in files){var asset=AssetDatabase.LoadMainAssetAtPath(path);if(asset)AssetDatabase.SaveAssetIfDirty(asset);}
            }
            internal List<string> CompleteFiles(){SaveCreatedAssets();complete=true;return parents.Concat(new[]{folder}).Concat(files).ToList();}
            public void Dispose(){if(!complete)RemoveCreated(parents.Concat(new[]{folder}).Concat(files));}
        }
    }
}
