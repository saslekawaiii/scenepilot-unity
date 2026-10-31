using System;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Explicit user-owned API configuration. Password field is never serialized by Unity.</summary>
    public sealed class ChatModelConnectionWindow : EditorWindow
    {
        [NonSerialized] ChatController controller;
        [NonSerialized] string sessionKey="";
        [NonSerialized] CancellationTokenSource testing;
        string displayName="Мой API",endpoint="https://api.example.com/v1",modelId="",environmentVariable="",selectedId="",status="";
        ChatConnectionProtocol protocol=ChatConnectionProtocol.OpenAICompatible;
        OpenAIResponseMode responseMode=OpenAIResponseMode.JsonObject;
        Vector2 scroll;
        public static void Open(ChatController controller)
        {
            var window=GetWindow<ChatModelConnectionWindow>(true,"Подключить модель",true);window.controller=controller;window.minSize=new Vector2(500,610);window.ShowUtility();window.Focus();
        }
        void OnDisable(){testing?.Cancel();testing?.Dispose();testing=null;sessionKey="";}
        void OnGUI()
        {
            if(controller==null){EditorGUILayout.HelpBox("Откройте это окно кнопкой «Добавить API…» в чате WorldWeaver.",MessageType.Info);return;}
            scroll=EditorGUILayout.BeginScrollView(scroll);EditorGUILayout.LabelField("Своя модель в том же чате",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Поддерживаются Ollama и OpenAI-compatible Chat Completions. Форматы Anthropic Messages и Gemini здесь не подключаются напрямую. Облачные подключения сейчас работают только с текстом.",MessageType.Info);
            using(new EditorGUI.DisabledScope(testing!=null||controller.HasPendingWork))
            {
                EditorGUILayout.Space(6);protocol=(ChatConnectionProtocol)EditorGUILayout.EnumPopup("Тип API",protocol);
                displayName=EditorGUILayout.TextField("Название в списке",displayName);
                endpoint=EditorGUILayout.TextField(protocol==ChatConnectionProtocol.Ollama?"URL сервера Ollama":"URL API",endpoint);
                EditorGUILayout.LabelField(protocol==ChatConnectionProtocol.Ollama?"Например http://127.0.0.1:11434 или HTTPS proxy path":"Например https://api.example.com/v1; root URL добавит /v1",EditorStyles.wordWrappedMiniLabel);
                modelId=EditorGUILayout.TextField("Точный ID модели",modelId);
                if(protocol==ChatConnectionProtocol.OpenAICompatible)
                {
                    responseMode=(OpenAIResponseMode)EditorGUILayout.EnumPopup("Формат ответа",responseMode);
                    EditorGUILayout.LabelField("JsonSchema — строгая схема, если сервер её поддерживает. JsonObject — JSON mode. Text — без response_format, ответ всё равно проверяется парсером WorldWeaver.",EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.Space(8);EditorGUILayout.LabelField("Доступ",EditorStyles.boldLabel);
                    sessionKey=EditorGUILayout.PasswordField("Ключ API",sessionKey??"");
                    environmentVariable=EditorGUILayout.TextField("Имя переменной",environmentVariable);
                    EditorGUILayout.HelpBox("Сам ключ хранится только в окружении процесса Unity: переживает перекомпиляцию, исчезает после закрытия Unity. На диск и в историю он не записывается. Для постоянного доступа заранее задайте переменную окружения и укажите только её имя. Для API без авторизации оставьте оба поля пустыми.",MessageType.None);
                }
                else EditorGUILayout.HelpBox("Этот адаптер Ollama работает без API-ключа. Vision включается только для локального сервера после проверки /api/show.",MessageType.None);
                if(GUILayout.Button("Добавить и выбрать модель",GUILayout.Height(30)))
                {
                    try
                    {
                        var value=new ChatModelConnection{Name=displayName,Endpoint=endpoint,Model=modelId,Protocol=protocol,ResponseMode=responseMode,ApiKeyEnvironmentVariable=protocol==ChatConnectionProtocol.Ollama?"":environmentVariable};
                        var added=controller.AddModelConnection(value,protocol==ChatConnectionProtocol.Ollama?null:sessionKey);selectedId=added.Id;sessionKey="";status="Подключение добавлено и выбрано в чате. Проверка соединения не запускает генерацию.";
                    }
                    catch(Exception error){status=ChatHistoryStore.Redact(error.Message);}
                }
            }
            EditorGUILayout.Space(12);var connections=controller.ModelConnections;
            if(!string.IsNullOrEmpty(controller.ModelConnectionsError))EditorGUILayout.HelpBox(controller.ModelConnectionsError,MessageType.Error);
            if(connections.Count>0)
            {
                EditorGUILayout.LabelField("Сохранённые подключения",EditorStyles.boldLabel);int selected=Math.Max(0,connections.ToList().FindIndex(c=>c.Id==selectedId));selected=EditorGUILayout.Popup("Подключение",selected,connections.Select(c=>c.Name+" · "+c.Model).ToArray());selectedId=connections[selected].Id;
                using(new EditorGUI.DisabledScope(testing!=null||controller.HasPendingWork))
                {
                    EditorGUILayout.BeginHorizontal();if(GUILayout.Button("Проверить соединение"))Test(false);if(GUILayout.Button("Проверить ответ"))Test(true);EditorGUILayout.EndHorizontal();
                    EditorGUILayout.LabelField("Соединение: список и свойства моделей, без генерации. Ответ: явный запрос до 64 выходных токенов, возможен расход квоты API.",EditorStyles.wordWrappedMiniLabel);
                    if(GUILayout.Button("Удалить подключение")){try{controller.RemoveModelConnection(selectedId);selectedId="";status="Подключение удалено. Внешняя переменная окружения не изменена.";}catch(Exception error){status=ChatHistoryStore.Redact(error.Message);}}
                }
            }
            if(testing!=null&&GUILayout.Button("Отменить проверку"))testing.Cancel();
            if(!string.IsNullOrEmpty(status))EditorGUILayout.HelpBox(status,testing!=null?MessageType.Info:MessageType.None);
            EditorGUILayout.EndScrollView();
        }
        async void Test(bool response)
        {
            if(testing!=null)return;testing=new CancellationTokenSource();status=response?"Проверяю ответ выбранной модели…":"Читаю реальный список моделей сервера…";Repaint();
            try{var result=await controller.TestModelConnectionAsync(selectedId,response,testing.Token);status=result.Message;}
            catch(OperationCanceledException){status="Проверка отменена.";}
            catch(Exception error){status=ChatHistoryStore.Redact(error.Message);}
            finally{testing?.Dispose();testing=null;if(this)Repaint();}
        }
    }
}
