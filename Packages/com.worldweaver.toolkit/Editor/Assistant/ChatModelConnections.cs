using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    public enum ChatConnectionProtocol { Ollama, OpenAICompatible }
    [Serializable] public sealed class ChatModelConnection
    {
        public string Id=Guid.NewGuid().ToString("N"),Name="",Endpoint="",Model="",ApiKeyEnvironmentVariable="";
        public ChatConnectionProtocol Protocol=ChatConnectionProtocol.OpenAICompatible;
        public OpenAIResponseMode ResponseMode=OpenAIResponseMode.JsonObject;
        [JsonIgnore] public string ModelKey=>"connection:"+Id;
    }
    public sealed class ChatConnectionTestResult
    {
        public bool Success,SupportsVision,ResponseRequested;
        public int ListedModels;
        public string Message;
    }
    /// <summary>Only non-secret connection metadata is persisted. Session credentials live in process environment.</summary>
    public sealed class ChatModelConnections
    {
        const string SessionPrefix="WORLDWEAVER_SESSION_KEY_";
        static readonly HashSet<string> secretVariables=new HashSet<string>(StringComparer.Ordinal);
        // Clearing a connection must not unmask raw strings still held by an open chat in this domain.
        // This set is memory-only; active credentials are rediscovered from process env after a reload.
        static readonly HashSet<string> observedSecrets=new HashSet<string>(StringComparer.Ordinal);
        public readonly string FilePath;
        [Serializable] sealed class Document { public int Version=1;public List<ChatModelConnection> Connections=new List<ChatModelConnection>(); }
        public string LoadError {get;private set;}
        public ChatModelConnections(string folder=null)
        {FilePath=Path.GetFullPath(Path.Combine(folder??"UserSettings/WorldWeaverModels","connections.json"));Read();}
        public IReadOnlyList<ChatModelConnection> Connections=>Read();
        static ChatModelConnection Copy(ChatModelConnection connection)=>JsonConvert.DeserializeObject<ChatModelConnection>(JsonConvert.SerializeObject(connection));
        List<ChatModelConnection> Read()
        {
            if(!File.Exists(FilePath)){LoadError=null;return new List<ChatModelConnection>();}
            try
            {
                if(new FileInfo(FilePath).Length>65536)throw new InvalidDataException();
                var root=JObject.Parse(File.ReadAllText(FilePath),new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
                var doc=root.ToObject<Document>(JsonSerializer.Create(new JsonSerializerSettings{TypeNameHandling=TypeNameHandling.None,MaxDepth=12,MissingMemberHandling=MissingMemberHandling.Error}));
                if(doc==null||doc.Version!=1||doc.Connections==null||doc.Connections.Count>12||doc.Connections.Select(c=>c?.Id).Distinct().Count()!=doc.Connections.Count)throw new InvalidDataException();
                foreach(var connection in doc.Connections){Validate(connection);RegisterVariable(connection.ApiKeyEnvironmentVariable);}
                LoadError=null;return doc.Connections;
            }
            catch(Exception error) when(error is IOException||error is JsonException||error is ArgumentException)
            {LoadError="Файл подключений повреждён. Он сохранён без изменений: "+FilePath;return new List<ChatModelConnection>();}
        }
        static void RegisterVariable(string name){if(!string.IsNullOrEmpty(name))secretVariables.Add(name);}
        public static string RedactSecrets(string text)
        {
            text=text??"";
            var names=secretVariables.ToList();
            foreach(DictionaryEntry item in Environment.GetEnvironmentVariables())if(item.Key is string name&&name.StartsWith(SessionPrefix,StringComparison.Ordinal)&&!names.Contains(name))names.Add(name);
            foreach(string name in names)
            {
                string value=Environment.GetEnvironmentVariable(name);if(string.IsNullOrEmpty(value)||value.Length<8||value.Length>4096)continue;
                observedSecrets.Add(value);
            }
            foreach(string value in observedSecrets.OrderByDescending(v=>v.Length))
            {
                // History/jobs can pass serialized JSON, where quotation marks/backslashes in a key are escaped.
                string encoded=JsonConvert.ToString(value);string escaped=encoded.Substring(1,encoded.Length-2);
                if(escaped!=value)text=text.Replace(escaped,"[секрет скрыт]");text=text.Replace(value,"[секрет скрыт]");
            }
            return text;
        }
        static bool SecretLike(string value)=>Regex.IsMatch(value??"",@"(?:gh[pousr]_[A-Za-z0-9]{15,}|github_pat_[A-Za-z0-9_]{15,}|sk-[A-Za-z0-9_-]{15,}|gsk_[A-Za-z0-9]{15,})");
        public static string ApiBase(ChatModelConnection connection)
        {
            if(!Uri.TryCreate(connection.Endpoint?.Trim(),UriKind.Absolute,out var uri)||(uri.Scheme!="https"&&!(uri.Scheme=="http"&&uri.IsLoopback))||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment)||SecretLike(connection.Endpoint))
                throw new ArgumentException("URL: HTTPS или HTTP на этом компьютере, без ключа, логина, query и fragment.");
            string endpoint=uri.AbsoluteUri.TrimEnd('/');string path=uri.AbsolutePath.TrimEnd('/');
            if(connection.Protocol==ChatConnectionProtocol.Ollama)
            {
                foreach(string suffix in new[]{"/api/chat","/api/tags","/api/show","/api"})if(path.EndsWith(suffix,StringComparison.Ordinal))return endpoint.Substring(0,endpoint.Length-suffix.Length);
                return endpoint;
            }
            if(path.EndsWith("/chat/completions",StringComparison.Ordinal))return endpoint.Substring(0,endpoint.Length-"/chat/completions".Length);
            if(path.EndsWith("/models",StringComparison.Ordinal))return endpoint.Substring(0,endpoint.Length-"/models".Length);
            return path.Length==0?endpoint+"/v1":endpoint;
        }
        public static void Validate(ChatModelConnection connection)
        {
            if(connection==null||!ChatHistoryStore.ValidId(connection.Id)||!Enum.IsDefined(typeof(ChatConnectionProtocol),connection.Protocol)||!Enum.IsDefined(typeof(OpenAIResponseMode),connection.ResponseMode))throw new ArgumentException("Некорректное подключение.");
            foreach(var pair in new[]{Tuple.Create(connection.Name,80),Tuple.Create(connection.Model,200),Tuple.Create(connection.Endpoint,700)})if(string.IsNullOrWhiteSpace(pair.Item1)||pair.Item1.Length>pair.Item2||pair.Item1.Any(char.IsControl)||SecretLike(pair.Item1))throw new ArgumentException("Укажите название, ID модели и URL без секретов.");
            string variable=connection.ApiKeyEnvironmentVariable??"";
            if(connection.Protocol==ChatConnectionProtocol.Ollama&&variable.Length>0)throw new ArgumentException("Этот адаптер Ollama работает без API-ключа. Для Bearer API выберите OpenAI-compatible.");
            if(variable.Length>0&&!Regex.IsMatch(variable,@"^[A-Za-z_][A-Za-z0-9_]{0,127}$"))throw new ArgumentException("Укажите имя переменной окружения, например MY_API_KEY. Сам ключ вводится в поле ключа.");
            ApiBase(connection);
        }
        void Write(List<ChatModelConnection> connections)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));string tmp=FilePath+".tmp";
            try {File.WriteAllText(tmp,JsonConvert.SerializeObject(new Document{Connections=connections},Formatting.Indented));if(File.Exists(FilePath))File.Replace(tmp,FilePath,null);else File.Move(tmp,FilePath);}
            finally {if(File.Exists(tmp))File.Delete(tmp);}
        }
        public ChatModelConnection Add(ChatModelConnection value,string sessionKey=null)
        {
            var connection=Copy(value??throw new ArgumentNullException(nameof(value)));connection.Name=connection.Name?.Trim();connection.Model=connection.Model?.Trim();connection.Endpoint=connection.Endpoint?.Trim().TrimEnd('/');connection.ApiKeyEnvironmentVariable=connection.ApiKeyEnvironmentVariable?.Trim()??"";
            Validate(connection);var connections=Read();if(LoadError!=null)throw new InvalidOperationException(LoadError);if(connections.Count>=12)throw new InvalidOperationException("Можно сохранить до 12 подключений.");if(connections.Any(c=>c.Id==connection.Id))throw new ArgumentException("Подключение с этим ID уже существует.");
            if(!string.IsNullOrEmpty(sessionKey))
            {
                if(connection.Protocol==ChatConnectionProtocol.Ollama)throw new ArgumentException("Для этого адаптера Ollama ключ не используется.");
                if(sessionKey.Length<8||sessionKey.Length>4096||sessionKey.Any(char.IsControl)||sessionKey.Any(char.IsWhiteSpace))throw new ArgumentException("Ключ должен содержать 8–4096 символов без пробелов и переводов строки.");
                if(new[]{connection.Name,connection.Model,connection.Endpoint,connection.ApiKeyEnvironmentVariable}.Any(v=>v.Contains(sessionKey)))throw new ArgumentException("Ключ не должен попадать в название, URL, ID модели или имя переменной.");
                if(connection.ApiKeyEnvironmentVariable.Length>0)throw new ArgumentException("Выберите ключ на этот запуск Unity или переменную окружения; заполните один вариант.");
                connection.ApiKeyEnvironmentVariable=SessionPrefix+connection.Id.ToUpperInvariant();
            }
            connections.Add(connection);Write(connections);
            if(!string.IsNullOrEmpty(sessionKey))Environment.SetEnvironmentVariable(connection.ApiKeyEnvironmentVariable,sessionKey,EnvironmentVariableTarget.Process);
            RegisterVariable(connection.ApiKeyEnvironmentVariable);return Copy(connection);
        }
        public bool Remove(string id)
        {
            var connections=Read();if(LoadError!=null)throw new InvalidOperationException(LoadError);var connection=connections.SingleOrDefault(c=>c.Id==id);if(connection==null)return false;
            RedactSecrets("");connections.Remove(connection);Write(connections);if(connection.ApiKeyEnvironmentVariable.StartsWith(SessionPrefix,StringComparison.Ordinal))Environment.SetEnvironmentVariable(connection.ApiKeyEnvironmentVariable,null,EnvironmentVariableTarget.Process);return true;
        }
        public static ModelProfile Profile(ChatModelConnection connection)
        {
            Validate(connection);ValidateCredentialValue(connection.ApiKeyEnvironmentVariable);var profile=ScriptableObject.CreateInstance<ModelProfile>();profile.hideFlags=HideFlags.HideAndDontSave;
            profile.Provider=connection.Protocol==ChatConnectionProtocol.Ollama?ProviderKind.Ollama:ProviderKind.OpenAICompatible;profile.Endpoint=ApiBase(connection);profile.Model=connection.Model;profile.ApiKeyEnvironmentVariable=connection.ApiKeyEnvironmentVariable;
            profile.ResponseMode=connection.ResponseMode;profile.StrictSchema=connection.Protocol==ChatConnectionProtocol.Ollama||connection.ResponseMode==OpenAIResponseMode.JsonSchema;profile.TimeoutSeconds=8;profile.Retries=0;profile.MaxTokens=64;profile.Validate();return profile;
        }
        internal static void ValidateCredentialValue(string variable)
        {
            if(string.IsNullOrEmpty(variable))return;string value=Environment.GetEnvironmentVariable(variable);if(string.IsNullOrEmpty(value))return;
            if(value.Length<8||value.Length>4096||value.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)))throw new ArgumentException("Ключ в переменной окружения должен содержать 8–4096 символов без пробелов и переводов строки.");
        }
        public static AvailableModel AsModel(ChatModelConnection connection)=>new AvailableModel {Key=connection.ModelKey,ConnectionId=connection.Id,Name=connection.Name+" · "+connection.Model,Model=connection.Model,Endpoint=ApiBase(connection),Provider=connection.Protocol==ChatConnectionProtocol.Ollama?ProviderKind.Ollama:ProviderKind.OpenAICompatible,ApiKeyEnvironmentVariable=connection.ApiKeyEnvironmentVariable,ResponseMode=connection.ResponseMode,Description=(connection.Protocol==ChatConnectionProtocol.Ollama?"Ваш сервер Ollama":"Ваш OpenAI-compatible API · только текст")+" · соединение ещё не проверено"};
        public async Task<ChatConnectionTestResult> TestAsync(string id,bool explicitResponse=false,CancellationToken token=default)
        {
            var connection=Read().SingleOrDefault(c=>c.Id==id)??throw new ArgumentException("Подключение не найдено.");var profile=Profile(connection);
            try
            {
                if(explicitResponse)
                {
                    var request=new CompletionRequest();request.Messages.Add(new ChatMessage("user","Reply with one JSON object: {\"ok\":true}."));
                    if(connection.ResponseMode==OpenAIResponseMode.JsonSchema)request.Schema=new JObject{["type"]="object",["properties"]=new JObject{["ok"]=new JObject{["type"]="boolean"}},["required"]=new JArray("ok"),["additionalProperties"]=false};
                    using(var provider=profile.CreateProvider()){var response=await provider.CompleteAsync(request,token);return new ChatConnectionTestResult{Success=!string.IsNullOrWhiteSpace(response.Text),ResponseRequested=true,Message="Модель вернула текстовый ответ. Проверка могла использовать квоту провайдера; лимит ответа 64 токена."};}
                }
                string key=connection.ApiKeyEnvironmentVariable.Length==0?"":Environment.GetEnvironmentVariable(connection.ApiKeyEnvironmentVariable);
                if(connection.ApiKeyEnvironmentVariable.Length>0&&string.IsNullOrEmpty(key))return new ChatConnectionTestResult{Message=connection.ApiKeyEnvironmentVariable.StartsWith(SessionPrefix,StringComparison.Ordinal)?"Ключ на этот запуск Unity отсутствует. Добавьте подключение с ключом заново или используйте переменную окружения.":"Переменная окружения не задана: "+connection.ApiKeyEnvironmentVariable};
                using(var transport=new JsonTransport())
                {
                    bool ollama=connection.Protocol==ChatConnectionProtocol.Ollama;var data=await transport.SendAsync(profile.Endpoint+(ollama?"/api/tags":"/models"),null,key,5,0,token);
                    var models=(data[ollama?"models":"data"] as JArray??new JArray());bool found=models.Any(m=>(string)m[ollama?"name":"id"]==connection.Model||ollama&&(string)m["name"]==connection.Model+":latest");bool vision=false;
                    bool cloud=ollama&&connection.Model.EndsWith(":cloud",StringComparison.OrdinalIgnoreCase);
                    if(ollama&&(found||cloud))
                    {
                        try
                        {
                            var details=await transport.SendAsync(profile.Endpoint+"/api/show",new JObject{["model"]=connection.Model},"",5,0,token);var capabilities=(details["capabilities"] as JArray??new JArray()).Values<string>().ToArray();
                            if(capabilities.Length>0&&!capabilities.Contains("completion"))return new ChatConnectionTestResult{ListedModels=models.Count,Message="Модель найдена, но сервер не объявляет поддержку completion. Выберите модель для чата, а не только embeddings."};
                            if(cloud&&capabilities.Contains("completion"))found=true;
                            vision=new Uri(profile.Endpoint).IsLoopback&&capabilities.Contains("vision");
                        }
                        catch(ProviderException){}
                    }
                    return new ChatConnectionTestResult{Success=found,ListedModels=models.Count,SupportsVision=vision,Message=found?cloud?"Ollama подтвердила облачную модель через /api/show. Скачивание и генерация не запускались; доступ к ответам зависит от аккаунта и квоты.":"Модель найдена в реальном списке сервера. Генерация не запускалась.":"Сервер ответил, но ID модели не найден в списке. Проверьте ID; если /models не поддерживается, можно явно проверить ответ."};
                }
            }
            catch(ProviderException error){return new ChatConnectionTestResult{ResponseRequested=explicitResponse,Message=ChatHistoryStore.Redact(error.Message)+" Для нестандартного API может потребоваться другой URL или формат ответа."};}
            finally{UnityEngine.Object.DestroyImmediate(profile);}
        }
    }

    public sealed partial class ChatController
    {
        ChatModelConnections connectionStore;
        public IReadOnlyList<ChatModelConnection> ModelConnections=>connectionStore.Connections;
        public string ModelConnectionsError=>connectionStore.LoadError;
        public ChatModelConnection AddModelConnection(ChatModelConnection connection,string sessionKey=null)
        {
            if(HasPendingWork)throw new InvalidOperationException("Дождитесь завершения текущей задачи.");var saved=connectionStore.Add(connection,sessionKey);models.Add(ChatModelConnections.AsModel(saved));Settings.ModelKey=saved.ModelKey;SaveSettings();Phase="API добавлен. Проверьте соединение или отправьте сообщение.";Notify();return saved;
        }
        public bool RemoveModelConnection(string id)
        {
            if(HasPendingWork)throw new InvalidOperationException("Дождитесь завершения текущей задачи.");if(!connectionStore.Remove(id))return false;models.RemoveAll(m=>m.ConnectionId==id);
            foreach(var session in sessions)if(session.Settings.ModelKey=="connection:"+id){session.Settings.ModelKey="ollama:qwen3.5:9b";store.Save(session);}Notify();return true;
        }
        public async Task<ChatConnectionTestResult> TestModelConnectionAsync(string id,bool explicitResponse=false,CancellationToken token=default)
        {
            var result=await connectionStore.TestAsync(id,explicitResponse,token);var model=models.FirstOrDefault(m=>m.ConnectionId==id);if(model!=null){model.Available=result.Success;if(!result.ResponseRequested)model.SupportsVision=result.SupportsVision;model.Description=result.Message;}if(!Busy)Phase=result.Message;Notify();return result;
        }
        async Task RefreshCustomModelsAsync(List<AvailableModel> found,CancellationToken token=default)
        {
            foreach(var connection in connectionStore.Connections)
            {
                token.ThrowIfCancellationRequested();if(disposed)return;
                var model=ChatModelConnections.AsModel(connection);ChatConnectionTestResult tested;
                try{tested=await connectionStore.TestAsync(connection.Id,false,token);}catch(ArgumentException error){if(!connectionStore.Connections.Any(c=>c.Id==connection.Id))continue;tested=new ChatConnectionTestResult{Message=ChatHistoryStore.Redact(error.Message)};}
                model.Available=tested.Success;model.SupportsVision=tested.SupportsVision;model.Description=tested.Message;found.Add(model);
            }
            var current=connectionStore.Connections.Select(c=>c.Id).ToArray();found.RemoveAll(m=>!string.IsNullOrEmpty(m.ConnectionId)&&!current.Contains(m.ConnectionId));
        }
        public async Task RefreshModelConnectionsAsync()
        {
            if(disposed)return;var found=new List<AvailableModel>();await RefreshCustomModelsAsync(found);if(disposed)return;models.RemoveAll(m=>!string.IsNullOrEmpty(m.ConnectionId));models.AddRange(found);Notify();
        }
    }
}
