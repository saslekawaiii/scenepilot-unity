using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    public sealed class DiagnosticSourceExcerptTests
    {
        SceneSetup[] previousScenes;bool fixtureActive;
        [SetUp] public void Setup()
        {
            fixtureActive=false;previousScenes=null;var existing=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            if(!Application.isBatchMode&&existing.Any(s=>s.isDirty||string.IsNullOrEmpty(s.path)))Assert.Ignore("Use an isolated consumer or save open scenes before compiler tests.");
            if(existing.All(s=>!string.IsNullOrEmpty(s.path)&&!s.isDirty))previousScenes=EditorSceneManager.GetSceneManagerSetup();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);fixtureActive=true;
        }
        [TearDown] public void Cleanup(){if(!fixtureActive)return;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);if(previousScenes!=null&&previousScenes.Length>0)EditorSceneManager.RestoreSceneManagerSetup(previousScenes);fixtureActive=false;}
        static JObject Build(string source,params string[] diagnostics)
        {
            var type=typeof(SceneToolExecutor).Assembly.GetType("WorldWeaver.Editor.Assistant.DiagnosticSourceExcerpts",true);
            return (JObject)type.GetMethod("Build",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{source,diagnostics});
        }
        static string ProjectSignature()=>(string)typeof(AgentCheckpoints).GetMethod("ProjectContentSignature",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        static EditorCommandResult Fail(string body)
        {
            var session=new ChatSession();session.Settings.Mode=AssistantMode.Execute;session.Settings.AllowEditorCode=true;
            var batch=SceneToolExecutor.Execute(new EditorPlan{actions=new List<EditorCommand>{new EditorCommand{tool="execute_editor_code",args=new JObject{["code"]=body}}}},executionContext:new AgentExecutionContext{Session=session});
            Assert.That(batch.Success,Is.False);return batch.Results.Single(r=>r.Tool=="execute_editor_code");
        }
        [TestCase("")]
        [TestCase("using Object = System.Object;\nusing StaticAlias = System.Math;\n")]
        public void ActualRoslynFailurePointsToOffendingGeneratedSourceDespiteWrapperAliases(string directives)
        {
            var original=new GameObject("Original preserved object");original.transform.position=new Vector3(2,3,4);string projectBefore=ProjectSignature();var roots=SceneManager.GetActiveScene().GetRootGameObjects().Select(g=>g.GetInstanceID()).OrderBy(i=>i).ToArray();
            string body=directives+"new GameObject(\"MustNeverExecute\");\nint marker = ??0;\nreturn marker;";var failure=Fail(body);var summary=JObject.Parse(failure.Summary);var excerpts=(JObject)summary["diagnosticSourceExcerpts"];
            Assert.That((bool)summary["compiled"],Is.False);Assert.That((string)excerpts["sourceHash"],Is.EqualTo((string)summary["sourceHash"]));Assert.That(excerpts.ToString(),Does.Contain("int marker = ??0;"));
            var errorLines=excerpts["windows"].SelectMany(w=>w["lines"]).Where(l=>(bool)l["isDiagnosticLine"]).ToArray();Assert.That(errorLines.Any(l=>((string)l["text"]).Contains("??0")),Is.True);
            foreach(var window in excerpts["windows"])Assert.That(window["generatedDiagnosticLine"].Type,Is.EqualTo(JTokenType.Integer));
            Assert.That(summary["diagnostics"].Values<string>().Any(d=>d.Contains("column")&&d.Contains("generated line")),Is.True);
            CollectionAssert.AreEqual(roots,SceneManager.GetActiveScene().GetRootGameObjects().Select(g=>g.GetInstanceID()).OrderBy(i=>i).ToArray());Assert.That(original.transform.position,Is.EqualTo(new Vector3(2,3,4)));Assert.That(ProjectSignature(),Is.EqualTo(projectBefore));Assert.That(GameObject.Find("MustNeverExecute"),Is.Null);
        }
        [Test] public void LongSingleLineWindowKeepsTheRealErrorColumnVisible()
        {
            string bad=new string(' ',900)+"int marker = ??0;";var excerpt=Build(bad,"Line 1, column 914 (generated line 1): Invalid expression term '?'");var line=excerpt["windows"][0]["lines"][0];
            Assert.That((string)line["text"],Does.Contain("??0"));Assert.That((int)line["startColumn"],Is.GreaterThan(1));Assert.That((int)line["diagnosticColumn"],Is.EqualTo(914));Assert.That(line["displayPointerColumn"].Type,Is.EqualTo(JTokenType.Integer));Assert.That((bool)line["clipped"],Is.True);
        }
        [Test] public void LegacyClampedLineDoesNotInventExactGeneratedPointer()
        {
            var excerpt=Build("using Missing;\nline2\nline3\nline4\nline5\nline6\nline7\nline8", "Line 1: Namespace missing");var window=excerpt["windows"][0];
            Assert.That(window["generatedDiagnosticLine"].Type,Is.EqualTo(JTokenType.Null));Assert.That((int)window["startLine"],Is.EqualTo(1));Assert.That(window["lines"].Any(l=>(bool)l["isDiagnosticLine"]),Is.False);
        }
        [Test] public void ManyRealDiagnosticLocationsAreBoundedWithoutEditingSource()
        {
            string source=string.Join("\n",Enumerable.Range(1,100).Select(i=>"int marker"+i+" = ??0;"));string before=source;
            var excerpt=Build(source,Enumerable.Range(1,100).Select(i=>"Line "+i+", column 16 (generated line "+i+"): Invalid expression").ToArray());
            Assert.That(((JArray)excerpt["windows"]).Count,Is.InRange(1,8));Assert.That(excerpt.ToString(Formatting.None).Length,Is.LessThanOrEqualTo(6000));Assert.That((bool)excerpt["truncated"],Is.True);Assert.That(source,Is.EqualTo(before));
        }
        [Test] public void SensitiveLiteralValuesAreMaskedButOriginalHashAndLineNumbersRemain()
        {
            string token="ghp_"+new string('Q',36);string source="string password = \"unregistered-private-value\";\nstring anything = \""+token+"\";\nint marker = ??0;";var excerpt=Build(source,"Line 1, column 14 (generated line 3): Invalid expression");string visible=excerpt.ToString();
            Assert.That(visible,Does.Not.Contain("unregistered-private-value").And.Not.Contain(token));Assert.That(visible,Does.Contain("int marker = ??0;"));Assert.That((bool)excerpt["redacted"],Is.True);Assert.That((string)excerpt["sourceHash"],Is.EqualTo(TextChunker.Digest(source)));Assert.That((int)excerpt["windows"][0]["generatedDiagnosticLine"],Is.EqualTo(3));
        }
        [Test] public void NonSourceCompilerFailureDoesNotDumpArbitrarySource()
        {
            var excerpt=Build("return 1;","Roslyn compilation error: missing metadata reference");Assert.That((bool)excerpt["excerptsAvailable"],Is.False);Assert.That(((JArray)excerpt["windows"]).Count,Is.Zero);
        }
    }
}
