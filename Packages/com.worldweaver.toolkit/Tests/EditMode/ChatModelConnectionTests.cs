using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using WorldWeaver.Editor.Assistant;
namespace WorldWeaver.Tests
{
    public sealed class ChatModelConnectionTests
    {
        const string FakeKey="fixtureSessionSecret_0123456789_OnlyForTest",ReloadState="WorldWeaver.ConnectionReloadTest";
        static readonly string DomainNonce=Guid.NewGuid().ToString("N");
        string folder;
        readonly List<string> createdEnvironments=new List<string>();
        [SetUp] public void Setup()
        {
            if(TestContext.CurrentContext.Test.Name==nameof(SessionCredentialAndModelSelectionSurviveActualDomainReload)&&!string.IsNullOrEmpty(SessionState.GetString(ReloadState,""))){folder=SessionState.GetString(ReloadState,"");return;}
            folder=Path.GetFullPath("UserSettings/WorldWeaverConnectionTests/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        }
        [TearDown] public void Cleanup()
        {
            foreach(string name in createdEnvironments)Environment.SetEnvironmentVariable(name,null,EnvironmentVariableTarget.Process);
            if(Directory.Exists(folder))
            {
                foreach(string path in Directory.GetFiles(folder,"connections.json",SearchOption.AllDirectories))
                {var store=new ChatModelConnections(Path.GetDirectoryName(path));foreach(var connection in store.Connections)store.Remove(connection.Id);}
                Directory.Delete(folder,true);
            }
            SessionState.EraseString(ReloadState);
            SessionState.EraseString(ReloadState+".domain");
        }
        ChatModelConnection Connection(string endpoint,ChatConnectionProtocol protocol=ChatConnectionProtocol.OpenAICompatible,OpenAIResponseMode response=OpenAIResponseMode.JsonObject)=>new ChatModelConnection{Name="Test connection",Endpoint=endpoint,Model=protocol==ChatConnectionProtocol.Ollama?"local-model":"mock-model",Protocol=protocol,ResponseMode=response};
        [TestCase("https://api.example.com","https://api.example.com/v1")]
        [TestCase("https://api.example.com/v1/","https://api.example.com/v1")]
        [TestCase("https://api.example.com/custom/v1/chat/completions","https://api.example.com/custom/v1")]
        [TestCase("https://api.example.com/chat/completions","https://api.example.com")]
        [TestCase("https://api.example.com/custom/base","https://api.example.com/custom/base")]
        public void OpenAIEndpointsPreserveExplicitPathsAndNormalizeRoot(string input,string expected)=>Assert.AreEqual(expected,ChatModelConnections.ApiBase(Connection(input)));
        [Test] public void InvalidUrlsAndCredentialMetadataFailBeforeWriting()
        {
            var store=new ChatModelConnections(folder);
            foreach(string url in new[]{"http://remote.example.com/v1","https://name:password@example.com/v1","https://example.com/v1?key=unsafe","https://example.com/v1#key"})Assert.Throws<ArgumentException>(()=>store.Add(Connection(url)));
            var config=Connection("https://example.com/v1");config.ApiKeyEnvironmentVariable="not a variable";Assert.Throws<ArgumentException>(()=>store.Add(config));
            config=Connection("https://example.com/v1");config.Name=FakeKey;Assert.Throws<ArgumentException>(()=>store.Add(config,FakeKey));Assert.IsFalse(File.Exists(store.FilePath));
        }
        [UnityTest] public IEnumerator HealthOnlyListsModelsAndCustomDropdownRefreshUsesThatActualServer()=>Await(CheckHealthAndRefresh());
        async Task CheckHealthAndRefresh()
        {
            using(var server=new ConnectionHttpServer((path,body)=>new JObject{["data"]=new JArray(new JObject{["id"]="mock-model"})}))
            using(var controller=new ChatController(null,folder+"/history",folder+"/models"))
            {
                var added=controller.AddModelConnection(Connection(server.Url),FakeKey);Assert.AreEqual(added.ModelKey,controller.Settings.ModelKey);Assert.IsFalse(controller.Models.Single(m=>m.Key==added.ModelKey).Available);
                var checkedConnection=await controller.TestModelConnectionAsync(added.Id);Assert.IsTrue(checkedConnection.Success);Assert.IsFalse(checkedConnection.ResponseRequested);Assert.AreEqual(1,checkedConnection.ListedModels);
                await controller.RefreshModelConnectionsAsync();Assert.AreEqual(added.ModelKey,controller.Settings.ModelKey);var model=controller.Models.Single(m=>m.Key==added.ModelKey);Assert.IsTrue(model.Available);Assert.IsFalse(model.SupportsVision);
                Assert.IsTrue(server.Requests.All(r=>r.Method=="GET"&&r.Path=="/v1/models"));Assert.IsTrue(server.Requests.All(r=>r.Authorization=="Bearer "+FakeKey));Assert.IsTrue(server.Requests.All(r=>!r.Body.Contains(FakeKey)));
                Assert.IsFalse(File.ReadAllText(folder+"/models/connections.json").Contains(FakeKey));Assert.IsTrue(controller.RemoveModelConnection(added.Id));Assert.IsFalse(controller.Models.Any(m=>m.Key==added.ModelKey));Assert.IsNull(Environment.GetEnvironmentVariable(added.ApiKeyEnvironmentVariable));
            }
        }
        [UnityTest] public IEnumerator OpenAIRequestModesMapActualHttpBodyAndNeverSendImageCapabilities()=>Await(CheckResponseModes());
        [UnityTest] public IEnumerator CloudOllamaCanBeSelectedWithoutBeingDownloadedOrListedInTags()=>Await(CheckUnlistedCloud());
        async Task CheckUnlistedCloud()
        {
            using(var server=new ConnectionHttpServer((path,body)=>path.EndsWith("/api/tags")?new JObject{["models"]=new JArray()}:new JObject{["capabilities"]=new JArray("completion","tools")}))
            using(var controller=new ChatController(null,folder+"/history",folder+"/models"))
            {
                var config=Connection(server.Url,ChatConnectionProtocol.Ollama);config.Model="test-model:cloud";
                controller.AddModelConnection(config);await controller.RefreshModelConnectionsAsync();Assert.IsTrue(controller.Connected);
                var profile=ChatModelConnections.Profile(config);
                try{using(var provider=new LocalProvider(profile))Assert.IsTrue(await provider.HealthAsync(CancellationToken.None));}finally{UnityEngine.Object.DestroyImmediate(profile);}
                Assert.IsTrue(server.Requests.All(r=>r.Path.EndsWith("/api/tags")||r.Path.EndsWith("/api/show")));Assert.IsTrue(server.Requests.Any(r=>r.Path.EndsWith("/api/show")));
            }
        }
        [Test] public void EscapedSessionSecretIsRedactedFromJsonHistoryAndMetadata()
        {
            const string escapedSecret="fixtureQuote\"AndBackslash\\Secret0123456789";
            using(var controller=new ChatController(null,folder+"/history",folder+"/models"))
            {
                var connection=controller.AddModelConnection(Connection("https://example.com/v1"),escapedSecret);controller.Current.Messages.Add(new AssistantMessage{Text="echo "+escapedSecret});controller.SaveSettings();
                var reloaded=new ChatHistoryStore(folder+"/history").Load().Single();StringAssert.Contains("[секрет скрыт]",reloaded.Messages.Single().Text);
                controller.RemoveModelConnection(connection.Id);controller.SaveSettings();Assert.IsNull(Environment.GetEnvironmentVariable(connection.ApiKeyEnvironmentVariable));
                foreach(string path in Directory.GetFiles(folder,"*.json",SearchOption.AllDirectories)){string text=File.ReadAllText(path);Assert.IsFalse(text.Contains(escapedSecret));Assert.IsFalse(text.Contains("fixtureQuote"));}
            }
        }
        [UnityTest] public IEnumerator CloudImagesAreRejectedBeforeAnyHttpRequest()=>Await(CheckCloudImages());
        async Task CheckCloudImages()
        {
            using(var server=new ConnectionHttpServer((path,body)=>new JObject()))
            {
                var profile=ChatModelConnections.Profile(Connection(server.Url));
                try{using(var provider=profile.CreateProvider()){var request=new CompletionRequest();request.Messages.Add(new ChatMessage("user","Look",new[]{"AAAA"}));bool rejected=false;try{await provider.CompleteAsync(request,CancellationToken.None);}catch(ProviderException){rejected=true;}Assert.IsTrue(rejected);Assert.IsEmpty(server.Requests);}}
                finally{UnityEngine.Object.DestroyImmediate(profile);}
            }
        }
        async Task CheckResponseModes()
        {
            using(var server=new ConnectionHttpServer((path,body)=>new JObject{["choices"]=new JArray(new JObject{["message"]=new JObject{["content"]="{\"ok\":true}"}})}))
            {
                foreach(OpenAIResponseMode mode in Enum.GetValues(typeof(OpenAIResponseMode)))
                {
                    var connection=Connection(server.Url+"proxy/v1/chat/completions",response:mode);var profile=ChatModelConnections.Profile(connection);
                    try
                    {
                        var request=new CompletionRequest{Schema=new JObject{["type"]="object",["properties"]=new JObject{["ok"]=new JObject{["type"]="boolean"}},["required"]=new JArray("ok"),["additionalProperties"]=false}};request.Messages.Add(new ChatMessage("user","Return JSON."));
                        using(var provider=profile.CreateProvider())Assert.IsNotEmpty((await provider.CompleteAsync(request,CancellationToken.None)).Text);
                        var sent=server.Requests.Last();Assert.AreEqual("/proxy/v1/chat/completions",sent.Path);var body=JObject.Parse(sent.Body);
                        if(mode==OpenAIResponseMode.Text)Assert.IsNull(body["response_format"]);else Assert.AreEqual(mode==OpenAIResponseMode.JsonSchema?"json_schema":"json_object",(string)body["response_format"]["type"]);
                        Assert.IsFalse(ChatModelConnections.AsModel(connection).SupportsVision);
                    }
                    finally{UnityEngine.Object.DestroyImmediate(profile);}
                }
            }
        }
        [UnityTest] public IEnumerator ExplicitResponseTestIsTheOnlyProbeThatPostsAChatRequest()=>Await(CheckExplicitResponse());
        async Task CheckExplicitResponse()
        {
            using(var server=new ConnectionHttpServer((path,body)=>path.EndsWith("/models")?new JObject{["data"]=new JArray()}:new JObject{["choices"]=new JArray(new JObject{["message"]=new JObject{["content"]="OK"}})}))
            {
                var store=new ChatModelConnections(folder);var connection=store.Add(Connection(server.Url,response:OpenAIResponseMode.Text));Assert.IsFalse((await store.TestAsync(connection.Id)).Success);Assert.IsTrue(server.Requests.All(r=>r.Method=="GET"));
                var response=await store.TestAsync(connection.Id,true);Assert.IsTrue(response.Success);Assert.IsTrue(response.ResponseRequested);var sent=server.Requests.Last();Assert.AreEqual("POST",sent.Method);Assert.AreEqual("/v1/chat/completions",sent.Path);Assert.AreEqual(64,(int)JObject.Parse(sent.Body)["max_tokens"]);
            }
        }
        [UnityTest] public IEnumerator OllamaProxyEndpointAndLocalVisionComeFromActualShowResponse()=>Await(CheckOllama());
        [UnityTest] public IEnumerator OllamaEmbeddingOnlyCapabilityDoesNotBecomeAnAvailableChatModel()=>Await(CheckEmbeddingOnly());
        async Task CheckEmbeddingOnly()
        {
            using(var server=new ConnectionHttpServer((path,body)=>path.EndsWith("/api/tags")?new JObject{["models"]=new JArray(new JObject{["name"]="local-model"})}:new JObject{["capabilities"]=new JArray("embedding")}))
            {
                var store=new ChatModelConnections(folder);var connection=store.Add(Connection(server.Url,ChatConnectionProtocol.Ollama));var result=await store.TestAsync(connection.Id);Assert.IsFalse(result.Success);Assert.IsFalse(result.SupportsVision);StringAssert.Contains("completion",result.Message);Assert.IsFalse(server.Requests.Any(r=>r.Path.EndsWith("/api/chat")));
            }
        }
        async Task CheckOllama()
        {
            using(var server=new ConnectionHttpServer((path,body)=>path.EndsWith("/api/tags")?new JObject{["models"]=new JArray(new JObject{["name"]="local-model"})}:new JObject{["capabilities"]=new JArray("completion","vision")}))
            {
                var store=new ChatModelConnections(folder);var config=Connection(server.Url+"proxy/api/chat",ChatConnectionProtocol.Ollama);var connection=store.Add(config);var result=await store.TestAsync(connection.Id);Assert.IsTrue(result.Success);Assert.IsTrue(result.SupportsVision);Assert.AreEqual(new[]{"/proxy/api/tags","/proxy/api/show"},server.Requests.Select(r=>r.Path).ToArray());
                Assert.Throws<ArgumentException>(()=>store.Add(Connection(server.Url,ChatConnectionProtocol.Ollama),FakeKey));
            }
        }
        [UnityTest] public IEnumerator SessionCredentialAndModelSelectionSurviveActualDomainReload()
        {
            using(var controller=new ChatController(null,folder+"/history",folder+"/models"))
            {
                var connection=controller.AddModelConnection(Connection("https://example.com/v1",response:OpenAIResponseMode.Text),FakeKey);controller.Current.Messages.Add(new AssistantMessage{Text="Untrusted echoed credential: "+FakeKey});controller.SaveSettings();SessionState.SetString(ReloadState,folder);
                Assert.AreEqual(FakeKey,Environment.GetEnvironmentVariable(connection.ApiKeyEnvironmentVariable));
            }
            // A no-change RecompileScripts(false) can return without a reload. Require a real domain transition.
            SessionState.SetString(ReloadState+".domain",DomainNonce);EditorUtility.RequestScriptReload();yield return new WaitForDomainReload();
            folder=SessionState.GetString(ReloadState,"");Assert.IsNotEmpty(folder);
            Assert.AreNotEqual(SessionState.GetString(ReloadState+".domain",""),DomainNonce,"No actual domain reload occurred.");
            using(var controller=new ChatController(null,folder+"/history",folder+"/models"))
            {
                var connection=controller.ModelConnections.Single();Assert.AreEqual(connection.ModelKey,controller.Settings.ModelKey);Assert.IsTrue(controller.Models.Any(m=>m.Key==connection.ModelKey));Assert.AreEqual(FakeKey,Environment.GetEnvironmentVariable(connection.ApiKeyEnvironmentVariable));
                var profile=(ModelProfile)typeof(ChatController).GetMethod("BuildProfile",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);
                try{Assert.AreEqual(ProviderKind.OpenAICompatible,profile.Provider);Assert.AreEqual(OpenAIResponseMode.Text,profile.ResponseMode);Assert.AreEqual(connection.ApiKeyEnvironmentVariable,profile.ApiKeyEnvironmentVariable);}finally{UnityEngine.Object.DestroyImmediate(profile);}
                foreach(string path in Directory.GetFiles(folder,"*.json",SearchOption.AllDirectories))Assert.IsFalse(File.ReadAllText(path).Contains(FakeKey),"A credential was persisted in config/history.");
                StringAssert.Contains("[секрет скрыт]",controller.Current.Messages.Last().Text);
            }
        }
        static IEnumerator Await(Task task){while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception.GetBaseException();if(task.IsCanceled)Assert.Fail("Unexpected cancellation.");}
    }
    sealed class ConnectionHttpServer:IDisposable
    {
        internal sealed class Request {public string Method,Path,Body,Authorization;}
        readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);readonly Func<string,JObject,JObject> response;readonly CancellationTokenSource cancellation=new CancellationTokenSource();readonly List<Request> requests=new List<Request>();
        public string Url {get;}
        public Request[] Requests {get{lock(requests)return requests.ToArray();}}
        public ConnectionHttpServer(Func<string,JObject,JObject> response){this.response=response;listener.Start();Url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/";_ = Run();}
        async Task Run()
        {
            while(!cancellation.IsCancellationRequested)
            {
                TcpClient client;try{client=await listener.AcceptTcpClientAsync().ConfigureAwait(false);}catch(ObjectDisposedException){return;}catch(SocketException){return;}
                using(client)using(var stream=client.GetStream())using(var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true))
                {
                    string first=await reader.ReadLineAsync().ConfigureAwait(false);if(string.IsNullOrEmpty(first))continue;string[] parts=first.Split(' ');int length=0;string authorization="",line;
                    while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync().ConfigureAwait(false))){int at=line.IndexOf(':');if(at<0)continue;string key=line.Substring(0,at),value=line.Substring(at+1).Trim();if(key.Equals("Content-Length",StringComparison.OrdinalIgnoreCase))int.TryParse(value,out length);if(key.Equals("Authorization",StringComparison.OrdinalIgnoreCase))authorization=value;}
                    if(length<0||length>256*1024)return;var buffer=new char[length];int read=0;while(read<length){int n=await reader.ReadAsync(buffer,read,length-read).ConfigureAwait(false);if(n==0)break;read+=n;}string text=new string(buffer,0,read);var request=new Request{Method=parts[0],Path=parts[1],Body=text,Authorization=authorization};lock(requests)requests.Add(request);
                    byte[] payload=Encoding.UTF8.GetBytes(response(request.Path,string.IsNullOrEmpty(text)?null:JObject.Parse(text)).ToString(Newtonsoft.Json.Formatting.None));byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: "+payload.Length+"\r\n\r\n");await stream.WriteAsync(header,0,header.Length).ConfigureAwait(false);await stream.WriteAsync(payload,0,payload.Length).ConfigureAwait(false);
                }
            }
        }
        public void Dispose(){cancellation.Cancel();listener.Stop();cancellation.Dispose();}
    }
}
