using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Persistent per-request snapshots. Only the sealed write set is restored; later edits are conflicts.</summary>
    public static class AgentCheckpoints
    {
        private const long ByteLimit=300L*1024*1024;
        private const int FileLimit=20000;
        private static readonly Dictionary<string,long> storedBytes=new Dictionary<string,long>();
        private static string Root=>Path.GetFullPath("UserSettings/WorldWeaverCheckpoints");
        [Serializable] private sealed class FileState { public string Path,Hash; public long Bytes; }
        [Serializable] private sealed class SceneState { public string Path,Name,Hash; public bool Dirty,Active; }
        [Serializable] private sealed class Journal
        {
            public int Version=1;public string Id,SessionId,MessageId;public bool Sealed,Restored;
            public List<FileState> Before=new List<FileState>(),After=new List<FileState>();
            public List<SceneState> ScenesBefore=new List<SceneState>(),ScenesAfter=new List<SceneState>();
            public List<string> WriteSet=new List<string>(),DirectoriesBefore=new List<string>(),DirectoriesAfter=new List<string>();public long BackupBytes;
        }
        [Serializable] private sealed class TestSceneScope
        {
            public int Version=1;public string Id;public bool Staged,Restored;
            public List<SceneState> Original=new List<SceneState>(),Expected=new List<SceneState>();
            public List<string> TemporaryPaths=new List<string>();public List<FileState> OriginalSceneFiles=new List<FileState>();
        }
        private static void Ready(bool allowCompiling=false)
        {
            if((!allowCompiling&&(EditorApplication.isCompiling||EditorApplication.isUpdating))||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Дождитесь завершения импорта/компиляции и выйдите из Play Mode.");
            if(UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage()!=null)throw new InvalidOperationException("Закройте Prefab Mode перед созданием контрольной точки.");
            if(SceneManager.sceneCount<1||SceneManager.sceneCount>8)throw new InvalidOperationException("Checkpoint supports one to eight loaded scenes.");
            if(SceneManager.sceneCount>1&&Enumerable.Range(0,SceneManager.sceneCount).Any(i=>string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)))throw new InvalidOperationException("Save additive scenes before checkpoint; one untitled scene is supported.");
            if(LightmapSettings.lightmaps.Any(m=>m!=null&&(m.lightmapColor||m.lightmapDir||m.shadowMask))||(LightmapSettings.lightProbes&&LightmapSettings.lightProbes.bakedProbes.Length>0))throw new InvalidOperationException("Checkpoint of an open scene with baked lighting is not supported yet. No changes were made.");
        }
        public static string Begin(string sessionId,string messageId)
        {
            Ready();if(!Guid.TryParseExact(sessionId,"N",out _)||!Guid.TryParseExact(messageId,"N",out _))throw new ArgumentException("Checkpoint requires valid session/message identities.");
            var journal=new Journal {Id=Guid.NewGuid().ToString("N"),SessionId=sessionId,MessageId=messageId};
            string folder=Folder(journal.Id);Directory.CreateDirectory(folder);
            try
            {
                journal.Before=Scan();journal.DirectoriesBefore=Directories();
                long bytes=journal.Before.Sum(f=>f.Bytes);if(bytes>ByteLimit)throw new InvalidOperationException("Проект превышает лимит checkpoint 300 МиБ; уменьшите рабочий проект до запуска задачи.");
                foreach(var file in journal.Before)Store(journal.Id,File.ReadAllBytes(file.Path),file.Hash);
                journal.ScenesBefore=Scenes(journal.Id,true);journal.BackupBytes=bytes+journal.ScenesBefore.Sum(s=>new FileInfo(Blob(journal.Id,s.Hash)).Length);
                if(journal.BackupBytes>ByteLimit)throw new InvalidOperationException("Checkpoint exceeds 300 MiB including dirty scenes.");
                Save(journal);return journal.Id;
            }
            catch { if(Directory.Exists(folder))Directory.Delete(folder,true);throw; }
        }
        /// <summary>Isolate TestRunner from unsaved/dirty working scenes without saving over user assets.</summary>
        internal static string BeginTestSceneScope()
        {
            Ready();var scope=new TestSceneScope {Id=Guid.NewGuid().ToString("N")};Directory.CreateDirectory(Folder(scope.Id));
            scope.Original=Scenes(scope.Id,true);
            foreach(var scene in scope.Original.Where(s=>!string.IsNullOrEmpty(s.Path)))
                foreach(string path in new[]{scene.Path,scene.Path+".meta"})if(File.Exists(path))scope.OriginalSceneFiles.Add(new FileState{Path=path,Hash=Hash(File.ReadAllBytes(path))});
            // Persist recovery bytes before assigning any temporary scene path.
            SaveTestSceneScope(scope);
            try
            {
                var active=SceneManager.GetActiveScene();
                for(int i=0;i<SceneManager.sceneCount;i++)
                {
                    var scene=SceneManager.GetSceneAt(i);string temporary="Assets/__WorldWeaverCheckpoint-test-"+scope.Id+"-"+i+".unity";
                    scope.TemporaryPaths.Add(temporary);SaveTestSceneScope(scope);
                    if(!EditorSceneManager.SaveScene(scene,temporary,false))throw new IOException("Cannot stage a scene for TestRunner.");
                }
                if(active.IsValid())SceneManager.SetActiveScene(active);
                scope.Expected=Scenes(scope.Id,true);scope.Staged=true;SaveTestSceneScope(scope);return scope.Id;
            }
            catch
            {
                RestoreSceneIdentity(new Journal{Id=scope.Id,ScenesBefore=scope.Original});
                foreach(string path in scope.TemporaryPaths)DeleteTemporary(path);
                throw;
            }
        }
        internal static void EndTestSceneScope(string id)
        {
            Ready();var scope=ReadTestSceneScope(id);if(scope.Restored)return;if(!scope.Staged)throw new InvalidOperationException("TestRunner scene staging was interrupted; manual recovery is required.");
            foreach(var file in scope.OriginalSceneFiles)if(!File.Exists(file.Path)||Hash(File.ReadAllBytes(file.Path))!=file.Hash)throw new InvalidOperationException("TestRunner changed an original scene asset; preserving its current bytes: "+file.Path);
            var actual=Scenes(id,false);
            if(actual.Count!=scope.Expected.Count||actual.Where((s,i)=>s.Path!=scope.Expected[i].Path||s.Hash!=scope.Expected[i].Hash).Any())throw new InvalidOperationException("Working scenes changed outside TestRunner cleanup; preserving them and the stored original snapshot.");
            foreach(var scene in scope.Original)CheckBlob(id,scene.Hash);
            RestoreSceneIdentity(new Journal{Id=id,ScenesBefore=scope.Original});
            var restored=Scenes(id,false);
            if(restored.Count!=scope.Original.Count||restored.Where((s,i)=>s.Path!=scope.Original[i].Path||s.Hash!=scope.Original[i].Hash).Any())throw new InvalidOperationException("Restored scene state differs from the exact pre-test snapshot; no completion is claimed.");
            scope.Restored=true;SaveTestSceneScope(scope);foreach(string path in scope.TemporaryPaths)DeleteTemporary(path);
        }
        private static void SaveTestSceneScope(TestSceneScope scope)
        {
            string path=Path.Combine(Folder(scope.Id),"test-scenes.json");File.WriteAllText(path,JsonConvert.SerializeObject(scope,Formatting.Indented));
        }
        private static TestSceneScope ReadTestSceneScope(string id)
        {
            var scope=JsonConvert.DeserializeObject<TestSceneScope>(File.ReadAllText(Path.Combine(Folder(id),"test-scenes.json")),new JsonSerializerSettings{TypeNameHandling=TypeNameHandling.None,MaxDepth=16});
            if(scope==null||scope.Version!=1||scope.Id!=id||scope.Original==null||scope.Expected==null||scope.TemporaryPaths==null||scope.OriginalSceneFiles==null||scope.Original.Count>8||scope.Expected.Count>8||scope.TemporaryPaths.Count>8)throw new InvalidDataException("Invalid TestRunner scene snapshot.");
            foreach(string path in scope.TemporaryPaths)if(!path.StartsWith("Assets/__WorldWeaverCheckpoint-test-"+id+"-",StringComparison.Ordinal)||!path.EndsWith(".unity",StringComparison.Ordinal)||path.Contains("..")||path.Contains('\\'))throw new InvalidDataException("Invalid temporary test scene path.");
            foreach(string path in scope.Original.Select(s=>s.Path).Concat(scope.OriginalSceneFiles.Select(f=>f.Path)))if(!string.IsNullOrEmpty(path)&&(!path.StartsWith("Assets/",StringComparison.Ordinal)||path.Contains("..")||path.Contains('\\')||!(path.EndsWith(".unity",StringComparison.Ordinal)||path.EndsWith(".unity.meta",StringComparison.Ordinal))))throw new InvalidDataException("Invalid original test scene path.");
            return scope;
        }
        private static void RestoreSceneIdentity(Journal journal)
        {
            GeneratedSceneObject.SuspendIdentityRegistration=true;
            try { RestoreScenes(journal); }
            finally { GeneratedSceneObject.SuspendIdentityRegistration=false;foreach(var owned in Object.FindObjectsByType<GeneratedSceneObject>(FindObjectsInactive.Include,FindObjectsSortMode.None))owned.EnsureUniqueIdentity(); }
        }
        public static void Seal(string id) => Seal(id,false);
        public static void Seal(string id,bool allowCompiling)
        {
            Ready(allowCompiling);
            // Complete synchronous asset import now so newly generated .meta files belong to this request.
            // Domain reload cannot interleave this synchronous method on the Editor main thread.
            if(allowCompiling)AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var journal=Read(id);if(journal.Restored)throw new InvalidOperationException("Checkpoint was already restored.");
            var after=Scan();var before=journal.Before.ToDictionary(f=>f.Path,StringComparer.Ordinal);var current=after.ToDictionary(f=>f.Path,StringComparer.Ordinal);
            journal.WriteSet=before.Keys.Union(current.Keys).Where(p=>!before.TryGetValue(p,out var old)||!current.TryGetValue(p,out var next)||old.Hash!=next.Hash).OrderBy(p=>p,StringComparer.Ordinal).ToList();
            foreach(string path in journal.WriteSet)if(current.TryGetValue(path,out var file))Store(id,File.ReadAllBytes(path),file.Hash);
            journal.After=after;journal.DirectoriesAfter=Directories();journal.ScenesAfter=Scenes(id,true);journal.BackupBytes=storedBytes.TryGetValue(id,out var total)?total:journal.BackupBytes;journal.Sealed=true;Save(journal);
        }
        public static bool CanRestore(string id)
        {
            try { Ready();var journal=Read(id);return journal.Sealed&&!journal.Restored&&Conflicts(journal).Count==0; }
            catch { return false; }
        }
        public static bool CanExtend(string id)
        {
            try
            {
                Ready();var journal=Read(id);if(!journal.Sealed||journal.Restored)return false;
                var actual=Scan();var expected=journal.After.ToDictionary(f=>f.Path,StringComparer.Ordinal);
                return actual.Count==expected.Count&&actual.All(f=>expected.TryGetValue(f.Path,out var known)&&known.Hash==f.Hash)&&Conflicts(journal).Count==0;
            }
            catch { return false; }
        }
        public static JObject Inspect(string id)
        {
            var journal=Read(id);var conflicts=journal.Sealed&&!journal.Restored?Conflicts(journal):new List<string>();
            return new JObject { ["id"]=id,["sealed"]=journal.Sealed,["restored"]=journal.Restored,["backupBytes"]=journal.BackupBytes,["filesChanged"]=journal.WriteSet.Count,["writeSet"]=new JArray(journal.WriteSet),["conflicts"]=new JArray(conflicts),["canRestore"]=journal.Sealed&&!journal.Restored&&conflicts.Count==0,["scope"]="Assets, ProjectSettings, package manifests, loaded scene working state; excludes Library, model files and baked lighting." };
        }
        public static JObject Restore(string id)
        {
            Ready();var journal=Read(id);if(!journal.Sealed||journal.Restored)throw new InvalidOperationException("Checkpoint is not available for restore.");
            var conflicts=Conflicts(journal);if(conflicts.Count>0)return new JObject { ["success"]=false,["conflicts"]=new JArray(conflicts),["message"]="После задачи появились ручные изменения. Ничего не восстановлено." };
            var before=journal.Before.ToDictionary(f=>f.Path,StringComparer.Ordinal);
            // Verify all backup bytes before touching the project.
            foreach(var path in journal.WriteSet)if(before.TryGetValue(path,out var file))CheckBlob(id,file.Hash);
            foreach(var scene in journal.ScenesBefore)CheckBlob(id,scene.Hash);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach(var path in journal.WriteSet)
                {
                    if(before.TryGetValue(path,out var file)){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,File.ReadAllBytes(Blob(id,file.Hash)));}
                    else if(File.Exists(path))File.Delete(path);
                }
                foreach(string directory in journal.DirectoriesAfter.Except(journal.DirectoriesBefore).OrderByDescending(p=>p.Length))if(Directory.Exists(directory)&&!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            RestoreSceneIdentity(journal);
            journal.Restored=true;Save(journal);
            return new JObject { ["success"]=true,["filesRestored"]=journal.WriteSet.Count,["scenesRestored"]=journal.ScenesBefore.Count,["message"]="Восстановлены изменения этого запроса. Другие файлы не затронуты." };
        }
        private static List<string> Conflicts(Journal journal)
        {
            var expected=journal.After.ToDictionary(f=>f.Path,StringComparer.Ordinal);var conflicts=new List<string>();
            foreach(var path in journal.WriteSet)
            {
                expected.TryGetValue(path,out var file);string actual=File.Exists(path)?Hash(File.ReadAllBytes(path)):null;if(actual!=file?.Hash)conflicts.Add(path);
            }
            foreach(string directory in journal.DirectoriesAfter.Except(journal.DirectoriesBefore))if(Directory.Exists(directory)&&Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Any(p=>!expected.ContainsKey(p.Replace('\\','/'))))conflicts.Add("New folder contains later files: "+directory);
            var scenes=Scenes(journal.Id,false);
            if(scenes.Count!=journal.ScenesAfter.Count)conflicts.Add("Open scene setup changed");
            else for(int i=0;i<scenes.Count;i++)if(scenes[i].Path!=journal.ScenesAfter[i].Path||scenes[i].Hash!=journal.ScenesAfter[i].Hash)conflicts.Add("Scene working state changed: "+scenes[i].Name);
            return conflicts;
        }
        private static List<string> Directories()
        {
            var result=new List<string>();foreach(string root in new[]{"Assets","ProjectSettings"})if(Directory.Exists(root))result.AddRange(Directory.GetDirectories(root,"*",SearchOption.AllDirectories).Select(p=>p.Replace('\\','/')));return result;
        }
        private static List<FileState> Scan()
        {
            var files=new List<string>();
            foreach(string root in new[]{"Assets","ProjectSettings"})if(Directory.Exists(root))Walk(root,files);
            foreach(string file in new[]{"Packages/manifest.json","Packages/packages-lock.json"})if(File.Exists(file))files.Add(file);
            if(files.Count>FileLimit)throw new InvalidOperationException("Checkpoint file limit exceeded (20000).");
            long total=0;var result=new List<FileState>();
            foreach(string path in files.OrderBy(p=>p,StringComparer.Ordinal))
            {
                var info=new FileInfo(path);total+=info.Length;if(total>ByteLimit)throw new InvalidOperationException("Checkpoint byte limit exceeded (300 MiB).");
                result.Add(new FileState {Path=path.Replace('\\','/'),Hash=Hash(File.ReadAllBytes(path)),Bytes=info.Length});
            }
            return result;
        }
        private static void Walk(string directory,List<string> files)
        {
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Symlinked checkpoint roots are not supported: "+directory);
            foreach(string file in Directory.GetFiles(directory))
            {
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Symlinked checkpoint files are not supported: "+file);
                if(Path.GetFileName(file).StartsWith("__WorldWeaverCheckpoint-",StringComparison.Ordinal))continue;
                files.Add(file);if(files.Count>FileLimit)throw new InvalidOperationException("Checkpoint file limit exceeded.");
            }
            foreach(string child in Directory.GetDirectories(directory))Walk(child,files);
        }
        private static List<SceneState> Scenes(string id,bool store)
        {
            var result=new List<SceneState>();var active=SceneManager.GetActiveScene();
            try
            {
                for(int i=0;i<SceneManager.sceneCount;i++)
                {
                    var scene=SceneManager.GetSceneAt(i);if(!scene.isLoaded)throw new InvalidOperationException("Unloaded scenes are not supported by checkpoint.");
                    string temporary="Assets/__WorldWeaverCheckpoint-"+Guid.NewGuid().ToString("N")+".unity";
                    try
                    {
                        if(!EditorSceneManager.SaveScene(scene,temporary,true))throw new IOException("Cannot snapshot scene working state.");
                        byte[] bytes=File.ReadAllBytes(temporary);string hash=Hash(bytes);if(store)Store(id,bytes,hash);
                        result.Add(new SceneState {Path=scene.path,Name=scene.name,Dirty=scene.isDirty,Active=scene==active,Hash=hash});
                    }
                    finally { DeleteTemporary(temporary); }
                }
            }
            finally { if(active.IsValid()&&active.isLoaded)SceneManager.SetActiveScene(active); }
            return result;
        }
        private static void RestoreScenes(Journal journal)
        {
            var previous=new List<Scene>();for(int i=0;i<SceneManager.sceneCount;i++)previous.Add(SceneManager.GetSceneAt(i));
            var temporaryWorking=new List<string>();
            foreach(var scene in previous)if(string.IsNullOrEmpty(scene.path))
            {
                string path="Assets/__WorldWeaverCheckpoint-"+Guid.NewGuid().ToString("N")+".unity";
                if(!EditorSceneManager.SaveScene(scene,path))throw new IOException("Could not stage the current untitled scene for restore.");temporaryWorking.Add(path);
            }
            var restored=new List<Scene>();Scene active=default;
            foreach(var state in journal.ScenesBefore)
            {
                string temporary="Assets/__WorldWeaverCheckpoint-"+Guid.NewGuid().ToString("N")+".unity";
                try
                {
                    File.WriteAllBytes(temporary,File.ReadAllBytes(Blob(journal.Id,state.Hash)));AssetDatabase.ImportAsset(temporary,ImportAssetOptions.ForceSynchronousImport);
                    var copy=EditorSceneManager.OpenScene(temporary,OpenSceneMode.Additive);SceneManager.SetActiveScene(copy);
                    if(!state.Dirty&&!string.IsNullOrEmpty(state.Path))
                    {
                        EditorSceneManager.CloseScene(copy,true);
                        var original=previous.FirstOrDefault(s=>s.path==state.Path);
                        if(original.IsValid()) { EditorSceneManager.CloseScene(original,true);previous.Remove(original); }
                        var clean=EditorSceneManager.OpenScene(state.Path,OpenSceneMode.Additive);restored.Add(clean);if(state.Active)active=clean;
                    }
                    else
                    {
                        // A new untitled target preserves the unsaved path; saved targets retain their original disk path.
                        var environment=new EnvironmentState();
                        Scene target;
                        var original=previous.FirstOrDefault(s=>!string.IsNullOrEmpty(state.Path)&&s.path==state.Path);
                        if(original.IsValid()) { target=original;previous.Remove(original); }
                        else if(!string.IsNullOrEmpty(state.Path))target=EditorSceneManager.OpenScene(state.Path,OpenSceneMode.Additive);
                        else target=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                        foreach(var old in target.GetRootGameObjects())Object.DestroyImmediate(old);
                        foreach(var go in copy.GetRootGameObjects())SceneManager.MoveGameObjectToScene(go,target);
                        SceneManager.SetActiveScene(target);environment.Apply();EditorSceneManager.MarkSceneDirty(target);EditorSceneManager.CloseScene(copy,true);
                        restored.Add(target);if(state.Active)active=target;
                    }
                }
                finally { DeleteTemporary(temporary); }
            }
            foreach(var scene in previous)if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
            if(active.IsValid())SceneManager.SetActiveScene(active);
            foreach(string path in temporaryWorking)DeleteTemporary(path);
        }
        /// <summary>Global RenderSettings are scene-owned; copy their explicit public state for untitled/dirty restoration.</summary>
        private sealed class EnvironmentState
        {
            private readonly Material sky=RenderSettings.skybox;private readonly Light sun=RenderSettings.sun;
            private readonly bool fog=RenderSettings.fog;private readonly Color fogColor=RenderSettings.fogColor,ambientSky=RenderSettings.ambientSkyColor,ambientEquator=RenderSettings.ambientEquatorColor,ambientGround=RenderSettings.ambientGroundColor,ambientLight=RenderSettings.ambientLight,subtractive=RenderSettings.subtractiveShadowColor;
            private readonly FogMode fogMode=RenderSettings.fogMode;private readonly float fogDensity=RenderSettings.fogDensity,fogStart=RenderSettings.fogStartDistance,fogEnd=RenderSettings.fogEndDistance,ambientIntensity=RenderSettings.ambientIntensity,reflectionIntensity=RenderSettings.reflectionIntensity;
            private readonly UnityEngine.Rendering.AmbientMode ambientMode=RenderSettings.ambientMode;private readonly UnityEngine.Rendering.DefaultReflectionMode reflectionMode=RenderSettings.defaultReflectionMode;
            private readonly int reflectionResolution=RenderSettings.defaultReflectionResolution,reflectionBounces=RenderSettings.reflectionBounces;
            private readonly Texture reflection=RenderSettings.customReflectionTexture;private readonly UnityEngine.Rendering.SphericalHarmonicsL2 probe=RenderSettings.ambientProbe;
            private readonly LightingSettings lighting;
            internal EnvironmentState() { Lightmapping.TryGetLightingSettings(out lighting); }
            internal void Apply()
            {
                RenderSettings.skybox=sky;RenderSettings.sun=sun;RenderSettings.fog=fog;RenderSettings.fogColor=fogColor;RenderSettings.fogMode=fogMode;RenderSettings.fogDensity=fogDensity;RenderSettings.fogStartDistance=fogStart;RenderSettings.fogEndDistance=fogEnd;
                RenderSettings.ambientMode=ambientMode;RenderSettings.ambientSkyColor=ambientSky;RenderSettings.ambientEquatorColor=ambientEquator;RenderSettings.ambientGroundColor=ambientGround;RenderSettings.ambientLight=ambientLight;RenderSettings.ambientIntensity=ambientIntensity;RenderSettings.subtractiveShadowColor=subtractive;RenderSettings.ambientProbe=probe;
                RenderSettings.defaultReflectionMode=reflectionMode;RenderSettings.defaultReflectionResolution=reflectionResolution;RenderSettings.reflectionBounces=reflectionBounces;RenderSettings.reflectionIntensity=reflectionIntensity;RenderSettings.customReflectionTexture=reflection;Lightmapping.lightingSettings=lighting;
            }
        }
        private static void DeleteTemporary(string path) { if(!AssetDatabase.DeleteAsset(path)) { if(File.Exists(path))File.Delete(path);if(File.Exists(path+".meta"))File.Delete(path+".meta"); } }
        private static string Hash(byte[] bytes) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        private static string Folder(string id) { if(!Guid.TryParseExact(id,"N",out _))throw new ArgumentException("Invalid checkpoint identity.");return Path.Combine(Root,id); }
        private static string Blob(string id,string hash) { if(hash==null||hash.Length!=64||hash.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("Invalid checkpoint blob.");return Path.Combine(Folder(id),"content",hash); }
        private static void Store(string id,byte[] bytes,string hash)
        {
            string path=Blob(id,hash),directory=Path.GetDirectoryName(path);Directory.CreateDirectory(directory);if(File.Exists(path))return;
            if(!storedBytes.TryGetValue(id,out var used))used=Directory.GetFiles(directory).Sum(p=>new FileInfo(p).Length);
            if(used+bytes.Length>ByteLimit)throw new InvalidOperationException("Checkpoint backup exceeds the 300 MiB limit.");
            File.WriteAllBytes(path,bytes);storedBytes[id]=used+bytes.Length;
        }
        private static void CheckBlob(string id,string hash) { string path=Blob(id,hash);if(!File.Exists(path)||Hash(File.ReadAllBytes(path))!=hash)throw new InvalidDataException("Checkpoint backup is missing or corrupted."); }
        private static Journal Read(string id)
        {
            var j=JsonConvert.DeserializeObject<Journal>(File.ReadAllText(Path.Combine(Folder(id),"journal.json")),new JsonSerializerSettings {TypeNameHandling=TypeNameHandling.None,MaxDepth=16,MissingMemberHandling=MissingMemberHandling.Error});
            if(j==null||j.Version!=1||j.Id!=id||j.Before==null||j.After==null||j.WriteSet==null||j.ScenesBefore==null||j.ScenesAfter==null||j.DirectoriesBefore==null||j.DirectoriesAfter==null||j.Before.Count>FileLimit||j.After.Count>FileLimit||j.WriteSet.Count>FileLimit||j.ScenesBefore.Count>8||j.ScenesAfter.Count>8)throw new InvalidDataException("Invalid checkpoint journal.");
            foreach(string scene in j.ScenesBefore.Concat(j.ScenesAfter).Select(s=>s.Path))if(!string.IsNullOrEmpty(scene)&&(!scene.StartsWith("Assets/",StringComparison.Ordinal)||scene.Contains("..")||scene.Contains('\\')||!scene.EndsWith(".unity",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Invalid scene checkpoint path.");
            foreach(string p in j.Before.Concat(j.After).Select(f=>f.Path).Concat(j.WriteSet).Concat(j.DirectoriesBefore).Concat(j.DirectoriesAfter))if(string.IsNullOrEmpty(p)||p.Contains("..")||p.Contains('\\')||!(p.StartsWith("Assets/",StringComparison.Ordinal)||p.StartsWith("ProjectSettings/",StringComparison.Ordinal)||p=="Packages/manifest.json"||p=="Packages/packages-lock.json"))throw new InvalidDataException("Checkpoint path escaped project scope.");
            return j;
        }
        private static void Save(Journal journal)
        {
            string path=Path.Combine(Folder(journal.Id),"journal.json"),temporary=path+".tmp";File.WriteAllText(temporary,JsonConvert.SerializeObject(journal,Formatting.Indented));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
        }
    }
}
