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
        [UnityTest] public IEnumerator CloudReasoningUsesNativeDefaultOnlyWhenEnabled()=>Await(ReasoningPayload());
        async Task ReasoningPayload()
        {
            foreach(var choice in new[]{("fixture:cloud",true),("fixture:cloud",false),("fixture-local",true)})
            using(var server=new ConnectionHttpServer((path,body)=>new JObject{["message"]=new JObject{["content"]="ok"}}))
            {
                var profile=ScriptableObject.CreateInstance<ModelProfile>();profile.Endpoint=server.Url.TrimEnd('/');profile.Model=choice.Item1;profile.EnableCloudReasoning=choice.Item2;profile.Retries=0;
                try
                {
                    using(var provider=new LocalProvider(profile))await provider.CompleteAsync(new CompletionRequest(),CancellationToken.None);
                    var body=JObject.Parse(server.Requests.Single().Body);
                    if(choice.Item1.EndsWith(":cloud")&&choice.Item2)Assert.IsNull(body["think"],"Do not force reasoning off in Deep cloud execution");
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
