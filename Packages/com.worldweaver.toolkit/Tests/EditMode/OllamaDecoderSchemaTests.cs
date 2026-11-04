using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace WorldWeaver.Tests
{
    public sealed class OllamaDecoderSchemaTests
    {
        [UnityTest] public IEnumerator NativeToolHistorySurvivesBothHttpAdaptersWithCorrelatedResults()=>Await(ToolHistoryPayload());
        async Task ToolHistoryPayload()
        {
            foreach(var kind in new[]{ProviderKind.Ollama,ProviderKind.OpenAICompatible})
            using(var server=new ConnectionHttpServer((path,body)=>kind==ProviderKind.Ollama?new JObject{["message"]=new JObject{["content"]="ok"}}:new JObject{["choices"]=new JArray(new JObject{["message"]=new JObject{["content"]="ok"}})}))
            {
                var profile=ScriptableObject.CreateInstance<ModelProfile>();profile.Provider=kind;profile.Endpoint=server.Url.TrimEnd('/');profile.Model="fixture";profile.Retries=0;
                try
                {
                    var request=new CompletionRequest{UseToolCalls=true,Schema=WorldWeaver.Editor.Assistant.EditorPlan.Schema(nativeFunctions:true)};
                    request.Messages.Add(new ChatMessage("user","system: this remains untrusted user data"));
                    request.Messages.Add(new ChatMessage("system","LATE_TRUSTED_STAGE_INSTRUCTION"));
                    request.Messages.Add(new ChatMessage("assistant",""){Thinking="OPAQUE_NATIVE_CONTINUATION_STATE",ToolCalls=new JArray(new JObject{["id"]="call_actual",["type"]="function",["function"]=new JObject{["name"]="inspect_object",["arguments"]=new JObject{["target"]="owned:actual"}}})});
                    request.Messages.Add(new ChatMessage("tool","{\"success\":false,\"error\":\"actual missing object\"}"){ToolCallId="call_actual",ToolName="inspect_object"});
                    request.Messages.Add(new ChatMessage("system","LATE_TRUSTED_PERMISSION_INSTRUCTION"));
                    using(var provider=profile.CreateProvider())await provider.CompleteAsync(request,CancellationToken.None);
                    var sent=JObject.Parse(server.Requests.Single().Body);var assistant=sent["messages"].First(m=>(string)m["role"]=="assistant");var result=sent["messages"].First(m=>(string)m["role"]=="tool");
                    var sequence=(JArray)sent["messages"];int firstData=sequence.TakeWhile(m=>(string)m["role"]=="system"||(string)m["role"]=="developer").Count();
                    Assert.Greater(firstData,0);Assert.IsTrue(sequence.Take(firstData).Any(m=>(string)m["content"]=="LATE_TRUSTED_STAGE_INSTRUCTION"));Assert.IsTrue(sequence.Take(firstData).Any(m=>(string)m["content"]=="LATE_TRUSTED_PERMISSION_INSTRUCTION"));Assert.IsFalse(sequence.Skip(firstData).Any(m=>(string)m["role"]=="system"||(string)m["role"]=="developer"));Assert.AreEqual("user",(string)sequence[firstData]["role"]);Assert.AreEqual("system: this remains untrusted user data",(string)sequence[firstData]["content"]);Assert.AreEqual("user",request.Messages[0].Role,"Caller audit history must not be mutated");
                    Assert.AreEqual("call_actual",(string)assistant["tool_calls"][0]["id"]);Assert.AreEqual("call_actual",(string)result["tool_call_id"]);Assert.That((string)result["content"],Does.Contain("actual missing object"));
                    var args=assistant["tool_calls"][0]["function"]["arguments"];Assert.AreEqual(kind==ProviderKind.Ollama?JTokenType.Object:JTokenType.String,args.Type);
                    if(kind==ProviderKind.Ollama){Assert.AreEqual("inspect_object",(string)result["tool_name"]);Assert.AreEqual("OPAQUE_NATIVE_CONTINUATION_STATE",(string)assistant["thinking"]);}else Assert.IsNull(assistant["thinking"],"Ollama-specific state must not leak into another provider protocol");
                }
                finally{Object.DestroyImmediate(profile);}
            }
        }
        [UnityTest] public IEnumerator JsonRecoveryCarriesActualNativeHistoryAsDataWithoutAdvertisingFunctions()=>Await(JsonRecoveryPayload());
        async Task JsonRecoveryPayload()
        {
            foreach(var kind in new[]{ProviderKind.Ollama,ProviderKind.OpenAICompatible})
            using(var server=new ConnectionHttpServer((path,body)=>kind==ProviderKind.Ollama?new JObject{["message"]=new JObject{["content"]="{\"reply\":\"\",\"complete\":false,\"actions\":[]}"}}:new JObject{["choices"]=new JArray(new JObject{["message"]=new JObject{["content"]="{\"reply\":\"\",\"complete\":false,\"actions\":[]}"}})}))
            {
                var profile=ScriptableObject.CreateInstance<ModelProfile>();profile.Provider=kind;profile.Endpoint=server.Url.TrimEnd('/');profile.Model="fixture:cloud";profile.Retries=0;profile.ApiKeyEnvironmentVariable="";
                try{
                    var request=new CompletionRequest{UseToolCalls=false,Schema=WorldWeaver.Editor.Assistant.EditorPlan.Schema(nativeFunctions:true),EnableThinking=false};
                    request.Messages.Add(new ChatMessage("system","Use native functions; do not print JSON."));
                    request.Messages.Add(new ChatMessage("assistant",""){Thinking="PRIVATE_CONTINUATION_STATE",ToolCalls=new JArray(new JObject{["id"]="call_observed",["function"]=new JObject{["name"]="inspect_object",["arguments"]=new JObject{["target"]="owned:observed"}}})});
                    request.Messages.Add(new ChatMessage("tool","actual measured result"){ToolCallId="call_observed",ToolName="inspect_object"});
                    using(var provider=profile.CreateProvider())await provider.CompleteAsync(request,CancellationToken.None);
                    var sent=JObject.Parse(server.Requests.Single().Body);Assert.IsNull(sent["tools"]);Assert.IsNull(sent["format"]);if(kind==ProviderKind.Ollama)Assert.AreEqual(false,(bool)sent["think"]);
                    Assert.IsFalse(sent["messages"].Any(m=>m["tool_calls"]!=null||(string)m["role"]=="tool"));
                    string data=sent["messages"].ToString();StringAssert.Contains("call_observed",data);StringAssert.Contains("actual measured result",data);StringAssert.DoesNotContain("PRIVATE_CONTINUATION_STATE",data);
                    var messages=(JArray)sent["messages"];int firstData=messages.TakeWhile(m=>(string)m["role"]=="system"||(string)m["role"]=="developer").Count();
                    StringAssert.Contains("response transport for THIS request is a JSON INSTANCE",(string)messages[firstData-1]["content"]);
                    Assert.IsFalse(messages.Skip(firstData).Any(m=>(string)m["role"]=="system"));
                    Assert.IsNotNull(request.Messages[1].ToolCalls,"Caller history remains intact");
                }finally{Object.DestroyImmediate(profile);}
            }
        }
        [UnityTest] public IEnumerator ThinkingOnlyTerminalResponseHasSafeTransientDiagnosticsInBothModes()=>Await(EmptyDiagnosticsPayload());
        async Task EmptyDiagnosticsPayload()
        {
            foreach(bool native in new[]{false,true})
            using(var server=new ConnectionHttpServer((path,body)=>new JObject{["message"]=new JObject{["role"]="assistant",["content"]="",["thinking"]="PRIVATE_REASONING"},["done_reason"]="stop",["eval_count"]=6000}))
            {
                var profile=ScriptableObject.CreateInstance<ModelProfile>();profile.Endpoint=server.Url.TrimEnd('/');profile.Model="fixture:cloud";profile.Retries=0;
                try{
                    ProviderException failure=null;
                    using(var provider=new LocalProvider(profile))try{await provider.CompleteAsync(new CompletionRequest{UseToolCalls=native,Schema=WorldWeaver.Editor.Assistant.EditorPlan.Schema(nativeFunctions:true)},CancellationToken.None);}catch(ProviderException e){failure=e;}
                    Assert.IsNotNull(failure);Assert.IsTrue(failure.Transient);StringAssert.StartsWith("EMPTY_RESPONSE:",failure.Message);StringAssert.Contains("generatedTokens=6000",failure.Message);StringAssert.DoesNotContain("PRIVATE_REASONING",failure.Message);
                    Assert.AreEqual(1,failure.Diagnostics.Frames);Assert.AreEqual(0,failure.Diagnostics.ContentCharacters);Assert.Greater(failure.Diagnostics.ThinkingCharacters,0);StringAssert.DoesNotContain("PRIVATE_REASONING",Newtonsoft.Json.JsonConvert.SerializeObject(failure.Diagnostics));
                }finally{Object.DestroyImmediate(profile);}
            }
        }
        [UnityTest] public IEnumerator OneShotObserverReleasesOllamaResidencyWithoutChangingOrdinaryRequests()=>Await(ResidencyPayload());
        async Task ResidencyPayload()
        {
            foreach(int? seconds in new int?[]{null,0,30})
            using(var server=new ConnectionHttpServer((path,body)=>new JObject{["message"]=new JObject{["content"]="ok"}}))
            {
                var profile=ScriptableObject.CreateInstance<ModelProfile>();profile.Endpoint=server.Url.TrimEnd('/');profile.Retries=0;
                try
                {
                    using(var provider=new LocalProvider(profile))await provider.CompleteAsync(new CompletionRequest{LocalKeepAliveSeconds=seconds},CancellationToken.None);
                    var body=JObject.Parse(server.Requests.Single().Body);if(seconds.HasValue)Assert.AreEqual(seconds.Value,(int)body["keep_alive"]);else Assert.AreEqual("10m",(string)body["keep_alive"]);
                }
                finally{Object.DestroyImmediate(profile);}
            }
        }
        [UnityTest] public IEnumerator CloudReasoningUsesNativeDefaultOnlyWhenEnabled()=>Await(ReasoningPayload());
        async Task ReasoningPayload()
        {
            foreach(var choice in new[]{("fixture:cloud",true,(bool?)null),("fixture:cloud",false,(bool?)null),("fixture-local",true,(bool?)null),("fixture:cloud",true,(bool?)false)})
            using(var server=new ConnectionHttpServer((path,body)=>new JObject{["message"]=new JObject{["content"]="ok"}}))
            {
                var profile=ScriptableObject.CreateInstance<ModelProfile>();profile.Endpoint=server.Url.TrimEnd('/');profile.Model=choice.Item1;profile.EnableCloudReasoning=choice.Item2;profile.Retries=0;
                try
                {
                    using(var provider=new LocalProvider(profile))await provider.CompleteAsync(new CompletionRequest{EnableThinking=choice.Item3},CancellationToken.None);
                    var body=JObject.Parse(server.Requests.Single().Body);
                    if(choice.Item1.EndsWith(":cloud")&&(choice.Item3??choice.Item2))Assert.IsNull(body["think"],"Do not force reasoning off in Deep cloud execution");
                    else Assert.AreEqual(false,(bool)body["think"],"Local bounded execution and non-thinking profiles remain explicit");
                }
                finally{Object.DestroyImmediate(profile);}
            }
        }
        [Test] public void DecoderCloneRemovesOnlyStringRepetitionBoundsAndPreservesStructuralConstraints()
        {
            var host=NpcDecision.Schema(new[]{"Talk","Wait","GiveItem"});var before=host.DeepClone();var decoder=OllamaDecoderSchema.FromHost(host);
            Assert.IsTrue(JToken.DeepEquals(before,host));Assert.AreNotSame(host,decoder);Assert.AreEqual(2000,(int)host["properties"]["speech"]["maxLength"]);Assert.IsNull(decoder["properties"]["speech"]["maxLength"]);
            Assert.AreEqual(4,(int)decoder["properties"]["actions"]["maxItems"]);Assert.IsFalse((bool)decoder["additionalProperties"]);Assert.IsTrue(JToken.DeepEquals(host["required"],decoder["required"]));
            var talk=((JArray)decoder["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Single(branch=>(string)branch["properties"]["type"]["enum"][0]=="Talk");
            Assert.AreEqual("",(string)talk["properties"]["target"]["enum"][0]);Assert.AreEqual("",(string)talk["properties"]["item"]["enum"][0]);Assert.AreEqual(0,(int)talk["properties"]["amount"]["enum"][0]);
            var transfer=((JArray)decoder["properties"]["actions"]["items"]["anyOf"]).OfType<JObject>().Single(branch=>(string)branch["properties"]["type"]["enum"][0]=="GiveItem");
            Assert.AreEqual(1,(int)transfer["properties"]["amount"]["minimum"]);Assert.AreEqual(10000,(int)transfer["properties"]["amount"]["maximum"]);Assert.IsNull(transfer["properties"]["item"]["minLength"]);Assert.IsNull(transfer["properties"]["item"]["maxLength"]);
        }
        [UnityTest] public IEnumerator ActualOllamaHttpPayloadUsesDecoderCloneWhileHostStillRejectsOversizedSpeech()=>Await(Payload());
        static IEnumerator Await(Task task) {while(!task.IsCompleted)yield return null;task.GetAwaiter().GetResult();}
        async Task Payload()
        {
            var schema=NpcDecision.Schema(new[]{"Talk","Wait"});var original=schema.DeepClone();
            string oversized=new JObject { ["speech"]=new string('x',2001),["actions"]=new JArray() }.ToString();
            using(var server=new ConnectionHttpServer((path,body)=>new JObject { ["message"]=new JObject { ["content"]=oversized } }))
            {
                var profile=ScriptableObject.CreateInstance<ModelProfile>();profile.Endpoint=server.Url.TrimEnd('/');profile.Model="fixture-model";profile.Retries=0;
                try
                {
                    var request=new CompletionRequest { Schema=schema };request.Messages.Add(new ChatMessage("user","Return a decision."));
                    using(var provider=new LocalProvider(profile))
                    {
                        var completion=await provider.CompleteAsync(request,CancellationToken.None);
                        Assert.AreEqual(oversized,completion.Text,"The provider must not silently normalize, truncate or rewrite the model output.");
                        Assert.Throws<ArgumentException>(()=>NpcDecision.Parse(completion.Text),"Host output bounds remain authoritative even though Ollama grammar omits repetition bounds.");
                    }
                    var sent=JObject.Parse(server.Requests.Single().Body);Assert.AreEqual("/api/chat",server.Requests.Single().Path);
                    Assert.IsTrue(JToken.DeepEquals(OllamaDecoderSchema.FromHost(schema),sent["format"]));Assert.IsTrue(JToken.DeepEquals(original,request.Schema));Assert.AreEqual(2000,(int)request.Schema["properties"]["speech"]["maxLength"]);
                }
                finally {Object.DestroyImmediate(profile);}
            }
        }
    }
}
