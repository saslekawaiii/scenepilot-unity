using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using WorldWeaver.Editor.Assistant;
namespace WorldWeaver.Tests
{
    public sealed class AgentSourcePatchTests
    {
        const string Folder="Assets/WorldWeaverSourcePatchTests",Resume="WorldWeaver.SourcePatchTest";
        string checkpoint;
        [SetUp] public void Setup()
        {
            if(TestContext.CurrentContext.Test.Name==nameof(ZZReviewedTwoFilePatchCompilesPreservesBytesAndCheckpointRestores)&&SessionState.GetBool(Resume,false))return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            bool compiled=Type.GetType("WWSourcePatchProbeA, Assembly-CSharp")!=null;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);AssetDatabase.DeleteAsset(Folder);SessionState.EraseBool(Resume);
            if(checkpoint!=null){string path=Path.Combine("UserSettings/WorldWeaverCheckpoints",checkpoint);if(Directory.Exists(path))Directory.Delete(path,true);}
            if(compiled)yield return new RecompileScripts(false);else yield return null;
        }
        static EditorCommand Patch(string path,string find,string replace)=>new EditorCommand {tool="patch_script",args=new JObject { ["path"]=path,["expected_sha256"]=AgentSourcePatches.SourceHash(path),["find"]=find,["replace"]=replace }};
        [Test] public void AmbiguousOverlappingMatchIsRejected()
        {
            string path=Folder+"/Ambiguous.cs";File.WriteAllText(path,"// aaa\n");var command=Patch(path,"aa","b");Assert.Throws<ArgumentException>(()=>AgentSourcePatches.Validate(command));Assert.AreEqual("// aaa\n",File.ReadAllText(path));
        }
        [Test] public void SecondFileConflictRejectsEntireBatchBeforeFirstWrite()
        {
            string a=Folder+"/A.cs",b=Folder+"/B.cs";File.WriteAllText(a,"// first before\n");File.WriteAllText(b,"// second before\n");var first=Patch(a,"first before","first after");var second=Patch(b,"second before","second after");File.WriteAllText(b,"// second manual\n");
            Assert.Throws<InvalidOperationException>(()=>AgentSourcePatches.Apply(new[]{first,second},Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"),"UserSettings/PatchTestHistory"));Assert.AreEqual("// first before\n",File.ReadAllText(a));Assert.AreEqual("// second manual\n",File.ReadAllText(b));
        }
        [Test] public void NonUtf8AndOversizedUtf8ReplacementFailBeforeWriting()
        {
            string path=Folder+"/Utf.cs";File.WriteAllText(path,"// utf16 before",Encoding.Unicode);var nonUtf=Patch(path,"before","after");Assert.Throws<ArgumentException>(()=>AgentSourcePatches.Validate(nonUtf));
            File.WriteAllText(path,"// marker"+new string('a',85000),new UTF8Encoding(false));var large=Patch(path,"marker",new string('я',18000));byte[] before=File.ReadAllBytes(path);
            Assert.Throws<ArgumentException>(()=>AgentSourcePatches.Apply(new[]{large},Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"),"UserSettings/PatchTestHistory"));CollectionAssert.AreEqual(before,File.ReadAllBytes(path));
        }
        [UnityTest] public IEnumerator ZZReviewedTwoFilePatchCompilesPreservesBytesAndCheckpointRestores()
        {
            const string a=Folder+"/WWSourcePatchProbeA.cs",b=Folder+"/WWSourcePatchProbeB.cs";
            const string sourceA="public static class WWSourcePatchProbeA { public const int Value=1; }\r\n",sourceB="public static class WWSourcePatchProbeB { public const int Value=2; }\r\n";
            SessionState.SetBool(Resume,true);File.WriteAllText(a,sourceA,new UTF8Encoding(true));File.WriteAllText(b,sourceB,new UTF8Encoding(false));new GameObject("source patch scene");
            yield return new RecompileScripts(false);
            byte[] beforeA=File.ReadAllBytes(a),beforeB=File.ReadAllBytes(b);string sessionId=Guid.NewGuid().ToString("N"),messageId=Guid.NewGuid().ToString("N"),history="UserSettings/WorldWeaverSourcePatchHistory";
            var session=new ChatSession {Id=sessionId};session.Messages.Add(new AssistantMessage {Id=messageId});new ChatHistoryStore(history).Save(session);
            string id=AgentCheckpoints.Begin(sessionId,messageId);SessionState.SetString(Resume+"Checkpoint",id);
            var result=AgentSourcePatches.Apply(new[]{Patch(a,"Value=1;","Value=11;"),Patch(b,"Value=2;","Value=22;")},sessionId,messageId,history);Assert.AreEqual(2,result.Count);AgentCheckpoints.Seal(id,true);
            yield return new RecompileScripts(false);
            checkpoint=id=SessionState.GetString(Resume+"Checkpoint","");
            Assert.AreEqual(11,Type.GetType("WWSourcePatchProbeA, Assembly-CSharp").GetField("Value").GetRawConstantValue());Assert.AreEqual(22,Type.GetType("WWSourcePatchProbeB, Assembly-CSharp").GetField("Value").GetRawConstantValue());
            var compiler=JObject.Parse(AgentSourcePatches.CompilationContext());Assert.IsTrue((bool)compiler["Confirmed"],compiler.ToString());Assert.IsTrue((bool)compiler["Success"],compiler.ToString());
            var encoding=new UTF8Encoding(true);CollectionAssert.AreEqual(encoding.GetPreamble().Concat(encoding.GetBytes(sourceA.Replace("Value=1;","Value=11;"))).ToArray(),File.ReadAllBytes(a));CollectionAssert.AreEqual(new UTF8Encoding(false).GetBytes(sourceB.Replace("Value=2;","Value=22;")),File.ReadAllBytes(b));
            Assert.IsTrue(AgentCheckpoints.CanRestore(id),AgentCheckpoints.Inspect(id).ToString());Assert.IsTrue((bool)AgentCheckpoints.Restore(id)["success"]);
            yield return new RecompileScripts(false);
            checkpoint=SessionState.GetString(Resume+"Checkpoint","");CollectionAssert.AreEqual(new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(sourceA)).ToArray(),File.ReadAllBytes(a));CollectionAssert.AreEqual(new UTF8Encoding(false).GetBytes(sourceB),File.ReadAllBytes(b));Assert.AreEqual(1,Type.GetType("WWSourcePatchProbeA, Assembly-CSharp").GetField("Value").GetRawConstantValue());Assert.AreEqual(2,Type.GetType("WWSourcePatchProbeB, Assembly-CSharp").GetField("Value").GetRawConstantValue());Assert.NotNull(GameObject.Find("source patch scene"));SessionState.EraseBool(Resume);
        }
    }
}
